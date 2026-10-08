using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

public static class PhoneticFirstResearchExporter
{
    public static PhoneticFirstResearchReport CreateReport(
        PhoneticFirstAnalysisResult result,
        PhoneticFirstParameters parameters) =>
        new(
            "incremental-boundary-lab/phonetic-first-rcr-v2",
            PhoneticFirstSession.AlgorithmVersion,
            DateTimeOffset.UtcNow,
            result.Input,
            result.Output,
            parameters,
            result.CommittedRawLength,
            result.CommittedSegments,
            result.ActiveSegments,
            result.Frames);

    public static string ToJson(PhoneticFirstResearchReport report)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(report, options);
    }
}
