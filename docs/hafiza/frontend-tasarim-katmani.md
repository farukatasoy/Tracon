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
- **🚨 BOŞ bir ekranda koşan taşma testi hiçbir şey kanıtlamaz** (2026-09-12,
  Faz 164): eski 375 px olgusu run seed etmiyordu, grafikler `EmptyChart`
  çiziyordu ve taşma hiç doğmuyordu. Yukarıdaki kusur bu yüzden aylarca
  görünmedi. Yeni olgu önce playground'dan gerçek bir run üretir.
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
  render edilir, gizliyken yalnız `sr-only` ile ekran dışına alınır — yalnız
  hover'da var olan bir öğeye işaret eden `aria-describedby` zamanın geri
  kalanında hiçbir şeyi tarif etmez.
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
