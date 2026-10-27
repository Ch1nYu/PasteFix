using System.Buffers;
using System.Text;

namespace PasteFix;

/// <summary>
/// Replaces the characters a web page or chat window puts on the clipboard that
/// look like a space, or like nothing, but that a shell reads as something else.
/// <c>git&#160;commit</c> with a no-break space is a single word to pwsh, and a
/// zero-width space inside a flag makes it an unknown flag — both paste looking
/// perfectly fine and fail with no visible cause.
///
/// Only two kinds of character are touched, and both are invisible by nature:
/// spaces that render as an ordinary space, and characters that render as
/// nothing. Everything a reader can see is left as copied. Curly quotes and
/// dashes are visible, and can be what was meant, so they are never rewritten.
/// Ideographic space (U+3000) is visible too — it is the full-width space CJK
/// text indents with. Zero-width joiner and non-joiner are kept because they
/// carry meaning: they hold emoji sequences together and are spelling in
/// Persian and several Indic scripts.
/// </summary>
public static class CharacterNormalizer
{
    // Render as an ordinary space: no-break space (what &nbsp; becomes), en quad
    // through hair space, narrow no-break space, medium mathematical space.
    private const string SpaceLookalikes =
        "\u00A0\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u202F\u205F";

    // Render as nothing: zero-width space, word joiner, byte order mark, soft hyphen.
    private const string Invisible = "\u200B\u2060\uFEFF\u00AD";

    private static readonly SearchValues<char> Spaces = SearchValues.Create(SpaceLookalikes);
    private static readonly SearchValues<char> Fixable = SearchValues.Create(SpaceLookalikes + Invisible);

    /// <summary>
    /// Returns the cleaned text and how many characters were fixed. Text with
    /// nothing to fix is returned as the same instance.
    /// </summary>
    public static (string Text, int Fixed) Clean(string input)
    {
        int first = input.AsSpan().IndexOfAny(Fixable);
        if (first < 0) return (input, 0);

        var result = new StringBuilder(input.Length);
        result.Append(input, 0, first);
        int fixedCount = 0;

        for (int i = first; i < input.Length; i++)
        {
            char c = input[i];
            if (!Fixable.Contains(c))
            {
                result.Append(c);
                continue;
            }

            fixedCount++;
            if (Spaces.Contains(c)) result.Append(' ');
        }

        return (result.ToString(), fixedCount);
    }
}
