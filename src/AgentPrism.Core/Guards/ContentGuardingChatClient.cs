using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Modele giden mesajlari ve modelden gelen yaniti kayitli
/// <see cref="IContentGuard"/> uygulamalarindan geciren dekorator.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Katman secimi olculdu ve plandan saptirildi.</strong> Bir
/// <c>IAgentDecorator</c> agent'in yalniz ilk girdisini ve son ciktisini gorur;
/// aradaki turlari gormez. Bir tool sonucu modele <em>ikinci</em> cagride girer ve
/// prompt injection'in en yaygin yolu budur. Bu yuzden guard <c>IChatClient</c>
/// katmanindadir.
/// </para>
/// <para>
/// 🚨 Ayrica <c>UseFunctionInvocation()</c>'in <strong>ICINDE</strong> durur.
/// Olculdu (Faz 48): tool cagri dongusu MAF'in <c>FunctionInvokingChatClient</c>'i
/// tarafindan surulur ve o dongunun her turu ayni ic istemciye gider. Dekorator
/// dongunun disinda olsaydi agent turu basina yalniz BIR cagri gorurdu ve tool
/// sonuclari hic denetlenmezdi. Boru hattinin tamami
/// <c>ModelProviderRegistry.CreateChatClient</c> icinde kurulur.
/// </para>
/// <para>
/// Devre kesici bu dekoratorun <em>disindadir</em>; engelleme kararinin devreyi
/// acmamasi <c>CircuitBreakingChatClient</c> icindeki acik bir ayiklamayla
/// saglanir (engelleme saglayici arizasi degildir — filtrelenmis yanitla ayni
/// gerekce).
/// </para>
/// <para>
/// <strong>Sistem talimati denetlenmez.</strong> Kodda veya yonetim API'sinde
/// yazilir, denetim izine zaten girer ve her cagride yeniden denetlemek sabit bir
/// maliyettir; hicbir sey yakalamaz.
/// </para>
/// </remarks>
internal sealed class ContentGuardingChatClient(
    ContentGuardPipeline pipeline,
    string? modelId,
    IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var settings = pipeline.Options;

        var outbound = settings.InspectInput
            ? await InspectInputAsync(messages, cancellationToken).ConfigureAwait(false)
            : messages;

        var response = await base.GetResponseAsync(outbound, options, cancellationToken).ConfigureAwait(false);

        if (settings.InspectOutput)
        {
            await InspectOutputAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = pipeline.Options;

        var outbound = settings.InspectInput
            ? await InspectInputAsync(messages, cancellationToken).ConfigureAwait(false)
            : messages;

        if (!settings.InspectOutput)
        {
            await foreach (var update in base.GetStreamingResponseAsync(outbound, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        if (!settings.BufferStreamingOutput)
        {
            // Cerceve cerceve denetim: kismi bir metin uzerinde desen eslesmeyebilir
            // ve bir desen iki cercevenin sinirinda KACAR. Bu yol acik bir tercihtir
            // (BufferStreamingOutput = false), gizli bir davranis degildir.
            await foreach (var update in base.GetStreamingResponseAsync(outbound, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return await MaskUpdateAsync(update, cancellationToken).ConfigureAwait(false) ?? update;
            }

            yield break;
        }

        // 🚨 Tamponlanmis yol: hicbir cerceve denetim bitmeden istemciye gitmez.
        // Bir cerceve gonderildikten sonra geri alinamaz; engelleme karari ancak
        // metnin tamami gorulduginde dogru verilebilir.
        var buffered = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(outbound, options, cancellationToken)
            .ConfigureAwait(false))
        {
            buffered.Add(update);
        }

        ApplyMask(buffered, await InspectBufferAsync(buffered, cancellationToken).ConfigureAwait(false));

        foreach (var update in buffered)
        {
            yield return update;
        }
    }

    /// <summary>
    /// Gonderilecek mesajlari denetler ve gerekiyorsa maskelenmis bir kopya kurar.
    /// </summary>
    /// <remarks>
    /// 🚨 Cagiranin listesi <strong>degistirilmez</strong>. Maskeleme modele giden
    /// istemi degistirir, konusma gecmisini degistirmez: gecmise yazilsaydi maske
    /// kalicilasir ve kullanicinin kendi yazdigi metin geri alinamaz sekilde
    /// kaybolurdu.
    /// </remarks>
    private ValueTask<IEnumerable<ChatMessage>> InspectInputAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        // Liste zaten bir IReadOnlyList ise kopyalanmaz: MAF mesajlari liste olarak
        // gecirir ve eslesme yoksa hicbir tahsis olmaz.
        var buffer = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        return MaskAsync(buffer, cancellationToken);
    }

    // RunRecordingAgent ile PAYLASILAN mantik: kayit yolu (RunStarted olayi,
    // IRunInputStore) ayni denetimi ayni sirada uygular ki modele giden ile
    // kaydedilen HIC ayrilmasin (HATA-S3-006).
    private async ValueTask<IEnumerable<ChatMessage>> MaskAsync(
        IReadOnlyList<ChatMessage> buffer,
        CancellationToken cancellationToken)
        => await ContentGuardMessageMasker
            .MaskAsync(pipeline, ContentGuardDirection.Input, buffer, modelId, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Yaniti denetler ve gerekiyorsa maskelenmis mesajlarla degistirir.
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="ChatMessage"/> nesneleri <strong>yerinde degistirilmez</strong>.
    /// Ic istemci ayni ornegi yeniden kullanabilir (onbellekleyen bir istemci veya
    /// onceden kurulmus bir sahte istemci); yerinde degistirme o ornegi kalici
    /// olarak bozar ve ikinci cagri maskelenmis metni "modelin yaniti" sanir.
    /// Yalniz <see cref="ChatResponse.Messages"/> listesinin kendisi bizimdir.
    /// </remarks>
    private async ValueTask InspectOutputAsync(ChatResponse response, CancellationToken cancellationToken)
    {
        var changed = false;

        for (var index = 0; index < response.Messages.Count; index++)
        {
            if (await MaskOutputAsync(response.Messages[index], cancellationToken).ConfigureAwait(false)
                is { } replacement)
            {
                response.Messages[index] = replacement;
                changed = true;
            }
        }

        if (changed)
        {
            // Ayni gerekce: ham yanit maskelenmemis metni tasir.
            response.RawRepresentation = null;
        }
    }

    private async ValueTask<ChatMessage?> MaskOutputAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < message.Contents.Count; index++)
        {
            var content = message.Contents[index];

            if (ContentGuardMessageMasker.ReadText(content) is not { Length: > 0 } text)
            {
                continue;
            }

            var masked = await pipeline
                .InspectAsync(ContentGuardDirection.Output, text, modelId, cancellationToken)
                .ConfigureAwait(false);

            if (masked is null)
            {
                continue;
            }

            contents ??= [.. message.Contents];
            contents[index] = ContentGuardMessageMasker.WriteText(content, masked);
        }

        if (contents is null)
        {
            return null;
        }

        var clone = message.Clone();
        clone.Contents = contents;
        clone.RawRepresentation = null;

        return clone;
    }

    /// <summary>
    /// Tek bir cerceveyi denetler; maskeleme gerekiyorsa yeni bir cerceve doner.
    /// </summary>
    /// <returns>Degisiklik yoksa <see langword="null"/>.</returns>
    private async ValueTask<ChatResponseUpdate?> MaskUpdateAsync(
        ChatResponseUpdate update,
        CancellationToken cancellationToken)
    {
        List<AIContent>? contents = null;

        for (var index = 0; index < update.Contents.Count; index++)
        {
            var content = update.Contents[index];

            if (ContentGuardMessageMasker.ReadText(content) is not { Length: > 0 } text)
            {
                continue;
            }

            var masked = await pipeline
                .InspectAsync(ContentGuardDirection.Output, text, modelId, cancellationToken)
                .ConfigureAwait(false);

            if (masked is null)
            {
                continue;
            }

            contents ??= [.. update.Contents];
            contents[index] = ContentGuardMessageMasker.WriteText(content, masked);
        }

        if (contents is null)
        {
            return null;
        }

        var clone = update.Clone();
        clone.Contents = contents;
        clone.RawRepresentation = null;

        return clone;
    }

    /// <summary>
    /// Tamponlanmis cercevelerin BIRLESIK metnini denetler.
    /// </summary>
    /// <returns>
    /// Maskelenmis birlesik metin; degisiklik yoksa <see langword="null"/>.
    /// </returns>
    private async ValueTask<string?> InspectBufferAsync(
        List<ChatResponseUpdate> buffered,
        CancellationToken cancellationToken)
    {
        StringBuilder? joined = null;

        foreach (var update in buffered)
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent { Text.Length: > 0 } text)
                {
                    (joined ??= new StringBuilder()).Append(text.Text);
                }
            }
        }

        return joined is null
            ? null
            : await pipeline
                .InspectAsync(ContentGuardDirection.Output, joined.ToString(), modelId, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Maskelenmis birlesik metni cerceve dizisine geri yazar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Maskeleme karakter sayisini degistirebildigi icin eslesmeler tek tek
    /// cercevelere geri haritalanamaz. Butun metin <strong>ilk</strong> metin
    /// cercevesine yazilir, kalan metin cerceveleri bosaltilir; metin disi
    /// icerikler (tool cagrisi, kullanim sayaci) yerinde kalir. Toplam metin
    /// korunur, kayit ve olculer bozulmaz.
    /// </para>
    /// <para>
    /// 🚨 Cerceveler <strong>kopyalanir</strong>, yerinde degistirilmez: ic
    /// istemci ayni ornekleri yeniden kullanabilir.
    /// </para>
    /// </remarks>
    private static void ApplyMask(List<ChatResponseUpdate> buffered, string? masked)
    {
        if (masked is null)
        {
            return;
        }

        var written = false;

        for (var position = 0; position < buffered.Count; position++)
        {
            var update = buffered[position];
            List<AIContent>? contents = null;

            for (var index = 0; index < update.Contents.Count; index++)
            {
                if (update.Contents[index] is not TextContent { Text.Length: > 0 })
                {
                    continue;
                }

                contents ??= [.. update.Contents];
                contents[index] = new TextContent(written ? string.Empty : masked);
                written = true;
            }

            if (contents is null)
            {
                continue;
            }

            var clone = update.Clone();
            clone.Contents = contents;
            clone.RawRepresentation = null;
            buffered[position] = clone;
        }
    }
}
