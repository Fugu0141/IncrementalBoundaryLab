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
    "basic output: " + sample.Output);
Check(sample.CommittedSegments
        .Concat(sample.ActiveSegments)
        .Any(s =>
            s.Raw == "commit" &&
            s.Language == LanguageKind.English),
    "commit must be recognized as English");

var methodCase = Run(
    "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo");
Check(methodCase.Output ==
      "精度はすごくいい感じになったからmethodとしてはこんな感じでいいかも",
    "method sentence: " + methodCase.Output);

var node = Run("node.js");
Check(node.Output == "node.js",
    "node.js must remain literal: " + node.Output);
Check(node.CommittedSegments
        .Concat(node.ActiveSegments)
        .Any(s =>
            s.Raw == "node.js" &&
            s.Language == LanguageKind.English &&
            s.DecisionReason ==
                "stage2-orthographic-latin-token"),
    "node.js must be protected by orthographic evidence");
Check(node.Frames.Any(f =>
        f.SymbolEvidence.Any(e =>
            e.Kind == "LatinBindingToken")),
    "node.js must emit LatinBindingToken evidence");
Check(node.Frames.Any(f =>
        f.SymbolEvidence.Any(e =>
            e.Kind == "IncompleteBindingToken")),
    "node. must first emit incomplete binding-token evidence");

var thawProbe = Run("kyouhaq");
Check(thawProbe.Frames.Any(f => f.ThawEvents.Count > 0),
    "nearby unresolved input must thaw a SoftFrozen prefix");

var nodeJapanese = Run("node.jswotukau");
Check(nodeJapanese.Output == "node.jsを使う",
    "node.js + Japanese suffix: " + nodeJapanese.Output);

var structuralLatin = new Dictionary<string, string>
{
    ["foo_bar"] = "foo_bar",
    ["c#"] = "c#",
    ["v2.0"] = "v2.0",
    ["github.com"] = "github.com"
};

foreach (var pair in structuralLatin)
{
    var actual = Run(pair.Key);
    Check(actual.Output == pair.Value,
        pair.Key + " must remain a structural Latin token: " + actual.Output);
    Check(actual.Frames.Any(f =>
            f.SymbolEvidence.Any(e =>
                e.Kind == "LatinBindingToken")),
        pair.Key + " must emit LatinBindingToken evidence");
}

var mawasu = Run("mawasu");
Check(mawasu.Output == "まわす",
    "mawasu should not be ma|wa|su: " + mawasu.Output);
Check(!mawasu.CommittedSegments
        .Concat(mawasu.ActiveSegments)
        .Any(s =>
            s.Raw == "wa" &&
            s.DecisionReason ==
                "stage2-japanese-particle"),
    "wa inside mawasu must not become a particle");

var rippleSession = new PhoneticFirstSession();
const string rippleText = "kyouzyuunitaiousiteoitayo";
for (var i = 1; i <= rippleText.Length; i++)
    rippleSession.Update(rippleText[..i]);

var ripple = rippleSession.Update(rippleText);
Check(!ripple.Output.Contains('z'),
    "zyu must not remain unresolved: " + ripple.Output);
Check(ripple.Output.StartsWith("今日じゅう", StringComparison.Ordinal),
    "kyouzyuu must remain locally revisable: " + ripple.Output);
Check(ripple.Frames.Any(f => f.RippleEvents.Count > 0),
    "ripple events must be recorded around uncertainty");

var punctuation = Run("kyouhaame!");
Check(punctuation.Output.EndsWith("!", StringComparison.Ordinal),
    "hard punctuation must survive: " + punctuation.Output);
Check(punctuation.Frames[^1].SymbolEvidence.Any(e =>
        e.Kind == "HardBoundary"),
    "hard punctuation must emit boundary evidence");

var perfSession = new PhoneticFirstSession();
const string perfText =
    "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo";
for (var i = 1; i <= perfText.Length; i++)
    perfSession.Update(perfText[..i]);

var perf = perfSession.Update(perfText);
var last = perf.Frames[^1];
var naiveWork =
    (long)perfText.Length *
    (perfText.Length + 1) / 2;

Check(last.TotalAnalyzedCharacters < naiveWork,
    "RCR must remain cheaper than full-prefix reanalysis: " +
    last.TotalAnalyzedCharacters + " >= " + naiveWork);

var report =
    PhoneticFirstResearchExporter.CreateReport(
        ripple,
        rippleSession.Parameters);
var json =
    PhoneticFirstResearchExporter.ToJson(report);

Check(json.Contains("phonetic-first-rcr-v2",
        StringComparison.Ordinal),
    "report format must be RCR v2");
Check(json.Contains("\"rippleEvents\"",
        StringComparison.Ordinal),
    "report must contain ripple events");
Check(json.Contains("\"thawEvents\"",
        StringComparison.Ordinal),
    "report must contain thaw events");
Check(json.Contains("\"symbolEvidence\"",
        StringComparison.Ordinal),
    "report must contain symbol evidence");
Check(json.Contains("\"freezeTransitions\"",
        StringComparison.Ordinal),
    "report must contain freeze transitions");

Check(InputSyntax.IsAllowed('.'),
    "period must be accepted");
Check(InputSyntax.IsAllowed('#'),
    "binding symbols must be accepted");
Check(InputSyntax.IsAllowed('2'),
    "digits must be accepted");

if (failures.Count > 0)
{
    Console.Error.WriteLine(
        "FAILED: " + failures.Count);
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine(
    "All phonetic-first RCR self-tests passed.");
Console.WriteLine("Basic: " + sample.Output);
Console.WriteLine("Node: " + node.Output);
Console.WriteLine("Node+JA: " + nodeJapanese.Output);
Console.WriteLine("Ripple: " + ripple.Output);
Console.WriteLine(
    "Work: " + last.TotalAnalyzedCharacters +
    " vs naive " + naiveWork);
return 0;
