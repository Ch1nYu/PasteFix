namespace PasteFix;

public enum ProcessingMode
{
    SmartContinuation,
    SingleLine,
}

public static class ClipboardTransformer
{
    /// <summary>Selects exactly one clipboard transformation mode.</summary>
    public static string Transform(string input, ProcessingMode mode)
    {
        return mode switch
        {
            ProcessingMode.SingleLine => SingleLineNormalizer.Flatten(input),
            _ => Normalizer.Normalize(input),
        };
    }
}
