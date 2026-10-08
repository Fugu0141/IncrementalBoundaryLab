using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LanguageKind
{
    Japanese,
    English,
    Unknown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CertaintyClass
{
    ClearBoundaryClearInterpretation,
    ClearBoundaryAmbiguousInterpretation,
    AmbiguousBoundaryClearInterpretation,
    AmbiguousBoundaryAmbiguousInterpretation
}

public sealed record RecognizerParameters(
    int BeamWidth = 32,
    int MaxFallbackLength = 12,
    double BoundaryClearThreshold = 0.82,
    double InterpretationClearThreshold = 0.72,
    double SoftmaxTemperature = 1.20);

public sealed record RecognizedSegment(
    int Start,
    int End,
    string Raw,
    string Converted,
    LanguageKind Language,
    bool Complete,
    bool Confirmed,
    double BoundaryConfidence,
    double InterpretationConfidence,
    CertaintyClass Certainty);

public sealed record BoundaryEstimate(
    int Position,
    double Probability,
    bool Confirmed,
    bool IsInputEnd,
    string Left,
    string Right);

public sealed record HypothesisSegmentSnapshot(
    int Start,
    int End,
    string Raw,
    string Converted,
    LanguageKind Language,
    bool Complete,
    string Evidence,
    double LexicalScore);

public sealed record HypothesisSnapshot(
    double Score,
    double Probability,
    string Segmentation,
    string Converted,
    IReadOnlyList<HypothesisSegmentSnapshot> Segments);

public sealed record IncrementalFrame(
    int Step,
    string Prefix,
    string BestSegmentation,
    string Converted,
    double BestHypothesisProbability,
    double EntropyBits,
    IReadOnlyList<RecognizedSegment> Segments,
    IReadOnlyList<BoundaryEstimate> Boundaries,
    IReadOnlyList<HypothesisSnapshot> TopHypotheses);

public sealed record AnalysisResult(
    string Input,
    string Converted,
    IReadOnlyList<RecognizedSegment> Segments,
    IReadOnlyList<BoundaryEstimate> Boundaries,
    IReadOnlyList<IncrementalFrame> Frames);

public sealed record ResearchReport(
    string FormatVersion,
    string AlgorithmVersion,
    DateTimeOffset GeneratedAtUtc,
    string Input,
    string Converted,
    RecognizerParameters Parameters,
    IReadOnlyList<RecognizedSegment> FinalSegments,
    IReadOnlyList<BoundaryEstimate> FinalBoundaries,
    IReadOnlyList<IncrementalFrame> Frames);
