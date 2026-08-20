using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PasteFix;

/// <summary>
/// 隱藏視窗，負責接收 Windows 剪貼簿變更訊息 (WM_CLIPBOARDUPDATE)。
/// </summary>
public sealed class MessageWindow : NativeWindow
{
    private const int WmClipboardUpdate = 0x031D;

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(nint hWnd);

    public event Action? ClipboardChanged;
    private bool _clipboardListening;

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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate)
        {
            ClipboardChanged?.Invoke();
        }

        base.WndProc(ref m);
    }

    public override void DestroyHandle()
    {
        if (_clipboardListening) RemoveClipboardFormatListener(Handle);
        base.DestroyHandle();
    }
}

