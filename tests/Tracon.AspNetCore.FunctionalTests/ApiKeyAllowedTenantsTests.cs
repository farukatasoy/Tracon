using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A key bound to a tenant that is not on <see cref="TraconTenancyOptions.AllowedTenants"/> does not work.
/// </summary>
/// <remarks>
/// The list says which tenants may use the installation. A tenant taken off it must stop at once,
/// and a key is another way to name a tenant than the header or the claim.
/// </remarks>
public sealed class ApiKeyAllowedTenantsTests
{
    private const string Path = "/tracon/api/agents";

    [Fact]
    public async Task A_key_of_a_tenant_on_the_list_works()
    {
        await using var host = await StartAsync();
        var key = await CreateKeyAsync(host, "tenant-a");

        using var response = await SendAsync(host, key);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_key_of_a_tenant_taken_off_the_list_is_refused()
    {
        await using var host = await StartAsync();
        var key = await CreateKeyAsync(host, "tenant-b");

        using var response = await SendAsync(host, key);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_conversation_socket_judges_a_key_of_a_tenant_by_the_list_too()
    {
        var provider = new StubVoiceProvider();
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowedTenants.Add("tenant-a");
                })
                .AddAgent(TestData.Definition("code-agent"))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            });

        using var allowed = await ConnectSocketAsync(host, await CreateKeyAsync(host, "tenant-a", ApiKeyScope.RunsWrite));
        allowed.State.ShouldBe(WebSocketState.Open);

        var refused = async () => await ConnectSocketAsync(host, await CreateKeyAsync(host, "tenant-b", ApiKeyScope.RunsWrite));

        (await refused.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("403");
    }

    private static async Task<WebSocket> ConnectSocketAsync(TraconTestHost host, string token)
    {
        var client = host.CreateWebSocketClient();
        client.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);
        client.SubProtocols.Add(VoiceConversationProtocol.TokenSubProtocolPrefix + token);

        return await client.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowedTenants.Add("tenant-a");
            }));

    private static async Task<string> CreateKeyAsync(TraconTestHost host, string tenantId, ApiKeyScope scope = ApiKeyScope.AgentsRead)
    {
        var created = await host.Services.GetRequiredService<IApiKeyStore>().CreateAsync(
            new ApiKeyDraft { TenantId = tenantId, Name = "probe", Scopes = [scope] },
            TestContext.Current.CancellationToken);

        return created.PlaintextKey;
    }

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
