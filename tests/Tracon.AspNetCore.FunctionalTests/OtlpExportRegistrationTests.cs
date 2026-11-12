using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Tracon.Api;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The reference host registers the OTLP exporter only when a collector is named.
/// </summary>
/// <remarks>
/// <para>
/// Without <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> the sample has to behave exactly as it did before telemetry export existed:
/// nothing registers, and nothing tries to reach a collector. The condition is one <c>if</c>; moving the registration out
/// of it, or dropping the length test so that an empty value counts as an endpoint, would not fail any other test, and
/// the only reader of this rule was a run by hand that counted connections.
/// </para>
/// <para>
/// The sample's <c>Program</c> cannot be hosted by a test, so the registration is a method of its own
/// (<see cref="OtlpExport.AddWhenConfigured"/>) that the test reads through the services it fills.
/// </para>
/// </remarks>
public sealed class OtlpExportRegistrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_an_endpoint_nothing_is_registered(string? endpoint)
    {
        var services = new ServiceCollection();

        OtlpExport.AddWhenConfigured(services, ConfigurationWith(endpoint)).ShouldBeFalse();

        services.ShouldBeEmpty("a sample without a collector must not register an exporter, a provider or a hosted service");
    }

    [Fact]
    public void With_an_endpoint_the_trace_and_metric_providers_are_registered()
    {
        var services = new ServiceCollection();

        OtlpExport.AddWhenConfigured(services, ConfigurationWith("http://collector.invalid:4317")).ShouldBeTrue();

        services.ShouldContain(static descriptor => descriptor.ServiceType == typeof(TracerProvider));
        services.ShouldContain(static descriptor => descriptor.ServiceType == typeof(MeterProvider));
    }

    private static IConfiguration ConfigurationWith(string? endpoint)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(endpoint is null
                ? []
                : new Dictionary<string, string?>(StringComparer.Ordinal) { [OtlpExport.EndpointVariable] = endpoint })
            .Build();
}
