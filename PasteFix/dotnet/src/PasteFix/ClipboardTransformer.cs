namespace PasteFix;

public enum ProcessingMode
{
    SmartContinuation,
    SingleLine,
}

/// <summary>
/// The outcome of one clipboard transformation: the text to paste, and which
/// wrappers were removed on the way. The wrappers are reported so the caller can
/// say what it did without ever naming the content.
/// </summary>
public sealed record ClipboardResult(string Text, IReadOnlyList<ContainerKind> Stripped);

public static class ClipboardTransformer
{
    /// <summary>
    /// Strips the wrapper the copy arrived in, then applies the selected mode.
    /// The wrapper goes first because a fence or a prompt gutter is a frame
    /// around the text, and the mode rules should see the text itself.
    /// </summary>
    public static ClipboardResult Transform(string input, ProcessingMode mode)
    {
        (string stripped, IReadOnlyList<ContainerKind> kinds) = ContainerStripper.Strip(input);

        string text = mode switch
        {
            ProcessingMode.SingleLine => SingleLineNormalizer.Flatten(stripped),
            _ => Normalizer.Normalize(stripped),
        };

        return new ClipboardResult(text, kinds);
    }
}
