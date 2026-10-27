using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PasteFix;

/// <summary>
/// Who put the current content on the clipboard. Neither call opens the
/// clipboard, so neither can fail with CLIPBRD_E_CANT_OPEN — which matters
/// because they run on every update, including the bursts from programs that
/// borrow the clipboard.
/// </summary>
internal static class ClipboardWriter
{
    private static readonly uint[] IgnoreFormats =
    [
        RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
        RegisterClipboardFormat("Clipboard Viewer Ignore"),
    ];

    /// <summary>
    /// The process name of the clipboard owner, or null when there is no owner
    /// window or the process has already exited. A name is not content, so it
    /// is safe in the permanent log.
    /// </summary>
    public static string? ProcessName()
    {
        IntPtr owner = GetClipboardOwner();
        if (owner == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(owner, out uint pid) == 0) return null;

        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool AskedToBeIgnored() =>
        IgnoreFormats.Any(format => format != 0 && IsClipboardFormatAvailable(format));

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
}
