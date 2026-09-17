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
