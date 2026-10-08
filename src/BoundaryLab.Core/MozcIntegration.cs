using System.Diagnostics;
using System.Text.Json;

namespace BoundaryLab.Core;

public sealed record MozcProbeResult(
    bool Available,
    bool Success,
    string Raw,
    string Preedit,
    IReadOnlyList<string> Candidates,
    double Quality,
    string? Error = null)
{
    public string? TopCandidate =>
        Candidates.Count == 0 ? null : Candidates[0];
}

public interface IMozcConversionOracle : IDisposable
{
    bool IsAvailable { get; }
    MozcProbeResult Probe(string raw);
}

public sealed class NullMozcConversionOracle : IMozcConversionOracle
{
    public bool IsAvailable => false;

    public MozcProbeResult Probe(string raw) =>
        new(false, false, raw, "", [], 0, "Mozc bridge is not available.");

    public void Dispose()
    {
    }
}

public sealed class MozcBridgeOracle : IMozcConversionOracle
{
    private sealed record BridgeResponse(
        bool Ok,
        string? Raw,
        string? Preedit,
        string[]? Candidates,
        string? Error);

    private readonly object _gate = new();
    private readonly Process _process;
    private bool _disposed;
    private bool _unresponsive;
    private static readonly TimeSpan BridgeResponseTimeout = TimeSpan.FromSeconds(3);

    private MozcBridgeOracle(string executablePath)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--stdio",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start Mozc bridge.");
    }

    public bool IsAvailable =>
        !_disposed && !_unresponsive && !_process.HasExited;

    public static IMozcConversionOracle TryCreateDefault()
    {
        var candidates = new List<string>();

        var configured =
            Environment.GetEnvironmentVariable("BOUNDARYLAB_MOZC_BRIDGE");
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        candidates.Add(Path.Combine(
            AppContext.BaseDirectory,
            "boundary_mozc_bridge.exe"));

        candidates.Add(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "artifacts",
            "mozc",
            "boundary_mozc_bridge.exe")));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                    return new MozcBridgeOracle(path);
            }
            catch
            {
                // Fall through and try the next location.
            }
        }

        return new NullMozcConversionOracle();
    }

    public MozcProbeResult Probe(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new(IsAvailable, false, raw, "", [], 0, "Empty query.");

        lock (_gate)
        {
            if (!IsAvailable)
                return new(false, false, raw, "", [], 0, "Mozc bridge exited.");

            try
            {
                _process.StandardInput.WriteLine(raw);
                _process.StandardInput.Flush();

                // A stalled Mozc process must not freeze the WinForms UI
                // indefinitely. A timed-out reader is abandoned only after
                // terminating the subprocess so future probes cannot race it.
                var line = _process.StandardOutput.ReadLineAsync()
                    .WaitAsync(BridgeResponseTimeout)
                    .GetAwaiter()
                    .GetResult();
                if (line is null)
                    return new(false, false, raw, "", [], 0, "Mozc bridge closed stdout.");

                var response = JsonSerializer.Deserialize<BridgeResponse>(
                    line,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (response is null || !response.Ok)
                    return new(true, false, raw, response?.Preedit ?? "", response?.Candidates ?? [],
                        0, response?.Error ?? "Invalid Mozc response.");

                var candidates = response.Candidates ?? [];
                var preedit = response.Preedit ?? "";
                var quality = EstimateQuality(raw, preedit, candidates);

                return new(
                    true,
                    true,
                    raw,
                    preedit,
                    candidates,
                    quality,
                    response.Error);
            }
            catch (TimeoutException)
            {
                _unresponsive = true;
                try
                {
                    if (!_process.HasExited)
                        _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already exited or inaccessible. Keep the oracle disabled.
                }

                return new(false, false, raw, "", [], 0,
                    "Mozc bridge response timed out after 3 seconds.");
            }
            catch (Exception ex)
            {
                return new(IsAvailable, false, raw, "", [], 0, ex.Message);
            }
        }
    }

    private static double EstimateQuality(
        string raw,
        string preedit,
        IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
            return string.IsNullOrEmpty(preedit) ? 0.05 : 0.45;

        var top = candidates[0];
        if (string.Equals(top, raw, StringComparison.OrdinalIgnoreCase))
            return 0.20;

        var nonAscii = top.Any(c => c > 0x7f);
        if (nonAscii)
            return candidates.Count >= 2 ? 0.98 : 0.92;

        if (!string.Equals(top, preedit, StringComparison.Ordinal))
            return 0.70;

        return 0.58;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.WriteLine(":quit");
                    _process.StandardInput.Flush();
                    if (!_process.WaitForExit(500))
                        _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
