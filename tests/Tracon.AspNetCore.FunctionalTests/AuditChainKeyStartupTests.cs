using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 260 (F-758): a host with a bad chain key configuration does not start, and its failure names the
/// configuration KEY and never a value.
/// </summary>
/// <remarks>
/// The keys are read lazily, on the first audit write. A lazy failure would be an audit entry lost in the
/// middle of a request, so the start reads them. Each case starts a real host: the hosted service that does
/// the reading only exists once the container is built.
/// </remarks>
public sealed class AuditChainKeyStartupTests
{
    // 32 bytes of 0x2A: easy to spot if a message ever carries it.
    private static readonly string GoodValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 32).ToArray());

    [Fact]
    public async Task A_key_id_with_no_value_stops_the_host_and_names_both_keys()
    {
        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Audit:ChainKeyId"] = "2026-10",
            }));

        failure.Message.ShouldContain("Tracon:Audit:ChainKeyId");
        failure.Message.ShouldContain("Tracon:AuditChainKeys:2026-10");
    }

    [Fact]
    public async Task A_value_that_is_not_base64_stops_the_host_and_is_not_printed()
    {
        const string NotBase64 = "this-is-a-canary-value-and-not-base64-!!";

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Audit:ChainKeyId"] = "k",
                ["Tracon:AuditChainKeys:k"] = NotBase64,
            }));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:k");
        failure.Message.ShouldNotContain(NotBase64);
    }

    [Fact]
    public async Task A_value_shorter_than_32_bytes_stops_the_host_and_is_not_printed()
    {
        var shortValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 16).ToArray());

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Audit:ChainKeyId"] = "k",
                ["Tracon:AuditChainKeys:k"] = shortValue,
            }));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:k");
        failure.Message.ShouldContain("32");
        failure.Message.ShouldNotContain(shortValue);
    }

    [Fact]
    public async Task An_upper_case_key_id_stops_the_host()
    {
        // Configuration keys ignore case: 'Key-A' and 'key-a' are ONE entry there but would be two ids in
        // the key_id column.
        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Audit:ChainKeyId"] = "Key-A",
                ["Tracon:AuditChainKeys:Key-A"] = GoodValue,
            }));

        failure.Message.ShouldContain("lowercase");
    }

    [Fact]
    public async Task A_verify_only_key_that_cannot_be_read_stops_the_host_too()
    {
        // The signing key is fine; an old key kept for verification has no value. Found at the start, not at
        // the first walk, months later.
        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Audit:ChainKeyId"] = "new",
                ["Tracon:AuditChainKeys:new"] = GoodValue,
                ["Tracon:AuditChainKeys:old"] = string.Empty,
            }));

        failure.Message.ShouldContain("Tracon:AuditChainKeys:old");
    }

    [Fact]
    public async Task A_host_without_the_setting_starts_exactly_as_before()
    {
        using var host = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal));

        host.Services.GetRequiredService<AuditChainKeyRing>().Active.ShouldBeNull();
    }

    [Fact]
    public async Task A_correct_key_starts_the_host_and_the_ring_signs_with_it()
    {
        using var host = await StartAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Audit:ChainKeyId"] = "2026-10",
            ["Tracon:AuditChainKeys:2026-10"] = GoodValue,
        });

        host.Services.GetRequiredService<AuditChainKeyRing>().Active.ShouldNotBeNull().Id.ShouldBe("2026-10");
    }

    private static async Task<IHost> StartAsync(Dictionary<string, string?> configuration)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new RecordingLoggerProvider());
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddTracon();

        var host = builder.Build();

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            host.Dispose();

            throw;
        }

        return host;
    }
}
