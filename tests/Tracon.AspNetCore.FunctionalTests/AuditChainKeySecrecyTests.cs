using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The chain key VALUE is a <c>secret</c> and reaches none of the four places a
/// secret leaks to: the database, the log, the diagnostics report and an HTTP response.
/// </summary>
/// <remarks>
/// The host signs real entries first, so the key has been used, not merely loaded. Then the value is looked
/// for in its two shapes: the base64 an operator pasted and the raw bytes the HMAC runs on.
/// </remarks>
public sealed class AuditChainKeySecrecyTests
{
    private static readonly byte[] KeyBytes = Enumerable.Range(150, 32).Select(static i => (byte)i).ToArray();
    private static readonly string KeyBase64 = Convert.ToBase64String(KeyBytes);

    [Fact]
    public async Task The_key_value_is_in_no_response_no_log_line_no_diagnostics_report_and_no_database_byte()
    {
        using var database = new TempSqliteDatabase("audit-key-secrecy");
        var responses = new StringBuilder();
        string logText;
        string options;

        var logs = new RecordingLoggerProvider();
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Audit:ChainKeyId"] = "2026-10",
            ["Tracon:AuditChainKeys:2026-10"] = KeyBase64,
        });
        builder.AddTracon().UseSqlite(database.ConnectionString);

        await using (var app = builder.Build())
        {
            app.MapTracon("/tracon", static endpoints => endpoints.EnableDiagnosticsEndpoint = true);
            await app.StartAsync(TestContext.Current.CancellationToken);

            var auditLog = app.Services.GetRequiredService<IAuditLog>();

            for (var i = 0; i < 3; i++)
            {
                await auditLog.WriteAsync(
                    new AuditEntry
                    {
                        Id = TraconId.NewId(),
                        TenantId = "default",
                        Action = "agent.update",
                        Entity = $"agent:{i}",
                        After = """{"version":2}""",
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    TestContext.Current.CancellationToken);
            }

            using var client = app.GetTestClient();

            foreach (var path in new[] { "/tracon/api/audit?limit=100", "/tracon/api/audit/verify", "/tracon/api/diagnostics", "/tracon/api/meta" })
            {
                using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

                response.EnsureSuccessStatusCode();
                responses.AppendLine(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            }

            options = System.Text.Json.JsonSerializer.Serialize(app.Services.GetRequiredService<IOptions<TraconOptions>>().Value.Audit);
            logText = logs.AllText;
        }

        // Responses and the report the diagnostics endpoint builds.
        responses.ToString().ShouldNotContain(KeyBase64);
        responses.ToString().ShouldContain("2026-10", Case.Sensitive, "the key ID is a name and is shown");

        // The log, at Trace.
        logText.ShouldNotContain(KeyBase64);

        // The options object the id is bound to: the id, never the value.
        options.ShouldContain("2026-10");
        options.ShouldNotContain(KeyBase64);

        // The database file, the journal beside it and every byte in them. The id is in the file (a scan of an
        // empty or wrong file would pass for the wrong reason); the value is not.
        SqliteBytes(database.Path).ShouldContain("2026-10", Case.Sensitive, DescribeFiles(database.Path));
        SqliteBytes(database.Path).ShouldNotContain(KeyBase64);
        ContainsBytes(AllFileBytes(database.Path), KeyBytes).ShouldBeFalse("the raw key bytes are in the database file");
    }

    private static byte[] AllFileBytes(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        using var all = new MemoryStream();

        foreach (var file in Directory.EnumerateFiles(directory, name + "*"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            stream.CopyTo(all);
        }

        return all.ToArray();
    }

    private static string SqliteBytes(string path) => Encoding.Latin1.GetString(AllFileBytes(path));

    // Said when the id is missing from the file: what was on disk, so a failure is not read as a missing row by guess.
    private static string DescribeFiles(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);

        return string.Join(
            "; ",
            Directory.EnumerateFiles(directory, name + "*").Select(file =>
            {
                var bytes = File.ReadAllBytes(file);

                return $"{Path.GetFileName(file)[name.Length..]}: {bytes.Length} bytes, id {(ContainsBytes(bytes, "2026-10"u8.ToArray()) ? "present" : "absent")}";
            }));
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;
}
