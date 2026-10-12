namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// Behavior specific to <see cref="InMemoryAttachmentStore"/>: delegating to
/// external storage. Shared behavior such as tenant isolation, listing, and
/// deletion runs in <c>AttachmentStoreContract</c> (PostgreSql.IntegrationTests)
/// against both this store and <c>PostgresAttachmentStore</c>.
/// </summary>
public sealed class InMemoryAttachmentStoreTests
{
    [Fact]
    public async Task When_external_storage_is_registered_content_lives_there()
    {
        var storage = new FakeAttachmentStorage();
        var store = new InMemoryAttachmentStore(storage);

        var saved = await store.SaveAsync(new AttachmentContent
        {
            TenantId = "tenant-a",
            FileName = "test.png",
            MediaType = "image/png",
            Data = new byte[] { 1, 2, 3, 4 },
        });

        storage.Written.ShouldContainKey(saved.Id);

        await using var stream = await store.OpenReadAsync("tenant-a", saved.Id);
        stream.ShouldNotBeNull();

        await store.DeleteAsync("tenant-a", saved.Id);
        storage.Written.ShouldNotContainKey(saved.Id);
    }

    /// <summary>
    /// F-332: the entry is the only record of where the bytes are. Removing it
    /// before the byte delete succeeds loses the location for good when the
    /// storage fails.
    /// </summary>
    [Fact]
    public async Task A_failed_byte_delete_keeps_the_attachment()
    {
        var storage = new FakeAttachmentStorage();
        var store = new InMemoryAttachmentStore(storage);
        var first = await store.SaveAsync(Content("closing-session"));
        var second = await store.SaveAsync(Content("closing-session"));
        storage.FailDeletes = true;

        await Should.ThrowAsync<IOException>(async () => await store.DeleteAsync("tenant-a", first.Id));
        await Should.ThrowAsync<IOException>(async () => await store.DeleteBySessionAsync("tenant-a", "closing-session"));

        (await store.GetAsync("tenant-a", first.Id)).ShouldNotBeNull();
        (await store.GetAsync("tenant-a", second.Id)).ShouldNotBeNull();

        storage.FailDeletes = false;
        (await store.DeleteBySessionAsync("tenant-a", "closing-session")).ShouldBe(2);
        storage.Written.ShouldBeEmpty();
    }

    private static AttachmentContent Content(string sessionId) => new()
    {
        TenantId = "tenant-a",
        SessionId = sessionId,
        FileName = "test.png",
        MediaType = "image/png",
        Data = new byte[] { 1, 2, 3, 4 },
    };

    private sealed class FakeAttachmentStorage : IAttachmentStorage
    {
        public Dictionary<Guid, byte[]> Written { get; } = [];

        public bool FailDeletes { get; set; }

        public ValueTask<Uri> WriteAsync(
            string tenantId,
            Guid id,
            Stream content,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Written[id] = buffer.ToArray();

            return new ValueTask<Uri>(new Uri($"fake://attachments/{tenantId}/{id}"));
        }

        public ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            var id = Guid.Parse(uri.Segments[^1]);

            return new ValueTask<Stream?>(
                Written.TryGetValue(id, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        }

        public ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
            {
                throw new IOException("The attachment storage is unavailable.");
            }

            Written.Remove(Guid.Parse(uri.Segments[^1]));
            return default;
        }
    }
}
