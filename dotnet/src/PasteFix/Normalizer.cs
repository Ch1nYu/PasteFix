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

    [GeneratedRegex(@"^(?:[>$]|PS[^\n]*>)\s*")]
    private static partial Regex AnyPrompt();

    [GeneratedRegex(@"\\\s*$")]
    private static partial Regex BackslashContinuation();

    [GeneratedRegex(@"&&\s*$")]
    private static partial Regex AndAndContinuation();

    public static string Normalize(string input)
    {
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
                return ps.Success ? t[ps.Length..] : ShellPrompt().Replace(t, "");
            })
            .Select(l => l.TrimEnd())
            .Where(l => !l.TrimStart().StartsWith('#'))
            .ToList();

        if (multiPrompt)
        {
            return string.Join("\n", list.Where(l => l.Length > 0));
        }

        var joined = new List<string>();
        string current = "";
        foreach (string line in list)
        {
            bool continues = BackslashContinuation().IsMatch(line);
            bool andAnd = AndAndContinuation().IsMatch(line);
            string segment = BackslashContinuation().Replace(line, "").TrimEnd();
            current = current.Length == 0 ? segment : $"{current} {segment}";
            if (!continues && !andAnd)
            {
                joined.Add(current);
                current = "";
            }
        }

        if (current.Length > 0) joined.Add(current);
        return string.Join("\n", joined);
    }
}
