using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LatticeEdgeKind
{
    JapanesePhonetic,
    JapaneseLexical,
    JapaneseMozc,
    EnglishLexical,
    LatinStructural,
    LiteralFallback,
    OpenPrefix,
    HardBoundary,
    NeutralSymbol,
    BindingSymbol,
    Unknown
}

public sealed record EvidenceLatticeParameters(
    int BeamWidth = 16,
    int MaxPhoneticSpan = 24,
    int MaxLatinSide = 12,
    int ConsensusPathCount = 8,
    double AlternativeScoreWindow = 4.0,
    int CommitLookahead = 3,
    int CommitStabilityFrames = 2,
    double LanguageSwitchPenalty = 0.30,
    int MozcProbeBudget = 18,
    double MozcScoreWeight = 2.6,
    double MozcRejectPenalty = 1.2,
    bool UsePhoneticFirstHybrid = false,
    string EngineId = "legacy");

public sealed record LatticeEdge(
    int Start,
    int End,
    string Raw,
    string Output,
    LanguageKind Language,
    LatticeEdgeKind Kind,
    double LocalScore,
    double JapaneseProfile,
    double EnglishProfile,
    string Evidence,
    double MozcQuality = -1,
    string? MozcTopCandidate = null);

public sealed record LatticePathSnapshot(
    double Score,
    double RelativeScore,
    string Segmentation,
    string Output,
    IReadOnlyList<LatticeEdge> Edges);

public sealed record LatticeCommitEvent(
    int Start,
    int End,
    string Raw,
    string Output,
    string Reason);

public sealed record EvidenceLatticeFrame(
    int Step,
    string Input,
    int CommittedRawLength,
    string CommittedOutput,
    string ActiveRaw,
    string PhoneticPreview,
    IReadOnlyList<LatticeEdge> CandidateEdges,
    IReadOnlyList<LatticePathSnapshot> TopPaths,
    int ConsensusEnd,
    string ConsensusSignature,
    int ConsensusStableFrames,
    IReadOnlyList<LatticeCommitEvent> CommitEvents,
    string Output,
    int ExpandedEdgesThisStep,
    long TotalExpandedEdges,
    int MozcProbesThisStep,
    bool MozcAvailable,
    bool Rebuilt)
{
    // Research annotation only; it does not decide responsibility or commits.
    public PhoneticStructureAnalysis? PhoneticStructure { get; init; }
}

public sealed record EvidenceLatticeResult(
    string Input,
    string Output,
    int CommittedRawLength,
    IReadOnlyList<LatticeEdge> CommittedSegments,
    IReadOnlyList<LatticeEdge> ActiveBestSegments,
    IReadOnlyList<EvidenceLatticeFrame> Frames);

public sealed record EvidenceLatticeResearchReport(
    string FormatVersion,
    string AlgorithmVersion,
    DateTimeOffset GeneratedAtUtc,
    string Input,
    string Output,
    EvidenceLatticeParameters Parameters,
    int CommittedRawLength,
    IReadOnlyList<LatticeEdge> CommittedSegments,
    IReadOnlyList<LatticeEdge> ActiveBestSegments,
    IReadOnlyList<EvidenceLatticeFrame> Frames);
