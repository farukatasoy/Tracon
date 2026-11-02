using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The plaintext of an API key the installation issues is in the response that issues it and in nothing else: not in
/// the list, not in the answer to a revocation, not in the answer to a retried creation, not in the audit trail, not in a
/// log line, a span, a metric tag, a refusal, or a byte of the database.
/// </summary>
/// <remarks>
/// <para>
/// The key is used before it is revoked (a read, a run, a call its scope forbids) and after (the refusal a revoked key
/// gets), because the places a presented credential can fall into are the places the authentication code and the
/// last-used stamp touch, not the places the creation touches. The creation is also retried with the same
/// <c>Idempotency-Key</c>, since a replayed creation is the one shape in which a stored response could hold the
/// plaintext.
/// </para>
/// <para>
/// What a key is looked up by, and what the database keeps, is its hash. The search is for the value and for a slice from
/// the middle of it, so a truncated copy is found too.
/// </para>
/// </remarks>
public sealed class ApiKeyPlaintextOnceTests
{
    [Fact]
    public async Task An_issued_key_is_in_its_creation_response_and_nowhere_else()
    {
        using var database = new TempSqliteDatabase("api-key-once");
        using var probe = new LeakProbe();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .AddAgent(TestData.Definition()),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true);

        var creations = new List<string>();
        var elsewhere = new StringBuilder();

        // Three keys: one that is used and then revoked, and two issued by the same retried request.
        var used = await IssueAsync(host, "used", ["RunsRead", "RunsWrite", "AgentsRead", "AuditRead"], idempotencyKey: null, creations);
        var retriedFirst = await IssueAsync(host, "retried", ["RunsRead"], idempotencyKey: "retry-1", creations);
        var retriedSecond = await IssueAsync(host, "retried", ["RunsRead"], idempotencyKey: "retry-1", creations);

        var issued = new[] { used, retriedFirst, retriedSecond }.Select(static key => key.Plaintext).Distinct(StringComparer.Ordinal).ToList();

        issued.Count.ShouldBeGreaterThanOrEqualTo(1);
        issued.ShouldAllBe(static plaintext => plaintext.Length > 16);

        // The key at work.
        using (var read = Authorized(HttpMethod.Get, "/tracon/api/agents", used.Plaintext))
        {
            (await SecretCanarySweepTests.SendAsync(host, read, elsewhere)).ShouldBe(HttpStatusCode.OK);
        }

        using (var run = Authorized(HttpMethod.Post, "/tracon/api/agents/kod-agent/run", used.Plaintext))
        {
            run.Content = JsonContent.Create(new AgentRunRequest { Message = "hello" });
            (await SecretCanarySweepTests.SendAsync(host, run, elsewhere, readEventStreams: true)).ShouldBe(HttpStatusCode.OK);
        }

        using (var forbidden = Authorized(HttpMethod.Post, "/tracon/api/api-keys", used.Plaintext))
        {
            forbidden.Content = JsonContent.Create(new { name = "no", scopes = new[] { "RunsRead" } });
            (await SecretCanarySweepTests.SendAsync(host, forbidden, elsewhere)).ShouldBe(HttpStatusCode.Forbidden);
        }

        // Every read the key may make, so the response of an operation nobody thought of is searched too.
        var reads = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not null)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.Ordinal) == true)
            .Select(static endpoint => EndpointAuthorizationMatrixTests.Fill(endpoint.RoutePattern.RawText!, "default"))
            .ToList();

        var answered = 0;

        foreach (var url in reads)
        {
            using var request = Authorized(HttpMethod.Get, url, used.Plaintext);

            if (await SecretCanarySweepTests.SendAsync(host, request, elsewhere) == HttpStatusCode.OK)
            {
                answered++;
            }
        }

        answered.ShouldBeGreaterThan(5, "the key reached too few operations for an empty search to mean anything");

        // The keys, revoked, listed, audited.
        foreach (var key in new[] { used, retriedFirst, retriedSecond }.DistinctBy(static key => key.Id))
        {
            using var revoke = Authorized(HttpMethod.Delete, $"/tracon/api/api-keys/{key.Id}", bearer: null);
            await SecretCanarySweepTests.SendAsync(host, revoke, elsewhere);
        }

        // The revoked key is presented once more.
        using (var refused = Authorized(HttpMethod.Get, "/tracon/api/agents", used.Plaintext))
        {
            (await SecretCanarySweepTests.SendAsync(host, refused, elsewhere)).ShouldBe(HttpStatusCode.Unauthorized);
        }

        foreach (var path in (string[])["/tracon/api/api-keys", "/tracon/api/audit?limit=500", "/tracon/api/audit/verify", "/tracon/api/diagnostics", "/tracon/api/meta"])
        {
            using var request = Authorized(HttpMethod.Get, path, bearer: null);
            await SecretCanarySweepTests.SendAsync(host, request, elsewhere);
        }

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { TenantId = "default", Limit = 500 }, TestContext.Current.CancellationToken);

        elsewhere.AppendLine(JsonSerializer.Serialize(entries));

        entries.Count.ShouldBeGreaterThanOrEqualTo(3, "the audit trail holds no key event, so its search proves nothing");

        var databaseBytes = database.ReadAllBytes();

        // The database holds the keys' hashes and their names, so the scan reads a file that has the keys in it.
        ContainsBytes(databaseBytes, Encoding.UTF8.GetBytes("retried")).ShouldBeTrue("the keys are not in the database, so the scan reads the wrong place");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses other than the creation"] = elsewhere.ToString(),
            ["log lines"] = host.Logs.AllText,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        var found = new List<string>();

        foreach (var plaintext in issued)
        {
            // The value, and a slice of its middle.
            foreach (var needle in (string[])[plaintext, plaintext.Substring(plaintext.Length / 4, 16)])
            {
                foreach (var (output, text) in outputs)
                {
                    if (text.Contains(needle, StringComparison.Ordinal))
                    {
                        found.Add($"a key's plaintext is in the {output}");
                    }
                }

                if (ContainsBytes(databaseBytes, Encoding.UTF8.GetBytes(needle)) || ContainsBytes(databaseBytes, Encoding.Unicode.GetBytes(needle)))
                {
                    found.Add("a key's plaintext is in the database");
                }
            }
        }

        found.Distinct(StringComparer.Ordinal).ShouldBeEmpty();

        // The plaintext was in the one place it belongs.
        foreach (var key in new[] { used, retriedFirst, retriedSecond })
        {
            creations.ShouldContain(creation => creation.Contains(key.Plaintext, StringComparison.Ordinal));
        }
    }

    private static async Task<IssuedKey> IssueAsync(
        TraconTestHost host,
        string name,
        string[] scopes,
        string? idempotencyKey,
        List<string> creations)
    {
        using var request = Authorized(HttpMethod.Post, "/tracon/api/api-keys", bearer: null);
        request.Content = JsonContent.Create(new { name, scopes });

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        creations.Add(body);

        var json = JsonDocument.Parse(body).RootElement;

        return new IssuedKey(
            json.GetProperty("plaintextKey").GetString()!,
            json.GetProperty("record").GetProperty("id").GetGuid());
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string? bearer)
        => SecretCanarySweepTests.Authorized(method, url, bearer);

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;

    private sealed record IssuedKey(string Plaintext, Guid Id);
}
