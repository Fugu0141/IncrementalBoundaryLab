namespace BoundaryLab.Core;

public sealed class PhoneticFirstSession
{
    public const string AlgorithmVersion = "iblab-phonetic-first-v0.3";

    private readonly PhoneticFirstParameters _parameters;
    private readonly PhoneticFirstResolver _resolver;
    private readonly List<ResolvedSegment> _committed = [];
    private readonly List<PhoneticFirstFrame> _frames = [];

    private string _input = "";
    private long _totalAnalyzedCharacters;

    public PhoneticFirstSession(PhoneticFirstParameters? parameters = null)
    {
        _parameters = parameters ?? new PhoneticFirstParameters();
        _resolver = new PhoneticFirstResolver(_parameters);
    }

    public PhoneticFirstParameters Parameters => _parameters;

    public PhoneticFirstAnalysisResult Update(string input)
    {
        if (!input.All(char.IsAsciiLetter))
            throw new ArgumentException("Input must contain ASCII letters only.", nameof(input));

        input = input.ToLowerInvariant();
        var appendOnly = input.StartsWith(_input, StringComparison.Ordinal) &&
                         input.Length >= _input.Length;

        if (!appendOnly)
        {
            ResetState();
            foreach (var c in input)
            {
                _input += c;
                ProcessStep(rebuilt: true);
            }

            return CurrentResult();
        }

        var suffix = input[_input.Length..];
        foreach (var c in suffix)
        {
            _input += c;
            ProcessStep(rebuilt: false);
        }

        return CurrentResult();
    }

    public void Reset() => ResetState();

    private void ProcessStep(bool rebuilt)
    {
        var committedLengthBefore = CommittedRawLength();
        var activeRaw = _input[committedLengthBefore..];
        var stage1 = PhoneticProjector.Project(activeRaw);
        var resolution = _resolver.Resolve(activeRaw, stage1);
        var analyzed = activeRaw.Length;

        var frozenLocal = FreezeablePrefix(activeRaw, resolution.Segments);
        var frozenGlobal = frozenLocal
            .Select(s => Globalize(s, committedLengthBefore, confirmed: true))
            .ToArray();

        if (frozenGlobal.Length > 0)
            _committed.AddRange(frozenGlobal);

        var committedLengthAfter = CommittedRawLength();

        PhoneticProjection displayStage1;
        PhoneticResolution displayResolution;

        if (committedLengthAfter != committedLengthBefore)
        {
            var remaining = _input[committedLengthAfter..];
            displayStage1 = PhoneticProjector.Project(remaining);
            displayResolution = _resolver.Resolve(remaining, displayStage1);
            analyzed += remaining.Length;
        }
        else
        {
            displayStage1 = stage1;
            displayResolution = resolution;
        }

        _totalAnalyzedCharacters += analyzed;

        var activeGlobal = displayResolution.Segments
            .Select(s => Globalize(s, committedLengthAfter, confirmed: false))
            .ToArray();

        var output = string.Concat(_committed.Select(s => s.Output)) +
                     string.Concat(activeGlobal.Select(s => s.Output));

        _frames.Add(new PhoneticFirstFrame(
            _input.Length,
            _input,
            committedLengthAfter,
            string.Concat(_committed.Select(s => s.Output)),
            _input[committedLengthAfter..],
            displayStage1,
            activeGlobal,
            frozenGlobal,
            displayResolution.Candidates,
            output,
            analyzed,
            _totalAnalyzedCharacters,
            rebuilt));
    }

    private IReadOnlyList<ResolvedSegment> FreezeablePrefix(
        string activeRaw,
        IReadOnlyList<ResolvedSegment> segments)
    {
        var result = new List<ResolvedSegment>();

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var lookahead = activeRaw.Length - segment.End;

            if (lookahead < _parameters.MinimumLookaheadToFreeze)
                break;

            var confidence = segment.Confidence;
            if (segment.DecisionReason == "stage2-phonetic-fallback" &&
                i + 1 < segments.Count &&
                segments[i + 1].DecisionReason.StartsWith(
                    "stage2-japanese-",
                    StringComparison.Ordinal))
            {
                confidence = Math.Max(confidence, 0.88);
                segment = segment with { Confidence = confidence };
            }

            if (confidence < _parameters.FreezeConfidenceThreshold ||
                segment.Language == LanguageKind.Unknown)
            {
                break;
            }

            result.Add(segment with { Confirmed = true });
        }

        return result;
    }

    private static ResolvedSegment Globalize(
        ResolvedSegment segment,
        int offset,
        bool confirmed) =>
        segment with
        {
            Start = segment.Start + offset,
            End = segment.End + offset,
            Confirmed = confirmed
        };

    private int CommittedRawLength() =>
        _committed.Count == 0 ? 0 : _committed[^1].End;

    private PhoneticFirstAnalysisResult CurrentResult()
    {
        if (_input.Length == 0)
            return new PhoneticFirstAnalysisResult("", "", 0, [], [], []);

        var last = _frames[^1];
        return new PhoneticFirstAnalysisResult(
            _input,
            last.Output,
            last.CommittedRawLength,
            _committed.ToArray(),
            last.ActiveSegments,
            _frames.ToArray());
    }

    private void ResetState()
    {
        _input = "";
        _committed.Clear();
        _frames.Clear();
        _totalAnalyzedCharacters = 0;
    }
}
