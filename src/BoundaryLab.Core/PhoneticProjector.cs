using System.Text;

namespace BoundaryLab.Core;

internal static class PhoneticProjector
{
    public static PhoneticProjection Project(string raw)
    {
        if (raw.Length == 0)
            return new PhoneticProjection("", "", 1, 0, 0, []);

        var units = new List<PhoneticUnit>();
        var preview = new StringBuilder();
        var kanaCharacters = 0;
        var ambiguousCharacters = 0;
        var i = 0;

        while (i < raw.Length)
        {
            if (RomajiConverter.TryConsume(raw, i, out var consumed, out var kana))
            {
                var token = raw.Substring(i, consumed);
                units.Add(new PhoneticUnit(
                    i,
                    i + consumed,
                    token,
                    kana,
                    PhoneticUnitKind.Kana,
                    1.0,
                    "romaji-token"));
                preview.Append(kana);
                kanaCharacters += consumed;
                i += consumed;
                continue;
            }

            var maxPrefix = Math.Min(3, raw.Length - i);
            var prefixLength = 0;
            for (var len = maxPrefix; len >= 1; len--)
            {
                var tail = raw.Substring(i, len);
                if (!RomajiConverter.IsPossiblePrefix(tail))
                    continue;
                prefixLength = len;
                break;
            }

            if (prefixLength > 0 && i + prefixLength == raw.Length)
            {
                var token = raw.Substring(i, prefixLength);
                units.Add(new PhoneticUnit(
                    i,
                    i + prefixLength,
                    token,
                    $"{{{token}…}}",
                    PhoneticUnitKind.Ambiguous,
                    0.35,
                    "incomplete-romaji-prefix"));
                preview.Append($"{{{token}…}}");
                ambiguousCharacters += prefixLength;
                i += prefixLength;
                continue;
            }

            var pending = raw[i].ToString();
            units.Add(new PhoneticUnit(
                i,
                i + 1,
                pending,
                $"{{{pending}?}}",
                PhoneticUnitKind.Pending,
                0.05,
                "not-readable-as-romaji-here"));
            preview.Append($"{{{pending}?}}");
            i++;
        }

        var kanaCoverage = (double)kanaCharacters / raw.Length;
        var ambiguousCoverage = (double)ambiguousCharacters / raw.Length;
        var unresolved = Math.Clamp(
            1.0 - kanaCoverage - ambiguousCoverage * 0.35,
            0,
            1);

        return new PhoneticProjection(
            raw,
            preview.ToString(),
            kanaCoverage,
            ambiguousCoverage,
            unresolved,
            units);
    }

    public static double Readability(
        PhoneticProjection projection,
        int start,
        int end)
    {
        if (end <= start)
            return 0;

        var weighted = 0.0;
        var length = end - start;

        foreach (var unit in projection.Units)
        {
            var overlapStart = Math.Max(start, unit.Start);
            var overlapEnd = Math.Min(end, unit.End);
            if (overlapEnd <= overlapStart)
                continue;

            var overlap = overlapEnd - overlapStart;
            weighted += overlap * unit.Confidence;
        }

        return Math.Clamp(weighted / length, 0, 1);
    }

    public static string PreviewRange(
        PhoneticProjection projection,
        int start,
        int end)
    {
        var builder = new StringBuilder();

        foreach (var unit in projection.Units)
        {
            if (unit.Start >= start && unit.End <= end)
                builder.Append(unit.Preview);
        }

        return builder.ToString();
    }
}
