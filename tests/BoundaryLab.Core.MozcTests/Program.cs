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
var mixedSegments = string.Join(
    " || ",
    mixed.CommittedSegments
        .Concat(mixed.ActiveBestSegments)
        .Select(e =>
            $"{e.Start}-{e.End}:{e.Raw}>{e.Output}:{e.Kind}:mozc={e.MozcQuality:F2}"));

var hyphenFrame = mixed.Frames
    .LastOrDefault(f => f.Input.Contains("de-ta", StringComparison.Ordinal));

var hyphenPaths = hyphenFrame is null
    ? "<none>"
    : string.Join(
        " // ",
        hyphenFrame.TopPaths.Take(5).Select(p =>
            $"{p.RelativeScore:F2}:{p.Segmentation}>{p.Output}"));

Check(mixed.Output == "commitしたデータをgithubにpushしてください",
    "mixed output: " + mixed.Output +
    " | segments: " + mixedSegments +
    " | hyphenPaths: " + hyphenPaths);

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


EvidenceLatticeResult RunOffline(string input, bool hybrid)
{
    var parameters = new EvidenceLatticeParameters(
        UsePhoneticFirstHybrid: hybrid);
    return new EvidenceLatticeSession(parameters).Update(input);
}

// Original research failures: preserve the baseline exactly and compare the
// new phonetic-first signal with the same input (no fake Mozc oracle).
var baselineOre = RunOffline("oreha", hybrid: false);
var baselineData = RunOffline("de-ta", hybrid: false);
Check(baselineOre.Output == "orえは",
    "v0.6 baseline oreha unexpectedly changed: " + baselineOre.Output);
Check(baselineData.Output == "de-tあ",
    "v0.6 baseline de-ta unexpectedly changed: " + baselineData.Output);

var hybridOre = RunOffline("oreha", hybrid: true);
var hybridData = RunOffline("de-ta", hybrid: true);
Check(hybridOre.Output == "おれは",
    "hybrid should protect fully readable oreha: " + hybridOre.Output);
Check(hybridData.Output == "でーた",
    "hybrid should treat de-ta as kana long vowel (not kanji): " + hybridData.Output);
Check(hybridOre.ActiveBestSegments.Any(
        e => e.Evidence == "phonetic-lattice-left-to-right"),
    "oreha needs phonetic-first evidence");
Check(hybridData.ActiveBestSegments.Any(
        e => e.Evidence == "phonetic-first-prolonged-vowel"),
    "de-ta needs a long-vowel alternative");

foreach (var token in new[]
{
    "commit", "theory", "node.js", "githubdeissue",
    "kyouhacommitsimasita", "networkmiru"
})
{
    var oldResult = RunOffline(token, hybrid: false);
    var newResult = RunOffline(token, hybrid: true);
    Check(newResult.Output == oldResult.Output,
        "hybrid regression on " + token + ": " +
        newResult.Output + " != " + oldResult.Output +
        " | segments: " +
        string.Join(" / ", newResult.CommittedSegments
            .Concat(newResult.ActiveBestSegments)
            .Select(e => e.Raw + "=>" + e.Output + ":" +
                e.Kind + ":" + e.Evidence + ":" +
                e.LocalScore.ToString("F2"))));
}

var hybridMixed = RunOffline(
    "commitsitade-tawogithubnipushsitekudasai", hybrid: true);
Check(hybridMixed.Output.Contains("でーた", StringComparison.Ordinal),
    "mixed input must preserve the de-ta long-vowel reading: " +
    hybridMixed.Output);

var hybridReport = EvidenceLatticeResearchExporter.CreateReport(
    hybridOre,
    new EvidenceLatticeParameters(UsePhoneticFirstHybrid: true));
Check(hybridReport.AlgorithmVersion == "iblab-phonetic-first-hybrid-v0.7",
    "hybrid research JSON must declare the actual scoring variant");


var ambiguousOre = PhoneticStructureAnalyzer.Analyze("oreha");
Check(ambiguousOre.Groups.Any(g =>
        g.Raw == "oreha" &&
        g.Status == PhoneticEvidenceStatus.Tentative),
    "oreha is readable but must stay structurally tentative due to 'or'");
Check(ambiguousOre.Boundaries.Any(b =>
        b.Position == 2 &&
        b.Status == PhoneticEvidenceStatus.Tentative),
    "'or' ends inside the kana token 're': expose an ambiguous cut at 2");

var ambiguousHyphen = PhoneticStructureAnalyzer.Analyze("de-ta");
Check(ambiguousHyphen.Boundaries.Any(b =>
        b.Position == 2 &&
        b.Status == PhoneticEvidenceStatus.Tentative),
    "hyphen should not be a confirmed language cut");
Check(ambiguousHyphen.ReviewWindows.Any(w =>
        w.Start <= 2 && w.End >= 4),
    "hyphen must request bilateral review");

var ambiguousPeriod = PhoneticStructureAnalyzer.Analyze("node.js");
Check(ambiguousPeriod.Boundaries.Any(b =>
        b.Position == 4 &&
        b.Status == PhoneticEvidenceStatus.Tentative),
    "'.' must be an ambiguous cut even within an apparent code token");
Check(PhoneticStructureAnalyzer.Analyze("nihongo.").Boundaries.Any(b =>
        b.Position == 7 &&
        b.Status == PhoneticEvidenceStatus.Tentative),
    "trailing period is not automatically a confirmed English boundary");

var firmComma = PhoneticStructureAnalyzer.Analyze("oreha,commit");
Check(firmComma.Boundaries.Any(b =>
        b.Position == 5 &&
        b.Status == PhoneticEvidenceStatus.Confirmed),
    "explicit comma must yield a confirmed structural cut");

var unreadableEnglish = PhoneticStructureAnalyzer.Analyze("englishwomanabu");
Check(unreadableEnglish.Groups.Any(g =>
        g.Kind == "Unresolved" &&
        g.Status == PhoneticEvidenceStatus.Tentative),
    "unreadable English must not be split into firmly committed kana");
Check(unreadableEnglish.ReviewWindows.Any(w =>
        w.Start == 0 && w.End >= 10),
    "unreadable English must trigger bilateral review including prefix/suffix");

var fourStateParameters = new EvidenceLatticeParameters(
    UsePhoneticFirstHybrid: true);
var fourStateTrace = new EvidenceLatticeSession(
    fourStateParameters).Update("oreha");
Check(fourStateTrace.Frames[^1].PhoneticStructure is not null,
    "four-state phonetic annotations must appear in incremental JSON frames");
var fourStateJson = EvidenceLatticeResearchExporter.ToJson(
    EvidenceLatticeResearchExporter.CreateReport(
        fourStateTrace, fourStateParameters));
Check(fourStateJson.Contains("iblab-phonetic-four-state-v0.8",
        StringComparison.Ordinal) &&
      fourStateJson.Contains("reviewWindows", StringComparison.Ordinal),
    "research JSON must identify experiment and include review windows");
var baselineNoAnnotations = new EvidenceLatticeSession().Update("oreha");
Check(baselineNoAnnotations.Frames[^1].PhoneticStructure is null,
    "v0.6 baseline must not implicitly enable four-state analysis");

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
