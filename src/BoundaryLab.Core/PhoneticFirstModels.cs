using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PhoneticUnitKind
{
    Kana,
    Ambiguous,
    Pending,
    Symbol
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FreezeState
{
    Active,
    SoftFrozen,
    HardFrozen
}

public sealed record PhoneticFirstParameters(
    int MinimumLookaheadToFreeze = 2,
    int MaximumJapaneseFallbackLength = 16,
    double EnglishAnomalyThreshold = 0.80,
    double FreezeConfidenceThreshold = 0.86,
    double LowConfidenceRippleThreshold = 0.86,
    int HardFreezeLookahead = 12,
    int RippleSegmentRadius = 2,
    int RippleCharacterRadius = 6,
    double RippleDecay = 0.45,
    double RippleTriggerThreshold = 0.45);

public sealed record PhoneticUnit(
    int Start,
    int End,
    string Raw,
    string Preview,
    PhoneticUnitKind Kind,
    double Confidence,
    string Reason);

public sealed record PhoneticProjection(
    string Raw,
    string Preview,
    double KanaCoverage,
    double AmbiguousCoverage,
    double UnresolvedRatio,
    IReadOnlyList<PhoneticUnit> Units);

public sealed record ResolutionCandidate(
    int Start,
    int End,
    string Raw,
    string Output,
    LanguageKind Language,
    double Score,
    double PhoneticConfidence,
    double LexicalConfidence,
    string Reason);

public sealed record SymbolEvidence(
    int Start,
    int End,
    string Raw,
    string Kind,
    double Confidence,
    bool Complete,
    string Reason);

public sealed record ContextRippleEvent(
    int SourceStart,
    int SourceEnd,
    string SourceKind,
    double Strength,
    int AffectedStart,
    int AffectedEnd,
    string Reason);

public sealed record ThawEvent(
    int Start,
    int End,
    IReadOnlyList<string> RawSegments,
    string Reason);

public sealed record FreezeTransition(
    int Start,
    int End,
    string Raw,
    FreezeState From,
    FreezeState To,
    string Reason);

public sealed record ResolvedSegment(
    int Start,
    int End,
    string Raw,
    string Output,
    LanguageKind Language,
    double Confidence,
    bool Confirmed,
    string DecisionReason,
    FreezeState FreezeState = FreezeState.Active,
    double ContextPenalty = 0);

public sealed record PhoneticFirstFrame(
    int Step,
    string Input,
    int CommittedRawLength,
    string CommittedOutput,
    string ActiveRaw,
    PhoneticProjection Stage1,
    IReadOnlyList<ResolvedSegment> ActiveSegments,
    IReadOnlyList<ResolvedSegment> FrozenThisStep,
    IReadOnlyList<ResolutionCandidate> Stage2Candidates,
    string Output,
    int AnalyzedCharactersThisStep,
    long TotalAnalyzedCharacters,
    bool Rebuilt,
    IReadOnlyList<SymbolEvidence> SymbolEvidence,
    IReadOnlyList<ContextRippleEvent> RippleEvents,
    IReadOnlyList<ThawEvent> ThawEvents,
    IReadOnlyList<FreezeTransition> FreezeTransitions);

public sealed record PhoneticFirstAnalysisResult(
    string Input,
    string Output,
    int CommittedRawLength,
    IReadOnlyList<ResolvedSegment> CommittedSegments,
    IReadOnlyList<ResolvedSegment> ActiveSegments,
    IReadOnlyList<PhoneticFirstFrame> Frames)
{
    public int HardCommittedRawLength =>
        CommittedSegments
            .Where(s => s.FreezeState == FreezeState.HardFrozen)
            .Select(s => s.End)
            .DefaultIfEmpty(0)
            .Max();
}

public sealed record PhoneticFirstResearchReport(
    string FormatVersion,
    string AlgorithmVersion,
    DateTimeOffset GeneratedAtUtc,
    string Input,
    string Output,
    PhoneticFirstParameters Parameters,
    int CommittedRawLength,
    IReadOnlyList<ResolvedSegment> CommittedSegments,
    IReadOnlyList<ResolvedSegment> ActiveSegments,
    IReadOnlyList<PhoneticFirstFrame> Frames);
