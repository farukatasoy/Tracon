using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// The failure paths of the headers a webhook subscription declares by
/// configuration key name (phase 190).
/// </summary>
/// <remarks>
/// The happy path — a resolved header reaching a real receiver — is proven at
/// the delivery boundary by <c>CredentialHeaderDeliveryTests</c>. These drive
/// the handler directly because each case needs a configuration or a stored
/// row the HTTP save refuses to create. Every case checks that the log names
/// the header and never carries a value.
/// </remarks>
public sealed class WebhookCredentialHeaderDeliveryTests
{
    private const string Key = "Tracon:WebhookSecrets:OrdersKey";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\r\nbreak-190")]
    public async Task An_unusable_resolved_value_is_not_sent(string configured)
    {
        var (sent, logs) = await DeliverAsync(
            keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = Key },
            configuration: new(StringComparer.Ordinal) { [Key] = configured });

        sent.Headers.Contains("X-Api-Key").ShouldBeFalse();
        logs.ShouldContain(Key);
        logs.ShouldNotContain("break-190");
    }

    [Fact]
    public async Task Without_configuration_the_header_is_not_sent()
    {
        var (sent, logs) = await DeliverAsync(
            keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = Key },
            configuration: null);

        sent.Headers.Contains("X-Api-Key").ShouldBeFalse();
        logs.ShouldContain("X-Api-Key");
    }

    [Fact]
    public async Task A_header_declared_by_key_is_sent_once_and_wins_over_the_plain_one()
    {
        var (sent, logs) = await DeliverAsync(
            headers: new(StringComparer.OrdinalIgnoreCase) { ["x-api-key"] = "stale-plain-190" },
            keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = Key },
            configuration: new(StringComparer.Ordinal) { [Key] = "resolved-190" });

        sent.Headers.GetValues("X-Api-Key").ShouldBe(["resolved-190"]);
        logs.ShouldNotContain("stale-plain-190");
        logs.ShouldNotContain("resolved-190");
    }

    /// <summary>A row written before the credential rule still delivers, with a warning by name (decision 7).</summary>
    [Fact]
    public async Task A_stored_plain_credential_is_sent_with_a_warning_that_carries_no_value()
    {
        var (sent, logs) = await DeliverAsync(
            headers: new(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer legacy-190" },
            keys: [],
            configuration: []);

        sent.Headers.GetValues("Authorization").ShouldBe(["Bearer legacy-190"]);
        logs.ShouldContain("'Authorization' in plain headers");
        logs.ShouldNotContain("legacy-190");
    }

    [Fact]
    public async Task A_plain_value_with_a_line_break_is_not_sent()
    {
        var (sent, logs) = await DeliverAsync(
            headers: new(StringComparer.OrdinalIgnoreCase) { ["X-Team"] = "a\r\nX-Injected: b" },
            keys: [],
            configuration: []);

        sent.Headers.Contains("X-Team").ShouldBeFalse();
        sent.Headers.Contains("X-Injected").ShouldBeFalse();
        logs.ShouldContain("X-Team");
    }

    [Fact]
    public async Task A_content_header_declared_by_key_is_reported_not_dropped_silently()
    {
        var (sent, logs) = await DeliverAsync(
            keys: new(StringComparer.Ordinal) { ["Content-Language"] = Key },
            configuration: new(StringComparer.Ordinal) { [Key] = "tr-190" });

        sent.Headers.NonValidated.Contains("Content-Language").ShouldBeFalse();
        logs.ShouldContain("'Content-Language'");
        logs.ShouldNotContain("tr-190");
    }

    /// <summary>The limit can only cut a plain header; a credential header is never the one dropped.</summary>
    [Fact]
    public async Task The_extra_header_limit_cuts_plain_headers_first()
    {
        var (sent, _) = await DeliverAsync(
            headers: new(StringComparer.OrdinalIgnoreCase) { ["X-Team"] = "t", ["X-Env"] = "e" },
            keys: new(StringComparer.Ordinal) { ["X-Api-Key"] = Key },
            configuration: new(StringComparer.Ordinal) { [Key] = "resolved-190" },
            maxExtraHeaders: 1);

        sent.Headers.GetValues("X-Api-Key").ShouldBe(["resolved-190"]);
        sent.Headers.Contains("X-Team").ShouldBeFalse();
        sent.Headers.Contains("X-Env").ShouldBeFalse();
    }

    private static async Task<(HttpRequestMessage Request, string Logs)> DeliverAsync(
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? keys = null,
        Dictionary<string, string?>? configuration = null,
        int maxExtraHeaders = 20)
    {
        var options = new TraconWebhookOptions { MaxExtraHeaders = maxExtraHeaders };
        var store = new InMemoryWebhookStore();

        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "orders",

            // A public IP literal: the delivery-time SSRF check resolves no DNS.
            Url = "https://8.8.8.8/orders",
            Events = [WebhookEvents.RunCompleted],
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            HeaderConfigurationKeys = keys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var delivery = await store.CreateDeliveryAsync(new WebhookDelivery
        {
            Id = TraconId.NewId(),
            SubscriptionId = subscription.Id,
            TenantId = "default",
            EventType = WebhookEvents.RunCompleted,
            Payload = """{"runId":"1"}""",
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var capture = new CapturingHandler();
        var logger = new RecordingLogger();

        using var client = new WebhookHttpClient(capture);

        var handler = new WebhookDeliveryJobHandler(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(options),
            Options.Create(new TraconOptions()),
            configuration is null ? null : new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(),
            logger: logger);

        await handler.ExecuteAsync(new JobContext
        {
            Job = new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = "default",
                HandlerKey = JobHandlerKeys.WebhookDelivery,
                TargetName = "orders",
                Status = JobStatus.Running,
                Payload = default,
                Attempt = 1,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items =
            [
                new JobItemRecord
                {
                    Id = TraconId.NewId(),
                    JobId = Guid.Empty,
                    Seq = 0,
                    Input = delivery.Id.ToString(),
                    Status = JobItemStatus.Pending,
                },
            ],
            ReportItemAsync = (_, _) => ValueTask.CompletedTask,
            IsCancelledAsync = _ => ValueTask.FromResult(false),
        });

        return (capture.Request.ShouldNotBeNull(), logger.Text);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RecordingLogger : ILogger<WebhookDeliveryJobHandler>
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Text => string.Join('\n', _lines);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _lines.Enqueue($"{logLevel} {formatter(state, exception)} {exception}");
    }
}
