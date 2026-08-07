using System;
using System.Drawing;
using System.Windows.Forms;

namespace PasteFix;

public sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly MessageWindow _window = new();
    private readonly Settings _settings;
    private readonly ToolStripMenuItem _autoMonitorItem;
    private readonly ToolStripMenuItem _hotkeyItem;

    public TrayApplication()
    {
        _settings = Settings.Load();

        _autoMonitorItem = new ToolStripMenuItem("全自動監聽", null, OnAutoMonitorToggle) { Checked = _settings.AutoMonitor };
        _hotkeyItem = new ToolStripMenuItem("熱鍵 Alt+V", null, OnHotkeyToggle) { Checked = _settings.HotkeyEnabled };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoMonitorItem);
        menu.Items.Add(_hotkeyItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Application.Exit()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Pastefix — 清洗 AI 產生的終端指令",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => CleanClipboard();

        _window.ClipboardChanged += OnClipboardChanged;
        _window.HotkeyPressed += OnHotkeyPressed;

        ApplySettings();
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
                CleanClipboard(showNotification: false);
            };
        }

        _clipDebounce.Stop();
        _clipDebounce.Start();
    }

    private void OnHotkeyPressed()
    {
        bool changed = CleanClipboard(showNotification: false);
        SendPaste();
        _icon.ShowBalloonTip(
            1200,
            "Pastefix",
            changed ? "已清洗並貼上" : "內容已乾淨，已貼上",
            ToolTipIcon.Info);
    }

    private static void SendPaste()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        SendMessage(hwnd, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
    }

    private const uint WM_PASTE = 0x0302;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private void ApplySettings()
    {
        _window.SetClipboardListening(_settings.AutoMonitor);
        _window.SetHotkeyListening(_settings.HotkeyEnabled);
    }

    private void OnAutoMonitorToggle(object? sender, EventArgs e)
    {
        _settings.AutoMonitor = _autoMonitorItem.Checked;
        _window.SetClipboardListening(_settings.AutoMonitor);
        _settings.Save();
    }

    private void OnHotkeyToggle(object? sender, EventArgs e)
    {
        _settings.HotkeyEnabled = _hotkeyItem.Checked;
        _window.SetHotkeyListening(_settings.HotkeyEnabled);
        _settings.Save();
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
        Trace($"  text=[{text}]");
        string cleaned = Normalizer.Normalize(text);
        Trace($"  cleaned=[{cleaned}]");
        if (cleaned == text)
        {
            Trace("  no change needed (idempotent)");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "沒有需要清洗的內容", ToolTipIcon.Info);
            return false;
        }

        Clipboard.SetText(cleaned);
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