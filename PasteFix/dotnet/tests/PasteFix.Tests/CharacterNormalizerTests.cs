namespace PasteFix.Tests;

/// <summary>
/// Web pages and chat windows put characters on the clipboard that look exactly
/// like a space, or like nothing at all, and that a shell reads as something
/// else: <c>git\u00A0commit</c> is one word to pwsh, not two. Only characters
/// that are invisible or render as an ordinary space are touched — anything a
/// reader can see stays exactly as copied.
/// </summary>
public class CharacterNormalizerTests
{
    [Theory]
    [InlineData("git\u00A0commit")]   // no-break space, what &nbsp; becomes
    [InlineData("git\u202Fcommit")]   // narrow no-break space
    [InlineData("git\u2007commit")]   // figure space
    [InlineData("git\u2009commit")]   // thin space
    [InlineData("git\u205Fcommit")]   // medium mathematical space
    public void SpaceLookalikesBecomeOrdinarySpaces(string input)
    {
        Assert.Equal(("git commit", 1), CharacterNormalizer.Clean(input));
    }

    [Theory]
    [InlineData("npm\u200B install")]  // zero-width space
    [InlineData("npm\u2060 install")]  // word joiner
    [InlineData("\uFEFFnpm install")]  // byte order mark / zero-width no-break space
    [InlineData("npm\u00AD install")]  // soft hyphen
    public void InvisibleCharactersAreRemoved(string input)
    {
        Assert.Equal(("npm install", 1), CharacterNormalizer.Clean(input));
    }

    [Fact]
    public void EveryFixIsCounted()
    {
        Assert.Equal(
            ("a b c d", 3),
            CharacterNormalizer.Clean("a\u00A0b\u00A0c\u200B d"));
    }

    [Fact]
    public void NoBreakSpaceIndentationBecomesIndentationTheNormalizerCanSee()
    {
        // A code block rendered with &nbsp; keeps its indent on screen, but
        // Normalizer only counts spaces and tabs as indentation.
        Assert.Equal(
            ("  echo one\n  echo two", 4),
            CharacterNormalizer.Clean("\u00A0\u00A0echo one\n\u00A0\u00A0echo two"));
    }

    [Fact]
    public void TextWithNothingToFixIsReturnedAsIs()
    {
        const string input = "echo one\r\n\techo two";

        (string text, int fixedCount) = CharacterNormalizer.Clean(input);

        Assert.Same(input, text);
        Assert.Equal(0, fixedCount);
    }

    [Theory]
    // Ideographic space is the full-width space CJK text indents with. It is
    // as wide as a character and is there on purpose.
    [InlineData("第一段\u3000開頭")]
    // Zero-width joiner holds emoji sequences together; zero-width non-joiner
    // is spelling in Persian and several Indic scripts.
    [InlineData("👨\u200D👩\u200D👧")]
    [InlineData("می\u200Cخواهم")]
    // Visible typography is the reader's to judge, not ours: a dash or a curly
    // quote can be exactly what was meant, so neither is rewritten.
    [InlineData("git push \u2014force")]
    [InlineData("echo \u201Chello\u201D")]
    public void VisibleOrMeaningfulCharactersAreLeftAlone(string input)
    {
        Assert.Equal((input, 0), CharacterNormalizer.Clean(input));
    }

    [Fact]
    public void EmptyTextIsReturnedAsIs()
    {
        Assert.Equal(("", 0), CharacterNormalizer.Clean(""));
    }
}
