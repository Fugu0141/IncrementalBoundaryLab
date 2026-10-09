namespace BoundaryLab.Core;

// "Confirmed" means supported as a PHONETIC/STRUCTURAL observation,
// never that the IME has committed Japanese-vs-English ownership.
public enum PhoneticEvidenceStatus
{
    Confirmed,
    Tentative
}

public sealed record PhoneticGroupEvidence(
    int Start,
    int End,
    string Raw,
    string Preview,
    string Kind,
    PhoneticEvidenceStatus Status,
    string Reason);

public sealed record PhoneticBoundaryEvidence(
    int Position,
    PhoneticEvidenceStatus Status,
    string Reason);

public sealed record PhoneticReviewWindow(
    int Start,
    int End,
    string Reason);

public sealed record PhoneticStructureAnalysis(
    IReadOnlyList<PhoneticGroupEvidence> Groups,
    IReadOnlyList<PhoneticBoundaryEvidence> Boundaries,
    IReadOnlyList<PhoneticReviewWindow> ReviewWindows);

// Four classifications: confirmed/tentative GROUP, confirmed/tentative CUT.
// Each classification is evidence, not a compulsory segmentation or commit.
// Existing phonetic projection and lattice remain free to choose other paths.
public static class PhoneticStructureAnalyzer
{
    public const int ReviewRadius = 6;

    public static PhoneticStructureAnalysis Analyze(string raw)
    {
        if (raw.Length == 0)
            return new PhoneticStructureAnalysis([], [], []);

        // Both stages must use exactly the same normalized source offsets.
        raw = InputSyntax.Normalize(raw);
        var projection = PhoneticProjector.Project(raw);
        var units = projection.Units;
        var boundaries = new Dictionary<int, PhoneticBoundaryEvidence>();
        var risks = new List<(int Start, int End, string Reason)>();

        void AddBoundary(int position, PhoneticEvidenceStatus status, string reason)
        {
            if (position <= 0 || position >= raw.Length)
                return;
            if (boundaries.TryGetValue(position, out var previous))
            {
                // Hard separators are the only indisputable cuts. All other
                // evidence stays tentative, including a period with letters
                // on both sides. A tentative competing hypothesis takes
                // priority unless there is a literal hard separator.
                if (previous.Status == PhoneticEvidenceStatus.Confirmed ||
                    status == PhoneticEvidenceStatus.Tentative)
                    return;
            }
            boundaries[position] =
                new PhoneticBoundaryEvidence(position, status, reason);
        }

        foreach (var unit in units)
        {
            if (unit.Kind is PhoneticUnitKind.Pending or PhoneticUnitKind.Ambiguous)
                risks.Add((unit.Start, unit.End, "unresolved-romaji"));

            if (unit.Kind == PhoneticUnitKind.Symbol &&
                InputSyntax.IsBindingSymbol(raw[unit.Start]))
            {
                // '.' is not an automatic hard cut or automatic proof of an
                // English identifier: node.js, nihongo., and a.b differ.
                var reason = raw[unit.Start] == '.'
                    ? "period-ownership-ambiguous"
                    : "binding-symbol-ownership-ambiguous";
                risks.Add((unit.Start, unit.End, reason));
                AddBoundary(unit.Start, PhoneticEvidenceStatus.Tentative, reason);
                AddBoundary(unit.End, PhoneticEvidenceStatus.Tentative, reason);
            }
            if (unit.Kind == PhoneticUnitKind.Symbol &&
                InputSyntax.IsHardBoundary(raw[unit.Start]))
            {
                AddBoundary(unit.Start, PhoneticEvidenceStatus.Confirmed,
                    "explicit-hard-delimiter");
                AddBoundary(unit.End, PhoneticEvidenceStatus.Confirmed,
                    "explicit-hard-delimiter");
            }
        }

        // Lexical alternatives can cut THROUGH kana tokens: "or" ends
        // inside the romaji syllable "re" of oreha, not at a kana boundary.
        // Make competing cuts visible instead of prematurely discarding them.
        var lexical = new List<(int Start, int End, LanguageKind Language)>();
        for (var i = 0; i < raw.Length; i++)
        {
            foreach (var entry in Lexicon.ExactAt(raw, i))
            {
                if (entry.Raw.Length < (entry.Language == LanguageKind.English ? 2 : 3))
                    continue;
                var end = i + entry.Raw.Length;
                lexical.Add((i, end, entry.Language));
                AddBoundary(i, PhoneticEvidenceStatus.Tentative,
                    "lexicon-candidate-start");
                AddBoundary(end, PhoneticEvidenceStatus.Tentative,
                    "lexicon-candidate-end");
            }
        }

        // A lexical English interval overlapping kana tokens is itself a
        // reason to defer a language commitment, even with full kana coverage.
        foreach (var anchor in lexical.Where(x => x.Language == LanguageKind.English))
        {
            if (projection.Units.Any(u =>
                    u.Kind == PhoneticUnitKind.Kana &&
                    u.Start < anchor.End && anchor.Start < u.End))
                risks.Add((anchor.Start, anchor.End,
                    "kana-readable-english-collision"));
        }

        var provisional = new List<(int Start, int End, string Preview,
            string Kind, string Reason)>();
        for (var i = 0; i < units.Count;)
        {
            var first = units[i];
            if (first.Kind == PhoneticUnitKind.Symbol)
            {
                var kind = InputSyntax.IsHardBoundary(raw[first.Start])
                    ? "HardDelimiter"
                    : InputSyntax.IsBindingSymbol(raw[first.Start])
                        ? "BindingSymbol" : "OtherSymbol";
                provisional.Add((first.Start, first.End, first.Preview,
                    kind, "symbol-unit"));
                i++;
                continue;
            }

            var readable = first.Kind == PhoneticUnitKind.Kana;
            var start = first.Start;
            var end = first.End;
            var preview = first.Preview;
            i++;
            while (i < units.Count && units[i].Kind != PhoneticUnitKind.Symbol &&
                (units[i].Kind == PhoneticUnitKind.Kana) == readable)
            {
                end = units[i].End;
                preview += units[i].Preview;
                i++;
            }
            provisional.Add((start, end, preview,
                readable ? "KanaReadable" : "Unresolved",
                readable ? "left-to-right-kana-run" : "pending-or-ambiguous-romaji"));
        }

        // Review windows deliberately spill to both sides of unreadable
        // islands and ambiguous punctuation. This is the old "context
        // ripple" principle, stored here as auditable data rather than
        // immediately rewriting a committed token.
        var windows = new List<PhoneticReviewWindow>();
        foreach (var r in risks.OrderBy(r => r.Start).ThenBy(r => r.End))
        {
            var start = Math.Max(0, r.Start - ReviewRadius);
            var end = Math.Min(raw.Length, r.End + ReviewRadius);
            if (windows.Count > 0 && start <= windows[^1].End)
            {
                var last = windows[^1];
                windows[^1] = last with
                {
                    End = Math.Max(last.End, end),
                    Reason = last.Reason.Contains(r.Reason, StringComparison.Ordinal)
                        ? last.Reason : last.Reason + "|" + r.Reason
                };
            }
            else
            {
                windows.Add(new PhoneticReviewWindow(start, end, r.Reason));
            }
        }

        var groups = new List<PhoneticGroupEvidence>();
        foreach (var g in provisional)
        {
            var inReview = windows.Any(w => g.Start < w.End && w.Start < g.End);
            var confirmed = g.Kind == "HardDelimiter" ||
                (g.Kind == "KanaReadable" && !inReview &&
                 !lexical.Any(a =>
                     a.Language == LanguageKind.English &&
                     a.Start < g.End && g.Start < a.End));

            groups.Add(new PhoneticGroupEvidence(
                g.Start, g.End, raw[g.Start..g.End], g.Preview,
                g.Kind,
                confirmed ? PhoneticEvidenceStatus.Confirmed
                          : PhoneticEvidenceStatus.Tentative,
                confirmed ? "phonetic-structure-only-not-language"
                          : inReview ? "reconsider-both-sides-of-anomaly"
                          : "needs-context-or-literal-comparison"));
        }

        // Between adjacent groups, offer a *possible* cut. Never mark a
        // normal kana syllable boundary as mandatory.
        for (var i = 1; i < groups.Count; i++)
        {
            var position = groups[i].Start;
            if (position > 0 && position < raw.Length &&
                (InputSyntax.IsHardBoundary(raw[position]) ||
                 InputSyntax.IsHardBoundary(raw[position - 1])))
                AddBoundary(position, PhoneticEvidenceStatus.Confirmed,
                    "explicit-hard-delimiter");
            else
                AddBoundary(position, PhoneticEvidenceStatus.Tentative,
                    "phonetic-group-transition");
        }

        return new PhoneticStructureAnalysis(
            groups,
            boundaries.Values.OrderBy(b => b.Position).ToArray(),
            windows);
    }
}
