using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PasteFix;

public sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly MessageWindow _window = new();
    private readonly Settings _settings;
    private readonly ToolStripMenuItem _autoMonitorItem;
    private readonly ToolStripMenuItem _hotkeyItem;
    private readonly ToolStripMenuItem _ctrlVItem;
    private readonly OwnWriteGate _ownWriteGate = new();

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _hookId = IntPtr.Zero;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
        _ctrlVItem = new ToolStripMenuItem("終端機 Ctrl+V 防倒反平滑貼上", null, OnCtrlVToggle)
        {
            CheckOnClick = true,
            Checked = _settings.InterceptTerminalCtrlV,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即清洗", null, (_, _) => CleanClipboard(force: true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoMonitorItem);
        menu.Items.Add(_hotkeyItem);
        menu.Items.Add(_ctrlVItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => Application.Exit()));

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Pastefix — 清洗 AI 產生的終端指令",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => CleanClipboard(force: true);

        _window.ClipboardChanged += OnClipboardChanged;
        _window.HotkeyPressed += OnHotkeyPressed;
        _window.TerminalCtrlVPressed += OnTerminalCtrlV;

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

    private static bool IsTerminalWindow(string className)
    {
        return className is "org.wezfurlong.wezterm"
            or "CASCADIA_HOSTING_WINDOW_CLASS"
            or "ConsoleWindowClass"
            or "mintty"
            or "VirtualConsoleClass";
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (kb.vkCode == 0x56) // 'V'
            {
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

                if (ctrl && !alt && !win && _settings.InterceptTerminalCtrlV)
                {
                    IntPtr fg = GetForegroundWindow();
                    string cls = GetClassName(fg);
                    if (IsTerminalWindow(cls))
                    {
                        Program.Log($"Intercepted Ctrl+V for terminal window class=[{cls}]");
                        PostMessage(_window.Handle, MessageWindow.WmTerminalCtrlV, IntPtr.Zero, IntPtr.Zero);
                        return (IntPtr)1; // Swallow raw Ctrl+V
                    }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void OnTerminalCtrlV()
    {
        PerformPasteFlow(force: false, isTerminal: true);
    }

    private void OnHotkeyPressed()
    {
        IntPtr fg = GetForegroundWindow();
        string cls = GetClassName(fg);
        PerformPasteFlow(force: true, isTerminal: IsTerminalWindow(cls));
    }

    private void PerformPasteFlow(bool force, bool isTerminal)
    {
        Program.Log($"PerformPasteFlow force={force} isTerminal={isTerminal}");
        IntPtr fgBoundary = GetForegroundWindow();
        string className = GetClassName(fgBoundary);
        Program.Log(
            $"  foreground hwnd=0x{fgBoundary.ToInt64():X} class=[{className}] title=[{GetWindowTitle(fgBoundary)}]");

        bool hasText = Clipboard.ContainsText();
        Program.Log($"  clipboard hasText={hasText}");
        bool changed = false;
        string textToPaste = "";

        if (hasText)
        {
            string raw = Clipboard.GetText();
            Program.Log($"  clipboard {ClipboardSummary.Describe(raw)}");
            textToPaste = force ? Normalizer.Normalize(raw) : Normalizer.Dedent(raw);

            if (textToPaste != raw)
            {
                Clipboard.SetText(textToPaste);
                _ownWriteGate.MarkOwnWrite(GetClipboardSequenceNumber());
                changed = true;
                Program.Log($"  CleanClipboard wrote cleaned text back");
            }
        }

        if (!hasText || string.IsNullOrEmpty(textToPaste))
        {
            Program.Log("  clipboard empty, skipping paste");
            _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿中沒有文字", ToolTipIcon.Info);
            return;
        }

        (List<string> lines, string _) = Normalizer.SplitLines(textToPaste);

        if (isTerminal && lines.Count > 1)
        {
            Program.Log($"  Paced sending {lines.Count} lines to terminal (delay={_settings.LineDelayMs}ms)");
            Task.Run(() => SendPacedLines(lines, _settings.LineDelayMs));
        }
        else
        {
            SendSinglePaste(className);
        }

        string message = changed ? "已清洗並平滑貼上" : "內容已乾淨，已平滑貼上";
        _icon.ShowBalloonTip(1200, "Pastefix", message, ToolTipIcon.Info);
    }

    private static void SendPacedLines(List<string> lines, int delayMs)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            SendUnicodeString(lines[i]);
            if (i < lines.Count - 1)
            {
                SendEnter();
                System.Threading.Thread.Sleep(Math.Max(10, delayMs));
            }
        }
    }

    private static void SendUnicodeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        List<NativeInput> inputs = new(text.Length * 2);
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint KeyeventfKeyUp = 0x0002;

        foreach (char c in text)
        {
            inputs.Add(new NativeInput
            {
                Type = 1,
                U = new InputUnion
                {
                    Ki = new KeybdInput
                    {
                        Vk = 0,
                        Scan = (ushort)c,
                        Flags = KEYEVENTF_UNICODE,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            });
            inputs.Add(new NativeInput
            {
                Type = 1,
                U = new InputUnion
                {
                    Ki = new KeybdInput
                    {
                        Vk = 0,
                        Scan = (ushort)c,
                        Flags = KEYEVENTF_UNICODE | KeyeventfKeyUp,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeInput>());
    }

    private static void SendEnter()
    {
        const uint KeyeventfKeyUp = 0x0002;
        NativeInput[] inputs = new NativeInput[]
        {
            new() { Type = 1, U = new InputUnion { Ki = new KeybdInput { Vk = 0x0D, Flags = 0 } } },
            new() { Type = 1, U = new InputUnion { Ki = new KeybdInput { Vk = 0x0D, Flags = KeyeventfKeyUp } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
    }

    private static string GetClassName(IntPtr hwnd) { try { var sb = new System.Text.StringBuilder(256); GetClassName(hwnd, sb, sb.Capacity); return sb.ToString(); } catch { return "?"; } }

    private static string GetWindowTitle(IntPtr hwnd) { try { var sb = new System.Text.StringBuilder(256); GetWindowText(hwnd, sb, sb.Capacity); return sb.ToString(); } catch { return "?"; } }

    private void SendSinglePaste(string className)
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var channel = PasteChannelSelector.Select(className, _settings.PasteChannels);
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
            Marshal.SizeOf<NativeInput>());
    }

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyUp = 0x0002;

    private static NativeInput Key(ushort vk, bool up = false) => new()
    {
        Type = InputKeyboard,
        U = new InputUnion { Ki = new KeybdInput { Vk = vk, Flags = up ? KeyeventfKeyUp : 0 } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeybdInput Ki;
        [FieldOffset(0)] public MouseInput Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    private const uint WM_PASTE = 0x0302;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    private void ApplySettings()
    {
        _window.SetClipboardListening(_settings.AutoMonitor);
        _window.SetHotkeyListening(_settings.HotkeyEnabled);

        if (_settings.InterceptTerminalCtrlV && _hookId == IntPtr.Zero)
        {
            _keyboardProc = HookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(curModule?.ModuleName), 0);
            Program.Log($"  keyboard hook {(_hookId != IntPtr.Zero ? "on" : "failed")}");
        }
        else if (!_settings.InterceptTerminalCtrlV && _hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Program.Log("  keyboard hook off");
        }
    }

    private void OnAutoMonitorToggle(object? sender, EventArgs e)
    {
        _settings.AutoMonitor = _autoMonitorItem.Checked;
        _window.SetClipboardListening(_settings.AutoMonitor);
        _settings.Save();
        Program.Log($"AutoMonitor -> {_settings.AutoMonitor}");
    }

    private void OnHotkeyToggle(object? sender, EventArgs e)
    {
        _settings.HotkeyEnabled = _hotkeyItem.Checked;
        _window.SetHotkeyListening(_settings.HotkeyEnabled);
        _settings.Save();
        Program.Log($"HotkeyEnabled -> {_settings.HotkeyEnabled}");
    }

    private void OnCtrlVToggle(object? sender, EventArgs e)
    {
        _settings.InterceptTerminalCtrlV = _ctrlVItem.Checked;
        ApplySettings();
        _settings.Save();
        Program.Log($"InterceptTerminalCtrlV -> {_settings.InterceptTerminalCtrlV}");
    }

    private bool CleanClipboard(bool showNotification = true, bool force = false)
    {
        Trace("CleanClipboard enter");
        if (!Clipboard.ContainsText())
        {
            Trace("  no text on clipboard");
            if (showNotification) _icon.ShowBalloonTip(1200, "Pastefix", "剪貼簿中沒有文字", ToolTipIcon.Info);
            return false;
        }

        string text = Clipboard.GetText();
        string cleaned = force ? Normalizer.Normalize(text) : Normalizer.Dedent(text);
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
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _icon.Visible = false;
        _icon.Dispose();
        _clipDebounce?.Dispose();
        _window.ReleaseHandle();
        base.ExitThreadCore();
    }
}

