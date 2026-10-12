using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PasteFix;

/// <summary>
/// The wrapper a copied block arrived in. These are frames around the text, not
/// part of it.
/// </summary>
public enum ContainerKind
{
    Fence,
    Prompt,
    LineNumber,
}

/// <summary>
/// Removes a wrapper that covers the whole copy: a markdown fence, a repeated
/// shell prompt on every line, or a line-number gutter.
///
/// A wrapper is only removed when it is provably uniform — the fence opens on
/// the first line and closes on the last with nothing fence-shaped in between;
/// every non-blank line carries the same prompt style; the numbers run in
/// sequence. Anything less is not a wrapper, it is prose that happens to
/// contain a command, and it is left exactly as copied.
///
/// This is not the line-by-line guessing removed on 2026-08-09. No rule here
/// decides which line is a command; the wrapper either covers everything or it
/// is not a wrapper. See docs/records/PLAN-2026-08-09.md.
/// </summary>
public static partial class ContainerStripper
{
    [GeneratedRegex(@"^(?<marker>`{3,}|~{3,})[^`~]*$")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^\s*PS [A-Za-z]:\\[^>]*>\s?")]
    private static partial Regex PowerShellPrompt();

    [GeneratedRegex(@"^\s*\$\s+")]
    private static partial Regex DollarPrompt();

    [GeneratedRegex(@"^\s*(?:❯|➜)\s+")]
    private static partial Regex ChevronPrompt();

    // The separator set deliberately excludes "." and ")". A gutter uses a tab,
    // a colon or a pipe; "." and ")" are how markdown writes an ordered list, and
    // "1. Run the installer / 2. Restart the shell" is prose that would come out
    // with its numbering eaten. No structural test can tell a list from a gutter,
    // so the list syntax is not treated as a gutter at all. Numbered code blocks
    // written as "1. " are therefore left alone — a visible miss the user can see
    // and undo, which is the trade this tool always takes over silent damage.
    [GeneratedRegex(@"^\s*(?<n>\d+)(?::|\||\t)\s*")]
    private static partial Regex LineNumberPrefix();

    /// <summary>
    /// Strips every wrapper the copy is wearing, outermost first, and reports
    /// which ones were removed. Text with no wrapper comes back unchanged.
    /// </summary>
    public static (string Text, IReadOnlyList<ContainerKind> Stripped) Strip(string input)
    {
        (List<string> lines, string eol) = Normalizer.SplitLines(input);
        var stripped = new List<ContainerKind>();
        if (lines.Count == 0) return ("", stripped);

        if (TryStripFence(lines, out List<string> inner))
        {
            stripped.Add(ContainerKind.Fence);
            lines = inner;
        }

        if (TryStripPrompt(lines, out inner))
        {
            stripped.Add(ContainerKind.Prompt);
            lines = inner;
        }

        if (TryStripLineNumbers(lines, out inner))
        {
            stripped.Add(ContainerKind.LineNumber);
            lines = inner;
        }

        // Nothing was a wrapper, so the copy is returned byte for byte rather
        // than re-joined from split lines.
        if (stripped.Count == 0) return (input, stripped);

        return (string.Join(eol, lines), stripped);
    }

    private static bool TryStripFence(List<string> lines, out List<string> inner)
    {
        inner = lines;
        if (lines.Count < 2) return false;

        Match open = FenceLine().Match(lines[0].TrimEnd());
        if (!open.Success) return false;

        string marker = open.Groups["marker"].Value;
        if (lines[^1].TrimEnd() != marker) return false;

        // A fence-shaped line inside means this is not one fence around the
        // whole copy — it is several blocks, or a document quoting code, and
        // removing the outer pair would leave broken structure behind.
        for (int i = 1; i < lines.Count - 1; i++)
        {
            if (FenceLine().IsMatch(lines[i].TrimEnd())) return false;
        }

        inner = lines.GetRange(1, lines.Count - 2);
        return true;
    }

    private static bool TryStripPrompt(List<string> lines, out List<string> inner)
    {
        inner = lines;
        if (lines.Count == 0) return false;

        Regex[] styles = [PowerShellPrompt(), DollarPrompt(), ChevronPrompt()];
        int style = -1;
        int prompted = 0;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            int match = Array.FindIndex(styles, s => s.IsMatch(line));
            if (match < 0) return false;

            if (style < 0) style = match;
            else if (style != match) return false;

            prompted++;
        }

        // A gutter is repeated down the side of a block; one line has no side.
        // "$ npm install" on its own is left alone, which is a visible miss
        // rather than a silent rewrite of a line that merely starts with "$ ".
        if (prompted < 2) return false;

        var result = new List<string>(lines.Count);
        foreach (string line in lines)
        {
            result.Add(string.IsNullOrWhiteSpace(line)
                ? line
                : styles[style].Replace(line, "", 1));
        }

        inner = result;
        return true;
    }

    private static bool TryStripLineNumbers(List<string> lines, out List<string> inner)
    {
        inner = lines;

        int? previous = null;
        int numbered = 0;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            Match match = LineNumberPrefix().Match(line);
            if (!match.Success) return false;

            int number = int.Parse(match.Groups["n"].Value);
            if (previous is not null && number != previous + 1) return false;

            previous = number;
            numbered++;
        }

        // One numbered line is a heading or a list of one; a gutter is a run.
        if (numbered < 2) return false;

        var result = new List<string>(lines.Count);
        foreach (string line in lines)
        {
            result.Add(string.IsNullOrWhiteSpace(line)
                ? line
                : LineNumberPrefix().Replace(line, "", 1));
        }

        inner = result;
        return true;
    }
}
