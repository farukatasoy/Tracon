# Faz 252 — Model Başına İstek Alanı Reddi ve Değer Sınırları

> **Durum:** ✅ Tamamlandı (2026-10-28)
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

- [x] Katalogda `RejectedSamplingFields: "StopSequences"` olan modelde `stop` yazan tanım `POST /api/agents/validate`'te `sampling_field_rejected_by_model` verir ve kaydı `400` döner; `Fallbacks` bağlantısı da denetlenir — `SamplingSupportCheckTests` (derleme, doğrulama, yedek bağlantı; birim), `ModelRejectedFieldTests` (HTTP: gerçek `UseOpenAI` bağlayıcısı, doğrulama kodu ve yolu, kayıt `400` ve kayıt yok, yedek bağlantı), canlı örnek uygulama (`MT-OAI-098`, 2026-10-27: `validate` `valid:false` · kayıt `400` · `GET` `404` · yedek bağlantı aynı kod)
- [x] `RejectedSamplingFields` boşken davranış bugünküyle aynı (regresyon testi) — `A_catalog_that_names_no_rejected_field_changes_nothing`, `A_model_the_catalog_does_not_list_is_not_checked`, `The_same_definition_on_a_model_the_catalog_does_not_name_is_valid`; mevcut `SamplingSupportTests` ve `SamplingCapabilityTests` (33 test) değişmeden geçer; `Tracon.Core.UnitTests` 15.774 test (üç hedefte 5.258; denetim bir test ekledi: `A_provider_whose_limits_member_returns_null_states_no_limit_and_does_not_break_the_check`), 0 kırmızı
- [x] Google'a `seed: 3000000000` yazan tanım `sampling_value_out_of_range` verir; sınır beyan etmeyen sağlayıcıda değer denetlenmez — `SamplingSupportCheckTests` (sınır dahil/dışı değerler, sınırsız sağlayıcı), `ModelSamplingLimitsTests` (gerçek sağlayıcılar: `google` reddeder; `openai` 64 bit seed, uyumlu sunucu, `anthropic` denetlenmez), `GoogleSamplingLimitsTests` (telde `-1294967296`), canlı örnek uygulama
- [x] Konsol `9007199254740993` `seed`'ini göndermez ve hatayı iki dilde gösterir; güvenli aralık dışı yüklenen değer sessizce yeniden yazılmaz — Vitest: `model.test.ts` "seed" (7), `agent-editor.test.tsx` "seed range" (3) ve — denetim 🟡1 sonrası — "Retry after a failed request" (3: Retry yalnız form geçerliyken görünür), tüm paket 531/531; Playwright: `AgentTests` üç yeni test (yazılan seed · Türkçe metin sözlükten okunur · API'nin yazdığı `9007199254740993` yuvarlanmış okunur, Save kapalı, sunucudaki değer değişmez); tüm E2E projesi 108/108 (denetimden önce; hata metninin ikinci cümlesi sonradan değişti ve E2E yeniden koşulmadı — `SeedRangeError` sabiti sözlük metniyle bire bir eşit doğrulandı)
- [x] `docs/sozlesme/dogrulama-kodlari.txt` iki yeni kodu taşır; OpenAPI ve iki istemci yenilendi — `ValidationCodeSnapshotTests` ve `ConfigurationKeySnapshotTests` (`TRACON_SOZLESME_REFRESH=1`), `docs/openapi/tracon.json` (`TRACON_OPENAPI_REFRESH=1`, +8 satır), `schema.ts` (`npm run generate`) ve `TraconApiClient.g.cs` (NSwag zinciri, +7 satır); `Tracon.Client.UnitTests` 28/28
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban cbf772e8` entegrasyon ağacında (faz 252 birleştirmesi ve doküman commit'leri) — 29.537 test, 39 modül, 1 atlandı (koşullu), 0 kırmızı; `dotnet format --verify-no-changes`, `npm run check` (site build, bağlantı, SEO, ağırlık, içerik), betik testleri, `kapi.py tarama` ve `dokuman-bakim.py --denetle` yeşil (2026-10-28)

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

Üçü de uygulamada kapandı (2026-10-27): her soruda **önerilen** seçenek alındı.

| # | Soru | Seçenekler | Karar |
|---|---|---|---|
| 1 | Shipped katalog örneği (`samples/Tracon.Api` `appsettings`) `gpt-5.4-mini` için `RejectedSamplingFields: "StopSequences"` yazsın mı? | A: evet, ölçülen tek ret · B: hayır, katalog tüketicinin | **A** — ölçülen üç modelin satırı işaretlidir: `gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`. İlk uygulamada yalnız `gpt-5.4-mini` işaretlenmişti; denetimden sonra diğer ikisi eklendi (Plandan Sapma 13). Gerçek OpenAI'de ölçüldü: üç hesap modeli de `stop`'u `unsupported_parameter` ile reddeder |
| 2 | Sınırlar `GET /api/models` yanıtında (`ModelProviderDescriptor`) görünsün mü? | A: evet, konsol sınırı ipucu olarak gösterir · B: yalnız doğrulama bulgusu | **B** — `ModelProviderDescriptor.SamplingLimits` `internal`'dır. `ModelRejectedFieldTests.The_model_list_carries_the_rejected_fields_as_names_and_no_limits` yanıtta sınır anahtarı olmadığını kilitler |
| 3 | `MinSeed` gerekli mi (negatif tohum)? | A: `MinSeed` + `MaxSeed` · B: yalnız `MaxSeed` | **A** — Google için ölçüldü: `-2147483648` ve `-5` kabul edilir, `-2147483649` adaptörde sarar |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

1. **Bayrak enum'unun tel biçimi planda yoktu.** Plan "`ModelDescriptor` ile kendiliğinden serileşir" dedi. `SamplingFields` tip düzeyinde `JsonStringEnumConverter<SamplingFields>` taşımazsa `GET /api/models` sayı yazar (`4`). Öznitelik eklendi. Bayrak enum'unun şeması `type: string`'dir (kapalı değer listesi yoktur); değer `"Seed, StopSequences"`, boş küme `"None"` olur. `Tracon.Client`'ta alan `string`'dir. Mutasyonla kanıtlandı: öznitelik kalkınca `The_model_list_carries_the_rejected_fields_as_names_and_no_limits` düşer.
2. **Katalog okuyucusu tanımsız bitleri reddeder.** Plan "`Enum<SamplingFields>` ile okur" dedi; `ConfigurationValueBatch`'te bu okuyucu yoktu, eklendi. `ConfigurationValueKinds.TryEnum` bayrak enum'unda her sayıyı kabul eder: `"256"` hiçbir alan adlandırmayan bir küme olurdu ve hiçbir şeyi denetlemezdi. Katalog yolunda tanımsız bit okunamaz sayılır ve açılış durur (`TryDefinedEnum`). `TryEnum` değişmedi; virgüllü liste ve "okunamayan değer açılışı durdurur" kuralı aynı.
3. **Üç bulgu türü, tek kural.** `SamplingSupportCheck.Unsupported` → `Find`. `Unsupported`, `RejectedByModel` ve `OutOfRange` ortak tabandan türer ve kendi kodunu taşır; doğrulayıcı `Code = finding.Code` yazar (`SamplingFieldUnsupportedCode` sabiti kalktı). Öncelik planda yoktu: yüzeyin düşürdüğü alan model reddi olarak ayrıca raporlanmaz; model reddi olan alan değer sınırı için ayrıca raporlanmaz. Çare tektir (alanı kaldır); ikinci bulgu yazarı yanlış çareye gönderirdi. `ModelCatalogLookup.Find`'a sağlayıcı listesi alan aşırı yükleme eklendi.
4. **OpenAI sınırı yalnız OpenAI'nin kendi hizmeti için bildirilir.** Plan "diğer sağlayıcıların `StopSequences` adedi ölçülmeli" dedi. `UseOpenAICompatible()` sunucusu ya da özel `Endpoint`'li `openai` dört diziden fazlasını kabul edebilir; dar sınır çalışan tanımı reddederdi. Faz 245'in `IsOpenAIService` emsali kullanıldı. Sağlayıcı kiracıdan bağımsızdır; BYOK kiracının uç nokta geçersiz kılması da aynı sınıra tabidir (tasarım kabulü).
5. **Anthropic sınır bildirmez.** Ölçüm: `stop_sequences` sınırı sayı değil **toplam karakter uzunluğudur** (2.500). `MaxStopSequences` sayıdır ve bunu söyleyemez; sınırsız bırakıldı. Sınıf aday olarak yazıldı (🟢 Entegrasyon Notları).
6. **OpenAI'nin dört dizi sınırı gerçek `400` ile kanıtlanamadı.** Hesabın üç modeli (`gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`) `stop`'u önce reddeder; model düzeyindeki ret dizi sayısı denetiminden önce gelir. Sınır belgelenmiş değerdir (OpenAI SDK XML'i ve OpenAI/Azure OpenAI v1 OpenAPI şeması: "Up to 4 sequences"). Plan bu yolu açıkça verdi ("belgelenmiş değer"). Azure için gerçek çağrı yoktur (kimlik yok).
7. **Google için gerçek-adaptör wire testi eklendi.** Plan "Google için yerel sahte sunucu yok" diyen Faz 225 notuna dayanıyordu. Paylaşılan `StubHttpServer` yeter: `GoogleSamplingLimitsTests` gerçek Google.GenAI 1.26.1 adaptörünü yerel sunucuya karşı koşar ve gövdeyi okur (`3000000000` telde `-1294967296`). Faz 225 denetimi 🟢9 (decompile) böylece telden de doğrulandı.
8. **Konsol: gönderim `Number` olarak kalır, yalnız güvenli aralıkta.** Plan "`Seed`'i JS `Number`'a çevirmeden metin olarak taşır" dedi. Form `seed`'i zaten metin tutuyordu; kusur gönderimdeki `Number(text)`'ti. Sunucu JSON sayısı bekler; `"9007199254740993"` dizisi göndermek sözleşmeyi değiştirirdi. Karar: metin önce doğrulanır, yalnız `Number.isSafeInteger` geçen metin sayıya çevrilir, geçmeyen hiçbir yolla gönderilmez. Yüklenen kayıtlı `seed` tarayıcının `JSON.parse`'ında zaten yuvarlanır; formun elindeki sayı kayıtlı değer **değildir**. Yuvarlanmış değer aynı denetimden geçip düşer, ayrı bir "yüklendi" bayrağı gerekmedi.
9. **Ekran görüntüleri yeniden üretilmedi; kaynak damgası yenilendi.** UI kaynağı değişti, bu yüzden `docs-site/public/screenshots/.ui-source.sha256` değişti. Yakalanan hiçbir ekran değişmedi: yeni hata yalnız geçersiz `seed`'de çıkar ve alan kapalı `<details>` grubunun içindedir. Entegrasyonda diğer fazlar da UI kaynağını değiştirirse damga yeniden hesaplanır (`node docs-site/scripts/refresh-console-screenshot-stamp.mjs`). Denetim düzeltmesi UI kaynağını yeniden değiştirdi (Retry kapısı, `agentEditor.seedError` metni); damga ikinci kez yenilendi (`2a2ff9d1`), `checkConsoleScreens` 0 hata verir. Yakalanan ekranlar yine değişmedi: Retry yalnız başarısız bir kayıttan sonra, hata metni yalnız geçersiz `seed`'de görünür.
10. **`capabilities.md` değişmedi.** Agent haritası bütçenin içinde ama sınırda (`Tracon.AgentMap.md` 11.256 / 11.264 B); "Request fields" satırına eklenecek bir cümle bütçeyi aşar, bütçeyi yükseltmek ayrı bir ölçümlü karardır. Mevcut satır yeni kuralla çelişmez. 🟢 aday olarak yazıldı.
11. **`PublicSurfaceBaselineTests` tabanı `Tracon.Abstractions` 408 → 409** (`SamplingLimits`). Tip sayısı ratchet'inin bilinçli artışıdır.
12. **`docs-site` senkron denetiminde üç kural çıktı.** `arayuz` → `ui.md` ("Editor boundaries": seed sınırı) ve `cekirdek-kavram` → `concepts/agents.md` ("Compiling a definition": istek alanı denetimi) güncellendi. `paket-readme` → `packages.md` **değişmedi**: `packages.md` her paketin ne işe yaradığını yazar; OpenAI README'sine eklenen cümle bir katalog anahtarını anlatır ve `guides/model-providers.md` ile `reference/configuration.md` onu karşılar. Bir paketin rolü değişmedi. Denetim 🟡1 aynı README cümlesini düzeltti (üç kapı, `3a68f422`); site katalog tablosu zaten üçünü sayıyordu ve `packages.md` yine değişmedi.
13. **Örnek katalog üç ölçülen modeli işaretler (denetim 🟡 sonrası).** Açık Soru 1 A yalnız `gpt-5.4-mini` satırını değiştirdi; `gpt-5.6-luna` ve `gpt-5.6-terra` işaretsiz kaldı, oysa ikisi de aynı ölçümle `stop`'u `unsupported_parameter` ile reddediyordu. Örnek uygulamada bu iki model kayıt anında `stop` kabul edip ilk run'da düşerdi: fazın kapattığı kusur sınıfı. İkisinin satırına `"RejectedSamplingFields": "StopSequences"` yazıldı. `MT-OAI-098` adım 4'ü `gpt-5.6-luna`'yı işaretsiz varsayıyordu; adım artık luna satırını ortam değişkeniyle boşaltır (`Tracon__Providers__OpenAI__Models__1__RejectedSamplingFields=`), adım 5'in satır 0 tekniğiyle aynı. İkinci etki: örnek uygulamada `openai` için beş `stop` dizisi artık `sampling_value_out_of_range` değil `sampling_field_rejected_by_model` verir (model reddi aralığın önünde gelir, Sapma 3); aralık bulgusu yalnız katalogda olmayan bir model adıyla görünür. Adım 1–4 ve aralıklar yeni katalogla yeniden koşuldu (gerçek çağrı yok).

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **Açık Sorular 1–3:** önerilen seçenekler alındı (A · B · A); ayrıntı "Açık Sorular" bölümündedir.
- **Model reddi katalogdadır, değer sınırı sağlayıcıdadır.** Model reddi `ModelDescriptor.RejectedSamplingFields` (varsayılan `None`, denetlenmez); değer sınırı `ISamplingSupport.SamplingLimits` (varsayılan gövdeli üye, varsayılan sınırsız). İkisi de `SamplingSupportCheck`'in bulgusudur ve derleyici ile doğrulayıcı aynı kuralı okur.
- **Yalnız ölçülmüş ya da belgelenmiş sınır bildirilir.** `google`: 32 bit `seed`, beş `stop`. `openai` (yalnız OpenAI'nin kendi hizmeti) ve `azure-openai`: dört `stop`. `anthropic` ve uyumlu sunucular: sınır yok.
- **Sınır tele açılmaz** (Açık Soru 2 B). `GET /api/models` yalnız `rejectedSamplingFields` taşır.
- **Konsol güvenli aralık dışı `seed`'i hiçbir yolla göndermez** ve yuvarlanmış okunan kayıtlı değerle kaydetmez; çare `seed`'i değiştirmek ya da silmektir. 64 bit `seed` HTTP API'den hâlâ kabul edilir.

### Ölçümler (uygulama oturumu, 2026-10-27)

Plan iki ölçümü bu oturuma bıraktı. Gerçek çağrılar küçük istemlerle yapıldı; `secret` hiçbir yere yazılmadı.

| Ölçüm | Sonuç | Kanıt |
|---|---|---|
| Google.GenAI 1.26.1 `ChatOptions.Seed`'i nasıl daraltır | Denetimsiz `(int)` dönüşümü (`GoogleGenAIChatClient` decompile): `3000000000` telde `-1294967296`, `int.MaxValue + 1` → `int.MinValue`, `int.MinValue - 1` → `int.MaxValue` | `GoogleSamplingLimitsTests` — gerçek adaptör, yerel `StubHttpServer`, gövdeyi okur |
| Gemini API `seed` | int32: `-5`, `-2147483648`, `2147483647` → `200`; `3000000000` → `400 Invalid value at 'generation_config.seed' (TYPE_INT32)` | 4 gerçek çağrı (`gemini-3.6-flash`, `maxOutputTokens: 8`) |
| Gemini `stopSequences` | Altı öğeli istek → `400 the number of stop_sequences must not exceed 5`; sınırı API'nin kendi hata metni (ve başvuru belgesi) verir. Beş öğeli isteğin kabulü ölçülmedi: yalnız altı öğeli istek gönderildi | 1 gerçek çağrı (istek reddedildi, token üretilmedi) |
| OpenAI Chat `stop` sayısı | Belgelenmiş: dört (OpenAI SDK XML'i ve OpenAI/Azure OpenAI v1 şeması: "Up to 4 sequences"). Gerçek `400` **kanıtlanamadı**: hesabın üç modeli `stop`'u hiç kabul etmez ve ret dizi sayısı denetiminden önce gelir | 3 gerçek çağrı (`400 unsupported_parameter`, token yok) |
| Azure OpenAI `stop` sayısı | Belgelenmiş: dört (`azure-v1-v1-generated.json`, `OpenAI.StopConfiguration`). Gerçek çağrı yok (kimlik yok) | spec |
| Anthropic `stop_sequences` | Sınır **toplam karakter uzunluğudur**, sayı değil: 10.000 öğe, 2.500 × 40 karakter, 2.501 × 5 karakter, 100 × 40 karakter ve 2.501 × 1 karakter → `400 stop_sequences: total length: 0..2,500`; 2.500 × 1 karakter ve 60 × 40 karakter → `200` | 7 gerçek çağrı (`claude-haiku-4-5-20251001`, `max_tokens: 1`) |
| `ISamplingSupport` ikili uyumu | Base commit'in `Tracon.Abstractions.dll`'ine (tek üyeli arayüz) karşı derlenmiş bir sağlayıcı yeni `Tracon.Abstractions.dll`'in yanında yüklendi: `SamplingLimits` varsayılan gövdeden `SamplingLimits.None` döndü, istisna yok | Tek seferlik prob (iki küçük proje, scratchpad). Kalıcı karşılığı kaynak uyumu: `SamplingSupportCheckTests.A_provider_written_before_the_limits_member_existed_still_works_and_states_none` |
| `gpt-5.4-mini` + `stop`, örnek uygulama | Satır varken `validate` reddeder, kayıt `400` döner; satır yokken kayıt `201`, run `Failed` ve günlükte `HTTP 400 (invalid_request_error: unsupported_parameter)` | `MT-OAI-098` (bkz. DoD kanıtı) |

### Canlı koşum kanıtı (2026-10-27)

`samples/Tracon.Api` derlenmiş DLL'den, `ASPNETCORE_ENVIRONMENT=Development`, port `5182`, dört boş ortam değişkeniyle (bellek içi depo); `Npgsql` günlük satırı `0`. Çıktılar kısaltıldı.

İki koşum var. **İlk koşum** gerçek `stop` çağrısını içerir; katalogda o gün yalnız `gpt-5.4-mini` işaretliydi. **İkinci koşum** denetim düzeltmesinden sonradır (Plandan Sapma 13): uygulama üç işaretli satırla yeniden başlatıldı, `validate` adımları ve aralıklar yeniden koşuldu; gerçek çağrı yoktur.

Üç satır işaretliyken (`gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra` → `RejectedSamplingFields: "StopSequences"`; ikinci koşum):

```text
GET  /tracon/api/models           openai: [('gpt-5.4-mini','StopSequences'), ('gpt-5.6-luna','StopSequences'), ('gpt-5.6-terra','StopSequences')]
                                    yanıtta samplingLimits anahtarı yok
POST /tracon/api/agents/validate  {"valid": false, "messages": [{"severity": "Error", "code": "sampling_field_rejected_by_model",
                                    "message": "ModelBinding.StopSequences is set, but the model catalog states that model 'gpt-5.4-mini' of the
                                    'openai' provider rejects it (the provider would answer the request with 400). Remove it from the definition
                                    or use another model.", "path": "model.stopSequences"}]}
                                    gpt-5.6-luna ve gpt-5.6-terra: aynı kod ve yol
POST /tracon/api/agents           HTTP 400 | title: Definition invalid | detail: <aynı mesaj>
GET  /tracon/api/agents/stop-probe HTTP 404
```

`gpt-5.6-luna` satırı ortam değişkeniyle boşaltılıp (`Tracon__Providers__OpenAI__Models__1__RejectedSamplingFields=`) uygulama yeniden başlatıldığında (ikinci koşum):

```text
GET  /tracon/api/models           openai: [('gpt-5.4-mini','StopSequences'), ('gpt-5.6-luna','None'), ('gpt-5.6-terra','StopSequences')]
fallback gpt-5.4-mini (birincil gpt-5.6-luna)  -> INVALID [('sampling_field_rejected_by_model', 'model.stopSequences')]  (mesaj 'gpt-5.4-mini' adlı)
aynı gövde, yedeksiz                           -> valid
gpt-5.6-terra + stop                           -> INVALID [('sampling_field_rejected_by_model', 'model.stopSequences')]
```

Aralıklar (aynı örnek uygulama, üç işaretli satırla; ikinci koşum, `validate`):

```text
google  seed 3000000000          -> sampling_value_out_of_range @ model.seed
        "ModelBinding.Seed is 3000000000, but the 'google' provider accepts only values from -2147483648 to 2147483647."
google  6 stopSequences          -> sampling_value_out_of_range @ model.stopSequences ("has 6 entries ... at most 5")
google  seed 2147483647 + 5 stop -> valid          openai seed 3000000000 -> valid (64 bit)
openai  5 stopSequences, katalogda olmayan model -> sampling_value_out_of_range ("has 5 entries ... at most 4")
openai  5 stopSequences, gpt-5.6-luna (işaretli)  -> sampling_field_rejected_by_model (model reddi aralığın önünde gelir)
anthropic 50 stopSequences       -> valid
```

İlk koşumda, satır boşaltılıp (`Tracon__Providers__OpenAI__Models__0__RejectedSamplingFields=`) uygulama yeniden başlatıldığında: katalogda `gpt-5.4-mini` → `None`; `validate` `valid: true`; kayıt `201`; `POST /api/agents/stop-probe/run` (tek gerçek çağrı):

```text
event: error
data: {"type":"ProviderInvocationException","message":"The model provider request failed. Provider: 'openai', fault: 'ClientResultException' (HTTP 400)."}
run 01a17e14-… status Failed, error class ProviderError
konak günlüğü: HTTP 400 (invalid_request_error: unsupported_parameter) — Unsupported parameter: 'stop' is not supported with this model.
```

### Tüketici Yüzeyi Envanteri

1. **`docs-site/` — elle yazılan:** `guides/model-providers.md` (§ *Sampling and tool-use fields*, katalog tablosu), `reference/configuration.md` (katalog anahtarı), `ui.md` (*Editor boundaries*), `concepts/agents.md` (*Compiling a definition*). **Üretilen, dokunulmadı:** `api/` (DocFX; yeni `///` blokları üretir), `http-api/` (OpenAPI'den). `capabilities.md` değişmedi (Plandan Sapma 10). `llms.txt`, `llms-full.txt` ve `Tracon.AgentMap.md` entegrasyonda `kapi.py uret` ile yenilenir.
2. **Sevk edilen metin:** `SamplingLimits` (yeni), `ISamplingSupport.SamplingLimits`, `ModelDescriptor.RejectedSamplingFields` ve `SamplingFields` özetinin `///` blokları; `src/Tracon.OpenAI/README.md` (katalog cümlesi); `docs/openapi/tracon.json`, `packages/tracon-client/src/schema.ts`, `src/Tracon.Client/Generated/TraconApiClient.g.cs`; `docs/sozlesme/dogrulama-kodlari.txt` (iki kod) ve `yapilandirma.txt` (dört anahtar).
3. **Yerel referans ve agent haritası:** yeni `Add*`/`Use*`/`Map*` giriş noktası yok, `<example>` gerekmedi; `buildTransitive` dosyaları değişmedi.

### Entegrasyon Notları

Aşağıdakiler entegratörün elle işleyeceği kalemlerdir (bu fazın yasak dosyaları).

**(a) CHANGELOG — `[Unreleased]`**

`### Added` altına:

- **A catalog entry can name the request fields its model rejects: `ModelDescriptor.RejectedSamplingFields`.** A model decides what it accepts, not the API surface: `openai` sends `stop`, and the three models of the OpenAI account used for the measurement answer it with `400 unsupported_parameter`. `Tracon:Providers:{Provider}:Models:{n}:RejectedSamplingFields` takes a name or a comma-separated list (`"StopSequences"`, `"Seed, StopSequences"`); a name that does not exist, and a number with bits no field owns, stop startup. A definition that sets a listed field for that model, on the primary model or on a `Fallbacks` link, does not compile; `POST /api/agents/validate` reports `sampling_field_rejected_by_model` at the field's path (`model.stopSequences`) and a save returns `400`. Left out (`None`), the catalog checks nothing, as before. `GET /api/models` returns the value as a string of names, and `Tracon.Client` has it as `ModelDescriptor.RejectedSamplingFields` (`string`). The sample catalog marks the three measured models.
- **A provider can state the value ranges its API surface accepts: `ISamplingSupport.SamplingLimits`.** The new `SamplingLimits` record (`MinSeed`, `MaxSeed`, `MaxStopSequences`) states no limit by default, and the member has a default body, so an `ISamplingSupport` implementation written earlier compiles and runs unchanged. A definition whose seed or number of stop sequences is outside a stated range does not compile, validation reports `sampling_value_out_of_range` at the field's path, and a save returns `400`; a `Fallbacks` link is held to the range of its own provider. `google` states a 32-bit seed and five stop sequences; `openai` (OpenAI's own service, not a compatible server) and `azure-openai` state four stop sequences; `anthropic` and compatible servers state none. The Google.GenAI client library does not check the seed: it narrowed `3000000000` to `-1294967296` and the run went on. The ranges are not part of `GET /api/models`.

`### Fixed` altına:

- **The agent editor of the console no longer rounds a seed it cannot hold exactly.** A seed above 9007199254740991 in absolute value went out as another number (`9007199254740993` as `9007199254740992`), and a stored seed of that size came back rounded and was written back the same way. The editor checks the text before it makes a number: it shows the error in English and Turkish, marks the field invalid, and keeps Validate, Save and the Try again button of a failed request off until the seed is replaced or cleared. The HTTP API still takes a 64-bit seed.

`### Upgrading` altına:

- **A saved agent for the `google` provider with a seed outside the 32-bit range no longer builds.** The Google.GenAI client library narrowed such a seed without a check (`3000000000` reached the model as `-1294967296`), so a definition saved earlier ran with a seed nobody had written. `POST /api/agents/validate` now reports `sampling_value_out_of_range` at `model.seed`, and a run of the stored agent fails with the compile error. Change the seed to a value from -2147483648 to 2147483647, or clear it.

Başka `### Upgrading` kalemi yok: `ISamplingSupport` üyesi varsayılan gövdelidir, kaynak ve ikili uyum korunur (`SamplingSupportCheckTests.A_provider_written_before_the_limits_member_existed_still_works_and_states_none`); sınır aşan diğer tanımlar (Google'da beşten, OpenAI ve Azure'da dörtten fazla `stop`) sağlayıcının kendi `400`'üyle zaten düşüyordu; şimdi derlemede düşer. OpenAI ve Azure sınırı belgelenmiş değerdir, gerçek `400` ile kanıtlanmadı (Sapma 6).

**(b) K-* taslakları** (`docs/KARARLAR.md` §2, şablon §3; numarayı entegratör verir)

| **K-NEW-1 — Model reddi katalogda `RejectedSamplingFields` ile yazılır; denetim yüzey desteği − model reddidir (F-606)** *(kategori: public-api)* | 2026-10-27 | Alternatif: kodda model adı listesi (K-032 yasaklar) · alan başına `bool?` bayrağı (her alan yeni public üye ister) · sağlayıcının `400`'üne bırakmak (tanım ilk run'da düşer). Ölçüldü: üç hesap modeli `stop`'u `400 unsupported_parameter` ile reddeder. Telde küme `"Seed, StopSequences"`, şema `type: string`. | Sağlayıcı model başına yetenek ucu sunarsa |
| **K-NEW-2 — Değer sınırı `ISamplingSupport.SamplingLimits` ile bildirilir; yalnız ölçülmüş ya da belgelenmiş sınır yazılır (F-607)** *(kategori: public-api)* | 2026-10-27 | Alternatif: sınırı her sağlayıcının kendi içinde atması (üçüncü taraf yol kalmaz; kayıt defteri reddi maskeler — bakımcı kararı) · `IModelProvider`'a üye (kırıcı). Ölçüldü: Google.GenAI 1.26.1 `Seed`'i `(int)` ile daraltır (`3000000000` → `-1294967296`); Anthropic sayıyı değil toplam uzunluğu (2.500) sınırlar. | SDK daraltmayı kaldırırsa |

**(c) Hafıza notları** (alan dosyasına; her biri kısa). Bütçe uyarısı (`--denetle`, 2026-10-27): `agent-derleme-ve-onbellek.md` 15.917 / 16.000 B, `frontend.md` 15.857 / 16.000 B, `aspnetcore-json.md` 15.264 / 16.000 B; her not ~500 B tutar, bu yüzden eski içerik önce `docs/arsiv/`'e taşınır.

- `docs/hafiza/aspnetcore-json.md` — *🚨 Bayrak (`[Flags]`) enum'u telde de `JsonStringEnumConverter<T>` ister ve şema `type: string` çıkar* (Faz 252): `SamplingFields` `ModelDescriptor.RejectedSamplingFields` ile telde public oldu. Tip düzeyinde öznitelik yoksa `GET /api/models` sayı yazar (`4`) ve typed C# testleri bunu görmez. Değer `"Seed, StopSequences"`; bayrak enum'unun OpenAPI şeması kapalı `enum` listesi taşımaz, istemcilerde `string` olur. Kapı: `ModelRejectedFieldTests` ham JSON okur; öznitelik kaldırılınca düşer (mutasyonla ölçüldü).
- `docs/hafiza/secenek-baglama-ve-gizlilik.md` — *Katalog okuyucusu bayrak enum'unda tanımsız biti reddeder* (Faz 252): `ConfigurationValueKinds.TryEnum` bayrak enum'unda her sayıyı kabul eder (`"256"` boş bir küme olurdu). `ConfigurationValueBatch.Enum<T>` `TryDefinedEnum` kullanır. `ConfigurationValueReader<T>.Enum<T>` (seçenek bağlayıcıları) hâlâ yalnız `TryEnum` kullanır; iki yol aynı kurala bağlanabilir (🟢 aday).
- `docs/hafiza/openai-saglayici.md` — *🚨 OpenAI'de model düzeyi `unsupported_parameter` dizi sayısı denetiminden ÖNCE gelir* (2026-10-27, Faz 252): hesabın üç modeli beş `stop` ile de aynı `400 unsupported_parameter` verdi; dört dizi sınırı gerçek `400` ile kanıtlanamadı, belgelenmiş değerdir. `stop` kabul eden bir model erişilirse `MT-OAI-098`'in yanına eklenir. Sınır yalnız `api.openai.com` için bildirilir: uyumlu sunucu daha fazlasını kabul edebilir (`IsOpenAIService`).
- `docs/hafiza/model-boru-hatti.md` — *🚨 Bir SDK adaptörü sayıyı denetimsiz daraltabilir* (Faz 252): Google.GenAI 1.26.1 `ChatOptions.Seed`'i (`long?`) `(int)` ile daraltır; `3000000000` telde `-1294967296` olur ve run başarıyla biter. Gemini API `seed`'i int32 ister. Gerçek adaptör gövdesi yerel `StubHttpServer`'a karşı okunur (`GoogleSamplingLimitsTests`); bir SDK yükseltmesi daralmayı kaldırırsa ilk test düşer. Anthropic `stop_sequences` sayıyı değil toplam uzunluğu (2.500 karakter) sınırlar.
- `docs/hafiza/agent-derleme-ve-onbellek.md` — *`SamplingSupportCheck` üç bulgunun tek kuralıdır* (Faz 252): `Unsupported` (yüzey düşürür) > `RejectedByModel` (katalog) > `OutOfRange` (`SamplingLimits`). Bir alan yalnız en temel bulguyu alır. Yeni bir tür bulgu kendi `Code`'unu `ValidationCodes`'ta ve `ValidationCodeSnapshotTests` dosyasında ister; doğrulayıcı `finding.Code` yazar.
- `docs/hafiza/frontend.md` — *🚨 Sunucunun 64 bit alanı tarayıcıda `JSON.parse`'ta yuvarlanır* (Faz 252): `seed` `long?`; `9007199254740993` yanıtta `9007199254740992` olur ve form onu kayıtlı değer sanır. Gönderimdeki `Number(text)` aynı yuvarlamayı yapar. `isValidSeed` metni `Number.isSafeInteger` ile denetler; yuvarlanmış yüklenen değer aynı denetimden düşer, ayrı bayrak gerekmez. Konsola başka bir `long` alanı girerse aynı sınıf. Aynı nota (denetim 🟢3): *Retry, Save'in yeniden çağrısıdır ve `disabled` kapısını atlar* — kayıt başarısız olduktan sonra geçersiz yazılan `seed` ya da bozulan şema JSON'u istekten düşer ve Retry alanı silerek kaydeder. `onRetry` yalnız form geçerliyken verilir (`agent-editor.tsx`; emsal `workflow-editor.tsx`); kapı `agent-editor.test.tsx` "Retry after a failed request".

**(d) MIMARI / MAF dokümanları**

- `MIMARI*.md`: değişiklik gerekmez (`ISamplingSupport`, katalog ve doğrulama kodları orada anılmıyor; `git grep ISamplingSupport docs/MIMARI*.md` boş).
- `docs/MAF-KAPSAMA.md` §5 "`ChatOptions` alanları" satırı: "sağlayıcı desteği `ISamplingSupport`" cümlesine "; model reddi `ModelDescriptor.RejectedSamplingFields`, değer sınırı `SamplingLimits`" eklenir.
- `docs/MAF-GENISLEME-NOKTALARI.md` § *Sürüm damgası*: Google.GenAI 1.26.1 satırına "`Seed` `(int)` ile denetimsiz daraltılır (2026-10-27, wire testi `GoogleSamplingLimitsTests`)" eklenir.

**(e) 🟢 Aday bulgular** (yalnız rapor; uygulanmadı)

1. **Anthropic toplam uzunluk sınırı:** `stop_sequences` toplam 2.500 karakterle sınırlıdır; `SamplingLimits` bunu söyleyemez. Bir `MaxStopSequenceCharacters` üyesi eklenebilir.
2. **Hosted tool'un model düzeyi reddi:** `ModelDescriptor`'a `RejectedHostedTools` (ya da `IHostedToolSupport` ile uyumlu bir küme) — plan zaten kapanışta aday dedi (`hafiza/hosted-tool.md:80`).
3. **Konsol ipucu:** `rejectedSamplingFields` artık telde. Editör, seçili modelin reddettiği alanı önceden gösterebilir; ihtiyaç ölçülürse sınırlar da `GET /api/models`'e açılır (Açık Soru 2).
4. **Agent haritası bütçesi sınırda** (11.256 / 11.264 B): `capabilities.md` bir cümle bile büyütemez. Bütçe kararı ya da satır kısaltması gerekir.
5. **İki enum okuyucusu iki kural:** `ConfigurationValueReader<T>.Enum<T>` bayrak enum'unda tanımsız biti hâlâ kabul eder (doğrulayıcıya bırakılmış), `ConfigurationValueBatch.Enum<T>` reddeder.
6. **64 bit alanlar konsolda:** `seed` dışında konsolun okuduğu 64 bit alanlar (sayaçlar, tutarlar) `JSON.parse`'ta güvenli aralık dışında yuvarlanır; ölçülmedi.
7. **OpenAI dört dizi sınırının gerçek kanıtı:** `stop` kabul eden bir model erişilirse `array too long` `400`'ü ile kanıtlanır.
8. **`dokuman-bakim.py --denetle` üretilen sayfa olmadan kırmızıdır:** `reference/versioning.md` → `/reference/changelog/` dört bozuk bağlantı gösterir (taban commit'te de); sayfa `docs-site` build'inde üretilince denetim yeşildir (çıkış 0). Bu faz değiştirmedi.
9. **BYOK kiracının `Endpoint` geçersiz kılması kayıtlı sağlayıcının sınırına tabidir:** `SamplingLimits` sağlayıcı kaydından okunur; kendi uyumlu uç noktasına bağlanan bir kiracı, kayıtlı `openai` sınırıyla (dört `stop`) reddedilir. `SamplingLimits` XML'i bunu söyler; sabitleyen test yok, site sayfası sessiz. Daha iyisi API değişikliği ister (`ISamplingSupport` kiracı parametresi taşımaz). Bu faz dokunmadı (Sapma 4: tasarım kabulü); `guides/model-providers.md` üç fazın elindedir ve sayfa ağırlığı tavana yakındır (denetim 🟢1).
10. **Konsol `seed` metninde `+5` reddedilir:** düzenli ifade (`^-?\d+$`) yalnız `-` işaretine izin verir; `Number('+5')` güvenli `5` olurdu, kullanıcı yine de hata görür. Bilerek bırakıldı (denetim 🟢); ihtiyaç doğarsa ifade `^[+-]?\d+$` olur.

**(f) Birleştirme ve üretim notları**

- `src/Tracon.Abstractions/PublicAPI.Unshipped.txt`: üç yerel ekleme (`ISamplingSupport.SamplingLimits`, `ModelDescriptor.RejectedSamplingFields`, `SamplingLimits` bloğu; `override`/`static` satırları `RunVersionStatistics` komşuluğunda). `tests/Tracon.Core.UnitTests/Architecture/public-surface-baseline.txt` `Tracon.Abstractions=409`: diğer fazlar tip eklerse sayıyı `TRACON_PUBLIC_SURFACE_REFRESH=1` ile yeniden hesapla.
- Üretilen dosyalar birleştirmeden sonra aynı zincirle yenilenir: `docs/openapi/tracon.json` (`TRACON_OPENAPI_REFRESH=1`), `packages/tracon-client/src/schema.ts` (`npm run generate`), `TraconApiClient.g.cs` (NSwag zinciri), `docs/sozlesme/dogrulama-kodlari.txt` ve `yapilandirma.txt` (`TRACON_SOZLESME_REFRESH=1`).
- `docs-site/public/screenshots/.ui-source.sha256`: UI kaynağını başka faz da değiştirdiyse `node docs-site/scripts/refresh-console-screenshot-stamp.mjs` ile yeniden hesapla.
- `docs-site/public/llms.txt`, `llms-full.txt` ve `Tracon.AgentMap.md`: `kapi.py uret` (site sayfalarım değişti, `llms-full.txt` bayat kalır). `npm run check` bu dalda yalnız bu tek maddede kırmızıdır: `check:content` "llms-full.txt does not match capabilities.md" der; build, `check:links` (bozuk bağlantı yok), SEO ve `check:weight` (1053 sayfa tavanın altında; en ağır `concepts/governance` 58.194 B) yeşildir.
- `README.md` "an API reference for 625 public types" cümlesi 626 oldu (`SamplingLimits`). Başka faz da public tip eklerse sayıyı entegratör toplar.
- Sıcak dosyalar: `ModelDescriptor.cs` (yalnız `SupportsSampling`'in altı), `ProviderRegistrationCore.BindModels` (yalnız `SupportsSampling`'in altı), `ProviderRegistrationCoreTests` ve `UnreadableProviderConfiguration.AValidCatalogEntryBindsEveryField` (aynı komşuluk), `samples/Tracon.Api/appsettings.json` (OpenAI `Models` altında yalnız `gpt-5.4-mini`, `gpt-5.6-luna` ve `gpt-5.6-terra` satırlarının `RejectedSamplingFields` anahtarı), `docs/manuel-test/00-INDEKS.md` (yalnız `OAI`, `PROV`, `UIAG` satırları).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 13 sapma |
| Düzeltme turu sayısı | 1 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.

Bağımsız denetçi (2026-10-27, tam diff okundu): 🔴 yok, 🟡 iki, 🟢 birkaçı. Hepsi aynı gün, `faz/252` üzerinde işlendi.

| # | Önem | Bulgu | Sonuç | Kanıt |
|---|---|---|---|---|
| 🟡1 | 🟡 | `src/Tracon.OpenAI/README.md` "iki giriş denetlenir" diyordu; üçüncü kapı `SupportsStructuredOutput: false` da derlemeyi durdurur | **Düzeltildi** | `3a68f422`. Site (`guides/model-providers.md`) zaten "yalnız üçü kapıdır" diyordu. Aynı cümle başka sevk edilen metinde yok: Azure ve Anthropic README'si, `ModelDescriptor` XML'i (`git grep`) |
| 🟡2 | 🟡 | Kapanış kapısı kanıtsız (DoD son kutusu): `llms-full.txt` ve agent haritası entegrasyonda yenilenir, `kapanis` orada koşar | **Entegratör** | Kutu işaretsiz bırakıldı; Entegrasyon Notları (f) |
| 🟢1 | 🟢 | BYOK kiracının `Endpoint` geçersiz kılması kayıtlı sağlayıcının sınırına tabidir; sabitleyen test yok, site sessiz | **Aday** (dokunulmadı) | Entegrasyon Notları (e) 9. `SamplingLimits` XML'i durumu söyler; site sayfası üç fazın elinde ve ağırlık tavanına yakın |
| 🟢2 | 🟢 | `ModelProviderRegistry.List()` sağlayıcının `null` `SamplingLimits` yanıtını korumasız kopyalar; `SamplingSupportCheck` NRE verir, `validate` ve kayıt `500` döner | **Düzeltildi** | `c78e93ff`: `?? SamplingLimits.None`. Test `SamplingSupportCheckTests.A_provider_whose_limits_member_returns_null_states_no_limit_and_does_not_break_the_check`: koruma yokken kırmızı (41 testten 1), varken yeşil |
| 🟢3 | 🟢 | Konsolda Retry `valid` kapısını atlar: kayıt `400` verir, kullanıcı güvenli aralık dışı `seed` yazar (alan hatası, Save kapalı), Retry `seed: null` ile kaydeder ve kayıtlı `seed` silinir. Geçersiz şema JSON'u aynı yoldan düşer | **Düzeltildi** | `bc4cdd44`: Retry yalnız `editor.valid` iken sunulur (Save ve Validate). Vitest `agent-editor.test.tsx` "Retry after a failed request" 3 test (seed · şema JSON'u · başarısız Validate); düzeltme kaldırılınca üçü de kırmızı. E2E eklenmedi (istek üzerine). Damga `2a2ff9d1` |
| 🟢4 | 🟢 | Örnek katalog yalnız `gpt-5.4-mini`'yi işaretler; ölçüm üç hesap modelinin de `stop`'u `unsupported_parameter` ile reddettiğini gösterdi | **Düzeltildi** | `e7140e3e`: `gpt-5.6-luna` ve `gpt-5.6-terra` satırları. `MT-OAI-098` adım 4 luna satırını ortam değişkeniyle boşaltır; Açık Soru 1, Plandan Sapma 13, CHANGELOG taslağı "üç ölçülen model" der. Testler örnek dosyayı okumaz (`git grep`); canlı örnek uygulama yeniden koşuldu |
| 🟢5a | 🟢 | `ModelRejectedFieldTests.A_field_the_model_does_not_reject_stays_valid_next_to_one_it_does` yalnız `seed` kuruyordu | **Düzeltildi** | `fbf43dc3`: `stop: ["END"]` ve `seed: 7`; tek bulgu `model.stopSequences` |
| 🟢5b | 🟢 | Ölçümler tablosu Google için "beş kabul" yazıyordu; gönderilen tek canlı istek altı öğeliydi | **Düzeltildi** | Bu commit: satır yalnız ölçüleni yazar (altı öğe → `400`); beş öğenin kabulü ölçülmedi |
| 🟢5c | 🟢 | CHANGELOG taslağı "Upgrading: kalem yok" diyordu; `int32` dışı `seed`'li kayıtlı Google tanımı sessizce sarılarak koşardı, şimdi `sampling_value_out_of_range` verir | **Düzeltildi** | Bu commit: Entegrasyon Notları (a) taslağına İngilizce `### Upgrading` girdisi |
| 8 | isteğe bağlı | Konsol `seed` hata metninin ikinci cümlesi ("daha büyük sayılar") `abc` girdisinde yanlış okunur | **Düzeltildi** | `b59ec1e1`: en/tr metin, E2E `SeedRangeError` sabiti sözlük metniyle bire bir eşit (E2E yeniden koşulmadı) |
| — | — | Konsol `seed` ifadesi `+5`'i reddeder (JSON sayı dilbilgisinde `+` yok) | **Aday** (bırakıldı) | Entegrasyon Notları (e) 10 |

**Düzeltme turu doğrulaması (2026-10-27).** `kapi.py test`: `Tracon.Core.UnitTests` tümü her hedefte 5.258/5.258 (net8.0, net9.0, net10.0; mimari ratchet'ler dahil); aynı projede `*SamplingSupportCheck*` + `*SamplingSupportTests*` + `*ModelProviderRegistry*` her hedefte 92/92; `Tracon.OpenAI.UnitTests` `*ProviderRegistrationCore*` + `*OpenAIModelCatalog*` her hedefte 20/20; `Tracon.AspNetCore.FunctionalTests` `*ModelRejectedField*` + `*ModelSamplingLimits*` 25/25. Vitest: tüm paket 37 dosya, 531/531; `tsc --noEmit` temiz; `check-tokens`, `check-modal-layer`, `check-custom-classes` geçer. `dotnet format --verify-no-changes`: değişen dört `.cs` dosyası temiz. `checkConsoleScreens` 0 hata. Canlı örnek uygulama (gerçek çağrı yok, `Npgsql` günlük satırı `0`): üç satır işaretli katalog, `validate` ve kayıt, luna satırı boş yedek bağlantı, aralıklar ("Canlı koşum kanıtı", ikinci koşum).
