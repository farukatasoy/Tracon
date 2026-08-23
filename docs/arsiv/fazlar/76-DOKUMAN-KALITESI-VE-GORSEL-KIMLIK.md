# Faz 76 — Doküman Kalitesi ve Görsel Kimlik

> **Durum:** ✅ Tamamlandı (2026-08-20)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-127**
> **Önkoşul:** [Faz 75](75-TUKETICI-DOKUMAN-DOGRULUGU.md) — **zorunlu sıra.** Yanlış bir sayfayı güzelleştirmek onu daha zararlı yapar; doğruluk ve eksiksizlik önce gelir · [Faz 59](59-URUN-DOKUMANTASYONU.md) — sitenin kendisi, `check-content.mjs` deseni ve `site.css` katmanı oradan devralınır
> **Paketler:** Yok — bu faz `src/**` altına **hiç dokunmaz** · `docs-site/` · `tests/AgentPrism.Ui.E2ETests` (yalnız ekran görüntüsü)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Büyümüyor.** Bu faz hiçbir `.cs` dosyasını değiştirmez
> **Site etkisi:** Tamamı. 37 elle yazılmış sayfa, `astro.config.mjs`, `src/styles/site.css`, yeni Starlight bileşen geçersiz kılmaları, `public/` görselleri
> **Manuel test alanı:** `docs/manuel-test/32-DOKUMAN-KALITESI.md` (31 numarayı Faz 75 aldı)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/76-DOKUMAN-KALITESI-VE-GORSEL-KIMLIK.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 75 dokümanın **doğru ve eksiksiz** olmasını sağladı. Bu faz onu **iyi** yapar. Ölçüm net bir tablo çiziyor: içerik yeterli, sunum değil. 37 elle yazılmış sayfa ve 371 KB anlatı var — kapsam sorunu yok.

## Bitiş Ölçütleri (DoD)

- [x] Site başlığı prizma işaretini taşıyor ve işaret **tek kaynaktan** geliyor —
      `logo: { src: './public/favicon.svg' }`; `dist/index.html` işareti
      `_astro/favicon.DvU8bgjN.svg` olarak basıyor, repo'da tek kopya var
- [x] `site.css` kapalı bir token kümesi tanımlıyor; her token açık **ve** koyu
      temada tanımlı — kapı zorluyor (`has no value in the light theme`), ve
      `:root` dışında bildirilen bir renk de reddediliyor
- [x] Kontrast kapısı yeşil; **ölçülen en düşük oran: metin 5,47:1**
      (`--ap-accent` / `--ap-accent-quiet`, açık tema), **metin dışı 3,46:1**
      (`--ap-border-strong` / `--ap-surface-raised`, koyu tema). Canlı doğrulama:
      kenar çubuğu/anahat/altbilgi metni iki temada 5,98–11,46:1
- [x] 39 elle yazılmış sayfanın **38'i** `## Read next` ile bitiyor; `index.mdx`
      kapıda gerekçeli muaf (sapma 3). "Related", "Next" ve "Related reference"
      başlığı **sıfır**
- [x] **Dokuz** uzun kılavuzun dokuzunda da diyagram var (plan sekiz diyordu —
      sapma 2); muafiyet listesindeki beş sayfanın her biri için gerekçe yazıldı
- [x] Dört bölümün dördünün de kendi `og:image`'ı var ve dosya diskte; on bir
      kenar çubuğu bölümünün tamamı bir görsele eşleniyor. Doğrulandı: dört
      görsel `200` döner, `ui` → `console.png`, `reference/glossary` →
      `reference.png`, `guides/production` → `operate.png`, açılış → `overview.png`
- [x] `troubleshooting.md` **14 girişli** belirti diziniyle açılıyor; 14
      bağlantının 14'ü bir bölüme çözülüyor (`check-links` çapaları doğruluyor) ve
      62 alt başlığın tamamı tek sayfada kaldı — `Ctrl+F` bozulmadı
- [x] `index.mdx` sorun cümlesi, akış diyagramı ve sınır cümlesi taşıyor; dört
      ölçülmüş sayı hâlâ kapıya bağlı (17 · 160 · 28 · 0)
- [x] Doküman sayfası ağırlığı **ölçüldü**: en ağır sayfa `troubleshooting`
      **49 376 B** gzip (HTML + başvurduğu CSS/JS; mermaid ayrıştırıcısı hariç,
      talep üzerine yükleniyor). Sınır **57 000 B** kondu (~%15 pay) ve
      `check-weight.mjs` ile zorlanıyor
- [x] Faz 75'in beş içerik iddiası hâlâ yeşil — hiçbiri gevşetilmedi; diff'te o
      bölüme yalnız **iki sıkılaştırma** var (kenar çubuğu erişilebilirliği artık
      metin araması değil yapı okuması; ayırıcı düzeltmesi `http-api.md`'yi
      denetime soktu)
- [x] `npm run check:content` on iddiayla yeşil; `npm run build` + `check-links`
      + `check:weight` temiz (1001 sayfa, 129 678 bağlantı, kırık yok)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build` 0/0, `test` 16 proje
      **4408/4408**, `pack` 0, `format` 0
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı: `support` agent'ı,
      `gpt-5.4-mini`, `status=Completed`, 236 girdi / 7 çıktı token, 6 olay,
      yanıt metni `documentation phase check ok`; kayıt
      `GET /api/runs/{id}` ile geri okundu
- [x] `secret` taraması boş döndü — bu fazın eklediği satırlarda hiçbir desen
      eşleşmedi; `secret`'lar yalnız `dotnet user-secrets` içinde
- [x] Manuel kabul case'leri `docs/manuel-test/32-DOKUMAN-KALITESI.md` içine
      eklendi (20 case, alan kodu `DKL`) ve `00-INDEKS.md`'ye işlendi.
      **Koşulanlar:** MT-DKL-005 (yalnız `index.mdx`, eski başlık sıfır),
      MT-DKL-007 (dört farklı görsel, dördü de `200`), MT-DKL-008–011 ve
      013–015, 017–019 (mutasyonla **kızardığı görüldü**; 14 mutasyonun 14'ü),
      MT-DKL-016 ve 020 (kapı zinciri temiz).
      **👤 elle koşulanlar:** MT-DKL-001 (açılış: işaret, dört sayı, sorun cümlesi
      ilk ekranda — hero 563 px, cümle 679 px'te), MT-DKL-002 (on sayfa, iki tema
      — okunmayan yüzey yok), MT-DKL-003 (dokuz sayfa 360 px'te —
      `scrollWidth - clientWidth = 0`, dokuzunda da), MT-DKL-004 (dokuz diyagram
      gerçek tarayıcıda render edildi, sözdizimi hatası yok, 5–12 düğüm),
      MT-DKL-006 (dizin → bölüm çözülüyor, 62 başlık yerinde),
      MT-DKL-012 (ilk `Tab` durağı "Skip to content", hedefi var, odak halkası
      görünür)
- [x] `faz-denetim` koşuldu; **11 bulgu** (1 🔴, 7 🟡, 3 🟢), 🔴 kapandı ve
      kalan 🔴 yok. Ayrıntı: [Denetim Bulguları](#denetim-bulguları)

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Neden |
|---|---|---|---|
| 1 | "37 elle yazılmış sayfa" | **39** sayfa | Plan Faz 75 kapanmadan ölçtü. Faz 75 `guides/coding-agents.md`'yi ekledi ve `http-api.md` kapının kör noktasındaydı (aşağıda, sapma 7). Kapanış sözleşmesi 39'un 38'inde uygulandı. |
| 2 | Kapanış sekiz sayfaya eklenir, sekiz kılavuz diyagram alır | **Dokuz** sayfaya kapanış, **dokuz** kılavuza diyagram | Planın "aday sekiz sayfa — hepsi 8 KB üstü" cümlesi ölçümle çelişiyordu: kendi listesindeki `external-agents` (7 181 B), `voice` (7 420 B) ve `openai-api` (6 901 B) eşiğin altındaydı, `inbound-triggers` (7 574 B) ise listede yoktu ama üçünden büyüktü. Eşik ölçülen dağılıma göre **6 500 B** kondu ve `inbound-triggers` de diyagram aldı. |
| 3 | `index.mdx` de `## Read next` ile biter | `index.mdx` kapıda **gerekçeli muaf** | Splash sayfasının son bölümü dört yollu "Choose your path" kart ızgarasıdır; altına üç madde koymak onu tekrar ederdi. Muafiyet `CLOSING_EXEMPT` içinde gerekçesiyle durur ve muaf sayfa kapanış kazanırsa kapı **kızarır** (`drop the exemption`). |
| 4 | Açık Soru 3: "az sayıda Starlight bileşeni geçersiz kılınır" | **Sıfır** bileşen geçersiz kılındı | Ölçüldü: prizma işareti Starlight'ın `logo:` yapılandırmasıyla, bölüm başına `og:image` ise `routeMiddleware` ile konuyor. İkisi de bileşen istemiyor. Starlight yükseltmeleri bu fazdan hiç pahalılaşmadı. |
| 5 | Diyagram paleti "açık/koyu temada okunaklı" | Palet **temadan bağımsız**; figürler iki temada da açık plaka üzerinde | Mermaid paletini çalışma anında JavaScript'te hesaplar ve bir CSS değişkenini çözemez, bu yüzden temaya tepki veren bir palet her tema düğmesinde yeniden render — ve bir yanıp sönme — isterdi. İki bağımsız kanıt bu yönü seçtirdi: (a) konsol ekran görüntüleri **açık** temadadır (ölçüldü: gösterge paneli ortalaması rgb(250, 249, 251)), koyu sayfada koyu bir diyagram yanındaki açık ekran görüntüsüyle çakışırdı; (b) mermaid kenar etiketini **%50 saydam** bir dikdörtgen üzerine çizer, yani etiketin gerçek zemini plakayla **karışımdır** — tema değiştiren bir plakada hiçbir tek etiket rengi iki karışımda birden AA geçemez. Sabit plakada karışım hep açıktır ve mürekkep hep koyudur. |
| 6 | Faz `src/**` ve `tests/**` altına dokunmaz (yalnız E2E ekran görüntüsü) | Üç entegrasyon testi altyapı dosyası değişti | Tam sürü koşumunda `AgentPrism.SqlServer.IntegrationTests` dört koşumun **ikisinde** 20–32 test düşürdü: her test sınıfı kendi şemasını kurup migration'ı eşzamanlılık denetimi olmadan uyguluyor, xunit sınıfları paralel koşturuyor ve `0017_approval_conditions` deadlock oluyordu. Kusur bu fazdan **önce** vardı (bu faz tek satır `.cs` değiştirmemişti) ama `dotnet test` kapısını kararsız bırakıyordu. **Kullanıcı kararıyla** düzeltildi: üç sağlayıcının test context'inde şema kurulumu `SemaphoreSlim(1, 1)` ile sıraya sokuldu. Kasıtlı eşzamanlılık testleri etkilenmez — onlar `Migrations.ApplyAsync()`'i doğrudan çağırır, kilitli `CreateAsync` yolundan geçmez. Sonuç: üç ardışık tam koşum 16/16 proje, 4408/4408 test. |
| 7 | Kapılar `check-content.mjs` içindedir | Ağırlık kapısı **ayrı** bir betiktir (`check-weight.mjs`) | Ağırlık `dist/` üzerinden ölçülür; `check-content.mjs` derlemeden **önce** koşar ve orada `dist/` yoktur. İkisini birleştirmek ya ucuz kapıyı pahalı derlemeye bağlardı ya da ölçümü sessizce atlardı. |
| 8 | (planda yok) | `check-content.mjs`'in **kör noktası** kapatıldı | `manualContent` süzgeci `join(docsRoot, 'http-api')` ile **ayırıcısız** ön ek karşılaştırması yapıyordu, bu yüzden elle yazılan `http-api.md` üretilen `http-api/` dizini sanılıyor ve **her** elle-sayfa denetimini atlıyordu (frontmatter, açıklama uzunluğu, kurulum komutu, diyagram erişilebilirliği). Ayırıcı eklendi; denetlenen sayfa 38 → **39**. |
| 9 | (planda yok) | İki sayfadan iç geliştirme referansı silindi | `guides/inbound-triggers.md` (`K-382`, "the same K2 rule"), `guides/model-providers.md` (`(F-59)`, `(K1)`). Faz 75'in kapattığı sınıfın **aynısı**, taramadığı bir yüzeyde. Kapı (iddia 10) artık elle yazılan sayfaları da tarıyor. |
| 10 | "Bu faz cümle silmez" | Dokuz **bağlantı** silindi (cümle silinmedi) | ≤3 kuralı planın kendi sözleşmesidir ve 13 sayfanın kapanışı üçe indirildi. Beş sayfada fazlalık, üretilen referansa giden bağlantılardı ve yeni `## In the reference` bölümüne **taşındı**. Yedi sayfada fazlalık prose bağlantısıydı ve kesildi: `background-work` → `http-api/`; `external-agents`, `openai-api` → `guides/reliability/`; `production` → `background-work/`, `reliability/`; `reliability` → `concepts/runs/`; `testing` → `guides/model-providers/`; `reference/compatibility` → `capabilities/`, `getting-started/security/`. Dokuzunun tamamı aynı sayfanın **gövdesinden** hâlâ erişilebilir; kaybolan bir hedef yok. |

## Bu Fazda Verilen Kararlar

Bu faz `docs/KARARLAR.md`'ye **üç** karar yazdı: **K-519** (doküman sitesinin
kapalı token kümesi ve kontrast kapısı), **K-520** (figürler temadan bağımsızdır),
**K-521** (kenar çubuğu üç tüketicinin paylaştığı tek modüldür). Gerekçeleri orada.

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, yalnız DoD + diff) **on bir** bulgu üretti ve
dördünü mutasyon testiyle ölçtü.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `check-content.mjs` → `sidebar.mjs` → `.gitignore`'daki üretilen JSON'a **statik import**; temiz checkout'ta `ERR_MODULE_NOT_FOUND` ile düşüyor ve yayın işini derlemeden önce öldürüyordu | **Düzeltildi** — üretilen dosyalar artık koşullu okunuyor. Doğrulandı: `src/generated/`, `api/`, `http-api/` kaldırılıp koşuldu, kapı geçti (`39 manual pages`) |
| 2 | 🟡 | `internal-history.pattern`'e eklenen `(?<!/)` kapıyı gereğinden geniş gevşetti; `../docs/KARARLAR.md` artık yakalanmıyordu | **Düzeltildi** — `(?<!https?://\S{0,200})` ile daraltıldı. Altı vaka ile doğrulandı; `.NET` tarafındaki `ShippedDocumentationSelfContainmentTests` yeşil |
| 3 | 🟡 | Kontrast kapısı yalnız `--ap-*`'ı ölçüyordu; kenar çubuğu/anahat/altbilgi metnini Starlight'ın **bağlanmamış** gri skalası boyuyordu | **Düzeltildi** — `--sl-color-gray-2/3` token'lara bağlandı. Canlı ölçüm: iki temada 5,98–11,46:1 |
| 4 | 🟡 | Kapanış normalize edilirken dokuz sayfa-içi bağlantı silindi | **Gerekçelendi** — sapma 10; dokuzunun tamamı aynı sayfanın gövdesinden erişilebilir |
| 5 | 🟡 | `build-social-images.mjs` spektrumu ve işareti elle kopyalıyordu | **Düzeltildi** — spektrum `site.css`'ten okunuyor; işaretin yeniden çizilme gerekçesi koda yazıldı. Çıktı bayt bayt aynı |
| 6 | 🟡 | Üç test dosyası fazın dosya listesinde yoktu | **Gerekçelendi** — sapma 6 |
| 7 | 🟡 | CI yorumu düzeltme iddia ediyordu ama `pages` işi PR'da koşmuyor | **Düzeltildi** — kapı `build` işine taşındı. Ölçüldü: betik `node_modules` silinmişken de koşuyor, `npm ci` istemiyor |
| 8 | 🟡 | `check-weight.mjs` çözülemeyen varlığı **sessizce 0** sayıyordu | **Düzeltildi** — artık hata verir ve dosyayı adıyla söyler |
| 9 | 🟢 | `http-api.md` eşiğin altındayken muafiyet listesindeydi | **Düzeltildi** — listeden çıkarıldı |
| 10 | 🟢 | `:root` dışında bildirilen renk token'ı kapıdan kaçıyordu | **Düzeltildi** — bildirim yeri artık kapının içinde |
| 11 | 🟢 | `CLOSING_EXEMPT` gerekçesi "ends with" diyordu, altında iki blok daha var | **Düzeltildi** — "last section is" |

Denetçinin **temiz** bulduğu başlıklar: 3.2 (test tiyatrosu — dört yeni iddiayı
12 mutasyonla kırdı, dördü de kızardı), 3.4 (`SchemaCreationLock` doğru; kasıtlı
eşzamanlılık testlerini bozmuyor), 3.8 (ürün yüzeyi). **Konu dışı**: 3.3, 3.5,
3.6 ve 3.7'nin çalışma anına bakan kısmı — bu faz `src/**` altında tek satır
değiştirmedi.

Denetçinin uyarısı, bulgu olarak değil ama kayda değer: **diyagram sözdizimi
derleme anında doğrulanmıyor**, çünkü mermaid tarayıcıda render eder. Bu oturumda
dokuz diyagram Playwright ile gerçek tarayıcıda render edilerek doğrulandı
(dokuzu da hatasız, 5–12 düğüm); kalıcı kanıt `MT-DKL-004`'tür ve 👤'dir.

## Sonraki Faza Devir Notu

1. 🚨 **`check-content.mjs` temiz bir checkout'ta koşabilmelidir.** Ona bir modül
   import ettirirken o modülün `.gitignore`'daki bir dosyaya statik bağımlılığı
   olmadığından emin ol. Ölçüldü: `src/generated/*.json`'a statik import kapıyı
   temiz klonda `ERR_MODULE_NOT_FOUND` ile düşürdü ve **denetim bulana kadar**
   yerelde yeşil göründü. Yerelde yeşil bir kapı, CI'da çalışan bir kapı değildir.
2. 🚨 **Bir kapı ekliyorsan CI'da hangi işte koştuğuna bak.** `pages` işi
   `github.event_name != 'pull_request'` koşulludur; oraya konan bir kapı hiçbir
   PR'ı durdurmaz. `check-content.mjs` yalnız `node:` yerleşikleri ve yerel dosya
   okur (ölçüldü: `node_modules` silinmişken koşuyor), bu yüzden `npm ci`
   olmadan `build` işinde koşabilir.
3. 🚨 **Ön ek karşılaştırmasında ayırıcıyı unutma.** `file.startsWith(join(root,
   'http-api'))` elle yazılan `http-api.md`'yi de yakalar ve o sayfa **her**
   denetimden sessizce muaf kalır. Bir yıl boyunca öyle kaldı; 38 sayfa denetlenip
   39 sayfa yayınlanıyordu.
4. 🚨 **Mermaid'in flowchart stil sayfası HER `.label`'ı `nodeTextColor` ile
   boyar — kenar etiketleri dâhil.** Kenar etiketi bir düğümün üzerinde değil,
   **plakanın** üzerinde durur (ve %50 saydam bir dikdörtgene çizilir, yani
   zemini plakayla karışımdır). Bu yüzden koyu dolgu + beyaz metin kutuların
   içinde okunur, aralarında **okunmaz**. Tek mürekkep rengi + açık dolgu ikisini
   birden çözer. İlk deneme bu yüzden geri alındı.
5. 🚨 **`astro-mermaid` kendi CSS'ini çalışma anında `document.head`'e ekler.**
   `[data-theme="dark"] pre.mermaid[data-processed]` seçicisi bizim
   `.sl-markdown-content pre.mermaid[data-processed]`'imizle **eşit** puanlıdır ve
   sonra geldiği için beraberliği kazanır. Plaka rengi bu yüzden özniteliği
   tekrarlayarak (`[data-processed][data-processed]`) yazıldı; `!important`
   seçilmedi çünkü o gelecekteki her düzeltmeyi de yener.
6. **Starlight `logo:` ve `routeMiddleware` bileşen geçersiz kılma istemez.**
   Prizma işareti `logo: { src: './public/favicon.svg' }` ile geliyor — dosya tek
   kopyadır, Astro derlemede hash'li bir kopya üretir. Bölüm başına `og:image`
   `src/starlightRouteData.mjs` içindedir. Bir sonraki görsel ihtiyacı için önce
   bu ikisine bak.
7. **`src/sidebar.mjs` üç tüketicinin tek kaynağıdır**: `astro.config.mjs`
   gezinmeyi, `starlightRouteData.mjs` paylaşım görselini, `check-content.mjs`
   erişilebilirlik ve görsel kapısını ondan okur. Kenar çubuğuna bölüm eklerken
   `sectionImages` de büyümelidir — kapı bunu zorlar.
8. **Ölçülen taban çizgileri.** Kontrast: metin **5,47:1**
   (`--ap-accent` / `--ap-accent-quiet`, açık tema), metin dışı **3,46:1**
   (`--ap-border-strong` / `--ap-surface-raised`, koyu tema). Sayfa ağırlığı: en
   ağır sayfa **49 376 B** gzip (`troubleshooting`), tavan **57 000 B**. Üçü de
   yalnız iyileşebilir; tavanı yükseltmek ölçümle gerekçelendirilir.
9. **İki ayrı bütçe kararı verildi; ikisini karıştırma.**
   (a) `docs/**.md` dizin bütçesi: Faz 75 bunu devretmişti. Ölçüldü — 106 KB
   boşluk vardı, bu fazın ihtiyacı ~31 KB'ydi. **Kullanıcı kararı: şimdilik
   dokunma.** Kapanış sonrası 4 920 206 / 5 000 000 (%2 boş). Faz 77 bunu
   yeniden sormalıdır.
   (b) `docs/hafiza/test-altyapisi.md` alan dosyası bütçesi **aşıldı**
   (16 126 / 16 000) çünkü dosya faz öncesinde zaten %94 doluydu. **Kullanıcı
   kararı: en eski on beş madde arşive.** 2026-08-01…03 aralığındaki maddeler
   (xunit.v3/VSTest, Testcontainers 4.13, Shouldly/Meziantou, erken Playwright)
   `docs/arsiv/HAFIZA-GECMISI.md`'ye taşındı — 4 626 B, silinmedi. Alan dosyası
   11 670 B'ye düştü ve başına yönlendirme satırı kondu. **Bir test tuzağı
   ararken artık iki yere grep at.**
