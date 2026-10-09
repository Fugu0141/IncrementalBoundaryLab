namespace BoundaryLab.Core;

// Derive the four categories from actual competing v1 hypotheses.
// One pass over the selected paths; no second quadratic lexicon scan.
internal static class StreamHybridStructure
{
    public static PhoneticStructureAnalysis Analyze(
        string raw, IReadOnlyList<LatticePathSnapshot> paths)
    {
        if (raw.Length == 0 || paths.Count == 0)
            return new([], [], []);

        var best = paths[0].Edges;
        var boundaries = new Dictionary<int, PhoneticBoundaryEvidence>();
        var groups = new List<PhoneticGroupEvidence>();
        var risks = new List<(int Start, int End, string Reason)>();

        foreach (var path in paths)
        foreach (var edge in path.Edges)
        {
            if (edge.End < raw.Length)
            {
                var confirmed = edge.Kind == LatticeEdgeKind.HardBoundary;
                if (!boundaries.TryGetValue(edge.End, out var old) ||
                    confirmed && old.Status == PhoneticEvidenceStatus.Tentative)
                    boundaries[edge.End] = new(
                        edge.End,
                        confirmed ? PhoneticEvidenceStatus.Confirmed :
                            PhoneticEvidenceStatus.Tentative,
                        confirmed ? "explicit-delimiter" : "competing-path-cut");
            }
            if (edge.Kind == LatticeEdgeKind.HardBoundary && edge.Start > 0)
                boundaries[edge.Start] =
                    new(edge.Start, PhoneticEvidenceStatus.Confirmed,
                        "explicit-delimiter");
        }

        foreach (var edge in best)
        {
            if (edge.Kind is LatticeEdgeKind.Unknown or
                LatticeEdgeKind.LiteralFallback or
                LatticeEdgeKind.OpenPrefix or
                LatticeEdgeKind.BindingSymbol)
                risks.Add((edge.Start, edge.End, edge.Evidence));
            if (edge.Raw.Contains('.') || edge.Raw.Contains('-'))
                risks.Add((edge.Start, edge.End, "ambiguous-binding-symbol"));
        }

        var windows = new List<PhoneticReviewWindow>();
        foreach (var risk in risks.OrderBy(r => r.Start))
        {
            var begin = Math.Max(0, risk.Start - 6);
            var end = Math.Min(raw.Length, risk.End + 6);
            if (windows.Count > 0 && begin <= windows[^1].End)
            {
                var last = windows[^1];
                windows[^1] = last with { End = Math.Max(last.End, end) };
            }
            else
                windows.Add(new(begin, end, risk.Reason));
        }

        foreach (var edge in best)
        {
            var competing = paths.Skip(1).Any(p =>
                p.Edges.Any(e => e.Start < edge.End &&
                    edge.Start < e.End &&
                    (e.Language != edge.Language ||
                     e.End != edge.End || e.Start != edge.Start)));
            var review = windows.Any(w =>
                w.Start < edge.End && edge.Start < w.End);
            var sureReading = edge.Kind is
                LatticeEdgeKind.JapaneseLexical or
                LatticeEdgeKind.JapanesePhonetic or
                LatticeEdgeKind.EnglishLexical or
                LatticeEdgeKind.LatinStructural or
                LatticeEdgeKind.JapaneseMozc or
                LatticeEdgeKind.HardBoundary;
            var confirmed = sureReading && !competing && !review;
            groups.Add(new PhoneticGroupEvidence(
                edge.Start, edge.End, edge.Raw, edge.Output,
                edge.Kind.ToString(),
                confirmed ? PhoneticEvidenceStatus.Confirmed :
                            PhoneticEvidenceStatus.Tentative,
                confirmed ? "supported-reading-not-final-ime-commit" :
                            "unresolved-ownership-or-competing-boundary"));
        }

        return new(groups, boundaries.Values
            .OrderBy(x => x.Position).ToArray(), windows);
    }
}
