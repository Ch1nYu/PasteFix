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
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly Settings _settings;
    private readonly ToolStripMenuItem _autoMonitorItem;
    private readonly ToolStripMenuItem _hotkeyItem;
    private readonly ToolStripMenuItem _restoreItem;
    private readonly OwnWriteGate _ownWriteGate = new();
    private readonly LastClean _lastClean = new();

    public TrayApplication()
    {
        _settings = Settings.Load();

        _autoMonitorItem = new ToolStripMenuItem("全自動監聽", null, OnAutoMonitorToggle)
        {
            CheckOnClick = true,
            Checked = _settings.AutoMonitor,
        };

        _hotkeyItem = new ToolStripMenuItem("全域熱鍵（Ctrl+Alt+Shift+V）", null, OnHotkeyToggle)
        {
            CheckOnClick = true,
            Checked = _settings.HotkeyEnabled,
        };

        _restoreItem = new ToolStripMenuItem("還原上一次清洗", null, (_, _) => RestoreLastClean());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard()));
        // Single Line used to be a mode that stayed on. It was switched 32 times
        // in a month, often back within a minute, and ran for 13 of 1025 copies:
        // what was wanted was this copy flattened, not every copy after it.
        menu.Items.Add(new ToolStripMenuItem("壓成單行（這一次）", null, (_, _) => CleanClipboard(ProcessingMode.SingleLine)));
        menu.Items.Add(_restoreItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoMonitorItem);
        menu.Items.Add(_hotkeyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Application.Exit()));
        menu.Opening += (_, _) => _restoreItem.Enabled = _lastClean.CanRestore;

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Pastefix — 清洗 AI 產生的終端指令",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => CleanClipboard();

        _window.ClipboardChanged += OnClipboardChanged;
        _hotkeyWindow.Pressed += () => CleanClipboard();

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

                string? owner = ClipboardWriter.ProcessName();
                MonitorSkip skip = MonitorFilter.Decide(owner, ClipboardWriter.AskedToBeIgnored(), _settings.IgnoredProcesses);
                if (skip != MonitorSkip.None)
                {
                    Program.Log($"monitor skipped: owner={owner ?? "unknown"} reason={skip}");
                    return;
                }
                CleanClipboard(showNotification: false, owner: owner ?? "unknown");
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

        _hotkeyWindow.SetHotkey(_settings.HotkeyEnabled);
        if (_settings.HotkeyEnabled && !_hotkeyWindow.Registered)
        {
            // Another program holds the combination. Show it as off rather than
            // pretending the key works; the setting stays as chosen so the next
            // start can claim it if the other program is gone.
            _hotkeyItem.Checked = false;
            Program.Log("hotkey unavailable, menu item shown unchecked");
        }
    }

    private void OnAutoMonitorToggle(object? sender, EventArgs e)
    {
        _settings.AutoMonitor = _autoMonitorItem.Checked;
        if (!_settings.AutoMonitor) _clipDebounce?.Stop();
        _window.SetClipboardListening(_settings.AutoMonitor);
        _settings.Save();
        Program.Log($"AutoMonitor -> {_settings.AutoMonitor} (saved to {Settings.FilePath})");
    }

    private void OnHotkeyToggle(object? sender, EventArgs e)
    {
        _settings.HotkeyEnabled = _hotkeyItem.Checked;
        _hotkeyWindow.SetHotkey(_settings.HotkeyEnabled);
        if (_settings.HotkeyEnabled && !_hotkeyWindow.Registered)
        {
            _hotkeyItem.Checked = false;
            _settings.HotkeyEnabled = false;
            _icon.ShowBalloonTip(1500, "Pastefix", "熱鍵已被其他程式占用", ToolTipIcon.Warning);
        }
        _settings.Save();
        Program.Log($"Hotkey -> {_settings.HotkeyEnabled} (saved to {Settings.FilePath})");
    }

    /// <param name="owner">
    /// The writer's process name, for the monitor's log lines. It is how a
    /// program that borrows the clipboard gets found and added to
    /// IgnoredProcesses. Manual cleans pass none.
    /// </param>
    private bool CleanClipboard(ProcessingMode mode = ProcessingMode.SmartContinuation, bool showNotification = true, string? owner = null)
    {
        Trace("CleanClipboard enter");
        if (!Clipboard.ContainsText())
        {
            Trace("  no text on clipboard");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿中沒有文字", ToolTipIcon.Info);
            return false;
        }

        string text = Clipboard.GetText();
        string source = _lastClean.SourceFor(text);
        ClipboardResult result = ClipboardTransformer.Transform(source, mode);
        string cleaned = result.Text;
        Trace($"  cleaned=[{cleaned}]");

        switch (ClipboardWriteback.Decide(text, cleaned))
        {
            case ClipboardWriteDecision.NothingToDo:
                // Nothing changed, so this copy wanted no cleaning at all. It is
                // still the most useful line in the log for the mode question:
                // it is the control group, the copy that proves the other mode
                // would have had nothing to do either.
                Program.Log($"clean skipped, no change needed: {ClipboardSummary.Describe(text, mode)}{OwnerField(owner)}");
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
            Program.Log($"clipboard write failed ({ClipboardSummary.Describe(cleaned, mode)}, 0x{ex.HResult:X8}), left uncleaned");
            Trace($"  write failed 0x{ex.HResult:X8}");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿被其他程式佔用，這次沒有清洗", ToolTipIcon.Warning);
            return false;
        }

        _ownWriteGate.MarkOwnWrite(GetClipboardSequenceNumber());
        _lastClean.Record(source, cleaned);
        // The permanent record of a clean that worked. Until this existed only
        // failures were ever written here, so a working tool left no trace.
        Program.Log($"cleaned: {ClipboardSummary.Describe(source, result, mode)}{OwnerField(owner)}");
        Trace($"  wrote back [{cleaned}]");

        if (showNotification)
            _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿已清洗，可以直接貼上", ToolTipIcon.Info);
        return true;
    }

    private static string OwnerField(string? owner) => owner is null ? "" : $" owner={owner}";

    private void RestoreLastClean()
    {
        if (!_lastClean.CanRestore)
        {
            _icon.ShowBalloonTip(1200, "Pastefix", "沒有可還原的清洗", ToolTipIcon.Info);
            return;
        }

        string? current = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        if (!_lastClean.StillOnClipboard(current))
        {
            // Something was copied after the clean. Putting the old text back
            // would destroy that copy, so the restore is dropped instead.
            _lastClean.Clear();
            Program.Log("restore skipped, clipboard moved on since the clean");
            _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿已換過內容，無法還原", ToolTipIcon.Warning);
            return;
        }

        string? before = _lastClean.Before;
        try
        {
            Clipboard.SetText(before!);
        }
        catch (ExternalException ex)
        {
            // The clean is kept: the clipboard was busy for a moment, and that
            // should not cost the user the only way back.
            Program.Log($"restore failed (0x{ex.HResult:X8})");
            _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿被其他程式佔用，無法還原", ToolTipIcon.Warning);
            return;
        }

        _lastClean.Clear();
        _ownWriteGate.MarkOwnWrite(GetClipboardSequenceNumber());
        Program.Log($"restored previous clipboard ({ClipboardSummary.Describe(before)})");
        _icon.ShowBalloonTip(1200, "Pastefix", "已還原上一次複製的內容", ToolTipIcon.Info);
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
        _hotkeyWindow.DestroyHandle();
        _window.ReleaseHandle();
        base.ExitThreadCore();
    }
}
