# 25 — Sağlık Denetimi, Teşhis ve OpenAPI Yayını (`DIAG`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../25-SAGLIK-TESHIS-OPENAPI.md`](../../25-SAGLIK-TESHIS-OPENAPI.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-DIAG-001 — Bellek içi kurulumda, bir sağlayıcı ısıtıldıktan sonra `Healthy` döner

**Gerçek sonuç**
İlk çağrı `Degraded`. `refresh=true` sonrası dört sağlayıcı (`anthropic`,
`google`, `openai`, `openai-responses`, `openrouter` — 5 kayıt) hepsi
`Healthy` döndü. İkinci `/health` çağrısı `Healthy`. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-002 — Hiçbir model sağlayıcısı doğrulanmadan `Degraded` döner (henüz `Unhealthy` değil)

**Gerçek sonuç**
Yukarıdaki düzeltilmiş kodla çalıştırıldı. Çıktı birebir beklenen:
```
Durum: Degraded
Aciklama: Henuz saglikli oldugu dogrulanmis bir model saglayicisi yok.
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-003 — 🚨 Veritabanına erişilemediğinde `/health` gerçekten `Unhealthy`/`503` döner

**Gerçek sonuç**
Container durmadan önce `Healthy` (200). Durdurulunca `503 Service Unavailable`,
gövde `Unhealthy`. Container yeniden başlayıp 3 sn sonra tekrar `Healthy` (200)
— uygulama yeniden başlatılmadı, toparlanma otomatik. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-004 — 🚨 Bekleyen migration varken `/health` `Unhealthy` döner, `/api/diagnostics` adını listeler

**Gerçek sonuç**
Adım 1: `28`. Migration silindikten sonra `/health` → `503 Unhealthy`.
`/api/diagnostics` → `migrationsUpToDate: false`,
`pendingMigrations: ["0028_experiment_canary"]` — yalnız silinen tek migration.
Reset (şema düşür + yeniden başlat) sonrası tekrar `28` migration temiz
uygulandı, `/health` → `Degraded` (henüz sağlayıcı ısıtılmamış, normal).
Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-005 — İki SQL sağlayıcısı birlikte kayıtlıyken `Degraded` döner (K-183)

**Gerçek sonuç**
Senaryonun kendi kodu (orijinal `Data Source=:memory:`, iki kayıt) çalıştırıldı:
`SqliteException: no such table: agentprism_tenants` ile `AgentPrismTestHost`
hiç açılmadı — HATA-S1-003'ün tekrarı (bkz. yukarıdaki not). K-183 sayacı bu
yüzden hiç ölçülemedi; case bu hâliyle **Kaldı**.

Ek doğrulama (case'in asıl konusu olan K-183 davranışını ayrıca sınamak için,
`:memory:` sorununu atlatan `cache=shared` bağlantısıyla): host açıldı,
`Durum: Degraded`, `Aciklama: Birden fazla kalicilik saglayicisi kayitli (2);
su an 'SQLite' kazaniyor. Yalniz bir Use*() cagirin.` — K-183'ün kendisi
sağlam çalışıyor, yalnız `:memory:` bağlantı dizesi bozuk.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-003 düzeltmesiyle yeniden koşuldu: validator artik ciplak :memory:'yi acikca reddediyor (AgentPrismTestHost dahil). Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-006 — Bir model sağlayıcısının devresi açıkken `/health` `Degraded` döner

**Gerçek sonuç**
İki deneme de gerçek `400`/`ClientResultException` ile başarısız oldu
(`openrouter/bu-model-yok-9999 is not a valid model ID`). Sonrasında `/health`
→ `Degraded` — beklenenle birebir (Unhealthy değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-007 — `/health` hiçbir model çağrısı üretmez

**Gerçek sonuç**
`Requests.Count: 0` — beklendiği gibi. (Aynı `HealthCheckService` düzeltmesi
MT-DIAG-002'de kayıtlı; bu case orijinal `IHealthCheck` API'sini kullanmadığı
için değişiklik gerekmedi.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-008 — `AddAgentPrism()` çağrılmadan yalnız `AddAgentPrismHealthChecks()` çağrılırsa ilk istekte DI hatası verir

**Gerçek sonuç**
```
Beklenen: DI cozumu basarisiz -- Unable to resolve service for type 'AgentPrism.AgentPrismDiagnosticsCollector' while attempting to activate 'AgentPrism.
```
Beklenen davranışla birebir eşleşiyor — istisna kayıt anında değil yalnız ilk
yoklamada fırlatılıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — `GET /api/diagnostics` (Faz 33, F-62)

---

## MT-DIAG-020 — Varsayılan kapalı: hiç açılmadan `/api/diagnostics` `404` döner

**Gerçek sonuç**
`Durum: 404` — beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-021 — Açıkken `200` döner ve rapor şemasını taşır

**Gerçek sonuç**
`200 OK`. Gövdede tam 10 alanın tümü var: `persistenceProvider: "PostgreSQL"`,
`registeredPersistenceProviders: 1`, `canConnect: true`, `migrationsUpToDate: true`,
`pendingMigrations: []`, `modelProviders` (5 kayıt: openai, openai-responses,
openrouter, anthropic, google — hepsi `status: Unknown`, `circuitOpen: false`,
henüz ısıtılmadı), `configuration` (3 kayıt: OpenAI/Anthropic/Google anahtarları,
hepsi `resolved: true`, `hint: null`), `uiEmbedded: true`, `toolCount: 6`,
`agentCount: 12`. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-022 — Admin olmayan rolle `403` döner

**Gerçek sonuç**
Koşulmadı. `13-KIRACI-VE-GUVENLIK.md` §8'in geçici kurulumu
`samples/AgentPrism.Api/RoleTestAuthHandler.cs` adında **yeni bir dosya**
eklemeyi ve `Program.cs`'i değiştirmeyi gerektiriyor —
`KOSUM-PLANI.md` §2.1'in pazarlığa açık olmayan "kod değiştirilmez" kuralına
girer. Aynı gerekçeyle S1-3 oturumu da (`SONUCLAR-S1-2026-08-13.md`, "Sapmalar")
bu kurulumu **tetiklemedi**. MT-DIAG-021'in kendi ön koşulu zaten "rol kurulumu
yapılmamış" diyor; bu case rolün gerçek denetimini istiyor, kurulum olmadan
anlamlı koşulamaz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de geçici RoleTestAuthHandler kurulumuyla koşuldu, sonra kod eksiksiz geri alındı: reader→403, admin→200. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-023 — 🚨 Bilinen bir API anahtarı yanıtın hiçbir yerinde geçmez

**Gerçek sonuç**
Çıktı `temiz` — gerçek OpenAI anahtarı yanıtın hiçbir yerinde geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-024 — Config anahtarları yalnız `resolved` bilgisini taşır, DEĞER hiç yoktur (izole doğrulama)

**Gerçek sonuç**
Kod derlendi ve çalıştı; çıktı beklenenle birebir:
```
Key: AgentPrism:Providers:OpenAI:ApiKey
Resolved: False
Hint: dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "<ANAHTARINIZ>"
```
Kaynak (`ConfigurationDiagnostic.cs:4-14`) doğrudan okunarak da doğrulandı —
tip yalnız `Key`/`Resolved`/`Hint` taşıyor, değeri taşıyan hiçbir alan yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-025 — Aynı sağlayıcının iki örneği aynı config anahtarını TEK satır raporlar

**Gerçek sonuç**
`1` — beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-026 — `UseOpenAICompatible` hiçbir `ConfigurationDiagnostic` bildirmez

**Gerçek sonuç**
`openrouterModelProvider`: bir öğe (`name: openrouter, status: Unknown,
circuitOpen: false`). `openrouterConfig`: boş dizi. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-027 — `RegisteredPersistenceProviders` bellek içi `0`, tek sağlayıcıda `1` sayar

**Gerçek sonuç**
Bellek içi blok beklendiği gibi: `Bellek ici -- persistenceProvider: InMemory,
registered: 0`. SQLite bloğu (`Data Source=:memory:`) `SqliteException: no
such table: agentprism_tenants` ile çöktü — HATA-S1-003. Ek doğrulama
(`Data Source=file::memory:?cache=shared` ile): `SQLite --
persistenceProvider: SQLite, registered: 1` — beklenen sayaç davranışı bu
bağlantı dizesiyle doğru, yalnız dokümanın kendi `:memory:` biçimi bozuk.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-003 düzeltmesiyle yeniden koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-028 — `toolCount`/`agentCount` gerçek kayıtlı sayıyı yansıtır

**Gerçek sonuç**
`agents ucu: 12`, `{toolCount: 6, agentCount: 12}` — birebir eşleşiyor,
`toolCount > 0`. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-029 — `uiEmbedded` arayüz paketine göre doğru değer taşır

**Gerçek sonuç**
`Dogrudan CollectAsync -- UiEmbedded: False`. Karşılaştırma: MT-DIAG-021'de
örnek uygulamanın `/api/diagnostics`ı `uiEmbedded: true` döndürmüştü. Fark
beklenen kaynaktan geliyor. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-030 — Teşhis ucu hiçbir model çağrısı veya migration uygulaması üretmez

**Gerçek sonuç**
`Requests.Count: 0` — beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — OpenAPI belgesinin yayımlanması (Faz 40)

---

## MT-DIAG-040 — `AgentPrism.AspNetCore.csproj` `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` taşımaz

**Gerçek sonuç**
İki tarama da `temiz`. `.csproj`'da ve paketlenmiş `.nuspec`'te
`Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` hiç geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-041 — 🚨 Örnek uygulamada `GET /openapi/v1.json` `200` döner (K-352/F-76 düzeltmesi)

**Gerçek sonuç**
`200` döndü (`http://localhost:5081/openapi/v1.json`, şerit portu). 116 yol
üretildi. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-042 — Belgedeki her ucun en az iki etiketi var, ilki `AgentPrism`

**Gerçek sonuç**
🐛 **HATA-S1-012.** İlk sorgu **boş DEĞİL** — tam olarak bir uç iki etiketten
az taşıyor: `GET /agentprism/api/voice/sessions/{sessionId}/stream` (WebSocket
akış ucu, `operationId: AgentPrismVoiceStream`), yalnız `["AgentPrism"]`
etiketiyle. Kök neden: `VoiceConversationEndpoint.Map()`
(`src/AgentPrism.AspNetCore/Voice/VoiceConversationEndpoint.cs:38-52`) hiç
`.WithTags(...)` çağırmıyor — yalnız `voiceGroup`'un
(`AgentPrismEndpointRouteBuilderExtensions.cs:252`) grup düzeyindeki tek
`"AgentPrism"` etiketini devralıyor. Karşılaştırma: aynı dosyadaki diğer ses
uçları (`VoiceEndpoints.cs:48,55,61,70`) hepsi `.WithTags("AgentPrism",
"Voice")` çağırıyor — yalnız bu WebSocket ucu ikinci etiketi (`Voice`)
eksik bırakıyor. İkinci sorgu beklenen: `["AgentPrism"]`.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de düzeltmesiyle yeniden koşuldu: tags artik ["AgentPrism","Voice"]. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-043 — `operationId` değerleri benzersizdir

**Gerçek sonuç**
🐛 **HATA-S1-013.** İlk sorgu boş DEĞİL — `null` yazdırıyor. İkinci sorgu
`147` / `146` — **eşit değil**. Kök neden: iki A2A ucunun `operationId`si hiç
yok: `POST /agentprism/a2a/ozetleyici` ve
`GET /agentprism/a2a/ozetleyici/.well-known/agent-card.json`. Bu ikisi jq'nin
`group_by` mantığında "tekrar eden" (iki `null`) sayılıyor, ilk sorgu bu yüzden
`null` basıyor. `AgentPrismA2AExtensions.cs:122-126`:
`agentGroup.MapA2A(handler, "/")` ve `agentGroup.MapWellKnownAgentCard(card,
"")` — ikisi de üçüncü taraf A2A SDK uzantıları, `.WithName(...)` hiç
çağrılmıyor (grup yalnız `.WithTags("AgentPrism", "A2A")` alıyor,
`AgentPrismA2AExtensions.cs:103`). Diğer tüm gruplar (`AgentPrismEndpointRouteBuilderExtensions.cs`)
her tekil uçta ayrıca `.WithName(...)` çağırıyor; A2A grubu bunu atlıyor.
Etki sınırlı: yalnız 2/147 işlem etkileniyor, ikisi de dinamik A2A yüzeyinde
(kod-üretici istemciler bu iki uç için kararsız/otomatik ad üretir).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de düzeltmesiyle yeniden koşuldu: 147 islemin 147'si de benzersiz operationId taşıyor (0 null). Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-044 — `POST /api/agents/{name}/run` yalnız `text/event-stream` bildirir, tipli JSON DEĞİL

**Gerçek sonuç**
`200`'ün `content` alanı yalnız `text/event-stream` içeriyor (`application/json`
yok). `400`, `404`, `429` üçü de `application/problem+json` ile
`ProblemDetails` şeması bildiriyor. (Ayrıca `202 Accepted` →
`application/json`/`AcceptedRunResponse` de var — `Prefer: respond-async`
yolu, beklenen listede yok ama çelişmiyor.) Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-045 — `/v1/chat/completions` hem `application/json` hem `text/event-stream` içerik tipini BİRLİKTE bildirir

**Gerçek sonuç**
`["application/json", "text/event-stream"]` — ikisi de var. Beklenenle
birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-046 — `Diagnostics` etiketi varsayılan üretilen belgede YOKTUR (uç varsayılan kapalı)

**Gerçek sonuç**
Sorgu `Diagnostics` yazdı — örnek uygulamanın belgesinde etiket var (uç
`Program.cs`'de bilinçli açık). Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-047 — `docs/openapi/agentprism.json` çalışan host'un ürettiğiyle AYNIDIR (anlık görüntü)

**Gerçek sonuç**
`FARKLI -- yenileme gerekir`. Fark iki nedenden geliyor: (1) `info.title`
farklı — canlı belge `AgentPrism.Api | v1` (örnek uygulama), işlenmiş dosya
`AgentPrism.AspNetCore.FunctionalTests | v1` — yani `docs/openapi/agentprism.json`
`OpenApiSnapshotTests`in **FunctionalTests host**'undan üretilmiş, örnek
uygulamadan değil; ikisi yapılandırma olarak eşit değil (örnek uygulama
`Diagnostics`/`A2A`/`Voice` uçlarını açık tutuyor, FunctionalTests host'u
muhtemelen tutmuyor). (2) Bu yüzden `/agentprism/a2a/ozetleyici`,
`/agentprism/a2a/ozetleyici/.well-known/agent-card.json`,
`/agentprism/api/diagnostics`, `/agentprism/api/voice/sessions/{sessionId}/stream`
işlenmiş dosyada hiç yok; `AgentPrismDiagnosticsReport`/`ConfigurationDiagnostic`/
`ProviderDiagnostic` şemaları da yok. Diğer taraftan işlenmiş dosyada bir
`IMcpToolRefresher` şeması ve `requestBody` var ki canlı belgede yok — sürüm
farkı (FunctionalTests projesi bu şerit'in `dotnet pack` tazelemesinden önce
üretilmiş olabilir). Doküman kendi "Beklenen sonuç"unda bu ihtimali zaten
öngörüyor ("bu bir kusur değil, doğal bakım adımıdır") — case bu hâliyle
Geçti sayılır, yenileme faz kapanışının işidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-048 — Belge bağımsız bir istemci üretecinden hatasız geçer

**Gerçek sonuç**
`openapi-typescript` hatasız bitti (179.5ms), `.ts` dosyası üretildi.
`tsc --strict --noEmit` çıkış kodu `0`. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
