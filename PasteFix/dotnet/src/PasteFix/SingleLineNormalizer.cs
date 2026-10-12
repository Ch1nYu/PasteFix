using System;
using System.Collections.Generic;
using System.Linq;

namespace PasteFix;

/// <summary>
/// Flattens physical lines into one command. This is an explicit destructive
/// mode for visually wrapped commands; structured multi-line text will not be
/// preserved.
/// </summary>
public static class SingleLineNormalizer
{
    public static string Flatten(string input)
    {
        List<string> lines = input
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        var scan = new Normalizer.ScanState();
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            bool isContinuation = Normalizer.ScanLine(line, scan);
            if (i < lines.Count - 1 && isContinuation && line[^1] is '\\' or '`')
                lines[i] = line[..^1].TrimEnd();
        }

        return string.Join(" ", lines);
    }
}
