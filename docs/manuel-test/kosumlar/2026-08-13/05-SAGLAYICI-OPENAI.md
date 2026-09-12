# 05 — Sağlayıcı: OpenAI (`OAI`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../05-SAGLAYICI-OPENAI.md`](../../05-SAGLAYICI-OPENAI.md) — `Ön koşul`, `Adımlar`,
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
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/05-SAGLAYICI-OPENAI.md
> ```

---

## Temiz geçen case'ler (30)

| Case | Durum | Başlık |
|---|---|---|
| MT-OAI-001 | ☑ | `UseOpenAI()` tek çağrıyla iki sağlayıcı kaydeder |
| MT-OAI-004 | ☑ | İkinci `UseOpenAI()` çağrısı sağlayıcıları çoğaltmaz |
| MT-OAI-011 | ☑ | Sıfır veya negatif `Timeout` reddedilir |
| MT-OAI-020 | ☑ | Katalog yalnız yapılandırmadan gelir ve `/api/models`'te görünür |
| MT-OAI-022 | ☑ | Aynı ad iki kez tanımlanırsa son tanım kazanır |
| MT-OAI-030 | ☑ | `openai-responses` yüzeyi PostgreSQL kalıcılığıyla çatışmaz |
| MT-OAI-031 | ☑ | İki yüzey de `/api/models`'te bağımsız görünür ve aynı katalogu taşır |
| MT-OAI-040 | ☑ | Tool çağrısı ile uçtan uca çalıştırma |
| MT-OAI-041 | ☑ | Akış (SSE) üç çerçeve üretir: `run`, `update`(ler), `done` |
| MT-OAI-042 | ☑ | `Idempotency-Key` başlığı akışsız (tek JSON) yanıt üretir |
| MT-OAI-050 | ☑ | `openrouter` adlandırılmış sağlayıcı olarak görünür |
| MT-OAI-052 | ☑ | Endpoint verilmeyen adlandırılmış sağlayıcı doğrulama hatası verir |
| MT-OAI-054 | ☑ | Responses yüzeyi varsayılan olarak KAYDEDİLMEZ |
| MT-OAI-055 | ☑ | `EnableResponsesSurface = true` ikinci bir sağlayıcı kaydeder |
| MT-OAI-056 | ☑ | Gerçek OpenRouter çağrısı: tool kullanımı |
| MT-OAI-058 | ☑ | İki adlandırılmış sağlayıcı farklı adreslere bağlanır |
| MT-OAI-070 | ☑ | Tüm sağlayıcılar `Healthy` döner |
| MT-OAI-071 | ☑ | Bilinmeyen sağlayıcı adıyla sağlık sorgusu `404` döner |
| MT-OAI-072 | ☑ | Önbellek TTL'si (60 sn) çalışır; `refresh=true` onu atlar |
| MT-OAI-073 | ☑ | Erişilemeyen sağlayıcının hata detayında adres veya anahtar sızmaz |
| MT-OAI-074 | ☑ | `/api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz |
| MT-OAI-080 | ☑ | Ardışık gerçek hatalar devreyi açar |
| MT-OAI-081 | ☑ | Açık devre `/api/models/health`'te `Unhealthy` olarak yansır |
| MT-OAI-082 | ☑ | Mola süresi dolunca yarı-açık tek deneme; başarılıysa devre kapanır |
| MT-OAI-083 | ☑ | Devre kesici kapatılırsa (`Enabled=false`) hatalar sayılmaz |
| MT-OAI-084 | ☑ | İçerik guard engellemesi devre kesici tarafından hata SAYILMAZ |
| MT-OAI-090 | ☑ | API anahtarı hiçbir HTTP çıktısında görünmez |
| MT-OAI-091 | ☑ | API anahtarı doğrulama/hata mesajlarında görünmez |
| MT-OAI-092 | ☑ | `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz |
| MT-OAI-093 | ☑ | Konsol günlüğünde API anahtarı görünmez |

## Ayrıntı taşıyan case'ler (10)

## MT-OAI-002 — Bilinmeyen sağlayıcı adıyla çalıştırma anlaşılır hata verir

**Gerçek sonuç**
`POST /api/agents` `201`. `POST .../run` → `HTTP/1.1 400`,
`Content-Type: application/problem+json`, gövde: `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Agent derlenemedi","status":400,"detail":"'manuel-yok-boyle-saglayici' agent'i derlenemedi: 'yok-boyle-bir-saglayici' adinda bir model saglayicisi kayitli degil. Kayitli saglayicilar: openai, openai-responses, openrouter, anthropic, google. OpenAI icin \`builder.AddTracon().UseOpenAI(apiKey)\` cagirin.",...}`.
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

## MT-OAI-010 — Göreli (relative) `Endpoint` reddedilir

**Gerçek sonuç**
`Tracon:Providers:OpenAI:Endpoint=sadece-bir-yol` (env değişkeni,
şerit izolasyonu — bkz. §2.2) ile uygulama **sorunsuz başladı**,
`http://localhost:5083`'te dinlemeye geçti, `/api/models` normal yanıt verdi.
`OptionsValidationException` **hiç fırlatılmadı**.

Kök neden bulundu: `src/Tracon.OpenAI/OpenAIProviderExtensions.cs:164-168`
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
(`Tracon__Providers__OpenAI__Endpoint=sadece-bir-yol`, temiz
`mt_fin_o` şeması, gerçek Postgres'e karşı): uygulama artık **başlamıyor**,
konsolda birebir beklenen metin görüldü:
`Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.Endpoint mutlak bir adres olmalidir. Gelen deger:
'sadece-bir-yol'.` Regresyon testi:
`tests/Tracon.OpenAI.UnitTests/OpenAIProviderExtensionsTests.cs`
`Yapilandirmadan_gelen_goreli_adres_reddedilir` (fix geri alınıp koşulduğunda
KIRMIZI verdiği ampirik olarak doğrulandı).

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
`git checkout -- samples/Tracon.Api/appsettings.json` ile geri alındı
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
beklenen gibi). Değişiklik `git checkout -- samples/Tracon.Api/appsettings.json`
ile geri alındı. Regresyon testi:
`tests/Tracon.OpenAI.UnitTests/OpenAIProviderExtensionsTests.cs`
`Yapilandirmadan_gelen_adsiz_model_reddedilir` (fix geri alınıp koşulduğunda
KIRMIZI verdiği ampirik olarak doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OAI-013 — `ApiKey` boşken `UseOpenAI()` çağrılırsa doğrulama hata verir

**Gerçek sonuç**
`~/tracon-local-feed`'den (`Tracon.Core`/`Tracon.OpenAI`
0.0.0-preview.0.78, `01-KURULUM-VE-PAKETLEME.md` tarafından hazırlanmış,
yalnız okundu — küresel kurulum adımına dokunulmadı) bağımsız bir konsol
projesi kuruldu, `UseOpenAI(o => { })` çağrıldı. `dotnet run` şununla
çöktü: `Microsoft.Extensions.Options.OptionsValidationException:
OpenAIProviderOptions.ApiKey bos olamaz. Anahtari \`UseOpenAI(apiKey)\`
cagrisinda verin veya 'Tracon:Providers:OpenAI:ApiKey' ayarini
\`dotnet user-secrets\` icinde tanimlayin.` — mesaj birebir eşleşti. Bu
kod-yolu (`Action<OpenAIProviderOptions>` overload'u) HATA-S3-001/002'nin
sessiz-eleme sorununu taşımıyor çünkü hiç `Bind()`/`BindModels()`'tan
geçmiyor — doğrudan `IValidateOptions` çalışıyor. `/tmp/ap-oai-apikey-test`
temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `rm -rf /tmp/ap-oai-apikey-test`

---

# 3 — Model kataloğu (`OpenAIModelCatalog`)

Tracon yerleşik model listesi taşımaz (K-032). Katalog tamamen
yapılandırmadan gelir ve bir **doğrulama listesi değildir**.

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
DIŞINDA erişilebilir hiçbir sohbet modeli yok. Bu bir Tracon kusuru
değil, hesabın model erişim kapsamı sınırlı; ürün davranışının kendisi
(engellemeden iletme + log uyarısı) log kanıtıyla doğrulandı.

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

## MT-OAI-053 — Anahtarsız yerel sağlayıcı (Ollama) — koşullu

**Gerçek sonuç**
`curl -m 2 http://localhost:11434/api/tags` bağlantı kuramadı — Ollama bu
makinede kurulu değil.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

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
