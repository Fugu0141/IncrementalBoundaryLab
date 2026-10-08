using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoundaryLab.Core;

public static class ResearchExporter
{
    public static ResearchReport CreateReport(
        AnalysisResult result,
        RecognizerParameters parameters) =>
        new(
            "incremental-boundary-lab/research-v2",
            IncrementalRecognizer.AlgorithmVersion,
            DateTimeOffset.UtcNow,
            result.Input,
            result.Converted,
            parameters,
            result.Segments,
            result.Boundaries,
            result.Frames);

    public static string ToJson(ResearchReport report)
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
