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
            if (i + 1 < raw.Length &&
                raw[i] == raw[i + 1] &&
                raw[i] is not ('a' or 'i' or 'u' or 'e' or 'o' or 'n'))
            {
                output.Append('っ');
                i++;
                continue;
            }

            if (raw[i] == 'n')
            {
                if (i == raw.Length - 1)
                {
                    output.Append('ん');
                    i++;
                    continue;
                }

                var next = raw[i + 1];
                if (next == 'n')
                {
                    output.Append('ん');
                    i++;
                    continue;
                }

                if ("aiueoy".IndexOf(next) < 0)
                {
                    output.Append('ん');
                    i++;
                    continue;
                }
            }

            var matched = false;
            for (var len = Math.Min(3, raw.Length - i); len >= 1; len--)
            {
                var token = raw.Substring(i, len);
                if (!Map.TryGetValue(token, out var kana))
                    continue;

                output.Append(kana);
                i += len;
                matched = true;
                break;
            }

            if (!matched)
                return false;
        }

        converted = output.ToString();
        return true;
    }
}
