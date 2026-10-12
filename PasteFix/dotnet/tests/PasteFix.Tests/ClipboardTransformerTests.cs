namespace PasteFix.Tests;

public class ClipboardTransformerTests
{
    [Fact]
    public void SmartModePreservesIndependentCommands()
    {
        const string input = "echo one\necho two";

        Assert.Equal(
            input,
            ClipboardTransformer.Transform(input, ProcessingMode.SmartContinuation));
    }

    [Fact]
    public void SingleLineModeFlattensIndependentCommands()
    {
        Assert.Equal(
            "echo one echo two",
            ClipboardTransformer.Transform("echo one\necho two", ProcessingMode.SingleLine));
    }

    [Fact]
    public void UnknownModeSafelyUsesSmartMode()
    {
        const string input = "echo one\necho two";

        Assert.Equal(input, ClipboardTransformer.Transform(input, (ProcessingMode)999));
    }
}
