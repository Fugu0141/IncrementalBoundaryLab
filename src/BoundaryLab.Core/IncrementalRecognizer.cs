namespace BoundaryLab.Core;

public sealed class IncrementalRecognizer
{
    public const string AlgorithmVersion = "iblab-beam-v0.1";
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
            frames.Add(AnalyzePrefix(input[..length]));

        var last = frames[^1];
        return new AnalysisResult(input, last.Converted, last.Segments, last.Boundaries, frames);
    }

    private IncrementalFrame AnalyzePrefix(string prefix)
    {
        var hypotheses = BeamSearch(prefix);
        var probabilities = Normalize(hypotheses.Select(h => h.Score).ToArray());
        var snapshots = hypotheses
            .Select((h, i) => Snapshot(h, probabilities[i]))
            .ToArray();

        var best = hypotheses[0];
        var boundaries = BuildBoundaries(prefix, hypotheses, probabilities);
        var segments = BuildSegments(prefix, best, hypotheses, probabilities, boundaries);
        var entropy = probabilities
            .Where(p => p > 0)
            .Sum(p => -p * Math.Log2(p));

        return new IncrementalFrame(
            prefix.Length,
            prefix,
            string.Join(" | ", best.Segments.Select(s => s.Raw)),
            string.Concat(best.Segments.Select(s => s.Converted)),
            probabilities[0],
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

        if (current.Evidence == "japanese-verb-suffix")
        {
            score += previous.Language == LanguageKind.English ? 1.10 : 0.45;
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

    private static HypothesisSnapshot Snapshot(Hypothesis hypothesis, double probability) =>
        new(
            hypothesis.Score,
            probability,
            string.Join(" | ", hypothesis.Segments.Select(s => s.Raw)),
            string.Concat(hypothesis.Segments.Select(s => s.Converted)),
            hypothesis.Segments.Select(s => new HypothesisSegmentSnapshot(
                s.Start, s.End, s.Raw, s.Converted, s.Language,
                s.Complete, s.Evidence, s.LexicalScore)).ToArray());

    private IReadOnlyList<BoundaryEstimate> BuildBoundaries(
        string input,
        IReadOnlyList<Hypothesis> hypotheses,
        IReadOnlyList<double> probabilities)
    {
        var result = new List<BoundaryEstimate>();

        for (var position = 1; position <= input.Length; position++)
        {
            var probability = 0.0;
            for (var i = 0; i < hypotheses.Count; i++)
            {
                if (hypotheses[i].Segments.Any(s => s.End == position))
                    probability += probabilities[i];
            }

            var isEnd = position == input.Length;
            result.Add(new BoundaryEstimate(
                position,
                probability,
                !isEnd && probability >= Parameters.BoundaryClearThreshold,
                isEnd,
                input[..position],
                input[position..]));
        }

        return result;
    }

    private IReadOnlyList<RecognizedSegment> BuildSegments(
        string input,
        Hypothesis best,
        IReadOnlyList<Hypothesis> hypotheses,
        IReadOnlyList<double> probabilities,
        IReadOnlyList<BoundaryEstimate> boundaries)
    {
        var result = new List<RecognizedSegment>();

        foreach (var segment in best.Segments)
        {
            var interpretation = 0.0;
            for (var i = 0; i < hypotheses.Count; i++)
            {
                if (hypotheses[i].Segments.Any(s =>
                    s.Start == segment.Start &&
                    s.End == segment.End &&
                    s.Language == segment.Language &&
                    s.Converted == segment.Converted))
                {
                    interpretation += probabilities[i];
                }
            }

            var boundary = boundaries.First(b => b.Position == segment.End);
            var boundaryClear = !boundary.IsInputEnd &&
                                boundary.Probability >= Parameters.BoundaryClearThreshold;
            var interpretationClear =
                interpretation >= Parameters.InterpretationClearThreshold;

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
                interpretation,
                certainty));
        }

        return result;
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
}
