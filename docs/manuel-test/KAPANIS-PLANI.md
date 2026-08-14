# Manuel Kabul Testi — Kapanış Planı (KOSUM-PLANI §8)

> **Bu dosya bir ajan talimatıdır ve oturumlar arası tek referanstır.**
> Kapanış **aile aile**, ayrı oturumlarda yapılır. Bir oturum açan ajan önce bu
> dosyayı baştan sona okur, §1'deki protokolü uygular, kendi ailesini kodlar ve
> §2'deki durum tablosunu günceller.
>
> Koşum protokolü (şerit izolasyonu, oturum bütçesi) [`KOSUM-PLANI.md`](KOSUM-PLANI.md)'dedir
> ve **koşum bitti**; bu dosya onun §8'ini yürütür. Ortam kurulumu ve hata
> şablonu [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## 1. Oturum protokolü

Her oturum şu yedi adımı uygular. Adım atlanmaz.

```mermaid
flowchart TD
    A["1. Bu dosyayi oku"] --> B["2. §2'den siradaki aileyi al"]
    B --> C["3. Dort kapiyi kos - taban cizgisi YESIL mi?"]
    C --> D["4. Kok nedeni duzelt + regresyon testi ekle"]
    D --> E["5. Dort kapi + CANLI sunucuda case'i yeniden kos"]
    E --> F["6. Case'in Gercek sonuc/Durum satirini guncelle"]
    F --> G["7. Ayri commit + §2 durum tablosunu guncelle"]
```

**Kurallar:**

| Kural | Gerekçe |
|---|---|
| Bir oturum **bir aile** bitirir (küçükse birkaç). Aile ortasında bırakma. | Aile sınırı kesme noktasıdır; yarım aile sonraki oturumu yanıltır. |
| Düzeltmeden önce **kusuru ampirik olarak yeniden üret**. | Kusurların bir kısmı önceki dalgalarda zaten kapandı (bkz. §3). Varsayma, ölç. |
| Her aile **ayrı commit**. | K-400..K-407 turunun yordamı; geri alınabilirlik. |
| Case'in `Gerçek sonuç` alanına **gözlenen** çıktı yazılır, beklenti tekrarlanmaz. | 00-INDEKS §5. |
| Eski `Gerçek sonuç` **silinmez**; altına `---` ve yeni koşum notu eklenir. | Kusurun tarihçesi en değerli bilgidir. |
| Dört kapı kırmızıysa iş **bitmemiştir**. | AGENTS.md. |

**Dört kapı:**

```bash
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

> ⚠️ `--no-build` ile test koşarken **önce build'in başarılı olduğunu doğrula**.
> Build kırıkken `dotnet test --no-build` eski ikiliyi koşar ve yanlış yeşil verir.
> Bu tuzağa bu koşumda bir kez düşüldü.

---

## 2. Durum

> Son güncelleme: **2026-08-14**.

| | |
|---|---|
| Toplam case | **1097** |
| Koşuldu | **1097** (koşulmamış case **yok**) |
| ☑ Geçti | **994** |
| ☒ **Kaldı** | **72** |
| ⏭ Atlandı | **30** |
| ☐ Beklemede | **1** (`MT-UIRUN-019`) |

### Biten işler

| İş | Commit | Kapanan case |
|---|---|---|
| **Adım 0** — dört kapı kırmızıydı; ses turu `commit` çerçevesi kendi sesinin önüne geçiyordu | `f36eeaf` | (`MT-PKG-010`'un kök nedeni — case'in kendisi yeniden koşulmalı) |
| **Aile A** — şema kapısı + teşhis ucu | `2126aab` | `MT-PG-025`, `MT-PG-034`, `MT-PG-051`, `MT-PG-052` |
| **Aile B** — Guard maskelemesi `RunStarted`'da ham kalıyor | `a3d1dea` | `MT-GUARD-041`, `MT-GUARD-053` |
| **Aile C** — Eşzamanlı ilk istekte oturum lost update | `f287b12` | `MT-CORE-054` |

### Kalan aileler

Sıra: Kritik → Yüksek → Orta/Düşük. Bir sonraki oturum **D** ile başlar.

| Aile | Önem | Konu | Case | Durum |
|---|---|---|---|---|
| ~~A~~ | Kritik | Şema kapısı, teşhis ucu | 4 | ✅ `2126aab` |
| ~~B~~ | Kritik | Guard maskelemesi `RunStarted`'da ham kalıyor | 2 | ✅ `a3d1dea` |
| ~~C~~ | Kritik | Eşzamanlı ilk istekte oturum lost update | 1 | ✅ (bu koşum) |
| **D** | Kritik | `T[]` parametreli tool derlenmiyor | 1 | ⬜ |
| **E** | Kritik | İki kalıcılık sağlayıcısı (K-183) | 1 | ⬜ |
| **F** | Yüksek | 21 endpoint dosyasında kapsam denetimi yok | 4 | ⬜ |
| **G** | Yüksek | JSON çözümleme hatası `400` yerine `500` | 4 | ⬜ |
| **H** | Yüksek | Dar `catch` → çıplak `500` | 1 | ⬜ |
| **I** | Yüksek | Kaynak üreteci sahte `mcp:` rozeti | 1 | ⬜ |
| **J** | Yüksek | Agent editörü sağlayıcı yarışı | 1 | ⬜ |
| **K** | Yüksek | CSP `blob:` beyaz listede değil | 2 | ⬜ |
| **L** | Yüksek | SPA geçişi run'ı `Running` bırakıyor | 1 | ⬜ |
| **M** | Yüksek | Loopback dışı erişimde ham JSON | 1 | ⬜ |
| **N** | Yüksek | 112 `ProblemDetails` başlığı Türkçe | 1 | ⬜ |
| **O** | Orta | Yapılandırmada geçersiz değer sessizce düşüyor | 4 | ⬜ |
| **P** | Orta | Bağlanmayan yapılandırma anahtarları | 2 | ⬜ |
| **Q** | Orta | Çapraz kiracı `404` dalı ölü kod | 4 | ⬜ |
| **R** | Orta | Çalıştırma filtreleri | 2 | ⬜ |
| **S** | Orta | Idempotency replay başlık kaybı | 1 | ⬜ |
| **T** | Orta | MCP "connection refused" → `unknown_tool` | 1 | ⬜ |
| **U** | Orta/Düşük | Kalan 10 arayüz kusuru | 10 | ⬜ |
| **V** | Karışık | Yetenek boşlukları | 8 | ⬜ |
| **Doküman** | — | Beklenen sonuç koda göre düzeltilir | 13 | ⬜ |
| **Yeniden koşum** | — | Kusuru zaten kapalı | 8 | ⬜ |
| **MT-PKG-010** | — | Kök neden `f36eeaf`'te kapandı, case yeniden koşulmalı | 1 | ⬜ |

**Toplam:** 51 (kod) + 13 (doküman) + 8 (yeniden koşum) + 1 = **73**.

---

## 3. Düzeltilmiş öncüller — ÖNCE BUNU OKU

`KOSUM-PLANI.md` §1 durum tablosu **bayattır** ve şu üç iddiası yanlıştır:

| KOSUM-PLANI §1 iddiası | Ölçülen gerçek |
|---|---|
| "898 case koşuldu" | **1097/1097 koşuldu** |
| "Şerit 4 ⏳ Başlamadı" | Şerit 4 **bitti** — `SONUCLAR-S4-2026-08-13.md` |
| "Kalan 199 case" | Koşulmamış case **yok**; kalan iş yalnız kusurlar |

**Daha önemlisi: kusurların bir kısmı önceki düzeltme dalgalarında zaten
kapandı ama sonuç dosyaları güncellenmedi.** Bu koşumda üç örnek çıktı:

- `HATA-001`/`HATA-002`'nin **çökme** yolu `2caa423` (S1-8) ile kapanmıştı —
  `ExternalSurfaceGuard.ListCatalogWithRetryAsync` artık `StopApplication`
  çağırmıyor.
- `MT-PKG-082`'nin "iki sağlayıcı da şema yazıyor" kusuru
  `MigrationHostedService.IsWinningProvider()` ile kapanmış (kod yorumu
  `MT-PKG-082`'yi adıyla anıyor). **Aile E bunu önce doğrulamalı.**
- `HATA-S4-015` = `HATA-S2-002` = `HATA-K-007`; K-406 ile kapandı.

> **Bu yüzden her aile "önce ampirik olarak yeniden üret" adımıyla başlar.**
> Kusur zaten kapalıysa iş, case'i yeniden koşup `Durum` satırını
> güncellemekten ibarettir.

---

## 4. Ortam tarifleri

### 4.1 Canlı sunucu (doğrulama için)

Anahtarlar `dotnet user-secrets` içinde **zaten kayıtlıdır** (OpenAI, Anthropic,
Google, OpenRouter, Voice/ElevenLabs). Hiçbir `secret` dosyaya yazılmaz.

```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism

export AgentPrism__Ui__AuthToken="manuel-test-token-2026"
export AgentPrism__PostgreSql__ConnectionString="Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"
export AgentPrism__PostgreSql__SchemaName="mt_fin"
export AgentPrism__Sqlite__ConnectionString=""
export AgentPrism__SqlServer__ConnectionString=""

dotnet run --project samples/AgentPrism.Api -c Release --no-build --urls "http://localhost:5090"
```

```bash
# Istek
curl -s -H "Authorization: Bearer manuel-test-token-2026" \
  "http://localhost:5090/agentprism/api/diagnostics" | python3 -m json.tool

# Temiz sema
docker exec -i ap-pg psql -U postgres -d agentprism -c "DROP SCHEMA IF EXISTS mt_fin CASCADE;"
```

**Tuzaklar (bu koşumda yaşandı):**

- `Bash` aracının çalışma dizini çağrılar arasında **korunur**. Bir `cd`'den
  sonra göreli yol kırılır — **mutlak yol kullan** ya da her komutu
  `cd /Users/farukatasoy/Desktop/projects/AgentPrism &&` ile başlat.
- `provider:"echo"` gerçek anahtarlar kayıtlıyken **kayıtlı değildir**;
  `400 Tanim gecersiz` alırsın. Veritabanına ulaşan yolu görmek için
  `openai` kullan.
- Container'lar: `ap-pg` (55432), `ap-mssql` (51433). `ap-pg-mt-core` (55434)
  eski bir oturumdan kalmadır — **dokunma**.

### 4.2 Hızlı iç döngü

Arayüze dokunmuyorsan `-p:AgentPrismFrontendEnabled=false` npm/Vite/Vitest
adımlarını atlar. Arayüz düzeltmelerinde **atlama** — `AgentPrism.UI`
yeniden derlenmezse E2E testi eski bundle'ı koşar.

---

## 5. Kullanıcı kararları (2026-08-14, bağlayıcı)

| # | Karar | Sonuç |
|---|---|---|
| 1 | **Azure (9 case)** | ⏭ Atlandı kalır, gerekçe yazılır. Kimlik sağlanmayacak. |
| 2 | **Kapsam denetimi** | 21 endpoint dosyasının **tamamı** düzeltilir — yalnız kanıtlanan 3'ü değil. |
| 3 | **`HATA-S4-007`** | 112 Türkçe `ProblemDetails` başlığı **İngilizce'ye çevrilir**. |
| 4 | **Yetenek isteyen bulgular** | MAF sınırındaki `HATA-S2-010` hariç hepsi kodlanır. |

---

## 6. Aileler

Her aile: kök neden (`dosya:satır`) · kapanan case · tasarım notu.

### ~~Aile B~~ — Guard'ın maskelediği girdi `RunStarted`'da ham kalıyor 🚨 Kritik ✅ (bu koşum)

**Kusur:** `HATA-S3-006`. Maskelenen/engellenen girdi (kredi kartı, `sk-…`)
`run_events`'e **ham** yazılıyor ve SSE ile yeniden oynatılıyor.

**Kök neden — katman sırası:**
`src/AgentPrism.Core/Recording/RunRecordingAgent.cs:521-546`
`ExtractQuery(input)` **ve** `SaveInputAsync(start, input)` ham listeyi yazar.
Guard bir `IChatClient` dekoratörüdür
(`src/AgentPrism.Core/Guards/ContentGuardingChatClient.cs`) — model çağrısında
maskeler. `RunRecordingAgent` agent'ı sarar ve chat client'a **inilmeden önce**
kaydeder.

**Tasarım kararı gerekir:** ya maskelemeyi agent düzeyine taşı, ya kaydediciye
guard hattını (`ContentGuardPipeline`) okut. İkincisi daha az kırıcı görünüyor
ama `IContentGuard` çağrısını iki kez yapar — karar `docs/KARARLAR.md`'ye yazılır.

**Uygulanan tasarım (K-408 adayı, kapanışta yazılacak):** İkinci seçenek
seçildi, ama `ContentGuardPipeline.InspectAsync` DEĞİL — yeni bir
`ContentGuardPipeline.PreviewAsync` metodu eklendi. Gerekçe: `InspectAsync`
karar bulunca `scope.Writer.AppendAsync` çağırır (`ContentMasked`/
`ContentBlocked` olayı + engellemede denetim izi); `BeginRunAsync` bu noktada
henüz `StartAsync`'i çağırmamıştır, yani `runs` satırı **yoktur**.
`run_events.run_id` bir FK taşır (canlı Postgres'te doğrulandı) — `runs`
satırı yokken `AppendEventAsync` istisna atar, `RunEventWriter` bunu yutup
`IsDisabled=true` yapar ve **o çalıştırmanın TÜM kaydı** (RunStarted dahil)
sessizce kaybolur. `PreviewAsync` aynı guard sırasını/maskeleme zincirini
uygular ama hiçbir olay/denetim izi yazmaz ve engellemede istisna ATMAZ
(sadece `"[content_blocked]"` işaretini döner); gerçek karar kaydı — tam
olarak bugünkü gibi — mesaj modele giderken `ContentGuardingChatClient`
`InspectAsync`'i çağırdığında oluşur. İki path'in ortak mesaj-tarama
mantığı `ContentGuardMessageMasker` (yeni dosya) içinde paylaşılır.
Yeni dosyalar: `src/AgentPrism.Core/Guards/ContentGuardMessageMasker.cs`.
Değişen dosyalar: `ContentGuardPipeline.cs` (+`PreviewAsync`),
`ContentGuardingChatClient.cs` (mesaj-tarama `ContentGuardMessageMasker`'a
taşındı), `RunRecordingAgent.cs`/`RunRecordingAgentDecorator.cs`
(+`ContentGuardPipeline?` parametresi), `AgentPrismServiceCollectionExtensions.cs`
(DI fabrikasına `ContentGuardPipeline` satırı eklendi — K-157 tuzağı).

**Case:** `MT-GUARD-041` (kredi kartı) ✅, `MT-GUARD-053` (sağlayıcı API
anahtarı) ✅. Kapsam notu: `MT-GUARD-043` aynı kök nedeni paylaşır — kendi
kriteri zaten Geçti idi, `RunStarted` tarafı da ayrıca doğrulandı.

**Regresyon testleri:** `tests/AgentPrism.Core.UnitTests/Guards/ContentGuardRecordingTests.cs`
`RunStarted_olayi_maskelenen_girdiyi_ham_tasimaz`,
`RunStarted_olayi_engellenen_girdiyi_ham_tasimaz`.

### ~~Aile C~~ — Eşzamanlı ilk istekte oturum lost update 🚨 Kritik ✅ (bu koşum)

**Kusur:** `HATA-004`. Aynı yeni oturuma eşzamanlı iki ilk istek → iki
`conversations` satırı; `state->stateBag->conversationId` last-write-wins ile
ezilir, kaybedenin mesajları DB'de ama oturumdan **erişilemez**.

**Kök neden:** check-then-create yarışı. Sonuç dosyası `dosya:satır`
vermemiş — ampirik kanıt DB düzeyinde (iki satır: `...6880-7137`, `...6880-700e`).
`src/AgentPrism.Core/Sessions/AgentSessionManager.cs` (`SaveSessionAsync`) ve
`src/AgentPrism.Sql.Shared/Stores/SqlSessionStore.cs`/`InMemorySessionStore.cs`
(`SaveAsync`/`ISessionStore.SaveAsync`) — kayıt kim olursa olsun kayıtsız şartsız
üzerine yazan bir `UPSERT`.

**Tasarım kararı — denenen ve terk edilen ilk yaklaşım:** İlk deneme, oturumu
`GetOrCreateSessionAsync` içinde HEMEN (agent hiç çalışmadan) atomik olarak
depoya yazmaktı (`ISessionStore.TryCreateAsync`, `INSERT` + tekillik ihlalini
yakala — `SqlIdempotencyStore.ReserveAsync`/`SqlExperimentStore.StartAsync`
ile aynı desen, `SqlDialect.IsUniqueViolation`). Bu YETERSİZ çıktı: konuşma
gecmişi sağlayıcısının (`SqlChatHistoryProvider`) konuşma kimliği yalnız agent
GERÇEKTEN çalışırken (`ProvideChatHistoryAsync`) üretilir, oturum
oluşturulurken değil — regresyon testiyle ampirik olarak doğrulandı (bkz.
`tests/AgentPrism.PostgreSql.IntegrationTests/SessionPersistenceTests.cs`).
İki eşzamanlı istek erken-yazılan BOŞ oturumu paylaşsa bile, ikisi de KENDİ
turunu bağımsız çalıştırıp KENDİ konuşma kimliğini üretiyordu — kusur aynen
tekrar üretiliyordu, yalnız yarış penceresi kayıyordu.

**Uygulanan tasarım (kapanışta karar defterine yazılacak):** Atomik iddia
`GetOrCreateSessionAsync`'e değil `SaveSessionAsync`'e taşındı — depoya
HİÇBİR ŞEY, o oturumun İLK turu tamamlanıp kaydedilene kadar yazılmaz.
`AgentSessionManager`, depoda kaydı bulunamayıp TAZE açılan oturumları bir
`ConditionalWeakTable<AgentSession, object>` ile (oturum nesnesinin kendi
kimliğine bağlı, geçici, sızıntısız) işaretler. O oturumun İLK
`SaveSessionAsync` çağrısı `ISessionStore.TryCreateAsync` ile atomik dener;
kaybederse **sessizce üzerine yazmaz**, açık bir `AgentPrismSessionConflictException`
fırlatır (yeni tip, `AgentPrismException`'dan türer, `ErrorType = "session_conflict"`).
`AgentEndpoints.cs`'in akışsız (`Idempotency-Key`) yolu bunu `409 Conflict`
`ProblemDetails`'e çevirir; akışlı (SSE) yol zaten var olan genel `catch`'e
düşüp `event: error` çerçevesi üretir (K-296 deseni, değişiklik gerekmedi).
Kaybeden isteğin model turu YİNE de baştan sona çalışır (gerçek maliyet) —
bu, dağıtık kilitleme gibi çok daha ağır bir çözüme göre kabul edilen bir
taviz (KOSUM-PLANI'nin kendi kabul kriteri: "bir çakışma denetimi varsa
isteklerden biri açık bir çakışma hatası döner; bu da kabul edilebilir").
Sonraki kayıtlar (ve baştan bulunan mevcut oturumların HER kaydı) değişmeden
koşulsuz `SaveAsync` kullanır — yalnız bir oturumun gerçekten İLK kaydı bu
korumadan geçer.

**Yeni dosyalar:** yok. **Değişen dosyalar:**
`src/AgentPrism.Abstractions/Sessions/ISessionStore.cs` (+`TryCreateAsync`,
varsayılan uygulama check-then-create — geriye dönük uyumluluk için ATOMİK
DEĞİL, gerçek depolar geçersiz kılar), `AgentPrismException.cs`
(+`AgentPrismSessionConflictException`), `AgentSessionManager.cs`
(`GetOrCreateSessionAsync`/`SaveSessionAsync` yeniden yazıldı),
`Core/Storage/InMemorySessionStore.cs` (+`TryCreateAsync`,
`ConcurrentDictionary.TryAdd`), `Core/Audit/AuditingSessionStore.cs`
(+`TryCreateAsync` — **atlanamaz**: dekoratör bu geri çağrıyı override
etmezse arayüzün varsayılan check-then-create uygulamasına düşer ve DI'da HER
ZAMAN kayıtlı olan bu dekoratör iç deponun gerçek atomik uygulamasını sessizce
devre dışı bırakırdı), `Sql.Shared/Stores/SqlSessionStore.cs`
(+`TryCreateAsync`, düz `INSERT` + `SqlDialect.IsUniqueViolation`
yakalama — `SqlIdempotencyStore` ile aynı desen), `Sql.Shared/Internal/SqlQueriesBase.cs`
(+`InsertSession`), `PostgreSql`/`Sqlite`/`SqlServer` `Internal/*Queries.cs`
(+`InsertSession` sorgusu, üç lehçe), `AgentEndpoints.cs`
(akışsız yolda `AgentPrismSessionConflictException` → `409`).

**Case:** `MT-CORE-054` ✅.

**Regresyon testleri:**
`tests/Shared/Contracts/SessionStoreContract.cs`
(`TryCreateAsync_yeni_kimlikte_true_doner_ve_kaydeder`,
`TryCreateAsync_var_olan_kimlikte_false_doner_ve_uzerine_yazmaz`,
`TryCreateAsync_eszamanli_ayni_kimlikte_yalniz_biri_kazanir`,
`TryCreateAsync_ayni_kimlik_iki_kiracida_bagimsiz_kazanir` — InMemory,
Postgres, Sqlite, SqlServer'ın dördünde de koşar) ·
`tests/AgentPrism.PostgreSql.IntegrationTests/SessionPersistenceTests.cs`
`Ayni_yeni_oturuma_eszamanli_iki_ilk_istek_sessizce_mesaj_kaybetmez` (gerçek
`Task.WhenAll` eşzamanlılığıyla HATA-004'ü ampirik olarak yeniden üretir).

### Aile D — `T[]` parametreli tool derlenmiyor 🚨 Kritik

**Kusur:** `MT-PKG-044`. `APG0003` diziyi "desteklenir" ilan ediyor ama
`T[]` parametreli hiçbir tool metodu derlenmiyor (`CS1503: IReadOnlyList<T>` → `T[]`).
`int[]` ile de tekrar üretildi.

**Kök neden:**
`src/AgentPrism.Generators/ParameterTypeValidator.cs:98-111`
(`TryGetArrayElementType` çıplak dizi/arayüz ayrımını kaybediyor) ·
`src/AgentPrism.Generators/SourceWriter.cs:143` (her zaman `GetArray(...)`,
`.ToArray()` eklenmiyor).

**Case:** `MT-PKG-044`.

### Aile E — İki kalıcılık sağlayıcısı 🚨 Kritik

**Önce doğrula:** `MigrationHostedService.IsWinningProvider()` bu kusuru zaten
kapatmış olabilir (kod yorumu `MT-PKG-082`'yi adıyla anıyor).

**Case'in kendisi örnek uygulamayla üretilemez** — `samples/AgentPrism.Api/Program.cs:635-647`
`if/else-if` ile **tek** sağlayıcı kaydeder. Case, `UsePostgreSql`'den sonra
geçici bir `UseSqlite(...)` satırı eklemeyi öngörür (MT-PG-034'ün adımlarıyla
aynı desen); test bitince geri alınır.

**Case:** `MT-PKG-082`.

### Aile F — API anahtarı kapsam denetimi yok 🚨 Yüksek (güvenlik)

Ayrıntılı eşleme tablosu **§7**'dedir.

**Kusur:** `HATA-S2-009`, `HATA-S2-011`, `HATA-S3-009`. `Endpoints/` +
`OpenAICompat/` altındaki **21 dosyada** hiç `RequireApiKeyScope` yok (~100 uç).
Salt-okunur bir anahtar dış MCP sunucusu kaydedebiliyor, onay kararı verebiliyor,
zamanlama silebiliyor.

**Uygulama TEK noktadadır:** `src/AgentPrism.AspNetCore/Security/AgentPrismEndpointFilter.cs:276`
uçtan `ApiKeyScopeRequirement` metadata'sını okur; filtre korumalı gruba
`AgentPrismEndpointRouteBuilderExtensions.cs:109`'da bir kez takılır ve 21
dosyanın hepsi o gruba eşlenir. **Metadata eklemek yeterlidir** — kayıt, DI veya
middleware değişikliği gerekmez.

**Dış yüzeyler zaten korumalı:** `A2A/AgentPrismA2AExtensions.cs:106` ve
`McpServer/AgentPrismMcpServerExtensions.cs:80` grup düzeyinde `ExternalInvoke`
uygular — bu 21'e dahil değildir.

**Denetimin üç özelliği:**
1. Denetim yalnız istek bir **API anahtarıyla** doğrulandıysa çalışır
   (satır 138 `record is not null` dalının içinde). Statik token, anonim ve
   kullanıcı kimliği bu denetimden etkilenmez — tasarım böyle.
2. `GetMetadata<T>` **son** eşleşmeyi döner → uç başına **tek** kapsam.
   AND/OR bileşimi yoktur.
3. `record.Scopes.Contains(...)` düz küme testidir → `SecurityAdmin` taşıyan bir
   anahtar **herhangi** kapsamda yeni anahtar üretebilir. **Yan düzeltme
   (yeni enum üyesi gerekmez):** `ApiKeyEndpoints.CreateAsync` çağıran anahtarın
   kendi taşımadığı kapsamı reddetmelidir (attenuation).

**Case:** `MT-MCP-051`, `MT-MCP-052`, `MT-RES-028`, `MT-JOB-090`.

### Aile G — JSON çözümleme hatası `400` yerine `500` 🚨 Yüksek

**Kusur:** `HATA-005`, `HATA-S2-006`, `HATA-S2-007`. Eksik `required` alan veya
bilinmeyen enum değeri → `JsonException` → `BadHttpRequestException` → genel
handler → **500**. "Hiçbir istekte 500 dönmez" ilkesi ihlali.

**Kök neden ve kapsam:** `ApiKeyEndpoints.cs:57-64` ve
`GovernanceEndpoints.cs:190-195` örtük gövde bağlama. Kapsam ~10 `[FromBody]`
dosyası.

**🚨 Kritik tasarım notu:** `AddProblemDetails()` ve `UseExceptionHandler()`
**yalnız örnekte** kayıtlı (`samples/AgentPrism.Api/Program.cs:71,682`),
kütüphanede değil. Düzeltme `AgentPrism.AspNetCore` içinde yaşamalıdır ki
tüketici de alsın — örneğe yazmak kusuru gizler.

**Case:** `MT-CORE-009`, `MT-CORE-022`, `MT-SEC-054`, `MT-MCP-003`.
Doküman düzeltmesi de gerekir: `MT-CORE-022`'de `"Summarize"` geçersiz,
doğrusu `Summarization`; c2'nin "mesaj bilinmeyen strateji adını taşır"
beklentisi HTTP üzerinden **hiçbir zaman** gerçekleşemez.

### Aile H — Dar `catch` → çıplak `500` 🚨 Yüksek

**Kusur:** `HATA-S2-003`, `HATA-S3-005`. K-296'nın düzeltmesi **akışsız** kardeş
yolları kaçırmış; gerçek sağlayıcı hatası `502`/`upstream_error` yerine genel
`500` veriyor.

**Kök neden (üç yer):**
`OpenAICompat/OpenAIResponsesEndpoints.cs:213` ·
`OpenAICompat/OpenAIChatCompletionsEndpoints.cs:145` ·
`Endpoints/AgentEndpoints.cs:876` (`ExecuteBufferedAsync`).
Doğru davranan akışlı yol karşılaştırma için: `ResponsesStream.ExecuteAsync:279`.

**Case:** `MT-COMPAT-027`.

### Aile I — Kaynak üreteci sahte `mcp:` rozeti 🚨 Yüksek

**Kusur:** `HATA-S2-008`. Üreteç kod-tanımlı `[AgentPrismTool]` tool'larına
koşulsuz `source:"generated"` yazıyor; arayüz bunu `mcp:` rozetiyle gösteriyor.
Framework geneli, `dotnet new` şablonu dahil.

**Kök neden:** `src/AgentPrism.Generators/SourceWriter.cs:105-107`.
Sözleşme: `AgentPrismToolRegistration.cs:25,41`, `ToolDescriptor.cs:29-37`.
Görüntüleme: `screens/tools.tsx:71`.

**Case:** `MT-MCP-047`.

### Aile J — Agent editörü sağlayıcı yarışı 🚨 Yüksek

**Kusur:** `HATA-S4-009`. `Sağlayıcı` alanı aralıklı olarak yanlış (`anthropic`)
doluyor → **sessiz veri bozulması**.

**Kök neden:** `src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx:197-247`
(A: 197-237, B: 241-247 stale closure).

**Case:** `MT-UIAG-014`.

### Aile K — CSP `blob:` beyaz listede değil 🚨 Yüksek

**Kusur:** `HATA-S4-011`. Ek önizleme (`img-src`) ve "Seslendir" oynatımı
(`media-src` yönergesi **hiç yok**) tamamen kırık.

**Kök neden:** `src/AgentPrism.UI/Internal/EmbeddedUiProvider.cs:42-51`, `:112`.
Tüketen: `screens/playground.tsx:676-691`, `:729`, `:621`.

**Case:** `MT-UIAG-044`, `MT-UIAG-050`.

### Aile L — SPA geçişi run'ı `Running` bırakıyor 🚨 Yüksek

**Kusur:** `HATA-S4-012`. Akış bitmeden run sayfasına SPA geçişi run'ı kalıcı
`Running`'de asılı bırakıyor; `cancel` de `409` veriyor.

**Kök neden:** `screens/playground.tsx:78` ·
`src/AgentPrism.Core/Recording/RunRecordingAgent.cs:298-388` ·
`src/AgentPrism.Abstractions/Runs/RunReconciliationOptions.cs:14-18`.

**Case:** `MT-UIRUN-007` (dolaylı: `016`, `018`, `021`, `022`).

### Aile M — Loopback dışı erişimde ham JSON 🚨 Yüksek

**Kusur:** `HATA-S4-003`. "Erişim reddedildi" kartı yerine ham `ProblemDetails`
JSON görünüyor.

**Kök neden:** `src/AgentPrism.AspNetCore/Security/AgentPrismEndpointFilter.cs:72-80`.

**Case:** `MT-UI-008`. **Aile N ile aynı satırlara dokunur — birlikte yapılabilir.**

### Aile N — 112 `ProblemDetails` başlığı Türkçe 🚨 Yüksek

**Kusur:** `HATA-S4-007`. Sunucunun `ProblemDetails` başlıkları koda gömülü
Türkçe: **112 farklı `title:` literali, 26 dosya**. Örnek:
`AgentEndpoints.cs:513` `"Agent derlenemedi"` ·
`Security/AgentPrismEndpointFilter.cs:74` `"Uzak erisim kapali"`.

**Bu yeni bir karar değil, K-232'nin doğrudan ihlalidir.** K-232 zaten şunu der:
"`ProblemDetails` metinleri, doğrulama mesajları ve sağlayıcı hataları
**İngilizce kalır**. Paket NuGet.org'a uluslararası yayınlanır ve aynı hata
metni günlükte, testte ve destek kaydında aynı olmalıdır."

```bash
# Tam liste
grep -rhoP 'title:\s*"[^"]+"' --include='*.cs' src/AgentPrism.AspNetCore/ | sort -u
```

**Case:** `MT-UI-032`.

### Aile O — Yapılandırmada geçersiz değer sessizce düşüyor · Orta

**Kusur:** `HATA-S3-001..004`. `Bind()` geçersiz değeri doğrulayıcıya
**ulaşmadan** eliyor; doğrulayıcı dalı erişilemez ölü koda dönüyor.

**Kök neden:**
`src/AgentPrism.OpenAI/OpenAIProviderExtensions.cs:164-168` (göreli `Endpoint`),
`:191-198` (boş `Models[i].Name`) ·
`src/AgentPrism.Anthropic/AnthropicProviderExtensions.cs:136-139`, `:167` ·
`src/AgentPrism.Google/GoogleProviderExtensions.cs:137-140`, `:163`.

**Case:** `MT-OAI-010`, `MT-OAI-012`, `MT-PROV-012`, `MT-PROV-013`.

### Aile P — Bağlanmayan yapılandırma anahtarları · Orta

**Kusur:** `HATA-007`, `HATA-S4-020` **ve bu koşumda bulunan yeni bir kalem**.

- `Pricing` uzun/C# özellik adlı anahtar sessizce düşüyor —
  `src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs:841-842`
  yalnız kısa `"Input"`/`"Output"` okuyor (K-034 ihlali).
- 🆕 **`Observability.IncludeAgentVersionTag` hiç bağlanmıyor.**
  `AgentPrismOptions.cs:396`'da tanımlı ve
  `Recording/RunRecordingAgentDecorator.cs:109`'da **okunuyor**, ama
  `BindObservability`'de karşılığı **yok**. `HATA-K-007`
  (`RecordRunInput`) ile birebir aynı kusur sınıfı.

**🚨 Kalıp uyarısı:** `Bind()` elle yazılıyor (AOT için, bilinçli — K-021).
Bu, "yeni alan eklendi ama `Bind`'a eklenmedi" kusurunu **yapısal** kılar; üç
kez tekrarlandı. Düzeltmenin bir parçası olarak, her `AgentPrismOptions`
alanının bağlandığını doğrulayan bir **yansımalı test** ekle — tek kalıcı çözüm
budur. Tuzak `docs/hafiza/cekirdek-calistirma.md`'ye yazılır.

**Case:** `MT-CORE-065`, `MT-OBS-036`.

### Aile Q — Çapraz kiracı `404` dalı ölü kod · Orta

**Kusur:** `HATA-S2-005`. Çapraz kiracı `conversation` erişimi `404` yerine
**sessizce yeni oturum açıyor**; belgelenen reddetme dalı erişilemez.
**Veri sızıntısı YOK** (negatif kontrol `MT-SEC-035` yönetim API'sinin
etkilenmediğini doğruladı).

**Kök neden:** `OpenAICompat/OpenAICompatSupport.cs:100-113`
(`IsOwnedByTenantAsync`) · `InMemorySessionStore.cs:20`, `:53-59`.

**Case:** `MT-COMPAT-023`, `MT-COMPAT-036`, `MT-COMPAT-039`, `MT-COMPAT-043`.

### Aile R — Çalıştırma filtreleri · Orta

**İki ayrı kusur:**

- `HATA-S2-001` — `GET /api/runs?sessionId=X&includeChildren=true` alt
  çalıştırmaları hiç göstermiyor (`/tree` doğru).
  Kök neden: `Core/Storage/InMemoryRunStore.cs:339` ·
  `RunRecordingAgent.cs:481-485` (K-217, child `SessionId=null`) ·
  `RunEndpoints.cs:83-86` · **dört store**: `PostgresQueries.cs:409`,
  `SqliteQueries.cs:458`, `SqlServerQueries.cs:488`. → `MT-API-060`
- `HATA-S3-007` — `GET /api/runs?errorType=` **sessizce yok sayılıyor**;
  parametre hiç bağlanmıyor. `RunEndpoints.cs:42-53`. → `MT-GUARD-064`
  (alternatif: `docs/48-GUARDRAILS.md`'den kaldırmak — ama sessiz yok sayma
  K-034 ihlalidir, bağlamak doğrusu).

### Aile S — Idempotency replay başlık kaybı · Orta

**Kusur:** `HATA-S3-008`. Replay yalnız gövdeyi koruyor; `Location` ve
`Preference-Applied` kayboluyor.

**Kök neden:** `src/AgentPrism.Abstractions/Idempotency/IdempotencyTypes.cs:45-56` ·
`src/AgentPrism.AspNetCore/Idempotency/IdempotencyResults.cs:12-19`.

**Case:** `MT-JOB-083`.

### Aile T — MCP "connection refused" → `unknown_tool` · Orta

**Kusur:** `HATA-006`. `mcp_unreachable` yalnız gerçek timeout'ta tetikleniyor;
aktif red sessizce `unknown_tool`'a düşüyor.

**Kök neden:** `src/AgentPrism.Mcp/Internal/McpToolCatalog.cs:187` bağlantı
istisnalarını yutuyor. Black-hole adresle (`192.0.2.1`) doğru sonuç 5.02 sn'de
alınıyor — yani yalnız red yolu kırık.

**Case:** `MT-CORE-006`. Doküman düzeltmesi de var: yol
`PUT /api/mcp-servers/{name}`, alan `endpoint` (`url` değil).

### Aile U — Kalan arayüz kusurları · Orta/Düşük

| Kusur | Kök neden | Case |
|---|---|---|
| `HATA-S4-001` | `lib/api.ts:155-159` 401'de koşulsuz `setToken(null)` → `failed` daima false, ölü kod; `components/access-gate.tsx:53-55` | `MT-UI-003` |
| `HATA-S4-004` | `components/layout.tsx:157`, `command-palette.tsx:117-122` — `Esc` odağı açan öğeye döndürmüyor | `MT-UI-020` |
| `HATA-S4-006` | `components/layout.tsx:319-353`, `screens/settings.tsx:31,134-147` — tema `<select>` ile `ThemeToggle` state paylaşmıyor | `MT-UI-028` |
| `HATA-S4-008` | `components/ui.tsx:44-57` (`Panel`, :49), `screens/settings.tsx:296-301` (`Row`), `screens/tools.tsx:62-78` (:70) — 375px'te yatay taşma, 3 ayrı kök neden | `MT-UI-043` |
| `HATA-S4-010` | `screens/agent-editor.tsx:202-208`, `:268-273` — kod kökenli agent'ta `Ad` boş+salt-okunur → "Sürüm Kaydet" daima disabled | `MT-UIAG-016` |
| `HATA-S4-013` | `screens/run-detail.tsx:93-98`, `:356-358` — React Query `isPending` + `enabled:false` → kalıcı "Yükleniyor" | `MT-UIRUN-015` |
| `HATA-S4-014` | `src/AgentPrism.Core/Replay/RunReplayService.cs:125-133`, `:174-210` — `409` yerine sessiz boş `200` | `MT-UIRUN-030` |
| `HATA-S4-016` | `screens/session-detail.tsx:88` — `foldMessage(message)` mesaj başına çağrılıyor | `MT-UIRUN-039` |
| `HATA-S4-017` | `lib/router.tsx:74-83`, `:55-57`, `:120-141` — sorgu dizgili SPA gezinme kırık; tetikleyici `session-detail.tsx:58` | `MT-UIRUN-041` |
| `HATA-S4-019` | `lib/format.ts:136-144` (`money()`) — 4 basamak yuvarlama, gerçek >0 maliyeti `0,00` gösteriyor | `MT-OBS-003` |

### Aile V — Yetenek boşlukları · Karışık

Kullanıcı kararı 4: **MAF sınırı hariç hepsi kodlanır.**

| Kusur | Konu | Case |
|---|---|---|
| `HATA-S2-004` | `RunStatus.AwaitingInput` agent-türü run'larda yapısal olarak kullanılmıyor; onay bekleyen tool `/v1/responses`'ta `completed` görünüyor | `MT-COMPAT-029`, `MT-MCP-023` |
| `HATA-S4-018` | Playground `?sessionId=` okumuyor; var olan oturuma UI'dan devam etmenin **hiçbir** yolu yok (`playground.tsx:58`, `:147-150`; `session-detail.tsx`'te devam linki de yok) | `MT-UIRUN-043` |
| `HATA-S4-005` | `/` kısayolunun hedefi `input[data-search]` hiçbir ekranda yok — ölü özellik, `?` yardımında reklamı var (`layout.tsx:202`; `command-palette.tsx:394`, `locales/en.ts:135`, `tr.ts:134`) | `MT-UI-024` |
| `HATA-S4-002` | Sunucu kapalıyken "ulaşılamıyor" kartı hiç görünmüyor; same-origin statik+API mimarisinin sonucu | `MT-UI-005` |
| — | `MT-WF-062`: `respond` onayı `ozetleyici`'yi **sıfırdan** yeniden koşuyor (gerçek maliyet) ve `AwaitingInput`'ta bitiyor. **Hiç `HATA-K` numarası açılmamış**, K-400..407'ye dahil değil | `MT-WF-062` |
| `HATA-K-003` | K-401 **kısmi**: mesaj anlamlı ama run hâlâ `RunFailed`; zarif durdurma F-106'ya yazıldı | `MT-WF-071`, `MT-WF-073` |

**Faz adayına gidecek (kodlanmaz):** `HATA-S2-010` — workflow iptali gerçekten
çalışmıyor (`202` alınsa da `Canceled` değil `Completed` oluyor). AgentPrism
teli **doğru** (`WorkflowRunner.cs:356-368`, `:362`, `:589`, `:612`); şüpheli
kaynak MAF `AgentWorkflowBuilder.BuildSequential` / `StreamingRun.WatchStreamAsync`.
`docs/UCUNCU-FAZ-ADAYLARI.md`'ye **F-107** olarak yazılır ve `MT-RES-005`'in
**beklenen sonucu** gerçek davranışa göre düzeltilir.

---

## 7. Aile F — Uç → kapsam eşlemesi

`AgentsRead` = "katalog ve tanım okuma" · `RunsRead` = "çalıştırma okuma, olay
akışı, **istatistik**" · `RunsWrite` = "çalıştırma başlatma, iptal, **onay
verme**". Bu üç doküman satırı aşağıdaki yeniden kullanımın çoğunu taşır.

| Dosya | Yol | Verb | Kapsam | Gerekçe |
|---|---|---|---|---|
| Governance | `/api/mcp-servers/{name}/oauth/callback` | GET | **MUAF** | Sağlayıcı yönlendirmesi; tarayıcı bearer gönderemez. Güvenlik tek kullanımlık `state`'te. |
| Governance | `/api/tenants/current` | GET | **MUAF** | Çağıranın kendi kiracısını döner; kimlik bilgisinin zaten kodladığından fazlasını açmaz. |
| Governance | `/api/tenants` | GET | `PlatformRead` | Kiracılar arası kayıt listesi. |
| Governance | `/api/tenants/{slug}` | PUT | `PlatformAdmin` | Kiracı kaydı yazma. |
| Governance | `/api/tenants/{slug}` | DELETE | `PlatformAdmin` | Kiracı kaydı silme. |
| Governance | `/api/mcp-servers` | GET | `AgentsRead` | Uzak MCP sunucuları tool kataloğu kalemidir. |
| Governance | `/api/mcp-servers/{name}` | PUT | `AgentsAdmin` | Sunucu kaydı agent'lara tool enjekte eder — tanım düzenlemekle aynı etki alanı. |
| Governance | `/api/mcp-servers/{name}` | DELETE | `AgentsAdmin` | Aynı aile, yazma tarafı. |
| Governance | `/api/mcp-servers/refresh` | POST | `AgentsAdmin` | Tool kataloğunu yeniden yazar. |
| Governance | `/api/mcp-servers/{name}/prompts` | GET | `AgentsRead` | Uzak tanım kataloğu okuma. |
| Governance | `/api/mcp-servers/{name}/prompts/{prompt}` | POST | `AgentsRead` | POST ama anlamsal olarak salt okunur (şablon çözer). |
| Governance | `/api/mcp-servers/{name}/resources` | GET | `AgentsRead` | Katalog okuma. |
| Governance | `/api/mcp-servers/{name}/resources/read` | GET | `AgentsRead` | Katalog içeriği okuma. |
| Governance | `/api/mcp-servers/{name}/oauth/start` | POST | `SecurityAdmin` | Üçüncü taraf kimlik bilgisi alır ve saklar. |
| Governance | `/api/approvals/rules` | GET | `RunsRead` | "Bir daha sorma" kuralları run onay durumudur. |
| Governance | `/api/approvals/rules/{ruleId}` | DELETE | `RunsWrite` | Kural iptali onay durumu yazmasıdır (daraltır, gevşetmez). |
| Scheduling | `/api/schedules` | GET | `PlatformRead` | İşletim yapılandırması okuma. |
| Scheduling | `/api/schedules/{name}` | GET | `PlatformRead` | Aynı. |
| Scheduling | `/api/schedules/{name}` | PUT | `PlatformAdmin` | Yapılandırma yazma. |
| Scheduling | `/api/schedules/{name}` | DELETE | `PlatformAdmin` | Yapılandırma silme. |
| Scheduling | `/api/schedules/{name}/trigger` | POST | `RunsWrite` | Hemen bir run başlatır. |
| Scheduling | `/api/jobs` | GET | `RunsRead` | İşler dayanıklı run kuyruğudur. |
| Scheduling | `/api/jobs/{id}` | GET | `RunsRead` | Aynı. |
| Scheduling | `/api/jobs/{id}/cancel` | POST | `RunsWrite` | "iptal" `RunsWrite` dokümanında adı geçer. |
| Retention | `/api/retention` | GET | `PlatformRead` | Politika okuma. |
| Retention | `/api/retention/preview` | GET | `PlatformRead` | Açıkça hiçbir silme yapmaz. |
| Retention | `/api/retention/run` | POST | `PlatformAdmin` | Tüm ailelerde yıkıcı temizlik; hiçbir aile kapsamı ima etmemeli. |
| Retention | `/api/retention/history` | GET | `PlatformRead` | Geçmiş temizlik koşumları. |
| Retention | `/api/retention/{target}` | GET | `PlatformRead` | Politika okuma. |
| Retention | `/api/retention/{target}` | PUT | `PlatformAdmin` | Politika yazma (veri ömrünü sessizce kısaltır). |
| Retention | `/api/retention/{target}` | DELETE | `PlatformAdmin` | Politika silme. |
| Webhook | `/api/webhooks` | GET | `PlatformRead` | Abonelik yapılandırması okuma. |
| Webhook | `/api/webhooks/{name}` | GET | `PlatformRead` | Aynı. |
| Webhook | `/api/webhooks/{name}` | PUT | `PlatformAdmin` | Dışa çıkış hedefi belirler. |
| Webhook | `/api/webhooks/{name}` | DELETE | `PlatformAdmin` | Yapılandırma silme. |
| Webhook | `/api/webhooks/{name}/test` | POST | `PlatformAdmin` | Yapılandırılmış URL'ye trafik üretir. |
| Webhook | `/api/webhooks/{name}/deliveries` | GET | `PlatformRead` | Teslim geçmişi. |
| Catalog | `/api/tools` | GET | `AgentsRead` | "Katalog okuma". |
| Catalog | `/api/models` | GET | `AgentsRead` | Sağlayıcı kataloğu. |
| Catalog | `/api/stats` | GET | `RunsRead` | "istatistik" `RunsRead` dokümanında adı geçer. |
| Catalog | `/api/stats/timeseries` | GET | `RunsRead` | Aynı. |
| Catalog | `/api/stats/errors` | GET | `RunsRead` | Aynı. |
| Catalog | `/api/stats/recalculate-costs` | POST | `RunsWrite` | Her run kaydının maliyet alanlarını yeniden yazar. |
| OpenAIConversations | `/v1/conversations` | POST | `RunsWrite` | Oturum (run durumu) oluşturur. |
| OpenAIConversations | `/v1/conversations/{id}` | GET | `RunsRead` | Oturum üstverisi. |
| OpenAIConversations | `/v1/conversations/{id}` | DELETE | `RunsWrite` | Oturumu ve alttaki store'u siler. |
| OpenAIConversations | `/v1/conversations/{id}/items` | GET | `RunsRead` | Konuşma geçmişi okuma. |
| OpenAIResponses | `/v1/responses` | POST | `RunsWrite` | Run başlatır; `/api/agents/{name}/run`'ın aynası. |
| OpenAIChatCompletions | `/v1/chat/completions` | POST | `RunsWrite` | Aynı. `ExternalInvoke` **değil**: o kapsam BİZİM MCP/A2A yüzeyimiz içindir. |
| Voice | `/api/voice/health` | GET | `PlatformRead` | Sağlayıcı işletim durumu. |
| Voice | `/api/voice/voices` | GET | `AgentsRead` | Sağlayıcı kataloğu, `/api/models` gibi. |
| Voice | `/api/voice/sessions` | GET | `RunsRead` | Konuşma bağlantı kayıtları + metrikler. |
| Voice | `/api/voice/speak` | POST | `RunsWrite` | Model çağırır, para harcar, ek yazar. |
| Skill | `/api/skills` | GET | `AgentsRead` | Skill'ler agent tanımıdır. |
| Skill | `/api/skills/{name}` | GET | `AgentsRead` | Aynı. |
| Skill | `/api/skills/{name}` | PUT | `AgentsAdmin` | "Tanım yazma". |
| Skill | `/api/skills/{name}` | DELETE | `AgentsAdmin` | Cascade tanım silme. |
| Session | `/api/sessions` | GET | `RunsRead` | Run'ların ürettiği konuşma durumu. |
| Session | `/api/sessions/{id}` | GET | `RunsRead` | Aynı. |
| Session | `/api/sessions/{id}` | DELETE | `RunsWrite` | Run durumu yazma. |
| Session | `/api/sessions/{id}/branch` | POST | `RunsWrite` | Yeni run durumu oluşturur. |
| Quota | `/api/quotas` | GET | `PlatformRead` | İşletim yapılandırması. |
| Quota | `/api/quotas` | PUT | `PlatformAdmin` | Kota yükseltmek bir harcama kararıdır. |
| Quota | `/api/quotas/{id}` | DELETE | `PlatformAdmin` | Harcama sınırını kaldırır. |
| Quota | `/api/quotas/usage` | GET | `PlatformRead` | Dönem içi tüketim. |
| Attachment | `/api/attachments` | POST | `RunsWrite` | Ekler oturuma bağlı run girdisidir (`?sessionId=`). |
| Attachment | `/api/attachments/{id}` | GET | `RunsRead` | `/api/runs/{id}/input` ile aynı hassasiyet sınıfı (o zaten `RunsRead`). |
| Attachment | `/api/attachments` | GET | `RunsRead` | Run yük nesnelerini listeler. |
| Attachment | `/api/attachments/{id}` | DELETE | `RunsWrite` | Run yükü yazma. |
| SkillScriptGrant | `/api/skill-script-grants` | GET | `SecurityAdmin` | Hangi script'in çalışabileceğini açık eder; verme ile aynı yüzey. |
| SkillScriptGrant | `/api/skill-script-grants` | POST | `SecurityAdmin` | Keyfi script çalıştırma izni verir — yetki genişletme. |
| SkillScriptGrant | `/api/skill-script-grants/{skillName}` | DELETE | `SecurityAdmin` | Aynı yüzey. |
| Observability | `/api/runs/{id}/trace` | GET | `RunsRead` | Run span ağacı. |
| Observability | `/api/runs/{id}/tools` | GET | `RunsRead` | Run tool çağrıları. |
| Observability | `/api/tools/usage` | GET | `RunsRead` | Toplulaştırılmış run istatistiği. |
| ApiKey | `/api/api-keys` | GET | `SecurityAdmin` | Kimlik bilgisi envanteri. |
| ApiKey | `/api/api-keys` | POST | `SecurityAdmin` | Kimlik üretir — yükselme kapısı; hiçbir şey bunu ima etmemeli. |
| ApiKey | `/api/api-keys/{id}` | DELETE | `SecurityAdmin` | İptal = tüm anahtarlar üzerinde erişilebilirlik denetimi. |
| ModelHealth | `/api/models/health` | GET | `PlatformRead` | Sağlayıcı işletim durumu. |
| ModelHealth | `/api/models/health/{provider}` | GET | `PlatformRead` | Aynı. |
| Audit | `/api/audit` | GET | `AuditRead` | Her ailede kim-ne-yaptı; hiçbir aile kapsamı açmamalı. |
| Audit | `/api/audit/{entity}` | GET | `AuditRead` | Aynı. |
| Diagnostics | `/api/diagnostics` | GET | `PlatformRead` | Kurulum öz denetimi; zaten opt-in uç. |
| Meta | `/api/meta` | GET | **MUAF** | `AllowAnonymous`, **filtresiz** grupta eşlenmiş — metadata inert olurdu. |

### 7.1 Yeni enum üyeleri (sona eklenir, 13+; var olanlar yeniden numaralanmaz)

| Değer | Üye | Gerekçe |
|---|---|---|
| 13 | `PlatformRead` | Platform işletim yapılandırması ve sağlık okuma: kiracı kaydı, kota, saklama, zamanlama, webhook, teşhis, sağlayıcı sağlığı. Hiçbir mevcut aile bu nesneleri kapsamıyor. |
| 14 | `PlatformAdmin` | Aynısının yazılması/silinmesi + saklama temizliğini çalıştırma. Okuma/yazma ayrımı diğer tüm çiftlerin (Knowledge/Workflows/Evals/Experiments) kurduğu desen. |
| 15 | `SecurityAdmin` | Yetki üreten/uzatan yüzeyler: API anahtarı, skill script izni, MCP OAuth başlatma. Kendini yükseltebilen tek kapsam — nadir kalmalı. |
| 16 | `AuditRead` | Denetim izi okuma. "Kota okuyabilen" bir anahtara ima ettirilemez. |

**Reddedilenler:** webhook/MCP-server yazmalarını `SecurityAdmin`'e katlamak
(onu rutin verilen bir kapsam yapar, yükselme kapısı değerini yok ederdi) ·
ayrı `SessionsRead/Write` ve `AttachmentsRead/Write` (oturum ve ek **run
yüküdür**; `/api/runs/{id}/input` zaten `RunsRead`) · OpenAI-uyumlu yollarda
`ExternalInvoke` (dokümanı onu bizim MCP/A2A **gelen** yüzeyimizle sınırlar).
Üç üye isteniyorsa katlanabilecek **tek** üye `AuditRead` → `PlatformRead`'dir;
Read/Admin ayrımı ve `SecurityAdmin` katlanamaz.

### 7.2 Muafiyetler (4, her biri gerekçeli)

1. `GET /api/meta` — `metaGroup` (`AgentPrismEndpointRouteBuilderExtensions.cs:83-84`)
   **`AgentPrismEndpointFilter` taşımaz**; oraya kapsam koymak sessizce etkisiz
   olurdu — hiç koymamaktan kötü.
2. `GET /api/mcp-servers/{name}/oauth/callback` — `callbackGroup`,
   `requireBearerToken: false` (satır 318). Sağlayıcı yönlendirmesi bearer
   taşıyamaz; API anahtarı bağlamı hiç oluşmaz.
3. `GET /api/tenants/current` — çağıranın kendi kimliğinin tarifi. Kapsam
   eklemek her dar kapsamlı anahtar için zararsız bir açılış yoklamasını
   kırardı. (Tek testin bağımlı olduğu muafiyet — bkz. §7.3.)
4. `UiEndpoints` `/` ve `/{**path}` — SPA kabuğu, `requireBearerToken: false`
   grubunda (satır 294). `<script src>` `Authorization` gönderemez; kabuk veri
   taşımaz, her veri ucu tam korumalı kalır.

**Kapsam dışı ama takip gerektirir:** `VoiceConversationEndpoint`
(`GET /api/voice/sessions/{id}/stream`, `Voice/VoiceConversationEndpoint.cs:44`)
token'ı **statik** `AuthToken`'a karşı kendisi doğrular; API anahtarları onu
zaten hiç açamaz, metadata eklemek bugün inert olur.

### 7.3 Test etkisi

**Kesin kırılan (1):** `tests/AgentPrism.AspNetCore.FunctionalTests/ApiKeyAuthenticationTests.cs:228`
`Kapsamsiz_ucta_denetim_yoktur` — `/api/api-keys`'in kapsamsız olduğunu
varsayar; `SecurityAdmin` ile `403` olur. `/api/tenants/current`'a taşınır
(filtrenin arkasındaki tek muaf uç) ve `AgentsRead` → `403` iddiası eklenir.

**`/api/tenants/current` muaf tutulmazsa kırılır:** `ApiKeyAuthenticationTests.cs:106`
`Kiraci_basliktan_degil_anahtardan_cozulur`.

**Güvenli doğrulandı:** diğer işlevsel testler ya `Authorization` göndermiyor
(test host `AuthToken` tanımlamaz → `ApiKeyRequestContext` hiç kurulmaz) ya da
zaten kapsamlı yollara gidiyor. `SqlApiKeyStore.ParseScope`
(`Enum.Parse<ApiKeyScope>`, `SqlApiKeyStore.cs:150`) adları **metin** olarak
saklar — üye eklemek migration gerektirmez.

**Eklenecek kapsam:**
1. Her yeni üye için yanlış-kapsam → `403` ve doğru-kapsam → `2xx` (8 test).
2. Yükselme kapısı: `PlatformAdmin` anahtarı `POST /api/api-keys`'ten `403` almalı.
3. Sessizce gerileyecek yeniden kullanımlar: `RunsRead` → `GET /api/stats` `200`
   ve `POST /api/stats/recalculate-costs` `403`; `RunsWrite` →
   `POST /api/approvals/{id}/decide` `200`.
4. 🚨 **Regresyon çiti (en yüksek değerli):** `EndpointDataSource`'u test
   host'tan çözen ve korumalı gruptaki **her** ucun `ApiKeyScopeRequirement`
   taşıdığını doğrulayan yansımalı test — yalnız 4 muafiyet açık, yorumlu bir
   izin listesinde. 21 dosyalık boşluğun geri gelmesini bu engeller.
5. Muafiyet testleri: `/api/meta` ve SPA kabuğu tek ilgisiz kapsamlı anahtarla
   erişilebilir olmalı.

### 7.4 Kod dışı zorunlu güncelleme

Arayüz hâlâ yalnız ilk 5 kapsamı listeler:
`src/AgentPrism.UI/frontend/src/lib/types.ts:1614` ·
`components/api-key-panel.tsx:21` · `locales/en.ts` + `tr.ts` `apiKeys.scope.*`.
🚨 Yeni yerel anahtarlar `lib/i18n.test.ts:132-137` `identicalOnPurpose`
kümesine de eklenmezse **test kırılır**.
`docs/53-KIRACI-API-ANAHTARLARI.md:435` ("— (kapsamsız uç)" tablosu) ve `:445`
("`RequireApiKeyScope` uygulanan uçlar") yanlışa döner, yeniden üretilmelidir.

---

## 8. Doküman düzeltmesi gereken case'ler (kod değişmez)

KOSUM-PLANI §2.1 istisnası: doküman ile kod çelişirse **doküman yanlıştır**.
Beklenen sonuç koda göre düzeltilir, gerekçe `Gerçek sonuç`a yazılır.

| Case | Düzeltme |
|---|---|
| `MT-PG-001` | İzlek B ile test edilemez: `Program.cs:639` boş bağlantı dizesinde `UsePostgreSql()`'i hiç çağırmaz → doğrulayıcıya ulaşılmaz. Case İzlek A/C'ye taşınmalı. |
| `MT-PG-050` | PostgreSQL kısmı TAM geçti; yalnız gövde `Degraded`. `AgentPrismHealthCheck.cs:70-77` `Healthy` için `ModelProviders.Any(Healthy)` arar, `echo` o listede yok. Beklenti `Degraded` olmalı. |
| `MT-PG-053` | Aynı kök neden (`HATA-003`). "echo yeterli" ön koşul varsayımı yanlış. |
| `MT-PKG-011` | Enjekte edilen klasik `namespace{}` bloğu dosya-kapsamlı `AgentPrismToolAttribute.cs` ile çakışıp `CS8955` ile **build'i** kırıyor; senaryonun "build farkına varmaz" kurgusu üretilemiyor. `dotnet format` kendisi doğru çalıştı (exit 2, 32× IDE0055). |
| `MT-PKG-023` | `<requireLicenseAcceptance>` nuspec'e hiç yazılmıyor; NuGet yokluğu `false` saydığı için **davranışsal etki yok**. Dokümanın "vardır" iddiası literal doğrulanamaz. |
| `MT-PKG-062` | Case 4 AOT-uyumlu paket bekliyor; gerçek küme **8** ve `AGENTS.md` zaten "Sekiz paket uyumludur" diyor. Düzeltilecek taraf bu test dosyası. |
| `MT-PKG-081` | Davranış doğru; yalnız isim beklentisi eski: `ISessionStore` → `AuditingSessionStore`, `IToolRegistry` → `ToolRegistry` (önek yok). |
| `MT-CORE-004` | Senaryo `provider:"echo"` kullanıyor; `EchoModelProvider` `ProviderSettings`'i hiç okumaz. Gerçek mekanizma `ModelProviderSettings.Validate` — `anthropic`/`google`/`azure` ile TAM beklenen `invalid_setting` alınır. |
| `MT-CORE-023` | İstisna doğru atıldı, mesaj farklı. `AgentPrismServiceCollectionExtensions.cs:381` `IAgentSkillCatalog`'u **koşulsuz** `TryAddSingleton` eder → dokümanın öncülü yanlış. `AgentDefinitionCompiler.cs:306` ve `:1137` dalları **ölü kod** (ayrı temizlik adayı). |
| `MT-CORE-024` | Mekanizma çalışıyor; tek sapma tool adının `"Var"` (metot adı) olması. `IAgentPrismBuilder.cs:47` `AddTool(Delegate, string? name = null)` boş adda metot adını kullandığını belgeliyor; `[AgentPrismTool]` yalnız `AddToolsFrom<T>()` tarafından okunur. Doküman iki idiomu karıştırmış. |
| `MT-EVAL-084` | Ön koşul ("`author` NULL değil") yanlış: `run_scores_target_author_idx` `author`'ı `COALESCE` etmiyor → upsert olmaz. `MT-EVAL-085`'in kabul edilen davranışıyla aynı. |
| `MT-TEST-044` | `args.Services is null` **her zaman false** (MAF `EmptyServiceProvider` gönderir). Düzeltilmiş iddia K-218'in hâlâ geçerli olduğunu doğruluyor. Üretim değişikliği yok. |
| `MT-RES-005` | Karar 4 gereği: `HATA-S2-010` faz adayı (F-107) olur; beklenen sonuç gerçek davranışa göre düzeltilir. |

---

## 9. Yalnız yeniden koşum (kusur zaten kapalı)

Kod değişmez; case koşulur ve `Durum` güncellenir.

| Case | Kapatan düzeltme |
|---|---|
| `MT-SKILL-059`, `060`, `061`, `062`, `063`, `070` | `HATA-K-002` → **K-400**. `AgentPrismSkillsSource.cs:20` artık `TypeInfoResolver` veriyor. `MT-SKILL-063` kritik bir güvenlik iddiasını (ortam değişkeni izolasyonu) taşıyor ve **hiç ampirik kanıtı yok** — önceliklidir. |
| `MT-API-064`, `MT-UIRUN-032` | `HATA-K-007`/`HATA-S2-002`/`HATA-S4-015` → **K-406**. `AgentPrismServiceCollectionExtensions.cs:1781` `RecordRunInput`'u bağlıyor. |
| `MT-PKG-010` | Kök neden **`f36eeaf`**'te düzeltildi (ses turu `commit` yarışı). Dört kapı artık yeşil; case yeniden koşulup `Geçti` işaretlenmeli. |

---

## 10. Atlandı case'leri (30)

**9'u Azure — kullanıcı kararı 1 gereği ⏭ Atlandı KALIR**, gerekçe yazılır:
`MT-PROV-080` … `MT-PROV-088`.

Kalan 21'i ortam kurulumuyla koşulabilir:

| Case | Gereken |
|---|---|
| `MT-OAI-053` | Ollama kur (`brew install ollama`) |
| `MT-MM-047` | `sesli-asistan` fixture'ının `maxOutputTokens:1024` sınırını yükselt — 6000 karakter tek tamamlamaya sığmıyor |
| `MT-MM-089` | `appsettings.json:31` `AllowRemoteAccess=true` ile koş |
| `MT-RES-014` | İkinci örneği farklı portta başlat |
| `MT-RES-029` | Örnekte rol politikalarını kaydet |
| `MT-UI-010`, `011`, `021`, `MT-UIAG-002` | Reader rollü API anahtarı fixture'ı (dosya `13`'ün fixture'ı) |
| `MT-UIAG-012` | 11 skill kaydı (dosya `14` artık koşuldu) |
| `MT-UI-038`, `MT-UIRUN-001`, `MT-OBS-001`, `MT-OBS-011` | Temiz şema — **reset artık serbest**, şerit izolasyonu bitti |
| `MT-UI-042` | `npx playwright install webkit` |
| `MT-UI-026`, `MT-UI-031`, `MT-UIRUN-019` | Bağımsız Playwright betiği (`colorScheme`, `locale`, CDP offline) — S1-9'un ses case'lerinde kurulan emsal |
| `MT-SQL-071` | `chmod` çalışan sürecin AÇIK dosya tanıtıcılarını etkilemez (Unix izni `open()` anında denetler). Salt-okunur mount gerekir ya da doküman düzeltmesi. |

---

## 11. Kapanış (tüm aileler bitince)

1. Altı `SONUCLAR-*.md` tek bir `SONUCLAR.md`'de birleştirilir; hatalar önem
   sırasına dizilir.
2. `KOSUM-PLANI.md` §1 durum tablosu gerçek sayımla değiştirilir (§3'teki üç
   yanlış iddia düzeltilir), §8 kapanış kaydı yazılır.
3. `00-INDEKS.md` §7 `Koşum` sütunu 25 satır için güncellenir.
4. Her kusur `docs/KARARLAR.md`'ye **K-408**'den başlayarak gerekçesiyle
   yazılır; indeks `python3 scripts/dokuman-bakim.py` ile üretilir.
5. Tuzaklar `docs/hafiza/` alan dosyalarına yazılır — özellikle:
   - elle `Bind()` yazılan bir alan unutulur (Aile P) → `cekirdek-calistirma.md`
   - `dotnet test --no-build` kırık build'de eski ikiliyi koşar → `test-altyapisi.md`
   - zincirlenmemiş `Blob.arrayBuffer()` sıra/yarış üretir → `frontend.md`
6. `docs/UCUNCU-FAZ-ADAYLARI.md`'ye `HATA-S2-010` **F-107** olarak eklenir
   (son aday F-106).
7. `git worktree remove ../ap-s1 …` — bu koşumda worktree kalmadı, atlanabilir.

---

## 12. Bitti tanımı

```bash
# 1. Hicbir dosyada Kaldi kalmamali
cd docs/manuel-test && grep -c '[☒☑] Kaldı' [0-2]*.md | grep -v ':0'
# (bos cikti = 0 Kaldi)

# 2. Beklemede kalmamali
grep -l '[☒☑] Beklemede' [0-2]*.md

# 3. Atlandi YALNIZ 9 Azure case'i olmali
grep -h '[☒☑] Atlandı' [0-2]*.md | wc -l   # 9

# 4. Dort kapi
cd /Users/farukatasoy/Desktop/projects/AgentPrism
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore

# 5. Dokuman butcesi
python3 scripts/dokuman-bakim.py

# 6. secret taramasi — faz-tamamlama skill'indeki komut
```
