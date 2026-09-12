# Faz 51 — Vektör Bellek ve RAG (`pgvector`)

> **Durum:** ✅ Tamamlandı (2026-08-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-30**
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`
> **Yeni paket:** Yok · **Yeni NuGet:** 🚨 **Yok** — gerekçe [51.2](#512--sıfır-yeni-paket-ölçülmüş-gerekçe) · **Migration:** **gerekli** — PostgreSQL'de bir tablo + uzantı; SQL Server ve SQLite'ta **yok**
> **Public API:** büyüyor — bir arayüz, üç kayıt tipi, iki ayar. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'de anlamsal arama yoktur. Bir agent'a doküman verip "buna göre cevapla" demenin yolu yoktur; dosya belleği vardır ama araması **regex**'tir ve her çağrıda **bütün dosyaları belleğe alır**. Bu faz `pgvector` ile anlamsal aramayı getirir ve dosya aramasının O(n) davranışını düzeltir.

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
- [x] Belge yüklenir, parçalanır, gömülür ve aranabilir — `samples/Tracon.Api`
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
      `Tracon:Tenancy:Enabled` kapalıydı (varsayılan), bu yüzden başlık
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
      `KnowledgeIngestionService.IsSupported=false` → `TraconException`, HTTP'de 501).
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
- [x] 🚨 `Tracon.PostgreSql` AOT uyarısı üretmez — proje `TraconAotCompatible`
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
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı — bir belge yüklendi ve
      agent ona dayanarak cevap verdi. 🚨 **Plandan sapma:** bu makinedeki
      `Tracon:Providers:OpenAI:ApiKey` embedding modeline erişemiyordu
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
psql "$TRACON_CONN" -c "SELECT extname, extversion FROM pg_extension WHERE extname='vector';"

# 1) Belge yukle
curl -s -X POST http://localhost:5081/tracon/api/knowledge/kurumsal/documents \
  -H "content-type: application/json" \
  -d '{"sourceId":"izin-politikasi",
       "text":"Yillik izin 14 gundur. Bes yildan sonra 20 gune cikar."}' | jq

# 2) Anlamsal arama — kelime esleşmesi OLMAYAN bir sorgu
curl -s -X POST http://localhost:5081/tracon/api/knowledge/kurumsal/search \
  -H "content-type: application/json" \
  -d '{"query":"tatil hakkim ne kadar","top":3}' \
  | jq '.[] | {sourceId, chunkIndex, distance}'
#    "tatil" kelimesi belgede GECMIYOR; anlamsal arama yine de bulmali

# 3) Agent tool'u kullaniyor mu
RUN=$(curl -s -X POST http://localhost:5081/tracon/api/agents/ik-asistani/run \
  -H "content-type: application/json" \
  -d '{"message":"kac gun tatilim var"}' | jq -r '.runId')
psql "$TRACON_CONN" -c \
  "SELECT tool_name FROM tracon.tool_invocations WHERE run_id='$RUN';"
#    search_knowledge gorulmeli

# 4) 🚨 Kiraci yalitimi
curl -s -X POST http://localhost:5081/tracon/api/knowledge/kurumsal/search \
  -H "content-type: application/json" -H "X-Tenant-Id: baska-kiraci" \
  -d '{"query":"tatil hakkim ne kadar"}' | jq 'length'
#    beklenen: 0

# 5) 🚨 Yanlis boyut hata vermeli
curl -s -o /dev/null -w "%{http_code}\n" -X POST \
  http://localhost:5081/tracon/api/knowledge/kurumsal/documents \
  -H "content-type: application/json" \
  -d '{"sourceId":"x","chunks":[{"index":0,"content":"a","embedding":[0.1,0.2]}]}'
#    beklenen: 400

# 6) 🚨 IS A — LoadAllAsync kalkti mi (10 000 dosya ile)
#    Sorgu sayaci veya EXPLAIN ile olculur; sonuc belgeye yazilir
psql "$TRACON_CONN" -c \
  "EXPLAIN ANALYZE SELECT path, content FROM tracon.agent_files
    WHERE tenant_id='default' AND agent_name='asistan'
      AND path LIKE 'docs/%' AND content ~ 'izin';"

# 7) 🚨 Yeni bagimlilik YOK
dotnet list Tracon.slnx package --include-transitive \
  | grep -Ei "pgvector|SemanticKernel|VectorData" || echo "TEMIZ"

# 8) HNSW indeks boyutu ve olusturma suresi
psql "$TRACON_CONN" -c \
  "SELECT pg_size_pretty(pg_relation_size('tracon.document_embeddings_hnsw_idx'));"
```

---

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
3. **`IVectorSearchStore` `Tracon.Sql.Shared`in paylaşılan katmanından
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
| **K-342 — K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş** | 2026-08-08 | K-105'in yeniden açılma koşulu ("`VectorStore` implementasyonu ve embedding sağlayıcısı seçildiğinde") kısmen karşılandı — embedding sağlayıcısı çözüldü (tüketici kaydeder) ama `VectorStore` çözülmedi: ölçüldü (`Microsoft.Extensions.VectorData.Abstractions` 10.8.0), `VectorStoreCollection<TKey,TRecord>.GetAsync` bir `Expression<Func<TRecord,bool>>` süzgeci ister; ifade ağacı yorumlamak `[RequiresDynamicCode]` sınıfına girer ve `Tracon.PostgreSql`'in AOT duruşunu bozar. `IVectorSearchStore` bu tipi SARMALAMAZ; kendi minimal sözleşmesini tanımlar. | Microsoft bu tipin AOT-güvenli bir filtre yüzeyi sunarsa, ya da tüketici ifade ağacı çevirmenin maliyetini kabul ederse. |
| **K-343 — Anlamsal arama yalnız PostgreSQL'de uygulanır** *(kullanıcı kararı, 2026-08-06)* | 2026-08-08 | SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın `sqlite-vec` uzantısı bu fazda ÖLÇÜLMEDİ (SQLite için: `SQLitePCLRaw` yerel kütüphanemiz bunu taşımıyor). `IVectorSearchStore` Abstractions'a girdi, varsayılan uygulaması YOK (K4); SQL Server/SQLite tüketicisi kendi uygulamasını kaydedebilir. `document_embeddings` tablosu yalnız PostgreSQL migration setindedir (0024); diğer iki setin migration'ları bu fazda HİÇ değişmedi. | SQL Server `VECTOR` tipi veya SQLite `sqlite-vec` ölçülüp bir tüketici/katkı bu sağlayıcılardan birine somut bir `IVectorSearchStore` eklerse. |
| **K-344 — Sql.Shared'in cross-provider katmanı `IVectorSearchStore` için kullanılmaz** | 2026-08-08 | K-176'nın "paylaşılan katman, saglayıcıdan bağımsız SQL üretir" kuralı 20 ÇOK-SAĞLAYICILI depo içindir. `IVectorSearchStore`'un TEK somut uygulaması (K-343) olduğu için bir `SqlDialect`/`SqlQueriesBase` soyutlaması eklemek gereksiz dolaylamadır; `PgVectorSearchStore` doğrudan `Npgsql` kullanır ve `Tracon.PostgreSql` derlemesinde yaşar (linked-source değil). | SQL Server veya SQLite için somut bir uygulama eklenirse, o zaman ortak bir arayüz zaten `IVectorSearchStore`'un kendisidir — yeni bir soyutlama katmanına gerek yoktur. |
| **K-345 — `document_embeddings` metadata sütunu `jsonb`'dir, `json` değil** | 2026-08-08 | K-027'nin "$type ilk özellik olmalı" kısıtı burada GEÇERLİ DEĞİLDİR: bu alan polimorfik `ChatMessage` taşımaz, düz bir `string → string` sözlüktür; `jsonb`'nin anahtar yeniden sıralaması zararsızdır ve indekslenebilirlik kazançtır. `PgVectorSearchStore` bunu yansımasız `Utf8JsonWriter`/`JsonDocument` (DOM tabanlı) ile serileştirir/ayrıştırır — AOT güvenlidir. | — |
| **K-346 — Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues`** | 2026-08-08 | `{dimension}` yer tutucusu `{schema}` ile AYNI mekanizmadan geçemezdi (`SqlQueriesBase.ApplySchema` yalnız şemayı bilir) ama "vektöre özel" bir çözüm de yanlış katmana ait olurdu (`MigrationRunner` üç sağlayıcıda ortaktır, K-176). Genel bir `IReadOnlyDictionary<string,string>` eklenip `MigrationRunner.ApplyTemplate` içinde şema değiştirmesinden SONRA uygulanır; SQL Server/SQLite boş sözlükle çalışmaya devam eder. | — |

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
   `postgres:18-alpine` DEĞİL (`tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/PostgresFixture.cs`).
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
