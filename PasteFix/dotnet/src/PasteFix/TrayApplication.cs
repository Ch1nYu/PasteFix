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
    private readonly OwnWriteGate _ownWriteGate = new();

    public TrayApplication()
    {
        _settings = Settings.Load();

        _autoMonitorItem = new ToolStripMenuItem("全自動監聽", null, OnAutoMonitorToggle)
        {
            CheckOnClick = true,
            Checked = _settings.AutoMonitor,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoMonitorItem);
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
    }

    private void OnAutoMonitorToggle(object? sender, EventArgs e)
    {
        _settings.AutoMonitor = _autoMonitorItem.Checked;
        _window.SetClipboardListening(_settings.AutoMonitor);
        _settings.Save();
        Program.Log($"AutoMonitor -> {_settings.AutoMonitor} (saved to {Settings.FilePath})");
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
        string cleaned = Normalizer.Normalize(text);
        Trace($"  cleaned=[{cleaned}]");
        if (cleaned == text)
        {
            Trace("  no change needed (idempotent)");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "沒有需要清洗的內容", ToolTipIcon.Info);
            return false;
        }

        Clipboard.SetText(cleaned);
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

