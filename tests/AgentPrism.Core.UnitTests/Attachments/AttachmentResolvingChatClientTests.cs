using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Attachments;

/// <summary>
/// <see cref="AttachmentResolvingChatClient"/>'in ek referanslarini model
/// cagrisindan hemen once gercek icerige cozdugunu dogrular.
/// </summary>
public sealed class AttachmentResolvingChatClientTests
{
    [Fact]
    public async Task Ek_referansi_gercek_icerige_cozulur()
    {
        var store = new InMemoryAttachmentStore();
        var saved = await store.SaveAsync(new AttachmentContent
        {
            TenantId = "tenant-a",
            FileName = "test.png",
            MediaType = "image/png",
            Data = new byte[] { 1, 2, 3 },
        });

        var inner = new CapturingChatClient();
        var client = new AttachmentResolvingChatClient(inner, store, new FixedTenantContext("tenant-a"));

        var uri = AttachmentUriReference.Create("/agentprism", saved.Id);
        var message = new ChatMessage(ChatRole.User, [new TextContent("bak"), new UriContent(uri, "image/png")]);

        await client.GetResponseAsync([message]);

        var resolvedContents = inner.LastMessages.ShouldNotBeNull()[0].Contents;
        resolvedContents.OfType<TextContent>().ShouldHaveSingleItem();

        var dataContent = resolvedContents.OfType<DataContent>().ShouldHaveSingleItem();
        dataContent.Data.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
        dataContent.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public async Task Ek_icermeyen_mesaj_degistirilmeden_gecer()
    {
        var store = new InMemoryAttachmentStore();
        var inner = new CapturingChatClient();
        var client = new AttachmentResolvingChatClient(inner, store, new FixedTenantContext("tenant-a"));

        var message = new ChatMessage(ChatRole.User, "merhaba");

        await client.GetResponseAsync([message]);

        inner.LastMessages.ShouldNotBeNull()[0].ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Bulunamayan_veya_baska_kiraciya_ait_ek_hata_uretir()
    {
        var store = new InMemoryAttachmentStore();
        var inner = new CapturingChatClient();
        var client = new AttachmentResolvingChatClient(inner, store, new FixedTenantContext("tenant-a"));

        var uri = AttachmentUriReference.Create("/agentprism", Guid.NewGuid());
        var message = new ChatMessage(ChatRole.User, [new UriContent(uri, "image/png")]);

        await Should.ThrowAsync<AgentPrismException>(async () => await client.GetResponseAsync([message]));
    }

    [Fact]
    public async Task Akisli_cagrida_da_cozulur()
    {
        var store = new InMemoryAttachmentStore();
        var saved = await store.SaveAsync(new AttachmentContent
        {
            TenantId = "tenant-a",
            FileName = "test.png",
            MediaType = "image/png",
            Data = new byte[] { 9, 9 },
        });

        var inner = new CapturingChatClient();
        var client = new AttachmentResolvingChatClient(inner, store, new FixedTenantContext("tenant-a"));

        var uri = AttachmentUriReference.Create("/agentprism", saved.Id);
        var message = new ChatMessage(ChatRole.User, [new UriContent(uri, "image/png")]);

        await foreach (var update in client.GetStreamingResponseAsync([message]))
        {
            _ = update;
        }

        inner.LastMessages.ShouldNotBeNull()[0].Contents.OfType<DataContent>().ShouldHaveSingleItem();
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = [.. messages];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = [.. messages];
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "tamam");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Sahte istemcinin serbest birakilacak kaynagi yok.
        }
    }
}
