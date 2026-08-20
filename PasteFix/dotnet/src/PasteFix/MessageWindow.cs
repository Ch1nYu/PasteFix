using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteFix;

/// <summary>
/// 隱藏視窗，負責接收剪貼簿變更與熱鍵訊息。
/// </summary>
public sealed class MessageWindow : NativeWindow
{
    private const int WmClipboardUpdate = 0x031D;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public event Action? ClipboardChanged;
    public event Action? HotkeyPressed;

    private bool _clipboardListening;
    private bool _hotkeyListening;

    public MessageWindow()
    {
        CreateHandle(new CreateParams());
    }

    public void SetClipboardListening(bool enabled)
    {
        if (enabled == _clipboardListening) return;
        _clipboardListening = enabled;

        bool ok = enabled ? AddClipboardFormatListener(Handle) : RemoveClipboardFormatListener(Handle);
        Program.Log($"  clipboard listener {(enabled ? "on" : "off")} ok={ok}");
    }

    public void SetHotkeyListening(bool enabled)
    {
        if (enabled == _hotkeyListening) return;
        _hotkeyListening = enabled;

        bool ok = enabled
            ? RegisterHotKey(Handle, HotkeyId, ModAlt | ModNoRepeat, (uint)Keys.V)
            : UnregisterHotKey(Handle, HotkeyId);
        Program.Log($"  hotkey Alt+V {(enabled ? "registered" : "unregistered")} ok={ok}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate)
        {
            ClipboardChanged?.Invoke();
        }
        else if (m.Msg == WmHotkey)
        {
            HotkeyPressed?.Invoke();
        }

        base.WndProc(ref m);
    }

    public override void DestroyHandle()
    {
        if (_clipboardListening) RemoveClipboardFormatListener(Handle);
        if (_hotkeyListening) UnregisterHotKey(Handle, HotkeyId);
        base.DestroyHandle();
    }
}

