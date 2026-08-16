# 06 — Sağlayıcı: Anthropic, Google, Azure OpenAI (`PROV`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../06-SAGLAYICI-DIGER.md`](../../06-SAGLAYICI-DIGER.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-PROV-001 — `UseAnthropic()`/`UseGoogle()` doğru adlarla kaydeder

**Gerçek sonuç**
`anthropic`: 3 model (`claude-haiku-4-5-20251001`, `claude-opus-5`,
`claude-sonnet-5`), alfabetik sıralı. `google`: 3 model
(`gemini-3.1-flash-lite`, `gemini-3.1-pro-preview`, `gemini-3.6-flash`),
alfabetik sıralı. `azure-openai` yok. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-002 — Anahtar yokken sağlayıcı VE ona bağlı agent'lar hiç kaydolmaz

**Gerçek sonuç**
Anthropic anahtarı env var ile boş verilerek yeniden başlatıldı (KOSUM-PLANI
§2.2 — `user-secrets remove` yerine env var isolation; paylaşılan
`user-secrets` deposu bu yüzden hiç dokunulmadı). Uygulama hatasız başladı.
Sağlayıcı listesi: `['google','openai','openai-responses','openrouter']` —
`anthropic` yok, `google` hâlâ var. Agent listesinde `claude-destek` ve
`claude-dusunen` yok, diğerleri (gemini-destek dahil) hâlâ var. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

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

## MT-PROV-010 — Anthropic: `DefaultMaxOutputTokens` sıfır veya negatifse reddedilir

**Gerçek sonuç**
Uygulama başlamayı reddetti: `Unhandled exception.
Microsoft.Extensions.Options.OptionsValidationException:
AnthropicProviderOptions.DefaultMaxOutputTokens sifirdan buyuk olmalidir.
Anthropic Messages API'si \`max_tokens\` alanini zorunlu tutar. Gelen
deger: 0.` — HTTP portu hiç açılmadı (`curl` bağlantı reddetti). Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-011 — Anthropic: negatif `MaxRetries` reddedilir

**Gerçek sonuç**
Uygulama başlamayı reddetti: `Unhandled exception.
Microsoft.Extensions.Options.OptionsValidationException:
AnthropicProviderOptions.MaxRetries negatif olamaz. Gelen deger: -1.` Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-PROV-020 — Aynı ad iki kez tanımlanırsa son tanım kazanır (Anthropic VE Google)

**Gerçek sonuç**
`3 ['IKINCI TANIM']` — dizi hâlâ 3 öge, `claude-haiku-4-5-20251001` için
tek `displayName` ve o da "IKINCI TANIM". Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-PROV-030 — Yabancı sağlayıcının ayarı reddedilir (`google.*` anahtarı `anthropic` binding'inde)

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`, `path:model.providerSettings`.
Mesaj: "...su anahtarlar 'anthropic' saglayicisina ait degil:
google.safety.harassment. ModelBinding.ProviderSettings yalnizca
ModelBinding.Provider alanindaki saglayicinin anahtarlarini tasiyabilir;
saglayici degistirildiginde eski ayarlar temizlenmelidir. Desteklenen
anahtarlar: anthropic.promptCaching, anthropic.thinking.budgetTokens."
Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-031 — Bilinmeyen Anthropic ayarı reddedilir ve desteklenen anahtarları listeler

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`. Mesaj: "...su anahtarlar
taninmiyor: anthropic.thinkingBudget. Desteklenen anahtarlar:
anthropic.promptCaching, anthropic.thinking.budgetTokens." Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-032 — Anthropic düşünme bütçesi sıfır veya negatifse reddedilir

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`. Mesaj: "'anthropic.thinking.
budgetTokens' sifirdan buyuk olmalidir. Gelen deger: 0." Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-033 — Google düşünme bütçesi `[-1, 65535]` aralığı dışındaysa reddedilir

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`. Mesaj: "'google.thinking.
budgetTokens' degeri [-1, 65535] araliginda olmalidir (-1 modele birakir,
0 dusunmeyi kapatir). Gelen deger: 100000." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-PROV-035 — `claude-dusunen` fixture'ı genişletilmiş düşünmeyle uçtan uca çalışır

**Gerçek sonuç**
Yanıt metninde "408" birden fazla kez geçti (adım adım hesap + özet: "17 ×
24 = 408" ve "Sonuç: 17 × 24 = 408"). Genişletilmiş düşünme (`thinking`
bloğu) da akışta gözlendi. Tam beklendiği gibi.

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

## MT-PROV-040 — `claude-destek`: tool çağrısıyla uçtan uca çalıştırma

**Gerçek sonuç**
Yanıt metni "ORD-1001" içerdi ("...siparişiniz **kargoya verildi**.
Tahmini teslim süresi **2 gün**..."). `status:Completed`,
`totalTokens:913` (pozitif). `get_order_status` tam bir kez çağrıldı
(`toolu_01Ks96...`), argüman `{"orderId":"ORD-1001"}`, sonuç doğru şekilde
`functionResult`'a eşlendi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-041 — Akış (SSE) `claude-destek` ile üç çerçeve üretir: `run`, `update`(ler), `done`

**Gerçek sonuç**
Çerçeve sayımı: `1 event: done`, `1 event: run`, `8 event: update`, `0
event: error`. Tam beklendiği gibi (içerik-type ayrıca kontrol edilmedi,
önceki case'lerde zaten doğrulandı, tekrar edilmedi — bütçe gerekçesiyle).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-PROV-050 — `gemini-destek`: tool çağrısıyla uçtan uca çalıştırma

**Gerçek sonuç**
Yanıt metni "ORD-1001" içerdi. `status:Completed`, `usage.totalTokens:447`
(pozitif). Olay listesi (`GET /api/runs/{id}/events`) `tool.invoking` /
`tool.invoked` ile `get_order_status` tam bir kez çağrıldığını gösterdi
(`orderId=ORD-1001` → "ORD-1001 numarali siparis kargoya verildi..."). Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-051 — Akış (SSE) `gemini-destek` ile üç çerçeve üretir: `run`, `update`(ler), `done`

**Gerçek sonuç**
Çerçeve sayımı: `1 event: done`, `1 event: run`, `3 event: update`, `0
event: error`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-PROV-060 — Anthropic ve Google `Healthy` döner, farklı kimlik başlıkları kullanır

**Gerçek sonuç**
`anthropic`: `status:Healthy`, `latency:00:00:00.64`, 10 gerçek model
kimliği (appsettings'teki 3'ün dışında da modeller var — katalog
doğrulama listesi değil, K-032 doğrulandı). `google`: `status:Healthy`,
`latency:00:00:00.37`, 48 gerçek model kimliği, hiçbirinde `models/`
öneki yok (temiz). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-061 — Erişilemeyen Anthropic adresi hata detayında adres veya anahtar sızdırmaz

**Gerçek sonuç**
`status:Unhealthy`, `detail:"Baglanti hatasi (ConnectionError)."` — ne
sahte anahtar (`sk-ant-cok-gizli-test-anahtari-12345`) ne de sahte adres
(`127.0.0.1:59999`) `detail` içinde göründü. Tüm konsol logu da tarandı
(`grep -c`): `0` eşleşme. Tam beklendiği gibi. Program.cs `git checkout
--` ile geri alındı, örnek uygulama yeniden derlendi (0 uyarı, 0 hata).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — `secret` sızıntısı

`ApiKey` hiçbir çıktıda, hiçbir günlükte, hiçbir hata mesajında görünmemelidir
(K-059). MT-PROV-014 doğrulama mesajlarını zaten kapsadı; bu bölüm çalışma
zamanı uçlarını ve konsolu kapsar.

---

## MT-PROV-070 — API anahtarları hiçbir HTTP çıktısında görünmez

**Gerçek sonuç**
Dört uç da (`/api/models`, `/api/models/health`,
`/api/models/health/anthropic`, `/api/models/health/google`) `0`
(temiz) döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-071 — `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz (Anthropic + Google)

**Gerçek sonuç**
`configuration` dizisinde `key:"AgentPrism:Providers:Anthropic:ApiKey"`
ve `key:"AgentPrism:Providers:Google:ApiKey"` — her biri tam bir kez,
ikisi de `resolved:true`, `hint:null`. İki sağlayıcı bağımsız girdiler
(OpenAI'nin tekilleştirilmiş girişinden farklı olarak). Hiçbir `key`
alanı gerçek anahtar dizgisi taşımıyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PROV-072 — Konsol günlüğünde API anahtarı görünmez

**Gerçek sonuç**
Uygulama konsol çıktısı `/tmp/ap-prov-console.log`'a yönlendirilerek
başlatıldı; `claude-destek` ve `gemini-destek` çalıştırıldı (MT-PROV-040/
050'nin tekrarı). Gerçek anahtarlar için `grep -c` sayımı: `0` (temiz).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Azure OpenAI

> ⏭ **ATLA — Azure kimliği yok.** `Sabit gerçekler` tablosuna göre bu ortamda
> gerçek bir Azure OpenAI kaynağı **yoktur**. Aşağıdaki her case yine de
> koddan doğrulanarak yazılmıştır — kullanıcı sonradan bir Azure kaynağı
> açarsa bu bölüm hazırdır. Her case'in kendi `⏭ ATLA` satırı vardır
> (`PROMPT.md` §6).

`AgentPrism.Azure` aynı `AgentPrism.OpenAI` boru hattından geçer
(`UseFunctionInvocation`, `OpenTelemetry`, devre kesici, içerik filtresi —
hepsi `ModelProviderRegistry` düzeyinde, bedava). Kendine özgü tek yüzey:
deployment≠model ayrımı, sıfır `ProviderSettings` ve iki kimlik doğrulama
yolu (API anahtarı / Microsoft Entra).

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
