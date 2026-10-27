namespace PasteFix.Tests;

/// <summary>
/// A copy from a browser carries an HTML version of the selection next to the
/// plain text, and in it a chat window's code block says outright what it is:
/// <c>&lt;pre&gt;&lt;code class="language-python"&gt;</c>. Reading that is not a
/// guess about what a line means — the page already said so. Anything less
/// certain than one code block covering the whole selection is reported as
/// such, and changes nothing.
/// </summary>
public class HtmlClipboardTests
{
    /// <summary>
    /// The CF_HTML clipboard format: a header of byte offsets, then the page,
    /// with the selection between the two fragment markers.
    /// </summary>
    private static string CfHtml(string fragment) =>
        "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000999\r\n" +
        "StartFragment:0000000141\r\nEndFragment:0000000900\r\n" +
        "<html>\r\n<body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body>\r\n</html>";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoHtmlOnTheClipboardIsPlainText(string? html)
    {
        Assert.Equal(CopySource.PlainText, HtmlClipboard.Classify(html));
    }

    [Fact]
    public void HtmlWithoutFragmentMarkersIsTreatedAsPlainText()
    {
        // Without the markers there is no telling which part was selected.
        Assert.Equal(CopySource.PlainText, HtmlClipboard.Classify("<pre><code class=\"language-python\">x</code></pre>"));
    }

    [Theory]
    // Claude
    [InlineData("<pre class=\"code-block__code\"><code class=\"language-python\">def f():\n    return 1</code></pre>", "python")]
    // ChatGPT
    [InlineData("<pre><code class=\"!whitespace-pre hljs language-bash\">echo one</code></pre>", "bash")]
    // Prism, highlight.js and docs sites
    [InlineData("<pre class=\"lang-yaml\"><code>a: 1</code></pre>", "yaml")]
    [InlineData("<pre data-language=\"PowerShell\"><code>Get-Item .</code></pre>", "powershell")]
    public void OneCodeBlockCoveringTheSelectionNamesItsLanguage(string fragment, string language)
    {
        Assert.Equal(new CopySource(SourceKind.Code, language), HtmlClipboard.Classify(CfHtml(fragment)));
    }

    [Fact]
    public void WhitespaceAroundTheBlockStillCountsAsOneBlock()
    {
        Assert.Equal(
            new CopySource(SourceKind.Code, "python"),
            HtmlClipboard.Classify(CfHtml("\r\n  <div>&nbsp;<pre><code class=\"language-python\">x = 1</code></pre></div>\r\n")));
    }

    [Fact]
    public void CodeBlockWithoutALanguageIsCodeOfUnknownLanguage()
    {
        Assert.Equal(new CopySource(SourceKind.Code, null), HtmlClipboard.Classify(CfHtml("<pre>echo one</pre>")));
    }

    [Fact]
    public void InlineCodeAloneIsCode()
    {
        Assert.Equal(new CopySource(SourceKind.Code, null), HtmlClipboard.Classify(CfHtml("<code>npm install</code>")));
    }

    [Fact]
    public void LanguageOutsideTheKnownListIsNotWrittenToTheLog()
    {
        // The class attribute is markup from whatever page was copied, and the
        // permanent log never carries copied content. Only names from a fixed
        // list are reported.
        Assert.Equal(
            new CopySource(SourceKind.Code, "other"),
            HtmlClipboard.Classify(CfHtml("<pre><code class=\"language-hunter2\">x</code></pre>")));
    }

    [Fact]
    public void ProseAroundACodeBlockIsMixed()
    {
        Assert.Equal(
            new CopySource(SourceKind.Mixed, null),
            HtmlClipboard.Classify(CfHtml("<p>Run this:</p><pre><code class=\"language-bash\">echo one</code></pre>")));
    }

    [Fact]
    public void TwoCodeBlocksAreMixed()
    {
        Assert.Equal(
            new CopySource(SourceKind.Mixed, null),
            HtmlClipboard.Classify(CfHtml(
                "<pre><code class=\"language-bash\">echo one</code></pre>" +
                "<pre><code class=\"language-python\">x = 1</code></pre>")));
    }

    [Fact]
    public void HtmlWithNoCodeElementIsJustHtml()
    {
        // Not "prose": VS Code copies highlighted code as plain divs and spans.
        // All this says is that the page did not mark the selection as code.
        Assert.Equal(
            new CopySource(SourceKind.Html, null),
            HtmlClipboard.Classify(CfHtml("<div><span style=\"color:#569cd6\">def</span> f():</div>")));
    }

    [Fact]
    public void EmptyFragmentIsPlainText()
    {
        Assert.Equal(CopySource.PlainText, HtmlClipboard.Classify(CfHtml("<span></span>")));
    }

    [Theory]
    [InlineData("python")]
    [InlineData("yaml")]
    [InlineData("json")]
    [InlineData("javascript")]
    public void StructuredLanguagesKeepTheirLineStructure(string language)
    {
        Assert.True(new CopySource(SourceKind.Code, language).KeepsLineStructure);
    }

    [Theory]
    [InlineData(SourceKind.Code, "bash")]
    [InlineData(SourceKind.Code, "powershell")]
    [InlineData(SourceKind.Code, "text")]
    [InlineData(SourceKind.Code, "other")]
    [InlineData(SourceKind.Code, null)]
    [InlineData(SourceKind.Mixed, null)]
    [InlineData(SourceKind.Html, null)]
    [InlineData(SourceKind.PlainText, null)]
    public void OnlyAKnownStructuredLanguageKeepsLineStructure(SourceKind kind, string? language)
    {
        Assert.False(new CopySource(kind, language).KeepsLineStructure);
    }

    [Theory]
    [InlineData(SourceKind.PlainText, null, "text")]
    [InlineData(SourceKind.Html, null, "html")]
    [InlineData(SourceKind.Mixed, null, "mixed")]
    [InlineData(SourceKind.Code, null, "code")]
    [InlineData(SourceKind.Code, "python", "code:python")]
    public void SourceDescribesItselfForTheLog(SourceKind kind, string? language, string expected)
    {
        Assert.Equal(expected, new CopySource(kind, language).ToString());
    }
}
