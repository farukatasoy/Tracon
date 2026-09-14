# Frontend Tuzaklari

> Vite, SPA yonlendirme, TypeScript, mermaid.
>
> Yerellestirme AYRI dosyadadir:
> [`frontend-yerellestirme.md`](frontend-yerellestirme.md).
> Token seti, tema, yogunluk olcegi, primitifler ve erisilebilirlik de AYRI:
> [`frontend-tasarim-katmani.md`](frontend-tasarim-katmani.md).
> Vitest component testi ve `openapi-fetch` stub'lari da AYRI:
> [`frontend-test-altyapisi.md`](frontend-test-altyapisi.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **Mermaid'i jsdom olmadan doğrulayamazsın** (2026-08-02): `mermaid.parse` DOM ister, aksi hâlde `DOMPurify.addHook is not a function` verir — bu bir sözdizimi hatası değildir. Doğrulayıcı: `jsdom` ile `window`/`document`/`DOMParser` global'lerini kur, sonra `mermaid.initialize({startOnLoad:false})`.
- **Vite `base: './'` + çalışma anında `<base href>`** (2026-08-02): prefix çalışma anında bilindiği için tek yol budur. `<base>` etiketini Vite'a yazdırma — HTML boru hattı `href` niteliklerini yeniden yazar. `postbuild.mjs` `<head>` içine yer tutucuyu kendisi koyar.
- **🚨 SPA rota tabanında sondaki eğik çizgi** (2026-08-02): `<base href="/tracon/">` iken `document.baseURI` sonda `/` taşır ama `location.pathname` taşımaz (`/tracon`). Yalnız `startsWith(base)` ile karşılaştırmak en sık kullanılan giriş adresinde "sayfa bulunamadı" üretir. `toRelativePath` bu iki biçimi de kök kabul eder; regresyon testi `format.test.ts` içinde.
- **`npm ci` bozuk kullanıcı önbelleğinde de çalışabilir** (2026-08-02): `npm install` `~/.npm/_cacache` içindeki root sahipli dosyalarda `EACCES` verirken `npm ci` geçti. Yine de kalıcı çözüm `sudo chown -R $(id -u):$(id -g) ~/.npm`.
- **`Select` bilesenin `onChange`'i cig string deger verir, DOM olayi degil** (2026-08-03, Faz 19): `components/ui.tsx`'teki `Select` `(value: string) => void` alir; `TextInput`/`TextArea` gibi native `event.target.value` OKUNMEZ. `onChange={(value) => ...}` yazilmali, `onChange={(event) => ... event.target.value}` derleme hatasi verir.

## Kod haritasi — arayuz

> Bu bes not `kod-haritasi.md`'den TASINDI (2026-08-05, Faz 28): o dosya
> bütçesini asmisti ve arayuz notlarinin dogal yeri zaten burasidir.

- **Arayüz üç dosyada** (2026-08-02): `UI/TraconUiBuilderExtensions.cs` (`UseUI()`), `UI/Internal/EmbeddedUiProvider.cs` (sunum), `AspNetCore/Endpoints/UiEndpoints.cs` (rotalar). Sözleşme `AspNetCore/Ui/ITraconUiProvider.cs` — tek metotlu (K-049).
- **Frontend derleme zinciri tek dosyada** (2026-08-02): `src/Tracon.UI/Tracon.UI.Frontend.targets`. npm adımları, damga dosyaları, `EmbeddedResource` toplama ve `pack` doğrulaması orada.
- **Bundle bütçesi + Brotli + base yer tutucusu tek betikte** (2026-08-02): `frontend/scripts/postbuild.mjs`. `npm run build` bunu çağırır; kapı bu yüzden hem yerelde hem `dotnet build`'de çalışır.
- **Frontend saf mantık `src/lib/` altında** (2026-08-02): `transcript.ts` (olay/güncelleme birleştirme), `format.ts`, `router.tsx`. Vitest yalnız bunları test eder; ekranlar Playwright ile doğrulanır. 🚨 SSE çözümleyici artık burada DEĞİL: Faz 159'da `packages/tracon-client/src/sse.ts`'e taşındı ve `readSse`/`SseDecoder` olarak dışa açıldı — ekranlar onu `@tracon/client`'tan alır ve o import `dist/`'i çözer (K-627).
- **Graf duzeni frontend'de saf mantiktir** (2026-08-03, Faz 16): `src/lib/workflow-graph.ts` → `computeLayers` (BFS, dongu guvenli), `layoutGraph` (sutun yerlesimi + SVG yol uretimi), `foldNodeStates` (olaylardan dugum durumu). Vitest yalniz bunlari test eder; `components/workflow-graph.tsx` yalnizca boyar.
- **Ses calma nesne URL'i ile yapilir** (2026-08-05, Faz 28): `screens/playground.tsx` icindeki `SpeakButton`. 🚨 `<audio src="api/attachments/{id}">` YAZILAMAZ — tarayici bearer basligini kaynak yuklemesine eklemez ve token acikken istek 401 alir. Baytlar `api.attachmentBlob(id)` ile cekilip `URL.createObjectURL` ile sarilir; `useAttachmentPreview` (goruntu onizlemesi) ayni sebeple Faz 14'te boyle yazilmisti. Nesne URL'i `useEffect` temizliginde `revokeObjectURL` ile birakilir.
- **🚨 `MediaRecorder` kap basligini YALNIZ ILK parcaya yazar** (2026-08-05, Faz 29): zaman dilimli (`start(250)`) bir kayittan alinan ikinci konusma parcasi basliksiz gelir ve cozulemez. Kaydedici her konusma parcasi icin `stop()` + `start()` ile YENIDEN baslatilir; `onstop` `commit` gonderir. Oynatma `decodeAudioData` ile yapilir ve bu tam bir dosya ister — sunucu sesi cumle cumle `audioStart`/`audioEnd` arasinda gonderir, kismi akis beslenmez (MSE'nin ham ses kaplarindaki destegi esit degildir). Ayrinti: `components/voice-panel.tsx`.

## Ekran ve bilesen tuzaklari

- **🚨 Playwright'in `ColorScheme` secenegi konsolun temasini DEGISTIRMEZ** (2026-09-15, anasayfa revizyonu): `lib/theme.ts` icindeki `DEFAULT_PREFERENCE` acikca `'dark'`tir ve `prefers-color-scheme` yalnizca saklanmis tercih `'system'` iken okunur. `ColorScheme = ColorScheme.Light` ile cekilen 19 ekran goruntusunun hepsi KOYU cikti ve dosya adlari `-light` oldugu icin hata sessizdi. Tema zorlamanin tek yolu tercihi tohumlamaktir: `context.AddInitScriptAsync("window.localStorage.setItem('tracon.theme','light')")` — sayfa script'lerinden ONCE kosar, yani ilk boyama zaten dogru temadir. Ayni tuzak konsolun temasina bagli her gorsel testi icin gecerlidir.
- **Mesaj bazlı "buradan dallan" YALNIZ oturum ekranındadır** (2026-08-07, Faz 47): `upToSequence` bir `conversation_items.seq` değeridir. `GET /api/sessions/{id}` geçmişi kayıtlı `ChatHistoryProvider` üzerinden sıra numarasına göre döndürür, dolayısıyla i'nci mesaj tam olarak `seq = i`'dir. Playground'un dökümü ise canlı SSE akışından katlanır ve hiçbir sıra numarası taşımaz; oradaki dallanma bu yüzden konuşmanın TAMAMINI kopyalar (`upToSequence` verilmez).
- **🚨 `relativeTime()` GELECEK bir zaman damgası için YANLIŞ — daima "az önce" yazar** (2026-08-09, Faz 55): saat kayması korumasi `if (elapsed < 1_000) return formatRelative(0, 'second')` NEGATİF `elapsed`'de de (yani gelecekteki her an) tetiklenir. Bir onayın `expiresAt`'ı her zaman gelecektedir — o alan için `relativeTime()` DEĞİL `absoluteTime()` kullan (`screens/approvals.tsx`). Kural: yalnız GEÇMİŞ zaman damgaları için `relativeTime()`.
- **🚨 `Blob.arrayBuffer()` zincirlenmeden gönderilirse WebSocket sırası bozulur** (2026-08-14, manuel kabul testi kapanışı): `arrayBuffer()` asenkron çözülür. `MediaRecorder.ondataavailable` içinde her gönderimi tek bir `pendingSends` promise zincirine bağlamazsan iki arıza doğar — parçalar sokete **sırasız** ulaşıp WebM akışını bozar, ve `commit` çerçevesi taahhüt ettiği sesin **önüne geçer** (sunucu boş tampon bulur, turu sessizce düşürür). Desen `components/voice-panel.tsx` içinde 🚨 yorumuyla yazılıdır; ses/ikili veri gönderen her yeni yolda tekrarlanır.
- **Yeni bir sunucu enum'ı arayüze girdiğinde `i18n.test.ts`'in `identicalOnPurpose` listesi kontrol edilir** (2026-08-07, Faz 47): iki dilde aynı kalan teknik terim (`Model` gibi) listeye eklenmezse "her anahtar çevrilmiş olmalı" testi düşer. Liste bir kısayol değil, bilinçli bir karardır.
- **🚨 Konsol DIŞINDA yeni bir `.ts` dosyasına Türkçe metin yazarsan `SourceLanguageTests` (Core.UnitTests, K-408) taban çizgisini büyütür ve kapı KIRMIZI olur** (2026-08-18, F-108/K-435, denetimde bulundu): istisna listesi (`SourceLanguageTests.SkippedFiles`) yalnız `src/Tracon.UI/frontend/src/locales/tr.ts`'yi tanır — başka bir yoldaki Türkçe metin (ör. yeni bir widget'ın kendi sözlüğü) kapsanmaz. En+tr'yi TEK dosyada karıştırma; `locales/tr.ts` deseninin AYNISINI tekrarla (Türkçe kısmı ayrı bir `*.tr.ts` dosyasına al) ve o dosyayı `SkippedFiles`'e ekle. Bu istisna genişletmesi kendi başına bir karar sayılır, sessizce eklenmez.
- **Gömülebilir/bağımsız bir JS paketi için Vite'ın kütüphane modu (`build.lib`, `formats: ['iife']`) ayrı bir `vite.<ad>.config.ts` ister — konsolun `vite.config.ts`'ine ikinci `input` eklemek YETMEZ** (2026-08-18, F-108/K-441/K-442): ikinci bir Vite girişini AYNI config'e eklemek iki paketin kodunu AYNI bütçe/derleme adımında karıştırır — konsol kodunun küçük bir widget'a sızdığı fark edilmeden büyür. Ayrı config, ayrı `outDir` (`wwwroot/<alt-dizin>`, konsolun `emptyOutDir`'inden SONRA derlenmeli — sıra `npm run build`'de sabitlenir), ayrı postbuild script'i (ayrı bütçe) gerektirir. `wwwroot/<alt-dizin>/` içindeki çıktı `EmbeddedUiAssetCatalog`'un GENEL `wwwroot/**/*` taramasıyla otomatik gömülür ve `{prefix}/{**path}` rotasıyla otomatik sunulur — **yeni bir C# `endpoint` yazmaya gerek yoktur**, içerik türü uzantıdan çözülür.

- **🚨 Boş grafik metni PAYLAŞILDIĞI için yeni bir panel eklemek var olan E2E testini strict mode ile kırar** (2026-08-19, Faz 68): gösterge paneline "Token kırılımı" paneli eklenince boş bir kurulumda `charts.noRuns` metni İKİ panelde birden göründü ve `Dashboard_charts_render_and_range_can_be_changed` düştü. Çözüm belgelenmiş desendir: `GetByText(...).First`. Paylaşılan bir boş-durum metni kullanan yeni panel eklerken o metne dayanan E2E testlerini önceden tara.
- **Gösterge paneli kırılım çubuğu saf mantığı `lib/chart.ts`'tedir** (2026-08-19, Faz 68): `tokenBreakdown(totals)` dört DİSJOINT dilim üretir ve hesap ÇIKARMALIdır — `cachedInputTokens`/`reasoningTokens` girdi/çıktı toplamlarının İÇİNDE sayılır, dördünü ham hâlde yığmak harcanandan uzun bir çubuk çizer. Vitest yalnız bu fonksiyonu test eder; `components/charts.tsx`'teki `TokenBreakdownChart` yalnız boyar. Renkler `--tracon-series-6/1/5/4` (Faz 164'e kadar `--ap-emerald/violet/amber/cyan`); seri paleti durum renklerini ICERMEZ.
- **🚨 Aynı erişilebilir adı taşıyan İKİNCİ bir kontrol Playwright strict mode'u
  kırar** (2026-09-12, Faz 165): boş durumlara birincil aksiyon eklenince aksiyon
  başlıktaki düğmenin etiketini birebir tekrarladı ve **dört mevcut E2E olgusu**
  birden düştü (`mcp`, `jobs`, `evals` ve eval detayında iki kez). Yukarıdaki
  `.First` deseninin ikizi ama çözümü o değil: iki kontrol gerçekten farklı
  şeyler söylüyor, o yüzden **metinleri** ayrıştı (`*.empty.action` → "ilkini
  oluştur"). Ekranda yeni bir düğme açarken `grep -rn '<etiket>'
  tests/Tracon.Ui.E2ETests/` ile o adı arayan olguları önceden tara.
- **`screens/<ad>.tsx` + `screens/<ad>/` YAN YANA yaşayabilir ve desen budur**
  (2026-09-12, Faz 165): `agent-editor.tsx` ekranı, `agent-editor/` parçaları
  tutar. `skills.tsx` aynı biçime geçti — dosyanın kendisi liste, `skills/`
  altında `skill-editor.tsx` · `script-grants.tsx` · `model.ts`. Tek dosyada iki
  desen (liste + form) taşımak ikisini de kanonik hâlinden uzaklaştırıyordu.
- **🚨 `isPending` istek DÜŞER DÜŞMEZ `false` olur; hata dalı yoksa editör BOŞ
  FORM gösterir** (2026-09-12, Faz 165, Faz 164 denetim 🟡 #2 ile aynı sınıf):
  `triggers`, `skills` ve `workflow-editor` yükleme hatasında "boş yüklenmiş bir
  kayıt" gibi görünüyordu ve **o hâlden kaydetmek gerçek tanımı hiçliğe
  çevirirdi**. Bir kayıt yükleyen her ekran ÜÇ dal taşır: `isPending`,
  `isError`, `isSuccess` — ikisi yetmez.
- **Bir mutation'ın hatası GÖSTERİLMEZSE işlem başarılı görünür** (2026-09-12,
  Faz 165): on dört `useMutation` (tetikleme, OAuth başlatma, silme, izin verme)
  reddedildiğinde ekran aynen kalıyordu. `isError` dalı olmayan bir mutation bir
  kusurdur; `onRetry` için `mutation.variables` kullanılır (argümanlı olanlarda
  `undefined` kontrolüyle).
- **`Record<RunEventType, ...>` sözlüğü eksik anahtarı DERLEME HATASI yapar — `run-detail.tsx`'teki `EVENT_STYLE` bu yüzden `ModelFallbackUsed`'ın (Faz 62'den beri backend'de var olan) frontend'de HİÇ tanımlanmadığını Faz 70'te ortaya çıkardı** (2026-08-19, Faz 70): `types.ts`'teki `RunEventType` union'ına yeni bir üye eklemek `EVENT_STYLE`'ın tüm anahtarları taşımasını ZORUNLU kılar (TS2739 benzeri hata) — bu, backend enum'ı ile frontend union'ının senkron kalmasını sağlayan TEK mekanizmadır. Yeni bir `RunEventType` üyesi eklerken `types.ts`'in union'ına VE `run-detail.tsx`'in `EVENT_STYLE`'ına birlikte eklenmeli; biri unutulursa derleyici yakalar, ikisi de eklenmezse (union'a hiç eklenmezse) hiçbir uyarı gelmez ve olay `foldRunEvents`'in `default: break` dalına sessizce düşer.
