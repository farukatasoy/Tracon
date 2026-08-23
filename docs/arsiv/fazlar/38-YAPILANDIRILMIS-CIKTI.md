# Faz 38 — Yapılandırılmış Çıktı (JSON Şeması)

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-42**
> **Önkoşul:** Yok. Kalem önkoşulsuzdur ve bugün yapılabilir
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.OpenAI`, `.Anthropic`, `.Google`, `.Azure`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok — gerekçe [38.5](#385--neden-migration-yok)
> **Public API:** büyüyor — 🚨 **Faz 7'den önce bedava, sonra kırıcı**

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/38-YAPILANDIRILMIS-CIKTI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir agent bugün yalnızca **metin** döndürebilir. Çağıran taraf, cevabı ayrıştırmak için istemde "lütfen JSON döndür" yazmak ve gelen metni umutla `JsonSerializer`'a vermek zorundadır. Bu, agent'ı bir API gibi kullanmanın önündeki tek engeldir. Bu faz `ModelBinding`'e bir `ResponseFormat` alanı ekler ve onu MAF'ın `ChatOptions.ResponseFormat` alanına bağlar.

## Bitiş Ölçütleri (DoD)

- [x] `responseFormat.kind = "JsonSchema"` taşıyan bir agent tanımı kaydedilir,
      okunur ve çalıştırılır; yanıt **şemaya uyan** bir JSON belgesidir —
      gerçek OpenAI çağrısı `{"total":1250,"currency":"TL"}` döndürdü (aşağıda)
- [x] `Kind = JsonSchema` ama şema boş olan tanım `AgentPrismCompilationException`
      ile reddedilir; hata mesajı `Schema` alanını adlandırır
- [x] `SupportsStructuredOutput = false` olan bir modelde derleme reddedilir
- [x] 🚨 `responseFormat` anahtarı olmayan **eski** bir `agent_definitions`
      satırı okunur ve `null` üretir; hiçbir uç 500 dönmez
- [x] `ForJsonSchema(Type, …)` çağrısı kaynak ağacında **yoktur**; AOT uyarısı
      üretilmez
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı **ölçüldü** ve buraya yazıldı
      (156,0 KB gzip / 250 KB bütçe; önceki ölçüm 151,3 KB'ydi — bu faz ~4,7 KB ekledi)

### Doğrulama komutları — gerçek koşum, 2026-08-06

> 🚨 Plandaki `run` gövdesi (`{"messages":[{"role":"user","text":"..."}]}`)
> gerçek sözleşmeyle uyuşmuyordu — `AgentRunRequest.Message` **tekil bir
> `string`** taşır (bkz. [Plandan Sapmalar](#plandan-sapmalar)). Aşağıdaki
> komutlar düzeltilmiş, gerçekten çalıştırılmış hâlidir. Model adı da
> `gpt-5`'ten `gpt-5.4-mini`'ye düzeltildi — K-032 gereği katalogdaki gerçek
> model budur (`samples/AgentPrism.Api/appsettings.json`).

```bash
# Semali agent tanimi kaydet
curl -s -X POST http://localhost:5081/agentprism/api/agents \
  -H 'content-type: application/json' \
  -d '{
        "name":"fatura-okuyucu",
        "instructions":"Faturayi ozetle ve JSON dondur.",
        "model":{
          "provider":"openai","model":"gpt-5.4-mini",
          "responseFormat":{
            "kind":"JsonSchema","schemaName":"invoice",
            "schema":{"type":"object",
                      "properties":{"total":{"type":"number"},
                                    "currency":{"type":"string"}},
                      "required":["total","currency"]}
          }
        }
      }' | jq
# → 201 Created; model.responseFormat kaydedilmis sekliyle geri dondu.

# Calistir (SSE akisli) — cikti gecerli JSON olmali ve semaya uymali
curl -s -N -X POST http://localhost:5081/agentprism/api/agents/fatura-okuyucu/run \
  -H 'content-type: application/json' \
  -d '{"message":"Toplam 1250 TL, para birimi Turk Lirasi."}'
# → text delta parcalari birlestiginde: {"total":1250,"currency":"TL"}
#   Akisli yolda da JsonSchema kipi calisiyor (Acik Soru 5 boylece cevaplandi).

# Gecersiz tanim SAVE aninda REDDEDILMEZ (derleme validasyonu Faz 38.2'de
# BILINCLI olarak calisma/derleme anina birakildi) — 201 doner:
curl -s -X POST http://localhost:5081/agentprism/api/agents \
  -d '{"name":"kirik","instructions":"x",
       "model":{"provider":"openai","model":"gpt-5.4-mini",
                "responseFormat":{"kind":"JsonSchema"}}}' \
  -H 'content-type: application/json' -i | head -5
# → 201 Created

# ...ama CALISTIRMA (derleme) aninda reddedilir:
curl -s -X POST http://localhost:5081/agentprism/api/agents/kirik/run \
  -H 'content-type: application/json' -d '{"message":"merhaba"}' -i | head -8
# → 400 Bad Request
#   {"title":"Agent derlenemedi","detail":"'kirik' agent'i JsonSchema cikti
#    kipini secti ancak Schema vermedi."}
```

---

## Plandan Sapmalar

- **🚨 `AgentResponseFormatKind` enum'ında `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`
  eksik yazılmıştı — planda bu ayrıntı yoktu.** Faz içi sözleşme testleri (C#
  nesne round-trip) bunu yakalamadı çünkü hem yazma hem okuma aynı varsayılan
  (sayısal) temsili kullanıyordu; **yalnızca** `samples/AgentPrism.Api`'ye
  gerçek bir HTTP isteği (`"kind":"JsonSchema"` dize değeri) atıldığında
  `System.Text.Json.JsonException` olarak ortaya çıktı. `CompactionStrategyKind`
  ve depodaki diğer tüm dize-seri hâle gelen enum'lar bu özniteliği taşıyor —
  bu faz aynı deseni gözden kaçırıp sonra gerçek koşumda buldu. Ders: MEMORY.md'nin
  "birim testi yetmez" kuralı burada üçüncü kez doğrulandı.
- **Model yetenek denetimi yalnızca `Json` ve `JsonSchema` kiplerinde çalışır,
  `Text`'te çalışmaz.** Plan (38.3) bu ayrımı açıkça yazmamıştı. Gerekçe: `Text`
  kipi `null`'dan farklı olsa da hiçbir sağlayıcıya özel API yüzeyi istemez —
  "açıkça düz metin iste" talimatı her modelde çalışır. `SupportsStructuredOutput`
  bayrağının anlamı ("JSON semasina uyan cikti uretebiliyor mu") `Text` için
  anlamsızdır; `Text`'i de reddetmek yanlış `false` bayrağı yüzünden çalışan bir
  agent'ı gereksiz yere kırardı. Bkz. K-267.
- **Doğrulama komutlarındaki `run` gövdesi ve model adı düzeltildi.** Planın
  taslak `curl` örneği `{"messages":[{"role":"user","text":"..."}]}` ve
  `"model":"gpt-5"` kullanıyordu; gerçek sözleşme `AgentRunRequest.Message`
  (tekil `string`) taşır ve `gpt-5` katalogda hiç yok (K-032 — gerçek liste
  `gpt-5.4-mini`/`gpt-5.6-luna`/`gpt-5.6-terra`). Düzeltilmiş komutlar DoD
  bölümünde.
- **`samples/AgentPrism.Api/appsettings.json`'a `SupportsStructuredOutput: true`
  eklendi** (üç OpenAI modeli için) — planda yoktu ama gerçek bir uçtan uca
  koşum için zorunluydu: bayrak varsayılan `false`'tur (K1) ve örnek uygulamanın
  kendi model kataloğu bunu açıkça söylemeden `JsonSchema` kipi her zaman
  reddedilirdi.
- **`AgentDefinitionStoreContract.Tanimin_tum_alanlari_gidip_gelir` genişletildi**
  (yeni dosya değil) — planın "mevcut testler jsonb'yi korur" varsayımı
  doğruydu, yalnız test gövdesine `ResponseFormat` alanı ve doğrulaması
  eklendi. PostgreSQL (505/505) ve SQLite (255/255) ile koştu; SQL Server
  konteyneri bu ortamda (ARM64 Mac) daha önce de çalışmıyordu (K-181/dokümante
  edilmiş bilinen sınırlama), bu fazın regresyonu değil.
- **Arayüzde `agent-detail.tsx`'in sürüm karşılaştırma tablosuna (`FieldDiffTable`)
  bir `responseFormat` satırı eklendi** — planda yoktu ama K-143'ün "az sayıda,
  nadiren değişen alan" ilkesiyle tutarlı, ucuz bir ek oldu (yalnızca `kind`
  gösterilir, tam şema değil).

## Bu Fazda Verilen Kararlar

K-267. Ayrıntı ve gerekçe: `docs/KARARLAR.md`.

| Karar | Özet |
|---|---|
| K-267 | `SupportsStructuredOutput` denetimi yalnızca `Json`/`JsonSchema` kiplerinde çalışır; `Text` her zaman izinlidir |

## Sonraki Faza Devir Notu

- **Devralınan sözleşme: `ModelBinding.ResponseFormat` ve `ModelDescriptor.SupportsStructuredOutput`.**
  İkisi de nullable/varsayılan-`false`; eski kayıtlar hiçbir göç gerektirmeden
  çalışmaya devam eder.
- **🚨 Yeni bir dize-seri hâle gelen enum eklerken `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`'i
  UNUTMA.** Bu fazda unutuldu ve yalnızca gerçek bir HTTP isteğiyle (birim
  testleriyle değil) ortaya çıktı. Depodaki her enum bu deseni taşır
  (`CompactionStrategyKind`, `RunStatus`, …) — yeni bir tane eklerken var olan
  birini kopyala.
- **F-44 (model yedek zinciri) `ModelBinding`'e dokunacaksa bu fazın deseni
  geçerlidir:** `ResponseFormat` gibi nullable, `sealed record` bir alan ekle;
  `AgentDefinitionCompiler.BuildChatOptions`'ın artık **instance metodu**
  olduğunu ve `_models` (katalog) erişimine sahip olduğunu unutma —
  `Fallbacks` alanı da model yetenek/varlık denetimi için muhtemelen aynı
  `_models.List()` yolunu kullanacaktır (`FindModelDescriptor` yardımcısı
  zaten var, yeniden kullanılabilir).
- **Açık Soru 4 (OpenAI uyumlu ucun `response_format` alanı bu sözleşmeye
  bağlanmadı) hâlâ açık.** `/v1/chat/completions` isteğinin kendi gövdesindeki
  `response_format` alanı agent tanımının `ResponseFormat`'ını hiç görmüyor;
  ikisinin çakıştığı durumda hangisinin kazanacağı tanımsız. Ayrı bir fazda
  ele alınmalı.
- **Yarım kalan iş yok.** Tüm DoD kalemleri karşılandı (yukarıdaki tablo).
  Sıradaki faz kimliği bağımsızdır (`docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`).
