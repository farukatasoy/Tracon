# Faz 160 — Lisans Modeli ve Paket Metaverisi

> **Durum:** ✅ Tamamlandı (2026-09-08)
> **Kaynak:** `nuget-danismani` turu (2026-09-08) — bu kalem [ADAYLAR.md](../../ADAYLAR.md) içinde hiç bulunmadı; yayın danışmanlığı üretti
> **Önkoşul:** Yok — ama yayın kapısını Faz 97 kurdu: [`arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md`](97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md)
> **Paketler:** 20 paketlenebilir projenin **tamamı** — yalnız metaveri ve lisans dosyası
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — tek satır C# değişmez. `wc -l src/*/PublicAPI.Shipped.txt` = 17 satır, hepsi `#nullable enable` (ölçüldü 2026-09-08); taban çizgisi boştur
> **Tüketici yüzeyi:** `docs-site/src/content/docs/reference/licensing.md` (**yeni**) · `packages.md` · `docs-site/src/sidebar.mjs` · sevk edilen: kök `README.md` (pakete girer), `LICENSE.md`, `LICENSE-MIT.md`
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 0cd18a20:docs/arsiv/fazlar/160-LISANS-MODELI-VE-PAKET-METAVERISI.md
> ```
>
> Damıtıldı 2026-09-08 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism bugün MIT lisanslıdır ve hiç yayınlanmamıştır. MIT, üçüncü tarafın paketi ticari bir üründe sınırsız ve bedelsiz kullanmasına izin verir. Kullanıcı bu üründen gelir elde etmeyi seçti; MIT bu hedefle bağdaşmaz. Bu faz lisans modelini **gelir eşikli, kaynağı görünür** bir modele geçirir: `PolyForm-Small-Business-1.0.0`.

## Bitiş Ölçütleri (DoD)

- [x] **`ReleaseArtifactTests` 51/51 yeşil** — `EveryPackageCarriesMetadata` 20 paketin her biri için lisans dosyasını, `.nuspec` `type="file"` değerini ve `requireLicenseAcceptance`'ı doğrular. Fixture paketleri taze üretir
- [x] **`unzip -l` ile ölçüldü (`1.0.0-preview.1`, 20 paket):** 3 MIT paketi `LICENSE-MIT.md` + `acc=0`, 17 paket `LICENSE.md` + `acc=1`. Her pakette beyan edilen dosya ile içerideki dosya aynı; hiçbiri ikisini birden taşımıyor
- [x] **`python3 scripts/kapi.py yayin --kuru` çıkış kodu 0** (commit `74a3c050`, temiz ağaç, sürüm `0.0.0-preview.0.669`): 20 paket · `npm publish --dry-run` · **6 exact-version packed sample izole `NUGET_PACKAGES` ile koştu** · Native AOT smoke publish. Ağa hiçbir şey yazılmadı
- [x] `COMMERCIAL.md` silindi; `README.md`, `src/Directory.Build.props` ve `scripts/kapi.py` içinde MIT yalnız üçlüye ait satırlarda geçiyor. Ayrıca **8 paket README'sindeki** `License: MIT` çelişkisi giderildi (planda yoktu)
- [x] `Company` = `Atanova`; `Copyright` = `Copyright (c) Faruk Atasoy` değişmedi
- [x] `PackageLicenseTests` 6/6 yeşil: bağımlılık yönü · 20 projenin tamamı tabloda · props ↔ `kapi.py` matris uyumu · npm kopyasının birebirliği · kanonik PolyForm gövdesi. **red→green doğrulandı** (kapıda tek bir paket kaydırıldı, `The_build_matrix_and_the_release_gate_agree_on_which_packages_are_MIT` düştü)
- [x] İzole `NUGET_PACKAGES` + exact sürümle 6 örnek tüketici `restore` + `build` + test koştu (`yayin --kuru` içinden)
- [x] **Dört doğrulama kapısı sıfır uyarı** (`kapanis --taban 0d537d3c`, EXIT=0; `dotnet test AgentPrism.slnx` ✅ 654.58 s)
- [x] `samples/AgentPrism.Api` ayağa kalktı ve hizmet verdi — **model çağıran bir `run` YAPILMADI**, çünkü bu faz çalışma anına hiç dokunmuyor ve gerçek bir `run` için sağlayıcı `secret`'ı gerekir. Ölçülen: 36 migration uygulandı · `Now listening on: http://localhost:5080` · `GET /agentprism` → `200` (konsol HTML'i) · `/api/agents`, `/api/diagnostics`, `/api/models/health` → `401` (yetkilendirme katmanı çalışıyor). Daha güçlü kanıt `yayin --kuru` içindedir: **6 packed sample izole feed'den `restore` + `build` + test koştu**
- [x] `secret` taraması boş döndü (`kapanis` içindeki `tarama` aşaması ✅)
- [x] `MT-PKG-118..121` eklendi (`01-KURULUM-VE-PAKETLEME.md`, sayım 76 → 80); `MT-PKG-023`'ün eski MIT beklentisi düzeltildi. `MT-PKG-121` 👤 işaretli
- [x] `faz-denetim` taze bağlamlı bağımsız denetçiyle koşuldu: **1 🔴 · 3 🟡 · 4 🟢**. 🔴 (npm README'si `package.json` ile çelişiyordu) **kapandı** ve sınıfı `MT-PKG-122` ile kapıya bağlandı; 🟡'lardan üçü düzeltildi, biri gerekçelendi. Ayrıntı: **Denetim Bulguları** bölümü
- [x] `docs-site/` güncellendi (`reference/licensing.md` · `packages.md` · `sidebar.mjs`); `npm run build` 1116 sayfa, `check-links.mjs` 165.996 bağlantı **sıfır kırık**, `check-content.mjs` 53 manuel sayfa geçti
- [x] `python3 scripts/dokuman-bakim.py` çıkış kodu 0 — manuel kabul sayımı ve kırık bağlantı temiz

### Doğrulama komutları

```bash
# Paket basina lisans - beklenen dosya paketlendi mi
for p in artifacts/*.nupkg; do
  echo "== $p"; unzip -l "$p" | grep -E "LICENSE(-MIT)?\.md"
  unzip -p "$p" '*.nuspec' | grep -E "<license |requireLicenseAcceptance"
done

# Yayin provasi - aga hicbir sey yazmaz
python3 scripts/kapi.py yayin --kuru --surum 0.1.0-preview.1

# Kapanis kapilari
python3 scripts/kapi.py kapanis --taban <faz oncesi commit>
```

---

## Plandan Sapmalar

> Uygulama sırasında ölçümle bulundu. Kapanışta gerekirse eklenir.

| # | Plan ne diyordu | Ölçüm ne gösterdi | Ne yapıldı |
|---|---|---|---|
| 1 | 🚨 "Üç MIT projesi kendi `.csproj`'unda geçersiz kılar" (160.3) | `<None Include="…$(PackageLicenseFile)">` **aynı** `Directory.Build.props` içindedir ve `ItemGroup` dosya sırasına göre, herhangi bir `csproj` gövdesinden **önce** değerlendirilir. `csproj` override'ı `.nuspec`'i doğru yazar ama **yanlış dosyayı paketlerdi** | Matris `Directory.Build.props` içine, `ItemGroup`'un **üstüne** taşındı ve `$(MSBuildProjectName)` ile anahtarlandı. `src/Directory.Build.targets` açmak kök `Directory.Build.targets`'ı sessizce devre dışı bırakacağı için seçilmedi |
| 2 | Kapı `<requireLicenseAcceptance>false</…>` metnini arayacaktı (160.4) | NuGet bu elementi **yalnız `true` iken** yazar; `false` varsayılandır ve element hiç görünmez. Üç paketle paketleyip ölçüldü | Kapı MIT tarafını elementin **yokluğu** ile doğruluyor. `MT-PKG-023` zaten bu davranışı 2026-08-15'te kaydetmişti |
| 3 | "`COMMERCIAL.md` silinince `SourceLanguageTests` taban çizgisi kayar" (hata modu tablosu) | **Yanlış.** `ScannedRootFiles` yalnız `README.md · CONTRIBUTING.md · ARCHITECTURE.md`'dir; `COMMERCIAL.md` hiç taranmıyordu. Silmenin taban çizgisine etkisi sıfır | Bunun yerine `LICENSE.md` ve `LICENSE-MIT.md` **iki kapıya da eklendi** (`SourceLanguageTests` · `ShippedDocumentationSelfContainmentTests`) — artık her pakette sevk edildikleri için paket README'siyle aynı statüdeler |
| 4 | npm paketi kapsamda değildi | `packages/agentprism-client/package.json` `"license": "MIT"` ilan ediyordu ve hiç lisans dosyası taşımıyordu — NuGet ikizi `AgentPrism.Client` ise PolyForm. Aile içinde çelişki | Kullanıcı kararıyla PolyForm'a alındı: `"PolyForm-Small-Business-1.0.0"`, `files`'a `LICENSE.md`, kök dosyanın kopyası. `PackageLicenseTests` kopyanın birebirliğini kilitler |
| 5 | — | `Directory.Build.props:120` yorumu "all 19 packages" diyordu; ölçülen sayı **20** | Düzeltildi. Değiştirdiğim `ItemGroup`'un hemen üstündeydi |
| 6 | — | `MT-PKG-023` `<license type="expression">MIT</license>` bekliyordu | Yeni beklentiye güncellendi; yoksa sonraki manuel koşum bu case'te kalırdı |
| 7 | "`packages.md` aynı matrisi taşır" (160.5) | Sayfa tek bir ana tablo değil, amaca göre bölünmüş **beş** tablo taşıyor. Matrisi oraya yaymak üçüncü bir kopya üretirdi | Kısa bir "Licensing in one line" bölümü + `reference/licensing` bağlantısı |
| 12 | — | `kapi.py yayin --kuru` **kirli çalışma ağacında reddetti** (K-661: "bir yayın provasının kanıt değeri kirli bir ağaçta yoktur"). Ardından temiz ağaçta da düştü: `artifacts/package/release/` **414 dosya** biriktirmişti (`0.0.0-preview.0.650` … `.669` ve `1.0.0-preview.1`), örnek tüketici sözleşmesi kirli feed'i reddediyor | Kullanıcı onayıyla commit atıldı, `artifacts/package/release` silindi (gitignore'da, güvenli) ve prova yeniden koşuldu. 🚨 **Devir notu:** bu dizin kendini temizlemiyor; her yerel `Package.Tests` koşumu bir sürüm daha ekliyor. Provadan önce silmek gerekiyor |
| 10 | 🔴 Plan yalnız `scripts/kapi.py:1077` kapısını saymıştı | **İkinci bir kapı aynı MIT dizgesini sabit kodluyordu:** `tests/AgentPrism.Package.Tests/ReleaseArtifactTests.cs:114`. Dört kapı koşumunda `EveryPackageCarriesMetadata` düştü ve izole koşumda da düştü — gerçek regresyon | Sınıf tarandı (`grep -rn 'license type='`), başka kopya çıkmadı. Test `PackableProjects.LicenceFileOf` ile **matrisi `Directory.Build.props`'tan okuyor**; dördüncü bir kopya yazılmadı. Aynı üç yönlü doğrulama (beyan · içerik · kabul) buraya da girdi |
| 11 | Plan `CHANGELOG.md`'yi hiç anmamıştı | Lisans değişikliği tam olarak sürüm notuna girmesi gereken şeydir; `Unreleased` bölümü ise MIT'den söz etmiyordu | `### Changed` altına giriş yazıldı, site changelog'u yeniden üretildi. 🚨 `kapi.py yayin --kuru` `CHANGELOG` kapısını yalnız sürüm `1.0.0-preview.N` desenine uyduğunda koşar; henüz yayınlanmış sürüm olmadığı için prova `--surum` verilmeden koşuldu ve o kapı bu fazda **görülmedi** |
| 9 | 🔴 Plan paket README'lerini hiç saymamıştı | **Sekiz paket README'si `License: MIT` diyordu** ve bunlar `PackageReadmeFile`'dır — nuget.org'un render ettiği sayfa. Paket PolyForm olurken en görünür tüketici yüzeyi MIT iddia edecekti | Sekizi de düzeltildi. Ayrıca MIT üçlüsünden sessiz kalan ikisine (`Templates`, `Testing.Contracts.Xunit`) neden MIT oldukları yazıldı — ayrıcalıklı olan onlar ve bunu söylemeyen bir README eklenti yazarını caydırır |
| 8 | — | `src/AgentPrism.Templates/content/AgentPrism.Starter/AgentPrism.Starter.csproj` bir şablon içeriğidir, paket değil. Testin proje keşfi `kapi.py`'nin `src/*/*.csproj` tek seviye globunu birebir izlemek zorunda | Test `EnumerateDirectories` + `TopDirectoryOnly` ile aynı şekli kullanıyor. İlk hâli bu farkı kırmızıyla yakaladı |

## Bu Fazda Verilen Kararlar

| Karar | Özet |
|---|---|
| **K-740** | AgentPrism MIT değil `PolyForm-Small-Business-1.0.0` ile sevk edilir; `Abstractions` · `Testing.Contracts.Xunit` · `Templates` MIT kalır (kullanıcı kararı) |
| **K-741** | Pakette lisans anahtarı, aktivasyon çağrısı veya özellik kapısı yoktur; tahsilat kurumsal lisans tarayıcılarına dayanır — bu yüzden kanonik gövde birebir sevk edilir (kullanıcı kararı) |
| **K-742** | Matris iki yetkili kaynakta yazılır (`Directory.Build.props` + `kapi.py`) ve `PackageLicenseTests` onları kilitler; üçüncü kopya türetilir |

`COMMERCIAL.md`'nin siteye taşınması ayrı bir karar kaydı almadı — yerel bir
doküman yerleşimi tercihidir ve K-659'un zaten kurduğu mantığı izler.

## Denetim Bulguları

> `faz-denetim`, taze bağlamlı bağımsız denetçi, 2026-09-08.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `packages/agentprism-client/README.md:102` hâlâ `License: MIT` diyordu; aynı paketin `package.json`'ı PolyForm ilan ediyor ve PolyForm `LICENSE.md` sevk ediyor | 🔴 | **Düzeltildi.** Çeliştiği yer npmjs.com'un render ettiği sayfaydı. Sınıfı da kapatıldı: sapma #9'un taraması yalnız `src/*/README.md`'yi süpürmüştü, `packages/*/README.md` de sevk edilen README'dir. Kapı olarak `MT-PKG-122` yazıldı ve tuzak `hafiza/paketleme-ve-dagitim.md`'ye geçti |
| 2 | Üçüncü kopya (npm) matrise değil **sabit dizgeye** kilitliydi; `AgentPrism.Client` ileride MIT'ye alınsa altı test de yeşil geçer, npm sessizce PolyForm kalırdı | 🟡 | **Düzeltildi.** Beklenen SPDX kimliği artık `PACKAGE_LICENSES["AgentPrism.Client"]`'tan türetiliyor. Denetçinin tarif ettiği senaryo birebir kuruldu (props + kapı `Client`'ı MIT yaptı, npm dokunulmadı) ve `The_npm_client_ships_the_same_licence_as_its_NuGet_twin` **düştü**; geri alınca 6/6 yeşil |
| 3 | 20 paketin 10'unun README'si lisans hakkında hiçbir şey söylemiyordu; aile varsayılanı artık gelir eşikli | 🟡 | **Düzeltildi.** On paket README'sine lisans satırı eklendi. `AgentPrism.Sql.Shared` bilerek dışarıda: `.csproj`'u yok, paketlenmiyor — "terms ship in the package" demek orada yanlış olurdu |
| 4 | Plandaki kabul case 8 ("README'de göreli link kalmamıştır") yazılmadı ve iddiası yanlıştı | 🟡 | **Gerekçelendi + değiştirildi.** İddia yanlıştı: `LICENSE.md` ve `LICENSE-MIT.md` README'nin yanına, paketin köküne paketlenir; `.nupkg`'yi tutan okuyucu için göreli link **çözülür** (denetçi 🟢 #7'de aynı sonuca vardı). Case yerine gerçek riski ölçen `MT-PKG-122` yazıldı |
| 5 | Kanonik PolyForm gövdesi otomatik olarak yalnız iki dizgeyle korunuyor; tam karşılaştırma `MT-PKG-120`'de ve **ağ gerektiriyor** | 🟢 | **Devredildi.** Plan bu doğrulamayı bilinçle Manuel'e verdi. Metnin SHA-256'sını teste sabitlemek ağsız bir kapı üretir — `docs/ADAYLAR.md` |
| 6 | `src/AgentPrism.UI/frontend/package.json` hâlâ `"license": "MIT"` | 🟢 | **Gerekçelendi.** `private: true`, hiç yayınlanmıyor; build çıktısı PolyForm `AgentPrism.UI` içine gömülür ve tüketici bu alanı görmez |
| 7 | README'nin göreli lisans linkleri nuget.org sayfasında 404 verir | 🟢 | **Gerekçelendi.** Her iki dosya da paketin köküne paketlenir; README zaten ~20 göreli link taşıyor ve `ShippedDocumentationSelfContainmentTests` bunu açıkça kabul ediyor |
| 8 | `hafiza/paketleme-ve-dagitim.md`'ye eklenen bölüm diyakritiksiz yazılmıştı | 🟢 | **Düzeltildi.** Sonraki oturumun `grep "lisans dosyası"` araması bölümü kaçırırdı |

**Denetçinin temiz bulduğu başlıklar:** 3.1 (13 DoD satırının 12'si için kanıt kodda/testte
doğrulandı) · 3.2 (altı iddianın hiçbiri boşa düşmüyor) · 3.3 · 3.4 · 3.5 · 3.6 (tek satır C#
değişmedi — `git diff --stat -- 'src/**/*.cs'` boş) · 3.7 · **muafiyet listesi ve taban çizgisi
denetimi: hiçbiri büyümedi**; iki kapının *kapsamı* büyüdü, bu ratchet'in doğru yönüdür.

## Tüketici Yüzeyi Envanteri

> `tuketici-dokuman-senkronu` Adım 1. Faz üç kovanın **üçüne birden** dokundu.

**1 · `docs-site/` (elle yazılan)**

| Sayfa | Ne oldu |
|---|---|
| `reference/licensing.md` | **Yeni.** Eşiğin iki kolu · 3+17 matrisi ve gerekçesi · lisans anahtarı olmadığı sözü · yayınlanmış sürümün değişmediği · procurement'ın sorduğu beş soru |
| `packages.md` | "Licensing in one line" bölümü + sayfaya bağlantı. Matris burada **tekrarlanmadı** — sayfa amaca göre bölünmüş beş tablo taşıyor, üçüncü bir kopya üretmek kayma riskidir |
| `sidebar.mjs` | `Reference` bölümüne `Licensing` kaydı. Kaydedilmezse `check-content.mjs` erişilemez sayfa diye kızarır |
| `reference/changelog.md` | **Üretilen** — `CHANGELOG.md`'den `build-changelog.mjs` ile yeniden üretildi |

**2 · Sevk edilen metin**

| Yapıt | Ne oldu |
|---|---|
| `LICENSE.md` · `LICENSE-MIT.md` | **Her pakette sevk ediliyor** (`PackageLicenseFile`). Bu yüzden ikisi de `SourceLanguageTests` ve `ShippedDocumentationSelfContainmentTests` kapsamına **alındı** |
| Kök `README.md` | Paket tablosuna `Licence` sütunu · `Licence` bölümü yeniden yazıldı |
| `src/*/README.md` | **19 paket.** 8'i `License: MIT` diyordu (çelişki), 11'i sessizdi. `Sql.Shared` bilerek dışarıda — paketlenmiyor |
| `packages/agentprism-client/README.md` | Denetimin 🔴'sı. npmjs.com'un render ettiği sayfaydı |
| `CHANGELOG.md` | `Unreleased → Changed` altına lisans değişikliği |
| `///` XML dokümanı | **Değişmedi** — tek satır C# değişmedi |
| `agentprism.json` | **Değişmedi** — HTTP yüzeyi değişmedi |

**3 · Yerel referans ve agent haritası**

`capabilities.md` **değişmedi** ve değişmemeliydi: faz yeni bir yetenek, yeni bir
paket veya yeni bir giriş noktası (`Add*`/`Use*`/`Map*`) eklemiyor. Lisans bir
yetenek değil, bir sevk koşuludur. `llms.txt` ve `llms-full.txt` yine de yeniden
üretildi (`build-agent-map.mjs`) çünkü site içeriği değişti.

---

## Sonraki Faza Devir Notu

### 🚨 Yayına giden yolda henüz YAPILMAMIŞ olanlar

Bu faz lisansı seçti; **ürünü yayınlamadı**. Sırasıyla açık kalanlar:

1. **Depo hâlâ `private`.** Public yapıldığı an K-659 yeniden açılır ve paket
   metaverisindeki üç URL alanı repo'ya dönebilir. Public yapmadan önce
   `LICENSE.md`'nin doğru olduğunu doğrula — o an dünyaya verilen metin odur.
2. **Alan adı kararı ertelendi.** `PackageProjectUrl` ve site hâlâ
   `agentprism.doayen.web.tr`. Ücretli bir ürün için kendi alan adı önerildi;
   karar kullanıcınındır.
3. **Fiyat, ödeme ve EULA kapsam dışıydı.** `LICENSE.md` ve site sayfası ticari
   lisans için yalnız bir e-posta adresi verir. Tahsilat (Paddle / Lemon Squeezy
   gibi bir Merchant of Record) kurulmadı.
4. **NuGet ID prefix rezervasyonu yapılmadı.** `AgentPrism` adı NuGet.org'da hâlâ
   boştur ve ilk gelene verilir. Risk depo public yapıldığı gün sıçrar; public
   yapma ile ilk `push` arasını uzun tutma.

### 🚨 Bilinen tuzaklar

- **`Directory.Build.props` içindeki `ItemGroup` hiçbir `csproj` gövdesini görmez.**
  Paket bazlı bir `pack` farkı `MSBuildProjectName` ile props içinde, `ItemGroup`'un
  üstünde çözülmelidir. `src/Directory.Build.targets` açmak köktekini sessizce
  devre dışı bırakır. Tam kayıt: [`hafiza/paketleme-ve-dagitim.md`](../../hafiza/paketleme-ve-dagitim.md)
- **`requireLicenseAcceptance` `false` iken `.nuspec`'e hiç yazılmaz.** Kapı
  elementin yokluğunu kontrol eder, `"false"` metnini değil.
- **Lisans iddiası taşıyan yüzey yalnız `.nuspec` değildir.** Sevk edilen README
  de lisans söyler ve nuget.org/npmjs.com **onu render eder**. Denetim burada bir
  🔴 buldu. Taranacak küme: `src/*/README.md` **ve** `packages/*/README.md` **ve**
  `package.json` **ve** `.nuspec`. Kapı: `MT-PKG-122`
- **`artifacts/package/release/` kendini temizlemez.** Her yerel `Package.Tests`
  koşumu bir sürüm daha ekler; 414 dosyaya ulaşmıştı ve `kapi.py yayin` bayat
  feed yüzünden düştü. Provadan önce `rm -rf artifacts/package/release`
- **Bu fazın kapı koşumlarında iki bilinen kırılgan çıktı:**
  `Playground_voice_mode_opens_microphone_and_shows_transcript` ve
  `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance`. İkisi de
  izole koşumda geçti ve ikisi de [`hafiza/test-yalitimi.md`](../../hafiza/test-yalitimi.md)'de
  kayıtlı altı vakadan. Kapının kendi teşhisi doğru davranışı söylüyor: *"Hepsi
  izole geçti → tam paketi TEKRAR koş."* Test atlanmaz, devre dışı bırakılmaz
- **`kapi.py yayin --kuru` `CHANGELOG` kapısını yalnız sürüm `1.0.0-preview.N`
  desenine uyduğunda koşar.** İlk gerçek yayında o kapı ilk kez görülecek; bölüm
  hazır olmalı

### Devralınan sözleşme

Yeni bir paket eklemek artık **lisans kararı ister**: `scripts/kapi.py`
`PACKAGE_LICENSES` tablosuna girmeyen paket için kapı hata verir ve varsayılan
yoktur. MIT tarafına eklenecek bir paket ayrıca `src/Directory.Build.props`
`AgentPrismMitLicensed` koşuluna girer — iki liste ayrışırsa `PackageLicenseTests`
düşer.
