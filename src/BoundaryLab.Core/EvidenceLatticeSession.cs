namespace BoundaryLab.Core;

public sealed class EvidenceLatticeSession
{
    public const string AlgorithmVersion =
        "iblab-mozc-responsibility-ime-v0.6";

    private readonly EvidenceLatticeParameters _parameters;
    private readonly EvidenceLatticeDecoder _decoder;
    private readonly List<LatticeEdge> _committed = [];
    private readonly List<EvidenceLatticeFrame> _frames = [];

    private string _input = "";
    private string _consensusSignature = "";
    private int _consensusStableFrames;
    private long _totalExpandedEdges;

    public EvidenceLatticeSession(
        EvidenceLatticeParameters? parameters = null,
        IMozcConversionOracle? mozc = null)
    {
        _parameters =
            parameters ?? new EvidenceLatticeParameters();
        _decoder = new EvidenceLatticeDecoder(_parameters, mozc);
    }

    public EvidenceLatticeParameters Parameters => _parameters;
    public bool MozcAvailable => _decoder.MozcAvailable;

    public EvidenceLatticeResult Update(string input)
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
            Reset();
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

    public void Reset()
    {
        _input = "";
        _committed.Clear();
        _frames.Clear();
        _consensusSignature = "";
        _consensusStableFrames = 0;
        _totalExpandedEdges = 0;
    }

    private void ProcessStep(bool rebuilt)
    {
        var committedBefore = CommittedRawLength();
        var activeRaw = _input[committedBefore..];
        var decode = _decoder.Decode(activeRaw);
        var expanded = decode.ExpandedEdges;
        var probes = decode.MozcProbes;
        var commits = new List<LatticeCommitEvent>();

        var eligible = SelectConsensusPaths(decode.TopPaths);
        var common = CommonEdgePrefix(eligible);
        var consensusEnd =
            common.Count == 0 ? 0 : common[^1].End;

        LatticeEdge? stableCandidate = null;
        if (common.Count > 0)
        {
            var first = common[0];
            if (CanCommitByConsensus(first, activeRaw) &&
                first.End <=
                activeRaw.Length -
                _parameters.CommitLookahead)
            {
                stableCandidate = first;
            }
        }

        var signature = stableCandidate is null
            ? ""
            : EdgeSignature(stableCandidate);

        if (signature.Length > 0 &&
            signature == _consensusSignature)
        {
            _consensusStableFrames++;
        }
        else
        {
            _consensusSignature = signature;
            _consensusStableFrames =
                signature.Length == 0 ? 0 : 1;
        }

        var best = decode.TopPaths.FirstOrDefault();
        var hardCommitEnd =
            FindLastHardBoundaryEnd(activeRaw);

        if (best is not null && hardCommitEnd > 0)
        {
            var hardSegments = ClosePathAtHardBoundary(
                best.Edges,
                activeRaw,
                hardCommitEnd);

            if (hardSegments.Count > 0 &&
                hardSegments[^1].End == hardCommitEnd)
            {
                CommitSegments(
                    hardSegments,
                    committedBefore,
                    "hard-boundary",
                    commits);

                _consensusSignature = "";
                _consensusStableFrames = 0;
            }
        }
        else if (
            stableCandidate is not null &&
            _consensusStableFrames >=
                _parameters.CommitStabilityFrames)
        {
            CommitSegments(
                [stableCandidate],
                committedBefore,
                "multi-hypothesis-prefix-consensus",
                commits);

            _consensusSignature = "";
            _consensusStableFrames = 0;
        }

        var committedAfter = CommittedRawLength();

        EvidenceLatticeDecodeResult displayDecode;
        string displayActive;

        if (committedAfter != committedBefore)
        {
            displayActive =
                _input[committedAfter..];
            displayDecode =
                _decoder.Decode(displayActive);
            expanded += displayDecode.ExpandedEdges;
            probes += displayDecode.MozcProbes;
        }
        else
        {
            displayActive = activeRaw;
            displayDecode = decode;
        }

        _totalExpandedEdges += expanded;

        var bestDisplay =
            displayDecode.TopPaths.FirstOrDefault();

        var output =
            string.Concat(_committed.Select(e => e.Output)) +
            (bestDisplay?.Output ?? "");

        var phoneticPreview =
            PhoneticProjector.Project(displayActive).Preview;

        _frames.Add(new EvidenceLatticeFrame(
            _input.Length,
            _input,
            committedAfter,
            string.Concat(_committed.Select(e => e.Output)),
            displayActive,
            phoneticPreview,
            displayDecode.CandidateEdges,
            displayDecode.TopPaths,
            consensusEnd,
            _consensusSignature,
            _consensusStableFrames,
            commits,
            output,
            expanded,
            _totalExpandedEdges,
            probes,
            _decoder.MozcAvailable,
            rebuilt));
    }

    private IReadOnlyList<LatticePathSnapshot> SelectConsensusPaths(
        IReadOnlyList<LatticePathSnapshot> paths)
    {
        if (paths.Count == 0)
            return [];

        var best = paths[0].Score;

        var selected = paths
            .Where(p =>
                best - p.Score <=
                _parameters.AlternativeScoreWindow)
            .Take(_parameters.ConsensusPathCount)
            .ToArray();

        if (selected.Length >= 2)
            return selected;

        return paths.Take(
            Math.Min(2, paths.Count)).ToArray();
    }

    private static IReadOnlyList<LatticeEdge> CommonEdgePrefix(
        IReadOnlyList<LatticePathSnapshot> paths)
    {
        if (paths.Count < 2)
            return [];

        var result = new List<LatticeEdge>();
        var index = 0;

        while (true)
        {
            if (paths.Any(p => p.Edges.Count <= index))
                break;

            var first = paths[0].Edges[index];

            if (paths.Skip(1).Any(p =>
                !SameEdge(first, p.Edges[index])))
                break;

            result.Add(first);
            index++;
        }

        return result;
    }

    private static bool CanCommitByConsensus(
        LatticeEdge edge,
        string activeRaw)
    {
        if (edge.Kind is
            LatticeEdgeKind.Unknown or
            LatticeEdgeKind.OpenPrefix or
            LatticeEdgeKind.BindingSymbol)
            return false;

        if (edge.Kind == LatticeEdgeKind.JapaneseMozc &&
            edge.MozcQuality < 0.65)
            return false;

        // A binding symbol immediately after this edge can change the
        // responsibility of the whole neighborhood:
        //   de + -ta  => データ (Mozc)
        //   node + .js => node.js (Literal)
        // Do not make the left edge irreversible until the symbol and its
        // right-hand side have been interpreted together.
        if (edge.End < activeRaw.Length &&
            InputSyntax.IsBindingSymbol(activeRaw[edge.End]))
            return false;

        return true;
    }

    private static IReadOnlyList<LatticeEdge> ClosePathAtHardBoundary(
        IReadOnlyList<LatticeEdge> path,
        string raw,
        int hardCommitEnd)
    {
        var selected = path
            .Where(e => e.End <= hardCommitEnd)
            .ToArray();

        if (selected.Length == 0)
            return [];

        // When the user explicitly types a hard separator after an unresolved
        // open token (e.g. "commi "), that separator closes the token as
        // literal. Do this as one span instead of permanently committing a
        // chain of Unknown characters.
        var boundaryIndex = Array.FindLastIndex(
            selected,
            e => e.Kind == LatticeEdgeKind.HardBoundary);

        if (boundaryIndex >= 0)
        {
            var content = selected.Take(boundaryIndex).ToArray();
            var boundary = selected[boundaryIndex];

            var unresolved =
                content.Any(e =>
                    e.Kind is
                        LatticeEdgeKind.OpenPrefix or
                        LatticeEdgeKind.Unknown or
                        LatticeEdgeKind.LiteralFallback);

            var hasWholeClosedExplanation =
                content.Length == 1 &&
                content[0].Start == 0 &&
                content[0].End == boundary.Start &&
                content[0].Kind is not (
                    LatticeEdgeKind.OpenPrefix or
                    LatticeEdgeKind.Unknown);

            if (content.Length > 0 &&
                content[0].Start == 0 &&
                !hasWholeClosedExplanation &&
                (
                    unresolved ||
                    content.Select(e => e.Language).Distinct().Count() > 1
                ))
            {
                var end = boundary.Start;
                var literalRaw = raw[..end];

                var literal = new LatticeEdge(
                    0,
                    end,
                    literalRaw,
                    literalRaw,
                    LanguageKind.English,
                    LatticeEdgeKind.LiteralFallback,
                    0,
                    -8,
                    -8,
                    "hard-boundary-literal-close");

                return [literal, boundary];
            }
        }

        return selected
            .Select(CloseAtHardBoundary)
            .ToArray();
    }

    private static LatticeEdge CloseAtHardBoundary(
        LatticeEdge edge)
    {
        if (edge.Kind is
            LatticeEdgeKind.Unknown or
            LatticeEdgeKind.OpenPrefix)
        {
            return edge with
            {
                Language = LanguageKind.English,
                Kind = LatticeEdgeKind.LiteralFallback,
                Evidence = "hard-boundary-literal-close"
            };
        }

        return edge;
    }

    private void CommitSegments(
        IReadOnlyList<LatticeEdge> segments,
        int globalOffset,
        string reason,
        List<LatticeCommitEvent> events)
    {
        foreach (var segment in segments)
        {
            var global = Globalize(
                segment,
                globalOffset);

            if (_committed.Count > 0 &&
                global.Start != _committed[^1].End)
                break;

            _committed.Add(global);
            events.Add(new LatticeCommitEvent(
                global.Start,
                global.End,
                global.Raw,
                global.Output,
                reason));
        }
    }

    private static int FindLastHardBoundaryEnd(string raw)
    {
        for (var i = raw.Length - 1; i >= 0; i--)
        {
            if (InputSyntax.IsHardBoundary(raw[i]) ||
                raw[i] == ' ')
                return i + 1;
        }

        return 0;
    }

    private static bool SameEdge(
        LatticeEdge left,
        LatticeEdge right) =>
        left.Start == right.Start &&
        left.End == right.End &&
        left.Output == right.Output &&
        left.Language == right.Language &&
        left.Kind == right.Kind;

    private static string EdgeSignature(LatticeEdge edge) =>
        $"{edge.Start}:{edge.End}:{edge.Output}:{edge.Language}:{edge.Kind}";

    private static LatticeEdge Globalize(
        LatticeEdge edge,
        int offset) =>
        edge with
        {
            Start = edge.Start + offset,
            End = edge.End + offset
        };

    private int CommittedRawLength() =>
        _committed.Count == 0
            ? 0
            : _committed[^1].End;

    private EvidenceLatticeResult CurrentResult()
    {
        if (_input.Length == 0)
            return new EvidenceLatticeResult(
                "", "", 0, [], [], []);

        var last = _frames[^1];
        var best = last.TopPaths.FirstOrDefault();

        var activeBest = best?.Edges
            .Select(e =>
                Globalize(
                    e,
                    last.CommittedRawLength))
            .ToArray() ?? [];

        return new EvidenceLatticeResult(
            _input,
            last.Output,
            last.CommittedRawLength,
            _committed.ToArray(),
            activeBest,
            _frames.ToArray());
    }
}
