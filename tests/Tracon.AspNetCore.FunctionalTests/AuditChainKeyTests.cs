using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 260 (F-758) at the host boundary: the chain key is read from configuration, signs what the
/// admin endpoints write, and is judged by <c>GET /api/audit/verify</c> across host restarts.
/// </summary>
/// <remarks>
/// <para>
/// Every case runs on a real SQLite file so a second host can read what the first one wrote: the key ring,
/// the <c>key_id</c> column and the walk are three separate pieces that only agree together. Entries are
/// written straight to <see cref="IAuditLog"/> where the count matters; one case goes through an admin
/// endpoint so the real write path is covered too.
/// </para>
/// <para>
/// A host is restarted by disposing one and starting another on the same file with a different
/// configuration. That is exactly what removing or adding a key looks like to an operator.
/// </para>
/// </remarks>
public sealed class AuditChainKeyTests
{
    private const string KeyId = "2026-10";
    private const string Tenant = "default";

    private static readonly Uri Verify = new("/tracon/api/audit/verify", UriKind.Relative);
    private static readonly Uri AuditList = new("/tracon/api/audit?limit=100", UriKind.Relative);

    private static readonly string KeyValue =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray());

    [Fact]
    public async Task The_key_is_read_from_configuration_and_signs_what_an_admin_endpoint_writes()
    {
        using var database = new TempSqliteDatabase("audit-key-signs");

        await using var host = await StartAsync(database, withKey: true);

        using (var save = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/retention/run_events", UriKind.Relative),
            new { maxAgeDays = 30 },
            TestContext.Current.CancellationToken))
        {
            save.EnsureSuccessStatusCode();
        }

        var entries = await ReadEntriesAsync(host);

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(static entry => string.Equals(KeyOf(entry), KeyId, StringComparison.Ordinal));

        var verification = await VerifyAsync(host);

        verification.GetProperty("status").GetString().ShouldBe("Valid");
        verification.GetProperty("unkeyedEntries").GetInt32().ShouldBe(0);
        verification.GetProperty("entriesChecked").GetInt32().ShouldBe(entries.Count);
    }

    [Fact]
    public async Task A_chain_started_without_a_key_stays_valid_and_counts_its_unkeyed_entries_after_the_key_is_turned_on()
    {
        using var database = new TempSqliteDatabase("audit-key-history");

        await using (var before = await StartAsync(database, withKey: false))
        {
            await WriteAsync(before, "agent:one");
            await WriteAsync(before, "agent:two");
        }

        await using var after = await StartAsync(database, withKey: true);

        await WriteAsync(after, "agent:three");

        var verification = await VerifyAsync(after);

        verification.GetProperty("status").GetString().ShouldBe("Valid");
        verification.GetProperty("entriesChecked").GetInt32().ShouldBe(3);
        verification.GetProperty("unkeyedEntries").GetInt32().ShouldBe(2);

        var entries = await ReadEntriesAsync(after);

        entries.Count(static entry => KeyOf(entry) is null).ShouldBe(2);
        entries.Count(static entry => string.Equals(KeyOf(entry), KeyId, StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public async Task A_removed_key_is_reported_as_unknown_key_at_the_first_entry_it_signed()
    {
        using var database = new TempSqliteDatabase("audit-key-removed");
        Guid firstSigned;

        await using (var signing = await StartAsync(database, withKey: true))
        {
            await WriteAsync(signing, "agent:one");
            await WriteAsync(signing, "agent:two");

            firstSigned = (await ReadEntriesAsync(signing))
                .Single(static entry => string.Equals(entry.GetProperty("entity").GetString(), "agent:one", StringComparison.Ordinal))
                .GetProperty("id").GetGuid();
        }

        // The deployment lost the key: no value, no id. It is an operations failure and the answer says so.
        await using var restarted = await StartAsync(database, withKey: false);

        var verification = await VerifyAsync(restarted);

        verification.GetProperty("status").GetString().ShouldBe("UnknownKey");
        verification.GetProperty("firstFailingEntryId").GetGuid().ShouldBe(firstSigned);
    }

    [Fact]
    public async Task Turning_the_key_off_still_writes_the_entry_and_warns_once_and_the_walk_reports_a_downgrade()
    {
        using var database = new TempSqliteDatabase("audit-key-off");

        await using (var signing = await StartAsync(database, withKey: true))
        {
            await WriteAsync(signing, "agent:signed");
        }

        // The value stays in configuration (so the signed entry can still be checked); only the id that
        // selects the signing key is gone.
        await using var restarted = await StartAsync(database, withKey: true, activeKey: false);

        await WriteAsync(restarted, "agent:unsigned-one");
        await WriteAsync(restarted, "agent:unsigned-two");

        var entries = await ReadEntriesAsync(restarted);

        entries.Count.ShouldBe(3, "an audit entry is never lost because the key was switched off");

        var warnings = restarted.Logs.Entries
            .Where(static entry => entry.StartsWith("Warning ", StringComparison.Ordinal)
                && entry.Contains("was signed with key", StringComparison.Ordinal))
            .ToList();

        warnings.Count.ShouldBe(1);
        warnings[0].ShouldContain(KeyId);
        warnings[0].ShouldContain("Tracon:Audit:ChainKeyId");
        warnings[0].ShouldNotContain(KeyValue);

        var verification = await VerifyAsync(restarted);
        var firstUnsigned = entries.Single(static entry => string.Equals(entry.GetProperty("entity").GetString(), "agent:unsigned-one", StringComparison.Ordinal));

        verification.GetProperty("status").GetString().ShouldBe("Broken");
        verification.GetProperty("firstFailingEntryId").GetGuid().ShouldBe(firstUnsigned.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_rotated_chain_verifies_when_both_keys_are_in_configuration()
    {
        using var database = new TempSqliteDatabase("audit-key-rotation");
        var secondValue = Convert.ToBase64String(Enumerable.Range(101, 32).Select(static i => (byte)i).ToArray());

        await using (var first = await StartAsync(database, withKey: true))
        {
            await WriteAsync(first, "agent:old-key");
        }

        var rotated = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Audit:ChainKeyId"] = "2026-11",
            ["Tracon:AuditChainKeys:2026-11"] = secondValue,
            [$"Tracon:AuditChainKeys:{KeyId}"] = KeyValue,
        };

        await using (var second = await StartAsync(database, rotated))
        {
            await WriteAsync(second, "agent:new-key");

            var verification = await VerifyAsync(second);

            verification.GetProperty("status").GetString().ShouldBe("Valid");
            verification.GetProperty("entriesChecked").GetInt32().ShouldBe(2);
        }

        // The old value is dropped from configuration: the entry it signed can no longer be checked.
        var oldDropped = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Audit:ChainKeyId"] = "2026-11",
            ["Tracon:AuditChainKeys:2026-11"] = secondValue,
        };

        await using var third = await StartAsync(database, oldDropped);

        (await VerifyAsync(third)).GetProperty("status").GetString().ShouldBe("UnknownKey");
    }

    private static Task<KeyedHost> StartAsync(TempSqliteDatabase database, bool withKey, bool activeKey = true)
        => StartAsync(
            database,
            !withKey
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : activeKey
                    ? new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["Tracon:Audit:ChainKeyId"] = KeyId,
                        [$"Tracon:AuditChainKeys:{KeyId}"] = KeyValue,
                    }
                    : new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [$"Tracon:AuditChainKeys:{KeyId}"] = KeyValue,
                    });

    private static async Task<KeyedHost> StartAsync(TempSqliteDatabase database, Dictionary<string, string?> configuration)
    {
        var logs = new RecordingLoggerProvider();
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Configuration.AddInMemoryCollection(configuration);

        builder.AddTracon().UseSqlite(database.ConnectionString);

        var app = builder.Build();

        app.MapTracon();

        await app.StartAsync();

        return new KeyedHost(app, logs);
    }

    private static async Task WriteAsync(KeyedHost host, string entity)
        => await host.Services.GetRequiredService<IAuditLog>().WriteAsync(
            new AuditEntry
            {
                Id = TraconId.NewId(),
                TenantId = Tenant,
                Actor = "user-1",
                Action = "agent.update",
                Entity = entity,
                Before = null,
                After = """{"version":2}""",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

    /// <summary>The key id of an audit entry as the API returns it: absent and <c>null</c> both mean no key.</summary>
    private static string? KeyOf(System.Text.Json.JsonElement entry)
        => entry.TryGetProperty("keyId", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<System.Text.Json.JsonElement> VerifyAsync(KeyedHost host)
    {
        using var response = await host.Client.GetAsync(Verify, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static async Task<List<System.Text.Json.JsonElement>> ReadEntriesAsync(KeyedHost host)
    {
        using var response = await host.Client.GetAsync(AuditList, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return [.. (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()];
    }

    /// <summary>A running host with its client and the log lines it wrote.</summary>
    private sealed class KeyedHost(WebApplication app, RecordingLoggerProvider logs) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public RecordingLoggerProvider Logs { get; } = logs;

        public IServiceProvider Services => app.Services;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
