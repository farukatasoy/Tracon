using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Belge yukleme, anlamsal arama ve kaynak yonetimi icin yonetim (operator) yuzeyi.
/// </summary>
/// <remarks>
/// <para>
/// Agent'in kendi isi degildir — bkz. <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>, 51.6.
/// Agent tarafi <see cref="VectorSearchToolFactory"/>'nin urettigi <c>search_knowledge</c>
/// tool'udur.
/// </para>
/// <para>
/// <see cref="IsSupported"/> <see langword="false"/> iken her metot
/// <see cref="AgentPrismException"/> firlatir; sessizce bos sonuc donmez (K1).
/// </para>
/// </remarks>
public sealed partial class KnowledgeIngestionService
{
    private readonly ITenantContext _tenantContext;
    private readonly AgentPrismKnowledgeOptions _options;
    private readonly IVectorSearchStore? _store;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddings;

    /// <summary>Yeni bir bilgi tabani servisi olusturur.</summary>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">Bilgi tabani ayarlari.</param>
    /// <param name="store">
    /// Vektor deposu. <see langword="null"/> ise <see cref="IsSupported"/>
    /// <see langword="false"/> olur (K4: varsayilan uygulama yok).
    /// </param>
    /// <param name="embeddings">
    /// Gomu ureticisi. <see langword="null"/> ise <see cref="IsSupported"/>
    /// <see langword="false"/> olur; tuketici kendi saglayicisini kaydetmelidir.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public KnowledgeIngestionService(
        ITenantContext tenantContext,
        IOptions<AgentPrismKnowledgeOptions> options,
        IVectorSearchStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);

        _tenantContext = tenantContext;
        _options = options.Value;
        _store = store;
        _embeddings = embeddings;
    }

    /// <summary>
    /// Bu kurulumda bilgi tabani destekleniyor mu.
    /// </summary>
    /// <remarks>
    /// Yalniz <see cref="IVectorSearchStore"/> (bugun: yalniz PostgreSQL) VE bir
    /// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> birlikte
    /// kayitliyken <see langword="true"/>.
    /// </remarks>
    public bool IsSupported => _store is not null && _embeddings is not null;

    /// <summary>
    /// Bir belgeyi yukler: parcalar (verilmemisse), gomuler (verilmemisse) ve yazar.
    /// </summary>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="sourceId">Kaynak kimligi. Ayni kimlikle yeniden yukleme eskiyi degistirir.</param>
    /// <param name="text">
    /// Ham metin. Verilirse <see cref="AgentPrismKnowledgeOptions.ChunkSize"/> ve
    /// <see cref="AgentPrismKnowledgeOptions.ChunkOverlap"/> ile parcalanir ve her
    /// parca gomulur. <paramref name="chunks"/> ile birlikte verilemez.
    /// </param>
    /// <param name="chunks">
    /// Hazir parcalar. <see cref="VectorChunk.Embedding"/> bos birakilan parcalar
    /// burada gomulur; doldurulmus olanlar OLDUGU GIBI yazilir (tuketici kendi
    /// gomusunu getirebilir). <paramref name="text"/> ile birlikte verilemez.
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yazilan parca sayisi.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> <see langword="false"/> ise.</exception>
    /// <exception cref="ArgumentException">
    /// Ne <paramref name="text"/> ne <paramref name="chunks"/> verilmisse, ikisi birden
    /// verilmisse, veya bir parcanin gomu uzunlugu depo boyutuyla eslesmiyorsa.
    /// </exception>
    public async ValueTask<int> IngestAsync(
        string collection,
        string sourceId,
        string? text,
        IReadOnlyList<VectorChunk>? chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        RequireSupported();

        if (string.IsNullOrEmpty(text) == (chunks is null or []))
        {
            throw new ArgumentException(
                $"Either {nameof(text)} or {nameof(chunks)} must be given; not both, and not neither.",
                nameof(text));
        }

        var prepared = text is not null
            ? await EmbedTextAsync(text, cancellationToken).ConfigureAwait(false)
            : await EmbedMissingAsync(chunks!, cancellationToken).ConfigureAwait(false);

        foreach (var chunk in prepared)
        {
            if (chunk.Embedding.Length != _store!.Dimensions)
            {
                throw new ArgumentException(
                    $"Chunk {chunk.Index} embedding length ({chunk.Embedding.Length}) does not match " +
                    $"the store dimension ({_store.Dimensions}).",
                    nameof(chunks));
            }
        }

        await _store!.UpsertAsync(_tenantContext.TenantId, collection, sourceId, prepared, cancellationToken)
            .ConfigureAwait(false);

        return prepared.Count;
    }

    /// <summary>Bir sorgu metnini gomup en yakin parcalari dondurur.</summary>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="query">Sorgu metni.</param>
    /// <param name="top">Kac sonuc dondurulecegi. Verilmezse <see cref="AgentPrismKnowledgeOptions.MaxResults"/>.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Mesafeye gore artan sirali sonuclar.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> <see langword="false"/> ise.</exception>
    public async ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        string collection,
        string query,
        int? top = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        RequireSupported();

        var queryEmbedding = await GenerateAsync(query, cancellationToken).ConfigureAwait(false);

        return await _store!.SearchAsync(
            new VectorSearchRequest
            {
                TenantId = _tenantContext.TenantId,
                Collection = collection,
                QueryEmbedding = queryEmbedding,
                Top = top ?? _options.MaxResults,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bir kaynagin tum parcalarini siler.</summary>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="sourceId">Silinecek kaynak kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silinen parca sayisi.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> <see langword="false"/> ise.</exception>
    public ValueTask<int> DeleteSourceAsync(string collection, string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        RequireSupported();

        return _store!.DeleteSourceAsync(_tenantContext.TenantId, collection, sourceId, cancellationToken);
    }

    /// <summary>Bir koleksiyondaki tum kaynaklari listeler.</summary>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaynak kimlikleri.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> <see langword="false"/> ise.</exception>
    public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        RequireSupported();

        return _store!.ListSourcesAsync(_tenantContext.TenantId, collection, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<VectorChunk>> EmbedTextAsync(string text, CancellationToken cancellationToken)
    {
        var pieces = TextChunker.Split(text, _options.ChunkSize, _options.ChunkOverlap);

        if (pieces.Count == 0)
        {
            return [];
        }

        var generated = await _embeddings!.GenerateAsync(pieces, options: null, cancellationToken).ConfigureAwait(false);
        var result = new List<VectorChunk>(pieces.Count);

        for (var index = 0; index < pieces.Count; index++)
        {
            result.Add(new VectorChunk
            {
                Index = index,
                Content = pieces[index],
                Embedding = generated[index].Vector,
            });
        }

        return result;
    }

    private async ValueTask<IReadOnlyList<VectorChunk>> EmbedMissingAsync(
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken)
    {
        var toEmbed = new List<int>();

        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Embedding.IsEmpty)
            {
                toEmbed.Add(i);
            }
        }

        if (toEmbed.Count == 0)
        {
            return chunks;
        }

        var generated = await _embeddings!
            .GenerateAsync(toEmbed.Select(i => chunks[i].Content), options: null, cancellationToken)
            .ConfigureAwait(false);

        var result = chunks.ToArray();

        for (var i = 0; i < toEmbed.Count; i++)
        {
            var chunk = result[toEmbed[i]];
            result[toEmbed[i]] = chunk with { Embedding = generated[i].Vector };
        }

        return result;
    }

    private async ValueTask<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        var generated = await _embeddings!.GenerateAsync([text], options: null, cancellationToken).ConfigureAwait(false);
        return generated[0].Vector;
    }

    private void RequireSupported()
    {
        if (!IsSupported)
        {
            throw new AgentPrismException(
                "Knowledge base not supported: an IVectorSearchStore (today only PostgreSQL, " +
                "UsePostgreSql()) AND an IEmbeddingGenerator<string, Embedding<float>> must both " +
                "be registered.");
        }
    }

    /// <summary>
    /// Koleksiyon adinin guvenli bir tanimlayici oldugunu dogrular. Ad sorguya
    /// PARAMETRE olarak gecer (SQL enjeksiyonu yolu degildir) ama arayuzden
    /// gelebilecegi icin serbest metin olarak KABUL EDILMEZ.
    /// </summary>
    /// <exception cref="ArgumentException">Ad desene uymuyorsa.</exception>
    private static void RequireValidCollectionName(string collection)
    {
        if (!ValidCollectionName().IsMatch(collection))
        {
            throw new ArgumentException(
                $"'{collection}' is not a valid collection name. It may only contain letters, digits, " +
                "underscores, and hyphens.",
                nameof(collection));
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ValidCollectionName();
}
