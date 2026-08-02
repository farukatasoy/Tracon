using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// OpenAI uyumlu uclarda govdeye gomulu <c>data:</c> URI'lerini ek tablosuna
/// alir ve yerine kucuk bir referans (<see cref="UriContent"/>) koyar.
/// </summary>
/// <remarks>
/// MAF'in kendi govde cozumleyicisi (<c>OpenAIResponses.ToAgentRunRequest</c>)
/// bir <c>data:</c> URI'sini dogrudan <see cref="DataContent"/>'e cevirir. Bu
/// haliyle saklansa mesaj buyur ve sohbet gecmisine base64 gomulur (bkz.
/// <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1). Bu yuzden agent'a
/// gonderilmeden once her <see cref="DataContent"/> bir ege cevrilir.
/// </remarks>
internal static class AttachmentIngestion
{
    /// <summary>
    /// Verilen mesajlardaki her <see cref="DataContent"/>'i bir ege cevirir.
    /// </summary>
    /// <returns>Basarisizsa kullaniciya gosterilecek gerekce; basarili ise <see langword="null"/>.</returns>
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
