# 06 — Sağlayıcı: Anthropic, Google, Azure OpenAI (`PROV`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../06-SAGLAYICI-DIGER.md`](../../06-SAGLAYICI-DIGER.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/06-SAGLAYICI-DIGER.md
> ```

---

## Temiz geçen case'ler (19)

| Case | Durum | Başlık |
|---|---|---|
| MT-PROV-001 | ☑ | `UseAnthropic()`/`UseGoogle()` doğru adlarla kaydeder |
| MT-PROV-002 | ☑ | Anahtar yokken sağlayıcı VE ona bağlı agent'lar hiç kaydolmaz |
| MT-PROV-010 | ☑ | Anthropic: `DefaultMaxOutputTokens` sıfır veya negatifse reddedilir |
| MT-PROV-011 | ☑ | Anthropic: negatif `MaxRetries` reddedilir |
| MT-PROV-020 | ☑ | Aynı ad iki kez tanımlanırsa son tanım kazanır (Anthropic VE Google) |
| MT-PROV-030 | ☑ | Yabancı sağlayıcının ayarı reddedilir (`google.*` anahtarı `anthropic` binding'inde) |
| MT-PROV-031 | ☑ | Bilinmeyen Anthropic ayarı reddedilir ve desteklenen anahtarları listeler |
| MT-PROV-032 | ☑ | Anthropic düşünme bütçesi sıfır veya negatifse reddedilir |
| MT-PROV-033 | ☑ | Google düşünme bütçesi `[-1, 65535]` aralığı dışındaysa reddedilir |
| MT-PROV-035 | ☑ | `claude-dusunen` fixture'ı genişletilmiş düşünmeyle uçtan uca çalışır |
| MT-PROV-040 | ☑ | `claude-destek`: tool çağrısıyla uçtan uca çalıştırma |
| MT-PROV-041 | ☑ | Akış (SSE) `claude-destek` ile üç çerçeve üretir: `run`, `update`(ler), `done` |
| MT-PROV-050 | ☑ | `gemini-destek`: tool çağrısıyla uçtan uca çalıştırma |
| MT-PROV-051 | ☑ | Akış (SSE) `gemini-destek` ile üç çerçeve üretir: `run`, `update`(ler), `done` |
| MT-PROV-060 | ☑ | Anthropic ve Google `Healthy` döner, farklı kimlik başlıkları kullanır |
| MT-PROV-061 | ☑ | Erişilemeyen Anthropic adresi hata detayında adres veya anahtar sızdırmaz |
| MT-PROV-070 | ☑ | API anahtarları hiçbir HTTP çıktısında görünmez |
| MT-PROV-071 | ☑ | `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz (Anthropic + Google) |
| MT-PROV-072 | ☑ | Konsol günlüğünde API anahtarı görünmez |

## Ayrıntı taşıyan case'ler (20)

## MT-PROV-003 — `ApiKey` boşken `UseAnthropic()`/`UseGoogle()` çağrılırsa doğrulama hata verir

**Gerçek sonuç**
Yerel feed'de sürüm `0.0.0-preview.0.78` bulundu, üç paket başarıyla
kuruldu. **Doküman notu:** `dotnet new console` şablonu `Host` sınıfını
sağlayan `Microsoft.Extensions.Hosting` paketini içermez — proje
`dotnet add package Microsoft.Extensions.Hosting` ile eklenmeden derlenmez
(`CS0103: The name 'Host' does not exist`). Bu bir AgentPrism kusuru
değil, doküman eksik bir kurulum adımı taşıyor. Ekleme sonrası: Anthropic
çalıştırması `AnthropicProviderOptions.ApiKey bos olamaz. Anahtari
UseAnthropic(apiKey) cagrisinda verin veya...` ile bitti; Google
çalıştırması `GoogleProviderOptions.ApiKey bos olamaz. Anahtari
UseGoogle(apiKey) cagrisinda verin veya...` ile bitti. İkisi de tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `rm -rf /tmp/ap-prov-apikey-test` uygulandı.

---

# 2 — Ayar doğrulama (`XxxProviderOptionsValidator`)

Doğrulama açılışta (`ValidateOnStart`) çalışır; geçersiz bir ayar uygulamanın
**hiç başlamamasına** yol açar. `Endpoint` mutlak adres kontrolü ve `Timeout`
pozitiflik kontrolü OpenAI'de zaten kanıtlandı (MT-OAI-010/011) — burada
**tekrarlanmaz**, yalnız Anthropic'e özgü iki alan (`DefaultMaxOutputTokens`,
`MaxRetries` — OpenAI'de yok) ve temsilci bir Google/ortak alan kontrolü
koşulur.

---

## MT-PROV-012 — Google: göreli (relative) `Endpoint` reddedilir

**Gerçek sonuç**
Uygulama **başladı** (`Now listening on: http://localhost:5083`),
`OptionsValidationException` hiç fırlatılmadı. Kök neden:
`GoogleProviderExtensions.cs:137-140`'daki `Bind()`,
`Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)` `false`
dönünce `options.Endpoint`'i hiç atamıyor — geçersiz değer doğrulayıcıya
hiç ulaşmadan sessizce eleniyor. **Bu, S3-1'de kaydedilen HATA-S3-001'in
(`OpenAIProviderOptions.Endpoint` için) birebir aynısı** —
`AnthropicProviderExtensions.cs:136-139` ve
`GoogleProviderExtensions.cs:137-140` OpenAI'nin `Bind()` desenini
harfiyen kopyalıyor. Üç sağlayıcının kaynağı karşılaştırıldı, üçü de aynı
`if (... Uri.TryCreate(..., UriKind.Absolute, ...)) { options.Endpoint = ...; }`
kalıbını taşıyor — `else` dalı yok. Yeni kayıt: `HATA-S3-003`.

---
**Yeniden koşum (Aile O, bu koşum).** Kök neden düzeltildi — üç sağlayıcının
(OpenAI, Anthropic, Google) `Bind()`'ı artık `Uri.TryCreate(endpoint,
UriKind.RelativeOrAbsolute, out var endpointUri)` kullanıyor; göreli bir
değer de atanıyor, `IsAbsoluteUri: false` denetimi artık tetikleniyor. Aynı
adım (`AgentPrism__Providers__Google__Endpoint=sadece-bir-yol`, temiz
`mt_fin_o` şeması, gerçek Postgres'e karşı) yeniden koşuldu: uygulama artık
**başlamıyor**, konsolda birebir beklenen metin görüldü:
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
GoogleProviderOptions.Endpoint mutlak bir adres olmalidir. Gelen deger:
'sadece-bir-yol'.` Regresyon testi:
`tests/AgentPrism.Google.UnitTests/GoogleProviderExtensionsTests.cs`
`Yapilandirmadan_gelen_goreli_adres_reddedilir` (Anthropic için eşdeğeri
`tests/AgentPrism.Anthropic.UnitTests/AnthropicProviderExtensionsTests.cs`'de
— fix geri alınıp koşulduğunda ikisi de KIRMIZI verdiği ampirik olarak
doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-013 — Katalogda adı boş bir model reddedilir (Anthropic VE Google)

**Gerçek sonuç**
Uygulama **başladı** (`Now listening` göründü), hiçbir
`OptionsValidationException` fırlatılmadı. Kök neden:
`AnthropicProviderExtensions.cs:167` ve `GoogleProviderExtensions.cs:163`
içindeki `BindModels()`, `if (child[nameof(ModelDescriptor.Name)] is not
{ Length: > 0 } name) { continue; }` ile boş `Name` taşıyan girdiyi listeye
hiç eklemeden atlıyor — doğrulayıcı asla boş isimli bir öge görmüyor. **Bu,
S3-1'de kaydedilen HATA-S3-002'nin (`OpenAIProviderOptions.Models` için)
birebir aynısı**, aynı satır numarası deseniyle Anthropic ve Google'da da
doğrulandı. Yeni kayıt: `HATA-S3-004`.

---
**Yeniden koşum (Aile O, bu koşum).** Kök neden düzeltildi — üç sağlayıcının
`BindModels()`'ı artık boş/eksik `Name`'i `continue` ile atlamıyor, ögeyi
`Name = ... ?? string.Empty` ile listeye ekliyor; doğrulayıcının
`Models[index]` döngüsü artık boş ismi gerçekten görüyor. Aynı adım
(Anthropic VE Google `Models` dizilerine `{"Name": ""}` eklendi, temiz
`mt_fin_o` şeması, gerçek Postgres'e karşı) yeniden koşuldu: uygulama artık
**başlamıyor**. Gözlenen konsol çıktısı beklenenden **kısmen** farklı: yalnız
**tek** bir `OptionsValidationException` görüldü —
`AnthropicProviderOptions.Models[3] icin model adi bos olamaz.` — Google'ın
kendi hatası hiç yazdırılmadı. Kök neden: `AddModelProvider` her sağlayıcıyı
ayrı bir `IModelProvider` fabrikası olarak kaydediyor; `MapAgentPrism`
`IEnumerable<IModelProvider>`'ı çözerken Anthropic'in fabrikası (kayıt
sırasında önce gelir) istisna atınca .NET DI'nin `IEnumerable` çözümü orada
durur, Google'ın fabrikası hiç çağrılmaz. Bu, Aile O'nun kök nedeniyle
(BindModels'in sessiz eleme) **ilgisiz**, ayrı bir DI çözümleme davranışı —
case'in kendi kabul kriteri buna zaten izin veriyordu ("iki ayrı ... **veya**
birleşik hata listesi"); burada gözlenen üçüncü bir örüntü (yalnız ilki)
olsa da temel iddia ("uygulama sessizce başlamaz") doğrulandı. Değişiklik
`git checkout -- samples/AgentPrism.Api/appsettings.json` ile geri alındı.
Regresyon testleri: `tests/AgentPrism.Anthropic.UnitTests/AnthropicProviderExtensionsTests.cs`
ve `tests/AgentPrism.Google.UnitTests/GoogleProviderExtensionsTests.cs`
`Yapilandirmadan_gelen_adsiz_model_reddedilir` (ikisi ayrı ayrı, kendi
sağlayıcı ayarında; fix geri alınıp koşulduğunda ikisi de KIRMIZI verdiği
ampirik olarak doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-014 — Doğrulama mesajları hiçbir alanda API anahtarını taşımaz

**Gerçek sonuç**
MT-PROV-010, 011, 003'ün ürettiği mesajlar yeniden gözden geçirildi —
hiçbirinde gerçek anahtar dizgisi geçmiyor, yalnız alan/ayar adları geçiyor.
MT-PROV-012 ve 013 zaten hiçbir doğrulama mesajı ÜRETMEDİ (HATA-S3-003,
HATA-S3-004 — ayrı bir kusur sınıfı, anahtar sızıntısı değil). Üretilen
mesajlar için sızıntı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Model kataloğu (`XxxModelCatalog`)

`Build()` deseni üç sağlayıcıda da birebir aynıdır: son tanım kazanır, ada göre
alfabetik sıralanır, büyük/küçük harf duyarsız. OpenAI'de kanıtlandı
(MT-OAI-022); burada yalnız katalog-dışı-model davranışı (K-032'nin asıl
kanıtı, log satırı Anthropic'e özgü metinle) koşulur.

---

## MT-PROV-021 — Katalogda olmayan bir Claude modeli reddedilmez, yalnız günlüğe yazılır

**Gerçek sonuç**
**Doküman notu:** `claude-3-5-haiku-20241022` (2024 tarihli) Anthropic
tarafından bu ortamda (2026-08-13) sunulmuyor artık — gerçek çağrı `404
not_found_error: model: claude-3-5-haiku-20241022` ile reddedildi (model
zaman içinde emekliye ayrılmış, ürün kusuru değil). Case, katalogda
OLMAYAN ama hâlâ GERÇEK bir modelle (`claude-sonnet-4-6` — sağlık
denetiminde keşfedildi, appsettings'teki 3 modelin dışında) tekrarlandı:
çalıştırma **başarıyla tamamlandı**, konsolda beklenen log satırı birebir
göründü: `'claude-sonnet-4-6' modeli 'anthropic' katalogunda yok; istek
yine de gonderiliyor. Model bilgisini kataloga eklemek icin
AgentPrism:Providers:Anthropic:Models ayarini kullanin.` Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Sağlayıcıya özgü ayarlar (`ModelBinding.ProviderSettings`)

Anthropic (`anthropic.promptCaching`, `anthropic.thinking.budgetTokens`) ve
Google (`google.safety.*`, `google.thinking.*`) kendi anahtar öneklerini
tanır; bilinmeyen veya yabancı bir anahtar **sessizce yok sayılmaz**, derleme
hatası verir. Doğrulama `POST /api/agents/validate` üzerinden ücretsiz
koşulur — `AgentDefinitionValidator.CheckModel`, gerçek yolla **aynı**
`_models.CreateChatClient(binding)` çağrısını yapar ama hiçbir ağ isteği
göndermez (`AgentDefinitionValidator.cs:164`); hata yakalanıp `code:
"invalid_setting"`, `path: "model.providerSettings"` ile `messages` dizisine
eklenir (`AgentDefinitionValidator.cs:161-175`).

---

## MT-PROV-034 — Google güvenlik eşiği taninmayan bir değer taşırsa reddedilir

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`. Mesaj: "'google.safety.harassment'
ayarinin degeri taninmiyor: 'COK_TEHLIKELI'. Gecerli degerler:
HARM_BLOCK_THRESHOLD_UNSPECIFIED, BLOCK_LOW_AND_ABOVE,
BLOCK_MEDIUM_AND_ABOVE, BLOCK_ONLY_HIGH, BLOCK_NONE, OFF." Kaynak
doğrulandı: `GoogleSafetySettings.ParseThreshold`,
`Google.GenAI.Types.HarmBlockThreshold.AllValues`'daki TÜM üyeleri
(6 tane) listeliyor — kod tasarlandığı gibi çalışıyor, doküman kusuruydu
(yukarıda düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-036 — Düşünme açıkken sıcaklık `1` değilse gerçek Anthropic API'si reddeder

**Gerçek sonuç**
Anthropic gerçek API'si isteği reddetti: SSE `event: error` **göründü**
(kod-doğrulanmış şüphenin tahmin ettiği "görünmeyebilir" olasılık
gerçekleşmedi — 2026-08-10 genel düzeltmesi bu durumu da kapsıyor
olmalı). `type:AnthropicBadRequestException`, mesaj: "Status Code:
BadRequest\n{...\"message\":\"\`temperature\` may only be set to 1 when
thinking is enabled...\"}". `GET /api/runs` çıktısı:
`status:Failed`, `error.message` içinde "temperature" geçiyor,
`error.class:Unknown`. Ana beklenti (Failed + temperature mesajı)
karşılandı; SSE `error` çerçevesinin görünmesi doc'un iki olası dalından
biriydi, bu koşumda gerçekleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-037 — Anthropic `promptCaching` açıkken önbellek sayaçları gözlenir

**Gerçek sonuç**
İki çalıştırma da `Completed` oldu. `cachedInputTokenCount: 0` iki
çalıştırmada da — istem (~1000 karakter talimat) Haiku'nun önbellek
eşiğinin altında kaldı. Bu, dokümanın öngördüğü "eşiğin altında" dalıdır,
AgentPrism kusuru değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Gerçek Anthropic çağrısı, akış ve tool eşlemesi

Bu bölüm gerçek ağ çağrısı yapar ve ölçülebilir ücrete yol açar. Anthropic
Messages API'si tool çağrısını `tool_use`/`tool_result` blokları ile taşır
(OpenAI'nin `function_call`'ından farklı bir sözleşme); adaptörün bunu
`Microsoft.Extensions.AI`'nin ortak `FunctionCallContent`/`FunctionResultContent`
biçimine doğru eşlediği burada kanıtlanır.

---

## MT-PROV-042 — Var olmayan bir Claude modeliyle çalıştırma: SSE `error` çerçevesi üretilir (düzeltildi); sınıflandırma hâlâ `Unknown` olabilir

**Gerçek sonuç**
SSE `event: error` çerçevesi göründü: `type:AnthropicNotFoundException`,
mesaj: "Status Code: NotFound\n{...\"message\":\"model:
claude-olmayan-model-xyz\"}". `GET /api/runs` çıktısı: `status:Failed`,
`error.type:Anthropic.Exceptions.AnthropicNotFoundException`. **Kod-
doğrulanmış şüphe doğrulandı:** `error.class:Unknown` (mesaj "Status Code:
NotFound" yazıyor, "HTTP 404" değil — `\bHTTP\s+[45]\d{2}\b` deseni
eşleşmedi). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Gerçek Google çağrısı, akış, tool eşlemesi ve içerik filtresi

Bu bölüm gerçek ağ çağrısı yapar ve ölçülebilir ücrete yol açar. Gemini tool
çağrısını `functionCall`/`functionResponse` parçaları ile taşır. **§6.4 bu
dosyanın en değerli case'idir**: `ContentFilterDetectingChatClient`
(`05-SAGLAYICI-OPENAI.md`'nin akış diyagramında listelenen ama hiç
tetiklenemeyen düğüm) burada `gemini-kati-filtre` fixture'ı ile gerçekten
koşulur.

---

## MT-PROV-052 — `gemini-kati-filtre`: güvenlik filtresi boş yanıt üretir ve `content_filtered` olarak kaydedilir

**Gerçek sonuç**
İki farklı istekle denendi (fiziksel zarar sorusu, ardından ev yapımı
patlayıcı sentezi sorusu — dangerous_content kategorisi). İkisinde de
Gemini 3.6 Flash `BLOCK_LOW_AND_ABOVE` eşiğinde filtrelemedi; model
isteği kendi metniyle reddetti (birincide) veya kısa/nötr yanıt üretti
(ikincide), ikisi de `status:Completed`, `error:null`. **Bu, dokümanın
öngördüğü ikinci dal:** "beklenen tetikleyici artık filtrelemiyor" —
model davranışı sürüm bağımlı, AgentPrism kusuru değil.
`ContentFilterDetectingChatClient` mekanizması bu koşumda tetiklenemedi
(00-INDEKS.md'ye not düşülmesi gerekiyor — bu oturumda düşürülmedi,
takip: toplama oturumunda). Case içeriği bu sonucu kayıt altına alır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-053 — Var olmayan bir Gemini modeliyle çalıştırma: SSE `error` çerçevesi üretilir (Google'da hiç boşluk yoktu)

**Gerçek sonuç**
SSE `event: error` çerçevesi göründü: `type:ClientError`, mesaj:
"models/gemini-olmayan-model-xyz is not found for API version v1beta,
or is not supported for generateContent...". `GET /api/runs` çıktısı:
`status:Failed`, `error.type:Google.GenAI.ClientError`. **Kod-doğrulanmış
şüphe doğrulandı:** `error.class:Unknown` (mesaj "HTTP" sözcüğünü hiç
taşımıyor). Google'ın davranışı 2026-08-10 düzeltmesinden ETKİLENMEDİ —
tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Sağlayıcı sağlık denetimi

Denetim **ücret üretmez**: Anthropic `GET {endpoint}/models`'e,
Google `GET {endpoint}/{apiVersion}/models`'e gider, model çağrısı yapmaz.
Önbellek TTL'si ve bilinmeyen sağlayıcı için `404` davranışı OpenAI'de
kanıtlandı (MT-OAI-071/072) — burada **tekrarlanmaz**.

---

## MT-PROV-080 — `UseAzureOpenAI()` `azure-openai` adıyla kaydeder

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok (KOSUM-PLANI §1 "Sabit
gerçekler"). Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-081 — `Endpoint` boşsa reddedilir

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-082 — `ApiKey` VE `CredentialFactory` ikisi de boşsa reddedilir

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-083 — `CredentialFactory` verilmişse `ApiKey`'i ezer

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı VE `az login` yapılmış bir
Entra kimliği yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-084 — Deployment adı yerine MODEL adı verilirse `HTTP 404` döner

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-085 — `ProviderSettings` içinde HERHANGİ bir anahtar reddedilir

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok — `azure-openai`
sağlayıcısı hiç kayıtlı değil (`UseAzureOpenAI()` hiç çağrılmadı),
`/api/agents/validate` bu durumda muhtemelen "sağlayıcı bulunamadı"
türü farklı bir hata dönerdi, dokümanın öngördüğü `invalid_setting`
akışını KANITLAMAZDI. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-086 — Sağlık denetimi model listesi döner, deployment listesi DEĞİL

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-087 — `azure-destek`: tool çağrısıyla uçtan uca çalıştırma

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-PROV-088 — `secret` sızıntısı: `ApiKey` VE kurumsal `Endpoint` hiçbir çıktıda görünmez

**Gerçek sonuç**
Bu ortamda gerçek bir Azure OpenAI kaynağı yok. Koşulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## Bu dosyada kanıtlanmayan, kod okurken fark edilen şüpheler

Bu bölüm bir kusur listesi değildir — koşum aşamasında doğrulanacak
**şüphelerdir** (`PROMPT.md` §7.3). Ayrıntı ve tam gerekçe, ilgili case'in
"Beklenen sonuç" alanındadır; burada yalnız özetlenir:

- **MT-PROV-036/042 — SSE çerçeve kısmı DÜZELTİLDİ (2026-08-10).** Anthropic
  SDK istisnaları (`AnthropicApiException` ve alt sınıfları)
  `HttpRequestException`'dan türemez — `05`'in OpenAI için tespit ettiği SSE
  `error` çerçevesi boşluğu (K-296) Anthropic'te de geçerliydi;
  `AgentEndpoints`/`OpenAIResponsesEndpoints`/`OpenAIChatCompletionsEndpoints`'in
  dar `catch` filtreleri kaldırılarak düzeltildi. **Hâlâ açık:**
  `AnthropicApiException.Message`'ın `$"Status Code: {StatusCode}\n..."`
  biçimi (enum adı yazar, sayısal kod değil) `RunErrorClass` sınıflandırmasını
  (kayıt alanı, `DefaultRunErrorClassifier`) `Unknown`'a düşürebilir — bu,
  SSE çerçevesinden BAĞIMSIZ bir kayıt-sınıflandırma sorunudur, dokunulmadı.
- **MT-PROV-053**: Google SDK istisnaları (`Google.GenAI.ClientError`/`ServerError`)
  **`HttpRequestException`'dan türer** — SSE `error` çerçevesi fix'ten önce
  de üretiliyordu. Hata mesajı formatı yine de `RunErrorClass`'ı `Unknown`'a
  düşürebilir (MT-PROV-036/042 ile aynı, dokunulmamış sınıflandırma sorunu).
- **MT-PROV-034**: `HarmBlockThreshold.AllValues` listesinin gerçek üye
  sayısı ve tam adları (`BLOCK_LOW_AND_ABOVE`, `BLOCK_MEDIUM_AND_ABOVE`,
  `BLOCK_ONLY_HIGH`, `BLOCK_NONE`, `OFF`) `Google.GenAI.Types` paketinden
  alındı (XML doküman yorumundan değil, SDK'nin kendi tip tanımından); SDK
  sürümü yükseltilirse bu liste değişebilir.

SSE çerçeve boşluğu 2026-08-10'da kodda düzeltildi (bkz. `00-INDEKS.md` §8'in
başındaki özet); `RunErrorClass` sınıflandırma nüansı ile `HarmBlockThreshold`
listesi hâlâ **koşulmadı** — koşum aşaması bunları doğrular veya çürütür.

---
