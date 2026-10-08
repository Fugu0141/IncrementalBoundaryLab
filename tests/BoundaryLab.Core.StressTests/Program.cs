using BoundaryLab.Core;

const int FuzzCases = 15000;
const int MaxRandomLength = 48;

var failures = new List<string>();
var executed = 0;
var totalInputCharacters = 0L;

void Fail(string message)
{
    if (failures.Count < 50)
        failures.Add(message);
}

void ValidateStructure(string input, PhoneticFirstAnalysisResult result, string label)
{
    executed++;
    totalInputCharacters += input.Length;

    var normalized = InputSyntax.Normalize(input);
    if (result.Input != normalized)
        Fail($"{label}: normalized input mismatch");

    var segments = result.CommittedSegments
        .Concat(result.ActiveSegments)
        .OrderBy(s => s.Start)
        .ToArray();

    var raw = string.Concat(segments.Select(s => s.Raw));
    if (raw != normalized)
        Fail($"{label}: raw reconstruction mismatch: {raw} != {normalized}");

    var expectedStart = 0;
    foreach (var segment in segments)
    {
        if (segment.Start != expectedStart)
            Fail($"{label}: gap/overlap before {segment.Raw} at {segment.Start}, expected {expectedStart}");

        if (segment.End < segment.Start ||
            segment.End > normalized.Length)
            Fail($"{label}: invalid range {segment.Start}..{segment.End}");

        if (segment.End <= normalized.Length &&
            normalized[segment.Start..segment.End] != segment.Raw)
            Fail($"{label}: raw slice mismatch for {segment.Raw}");

        expectedStart = segment.End;
    }

    if (expectedStart != normalized.Length)
        Fail($"{label}: segments stop at {expectedStart}/{normalized.Length}");

    var output = string.Concat(segments.Select(s => s.Output));
    if (output != result.Output)
        Fail($"{label}: output reconstruction mismatch");

    if (result.CommittedRawLength < 0 ||
        result.CommittedRawLength > normalized.Length)
        Fail($"{label}: invalid committed length");

    if (result.HardCommittedRawLength > result.CommittedRawLength)
        Fail($"{label}: hard committed length exceeds committed length");

    if (result.CommittedSegments.Any(s =>
        s.FreezeState == FreezeState.Active || !s.Confirmed))
        Fail($"{label}: committed segment is not frozen/confirmed");

    if (result.ActiveSegments.Any(s =>
        s.FreezeState != FreezeState.Active || s.Confirmed))
        Fail($"{label}: active segment has frozen state");

    if (result.Frames.Any(f =>
        f.FreezeTransitions.Any(t =>
            t.From == FreezeState.HardFrozen &&
            t.To == FreezeState.Active)))
        Fail($"{label}: HardFrozen segment thawed");

    if (result.Frames.Count != normalized.Length)
        Fail($"{label}: expected {normalized.Length} frames, got {result.Frames.Count}");
}

PhoneticFirstAnalysisResult Analyze(string input)
{
    var session = new PhoneticFirstSession();
    return session.Update(input);
}

var regressions = new Dictionary<string, string>
{
    ["kyouhacommitsimasita"] = "今日はcommitしました",
    ["node.js"] = "node.js",
    ["node.jswotukau"] = "node.jsを使う",
    ["mawasu"] = "まわす",
    ["foo_bar"] = "foo_bar",
    ["c#"] = "c#",
    ["v2.0"] = "v2.0",
    ["github.com"] = "github.com",
    ["kyouzyuunitaiousiteoitayo"] = "今日じゅうにたいおうしてをいたよ"
};

foreach (var pair in regressions)
{
    var result = Analyze(pair.Key);
    ValidateStructure(pair.Key, result, "regression:" + pair.Key);
    if (result.Output != pair.Value)
        Fail($"regression:{pair.Key}: {result.Output} != {pair.Value}");
}

var japanese = new (string Raw, string Output)[]
{
    ("kyou", "今日"),
    ("asita", "明日"),
    ("nihongo", "日本語"),
    ("watasi", "私"),
    ("seido", "精度"),
    ("sugoku", "すごく"),
    ("kannzi", "感じ"),
    ("natta", "なった"),
    ("nado", "など"),
    ("suru", "する")
};

var english = new[]
{
    "commit", "issue", "github", "linux", "kernel",
    "network", "server", "cache", "debug", "method"
};

var suffixes = new (string Raw, string Output)[]
{
    ("ha", "は"),
    ("de", "で"),
    ("ni", "に"),
    ("wo", "を"),
    ("suru", "する")
};

foreach (var ja in japanese)
foreach (var en in english)
foreach (var suffix in suffixes)
{
    var input = ja.Raw + en + suffix.Raw;
    var expected = ja.Output + en + suffix.Output;
    var result = Analyze(input);
    ValidateStructure(input, result, "mixed:" + input);

    if (result.Output != expected)
        Fail($"mixed:{input}: {result.Output} != {expected}");
}

var leftParts = new[]
{
    "node", "foo", "bar", "alpha", "beta", "pkg",
    "lib", "api", "github", "sample", "v2", "net8"
};
var rightParts = new[]
{
    "js", "core", "test", "dev", "com", "io", "0", "1", "api"
};
var binders = new[] { '.', '_', '-', '@', '/', ':' };

foreach (var left in leftParts)
foreach (var right in rightParts)
foreach (var binder in binders)
{
    var input = left + binder + right;
    var result = Analyze(input);
    ValidateStructure(input, result, "binding:" + input);

    if (result.Output != input)
        Fail($"binding:{input}: {result.Output} != {input}");

    if (!result.Frames.Any(f =>
        f.SymbolEvidence.Any(e => e.Kind == "LatinBindingToken")))
        Fail($"binding:{input}: missing LatinBindingToken evidence");
}

var postfixTokens = new[] { "c#", "c++", "f#", "x#", "lib+", "pkg++" };
foreach (var input in postfixTokens)
{
    var result = Analyze(input);
    ValidateStructure(input, result, "postfix:" + input);
    if (result.Output != input)
        Fail($"postfix:{input}: {result.Output} != {input}");
}

var lowConfidence = Analyze("harucommit");
ValidateStructure("harucommit", lowConfidence, "low-confidence");
if (!lowConfidence.Frames.Any(f =>
    f.RippleEvents.Any(e => e.SourceKind == "LowConfidenceSegment")))
{
    Fail("low-confidence: no LowConfidenceSegment ripple event was recorded");
}

var deterministicSamples = new List<string>();
var rng = new XorShift32(0xC0FFEE42u);
const string alphabet =
    "abcdefghijklmnopqrstuvwxyz0123456789._-+#@/:,;!?()[]{}\"' ";

for (var i = 0; i < FuzzCases; i++)
{
    var length = 1 + (int)(rng.Next() % MaxRandomLength);
    var chars = new char[length];

    for (var j = 0; j < chars.Length; j++)
        chars[j] = alphabet[(int)(rng.Next() % (uint)alphabet.Length)];

    var input = new string(chars);
    var result = Analyze(input);
    ValidateStructure(input, result, $"fuzz:{i}");

    if (i < 500)
        deterministicSamples.Add(input);
}

foreach (var input in deterministicSamples)
{
    var a = Analyze(input);
    var b = Analyze(input);

    var aSignature = Signature(a);
    var bSignature = Signature(b);
    if (aSignature != bSignature)
        Fail($"determinism:{input}: signatures differ");
}

const string perfText =
    "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo";
var perfSession = new PhoneticFirstSession();
for (var i = 1; i <= perfText.Length; i++)
    perfSession.Update(perfText[..i]);

var perf = perfSession.Update(perfText);
var totalWork = perf.Frames[^1].TotalAnalyzedCharacters;
var naiveWork =
    (long)perfText.Length *
    (perfText.Length + 1) / 2;

if (totalWork >= naiveWork * 0.80)
    Fail($"performance: {totalWork} is not at least 20% below naive {naiveWork}");

Console.WriteLine($"Stress cases executed: {executed:N0}");
Console.WriteLine($"Input characters covered: {totalInputCharacters:N0}");
Console.WriteLine($"Determinism rechecks: {deterministicSamples.Count:N0}");
Console.WriteLine($"Performance: {totalWork} analyzed chars vs naive {naiveWork}");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAILED: {failures.Count} (showing up to 50)");
    foreach (var failure in failures)
        Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("All large-scale RCR stress/property tests passed.");
return 0;

static string Signature(PhoneticFirstAnalysisResult result) =>
    result.Output + "|" +
    result.CommittedRawLength + "|" +
    string.Join(";", result.CommittedSegments
        .Concat(result.ActiveSegments)
        .OrderBy(s => s.Start)
        .Select(s =>
            $"{s.Start}-{s.End}:{s.Raw}>{s.Output}:{s.Language}:{s.FreezeState}:{s.Confidence:F6}"));

sealed class XorShift32
{
    private uint _state;

    public XorShift32(uint seed)
    {
        _state = seed == 0 ? 0x9E3779B9u : seed;
    }

    public uint Next()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }
}
