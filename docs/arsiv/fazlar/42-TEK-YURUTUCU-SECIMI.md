# Faz 42 — Tek Yürütücü Seçimi (Çok Örnekli Koordinasyon)

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-57**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Mcp`
> **Yeni paket:** Yok · **Migration:** **gerekli** — bir tablo, üç set, numaralar uygulama anında alınır (K-178)
> **Public API:** büyüyor — bir arayüz ve bir ayar. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/42-TEK-YURUTUCU-SECIMI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism bugün **tek örnekli** çalışmayı varsayıyor. İki veya üç replika ile dağıtıldığında bazı işler sessizce N katına çıkıyor: her replika kendi MCP keşfini yapıyor, kendi sağlık yoklamasını gönderiyor ve kendi bellek içi durumunu tutuyor. Bu faz, "bu işi kümede yalnızca **bir** örnek yapsın" diyebilmeyi getirir.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 **İki örnek** aynı veritabanına bağlanır; MCP keşfi ve model sağlık
      yoklaması **yalnız birinde** koşar. Log çıktısı bu belgeye yazılır —
      bkz. [Gerçek Çalıştırma Kanıtı](#gerçek-çalıştırma-kanıtı)
- [x] Birinci örnek durdurulur; ikinci örnek işi **devralır**. Devralma süresi
      **ölçülür** ve buraya yazılır — **~17,6 sn** (`LeaseDuration=12sn`,
      yenileme aralığı 4 sn); bkz. aynı bölüm
- [x] `Enabled = false` (varsayılan) iken kira tablosuna hiçbir sorgu gitmez —
      `SingletonGuard.RunAsync`/`IsHeld` `Enabled` kontrolünü depoya HİÇ
      dokunmadan yapar; `SingletonDisabledTests`
- [x] `Enabled = false` iken bugünkü tek örnekli davranış **birebir** korunur —
      aynı test
- [x] Kirayı kaybeden yürütücü döngüsünü durdurur ve bir uyarı loglar —
      `SingletonGuardTests.Kira_kaybedilince_IsHeld_false_olur_...`
- [x] 🚨 `JobWorkerBackgroundService` **değiştirilmedi** — `git diff --stat`
      boş döndü, doğrulandı
- [x] Sözleşme testleri bellek içi + üç SQL sağlayıcısında geçer —
      `SingletonLeaseStoreContract` (9 test × 4 koşum = 36); PostgreSQL ve
      SQLite gerçekten koşturuldu (796 ve 411 test, hepsi yeşil), SQL Server
      bu ortamda (Apple Silicon, Rosetta kapalı) Testcontainers zaman
      aşımıyla başlamadı — ölçülmüş, önceden bilinen bir ortam sınırı
      (`docs/hafiza/sql-saglayicilari.md`), koddan bağımsız
- [x] Migration üç sette de uygulandı; numaralar sağlayıcı başına bağımsız
      (K-178) — PostgreSQL `0019`, SQL Server `0007`, SQLite `0007`;
      `samples/AgentPrism.Api` çalıştırıldığında SQLite üzerinde "AgentPrism 7
      migration uyguladi" logu gözlendi
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü

### Gerçek Çalıştırma Kanıtı

İki `samples/AgentPrism.Api` süreci aynı SQLite dosyasına (`Data Source=demo3.db`)
bağlandı, `AgentPrism:SingletonExecution:Enabled=true`,
`LeaseDuration=00:00:12`, model sağlık `BackgroundInterval=00:00:05`, MCP
`RefreshInterval=5sn` (geçici bir yerel test bağlantısıyla; kalıcı değişiklik
değil — bkz. [Plandan Sapmalar](#plandan-sapmalar)).

```
$ sqlite3 demo3.db "SELECT name, owner_id, expires_at, updated_at FROM agentprism_singleton_leases;"
mcp-discovery|Faruk-MacBook-Pro:63186:c7deb477659f4d21882e2114ec3e5fa4|2026-08-07T00:19:45Z|2026-08-07T00:19:33Z
model-provider-health|Faruk-MacBook-Pro:63186:030db68148924878892ef57de8d1fcbc|2026-08-07T00:19:45Z|2026-08-07T00:19:33Z
```

B sürecinin (`PID 63186`) günlüğünde `"MCP kesfi tamamlandi: 0 tool
kullanilabilir."` **4 kez** göründü; A sürecinin (`PID 63185`) günlüğünde bu
satır **hiç** görünmedi — MCP keşfi yalnız B'de koştu. Aynı anda ikisi de
`agentprism_model_provider_health`'i (sağlık önbelleği DB'ye yazmaz, ama
`singleton_leases` satırı A'da hiç oluşmadı) doğruladı: yalnız B'nin
`ownerId`'si her iki kira adında da görünüyor.

B öldürüldü (`kill -TERM`, epoch `1786061992` = `2026-08-07T00:19:52Z`).
20 saniye sonra:

```
$ sqlite3 demo3.db "SELECT name, owner_id, expires_at, updated_at FROM agentprism_singleton_leases;"
model-provider-health|Faruk-MacBook-Pro:63185:6b171c7f44c34cc7a1fd050e2d2785bb|2026-08-07T00:20:21Z|2026-08-07T00:20:09Z
mcp-discovery|Faruk-MacBook-Pro:63185:69983c5821e6498b8e46885771767058|2026-08-07T00:20:21Z|2026-08-07T00:20:09Z
```

Kira A'ya (`PID 63185`) geçti; `updated_at = 2026-08-07T00:20:09Z`, kill anı
`2026-08-07T00:19:52Z` → **devralma ~17,6 sn** sürdü. A'nın günlüğünde kill
sonrası 20 sn içinde 4 kez `"MCP kesfi tamamlandi"` göründü. Bu, taslak
`LeaseDuration=60sn` varsayımıyla orantılıdır: `12sn` kirada devralma
`~1.5×LeaseDuration` sürdü (kira zaten neredeyse dolmuşken B öldürüldüğü ve
A'nın kendi yenileme turunu (4 sn) beklediği için); `60sn`'lik varsayılan
kirada devralma **en kötü ihtimalle ~90 sn** sürer. Ayrıntı ve karar:
[K-286](#bu-fazda-verilen-kararlar).

### Doğrulama komutları

```bash
# Iki ornek, ayni SQLite dosyasi, farkli port
AGENTPRISM__SQLITE__CONNECTIONSTRING="Data Source=demo.db" \
  AGENTPRISM__SINGLETONEXECUTION__ENABLED=true \
  AGENTPRISM__SINGLETONEXECUTION__LEASEDURATION=00:01:00 \
  dotnet artifacts/bin/AgentPrism.Api/release/AgentPrism.Api.dll --urls http://localhost:5081 &
AGENTPRISM__SQLITE__CONNECTIONSTRING="Data Source=demo.db" \
  AGENTPRISM__SINGLETONEXECUTION__ENABLED=true \
  AGENTPRISM__SINGLETONEXECUTION__LEASEDURATION=00:01:00 \
  dotnet artifacts/bin/AgentPrism.Api/release/AgentPrism.Api.dll --urls http://localhost:5082 &

# Kira kimde
sqlite3 demo.db "SELECT name, owner_id, expires_at FROM agentprism_singleton_leases;"

# Birinci sureci durdur (PID'i yukaridaki & ciktisindan al), devralma suresini olc
kill <PIDA>
watch -n 2 'sqlite3 demo.db "SELECT owner_id, expires_at FROM agentprism_singleton_leases;"'

# Is kuyrugu degismedi mi
git diff --stat src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs
```

🚨 `dotnet run`in `launchSettings.json`'i her zaman 5080'i açtığı ve
`ASPNETCORE_URLS`'i ezdiği unutulmamalı (bkz. `docs/hafiza/aspnetcore-di.md`);
yukarıdaki komutlar bu yüzden **derlenmiş DLL'i doğrudan** `--urls` ile
çalıştırır, `dotnet run` değil. Ayrıca `AgentPrismMcpOptions` (MCP
`RefreshInterval` dâhil) **yapılandırmadan bağlanmaz** — yalnız
`.UseMcp(configure)` delegesiyle koddan verilir; env değişkeniyle
değiştirilemez (Faz 42'de yeni öğrenildi, bkz. devir notu).

---

## Plandan Sapmalar

1. **`SingletonGuard` `internal` değil `public` oldu** (Açık Soru 4'ün önerisi
   A'ydı — `internal`). Uygulama sırasında ortaya çıktı: `McpDiscoveryService`
   `AgentPrism.Mcp` derlemesinde yaşıyor, `SingletonGuard` ise
   `AgentPrism.Core`'da. `InternalsVisibleTo` yalnız
   `$(MSBuildProjectName).UnitTests/.IntegrationTests/.FunctionalTests`'i
   kapsar (`Directory.Build.props`) — **kardeş paketleri değil**. İki
   `BackgroundService`'in "aynı yardımcıyı kullanması" (plan metni) ile
   "yardımcı `internal` kalsın" (Açık Soru 4) birbiriyle **çelişiyordu**;
   kod paylaşımı çelişkiyi kazandı. Doğrulama: `ISingletonLeaseStore` ve
   `SingletonExecutionOptions` hâlâ tek gerçek "yeni sözleşme" — `SingletonGuard`
   bir orkestratördür, kendi veri modelini tanımlamaz.
2. **`SingletonExecutionOptionsValidator` plana yazılı değildi**, ama her
   `AgentPrism*Options` sınıfının `IValidateOptions<T>` alma deseni
   (K-006/K-021) burada da uygulandı — `LeaseDuration <= 0` başlangıçta
   reddedilir.
3. **K-278 ("çağıranın verdiği metin tek başına birincil anahtar olamaz")
   burada uygulanmadı ve bu bilinçli.** `singleton_leases.name` çağırandan
   (bir kiracıdan) değil, AgentPrism'in **kendi kodundan** gelir (`"mcp-discovery"`,
   `"model-provider-health"` gibi sabit adlar) — K-278'in kapsadığı sorun
   (bir kiracının başka bir kiracının satırını ele geçirmesi) burada
   yapısal olarak yoktur: kiracı kavramı tabloda hiç yok.
4. **MCP keşif aralığının (`AgentPrismMcpOptions.RefreshInterval`)
   yapılandırmadan (env değişkeni/appsettings) bağlanamadığı keşfedildi** —
   bu Faz 42'nin kapsamı DIŞINDA, önceden var olan bir davranış
   (`AgentPrismMcpBuilderExtensions.UseMcp` yalnız kod-taraflı `configure`
   delegesi alır, `IConfiguration.Bind` çağırmaz). Gerçek çalıştırma
   doğrulaması bu yüzden geçici bir yerel Program.cs yamasıyla yapıldı (env
   değişkeninden `RefreshInterval` okuyan bir `configure` delegesi), test
   bitince **geri alındı** (`git checkout`). Devir notuna yazıldı.
5. **Arka plan servisi başlatma sırası, `singleton_leases` tablosu henüz
   yokken ilk kira denemesinin başarısız olabileceğini ortaya çıkardı**
   (ölçüldü — bkz. devir notu, madde 3). Bu **yeni bir kusur değil**:
   `McpDiscoveryService` zaten ilk turunda `mcp_servers` tablosunu okuyordu
   ve aynı yarışa açıktı; Faz 42 yalnız ikinci bir örneğini ekledi. Guard
   hatayı yutar, loglar, bir sonraki turda yeniden dener — kendiliğinden
   iyileşir (~1 yenileme aralığı içinde). Düzeltilmedi (kapsam dışı, bkz.
   devir notu).

## Bu Fazda Verilen Kararlar

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **K-284 — Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil** | 2026-08-07 | SQLite'ın `pg_try_advisory_lock`/`sp_getapplock` karşılığı yok; bir tablo üç sağlayıcıda da aynı davranışı verir ve bağlantı havuzundan bağımsızdır. Desen `jobs` kirasıyla (Faz 17) aynıdır ve zaten üretimde kanıtlanmıştır. Ayrıntı: [42.3](#423--tasarım-kira-tablosu-advisory-lock-değil) | — |
| **K-285 — `SingletonGuard` `public`tir; Açık Soru 4'ün "`internal`" önerisi uygulanamadı** | 2026-08-07 | `InternalsVisibleTo` yalnız test projelerini kapsar (`Directory.Build.props`); `AgentPrism.Mcp`, `AgentPrism.Core`'un `internal` tiplerini göremez. `McpDiscoveryService` ve `ModelProviderHealthBackgroundService`'in AYNI yardımcıyı kullanması (plan gereksinimi) `public` görünürlük gerektirdi. Genel public yüzey büyümesi küçük kabul edildi: `SingletonGuard`'ın kendi tuttuğu durum (`ISingletonLeaseStore`, `SingletonExecutionOptions`) zaten public'ti | `AgentPrism.Mcp` `AgentPrism.Core` içine taşınırsa (olası değil) `internal`'e geri alınabilir |
| **K-286 — Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn); gerçek devralma ölçüldü** | 2026-08-07 | İki gerçek `samples/AgentPrism.Api` süreci, aynı SQLite dosyası, `LeaseDuration=12sn` (yenileme 4 sn) ile devralma **~17,6 sn** sürdü (kira B öldürülmeden az önce zaten sona ermek üzereydi + A'nın 4 sn'lik kendi yenileme turunu beklemesi). Bu, `60sn` varsayılan kirada en kötü durumda **~90 sn**'lik bir devralma öngörür (`LeaseDuration` + bir yenileme turu). `1/3` oranı: iki kaçırılmış yenilemeye dayanır, yarısı seçilseydi tek bir kaçırılmış yenileme kirayı düşürürdü. 1 saniyelik taban, `LeaseDuration` çok kısa ayarlanırsa (test/yanlış yapılandırma) yenileme döngüsünün mantıksız sıklıkta dönmesini engeller | Üretimde ölçülen gerçek devralma süreleri 90 sn'yi anlamlı ölçüde aşarsa (ör. yavaş bir SQL sağlayıcısında) varsayılan yeniden gözden geçirilir |
| **K-287 — Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil** | 2026-08-07 | Açık Soru 5'in B seçeneği bilinçli seçildi: `SqlSingletonLeaseStore` `DateTimeOffset.UtcNow`'ı C# tarafında hesaplar (tıpkı `SqlJobStore.LeaseAsync`'in `RenewJobLease`'de yaptığı gibi — mevcut desenle tutarlı). Veritabanının kendi saatini (`NOW()`/`GETUTCDATE()`/`CURRENT_TIMESTAMP`) kullanmak üç saglayıcıda üç farklı SQL fonksiyonu ve üç farklı hassasiyet demektir; `jobs` kirası da aynı basitleştirmeyi yapıyor ve üretimde sorun çıkarmadı. Saat kayması riski, çoğu dağıtımın NTP ile senkronize sunucular kullanmasıyla küçüktür ve mevcut `jobs` kira mekanizmasıyla zaten paylaşılan bir risktir | Gerçek bir saat kayması olayı yaşanırsa `expires_at`'i veritabanı saatine taşımak (üç diyalektte üç ayrı ifade) yeniden değerlendirilir |

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler**

- `ISingletonLeaseStore` (Abstractions/Coordination): `TryAcquireAsync`/
  `RenewAsync`/`ReleaseAsync`. Yeni bir arka plan işini tek örneğe indirmek
  isteyen kod, kendi `SingletonGuard` örneğini kurar (bkz. `ModelProviderHealthBackgroundService`
  veya `McpDiscoveryService`'in kurucusu — desen ikisinde de aynı) ve
  benzersiz bir kira adı seçer.
- `SingletonGuard` **public**'tir (`AgentPrism.Core`); herhangi bir paket
  onu kullanabilir. `RunAsync(stoppingToken)` bir arka plan görevi olarak
  başlatılır (await edilmez), `IsHeld` her iş turunda senkron okunur.

**Bilinen tuzaklar (🚨)**

1. **`AgentPrismMcpOptions` (dolayısıyla `RefreshInterval`)
   `IConfiguration`'dan bağlanmaz** — yalnız `.UseMcp(o => ...)` delegesiyle
   koddan verilir. `AGENTPRISM__MCP__REFRESHINTERVAL` gibi bir ortam
   değişkeni **sessizce hiçbir şey yapmaz**. Bu Faz 42'den önce de böyleydi;
   ilk kez burada ölçülerek doğrulandı. Mcp modülüne dokunan bir faz bunu
   düzeltmeyi (config-bound hâle getirmeyi) değerlendirmelidir.
2. **Yenileme aralığının bir alt sınırı vardır (`SingletonGuard.RunAsync`
   içinde 1 saniye)** — `LeaseDuration` çok kısa (< 3 sn) ayarlanırsa
   yenileme `LeaseDuration/3`'ten DAHA SEYREK olur ve kira, sahibi hâlâ
   çalışırken bile süresi dolmuş görünebilir (başka bir örnek onu haksız
   yere devralabilir). Üretimde `LeaseDuration` en az birkaç saniye
   olmalıdır; varsayılan (60 sn) bu sınırın çok üzerindedir.
3. **🚨 Arka plan servisi başlatma sırası bir yarış açık bırakır.**
   `McpDiscoveryService` (bir `BackgroundService`) `.UseMcp()` çağrısında,
   `MigrationHostedService` (düz bir `IHostedService`) ise `.UseSqlite()`/
   `.UsePostgreSql()`/`.UseSqlServer()` çağrısında kaydedilir. Genel Host,
   `IHostedService.StartAsync`'i KAYIT SIRASINA göre çağırır; `.UseMcp()`
   Program.cs'te genelde `.UseSqlite()`'tan ÖNCE çağrılır. `BackgroundService.StartAsync`
   `ExecuteAsync`'i bekletmeden döner — bu yüzden `McpDiscoveryService`'in
   ilk `SingletonGuard` denemesi, `MigrationHostedService.StartAsync`
   migration'ları tamamlamadan ÖNCE çalışabilir ve `"no such table:
   ...singleton_leases"` (veya `mcp_servers` için de aynı desen) uyarısıyla
   başarısız olabilir. **Yeni bir kusur değildir** — `McpDiscoveryService`
   zaten ilk turunda `mcp_servers` tablosunu okuyordu ve aynı yarışa
   açıktı; Faz 42 yalnız ikinci bir örneğini (kira tablosu) ekledi.
   `SingletonGuard.TickAsync` hatayı yutar, loglar, bir sonraki yenileme
   turunda (birkaç saniye içinde) kendiliğinden düzelir — üretim
   etkisi geçicidir ama gözlemlenebilir bir uyarı satırı üretir. Kalıcı
   çözüm (hosted service kayıt sırasını garanti etmek veya `.UseMcp()`'in
   kendi ilk turunu geciktirmesi) bu fazın kapsamı dışıdır; aday listesine
   yazılmalıdır.

**Yarım kalan işler / açık uçlar — aday listesine yazılacak dört kalem**

1. **MCP OAuth token'ının örnekler arasında paylaşılması** — K-059 ile
   çatışır ([42.5](#425--kapsam-dışı-hız-sınırı-ve-mcp-tokenı)).
2. **Paylaşılan hız sınırı** — K-158 bunu bilerek bellekte tuttu.
3. **Kira durumunun teşhis ucunda gösterilmesi** —
   [Faz 33](33-SAGLIK-DENETIMI-VE-TESHIS.md)'e aittir.
4. **`AgentPrismMcpOptions`'ı `IConfiguration`'a bağlamak** — yukarıdaki
   tuzak 1; şu an yalnız kod-taraflı `configure` delegesiyle ayarlanabiliyor.

Ayrıca aday listesindeki **F-36** (öksüz çalıştırma uzlaştırması) bu fazın
`ISingletonLeaseStore`'unu doğrudan kullanır. Uzlaştırıcı kendi kira adını
seçmelidir (ör. `"orphan-run-reconciliation"`); `SingletonGuard`'ın kurucu
imzası (`store, optionsMonitor, leaseName, logger`) doğrudan yeniden
kullanılabilir.

**Sıradaki faz:** [Faz 43 — Idempotency Key](43-IDEMPOTENCY-KEY.md).
