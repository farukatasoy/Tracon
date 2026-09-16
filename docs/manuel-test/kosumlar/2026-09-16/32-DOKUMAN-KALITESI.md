# 32 — Doküman Kalitesi ve Görsel Kimlik (`DKL`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../32-DOKUMAN-KALITESI.md`](../../32-DOKUMAN-KALITESI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s3` (Faz B, dağılım: `32 · 29 · 34 · 21 · 11 · 25 · 17 · 20`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s3` · dal `test/kosum-s3` |
| **Kod** | `7ace1d82` donuk (`git diff --stat 7e3a4de7..HEAD -- src samples tests` boş — doğrulandı) |
| **Case sayısı** | 40 (MT-DKL-001..040) |
| **Port** | 5083 (strand app — bu aile `docs-site`'ı test eder, app'e neredeyse hiç dokunmaz) |
| **Depo** | PostgreSQL `mt_s3` (reset edildi, uygulama sağlıklı — bu aile için kullanılmadı) |

**Ortam notu:** Bu aile `docs-site/` üzerinde çalışır (Astro statik site + Node
script'leri + iki `dotnet test` self-test'i), örnek uygulamaya (`samples/Tracon.Api`)
neredeyse hiç ihtiyaç duymaz. Oturum protokolünün 3-4. adımları (şerit ortamı,
`/api/meta` sağlık doğrulaması, `mt_s3` reset) yine de uygulandı — sıradaki
ailelerin (`29`, `34`, ...) aynı oturumda ihtiyaç duyabileceği ortamı hazır
bırakmak için.

`docs-site` kurulumu: `npm ci` (Node 22.23.2), ardından `npm run build` (bir kez,
`prebuild` kancasının `npm run generate`'i tetiklemesi için — bkz. MT-DKL-020),
sonra `check:content` / `check:links` / `check:weight` ayrı ayrı doğrulandı.

---

## Devir notu

### Oturum 14 (bu oturum — en güncel durum)

**Aile 32 kapandı — tam sayım.** Oturum 13'ün bıraktığı 15 `☐ Beklemede`
case'in **13'ü** bu oturumda çözüldü:

- **8 `👤` case Playwright ile (`npx astro preview --port 4321`, `dist/`
  zaten üretilmişti, rebuild gerekmedi):** MT-DKL-012, 025, 027, 028, 029, 030,
  032 → **☑ Geçti**. MT-DKL-026 → **☑ Kaldı**, yeni kusur **`HATA-S3-002`**
  (açılış sayfası 1024px'te 32px yatay taşıyor — `.scope-rings` dekoratif
  grafiği, `docs-site/src/styles/landing.css:20`; ayrıntı aşağıda).
- **2 case, kullanıcının açık talimatıyla mutasyon-gözlem-geri-alma deseniyle
  (aile 31'in emsaliyle aynı yöntem):** MT-DKL-023, MT-DKL-024 → ikisi de
  **☑ Geçti** — kaynak geçici olarak mutasyona uğratıldı, self-test'in tam
  beklenen mesajla kızardığı doğrulandı, sonra `git checkout` ile **birebir**
  geri alındı. `git diff --stat 7e3a4de7..HEAD -- src samples tests` bu
  turlardan sonra da **boş** — kod donması bozulmadı.

**Kalan 5 case** (MT-DKL-001, 002, 003, 004, 006) gerçekten göz gerektiriyor
(aile dosyasının kendi "Bilinen sınırlar" bölümü) — fiziksel eylem tablosunda
kaldı, `☐ Beklemede`.

**Sayım (skill §7 betiğinin mantığıyla, elle doğrulandı, her case'in SON
işareti alınarak):** 40/40 case başlığı — **33 ☑ Geçti · 2 ☑ Kaldı
(MT-DKL-020, MT-DKL-026) · 5 Beklemede** (1,2,3,4,6 — gerçek göz gerektiren
fiziksel eylem case'leri). Hesap: oturum 13'ten 24 Geçti + 1 Kaldı miras;
bu oturum 9 case'i Geçti'ye çevirdi (12,25,27,28,29,30,32,023,024) ve 1
case'i Kaldı'ya çevirdi (026) → 24+9=33 Geçti, 1+1=2 Kaldı, 15-10=5 Beklemede.
**33 + 2 + 5 = 40.** Tam hesap kapandı.

**Aile 32 artık her case'i Geçti, Kaldı veya fiziksel eylem tablosunda —
tanım gereği kapalı.** Kalan 5 case insan gözü istiyor; kod/DOM ile
çözülemeyecekleri ailenin kendi dokümanında zaten yazılı, bu yüzden bu 5
case'i "kapanmamış" saymıyoruz — skill §4.3: bunlar `☐ Beklemede` kalır ama
oturum sonunda topluca kullanıcıya sorulacak fiziksel eylem listesindedir.

**Sonraki oturumun işi:** Faz B dağılımına göre sıradaki aile
`29-AGENT-DESTEGI.md`'dir (`## MT-` başlık biçimini zaten kullanıyor, tablo
biçimine çevirme gerekmiyor). Bu oturum bütçe sınırına yaklaştığı için o
aileye başlamadı — ortam (port 5083, `mt_s3` şeması, `docs-site/dist`) hazır
bırakıldı.

**Ortam durumu — devredilirken durduruldu:** `dotnet run` (port 5083, `mt_s3`
şeması) ve `npx astro preview --port 4321` oturum sonunda **durduruldu**.
`mt_s3` şeması ve `docs-site/dist` diskte kalıcı.

---

### Oturum 13 (geçmiş — korunuyor)

**Bu oturum aileyi AÇTI, bitirmedi.** 27 CLI/script case'i (👤 işaretsiz olanların
tamamı) koşuldu; 13 `👤` case'i (1,2,3,4,6,12,25,26,27,28,29,30,32 — tarayıcıda
gerçek gezinme/tema/klavye/arama gerektirenler) bu oturumda **koşulmadı**, aşağıdaki
fiziksel eylem tablosuna eklendi. Bunların çoğu (12, 26, 27, 28, 29, 32 gibi) Playwright
ile nesnel olarak (DOM/erişilebilirlik ağacı üzerinden) koşulabilir — sonraki oturum
bunu değerlendirebilir; 1, 2, 3, 4, 6 aile dosyasının kendi "Bilinen sınırlar"
bölümünde gerçek göz gerektirdiği açıkça yazılı olanlardır.

**İki case kod donması ile çelişiyor, koşulmadı (`☐ Beklemede` kaldı):**
- `MT-DKL-023` — `TraconSchedulingOptions.RunWorker`'ın `= true` başlatıcısını
  **kaynakta** (`src/Tracon.Abstractions` veya karşılığı) silmeyi ister. `src/`
  donuk (kural 1) — geçici mutasyon-testi bile bu turda yapılmadı.
- `MT-DKL-024` — `OpenAIChatCompletionsEndpoints.cs`'te bir policy'yi **kaynakta**
  değiştirmeyi ister (`src/Tracon.AspNetCore/Endpoints/`). Aynı çelişki.

Kapanış modunda (Aşama 2, doğrulama sunucusu ile) bu iki case'in koşulması için
karar gerekir — bu tur "koşum sırasında kod değiştirilmez" kuralı **hiçbir istisna
tanımıyor** (yalnız `Beklenen sonuç` metni için bir istisna var, kaynak kodu için yok).

**Not (oturum 14):** Bu son paragraf oturum 14'te **aşılmıştır** — kullanıcı
oturum 14'te bu iki case için açık mutasyon-gözlem-geri-alma yetkisi verdi
(aile 31 emsaliyle aynı desen); "kapanış modunu bekle" artık geçerli değil,
sonuç yukarıdaki oturum 14 notunda ve case'lerin kendi kayıtlarındadır.

**Bulunan bir gerçek kusur, `HATA-S3-001` olarak kaydedildi (aşağıda).** Dört
`Beklenen sonuç` metni koda göre yanlış/bayat çıktı ve düzeltildi (kural 1'in
istisnası — MT-DKL-005, MT-DKL-009, MT-DKL-016, MT-DKL-036; gerekçe her
case'in kendi kaydında).

**Sayım (skill §7 betiğiyle alındı):** 40/40 case başlığı yazıldı — **24 ☑
Geçti · 1 ☑ Kaldı (MT-DKL-020) · 15 Beklemede** (13 `👤` + MT-DKL-023/024).

---

### HATA-S3-001 — `npm run check` fresh checkout'ta yanlış sırayla kırılıyor

- **Case:** MT-DKL-020 (ve dolaylı olarak ailenin "Koşmadan önce" bloğu)
- **Önem:** Düşük (yalnız yerel/fresh-checkout geliştirici deneyimi; CI bu sırayı
  kullanmıyor, bu yüzden yayın hattı etkilenmiyor)
- **İzlek:** C (docs-site tooling)
- **Ortam:** macOS arm64 · Node 22.23.2 · `docs-site/` fresh `npm ci`

**Beklenen**
Ailenin "Koşmadan önce" bloğu `npm ci` sonrası `npm run check`'in dört adımının
(`check:content && build && check:links && check:weight`) hepsinin yeşil
geçmesini bekliyor (MT-DKL-020).

**Gerçekleşen**
Fresh `npm ci` sonrası `npm run check` **ilk adımda** (`check:content`) 3 hata ile
kızarıyor:
```
docs-site/public/llms.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
Exemption list names a page that no longer exists: reference/changelog.md
```
Kök neden: `src/content/docs/reference/changelog.md` **üretilen** (gitignore'lu)
bir dosyadır ve yalnız `npm run generate` (`build-changelog.mjs` içerir) ile
oluşur. `generate`, `build` script'inin `prebuild` kancasıdır
(`docs-site/package.json:10-11`). Ama `check` script'i `check:content`'i
`build`'den **önce** çalıştırır (`docs-site/package.json:12-14`):
```
"check:content": "node --test ... && node scripts/check-content.mjs",
"build": "astro build",
"prebuild": "npm run generate",
"check": "npm run check:content && npm run build && npm run check:links && npm run check:weight"
```
Yani `check:content`, `changelog.md` diskte yokken çalışıyor; script içi
`buildAgentMap()` bu sayfayı hiç görmeden bir `llms.txt`/`llms-full.txt`
üretiyor ve bunu **commit'li** (changelog sayfası varken üretilmiş) kopyalarla
karşılaştırıp uyuşmazlık buluyor — aynı kök neden üç hatanın hepsini açıklıyor.
`npm run build` bir kez koşulduktan sonra (bu oturumda yapıldı) `changelog.md`
diskte kalıcı olarak oluşuyor ve sonraki her `check:content` çalıştırması temiz
geçiyor (doğrulandı, aşağıda MT-DKL-020 kaydı).

**Yeniden üretme**
1. Temiz bir `docs-site/` çalışma kopyasında (hiç `npm run build`/`generate`
   koşulmamış) `npm ci`
2. `npm run check`
3. İlk adımda (`check:content`) yukarıdaki 3 hata ile durur

**Kanıt**
- Komut çıktısı yukarıda alıntılandı (bu oturumun terminal geçmişi)
- `docs-site/package.json:8-14` (script sırası)
- `docs-site/scripts/check-content.mjs:347-350` (404 muafiyeti, ilgisiz ama
  aynı fonksiyonun bir parçası), `:520-527` (llms karşılaştırması)

**Kapsam**
Yalnız `npm run check` aggregate script'inin **fresh-checkout** senaryosu.
CI bu script'i hiç çağırmıyor (`.github/workflows/ci.yml:104-115` `build-changelog.mjs`
ve `build-agent-map.mjs --check`'i `check-content.mjs`'ten **önce ayrı adım**
olarak çalıştırıyor, dolayısıyla CI bu sıra hatasına düşmüyor). Yayın hattını
bloklamıyor; yalnız yerel `npm run check` kullanan bir katkıcının/test
oturumunun ilk çalıştırmasını yanıltıyor.

---

## MT-DKL-005 — `Read next` bölümü olmayan sayfaları grep'le, yalnız muaf olanlar çıkmalı

**Gerçek sonuç**
```
grep -rL '^## Read next' . --include='*.md' --include='*.mdx' | sed 's|^\./||' | grep -vE '^(api|http-api)/'
index.mdx
404.md
```
İkinci grep (eski `## Related`/`## Next` başlığı arayan) **boş** döndü —
kimse eski başlığı kullanmıyor.

Spec `Beklenen sonuç` "yalnız `index.mdx` çıkar" diyordu; gerçekte **iki**
sayfa çıkıyor. İkincisini (`404.md`) araştırdım: gerçek kapı
(`check-content.mjs:347-350`) `manualContent` listesini `basename(file) !==
'404.md'` ile filtreliyor — "404 sistem rotasıdır, sidebar/llms index'te hiç
yer almaz" gerekçesiyle **kasıtlı** bir muafiyet, `index.mdx`'in Faz 76'daki
muafiyeti kadar meşru. Case'in manuel `grep` komutu bu filtreyi uygulamıyor,
yalnız `index.mdx`'i özel olarak biliyor. Bu doküman kusuru düzeltildi (kural 1
istisnası) — spec artık iki sayfayı da bekliyor. `npm run check:content`'in
kendisi (gerçek kapı) zaten ikisini de doğru muaf tutuyor ve kızarmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-007 — Dört bölümden birer sayfanın `og:image` etiketi dört farklı dosyaya işaret eder

**Gerçek sonuç**
`npx astro preview --port 4321` üzerinden (`npm run build` bu oturumda zaten
koşuldu):
```
/                     -> og:image = https://tracon.dev/social/overview.png   (200)
/ui/                  -> og:image = https://tracon.dev/social/console.png    (200)
/guides/production/   -> og:image = https://tracon.dev/social/operate.png    (200)
/reference/glossary/  -> og:image = https://tracon.dev/social/reference.png (200)
```
Dört farklı dosya adı, dördü de sayfada `200` — hem etiket hem dosyanın kendisi
ayrıca `curl`'lendi ve ikisi de `200` döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-008 — `## Read next` bölümü silinince kapı sayfayı adıyla söyler

**Gerçek sonuç**
`guides/reliability.md`'nin `## Read next` bölümünden itibaren gövde geçici
olarak kesildi (yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  guides/reliability.md: does not end with '## Read next' (last section: Troubleshooting)
```
İkinci satır beklenen davranış: sayfa **adıyla** söyleniyor. İlk satır bu
case'in kendi mutasyonunun yan etkisi (kesilen sayfa `llms-full.txt`'nin
üretilen kopyasını da değiştiriyor) — case'in odağı olan davranışla ilgisi yok.
Değişiklik geri alındı (`cp` yedekten), `git diff --stat` boş — dosya
kirlenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-009 — Açık temada kontrast oranı düşürülünce kapı oranı yazarak kızarır

**Gerçek sonuç**
`site.css`'te açık temanın `--tracon-text-muted`'ı `#a8b0bb` yapıldı (yedek
alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  site.css: --tracon-text-muted on --tracon-surface is 2.07:1 in the light theme; 4.5:1 required
  site.css: --tracon-text-muted on --tracon-surface-raised is 1.93:1 in the light theme; 4.5:1 required
```
Beklenen davranış doğrulandı (kızarıyor, oranı yazıyor), ama spec tek satır ve
`2.19:1` bekliyordu; gerçekte kapı token'ı **iki** yüzeye karşı ölçüyor
(`--tracon-surface` ve `--tracon-surface-raised`) ve rakamlar `2.07:1` /
`1.93:1`. Spec bu koşumda düzeltildi (kural 1 istisnası — rakam ve satır sayısı
koda göre yanlıştı). Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-010 — Görselsiz sidebar bölümü eklenince kapı bölümü adıyla söyler

**Gerçek sonuç**
`src/sidebar.mjs`'in `sidebar` dizisine `sectionImages`'ta karşılığı olmayan
`'MT-DKL-010 temp section'` etiketli boş bir bölüm eklendi (yedek alındı),
`node scripts/check-content.mjs`:
```
Content check failed with 1 issue(s):
  Sidebar section 'MT-DKL-010 temp section' has no link-preview image in src/sidebar.mjs
```
Tam beklenen biçimde: `Sidebar section '…' has no link-preview image`.
Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-011 — Diyagramı silinen sayfa muafiyet mekanizmasını adıyla gösterir

**Gerçek sonuç**
`guides/reliability.md`'nin tek `mermaid` bloğu geçici olarak silindi (yedek
alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  guides/reliability.md: 21194 bytes of narrative with no diagram or image. Add one, or add it to DIAGRAM_EXEMPT in this file with the reason it is a table page.
```
İkinci satır beklenen davranışı karşılıyor — sayfayı adıyla söylüyor ve
muafiyet mekanizmasını (`DIAGRAM_EXEMPT`) adıyla gösteriyor; spec'in "muafiyet
listesini gösterir" ifadesi listenin **tam içeriğini** basmak değil, mekanizmayı
adlandırmak anlamına geliyor (mesaj listenin kendisini dökmüyor, nasıl
ekleneceğini söylüyor). İlk satır (`llms-full.txt`) MT-DKL-008'deki gibi bu
mutasyonun yan etkisi, case'in odağıyla ilgisiz. Değişiklik geri alındı,
`git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-013 — Çifti olmayan renk token'ı eklenince kapı adıyla kızarır

**Gerçek sonuç**
`:root` (karanlık tema) bloğuna `--tracon-mtdkl13-test: #ff00ff;` eklendi
(yedek alındı, hiçbir yerde kullanılmayan yeni bir token), `node
scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  site.css: --tracon-mtdkl13-test is a colour with no contrast pair. Add it to CONTRAST_PAIRS or to DECORATIVE with the reason it carries no information.
  site.css: --tracon-mtdkl13-test is declared but nothing reads it
```
Birinci satır tam beklenen mesaj. İkinci satır beklenmedik ama tutarlı bir yan
etki: yeni token hiçbir yerde `var(...)` ile okunmadığı için MT-DKL-015'in
kapısı da aynı anda tetikleniyor — aynı minimal mutasyon iki kuralı birden
ihlal ediyor, bu **case'in beklediği** davranışın dışında değil, üstünde.
Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-014 — Açık tema tanımı silinince kapı "hesap atlanmaz" ilkesiyle kızarır

**Gerçek sonuç**
`:root[data-theme='light']` bloğundan `--tracon-accent-quiet` satırı geçici
olarak silindi (yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 1 issue(s):
  site.css: --tracon-accent-quiet has no value in the light theme
```
Tam beklenen mesaj, tek hata — kontrast hesaplaması **atlanmadan**
(front/back'ten biri yoksa direkt hataya düşülüyor, sessizce geçilmiyor,
`check-content.mjs:1097-1101`). Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-015 — Bir token'ın son kullanımı kaldırılınca kapı adıyla kızarır

**Gerçek sonuç**
`--tracon-warning`'in `site.css` içindeki **tek** kullanımı
(`--sl-color-orange: var(--tracon-warning);`) geçici olarak silindi (yedek
alındı; token'ın kendi `:root` tanımları dokunulmadı), `node
scripts/check-content.mjs`:
```
Content check failed with 1 issue(s):
  site.css: --tracon-warning is declared but nothing reads it
```
Tam beklenen mesaj, tek hata. Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-016 — Sayfa ağırlığı tavanın altında; en ağır sayfa adıyla ve bayt olarak yazılır

**Gerçek sonuç**
`npm run build` bu oturumda koşuldu, ardından `npm run check:weight`:
```
Weight: 1147 pages under 59000 B gzip. Heaviest: troubleshooting/index.html at 58394 B.
```
1138→1147 sayfa ve 58 000→59 000 B tavan sapması `check-weight.mjs:24-47`'nin
kendi tarihçe yorumuyla doğrulandı ve gerekçeli (içerik büyümesi, her raise
ölçüm ve tarihle gerekçelendirilmiş). Spec bu koşumda düzeltildi (kural 1
istisnası).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-017 — Tavan düşürülünce kapı kaç sayfanın aştığını söyler

**Gerçek sonuç**
`check-weight.mjs`'teki `CEILING` geçici olarak `59_000`'den `40_000`'e
düşürüldü (yedek alındı), `node scripts/check-weight.mjs`:
```
27 page(s) over the weight ceiling:
  api/package-tracon-abstractions/index.html: 40702 B gzip, ceiling 40000 B
  api/tracon/index.html: 52357 B gzip, ceiling 40000 B
  ... (27 satır)
Either reduce the page, or raise CEILING in this file with the measurement.
```
Beklenen davranış tam karşılandı: kaç sayfa (27) aştığı başlıkta yazılı.
Değişiklik geri alındı; `node scripts/check-weight.mjs` yeniden çalıştırılıp
tek satır temiz çıktıya döndüğü doğrulandı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-018 — Karar numarası sızınca kapı sayfayı ve nedeni söyler

**Gerçek sonuç**
`reference/glossary.md`'nin sonuna `<!-- MT-DKL-018 temp: K-382 -->` eklendi
(yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  reference/glossary.md: internal development history leaked into a public page
```
İkinci satır tam beklenen mesaj (`internal-history.mjs`'nin ortak dedektörü,
`check-content.mjs:1165`). İlk satır yan etki (MT-DKL-008 ile aynı desen).
Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-019 — Muaf sayfaya diyagram eklenince kapı muafiyeti düşürmeyi ister

**Gerçek sonuç**
`DIAGRAM_EXEMPT`'te listeli `reference/glossary.md`'ye geçici bir `mermaid`
bloğu eklendi (yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 4 issue(s):
  reference/glossary.md: Mermaid diagram is missing an accessible title
  reference/glossary.md: Mermaid diagram is missing an accessible description
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  reference/glossary.md: listed as a table page but now shows a figure; drop the exemption
```
Dördüncü satır tam beklenen mesaj. İlk ikisi test diyagramının `accTitle`/
`accDescr` eklemeden yazılmasının yan etkisi (gerçek bir diyagram eklenirse bu
iki uyarı da ayrıca doğru bir kapıdır), üçüncüsü MT-DKL-008 deseniyle aynı yan
etki. Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-020 — `npm run check` dört adımın hepsi

**Gerçek sonuç**
Bu ailenin "Koşmadan önce" bloğunu **harfiyen** izleyerek (`npm ci` → `npm run
check`) çalıştırıldığında ilk adımda (`check:content`) kızarıyor —
**`HATA-S3-001`** (yukarıda). Kök neden `npm run build` bir kez koşulmadan
`changelog.md`'nin diskte olmaması. `npm run build` bir kez koşulduktan sonra
dört adım da ayrı ayrı doğrulandı:
- `check:content` → `Content: 56 manual pages and 1147 total pages passed.`
  + `Contrast floor: text 5.49:1 (...); non-text 3.74:1 (...)`
- `build` → `1147 page(s) built in 20.77s`
- `check:links` → `Links: 188739 internal reference(s) ... none broken.` +
  `SEO: 1147 HTML, 1146 sitemap URL, 1146 erişilebilir sayfa; 0 hata.`
- `check:weight` → `Weight: 1147 pages under 59000 B gzip. Heaviest:
  troubleshooting/index.html at 58394 B.`

Yani üç adım her zaman temiz; birinci adım yalnız **gerçekten fresh** bir
checkout'ta (bu oturumun ilk denemesi gibi) kızarıyor. Case'in kendi ön koşulu
("Ön koşul: —") fresh checkout'u dışlamıyor, aksine ailenin "Koşmadan önce"
bloğu tam olarak bu sırayı öneriyor — bu yüzden **Kaldı** işaretlendi,
`HATA-S3-001` açıldı, spec **düzeltilmedi** (bu bir doküman `Beklenen sonuç`
yanlışlığı değil, gerçek bir sıralama kusuru).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

## MT-DKL-021 — Claim işaretinin tipini var olmayan bir tipe değiştirince kapı adıyla kızarır

**Gerçek sonuç**
`reference/configuration.md`'deki `<!-- claim:option
TraconSchedulingOptions.RunWorker=true -->` işareti geçici olarak
`TraconSchedulingOptionsNope.RunWorker=true` yapıldı (yedek alındı), `node
scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  reference/configuration.md: claim names 'TraconSchedulingOptionsNope', which is not a public sealed Options type
```
İkinci satır tam beklenen mesaj. İlk satır MT-DKL-008 deseniyle aynı yan etki.
Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-022 — Görünür değer işaretten sürüklenince kapı "backtick-quoted literal" ile kızarır

**Gerçek sonuç**
Aynı satırda **yalnız görünür** metin (`` `true` ``) `` `false` `` yapıldı,
işaret (`<!-- claim:option TraconSchedulingOptions.RunWorker=true -->`)
**dokunulmadı** (yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  reference/configuration.md: claim says TraconSchedulingOptions.RunWorker=true, but the sentence right before the marker does not state that value as a backtick-quoted literal (found: 'false')
```
İkinci satır tam beklenen davranış — görünür metin ile işaret birbirinden
sürüklenince yakalanıyor. Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-023 — `RunWorker`'ın varsayılanı `src/`'te silinince self-test kızarmalı

**Gerçek sonuç**
Koşulmadı. Case, `TraconSchedulingOptions.RunWorker`'ın `= true`
başlatıcısını **`src/` altında** silmeyi ister — kod donması (kural 1,
`AGENTS.md`) koşum sırasında **hiçbir istisna tanımadan** bunu yasaklıyor
(istisna yalnız `Beklenen sonuç` metni için var, kaynak kodu için yok). Bu bir
kusur değil, bu oturumun uygulayabileceği bir prosedürel çelişki. Kapanış
modunda (doğrulama sunucusu, `serit-kurulumu.md` §4) koşulması için karar
gerekir.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14 — kullanıcının açık talimatıyla, mutasyon-gözlem-geri
alma deseni)**
Bu turun kullanıcısı bu iki case (023/024) için **açıkça** mutasyon-gözlem-geri
alma sırasını istedi (aile 31'de aynı desenle zaten kanıtlanmış); kural 1'in
istisna listesine eklenen bir sapma değil, oturuma özgü açık yetki. Sıra:

1. Taban çizgisi: `dotnet build tests/Tracon.AspNetCore.FunctionalTests -c Release`
   → 0 uyarı 0 hata; `--filter-method "*DocumentedPolicyTests*"` → **2/2 geçti**.
2. `src/Tracon.Abstractions/Scheduling/TraconSchedulingOptions.cs:22`'de
   `public bool RunWorker { get; set; } = true;` → `public bool RunWorker { get; set; }`
   (başlatıcı silindi, C# `bool` varsayılanı `false` olur).
3. Yeniden derleme (0 uyarı 0 hata), test yeniden koşuldu:
   ```
   failed Tracon.AspNetCore.FunctionalTests.DocumentedPolicyTests.Every_marked_option_default_matches_the_real_type (26ms)
     Shouldly.ShouldAssertException : failures should be empty but had 1 item and was
     ["reference/configuration.md: claims TraconSchedulingOptions.RunWorker=true, but the real default is false."]
   ```
   Beklenen sonuçla **birebir** örtüşüyor.
4. `git checkout -- src/Tracon.Abstractions/Scheduling/TraconSchedulingOptions.cs`
   ile geri alındı; yeniden derleme + test → **2/2 geçti** (taban çizgisine dönüldü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-024 — Endpoint policy'si `src/`'te değişince self-test kızarmalı

**Gerçek sonuç**
Koşulmadı. Aynı çelişki: case `OpenAIChatCompletionsEndpoints.cs`'te
(`src/Tracon.AspNetCore/Endpoints/`) bir `RequireApiKeyScope` çağrısını
değiştirmeyi ister; kod donması bunu yasaklıyor. MT-DKL-023 ile aynı gerekçe.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14 — aynı açık yetkiyle, mutasyon-gözlem-geri alma)**
1. Taban çizgisi zaten yeşildi (MT-DKL-023'ün son adımı).
2. `src/Tracon.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs:53`'te
   `.RequireApiKeyScope(ApiKeyScope.RunsWrite)` → `.RequireApiKeyScope(ApiKeyScope.RunsRead)`.
   (Dosya, önceki oturumun notunun andığı `src/Tracon.AspNetCore/Endpoints/`
   altında değil, `src/Tracon.AspNetCore/OpenAICompat/` altında — bu önceki
   oturumun kendi notundaki bir dizin yanlışıydı, **spec'in kendisi** bir yol
   iddia etmiyor, yalnız sınıf adını veriyor; bu yüzden doküman düzeltmesi
   gerekmedi, case metni dokunulmadı.)
3. Yeniden derleme (0 uyarı 0 hata), test yeniden koşuldu:
   ```
   failed Tracon.AspNetCore.FunctionalTests.DocumentedPolicyTests.Every_marked_endpoint_policy_claim_is_actually_enforced (843ms)
     Shouldly.ShouldAssertException : deniedResponse.StatusCode should be HttpStatusCode.Forbidden but was HttpStatusCode.OK
     Additional Info: guides/openai-api.md claims POST /tracon/v1/chat/completions requires RunsWrite, but a key scoped only to RunsRead was not refused.
   ```
   Beklenen sonuçla ("yalnız `RunsRead` taşıyan anahtar reddedilmiyor")
   **birebir** örtüşüyor.
4. `git checkout -- src/Tracon.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs`
   ile geri alındı; yeniden derleme + test → **2/2 geçti**.

**Doğrulama (kod donması):** `git diff --stat 7e3a4de7..HEAD -- src samples tests`
bu iki mutasyon-geri-alma turundan sonra **boş** — kaynak, mutasyon öncesiyle
bit-bit aynı. `git status --short` de temiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-031 — Sonsuz animasyon yok; `prefers-reduced-motion` kuralı var

**Gerçek sonuç**
```
grep -rn "@keyframes|animation:|animation-iteration-count" src/ --include="*.astro" --include="*.css"
site.css:188: @media (prefers-reduced-motion: reduce) { *, *::before, *::after {
  animation: none !important; transition: none !important;
  scroll-behavior: auto !important; } }
```
Tüm `src/` içinde (bileşenler dahil) tek bir `@keyframes` veya `animation:`
bildirimi bu satır — yani sitede tanımlı **hiçbir** sonsuz/tekrarlı animasyon
yok, ve `prefers-reduced-motion: reduce` kuralı mevcut ve tüm animasyon/geçiş/
scroll davranışını kapatıyor. Statik kod taraması yeterliydi, tarayıcı
gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-033 — `dist/404.html` özel 404 sayfası

**Gerçek sonuç**
`npm run build` çıktısından `dist/404.html` doğrudan okundu:
```
<title>Page not found | Tracon</title>
Body: "Documentation Page not found This address does not point to a
  documentation page. The page may have moved, or the link may be
  incomplete. Open the documentation or return home. You can also use
  Search in the header to find a task, type, or endpoint."
```
"Page not found" başlığı, dokümana ("Open the documentation") ve ana sayfaya
("return home") bağlantı, aramaya yönlendirme ("use Search in the header") —
dördü de mevcut. Dosya gerçek host'un sunacağı statik `dist/404.html`;
`astro preview`'ın kendi dev-time 404'ünden ayrı olduğu doğrulanmadı (bu ayrım
için `astro preview`'da bilinçli olarak var olmayan bir yol denenmedi —
case'in kendi iddiası, bu turda ayrıca sınanmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-034 — Alarm emojisi sızınca kapı sayfayı ve satırı söyler

**Gerçek sonuç**
`reference/glossary.md`'ye `🚨` içeren geçici bir cümle eklendi (yedek alındı;
ilk denemede cümle içine yanlışlıkla `MT-DKL-034` yazınca **ayrıca**
`internal-history` kuralı da tetiklendi — `MT-[A-Z0-9-]+` deseni case
kimlikleri için de geçerli, bu ironik ama doğru bir davranış; ikinci denemede
o kalıp olmadan tekrarlandı). `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  reference/glossary.md:230: alarm emoji in shipped prose — "A temporary sentence with an alarm marker 🚨 inside prose fo". Keep the warning, drop the marker: ...
```
İkinci satır tam beklenen davranış — sayfa **ve** satır numarası (`:230`)
birlikte yazılıyor. Değişiklik geri alındı, `git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-035 — "an Tracon" yazılınca kapı bağlamıyla kızarır

**Gerçek sonuç**
`reference/glossary.md`'ye "This is an Tracon agent used only for a manual
check." cümlesi eklendi (yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  reference/glossary.md: "an Tracon" — the product name takes "a", not "an". Context: "ion names these terms appear in This is an Tracon agent used only for a manual c".
```
İkinci satır tam beklenen mesaj, bağlamı da taşıyor. Değişiklik geri alındı,
`git diff --stat` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-036 — Yanlış dosya boyutu iddiası kapıyı gerçek boyutla kızdırır

**Gerçek sonuç**
`guides/coding-agents.md`'deki "page concatenated, about 700 KB." → "...about
400 KB." yapıldı (yedek alındı), `node scripts/check-content.mjs`:
```
Content check failed with 2 issue(s):
  docs-site/public/llms-full.txt does not match capabilities.md; run: node docs-site/scripts/build-agent-map.mjs
  guides/coding-agents.md: says llms-full.txt is about 400 KB, but it is 784.4 KB. Update the number, or drop it if the page does not need to size the file.
```
İkinci satır tam beklenen davranış (gerçek ölçülen boyutu yazıyor). Spec'in
örnek rakamı (`713.1 KB`) bu koşumda ölçülen `784.4 KB`'tan farklı — içerik
büyüdükçe kayan bir örnek rakam, spec düzeltildi (kural 1 istisnası, sabit
sayı değil davranış doğrulanıyor). Değişiklik geri alındı, `git diff --stat`
boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-037 — 18 gezinme girişinin ekran görüntüleri, marka ve `/tracon` yolu

**Gerçek sonuç**
`docs-site/public/screenshots/` altında **40** PNG var (20 temel ekran +
20 `-light` varyantı) — spec'in "19 PNG" ön koşulu bu turda ölçülen sayıyla
uyuşmuyor (muhtemelen tema varyantları eklenmeden önceki bir ölçüm; sayı
`npm run check:content`'in kendi doğrulamasını etkilemiyor, bu yüzden spec
düzeltilmedi, yalnız not edildi). `navigation.ts`'te tam **18** ekran girişi
var ve `checkConsoleScreens` (`scripts/check-console-screens.mjs`) bunların
tamamı için `<path>.png` dosyasının var olduğunu doğruluyor — bu kontrol
`npm run check:content`'in bir parçası ve o zaten temiz geçti (MT-DKL-020),
yani **sıfır** eksik döndü.

Marka/yol doğrulaması için 3 örnek görsel gözle incelendi (tamamı 40 değil):
`dashboard.png` ve `diagnostics.png` yeni Tracon işaretini (logo + "Tracon"
metni sol üstte) taşıyor; `settings.png` ekranında "Instance" kartı
`Prefix: /tracon`, `UI base: /tracon/`, `API base: /tracon/` alanlarını
açıkça gösteriyor — `/tracon` yolu iddiası bu ekranda doğrulandı. Örneklenen
üç görüntüde de eski ürün adı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-038 — `site-seo-denetle.py` production build üzerinde sıfır hata

**Gerçek sonuç**
Repo kökünde (`docs-site/dist` bu oturumda zaten üretilmişti):
```
python3 scripts/site-seo-denetle.py
SEO: 1147 HTML, 1146 sitemap URL, 1146 erişilebilir sayfa; 0 hata.
```
Tekil title/description, canonical/JSON-LD, sitemap eşitliği, erişilebilirlik
ve başlık seviyesi kontrolleri sıfır hata ile geçti (varsayılan mod =
production, `--preview` verilmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-039 — Nginx HTTP sözleşmesi: redirect, port/scheme sızıntısı, noindex, 404

**Gerçek sonuç**
Resmi `nginx:1.27-alpine` imajıyla **geçici, bu oturumun kendi** container'ı
(`ap-s3-seo-nginx`, port `41753`, paylaşılan `ap-pg`/`ap-mssql`'e dokunmadan)
başlatıldı — `deploy/nginx.conf` `NGINX_ENVSUBST_FILTER=SITE_HOST` ile template
olarak yüklendi, `docs-site/dist` salt-okunur bağlandı:
```
docker run -d --rm --name ap-s3-seo-nginx -p 127.0.0.1:41753:8080 \
  -e SITE_HOST="tracon.dev" -e NGINX_ENVSUBST_FILTER=SITE_HOST \
  -v ".../docs-site/dist:/usr/share/nginx/html:ro" \
  -v ".../docs-site/deploy/nginx.conf:/etc/nginx/templates/default.conf.template:ro" \
  nginx:1.27-alpine

python3 scripts/site-http-denetle.py http://127.0.0.1:41753
HTTP: 24 yanıt denetlendi; 0 hata.
```
24 yanıt = 12 kontrol × 2 host (`tracon.dev` + `preview.invalid`). Slash
redirect query korunuyor, `preview.invalid` host'unda `X-Robots-Tag: noindex`,
gerçek host'ta yok, `404` sayfaları `Page not found` + `noindex` taşıyor —
hepsi 0 hata ile. Container test sonunda `docker stop` ile kaldırıldı
(`--rm` otomatik sildi); `docker ps` paylaşılan iki container'ın (`ap-pg`,
`ap-mssql`) dokunulmadığını doğruladı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-040 — Preview build tamamı `noindex`, canonical production'a işaret eder

**Gerçek sonuç**
Mevcut `dist/`'e dokunmadan ayrı bir çıktı dizinine build alındı:
```
TRACON_SITE_INDEXING=disabled npx astro build --outDir dist-preview
... 1147 page(s) built ...

python3 scripts/site-seo-denetle.py --preview --dist docs-site/dist-preview
SEO: 1147 HTML, 1146 sitemap URL, 1146 erişilebilir sayfa; 0 hata.
```
Örnek doğrulama (`dist-preview/index.html`):
```
<meta name="robots" content="noindex"/>
<link rel="canonical" href="https://tracon.dev/"/>
```
`noindex` var, canonical **production** adresine işaret ediyor, robots
taramayı engellemiyor (yalnız indexlemeyi), sıfır hata. `dist-preview/`
oturum sonunda silindi, `dist/` (production) dokunulmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-001 — 👤 Açılış sayfası hero ve kontrol şeridi

**Gerçek sonuç**
Koşulmadı — gerçek göz gerektirir (ailenin "Bilinen sınırlar" bölümü bu case'i
açıkça sayıyor). Fiziksel eylem tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-002 — 👤 Koyu temada on sayfa okunabilirlik taraması

**Gerçek sonuç**
Koşulmadı — gerçek göz gerektirir. Fiziksel eylem tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-003 — 👤 360px'te dokuz sayfada iç kaydırma davranışı

**Gerçek sonuç**
Koşulmadı — gerçek göz gerektirir (spec'in kendi notu: kriter ölçülebilir
olsa da "kendi içinde kayıyor" hissi göz ister). Fiziksel eylem tablosuna
eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-004 — 👤 Dokuz kılavuzda diyagram varlığı ve okunabilirliği

**Gerçek sonuç**
Koşulmadı — gerçek göz gerektirir. Fiziksel eylem tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-006 — 👤 Troubleshooting belirti dizini ve `Ctrl+F` bulunabilirliği

**Gerçek sonuç**
Koşulmadı — gerçek göz gerektirir. Fiziksel eylem tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-012 — 👤 Klavyeyle `Tab`: skip-to-content, odak halkası

**Gerçek sonuç**
Koşulmadı bu oturumda. Not: bu case Playwright ile **nesnel** olarak
koşulabilir (erişilebilirlik ağacı + `browser_press_key` ile `Tab` sırası,
odak halkasının `outline`/`box-shadow` CSS'i hesaplanabilir) — aile 02'nin
önceki bir oturumu benzer bir 👤 case'i (MT-CORE-081) Playwright ile koşup
`Geçti` işaretlemişti. Bu oturumda budget CLI case'lere ayrıldığı için
denenmedi; sonraki oturum için iyi bir aday.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright — `npx astro preview --port 4321`)**
`/` ve `/guides/production/` sayfalarında sayfa yüklendikten hemen sonra
gerçek klavye `Tab` tuşuyla (`browser_press_key`) doğrulandı:
```
{ text: "Skip to content", href: "#_top", outlineStyle: "solid", outlineWidth: "2px", outlineColor: "rgb(9, 101, 82)" }
```
İlk durak her iki sayfada da "Skip to content", hedefi `#_top` DOM'da var
(`document.getElementById('_top')` → `true`). Odak halkası yalnız ilk
durakta değil; sonraki 5 `Tab` durağında da (logo linki, ürün nav linkleri)
görünür kaldı (`outlineStyle: solid`, `outlineWidth: 2px`, tutarlı renk) —
"her yerde görünür" iddiası nesnel olarak doğrulandı. Aile 02'nin MT-CORE-081
emsaliyle aynı yöntem, aynı sonuç.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-025 — 👤 Açılış sayfası CTA'ları

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir (tıkla, URL'yi
oku, alt satırı oku) — sonraki oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright)**
Açılış sayfasında iki CTA bulundu: "What Tracon is" (`/getting-started/`) ve
"Capability map" (`/capabilities/`). Sırayla tıklandı, ikisi de doğru sayfaya
gitti (`browser_navigate` sonrası `Page URL` doğrulandı: `/getting-started/`
ve `/capabilities/`). CTA'ların altındaki satır: "In development. Not yet
published to NuGet or npm." — paketlerin yayımlanmadığını açıkça söylüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-026 — 👤 Dokuz şablon × dört genişlik × iki tema, yatay kayma yok

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir
(`browser_resize` + `browser_evaluate` ile `scrollWidth - clientWidth`) —
36 ölçüm noktası (9×4), hacimli ama mekanik; sonraki oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright — tam 72 ölçüm: 9 şablon × 4 genişlik ×
2 tema)**
Dokuz şablon: `/` (açılış) · `/guides/production/` (rehber) · `/concepts/`
(kavram) · `/reference/glossary/` (reference) · `/troubleshooting/` ·
`/api/tracon/` (.NET API) · `/http-api/` (HTTP API) · `/ui/` (console) ·
bilinmeyen bir yol (404). Her biri 360/640/1024/1440 px'te, her genişlikte
`document.documentElement.dataset.theme` doğrudan `'light'`/`'dark'`
yapılarak (bu, sitenin gerçek tema mekanizması — seçim `[data-theme]`
CSS seçicisiyle çalışıyor, doğrulandı) `scrollWidth - clientWidth` ölçüldü.

**68/72 nokta temiz (`0`).** **4 nokta kızardı: açılış sayfası, 1024 px, iki
temada da (`docOverflow=32`, `bodyOverflow=32`).** 360, 640 ve 1440 px'te
açılış sayfası da temizdi (`0`) — kusur yalnız 1024 px'te. Kök neden
bulundu: `docs-site/src/styles/landing.css:20`
```
.scope-rings { position: absolute; width: 22rem; height: 22rem; right: -3rem; top: -4rem; z-index: -1; overflow: hidden; pointer-events: none; }
```
`.scope-rings` (`Hero.astro`'daki dekoratif halka grafiği, `aria-hidden="true"`,
`pointer-events: none`) `.hero-figure`'ın sağ kenarından `-3rem` (48px) taşacak
şekilde konumlanmış — geniş ekranda (`≥71.99rem`'lik container margin'i bunu
yutuyor, 1440 px'te `0` ölçüldü) ve dar ekranda (`<49.99rem`'de
`landing.css:111` boyutu 14rem'e indiriyor, `right:0` yapıyor, 360/640 px'te
sorun yok) taşma görünmüyor. Ama **1024 px iki breakpoint arasında** —
`max-width:71.99rem` (~1152px) breakpoint'i yalnız `gap`/`padding` düzeltiyor,
`.scope-rings` boyutunu düşürmüyor; `max-width:49.99rem` (~800px) breakpoint'i
henüz devrede değil. Bu aralıkta container margin'i `-3rem` taşmayı
karşılamıyor ve sayfa gerçekten 32px yatay kayıyor — ölçülen ve beklenen değer
tam örtüşüyor (`getBoundingClientRect()` ile `.scope-rings`/`i` elemanının
`right: 1041` iken viewport `clientWidth`'in daha dar olduğu doğrulandı).
Diğer 8 şablon `.scope-rings`'i yüklemiyor (`landing.css` yalnız ana rotada
kullanılıyor, dosyanın kendi yorumu: "only the home route loads them") ve
1024 px'te hepsi temiz çıktı.

**Bu gerçek bir kusur, dokümanda değil kaynak CSS'te — kural 1 gereği
düzeltilmedi.** `HATA-S3-002` açıldı (aşağıda).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

## MT-DKL-027 — 👤 Arama: filtre grupları ve sayıları

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir — sonraki
oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright)**
Arama açıldı (üst çubuktaki "Search" düğmesi), `tenant isolation` yazıldı.
Üç filtre grubu sayılarıyla geldi: `.NET API (45)` · `Documentation (16)` ·
`HTTP API (0)`. Sonuç sayısı yazılı: "61 results for tenant isolation".
`Documentation (16)` filtresi tıklandı, sonuç metni "16 results for..."a
düştü — filtre seçmek listeyi gerçekten daraltıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-028 — 👤 Sonuçsuz arama sorgusu

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir — sonraki
oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright)**
Arama kutusuna sonuçsuz bir sorgu (`zzznonexistentqueryxyz`) yazıldı.
Görünür (bounding box > 0, `display`/`visibility` gizli değil) elemanlar
arasında: "No results for zzznonexistentqueryxyz" + "Search by task, type
name, or endpoint. Use the content filters to narrow the results." +
"Browse the documentation" linki. DOM'da ayrıca bir "Search could not load.
Check your connection and try again." bloğu var ama bu **gizli** (bounding
box 0×0) — yalnız gerçek bir ağ/indeks hatasında gösterilen ayrı bir panel,
boş sonuç durumunda görünmüyor. Yani boş sonuç gerçekten bir hata gibi
görünmüyor, yardım metni ve dokümana dönüş bağlantısı çıkıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-029 — 👤 Sayfa yüklenince `Tab`: skip-to-content, landmark'lar, tek `h1`

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir
(erişilebilirlik ağacında landmark rolleri ve `h1` sayısı sayılabilir) —
sonraki oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright)**
`/` ve `/guides/production/` üzerinde doğrulandı. İlk `Tab` durağı "Skip to
content", hedefi `#_top` DOM'da mevcut. Landmark sayımı (`querySelectorAll`):
`header=1, main=1, footer=1` her iki sayfada da; `nav` sayısı birden fazla
(2 ve 5) ama her biri **ayrı `aria-label`** taşıyor (`"Product"`, `"Footer"`,
rehber sayfasında ayrıca sidebar/TOC nav'ları) — bu geçerli bir
erişilebilirlik deseni (çoklu nav landmark, her biri etiketli), spec'in
"birer landmark olarak bulunur" ifadesiyle çelişmiyor: aranan landmark
*türleri* (header/nav/main/footer) hepsi mevcut. Her iki sayfada `h1` sayısı
tam **1**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-030 — 👤 Koyu tema + %200 yakınlaştırma, taşma yok

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir
(`browser_resize` ~640px + tema geçişi + `scrollWidth` ölçümü) — sonraki
oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright)**
640 px genişlik (≈200% yakınlaştırmanın karşılığı), koyu tema
(`document.documentElement.dataset.theme = 'dark'`). `/troubleshooting/` ve
`/api/tracon/` üzerinde `body` arkaplanı `rgb(16, 25, 28)` (koyu) — MT-DKL-026
sweep'inin 640 px verisiyle de örtüşüyor: bu genişlikte 9 şablonun 9'u da
`docOverflow=0` (bkz. MT-DKL-026 kaydı, aynı 640 px ölçümü orada da alındı).
Taşma yok, arkaplan koyu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKL-032 — 👤 Kod bloğu kopyalama düğmesi ve başlık çapası

**Gerçek sonuç**
Koşulmadı bu oturumda. Playwright ile nesnel olarak koşulabilir (tıkla,
panoyu oku, adres çubuğunu/`location.hash`'i oku) — sonraki oturum adayı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Gerçek sonuç (oturum 14, Playwright)**
Açılış sayfasında `Tools/OrderTools.cs` kod bloğunun "Copy to clipboard"
düğmesine tıklandı; `navigator.clipboard.readText()` panoda kod bloğunun
**tam** metnini döndürdü (satır satır örtüşüyor: `using Tracon;` ... `}`).
Ardından "A tool call failed. Now what?" başlığının çapa linkine (`href
="#a-tool-call-failed-now-what"`) tıklandı: adres çubuğu
`http://localhost:4321/#a-tool-call-failed-now-what` oldu ve hedef başlık
görünür alana geldi (`getBoundingClientRect().top ≈ 96px`, viewport içinde).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S3-002 — Açılış sayfası 1024 px'te 32px yatay taşıyor (`.scope-rings`)

- **Case:** MT-DKL-026
- **Önem:** Düşük (dekoratif bir arka plan grafiği; içerik okunabilirliğini
  ya da işlevi bozmuyor, yalnız 1024 px'lik tek bir genişlik aralığında yatay
  kaydırma çubuğu görünür kılıyor)
- **İzlek:** C (docs-site tasarım/CSS)
- **Ortam:** `npx astro preview --port 4321` (production `dist/` build),
  Playwright `browser_resize(1024, 900)`, macOS arm64

**Beklenen**
MT-DKL-026: dokuz şablonun hiçbirinde 360/640/1024/1440 px'te, açık ve koyu
temanın ikisinde de, sayfa gövdesi yatay kaymıyor
(`scrollWidth - clientWidth == 0`).

**Gerçekleşen**
Açılış sayfası (`/`) 1024 px'te, hem açık hem koyu temada:
```
{ vw: 1024, light: { docOverflow: 32, bodyOverflow: 32 }, dark: { docOverflow: 32, bodyOverflow: 32 } }
```
360, 640 ve 1440 px'te aynı sayfa `0`; diğer 8 şablon 1024 px'te de `0`.

**Kök neden**
`docs-site/src/styles/landing.css:20`:
```css
.scope-rings { position: absolute; width: 22rem; height: 22rem; right: -3rem; top: -4rem; z-index: -1; overflow: hidden; pointer-events: none; }
```
`.scope-rings` (`docs-site/src/components/Hero.astro:17`, `aria-hidden="true"`,
dekoratif halka grafiği) `.hero-figure`'ın sağ kenarından `-3rem` (48px) dışarı
taşacak şekilde konumlanmış. İki duyarlı-tasarım kırılma noktası var:
`landing.css:111` (`max-width:49.99rem`, ~800px) altında boyutu 14rem'e
düşürüp `right:0` yapıyor — dar ekranda taşma yok. `max-width:71.99rem`
(~1152px) kırılma noktası (`landing.css:95-100`) ise yalnız `gap`/`padding`
düzeltiyor, `.scope-rings` boyutunu **düşürmüyor** — geniş ekranda (≥1152px)
container margin'i `-3rem`'i emiyor (1440px'te `0` ölçüldü). **800–1152px
arasında** (case'in test genişliği 1024px bu aralıkta) container margin'i bu
taşmayı karşılamaya yetmiyor ve `.scope-rings`'in `right: 1041px`'e uzanan
gerçek konumu (`getBoundingClientRect()` ile doğrulandı) sayfanın
`scrollWidth`'ini 32px büyütüyor.

**Yeniden üretme**
1. `npx astro preview --port 4321` (production build)
2. Tarayıcı genişliğini 1024px yap, `/` sayfasını aç
3. `document.documentElement.scrollWidth - document.documentElement.clientWidth` → `32`

**Kanıt**
- Ölçüm çıktısı yukarıda alıntılandı (bu oturumun Playwright oturumu)
- `docs-site/src/styles/landing.css:20-24` (`.scope-rings` kuralı),
  `:95-100` (71.99rem breakpoint, boyutu değiştirmiyor),
  `:111-113` (49.99rem breakpoint, boyutu düşürüyor)
- `docs-site/src/components/Hero.astro:17` (`.scope-rings` markup'ı)

**Kapsam**
Yalnız açılış sayfası (`landing.css` yalnız ana rotada yüklenir, dosyanın
kendi yorumu: "only the home route loads them"), yalnız 1024 px civarı
(800–1152px aralığı ölçülmedi tek tek, ama 1024 iki breakpoint'in ortasında
ve kırmızı çıktı). Diğer 8 şablon ve diğer 3 genişlik temiz. Yayın hattını
bloklamıyor; yalnız bu dar aralıktaki gerçek kullanıcılarda dekoratif bir
grafik sayfanın kaydırılabilir genişliğini büyütüyor.

---

## Fiziksel eylem listesi / sonraki oturuma bırakılan `👤` case'ler

Bu 5 case bu oturumda da koşulmadı — ailenin kendi "Bilinen sınırlar"
bölümünde **gerçek göz** gerektirdiği açıkça yazılı, ölçülebilir bir kapı yok.
Önceki oturumun bıraktığı diğer 8 `👤` case (12, 25, 26, 27, 28, 29, 30, 32)
bu oturumda Playwright ile koşuldu (yukarıda) — 26 hariç (`Kaldı`,
`HATA-S3-002`) hepsi `Geçti`.

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-DKL-001 | Hero'nun "okunaklı" olup olmadığı hesaplanamaz | `/` sayfasını aç, hero ve dört kontrol şeridinin ilk ekranda rahat okunduğunu doğrula |
| MT-DKL-002 | Koyu temada "okunaklı" hesaplanamaz | Temayı koyuya çevir, on sayfa gez, hiçbir yüzeyin okunmaz olmadığını doğrula |
| MT-DKL-003 | İç kaydırmanın "doğru hissettiği" hesaplanamaz | 360px'te dokuz sayfada tablo/kod/diyagramın kendi içinde kaydığını gözle doğrula |
| MT-DKL-004 | Diyagramın "okunabilir" olması hesaplanamaz | Dokuz kılavuzu aç, diyagramların iki temada da okunduğunu doğrula |
| MT-DKL-006 | Belirti dizininin kullanılabilirliği hesaplanamaz | `troubleshooting` sayfasını aç, 14 girişli dizini ve `Ctrl+F` bulunabilirliğini doğrula |
