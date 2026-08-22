using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Images;

/// <summary>Verifies the persistence and outbound-boundary rules for generated images.</summary>
#pragma warning disable MEAI001
public sealed class ImageAttachmentWriterTests
{
    [Fact]
    public async Task Data_content_is_saved_with_the_current_tenant_and_supplied_session()
    {
        var store = new InMemoryAttachmentStore();
        var writer = CreateWriter(store);
        var runId = AgentPrismId.NewId();

        var attachment = (await writer.WriteAsync(
            [new DataContent(Png(), "image/png")],
            "tenant-a",
            "image-session",
            runId,
            "image-agent",
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        attachment.TenantId.ShouldBe("tenant-a");
        attachment.SessionId.ShouldBe("image-session");
        attachment.RunId.ShouldBe(runId);
        attachment.MediaType.ShouldBe("image/png");
        attachment.CreatedBy.ShouldBe("image-agent");

        await using var saved = (await store.OpenReadAsync("tenant-a", attachment.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        using var bytes = new MemoryStream();
        await saved.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        bytes.ToArray().ShouldBe(Png());
    }

    [Fact]
    public async Task Uri_content_uses_the_egress_guard_instead_of_connecting_to_loopback()
    {
        var writer = CreateWriter(new InMemoryAttachmentStore());

        var exception = await Should.ThrowAsync<Exception>(async () => await writer.WriteAsync(
            [new UriContent("http://127.0.0.1:1/image.png", "image/png")],
            "tenant-a",
            sessionId: null,
            runId: null,
            createdBy: null,
            TestContext.Current.CancellationToken));

        exception.ToString().ShouldContain("loopback", Case.Insensitive);
    }

    [Fact]
    public async Task Uri_content_is_downloaded_through_the_guard_and_saved_as_an_attachment()
    {
        var store = new InMemoryAttachmentStore();
        await using var server = await ImageServer.StartAsync(Png(), chunked: true);
        var writer = CreateWriter(store, allowLoopback: true);

        var attachment = (await writer.WriteAsync(
            [new UriContent(server.Uri, "image/png")],
            "tenant-a",
            sessionId: "image-session",
            runId: null,
            createdBy: "operator",
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        attachment.TenantId.ShouldBe("tenant-a");
        attachment.SessionId.ShouldBe("image-session");
        attachment.MediaType.ShouldBe("image/png");
        await using var saved = (await store.OpenReadAsync("tenant-a", attachment.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        using var bytes = new MemoryStream();
        await saved.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        bytes.ToArray().ShouldBe(Png());
    }

    [Fact]
    public async Task Chunked_uri_content_over_the_limit_is_rejected_before_an_attachment_is_written()
    {
        var store = new InMemoryAttachmentStore();
        await using var server = await ImageServer.StartAsync([.. Png(), 0x00], chunked: true);
        var writer = CreateWriter(store, allowLoopback: true, maximumBytes: Png().Length);

        await Should.ThrowAsync<AgentPrismException>(async () => await writer.WriteAsync(
            [new UriContent(server.Uri, "image/png")],
            "tenant-a",
            sessionId: null,
            runId: null,
            createdBy: null,
            TestContext.Current.CancellationToken));

        (await store.ListAsync(new AttachmentQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Hosted_file_reference_is_rejected_without_a_download_or_broken_attachment()
    {
        var store = new InMemoryAttachmentStore();
        var writer = CreateWriter(store);

        var exception = await Should.ThrowAsync<AgentPrismException>(async () => await writer.WriteAsync(
            [new HostedFileContent("file-image-1") { MediaType = "image/png" }],
            "tenant-a",
            sessionId: "image-session",
            runId: null,
            createdBy: null,
            TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("hosted file reference", Case.Insensitive);
        (await store.ListAsync(new AttachmentQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_writes_get_distinct_attachment_identifiers()
    {
        var writer = CreateWriter(new InMemoryAttachmentStore());

        var writes = await Task.WhenAll(
            writer.WriteAsync([new DataContent(Png(), "image/png")], "tenant-a", "s", null, null, TestContext.Current.CancellationToken).AsTask(),
            writer.WriteAsync([new DataContent(Png(), "image/png")], "tenant-a", "s", null, null, TestContext.Current.CancellationToken).AsTask());

        writes.SelectMany(static items => items).Select(static item => item.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Failure_after_the_first_image_rolls_back_saved_attachments()
    {
        var store = new InMemoryAttachmentStore();
        var writer = CreateWriter(store);

        await Should.ThrowAsync<AgentPrismException>(async () => await writer.WriteAsync(
            [
                new DataContent(Png(), "image/png"),
                new HostedFileContent("file-image-1") { MediaType = "image/png" },
            ],
            "tenant-a",
            sessionId: "image-session",
            runId: AgentPrismId.NewId(),
            createdBy: "image-agent",
            TestContext.Current.CancellationToken));

        (await store.ListAsync(new AttachmentQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_rollback_preserves_the_original_error_and_is_best_effort()
    {
        var innerStore = new InMemoryAttachmentStore();
        var store = new DeleteFailingAttachmentStore(innerStore);
        var writer = CreateWriter(store);

        var exception = await Should.ThrowAsync<AgentPrismException>(async () => await writer.WriteAsync(
            [
                new DataContent(Png(), "image/png"),
                new HostedFileContent("file-image-1") { MediaType = "image/png" },
            ],
            "tenant-a",
            sessionId: "image-session",
            runId: AgentPrismId.NewId(),
            createdBy: "image-agent",
            TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("hosted file reference", Case.Insensitive);
        store.DeleteCalls.ShouldBe(1);
        (await innerStore.ListAsync(new AttachmentQuery { TenantId = "tenant-a" }, TestContext.Current.CancellationToken))
            .Count.ShouldBe(1);
    }

    private static ImageAttachmentWriter CreateWriter(
        IAttachmentStore store,
        bool allowLoopback = false,
        long? maximumBytes = null)
    {
        var options = Options.Create(new AgentPrismOptions());
        if (maximumBytes is { } value)
        {
            options.Value.Attachments.MaxBytes = value;
        }

        return new ImageAttachmentWriter(
            store,
            new AttachmentTypeGuard(options),
            new EgressSocketGuard(() => new EgressAddressPolicy(AllowPrivateNetworkTargets: allowLoopback, AllowLoopback: allowLoopback)),
            NullLogger<ImageAttachmentWriter>.Instance);
    }

    private static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[8]];
    }

    private sealed class DeleteFailingAttachmentStore(IAttachmentStore inner) : IAttachmentStore
    {
        public int DeleteCalls { get; private set; }

        public ValueTask<AttachmentDescriptor> SaveAsync(
            AttachmentContent content,
            CancellationToken cancellationToken = default)
            => inner.SaveAsync(content, cancellationToken);

        public ValueTask<AttachmentDescriptor?> GetAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<Stream?> OpenReadAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
            => inner.OpenReadAsync(tenantId, id, cancellationToken);

        public ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(
            AttachmentQuery query,
            CancellationToken cancellationToken = default)
            => inner.ListAsync(query, cancellationToken);

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid id,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            throw new InvalidOperationException("Attachment cleanup is unavailable.");
        }

        public ValueTask<int> DeleteBySessionAsync(
            string tenantId,
            string sessionId,
            CancellationToken cancellationToken = default)
            => inner.DeleteBySessionAsync(tenantId, sessionId, cancellationToken);
    }

    private sealed class ImageServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _request;

        private ImageServer(HttpListener listener, Task request, Uri uri)
        {
            _listener = listener;
            _request = request;
            Uri = uri;
        }

        public Uri Uri { get; }

        public static async Task<ImageServer> StartAsync(byte[] payload, bool chunked)
        {
            using var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();

            var listener = new HttpListener();
            var uri = new Uri($"http://127.0.0.1:{port}/image.png");
            listener.Prefixes.Add(uri.GetLeftPart(UriPartial.Authority) + "/");
            listener.Start();

            var request = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                context.Response.ContentType = "image/png";
                context.Response.SendChunked = chunked;
                await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
                context.Response.Close();
            });

            await Task.Yield();
            return new ImageServer(listener, request, uri);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await _request.ConfigureAwait(false);
            _listener.Close();
        }
    }
}
#pragma warning restore MEAI001
