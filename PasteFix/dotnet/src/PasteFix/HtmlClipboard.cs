using System.Text.RegularExpressions;

namespace PasteFix;

/// <summary>What the HTML copy of the selection says the copy was.</summary>
public enum SourceKind
{
    /// <summary>No usable HTML on the clipboard: a plain-text copy, or HTML without fragment markers.</summary>
    PlainText,

    /// <summary>HTML with no code element. Not "prose" — VS Code copies code as plain divs too.</summary>
    Html,

    /// <summary>A code block plus something else: prose around it, or a second block.</summary>
    Mixed,

    /// <summary>One code element covering the whole selection.</summary>
    Code,
}

/// <summary>
/// Where a copy came from, as far as the clipboard's HTML says. The language is
/// only ever a name from <see cref="HtmlClipboard"/>'s fixed list, or "other",
/// because this value is written to the permanent log and the class attribute
/// it is read from is markup from whatever page was copied.
/// </summary>
public sealed record CopySource(SourceKind Kind, string? Language)
{
    public static readonly CopySource PlainText = new(SourceKind.PlainText, null);

    /// <summary>
    /// True when the page said the copy is one block of a language whose line
    /// breaks carry meaning, which Force Single Line would destroy.
    /// </summary>
    public bool KeepsLineStructure =>
        Kind == SourceKind.Code && Language is not null && HtmlClipboard.IsStructured(Language);

    public override string ToString() => Kind switch
    {
        SourceKind.Code => Language is null ? "code" : $"code:{Language}",
        SourceKind.Mixed => "mixed",
        SourceKind.Html => "html",
        _ => "text",
    };
}

/// <summary>
/// Reads the CF_HTML copy of a selection. A browser puts it on the clipboard
/// next to the plain text, and in it a chat window's code block says outright
/// what it is: <c>&lt;pre&gt;&lt;code class="language-python"&gt;</c>.
///
/// This is not the line-by-line guessing removed on 2026-08-09: nothing here
/// decides what a line means. The page either marked the whole selection as one
/// code block, or this reports that it did not. A selection that only partly
/// covers a block may arrive without the block's tags; it is then reported as
/// <see cref="SourceKind.Html"/> and changes nothing, which is a miss, not damage.
/// </summary>
public static partial class HtmlClipboard
{
    [GeneratedRegex(@"<!--StartFragment-->(?<f>.*)<!--EndFragment-->", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Fragment();

    [GeneratedRegex(@"<pre\b[^>]*>.*?</pre\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PreBlock();

    [GeneratedRegex(@"<code\b[^>]*>.*?</code\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CodeElement();

    [GeneratedRegex(@"<(?:pre|code)\b[^>]*?(?:\bclass\s*=\s*[""'][^""']*?(?<![\w-])(?:language|lang)-(?<lang>[\w+#.-]+)|\bdata-lang(?:uage)?\s*=\s*[""'](?<lang>[^""']+))", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageAttribute();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"&(?:nbsp|#160|#x0*a0);", RegexOptions.IgnoreCase)]
    private static partial Regex NoBreakSpaceEntity();

    // Languages whose line breaks and indentation carry meaning, or whose line
    // comments would swallow everything after them once flattened.
    private static readonly HashSet<string> Structured = new(StringComparer.Ordinal)
    {
        "python", "py", "python3", "yaml", "yml", "json", "jsonc", "json5", "toml", "ini",
        "xml", "html", "css", "scss", "javascript", "js", "jsx", "typescript", "ts", "tsx",
        "csharp", "cs", "c#", "c", "cpp", "c++", "java", "kotlin", "kt", "go", "golang",
        "rust", "rs", "ruby", "rb", "php", "swift", "lua", "sql", "dockerfile", "makefile",
        "make", "markdown", "md", "diff", "patch", "hcl", "terraform", "tf", "r", "perl",
    };

    // Shells and plain text: the mode the user picked applies as it always has.
    private static readonly HashSet<string> Flattenable = new(StringComparer.Ordinal)
    {
        "bash", "sh", "shell", "zsh", "fish", "ksh", "console", "shellsession", "sh-session",
        "terminal", "powershell", "pwsh", "ps1", "ps", "posh", "cmd", "bat", "batch", "dos",
        "text", "txt", "plaintext", "plain", "nohighlight",
    };

    internal static bool IsStructured(string language) => Structured.Contains(language);

    public static CopySource Classify(string? cfHtml)
    {
        if (string.IsNullOrEmpty(cfHtml)) return CopySource.PlainText;

        Match fragment = Fragment().Match(cfHtml);
        if (!fragment.Success) return CopySource.PlainText;

        string html = fragment.Groups["f"].Value;
        MatchCollection blocks = PreBlock().Matches(html);
        if (blocks.Count == 0) blocks = CodeElement().Matches(html);

        if (blocks.Count == 0)
            return HasVisibleText(html) ? new CopySource(SourceKind.Html, null) : CopySource.PlainText;

        if (blocks.Count > 1) return new CopySource(SourceKind.Mixed, null);

        Match block = blocks[0];
        string outside = html[..block.Index] + html[(block.Index + block.Length)..];
        if (HasVisibleText(outside)) return new CopySource(SourceKind.Mixed, null);

        return new CopySource(SourceKind.Code, KnownLanguage(block.Value));
    }

    private static string? KnownLanguage(string block)
    {
        Match match = LanguageAttribute().Match(block);
        if (!match.Success) return null;

        string language = match.Groups["lang"].Value.Trim().ToLowerInvariant();
        return Structured.Contains(language) || Flattenable.Contains(language) ? language : "other";
    }

    private static bool HasVisibleText(string html) =>
        !string.IsNullOrWhiteSpace(NoBreakSpaceEntity().Replace(Tag().Replace(html, ""), " "));
}
