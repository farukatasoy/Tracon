# docs-site GEO denetimi — 2026-09-14

Taban: `d9b41440`. Commit ve production deploy yapılmadı. Klasik SEO denetimi
(`2026-09-14-site-seo-denetimi.md`) **canlıya alınmıştır** — bu tur onu tekrar
ölçmedi; canlı `llms.txt` ve `robots.txt` o turun `dist` çıktısıyla birebir aynı
doğrulandı, `/getting-started` göreli 301 veriyor.

## Doğrulanan altyapı

Astro 7.2.2 + Starlight 0.41.7, `trailingSlash: 'always'`, statik HTML, 1.138 sayfa.
Production origin `https://tracon.dev` (`site.config.mjs`, tek kaynak). Yayın:
`scripts/site-deploy.sh` → rsync → nginx:1.27-alpine (`docs-site/deploy/`), TLS
Traefik'te. AI tarafı altyapısı zaten vardı ve **üretiliyordu**: `build-agent-map.mjs`
tek kaynaktan (`capabilities.md`) üç artefakt yazıyor — pakete giren
`Tracon.AgentMap.md`, `public/llms.txt`, `public/llms-full.txt`. Kapı `--check` ile
commit edilmiş kopyayı üretilenle **bayt bayt** karşılaştırır ve iki bütçeyi ölçer
(map 11.264 B, llms.txt 24.576 B). llms-full.txt'in bütçesi **yoktur**; tazeliği
ölçen bir şey yoktur, yalnız kaynakla aynılığı ölçülür.

## Bulgular ve uygulama

P1: makine okuyucusuna yanlış veya erişilemez içerik. P2: alıntılanabilirlik ve
keşif. P3: ek sinyal.

| Öncelik / kaynak | Dosya veya URL, kanıt | Etki ve yapılan işlem |
|---|---|---|
| P1 / build | Kod blokları düz metne indirgenince **satır yapısını kaybediyor**. Expressive Code her satırı ayrı `<div class="ec-line">` yazar, aralarına newline koymaz; ana sayfadan ölçüldü: `var app = builder.Build();app.MapTracon("/tracon");app.Run();`. Tablolarda sütun sınırı aynı şekilde kayboluyor (`reference/compatibility`). | Bir modelin ürettiği C# derlenmez, tablo hücresi yanlış başlığa bağlanır. Render doğru olduğu için hiçbir kapı bunu göremiyordu. `src/pages/[...slug].md.ts` her içerik sayfasının markdown kaynağını `<adres>index.md` olarak yayınlıyor (1.136 dosya, dist 92 → 100 MB). Kaynak markdown'da fence ve tablo aynen durur. Mevcut URL'ler değişmedi. |
| P1 / build (bu turda üretildi ve yakalandı) | İlk koşumda açılış sayfası `/index/index.md` altına, gövdesinde `Page: https://tracon.dev/index/` ile yazıldı — var olmayan bir adres. Sebep: loader `concepts/index.md`'den `index`'i düşürür, site kökünde düşürmez. | Site içinden bakan hiçbir kontrol bunu göremezdi. `site-seo-denetle.py` artık her sayfa için kopyanın **varlığını değil** gövdesindeki `> Page:` satırının canonical'a eşitliğini denetliyor; açılış ve hata sayfası kopya almamalı. Sınıf kapatıldı. |
| P1 / yapılandırma | nginx stok `mime.types`'ta `.md` yok: kopyalar `application/octet-stream` gidecekti. | Tarayıcıda indirme istemi, Content-Type'a güvenen bir fetcher için okunamaz gövde. `location ~ \.md$ { default_type text/markdown; }` eklendi. Server seviyesinde `types { }` bloğu **kullanılmadı** — o blok miras alınan haritanın tamamını siler ve `text/css`/`image/png`'yi de götürürdü; yerel nginx'te üçü de doğrulandı. `charset_types` ve `gzip_types`'a `text/markdown` eklendi. |
| P2 / build | llms-full.txt'te her sayfa yalnız `# Başlık` ile ayrılıyordu; **hiçbir bölüm kaynağını taşımıyordu**. 720 KB'lık dosyadan cevap veren bir model alıntı adresi üretemez. | Her bölüm artık `Source: <canonical>` taşıyor; dosyanın önsözü de "cite that, not this file" diyor. |
| P2 / build | llms.txt konvansiyona iki yerde uymuyordu: H1'in altında **blockquote özet yoktu**, ve bağlantılar HTML'e işaret ediyordu. Kaynak: llmstxt.org — blockquote "key information necessary for understanding the rest of the file", ve "links … should therefore point to LLM-friendly content, such as the markdown versions of pages". | Lead cümlesi blockquote'a alındı (üreteçte; elle düzenlenmedi). Bağlantılar **kasıtlı olarak** HTML kaldı: alıntılanacak adres odur, `.md` bir fetch ayrıntısıdır. Bölüm önsözü ve "Where to look" `index.md` kuralını söylüyor. 52 indeks satırının 52'sinde `index.md` çözülüyor (ölçüldü). |
| P2 / build | Sayfalar `<link rel="alternate" type="text/markdown">` ve `rel="describedby"` bildirmiyordu — llmstxt.org'un adıyla önerdiği iki ilişki. | Route middleware'de merkezî olarak eklendi (1.138 sayfa, elle düzenleme yok). Ağırlık: 57.806 → 57.848 B gzip, tavan 58.000 B değişmedi. |
| P2 / yapılandırma | `robots.txt` yalnız `User-agent: * / Allow: /` idi; hiçbir crawler adıyla anılmıyordu ve **SEO kapısı metinde `Disallow: /` arıyordu**, yani herhangi bir alt yol kısıtı bile kapıyı kırardı. Politika yazmak imkânsızdı. | Politika `site.config.mjs`'de `crawlerPolicy` verisi oldu; endpoint yalnız render ediyor. Kullanıcı kararı: **hepsi açık kalsın, ama açıkça yazılsın** (14 crawler, üç amaç grubu). Kapı artık grup grup ayrıştırıyor ve yalnız tam `/` değerini site kapatma sayar. |
| P2 / bu turda üretildi ve yakalandı | İsimli grup eklenince `*` grubundaki `/pagefind/` kısıtı 14 crawler için **sessizce etkisiz** kaldı (RFC 9309: yalnız en özgül grup okunur). Ayrıca Python'un `urllib.robotparser`'ı `Allow: /` başta iken pagefind'ı taranabilir raporladı. | Kısıtlar her gruba tekrar yazılıyor ve `Allow: /`'dan **önce** geliyor; kural hem en-uzun-eşleşme hem ilk-eşleşme okumasında doğru. Gerçek bir parser ile doğrulandı. |
| P2 / build | `/pagefind/fragment/` 1.137 dosya · 4,7 MB (her sayfanın metninin JSON kopyası), `/pagefind/index/` 44 dosya · 1,3 MB — tamamı taranabilirdi. | İç bağlantısı olmayan yeni bir sitede ilk taramanın çoğu buraya giderdi ve içerik ikinci kez, daha kötü bir kodlamada sunuluyordu. İkisi `Disallow` edildi. Yanındaki script ve stylesheet **kasıtlı olarak** listelenmedi — Google'ın kuralı render için gereken kaynağı kapatmamaktır. |
| P3 / build | `/openapi/tracon.json` (595 KB) hiçbir makine haritasında anılmıyordu; yalnız bir kılavuzdan bağlanıyordu. | 130 HTTP referans sayfasının cevabını tek fetch'te veren artefakt. llms.txt ve pakete giren map'in "Where to look" bölümüne eklendi. |
| P3 / build | llms-full.txt'i baştan okuyan bir model, adın havacılık terimiyle çakışmasına karşı hiçbir şey görmüyordu. | Dosyanın başına map'in blockquote'uyla aynı tanım cümlesi kondu. Ayrıca her `index.md` kendi başlığının altında bir cümlelik provenance taşıyor — alınan parça sitesiz geldiğinde ürünü adlandıran tek şey odur. |

## Sağlam bulunan alanlar ve sınırlar

- **Entity netliği zaten iyiydi; ilk analizim yanlıştı.** Açılış sayfasının H1'i
  metafordur, ama hemen altındaki hero alt başlığı statik HTML'de ".NET package
  family that adds a production control plane to Microsoft Agent Framework" diyor.
  Bunu ilk ölçümde kaçırdım çünkü yalnız `sl-markdown-content`'e baktım; `Hero.astro`
  dışarıda kalıyor. Buna dayanarak eklediğim ikinci tanım cümlesi **geri alındı** —
  iki ekranda iki kez ".NET package family" tekrar olurdu. Ölçüm: 1.138 sayfanın
  1.138'inde hem `Tracon` hem bir kategori terimi (.NET/NuGet/MAF/ASP.NET) gövdede
  geçiyor. 52 elle yazılmış sayfanın 18'inde **ilk paragraf** ürünü adlandırmıyor;
  bu HTML'de sorun değil (title ve hero adlandırıyor), makine parçasında sorundu ve
  `index.md` provenance başlığı onu kapatıyor. Konumlandırma ve metafor değişmedi.
- **Halüsinasyon yüzeyi ölçüldü, sapma bulunmadı.** Elle yazılmış sayfalardaki 26
  ayrı `Tracon.X` kod span'i: 20'si gerçek paketleştirilebilir paket veya
  dokümante tip; kalan 6'sı (`Tracon.Reader/Operator/Admin` rol politikaları,
  `Tracon.Usage`/`Tracon.Tools` analyzer kategorileri, `Tracon.Generators`) kodda
  birebir doğrulandı — üçüncüsünü `compatibility.md` zaten "ayrı public paket değil"
  diye söylüyor. MAF biçimli 20 tip adının 20'si `src/` içinde geçiyor. Sürüm
  numarası uydurulmamış: site `1.0.0-preview.N` yer tutucusunu kullanıyor ve
  paketlerin yayınlanmadığını söylüyor.
- **Başlık anchor'ları kararlı:** 1.138 sayfada tekrarlanan heading id'si yok, h2–h6
  başlıklarının yalnız 5'i id'siz (açılış sayfasının kendi HTML'i).
- **Yapılandırılmış veri:** yalnız ana sayfadaki `WebSite` JSON-LD var ve olduğu gibi
  bırakıldı. `SoftwareApplication` **eklenmedi**: Google, AI Overviews / AI Mode için
  "You don't need to create new machine readable files, AI text files, or markup …
  There's also no special schema.org structured data that you need to add" diyor;
  puan için schema eklemek bu denetimin kapsamı dışıdır.
- **Bing/Copilot varsayılanı zaten en geniş kapsam.** `NOCACHE`/`NOARCHIVE` yokluğu
  Bing'in kendi duyurusunda "may be included in Bing Chat answers" demektir; bu
  etiketleri eklemek kapsamı daraltırdı, eklenmedi.
- **Tazelik:** her sayfa `Last updated:` ile `<time datetime>` basıyor. llms.txt ve
  llms-full.txt içerik türevi bir revision hash taşıyor; **tarih taşımıyor ve
  taşımamalı** — commit zamanına bağlı bir damga, içerik değişmeden `--check`'i
  düşürürdü (rebase, amend).
- Mermaid diyagramları JS ile çizilir ama kaynak metin (`accTitle`/`accDescr` dahil)
  statik HTML'de düz metin olarak durur; bir okuyucu anlamı JS'siz alır.
- `[WARN] Could not render '/404' from route '/[...slug]'` uyarısı **bu turdan önce
  de vardı**: `[...slug].md.ts` dosyası kaldırılıp build tekrarlandığında aynen çıkıyor.
  Starlight'ın kendi catch-all route'u ile enjekte ettiği `/404` arasındadır.

## Ölçülmeyen ve ölçülemeyen

Bir modelin bu siteyi alıntılayıp alıntılamadığı, AI trafiği, "GEO skoru" veya
görünürlük yüzdesi **ölçülmedi ve tahmin edilmedi**. Sohbet botuna soru sormak
tekrarlanabilir bir ölçüm değildir. Search Console yeni bağlandı, tarama başlamadı.
Bu kayıttaki her sayı ya `dist/` ya yerel nginx ya da canlı `curl` çıktısıdır.
Sıralama veya alıntılanma artışı vaat edilmiyor.

## Doğrulama ve tekrar koşumu

```bash
npm --prefix docs-site run check          # content · build · links+SEO · weight
python3 scripts/site-http-denetle.py http://127.0.0.1:4175
node docs-site/scripts/build-agent-map.mjs --check
python3 scripts/dokuman-bakim.py --site-denetle --taban d9b41440
python3 scripts/kapi.py kapanis --taban d9b41440
```

Yerel nginx ön koşulu SEO turuyla aynıdır (`dist` salt okunur bağlı, `SITE_HOST`
merkezî host, `NGINX_ENVSUBST_FILTER=SITE_HOST`, port yalnız loopback).

Önce/sonra: SEO kapısı 1.138 HTML'de 0 hata (yeni markdown/describedby denetimi ilk
koşumda 1.138 hata verdi — beklenti mutlak adresti, bağlantılar kök-göreli; kapı
düzeltildi). HTTP kapısı 18 → 24 yanıt, 0 hata. Bağlantı denetimi 181.055 → 183.334
iç referans, kırık yok. Ağırlık 57.806 → 57.848 B gzip, tavan değişmedi (kalan
boşluk 194 → 152 B). llms.txt 20.528 → 21.053 B (bütçe 24.576), agent map 10.529 B
(bütçe 11.264), llms-full.txt 717.689 → 721.574 B (bütçesiz). `nginx -t` uyarısız —
ilk denemede `charset_types`'a `text/html` yazmak "duplicate MIME type" uyarısı
verdi, kaldırıldı. Preview build (`TRACON_SITE_INDEXING=disabled`) 1.138 sayfada
0 hata. Tarayıcı: yerel nginx üzerinden açılış sayfası 1280 px ve 400 px'te
render edildi; görünür değişiklik kalmadı (tek görünür düzenleme geri alındı).

## Resmi dayanaklar

- [llms.txt spesifikasyonu](https://llmstxt.org/): H1 tek zorunlu bölüm; blockquote özet; `.md` kopya önerisi; `rel="alternate" type="text/markdown"` ve `rel="describedby"`. **`llms-full.txt` spec'te tanımlı değildir** — yaygınlaşmış bir konvansiyondur, bu ayrım doğrulandı.
- [OpenAI bots](https://developers.openai.com/api/docs/bots): GPTBot training, OAI-SearchBot arama/atıf, ChatGPT-User kullanıcı tetikli, OAI-AdsBot reklam doğrulaması — ayrı kararlar.
- [Anthropic crawler'ları](https://support.claude.com/en/articles/8896518-does-anthropic-crawl-data-from-the-web-and-how-can-site-owners-block-the-crawler): ClaudeBot training, Claude-SearchBot arama, Claude-User kullanıcı tetikli.
- [Perplexity bots](https://docs.perplexity.ai/guides/bots): PerplexityBot arama/atıf ve foundation model eğitimi için **kullanılmaz**; Perplexity-User robots.txt'i genelde yok sayar.
- [Google common crawlers](https://developers.google.com/search/docs/crawling-indexing/google-common-crawlers): Google-Extended "does not impact a site's inclusion in Google Search nor is it used as a ranking signal".
- [Google AI features](https://developers.google.com/search/docs/appearance/ai-features): AI Overviews/AI Mode için ek dosya, AI text dosyası veya özel schema **gerekmez**; kontrol Googlebot robots.txt kuralları ve snippet direktifleridir.
- [Applebot](https://support.apple.com/en-us/119829): Applebot-Extended yalnız training'i kapatır, arama kapsamını değil.
- [Common Crawl CCBot](https://commoncrawl.org/ccbot): robots.txt'e uyar; veri açık depoya girer.
- [Meta web crawlers](https://developers.facebook.com/documentation/sharing/webmasters/web-crawlers): meta-externalagent training, meta-webindexer arama, meta-externalfetcher kullanıcı tetikli, facebookexternalhit link önizlemesi.
- [Bing içerik kontrolleri](https://blogs.bing.com/webmaster/september-2023/Announcing-new-options-for-webmasters-to-control-usage-of-their-content-in-Bing-Chat): NOCACHE/NOARCHIVE yokluğu en geniş Bing Chat kapsamı demektir.
- [RFC 9309](https://www.rfc-editor.org/rfc/rfc9309.html): crawler yalnız en özgül eşleşen grubu uygular; en özgül kural kazanır.
- [nginx charset_types](https://nginx.org/en/docs/http/ngx_http_charset_module.html#charset_types) ve [types](https://nginx.org/en/docs/http/ngx_http_core_module.html#types).

## Yayın sonrası kalan işler

Bu tur deploy yapmadı. Yayından sonra doğrulanmalı: `.md` kopyaların canlı
Content-Type'ı, yeni `robots.txt`'in canlı gövdesi, `/pagefind/fragment/` kısıtının
Search Console tarama istatistiklerine yansıması, ve `<link rel="alternate">`'in
üçüncü taraf fetcher'larda işe yarayıp yaramadığı. Search Console erişimiyle
sitemap gönderimi ve temsilî URL Inspection hâlâ bekliyor. Crawler politikası
`site.config.mjs`'de veri olduğundan, bir lisans kararı değişirse tek satırlık bir
düzenlemedir; ölçülmüş gerekçeler o dosyada kayıtlıdır.
