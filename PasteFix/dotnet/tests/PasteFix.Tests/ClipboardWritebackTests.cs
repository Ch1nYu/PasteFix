namespace PasteFix.Tests;

/// <summary>
/// CleanClipboard used to write the cleaned text back whenever it differed from
/// the clipboard text. Two of the outcomes that produced a crash are decided
/// here instead.
///
/// Cleaning a copy that held nothing but whitespace leaves an empty string, and
/// Clipboard.SetText rejects that with ArgumentNullException. Six such crashes
/// are in pastefix.log between 2026-09-03 and 2026-10-10. Each one was swallowed
/// by Application.ThreadException, so the tray kept running and the only symptom
/// was a paste that had not been cleaned.
/// </summary>
public class ClipboardWritebackTests
{
    [Fact]
    public void WhitespaceOnlyCopyIsNotWrittenBackAsAnEmptyString()
    {
        // The exact crash: SplitLines drops whitespace-only lines, so the whole
        // input reduces to nothing, and the empty result used to be written back.
        const string copied = "\n\n";
        string cleaned = ClipboardTransformer.Transform(copied, ProcessingMode.SmartContinuation).Text;

        Assert.Equal("", cleaned);
        Assert.Equal(ClipboardWriteDecision.EmptyResult, ClipboardWriteback.Decide(copied, cleaned));
    }

    [Fact]
    public void WhitespaceOnlyCopyIsNotWrittenBackInSingleLineModeEither()
    {
        const string copied = "   \r\n\t\r\n  ";
        string cleaned = ClipboardTransformer.Transform(copied, ProcessingMode.SingleLine).Text;

        Assert.Equal("", cleaned);
        Assert.Equal(ClipboardWriteDecision.EmptyResult, ClipboardWriteback.Decide(copied, cleaned));
    }

    [Fact]
    public void EmptyClipboardReadIsNotWrittenBack()
    {
        Assert.Equal(ClipboardWriteDecision.EmptyResult, ClipboardWriteback.Decide("", ""));
        Assert.Equal(ClipboardWriteDecision.EmptyResult, ClipboardWriteback.Decide(null, ""));
    }

    [Fact]
    public void UnchangedTextIsLeftAlone()
    {
        const string copied = "echo one\necho two";

        Assert.Equal(ClipboardWriteDecision.NothingToDo, ClipboardWriteback.Decide(copied, copied));
    }

    [Fact]
    public void CleanedTextReplacesTheClipboard()
    {
        const string copied = "    echo one\n    echo two";
        string cleaned = ClipboardTransformer.Transform(copied, ProcessingMode.SmartContinuation).Text;

        Assert.Equal("echo one\necho two", cleaned);
        Assert.Equal(ClipboardWriteDecision.Write, ClipboardWriteback.Decide(copied, cleaned));
    }
}
