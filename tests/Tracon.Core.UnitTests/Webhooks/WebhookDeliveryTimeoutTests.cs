using System.Net;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Webhooks;

/// <summary>
/// <see cref="TraconWebhookOptions.Timeout"/> bounds the whole delivery
/// attempt, not only its header phase.
/// </summary>
/// <remarks>
/// 🚨 The failure this closes: the deadline lived inside
/// <c>WebhookHttpClient.SendAsync</c>, whose token source was disposed the
/// moment the response HEADERS arrived. The body read that follows then ran
/// under the job worker's token alone. A recipient that answered its headers
/// instantly and then stalled on the body held a worker slot for as long as it
/// liked, while the option's own documentation said "a single delivery
/// attempt".
/// </remarks>
public sealed class WebhookDeliveryTimeoutTests
{
    [Fact]
    public async Task A_recipient_that_stalls_on_the_body_is_cut_off()
    {
        var store = new InMemoryWebhookStore();
        var options = new TraconWebhookOptions
        {
            AllowInsecureHttp = false,
            Timeout = TimeSpan.FromMilliseconds(150),
        };

        var subscription = await store.SaveSubscriptionAsync(new WebhookSubscription
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            Name = "orders",
            // A public IP literal: the delivery-time SSRF check resolves no DNS
            // for it, so the test never touches the network.
            Url = "https://8.8.8.8/orders",
            Events = [WebhookEvents.RunCompleted],
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

        using var client = new WebhookHttpClient(new StallingBodyHandler());

        var handler = new WebhookDeliveryJobHandler(
            store,
            client,
            new StaticOptionsMonitor<TraconWebhookOptions>(options),
            Options.Create(new TraconOptions()),
            new SecretConfiguration("ConnectionStrings:Default", "s3cret"));

        // 🚨 A worker token with its own deadline, an order of magnitude longer
        // than the option. It is the REGRESSION SIGNATURE: with the deadline
        // back inside the send call the body read has none of its own, and
        // this token is the only thing that ends the test - it then fails with
        // a cancellation instead of hanging until the whole run is killed.
        using var worker = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // A timeout is retryable, so the handler asks for another attempt
        // rather than dropping the delivery.
        var retry = await Should.ThrowAsync<JobRetryException>(
            async () => await handler.ExecuteAsync(BuildContext(delivery), worker.Token));

        retry.Message.ShouldContain("Timed out");

        var recorded = await store.GetDeliveryAsync(delivery.Id);

        recorded.ShouldNotBeNull().Error.ShouldNotBeNull().ShouldContain("Timed out");
    }

    /// <summary>
    /// Answers the headers at once and then never finishes the body.
    /// </summary>
    private sealed class StallingBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                // A 5xx makes the handler read the body, which is where the
                // stall waits. A 2xx returns before the read.
                Content = new StreamContent(new NeverEndingStream()),
            });
    }

    /// <summary>A body that stays open and never produces a byte.</summary>
    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
