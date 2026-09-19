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

**🎉 DOSYA 05 KAPANDI — 40/40 case koşuldu** (oturum 10: 17 · oturum 11: 23).
Dosya sonucu: **38 ☑ Geçti · 1 ☑ Kaldı (MT-OAI-043) · 1 ⏭ Atlandı (MT-OAI-053,
Ollama yok)**. Sayım skill §7 betiğiyle alındı, elle yazılmadı.

- **Sonraki oturumun işi:** zincirin beşinci ve son ailesi —
  [`07-HTTP-YONETIM-API.md`](../../07-HTTP-YONETIM-API.md), 43 case, 2 oturum.
  Bu dosyaya dönme.
- **Bozuk ön koşul:** yok.
- **Zincirin durumu:** `01 → 02 → 03 → 05` yeşil; `07` bitince Faz B (dört
  paralel şerit) açılabilir.

### Bu dosyanın bulgusu

| Bulgu | Önem | Kısaca |
|---|---|---|
| `HATA-S1-020` | Orta | Normalleştirilen her sağlayıcı hatası `RunError.Class = Unknown`'a **ve tek bir fingerprint'e** düşüyor; K-296'nın eklediği desenler bu yolda ölü kod. İki bağımsız ölçüm (OpenAI 404 · OpenRouter 402) aynı parmak izini verdi. Tarama: 17 kararlı kimliğin 10'u sınıflandırıcıda tanınmıyor |

Bu dosyada **tek** ürün kusuru çıktı. Geri kalan her sapma dokümandaydı:
**14 case'in `Beklenen sonuç`'u düzeltildi** — ikisi davranış değişikliği
(K-404 doğrulamayı yazma yoluna taşımış; sağlayıcı hatası normalleştirmesi
detayı günlüğe taşımış), biri bayat fixture terimi (MT-OAI-084), üçü yanlış
alan yolu (`usage.*`, `response.messages[0]...`, `providerName`), kalanı dil
(K-228 — sevk edilen metin İngilizce).

### Oturum 11'in ortam notları

🚨 **Devre kesici durumu süreç-içidir ve şeridi kirletir.** MT-OAI-080..083
ayrı bir örnekte (5092) koşuldu; 5081'de koşulsaydı `openai` devresi açık
kalır ve sonraki case'ler sebepsiz `TraconProviderUnavailableException` alırdı.
Eşik ve mola ortam değişkeniyle verilir:
`Tracon__CircuitBreaker__FailureThreshold` · `__BreakDuration` · `__Enabled`.

🚨 **Süre ölçümü bu blokta kanıtın kendisidir.** Kapalı devre ~57 ms, gerçek
ağ çağrısı 300–1400 ms. Yalnız hata tipine bakmak devrenin mi yoksa
sağlayıcının mı reddettiğini ayırt etmez; her denemenin süresini yaz.

🚨 **Sağlık ucunda `checkedAt`'e bak, `latency`'ye değil** (MT-OAI-072).
İki ayrı ölçüm tesadüfen aynı `latency`'yi verebilir; `checkedAt` vermez.

⚠️ **Guard terimi `confidential-project`** (`Program.cs:217`), spec'in yazdığı
`gizli-proje` değil. Aşama 0'ın fixture süpürmesi bu terimi atlamış.

### Oturum 10'un ortam notları — sonraki oturumun bilmesi gerekenler

🚨 **`usage` ve `response` alanları KÖKTE DEĞİL.** `GET /api/runs/{id}` token
sayısını `usage.totalTokens` altında taşır; akışsız `/run` yanıtı metni
`response.messages[0].contents[0].text` altında taşır. Kökten okuyan bir sonda
`None` görür ve geçen bir case'i `Kaldı` sanar — bu oturumda bir kez oldu.

🚨 **`GET /api/runs/{id}/events` SSE döner, JSON değil.** `python3 -m json.tool`
bu uçta patlar. Ayrıştırıcı: `<scratch>/sse.py` (çerçeve sayımı + birleşik metin).
Ham `grep` işe yaramaz — `update` çerçevesinin gövdesi çok satırlı `data:`
öneklerine bölünmüştür ve kelimeleri parçalar.

🚨 **Donuk `samples/` isteyen üç case repo'ya dokunmadan koşuldu.** İki tarif:
- **Yapılandırma katmanı** (MT-OAI-012 · 022): dizi ögesi ortam değişkeniyle
  eklenir — `Tracon__Providers__OpenAI__Models__3__Name=...`. `appsettings.json`
  dizisinin üzerine biner ve dizin numarası dâhil aynı sonucu verir.
- **Paketlenmiş tüketici host'u** (MT-OAI-004): `~/tracon-manuel/oai-ikili`,
  yerel feed `0.0.0-preview.0.789`. Kurulum zamanı davranışını (çift kayıt,
  eksik ayar) sınayan her case bu yolu kullanabilir.

🚨 **Katalog daraltmak modeli değiştirmekten iyidir.** Hesap yalnız
`gpt-5.4-mini`'ye erişiyor; `gpt-4o-mini` `HTTP 403 model_not_found` verir.
"Katalog dışı model" senaryosu için modeli değil **katalogu** daralt
(`Models__0__Name=<olmayan-ad>`), böylece erişilebilir bir model katalog dışına
düşer ve gerçek yanıt da ölçülebilir. MT-OAI-021 böyle tam kanıtlandı.

⚠️ **Spec'in bu dosyadaki `Beklenen sonuç` metinleri sistematik olarak
bayattı.** Sekiz case düzeltildi; ikisi davranış değişikliği (K-404, sağlayıcı
hatası normalleştirmesi), altısı dil (K-228 — sevk edilen metin İngilizce).
Sonraki oturum aynı deseni beklemeli: Türkçe hata mesajı bekleyen her satır
şüphelidir.

⚠️ **Ayrı bir uygulama örneği 5092'de açılabilir.** Şeridin 5081'deki uygulaması
ayakta kalır, ikisi aynı `mt_s1` şemasını paylaşır. Başlatma davranışı sınayan
case'ler için `<scratch>/kos.sh <etiket> <port> [ek args]` kullanılır —
çıkış kodunu dosyaya yazar (`timeout` macOS'ta yok).

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

## MT-OAI-030 — `openai-responses` yüzeyi PostgreSQL kalıcılığıyla çatışmaz

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Faz 3'ün S2 sapması hâlâ yerinde ve işini yapıyor.

İki çalıştırma da `error` çerçevesi üretmeden `done` ile bitti:

```
1. mesaj: cerceve sayimi {'run': 1, 'update': 10, 'done': 1}  -> "tamam"
2. mesaj: cerceve sayimi {'run': 1, 'update':  9, 'done': 1}  -> "47"
```

`InvalidOperationException: Only ConversationId or ChatHistoryProvider may be
used, but not both.` **hiç görülmedi**. İkinci yanıt `47` dizgisini taşıyor.

Geçmişin **Tracon'un** deposunda tutulduğu ayrıca ölçüldü, yanıt metnine
güvenilmedi:

```
/api/diagnostics -> persistenceProvider: PostgreSQL,
                    registeredPersistenceProviders: 1, canConnect: true
mt_s1.sessions   -> 'resp-yuzeyi-test' var
mt_s1.conversation_items -> 4 satir (iki tur x kullanici+asistan)
```

Sapmanın kendisi kaynakta duruyor: `OpenAIChatClientFactory.cs:184`
`GetResponsesClient().AsIChatClientWithStoredOutputDisabled(model)` çağırıyor —
düz `AsIChatClient(defaultModelId)` değil. Case'in "sapma kaldırılırsa bu case
kaldı vermelidir" koşulu, sapmanın varlığıyla birlikte doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-031 — İki yüzey de `/api/models`'te bağımsız görünür ve aynı katalogu taşır

**Gerçek sonuç**
MT-OAI-001'de nesne düzeyinde eşitlik zaten ölçülmüştü; burada spec'in kendi
komutuyla tekrarlandı ve `True` döndü. İki yüzey `/api/models`'te **ayrı ögeler**
olarak görünüyor (`openai` ve `openai-responses`), `models` dizileri her alanda
birebir aynı. `UseOpenAI`'nin ikisini de tek `OpenAIModelCatalog.Build(options)`
sonucuyla kurduğu doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-040 — Tool çağrısı ile uçtan uca çalıştırma

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Üç beklentinin üçü de doğrulandı, gerçek OpenAI
çağrısıyla.

Akış temiz bitti (`run` 1 · `update` 39 · `done` 1, `error` yok) ve yanıt metni
`ORD-1001` dizgisini taşıyor:

```
ORD-1001 siparişiniz kargoya verilmiş. Tahmini teslimat: 2 gün.
```

`GET /api/runs/01a0ab83-711a-7d0d-9727-c1ad10519776`:

```json
"status": "Completed",
"modelId": "gpt-5.4-mini", "modelProvider": "openai", "isStreaming": true,
"usage": { "inputTokens": 402, "outputTokens": 25, "totalTokens": 427,
           "cachedInputTokens": 0, "reasoningTokens": 0 },
"error": null, "eventCount": 26
```

`totalTokens` = **427**, `null` değil ve pozitif.

⚠️ **Spec'in alan yolu belirsiz:** "`totalTokens` null değil" diyor ama alan
kökte değil, **`usage` nesnesinin içinde**. Kökte `totalTokens` diye bir alan
yok; kökten okuyan bir doğrulama `None` görür ve case'i yanlışlıkla `Kaldı`
sayar. `Beklenen sonuç`'a tam yol yazıldı.

Tool çağrısı olay listesinden sayıldı (`GET /api/runs/{runId}/events` — bu uç
JSON değil **SSE** döner, spec'in ima ettiği `json.tool` çalışmaz):

```
olay sayimi: {'RunStarted': 1, 'ToolInvoking': 1, 'ToolInvoked': 1,
              'MessageDelta': 22, 'RunCompleted': 1}
get_order_status ToolInvoking sayisi: 1    (tam bir kez)
```

Aynı `toolCallId` (`call_zLLpHQXzvHJ5JI0a1DaSlYZp`) hem `ToolInvoking` hem
`ToolInvoked` olayında görünüyor, yani tek bir çağrının iki ucu — tekrar yok.
Tool `payload`'ı gidiş-dönüş taşıyor: `orderId=ORD-1001` →
`Order ORD-1001 has shipped. Estimated delivery: 2 days.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-041 — Akış (SSE) üç çerçeve üretir: `run`, `update`(ler), `done`

**Gerçek sonuç**
Beş beklentinin beşi de doğrulandı.

Yanıt başlığı: `Content-Type: text/event-stream`.

İlk çerçeve (ham):

```
id: 0
event: run
data: {"runId":"01a0ab83-fc49-7753-ad48-605abcdda6f9","sessionId":"oai-sse-01"}
```

Alan adları camelCase (`runId`, `sessionId`). Ardından **5** `event: update`
çerçevesi geldi. Son çerçeve:

```
id: 6
event: done
data: {"sessionId":"oai-sse-01"}
```

Çerçeve sayımı `{'run': 1, 'update': 5, 'done': 1}` — hiçbir `event: error`
yok. Birleşik metin: `tamam`.

Her çerçevenin `id:` alanı da var ve sıfırdan artıyor (spec bunu istemiyor ama
akışın yeniden bağlanabilirliği için önemli).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-042 — `Idempotency-Key` başlığı akışsız (tek JSON) yanıt üretir

**Gerçek sonuç**
```
HTTP: 200
CT:   application/json; charset=utf-8      (SSE degil)
```

Gövde tek bir JSON nesnesi; `finishReason: "stop"`, metin dolu (`"tamam"`),
`usage.totalTokenCount: 356`. Aynı uç başlıksız çağrıldığında `text/event-stream`
döndüğü MT-OAI-041'de ölçüldüğü için, biçim değişikliğini yapan şeyin gerçekten
`Idempotency-Key` başlığı olduğu kanıtlanmış olur (K-288).

⚠️ **Spec'in alan yolu belirsiz** — MT-OAI-040'takiyle aynı sorun. "`text` alanı
doludur" diyor ama kökte `text` yok; gerçek yol
`response.messages[0].contents[0].text`. `Beklenen sonuç`'a tam yol yazıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-043 — Gerçek OpenAI 404 (`model_not_found`): akışlı yanıt `error` çerçevesi üretir

**Gerçek sonuç**
Dört beklentinin **ikisi geçti, ikisi geçmedi.** İkisi doküman bayatlığı, ama
ölçüm sırasında ayrı bir **ürün kusuru** çıktı: `HATA-S1-020`.

**Geçen kısım — case'in asıl konusu.** Akış çerçevesiz kapanmadı:

```
id: 0
event: run
data: {"runId":"01a0ab84-56c7-7ec1-833f-7a2a924bba66","sessionId":"oai-404-test"}

id: 1
event: error
data: {"type":"ProviderInvocationException","message":"The model provider request failed."}
```

`curl` çıkış kodu `0` — bağlantı düzgün sonlandı. `GET /api/runs?agentName=...`
kaydı `status: Failed` ile döndü, yani K-294'ün `RunRecordingAgent.CompleteAsync`
yolu çalışıyor.

**Geçmeyen kısım 1 — doküman bayat.** Spec `error.type`'ın
`System.ClientModel.ClientResultException` içermesini, `error.message`'ın
`model_not_found` ya da `404` taşımasını bekliyor. Gerçek kayıt:

```json
{"type": "upstream_error",
 "message": "The model provider request failed.",
 "class": "Unknown",
 "fingerprint": "4eca6e35...c1cc7424"}
```

Bu **kasıtlı** bir sınırdır, gerileme değil. `ProviderFailureNormalizer`
(`src/Tracon.Core/Models/ProviderFailureNormalizer.cs:15`) yabancı her istisnayı
model çağrısı sınırında `ProviderInvocationException`'a çeviriyor.
`SafeErrorText`'in XML dokümanı gerekçeyi açıkça yazıyor: *"a foreign message
can carry a request detail, an internal URL, a `host:port`, or a partial
credential that the exception's own producer put there without Tracon's
control."* Detay kaybolmuyor, **yer değiştiriyor** — tam hâli günlüğe gidiyor ve
ölçüldü:

```
System.ClientModel.ClientResultException: HTTP 404 (invalid_request_error: model_not_found)
The model `gpt-olmayan-model-xyz` does not exist or you do not have access to it.
```

Spec'in `Beklenen sonuç`'u bu tasarıma göre düzeltildi (skill §1.1 istisnası).
MT-OAI-073 (anahtar/adres sızmaması) aynı mekanizmanın kardeş kanıtıdır.

**Geçmeyen kısım 2 — gerçek kusur, `HATA-S1-020`.** `class` alanı `Unknown`.
K-296'nın kurduğu şey tam olarak buydu: sağlayıcı hatasının **sınıflandırılması**.
Normalleştirme sınıflandırıcıdan **önce** çalıştığı için K-296'nın eklediği
`clientresultexception` ve `HTTP 4xx` desenleri artık bu yolda hiç eşleşmiyor —
ölü kod hâline geldiler. Ayrıntı aşağıdaki hata kaydında.

Case bu yüzden `Kaldı` işaretlendi: iddiaların ikisi doküman düzeltmesiyle
kapanıyor ama kaydın hata bilgisi K-296'nın bıraktığı yerden **ölçülebilir
biçimde zayıf** ve bunu ölçen case budur.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### HATA-S1-020 — Normalleştirilen her sağlayıcı hatası `Unknown` sınıfına düşer

| | |
|---|---|
| **Case** | MT-OAI-043 |
| **Önem** | **Orta** |
| **Sınıf** | gözlemlenebilirlik — K-296'nın düzelttiği davranış başka bir katman tarafından geçersizleştirilmiş |

**Belirti.** Gerçek bir OpenAI `HTTP 404 model_not_found` sonrası `run` kaydı:

```json
"error": { "type": "upstream_error",
           "message": "The model provider request failed.",
           "class": "Unknown" }
```

Beklenen sınıf `ProviderError`'dır. `RunErrorClass.ProviderError` mevcut ve
`DefaultRunErrorClassifier` onu üretebiliyor — ama bu girdiyle üretemiyor.

**Kök neden.** İki mekanizma birbirinin önüne geçiyor:

1. `ProviderFailureNormalizer` (`ProviderFailureNormalizer.cs:130`) yabancı
   istisnayı `ProviderInvocationException.UpstreamFailure(exception)` ile
   sarıyor. Bu bir `TraconException`'dır (`:120`) ve `ErrorType` = `upstream_error`,
   `Message` = sabit `"The model provider request failed."` (`:12-13`).
2. `DefaultRunErrorClassifier` **istisna nesnesine değil**, `RunError`'ın iki
   dizgisine bakıyor (`DefaultRunErrorClassifier.cs:69` `ClassifyCore`). Sırayla:
   - `StableIdentities` sözlüğünde `upstream_error` **yok** (`:41-52`) →
   - `Timeout`/`Canceled`/`RateLimit`/`Quota`/`Tool` desenleri sabit mesajla
     eşleşmiyor →
   - `ProviderErrorTypePattern()` (`:144`, K-296'nın eklediği
     `clientresultexception|requestfailedexception|...`) `upstream_error` ile
     eşleşmiyor →
   - `ProviderErrorMessagePattern()` (`:152`, `\bHTTP\s+[45]\d{2}\b`) sabit
     mesajla eşleşmiyor →
   - `return RunErrorClass.Unknown` (`:110`).

`TraconException` olduğu için `SafeErrorText.ForPersistence` mesajı olduğu gibi
koruyor; yani sarmalayıcı `ClientResultException failed. (ref: ...)` metnini
bile üretmiyor — o metin **eşleşirdi**.

🚨 **K-296'nın kodu artık ulaşılamaz.** `DefaultRunErrorClassifier.cs:137-142`'deki
yorum hâlâ *"Measured (samples/Tracon.Api, a real OpenAI 404 response): the
official provider SDKs do NOT THROW HttpRequestException..."* diyor. O ölçüm
doğruydu, ama normalleştirme katmanı araya girdiğinden beri sınıflandırıcı o
istisna adını **hiç görmüyor**. Yorum bugünkü kodu yanlış tarif ediyor.

**Etki.** Yabancı sağlayıcıdan gelen **her** hata — 404, 500, bağlantı reddi,
kimlik hatası — aynı `Unknown` kovasına düşüyor. `RunErrorClass` taksonomisinin
amacı operatörün "sağlayıcı mı, kota mı, tool mu, iptal mi" sorusunu kayıttan
yanıtlayabilmesiydi. Sınıfın kendi dosyasındaki Faz 157 yorumu aynı riski başka
bir vaka için yazıyor: *"every provider timeout was filed as Canceled and
disappeared from the failure dashboards."* Bu sefer `Unknown`'a düşüyorlar.

**Kapsam.** Yol sağlayıcıdan bağımsızdır — normalleştirici ortak katmandadır,
yani OpenAI, Anthropic, Google ve gelecekteki her `IModelProvider` aynı şekilde
etkilenir. Kalıcı `run` kaydını okuyan her yüzey (arayüz, `/api/runs`,
maliyet/hata panoları) aynı `Unknown`'ı görür.

**Kanıt sınırı.** `upstream_error` dizgisi kaynakta yalnız üç yerde geçiyor:
normalleştiricide, iki OpenAI-uyumlu uçta ve bir depo sözleşme testinde
(`RunStoreContract.cs:1175` — orada yalnızca **saklanabilirliği** sınanıyor,
sınıflandırması değil). Yani bu boşluğu tutan **hiçbir test yok**.

**Önerilen yön (kapanış oturumunun kararı).** En küçük düzeltme
`StableIdentities` sözlüğüne iki giriş eklemektir —
`upstream_error → ProviderError` ve `provider_credential_unsupported` için uygun
sınıf. Sınıflandırıcının kendi dokümanı zaten bu mekanizmayı tarif ediyor:
*"First tries an exact match on the error's stable identity."* Normalleştirici
kararlı bir kimlik üretiyor, sınıflandırıcı onu tanımıyor; iki sözlük satırı
boşluğu kapatır ve desen eşleştirmeye hiç gerek kalmaz.
`kusur-giderme` sınıf taraması: `TraconException` türeten **her** `ErrorType`
sabiti `StableIdentities`'e karşı taranmalı — `upstream_error` tek eksik
olmayabilir.

**🚨 İkinci ölçüm bulguyu ağırlaştırdı (MT-OAI-057).** Bambaşka bir sağlayıcıda,
bambaşka bir hatada — OpenRouter, `HTTP 402`, yetersiz kredi — kayıt şu çıktı:

```json
{"type": "upstream_error", "message": "The model provider request failed.",
 "class": "Unknown",
 "fingerprint": "4eca6e3521f8fa3792501989d426fb7e19cf48022733537d847be43ac1cc7424"}
```

**Fingerprint MT-OAI-043'ünkiyle karakter karakter aynı.** İki farklı sağlayıcı
(OpenAI · OpenRouter), iki farklı HTTP kodu (404 · 402), iki farklı kök neden
(model yok · kredi yok) → **tek bir parmak izi**.

Nedeni yapısal: `Classify` parmak izini `ErrorFingerprint.Compute(runError.Message)`
ile üretir (`DefaultRunErrorClassifier.cs:63`) ve normalleştirme sonrası mesaj
**sabittir**. Parmak izinin işi benzer hataları gruplamak, farklı olanları
ayırmaktır; bu hâliyle yabancı sağlayıcıdan gelen her hata tek bir kovaya
çöküyor. `class: Unknown` ile birleşince kayıt operatöre iki soruyu da
yanıtlayamıyor: *ne tür bir hata* ve *daha önce gördüğüm hata mı*.

Bu, düzeltmenin yalnız `StableIdentities`'e iki satır eklemekle bitmeyebileceğini
gösterir: parmak izinin ayırt edici bir girdiye (örneğin iç istisnanın tip adına
ya da sağlayıcı adı + durum koduna) dayanması gerekir. Karar kapanış
oturumunundur.

**Sınıf taraması — koşum sırasında yapıldı, boşluk tek değil.** Kaynakta **17**
kararlı hata kimliği tanımlı (`const string *ErrorType`), `StableIdentities`
sözlüğü bunların yalnız **7**'sini tanıyor. Tanınmayan 10:

| Tanınmayan kimlik | Not |
|---|---|
| `upstream_error` | bu bulgunun konusu — yabancı sağlayıcı hatası |
| `provider_credential_unsupported` | aynı normalleştiricinin ikinci çıktısı (`ProviderFailureNormalizer.cs:11`) |
| `agent_source_contract` · `agent_source_failed` | agent kaynağı sözleşme/çalışma hatası |
| `external_call_rejected` | dış çağrı politikası |
| `session_conflict` · `session_owner_required` | oturum sınırı |
| `replay_tool_mismatch` | replay uyuşmazlığı |
| `job_retry` · `eval_run_diff_unavailable` | iş/eval yolları |

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile J)

**Kaydın iki teşhisi de doğruydu; kapanış ÜÇÜNCÜ bir katman buldu.**

**Birinci yarı — sınıf.** Kaydın önerdiği yön aynen uygulandı (K-816):
`StableIdentities`'e `upstream_error → ProviderError` ve
`provider_credential_unsupported → CompilationFailed` girdileri eklendi. İkincisi
bir sağlayıcı hatası değildir: kiracının kimlik bilgisi var ama adaptör kabul
etmiyor, yani sohbet istemcisi hiç kurulamıyor. Kaydın "K-296'nın kodu artık
ulaşılamaz" tespiti **yarı doğru** çıktı: desenler ölü değil — normalleştiricinin
sarmalamadığı yerlerde kaçan aynı SDK tiplerini hâlâ taşıyorlar — ama sağlayıcı
404'ünü yakalayan şey artık onlar değil. Bayat yorum düzeltildi.

**İkinci yarı — parmak izi** (K-817 👤). Kaydın "ayırt edici bir girdiye dayanması
gerekir" tespiti uygulandı: mesaj Tracon'un **kendi seçtiği** üç olguyu taşıyor —
sağlayıcı adı, istisnanın tip adı, HTTP durum kodu. Sağlayıcının kendi metni
**hiç** girmiyor (istek gövdesi, iç URL, `host:port` ya da kısmi kimlik bilgisi
taşıyabilir ve `run` kaydı kalıcıdır); tam istisna yine `ILogger`'a gidiyor.
Durum kodu **Core'da, mesaj metninden** okunuyor — `FallbackRetryClassifier`'ın
zaten uyguladığı emsal, SDK referansı ve reflection yok.

⚠️ **Adaptör başına tipli bir seam önce seçildi, sonra geri alındı.** Seçim
"durum kodunu yalnız adaptör bilebilir" gerekçesine dayanıyordu; kod okununca o
gerekçenin yanlış olduğu görüldü, kullanıcıya bildirildi ve karar yenilendi.

🚨 **ÜÇÜNCÜ katman: ayırt edici mesaj eklendi ve test HÂLÂ kırmızı kaldı.**
`ErrorFingerprint.NumberPattern` (`\d+`) **her** sayı dizisini `{n}` yapıyor,
yani `HTTP 404` ile `HTTP 500` aynı normalleştirilmiş metne düşüyordu. Sayı
temizliği bir **id**'nin ya da bir **sayım**ın tek hatayı yüzlerce kümeye
bölmesini engellemek için vardır (o sınıfın kendi ölçümü: 2000 oluşum → 1368
küme); bir **durum kodu** bunun tersini yapar. `(?<!\bHTTP\s)` lookbehind'ı
yalnız onu koruyor, diğer her sayı yine `{n}`'e iniyor (K-818). Bu katmanı ne
kusur kaydı ne kapanış analizi öngörmüştü — yalnız kırmızı kalan test gösterdi.

**Sınıf taraması — kalan sekiz kimlik BİLEREK eşlenmedi.** Kaydın saydığı 10
tanınmayan kimlikten ikisi yukarıda kapandı; kalan 8'inin (`session_conflict` ·
`session_owner_required` · `external_call_rejected` · `agent_source_contract` ·
`agent_source_failed` · `replay_tool_mismatch` · `job_retry` ·
`eval_run_diff_unavailable`) **karşılığı olan bir `RunErrorClass` üyesi yok** ve
var olan üyelerin doküman anlamları dardır (`Infrastructure` kendi yorumunda
"yalnız öksüz run uzlaştırması bu sınıfa düşer" diyor). Yanlış kovaya koymak
`Unknown`'dan kötüdür — `Unknown` kendi dokümanında "bir kusur değil, bir ölçüm
aracı". Yeni enum üyesi eklemek public sözleşme işidir (OpenAPI, TypeScript
şeması, iki arayüz sözlüğü, `RunErrorClassContractTests`'in emekli-boşluk
disiplini), yani **yeni yetenek**: `docs/ADAYLAR.md` → **F-246**.

**Son doğrulama koşumu (2026-09-18).** Sekiz kapının sekizi de yeşil: `build`
(sıfır uyarı) · `pack` · `format` · `scripts` birim testleri (339) · agent map ·
`denetim-paketi` · `docs-site npm run check` · tam `dotnet test`. Tam koşumda üç
test düştü ve **üçü de izole koşumda geçti** — ikisi Aile T'de zaten kayıtlı
(`ObjectToolAotPackageTests`), üçüncüsü bu turun en ağır örneği: SQL Server
container'ı zaman aşımına uğrayınca `SqlServerSchemaFixture` migration'ı
uygulayamadı ve **31 test zincirleme** düştü; aynı proje tek başına **820/820**
geçti. Aile T'ye yazıldı.

🚨 **Sevk edilen doküman kapısı bu ailede de kırmızıya döndü** (`///` satırında
🚨). Aile D ve I'yle aynı kapı, üçüncü vaka. **Taban tazelenmedi**, iki cümle
yeniden yazıldı; kuralın kendisi `docs/hafiza/dokumantasyon.md`'ye eklendi.

Bunların bir kısmı geri düşüş desenlerine **rastlantısal** olarak takılabilir
(örneğin mesajında `tool` geçen bir kimlik `ToolError` sayılır) — ama bu tasarım
değil, tesadüftür ve mesaj metni değişince sessizce bozulur. Taksonomide karşılığı
hazır duran sınıflar var: `ProviderError`, `Infrastructure`, `ToolError`.

**Bu tarama bir ölçümdür, düzeltme önerisi değildir.** Hangi kimliğin hangi
sınıfa gideceği kapanış oturumunun kararıdır; bazıları bilinçli olarak
`Unknown` bırakılmış olabilir. Kesin olan: `upstream_error` için `Unknown`
bilinçli **değildir**, çünkü K-296 tam da o vakayı sınıflandırmak için
yazılmıştı.

---

**Yeniden koşum — 2026-09-19 (kapanış, Aile J sonrası) · ☑ GEÇTİ**

Gerçek OpenAI anahtarı, gerçek `gpt-olmayan-model-xyz` 404'ü, `mt_z` şeması.
Dört beklentinin **dördü de** karşılandı.

**1 · Akış çerçevesiz kapanmadı** — turdakiyle aynı iki çerçeve, ama `error`
çerçevesinin mesajı artık ayırt edici:

```
id: 0
event: run
data: {"runId":"01a0b70a-a275-78b7-bb02-787788069db0","sessionId":"oai-404-test"}

id: 1
event: error
data: {"type":"ProviderInvocationException","message":"The model provider request
       failed. Provider: 'openai', fault: 'ClientResultException' (HTTP 404)."}
```

**2 · Kalıcı kayıt** (`GET /api/runs/{id}`):

```json
"status": "Failed",
"error": {
  "type": "upstream_error",
  "message": "The model provider request failed. Provider: 'openai', fault: 'ClientResultException' (HTTP 404).",
  "class": "ProviderError",
  "fingerprint": "039d1063ddcf549b1b7a97bef2a637b5b1cf0dbaf91df95339dad96a8b22c791"
}
```

**3 · `error.class` `Unknown` → `ProviderError`** — `HATA-S1-020`'nin ölçülen
kapanışı. Tur bu alanı `Unknown` görmüştü.

**4 · Sağlayıcının ham metni hâlâ kayda yazılmıyor.** Yanıttaki üç olgunun
üçünü de Tracon seçti (sağlayıcı adı · istisna tip adı · HTTP kodu, K-817).
Sağlayıcının kendi metni yalnız **günlükte**:

```
Tracon.ProviderInvocationException: The model provider request failed. Provider: 'openai', fault: 'ClientResultException' (HTTP 404).
 ---> System.ClientModel.ClientResultException: HTTP 404 (invalid_request_error: model_not_found)
```

⚠️ **Spec'in iki satırı bayatladı ve düzeltildi** (skill §1.1 istisnası):
`error.message`'ın "sabit metin" olduğunu söyleyen satır — K-817 onu bilerek
ayırt edici yaptı, çünkü sabit mesaj her sağlayıcı hatasına **tek** bir
`fingerprint` veriyordu; ve `error.class`'ın `Unknown` geldiğini söyleyen satır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı


---

## MT-OAI-050 — `openrouter` adlandırılmış sağlayıcı olarak görünür

**Gerçek sonuç**
Her iki beklenti de doğrulandı.

```
saglayicilar: ['anthropic', 'google', 'openai', 'openai-responses', 'openrouter']
openrouter-responses var mi: False
openrouter modelleri: ['openai/gpt-5.4-mini']
```

`appsettings.json`'ın `OpenAICompatible:openrouter` bloğuyla karşılaştırıldı:
tek `Models` girdisi var (`openai/gpt-5.4-mini`, `DisplayName` "GPT-5.4 mini
(OpenRouter)") ve katalog birebir onu taşıyor. Responses yüzeyi varsayılan
kapalı — MT-OAI-054/055 bunu ayrıca kanıtlayacak.

Yapılandırmanın kendi yorum satırı OpenRouter'ın model kimliği kuralını da
belgeliyor: *"OpenRouter model ids carry a provider prefix: not `gpt-5.4-mini`
but `openai/gpt-5.4-mini`."*

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-051 — Sağlayıcı adı doğrulaması: rezerve ad, geçersiz desen, 33. karakter

**Gerçek sonuç**
🚨 **Sapma — spec üç kez `Program.cs` düzenlemesi ister; kural 1 yasaklıyor.**
Üç deneme repo dışı tüketici host'uyla koşuldu
(`~/tracon-manuel/oai-ad-dogrulama`, ad tek ortam değişkeninden gelir). Bu
spec'in istediğinden temizdir: doğrulama kütüphane davranışıdır, örnek
uygulamaya bağlı değildir.

Üçü de `System.ArgumentException` ile, `EXIT=134`, `ValidateName`'de patladı;
hiçbiri `/health`'e ulaşmadı.

**Deneme 1 — rezerve ad `openai`:**

```
System.ArgumentException: The provider name 'openai' is reserved. 'openai' and
'openai-responses' are used by UseOpenAI() only; the ModelBinding.Provider
values of agent definitions rely on those names. (Parameter 'name')
   at Tracon.OpenAICompatibleProviderExtensions.ValidateName(String name)
```

**Deneme 2 — büyük harf `OpenRouter2`** ve **Deneme 3 — 33 karakter** aynı
mesajı verdi:

```
'<ad>' is not a valid provider name. The name must contain lower case letters,
digits and hyphens, must start with a lower case letter or a digit, and must be
at most 32 characters long (for example 'openrouter', 'local-vllm').
```

**Ek ölçüm — sınırın 32 olduğu pozitif yönden de kanıtlandı.** Spec 2026-08'de
tam bu noktada bir kez yanılmıştı (33 sandığı dizgi 32 çıkmıştı), o yüzden
sınırın **kabul eden** tarafı da ölçüldü: 32 karakterlik
`a000000000000000000000000000000x` sorunsuz kaydedildi ve `/api/models`'te
göründü. Yani 32 geçerli, 33 geçersiz — sınır tam yerinde.

Sapma yalnız **dil** (K-228); üç `Beklenen sonuç` da düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-052 — Endpoint verilmeyen adlandırılmış sağlayıcı doğrulama hatası verir

**Gerçek sonuç**
🚨 **Sapma — spec `Program.cs` düzenlemesi ister; kural 1 yasaklıyor.** Aynı
tüketici host'u `ENDPOINT_VER=0` ile koşuldu (`o.ApiKey` verilir, `o.Endpoint`
verilmez).

Uygulama başlamayı reddetti, `EXIT=134`:

```
OptionsValidationException: OpenAIProviderOptions.Endpoint is required for
compatible providers. If it were left empty, the request would silently go to
the official OpenAI address. Set it with `UseOpenAICompatible(name,
o => o.Endpoint = new Uri("https://..."))`.
```

Mesajın **neden**i de taşıması iyi bir ayrıntı: boş bırakılsa istek sessizce
resmî OpenAI adresine giderdi. Bu, adlandırılmış bir sağlayıcı için gerçek bir
veri sızıntısı yolu olurdu — anahtar ve istem yanlış tarafa giderdi. Sıkı
davranış doğrudur.

Sapma yalnız **dil** (K-228); `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-053 — Anahtarsız yerel sağlayıcı (Ollama) — koşullu

**Gerçek sonuç**
Ollama bu makinede **yok**, case'in kendi koşulu gereği atlandı.

```
curl -m 3 http://localhost:11434/api/tags  -> baglanti yok
which ollama                                -> ollama not found
```

Sunucu da, CLI de kurulu değil. Case'in `Beklenen sonuç`'u bu durumu zaten
öngörüyor. Mekanizma Faz 8 DoD'unda birim/fonksiyonel olarak kapsanmış durumda
(`OpenAICompatibleProviderExtensionsTests` / `FakeOpenAiCompatibleServer`,
sapma S5), yani kapsama boşluğu bırakmıyor.

⚠️ Bu **ortam eksikliğidir, kusur değildir** — dosyanın Azure case'leriyle aynı
kategori.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-OAI-054 — Responses yüzeyi varsayılan olarak KAYDEDİLMEZ

**Gerçek sonuç**
Şeridin uygulamasında spec'in komutu `False` döndü — `openrouter-responses`
katalogda yok. Sağlayıcı listesi: `anthropic, google, openai, openai-responses,
openrouter`.

Varsayılanın gerçekten **varsayılan** olduğu ayrıca doğrulandı: MT-OAI-055'in
tüketici host'u `EnableResponsesSurface` bayrağına hiç dokunmadan da
`['openrouter']` verdi. Yani yokluk `appsettings.json`'ın bir ayarından değil,
`UseOpenAICompatible`'ın kendi varsayılanından geliyor.

Karşıtlığı da anlamlı: `UseOpenAI` iki yüzeyi **birden** kaydeder (MT-OAI-001),
`UseOpenAICompatible` yalnız birini. Uyumlu sağlayıcıların çoğu Responses API'yi
konuşmaz; varsayılanın kapalı olması doğru yöndür.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-055 — `EnableResponsesSurface = true` ikinci bir sağlayıcı kaydeder

**Gerçek sonuç**
🚨 **Sapma — spec `Program.cs` düzenlemesi ister; kural 1 yasaklıyor.** Bayrak
tüketici host'unda tek değişkene bağlandı ve **iki yönlü** ölçüldü — spec yalnız
açık hâli istiyordu, kapalı hâl kontrol grubu olarak eklendi:

```
EnableResponsesSurface = false -> ['openrouter']
EnableResponsesSurface = true  -> ['openrouter', 'openrouter-responses']
```

Çıktı `True`. Aynı host'ta, aynı ikilide, tek farkla — yani ikinci sağlayıcıyı
ekleyen şeyin gerçekten bu bayrak olduğu kanıtlanmış olur.

Spec'in adım 1'indeki `OpenAIProviderExtensions.Bind` uyarısına gerek kalmadı:
host zaten seçenekleri doğrudan kuruyor, `internal` bir API'ye hiç
dokunulmuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-056 — Gerçek OpenRouter çağrısı: tool kullanımı

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Üç beklentinin üçü de doğrulandı, gerçek OpenRouter
hesabı üzerinden.

```
cerceve sayimi: {'run': 1, 'update': 39, 'done': 1}
BIRLESIK METIN: ORD-1002 siparişiniz kargoya verilmiş. Tahmini teslimat: 2 gün.
```

Yanıt `ORD-1002` dizgisini taşıyor. Olay listesi:

```
{'RunStarted': 1, 'ToolInvoking': 1, 'ToolInvoked': 1,
 'MessageDelta': 22, 'RunCompleted': 1}
tool olaylari: [('ToolInvoking', 'get_order_status'),
                ('ToolInvoked',  'get_order_status')]
```

`get_order_status` **tam bir kez**. `HTTP 402` görülmedi, hiç `error` çerçevesi
yok — `openrouter-support` tanımının `MaxOutputTokens: 512` taşıması S6
sapmasını kapatmaya yetiyor.

⚠️ Olay profili MT-OAI-040'ın (doğrudan OpenAI) profiliyle **birebir aynı**:
aynı çerçeve sayıları, aynı tek tool çağrısı, aynı cümle. Aynı boru hattının
iki farklı adrese bağlandığını, davranış farkı üretmeden, gösteriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-057 — `MaxOutputTokens` verilmezse gerçek OpenRouter hesabı `HTTP 402` üretebilir

**Gerçek sonuç**
S6 sapması **birebir yeniden üretildi** — case'in iki dalından ikincisi
gözlemlendi. Akış `run` + `error` ile bitti:

```
cerceve sayimi: {'run': 1, 'error': 1}
[error] {"type":"ProviderInvocationException","message":"The model provider request failed."}
```

Gerçek neden günlükte, ve sapmanın tarifiyle **sayı sayı** örtüşüyor:

```
System.ClientModel.ClientResultException: HTTP 402
This request requires more credits, or fewer max_tokens. You requested up to
65536 tokens, but can only afford 7795.
```

`65536` tam olarak spec'in "MAF/OpenAI istemcisi `MaxOutputTokens` verilmezse
varsayılan olarak `max_tokens=65536` gönderir" cümlesindeki sayıdır. Hesabın o
anki gücü ~**7795** token. Bu Tracon'un hatası değildir; MT-OAI-056'nın
`MaxOutputTokens: 512` taşıyan tanımı aynı hesapta sorunsuz çalıştı.

⚠️ **Spec'in ölçüm noktası bayat.** "SSE `error` çerçevesinde `402` veya
`insufficient credits` dizgisi görünür" diyor; normalleştirme yüzünden çerçeve
sabit metni taşıyor ve o dizgiler **günlüktedir**. Kök neden MT-OAI-043'ünkiyle
aynıdır. `Beklenen sonuç` düzeltildi.

🚨 **Yan bulgu — normalleştirmenin neden var olduğunun canlı kanıtı.**
OpenRouter'ın hata mesajı, Tracon'un denetimi dışında, içine bir **anahtar
yönetim URL'si** koymuş:
`https://openrouter.ai/workspaces/default/keys/<64-hex — bu kayda YAZILMADI>`.
`SafeErrorText`'in XML dokümanı tam bu riski tarif ediyor (*"a foreign message
can carry a request detail, an internal URL, a `host:port`, or a partial
credential"*). Normalleştirme olmasaydı bu URL kalıcı `run` kaydına ve HTTP
yanıtına girecekti. Yani `HATA-S1-020` normalleştirmenin **varlığına** değil,
yalnız sınıflandırıcının kararlı kimliği tanımamasına itiraz eder.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-058 — İki adlandırılmış sağlayıcı farklı adreslere bağlanır

**Gerçek sonuç**
Her iki beklenti de doğrulandı; iki liste birbirinden tamamen farklı.

```
openai      -> Healthy, 7 model:
               gpt-5.4-mini, gpt-5.6-luna, gpt-5.6-terra, gpt-image-1,
               gpt-live-1, text-embedding-3-large, text-embedding-3-small
openrouter  -> Healthy, 200 model:
               aion-labs/aion-3.0, aion-labs/aion-3.0-mini,
               anthropic/claude-fable-5, anthropic/claude-fable-5.1, ...
```

**Ağ-seviyesi kanıt sağlam.** İki liste yalnız farklı değil, **yapısal olarak**
farklı: OpenRouter'ın kimlikleri sağlayıcı önekli (`anthropic/...`,
`aion-labs/...`) ve tam 200'de kırpılmış; OpenAI'ınkiler öneksiz ve yedi tane.
Bu listeler aynı adresten gelemez.

`openai`'ın listesi **katalogla sınırlı değil** — `appsettings.json` üç model
tanımlıyor, sağlık denetimi **yedi** döndürdü. Fazladan dördü
(`gpt-image-1`, `gpt-live-1`, iki `text-embedding-*`) katalogda hiç yok. Yani
sağlık denetimi katalogdan değil, doğrudan `GET {endpoint}/models`'ten okuyor —
spec'in ikinci iddiası tam da bu.

⚠️ Bu aynı zamanda MT-OAI-021'in ortam notunu açıklıyor: hesap `gpt-4o-mini`'ye
erişemiyor çünkü listede yok; erişebildiği yedi modelin hepsi burada.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-070 — Tüm sağlayıcılar `Healthy` döner

**Gerçek sonuç**
Beş sağlayıcının beşi de `Healthy`, `latency` dolu, `models` dolu.

```
providerName      status   latency           models
anthropic         Healthy  00:00:00.8258595   11
google            Healthy  00:00:00.4919904   50
openai            Healthy  00:00:01.3151189    7
openai-responses  Healthy  00:00:00.7549783    7
openrouter        Healthy  00:00:00.5322524  200
```

`latency` biçimi spec'in beklediği `00:00:0X.XXXXXXX` kalıbında. `detail` alanı
sağlıklı sağlayıcılarda `null` — hata yolu boş, beklendiği gibi.

⚠️ **Alan adı `providerName`**, `provider` ya da `name` değil; ilk sondam bu
yüzden beş satırı da `None` yazdı. Yanıt nesnesinin alanları:
`providerName`, `status`, `detail`, `latency`, `checkedAt`, `models`.
`Beklenen sonuç`'a not düşüldü.

`openai` ve `openai-responses` aynı 7 modeli döndürüyor — aynı hesaba, aynı
adrese bağlandıkları için beklenen budur (MT-OAI-058 farkı **farklı** sağlayıcılar
arasında gösterdi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-071 — Bilinmeyen sağlayıcı adıyla sağlık sorgusu `404` döner

**Gerçek sonuç**
```
HTTP: 404
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5",
 "title":"Provider not found","status":404,
 "detail":"There is no registered model provider named 'hic-boyle-bir-saglayici'."}
```

Gövde geçerli bir `ProblemDetails`; `type` RFC 9110 §15.5.5 (Not Found)
bağlantısı, yani durum koduyla tutarlı.

Sapma yalnız **dil** (K-228): spec `Saglayici bulunamadi` / `'...' adinda
kayitli bir model saglayicisi yok.` bekliyordu. `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-072 — Önbellek TTL'si (60 sn) çalışır; `refresh=true` onu atlar

**Gerçek sonuç**
Spec'in iki beklentisi de doğrulandı, üstelik `latency`'den daha güçlü bir
tanıkla: `checkedAt`.

```
1. cagri  latency 00:00:01.3151189  checkedAt 2026-09-16T18:43:56.736671+00:00
2. cagri  latency 00:00:01.3151189  checkedAt 2026-09-16T18:43:56.736671+00:00   <- AYNI nesne
3. cagri  latency 00:00:00.7017441  checkedAt 2026-09-16T18:44:46.487907+00:00   <- refresh=true, YENIDEN olculdu
```

İkinci çağrının `latency`'si birinciyle **aynı**; `checkedAt` de aynı, yani
dönen şey gerçekten önbellekteki aynı `ModelProviderHealth` nesnesi — yeniden
ölçüm yok. `refresh=true` ikisini birden değiştirdi.

⚠️ **Yalnız `latency` karşılaştırmak zayıf bir tanıktır** — iki ayrı ölçüm
tesadüfen aynı değeri verebilir. `checkedAt` tam zaman damgası taşıdığı için
ayrımı kesin yapar. `Beklenen sonuç`'a eklendi.

**Ek ölçüm — TTL'nin kendisi de kanıtlandı.** Case'in başlığı "60 sn" der ama
adımları süre dolmasını hiç sınamıyor; 60 saniye beklemek yerine TTL kısaltıldı
(`Tracon__Health__CacheTtl=00:00:03`, ayrı örnek 5092'de):

```
A  checkedAt 18:45:16.634050     ilk olcum
B  checkedAt 18:45:16.634050     hemen ardindan — TTL icinde, AYNI
C  checkedAt 18:45:22.542222     4 sn sonra — TTL doldu, refresh OLMADAN yeniden olculdu
```

Yani süre dolunca önbellek kendiliğinden yenileniyor. Varsayılanın 60 saniye
olduğu kaynaktan doğrulandı: `src/Tracon.Core/TraconOptions.cs:461`
`public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(60);` — ve
yapılandırılabilir olduğu bu ölçümün kendisiyle kanıtlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-073 — Erişilemeyen sağlayıcının hata detayında adres veya anahtar sızmaz

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Spec `Program.cs` düzenlemesi ister; kural 1
yasakladığı için tüketici host'una kapalı bir porta bakan sağlayıcı kuruldu
(`http://127.0.0.1:59999/v1`, anahtar `sk-cok-gizli-test-anahtari-12345`).

```json
{
  "providerName": "kapali-port-testi",
  "status": "Unhealthy",
  "detail": "Connection error (ConnectionError).",
  "latency": "00:00:00.0190378",
  "models": []
}
```

**Sızıntı taraması — dördü de temiz.** Her dizgi HTTP yanıtının ham gövdesinde
`grep -F` ile arandı:

```
127.0.0.1                         -> yok
59999                             -> yok
sk-cok-gizli-test-anahtari-12345  -> yok
localhost                         -> yok
```

`detail` yalnız kategoriyi taşıyor: `Connection error (ConnectionError).`
K-073'ün sınırı yerinde.

Sapmalar (ikisi de doküman tarafında):
- **Dil:** spec `Baglanti hatasi (` bekliyordu; sevk edilen metin İngilizce
  (K-228).
- **Kategori örneği yanlıştı.** Spec "örnek: `ConnectionRefused`" diyor, ama
  `ConnectionRefused` bir `HttpRequestError` üyesi **değildir** (o bir
  `SocketError` adıdır). Gerçek üye `ConnectionError`'dır. Spec'in asıl şartı —
  "bir `HttpRequestError` kategori adı içerir" — karşılanıyor; yanlış olan
  parantez içindeki örnekti. Düzeltildi.

⚠️ **Ek gözlem:** üç dizgi **günlükte de** yok. `SafeErrorText` sözleşmesi tam
ayrıntının `ILogger`'a gitmesini öngörür (MT-OAI-043/057'de öyle oldu); burada
sağlık denetimi yolu istisnayı hiç loglamıyor. Bu case için bir sorun değil —
gizlilik açısından daha sıkı. Ama teşhis açısından, kapalı bir portun **hangi**
adres olduğu operatöre hiçbir yerden görünmüyor. Kayıt amaçlı not; bu case bunu
sınamıyor ve kusur olarak açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-074 — `/api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz

**Gerçek sonuç**
Üç beklentinin üçü de doğrulandı. Taze bir örnek (5092) açıldı; hazır olma
yoklaması `/health` ile yapıldı, model sağlık ucuna **hiç** gidilmedi.

```
1. /api/models  -> anthropic:Unknown  google:Unknown  openai:Unknown
                   openai-responses:Unknown  openrouter:Unknown        (0.02 sn)
2. /api/models/health/openai  (bir kez)
3. /api/models  -> anthropic:Unknown  google:Unknown  openai:Healthy
                   openai-responses:Unknown  openrouter:Unknown        (0.01 sn)
```

**Ayrım tek sağlayıcıda ve tam olarak beklenen yerde.** Yalnız `openai`
`Healthy`'ye döndü; diğer dördü `Unknown` kaldı. Bu, `/api/models`'in
gerçekten `TryPeek` ile önbelleğe **baktığını**, kendi başına denetim
**tetiklemediğini** kanıtlar — tetikleseydi beşi birden dolardı.

İki çağrı da 0.02 ve 0.01 saniye, yani spec'in "< 1 sn" eşiğinin çok altında.
Kıyas için: gerçek bir sağlık denetimi MT-OAI-070'te 0.5–1.3 saniye sürüyordu.
Aradaki iki büyüklük mertebesi ağ çağrısı olmadığının kendi başına kanıtıdır.

⚠️ Bu ölçüm dosya 03'ün `HATA-S1-016` bulgusunu da açıklıyor: `/health`
sağlayıcı önbelleğini doldurmuyor, bu yüzden `/api/models/health` çağrılmadıkça
toplu sağlık sonsuza dek `Degraded` kalıyor. Buradaki ilk ölçüm (`/health` ile
açıldı, beşi de `Unknown`) tam olarak o mekanizmanın görüntüsüdür.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-080 — Ardışık gerçek hatalar devreyi açar

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Eşik `2`, mola `20 sn` ortam değişkeniyle verildi
(`user-secrets` yazılmadı, skill §1.2); ayrı örnek 5092'de koşuldu ki devre
kesici durumu şeridin 5081'deki uygulamasını kirletmesin (durum süreç-içidir).

```
--- deneme 1 ---  1.340 sn  error: ProviderInvocationException
--- deneme 2 ---  0.275 sn  error: ProviderInvocationException
--- deneme 3 ---  0.057 sn  error: TraconProviderUnavailableException
```

**Süre ayrımı spec'in istediği kanıtı tek başına veriyor.** İlk iki deneme ağa
çıktı (1340 ms ve 275 ms); üçüncüsü **57 ms** — spec'in "< 100ms" eşiğinin
altında, yani hiç ağa çıkmadı.

Üçüncü denemenin çerçevesi:

```json
{"type":"TraconProviderUnavailableException",
 "message":"The 'openai' provider was temporarily stopped by the circuit
            breaker (2 consecutive failures). Will retry in 20s."}
```

Mesaj eşiği (`2 consecutive failures`) **ve** kalan süreyi (`Will retry in 20s`)
birlikte taşıyor. `AgentRunStream`'in `catch` bloğunun her istisnayı `error`
çerçevesine çevirdiği doğrulandı: MT-OAI-043'ün normalleştirilmiş istisnası da,
buradaki `TraconProviderUnavailableException` da aynı şekilde çerçeveye döndü.

⚠️ `TraconProviderUnavailableException` bir `TraconException` olduğu için
mesajı **normalleştirilmiyor** — `ProviderFailureNormalizer.IsKnownSafe` onu
listede tutuyor. Yani devre kesici mesajı okunabilir kalırken yabancı sağlayıcı
mesajı gizleniyor; sınır tam olarak doğru yerde.

Sapma yalnız **dil** (K-228); `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-081 — Açık devre `/api/models/health`'te `Unhealthy` olarak yansır

**Gerçek sonuç**
Her iki beklenti de doğrulandı — ve spec'in "ham denetim başarılı olsa bile"
koşulu **fiilen gerçekleşti**, yani case en güçlü hâliyle koşuldu:

```json
{ "providerName": "openai",
  "status": "Unhealthy",
  "detail": "Circuit breaker is open. Will retry in 13s.",
  "latency": "00:00:00.9922843",
  "models": ["gpt-5.4-mini", "gpt-5.6-luna", "gpt-5.6-terra", "gpt-image-1",
             "gpt-live-1", "text-embedding-3-large", "text-embedding-3-small"] }
```

`models` dizisi **dolu** ve `latency` 0.99 saniye — yani `refresh=true` gerçek
ağ çağrısını yaptı ve `GET {endpoint}/models` **başarılı** oldu. Buna rağmen
`status` `Unhealthy`. Durumu ezen şey ham denetim değil,
`ModelProviderHealthCache.ApplyCircuitBreakerOverlay`.

`detail` kalan süreyi taşıyor (`13s`) — MT-OAI-080'in 20 saniyesinden geriye
sayıyor.

Sapma yalnız **dil** (K-228): spec `Devre kesici acik.` bekliyordu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-082 — Mola süresi dolunca yarı-açık tek deneme; başarılıysa devre kapanır

**Gerçek sonuç**
Her iki beklenti de doğrulandı. MT-OAI-080/081'in devresi 20 saniye sonra
yeniden denemeye izin verdi.

```
calistirma: {'run': 1, 'update': 5, 'done': 1}  -> "tamam"   (1.38 sn)
sonrasinda /api/models/health/openai -> Healthy, detail: null
```

Çalıştırma gerçek OpenAI çağrısıyla başarıyla tamamlandı — 1.38 saniye, yani ağa
çıktı (MT-OAI-080'in kapalı-devre denemesi 57 ms idi). `EnsureRequestAllowed`
devreyi `HalfOpen`'a çevirip tek denemeye izin vermiş, `RecordSuccess` de
`Closed`'a sıfırlamış.

⚠️ **Ek gözlem — sıralamada bir incelik.** Sağlık ucu, çalıştırmadan **önce**
de `Healthy` döndü (`detail: null`). Yani overlay mola süresi dolar dolmaz
kalkıyor; devrenin gerçekten sağlıklı olduğunu kanıtlayan bir başarı henüz
kaydedilmemişken `Healthy` görünüyor. Bu `HalfOpen` durumunun doğasıdır ve
kusur olarak açılmadı — ama sağlık ucuna bakan bir otomasyon, mola bitiminde
sağlayıcının **denenmemiş** olduğunu bilmez. Kayıt amaçlı not.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-083 — Devre kesici kapatılırsa (`Enabled=false`) hatalar sayılmaz

**Gerçek sonuç**
Her iki beklenti de doğrulandı. `Enabled=false` **ve** `FailureThreshold=1` ile
açıldı — yani eşik en agresif değerinde, devre yine de hiç açılmadı.

```
deneme 1 | 1.184 sn | ProviderInvocationException
deneme 2 | 0.356 sn | ProviderInvocationException
deneme 3 | 0.430 sn | ProviderInvocationException
```

**Süreler kanıtın kendisi.** Üçü de yüz milisaniyelerin üstünde, yani üçü de
gerçek OpenAI'a çıktı. `TraconProviderUnavailableException` hiç görünmedi.
Kıyas: MT-OAI-080'de devre açıkken üçüncü deneme 57 ms sürüyordu. Eşik `1`
olduğu için, devre kesici etkin olsaydı ikinci deneme zaten kesilmiş olurdu.

Sağlık ucu ham denetim sonucunu gösterdi: `Healthy`, `detail: null`, 7 model —
üç ardışık başarısızlığa rağmen devre kesici katmanı hiç eklenmedi. MT-OAI-081
ile karşıtlığı tam: orada ham denetim başarılıyken `Unhealthy` görünüyordu.

`IsEnabled` kontrolünün `EnsureRequestAllowed`/`RecordFailure`'ı baştan devre
dışı bıraktığı, iki uçtan (çalıştırma ve sağlık) birden doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-084 — İçerik guard engellemesi devre kesici tarafından hata SAYILMAZ

**Gerçek sonuç**
🚨 **Spec'in guard terimi bayat — `gizli-proje` DEĞİL, `confidential-project`.**
Örnek uygulama `Program.cs:217`'de `options.DeniedTerms.Add("confidential-project")`
diyor. Aşama 0'ın fixture adı süpürmesi (294 geçiş, 17 aile) bu terimi
**atlamış**; DEVIR §7.1 tam olarak bu sınıfı "görürsen kusurdur, kaydet" diye
işaretliyor. `Beklenen sonuç` ve `Girilecek veri` düzeltildi.

⚠️ **Bu bayatlık sessiz bir yanlış-geçiş üretirdi.** `gizli-proje` ile koşulsa
guard hiç devreye girmez, beş çağrı gerçek OpenAI'a çıkar ve **başarılı** olur;
altıncı çağrı da başarılı olacağı için case "Geçti" görünür — ama kanıtladığı
şey guard'ın engellemediğidir, devre kesicinin saymadığı değil. Case'in tüm
değeri kaybolurdu.

Doğru terimle, varsayılan ayarlarda (`Enabled=true`, `FailureThreshold=5`),
önce başarılı bir çalıştırmayla devre sıfırlandı:

```
engelleme 1 | 0.055 sn | TraconContentBlockedException
engelleme 2 | 0.046 sn | TraconContentBlockedException
engelleme 3 | 0.046 sn | TraconContentBlockedException
engelleme 4 | 0.045 sn | TraconContentBlockedException
engelleme 5 | 0.046 sn | TraconContentBlockedException
```

**Beşi de ~46 ms** — hiçbiri ağa çıkmadı. `ContentGuard`'ın boru hattında
`CircuitBreaker.Wrap`'ten önce durduğu doğrulandı. Mesaj:
`Content was blocked by the 'pattern' guard...`

Altıncı, geçerli çağrı:

```
{'run': 1, 'update': 5, 'done': 1} -> "tamam"   (0.72 sn)
/api/models/health/openai?refresh=true -> Healthy, detail: null
```

**Eşik tam olarak 5 ve engelleme tam olarak 5 kez oldu** — sayılsalardı devre
kesin açılırdı. Açılmadı: altıncı çağrı gerçek OpenAI'a çıktı (0.72 sn; kapalı
devre 57 ms'ti) ve sağlık `Healthy`, `detail: null` döndü. Yani ne çalıştırma
yolunda ne sağlık overlay'inde devrenin açıldığına dair bir iz var.

`TraconContentBlockedException`'ın `CircuitBreakingChatClient`'ın özel `catch`
bloğunda hata sayılmadan yeniden fırlatıldığı (K-322) iki uçtan doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-090 — API anahtarı hiçbir HTTP çıktısında görünmez

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Spec dört uç tarar; **altı** uç tarandı ve **iki**
anahtar birden arandı (OpenAI + OpenRouter), spec yalnız OpenAI'ı istiyordu.
Arama `grep -cF` ile ham gövde üzerinde yapıldı.

```
/api/models                  bayt=4538    OpenAI=0  OpenRouter=0
/api/models/health           bayt=8035    OpenAI=0  OpenRouter=0
/api/models/health/openai    bayt=268     OpenAI=0  OpenRouter=0
/api/tools                   bayt=5110    OpenAI=0  OpenRouter=0
/api/diagnostics             bayt=1637    OpenAI=0  OpenRouter=0
/api/agents                  bayt=10530   OpenAI=0  OpenRouter=0
```

Altı ucun altısında sıfır. Bayt sayıları da yazıldı: gövdeler **boş değil**
(4.5–10.5 KB), yani "sıfır eşleşme" boş yanıttan gelmiyor — gerçekten dolu
yanıtlar tarandı.

Sapma (skill §1.2): anahtarlar `user-secrets`'tan **okundu** (yazma yok) ve
şeridin ortam değişkeninden alındı; hiçbir dosyaya, hiçbir çıktıya yazılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-092 — `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz

**Gerçek sonuç**
`GET /api/diagnostics` → `HTTP 200` (`401`/`403` değil, yani case atlanmadı).
`configuration` dizisinin **tamamı**:

```json
{"key": "Tracon:Providers:OpenAI:ApiKey",    "resolved": true, "hint": null}
{"key": "Tracon:Providers:Anthropic:ApiKey", "resolved": true, "hint": null}
{"key": "Tracon:Providers:Google:ApiKey",    "resolved": true, "hint": null}
```

Üç beklentinin üçü de doğrulandı:

1. **`Tracon:Providers:OpenAI:ApiKey` tam olarak bir kez.** `openai` ve
   `openai-responses` iki ayrı sağlayıcı olarak kayıtlı olmasına rağmen tek
   girdi var — `TraconDiagnosticsCollector`'ın `seenConfigurationKeys`
   tekilleştirmesi çalışıyor. `resolved: true`, `hint: null`.
2. **`key` yalnız ayar yolunu taşıyor**, değeri değil. MT-OAI-090'ın taraması
   bu ucu da kapsadı ve sıfır eşleşme verdi.
3. **`openrouter` için hiçbir girdi yok.** Listedeki üçü de `UseOpenAI` /
   `UseAnthropic` / `UseGoogle` ile kaydedilmiş sağlayıcılar;
   `UseOpenAICompatible` `configurationSectionKey: null` geçtiği için (K-249)
   hiç bildirmiyor.

⚠️ Bu aynı zamanda K-249'un **istenen** yan etkisini gösteriyor: adlandırılmış
sağlayıcının ayar yolu tüketicinin kendi seçimidir, Tracon onu tahmin edip
teşhise yazmaz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-093 — Konsol günlüğünde API anahtarı görünmez

**Gerçek sonuç**
🚨 **Kritik case — geçti.** Spec beş saniyelik taze bir koşum ister; bunun
yerine **oturum boyunca biriken altı günlüğün tamamı** tarandı — 13.310 satır,
onlarca gerçek sağlayıcı çağrısı, iki kritik hata yolu (404 ve 402) ve tam
yığın izleri dâhil. Bu, spec'in istediğinden çok daha geniş bir örnektir.

```
app.log      satir=10034   OpenAI=0  OpenRouter=0
cb.log       satir= 1504   OpenAI=0  OpenRouter=0
cb083.log    satir= 1253   OpenAI=0  OpenRouter=0
oai074.log   satir=   82   OpenAI=0  OpenRouter=0
ttl.log      satir=   82   OpenAI=0  OpenRouter=0
oai021b.log  satir=  355   OpenAI=0  OpenRouter=0
```

**Kısmi sızıntı da arandı** — tam anahtar yerine yalnız **ilk 12 karakteri**:
`app.log` içinde OpenAI için `0`, OpenRouter için `0`. Yani anahtar kırpılmış
ya da maskelenmiş bir biçimde de görünmüyor. Tam eşleşme aramak tek başına
yetmezdi: `sk-proj-abc…` gibi bir önek loglansa tam arama onu kaçırırdı.

`Logging:LogLevel:Default` = `Information` (varsayılan, değiştirilmedi) —
yani case'in ön koşulu sağlandı ve bu ayrıntı düzeyinde bile sızıntı yok.

⚠️ Bu ölçümün ağırlığı MT-OAI-057'den geliyor: OpenRouter'ın hata mesajı
günlüğe bir **anahtar yönetim URL'si** yazdı. Yani günlüğe yabancı içerik
gerçekten akıyor; buna rağmen anahtarın kendisi hiçbir yerde yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OAI-091 — API anahtarı doğrulama/hata mesajlarında görünmez

**Gerçek sonuç**
Bu case ayrı çağrı yapmaz; bu turun önceki kayıtlarını bu açıdan yeniden okur.
Ön koşulun saydığı dört case (MT-OAI-010 · 011 · 052 · 073) ve ayrıca 012 · 013
· 043 · 057 koşuldu. Ürettikleri her hata metni gözden geçirildi:

| Case | Üretilen mesaj | Anahtar değeri var mı |
|---|---|---|
| MT-OAI-010 | `...Endpoint must be an absolute address. Received value: 'sadece-bir-yol'.` | hayır |
| MT-OAI-011 | `...Timeout must be greater than zero. Received value: 00:00:00.` | hayır |
| MT-OAI-012 | `The model name for ...Models[3] cannot be empty.` | hayır |
| MT-OAI-013 | `...ApiKey cannot be empty. Pass the key to the \`UseOpenAI(apiKey)\` call, or define 'Tracon:Providers:OpenAI:ApiKey' in \`dotnet user-secrets\`.` | hayır — yalnız **ayar yolunun adı** |
| MT-OAI-043 | `The model provider request failed.` (`upstream_error`) | hayır |
| MT-OAI-052 | `...Endpoint is required for compatible providers...` | hayır |
| MT-OAI-057 | `The model provider request failed.` | hayır |
| MT-OAI-073 | `Connection error (ConnectionError).` | hayır — sahte anahtar `sk-cok-gizli-test-anahtari-12345` de **yok** |

Hiçbirinde `sk-` ile başlayan gerçek bir OpenAI anahtarı, `sk-or-` ile başlayan
bir OpenRouter anahtarı geçmiyor. MT-OAI-013'ün mesajı beklendiği gibi yalnız
**ayar anahtarının adını** taşıyor, değerini değil — K-059'un sözleşmesi tam
budur.

**MT-OAI-073 bu case'in en güçlü tanığıdır:** orada anahtar *bilinen bir
dizgiydi* (`sk-cok-gizli-test-anahtari-12345`) ve hata yolu doğrudan o
sağlayıcıya aitti; `grep -F` ile arandı, bulunamadı. Gerçek anahtarlarla yapılan
arama ise MT-OAI-090 (altı HTTP ucu) ve MT-OAI-093 (13.310 satır günlük)
kayıtlarındadır — ikisi de sıfır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
