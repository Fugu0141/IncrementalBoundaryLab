namespace BoundaryLab.Core;

internal sealed record LexiconEntry(
    string Raw,
    string Converted,
    LanguageKind Language,
    double Weight,
    string Evidence);

internal static class Lexicon
{
    public static readonly IReadOnlyList<LexiconEntry> Entries = Build();

    public static IEnumerable<LexiconEntry> ExactAt(string input, int start)
    {
        foreach (var entry in Entries)
        {
            if (start + entry.Raw.Length <= input.Length &&
                input.AsSpan(start, entry.Raw.Length).SequenceEqual(entry.Raw))
            {
                yield return entry;
            }
        }
    }

    public static IReadOnlyList<LexiconEntry> PrefixMatches(string tail) =>
        Entries.Where(e => e.Raw.Length > tail.Length &&
                           e.Raw.StartsWith(tail, StringComparison.Ordinal))
               .ToArray();

    private static IReadOnlyList<LexiconEntry> Build()
    {
        var j = new (string raw, string converted, double weight, string kind)[]
        {
            ("kyou", "今日", 6.2, "japanese-lexeme"),
            ("kore", "これ", 5.8, "japanese-lexeme"),
            ("nihongo", "日本語", 6.2, "japanese-lexeme"),
            ("watashi", "私", 6.0, "japanese-lexeme"),
            ("watasi", "私", 5.9, "japanese-romaji-alias"),
            ("gakusei", "学生", 6.0, "japanese-lexeme"),
            ("ashita", "明日", 6.0, "japanese-lexeme"),
            ("asita", "明日", 5.9, "japanese-romaji-alias"),
            ("ame", "雨", 5.4, "japanese-lexeme"),
            ("samui", "寒い", 5.6, "japanese-lexeme"),
            ("benkyou", "勉強", 6.0, "japanese-lexeme"),
            ("tomodachi", "友達", 6.0, "japanese-lexeme"),
            ("tomodati", "友達", 5.9, "japanese-romaji-alias"),
            ("kaisha", "会社", 5.9, "japanese-lexeme"),
            ("kaisya", "会社", 5.8, "japanese-romaji-alias"),
            ("anime", "アニメ", 5.4, "japanese-lexeme"),
            ("sushi", "すし", 5.4, "japanese-lexeme"),
            ("susi", "すし", 5.3, "japanese-romaji-alias"),
            ("tomato", "トマト", 5.2, "japanese-lexeme"),
            ("miru", "みる", 5.3, "japanese-lexeme"),
            ("tsukau", "使う", 5.5, "japanese-lexeme"),
            ("tukau", "使う", 5.4, "japanese-romaji-alias"),
            ("okuru", "送る", 5.5, "japanese-lexeme"),
            ("tateru", "たてる", 5.4, "japanese-lexeme"),
            ("tateta", "たてた", 5.4, "japanese-lexeme"),
            ("kakunin", "確認", 5.8, "japanese-lexeme"),
            ("seido", "精度", 6.0, "japanese-lexeme"),
            ("sugoku", "すごく", 5.8, "japanese-lexeme"),
            ("ii", "いい", 5.3, "japanese-lexeme"),
            ("kanji", "感じ", 5.9, "japanese-lexeme"),
            ("kannzi", "感じ", 5.8, "japanese-romaji-alias"),
            ("natta", "なった", 5.8, "japanese-verb"),
            ("konna", "こんな", 5.7, "japanese-lexeme"),
            ("konnna", "こんな", 5.6, "japanese-romaji-alias"),
            ("kamo", "かも", 5.4, "japanese-auxiliary"),
            ("gamenn", "画面", 5.9, "japanese-romaji-alias"),
            ("gamen", "画面", 5.8, "japanese-lexeme"),
            ("menndou", "面倒", 5.7, "japanese-romaji-alias"),
            ("mendou", "面倒", 5.8, "japanese-lexeme"),
            ("datta", "だった", 5.6, "japanese-auxiliary"),
            ("kou", "こう", 5.3, "japanese-lexeme"),
            ("motikosu", "持ち越す", 5.6, "japanese-romaji-alias"),
            ("noyatsu", "のやつ", 5.8, "japanese-phrase"),
            ("noyatu", "のやつ", 5.7, "japanese-romaji-alias"),
            ("noyatsudayo", "のやつだよ", 6.3, "japanese-phrase"),
            ("noyatudayo", "のやつだよ", 6.2, "japanese-romaji-alias"),
            ("nado", "など", 5.3, "japanese-lexeme"),
            ("desu", "です", 5.5, "japanese-auxiliary"),
            ("dayo", "だよ", 5.3, "japanese-auxiliary"),
            ("ima", "今", 5.6, "japanese-lexeme"),
            ("ikimasu", "行きます", 5.7, "japanese-verb"),
            ("taberu", "食べる", 5.5, "japanese-verb"),
            ("asobu", "遊ぶ", 5.5, "japanese-verb"),
            ("simasita", "しました", 6.5, "japanese-verb-suffix"),
            ("simashita", "しました", 6.5, "japanese-verb-suffix"),
            ("shimasita", "しました", 6.5, "japanese-verb-suffix"),
            ("shimashita", "しました", 6.5, "japanese-verb-suffix"),
            ("simasu", "します", 6.1, "japanese-verb-suffix"),
            ("shimasu", "します", 6.1, "japanese-verb-suffix"),
            ("suru", "する", 6.1, "japanese-verb-suffix"),
            ("sita", "した", 5.9, "japanese-verb-suffix"),
            ("shita", "した", 5.9, "japanese-verb-suffix"),
            ("site", "して", 5.8, "japanese-verb-suffix"),
            ("shite", "して", 5.8, "japanese-verb-suffix"),
            ("shitai", "したい", 5.9, "japanese-verb-suffix"),
            ("sitai", "したい", 5.8, "japanese-romaji-alias"),
            ("sareta", "された", 6.0, "japanese-verb-suffix"),
            ("kunakatta", "くなかった", 5.8, "japanese-verb-suffix"),
            ("sityatta", "しちゃった", 6.1, "japanese-romaji-alias"),
            ("shichatta", "しちゃった", 6.1, "japanese-verb-suffix"),
            ("ha", "は", 5.7, "japanese-particle"),
            ("wa", "は", 5.3, "japanese-particle"),
            ("ga", "が", 5.5, "japanese-particle"),
            ("wo", "を", 5.7, "japanese-particle"),
            ("o", "を", 4.5, "japanese-particle"),
            ("ni", "に", 5.5, "japanese-particle"),
            ("de", "で", 5.5, "japanese-particle"),
            ("to", "と", 5.3, "japanese-particle"),
            ("mo", "も", 5.3, "japanese-particle"),
            ("he", "へ", 5.0, "japanese-particle"),
            ("no", "の", 5.5, "japanese-particle"),
            ("kara", "から", 5.4, "japanese-particle"),
            ("made", "まで", 5.4, "japanese-particle"),
            ("yori", "より", 5.4, "japanese-particle"),
            ("yo", "よ", 5.1, "japanese-particle")
        };

        var e = new (string raw, double weight)[]
        {
            ("commit", 6.8), ("issue", 6.6), ("reflect", 6.5), ("invite", 6.4),
            ("github", 7.0), ("linux", 6.7), ("kernel", 6.6), ("build", 6.4),
            ("network", 6.5), ("server", 6.4), ("cache", 6.3), ("deploy", 6.4),
            ("debug", 6.4), ("google", 6.8), ("push", 6.3), ("pull", 6.2),
            ("merge", 6.4), ("method", 6.5), ("branch", 6.4), ("repo", 5.9), ("code", 6.0),
            ("test", 6.1), ("api", 5.8), ("bug", 5.7), ("fix", 5.7),
            ("windows", 6.7), ("ubuntu", 6.7), ("the", 5.9), ("then", 6.0),
            ("and", 5.8), ("or", 5.2), ("for", 5.6), ("but", 5.5),
            ("with", 5.8), ("from", 5.8), ("not", 5.7), ("yes", 5.5),
            ("can", 5.6), ("this", 5.8), ("that", 5.8)
        };

        return j.Select(x => new LexiconEntry(x.raw, x.converted, LanguageKind.Japanese, x.weight, x.kind))
            .Concat(e.Select(x => new LexiconEntry(x.raw, x.raw, LanguageKind.English, x.weight, "english-lexeme")))
            .OrderByDescending(x => x.Raw.Length)
            .ThenByDescending(x => x.Weight)
            .ToArray();
    }
}
