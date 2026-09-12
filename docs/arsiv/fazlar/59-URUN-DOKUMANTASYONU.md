# Faz 59 — Ürün Dokümantasyonu (Doküman Sitesi)

> **Durum:** ✅ Tamamlandı (2026-08-16)
> **Kaynak:** Kullanıcı isteği (2026-08-15). Faz 7'nin 7.7 alt başlığını
> ("Dokümantasyon — dış tüketici gözüyle") **devralır ve genişletir**.
> **Önkoşul:** [Faz 57](57-KOD-DILI-BIRLESTIRME.md) — **zorunlu.** API referansı
> XML dokümandan üretilir; Türkçe XML doküman Türkçe site üretir.
> **Paketler:** Kod değişmez; `Tracon.AspNetCore` OpenAPI üstverisi düzeltilir
> **Yeni paket:** Yok (Node tarafında yeni bir çalışma alanı) · **Migration:** Yok
> **Public API:** Değişmiyor

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/59-URUN-DOKUMANTASYONU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon 17 NuGet paketi ve 33 route'luk bir arayüz yayınlıyor. Kullanıcıya dönük dokümantasyon bugün ~2.500 satırdır (`README.md` 362 + 18 paket README 1.375 + `MIMARI.md` 751); geri kalan ~99.500 satır **geliştirme günlüğüdür**. Site altyapısı sıfırdır — repo genelinde DocFX, Docusaurus, MkDocs veya GitHub Pages izi yoktur.

## Bitiş Ölçütleri (DoD)

- [x] 59.0 spike sonucu bir karar cümlesiyle bu dokümana yazıldı — 59.0 bölümü
- [x] Site yerel derleniyor: **628 sayfa**, sıfır hata (Node 22.21.0)
- [x] `/api/` altında **588 public tip** görünüyor ve açıklamaları İngilizce —
      plandaki 590 namespace sayfalarını da sayıyordu (bkz. 59.0 spike sonucu)
- [x] 143 operasyon render ediliyor (Scalar yerine üretilen sayfalarla, K-417);
      `info.title` = `Tracon HTTP API`, `servers` ve `securitySchemes` dolu
- [~] Getting started **temiz bir makinede** izlenmedi. Bu makinede doğrulanan:
      `dotnet new tracon-api` şablonu ve seçenekleri (`Templates.Tests`,
      canlı `--help`), örnek uygulamanın gerçek koşumu, sayfadaki her `curl`
      kalıbının canlı uygulamaya karşı çalışması. Temiz makine doğrulaması
      yapılmadı — .NET SDK'sız bir ortam gerektirir
- [x] UI guide'daki 14 ekran görüntüsünün tamamı E2E koşumundan **üretildi**
      (`DocumentationScreenshotTests`, `TRACON_UI_SCREENSHOTS=1`)
- [x] İç bağlantı denetimi: **628 sayfada 403.240 referans, sıfır kırık**
- [~] `pages` job'u yazıldı ve YAML doğrulandı; **yeşil olduğu görülmedi** —
      depo henüz GitHub'da yok (`git remote -v` boş). Bkz. "Yayın için gereken
      tek elle adım"
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/pack/format hepsi 0
- [x] `secret` taraması boş döndü; sitede bağlantı dizesi/anahtar yok
      (`docs/manuel-test/**`'teki sahte test anahtarları fazdan öncedir ve
      kapsam dışıdır — Faz 57 ile aynı gerekçe)
- [x] `find ... -name "* [0-9].*"` boş; `artifacts/` iki kez temizlendi ve
      `docfx.json` kalıcı `exclude` deseni taşıyor (Sapma 9)

### Doğrulama komutları

> ⚠️ Site **Node 22.12+** ister (Astro 7). Yerelde 20.x varsa derlenmez.

```bash
# Site — uretim + derleme + baglanti denetimi
cd docs-site && npm ci && npm run build && node scripts/check-links.mjs

# API referansi: kac tip, kac cozulemeyen capraz referans
cd docs-site && npm run generate
#   -> API reference: 588 types across 15 assemblies; 76 cross-reference(s) ...
#   -> HTTP API: 143 operations across 19 groups.

# OpenAPI ustverisi ve K-418 kapisinin olctugu sey
python3 -c "
import json; d=json.load(open('docs/openapi/tracon.json'))
ops=[o for p,v in d['paths'].items() for m,o in v.items() if m in ('get','post','put','delete','patch')]
props=[pv for s in d['components']['schemas'].values() for pv in (s.get('properties') or {}).values()]
print(d['info']['title'], d['servers'], d.get('security'))
print('description:', sum(1 for o in ops if o.get('description')), '/', len(ops))
print('sema property dokumani:', sum(1 for v in props if v.get('description')), '/', len(props))
"
#   -> Tracon HTTP API [...] [{'bearer': []}]
#   -> description: 143 / 143
#   -> sema property dokumani: 819 / 1080

# Ekran goruntulerini yenile
TRACON_UI_SCREENSHOTS=1 dotnet test tests/Tracon.Ui.E2ETests -c Release
```

---

## Plandan Sapmalar

1. **HTTP API için Scalar kullanılmadı; sayfalar OpenAPI belgesinden üretildi (K-417).**
   Plan Scalar öngörüyordu. Ölçüldü: standalone paket **7,4 MB / 90 chunk**, kendi
   temasında render ediyor ve içeriği **Pagefind'e girmiyor**. Bu fazda 143
   operasyonun tamamına açıklama yazıldı; hepsini sitenin kendi aramasından gizlemek
   kabul edilemezdi. `scripts/build-http-api.mjs` etiket başına bir Starlight sayfası
   üretir. Ham belge `/openapi/tracon.json` olarak yayınlanır.

2. **API referansı DocFX'in HTML sitesi değil, markdown çıktısı (K-416).** Aynı
   gerekçe: tek site, tek tema, tek arama indeksi. Bedeli ölçüldü ve ödendi — DocFX
   prose içindeki `<see cref>`'leri ham `<xref>` olarak bırakıyor (374/590 sayfada
   1351 oluşum) ve bunları çözen ~250 satırlık bir üreteç yazıldı.

3. **🚨 Fazın kendisi bir gerileme üretti ve kapı onu göstermedi (K-418).**
   `AddOpenApi(ProductOpenApiDocument.Configure)` yazıldı; belge **226 şemanın
   166'sının** property açıklamalarını kaybetti. `OpenApiSnapshotTests` yakalayamadı
   çünkü yalnız "dosya host'la aynı mı" der — snapshot yenilendiği an gerileme
   gizlendi. `git diff`'in satır sayısı (711 eklendi / 1639 silindi) fark edilip
   yapısal olarak ölçülünce çıktı. Kalıcı kapı eklendi ve hatalı bağlamayla gerçekten
   düştüğü doğrulandı.

4. **A2A uçları plan dışı eklendi.** Snapshot'ta yoklar (test host `MapTraconA2A`
   çağırmaz), bu yüzden "eksik 75" listesinde görünmüyorlardı. Örnek uygulamanın
   kendi belgesi 147/147 yerine 145/147 gösterince ortaya çıktılar; ikisine de
   `summary` + `description` yazıldı.

5. **Yayınlanan açıklamalardaki iç doküman referansları temizlendi (K-420).** Plan
   yalnız "eksik 75'i yaz" diyordu; var olan 68'in 6'sı Türkçe geliştirme günlüğüne
   işaret ediyordu ve o metinler siteye giriyor.

6. **Plan kanıtı bayattı: `src/Tracon.UI/README.md` zaten güncelmiş.** Plan "7
   ekran, ~88 KB" diyordu; dosya gerçekte 27 ekran / 33 route / 165,8 KB yazıyordu.
   Üçü de bağımsız ölçüldü ve doğrulandı (27 `.tsx`, 33 `pattern:`, `npm run build`
   → 165,8 KB). 59.1'in 5. kalemi bu yüzden **gereksizdi**.

7. **Node sürümü yükseldi.** Astro 7 **22.12+** ister; arayüzün Vite 7'si 20.19 ile
   yetiniyordu. Astro 5 seçilmedi çünkü `npm audit` 5 açık bildirdi (2 yüksek: XSS,
   SSRF). Astro 7.2.2 + Starlight 0.41.7 = 0 açık.

8. **`Tracon.Generators` referans listesinden çıkarıldı** — XML dokümanı yok ve
   `IsPackable=false`. Kullanıcının "17 paketin hepsi" kararı paketlenebilir 17
   projeyi kapsar; `Sql.Shared`'in `.csproj`'u yoktur, `Generators` paket değildir.

9. **🚨 Senkron kopyaları iki kez araya girdi.** `artifacts/` altında 1707 ve sonra
   814 adet `<ad> N.dll` kopyası oluştu; ikincisi DocFX'i `IOException` ile kırdı.
   Silmek yetmedi — `docfx.json`'ın `references` bloğuna kalıcı `exclude` deseni
   eklendi (`**/* [0-9].dll`), assembly listesi de glob yerine 17 yolu **açıkça**
   yazar. MEMORY.md'nin beş kez yaşanmış tuzağının altıncı ve yedinci vakası.

## Bu Fazda Verilen Kararlar

`docs/KARARLAR.md`'ye **K-415 – K-420** eklendi:

- **K-415** — Ürün sitesi ayrı, İngilizce, Astro Starlight, GitHub Pages (kullanıcı kararı)
- **K-416** — API referansı DocFX markdown + kendi bağlantı çözücümüz
- **K-417** — HTTP API sayfaları üretilir; Scalar gömülmedi
- **K-418** — 🚨 `AddOpenApi()` çıplak çağrılmalı; aksi hâlde şema XML dokümanı düşer
- **K-419** — Ekran görüntüleri E2E'den üretilir, `en-US` + Light sabit
- **K-420** — Yayınlanan `description` metinleri iç doküman referansı taşımaz

### Açık soruların cevapları (kullanıcı, 2026-08-16)

| # | Soru | Karar |
|---|---|---|
| 1 | Sürümleme | **A** — hayır, tek sürüm |
| 2 | `/api/` kapsamı | **A** — paketlenebilir 17 paketin hepsi |
| 3 | Concepts | **B** — sıfırdan, dış okuyucu için |
| 4 | Alan adı | `farukatasoy.github.io/Tracon` (proje sitesi, `base: /Tracon/`) |

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**

- `docs/` ile `docs-site/` iki ayrı şeydir ve karıştırılmaz (K-415). Kural
  `AGENTS.md`'dedir. Kullanıcıya dönük her yeni anlatı **siteye** yazılır.
- Site içeriğinin bir kısmı **üretilir ve commit edilmez**: `src/content/docs/api/`,
  `src/content/docs/http-api/`, `src/generated/`, `public/openapi/`. `npm run generate`
  onları yeniden kurar. Ekran görüntüleri **commit edilir**.
- Yeni bir HTTP ucu eklerken `.WithSummary` **ve** `.WithDescription` zorunludur —
  `OpenApiDocumentTests` artık her operasyonda `description` arar.

**Bilinen tuzaklar (🚨):**

- **`AddOpenApi()` çıplak çağrılmalıdır** (K-418). Delege alan aşırı yükleme XML
  doküman interceptor'ını devre dışı bırakır ve 226 şemanın 166'sı açıklamasız kalır.
  `OpenApiSnapshotTests` bunu göremez; snapshot yenilemek gerilemeyi gizler.
- **Snapshot yenilemeden önce `git diff --stat`'e bak.** Bu fazda 1639 satırlık bir
  silme, sessiz bir kayıp anlamına geliyordu. Satır sayısı beklenmedikse **yapısal
  olarak ölç** (path/op/schema/requestBody/response sayıları), sonra yenile.
- **Node 22.12+** gerekir (Astro 7). Yerel makinede 20.19 varsa site derlenmez;
  CI `node-version: '22'` kullanır.
- **Senkron kopyaları DocFX'i kırar** (`IOException`, yarım yazılmış `<ad> N.dll`).
  `docfx.json` artık `exclude` taşır ama `artifacts/` temizliği yine de ilk adımdır.
- **Starlight sidebar bağlantıları `base` TAŞIMAZ**, markdown bağlantıları taşır.
  Karıştırmak `/Tracon/Tracon/...` üretir; `check-links.mjs` yakalar.
- 🚨 **Tam çözüm koşumunda iki kez aralıklı bir test düşüşü görüldü** (bir kez
  `AspNetCore.FunctionalTests`, bir kez `Core.UnitTests`), hiçbiri izole koşumda
  tekrar etmedi ve ikisi de yakalanamadı. Sonrasında **altı ardışık tam koşum
  temiz** (`exit=0`), TRX ile üç kez daha denendi ve tekrar üretilemedi. Sebep
  atfedilmedi; bu faz bir E2E testi ekledi (`DocumentationScreenshotTests` — Kestrel
  host + tarayıcı bağlamı + iki gerçek `run`) ve paralel yükü artırdı, dolayısıyla
  ilişkisi dışlanamaz. MEMORY.md'de zaten kayıtlı olan "49 E2E testinden 1'i tam
  koşumda rastgele zaman aşımına uğrayabilir" tuzağıyla aynı sınıfta görünüyor.
  Kırmızı bir kapı görürsen **önce izole koş** — bayat mı kusurlu mu ayrımı budur.
- `run` kaydında `durationMs` ve token alanları gerçek Anthropic koşumunda `null`
  döndü (`startedAt`/`completedAt` dolu, 5 olay yazıldı, `status: Completed`). Bu
  fazdan **önce de** böyleydi ve bu faz çalışma anına dokunmadı; ölçülmemiş bir
  açık kalemdir.

**Sıradaki faz:** `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md` ve README'nin yol haritası
tablosu. Açık kalan büyük kalem **Faz 7**'dir (public API dondurma + NuGet yayını,
K-068); bu faz onun için gereken tüketici dokümantasyonunu hazırladı.

### 🚨 Yayın için gereken tek elle adım

`pages` job'ı yazıldı ve yeşil olmaya hazır, ama **GitHub deposu henüz yok**
(`git remote -v` boş). Site yayına girmeden önce depo açılmalı ve
**Settings → Pages → Source = GitHub Actions** seçilmelidir. Bu adım olmadan job
`actions/configure-pages` aşamasında düşer.
