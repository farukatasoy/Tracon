using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests;

/// <summary>
/// The options a <see cref="TraconMetrics"/> needs to publish <c>tracon.tenant.id</c> (Phase 238). The tag is off
/// by default, so a test that reads a tenant series asks for it.
/// </summary>
internal static class TenantTaggedMetricsOptions
{
    /// <summary>Creates options with <see cref="TraconObservabilityOptions.IncludeTenantTag"/> on.</summary>
    /// <returns>The options monitor to give <c>new TraconMetrics(factory, …)</c>.</returns>
    public static IOptionsMonitor<TraconOptions> Create()
    {
        var options = new TraconOptions();
        options.Observability.IncludeTenantTag = true;

        return new StaticOptionsMonitor<TraconOptions>(options);
    }
}
