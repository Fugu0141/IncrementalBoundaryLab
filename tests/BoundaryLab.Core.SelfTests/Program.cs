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
Check(sample.Boundaries.Any(b => b.Position == 6 && b.Probability > 0.70),
    "boundary after kyouha should be strong");
Check(sample.Boundaries.Any(b => b.Position == 12 && b.Probability > 0.70),
    "boundary after commit should be strong");

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

Check(!IncrementalRecognizer.IsValidInput("abc123"), "digits must be rejected");
Check(!IncrementalRecognizer.IsValidInput("abc def"), "spaces must be rejected");
Check(IncrementalRecognizer.IsValidInput("KyouHaCommit"), "ASCII letters are valid");

var report = ResearchExporter.CreateReport(sample, recognizer.Parameters);
var json = ResearchExporter.ToJson(report);
Check(json.Contains("\"frames\"", StringComparison.Ordinal), "report must contain frames");
Check(json.Contains("\"topHypotheses\"", StringComparison.Ordinal), "report must contain hypotheses");
Check(json.Contains("\"finalBoundaries\"", StringComparison.Ordinal), "report must contain boundaries");

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
return 0;
