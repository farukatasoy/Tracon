using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// What a caller is told on a real database server when a value is too long for the column that holds it.
/// </summary>
/// <remarks>
/// <para>
/// The scenario is the same on every server and the answer is not: SQL Server holds the identifier columns to 200
/// characters and refuses a longer value, and it must say <c>400</c>; PostgreSQL and SQLite keep text of any length and
/// take it. A provider's class says which it is. Every write below is a different endpoint that stores a value its own
/// validation does not bound, so a mapping that covered one endpoint or one table would not pass.
/// </para>
/// <para>
/// The sentinel is a value that must never come back: neither the response nor the log may carry it, nor the name of a
/// table or column, nor SQL.
/// </para>
/// </remarks>
public abstract class StoreDataFaultAcceptanceContract
{
    private const string TooLongTitle = StoreDataFaultProblemMiddleware.TextTooLongTitle;

    private static readonly string Long = "CANARY-" + new string('x', 194);

    private static Uri Api(string path) => new($"/tracon/api/{path}", UriKind.Relative);

    /// <summary>Gets a value indicating whether the server refuses a 201-character value in a 200-character column.</summary>
    protected abstract bool RefusesLongText { get; }

    /// <summary>Gets a schema no other test uses, on the class's server.</summary>
    /// <returns>The database the host connects to.</returns>
    protected abstract TwoNodeDatabase NewDatabase();

    /// <summary>Points a host's chain at a server that nothing listens on.</summary>
    /// <param name="builder">The chain of the host.</param>
    protected abstract void UseUnreachable(ITraconBuilder builder);

    /// <summary>
    /// Each write is a different endpoint and a different table, and each takes a value its own validation lets through.
    /// </summary>
    [Theory]
    [InlineData("tenant")]
    [InlineData("approval-rule")]
    [InlineData("feedback")]
    [InlineData("eval-suite")]
    [InlineData("mcp-server")]
    public async Task A_value_over_its_column_is_answered_for_what_it_is_on_every_endpoint_that_stores_it(string write)
    {
        await using var host = await StartAsync(NewDatabase());

        using var response = await SendAsync(host, write);
        var raw = await response.Content.ReadAsStringAsync();

        if (!RefusesLongText)
        {
            ((int)response.StatusCode).ShouldBeInRange(200, 299, raw);

            return;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, raw);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("title").GetString().ShouldBe(TooLongTitle);
        body.GetProperty("detail").GetString().ShouldBe(StoreDataFaultProblemMiddleware.TextTooLongDetail);

        AssertNothingLeaks(raw, host);
    }

    /// <summary>The refused write leaves the row as it was, and the next write, from the same pool, succeeds.</summary>
    [Fact]
    public async Task A_refused_write_leaves_the_earlier_value_and_the_next_write_succeeds()
    {
        await using var host = await StartAsync(NewDatabase());

        (await PutTenantAsync(host, "acme", "first")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refused = await PutTenantAsync(host, "acme", Long);
        refused.StatusCode.ShouldBe(RefusesLongText ? HttpStatusCode.BadRequest : HttpStatusCode.OK);

        if (RefusesLongText)
        {
            (await DisplayNameAsync(host, "acme")).ShouldBe("first");
        }

        using var next = await PutTenantAsync(host, "acme", "second");
        next.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DisplayNameAsync(host, "acme")).ShouldBe("second");
    }

    /// <summary>Four valid and four refused writes to one key at once: no 5xx, and the row is one of the valid values.</summary>
    [Fact]
    public async Task Valid_and_refused_writes_at_the_same_time_neither_fail_the_server_nor_corrupt_the_row()
    {
        await using var host = await StartAsync(NewDatabase());

        var valid = Enumerable.Range(0, 4).Select(index => $"valid-{index}").ToArray();
        var requests = valid
            .Select(name => (Name: name, Refused: false))
            .Concat(Enumerable.Range(0, 4).Select(index => (Name: Long + index, Refused: true)))
            .ToArray();

        var responses = await Task.WhenAll(requests.Select(async request =>
        {
            using var response = await PutTenantAsync(host, "acme", request.Name);

            return (request.Refused, response.StatusCode);
        }));

        foreach (var (refused, status) in responses)
        {
            ((int)status).ShouldBeLessThan(500);
            status.ShouldBe(refused && RefusesLongText ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        }

        var stored = await DisplayNameAsync(host, "acme");

        if (RefusesLongText)
        {
            valid.Contains(stored, StringComparer.Ordinal).ShouldBeTrue($"The row holds '{stored}'.");
        }
    }

    /// <summary>
    /// A host that raises the key limit above the column that stores it must still answer a key between the two with a
    /// 400 and not an outage: the filter lets the key through, and the store is where it is refused.
    /// </summary>
    [Fact]
    public async Task An_idempotency_key_longer_than_its_column_is_a_400_and_not_an_outage()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: NewDatabase().Use,
            configureServices: static services => services.PostConfigure<TraconIdempotencyOptions>(static options => options.MaxKeyLength = 255));

        await CreateIdempotentAgentAsync(host);

        using (var tooLong = await RunAsync(host, new string('k', 250)))
        {
            tooLong.StatusCode.ShouldBe(
                RefusesLongText ? HttpStatusCode.BadRequest : HttpStatusCode.OK,
                await tooLong.Content.ReadAsStringAsync());
        }

        // The refusal left nothing behind: a key that fits is accepted.
        using var fits = await RunAsync(host, new string('k', 200));

        fits.StatusCode.ShouldBe(HttpStatusCode.OK, await fits.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The default limit is the width of the narrowest column, so a key over it is refused for what it is on every
    /// server, and one exactly at it is stored on every server.
    /// </summary>
    [Fact]
    public async Task The_default_key_limit_is_the_same_on_every_server()
    {
        await using var host = await StartAsync(NewDatabase());

        await CreateIdempotentAgentAsync(host);

        using (var over = await RunAsync(host, new string('k', 201)))
        {
            over.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await over.Content.ReadAsStringAsync());
        }

        using var atLimit = await RunAsync(host, new string('k', 200));

        atLimit.StatusCode.ShouldBe(HttpStatusCode.OK, await atLimit.Content.ReadAsStringAsync());
    }

    /// <summary>The same limit, counted the way the server counts it: UTF-16 code units on SQL Server.</summary>
    [Fact]
    public async Task The_limit_is_counted_in_the_servers_own_unit()
    {
        await using var host = await StartAsync(NewDatabase());
        var emoji = "\U0001F600";

        // 100 emoji are 200 UTF-16 code units: they fit. A 101st, or a surrogate pair after 199 letters, does not.
        (await PutTenantAsync(host, "fits", string.Concat(Enumerable.Repeat(emoji, 100)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var over = new[]
        {
            string.Concat(Enumerable.Repeat(emoji, 101)),
            new string('a', 199) + emoji,
        };

        foreach (var name in over)
        {
            using var response = await PutTenantAsync(host, "over", name);

            response.StatusCode.ShouldBe(RefusesLongText ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        }
    }

    /// <summary>A server that cannot be reached stays the <c>503</c> that invites a retry, whatever the value.</summary>
    [Fact]
    public async Task A_server_that_cannot_be_reached_is_still_a_503()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: UseUnreachable);

        using var response = await PutTenantAsync(host, "acme", Long);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartAsync(TwoNodeDatabase database)
        => TraconTestHost.StartAsync(configureTracon: database.Use);

    private static void AssertNothingLeaks(string raw, TraconTestHost host)
    {
        foreach (var text in new[] { raw, host.Logs.AllText })
        {
            text.ShouldNotContain("CANARY-");
            text.ShouldNotContain("display_name");
            text.ShouldNotContain("tool_name");
            text.ShouldNotContain("INSERT");
            text.ShouldNotContain("UPDATE ");
            text.ShouldNotContain("nvarchar");
            text.ShouldNotContain("truncated");
        }
    }

    private static Task<HttpResponseMessage> PutTenantAsync(TraconTestHost host, string slug, string displayName)
        => host.Client.PutAsJsonAsync(Api($"tenants/{slug}"), new { displayName });

    private static async Task<string?> DisplayNameAsync(TraconTestHost host, string slug)
    {
        using var response = await host.Client.GetAsync(Api("tenants"));
        var tenants = await TraconTestHost.ReadJsonAsync(response);

        return tenants.EnumerateArray()
            .Where(tenant => string.Equals(tenant.GetProperty("slug").GetString(), slug, StringComparison.Ordinal))
            .Select(tenant => tenant.GetProperty("displayName").GetString())
            .SingleOrDefault();
    }

    private static async Task CreateIdempotentAgentAsync(TraconTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            Api("agents"),
            new AgentDefinitionRequest
            {
                Name = "idem",
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
            });

        created.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> RunAsync(TraconTestHost host, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Api("agents/idem/run"))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Idempotency-Key", key);

        return host.Client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, string write)
    {
        switch (write)
        {
            case "tenant":
                return await PutTenantAsync(host, "acme", Long);

            case "approval-rule":
                return await host.Client.PostAsJsonAsync(Api("approvals/rules"), new { toolName = Long });

            case "feedback":
                return await SendFeedbackAsync(host);

            case "eval-suite":
                return await host.Client.PutAsJsonAsync(
                    Api("evals/suite"),
                    new { agentName = Long, checks = Array.Empty<object>() });

            case "mcp-server":
                return await host.Client.PutAsJsonAsync(
                    Api("mcp-servers/search"),
                    new
                    {
                        endpoint = "https://mcp.example.test/mcp",
                        oauthEnabled = true,
                        oauthClientId = Long,
                        oauthClientSecretConfigurationKey = "Tracon:McpSecrets:Search",
                    });

            default:
                throw new ArgumentOutOfRangeException(nameof(write), write, null);
        }
    }

    // The score's own validation bounds its name and its texts; the message it is attached to is free.
    private static async Task<HttpResponseMessage> SendFeedbackAsync(TraconTestHost host)
    {
        var runId = TraconId.NewId();

        await host.Services.GetRequiredService<IRunStore>().StartRunAsync(
            new RunStartInfo
            {
                RunId = runId,
                AgentName = "agent",
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = host.Services.GetRequiredService<ITenantContext>().TenantId,
            });

        return await host.Client.PostAsJsonAsync(
            Api($"runs/{runId}/feedback"),
            new { kind = "binary", value = 1, messageId = Long });
    }
}
