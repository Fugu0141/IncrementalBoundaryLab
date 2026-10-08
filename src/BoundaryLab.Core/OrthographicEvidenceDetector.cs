namespace BoundaryLab.Core;

internal static class OrthographicEvidenceDetector
{
    public static IReadOnlyList<SymbolEvidence> Find(string raw)
    {
        var result = new List<SymbolEvidence>();

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (InputSyntax.IsHardBoundary(c))
            {
                result.Add(new SymbolEvidence(
                    i, i + 1, c.ToString(), "HardBoundary",
                    0.99, true, "hard-boundary-symbol"));
                continue;
            }

            if (InputSyntax.IsNeutralSymbol(c))
            {
                result.Add(new SymbolEvidence(
                    i, i + 1, c.ToString(), "NeutralSymbol",
                    0.70, true, "neutral-symbol"));
                continue;
            }

            if (!InputSyntax.IsBindingSymbol(c))
                continue;

            var leftWord = i > 0 && InputSyntax.IsWordChar(raw[i - 1]);
            var rightWord = i + 1 < raw.Length && InputSyntax.IsWordChar(raw[i + 1]);
            var postfix = c is '#' or '+' && leftWord;

            if ((leftWord && rightWord) || postfix)
            {
                var start = ExpandLeft(raw, i - 1);
                var end = postfix && !rightWord
                    ? ExpandPostfix(raw, i)
                    : ExpandRight(raw, i + 1);

                if (rightWord)
                    end = FindJapaneseSuffixBoundary(raw, i + 1, end);

                result.Add(new SymbolEvidence(
                    start,
                    end,
                    raw[start..end],
                    "LatinBindingToken",
                    0.97,
                    true,
                    "binding-symbol-joins-latin-token"));
            }
            else if (leftWord && i == raw.Length - 1)
            {
                var start = ExpandLeft(raw, i - 1);
                result.Add(new SymbolEvidence(
                    start,
                    i + 1,
                    raw[start..(i + 1)],
                    "IncompleteBindingToken",
                    0.72,
                    false,
                    "binding-symbol-awaiting-right-context"));
            }
            else
            {
                result.Add(new SymbolEvidence(
                    i, i + 1, c.ToString(), "BindingSymbol",
                    0.55, false, "binding-symbol-without-complete-token"));
            }
        }

        return MergeDuplicateLatinSpans(result);
    }

    private static int ExpandLeft(string raw, int index)
    {
        var i = index;
        while (i > 0)
        {
            var previous = raw[i - 1];
            if (!InputSyntax.IsWordChar(previous) &&
                !InputSyntax.IsBindingSymbol(previous))
                break;
            i--;
        }
        return i;
    }

    private static int ExpandRight(string raw, int index)
    {
        var i = index;
        while (i < raw.Length)
        {
            var c = raw[i];
            if (!InputSyntax.IsWordChar(c) &&
                !InputSyntax.IsBindingSymbol(c))
                break;
            i++;
        }
        return i;
    }

    private static int ExpandPostfix(string raw, int symbolIndex)
    {
        var end = symbolIndex + 1;
        while (end < raw.Length && raw[end] is '#' or '+')
            end++;
        return end;
    }

    private static int FindJapaneseSuffixBoundary(
        string raw,
        int rightStart,
        int rawEnd)
    {
        for (var split = rightStart + 1; split <= rawEnd - 2; split++)
        {
            var latinPart = raw[rightStart..split];
            var suffix = raw[split..rawEnd];

            if (!latinPart.Any(char.IsAsciiLetter) ||
                !suffix.All(char.IsAsciiLetter) ||
                !RomajiConverter.TryConvert(suffix, out _))
                continue;

            var latinProjection = PhoneticProjector.Project(latinPart);
            if (latinProjection.UnresolvedRatio >= 0.20)
                return split;
        }

        return rawEnd;
    }

    private static IReadOnlyList<SymbolEvidence> MergeDuplicateLatinSpans(
        IReadOnlyList<SymbolEvidence> source)
    {
        var result = new List<SymbolEvidence>();

        foreach (var item in source
                     .OrderBy(e => e.Start)
                     .ThenByDescending(e => e.End))
        {
            if (item.Kind == "LatinBindingToken" &&
                result.Any(e =>
                    e.Kind == item.Kind &&
                    e.Start <= item.Start &&
                    e.End >= item.End))
                continue;

            result.Add(item);
        }

        return result;
    }
}
