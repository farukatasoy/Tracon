# docs-site Yayin Hatti ve Temasi

> Siteyi YAYINLAMA (`site-deploy.sh`, K-542) ve Starlight temasi (logo,
> `og:image`, baslik sarmalayicilari). Dokuman KAPILARI ve ureteclleri icin:
> [`dokumantasyon.md`](dokumantasyon.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: bu tek bolum `dokumantasyon.md`'nin %31'ine ulasmisti ve
> sonraki en buyuk bolumun DORT katiydi -- `MIMARI.md` §7 ile ayni sekil
> (K-524). Yayin/tema ekseni kapi ekseninden BAGIMSIZ buyuyor.
## Starlight: logo ve bolum basina og:image bilesen ISTEMEZ

Prizma isareti `logo: { src: './public/favicon.svg' }` ile gelir — dosya tek
kopyadir, Astro derlemede hash'li bir kopya uretir. Bolum basina `og:image`
`routeMiddleware` ile yazilir (`src/starlightRouteData.mjs`); `starlightRoute.head`
dizisi orada degistirilebilir. Faz 76 sifir bilesen gecersiz kildi.

🚨 Bir de sasirtan yer: Starlight basliklari `.sl-heading-wrapper.level-h2` icine
**sarar** (cengel baglantilari aciksa). `.sl-markdown-content > h2` seciciisi
hicbir seyi eslemez.
- **`docs-site/` yayin hatti** (AGENTS.md'den, Faz 77): site `dotnet build`'e BAGLANMAZ,
  pakete GIRMEZ, Node **22.12+** ister. `api/` ve `http-api/` sayfalari URETILIR
  (`npm run generate`) ve commit EDILMEZ; ekran goruntuleri E2E kosumundan uretilir
  (`TRACON_UI_SCREENSHOTS=1`) ve commit EDILIR.
- **🚨 Yayin GitHub Pages DEGILDIR (K-542).** Site `tracon.dev` adresinde
  kendi sunucumuzda barinir; yayini `scripts/site-deploy.sh` yapar (derleme → dort kapi →
  rsync → `docker compose up -d`). CI'nin `site` isi YAYINLAMAZ, yalnizca derler ve
  kapilari kosar.
- **🚨 Sunucuda nginx/Caddy servisi YOKTUR — Traefik vardir.** :80 ve :443'u Docker
  dinler; yonlendirme Docker LABEL'iyle yapilir. Site bir `nginx:1.27-alpine`
  konteyneridir ve yapilandirmasi `docs-site/deploy/` altinda REPO'dadir. Elle
  sunucuda duzenleme yapma — script her yayinda compose dosyasini da gonderir.
- **macOS'ta `rsync` `--chmod` KABUL ETMEZ.** Apple `openrsync` sevk eder
  (`rsync version 2.6.9 compatible`); bayrak `invalid argument` verir. Derleme
  zaten 644/755 uretiyor, `-a` onu korur.
- **🚨 Adres TEK dosyada yasar: `docs-site/site.config.mjs`.** `site`, `base` ve
  `formerHosts` oradan gelir; sekiz tuketici onu import eder. Bir adres literali
  ELLE yazilirsa `check-content.mjs` 11. kontrolu kizarir — hem yeni bir kopya
  dogdugunda hem de `formerHosts`'taki eski bir barindiriciya isaret edildiginde.
  C# tarafi ayri dildir (`DocumentationLinks`); ikisini `DiagnosticIntegrityTests`
  bagli tutar — o test `site.config.mjs`'i OKUR.
- **`base` `/`dir ve oyle kalir.** El yazisi sayfalar kok-goreli baglanti yazar
  (`/guides/production/`). Alt yola donulurse 39 sayfadaki 224 baglanti da
  guncellenmelidir; `check-links.mjs` bunu yayindan once kizartir.
- **Dil sinirinin kapsadigi yuzeyler** (AGENTS.md'den, Faz 77): kod, yorum, XML dokumani,
  `exception`/log/`ProblemDetails` metni, migration `.sql` yorumu, `template.json`
  aciklamasi — hepsi Ingilizce'dir.
- **Mermaid diyagram tipi ve yazim tuzaklari** (AGENTS.md'den, Faz 77): katman/akis/karar
  agaci → `flowchart TD|LR` · cagri sirasi → `sequenceDiagram` · veri modeli → `erDiagram` ·
  durum makinesi → `stateDiagram-v2` · zaman plani → `gantt`. Turkce etiket serbest, teknik
  terim orijinal dilinde kalir (`AIAgent`). Dugum metninde `(`, `)`, `,`, `:` ayristiriciyi
  bozar — tirnak kullan. Tek fikir anlatir; on bes dugumu asiyorsa ikiye bol.
- **🚨 Arsivleme iki yonlu baglanti kirar; SADECE tasinan dosyanin kendi linklerini
  duzeltmek YETMEZ** (Faz 77): bir blogu `docs/X.md`'den `docs/arsiv/Y.md`'ye
  KOPYALADIGINDA o blogun ICINDEKI goreli linkler hâlâ `docs/`'a goredir ve arsiv
  dizininden cozulmez. Faz 77'de 17 baglanti boyle kirildi (`KARARLAR-GECMISI.md`,
  `PLANA-DONUSEN-ADAYLAR.md`). Kural: tasima sonrasi `docs/arsiv/**` icinde
  "buradan cozulmuyor ama `docs/`'tan cozuluyor" olan her linki yeniden tabanla.
- **🚨 Faz dokumani yeniden adlandirilirken/tasinirken duz `sed` KULLANMA** (Faz 77):
  `docs/manuel-test/` faz dokumanlariyla AYNI `NN-AD.md` desenini kullanir; metin
  eslemesi onlari da bozar. Yeniden yazma COZUMLEMEYE dayanmalidir — link once
  dosyanin ESKI dizinine gore cozulur, tasima haritasindan gecirilir, sonra YENI
  dizine gore gorelilestirilir. 60 faz dosyasinin 874 atifi boyle tasindi.
- **Butce raporu "✅" derken bile DAR bandina bak** (Faz 77): `dokuman-bakim.py`
  yalniz ASIM'da kirmizi verir. Faz 77 oncesi hafiza dongusunde DAR bandi HIC yoktu
  ve 16000/16000 bir dosya "ok" yaziyordu. `--projeksiyon` kalan faz sayisini basar;
  yeni bir sinir koyarken OLCULEN boyuta %15 bosluk ekle (Faz 58.4 kalibrasyon kurali).

## 🚨 Figür sığdırmanın bir OKUNABİLİRLİK TABANI vardır (2026-09-13)

Figürler artık sütuna **sığdırılır**, kaydırılmaz (`pre.mermaid` + `MarkdownContent.astro`).
Sığdırmanın tuzağı ölçekle gelir: bir telefon sütunu geniş bir akış şemasının beşte
biridir ve o kadar küçültülen diyagram, metnin fotoğrafına döner — ölçüldü, 390px'te
en geniş figür **3.7px** yazı üretti. Bu yüzden `MIN_SCALE = 0.5` tabanı vardır:
altına inmek gerekiyorsa figür taban boyunda kalır ve **o zaman** kaydırılır
(`[data-scrolls]`). Sonuç ölçümü: 1920/2560'ta 44 figürün hiçbiri kaydırılmaz,
1440'ta da kaydırılmaz; yalnız tablet ve telefonda en geniş birkaçı kaydırılır.

İkinci ders, hangi mermaid ayarının işe yaradığıdır. **Yazı boyutu ölçek-değişmezdir**
— büyütürsen düğüm de büyür, küçültme oranı da aynı oranda düşer, ekrandaki boy
değişmez. Kazandıran, yazıyla birlikte büyümeyen SABİT piksellerdir: `padding` ve
`rankSpacing`. Onları kısmak (20→12, 70→48) + `fontSize`'ı sayfanın kendi boyuna
çekmek (15→17px) en geniş figürün ekrandaki yazısını **9.5px'ten 12.6px'e** çıkardı.

Plaka `fit-content`'tir: dar diyagram artık boş yeşil bir tarlanın ortasında durmaz.
🚨 Bunun bedeli: SVG'ye `width: 100%` YAZILAMAZ (ebeveyn çocuğa, çocuk ebeveyne
bakar; ölçüldü — plaka 366px'e çöktü). Genişlik `viewBox`'tan okunup piksel olarak
yazılır, `max-width` ile küçültülür.

## 🚨 Tek başına SVG favicon, sekmede ESKİ ikonu bırakır (2026-09-13)

`favicon.svg` Faz 163'te yeni işaretle değişmişti ve canlı site doğru dosyayı
veriyordu — ama sekmede hâlâ eski marka görünüyordu. Sebep dosya değil, **eksik
dosyaydı**: SVG'yi almayan tarayıcı `/favicon.ico`'yu ADIYLA ister, site 404
döndürüyordu, tarayıcı da elindeki önbelleklenmiş (yeniden adlandırma öncesi) ikonu
göstermeye devam ediyordu. Favicon önbelleği HTTP önbelleğinden ayrıdır; sabit bir
URL'nin içeriğini değiştirmek onu düşürmeye yetmez.

Kural: işaret üç dosyayla sevk edilir — `favicon.svg`, `favicon.ico` (16/32/48) ve
`apple-touch-icon.png` (180). Üçü de `build-package-icon.mjs`'in ürettiği türevdir,
tek kaynak `assets/tracon-mark.svg`. `.ico` konteyneri PNG kareleri doğrudan taşır;
sharp `.ico` yazamadığı için kodlayıcı script'in içindedir. Starlight'ın `favicon`
seçeneği yalnız SVG bağlantısını basar, diğer ikisi `head`'e elle eklenir.

## Reverse proxy arkasında SEO (2026-09-14)

- nginx dizin redirect'i varsayılan olarak kendi HTTP scheme/8080 portunu basar.
  `absolute_redirect off` dış origin'i korur. `site-http-denetle.py` gerçek nginx
  üzerinde query, 404 ve preview host davranışını ölçer; Astro preview bunu kanıtlamaz.
- `nginx.conf` resmi image'ın template yoluna bağlanır; yalnız `SITE_HOST`
  envsubst edilir. Diğer host'lar `X-Robots-Tag: noindex` alır. Ayrı preview
  build'i için `TRACON_SITE_INDEXING=disabled`; robots taraması açık kalır ki
  crawler HTML noindex'i okuyabilsin. Başka hosting bu header'ı ayrıca uygulamalıdır.
- CLR tipi ile aynı adlı HTTP schema farklı sözleşmelerdir. Merkezi route
  middleware title'a yüzeyi ekler; üreteç description'a tip adını ekler.
  `site-seo-denetle.py`, `check:links` içinde tüm HTML ve sitemap kümesini denetler.
- DocFX, kaldırılan `#`'in altına doğrudan `#### Inheritance` yazar; Starlight'ın
  bastığı H1'e karşı bu `h1→h4` atlamasıdır. Tip sayfasındaki `####` dizisi metadata'dır
  → `<dl class="api-relations">`; namespace sayfasındaki `###` bölümdür → `##`'a
  yükseltilir. Altındaki `##`→`###`→`####` zaten doğrudur; toptan yükseltmek düzleştirirdi.
  Kapı: `site-seo-denetle.py` başlık denetimi. Üç tuzak, üçü de ölçümle çıktı:
  (1) `m` bayrağıyla `$` HER satır sonunda eşleşir — `(?![\s\S])` kullan, yoksa her
  satır boş yakalanır; (2) HTML bloğu bir sonraki BOŞ satıra kadar sürer — `</dl>`
  sonrası boş satır konmazsa ardından gelen `## Constructors` yutulur ve başlık olmaz;
  (3) DocFX overload URL'sinde parantezi de escape eder (`…equals\(system-object\)`),
  href deseni `\)` üzerinden geçebilmelidir. Ayrıca `dt`/`dd` marjını SIFIRLA: Starlight'ın
  `* + *` kuralı ilk etiketten sonrakileri aşağı iter ve çiftler kayar.
- 🚨 **`check-links.mjs` `/` ile başlamayan adresi "external" sayıp ATLIYORDU.**
  Bu yüzden 640 kırık iç bağlantı kapı yeşilken yaşadı: DocFX `Extension Methods`
  listesinde `#`'i `\#`, generic arity'yi `\-1` escape'ler ve üretecin yeniden yazma
  deseni ikisini de kaçırıyordu; kalan `.md` yolu sayfaya göreli çözülüp 404 veriyordu.
  Artık scheme'siz göreli adres sayfaya göre çözülüp denetleniyor. Ders: bir kapının
  "sıfır hata" demesi, baktığı kümenin doğru olduğunu KANITLAMAZ — neyi atladığını sor.
