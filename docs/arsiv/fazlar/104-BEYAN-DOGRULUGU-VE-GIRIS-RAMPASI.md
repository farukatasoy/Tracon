# Faz 104 — Beyan Doğruluğu ve Giriş Rampası

> **Durum:** ✅ Tamamlandı (2026-08-25)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 14** (Blok C'den öne çekildi, kullanıcı kararı 👤) · **kalem 12** ve **kalem 23** (Blok B). `ADAYLAR.md`'de F-NN karşılığı yoktur; bu kalemler keşif turundan gelir
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Core` (uyarı servisi + kalıcılık yargısının tek kaynağı) · `AgentPrism.AspNetCore` (yalnız `/api/meta` o tek kaynağa bağlanır)
> **Yeni paket:** Yok — `Microsoft.Extensions.Hosting.Abstractions` `Core`'un mevcut bağımlılığıdır ([`AgentPrism.Core.csproj:93`](../../../src/AgentPrism.Core/AgentPrism.Core.csproj)) · **Migration:** Yok
> **Public API:** **Büyümüyor.** Uyarı servisi `internal`, kalıcılık yardımcısı `internal`. Değişen tek şey mevcut bir tipin XML doküman metnidir. `EnablePublicApiTracking` açıktır (K-421) ve `PublicAPI.Shipped.txt` dosyalarının hepsi boştur — uygulayan oturum bunu `wc -l src/*/PublicAPI.Shipped.txt` ile ölçüp doğrular
> **Tüketici yüzeyi:** site — [`guides/production.md`](../../../docs-site/src/content/docs/guides/production.md) (ölçekleme bölümüne hız sınırı kapsamı), [`concepts/governance.md`](../../../docs-site/src/content/docs/concepts/governance.md) (kiracı yalıtımının nerede zorlandığı), `api/AgentPrism.AgentPrismRateLimitOptions.md` (**üretilir** — iş XML dokümanındadır) · sevk edilen — `AgentPrismRateLimitOptions` XML dokümanı. Kök `CONTRIBUTING.md` ve `ARCHITECTURE.md` **pakete girmez**, depoya gelen katkıcıya dönüktür
> **Manuel test alanı:** [`manuel-test/25-SAGLIK-TESHIS-OPENAPI.md`](../../manuel-test/25-SAGLIK-TESHIS-OPENAPI.md) (§104.3 uyarısı) · [`manuel-test/31-DOKUMAN-DOGRULUGU.md`](../../manuel-test/31-DOKUMAN-DOGRULUGU.md) (§104.1 · §104.2 · §104.4 beyanları)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 401e52b:docs/arsiv/fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md
> ```
>
> Damıtıldı 2026-08-25 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz **kod yeteneği eklemez**. Üç yerde, sistemin kendisi hakkında söylediği ile gerçeği arasındaki farkı kapatır: kiracı yalıtımının **hangi katmanda** durduğu bir karar olarak kaydedilir, hız sınırının çok örnekli kurulumdaki kapsamı eksiksiz beyan edilir, ve `Production` ortamında kalıcı olmayan bir store ile kalkan kurulum bunu **sunucu tarafında** söyler.

## Bitiş Ölçütleri (DoD)

- [x] ✅ `Production` + varsayılan store ile kalkışta **tam bir** uyarı düşer;
  `Development`'ta ve Sqlite bağlıyken **düşmez**. Üç koşum, `samples/AgentPrism.Api`:

  | Ortam | Store | Uyarı sayısı | `/api/meta` `storage.persistent` |
  |---|---|---:|---|
  | `Production` | bellek içi | **1** | `false` |
  | `Development` | bellek içi | **0** | `false` |
  | `Production` | Sqlite | **0** | `true` (`runStore = SqlRunStore`) |

  Uyarının gerçek çıktısı (`warn:` seviyesi, kaynak
  `AgentPrism.NonPersistentStorageWarningService`):
  > `AgentPrism is running in the Production environment with storage that is not persistent: agent definitions, runs, sessions. The data lives as long as this process does; a restart loses it and a second instance does not see it. Register a persistence package to keep it - for example UsePostgreSql(connectionString), UseSqlServer(...) or UseSqlite(...). In-memory storage is a supported mode; this message reports the environment, it does not report a broken setup.`

- [x] ✅ Kalıcılık yargısı tek kaynakta: `grep -rn "InMemoryRunStore\|InMemorySessionStore\|InMemoryAgentDefinitionStore" src/AgentPrism.AspNetCore/` → **sıfır satır** (DoD "yalnız yorum" bekliyordu; sonuç daha güçlü)
- [x] ✅ RLS kararı **K-623**; `MIMARI-GUVENLIK.md` §Çok kiracılılık ona link veriyor; `KARARLAR-INDEKS.md` yeniden üretildi
- [x] ✅ `AgentPrismRateLimitOptions` XML'i çok örnekli kapsamı yazıyor; `api/AgentPrism.AgentPrismRateLimitOptions.md` yeniden üretildi ve "per instance" satırını taşıyor
- [x] ✅ `guides/production.md` · `concepts/governance.md` · (denetim bulgusu 3) `getting-started/persistence.md` güncellendi. `check:content` **temiz** (46 elle yazılan · 1028 toplam sayfa). ⚠️ `npm run build` **koşulamadı** — `docfx metadata` `CS1704` veriyor ve bu kaynaktan bağımsız bir makine sorunudur (devir notu)
- [x] ✅ `CONTRIBUTING.md` ve `ARCHITECTURE.md` kökte, İngilizce; `SourceLanguageTests` üç kök dosyayı da tarıyor ve **taban çizgisi boş kaldı**. Kapının gerçekten koştuğu kanıtlandı: `CONTRIBUTING.md`'ye Türkçe bir satır eklenince test `+ CONTRIBUTING.md: 1 offending lines` ile düştü, geri alınınca geçti
- [x] ✅ İki bütçe girdisi eklendi; sınırlar **ölçülen** son boyuta göre kondu (`CONTRIBUTING.md` 6 624/7 800 · `ARCHITECTURE.md` 7 484/8 900, ikisi de %15+ boşluk). `dokuman-bakim.py --denetle` çıkış kodu **0**
- [x] ✅ Public yüzey büyümedi — `git diff` `PublicAPI.*.txt` dosyalarında **sıfır** satır
- [x] ⚠️ **Dört kapı — üçü yeşil, biri kırılgan.** `build` ✅ (0 uyarı) · `pack` ✅ (çıkış 0, 20 paket) · `format` ✅ (çıkış 0 — **Faz 103'ten kalma** bir import sırası kırmızısı düzeltildikten sonra) · `test`: tam koşum **kırılgan**. Bir tam koşum uçtan uca **yeşil** geçti (çıkış 0); diğerlerinde her seferinde **farklı** bir test düştü ve aynı desen **değiştirilmemiş tabanda da** ölçüldü. Fazın dokunduğu dört proje tek tek yeşil: `Core.UnitTests` 1948 · `AspNetCore.FunctionalTests` 670 · `Mcp.UnitTests` 31 · `Ui.E2ETests` 57
- [x] ✅ `samples/AgentPrism.Api` ile gerçek `run` yapıldı: `POST /api/agents/support/run` SSE ile akıttı, üç `run` kaydı `Completed` olarak listelendi
- [x] ✅ `secret` taraması temiz (`kapi.py tarama`)
- [x] ✅ Manuel case'ler eklendi: `25-SAGLIK-TESHIS-OPENAPI.md` **MT-DIAG-052..054** (üçü de koşuldu) · `31-DOKUMAN-DOGRULUGU.md` **MT-DDG-030..032**
- [x] ✅ `faz-denetim` koşuldu; **2 🔴 + 5 🟡** bulgu üretti, **hepsi kapandı**; 🔴 kalmadı

### Doğrulama komutları

```bash
# Uyari yalniz Production'da ve yalniz kalici olmayan store ile duser
ASPNETCORE_ENVIRONMENT=Production dotnet run --project samples/AgentPrism.Api 2>&1 | grep -i "not persistent"
ASPNETCORE_ENVIRONMENT=Development dotnet run --project samples/AgentPrism.Api 2>&1 | grep -ci "not persistent"   # 0

# Uc ile log ayni yargiyi verir
curl -s http://localhost:5081/agentprism/api/meta | python3 -c "import sys,json; print(json.load(sys.stdin)['storage']['persistent'])"

# Kopya ifade kalmadi
grep -n "InMemoryRunStore\|InMemorySessionStore" src/AgentPrism.AspNetCore/Endpoints/MetaEndpoints.cs

# Public yuzey buyumedi
git diff --stat -- 'src/*/PublicAPI.Unshipped.txt'
wc -l src/*/PublicAPI.Shipped.txt

# Butce ve kapilar
python3 scripts/dokuman-bakim.py --denetle
python3 scripts/kapi.py kapanis --taban <faz oncesi commit>
```

---

## Plandan Sapmalar

**1. Kalem 14'ün yarısı zaten yapılmıştı — kapsam plan turunda daraldı.**
Envanter "hız sınırı bellekte, beyan yok" diyordu. Ölçüm bunun **yarısını**
düşürdü: `InboundTriggerRateLimiter.cs:8-13` "PER INSTANCE" yazıyor ve
`guides/inbound-triggers.md:156-160` bir `caution` bloğu taşıyor. Faza yalnız
iki gerçek boşluk girdi (`AgentPrismRateLimitOptions` XML'i ve
`guides/production.md`) ve RLS kararı — o hiçbir yerde kayıtlı değildi.

**2. Kalem 12 de daraldı.** Arayüz zaten dürüsttü (`settings.inMemoryNotice`).
Eksik olan yalnız sunucu tarafı sinyaliydi.

**3. Üç kalem tek fazda birleşti.** `faz-planlama` iki kalemi sınır sayar. Sapma
bilinçliydi; ortak eksen "kurulum ve depo kendi gerçeğini söyler" ve üç kalemin
DoD'si birbirinden bağımsız ölçüldü.

**4. 🚨 Kayıt biçimi plandan saptı: açık fabrika zorunlu çıktı.** Plan
`TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, T>())` varsayıyordu.
Kapılar bunu **kırmızıyla** yakaladı: `IHostEnvironment`'ı yalnız bir host
kaydeder, ve `AddAgentPrism()` çıplak bir `ServiceCollection` üzerinde de
geçerlidir. Kurucu çözümlemesiyle kayıt, host'suz her tüketicide
`GetServices<IHostedService>()` çağrısını patlatıyordu — üç mevcut test sınıfı
düştü. Çözüm `AgentPrismDrainService`'in emsalidir: opsiyonel bağımlılık +
açık fabrika. Bu, `docs/hafiza/aspnetcore-di.md`'deki kayıtlı tuzağın aynısıdır.

**5. Faz dışı iki düzeltme yapıldı.**
[`ModelRunJudgeTests.cs`](../../../arsiv/fazlar/103-EXTENSION-SOZLESMELERININ-YAYIN-ONCESI-SERTLESTIRILMESI.md)
`using System.Reflection;`'ı son sıraya koyuyordu ve `dotnet format` kapısı
**Faz 103'ün commit'inden (`1d9b9bd`) beri kırmızıydı**. Tek satırlık import
sırası düzeltildi. İkincisi: `MetaEndpoints`'ten `Unwrap` kaldırılınca öksüz
kalan XML yorumu silindi.

**6. Site yayın adımı (Adım 10) koşulmadı.** İki sebep: `docfx metadata` bu
makinede `CS1704` veriyor (aşağıda, devir notunda) ve yayın geri alınamaz bir
dış eylemdir — kullanıcı kararına bırakıldı (👤).

**7. `--site-gerekce-yazildi` gerekçesi.** `dokuman-bakim.py --site-denetle`
`MetaEndpoints.cs` değiştiği için `http-api.md`'yi bekliyor. **HTTP sözleşmesi
değişmedi:** `/api/meta` aynı alanları aynı anlamla döndürür; değişen tek şey
`storage.persistent` yargısının nereden okunduğudur (`StorePersistence`).
Uç için üretilen sayfa ve OpenAPI belgesi bit düzeyinde aynıdır.

## Bu Fazda Verilen Kararlar

| Karar | Özet |
|---|---|
| **K-623** 👤 | Kiracı yalıtımı uygulama katmanında tek hat kalır; veritabanı RLS'i eklenmez. Üç gerekçe: `TenantCoverageTests` kapısı zaten var · SQLite'ta RLS yok (üç sağlayıcı ayrışır) · gerçek `mssql/server` yerelde koşturulamıyor (K-186, K-317). Yeniden açılma koşulu kayıtlı |
| **K-624** 👤 | `Production` + kalıcı olmayan store **uyarır**, hata fırlatmaz, susturma seçeneği yoktur. `AgentPrismDiagnosticsReport`'a alan bilinçli olarak **eklenmedi** (`required` alan yayından sonra kırıcıdır) |

## Denetim Bulguları

Denetçi: bağımsız `general-purpose` agent, taze bağlam, yalnız DoD + diff.
**Temiz çıkan başlıklar:** 3.1 (DoD) · 3.3 (test seviyesi) · 3.5 (imza-gövde) ·
3.6 (plan dışı public API) · 3.7 (repo kuralları).

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `ARCHITECTURE.md` "19 packable proje" diyordu; gerçek **20** (21 `csproj` − `Generators`). "19" Faz 97'den kalma bayat sayıydı — Faz 98 `Testing.Contracts.Xunit`'i ekledi | **Düzeltildi.** `ls src/*/*.csproj` ve 20 `.nupkg` ile doğrulandı. Beyan doğruluğu fazının kendi ilk teknik iddiası yanlıştı — bulgu haklı |
| 2 | 🔴 | `ARCHITECTURE.md` diyagramı (`HTTP --> UI`, `HTTP --> PROV`) altındaki paragrafın tersini çiziyordu | **Düzeltildi.** Diyagram artık **derleme-zamanı** yönünü çizer; çalışma anı ilişkisi ayrı bir paragrafta anlatılır |
| 3 | 🟡 | Yeni `Production` uyarısı tüketiciye dönük bir davranış ama `docs-site`'ta karşılığı yoktu | **Düzeltildi.** `getting-started/persistence.md`'ye `caution` bloğu eklendi; K-624'ün "susturulamaz" kararı da orada söylenir |
| 4 | 🟡 | `CONTRIBUTING.md` `/api/diagnostics` çıktısını istiyordu; uç **varsayılan kapalı** ve `Admin` politikası ister | **Düzeltildi** |
| 5 | 🟡 | `StartAsync` korumasızdı: `IAuditDecorated.AuditedInner` **public**'tir, fırlatan bir uygulama host'un kalkışını düşürürdü — sınıfın kendi XML'i "asla hata fırlatmaz" diyordu | **Düzeltildi.** `try/catch` + `LogDebug`; `A_decorator_that_throws_does_not_stop_the_host` testi önce **kırmızı** koştu, sonra yeşil |
| 6 | 🟡 | Log **seviyesi** ölçülmüyordu; `AllText.ShouldContain("Warning")` başka bir satırla da yeşil kalırdı | **Düzeltildi.** Eşleşen tek girdi üzerinde `ShouldStartWith("Warning AgentPrism.NonPersistentStorageWarningService")` |
| 7 | 🟡 | `Without_a_host_environment_the_service_stays_silent` hiçbir iddia taşımıyordu — yalnız "patlamadı"yı kanıtlıyordu | **Düzeltildi.** Kaydeden logger provider takıldı; `logs.Entries.ShouldBeEmpty()` |
| 8 | 🟢 | `CONTRIBUTING.md` `kapanis`'i `--taban` olmadan gösteriyordu (argparse hatası verir) | **Düzeltildi** — tek kelime |
| 9 | 🟢 | `AuditedInner` `null` dönerse `/api/meta` `NullReferenceException` verir | **Devredildi.** Fazdan önce vardı, davranış aynen taşındı |
| 10 | 🟢 | `MetaEndpoints` üç store'u `Unwrap` ediyor, `IsPersistent` aynısını tekrar yapıyor | **Gerekçelendi.** Ölçülebilir maliyeti yok; tek kaynak kuralı sağlandı |

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `StorePersistence` (internal, `Core`) kalıcılık yargısının **tek kaynağıdır**.
  Yeni bir kalıcı olmayan store eklenirse **yalnız orası** güncellenir;
  `/api/meta` ve başlangıç uyarısı ikisi de oradan okur. Kanıt:
  `grep -rn "InMemoryRunStore" src/AgentPrism.AspNetCore/` → **sıfır**.
- `NonPersistentStorageWarningService` **asla fırlatmaz** ve `Production`
  dışında hiçbir şey yapmaz. `IHostEnvironment` yoksa da sessizdir.
- K-623 gereği kiracı yalıtımı uygulama katmanındadır; bir sonraki güvenlik
  fazı RLS varsayamaz.

**🚨 Bilinen tuzaklar:**
- 🚨 **`Core` içinde host-only bir servise bağımlı `IHostedService` yazma.**
  `IHostEnvironment` ve `IHostApplicationLifetime` yalnız bir host'ta kayıtlıdır;
  `AddAgentPrism()` çıplak `ServiceCollection` üzerinde de geçerlidir. Parametreyi
  nullable yapmak **yetmez** — kayıt **açık fabrika** ile yapılmalıdır
  (`provider.GetService<IHostEnvironment>()`). Sınıf taraması yapıldı:
  `Core`'da bu deseni taşıyan yalnız iki servis var ve ikisi de artık doğru
  (`AgentPrismDrainService`, `NonPersistentStorageWarningService`).
- 🚨 **`docs-site` API referansı bu makinede kırık.** `docfx metadata` **360**
  `CS1704` veriyor ve hata **kaynaktan bağımsızdır** (`git stash` ile taban
  kaynağında birebir aynı). `references.exclude`'a test ve örnek host dizinleri
  eklemek sayıyı **değiştirmedi**. Aynı oturumda iki kez de 0 hatayla koştu, ama
  koşul izole edilemedi. Bir sonraki oturum bunu bir **kusur kalemi** olarak ele
  almalı; ayrıntı `docs/hafiza/dokumantasyon.md`.
- 🚨 **Tam test koşumu kırılgan ve bu ölçüldü.** `UiTests.Runs_button_on_session_page_navigates_to_filtered_list`
  fazın değişiklikleri **rafa alınıp taban kaynağı derlendiğinde de** düştü.
  Ölçüm yolu `docs/hafiza/test-altyapisi.md`'ye yazıldı: `git stash push -u` →
  tam koşum → `git stash pop`. Worktree denemesi **işe yaramaz** (extension
  sample'ları yerel NuGet feed'i ister).
- 🚨 **`dotnet format` kapısı Faz 103'ten beri kırmızıydı** ve kimse görmemişti;
  çünkü `kapi.py kapanis` ilk kırmızıda durur ve test adımı ondan önce gelir.
  Kırılgan bir test adımı, arkasındaki kapıları **görünmez** yapar.

**Yarım kalan işler:** Site yayını (Adım 10) koşulmadı — `docfx` kırmızısı ve
yayının geri alınamazlığı sebebiyle kullanıcıya bırakıldı (👤).

**Sıradaki adım:** Faz yok. Keşif turunun sıra tablosu
([`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) §7.2)
Blok C'yi yayından sonraya koyuyor; yayın kararı kullanıcınındır.
