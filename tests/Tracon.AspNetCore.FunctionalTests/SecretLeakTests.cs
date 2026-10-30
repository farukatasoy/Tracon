using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that the bearer token never appears in any output Tracon
/// gives out.
/// </summary>
/// <remarks>
/// The protected boundary is this: the token is compared only against the
/// incoming request's header. The options object, the <c>/api/meta</c>
/// output, error responses, and log lines must <strong>never, under any
/// condition,</strong> carry the token.
/// </remarks>
public sealed class SecretLeakTests
{
    private const string Token = "very-secret-token-TEST-91af3c";

    [Fact]
    public void Options_object_does_not_define_its_own_ToString_method()
    {
        // The options object must NOT be a record: the compiler-generated
        // ToString would print every property and leak the token in the
        // very first log line.
        typeof(TraconEndpointOptions).GetMethod(nameof(ToString), Type.EmptyTypes)!
            .DeclaringType.ShouldBe(typeof(object));
    }

    [Fact]
    public async Task Meta_output_does_not_contain_the_token()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/meta", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
    }

    [Fact]
    public async Task Rejected_requests_response_does_not_contain_the_token()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        using var response = await host.Client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
    }

    [Fact]
    public async Task Log_lines_do_not_contain_the_token()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AuthToken = Token);

        using (var rejected = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative)))
        {
            rejected.Dispose();
        }

        using var accepted = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        accepted.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var response = await host.Client.SendAsync(accepted))
        {
            response.EnsureSuccessStatusCode();
        }

        host.Logs.AllText.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Running_a_code_agent_does_not_leak_the_token()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            static options =>
            {
                options.AuthToken = Token;
                options.AllowRemoteAccess = true;
            });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba", SessionId = "s-secret" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
            (await response.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        // Session state, run records, and logs must also be clean.
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/sessions/s-secret");
        sessionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var session = await host.Client.SendAsync(sessionRequest))
        {
            (await session.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        using var runsRequest = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/runs");
        runsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        using (var runs = await host.Client.SendAsync(runsRequest))
        {
            (await runs.Content.ReadAsStringAsync()).ShouldNotContain(Token);
        }

        host.Logs.AllText.ShouldNotContain(Token);
    }

    // --- an earlier change: API key ---

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_the_listing()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/api-keys", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_the_audit_trail()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_the_rejected_requests_response()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "readonly", "RunsRead");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/kod-agent/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "merhaba" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task Raw_api_key_does_not_appear_in_log_lines()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "ci", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using (var response = await host.Client.SendAsync(request))
        {
            response.EnsureSuccessStatusCode();
        }

        host.Logs.AllText.ShouldNotContain(created.PlaintextKey);
    }

    [Fact]
    public async Task A_tenant_speech_key_appears_in_no_response_and_no_log_line()
    {
        // A speech refusal names the tenant, the provider and the key's NAME;
        // the key's value stays in configuration. Both refusal kinds are exercised:
        // a provider that cannot use the key, and one that can (the call succeeds).
        const string KeyName = "Tracon:ProviderKeys:Studio";
        const string KeyValue = "studio-tenant-key-a81f20";

        var refusing = new CountingSpeechProvider("studio-engine", supportsTenantCredentials: false);
        var accepting = new CountingSpeechProvider("cloud-engine", supportsTenantCredentials: true);

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddSpeechSynthesizer("studio", _ => refusing)
                .AddSpeechSynthesizer("cloud", _ => accepting),
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, KeyValue)]).Build()));

        var bindings = host.Services.GetRequiredService<ITenantProviderBindingStore>();

        foreach (var provider in new[] { "studio-engine", "cloud-engine" })
        {
            await bindings.UpsertAsync(
                new TenantProviderBinding
                {
                    TenantId = "default",
                    ProviderName = provider,
                    ApiKeyConfigurationName = KeyName,
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
                TestContext.Current.CancellationToken);
        }

        using var refused = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/speak", new { text = "hello", provider = "studio" });
        using var served = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/speak", new { text = "hello", provider = "cloud" });
        using var providers = await host.Client.GetAsync("/tracon/api/voice/providers");

        refused.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        served.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        accepting.Received.ShouldHaveSingleItem().ApiKey.ShouldBe(KeyValue);

        (await refused.Content.ReadAsStringAsync()).ShouldNotContain(KeyValue);
        (await served.Content.ReadAsStringAsync()).ShouldNotContain(KeyValue);
        (await providers.Content.ReadAsStringAsync()).ShouldNotContain(KeyValue);
        host.Logs.AllText.ShouldNotContain(KeyValue);
    }
}
