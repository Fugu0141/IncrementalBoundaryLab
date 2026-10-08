using BoundaryLab.Core;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

EvidenceLatticeResult Run(string input)
{
    var session = new EvidenceLatticeSession();
    return session.Update(input);
}

var basic = Run("kyouhacommitsimasita");
Check(basic.Output == "今日はcommitしました",
    "basic: " + basic.Output);

var issue = Run("kyouissueha");
Check(issue.Output == "今日issueは",
    "issue: " + issue.Output);

var debug = Run("kyoudebugni");
Check(debug.Output == "今日debugに",
    "debug: " + debug.Output);

var node = Run("node.jswotukau");
Check(node.Output == "node.jsを使う",
    "node: " + node.Output);

var longNode = Run(
    "demotyottomonndainanoganode.jstoiukotoba");
Check(longNode.Output ==
      "でもちょっともんだいなのがnode.jsということば",
    "long node: " + longNode.Output);
Check(!longNode.ActiveBestSegments.Any(s =>
        s.Raw.StartsWith("demo", StringComparison.Ordinal) &&
        s.Kind == LatticeEdgeKind.LatinStructural),
    "node.js structural token must not swallow preceding Japanese");

var real = Run(
    "hennkannnikannsitehacommittokanoeigoiretemondainasasoudane demotyottomonndainanoganode.jstoiukotobagabunnkatudekitenainndayone");
Check(real.Output ==
      "へんかんにかんしてはcommitとかのえいごいれてもんだいなさそうだね でもちょっともんだいなのがnode.jsということばがぶんかつできてないんだよね",
    "real: " + real.Output);

var early = new EvidenceLatticeSession();
early.Update("henn");
var atFour = early.Update("henn");
Check(atFour.CommittedRawLength == 0,
    "henn must not prematurely commit he as particle");

var later = early.Update("hennkann");
Check(!later.CommittedSegments.Any(s =>
        s.Start == 0 &&
        s.End == 2 &&
        s.Output == "へ"),
    "he particle must not become irreversible inside hennkann");

var anime = Run("animeha");
Check(anime.Output == "アニメは",
    "anime: " + anime.Output);

var repo = Run("repoha");
Check(repo.Output == "れぽは",
    "short ambiguous repo should stay phonetic by default: " + repo.Output);

var determinismInput =
    "hennkannnikannsitehacommitnode.jswotukau";
var a = Run(determinismInput);
var b = Run(determinismInput);
Check(Signature(a) == Signature(b),
    "decoder must be deterministic");

Check(real.Frames.All(f =>
    f.TopPaths.Count <=
    new EvidenceLatticeParameters().BeamWidth),
    "beam width must be respected");

var report = EvidenceLatticeResearchExporter.CreateReport(
    real,
    new EvidenceLatticeSession().Parameters);
var json = EvidenceLatticeResearchExporter.ToJson(report);
Check(json.Contains("evidence-lattice-v1", StringComparison.Ordinal),
    "report format");
Check(json.Contains("candidateEdges", StringComparison.Ordinal),
    "report must contain lattice edges");
Check(json.Contains("topPaths", StringComparison.Ordinal),
    "report must contain top paths");
Check(json.Contains("commitEvents", StringComparison.Ordinal),
    "report must contain commit events");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("All Evidence Lattice v0.5 tests passed.");
Console.WriteLine("Basic: " + basic.Output);
Console.WriteLine("Issue: " + issue.Output);
Console.WriteLine("Debug: " + debug.Output);
Console.WriteLine("Node: " + node.Output);
Console.WriteLine("Real: " + real.Output);
return 0;

static string Signature(EvidenceLatticeResult result) =>
    result.Output + "|" +
    result.CommittedRawLength + "|" +
    string.Join(";", result.CommittedSegments
        .Concat(result.ActiveBestSegments)
        .Select(e =>
            $"{e.Start}-{e.End}:{e.Raw}>{e.Output}:{e.Language}:{e.Kind}"));
