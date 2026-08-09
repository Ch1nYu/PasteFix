using System.Text.RegularExpressions;

namespace PasteFix;

/// <summary>
/// Turns a multi-line command copied from a chat window into something that can
/// be pasted into a terminal in one go. It does exactly two things:
///
///   <see cref="Dedent"/>   — remove the indent common to every line.
///   <see cref="Normalize"/> — dedent, then join continuation lines.
///
/// Nothing else. It does not strip prompts, delete comments, remove markdown
/// fences, or rewrite line endings; those rules were removed on 2026-08-09
/// because none of them had a real scenario behind it (PLAN-2026-08-09.md).
///
/// Dedent is structure-preserving, so AutoMonitor may run it on any clipboard
/// content. Joining continuations changes the text, so it only runs when the
/// user explicitly asks for it (Alt+V / the tray menu). The
/// <c>ignoreFirstLineIndent</c> option is destructive for the same reason and
/// carries the same rule: only the user may switch it on.
/// </summary>
public static partial class Normalizer
{
    // A trailing `\` (bash) or backtick (PowerShell) is a continuation marker and
    // is consumed when joining. `&&` and a trailing pipe also continue, but stay.
    //
    // The marker must have whitespace in front of it. Without that guard a
    // Windows path (`cd C:\temp\`) or a ``` fence looks exactly like one, and
    // merging would silently corrupt the command. A real continuation is always
    // written `command \` / `command ` + backtick.
    [GeneratedRegex(@"(?:(?<=[ \t])[\\`]|&&|\|\|?)[ \t]*$")]
    private static partial Regex ContinuationMarker();

    [GeneratedRegex(@"(?<=[ \t])[\\`][ \t]*$")]
    private static partial Regex ConsumedMarker();

    // bash heredoc: <<EOF / <<-EOF / <<'EOF' / <<"EOF"
    [GeneratedRegex(@"<<-?\s*(?<q>['""]?)(?<tag>[A-Za-z_][A-Za-z0-9_]*)\k<q>")]
    private static partial Regex Heredoc();

    /// <summary>
    /// Removes the whitespace prefix shared by every non-blank line, and drops
    /// blank lines at both ends. Relative indentation is untouched, so Python,
    /// YAML and here-strings survive. A trailing newline is dropped too — it
    /// would otherwise make a terminal run the last line the moment you paste.
    /// </summary>
    /// <param name="ignoreFirstLineIndent">
    /// Dedent a second time over lines 2..n only. For the case where a mouse
    /// selection started at the first character of line 1 and so missed the left
    /// margin that every following line carries — the common prefix is then empty
    /// and an ordinary dedent can do nothing.
    ///
    /// This is destructive by design: on <c>class A:</c> followed by an indented
    /// body it flattens code that was correct. Nothing may turn it on by itself;
    /// only the user, who knows how the text was selected, may ask for it.
    /// </param>
    public static string Dedent(string input, bool ignoreFirstLineIndent = false)
    {
        (List<string> lines, string eol) = SplitLines(input);
        if (lines.Count == 0) return "";

        StripCommonIndent(lines);
        if (ignoreFirstLineIndent) StripCommonIndent(lines, from: 1);
        return string.Join(eol, lines);
    }

    /// <summary>
    /// <see cref="Dedent"/>, then join continuation lines into single commands.
    /// A trailing `\`, backtick, `&amp;&amp;` or `|` only counts as a continuation
    /// when it sits at bracket depth zero, outside any quoted string, and outside
    /// a here-string — in code such a marker is nearly always something else
    /// (<c>if (a &amp;&amp;</c>, <c>do { \</c>).
    /// </summary>
    /// <param name="ignoreFirstLineIndent">See <see cref="Dedent"/>.</param>
    public static string Normalize(string input, bool ignoreFirstLineIndent = false)
    {
        (List<string> lines, string eol) = SplitLines(input);
        if (lines.Count == 0) return "";

        StripCommonIndent(lines);
        if (ignoreFirstLineIndent) StripCommonIndent(lines, from: 1);
        return string.Join(eol, JoinContinuations(lines));
    }

    // ------------------------------------------------------------------ splitting

    /// Splits on any line ending, remembers which one the input used, and trims
    /// blank lines from both ends.
    private static (List<string> Lines, string Eol) SplitLines(string input)
    {
        string eol = "\n";
        int lf = input.IndexOf('\n');
        int cr = input.IndexOf('\r');
        if (cr >= 0 && (lf < 0 || cr < lf))
            eol = cr + 1 < input.Length && input[cr + 1] == '\n' ? "\r\n" : "\r";

        string[] raw = input.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        int first = 0, last = raw.Length - 1;
        while (first <= last && string.IsNullOrWhiteSpace(raw[first])) first++;
        while (last >= first && string.IsNullOrWhiteSpace(raw[last])) last--;

        return first > last
            ? ([], eol)
            : (raw[first..(last + 1)].ToList(), eol);
    }

    // -------------------------------------------------------------------- dedent

    /// <param name="from">
    /// First line to consider. Only ever 1, for the "the selection missed the
    /// left margin on line 1" case — see <see cref="Dedent"/>.
    /// </param>
    private static void StripCommonIndent(List<string> lines, int from = 0)
    {
        string? common = null;
        for (int i = from; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string indent = line[..IndentLength(line)];
            common = common is null ? indent : CommonPrefix(common, indent);
            if (common.Length == 0) return;
        }
        if (string.IsNullOrEmpty(common)) return;

        for (int i = from; i < lines.Count; i++)
        {
            lines[i] = lines[i].StartsWith(common, StringComparison.Ordinal)
                ? lines[i][common.Length..]
                : lines[i].TrimStart();
        }
    }

    private static int IndentLength(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    private static string CommonPrefix(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return a[..i];
    }

    // --------------------------------------------------------------------- merge

    private static List<string> JoinContinuations(List<string> lines)
    {
        var result = new List<string>();
        var scan = new ScanState();
        string current = "";

        foreach (string line in lines)
        {
            // A blank line between a continuation marker and the rest of the
            // command is meaningless — and it is exactly what turned
            // `Get-Process |` into "An empty pipe element is not allowed."
            if (current.Length > 0 && string.IsNullOrWhiteSpace(line)) continue;

            bool continues = ScanLine(line, scan);

            // Nothing accumulated and nothing to accumulate: pass the line through
            // untouched, so trailing whitespace and blank lines survive.
            if (current.Length == 0 && !continues)
            {
                result.Add(line);
                continue;
            }

            string segment = ConsumedMarker().Replace(line, "").TrimEnd();
            current = current.Length == 0 ? segment : $"{current} {segment.TrimStart()}";

            if (!continues)
            {
                result.Add(current);
                current = "";
            }
        }
        if (current.Length > 0) result.Add(current);

        return result;
    }

    /// Carried from line to line: an unbalanced bracket, an unterminated quote or
    /// an open here-string all mean "we are inside something, do not join".
    private sealed class ScanState
    {
        public int Depth;
        public char Quote;        // '\0', '\'' or '"'
        public string? HereEnd;   // terminator while inside a here-string / heredoc
    }

    /// Updates <paramref name="s"/> for this line and reports whether the line
    /// ends with a continuation marker that is actually acting as one.
    private static bool ScanLine(string line, ScanState s)
    {
        if (s.HereEnd is not null)
        {
            if (line.Trim() == s.HereEnd) s.HereEnd = null;
            return false;
        }

        bool escaped = false;
        foreach (char c in line)
        {
            if (escaped) { escaped = false; continue; }

            if (s.Quote == '\'')
            {
                if (c == '\'') s.Quote = '\0';
                continue;
            }
            if (s.Quote == '"')
            {
                if (c is '`' or '\\') { escaped = true; continue; }
                if (c == '"') s.Quote = '\0';
                continue;
            }

            switch (c)
            {
                case '\'': case '"': s.Quote = c; break;
                case '(': case '[': case '{': s.Depth++; break;
                case ')': case ']': case '}': s.Depth--; break;
            }
        }

        if (s.Quote != '\0')
        {
            // An unterminated quote at end of line is either a PowerShell
            // here-string opener or a string that genuinely spans lines. Either
            // way there is nothing to join here.
            string trimmed = line.TrimEnd();
            if (trimmed.EndsWith("@\"", StringComparison.Ordinal)) { s.HereEnd = "\"@"; s.Quote = '\0'; }
            else if (trimmed.EndsWith("@'", StringComparison.Ordinal)) { s.HereEnd = "'@"; s.Quote = '\0'; }
            return false;
        }

        Match heredoc = Heredoc().Match(line);
        if (heredoc.Success)
        {
            s.HereEnd = heredoc.Groups["tag"].Value;
            return false;
        }

        return s.Depth == 0 && ContinuationMarker().IsMatch(line);
    }
}
