using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="ChatMessage"/> listelerini <see cref="ContentGuardPipeline"/> uzerinden
/// gecirip maskelenmis bir kopyasini kuran paylasilan yardimci.
/// </summary>
/// <remarks>
/// <see cref="ContentGuardingChatClient"/> (modele giden istek, <see cref="MaskAsync"/>
/// ile <see cref="ContentGuardPipeline.InspectAsync"/> kullanir) ve
/// <see cref="RunRecordingAgent"/> (calistirma kaydi — <c>RunStarted</c> olayi ve
/// <see cref="IRunInputStore"/>, <see cref="PreviewAsync"/> ile
/// <see cref="ContentGuardPipeline.PreviewAsync"/> kullanir) AYNI mesaj-tarama
/// mantigini paylasir: ikisi de kayitli girdiyi modelin gordugu haliyle
/// esitlemek zorundadir. Guard sonucu ikisinde ayrilirsa maskelenen/engellenen
/// icerik kalici depoda ham kalir (HATA-S3-006).
/// </remarks>
internal static class ContentGuardMessageMasker
{
    /// <summary>
    /// Sistem talimati HARIC her mesaji verilen yonde denetler ve KARAR KAYDI
    /// YAPAR (<see cref="ContentGuardPipeline.InspectAsync"/>).
    /// </summary>
    /// <returns>Degisiklik yoksa cagiranin KENDI listesi doner (tahsissiz yol).</returns>
    public static ValueTask<IReadOnlyList<ChatMessage>> MaskAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        IReadOnlyList<ChatMessage> messages,
        string? modelId,
        CancellationToken cancellationToken)
        => RewriteAsync(
            messages,
            message => MaskMessageAsync(pipeline, direction, message, modelId, cancellationToken));

    /// <summary>
    /// Sistem talimati HARIC her mesaji verilen yonde denetler ama KARAR KAYDI
    /// YAPMAZ (<see cref="ContentGuardPipeline.PreviewAsync"/>) — bkz. o metodun
    /// belgesi: <c>RunRecordingAgent.BeginRunAsync</c>'in calistirma satiri henuz
    /// yokken guvenle cagirabildigi tek yol budur.
    /// </summary>
    /// <returns>Degisiklik yoksa cagiranin KENDI listesi doner (tahsissiz yol).</returns>
    public static ValueTask<IReadOnlyList<ChatMessage>> PreviewAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        IReadOnlyList<ChatMessage> messages,
        string? modelId,
        CancellationToken cancellationToken)
        => RewriteAsync(
            messages,
            message => PreviewMessageAsync(pipeline, direction, message, modelId, cancellationToken));

    private static async ValueTask<IReadOnlyList<ChatMessage>> RewriteAsync(
        IReadOnlyList<ChatMessage> messages,
        Func<ChatMessage, ValueTask<ChatMessage?>> rewriteAsync)
    {
        List<ChatMessage>? rebuilt = null;

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            // Sistem talimati bilerek atlanir (bkz. ContentGuardingChatClient).
            var replacement = message.Role == ChatRole.System
                ? null
                : await rewriteAsync(message).ConfigureAwait(false);

            if (replacement is null)
            {
                rebuilt?.Add(message);
                continue;
            }

            if (rebuilt is null)
            {
                rebuilt = new List<ChatMessage>(messages.Count);

                for (var earlier = 0; earlier < index; earlier++)
                {
                    rebuilt.Add(messages[earlier]);
                }
            }

            rebuilt.Add(replacement);
        }

        return rebuilt ?? messages;
    }

    private static async ValueTask<ChatMessage?> MaskMessageAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        ChatMessage message,
        string? modelId,
        CancellationToken cancellationToken)
        => await RewriteMessageAsync(
            message,
            text => pipeline.InspectAsync(direction, text, modelId, cancellationToken)).ConfigureAwait(false);

    private static async ValueTask<ChatMessage?> PreviewMessageAsync(
        ContentGuardPipeline pipeline,
        ContentGuardDirection direction,
        ChatMessage message,
        string? modelId,
        CancellationToken cancellationToken)
        => await RewriteMessageAsync(
            message,
            text => pipeline.PreviewAsync(direction, text, modelId, cancellationToken)).ConfigureAwait(false);

    private static async ValueTask<ChatMessage?> RewriteMessageAsync(
        ChatMessage message,
        Func<string, ValueTask<string?>> inspectAsync)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < message.Contents.Count; index++)
        {
            var content = message.Contents[index];

            if (ReadText(content) is not { Length: > 0 } text)
            {
                continue;
            }

            var rewritten = await inspectAsync(text).ConfigureAwait(false);

            if (rewritten is null)
            {
                continue;
            }

            contents ??= [.. message.Contents];
            contents[index] = WriteText(content, rewritten);
        }

        if (contents is null)
        {
            return null;
        }

        var clone = message.Clone();
        clone.Contents = contents;

        // 🚨 Ham gosterim BILEREK dusurulur — bkz. ContentGuardingChatClient
        // ayni gerekce (Faz 26).
        clone.RawRepresentation = null;

        return clone;
    }

    /// <summary>
    /// Denetlenebilir metni okur; denetlenemeyen icerik icin <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="FunctionResultContent"/> bilerek kapsanir: bir tool sonucu
    /// modelin gordugu icerigin parcasidir ve uzak bir MCP tool'unun dondurdugu
    /// zararli metin tam olarak buradan girer.
    /// </remarks>
    public static string? ReadText(AIContent content) => content switch
    {
        TextContent text => text.Text,
        FunctionResultContent { Result: string result } => result,
        FunctionResultContent { Result: { } result } => result.ToString(),
        _ => null,
    };

    public static AIContent WriteText(AIContent content, string text) => content switch
    {
        FunctionResultContent result => new FunctionResultContent(result.CallId, text) { Exception = result.Exception },
        _ => new TextContent(text),
    };
}
