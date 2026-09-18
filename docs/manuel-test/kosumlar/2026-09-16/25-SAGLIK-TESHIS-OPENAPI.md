# 25 — Sağlık Denetimi, Teşhis ve OpenAPI Yayını (`DIAG`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../25-SAGLIK-TESHIS-OPENAPI.md`](../../25-SAGLIK-TESHIS-OPENAPI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (şerit ap-s3, aile 11'in hemen
> ardından) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## Devir notu (oturum 18)

Aile açıldı. Bu aile çoğunlukla İzlek B/C — kendi kendine yeten geçici
`TraconTestHost`/bağımsız uygulama örnekleri kullanıyor, ap-s3'ün ana
uygulaması (port 5083, `mt_s3`) çoğu case için gerekmiyor. İzlek C
case'leri `~/tracon-manuel/test-paketi`'nin `Program.cs`'i her case için
üzerine yazılıp `dotnet run` ile koşuluyor.

---

### MT-DIAG-001

**Gerçek sonuç**
Geçici bellek-içi bir örnek `localhost:5093`'te başlatıldı (üç kalıcılık
env değişkeni de boş bırakıldı — log'da hiçbir Npgsql komutu YOK, doğru
mod). İlk `GET /health`: `Degraded`. `refresh=true` ile `anthropic`
sağlayıcısı ısıtıldı (`200`, `status:"Healthy"`). İkinci `GET /health`:
`Healthy`. İkisi de HTTP `200`. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-002

**Gerçek sonuç**
`HealthCheckService.CheckHealthAsync()` → `report.Entries["tracon"]`:
`Durum: Degraded`, `Aciklama: No model provider has been confirmed healthy
yet.` — `Durum` beklendiği gibi `Degraded`. `Aciklama` metni spec'in
beklediği TÜRKÇE metinle değil, kaynaktaki (`TraconHealthCheck.cs:76`)
GERÇEK İngilizce metinle birebir örtüşüyor. Spec düzeltildi (yukarıda,
K-228 — paket çalışma-anı mesajları İngilizce'dir), ürün kusuru değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-003

**Sapma — container durdurulmadı** (skill §1.3/§1.4 kural 3 — paylaşılan
kaynak). Erişilemezlik, şerit-yerel bir TCP yönlendiriciyle taklit edildi
(`<scratch>/pgproxy.py`, `55483 → 55432`, dosya 03'ün kaydında zaten
belgeli teknik — bu şeritte bir önceki oturumdan ayakta kalan aynı
yönlendirici yeniden kullanıldı). Geçici bir örnek `localhost:5094`'te
`Host=localhost;Port=55483` ile açıldı (`mt_s3` şeması, ana uygulamayla
aynı — salt okunur bağlantı denemesi, migration zaten uygulanmıştı).

**Gerçek sonuç**
Yönlendirici ayaktayken: bir sağlayıcı ısıtıldıktan sonra `/health` →
`200 Healthy`. Yönlendirici öldürüldü (`ap-pg` container'ı boyunca
`Up 32 hours` kaldı, hiç dokunulmadı) → `/health` → **`503 Unhealthy`**;
`diagnostics` aynı anda `canConnect:false`, `migrationsUpToDate:false`
gösterdi. Yönlendirici yeniden başlatıldı, **uygulama YENİDEN
BAŞLATILMADI** → `/health` → `200 Healthy` — toparlanma otomatik. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-004

**Sapma — ana `mt_s3` şeması yerine ayrı bir atılabilir şema kullanıldı**
(`mt_s3_diag004`). Bu case'in kendi 5. adımı zaten "gerçek reset (şemayı
tamamen düşürüp yeniden kurmak)" istiyor — bunu şeridin BİRİKMİŞ ana
şemasında (aile 11/21/29/32/34'ün 133+ run'ı) yapmak paylaşılan bir kaynağı
(bu şeridin kendi kümülatif kanıtı) geri dönüşsüz silerdi. Onun yerine
`localhost:5095`'te atılabilir bir şemayla (`mt_s3_diag004`) yeni bir örnek
açıldı, case sonunda şema tamamen düşürüldü — case'in kendi istediği
"gerçek reset"in daha güvenli bir biçimi.

**Gerçek sonuç**
Taze açılışta `51` migration uygulandı (adım 1 — spec'in beklediği `28`
bayat, K-166'nın bilinen aile-04/18/25 tuzağı; sayı değil YAPI doğrulandı).
En son kayıt (`0051_run_score_evaluator_version`) `__migrations`den
silindi. `/health` → **`503 Unhealthy`**, log mesajı `"1 migration(s) are
pending."` (`TraconHealthCheck.cs`, İngilizce — spec düzeltildi, yukarıda).
`/api/diagnostics` → `migrationsUpToDate:false`,
`pendingMigrations:["0051_run_score_evaluator_version"]` — TEK silinen
migration, gerçek tablo nesneleri (49 tablo) yerinde kaldı (`DROP SCHEMA`
öncesi doğrulandı). Beklenen sonucun tamamı (adım 5'in "reset sonrası temiz
açılış" iddiası dahil — şema tamamen düşürülüp örnek durdurulduğu için bu
DAHA GÜÇLÜ biçimde doğrulandı) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-005

**Yöntem notu.** `test-paketi.csproj`'a `Tracon.Sqlite` paket referansı
eklendi (yalnız `Tracon.Testing` yetmiyordu — `UseSqlite` uzantı metodu
`ITraconBuilder` üzerinde `Tracon.Sqlite`'ta tanımlı). Spec'in kendi
düzeltilmiş kod örneği (`file::memory:?cache=shared`, HATA-S1-003'ün
"doğrulanan çalışan biçimi") aynen kullanıldı — **çökme YOK**, doğrudan
`Degraded`'a ulaşıldı.

**Gerçek sonuç**
`Durum: Degraded`, `Aciklama: More than one persistence provider is
registered (2); 'SQLite' currently wins. Call only one Use*().` — sayaç
`2` doğru (K-183 doğru sayıyor), mesaj kaynakla birebir (İngilizce, spec
düzeltildi yukarıda). Beklenen sonucun tamamı örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-006

**Yöntem notu.** Bu şerit `05-SAGLAYICI-OPENAI.md`'yi koşmadığı için
`manuel-bozuk-model` fixture'ı yoktu. Spec'in kendi düzeltmesindeki gibi
atılabilir bir örnekte (`localhost:5096`, şema `mt_s3_diag006`,
`Tracon__CircuitBreaker__FailureThreshold=2`,
`Tracon__CircuitBreaker__BreakDuration=00:00:20` env değişkenleriyle —
`dotnet user-secrets set` DEĞİL, skill §1.2) geçici bir `manuel-diag-bozuk`
agent'ı (`openrouter`, kasıtlı geçersiz model `openrouter/bu-model-yok-9999`)
`POST /api/agents` ile kaydedildi, iki kez çağrıldı.

**Gerçek sonuç**
İki çağrı da `ProviderInvocationException` ile başarısız oldu (gerçek
OpenRouter isteği — küçük ölçekli gerçek para harcaması, spec'in izin
verdiği tek case). Ardından `GET /health` → **`Degraded`** (`Unhealthy`
DEĞİL). `GET /api/models/health`'te `openrouter` satırı:
`status:"Unhealthy"`, `detail:"Circuit breaker is open. Will retry in
14s."` — devrenin gerçekten açıldığını doğrudan kanıtlıyor. Beklenen
sonucun tamamı birebir örtüştü. Örnek ve şema case sonunda temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-007

**Yöntem notu.** Spec'in kod örneğindeki `GetServices<IHealthCheck>()
.OfType<TraconHealthCheck>().Single()` deseni MT-DIAG-002'deki AYNI
sebeple çalışmaz (`TraconHealthCheck` `internal sealed`, DI'ye
`IHealthCheck` olarak kaydolmaz) — `HealthCheckService.CheckHealthAsync()`
üç kez çağrıldı.

**Gerçek sonuç**
`Requests.Count: 0` — üç sağlık denetimi de modele hiç istek göndermedi.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-008

**Gerçek sonuç**
`AddTracon()` hiç çağrılmadan, yalnız `AddTraconHealthChecks()` ile kurulan
minimal `WebApplication`da `HealthCheckService.CheckHealthAsync()` ilk
çağrıda `InvalidOperationException` fırlattı: `"Unable to resolve service
for type 'Tracon.TraconDiagnosticsCollector' while attempting to activate
'Tracon.TraconHealth..."` — kayıt anında değil, İLK yoklamada patlıyor.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-020

**Gerçek sonuç**
`ConfigureEndpoints` hiç verilmeden başlatılan `TraconTestHost`ta
`GET /tracon/api/diagnostics` → `404`. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-021

**Gerçek sonuç**
Ana örnekte (port 5083, rol kurulumu yok) `GET /api/diagnostics` → `200`.
Gövde on alanı da taşıyor: `persistenceProvider`,
`registeredPersistenceProviders`, `canConnect`, `migrationsUpToDate`,
`pendingMigrations`, `modelProviders[].{name,status,circuitOpen}`,
`configuration[].{key,resolved,hint}`, `uiEmbedded`, `toolCount`,
`agentCount` — hepsi dolu (4 model sağlayıcı, 3 config anahtarı,
`toolCount:7`, `agentCount:13`). Ek olarak spec'te anılmayan bir
`agentSources` alanı da var (fazladan bilgi, eksiklik değil). Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-022

**Yöntem notu.** `13-KIRACI-VE-GUVENLIK.md`'nin K-431 notuna göre §8 artık
elle kod yazmıyor — `Tracon__Demo__Roles__Enabled=true` ortam değişkeniyle
gösterim rol şeması açılıyor, rol `X-Tracon-Demo-Role: reader|operator|admin`
başlığıyla geliyor. Atılabilir bir örnek (`localhost:5097`, `mt_s3` şeması
paylaşılan ama yalnız okundu) bu bayrakla açıldı.

**Gerçek sonuç**
`X-Tracon-Demo-Role: reader` ile `GET /api/diagnostics` → `403 Forbidden`.
`X-Tracon-Demo-Role: admin` ile aynı uç → `200 OK`. Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-023

**Gerçek sonuç**
Gerçek OpenAI anahtarı `user-secrets`ten okundu (transkripte hiç
yazdırılmadan — değişken bir `grep`e verildi, sonucu `unset` edildi) ve
`/api/diagnostics`ın TAM gövdesinde arandı: **`temiz`** — hiçbir eşleşme
yok. `configuration[]`'da yalnız `resolved: true` bayrağı var (MT-DIAG-021
kaydında zaten görülmüştü). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-024

**Gerçek sonuç**
`ConfigurationDiagnostic { Key, Resolved, Hint }` üç alanı da beklendiği
gibi yazdı. `.Value` üyesine erişim denendiğinde derleme **GERÇEKTEN**
`CS1061` ile reddedildi (`'ConfigurationDiagnostic' does not contain a
definition for 'Value'`) — tipin `secret` taşıyamayacağı yapısal olarak
kanıtlandı. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-025

**Gerçek sonuç**
`GET /api/diagnostics`'in `configuration[]` dizisinde
`Tracon:Providers:OpenAI:ApiKey` anahtarı **tam 1** kez geçiyor
(`resolved:true`) — `openai` ve `openai-responses` iki ayrı model sağlayıcı
kaydı aynı anahtarı paylaştığı hâlde tek satıra düşürülmüş. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-026

**Gerçek sonuç**
`modelProviders[]`'da `openrouter` bir kayıt olarak VAR (`name:
"openrouter"`, `status`, `circuitOpen` alanlarıyla). `configuration[]`'da
`key` alanı "openrouter" içeren **hiçbir** satır YOK (boş dizi) — anahtarı
gerçekten tanımlı olmasına rağmen. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-027

**Yöntem notu.** Spec'in kendi kod örneği (`Data Source=:memory:`) bilinen
`HATA-S1-003`'ü tetikler (`04-KALICılık-DIGER.md`/`MT-SQL-005`,
`MT-DIAG-005`'te bu şeritte de zaten görülmüştü) — case'in KENDİ konusu
K-183/K-247 sayacı olduğu için `Data Source=file::memory:?cache=shared`
(bilinen çalışan biçim) kullanıldı; kusur burada yeniden tetiklenmedi,
zaten kayıtlı.

**Gerçek sonuç**
Bellek içi: `persistenceProvider: InMemory, registered: 0`. SQLite (tek
`UseSqlite`): `persistenceProvider: SQLite, registered: 1`. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-028

**Gerçek sonuç**
Ana örnekte (port 5083) `GET /api/agents` uzunluğu **13**,
`/api/diagnostics`'in `agentCount`'u da **13** — birebir eşleşiyor.
`toolCount: 7` (>0). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-029

**Gerçek sonuç**
`TraconDiagnosticsCollector.CollectAsync()` doğrudan (HTTP'siz) çağrıldığında
`UiEmbedded: False` — konsol projesi `Tracon.UI` referans vermiyor. Karşıt
kanıt zaten MT-DIAG-021'in kaydında var: aynı ana örnekte HTTP ucu
(`/api/diagnostics`, `Tracon.UI` kayıtlı) `uiEmbedded: true` döndürmüştü —
fark `DiagnosticsEndpoints.cs:42`'deki `with` ifadesinden geliyor, tam
spec'in iddia ettiği gibi. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-030

**Gerçek sonuç**
`CollectAsync()` üç kez art arda çağrıldı, `provider.Requests.Count: 0` —
hiçbir model çağrısı üretilmedi. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 18 devam) — §3 OpenAPI başladı

§1 (001-008) ve §2 (020-030) kapandı, sıfır kusur. §3'e (OpenAPI, 040-065)
geçiliyor — ana örneğin (port 5083) gerçek `/openapi/v1.json`'ı kullanılıyor.

---

### MT-DIAG-040

**Gerçek sonuç**
`src/Tracon.AspNetCore/Tracon.AspNetCore.csproj`'da
`Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` araması: **temiz**.
Paketlenmiş `.nuspec`de (`~/tracon-local-feed/Tracon.AspNetCore.0.0.0-preview.0.789.nupkg`)
aynı arama: **temiz**. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-041

**Gerçek sonuç**
`GET /openapi/v1.json` → `200` (ana örnek, port 5083 — hem `Tracon.SqlServer`
hem `Tracon.Sqlite` `ProjectReference` taşıyor, `TraconRemoveDuplicateSqlXmlDocs`
hedefi çakışmayı gerçekten ayıklamış). Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-042

**Gerçek sonuç**
İki etiketten az taşıyan uç sorgusu **boş** döndü. İlk etiketlerin
benzersiz kümesi **tam olarak** `["Tracon"]`. Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-043

**Gerçek sonuç**
Tekrar eden `operationId` sorgusu **boş** döndü. Toplam/benzersiz sayısı
ikisi de **169** — birebir eşit. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-044

**Gerçek sonuç**
`POST /tracon/api/agents/{name}/run`'ın `200` yanıtının `content`'i
**yalnız** `text/event-stream` (`schema.type:"string"`) — `application/json`
YOK. `400`/`404`/`429` (ve ek olarak `403`/`422`/`501`/`503`) hepsi
`application/problem+json` + `ProblemDetails` şemasıyla belgelenmiş. (Ayrıca
belgede spec'te anılmayan bir `202 Accepted` → `application/json`
`AcceptedRunResponse` yanıtı var — bu FARKLI bir durum kodu, `200`'ün
"yalnız SSE" iddiasıyla çelişmiyor.) Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-045

**Gerçek sonuç**
`POST /tracon/v1/chat/completions`'ın `200` yanıtının içerik tipi
anahtarları: `["application/json", "text/event-stream"]` — İKİSİ BİRDEN,
K-274'ün tek `.Produces` çağrısı çözümü hâlâ kalıcı. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-046

**Yöntem notu.** Adım 2 (izlek C, `EnableDiagnosticsEndpoint` ayarlanmadan
`404` teyidi) MT-DIAG-020'nin **birebir aynısı** — o case bu turda zaten
koşuldu, tekrar koşulmadı.

**Gerçek sonuç**
Ana örneğin belgesinde (`EnableDiagnosticsEndpoint=true`) benzersiz etiket
kümesinde `Diagnostics` **VAR**. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-047

**Gerçek sonuç**
`diff <(jq -S . /tmp/apidoc.json) <(jq -S . docs/openapi/tracon.json)` →
**FARKLI** (205 satır). Spec'in kendi öngördüğü iki sonuçtan biri — kusur
DEĞİL, doğal bakım boşluğu. Fark iki türden: (1) gürültü — bu şeridin
kendi portu (`http://localhost:5083/`) vs şablonun kanonik
`http://localhost:5081`; (2) gerçek — işlenmiş dosyada `Images` etiketi ve
görsel-üretim şemaları (`ImageGenerationOperatorRequest/Response`,
`IMcpToolRefresher`) hiç YOK, koddaki (çalışan host'un ürettiği) belgede
VAR. `docs/openapi/tracon.json` yenilenmemiş — yenileme komutu
(`TRACON_OPENAPI_REFRESH=1 dotnet test ... OpenApiSnapshotTests`) spec'te
yazılı, koşum modunda çalıştırılmadı (kural 1 — `docs/openapi/tracon.json`
`src/`/`samples/`/`tests/` dışında olsa da bu bir bakım adımıdır, kapanışa
bırakıldı). Beklenen sonucun "FARKLI çıkabilir, bu kusur değildir" dalı
gerçekleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-048

**Gerçek sonuç**
`npx openapi-typescript docs/openapi/tracon.json` → temiz üretim (194ms,
hata yok). Üretilen `.ts` dosyası `tsc --strict --noEmit` ile **çıkış kodu
0** verdi — hiç tip hatası yok. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-049

**Yöntem notu — spec bayat çıktı.** `GET /tracon/api/diagnostics`
(port 5083) `extensionPoints`'te **7** eleman döndürdü, spec'in beklediği
5 değil. Kaynağı (`TraconExtensionPoints.cs`) okundu: dosyanın kendi XML
dokümanı zaten "the **seven** embedding points" diyor — spec Faz 85'ten
kalma, sonraki bir faz `IRunAuthorizationHandler` ve
`IToolApprovalPresenter`'ı eklemiş. Spec yukarıda düzeltildi.

**Gerçek sonuç**
7 elemanlı dizi: `IToolAuthorizationHandler` (`AllowAllToolAuthorizationHandler`,
`true`), `IRunAuthorizationHandler` (`AllowAllRunAuthorizationHandler`,
`true`), `IRunEventSink` (`(none)`, `true`), `IAttachmentStorage`
(`(database)`, `true`), `IRunAttributionContext`
(`DemoRunAttributionContext`, `false`), `IToolApprovalPresenter`
(`OrderApprovalPresenter`, `false` — `Program.cs:146`'daki
`AddSingleton` kaydı), `ITenantContext` (`HttpTenantContext`, **`false`**
— Core'un çıplak varsayılanı `SingleTenantContext`, `Tracon.AspNetCore`
kendi HTTP-farkında varsayılanını bağlıyor, bu tüketici özelleştirmesi
değil). Düzeltilmiş beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-050

**Yöntem notu.** Spec'in `localhost:5082`'si bu turun şerit port
tahsisiyle çakışıyor (`ap-s2`'nin ana portu) — `samples/Tracon.Embedded`
bunun yerine boş bir portta (`5098`) açıldı. Bu örnek uygulamada bearer
token yapılandırılı DEĞİL (auth'suz `200` döndü) — bu, örneğin kendi
bilinen minimal kurulumudur, ürün kusuru değil.

**Gerçek sonuç**
7 elemanlı dizi (MT-DIAG-049'daki AYNI 5→7 bayatlığı — spec düzeltildi
yukarıda). Altısı `isBuiltInDefault:false`: `ITenantContext→
EmbeddedTenantContext`, `IRunAttributionContext→
EmbeddedRunAttributionContext`, `IToolAuthorizationHandler→
EmbeddedToolAuthorizationHandler`, `IRunAuthorizationHandler→
EmbeddedRunAuthorizationHandler` (yeni nokta, bu örnek de özelleştirmiş),
`IRunEventSink→BoundedChannelRunEventSink`, `IAttachmentStorage→
InMemoryBufferAttachmentStorage`. Tek istisna: `IToolApprovalPresenter→
NullToolApprovalPresenter`, `isBuiltInDefault:true` — Embedded örneği bunu
özelleştirmiyor. Düzeltilmiş beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-051

**Gerçek sonuç**
`extensionPoints[].{contract,implementation}` metninin tamamında
`sk-|apikey|connectionstring|password|://` deseni arandı: **`TEMIZ`** —
hiçbir eşleşme yok (tip adlarının tamamı düz sınıf adı, host/bağlantı
bilgisi taşımıyor). Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-052

**Gerçek sonuç**
`ASPNETCORE_ENVIRONMENT=Production`, üç kalıcılık env değişkeni de boş,
`localhost:5099`'da açıldı. Log'da `"storage that is not persistent"`
**tam 1** kez geçti — `warn: Tracon.NonPersistentStorageWarningService[0]`
seviyesinde. Mesaj üçünü de adlandırıyor: `"agent definitions, runs,
sessions"`, süreç ömrü sınırı ("a restart loses it"), ve kalıcılığa geçiş
çağrısı örneği (`UsePostgreSql(connectionString)`). `/api/meta` →
`"persistent":false` — log ile aynı yargı. Uygulama normal ayağa kalktı.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-053

**Gerçek sonuç**
AYNI kurulum (üç env değişkeni boş), yalnız `ASPNETCORE_ENVIRONMENT=Development`.
`"Application started"` **1** kez, `"storage that is not persistent"`
**0** kez — tamamen sessiz. Kontrol gerçekten `IHostEnvironment.IsProduction()`
üzerinden çalışıyor. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-054

**Gerçek sonuç**
`Production` + `Tracon__Sqlite__ConnectionString="Data Source=/tmp/faz104.db"`
ile açıldı. Uyarı **0** kez düştü. `/api/meta` → `"persistent":true`,
`"runStore":"SqlRunStore"`. Kalıcı bir kurulum yanlış pozitif üretmiyor.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-055

**Sapma — `samples/Tracon.Api/Program.cs` değiştirilmedi** (kural 1,
mutlak). Case'in kendi adımı `.AddPatternContentGuard(...)`'ı geçici olarak
yorum satırına almayı istiyor — bu, koşum sırasında `samples/` altında
dosya değişikliği demektir ve skill'in "Kod DONUK" kuralı istisnasızdır.
Onun yerine `~/tracon-manuel/test-paketi`de **hiçbir content guard
kaydetmeyen**, minimal bir `AddTracon()` host'u (`WebApplication.CreateBuilder()`
+ `FakeModelProvider`) kuruldu; aynı `SilentGapWarningService`'i sınıyor,
yalnız `samples/`'a dokunmadan.

**Yöntem tuzağı (bu koşumda bulundu, ürün kusuru DEĞİL).**
`builder.Environment.EnvironmentName = Environments.Development` gibi
`WebApplicationBuilder.Build()`'DAN ÖNCE elle mutasyon, DI konteynerine
kaydolan `IHostEnvironment`i ETKİLEMİYOR — çıplak ASP.NET Core'da (Tracon
hiç yokken) bile ölçüldü: `app.Environment.EnvironmentName` "Development"
gösterirken `app.Services.GetRequiredService<IHostEnvironment>()`
"Production" döndü, **iki ayrı nesne** (`ReferenceEquals: false`). Doğru
yöntem spec'in zaten kullandığı gerçek süreç ortam değişkenidir
(`ASPNETCORE_ENVIRONMENT=...  dotnet run`) — ona geçilince beklenen
davranış tam olarak gözlemlendi.

**Gerçek sonuç (055, Production)**
`ASPNETCORE_ENVIRONMENT=Production` ile DI'nin `IHostEnvironment.EnvironmentName`si
doğrulandı: `Production`. `"no IContentGuard registered"` **tam 1** kez
düştü — `[Warning] Tracon.SilentGapWarningService: ... Register one with
AddPatternContentGuard() or AddContentGuard<T>() ...`. Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-056

**Yöntem notu.** MT-DIAG-055'in AYNI atılabilir `AddTracon()` host'u
(yukarıdaki sapma ve yöntem-tuzağı notu geçerli) — yalnız
`ASPNETCORE_ENVIRONMENT=Development`.

**Gerçek sonuç**
DI'nin `IHostEnvironment.EnvironmentName`'i doğrulandı: `Development`.
`"no IContentGuard registered"` **0** kez — tamamen sessiz. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-057

**Yöntem notu.** Bu case kod değişikliği istemiyor (spec'in kendi notu) —
DEĞİŞTİRİLMEMİŞ `samples/Tracon.Api` doğrudan `Production`da açıldı.

**Gerçek sonuç**
`"data retention disabled"` **tam 1** kez düştü — `warn:
Tracon.SilentGapWarningService[0]: ... Tracon:Retention:Enabled is false
... register a policy through IRetentionPolicyStore ...`. Kontrol grubu:
aynı log'da `"no IContentGuard registered"` **0** kez — örnek uygulama
varsayılan olarak bir guard kaydettiği için MT-DIAG-055'in uyarısı burada
HİÇ görünmüyor (iki uyarı birbirinden bağımsız tetikleniyor, spec'in
öngördüğü gibi). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-058

**Gerçek sonuç**
Değiştirilmemiş `samples/Tracon.Api` (hiçbir `RequireCustomBinding`
çağrısı yok) normal açıldı: `"Application started"` **1** kez,
`"required custom binding"` **0** kez. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-060

**Gerçek sonuç**
Değiştirilmemiş `samples/Tracon.Embedded` (dört sözleşmeyi de `AddTracon()`'den
ÖNCE kaydediyor) normal açıldı: `"Application started"` **1** kez,
`"required custom binding"` **0** kez — dördü de kabul edildi. Beklenen
sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-063

**Gerçek sonuç**
Aynı değiştirilmemiş gömme örneğinde `/api/diagnostics`'in
`extensionPoints[]`'inde `IAttachmentStorage` → `implementation:
"InMemoryBufferAttachmentStorage"` — kendi adaptör tipiyle görünüyor,
yokluk dalı tetiklenmedi. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-065

**Gerçek sonuç**
`extensionPoints | length` → **7** (MT-DIAG-050'nin ölçtüğü güncel sayı —
bu case Faz 150'den, zaten doğru sayıyı bekliyordu). İlk girdinin alan
kümesi tam olarak `contract,implementation,isBuiltInDefault` — zorunluluğu
bildiren yeni bir alan yok. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-059

**Sapma — `samples/Tracon.Api`/`samples/Tracon.Embedded` değiştirilmedi**
(kural 1, mutlak — bu not 061/062/064'te de geçerlidir, tekrar
yazılmayacak). Case'in kendi adımı geçici `Program.cs` düzenlemesi
istiyor (`RequireCustomBinding` ekleme, bir kaydı sonraya taşıma, bir
satırı yorumlama, `MapTracon`'u yorumlama — dördü de aynı sorunu paylaşır).
Onun yerine `~/tracon-manuel/test-paketi`de her case için minimal,
bağımsız bir `AddTracon()` host'u kuruldu — aynı `RequiredBindingValidator`'ı
gerçek DI kompozisyonuyla sınıyor, yalnız `samples/`'a dokunmadan.

**Gerçek sonuç — zorunlu sözleşme yerleşik varsayılanla çözülüyor**
`RequireCustomBinding<IRunAuthorizationHandler>()`, özel kayıt YOK.
`builder.Build()` + `StartAsync()` gerçek bir `InvalidOperationException`
ile durdu: *"IRunAuthorizationHandler was declared as a required custom
binding, but Tracon's built-in default AllowAllRunAuthorizationHandler is
what resolved. Register your own IRunAuthorizationHandler on
IServiceCollection BEFORE the AddTracon() call. ..."* — üçü de (sözleşme,
çözülen tip, düzeltme) mesajda. `"Application started"` hiç yazılmadı.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-061

**Gerçek sonuç — geç `TryAdd` düşüyor, geç `AddSingleton` düşmüyor**
MT-DIAG-059'un AYNI kurulumu + `builder.Services.TryAddSingleton<
IRunAuthorizationHandler, StubHandler>()` **`AddTracon()`'den SONRA** →
AYNI istisna (yerleşik varsayılan zaten slotu tutuyor, geç `TryAdd`
sessizce düşüyor). Kontrast: `TryAddSingleton` yerine düz `AddSingleton`
ile AYNI geç kayıt → host **AÇILDI** (`"Application started"` gerçekten
yazıldı) — kap gerçekten SON kaydı çözüyor. Spec'in "olguyu bildirir,
sırayı değil" iddiası ampirik olarak doğrulandı. Beklenen sonuç birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-062

**Gerçek sonuç — gerçek yokluk farklı mesaj üretiyor**
`RequireCustomBinding<IAttachmentStorage>()`, hiçbir kayıt yok (yerleşik
bir varsayılan tipi de yok — bu sözleşme için Tracon hiçbir şey
kaydetmiyor). İstisna: *"IAttachmentStorage was declared as a required
custom binding, but **nothing is registered for it**. ..."* — bir tip adı
DEĞİL, `MT-DIAG-059`'un "AllowAll..." biçiminden yapısal olarak farklı.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-DIAG-064

**Gerçek sonuç — kontrol HTTP'siz de çalışıyor**
`RequireCustomBinding<IToolAuthorizationHandler>()`, özel kayıt yok,
`app.MapTracon(...)` **HİÇ ÇAĞRILMADI** (hiçbir HTTP ucu haritalanmadı).
Yine de `StartAsync()` AYNI türde bir `InvalidOperationException` ile
durdu (`AllowAllToolAuthorizationHandler` adlandırıldı) — kontrol gerçekten
`MapTracon`'a bağlı değil, kompozisyon anında (`IHostedService.StartAsync`)
çalışıyor. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Aile 25 (`DIAG`) TAMAMLANDI (oturum 18 sonu)

45/45 case işlendi, **0 Kaldı**. Dört case (055, 056, 059, 061, 062, 064 —
altısı, `RequireCustomBinding`/guard-yokluğu serisi) spec'in kendi adımı
`samples/` altında geçici kod değişikliği istediği için **atılabilir bir
`AddTracon()` host'uyla** (`~/tracon-manuel/test-paketi`) koşuldu — kural 1
hiç ihlal edilmedi, ama koşum yöntemi spec'in "Girilecek veri"sinden
sapıyor (her birinde not edilmiştir). Birkaç case'in `Beklenen sonuç`ü
Türkçe/bayat metin taşıyordu (K-228 dil sınırı deseni × 4: 002, 004, 005,
049/050'nin 5→7 genişleme-noktası bayatlığı) — hepsi yerinde düzeltildi,
gerekçesiyle. `MT-DIAG-047` işlenmiş `docs/openapi/tracon.json`
anlık görüntüsünün canlı host'tan sapmış olduğunu buldu (yeni `Images`
şemaları) — beklenen bir bakım boşluğu, kapanışta yenilenecek.

Kod tamamen donuk bırakıldı (tüm geçici örnekler durduruldu, atılabilir
şemalar düşürüldü). Sıradaki aile: `17-EVAL-VE-DENEYLER.md`.

---
