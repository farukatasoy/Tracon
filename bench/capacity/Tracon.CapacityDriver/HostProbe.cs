using System.Text.Json;
using Tracon.Capacity;

namespace Tracon.CapacityDriver;

/// <summary>What the host reports about itself at run time.</summary>
/// <remarks>
/// 🚨 These values are read from the running process, not copied from the
/// profile that was supposed to configure it. The worker axis depends on it:
/// a host whose own in-process worker is still leasing turns the "1 worker"
/// cell into two workers and shifts every point on the axis, and the only
/// honest way to know is to ask the process that is running.
/// </remarks>
public sealed record HostSettings(
    bool RunWorker,
    int MaxConcurrentJobs,
    double PollIntervalSeconds,
    double LeaseDurationSeconds,
    int MaxPoolSize,
    bool RetentionEnabled,
    bool ResponseCacheEnabled,
    bool RecordingEnabled,
    string Schema,
    string ProcessName,
    int ProcessId,
    string TraconVersion,
    string RuntimeVersion);

/// <summary>The counters the host kept while it served.</summary>
public sealed record HostTelemetry(
    long ModelCalls,
    long ModelTurns,
    long ToolInvocations,
    long RecordingFailures,
    IReadOnlyList<string> Unavailable);

/// <summary>Reads the host's apparatus surface.</summary>
public sealed class HostProbe
{
    private readonly HttpClient _client;
    private readonly string _origin;

    /// <summary>Creates a probe against one host.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="baseAddress">The host's Tracon prefix, e.g. <c>http://127.0.0.1:5199/tracon</c>.</param>
    public HostProbe(HttpClient client, string baseAddress)
    {
        _client = client;

        var uri = new Uri(baseAddress, UriKind.Absolute);
        _origin = uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>Reads the host's effective settings.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings.</returns>
    public async Task<HostSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        using var document = await GetAsync(CapacityContract.SettingsRoute, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        return new HostSettings(
            root.GetProperty("runWorker").GetBoolean(),
            root.GetProperty("maxConcurrentJobs").GetInt32(),
            root.GetProperty("pollIntervalSeconds").GetDouble(),
            root.GetProperty("leaseDurationSeconds").GetDouble(),
            root.GetProperty("maxPoolSize").GetInt32(),
            root.GetProperty("retentionEnabled").GetBoolean(),
            root.GetProperty("responseCacheEnabled").GetBoolean(),
            root.GetProperty("recordingEnabled").GetBoolean(),
            root.GetProperty("schema").GetString() ?? "",
            root.GetProperty("processName").GetString() ?? "",
            root.GetProperty("processId").GetInt32(),
            root.GetProperty("traconVersion").GetString() ?? "",
            root.GetProperty("runtimeVersion").GetString() ?? "");
    }

    /// <summary>Reads the host's counters.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The counters.</returns>
    public async Task<HostTelemetry> ReadTelemetryAsync(CancellationToken cancellationToken)
    {
        using var document = await GetAsync(CapacityContract.TelemetryRoute, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var unavailable = new List<string>();

        if (root.TryGetProperty("unavailable", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.GetString() is { Length: > 0 } reason)
                {
                    unavailable.Add(reason);
                }
            }
        }

        return new HostTelemetry(
            root.GetProperty("modelCalls").GetInt64(),
            root.GetProperty("modelTurns").GetInt64(),
            root.GetProperty("toolInvocations").GetInt64(),
            root.GetProperty("recordingFailures").GetInt64(),
            unavailable);
    }

    /// <summary>Resets the host's counters so the warm-up does not leak into the window.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the host has reset.</returns>
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        using var response = await _client
            .PostAsync(new Uri(_origin + CapacityContract.ResetRoute, UriKind.Absolute), content: null, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonDocument> GetAsync(string route, CancellationToken cancellationToken)
    {
        using var response = await _client
            .GetAsync(new Uri(_origin + route, UriKind.Absolute), cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(payload);
    }
}
