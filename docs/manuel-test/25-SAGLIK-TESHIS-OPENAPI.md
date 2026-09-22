# 25 — Sağlık Denetimi, Teşhis ve OpenAPI Yayını (`DIAG`)

> **Alan kodu:** `DIAG` · **Faz:** 33 (sağlık denetimi + `/api/diagnostics`), 40 (OpenAPI yayını), 85 (`extensionPoints`), 150 (zorunlu binding profili)
> **Kaynak:** `src/Tracon.AspNetCore/Health/` (tümü) ·
> `src/Tracon.AspNetCore/Endpoints/DiagnosticsEndpoints.cs` ·
> `src/Tracon.Abstractions/Diagnostics/` (tümü) ·
> `src/Tracon.Core/Diagnostics/TraconDiagnosticsCollector.cs` ·
> `src/Tracon.Core/Diagnostics/TraconExtensionPoints.cs` ·
> `src/Tracon.Core/Diagnostics/RequiredBindingValidator.cs` (Faz 150) ·
> `src/Tracon.Sql.Shared/Migrations/MigrationRunner.cs` (yalnız `GetSnapshotAsync`,
> `ISqlPersistenceDiagnostics` uygulaması) · uç üstverisi için `src/Tracon.AspNetCore/Endpoints/*.cs`
> ve `src/Tracon.AspNetCore/OpenAICompat/*.cs`'in `.WithTags`/`.Produces` çağrıları ·
> `docs/openapi/tracon.json` (yayımlanan artefakt) ·
> çapraz doğrulama için `tests/Tracon.AspNetCore.FunctionalTests/OpenApi*.cs`,
> `samples/Tracon.Api/Tracon.Api.csproj` (F-76/K-352 düzeltmesi).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.
> Rol tabanlı yetkilendirme kurulumu (Admin case'leri için) [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md) §8'dedir.
> Devre kesici kurulumu [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) §8'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/25-SAGLIK-TESHIS-OPENAPI.md`](../arsiv/manuel-test-kosum-2026-09/25-SAGLIK-TESHIS-OPENAPI.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/25-SAGLIK-TESHIS-OPENAPI.md

---

## Bu dosya neyi kanıtlar

Üç ayrı yüzey, tek bir ortak toplayıcıyı (`TraconDiagnosticsCollector`)
paylaşır: standart `/health` (makine okur — Kubernetes, App Service),
`/api/diagnostics` (insan okur — ayrıntılı öz denetim) ve OpenAPI belgesi
(araç okur — istemci üreteci, dağıtım hattı). Üçünün de ortak ilkesi **yan
etkisizliktir**: hiçbiri model çağırmaz, hiçbiri migration uygulamaz, hiçbiri
`secret` sızdırmaz.

```mermaid
flowchart TD
    A["TraconDiagnosticsCollector<br/>yan etkisiz"] --> B["kalicilik: SELECT 1 -- yalniz baglanti sinamasi"]
    A --> C["migration: __migrations tablosu OKUNUR<br/>ASLA uygulanmaz"]
    A --> D["model saglayicilari:<br/>ModelProviderHealthCache'ten OKUNUR"]
    A --> E["config anahtarlari:<br/>cozuldu/cozulmedi -- DEGER YOK (K-059)"]

    A --> F["TraconHealthCheck<br/>Healthy / Degraded / Unhealthy"]
    A --> G["GET /api/diagnostics<br/>Admin -- varsayilan KAPALI"]

    F --> H["GET /health<br/>Unhealthy -> 503, digerleri -> 200"]
    G --> I["JSON rapor"]

    J["121+ HTTP ucu<br/>.WithTags / .Produces"] --> K["docs/openapi/tracon.json<br/>test korumali anlik goruntu"]

    classDef red fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    class G red
```

**Neden aynı dosyada.** Faz 33'ün kendi gerekçesi (39.'nin değil — bu fazın
adı 33): iki kalem (sağlık + teşhis) aynı toplayıcıyı okur; ayrı dosyalarda
sınanırsa aynı kurulum iki kez tekrarlanır. OpenAPI (Faz 40) farklı bir
mekanizma olsa da HTTP yüzeyinin **kendi kendini tanımlama** yeteneğinin
üçüncü ayağıdır — üçü de "kurulumum/yüzeyim doğru mu?" sorusuna cevap verir.

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| `ModelProviderHealthCache`/`ModelProviderCircuitBreaker`in KENDİ mekaniği (nasıl dolduğu, `refresh=true`, devre açma/kapama eşiği) | `05-SAGLAYICI-OPENAI.md` §8 — bu dosya yalnız bu önbellekten **okunan** teşhis/sağlık yansımasını sınar |
| Rol tabanlı yetkilendirmenin GENEL sözleşmesi (`TraconPolicies`, `RequireRole`) | `13-KIRACI-VE-GUVENLIK.md` §8 — bu dosya yalnız `/api/diagnostics`'in Admin kapısını sınar, genel mekanizmayı yeniden kurmaz |
| `POST /api/agents/{name}/run` uçlarının GENEL SSE/tool/hata davranışı | `07-HTTP-YONETIM-API.md`, `11-ARAYUZ-RUN-SESSION-SSE.md` — bu dosya yalnız bu ucun OpenAPI **üstverisini** (yanıt şeması, içerik tipi) sınar |
| Migration'ların GENEL uygulanma/idempotency davranışı | `03-KALICILIK-POSTGRESQL.md`, `04-KALICILIK-DIGER.md` — bu dosya yalnız teşhisin migration durumunu **doğru okuyup okumadığını** sınar, migration'ı bizzat uygulamaz |
| Diğer 15 OpenAPI etiketinin (`Runs`, `Sessions`, `Evals`, ...) kendi HTTP sözleşmesi | İlgili alan dosyaları — bu dosya yalnız etiketleme/şema **mekanizmasının** kendisini (tag kapsaması, şema kapsaması, `operationId` benzersizliği) sınar, tek tek uçların iş mantığını değil |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı **§1'in migration/DB
   case'leri için** uygulanır (MT-DIAG-003, 004, 005). Diğer case'ler için
   gerekmez.
2. **İzlek C case'leri** (§1'in çoğu, §2'nin çoğu) `Tracon.Testing`
   kullanan paylaşılan konsol projesini kullanır — `24-TEST-PAKETI-VE-SABLON.md`nin
   "Koşmadan önce" adım 4'ünde kurulan `~/tracon-manuel/test-paketi`
   **aynen** kullanılır (henüz kurulmadıysa oradaki adımları uygula).
3. **İzlek B case'leri** (§3'ün tamamı, §1/§2'nin bazı case'leri) örnek
   uygulamayı çalıştırır:
   ```bash
   cd samples/Tracon.Api && dotnet run     # http://localhost:5080/tracon
   export APB="Authorization: Bearer manuel-test-token-2026"
   export APU="http://localhost:5080/tracon"
   ```
4. **Admin rolü gerektiren case'ler** (MT-DIAG-022) [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md)
   §8'deki **geçici** `RoleTestAuthHandler` kurulumunu ister — o bölümün
   1-5 numaralı adımları önce uygulanır. MT-DIAG-021 bu kurulum **olmadan**
   da koşulabilir (varsayılan durumda hiçbir rol policy'si kayıtlı değildir —
   `13-KIRACI-VE-GUVENLIK.md`'nin MT-SEC-080 bulgusu).
5. 🚨 **`/api/diagnostics` örnek uygulamada zaten AÇIKTIR.**
   `samples/Tracon.Api/Program.cs:711`'de `options.EnableDiagnosticsEndpoint = true`
   kodda yazılıdır — bu, "varsayılan kapalı" davranışını (MT-DIAG-020) örnek
   uygulama üzerinde **göstermez**. O case bilinçli olarak izlek C kullanır
   (bir `TraconTestHost` hiçbir `ConfigureEndpoints` almadan).

> **Gerçek para uyarısı.** MT-DIAG-006 (devre kesici) hariç, bu dosyanın
> hiçbir case'i gerçek bir model sağlayıcısı çağırmaz — toplayıcı zaten
> yan etkisizdir. MT-DIAG-006 `05-SAGLAYICI-OPENAI.md`'nin kendi devre kesici
> kurulumunu (küçük ölçekte gerçek çağrı) yeniden kullanır.

---

# 1 — Sağlık denetimi: `GET /health` (Faz 33, F-38)

### MT-DIAG-001 — Bellek içi kurulumda, bir sağlayıcı ısıtıldıktan sonra `Healthy` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | 33.3 |

Faz 33'ün kendi kapanış ölçümünün (`docs/arsiv/fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md`,
"Doğrulama komutları") birebir tekrarı.

**Ön koşul**
- Örnek uygulama çalışıyor, bellek içi kalıcılık (hiçbir `Use*Sql()` çağrılmadı).

**Adımlar**
1. Uygulama yeni açılmışken (hiçbir model sağlığı yoklanmamış) `/health`ı çağır.
2. Bir sağlayıcıyı `refresh=true` ile ısıt.
3. `/health`ı tekrar çağır.

**Girilecek veri**
```bash
curl -s -i http://localhost:5080/health | tail -1

curl -s "$APU/api/models/health?refresh=true" -H "$APB" > /dev/null

curl -s -i http://localhost:5080/health | tail -1
```

**Beklenen sonuç**
- İlk çağrı gövdesi `Degraded` yazar (aşağıdaki MT-DIAG-002'nin gerekçesiyle
  aynı — henüz doğrulanmış bir sağlayıcı yok).
- İkinci çağrı gövdesi `Healthy` yazar; HTTP durumu ikisinde de `200`dür
  (yalnız `Unhealthy` `503` üretir — ASP.NET Core'un varsayılan `HealthStatus`→HTTP
  eşlemesi).

---

### MT-DIAG-002 — Hiçbir model sağlayıcısı doğrulanmadan `Degraded` döner (henüz `Unhealthy` değil)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | 33.3 |

Sınır senaryosu. `TraconHealthCheck`in son kuralı: hiçbir sağlayıcı
`Healthy` olarak doğrulanmadıysa `Degraded` (`Unhealthy` **değil**) —
kod: `TraconHealthCheck.cs:70-77`.

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `TraconTestHost`ta bir `FakeModelProvider` kaydet, hiç çağırma.
2. `IHealthCheck`ı doğrudan `Services`ten çöz, çalıştır.

**Girilecek veri**
```csharp
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await TraconTestHost.StartAsync(options =>
{
    options.ModelProvider = provider;
    options.ConfigureServices = services => services
        .AddHealthChecks()
        .AddTraconHealthChecks();
});

var healthCheckService = host.Services.GetRequiredService<HealthCheckService>();
var report = await healthCheckService.CheckHealthAsync();
var entry = report.Entries["tracon"];

Console.WriteLine("Durum: " + entry.Status);
Console.WriteLine("Aciklama: " + entry.Description);
```

> 🚨 **Düzeltildi (koşum, 2026-08-13, doküman kusuru):** orijinal kod
> `host.Services.GetServices<IHealthCheck>().OfType<TraconHealthCheck>()`
> kullanıyordu. İki ayrı hata: (1) `TraconHealthCheck` `internal sealed`
> (`TraconHealthCheck.cs:25`) — dış projeden derlenmez (`CS0122`); (2)
> `AddHealthChecks()` denetimleri DI konteynerine `IHealthCheck` olarak
> **kaydetmez** — `IHealthChecksBuilder.AddCheck<T>` bir `HealthCheckRegistration`
> ekler, `GetServices<IHealthCheck>()` her zaman **boş** döner. Doğru yol
> `HealthCheckService.CheckHealthAsync()`'i çağırıp `report.Entries["tracon"]`
> okumaktır — bu, `/health` ucunun kullandığı gerçek yoldur. Ürün kusuru
> değildir; senaryonun kendi kod örneği hiç çalıştırılmadan yazılmıştı.

**Beklenen sonuç**
- `Durum: Degraded`.
- `Aciklama: No model provider has been confirmed healthy yet.`
  (`TraconHealthCheck.cs:76` mesajıyla birebir — 🚨 **doküman düzeltildi
  (koşum, 2026-09-17, ap-s3):** spec Türkçe bir mesaj bekliyordu, ama
  paket çalışma-anı mesajları İngilizce'dir (K-228, dil sınırı kuralı);
  ürün kusuru değil).

---

### MT-DIAG-003 — 🚨 Veritabanına erişilemediğinde `/health` gerçekten `Unhealthy`/`503` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

Bu, Faz 33'ün kendi kapanış notunda **"yarım kalan/ölçülmedi"** olarak
işaretlediği tek DoD kalemidir: *"gerçek bir Postgres container'ı durdurup
`/health`'in `503`'e döndüğü manuel olarak doğrulanmadı."* Bu case o boşluğu kapatır.

**Ön koşul**
- Örnek uygulama PostgreSQL ile çalışıyor (`Tracon:PostgreSql:ConnectionString`
  tanımlı, `ap-pg` container'ı ayakta), en az bir sağlayıcı ısıtılmış (`Healthy`).

**Adımlar**
1. `/health`'in `Healthy` döndüğünü doğrula.
2. PostgreSQL container'ını durdur.
3. `/health`ı tekrar çağır.
4. Container'ı yeniden başlat, `/health`'in toparlandığını doğrula.

**Girilecek veri**
```bash
curl -s -i http://localhost:5080/health | tail -1

docker stop ap-pg

curl -s -i http://localhost:5080/health

docker start ap-pg
sleep 3
curl -s -i http://localhost:5080/health | tail -1
```

**Beklenen sonuç**
- Container durmadan önce: `200`, gövde `Healthy`.
- Container durduktan sonra: **`503 Service Unavailable`**, gövde `Unhealthy`
  (`MigrationRunner.GetSnapshotAsync`'in `DbException` yakalayıp
  `CanConnect: false` döndürmesi → `TraconHealthCheck.cs:41-44`).
- Container yeniden başladıktan sonra: `200`, gövde tekrar `Healthy` —
  toparlanma otomatiktir, uygulamanın yeniden başlatılması **gerekmez**.

---

### MT-DIAG-004 — 🚨 Bekleyen migration varken `/health` `Unhealthy` döner, `/api/diagnostics` adını listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

Bu da Faz 33'ün gerçek bir veritabanına karşı **manuel** doğrulanmamış ikinci
DoD kalemidir — yalnız fonksiyonel test (sahte `ISqlPersistenceDiagnostics`)
ve gerçek DB'ye karşı `GetSnapshotAsync` **ayrı ayrı** kanıtlanmıştı, ikisinin
birleşimi (gerçek DB'de gerçekten eksik bir migration satırı) hiç denenmemişti.

**Ön koşul**
- [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulandı — PostgreSQL şeması
  temiz, uygulama açılışta tüm migration'ları uyguladı (28 migration).

**Adımlar**
1. Uygulanmış migration sayısını doğrula.
2. **En son** uygulanan migration kaydını `__migrations` defterinden sil (gerçek
   şema nesneleri — tablolar — yerinde kalır, yalnız defter kaydı silinir).
3. `/health`ı çağır.
4. `/api/diagnostics`ı çağır, `pendingMigrations` alanını oku.
5. Reset yordamını tekrar uygula (bu case veritabanını tutarsız bırakır —
   defterde eksik ama şemada var olan bir migration; sonraki case'ler için
   temizlenmelidir).

**Girilecek veri**
```bash
docker exec -i ap-pg psql -U postgres -d tracon -c \
  "SELECT count(*) FROM tracon.__migrations;"

docker exec -i ap-pg psql -U postgres -d tracon -c \
  "DELETE FROM tracon.__migrations WHERE id = (SELECT MAX(id) FROM tracon.__migrations);"

curl -s -i http://localhost:5080/health

curl -s "$APU/api/diagnostics" -H "$APB" | jq '{migrationsUpToDate, pendingMigrations}'
```

**Beklenen sonuç**
- Adım 1'de `28` (veya güncel migration sayısı) döner.
- `/health` **`503 Unhealthy`** döner (`TraconHealthCheck.cs:46-51`,
  `"{N} migration(s) are pending."` mesajıyla — 🚨 **doküman düzeltildi
  (koşum, 2026-09-17, ap-s3):** spec Türkçe mesaj bekliyordu, kaynak
  İngilizce'dir (K-228), ürün kusuru değil).
- `/api/diagnostics` `migrationsUpToDate: false` ve `pendingMigrations`
  alanında **silinen tek** migration'ın adını taşır — geri kalan 27 (veya N-1)
  migration hâlâ uygulanmış sayılır, yalnız silinen satır eksiktir.
- Reset sonrası (adım 5) sistem tekrar temiz açılır — silinen defter kaydı
  şema nesnelerini bozmadığı için `ApplyAsync` yeniden çalıştırılırsa aynı
  migration'ı **tekrar uygulamaya çalışır**; bu genelde `CREATE TABLE IF NOT
  EXISTS` gibi idempotent ifadeler yüzünden sorunsuz geçer ama **garanti
  değildir** — bu yüzden adım 5 gerçek reset (şemayı tamamen düşürüp yeniden
  kurmak) kullanır, yalnız `ApplyAsync`e güvenmez.

---

### MT-DIAG-005 — İki SQL sağlayıcısı birlikte kayıtlıyken `Degraded` döner (K-183)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-183, K-247 |

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. Aynı `TraconTestHost`ta hem `UseSqlite` hem (ikinci kez) `UseSqlite`
   çağırarak (aynı sağlayıcı türünü iki kez kaydetmek, K-183'ü tetiklemek için
   yeterlidir — kayıt `AddSingleton`dır, `TryAdd` değil) iki işaret ekle.
2. `IHealthCheck`ı çalıştır.

**Girilecek veri**
```csharp
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await TraconTestHost.StartAsync(options =>
{
    options.ModelProvider = provider;
    options.ConfigureServices = services => services
        .AddHealthChecks()
        .AddTraconHealthChecks();
    options.ConfigureTracon = builder => builder
        .UseSqlite("Data Source=file::memory:?cache=shared")
        .UseSqlite("Data Source=file::memory:?cache=shared");   // ikinci kayit -- K-183
});

var healthCheckService = host.Services.GetRequiredService<HealthCheckService>();
var report = await healthCheckService.CheckHealthAsync();
var entry = report.Entries["tracon"];

Console.WriteLine("Durum: " + entry.Status);
Console.WriteLine("Aciklama: " + entry.Description);
```

> 🚨 **Doküman düzeltmesi (`HealthCheckService` kısmı) + 🐛 ÜRÜN KUSURU
> (`:memory:` kısmı — HATA-S1-003'ün tekrarı):**
> (1) `GetServices<IHealthCheck>().OfType<TraconHealthCheck>()` deseni
> derlenmez/boş döner (doküman kusuru, MT-DIAG-002'deki gibi) —
> `HealthCheckService.CheckHealthAsync()` kullanıldı.
> (2) Orijinal `Data Source=:memory:` ile — **tek** `UseSqlite` çağrısıyla bile
> (K-183'ün ikinci kaydından bağımsız, aşağıda MT-DIAG-027'de izole doğrulandı)
> — uygulama `SqliteException: no such table: tracon_tenants` ile hiç
> açılmıyor. Bu, `04-KALICILIK-DIGER.md`'nin koşumunda zaten bulunan ve
> `SONUCLAR-S1-2026-08-13.md`'de kayıtlı **HATA-S1-003**'ün (`Data
> Source=:memory:` dokümante edildiği hâlde çalışmıyor, `Tracon.Sqlite`
> XML dokümanı hâlâ "desteklenir" diyor) `TraconTestHost` üzerinden
> **ikinci bir kod yolunda** tekrarıdır — kök neden aynı
> (`SqliteDataSource.CreateDbConnection` her çağrıda ayrı bağlantı açıyor,
> çıplak `:memory:` bağlantıya özel). Bu case'in kendi asıl konusu (K-183
> sayacı) `:memory:` bloğunu atlatmak için `Data
> Source=file::memory:?cache=shared` (HATA-S1-003'ün "doğrulanan çalışan
> biçim"i) ile ayrıca koşuldu — sonucu aşağıda.

**Beklenen sonuç**
- `Durum: Degraded`.
- `Aciklama:` `"More than one persistence provider is registered (2);
  'SQLite' currently wins. Call only one Use*()."` biçiminde bir mesaj
  (`TraconHealthCheck.cs:55-58` — 🚨 **doküman düzeltildi (koşum,
  2026-09-17, ap-s3):** spec Türkçe mesaj bekliyordu, kaynak İngilizce'dir
  (K-228), ürün kusuru değil).
- **Not:** `UseSqlite("Data Source=:memory:")` ile bellek içi SQLite'ın
  gerçekte açılıp açılmadığı bu case'in konusu değildir; yalnız K-183
  sayacının davranışı ölçülür. Sayı tutmazsa (`2` yerine başka bir değer)
  bu şüpheli bir bulgudur ve not düşülür.

---

### MT-DIAG-006 — Bir model sağlayıcısının devresi açıkken `/health` `Degraded` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

`05-SAGLAYICI-OPENAI.md` §8'in ("Devre kesici") kurulumunu yeniden kullanır —
devre kesici mekaniğinin kendisi orada zaten sınanmıştır; burada yalnız
`/health`in bunu **yansıttığı** doğrulanır.

**Ön koşul**
- `05-SAGLAYICI-OPENAI.md` §8'deki gibi:
  ```bash
  dotnet user-secrets set "Tracon:CircuitBreaker:FailureThreshold" "2"
  dotnet user-secrets set "Tracon:CircuitBreaker:BreakDuration" "00:00:20"
  ```
  uygulanmış, uygulama yeniden başlatılmış.

**Adımlar**
1. Geçersiz bir model adıyla iki kez arka arkaya çalıştırarak devreyi aç
   (`05-SAGLAYICI-OPENAI.md` §8'deki desenle aynı).
2. `/health`ı çağır.
3. `dotnet user-secrets remove` ile devre kesici ayarlarını geri al, yeniden başlat.

**Girilecek veri**
```bash
for i in 1 2; do
  curl -s -X POST "$APU/api/agents/openrouter-support/run" -H "$APB" \
       -H "content-type: application/json" \
       -d '{"message":"merhaba","sessionId":"diag-circuit-'"$i"'"}' \
       2>&1 | grep -E "^event:|^data:"
done

curl -s -i http://localhost:5080/health | tail -1
```

**Beklenen sonuç**
- İki başarısız çağrıdan sonra devre açılır (05'in kendi doğrulaması).
- `/health` gövdesi **`Degraded`**dir — `openrouter-support` sağlayıcısının
  devresi açık olsa bile diğer sağlayıcılar (varsa) sağlıklıysa uygulama
  `Unhealthy` **olmaz** (`TraconHealthCheck.cs:63-68`).

> 🚨 **Düzeltildi (koşum, 2026-08-13, doküman kusuru):** "Girilecek veri"
> `openrouter-support` fixture'ını **geçerli** modeliyle çağırıyor
> (`openai/gpt-5.4-mini` üzerinden OpenRouter, `Program.cs:457`) — gerçek bir
> anahtarla bu her zaman **başarılı** olur, devreyi asla açmaz. "Adımlar"
> bölümünün kendisi ise "geçersiz bir model adı" gerektiğini söylüyor
> (MT-OAI-080'in deseni) — iki bölüm birbiriyle çelişiyordu. Düzeltme:
> `openrouter` sağlayıcılı, bilerek geçersiz modelli (`openrouter/bu-model-yok-9999`)
> geçici bir `manuel-diag-bozuk` agent'ı `POST /api/agents` ile kaydedildi
> (05'in henüz koşulmamış `manuel-bozuk-model` fixture'ının yerine — bu şerit
> 05'i koşmadı), o agent iki kez çağrıldı.

---

### MT-DIAG-007 — `/health` hiçbir model çağrısı üretmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

Negatif/kontrol senaryosu — sağlık denetimi maliyet üretmemelidir.

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `FakeModelProvider`ı kaydet, `IHealthCheck`ı üç kez art arda çalıştır.
2. `provider.Requests.Count`ı oku.

**Girilecek veri**
```csharp
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await TraconTestHost.StartAsync(options =>
{
    options.ModelProvider = provider;
    options.ConfigureServices = services => services.AddHealthChecks().AddTraconHealthChecks();
});

var check = host.Services.GetServices<IHealthCheck>().OfType<TraconHealthCheck>().Single();

for (var i = 0; i < 3; i++)
{
    await check.CheckHealthAsync(new HealthCheckContext());
}

Console.WriteLine("Requests.Count: " + provider.Requests.Count);
```

**Beklenen sonuç**
- `Requests.Count: 0` — üç sağlık denetimi çağrısı da modele **hiç** istek göndermez.

---

### MT-DIAG-008 — `AddTracon()` çağrılmadan yalnız `AddTraconHealthChecks()` çağrılırsa ilk istekte DI hatası verir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-251 |

Sınır senaryosu — `TraconHealthCheckExtensions`in kendi XML dokümanının
(`TraconHealthCheckExtensions.cs:34-38`) doğrulaması: kayıt anında hiçbir
kontrol yapılmaz, hata yalnız **ilk yoklamada** DI çözümü başarısız olduğunda çıkar.

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `TraconTestHost` yerine, `AddTracon()`in **hiç çağrılmadığı** ayrı,
   yalın bir `WebApplication` kur (bu case `TraconTestHost` kullanmaz,
   çünkü o zaten dahili olarak `AddTracon()` çağırır).
2. `IHealthCheck`ı çöz, çalıştır.

**Girilecek veri**
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tracon;

var builder = WebApplication.CreateSlimBuilder();
builder.Services.AddHealthChecks().AddTraconHealthChecks();
// DIKKAT: builder.Services.AddTracon() HIC CAGRILMADI.

var app = builder.Build();

try
{
    var healthCheckService = app.Services.GetRequiredService<HealthCheckService>();
    await healthCheckService.CheckHealthAsync();
    Console.WriteLine("HATA: istisna beklenirdi");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine("Beklenen: DI cozumu basarisiz -- " + ex.Message[..Math.Min(120, ex.Message.Length)]);
}
```

> 🚨 **Düzeltildi (koşum, 2026-08-13, doküman kusuru — aynı desen
> MT-DIAG-002'de):** `GetServices<IHealthCheck>().OfType<TraconHealthCheck>()`
> yerine `HealthCheckService.CheckHealthAsync()` kullanıldı; istisna zaten
> `CheckHealthAsync` sırasında (Factory çağrısında) fırlatılıyor, davranış
> beklenenle aynı.

**Beklenen sonuç**
- `TraconDiagnosticsCollector`in bağımlılıklarından biri (örn.
  `IAgentCatalog`) çözülemediği için bir `InvalidOperationException`
  fırlatılır — kayıt sırasında **değil**, yalnız ilk yoklamada.

### MT-DIAG-020 — Varsayılan kapalı: hiç açılmadan `/api/diagnostics` `404` döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | Açık Soru 4 |

🚨 **Örnek uygulama üzerinde gösterilemez** — orada zaten açık
(bkz. "Koşmadan önce" madde 5). Bu case bilinçli olarak izlek C kullanır.

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `TraconTestHost`ı hiçbir `ConfigureEndpoints` vermeden başlat.
2. `/tracon/api/diagnostics`a GET at.

**Girilecek veri**
```csharp
using Tracon.Testing;

await using var host = await TraconTestHost.StartAsync();

using var response = await host.Client.GetAsync("/tracon/api/diagnostics");
Console.WriteLine("Durum: " + (int)response.StatusCode);
```

**Beklenen sonuç**
- `Durum: 404` — `EnableDiagnosticsEndpoint`in varsayılanı `false`
  olduğundan uç hiç haritalanmaz.

---

### MT-DIAG-021 — Açıkken `200` döner ve rapor şemasını taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor (`EnableDiagnosticsEndpoint = true` zaten kodda).
- **Rol kurulumu yapılmamış** (bu case'in konusu Admin **denetimi değil**,
  raporun şeklidir — bkz. MT-DIAG-022 gerçek Admin denetimi için).

**Adımlar**
1. `/api/diagnostics`ı çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | jq
```

**Beklenen sonuç**
- `200 OK`.
- Gövde şu alanların **tamamını** taşır: `persistenceProvider`,
  `registeredPersistenceProviders`, `canConnect`, `migrationsUpToDate`,
  `pendingMigrations`, `modelProviders` (dizi, her öğede `name`/`status`/`circuitOpen`),
  `configuration` (dizi, her öğede `key`/`resolved`/`hint`), `uiEmbedded`,
  `toolCount`, `agentCount` (`TraconDiagnosticsReport.cs`'in gerçekleşen 10 alanı).

---

### MT-DIAG-022 — Admin olmayan rolle `403` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

🚨 Bu case, `13-KIRACI-VE-GUVENLIK.md` §8'deki **geçici** `RoleTestAuthHandler`
kurulumu **olmadan** anlamsızdır — kurulum olmadan hiçbir rol policy'si kayıtlı
değildir ve `Admin` gerektiren bir uç **her isteğe** `200` verir (K-042,
`13-KIRACI-VE-GUVENLIK.md`nin MT-SEC-080 bulgusu). Bu, `/api/diagnostics`'e
özgü bir kusur değildir — tüm `RequireRole` çağrılarının ortak davranışıdır.

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md` §8'in 1-5 numaralı geçici kurulum adımları
  uygulandı, uygulama yeniden başlatıldı.

**Adımlar**
1. `X-Test-Role: reader` (Admin değil) ile `/api/diagnostics`ı çağır.
2. `X-Test-Role: admin` ile aynı ucu çağır (kontrol grubu).

**Girilecek veri**
```bash
curl -s -i "$APU/api/diagnostics" -H "$APB" -H "X-Test-Role: reader" | head -1
curl -s -i "$APU/api/diagnostics" -H "$APB" -H "X-Test-Role: admin"  | head -1
```

**Beklenen sonuç**
- `reader` rolüyle **`403 Forbidden`**.
- `admin` rolüyle **`200 OK`**.

---

### MT-DIAG-023 — 🚨 Bilinen bir API anahtarı yanıtın hiçbir yerinde geçmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-059 |

Faz 33'ün kendi kapanış ölçümünün (`docs/arsiv/fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md`,
sızıntı denetimi) birebir tekrarı — bu depodaki `secret` sızıntı testlerinin
en doğrudan olanlarından biridir.

**Ön koşul**
- Örnek uygulamada gerçek bir OpenAI anahtarı `dotnet user-secrets`'ta tanımlı.

**Adımlar**
1. `user-secrets`taki gerçek anahtarı oku.
2. Teşhis yanıtının tamamında bu anahtarı ara.

**Girilecek veri**
```bash
KEY=$(dotnet user-secrets list --project samples/Tracon.Api \
      | grep -i 'OpenAI:ApiKey' | cut -d= -f2- | tr -d ' ')

curl -s "$APU/api/diagnostics" -H "$APB" | grep -F "$KEY" && echo "SIZINTI VAR" || echo "temiz"
```

**Beklenen sonuç**
- Çıktı **"temiz"** yazar — anahtarın kendisi hiçbir alanda görünmez, yalnız
  `configuration[].resolved: true` biçiminde bir bayrak taşınır.

---

### MT-DIAG-024 — Config anahtarları yalnız `resolved` bilgisini taşır, DEĞER hiç yoktur (izole doğrulama)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-059 |

MT-DIAG-023'ün izole/deterministik karşılığı — gerçek bir anahtar olmadan,
sahte bir yapılandırma anahtarıyla `Resolved`/`Hint` alanlarının davranışını
doğrular. `Tracon.Testing`'in kendisi bir `IModelProviderConfigurationDiagnostics`
uygulaması taşımadığından, bu case doğrudan sözleşme tipini örnekler.

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `ConfigurationDiagnostic` kaydını elle oluştur, alanlarını doğrula (tip
   düzeyinde bir sözleşme testidir — gerçek bir sağlayıcı gerekmez).

**Girilecek veri**
```csharp
using Tracon;

var cozulmemis = new ConfigurationDiagnostic
{
    Key = "Tracon:Providers:OpenAI:ApiKey",
    Resolved = false,
    Hint = "dotnet user-secrets set \"Tracon:Providers:OpenAI:ApiKey\" \"<ANAHTARINIZ>\"",
};

Console.WriteLine("Key: " + cozulmemis.Key);
Console.WriteLine("Resolved: " + cozulmemis.Resolved);
Console.WriteLine("Hint: " + cozulmemis.Hint);
// Tipte bir "Value"/"ApiKey" alani ARANDIGINDA DERLEME HATASI beklenir --
// alttaki satirin YORUM SATIRI olarak kalmasi kanittir:
// var deger = cozulmemis.Value; // CS1061 -- boyle bir uye yok
```

**Beklenen sonuç**
- `ConfigurationDiagnostic` tipinde **yalnız** `Key`, `Resolved`, `Hint`
  üyeleri vardır (`ConfigurationDiagnostic.cs`) — değeri taşıyan hiçbir alan
  yoktur; bu, `secret`in tip düzeyinde **yapısal olarak** imkansız kılındığının
  kanıtıdır (bir alan eklemek için tipin kendisi değişmelidir).

---

### MT-DIAG-025 — Aynı sağlayıcının iki örneği aynı config anahtarını TEK satır raporlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

Sınır senaryosu. `TraconDiagnosticsCollector.CollectAsync`in kendi yorumu:
*"Aynı sağlayıcı (örnek: `UseOpenAI()` ChatCompletions VE Responses için iki
örnek kaydeder) aynı yapılandırma anahtarını birden fazla bildirebilir; rapor
anahtar başına tek satır taşır"* (`TraconDiagnosticsCollector.cs:110-112`,
`seenConfigurationKeys` `HashSet`i).

**Ön koşul**
- Örnek uygulama OpenAI anahtarıyla çalışıyor (`UseOpenAI()` hem `openai`
  hem `openai-responses` sağlayıcı adını kaydeder — `03-KALICILIK-POSTGRESQL.md`
  fixture'larına paralel, `05-SAGLAYICI-OPENAI.md`'de doğrulanmış davranış).

**Adımlar**
1. `/api/diagnostics`ı çağır, `configuration` dizisinde `Tracon:Providers:OpenAI:ApiKey`
   anahtarının kaç kez geçtiğini say.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | \
  jq '[.configuration[] | select(.key == "Tracon:Providers:OpenAI:ApiKey")] | length'
```

**Beklenen sonuç**
- Sonuç **`1`**dir — `openai` ve `openai-responses` iki ayrı `IModelProvider`
  örneği olsa da aynı config anahtarını bildirdiklerinden yalnız ilki kayda geçer.

---

### MT-DIAG-026 — `UseOpenAICompatible` hiçbir `ConfigurationDiagnostic` bildirmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-249 |

Negatif/sınır senaryosu. K-249'un doğrudan kanıtı: `UseOpenAICompatible`'ın
sabit bir yapılandırma bölüm yolu **yoktur** (kod içinde serbestçe yapılandırılır);
yanlış bir anahtar adı raporlamak yerine bilinçli olarak **hiç raporlanmaz**.

**Ön koşul**
- Örnek uygulama OpenRouter anahtarıyla çalışıyor (`openrouter-support` fixture'ı).

**Adımlar**
1. `/api/diagnostics`taki `modelProviders` listesinde `openrouter`ın var
   olduğunu, ama `configuration` listesinde OpenRouter'a ait hiçbir anahtarın
   **olmadığını** doğrula.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | jq '{
  openrouterModelProvider: [.modelProviders[] | select(.name == "openrouter")],
  openrouterConfig: [.configuration[] | select(.key | test("openrouter"; "i"))]
}'
```

**Beklenen sonuç**
- `openrouterModelProvider` **bir** öğe içerir (`name: "openrouter"`,
  `status`, `circuitOpen`).
- `openrouterConfig` **boş dizi** döner — `openrouter`ın API anahtarı gerçekte
  tanımlı olsa bile `configuration` listesinde **hiçbir** satırı yoktur.

---

### MT-DIAG-027 — `RegisteredPersistenceProviders` bellek içi `0`, tek sağlayıcıda `1` sayar

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-183, K-247 |

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. Hiçbir `Use*Sql()` çağrılmadan raporu topla, `registeredPersistenceProviders`ı oku.
2. `UseSqlite`i bir kez çağırarak aynı ölçümü tekrarla.

**Girilecek veri**
```csharp
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

var provider = new FakeModelProvider().EchoesUserMessage();

await using (var bellekIci = await TraconTestHost.StartAsync(o => o.ModelProvider = provider))
{
    var collector = bellekIci.Services.GetRequiredService<TraconDiagnosticsCollector>();
    var report = await collector.CollectAsync();
    Console.WriteLine("Bellek ici -- persistenceProvider: " + report.PersistenceProvider +
                       ", registered: " + report.RegisteredPersistenceProviders);
}

await using (var sqliteli = await TraconTestHost.StartAsync(o =>
{
    o.ModelProvider = provider;
    o.ConfigureTracon = b => b.UseSqlite("Data Source=:memory:");
}))
{
    var collector = sqliteli.Services.GetRequiredService<TraconDiagnosticsCollector>();
    var report = await collector.CollectAsync();
    Console.WriteLine("SQLite -- persistenceProvider: " + report.PersistenceProvider +
                       ", registered: " + report.RegisteredPersistenceProviders);
}
```

> 🐛 **ÜRÜN KUSURU — HATA-S1-003'ün üçüncü tekrarı (aynı kök neden MT-DIAG-005
> ve `04-KALICILIK-DIGER.md`nin `MT-SQL-005`'inde).** Yukarıdaki kod, **tek**
> `UseSqlite("Data Source=:memory:")` çağrısıyla bile (K-183 çoklu-kayıt
> senaryosu yok) `SqliteException: no such table: tracon_tenants` ile
> çöküyor. Bu, HATA-S1-003'ün K-183'ten tamamen bağımsız, en yalın hâlidir —
> `TraconTestHost` üzerinden bulundu.

**Beklenen sonuç**
- Bellek içi: `persistenceProvider: InMemory`, `registered: 0`.
- SQLite: `persistenceProvider: SQLite`, `registered: 1`.

---

### MT-DIAG-028 — `toolCount`/`agentCount` gerçek kayıtlı sayıyı yansıtır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `/api/agents` ve `/api/diagnostics`taki sayıları çapraz karşılaştır.

**Girilecek veri**
```bash
echo "agents ucu: $(curl -s "$APU/api/agents" -H "$APB" | jq 'length')"
curl -s "$APU/api/diagnostics" -H "$APB" | jq '{toolCount, agentCount}'
```

**Beklenen sonuç**
- `/api/diagnostics`taki `agentCount`, `/api/agents`ın döndürdüğü dizinin
  uzunluğuyla **birebir eşleşir**.
- `toolCount` `0`'dan büyüktür (örnek uygulamanın en az `get_order_status`,
  `list_recent_orders`, `cancel_order` tool'ları kayıtlıdır).

---

### MT-DIAG-029 — `uiEmbedded` arayüz paketine göre doğru değer taşır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

🚨 `TraconDiagnosticsCollector.CollectAsync`in kendi XML dokümanı:
`UiEmbedded` her zaman `false` döner çünkü bu alan `Tracon.Core`'un
bilmediği `Tracon.AspNetCore` katmanına aittir — **`DiagnosticsEndpoints.Map`
bunu bir `with` ifadesiyle sonradan doldurur** (`DiagnosticsEndpoints.cs:42`,
`TraconDiagnosticsCollector.cs:66-69`).

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `TraconDiagnosticsCollector.CollectAsync()`i **doğrudan** (HTTP ucu
   olmadan) çağır, `UiEmbedded`ı oku.

**Girilecek veri**
```csharp
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

var provider = new FakeModelProvider().EchoesUserMessage();
await using var host = await TraconTestHost.StartAsync(o => o.ModelProvider = provider);

var collector = host.Services.GetRequiredService<TraconDiagnosticsCollector>();
var report = await collector.CollectAsync();

Console.WriteLine("Dogrudan CollectAsync -- UiEmbedded: " + report.UiEmbedded);
```

**Beklenen sonuç**
- `Dogrudan CollectAsync -- UiEmbedded: False` — konsol projesi `Tracon.UI`
  paketini hiç referans vermediği için bu **doğru** bir sonuçtur, ama asıl
  kanıt şudur: değer **her zaman** `false`dur, çünkü `Tracon.Core`
  katmanı bunu asla dolduramaz. `/api/diagnostics` HTTP ucu (MT-DIAG-021,
  örnek uygulamada arayüz paketi kayıtlıysa) `uiEmbedded: true` döndürüyorsa,
  bu farkın kaynağı `DiagnosticsEndpoints.cs:42`'deki `with` ifadesidir.

---

### MT-DIAG-030 — Teşhis ucu hiçbir model çağrısı veya migration uygulaması üretmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

Negatif/kontrol senaryosu — `CollectAsync`in XML dokümanının
("yan etkisizdir... migration UYGULANMAZ") ampirik kanıtı.

**Ön koşul**
- `~/tracon-manuel/test-paketi` kurulu.

**Adımlar**
1. `CollectAsync()`i üç kez art arda çağır.
2. `FakeModelProvider.Requests.Count`ı ölç.

**Girilecek veri**
```csharp
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

var provider = new FakeModelProvider().EchoesUserMessage();
await using var host = await TraconTestHost.StartAsync(o => o.ModelProvider = provider);

var collector = host.Services.GetRequiredService<TraconDiagnosticsCollector>();

for (var i = 0; i < 3; i++)
{
    await collector.CollectAsync();
}

Console.WriteLine("Requests.Count: " + provider.Requests.Count);
```

**Beklenen sonuç**
- `Requests.Count: 0`.

### MT-DIAG-040 — `Tracon.AspNetCore.csproj` `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` taşımaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | K-039, L35 |

Negatif/kontrol senaryosu — kütüphanenin CVE'li (GHSA-v5pm-xwqc-g5wc) bir
geçişli bağımlılık dayatmadığının doğrudan kanıtı. `OpenApiDependencyTests`in tekrarı.

**Ön koşul**
- Yok.

**Adımlar**
1. Kaynak `.csproj`'u tara.
2. Paketlenmiş `.nuspec`i de tara (yerel feed hazırsa).

**Girilecek veri**
```bash
grep -n "Microsoft.AspNetCore.OpenApi\|Microsoft.OpenApi" \
  src/Tracon.AspNetCore/Tracon.AspNetCore.csproj && echo "VAR" || echo "temiz"

unzip -p ~/tracon-local-feed/Tracon.AspNetCore.*.nupkg \
  Tracon.AspNetCore.nuspec 2>/dev/null | grep -i "openapi" && echo "VAR" || echo "temiz"
```

**Beklenen sonuç**
- İki tarama da **"temiz"** yazar.

---

### MT-DIAG-041 — 🚨 Örnek uygulamada `GET /openapi/v1.json` `200` döner (K-352/F-76 düzeltmesi)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 40 (düzeltme sonraki bir oturumda, K-352) |
| **İlgili karar** | K-352 |

Bu, Faz 40'ın kendi kapanış notunda **çözülmeden bırakılan** F-76'nın
("`AddOpenApi()` + SqlServer/Sqlite birlikte 500 veriyor") sonradan **düzeltildiğinin**
kanıtıdır — `samples/Tracon.Api.csproj`'daki `TraconRemoveDuplicateSqlXmlDocs`
MSBuild hedefi (K-352) artık iki paylaşılan-kaynak XML doküman çakışmasını
(`Tracon.SqlServer`/`Tracon.Sqlite`, K-185 linked-source deseni) belgeleme
üretiminden önce ayıklıyor.

**Ön koşul**
- Örnek uygulama çalışıyor (`.csproj`'u hem `Tracon.SqlServer` hem
  `Tracon.Sqlite`ı `ProjectReference` ile taşıyor — hangi `Use*Sql()`
  çağrıldığından **bağımsız**, çakışma derleme zamanı XML doküman üretiminde
  oluşur).

**Adımlar**
1. OpenAPI belgesini çağır.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5080/openapi/v1.json
```

**Beklenen sonuç**
- **`200`** döner (F-76'nın orijinal kanıtladığı `500` **değil**).
- Bu çalışmazsa (`500` dönerse), `samples/Tracon.Api.csproj`'daki
  `TraconRemoveDuplicateSqlXmlDocs` hedefinin kaldırılmış veya bozulmuş
  olabileceği anlamına gelir — `OpenApiSharedSqlXmlDocTests.cs`'in kendisi
  bunu zaten bir birim/fonksiyonel testle korur; bu case gerçek çalışan
  uygulamada **ayrıca** doğrular.

---

### MT-DIAG-042 — Belgedeki her ucun en az iki etiketi var, ilki `Tracon`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | 40.3 |

**Ön koşul**
- MT-DIAG-041 geçti (belge `200` ile üretiliyor).

**Adımlar**
1. Belgeyi indir.
2. İki etiketten azına sahip bir uç ara.
3. İlk etiketin her yerde `Tracon` olduğunu doğrula.

**Girilecek veri**
```bash
curl -s http://localhost:5080/openapi/v1.json > /tmp/apidoc.json

jq -r '.paths | to_entries[] | .key as $p | .value | to_entries[]
       | select((.value.tags // []) | length < 2)
       | "\($p) \(.key)"' /tmp/apidoc.json

jq -r '[.paths[][]? | .tags[0]] | unique' /tmp/apidoc.json
```

**Beklenen sonuç**
- İlk sorgu **boş** döner — iki etiketten az taşıyan uç yoktur.
- İkinci sorgu **tek elemanlı** bir dizi döner: `["Tracon"]`.

---

### MT-DIAG-043 — `operationId` değerleri benzersizdir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | — |

Negatif senaryo — bir istemci üreteci çakışan `operationId`de sessizce birini
ezer.

**Ön koşul**
- MT-DIAG-041 geçti.

**Adımlar**
1. Tüm `operationId`leri topla, tekrar edeni ara.

**Girilecek veri**
```bash
jq -r '[.paths[][]?.operationId] | group_by(.) | map(select(length>1)) | .[][0]' \
  /tmp/apidoc.json

jq -r '[.paths[][]?.operationId] | length, ([.[]] | unique | length)' /tmp/apidoc.json
```

**Beklenen sonuç**
- İlk sorgu **boş** döner (tekrar eden yok).
- İkinci sorgunun iki satırı **eşittir** — toplam sayı ile benzersiz sayı aynıdır.

---

### MT-DIAG-044 — `POST /api/agents/{name}/run` yalnız `text/event-stream` bildirir, tipli JSON DEĞİL

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | K-275 |

🚨 Bu, Faz 40'ın **kendi planının yanlış çıktığı** noktadır (Plandan
Sapmalar #7): plan bu ucun "hem tipli hem akışlı" olduğunu varsaymıştı, kod
incelemesi başarı yanıtının **her zaman** SSE olduğunu gösterdi. Bu case o
düzeltilmiş gerçeği doğrular.

**Ön koşul**
- MT-DIAG-041 geçti.

**Adımlar**
1. `TraconRunAgent` operasyonunun yanıt şemasını oku.

**Girilecek veri**
```bash
jq '.paths["/tracon/api/agents/{name}/run"].post.responses' /tmp/apidoc.json
```

**Beklenen sonuç**
- `200` yanıtının `content` alanı **yalnız** `text/event-stream` içerir —
  `application/json` **yoktur**.
- `400`, `404`, `429` durumları `application/problem+json` ile
  `ProblemDetails` şeması bildirir (`ProducesProblem` çağrıları).

---

### MT-DIAG-045 — `/v1/chat/completions` hem `application/json` hem `text/event-stream` içerik tipini BİRLİKTE bildirir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | K-274, K-275 |

Bu ucun asıl "dual-mode" (JSON/SSE) örneği olduğunun kanıtı — Faz 40'ın
`additionalContentTypes` parametresiyle **TEK** `.Produces` çağrısında
topladığı davranış (K-274'ün çözümü).

**Ön koşul**
- MT-DIAG-041 geçti.

**Adımlar**
1. `TraconOpenAIChatCompletions` operasyonunun `200` yanıtındaki içerik
   tiplerini oku.

**Girilecek veri**
```bash
jq '.paths["/tracon/v1/chat/completions"].post.responses."200".content | keys' /tmp/apidoc.json
```

**Beklenen sonuç**
- Dizi **hem** `application/json` **hem** `text/event-stream` içerir — K-274
  düzeltilmeden önce ikinci `.Produces` çağrısı birinciyi eziyordu (yalnız
  tek içerik tipi kalıyordu); bu case o düzeltmenin kalıcılığını doğrular.

---

### MT-DIAG-046 — `Diagnostics` etiketi varsayılan üretilen belgede YOKTUR (uç varsayılan kapalı)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 × 40 |
| **İlgili karar** | — |

Sınır senaryosu — iki fazın kesişimi. `DiagnosticsEndpoints` kendi etiketini
taşır (`DiagnosticsEndpoints.cs:46`, `.WithTags("Tracon", "Diagnostics")`)
ama `EnableDiagnosticsEndpoint`in varsayılanı `false` olduğundan bu uç
**varsayılan yapılandırmada hiç haritalanmaz** — OpenAPI belgesi yalnız
haritalanmış uçları listeler.

**Ön koşul**
- MT-DIAG-041 geçti (bu ölçüm örnek uygulamanın gerçek belgesi üzerinde
  yapılır — orada `EnableDiagnosticsEndpoint = true` olduğu için burada
  **`Diagnostics` etiketinin var OLMASI** beklenir; varsayılan-kapalı bir
  kurulumda YOK olması gerektiğini göstermek için ikinci bir ölçüm izlek
  C ile yapılır).

**Adımlar**
1. Örnek uygulamanın belgesinde `Diagnostics` etiketinin **var olduğunu** doğrula
   (çünkü orada uç açık).
2. `~/tracon-manuel/test-paketi`de, `EnableDiagnosticsEndpoint`
   ayarlanmadan bir `TraconTestHost` başlat; o host'ta uç hiç
   haritalanmadığı için OpenAPI'ye bu dosyanın kapsamı **dışıdır**
   (`Tracon.Testing` OpenAPI belgesi üretmez) — bu adım yalnız `404`
   ile ucun gerçekten yok olduğunu (MT-DIAG-020'nin tekrarı) teyit eder.

**Girilecek veri**
```bash
jq -r '[.paths[][]? | .tags[]] | unique | .[] | select(. == "Diagnostics")' /tmp/apidoc.json
```

**Beklenen sonuç**
- Sorgu `Diagnostics` yazar — örnek uygulamanın belgesinde (uç açık
  olduğundan) etiket **vardır**.
- **Not:** Bu case'in başlığındaki "varsayılan belgede YOKTUR" iddiası, F-76
  düzeltmesinden ÖNCEKİ (Faz 40 kapanış anındaki, `EnableDiagnosticsEndpoint`
  o zaman da örnek uygulamada açıktı ama farklı bir nedenle — bkz. Plandan
  Sapmalar #1'in "**varsayılan yapılandırmada** haritalanmaz" ifadesi) genel
  kütüphane davranışına aittir; **bu depodaki örnek uygulamanın belgesinde**
  etiket görünür çünkü uç bilinçli olarak açılmıştır. Koşumda hangi durumun
  gözlemlendiği (var/yok) kesin olarak kaydedilir — iki ifade de (kütüphane
  varsayılanı kapalı, örnek uygulama açık) doğru olabilir aynı anda.

---

### MT-DIAG-047 — `docs/openapi/tracon.json` çalışan host'un ürettiğiyle AYNIDIR (anlık görüntü)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | 40.4 |

`OpenApiSnapshotTests`in elle tekrarı — depoya işlenmiş dosyanın koddan
**sapmadığının** kanıtı.

**Ön koşul**
- MT-DIAG-041 geçti.

**Adımlar**
1. Çalışan host'tan alınan belgeyi işlenmiş dosyayla karşılaştır.

**Girilecek veri**
```bash
diff <(jq -S . /tmp/apidoc.json) <(jq -S . docs/openapi/tracon.json) \
  && echo "AYNI" || echo "FARKLI -- yenileme gerekir"
```

**Beklenen sonuç**
- `AYNI` yazdırılır. `FARKLI` çıkarsa, kod HTTP yüzeyini değiştirmiş ama
  `docs/openapi/tracon.json` yenilenmemiş demektir; yenileme komutu
  `TRACON_OPENAPI_REFRESH=1 dotnet test tests/Tracon.AspNetCore.FunctionalTests
  -c Release --filter FullyQualifiedName~OpenApiSnapshotTests`dir (Faz 40
  devir notu) — bu bir kusur değil, doğal bakım adımıdır (dosya son
  üretildiğinden beri kaç faz geçtiğine bağlı olarak fark **beklenir**
  olabilir; koşumda hangisi olduğu kaydedilir).

---

### MT-DIAG-048 — Belge bağımsız bir istemci üretecinden hatasız geçer

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 40 |
| **İlgili karar** | — |

Faz 40'ın kendi DoD kanıtının (`npx openapi-typescript` + `tsc --strict`)
tekrarı.

**Ön koşul**
- Node.js kurulu. `docs/openapi/tracon.json` mevcut.

**Adımlar**
1. Belgeyi TypeScript tiplerine dönüştür.
2. Üretilen dosyayı katı modda derle.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/Tracon
npx --yes openapi-typescript docs/openapi/tracon.json -o /tmp/tracon-api-types.ts

npx --yes typescript@latest --strict --noEmit /tmp/tracon-api-types.ts
echo "tsc cikis kodu: $?"
```

**Beklenen sonuç**
- `openapi-typescript` hatasız biter, bir `.ts` dosyası üretir.
- `tsc --strict --noEmit` **sıfır** hatayla biter (çıkış kodu `0`).

---

### MT-DIAG-049 — Yeşil alan örneğinde `extensionPoints`: dördü yerleşik, biri değil — Faz 85

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 85 |
| **İlgili karar** | — |

🚨 **Plandan sapma:** `samples/Tracon.Api` saf bir "beşi de yerleşik"
örneği DEĞİLDİR — `Program.cs` zaten `IRunAttributionContext`'i
`DemoRunAttributionContext` ile bağlar (Faz 68, kullanıcı bazlı maliyet
demosu için). Bu case gerçek durumu ölçer.

**Ön koşul**
- `samples/Tracon.Api` ayakta (`dotnet run --project samples/Tracon.Api`),
  `Tracon:Ui:AllowRemoteAccess` gerekmez (loopback yeterli).

**Adımlar**
1. Teşhis ucunu oku.

**Girilecek veri**
```bash
curl -s http://localhost:5080/tracon/api/diagnostics | jq '.extensionPoints'
```

**Beklenen sonuç**

🚨 **Doküman düzeltildi (koşum, 2026-09-17, ap-s3).** Spec Faz 85 anında
**beş** genişleme noktası varsayıyordu; kaynak (`TraconExtensionPoints.cs`'in
kendi XML dokümanı: *"the **seven** embedding points"*) artık **yedi**
taşıyor — sonraki bir faz `IRunAuthorizationHandler` ve
`IToolApprovalPresenter`'ı ekledi. Ürün kusuru değil, doküman bayatlığı
(51-migration deseninin aynısı).

- Dizi **7** eleman taşır (5 değil).
- `IToolAuthorizationHandler`, `IRunAuthorizationHandler`, `IRunEventSink`,
  `IAttachmentStorage` → `isBuiltInDefault: true`.
- `IRunAttributionContext` → `isBuiltInDefault: false`,
  `implementation: "DemoRunAttributionContext"` — bu ÖNCEDEN gelen bir
  bağlamadır, Faz 85'in eklediği bir şey değil.
- `IToolApprovalPresenter` → `isBuiltInDefault: false`,
  `implementation: "OrderApprovalPresenter"` (`Program.cs:146`,
  `AddSingleton<IToolApprovalPresenter, OrderApprovalPresenter>()`).
- **`ITenantContext`'in kendisi ARTIK `isBuiltInDefault: true` DEĞİL** —
  `implementation: "HttpTenantContext"`, `isBuiltInDefault: false`.
  Kaynağa göre (`TraconExtensionPoints.cs`) Core katmanının çıplak
  varsayılanı `SingleTenantContext`'tir; `Tracon.AspNetCore` barındırma
  katmanı kendi HTTP-context-farkında varsayılanını (`HttpTenantContext`)
  bağlıyor — bu **tüketicinin özelleştirmesi değil**, ASP.NET Core
  entegrasyonunun kendi (Core'dan farklı) varsayılanıdır. Spec'in "beşi
  yerleşik" varsayımı bu ayrımı hiç görmüyordu.

---

### MT-DIAG-050 — Gömme örneğinde `extensionPoints`: beşi de kendi tipleri — Faz 85

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 85 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Embedded` ayakta (`dotnet run --project samples/Tracon.Embedded`).

**Adımlar**
1. Teşhis ucunu oku.

**Girilecek veri**
```bash
curl -s http://localhost:5082/tracon/api/diagnostics > /tmp/embedded-diag.json
jq '.extensionPoints' /tmp/embedded-diag.json
```

**Beklenen sonuç**

🚨 **Doküman düzeltildi (koşum, 2026-09-17, ap-s3) — MT-DIAG-049'daki AYNI
5→7 bayatlığı.**

- Dizi **7** eleman taşır (5 değil).
- Altısı `isBuiltInDefault: false` — `implementation` alanları:
  `ITenantContext→EmbeddedTenantContext`,
  `IRunAttributionContext→EmbeddedRunAttributionContext`,
  `IToolAuthorizationHandler→EmbeddedToolAuthorizationHandler`,
  `IRunAuthorizationHandler→EmbeddedRunAuthorizationHandler` (yeni nokta,
  Embedded örneği bunu da özelleştiriyor), `IRunEventSink→
  BoundedChannelRunEventSink`, `IAttachmentStorage→
  InMemoryBufferAttachmentStorage`.
- **`IToolApprovalPresenter`** — `isBuiltInDefault: true`,
  `implementation: "NullToolApprovalPresenter"` — Embedded örneği bunu
  ÖZELLEŞTİRMİYOR, tek istisna budur (Faz 85'in "beşi de kendi tipi"
  iddiası artık yedi noktanın altısı için doğru, yedincisi için değil).

---

### MT-DIAG-051 — `extensionPoints` hiçbir yapılandırma değeri sızdırmaz — Faz 85

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 85 |
| **İlgili karar** | K-059 |

**Ön koşul**
- MT-DIAG-050 geçti; `/tmp/embedded-diag.json` bu ucun çıktısını taşıyor.

**Adımlar**
1. `implementation` ve `contract` alanlarının yalnız **tip adı** taşıdığını,
   hiçbir `Host`/`localhost`/bağlantı dizesi/`ApiKey`/`sk-` deseni
   İÇERMEDİĞİNİ doğrula.

**Girilecek veri**
```bash
jq -r '.extensionPoints[] | .contract, .implementation' /tmp/embedded-diag.json \
  | grep -inE "sk-|apikey|connectionstring|password|://" \
  && echo "SIZINTI" || echo "TEMIZ"
```

**Beklenen sonuç**
- `TEMIZ` yazdırılır. Her `implementation` yalnız bir CLR tip adıdır
  (örn. `EmbeddedTenantContext`); tip adı bir sır değildir, ama alanın
  KENDİSİ hiçbir yapılandırma değeri taşımaz (K-059).

---
### MT-DIAG-052 — `Production` + kalıcı olmayan store başlangıçta TAM BİR uyarı düşürür — Faz 104

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 104 |
| **İlgili karar** | K-624 |

**Ön koşul**
- Örnek uygulama derlendi. Hiçbir SQL bağlantı dizesi yapılandırılmamış olmalı
  (`user-secrets` bir bağlantı dizesi taşıyorsa aşağıdaki boş ortam değişkenleri
  onu ezer).

**Adımlar**
1. Örnek uygulamayı `Production` ortamında, kalıcı olmayan store ile başlat.
2. Kalkış log'unda uyarıyı say.
3. `/api/meta`'nın aynı yargıyı verdiğini doğrula.

**Girilecek veri**
```bash
ASPNETCORE_ENVIRONMENT=Production \
Tracon__PostgreSql__ConnectionString= \
Tracon__SqlServer__ConnectionString= \
Tracon__Sqlite__ConnectionString= \
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz104-prod.log 2>&1 &
sleep 14
grep -ci "storage that is not persistent" /tmp/faz104-prod.log        # beklenen: 1
grep -B1 "storage that is not persistent" /tmp/faz104-prod.log | head -1
curl -s http://localhost:5099/tracon/api/meta | grep -o '"persistent":[a-z]*'
```

**Beklenen sonuç**
- Uyarı **tam bir kez** düşer; satır `warn:` seviyesindedir ve kaynağı
  `Tracon.NonPersistentStorageWarningService`'tir.
- Mesaj üç şeyi adlandırır: hangi store'ların kalıcı olmadığı
  (`agent definitions, runs, sessions`), verinin süreç ömrüyle sınırlı olduğu,
  ve kalıcılığa geçiş çağrısı (`UsePostgreSql(...)`).
- `/api/meta` `"persistent":false` döner — log ile uç **aynı** yargıyı verir.
- Uygulama ayağa kalkar; uyarı bir hata değildir.

---

### MT-DIAG-053 — Aynı kurulum `Development`'ta SESSİZDİR — Faz 104

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 104 |
| **İlgili karar** | K-624 |

**Ön koşul**
- MT-DIAG-052 koşuldu ve süreç durduruldu (`pkill -f Tracon.Api`).

**Adımlar**
1. **Aynı** store kurulumuyla, yalnız ortamı değiştirerek başlat.

**Girilecek veri**
```bash
ASPNETCORE_ENVIRONMENT=Development \
Tracon__PostgreSql__ConnectionString= \
Tracon__SqlServer__ConnectionString= \
Tracon__Sqlite__ConnectionString= \
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz104-dev.log 2>&1 &
sleep 14
grep -c "Application started" /tmp/faz104-dev.log                     # beklenen: 1
grep -ci "storage that is not persistent" /tmp/faz104-dev.log         # beklenen: 0
```

**Beklenen sonuç**
- Uygulama kalkar ve **hiç uyarı düşmez**. Store kurulumu MT-DIAG-052 ile
  birebir aynı olduğu için tek değişken ortamdır — bu, kontrolün gerçekten
  `IHostEnvironment.IsProduction()` üzerinden çalıştığını kanıtlar.

---

### MT-DIAG-054 — `Production` + kalıcı store SESSİZDİR (yanlış pozitif yok) — Faz 104

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 104 |
| **İlgili karar** | K-624 |

**Ön koşul**
- Önceki süreç durduruldu.

**Adımlar**
1. `Production` ortamında, SQLite kalıcılığı ile başlat.

**Girilecek veri**
```bash
ASPNETCORE_ENVIRONMENT=Production \
Tracon__PostgreSql__ConnectionString= \
Tracon__SqlServer__ConnectionString= \
Tracon__Sqlite__ConnectionString="Data Source=/tmp/faz104.db" \
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz104-sqlite.log 2>&1 &
sleep 16
grep -ci "storage that is not persistent" /tmp/faz104-sqlite.log      # beklenen: 0
curl -s http://localhost:5099/tracon/api/meta | grep -o '"runStore":"[A-Za-z]*"'
```

**Beklenen sonuç**
- Uyarı düşmez ve `/api/meta` `"runStore":"SqlRunStore"` ile
  `"persistent":true` döner. Kalıcı bir kurulum uyarı görmez.

---

### MT-DIAG-055 — `Production` + kayıtsız `IContentGuard` başlangıçta TAM BİR uyarı düşürür — Faz 122

`SilentGapWarningService`, `NonPersistentStorageWarningService`'in aynı üç
kuralını (yalnız Production · asla fırlatma · yalnız kayıtlara bak) iki farklı
boşluğa uygular. Bu case birincisi: içerik denetimi.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 122 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Api/Program.cs`'teki `.AddPatternContentGuard(...)`
  çağrısını (üç satırlık `options => { ... }` bloğuyla birlikte) GEÇİCİ olarak
  yorum satırına al — örnek uygulama varsayılan olarak bir guard kaydeder,
  bu case'in ölçtüğü tam da o kaydın YOKLUĞUdur.

**Adımlar**
1. Değiştirilmiş örnek uygulamayı `Production` ortamında başlat.
2. Kalkış log'unda uyarıyı say.
3. `Program.cs`'i geri al (`git checkout -- samples/Tracon.Api/Program.cs`), yeniden derle.

**Girilecek veri**
```bash
ASPNETCORE_ENVIRONMENT=Production \
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz122-guard-prod.log 2>&1 &
sleep 14
grep -ci "no IContentGuard registered" /tmp/faz122-guard-prod.log     # beklenen: 1
grep -B1 "no IContentGuard registered" /tmp/faz122-guard-prod.log | head -1
pkill -f Tracon.Api
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Uyarı **tam bir kez** düşer; satır `warn:` seviyesindedir ve kaynağı
  `Tracon.SilentGapWarningService`'tir.
- Mesaj `AddPatternContentGuard()` ve `AddContentGuard<T>()`'i adlandırır.
- Uygulama ayağa kalkar; uyarı bir hata değildir.

---

### MT-DIAG-056 — Aynı kurulum `Development`'ta SESSİZDİR — Faz 122

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 122 |
| **İlgili karar** | — |

**Ön koşul**
- MT-DIAG-055'in `Program.cs` değişikliği (guard çağrısı yorumda) yeniden
  uygulanır — bu kez geri ALINMADAN önce bu case de koşulur.

**Adımlar**
1. **Aynı** (guard'sız) kurulumla, yalnız ortamı değiştirerek başlat.

**Girilecek veri**
```bash
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz122-guard-dev.log 2>&1 &
sleep 14
grep -c "Application started" /tmp/faz122-guard-dev.log               # beklenen: 1
grep -ci "no IContentGuard registered" /tmp/faz122-guard-dev.log      # beklenen: 0
pkill -f Tracon.Api
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Uygulama kalkar ve **hiç uyarı düşmez** — tek değişken ortamdır.

---

### MT-DIAG-057 — `Production` + `Retention:Enabled=false` (varsayılan) başlangıçta TAM BİR uyarı düşürür — Faz 122

Örnek uygulama `appsettings.json`'da retention'ı hiç açmaz — bu case KOD
DEĞİŞİKLİĞİ gerektirmez, sadece varsayılan kurulumu Production'da başlatır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 122 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama değiştirilmemiş hâliyle derlendi.

**Adımlar**
1. Örnek uygulamayı `Production` ortamında, varsayılan yapılandırmayla başlat.
2. Kalkış log'unda uyarıyı say.

**Girilecek veri**
```bash
ASPNETCORE_ENVIRONMENT=Production \
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz122-retention-prod.log 2>&1 &
sleep 14
grep -ci "data retention disabled" /tmp/faz122-retention-prod.log     # beklenen: 1
grep -B1 "data retention disabled" /tmp/faz122-retention-prod.log | head -1
pkill -f Tracon.Api
```

**Beklenen sonuç**
- Uyarı **tam bir kez** düşer; satır `warn:` seviyesindedir ve kaynağı
  `Tracon.SilentGapWarningService`'tir.
- Mesaj `Tracon:Retention:Enabled` anahtarını ve `IRetentionPolicyStore`
  üzerinden elle politika kaydetme seçeneğini adlandırır.
- MT-DIAG-055'in uyarısıyla **aynı** log turunda birlikte görünebilir —
  ikisi de bağımsız kontroller, biri diğerini bastırmaz.

---

### MT-DIAG-058 — `RequireCustomBinding` çağrılmayan kurulum bugünkü gibi açılır — Faz 150

Zorunlu binding profili **kapalıdır**. Bu case fazın ilk DoD satırıdır: çağrı
yoksa hiçbir davranış değişmez ve doğrulayıcı hiçbir servisi çözmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama değiştirilmemiş hâliyle derlendi (`samples/Tracon.Api`
  hiçbir `RequireCustomBinding` çağrısı taşımaz).

**Adımlar**
1. Örnek uygulamayı başlat.
2. Kalkışın tamamlandığını ve hiçbir binding hatası düşmediğini doğrula.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz150-varsayilan.log 2>&1 &
sleep 14
grep -c "Application started" /tmp/faz150-varsayilan.log                  # beklenen: 1
grep -ci "required custom binding" /tmp/faz150-varsayilan.log             # beklenen: 0
pkill -f Tracon.Api
```

**Beklenen sonuç**
- Uygulama ayağa kalkar; `required custom binding` dizgesi log'da **hiç** geçmez.

---

### MT-DIAG-059 — Zorunlu ilan edilen sözleşme yerleşik varsayılanla çözülürse host BAŞLAMAZ — Faz 150

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Api/Program.cs` içinde `builder.AddTracon()`
  zincirine GEÇİCİ olarak `.RequireCustomBinding<IRunAuthorizationHandler>()`
  ekle. Örnek uygulama kendi `IRunAuthorizationHandler`'ını kaydetmez.

**Adımlar**
1. Değiştirilmiş örnek uygulamayı başlat.
2. Kalkışın **başarısız** olduğunu ve mesajın üç bilgiyi taşıdığını doğrula.
3. `Program.cs`'i geri al.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Api --no-launch-profile --urls http://localhost:5099 \
  > /tmp/faz150-eksik.log 2>&1
grep -c "IRunAuthorizationHandler" /tmp/faz150-eksik.log                  # beklenen: >=1
grep -c "AllowAllRunAuthorizationHandler" /tmp/faz150-eksik.log           # beklenen: >=1
grep -c "BEFORE the AddTracon() call" /tmp/faz150-eksik.log           # beklenen: >=1
grep -c "Application started" /tmp/faz150-eksik.log                       # beklenen: 0
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Süreç bir `InvalidOperationException` ile durur; `Application started` **hiç** yazılmaz.
- Mesaj üçünü de adlandırır: zorunlu sözleşme (`IRunAuthorizationHandler`),
  onun yerine çözülen tip (`AllowAllRunAuthorizationHandler`) ve düzeltme
  (`BEFORE the AddTracon() call`).

---

### MT-DIAG-060 — Tüketici kaydı `AddTracon`'den ÖNCE yapılırsa host açılır — Faz 150

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Embedded` değiştirilmemiş hâliyle derlendi. Bu örnek
  dört sözleşmeyi zorunlu ilan eder ve dördünü de `AddTracon()`'den ÖNCE
  kaydeder — case'in ölçtüğü tam olarak bu sıradır.

**Adımlar**
1. Gömme örneğini başlat.
2. Kalkışı ve dört zorunlu binding'in de kabul edildiğini doğrula.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Embedded --no-launch-profile --urls http://localhost:5098 \
  > /tmp/faz150-gomme.log 2>&1 &
sleep 14
grep -c "Application started" /tmp/faz150-gomme.log                       # beklenen: 1
grep -ci "required custom binding" /tmp/faz150-gomme.log                  # beklenen: 0
pkill -f Tracon.Embedded
```

**Beklenen sonuç**
- Uygulama ayağa kalkar; hiçbir binding hatası düşmez.

---

### MT-DIAG-061 — 🚨 `TryAdd` kaydı `AddTracon`'den SONRA yapılırsa host BAŞLAMAZ — Faz 150

Tüketicinin bildirdiği senaryo budur: modül sırası nedeniyle handler kaydı
`AddTracon`'den sonra koşar, yerleşik varsayılan slotu zaten tutmaktadır ve
`TryAdd` sessizce düşer.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Embedded/Program.cs` içinde
  `builder.Services.AddSingleton<IRunAuthorizationHandler, EmbeddedRunAuthorizationHandler>();`
  satırını GEÇİCİ olarak `AddTracon()` çağrısından **sonraya** taşı ve
  `AddSingleton` yerine `TryAddSingleton` yaz
  (`using Microsoft.Extensions.DependencyInjection.Extensions;` gerekir).

**Adımlar**
1. Değiştirilmiş gömme örneğini başlat.
2. Kalkışın başarısız olduğunu doğrula.
3. `Program.cs`'i geri al.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Embedded --no-launch-profile --urls http://localhost:5098 \
  > /tmp/faz150-gec-tryadd.log 2>&1
grep -c "AllowAllRunAuthorizationHandler" /tmp/faz150-gec-tryadd.log      # beklenen: >=1
grep -c "Application started" /tmp/faz150-gec-tryadd.log                  # beklenen: 0
git checkout -- samples/Tracon.Embedded/Program.cs
```

**Beklenen sonuç**
- Host başlamaz; mesaj yerleşik varsayılanı adlandırır.
- 🚨 Aynı satır `AddSingleton` ile (`TryAdd` olmadan) geç kaydedilirse host
  **açılır** — kap son kaydı çözer. Kapı olguyu bildirir, sırayı değil.

---

### MT-DIAG-062 — `IAttachmentStorage` YOKLUK dalı: hiç kayıt yoksa host BAŞLAMAZ — Faz 150

Diğer beş sözleşmede "varsayılan" bir tiptir; bu ikisinde bir **yokluktur**.
Tip karşılaştırmasıyla ölçen bir uygulama burada yanlış cevap verir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Embedded/Program.cs` içindeki
  `builder.Services.AddSingleton<IAttachmentStorage, InMemoryBufferAttachmentStorage>();`
  satırını GEÇİCİ olarak yorum satırına al. Zincirdeki
  `.RequireCustomBinding<IAttachmentStorage>()` çağrısı yerinde kalır.

**Adımlar**
1. Değiştirilmiş gömme örneğini başlat.
2. Kalkışın başarısız olduğunu doğrula.
3. `Program.cs`'i geri al.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Embedded --no-launch-profile --urls http://localhost:5098 \
  > /tmp/faz150-yokluk.log 2>&1
grep -c "IAttachmentStorage" /tmp/faz150-yokluk.log                       # beklenen: >=1
grep -c "nothing is registered" /tmp/faz150-yokluk.log                    # beklenen: >=1
grep -c "Application started" /tmp/faz150-yokluk.log                      # beklenen: 0
git checkout -- samples/Tracon.Embedded/Program.cs
```

**Beklenen sonuç**
- Host başlamaz. Mesaj `nothing is registered` der — bir tip adı **değil**,
  çünkü Tracon bu sözleşme için hiçbir şey kaydetmez.

---

### MT-DIAG-063 — Aynı sözleşme adaptörle kayıtlıysa host açılır (yokluk dalının karşıtı) — Faz 150

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- MT-DIAG-062'nin değişikliği geri alındı — `InMemoryBufferAttachmentStorage`
  yeniden kayıtlıdır.

**Adımlar**
1. Gömme örneğini başlat.
2. `/api/diagnostics` çıktısında `IAttachmentStorage`'ın kendi tipiyle
   göründüğünü doğrula.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Embedded --no-launch-profile --urls http://localhost:5098 \
  > /tmp/faz150-adaptor.log 2>&1 &
sleep 14
grep -c "Application started" /tmp/faz150-adaptor.log                     # beklenen: 1
pkill -f Tracon.Embedded
```

**Beklenen sonuç**
- Uygulama ayağa kalkar; yokluk dalı yalnız gerçek yoklukta tetiklenir.

---

### MT-DIAG-064 — Kontrol HTTP'siz host'ta da çalışır — Faz 150

Zorunlu binding bir HTTP kavramı değil, bir kompozisyon kavramıdır. Bu case
`MapTracon` çağırmayan bir host'ta kontrolün koştuğunu ölçer.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Embedded/Program.cs` içindeki `app.MapTracon(...)`
  çağrısını (ve varsa ona bağlı `options` bloğunu) GEÇİCİ olarak yorum satırına
  al. Ayrıca `IToolAuthorizationHandler` kaydını da yorum satırına al.

**Adımlar**
1. Değiştirilmiş gömme örneğini başlat.
2. Uç hiç map edilmemiş olmasına rağmen host'un başlamadığını doğrula.
3. `Program.cs`'i geri al.

**Girilecek veri**
```bash
dotnet run --project samples/Tracon.Embedded --no-launch-profile --urls http://localhost:5098 \
  > /tmp/faz150-httpsiz.log 2>&1
grep -c "AllowAllToolAuthorizationHandler" /tmp/faz150-httpsiz.log        # beklenen: >=1
grep -c "Application started" /tmp/faz150-httpsiz.log                     # beklenen: 0
git checkout -- samples/Tracon.Embedded/Program.cs
```

**Beklenen sonuç**
- Host başlamaz. Kontrol `MapTracon`'e bağlı değildir.

---

### MT-DIAG-065 — `/api/diagnostics` çıktısı DEĞİŞMEDİ (K-250) — Faz 150

Zorunluluk bir **niyettir**; teşhis raporu yalnız **olguyu** taşır. Bu faz
`extensionPoints` şemasına hiçbir alan eklemez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 150 |
| **İlgili karar** | K-250 |

**Ön koşul**
- MT-DIAG-050 kurulumu (gömme örneği ayakta, teşhis ucu açık).

**Adımlar**
1. Teşhis ucunu çağır.
2. `extensionPoints` girdi sayısını ve alan kümesini doğrula.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | jq '.extensionPoints | length'
# beklenen: 7
curl -s "$APU/api/diagnostics" -H "$APB" | jq -r '.extensionPoints[0] | keys | join(",")'
# beklenen: contract,implementation,isBuiltInDefault
```

**Beklenen sonuç**
- Tam **yedi** girdi.
- Her girdide yalnız üç alan: `contract`, `implementation`, `isBuiltInDefault`.
  Zorunluluğu bildiren yeni bir alan **yoktur**.

---
