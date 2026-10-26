using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Testing.UnitTests;

/// <summary>
/// A configuration value the binders cannot read stops a real web host at <c>StartAsync</c>,
/// before the first request, and names the key and the text.
/// </summary>
/// <remarks>
/// The unit tests of the binders run <c>IStartupValidator</c> on a bare container. This one goes
/// through the path an application takes: <c>WebApplication</c>, <c>AddTracon()</c> reading the
/// <c>Tracon</c> section of the host configuration, and the hosted service that runs
/// <c>ValidateOnStart</c>.
/// </remarks>
public sealed class ConfigurationStartupTests
{
    [Fact]
    public async Task A_web_host_with_values_the_binders_cannot_read_refuses_to_start()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Tools:DefaultMaxOutputBytes"] = "10MB",
            ["Tracon:RunRecording:RecordToolPayloads"] = "nope",
        });
        builder.Services.AddTracon(builder.Configuration.GetSection("Tracon"));

        await using var app = builder.Build();

        var failure = await Should.ThrowAsync<OptionsValidationException>(() => app.StartAsync());

        failure.Message.ShouldContain("Tracon:Tools:DefaultMaxOutputBytes is '10MB'");
        failure.Message.ShouldContain("Tracon:RunRecording:RecordToolPayloads is 'nope'");
    }

    [Fact]
    public async Task A_web_host_with_a_valid_configuration_starts()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Tools:DefaultMaxOutputBytes"] = "4096",
            ["Tracon:RunRecording:RecordToolPayloads"] = "false",
        });
        builder.Services.AddTracon(builder.Configuration.GetSection("Tracon"));

        await using var app = builder.Build();

        await app.StartAsync();
        await app.StopAsync();
    }
}
