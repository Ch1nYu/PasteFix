namespace PasteFix.Tests;

/// <summary>
/// Some programs borrow the clipboard: LazyTyper puts dictated text on it,
/// pastes, and about 0.72 s later puts the previous content back. That was a
/// third of all logged copies on 2026-10-27. Those writes are not copies the
/// user made, so the monitor leaves them alone. A manual clean is never
/// filtered — asking for one is the user saying this copy is theirs.
/// </summary>
public class MonitorFilterTests
{
    private static readonly string[] Ignored = ["LazyTyper"];

    [Fact]
    public void AWriterThatAskedToBeIgnoredIsSkipped()
    {
        // ExcludeClipboardContentFromMonitorProcessing is the Windows convention
        // for "this is not a real copy"; clipboard managers honour it too.
        Assert.Equal(MonitorSkip.WriterAskedToBeIgnored, MonitorFilter.Decide("chrome", writerAskedToBeIgnored: true, Ignored));
        Assert.Equal(MonitorSkip.WriterAskedToBeIgnored, MonitorFilter.Decide(null, writerAskedToBeIgnored: true, Ignored));
    }

    [Theory]
    [InlineData("LazyTyper")]
    [InlineData("lazytyper")]
    [InlineData("LAZYTYPER")]
    public void AnIgnoredProcessIsSkippedWhateverTheCase(string owner)
    {
        Assert.Equal(MonitorSkip.IgnoredProcess, MonitorFilter.Decide(owner, writerAskedToBeIgnored: false, Ignored));
    }

    [Fact]
    public void AnEntryWrittenWithItsExeSuffixStillMatches()
    {
        // Task Manager shows "LazyTyper.exe"; Process.ProcessName has no suffix.
        Assert.Equal(
            MonitorSkip.IgnoredProcess,
            MonitorFilter.Decide("LazyTyper", writerAskedToBeIgnored: false, [" LazyTyper.EXE "]));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("LazyTyperHelper")]
    public void AnyOtherProcessIsCleaned(string owner)
    {
        Assert.Equal(MonitorSkip.None, MonitorFilter.Decide(owner, writerAskedToBeIgnored: false, Ignored));
    }

    [Fact]
    public void AnUnknownWriterIsCleaned()
    {
        // GetClipboardOwner returns no window for programs that write without
        // one. Not knowing who wrote is not a reason to skip a real copy.
        Assert.Equal(MonitorSkip.None, MonitorFilter.Decide(null, writerAskedToBeIgnored: false, Ignored));
    }

    [Fact]
    public void AnEmptyListIgnoresNoProcess()
    {
        Assert.Equal(MonitorSkip.None, MonitorFilter.Decide("LazyTyper", writerAskedToBeIgnored: false, []));
    }
}
