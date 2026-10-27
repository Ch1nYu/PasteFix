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
    /// <summary>
    /// Returns the cleaned text and how many characters were fixed. Text with
    /// nothing to fix is returned as the same instance.
    /// </summary>
    public static (string Text, int Fixed) Clean(string input)
    {
        int first = IndexOfFix(input);
        if (first < 0) return (input, 0);

        var result = new StringBuilder(input.Length);
        result.Append(input, 0, first);
        int fixedCount = 0;

        for (int i = first; i < input.Length; i++)
        {
            char c = input[i];
            if (IsSpaceLookalike(c))
            {
                result.Append(' ');
                fixedCount++;
            }
            else if (IsInvisible(c))
            {
                fixedCount++;
            }
            else
            {
                result.Append(c);
            }
        }

        return (result.ToString(), fixedCount);
    }

    private static int IndexOfFix(string input)
    {
        for (int i = 0; i < input.Length; i++)
        {
            if (IsSpaceLookalike(input[i]) || IsInvisible(input[i])) return i;
        }
        return -1;
    }

    private static bool IsSpaceLookalike(char c) => c switch
    {
        '\u00A0' => true,                    // no-break space, what &nbsp; becomes
        >= '\u2000' and <= '\u200A' => true, // en quad … hair space
        '\u202F' => true,                    // narrow no-break space
        '\u205F' => true,                    // medium mathematical space
        _ => false,
    };

    private static bool IsInvisible(char c) => c switch
    {
        '\u200B' => true, // zero-width space
        '\u2060' => true, // word joiner
        '\uFEFF' => true, // byte order mark / zero-width no-break space
        '\u00AD' => true, // soft hyphen
        _ => false,
    };
}
