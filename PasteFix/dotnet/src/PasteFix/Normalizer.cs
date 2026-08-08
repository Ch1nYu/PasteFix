using System.Text.RegularExpressions;

namespace PasteFix;

public static partial class Normalizer
{
    [GeneratedRegex(@"^```[a-zA-Z0-9_-]*\s*$")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^[>$]\s+")]
    private static partial Regex ShellPrompt();

    [GeneratedRegex(@"^PS[^\n]*>\s*")]
    private static partial Regex PowerShellPrompt();

    // Must stay in sync with ShellPrompt/PowerShellPrompt: a `>`/`$` is only a prompt
    // when followed by whitespace. A zero-width `\s*` here made PowerShell variables
    // ($a, $b) count as prompts, wrongly flipping multiPrompt on.
    [GeneratedRegex(@"^(?:[>$]\s+|PS[^\n]*>\s*)")]
    private static partial Regex AnyPrompt();

    [GeneratedRegex(@"\\\s*$")]
    private static partial Regex BackslashContinuation();

    [GeneratedRegex(@"&&\s*$")]
    private static partial Regex AndAndContinuation();

    // BUG-3: web copies are often LF-only; Windows terminal paste needs CRLF.
    // This is safe for ANY content (no semantic change), so it runs even when
    // the input is not command-shaped.
    public static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    public static string Normalize(string input)
    {
        // BUG-3: input may be LF (web copy) or CRLF. Output is always CRLF for
        // Windows clipboard / terminal paste compatibility.
        const string EOL = "\r\n";
        string[] lines = input.Replace("\r\n", "\n").Split('\n');

        int first = 0, last = lines.Length - 1;
        while (first < lines.Length && string.IsNullOrWhiteSpace(lines[first])) first++;
        while (last > first && string.IsNullOrWhiteSpace(lines[last])) last--;
        if (first >= lines.Length || first > last) return "";

        List<string> list = lines[first..(last + 1)].ToList();

        if (FenceLine().IsMatch(list[0]))
        {
            list.RemoveAt(0);
            if (list.Count > 0 && list[^1].Trim() == "```") list.RemoveAt(list.Count - 1);
        }

        string[] nonEmpty = list.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (nonEmpty.Length == 0) return "";

        int commonIndent = nonEmpty.Min(l => l.TakeWhile(char.IsWhiteSpace).Count());
        list = list.Select(l => l.Length >= commonIndent ? l[commonIndent..] : l).ToList();

        int promptCount = nonEmpty.Count(l => AnyPrompt().IsMatch(l.TrimStart()));
        bool multiPrompt = promptCount * 2 >= nonEmpty.Length;

        list = list
            .Select(l =>
            {
                string t = l.TrimStart();
                Match ps = PowerShellPrompt().Match(t);
                if (ps.Success)
                    return t[ps.Length..];

                // BUG-1 & BUG-4: only strip shell prompt when multiPrompt protects it
                if (multiPrompt && ShellPrompt().IsMatch(t))
                    return ShellPrompt().Replace(t, "");

                return l; // preserve original indent / content
            })
            .Select(l => l.TrimEnd())
            // BUG-2: only remove # lines in multiPrompt command context
            .Where(l => !(multiPrompt && l.TrimStart().StartsWith('#')))
            .ToList();

        if (multiPrompt)
        {
            return string.Join(EOL, list.Where(l => l.Length > 0));
        }

        // BUG-5: continuation merge is a shell transform. Since BUG-1/BUG-4 made the
        // non-multiPrompt branch the default path for arbitrary code, a trailing
        // `&&` / `\` no longer implies a shell continuation — in code it usually sits
        // inside an unclosed bracket (`if (a &&`, `do { \`). Only merge while bracket
        // depth is zero; otherwise emit the line verbatim.
        var joined = new List<string>();
        string current = "";
        int depth = 0;
        foreach (string line in list)
        {
            bool insideBrackets = depth != 0;
            depth += BracketDelta(line);

            bool continues = BackslashContinuation().IsMatch(line) || AndAndContinuation().IsMatch(line);
            string segment = BackslashContinuation().Replace(line, "").TrimEnd();
            bool mergeable = !insideBrackets && depth == 0;

            if (current.Length == 0 && !mergeable)
            {
                joined.Add(line);
                continue;
            }

            // Continuation lines keep their own indentation in the source; collapse it
            // so the merged command has single spaces between arguments.
            current = current.Length == 0 ? segment : $"{current} {segment.TrimStart()}";
            if (!continues || !mergeable)
            {
                joined.Add(current);
                current = "";
            }
        }
        if (current.Length > 0) joined.Add(current);

        return string.Join(EOL, joined);
    }

    private static int BracketDelta(string line)
    {
        int delta = 0;
        foreach (char c in line)
        {
            if (c is '(' or '[' or '{') delta++;
            else if (c is ')' or ']' or '}') delta--;
        }
        return delta;
    }
}
