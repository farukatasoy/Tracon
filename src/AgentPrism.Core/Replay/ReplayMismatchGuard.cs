using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Eslesmeyen bir tool cagrisini calistirma bittikten sonra gercek bir hataya
/// cevirir (Faz 47).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Bu sarmalayici, <c>FunctionInvokingChatClient</c>'in tool govdesinden
/// cikan istisnalari <strong>yutmasi</strong> yuzunden vardir (olculdu):
/// oynatici icinden firlatilan hata uca hic ulasmaz ve istek <c>200</c> doner.
/// Eslesmeme oynaticida kaydedilir, dongu kesilir ve hata BURADA — model
/// cagrisinin disinda — firlatilir.
/// </para>
/// <para>
/// Sarmalayici <see cref="RunRecordingAgent"/>'in <strong>icinde</strong>
/// durur: boylece istisna kayit sarmalayicisinin <c>catch</c> blokina duser,
/// <c>runs</c> satiri <c>Failed</c> olarak kapanir ve hata tipi kararli
/// <see cref="ReplayToolMismatchException.ReplayToolMismatchErrorType"/>
/// degerini tasir. Tersi sirada calistirma <c>Completed</c> gorunur ve kayit
/// sessizce yanlis olurdu.
/// </para>
/// </remarks>
internal sealed class ReplayMismatchGuard(AIAgent innerAgent, RecordedToolPlayback playback)
    : DelegatingAIAgent(innerAgent)
{
    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

        playback.ThrowIfMismatched();

        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base
            .RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }

        playback.ThrowIfMismatched();
    }
}
