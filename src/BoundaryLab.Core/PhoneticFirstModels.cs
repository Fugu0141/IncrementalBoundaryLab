using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PhoneticUnitKind
{
    Kana,
    Ambiguous,
    Pending
}

public sealed record PhoneticFirstParameters(
    int MinimumLookaheadToFreeze = 2,
    int MaximumJapaneseFallbackLength = 16,
    double EnglishAnomalyThreshold = 0.80,
    double FreezeConfidenceThreshold = 0.86);

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

public sealed record ResolvedSegment(
    int Start,
    int End,
    string Raw,
    string Output,
    LanguageKind Language,
    double Confidence,
    bool Confirmed,
    string DecisionReason);

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
    bool Rebuilt);

public sealed record PhoneticFirstAnalysisResult(
    string Input,
    string Output,
    int CommittedRawLength,
    IReadOnlyList<ResolvedSegment> CommittedSegments,
    IReadOnlyList<ResolvedSegment> ActiveSegments,
    IReadOnlyList<PhoneticFirstFrame> Frames);

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
