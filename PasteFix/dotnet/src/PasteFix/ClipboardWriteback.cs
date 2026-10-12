namespace PasteFix;

/// <summary>
/// Whether cleaning the clipboard text is worth writing back.
/// </summary>
public enum ClipboardWriteDecision
{
    /// <summary>Cleaning changed the text, so it replaces the clipboard.</summary>
    Write,

    /// <summary>Cleaning changed nothing, so the clipboard is left alone.</summary>
    NothingToDo,

    /// <summary>
    /// Cleaning produced no text at all. <c>Clipboard.SetText</c> rejects an
    /// empty string with <c>ArgumentNullException</c>, and an empty result is
    /// not a cleaning, so the clipboard keeps whatever the user copied.
    /// </summary>
    EmptyResult,
}

/// <summary>
/// The write-back step of CleanClipboard. It is a pure function so the skip
/// cases stay testable without a clipboard; the write itself, and failing to
/// write, remain in TrayApplication.
///
/// EmptyResult was a real crash in pastefix.log: six ArgumentNullException
/// entries from SetText between 2026-09-03 and 2026-10-10. None was noticed at
/// the time — Application.ThreadException swallowed the exception, so the tray
/// kept running and the only symptom was a paste that had not been cleaned.
/// </summary>
public static class ClipboardWriteback
{
    public static ClipboardWriteDecision Decide(string? text, string cleaned)
    {
        if (cleaned.Length == 0) return ClipboardWriteDecision.EmptyResult;
        return cleaned == text ? ClipboardWriteDecision.NothingToDo : ClipboardWriteDecision.Write;
    }
}
