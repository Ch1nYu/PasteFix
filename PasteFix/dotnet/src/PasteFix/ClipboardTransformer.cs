namespace PasteFix;

public enum ProcessingMode
{
    SmartContinuation,
    SingleLine,
}

/// <summary>
/// The outcome of one clipboard transformation: the text to paste, which
/// wrappers were removed on the way, and how many invisible characters were
/// fixed. Both are reported so the caller can say what it did without ever
/// naming the content.
/// </summary>
public sealed record ClipboardResult(string Text, IReadOnlyList<ContainerKind> Stripped, int CharactersFixed);

public static class ClipboardTransformer
{
    /// <summary>
    /// Fixes invisible characters, strips the wrapper the copy arrived in, then
    /// applies the selected mode. Characters go first so a prompt or a fence
    /// indented with no-break spaces is still recognised; the wrapper goes
    /// before the mode because a fence or a prompt gutter is a frame around the
    /// text, and the mode rules should see the text itself.
    /// </summary>
    public static ClipboardResult Transform(string input, ProcessingMode mode)
    {
        (string chars, int fixedCount) = CharacterNormalizer.Clean(input);
        (string stripped, IReadOnlyList<ContainerKind> kinds) = ContainerStripper.Strip(chars);

        string text = mode switch
        {
            ProcessingMode.SingleLine => SingleLineNormalizer.Flatten(stripped),
            _ => Normalizer.Normalize(stripped),
        };

        return new ClipboardResult(text, kinds, fixedCount);
    }
}
