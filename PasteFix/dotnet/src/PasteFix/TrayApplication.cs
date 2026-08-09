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
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard(force: true)));
        menu.Items.Add(new ToolStripMenuItem(
            "立即清洗（第一行沒縮排）", null,
            (_, _) => CleanClipboard(force: true, ignoreFirstLineIndent: true))
        {
            ToolTipText =
                "滑鼠是從第一行的文字起點拖曳、漏掉左邊界時用這個。\n"
                + "它會改掉第一行與其餘各行的相對縮排，Python 這類靠縮排的內容會被弄壞。",
        });
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
        _icon.DoubleClick += (_, _) => CleanClipboard(force: true);

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
        Program.Log($"HotkeyPressed fired");
        IntPtr fgBoundary = GetForegroundWindow();
        Program.Log(
            $"  foreground hwnd=0x{fgBoundary.ToInt64():X} class=[{GetClassName(fgBoundary)}] title=[{GetWindowTitle(fgBoundary)}]");
        bool hasText = Clipboard.ContainsText();
        Program.Log($"  clipboard hasText={hasText}");
        bool changed = false;
        if (hasText)
        {
            string raw = Clipboard.GetText();
            Program.Log($"  clipboard text=[{Shorten(raw)}]");
            changed = CleanClipboard(showNotification: false, force: true);
            Program.Log($"  CleanClipboard changed={changed}");
        }
        else
        {
            Program.Log("  clipboard empty, skipping clean");
        }
        SendPaste();
        string message = !hasText ? "剪貼簿中沒有文字" : changed ? "已清洗並貼上" : "內容已乾淨，已貼上";
        _icon.ShowBalloonTip(1200, "Pastefix", message, ToolTipIcon.Info);
    }

    private static string Shorten(string s) => s.Length > 100 ? s.Substring(0, 100) + "…" : s;

    private static string GetClassName(IntPtr hwnd) { try { var sb = new System.Text.StringBuilder(256); GetClassName(hwnd, sb, sb.Capacity); return sb.ToString(); } catch { return "?"; } }

    private static string GetWindowTitle(IntPtr hwnd) { try { var sb = new System.Text.StringBuilder(256); GetWindowText(hwnd, sb, sb.Capacity); return sb.ToString(); } catch { return "?"; } }

    private static void SendPaste()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        string className = GetClassName(hwnd);
        Program.Log($"  SendPaste fg class=[{className}]");

        // 現代終端機把輸入放在子表面（WezTerm 的 GL 視窗、Windows Terminal 的 XAML
        // island），送到頂層 HWND 的 WM_PASTE 會被靜默丟掉——2026-08-09 實測：剪貼簿
        // 清洗成功、貼上沒發生。這類視窗只能用它自己的貼上鍵驅動。
        switch (className)
        {
            case "org.wezfurlong.wezterm":
                SendPasteKeys(shift: true);     // WezTerm 的貼上是 Ctrl+Shift+V
                Program.Log("  paste via Ctrl+Shift+V (WezTerm)");
                break;

            case "CASCADIA_HOSTING_WINDOW_CLASS":
                SendPasteKeys(shift: false);    // Windows Terminal 的預設貼上是 Ctrl+V
                Program.Log("  paste via Ctrl+V (Windows Terminal)");
                break;

            default:
                // 一般的 Edit 控制項（記事本那類）收 WM_PASTE，而且比送鍵可靠：
                // 不受焦點、修飾鍵狀態與按鍵綁定影響。
                SendMessage(hwnd, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
                Program.Log("  paste via WM_PASTE");
                break;
        }
    }

    private static void SendPasteKeys(bool shift)
    {
        const byte vkMenu = 0x12;
        const byte vkControl = 0x11;
        const byte vkShift = 0x10;
        const byte vkV = 0x56;
        const uint keyUp = 0x0002;

        // 先釋放實體 Alt：熱鍵是 Alt+V，RegisterHotKey 不會幫忙放開，
        // 而卡住的 Alt 會把接下來的 Ctrl+V 變成選單快捷鍵。
        keybd_event(vkMenu, 0, keyUp, UIntPtr.Zero);

        keybd_event(vkControl, 0, 0, UIntPtr.Zero);
        if (shift) keybd_event(vkShift, 0, 0, UIntPtr.Zero);
        keybd_event(vkV, 0, 0, UIntPtr.Zero);
        keybd_event(vkV, 0, keyUp, UIntPtr.Zero);
        if (shift) keybd_event(vkShift, 0, keyUp, UIntPtr.Zero);
        keybd_event(vkControl, 0, keyUp, UIntPtr.Zero);
    }

    private const uint WM_PASTE = 0x0302;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

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

    /// <param name="force">
    /// True for the paths the user triggers on purpose (Alt+V, the tray menu,
    /// double-click): those may also join continuation lines, which rewrites the
    /// text. AutoMonitor passes false and only dedents, which preserves structure
    /// — so it needs no guess about whether the clipboard "looks like a command".
    /// </param>
    /// <param name="ignoreFirstLineIndent">
    /// Only ever true for the tray item that says so. It rewrites the indent of
    /// line 1 relative to the rest, which is right when the selection missed the
    /// left margin and wrong for everything else — the user is the only one who
    /// knows which.
    /// </param>
    private bool CleanClipboard(
        bool showNotification = true, bool force = false, bool ignoreFirstLineIndent = false)
    {
        Trace("CleanClipboard enter");
        if (!Clipboard.ContainsText())
        {
            Trace("  no text on clipboard");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿中沒有文字", ToolTipIcon.Info);
            return false;
        }

        string text = Clipboard.GetText();
        string cleaned = force
            ? Normalizer.Normalize(text, ignoreFirstLineIndent)
            : Normalizer.Dedent(text);
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