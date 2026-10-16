# Faz 212 — HTTP DTO'larını Daralt: Yalnız OpenAPI İçin Public Olan 81 Tip

> **Durum:** ✅ Tamamlandı (2026-10-16)
> **Plan onayı:** onaylandı — farukatasoy, 2026-10-16 ("planı uygula", `faz-uygulama` ile). Kapsam kararı: **UR-010 B**, farukatasoy, 2026-10-16 ([`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §7). Açık Soru 1 ve 2'de önerilen seçenek (A) uygulandı; kullanıcıya ayrıca sorulmadı, itiraz gelirse geri alınır.
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-554**
> **Genellik:** Her .NET tüketicisi. 1.0 sözü bugün 81 HTTP DTO'sunu da kapsar; .NET tüketicisi bunları kullanmaz (`Tracon.Client` kendi DTO'larını üretir). İş tüketici kodunda kalamaz: sözleşme bizim. İkinci kanıt: ilk ilke (donan yüzey küçüldükçe söz ucuzlar) ve [Faz 182](182-PUBLIC-API-YUZEY-DARALTMA.md) emsali (91 tip, aynı ölçüt).
> **Karşı görüş:** Faz 182 "OpenAPI şema tipi daraltılmaz" dedi: üretici yalnız public tipin XML'ini taşır, tip `internal` olunca `description` silinir. **Cevap:** Bu doğrudur ve ölçüldü; plan onu çözer, yok saymaz. `[Description]` aynı metni geri getirir (5 tip / 12 özellik denendi, fark yok). Bedeli: 393 metin tek yere taşınır. Kopya değil **taşıma** olduğu için ikinci kaynak doğmaz; kaybı görünür kılan yeni bir kapı eklenir (§212.4).
> **Önkoşul:** Yok. [Faz 213](../../213-TESISAT-ARAYUZLERI-VE-UYE-HIJYENI.md) bu fazdan **sonra** gelir (ikisi de `PublicAPI.Unshipped.txt` ve envanter betiğini değiştirir; paralel koşmaz).
> **Paketler:** `Tracon.Abstractions`, `Tracon.AspNetCore`, `Tracon.Core` (+ `scripts/`, `tests/`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Küçülüyor.** 81 tip ve 1 345 `PublicAPI.Unshipped.txt` satırı gider (AspNetCore 1 058 · Abstractions 258 · Core 29). `PublicAPI.Shipped.txt` bugün **0** satırdır: `find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'` → `0`. Daraltma `1.0.0`'dan önce bedavadır; sonra her biri major sürümdür.
> **Tüketici yüzeyi:** site: Yok (API referansında 81 üretilen sayfa kalkar; izlenen hiçbir sayfa onlara bağlanmaz). Sevk edilen: `CHANGELOG.md` `[Unreleased]` → **Removed**, 81 tipin her biri adıyla (K-864 kapısı ister); OpenAPI belgesi `docs/openapi/tracon.json` yalnız 5 `description` satırı değişir (§212.3)
> **Manuel test alanı:** [`manuel-test/25-SAGLIK-TESHIS-OPENAPI.md`](../../manuel-test/25-SAGLIK-TESHIS-OPENAPI.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7b66aba3:docs/arsiv/fazlar/212-HTTP-DTO-DARALTMA.md
> ```
>
> Damıtıldı 2026-10-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

81 HTTP DTO'su yalnız bir sebeple public: üretici, `description` metnini yalnız public tipin XML'inden alır. Bu tipleri 1.0'a dondurmak, kimsenin kullanmadığı 1 345 satırı sonsuza dek taşımak demektir. Faz tipleri `internal` yapar, metni `[Description]` özniteliğine **taşır** ve OpenAPI belgesinin değişmediğini kanıtlar.

## Bitiş Ölçütleri (DoD)

- [x] `python3 scripts/public-yuzey-envanteri.py --denetle` çıkış `0`; kanıt kuralı değişikliğinden **sonra** `--liste kanıtsız` boş — kural değişince `--liste kanıtsız | wc -l` → `81`; 81 tip `internal` olunca `--denetle` çıkış `0`, liste boş
- [x] 81 tipin her biri `internal`; `cat src/*/PublicAPI.Unshipped.txt | grep -vcE '^\s*$|^#'` → `8530` (9 875 − 1 345; Abstractions 258 · AspNetCore 1 058 · Core 29); `Shipped` → `0`
- [x] `OpenApiSnapshotTests` yeşil ve `git diff docs/openapi/tracon.json` **yalnız 5 `description` satırı**: `5 insertions(+), 5 deletions(-)`
- [x] `OpenApiDescriptionCoverageTests` yeşil; negatif kontrol kayda geçti — § Örnek Uygulama Koşumu
- [x] `packages/tracon-client` `npm run generate` + `npm run build` sonrası `src/schema.ts` farkı yalnız o 5 satır (`tsc` temiz); NSwag zinciri sonrası `src/Tracon.Client/Generated/` farkı aynı 5 satır (`TraconClientJsonContext.g.cs` değişmedi)
- [x] `CHANGELOG.md` `[Unreleased]` → Removed, 81 tipin her biri adıyla; `kapi.py yayin --kuru` yeşil (`exit=0`; ApiCompat tabanı `1.0.0-preview.4`, `breaking-changes.json` 17 + 63 + 1 tip, notlar hepsini adlandırdı; altı paketlenmiş sample, Native AOT ve net8.0 tüketici smoke)
- [x] Dört doğrulama kapısı sıfır uyarı: `python3 scripts/kapi.py kapanis --taban 87fa2918` — § Örnek Uygulama Koşumu
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — § Örnek Uygulama Koşumu
- [x] `secret` taraması boş döndü (`kapi.py tarama` → `✅ temiz`)
- [x] Manuel kabul case'leri `docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md` içine eklendi (`MT-DIAG-066`…`069`, `00-INDEKS.md` sayımı 49); dördü de koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (🔴 ve 🟡 yok; dört 🟢)
- [x] K-850 devir notunun ikinci kuralı yeni `K-1012` ile geçersiz kılındı; `hafiza/http-uc-guvenlik-ve-sozlesme.md` satır 30 güncellendi
- [x] Site: `docs-site/` `npm run check` temiz (`1031 pages`, `none broken`, SEO 0 hata, ağırlık tavanı altında)

### Doğrulama komutları

```bash
python3 scripts/public-yuzey-envanteri.py --liste kanıtsız | wc -l          # kural değişince 81
find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'  # 0
TRACON_OPENAPI_REFRESH=1 python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*OpenApiSnapshotTests"
git diff --stat docs/openapi/tracon.json                                     # 5 satır
curl -s http://localhost:5081/openapi/v1.json | python3 -c "import json,sys; print(json.load(sys.stdin)['components']['schemas']['WorkflowSaveRequest'].get('description'))"
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne oldu | Neden |
|---|---|---|---|
| 1 | §212.4: kapı `docs/openapi/tracon.json`'u okur, tabanı **sayı** tutar | Kapı **canlı belgeyi** okur (`OpenApiSnapshotTests.GenerateAsync` `internal static` oldu) ve tabanı **ad listesi** tutar: 290 satır (62 şema + 228 özellik) | Snapshot yenileme koşusu dosyayı yazarken kapı onu okurdu (yarış); canlı belge snapshot yenilenmeden önce de konuşur. Sayı tabanı bir kaybı bir kazançla gizler; ad listesi gizleyemez. Açık Soru 1-A uygulandı: `AIContent*` gerekçeli satırlarla tabanda |
| 2 | §212.3 "Doğrulanmadı": enum tip düzeyi `[Description]`; net8/net9 tüketicisi | **Ölçüldü.** .NET 10: tip, özellik, enum (`CanaryDecisionKind`, `ValidationSeverity`), `oneOf` dalı ve konumsal `[property: Description]` snapshot'ı **değiştirmedi**; diff yalnız 5 `cref` satırı. net9 (`Microsoft.AspNetCore.OpenApi` 9.0.8, ayrı probe): özellik düzeyi okunur, tip düzeyi okunmaz, `$ref` taşıyan özelliğin metni başvurulan şemaya yazılır. XML yolu .NET 10'a özgüdür (9.0.8 paketinde `analyzers/` yok), yani net9 tüketicisi **bir şey yitirmez**. net8'de `AddOpenApi()` yoktur | Plan "ilk iş ölç" demişti. **Ölçülmedi:** Swashbuckle + `IncludeXmlComments` ile kendi belgesini üreten tüketici bu 81 tipin `<summary>`'sini kaybeder; Tracon belgeyi `AddOpenApi()` ile üretir (K-352), Swashbuckle desteklenen yol değildir. Risk satırı buna göre okunur |
| 3 | Kanıt tablosu: `HeaderBearingTypeTests.cs:43` ve `:50` | Gerçek konum: `GetExportedTypes()` `:56`, sentinel `:50`. Tarama `GetTypes()`'e çevrildi; yeni ihlal çıkmadı | Plan satırı yaklaşık yazmıştı. Daha geniş tarama (internal tipler dahil) hiçbir yeni `Headers` taşıyıcısı bulmadı |
| 4 | Manuel case 1: `curl :5081`; case 4: `CS0122` beklenir | Örnek uygulamanın varsayılan adresi `:5080`; case'ler `MT-DIAG-066`…`069`. Case 4 paketlenmiş tüketiciyle ölçüldü: `CS0122` doğru | Plan portu `--urls` ile verilen koşumdan almıştı. `CS0122` beklentisi plan anında ölçülmemişti, ölçüm doğruladı |
| 5 | §212.3 madde 3: çok satırlı metin (75 adet) `\n` ile kalır | 71 çok satırlı + 320 tek satırlı + 2 konumsal = 393 `[Description]`. Çok satırlı metin `"…\n" +` birleştirmesiyle yazıldı | Kaynak satır yapısı korunur; ham çok satırlı dize satır sonunu platforma bağlar |
| 6 | §212.3 madde 5: tek seferlik betik, iş bitince silinir | Betik yazıldı, koştu, repoya **girmedi** (scratchpad). Bir hata: konumsal `record`'ta kapanış `)` düşmüştü; elle düzeltildi, derleme yakalamıştı | Betik yalnız bir dosyalı konumsal kolu desteklemişti |
| 7 | Hafıza notu ve karar defteri bütçesi planda yok | `hafiza/http-uc-guvenlik-ve-sozlesme.md` 16 000 B sınırını aştı (16 326) → iki satır sıkıştırıldı (15 494). `KARARLAR.md` 586 000 B sınırını aştı → `karar-damit --sinir 350` ve `--sinir 330` (12 satır, 781 B `KARARLAR-GECMISI.md`'ye taşındı) ve yeni satır kısaltıldı (585 833 B) | İçerik silinmedi, taşındı. Ledger sınırı tükenmeye yakın: sonraki faz yeniden kalibre etmek zorunda kalabilir |
| 8 | Planlanan dosya listesi | Eklenen: `scripts/kapi.py` (yalnız docstring), `ADAYLAR.md` (F-562, F-563), `KARARLAR.md` + `KARARLAR-GECMISI.md` (K-1012), `manuel-test/` (2 dosya) | Denetim 🟢'leri ve kapanış adımları |

## Bu Fazda Verilen Kararlar

- **`K-1012`** *(kategori: public-api, kullanıcı kararı UR-010 B)* — HTTP istek/yanıt DTO'su `internal` doğar, açıklamasını `[Description]` taşır; OpenAPI şeması kanıt değildir; K-850'nin ikinci kuralı geçersiz. Tam gerekçe `arsiv/KARARLAR-GECMISI.md`.
- **Kapı canlı belgeyi okur, tabanı ad listesi tutar** (yerel tercih, `K-*` açılmadı): Plandan Sapmalar 1.
- **Karma açıklama kaynağı kasıtlıdır** (yerel tercih): beş public istek DTO'su (`AgentDefinitionRequest`, `AgentRunRequest`, `McpServerRequest`, `SearchKnowledgeRequest`, `UploadDocumentRequest`) site kanıtı taşıdığı için public kalır ve açıklamasını XML `<summary>`'den almaya devam eder. Aynı dizinde iki kaynak vardır; kural: public tip → XML, `internal` DTO → `[Description]`.
- **Açık Soru 1-A ve 2-A** kullanıcıya ayrıca sorulmadan önerilen seçenekle uygulandı: `AIContent*` tabanda, `<remarks>` internal tipte kalır (kullanıcı itiraz ederse geri alınır).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, PostgreSQL (kullanıcı sırlarından), `http://localhost:5081` ve `:5080`; gerçek model anahtarları yapılandırılı, tek bir kısa gerçek `run` yapıldı.

| Ne | Gerçek çıktı |
|---|---|
| Canlı belge (`MT-DIAG-066`) | `WorkflowSaveRequest` → `Request to save a workflow definition.`; `nodes` üç satır, `AgentNames; leave both empty or set only one.`, `&lt;` sayısı `0`; `CanaryDecisionKind` → `Defines the outcome of a canary evaluation.`; `UploadDocumentResponse.sourceId` → `Identifier of the uploaded source.` |
| HTTP bağlama (`MT-DIAG-067`) | `PUT /api/workflows/mt-diag-067` `200` (`"kind":"Sequential"`, `"agentNames":["support","translator"]`), okuma `200`, silme `204`; `meta` anahtarları `authentication,prefix,roles,storage,version`; kiracı `{"tenantId":"default"}`; kota `dailyResetsAt,definitions,monthlyResetsAt,tenantId,timeZone,usage` |
| Gerçek `run` | `POST /api/agents/summarizer/run` `{"message":"Reply with the single word: pong"}` → SSE `run` olayı `runId 01a14471-7ffb-7638-acef-de8de8efbc59`, güncellemeler `"-"` ve `" pong"`; uygulama `gpt-5.4-mini` ile yanıtladı |
| Yeniden üretim (`MT-DIAG-068`) | `OpenApiSnapshotTests` yenileme, `npm run generate` + `build`, NSwag zinciri → `git status --short` boş |
| Paketlenmiş tüketici (`MT-DIAG-069`) | `Tracon.AspNetCore 1.0.0-preview.4.17` + `new WorkflowSaveRequest { … }` → `error CS0122: 'WorkflowSaveRequest' is inaccessible due to its protection level` |
| Negatif kontrol, kapı | `WorkflowRollbackRequest.Version`'dan `[Description]` kaldırıldı → `OpenApiDescriptionCoverageTests` kırmızı: `["WorkflowRollbackRequest.version"]`; geri konunca 27/27 `*OpenApi*` testi yeşil |
| Negatif kontrol, envanter | `Tracon.ZzProbeResponse` satırı `PublicAPI.Unshipped.txt`'e eklendi → `kapi.py tarama` ❌ `kanıtsız public yüzey (K-850)`; geri alınca ✅ |
| `ic-dongu` | `Tracon.AspNetCore.FunctionalTests` 2 127 test, 0 hata; `Tracon.Core.UnitTests` 4 125 test üç hedefte; ilk koşumda tek kırmızı `SourceLanguageTests` (yeni test dosyalarında "Faz" sözcüğü, "Phase" yapıldı) |
| `kapi.py kapanis --taban 87fa2918`, ilk koşum | Tam test aşamasında (628 sn) tek kırmızı: `LiveVoiceTests.An_empty_cut_opens_NO_run_and_says_so_on_the_thinking_channel`, `ObjectDisposedException` (`LiveVoiceSessionHost.cs:474`). Faz diff'i bu dosyaya dokunmaz; sınıf yalıtık 5/5 yeşil. Kök neden kodu okuyarak bulundu (`CancelAsync` ile `Dispose` yarışı) ve **F-564** olarak devredildi; kapı yeniden koşuldu — bkz. aşağıdaki satır |
| `kapi.py kapanis --taban 87fa2918`, yeniden koşum | Dokuz aşamanın tamamı yeşil, `exit=0`: `tarama` · `dokuman-bakim --denetle` · script birim testleri · agent haritası · `denetim-paketi` · `dotnet build` (0 uyarı, 0 hata, 93 sn) · `dotnet test Tracon.slnx` (677 sn, 76 test derlemesi, 0 hata; önceki koşumun `LiveVoiceTests` kırmızısı tekrarlanmadı) · `dotnet pack` · `dotnet format --verify-no-changes` · `docs-site` `npm run check` (1 031 sayfa, kırık bağlantı yok) |

### Tüketici yüzeyi senkronu (`tuketici-dokuman-senkronu`)

`dokuman-bakim.py --site-denetle --taban 87fa2918` iki kural tetikledi: `http-api` (karşılandı: `docs/openapi/tracon.json` değişti) ve `cekirdek-kavram` (hedef `concepts/` değişmedi). **Site güncellemesi gerekmiyor**, gerekçe:

- Elle yazılan hiçbir `docs-site/` sayfası 81 tipten birini anmaz: envanterin `kanıtsız` tanımı tam olarak budur (site, sample, şablon, README ve `<example>` korpusunda yok).
- `api/` DocFX'ten üretilir ve izlenmez: 81 tipin sayfası kalktı. `http-api/` OpenAPI'den üretilir: yalnız 5 `description` satırı değişti. Uç sayısı, yol, yöntem ve şema adı aynıdır (`http-api.md` sayısı değişmedi).
- `concepts/` bir davranış anlatır; bu faz davranış değiştirmedi, yalnız görünürlük ve açıklama kaynağı değişti.
- Sevk edilen metin: `CHANGELOG.md` `[Unreleased]` → Removed (81 tip adıyla) ve Fixed (5 açıklama). `capabilities.md` ve agent haritası etkilenmedi (`build-agent-map.mjs --check` güncel).
- `npm run check` yeşil: 1 031 sayfa, kırık bağlantı yok, SEO 0 hata, ağırlık tavanı altında.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan kullanıcı onayıyla olduğu gibi uygulandı; sekiz sapma yukarıda) |
| Düzeltme turu sayısı | 4 (konumsal `record` kapanış parantezi; `SourceLanguageTests` "Faz" sözcüğü; hafıza notu bütçesi; `KARARLAR.md` bütçesi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (denetçi 🔴 ve 🟡 bulmadı; dört 🟢) |
| Fazın ürettiği regresyon | 1, kapanıştan önce yakalandı (`SourceLanguageTests`, `ic-dongu`); üretim yolunda 0 |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anında henüz yok) |

## Denetim Bulguları

Bağımsız `faz-denetcisi` (taze bağlam, `git diff 87fa2918..HEAD`): **🔴 ve 🟡 yok.** Denetçi 393 metni kaynaktan ayrıştırıp snapshot ile karşılaştırdı (0 uyuşmazlık), 1 345 silinen satırın hepsinin 81 tipe ait olduğunu ve tabanın faz öncesi/sonrası açıklamasız kümeyle birebir aynı olduğunu doğruladı.

| # | 🟢 bulgu | Sonuç |
|---|---|---|
| 1 | `HeaderBearingTypeTests` yorumu `McpServerRequest`'i internal sanıyor; tip public | Düzeltildi (yorum) |
| 2 | `test_openapi_semasi_tuketici_kaniti_degildir` `envanter_olustur`'daki geri eklemeyi uçtan uca görmez | Devredildi: F-562 |
| 3 | Taban başlığı "HTTP DTO'ları burada olmaz" diyor; tabanda `internal` OpenAI uyumlu tipler ve 7 public enum var | Başlık düzeltildi (yalnız 81 tip); açıklama işi devredildi: F-563 |
| 4 | Beş public istek DTO'su XML'den, 81 tip `[Description]`'dan açıklama alıyor (iki kaynak) | Gerekçelendi: Bu Fazda Verilen Kararlar (karma durum kasıtlı) |

Denetçi salt-okunur çalıştığı için `dotnet build/test` koşmadı; derleme ve test durumunu `kapi-olcum.jsonl` ve test günlüklerinden okudu. Kapanış kapıları aşağıda ana oturumda koştu.

## Sonraki Faza Devir Notu

- **[Faz 213](../../213-TESISAT-ARAYUZLERI-VE-UYE-HIJYENI.md) başlangıç sayıları:** `PublicAPI.Unshipped.txt` toplamı `8530`; `public-surface-baseline.txt` Abstractions `423`, AspNetCore `23`, Core `79`; `--liste kanıtsız` boş. İkisi de aynı dosyaları değiştirir; 212 işlenmiştir, 213 bu sayılardan başlar.
- 🚨 **Yeni HTTP istek/yanıt DTO'su `internal` doğar:** `using System.ComponentModel;`, tipte ve her özellikte `[Description("…")]`, `<summary>` yazılmaz (`<remarks>` kalabilir), konumsal `record`'ta `[property: Description("…")]`. Metni unutmak derlenir ve her davranış testinden geçer; `OpenApiDescriptionCoverageTests` yakalar.
- 🚨 **`openapi-undescribed-baseline.txt` yalnız küçülür.** Yeni açıklamasız şema/özellik kırmızıdır; açıklama kazanmış bayat satır da kırmızıdır. Snapshot yenileme tabanı yeniden yazmaz.
- 🚨 **Swashbuckle + `IncludeXmlComments` ölçülmedi.** O yoldan kendi belgesini üreten tüketici 81 tipin `<summary>`'sini kaybeder. Desteklenen yol değildir (K-352); biri talep ederse K-1012'nin yeniden açılma koşuludur.
- 🚨 **Test kaynaklarında "Faz" yazma.** `SourceLanguageTests` Türkçe sözcük sayar; test ve `src` yorumlarında "Phase" kullan.
- **Karma açıklama kaynağı:** beş public istek DTO'su XML'den okur. Biri `internal` olursa `<summary>`'sini `[Description]`'a taşı.
- **Ledger sınırı:** `KARARLAR.md` 585 833 / 586 000 B. `karar-damit` tükendi (12 satır taşındı); bir sonraki karar satırı yeniden kalibrasyon ister (`scripts/dokuman-bakim.py` sabiti ve gerekçesi).
- **Yarım kalan iş:** yok. Site `npm run check` yeşil, **yayınlanmadı** (`site-deploy.sh` kullanıcı onayı bekler).

---

## EK-A — Kapsam (81 tip, 2026-10-16 ölçümü)

> Liste yalnız okuma kolaylığı içindir. Doğru kaynak: `--liste kanıtsız` (kural değişikliğinden sonra).

**`Tracon.Abstractions` (17):** `AgentValidationReport`, `CanaryDecisionKind`, `CanaryEvaluation`, `ContextWindowEstimate`, `DataSubjectErasureResult`, `ImageGenerationOperatorRequest`, `ImageGenerationOperatorResponse`, `JudgeFailure`, `JudgeRunResponse`, `RetentionPreview`, `RunCostRecalculationResult`, `SessionBranchRequest`, `SessionBranchResult`, `SpeakRequest`, `SpeakResponse`, `ValidationMessage`, `ValidationSeverity`

**`Tracon.AspNetCore` (63):** `AcceptedRunResponse`, `AgentDetailResponse`, `AgentRollbackRequest`, `AgentSkillRequest`, `AgentVersionDiffResponse`, `ApiKeyCreateRequest`, `ApprovalDecisionRequest`, `CurrentTenantResponse`, `EvalCaseInput`, `EvalCasePromotionRequest`, `EvalRunDetailResponse`, `EvalRunTriggerRequest`, `EvalSuiteSaveRequest`, `ExperimentCanaryResponse`, `ExperimentResultsResponse`, `ExperimentSaveRequest`, `InboundTriggerAcceptedResponse`, `InboundTriggerResponse`, `InboundTriggerSaveRequest`, `JobDetailResponse`, `JobScheduleSaveRequest`, `JobTriggerRequest`, `LiveVoiceSessionCreateRequest`, `LiveVoiceSessionCreateResponse`, `LiveVoiceSessionStatusResponse`, `LoopCriterionCatalogResponse`, `LoopCriterionKindResponse`, `LoopJudgeModelResponse`, `McpOAuthStartResponse`, `McpPromptArgumentsRequest`, `McpRefreshResponse`, `QuotaSaveRequest`, `QuotaUsageResponse`, `RetentionPolicySaveRequest`, `RetentionRunTriggerResponse`, `RunComparisonResponse`, `RunComparisonSide`, `RunFeedbackRequest`, `RunInputResponse`, `RunReplayResponse`, `SearchKnowledgeHit`, `SessionDetailResponse`, `SkillScriptGrantRequest`, `TenantEgressPolicyRequest`, `TenantEgressPolicyResponse`, `TenantProviderBindingRequest`, `TenantProviderBindingResponse`, `TenantRequest`, `ToolApprovalRuleRequest`, `TraconAuthenticationMeta`, `TraconMetaResponse`, `TraconRoleMeta`, `TraconStorageMeta`, `UploadDocumentResponse`, `WebhookSaveRequest`, `WebhookTestResponse`, `WorkflowFunctionResponse`, `WorkflowRespondHttpRequest`, `WorkflowResumeHttpRequest`, `WorkflowRollbackRequest`, `WorkflowRunHttpRequest`, `WorkflowSaveRequest`, `WorkflowVersionDiffResponse`

**`Tracon.Core` (1):** `OnlineEvaluationSummary`
