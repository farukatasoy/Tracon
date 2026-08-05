# Frontend Tuzaklari

> Vite, SPA yonlendirme, TypeScript, mermaid.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **Mermaid'i jsdom olmadan doğrulayamazsın** (2026-08-02): `mermaid.parse` DOM ister, aksi hâlde `DOMPurify.addHook is not a function` verir — bu bir sözdizimi hatası değildir. Doğrulayıcı: `jsdom` ile `window`/`document`/`DOMParser` global'lerini kur, sonra `mermaid.initialize({startOnLoad:false})`.
- **Vite `base: './'` + çalışma anında `<base href>`** (2026-08-02): prefix çalışma anında bilindiği için tek yol budur. `<base>` etiketini Vite'a yazdırma — HTML boru hattı `href` niteliklerini yeniden yazar. `postbuild.mjs` `<head>` içine yer tutucuyu kendisi koyar.
- **🚨 SPA rota tabanında sondaki eğik çizgi** (2026-08-02): `<base href="/agentprism/">` iken `document.baseURI` sonda `/` taşır ama `location.pathname` taşımaz (`/agentprism`). Yalnız `startsWith(base)` ile karşılaştırmak en sık kullanılan giriş adresinde "sayfa bulunamadı" üretir. `toRelativePath` bu iki biçimi de kök kabul eder; regresyon testi `format.test.ts` içinde.
- **`npm ci` bozuk kullanıcı önbelleğinde de çalışabilir** (2026-08-02): `npm install` `~/.npm/_cacache` içindeki root sahipli dosyalarda `EACCES` verirken `npm ci` geçti. Yine de kalıcı çözüm `sudo chown -R $(id -u):$(id -g) ~/.npm`.
- **`Select` bilesenin `onChange`'i cig string deger verir, DOM olayi degil** (2026-08-03, Faz 19): `components/ui.tsx`'teki `Select` `(value: string) => void` alir; `TextInput`/`TextArea` gibi native `event.target.value` OKUNMEZ. `onChange={(value) => ...}` yazilmali, `onChange={(event) => ... event.target.value}` derleme hatasi verir.

## Kod haritasi — arayuz

> Bu bes not `kod-haritasi.md`'den TASINDI (2026-08-05, Faz 28): o dosya
> bütçesini asmisti ve arayuz notlarinin dogal yeri zaten burasidir.

- **Arayüz üç dosyada** (2026-08-02): `UI/AgentPrismUiBuilderExtensions.cs` (`UseUI()`), `UI/Internal/EmbeddedUiProvider.cs` (sunum), `AspNetCore/Endpoints/UiEndpoints.cs` (rotalar). Sözleşme `AspNetCore/Ui/IAgentPrismUiProvider.cs` — tek metotlu (K-049).
- **Frontend derleme zinciri tek dosyada** (2026-08-02): `src/AgentPrism.UI/AgentPrism.UI.Frontend.targets`. npm adımları, damga dosyaları, `EmbeddedResource` toplama ve `pack` doğrulaması orada.
- **Bundle bütçesi + Brotli + base yer tutucusu tek betikte** (2026-08-02): `frontend/scripts/postbuild.mjs`. `npm run build` bunu çağırır; kapı bu yüzden hem yerelde hem `dotnet build`'de çalışır.
- **Frontend saf mantık `src/lib/` altında** (2026-08-02): `sse.ts` (SSE çözümleyici), `transcript.ts` (olay/güncelleme birleştirme), `format.ts`, `router.tsx`. Vitest yalnız bunları test eder; ekranlar Playwright ile doğrulanır.
- **Graf duzeni frontend'de saf mantiktir** (2026-08-03, Faz 16): `src/lib/workflow-graph.ts` → `computeLayers` (BFS, dongu guvenli), `layoutGraph` (sutun yerlesimi + SVG yol uretimi), `foldNodeStates` (olaylardan dugum durumu). Vitest yalniz bunlari test eder; `components/workflow-graph.tsx` yalnizca boyar.
- **Ses calma nesne URL'i ile yapilir** (2026-08-05, Faz 28): `screens/playground.tsx` icindeki `SpeakButton`. 🚨 `<audio src="api/attachments/{id}">` YAZILAMAZ — tarayici bearer basligini kaynak yuklemesine eklemez ve token acikken istek 401 alir. Baytlar `api.attachmentBlob(id)` ile cekilip `URL.createObjectURL` ile sarilir; `useAttachmentPreview` (goruntu onizlemesi) ayni sebeple Faz 14'te boyle yazilmisti. Nesne URL'i `useEffect` temizliginde `revokeObjectURL` ile birakilir.
- **🚨 `MediaRecorder` kap basligini YALNIZ ILK parcaya yazar** (2026-08-05, Faz 29): zaman dilimli (`start(250)`) bir kayittan alinan ikinci konusma parcasi basliksiz gelir ve cozulemez. Kaydedici her konusma parcasi icin `stop()` + `start()` ile YENIDEN baslatilir; `onstop` `commit` gonderir. Oynatma `decodeAudioData` ile yapilir ve bu tam bir dosya ister — sunucu sesi cumle cumle `audioStart`/`audioEnd` arasinda gonderir, kismi akis beslenmez (MSE'nin ham ses kaplarindaki destegi esit degildir). Ayrinti: `components/voice-panel.tsx`.

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

### Yerellestirme dosyalari nerede (2026-08-05, Faz 30)

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
