using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Tests for the webhook endpoints (Phase 21).</summary>
/// <remarks>
/// 🚨 The most important test is <see cref="Response_and_record_carry_no_secret"/>:
/// the contract <strong>has no secret field at all</strong>, and an extra
/// field sent in the request is not bound (K-059).
/// </remarks>
public sealed class WebhookEndpointTests
{
    private static readonly Uri Webhooks = new("/agentprism/api/webhooks", UriKind.Relative);
    private static readonly Uri Orders = new("/agentprism/api/webhooks/orders", UriKind.Relative);

    [Fact]
    public async Task Subscription_is_created_read_and_deleted()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("url").GetString().ShouldBe("https://example.com/hook");
            body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
            body.GetProperty("events").GetArrayLength().ShouldBe(2);
        }

        using (var listed = await host.Client.GetAsync(Webhooks))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using (var deleted = await host.Client.DeleteAsync(Orders))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var missing = await host.Client.GetAsync(Orders))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Response_and_record_carry_no_secret()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        // The client sends an extra 'secret' field. No such field exists in
        // the contract; it must not bind and must not appear in any response.
        using (var created = await host.Client.PutAsJsonAsync(
                   Orders,
                   new
                   {
                       url = "https://example.com/hook",
                       events = new[] { "run.completed" },
                       secretConfigurationKey = "AgentPrism:WebhookSecrets:orders",
                       secret = "super-secret-value",
                       signingSecret = "another-secret-value",
                       enabled = true,
                   }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var raw = await created.Content.ReadAsStringAsync();

            raw.ShouldNotContain("super-secret-value");
            raw.ShouldNotContain("another-secret-value");

            // The key's NAME must be returned — not the value.
            raw.ShouldContain("AgentPrism:WebhookSecrets:orders");
        }

        using var listed = await host.Client.GetAsync(Webhooks);
        var listRaw = await listed.Content.ReadAsStringAsync();

        listRaw.ShouldNotContain("super-secret-value");
        listRaw.ShouldNotContain("another-secret-value");
    }

    [Fact]
    public async Task Http_url_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(url: "http://example.com/hook"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("not-a-url")]
    public async Task Invalid_scheme_or_format_is_rejected(string url)
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(url: url));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unrecognized_event_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request(events: ["run.completed", "made-up.event"]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("made-up.event");
    }

    [Fact]
    public async Task Empty_event_list_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(events: []));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_event_is_queued()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/webhooks/orders/test", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("queued").GetBoolean().ShouldBeTrue();

        // The subscription was not subscribed to the 'test.ping' event; the
        // test is still sent, and the event list does not change permanently.
        using var reloaded = await host.Client.GetAsync(Orders);
        var subscription = await AgentPrismTestHost.ReadJsonAsync(reloaded);

        subscription.GetProperty("events").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Delivery_history_is_read()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var test = await host.Client.PostAsJsonAsync(
                   new Uri("/agentprism/api/webhooks/orders/test", UriKind.Relative),
                   new { }))
        {
            test.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/webhooks/orders/deliveries", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Test_cannot_be_sent_to_a_nonexistent_subscription()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/webhooks/no-such-subscription/test", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Loopback_http_url_is_accepted_when_allowed()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.Configure<AgentPrismWebhookOptions>(
                static options => options.AllowInsecureHttp = true));

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request(url: "http://localhost:9999/hook"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static WebhookSaveRequest Request(
        string url = "https://example.com/hook",
        IReadOnlyList<string>? events = null)
        => new()
        {
            Url = url,
            Events = events ?? ["run.completed", "run.failed"],
            Enabled = true,
        };
}
