namespace BoundaryLab.Core;

public sealed class EvidenceLatticeSession
{
    public const string AlgorithmVersion =
        "iblab-evidence-lattice-delayed-commit-v0.5";

    private readonly EvidenceLatticeParameters _parameters;
    private readonly EvidenceLatticeDecoder _decoder;
    private readonly List<LatticeEdge> _committed = [];
    private readonly List<EvidenceLatticeFrame> _frames = [];

    private string _input = "";
    private string _consensusSignature = "";
    private int _consensusStableFrames;
    private long _totalExpandedEdges;

    public EvidenceLatticeSession(
        EvidenceLatticeParameters? parameters = null)
    {
        _parameters =
            parameters ?? new EvidenceLatticeParameters();
        _decoder = new EvidenceLatticeDecoder(_parameters);
    }

    public EvidenceLatticeParameters Parameters => _parameters;

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
        var commits = new List<LatticeCommitEvent>();

        var eligible = SelectConsensusPaths(decode.TopPaths);
        var common = CommonEdgePrefix(eligible);
        var consensusEnd =
            common.Count == 0 ? 0 : common[^1].End;

        var stableCandidate = common
            .Where(e =>
                e.End <=
                activeRaw.Length -
                _parameters.CommitLookahead)
            .FirstOrDefault();

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
            var hardSegments = best.Edges
                .Where(e => e.End <= hardCommitEnd)
                .ToArray();

            if (hardSegments.Length > 0 &&
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
        }
        else
        {
            displayActive = activeRaw;
            displayDecode = decode;
        }

        _totalExpandedEdges += expanded;

        var bestDisplay =
            displayDecode.TopPaths.FirstOrDefault();
        var activeBest = bestDisplay?.Edges
            .Select(e => Globalize(e, committedAfter))
            .ToArray() ?? [];

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
