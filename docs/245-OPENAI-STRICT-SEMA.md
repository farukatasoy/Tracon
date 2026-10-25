# Faz 245 — OpenAI Strict Şema Modu ve Sağlayıcıya Giden Etkin Şema

> **Durum:** ✅ Tamamlandı (2026-10-25)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (yalnız ekleme: bir sağlayıcı yetenek arayüzü, iki ayar anahtarı, doğrulama yanıtına bir alan) · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-25 (sohbette alınan kararlar: strict yalnız yanıt şemasına uygulanır, tool'lar strict dışı kalır; `openai.*` ve `azure-openai.*` önekleri; `/validate` uyarı + etkin şema verir)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-347**
> **Genellik:** OpenAI ya da Azure OpenAI ile yapılandırılmış yanıt kullanan her tüketici. Tek tüketici turundan geldi (2026-10-11 video). İkinci kanıt iki tanedir. Birincisi ekosistem standardıdır: OpenAI'nin strict structured outputs özelliği. İkincisi bu fazın ölçümüdür: MEAI, strict kapalıyken de şemayı değiştirir ve bu fark bütün OpenAI ailesi kullanıcılarını etkiler. İş tüketici kodunda kalamaz: ayar Tracon'un sağlayıcı paketlerindedir; etkin şemayı yalnız Tracon hesaplayabilir
> **Karşı görüş:** "Host doğrulayıcısı ve onarım turu strict olmadan da şemayı zorlar." Cevap: doğru, ve bu yüzden strict isteğe bağlıdır (K1). Değeri ilk denemede uyum ve onarım turunun maliyetidir. Görünürlük kısmı karşı görüşten bağımsızdır: yazar bugün sağlayıcının hangi şemayı gördüğünü bilmez
> **Paketler:** `Tracon.Abstractions` (yetenek arayüzü), `Tracon.Core` (doğrulayıcı), `Tracon.OpenAI` + `Tracon.Azure` (ortak kaynak `Tracon.OpenAI.Shared`: strict dekoratörü ve şema kopyası), `Tracon.AspNetCore` (rapor alanı, OpenAPI)
> **Tüketici yüzeyi:** site `guides/structured-output.md` (strict modu, etkin şema, "Defaults and caveats"), `guides/model-providers.md` (OpenAI ve Azure ayar anahtarları — bugün "anahtar yok" diyor), `capabilities.md` satırı · sevk edilen: `OpenAIProviderNames`/`AzureOpenAIProviderNames` XML, `docs/sozlesme/dogrulama-kodlari.txt` (üretilir), OpenAPI → iki istemci (üretilir), `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`

## Önceki Fazdan Devir

> Faz 244 (Gemini konuşma adaptörü) devri; yalnız bu fazı etkileyenler.

- **Site ağırlığı sıfır boşlukta.** `check-weight.mjs` tavanı 59.000 B ve en ağır sayfa `guides/voice` 58.963 B; `concepts/workflows` da tavana yakın. Faz 244 yeni bir sayfa (`guides/gemini-speech`) ekledi ve kenar çubuğu girişi **her sayfaya 21–27 B gzip** ekler. Bu faz `guides/model-providers.md` (57,1 KB) ve `guides/structured-output.md` sayfalarını büyütür; ağır bir sayfaya yeni içerik girecekse önce `npm run check` ağırlığını ölç, tavanı yükseltme: sayfayı böl ya da kes (kalite sözleşmesi F: "yalnız düşer").
- **`llms.txt` bütçesi 24 → 25 KiB** oldu (`build-agent-map.mjs`, ölçüm başında); 24.777 / 25.600 B, yaklaşık üç yeni sayfalık boşluk. Yeni sayfa eklemeden önce `kapi.py uret` ile sayıyı gör.
- **`guides/model-providers.md` tablosunda `Tracon.Google` satırı** artık Gemini konuşmasına bağlanır; bu faz aynı tabloya OpenAI/Azure ayar anahtarlarını yazarken o satırı bozma.
- **`ModelProviderCore<T>`'ye `GetTenantFactory` eklendi** (paylaşılan kaynak, dört pakete derlenir; yalnız Google kullanır). `Tracon.OpenAI.Shared` ile karıştırma: o ayrı bir ortak kaynaktır.

## Amaç

Tracon yanıt şemasını `ChatResponseFormat.ForJsonSchema` ile verir. OpenAI'nin
strict modu istenemez. Ayrıca yazar, sağlayıcıya **hangi** şemanın gittiğini
göremez. Bu fazın ölçümü ikinci sorunun düşünülenden büyük olduğunu gösterdi:
MEAI, strict **kapalıyken de** şemayı dönüştürür.

**Ölçüldü (2026-10-25, MEAI.OpenAI 10.11.0 decompile + MEAI.Abstractions 10.11.0 probu):**

- `OpenAIChatClient` ve `OpenAIResponsesChatClient` yanıt şemasını **her zaman**
  `StrictSchemaTransformCache` ile dönüştürür. Strict bayrağı yalnız
  `CreateJsonSchemaFormat(…, strict)` parametresini belirler.
- Dönüşüm seçenekleri: `DisallowAdditionalProperties`, `ConvertBooleanSchemas`,
  `MoveDefaultKeywordToDescription`, `RequireAllProperties`. Ek olarak 22 anahtar
  şemadan silinir ve `description`'a metin olarak taşınır: `contentEncoding`,
  `contentMediaType`, `not`, `minLength`, `maxLength`, `pattern`, `format`,
  `minimum`, `maximum`, `multipleOf`, `patternProperties`, `minItems`, `maxItems`,
  `unevaluatedProperties`, `propertyNames`, `minProperties`, `maxProperties`,
  `unevaluatedItems`, `contains`, `minContains`, `maxContains`, `uniqueItems`.
- Prob: `RequireAllProperties` isteğe bağlı her alanı `required`'a ekler ve tipine
  `null` **eklemez**. `oneOf` korunur.
- Sonuç: OpenAI ailesinde isteğe bağlı bir alan modele zorunlu görünür, ve
  `maxLength` gibi bir sınır sağlayıcıya sınır olarak gitmez. Tracon'un host
  doğrulaması özgün şemayı uygulamaya devam eder. Yazar bu farkı bugün hiçbir
  yerde görmez.
- Strict bayrağı `ChatOptions.AdditionalProperties["strict"]` (`bool`) değeridir.
  Aynı anahtar tool'ları da etkiler: `HasStrict(tool.AdditionalProperties) ?? HasStrict(options.AdditionalProperties)`.
  Tool düzeyindeki değer kazanır.

**Doğrulanmış kod kanıtı (2026-10-25):**

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinitionCompiler.ChatOptions.cs:359`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.ChatOptions.cs) | `ChatResponseFormat.ForJsonSchema(schema, name, description)`. Aday `:199` diyordu; satır kaydı |
| [`OpenAIProviderNames.cs:45`](../src/Tracon.OpenAI/OpenAIProviderNames.cs) · [`AzureOpenAIProviderNames.cs:44`](../src/Tracon.Azure/AzureOpenAIProviderNames.cs) | İki sağlayıcının desteklediği ayar listesi boştur; her anahtar reddedilir (K-208) |
| [`OpenAIChatClientFactory.cs:110-113`](../src/Tracon.OpenAI/OpenAIChatClientFactory.cs) · [`AzureOpenAIChatClientFactory.cs:126-129`](../src/Tracon.Azure/AzureOpenAIChatClientFactory.cs) | Ayar denetiminin yeri |
| `src/` içinde `"strict"` anahtarı (`grep`) | Hiçbir kod yazmaz |
| [`Tracon.OpenAI.Shared/README.md`](../src/Tracon.OpenAI.Shared/README.md) | Ortak kaynak iki pakete derlenir; dekoratör ve şema kopyası bir kez yazılır |
| [`ISamplingSupport.cs:78`](../src/Tracon.Abstractions/Models/ISamplingSupport.cs) · [`IHostedToolSupport.cs:64`](../src/Tracon.Abstractions/Models/IHostedToolSupport.cs) | Sağlayıcı yeteneği için public, isteğe bağlı arayüz emsali |
| [`AgentValidationReport.cs`](../src/Tracon.Abstractions/Agents/AgentValidationReport.cs) | `internal` HTTP DTO'su (Faz 212): `valid`, `inconclusive`, `messages`. Alan eklemek yalnız HTTP sözleşmesini büyütür |
| [`AgentDefinitionValidator.cs:135-184`](../src/Tracon.Core/Compilation/AgentDefinitionValidator.cs) | Model adımı istemciyi gerçek yolla kurar (kiracının kimliği ve egress'i dahil), istek göndermez |
| [`ScriptedChatCompletionsServer.cs`](../tests/Tracon.AspNetCore.FunctionalTests/Infrastructure/ScriptedChatCompletionsServer.cs) | Tel düzeyinde istek gövdesini yakalayan test sunucusu |
| Faz 204 devri (aday gövdesi) | Kesik strict yanıt da `OutputTruncated` olur (sağlayıcıdan bağımsız) |

## Tasarım

- **Ayarlar:** `openai.strictJsonSchema` ve `azure-openai.strictJsonSchema`
  (`bool`, varsayılan `false`; K1). İki listeye de girer. Boolean olmayan değer
  bugünkü `invalid_setting` kuralıyla reddedilir. `InvalidSettingValue` `null`
  kalır (Faz 204 notu, K-954).
- **Dekoratör (ortak kaynak):** ayar `true` ve `ResponseFormat` bir JSON şeması
  ise, dekoratör isteğin `ChatOptions`'ını **kopyalar** ve
  `AdditionalProperties["strict"] = true` yazar. Kopyadaki her
  `AIFunctionDeclaration`'ı `strict = false` taşıyan bir sarmalayıcıyla değiştirir
  (bakımcı kararı: tool'lar strict dışı kalır). Dekoratör `FunctionInvokingChatClient`'ın
  **altındadır**. Tool çağrısını MAF'ın kendi tool haritası yapar; sarmalayıcı
  yalnız sağlayıcıya giden bildirimi değiştirir. **Uygulamanın ilk ölçümü:**
  `AITool.AdditionalProperties` sarmalayıcıda geçersiz kılınabiliyor mu, ve tel
  düzeyinde tool `strict` alanı `false` mu?
- **Yetenek arayüzü (public, isteğe bağlı):** `IResponseSchemaSupport` (ad
  taslaktır). Sağlayıcı bir bağlama ve bir şema alır ve sağlayıcıya gidecek etkin
  şemayı, strict durumunu ve farkları verir. OpenAI ve Azure sağlayıcıları onu
  uygular. Arayüzü uygulamayan sağlayıcı için rapor alan vermez.
- **Kopya ve kayma kapısı.** Etkin şema MEAI'nin `internal` dönüşümünün bir
  kopyasıdır: public `AIJsonUtilities.TransformSchema`, aynı dört seçenek ve
  aynı 22 anahtarlık liste. 🚨 Kopya MEAI yükseltmesinde kayar. Bir **tel testi**
  bunu engeller: `ScriptedChatCompletionsServer` gerçek istek gövdesindeki şemayı
  yakalar. Test bu şemayı, iki yüzeyde (Chat Completions, Responses) ve iki modda
  (strict açık/kapalı), kopyanın çıktısıyla karşılaştırır. MEAI değişirse test
  kırmızı olur.
- **Doğrulama (`POST /api/agents/validate`):**
  - Rapora `responseSchemas` alanı girer: birincil bağlama ve her yedek
    (`ModelBinding.Fallbacks`) için `{ provider, model, strict, schema }`. Yalnız
    `IResponseSchemaSupport` uygulayan sağlayıcılar listelenir.
  - Uyarılar (`ValidationSeverity.Warning`; kodlar taslak, sözleşme dosyası
    testle üretilir):
    `response_schema_optional_required` — "bu isteğe bağlı alanlar sağlayıcıya
    zorunlu gider" + alan yolları · `response_schema_keyword_not_enforced` —
    "bu sınırlar sağlayıcıya sınır olarak gitmez; Tracon'un doğrulaması uygular" +
    anahtar yolları.
  - Strict açıkken sağlayıcının reddettiği yapı (ör. `oneOf`) **ölçülür**
    (Manuel 1). Ölçüm reddi gösterirse bir hata kodu eklenir:
    `response_schema_strict_unsupported`. Ölçülmeden kod yazılmaz.
- **Site cümlesi.** Strict, isteğe bağlı alanları **zaten** zorunlu gönderilen
  şemanın sağlayıcı tarafından **uygulanmasıdır**. İsteğe bağlı bir alan isteyen
  yazar onu `["string", "null"]` yapar. "Defaults and caveats" bölümü bugünkü
  dönüşümü de yazar (strict kapalıyken de geçerli).
- **Uyumlu sunucular.** `UseOpenAICompatible()` ile kaydedilen sunucu da `openai`
  önekini kullanır. Strict'i desteklemeyen bir sunucu isteği reddedebilir; ayar
  isteğe bağlıdır ve site bunu yazar.
- **Kapsam dışı:** OpenAI'de `output_truncation_risk` (Faz 204 devri). İstemci
  `ModelOutputLimit` bildirmez; bu iş istenmedi. İhtiyaç olursa ayrı aday olur.

### Planlanan public yüzey (taslak)

```csharp
// Tracon.Abstractions
public interface IResponseSchemaSupport
{
    ResponseSchemaShape DescribeResponseSchema(ModelBinding binding, JsonElement schema);
}

public sealed record ResponseSchemaShape
{
    public required JsonElement EffectiveSchema { get; init; }
    public required bool Strict { get; init; }
    public IReadOnlyList<string> OptionalMadeRequired { get; init; }   // JSON yolları
    public IReadOnlyList<string> KeywordsNotEnforced { get; init; }    // "<yol>: <anahtar>"
}

// Tracon.OpenAI / Tracon.Azure
public const string StrictJsonSchemaSetting = "openai.strictJsonSchema";        // OpenAIProviderNames
public const string StrictJsonSchemaSetting = "azure-openai.strictJsonSchema";  // AzureOpenAIProviderNames
```

**Bugün eklemek bedava / sonradan eklemek kırıcı:** hepsi yeni üyedir; GA'dan
sonra da ekleyicidir. `SupportedSettings` listesinin boş olmaktan çıkması, XML'de
"Empty today" diyen metnin değişmesidir.

## Bitiş Ölçütleri (DoD)

- [x] `openai.strictJsonSchema: true` ile gerçek OpenAI isteğinde `response_format.json_schema.strict = true` gider; tool'ların `strict` alanı `false`tur (tel testi) — `StrictResponseSchemaWireTests.The_strict_setting_puts_strict_true_on_the_response_format_and_false_on_every_tool` (Chat Completions + Responses, `tools[].function.strict` / `tools[].strict`)
- [x] Aynısı `azure-openai.strictJsonSchema` için (tel testi) — aynı sınıf `Tracon.Azure.UnitTests`'te (`Surfaces` yalnız Chat Completions)
- [x] Kopyalanan dönüşüm MEAI'nin gerçek isteğiyle iki yüzeyde ve iki modda bayt bayt aynıdır (tel testi) — `The_schema_on_the_wire_is_the_schema_the_copy_computes` ve 22 anahtarın hepsini taşıyan `Every_keyword_the_library_moves_is_moved_by_the_copy_too`; mutasyonla ölçüldü (anahtar silme, `RequireAllProperties` kapatma, tool sarmayı atlama, `options` yerinde yazma: hepsi kırmızı)
- [x] `/validate` raporu `responseSchemas` ve iki uyarı kodunu verir; `dogrulama-kodlari.txt` yeniden üretildi; OpenAPI ve iki istemci yeniden üretildi — `ResponseSchemaValidationTests` (HTTP), `ResponseSchemaReportTests` (birim); `docs/sozlesme/dogrulama-kodlari.txt` (+3 kod), `docs/openapi/tracon.json`, `packages/tracon-client/src/schema.ts`, `TraconApiClient.g.cs`
- [x] Strict + `oneOf` gerçek OpenAI ile ölçüldü; sonuç ve hata kodu belgeye yazıldı — `HTTP 400 … In context=('properties', 'answer'), 'oneOf' is not permitted.`; strict kapalıyken aynı şema `200`. Hata kodu `response_schema_strict_unsupported` eklendi (K-1096); `MT-GUARD-117`, `docs/hafiza/openai-yanit-semasi.md`
- [x] `samples/Tracon.Api` yapılandırılmış yanıtlı bir agent ile strict açık ve kapalı koşuldu; iki çıktı belgeye yazıldı — gerçek `gpt-5.4-mini`, izole şerit:
  - strict açık: `{"name":"Ada","nickname":"","age":null}` (106 girdi / 17 çıktı token)
  - strict kapalı: `{"name":"Ada","nickname":"","age":null}` (106 / 17) — aynı sonuç: dönüşüm strict kapalıyken de koşar
  - `nickname` → `["string","null"]`, strict açık: uyarı yok, `{"name":"Ada","nickname":null,"answer":"Ada Lovelace was born in 1815."}`; `openai-responses` yüzeyinde aynı biçim
  - `oneOf` + strict açık: `validate` `valid: true` + uyarı `response_schema_strict_unsupported`, çalıştırma `502` (günlükte `'oneOf' is not permitted`); strict kapalı `{"answer":42}`
- [x] Site sayfaları, iki sağlayıcının XML'i ve `CHANGELOG.md [Unreleased]` güncel — `guides/structured-output.md`, `guides/model-providers.md`, `capabilities.md`; `OpenAIProviderNames`/`AzureOpenAIProviderNames`/`IResponseSchemaSupport` XML; iki paket README'si; `npm run check` yeşil (ağırlık tavanı değişmedi)
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban 3697d93c` — üretim, tarama, doküman denetimi, build, pack, site (`npm run check`), format yeşil; tam test 28.344 / 0 düşen / 1 atlanan. İlk tam koşumda tek test (`JobWorkerLeaseContractTests.A_cancelled_job_whose_handler_throws_stays_cancelled`, zamanlama) düştü ve izole geçti; tam paket yeniden koşuldu, yeşil

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Strict bayrağı yanıt şemasına gider, tool'lara gitmez | Fonksiyonel (tel, `ScriptedChatCompletionsServer`, Chat Completions + Responses) | `StrictResponseSchemaWireTests` |
| Kopya, MEAI'nin gerçek dönüşümüyle aynıdır | Fonksiyonel (tel) | `StrictResponseSchemaWireTests` |
| Ayar yokken istek değişmez | Fonksiyonel (tel) | `StrictResponseSchemaWireTests` |
| Bilinmeyen ya da boolean olmayan değer reddedilir | Sözleşme | `ModelProviderSettingsContract` (OpenAI ve Azure türevleri) |
| Rapor alanları ve uyarı yolları | Birim (doğrulayıcı) | `AgentDefinitionValidatorTests` |
| Yedek bağlama ayrı satır alır | Birim | `AgentDefinitionValidatorTests` |
| Uç noktanın JSON'u ve OpenAPI şeması | Fonksiyonel (HTTP) | `/validate` uç testi |

Beş soru: iptal — dekoratör token'ı aynen geçirir · eşzamanlılık — dekoratör
paylaşılan `ChatOptions`'ı değiştirmez, kopyalar (test: aynı istemciyle iki
eşzamanlı istek) · boş/aşırı girdi — şemasız istekte dekoratör hiçbir şey
yapmaz; çok büyük şemada kopya MEAI'nin önbelleğine benzer bir önbellek
kullanmaz, doğrulama anında bir kez hesaplanır · başka kiracı — rapor
kiracının bağlamasıyla kurulur (doğrulayıcının bugünkü yolu) · alt sistem
hatası — sağlayıcı strict'i reddederse bugünkü sağlayıcı hata yolu. `🤖` case:
gerçek OpenAI ile strict açık/kapalı ve `oneOf` ölçümü.

## Açık Sorular

| # | Soru | Öneri |
|---|---|---|
| 1 | `IResponseSchemaSupport` public mi olsun, yoksa yalnız iki Tracon paketi mi bilsin? | Public: `ISamplingSupport` ve `IHostedToolSupport` emsali; şema dönüştüren üçüncü taraf sağlayıcı da raporu doldurur |
| 2 | Rapordaki etkin şema her doğrulamada yeniden mi hesaplanır? | Evet. Doğrulama sıcak yol değildir; önbellek ikinci bir kayma kaynağıdır |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

1. **"Host doğrulaması özgün şemayı uygular" yanlıştı.** Tracon yerleşik bir şema doğrulayıcı taşımaz; yalnız tüketicinin `IStructuredResponseValidator`'ı şemayı özgün hâliyle alır (`Enabled` açıkken). Uyarı metni ve site bunu söyler: "A registered `IStructuredResponseValidator` still receives the schema as written."
2. **`ResponseSchemaShape.StrictUnsupported` planda yoktu.** Ölçülen red (`oneOf` → `400`) bir taşıyıcı ister; ayrı bir yetenek arayüzü yerine şekle üçüncü liste eklendi. Liste yalnız ölçülen hizmette (`api.openai.com` ya da adres verilmemiş) dolar; Azure ve uyumlu sunucu için boş kalır (K-1096). Canlı sondaj (Chat Completions): `oneOf`, `allOf`, `if`, `then`, `dependentRequired`, `dependentSchemas` ve `additionalProperties`'i `false` olmayan nesne (serbest nesne, harita, `$defs` içindekiler) `400` verdi; `prefixItems` ve `else` ayrı ölçülmedi, listede yok. Responses yüzeyi (denetim 🟡): `oneOf` aynı mesajla reddedildi, bu yüzden liste iki yüzeye uygulanır; Responses'ta tool için `strict` yazılmayınca açık (strict'e uymayan) bir şemayla `200` döndü, yani ayarı açmak tool'u 'strict'ten non-strict'e çevirmez.
3. **`response_schema_strict_unsupported` UYARI** (kullanıcı kararı, denetim 🔴). Plan "hata kodu" dedi, önem yazmadı; ilk uygulama ERROR yaptı ve bu, kayıt yolunun da `ValidateEntitiesAsync` ile doğrulayıp `400` dönmesi yüzünden strict + `oneOf` tanımı **kaydedilemez** kıldı (`MT-GUARD-117`'nin kayıt adımı kırıldı). Cevap kiracıdan bağımsız olduğu için kiracının kendi uyumlu sunucusunu bağlayan BYOK'u yanlışlıkla engellerdi. Uyarı olarak kaldı: çalışmayacak tanım `valid: true` döner (K-1096'daki alternatif: hata). Yeni davranış `ResponseSchemaValidationTests` ile sabit (uyarı + `POST /api/agents` `201`).
4. **Uyarıdan null kabul eden alan çıkarıldı.** Plan "bu isteğe bağlı alanlar zorunlu gider" dedi. Tipi `null` kabul eden (`type` dizisi, `enum`/`const` null, `anyOf`/`oneOf` kolu, boş şema) alan zorunlu gitse de model onu boş bırakabilir; uyarı gürültü olurdu. `$ref` izlenmez, uyarıya girer.
5. **Konum biçimi JSON Pointer** (`#/properties/address/properties/zip`), plan "JSON yolları" dedi.
6. **`IResponseSchemaSupport` `ModelProviderDescriptor.ResponseSchemaSupport` (internal) üzerinden okunur;** `IModelProviderRegistry`'ye üye eklenmedi (`ISamplingSupport` emsali). Public yüzey: `IResponseSchemaSupport`, `ResponseSchemaShape`, iki `StrictJsonSchemaSetting` sabiti.
7. **Tel testleri `StubHttpServer` ile** ve `tests/Shared/OpenAI/` altında (iki sağlayıcı test projesine bağlı), `ScriptedChatCompletionsServer` ile değil: ortak kaynağın her derleme kopyasını sınamak gerekir (`Providers.Shared` testleri emsali) ve Azure test projesi `AspNetCore.FunctionalTests`'e bağlı değil. Tek istekli gerçek loopback soketi aynı kanıtı verir; Responses yüzeyi de aynı sunucuyla.
8. **`InvalidSettingValue` `null` kalmadı.** Faz 204 notu boolean ayar için `null` demişti; sözleşme senaryosu boolean olmayan bir metinle de çalışır ve değeri yankılamadığını sınar. İki sözleşme türevinde `"sometimes"` kullanıldı.
9. **Dekoratörün yeri ölçüldü, plan doğru çıktı:** `AITool.AdditionalProperties` sarmalayıcıda geçersiz kılınabilir (`AIFunctionDeclaration` alt sınıfı; MEAI'nin `DelegatingAIFunctionDeclaration`'ı `internal`), tel düzeyinde `strict: false`. Tool kendi `strict` anahtarını yazdıysa dokunulmaz.
10. **`SupportedSettings` mesajı değişti:** boş liste "supports no extra settings" diyordu; artık "Supported keys: …". Üç testin ve iki manuel case'in metni güncellendi (`MT-OAI-*`, `MT-PROV-*` beklenen metni).
11. **Yeni kapılar:** `dokuman-bakim.py` `SURUM_DAMGASI_KAYDI` yeni satır (`OpenAIResponseSchema.cs` → `Microsoft.Extensions.AI.OpenAI`); `README.md` public tip sayısı 622 → 624; `public-surface-baseline.txt` `Tracon.Abstractions` 405 → 407 (planlı iki tip: `IResponseSchemaSupport`, `ResponseSchemaShape`; yüzey gerekçeli ve planlı büyüdü).
12. **Python `urllib` bu makinede sertifika hatası verdi;** canlı sondaj `curl` ile koşuldu (`docs/hafiza/openai-yanit-semasi.md`).

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **K-1094** — strict yalnız yanıt şemasına, tool'lar `strict = false` *(public-api; alternatif: MEAI'nin yerel anlamı)*
- **K-1095** — `IResponseSchemaSupport` + kopya + tel kapısı *(public-api; alternatif: yalnız özet/hash)*
- **K-1096** — `response_schema_strict_unsupported` yalnız ölçülen hizmette ve uyarı *(public-api, kullanıcı kararı; alternatif: hata seviyesi, tüm OpenAI ailesi)*

Kategori dışı (kapanış commit mesajı): null kabul eden alanın uyarıdan çıkarılması, JSON Pointer biçimi, `StubHttpServer` ile tel testleri, `SupportedSettings` mesaj metni, `SURUM_DAMGASI_KAYDI` satırı.

## Tüketici Yüzeyi Envanteri

1. **`docs-site/`** (elle): `guides/structured-output.md` (yeni bölüm "The schema the provider receives" + "Strict mode", tablo ve sorun giderme), `guides/model-providers.md` (OpenAI/Azure ayar anahtarı tablosu), `capabilities.md`. Üretilen: `http-api/` (OpenAPI), `llms-full.txt`. Ağırlık: tavan değişmedi (en ağır `guides/voice` 58.964 B).
2. **Sevk edilen metin**: `OpenAIProviderNames`/`AzureOpenAIProviderNames` XML (`StrictJsonSchemaSetting`, `SupportedSettings`), `IResponseSchemaSupport`/`ResponseSchemaShape` XML, `src/Tracon.OpenAI/README.md`, `src/Tracon.Azure/README.md`, `CHANGELOG.md [Unreleased]`, `docs/openapi/tracon.json`, `packages/tracon-client/src/schema.ts`, `TraconApiClient.g.cs`, `docs/sozlesme/dogrulama-kodlari.txt`.
3. **Yerel referans / agent haritası**: `capabilities.md` yapılandırılmış çıktı satırı; harita bütçesi içinde.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 12 sapma; biri kullanıcı kararı (hata → uyarı, denetim 🔴) |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (kapanış kapısı yeşil; 🔴 fazın kendi ilk uygulamasındaydı, kapanmadan düzeltildi) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı (2026-10-25, taze bağlamlı `faz-denetcisi`); 🟢 `docs/ADAYLAR.md`'ye gitti.

- 🔴 **`response_schema_strict_unsupported` ERROR olduğu için strict + `oneOf` tanımı kaydedilemiyordu.** Gerçek (kullanıcı kararı: uyarıya indir). Kayıt yolu (`ValidateEntitiesAsync`) hatayı `400` yapar; faz dokümanı "kayıt engellenmez" diyordu. Düzeltme: uyarı; `POST /api/agents` `201` döner (`ResponseSchemaValidationTests`); sapma 3, K-1096, site, CHANGELOG, `MT-GUARD-117` düzeltildi.
- 🟡 Public XML yanlış iddia taşıyordu ("host reply'i doğrular"). Kapandı: `IResponseSchemaSupport` ve `ResponseSchemaShape` XML'i "a registered `IStructuredResponseValidator` receives the schema as written" der.
- 🟡 `StrictUnsupported` ölçüm kapsamı testsizdi ve Responses ölçülmemişti. Kapandı: `OpenAIResponseSchemaSupportTests` (varsayılan/`api.openai.com`/özel adres/Responses/yanlış tipli ayar) ve `AzureOpenAIResponseSchemaSupportTests`; Responses yüzeyinde `oneOf` canlı ölçüldü (aynı `400`); kiracı uç adresi (BYOK) cevabın dışında kalır, bu yüzden kod uyarıdır (sapma 3).
- 🟡 Doğrulayıcı yalnız `TraconException`/`ArgumentException` yakalıyordu; istemci `default` yanında metin olmayan `description`'da `InvalidOperationException` atar (denetçinin okuması, karakterizasyon testiyle doğrulandı). Kapandı: `OperationCanceledException` dışı her istisna yakalanır; birim (`ResponseSchemaReportTests`) ve HTTP (`A_schema_the_client_cannot_rewrite…`) testi.
- 🟡 Kayma kapısı yalnız bugünkü 22 anahtarı taşıyordu. Kapandı: "kalan anahtarlar" sondası (`enum`, `const`, `exclusiveMinimum`, `prefixItems`, `dependentRequired`, `$defs`+`$ref`, `anyOf`, `additionalProperties` şeması); `OpenAIResponseSchema`'ya sahte bir 23. anahtar eklemek (`exclusiveMinimum`) iki yüzeyde kırmızı oldu. İddia "sondada bulunan her şey" diye daraltıldı.
- 🟡 Dekoratör gerçek `FunctionInvokingChatClient` altında sınanmamıştı. Kapandı: `Under_the_function_loop_the_tool_runs_and_both_turns_carry…` (iki tur, fonksiyon bir kez çalışır, her iki turda `strict: true` biçim ve `strict: false` tool).
- 🟢 F-694 (sağlayıcı kılavuzunda `IResponseSchemaSupport` yok), F-695 (kayıtta durmaz; K-1096), F-696 (Azure/uyumlu sunucu ölçümü), F-697 (yeniden yazılamayan şema raporda iz bırakmaz). Denetçinin ikinci 🟢'si (Responses'ta tool `strict` varsayılanı) ölçüldü ve kapandı: ayar açıkken `strict: false` yazmak sağlayıcı varsayılanını değiştirmez.
