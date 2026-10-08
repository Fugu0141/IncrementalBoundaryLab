namespace BoundaryLab.Core;

internal static class RomajiConverter
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kya"]="きゃ", ["kyu"]="きゅ", ["kyo"]="きょ",
            ["gya"]="ぎゃ", ["gyu"]="ぎゅ", ["gyo"]="ぎょ",
            ["sha"]="しゃ", ["shu"]="しゅ", ["sho"]="しょ",
            ["sya"]="しゃ", ["syu"]="しゅ", ["syo"]="しょ",
            ["ja"]="じゃ", ["ju"]="じゅ", ["jo"]="じょ",
            ["jya"]="じゃ", ["jyu"]="じゅ", ["jyo"]="じょ",
            ["cha"]="ちゃ", ["chu"]="ちゅ", ["cho"]="ちょ",
            ["tya"]="ちゃ", ["tyu"]="ちゅ", ["tyo"]="ちょ",
            ["nya"]="にゃ", ["nyu"]="にゅ", ["nyo"]="にょ",
            ["hya"]="ひゃ", ["hyu"]="ひゅ", ["hyo"]="ひょ",
            ["bya"]="びゃ", ["byu"]="びゅ", ["byo"]="びょ",
            ["pya"]="ぴゃ", ["pyu"]="ぴゅ", ["pyo"]="ぴょ",
            ["mya"]="みゃ", ["myu"]="みゅ", ["myo"]="みょ",
            ["rya"]="りゃ", ["ryu"]="りゅ", ["ryo"]="りょ",
            ["shi"]="し", ["chi"]="ち", ["tsu"]="つ", ["fu"]="ふ",
            ["ka"]="か", ["ki"]="き", ["ku"]="く", ["ke"]="け", ["ko"]="こ",
            ["ga"]="が", ["gi"]="ぎ", ["gu"]="ぐ", ["ge"]="げ", ["go"]="ご",
            ["sa"]="さ", ["si"]="し", ["su"]="す", ["se"]="せ", ["so"]="そ",
            ["za"]="ざ", ["zi"]="じ", ["zu"]="ず", ["ze"]="ぜ", ["zo"]="ぞ",
            ["ta"]="た", ["ti"]="ち", ["tu"]="つ", ["te"]="て", ["to"]="と",
            ["da"]="だ", ["di"]="ぢ", ["du"]="づ", ["de"]="で", ["do"]="ど",
            ["na"]="な", ["ni"]="に", ["nu"]="ぬ", ["ne"]="ね", ["no"]="の",
            ["ha"]="は", ["hi"]="ひ", ["hu"]="ふ", ["he"]="へ", ["ho"]="ほ",
            ["ba"]="ば", ["bi"]="び", ["bu"]="ぶ", ["be"]="べ", ["bo"]="ぼ",
            ["pa"]="ぱ", ["pi"]="ぴ", ["pu"]="ぷ", ["pe"]="ぺ", ["po"]="ぽ",
            ["ma"]="ま", ["mi"]="み", ["mu"]="む", ["me"]="め", ["mo"]="も",
            ["ya"]="や", ["yu"]="ゆ", ["yo"]="よ",
            ["ra"]="ら", ["ri"]="り", ["ru"]="る", ["re"]="れ", ["ro"]="ろ",
            ["wa"]="わ", ["wo"]="を",
            ["a"]="あ", ["i"]="い", ["u"]="う", ["e"]="え", ["o"]="お"
        };

    public static bool TryConvert(string raw, out string converted)
    {
        converted = "";
        if (raw.Length == 0)
            return false;

        var output = new System.Text.StringBuilder();
        var i = 0;

        while (i < raw.Length)
        {
            if (!TryConsume(raw, i, out var consumed, out var kana))
                return false;

            output.Append(kana);
            i += consumed;
        }

        converted = output.ToString();
        return true;
    }

    public static bool TryConsume(
        string raw,
        int index,
        out int consumed,
        out string kana)
    {
        consumed = 0;
        kana = "";

        if (index < 0 || index >= raw.Length)
            return false;

        if (index + 1 < raw.Length &&
            raw[index] == raw[index + 1] &&
            raw[index] is not ('a' or 'i' or 'u' or 'e' or 'o' or 'n'))
        {
            consumed = 1;
            kana = "っ";
            return true;
        }

        if (raw[index] == 'n')
        {
            if (index + 1 < raw.Length && raw[index + 1] == 'n')
            {
                consumed = 2;
                kana = "ん";
                return true;
            }

            if (index == raw.Length - 1)
            {
                consumed = 1;
                kana = "ん";
                return true;
            }

            var next = raw[index + 1];
            if ("aiueoy".IndexOf(next) < 0)
            {
                consumed = 1;
                kana = "ん";
                return true;
            }
        }

        for (var len = Math.Min(3, raw.Length - index); len >= 1; len--)
        {
            var token = raw.Substring(index, len);
            if (!Map.TryGetValue(token, out var mapped))
                continue;

            consumed = len;
            kana = mapped;
            return true;
        }

        return false;
    }

    public static bool IsPossiblePrefix(string rawTail)
    {
        if (rawTail.Length == 0)
            return false;

        if (rawTail == "n")
            return true;

        return Map.Keys.Any(key =>
            key.Length > rawTail.Length &&
            key.StartsWith(rawTail, StringComparison.Ordinal));
    }
}
