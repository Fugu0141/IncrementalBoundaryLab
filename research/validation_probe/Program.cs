using System.Diagnostics;
using System.Text.Json;
using BoundaryLab.Core;

// Diagnostic-only probe. Gold offsets are accumulated from component lengths.
record Part(string Raw, string Output, bool Latin = false);
record ProbeCase(string Name, Part[] Parts, string[]? OtherAcceptable = null);

static class Program
{
    static void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("BOUNDARYLAB_STREAM_MOZC", null);
        var cases = new[]
        {
            new ProbeCase("customer", [new("customer", "customer", true)]),
            new ProbeCase("monitor", [new("monitor", "monitor", true)]),
            new ProbeCase("strategy", [new("strategy", "strategy", true)]),
            new ProbeCase("japanese", [new("japanese", "japanese", true)]),
            new ProbeCase("tokyo", [new("tokyo", "tokyo", true)], ["ときょ"]),
            new ProbeCase("github", [new("github", "github", true)]),
            new ProbeCase("meltype", [new("meltype", "meltype", true)]),
            new ProbeCase("ime", [new("ime", "ime", true)], ["いめ"]),
            new ProbeCase("japaneseno", [new("japanese", "japanese", true), new("no", "の")]),
            new ProbeCase("orehajapaneseno", [new("oreha", "おれは"), new("japanese", "japanese", true), new("no", "の")]),
            new ProbeCase("meltypega", [new("meltype", "meltype", true), new("ga", "が")]),
            new ProbeCase("toomoimasu", [new("toomoimasu", "とおもいます")]),
            new ProbeCase("githubdeissue", [new("github", "github", true), new("de", "で"), new("issue", "issue", true)]),
            new ProbeCase("networkmiru", [new("network", "network", true), new("miru", "みる")]),
            new ProbeCase("myunknownsite.comniarimasu", [new("myunknownsite.com", "myunknownsite.com", true), new("niarimasu", "にあります")]),
            new ProbeCase("CUSTOMER", [new("CUSTOMER", "CUSTOMER", true)]),
            new ProbeCase("IME", [new("IME", "IME", true)]),
        };
        var rows = new List<object>();
        foreach (var c in cases)
        {
            var raw = string.Concat(c.Parts.Select(p => p.Raw));
            var expected = string.Concat(c.Parts.Select(p => p.Output));
            var result = new StreamHybridSession().Update(raw);
            var frame = result.Frames[^1];
            var offsets = new List<int>();
            var latin = new List<object>();
            var cursor = 0;
            foreach (var part in c.Parts)
            {
                var start = cursor;
                cursor += part.Raw.Length;
                if (cursor < raw.Length) offsets.Add(cursor);
                if (part.Latin)
                {
                    var found = frame.CandidateEdges.Where(e =>
                        e.Start == start && e.End == cursor &&
                        e.Language == LanguageKind.English &&
                        e.Output == part.Raw.ToLowerInvariant()).ToArray();
                    latin.Add(new { start, end = cursor, raw = part.Raw,
                        diagnosticEdge = found.Select(e => new { kind = e.Kind.ToString(), e.Evidence, e.LocalScore }).ToArray(),
                        inTop4Path = frame.TopPaths.Any(path => path.Edges.Any(e =>
                            e.Start == start && e.End == cursor && e.Language == LanguageKind.English)) });
                }
            }
            rows.Add(new { name = c.Name, raw, expected, otherAcceptable = c.OtherAcceptable,
                actual = result.Output, exact = result.Output == expected,
                acceptable = result.Output == expected || (c.OtherAcceptable?.Contains(result.Output) ?? false),
                goldBoundaries = offsets, selectedBoundaries = result.ActiveBestSegments.Select(e => e.End).Where(x => x < raw.Length).ToArray(),
                selected = result.ActiveBestSegments.Select(e => new { e.Start, e.End, e.Raw, e.Output,
                    kind = e.Kind.ToString(), language = e.Language.ToString() }).ToArray(),
                latin, candidateEdgeCount = frame.CandidateEdges.Count,
                topPaths = frame.TopPaths.Select(p => new { p.Score, p.Output, p.Segmentation }).ToArray(),
                committed = result.CommittedRawLength, expanded = frame.TotalExpandedEdges });
        }

        var edits = new StreamHybridSession();
        var editResults = new[] { "japan", "japanese", "japaneseno", "japanesen", "japaneseno", "japaneseno," }
            .Select(s => { var x = edits.Update(s); return new { input = s, x.Output, x.CommittedRawLength,
                rebuilt = x.Frames[^1].Rebuilt }; }).ToArray();
        var prefixes = new Dictionary<string, object>();
        foreach (var word in new[] { "japaneseno", "customer", "ime" })
        {
            var session = new StreamHybridSession();
            var steps = new List<object>();
            for (var i = 1; i <= word.Length; i++)
            {
                var x = session.Update(word[..i]);
                steps.Add(new { prefix = word[..i], x.Output, x.CommittedRawLength });
            }
            prefixes[word] = steps;
        }

        _ = new StreamHybridSession().Update("aaaaaaaa");
        var scaling = new List<object>();
        foreach (var n in new[] { 40, 80, 160 })
        {
            var times = new List<double>();
            var allocs = new List<long>();
            long expanded = 0;
            for (var rep = 0; rep < 3; rep++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                var x = new StreamHybridSession().Update(new string('a', n));
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds);
                allocs.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                expanded = x.Frames[^1].TotalExpandedEdges;
            }
            scaling.Add(new { length = n, ms = times.Order().ToArray(), allocatedBytes = allocs.Order().ToArray(), expanded });
        }
        var json = JsonSerializer.Serialize(new { branch = "experiment/stream-hybrid-v1.3-latin-morphology",
            algorithm = StreamHybridSession.AlgorithmVersion, cases = rows, editResults, prefixes, scaling },
            new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 0) File.WriteAllText(args[0], json + Environment.NewLine);
        else Console.WriteLine(json);
    }
}
