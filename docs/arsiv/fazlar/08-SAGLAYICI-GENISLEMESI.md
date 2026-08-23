# Faz 8 — Sağlayıcı Genişlemesi ve Sağlık Denetimi

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-03**, **F-05**, **F-16**
> **Önkoşul:** Yok — Faz 6 sonundaki kod tabanı yeterli
> **Paketler:** `AgentPrism.OpenAI` (genişler), `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün AgentPrism tek satıcıya bağlı. "Kontrol düzlemi" iddiası tek sağlayıcıyla zayıf kalır.

## Bugün Ne Var (ölçüldü, 2026-08-02)

`src/AgentPrism.OpenAI/`:

| Dosya | Bugünkü davranış |
|-------|------------------|
| `OpenAIProviderOptions.cs` | **`Endpoint` alanı ZATEN VAR** ve yapılandırmadan bağlanıyor (`OpenAIProviderExtensions.Bind`) |
| `OpenAIProviderExtensions.cs` | `UseOpenAI(...)` **tek** bir options örneği yapılandırır; `alreadyRegistered` bayrağı ikinci çağrıyı yok sayar |
| `OpenAIProviderNames.cs` | Sağlayıcı adları **sabit**: `openai`, `openai-responses` |
| `OpenAIChatClientFactory.cs` | Tek `OpenAIClient`; iki sağlayıcı paylaşır |
| `OpenAIModelCatalog.cs` | Katalog yalnız yapılandırmadan gelir (K-032) |

**Sonuç:** taban adres zaten verilebiliyor. Eksik olan şey **aynı anda birden çok
uyumlu sağlayıcı** ve **sağlayıcı adının serbest olması**. F-03'ün gerçek işi
budur — beyin fırtınası belgesinin sandığından da küçüktür.

`ModelDescriptor` şu alanları **zaten taşıyor**: `InputCostPerMillionTokens`,
`OutputCostPerMillionTokens`. Faz 20 (maliyet) bunları kullanacak; bu fazda
yalnız yapılandırmadan doldurulur.

---

## Arayüz

`frontend/src/screens/models.tsx`:

- Her sağlayıcı kartında durum rozeti (yeşil / sarı / kırmızı / gri)
- "Şimdi denetle" düğmesi → `?refresh=true`
- Devre kesici açıksa kart üzerinde geri sayım
- Faz 5'ten kalan "sağlık kontrolü faz 6'da gelir" notu **silinir**

Bundle etkisi hedefi: **+3 KB gzip'ten az**. Yeni kütüphane eklenmez.

---

## Bu Fazda Verilen Kararlar

Karar defterine yazıldı (bkz. `docs/KARARLAR.md`, K-069–K-073):

1. **Uyumlu sağlayıcılarda Responses yüzeyi varsayılan kapalı** — çoğu uyumlu
   sunucu uygulamıyor; açık bırakmak çalışma anında anlaşılmaz hata üretir.
2. **`IModelProvider` genişletilmedi, ayrı arayüz eklendi** — K4 gereği.
3. **Devre kesici için yeni paket alınmadı** — K-007 ve AOT vaadi.
4. **Sağlık denetimi `/models` ucunu kullanır, model çağrısı yapmaz** — denetim
   ücret üretmemelidir.
5. **Devre kesici varsayılan açık** *(kullanıcı kararı)*.
6. **`UseOpenAICompatible` ayrı ad, `UseOpenAI` aşırı yüklemesi değil** *(kullanıcı kararı)*.
7. **Sağlık denetimi arka planda varsayılan kapalı** *(kullanıcı kararı)*.

---

## Uygulamadan Önce Sorulan Sorular — Cevaplandı

Üçü de dokümanın önerisiyle aynı yönde karara bağlandı (kullanıcı onayı,
2026-08-02):

1. **Devre kesici varsayılan açık** (`Enabled = true`).
2. **`UseOpenAICompatible(ad, ...)`** — ayrı ad, `UseOpenAI` aşırı yüklemesi değil.
3. **Sağlık denetimi arka planda varsayılan kapalı** (`BackgroundInterval = null`).

---

## Plandan Sapmalar

Yedi sapma var; hepsi ölçüme veya analyzer'a dayanıyor.

### S1 — Ad deseni doğrulaması regex değil, elle karakter denetimi

**Plan:** `^[a-z0-9][a-z0-9-]{0,31}$` deseni.
**Sorun:** `MA0009` (regex DoS analizi) `[GeneratedRegex]` ile kaynak üretilmiş
düzenli ifadeleri de işaretliyor; bu kadar basit bir desen için bastırmaya
gerek yoktu.
**Yapılan:** `OpenAICompatibleProviderExtensions.IsValidNamePattern` elle
karakter döngüsüyle aynı kuralı uyguluyor. Regex bağımlılığı yok.

### S2 — Doğrulayıcı `name`'i artık kullanıyor (plan zaten böyle öngörmüştü)

`OpenAIProviderOptionsValidator.Validate(string? name, ...)` adsız örnekte
(`UseOpenAI()`) `ApiKey` zorunlu tutar; adlandırılmış örnekte
(`UseOpenAICompatible()`) `Endpoint` zorunlu, `ApiKey` isteğe bağlıdır. Bu,
dokümanın 8.1 bölümünde zaten öngörülmüştü ("bugünkü uygulama `name`
parametresini yok sayıyorsa düzeltilir") — sapma değil, planın kendisi.

### S3 — Sağlık denetiminin hata detayı `HttpRequestException.Message` değil, `HttpRequestError` kategorisi

**Ölçüldü:** Bağlantı reddi (`connection refused`) gibi durumlarda
`.Message` hedef adresi (host:port) gövdeye gömüyor. DoD açıkça "hata
detayında API anahtarı veya uç adresi sızmıyor" diyor; ham mesaj bunu ihlal
ederdi. **Yapılan:** `exception.HttpRequestError` (.NET 8+ kategori enum'u,
adres taşımaz) kullanıldı. Fonksiyonel test
(`Baglanamayan_saglayicinin_detayinda_ne_anahtar_ne_adres_gorunur`) bunu
kapalı bir porta bağlanarak doğruluyor.

### S4 — `ModelProviderRegistry` yapıcısına opsiyonel devre kesici parametresi

Devre kesicinin entegrasyon noktası olarak `ModelProviderRegistry.CreateChatClient`
seçildi (üretilen her istemciyi sağlayıcı adına göre sarar). Yapıcıya
`ModelProviderCircuitBreaker? circuitBreaker = null` eklendi — varsayılan
`null` sayesinde doğrudan `new ModelProviderRegistry(providers)` ile kurulan
mevcut testler (`ModelProviderRegistryTests`) değişmeden geçti.

### S5 — Ollama gerçek kurulu değildi; anahtarsız yerel bağlantı sahte bir sunucuyla doğrulandı

Kullanıcı "Ollama bilgisayarımda yok, kendi yöntemlerinle test et" dedi. Gerçek
bir Ollama kurulumu yerine `FakeOpenAiCompatibleServer` (gerçek bir Kestrel
dinleyicisi, `127.0.0.1`, rastgele port) yazıldı ve `AgentPrism.AspNetCore.FunctionalTests`
projesine eklendi. Bu, F-05'in asıl riskli mekanizmasını (yerel adrese
bağlanma + anahtarsız istek + `OpenAIClient`'in boş kimlik kabul etmemesi
için yer tutucu kullanılması) **gerçek bir soket üzerinden**, deterministik
ve CI-güvenli biçimde doğrular. Gerçek OpenAI/OpenRouter çağrısı yapan
otomatik test **yoktur** (Faz 3'teki aynı kararla tutarlı); ağ gerektiren
doğrulama elle yapıldı (aşağıdaki DoD kanıtı).

### S6 — Örnek uygulamada OpenRouter modeli için `MaxOutputTokens` verildi

**Ölçüldü:** `openrouter-destek` agent'ı ilk halinde `MaxOutputTokens`
belirtmiyordu; MAF/OpenAI istemcisi varsayılan olarak `max_tokens=65536`
gönderiyor. OpenRouter'ın kredi kontrolü `max_tokens`'i "en kötü durum"
maliyeti olarak hesaba katıyor ve düşük bakiyeli anahtarlarda gerçek bir
`HTTP 402 (insufficient credits)` üretti — kodda hata değil, gerçek bir
hesap kısıtı. **Yapılan:** `ModelBinding.MaxOutputTokens = 512` eklendi;
sonrasında çağrı sorunsuz tamamlandı (bkz. DoD kanıtı). Uyumlu bir sağlayıcı
eklerken bu, bilinmesi gereken bir tuzaktır — `MEMORY.md`'ye yazıldı.

### S7 — `/api/models` yanıtı katkısal olarak genişledi

Doküman "`/api/models` yanıtı `providers[].status` alanı ile genişler"
diyordu. Üst düzey şekil (dizi) korunarak her `ModelProviderDescriptor`'a
`status` alanı eklendi — mevcut tüketicileri kırmayan, katkısal bir değişiklik.
Alan **onbellekten** doldurulur (`ModelProviderHealthCache.TryPeek`); bu uç
hiçbir zaman sağlayıcıya ağ çağrısı yapmaz.

---

## Bitiş Ölçütleri (DoD)

Elle doğrulama: gerçek OpenAI anahtarı + gerçek OpenRouter anahtarı,
`samples/AgentPrism.Api`, `ASPNETCORE_ENVIRONMENT=Development` (user-secrets
yalnız Development'ta yüklenir).

| Ölçüt | Durum | Kanıt |
|-------|-------|-------|
| Aynı uygulamada `openai` + iki uyumlu sağlayıcı kayıtlı, üçünden de gerçek yanıt alınıyor | ✅ | `openai` (support), `openai-responses` (kayıtlı, denetlendi), `openrouter` (openrouter-destek) — aşağıdaki çıktı |
| Ollama ile anahtarsız yerel çalıştırma doğrulandı | ⚠️ **kısmi — bkz. sapma S5** | Ollama bu makinede kurulu değil; mekanizma `FakeOpenAiCompatibleServer` ile gerçek bir soket üzerinden doğrulandı (fonksiyonel test, 7 senaryo) |
| `GET /api/models/health` üç sağlayıcı için durum döndürüyor | ✅ | aşağıdaki çıktı — üçü de `Healthy` |
| Sağlayıcı kapatıldığında devre açılıyor, `BreakDuration` sonunda kapanıyor | ✅ | `ModelProviderCircuitBreakerTests` (9 senaryo) + `ModelHealthEndpointsTests.Ardisik_hatada_devre_acilir_ve_saglik_ucu_bunu_yansitir` |
| Models ekranındaki "faz 6'da gelir" notu kalktı | ✅ | `models.tsx` güncellendi; E2E testi doğruluyor |
| Hata detayında API anahtarı veya uç adresi **sızmıyor** | ✅ | `HttpRequestError` kategorisi kullanılır (sapma S3); `Sunucu_hata_dondurunce_...` ve `Baglanamayan_saglayicinin_detayinda_...` testleri |
| Dört doğrulama kapısı sıfır uyarı; sır taraması boş | ✅ | build/test(434)/pack(8 paket)/format → 0 uyarı; `grep` sır taraması boş |
| Bundle ölçüldü ve DoD'ye yazıldı | ✅ | **93,0 KB gzip** (bütçe 250 KB), Faz 6 sonu 92,4 KB idi → **+0,6 KB** (hedef: +3 KB altı) |

### Gerçek çıktı — üç sağlayıcı, gerçek yanıt (2026-08-02)

```
$ curl -s localhost:5080/health
{"status":"healthy","phase":"8 - saglayici genislemesi ve saglik denetimi",
 "storage":{"persistent":false,"runStore":"InMemoryRunStore","sessionStore":"InMemorySessionStore"},
 "provider":{"openAI":true,"model":"gpt-5.4-mini","name":"openai"},
 "openRouter":true}

$ curl -s localhost:5080/agentprism/api/models/health
[
  {"providerName":"openai","status":"Healthy","latency":"00:00:00.8933032","models":["gpt-5.4-mini","gpt-5.6-luna","gpt-5.6-terra"]},
  {"providerName":"openai-responses","status":"Healthy","latency":"00:00:00.4520805","models":["gpt-5.4-mini","gpt-5.6-luna","gpt-5.6-terra"]},
  {"providerName":"openrouter","status":"Healthy","latency":"00:00:00.3373152","models":[/* 200 model, ucret uretmedi */]}
]

$ curl -s -X POST localhost:5080/agentprism/api/agents/support/run \
       -d '{"message":"ORD-7 siparisim nerede","sessionId":"faz8-openai-test"}'
# ... tool cagrisi (get_order_status) + akisli yanit ...
# Son metin: "ORD-7 siparişiniz kargoya verilmiş. Tahmini teslim süresi: 2 gün."

$ curl -s -X POST localhost:5080/agentprism/api/agents/openrouter-destek/run \
       -d '{"message":"ORD-9 siparisim nerede","sessionId":"faz8-openrouter-test"}'
# openrouter -> openai/gpt-5.4-mini (resmi OpenAI DEGIL, farkli satici)
# ... tool cagrisi (get_order_status) + akisli yanit ...
# Son metin: "ORD-9 siparişiniz kargoya verildi. Tahmini teslim süresi: 2 gün."

$ curl -s localhost:5080/agentprism/api/models | # her ogede status alani
[{"name":"openai","status":"Healthy",...},{"name":"openai-responses","status":"Healthy",...},{"name":"openrouter","status":"Healthy",...}]
```

**Not:** İlk OpenRouter denemesi `HTTP 402 (insufficient credits)` ile
başarısız oldu — `max_tokens` varsayılanı (65536) hesabın karşılayabileceği
kredinin üzerindeydi. `ModelBinding.MaxOutputTokens = 512` eklenince sorun
çözüldü (sapma S6). Bu, koddaki bir hata değil, gerçek bir OpenRouter hesap
kısıtıydı ve gizlenmedi.

---

## Sonraki Faza Devir Notu

- **Ollama ile gerçek doğrulama henüz yapılmadı.** Mekanizma (anahtarsız
  bağlantı, yer tutucu kimlik, `GET /models` denetimi) gerçek bir Kestrel
  sunucusuyla test edildi ve doğru çalışıyor, ancak gerçek Ollama'nın kendi
  `tool_choice`/`usage` davranışı bu ortamda hiç gözlenmedi. Ollama kurulu bir
  makinede fırsat çıkarsa `samples/AgentPrism.Api` içindeki yorum satırları
  açılıp elle doğrulanmalı.
- **OpenRouter (ve muhtemelen diğer uyumlu sağlayıcılar) `max_tokens`
  varsayılanına duyarlıdır.** Yeni bir uyumlu sağlayıcı eklerken
  `ModelBinding.MaxOutputTokens` verilmemesi hesap kredi hatası (`HTTP 402`
  benzeri) üretebilir — bu AgentPrism'in hatası değildir ama şaşırtıcıdır.
  Bkz. `MEMORY.md`.
- Faz 20 (maliyet) `ModelDescriptor.*CostPerMillionTokens` alanlarını
  kullanacak. Uyumlu sağlayıcılar için de yapılandırmadan doldurulabildiği
  bu fazda **doğrulanmadı** (appsettings örneğinde fiyat alanı boş bırakıldı);
  Faz 20'de gerçek bir örnekle ölçülmeli.
- Faz 26 (Anthropic/Gemini) devre kesici dekoratörünü **hazır bulacaktır**;
  entegrasyon noktası `IModelProviderRegistry` seviyesinde olduğu için yeni
  sağlayıcı paketleri hiçbir ek kod yazmadan aynı korumayı alır — tek şart
  `IModelProvider` singleton olarak DI'da kayıtlı olmak.
- Sağlık sözleşmesi (`IModelProviderHealthCheck`) Faz 26 ve 27'de
  uygulanmalıdır; desen `OpenAIProviderHealthCheck` ile birebir aynıdır
  (`GET {endpoint}/models`, `internal` statik yardımcılar test edilebilir).
- `AgentPrismHealthOptions.BackgroundInterval` seti çalışıyor
  (`ModelProviderHealthBackgroundService`) ama gerçek bir uzun-ömürlü
  süreçte hiç gözlenmedi (yalnızca `null` — kapalı — yolu elle doğrulandı).
