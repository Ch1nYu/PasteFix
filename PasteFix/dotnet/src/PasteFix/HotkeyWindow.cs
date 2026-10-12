using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteFix;

/// <summary>
/// Registers one global hotkey and reports presses.
///
/// This is not the input simulation banned on 2026-08-16. Nothing here sends
/// keystrokes or hooks the keyboard: RegisterHotKey asks Windows to deliver
/// WM_HOTKEY to this window when the combination is pressed, and the handler
/// runs the same cleaning path the tray menu already runs. The ban is on
/// synthesising input (SendInput/keybd_event), which once opened dozens of
/// WezTerm tabs. See DEVELOPMENT_HANDOFF.md, gotcha 1.
/// </summary>
public sealed class HotkeyWindow : NativeWindow
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    // VK_V. Ctrl+Alt+V is free in Windows and does not collide with the
    // terminal paste chords Pastefix exists to serve.
    private const uint VirtualKeyV = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public event Action? Pressed;

    private bool _registered;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    /// <summary>Whether the hotkey is live. False means another program owns it.</summary>
    public bool Registered => _registered;

    public void SetHotkey(bool enabled)
    {
        if (enabled == _registered) return;

        if (enabled)
        {
            _registered = RegisterHotKey(Handle, 1, ModControl | ModAlt | ModShift | ModNoRepeat, VirtualKeyV);
            Program.Log(_registered
                ? "  hotkey Ctrl+Alt+Shift+V registered"
                : "  hotkey Ctrl+Alt+Shift+V unavailable, another program owns it");
        }
        else
        {
            UnregisterHotKey(Handle, 1);
            _registered = false;
            Program.Log("  hotkey released");
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == 1)
        {
            Pressed?.Invoke();
        }

        base.WndProc(ref m);
    }

    public override void DestroyHandle()
    {
        if (_registered) UnregisterHotKey(Handle, 1);
        base.DestroyHandle();
    }
}
