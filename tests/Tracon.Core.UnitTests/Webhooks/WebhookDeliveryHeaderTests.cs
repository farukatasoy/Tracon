using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// The limits applied to a subscription's extra headers (B05-5).
/// </summary>
/// <remarks>
/// A subscription's extra headers are administrator input and travel on every
/// delivery. <c>TryAddWithoutValidation</c> APPENDS rather than replaces, so an
/// entry named <c>X-Tracon-Signature</c> used to add a SECOND signature
/// value: the recipient then saw two values and could not tell which one to
/// verify, which silently broke verification instead of failing loudly.
/// </remarks>
public sealed class WebhookDeliveryHeaderTests
{
    [Theory]
    [InlineData("X-Tracon-Signature")]
    [InlineData("x-tracon-signature")]
    [InlineData("X-Tracon-Timestamp")]
    [InlineData("X-Tracon-Event")]
    [InlineData("X-Tracon-Delivery")]
    public void Reserved_header_names_are_recognised_case_insensitively(string name)
        => WebhookSigner.IsReservedHeader(name).ShouldBeTrue();

    [Theory]
    [InlineData("X-Tenant")]
    [InlineData("Authorization")]
    [InlineData("X-Tracon-Custom")]
    [InlineData(null)]
    [InlineData("")]
    public void Other_header_names_are_not_reserved(string? name)
        => WebhookSigner.IsReservedHeader(name).ShouldBeFalse();

    [Fact]
    public async Task Extra_header_carrying_a_reserved_name_is_not_sent()
    {
        var (sent, _) = await DeliverAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Tracon-Signature"] = "attacker-supplied",
            ["X-Tenant"] = "acme",
        });

        // Exactly one signature value reaches the recipient, and it is ours.
        sent.Headers.GetValues(WebhookSigner.SignatureHeader).Count().ShouldBe(1);
        string.Equals(
            sent.Headers.GetValues(WebhookSigner.SignatureHeader).Single(),
            "attacker-supplied",
            StringComparison.Ordinal).ShouldBeFalse();

        // A header with an ordinary name is unaffected.
        sent.Headers.GetValues("X-Tenant").Single().ShouldBe("acme");
    }

    [Fact]
    public async Task Extra_headers_beyond_the_limit_are_not_sent()
    {
        var headers = Enumerable
            .Range(0, 30)
            .ToDictionary(
                index => string.Create(CultureInfo.InvariantCulture, $"X-Custom-{index:D2}"), index => index.ToString(CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);

        var (sent, _) = await DeliverAsync(headers, maxExtraHeaders: 5);

        sent.Headers.Count(header => header.Key.StartsWith("X-Custom-", StringComparison.Ordinal)).ShouldBe(5);
    }

    [Fact]
    public async Task Reserved_names_do_not_consume_the_extra_header_budget()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Tracon-Event"] = "spoofed",
            ["X-Custom-A"] = "a",
            ["X-Custom-B"] = "b",
        };

        var (sent, _) = await DeliverAsync(headers, maxExtraHeaders: 2);

        sent.Headers.GetValues(WebhookSigner.EventHeader).Count().ShouldBe(1);
        sent.Headers.GetValues(WebhookSigner.EventHeader).Single().ShouldBe("run.completed");
        sent.Headers.Count(header => header.Key.StartsWith("X-Custom-", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task A_subscription_without_extra_headers_still_carries_the_four_own_headers()
    {
        var (sent, _) = await DeliverAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        sent.Headers.Contains(WebhookSigner.EventHeader).ShouldBeTrue();
        sent.Headers.Contains(WebhookSigner.DeliveryHeader).ShouldBeTrue();
        sent.Headers.Contains(WebhookSigner.TimestampHeader).ShouldBeTrue();
        sent.Headers.Contains(WebhookSigner.SignatureHeader).ShouldBeTrue();
    }

    /// <summary>
    /// A subscription written before the prefix rule existed must be DROPPED,
    /// not left to throw.
    /// </summary>
    /// <remarks>
    /// The prefix guard throws, and the send path only caught cancellation and
    /// <see cref="HttpRequestException"/>. An escaping exception skipped both
    /// the delivery result and the consecutive-failure counter, so the record
    /// stayed <c>Pending</c> for ever and the subscription never disabled
    /// itself — it was retried to the attempt ceiling instead. The address
    /// rejection twenty lines above already dropped; this now matches it.
    /// </remarks>
    [Fact]
    public async Task Out_of_prefix_secret_key_drops_the_delivery_instead_of_throwing()
    {
        var options = new TraconWebhookOptions();
        var store = new InMemoryWebhookStore();

        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "legacy",
            Url = "https://8.8.8.8/orders",
            Events = [WebhookEvents.RunCompleted],

            // Written before the prefix rule existed.
            SecretConfigurationKey = "ConnectionStrings:Default",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
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

        using var client = new WebhookHttpClient(capture);

        var handler = new WebhookDeliveryJobHandler(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(options),
            Options.Create(new TraconOptions()),
            new SecretConfiguration("ConnectionStrings:Default", "s3cret"));

        // Neither an escaping exception nor a JobRetryException: the verdict
        // cannot change until the record does, so retrying is pointless.
        await Should.NotThrowAsync(async () => await handler.ExecuteAsync(BuildContext(delivery)));

        // Nothing was sent: the out-of-prefix value never left the process.
        capture.Request.ShouldBeNull();

        var recorded = await store.GetDeliveryAsync(delivery.Id);

        recorded.ShouldNotBeNull().Status.ShouldBe(WebhookDeliveryStatus.Dropped);
        recorded.Error.ShouldNotBeNull().ShouldContain(options.AllowedConfigurationPrefix);
    }

    private static JobContext BuildContext(WebhookDelivery delivery)
        => new()
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
        };

    private static async Task<(HttpRequestMessage Request, WebhookDelivery Delivery)> DeliverAsync(
        Dictionary<string, string> headers,
        int maxExtraHeaders = 20)
    {
        var options = new TraconWebhookOptions
        {
            AllowInsecureHttp = false,
            MaxExtraHeaders = maxExtraHeaders,
        };

        var store = new InMemoryWebhookStore();

        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "orders",
            // A public IP literal: the delivery-time SSRF check resolves no DNS for
            // it, so the test never touches the network.
            Url = "https://8.8.8.8/orders",
            Events = [WebhookEvents.RunCompleted],
            SecretConfigurationKey = $"{options.AllowedConfigurationPrefix}orders",
            Headers = headers,
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

        using var client = new WebhookHttpClient(capture);

        var handler = new WebhookDeliveryJobHandler(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(options),
            Options.Create(new TraconOptions()),
            new SecretConfiguration($"{options.AllowedConfigurationPrefix}orders", "s3cret"));

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

        return (capture.Request.ShouldNotBeNull(), delivery);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty),
            });
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class SecretConfiguration(string key, string secret)
        : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string name]
        {
            get => string.Equals(name, key, StringComparison.Ordinal) ? secret : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
            => throw new NotSupportedException();

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string name)
            => throw new NotSupportedException();
    }
}
