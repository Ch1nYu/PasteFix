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
    private readonly OwnWriteGate _ownWriteGate = new();

    public TrayApplication()
    {
        _settings = Settings.Load();

        _autoMonitorItem = new ToolStripMenuItem("全自動監聽", null, OnAutoMonitorToggle)
        {
            CheckOnClick = true,
            Checked = _settings.AutoMonitor,
        };
        _hotkeyItem = new ToolStripMenuItem("熱鍵 Alt+V", null, OnHotkeyToggle)
        {
            CheckOnClick = true,
            Checked = _settings.HotkeyEnabled,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoMonitorItem);
        menu.Items.Add(_hotkeyItem);
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
        _window.HotkeyPressed += OnHotkeyPressed;

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

    private void OnHotkeyPressed()
    {
        Program.Log("HotkeyPressed fired");
        IntPtr fgBoundary = GetForegroundWindow();
        string className = GetClassName(fgBoundary);
        Program.Log(
            $"  foreground hwnd=0x{fgBoundary.ToInt64():X} class=[{className}] title=[{GetWindowTitle(fgBoundary)}]");
        bool hasText = Clipboard.ContainsText();
        Program.Log($"  clipboard hasText={hasText}");
        bool changed = false;
        if (hasText)
        {
            string raw = Clipboard.GetText();
            Program.Log($"  clipboard {ClipboardSummary.Describe(raw)}");
            changed = CleanClipboard(showNotification: false);
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

    private static string GetClassName(IntPtr hwnd) { try { var sb = new System.Text.StringBuilder(256); GetClassName(hwnd, sb, sb.Capacity); return sb.ToString(); } catch { return "?"; } }

    private static string GetWindowTitle(IntPtr hwnd) { try { var sb = new System.Text.StringBuilder(256); GetWindowText(hwnd, sb, sb.Capacity); return sb.ToString(); } catch { return "?"; } }

    private void SendPaste()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        string className = GetClassName(hwnd);

        var channel = PasteChannelSelector.Select(className, _settings.PasteChannels);
        bool mapped = PasteChannelSelector.IsMapped(className, _settings.PasteChannels);
        Program.Log(mapped
            ? $"  SendPaste fg class=[{className}] channel={channel}"
            : $"  SendPaste fg class=[{className}] channel={channel} (default; override via PasteChannels in {Settings.FilePath})");

        switch (channel)
        {
            case PasteChannel.CtrlShiftV:
                SendPasteKeys(shift: true);
                break;

            case PasteChannel.CtrlV:
                SendPasteKeys(shift: false);
                break;

            default:
                SendMessage(hwnd, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
                break;
        }
    }

    private static void SendPasteKeys(bool shift)
    {
        const byte vkMenu = 0x12;
        const byte vkControl = 0x11;
        const byte vkShift = 0x10;
        const byte vkV = 0x56;

        List<NativeInput> inputs = new() { Key(vkMenu, up: true), Key(vkControl) };
        if (shift) inputs.Add(Key(vkShift));
        inputs.Add(Key(vkV));
        inputs.Add(Key(vkV, up: true));
        if (shift) inputs.Add(Key(vkShift, up: true));
        inputs.Add(Key(vkControl, up: true));

        SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            System.Runtime.InteropServices.Marshal.SizeOf<NativeInput>());
    }

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;

    private static NativeInput Key(ushort vk, bool up = false) => new()
    {
        Type = InputKeyboard,
        U = new InputUnion { Ki = new KeybdInput { Vk = vk, Flags = up ? KeyeventfKeyUp : 0 } },
    };

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct InputUnion
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public KeybdInput Ki;
        [System.Runtime.InteropServices.FieldOffset(0)] public MouseInput Mi;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion U;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    private const uint WM_PASTE = 0x0302;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

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
        Program.Log($"AutoMonitor -> {_settings.AutoMonitor} (saved to {Settings.FilePath})");
    }

    private void OnHotkeyToggle(object? sender, EventArgs e)
    {
        _settings.HotkeyEnabled = _hotkeyItem.Checked;
        _window.SetHotkeyListening(_settings.HotkeyEnabled);
        _settings.Save();
        Program.Log($"HotkeyEnabled -> {_settings.HotkeyEnabled} (saved to {Settings.FilePath})");
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

