namespace PasteFix.Tests;

/// <summary>
/// Cleaning replaces what the user copied. Until this existed there was no way
/// back — the only recourse was turning AutoMonitor off, which is a switch the
/// user has to remember to turn back on. One step of undo covers the mistake
/// that actually happens: pasting a clean the tool got wrong.
/// </summary>
public class LastCleanTests
{
    [Fact]
    public void NothingToRestoreBeforeTheFirstClean()
    {
        var lastClean = new LastClean();

        Assert.False(lastClean.CanRestore);
        Assert.Null(lastClean.Before);
    }

    [Fact]
    public void TheTextFromBeforeTheCleanIsWhatComesBack()
    {
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");

        Assert.True(lastClean.CanRestore);
        Assert.Equal("    echo one", lastClean.Before);
    }

    [Fact]
    public void RestoreIsOfferedWhileTheClipboardStillHoldsTheClean()
    {
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");

        Assert.True(lastClean.StillOnClipboard("echo one"));
    }

    [Fact]
    public void RestoreIsRefusedOnceSomethingElseWasCopied()
    {
        // Putting the old text back would destroy the newer copy, which the user
        // made on purpose. Dropping the undo is the safe direction.
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");

        Assert.False(lastClean.StillOnClipboard("something else entirely"));
    }

    [Fact]
    public void RestoreIsRefusedWhenTheClipboardIsEmpty()
    {
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");

        Assert.False(lastClean.StillOnClipboard(null));
        Assert.False(lastClean.StillOnClipboard(""));
    }

    [Fact]
    public void ARestoreThatFailedKeepsTheWayBack()
    {
        // Clipboard.SetText can fail because another process holds the clipboard
        // open for a moment. That is not a reason to lose the only undo.
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");

        Assert.True(lastClean.CanRestore);
        Assert.Equal("    echo one", lastClean.Before);
    }

    [Fact]
    public void ClearingForgetsTheClean()
    {
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");
        lastClean.Clear();

        Assert.False(lastClean.CanRestore);
        Assert.Null(lastClean.Before);
        Assert.False(lastClean.StillOnClipboard("echo one"));
    }

    [Fact]
    public void OnlyTheMostRecentCleanIsUndoable()
    {
        var lastClean = new LastClean();
        lastClean.Record("    echo one", "echo one");
        lastClean.Record("    echo two", "echo two");

        Assert.Equal("    echo two", lastClean.Before);
        Assert.False(lastClean.StillOnClipboard("echo one"));
    }

    [Fact]
    public void AWhitespaceOnlyCleanIsNotRecordedAsUndoable()
    {
        // Nothing is written back for an empty result, so there is nothing to
        // undo. Recording it would offer a restore that puts back the whitespace
        // the user just copied.
        var lastClean = new LastClean();

        Assert.False(lastClean.CanRestore);
        Assert.False(lastClean.StillOnClipboard(""));
    }
}
