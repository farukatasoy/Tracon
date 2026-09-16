# 05 — Sağlayıcı: OpenAI (`OAI`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../05-SAGLAYICI-OPENAI.md`](../../05-SAGLAYICI-OPENAI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 40 (MT-OAI-001..093) |
| **Port** | 5081 (spec 5080 yazar — şerit sapması) |
| **Şema** | `mt_s1` (spec `tracon` yazar — şerit sapması, skill §1.3) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): spec'in her yapılandırma adımı
şeridin **ortam değişkenine** çevrildi. Ortam değişkeni `user-secrets`'ı ezer;
boş değer "kayıtlı değil" demektir. Depo makine genelinde tektir ve yazılmaz.

**Açılış ölçümü (reset sonrası):**

```
DROP SCHEMA mt_s1 CASCADE -> 49 nesne dustu
information_schema.schemata -> agentprism, mt_s1_k, mt_s1_v, public (mt_s1 YOK)
/health -> Degraded
/api/models -> 5 saglayici: anthropic(3) google(3) openai(3) openai-responses(3) openrouter(1)
```

---

## Devir notu

*(oturum sonunda yazılacak)*

---

## MT-OAI-001 — `UseOpenAI()` tek çağrıyla iki sağlayıcı kaydeder

**Gerçek sonuç**
`GET /api/models` beş sağlayıcı döndü: `anthropic`, `google`, `openai`,
`openai-responses`, `openrouter`. Beklenen ikisi de var.

```
openai            -> gpt-5.4-mini, gpt-5.6-luna, gpt-5.6-terra
openai-responses  -> gpt-5.4-mini, gpt-5.6-luna, gpt-5.6-terra
```

İki `models` listesi **nesne düzeyinde birebir eşit** (Python `==` ile
karşılaştırıldı, yalnız ad değil; `contextWindowTokens`, `supportsTools`,
`supportsReasoning` dâhil her alan aynı). Tek `OpenAIProviderOptions` örneğinin
ikisini de beslediği doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-002 — Bilinmeyen sağlayıcı adıyla çalıştırma anlaşılır hata verir

**Gerçek sonuç**
Spec'in beklentisi **bayat** — ürün kusuru değil, doküman kusuru (skill §1.1
istisnası; `Beklenen sonuç` düzeltildi).

Kayıt adımı `HTTP 400` ile **reddedildi**; agent hiç yazılmadı:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1",
 "title":"Definition invalid","status":400,
 "detail":"No model provider named 'yok-boyle-bir-saglayici' is registered.
           Registered providers: anthropic, google, openai, openai-responses, openrouter."}
```

İkinci adım (`POST .../run`) bu yüzden `HTTP 404 Agent not found` döndü —
`'manuel-yok-boyle-saglayici' adında bir agent yok`.

**Kök neden — iki sapma, ikisi de doküman tarafında:**

1. **Doğrulama noktası taşındı.** Spec "sağlayıcı varlığı kayıt anında
   denetlenmez, yalnız çalıştırma anında" der. Bu, **2026-08 turunun** gözlemidir
   ve o turun kapanışı onu **kusur olarak kapatmıştır**:
   `AgentEndpoints.cs:1670-1677` XML dokümanı aynen söylüyor — *"Before this
   check existed, only the separate `POST /api/agents/validate` endpoint was
   called; the SAVE path itself would save an unknown skill/tool name without
   any error."* Git ile doğrulandı: `069ce9f2` ("phase 75") bu bloğu taşırken
   yorumdan `K-404` ve `HATA-K-001` referanslarını düşürmüş. Yani spec'in
   "düzeltilmiş" beklentisi **K-404'ün düzelttiği kusurun kendisini** tarif
   ediyor. Bugünkü davranış doğru olandır.
2. **Dil.** Spec Türkçe mesaj bekliyor; sevk edilen metin İngilizce (K-228) —
   dosya 03'ün 9 case'inde görülen aynı sapma.

**Çalıştırma-anı mesajı hâlâ yerinde**, yalnız bu HTTP yolundan erişilemez:
`ModelProviderRegistry.cs:258` aynı dizgiyi `TraconException` olarak fırlatır ve
ek olarak `"For OpenAI, call \`builder.AddTracon().UseOpenAI(apiKey)\`."`
tavsiyesini taşır. Yazma yolundaki `AgentDefinitionValidator.cs:151` ise
`unknown_model` kodu ve `model.provider` yolu ile aynı metni üretir. İki katman
aynı mesajı verir; kullanıcı erken olanı görür.

MT-OAI-003'ün spec'inde zaten kayıtlı olan gerekçe bu case için de geçerlidir:
daha erken, daha güvenli bir noktada reddetmek beklenenden **sıkı** bir
davranıştır, kusur değildir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-003 — Model adı boş ve `DefaultModel` tanımsızsa anlaşılır hata verir

**Gerçek sonuç**
Spec'in mekanik beklentisi **birebir doğrulandı**. Kayıt `HTTP 400` ile
reddedildi, agent yazılmadı:

```json
{"title":"Model binding missing","status":400,
 "detail":"'model.provider' and 'model.model' are required."}
```

`POST .../run` → `HTTP 404 "There is no agent named 'manuel-model-adi-yok'."` —
spec'in dediği gibi çalıştırma adımına hiç ulaşılmıyor.

Tek sapma **dil**: spec `'model.provider' ve 'model.model' alanlari
zorunludur.` yazıyordu, sevk edilen metin İngilizce (K-228). `Beklenen sonuç`
düzeltildi (skill §1.1 istisnası). `OpenAIChatClientFactory`'nin çalıştırma-anı
mesajının bu yoldan erişilemez olduğu teyit edildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-004 — İkinci `UseOpenAI()` çağrısı sağlayıcıları çoğaltmaz

**Gerçek sonuç**
🚨 **Sapma — spec `samples/Tracon.Api/Program.cs`'i geçici olarak değiştirmeyi
ister; kural 1 (kod donuk) yasaklıyor.** Case dosya 02'nin tarifiyle **repo
dışında** bir tüketici host'uyla koşuldu: `~/tracon-manuel/oai-ikili`,
paketlenmiş `Tracon.AspNetCore` + `Tracon.OpenAI` `0.0.0-preview.0.789`
(donuk koddan üretilmiş yerel feed). Bu spec'in istediğinden **daha güçlü** bir
kanıttır: çağrı gerçek bir tüketici host'undan ve **paketlenmiş** ikiliden gelir,
örnek uygulamanın kendi kurulumundan değil.

Host tek değişkenle bir ya da iki kez `tracon.UseOpenAI(key)` çağırır. İki koşum:

```
UseOpenAI() 1 KEZ -> ['openai', 'openai-responses']
UseOpenAI() 2 KEZ -> ['openai', 'openai-responses']
```

Uygulama ikinci çağrıda da **başladı ve çökmedi** (süreç sağlıklı ayağa kalktı,
`/api/models` cevap verdi, `kill` ile düzgün kapandı). Günlükte hiçbir `error`
ya da `exception` satırı yok; yalnız üç beklenen `warn`
(`NonPersistentStorageWarningService`, `SilentGapWarningService` ×2 — kalıcılık
kaydı olmayan minimal host olduğu için).

`ModelProviderRegistry`'nin "aynı ad birden çok kez kaydedilmiş" hatası
fırlatılmadı. Mekanizma spec'in dediği yerde:
`src/Tracon.OpenAI/OpenAIProviderExtensions.cs:107` `alreadyRegistered`
bayrağını hesaplar, `:115` ikinci çağrıda sağlayıcı ekleme adımını atlar.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-010 — Göreli (relative) `Endpoint` reddedilir

**Gerçek sonuç**
Uygulama başlamayı **reddetti**, süreç `EXIT=134` ile sonlandı; `Now listening on`
satırı hiç yazılmadı.

```
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
  OpenAIProviderOptions.Endpoint must be an absolute address.
  Received value: 'sadece-bir-yol'.
   at Tracon.OpenAIProviderExtensions.CreateProvider(...)
      in src/Tracon.OpenAI/OpenAIProviderExtensions.cs:line 138
```

Doğrulayıcı **sağlayıcı kurulurken** (`CreateProvider`, DI kök çözümü sırasında)
patlıyor — `UseOpenAI` çağrısının kendisinde değil. K-006'nın istediği fail-fast
davranışı yerinde.

Sapmalar (ikisi de koşum ortamı/doküman tarafında, ürün kusuru değil):
- **Dil:** spec Türkçe `mutlak bir adres olmalidir` bekliyordu; sevk edilen
  metin İngilizce (K-228). `Beklenen sonuç` düzeltildi (skill §1.1 istisnası).
- **`user-secrets` yazılmadı** (skill §1.2); `Tracon__Providers__OpenAI__Endpoint`
  ortam değişkeni kullanıldı, ayrı portta (5092) koşuldu ki şeridin 5081'deki
  uygulaması ayakta kalsın.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-011 — Sıfır veya negatif `Timeout` reddedilir

**Gerçek sonuç**
Case başlığı **iki** değer der; ikisi de ayrı ayrı koşuldu ve ikisi de reddedildi.

`Timeout=00:00:00` → `EXIT=134`:

```
OptionsValidationException: OpenAIProviderOptions.Timeout must be greater than
zero. Received value: 00:00:00.
```

`Timeout=-00:00:05` → `EXIT=134`, aynı mesaj, `Received value: -00:00:05.`
Negatif değer yapılandırmadan sorunsuz bağlanıyor (`FormatException` değil,
**doğrulama** hatası veriyor) — yani reddeden şey ayrıştırıcı değil, K-006'nın
doğrulayıcısı. İstenen davranış budur.

Sapma yalnız **dil**: spec Türkçe `sifirdan buyuk olmalidir` bekliyordu, sevk
edilen metin İngilizce (K-228). `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-012 — Katalogda adı boş bir model reddedilir

**Gerçek sonuç**
🚨 **Sapma — spec `samples/Tracon.Api/appsettings.json`'ı geçici olarak
değiştirmeyi ister; kural 1 (kod donuk) yasaklıyor.** Aynı girdi **yapılandırma
katmanından** üretildi, hiçbir dosyaya dokunulmadan:

```bash
Tracon__Providers__OpenAI__Models__3__Name=""   # dizinin 4. ogesi, adi bos
```

Sonuç spec'in beklediğiyle **birebir aynı**, dizin numarası dâhil:

```
OptionsValidationException: The model name for OpenAIProviderOptions.Models[3]
cannot be empty.            (EXIT=134)
```

Bu yol spec'in istediğinden **daha temizdir**: ortam değişkeni `appsettings.json`
dizisinin üzerine dördüncü ögeyi ekliyor, yani doğrulayıcı gerçek bir tüketici
yapılandırmasıyla sınanıyor ve depo değişmiyor. Spec'in adımlarına bu alternatif
bir not olarak eklendi — donuk kod kuralı her turda geçerli olduğu için sonraki
turlar da dosya değiştirmek zorunda kalmasın.

Sapma (ürün değil, doküman): **dil** — spec Türkçe `icin model adi bos olamaz`
bekliyordu, sevk edilen metin İngilizce (K-228). `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-013 — `ApiKey` boşken `UseOpenAI()` çağrılırsa doğrulama hata verir

**Gerçek sonuç**
MT-PKG-070'in feed'i (`~/tracon-local-feed`) yerinde; `Tracon.Core` ve
`Tracon.OpenAI` `0.0.0-preview.0.789` oradan çözüldü. Bağımsız konsol uygulaması
`~/tracon-manuel/oai-apikey` altında kuruldu (spec `/tmp` der; şerit-yerel
aparat dizini kullanıldı ki tur sonunda topluca silinsin).

Süreç `EXIT=134` ile sonlandı:

```
OptionsValidationException: OpenAIProviderOptions.ApiKey cannot be empty.
Pass the key to the `UseOpenAI(apiKey)` call, or define
'Tracon:Providers:OpenAI:ApiKey' in `dotnet user-secrets`.
   at Tracon.OpenAIProviderExtensions.CreateProvider(...)
```

Mesajın **iki** çıkış yolu birden göstermesi spec'in beklediğinden iyidir:
hem `UseOpenAI(apiKey)` aşırı yüklemesi hem `user-secrets` anahtarının tam adı.
`dotnet user-secrets` ibaresi mevcut. Patlama noktası MT-OAI-010/011/012 ile
aynı: `CreateProvider`, yani DI kök çözümü.

Sapma yalnız **dil** (K-228); `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-020 — Katalog yalnız yapılandırmadan gelir ve `/api/models`'te görünür

**Gerçek sonuç**
Üç beklentinin üçü de doğrulandı.

```
model sayisi: 3
adlar: ['gpt-5.4-mini', 'gpt-5.6-luna', 'gpt-5.6-terra']   (alfabetik: True)
supportsStructuredOutput: [True, True, True]
contextWindowTokens: [None, None, None]
fiyat alanlari (input/output/cachedInput): hepsi None
```

⚠️ **Sıralama iddiası bu veriyle tek başına kanıtlanmaz** —
`appsettings.json`'ın kendi sırası da `gpt-5.4-mini, gpt-5.6-luna,
gpt-5.6-terra`, yani alfabetik. Çıktıya bakarak sıralamanın kataloğun mu yoksa
yapılandırmanın mı eseri olduğu ayırt edilemez. Kaynağa bakıldı:
`src/Tracon.OpenAI/OpenAIModelCatalog.cs:64` açık bir
`result.Sort(string.CompareOrdinal(left.Name, right.Name))` yapıyor — iddia
doğru, kanıt koddan. Sıralamayı ampirik kanıtlamak isteyen bir sonraki tur
yapılandırmaya sırayı bozan bir model eklemeli.

`displayName` alanları dolu (`GPT-5.4 mini`), `supportsStreaming`/`supportsTools`
`true`, `supportsReasoning` `false`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-021 — Katalogda olmayan bir model adı reddedilmez, yalnız günlüğe yazılır

**Gerçek sonuç**
Case'in çekirdek iddiası — *katalog bir doğrulama listesi değildir* — **iki ayrı
ölçümle** kanıtlandı. Spec'in kendi verisi tek başına yetmedi; nedeni ürün değil,
hesap:

**1. Spec'in adımları (`gpt-4o-mini`).** Kayıt `HTTP 201` ile **kabul edildi** —
katalog dışı olmak yazmayı engellemiyor. Çalıştırmada beklenen günlük satırı
`Information` seviyesinde çıktı:

```
info: Tracon.OpenAIModelProvider[0]
      Model 'gpt-4o-mini' is not in the 'openai' catalog; the request is sent
      anyway. Use the Tracon:Providers:OpenAI:Models option to add the model
      to the catalog.
```

Ama akış `error` çerçevesiyle bitti
(`ProviderInvocationException: The model provider request failed.`) çünkü
**hesabın bu modele erişimi yok**:

```
System.ClientModel.ClientResultException: HTTP 403 (invalid_request_error: model_not_found)
Project `proj_0iwMbkX0...` does not have access to model `gpt-4o-mini`
```

Bu dosya 03'ün devir notunda zaten kayıtlı ortam sınırıdır, **ürün kusuru
değildir**. Üstelik 403'ün kendisi isteğin gerçekten OpenAI'a **gönderildiğini**
kanıtlar — yani yerel bir reddetme yok.

**2. Eksik parçayı kapatan ikinci ölçüm.** Spec "gerçek bir OpenAI yanıtı
üretir" der; bunu kanıtlamak için katalog daraltıldı, model değil: ikinci bir
uygulama örneği 5092'de `Tracon__Providers__OpenAI__Models__0__Name=gpt-5.9-yok-boyle`
ile açıldı, böylece **erişilebilir** `gpt-5.4-mini` katalog dışında kaldı:

```
5092 katalogu -> ['gpt-5.6-luna', 'gpt-5.6-terra', 'gpt-5.9-yok-boyle']
```

Aynı günlük satırı yine çıktı (`Model 'gpt-5.4-mini' is not in the 'openai'
catalog...`) **ve** çalıştırma gerçek yanıtla tamamlandı — SSE `update`
çerçeveleri `chatcmpl-EOonE0Coiizs4z3Pm6Ige3V5YoApf` yanıt kimliğiyle `"tam"`,
`"am"` parçalarını akıttı. Katalog dışı bir model, hesap erişimi varsa,
sorunsuz çalışıyor.

Kaynak: `src/Tracon.OpenAI/OpenAIModelProvider.cs:212`.

Sapma yalnız **dil** (K-228); `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-022 — Aynı ad iki kez tanımlanırsa son tanım kazanır

**Gerçek sonuç**
🚨 **Sapma — spec `appsettings.json` düzenlemesi ister; kural 1 yasaklıyor.**
İkinci tanım yapılandırma katmanından eklendi, dosyaya dokunulmadan:

```bash
Tracon__Providers__OpenAI__Models__3__Name="gpt-5.4-mini"
Tracon__Providers__OpenAI__Models__3__DisplayName="IKINCI TANIM"
```

Beklentinin **ikisi de** doğrulandı:

```
toplam model: 3                                    (dort degil)
adlar: ['gpt-5.4-mini', 'gpt-5.6-luna', 'gpt-5.6-terra']
gpt-5.4-mini displayName: ['IKINCI TANIM']         (son tanim kazandi)
```

`OpenAIModelCatalog.Build`'in `Dictionary` birleştirmesi spec'in dediği gibi
çalışıyor: aynı ad ikinci kez geldiğinde yeni kayıt eskisini eziyor, dizi
büyümüyor. Ayrıca sıralamanın katalogda yapıldığı buradan da görülüyor —
dördüncü sıradan gelen tanım çıktıda **birinci** sırada.

Alternatif yöntem spec'in adımlarına not olarak eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
