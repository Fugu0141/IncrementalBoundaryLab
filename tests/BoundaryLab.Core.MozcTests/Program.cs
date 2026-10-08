using BoundaryLab.Core;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

using var mozc = new FakeMozcOracle(new Dictionary<string, string>
{
    ["de-ta"] = "データ",
    ["kudasai"] = "ください",
    ["sitekudasai"] = "してください",
    ["hennkann"] = "変換",
    ["hennkannnikannsiteha"] = "変換に関しては"
});

EvidenceLatticeResult Run(string input, IMozcConversionOracle? oracle = null)
{
    var session = new EvidenceLatticeSession(mozc: oracle);
    return session.Update(input);
}

var openSession = new EvidenceLatticeSession(mozc: mozc);
var open = openSession.Update("commi");
Check(open.CommittedRawLength == 0,
    "commi must remain Open and must not commit c");
Check(open.Frames[^1].CandidateEdges.Any(e =>
        e.Start == 0 &&
        e.End == 5 &&
        e.Kind == LatticeEdgeKind.OpenPrefix),
    "commi must have an OpenPrefix candidate");
Check(!open.CommittedSegments.Any(e =>
        e.Kind is LatticeEdgeKind.OpenPrefix or LatticeEdgeKind.Unknown),
    "Open/Unknown must never be normal committed");

var data = Run("de-ta", mozc);
Check(data.Output == "データ",
    "Mozc should resolve de-ta as Japanese: " + data.Output);
Check(data.ActiveBestSegments.Any(e =>
        e.Kind == LatticeEdgeKind.JapaneseMozc &&
        e.MozcQuality >= 0.9),
    "de-ta must be backed by strong Mozc evidence");
Check(!data.ActiveBestSegments.Any(e =>
        e.Kind == LatticeEdgeKind.LatinStructural &&
        e.Raw == "de-ta"),
    "de-ta must not win as a Latin structural token when Mozc strongly converts it");

var node = Run("node.js", mozc);
Check(node.Output == "node.js",
    "node.js must remain literal: " + node.Output);
Check(node.ActiveBestSegments.Any(e =>
        e.Kind == LatticeEdgeKind.LatinStructural &&
        e.Raw == "node.js"),
    "node.js must be selected as structural Latin");

var mixed = Run(
    "commitsitade-tawogithubnipushsitekudasai",
    mozc);
Check(mixed.Output == "commitしたデータをgithubにpushしてください",
    "mixed output: " + mixed.Output);

var hard = Run("commi ", mozc);
Check(hard.CommittedRawLength == "commi ".Length,
    "hard boundary should close unresolved Open token");
Check(hard.CommittedSegments.Any(e =>
        e.Raw == "commi" &&
        e.Kind == LatticeEdgeKind.LiteralFallback),
    "explicit boundary should close Open as literal");

var report = EvidenceLatticeResearchExporter.CreateReport(
    mixed,
    new EvidenceLatticeSession(mozc: mozc).Parameters);
var json = EvidenceLatticeResearchExporter.ToJson(report);
Check(json.Contains("mozc-responsibility-v1", StringComparison.Ordinal),
    "research format must be Mozc responsibility v1");
Check(json.Contains("mozcQuality", StringComparison.Ordinal),
    "research JSON must contain Mozc quality");
Check(json.Contains("mozcTopCandidate", StringComparison.Ordinal),
    "research JSON must contain Mozc top candidate");
Check(json.Contains("mozcProbesThisStep", StringComparison.Ordinal),
    "research JSON must contain Mozc probe count");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("All Mozc Responsibility IME v0.6 tests passed.");
Console.WriteLine("Open: " + open.Output);
Console.WriteLine("de-ta: " + data.Output);
Console.WriteLine("node.js: " + node.Output);
Console.WriteLine("mixed: " + mixed.Output);
return 0;

sealed class FakeMozcOracle : IMozcConversionOracle
{
    private readonly IReadOnlyDictionary<string, string> _map;

    public FakeMozcOracle(IReadOnlyDictionary<string, string> map)
    {
        _map = map;
    }

    public bool IsAvailable => true;

    public MozcProbeResult Probe(string raw)
    {
        if (_map.TryGetValue(raw, out var value))
        {
            return new(
                true,
                true,
                raw,
                value,
                [value, raw],
                0.98);
        }

        if (RomajiReadable(raw))
        {
            return new(
                true,
                true,
                raw,
                raw,
                [raw],
                0.30);
        }

        return new(
            true,
            true,
            raw,
            raw,
            [raw],
            0.10);
    }

    private static bool RomajiReadable(string raw) =>
        raw.All(c => char.IsAsciiLetter(c));

    public void Dispose()
    {
    }
}
