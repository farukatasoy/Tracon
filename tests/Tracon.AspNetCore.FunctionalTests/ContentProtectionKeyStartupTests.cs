using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host whose active content protection key has a value that cannot be a key does not start, and its failure names the
/// configuration KEY and never a value.
/// </summary>
/// <remarks>
/// <para>
/// The key is read when the first value is written. A recording write is best effort: a failure is logged and counted and
/// the run goes on, so a key of the wrong length did not fail anything an operator could see. The runs answered 200, the
/// protected columns stayed empty, and the only trace was a counter. Reading the active key at the start moves the failure
/// to the one moment an operator is watching.
/// </para>
/// <para>
/// Only the ACTIVE key is read, and only when it has a value. A key kept to read old rows can be absent for a while during
/// a rotation, and a host that refused to start for it would turn a rotation into an outage. An active key with no value
/// is how the reference host and the OpenAPI fuzz run start on in-memory stores, where nothing calls the protector.
/// </para>
/// </remarks>
public sealed class ContentProtectionKeyStartupTests
{
    private const string KeyName = "ContentProtectionKeys:Startup";

    // 32 bytes of 0x2A: easy to spot if a message ever carries it.
    private static readonly string GoodValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 32).ToArray());

    [Fact]
    public async Task An_active_key_with_no_value_does_not_stop_the_host()
    {
        // The reference host and the OpenAPI fuzz run start like this on in-memory stores, where nothing calls the
        // protector: the key name is configured and the material is supplied only with a SQL store.
        using var host = await StartAsync(value: null);

        host.Services.GetRequiredService<IContentProtector>().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task An_active_key_with_no_value_stops_a_host_with_a_database_store_before_the_database_exists()
    {
        // With a SQL store every protected write reads the key. The run still answers 200 (the failure is a frame inside
        // the stream), its record and its input are lost on a best-effort write, and the session is never saved. The one
        // moment an operator is watching is the start, and the migration has not run yet.
        using var database = new TempSqliteDatabase("keyless-start");

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(value: null, database: database));

        failure.Message.ShouldContain(KeyName);
        failure.Message.ShouldContain("no value");
        File.Exists(database.Path).ShouldBeFalse("the gate runs before the migration creates the database");
    }

    [Fact]
    public async Task A_good_active_key_starts_a_host_with_a_database_store()
    {
        using var database = new TempSqliteDatabase("keyed-start");
        using var host = await StartAsync(GoodValue, database: database);

        host.Services.GetRequiredService<IContentProtector>().IsEnabled.ShouldBeTrue();
        File.Exists(database.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Content_protection_switched_off_does_not_ask_a_database_store_for_a_key()
    {
        using var database = new TempSqliteDatabase("keyless-off");
        using var host = await StartAsync(
            value: null,
            extra: new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:ContentProtection:Enabled"] = "false" },
            database: database);

        host.Services.GetRequiredService<IContentProtector>().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_protector_the_host_supplies_is_not_asked_for_a_key_with_a_database_store()
    {
        using var database = new TempSqliteDatabase("keyless-supplied");
        using var host = await StartAsync(
            value: null,
            protector: static builder => builder.AddContentProtection<SuppliedProtector>(),
            database: database);

        host.Services.GetRequiredService<IContentProtector>().ShouldBeOfType<SuppliedProtector>();
    }

    [Fact]
    public async Task An_active_key_that_is_not_base64_stops_the_host_and_is_not_printed()
    {
        const string NotBase64 = "this-is-a-canary-value-and-not-base64-!!";

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(NotBase64));

        failure.Message.ShouldContain(KeyName);
        failure.Message.ShouldNotContain(NotBase64);
    }

    [Fact]
    public async Task An_active_key_of_31_bytes_stops_the_host_and_is_not_printed()
    {
        var shortValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 31).ToArray());

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await StartAsync(shortValue));

        failure.Message.ShouldContain(KeyName);
        failure.Message.ShouldContain("32 bytes");
        failure.Message.ShouldNotContain(shortValue);
    }

    [Fact]
    public async Task A_good_active_key_starts_the_host()
    {
        using var host = await StartAsync(GoodValue);

        host.Services.GetRequiredService<IContentProtector>().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_key_kept_for_old_rows_that_cannot_be_read_does_not_stop_the_host()
    {
        // The rotation: the new key is active and good, the old one is listed and its value is not there yet.
        using var host = await StartAsync(
            GoodValue,
            extra: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:ContentProtection:Keys:old"] = "ContentProtectionKeys:Old",
            });

        host.Services.GetRequiredService<IContentProtector>().IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_short_key_with_content_protection_off_does_not_stop_the_host()
    {
        var shortValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x2A, 31).ToArray());

        using var host = await StartAsync(
            shortValue,
            extra: new Dictionary<string, string?>(StringComparer.Ordinal) { ["Tracon:ContentProtection:Enabled"] = "false" });

        host.Services.GetRequiredService<IContentProtector>().IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_protector_the_host_supplies_is_not_asked_for_a_key()
    {
        // The check knows the protector Tracon ships; another one decides for itself what a key is.
        using var host = await StartAsync(value: null, protector: static builder => builder.AddContentProtection<SuppliedProtector>());

        host.Services.GetRequiredService<IContentProtector>().ShouldBeOfType<SuppliedProtector>();
    }

    private static async Task<IHost> StartAsync(
        string? value,
        Dictionary<string, string?>? extra = null,
        Action<ITraconBuilder>? protector = null,
        TempSqliteDatabase? database = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new RecordingLoggerProvider());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Enabled"] = "true",
            ["Tracon:ContentProtection:ActiveKeyId"] = "startup",
            ["Tracon:ContentProtection:Keys:startup"] = KeyName,
        });

        if (value is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [KeyName] = value });
        }

        if (extra is not null)
        {
            builder.Configuration.AddInMemoryCollection(extra);
        }

        var tracon = builder.AddTracon();

        if (protector is null)
        {
            tracon.AddContentProtection();
        }
        else
        {
            protector(tracon);
        }

        if (database is not null)
        {
            tracon.UseSqlite(database.ConnectionString);
        }

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

    private sealed class SuppliedProtector : IContentProtector
    {
        public bool IsEnabled => true;

        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string stored) => stored;

        public byte[] ProtectBytes(ReadOnlySpan<byte> plaintext) => plaintext.ToArray();

        public byte[] UnprotectBytes(ReadOnlySpan<byte> stored) => stored.ToArray();
    }
}
