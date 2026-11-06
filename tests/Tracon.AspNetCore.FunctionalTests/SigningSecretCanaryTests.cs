using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The two secrets that sign and verify webhooks (the one an outbound subscription signs its deliveries with, and the one an
/// inbound trigger verifies its callers with) are used and reach no output: not a response, a record, an audit entry, a
/// log line, a span, a metric tag or a byte of the database.
/// </summary>
/// <remarks>
/// <para>
/// The inbound trigger is the surface that answers to strangers, so it is driven the way a stranger drives it: signed
/// correctly, signed with another secret, tampered, stale, replayed. The outbound side is driven with a real receiver that
/// checks the signature it is sent against the secret, so the secret is known to have been resolved and used. A search
/// that finds nothing proves nothing unless the secret did its work.
/// </para>
/// <para>
/// Both values are searched by their stem, so a truncated or masked copy is found too.
/// </para>
/// </remarks>
public sealed class SigningSecretCanaryTests
{
    private const string TriggerStem = "canary-trigger-signing-secret";
    private const string TriggerSecret = TriggerStem + "-4e7a19c0b2d583f6";
    private const string WebhookStem = "canary-webhook-signing-secret";
    private const string WebhookSecret = WebhookStem + "-a05d3f81c7e92b64";
    private const string TriggerKey = "Tracon:TriggerSecrets:Slack";
    private const string WebhookKey = "Tracon:WebhookSecrets:Signer";
    private const string Body = """{"event":{"text":"hello"}}""";

    [Fact]
    public async Task The_signing_secrets_do_their_work_and_reach_no_output()
    {
        using var database = new TempSqliteDatabase("signing-secret-canary");
        using var probe = new LeakProbe();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .AddAgent(TestData.Definition()),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: static services =>
            {
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection([new(TriggerKey, TriggerSecret), new(WebhookKey, WebhookSecret)])
                    .Build());
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconWebhookOptions>(static options => options.AllowInsecureHttp = true);
                services.Configure<TraconSchedulingOptions>(static options => options.PollInterval = TimeSpan.FromMilliseconds(50));
            });

        var seen = new StringBuilder();

        // A receiver that checks the signature of what it is sent against the secret the subscription names.
        var receiverBuilder = WebApplication.CreateSlimBuilder();
        receiverBuilder.Logging.ClearProviders();
        await using var receiver = receiverBuilder.Build();
        receiver.Urls.Add("http://127.0.0.1:0");

        var verified = 0;
        var forged = 0;

        receiver.MapPost("/hook", async (HttpRequest request) =>
        {
            using var reader = new StreamReader(request.Body);
            var text = await reader.ReadToEndAsync();
            var stamp = long.Parse(request.Headers[WebhookSigner.TimestampHeader].ToString(), CultureInfo.InvariantCulture);
            var expected = WebhookSigner.Sign(text, DateTimeOffset.FromUnixTimeSeconds(stamp), WebhookSecret);

            if (string.Equals(request.Headers[WebhookSigner.SignatureHeader], expected, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref verified);
            }
            else
            {
                Interlocked.Increment(ref forged);
            }

            return Results.Ok();
        });
        await receiver.StartAsync(TestContext.Current.CancellationToken);

        var address = receiver.Urls.First();

        // The operator pastes a value where a key name belongs, on both surfaces.
        (await SendAsync(host, HttpMethod.Put, "/tracon/api/triggers/pasted", new { targetKind = "agent", targetName = "kod-agent", signingSecretConfigurationName = TriggerSecret }, seen))
            .ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(host, HttpMethod.Put, "/tracon/api/webhooks/pasted", new { url = $"{address}/hook", events = new[] { "run.completed" }, secretConfigurationKey = WebhookSecret }, seen))
            .ShouldBe(HttpStatusCode.BadRequest);

        (await SendAsync(
            host,
            HttpMethod.Put,
            "/tracon/api/triggers/slack",
            new { targetKind = "agent", targetName = "kod-agent", signingSecretConfigurationName = TriggerKey, enabled = true },
            seen)).ShouldBe(HttpStatusCode.OK);
        (await SendAsync(
            host,
            HttpMethod.Put,
            "/tracon/api/webhooks/w1",
            new { url = $"{address}/hook", events = new[] { "run.completed" }, secretConfigurationKey = WebhookKey, enabled = true },
            seen)).ShouldBe(HttpStatusCode.OK);

        // A stranger drives the inbound side: correctly signed, signed with another secret, tampered, stale, replayed.
        var now = DateTimeOffset.UtcNow;
        var accepted = await TriggerAsync(host, Body, now, WebhookSigner.Sign(Body, now, TriggerSecret), seen);
        var replayed = await TriggerAsync(host, Body, now, WebhookSigner.Sign(Body, now, TriggerSecret), seen);
        var anotherSecret = await TriggerAsync(host, Body, now, WebhookSigner.Sign(Body, now, "another-secret-entirely"), seen);
        var tampered = await TriggerAsync(host, """{"event":{"text":"tampered"}}""", now, WebhookSigner.Sign(Body, now, TriggerSecret), seen);
        var stale = await TriggerAsync(host, Body, now.AddMinutes(-10), WebhookSigner.Sign(Body, now.AddMinutes(-10), TriggerSecret), seen);
        var unsigned = await TriggerAsync(host, Body, now, signature: null, seen);

        accepted.ShouldBe(HttpStatusCode.Accepted, "the trigger did not accept a correctly signed request, so its secret was not used");
        replayed.ShouldBe(HttpStatusCode.Conflict);
        anotherSecret.ShouldBe(HttpStatusCode.Unauthorized);
        tampered.ShouldBe(HttpStatusCode.Unauthorized);
        stale.ShouldBe(HttpStatusCode.Unauthorized);
        unsigned.ShouldBe(HttpStatusCode.Unauthorized);

        (await SendAsync(host, HttpMethod.Post, "/tracon/api/webhooks/w1/test", new { }, seen)).ShouldBe(HttpStatusCode.OK);

        // The deliveries settle before the search looks at what they left.
        await WaitUntil.ValueAsync(
            async () => Volatile.Read(ref verified) + Volatile.Read(ref forged),
            static delivered => delivered >= 2,
            "the run's event and the test delivery to arrive");

        Volatile.Read(ref verified).ShouldBeGreaterThanOrEqualTo(2, "the receiver did not see a signature made with the subscription's secret");
        Volatile.Read(ref forged).ShouldBe(0);

        var reads = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not null)
            .Where(static endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET", StringComparer.Ordinal) == true)
            .Select(static endpoint => EndpointAuthorizationMatrixTests.Fill(endpoint.RoutePattern.RawText!, "default"))
            .Concat(["/tracon/api/webhooks/w1", "/tracon/api/webhooks/w1/deliveries", "/tracon/api/triggers/slack"])
            .ToList();

        var answered = 0;

        foreach (var url in reads)
        {
            if (await SendAsync(host, HttpMethod.Get, url, null, seen) == HttpStatusCode.OK)
            {
                answered++;
            }
        }

        answered.ShouldBeGreaterThan(40, "the sweep reached too few operations for an empty search to mean anything");

        var entries = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { TenantId = "default", Limit = 500 }, TestContext.Current.CancellationToken);

        seen.AppendLine(JsonSerializer.Serialize(entries));
        entries.Count.ShouldBeGreaterThanOrEqualTo(2, "the audit trail holds no event for the two subscriptions");

        var bytes = database.ReadAllBytes();

        // The names of the two keys are in the file; the values are not.
        ContainsBytes(bytes, Encoding.UTF8.GetBytes(TriggerKey)).ShouldBeTrue("the trigger is not in the database, so the scan reads the wrong place");
        ContainsBytes(bytes, Encoding.UTF8.GetBytes(WebhookKey)).ShouldBeTrue("the subscription is not in the database, so the scan reads the wrong place");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = seen.ToString(),
            ["log lines"] = host.Logs.AllText,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        var found = new List<string>();

        foreach (var (name, stem) in (ValueTuple<string, string>[])[("trigger signing secret", TriggerStem), ("webhook signing secret", WebhookStem)])
        {
            foreach (var (output, text) in outputs)
            {
                var at = text.IndexOf(stem, StringComparison.Ordinal);

                if (at >= 0)
                {
                    var from = Math.Max(0, at - 200);

                    found.Add($"the {name} is in the {output}: ...{text.Substring(from, Math.Min(text.Length - from, 500))}...");
                }
            }

            if (ContainsBytes(bytes, Encoding.UTF8.GetBytes(stem)) || ContainsBytes(bytes, Encoding.Unicode.GetBytes(stem)))
            {
                found.Add($"the {name} is in the database");
            }
        }

        found.ShouldBeEmpty();
    }

    private static async Task<HttpStatusCode> TriggerAsync(
        TraconTestHost host,
        string body,
        DateTimeOffset timestamp,
        string? signature,
        StringBuilder seen)
    {
        using var request = SecretCanarySweepTests.Authorized(HttpMethod.Post, "/tracon/api/triggers/default/slack", bearer: null);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        if (signature is not null)
        {
            request.Headers.Add(WebhookSigner.SignatureHeader, signature);
        }

        return await SecretCanarySweepTests.SendAsync(host, request, seen);
    }

    private static async Task<HttpStatusCode> SendAsync(TraconTestHost host, HttpMethod method, string url, object? body, StringBuilder seen)
    {
        using var request = SecretCanarySweepTests.Authorized(method, url, bearer: null);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await SecretCanarySweepTests.SendAsync(host, request, seen);
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;
}
