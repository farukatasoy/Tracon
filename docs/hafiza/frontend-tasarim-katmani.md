# Arayüz Tasarım Katmanı Tuzakları

> Token seti, tema, yoğunluk ölçeği, primitifler, erişilebilirlik ve arayüz
> kapıları (kontrast, bundle, bağımlılık).
>
> Kardeş dosyalar: [`frontend.md`](frontend.md) (Vite, SPA rota, ekran
> mantığı) · [`frontend-yerellestirme.md`](frontend-yerellestirme.md)
> (`useT`, `Messages`, `Intl`).
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken
> okunur. Yeni not buraya eklenir, `MEMORY.md`'ye değil.

## Tema ve token

- **🚨 `prefers-color-scheme` "seçilmemiş" durumunu BİLDİREMEZ** (2026-09-12,
  Faz 164, K-757): medya özelliğinin `no-preference` değeri modern tarayıcıdan
  kalktı. Seçim yapmamış her makine `light` bildirir, dolayısıyla **"sistemi
  izle"yi varsayılan yapmak açık temayı varsayılan yapmaktır.** Koyu bir
  varsayılan CSS'te veya medya sorgusunda kurulamaz; **saklanan tercihin**
  varsayılanı olmak zorundadır (`readThemePreference()` hiçbir şey yokken
  `'dark'` döner). Gerçek tarayıcıda ölçüldü: `ColorScheme.NoPreference` ile
  açılan Playwright bağlamı `light` verdi — `(prefers-color-scheme: light)`
  sorgusuna dayanan ilk uygulama bu yüzden düştü.
- **Bir rengi yalnız `@media` veya bileşen kuralı içinde tanımlamak onu tema
  değişiminde kaybettirir** (2026-09-12, Faz 164): `scripts/check-tokens.mjs`
  `--tracon-*` **bildirim sayısını** iki tema bloğundaki token sayısıyla
  karşılaştırır. Blokların dışında bir bildirim varsa kapı kırmızıdır. Token
  bir başka token'a `var()` ile de işaret edemez — zincir çözülür ama yanlış
  renge çözülür.
- **Token seti iki dosyaya bölünmüştür ve aynı DEĞERLERİ paylaşmaz**
  (2026-09-12, Faz 164): `docs-site/src/styles/site.css` okuma yüzeyi,
  `src/Tracon.UI/frontend/src/styles.css` operasyon yüzeyi. Aynı **aileden**
  gelirler (koyu kömür zemin, kırık beyaz metin, tek teal vurgu); birini
  değiştirirken diğerini otomatik değişmiş sayma.
- **🚨 Ham Tailwind paleti (`red-500`, `slate-700`) bir sızıntıdır** (Faz 165):
  tema izlemez ve konsolun `danger` tonu değildir. `skills.tsx` iki uyarı
  bandını böyle yazmıştı — ürünün en yüksek sesli uyarısı, başka hiçbir yerinin
  kullanmadığı renkteydi. Tarama: `grep -rn "red-500\|slate-\|gray-\|zinc-"
  src/Tracon.UI/frontend/src/`.
- **Durum rengi ile seri rengi AYRI kümelerdir** (2026-09-12, Faz 164): durum
  altı tondan biridir (`status-dot.tsx`: accent · success · warn · danger ·
  info · neutral) ve **tek kaynağı** o modüldür. Grafik serileri
  `--tracon-series-1..6`'dır. Kırmızı/yeşil bir seri bir hüküm gibi okunur; bu
  yüzden seri paleti bilerek durum renklerini içermez.

## Yoğunluk ölçeği

- **Ölçek `@theme inline` içindeki `--text-*` adımlarıdır, `text-[13px]` değil**
  (2026-09-12, Faz 164): Tailwind v4 `--text-<ad>` ad alanından `text-<ad>`
  yardımcısını üretir. Konsolun adımları: `2xs` 10 · `xs` 11 · `sm` 12 ·
  `base` 13 · `body` 14 · `section` 15 · `metric` 18 · `title` 20 · `id` 12,5
  (monospace kimlik). Bir bileşende `text-[NNpx]` görürsen ölçek oradan
  sızmıştır.
- **Tanımlayıcı her yerde `text-id` ve monospace'tir** (Faz 164): `run`,
  `session`, `tenant`, `span`, API anahtarı ön eki. `Mono` primitifi bunu
  uygular; `copy` prop'u tam değeri panoya koyar (kısaltılmışı değil).

## Yerleşim

- **🚨 Grid/flex öğesinin `min-width: auto`'su TEK bir geniş çocuğu tüm
  SÜTUNU genişletmeye çevirir** (2026-09-12, Faz 164): dashboard 375 px'te
  87 px yatay taşıyordu; suçlu sabit genişlikli dört sütunlu bir model kırılım
  satırıydı ve taşma onu **içeren panelin** sütununa yansıyordu. `Panel`
  artık `min-w-0` taşır. Tablo ve kod bloğu kendi `overflow-x-auto`
  sarmalayıcısına sahiptir; panelin altına inmesi güvenlidir.
- **🚨 `min-w-0` kuralı BİR SEVİYE DAHA aşağı iner: `truncate` geçen bir
  çağıran içeriği `nowrap` yapar ve o içeriğin min-content'i TÜM DİZEDİR**
  (2026-09-12, Faz 165): `Mono`'nun `copy` sarmalayıcısı bir flex container'dı
  ve `min-w-0` taşımıyordu, dolayısıyla 32 karakterlik bir W3C trace id onu
  ~210 px'e sabitliyordu — `truncate` hiçbir şey kesmiyordu çünkü kesecek yer
  yoktu. Kural: `overflow-hidden`/`truncate` bir çocuğa konuyorsa, **sarmalayan
  her flex/grid öğesi** `min-w-0` taşımalıdır. `Panel`, `Mono`'nun copy
  sarmalayıcısı, `Stat` ve `ErrorNote` artık taşıyor.
- **🚨 KARARSIZ bir taşma testi gevşetilmez, okunur** (Faz 165): yukarıdaki
  kusur `Proof_slice_screens_...`'ı üç koşumda bir düşürüyordu ve **izole her
  zaman geçiyordu** — çünkü `Waterfall` yalnız span örneklenmişse render edilir.
  İlk hipotez (Playwright'ın fareyi son tıklamada bırakması) yanlıştı; doğru
  cevabı taşma probunun bastığı **suçlu listesi** verdi. Bir taşma testi
  kararsızsa, taşan öğe koşullu olarak render ediliyordur.
- **🚨 BOŞ bir ekranda koşan taşma testi hiçbir şey kanıtlamaz** (2026-09-12,
  Faz 164): eski 375 px olgusu run seed etmiyordu, grafikler `EmptyChart`
  çiziyordu ve taşma hiç doğmuyordu. Yukarıdaki kusur bu yüzden aylarca
  görünmedi. Yeni olgu önce playground'dan gerçek bir run üretir.
- **🚨 GİZLİ bir öğe `sr-only` aldığı hâlde yer kaplamaya devam edebilir**
  (2026-09-12, Faz 165): tooltip balonu gizliyken `sr-only` alıyordu ama
  `w-max max-w-56` sınıflarını da koruyordu. **Tailwind çakışmayı stylesheet
  sırasına göre çözer, `class` attribute'undaki sıraya göre değil** — `w-max`
  kazanıyor ve "gizli" balon absolute konumlu, 224 px genişliğinde kalıyordu.
  Absolute bir öğe `scrollWidth`'e **dahildir**: sağ kenara yakın her tooltip
  sayfayı yana kaydırıyordu. Kural: gizli hâl `sr-only` alır ve **başka hiçbir
  şey almaz** — boyut/konum sınıfları yalnız görünür daldadır.
  **Kaynak okuması bunu bulamadı**; çalışma anı probu buldu (aynı anda görünür
  balon doğru ölçüldü, gizli balon 224 px kenar dışında).
- **🚨 Kenara yakın bir balon CSS ile kırpılamaz** (Faz 165): kırpma
  tetikleyicinin konumunu ve balonun genişliğini gerektirir, ikisi de bir
  stylesheet'in bilemeyeceği şeydir. `Tooltip` gösterim anında ölçüp kaydırır.
  **`calc(-50% + -112px)` bir AYRIŞTIRMA HATASIDIR** ve tarayıcı tüm bildirimi
  düşürür — kırpma sessizce hiçbir şey yapmaz. İşaret **operatöre** konur
  (`calc(-50% - 112px)`), operanda değil.
- **Taşma testi NEYİN geniş olduğunu söylemelidir** (Faz 164):
  `AssertNoHorizontalOverflowAsync` artık viewport'u aşan ilk beş öğeyi
  sınıflarıyla birlikte basar. Yalnız piksel sayısı vermek bir sonraki okuru
  otuz öğeyi elle ölçmeye gönderir.

## Primitifler ve erişilebilirlik

- **🚨 `Field`'ın zorunluluk yıldızı erişilebilir adın PARÇASIDIR**
  (2026-09-12, Faz 164): `aria-hidden` yapmak `Trigger_created_from_UI_is_listed`
  olgusunu düşürdü (`Name *` → `Name`). Test değil kod düzeltildi ve gerekçe
  testten bağımsızdır: `Field`'ın düz-`children` biçimi kontrolün üstüne
  `aria-required` **yazamaz** (çocuk bir `ReactNode`'dur), dolayısıyla yıldızı
  gizlemek zorunlu bir alanı isteğe bağlı gibi duyurur. Hata mesajını kontrole
  bağlamak isteyen çağıran **render-prop** biçimini kullanır:
  `<Field error={...}>{(ids) => <TextArea {...ids} />}</Field>`.
- **`title` özniteliği bir tooltip DEĞİLDİR** (Faz 164): dokunmatik ekranda hiç
  görünmez, klavyeyle odaklanan kullanıcıya hiç görünmez ve ekran okuyucular
  tutarsız okur. `components/tooltip.tsx` hover VE odakta görünür, `Esc` ile
  kapanır ve metni `aria-describedby` ile kontrole bağlar. Balon her zaman
  render edilir — yalnız hover'da var olan bir öğeye işaret eden
  `aria-describedby` zamanın geri kalanında hiçbir şeyi tarif etmez — ve
  gizliyken **yalnız** `sr-only` alır (Faz 165 kusuru: boyut sınıflarını da
  korumak balonu gizliyken 224 px geniş bırakıyordu, bkz. Yerleşim).
- **🚨 Bir NAVİGASYON `Button` değildir** (Faz 165): `<Link><Button>…</Button></Link>`
  bir `<a>` içinde `<button>` demektir — geçersiz HTML, tek hedef için iki tab
  durağı, ve işaretçinin hangisine düştüğüne göre değişen davranış. `LinkButton`
  gider (`href`, orta tuş, kopyala-bağlantı), `Button` yapar. `CONTROL_BASE` ve
  `CONTROL_TONES` ikisinin de kaynağıdır; ayrı yazılırsa 1 px kayarlar.
- **`Link` varsayılan bir görünüş taşır ve `className` onu TAMAMEN ezer**
  (Faz 165): `className ?? LINK_CLASS`. Tailwind sınıf sırası özgüllüğü
  belirlemez, dolayısıyla `cx(default, className)` iki rengin hangisinin
  kazandığını **belirsiz** bırakır. Bilerek soluk bir tablo bağlantısı veya
  düğme biçimli bir navigasyon varsayılanı devralmaz.
- **Bir açıklamayı odaklanılamayan bir öğeye koymak onu ULAŞILAMAZ yapar**
  (Faz 165): 26 badge `title` taşıyordu — dokunmatikte hiç, klavyede hiç
  görünmüyordu. `Badge description` badge'i `tabIndex={0}` yapar ve `Tooltip`'e
  verir. **Bedeli açıklanan badge başına bir tab durağıdır**; bu yüzden yalnız
  badge'in kendi metninin söylemediği bir şey varsa geçilir. Sütun açıklaması
  `Th description`'a gider: elli satır yerine bir durak.
- **Bir metriğin açıklaması GÖRÜNÜR metindir** (Faz 165): `Stat hint` artık
  `title` değil. Metrik kartında yer vardır, ve açıklamayı taşımak için
  odaklanabilir bir kontrol icat etmek açıklamayı basmaktan kötüdür.
- **Sekmeler tek bir yerden gelir** (Faz 165): `Tabs` hem şeridi hem paneli
  render eder, çünkü **kablolama** varlık sebebidir — `aria-selected`,
  `aria-controls`, panelin geri işaret etmesi, ve seçilmeyen sekmelerin tab
  sırasından çıkıp ok tuşlarına devredilmesi. İki ekran ayrı `TabButton` yazmıştı
  ve hiçbiri bunların hiçbirini taşımıyordu.
- **🚨 Boş durumun aksiyonu başlıktaki düğmenin etiketini TEKRARLAMAZ**
  (Faz 165): aynı erişilebilir ada sahip iki kontrol tek ekranda Playwright
  strict mode'u kırar (dört mevcut olgu düştü) ve ekran okuyucuya iki aynı
  düğme duyurur. `*.empty.action` anahtarları "ilkini oluştur" dilini taşır.
- **Modal katmanı TEK yerdedir** (Faz 164): `dialog.tsx`. Dört şeyi birden
  yapar (odağı içeri al · `Tab`'ı hapset · `Esc` · odağı tetikleyiciye geri
  ver) ve `Esc`'te `stopPropagation` çağırır — aksi hâlde kabuğun genel `Esc`
  bağlaması alttaki katmanı da kapatır. Komut paleti de bu hook'u kullanır.
  Kendi `fixed inset-0` panelini çizen bir ekran bir kusurdur.

## Kapılar

- **Konsolun `tsconfig`'i `@types/node` TAŞIMAZ** (2026-09-12, Faz 164): bu
  bilinçlidir. Dolayısıyla `node:fs` okuyan bir doğrulama **Vitest testi
  olamaz** — `tsc --noEmit` kırılır. Üstelik Vite, test altında
  `import.meta.url`'i `http://` adresine çevirir ve `fileURLToPath` atar. Böyle
  doğrulamalar `frontend/scripts/*.mjs` altında yaşar ve `npm run build`
  zincirinden çağrılır (`check-tokens.mjs` örneği).
- **Bundle bütçesi tek başına bir bağımlılığı durdurmaz** (2026-09-12, Faz 164,
  K-758): 6 KB'lik bir headless kitaplık 250 KB tavanın rahatça altında kalır
  ve yine her tüketicinin grafiğine girer. `postbuild.mjs` `dependencies`
  kümesini `ALLOWED_DEPENDENCIES` ile karşılaştırır ve farkta derlemeyi kırar.
- **Ekran envanteri `components/navigation.ts`'tir** (2026-09-12, Faz 164):
  kenar çubuğu, komut paleti ve `docs-site/scripts/check-console-screens.mjs`
  üçü de oradan okur. Sözlükten okumak `nav.*` ad alanındaki **her** anahtarı
  ekran sanıyordu; ekran olmayan bir anahtar (atlama bağlantısı) kapıyı
  kırmızı yapıp kendi ekran görüntüsünü istedi. Yeni bir ekran eklerken tabloya
  eklemek yeter; `ui.md` bölümü ve `public/screenshots/<path>.png` kapı
  tarafından zorlanır.
