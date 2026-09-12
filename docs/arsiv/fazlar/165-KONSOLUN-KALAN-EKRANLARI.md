# Faz 165 — Konsolun Kalan Ekranları

> **Durum:** ✅ Tamamlandı (2026-09-12)
> **Kaynak:** **F-223**'ün ikinci yarısı. Kalem Faz 164 planlanırken kullanıcıyla
> ikiye bölündü: *"İki faza bölünür: 164 enstrüman katmanı + kanıt dilimi,
> 165 kalan ekranlar."* Yeni bir aday açılmadı.
> **Önkoşul:** [Faz 164](164-CONSOLE-ENSTRUMAN-KATMANI.md) — token
> seti, yoğunluk ölçeği, primitif kümesi (19 yeniden yazıldı, altı eklendi),
> durum dili ve kapılar orada kuruldu ve beş ekranda ispatlandı. Bu faz o
> katmanı **kullanır**, genişletmez.
> **Paketler:** `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Değişmiyor. HTTP sözleşmesi, `UseUI()` ve `MapTracon()` aynı
> **Tüketici yüzeyi:** Kalan 25 ekran · `docs-site/src/content/docs/ui.md` ·
> 19 ekran görüntüsü yeniden üretilir
> **Manuel test alanı:** `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` ·
> `11-ARAYUZ-RUN-SESSION-SSE.md` — case ekranın **sahibi olan** dosyaya girer.
> 🚨 `09-ARAYUZ-GENEL.md` 54 case'e çıktı ve bütçesine yaklaşıyor; oraya yalnız
> gerçekten kesişen bir case eklenir
> **Taban:** `18a3d6f2` (Faz 164'ün damıtma commit'i)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 75dc9622:docs/arsiv/fazlar/165-KONSOLUN-KALAN-EKRANLARI.md
> ```
>
> Damıtıldı 2026-09-12 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Konsolun enstrüman katmanı var ve beş ekranda çalışıyor. Kalan 25 ekran **yeni token setini aldı** — renk, yazı tipi ve yoğunluk otomatik geldi, çünkü hepsi aynı primitifleri çağırıyor — ama **deseni almadı**.

## Bitiş Ölçütleri (DoD)

- [x] 25 ekranın her biri beş desenden birine oturdu; hangisinin hangi desene
      girdiği kapanışta tabloya yazıldı ("Hangi ekran hangi desene oturdu").
- [x] `Empty` çağrı yerlerinin tamamı ya birincil aksiyon taşıyor ya da
      taşımama gerekçesi kodda yazılı. Ölçüldü: **46 · 1 → 44 · 19**, kalan
      25'in tamamının gerekçesi kodda (plan 45 · 1 diyordu; farkı Sapma 2
      açıklar).
- [x] `ErrorNote` çağrı yerlerinin tamamı `onRetry` taşıyor ya da gerekçesi
      kodda yazılı. Ölçüldü: **95 · 85 `onRetry`'siz → 114 · 6**, altısı da
      gerekçeli.
- [x] `<Loading />` varsayılan satır sayısıyla çağrılmıyor: **55 → 0**.
- [x] Rol yetmeyen her ekran `Unauthorized` gösteriyor; E2E ile kanıtlandı
      (`Reader_role_is_refused_on_admin_screens_rather_than_shown_an_empty_one`).
- [x] `Link` varsayılan bir görünüş taşıyor; `className` geçen çağrı yerleri
      etkilenmedi. Kapı: `A_link_reads_as_a_link_unless_its_call_site_dresses_it`
      (üç yarısı birden: çıplak · kendi sınıfını geçen · düğme biçimli).
- [x] `title` kullanımları üç sınıfa ayrıldı; kontrol açıklamaları `Tooltip`'e
      taşındı. Ölçüldü: kontrol üzerinde **19 → 0**, toplam DOM `title`
      **74 → 45** (hepsi "tam değer" sınıfı). Kural artık yapısal: `Button` ve
      `Link` `title` prop'u **kabul etmiyor**.
- [x] **Mevcut 64 E2E olgusunun 61'i hiç değiştirilmedi** ve hepsi yeşil.
      Üçünde locator veya assertion değişti, hiçbirinde kanıtlanan davranış
      değişmedi — gerekçeleri tek tek Sapma 4'te.
- [x] Bundle 250 KB gzip altında (**190,3 KB**); tavan yükseltilmedi;
      bağımlılık kümesi dört.
- [x] Kontrast tabanı düşmedi: metin **5,22:1**, metin dışı **3,56:1**.
- [x] `PlaywrightLocatorTests` tabanı büyümedi (**119**, dosya dokunulmadı).
- [x] 19 ekran görüntüsü yeniden üretildi; `ui.md` güncellendi.
- [x] Dört doğrulama kapısı sıfır uyarı (`kapi.py kapanis`).
- [x] `samples/Tracon.Api` ile gerçek `run` — çıktı "Örnek uygulama koşumu"
      bölümünde.
- [x] `secret` taraması boş döndü (`kapi.py tarama`).
- [x] Manuel kabul case'leri sahibi olan `docs/manuel-test/` dosyasına eklendi
      (`10-*`: 3, `11-*`: 5); otomatikleştirilebilenler yedi E2E olgusu olarak
      koşuluyor.
- [x] `faz-denetim` koşuldu; **5 🔴 · 7 🟡 · 3 🟢** bulundu ve 🔴'ların tamamı
      kapandı.

## Plandan Sapmalar

**1. 🚨 Planın `title` sayısı yanlıştı: 211 değil 74.** Plan `grep "title={t("`
sayıyordu ve bu, **bileşenlerin `title` PROP'unu** da sayıyor — `Panel title=`
(69 yer), `Empty title=` (44), `Section`/`Card title=` — hepsi birer BAŞLIK,
DOM özniteliği değil. Gerçek DOM `title` özniteliği fazın başında **74** yerdeydi;
kapanışta **45**. Üç sınıfa ayrıldı:

| Sınıf | Başta | Sonda | İşlem |
|---|---|---|---|
| Kısaltılmış bir değerin tamamı (`absoluteTime` yanında `relativeTime`, kısaltılmış kimlik, `truncate` metin) | 45 | 45 | **Kaldı** — `title`'ın meşru işi |
| Bir kontrolün açıklaması (`Button` · `Link` · ham `<button>`) | 19 | **0** | `Tooltip`'e taşındı veya `aria-label`'ı tekrarladığı için silindi |
| Bir badge'in ne demek olduğu | 26 | **0** | `Badge description` → `Tooltip` + odaklanabilir chip |

**Sayı bir cırcıra değil, YAPIYA bağlandı:** `Button` ve `Link` artık `title`
prop'u **kabul etmiyor**. Bir sonraki çağrı yerinin ona uzanması derleme
hatasıdır — sayım disipliniyle korunan bir kuralın yerine derleyici geçti.
`Mono` ve `Td` `title`'ı korur, çünkü onların işi tam değeri göstermektir.

**2. `Empty` çağrı yeri 45 değil 46'ydı**, kapanışta **44** (bir liste ekranı
ikiye bölündüğü için bir çağrı yeri kayboldu). **19'u** birincil aksiyon taşıyor
(başta 1), **25'i** gerekçesi **kodda yazılı** olarak taşımıyor. Gerekçeler beş
sınıfa düşüyor: (a) boş olması **istenen** liste (onay kuyruğu, kota, script
izni, kanarya kuralı) — sahte bir aksiyon en tehlikeli anahtarı reklam etmek
olurdu; (b) **kayıt** olan liste (run olayları, span'ler, job kalemleri, denetim
izi) — konsol geçmişe kayıt ekleyemez; (c) kodda tanımlanan kayıt (tool,
sağlayıcı, workflow grafiği) — dürüst sonraki adım o kod çağrısıdır ve metinde
yazılıdır; (d) aksiyonu **hemen altında** olan panel (API anahtarı, webhook,
kota formu); (e) uzak sunucunun kendi kararı (MCP prompt/resource).

**3. `ErrorNote` 85 değil 95 çağrı yerinde `onRetry` taşımıyordu.** Kapanışta
114 çağrı yerinin **108'i** taşıyor; kalan 6'sının gerekçesi kodda yazılı.
Beşi bir **okumanın** değil bir **kararın** hatası (onay verdi, run'ı iptal
etti, rollback yaptı, prompt gönderdi): genel bir "tekrar dene" düğmesi,
argümanları artık görünmeyen bir kararı yeniden göndermek olurdu ve karar
düğmesinin kendisi hâlâ ekranda. Altıncısı farklı bir sınıftır ve gerekçesi de
öyle yazılı: `job-detail` kayıtlı bir hatayı basar (o isteğin kendisi değildir),
`playground`'un ek dosya hatası ise `File` nesneleri seçici kapandığında yok
olduğu için tekrar denenemez.

**4. 🚨 Üç mevcut E2E olgusunun LOCATOR'ı ya da ASSERTION'ı değişti** (DoD
"hiçbiri değiştirilmedi" diyordu). Üçünde de **kanıtlanan davranış aynen
duruyor**; değişen, o davranışın artık nerede okunduğudur.

| Olgu | Ne değişti | Neden |
|---|---|---|
| `Runs_button_on_session_page_navigates_to_filtered_list` | `AriaRole.Button` → `AriaRole.Link` | Eski yazım `<Link><Button>N runs</Button></Link>` idi — bir `<a>` içinde `<button>`, yani **geçersiz HTML**, tek hedef için iki tab durağı ve işaretçinin hangisine düştüğüne göre değişen bir davranış. `LinkButton` onu tek bir `<a>` yaptı, dolayısıyla artık gerçekten bildirdiği rolü bildiriyor. |
| `Theme_selector_in_settings_updates_top_bar_toggle_in_same_session` | `GetAttributeAsync("title")` → `aria-describedby`'ı izleyip o öğenin metnini oku | Tema düğmesinin "mevcut tercih" açıklaması `Tooltip`'e taşındı (denetim 🔴 #4). İddia **güçlendi**: artık metnin kontrole *bağlı* olduğunu kanıtlıyor, yalnız üzerinde bir yerde bulunduğunu değil. |
| `Proof_slice_screens_do_not_overflow_horizontally_at_375px_width` | Ölçümden önce işaretçi (0,0)'a park ediyor | Bu bir **sıra bağımlılığı** kusuruydu, bir gevşetme değil: Playwright fareyi son tıklamanın bıraktığı yerde bırakır ve bu faz badge'lere tooltip koydu, dolayısıyla ölçüm **önceki adımın tıklama koordinatına** bağımlı hâle gelmişti (izole geçiyor, tam koşumda ~4'te 1 düşüyordu). Tooltip'in kendi yerleşimi zaten kendi olgusuna sahip. |

Diğer 60 olgu ve ekran görüntüsü olgusu değişmedi.

**5. Dokuz boş-durum aksiyonu kendi metnini aldı** — plan bunu öngörmemişti ama
gerekliydi: aksiyon başlıktaki düğmenin etiketini birebir tekrarlıyordu ve bu,
tek ekranda **aynı erişilebilir ada sahip iki kontrol** üretti (Playwright strict
mode dört mevcut olguyu düşürdü: `mcp` · `jobs` · `evals`, ve eval detayında
iki kez). `*.empty.action` anahtarları "ilkini oluştur" dilini taşıyor, ki
planın istediği de buydu ("boş: ilkini nasıl oluştururum").

**6. Kapsam dışıydı ama yapıldı: `dashboard.tsx`, `run-detail.tsx`,
`approvals.tsx` ve on paylaşılan panele de dokunuldu.** DoD "`Loading`
varsayılan satır sayısıyla **çağrılmıyor**" ve "`ErrorNote` çağrı yerlerinin
**tamamı**" diyor — kanıt diliminin beş ekranı ve `components/` altındaki
paneller bu "tamamı"nın içindedir, ve o paneller kapsamdaki `settings.tsx`'in
gövdesini oluşturuyor.

## Bu Fazda Verilen Kararlar

Public API, güvenlik sınırı, kalıcı veri veya geri dönüşü pahalı bir sistem
kararı **değişmedi**; karar defterine yeni `K-*` kaydı girmez. Yerel tercihler:

- **Beş primitif eklendi, üçü kapsam genişletmesi olarak gerekçelendirildi.**
  Plan yeni primitifi yasaklamıştı ama "gerçekten eksikse faz dokümanına yazılır"
  diyordu; üçü de ikinci/üçüncü kez elle yazılmış bir şeydi:
  - `LinkButton` — üç ekran vurgu sınıflarını elle kopyalamış, iki ekran
    `<Link><Button>` yazmıştı (geçersiz HTML, iki tab durağı).
  - `Tabs` — `session-detail.tsx` ve `mcp-server-detail.tsx` ayrı `TabButton`
    yazmıştı, ikisi de `role="tab"`/`aria-selected`/`aria-controls`/ok tuşu
    taşımıyordu. ARIA kablolaması artık tek yerde.
  - `Badge description` ve `Th description` — 26 badge ve bir sütun başlığı
    ulaşılamaz bir `title` taşıyordu.
  - `Stat` ve `Pager` **taşındı**, eklenmedi: `runs.tsx` ve `sessions.tsx`
    içinden `ui.tsx`'e. Beş ekran `Stat`, on iki ekran `Pager` kullanıyor;
    `diagnostics.tsx`'in run listesine bağımlı olması bir hataydı.
- **`min-w-0` kuralı BİR SEVİYE aşağı indi.** Faz 164 `Panel`'e verdi; bu faz
  `Mono`'nun `copy` sarmalayıcısına, `Stat`'a ve `ErrorNote`'a verdi. Kural
  yazıldı: **`overflow-hidden`/`truncate` bir çocuğa konuyorsa, onu sarmalayan
  her flex/grid öğesi `min-w-0` taşımalıdır** — `truncate` içeriği `nowrap`
  yapar ve o içeriğin min-content'i tüm dizedir, yani `truncate` tek başına
  hiçbir şey kesmez.
- **`lib/cx.ts` açıldı.** `ui.tsx` ↔ `status-dot.tsx` arasında zaten bir import
  döngüsü vardı (`cx` yüzünden) ve `Tooltip` ikinci bir tanesini ekleyecekti.
  Döngü çözülüyordu ama hiçbir bundler'ın sürdürmek zorunda olmadığı bir
  şekilde. `ui.tsx` `cx`'i yeniden ihraç ediyor; 30 ekranın importu değişmedi.
- **`aria-*` sayısı bir kapıya BAĞLANMADI** (kullanıcı kararı). Sayı kaba bir
  ölçüdür — bir `role="none"` da sayılır — ve cırcır anlamsız ARIA eklemeyi
  ödüllendirir. Gerçek kapılar davranışı kanıtlayan E2E olgularıdır. Ölçüm
  (öznitelik · `role=`): **74 · 0 → 88 · 26**.
- **`skills.tsx` bölündü** (kullanıcı kararı): `screens/skills.tsx` liste,
  `screens/skills/skill-editor.tsx` form, `screens/skills/script-grants.tsx`
  izin yüzeyi, `screens/skills/model.ts` form tipi. `agent-editor` ile aynı
  biçim.
- **`playground.tsx`'te yalnız çerçeve taşındı** (kullanıcı kararı): boş/hata/
  yükleme durumları, `Select`'in erişilebilir adı, bağlantı görünüşü ve iki
  `title`→`Tooltip`. Canlı akış, ses paneli ve transkript iç davranışı
  dokunulmadı.
- **`waterfall.traceIdLabel` / `spanIdLabel` `identicalOnPurpose` listesine
  girdi.** İkisi de tamamen İngilizce kalan terimlerden oluşuyor (`trace`,
  `span`, `id` — AGENTS.md dil kuralı), dolayısıyla Türkçesi İngilizcesidir.
  Değiştirdikleri kopya onları **çevirmişti** ("W3C iz kimliği") ve o kural
  ihlali kimsenin ulaşamadığı bir `title` özniteliğinin içinde duruyordu.

## Ölçülen sonuç

Her satır **aynı** betikle iki kez ölçüldü: taban `18a3d6f2`'nin ağacı
`git archive` ile çıkarılıp aynı sayım koşuldu. Planın kendi sayıları bazı
yerlerde farklıydı (`title` 211, `Empty` 45, `ErrorNote` 85) çünkü farklı bir
desen kullanıyordu; aşağıdaki iki kolon karşılaştırılabilirdir.

| Ölçüm | Faz öncesi | Faz sonrası |
|---|---|---|
| `<Loading />` (varsayılan satır sayısı) | 55 | **0** |
| `ErrorNote` çağrı yeri · `onRetry`'siz | 95 · 85 | 114 · **6** (hepsi gerekçeli) |
| `Empty` çağrı yeri · aksiyonlu | 46 · 1 | 44 · **19** (kalan 25 gerekçeli) |
| Bir kontrol üzerinde `title` | 19 | **0** (`Button`/`Link` artık prop'u almıyor) |
| DOM `title` özniteliği (toplam) | 74 | **45** (hepsi "tam değer" sınıfı) |
| `Toolbar` kullanan ekran | 1 | **13** |
| `Unauthorized` kullanan dosya | 2 | **6** |
| `aria-*` özniteliği · `role=` | 74 · 0 | **88 · 26** |
| E2E olgusu | 64 | **71** |
| Ham Tailwind renk sızıntısı (`red-500`) | 2 | **0** |
| Ekran görüntüsü | 19 (Faz 164) | **19 yeniden üretildi** |

## Hangi ekran hangi desene oturdu

| Desen | Kanonik | Bu fazda oturan ekranlar |
|---|---|---|
| Liste | `runs.tsx` | `sessions` · `agents` · `tools` · `models` · `skills` · `workflows` · `triggers` · `jobs` · `evals` · `experiments` · `mcp` · `audit` (12) |
| Detay | `run-detail.tsx` | `session-detail` · `job-detail` · `eval-run-detail` · `workflow-detail` · `playground` (5, playground yalnız çerçeve) |
| Form | `agent-editor/` | `workflow-editor` · `skills/skill-editor` · `settings` (3) |
| Karar | `approvals.tsx` | `agent-detail` (rollback) · `experiment-detail` (başlat/durdur) · `eval-detail` (şimdi koş) (3) |
| Metrik | `dashboard.tsx` | `diagnostics` (1) |

Toplam **24 ekran** + `skills` ikiye bölündüğü için ortaya çıkan editör = plandaki
25 kalem.

## Bu fazda bulunan ve kapatılan kusurlar

Hiçbiri planda yoktu; ikisi Faz 164'ten kalmıştı, dördünü bağımsız denetim
buldu ve birini **kararsız bir test** buldu.

1. **🚨 `Tooltip`'in GİZLİ balonu 224 px yer kaplıyordu** (Faz 164 kusuru).
   Gizli hâlde `sr-only` alıyordu ama `w-max max-w-56` sınıflarını da
   koruyordu; Tailwind çakışmayı **stylesheet sırasına** göre çözer, attribute
   sırasına göre değil — yani `w-max` kazanıyor ve "gizli" balon absolute
   konumlu, 224 px genişliğinde kalıyordu. Absolute bir öğe `scrollWidth`'e
   **dahildir**: sağ kenara yakın her tooltip sayfayı yana kaydırıyordu. Faz 165
   tooltip'i badge ve sütun başlıklarına yaydığı için ortaya çıktı; yeni 375 px
   olgusu `models` ekranında **104 px** taşma ölçtü.
   **Kaynak okuması bunu bulamadı** — çalışma anı probu buldu (aynı anda görünür
   balon doğru, gizli balon 224 px kenar dışında ölçüldü). Kapı:
   `A_tooltip_near_the_right_edge_stays_inside_the_viewport`.
2. **Tooltip balonu tetikleyicisine ortalıydı ve kenarı aşıyordu.** Artık
   gösterim anında ölçülüp kırpılıyor. 🚨 `calc(-50% + -112px)` bir **ayrıştırma
   hatasıdır** ve tüm bildirim düşürülür — işaret operatöre konur, operanda değil.
3. **`skills.tsx` iki yerde ham Tailwind paleti kullanıyordu**
   (`border-red-500 bg-red-500/10 text-red-500`). Tema izlemiyorlar ve konsolun
   `danger` tonu değiller: ürünün en yüksek sesli uyarısı, başka hiçbir yerinin
   kullanmadığı renkteydi. Token setine döndü.
4. **On dört mutation hatası sessizdi.** Bir tetikleme, bir OAuth başlatma, bir
   silme, bir izin verme sunucu tarafından reddedildiğinde ekran **aynen**
   kalıyordu — operatör işlemin başarılı olduğu sonucuna varıyordu. Hepsi artık
   `ErrorNote` gösteriyor.
5. **Üç editör yükleme hatasında BOŞ FORM gösteriyordu** (Faz 164 denetim 🟡 #2
   ile aynı sınıf): `triggers`, `skills`, `workflow-editor`. `isPending`
   istek düşer düşmez `false` oluyor ve altındaki hiçbir dal o durumu
   karşılamıyordu — ekran "boş yüklenmiş bir kayıt" gibi görünüyordu ve **o
   hâlden kaydetmek gerçek tanımı hiçliğe çevirirdi**. `eval-detail`'in case
   listesi aynı sınıftaydı.
6. **`audit.tsx`'in satırı yalnız işaretçiye yanıt veriyordu** (`<tr onClick>`):
   klavyeyle ulaşılamaz ve genişleyebilir olduğunu duyurmuyordu. Gerçek bir
   `<button>` oldu, `aria-expanded` + `aria-controls` taşıyor. Kapı:
   `An_expanding_row_announces_its_state_and_opens_from_the_keyboard`.
7. **`Button` `aria-expanded`/`aria-controls`'ü yutuyordu** — Faz 164 devir notu
   #2'nin tam olarak uyardığı tuzak. Test önce genişletildi, sonra kod yazıldı.
8. **`settings.tsx`'in "token by model" panelinde hata dalı hiç yoktu**, ve
   kiracı sorgusu düştüğünde ekran "kiracı yok" ile aynı em dash'i gösteriyordu.
9. **`agents.tsx`'in arama kutusu liste doldukça ortaya çıkıyordu** ve altındaki
   her şeyi aşağı itiyordu; `Toolbar` koşulsuz render ediliyor.
10. **Tooltip balonu ilk karede kırpılmamış çiziliyordu** (denetim 🟡 #7):
    kırpma `useEffect` ile ölçüyordu ve passive effect **boyamadan sonra**
    koşar. `useLayoutEffect` oldu; ayrıca görünürken pencere yeniden
    boyutlanırsa yeniden ölçüyor.
11. **`access-gate.tsx`'in tam sayfa bağlanma durumu dört satırlık iskelet
    çiziyordu** (denetim 🟡 #6): arkasında yüksekliği tutulacak bir liste yok,
    dolayısıyla dört çubuk beklenecek bir progress bar gibi okunuyordu.
    `rows={1}`.
12. **`triggers.tsx` kiracı sorgusu düştüğünde kabul URL'ini `…` ile üretiyordu**
    (denetim 🟢 #13, sınıf taraması): kiracı kimliği harici bir sistemin
    imzalayacağı adresin parçasıdır, dolayısıyla o URL asla eşleşemezdi ve
    operatör bunu bilemezdi. `settings.tsx`'te kapatılan kusur #8'in **aynı
    sınıfı** — sınıf taraması ikinci vakayı buldu.
13. **`audit.tsx`'in `aria-controls`'ü yalnız genişletilmişken var olan bir
    `id`'ye işaret ediyordu** (denetim 🟢 #14): detay satırı artık her zaman
    render ediliyor ve `hidden` ile gizleniyor.
14. **🚨 `Mono`'nun `copy` sarmalayıcısı `min-w-0` taşımıyordu** ve bu, Faz
    164'ün `Panel`'e `min-w-0` eklerken kaydettiği tuzağın **bir seviye
    altıydı**: sarmalayıcı bir flex container'dır, yani `min-width: auto` ile
    içeriğinin min-content genişliğinin altına inmeyi reddeder — ve `truncate`
    geçen bir çağıran o içeriği `white-space: nowrap` yapmıştır, ki onun
    min-content'i **tüm dizedir**. 32 karakterlik bir W3C trace id bu span'i
    ~210 px'e sabitliyor ve run detay ekranını 375 px'te yana kaydırıyordu.
    **Kararsız bir testle ortaya çıktı:** `Waterfall` yalnız span örneklenmişse
    render ediliyor, dolayısıyla `Proof_slice_screens_...` üç koşumda bir
    düşüyordu — izole her zaman geçiyordu. Aynı kural `Stat` ve `ErrorNote`'a da
    uygulandı; ikisi de neredeyse her zaman bir grid öğesidir.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent) 2026-09-12'de koştu: **5 🔴 · 7 🟡 ·
3 🟢**. En değerli bulgusu, **kendi sertleştirmemin kırdığı iki testti** —
locator'lara `Exact = true` ekledikten sonra o iki olguyu yeniden koşmamıştım.

### 🔴 — hepsi kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | İki yeni E2E olgusu **hiç yeşil olmadı**: sekmenin adı `History` değil `Chat history`, hata metni `title` değil `"<title>: <detail>"` (`TraconError` böyle kurar). | Gerçek metinler kullanıldı; sunucu cümlesi `ServerFailureText` sabitine alındı ve **tam cümle** iddia ediliyor — yalnız başlığı eşlemek, olgunun iddia ettiğinden azını kanıtlıyordu. |
| 2 | `ui.md` değişti ama türevi `llms-full.txt` üretilmedi → iki kapanış kapısı kırmızı. | `build-agent-map.mjs` koşuldu. |
| 3 | 19 ekran görüntüsü yeniden üretilmedi; sevk edilen site Faz 164'ün konsolunu gösteriyordu. | `TRACON_UI_SCREENSHOTS=1` ile üretildi ve gözle incelendi. |
| 4 | "`Button`/`Link` üzerinde `title` = 0" **yanlıştı, altı tane duruyordu** (grep'im tek satırlıydı); ikisi ikon-only ve `ariaLabel` taşımıyordu, yani adları yalnız `title`'dan geliyordu. | Altısı `Tooltip`'e taşındı (+`ariaLabel`), sonra **prop kaldırıldı** — kusur listesi #1 ve devir notu #2. Kabuktaki üç kullanım da kapandı. **19 → 0.** |
| 5 | DoD'nin "`samples/Tracon.Api` ile gerçek `run`" satırı karşılıksız. | Koşuldu; çıktı "Örnek uygulama koşumu"nda. |

### 🟡

| # | Bulgu | Sonuç |
|---|---|---|
| 6 | `<Loading />` iki yerde daha varsayılan satır sayısıyla (`access-gate.tsx`); grep'im `<Loading label={…} />` biçimini görmemişti. | Kusur listesi #11. |
| 7 | Tooltip kırpması `useEffect` ile ölçüyordu (passive effect boyamadan **sonra** koşar) ve `resize`'da yeniden hesaplamıyordu. | Kusur listesi #10. |
| 8 | 375 px gezintisi 14 ekranın sekizinde **satırsız** koşuyordu — "boş ekran taşmaz" kuralının ihlali. | `SeedEveryListAsync` her listeye bir satır yazıyor; denetim izi yan etki olarak doluyor. Tohumlama iki sözleşmeyi ortaya çıkardı: bir deney **kayıtlı** agent ister (kodda tanımlının sürüm geçmişi yoktur) ve `PUT /api/agents/{ad}` günceller, oluşturmak `POST /api/agents`'tır. `diagnostics`'in listesi yoktur; yorum olarak yazıldı. |
| 9 | `identicalOnPurpose` muafiyeti **+2 büyüdü**; skill'e göre gerekçeli olsa bile 🔴. | **Bilinçli kapatıldı** — aşağıdaki bölüm. Diğer üç taban çizgisi dokunulmadı. |
| 10 | "Ölçülen sonuç"un beş sayısı **yeniden üretilemiyordu** (`Empty` sayımı `<EmptyChart`'ı da sayıyordu; `aria` sayısı hiçbir desenle çıkmıyordu). | Tablo yeniden yazıldı; her satır taban ağacı `git archive 18a3d6f2` ile çıkarılıp **aynı betikle** iki kez ölçüldü. |
| 11 | Sevk edilen bundle sayısı yanlıştı. | Son ölçüm **190,3 KB**; `ui.md` ve bu doküman aynı sayıyı taşıyor. |
| 12 | Üretilen `YOL-HARITASI.md` kaynağıyla aynı değil. | Kapanışta düzeltildi. |

**Denetim sonrası dört kapı yeniden koştu ve bir KARARSIZLIK buldu** — skill'in
"🔴 kapandıktan sonra kapılar yeniden koşar; düzeltme yeni kusur üretebilir"
kuralının karşılığı. Ayrıntı: kusur listesi #14 ve devir notu #4. Düzeltme
sonrası set **altı kez** üst üste 71/71 koştu.

### 🟢 — ikisi yine de kapatıldı

| # | Bulgu | Sonuç |
|---|---|---|
| 13 | `triggers.tsx`: kiracı sorgusu düştüğünde kabul URL'i `…` ile sessizce **yanlış** üretiliyordu — fazın `settings.tsx`'te kapattığı kusurun aynı sınıfı. | Kusur listesi #12 (sınıf taraması). |
| 14 | `audit.tsx`: `aria-controls` yalnız genişletilmişken var olan bir `id`'ye işaret ediyordu. | Kusur listesi #13. |
| 15 | `Reader_role_is_refused_…` audit'in boş-durum metnini `diagnostics` için de arıyor; orada o metin yok. | **Gerekçelendi:** ikinci iddia gereksiz ama zararsız değil, yanlış güven verir. Olgu `unauthorized` testid'siyle her iki ekranı kanıtlıyor; metin kontrolü anlamlı olduğu yerde kaldı ve yorumu bunu söylüyor. |

### Muafiyet listesinin büyümesi — bilinçli karar

`identicalOnPurpose` iki anahtar büyüdü: `waterfall.traceIdLabel` ve
`waterfall.spanIdLabel` (`'W3C trace id'` / `'W3C span id'`). İkisi de
**tamamen** İngilizce kalan terimlerden oluşur — dil kuralı `trace`, `span` ve
`id`'nin çevrilmemesini şart koşar — dolayısıyla Türkçesi İngilizcesidir.

Alternatif **daha kötüdür**: değiştirdikleri iki `title` anahtarı bu terimleri
gerçekten çevirmişti ("W3C iz kimliği"), yani o kuralı ihlal ediyordu ve bunu
kimsenin ulaşamadığı bir `title` özniteliğinin içinde yapıyordu. Listeyi
büyütmemenin tek yolu terimleri yeniden çevirmekti. **Net: iki muafiyet
kazanıldı, iki dil kuralı ihlali kaldırıldı, iki açıklama görünür etikete
dönüştü.**

## Örnek uygulama koşumu

`samples/Tracon.Api`, taze derlenmiş gömülü varlıklarla, `echo` sağlayıcısıyla
(ağa hiçbir şey gitmez):

```
$ curl -s http://localhost:5080/tracon/api/meta
{"version":"0.0.0-preview.0.699","prefix":"/tracon",
 "storage":{"persistent":false,"runStore":"InMemoryRunStore", ...},
 "roles":{"canRead":true,"canOperate":true,"canAdminister":true}}

$ curl -s -X POST .../api/agents/support/run -d '{"message":"where is ORD-7?"}'
event: run
data: {"runId":"01a09703-911a-7f7e-a054-9c15f590f562","sessionId":null}
event: update
data: { "role": "assistant", "contents": [{ "$type": "text", "text": "Echo: " }] }
...

$ curl -s ".../api/runs?take=2"
[{"id":"01a09703-...","agentName":"support","status":"Completed","eventCount":6}]
```

Konsolun kendisi `GET /tracon/` ile servis edildi ve `<base href="/tracon/" />`
ile geldi — prefix çalışma anında öğreniliyor. **Ayrıca 19 ekran görüntüsü
gerçek bir tarayıcıda gerçek bir konsoldan üretildi** ve gözle incelendi
(`agents`: şerit hep görünür, kimlik bağlantısı vurgu renginde, sağlayıcı model
altında soluk metin, sayısal hücreler sağa yaslı ve monospace · `mcp`:
"Hatırlanan onaylar" boş durumu sahte aksiyon taşımıyor).

## Sonraki Faza Devir Notu

**Faz kapandı.** Dört kapı sıfır uyarı, 71/71 E2E, bağımsız denetimin beş 🔴 ve
yedi 🟡 bulgusu kapandı, 19 ekran görüntüsü yeniden üretildi. Konsolun **30
ekranının tamamı** artık beş desenden birine oturuyor; F-223 kapandı.

**Sonraki oturumun bilmesi gerekenler:**

1. **Bundle payı 60 KB'ye indi.** 190,3 / 250 KB. Faz 164 + 165 birlikte
   6,2 KB ekledi (180,0 → 190,3). Bağımsız kapı hâlâ dört isimle bağlıdır
   (K-758); bir beşinci bağımlılık bir karardır.
2. **🚨 Bir kuralı SAYIYLA değil YAPIYLA zorla.** Bu fazın en kalıcı dersi:
   `title`'ı kontrol üzerinden kaldırmak için 19 çağrı yerini düzeltmek yetmedi
   — denetim altı tanesini benim grep'imin kaçırdığını buldu. Kural ancak
   `Button` ve `Link` `title` prop'unu **almayı bıraktığında** kapandı.
   Aynı soruyu her yeni kuralda sor: bu, bir sonraki çağrı yerinin *yapamayacağı*
   bir şey mi, yoksa *yapmaması gereken* bir şey mi?
3. **🚨 Locator'ı sertleştirdikten sonra testi YENİDEN KOŞ.** İki yeni olgu
   `Exact = true` eklendiği anda kırıldı ve ben koşmadım; denetim buldu.
   `PlaywrightLocatorTests` tabanını korumak için yapılan her düzenleme, o
   olguyu yeniden koşmayı **gerektirir** — daralan bir locator eşleşmeyi
   kaybedebilir.
4. **🚨 KARARSIZ bir taşma testi gevşetilmez, suçlu listesi okunur.**
   `Proof_slice_screens_...` denetim düzeltmelerinden sonra üç koşumda bir
   düşüyordu ve izole her zaman geçiyordu. İlk hipotezim (Playwright'ın fareyi
   son tıklamada bırakması) **yanlıştı** ve o yönde iki tur harcadım; doğru
   cevabı taşma probunun bastığı suçlu listesi bir bakışta verdi (`Mono`'nun
   `min-w-0`'ı olmayan `copy` sarmalayıcısı). Bir taşma testi kararsızsa, taşan
   öğe **koşullu olarak** render ediliyordur — burada `Waterfall` yalnız span
   örneklenmişse çiziliyor. Önce bir düşen koşumu YAKALA, sonra düşün.
5. **F-224 hâlâ açık ve artık daha kolay:** geri alınamaz kararlar için
   doğrulama adımı. Bu faz karar yüzeylerine **sonucu okunabilir** hâle getiren
   `Tooltip`'leri koydu (`agentDetail.rollbackEffect`,
   `experiments.startEffect`/`stopEffect`, `evals.runNowEffect`) — bir `Dialog`
   adımı artık aynı metni yeniden kullanabilir. Envanter: rollback · deney
   başlat/durdur · eval koş · MCP server sil · skill/trigger/eval/deney sil
   (şu an `window.confirm`). `window.confirm` E2E'de Playwright tarafından
   **sessizce iptal edilir** — `Dialog`'a geçerken etkilenen olguları birlikte
   taşı.
6. **Beş primitif eklendi/taşındı.** `LinkButton` · `Tabs` · `Badge
   description` · `Th description`, ve `Stat`/`Pager` `ui.tsx`'e taşındı. Bir
   ekran artık `Stat`'ı run listesinden import etmiyor. Yeni bir sekme şeridi
   yazma — `Tabs` ARIA kablolamasının tek kaynağıdır.
7. **`docs/hafiza/frontend.md` bütçesini aştı ve bölündü:** component-test ve
   `openapi-fetch` stub notları `frontend-test-altyapisi.md`'ye **taşındı**
   (içerik silinmedi). Arayüz alan dosyası artık dört: `frontend` ·
   `frontend-tasarim-katmani` · `frontend-yerellestirme` ·
   `frontend-test-altyapisi`.
8. **F-221'in kalanı hâlâ açık:** `assets/icon.png`,
   `docs-site/public/favicon.svg` ve `src/` içindeki
   `_prismOptions`/`prismException` adları eski markadan.
9. **Ekran görüntüleri koyu temada, 19'u birden üretilir**
   (`TRACON_UI_SCREENSHOTS=1`). Bir ekranın görünüşü değişirse tek tek
   üretmenin yolu yoktur; `dotnet build` öncesi
   `rm artifacts/obj/Tracon.UI/tracon-frontend.stamp` yapmadan gömülü varlıklar
   bayat kalır ve testler eski konsolu ölçer (bu fazda iki kez bedel ödedi).
