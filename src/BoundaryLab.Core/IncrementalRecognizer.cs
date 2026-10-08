namespace BoundaryLab.Core;

public sealed class IncrementalRecognizer
{
    public const string AlgorithmVersion = "iblab-consensus-v0.2";
    public RecognizerParameters Parameters { get; }

    public IncrementalRecognizer(RecognizerParameters? parameters = null)
    {
        Parameters = parameters ?? new RecognizerParameters();
    }

    public static bool IsValidInput(string input) =>
        input.All(char.IsAsciiLetter);

    public AnalysisResult Analyze(string input)
    {
        if (!IsValidInput(input))
            throw new ArgumentException("Input must contain ASCII letters only.", nameof(input));

        input = input.ToLowerInvariant();
        if (input.Length == 0)
            return new AnalysisResult("", "", [], [], []);

        var frames = new List<IncrementalFrame>(input.Length);
        for (var length = 1; length <= input.Length; length++)
            frames.Add(AnalyzePrefix(input[..length], frames));

        var last = frames[^1];
        return new AnalysisResult(input, last.Converted, last.Segments, last.Boundaries, frames);
    }

    private IncrementalFrame AnalyzePrefix(
        string prefix,
        IReadOnlyList<IncrementalFrame> previousFrames)
    {
        var hypotheses = BeamSearch(prefix);
        var probabilities = Normalize(hypotheses.Select(h => h.Score).ToArray());

        var boundaries = BuildBoundaries(
            prefix,
            hypotheses,
            probabilities,
            previousFrames);

        var ranked = hypotheses
            .Select((hypothesis, index) =>
            {
                var boundaryAgreement = BoundaryAgreement(hypothesis, prefix.Length, boundaries);
                var lexicalAgreement = hypothesis.Segments.Count == 0
                    ? 0
                    : hypothesis.Segments.Average(EvidenceSupport);
                var unknownPenalty = hypothesis.Segments.Count(s => s.Language == LanguageKind.Unknown) * 0.55;
                var ensembleScore =
                    Math.Log(Math.Max(probabilities[index], 1e-12)) +
                    boundaryAgreement * 1.45 +
                    lexicalAgreement * 0.90 -
                    unknownPenalty;

                return new RankedHypothesis(
                    hypothesis,
                    probabilities[index],
                    ensembleScore,
                    boundaryAgreement,
                    lexicalAgreement);
            })
            .OrderByDescending(x => x.EnsembleScore)
            .ThenByDescending(x => x.BeamProbability)
            .ToArray();

        var best = ranked[0];
        var segments = BuildSegments(
            prefix,
            best.Hypothesis,
            hypotheses,
            probabilities,
            boundaries,
            previousFrames);

        var snapshots = ranked
            .Select(Snapshot)
            .ToArray();

        var entropy = probabilities
            .Where(p => p > 0)
            .Sum(p => -p * Math.Log2(p));

        return new IncrementalFrame(
            prefix.Length,
            prefix,
            string.Join(" | ", best.Hypothesis.Segments.Select(s => s.Raw)),
            string.Concat(best.Hypothesis.Segments.Select(s => s.Converted)),
            best.BeamProbability,
            best.EnsembleScore,
            entropy,
            segments,
            boundaries,
            snapshots);
    }

    private List<Hypothesis> BeamSearch(string input)
    {
        var beams = new List<Hypothesis>[input.Length + 1];
        for (var i = 0; i < beams.Length; i++)
            beams[i] = [];

        beams[0].Add(new Hypothesis(0, []));

        for (var position = 0; position < input.Length; position++)
        {
            if (beams[position].Count == 0)
                continue;

            var candidates = Candidates(input, position).ToArray();
            foreach (var hypothesis in beams[position])
            {
                foreach (var candidate in candidates)
                {
                    var transition = TransitionScore(hypothesis.Segments.LastOrDefault(), candidate);
                    var score = hypothesis.Score + candidate.LexicalScore + transition - 0.55;
                    var next = new Hypothesis(score, [.. hypothesis.Segments, candidate]);
                    AddBeam(beams[candidate.End], next);
                }
            }
        }

        var result = beams[input.Length]
            .OrderByDescending(h => h.Score)
            .Take(Parameters.BeamWidth)
            .ToList();

        if (result.Count == 0)
        {
            var segment = new CandidateSegment(
                0, input.Length, input, input, LanguageKind.Unknown,
                false, "emergency-fallback", -8);
            result.Add(new Hypothesis(-8, [segment]));
        }

        return result;
    }

    private IEnumerable<CandidateSegment> Candidates(string input, int start)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in Lexicon.ExactAt(input, start))
        {
            var end = start + entry.Raw.Length;
            var key = $"{end}:{entry.Language}:{entry.Converted}:1";
            if (!emitted.Add(key))
                continue;

            yield return new CandidateSegment(
                start, end, entry.Raw, entry.Converted, entry.Language,
                true, entry.Evidence, entry.Weight);
        }

        var remaining = input.Length - start;
        var max = Math.Min(Parameters.MaxFallbackLength, remaining);
        for (var len = 2; len <= max; len++)
        {
            var raw = input.Substring(start, len);
            if (!RomajiConverter.TryConvert(raw, out var kana))
                continue;

            var end = start + len;
            var key = $"{end}:{LanguageKind.Japanese}:{kana}:1";
            if (!emitted.Add(key))
                continue;

            var score = 0.15 + len * 0.055;
            yield return new CandidateSegment(
                start, end, raw, kana, LanguageKind.Japanese,
                true, "romaji-fallback", score);
        }

        var tail = input[start..];
        var prefixMatches = Lexicon.PrefixMatches(tail);
        if (prefixMatches.Count > 0)
        {
            var languages = prefixMatches.Select(e => e.Language).Distinct().ToArray();
            var language = languages.Length == 1 ? languages[0] : LanguageKind.Unknown;
            var converted = language == LanguageKind.Japanese &&
                            RomajiConverter.TryConvert(tail, out var kana)
                ? kana
                : tail;

            var maxWeight = prefixMatches.Max(e => e.Weight);
            var score = 0.75 + tail.Length * 0.11 + maxWeight * 0.05;
            var key = $"{input.Length}:{language}:{converted}:0";
            if (emitted.Add(key))
            {
                yield return new CandidateSegment(
                    start, input.Length, tail, converted, language,
                    false, "lexicon-prefix", score);
            }
        }

        var one = input[start].ToString();
        yield return new CandidateSegment(
            start, start + 1, one, one, LanguageKind.Unknown,
            false, "unknown-character", -3.2);
    }

    private double TransitionScore(CandidateSegment? previous, CandidateSegment current)
    {
        if (previous is null)
            return 0;

        var score = 0.0;

        if (previous.Language == current.Language && current.Language != LanguageKind.Unknown)
            score += 0.18;

        if (current.Evidence == "japanese-particle")
            score += 0.65;

        if (current.Evidence is "japanese-verb-suffix" or "japanese-romaji-alias")
        {
            score += previous.Language == LanguageKind.English ? 1.05 : 0.40;
        }

        if (current.Language == LanguageKind.English &&
            previous.Evidence == "japanese-particle")
        {
            score += 0.45;
        }

        if (!current.Complete)
            score -= 0.35;

        return score;
    }

    private void AddBeam(List<Hypothesis> beam, Hypothesis hypothesis)
    {
        beam.Add(hypothesis);
        if (beam.Count <= Parameters.BeamWidth * 2)
            return;

        beam.Sort((a, b) => b.Score.CompareTo(a.Score));
        beam.RemoveRange(Parameters.BeamWidth, beam.Count - Parameters.BeamWidth);
    }

    private double[] Normalize(double[] scores)
    {
        var max = scores.Max();
        var weights = scores
            .Select(s => Math.Exp((s - max) / Parameters.SoftmaxTemperature))
            .ToArray();
        var total = weights.Sum();
        return weights.Select(w => w / total).ToArray();
    }

    private HypothesisSnapshot Snapshot(RankedHypothesis ranked) =>
        new(
            ranked.Hypothesis.Score,
            ranked.BeamProbability,
            ranked.EnsembleScore,
            ranked.BoundaryAgreement,
            ranked.LexicalAgreement,
            string.Join(" | ", ranked.Hypothesis.Segments.Select(s => s.Raw)),
            string.Concat(ranked.Hypothesis.Segments.Select(s => s.Converted)),
            ranked.Hypothesis.Segments.Select(s => new HypothesisSegmentSnapshot(
                s.Start, s.End, s.Raw, s.Converted, s.Language,
                s.Complete, s.Evidence, s.LexicalScore)).ToArray());

    private IReadOnlyList<BoundaryEstimate> BuildBoundaries(
        string input,
        IReadOnlyList<Hypothesis> hypotheses,
        IReadOnlyList<double> probabilities,
        IReadOnlyList<IncrementalFrame> previousFrames)
    {
        var result = new List<BoundaryEstimate>();
        var bidirectional = BuildBidirectionalBoundarySupport(input);

        for (var position = 1; position <= input.Length; position++)
        {
            var beam = 0.0;
            for (var i = 0; i < hypotheses.Count; i++)
            {
                if (hypotheses[i].Segments.Any(s => s.End == position))
                    beam += probabilities[i];
            }

            var lexical = LexicalBoundarySupport(input, position);
            var reverse = bidirectional[position];
            var stability = TemporalBoundarySupport(previousFrames, position);

            var views = new List<double> { beam, reverse, lexical };
            if (stability.HasValue)
                views.Add(stability.Value);

            var consensus = views.Average();
            var support = views.Count(v => v >= Parameters.ConsensusVoteThreshold);
            var isEnd = position == input.Length;

            result.Add(new BoundaryEstimate(
                position,
                consensus,
                beam,
                reverse,
                lexical,
                stability ?? -1,
                support,
                !isEnd &&
                consensus >= Parameters.BoundaryClearThreshold &&
                support >= Parameters.MinimumIndependentSupport,
                isEnd,
                input[..position],
                input[position..]));
        }

        return result;
    }

    private double[] BuildBidirectionalBoundarySupport(string input)
    {
        var n = input.Length;
        var byStart = new CandidateSegment[n][];
        for (var start = 0; start < n; start++)
            byStart[start] = Candidates(input, start).ToArray();

        var forward = Enumerable.Repeat(double.NegativeInfinity, n + 1).ToArray();
        var backward = Enumerable.Repeat(double.NegativeInfinity, n + 1).ToArray();
        forward[0] = 0;

        for (var start = 0; start < n; start++)
        {
            if (double.IsNegativeInfinity(forward[start]))
                continue;

            foreach (var candidate in byStart[start])
            {
                var score = forward[start] + StructuralScore(candidate);
                if (score > forward[candidate.End])
                    forward[candidate.End] = score;
            }
        }

        backward[n] = 0;
        for (var start = n - 1; start >= 0; start--)
        {
            foreach (var candidate in byStart[start])
            {
                if (double.IsNegativeInfinity(backward[candidate.End]))
                    continue;

                var score = StructuralScore(candidate) + backward[candidate.End];
                if (score > backward[start])
                    backward[start] = score;
            }
        }

        var global = forward[n];
        var result = new double[n + 1];

        for (var position = 1; position <= n; position++)
        {
            if (double.IsNegativeInfinity(forward[position]) ||
                double.IsNegativeInfinity(backward[position]) ||
                double.IsNegativeInfinity(global))
            {
                result[position] = 0;
                continue;
            }

            var gap = forward[position] + backward[position] - global;
            result[position] = Math.Clamp(Math.Exp(Math.Min(0, gap) / 1.35), 0, 1);
        }

        return result;
    }

    private static double StructuralScore(CandidateSegment candidate)
    {
        if (candidate.Evidence == "unknown-character")
            return -4.0;
        if (!candidate.Complete)
            return -1.8 + EvidenceSupport(candidate) * 0.4;

        return candidate.LexicalScore +
               EvidenceSupport(candidate) * 0.8 -
               0.95;
    }

    private static double EvidenceSupport(CandidateSegment segment) =>
        segment.Evidence switch
        {
            "english-lexeme" => 0.98,
            "japanese-lexeme" => 0.98,
            "japanese-phrase" => 0.98,
            "japanese-particle" => 0.96,
            "japanese-auxiliary" => 0.96,
            "japanese-verb" => 0.96,
            "japanese-verb-suffix" => 0.96,
            "japanese-romaji-alias" => 0.93,
            "romaji-fallback" => 0.50,
            "lexicon-prefix" => 0.38,
            "unknown-character" => 0.05,
            _ => 0.20
        };

    private static double LexicalBoundarySupport(string input, int position)
    {
        if (position <= 0 || position >= input.Length)
            return 0;

        var left = 0.0;
        for (var start = 0; start < position; start++)
        {
            foreach (var entry in Lexicon.ExactAt(input, start))
            {
                if (start + entry.Raw.Length == position)
                    left = Math.Max(left, WeightToConfidence(entry.Weight));
            }
        }

        var right = Lexicon.ExactAt(input, position)
            .Select(e => WeightToConfidence(e.Weight))
            .DefaultIfEmpty(0)
            .Max();

        var rightTail = input[position..];
        var rightPrefix = Lexicon.PrefixMatches(rightTail)
            .Select(e => WeightToConfidence(e.Weight) * 0.75)
            .DefaultIfEmpty(0)
            .Max();

        right = Math.Max(right, rightPrefix);

        if (left > 0 && right > 0)
            return Math.Sqrt(left * right);

        return Math.Max(left, right) * 0.35;
    }

    private static double WeightToConfidence(double weight) =>
        Math.Clamp((weight - 3.5) / 3.5, 0, 1);

    private double? TemporalBoundarySupport(
        IReadOnlyList<IncrementalFrame> previousFrames,
        int position)
    {
        var observations = previousFrames
            .Where(frame => frame.Prefix.Length > position)
            .TakeLast(Parameters.StabilityWindow)
            .Select(frame => frame.Boundaries.FirstOrDefault(b => b.Position == position))
            .Where(boundary => boundary is not null)
            .Select(boundary => boundary!.BeamProbability)
            .ToArray();

        if (observations.Length < 2)
            return null;

        return observations.Average();
    }

    private static double BoundaryAgreement(
        Hypothesis hypothesis,
        int inputLength,
        IReadOnlyList<BoundaryEstimate> boundaries)
    {
        var internalEnds = hypothesis.Segments
            .Select(s => s.End)
            .Where(end => end < inputLength)
            .ToArray();

        if (internalEnds.Length == 0)
            return 0.5;

        return internalEnds
            .Select(end => boundaries.First(b => b.Position == end).Probability)
            .Average();
    }

    private IReadOnlyList<RecognizedSegment> BuildSegments(
        string input,
        Hypothesis best,
        IReadOnlyList<Hypothesis> hypotheses,
        IReadOnlyList<double> probabilities,
        IReadOnlyList<BoundaryEstimate> boundaries,
        IReadOnlyList<IncrementalFrame> previousFrames)
    {
        var result = new List<RecognizedSegment>();

        foreach (var segment in best.Segments)
        {
            var beamInterpretation = 0.0;
            for (var i = 0; i < hypotheses.Count; i++)
            {
                if (hypotheses[i].Segments.Any(s =>
                    s.Start == segment.Start &&
                    s.End == segment.End &&
                    s.Language == segment.Language &&
                    s.Converted == segment.Converted))
                {
                    beamInterpretation += probabilities[i];
                }
            }

            var lexicalInterpretation = EvidenceSupport(segment);
            var stabilityInterpretation = TemporalInterpretationSupport(previousFrames, segment);

            var views = new List<double> { beamInterpretation, lexicalInterpretation };
            if (stabilityInterpretation.HasValue)
                views.Add(stabilityInterpretation.Value);

            var interpretation = views.Average();
            var interpretationSupport =
                views.Count(v => v >= Parameters.ConsensusVoteThreshold);

            var boundary = boundaries.First(b => b.Position == segment.End);
            var boundaryClear = !boundary.IsInputEnd && boundary.Confirmed;
            var interpretationClear =
                interpretation >= Parameters.InterpretationClearThreshold &&
                interpretationSupport >= Parameters.MinimumIndependentSupport;

            var certainty = (boundaryClear, interpretationClear) switch
            {
                (true, true) => CertaintyClass.ClearBoundaryClearInterpretation,
                (true, false) => CertaintyClass.ClearBoundaryAmbiguousInterpretation,
                (false, true) => CertaintyClass.AmbiguousBoundaryClearInterpretation,
                _ => CertaintyClass.AmbiguousBoundaryAmbiguousInterpretation
            };

            result.Add(new RecognizedSegment(
                segment.Start,
                segment.End,
                segment.Raw,
                segment.Converted,
                segment.Language,
                segment.Complete,
                segment.Complete && boundaryClear && interpretationClear,
                boundary.Probability,
                beamInterpretation,
                lexicalInterpretation,
                stabilityInterpretation ?? -1,
                interpretation,
                interpretationSupport,
                certainty));
        }

        return result;
    }

    private double? TemporalInterpretationSupport(
        IReadOnlyList<IncrementalFrame> previousFrames,
        CandidateSegment segment)
    {
        var frames = previousFrames
            .Where(frame => frame.Prefix.Length > segment.End)
            .TakeLast(Parameters.StabilityWindow)
            .ToArray();

        if (frames.Length < 2)
            return null;

        var matches = frames.Count(frame => frame.Segments.Any(s =>
            s.Start == segment.Start &&
            s.End == segment.End &&
            s.Raw == segment.Raw &&
            s.Converted == segment.Converted &&
            s.Language == segment.Language));

        return (double)matches / frames.Length;
    }

    private sealed record CandidateSegment(
        int Start,
        int End,
        string Raw,
        string Converted,
        LanguageKind Language,
        bool Complete,
        string Evidence,
        double LexicalScore);

    private sealed record Hypothesis(
        double Score,
        IReadOnlyList<CandidateSegment> Segments);

    private sealed record RankedHypothesis(
        Hypothesis Hypothesis,
        double BeamProbability,
        double EnsembleScore,
        double BoundaryAgreement,
        double LexicalAgreement);
}
