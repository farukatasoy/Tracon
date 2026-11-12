using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Tracon;

namespace Tracon.Api;

/// <summary>
/// Registers the OTLP exporter of the reference application, and only when an endpoint is configured.
/// </summary>
/// <remarks>
/// <para>
/// Tracon does not choose an exporter; the application does. The exporter is registered ONLY when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set - the Aspire AppHost (<c>samples/Tracon.AppHost</c>) sets it, and so can any
/// collector setup. Without it nothing here runs and the sample behaves exactly as before.
/// </para>
/// <para>
/// <c>UseOtlpExporter()</c> reads the standard <c>OTEL_*</c> variables (endpoint, protocol, headers, service name), so no
/// address is written here. If the endpoint is unreachable the SDK drops what it cannot send; it does not stop the
/// application.
/// </para>
/// <para>
/// A method of its own, and not lines in <c>Program</c>, so that a test can call it with a configuration and read what it
/// registered: the sample's <c>Program</c> is two thousand lines that nothing can host.
/// </para>
/// </remarks>
internal static class OtlpExport
{
    /// <summary>The standard variable that names the collector.</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Registers the OpenTelemetry providers and the OTLP exporter when the endpoint variable has a value.</summary>
    /// <param name="services">The services of the application.</param>
    /// <param name="configuration">The configuration of the application, where the environment variables are read from.</param>
    /// <returns><see langword="true"/> when the exporter was registered; <see langword="false"/> when no endpoint is set.</returns>
    public static bool AddWhenConfigured(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration[EndpointVariable] is not { Length: > 0 })
        {
            return false;
        }

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                // The request span is the parent of `tracon.run`, so the dashboard shows one tree per request.
                .AddAspNetCoreInstrumentation()
                .AddSource(TraconDiagnostics.ActivitySourceName)
                // The framework's own workflow spans, for a graph built in code with `WithOpenTelemetry()`.
                .AddSource("Microsoft.Agents.AI.Workflows"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter(TraconDiagnostics.MeterName))
            .UseOtlpExporter();

        return true;
    }
}
