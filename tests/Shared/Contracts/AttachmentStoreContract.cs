namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IAttachmentStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the PostgreSQL store must pass the same
/// scenarios; in particular, tenant isolation and attachments being removed
/// when a session is deleted (docs/arsiv/fazlar/14-COK-MODLULUK.md, open question 2) must
/// behave identically in both implementations.
/// </remarks>
public abstract class AttachmentStoreContract : TenantIsolationContract<IAttachmentStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
        => (await Store.SaveAsync(Content(tenantId, fileName: $"{name}.png"))).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var id = (Guid)key;
        var descriptor = await Store.GetAsync(tenantId, id);

        // Metadata and content must carry the same isolation; if one leaks,
        // the other is considered leaked too.
        var content = await Store.OpenReadAsync(tenantId, id);

        await using (content)
        {
            (content is not null).ShouldBe(descriptor is not null);
        }

        return descriptor is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(new AttachmentQuery { TenantId = tenantId })).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Record_metadata_is_stored_correctly()
    {
        var saved = await Store.SaveAsync(Content("tenant-a", fileName: "report.pdf", mediaType: "application/pdf"));

        var loaded = await Store.GetAsync("tenant-a", saved.Id);

        loaded.ShouldNotBeNull();
        loaded.FileName.ShouldBe("report.pdf");
        loaded.MediaType.ShouldBe("application/pdf");
        loaded.ByteSize.ShouldBe(saved.ByteSize);
        loaded.Sha256.ShouldBe(saved.Sha256);
    }

    [Fact]
    public async Task Content_is_read_back_as_is()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var saved = await Store.SaveAsync(Content("tenant-a", data: data));

        await using var stream = await Store.OpenReadAsync("tenant-a", saved.Id);
        stream.ShouldNotBeNull();

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        buffer.ToArray().ShouldBe(data);
    }

    [Fact]
    public async Task Another_tenants_attachment_is_not_visible()
    {
        var saved = await Store.SaveAsync(Content("tenant-a"));

        (await Store.GetAsync("tenant-b", saved.Id)).ShouldBeNull();
        (await Store.OpenReadAsync("tenant-b", saved.Id)).ShouldBeNull();
        (await Store.DeleteAsync("tenant-b", saved.Id)).ShouldBeFalse();

        (await Store.GetAsync("tenant-a", saved.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Listing_filters_by_session()
    {
        await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));
        await Store.SaveAsync(Content("tenant-a", sessionId: "s-2"));
        await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));

        var results = await Store.ListAsync(new AttachmentQuery { TenantId = "tenant-a", SessionId = "s-1" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static descriptor => string.Equals(descriptor.SessionId, "s-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_succeeds_once_and_returns_false_the_second_time()
    {
        var saved = await Store.SaveAsync(Content("tenant-a"));

        (await Store.DeleteAsync("tenant-a", saved.Id)).ShouldBeTrue();
        (await Store.DeleteAsync("tenant-a", saved.Id)).ShouldBeFalse();
        (await Store.GetAsync("tenant-a", saved.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Bulk_delete_by_session_removes_only_matches()
    {
        var a = await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));
        var b = await Store.SaveAsync(Content("tenant-a", sessionId: "s-1"));
        var other = await Store.SaveAsync(Content("tenant-a", sessionId: "s-2"));

        var deleted = await Store.DeleteBySessionAsync("tenant-a", "s-1");

        deleted.ShouldBe(2);
        (await Store.GetAsync("tenant-a", a.Id)).ShouldBeNull();
        (await Store.GetAsync("tenant-a", b.Id)).ShouldBeNull();
        (await Store.GetAsync("tenant-a", other.Id)).ShouldNotBeNull();
    }

    private static AttachmentContent Content(
        string tenantId,
        string fileName = "test.png",
        string mediaType = "image/png",
        string? sessionId = null,
        byte[]? data = null)
        => new()
        {
            TenantId = tenantId,
            SessionId = sessionId,
            FileName = fileName,
            MediaType = mediaType,
            Data = data ?? [1, 2, 3, 4],
        };

    [Fact]
    public async Task Session_based_delete_does_not_delete_the_others_attachments()
    {
        await Store.SaveAsync(Content("tenant-a", sessionId: "shared-session"));
        await Store.SaveAsync(Content("tenant-b", sessionId: "shared-session"));

        (await Store.DeleteBySessionAsync("tenant-a", "shared-session")).ShouldBe(1);

        (await Store.ListAsync(new AttachmentQuery { TenantId = "tenant-a" })).ShouldBeEmpty();
        (await Store.ListAsync(new AttachmentQuery { TenantId = "tenant-b" })).ShouldHaveSingleItem();
    }
}
