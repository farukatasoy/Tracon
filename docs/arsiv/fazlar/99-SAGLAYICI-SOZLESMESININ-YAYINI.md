# Faz 99 — Sağlayıcı Sözleşmesinin Yayını

> **Durum:** ✅ Tamamlandı (2026-08-25) — bağımsız `faz-denetim` bulguları kapatıldı, kapanış kapıları temiz ve site yayınlandı.
> **Kaynak:** Doğrudan kullanıcı isteği (2026-08-25). Aday listesinden gelmedi;
> `preview.1` öncesi genişleme noktası olgunlaştırma işidir.
> **Önkoşul:** [Faz 98](98-DEPOLAMA-SOZLESMESININ-YAYINI.md) —
> `Tracon.Testing.Contracts.Xunit` paketini, `ContractCoverage` kapısını ve
> yalnız-NuGet sample emsalini (`Tracon.Samples.FileRunStore`) bu faz devralır.
> **Paketler:** `Tracon.Abstractions`, `Tracon.Testing.Contracts.Xunit`,
> `Tracon.Core` (yalnız XML dokümanı), `samples/`
> **Yeni paket:** Yok — sözleşme suite'i var olan pakete yeni bir ad alanı ekler ·
> **Migration:** Yok
> **Public API:** Büyüyor — `ModelProviderContract` + `ContractCoverage`'ın kapsam
> parametresi. `wc -l src/*/PublicAPI.Shipped.txt` = 17 satır, hepsi başlık: **her
> dosya boş**, yani yüzeyi bugün büyütmek bedavadır.
> **Tüketici yüzeyi:** site: `guides/model-providers.md` (üçüncü taraf bölümü),
> `packages.md`, `capabilities.md` · sevk edilen: `IModelProvider` XML dokümanı,
> `src/Tracon.Testing.Contracts.Xunit/README.md`, `IContentGuard` XML dokümanı
> **Manuel test alanı:** `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` (`MT-TEST-078..083`) — plandan sapma 5

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show ca420de:docs/arsiv/fazlar/99-SAGLAYICI-SOZLESMESININ-YAYINI.md
> ```
>
> Damıtıldı 2026-08-25 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'in `IModelProvider` genişleme noktası bugün **kaynak kodu okumadan doğru uygulanamaz.** Arayüzün XML dokümanı ham istemci kuralını ve boru hattı sahipliğini anlatır; ama singleton ömrü, thread-safety beklentisi, dispose sahipliği, ad karşılaştırması, katalog semantiği ve hata sınıflandırmasının mesaj metnine bağlı olduğu **hiçbir sevk edilen yüzeyde yazmaz**.

## Bitiş Ölçütleri (DoD)

- [x] `IModelProvider` XML dokümanı 99.1'deki **sekiz maddenin hepsini** taşır; `credential` semantiği ve `CompiledAgentCache` uyarısı çapraz referanslıdır
- [x] `IContentGuard`'ın "OUTERMOST" cümlesi koda göre düzeltildi
- [x] `ModelProviderContract` `Tracon.Testing.Contracts.Providers` ad alanında yayınlandı; yalnız `Abstractions` + `Microsoft.Extensions.AI` alır (`Tracon.Core` **inmez** — bağımlılık grafiği ölçülerek doğrulandı)
- [x] `samples/Tracon.Samples.CustomModelProvider` yalnız `PackageReference` kullanır; `grep -c ProjectReference` → `0`
- [x] Sample'ın test projesi `ModelProviderContract`'ı türetir, credential'ı gerçek istemci sınırında uygular **ve** uçtan uca bir `run` tamamlar; hepsi yeşil
- [x] `PipelineOwnershipTests` Ö2'nin farkını ölçer: ham sağlayıcıda tool sonucu `Input` yönünde denetlenir, sarmalayan sağlayıcıda denetlenmez. Test hiçbir implementation tip adına bağlanmaz
- [x] Dört mevcut `StoreContractCoverageTests` yeşil kaldı
- [x] Sözleşme paketinin public yüzeyine `Shouldly` tipi **sızmaz**: `protected`/`public` imzalarda Shouldly tipi yok
- [x] Dört doğrulama kapısı sıfır uyarı verdi
- [x] `samples/Tracon.Api` gerçek PostgreSQL veritabanıyla başladı; `/health` 200 döndü
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi; 1·2·4·5 koşuldu
- [x] `faz-denetim` koşuldu; ilk turdaki iki 🔴 ve üç 🟡 bulgu kapatıldı
- [x] `docs-site/` güncellendi (`guides/model-providers.md`, `packages.md`, üretilen `llms-full.txt`); Node 22 ile site doğrulama ve link kapıları temiz
- [x] `F-149` `docs/ADAYLAR.md` içine yazıldı
- [x] Dispose sahipliği kararı `KARARLAR.md`'ye K-609 olarak girdi (public contract kararıdır)

### Doğrulama komutları

```bash
# Sözleşme paketi Core'a inmiyor
F=$(find artifacts/obj/Tracon.Testing.Contracts.Xunit -name project.assets.json | head -1)
python3 -c "import json;d=json.load(open('$F'));print([k for k in list(d['targets'].values())[0] if 'Tracon.Core' in k])"
# beklenen: []

# Sample yalnız NuGet
grep -c ProjectReference samples/Tracon.Samples.CustomModelProvider/*.csproj || true   # 0

# Sample sözleşmeyi geçiyor
MSBUILDDISABLENODEREUSE=1 dotnet test samples/Tracon.Samples.CustomModelProvider.Tests

# Boru hattı sahipliği regresyonu
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests --filter-method "*PipelineOwnership*"
```

---

## Plandan Sapmalar

> Uygulama sırasında yazıldı; kapanışta gözden geçirilir.

**1. `ModelProviderContract` tek sınıf değil, ÜÇ sınıf oldu.** Plan isteğe bağlı
davranışları (BYOK, sağlayıcı ayarları) tek sınıfta atlanan senaryolar olarak
tasarlamıştı. Ölçüm bunu düşürdü: `Assert.Skip` `xunit.v3.assert` paketindedir ve
o paket sözleşme paketinin çözülmüş grafiğinde **yoktur** — eklemek planın "yeni
paket: yok" satırını ihlal ederdi. xunit v3'ün `[Fact(SkipUnless = ...)]`
alternatifi ise **public static** bir özellik ister; bizim koşulumuz örnek
düzeyinde sanal bir üyeye bağlıdır, static bir üye onu okuyamaz. Çözüm paketin
kendi deyimidir: isteğe bağlı davranış ayrı bir opt-in sözleşme sınıfıdır
(`ModelProviderCredentialContract`, `ModelProviderSettingsContract`). Türetmek
niyet beyanıdır ve **sessizce geçen senaryo kalmaz** — depolama tarafındaki 32
sınıf + muafiyet deseninin aynısı.

**2. `ContractCoverage` ad alanı taşındı ve kapsamsız aşırı yüklemeler KALDIRILDI.**
Plan yalnız kapsamlı bir aşırı yükleme *eklemeyi* öngörüyordu. Kapsamsız olanı
bırakmak, dört depolama kapsam testini kıran tuzağın kendisini bırakmak olurdu:
bir depolama tüketicisi, Tracon sağlayıcı sözleşmesi yayınladı diye
kırılabilirdi. Tip `Tracon.Testing.Contracts.Storage`'dan
`Tracon.Testing.Contracts`'a taşındı — iki aileye birden hizmet ediyor, ad
alanı artık yanıltmıyor. Aile adları `ContractCoverage.StorageContracts` /
`ProviderContracts` sabitleridir; yazım hatası derleme hatasıdır. Hiçbir
sözleşme tipi eşleşmeyen bir kapsam `ArgumentException` verir — sessizce hiçbir
şeyi kontrol etmeyen yeşil bir kapı üretmez.

**3. Public yüzey büyümesi ölçüldü ve kabul edildi.** `Tracon.Testing.Contracts.Xunit`
36 → **39** public tip (üç sözleşme sınıfı); `PublicAPI.Unshipped.txt` +38 girdi,
eski `Storage.ContractCoverage` girdileri düştü. `PublicSurfaceBaselineTests`
büyümeyi kasıtlı bir ekleme olarak zorladı — taban çizgisi elle güncellendi.

**4. Analyzer gevşetmeleri `.editorconfig`'e değil sample'ın csproj'una gitti.**
`[samples/*.Tests/**/*.cs]` glob'u eşleşmedi (ölçüldü). Ayrıca `.editorconfig`'de
mevcut `[samples/**/*.cs]` bloğunun ilk satırına tutunan bir düzenleme bloğu
**ikiye böldü** — CA2007/MA0004 satırları yanlış bölüme kaydı ve geri alındı.
Gevşetme artık projeye özgü ve gerekçeli: `<NoWarn>CA1707;xUnit1051</NoWarn>`.

**5. Manuel case'ler ayrı dosyaya değil `24-TEST-PAKETI-VE-SABLON.md`'ye girdi.**
Plan `docs/manuel-test/99-SAGLAYICI-SOZLESMESI.md` diyordu; 22 numarası zaten
doluydu ve daha önemlisi Faz 98 aynı türden case'leri (sözleşme paketi + örnek
tüketici) 24'e koymuştu. O dosyanın kapsam satırı zaten
`src/Tracon.Testing.Contracts.Xunit` ve `samples/*` içeriyor. Case'ler
`MT-TEST-078..083`.

**6. Planda olmayan bir doküman kusuru bulundu ve düzeltildi.** `IContentGuard`'ın
XML dokümanı guard'ın "OUTERMOST" koştuğunu yazıyordu; K-320'den beri guard
tool-call döngüsünün **içindedir**. Koda göre düzeltildi.

**7. Sevk edilen doküman kapısı planın kendi metnini yakaladı.**
`ShippedDocumentationSelfContainmentTests` `IModelProvider`'ın yeni
`<remarks>`'ında bir 🚨 buldu ve reddetti — sevk edilen XML alarm emojisi
taşımaz. Kaldırıldı. Kapı, bu fazın amacını (tüketici sesiyle yazmak) kendi
üzerimizde uyguladı.

**8. Ölçülen bulgu: en olası hatayı yapmak yapısal olarak zordur.** Sözleşmeyi
kasten ihlal etme denemesi **derlenmedi**: yalnız `Tracon.Abstractions`'a
bağlı bir sağlayıcı `AsBuilder()`/`UseFunctionInvocation()` tiplerine erişemez —
onlar `Microsoft.Extensions.AI` paketindedir ve `Abstractions` yalnız
`Microsoft.Extensions.AI.Abstractions` taşır. İhlal ancak paket bilerek
eklenirse mümkün. Sözleşme testi yine de gereklidir (sağlayıcı o paketi başka
bir sebeple almış olabilir), ama risk planın varsaydığından düşüktür.

**9. Denetim, BYOK iddiasının eksik ölçüldüğünü buldu.** İlk test, credential ile
oluşan iki istemcinin ayrı nesne olduğunu doğruluyordu; bu, credential'ın gerçek
istek sınırında kullanıldığını kanıtlamaz. `ModelProviderCredentialContract` artık
sağlayıcının `AssertCredentialIsApplied` kancasını zorunlu tutar. Sample bu
kancada scriptlenmiş istemcinin API key'i kullandığını doğrular.

**10. Faz dışı iki üretim kusuru kapanışa alındı.** F-150 için worker, uçuştaki
işleri kaydeder ve slot semaforunu dispose etmeden önce tamamlanmalarını bekler.
F-151 için iki uygulanmış migration, uygulanmış ilk baytlarına döndürüldü; kapı
dosyaları değiştirilebilir checksum manifestinden değil, Git'teki sabit kaynak
commit'lerden okur. Bu iki düzeltme fazın kapsamını genişletti, çünkü `preview.1`
öncesinde sevk edilmiş kırılmayı açık bırakmak kabul edilemezdi.

## Bu Fazda Verilen Kararlar

- **K-609:** Tracon'in dönen `IChatClient` için dispose sahipliği yoktur.
- **K-610:** `ContractCoverage` her çağrıda açık bir sözleşme ailesi alır.
- **K-611:** İsteğe bağlı sağlayıcı davranışı, atlanan case değil ayrı opt-in
  sözleşme sınıfıdır.
- **K-612:** Uygulanmış migration baytları, değiştirilebilir checksum kaydına
  değil Git'teki sabit kaynak commit'lerine göre doğrulanır.

## Site Senkron Gerekçesi

`--site-denetle` bir kural bildirdi: `IContentGuard.cs` değişti ama
`docs-site/.../concepts/` değişmedi. **Site güncelleme gerektirmiyor, çünkü site
zaten doğruydu.** `concepts/governance.md` şunu yazıyor: *"The guard sits
**inside** the tool-call loop, above the raw client. A tool result re-enters the
model on a second call, and a guard outside the loop would never see it."*
Bayat olan sevk edilen XML dokümanıydı ("OUTERMOST") ve bu fazda koda göre
düzeltildi (sapma 6). Yani değişiklik siteyi siteye yaklaştırdı, ondan
uzaklaştırmadı. `--site-gerekce-yazildi` ile geçildi.

Fazın gerçekten dokunduğu site sayfaları güncellendi:
`guides/model-providers.md` (üçüncü taraf bölümü baştan yazıldı, hata
sınıflandırma tablosu, yetenek bayrağı tablosu ve gerekli `AddHttpClient`
kaydını ekledi) ve `packages.md` (sözleşme paketinin satırı iki aileyi kapsıyor).
`llms-full.txt` üretilen yüzeydir. `capabilities.md` değişmedi; paket ya da
yetenek tanımı değişmedi. F-151'deki iki SQL yorumunun geri alınması yalnız
migration bayt bütünlüğünü düzeltir; persistence sayfasındaki tüketici davranışı
zaten doğrudur. Bu gerekçelerle `--site-denetle --site-gerekce-yazildi` geçti.

---

## Denetim Bulguları

2026-08-25'te taze bağlamlı bağımsız denetçi `faz-denetim` koştu. İlk turdaki
iki 🔴 ve üç 🟡 bulgunun tamamı aşağıdaki odak testleriyle kapatıldı:

| Bulgu | Seviye | Kapanış kanıtı |
|---|---|---|
| Sample csproj yorumunda `ProjectReference` sözcüğü kaldığı için DoD komutu yanlış pozitif veriyordu | 🔴 | Sözcük yorumdan kaldırıldı; `grep -c ... || true` → `0` |
| Uygulanmış migration bütünlüğü değiştirilebilir checksum manifestine dayanıyordu | 🔴 | `scripts/applied-migrations.json` Git kaynak commit'lerini taşır; `kapi.py tarama` bu commit'lerin baytlarını okur ve manifest değişse bile farklı migration'ı reddeder |
| BYOK testi yalnız nesne ayrılığını ölçüyordu | 🟡 | Yeni zorunlu `AssertCredentialIsApplied` kancası sample istemcinin API key'i gerçekten kullandığını doğrular |
| F-150 için kapanış yarışını doğrudan ölçen test yoktu | 🟡 | `JobWorkerBackgroundServiceTests` `StopAsync`'in iş slotu serbest kalmadan dönmediğini doğrular |
| Sağlayıcı site örneğinde `HttpClient` kaydı yoktu | 🟡 | Örneğe `services.AddHttpClient()` eklendi |

F-150, temel commit `c4e3189` üzerinde yeniden üretildi ve düzeltildi. F-151'in
iki dosyası ilk uygulanmış baytlarına döndürüldü. İnceleme ayrıca
`dokuman-bakim.py` arşivleme yolunun yalnız `*.md` dosyalarına dokunduğunu
gösterdi; önceki "arşivleme SQL yorumunu değiştirdi" kök neden iddiası yanlıştı.
Yeni Git tabanlı kapı, değişikliği hangi araç yaparsa yapsın yakalar.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IModelProvider`'ın çalışma anı sözleşmesi artık **sevk edilen yüzeydedir**
  (XML + site). Sekiz madde: singleton ömrü · eşzamanlı çağrı · ham istemci ·
  ortak halkaların registry'ye aitliği · dispose sahipliği (K-609) · credential
  fabrikasının yan etkisizliği · `OrdinalIgnoreCase` ad + yinelenen kayıt hatası ·
  kataloğun izin listesi olmaması. Bu maddelerden birini değiştiren her faz
  `ModelProviderContract`'ı da değiştirmek zorundadır.
- `ContractCoverage` artık **aile adı alır** (K-610). Yeni bir sözleşme ailesi
  eklerken: `Contracts/<Aile>/` klasörü, `…Contracts.<Aile>` ad alanı,
  `ContractCoverage`'a bir sabit. Kapsamsız aşırı yükleme **yoktur ve
  eklenmemelidir**.
- İsteğe bağlı davranış = **ayrı opt-in sözleşme sınıfı** (K-611), atlanan
  senaryo değil.

**Bilinen tuzaklar (🚨):**
- 🚨 `xunit.v3.extensibility.core` `Assert` taşımaz; `[Fact(SkipUnless=…)]`
  **public static** özellik ister. `docs/hafiza/test-altyapisi.md`.
- 🚨 Sevk edilen `///` XML'inde alarm emojisi yasaktır — kapı kırar.
  `docs/hafiza/dokumantasyon.md`.
- 🚨 Yeni `samples/*.Tests` projesi `tests/**` analyzer gevşemelerini almaz;
  bastırma projenin kendi `<NoWarn>`'una yazılır.
- 🚨 Bu makinede tam çözüm testi (`dotnet test Tracon.slnx`) **host
  çekişmesinden** kırılıyor: iki veritabanı konteyneri + Playwright aynı anda
  koşuyor. Faz 99'da 641 kırmızının **tamamı** izole koşumda yeşile döndü
  (PostgreSQL 637/637, şablon 1/1, E2E 2/2, ToolGovernance 4/4). Faz 97'nin
  kapanış kaydı aynı sınıfı yazmıştı. **İzole koşum ayırt eder** — kırmızıyı
  otomatik olarak kusur sayma.

**Açık iş:**
- **F-149** hata sınıflandırmasının yapısal sözleşmeye taşınması — `1.0`
  öncesi karara bağlanmalıdır.
