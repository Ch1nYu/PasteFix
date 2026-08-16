namespace PasteFix.Tests;

/// <summary>
/// CleanClipboard writes its result back to the clipboard while the format
/// listener is still armed. Until 2026-08-16 that re-entry was defended only
/// implicitly: the written-back text is dedent-idempotent, so the monitor pass
/// found nothing to do. The gate makes the protection structural — the
/// clipboard's own sequence number tells the writes apart — so a future
/// normalizer that isn't idempotent still can't loop.
/// </summary>
public class OwnWriteGateTests
{
    [Fact]
    public void NothingIsIgnoredBeforeWeHaveWrittenAnything()
    {
        var gate = new OwnWriteGate();
        Assert.False(gate.IsOwnWrite(42));
    }

    [Fact]
    public void TheEventCausedByOurOwnWriteBackIsIgnored()
    {
        var gate = new OwnWriteGate();
        gate.MarkOwnWrite(42);
        Assert.True(gate.IsOwnWrite(42));
    }

    [Fact]
    public void AUserCopyAfterOurWriteBackIsStillProcessed()
    {
        // The user copied something else after our write-back: the sequence
        // moved past ours, so the event is genuinely new and must be cleaned.
        var gate = new OwnWriteGate();
        gate.MarkOwnWrite(42);
        Assert.False(gate.IsOwnWrite(43));
    }
}
