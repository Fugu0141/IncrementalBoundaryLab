namespace BoundaryLab.Core;

internal sealed record PhoneticResolution(
    IReadOnlyList<ResolvedSegment> Segments,
    IReadOnlyList<ResolutionCandidate> Candidates,
    IReadOnlyList<SymbolEvidence> SymbolEvidence);

internal sealed class PhoneticFirstResolver
{
    private readonly PhoneticFirstParameters _parameters;

    public PhoneticFirstResolver(PhoneticFirstParameters parameters)
    {
        _parameters = parameters;
    }

    public PhoneticResolution Resolve(
        string raw,
        PhoneticProjection projection)
    {
        if (raw.Length == 0)
            return new PhoneticResolution([], [], []);

        var candidates = new List<ResolutionCandidate>();
        var symbolEvidence = OrthographicEvidenceDetector.Find(raw);
        var anchors = SelectLatinAnchors(
            raw,
            projection,
            symbolEvidence,
            candidates);

        var segments = new List<ResolvedSegment>();
        var position = 0;

        foreach (var anchor in anchors)
        {
            if (anchor.Start > position)
                ResolveJapaneseGap(
                    raw, position, anchor.Start,
                    projection, segments, candidates);

            segments.Add(new ResolvedSegment(
                anchor.Start,
                anchor.End,
                anchor.Raw,
                anchor.Output,
                LanguageKind.English,
                anchor.Reason == "orthographic-binding-token"
                    ? 0.98
                    : Math.Clamp(
                        0.90 + (1.0 - anchor.PhoneticConfidence) * 0.08,
                        0, 0.99),
                false,
                anchor.Reason == "orthographic-binding-token"
                    ? "stage2-orthographic-latin-token"
                    : "stage2-english-from-phonetic-anomaly"));
            position = anchor.End;
        }

        if (position < raw.Length)
            ResolveJapaneseGap(
                raw, position, raw.Length,
                projection, segments, candidates);

        return new PhoneticResolution(
            segments,
            candidates,
            symbolEvidence);
    }

    private IReadOnlyList<ResolutionCandidate> SelectLatinAnchors(
        string raw,
        PhoneticProjection projection,
        IReadOnlyList<SymbolEvidence> symbolEvidence,
        List<ResolutionCandidate> allCandidates)
    {
        var anchors = new List<ResolutionCandidate>();

        foreach (var evidence in symbolEvidence
                     .Where(e => e.Kind == "LatinBindingToken" && e.Complete))
        {
            var candidate = new ResolutionCandidate(
                evidence.Start,
                evidence.End,
                evidence.Raw,
                evidence.Raw,
                LanguageKind.English,
                9.0 + evidence.Raw.Length * 0.05,
                0,
                evidence.Confidence,
                "orthographic-binding-token");
            allCandidates.Add(candidate);
            anchors.Add(candidate);
        }

        for (var start = 0; start < raw.Length; start++)
        {
            foreach (var entry in Lexicon.ExactAt(raw, start)
                         .Where(e => e.Language == LanguageKind.English))
            {
                var end = start + entry.Raw.Length;
                var isolatedProjection = PhoneticProjector.Project(entry.Raw);
                var readability = PhoneticProjector.Readability(
                    isolatedProjection,
                    0,
                    entry.Raw.Length);
                var anomaly = 1.0 - readability;
                var lexical = WeightToConfidence(entry.Weight);
                var score = lexical * 2.0 +
                            anomaly * 2.5 +
                            entry.Raw.Length * 0.03;

                var candidate = new ResolutionCandidate(
                    start,
                    end,
                    entry.Raw,
                    entry.Converted,
                    LanguageKind.English,
                    score,
                    readability,
                    lexical,
                    readability <= _parameters.EnglishAnomalyThreshold
                        ? "english-dictionary + phonetic-anomaly"
                        : "english-dictionary but kana-readable");

                allCandidates.Add(candidate);

                if (readability <= _parameters.EnglishAnomalyThreshold)
                    anchors.Add(candidate);
            }
        }

        var chosen = new List<ResolutionCandidate>();
        foreach (var candidate in anchors
                     .OrderByDescending(c => c.Reason == "orthographic-binding-token")
                     .ThenByDescending(c => c.Score)
                     .ThenByDescending(c => c.End - c.Start))
        {
            if (chosen.Any(x => Overlaps(x, candidate)))
                continue;

            chosen.Add(candidate);
        }

        return chosen.OrderBy(c => c.Start).ToArray();
    }

    private void ResolveJapaneseGap(
        string raw,
        int start,
        int end,
        PhoneticProjection projection,
        List<ResolvedSegment> output,
        List<ResolutionCandidate> candidates)
    {
        var position = start;

        while (position < end)
        {
            if (!char.IsAsciiLetter(raw[position]))
            {
                var literal = raw[position].ToString();
                output.Add(new ResolvedSegment(
                    position,
                    position + 1,
                    literal,
                    literal,
                    LanguageKind.Unknown,
                    InputSyntax.IsHardBoundary(raw[position]) ? 0.99 : 0.70,
                    false,
                    InputSyntax.IsHardBoundary(raw[position])
                        ? "stage2-hard-boundary-symbol"
                        : "stage2-symbol"));
                position++;
                continue;
            }

            var exact = Lexicon.ExactAt(raw, position)
                .Where(e => e.Language == LanguageKind.Japanese &&
                            position + e.Raw.Length <= end)
                .OrderByDescending(e => e.Raw.Length)
                .ThenByDescending(e => e.Weight)
                .FirstOrDefault();

            if (exact is not null)
            {
                var exactEnd = position + exact.Raw.Length;
                var readability = PhoneticProjector.Readability(
                    projection,
                    position,
                    exactEnd);
                var confidence =
                    exact.Evidence == "japanese-romaji-alias" ? 0.92 : 0.96;

                candidates.Add(new ResolutionCandidate(
                    position,
                    exactEnd,
                    exact.Raw,
                    exact.Converted,
                    LanguageKind.Japanese,
                    exact.Weight,
                    readability,
                    WeightToConfidence(exact.Weight),
                    exact.Evidence));

                output.Add(new ResolvedSegment(
                    position,
                    exactEnd,
                    exact.Raw,
                    exact.Converted,
                    LanguageKind.Japanese,
                    confidence,
                    false,
                    $"stage2-{exact.Evidence}"));
                position = exactEnd;
                continue;
            }

            var nextKnown = FindNextStrongJapaneseLexicalStart(
                raw, position, end);
            var nextSymbol = FindNextSymbol(raw, position, end);

            var fallbackEnd = end;
            if (nextKnown > position)
                fallbackEnd = Math.Min(fallbackEnd, nextKnown);
            if (nextSymbol > position)
                fallbackEnd = Math.Min(fallbackEnd, nextSymbol);

            fallbackEnd = Math.Min(
                fallbackEnd,
                position + _parameters.MaximumJapaneseFallbackLength);

            var found = false;
            for (var candidateEnd = fallbackEnd;
                 candidateEnd > position;
                 candidateEnd--)
            {
                var span = raw.Substring(
                    position,
                    candidateEnd - position);

                if (!span.All(char.IsAsciiLetter) ||
                    !RomajiConverter.TryConvert(span, out var kana))
                    continue;

                var readability = PhoneticProjector.Readability(
                    projection,
                    position,
                    candidateEnd);

                var boundarySupported = candidateEnd < end &&
                    Lexicon.ExactAt(raw, candidateEnd)
                        .Any(e =>
                            e.Language == LanguageKind.Japanese &&
                            e.Evidence != "japanese-particle");

                var confidence = boundarySupported ? 0.89 : 0.80;
                candidates.Add(new ResolutionCandidate(
                    position,
                    candidateEnd,
                    span,
                    kana,
                    LanguageKind.Japanese,
                    confidence,
                    readability,
                    0.35,
                    boundarySupported
                        ? "phonetic-fallback + strong-lexical-boundary"
                        : "phonetic-fallback"));

                output.Add(new ResolvedSegment(
                    position,
                    candidateEnd,
                    span,
                    kana,
                    LanguageKind.Japanese,
                    confidence,
                    false,
                    boundarySupported
                        ? "stage2-phonetic-fallback-boundary-supported"
                        : "stage2-phonetic-fallback"));
                position = candidateEnd;
                found = true;
                break;
            }

            if (found)
                continue;

            var unresolvedEnd = position + 1;
            var rawUnit = raw[position].ToString();
            candidates.Add(new ResolutionCandidate(
                position,
                unresolvedEnd,
                rawUnit,
                rawUnit,
                LanguageKind.Unknown,
                -2,
                PhoneticProjector.Readability(
                    projection,
                    position,
                    unresolvedEnd),
                0,
                "unresolved-after-stage2"));

            output.Add(new ResolvedSegment(
                position,
                unresolvedEnd,
                rawUnit,
                rawUnit,
                LanguageKind.Unknown,
                0.15,
                false,
                "stage2-unresolved"));
            position = unresolvedEnd;
        }
    }

    private static int FindNextStrongJapaneseLexicalStart(
        string raw,
        int start,
        int end)
    {
        for (var position = start + 1;
             position < end;
             position++)
        {
            if (!char.IsAsciiLetter(raw[position]))
                return position;

            var matches = Lexicon.ExactAt(raw, position)
                .Where(e =>
                    e.Language == LanguageKind.Japanese &&
                    e.Evidence != "japanese-particle" &&
                    position + e.Raw.Length <= end)
                .ToArray();

            if (matches.Any(e =>
                e.Raw.Length >= 3 &&
                e.Weight >= 5.2))
                return position;
        }

        return -1;
    }

    private static int FindNextSymbol(
        string raw,
        int start,
        int end)
    {
        for (var position = start + 1;
             position < end;
             position++)
        {
            if (!char.IsAsciiLetter(raw[position]))
                return position;
        }

        return -1;
    }

    private static bool Overlaps(
        ResolutionCandidate left,
        ResolutionCandidate right) =>
        left.Start < right.End &&
        right.Start < left.End;

    private static double WeightToConfidence(double weight) =>
        Math.Clamp((weight - 3.5) / 3.5, 0, 1);
}
