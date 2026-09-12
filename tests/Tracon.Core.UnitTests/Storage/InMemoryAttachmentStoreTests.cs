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

    private sealed class FakeAttachmentStorage : IAttachmentStorage
    {
        public Dictionary<Guid, byte[]> Written { get; } = [];

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
            Written.Remove(Guid.Parse(uri.Segments[^1]));
            return default;
        }
    }
}
