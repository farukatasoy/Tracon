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
