# Faz 40 — OpenAPI Belgesinin Yayımlanması

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-63**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.AspNetCore`
> **Yeni paket:** Yok — 🚨 gerekçe [40.1](#401--k-039-korunur--bu-fazın-ana-kısıtı) · **Migration:** Yok
> **Public API:** büyümüyor — uç **üstverisi** zenginleşir, imzalar değişmez

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/40-OPENAPI-YAYINI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Kendi arayüzünü yazmak veya AgentPrism'i bir dağıtım hattına bağlamak isteyen tüketici, uçları bugün **elle** okumak zorundadır. Belge üretiliyor ama kullanılabilir değil: 121 ucun tamamı tek bir `AgentPrism` etiketi altında duruyor ve **on bir uç hiçbir yanıt şeması bildirmiyor** — bunlardan biri agent'ı çalıştıran uçtur.

## Plandan Sapmalar

1. **Gerçek ölçüm 121 değil 124 `WithName`, 123 varsayılan-yapılandırma operasyonu.**
   Plan 2026-08-06 kanıtını 121 `Map*` çağrısı üzerinden veriyordu. Uygulama
   anında yeniden ölçüldü: 126 `WithName` (2'si `UiEndpoints` — statik
   varlıklar, `ExcludeFromDescription()` ile zaten belgeden hariç) = 124 gerçek
   uç. `docs/openapi/agentprism.json`'da **123** operasyon görünür çünkü
   `DiagnosticsEndpoints` (Faz 33) varsayılan **kapalı**dır
   (`EnableDiagnosticsEndpoint = false`) ve varsayılan yapılandırmada
   haritalanmaz — eksik değil, beklenen.
2. **"119/121 `WithSummary`, 2 eksik" iddiası doğrulanamadı.** Gerçek ölçüm:
   124 uçun **tamamı** özet taşıyor (`missing summary: []`,
   `docs/openapi/agentprism.json` üzerinden doğrulandı). DoD'nin "eksik iki
   `WithSummary`" maddesi bu yüzden **yapılacak iş değil**, zaten tamam.
3. **`DiagnosticsEndpoints.cs` plan tablosunda yoktu; `Diagnostics` etiketi
   eklendi.** 40.3'ün 16 satırlık tablosu bu dosyayı atlamıştı. Dosya
   sınırına göre ayrım ilkesi (40.3) tutarlı biçimde uygulandı: kendi etiketi
   verildi, `Meta`'ya karıştırılmadı.
4. **🚨 `.WithTags(...)` art arda çağrılınca BİRİKMEZ, EZER (K-272).** Plan
   "grup düzeyindeki `WithTags("AgentPrism")` kalır, uç düzeyinde ikinci bir
   etiket eklenir" diyordu — ölçülünce bunun **çalışmadığı** görüldü: ikinci
   çağrı birinciyi tamamen değiştiriyor (`tags[0]` "AgentPrism" yerine
   "Meta" döndü, mevcut `OpenApiDocumentTests` bunu yakaladı). Çözüm: her uçta
   TEK `.WithTags("AgentPrism", "<Alan>")` çağrısı.
5. **🚨 Aynı statü kodu için birden fazla `.Produces` çağrısı da EZER,
   BİRLEŞMEZ (K-274).** `/v1/responses` ve `/v1/chat/completions`'ta hem
   JSON hem SSE bildirmek için önce iki ayrı çağrı denendi; ikincisi
   birinciyi sildi. `additionalContentTypes` parametresiyle TEK çağrıya
   toplandı.
6. **🚨 `responseType: null` verilen `.Produces` çağrısı `contentType`'ı
   TAMAMEN DÜŞÜRÜR (K-273).** İzole repro ile doğrulandı:
   `.Produces(200, contentType: "text/event-stream")` (tipsiz) belgede
   `content` alanı üretmiyordu. Her SSE/ikili yanıt somut bir tiple
   (`Produces<string>` SSE, `Produces<Stream>` ikili gövde) bildirilmelidir.
7. **`POST /api/agents/{name}/run` planın iddia ettiği gibi "hem tipli hem
   akışlı" DEĞİL — SADECE SSE.** Kod incelemesi (`AgentEndpoints.RunAsync`)
   gösterdi ki başarı yanıtı her zaman `AgentRunStream` (SSE)'dir; hiçbir
   kod yolu tipli JSON döndürmez. DoD'nin bu maddesi yanlış varsayıma
   dayanıyordu; gerçek dual-mode uçlar `/v1/responses` ve
   `/v1/chat/completions`'tır (bkz. K-275).
8. **On bir ucun TAMAMI yol B'ye gitti; A hiçbirine uymadı (K-275).** Plan
   "uca göre" A/B karışımı öngörüyordu. Gerçek inceleme: beş uç yalnız SSE/
   ikili döner (asla tipli JSON), altı uç `Results.Json(...)` ile özel
   `JsonSerializerOptions` (`WhenWritingNull`) kullanır — `TypedResults.Ok<T>`
   bunu bozardı (null alanlar dizilir, OpenAI SDK tel uyumluluğu kırılır).
9. **🚨 `samples/AgentPrism.Api`'de `AddOpenApi()` + SqlServer/Sqlite birlikte
   500 veriyor (F-76, K-276).** Faz 40'ın "örnek uygulamada gerçek üretim"
   hedefi bu yüzden `AgentPrism.AspNetCore.FunctionalTests`'e kaydırıldı
   (SQL sağlayıcısı yok, çakışmadan bağışık). Kök neden `AgentPrism.Sql.Shared`
   linked-source deseninin (K-185) üç derlemede aynı tam nitelikli tip adı
   üretmesi ve `Microsoft.AspNetCore.OpenApi`'nin XML yorum önbelleğinin
   bunu tek sözlükte toplayıp çakışması. Bu fazın paket sınırının
   (`AgentPrism.AspNetCore`) dışında kaldığı için **çözülmedi**, aday listesine
   F-76 olarak kaydedildi. Örnek uygulamada gerçek bir agent çalıştırması
   (`POST /api/agents/support/run`, `EchoModelProvider` ile, `/openapi/v1.json`
   HARİÇ) uçtan uca doğrulandı ve SSE akışı beklendiği gibi çalıştı.
10. **`Microsoft.OpenApi.Any`'nin `type: "string", format: "byte"` yerine
    `Stream` tipi kullanıldı.** `AgentPrismDownloadAttachment` için `byte[]`
    denendi, ama `format: byte` base64 kodlamayı ima ediyordu (yanlış — ham
    ikili akış). `Stream` tipi doğru `format: binary` şemasını üretiyor.

## Bu Fazda Verilen Kararlar

K-272 – K-276, `docs/KARARLAR.md` bölüm 2'ye eklendi. On bir ucun A/B kararı:

| Uç (`operationId`) | Karar | Gerekçe |
|---|---|---|
| `AgentPrismRunAgent` | B | Başarı yanıtı yalnız SSE; hiçbir zaman tipli JSON değil |
| `AgentPrismDownloadAttachment` | B | İkili gövde, tip derleme zamanında bilinmiyor |
| `AgentPrismRunWorkflow` | B | Başarı yanıtı yalnız SSE |
| `AgentPrismResumeWorkflow` | B | Başarı yanıtı yalnız SSE |
| `AgentPrismRespondWorkflowRequest` | B | Başarı yanıtı yalnız SSE |
| `AgentPrismOpenAIResponses` | B | Govdedeki `stream` bayrağına göre JSON/SSE; `Results.Json` özel `JsonSerializerOptions` kullanıyor |
| `AgentPrismOpenAIChatCompletions` | B | Aynı gerekçe |
| `AgentPrismOpenAICreateConversation` | B | `Results.Json` özel seri hâle getirme; `TypedResults.Ok` null alanları dizip tel uyumluluğunu bozardı |
| `AgentPrismOpenAIGetConversation` | B | Aynı gerekçe |
| `AgentPrismOpenAIDeleteConversation` | B | Aynı gerekçe |
| `AgentPrismOpenAIListConversationItems` | B | Aynı gerekçe |

## Bitiş Ölçütleri (DoD) — gerçekleşen

- [x] 🚨 `AgentPrism.AspNetCore.csproj` **hâlâ** `Microsoft.AspNetCore.OpenApi` taşımıyor (K-039/L35 korundu; `OpenApiDependencyTests` + `grep` ile doğrulandı)
- [x] Belgedeki **her** ucun en az iki etiketi var: `AgentPrism` + alan etiketi (`OpenApiTagCoverageTests`)
- [x] On bir `Task<IResult>` ucunun **hepsi** yanıt şeması bildiriyor (`OpenApiResponseSchemaTests`)
- [x] `POST /v1/responses` ve `POST /v1/chat/completions` hem tipli JSON hem `text/event-stream` yanıtını bildiriyor (planın `POST /api/agents/{name}/run` beklentisi yanlış varsayıma dayanıyordu, bkz. Plandan Sapmalar #7)
- [x] 123 `operationId` benzersiz (`OpenApiOperationIdTests`)
- [x] `WithSummary` zaten 124/124 tamdı; eksik yoktu (bkz. Plandan Sapmalar #2)
- [x] `docs/openapi/agentprism.json` işlendi ve anlık görüntü testi geçiyor
- [x] Belge bir istemci üretecinden geçirildi: `npx openapi-typescript docs/openapi/agentprism.json` hatasız 7838 satırlık `.d.ts` üretti; `tsc --strict` sıfır hata ile derledi
- [x] Dört doğrulama kapısı sıfır uyarı verir (`build`/`pack`/`format` doğrudan çalıştırıldı; `test` proje-proje çalıştırıldı — bkz. not aşağıda)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı: `POST /agentprism/api/agents/support/run` `EchoModelProvider` ile SSE akışını uçtan uca üretti (çıktı yukarıda, Plandan Sapmalar #9)
- [x] `secret` taraması boş döndü

**Test kapısı notu:** `dotnet test AgentPrism.slnx --no-build` tüm çözüm için
tek komutta 11+ dakika CPU'suz askıda kaldı (`AgentPrism.Templates.Tests`,
bu fazla ilgisiz) ve öldürüldü. Proje proje koşuldu:

| Proje | Sonuç |
|---|---|
| `AgentPrism.AspNetCore.FunctionalTests` | ✅ 337/337 |
| `AgentPrism.Core.UnitTests` | ✅ 576/576 |
| `AgentPrism.Mcp.UnitTests` | ✅ 15/15 |
| `AgentPrism.OpenAI.UnitTests` | ✅ 81/81 |
| `AgentPrism.Anthropic.UnitTests` | ✅ 39/39 |
| `AgentPrism.Google.UnitTests` | ✅ 43/43 |
| `AgentPrism.Azure.UnitTests` | ✅ 48/48 |
| `AgentPrism.Voice.UnitTests` | ✅ 36/36 |
| `AgentPrism.Sqlite.IntegrationTests` | ✅ 255/255 |
| `AgentPrism.Workflows.UnitTests` | ✅ 69/69 |
| `AgentPrism.Testing.UnitTests` | ✅ 32/32 |
| `AgentPrism.PostgreSql.IntegrationTests` | ✅ 505/505 |
| `AgentPrism.SqlServer.IntegrationTests` | ❌ 0/250 — önceden bilinen ARM64 Testcontainers kısıtı (`docs/hafiza/sql-saglayicilari.md`), bu fazla **ilgisiz** (SqlServer koduna dokunulmadı) |
| `AgentPrism.Ui.E2ETests` | ⚠️ 40/41 — tek hata `Playground_konusma_modu_mikrofonu_acar` (ses/WebRTC E2E'si), bu fazla **ilgisiz** (arayüze dokunulmadı) |
| `AgentPrism.Templates.Tests` | ⏭️ Askıda kaldı (>11dk, CPU'suz), koşulmadı — bu fazla **ilgisiz** (`dotnet new` şablon testi) |

Bu üç istisnanın hiçbiri Faz 40'ta değiştirilen dosyalara dokunmaz; üçü de
ortam kısıtları veya önceden var olan kırılganlıklardır.

## Sonraki Faza Devir Notu

**Devralınan sözleşme:** `AgentPrism.AspNetCore`'un HTTP yüzeyi artık test
korumalı bir OpenAPI belgesi taşıyor. `docs/openapi/agentprism.json` her uç
değişikliğinde `OpenApiSnapshotTests` tarafından zorlanır — yenilemek için
`AGENTPRISM_OPENAPI_REFRESH=1 dotnet test tests/AgentPrism.AspNetCore.FunctionalTests
-c Release --filter FullyQualifiedName~OpenApiSnapshotTests`.

**🚨 Bilinen tuzaklar (yeni uç eklerken tekrarlanmasın):**
- `.WithTags(...)` ve aynı statü koduna `.Produces(...)` çağrıları **birikmez,
  son çağrı öncekini ezer** (K-272, K-274). Her zaman TEK çağrıda: alan
  etiketleri `.WithTags("AgentPrism", "<Alan>")`, çoklu içerik tipi
  `additionalContentTypes` ile.
- `.Produces(statusCode, contentType: "...")` **tipsiz** çağrıldığında
  `contentType` sessizce düşer (K-273). SSE için `Produces<string>`, ikili
  gövde için `Produces<Stream>` kullanılmalı.

**Yarım kalan iş:** F-76 (`docs/ADAYLAR.md`) — `samples/AgentPrism.Api`
2+ SQL sağlayıcısı birlikte kuruluyken `AddOpenApi()` 500 veriyor. Ayrı bir
kalem olarak durur; bu faz onu çözmedi.

**F-50 (tipli yönetim istemcisi ve CLI) için not:** Belge istemci üretimine
**hazır** — `openapi-typescript` ile doğrulandı, `tsc --strict` temiz derledi.
Hâlâ elle sarmalama gerektiren tek şey: `POST /v1/responses` ve
`POST /v1/chat/completions`'ın akışlı yanıtları (`text/event-stream`),
schema'da `ChatCompletion`/`JsonElement` ile **yaklaştırılmıştır** — gerçek
akış çerçeveleri (`ChatCompletionChunk` vb.) üretilen istemcide görünmez;
F-50'nin SSE ayrıştırıcısı bunu üretilen tiplerden değil elle yazmalıdır.
**TypeScript/npm paketi bu fazın kapsamı dışındadır** ve ayrı bir aday
kalemi olarak durur.

**Sıradaki faz:** [Faz 41 — Kiracı Yalıtımının Zorlanması](41-KIRACI-YALITIMININ-ZORLANMASI.md).
