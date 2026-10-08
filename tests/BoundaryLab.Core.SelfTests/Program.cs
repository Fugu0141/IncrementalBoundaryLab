using BoundaryLab.Core;

var recognizer = new IncrementalRecognizer();
var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

var sample = recognizer.Analyze("kyouhacommitsimasita");
Check(sample.Converted == "今日はcommitしました",
    $"sample conversion: {sample.Converted}");
Check(string.Join("|", sample.Segments.Select(s => s.Raw)) ==
      "kyou|ha|commit|simasita",
    $"sample segmentation: {string.Join("|", sample.Segments.Select(s => s.Raw))}");
Check(sample.Frames.Count == "kyouhacommitsimasita".Length,
    "one frame must exist for every input character");
Check(sample.Boundaries.Any(b => b.Position == 6 && b.IndependentSupport >= 2),
    "boundary after kyouha needs independent support");
Check(sample.Boundaries.Any(b => b.Position == 12 && b.IndependentSupport >= 2),
    "boundary after commit needs independent support");

var mixed = new Dictionary<string, string>
{
    ["commitsuru"] = "commitする",
    ["githubdeissue"] = "githubでissue",
    ["networkmiru"] = "networkみる",
    ["the"] = "the",
    ["thennado"] = "thenなど"
};

foreach (var pair in mixed)
{
    var actual = recognizer.Analyze(pair.Key).Converted;
    Check(actual == pair.Value, $"{pair.Key}: {actual} != {pair.Value}");
}

var researchCase =
    recognizer.Analyze("kyouhacommitasitanisitakunakattakaraimamergesityattayo");
Check(researchCase.Converted == "今日はcommit明日にしたくなかったから今mergeしちゃったよ",
    $"research sample conversion: {researchCase.Converted}");
Check(researchCase.Segments.Any(s => s.Raw == "asita" && s.Converted == "明日"),
    "kunrei-style asita must remain a Japanese candidate");
Check(!researchCase.Segments.Any(s => s.Raw == "a" && s.Language == LanguageKind.Unknown),
    "research sample must not split asita into unknown a + sita");

foreach (var boundary in researchCase.Boundaries.Where(b => b.Confirmed))
{
    Check(boundary.IndependentSupport >= 2,
        $"confirmed boundary {boundary.Position} has only {boundary.IndependentSupport} independent votes");
}

Check(!IncrementalRecognizer.IsValidInput("abc123"), "digits must be rejected");
Check(!IncrementalRecognizer.IsValidInput("abc def"), "spaces must be rejected");
Check(IncrementalRecognizer.IsValidInput("KyouHaCommit"), "ASCII letters are valid");

var report = ResearchExporter.CreateReport(sample, recognizer.Parameters);
var json = ResearchExporter.ToJson(report);
Check(json.Contains("\"frames\"", StringComparison.Ordinal), "report must contain frames");
Check(json.Contains("\"topHypotheses\"", StringComparison.Ordinal), "report must contain hypotheses");
Check(json.Contains("\"finalBoundaries\"", StringComparison.Ordinal), "report must contain boundaries");
Check(json.Contains("\"bidirectionalProbability\"", StringComparison.Ordinal),
    "report must contain bidirectional boundary evidence");
Check(json.Contains("\"lexicalProbability\"", StringComparison.Ordinal),
    "report must contain lexical boundary evidence");
Check(json.Contains("\"independentSupport\"", StringComparison.Ordinal),
    "report must contain independent support count");
Check(json.Contains("research-v2", StringComparison.Ordinal),
    "report must use research-v2 schema");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("All BoundaryLab self-tests passed.");
Console.WriteLine($"Sample: kyouhacommitsimasita -> {sample.Converted}");
Console.WriteLine("Segments: " + string.Join(" | ", sample.Segments.Select(s => s.Raw)));
Console.WriteLine("Research sample: " + researchCase.Converted);
return 0;
