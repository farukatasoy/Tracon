# Faz 245 — OpenAI Strict Şema Modu ve Sağlayıcıya Giden Etkin Şema

> **Durum:** 📋 Planlandı (2026-10-25)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (yalnız ekleme: bir sağlayıcı yetenek arayüzü, iki ayar anahtarı, doğrulama yanıtına bir alan) · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-25 (sohbette alınan kararlar: strict yalnız yanıt şemasına uygulanır, tool'lar strict dışı kalır; `openai.*` ve `azure-openai.*` önekleri; `/validate` uyarı + etkin şema verir)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-347**
> **Genellik:** OpenAI ya da Azure OpenAI ile yapılandırılmış yanıt kullanan her tüketici. Tek tüketici turundan geldi (2026-10-11 video). İkinci kanıt iki tanedir. Birincisi ekosistem standardıdır: OpenAI'nin strict structured outputs özelliği. İkincisi bu fazın ölçümüdür: MEAI, strict kapalıyken de şemayı değiştirir ve bu fark bütün OpenAI ailesi kullanıcılarını etkiler. İş tüketici kodunda kalamaz: ayar Tracon'un sağlayıcı paketlerindedir; etkin şemayı yalnız Tracon hesaplayabilir
> **Karşı görüş:** "Host doğrulayıcısı ve onarım turu strict olmadan da şemayı zorlar." Cevap: doğru, ve bu yüzden strict isteğe bağlıdır (K1). Değeri ilk denemede uyum ve onarım turunun maliyetidir. Görünürlük kısmı karşı görüşten bağımsızdır: yazar bugün sağlayıcının hangi şemayı gördüğünü bilmez
> **Paketler:** `Tracon.Abstractions` (yetenek arayüzü), `Tracon.Core` (doğrulayıcı), `Tracon.OpenAI` + `Tracon.Azure` (ortak kaynak `Tracon.OpenAI.Shared`: strict dekoratörü ve şema kopyası), `Tracon.AspNetCore` (rapor alanı, OpenAPI)
> **Tüketici yüzeyi:** site `guides/structured-output.md` (strict modu, etkin şema, "Defaults and caveats"), `guides/model-providers.md` (OpenAI ve Azure ayar anahtarları — bugün "anahtar yok" diyor), `capabilities.md` satırı · sevk edilen: `OpenAIProviderNames`/`AzureOpenAIProviderNames` XML, `docs/sozlesme/dogrulama-kodlari.txt` (üretilir), OpenAPI → iki istemci (üretilir), `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`

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

- [ ] `openai.strictJsonSchema: true` ile gerçek OpenAI isteğinde `response_format.json_schema.strict = true` gider; tool'ların `strict` alanı `false`tur (tel testi)
- [ ] Aynısı `azure-openai.strictJsonSchema` için (tel testi)
- [ ] Kopyalanan dönüşüm MEAI'nin gerçek isteğiyle iki yüzeyde ve iki modda bayt bayt aynıdır (tel testi)
- [ ] `/validate` raporu `responseSchemas` ve iki uyarı kodunu verir; `dogrulama-kodlari.txt` yeniden üretildi; OpenAPI ve iki istemci yeniden üretildi
- [ ] Strict + `oneOf` gerçek OpenAI ile ölçüldü; sonuç ve (gerekirse) hata kodu belgeye yazıldı
- [ ] `samples/Tracon.Api` yapılandırılmış yanıtlı bir agent ile strict açık ve kapalı koşuldu; iki çıktı belgeye yazıldı
- [ ] Site sayfaları, iki sağlayıcının XML'i ve `CHANGELOG.md [Unreleased]` güncel
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

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

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.
> Beklenen kayıtlar: strict yalnız yanıt şemasına, tool'lar `strict = false`
> *(public-api; alternatif: MEAI'nin yerel anlamı — tool'lar da strict)* ·
> `IResponseSchemaSupport` ve kopya + tel kapısı *(public-api; alternatif: yalnız
> özet/hash — yazar farkı görmez)*.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.
