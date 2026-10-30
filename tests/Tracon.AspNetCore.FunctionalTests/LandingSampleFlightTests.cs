using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Tracon.Testing.TraconTestHost and this project's own host
// share a name, so the package is imported one type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The sample flight that the tracon.dev landing page prints, run against the
/// real product.
/// </summary>
/// <remarks>
/// <para>
/// The landing page tells the story of ONE request: a support agent is asked to
/// cancel order ORD-7, the cancel tool stops for approval, an operator of
/// tenant <c>acme</c> declines it, and the audit chain of <c>acme</c> verifies.
/// The page claims real event names, statuses and audit actions, and it quotes
/// the startup message of <c>RequireProductionProfile()</c>. It reads those
/// values from <c>docs-site/src/components/landing/flight.events.json</c>.
/// </para>
/// <para>
/// This test exists so that the page cannot lie. It runs the same flight end to
/// end and compares what the product produced with that file. If the product
/// changes, this test fails instead of the page going stale. The failure message
/// carries the recorded values in the file's own shape: copy them into the file
/// and update the page text that tells the story.
/// </para>
/// <para>
/// The host differs from the one-line <c>UseTenancy(o =&gt; o.Enabled = true)</c>
/// in one respect: a caller that is not an API key resolves its tenant only from
/// a claim (<see cref="TraconTenancyOptions.ClaimType"/>) or a header, and
/// neither is on by default. Without one of them the operator of <c>acme</c>
/// resolves to the default tenant, the key is minted there, and the run never
/// reaches <c>acme</c>. The flight uses a claim, the production path.
/// </para>
/// <para>
/// Only what the file holds is compared. Ids, key prefixes and timestamps change
/// on every run; the test output lists them next to the exact audit texts.
/// </para>
/// </remarks>
public sealed class LandingSampleFlightTests
{
    private const string Tenant = "acme";
    private const string SessionId = "s-7f3a";
    private const string IdempotencyKey = "ord-7-cancel-1";
    private const string CustomerMessage = "Please cancel ORD-7. I ordered it twice.";

    /// <summary>
    /// The one person of tenant <c>acme</c> in the story. The page hashes every
    /// audit entry with this actor, so it is also the admin who registers the
    /// tenant and mints the key.
    /// </summary>
    private const string Operator = "ops@acme.test";
    private const string TenantClaim = "tenant_id";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task The_landing_page_sample_flight_is_what_the_product_does()
    {
        var recorded = new JsonObject();
        var notes = new StringBuilder();

        await using (var host = await StartFlightHostAsync())
        {
            await FlyAsync(host, recorded, notes);
        }

        var profileMessage = await ReadProductionProfileFailureAsync();
        var firstLine = profileMessage.Split('\n')[0].TrimEnd('\r');

        recorded["productionProfileOpen"] = CountOpenDecisions(profileMessage);
        recorded["productionProfileFirstLine"] = firstLine;
        recorded["canonicalSample"] = HashCanonicalProbe();

        var recordedText = recorded.ToJsonString(Indented);

        notes.Insert(0, $"Recorded flight:{Environment.NewLine}{recordedText}{Environment.NewLine}");
        TestContext.Current.TestOutputHelper?.WriteLine(notes.ToString());

        File.Exists(FlightFile).ShouldBeTrue(
            $"'{FlightFile}' is missing. The recorded flight is:{Environment.NewLine}{recordedText}");

        var expected = JsonNode.Parse(await File.ReadAllTextAsync(FlightFile, TestContext.Current.CancellationToken));

        JsonNode.DeepEquals(expected, recorded).ShouldBeTrue(
            $"The landing page's sample flight no longer matches the product. Update '{FlightFile}' " +
            $"and the page text around it from what the product did:{Environment.NewLine}{recordedText}" +
            $"{Environment.NewLine}{notes}");
    }

    private static async Task FlyAsync(TraconTestHost host, JsonObject recorded, StringBuilder notes)
    {
        // Step 2: the tenant record.
        using (var request = AsUser(HttpMethod.Put, "/tracon/api/tenants/acme", Operator, new { displayName = "Acme" }))
        using (var saved = await host.Client.SendAsync(request))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        // Step 3: a key bound to acme, allowed to start and read runs.
        string apiKey;

        using (var request = AsUser(
                   HttpMethod.Post,
                   "/tracon/api/api-keys",
                   Operator,
                   new { name = "support-desk", scopes = new[] { "RunsRead", "RunsWrite" } }))
        using (var created = await host.Client.SendAsync(request))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

            var result = (await created.Content.ReadFromJsonAsync<ApiKeyCreationResult>()).ShouldNotBeNull();

            result.Record.TenantId.ShouldBe(Tenant);
            apiKey = result.PlaintextKey;
        }

        // Step 4: the customer's request, queued. The same request sent twice
        // is one run: the second answer is the stored first one.
        Guid firstRunId;
        string location;

        using (var request = RunRequest(apiKey))
        using (var accepted = await host.Client.SendAsync(request))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
            accepted.Headers.Location.ShouldNotBeNull();

            location = accepted.Headers.Location.OriginalString;
            firstRunId = (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
        }

        using (var request = RunRequest(apiKey))
        using (var replayed = await host.Client.SendAsync(request))
        {
            replayed.StatusCode.ShouldBe(HttpStatusCode.Accepted, await replayed.Content.ReadAsStringAsync());
            replayed.Headers.GetValues("Idempotency-Replayed").ShouldContain("true", StringComparer.Ordinal);
            replayed.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(location);
            (await TraconTestHost.ReadJsonAsync(replayed)).GetProperty("runId").GetGuid().ShouldBe(firstRunId);
        }

        // Step 5: the run stops on the tool that needs approval.
        var firstRun = await WaitForRunAsync(
            host,
            () => WithKey(HttpMethod.Get, $"/tracon/api/runs/{firstRunId}", apiKey),
            static run => run.GetProperty("status").GetString() is "AwaitingApproval",
            "the first run to stop for approval");

        // The tenant came from the key: the request carried no tenant header or claim.
        firstRun.GetProperty("tenantId").GetString().ShouldBe(Tenant);
        firstRun.GetProperty("sessionId").GetString().ShouldBe(SessionId);

        var names = new JsonArray();
        var tools = new JsonArray();

        foreach (var (name, toolName) in await ReadEventsAsync(
                     host,
                     WithKey(HttpMethod.Get, $"/tracon/api/runs/{firstRunId}/events", apiKey),
                     "First run events:",
                     notes))
        {
            names.Add(name);
            tools.Add(toolName);
        }

        recorded["r1"] = names;
        recorded["r1Tools"] = tools;

        // Step 6: an operator of acme declines the cancellation.
        Guid approvalId;

        using (var request = AsUser(HttpMethod.Get, "/tracon/api/approvals/pending", Operator))
        using (var pending = await host.Client.SendAsync(request))
        {
            pending.StatusCode.ShouldBe(HttpStatusCode.OK);

            var approval = (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem();

            approval.GetProperty("toolName").GetString().ShouldBe("cancel_order");
            approval.GetProperty("runId").GetGuid().ShouldBe(firstRunId);
            approvalId = approval.GetProperty("id").GetGuid();
        }

        using (var request = AsUser(
                   HttpMethod.Post,
                   $"/tracon/api/approvals/{approvalId}/decide",
                   Operator,
                   new { approved = false }))
        using (var decided = await host.Client.SendAsync(request))
        {
            decided.StatusCode.ShouldBe(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync());
        }

        // Step 7: the decision continues the session in a NEW run; the first one stays as it was.
        var sessionRuns = await WaitForRunAsync(
            host,
            () => AsUser(HttpMethod.Get, $"/tracon/api/runs?sessionId={SessionId}", Operator),
            run => run.EnumerateArray().Any(entry =>
                entry.GetProperty("id").GetGuid() != firstRunId
                && entry.GetProperty("status").GetString() is "Completed" or "Failed" or "Canceled"),
            "a second run in the session to settle");

        var runs = sessionRuns.EnumerateArray().ToList();

        runs.Count.ShouldBe(2, "the replayed request must not have started a run of its own.");

        var secondRun = runs.Single(run => run.GetProperty("id").GetGuid() != firstRunId);

        using (var request = AsUser(HttpMethod.Get, $"/tracon/api/runs/{firstRunId}", Operator))
        using (var reread = await host.Client.SendAsync(request))
        {
            recorded["r1Status"] = (await TraconTestHost.ReadJsonAsync(reread)).GetProperty("status").GetString();
        }

        recorded["r2Status"] = secondRun.GetProperty("status").GetString();
        recorded["sameSession"] = string.Equals(
            secondRun.GetProperty("sessionId").GetString(),
            firstRun.GetProperty("sessionId").GetString(),
            StringComparison.Ordinal);

        // Not compared, only reported: what the continuation run said.
        await ReadEventsAsync(
            host,
            AsUser(HttpMethod.Get, $"/tracon/api/runs/{secondRun.GetProperty("id").GetGuid()}/events", Operator),
            "Second run events:",
            notes);

        // Step 8: the audit chain of acme, oldest first, and its verification.
        await RecordAuditAsync(host, recorded, notes);
    }

    /// <summary>Reads a settled run's SSE stream and notes every frame.</summary>
    private static async Task<List<(string? Name, string? ToolName)>> ReadEventsAsync(
        TraconTestHost host,
        HttpRequestMessage request,
        string heading,
        StringBuilder notes)
    {
        using (request)
        {
            using var events = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            events.StatusCode.ShouldBe(HttpStatusCode.OK);

            await using var stream = await events.Content.ReadAsStreamAsync();

            var frames = new List<(string? Name, string? ToolName)>();

            notes.AppendLine(heading);

            foreach (var frame in await SseReader.ReadAllAsync(stream))
            {
                using var data = JsonDocument.Parse(frame.Data);
                var toolName = ReadString(data.RootElement, "toolName");

                frames.Add((frame.Event, toolName));
                notes.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  {frame.Event} toolName={toolName ?? "null"} text={ReadString(data.RootElement, "text") ?? "null"} payload={ReadString(data.RootElement, "payload") ?? "null"}");
            }

            return frames;
        }
    }

    private static async Task RecordAuditAsync(TraconTestHost host, JsonObject recorded, StringBuilder notes)
    {
        using (var request = AsUser(HttpMethod.Get, "/tracon/api/audit?limit=500", Operator))
        using (var audit = await host.Client.SendAsync(request))
        {
            audit.StatusCode.ShouldBe(HttpStatusCode.OK);

            var chain = ChainOrder((await TraconTestHost.ReadJsonAsync(audit)).EnumerateArray().ToList());
            var actions = new JsonArray();

            notes.AppendLine("Audit chain of acme, oldest first:");

            foreach (var entry in chain)
            {
                entry.GetProperty("tenantId").GetString().ShouldBe(Tenant);
                actions.Add(entry.GetProperty("action").GetString());

                notes.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  action={entry.GetProperty("action").GetString()} entity={entry.GetProperty("entity").GetString()} actor={ReadString(entry, "actor") ?? "null"} createdAt={entry.GetProperty("createdAt").GetRawText()}");
                notes.AppendLine(CultureInfo.InvariantCulture, $"    before={ReadString(entry, "before") ?? "null"}");
                notes.AppendLine(CultureInfo.InvariantCulture, $"    after={ReadString(entry, "after") ?? "null"}");
            }

            recorded["auditActions"] = actions;
        }

        using (var request = AsUser(HttpMethod.Get, "/tracon/api/audit/verify", Operator))
        using (var verify = await host.Client.SendAsync(request))
        {
            verify.StatusCode.ShouldBe(HttpStatusCode.OK);

            var result = await TraconTestHost.ReadJsonAsync(verify);

            recorded["verify"] = new JsonObject
            {
                ["status"] = result.GetProperty("status").ToString(),
                ["entriesChecked"] = result.GetProperty("entriesChecked").GetInt32(),
            };
        }
    }

    /// <summary>
    /// Orders audit entries by their hash links, oldest first — the order the
    /// chain was written in, which the listing's newest-first order does not keep
    /// for two entries written in the same tick.
    /// </summary>
    private static List<JsonElement> ChainOrder(List<JsonElement> entries)
    {
        var byPrevious = entries.ToDictionary(
            static entry => ReadString(entry, "previousHash") ?? string.Empty,
            StringComparer.Ordinal);

        var ordered = new List<JsonElement>(entries.Count);
        var link = string.Empty;

        while (byPrevious.TryGetValue(link, out var next))
        {
            ordered.Add(next);
            link = next.GetProperty("hash").GetString() ?? throw new InvalidOperationException("An audit entry carries no hash.");
        }

        ordered.Count.ShouldBe(entries.Count, "the audit entries of acme do not form one unbroken chain.");

        return ordered;
    }

    /// <summary>Step 9: a host that declares the production profile and answers none of it.</summary>
    private static async Task<string> ReadProductionProfileFailureAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddTracon().RequireProductionProfile();

        await using var app = builder.Build();
        app.MapTracon();

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () => await app.StartAsync());

        return failure.Message;
    }

    /// <summary>
    /// The product's own hash of one fixed audit entry. The page prints hashes it
    /// computes at build time with its own copy of the canonical form, and it
    /// hashes this same entry first; the two must agree, or the page would print
    /// hashes the product cannot produce. The entry has a previous hash, quotes and
    /// the escaped '+' the JSON writer stores, the parts a copy most easily gets wrong.
    /// </summary>
    private static string HashCanonicalProbe()
        => AuditChainHasher.ComputeHash(
            previousHash: "548f50472d6b293459b1f66d8dc2c347d2506aa7b5db9fbe859844efcb65d746",
            tenantId: Tenant,
            actor: Operator,
            action: "tenant.create",
            entity: "tenant:acme",
            before: null,
            after: """{"id":"01a0d5a1-dde5-7c5e-bd60-3fc0cbcbbe46","slug":"acme","displayName":"Acme","createdAt":"2026-09-24T22:55:50.24598\u002B00:00"}""",
            createdAt: DateTimeOffset.Parse("2026-09-24T22:55:50.484878Z", CultureInfo.InvariantCulture));

    /// <summary>Counts the decisions the startup message lists, one indented risk name each.</summary>
    private static int CountOpenDecisions(string message)
    {
        var risks = Enum.GetNames<TraconProductionRisk>();

        return message
            .Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .Count(line => risks.Any(risk => string.Equals(line, $"  {risk}", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Step 1: the host. <c>AddTracon()</c> already turns on queued runs and their
    /// worker; the shorter poll interval only keeps the test fast.
    /// </summary>
    private static Task<TraconTestHost> StartFlightHostAsync()
        => TraconTestHost.StartAsync(
            static builder =>
            {
                builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.ClaimType = TenantClaim;
                })
                .AddModelProvider(new FakeModelProvider()
                    .CallsTool("get_order_status", new { orderId = "ORD-7" })
                    .CallsTool("cancel_order", new { orderId = "ORD-7" })
                    .RespondsWith("ORD-7 has already shipped, and the cancellation was not approved, so the order stands."))
                .AddToolsFrom(typeof(LandingOrderTools))
                .AddAgent(new AgentDefinition
                {
                    Name = "support",
                    Instructions = "You help customers with their orders.",
                    Model = new ModelBinding { Provider = "fake", Model = "fake-model" },
                    ToolNames = ["get_order_status", "cancel_order"],
                });
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: static services =>
            {
                services
                    .AddAuthentication(SignInHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, SignInHandler>(SignInHandler.SchemeName, configureOptions: null);

            });

    private static async Task<JsonElement> WaitForRunAsync(
        TraconTestHost host,
        Func<HttpRequestMessage> request,
        Func<JsonElement, bool> accept,
        string description)
        => await WaitUntil.ValueAsync(
            async () =>
            {
                using var message = request();
                using var response = await host.Client.SendAsync(message);

                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

                return await TraconTestHost.ReadJsonAsync(response);
            },
            accept,
            description);

    private static HttpRequestMessage RunRequest(string apiKey)
    {
        var request = WithKey(
            HttpMethod.Post,
            "/tracon/api/agents/support/run",
            apiKey,
            new { message = CustomerMessage, sessionId = SessionId });

        request.Headers.Add("Prefer", "respond-async");
        request.Headers.Add("Idempotency-Key", IdempotencyKey);

        return request;
    }

    private static HttpRequestMessage AsUser(HttpMethod method, string path, string user, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };

        request.Headers.Add(SignInHandler.UserHeader, user);

        return request;
    }

    private static HttpRequestMessage WithKey(HttpMethod method, string path, string apiKey, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return request;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FlightFile { get; } = Path.Combine(
        FindRepositoryRoot(), "docs-site", "src", "components", "landing", "flight.events.json");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The host's own sign-in, standing in for the application's real one (a JWT
    /// bearer, a cookie). A request that names a user is that user of tenant
    /// <c>acme</c>; any other request is anonymous, so the API key request keeps
    /// the key as its only identity.
    /// </summary>
    private sealed class SignInHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "LandingSignIn";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[UserHeader].ToString() is not { Length: > 0 } user)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, user), new Claim(TenantClaim, Tenant)],
                SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

/// <summary>The two order tools of the landing page's support agent.</summary>
internal static class LandingOrderTools
{
    /// <summary>Returns an order's shipping status.</summary>
    /// <param name="orderId">The order id.</param>
    /// <returns>The status text.</returns>
    [TraconTool("get_order_status", "Returns an order's shipping status.")]
    public static string GetOrderStatus([Description("The order id.")] string orderId) => $"Order {orderId} has shipped.";

    /// <summary>Cancels an order. Needs a human decision first.</summary>
    /// <param name="orderId">The order id.</param>
    /// <returns>The result text.</returns>
    [TraconTool("cancel_order", "Cancels an order.", RequiresApproval = true, Effect = ToolEffect.Destructive)]
    public static string CancelOrder([Description("The order id.")] string orderId) => $"Order {orderId} is canceled.";
}
