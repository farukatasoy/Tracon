using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Attachments;

/// <summary>
/// Verifies that <see cref="AttachmentResolvingChatClient"/> resolves attachment
/// references to their actual content right before the model call.
/// </summary>
public sealed class AttachmentResolvingChatClientTests
{
    [Fact]
    public async Task Attachment_reference_resolves_to_actual_content()
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
        var message = new ChatMessage(ChatRole.User, [new TextContent("look"), new UriContent(uri, "image/png")]);

        await client.GetResponseAsync([message]);

        var resolvedContents = inner.LastMessages.ShouldNotBeNull()[0].Contents;
        resolvedContents.OfType<TextContent>().ShouldHaveSingleItem();

        var dataContent = resolvedContents.OfType<DataContent>().ShouldHaveSingleItem();
        dataContent.Data.ToArray().ShouldBe(new byte[] { 1, 2, 3 });
        dataContent.MediaType.ShouldBe("image/png");
    }

    [Fact]
    public async Task Message_without_attachments_passes_through_unchanged()
    {
        var store = new InMemoryAttachmentStore();
        var inner = new CapturingChatClient();
        var client = new AttachmentResolvingChatClient(inner, store, new FixedTenantContext("tenant-a"));

        var message = new ChatMessage(ChatRole.User, "hello");

        await client.GetResponseAsync([message]);

        inner.LastMessages.ShouldNotBeNull()[0].ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Missing_or_other_tenants_attachment_throws()
    {
        var store = new InMemoryAttachmentStore();
        var inner = new CapturingChatClient();
        var client = new AttachmentResolvingChatClient(inner, store, new FixedTenantContext("tenant-a"));

        var uri = AttachmentUriReference.Create("/agentprism", Guid.NewGuid());
        var message = new ChatMessage(ChatRole.User, [new UriContent(uri, "image/png")]);

        await Should.ThrowAsync<AgentPrismException>(async () => await client.GetResponseAsync([message]));
    }

    [Fact]
    public async Task Streaming_call_also_resolves()
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
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = [.. messages];
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // The fake client has no resources to release.
        }
    }
}
