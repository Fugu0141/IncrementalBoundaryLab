namespace BoundaryLab.Core;

internal sealed record EvidenceLatticeDecodeResult(
    IReadOnlyList<LatticeEdge> CandidateEdges,
    IReadOnlyList<LatticePathSnapshot> TopPaths,
    int ExpandedEdges);

internal sealed class EvidenceLatticeDecoder
{
    private sealed record PathState(
        double Score,
        IReadOnlyList<LatticeEdge> Edges);

    private readonly EvidenceLatticeParameters _parameters;

    public EvidenceLatticeDecoder(EvidenceLatticeParameters parameters)
    {
        _parameters = parameters;
    }

    public EvidenceLatticeDecodeResult Decode(string raw)
    {
        if (raw.Length == 0)
            return new EvidenceLatticeDecodeResult([], [], 0);

        var allEdges = GenerateEdges(raw);
        var byStart = allEdges
            .GroupBy(e => e.Start)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(e => e.LocalScore)
                    .ToArray());

        var states = new List<PathState>[raw.Length + 1];
        for (var i = 0; i < states.Length; i++)
            states[i] = [];

        states[0].Add(new PathState(0, []));
        var expanded = 0;

        for (var position = 0; position < raw.Length; position++)
        {
            if (states[position].Count == 0)
                continue;

            var incoming = states[position]
                .OrderByDescending(s => s.Score)
                .Take(_parameters.BeamWidth)
                .ToArray();

            if (!byStart.TryGetValue(position, out var edges))
                continue;

            foreach (var state in incoming)
            {
                var previous = state.Edges.Count == 0
                    ? null
                    : state.Edges[^1];

                foreach (var edge in edges)
                {
                    expanded++;
                    var score =
                        state.Score +
                        edge.LocalScore +
                        TransitionScore(previous, edge);

                    var path = new PathState(
                        score,
                        state.Edges.Append(edge).ToArray());

                    states[edge.End].Add(path);

                    if (states[edge.End].Count >
                        _parameters.BeamWidth * 5)
                    {
                        states[edge.End] = states[edge.End]
                            .OrderByDescending(s => s.Score)
                            .Take(_parameters.BeamWidth * 2)
                            .ToList();
                    }
                }
            }
        }

        var finals = states[raw.Length]
            .OrderByDescending(s => s.Score)
            .Take(_parameters.BeamWidth)
            .ToArray();

        if (finals.Length == 0)
        {
            var unknown = raw.Select((c, i) =>
                UnknownEdge(raw, i)).ToArray();

            finals =
            [
                new PathState(
                    unknown.Sum(e => e.LocalScore),
                    unknown)
            ];
        }

        var best = finals[0].Score;
        var snapshots = finals.Select(state =>
            new LatticePathSnapshot(
                state.Score,
                state.Score - best,
                string.Join(" | ", state.Edges.Select(e => e.Raw)),
                string.Concat(state.Edges.Select(e => e.Output)),
                state.Edges))
            .ToArray();

        return new EvidenceLatticeDecodeResult(
            allEdges,
            snapshots,
            expanded);
    }

    private IReadOnlyList<LatticeEdge> GenerateEdges(string raw)
    {
        var edges = new List<LatticeEdge>();

        AddStructuralLatinEdges(raw, edges);

        for (var start = 0; start < raw.Length; start++)
        {
            var c = raw[start];

            if (InputSyntax.IsSymbol(c) || c == ' ')
            {
                edges.Add(SymbolEdge(raw, start));
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c))
            {
                edges.Add(UnknownEdge(raw, start));
                continue;
            }

            foreach (var entry in Lexicon.ExactAt(raw, start))
            {
                var end = start + entry.Raw.Length;
                var (ja, en) = LanguageProfileScorer.Score(entry.Raw);

                if (entry.Language == LanguageKind.Japanese)
                {
                    var particle =
                        entry.Evidence == "japanese-particle";
                    var bonus = particle ? 0.45 : 0.95;

                    edges.Add(new LatticeEdge(
                        start,
                        end,
                        entry.Raw,
                        entry.Converted,
                        LanguageKind.Japanese,
                        LatticeEdgeKind.JapaneseLexical,
                        entry.Raw.Length -
                        0.45 +
                        bonus +
                        entry.Weight * 0.10 +
                        LanguageProfileScorer.Advantage(
                            entry.Raw,
                            LanguageKind.Japanese) * 0.35,
                        ja,
                        en,
                        entry.Evidence));
                }
                else
                {
                    var shortKanaReadablePenalty =
                        entry.Raw.Length < 5 &&
                        RomajiConverter.TryConvert(
                            entry.Raw,
                            out _)
                            ? 2.35
                            : 0.0;

                    var exactBonus =
                        entry.Raw.Length >= 5 ? 1.80 : 0.20;

                    edges.Add(new LatticeEdge(
                        start,
                        end,
                        entry.Raw,
                        entry.Converted,
                        LanguageKind.English,
                        LatticeEdgeKind.EnglishLexical,
                        entry.Raw.Length -
                        0.45 +
                        exactBonus +
                        entry.Weight * 0.10 +
                        LanguageProfileScorer.Advantage(
                            entry.Raw,
                            LanguageKind.English) * 0.50 -
                        shortKanaReadablePenalty,
                        ja,
                        en,
                        entry.Evidence));
                }
            }

            if (char.IsAsciiLetter(c))
            {
                var maxEnd = Math.Min(
                    raw.Length,
                    start + _parameters.MaxPhoneticSpan);

                for (var end = start + 1; end <= maxEnd; end++)
                {
                    var span = raw[start..end];
                    if (!span.All(char.IsAsciiLetter))
                        break;

                    if (!RomajiConverter.TryConvert(span, out var kana))
                        continue;

                    var (ja, en) =
                        LanguageProfileScorer.Score(span);
                    var advantage =
                        LanguageProfileScorer.Advantage(
                            span,
                            LanguageKind.Japanese);

                    edges.Add(new LatticeEdge(
                        start,
                        end,
                        span,
                        kana,
                        LanguageKind.Japanese,
                        LatticeEdgeKind.JapanesePhonetic,
                        span.Length -
                        0.55 +
                        advantage * 0.42,
                        ja,
                        en,
                        "phonetic-lattice"));
                }
            }

            if (!edges.Any(e => e.Start == start))
                edges.Add(UnknownEdge(raw, start));
        }

        return edges
            .GroupBy(e =>
                (e.Start, e.End, e.Output, e.Language, e.Kind))
            .Select(g =>
                g.OrderByDescending(e => e.LocalScore).First())
            .OrderBy(e => e.Start)
            .ThenByDescending(e => e.End - e.Start)
            .ThenByDescending(e => e.LocalScore)
            .ToArray();
    }

    private void AddStructuralLatinEdges(
        string raw,
        List<LatticeEdge> edges)
    {
        for (var symbol = 0; symbol < raw.Length; symbol++)
        {
            if (!InputSyntax.IsBindingSymbol(raw[symbol]))
                continue;

            var leftRunStart = symbol;
            while (leftRunStart > 0 &&
                   InputSyntax.IsWordChar(raw[leftRunStart - 1]))
                leftRunStart--;

            var rightRunEnd = symbol + 1;
            while (rightRunEnd < raw.Length &&
                   InputSyntax.IsWordChar(raw[rightRunEnd]))
                rightRunEnd++;

            var minLeft = Math.Max(
                leftRunStart,
                symbol - _parameters.MaxLatinSide);

            var hasRight =
                symbol + 1 < raw.Length &&
                InputSyntax.IsWordChar(raw[symbol + 1]);

            if (hasRight)
            {
                var maxRight = Math.Min(
                    rightRunEnd,
                    symbol + 1 + _parameters.MaxLatinSide);

                for (var start = minLeft; start < symbol; start++)
                {
                    if (!InputSyntax.IsWordChar(raw[start]))
                        continue;

                    for (var end = symbol + 2;
                         end <= maxRight;
                         end++)
                    {
                        AddStructuralCandidate(
                            raw,
                            start,
                            end,
                            edges);
                    }
                }
            }
            else if (
                raw[symbol] is '#' or '+' &&
                symbol > leftRunStart)
            {
                var end = symbol + 1;
                while (end < raw.Length &&
                       raw[end] == raw[symbol] &&
                       raw[symbol] == '+')
                    end++;

                for (var start = minLeft; start < symbol; start++)
                    AddStructuralCandidate(
                        raw,
                        start,
                        end,
                        edges);
            }
        }
    }

    private static void AddStructuralCandidate(
        string raw,
        int start,
        int end,
        List<LatticeEdge> edges)
    {
        if (end <= start ||
            end > raw.Length)
            return;

        var span = raw[start..end];
        if (!span.Any(char.IsAsciiLetter) ||
            !span.Any(InputSyntax.IsBindingSymbol))
            return;

        var (ja, en) = LanguageProfileScorer.Score(span);
        var advantage =
            LanguageProfileScorer.Advantage(
                span,
                LanguageKind.English);
        var symbolCount =
            span.Count(InputSyntax.IsBindingSymbol);

        var firstBinding = span
            .Select((c, i) => (c, i))
            .First(x => InputSyntax.IsBindingSymbol(x.c))
            .i;

        var leftPart = span[..firstBinding];
        var rightPart =
            firstBinding + 1 < span.Length
                ? span[(firstBinding + 1)..]
                : "";

        var leftAdvantage =
            LanguageProfileScorer.Advantage(
                leftPart,
                LanguageKind.English);
        var rightAdvantage =
            rightPart.Length == 0
                ? 0
                : LanguageProfileScorer.Advantage(
                    rightPart,
                    LanguageKind.English);

        var rightBoundaryBonus =
            end < raw.Length &&
            Lexicon.ExactAt(raw, end).Any(e =>
                e.Language == LanguageKind.Japanese &&
                e.Weight >= 5.1)
                ? 1.80
                : 0.0;

        var componentPenalty =
            leftPart.All(char.IsAsciiLetter) &&
            leftPart.Length <= 2
                ? 2.60
                : leftPart.All(char.IsAsciiLetter) &&
                  leftPart.Length == 3
                    ? 0.45
                    : 0.0;

        var score =
            span.Length -
            0.65 +
            1.75 +
            symbolCount * 0.55 +
            advantage * 0.25 +
            leftAdvantage * 0.95 +
            rightAdvantage * 0.65 +
            rightBoundaryBonus -
            componentPenalty -
            Math.Max(0, span.Length - 12) * 0.35;

        edges.Add(new LatticeEdge(
            start,
            end,
            span,
            span,
            LanguageKind.English,
            LatticeEdgeKind.LatinStructural,
            score,
            ja,
            en,
            "orthographic-lattice"));
    }

    private double TransitionScore(
        LatticeEdge? previous,
        LatticeEdge current)
    {
        var score = 0.0;

        if (previous is null)
        {
            if (IsJapaneseParticle(current))
                score -= 3.25;

            if (IsJapaneseSuffix(current))
                score -= 1.75;

            return score;
        }

        if (previous.Kind == LatticeEdgeKind.HardBoundary)
            return 0.35;

        if (current.Kind == LatticeEdgeKind.HardBoundary)
            return 0.45;

        if (previous.Language == current.Language &&
            current.Language != LanguageKind.Unknown)
            score += 0.12;

        if (previous.Language != current.Language &&
            previous.Language != LanguageKind.Unknown &&
            current.Language != LanguageKind.Unknown)
        {
            score -= _parameters.LanguageSwitchPenalty;
        }

        if (previous.Language == LanguageKind.English &&
            IsJapaneseParticle(current))
            score += 0.55;

        if (IsJapaneseParticle(previous) &&
            IsJapaneseParticle(current))
            score -= 1.20;

        if (current.Kind == LatticeEdgeKind.Unknown)
            score -= 1.0;

        return score;
    }

    private static bool IsJapaneseParticle(LatticeEdge edge) =>
        edge.Language == LanguageKind.Japanese &&
        edge.Evidence == "japanese-particle";

    private static bool IsJapaneseSuffix(LatticeEdge edge) =>
        edge.Language == LanguageKind.Japanese &&
        (
            edge.Evidence == "japanese-verb-suffix" ||
            edge.Evidence == "japanese-auxiliary"
        );

    private static LatticeEdge SymbolEdge(
        string raw,
        int start)
    {
        var symbol = raw[start].ToString();
        var c = raw[start];

        if (InputSyntax.IsHardBoundary(c) || c == ' ')
        {
            return new LatticeEdge(
                start,
                start + 1,
                symbol,
                symbol,
                LanguageKind.Unknown,
                LatticeEdgeKind.HardBoundary,
                1.0,
                -8,
                -8,
                "hard-boundary");
        }

        if (InputSyntax.IsBindingSymbol(c))
        {
            return new LatticeEdge(
                start,
                start + 1,
                symbol,
                symbol,
                LanguageKind.Unknown,
                LatticeEdgeKind.BindingSymbol,
                0.10,
                -8,
                -8,
                "standalone-binding-symbol");
        }

        return new LatticeEdge(
            start,
            start + 1,
            symbol,
            symbol,
            LanguageKind.Unknown,
            LatticeEdgeKind.NeutralSymbol,
            0.45,
            -8,
            -8,
            "neutral-symbol");
    }

    private static LatticeEdge UnknownEdge(
        string raw,
        int start)
    {
        var token = raw[start].ToString();

        return new LatticeEdge(
            start,
            start + 1,
            token,
            token,
            LanguageKind.Unknown,
            LatticeEdgeKind.Unknown,
            -3.2,
            -8,
            -8,
            "unknown-character");
    }
}
