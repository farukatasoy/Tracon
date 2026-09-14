# docs-site SEO denetimi — 2026-09-14

Taban: `16defb515d0a88da023fb3da7b8bdc7d20f7ea54`. Commit ve production deploy yapılmadı.
Ham ölçümler yerelde `artifacts/seo/` altında; bu kayıt yeniden koşulabilir komutları taşır.

## Doğrulanan altyapı ve kapsam

`site.config.mjs` production origin'i `https://tracon.dev`, base'i `/` olarak
belirler. Lockfile ile build edilen Astro 7.2.2 + Starlight 0.41.7 statik HTML
üretir. Starlight sitemap entegrasyonunu zaten kaydeder. `npm run generate`
DocFX/XML ve OpenAPI'den referans üretir. Üretilen sayfalar elle değiştirilmedi.
`ci.yml` siteyi doğrular; `scripts/site-deploy.sh` rsync ile kendi sunucusuna
aktarır. TLS Traefik'te sonlanır; içerik nginx:1.27-alpine üzerinden sunulur.
`.openai/hosting.json` yoktur; Sites hosting kullanılmaz.

Değişen yüzeyler: route metadata middleware'i, API description üreteci, ana sayfa
title'ı, robots endpoint'i, nginx/Compose, doğrulama script'leri ve CI.
`site-deploy.sh` container yeniden kullanılınca da template üretimi, nginx config testi
ve reload yapacak şekilde güncellendi; bu komut yerelde doğrulandı, yayın çalıştırılmadı.
Public .NET API, XML yorumları, paket README'leri, HTTP contract, capabilities,
agent haritası ve yerel referans davranışı değişmedi. Site senkronu denetiminde
paket/kod kaynaklı eşleme tetiklenmedi; agent haritası güncelliği ayrıca ölçüldü.

## Bulgular ve uygulama

P1: erişimi bozan sorun. P2: keşif ve sonuçların anlaşılması. P3: ek sinyal veya savunma.

| Öncelik / kaynak | Dosya veya URL, kanıt | Etki ve yapılan işlem |
|---|---|---|
| P1 / canlı + kod | `/getting-started` 301 ile `http://tracon.dev:8080/getting-started/` döndürüyor; dış port bağlantısı başarısız. `deploy/nginx.conf` varsayılan absolute redirect kullanıyordu. | Slash'siz bağlantı ziyaretçi ve crawler için kırık. `absolute_redirect off` eklendi; query korunarak göreli 301 üretildi. URL stratejisi değişmedi. |
| P2 / build | 226 title tekrar grubu. Örnek: `/api/tracon.evalsuite/` ile `/http-api/schemas/evalsuite/`, `EvalSuite \| Tracon`. Ayrıca namespace/paket ve kavram/HTTP sayfaları çakışıyor. | Arama sonuçlarında CLR contract ile wire schema ayırt edilemiyor. `src/starlightRouteData.mjs` merkezi olarak `.NET API`, `.NET package`, `HTTP API`, `HTTP schema` bağlamını title/OG title'a ekliyor. Tekrar grubu sıfır. |
| P2 / build | İki description tekrar grubu: VectorSearchRequest/SearchKnowledgeRequest ve RunAuthorizationResult/ToolAuthorizationResult. | `scripts/build-api-reference.mjs` XML özetini koruyarak başına tip adını ekliyor. Tüm sayfalarda description var; tekrar sıfır. |
| P2 / build + canlı ana sayfa | `index.mdx` title'ı `Tracon — approach control for your agents`; framework/problem arama sonucunda belirsiz, site adı title sonunda tekrar ekleniyor. | Title `.NET control plane for Microsoft Agent Framework` oldu. İngilizce ürün anlatısı, .NET geliştiricileri için mevcut açıklama ve yayınlanmamış paket bilgisi korundu. Konumlandırma değiştirilmedi. |
| P2 / yapılandırma, canlı preview gözlenmedi | nginx tüm host'larda aynı indekslenebilir içeriği sunuyordu; ayrı preview build modu yoktu. | nginx template production host'u merkezi config'den alır; diğer host'lara `X-Robots-Tag: noindex` verir. `TRACON_SITE_INDEXING=disabled` ayrı preview HTML'sine noindex ekler. Robots taraması açık kalır. Başka hosting bu kurala kendiliğinden uymaz. Traefik'in gerçek Host'u ilettiği PRODUCTION kanıtıyla doğrulandı: bozuk redirect'in `Location`'ı `tracon.dev` yazıyor ve `server_name _` bir ad veremeyeceği, `server_name_in_redirect` varsayılan olarak kapalı olduğu için o ad yalnız istek Host'undan gelebilir. Yani `map $host` production'da eşleşir ve noindex ÇIKMAZ; bu varsayım değil ölçümdür. |
| P3 / build | `404.html` canonical/OG URL olarak `/404/` yayıyor; noindex yok. Canlı olmayan URL zaten gerçek 404 veriyor. | Hata sayfasının canonical/OG URL'si kaldırıldı, noindex eklendi. Bu, mevcut gerçek 404'ün yerine geçmez; ek savunmadır. Sitemap zaten 404'ü dışlıyordu. |
| P3 / build | Ana sayfada WebSite structured data yok; OG type `article`. | Yalnız ana sayfaya gerçek ad ve canonical origin ile WebSite JSON-LD, OG type `website` eklendi. SoftwareApplication rating/offer, FAQ veya yapay review eklenmedi. |
| P2 / build | 640 iç bağlantı yayımlanmayan bir `.md` yoluna işaret ediyor, ör. `/api/tracon.itraconbuilder/` → `Tracon.TraconClientToolExtensions.md#...`; sayfaya göreli çözülüp 404 veriyor. İki sebep üst üste: DocFX `Extension Methods` listesinde `#`'i `\#`, generic arity'yi `\-1` olarak escape eder ve üretecin yeniden yazma deseni ikisini de kaçırıyordu; `check-links.mjs` ise `/` ile başlamayan her adresi "external" sayıp atlıyordu. Bu yüzden kapı yeşilken kırık kaldılar. | Kırık iç bağlantı hem ziyaretçiyi hem crawler'ı çıkmaza sokar ve var olmayan bir sayfa varmış gibi görünür. Desen artık `\#` ve `\-` üzerinden geçiyor (640 bağlantı `/api/...#anchor` oldu, generic'ler dahil). SINIF kapatıldı: `check-links.mjs` scheme'siz göreli adresi sayfaya göre çözüp denetliyor — düzeltmeden önceki build'e karşı tam 640 hata verdi, sonra sıfır. Denetlenen referans 180.415 → 181.055. |
| P3 / build | 1.138 sayfanın 616'sında başlık seviyesi atlıyor. DocFX, kaldırılan `#`'in altına `#### Inheritance` · `#### Implements` · `#### Inherited Members`, namespace sayfalarına `### Classes` yazıyor; Starlight'ın bastığı H1'e karşı bu `h1→h4`'tür. Ölçüm: ilk `##`'den SONRA 765 üretilen dosyada sıfır atlama. | Semantik yapı ve ekran okuyucu gezinmesi; sıralama sinyali değil. İki durum aynı şey değildir, aynı şekilde onarılmaz: tip sayfasındaki `####` dizisi bölüm değil METADATA'dır ve `<dl class="api-relations">` name/value listesine dönüşür; namespace sayfasındaki `###` gerçekten bölümdür ve `##`'a yükseltilir. Bağlantılar korunur (607 sayfada 6.284 bağlantı anchor olarak yeniden yazıldı). SEO etkisi nötr: metin ve bağlantı aynen HTML'de; yalnız 600+ sayfada tekrarlanan üç sabit etiket başlık olmaktan çıkar — açıklayıcı başlık olmadıkları için sinyal kaybı yok. TOC temiz kalır. |

## Sağlam bulunan alanlar ve sınırlar

- Build: 1.138 HTML, tek H1 her sayfada. 1.137 içerik URL'si sitemap ve self-canonical
  kümelerinde birebir eşleşiyor; 404 dışarıda. Ana sayfadan HTML `a[href]` bağlantıları
  izlenince tüm içeriklere erişiliyor. Yetim sayfa bulunmadı. 182.000 iç referans ve
  fragment kontrolü kırık bağlantı bulmadı. Tekrarlanan title, aynı içerik demek değildir:
  CLR üyeleri ve HTTP schema alanları farklı sözleşmeleri anlatır; canonical ile birleştirilmedi.
- Canlı HTTP: apex 200; HTTP→HTTPS 301; www→apex 301. Robots ve iki sitemap 200.
  Rastgele olmayan URL ve `/404/` 404. `/getting-started/index.html` 200; HTML canonical'ı
  slash URL'sine işaret ediyor. Bu alias erişimi korundu; canonical zaten tercihi bildiriyor.
- OG/Twitter görselleri bölümden türetiliyor, mutlak production URL'si taşıyor ve build'de
  mevcut dosyalara çözülüyor. Title/description HTML içinde hazır; JS gerektirmiyor.
  Twitter'ın OG fallback'i mevcut olduğundan gereksiz kopya metadata eklenmedi.
- İçerik: ana sayfa .NET paket ailesini, MAF üstündeki işletim kontrollerini, host'un
  sorumluluklarını ve henüz paket yayımlanmadığını açıkça söylüyor. Getting started,
  security, production ve capability bağlantıları task niyetini karşılıyor. Kod/ref
  sayfaları tekil tip aramasını karşılıyor. Yeni anahtar kelime sayfaları üretilmedi.
- Render/performance: statik HTML gövde ve linkler JS kapalıyken de mevcut. Arama ve
  Mermaid çizimi istemci JS kullanıyor; diyagramın SVG yerleşimi JS olmadan oluşmaz.
  Prose ve kod örnekleri bunun arkasına saklanmıyor. Sistem fontları kullanılıyor;
  dış font indirmesi yok. Screenshot'larda 2880×1800 boyut, alt metin, lazy loading ve
  async decoding mevcut. 19 PNG toplamı 4.209.634 B; hepsi ilk yüklemede indirilmez.
  Font veya görsel pipeline değişikliği gerektiren somut bir hata ölçülmedi.
- Mevcut ağırlık kapısı HTML + doğrudan CSS/JS gzip boyutunu ölçer; resimleri ve dinamik
  Mermaid parser maliyetini içermez. En ağır sayfa önce/sonra `troubleshooting`: 57.732 B;
  58.000 B sınırı değişmedi. Bu ölçüm LCP, INP veya CLS değildir.
- Search Console, CrUX veya gerçek kullanıcı ölçümlerine erişim yok. Trafik, indeks sayısı,
  sıralama ve Core Web Vitals başarısı hakkında sonuç çıkarılmadı. Python'un yerel CA
  deposu TLS doğrulamasında hata verdi; canlı ölçüm certificate doğrulaması açık curl ile
  başarıyla tekrarlandı. Bu yerel hata site TLS kusuru diye raporlanmadı.

## Doğrulama ve tekrar koşumu

```bash
npm --prefix docs-site run check   # content · build · links+SEO · weight
python3 scripts/site-seo-denetle.py
python3 scripts/site-http-denetle.py http://127.0.0.1:4175
python3 scripts/dokuman-bakim.py --site-denetle --taban 16defb515d0a88da023fb3da7b8bdc7d20f7ea54
node docs-site/scripts/build-agent-map.mjs --check
python3 scripts/kapi.py kapanis --taban 16defb515d0a88da023fb3da7b8bdc7d20f7ea54
```

HTTP testinin ön koşulu: production `dist` yerel nginx container'ına salt okunur bağlı;
`nginx.conf` `/etc/nginx/templates/default.conf.template` yolunda; `SITE_HOST` merkezi
production host, `NGINX_ENVSUBST_FILTER=SITE_HOST`. CI site işi aynı kurulumu yapar.
Container portu yalnız loopback'e açılır. Deploy gerekmez.

Site dört kapısı (content, build, links/SEO, weight) geçti. `git diff --check`,
doküman bütçe/bağlantı denetimi ve agent haritası güncelliği geçti. Doküman denetimi
önceden de bulunan 15 dar bütçe bilgilendirmesini koruyor; bütçe aşımı yok.

SEO kapısı düzeltmeden önce 230 hata verdi, sonra sıfır. Başlık denetimi eklenince
616 hata daha çıktı (yalnız `/api/`; başka bölümde sıfır yanlış pozitif), üreteç
düzeltmesinden sonra sıfır. HTTP kapısı eski nginx config ile 11 hata verdi, yeni
config ile 18 yanıtın tamamı geçti. Manuel case'ler `MT-DKL-038…040`; indeks sayacı
güncellendi. Sınıf taraması tüm route'ları kapsadı.

**Kapanış koşumu tamamlandı (2026-09-14).** `kapi.py kapanis` on adımın onunda yeşil:
tarama · doküman bakım · script unittest'leri (279) · agent haritası · denetim paketi ·
`dotnet build` 63 s · `dotnet test` 635 s · `dotnet pack` 6 s · `dotnet format` 94 s ·
`npm run check` 28 s. Site çıktısı: 1.138 HTML, 181.055 iç referans kırıksız (640'ı bu
turda ilk kez denetlenebilir oldu), SEO kapısı sıfır hata, en ağır sayfa `troubleshooting`
57.806 B — 58.000 B tavanı değişmedi, kalan boşluk 194 B. Artış eklenen CSS'tendir; başlık
değişikliği yalnız `/api/` sayfalarını etkiler ve en ağır tip sayfası 22.393 B gzip'tir.

Önceki oturumun gördüğü AOT/PDB kilidi bu seri koşumda TEKRARLAMADI; kaynak çekişmesi
olduğu böylece doğrulandı, kırmızı sonuç yeşile "çevrilmedi". Tarayıcı doğrulaması:
yerel nginx üzerinden `/api/tracon.agentdefinition/` açık ve koyu temada açıldı; üç satır
name/value olarak hizalı render oluyor (dt/dd üstleri birebir aynı piksel), bağlantı rengi
gövde metniyle aynı, 400 px'te tek sütuna iniyor ve yatay taşma yok.

Preview kontrolü: site dizininde Node 22.12+ ile `TRACON_SITE_INDEXING=disabled node
node_modules/astro/bin/astro.mjs build --outDir ./.astro/seo-preview`, sonra repo kökünde
`python3 scripts/site-seo-denetle.py --preview --dist docs-site/.astro/seo-preview`.
Preview kapısı da 1.138 HTML üzerinde sıfır hata verdi. Production `dist` ayrı kalır. Astro prerender bağımlılıkları çıktıdan çözüldüğünden,
çıktıyı site ağacı dışına almak yerelde `piccolore` module resolution hatası üretti.

## Resmi dayanaklar

- [Google title links](https://developers.google.com/search/docs/appearance/title-link): açıklayıcı, ayırt edilebilir title; sabit karakter sınırı bir sıralama şartı değildir.
- [Google snippets](https://developers.google.com/search/docs/appearance/snippet): sayfaya özgü description; programatik üretim uygundur, snippet seçimi Google'a aittir.
- [Canonical birleştirme](https://developers.google.com/search/docs/crawling-indexing/consolidate-duplicate-urls) ve [sitemap](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap): canonical URL'leri tutarlı biçimde bildirme.
- [Robots kuralları](https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag) ve [robots.txt sınırı](https://developers.google.com/search/docs/crawling-indexing/robots/intro): Disallow tek başına indekslemeyi engellemez; noindex okunabilir olmalıdır.
- [JavaScript SEO](https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics): HTML metadata, bağlantılar ve doğru hata durumları.
- [Google site names](https://developers.google.com/search/docs/appearance/site-names): ana sayfada WebSite name/url desteklenir; site names Rich Results Test tarafından doğrulanmaz.
- [SoftwareApplication](https://developers.google.com/search/docs/appearance/structured-data/software-app): required offer/review bilgileri bu yayınlanmamış paket sitesinde kanıtlanmadığı için eklenmedi.
- [nginx absolute_redirect](https://nginx.org/en/docs/http/ngx_http_core_module.html#absolute_redirect): off göreli redirect üretir.
- [web.dev CLS](https://web.dev/articles/optimize-cls): resimler için boyut ayırmak kaymayı azaltır; mevcut boyutlar korundu.

## Yayın sonrası kalan işler

Bakımcı izin verdiğinde normal yayın hattını çalıştırmalı; bu tur deploy yapmadı.
Canlı slash redirect, query, www/HTTP ve 404 testlerini yeniden koşmalı. Başka bir
staging host/service varsa authentication veya noindex header'ı ayrıca doğrulanmalı.
Search Console erişimiyle sitemap gönderimi, temsilî URL Inspection ve seçilen
canonical kontrol edilmeli. WebSite JSON-LD syntax/alanları yerelde doğrulandı;
Google'ın algısı URL Inspection ile takip edilmeli. Sosyal paylaşım cache'leri ve
CrUX/Search Console CWV verisi yayın sonrası incelenmeli. Sıralama artışı vaat edilmez.
