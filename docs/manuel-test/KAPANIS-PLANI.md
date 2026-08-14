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
| ☑ Geçti | **1011** |
| ☒ **Kaldı** | **55** |
| ⏭ Atlandı | **30** |
| ☐ Beklemede | **1** (`MT-UIRUN-019`) |

### Biten işler

| İş | Commit | Kapanan case |
|---|---|---|
| **Adım 0** — dört kapı kırmızıydı; ses turu `commit` çerçevesi kendi sesinin önüne geçiyordu | `f36eeaf` | (`MT-PKG-010`'un kök nedeni — case'in kendisi yeniden koşulmalı) |
| **Aile A** — şema kapısı + teşhis ucu | `2126aab` | `MT-PG-025`, `MT-PG-034`, `MT-PG-051`, `MT-PG-052` |
| **Aile B** — Guard maskelemesi `RunStarted`'da ham kalıyor | `a3d1dea` | `MT-GUARD-041`, `MT-GUARD-053` |
| **Aile C** — Eşzamanlı ilk istekte oturum lost update | `f287b12` | `MT-CORE-054` |
| **Aile D** — `T[]` parametreli tool derlenmiyor; kök neden zaten `75990fd`'de kapanmıştı, kod değişikliği yok, yalnız case yeniden koşuldu | (bu koşum, docs-only) | `MT-PKG-044` |
| **Aile E** — İki kalıcılık sağlayıcısı; `MigrationHostedService.IsWinningProvider()` zaten kapatmış, kod değişikliği yok, yalnız case yeniden koşuldu | (bu koşum, docs-only) | `MT-PKG-082` |
| **Aile F** — 21 endpoint dosyasında kapsam denetimi yok; 4 yeni `ApiKeyScope` üyesi (`PlatformRead/Admin`, `SecurityAdmin`, `AuditRead`) + attenuation | `c96006c` | `MT-MCP-051`, `MT-RES-028`, `MT-JOB-090` (3/4 — `MT-MCP-052` ayrı bulgu olarak Kaldı kalır, bkz. §6) |
| **Aile G** — JSON çözümleme hatası `400` yerine `500`; kütüphane çapında `RequestBodyBinding.ReadAsync<T>` — 21 dosya, implicit binding kullanan 9 EK uç dahil | `0b28210` | `MT-CORE-009`, `MT-CORE-022`, `MT-SEC-054`, `MT-MCP-003` |
| **Aile H** — Dar `catch` → çıplak `500`; üç dosyada (`AgentEndpoints.ExecuteBufferedAsync`, `OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`) akışsız yolun dar `when` filtresi kaldırıldı (K-296/K-384'ün akışsız kardeşlere tamamlanması) | `12f163f` | `MT-COMPAT-027` |
| **Aile I** — Kaynak üreteci `[AgentPrismTool]` kayıtlarına koşulsuz `source: "generated"` yazıyordu; `SourceWriter.WriteAggregator` artık `source` argümanını hiç geçirmiyor (kayıt belgelenen `null` varsayılanını kullanıyor) | `cbecd59` | `MT-MCP-047` |
| **Aile J** — Agent editörünün iki `useEffect`'i aynı commit'te çözülünce sağlayıcıyı ezen yarış; guard functional `setForm` updater'ının içine, `current` (taze state) üzerinden karar verecek şekilde taşındı | `c0175b9` | `MT-UIAG-014` |
| **Aile K** — CSP `blob:` şemasını hiçbir yönergede beyaz listeye almıyordu; `EmbeddedUiProvider.ContentSecurityPolicy`'ye `img-src`'e `blob:` + yeni `media-src 'self' blob:;` eklendi | `c1efa8a` | `MT-UIAG-044`, `MT-UIAG-050` |
| **Aile L** — SPA geçişinin erken `AbortController.abort()`'u `RunStarted` yazıldıktan sonra ama try/finally güvenlik ağına girmeden çalıştırmayı sonsuza dek `Running`de bırakıyordu; `BeginRunAsync` `CreateScope` (saf) + `WriteRunStartAsync` (G/Ç) olarak ikiye bölündü, ikincisi güvenlik ağının içine taşındı | `a61f999` | `MT-UIRUN-007` |
| **Aile M** — Kabuk loopback kısıtından muaf değildi, loopback dışı erişimde React hiç başlamıyordu; `AgentPrismEndpointFilter`'a `requireLoopback` parametresi eklendi, kabuk grubu bearer token gibi loopback'ten de muaf tutuldu | `58c3268` | `MT-UI-008` |
| **Aile N** — `ProblemDetails` başlıkları koda gömülü Türkçe'ydi (113 `title:` literali); `src/AgentPrism.AspNetCore/` + besleyen Core/Workflows/Generators dosyaları + OpenAI-uyumlu/A2A/MCP/Voice yüzeyleri İngilizce'ye çevrildi, kaynak taramalı regresyon çiti eklendi | `e9f9006` | `MT-UI-032` |

### Kalan aileler

Sıra: Kritik → Yüksek → Orta/Düşük. Bir sonraki oturum **M** ile başlar.

| Aile | Önem | Konu | Case | Durum |
|---|---|---|---|---|
| ~~A~~ | Kritik | Şema kapısı, teşhis ucu | 4 | ✅ `2126aab` |
| ~~B~~ | Kritik | Guard maskelemesi `RunStarted`'da ham kalıyor | 2 | ✅ `a3d1dea` |
| ~~C~~ | Kritik | Eşzamanlı ilk istekte oturum lost update | 1 | ✅ (bu koşum) |
| ~~D~~ | Kritik | `T[]` parametreli tool derlenmiyor | 1 | ✅ (bu koşum) |
| ~~E~~ | Kritik | İki kalıcılık sağlayıcısı (K-183) | 1 | ✅ (bu koşum) |
| ~~F~~ | Yüksek | 21 endpoint dosyasında kapsam denetimi yok | 4 | ✅ (bu koşum, 3/4 — `MT-MCP-052` §6'da yeni bulgu) |
| ~~G~~ | Yüksek | JSON çözümleme hatası `400` yerine `500` | 4 | ✅ (bu koşum) |
| ~~H~~ | Yüksek | Dar `catch` → çıplak `500` | 1 | ✅ (bu koşum) |
| ~~I~~ | Yüksek | Kaynak üreteci sahte `mcp:` rozeti | 1 | ✅ (bu koşum) |
| ~~J~~ | Yüksek | Agent editörü sağlayıcı yarışı | 1 | ✅ (bu koşum) |
| ~~K~~ | Yüksek | CSP `blob:` beyaz listede değil | 2 | ✅ (bu koşum) |
| ~~L~~ | Yüksek | SPA geçişi run'ı `Running` bırakıyor | 1 | ✅ (bu koşum) |
| ~~M~~ | Yüksek | Loopback dışı erişimde ham JSON | 1 | ✅ (bu koşum) |
| ~~N~~ | Yüksek | 113 `ProblemDetails` başlığı Türkçe | 1 | ✅ (bu koşum) |
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

**Toplam:** 35 (kod) + 13 (doküman) + 8 (yeniden koşum) + 1 = **57**. (Aile F
bitti: 51 → 47; Aile G bitti: 47 → 43; Aile H bitti: 43 → 42; Aile I bitti:
42 → 41; Aile J bitti: 41 → 40; Aile K bitti: 40 → 38; Aile L bitti: 38 → 37;
Aile M bitti: 37 → 36; Aile N bitti: 36 → 35. `MT-MCP-052` bu sayıma dahil
değildir — Kaldı kalır, ayrı bir bulgu olarak izlenir, gelecekte kendi
ailesini gerektirebilir.)

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

### ~~Aile D~~ — `T[]` parametreli tool derlenmiyor 🚨 Kritik ✅ (bu koşum — zaten kapalıydı)

**Kusur:** `MT-PKG-044`. `APG0003` diziyi "desteklenir" ilan ediyor ama
`T[]` parametreli hiçbir tool metodu derlenmiyor (`CS1503: IReadOnlyList<T>` → `T[]`).
`int[]` ile de tekrar üretildi.

**Kök neden (o zaman):**
`src/AgentPrism.Generators/ParameterTypeValidator.cs:98-111`
(`TryGetArrayElementType` çıplak dizi/arayüz ayrımını kaybediyor) ·
`src/AgentPrism.Generators/SourceWriter.cs:143` (her zaman `GetArray(...)`,
`.ToArray()` eklenmiyor).

**Bu koşumda bulunan:** Kusur zaten `75990fd`'de kapanmış — §3'ün uyardığı
"kapalı ama sonuç dosyası güncellenmedi" örneklerinden biri daha.
`ParameterModel.IsConcreteArray` alanı ekli, `SourceWriter.cs:147`
`.ToArray()` sarmalıyor, ve genişletilmiş bir regresyon testi
(`GeneratedOutputTests.cs` `Ciplak_dizi_parametresi_ToArray_ile_cevrilir_ve_uretilen_kod_derlenir`,
`int[]`/`string[]`/`IReadOnlyList<int>` karışımını gerçek Roslyn derlemesinden
geçiriyor) zaten repoda. Canlı doğrulama tazelenmiş
`AgentPrism.0.0.0-preview.0.138` paketiyle `~/agentprism-manuel/uretec`'te
tekrarlandı: `APG0003` sayısı 0, derleme 0 hata ile bitti. Kod değişikliği
gerekmedi; yalnız case'in `Gerçek sonuç`/`Durum` alanları güncellendi.

**Case:** `MT-PKG-044` ✅.

### ~~Aile E~~ — İki kalıcılık sağlayıcısı 🚨 Kritik ✅ (bu koşum — zaten kapalıydı)

**Doğrulandı:** `MigrationHostedService.IsWinningProvider()` bu kusuru zaten
kapatmış — kod yorumu `MT-PKG-082`'yi adıyla anıyor ve
`_registrations` paylaşılan listesindeki **son** kaydı (tüm sağlayıcılar
arası) kazanan sayıyor; kaybeden migration'a hiç dokunmadan çıkıyor.
İki entegrasyon testi (`Kaybeden_saglayici_migration_uygulamaz`,
`Kazanan_saglayici_migration_uygular`, gerçek PostgreSQL'e karşı) zaten repoda.

Canlı doğrulama `samples/AgentPrism.Api/Program.cs`'e geçici bir
`UseSqlite(...)` satırı eklenerek (MT-PG-034 deseni, sonra `git checkout` ile
geri alındı) yapıldı: iki log satırı artık **tutarlı** (ikisi de "SQLite
kazandı" diyor — önceki koşumda çelişkiliydi), PostgreSQL şeması
**oluşmadı** (sayım 0), yalnız SQLite gerçekten yazdı (45 tablo). Kod
değişikliği gerekmedi.

**Case:** `MT-PKG-082` ✅.

### ~~Aile F~~ — API anahtarı kapsam denetimi yok 🚨 Yüksek (güvenlik) ✅ (bu koşum)

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

**Uygulanan tasarım (bu koşum):** Tüm 21 dosyanın `~100` ucuna
`RequireApiKeyScope` eklendi (§7 tablosu — `ApprovalEndpoints.cs`'in üç ucu
tabloda satır olarak yoktu, `RunsRead`/`RunsWrite` tanımlarındaki "onay
verme" ifadesiyle aynı akıl yürütmeyle dolduruldu). 4 yeni `ApiKeyScope`
üyesi eklendi: `PlatformRead`, `PlatformAdmin`, `SecurityAdmin`, `AuditRead`
(§7.1). `ApiKeyEndpoints.CreateAsync`'e yetki uzatma (attenuation) denetimi
eklendi: istek bir API anahtarıyla doğrulandıysa, o anahtarın kendi
taşımadığı bir kapsam için yeni anahtar üretilemez (`400`). Arayüz
(`types.ts`, `api-key-panel.tsx`, `en.ts`/`tr.ts`) zaten 8 eski kapsamı
(Knowledge/Workflows/Evals/Experiments) da listelemiyordu — Aile F ile
birlikte **16 kapsamın tamamı** arayüze eklendi (yalnız 4 yenisi değil).
Regresyon çiti: `ApiKeyScopeCoverageTests.cs` — korumalı gruptaki HER ucu
`EndpointDataSource` üzerinden yansımalı tarar, yorumlu 3+3 muafiyet dışında
hiçbiri `ApiKeyScopeRequirement`siz kalamaz.

**🚨 Ampirik olarak kapanmayan bulgu — `MT-MCP-052`.** Denetimin 1. özelliği
(yukarıda) doğrulandı: `RequireApiKeyScope` yalnız `ApiKeyRequestContext`
doluysa (istek bir API anahtarıyla doğrulandıysa) çalışır. `MT-MCP-052`'nin
sömürdüğü yol düz statik `AuthToken`'dır — bu yol `ApiKeyRequestContext`'i
hiç kurmaz, dolayısıyla metadata eklemek onu etkilemez. Canlı PostgreSQL'e
karşı doğrulandı: aynı curl (statik token ile `PUT /api/mcp-servers/...`)
Aile F sonrası da `HTTP 200`. Kullanıcı kararıyla (bu koşum) `MT-MCP-052`
**Kaldı kalır** — ayrı, açık bir bulgu olarak izlenir (kök neden: statik
token + kayıtlı olmayan rol politikaları = fiilen tam yetki). Bu nedenle
Aile F **3/4** case kapatır; `MT-MCP-051`, `MT-RES-028`, `MT-JOB-090` ✅.

**Değişen dosyalar:** `ApiKeyScope.cs` (+4 üye), 21 endpoint dosyası
(`RequireApiKeyScope` eklendi), `ApiKeyEndpoints.cs` (+attenuation),
`ApiKeyAuthenticationTests.cs` (`Kapsamsiz_ucta_denetim_yoktur` artık
`/api/tenants/current`'ı hedefliyor + yeni `Kapsamli_ucta_yanlis_kapsam_403_doner`),
`docs/openapi/agentprism.json` (yenilendi). **Yeni dosyalar:**
`ApiKeyScopeCoverageTests.cs`, `ApiKeyScopeEnforcementTests.cs`.

**Regresyon testleri:** `ApiKeyScopeCoverageTests.cs`
(`Korumali_gruptaki_her_uc_kapsam_tasir_veya_acikca_muaftir`,
`Muafiyet_listesindeki_her_satir_gercekten_haritada_var`) ·
`ApiKeyScopeEnforcementTests.cs` (4 yeni kapsamın nokta doğrulamaları,
yeniden kullanım regresyonları, yükselme kapısı, attenuation — 12 test).

### ~~Aile G~~ — JSON çözümleme hatası `400` yerine `500` 🚨 Yüksek ✅ (`0b28210`)

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

**Önce ampirik yeniden üretim.** `MT-CORE-009`/`MT-CORE-022` (`/api/agents/validate`)
zaten kapalı çıktı — önceki bir dalgada (`HATA-S1-007`) `AgentEndpoints`
gövdeyi elle okuyacak şekilde değiştirilmiş, sonuç dosyaları güncellenmemiş
(§3'ün uyardığı örüntü). `MT-SEC-054`/`MT-MCP-003` canlı doğrulandı: gerçekten
500.

**🚨 İlk tasarım denemesi — YETERSİZ çıktı.** İlk yaklaşım yalnız bir global
ara yazılımdı: `MapAgentPrism` içine, `BadHttpRequestException` yakalayıp
`InnerException is JsonException` ise `400` `ProblemDetails` yazan bir
`app.Use(...)` bloğu eklendi (`JsonBindingProblemMiddleware`, "AgentPrism"
etiketli uçlarla sınırlı — `ITagsMetadata` denetimi). Development'ta (`dotnet
run`, MT-SEC-054/MT-MCP-003'ün doğrulandığı ortam) çalıştı. Ama minimal API'nin
otomatik gövde bağlaması `JsonException`'ı yalnız
`RouteHandlerOptions.ThrowOnBadRequest` açıkken fırlatır — bu bayrağın
**varsayılanı yalnız `Development` ortamında açıktır** (reflection ile
`Microsoft.AspNetCore.Routing.RouteHandlerOptions` üzerinde doğrulandı).
**Production'da** (gerçek dağıtımların çoğu, `ASPNETCORE_ENVIRONMENT` ayarsız)
istisna hiç atılmaz; minimal API kendisi gövdesiz bir `400` yazar (500 değil,
ama `ProblemDetails` de değil) ve ara yazılım bu yolu **hiç göremez**.
`services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true)` ile
bayrağı global açmak da reddedildi: bu, `IOptions<RouteHandlerOptions>`'ın TEK
paylaşılan örneğini değiştirir — tüketicinin **kendi** ilgisiz uçlarını da
etkiler ve onlarda da (tüketicinin kendi `UseExceptionHandler()`'ı
`BadHttpRequestException.StatusCode`'u okumuyorsa) aynı 500 kusurunu üretebilir.

**Uygulanan tasarım (K-409 adayı, kapanışta yazılacak):** Gövde okuma
ortamdan (Development/Production) **bağımsız** olmalı — `AgentEndpoints`'in
zaten kullandığı "elle oku, `JsonException`'ı yakala" deseni tek doğru
çözümdür. Yeni paylaşılan yardımcı `RequestBodyBinding` (statik, iki metot):
`ReadAsync<T>` (zorunlu gövde — boş gövde de hata sayılır) ve
`ReadOptionalAsync<T>` (`T?` parametreler için — gövde yoksa hata SAYILMAZ).
Govde-yokluk denetimi `Content-Length` başlığına **bakmaz** — `TestServer`
altında istemcinin gönderdiği `Content-Length` güvenilir değildir (dolu bir
gövde bir regresyonla "boş" sayıldı, ampirik olarak yakalandı); bunun yerine
`HttpRequest.HasJsonContentType()` denetlenir — gövde/tip hiç yoksa
`ReadFromJsonAsync` `JsonException` DEĞİL `InvalidOperationException` fırlatır,
bu da doğru ayırt edicidir. Kütüphanenin gövde-bağlayan **tüm** uçları (21
dosya) bu deseni kullanacak şekilde değiştirildi — yalnız tahmin edilen ~10
`[FromBody]` dosyası değil. `[FromBody]` grep'i, örtük bağlama kullanan uçları
**kaçırdığı** MT-MCP-003'ün kendi bulgusuyla kanıtlandığı için, gerçek kapsam
`docs/openapi/agentprism.json`'daki `requestBody` taşıyan **her** rota tek tek
çapraz kontrol edilerek çıkarıldı: tahmin edilen dosyalara ek olarak
`AgentEndpoints.RollbackAsync`, `AgentEndpoints`'in `/api/agents/{name}/run`
uç noktası, `SkillEndpoints.SaveAsync`, `SessionEndpoints.BranchSessionAsync`,
`KnowledgeEndpoints.UploadAsync`/`SearchAsync`, `VoiceEndpoints.SpeakAsync`,
`GovernanceEndpoints`'in `/api/tenants/{slug}` PUT ve
`/api/mcp-servers/{name}/prompts/{prompt}` POST uçları da aynı kusuru
taşıyordu (implicit binding, `[FromBody]` özniteliği yok). İlk ara yazılım
(`JsonBindingProblemMiddleware`) **kaldırılmadı** — artık yalnız savunma
katmanıdır: elle okumayı unutan gelecekteki bir uç için, yalnız
Development'ta 500'ü önler; gerçek düzeltme her zaman `RequestBodyBinding`'dir.

**Case:** `MT-CORE-009` ✅ (zaten kapalıydı, yeniden doğrulandı),
`MT-CORE-022` ✅ (aynı, doküman düzeltmesiyle — "Summarize" tiposu,
c2'nin "mesajı aynen taşır" beklentisi asla gerçekleşemez), `MT-SEC-054` ✅,
`MT-MCP-003` ✅.

**Yeni dosyalar:** `AgentPrism.AspNetCore/Internal/RequestBodyBinding.cs`,
`AgentPrism.AspNetCore/Internal/JsonBindingProblemMiddleware.cs`,
`tests/AgentPrism.AspNetCore.FunctionalTests/JsonBindingProblemMiddlewareTests.cs`.
**Değişen dosyalar:** `AgentPrismEndpointRouteBuilderExtensions.cs`
(+ara yazılım kaydı) ve gövde bağlayan 21 uç dosyası: `ApiKeyEndpoints.cs`,
`ApprovalEndpoints.cs`, `EvalEndpoints.cs` (×4), `ExperimentEndpoints.cs` (×2),
`QuotaEndpoints.cs`, `RetentionEndpoints.cs`, `RunEndpoints.cs` (×3),
`SchedulingEndpoints.cs` (×2), `SkillScriptGrantEndpoints.cs`,
`WebhookEndpoints.cs`, `WorkflowEndpoints.cs` (×4), `GovernanceEndpoints.cs`
(×3 — mcp-servers, tenants, mcp-prompts), `AgentEndpoints.cs` (×2 — rollback,
run), `SkillEndpoints.cs`, `SessionEndpoints.cs`, `KnowledgeEndpoints.cs` (×2),
`VoiceEndpoints.cs`. **🚨 Tuzak:** tipli parametreyi (`T request`) `HttpContext
httpContext` ile değiştirmek OpenAPI `requestBody` üstverisini SESSİZCE
DÜŞÜRÜR — .NET'in üstveri üretimi gövde şemasını endpoint PARAMETRE
TİPİNDEN çıkarır; elle okuyan bir endpoint'in artık böyle bir parametresi
yoktur. `AgentEndpoints`'in zaten bildiği çözüm uygulandı: her rotaya
`.Accepts<T>("application/json")` (zorunlu) veya `.Accepts<T>(true,
"application/json")` (opsiyonel, `T?` parametreler için) eklendi —
`docs/openapi/agentprism.json` yeniden üretildi, 35 `requestBody` de
korundu. Tek kalıcı fark: opsiyonel gövdelerde önceki `oneOf: [null, $ref]`
şeması artık düz `$ref` (`required` alanı zaten yok, yani "gövde
gerekli değil" anlamı korunuyor — yalnız temsil sadeleşti).

**Regresyon testleri:** `JsonBindingProblemMiddlewareTests.cs` — 3 senaryo:
eskiden `[FromBody]` otomatik bağlama kullanan bir uç (`ApiKeyEndpoints`),
eskiden örtük bağlama kullanan bir uç (`GovernanceEndpoints`, MT-MCP-003'ün
kendisi) ve enum dışı bir tür uyuşmazlığı (`RetentionEndpoints`) — üçü de artık
`400` `ProblemDetails` döner. Ayrıca tam test paketi (`dotnet test`,
948 fonksiyonel/entegrasyon testi) bu değişiklikten sonra **sıfır regresyon**
ile geçti; `RequestBodyBinding.ReadOptionalAsync`'in `Content-Length`
tuzağı bu koşumda üç test ailesinde (`ExperimentEndpointTests`,
`EvalEndpointTests`, `WorkflowEndpointTests`) yakalanıp düzeltildi —
tuzak `docs/hafiza/aspnetcore-json.md`'ye yazıldı.

### ~~Aile H~~ — Dar `catch` → çıplak `500` 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S2-003`, `HATA-S3-005`. K-296'nın düzeltmesi **akışsız** kardeş
yolları kaçırmış; gerçek sağlayıcı hatası `502`/`upstream_error` yerine genel
`500` veriyor.

**Kök neden (üç yer):**
`OpenAICompat/OpenAIResponsesEndpoints.cs:213` ·
`OpenAICompat/OpenAIChatCompletionsEndpoints.cs:145` ·
`Endpoints/AgentEndpoints.cs:876` (`ExecuteBufferedAsync`).
Doğru davranan akışlı yol karşılaştırma için: `ResponsesStream.ExecuteAsync:279`.

**Uygulanan tasarım (`12f163f`):** K-296/K-384'ün akışlı kardeşlerde
(`ResponsesStream.ExecuteAsync`, `ChatCompletionsStream.ExecuteAsync`,
`AgentEndpoints.ExecuteStreamingAsync`) zaten uyguladığı desen üç akışsız yola
da taşındı: dar `when` filtresi (yalnız `AgentPrismException`/
`InvalidOperationException`/`HttpRequestException`) kaldırıldı, düz
`catch (Exception ex)` bırakıldı. `OperationCanceledException` her üç yerde de
AYRI ve ÖNCE yakalanır (istemci bağlantıyı kesince 502 yazmaya çalışılmaz) —
`AgentEndpoints.ExecuteBufferedAsync`'te bu catch zaten vardı (satır 873);
`OpenAIResponsesEndpoints`/`OpenAIChatCompletionsEndpoints`'in akışsız yollarında
YOKTU, dar filtre `OperationCanceledException`'ı da örtük biçimde dışarı
sızdırıyordu — genel `catch (Exception ex)`'e geçince bu iki yere de açık bir
`catch (OperationCanceledException) { throw; }` eklendi ki davranış (iptal
sessizce yukarı fırlar, 502 gövdesi üretilmez) DEĞİŞMESİN.

**Değişen dosyalar:** `AgentEndpoints.cs` (`ExecuteBufferedAsync`'in son
`catch`'i genişletildi), `OpenAIResponsesEndpoints.cs`,
`OpenAIChatCompletionsEndpoints.cs` (ikisine de `catch (OperationCanceledException)
{ throw; }` + genişletilmiş `catch (Exception ex)` eklendi). **Yeni dosyalar:**
`tests/AgentPrism.AspNetCore.FunctionalTests/Infrastructure/ThrowingModelProvider.cs`
(gerçek sağlayıcı SDK istisnalarını taklit eden `IModelProvider` — `Exception`'dan
DOGRUDAN türer, whitelist'e uymaz), `ProviderOutageErrorHandlingTests.cs` (üç ucu
da kapsar; fix geri alınıp koşulduğunda üçü de KIRMIZI verdiği ampirik olarak
doğrulandı).

**Canlı doğrulama:** Sahte Azure `Endpoint`/`ApiKey`/`DefaultDeployment` ile
`azure-destek` agent'ı geçici olarak kayıtlı hale getirilip (K-183/Aile E
deseniyle aynı, gerçek Azure kimliği HİÇ kullanılmadı) temiz `mt_fin` şemasına
karşı gerçek bir DNS çözümleme hatası tetiklendi: `POST /v1/responses` →
`HTTP 502` + `error.type: upstream_error`; `POST /api/agents/azure-destek/run`
(`Idempotency-Key` ile akışsız yol) → `HTTP 502` +
`title: "Agent calistirilamadi"`, `application/problem+json`. Geçici `secret`'lar
doğrulama sonrası `dotnet user-secrets remove` ile temizlendi.

**Case:** `MT-COMPAT-027` ✅.

**Regresyon testleri:** `ProviderOutageErrorHandlingTests.cs`
(`Akissiz_calistirma_ucu_saglayici_hatasinda_502_ProblemDetails_doner`,
`Responses_ucu_saglayici_hatasinda_502_upstream_error_doner`,
`ChatCompletions_ucu_saglayici_hatasinda_502_upstream_error_doner`).

### ~~Aile I~~ — Kaynak üreteci sahte `mcp:` rozeti 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S2-008`. Üreteç kod-tanımlı `[AgentPrismTool]` tool'larına
koşulsuz `source:"generated"` yazıyor; arayüz bunu `mcp:` rozetiyle gösteriyor.
Framework geneli, `dotnet new` şablonu dahil.

**Kök neden:** `src/AgentPrism.Generators/SourceWriter.cs:105-107`.
Sözleşme: `AgentPrismToolRegistration.cs:25,41`, `ToolDescriptor.cs:29-37`.
Görüntüleme: `screens/tools.tsx:71`.

**Önce ampirik yeniden üretim.** Canlı sunucuda (`samples/AgentPrism.Api`,
`ProjectReference` ile üreteci doğrudan çalıştırıyor) `GET /agentprism/api/tools`
kod-tanımlı `get_order_status`/`cancel_order`/`list_recent_orders` için
`"source": "generated"` döndürdüğü doğrulandı — kusur hâlâ açıktı.

**Uygulanan tasarım:** Tasarım kararı gerektirmeyen, tek satırlık kök neden
düzeltmesi. `SourceWriter.WriteAggregator` her kayıt için `source: "generated"`
argümanını koşulsuz üretiyordu; bu argüman tamamen kaldırıldı.
`AgentPrismToolRegistration` constructor'ının varsayılanı zaten `source: null`'dır
ve bu, hem kendi XML belgesinin ("Kodda tanimli tool'larda null") hem de
`ToolDescriptor.Source`'un belgesinin ("yalniz uzak MCP sunucusundan gelen
tool'larda") tarif ettiği sözleşmedir — `McpTenantTools.cs:79`
(`Source = registration.Source`) ve `tools.tsx:71`
(`{tool.source != null && <Badge>mcp: {tool.source}</Badge>}`) zaten `null`'ı
doğru ele alıyordu, aradaki tek kırık halka üreteçti. Kapsam MCP sunucularını
etkilemez — uzak MCP tool'ları `McpTenantTools.cs` içinde ayrı bir yoldan,
gerçek sunucu adıyla `Source` alıyor, bu değişmedi.

**Canlı doğrulama:** Aynı `GET /agentprism/api/tools` isteği düzeltme
sonrası `get_order_status`/`cancel_order`/`list_recent_orders`/`list_voices`/
`speak`/`transcribe` için `"source": null` döndürdü.

**Değişen dosyalar:** `src/AgentPrism.Generators/SourceWriter.cs`
(`WriteAggregator`'daki `source: "generated"` argümanı kaldırıldı). **Yeni
dosya yok.**

**Case:** `MT-MCP-047` ✅.

**Regresyon testleri:**
`tests/AgentPrism.Generators.UnitTests/GeneratedOutputTests.cs`
`Isaretli_statik_metot_icin_kayit_uretilir` artık üretilen toplayıcı
dosyasının `source:` literalini hiç taşımadığını doğruluyor (`ShouldNotContain`).

### ~~Aile J~~ — Agent editörü sağlayıcı yarışı 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S4-009`. `Sağlayıcı` alanı aralıklı olarak yanlış (`anthropic`)
doluyor → **sessiz veri bozulması**.

**Kök neden:** `src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx:197-247`
(A: 197-237, tanımı yükler; B: 241-247, `stale closure`).

**Önce ampirik yeniden üretim.** Kod okuması `MT-UIAG-014`'ün kayıtlı
gözleminin (definition `openai`, `<select>` `anthropic` gösterdi) doğru kök
nedene işaret ettiğini doğruladı: canlı `/api/models` katalog sırası
alfabetiktir (`anthropic, google, openai, openai-responses, openrouter`) —
gözlenen yanlış değer (`anthropic`) tam olarak alfabetik ilk kayıt.

**İki effect'in etkileşimi:** A (tanımı yükleyen) `existing.isSuccess`
olunca **tüm** formu (`provider` dahil) tek `setForm(tamObje)` çağrısıyla
yazar ve `ready=true` yapar. B (varsayılan sağlayıcı atayan) `providers`
kataloğu yüklenince ve form'da sağlayıcı yoksa ilk kaydı atar — ama guard
(`if (form.provider.length > 0 …) return;`) effect gövdesinde render
anının **stale** `form` kapanışını okuyordu; uyguladığı `setForm((current)
=> ({...current, provider: data[0]}))` ise `current`'ı — yani **taze**
state'i — alıp koşulsuz eziyordu. İki effect aynı React commit'inde
(iki query aynı anda/cache'ten çözülünce) sıraya girdiğinde: A'nın
`setForm` çağrısı kuyruğa girer, B (aynı render'ın stale `form.provider ===
''` kapanışını gördüğü için) guard'ı geçer ve kendi `setForm`'unu da
kuyruğa ekler; React bu ikisini sırayla uyguladığında B'nin updater'ı
A'nın az önce yazdığı **doğru** `current.provider`'ı görür ama hiç
kontrol etmeden `providers.data[0]` ile ezer.

**Uygulanan tasarım:** Guard, effect gövdesinden functional `setForm`
updater'ının İÇİNE taşındı. Yeni saf fonksiyon `withDefaultProvider(current,
providerNames)` kararı `current.provider` üzerinden verir — bu her zaman
uygulanma anındaki taze state'tir, hangi effect'in hangi commit'te önce/
sonra çalıştığından bağımsızdır. B artık yalnız `setForm((current) =>
withDefaultProvider(current, providerNames))` çağırır; A'nın yazdığı
sağlayıcı artık hiçbir zaman ezilmiyor.

**Değişen dosya:** `src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx`
(`FormState`/`emptyForm` test edilebilirlik için `export` edildi,
`withDefaultProvider` yeni saf fonksiyon). **Yeni dosya:**
`src/AgentPrism.UI/frontend/src/screens/agent-editor.test.ts`.

**Regresyon testi:** `agent-editor.test.ts` üç senaryo — boş formda
varsayılan atama, boş katalogda no-op, ve **eşzamanlı bir güncellemenin
zaten yazdığı sağlayıcının ezilmediği** (asıl kusuru kapsayan senaryo).
Bu üçüncü test, fix'ten önceki koda karşı (eski `setForm` çağrısı geçici
olarak geri konularak) **KIRMIZI** verdiği ampirik olarak doğrulandıktan
sonra fix geri uygulandı.

**Canlı doğrulama:** Sahte bir kilitlenme nedeniyle (Playwright MCP
tarayıcısı başka bir çalışan oturumca kilitliydi, o oturum bozulmasın
diye zorlanmadı) tarayıcı üzerinden birebir tekrar üretim YAPILMADI.
Bunun yerine canlı Postgres'e karşı, katalogun alfabetik ilki OLMAYAN bir
sağlayıcıyla (`google`) agent oluşturulup `PUT`/`GET api/agents/
mt-uiag-014-test` ile provider'ın bozulmadan döndüğü doğrulandı — düzeltme
artık commit sırasından bağımsız olduğu için (kusurun kendisi zamanlamaya
bağlıydı, düzeltme değil) birim testi asıl kanıt sayıldı.

**Case:** `MT-UIAG-014` ✅.

### ~~Aile K~~ — CSP `blob:` beyaz listede değil 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S4-011`. Ek önizleme (`img-src`) ve "Seslendir" oynatımı
(`media-src` yönergesi **hiç yok**) tamamen kırık.

**Kök neden:** `src/AgentPrism.UI/Internal/EmbeddedUiProvider.cs:42-51`, `:112`.
Tüketen: `screens/playground.tsx:676-691`, `:729`, `:621`.

**Önce ampirik yeniden üretim.** Kod okuması iki case'in kayıtlı gözlemini
doğruladı: `ContentSecurityPolicy` sabiti `img-src 'self' data:;` taşıyordu
(`blob:` yok) ve `media-src` yönergesi hiç tanımlı değildi — `default-src
'none'`'a düşüyordu. `useAttachmentPreview` (`playground.tsx:676-691`) ve
`SpeakButton` (`playground.tsx:621-637`) ikisi de bearer token taşıyamayan
dogrudan bir uç yerine `fetch` ile çekilen baytları `URL.createObjectURL`
ile sarar — kaynak her zaman bir `blob:` URL'idir; bu yüzden ikisi de aynı
kök nedenden kırılıyordu.

**Uygulanan tasarım:** Tasarım kararı gerektirmeyen, tek satırlık kök neden
düzeltmesi. `img-src`'e `blob:` eklendi, yeni bir `media-src 'self' blob:;`
yönergesi eklendi. Kapsam yalnız bu iki yönergeydi — diğer yönergeler
(`script-src`, `connect-src` vb.) kaynağı zaten `'self'`e sabitliyor ve
`blob:` gerektiren başka bir tüketici yok.

**Canlı doğrulama:** `samples/AgentPrism.Api`'ye gerçek Postgres'e karşı
`curl -si -H "Authorization: Bearer ..." http://localhost:5090/agentprism/`
düzeltme sonrası `Content-Security-Policy: ... img-src 'self' data: blob:;
media-src 'self' blob:; ...` döndürdü.

**Değişen dosya:** `src/AgentPrism.UI/Internal/EmbeddedUiProvider.cs`
(`ContentSecurityPolicy` sabiti). **Doküman:** `docs/05-AGENTPRISM-UI.md`'deki
örnek `curl` çıktısı yeni başlığa göre güncellendi. **Yeni dosya yok.**

**Case:** `MT-UIAG-044` ✅, `MT-UIAG-050` ✅.

**Regresyon testleri:** `tests/AgentPrism.Ui.E2ETests/UiTests.cs` — yeni,
tarayıcısız `Kabuk_CSP_basligi_blob_URLlerini_ek_onizlemesi_ve_seslendirme_icin_beyaz_listeye_alir`
kabuk yanıtının `Content-Security-Policy` başlığını doğrudan kontrol eder
(saniyeler içinde, HTTP GET). İki mevcut Playwright testi
(`Playground_dosya_yuklenir_onizleme_gorunur_ve_calistirma_devam_eder`,
`Playground_yaniti_seslendirilir_ve_ses_ogesi_calar`) güçlendirildi: ikisi
de `document.addEventListener('securitypolicyviolation', ...)` ile ilgili
kaynağın (ek önizlemesi / ses oynatıcısı) yüklenmesi sırasında GERÇEKTEN
hiçbir CSP ihlali olmadığını doğrular — sadece `src` niteliğinin `blob:`
ile başlaması yeterli sayılmıyordu (kusur tam olarak buradan kaçmıştı: eski
testler yalnız niteliği kontrol ediyordu, tarayıcının kaynağı gerçekten
yüklemesine izin verilip verilmediğini değil). Üç test de fix geri alınıp
koşulduğunda KIRMIZI verdiği ampirik olarak doğrulandıktan sonra fix geri
uygulandı.

### ~~Aile L~~ — SPA geçişi run'ı `Running` bırakıyor 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S4-012`. Akış bitmeden run sayfasına SPA geçişi run'ı kalıcı
`Running`'de asılı bırakıyor; `cancel` de `409` veriyor.

**Kök neden — katman sırası:** `screens/playground.tsx:78`
(`AbortController.abort()`, bileşen unmount) run'ı başlatan POST isteğini
kesiyor. `src/AgentPrism.Core/Recording/RunRecordingAgent.cs`'in eski
tek-parça `BeginRunAsync`'i (`RunCoreAsync`/`RunCoreStreamingAsync`'in
try/finally güvenlik ağının **DIŞINDA** çağrılıyordu) `RunStarted` olayını
depoya yazdıktan **SONRA** `SaveInputAsync`'i çağırıyordu; `SaveInputAsync`
(ve altındaki `RunEventWriter.StartAsync`/`AppendAsync`) `OperationCanceledException`'ı
BİLEREK yutmaz — `catch (Exception ex) when (ex is not OperationCanceledException)`.
İstemci bu dar pencerede (RunStarted zaten yazılmış, model çağrısı henüz
başlamamış) bağlantıyı keserse istisna hiçbir güvenlik ağını tetiklemeden
metodun dışına fırlıyordu: `CompleteAsync` hiç çağrılmıyor, `IRunCancellationRegistry`
kaydı `using` disposal ile sessizce siliniyor (`cancel` bu yüzden `409`
veriyor), ve `RunReconciliationOptions.Enabled` varsayılanı `false` olduğu
için (örnek uygulama da açmıyor) run kendiliğinden asla iyileşmiyordu.

**Uygulanan tasarım:** Tasarım kararı gerektirmeyen, yapısal bir kök neden
düzeltmesi. `BeginRunAsync` ikiye bölündü: `CreateScope` (yalnız bellek
içinde `RunScope` kurar — G/Ç yapmaz, hiçbir zaman istisna atmaz veya iptal
edilmez) ve `WriteRunStartAsync` (asıl G/Ç — `runs` satırını ve
`RunStarted` olayını yazar, girdiyi kaydeder). Kapsam artık G/Ç'den **ÖNCE**
kurulur; `WriteRunStartAsync` her iki metodun da (`RunCoreAsync`,
`RunCoreStreamingAsync`) try/finally güvenlik ağının **İÇİNDE** çağrılır —
böylece bu adımda oluşan bir iptal, tam bir `RunScope` ile zaten var olan
HATA-S1-015 mekanizmasının aynısı tarafından `Canceled` olarak kapatılır.
`RunEventWriter`'ın `OperationCanceledException`'ı BİLEREK yutmayan
davranışı değiştirilmedi — o davranış mid-stream iptalinin doğru şekilde
yukarı akmasını sağlayan mevcut, çalışan mekanizmanın ta kendisidir; kusur
yalnız `BeginRunAsync`'in bu mekanizmanın dışında kalmasıydı.

**Canlı doğrulama:** Gerçek Postgres'e karşı (`samples/AgentPrism.Api`)
`support` agent'ına 11 istek `curl --max-time` ile 2ms-120ms aralığında
erken kesildi; 5'i sunucuya ulaşıp bir `runs` satırı açtı, **5'i de**
`Canceled` ile kapandı (`eventCount:2`; `run_events` sorgusu: seq 0
`RunStarted`, seq 1 "Calistirma iptal edildi." — hiçbiri `Running`de asılı
kalmadı). Aynı sunucuda kesilmemiş normal bir istek `Completed` ile doğru
şekilde tamamlandı — regresyon yok.

**Değişen dosya:** `src/AgentPrism.Core/Recording/RunRecordingAgent.cs`
(`BeginRunAsync` → `CreateScope` + `WriteRunStartAsync`, çağrı yerleri
`RunCoreAsync`/`RunCoreStreamingAsync`'te try/finally'nin içine taşındı).
**Yeni dosya:** `tests/AgentPrism.Core.UnitTests/Recording/RunStartCancellationTests.cs`.

**Case:** `MT-UIRUN-007` ✅ (dolaylı: `016`, `018`, `021`, `022` — bu dördü
zaten Geçti işaretliydi, HATA-S4-012'nin kalıcı `Running` run'ını yalnız
sabit veri olarak kullanmışlardı, kod değişikliğinden etkilenmezler).

**Regresyon testleri:** `RunStartCancellationTests.cs` — akışsız
(`Girdi_kaydi_sirasinda_iptal_akissiz_calistirmayi_Canceled_yazar_Running_de_asili_birakmaz`)
ve akışlı (`..._akisli_...`) iki senaryo, `SaveInputAsync`'in tam bu
penceresinde iptali deterministik olarak yeniden üretir (sahte
`IRunInputStore.SaveAsync` çağrıldığı anda kendi belirtecini iptal edip o
belirtecten fırlatır). Fix geri alınıp koşulduğunda ikisi de
`run.Status == RunStatus.Running` ile KIRMIZI verdiği ampirik olarak
doğrulandıktan sonra fix geri uygulandı.

### ~~Aile M~~ — Loopback dışı erişimde ham JSON 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S4-003`. "Erişim reddedildi" kartı yerine ham `ProblemDetails`
JSON görünüyor.

**Kök neden:** `src/AgentPrism.AspNetCore/Security/AgentPrismEndpointFilter.cs:72-80`.
Kabuk grubu (`MapUi`, `AgentPrismEndpointRouteBuilderExtensions.cs:301-302`)
bearer token denetiminden muaf (`requireBearerToken: false`) ama loopback
kısıtından **muaf değildi** — sınıfın kendi XML yorumu (eski satır 42-49)
bearer-token için tam olarak aynı sorunu ("kabuk kilitlenirse kullanıcı token
girebileceği ekranı hiç göremez") çözdüğünü anlatıyordu ama aynı çözüm
loopback denetimine hiç uygulanmamıştı. `/api/meta` bu filtrenin **tamamından**
muaf (K-010) olduğu için sorun orada görünmüyordu; yalnız kabuk (statik
varlıklar) etkileniyordu.

**Önce ampirik yeniden üretim.** Kod okuması kusurun hâlâ açık olduğunu
doğruladı: `AgentPrismEndpointFilter.InvokeAsync` satır 72'deki loopback
denetimi `requireBearerToken` parametresinden bağımsız her uca (kabuk dahil)
aynı şekilde uygulanıyordu.

**Uygulanan tasarım:** Tasarım kararı gerektirmeyen, bearer-token muafiyetiyle
simetrik bir düzeltme. `AgentPrismEndpointFilter`'a yeni bir `requireLoopback`
parametresi eklendi (varsayılan `true` — mevcut tüm gruplar davranışını
korur). `MapUi` artık `requireLoopback: false` ile kuruyor. Veri uçlarındaki
asıl korumalı grup (`AgentPrismEndpointRouteBuilderExtensions.cs:116-117`,
`api/agents` vb.) değişmedi — loopback kısıtı orada `true` kalıyor; gerçek
koruma hâlâ oradan geliyor. `access-gate.tsx` zaten bu akışı (kabuk açılır →
`probe` sorgusu bir veri ucuna gider → `403` alırsa "Access denied" kartını
sunucunun ham metniyle gösterir) destekleyecek şekilde yazılmıştı — eksik olan
yalnızca kabuğun kendisinin loopback dışı istekte hiç açılamamasıydı.

**Canlı doğrulama.** `AuthToken` bu proje için `dotnet user-secrets`'ta kalıcı
kayıtlı olduğundan (bearer token katmanını devre dışı bırakıp yalnız loopback
katmanını izole etmek için) geçici olarak `dotnet user-secrets remove` ile
kaldırıldı, doğrulama sonrası aynı değerle geri eklendi. Gerçek Postgres'e
karşı, makinenin LAN IP'sinden (`http://192.168.1.102:5090/agentprism/`,
`AllowRemoteAccess=false`) gerçek bir Playwright oturumuyla: kabuk artık
yükleniyor (200, HTML), React çalışıyor, `AccessGate` "Access denied" kartını
sunucunun ham `detail` metniyle ve `access.denied.remote` ipucuyla gösteriyor.
Aynı LAN IP'den veri ucu (`/api/agents`) hâlâ `403`/`"Uzak erisim kapali"`
döndürüyor — koruma kaybolmadı, yalnızca kabuk artık React'i başlatabiliyor.

**Değişen dosyalar:** `src/AgentPrism.AspNetCore/Security/AgentPrismEndpointFilter.cs`
(+`requireLoopback` parametresi, `InvokeAsync`'teki loopback denetimi buna
bağlandı), `src/AgentPrism.AspNetCore/AgentPrismEndpointRouteBuilderExtensions.cs`
(`MapUi` çağrısına `requireLoopback: false` eklendi, ilgili XML yorumları
güncellendi). **Yeni dosya:**
`tests/AgentPrism.AspNetCore.FunctionalTests/Infrastructure/FakeUiProvider.cs`
(bu proje `AgentPrism.UI`'a bağımlı değil; kabuk davranışını gerçek varlık
derlemesi olmadan test etmek için).

**Case:** `MT-UI-008` ✅.

**Regresyon testi:** `tests/AgentPrism.AspNetCore.FunctionalTests/SecurityTests.cs`
`Kabuk_loopback_disi_istekte_hala_yuklenir_HATA_S4_003` — aynı istekte kabuğun
(`/agentprism/`) loopback dışı IP'den `200` döndüğünü, veri ucunun
(`/agentprism/api/agents`) aynı IP'den hâlâ `403` döndürdüğünü tek testte
doğrular. Fix geri alınıp koşulduğunda kabuk isteği `Forbidden` ile KIRMIZI
verdiği ampirik olarak doğrulandıktan sonra fix geri uygulandı.

### ~~Aile N~~ — 112 `ProblemDetails` başlığı Türkçe 🚨 Yüksek ✅ (bu koşum)

**Kusur:** `HATA-S4-007`. Sunucunun `ProblemDetails` başlıkları koda gömülü
Türkçe: **113 farklı `title:` literali, 26 dosya**. Örnek:
`AgentEndpoints.cs:513` `"Agent derlenemedi"` ·
`Security/AgentPrismEndpointFilter.cs:74` `"Uzak erisim kapali"`.

**Bu yeni bir karar değil, K-232'nin doğrudan ihlalidir.** K-232 zaten şunu der:
"`ProblemDetails` metinleri, doğrulama mesajları ve sağlayıcı hataları
**İngilizce kalır**. Paket NuGet.org'a uluslararası yayınlanır ve aynı hata
metni günlükte, testte ve destek kaydında aynı olmalıdır."

**Önce ampirik yeniden üretim.** `grep -rhoP 'title:\s*"[^"]+"' --include='*.cs'
src/AgentPrism.AspNetCore/` **113** eşleşme, **26** dosya döndürdü — kusur
hâlâ tam olarak plan dokümanının öngördüğü boyuttaydı, önceki hiçbir dalga
buna dokunmamıştı.

**Uygulanan kapsam — üç katman:**

1. **Doğrudan `title:`/`detail:` literalleri** — 26 dosyanın tamamı
   (21 `Endpoints/*.cs` + `Voice/VoiceConversationEndpoint.cs` +
   `Security/AgentPrismEndpointFilter.cs` + `Idempotency/IdempotencyFilter.cs` +
   `RateLimiting/AgentPrismRateLimitFilter.cs` + `RateLimiting/QuotaGate.cs` +
   `Internal/RequestBodyBinding.cs` — sonuncusu `title:` grep'ine
   yakalanmamıştı çünkü sabit bir `ProblemTitle` alanı kullanıyordu, ayrı
   taramada bulundu).
2. **Bu literallerin beslendiği alt katman** — `detail: ex.Message` veya
   `detail: detail` (validator çıktısı) yolundan Türkçe metin sızmaya devam
   ederdi; bu yüzden case'in kendi öngördüğü "AgentPrism.Core/
   AgentPrism.Workflows/AgentPrism.Generators'daki birkaç dosya" kapsamı da
   aynı geçişte çevrildi: `AttachmentTypeGuard`, `RunTimeSeriesBucketing`,
   `KnowledgeIngestionService`, `RunReplayService`, `ConversationBranchService`,
   `WebhookUrlValidator`, `QuotaEnforcer`, `WorkflowDefinitionValidator`
   (Core) · `WorkflowRunner`, `WorkflowDefinitionCompiler`,
   `AgentPrismCheckpointStore`, `WorkflowSessionId`, `WorkflowResponseFactory`
   (Workflows) · `ToolDiagnostics` (Generators — `APG0001`-`APG0007`
   analyzer tanı mesajları; testler yalnız tanı KİMLİĞİNİ doğruluyor, metni
   değil, bu yüzden çeviri güvenliydi).
3. **`ProblemDetails` kullanmayan ama aynı kullanıcı kararının (§5.3)
   kapsamındaki yüzeyler** — ilk grep bunları yakalamadı çünkü farklı bir
   hata zarfı kullanıyorlar: OpenAI-uyumlu uçlar
   (`OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`,
   `OpenAIConversationsEndpoints` — `{"error":{"message":...}}` zarfı),
   A2A/MCP dış çağrı hataları (`ExternalAgentProxy`,
   `CatalogToolCallHandler`), `AgentPrism.Voice`'un WebSocket
   `WriteProblemAsync` yazıcısı (`VoiceConversationEndpoint`). Bunlar da
   İngilizce'ye çevrildi — aksi halde "sunucu tek dilli" iddiası bu üç
   yüzeyde yanlış kalırdı.

**Regresyon çiti (en yüksek değerli):**
`tests/AgentPrism.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs`
— `DependencyDirectionTests.cs` ile aynı desende, tüm `src/` ağacını kaynak
metni olarak tarar (derleme çıktısını değil) ve her `title:` literalinin
ASCII-Türkçe kalıp (`bulunamadi`, `gecersiz`, `zorunlu`, ...) taşımadığını
doğrular. Regex yalnız `title:\s*"..."` bağlamını hedefler — kod yorumları
(proje kuralı gereği Türkçe kalır) bu kalıba hiç girmez. Fix geri alınıp
(bir `title:` Türkçe'ye çevrilip) koşulduğunda KIRMIZI verdiği ampirik
olarak doğrulandıktan sonra fix geri uygulandı.

**Ampirik yeniden üretim ve canlı doğrulama.** Case'in kendi kayıtlı
tekrarı — dil Türkçeyken var olmayan bir agent'a gitmek
(`GET /api/agents/does-not-exist-xyz`) — gerçek Postgres'e karşı tekrar
edildi: yanıt artık `{"title":"Agent not found","detail":"There is no
agent named 'does-not-exist-xyz'.",...}`. `Accept-Language: en` başlığı
eklensin ya da eklenmesin yanıt AYNI — sunucu artık gerçekten tek dilli
(İngilizce), önceki koşumun "sunucu Türkçe'ye sabitlenmiş, dile göre
değişmiyor" bulgusunun ayna görüntüsü.

**🚨 Yan etki — 29 mevcut test.** İki test projesi eski Türkçe metni
`ShouldContain`/`ShouldBe` ile doğrudan arıyordu: `Workflows.UnitTests`
(17: `WorkflowDefinitionValidatorTests`, `WorkflowDefinitionCompilerTests`,
`WorkflowRunnerTests`, `WorkflowHumanInTheLoopTests`,
`WorkflowPlanApprovalTests`) ve `AspNetCore.FunctionalTests` (12:
`RateLimitTests`, `EvalEndpointTests`, `QuotaEndpointTests`,
`JsonBindingProblemMiddlewareTests` ×3, `AgentCrudTests`,
`WorkflowEndpointTests`, `GovernanceEndpointTests`, `ExperimentEndpointTests`
×2, `ContentGuardEndpointTests`). Hepsi yeni İngilizce alt dizeye
güncellendi — davranış değişmedi, yalnız beklenen dil.

**Case:** `MT-UI-032` ✅.

**Değişen dosyalar:** 26 `AspNetCore` dosyası (§7 grep listesi) +
`Internal/RequestBodyBinding.cs` + 8 `AgentPrism.Core` dosyası + 5
`AgentPrism.Workflows` dosyası + `AgentPrism.Generators/ToolDiagnostics.cs`
+ 3 `OpenAICompat` dosyası + `A2A/ExternalAgentProxy.cs` +
`McpServer/CatalogToolCallHandler.cs` + `Voice/VoiceConversationEndpoint.cs`.
**Yeni dosya:**
`tests/AgentPrism.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs`.
**Güncellenen testler:** 29 (yukarıda listeli).

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
