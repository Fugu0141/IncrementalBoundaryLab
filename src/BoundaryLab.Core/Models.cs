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
    int BeamWidth = 64,
    int MaxFallbackLength = 16,
    double BoundaryClearThreshold = 0.68,
    double InterpretationClearThreshold = 0.68,
    double ConsensusVoteThreshold = 0.67,
    int MinimumIndependentSupport = 2,
    int StabilityWindow = 4,
    double SoftmaxTemperature = 1.30);

public sealed record RecognizedSegment(
    int Start,
    int End,
    string Raw,
    string Converted,
    LanguageKind Language,
    bool Complete,
    bool Confirmed,
    double BoundaryConfidence,
    double BeamInterpretationConfidence,
    double LexicalInterpretationConfidence,
    double StabilityInterpretationConfidence,
    double InterpretationConfidence,
    int IndependentSupport,
    CertaintyClass Certainty);

public sealed record BoundaryEstimate(
    int Position,
    double Probability,
    double BeamProbability,
    double BidirectionalProbability,
    double LexicalProbability,
    double StabilityProbability,
    int IndependentSupport,
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
    double EnsembleScore,
    double BoundaryAgreement,
    double LexicalAgreement,
    string Segmentation,
    string Converted,
    IReadOnlyList<HypothesisSegmentSnapshot> Segments);

public sealed record IncrementalFrame(
    int Step,
    string Prefix,
    string BestSegmentation,
    string Converted,
    double BestHypothesisProbability,
    double BestEnsembleScore,
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
