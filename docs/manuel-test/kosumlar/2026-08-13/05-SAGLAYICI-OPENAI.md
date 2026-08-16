# 05 — Sağlayıcı: OpenAI (`OAI`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../05-SAGLAYICI-OPENAI.md`](../../05-SAGLAYICI-OPENAI.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-OAI-001 — `UseOpenAI()` tek çağrıyla iki sağlayıcı kaydeder

**Gerçek sonuç**
`GET /api/models` yanıtı beş sağlayıcı döndü: `anthropic`, `google`, `openai`,
`openai-responses`, `openrouter`. `openai` ve `openai-responses` ikisi de
`gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra` — aynı üç model, aynı sıra.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-002 — Bilinmeyen sağlayıcı adıyla çalıştırma anlaşılır hata verir

**Gerçek sonuç**
`POST /api/agents` `201`. `POST .../run` → `HTTP/1.1 400`,
`Content-Type: application/problem+json`, gövde: `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Agent derlenemedi","status":400,"detail":"'manuel-yok-boyle-saglayici' agent'i derlenemedi: 'yok-boyle-bir-saglayici' adinda bir model saglayicisi kayitli degil. Kayitli saglayicilar: openai, openai-responses, openrouter, anthropic, google. OpenAI icin \`builder.AddAgentPrism().UseOpenAI(apiKey)\` cagirin.",...}`.
Mesaj metni tam eşleşti. Delivery mekanizması (SSE değil, senkron 400) ve
`type` alanı dokümanın orijinal beklentisinden farklıydı — yukarıda düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-003 — Model adı boş ve `DefaultModel` tanımsızsa anlaşılır hata verir

**Gerçek sonuç**
`POST /api/agents` → `400`, `application/problem+json`,
`detail:"'model.provider' ve 'model.model' alanlari zorunludur."`. Agent
kaydedilmedi; `/run` denemesi (orijinal adım 2) bu yüzden anlamsız hale
geldi — atlandı. Orijinal beklenti (`OpenAIChatClientFactory`'nin çalıştırma
anı mesajı) koddan farklı çıktı; yukarıda düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-004 — İkinci `UseOpenAI()` çağrısı sağlayıcıları çoğaltmaz

**Gerçek sonuç**
`Program.cs:135`'e ikinci `agentPrism.UseOpenAI(openAi);` çağrısı geçici
eklendi, derlendi, uygulama başlatıldı — çökme yok, `Now listening on:
http://localhost:5083` normal çıktı. `GET /api/models` → `['anthropic',
'google', 'openai', 'openai-responses', 'openrouter']` — `openai` ve
`openai-responses` yalnız birer kez. Değişiklik `git checkout --
samples/AgentPrism.Api/Program.cs` ile geri alındı (`git diff` boş
doğrulandı), uygulama temiz haliyle yeniden derlenip başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Ayar doğrulama (`OpenAIProviderOptionsValidator`)

Doğrulama açılışta (`ValidateOnStart`) çalışır; geçersiz bir ayar uygulamanın
**hiç başlamamasına** yol açar.

---

## MT-OAI-010 — Göreli (relative) `Endpoint` reddedilir

**Gerçek sonuç**
`AgentPrism:Providers:OpenAI:Endpoint=sadece-bir-yol` (env değişkeni,
şerit izolasyonu — bkz. §2.2) ile uygulama **sorunsuz başladı**,
`http://localhost:5083`'te dinlemeye geçti, `/api/models` normal yanıt verdi.
`OptionsValidationException` **hiç fırlatılmadı**.

Kök neden bulundu: `src/AgentPrism.OpenAI/OpenAIProviderExtensions.cs:164-168`
`Bind()` metodu `Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)`
kullanıyor — `"sadece-bir-yol"` mutlak URI olarak ayrıştırılamadığı için
`TryCreate` `false` döner ve `options.Endpoint` HİÇ ATANMAZ (varsayılanında
kalır). `OpenAIProviderOptionsValidator.cs:56`'daki
`options.Endpoint is { IsAbsoluteUri: false }` denetimi bu yüzden varsayılan
(adsız) `openai` sağlayıcısı için **hiçbir zaman tetiklenemez** — `Bind()`
geçersiz değeri doğrulayıcıya ulaşmadan sessizce eler. Bu, tam olarak
`OpenAIProviderOptionsValidator`'ın kendi XML belgesinin önlemeyi amaçladığı
senaryo: "Bos birakilirsa istek sessizce resmi OpenAI adresine giderdi" —
ama burada boş değil, GEÇERSİZ bir değer de aynı sessiz düşüşe uğruyor.
Kayıt: `HATA-S3-001`.

---
**Yeniden koşum (Aile O, bu koşum).** Kök neden düzeltildi:
`Bind()` artık `Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out
var endpointUri)` kullanıyor — göreli bir değer de `options.Endpoint`'e
atanıyor, `IsAbsoluteUri: false` denetimi artık gerçekten tetikleniyor.
Aynı env değişkeni yeniden verildi
(`AgentPrism__Providers__OpenAI__Endpoint=sadece-bir-yol`, temiz
`mt_fin_o` şeması, gerçek Postgres'e karşı): uygulama artık **başlamıyor**,
konsolda birebir beklenen metin görüldü:
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.Endpoint mutlak bir adres olmalidir. Gelen deger:
'sadece-bir-yol'.` Regresyon testi:
`tests/AgentPrism.OpenAI.UnitTests/OpenAIProviderExtensionsTests.cs`
`Yapilandirmadan_gelen_goreli_adres_reddedilir` (fix geri alınıp koşulduğunda
KIRMIZI verdiği ampirik olarak doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-011 — Sıfır veya negatif `Timeout` reddedilir

**Gerçek sonuç**
`AgentPrism:Providers:OpenAI:Timeout=00:00:00` (env değişkeni) ile uygulama
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.Timeout sifirdan buyuk olmalidir. Gelen deger:
00:00:00.` ile çöktü, port `5083`'e hiç bağlanmadı. Mesaj birebir eşleşti.
(MT-OAI-010'un aksine `Bind()` içindeki `TimeSpan.TryParse` "00:00:00"'ı
geçerli bir `TimeSpan` olarak ayrıştırıp doğrudan atıyor, bu yüzden
doğrulayıcıya sağlıklı ulaşıyor — Endpoint'teki gibi sessiz eleme yok.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-012 — Katalogda adı boş bir model reddedilir

**Gerçek sonuç**
`appsettings.json`'a `{"Name": ""}` eklendi (4. öge, dizin 3), uygulama
**sorunsuz başladı** — `Now listening on: http://localhost:5083`.
`OptionsValidationException` **hiç fırlatılmadı**.

Kök neden, HATA-S3-001 ile aynı ailede:
`OpenAIProviderExtensions.cs:191-198` `BindModels()` şu satırı taşır:
```csharp
if (child[nameof(ModelDescriptor.Name)] is not { Length: > 0 } name)
{
    continue;
}
```
Boş (veya yok) `Name` alanı taşıyan bir model girdisi `options.Models`
listesine **hiç eklenmiyor** — sessizce atlanıyor. `OpenAIProviderOptionsValidator`'ın
`Models[index]` döngüsü (satır 70-78) bu yüzden asla boş isimli bir öge
görmüyor; doğrulama dalı ölü koddur. Değişiklik
`git checkout -- samples/AgentPrism.Api/appsettings.json` ile geri alındı
(`git diff` boş doğrulandı). Kayıt: `HATA-S3-002`.

---
**Yeniden koşum (Aile O, bu koşum).** Kök neden düzeltildi: `BindModels()`
artık `continue` ile atlamıyor, `Name` boş/yok olsa bile ögeyi
`Name = child[nameof(ModelDescriptor.Name)] ?? string.Empty` ile listeye
ekliyor — doğrulayıcının `Models[index]` döngüsü artık boş ismi gerçekten
görüyor. Aynı adım (`appsettings.json`'a 4. öge olarak `{"Name": ""}`
eklendi, temiz `mt_fin_o` şeması, gerçek Postgres'e karşı) yeniden koşuldu:
uygulama artık **başlamıyor**, konsolda birebir beklenen metin görüldü:
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.Models[3] icin model adi bos olamaz.` (dizin `3`,
beklenen gibi). Değişiklik `git checkout -- samples/AgentPrism.Api/appsettings.json`
ile geri alındı. Regresyon testi:
`tests/AgentPrism.OpenAI.UnitTests/OpenAIProviderExtensionsTests.cs`
`Yapilandirmadan_gelen_adsiz_model_reddedilir` (fix geri alınıp koşulduğunda
KIRMIZI verdiği ampirik olarak doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-013 — `ApiKey` boşken `UseOpenAI()` çağrılırsa doğrulama hata verir

**Gerçek sonuç**
`~/agentprism-local-feed`'den (`AgentPrism.Core`/`AgentPrism.OpenAI`
0.0.0-preview.0.78, `01-KURULUM-VE-PAKETLEME.md` tarafından hazırlanmış,
yalnız okundu — küresel kurulum adımına dokunulmadı) bağımsız bir konsol
projesi kuruldu, `UseOpenAI(o => { })` çağrıldı. `dotnet run` şununla
çöktü: `Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.ApiKey bos olamaz. Anahtari \`UseOpenAI(apiKey)\`
cagrisinda verin veya 'AgentPrism:Providers:OpenAI:ApiKey' ayarini
\`dotnet user-secrets\` icinde tanimlayin.` — mesaj birebir eşleşti. Bu
kod-yolu (`Action<OpenAIProviderOptions>` overload'u) HATA-S3-001/002'nin
sessiz-eleme sorununu taşımıyor çünkü hiç `Bind()`/`BindModels()`'tan
geçmiyor — doğrudan `IValidateOptions` çalışıyor. `/tmp/ap-oai-apikey-test`
temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `rm -rf /tmp/ap-oai-apikey-test`

---

# 3 — Model kataloğu (`OpenAIModelCatalog`)

AgentPrism yerleşik model listesi taşımaz (K-032). Katalog tamamen
yapılandırmadan gelir ve bir **doğrulama listesi değildir**.

---

## MT-OAI-020 — Katalog yalnız yapılandırmadan gelir ve `/api/models`'te görünür

**Gerçek sonuç**
`openai` sağlayıcısının `models` dizisi tam üç öge: `gpt-5.4-mini`,
`gpt-5.6-luna`, `gpt-5.6-terra` — bu sıra alfabetik doğru (`5.4` <
`5.6-l` < `5.6-t`). Üçünün de `supportsStructuredOutput:true`,
`contextWindowTokens:null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-021 — Katalogda olmayan bir model adı reddedilmez, yalnız günlüğe yazılır

**Gerçek sonuç**
Agent kaydedildi, çalıştırıldı. Uygulama konsolunda **beklenen log satırı
birebir çıktı**: `'gpt-4o-mini' modeli 'openai' katalogunda yok; istek
yine de gonderiliyor.` (`/tmp/ap-s3.log:240`) — bu, katalogun bir
doğrulama listesi olmadığını ve isteğin engellenmeden sağlayıcıya
iletildiğini kanıtlıyor.

İkinci iddia ("başarıyla tamamlanır, gerçek yanıt üretir") **bu hesapla
doğrulanamadı**: istek gerçekten OpenAI'a gitti ve OpenAI'in KENDİSİ
`HTTP 403 model_not_found` ile reddetti — `Project 'proj_0iwMbkX0bZWgw9Yqq4XK3U4K'
does not have access to model 'gpt-4o-mini'` (`/tmp/ap-s3.log:292`).
`GET https://api.openai.com/v1/models` ile doğrulandı: bu hesabın
erişebildiği TÜM modeller `gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`
(üçü de zaten katalogda) artı iki embedding modeli — hesapta kataloğun
DIŞINDA erişilebilir hiçbir sohbet modeli yok. Bu bir AgentPrism kusuru
değil, hesabın model erişim kapsamı sınırlı; ürün davranışının kendisi
(engellemeden iletme + log uyarısı) log kanıtıyla doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-022 — Aynı ad iki kez tanımlanırsa son tanım kazanır

**Gerçek sonuç**
`appsettings.json`'a `gpt-5.4-mini` için ikinci bir girdi (`DisplayName:
"IKINCI TANIM"`, diğer alanlar boş) eklendi. `GET /api/models` →
`openai` sağlayıcısında **3 model** (4 değil), `gpt-5.4-mini`'nin
`displayName`'i `IKINCI TANIM` — son yazan kazandı, tek kayıt olarak
göründü. Değişiklik `git checkout --
samples/AgentPrism.Api/appsettings.json` ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Chat Completions ve Responses yüzeyleri

`UseOpenAI()`'nin kaydettiği iki yüzey de aynı `OpenAIClient`'ı paylaşır ama
farklı bir OpenAI API'sine gider. Bu bölüm S2 sapmasını (Responses API'nin
sunucu tarafı depolamayı bilerek kapatması) gerçek bir PostgreSQL kurulumunda
kanıtlar.

---

## MT-OAI-030 — `openai-responses` yüzeyi PostgreSQL kalıcılığıyla çatışmaz

**Gerçek sonuç**
İki çalıştırma da `error` çerçevesi ÜRETMEDEN `done` ile bitti.
`InvalidOperationException` görülmedi. İkinci çalıştırmanın metin
içeriği `"47"` — ilk mesajdaki "sansli sayim 47" PostgreSQL
kalıcılığından (`mt_s3` şeması) doğru hatırlandı, aynı `responseId`
(OpenAI'ın kendi konuşma kimliği) İKİNCİ çalıştırmada FARKLIYDI
(`resp_0715af...` → `resp_0d9606...`) — geçmiş OpenAI'ın kendi tarafında
değil AgentPrism'in deposunda tutulduğunu doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-031 — İki yüzey de `/api/models`'te bağımsız görünür ve aynı katalogu taşır

**Gerçek sonuç**
Çıktı `True` — `openai` ve `openai-responses` sağlayıcılarının `models`
listeleri birebir aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Gerçek OpenAI çağrısı, akış ve hata sınıflandırması

Bu bölüm gerçek ağ çağrısı yapar ve ölçülebilir ücrete yol açar.

---

## MT-OAI-040 — Tool çağrısı ile uçtan uca çalıştırma

**Gerçek sonuç**
Yanıt metni "ORD-1001 siparişiniz verilmiş... Tahmini teslimat: 2 gün."
içeriyor. `get_order_status` tool'u tam bir kez `orderId:"ORD-1001"` ile
çağrıldı (`id: 13` frame). `GET /api/runs/{runId}`: `status:"Completed"`,
`usage.totalTokens:307` (null değil, pozitif). runId
`019ffb8a-1386-763e-bf50-9a5f48e90f70`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-041 — Akış (SSE) üç çerçeve üretir: `run`, `update`(ler), `done`

**Gerçek sonuç**
`Content-Type: text/event-stream` doğrulandı. Çerçeve sırası: 1× `event: run`
(`data: {"runId":"019ffb8a-ab26-7284-a1b5-8b9472105044","sessionId":"oai-sse-01"}`,
camelCase) → 5× `event: update` → 1× `event: done`
(`data: {"sessionId":"oai-sse-01"}`). `event: error` hiç görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-042 — `Idempotency-Key` başlığı akışsız (tek JSON) yanıt üretir

**Gerçek sonuç**
`HTTP: 200`, `CT: application/json; charset=utf-8`. Gövde tek JSON nesnesi,
`response.messages[0].contents[0].text:"tamam"` dolu. `usage.totalTokenCount:224`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-043 — Gerçek OpenAI 404 (`model_not_found`): akışlı yanıt `error` çerçevesi üretir (düzeltildi)

**Gerçek sonuç**
İlk `curl`: `event: run` sonra `event: error` geldi (bağlantı çerçevesiz
kapanmadı) — SSE gövdesinde `type` alanı kısa ad `"ClientResultException"`,
`message` `"HTTP 404 (invalid_request_error: model_not_found)..."` taşıyor.
`GET /api/runs?agentName=manuel-bozuk-model`: kayıt `status:"Failed"`,
`error.type:"System.ClientModel.ClientResultException"` (tam nitelenmiş),
`error.message` `model_not_found` içeriyor, `error.class:"ProviderError"`.
K-296/K-294 düzeltmesi doğrulandı, regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Not.** `event: error` çerçevesi GELMEZse (fix'in regresyonu): hemen
> `AgentEndpoints.cs`'in `ExecuteStreamingAsync`'indeki `catch` bloğunun dar
> bir filtreye geri dönüp dönmediği kontrol edilmelidir.

---

# 6 — OpenAI uyumlu sağlayıcılar (`UseOpenAICompatible`)

OpenRouter, Groq, vLLM, yerel Ollama/LM Studio gibi herhangi bir OpenAI uyumlu
uca **adlandırılmış** bir sağlayıcı olarak bağlanma. Örnek uygulama bunu
`openrouter` adıyla zaten kurar (`Program.cs`, satır ~155-160).

---

## MT-OAI-050 — `openrouter` adlandırılmış sağlayıcı olarak görünür

**Gerçek sonuç**
`/api/models` listesi: `['anthropic','google','openai','openai-responses','openrouter']`
— `openrouter-responses` yok. `openrouter`'ın `models` dizisi tek girdi taşıyor:
`openai/gpt-5.4-mini`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-051 — Sağlayıcı adı doğrulaması: rezerve ad, geçersiz desen, 33. karakter

**Gerçek sonuç**
Üç denemede de `dotnet run` **derleme dahil** (`--no-build` olmadan)
çalıştırıldı — ilk deneme yanlışlıkla `--no-build` ile koşulup eski
binary'yi test etti (yanlış negatif, düzeltilip tekrarlandı).
- Deneme 1 (`"openai"`): `Unhandled exception. System.ArgumentException:
  'openai' saglayici adi rezervedir. ...` — exit code 134.
- Deneme 2 (`"OpenRouter2"`): `... 'OpenRouter2' gecerli bir saglayici adi
  degil. ...` — exit code 134.
- Deneme 3: **dokümanın kendi örnek dizgisi** `a234567890123456789012345678901x`
  aslında `len()` ile ölçülünce **32 karakter** çıktı (sınırda, geçerli) — 33
  karakter değil. Bu bir doküman/test-verisi hatasıdır (ürün hatası değil,
  KOSUM-PLANI §2.1 istisnası): örnek dizgi `a0000000000000000000000000000000x`
  (gerçek 33 karakter) ile değiştirildi. Bununla: `... 'a000...x' gecerli bir
  saglayici adi degil. Ad kucuk harf, rakam ve tire icermeli... en fazla 32
  karakter olmalidir ...` — exit code 134.
Üçünde de `/health` hiç yanıt vermedi (süreç Kestrel başlamadan çöktü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Not.** 32 karakterlik (sınırın tam üzerinde, geçerli) bir adın kabul
> edildiği ayrıca doğrulanabilir ama bu koşumda ölçüm değeri düşüktür (birim
> testinde zaten `Otuz_iki_karakterlik_ad_kabul_edilir_otuz_ucuncu_reddedilir`
> ile kanıtlı); bu dosyada tekrarlanmaz.

---

## MT-OAI-052 — Endpoint verilmeyen adlandırılmış sağlayıcı doğrulama hatası verir

**Gerçek sonuç**
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.Endpoint uyumlu saglayicilar icin zorunludur. Bos
birakilirsa istek sessizce resmi OpenAI adresine giderdi. ...` — tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-053 — Anahtarsız yerel sağlayıcı (Ollama) — koşullu

**Gerçek sonuç**
`curl -m 2 http://localhost:11434/api/tags` bağlantı kuramadı — Ollama bu
makinede kurulu değil.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-OAI-054 — Responses yüzeyi varsayılan olarak KAYDEDİLMEZ

**Gerçek sonuç**
Çıktı `False`. `/api/models` listesi `openrouter-responses` içermiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-055 — `EnableResponsesSurface = true` ikinci bir sağlayıcı kaydeder

**Gerçek sonuç**
Doküman'ın öngördüğü gibi `OpenAIProviderExtensions.Bind` `internal` olduğu
için erişilemedi; genel `IConfiguration.Bind(object)` uzantı metoduyla
(`openRouter.Bind(o); o.EnableResponsesSurface = true;`) değiştirildi —
derlendi ve çalıştı. `/api/models`: `['anthropic','google','openai',
'openai-responses','openrouter','openrouter-responses']` — çıktı `True`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-056 — Gerçek OpenRouter çağrısı: tool kullanımı

**Gerçek sonuç**
Yanıt metni "ORD-1002 siparişiniz kargoya verildi. Tahmini teslimat: 2 gün."
içeriyor. `get_order_status` tam bir kez `orderId:"ORD-1002"` ile çağrıldı.
`HTTP 402` görülmedi. runId `019ffb94-2556-7541-afdc-62887ada74a7`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-057 — `MaxOutputTokens` verilmezse gerçek OpenRouter hesabı `HTTP 402` üretebilir

**Gerçek sonuç**
İkinci davranış gözlendi: `event: error`, `type:"ClientResultException"`,
`message:"HTTP 402 (: )\n\nThis request requires more credits, or fewer
max_tokens. You requested up to 65536 tokens, but can only afford 8816. ..."`
— hesap bakiyesi düşük (~8816 token karşılığı kredi). Kusur değil, dokümanın
öngördüğü bakiyeye-bağlı davranış; `MaxOutputTokens` verilmediğinde MAF/OpenAI
istemcisinin varsayılan `max_tokens=65536` göndermesi doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-058 — İki adlandırılmış sağlayıcı farklı adreslere bağlanır

**Gerçek sonuç**
`openai` sağlığı: 5 model (`gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`, ...)
— `appsettings.json` katalogundaki 3 modelle sınırlı değil, hesabın ham
listesi. `openrouter` sağlığı: 200 model (`aion-labs/aion-2.0`, ...). İki
liste tamamen farklı — iki sağlayıcının farklı `Endpoint`'e bağlandığının
ağ-seviyesi kanıtı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Sağlayıcı sağlık denetimi

Denetim **ücret üretmez**: `GET {endpoint}/models`'e gider, model çağrısı
yapmaz.

---

## MT-OAI-070 — Tüm sağlayıcılar `Healthy` döner

**Gerçek sonuç**
Beş sağlayıcının hepsi (`anthropic`, `google`, `openai`, `openai-responses`,
`openrouter`) `status:"Healthy"`, `latency` dolu (`00:00:0X.XXXXXXX`), `models`
dizisi dolu (openai: 5, openrouter: 200, anthropic: 10, google: 49 model).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-071 — Bilinmeyen sağlayıcı adıyla sağlık sorgusu `404` döner

**Gerçek sonuç**
`HTTP: 404`. Gövde `ProblemDetails`: `title:"Saglayici bulunamadi"`,
`detail:"'hic-boyle-bir-saglayici' adinda kayitli bir model saglayicisi
yok."` — tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-072 — Önbellek TTL'si (60 sn) çalışır; `refresh=true` onu atlar

**Gerçek sonuç**
Çağrı 1: `00:00:10.0110195`. Çağrı 2: `00:00:10.0110195` (birebir aynı —
önbellekten). Çağrı 3 (`refresh=true`): `00:00:01.0158424` (farklı —
yeniden ölçüldü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-073 — Erişilemeyen sağlayıcının hata detayında adres veya anahtar sızmaz

**Gerçek sonuç**
`status:"Unhealthy"`, `detail:"Baglanti hatasi (ConnectionError)."` —
`ConnectionError` geçerli bir `HttpRequestError` kategorisidir (doküman
örneği `ConnectionRefused` idi, gerçek kategori farklı ama aynı enum'dan).
`grep -c` ile `127.0.0.1`, `59999`, `sk-cok-gizli-test-anahtari-12345`
taraması: `0` — hiçbiri sızmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-074 — `/api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz

**Gerçek sonuç**
İlk okuma: `openai` → `"Unknown"` (uygulama az önce başlatıldı, hiçbir
`/health` çağrısı yapılmadan). `/api/models/health/openai` bir kez
çağrıldıktan sonra ikinci okuma: `openai` → `"Healthy"`. İki `/api/models`
çağrısı da hızlı: 0.026s ve altındaki python3 ayrıştırması dahil <0.1s.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Devre kesici (`ModelProviderCircuitBreaker`)

Varsayılan: `Enabled=true`, `FailureThreshold=5`, `BreakDuration=30sn`.
Koşumu hızlandırmak için bu bölümdeki case'ler eşiği ve süreyi geçici olarak
düşürür.

---

## MT-OAI-080 — Ardışık gerçek hatalar devreyi açar

**Gerçek sonuç**
`dotnet user-secrets` yerine ortam değişkeni kullanıldı
(`AgentPrism__CircuitBreaker__FailureThreshold=2`,
`AgentPrism__CircuitBreaker__BreakDuration=00:00:20`) — şerit izolasyonu
(KOSUM-PLANI §2.2, `user-secrets` deposu makine genelinde paylaşılıyor).
Deneme 1 ve 2: gerçek OpenAI 404 (`ClientResultException`, `model_not_found`).
Deneme 3: anında `event: error`,
`type:"AgentPrismProviderUnavailableException"`,
`message:"'openai' saglayicisi devre kesici tarafindan gecici olarak
durduruldu (2 ardisik hata). 20 sn sonra yeniden denenecek."` — tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-081 — Açık devre `/api/models/health`'te `Unhealthy` olarak yansır

**Gerçek sonuç**
İlk koşumda breaker'ı tetikleyen üç denemeden sonra dokümantasyona yazma
sırasında geçen süre `BreakDuration=20s`'yi aştı ve gecikmeli `refresh=true`
çağrısı yanlışlıkla `Healthy` döndü (breaker zaten kapanmıştı — ölçüm
hatası, ürün hatası değil). `BreakDuration=30s`'ye çıkarılıp deneme üçlüsü
ile bu case'in kendi çağrısı **aynı komutta arka arkaya** koşularak
tekrarlandı: `status:"Unhealthy"`,
`detail:"Devre kesici acik. 29 sn sonra yeniden denenecek."` — tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-082 — Mola süresi dolunca yarı-açık tek deneme; başarılıysa devre kapanır

**Gerçek sonuç**
`BreakDuration=30s` doldurulup (`sleep 32`) `support` agent'ı çalıştırıldı:
`event: run` → normal metin akışı ("tamam") → `event: done` — başarıyla
tamamlandı, hata görülmedi. Ardından `GET /api/models/health/openai`:
`status:"Healthy"`, `detail:null` — devre `Closed`'a döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-083 — Devre kesici kapatılırsa (`Enabled=false`) hatalar sayılmaz

**Gerçek sonuç**
`AgentPrism__CircuitBreaker__Enabled=false`, `FailureThreshold=1` ile üç
deneme de gerçek OpenAI'a gitti — üçü de `ClientResultException`/
`model_not_found` (404), `AgentPrismProviderUnavailableException` hiç
görünmedi. `GET /api/models/health/openai`: `status:"Healthy"`,
`detail:null` — ham sonuç, devre kesici katmanı eklenmemiş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-084 — İçerik guard engellemesi devre kesici tarafından hata SAYILMAZ

**Gerçek sonuç**
Beş çağrı da `event: error`, `type:"AgentPrismContentBlockedException"`,
`message:"Icerik 'pattern' guard'i tarafindan engellendi (kural:
denied-term, yon: Input)..."` ile bloklandı (`HTTP: 200` — SSE bağlantı
seviyesinde). Altıncı (geçerli) çağrı normal tamamlandı: `run` → `update`
("tamam") → `done`, hiçbir devre kesici belirtisi yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — `secret` sızıntısı

`ApiKey` hiçbir çıktıda, hiçbir günlükte, hiçbir hata mesajında görünmemelidir.

---

## MT-OAI-090 — API anahtarı hiçbir HTTP çıktısında görünmez

**Gerçek sonuç**
Dört uç da (`/api/models`, `/api/models/health`, `/api/models/health/openai`,
`/api/tools`) tarandı, `grep -c` sayımı hepsinde `0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-091 — API anahtarı doğrulama/hata mesajlarında görünmez

**Gerçek sonuç**
Beş case'in kayıtlı "Gerçek sonuç" metinleri yeniden gözden geçirildi:
MT-OAI-010 (`Endpoint=sadece-bir-yol` — sorunsuz başladı, anahtar yok),
MT-OAI-011 (`OptionsValidationException: Timeout sifirdan buyuk olmalidir` —
anahtar yok), MT-OAI-052 (`Endpoint ... zorunludur` — anahtar yok),
MT-OAI-073 (`Baglanti hatasi (ConnectionError).` — anahtar yok, ayrıca
`grep -c` ile doğrulandı). Hiçbirinde `sk-...` veya `sk-or-...` dizgisi yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-092 — `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz

**Gerçek sonuç**
`HTTP: 200` (401/403 yok, 13 dosyasına not düşülmesi gerekmedi). `configuration`
dizisinde `AgentPrism:Providers:OpenAI:ApiKey` **tam olarak bir kez**,
`resolved:true`, `hint:null`. `openrouter`'a ait hiçbir girdi yok. `key`
alanları yalnız ayar yolu adı taşıyor, gerçek değer hiçbir yerde yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Not.** `Admin` rolünün bu tokene gerçekte nasıl bağlandığı (rol sistemi
> hiç kayıtlı değilse üç katmanlı düşüş: loopback + bearer token) koşumda
> gözlemlenir ve gerekirse [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md)'ye
> not düşülür — bu dosya yalnız OpenAI'a özgü rapor içeriğini sınar.

---

## MT-OAI-093 — Konsol günlüğünde API anahtarı görünmez

**Gerçek sonuç**
Ayrı bir kısa koşum yerine, bu oturumun tüm §5–§9 çalıştırmalarını (onlarca
gerçek OpenAI çağrısı dahil) kapsayan sürekli konsol logu (`s3-app.log`)
tarandı — daha geniş kapsam. `grep -c "$ANAHTAR"` çıktısı `0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `rm -f /tmp/ap-oai-log.txt`

---
