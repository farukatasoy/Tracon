# Faz 252 — Model Başına İstek Alanı Reddi ve Değer Sınırları

> **Durum:** 📋 Planlandı (2026-10-26)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (yalnız ekleme: `ModelDescriptor`'a bir alan, `ISamplingSupport`'a varsayılan gövdeli bir üye ve bir sınır record'u, iki doğrulama kodu) · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok (`1.0.0` GA'dan önce). Emsal: Faz 245 (sağlayıcı yetenek arayüzü, hafif)
> **Plan onayı:** Bakımcı, 2026-10-26 (sohbette alınan kararlar: model düzeyi bir **ret listesidir** — `ModelDescriptor.RejectedSamplingFields`, varsayılan `None`; değer sınırları `ISamplingSupport`'un varsayılan gövdeli bir üyesiyle beyan edilir; konsol `Seed`'i metin olarak taşır ve güvenli tamsayı dışını reddeder)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-606**, **F-607**
> **Genellik:** Tanımında `Seed`, `TopK`, `StopSequences`, ceza alanları ya da tool modu yazan her tüketici; birden çok modeli aynı sağlayıcı yüzeyinden kullanan her kurulum. İkinci kanıt ölçümdür (Faz 225 gerçek koşumu): `openai` yüzeyi `stop`'u gönderir ama `gpt-5.4-mini` onu `400 unsupported_parameter` ile reddeder; destek yüzeyin değil modelin özelliğidir. F-607'nin kanıtı ilk ilkedir: bir tanımın kaydedilip ilk run'da `400` alması ya da `Seed`'in sessizce başka bir değere dönmesi K-208'in "alan asla sessizce düşmez" kuralını çiğner. İş tüketici kodunda kalamaz: denetim derleyicide ve doğrulama raporundadır
> **Karşı görüş:** (1) "Sağlayıcının `400`'ü zaten söylüyor." Cevap: tanım kaydedilir, onay alır, kuyruğa girer ve ilk gerçek run'da düşer; doğrulama raporu (`POST /api/agents/validate`) bunu kaydetmeden önce söylemelidir — `SupportsSampling: false` aynı gerekçeyle var. (2) "Sınırları her sağlayıcı kendi içinde atabilir." Cevap: bakımcı kararıyla reddedildi — üçüncü taraf sağlayıcının yolu kalmaz ve kayıt defteri sağlayıcının kendi reddini maskeler (F-355)
> **Önkoşul:** Yok. Faz 225 (`ISamplingSupport`, `SamplingSupportCheck`) kapandı. **Sıra notu:** Faz 253 de `ModelDescriptor`'a ve `ProviderRegistrationCore.BindModels`'e alan ekler; iki faz ardışık uygulanır, satır numaraları kayar
> **Paketler:** `Tracon.Abstractions` (`ModelDescriptor`, `ISamplingSupport`, yeni `SamplingLimits`) · `Tracon.Core` (`SamplingSupportCheck`, `ValidationCodes`, katalog bağlama) · `Tracon.Providers.Shared` (`BindModels`) · `Tracon.OpenAI`, `Tracon.Azure`, `Tracon.Anthropic`, `Tracon.Google` (sınır beyanı) · `Tracon.UI` (konsol `Seed` alanı)
> **Tüketici yüzeyi:** site `guides/model-providers.md` (§ *Sampling and tool-use fields* `:187`, katalog tablosu `:691` yanına `RejectedSamplingFields`, `:246` sağlayıcı yazarı paragrafı `SamplingLimits` ile), `reference/configuration.md` (katalog anahtarı) · sevk edilen: `ModelDescriptor`/`ISamplingSupport`/`SamplingLimits` XML, `docs/sozlesme/dogrulama-kodlari.txt` (üretilir), OpenAPI → iki istemci (`ModelDescriptor` şeması büyür), `CHANGELOG.md` `[Unreleased]` → *Added*
> **Manuel test alanı:** `docs/manuel-test/05-SAGLAYICI-OPENAI.md` (gerçek `stop` reddi) · `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` (konsol `Seed`)

## Bu Faza Başlarken

1. Bu doküman
2. `grep -n "K-032 —\|K-208 —\|K-034 —\|K-1036 —" docs/KARARLAR.md` — **K-032** (katalog yapılandırmadan gelir, kodda model listesi yok), **K-208** (bilinmeyen ayar derleme hatasıdır), **K-034** (geçersiz değer reddedilir), **K-1036** (deneysel upstream tip public imzaya girmez)
3. Alan hafızası: [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) · [`hafiza/hosted-tool.md`](hafiza/hosted-tool.md) (`:80` — model düzeyi ret F-606 sınıfıdır; yüzey `✓` iken model aracı reddedebilir) · [`hafiza/frontend.md`](hafiza/frontend.md)
4. Faz 225'in ölçümü gerekirse: `git show 44866805:docs/225-MODEL-ORNEKLEME-VE-TOOL-DONGUSU.md | sed -n 240,330p`

## Amaç

Tracon bugün bir istek alanının desteğini **sağlayıcı yüzeyine** göre denetler
(`ISamplingSupport.SupportedSamplingFields`). Faz iki eksiği kapatır:

- **F-606** — Bir model, yüzeyin gönderdiği alanı reddedebilir. Katalog bunu
  `RejectedSamplingFields` ile yazar; denetim "yüzey desteği − model reddi" olur.
  `null`/`None` = bilinmiyor, denetlenmez (K-032; `SupportsSampling: false` deseninin genellemesi).
- **F-607** — Bir değer, yüzeyin kabul ettiği aralığın dışında olabilir. Sağlayıcı sınırı
  `ISamplingSupport.SamplingLimits` ile beyan eder (varsayılan: sınır yok); denetim adlı bir
  bulgu verir. Konsol `Seed`'i JS `Number`'a çevirmeden metin olarak taşır.

**Doğrulanmış kanıt (2026-10-26):**

| Kanıt | Gözlem |
|---|---|
| [`ISamplingSupport.cs:13-42`](../src/Tracon.Abstractions/Models/ISamplingSupport.cs) · [`:78-85`](../src/Tracon.Abstractions/Models/ISamplingSupport.cs) | `SamplingFields` bayrak enum'u; arayüzün tek üyesi `SupportedSamplingFields` |
| [`SamplingSupportCheck.cs:52-76`](../src/Tracon.Core/Compilation/SamplingSupportCheck.cs) | `Unsupported` yalnız sağlayıcı tanımlayıcısını okur; birincil + her `Fallbacks` bağlantısı |
| [`SamplingCapabilityCheck.cs:59`](../src/Tracon.Core/Compilation/SamplingCapabilityCheck.cs) | Model düzeyinin tek emsali: `ModelCatalogLookup.Find(...) is { SupportsSampling: false }` |
| [`ModelDescriptor.cs:51`](../src/Tracon.Abstractions/Models/ModelDescriptor.cs) | `bool? SupportsSampling` — yalnız `Temperature`/`TopP` |
| [`ProviderRegistrationCore.cs:102-129`](../src/Tracon.Providers.Shared/ProviderRegistrationCore.cs) | `ModelDescriptor`'ın her alanı burada bağlanır ("A new field is added here once") |
| [`AgentDefinitionCompiler.RequestFields.cs:20`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.RequestFields.cs) | Derleme aynı denetimi okur (doğrulama raporuyla tek kural) |
| [`ValidationCodes.cs:61`](../src/Tracon.Core/Compilation/ValidationCodes.cs) | `sampling_field_unsupported` — tek kod; model reddi ve aralık için kod yok |
| `GoogleModelProvider.cs:91` · `OpenAIModelProvider.cs:120` · `AnthropicModelProvider.cs:88` · `AzureOpenAIModelProvider.cs:93` | Dört sağlayıcı yüzey desteğini beyan eder; hiçbiri sınır beyan etmez |
| `src/Tracon.UI/frontend/src/screens/agent-editor/model.ts:154`, `:267`, `:336` | Form `seed`'i metin tutar ama gönderirken `toNumber` ile JS `Number`'a çevirir; `2^53` üstü bozulur, yüklerken `toString()` yuvarlanmış değeri gösterir |
| Faz 225 kapanışı (`44866805`, satır 246, 301) | Gerçek OpenAI: `gpt-5.4-mini` `stop`'u model düzeyinde `400 unsupported_parameter` ile reddetti |
| Faz 225 denetimi 🟢9 (decompile) | Google.GenAI adaptörü `ChatOptions.Seed`'i (`long?`) `(int)` ile denetimsiz daraltır; `2^31` üstü başka değere döner. **Uygulama oturumu yeniden ölçer** (`maf-api-kesfi` ya da SDK decompile) |

## Tasarım

Tasarım dört maddedir:

1. **Model reddi (F-606).** `ModelDescriptor.RejectedSamplingFields` (`SamplingFields`,
   varsayılan `None`). Katalog bağlayıcısı onu `Enum<SamplingFields>` ile okur (`[Flags]`:
   virgüllü liste geçerli; okunamayan metin açılışı durdurur, K-1099). `SamplingSupportCheck`
   her bağlantı için `ModelCatalogLookup.Find(registry, provider, model)` okur; istenen alan
   sağlayıcıda var ama modelde reddedilmişse yeni bulgu `sampling_field_rejected_by_model`.
   Mesaj modeli ve alanı adlandırır, çareyi söyler (alanı kaldır ya da başka model). Ayrı kod,
   çünkü çare farklıdır: yüzey reddinde sağlayıcı, model reddinde model değişir.
2. **Değer sınırları (F-607).** `Tracon.Abstractions`'ta `sealed record SamplingLimits`
   (`long? MinSeed`, `long? MaxSeed`, `int? MaxStopSequences`; `static None`). `ISamplingSupport`'a
   varsayılan gövdeli üye: `SamplingLimits SamplingLimits => SamplingLimits.None;`.
   `SamplingSupportCheck` sınır dışı değere `sampling_value_out_of_range` bulgusu verir (alan,
   değer, sınır, sağlayıcı). Sevk edilen beyanlar: Google `Seed` `int` aralığı; diğer
   sağlayıcıların `StopSequences` adedi **ölçülmeli** (belgelenmiş değer + gerçek `400`; ölçülemezse
   `null` kalır, denetlenmez).
3. **Konsol.** `seed` metin olarak doğrulanır: `Number.isSafeInteger` dışı değer gönderilmez
   (`en.ts` + `tr.ts` hata metni). Yüklenen tanımın `seed`'i güvenli aralık dışındaysa form
   alanı uyarır ve değeri yeniden girilmeden kaydetmez (yuvarlanmış değer sessizce yazılmaz).
   Sunucu `long`'u olduğu gibi kabul etmeye devam eder.
4. **`GET /api/models`** (`ModelProviderDescriptor` listesi, `CatalogEndpoints.cs:34`):
   `RejectedSamplingFields` `ModelDescriptor` ile kendiliğinden serileşir; sınırların bu yanıta
   girip girmeyeceği Açık Soru 2'dir.

Hosted tool'un model düzeyi reddi (`hafiza/hosted-tool.md:80`) aynı sınıftır ama bu fazda
**değildir**; ayrı bir alan ister (`IHostedToolSupport`). Kapanışta aday olarak yazılır.

## Bitiş Ölçütleri (DoD)

- [ ] Katalogda `RejectedSamplingFields: "StopSequences"` olan modelde `stop` yazan tanım `POST /api/agents/validate`'te `sampling_field_rejected_by_model` verir ve kaydı `400` döner; `Fallbacks` bağlantısı da denetlenir
- [ ] `RejectedSamplingFields` boşken davranış bugünküyle aynı (regresyon testi)
- [ ] Google'a `seed: 3000000000` yazan tanım `sampling_value_out_of_range` verir; sınır beyan etmeyen sağlayıcıda değer denetlenmez
- [ ] Konsol `9007199254740993` `seed`'ini göndermez ve hatayı iki dilde gösterir; güvenli aralık dışı yüklenen değer sessizce yeniden yazılmaz
- [ ] `docs/sozlesme/dogrulama-kodlari.txt` iki yeni kodu taşır; OpenAPI ve iki istemci yenilendi
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Model reddi birincil ve yedek bağlantıda bulgu verir | Birim | `SamplingSupportCheckTests` |
| Kayıt `400`, doğrulama raporu kodu ve yolu (`model.stopSequences`) | Fonksiyonel | `AgentValidationEndpointTests` ya da `SamplingFieldEndpointTests` (Faz 225 emsali) |
| Katalog bağlayıcısı `RejectedSamplingFields`'ı okur; okunamayan metin açılışı durdurur | Birim | `ProviderRegistrationCore` model bağlama testleri + `UnreadableProviderConfiguration` probu |
| Sınır dışı `Seed`/`StopSequences` adedi bulgu verir; sınırsız sağlayıcı denetlenmez | Birim | `SamplingSupportCheckTests` |
| `ISamplingSupport`'u eskisi gibi uygulayan üçüncü taraf sağlayıcı derlenir ve sınırsız sayılır | Birim | varsayılan üye testi (`SamplingLimits.None`) |
| Konsol güvenli tamsayı dışı `seed`'i göndermez | Vitest | `model.test.ts` |
| Konsolda hata metni ve alan davranışı | E2E | ajan düzenleyici `seed` alanı |

Beş soru: iptal — yeni yol yok · eşzamanlılık — tanımlayıcılar değişmez okunur · boş/aşırı girdi — `long.MinValue`/`MaxValue`, boş liste, `None` · başka kiracı — katalog kiracıdan bağımsızdır (`ISamplingSupport` remarks) · alt sistem hatası — katalogda model yoksa denetim atlanır (bugünkü `SupportsSampling` kuralı). `🤖` case: gerçek OpenAI'de `gpt-5.4-mini` + `stop` → katalog satırı yazılınca validate reddeder, satır yokken run `400` alır.

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Shipped katalog örneği (`samples/Tracon.Api` `appsettings`) `gpt-5.4-mini` için `RejectedSamplingFields: "StopSequences"` yazsın mı? | A: evet, ölçülen tek ret · B: hayır, katalog tüketicinin | **A** — örnek, ölçülen gerçeği gösterir; K-032 kodda liste yasaklar, örnek yapılandırma yasak değildir |
| 2 | Sınırlar `GET /api/models` yanıtında (`ModelProviderDescriptor`) görünsün mü? | A: evet, konsol sınırı ipucu olarak gösterir · B: yalnız doğrulama bulgusu | **B** bu fazda — yüzeyi büyütmeden; konsol ihtiyacı ölçülürse aday |
| 3 | `MinSeed` gerekli mi (negatif tohum)? | A: `MinSeed` + `MaxSeed` · B: yalnız `MaxSeed` | **A** — Google `int` aralığı negatif değeri de sınırlar |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

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
