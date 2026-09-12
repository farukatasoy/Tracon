# 25 — Sağlık Denetimi, Teşhis ve OpenAPI Yayını (`DIAG`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../25-SAGLIK-TESHIS-OPENAPI.md`](../../25-SAGLIK-TESHIS-OPENAPI.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/25-SAGLIK-TESHIS-OPENAPI.md
> ```

---

## Temiz geçen case'ler (17)

| Case | Durum | Başlık |
|---|---|---|
| MT-DIAG-001 | ☑ | Bellek içi kurulumda, bir sağlayıcı ısıtıldıktan sonra `Healthy` döner |
| MT-DIAG-006 | ☑ | Bir model sağlayıcısının devresi açıkken `/health` `Degraded` döner |
| MT-DIAG-008 | ☑ | `AddTracon()` çağrılmadan yalnız `AddTraconHealthChecks()` çağrılırsa ilk istekte DI hatası verir |
| MT-DIAG-020 | ☑ | Varsayılan kapalı: hiç açılmadan `/api/diagnostics` `404` döner |
| MT-DIAG-021 | ☑ | Açıkken `200` döner ve rapor şemasını taşır |
| MT-DIAG-022 | ☑ | Admin olmayan rolle `403` döner |
| MT-DIAG-024 | ☑ | Config anahtarları yalnız `resolved` bilgisini taşır, DEĞER hiç yoktur (izole doğrulama) |
| MT-DIAG-025 | ☑ | Aynı sağlayıcının iki örneği aynı config anahtarını TEK satır raporlar |
| MT-DIAG-026 | ☑ | `UseOpenAICompatible` hiçbir `ConfigurationDiagnostic` bildirmez |
| MT-DIAG-028 | ☑ | `toolCount`/`agentCount` gerçek kayıtlı sayıyı yansıtır |
| MT-DIAG-029 | ☑ | `uiEmbedded` arayüz paketine göre doğru değer taşır |
| MT-DIAG-030 | ☑ | Teşhis ucu hiçbir model çağrısı veya migration uygulaması üretmez |
| MT-DIAG-040 | ☑ | `Tracon.AspNetCore.csproj` `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` taşımaz |
| MT-DIAG-044 | ☑ | `POST /api/agents/{name}/run` yalnız `text/event-stream` bildirir, tipli JSON DEĞİL |
| MT-DIAG-045 | ☑ | `/v1/chat/completions` hem `application/json` hem `text/event-stream` içerik tipini BİRLİKTE bildirir |
| MT-DIAG-046 | ☑ | `Diagnostics` etiketi varsayılan üretilen belgede YOKTUR (uç varsayılan kapalı) |
| MT-DIAG-048 | ☑ | Belge bağımsız bir istemci üretecinden hatasız geçer |

## Ayrıntı taşıyan case'ler (11)

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
`SqliteException: no such table: tracon_tenants` ile `TraconTestHost`
hiç açılmadı — HATA-S1-003'ün tekrarı (bkz. yukarıdaki not). K-183 sayacı bu
yüzden hiç ölçülemedi; case bu hâliyle **Kaldı**.

Ek doğrulama (case'in asıl konusu olan K-183 davranışını ayrıca sınamak için,
`:memory:` sorununu atlatan `cache=shared` bağlantısıyla): host açıldı,
`Durum: Degraded`, `Aciklama: Birden fazla kalicilik saglayicisi kayitli (2);
su an 'SQLite' kazaniyor. Yalniz bir Use*() cagirin.` — K-183'ün kendisi
sağlam çalışıyor, yalnız `:memory:` bağlantı dizesi bozuk.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-003 düzeltmesiyle yeniden koşuldu: validator artik ciplak :memory:'yi acikca reddediyor (TraconTestHost dahil). Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-007 — `/health` hiçbir model çağrısı üretmez

**Gerçek sonuç**
`Requests.Count: 0` — beklendiği gibi. (Aynı `HealthCheckService` düzeltmesi
MT-DIAG-002'de kayıtlı; bu case orijinal `IHealthCheck` API'sini kullanmadığı
için değişiklik gerekmedi.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-023 — 🚨 Bilinen bir API anahtarı yanıtın hiçbir yerinde geçmez

**Gerçek sonuç**
Çıktı `temiz` — gerçek OpenAI anahtarı yanıtın hiçbir yerinde geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-027 — `RegisteredPersistenceProviders` bellek içi `0`, tek sağlayıcıda `1` sayar

**Gerçek sonuç**
Bellek içi blok beklendiği gibi: `Bellek ici -- persistenceProvider: InMemory,
registered: 0`. SQLite bloğu (`Data Source=:memory:`) `SqliteException: no
such table: tracon_tenants` ile çöktü — HATA-S1-003. Ek doğrulama
(`Data Source=file::memory:?cache=shared` ile): `SQLite --
persistenceProvider: SQLite, registered: 1` — beklenen sayaç davranışı bu
bağlantı dizesiyle doğru, yalnız dokümanın kendi `:memory:` biçimi bozuk.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-003 düzeltmesiyle yeniden koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-041 — 🚨 Örnek uygulamada `GET /openapi/v1.json` `200` döner (K-352/F-76 düzeltmesi)

**Gerçek sonuç**
`200` döndü (`http://localhost:5081/openapi/v1.json`, şerit portu). 116 yol
üretildi. Beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DIAG-042 — Belgedeki her ucun en az iki etiketi var, ilki `Tracon`

**Gerçek sonuç**
🐛 **HATA-S1-012.** İlk sorgu **boş DEĞİL** — tam olarak bir uç iki etiketten
az taşıyor: `GET /tracon/api/voice/sessions/{sessionId}/stream` (WebSocket
akış ucu, `operationId: TraconVoiceStream`), yalnız `["Tracon"]`
etiketiyle. Kök neden: `VoiceConversationEndpoint.Map()`
(`src/Tracon.AspNetCore/Voice/VoiceConversationEndpoint.cs:38-52`) hiç
`.WithTags(...)` çağırmıyor — yalnız `voiceGroup`'un
(`TraconEndpointRouteBuilderExtensions.cs:252`) grup düzeyindeki tek
`"Tracon"` etiketini devralıyor. Karşılaştırma: aynı dosyadaki diğer ses
uçları (`VoiceEndpoints.cs:48,55,61,70`) hepsi `.WithTags("Tracon",
"Voice")` çağırıyor — yalnız bu WebSocket ucu ikinci etiketi (`Voice`)
eksik bırakıyor. İkinci sorgu beklenen: `["Tracon"]`.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de düzeltmesiyle yeniden koşuldu: tags artik ["Tracon","Voice"]. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-043 — `operationId` değerleri benzersizdir

**Gerçek sonuç**
🐛 **HATA-S1-013.** İlk sorgu boş DEĞİL — `null` yazdırıyor. İkinci sorgu
`147` / `146` — **eşit değil**. Kök neden: iki A2A ucunun `operationId`si hiç
yok: `POST /tracon/a2a/ozetleyici` ve
`GET /tracon/a2a/ozetleyici/.well-known/agent-card.json`. Bu ikisi jq'nin
`group_by` mantığında "tekrar eden" (iki `null`) sayılıyor, ilk sorgu bu yüzden
`null` basıyor. `TraconA2AExtensions.cs:122-126`:
`agentGroup.MapA2A(handler, "/")` ve `agentGroup.MapWellKnownAgentCard(card,
"")` — ikisi de üçüncü taraf A2A SDK uzantıları, `.WithName(...)` hiç
çağrılmıyor (grup yalnız `.WithTags("Tracon", "A2A")` alıyor,
`TraconA2AExtensions.cs:103`). Diğer tüm gruplar (`TraconEndpointRouteBuilderExtensions.cs`)
her tekil uçta ayrıca `.WithName(...)` çağırıyor; A2A grubu bunu atlıyor.
Etki sınırlı: yalnız 2/147 işlem etkileniyor, ikisi de dinamik A2A yüzeyinde
(kod-üretici istemciler bu iki uç için kararsız/otomatik ad üretir).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de düzeltmesiyle yeniden koşuldu: 147 islemin 147'si de benzersiz operationId taşıyor (0 null). Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-DIAG-047 — `docs/openapi/tracon.json` çalışan host'un ürettiğiyle AYNIDIR (anlık görüntü)

**Gerçek sonuç**
`FARKLI -- yenileme gerekir`. Fark iki nedenden geliyor: (1) `info.title`
farklı — canlı belge `Tracon.Api | v1` (örnek uygulama), işlenmiş dosya
`Tracon.AspNetCore.FunctionalTests | v1` — yani `docs/openapi/tracon.json`
`OpenApiSnapshotTests`in **FunctionalTests host**'undan üretilmiş, örnek
uygulamadan değil; ikisi yapılandırma olarak eşit değil (örnek uygulama
`Diagnostics`/`A2A`/`Voice` uçlarını açık tutuyor, FunctionalTests host'u
muhtemelen tutmuyor). (2) Bu yüzden `/tracon/a2a/ozetleyici`,
`/tracon/a2a/ozetleyici/.well-known/agent-card.json`,
`/tracon/api/diagnostics`, `/tracon/api/voice/sessions/{sessionId}/stream`
işlenmiş dosyada hiç yok; `TraconDiagnosticsReport`/`ConfigurationDiagnostic`/
`ProviderDiagnostic` şemaları da yok. Diğer taraftan işlenmiş dosyada bir
`IMcpToolRefresher` şeması ve `requestBody` var ki canlı belgede yok — sürüm
farkı (FunctionalTests projesi bu şerit'in `dotnet pack` tazelemesinden önce
üretilmiş olabilir). Doküman kendi "Beklenen sonuç"unda bu ihtimali zaten
öngörüyor ("bu bir kusur değil, doğal bakım adımıdır") — case bu hâliyle
Geçti sayılır, yenileme faz kapanışının işidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
