namespace BoundaryLab.Core;

public sealed class PhoneticFirstSession
{
    public const string AlgorithmVersion =
        "iblab-phonetic-first-rcr-v0.4";

    private readonly PhoneticFirstParameters _parameters;
    private readonly PhoneticFirstResolver _resolver;
    private readonly List<ResolvedSegment> _committed = [];
    private readonly List<PhoneticFirstFrame> _frames = [];

    private string _input = "";
    private long _totalAnalyzedCharacters;

    public PhoneticFirstSession(
        PhoneticFirstParameters? parameters = null)
    {
        _parameters =
            parameters ?? new PhoneticFirstParameters();
        _resolver = new PhoneticFirstResolver(_parameters);
    }

    public PhoneticFirstParameters Parameters => _parameters;

    public PhoneticFirstAnalysisResult Update(string input)
    {
        if (!input.All(InputSyntax.IsAllowed))
            throw new ArgumentException(
                "Input contains unsupported characters.",
                nameof(input));

        input = InputSyntax.Normalize(input);
        var appendOnly =
            input.StartsWith(_input, StringComparison.Ordinal) &&
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
        var transitions = new List<FreezeTransition>();
        var thawEvents = new List<ThawEvent>();
        var rippleEvents = new List<ContextRippleEvent>();

        PromoteOldSoftSegments(transitions);

        var committedLengthBefore = CommittedRawLength();
        var activeRaw = _input[committedLengthBefore..];
        var stage1 = PhoneticProjector.Project(activeRaw);
        var resolution = _resolver.Resolve(activeRaw, stage1);
        var analyzed = activeRaw.Length;

        var initialAnomalies = DetectAnomalies(
            stage1,
            resolution,
            committedLengthBefore);

        if (ShouldThawSoftTail(initialAnomalies))
        {
            var thawed = ThawSoftTail(
                initialAnomalies,
                thawEvents,
                transitions);

            if (thawed)
            {
                committedLengthBefore = CommittedRawLength();
                activeRaw = _input[committedLengthBefore..];
                stage1 = PhoneticProjector.Project(activeRaw);
                resolution = _resolver.Resolve(activeRaw, stage1);
                analyzed += activeRaw.Length;
            }
        }

        var anomalies = DetectAnomalies(
            stage1,
            resolution,
            committedLengthBefore);

        resolution = resolution with
        {
            Segments = ApplyRipple(
                resolution.Segments,
                anomalies,
                committedLengthBefore,
                rippleEvents)
        };

        var frozenLocal = FreezeablePrefix(
            activeRaw,
            resolution.Segments,
            anomalies,
            committedLengthBefore);

        var frozenGlobal = frozenLocal
            .Select(s => Globalize(
                s,
                committedLengthBefore,
                confirmed: true,
                FreezeState.SoftFrozen))
            .ToArray();

        foreach (var segment in frozenGlobal)
        {
            _committed.Add(segment);
            transitions.Add(new FreezeTransition(
                segment.Start,
                segment.End,
                segment.Raw,
                FreezeState.Active,
                FreezeState.SoftFrozen,
                "high-confidence-with-lookahead"));
        }

        var committedLengthAfter = CommittedRawLength();

        PhoneticProjection displayStage1;
        PhoneticResolution displayResolution;

        if (committedLengthAfter != committedLengthBefore)
        {
            var remaining =
                _input[committedLengthAfter..];

            displayStage1 =
                PhoneticProjector.Project(remaining);
            displayResolution =
                _resolver.Resolve(remaining, displayStage1);

            var remainingAnomalies = DetectAnomalies(
                displayStage1,
                displayResolution,
                committedLengthAfter);

            displayResolution = displayResolution with
            {
                Segments = ApplyRipple(
                    displayResolution.Segments,
                    remainingAnomalies,
                    committedLengthAfter,
                    rippleEvents)
            };

            analyzed += remaining.Length;
        }
        else
        {
            displayStage1 = stage1;
            displayResolution = resolution;
        }

        PromoteThroughHardBoundary(
            displayResolution.SymbolEvidence,
            transitions);

        _totalAnalyzedCharacters += analyzed;

        var activeGlobal = displayResolution.Segments
            .Select(s => Globalize(
                s,
                committedLengthAfter,
                confirmed: false,
                FreezeState.Active))
            .ToArray();

        var output =
            string.Concat(_committed.Select(s => s.Output)) +
            string.Concat(activeGlobal.Select(s => s.Output));

        _frames.Add(new PhoneticFirstFrame(
            _input.Length,
            _input,
            CommittedRawLength(),
            string.Concat(_committed.Select(s => s.Output)),
            _input[CommittedRawLength()..],
            displayStage1,
            activeGlobal,
            frozenGlobal,
            displayResolution.Candidates,
            output,
            analyzed,
            _totalAnalyzedCharacters,
            rebuilt,
            displayResolution.SymbolEvidence,
            rippleEvents,
            thawEvents,
            transitions));
    }

    private IReadOnlyList<ContextRippleEvent> DetectAnomalies(
        PhoneticProjection projection,
        PhoneticResolution resolution,
        int globalOffset)
    {
        var result = new List<ContextRippleEvent>();

        foreach (var unit in projection.Units)
        {
            var explainedByLatin = resolution.Segments.Any(s =>
                s.Language == LanguageKind.English &&
                s.Confidence >= 0.90 &&
                s.Start <= unit.Start &&
                s.End >= unit.End);

            if (explainedByLatin)
                continue;

            if (unit.Kind == PhoneticUnitKind.Pending)
            {
                var strength =
                    unit.End == projection.Raw.Length ? 0.35 : 1.0;

                result.Add(new ContextRippleEvent(
                    globalOffset + unit.Start,
                    globalOffset + unit.End,
                    "PendingPhonetic",
                    strength,
                    globalOffset,
                    globalOffset + projection.Raw.Length,
                    unit.Reason));
            }
            else if (
                unit.Kind == PhoneticUnitKind.Ambiguous &&
                unit.End < projection.Raw.Length)
            {
                result.Add(new ContextRippleEvent(
                    globalOffset + unit.Start,
                    globalOffset + unit.End,
                    "InteriorAmbiguity",
                    0.55,
                    globalOffset,
                    globalOffset + projection.Raw.Length,
                    unit.Reason));
            }
        }

        foreach (var segment in resolution.Segments
                     .Where(s =>
                         s.Language == LanguageKind.Unknown &&
                         !s.DecisionReason.Contains(
                             "symbol",
                             StringComparison.Ordinal)))
        {
            result.Add(new ContextRippleEvent(
                globalOffset + segment.Start,
                globalOffset + segment.End,
                "UnknownSegment",
                1.0,
                globalOffset,
                globalOffset + projection.Raw.Length,
                segment.DecisionReason));
        }

        foreach (var evidence in resolution.SymbolEvidence
                     .Where(e => e.Kind == "IncompleteBindingToken"))
        {
            result.Add(new ContextRippleEvent(
                globalOffset + evidence.Start,
                globalOffset + evidence.End,
                "IncompleteBindingToken",
                0.95,
                globalOffset,
                globalOffset + projection.Raw.Length,
                evidence.Reason));
        }

        return result
            .GroupBy(e => (e.SourceStart, e.SourceEnd, e.SourceKind))
            .Select(g =>
                g.OrderByDescending(x => x.Strength).First())
            .ToArray();
    }

    private bool ShouldThawSoftTail(
        IReadOnlyList<ContextRippleEvent> anomalies)
    {
        if (anomalies.Count == 0 ||
            !_committed.Any(s =>
                s.FreezeState == FreezeState.SoftFrozen))
            return false;

        var boundary = CommittedRawLength();

        return anomalies.Any(a =>
            a.Strength >= _parameters.RippleTriggerThreshold &&
            a.SourceStart - boundary <=
                _parameters.RippleCharacterRadius);
    }

    private bool ThawSoftTail(
        IReadOnlyList<ContextRippleEvent> anomalies,
        List<ThawEvent> thawEvents,
        List<FreezeTransition> transitions)
    {
        var softIndices = _committed
            .Select((segment, index) => (segment, index))
            .Where(x =>
                x.segment.FreezeState ==
                FreezeState.SoftFrozen)
            .Select(x => x.index)
            .ToArray();

        if (softIndices.Length == 0)
            return false;

        var first = softIndices
            .TakeLast(_parameters.RippleSegmentRadius)
            .First();

        var thawed = _committed.Skip(first).ToArray();

        if (thawed.Any(s =>
            s.FreezeState == FreezeState.HardFrozen))
            return false;

        _committed.RemoveRange(
            first,
            _committed.Count - first);

        var reason = string.Join(
            "+",
            anomalies
                .Select(a => a.SourceKind)
                .Distinct());

        thawEvents.Add(new ThawEvent(
            thawed[0].Start,
            thawed[^1].End,
            thawed.Select(s => s.Raw).ToArray(),
            reason));

        foreach (var segment in thawed)
        {
            transitions.Add(new FreezeTransition(
                segment.Start,
                segment.End,
                segment.Raw,
                FreezeState.SoftFrozen,
                FreezeState.Active,
                $"context-ripple:{reason}"));
        }

        return true;
    }

    private IReadOnlyList<ResolvedSegment> ApplyRipple(
        IReadOnlyList<ResolvedSegment> segments,
        IReadOnlyList<ContextRippleEvent> anomalies,
        int globalOffset,
        List<ContextRippleEvent> outputEvents)
    {
        if (anomalies.Count == 0)
            return segments;

        var result = new List<ResolvedSegment>(segments.Count);

        foreach (var segment in segments)
        {
            var globalStart = globalOffset + segment.Start;
            var globalEnd = globalOffset + segment.End;
            var penalty = 0.0;

            foreach (var anomaly in anomalies)
            {
                var distance = Distance(
                    globalStart,
                    globalEnd,
                    anomaly.SourceStart,
                    anomaly.SourceEnd);

                if (distance > _parameters.RippleCharacterRadius)
                    continue;

                var localPenalty =
                    anomaly.Strength *
                    Math.Pow(
                        _parameters.RippleDecay,
                        distance);

                penalty = Math.Max(
                    penalty,
                    Math.Min(0.70, localPenalty));

                outputEvents.Add(
                    anomaly with
                    {
                        AffectedStart = globalStart,
                        AffectedEnd = globalEnd,
                        Reason =
                            $"{anomaly.Reason};distance={distance}"
                    });
            }

            if (penalty <= 0 ||
                segment.DecisionReason ==
                    "stage2-orthographic-latin-token")
            {
                result.Add(segment);
                continue;
            }

            result.Add(segment with
            {
                Confidence =
                    segment.Confidence * (1.0 - penalty),
                ContextPenalty = penalty
            });
        }

        return result;
    }

    private IReadOnlyList<ResolvedSegment> FreezeablePrefix(
        string activeRaw,
        IReadOnlyList<ResolvedSegment> segments,
        IReadOnlyList<ContextRippleEvent> anomalies,
        int globalOffset)
    {
        var result = new List<ResolvedSegment>();

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var lookahead =
                activeRaw.Length - segment.End;

            if (lookahead <
                _parameters.MinimumLookaheadToFreeze)
                break;

            var confidence = segment.Confidence;

            if (segment.DecisionReason ==
                    "stage2-phonetic-fallback" &&
                i + 1 < segments.Count &&
                segments[i + 1].DecisionReason.StartsWith(
                    "stage2-japanese-",
                    StringComparison.Ordinal))
            {
                confidence = Math.Max(confidence, 0.88);
                segment =
                    segment with { Confidence = confidence };
            }

            var globalStart = globalOffset + segment.Start;
            var globalEnd = globalOffset + segment.End;
            var nearAnomaly = anomalies.Any(a =>
                a.Strength >= _parameters.RippleTriggerThreshold &&
                Distance(
                    globalStart,
                    globalEnd,
                    a.SourceStart,
                    a.SourceEnd) <=
                _parameters.RippleCharacterRadius);

            if (confidence <
                    _parameters.FreezeConfidenceThreshold ||
                segment.Language == LanguageKind.Unknown ||
                nearAnomaly)
            {
                break;
            }

            result.Add(segment with
            {
                Confirmed = true,
                FreezeState = FreezeState.SoftFrozen
            });
        }

        return result;
    }

    private void PromoteOldSoftSegments(
        List<FreezeTransition> transitions)
    {
        for (var i = 0; i < _committed.Count; i++)
        {
            var segment = _committed[i];

            if (segment.FreezeState !=
                FreezeState.SoftFrozen)
                continue;

            if (_input.Length - segment.End <
                _parameters.HardFreezeLookahead)
                continue;

            _committed[i] = segment with
            {
                FreezeState = FreezeState.HardFrozen,
                Confirmed = true
            };

            transitions.Add(new FreezeTransition(
                segment.Start,
                segment.End,
                segment.Raw,
                FreezeState.SoftFrozen,
                FreezeState.HardFrozen,
                "sufficient-lookahead-without-local-contradiction"));
        }
    }

    private void PromoteThroughHardBoundary(
        IReadOnlyList<SymbolEvidence> evidence,
        List<FreezeTransition> transitions)
    {
        if (!evidence.Any(e =>
            e.Kind == "HardBoundary"))
            return;

        for (var i = 0; i < _committed.Count; i++)
        {
            var segment = _committed[i];

            if (segment.FreezeState !=
                FreezeState.SoftFrozen)
                continue;

            _committed[i] = segment with
            {
                FreezeState = FreezeState.HardFrozen,
                Confirmed = true
            };

            transitions.Add(new FreezeTransition(
                segment.Start,
                segment.End,
                segment.Raw,
                FreezeState.SoftFrozen,
                FreezeState.HardFrozen,
                "hard-boundary-observed"));
        }
    }

    private static int Distance(
        int startA,
        int endA,
        int startB,
        int endB)
    {
        if (endA <= startB)
            return startB - endA;

        if (endB <= startA)
            return startA - endB;

        return 0;
    }

    private static ResolvedSegment Globalize(
        ResolvedSegment segment,
        int offset,
        bool confirmed,
        FreezeState freezeState) =>
        segment with
        {
            Start = segment.Start + offset,
            End = segment.End + offset,
            Confirmed = confirmed,
            FreezeState = freezeState
        };

    private int CommittedRawLength() =>
        _committed.Count == 0
            ? 0
            : _committed[^1].End;

    private PhoneticFirstAnalysisResult CurrentResult()
    {
        if (_input.Length == 0)
            return new PhoneticFirstAnalysisResult(
                "", "", 0, [], [], []);

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
