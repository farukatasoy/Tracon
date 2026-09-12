using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Takes <c>data:</c> URIs embedded in the body of OpenAI-compatible endpoints
/// into the attachment table and replaces them with a small reference
/// (<see cref="UriContent"/>).
/// </summary>
/// <remarks>
/// MAF's own body parser (<c>OpenAIResponses.ToAgentRunRequest</c>) converts a
/// <c>data:</c> URI directly into a <see cref="DataContent"/>. If stored as
/// is, the message grows and gets embedded as base64 in the chat history. Each <see cref="DataContent"/>
/// is therefore converted into an attachment before being sent to the agent.
/// </remarks>
internal static class AttachmentIngestion
{
    /// <summary>
    /// Converts every <see cref="DataContent"/> in the given messages into an attachment.
    /// </summary>
    /// <returns>The reason to show the user if it failed; <see langword="null"/> if successful.</returns>
    public static async ValueTask<string?> ReplaceEmbeddedDataAsync(
        IList<ChatMessage> messages,
        string prefix,
        string tenantId,
        string? sessionId,
        string? createdBy,
        IAttachmentStore store,
        AttachmentTypeGuard guard,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is not DataContent data)
                {
                    continue;
                }

                var validation = guard.Validate(data.Data.Span);

                if (!validation.IsValid)
                {
                    return validation.Error;
                }

                var descriptor = await store.SaveAsync(
                    new AttachmentContent
                    {
                        TenantId = tenantId,
                        SessionId = sessionId,
                        FileName = data.Name is { Length: > 0 } name ? name : "upload",
                        MediaType = validation.MediaType!,
                        Data = data.Data,
                        CreatedBy = createdBy,
                    },
                    cancellationToken).ConfigureAwait(false);

                message.Contents[i] = new UriContent(
                    AttachmentUriReference.Create(prefix, descriptor.Id),
                    validation.MediaType!);
            }
        }

        return null;
    }
}
