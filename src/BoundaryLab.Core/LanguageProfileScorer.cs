namespace BoundaryLab.Core;

internal static class LanguageProfileScorer
{
    private sealed class Profile
    {
        private readonly Dictionary<string, int> _grams = [];
        private readonly int _total;
        private readonly int _vocabulary;

        public Profile(IEnumerable<string> samples)
        {
            foreach (var sample in samples
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Select(s => "^" + s.ToLowerInvariant() + "$"))
            {
                for (var n = 2; n <= 3; n++)
                {
                    for (var i = 0; i + n <= sample.Length; i++)
                    {
                        var gram = sample.Substring(i, n);
                        _grams.TryGetValue(gram, out var count);
                        _grams[gram] = count + 1;
                    }
                }
            }

            _total = Math.Max(1, _grams.Values.Sum());
            _vocabulary = Math.Max(32, _grams.Count);
        }

        public double Score(string raw)
        {
            if (raw.Length == 0)
                return -8;

            var text = "^" + raw.ToLowerInvariant() + "$";
            var log = 0.0;
            var count = 0;

            for (var n = 2; n <= 3; n++)
            {
                for (var i = 0; i + n <= text.Length; i++)
                {
                    var gram = text.Substring(i, n);
                    _grams.TryGetValue(gram, out var seen);
                    var probability =
                        (seen + 0.35) /
                        (_total + 0.35 * _vocabulary);
                    log += Math.Log(probability);
                    count++;
                }
            }

            return count == 0 ? -8 : log / count;
        }
    }

    private static readonly string[] JapaneseSeeds =
    [
        "hennkan", "hennkann", "kannsite", "mondai", "tyotto",
        "kotoba", "bunnkatu", "dekitenai", "ninnsiki", "bunmyaku",
        "koreha", "soreha", "dousuru", "yappari", "tokoro",
        "kangaeru", "kanousei", "wakaranai", "tadasii", "tukau",
        "iretemo", "nasasou", "dayone", "demo", "nanoga"
    ];

    private static readonly string[] EnglishSeeds =
    [
        "node", "js", "jsx", "ts", "tsx", "json", "html", "css",
        "javascript", "typescript", "python", "rust",
        "class", "string", "method", "function", "object", "client",
        "server", "network", "debug", "issue", "commit", "merge",
        "branch", "github", "linux", "kernel", "cache", "build",
        "deploy", "window", "button", "input", "output", "token"
    ];

    private static readonly Profile Japanese = new(
        Lexicon.Entries
            .Where(e => e.Language == LanguageKind.Japanese)
            .Select(e => e.Raw)
            .Concat(JapaneseSeeds));

    private static readonly Profile English = new(
        Lexicon.Entries
            .Where(e => e.Language == LanguageKind.English)
            .Select(e => e.Raw)
            .Concat(EnglishSeeds));

    public static (double Japanese, double English) Score(string raw)
    {
        var letters = new string(
            raw.Where(char.IsAsciiLetter).ToArray());

        if (letters.Length == 0)
            return (-8, -8);

        return (Japanese.Score(letters), English.Score(letters));
    }

    public static double Advantage(string raw, LanguageKind language)
    {
        var (ja, en) = Score(raw);
        var diff = language == LanguageKind.English
            ? en - ja
            : ja - en;

        return Math.Clamp(diff, -2.5, 2.5);
    }
}
