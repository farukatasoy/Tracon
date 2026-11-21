using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Tracon.AGUI.FunctionalTests.Infrastructure;

namespace Tracon.AGUI.FunctionalTests;

/// <summary>
/// The key of a chat front end: the <c>ChatRun</c> scope opens the AG-UI endpoint and nothing else, and a key made for it
/// before that scope existed (<c>RunsWrite</c>) opens it as before.
/// </summary>
/// <remarks>
/// <para>
/// The claim is about reach, so the test does not stop at "the AG-UI endpoint answers": it asks the management endpoints the
/// same key, and compares what they answer with what they answer a key that holds some other scope. A refusal that were a
/// <c>404</c> for a missing route would read as a refusal and prove nothing, so each sampled endpoint is also asked with
/// the key that is meant to open it.
/// </para>
/// </remarks>
public sealed class AGUIApiKeyScopeTests
{
    private static readonly string[] AllScopeNames = [.. Enum.GetValues<ApiKeyScope>().Select(static scope => scope.ToString())];

    [Fact]
    public async Task A_key_that_holds_only_ChatRun_runs_an_agent_through_the_AG_UI_endpoint()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);
        var chat = await CreateKeyAsync(host, "ChatRun");

        var response = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-chat", "hello"), Bearer(chat));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text.ShouldBe("Echo: hello");
        response.Finished.Outcome.ShouldBeOfType<RunFinishedSuccessOutcome>();
    }

    [Fact]
    public async Task A_key_that_holds_RunsWrite_keeps_running_agents_through_the_AG_UI_endpoint()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);
        var writer = await CreateKeyAsync(host, "RunsWrite");

        var response = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-writer", "hello"), Bearer(writer));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text.ShouldBe("Echo: hello");
    }

    [Fact]
    public async Task A_key_with_neither_scope_is_refused_even_when_it_holds_every_other_one()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        var readOnly = await CreateKeyAsync(host, "RunsRead");
        var everythingElse = await CreateKeyAsync(
            host,
            [.. AllScopeNames.Where(static name => name is not ("ChatRun" or "RunsWrite"))]);

        foreach (var key in new[] { readOnly, everythingElse })
        {
            var response = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-neither", "hello"), Bearer(key));

            response.Status.ShouldBe(HttpStatusCode.Forbidden, response.Body);
        }

        // What the refusal says names the two scopes that would open the endpoint.
        var refused = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-neither", "hello"), Bearer(readOnly));
        refused.Problem.GetProperty("title").GetString().ShouldBe("Insufficient scope");
        refused.Problem.GetProperty("detail").GetString()!.ShouldContain("ChatRun", Case.Sensitive);
        refused.Problem.GetProperty("detail").GetString()!.ShouldContain("RunsWrite", Case.Sensitive);
    }

    [Fact]
    public async Task A_key_that_holds_only_ChatRun_reaches_no_management_endpoint()
    {
        await using var host = await AGUITestHost.StartAsync(TestAgents.AddEcho);

        var chat = await CreateKeyAsync(host, "ChatRun");
        var unrelated = await CreateKeyAsync(host, "KnowledgeRead");
        var reader = await CreateKeyAsync(host, "RunsRead", "AgentsRead");
        var writer = await CreateKeyAsync(host, "RunsWrite");
        var security = await CreateKeyAsync(host, "SecurityAdmin");

        var run = """{"message":"hello"}""";
        var newKey = """{"name":"minted","scopes":["SecurityAdmin"]}""";
        var approval = Guid.NewGuid();

        // Each sampled endpoint, the key that is meant to open it, and what that key is answered.
        var samples = new (HttpMethod Method, string Path, string? Body, string Opener, HttpStatusCode OpenerGets)[]
        {
            (HttpMethod.Get, "/tracon/api/runs", null, reader, HttpStatusCode.OK),
            (HttpMethod.Get, "/tracon/api/sessions", null, reader, HttpStatusCode.OK),
            (HttpMethod.Get, "/tracon/api/agents", null, reader, HttpStatusCode.OK),
            (HttpMethod.Get, "/tracon/api/approvals/pending", null, reader, HttpStatusCode.OK),
            (HttpMethod.Post, $"/tracon/api/agents/{TestAgents.Echo}/run", run, writer, HttpStatusCode.OK),
            (HttpMethod.Post, $"/tracon/api/approvals/{approval}/decide", """{"approved":true}""", writer, HttpStatusCode.NotFound),
            (HttpMethod.Delete, "/tracon/api/sessions/some-thread", null, writer, HttpStatusCode.NotFound),
            (HttpMethod.Get, "/tracon/api/api-keys", null, security, HttpStatusCode.OK),
            (HttpMethod.Post, "/tracon/api/api-keys", newKey, security, HttpStatusCode.OK),
        };

        foreach (var (method, path, body, opener, openerGets) in samples)
        {
            var label = $"{method} {path}";

            var withOpener = await SendAsync(host, method, path, body, opener);
            withOpener.ShouldBe(openerGets, $"{label} answers the key that is meant to open it");

            var withChat = await SendAsync(host, method, path, body, chat);
            var withUnrelated = await SendAsync(host, method, path, body, unrelated);

            withChat.ShouldBe(HttpStatusCode.Forbidden, $"{label} must not open for a ChatRun key");
            withChat.ShouldBe(withUnrelated, $"{label} answers a ChatRun key as it answers a key that lacks its scope");
        }
    }

    [Fact]
    public async Task A_caller_without_an_API_key_is_gated_by_role_alone()
    {
        // A claims principal carries no key, so the scope has nothing to narrow: the Operator role is what decides.
        await using var host = await AGUITestHost.StartAsync(
            TestAgents.AddEcho,
            configureServices: static services =>
            {
                services
                    .AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", configureOptions: null);
                services.AddAuthorizationBuilder()
                    .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                    .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                    .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => true));
            });

        var response = await host.RunAsync(TestAgents.Echo, AGUIInputs.User("t-claims", "hello"));

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text.ShouldBe("Echo: hello");
    }

    private static Action<HttpRequestMessage> Bearer(string key)
        => request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

    private static async Task<HttpStatusCode> SendAsync(AGUITestHost host, HttpMethod method, string path, string? body, string key)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static async Task<string> CreateKeyAsync(AGUITestHost host, params string[] scopes)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/api-keys", UriKind.Relative),
            new { name = $"agui-{string.Join('-', scopes.Take(3))}-{Guid.NewGuid():N}", scopes },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("plaintextKey").GetString()!;
    }
}
