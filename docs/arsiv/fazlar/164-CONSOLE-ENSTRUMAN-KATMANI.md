# Faz 164 — Console'un Enstrüman Katmanı

> **Durum:** ✅ Tamamlandı (2026-09-12)
> **Kaynak:** Kullanıcı isteği · **F-223** (aday listesi boştu; kalem doğrudan
> kullanıcıdan geldi ve dört tasarım kararı onunla birlikte alındı)
> **Önkoşul:** [Faz 163](163-MARKA-VE-DOKUMANTASYON.md) — marka dili,
> teal palet ve `hafiza/marka.md` orada kuruldu; bu faz onu console'a taşır
> **Paketler:** `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Değişmiyor. HTTP sözleşmesi, `UseUI()` ve `MapTracon()` aynı
> **Tüketici yüzeyi:** Console'un tamamı · `docs-site/src/content/docs/ui.md` ·
> 19 ekran görüntüsü · `capabilities.md` console satırı
> **Manuel test alanı:** `docs/manuel-test/09-ARAYUZ-GENEL.md`
> **Taban:** `7cbc0f354337bd8a6be46eaa466f1eff087aa831`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show dd152a0b:docs/arsiv/fazlar/164-CONSOLE-ENSTRUMAN-KATMANI.md
> ```
>
> Damıtıldı 2026-09-12 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Console bugün çalışır ve eksiksizdir; **konuşmaz.** 30 ekran, eski markanın mor vurgusunu ve okuma-ağırlıklı bir yerleşimi taşır. Site Faz 163'te bir operasyon ekranı diline kavuştu; console hâlâ eski dildedir.

## Onaylanan tasarım kararları

Dördü de kullanıcıyla plan yazılmadan **önce** kararlaştırıldı. Uygulama bunları
tartışmaz:

| Karar | Seçim |
|---|---|
| Kapsam | Görsel katman **ve** ekran bazında UX yeniden tasarımı. İki faza bölünür: 164 enstrüman katmanı + kanıt dilimi, 165 kalan ekranlar |
| Kimlik | Sitenin teal'ı, **koyu tema öncelikli**. Açık tema tam destekli kalır |
| "Lite" | 250 KB gzip tavanı **korunur**, **yeni runtime bağımlılığı yok**. Erişilebilirlik elle yazılır |
| Yoğunluk | Operatör yoğunluğu: 13-14 px taban, sıkı satır yüksekliği, monospace kimlikler |

---

## Bitiş Ölçütleri (DoD)

- [x] Token seti `--tracon-*` önekinde; her token iki temada değer alıyor; koyu
      varsayılan. Kontrast tabanı ölçüldü ve `kalite-sozlesmesi.md` F tablosuna
      yazıldı. — `scripts/check-tokens.mjs`: 27 token × 2 tema, 70 çift, metin
      **5,22:1**, metin dışı **3,56:1**. `--ap-*` deposunda sıfır kaldı.
- [x] 19 primitif yeniden yazıldı, **imzaları korundu**; beş yeni primitif
      (`Dialog`, `Menu`, `Tooltip`, `Toolbar`, `StatusDot`) eklendi. — Altıncısı
      da eklendi: `Unauthorized` (§164.5 durum dilinin eksik parçası). Prop
      yalnız **eklendi**; hiçbiri yeniden adlandırılmadı veya kaldırılmadı.
- [x] Kabuk gruplu navigasyonu ve komut paletini taşıyor; `nav.*` anahtar adları
      değişmedi ve ekran görüntüsü kapısı 18 girişin hepsini buluyor. — Envanter
      artık `components/navigation.ts`; kenar çubuğu, palet ve kapı ondan okur.
      `triggers` ve `diagnostics` palette **ilk kez** erişilebilir.
- [x] Dört durum dili (boş · yükleniyor · hata · yetkisiz) tek kaynaktan geliyor.
      — `Empty` (birincil aksiyonlu) · `Loading` (iskelet) · `ErrorNote`
      (`onRetry`) · `Unauthorized`.
- [x] Beş kanıt ekranı yeni katman üzerinde; **mevcut 58 E2E olgusunun hiçbiri
      değiştirilmedi** ve hepsi yeşil. — Bağımsız denetim `git diff` ile
      doğruladı: `UiTests.cs`'te yalnız ekleme, artı `Session.OpenAsync`'e
      isteğe bağlı bir parametre ve taşma yardımcısının hata mesajı.
- [x] Klavye sözleşmesi E2E ile kanıtlandı: odak sırası, `Esc`, odak dönüşü.
      — `Command_palette_takes_focus_traps_Tab_and_returns_focus_to_its_trigger_on_Escape` ·
      `Shortcut_help_dialog_opens_with_the_question_mark_and_closes_on_Escape` ·
      `Every_one_of_the_first_ten_Tab_stops_has_an_accessible_name_and_a_visible_focus_ring`.
- [x] Beş kanıt ekranı 375 px'te yatay taşmıyor; E2E testi genişletildi.
      — Genişletilmedi, **eklendi** (bkz. Plandan Sapmalar §5):
      `Proof_slice_screens_do_not_overflow_horizontally_at_375px_width`. İlk
      koşumda gerçek bir kusur buldu (87 px).
- [x] Bundle 250 KB gzip altında; **tavan yükseltilmedi**; `package.json`
      `dependencies` hâlâ dört paket. Ölçülen değer dokümana yazıldı. —
      **184,1 KB**; `JS_BUDGET_BYTES` değişmedi; bağımlılık kümesi artık
      `postbuild.mjs` tarafından zorlanıyor (K-758).
- [x] 19 ekran görüntüsü yeni görünüşle yeniden üretildi; `ui.md` güncellendi.
      — Koyu temada; `ui.md`'ye "How it is laid out" bölümü eklendi.
- [x] Dört doğrulama kapısı sıfır uyarı (`kapi.py kapanis`).
- [x] `samples/Tracon.Api` ile gerçek `run` — çıktı belgeye yazıldı (aşağıda).
- [x] `secret` taraması boş döndü. — `kapi.py tarama` ✅ temiz.
- [x] Manuel kabul case'leri `docs/manuel-test/09-ARAYUZ-GENEL.md` içine eklendi;
      otomatikleştirilebilenler koşuldu. — **MT-UI-044..054**; 044 · 046 · 050 ·
      053 otomatik karşılıklarıyla `UiTests`'te koştu.
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı.

### Örnek uygulama koşumu

`samples/Tracon.Api`, gerçek bir HTTP çağrısı, akışlı yanıt:

```
POST /tracon/api/agents/support/run   {"message":"where is ORD-7"}
→ event: run    {"runId":"01a09646-c56a-7b36-9a71-00032889a469"}
→ event: update × 4   "Echo: " / "where " / "is " / "ORD-7"

GET /tracon/api/runs/01a09646-c56a-7b36-9a71-00032889a469
  status      Completed
  modelId     echo-1        modelProvider  echo
  startedAt   15:40:15.646  completedAt    15:40:15.878   (232 ms)
  eventCount  6             isStreaming    true
```

Aynı `run` console'da açıldı (`.playwright-mcp/console-final-run-detail.png`):
saklanan tercih silinerek yüklendi ve **koyu** açıldı (K-757), yedi ölçüm
kutusu tek satıra sığdı, `message.delta` satırları canlı vurgu tonunda,
`run.started` bilgi tonunda, kimlik monospace ve kopyalanabilir.

## Plandan Sapmalar

> Plan doğru bir hedef anlattı ve iki yerde bayattı. İkisi de burada.

**1. Taban ölçüm yanlıştı: 175,9 KB değil, 180,0 KB.** Planın kanıt tablosu
`ui.md:331`'i alıntılıyordu; o sayı Faz 108'den kalmaydı. `npm run build` ile
ölçülen gerçek taban **180,0 KB gzip**'ti — pay ~74 KB değil, **70 KB**.
Kapanışta ölçülen değer **184,1 KB**'dir; enstrüman katmanının tamamı (beş yeni
primitif, durum dili, gruplu kabuk, kanıt dilimi) **4,1 KB gzip** eklemiştir.
Tavan yükseltilmedi, `package.json` `dependencies` hâlâ dört isimdir ve artık
bunu bir kapı zorluyor.

**2. 🚨 "Koyu öncelikli" bir CSS sorusu DEĞİLDİR — `prefers-color-scheme`
"seçilmemiş" durumunu bildiremez.** Plan §164.1 "sistem tercihi yine okunur,
varsayılanın yönü değişir" diyordu. Token katmanı planlandığı gibi yazıldı
(`:root` koyu, `:root[data-theme='light']` açık), ama **varsayılanın yönü CSS'te
değiştirilemez**: medya sorgusunun `no-preference` değeri modern tarayıcıdan
kalktı ve seçim yapmamış her makine `light` döner. İlk uygulama sorguyu
`(prefers-color-scheme: light)` olarak çevirdi; gerçek tarayıcıda ölçüldü
(`ColorScheme.NoPreference` ile açılan Playwright bağlamı **`light`** verdi) ve
düştü. Çalışan çözüm: **varsayılan saklanan tercihtir** — hiçbir şey
saklanmamışsa `dark`. Ayarlar'daki "sistemi izle" seçeneği aynen korundu ve
seçildiğinde eskisi gibi davranır. Kayıt: **K-757**.

**3. Onay ekranının doğrulama diyaloğu yazıldı ve GERİ ALINDI.** §164.7 onaylar
ekranını "geri alınamaz aksiyon" deseninin kanıtı olarak seçiyordu; ilk uygulama
her iki cevabı da bir `Dialog` arkasına aldı. Mevcut bir E2E olgusu
(`Approvals_screen_shows_pending_request_and_run_completes_once_approved`) düştü:
tek tık ile karar veriliyordu, artık iki tık gerekiyordu. DoD "mevcut 58 E2E
olgusunun hiçbiri değiştirilmedi" diyor ve kapsam "ekranın **ne yaptığı**"nı
dışarıda bırakıyor — bir onay adımı ekranın etkileşim sözleşmesini değiştirir.
Diyalog kaldırıldı. Yerine kararın **sonucu** karar anında okunur hâle geldi:
argümanlar satırın üstünde, süre sonu amber ve mutlak saatte, ve her iki düğme
`title` özniteliği yerine **gerçek bir tooltip** taşıyor (klavyeyle ve dokunmatik
ekranda erişilebilir). Onay adımının kendisi `docs/ADAYLAR.md`'ye aday olarak
kaydedildi.

**4. `Field`'ın zorunluluk yıldızı erişilebilir addan çıkarılamadı.** İlk uygulama
`*` işaretini `aria-hidden` yaptı; `Trigger_created_from_UI_is_listed` düştü
(erişilebilir ad "Name *" → "Name"). Kod düzeltildi, test değil. Gerekçe testin
kendisinden bağımsız: `Field`'ın düz-`children` biçimi kontrolün üstüne
`aria-required` **yazamaz** (çocuk bir `ReactNode`'dur), dolayısıyla yıldızı
gizlemek zorunlu bir alanı isteğe bağlı gibi duyururdu. Yıldız adın parçası
kaldı; `aria-describedby` ve `aria-invalid` bağlaması ise render-prop biçimiyle
geldi ve agent editörünün şema alanında kullanıldı.

**5. 375 px testi GENİŞLETİLMEDİ, yeni bir olgu eklendi.** Plan
"`UiTests:95` genişletilir" diyordu; DoD "hiçbir mevcut olgu değiştirilmedi"
diyor. İkisi çelişiyor. Mevcut olgu (dört genel ekran) olduğu gibi bırakıldı ve
beş kanıt ekranı için ayrı bir olgu yazıldı. Yan fayda: ikisi farklı şeyleri
kanıtlıyor — biri **boş** kabuğu, diğeri **dolu** ekranı, ve taşma yalnız
ikincisinde çıktı.

**6. Ekran görüntüsü kapısı sözlükten değil, gezinme TABLOSUNDAN okur.**
`check-console-screens.mjs` `nav\.([A-Za-z]+)` desenini `en/common.ts` üzerinde
koşuyordu — yani `nav.*` ad alanındaki **her** anahtar bir ekran sanılıyordu.
Bu fazda eklenen "İçeriğe geç" bağlantısı (`nav.skipToContent`) kapıyı kırmızı
yaptı ve kendisinin bir ekran görüntüsünü istedi. İki düzeltme birlikte yapıldı:
anahtar `shell.skipToContent`'e taşındı (gerçekten bir ekran değil) **ve** kapı
artık `components/navigation.ts`'i okuyor — kenar çubuğu ile paletin paylaştığı
tek envanter. Sınıfın tamamı kapandı; regresyon testi eklendi.

**7. Kapsam dışı kusurlar bulundu ve düzeltildi** (kullanıcı isteği). Listesi
"Yol Üzerinde Kapatılan Kusurlar" bölümündedir.

---

## Yol Üzerinde Kapatılan Kusurlar

Hiçbiri bu fazın konusu değildi; hepsi bu fazın işi sırasında görüldü.

| Kusur | Kanıt | Düzeltme |
|---|---|---|
| **Dashboard 375 px'te 87 px yatay taşıyordu** | Yeni E2E olgusu. Eski 375 px testi **run seed etmiyordu**, bu yüzden grafikler boş çiziliyor ve taşma hiç doğmuyordu | `Panel` artık `min-w-0`: bir panel neredeyse her zaman grid/flex öğesidir ve `min-width: auto` onu içindeki en geniş şeyin altına inmekten alıkoyar. Model kırılım satırı da sarmalıyor: dört sabit sütun + esnek çubuk bir telefona sığmıyordu |
| **Console favicon'u eski markanın mor üçgeniydi** | `index.html`, Faz 162/163 yeniden adlandırmasından kalma | Tracon radar işareti, satır içi veri URI'si olarak. Ayrıca soğuk yüklemede beyaz parlama vardı: `<head>` artık zemin rengini kendisi taşıyor |
| **Zaman serisi grafiğinin son eksen etiketi kırpılıyordu** | Ekran görüntüsü (`dashboard.png`) | Yatay iç boşluk 12 → 16 px; ilk ve son etiket artık uçlarına yaslanıyor, ortalanmıyor |
| **Durum dağılımı çubuğu %16 tint'ti — koyu temada görünmüyordu** | Ekran görüntüsü: tek bir koyu blok | Seri renginin kendisi kullanılıyor. `tint()` yardımcısı bu dosyada ölü kaldı ve silindi |
| **Runs boş durumu yarım cümle gösteriyordu** | "Send a message in the or call the agent through the API." — `before`/`after` parçaları aradaki bağlantıyı sarmak için vardı | Tek cümlelik `runs.empty.body`; bağlantının işini birincil aksiyon düğmesi yapıyor |
| **Runs kapsam filtresi yanlış etiketliydi** | Filtre şeridinde "TREE TOKENS" yazıyordu; kontrol kök/alt run seçimidir | Yeni anahtar `runs.filter.scope` |
| **`Menu` tetikleyicisinin ok işareti sağa bakıyordu** | Görsel denetim | `ChevronIcon` konsolun açılım okudur (sağa bakar); menü tetikleyicisi aşağı bakar, açıkken yukarı |

---

## Bu Fazda Verilen Kararlar

| Numara | Karar |
|---|---|
| **K-757** | Console'un varsayılan teması SAKLANAN TERCİHTİR (`dark`), medya sorgusu DEĞİL |
| **K-758** | Console'un runtime bağımlılık kümesi DÖRT isimle kapıya bağlandı |

Karar defterine yalnız bu ikisi girdi. Token adlandırması (`--ap-*` →
`--tracon-*`), yoğunluk ölçeği, gruplu gezinme ve durum sözlüğü gömülü varlığın
iç tercihleridir: public API'ye, kalıcı veriye veya bir güvenlik sınırına
dokunmazlar ve bu dokümanda kalırlar.

---

## Açık Soruların Cevapları

1. **Navigasyon grupları:** `Operate` / `Configure` — Türkçesi `Operasyon` /
   `Yapılandırma`. Metafor kullanılmadı: marka §4 bir kullanıcının bir şeyi
   *yapmaya* çalıştığı yerde metaforu yasaklar ve gezinme tam olarak orasıdır.
2. **Grafikler:** ayrı bir seri paleti kuruldu (`--tracon-series-1..6`). Gerekçe
   ölçülebilir: bir seri bir **kimliktir**, bir durum değil — kırmızı/yeşil bir
   çizgi bir hüküm gibi okunur. Altı seri de panele karşı ≥3:1 ölçülür.
3. **Yoğunluk tercihi:** eklenmedi. Tek ölçek kondu, ferah mod talebi çıkmadı.

---

## Denetim Bulguları

Taze bağlamlı bağımsız denetçi koştu (2026-09-12). Temiz çıkan başlıklar: 3.5
(imza-gövde kayması), 3.6 (plan dışı public API), 3.7 (repo kuralları).

Denetçi dört şeyi **kendisi ölçtü**, dokümanın iddiasına güvenmedi: 58 olgunun
değişmediğini `git diff` ile, 64/64 yeşili tam koşumla, primitif imzalarını
tek tek, ve **kapıların gerçekten kapı olduğunu ikisini de elle kırarak** (token
silme · `@media` içine token · kontrast düşürme · beşinci bağımlılık) — her
denemeden sonra dosyaları `shasum` ile doğrulayarak geri koydu.

### 🔴 — kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | **`Tooltip`'in `aria-describedby` bağlaması DOM'a hiç ulaşmıyordu.** `Button` kapalı bir prop listesiyle render ediyor ve bilmediği prop'u sessizce yutuyor; tooltip görsel olarak çalışıyor, testler yeşil, ve onay ekranı faz **öncesinde** `title` ile taşıdığı açıklamayı kaybetmiş oluyordu. Sınıf taraması `Select`'i de gösterdi: `Field`'ın render-prop biçimindeki `aria-describedby`/`aria-invalid` orada da yutuluyordu | **Düzeltildi.** `Button` ve `Select` artık bağlamaları açıkça alıp DOM'a yazıyor. Yeni test dosyası `components/ui.test.tsx` dördünü birden kanıtlıyor: tooltip → düğme, alan hatası → `TextInput`, alan hatası → `Select`, ve zorunluluk yıldızının erişilebilir adda kalması |

### 🟡 — kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 2 | Agent editörünün **yükleme hata yolu yoktu**: `ready` yalnız başarıda true olduğu için çözülemeyen bir tanım (başka kiracının kaydı, silinmiş agent, düşmüş depo) ekranı sonsuza dek `aria-busy` iskeletinde bırakıyordu | **Düzeltildi.** `useAgentEditor` sorguyu dışa açıyor, ekran `ErrorNote` + "yeniden dene" çiziyor. Regresyon testi: `agent-editor.test.tsx` → 404 fixture'ı, `role="alert"` ve `error-retry` beklenir, `role="status"` beklenmez |
| 3 | Ekran görüntüsü kapısının regresyon testi adının iddia ettiğini koşmuyordu; ayrıca kapının envanter deseni **prop sırasına** bağlıydı — sırası değişen bir giriş sessizce sıfır ekran olarak ayrıştırılırdı | **Düzeltildi.** Kapı artık girişleri bağımsız sayıyor (`label: 'nav.*'`) ve ayrıştırılanla karşılaştırıyor; iki gerçek test eklendi (tabloda olmayan `nav.*` anahtarı hiçbir şey istemez · sırası değişmiş giriş **gürültüyle** düşer) |
| 4 | `index.html` iki temanın zemin rengini elle kopyalıyordu ve hiçbir kapı görmüyordu; ayrıca saklı tercihi `light` olan kullanıcı artık **koyu** parlama görüyordu | **İkisi de düzeltildi.** `check-tokens.mjs` kabuğun zemin renklerini `--tracon-bg` ile karşılaştırıyor. Parlamanın kendisi kaynağında kapandı: `<head>` içinde stylesheet'ten **önce** koşan küçük bir script saklanan tercihi `data-theme` olarak basıyor; `theme.ts` yine tek kaynak, yalnız yazma anı öne alındı |
| 5 | Depo kökünde takipsiz iki PNG kalmıştı | **Silindi.** |

### 🟢 — kapatıldı (üçü) ve gerekçelendi (biri)

| # | Bulgu | Sonuç |
|---|---|---|
| 6 | `dialog.tsx` var olmayan bir teste atıf yapıyordu | Düzeltildi; iki gerçek olgunun adı yazıldı |
| 7 | `FocusProbe` hiçbir kuralı olmayan bir sınıfı ekleyip siliyordu — no-op | Kaldırıldı |
| 8 | Kapının taban sabitleri 5,2 / 3,5; ölçülen 5,22 / 3,56 | **Bilerek.** Son haneye yazılan bir taban, algılanamaz bir yuvarlamayı kırmızı derlemeye çevirir ve insanlara tabanı düzenlemeyi öğretir. Gerekçe koda yazıldı |
| 9 | `runs.tsx`'te `filtered`, `?sessionId=` ile gelen daraltmayı saymıyordu; boş liste "henüz hiçbir şey çalıştırılmadı" diyordu | Düzeltildi — ve iki kavram ayrıldı: `resettable` (bu ekranın kontrollerinin geri alabildiği) ile `narrowed` (boş durumun ne demesi gerektiği). Tek bayrak kullanmak hiçbir şeyi temizlemeyen bir "Filtreleri temizle" düğmesi üretirdi |

## Sonraki Faza Devir Notu

**Faz kapandı.** Dört kapı sıfır uyarı, 64/64 E2E, bağımsız denetimin bir 🔴 ve
dört 🟡 bulgusu kapatıldı, 19 ekran görüntüsü yeniden üretildi, site yayınlandı.

**Faz 165'in kapsamı: kalan 25 ekran.** Kanıt dilimi beş deseni ispatladı;
her ekran bunlardan birine benzer:

| Desen | Kanıtı | Bu desene giren ekranlar |
|---|---|---|
| Metrik + grafik + zaman aralığı | `dashboard.tsx` | `diagnostics` |
| Liste + filtre + sayfalama | `runs.tsx` | `sessions` · `jobs` · `evals` · `experiments` · `audit` · `agents` · `tools` · `skills` · `models` · `mcp` · `triggers` · `workflows` |
| Derin detay + canlı akış | `run-detail.tsx` | `session-detail` · `job-detail` · `eval-run-detail` · `workflow-detail` · `playground` |
| Form + doğrulama + sürüm | `agent-editor/` | `workflow-editor` · `skills` editörü · `settings` |
| Karar yüzeyi | `approvals.tsx` | `experiment-detail` (promosyon) · `agent-detail` (rollback) |

**Sonraki oturumun bilmesi gerekenler:**

1. **Bundle payı 66 KB.** 184,1 / 250 KB. Kalan 25 ekran yeni primitif
   getirmemeli — hepsi zaten yazıldı; iş onları **kullanmaktır**. Ölçüm her
   ekrandan sonra yapılır, faz sonunda değil.
2. **🚨 Kapalı prop listesi taşıyan her primitif bir tuzaktır.** Denetimin 🔴
   bulgusu buydu: `Button` `aria-describedby`'ı yutuyordu. `ui.test.tsx` şimdi
   dört bağlamayı koruyor. Bir primitife yeni bir ARIA bağlaması geçireceksen
   önce o testi genişlet, sonra kodu yaz.
3. **Kalan ekranlar hâlâ eski desenleri taşıyor.** Somut örnekler: `agents.tsx`
   kendi arama girdisini elle kuruyor (`Toolbar` var), "Run" bağlantıları vurgu
   rengi taşımıyor (bağlantı gibi okunmuyor), liste ekranlarının çoğu hâlâ
   `Loading`'i varsayılan satır sayısıyla çağırıyor ve boş durumları birincil
   aksiyon taşımıyor.
4. **`docs/manuel-test/09-ARAYUZ-GENEL.md` 54 case'e çıktı** ve dosya bütçesine
   yaklaşıyor. Faz 165'in case'leri `10-ARAYUZ-AGENT-PLAYGROUND.md` ve
   `11-ARAYUZ-RUN-SESSION-SSE.md` içine, ekranın sahibi olan dosyaya girmeli.
5. **Ekran görüntüleri koyu temada üretiliyor** (`DocumentationScreenshotTests`,
   `ColorScheme.Dark`). Bir ekranın görünüşü değişirse 19'u birden yeniden
   üretilir; tek tek üretmenin yolu yoktur.
6. **F-224 açık:** geri alınamaz kararlar için doğrulama adımı. `Dialog` hazır
   ve kanıtlı; iş, envanteri çıkarmak ve etkilenen E2E olgularını birlikte
   taşımaktır. Bu faz yapamazdı — DoD'si mevcut olguların değişmemesini şart
   koşuyordu.
7. **F-221'in kalanı:** `assets/icon.png`, `docs-site/public/favicon.svg` ve
   `src/` içindeki `_prismOptions`/`prismException` adları hâlâ eski markadan.
   Console tarafı bu fazda kapandı.
