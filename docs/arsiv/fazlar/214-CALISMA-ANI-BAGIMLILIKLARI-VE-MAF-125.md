# Faz 214 — Çalışma Anı Bağımlılıkları ve MAF 1.25

> **Durum:** ✅ Tamamlandı (2026-10-16)
> **Plan onayı:** kullanıcı fazı `faza başla ve bitir` diyerek başlattı (2026-10-16); Açık Sorular 1-5 ayrıca yanıtlanmadı, **planın önerdiği seçenekle (A) uygulandı** — bkz. *Plandan Sapmalar* 1 ve *Bu Fazda Verilen Kararlar*. Kapsam kararı: **UR-009 B** (küçült → yükselt → modernize et), farukatasoy, 2026-10-16 ([`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §7).
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-557** (üç fazın birincisi: çalışma anı; [Faz 215](215-TEST-VE-DERLEME-ARACLARI.md) test ve derleme araçları, [Faz 216](../../216-NPM-BAGIMLILIKLARI.md) npm)
> **Genellik:** Her tüketici. Sevk edilen 20 paketin bağımlılık grafiği beş MAF minor sürümü geride (`1.20` → `1.25`); tüketici daha yeni MAF'ı kendi projesine alınca Tracon'un ikilisi `TypeLoadException` ile kırılır (A-59, ölçüldü). İş tüketici kodunda kalamaz. İkinci kanıt: ilk ilke (1.0 bu grafiği dondurur) ve bağımlılık botu PR'larının beş haftadır kırmızı kapanması (A-62).
> **Karşı görüş:** "Yükseltme kendi başına değer üretmez; ön sürüm Hosting her minor'da kırar, freeze'e yakın yükseltmek risk alır." **Cevap:** Freeze'den sonra yükseltme daha pahalıdır: zorunlu tip değişikliği (`AgentSessionStore` taşındı) 1.x içinde major ister. Deneme yükseltmesi (izole worktree, 2026-10-16) derlemeyi 6 dosyada yeşile çevirdi; kırılmalar bilinir ve aşağıda tek tek planlıdır. **Bu faz davranış değiştirmez:** upstream'in getirdiği davranış farkı ya korunur (eski davranış açıkça istenir) ya da Açık Soru olarak karara bağlanır.
> **Önkoşul:** [Faz 212](212-HTTP-DTO-DARALTMA.md), [Faz 213](213-TESISAT-ARAYUZLERI-VE-UYE-HIJYENI.md) (kapandı). Faz 215 ve 216 bu fazdan **sonra** gelir; üçü de `Directory.Packages.props`, `dependabot.yml` ve `CHANGELOG.md` değiştirir, paralel koşmaz.
> **Paketler:** `Tracon.AspNetCore`, `.Core`, `.Anthropic`, `.Google`, `.Azure`, `.OpenAI`, `.Sqlite`, `.SqlServer` (+ `Directory.Packages.props`, `.github/dependabot.yml`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Değişiyor.** `TraconAgentSessionStore`'un taban tipi ve üç override'ı upstream yüzünden zorunlu değişir; Açık Soru 1'in önerisiyle tip `internal` olur (küçülür). `PublicAPI.Shipped.txt` bugün **0** satırdır (`find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'` → `0`); değişiklik `1.0.0`'dan önce bedavadır.
> **Tüketici yüzeyi:** site: `reference/compatibility.md:134-148` (test edilen sürümler: `1.20.0`/`10.9.0` → `1.25.0`/`10.11.0`) · sevk edilen: `src/Tracon.Anthropic/README.md` (thinking satırları, Açık Soru 3'e bağlı), `TraconAgentSessionStore` XML'i, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** [`manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) (paketlenmiş tüketici) · `06-SAGLAYICI-DIGER.md` (Anthropic) · `08-OPENAI-UYUMLU-UCLAR.md` (Responses oturumu) · `18-MCP-VE-A2A.md` (A2A)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show d8a0ee46:docs/arsiv/fazlar/214-CALISMA-ANI-BAGIMLILIKLARI-VE-MAF-125.md
> ```
>
> Damıtıldı 2026-10-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Sevk edilen paketlerin çalışma anı grafiğini en yeni sürümlere taşı; upstream'in zorladığı tip değişikliğini 1.0'dan önce yap; artık gereksiz CVE sabitlerini kaldır; botun bu kümeyi bir arada yükseltmesini sağla.

## Bitiş Ölçütleri (DoD)

- [x] `Directory.Packages.props` §214.1 hedeflerinde (flat container ile yeniden ölçüldü, plan sayılarıyla **aynı**: MAF 1.25.0, Hosting `[1.25.0-preview.261014.1]`, A2A `[1.0.0-preview3]`, MEAI 10.11.0, OpenAI 2.14.0, platform 10.0.13, Anthropic 12.58.0, Google.GenAI 1.26.1, Azure.Core 1.63.0, SqlClient 7.1.1, Tokenizers 2.0.1); `dotnet restore Tracon.slnx` uyarısız; `dotnet list Tracon.slnx package --vulnerable --include-transitive` → 47 proje "has no vulnerable packages", açık yok
- [x] §214.2'deki dört sabit grubu kaldırıldı (`Bcl.Memory`, `SQLitePCLRaw.*`, `Microsoft.OpenApi`, `OpenTelemetry.*`); `Pkcs` ×3 kaldı, **yeniden ölçüldü** (boş proje + `NuGetAudit`, SqlClient 7.1.1: net8.0 8.0.1, net9.0/net10.0 9.0.18, `NU1902` + `NU1903` sürüyor) ve `Directory.Packages.props` yorumu 2026-10-16 ile güncellendi
- [x] `uyum-probu.cs -- ileri --tuketici artifacts/package/yayin/1.0.0-preview.5.214/Tracon*.nupkg`: 1.237 üye, **0 eksik tip, 0 eksik üye, 0 çözülemeyen**; negatif kontrol (`Microsoft.Agents.AI(.Abstractions)@1.13.0`) 272 üyede **3 eksik tip + 2 eksik üye** raporladı (`AgentSessionStore`, `AgentSessionStoreKey`, `ToolAutoApprovalRuleContext`, …)
- [x] `uyum-probu.cs -- deneysel`: `public_maruziyet=4`, dördü `AddLoopEvaluator` (F-560); `TraconAgentSessionStore` listede yok
- [x] `SessionStoreIsolationTests` (8) ve `A2ARunModeTests` (1) yeşil. Mutasyon denetimi: bölümü yok sayan köprü 5 testi, `GetSessionAsync`'i oluşturan köprü 1 testi, `AgentRunMode.ReturnTask` 1 testi kırdı; `register.AgentRunMode` satırını silmek **hiçbir testi kırmaz** (MAF varsayılanı zaten mesajdır) — test sonucu sabitler, satırı değil
- [x] Anthropic `System_placement_*` (2 birim) ve `AnthropicRequestSettingsEndpointTests…one_hour_cache` yeşil; `ReasoningEffort_alone_becomes_an_enabled_thinking_budget` (3) yeşil — Açık Soru 3 = A (`Extended`)
- [x] Anlık görüntü yenilendi: `docs/openapi/tracon.json` **+12 satır**, hepsi `"examples":[null]` ×4 şema; `packages/tracon-client/src/schema.ts` +7/−1 (`@example null`); `Tracon.Client` üretilen dosyası **değişmedi**
- [x] `dependabot.yml`: tek `agent-stack` grubu; `compatibility.md` 1.25.0/10.11.0; `///` satırlarındaki 16 sürüm damgası ve 7 test yorumu yeniden ölçüldü (tablo: *Plandan Sapmalar* 5) — `dokuman-bakim.py` "Bağımlılık sürüm damgası" kapısı temiz
- [x] `CHANGELOG.md` `[Unreleased]`: Changed (bağımlılıklar), Removed (`TraconAgentSessionStore` adıyla), Fixed (Anthropic system yeri); `kapi.py yayin --kuru --surum 1.0.0-preview.5.214` EXIT 0 (altı dış sample + AOT smoke + net8 tüketici)
- [x] Dört doğrulama kapısı sıfır uyarı: `python3 scripts/kapi.py kapanis --taban 53249027` **EXIT 0** (2026-10-16, denetim bulguları kapandıktan sonra, commit `HEAD` temiz ağaçta): 10/10 komut ✅ — `build` 64,5 s · `test` 582,7 s, **38 koşumda 23.370 test, 0 kırmızı** · `pack` 6,5 s · `format --verify-no-changes` 107,2 s · `docs-site npm run check` 32,0 s. Ayrıca `kapi.py yayin --kuru --surum 1.0.0-preview.5.214` EXIT 0 ve `uyum-probu` (ileri/deneysel) bu kapının DIŞINDA koşuldu
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (*Örnek Uygulama Koşumu*)
- [x] `secret` taraması boş: `kapi.py tarama` "temiz (51 işaretli sentetik credential atlandı)"
- [x] Manuel kabul case'leri dört dosyaya eklendi: `MT-PKG-166`, `MT-PROV-090`, `MT-COMPAT-055`, `MT-COMPAT-056`, `MT-MCP-085`; otomatikleştirilebilenler (`MT-COMPAT-055`, `MT-MCP-085`) örnek uygulamada koşuldu; kalanı ➜ CI (gerçek Anthropic anahtarı, tüketici kaydı, yayın provası)
- [x] `faz-denetim` koşuldu — *Denetim Bulguları*
- [x] Yeni `K-*`: K-1016 (köprü görünürlüğü, bölüm anlamı, oluşturmama sözleşmesi, `MAAI001`), K-1017 (Anthropic istek davranışı); K-026/K-008 gerekçe notu, K-197/K-448 kapanış notu, K-211 yeniden ölçüm notu, K-543 grup notu

### Doğrulama komutları

```bash
dotnet restore Tracon.slnx && dotnet list Tracon.slnx package --vulnerable --include-transitive
dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- deneysel
python3 scripts/kapi.py test --proje Tracon.Anthropic.UnitTests --sinif "*AnthropicRequestSettingsTests*" --tfm net10.0
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.5.214
```

---

## Plandan Sapmalar

1. **Açık Sorular 1-5 kullanıcıya sorulmadı; planın önerdiği seçenekle (A) uygulandı.** Kullanıcı fazı "başla ve bitir" diye açtı ve plan metnini (önerileriyle) görmüştü; AGENTS.md "her belirsizliği sor" der — bu bir **sapmadır** ve kullanıcının geri alma hakkı saklıdır. Beş seçimin tümü K-1016/K-1017'de yazılı ve her biri tek commit'te geri alınabilir.
2. **Plan "MapTracon'da `TryAddSingleton<AgentSessionStore>`" dedi; bu yapılamaz.** `MapTracon` kurulmuş bir kapsayıcıyla çalışır, `Tracon.AspNetCore`'un servis kaydı yapan bir yöntemi yoktur. `ResolveSessionStore` aynı kaldı (tüketicinin kaydı kazanır, yoksa `internal` köprü). **Sonuç planın söylemediği bir şey:** tüketici köprüyü artık sarmalayamaz (tip adlandırılamaz). MAF yalıtımı isteyen kendi `AgentSessionStore`'unu verir ve bu `/v1/responses` için Tracon deposunun yerine geçer. Sarmalama ihtiyacı kanıtlanırsa DI'da adlandırılmış bir kayıt tasarlanır (K-1016 yeniden açılma koşulu). Hiçbir rehber, sample veya sözleşme paketi köprüyü kurmuyordu (`public-yuzey-gerekceleri.tsv` satırı yalnız tipin kendi XML'ini gerekçe gösteriyordu).
3. **`PublicAPI.Unshipped.txt`'ten 5 satır düştü, plan 6 dedi.** Tip, kurucu ve üç override; altıncı satır yoktu. Ek olarak `public-constructor-baseline.txt` (−1), `public-yuzey-gerekceleri.tsv` (−1) ve `public-surface-baseline.txt` (`Tracon.AspNetCore` 22 → 21) küçüldü.
4. **Plan "docs-site'ta bu iki adı anan izlenen sayfa yok" dedi — doğru, ama üretilen `docs-site/src/content/docs/api/` sayfaları vardı.** Bu dizin `.gitignore`'dadır (üretilir); tazelemek gerekmez.
5. **Sürüm damgası kapısı planın saydığı 10 yorumdan fazlasını buldu: 16 `///` bulgusu + 7 test yorumu.** Her biri yeniden ölçüldü ya da tarihlendi:

   | Yer | İddia | Yeniden ölçüm (2026-10-16) | Sonuç |
   |---|---|---|---|
   | `AnthropicStopReasonChatClient` ×2 | Adaptör yalnız `refusal`/`max_tokens`/`tool_use` eşler; aggregator server tool'un `input_json_delta`'sında atar | 12.58.0 decompile: `model_context_window_exceeded` → **Length**, `pause_turn` kendi adıyla geçer; aggregator `StreamedToolInput` ile birleştirir | **İddia bayattı** — yorum yeniden yazıldı; kod davranışı raw temsilden okuduğu için değişmedi |
   | `TraconA2AOptions` | `AddA2AServer` kayıt anıdır, yalnız adı verilen agent'ı yayımlar | 1.25 decompile: beş aşırı yükleme, hepsi açık agent ister | doğru |
   | `OpenAIResponsesEndpoints` ×2 | `IConversationStorage`/`IResponsesService`/`IAgentConversationIndex` `internal`; `ToAgentRunRequest` `function_call_output` kalemini `ArgumentException` ile reddeder | decompile + probe (`ToAgentRunRequest` dört gövdeyle) | doğru |
   | `RecordingLoopEvaluator` ×2 | Loop ilk "devam" diyen evaluator'da durur; `MaxIterations` turunda evaluator'a bakmaz | `LoopAgent` 1.25 decompile (`EvaluateAndBuildNextAsync`, `iteration >= _maxIterations`) | doğru |
   | `AggregatedResponseGuard` | `LoopAgent` toplamı son yanıta yerinde yazar; `OpenTelemetryAgent` raw temsili tercih eder | 1.25 decompile (`ApplyAggregatedUsage`, `RawRepresentation`) | doğru |
   | `StructuredResponseValidatingAgent`, `FinishReasonDetectingChatClient` | MEAI son turda fonksiyon tool'larını çıkarır; akışta ara tur güncellemeleri sırayla gelir | MEAI 10.11.0 `PrepareOptionsForLastIteration` decompile; `OutputTruncation*Tests` yeşil | doğru |
   | `UsageBreakdown` | `InputAudio`/`OutputAudio` deneysel, `CachedInput`/`Reasoning` değil | 10.11.0 decompile: aynı; **ek olarak** `InputText`/`OutputText` da deneysel (okunmuyor) | doğru + not |
   | `OpenAIChatClientFactory` | Görüntü yüzeyi MEAI'de deneysel | `IImageGenerator` hâlâ `[Experimental("MEAI001")]` | doğru |
   | `WorkflowDefinitionValidator` ×2 | MAF eşleşmeyen mesajı sessizce düşürür; ilk düğüm `object` alırsa `TurnToken` ikinci girdi olur | `WorkflowNodeTypeChainTests`, `WorkflowEntryFunctionRunsOnceTests`, `WorkflowNodeTypeRuleTests` (TurnToken tabanı) yeşil | doğru |
   | `FallbackChatClient` + test | OpenAI istemcisi dinlenmeyen porta `AggregateException("Retry failed after 4 tries")` → `ClientResultException` atar | **probe**, OpenAI 2.14.0, gerçek istemci | doğru |
   | `TraconSqlServerOptions` | SqlClient `DbDataSource` sunmaz | 7.1.1: yalnız `SqlDataSourceEnumerator` var | doğru |
   | K-211 (`Directory.Packages.props`) | Azure uzantıları OpenAI'ye karşı `MissingMethodException` | **probe**, OpenAI 2.14.0: `AddDataSource` aynı eksik `get_SerializedAdditionalRawData` ile atar; sohbet/akış çalışır | doğru |
6. **Anthropic: plandaki iki farkın dışında üçüncü bir upstream değişikliği çıktı** — 12.58 `model_context_window_exceeded`'ı artık `Length` yapıyor. Tracon `TraconFinishReasons.ContextWindowExceeded`'i ham stop reason'dan kendisi üretir; test yeşil kalır, ama açıklama yanlış kalırdı. Aggregator artık server tool'u birleştirdiği için Tracon'un kendi yeniden kurma kodunun gerekçesi kısmen düştü; kod **bilerek** kaldı (yükseltme istek değiştirmez) ve sadeleştirme adayı olarak yoruma yazıldı.
7. **`AgentRunMode` satırı davranış değiştirmiyor.** MAF varsayılanı zaten mesajdır (satırı silmek hiçbir testi kırmadı). Satır açık kalır; `A2ARunModeTests` sonucu sabitler.
8. **Plan "Azure istek yolu testi eklenir" dedi; embedding yok.** `Tracon.Azure` embedding istemcisi sunmaz; `AzureOpenAIChatRequestWireTests` sohbet ve akışı gerçek bir loopback sokete gönderir (3 TFM'de yeşil).
9. **`dependabot.yml`: planda olmayan iki satır daha silindi** — sabitleri kalkan `Microsoft.OpenApi` ve `SQLitePCLRaw.*` `ignore` kalemleri (K-543 koşulu).
10. **Taban dosyaları tek yönde değişti (yalnız küçüldü):** `transitive-dependencies.txt` −9 satır (3 paket × 3 bölüm), `public-surface-baseline.txt` −1, `public-constructor-baseline.txt` −1. `SourceLanguageTests` tabanı **değişmedi** (kapı yeni "Faz" geçen satırı yakaladı; "Phase" yazıldı).
11. **`WorkflowResponseOwnershipTests.A_client_that_goes_away…` bu koşumda düşmedi** (planın uyarısı: denemede bir kez düşmüştü); tam koşumda 2.139 test yeşil, izole koşum gerekmedi.

## Bu Fazda Verilen Kararlar

Dört kategoriye girenler `docs/KARARLAR.md`'de: **K-1016** (`TraconAgentSessionStore` `internal`, oluşturmama sözleşmesi, `isolation` bölümü katlanır, başka bölüm reddedilir, `MAAI001` bastırması) ve **K-1017** (Anthropic: `Extended` düşünme, talimat bloğu yerleşimi). K-008, K-026, K-197, K-211, K-448 ve K-543'e not düşüldü.

Yerel tercihler (K açılmaz):

- **Açık Soru 4 = A:** `"examples":[null]` upstream çıktısıdır; anlık görüntü, `schema.ts` ve zincir olduğu gibi yenilendi, dönüştürücü eklenmedi.
- **`AgentSessionManager.GetExistingSessionAsync` adı ve `internal`liği:** `TryGet…` yerine "var olanı getir" anlamı; `GetOrCreateSessionAsync`'in geri yükleme yolunu paylaşır (`RestoreSessionAsync`), böylece nesil izi (`_restoredVersions`) ve şema sürümü reddi iki yolda aynıdır.
- **`Microsoft.Bcl.Memory` için sabit değil çıkarım:** O200kBase 2.0.1 `net8.0` grubunu taşıyor; sabit eklemek yerine graf tabanı küçüldü.
- **Anthropic README değişmedi:** `ReasoningEffort` → bütçe satırı `Extended` ile doğru kalır.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, Production, bellek içi depo, `echo` sağlayıcısı (`dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll --contentRoot … --urls http://127.0.0.1:5299`). İkinci koşum varsayılan dışı: `Tracon__Observability__SuccessSampleRatio=1`.

| Case | Girdi | Gerçek çıktı |
|---|---|---|
| `MT-COMPAT-055` | `POST /tracon/v1/responses` `{"model":"summarizer","input":"first turn"}` | `resp_ZmVoSWhc… completed` · `Echo: first turn` |
| | aynı uç, `previous_response_id=<ilk id>` | `resp_bXYChJLr… completed` · `Echo: second turn` |
| | `GET /tracon/api/sessions` | `['resp_bXYChJLr…', 'resp_ZmVoSWhc…']` — kimlik önekli kayıt yok |
| | `conversation:"conv-214"` iki kez | `200 200`; oturum listesinde `conv-214` **bir** kez |
| `MT-MCP-085` | `POST /tracon/a2a/summarizer` JSON-RPC `SendMessage` (`ROLE_USER`, `messageId` dolu) | `result` anahtarları `['message']`; `{"message":{"role":"ROLE_AGENT","parts":[{"text":"Echo: hello "}],…}}` — `task` yok |
| varsayılan dışı | `SuccessSampleRatio=1`, Responses + A2A | `completed` · `['message']`; günlükte hata satırı yok (eşleşenler `ProviderFailureNormalizingChatClient` adı) |

`MT-PKG-166`, `MT-PROV-090`, `MT-COMPAT-056` örnek uygulamada koşulamaz: yayın provası beslemesi (`kapi.py yayin --kuru` EXIT 0), gerçek Anthropic anahtarı ve tüketici kaydı gerekir → ➜ CI (`AnthropicRequestSettings*`, `SessionStoreIsolationTests`).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 4 kapı kırmızısı turu: (1) tam koşumda 6 test — 3 taban (graf, tip sayısı) bilerek küçüldü, 1 anlık görüntü yenilendi, `Faz` sözcüğü dil kapısını kırdı; (2) `dokuman-bakim` 16 damga bulgusu; (3) manuel case sayacı; (4) üretilen `llms-full.txt` bayat. Denetim turu ayrıca: 5 🟡 kapatıldı (2 yeni test, 3 yeniden damgalama, 1 hafıza satırı, 1 CHANGELOG cümlesi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (🟡 5 → hepsi kapandı, 🟢 4 → 2'si aday) |
| Fazın ürettiği regresyon | 0 bilinen (kapı dışı bulunan yok) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taze bağlam, salt-okunur; `git diff 53249027..HEAD`, 55 dosya, 137 araç çağrısı, MAF/Anthropic/MEAI decompile'ı dahil). Sonuç: **🔴 yok** (denetçi önerisi tablosu boş) — Adım 5.1 triyajı bu yüzden uygulanmadı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Akışlı Responses `SaveSessionAsync` (`OpenAIResponsesEndpoints.cs:506`) ve `previous_response_id` zincirinin ikinci turda oturumu **yüklediği** hiçbir testte yok; mevcut zincir testi yalnız ilk kaydın varlığına bakıyor | **Düzeltildi** — `OpenAICompatTests.Responses_second_turn_loads_the_session_the_first_turn_saved` (ikinci kaydın durumu iki turu da taşır, birincisi yalnız kendi turunu) ve `Responses_streaming_saves_the_session_under_the_conversation_id`. Mutasyon: yükleme kimliğini kayıt kimliğiyle değiştirmek birinciyi, akışlı kaydı yanlış kimliğe yazmak ikinciyi kırdı |
| 2 | 🟡 | Köprünün `isolation` katlama/ret mantığı ve `GetSessionAsync` üretimde hiçbir çağırandan çalışmıyor; ulaşılabilir sınır (host kendi store'unu verir) ne testte ne CHANGELOG'da. Üç kapı (`CheckRunSessionAsync`, `IsOwnedByTenantAsync`) çıplak kimliği Tracon deposunda arar, bulamayıp geçirir | **Gerekçelendi + belgelendi** — mantık MAF'ın `AgentSessionStore` sözleşmesini (bölümler kimliğin parçasıdır) doğru uygulamak ve K-1016 yeniden açılma koşuluna hazır durmak için ~20 satır olarak kalır; K-1016 ve CHANGELOG artık "host store'u Tracon'un kiracı süzgecini, sahiplik kapısını ve oturum API'sini devre dışı bırakır" der. Bu davranış Faz 214'ten önce de vardı (`ResolveSessionStore` aynı) |
| 3 | 🟡 | `src/Tracon.SqlServer/README.md:48` ve `guides/ef-core.md:44` "ölçüldü 7.0.2" diyor; kapı yalnız `src/**/*.cs` `///` satırlarını tarıyor | **Düzeltildi** — ikisi de 7.1.1'e yeniden damgalandı; `Microsoft.Data.Sqlite` 10.0.13 için de ölçüldü (`DbDataSource` yok) ve guide'a yazıldı |
| 4 | 🟡 | HEAD'de `llms-full.txt` bayat; DoD kapanış satırının sonucu yok | **Düzeltildi** — `node docs-site/scripts/build-agent-map.mjs` commit'e girdi; kapanış kapısı sonucu DoD'da |
| 5 | 🟡 | `docs/hafiza/openai-saglayici.md:29` Anthropic 12.39 davranışını gerçek diye anlatıyor (stop reason eşlemesi, aggregator, `ReasoningEffort`) ve iki yeni tuzak yok | **Düzeltildi** — satır 12.58'e göre yeniden yazıldı, iki tuzak eklendi (yalnız baştaki system mesajları `system`'e girer; `Extended` açıkça istenmeli); dosya 15.993 B (bütçe 16.000) |
| 6 | 🟢 | `examples:[null]` sözleşme gürültüsü; `evaluation.md:291` örneğinde `evaluatorVersion` 10.9.0 | **Devredildi** — `ADAYLAR.md` **F-570** |
| 7 | 🟢 | (Uygulayan oturumun notu, denetçi bulgusu değil) Anthropic yeniden kurma kodunun gerekçesi kısmen düştü | **Devredildi** — `ADAYLAR.md` **F-571** |
| 8 | 🟢 | K-197 notu yanlışlıkla K-1016'yı anıyordu; `MAAI001` bastırması iki değil üç yerde (`ResolveSessionStore`) | **Düzeltildi** — K-197 notu ve K-1016 metni |
| 9 | 🟢 | CHANGELOG "Fixed" başlığı: kusur yalnız yükseltilmiş durumda doğdu, yayınlanmış sürümde yoktu | **Gerekçelendi** — değişiklik notu "Unreleased"in müşterisine bir şey söylemiyor; başlık okuyucuyu yanıltmıyor, kullanıcı `Fixed` altında davranış düzeltmesini arar |

Denetçinin temiz bulduğu başlıklar: 3.1 (DoD kanıtları; kapanış sonucu hariç), 3.5, 3.6, 3.7; doğruladığı noktalar: kaçış biçimi 1.20 decompile'ıyla aynı, `GetExistingSessionAsync` hiçbir şey yazmıyor ve yeni-oturum işaretini kurmuyor, eski `GetOrCreate` sırası korundu, çağıranın mesaj listesi değişmiyor, taban dosyaları yalnız küçüldü, sevk edilen metinde iç referans yok.

## Sonraki Faza Devir Notu

**Sıradaki iş:** [Faz 215](215-TEST-VE-DERLEME-ARACLARI.md) (test ve derleme araçları: xunit.v3 4 + MTP 2, Meziantou, F-568), sonra [Faz 216](../../216-NPM-BAGIMLILIKLARI.md) (npm). Üçü `Directory.Packages.props`, `dependabot.yml` ve `CHANGELOG.md`'yi paylaşır, paralel koşmaz. Sonra F-558 (modernleşme: Azure için düz `OpenAI` + `/openai/v1/`, Anthropic'in `Adaptive` düşünmesi) ve F-560 (deneysel tip: `AddLoopEvaluator` ×4, `AgentFileStoreContract`).

**Başlangıç sayıları (Faz 215 bunları ön koşul olarak yazar):** `PublicAPI.Unshipped.txt` toplamı −5 (`Tracon.AspNetCore`) · `public-surface-baseline.txt` `Tracon.AspNetCore` `21` · `public-constructor-baseline.txt` 16 giriş · `transitive-dependencies.txt` üç bölümde −3 ad (`Microsoft.Bcl.{AsyncInterfaces,HashCode,Memory}`) · tam koşum 38 koşumda 23.362 test (+ Azure 6 + Responses 2 = **23.370**) · `uyum-probu -- deneysel` public maruziyet 4 · `uyum-probu -- ileri` 1.237 üye, 0 eksik · `ADAYLAR.md` F-ID sayacı F-571'den sonrasını gösterir.

**Devralınan sözleşmeler:**

- **K-1016** — `TraconAgentSessionStore` `internal`; `GetSessionAsync` `null` döner ve hiçbir şey yazmaz (`AgentSessionManager.GetExistingSessionAsync`, `internal`); anahtarın `isolation` bölümü `kaçışlı-anahtar::kimlik` olarak katlanır, başka her bölüm `NotSupportedException`. `ResolveSessionStore` değişmedi: tüketicinin `AgentSessionStore` kaydı kazanır. Tüketici köprüyü sarmalayamaz; kendi store'unu verirse Tracon'un kiracı süzgeci, sahiplik kapısı ve oturum API'si o konuşmaları görmez.
- **K-1017** — `AnthropicChatClientFactory.ExtendedThinking` sabiti SDK'nın `Adaptive` varsayılanını kapatır; önbellekli talimat bloğu ilk system-olmayan mesajdan önce eklenir. `Adaptive`'e geçmek F-558'in kararıdır.
- **Dependabot:** tek `agent-stack` grubu (MAF + `Microsoft.Extensions.*` + OpenAI + A2A + AspNetCore/Bcl/Data); `ignore` yalnız üç kalem (`CodeAnalysis.CSharp`, `TrxReport`, `xunit.v3`).
- **CVE sabitleri:** yalnız `System.Security.Cryptography.Pkcs` ×3 kaldı (yeniden ölçüldü, SqlClient 7.1.1). Yeni bir sabit eklemeden önce boş projede `NuGetAudit` ile ölç.

**🚨 Tuzaklar:**

- **`kapi.py test` derlemez.** Kaynağı değiştirip testi koşarsan bayat ikiliyi koşarsın ve "yeşil"/"kırmızı" yanlış çıkar. Önce `dotnet build <test projesi> -c Release --no-restore -p:TraconFrontendEnabled=false`. Mutasyon denetiminde de bu (üç kez yakalandı: derleme hatası sessiz kaldı, eski ikili koştu).
- **Sürüm damgası kapısı yalnız `src/**/*.cs` `///` satırlarını tarar ve bir satırdaki HER sürümü pinle karşılaştırır.** Tarihsel sürümü (`1.20.0'da…`) aynı satıra yazma; `SURUM_DAMGASI_TARIHSEL` ya da cümleyi yeniden kur. README ve `docs-site` taranmaz (denetçi `7.0.2` kalıntısını buldu): yükseltmede `grep -rn "<eski sürüm>" src/*/README.md docs-site/src` elle koş.
- **Dil kapısı `Faz` sözcüğünü Türkçe sayar.** İngilizce yorum ve test açıklamasında `Phase` yaz (4 satır kırdı).
- **Sabiti silmek tüketen `csproj`'u da kırar** (merkezî yönetimde sürümsüz `PackageReference`): `Microsoft.OpenApi` (FunctionalTests, `samples/Tracon.Api`), `SQLitePCLRaw.bundle_e_sqlite3` (`Tracon.Sqlite`), `Microsoft.Bcl.Memory` (`Tracon.Core`).
- **Tam koşum uzundur:** AspNetCore fonksiyonel ~9 dk, Package.Tests ~3 dk (paket üretir), SqlServer entegrasyon ~4,5 dk (Docker). `kapi.py kapanis` ilk kırmızıda durur; kırmızıları önce `dotnet test Tracon.slnx -c Release --no-build -maxcpucount:2` ile topla.
- **`kapi.py yayin --kuru` temiz ağaç ister.** Önce commit; yeni sürüm adı kullan (`1.0.0-preview.5.214` bu fazda kullanıldı, `artifacts/package/yayin/` altında durur).
- **Üretilen `docs-site/public/llms-full.txt`** compat sayfası ya da CHANGELOG değişince bayatlar: `node docs-site/scripts/build-agent-map.mjs`. `npm run check` bunu `check:content` içinde yakalar.
- **`AgentRunMode` varsayılanı zaten mesajdır.** `A2ARunModeTests` sonucu sabitler; `TraconA2ABuilderExtensions` satırını silmek testi kırmaz.
- **Upstream'in sessiz değişikliği:** SDK sürüm atlamalarında decompile yetmez; `model_context_window_exceeded` eşlemesi Tracon'un dışında değişti (testler yeşil kaldı, yorum yalan oldu). Davranış iddiası taşıyan `///` yorumlarını yükseltmede yeniden oku.

**Açık iş:** Faz 215, Faz 216, F-558, F-560; **F-570** (`examples:[null]` gürültüsü, `evaluation.md` örneği) ve **F-571** (Anthropic yeniden kurma kodu SDK aggregator'ına taşınabilir mi) bu fazda açıldı. Site yayını (`faz-tamamlama` Adım 10) bakımcı eylemidir: `./scripts/site-deploy.sh` **koşulmadı** — compat sayfası (1.25.0/10.11.0), CHANGELOG ve `llms-full.txt` canlıda bir önceki fazın sürümünü söylemeye devam eder.
