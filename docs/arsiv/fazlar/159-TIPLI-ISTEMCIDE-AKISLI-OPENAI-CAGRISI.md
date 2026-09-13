# Faz 159 — Tipli İstemcide Akışlı OpenAI Çağrısı

> **Durum:** ✅ Tamamlandı (2026-09-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-198**
> **Önkoşul:** Yok — ama 🚨 **gövde bildirimi kusuru bu plandan ÖNCE kapandı** (2026-09-08, `DeclaredRequestBodyTests`). Plan o düzeltilmiş imzanın üstüne yazılmıştır
> **Paketler:** `Tracon.Client` (üretilen) · `@tracon/client` (üretilen)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — iki yeni üretilmiş metot. Yayımlanmamış olduğu için bugün ucuz
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/openai-api.md` · `guides/typescript-client.md` · sevk edilen: `Tracon.Client` README'si
> **Manuel test alanı:** [`docs/manuel-test/34-ISTEMCI-VE-CLI.md`](../../manuel-test/34-ISTEMCI-VE-CLI.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 0e38c382:docs/arsiv/fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md
> ```
>
> Damıtıldı 2026-09-08 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`/v1/responses` ve `/v1/chat/completions` 200 yanıtı için **iki içerik tipi** ilan eder — `application/json` ve `text/event-stream` — ve hangisinin döneceğini istek gövdesindeki `stream` bayrağı çalışma anında seçer. Üretilen tipli istemci yalnız JSON şeklini bilir. `stream: true` gönderen bir çağıran SSE gövdesini JSON çözücüye vermiş olur.

## Bitiş Ölçütleri (DoD)

- [x] `…StreamAsync` gerçek sunucuya karşı `stream: true` gövdesiyle çerçeve üretir; çıktı belgeye yazıldı
- [x] `…Async` ile `stream: false` bugünkü davranışı **birebir** korur
- [x] `stream: true` + JSON metodu birleşimi sessizce çökmüyor; davranış belgelendi
- [x] Postprocess tam yeniden üretimle koşuldu ve delta `diff` ile doğrulandı — yalnız yeni geçişin farkı
- [x] TypeScript istemcisi için ölçüm yapıldı ve Açık Soru 3 karara bağlandı
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/34-ISTEMCI-VE-CLI.md` içine eklendi ve **`00-INDEKS.md` sayımı güncellendi**
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (`guides/openai-api.md` akış bölümü); `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Tam yeniden üretim ve delta doğrulaması
python3 scripts/nswag-prepare-document.py docs/openapi/tracon.json artifacts/openapi/tracon.client-input.json
dotnet nswag run nswag.json
python3 scripts/nswag-postprocess-client.py src/Tracon.Client/Generated/TraconApiClient.g.cs
git diff --stat src/Tracon.Client/Generated/
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne oldu | Neden |
|---|---|---|---|
| 1 | Gövde parametresi `JsonElement body` (Planlanan Public API) | **Gerçekte `object body` idi.** Planın kanıt tablosu yanlıştı | Aynı gün kapanan gövde bildirimi kusuru `.Accepts<object>` bırakmıştı. Ölçüm: `object` gövde **çağrılamaz** — istemci kaynak-üretilmiş `JsonSerializerContext` ile serilestirir, anonim tip/POCO geçen her çağıran `NotSupportedException` alır. Sunucu `.Accepts<JsonElement>`'e çevrildi; imza artık planın yazdığı hâle geldi |
| 2 | Kapsam: yalnız iki dual uç | **SSE ilan eden yedi ucun tamamı** (kullanıcı kararı) | Açık Soru 1 "ailede iki farklı akış sözleşmesi" riskini yazıyordu. İki uçla sınırlamak tam da onu üretirdi (5 tamponlu + 2 akışlı). Geçiş genelleşince **basitleşti** de: "dual" özel durumu kalmadı, ayırt edici `text/event-stream` ilanının kendisi oldu |
| 3 | Açık Soru 2 önerisi B: "gövdeyi olduğu gibi gönder, belgele" | Gövde **yine** olduğu gibi gönderilir, ama yanıtın `Content-Type`'ı denetlenir ve tanımlı hata atılır (kullanıcı kararı) | Saf B, `…StreamAsync` + `stream:false` birleşimini **sessiz boş akış** bırakıyordu (SSE ayrıştırıcısı JSON belgesinde çerçeve bulamaz) — DoD'nin "sessiz çökme değil" satırıyla çelişirdi. Guard çağıranın gövdesini değiştirmez; yalnız sunucunun **gerçekten** ne yanıtladığına bakar |
| 4 | Açık Soru 3: "ölç, sonra karar ver" | Ölçüldü: `parseAs: 'stream'` **zaten çalışıyor** (B doğru). Ama paket bir SSE çözücüsü **sevk etmiyordu** | Her tüketici çerçeveleyiciyi yeniden yazacaktı. Frontend'in test edilmiş `SseDecoder`/`readSse`'si pakete taşındı ve dışa açıldı; frontend artık onu paketten alır (kullanıcı kararı) |
| 5 | Geçiş "altıncı" olarak eklenecekti | Altıncı geçiş **dördüncüden ÖNCE** koşar | Dördüncü geçiş saf-SSE 200 dalını yeniden yazınca yedi ucun dalı iki ayrı şekle ayrılır. Önce koşunca hepsi hâlâ NJsonSchema'nın **tek** şeklindedir ve tek desen yediyi de eşler. `main` bunu iddia olarak zorlar: dördüncü geçiş sonrasında hâlâ tam `len(operations) - guarded` eşleşme olmalı |
| 6 | Kapsam dışı | `.Accepts<JsonElement>` **CS0019** doğurdu (`if (body == null)` bir struct'ta derlenmez) | K-633'ün belgelediği sınıfın **ikinci ekseni**: üçüncü geçiş yanıt tarafındaki null denetimini zaten siliyordu, gövde tarafındakini bilmiyordu. Silmek yerine **anlamlı guard'a çevrildi** — `default(JsonElement)` aksi hâlde serilestiricinin içinde `InvalidOperationException: Operation is not valid due to the current state of the object` ile düşüyordu (ölçüldü) |

### Faz dışında kapatılan kusurlar

Kullanıcı "konuyla alakasız kusurları da çöz" dedi; yol üstünde bulunanlar:

| Kusur | Nerede | Düzeltme |
|---|---|---|
| `typescript-client.md` "Server-Sent Events **desteklenmiyor**" diyordu | Sevk edilen site sayfası | Artık destekleniyor — bölüm `## Streaming responses` olarak yeniden yazıldı |
| Aynı sayfa "**Six** operations answer `text/event-stream`" diyordu | Aynı | **Yedi**; `GET /api/runs/{runId}/events` sayımdan düşmüştü |
| Kök `README.md` "HTTP API (**160** operations)" diyordu | Sevk edilen metin | **165**. `http-api.md`'nin aynı iddiası kapılıydı ve doğruydu; kök README'ninki değildi |
| `Tracon.Client` README'si "**160** generated methods" diyordu | Paket README'si | Sabit sayı kaldırıldı — "one per operation in the document" |
| `Tracon.Client.csproj` yorumu "160 generated operations" diyordu | Kaynak yorumu | Aynı düzeltme |

## Bu Fazda Verilen Kararlar

Karar defterine **yeni `K-*` kaydı girmedi.** Üç aday tartıldı ve üçü de
`AGENTS.md`'nin ölçütünü karşılamadı:

- **İki metotlu tasarım** — `Tracon.Client` public API takibinin dışındadır
  (K-566) ve yüzey belgeden türetilir; bu bir üretim kuralıdır, bir contract
  kararı değil. Kural `nswag-postprocess-client.py`'nin docstring'inde ve
  `docs/hafiza/nswag-istemci-uretimi.md`'de yaşar.
- **`.Accepts<object>` → `.Accepts<JsonElement>`** — belge **anlamca aynı**
  kalır (her ikisi de "her türlü JSON"), TypeScript çıktısı **birebir** aynıdır
  (`JsonElement` = `unknown`). Kırıcı bir sözleşme değişikliği değil, yanlış bir
  imzanın düzeltilmesi. K-633'ün "yeniden açılmaz" notu bu sınıfı zaten
  kapsıyor: yeni bir opak değer tipi keşfedilirse tabloya eklenir, yeni karar
  gerekmez.
- **`readSse`'nin pakete taşınması** — paket içi bir modül yerleşimi; public
  npm yüzeyi büyür ama `@tracon/client` yayımlanmamıştır ve yüzeyi zaten
  belgeden türetilir.

## Gerçek Koşum Kanıtı

`samples/Tracon.Api` ayağa kaldırıldı (`EchoModelProvider`, ağ çağrısı yok)
ve üretilen istemciyle **gerçek** çağrılar yapıldı:

```
## 1. TraconOpenAIResponsesStreamAsync  (stream: true)
  [0] event: response.created | data: {"type":"response.created",...
  [1] event: response.in_progress | data: {...
  [2] event: response.output_item.added | data: {...
  [18] event: response.completed | data: {...
  -> 19 frames

## 2. TraconOpenAIChatCompletionsStreamAsync  (stream: true)
  first: data: {"id":"chatcmpl-...","object":"chat.completion.chunk",...
  last : data: [DONE]
  -> 4 frames

## 3. TraconOpenAIResponsesAsync  (stream: false)
  object=response status=completed
  text=Merhaba! Size nasıl yardımcı olabilirim?

## 4. JSON method + stream:true
  TraconApiException: The server answered 200 with content type
  'text/event-stream', not 'application/json'. ... Send "stream": false, or call
  TraconOpenAIResponsesStreamAsync for the streaming shape.

## 5. Streaming method + stream:false
  TraconApiException: The server answered 200 with content type
  'application/json', not 'text/event-stream'. ... Send "stream": true, or call
  TraconOpenAIResponsesAsync for the JSON shape.

## 6. default(JsonElement) body
  ArgumentException (body): The request body is an uninitialized JsonElement.

## 7. TraconRunAgentStreamAsync + early break
  [0] id: 0 | event: run | data: {"runId":"01a081fb-...
  [1] id: 1 | event: update | data: { | data:   "authorName": "cached-support",...
  -> break after 2 frames (stream released)
```

🚨 Örnek uygulamanın `secret`'ına **dokunulmadı**: `Tracon__Ui__AuthToken`
ortam değişkeni yalnız bu koşum için verildi (ortam değişkeni user-secrets'ı
ezer), böylece kullanıcının kendi token'ı ne okundu ne yazdırıldı.

### Yeniden üretim deltası

Taban doğrulaması **önce** yapıldı: değiştirilmemiş script tam zinciri koşunca
`git diff` **boş** döndü — hat güvenilir. Altıncı geçişten sonra delta yalnız
`TraconApiClient.g.cs`'te **+1003 satır** (7 kardeş metot + 2 guard);
`TraconClientJsonContext.g.cs` **değişmedi** (153 kök tip, aynı), yani yeni
metotlar hiçbir yeni serilestirme kökü getirmedi.

## Site Senkronu

`tuketici-dokuman-senkronu` koşuldu. Dört kural tetiklendi; üçü hedefiyle
karşılandı (`http-api` → `docs/openapi/tracon.json` · `paket-tanimi` ve
`paket-readme` → `packages.md`). Dördüncüsü **gerekçeyle** geçildi:

> **`arayuz` → `ui.md` güncellenmedi.** Tetikleyen üç dosya
> (`screens/run-detail.tsx`, `screens/workflow-detail.tsx`,
> `screens/playground/use-playground-run.ts`) yalnız **import kaynağını**
> değiştirdi: `../lib/sse` → `@tracon/client`. Aynı `readSse`, aynı
> çağrı, aynı çerçeveler. Hiçbir ekran, hiçbir metin, hiçbir davranış
> değişmedi; ekran görüntüsü de bayatlamadı. `ui.md`'ye yazılacak bir şey
> yoktur ve uydurmak sayfayı yanlış yapardı.

Kapı `--site-gerekce-yazildi` ile geçildi; bu bölüm o gerekçenin kaydıdır.

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, yalnız DoD + `git diff 9539b670`) koştu.
**Bir 🔴, altı 🟡, üç 🟢.** Hepsi kapandı veya devredildi.

| # | Sev. | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `llms-full.txt` kaynağından geride: agent haritası `packages.md`'nin **son** düzenlemesinden ÖNCE üretilmişti; `build-agent-map.mjs --check` ve `check:content` kırmızıydı | **Düzeltildi** — `build-agent-map.mjs` yeniden koşuldu, dört site kapısı yeşil. 🚨 Ders: harita üretimi doküman düzenlemelerinin **sonuncusu** olmalıdır, ortası değil |
| 2 | 🟡 | Yeni dışa açılan `readSse`'nin HTTP sınırında hiç testi yoktu; iki sayfanın "`parseAs: 'stream'` + `readSse`" vaadini hiçbir test kanıtlamıyordu (`sse.test.ts` yalnız çözücüyü test ediyor) | **Düzeltildi** — `packages/tracon-client/test/streaming.test.ts` (3 test): sahte `fetch` ile gerçek `client.POST(..., parseAs: 'stream')` → `readSse`; parçalanmış çerçeve; ve **403'ün hâlâ `TraconError` fırlattığı** (akış modu hata ara yazılımını atlatmıyor) |
| 3 | 🟡 | `guides/openai-api.md`'deki TypeScript örneği çalışmıyordu: `client` tanımsızdı | **Düzeltildi** — `createTraconClient(...)` çağrısı örneğe eklendi |
| 4 | 🟡 | Taşınan `src/lib/sse.ts`'e beş bayat referans kaldı; `kapi.py tarama` ve `dokuman-bakim.py` ikisi de temiz döndü (dizin öneki taşımayan yol iddiası kapıların deliğinden geçiyor) | **Düzeltildi** — beşi de `@tracon/client`'ı gösteriyor. `docs/guvenlik-tarama/BULGULAR.md`'deki altıncı referans **bilerek** bırakıldı: tarihli bir bulgu kaydıdır, ledger geçmişi yeniden yazılmaz |
| 5 | 🟡 | `A_matching_content_type_passes_in_both_directions` hiçbir şey iddia etmiyordu (yalnız "patlamadı") | **Düzeltildi** — `Should.NotThrowAsync` ile niyet görünür |
| 6 | 🟡 | Fazın kendi listelediği beş sorudan **alt sistem hatası** (sunucu akış ortasında bağlantıyı keser) iki seviyede de test edilmemişti | **Düzeltildi** — `A_connection_that_drops_mid_stream_surfaces_the_error_rather_than_ending_quietly`: `FailingStream` okuma ortasında `IOException` atar; istisna `MoveNextAsync`'ten çıkar ve o ana kadarki çerçeveler teslim edilmiş olur. Sessizce bitmek "run tamamlandı" diye okunurdu |
| 7 | 🟡 | `YOL-HARITASI.md` fazı hâlâ `📋 Planlandı` gösteriyordu | **Düzeltildi** — üretildi (K-413: elle yazılmaz) |
| 8 | 🟢 | Python testinin `document()` yardımcısı geçici dosya bırakıyordu | **Düzeltildi** (kendi kodum; `TemporaryDirectory` bağlam yöneticisi) |
| 9 | 🟢 | `text/event-stream` yanıtının şeması hâlâ JSON şeklini ilan ediyor | **Devredildi** — `ADAYLAR.md` **F-226**. 🚨 Bu satır 2026-09-08'de **F-221** yazıyordu; o numara Faz 162'de başka bir kaleme de verildi ve çakışma 2026-09-13'te bu kalem yeniden numaralanarak çözüldü |
| 10 | 🟢 | Site ağırlık marjı %0,6'ya indi; kalite sözleşmesindeki taban kaydı bayat | **Kapandı** — bu satır 2026-09-08'de `ADAYLAR.md` **F-222** olarak devredilmişti; o numara Faz 163'te başka bir kaleme de verildi. 2026-09-13'te kalem **kapatıldı**: tavan K-756 ile 58 000 B'ye çıktı ve K-756 bir sonraki adımı zaten yazıyor ("tavan İKİNCİ kez yükseltilmez") |

**Denetçinin temiz bulduğu başlıklar:** 3.3 (test seviyesi) · 3.5 (imza-gövde
kayması) · 3.6 (plan dışı public API) · 3.7 (repo kuralları).

### Denetimin çürüttüğü bir varsayım

Denetçi izole bir probe koştu: `HttpClient.Timeout = 3s` ile
`ResponseHeadersRead` üzerinden **10 saniyelik** bir SSE akışı .NET 10'da
**kesilmiyor** ("COMPLETED normally after 10,1s"). Yani `AddTraconClient`'ın
varsayılan 100 sn `Timeout`'u uzun akışlar için bir tuzak **değildir** — bu faz
için ayrı bir timeout ayarı gerekmedi.

### Denetim sonrası kapı koşumu

🔴 kapandıktan sonra kapılar yeniden koşuldu (düzeltme yeni kusur üretebilir):
`Tracon.Client.UnitTests` 18/18 · `@tracon/client` 34/34 ·
`python -m unittest discover -s scripts` 257/257 · `build-agent-map --check` ✅ ·
`npm run check` (dördü) ✅.

## Sonraki Faza Devir Notu

- **`nswag-postprocess-client.py` artık İKİ argüman alır** —
  `<uretilen.cs> <openapi-document.json>`. Zorunludur, varsayılanı yoktur:
  unutulan yol sessizce kardeş üretmemek yerine yüksek sesle düşer. Reçetenin
  yazılı olduğu üç yer güncellendi (`ClientCoverageTests` mesajı,
  `docs/hafiza/nswag-istemci-uretimi.md`, bu doküman).
- **Geçiş sırası artık anlamlıdır ve `main` onu iddia eder.** Altıncı geçiş
  dördüncüden önce koşar; yeni bir geçiş eklerken bu iddiayı bozma.
- **Script hâlâ idempotent değil**, ama altıncı geçiş bunu artık kendisi
  yakalar (`SystemExit`, CS0111 beklemeden).
- **`readSse` artık `@tracon/client`'ta yaşıyor.** Frontend onu paketten
  alır ve import **`dist/`'i** çözer — K-627'nin dördüncü adımı (`npm run build`)
  bu yüzden hâlâ zorunludur.
- **Açık kalan:** `text/event-stream` içerik tipinin **şeması** hâlâ JSON
  şeklini ilan ediyor (`ChatCompletion`/`JsonElement`); saf-SSE uçlar doğru
  biçimde `type: string` diyor. ASP.NET Core'un üstveri modeli aynı statü kodu
  için iki farklı şema ifade edemiyor (kaynaktaki yorum bunu zaten kabul
  ediyor) ve K-039 gereği kütüphane `Microsoft.AspNetCore.OpenApi`'ye bağımlı
  değil, yani bir `OpenApiOperationTransformer` de kütüphanede yaşayamaz.
  Üretilen istemci bundan **etkilenmiyor** (altıncı geçiş içerik tipinin
  varlığına bakar, şemasına değil), ama belgeyi okuyan bir üçüncü taraf üreteci
  yanlış bilgilenir. Aday olarak yazılmaya değer.
- **Örnek uygulamada bağlantısız bir gürültü var:** açılışta
  `SqliteException: no such table: tracon_agent_definitions` loglanıyor —
  katalog, migration'lar tamamlanmadan listeliyor. Zararsız (migration hemen
  ardından koşuyor ve uygulama çalışıyor) ve bu fazın kapsamı dışında; ayrı bir
  kusur kalemi olarak açılmalı.
