using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

public static class EvidenceLatticeResearchExporter
{
    public static EvidenceLatticeResearchReport CreateReport(
        EvidenceLatticeResult result,
        EvidenceLatticeParameters parameters) =>
        new(
            "incremental-boundary-lab/mozc-responsibility-v1",
            parameters.EngineId.StartsWith(
                "stream-hybrid-v1", StringComparison.Ordinal)
                ? StreamHybridSession.AlgorithmVersion
                : parameters.UsePhoneticFirstHybrid
                    ? "iblab-phonetic-four-state-v0.8"
                    : EvidenceLatticeSession.AlgorithmVersion,
            DateTimeOffset.UtcNow,
            result.Input,
            result.Output,
            parameters,
            result.CommittedRawLength,
            result.CommittedSegments,
            result.ActiveBestSegments,
            result.Frames);

    public static string ToJson(
        EvidenceLatticeResearchReport report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(
            new JsonStringEnumConverter());

        return JsonSerializer.Serialize(
            report,
            options);
    }
}
