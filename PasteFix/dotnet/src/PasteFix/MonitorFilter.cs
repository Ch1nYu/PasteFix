namespace PasteFix;

/// <summary>Why the monitor left a clipboard update alone.</summary>
public enum MonitorSkip
{
    None,

    /// <summary>
    /// The writer put ExcludeClipboardContentFromMonitorProcessing (or the older
    /// "Clipboard Viewer Ignore") on the clipboard — the Windows convention for
    /// "this is not a copy the user made".
    /// </summary>
    WriterAskedToBeIgnored,

    /// <summary>The writer is in <see cref="Settings.IgnoredProcesses"/>.</summary>
    IgnoredProcess,
}

/// <summary>
/// Decides whether the monitor should clean a clipboard update, from who wrote
/// it. Some programs borrow the clipboard rather than copy to it: LazyTyper puts
/// dictated text there, pastes, and puts the previous content back about 0.72 s
/// later — a third of all logged copies on 2026-10-27. Only the monitor is
/// filtered; a manual clean is the user saying this copy is theirs.
/// </summary>
public static class MonitorFilter
{
    public static MonitorSkip Decide(string? ownerProcess, bool writerAskedToBeIgnored, IReadOnlyCollection<string> ignoredProcesses)
    {
        if (writerAskedToBeIgnored) return MonitorSkip.WriterAskedToBeIgnored;
        if (ownerProcess is null) return MonitorSkip.None;

        foreach (string entry in ignoredProcesses)
        {
            if (string.Equals(ProcessName(entry), ownerProcess, StringComparison.OrdinalIgnoreCase))
                return MonitorSkip.IgnoredProcess;
        }
        return MonitorSkip.None;
    }

    // Task Manager shows "LazyTyper.exe"; Process.ProcessName has no suffix.
    private static string ProcessName(string entry)
    {
        string name = entry.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
