using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The save rules for credential headers on the two surfaces that store
/// request headers: MCP servers and webhook subscriptions (phase 190).
/// </summary>
/// <remarks>
/// A plain header is stored in the clear, so a credential-looking name is
/// rejected and pointed at <c>headerConfigurationKeys</c>, which stores only
/// the NAME of the configuration key (K-059). These run at the HTTP boundary:
/// the binding, the stored record and the response shape are all part of the
/// rule.
/// </remarks>
public sealed class CredentialHeaderSaveTests
{
    private const string PlainValue = "plain-value-190";

    private static readonly Uri Mcp = new("/tracon/api/mcp-servers/m1", UriKind.Relative);
    private static readonly Uri Webhook = new("/tracon/api/webhooks/w1", UriKind.Relative);

    public static TheoryData<string> CredentialNames => new()
    {
        "X-API-Key",
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Ocp-Apim-Subscription-Key",
        "X-Auth-Token",
    };

    [Theory]
    [MemberData(nameof(CredentialNames))]
    public async Task Mcp_plain_credential_header_is_rejected_without_echoing_the_value(string header)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Mcp, McpBody(headers: Map((header, PlainValue))));

        await ShouldBeRejectedAsync(response, header);
        (await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1")).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(CredentialNames))]
    public async Task Webhook_plain_credential_header_is_rejected_without_echoing_the_value(string header)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Webhook, WebhookBody(headers: Map((header, PlainValue))));

        await ShouldBeRejectedAsync(response, header);
        (await host.Services.GetRequiredService<IWebhookStore>().GetSubscriptionAsync("default", "w1")).ShouldBeNull();
    }

    [Fact]
    public async Task Header_configuration_keys_are_stored_as_names_and_returned_unmasked()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            Mcp,
            McpBody(
                headers: Map(("X-Team", "platform")),
                keys: Map(("X-API-Key", "Tracon:McpSecrets:SearchKey"))));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(saved);

        // The plain value is masked; the key NAME is not — it carries no value.
        body.GetProperty("headers").GetProperty("X-Team").GetString().ShouldBe(HeaderValueMask.Value);
        body.GetProperty("headerConfigurationKeys").GetProperty("X-API-Key").GetString()
            .ShouldBe("Tracon:McpSecrets:SearchKey");

        var listed = await TraconTestHost.ReadJsonAsync(await host.Client.GetAsync(new Uri("/tracon/api/mcp-servers", UriKind.Relative)));
        listed[0].GetProperty("headerConfigurationKeys").GetProperty("X-API-Key").GetString()
            .ShouldBe("Tracon:McpSecrets:SearchKey");

        var stored = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1");
        stored.ShouldNotBeNull().HeaderConfigurationKeys["x-api-key"].ShouldBe("Tracon:McpSecrets:SearchKey");
    }

    [Fact]
    public async Task Webhook_header_configuration_keys_are_returned_unmasked()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            Webhook,
            WebhookBody(keys: Map(("X-API-Key", "Tracon:WebhookSecrets:OrdersKey"))));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        foreach (var response in new[] { saved, await host.Client.GetAsync(Webhook) })
        {
            (await TraconTestHost.ReadJsonAsync(response))
                .GetProperty("headerConfigurationKeys").GetProperty("X-API-Key").GetString()
                .ShouldBe("Tracon:WebhookSecrets:OrdersKey");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_configuration_key_name_is_rejected(string keyName)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var mcp = await host.Client.PutAsJsonAsync(Mcp, McpBody(keys: Map(("X-API-Key", keyName))));
        using var webhook = await host.Client.PutAsJsonAsync(Webhook, WebhookBody(keys: Map(("X-API-Key", keyName))));

        foreach (var response in new[] { mcp, webhook })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Detail(response)).ShouldContain("headerConfigurationKeys[X-API-Key]");
        }
    }

    [Theory]
    [InlineData("X Api Key")]
    [InlineData("X-Api:Key")]
    [InlineData("X-Api\r\nX-Injected")]
    [InlineData("")]
    public async Task A_header_name_outside_the_http_token_grammar_is_rejected(string name)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var plain = await host.Client.PutAsJsonAsync(Mcp, McpBody(headers: Map((name, "v"))));
        using var keyed = await host.Client.PutAsJsonAsync(
            Webhook,
            WebhookBody(keys: Map((name, "Tracon:WebhookSecrets:OrdersKey"))));

        foreach (var response in new[] { plain, keyed })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var detail = await Detail(response);
            detail.ShouldContain("header name");
            detail.ShouldNotContain("X-Injected");
        }
    }

    [Theory]
    [InlineData("X-Tracon-Signature")]
    [InlineData("x-tracon-event")]
    public async Task Webhook_key_for_a_reserved_header_is_rejected(string header)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Webhook,
            WebhookBody(keys: Map((header, "Tracon:WebhookSecrets:DemoKey"))));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(response)).ShouldContain($"headerConfigurationKeys[{header}]");
    }

    [Fact]
    public async Task Webhook_maps_together_may_not_exceed_the_extra_header_limit()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconWebhookOptions>(
                static options => options.MaxExtraHeaders = 2));

        using var response = await host.Client.PutAsJsonAsync(
            Webhook,
            WebhookBody(
                headers: Map(("X-Team", "t"), ("X-Env", "e")),
                keys: Map(("X-API-Key", "Tracon:WebhookSecrets:OrdersKey"))));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(response)).ShouldContain("MaxExtraHeaders");
    }

    /// <summary>
    /// A case-only duplicate used to reach the stores: the webhook store threw
    /// (500) and the MCP transport could not connect.
    /// </summary>
    [Fact]
    public async Task Header_names_that_differ_only_in_case_are_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var mcp = await PutRawAsync(host, Mcp, """{"endpoint":"https://mcp.example.com/mcp","headers":{"X-Team":"a","x-team":"b"}}""");
        using var webhook = await PutRawAsync(host, Webhook, """{"url":"https://example.com/hook","events":["run.completed"],"headers":{"X-Team":"a","x-team":"b"}}""");
        using var crossed = await PutRawAsync(
            host,
            Mcp,
            """{"endpoint":"https://mcp.example.com/mcp","headers":{"X-Team":"a"},"headerConfigurationKeys":{"x-team":"Tracon:McpSecrets:TeamKey"}}""");

        foreach (var response in new[] { mcp, webhook, crossed })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await Detail(response)).ShouldContain("case-insensitive", Case.Insensitive);
        }
    }

    [Fact]
    public async Task Authorization_from_both_fields_or_with_oauth_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var both = await host.Client.PutAsJsonAsync(Mcp, new
        {
            endpoint = "https://mcp.example.com/mcp",
            authorizationConfigurationKey = "Tracon:McpSecrets:Legacy",
            headerConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = "Tracon:McpSecrets:Bearer" },
        });

        using var oauth = await host.Client.PutAsJsonAsync(new Uri("/tracon/api/mcp-servers/m2", UriKind.Relative), new
        {
            endpoint = "https://mcp.example.com/mcp",
            oauthEnabled = true,
            oauthClientId = "client",
            headerConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["authorization"] = "Tracon:McpSecrets:Bearer" },
        });

        foreach (var response in new[] { both, oauth })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Detail(response)).ShouldContain("'headerConfigurationKeys' names 'Authorization'");
        }
    }

    /// <summary>
    /// The conflict is judged on what will be STORED: the form writes the
    /// deprecated field and leaves the maps out, so the stored map with
    /// <c>Authorization</c> is kept and would sit next to it.
    /// </summary>
    [Fact]
    public async Task Authorization_conflict_counts_the_preserved_map()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var first = await host.Client.PutAsJsonAsync(
                   Mcp,
                   McpBody(keys: Map(("Authorization", "Tracon:McpSecrets:Bearer")))))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        }

        using var form = await host.Client.PutAsJsonAsync(Mcp, new
        {
            endpoint = "https://mcp.example.com/mcp",
            authorizationConfigurationKey = "Tracon:McpSecrets:Legacy",
        });

        form.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Detail(form)).ShouldContain("authorizationConfigurationKey");
    }

    /// <summary>
    /// A stored row may still carry a plain Authorization header. Turning OAuth
    /// on while it is kept would make the MCP client skip its bearer token on
    /// every request (measured against the SDK), so the save is refused.
    /// </summary>
    [Fact]
    public async Task Oauth_is_refused_while_the_kept_plain_headers_carry_authorization()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.Services.GetRequiredService<IMcpServerStore>().SaveAsync(new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = "default",
            Name = "m1",
            Endpoint = new Uri("https://mcp.example.com/mcp"),
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = "Bearer legacy-190" },
        });

        using var refused = await host.Client.PutAsJsonAsync(Mcp, new
        {
            endpoint = "https://mcp.example.com/mcp",
            oauthEnabled = true,
            oauthClientId = "client",
        });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var detail = await Detail(refused);
        detail.ShouldContain("stored 'headers' carry 'Authorization'");
        detail.ShouldNotContain("legacy-190");

        using var accepted = await host.Client.PutAsJsonAsync(Mcp, new
        {
            endpoint = "https://mcp.example.com/mcp",
            oauthEnabled = true,
            oauthClientId = "client",
            headers = Map(),
        });

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_deprecated_field_still_saves_and_reads_back()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(Mcp, new
        {
            endpoint = "https://mcp.example.com/mcp",
            authorizationConfigurationKey = "Tracon:McpSecrets:Legacy",
        });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(saved)).GetProperty("authorizationConfigurationKey").GetString()
            .ShouldBe("Tracon:McpSecrets:Legacy");
    }

    internal static object McpBody(
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? keys = null,
        string endpoint = "https://mcp.example.com/mcp")
        => new
        {
            endpoint,
            headers,
            headerConfigurationKeys = keys,
        };

    internal static object WebhookBody(
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? keys = null,
        string url = "https://example.com/hook")
        => new
        {
            url,
            events = new[] { "run.completed" },
            headers,
            headerConfigurationKeys = keys,
        };

    internal static Dictionary<string, string> Map(params (string Name, string Value)[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in entries)
        {
            map[name] = value;
        }

        return map;
    }

    internal static async Task<string> Detail(HttpResponseMessage response)
        => (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString() ?? string.Empty;

    private static async Task ShouldBeRejectedAsync(HttpResponseMessage response, string header)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(PlainValue);

        var detail = JsonDocument.Parse(raw).RootElement.GetProperty("detail").GetString().ShouldNotBeNull();
        detail.ShouldContain(header);
        detail.ShouldContain("headerConfigurationKeys");
    }

    private static Task<HttpResponseMessage> PutRawAsync(TraconTestHost host, Uri uri, string json)
        => host.Client.PutAsync(uri, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
}
