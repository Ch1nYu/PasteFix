namespace PasteFix;

/// <summary>
/// The clipboard text before and after the last write-back, so one paste can be
/// undone. Cleaning replaces what the user copied, and until now there was no
/// way back — the only recourse was turning AutoMonitor off.
///
/// Held in memory only. The whole point of the permanent log's len=N rule is
/// that clipboard contents never reach disk, and a restore file would defeat it.
/// </summary>
public sealed class LastClean
{
    private string? _before;
    private string? _after;

    /// <summary>True when there is a clean to undo.</summary>
    public bool CanRestore => _before is not null;

    /// <summary>The text to put back. Only read it when <see cref="CanRestore"/>.</summary>
    public string? Before => _before;

    /// <summary>
    /// True when the clipboard still holds what this clean produced. If the user
    /// has copied something else since, restoring would overwrite that copy.
    /// </summary>
    public bool StillOnClipboard(string? current) => _before is not null && _after == current;

    public void Record(string before, string after)
    {
        _before = before;
        _after = after;
    }

    /// <summary>
    /// Forgets the clean. Called once the undo has happened, or once it can no
    /// longer happen — never before the write-back succeeds, so a clipboard that
    /// was busy for a moment does not cost the user the way back.
    /// </summary>
    public void Clear()
    {
        _before = null;
        _after = null;
    }
}
