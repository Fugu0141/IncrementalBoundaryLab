using BoundaryLab.Core;

var failures = new List<string>();

void Check(bool condition, string message)
{
    if (!condition)
        failures.Add(message);
}

PhoneticFirstAnalysisResult Run(string text)
{
    var session = new PhoneticFirstSession();
    return session.Update(text);
}

var sample = Run("kyouhacommitsimasita");
Check(sample.Output == "今日はcommitしました",
    $"basic output: {sample.Output}");
Check(sample.CommittedSegments.Any(s => s.Raw == "commit" && s.Language == LanguageKind.English),
    "commit must be recognized as English");
Check(sample.Frames[^1].Stage1.Raw == sample.Frames[^1].ActiveRaw,
    "stage1 must only describe the active window");

var methodCase = Run(
    "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo");
Check(methodCase.Output.Contains("method", StringComparison.Ordinal),
    $"method must survive as English: {methodCase.Output}");
Check(methodCase.CommittedSegments
        .Concat(methodCase.ActiveSegments)
        .Any(s => s.Raw == "method" &&
                  s.Language == LanguageKind.English),
    "method segment must be English");
Check(!methodCase.Output.Contains("th", StringComparison.Ordinal),
    $"method must not degrade into partial raw fragments: {methodCase.Output}");

var longCase = Run(
    "kyouhacommitasitanimotikosunogamenndoudattakarakousitayo");
Check(longCase.Output.StartsWith("今日はcommit明日に", StringComparison.Ordinal),
    $"long sample prefix: {longCase.Output}");

var nnCase = Run("kannzi");
Check(nnCase.Output is "感じ" or "かんじ",
    $"nn must be consumed as one ん: {nnCase.Output}");

var perfSession = new PhoneticFirstSession();
const string perfText =
    "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo";
for (var i = 1; i <= perfText.Length; i++)
    perfSession.Update(perfText[..i]);

var perf = perfSession.Update(perfText);
var last = perf.Frames[^1];
var naiveWork = (long)perfText.Length * (perfText.Length + 1) / 2;
Check(last.TotalAnalyzedCharacters < naiveWork,
    $"frozen-prefix path should do less work than full-prefix reanalysis: {last.TotalAnalyzedCharacters} >= {naiveWork}");
Check(perf.CommittedRawLength > 0,
    "long append-only input should freeze at least one prefix");

var report = PhoneticFirstResearchExporter.CreateReport(perf, perfSession.Parameters);
var json = PhoneticFirstResearchExporter.ToJson(report);
Check(json.Contains("phonetic-first-v1", StringComparison.Ordinal),
    "new report format must be phonetic-first-v1");
Check(json.Contains("\"stage1\"", StringComparison.Ordinal),
    "report must contain stage1 trace");
Check(json.Contains("\"stage2Candidates\"", StringComparison.Ordinal),
    "report must contain stage2 candidates");
Check(json.Contains("\"frozenThisStep\"", StringComparison.Ordinal),
    "report must contain freeze events");
Check(json.Contains("\"analyzedCharactersThisStep\"", StringComparison.Ordinal),
    "report must contain workload measurements");

Check(!IncrementalRecognizer.IsValidInput("abc123"), "digits must still be rejected");
Check(IncrementalRecognizer.IsValidInput("KyouHaCommit"), "ASCII letters are valid");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {failures.Count}");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("All phonetic-first self-tests passed.");
Console.WriteLine($"Basic: {sample.Output}");
Console.WriteLine($"Method: {methodCase.Output}");
Console.WriteLine($"Long: {longCase.Output}");
Console.WriteLine($"Work: {last.TotalAnalyzedCharacters} vs naive {naiveWork}");
return 0;
