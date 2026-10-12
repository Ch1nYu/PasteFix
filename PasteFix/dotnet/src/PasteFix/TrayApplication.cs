using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteFix;

public sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly MessageWindow _window = new();
    private readonly Settings _settings;
    private readonly ToolStripMenuItem _autoMonitorItem;
    private readonly ToolStripMenuItem _smartModeItem;
    private readonly ToolStripMenuItem _singleLineModeItem;
    private readonly OwnWriteGate _ownWriteGate = new();

    public TrayApplication()
    {
        _settings = Settings.Load();

        _autoMonitorItem = new ToolStripMenuItem("全自動監聽", null, OnAutoMonitorToggle)
        {
            CheckOnClick = true,
            Checked = _settings.AutoMonitor,
        };

        _smartModeItem = new ToolStripMenuItem(
            "智慧續行（現有）",
            null,
            (_, _) => SetProcessingMode(ProcessingMode.SmartContinuation));
        _singleLineModeItem = new ToolStripMenuItem(
            "強制單行",
            null,
            (_, _) => SetProcessingMode(ProcessingMode.SingleLine));

        var processingModeItem = new ToolStripMenuItem("處理模式");
        processingModeItem.DropDownItems.Add(_smartModeItem);
        processingModeItem.DropDownItems.Add(_singleLineModeItem);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoMonitorItem);
        menu.Items.Add(processingModeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Application.Exit()));

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Pastefix — 清洗 AI 產生的終端指令",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => CleanClipboard();

        _window.ClipboardChanged += OnClipboardChanged;

        ApplySettings();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            using Stream? s = typeof(TrayApplication).Assembly
                .GetManifestResourceStream("PasteFix.app.ico");
            if (s is null)
            {
                Program.Log("tray icon resource missing, using system icon");
                return SystemIcons.Application;
            }

            using var full = new Icon(s);
            return new Icon(full, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Program.Log("tray icon load failed: " + ex);
            return SystemIcons.Application;
        }
    }

    private System.Windows.Forms.Timer? _clipDebounce;

    private void OnClipboardChanged()
    {
        if (_clipDebounce == null)
        {
            _clipDebounce = new System.Windows.Forms.Timer { Interval = 300 };
            _clipDebounce.Tick += (_, _) =>
            {
                _clipDebounce.Stop();
                if (!_settings.AutoMonitor)
                {
                    Trace("  AutoMonitor disabled before debounce completed");
                    return;
                }
                if (_ownWriteGate.IsOwnWrite(GetClipboardSequenceNumber()))
                {
                    Trace("  clipboard sequence is our own write-back, monitor skips");
                    return;
                }
                CleanClipboard(showNotification: false);
            };
        }

        _clipDebounce.Stop();
        _clipDebounce.Start();
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private void ApplySettings()
    {
        _window.SetClipboardListening(_settings.AutoMonitor);
        SyncProcessingModeChecks();
    }

    private void OnAutoMonitorToggle(object? sender, EventArgs e)
    {
        _settings.AutoMonitor = _autoMonitorItem.Checked;
        if (!_settings.AutoMonitor) _clipDebounce?.Stop();
        _window.SetClipboardListening(_settings.AutoMonitor);
        _settings.Save();
        Program.Log($"AutoMonitor -> {_settings.AutoMonitor} (saved to {Settings.FilePath})");
    }

    private void SetProcessingMode(ProcessingMode mode)
    {
        _settings.Mode = mode;
        SyncProcessingModeChecks();
        _settings.Save();
        Program.Log($"ProcessingMode -> {_settings.Mode} (saved to {Settings.FilePath})");
    }

    private void SyncProcessingModeChecks()
    {
        _smartModeItem.Checked = _settings.Mode == ProcessingMode.SmartContinuation;
        _singleLineModeItem.Checked = _settings.Mode == ProcessingMode.SingleLine;
    }

    private bool CleanClipboard(bool showNotification = true)
    {
        Trace("CleanClipboard enter");
        if (!Clipboard.ContainsText())
        {
            Trace("  no text on clipboard");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿中沒有文字", ToolTipIcon.Info);
            return false;
        }

        string text = Clipboard.GetText();
        string cleaned = ClipboardTransformer.Transform(text, _settings.Mode);
        Trace($"  cleaned=[{cleaned}]");

        switch (ClipboardWriteback.Decide(text, cleaned))
        {
            case ClipboardWriteDecision.NothingToDo:
                Trace("  no change needed (idempotent)");
                if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "沒有需要清洗的內容", ToolTipIcon.Info);
                return false;

            case ClipboardWriteDecision.EmptyResult:
                // The copy held nothing but whitespace, so cleaning leaves no
                // text at all. SetText rejects that, and an empty result is not
                // a cleaning anyway — the clipboard keeps what was copied.
                Program.Log($"clean produced empty text ({ClipboardSummary.Describe(text)}), clipboard left as copied");
                Trace("  empty result, clipboard left untouched");
                if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "清洗後沒有內容，剪貼簿保持原樣", ToolTipIcon.Info);
                return false;
        }

        try
        {
            Clipboard.SetText(cleaned);
        }
        catch (ExternalException ex)
        {
            // Another process holds the clipboard open. SetText already retried
            // ten times over a second before throwing, so retrying again here
            // would only repeat the same wait. Left unhandled this reaches
            // Application.ThreadException, which logs it and keeps the tray
            // alive — so the paste simply goes out uncleaned with no sign of
            // why. Other exception types are still left to that handler, so a
            // genuine bug stays visible instead of being blamed on contention.
            Program.Log($"clipboard write failed ({ClipboardSummary.Describe(cleaned)}, 0x{ex.HResult:X8}), left uncleaned");
            Trace($"  write failed 0x{ex.HResult:X8}");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿被其他程式佔用，這次沒有清洗", ToolTipIcon.Warning);
            return false;
        }

        _ownWriteGate.MarkOwnWrite(GetClipboardSequenceNumber());
        Trace($"  wrote back [{cleaned}]");

        if (showNotification)
            _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿已清洗，可以直接貼上", ToolTipIcon.Info);
        return true;
    }

    private static void Trace(string message)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("PASTEFIX_TRACE") == "1")
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "pastefix-trace.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _clipDebounce?.Dispose();
        _window.ReleaseHandle();
        base.ExitThreadCore();
    }
}

