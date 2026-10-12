using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PasteFix;

/// <summary>
/// Turns a multi-line command copied from a chat window into something that can
/// be pasted into a terminal in one go. It does exactly two things:
///
///   <see cref="Dedent"/>   — remove the indent common to every line.
///   <see cref="Normalize"/> — dedent, then join continuation lines.
///
/// Dedent is structure-preserving. Normalize removes common indent, repairs
/// margins, and joins shell continuations, which AutoMonitor runs automatically
/// on clipboard copy.
/// </summary>
public static partial class Normalizer
{
    [GeneratedRegex(@"(?:(?<=[ \t])[\\`]|&&|\|\|?)[ \t]*$")]
    private static partial Regex ContinuationMarker();

    [GeneratedRegex(@"(?<=[ \t])[\\`][ \t]*$")]
    private static partial Regex ConsumedMarker();

    [GeneratedRegex(@"^(?:--?[a-zA-Z]|&&|\|\|?)[ \t]*")]
    private static partial Regex LeadingContinuation();

    [GeneratedRegex(@"<<-?\s*(?<q>['""]?)(?<tag>[A-Za-z_][A-Za-z0-9_]*)\k<q>")]
    private static partial Regex Heredoc();

    public static string Dedent(string input)
    {
        (List<string> lines, string eol) = SplitLines(input);
        if (lines.Count == 0) return "";

        StripCommonIndent(lines);
        return string.Join(eol, lines);
    }

    public static string Normalize(string input)
    {
        (List<string> lines, string eol) = SplitLines(input);
        if (lines.Count == 0) return "";

        StripCommonIndent(lines);
        if (FirstLineMissedTheMargin(lines)) StripCommonIndent(lines, from: 1);
        return string.Join(eol, JoinContinuations(lines));
    }

    public static (List<string> Lines, string Eol) SplitLines(string input)
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

    private static bool FirstLineMissedTheMargin(List<string> lines)
    {
        if (lines.Count < 2) return false;
        if (IndentLength(lines[0]) != 0) return false;

        var scan = new ScanState();
        if (ScanLine(lines[0], scan)) return false;
        if (scan.Depth != 0 || scan.Quote != '\0' || scan.HereEnd is not null) return false;

        char last = lines[0].TrimEnd() is { Length: > 0 } t ? t[^1] : '\0';
        if (last is ':' or '：') return false;

        return CommonIndent(lines, from: 1).Length > 0;
    }

    private static void StripCommonIndent(List<string> lines, int from = 0)
    {
        string common = CommonIndent(lines, from);
        if (common.Length == 0) return;

        for (int i = from; i < lines.Count; i++)
        {
            lines[i] = lines[i].StartsWith(common, StringComparison.Ordinal)
                ? lines[i][common.Length..]
                : lines[i].TrimStart();
        }
    }

    private static string CommonIndent(List<string> lines, int from)
    {
        string? common = null;
        for (int i = from; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string indent = line[..IndentLength(line)];
            common = common is null ? indent : CommonPrefix(common, indent);
            if (common.Length == 0) return "";
        }
        return common ?? "";
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

    private static List<string> JoinContinuations(List<string> lines)
    {
        var result = new List<string>();
        var scan = new ScanState();
        string current = "";

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (current.Length > 0 && string.IsNullOrWhiteSpace(line)) continue;

            bool endsWithContinuation = ScanLine(line, scan);

            // Check if NEXT line starts with a leading continuation (e.g. --flag, -f, | or &&)
            bool nextStartsContinuation = false;
            if (scan.Depth == 0 && scan.Quote == '\0' && scan.HereEnd is null)
            {
                char last = line.TrimEnd() is { Length: > 0 } t ? t[^1] : '\0';
                if (last is not (':' or '：' or ';' or '{' or '}'))
                {
                    for (int j = i + 1; j < lines.Count; j++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[j])) continue;
                        nextStartsContinuation = LeadingContinuation().IsMatch(lines[j].TrimStart());
                        break;
                    }
                }
            }

            bool continues = endsWithContinuation || nextStartsContinuation;

            if (current.Length == 0 && !continues)
            {
                result.Add(line);
                continue;
            }

            string segment = ConsumedMarker().Replace(line, "").TrimEnd();
            current = current.Length == 0 ? segment : current + " " + segment.TrimStart();

            if (!continues)
            {
                result.Add(current);
                current = "";
            }
        }
        if (current.Length > 0) result.Add(current);

        return result;
    }

    internal sealed class ScanState
    {
        public int Depth;
        public char Quote;
        public string? HereEnd;
    }

    internal static bool ScanLine(string line, ScanState s)
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
            string trimmed = line.TrimEnd();
            if (trimmed.EndsWith("@\"", StringComparison.Ordinal)) { s.HereEnd = "\"@"; s.Quote = '\0'; }
            else if (trimmed.EndsWith("@\'", StringComparison.Ordinal)) { s.HereEnd = "'@"; s.Quote = '\0'; }
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
