namespace BoundaryLab.Core;

// New engine. Unlike v0.6 this has no tentative auto-commits. It commits
// only at explicit hard separators and never treats '.'/'-' as such.
public sealed class StreamHybridSession : IResearchSession
{
    public const string AlgorithmVersion = "iblab-stream-hybrid-v1";
    private const int MaxRetainedFrames = 256;

    private readonly StreamHybridDecoder _decoder;
    private readonly List<LatticeEdge> _committed = [];
    private readonly List<EvidenceLatticeFrame> _frames = [];
    private string _input = "";
    private int _committedLength;
    private long _expanded;

    public StreamHybridSession(IMozcConversionOracle? mozc = null)
    {
        Parameters = new EvidenceLatticeParameters(
            BeamWidth: 6,
            UsePhoneticFirstHybrid: true,
            EngineId: "stream-hybrid-v1");
        _decoder = new StreamHybridDecoder(mozc);
    }

    public EvidenceLatticeParameters Parameters { get; }
    public bool MozcAvailable => _decoder.MozcAvailable;

    public EvidenceLatticeResult Update(string raw)
    {
        if (!raw.All(InputSyntax.IsAllowed))
            throw new ArgumentException(
                "Input contains unsupported characters.", nameof(raw));
        raw = InputSyntax.Normalize(raw);

        if (!raw.StartsWith(_input, StringComparison.Ordinal))
        {
            Reset();
            foreach (var c in raw)
            {
                _input += c;
                Process(rebuilt: true);
            }
        }
        else
        {
            foreach (var c in raw[_input.Length..])
            {
                _input += c;
                Process(rebuilt: false);
            }
        }

        return Current();
    }

    public void Reset()
    {
        _input = "";
        _committed.Clear();
        _frames.Clear();
        _committedLength = 0;
        _expanded = 0;
    }

    private void Process(bool rebuilt)
    {
        var active = _input[_committedLength..];
        var result = _decoder.Decode(active);
        _expanded += result.Expanded;
        var lastPath = result.Paths.FirstOrDefault();
        var commits = new List<LatticeCommitEvent>();

        // A hard separator is an explicit user-supplied boundary. '.'/'-'
        // are contextual binding symbols and never trigger this action.
        if (active.Length > 0 &&
            InputSyntax.IsHardBoundary(active[^1]) &&
            lastPath is not null)
        {
            foreach (var edge in lastPath.Edges)
            {
                var global = edge with
                {
                    Start = edge.Start + _committedLength,
                    End = edge.End + _committedLength
                };
                _committed.Add(global);
                commits.Add(new(global.Start, global.End, global.Raw,
                    global.Output, "explicit-hard-delimiter"));
            }
            _committedLength = _input.Length;
        }

        var display = _input[_committedLength..];
        var committedOutput = string.Concat(_committed.Select(e => e.Output));
        var output = committedOutput +
            (_committedLength == _input.Length ? "" :
                lastPath?.Output ?? "");
        var structure = StreamHybridStructure.Analyze(active, result.Paths);
        var frame = new EvidenceLatticeFrame(
            _input.Length, _input, _committedLength, committedOutput,
            display, result.PhoneticPreview,
            result.DiagnosticEdges, result.Paths,
            0, "", 0, commits, output,
            result.Expanded, _expanded, result.MozcProbes,
            _decoder.MozcAvailable, rebuilt)
        { PhoneticStructure = structure };
        _frames.Add(frame);
        // Keep recent keypress diagnostics, not an unbounded giant JSON.
        if (_frames.Count > MaxRetainedFrames)
            _frames.RemoveRange(0, _frames.Count - MaxRetainedFrames);
    }

    private EvidenceLatticeResult Current()
    {
        if (_frames.Count == 0)
            return new("", "", 0, [], [], []);

        var last = _frames[^1];
        var active = _committedLength == _input.Length
            ? Array.Empty<LatticeEdge>()
            : last.TopPaths.FirstOrDefault()?.Edges.ToArray()
              ?? Array.Empty<LatticeEdge>();
        return new(_input, last.Output, _committedLength,
            _committed.ToArray(), active, _frames.ToArray());
    }
}
