# Arayuz Yerellestirme Tuzaklari

> `useT()`, `Messages` tipi, sozluk dosyalari, `Intl` bicimlendirme ve dil
> basina ses tercihi. Diger arayuz notlari icin: [`frontend.md`](frontend.md).
> Dil siniri kurali (`SourceLanguageTests`) `frontend.md`'de kalir.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `frontend.md` 16 KB butcesinde %14 bosluga dusmustu.
> Yerellestirme kendi basina tutarli bir eksen (Faz 30) ve her yeni ekranla
> BAGIMSIZ buyuyor. K-214 merdiveni: gercek bolunme.
## Yerellestirme (Faz 30, 2026-08-05)

- **🚨 `useT()` KARARLI bir referans dondurur** (2026-08-05, Faz 30): `useT` baglami
  okur (bilesen dil degisiminde yeniden cizilir) ama **modul duzeyindeki**
  `translate` fonksiyonunu dondurur. Dil basina yeni closure dondurulseydi `t`,
  `useCallback` bagimlilik dizilerine girer ve `voice-panel.tsx`'teki `start`/`stop`
  yeniden kurulur, **acik konusma WebSocket'i koparadi**. Yeni bir hook yazarken
  bu kaliba uy. Karar K-229.
- **🚨 Sozluk tipi `as const` ALMAZ** (2026-08-05): `locales/en.ts` icindeki nesne
  `as const` ile bildirilirse `Messages` cumleleri de tasir ve `tr` ancak
  Ingilizce metni birebir tekrarladiginda derlenirdi. `as const` yoksa TypeScript
  degerleri `string`'e genisletir, anahtarlari aynen tutar — istenen sozlesme budur.
- **🚨 Iki mesajdan biri digerinin ONEKI olmamali** (2026-08-05): Playwright'in
  `GetByText` cagrisi alt dizi esler. `charts.noRuns` ("No run in this window") ile
  `dashboard.noRunsInWindow` ("No run in this window yet.") cakisti ve E2E'yi strict
  mode ihlaliyle kirdi. Ikincisi yeniden yazildi.
- **🚨 Metin uzerine iddia kuran E2E testi dili SABITLEMELIDIR** (2026-08-05):
  varsayilan dil `navigator.language`'dan gelir (K-231), dolayisiyla sabitlenmezse
  testin sonucu calistiran makinenin sistem diline baglanir. `Session.OpenAsync`
  varsayilani `en-US`'tir; dil testleri acikca baska deger gecer.
- **Rozet metni kucuk harf, secenek/baslik buyuk harf** (2026-08-05): konsolun rozet
  dili kucuk harftir (`code`, `db`, `ok`, `done`). Ayni durum adi bir `<option>`
  etiketinde buyuk harf ister. Bu yuzden `runs.status.*` (rozet) ve `runs.filter.*`
  (suzgec/baslik) iki ayri anahtar kumesidir — kopya degil, iki sunum baglami (K-233).
- **`microphoneSupport()` bir MESAJ ANAHTARI dondurur** (2026-08-05): gerekce
  kullaniciya gosterildigi icin dile uymak zorundadir. Anahtar dondurmek modulu
  React'ten ve sozlukten bagimsiz tutar; panel `t(support.reason)` ile cozer.
- **Biçimlendirme `Intl` ile yapilir** (2026-08-05): `relativeTime` →
  `Intl.RelativeTimeFormat`, `absoluteTime` → `Intl.DateTimeFormat`,
  `count`/`percent`/`money` → `Intl.NumberFormat`. Turkce `1.234,5`, Ingilizce
  `1,234.5` yazar. SI birim simgeleri (`ms`, `s`, `m`) **cevrilmez**; `duration()`
  her dilde aynidir.
- **`applyDocumentLocale` DOM yoksa sessizce doner** (2026-08-05): Vitest Node
  ortaminda kosar ve saf biçimlendirme testleri `initialiseLocale()` cagirir.
- **Sozlukler bundle'in buyume kaynagidir** (2026-08-05): 794 anahtar × 2 dil =
  +26,4 KB gzip; i18n calisma zamani, komut paleti ve kisayol modulu birlikte
  gurultu seviyesinde. Toplam 151,3 KB / 250 KB. Ucuncu dil kabaca +13 KB getirir;
  dorduncu dilden once gecikmeli yukleme degerlendirilir.
- **🚨 Kontrast degeri degistirmeden once OLC** (2026-08-05): `--ap-subtle` ve
  `--ap-muted` 11–12 px'te kullanilir, yani WCAG icin normal boy metindir ve
  4,5:1 ister. Eski degerler 3,2–3,8:1 ile kaliyordu. Esikler ve olculen degerler
  `styles.css` icinde 🚨 notuyla yazilidir (K-236).

## Yerellestirme dosyalari nerede (2026-08-05, Faz 30)

- `src/lib/i18n.tsx` — sozluk secimi, `translate`/`plural`, `Intl` sarmalayicilari,
  `LocaleProvider`, `useLocale`/`useT`/`usePlural`. `initialiseLocale()` `main.tsx`'te
  ilk render'dan **once** cagrilir (`applyTheme` ile ayni gerekce).
- `src/locales/en.ts` — anahtar kumesinin **kaynagi**; `Messages` tipi buradan cikar.
- `src/locales/tr.ts` — `tr: Messages`; eksik anahtar derleme hatasi.
- `src/lib/shortcuts.ts` — saf kisayol eslemesi (`chordOf`, `isTextEntry`,
  `createShortcutMatcher`). DOM'a dokunan tek yer `components/layout.tsx`'teki
  `useConsoleShortcuts`; pencere dinleyicisi bir ref uzerinden **tek** tutulur.
- `src/lib/palette.ts` — bulanik eslesme (`fuzzyScore`, `rankCommands`).
- `src/components/command-palette.tsx` — `CommandPalette` + `ShortcutHelp`.
- Dil basina ses tercihi: `lib/voice.ts` icinde `readVoiceForLocale` /
  `writeVoiceForLocale`; secici `screens/settings.tsx` icindeki `VoicePreferencePanel`.
