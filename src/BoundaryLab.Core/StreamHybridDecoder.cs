namespace BoundaryLab.Core;

// Independent v1 engine: bounded candidate fan-out and linked backpointers.
// Old EvidenceLatticeDecoder remains untouched for controlled A/B comparisons.
internal sealed class StreamHybridDecoder
{
    private sealed record Node(double Score, LatticeEdge? Edge, Node? Previous);
    internal sealed record DecodeResult(
        IReadOnlyList<LatticeEdge> DiagnosticEdges,
        IReadOnlyList<LatticePathSnapshot> Paths,
        string PhoneticPreview,
        int Expanded,
        int MozcProbes,
        IReadOnlyList<int> OrthographicCodeOnsets);

    private const int Beam = 6;
    private const int MaxSpan = 24;
    private const int MaxEdgesAtStart = 12;
    private const int TraceLimit = 80;
    private const double CutCost = 1.15;
    private static readonly HashSet<string> Extensions = new(
        ["js", "jsx", "ts", "tsx", "json", "html", "css", "py",
         "rs", "md", "cs", "cpp", "com", "org", "net", "io", "dev"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> CodeRoots = new(
        ["node", "github", "python", "rust", "react", "test", "index",
         "main", "app", "file", "package", "api", "www"],
        StringComparer.Ordinal);
    // Weak shape evidence, NOT a dictionary lookup or proof of English.
    // Rare endings can expose an otherwise wholly kana-readable word such
    // as "japanese", especially before a Japanese particle ("...seno").
    private static readonly string[] EnglishMorphologyEndings =
        ["ology", "tion", "sion", "ment", "ness", "able", "ible", "ese"];
    private static readonly IReadOnlyDictionary<char, LexiconEntry[]> LexicalIndex =
        Lexicon.Entries.GroupBy(e => e.Raw[0])
            .ToDictionary(g => g.Key, g => g.ToArray());

    private readonly IMozcConversionOracle _mozc;
    // Real bridge requests are synchronous and may timeout after 3s. Keep
    // speculative per-keystroke queries DISABLED unless explicitly opted in.
    private readonly bool _enableMozcProbes;
    private readonly Dictionary<string, MozcProbeResult> _probes =
        new(StringComparer.Ordinal);

    public StreamHybridDecoder(
        IMozcConversionOracle? mozc = null,
        bool enableMozcProbes = false)
    {
        _mozc = mozc ?? new NullMozcConversionOracle();
        _enableMozcProbes = enableMozcProbes;
    }

    public bool MozcAvailable => _mozc.IsAvailable;

    public DecodeResult Decode(string raw)
    {
        if (raw.Length == 0)
            return new([], [], "", 0, 0, []);

        var projection = PhoneticProjector.Project(raw);
        var candidate = new List<LatticeEdge>[raw.Length];
        for (var i = 0; i < raw.Length; i++)
            candidate[i] = [];

        // The same raw coordinates are shared by phonetic, dictionary,
        // unknown-language and punctuation evidence. No early ownership.
        AddLexical(raw, candidate);
        // Structural code spans own the boundary evidence. Generate them
        // BEFORE phonetic runs so the Japanese side can stop precisely at
        // the beginning of a viable identifier such as node.js.
        AddStructuralCodes(raw, candidate);
        AddMorphologicalLatinIslands(raw, candidate);
        AddUnknownLatinIslands(raw, projection, candidate);
        var codeStarts = new HashSet<int>();
        var latinStarts = new HashSet<int>();
        for (var i = 0; i < candidate.Length; i++)
        {
            if (candidate[i].Any(e => e.Kind == LatticeEdgeKind.LatinStructural))
                codeStarts.Add(i);
            // A proposed Latin island is useless unless preceding kana
            // candidates can stop at exactly its starting offset.
            if (candidate[i].Any(e => e.Language == LanguageKind.English &&
                e.Kind is LatticeEdgeKind.EnglishLexical or
                    LatticeEdgeKind.LatinStructural or
                    LatticeEdgeKind.LiteralFallback))
                latinStarts.Add(i);
        }
        AddKanaRuns(raw, candidate, latinStarts);
        AddPunctuation(raw, candidate);
        AddLongVowels(raw, candidate);

        var flattened = new List<LatticeEdge>();
        for (var i = 0; i < raw.Length; i++)
        {
            if (candidate[i].Count == 0)
                candidate[i].Add(Edge(raw, i, i + 1, raw[i].ToString(),
                    LanguageKind.Unknown, LatticeEdgeKind.Unknown,
                    -2.0, "unresolved-literal"));

            // Keep at most one edge per output/meaning at each position,
            // but retain strong orthographic and phonetic alternatives.
            candidate[i] = candidate[i]
                .GroupBy(e => (e.End, e.Output, e.Language))
                .Select(g => g.OrderByDescending(e => e.LocalScore).First())
                .OrderByDescending(e => e.LocalScore)
                .ThenByDescending(e => e.End - e.Start)
                .Take(MaxEdgesAtStart).ToList();
            flattened.AddRange(candidate[i]);
        }

        var dp = new List<Node>[raw.Length + 1];
        for (var i = 0; i < dp.Length; i++)
            dp[i] = [];
        dp[0].Add(new Node(0, null, null));
        var expanded = 0;

        for (var i = 0; i < raw.Length; i++)
        {
            if (dp[i].Count == 0)
                continue;
            var states = dp[i].OrderByDescending(n => n.Score)
                .Take(Beam).ToArray();
            foreach (var state in states)
            foreach (var edge in candidate[i])
            {
                expanded++;
                var score = state.Score + edge.LocalScore;
                if (state.Edge is not null)
                {
                    score -= CutCost;
                    if (state.Edge.Language == edge.Language &&
                        edge.Language != LanguageKind.Unknown)
                        score += 0.35;
                    else if (state.Edge.Language == LanguageKind.English &&
                             edge.Language == LanguageKind.Japanese)
                        score += 0.3;
                }
                var next = dp[edge.End];
                next.Add(new Node(score, edge, state));
                if (next.Count > Beam * 3)
                {
                    var trim = next.OrderByDescending(n => n.Score)
                        .Take(Beam).ToArray();
                    next.Clear();
                    next.AddRange(trim);
                }
            }
        }

        var finalNodes = dp[raw.Length].OrderByDescending(n => n.Score)
            .Take(Beam).ToArray();
        var best = finalNodes.Length > 0 ? finalNodes[0].Score : 0;
        var paths = new List<LatticePathSnapshot>();
        var probes = 0;
        foreach (var node in finalNodes.Take(4))
        {
            var edges = Trace(node);
            // Only the top hypothesis may ask Mozc for one Japanese span.
            // Never spray speculative queries across the entire lattice.
            if (paths.Count == 0 && _enableMozcProbes && _mozc.IsAvailable)
            {
                var index = Array.FindIndex(edges, e =>
                    e.Language == LanguageKind.Japanese &&
                    e.Kind == LatticeEdgeKind.JapanesePhonetic &&
                    e.Raw.Length >= 5);
                if (index >= 0)
                {
                    var target = edges[index];
                    if (!_probes.TryGetValue(target.Raw, out var result))
                    {
                        result = _mozc.Probe(target.Raw);
                        _probes[target.Raw] = result;
                        probes++;
                    }
                    if (result.Success && result.Quality >= 0.85 &&
                        !string.IsNullOrEmpty(result.TopCandidate))
                    {
                        edges[index] = target with
                        {
                            Kind = LatticeEdgeKind.JapaneseMozc,
                            Output = result.TopCandidate,
                            MozcQuality = result.Quality,
                            MozcTopCandidate = result.TopCandidate,
                            Evidence = "stream-selected-mozc-preview"
                        };
                    }
                }
            }
            paths.Add(new LatticePathSnapshot(
                node.Score, node.Score - best,
                string.Join(" | ", edges.Select(e => e.Raw)),
                string.Concat(edges.Select(e => e.Output)), edges));
        }

        // Export only a diagnostic slice. Full candidate graphs used to
        // explode trace JSON size in v0.7; the top paths still store all
        // selected edges, and expanded counts preserve work measurements.
        var diagnostics = flattened.OrderBy(e => e.Start)
            .ThenByDescending(e => e.LocalScore)
            .Take(TraceLimit).ToArray();
        return new(
            diagnostics, paths, projection.Preview, expanded, probes,
            codeStarts.OrderBy(x => x).ToArray());
    }

    private static LatticeEdge[] Trace(Node node)
    {
        var list = new List<LatticeEdge>();
        for (Node? cursor = node; cursor?.Edge is not null; cursor = cursor.Previous)
            list.Add(cursor.Edge);
        list.Reverse();
        return list.ToArray();
    }

    private static void AddLexical(string raw, List<LatticeEdge>[] dst)
    {
        for (var start = 0; start < raw.Length; start++)
        {
            if (!LexicalIndex.TryGetValue(raw[start], out var entries))
                continue;
            foreach (var e in entries)
            {
                if (start + e.Raw.Length > raw.Length ||
                    !raw.AsSpan(start, e.Raw.Length).SequenceEqual(e.Raw))
                    continue;
                var end = start + e.Raw.Length;
                var ja = e.Language == LanguageKind.Japanese;
                var particle = ja && e.Evidence == "japanese-particle";
                // Two-letter function words like "or" are ambiguous
                // inside romaji. A three-letter recognized English word
                // ("the") must not be demoted behind an orphan 't'
                // followed by the Japanese 'he' particle.
                var shortEnglish = !ja && e.Raw.Length <= 2;
                var japaneseScore = particle
                    ? e.Raw.Length * 0.72 + 0.65
                    : e.Raw.Length * 0.85 + 2.45;
                var englishScore = e.Raw.Length * 0.9 + 2.65;
                // A single 'o' in continuous ASCII is far more likely
                // the onset of a Japanese syllable (o-mo-i...) than the
                // complete object particle を. The conventional explicit
                // input for the particle is 'wo', which remains supported.
                // Do not turn a kana-readable word into o|mo|ima|su.
                if (ja && particle && e.Raw == "o" &&
                    end < raw.Length && char.IsAsciiLetter(raw[end]))
                    japaneseScore -= 2.9;
                // Short English inside a continuous kana-readable word
                // is ambiguous; require external corroboration.
                if (shortEnglish &&
                    (end < raw.Length && char.IsAsciiLetter(raw[end]) ||
                     start > 0 && char.IsAsciiLetter(raw[start - 1])))
                    englishScore -= 2.5;
                dst[start].Add(Edge(raw, start, end, e.Converted,
                    e.Language,
                    ja ? LatticeEdgeKind.JapaneseLexical : LatticeEdgeKind.EnglishLexical,
                    ja ? japaneseScore : englishScore, e.Evidence));
            }
        }
    }

    private static void AddKanaRuns(
        string raw, List<LatticeEdge>[] dst, IReadOnlySet<int> latinStarts)
    {
        for (var start = 0; start < raw.Length; start++)
        {
            if (!char.IsAsciiLetter(raw[start])) continue;
            var end = start;
            var text = "";
            var steps = 0;
            var candidates = new List<(int End, string Kana)>();
            while (end < raw.Length && end - start < MaxSpan &&
                   char.IsAsciiLetter(raw[end]) &&
                   RomajiConverter.TryConsume(raw, end, out var used, out var kana) &&
                   end + used <= raw.Length && end + used - start <= MaxSpan)
            {
                text += kana;
                end += used;
                steps++;

                // Unknown Latin can BEGIN with a perfectly readable kana
                // unit. For womotonimeltype..., the Japanese candidate
                // must be allowed to stop BEFORE 'me', otherwise the
                // English path cannot attach even if 'meltype' is present.
                if (end > start && LooksLikeUnknownLatinStart(raw, end))
                    candidates.Add((end, text));
                // Avoid O(n^2) output hypotheses. Keep only a few
                // meaningful possible cuts: full runs, dictionary anchors,
                // and the transition to an unknown Latin island.
                if (end == raw.Length || !char.IsAsciiLetter(raw[end]) ||
                    IsEnglishStart(raw, end) || IsJapaneseStart(raw, end) ||
                    latinStarts.Contains(end))
                    candidates.Add((end, text));
                if (steps >= MaxSpan) break;
            }
            if (end > start && !candidates.Any(e => e.End == end))
                candidates.Add((end, text));

            // The ordinary four candidate cuts can be pruned for speed,
            // but an evidence-backed Latin onset must never disappear.
            // This includes known words, structural code and unknown/
            // morphological islands; none of these forces Latin ownership.
            var chosen = candidates.TakeLast(4).ToList();
            foreach (var anchored in candidates)
            {
                if (latinStarts.Contains(anchored.End) &&
                    !chosen.Any(c => c.End == anchored.End))
                    chosen.Add(anchored);
            }

            foreach (var (stop, kana) in chosen)
            {
                var span = raw[start..stop];
                dst[start].Add(Edge(raw, start, stop, kana,
                    LanguageKind.Japanese, LatticeEdgeKind.JapanesePhonetic,
                    span.Length * 0.77 + 0.8, "stream-kana-run"));
            }
        }
    }

    private static bool LooksLikeUnknownLatinStart(string raw, int at)
    {
        // A readable 2- or 3-character kana unit immediately followed
        // by two consonants that cannot begin a romaji unit is evidence
        // for a *possible* unknown English word, not proof of English.
        if (at >= raw.Length || !char.IsAsciiLetter(raw[at]) ||
            !RomajiConverter.TryConsume(raw, at, out var length, out _) ||
            length < 2 || at + length + 1 >= raw.Length)
            return false;
        var first = at + length;
        if (!char.IsAsciiLetter(raw[first]) ||
            !char.IsAsciiLetter(raw[first + 1]) ||
            RomajiConverter.TryConsume(raw, first, out _, out _))
            return false;
        return !RomajiConverter.TryConsume(raw, first + 1, out _, out _);
    }

    private static bool IsEnglishStart(string raw, int at) =>
        at < raw.Length && LexicalIndex.TryGetValue(raw[at], out var entries) &&
        entries.Any(e => e.Language == LanguageKind.English &&
            e.Raw.Length >= 3 && at + e.Raw.Length <= raw.Length &&
            raw.AsSpan(at, e.Raw.Length).SequenceEqual(e.Raw));

    private static bool IsJapaneseStart(string raw, int at) =>
        at < raw.Length && LexicalIndex.TryGetValue(raw[at], out var entries) &&
        entries.Any(e => e.Language == LanguageKind.Japanese &&
            e.Raw.Length >= 4 && at + e.Raw.Length <= raw.Length &&
            raw.AsSpan(at, e.Raw.Length).SequenceEqual(e.Raw));


    private static void AddMorphologicalLatinIslands(
        string raw, List<LatticeEdge>[] dst)
    {
        // Romaji-readability alone is not enough: "japanese" is readable
        // as ja|pa|ne|se. Try *reversible* English word-shape edges whose
        // endings are relatively uncommon in Japanese keyboard romanization.
        // Bounded lengths and a flat-ish score avoid greedily swallowing
        // a preceding Japanese prefix (oreha|japanese, not orehajapanese).
        for (var start = 0; start < raw.Length; start++)
        {
            if (!char.IsAsciiLetter(raw[start])) continue;
            var maxEnd = Math.Min(raw.Length, start + MaxSpan);
            for (var end = start + 7; end <= maxEnd; end++)
            {
                var span = raw.AsSpan(start, end - start);
                if (!span.ToString().All(char.IsAsciiLetter))
                    break;

                var ending = EnglishMorphologyEndings
                    .FirstOrDefault(suffix =>
                        span.EndsWith(suffix, StringComparison.Ordinal));
                if (ending is null) continue;

                // A complete Latin token must reach punctuation/input end
                // or be followed by a plausible Japanese particle. Never
                // treat arbitrary intermediate morphology as a word cut.
                var particleAfter = IsJapaneseParticle(raw, end);
                if (end < raw.Length && char.IsAsciiLetter(raw[end]) &&
                    !particleAfter)
                    continue;

                var score = 8.4 + Math.Min(end - start - 7, 8) * 0.10 +
                            (particleAfter ? 0.6 : 0.0);
                dst[start].Add(Edge(raw, start, end, raw[start..end],
                    LanguageKind.English, LatticeEdgeKind.LiteralFallback,
                    score, "latin-morphology-" + ending + "-tentative"));
            }
        }
    }

    private static void AddUnknownLatinIslands(
        string raw, PhoneticProjection projection, List<LatticeEdge>[] dst)
    {
        var units = projection.Units;
        // A maximal unknown island may contain short, readable islands
        // between pending Latin consonants (english -> en / gl / i / sh).
        // Include up to 2 chars of readable prefix; exclude preceding
        // well-formed kana words.
        for (var i = 0; i < units.Count; i++)
        {
            if (units[i].Kind != PhoneticUnitKind.Pending) continue;
            var left = i;
            var lookback = 0;
            while (left > 0 && units[left - 1].Kind == PhoneticUnitKind.Kana &&
                   lookback + units[left - 1].Raw.Length <= 2)
            {
                left--;
                lookback += units[left].Raw.Length;
            }
            var end = units[i].End;
            var unknown = 0;
            var gap = 0;
            for (var j = i; j < units.Count; j++)
            {
                var u = units[j];
                if (u.Start - units[left].Start >= MaxSpan ||
                    u.Kind == PhoneticUnitKind.Symbol) break;
                if (u.Kind is PhoneticUnitKind.Pending or PhoneticUnitKind.Ambiguous)
                {
                    unknown += u.Raw.Length;
                    gap = 0;
                    end = u.End;
                }
                else if (u.Kind == PhoneticUnitKind.Kana)
                {
                    if (gap + u.Raw.Length >= 2) break;
                    gap += u.Raw.Length;
                }
            }
            var start = units[left].Start;
            if (end - start < 2 || !raw[start..end].All(char.IsAsciiLetter))
                continue;
            // A recognized embedded English word takes priority over a
            // generic island that would swallow its left neighbor.
            var protectedWord = false;
            for (var k = start + 1; k < end; k++)
                if (IsEnglishStart(raw, k))
                {
                    protectedWord = true;
                    break;
                }
            var score = (end - start) * 0.90 + 1.5 +
                        Math.Min(unknown, 6) * 0.48 -
                        (protectedWord ? 3.5 : 0);
            dst[start].Add(Edge(raw, start, end, raw[start..end],
                LanguageKind.English, LatticeEdgeKind.LiteralFallback,
                score, "unknown-latin-island-review-both-sides"));

            // A pending consonant island is not necessarily the WHOLE
            // English word. 'meltype' tokenizes phonetically as
            // me + [lty unresolved] + pe, so stopping at 'melty'
            // incorrectly converts the final 'pe' to ぺ. Offer a
            // one-syllable right extension if the syllable is NOT itself
            // a Japanese particle and a word boundary follows it.
            var nextUnit = units.FirstOrDefault(u => u.Start == end);
            if (unknown >= 2 && nextUnit is not null &&
                nextUnit.Kind == PhoneticUnitKind.Kana &&
                nextUnit.Raw.Length is >= 2 and <= 3 &&
                !IsJapaneseParticle(raw, nextUnit.Start))
            {
                var stop = nextUnit.End;
                var terminal = stop == raw.Length ||
                    !char.IsAsciiLetter(raw[stop]) ||
                    IsJapaneseParticle(raw, stop);
                if (terminal && stop - start <= MaxSpan)
                {
                    var suffixBonus = stop < raw.Length &&
                        IsJapaneseParticle(raw, stop) ? 1.6 : 0.7;
                    dst[start].Add(Edge(raw, start, stop, raw[start..stop],
                        LanguageKind.English, LatticeEdgeKind.LiteralFallback,
                        score + nextUnit.Raw.Length * 0.9 + suffixBonus,
                        "unknown-latin-island-complete-terminal-kana"));
                    if (start < units[i].Start)
                    {
                        var suffixStart = units[i].Start;
                        dst[suffixStart].Add(Edge(
                            raw, suffixStart, stop, raw[suffixStart..stop],
                            LanguageKind.English, LatticeEdgeKind.LiteralFallback,
                            (stop - suffixStart) * 0.9 + 1.0 +
                            Math.Min(unknown, 6) * 0.48 + suffixBonus,
                            "unknown-latin-island-complete-without-prefix"));
                    }
                }
            }
            // Also expose the suffix-only alternative, so unrecognized
            // material never has to steal preceding Japanese syllables.
            if (start < units[i].Start)
            {
                var s = units[i].Start;
                dst[s].Add(Edge(raw, s, end, raw[s..end],
                    LanguageKind.English, LatticeEdgeKind.LiteralFallback,
                    (end - s) * 0.90 + 1.0 +
                    Math.Min(unknown, 6) * 0.48,
                    "unknown-latin-island-no-prefix"));
            }
        }
    }

    private static bool IsJapaneseParticle(string raw, int position) =>
        position >= 0 && position < raw.Length &&
        Lexicon.ExactAt(raw, position).Any(e =>
            e.Language == LanguageKind.Japanese &&
            e.Evidence == "japanese-particle" &&
            e.Raw.Length >= 2);

    private static void AddPunctuation(string raw, List<LatticeEdge>[] dst)
    {
        for (var i = 0; i < raw.Length; i++)
        {
            if (!InputSyntax.IsSymbol(raw[i]) && raw[i] != ' ') continue;
            var hard = InputSyntax.IsHardBoundary(raw[i]);
            dst[i].Add(Edge(raw, i, i + 1, raw[i].ToString(),
                LanguageKind.Unknown,
                hard ? LatticeEdgeKind.HardBoundary :
                InputSyntax.IsBindingSymbol(raw[i])
                    ? LatticeEdgeKind.BindingSymbol : LatticeEdgeKind.NeutralSymbol,
                hard ? 0.35 : -0.05,
                hard ? "explicit-hard-delimiter" : "punctuation-ambiguous"));
        }
    }

    private static void AddStructuralCodes(string raw, List<LatticeEdge>[] dst)
    {
        for (var dot = 1; dot + 2 < raw.Length; dot++)
        {
            if (raw[dot] != '.') continue;
            var suffixStart = dot + 1;
            foreach (var ext in Extensions)
            {
                if (suffixStart + ext.Length > raw.Length ||
                    !raw.AsSpan(suffixStart, ext.Length).SequenceEqual(ext))
                    continue;
                // Candidate's suffix must finish at a plausible boundary.
                var end = suffixStart + ext.Length;
                if (end < raw.Length && raw[end] == '.') continue;
                for (var start = Math.Max(0, dot - 14); start < dot; start++)
                {
                    var stem = raw[start..dot];
                    if (!stem.All(char.IsAsciiLetterOrDigit)) continue;
                    // Do not greedily bind arbitrary preceding Japanese.
                    if (!CodeRoots.Contains(stem) && stem.Length > 8) continue;
                    if (!CodeRoots.Contains(stem) &&
                        RomajiConverter.TryConvert(stem, out _)) continue;
                    var score = (end - start) * 1.05 + 3.0 +
                                (CodeRoots.Contains(stem) ? 2.5 : 0);
                    dst[start].Add(Edge(raw, start, end, raw[start..end],
                        LanguageKind.English, LatticeEdgeKind.LatinStructural,
                        score, "verified-code-extension-not-period-default"));
                }
            }
        }
    }

    private static void AddLongVowels(string raw, List<LatticeEdge>[] dst)
    {
        for (var mark = 2; mark < raw.Length - 2; mark++)
        {
            if (raw[mark] != '-') continue;
            // Bounded phonetic alternatives: the symbol might also be a
            // literal identifier separator. Keep both paths.
            for (var start = Math.Max(0, mark - 5); start < mark - 1; start++)
            {
                var left = raw[start..mark];
                if (!RomajiConverter.TryConvert(left, out var kanaLeft))
                    continue;
                for (var end = mark + 3; end <= Math.Min(raw.Length, mark + 8); end++)
                {
                    var right = raw[(mark + 1)..end];
                    if (!RomajiConverter.TryConvert(right, out var kanaRight))
                        continue;
                    var length = end - start;
                    dst[start].Add(Edge(raw, start, end,
                        kanaLeft + "ー" + kanaRight,
                        LanguageKind.Japanese, LatticeEdgeKind.JapanesePhonetic,
                        length * 0.82 + 1.6, "hyphen-kana-hypothesis"));
                }
            }
        }
    }

    private static LatticeEdge Edge(
        string raw, int start, int end, string output,
        LanguageKind language, LatticeEdgeKind kind, double score,
        string reason) =>
        new(start, end, raw[start..end], output, language, kind,
            score, -8, -8, reason);
}
