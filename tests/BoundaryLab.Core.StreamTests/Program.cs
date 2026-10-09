using System.Diagnostics;
using BoundaryLab.Core;

var failures = new List<string>();
void Check(bool ok, string message)
{
    if (!ok) failures.Add(message);
}

EvidenceLatticeResult Run(string input) =>
    new StreamHybridSession().Update(input);

var cases = new[]
{
    ("oreha", "おれは"),
    ("de-ta", "でーた"),
    ("de-taganaidesune", "でーたがないですね"),
    ("nihongo", "日本語"),
    ("commit", "commit"),
    ("theory", "theory"),
    ("node.js", "node.js"),
    ("githubdeissue", "githubでissue"),
    ("networkmiru", "networkみる"),
    ("kyouhacommitsimasita", "今日はcommitしました")
};
foreach (var (input, expected) in cases)
{
    var x = Run(input);
    Console.WriteLine($"CASE {input}: {x.Output}");
    Check(x.Output == expected,
        $"expected {input} -> {expected}, got {x.Output}");
    Check(x.Frames.All(f =>
            f.TopPaths.Count <= 4 &&
            f.CandidateEdges.Count <= 80),
        "unbounded trace output for " + input);
}

var mixed = Run("tatoebathetoiukotobaha,englishwomanabunihadaizinakotodesu");
Console.WriteLine("MIXED " + mixed.Output);
Console.WriteLine("MIXED SEGMENTS " + string.Join(" / ",
    mixed.CommittedSegments.Concat(mixed.ActiveBestSegments)
        .Select(e => $"{e.Raw}=>{e.Output}:{e.Kind}:{e.LocalScore:F2}")));
Console.WriteLine("MIXED PATHS " + string.Join(" || ",
    mixed.Frames[^1].TopPaths.Take(3).Select(p =>
        $"{p.Score:F2}:{p.Segmentation}=>{p.Output}")));
Check(mixed.Output.StartsWith("たとえばthe", StringComparison.Ordinal),
    "lost 'the' after Japanese: " + mixed.Output);
Check(mixed.Output.Contains("english", StringComparison.Ordinal),
    "unknown English must remain one literal island: " + mixed.Output);
Check(!mixed.Output.Contains("glいsh", StringComparison.Ordinal),
    "unknown word fragment still present: " + mixed.Output);

var period = Run("nihongo.");
Console.WriteLine("PERIOD " + period.Output);
Check(period.Output == "日本語.",
    "period following Japanese should remain punctuation: " + period.Output);
var nodeSentence = Run("node.jswotukau");
Console.WriteLine("NODE " + nodeSentence.Output);
Check(nodeSentence.Output == "node.jsを使う",
    "code identifier with Japanese must survive: " + nodeSentence.Output);

var hard = Run("oreha,english");
Check(hard.CommittedRawLength == "oreha,".Length,
    "hard cut should commit only through the comma");
Check(hard.CommittedSegments.All(e => e.End <= "oreha,".Length),
    "no segment beyond explicit hard cut should be committed");
var dot = Run("node.");
Check(dot.CommittedRawLength == 0,
    "period must not force an irreversible commit");

foreach (var input in new[] { "oreha.", "github.comde", "nihongo.node.js" })
{
    var result = Run(input);
    Console.WriteLine("PERIOD_CASE " + input + " -> " + result.Output);
    Check(result.CommittedRawLength == 0,
        "'.' must not cause irreversible commit: " + input);
    Check(result.Output.Contains('.'),
        "'.' must never disappear or become kana: " + input);
}

var longStream = new string('a', 270);
var bounded = Run(longStream);
Check(bounded.Frames.Count <= 256,
    "v1 per-keypress trace must remain bounded");
Check(bounded.Frames[^1].CandidateEdges.Count <= 80,
    "v1 diagnostic edge export must remain bounded");
Console.WriteLine($"BOUNDED input={longStream.Length} frames={bounded.Frames.Count} " +
                  $"expanded={bounded.Frames[^1].TotalExpandedEdges}");

var steady = new StreamHybridSession();
var before = steady.Update("ore");
var after = steady.Update("oreha");
var rebuilt = Run("oreha");
Check(after.Output == rebuilt.Output,
    "stream incremental/full replay must match");
Check(after.Frames.All(f => f.Step > 0),
    "invalid per-frame position");
Check(after.Frames[^1].PhoneticStructure is not null,
    "four-state structure must derive from selected candidate paths");

var report = EvidenceLatticeResearchExporter.CreateReport(
    after, steady.Parameters);
Check(report.AlgorithmVersion == StreamHybridSession.AlgorithmVersion,
    "v1 research report must identify correct engine");
Check(EvidenceLatticeResearchExporter.ToJson(report).Contains(
        "reviewWindows", StringComparison.Ordinal),
    "research JSON must contain four-state context windows");

// Bridge calls must not happen implicitly on each keystroke.
// With explicit opt-in, a fake independent converter may supply a
// Japanese display candidate; actual Windows bridge quality is untested.
var originalBridgeSetting =
    Environment.GetEnvironmentVariable("BOUNDARYLAB_STREAM_MOZC");
try
{
    Environment.SetEnvironmentVariable("BOUNDARYLAB_STREAM_MOZC", null);
    using (var disabledOracle = new CountingOracle())
    {
        var disabled = new StreamHybridSession(disabledOracle)
            .Update("de-ta");
        Check(disabledOracle.Probes == 0,
            "new engine must not silently query synchronous Mozc");
        Check(disabled.Output == "でーた",
            "disabling bridge must preserve local kana hypothesis");
    }
    Environment.SetEnvironmentVariable("BOUNDARYLAB_STREAM_MOZC", "1");
    using (var enabledOracle = new CountingOracle())
    {
        var enabled = new StreamHybridSession(enabledOracle)
            .Update("de-ta");
        Check(enabledOracle.Probes <= "de-ta".Length && enabledOracle.Probes > 0,
            "enabled Mozc must be bounded to at most one probe/keypress");
        Check(enabled.Output == "データ",
            "selected real/bridge candidate must be displayed when available: " +
            enabled.Output);
        Check(enabled.Frames[^1].MozcProbesThisStep <= 1,
            "one-probe budget violated");
    }
}
finally
{
    Environment.SetEnvironmentVariable(
        "BOUNDARYLAB_STREAM_MOZC", originalBridgeSetting);
}

// Representative probe, not an absolute speed benchmark. GitHub Actions
// runners vary. Compare the same 57-character input and log both times.
const string perfText =
    "tatoebathetoiukotobaha,englishwomanabunihadaizinakotodesu";
var legacyWatch = Stopwatch.StartNew();
var legacy = new EvidenceLatticeSession(
    new EvidenceLatticeParameters(UsePhoneticFirstHybrid: true))
    .Update(perfText);
legacyWatch.Stop();
var streamWatch = Stopwatch.StartNew();
var stream = Run(perfText);
streamWatch.Stop();
Console.WriteLine(
    $"PERF legacyMs={legacyWatch.Elapsed.TotalMilliseconds:F1}, " +
    $"streamMs={streamWatch.Elapsed.TotalMilliseconds:F1}, " +
    $"legacyExpanded={legacy.Frames[^1].TotalExpandedEdges}, " +
    $"streamExpanded={stream.Frames[^1].TotalExpandedEdges}");
Check(stream.Frames[^1].TotalExpandedEdges <
      legacy.Frames[^1].TotalExpandedEdges,
    "v1 expands too many candidate transitions against legacy");

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILED: " + failures.Count);
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("Stream Hybrid v1 core tests passed.");
return 0;

internal sealed class CountingOracle : IMozcConversionOracle
{
    public int Probes { get; private set; }
    public bool IsAvailable => true;
    public MozcProbeResult Probe(string raw)
    {
        Probes++;
        if (raw == "de-ta")
            return new(true, true, raw, "でーた", ["データ"], 0.98);
        return new(true, false, raw, "", [], 0);
    }
    public void Dispose() { }
}
