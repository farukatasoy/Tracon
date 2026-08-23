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

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-132\|K-228\|K-232\|K-413\|K-415" docs/KARARLAR.md
   ```
   **K-415** (ürün dokümantasyonu ayrı bir Astro Starlight sitesidir, İngilizce'dir
   — bu faz o kararı uygular, yeniden açmaz), **K-132** (arayüzde `mermaid.js`
   **reddedildi**: ~100 KB gzip. 🚨 Bu karar **konsol** içindir; doküman sitesi
   ayrı bir yayın hattıdır ve mermaid'i zaten kullanır — karar yeniden
   açılmıyor, sınırı hatırlatılıyor), **K-413** (üretilen dosya elle yazılmaz —
   `api/`, `http-api/` ve harita bu fazda **elle düzenlenmez**), **K-228** ve
   **K-232** (arayüz sözlüğü ve sunucu yanıtı — bu faz ikisine de dokunmaz)
3. [`75-TUKETICI-DOKUMAN-DOGRULUGU.md`](75-TUKETICI-DOKUMAN-DOGRULUGU.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md
   ```
   Bu faz onun kapılarının **üstüne** yazar. Faz 75'in eklediği beş içerik
   iddiası hâlâ koşuyor olmalıdır; bu fazın hiçbir düzenlemesi onları
   gevşetemez.
4. Alan hafızası:
   [`hafiza/frontend.md`](../../hafiza/frontend.md) (yalnız ekran görüntüsü üreten
   E2E testine dokunulacaksa) · Faz 75 bir `hafiza/dokumantasyon.md` açtıysa **o**
5. Gerektiğinde: [`docs-site/README`](../../../docs-site/package.json) yerine doğrudan
   `astro.config.mjs` — kenar çubuğu ve bileşen sözleşmesi oradadır

---

## Amaç

Faz 75 dokümanın **doğru ve eksiksiz** olmasını sağladı. Bu faz onu **iyi**
yapar.

Ölçüm net bir tablo çiziyor: içerik yeterli, sunum değil. 37 elle yazılmış sayfa
ve 371 KB anlatı var — kapsam sorunu yok. Ama sayfaların **21'i** ne bir
diyagram ne bir görsel taşıyor; kapanış bölümü **üç farklı adla** yazılmış ve
sekiz sayfada hiç yok; açılış sayfası ürünün en büyük iddiasını **4,7 KB**'da
anlatıyor; ve markanın kendi prizma işareti `favicon.svg` ile konsolda
yaşıyorken sitenin başlığında **yok**.

Bu bir "tema seçme" fazı değildir. Marka **zaten var** — spektrum gradyanı,
prizma işareti, sakin ürün yüzeyi. Ölçüldü: `site.css` 163 satır ve 21 sınıf
taşıyor; bu bir stok tema değil, **eksik bırakılmış** bir tasarım katmanıdır.
Faz onu tamamlar.

- **F-127** — Doküman sitesi tek bir tasarım sistemine oturur, her sayfa aynı
  okuma sözleşmesini izler ve nitelikler ölçülebilir kapılara bağlanır.

Kapsam dışı: yeni içerik yazmak. Bir sayfa eksikse o Faz 75'in işidir. Bu faz
**var olan** içeriği yeniden düzenler, görselleştirir ve açar.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -LE '```mermaid\|<img \|!\[' <37 sayfa>` | **21 sayfada** ne diyagram ne görsel var. Aralarında en uzun kılavuzlar da var: `guides/reliability.md` (14 771 B), `guides/production.md` (14 812 B), `guides/background-work.md` (12 768 B), `guides/observability.md` (12 720 B) |
| `grep -l "^## Read next"` / `"^## Related"` / `"^## Next$"` | Aynı iş **üç adla** yapılıyor: **19** sayfa "Read next", **7** sayfa "Related", **5** sayfa "Next". **8** sayfada hiç yok. Okur her sayfanın sonunda farklı bir sözleşmeyle karşılaşıyor |
| [`index.mdx`](../../../docs-site/src/content/docs/index.mdx) | **4 698 bayt** — 37 sayfanın en küçüklerinden biri. Ürünün tek satışa dönük yüzeyi bu sayfadır; `troubleshooting.md` ondan **beş kat** büyüktür |
| [`astro.config.mjs`](../../../docs-site/astro.config.mjs) | `logo:` anahtarı **yok** — başlık düz metindir. Oysa [`public/favicon.svg`](../../../docs-site/public/favicon.svg) (814 B) gerçek bir prizma işareti taşıyor ve yorumu "the same mark the console uses" diyor; konsol onu [`layout.tsx`](../../../src/AgentPrism.UI/frontend/src/components/layout.tsx) içinde kullanıyor |
| `astro.config.mjs` `head:` bloğu | **Tek bir `og:image`** bütün site için: `screenshots/dashboard.png`. Paylaşılan her bağlantı — kavram sayfası, HTTP referansı, sorun giderme — aynı görünüyor |
| [`site.css`](../../../docs-site/src/styles/site.css) | **163 satır, 26 CSS değişkeni, 21 sınıf.** Spektrum motifi (`--ap-spectrum`) yalnız **bir** yerde kullanılıyor: `.site-title::after` |
| `mermaid({ theme: 'neutral', autoTheme: true })` | 22 diyagramın hepsi Mermaid'in stok `neutral` paletiyle çiziliyor. Marka spektrumuyla ilişkisi yok |
| [`troubleshooting.md`](../../../docs-site/src/content/docs/troubleshooting.md) | **23 873 bayt, 62 `###` alt başlığı, 14 `##` bölümü.** Sitenin en büyük sayfası ve tek gezinme yardımı Starlight'ın sağ kenar listesidir |
| `reference/configuration.md` | **22 616 bayt** — ikinci en büyük. Faz 75 buna 13 üye daha ekliyor |
| `public/screenshots/` | **14 görüntü** (Faz 75 bunu 19'a çıkarır). Hepsi tek temada, tek çözünürlükte, `2880×1800` |
| `grep -lE "<img\|!\[" <37 sayfa>` | Görsel taşıyan sayfa sayısı: **1** (`ui.md`). `index.mdx` görselini bir Astro bileşeniyle koyar |

> Kanıtlar 2026-08-20 tarihinde doğrulandı.

🚨 **"Stok tema" teşhisi yanlıştır ve plan bunu ölçümle düzeltir.** İlk bakışta
site Starlight varsayılanı gibi görünüyor; `site.css` ölçülünce 21 sınıflık
bir ürün katmanı çıkıyor. Doğru teşhis: **kimlik var, uygulanmamış.** Bu, işi
küçültür — yeni bir dil icat edilmeyecek, var olan dil bütün sayfalara
yayılacak.

---

## 76.1 — Marka: var olan prizma motifini tamamlamak

Üç öğe zaten var: prizma işareti (`favicon.svg`), spektrum gradyanı
(`--ap-spectrum`) ve sakin bir renk paleti (`--sl-color-accent`). Faz bunları
bir sisteme çevirir.

```mermaid
flowchart TD
    accTitle: Var olan marka ogelerinin yayilmasi
    accDescr: Prizma isareti ve spektrum gradyani bugun yalniz favicon ve baslik altinda kullaniliyor; faz onlari basliga, diyagramlara, paylasim gorsellerine ve bilesenlere yayar.
    MARK["Prizma isareti<br/>favicon.svg · konsol"] --> HDR["Site basligi"]
    MARK --> OG["Paylasim gorseli"]
    SPEC["Spektrum gradyani<br/>--ap-spectrum"] --> HDR
    SPEC --> DIAG["Mermaid paleti"]
    SPEC --> COMP["Kart ve vurgu bilesenleri"]
    TOK["Renk · tipografi · aralik token'lari"] --> COMP
    TOK --> DIAG
```

| İş | Bugün | Sonra |
|---|---|---|
| Site başlığı | Düz metin + spektrum alt çizgisi | Prizma işareti + kelime markası; işaret tek kaynaktan gelir |
| Renk | 26 değişken, çoğu Starlight'ın kendi adları | Kapalı bir token kümesi: yüzey, metin, vurgu, uyarı, kod — açık ve koyu temada **ikisi de tanımlı** |
| Tipografi | Starlight varsayılanı | Ölçek tanımlanır (başlık, gövde, kod); satır uzunluğu `--sl-content-width` ile hizalanır |
| Spektrum | Bir yerde | Bölüm ayracı, aktif gezinme, diyagram vurgusu — **ölçülü**; her yerde kullanılırsa anlamını yitirir |
| Mermaid | Stok `neutral` | Token'lardan türeyen palet; açık/koyu temada okunaklı |

🚨 Renk kararı **erişilebilirlikten** türetilir, tersinden değil. Her metin/zemin
çifti WCAG AA eşiğini (normal metin 4.5:1) geçmelidir ve bu bir kapıya bağlanır
([§76.7](#767--erişilebilirlik-ve-ağırlık-kapıları)). Spektrum gradyanı
**metin arkasında kullanılamaz**.

---

## 76.2 — Açılış sayfası

`index.mdx` bugün doğru şeyleri söylüyor ve kısa kesiyor: dört ölçülmüş sayı,
dört kart, bir kod bloğu, bir ekran görüntüsü, dört yol.

Eksik olan, okurun **kendi durumunu** tanıması. Sayfa ürünü anlatıyor; okurun
sorununu değil. Üç ekleme:

1. **Sorun cümlesi.** "MAF ile bir agent yazdınız. Şimdi onu üretimde
   işletmeniz gerekiyor" — okur ilk ekranda kendini tanımalı.
2. **Ne yaptığını gösteren tek bir akış.** Bir agent tanımından bir `run`
   kaydına giden kısa bir görsel şerit; bugün bunu yalnız `capabilities.md`
   içindeki diyagram yapıyor ve oraya gitmek gerekiyor.
3. **Sınırın açıkça yazılması.** "Bir kütüphanedir, barındırılan bir servis
   değildir" ve dört tasarım kuralı bugün `getting-started/index.md`'de.
   Açılışta bir satırla durmalı — dürüstlük bir satış argümanıdır.

Dört ölçülmüş sayı **kalır** ve kapıya bağlı kalır
([`check-content.mjs:97-105`](../../../docs-site/scripts/check-content.mjs#L97-L105)).
Pazarlama dili girmez; sayı ve sınır, sıfat değil.

---

## 76.3 — Sayfa sözleşmesi

Okur bir sayfayı açtığında ne bulacağını bilmelidir. Bugün üç farklı kapanış
ve iki farklı açılış deseni var (bazı kılavuzlar "Mental model:" ile açıyor,
bazıları doğrudan işe giriyor).

Tek sözleşme:

| Konum | Ne | Zorunlu mu |
|---|---|---|
| Başlık altı | `description` — bugün zaten 70–180 karakter kapısında | ✅ (var) |
| İlk paragraf | Bu sayfanın hangi soruyu cevapladığı | ✅ |
| Gövde | İşin kendisi | ✅ |
| Son bölüm | **`## Read next`** — üç bağlantıdan fazla değil | ✅ |

"Related" ve "Next" başlıkları `Read next` olur. Sekiz sayfa yeni bir kapanış
kazanır. Kapanışsız bir sayfa okuru çıkmaza sokar; bu ölçülebilir ve kapıya
bağlanır ([§76.8](#768--kapılar)).

🚨 Bu bir yeniden yazma turu **değildir**. Cümleler korunur; başlık adı,
sıralama ve eksik kapanışlar düzeltilir. `check-content.mjs`'in mevcut
iddiaları (frontmatter, açıklama uzunluğu, kenar çubuğu erişilebilirliği)
zaten yeşildir ve yeşil kalmalıdır.

---

## 76.4 — Diyagram borcu

Yirmi bir sayfada görsel yok. Hepsine diyagram koymak yanlış olur — bir tablo
sayfası diyagram istemez. Kural şudur:

> Bir sayfa bir **akış**, bir **karar** veya bir **katman** anlatıyorsa, onu
> bir diyagram anlatmalıdır. Bir **liste** anlatıyorsa tablo yeterlidir.

Ölçülen aday sekiz sayfadır — hepsi 8 KB üstü ve hepsi akış anlatıyor:

| Sayfa | Boyut | Ne anlatmalı |
|---|---|---|
| `guides/reliability.md` | 14 771 B | Arıza sınırları: sağlık → yedek zincir → devre kesici → uzlaştırma |
| `guides/production.md` | 14 812 B | Süreç topolojisi: API · worker · tek yürütücü · store |
| `guides/background-work.md` | 12 768 B | Kuyruk yaşam döngüsü: kayıt → seçim → çalıştırma → yeniden deneme |
| `guides/observability.md` | 12 720 B | `span` ağacı ve metriklerin nereden doğduğu |
| `guides/testing.md` | 10 487 B | Test seviyeleri: `FakeModelProvider` → `AgentPrismTestHost` → E2E |
| `guides/external-agents.md` | 6 767 B | İki yön: tüketilen MCP · yayımlanan MCP/A2A |
| `guides/openai-api.md` | 6 901 B | İki uyumluluk yüzeyi ve durum sahipliği |
| `guides/voice.md` | 7 420 B | Ses yolu: tool'lar · gerçek zamanlı oturum |

Her diyagram **Mermaid**'dir (repo kuralı) ve `accTitle` + `accDescr` taşır —
bu zaten kapıdadır
([`check-content.mjs:214-221`](../../../docs-site/scripts/check-content.mjs#L214-L221)).

Kalan on üç sayfa (`reference/*`, `packages.md`, `http-api.md`, kısa başlangıç
sayfaları) **tablo sayfasıdır** ve diyagram almaz. Kapı bu ayrımı bilir:
eşik boyutun üstündeki sayfa ya bir diyagram taşır ya bir muafiyet listesinde
durur ([§76.8](#768--kapılar)).

---

## 76.5 — `troubleshooting.md` yeniden düzenlenir

23 873 bayt, 62 alt başlık, tek sayfa. Sorun giderme sayfası **taranarak**
okunur, baştan sona değil; bugünkü tek yardım sağ kenardaki başlık listesidir.

İki seçenek ölçüldü ve **birincisi öneriliyor**:

- **A — Tek sayfa kalır, gezinmesi güçlenir.** Başa bir belirti dizini
  (semptom → bölüm bağlantısı) konur; 14 `##` bölümü daraltılabilir gruplara
  ayrılır. `Ctrl+F` çalışmaya devam eder ve tek URL korunur.
- **B — Alt sayfalara bölünür.** Her bölüm kendi sayfası olur. Arama iyileşir
  ama tek sayfada arama biter ve 14 yeni URL doğar.

**A**, çünkü sorun giderme sayfasının en çok kullanılan özelliği tarayıcı
aramasıdır ve bölmek onu öldürür. Sayfanın başındaki mevcut "A fast diagnostic
order" bölümü bu dizinin doğal yeridir.

`reference/configuration.md` (22 616 B, Faz 75 sonrası daha büyük) aynı
tedaviyi alır: bölüm indeksi zaten var (`## Section index`), eksik olan her
bölümün başında **hangi paketin** o bölümü okuduğudur.

---

## 76.6 — Paylaşım yüzeyi

Bugün her sayfa aynı `og:image` ile paylaşılıyor: konsol gösterge paneli. Bir
kavram sayfasının bağlantısını paylaşan okur, ekran görüntüsü gören biri için
yanlış beklenti üretir.

Bölüm başına bir görsel yeterlidir — sayfa başına üretim gerekmez:

| Bölüm | Görsel |
|---|---|
| Start here · Build agents | Prizma işareti + ürün adı |
| The console | Gösterge paneli (bugünkü) |
| Operate in production | Bir `run` detay ekranı |
| Reference · HTTP API | Şema/tablo motifi |

Görseller **statiktir** ve `public/` altında durur. Derleme anında üretim
([Açık Soru 2](#açık-sorular)) yeni bir yayın hattı borcudur ve bu fazın
kapsamında değildir.

---

## 76.7 — Erişilebilirlik ve ağırlık kapıları

Yeni tasarımın iki bedeli vardır ve ikisi de ölçülür.

**Kontrast.** Her token çifti WCAG AA eşiğini geçer. Ölçüm derleme anında
yapılır: `site.css`'teki token'lar okunur, çiftler hesaplanır, eşiği geçmeyen
**hata** verir. Bu, `check-content.mjs`'in "koddan ölç" desenidir ve tarayıcı
gerektirmez.

**Ağırlık.** Bugün ölçülmemiş bir sayı yok — çünkü hiç ölçülmemiş. Faz bir
taban çizgisi kurar: bir doküman sayfasının HTML + CSS + JS ağırlığı
(mermaid parser hariç, çünkü o zaten talep üzerine yükleniyor ve
[`astro.config.mjs:20-22`](../../../docs-site/astro.config.mjs#L20-L22) bunu
gerekçelendiriyor). Sınır **ilk ölçümden sonra** konur — plan sayı uydurmaz.

🚨 Font kararı ağırlığın en büyük tek kalemidir. Web fontu **eklenirse**
`font-display: swap` ve alt küme zorunludur; eklenmezse sistem yığını kalır ve
ağırlık değişmez. Bu bir açık sorudur ([Açık Soru 1](#açık-sorular)).

---

## 76.8 — Kapılar

Faz 75'in beş içerik iddiasının üstüne dört sunum iddiası eklenir. Hepsi
`check-content.mjs` içindedir ve tarayıcı gerektirmez.

| # | İddia | Neden ölçülebilir |
|---|---|---|
| 6 | Her elle yazılmış sayfa bir `## Read next` bölümüyle biter | Başlık taraması |
| 7 | Eşiği aşan her anlatı sayfası en az bir diyagram veya görsel taşır; muafiyet listesi açıkça yazılır | Boyut + içerik taraması |
| 8 | `site.css` token çiftleri WCAG AA kontrastını geçer | Token ayrıştırma + kontrast hesabı |
| 9 | Her kenar çubuğu bölümünün bir `og:image`'ı var ve dosya diskte | Yapılandırma + dosya varlığı |

Muafiyet listesi bir kaçış kapısı değildir: içinde duran her sayfa için **neden**
yazılır ve `faz-denetim` listenin büyümesini 🔴 sayar — Faz 73 ve 74'ün taban
çizgisi kuralının aynısı.

---

## Planlanan Public API

**Yok.** Bu faz `src/**` altındaki hiçbir dosyayı değiştirmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Konsol bundle'ı **değişmez** — bu faz `AgentPrism.UI` frontend koduna dokunmaz.
Doküman sitesinin ağırlığı ayrı ölçülür ([§76.7](#767--erişilebilirlik-ve-ağırlık-kapıları));
o bundle pakete girmez ve 250 KB konsol bütçesiyle ilgisi yoktur.

---

## Planlanan Dosya Listesi

```
docs-site/
├── astro.config.mjs                     (degisir — logo, bolum basina og:image, mermaid paleti)
├── src/styles/site.css                  (degisir — token kumesi, bilesen sinirlari)
├── src/components/                      (YENI — Starlight bilesen gecersiz kilmalari)
│   └── <az sayida, gerektigi kadar>
├── src/assets/prism-mark.svg            (YENI — favicon ile ayni kaynak, tek kopya)
├── public/social/<bolum>.png            (YENI — dort paylasim gorseli)
├── scripts/check-content.mjs            (degisir — dort yeni iddia)
└── src/content/docs/
    ├── index.mdx                        (degisir — acilis)
    ├── troubleshooting.md               (degisir — belirti dizini)
    ├── reference/configuration.md       (degisir — bolum basi isaretleri)
    ├── guides/{reliability,production,background-work,observability,
    │           testing,external-agents,openai-api,voice}.md   (degisir — diyagram)
    └── <20 sayfa>                       (degisir — kapanis bolumu tekleseme)

docs/manuel-test/32-DOKUMAN-KALITESI.md  (YENI)
docs/manuel-test/00-INDEKS.md            (degisir — satir 32)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Yeni sayfa kapanış bölümü olmadan eklenir | CI | `check-content.mjs` iddia 6 |
| Uzun bir kılavuz diyagramsız kalır | CI | `check-content.mjs` iddia 7 |
| Muafiyet listesi sessizce büyür | CI + denetim | `check-content.mjs` + `faz-denetim` |
| Yeni bir renk token'ı kontrastı düşürür | CI | `check-content.mjs` iddia 8 |
| Koyu temada bir yüzey tanımsız kalır ve okunmaz olur | CI + **Manuel** | iddia 8 (token çifti) + 👤 iki temada göz denetimi |
| Kenar çubuğuna bölüm eklenir, paylaşım görseli eklenmez | CI | `check-content.mjs` iddia 9 |
| Diyagram `accTitle`/`accDescr` taşımaz | CI | `check-content.mjs` (mevcut iddia) |
| Mermaid paleti koyu temada okunmaz | **Manuel** 👤 | Görsel denetim, iki tema |
| Yeni bileşen mobilde taşar | **Manuel** 👤 | 360 px genişlikte denetim |
| Ekran görüntüsü tema değişikliğinden sonra bayatlar | **E2E** | `DocumentationScreenshotTests` |
| Tasarım değişikliği Faz 75'in içerik kapılarını gevşetir | CI | Faz 75 iddiaları koşmaya devam eder |

Beş soru:

| Soru | Cevap |
|---|---|
| İptal | Konu dışı — çalışma anına dokunulmuyor |
| Eşzamanlılık | Konu dışı |
| Boş/aşırı girdi | Muafiyet listesindeki bir sayfa silinirse kapı **kızarır** (bayat liste), sessizce geçmez |
| Başka kiracı | Konu dışı |
| Alt sistem hatası | Kontrast hesabı bir token'ı çözemezse **hata verir**, iddiayı atlamaz |

🚨 Bu fazın çıktısının bir kısmı **göz gerektirir** ve bu dürüstçe yazılır.
Kontrast hesaplanabilir; "okunaklı" hesaplanamaz. Manuel case'ler 👤 ile
işaretlenir ve `faz-tamamlama` onları atlamaz.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Yayınlanan site | Açılış sayfasını aç | Prizma işareti başlıkta; dört ölçülmüş sayı yerinde; sorun cümlesi ilk ekranda | 👤 |
| 2 | Aynı | Temayı koyuya çevir, on sayfa gez | Hiçbir yüzey okunmaz hâle gelmiyor; spektrum yalnız ayraçlarda | 👤 |
| 3 | Aynı | Tarayıcıyı 360 px genişliğe daralt | Tablo ve kod blokları kendi içinde kayıyor; sayfa gövdesi yatay kaymıyor | 👤 |
| 4 | Aynı | Sekiz kılavuzu tek tek aç | Sekizinde de diyagram var ve iki temada da okunuyor | 👤 |
| 5 | Aynı | Rastgele on sayfanın sonuna in | Onunda da `Read next` var ve en fazla üç bağlantı taşıyor |
| 6 | Aynı | `troubleshooting` sayfasını aç | Başta belirti dizini var; bir belirtiye tıklamak doğru bölüme gidiyor; `Ctrl+F` hâlâ tüm sayfayı buluyor | 👤 |
| 7 | Aynı | Dört bölümden birer sayfayı bir sohbete/sosyal ağa yapıştır | Dört farklı önizleme görseli çıkıyor | 👤 |
| 8 | Bir sayfadan `## Read next` bölümünü sil | `npm run check:content` | Kızarır ve sayfayı adıyla söyler |
| 9 | `site.css`'e kontrastı düşük bir token çifti ekle | Aynı | Kızarır ve oranı yazar |
| 10 | Kenar çubuğuna görselsiz bir bölüm ekle | Aynı | Kızarır |
| 11 | Uzun bir kılavuzdan diyagramı sil | Aynı | Kızarır veya muafiyet gerekçesi ister |
| 12 | Klavye ile gez | `Tab` ile başlıktan içeriğe | "Skip to content" çalışıyor; odak halkası her yerde görünür | 👤 |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Web fontu eklensin mi | A: Hayır, sistem yığını · B: Evet, tek aile, alt kümelenmiş | **A** ile başla, tipografi ölçeğini kur, **sonra** ölç. Sistem yığını sıfır ağırlıktır ve .NET okurunun makinesinde tutarlı görünür. Font ancak ölçek yetersiz kalırsa açılır |
| 2 | Paylaşım görselleri üretilsin mi | A: Dört statik dosya · B: Sayfa başına derleme anında üretim | **A.** B yeni bir üretim hattı, yeni bir bağımlılık ve K-413 kapsamında yeni bir "üretilen dosya" borcudur. Dört görsel bugünkü tek görselden dört kat iyidir |
| 3 | Starlight bileşenleri geçersiz kılınsın mı | A: Yalnız CSS · B: Az sayıda bileşen (başlık, açılış kartları) · C: Geniş geçersiz kılma | **B.** Yalnız CSS ile prizma işareti başlığa konamaz. Geniş geçersiz kılma Starlight yükseltmelerini pahalı yapar; sınır "kaçınılmaz olanlar" olmalı |
| 4 | `troubleshooting.md` bölünsün mü | A: Tek sayfa + belirti dizini · B: Alt sayfalar | **A** — [§76.5](#765--troubleshootingmd-yeniden-düzenlenir)'te gerekçelendirildi |
| 5 | Ekran görüntüleri koyu temada da üretilsin mi | A: Hayır, tek tema · B: Evet, çift set | **A.** Çift set 19 yerine 38 görüntü ve iki kat bakım demektir; kazanç kozmetiktir. Ölçüm gösterirse (koyu tema kullanım oranı) yeniden açılır |
| 6 | Sayfa ağırlığı sınırı ne olmalı | — | **Ölçülmedi.** İlk ölçümden sonra, ölçülen değere pay eklenerek konur. Plan sayı uydurmaz |

---

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

## Riskler

| Risk | Önlem |
|------|-------|
| Tasarım turu içeriği bozar — cümleler "sadeleştirilirken" bilgi düşer | Bu faz **yeni içerik yazmaz ve cümle silmez**; başlık adı, sıralama ve görselleştirme yapar. `faz-denetim` `git diff`'te silinen cümleleri arar |
| Faz 75'in kapıları bu fazın düzenlemeleriyle kızarır ve gevşetilme baskısı doğar | DoD'de açık satır: beş iddia yeşil kalmalı. Kızaran kapı **düzenlemenin** kusurudur, kapının değil |
| Starlight bileşen geçersiz kılmaları sonraki yükseltmeleri pahalı yapar | Açık Soru 3 sınırı "kaçınılmaz olanlar" olarak çiziyor; her geçersiz kılma için gerekçe yazılır |
| Görsel iş öznel bir tartışmaya dönüşür ve faz kapanmaz | DoD'nin **on maddesi ölçülebilir**; öznel olanlar 👤 manuel case'lerdir ve "koştu/koşmadı" ile kapanır |
| Mermaid paleti markayla uyumlu ama okunmaz olur | Kontrast kuralı diyagram renklerini de kapsar; iki temada manuel case (MT 4) |
| Sayfa ağırlığı sessizce büyür | Faz bir taban çizgisi kurar; sınır ölçülen değere göre konur ve `check-content.mjs` zorlar |
| `troubleshooting.md` dizini bayatlar — bölüm eklenir, dizine yazılmaz | Dizin bağlantıları başlıklara çözülür; kapı çözülmeyen bağlantıyı yakalar (`check-links.mjs` site içi çapaları görür) |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

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

## Gerçekleşen Public API

**Büyümedi.** Ölçüldü: `git diff HEAD --stat` içinde `src/**` altında tek bir
`.cs` dosyası yok. `dotnet pack` 0 uyarı verdi.

## Dosya Listesi (gerçekleşen)

```
docs-site/
├── astro.config.mjs                (degisti — logo · mermaid base paleti · routeMiddleware · sidebar importu)
├── package.json                    (degisti — check:weight, check zinciri)
├── src/styles/site.css             (degisti — 163 -> 447 satir; token kumesi, tipografi, bilesenler)
├── src/sidebar.mjs                 (YENI — kenar cubugu + bolum gorseli, uc tuketici okur)
├── src/starlightRouteData.mjs      (YENI — bolum basina og:image)
├── scripts/check-content.mjs       (degisti — bes yeni iddia + iki sikilastirma)
├── scripts/check-weight.mjs        (YENI — gzip sayfa agirligi tavani)
├── scripts/build-social-images.mjs (YENI — dort paylasim gorseli, bir kez kosar)
├── scripts/internal-history.pattern(degisti — URL icindeki docs/ artik eslesmiyor)
├── public/social/{overview,console,operate,reference}.png   (YENI)
└── src/content/docs/               (24 sayfa degisti — kapanis · diyagram · acilis · dizin)

.github/workflows/ci.yml            (degisti — icerik kapisi PR gorunur ise tasindi, agirlik kapisi eklendi)

tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/
└── Infrastructure/*TestContext.cs  (degisti — SchemaCreationLock; sapma 6)

docs/manuel-test/32-DOKUMAN-KALITESI.md  (YENI — 20 case, alan kodu DKL)
docs/manuel-test/00-INDEKS.md            (degisti — satir 32)
docs/KARARLAR.md                         (degisti — K-519, K-520, K-521)
docs/hafiza/dokumantasyon.md             (degisti — sunum kapilari ve mermaid tuzaklari)
docs/hafiza/test-altyapisi.md            (degisti — deadlock notu; 15 eski madde arsive)
docs/arsiv/HAFIZA-GECMISI.md             (degisti — o 15 madde, 4 626 B)
```

Toplam: 39 dosya değişti, 6 dosya eklendi (`+1368 / -262`).

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
