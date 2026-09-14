using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Tracon.Capacity;

namespace Tracon.CapacityHost;

/// <summary>The measurement surface the host adds beside Tracon's own.</summary>
/// <remarks>
/// <para>
/// 🚨 Three endpoints, all outside Tracon's prefix, all belonging to the
/// apparatus rather than to the product. Nothing here is packed, shipped, or
/// part of any HTTP contract a consumer sees.
/// </para>
/// <para>
/// 🚨 <c>/capacity/settings</c> reports the host's EFFECTIVE values, read from
/// the options the process actually resolved. That is the point: the worker
/// axis must be able to prove the host is not leasing, and a value copied from
/// the profile that was supposed to configure it would prove nothing.
/// </para>
/// </remarks>
public static class CapacityApparatus
{
    /// <summary>Maps the apparatus endpoints.</summary>
    /// <param name="app">The application.</param>
    /// <param name="settings">What this process was started with.</param>
    /// <param name="counters">The host's counters.</param>
    public static void Map(WebApplication app, CapacitySettings settings, CapacityCounters counters)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(counters);

        app.MapGet(CapacityContract.SettingsRoute, (IServiceProvider services) =>
        {
            var scheduling = services.GetRequiredService<IOptions<TraconSchedulingOptions>>().Value;
            var pool = new NpgsqlConnectionStringBuilder(settings.ConnectionString).MaxPoolSize;

            return Results.Json(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runWorker"] = scheduling.RunWorker,
                ["maxConcurrentJobs"] = scheduling.MaxConcurrentJobs,
                ["pollIntervalSeconds"] = scheduling.PollInterval.TotalSeconds,
                ["leaseDurationSeconds"] = scheduling.LeaseDuration.TotalSeconds,
                ["maxPoolSize"] = settings.MaxPoolSize,
                ["retentionEnabled"] = RetentionEnabled(services),
                ["responseCacheEnabled"] = false,
                ["recordingEnabled"] = true,
                ["schema"] = settings.Schema,
                ["processName"] = settings.Name,
                ["processId"] = Environment.ProcessId,
                ["traconVersion"] = TraconVersion(),
                ["runtimeVersion"] = Environment.Version.ToString(),
                ["inFlightRuns"] = CapacityRunLedger.InFlight,

                // 🚨 The pool value above is the one this process was told to
                // use; the one below is what the connection string actually
                // carries after the builder wrote it. They are reported
                // separately so a silent mismatch cannot hide.
                ["effectivePoolSize"] = pool,
            });
        });

        app.MapGet(CapacityContract.TelemetryRoute, () => Results.Json(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["modelCalls"] = counters.ModelCalls,
                ["modelTurns"] = counters.ModelTurns,
                ["toolInvocations"] = counters.ToolInvocations,
                ["recordingFailures"] = counters.RecordingFailures,

                // An instrument this host cannot supply is NAMED here, never
                // written as a zero somewhere the reader would take for a
                // measurement.
                ["unavailable"] = Array.Empty<string>(),
            }));

        app.MapPost(CapacityContract.ResetRoute, () =>
        {
            counters.Reset();
            return Results.NoContent();
        });
    }

    private static bool RetentionEnabled(IServiceProvider services)
    {
        // Retention changes what storage growth MEANS: with rows being deleted
        // underneath it, bytes-per-run answers a different question. The
        // manifest records the answer either way.
        var options = services.GetService<IOptions<TraconRetentionOptions>>();
        return options?.Value.Enabled ?? false;
    }

    private static string TraconVersion()
        => typeof(ITraconBuilder).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(ITraconBuilder).Assembly.GetName().Version?.ToString()
           ?? "unknown";
}
