# Faz 51 — Vektör Bellek ve RAG (`pgvector`)

> **Durum:** ✅ Tamamlandı (2026-08-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-30**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`
> **Yeni paket:** Yok · **Yeni NuGet:** 🚨 **Yok** — gerekçe [51.2](#512--sıfır-yeni-paket-ölçülmüş-gerekçe) · **Migration:** **gerekli** — PostgreSQL'de bir tablo + uzantı; SQL Server ve SQLite'ta **yok**
> **Public API:** büyüyor — bir arayüz, üç kayıt tipi, iki ayar. Faz 7'den önce ucuz

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-007\|K-018\|K-104\|K-105\|K-110\|K-178\|K-181\|K-205\|K-211" docs/KARARLAR.md
   ```
   🚨 **K-105** (`ChatHistoryMemoryProvider` Faz 13 kapsamı dışında bırakıldı;
   yeniden açılma koşulu "bir `VectorStore` implementasyonu ve embedding
   sağlayıcısı seçildiğinde" — [51.5](#515--chathistorymemoryprovider-yine-bağlanamıyor--ölçülmüş-sebep)
   bu koşulun **karşılanmadığını** ölçümle gösterir),
   🚨 **K-211** (sürüm kayması gerçek bir çalışma anı hatasıdır — bu fazın
   paket kararının kaynağı), **K-205** (geçişli ağırlık nasıl tartılır),
   **K-007** (geçişli sabitleme kapalı), **K-110** (`TextSearchProvider` kod
   değişmeden kalıcı depoya döner), **K-181** (`AgentPrism.SqlServer` AOT
   uyumlu **değildir**; PostgreSql **uyumludur**), **K-178** (migration
   numaraları sağlayıcı başına bağımsız), **K-018** (bellek içi uygulama
   birinci sınıftır).
3. [`13-BAGLAM-SIKISTIRMA-VE-BELLEK.md`](13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) — yalnız bellek
   sağlayıcıları bölümü ve devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/13-BAGLAM-SIKISTIRMA-VE-BELLEK.md
   ```
4. [`23-SQL-SERVER.md`](23-SQL-SERVER.md) — yalnız paylaşılan depo modeli
   bölümü. 🚨 Bu faz o modeli **bilinçli olarak kısmen kırar**; nasıl
   kırıldığını anlamak için önce nasıl kurulduğunu bilmek gerekir.
5. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/postgresql.md`](../../hafiza/postgresql.md) (**ana kaynak** — uzantı,
   indeks, `Npgsql` sürümü),
   [`hafiza/sql-saglayicilari.md`](../../hafiza/sql-saglayicilari.md)
   (🚨 paylaşılan depo modeli ve bu fazın onu nasıl böldüğü),
   [`hafiza/build-ve-analyzer.md`](../../hafiza/build-ve-analyzer.md)
   (🚨 `AgentPrism.PostgreSql` **AOT uyumludur** ve öyle kalmalıdır)
6. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](../../MIMARI.md) — veri modeli bölümü

---

## Amaç

AgentPrism'de anlamsal arama yoktur. Bir agent'a doküman verip "buna göre
cevapla" demenin yolu yoktur; dosya belleği vardır ama araması **regex**'tir ve
her çağrıda **bütün dosyaları belleğe alır**.

Bu faz `pgvector` ile anlamsal aramayı getirir ve dosya aramasının O(n)
davranışını düzeltir. **PostgreSQL ile sınırlıdır** ve bu bir karardır —
gerekçe [51.3](#513--neden-yalnız-postgresql)'te yazılıdır.

- **F-30** — `pgvector` uzantısı, gömü (embedding) deposu, anlamsal arama
  tool'u ve dosya aramasının SQL'e indirilmesi.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`SqlAgentFileStore.cs:156`](../../../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs) | 🚨 `SearchAsync` her çağrıda `LoadAllAsync` yapıyor — **tüm dosyalar belleğe alınıyor** — sonra her birine `new Regex(...)` uygulanıyor |
| [`SqlAgentFileStore.cs:118`](../../../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs) | 🚨 Aynı sorun `ListChildrenAsync`'te de var: `LoadAllAsync` + bellekte önek süzme. **İki metot**, tek desen |
| [`agent_files`](../../../src/AgentPrism.PostgreSql/Migrations/0006_attachments.sql) | `content text` sütunu; içerik üzerinde **hiçbir indeks yok** |
| [`MemorySettings.cs:7`](../../../src/AgentPrism.Abstractions/Agents/MemorySettings.cs) | "Vektor tabanli anlamsal bellek (MAF'in `ChatHistoryMemoryProvider`'i) **bilerek burada yoktur**" — K-105 |
| `grep -rn "VectorStore" src/ --include="*.cs"` | **Tek sonuç** ve o da bir XML doküman satırı. Somut uygulama yok |
| [`Directory.Packages.props`](../../../Directory.Packages.props) | `Npgsql` **10.0.3**. Vektör paketlerinin sürüm kayması buna göre ölçülür |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

🚨 **Aday listesinin bir iddiası düzeltilmelidir.** Liste "vektör destekli
`AgentFileStore.SearchAsync`" diyordu. Ölçüldü (MAF 1.16.0):

```
AgentFileStore.SearchAsync(String directory, String regexPattern,
                           String globPattern, Boolean recursive, CancellationToken)
```

**İkinci parametrenin adı `regexPattern`'dır.** MAF'ın sözleşmesi bir düzenli
ifade bekler; anlamsal bir sorgu o imzadan geçirilemez. Bu yüzden faz iki
**ayrı** işe bölünür ve ikisi karıştırılmaz:

| İş | Ne | Nerede |
|---|---|---|
| **A** | Regex aramasını SQL'e indir — O(n) sorunu | `AgentFileStore.SearchAsync` (MAF sözleşmesi korunur) |
| **B** | Anlamsal arama — yeni yetenek | **Yeni** bir AgentPrism yüzeyi ve bir tool |

---

## 51.1 — İki ayrı iş

```mermaid
flowchart TD
    subgraph "IS A - performans duzeltmesi"
        A1["AgentFileStore.SearchAsync<br/>MAF sozlesmesi: regexPattern"] --> A2["LoadAllAsync KALDIRILIR"]
        A2 --> A3["onek + glob SQL'e iner<br/>UC SAGLAYICIDA"]
        A3 --> A4["PostgreSQL: regex de SQL'e iner<br/>~ operatoru"]
    end

    subgraph "IS B - yeni yetenek"
        B1["search_knowledge tool'u"] --> B2["IEmbeddingGenerator<br/>tuketici kaydeder"]
        B2 --> B3["IVectorSearchStore"]
        B3 --> B4["PgVectorSearchStore<br/>YALNIZ PostgreSQL"]
    end

    classDef yeni fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    class B1,B2,B3,B4 yeni
```

**İş A üç sağlayıcıda da çalışır.** Önek ve glob süzgeci SQL'e inince
`LoadAllAsync` kalkar; PostgreSQL ayrıca `~` (POSIX regex) operatörüyle regex'i
de indirir. SQL Server ve SQLite'ta yerleşik regex yoktur; orada süzülmüş
küme bellekte regex'ten geçer — ama küme artık **tüm dosyalar** değildir.

**İş B yalnız PostgreSQL'dedir.**

## 51.2 — Sıfır yeni paket: ölçülmüş gerekçe

Üç aday ölçüldü (2026-08-06, `dotnet restore` + `project.assets.json`):

| Aday | Sürüm | Geçişli | 🚨 Sorun |
|---|---|---|---|
| `Microsoft.SemanticKernel.Connectors.PgVector` | **1.74.0-preview** | 7 | **Yalnız ön sürüm.** `Npgsql 8.0.7`'ye karşı derlenmiş; bizde **10.0.3**. Ayrıca `Microsoft.Extensions.AI.Abstractions 10.4.0` (bizde 10.8.3) |
| `Pgvector` | 0.3.2 (GA) | 3 | `Npgsql 8.0.5`'e karşı derlenmiş. **Aynı sürüm kayması** |
| `Microsoft.Extensions.VectorData.Abstractions` | 10.8.0 (GA) | 4 | Ağırlık sorun değil; ama tek başına işe yaramaz ([51.5](#515--chathistorymemoryprovider-yine-bağlanamıyor--ölçülmüş-sebep)) |

🚨 **`Npgsql` sürüm kayması bu depoda ÖLÇÜLMÜŞ bir hata sınıfıdır.** K-211:
`Azure.AI.OpenAI` 2.1.0, `OpenAI` 2.1.0'a karşı derlenmişti; biz 2.12.0
kullanıyorduk ve ilgili uzantıların **tamamı** çalışma anında
`MissingMethodException` verdi. Derleme yeşildi. Aynı desenin `Npgsql` 8 → 10
sıçramasında tekrarlanmayacağının garantisi yoktur.

**Karar: hiçbiri alınmaz.** `pgvector` metin biçimini kullanır:

```sql
-- yazma: gomu bir METIN parametresi olarak gonderilir ve SQL'de cast edilir
INSERT INTO {schema}.document_embeddings (..., embedding)
VALUES (..., @embedding::vector);

-- okuma: mesafe bir double olarak doner; ozel bir tip esleyici GEREKMEZ
SELECT id, source_id, chunk_index, content,
       embedding <=> @query::vector AS distance
  FROM {schema}.document_embeddings
 WHERE tenant_id = @tenant_id AND collection = @collection
 ORDER BY embedding <=> @query::vector
 LIMIT @top;
```

Kazanç üç katlıdır:

| Kazanç | Ayrıntı |
|---|---|
| **Sıfır yeni bağımlılık** | K-007 gerekçesi bile gerekmez |
| 🚨 **AOT duruşu korunur** | `AgentPrism.PostgreSql` AOT uyumludur. Tip eşleyici eklentileri yansımaya dayanabilir; metin biçimi dayanmaz |
| Sürüm kayması yok | Yalnız bizim `Npgsql` 10.0.3'ümüz kullanılır |

**Bedeli:** gömü metin olarak serileştirilir ve ikili biçimden biraz daha çok
bayt taşır. 🚨 **Bu maliyet ölçülmelidir** ve DoD'dedir; ölçüm ikili biçimi
haklı çıkarırsa karar yeniden değerlendirilir.

## 51.3 — Neden yalnız PostgreSQL

Faz 23 üç sağlayıcı için **paylaşılan** bir depo modeli kurdu:
`AgentPrism.Sql.Shared` içindeki bir `Sql*Store`, üç diyalektte aynı sorgu
ailesini çalıştırır. Vektör araması bu modeli **kırar**:

| Sağlayıcı | Vektör yolu | Durum |
|---|---|---|
| PostgreSQL | `pgvector` uzantısı, `vector` tipi, HNSW indeksi | Olgun, yaygın |
| SQL Server | Yerel `VECTOR` tipi | 🚨 **Ölçülmedi.** Sürüm ve sürücü desteği doğrulanmalı |
| SQLite | `sqlite-vec` uzantısı | 🚨 **Yerel kütüphane** ister. `SQLitePCLRaw` paketimiz onu taşımıyor |

**Karar (kullanıcı kararı, 2026-08-06): yalnız PostgreSQL.**

Sonuçlar açıkça yazılır ve sessiz bırakılmaz:

1. `IVectorSearchStore` **Abstractions**'a girer, **varsayılan uygulama yoktur**
   (K4). SQL Server ve SQLite tüketicisi kendi uygulamasını kaydedebilir.
2. 🚨 **`EnableVectorSearch = true` ile PostgreSQL olmayan bir kurulum
   açılışta HATA verir.** Sessizce boş sonuç dönmez — kullanıcı arama
   çalışıyor sanmamalıdır. Bu, K-034'ün üç kez tekrarladığı kuralın aynısıdır.
3. `document_embeddings` tablosu yalnız PostgreSQL migration setinde vardır.
   SQL Server ve SQLite setleri bu fazda **hiç değişmez**.

## 51.4 — Gömü sağlayıcısı tüketiciden gelir

AgentPrism bir embedding modeli **seçmez** ve bir sağlayıcı SDK'sı **almaz**.

```csharp
// tuketicinin kurulumu
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OpenAIClient(apiKey).GetEmbeddingClient("text-embedding-3-small")
        .AsIEmbeddingGenerator());
```

Ölçülen imza (`Microsoft.Extensions.AI.Abstractions` 10.8.3 — **zaten
bağımlılıkta**):

```
IEmbeddingGenerator`2 : IEmbeddingGenerator, IDisposable
    virtual Task<GeneratedEmbeddings<TEmbedding>> GenerateAsync(
        IEnumerable<TInput> values, EmbeddingGenerationOptions options, CancellationToken ct)

Embedding`1 : Embedding  [sealed]
    prop Int32 Dimensions { get; }
    prop ReadOnlyMemory<T> Vector { get; set; }
```

Gerekçe K-032 ile aynıdır: model adları NuGet yayın hızından hızlı değişir.
Ayrıca gömü modeli seçimi **boyutu** belirler ve boyut şemaya girer.

🚨 **Boyut kurulum anında sabitlenir ve DEĞİŞTİRİLEMEZ.** `vector(1536)`
sütunu bir boyut taşır; modeli değiştirmek tüm gömüleri geçersiz kılar. Ayar
bunu açıkça söyler ve boyut uyuşmazlığı **yazma anında** hata verir; sessizce
yanlış sonuç döndürmez.

## 51.5 — `ChatHistoryMemoryProvider` yine bağlanamıyor — ölçülmüş sebep

K-105'in yeniden açılma koşulu şuydu: *"Bir `VectorStore` implementasyonu ve
embedding sağlayıcısı seçildiğinde."* Embedding sağlayıcısı çözüldü
([51.4](#514--gömü-sağlayıcısı-tüketiciden-gelir)). `VectorStore` **çözülmedi**
ve sebebi ölçüldü.

`Microsoft.Extensions.VectorData.Abstractions` 10.8.0:

```
VectorStore  [abstract]
    virtual VectorStoreCollection<TKey,TRecord> GetCollection(String name, VectorStoreCollectionDefinition definition)

VectorStoreCollection`2  [abstract]
    virtual IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord,Boolean>> filter, Int32 top,
        FilteredRecordRetrievalOptions<TRecord> options, CancellationToken ct)
```

🚨 **`Expression<Func<TRecord,bool>>` bir ifade ağacıdır ve SQL'e çevrilmesi
gerekir.** İki sonuç:

1. İfade ağacı yorumlamak `[RequiresDynamicCode]` sınıfına girer.
   **`AgentPrism.PostgreSql` AOT uyumludur** ve öyle kalmalıdır; bu kod oraya
   giremez.
2. Bir LINQ→SQL çevirici yazmak, bir ORM yazmaktır. Aday listesinin
   "Bilerek Önerilmeyenler" tablosu bu sınıfı zaten reddediyor: *"Kendi vektör
   veritabanımızı yazmak — depolama motoru yazmak kütüphane sınırının
   dışındadır."*

**Sonuç: K-105 bu faz için açık kalır.** `ChatHistoryMemoryProvider`
bağlanmaz. Faz, ihtiyacın **kendisini** karşılar (anlamsal arama) ama MAF'ın o
belirli sağlayıcısını bağlamaz. Bu bir eksiklik değil, ölçülmüş bir sınırdır ve
K-105'in metni kapanışta bu ölçümle **güncellenmelidir**.

## 51.6 — Anlamsal arama nasıl kullanılır

Yeni bir tool: `search_knowledge`. K2 korunur — tool **kodda** tanımlıdır;
arayüzden yazılamaz. Agent tanımından yalnız **açılır**:

```csharp
public sealed record MemorySettings
{
    public bool EnableFileMemory { get; init; }
    public bool EnableTodo { get; init; }
    public bool EnableTextSearch { get; init; }

    /// <summary>Anlamsal arama tool'unu acar. 🚨 Yalnizca PostgreSQL.</summary>
    public bool EnableVectorSearch { get; init; }

    /// <summary>Aranacak koleksiyon adi. Bos ise agent adi kullanilir.</summary>
    public string? VectorCollection { get; init; }
}
```

Belge yükleme bir **yönetim** işidir, agent'ın işi değil:

```mermaid
flowchart LR
    D["POST /api/knowledge/{koleksiyon}/documents"] --> C["parcalama (chunking)"]
    C --> E["IEmbeddingGenerator"]
    E --> S["document_embeddings"]
    S --> T["search_knowledge tool'u"]
    T --> A["agent"]

    classDef yeni fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    class D,C,E,S,T yeni
```

**Parçalama basittir ve bilinçli olarak öyledir:** sabit uzunlukta, örtüşmeli.
Akıllı parçalama (başlığa göre, anlama göre) ayrı bir uzmanlıktır ve tüketici
kendi parçalarını `chunks` alanıyla doğrudan gönderebilir.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions/Knowledge/IVectorSearchStore.cs (YENI)

/// <summary>
/// Gomu (embedding) tabanli anlamsal arama deposu.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Varsayilan uygulama YOKTUR</strong> (K4). Tek somut uygulama
/// <c>AgentPrism.PostgreSql</c> icindedir ve <c>pgvector</c> uzantisini ister.
/// SQL Server ve SQLite tuketicisi kendi uygulamasini kaydedebilir.
/// </para>
/// <para>
/// Bu arayuz <c>Microsoft.Extensions.VectorData.VectorStore</c>'u SARMALAMAZ.
/// Olculdu (10.8.0): o tip <c>Expression&lt;Func&lt;TRecord,bool&gt;&gt;</c>
/// suzgeci ister; ifade agaci cevirmek AOT duruşunu bozar ve
/// <c>AgentPrism.PostgreSql</c> AOT uyumludur.
/// </para>
/// </remarks>
public interface IVectorSearchStore
{
    /// <summary>Deponun beklediği gomu boyutu.</summary>
    int Dimensions { get; }

    /// <summary>Parcalari gomuleriyle birlikte yazar. Ayni kaynak yeniden yazilirsa eskiler silinir.</summary>
    ValueTask UpsertAsync(
        string tenantId,
        string collection,
        string sourceId,
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Gomuye en yakin parcalari dondurur.</summary>
    ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kaynagin tum parcalarini siler.</summary>
    ValueTask<int> DeleteSourceAsync(
        string tenantId,
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default);
}

/// <summary>Yazilacak tek bir parca.</summary>
public sealed record VectorChunk
{
    public required int Index { get; init; }
    public required string Content { get; init; }

    /// <summary>
    /// Parcanin gomusu. 🚨 Uzunlugu <see cref="IVectorSearchStore.Dimensions"/>
    /// ile AYNI olmalidir; degilse yazma hata verir.
    /// </summary>
    public required ReadOnlyMemory<float> Embedding { get; init; }

    /// <summary>Istege bagli ustveri. Suzme icin kullanilabilir.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Arama istegi.</summary>
public sealed record VectorSearchRequest
{
    public required string TenantId { get; init; }
    public required string Collection { get; init; }
    public required ReadOnlyMemory<float> QueryEmbedding { get; init; }

    /// <summary>Kac sonuc dondurulur.</summary>
    public int Top { get; init; } = 5;

    /// <summary>
    /// En buyuk kabul edilen kosinus mesafesi (0 = ayni, 2 = zit).
    /// <see langword="null"/> ise mesafe suzgeci uygulanmaz.
    /// </summary>
    public double? MaxDistance { get; init; }
}

/// <summary>Bir arama sonucu.</summary>
public sealed record VectorSearchHit
{
    public required string SourceId { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Content { get; init; }

    /// <summary>Kosinus mesafesi. Kucuk deger daha yakin demektir.</summary>
    public required double Distance { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
```

```csharp
// AgentPrism.Abstractions/Agents/MemorySettings.cs — iki alan
public sealed record MemorySettings
{
    // ... mevcut alanlar
    public bool EnableVectorSearch { get; init; }
    public string? VectorCollection { get; init; }
}

// AgentPrism.Core — ayar
public sealed class AgentPrismKnowledgeOptions
{
    /// <summary>
    /// Gomu boyutu. 🚨 Sema ile birlikte SABITLENIR; degistirmek tum
    /// gomuleri gecersiz kilar.
    /// </summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>Parca uzunlugu (karakter).</summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>Ardisik parcalarin ortusme uzunlugu (karakter).</summary>
    public int ChunkOverlap { get; set; } = 100;

    /// <summary>Tool'un dondurecegi en fazla sonuc sayisi.</summary>
    public int MaxResults { get; set; } = 5;
}
```

### Yeni tablo — yalnız PostgreSQL

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS {schema}.document_embeddings (
    id          uuid          NOT NULL PRIMARY KEY,
    tenant_id   text          NOT NULL,
    collection  text          NOT NULL,
    source_id   text          NOT NULL,
    chunk_index integer       NOT NULL,
    content     text          NOT NULL,
    metadata    jsonb         NOT NULL DEFAULT '{}'::jsonb,
    embedding   vector({dim}) NOT NULL,
    created_at  timestamptz   NOT NULL,
    CONSTRAINT document_embeddings_uq UNIQUE (tenant_id, collection, source_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS document_embeddings_tenant_collection_idx
    ON {schema}.document_embeddings (tenant_id, collection);

-- Kosinus mesafesi icin HNSW. 🚨 Indeks olusturma buyuk tabloda uzun surer;
-- migration suresi OLCULMELIDIR.
CREATE INDEX IF NOT EXISTS document_embeddings_hnsw_idx
    ON {schema}.document_embeddings USING hnsw (embedding vector_cosine_ops);
```

🚨 **`metadata` `jsonb`'dir, `json` değil.** K-027'nin `$type` kuralı burada
**geçerli değildir**: bu alan polimorfik `ChatMessage` taşımaz, düz bir
`string → string` sözlüğüdür ve `jsonb`'nin indekslenebilirliği kazançtır.
Ayrımı bilerek yazmak gerekir; iki kural karıştırılırsa yanlış sütun tipi
seçilir.

🚨 **`vector({dim})` bir şablon değeridir.** Boyut yapılandırmadan gelir ve
migration'a **kurulum anında** yazılır. Migration çalıştıktan sonra boyutu
değiştirmek yeni bir migration ister; ayar bunu açıkça söyler.

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/knowledge/{collection}/documents` | Operator | Belge yükler; parçalar, gömüler ve yazar |
| `GET` | `/api/knowledge/{collection}/documents` | Reader | Kaynakları listeler |
| `DELETE` | `/api/knowledge/{collection}/documents/{sourceId}` | Operator | Bir kaynağın tüm parçalarını siler |
| `POST` | `/api/knowledge/{collection}/search` | Reader | Anlamsal arama — teşhis ve kalibrasyon için |

### Arayüz payı

**Yok.** Bu faz arayüze dokunmaz. Belge yönetimi için bir ekran değerlidir ama
ayrı bir iştir; bundle payı ve kapsam bu fazı iki katına çıkarır. Yeni bir
aday kalemidir.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Knowledge/
├── IVectorSearchStore.cs               (YENI)
├── VectorChunk.cs                      (YENI)
├── VectorSearchRequest.cs              (YENI)
└── VectorSearchHit.cs                  (YENI)

src/AgentPrism.Abstractions/
├── Agents/MemorySettings.cs            (iki alan)
└── Retention/RetentionTargets.cs       (document_embeddings hedefi)

src/AgentPrism.Core/Knowledge/
├── AgentPrismKnowledgeOptions.cs       (YENI)
├── TextChunker.cs                      (YENI — sabit uzunluk + ortusme)
├── KnowledgeIngestionService.cs        (YENI — parcala, gomule, yaz)
└── VectorSearchTool.cs                 (YENI — search_knowledge, K2: KODDA)

src/AgentPrism.Core/Compilation/
└── AgentDefinitionCompiler.cs          (EnableVectorSearch -> tool baglanir)

src/AgentPrism.PostgreSql/
├── Stores/PgVectorSearchStore.cs       (YENI — TEK somut uygulama)
├── Internal/PostgresQueries.cs         (vektor sorgulari + regex indirme)
├── AgentPrismPostgreSqlBuilderExtensions.cs   (TryAddSingleton)
└── Migrations/NNNN_vector.sql          (YENI — uzanti + tablo + indeks)

src/AgentPrism.Sql.Shared/Stores/
└── SqlAgentFileStore.cs                (🚨 LoadAllAsync KALDIRILIR — IS A)

src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs   (IS A: onek + glob SQL'e)
src/AgentPrism.Sqlite/Internal/SqliteQueries.cs         (IS A: ayni)

src/AgentPrism.AspNetCore/
├── Endpoints/KnowledgeEndpoints.cs     (YENI)
└── Contracts/KnowledgeContracts.cs     (YENI)
```

🚨 **`AgentPrism.SqlServer` ve `AgentPrism.Sqlite` migration setleri bu fazda
DEĞİŞMEZ.** Yalnız sorgu dosyaları İş A için güncellenir.

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `AgentFileSearchNoLoadAllTests` | 🚨 **İş A.** `SearchAsync` artık `LoadAllAsync` çağırmaz; 10 000 dosyalı bir depoda tek dosya araması sabit sayıda satır okur |
| `AgentFileListChildrenNoLoadAllTests` | 🚨 Aynı düzeltme `ListChildrenAsync` için de yapıldı |
| `AgentFileSearchContractTests` | 🚨 İş A davranışı **değiştirmez**: aynı regex, aynı sonuç kümesi. Üç sağlayıcıda ve bellek içinde |
| `AgentFileSearchPostgresRegexTests` | PostgreSQL'de regex SQL'e iner (`~`); sonuç .NET `Regex` ile **aynı** |
| `VectorStoreContractTests` | `IVectorSearchStore` sözleşmesi — PostgreSQL üzerinde (tek uygulama) |
| `VectorDimensionMismatchTests` | 🚨 Yanlış boyutlu gömü yazma **hata verir**; sessizce kesilmez |
| `VectorTenantIsolationTests` | 🚨 Bir kiracının koleksiyonu diğerine **sızmaz**. Faz 41'in sözleşme testi bu depoyu da kapsar |
| `VectorSearchOrderTests` | Sonuçlar mesafeye göre artan sırada; `MaxDistance` süzgeci çalışır |
| `VectorUpsertReplacesTests` | Aynı `sourceId` yeniden yazılınca eski parçalar silinir |
| `VectorDeleteSourceTests` | `DeleteSourceAsync` yalnız o kaynağı siler |
| `VectorSearchToolTests` | `EnableVectorSearch = true` ile `search_knowledge` tool'u agent'a bağlanır |
| `VectorSearchToolDisabledTests` | Varsayılan kapalıyken tool bağlanmaz |
| `VectorSearchWrongProviderTests` | 🚨 `EnableVectorSearch = true` + SQLite → **açılışta hata**; sessizce boş sonuç dönmez |
| `VectorNoEmbeddingGeneratorTests` | 🚨 `IEmbeddingGenerator` kayıtlı değilken açılışta anlaşılır hata |
| `TextChunkerTests` | Örtüşmeli parçalama; sınırda karakter kaybı yok |
| `KnowledgeRetentionTests` | `document_embeddings` saklama hedefi olarak tanınır |
| `VectorAotTests` | 🚨 `AgentPrism.PostgreSql` AOT uyarısı üretmez |
| `DependencyDirectionTests` | 🚨 Hiçbir projeye yeni NuGet bağımlılığı eklenmedi |

🚨 **Vektör testleri Testcontainers ile `pgvector` uzantısı taşıyan bir imaj
ister** (`pgvector/pgvector:pg17` veya eşdeğeri). Bugünkü PostgreSQL imajı bu
uzantıyı taşımıyor olabilir; **ölçülmeli** ve gerekiyorsa test fixture'ı
güncellenmelidir.

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** soruldu (sağlayıcı kapsamı, paket seçimi).

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | İş A ve İş B aynı fazda mı kalsın? | A: **evet** · B: İş A ayrı bir faza | **A.** İkisi de `SqlAgentFileStore`'un aynı zayıflığından doğuyor ve aynı dosyalara dokunuyor. Ayırmak `SqlAgentFileStore`'u iki kez değiştirmek olurdu. Ama DoD'de **ayrı** ölçütlerdir |
| 2 | Gömü ikili mi metin mi gönderilsin? | A: **metin** (`::vector` cast) · B: ikili | **A.** Sıfır bağımlılık ve AOT güvenliği. 🚨 Fark **ölçülmeli**; ikili biçim anlamlı bir kazanç verirse karar yeniden değerlendirilir ve o zaman `Pgvector` paketinin `Npgsql` sürüm kayması tekrar tartılır |
| 3 | HNSW mi IVFFlat mı? | A: **HNSW** · B: IVFFlat | **A.** HNSW eğitim gerektirmez ve küçük veride de çalışır; IVFFlat liste sayısı ayarı ister ve boş tabloda kötü davranır. 🚨 İndeks oluşturma süresi **ölçülmelidir** |
| 4 | Parçalama sunucuda mı istemcide mi? | A: **ikisi de** · B: yalnız sunucu | **A.** Basit parçalama sunucuda; tüketici kendi parçalarını `chunks` alanıyla gönderebilir. Akıllı parçalama kütüphane sınırının dışındadır |
| 5 | Belge yükleme kotaya girsin mi? | A: **evet, gömü maliyeti sayılır** · B: hayır | **A.** Gömü bir model çağrısıdır ve para harcar. Faz 20'nin fiyat modeli embedding modellerini **tanımıyor** olabilir; **ölçülmeli** |
| 6 | Koleksiyon adı serbest metin mi? | A: **doğrulanmış** (`^[a-zA-Z0-9_-]+$`) · B: serbest | **A.** Koleksiyon adı sorguya girer ve arayüzden gelebilir. Faz 22'nin `[GeneratedRegex]` doğrulaması yeniden kullanılır |
| 7 | `search_knowledge` sonuçları çalıştırma kaydına yazılsın mı? | A: **evet, tool çağrısı olarak** | **A.** Zaten bir tool'dur; `tool_invocations` kendiliğinden yazar. Ek bir mekanizma gerekmez |
| 8 | Boyut değişirse ne olur? | A: **açılışta hata** · B: otomatik migration | **A.** Boyut değişikliği tüm gömüleri geçersiz kılar; sessizce yeniden gömmek saatler sürebilir ve para harcar. Operatör bilinçli karar vermelidir |

---

## Bitiş Ölçütleri (DoD)

### İş A — performans düzeltmesi (üç sağlayıcı)

- [x] 🚨 `SqlAgentFileStore.SearchAsync` ve `ListChildrenAsync` **`LoadAllAsync`
      çağırmaz** — metot komple kaldırıldı, yerine `LoadFilteredAsync` geçti.
- [x] 10 000 dosyalı bir depoda tek dosya araması sabit sayıda satır okur;
      **ölçüldü: 1 satır** (`AgentFileSearchPostgresRegexTests.Buyuk_depoda_hedef_dizin_disindaki_satirlar_taranmaz`,
      `EXPLAIN (ANALYZE)` ile). Test container'ının locale'i onek `LIKE`'ı bir
      index range scan'e çeviriyor; tam sayı ortama göre değişebilir, kanıt
      `UnrelatedFileCount`'tan kat kat küçük kalmasıdır.
- [x] Davranış **değişmedi**: aynı regex, aynı sonuç kümesi — bellek içi ve
      üç SQL sağlayıcısında (`AgentFileStoreContract`, tüm 3 sağlayıcıda 851+/435/449
      testin parçası olarak koştu).
- [x] PostgreSQL'de regex SQL'e iner ve sonuç .NET `Regex` ile aynı
      (`AgentFileSearchPostgresRegexTests.Are_uyumlu_desen_dotnet_ile_ayni_sonucu_dondurur`).
      🚨 PostgreSQL'in ARE sözdizimini bozan .NET'e özgü bir desen (adlandırılmış
      grup) `SqlDialect.IsInvalidRegexError` ile yakalanıp on süzgeçsiz yeniden
      denenir; nihai eşleşme her zaman .NET `Regex` ile yapılır — davranış hiç
      değişmez (`Dotnet_ozel_adlandirilmis_grup_on_suzgecsiz_geri_duser`).

### İş B — anlamsal arama (yalnız PostgreSQL)

- [x] `pgvector` uzantısı migration ile kurulur; **HNSW indeks oluşturma
      süresi ölçüldü**: boş tabloda migration 0024'ün tamamı (uzantı + tablo +
      3 indeks) < 50ms (test container'ında, `MigrationRunnerTests` içinde
      diğer 23 migration'la birlikte). Tablo bu fazda yeni açıldığı için
      gerçek kurulumlarda da maliyet aynı şekilde sıfırdır.
- [x] Belge yüklenir, parçalanır, gömülür ve aranabilir — `samples/AgentPrism.Api`
      ile GERÇEK bir embedding modeliyle uçtan uca doğrulandı (aşağıda,
      Ortak bölümü).
- [x] 🚨 Yanlış boyutlu gömü yazma **hata verir**
      (`PgVectorSearchStoreTests.Yanlis_boyutlu_gomu_UpsertAsync_hata_verir`,
      `KnowledgeIngestionServiceTests.Yanlis_boyutlu_hazir_gomu_hata_verir`,
      gerçek run'da `POST .../documents` ile `400`).
- [x] 🚨 Kiracı yalıtımı korunur — bir kiracının koleksiyonu diğerine sızmaz
      (`PgVectorSearchStoreTests.Kiraci_birbirinin_koleksiyonunu_gormez`,
      `KnowledgeRetentionTests.Kiraci_suzgeciyle_yalniz_o_kiracinin_satirlari_silinir`).
      🚨 Gerçek run'daki `X-Tenant-Id` denemesi bunu GÖSTEREMEDİ: örnek uygulamada
      `AgentPrism:Tenancy:Enabled` kapalıydı (varsayılan), bu yüzden başlık
      yok sayıldı ve tek sabit kiracı kullanıldı — beklenen davranış, yalıtım
      eksikliği değil. Kanıt depo/saklama katmanındaki testlerdedir.
- [x] 🚨 `EnableVectorSearch = true` + SQLite/SQL Server → **derleme anında
      açık hata**; sessizce boş sonuç dönmez. 🚨 **Plandan sapma:** "açılışta"
      host başlangıcı değil, `AgentDefinitionCompiler`'ın ilk derleme anıdır —
      `EnableFileMemory`/`EnableTextSearch` ile AYNI, önceden kurulu desen
      (K-034). Agent tanımları çalışma anında (HTTP'den) eklenebildiği için
      host açılışında hangi tanımların bu bayrağı taşıyacağı bilinemez.
      (`AgentDefinitionCompilerTests.Anlamsal_arama_istenip_depo_kayitli_degilse_derlemeyi_durdurur`
      — testte hiçbir SQL sağlayıcısı kayıtlı değil, ki bu SQLite/SQL Server
      açıkken de `IVectorSearchStore`'un `null` kalmasıyla BİREBİR aynı durumdur.)
- [x] 🚨 `IEmbeddingGenerator` kayıtlı değilken açılışta anlaşılır hata
      (`AgentDefinitionCompilerTests.Anlamsal_arama_istenip_gomu_ureticisi_kayitli_degilse_derlemeyi_durdurur`;
      `KnowledgeIngestionService.IsSupported=false` → `AgentPrismException`, HTTP'de 501).
- [x] `search_knowledge` tool'u yalnız `EnableVectorSearch` ile bağlanır
      (`AgentDefinitionCompilerTests.Anlamsal_arama_ikisi_de_kayitliyken_tool_baglanir`;
      gerçek run'da agent `search_knowledge`'ı çağırdı, bkz. Ortak).
- [x] `document_embeddings` bir saklama hedefidir
      (`RetentionTargets.DocumentEmbeddings`, `KnowledgeRetentionTests`, 2 test).
- [x] Aynı `sourceId` yeniden yüklenince eski parçalar silinir
      (`PgVectorSearchStoreTests.Ayni_kaynak_yeniden_yazilinca_eski_parcalar_silinir`).

### Ortak

- [x] 🚨 **Hiçbir projeye yeni NuGet bağımlılığı eklenmedi** — hiçbir `.csproj`
      değişmedi (`git diff --stat -- '*.csproj'` boş döndü); `PgVectorSearchStore`
      doğrudan zaten bağımlı olunan `Npgsql` ve `System.Text.Json` (DOM tabanlı
      `Utf8JsonWriter`/`JsonDocument`, yansımasız) kullanır.
- [x] 🚨 `AgentPrism.PostgreSql` AOT uyarısı üretmez — proje `AgentPrismAotCompatible`
      bayrağını override ETMEZ (varsayılan `true`), yani trim/AOT analizi normal
      `dotnet build`'in bir parçası olarak zaten çalıştı; 0 uyarı (`TreatWarningsAsErrors`
      açıkken IL2026/IL3050 derlemeyi kırardı). Ayrı bir `VectorAotTests` yazılmadı —
      bu kapı zaten mevcut.
- [x] 🚨 **Metin biçimi ile ikili biçim arasındaki fark ölçüldü**: 1536 boyutlu
      bir gömü SQL'e metin olarak gönderildiğinde **14 416 bayt** (`length(v::text)`);
      PostgreSQL bunu ayrıştırıp kalıcı `vector` sütununda **6 148 bayt** olarak
      saklıyor (`pg_column_size(v)` — tam olarak `1536×4 + 4` bayt, yani ikili
      biçimin ta kendisi). 🚨 **Bulgu, planın varsayımını düzeltir**: metin/ikili
      farkı yalnızca YAZMA sırasında istemci→sunucu TELİNDEDİR (~2,3× daha
      fazla bayt); disk üzerindeki saklama ve okuma maliyeti FARKLI DEĞİLDİR —
      PostgreSQL her iki yoldan da aynı kanonik ikili gösterimi saklar. Karar
      (metin biçimi, K-007/AOT gerekçesiyle) bu ölçümle birlikte KORUNUR.
- [x] SQL Server ve SQLite migration setleri **değişmedi** — yalnız
      `Internal/*Queries.cs` dosyaları güncellendi (İş A); `Migrations/` klasörlerine
      hiçbir dosya eklenmedi/değişmedi.
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`pack`/`format`
      0 uyarı; `dotnet test` PostgreSQL (862), SQLite (449), Core.UnitTests (736),
      AspNetCore.FunctionalTests (410), Ui.E2ETests (41), Templates.Tests (10)
      hepsi yeşil. SQL Server (435 test) bu makinede yalnız geçici bir
      `azure-sql-edge` yamasıyla koşturulabildi (K-317'nin bilinen yerel
      kısıtı, Faz 51'le ilgisizdir) — 435/435 yeşil, yama commit'e GİRMEDİ.
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — bir belge yüklendi ve
      agent ona dayanarak cevap verdi. 🚨 **Plandan sapma:** bu makinedeki
      `AgentPrism:Providers:OpenAI:ApiKey` embedding modeline erişemiyordu
      (`403 model_not_found`, proje yalnız sohbet modellerine izinli); doğrulama
      **Google `gemini-embedding-001`** ile (`outputDimensionality=1536`) yapıldı
      ve sonra örnek koddan SÖKÜLDÜ — kalıcı örnek kodu plandaki gibi OpenAI
      `text-embedding-3-small` kullanır. Gerçek çıktı:
      - `POST /api/knowledge/kurumsal/documents` `{"sourceId":"izin-politikasi","text":"Yillik izin 14 gundur. Bes yildan sonra 20 gune cikar."}` → `{"sourceId":"izin-politikasi","chunkCount":1}`
      - `POST /api/knowledge/kurumsal/search` `{"query":"tatil hakkim ne kadar"}` (belgede "tatil" kelimesi HİÇ geçmiyor) → `distance: 0.241`, doğru belge bulundu.
      - `POST /api/agents/bilgi-asistani/run` `{"message":"kac gun tatilim var"}` → agent `search_knowledge("izin/tatil hakkı kaç gün...")` çağırdı, `distance: 0.183` ile aynı parçayı buldu, cevap: *"Bilgi tabanına göre yıllık izin **14 gün**. **5 yıldan sonra 20 güne çıkar.**"*
      - `tool_invocations` tablosunda `search_knowledge` çağrısı kayıtlı bulundu.
      - Yanlış boyutlu gömü (`embedding:[0.1,0.2]`) → `HTTP 400`.
- [x] `secret` taraması boş döndü (yalnız `docs/hafiza/sql-server-yerel-test.md`'de
      C# ifade interpolasyonu `Password={MsSqlBuilder.DefaultPassword}` eşleşti —
      gerçek bir sabit değer değil, Faz 51 öncesinden kalma bilinen yanlış pozitif).

### Doğrulama komutları

```bash
# 0) pgvector uzantisi var mi
psql "$AGENTPRISM_CONN" -c "SELECT extname, extversion FROM pg_extension WHERE extname='vector';"

# 1) Belge yukle
curl -s -X POST http://localhost:5081/agentprism/api/knowledge/kurumsal/documents \
  -H "content-type: application/json" \
  -d '{"sourceId":"izin-politikasi",
       "text":"Yillik izin 14 gundur. Bes yildan sonra 20 gune cikar."}' | jq

# 2) Anlamsal arama — kelime esleşmesi OLMAYAN bir sorgu
curl -s -X POST http://localhost:5081/agentprism/api/knowledge/kurumsal/search \
  -H "content-type: application/json" \
  -d '{"query":"tatil hakkim ne kadar","top":3}' \
  | jq '.[] | {sourceId, chunkIndex, distance}'
#    "tatil" kelimesi belgede GECMIYOR; anlamsal arama yine de bulmali

# 3) Agent tool'u kullaniyor mu
RUN=$(curl -s -X POST http://localhost:5081/agentprism/api/agents/ik-asistani/run \
  -H "content-type: application/json" \
  -d '{"message":"kac gun tatilim var"}' | jq -r '.runId')
psql "$AGENTPRISM_CONN" -c \
  "SELECT tool_name FROM agentprism.tool_invocations WHERE run_id='$RUN';"
#    search_knowledge gorulmeli

# 4) 🚨 Kiraci yalitimi
curl -s -X POST http://localhost:5081/agentprism/api/knowledge/kurumsal/search \
  -H "content-type: application/json" -H "X-Tenant-Id: baska-kiraci" \
  -d '{"query":"tatil hakkim ne kadar"}' | jq 'length'
#    beklenen: 0

# 5) 🚨 Yanlis boyut hata vermeli
curl -s -o /dev/null -w "%{http_code}\n" -X POST \
  http://localhost:5081/agentprism/api/knowledge/kurumsal/documents \
  -H "content-type: application/json" \
  -d '{"sourceId":"x","chunks":[{"index":0,"content":"a","embedding":[0.1,0.2]}]}'
#    beklenen: 400

# 6) 🚨 IS A — LoadAllAsync kalkti mi (10 000 dosya ile)
#    Sorgu sayaci veya EXPLAIN ile olculur; sonuc belgeye yazilir
psql "$AGENTPRISM_CONN" -c \
  "EXPLAIN ANALYZE SELECT path, content FROM agentprism.agent_files
    WHERE tenant_id='default' AND agent_name='asistan'
      AND path LIKE 'docs/%' AND content ~ 'izin';"

# 7) 🚨 Yeni bagimlilik YOK
dotnet list AgentPrism.slnx package --include-transitive \
  | grep -Ei "pgvector|SemanticKernel|VectorData" || echo "TEMIZ"

# 8) HNSW indeks boyutu ve olusturma suresi
psql "$AGENTPRISM_CONN" -c \
  "SELECT pg_size_pretty(pg_relation_size('agentprism.document_embeddings_hnsw_idx'));"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 `Npgsql` sürüm kayması çalışma anında `MissingMethodException` üretir (K-211) | **Hiçbir vektör paketi alınmaz**; metin biçimi + `::vector` cast kullanılır. `DependencyDirectionTests` yeni bağımlılık olmadığını doğrular |
| 🚨 `AgentPrism.PostgreSql` AOT duruşu bozulur | İfade ağacı yok, yansıma yok, tip eşleyici eklentisi yok. `VectorAotTests` doğrular |
| 🚨 Paylaşılan depo modeli kırılır ve üç sağlayıcı ayrışır | Kırılma **sınırlıdır**: yalnız İş B. İş A üçünde de çalışır. Sınır belgeye ve `IVectorSearchStore`'un XML dokümanına yazılır |
| SQLite/SQL Server kullanıcısı sessizce boş sonuç alır | 🚨 **Açılışta hata** verilir; sessiz yarım çalışma reddedildi |
| Gömü boyutu değişirse tüm veri geçersiz olur | Boyut şemadadır; uyuşmazlık **yazma anında** hata verir. Değiştirmek bilinçli bir migration ister |
| HNSW indeks oluşturma migration'ı uzun sürer | Süre **ölçülür** ve belgeye yazılır. Boş tabloda maliyet sıfırdır; mevcut kurulumda tablo yeni açıldığı için de sıfırdır |
| Gömü maliyeti fiyat modelinde görünmez | Faz 20'nin embedding modeli desteği **ölçülür** (Açık Soru 5) |
| Testcontainers imajı `pgvector` taşımıyor | 🚨 **Ölçülmeli**; gerekirse `pgvector/pgvector` imajına geçilir. Bu bir DoD kalemidir |
| Belge içeriği hassas olabilir | `document_embeddings` bir saklama hedefidir; kiracı yalıtımı sözleşme testiyle kapatılır |
| Koleksiyon adı SQL'e enjekte edilir | Ad `[GeneratedRegex]` ile doğrulanır ve parametre olarak geçirilir |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. 🚨 **PostgreSQL entegrasyon test fixture'ının imajı değişti.** `postgres:18-alpine` →
   `pgvector/pgvector:pg18`. Migration 0024 `CREATE EXTENSION IF NOT EXISTS vector;`
   çalıştırır ve bu **her** PostgreSQL testinde (yalnız vektör testlerinde değil)
   uygulanır; düz Postgres imajı uzantıyı taşımadığı için migration seti TÜM
   paket için patlardı. `pgvector/pgvector` imajı resmi `postgres` imajının
   üstüne yalnız bu uzantıyı ekler; 862 testin tamamı (Faz 51 öncesi 851 dahil)
   bu imajla yeşil koştu — davranış farkı gözlenmedi.
2. 🚨 **Migration şablon mekanizması genelleştirildi.** Plan `vector({dim})`
   yer tutucusunu doğrudan `{schema}` gibi ele alıyor gibi görünüyordu; gerçekte
   `SqlQueriesBase.ApplySchema` yalnız şemayı bilir. `SqlStoreContext`'e genel
   amaçlı bir `MigrationTemplateValues` sözlüğü eklendi (yalnız PostgreSQL
   `{dimension}` için doldurur); `MigrationRunner.ApplyTemplate` bunu şema
   değiştirmesinden SONRA uygular. Mekanizma "vektör"e özel değildir — ileride
   başka bir sağlayıcıya özgü kurulum-anı değeri gerekirse aynı yol kullanılır.
3. **`IVectorSearchStore` `AgentPrism.Sql.Shared`in paylaşılan katmanından
   GEÇMEZ.** Plan dosya listesi `PostgresQueries.cs`e "vektör sorguları" eklemeyi
   öngörüyordu; gerçekte `PgVectorSearchStore` doğrudan `Npgsql` kullanır (K-176
   yalnız 20 ÇOK-SAĞLAYICILI depo için geçerlidir — bu depo tanım gereği TEK
   sağlayıcılıdır, bir soyutlama katmanı eklemek gereksiz dolaylama olurdu).
4. **`IVectorSearchStore`'a plan taslağında olmayan `ListSourcesAsync` eklendi.**
   `GET /api/knowledge/{collection}/documents` (plan HTTP tablosunda vardı) bu
   olmadan uygulanamazdı; arayüz "taslak imzadır" notuyla zaten esnek
   bırakılmıştı.
5. **`EnableVectorSearch = true` + yanlış sağlayıcı hatası "açılışta" değil
   "derleme anında" verilir.** Bkz. DoD tablosu; `EnableFileMemory`/`EnableTextSearch`
   ile AYNI, önceden kurulu K-034 deseni — agent tanımları çalışma anında
   eklenebildiği için host açılışında hangi tanımların bayrağı taşıyacağı
   bilinemez.
6. **Gerçek-run doğrulaması Google `gemini-embedding-001` ile yapıldı, OpenAI
   `text-embedding-3-small` ile DEĞİL.** Bu makinedeki OpenAI anahtarı embedding
   modeline erişemiyordu (`403 model_not_found`); geçici bir `IEmbeddingGenerator`
   sarmalayıcısı yazılıp doğrulama sonrası SÖKÜLDÜ. Kalıcı örnek kod plandaki
   gibi OpenAI kullanır (dimension varsayılanı 1536 ile eşleşir).
7. **Kiracı yalıtımı gerçek-run'da `X-Tenant-Id` başlığıyla GÖSTERİLEMEDİ**
   (örnek uygulamada çok kiracılılık varsayılan kapalı); kanıt tamamen
   otomatik testlerdedir (bkz. DoD).

## Bu Fazda Verilen Kararlar

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **K-341 — Vektör gömüsü metin biçiminde (`::vector` cast) yazılır, hiçbir vektör paketi alınmaz** | 2026-08-08 | Üç aday ölçüldü: `Microsoft.SemanticKernel.Connectors.PgVector` 1.74.0-preview `Npgsql 8.0.7`'ye karşı derlenmiş (bizde 10.0.3); `Pgvector` 0.3.2 `Npgsql 8.0.5`'e karşı; `Microsoft.Extensions.VectorData.Abstractions` 10.8.0 tek başına işe yaramaz (K-343). K-211'in ikinci uygulaması: sürüm kayması bu depoda ölçülmüş bir hata sınıfıdır. Metin/ikili farkı da ölçüldü (bkz. DoD Ortak): fark yalnız yazma telinde (~2,3×), disk saklama ve okuma **özdeş** (PostgreSQL her iki yoldan da aynı kanonik ikili gösterimi saklar — `pg_column_size` 6148 bayt, tam olarak `1536×4+4`). | `Npgsql` sürüm kayması deseni bu paketlerde giderilirse veya ikili biçim ölçülebilir bir kazanç gösterirse. |
| **K-342 — K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş** | 2026-08-08 | K-105'in yeniden açılma koşulu ("`VectorStore` implementasyonu ve embedding sağlayıcısı seçildiğinde") kısmen karşılandı — embedding sağlayıcısı çözüldü (tüketici kaydeder) ama `VectorStore` çözülmedi: ölçüldü (`Microsoft.Extensions.VectorData.Abstractions` 10.8.0), `VectorStoreCollection<TKey,TRecord>.GetAsync` bir `Expression<Func<TRecord,bool>>` süzgeci ister; ifade ağacı yorumlamak `[RequiresDynamicCode]` sınıfına girer ve `AgentPrism.PostgreSql`'in AOT duruşunu bozar. `IVectorSearchStore` bu tipi SARMALAMAZ; kendi minimal sözleşmesini tanımlar. | Microsoft bu tipin AOT-güvenli bir filtre yüzeyi sunarsa, ya da tüketici ifade ağacı çevirmenin maliyetini kabul ederse. |
| **K-343 — Anlamsal arama yalnız PostgreSQL'de uygulanır** *(kullanıcı kararı, 2026-08-06)* | 2026-08-08 | SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın `sqlite-vec` uzantısı bu fazda ÖLÇÜLMEDİ (SQLite için: `SQLitePCLRaw` yerel kütüphanemiz bunu taşımıyor). `IVectorSearchStore` Abstractions'a girdi, varsayılan uygulaması YOK (K4); SQL Server/SQLite tüketicisi kendi uygulamasını kaydedebilir. `document_embeddings` tablosu yalnız PostgreSQL migration setindedir (0024); diğer iki setin migration'ları bu fazda HİÇ değişmedi. | SQL Server `VECTOR` tipi veya SQLite `sqlite-vec` ölçülüp bir tüketici/katkı bu sağlayıcılardan birine somut bir `IVectorSearchStore` eklerse. |
| **K-344 — Sql.Shared'in cross-provider katmanı `IVectorSearchStore` için kullanılmaz** | 2026-08-08 | K-176'nın "paylaşılan katman, saglayıcıdan bağımsız SQL üretir" kuralı 20 ÇOK-SAĞLAYICILI depo içindir. `IVectorSearchStore`'un TEK somut uygulaması (K-343) olduğu için bir `SqlDialect`/`SqlQueriesBase` soyutlaması eklemek gereksiz dolaylamadır; `PgVectorSearchStore` doğrudan `Npgsql` kullanır ve `AgentPrism.PostgreSql` derlemesinde yaşar (linked-source değil). | SQL Server veya SQLite için somut bir uygulama eklenirse, o zaman ortak bir arayüz zaten `IVectorSearchStore`'un kendisidir — yeni bir soyutlama katmanına gerek yoktur. |
| **K-345 — `document_embeddings` metadata sütunu `jsonb`'dir, `json` değil** | 2026-08-08 | K-027'nin "$type ilk özellik olmalı" kısıtı burada GEÇERLİ DEĞİLDİR: bu alan polimorfik `ChatMessage` taşımaz, düz bir `string → string` sözlüktür; `jsonb`'nin anahtar yeniden sıralaması zararsızdır ve indekslenebilirlik kazançtır. `PgVectorSearchStore` bunu yansımasız `Utf8JsonWriter`/`JsonDocument` (DOM tabanlı) ile serileştirir/ayrıştırır — AOT güvenlidir. | — |
| **K-346 — Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues`** | 2026-08-08 | `{dimension}` yer tutucusu `{schema}` ile AYNI mekanizmadan geçemezdi (`SqlQueriesBase.ApplySchema` yalnız şemayı bilir) ama "vektöre özel" bir çözüm de yanlış katmana ait olurdu (`MigrationRunner` üç sağlayıcıda ortaktır, K-176). Genel bir `IReadOnlyDictionary<string,string>` eklenip `MigrationRunner.ApplyTemplate` içinde şema değiştirmesinden SONRA uygulanır; SQL Server/SQLite boş sözlükle çalışmaya devam eder. | — |

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions/Knowledge/IVectorSearchStore.cs
public interface IVectorSearchStore
{
    int Dimensions { get; }

    ValueTask UpsertAsync(string tenantId, string collection, string sourceId,
        IReadOnlyList<VectorChunk> chunks, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request, CancellationToken cancellationToken = default);

    ValueTask<int> DeleteSourceAsync(string tenantId, string collection, string sourceId,
        CancellationToken cancellationToken = default);

    // PLANDA YOKTU — GET /api/knowledge/{collection}/documents bunsuz uygulanamazdı.
    ValueTask<IReadOnlyList<string>> ListSourcesAsync(string tenantId, string collection,
        CancellationToken cancellationToken = default);
}

public sealed record VectorChunk
{
    public required int Index { get; init; }
    public required string Content { get; init; }
    public required ReadOnlyMemory<float> Embedding { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record VectorSearchRequest
{
    public required string TenantId { get; init; }
    public required string Collection { get; init; }
    public required ReadOnlyMemory<float> QueryEmbedding { get; init; }
    public int Top { get; init; } = 5;
    public double? MaxDistance { get; init; }
}

public sealed record VectorSearchHit
{
    public required string SourceId { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Content { get; init; }
    public required double Distance { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

// AgentPrism.Abstractions/Agents/MemorySettings.cs — plandaki gibi, iki alan
public sealed record MemorySettings
{
    // ... mevcut alanlar
    public bool EnableVectorSearch { get; init; }
    public string? VectorCollection { get; init; }
}

// AgentPrism.Core/Knowledge/AgentPrismKnowledgeOptions.cs — plandaki gibi
public sealed class AgentPrismKnowledgeOptions
{
    public const string SectionName = "AgentPrism:Knowledge";
    public int Dimensions { get; set; } = 1536;
    public int ChunkSize { get; set; } = 1000;
    public int ChunkOverlap { get; set; } = 100;
    public int MaxResults { get; set; } = 5;
}

// AgentPrism.Core/Knowledge/TextChunker.cs — statik, tek metot
public static class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize, int chunkOverlap);
}

// AgentPrism.Core/Knowledge/KnowledgeIngestionService.cs — PLANDA "Core servis"
// olarak vardı ama tam imzası taslak değildi; gerçek yüzey:
public sealed partial class KnowledgeIngestionService
{
    public KnowledgeIngestionService(ITenantContext tenantContext,
        IOptions<AgentPrismKnowledgeOptions> options,
        IVectorSearchStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null);

    public bool IsSupported { get; }

    public ValueTask<int> IngestAsync(string collection, string sourceId,
        string? text, IReadOnlyList<VectorChunk>? chunks, CancellationToken ct = default);

    public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(string collection,
        string query, int? top = null, CancellationToken ct = default);

    public ValueTask<int> DeleteSourceAsync(string collection, string sourceId,
        CancellationToken ct = default);

    public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string collection,
        CancellationToken ct = default);
}

// AgentPrism.PostgreSql/Stores/PgVectorSearchStore.cs — internal, TEK somut uygulama.
// Sql.Shared'den GEÇMEZ (Plandan Sapma 3); doğrudan Npgsql kullanır.
internal sealed class PgVectorSearchStore : IVectorSearchStore { /* ... */ }
```

### HTTP `endpoint`'leri (gerçekleşen — plandakiyle birebir aynı)

| Metot | Yol | Rol |
|---|---|---|
| `POST` | `/api/knowledge/{collection}/documents` | Operator |
| `GET` | `/api/knowledge/{collection}/documents` | Reader |
| `DELETE` | `/api/knowledge/{collection}/documents/{sourceId}` | Operator |
| `POST` | `/api/knowledge/{collection}/search` | Reader |

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Knowledge/
├── IVectorSearchStore.cs
├── VectorChunk.cs
├── VectorSearchRequest.cs
└── VectorSearchHit.cs

src/AgentPrism.Abstractions/Agents/MemorySettings.cs        (EnableVectorSearch, VectorCollection)
src/AgentPrism.Abstractions/Retention/RetentionTargets.cs   (DocumentEmbeddings)

src/AgentPrism.Core/Knowledge/
├── AgentPrismKnowledgeOptions.cs
├── TextChunker.cs
├── KnowledgeIngestionService.cs
└── VectorSearchToolFactory.cs      (search_knowledge, K2: KODDA — plandaki VectorSearchTool.cs'in yerini aldı)

src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs  (AddVectorSearchTool)
src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs (AgentPrismKnowledgeOptions + KnowledgeIngestionService kaydı)

src/AgentPrism.PostgreSql/
├── Stores/PgVectorSearchStore.cs
├── Migrations/0024_vector.sql
└── AgentPrismPostgreSqlBuilderExtensions.cs   (IVectorSearchStore TryAddSingleton, MigrationTemplateValues)

src/AgentPrism.Sql.Shared/
├── Internal/SqlStoreContext.cs         (MigrationTemplateValues — genel mekanizma, Plandan Sapma 2)
├── Internal/RetentionTargetRegistry.cs (DocumentEmbeddings hedefi)
├── Migrations/MigrationRunner.cs       (ApplyTemplate)
├── Internal/SqlQueriesBase.cs          (SelectAgentFilesFiltered — İş A)
└── Stores/SqlAgentFileStore.cs         (LoadAllAsync KALDIRILDI — İş A)

src/AgentPrism.Sql.Shared/Internal/SqlDialect.cs            (IsInvalidRegexError — İş A)
src/AgentPrism.PostgreSql/Internal/{PostgresDialect,PostgresQueries}.cs   (İş A: filtreli sorgu + regex ~ + IsInvalidRegexError)
src/AgentPrism.SqlServer/Internal/{SqlServerDialect,SqlServerQueries}.cs  (İş A: filtreli sorgu, regex yok)
src/AgentPrism.Sqlite/Internal/{SqliteDialect,SqliteQueries,SqliteDataSource}.cs (İş A: filtreli sorgu + case_sensitive_like pragma)

src/AgentPrism.AspNetCore/
├── Endpoints/KnowledgeEndpoints.cs
├── Contracts/KnowledgeContracts.cs
└── AgentPrismEndpointRouteBuilderExtensions.cs (KnowledgeEndpoints.Map kaydı)

tests/AgentPrism.Core.UnitTests/
├── Knowledge/TextChunkerTests.cs                (6 test)
├── Knowledge/KnowledgeIngestionServiceTests.cs  (13 test)
├── Fakes/FakeVectorSearchStore.cs
├── Fakes/FakeEmbeddingGenerator.cs
└── Compilation/AgentDefinitionCompilerTests.cs  (+4 test: anlamsal arama wiring)

tests/AgentPrism.PostgreSql.IntegrationTests/
├── PgVectorSearchStoreTests.cs               (9 test)
├── KnowledgeRetentionTests.cs                (2 test)
├── AgentFileSearchPostgresRegexTests.cs      (3 test — İş A)
├── Infrastructure/PostgresFixture.cs         (imaj: pgvector/pgvector:pg18)
└── Infrastructure/PostgresTestContext.cs     (Vectors, VectorDimensions=3)

tests/Shared/Contracts/AgentFileStoreContract.cs   (+3 test — İş A, üç sağlayıcıda koşar)

samples/AgentPrism.Api/Program.cs   (OpenAI embedding wiring + bilgi-asistani agent'i)
```

## Sonraki Faza Devir Notu

Faz 52'nin önkoşulu yoktur ve bu fazla ilgisizdir (kaynak üreteci konusu);
aşağıdaki notlar **yeni aday kalemleri** ve genel mimari tuzaklardır, belirli
bir sıradaki faza değil.

1. 🚨 **`IVectorSearchStore`'un SQL Server ve SQLite uygulaması yoktur** ve bu
   yeni bir aday kalemidir. SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın
   `sqlite-vec` uzantısı **ölçülmemiştir** (SQLite için ek engel: `SQLitePCLRaw`
   paketimiz yerel `sqlite-vec` kütüphanesini taşımıyor).
2. **Bilgi tabanı yönetim ekranı (arayüz)** yeni bir aday kalemidir; bu faz
   arayüze hiç dokunmadı. Belge yükleme/listeleme/silme yalnız HTTP API
   üzerinden yapılabilir.
3. **Akıllı parçalama** (başlığa/anlama göre) bilerek kapsam dışıdır;
   `TextChunker.Split` sabit uzunluk + örtüşme yapar. Tüketici kendi
   parçalarını `KnowledgeIngestionService.IngestAsync`'in `chunks` parametresiyle
   doğrudan gönderebilir.
4. **Aday listesindeki F-67** (performans regresyon kapısı, varsa) İş A'nın
   düzelttiği yolu ilk hedefi sayabilir: bu fazda ölçülen "10 000 dosyalı
   depoda hedef dizin dışı satır okunmaz" kanıtı **1 satır** (test
   container'ında, `EXPLAIN ANALYZE` ile) — kapının başlangıç eşiği budur.
5. 🚨 **PostgreSQL entegrasyon test imajı artık `pgvector/pgvector:pg18`'dir**,
   `postgres:18-alpine` DEĞİL (`tests/AgentPrism.PostgreSql.IntegrationTests/Infrastructure/PostgresFixture.cs`).
   Yeni bir migration eklerken veya imaj sürümünü yükseltirken bu satırı
   unutmayın — düz `postgres` imajına dönmek migration 0024'ü (ve onu izleyen
   HER migration'ı, aynı `ApplyPendingAsync` tek toplu iş içinde çalıştığı için)
   kırar.
6. `SqlStoreContext.MigrationTemplateValues` genel bir mekanizmadır (K-346);
   yeni bir sağlayıcıya özgü "kurulum anında bilinen" migration değeri
   gerekirse (vektör boyutu gibi) bu sözlük yeniden kullanılabilir — yeni bir
   şablon sistemi icat etmeyin.
7. 🚨 **`SqlAgentFileStore`'a yeni bir arama/listeleme metodu eklerken
   `LoadFilteredAsync`'in LIKE-tabanlı desenini izleyin**, tekrar tam tablo
   okumaya (`LoadAllAsync` benzeri bir şey) dönmeyin — bu fazın tam konusu
   buydu (bkz. `docs/hafiza/sql-saglayicilari.md`).
