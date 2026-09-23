using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the webhook endpoints (Phase 21).</summary>
/// <remarks>
/// 🚨 The most important test is <see cref="Response_and_record_carry_no_secret"/>:
/// the contract <strong>has no secret field at all</strong>, and an extra
/// field sent in the request is not bound (K-059).
/// </remarks>
public sealed class WebhookEndpointTests
{
    private static readonly Uri Webhooks = new("/tracon/api/webhooks", UriKind.Relative);
    private static readonly Uri Orders = new("/tracon/api/webhooks/orders", UriKind.Relative);

    [Fact]
    public async Task Subscription_is_created_read_and_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await TraconTestHost.ReadJsonAsync(created);
            body.GetProperty("url").GetString().ShouldBe("https://example.com/hook");
            body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
            body.GetProperty("events").GetArrayLength().ShouldBe(2);
        }

        using (var listed = await host.Client.GetAsync(Webhooks))
        {
            (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
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
        await using var host = await TraconTestHost.StartAsync();

        // The client sends an extra 'secret' field. No such field exists in
        // the contract; it must not bind and must not appear in any response.
        using (var created = await host.Client.PutAsJsonAsync(
                   Orders,
                   new
                   {
                       url = "https://example.com/hook",
                       events = new[] { "run.completed" },
                       secretConfigurationKey = "Tracon:WebhookSecrets:orders",
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
            raw.ShouldContain("Tracon:WebhookSecrets:orders");
        }

        using var listed = await host.Client.GetAsync(Webhooks);
        var listRaw = await listed.Content.ReadAsStringAsync();

        listRaw.ShouldNotContain("super-secret-value");
        listRaw.ShouldNotContain("another-secret-value");
    }

    [Fact]
    public async Task Http_url_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(url: "http://example.com/hook"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("not-a-url")]
    public async Task Invalid_scheme_or_format_is_rejected(string url)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(url: url));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unrecognized_event_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request(events: ["run.completed", "made-up.event"]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("made-up.event");
    }

    [Fact]
    public async Task Empty_event_list_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(events: []));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_event_is_queued()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/webhooks/orders/test", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("queued").GetBoolean().ShouldBeTrue();

        // The subscription was not subscribed to the 'test.ping' event; the
        // test is still sent, and the event list does not change permanently.
        using var reloaded = await host.Client.GetAsync(Orders);
        var subscription = await TraconTestHost.ReadJsonAsync(reloaded);

        subscription.GetProperty("events").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Delivery_history_is_read()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var test = await host.Client.PostAsJsonAsync(
                   new Uri("/tracon/api/webhooks/orders/test", UriKind.Relative),
                   new { }))
        {
            test.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/webhooks/orders/deliveries", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Test_cannot_be_sent_to_a_nonexistent_subscription()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/webhooks/no-such-subscription/test", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Loopback_http_url_is_accepted_when_allowed()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconWebhookOptions>(
                static options => options.AllowInsecureHttp = true));

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request(url: "http://localhost:9999/hook"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Header_values_are_masked_in_every_response_and_kept_in_the_store()
    {
        const string ApiKey = "receiver-api-key-value";

        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            Orders,
            Request() with
            {
                Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Api-Key"] = ApiKey, ["X-Team"] = "team-blue-value" },
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var read = await host.Client.GetAsync(Orders);
        using var listed = await host.Client.GetAsync(Webhooks);

        foreach (var response in new[] { saved, read, listed })
        {
            var body = await response.Content.ReadAsStringAsync();

            body.ShouldNotContain(ApiKey);
            body.ShouldNotContain("team-blue-value");
        }

        var subscription = (await read.Content.ReadFromJsonAsync<WebhookSubscription>())!;
        subscription.Headers.ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Api-Key"] = "***", ["X-Team"] = "***" },
            ignoreOrder: true);

        // The receiver still gets the real value: the mask is a response rule.
        var stored = await host.Services.GetRequiredService<IWebhookStore>().GetSubscriptionAsync("default", "orders");
        stored!.Headers["X-Api-Key"].ShouldBe(ApiKey);

        // The audit trail records no header value either.
        var entries = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Entity = "webhook:orders" });

        entries.ShouldNotBeEmpty();
        entries.ShouldAllBe(static entry => !$"{entry.Before}{entry.After}".Contains(ApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_that_sends_the_mask_back_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request() with { Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Api-Key"] = "***" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString();
        detail.ShouldNotBeNull().ShouldContain("X-Api-Key");
        detail.ShouldContain("real value");

        (await host.Services.GetRequiredService<IWebhookStore>().GetSubscriptionAsync("default", "orders")).ShouldBeNull();
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
