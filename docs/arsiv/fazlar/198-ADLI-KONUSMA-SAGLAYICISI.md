# Faz 198 — Adlı Konuşma Sağlayıcısı ve Kiracı Bağlaması

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-298**
> **Önkoşul:** Yok (F-336 kapandı). Örnek, Faz 195 düğüm ayarını; o kapanmazsa run parametresini (Faz 194) kullanır · [Faz 203](../../203-COK-KONUSMACILI-DIYALOG-SENTEZI.md) (diyalog yeteneği, F-296) bu fazdan **sonra** gelir ve § 198.6'nın bıraktığı yere `ISpeechDialogueSynthesizer`'ı ekler (2026-10-13, yeniden tartıldı)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Voice`, `.AspNetCore`, `.UI` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** Yok (Açık Soru 1-A; 1-B'de üç sağlayıcıda gerekir)
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `guides/voice.md`, `guides/model-providers.md` § BYOK, `getting-started/security.md`, `ui.md`, `capabilities.md` · sevk edilen: XML `<example>`, `src/Tracon.Voice/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/19-COK-MODLULUK-VE-SES.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Yalnız işaret edilen bölümleri oku.

1. Bu doküman
2. `grep -n "K-059\|K-221\|K-467\|K-589\|K-663\|K-874\|K-878" docs/KARARLAR.md` — emsal **K-878** (canlı ses egress + BYOK); **K-467**, **K-874** (her yol kapıdan geçer); **K-589** (keyed kayıt); **K-663** (kayıt sırası sonucu değiştirmez); **K-221**/**K-059** (yalnız anahtar adı)
3. `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md` · `grep -n "F-336" docs/arsiv/ERTELENEN-ADAYLAR.md`
4. [`hafiza/ses-ve-konusma.md`](../../hafiza/ses-ve-konusma.md) (F-336, canlı ses) · [`hafiza/genisleme-noktalari-ve-denetim.md`](../../hafiza/genisleme-noktalari-ve-denetim.md) (kayıt sırası)

---

## Amaç

Tracon süreçte tek konuşma sağlayıcısı tanır: adsız kayıt, süreç geneli anahtar. Kiracı egress politikası ve kiracı anahtarı model çağrısına (K-467) ve canlı sese (K-878) uygulanır, TTS/STT'ye uygulanmaz. Bu faz **adlı** kayıt, her konuşma çağrısından önce **kiracı kapısı** (önce egress, sonra kiracı anahtarı) ve sağlayıcıyı adla veren bir **çözümleyici** ekler. `UseVoice()` değişmez (K1).

Tüketici bağlamı: çok kiracılı bir host, podcast ve video hatlarında TTS motorunu kiracı başına seçer (ElevenLabs, Gemini, GPU'da VoxCPM2/Trendyol-TTS). TTS fonksiyon düğümünde koşar; anahtarlar host'undur.

- **F-298** — adlı konuşma sağlayıcısı, kiracı egress denetimi ve kiracı anahtarı sözleşmesi.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Kiracı varsayılan motor satırı | Açık Soru 1 |
| Agent yollarında adla seçim | Agent tool ayarı taşımaz (`AgentDefinition.cs:50`); Açık Soru 5 |
| Yerleşik istemcide kiracı anahtarı | Açık Soru 4 |
| İkinci ElevenLabs hesabı | Ölçülmüş ihtiyaç yok |
| Kiracı başına eşzamanlılık, hesap kotası | Ölçülmedi; F-322 ile. Ses dakikası kota birimi değil (K-227) |
| Düğüm maliyeti (Faz 199), F-296, F-297 | Sözleşme yer bırakır (§198.5, §198.6) |
| Adlı sağlık ucu | `/api/voice/health` platform düzeyidir |
| Görsel yolunun egress'i | Aynı sınıf (`GenerateImageTool.cs:86-97`); ayrı kusur kaydı (F-384) |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `VoiceBuilderExtensions.cs:87-94` · `VoiceOptions.cs:49` | Tek ElevenLabs istemcisi iki sözleşmeye adsız bağlı; `ApiKey` süreç geneli |
| `SpeechContracts.cs:20-25` | Sözleşme "TENANT-INDEPENDENT" der |
| `SpeakTool.cs:71`, `:93-94` · `TranscribeTool.cs:64`, `:92` · `ListVoicesTool.cs:51` | Tool'lar adsız sözleşmeyi çözer, varsayılan modeli her isteğe koyar |
| `VoiceEndpoints.cs:93`, `:199`, `:256` | `/speak` ve `/voices` adsız sağlayıcıyı kullanır; kiracı denetimi yok |
| `VoiceConversationBuilderExtensions.cs:123-124` · `VoiceConversationDriver.cs:622`, `:725` | Sürücü sözleşmeleri kurulumda alır; STT/TTS çağrısında politika yok |
| `VoicePricing.cs:66-69` | Fiyat `ProviderName` ile; model süreç geneli seçenekten ve tip denetiminden gelir |
| `VoiceProviderBinding.cs:57-63` | Keyed kayıt F-336 bağlama kararını etkilemez |
| `TenantProviderAccess.cs:33-48`, `:82-107` | Tek kural: önce egress, sonra anahtar. Ret metni (`:63-65`) "model provider" der; `SecondaryModelEgressTests` ve `EgressPolicyCacheTests` onu sabitler |
| `LiveVoiceSessionLauncher.cs:242-283` · `LiveVoiceContracts.cs:95` · `OpenAILiveProvider.cs:84` | K-878 sırası: egress → anahtar → `SupportsTenantCredentials` → uç nokta override reddi; varsayılan `false`, yerleşik opt-in |
| `ElevenLabsSpeechClient.cs:440-444` | Anahtar her isteğe eklenir; istek başına kimlik mümkün |
| `TenantProviderEndpoints.cs:201-206` | Bağlama ucu adı doğrulamaz; ses satıcısı bağlanabilir |
| `TenantEgressPolicy.cs:3-12` · `TenantProviderBinding.cs:19` | XML yalnız model sağlayıcısını anar |
| 🚨 `tenant-provider-panel.tsx:88-89`, `:297-298` | Konsolun bağlama ve egress seçenekleri yalnız `GET /api/models`'tan gelir; ses satıcısı konsoldan izinliye eklenemez |
| `ImageGeneratorResolver.cs:28-38` | Emsal: ada göre keyed + adsız fallback; kiracı denetimi yok |
| `TraconWorkflowContextExtensions.cs:52` · `WorkflowRunner.cs:583` | Fonksiyon düğümünde `TraconRunContext.Current` kiracıyı taşır |
| `samples/Tracon.Api/Program.cs:433-438` | Örnek ses yalnız ElevenLabs anahtarıyla açılır |

> Kanıtlar 2026-10-12'de `99fe0b13` üzerinde doğrulandı.

---

## 198.1 — Adlı kayıt

- `AddSpeechSynthesizer(name, factory)` ve `AddSpeechTranscriber(name, factory)` `Tracon.Core`'dadır (`AddModelProvider` deseni): keyed singleton + iç ad kaydı.
- Ad küçük harfe katlanır; boş, 64 karakterden uzun ya da `[a-z0-9._-]` dışı ad `ArgumentException`. Sentezleyici ve tanıyıcı ayrı ad alanıdır.
- 🚨 Çift ad kayıtta `InvalidOperationException` atar; keyed çözümde "son kazanır" sessiz bir sıra kusurudur (K-663).
- Ham `AddKeyedSingleton` görülmez; bilinmeyen ad hatası kayıtlı adları listeler.
- `UseVoice()` adsız varsayılanı `TryAdd` ile kaydeder (K1, K4); adlı kayıt önce de sonra da gelebilir.
- Eşzamanlılık sınırı örnektedir (`VoiceBuilderExtensions.cs:83-86`); bir hesabı paylaşan iki ad aynı singleton'ı döner.

## 198.2 — Çözümleyici ve kiracı kapısı

`ISpeechProviderResolver` `Abstractions`'ta (K-215), uygulaması `Core`'da. `AddTracon()` onu `TryAdd` ile kaydeder; konuşma katmanı `Tracon.Voice` olmadan da kapıdan geçer. Sıra K-878'inkidir:

1. Ad → örnek; `null` = adsız varsayılan, bilinmeyen ad → `TraconException`.
2. Kiracı: `TraconRunContext.Current?.TenantId ?? ITenantContext.TenantId`. 🚨 Kiracı parametresi yoktur; olsaydı çağıran başka kiracının anahtarını seçerdi.
3. Egress, örneğin `ProviderName`'iyle. 🚨 Kayıt adıyla değil; yoksa takma ad politikayı atlar.
4. Kiracı anahtarı: bağlama var ama `SupportsTenantCredentials` yok, uç nokta override'ı var ya da anahtar değersiz → ret; global anahtara düşmez.
5. Her isteğe kiracı anahtarını koyan sarmalayıcı döner.

Politika her çözümlemede okunur, önbellek yok (F-304); depo atarsa sağlayıcı çağrılmaz. `DescribeDenial` tür alır; model metni aynı kalır. "TENANT-INDEPENDENT" XML'i yeniden yazılır.

## 198.3 — Kiracı anahtarı sözleşmesi (BYOK ikincil)

`ISpeechSynthesizer` ve `ISpeechTranscriber` `SupportsTenantCredentials => false` alır (varsayılan arayüz üyesi, K-878). `SpeechRequest` ve `SpeechTranscriptionOptions` `Credential` (`ModelProviderCredential?`) taşır. `ListVoicesAsync(credential, ct)` kimlik yoksa eski aşırı yüklemeyi çağırır, varsa `NotSupportedException` atar. Bağlama yalnız anahtar adını taşır (K-059, K-221).

## 198.4 — Yollar

| Yol | Ad kaynağı | Kapı | Ret |
|---|---|---|---|
| `speak`, `transcribe`, `list_voices` | `null` (Açık Soru 5) | çağrı | tool hatası |
| `POST /api/voice/speak` | gövde `provider` | istek | `403`; bilinmeyen ad `400`; sağlayıcı yok `501` |
| `GET /api/voice/voices` | `?provider=` | istek | `403` / `400` |
| Konuşma soketi | `null` | oturum kapısı (`VoiceConversationEndpoint.cs:149`) ile slot (`:159`) arası | `403` |
| Fonksiyon düğümü | düğüm ayarı ya da run parametresi | çağrı | `TraconException`, geçici değil |

Tool çözümleyiciyi kurulumda alır, sağlayıcıyı çağrıda çözer (K-218). 🚨 Adlı isteğe varsayılan `SynthesisModelId`/`OutputFormat` konmaz (`SpeakTool.cs:93-94` koyar); başka motorun modeli yanlış motora gider.

## 198.5 — Fiyat kaynağı (Faz 199 kullanır)

`SpeechAudio.ModelId` ve `SpeechTranscript.ModelId` gerçekte kullanılan modeli taşır; yerleşik istemci doldurur. Model sırası: bildirilen → istekteki → (yalnız yerleşik) varsayılan. 🚨 `VoicePricing` bugün varsayılan örneğin modelini (`IOptions<VoiceOptions>.Value`) ve `is ElevenLabsSpeechClient` denetimini (`:67`) kullanır; sarmalayıcıyla fiyat sessizce `null` olur. `IVoicePricingReader` `ForSynthesis(providerName, modelId, characters)` ve `ForTranscription(…)` alır (varsayılan üye, `null`); `/speak` bunu kullanır.

## 198.6 — Listeleme, konsol, sonraki fazlar

- `GET /api/voice/providers` (Reader): ad (`null` = varsayılan), tür, `providerName`, `supportsTenantCredentials`.
- Konsol kiracı paneli seçenekleri `/api/models` ∪ `/api/voice/providers` sağlayıcı adlarıdır; liste dışı kayıtlı ad korunur. Ad alanı tektir (K-878).
- F-296'nın yetenek arayüzünü sarmalayıcı da uygular; çağıran `is` yerine `ProviderName` kullanır. F-297 `AddSpeechSynthesizer` ile kaydeder. 1-B, `name == null`'ı imza değişmeden genişletir.

---

## Planlanan Public API

> Taslak. Alan ve arayüz üyesi eklemek **bugün bedava / `1.0.0` GA sonrası kırıcı**; varsayılan arayüz üyesi uygulayıcıyı kırmaz.

```csharp
public interface ISpeechProviderResolver   // Tracon.Abstractions
{
    IReadOnlyList<SpeechProviderDescriptor> Providers { get; }
    ValueTask<ISpeechSynthesizer> GetSynthesizerAsync(string? name = null, CancellationToken cancellationToken = default);
    ValueTask<ISpeechTranscriber> GetTranscriberAsync(string? name = null, CancellationToken cancellationToken = default);
}
// SpeechProviderDescriptor (sealed record): Name?, Kind, ProviderName, SupportsTenantCredentials
public enum SpeechProviderKind { Synthesizer, Transcriber }

// Tracon.Core — AddSpeechTranscriber aynı biçimde
public static ITraconBuilder AddSpeechSynthesizer(this ITraconBuilder builder, string name, Func<IServiceProvider, ISpeechSynthesizer> factory);
```

Ayrıca §198.3 ve §198.5 üyeleri, `SpeakRequest.Provider`. **Ölçüldü (2026-10-12, `maf-api-kesfi`, DI Abstractions 10.0.11):** `GetKeyedService<T>` ve `IServiceProviderIsKeyedService.IsKeyedService` var; keyed anahtarları listeleyen API yok — iç ad kaydı bu yüzden gerekir. MAF tipi ve yansıma yok.

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/voice/providers` | Reader | Kayıtlı adlar |
| `POST` | `/api/voice/speak` | Operator | `provider` alanı; kiracı kapısı |
| `GET` | `/api/voice/voices` | Reader | `?provider=`; kiracı kapısı |

Roller değişmez (`VoiceEndpoints.cs:48`, `:56`, `:79`).

### Arayüz payı

Yalnız `tenant-provider-panel.tsx`; taban uygulama başında ölçülür (Faz 194 kapanışı 197,7 KB gzip, bütçe 250 KB).

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/  Voice/SpeechProviderContracts.cs (yeni), SpeechContracts.cs, SpeechModels.cs; Tenancy/*
src/Tracon.Core/          TraconBuilderExtensions.Speech.cs, Voice/SpeechProviderResolver.cs (yeni), VoiceConversationDriver.cs, TenantProviderAccess.cs
src/Tracon.Voice/         Tools/*Tool.cs, Internal/VoicePricing.cs, ElevenLabsSpeechClient.cs
src/Tracon.AspNetCore/    VoiceEndpoints.cs, VoiceConversationEndpoint.cs
src/Tracon.UI/            tenant-provider-panel.tsx (+ test)
samples/Tracon.Api/       Program.cs (anahtarsız yerel sentezleyici `tone`)
```

---

## Hata Modları ve Testler

> Seviyeyi plan seçer (`.agents/ortak/test-seviyeleri.md`). Sahte sağlayıcı **çağrı sayacı** ve aldığı anahtarı tutar; ret testi sayacın 0 kaldığını ölçer.

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Ad yanlış örneğe gider (iki adlı + adsız) | Fonksiyonel | `NamedSpeechProviderTests` |
| 2 | Çift ad sessizce son kayda bağlanır | Birim | `SpeechProviderRegistrationTests` |
| 3 | Adlı kayıt F-336 bağlamasını ya da `ValidateOnStart`'ı bozar | Fonksiyonel (iki kayıt sırası) | `CustomVoiceProviderTests` |
| 4 | Takma ad egress'i atlar | Fonksiyonel | `SpeechTenantPolicyTests` |
| 5 | Ret varken sağlayıcı çağrılır — yedi yol (üç tool, `/speak`, `/voices`, konuşma, düğüm) | Fonksiyonel, yol başına | `SpeechTenantPolicyTests` |
| 6 | Bağlamalı kiracı host anahtarıyla faturalanır | Fonksiyonel | `SpeechTenantCredentialTests` |
| 7 | **Başka kiracı / eşzamanlılık:** paralel iki kiracıda anahtar karışır | Fonksiyonel | `SpeechTenantCredentialTests` |
| 8 | Uç nokta override'ı kullanılır; değersiz anahtar global anahtara düşer | Fonksiyonel | `SpeechTenantCredentialTests` |
| 9 | Sonradan kaydedilen politika sonraki çağrıyı durdurmaz (F-304) | Fonksiyonel | `SpeechTenantPolicyTests` |
| 10 | Konuşmada ret el sıkışmadan sonra gelir ya da slot tüketir | Fonksiyonel | `VoiceConversationTenantPolicyTests` |
| 11 | Kuyruklu workflow'da kiracı yanlış okunur | Fonksiyonel (iki kiracı) | `SpeechResolverTenantTests` |
| 12 | Adlı örnek yanlış modelle ya da `null` fiyatlanır | Birim + Fonksiyonel | `VoicePricingTests` |
| 13 | **Alt sistem hatası:** depo atar, kapı fail-open olur | Fonksiyonel | `SpeechTenantPolicyTests` |
| 14 | **Boş/aşırı girdi:** boş, 64+ karakter, bilinmeyen ad; varsayılan yokken adsız istek | Birim + Fonksiyonel | `VoiceEndpointTests` |
| 15 | **İptal:** çözümlemede iptal sağlayıcıyı yine çağırır | Fonksiyonel | `NamedSpeechProviderTests` |
| 16 | Ret düğümde geçici sayılır, tekrar denenir | Fonksiyonel | `NamedSpeechProviderTests` |
| 17 | Konsol listesi ses satıcısını göstermez ya da kaydederken düşürür | Vitest + E2E | `tenant-provider-panel.test.tsx` |
| 18 | Kimlik log'a ya da hata metnine düz yazılır | Birim + Fonksiyonel | `SecretLeakTests` |
| 19 | Üretilen istemciler yeni alanları taşımaz | Paket | NSwag/TS kapıları |

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklenir; ID'ler o anda alınır.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama, yerel sentezleyici `tone` | `GET /tracon/api/voice/providers` | `tone` `providerName` ile listelenir |
| 2 | Aynı | `POST /tracon/api/voice/speak` `{"text":"Merhaba","provider":"tone"}` | `200`; ek oluşur |
| 3 | Kiracı politikası `tone`'u içermez | Case 2 | `403`; sağlayıcı çağrılmadı |
| 4 | Konsol → Ayarlar → kiracı egress | Satıcıyı seç, kaydet, case 2 | `200` — 👤 görsel |
| 5 | Fonksiyon düğümü, ayar `{"provider":"tone"}` | Run | `Completed`; ek oluşur |
| 6 | Kiracının yerleşik sağlayıcı bağlaması var | Agent `speak` çağırır | Tool hatası; host anahtarıyla çağrı yok |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kiracının motor seçimi Tracon'da nerede durur? **Engelleyici** (migration) | A: çağıran adı verir (düğüm ayarı, run parametresi, HTTP) · B: kiracı varsayılan satırı (tablo ×3, uç, denetim izi, konsol) · C: ikisi | **A** — veri host'undur, ikinci kopya kayar; sınır seçim değil egress'tir; `name == null` B'yi sonra kırmadan ekler |
| 2 | Politikalı kiracı listede olmayan ses satıcısına artık çıkamaz. **Engelleyici** | A: hemen, `CHANGELOG` anar · B: opt-in bayrak (K1) | **A** — K-878 canlı seste aynısını seçti; kapalı kümenin dışına ses çıkması sızıntıdır |
| 3 | Yerel GPU motoru egress'ten muaf mı? | A: hayır · B: kodda bayrak | **A** — model yolunda muafiyet yok (`ModelProviderRegistry.cs:330-334`); "yerel" servis üçüncü tarafa iletebilir |
| 4 | Yerleşik ElevenLabs istemcisi kiracı anahtarını bu fazda taşısın mı? | A: hayır · B: evet | **A** — anahtar host'undur (kullanıcı kararı); B'de yabancı hesap host'un eşzamanlılık sayacını paylaşır |
| 5 | Agent yolları adla seçsin mi? | A: hayır · B: tool argümanı · C: agent ses bağlaması | **A** şimdi, **C** yeni aday (F-406); B seçimi modele bırakır |

---

## Bitiş Ölçütleri (DoD)

- [x] İki adlı + adsız sentezleyici aynı süreçte; `/speak` `provider` ile doğru örneği çağırır (test 1) — `NamedSpeechProviderTests` (her örneğin çağrı sayacı); örnek uygulama `tone`
- [x] Yedi yolda ret sağlayıcıyı çağırmaz; konuşma el sıkışmadan önce `403` döner (test 5, 10) — üç tool `Tracon.Voice.UnitTests.SpeechTenantPolicyTests` (×3 teori), `/speak`, `/voices`, düğüm `SpeechTenantPolicyTests`, soket `VoiceConversationTenantPolicyTests`; kapı kapatılınca 20 fonksiyonel + 6 tool testi kırmızı (mutasyon)
- [x] Desteklemeyen sağlayıcıda bağlamalı kiracı reddedilir; destekleyende anahtar ulaşır, sızmaz (test 6-7) — `SpeechTenantCredentialTests` (6; 24 paralel istek), `SecretLeakTests` (iki proje)
- [x] Test 11, 12, 17 yeşil (ambient kiracı, model fiyatı, konsol listesi) — `SpeechResolverTenantTests` (sınırı: Plandan Sapmalar #16), `VoicePricingTests` (iki proje), Vitest `tenant-provider-panel.test.tsx` + E2E `A_speech_provider_allowed_in_the_console_unblocks_speech_for_the_tenant`
- [x] Dört doğrulama kapısı sıfır uyarı verir — uygulama dalında `python3 scripts/kapi.py kapanis --taban 65d72b98` çıkış 0 (`db9fc9fe`): tarama 5,9 sn · `--denetle` 1,8 sn · betik testleri 7,6 sn · ajan haritası 0,1 sn · denetim paketi 0,3 sn · build 101,4 sn · test 1053,4 sn (38 koşum, 20 386 test, 20 367 geçti, 19 atlandı) · pack 47,7 sn · format 334,2 sn · site 87,2 sn. İlk koşum test adımında kırmızıydı (etiketsiz `Task.Delay`, § Süreç Ölçümü). Denetim düzeltmeleri hedefli testlerle doğrulandı. Birleştirmeden sonra tam kapı Faz 197'nin birleştirilmiş dalında (`3b93b79d`, Faz 198'in tamamını taşır) baştan sona tek koşumda çıkış 0 verdi (1219 sn; ayrıntı Faz 197 § *Kapanış kapısı*). `main`'de `yayin --kuru --surum 1.0.0-preview.4` ilk koşumda `Tracon.Client: TraconApiClient`'ı kırıcı buldu: `TraconVoiceListAsync` `provider`'ı iptal token'ından önce aldı, konumsal çağıran derlenmez; `CHANGELOG` `[Unreleased] → Changed` imzayı adıyla anar (`61283c9a`). Üçüncü koşum çıkış 0 (`dcc1a88f`): kırıcı liste 3 tip, 20 paket, altı paketlenmiş örnek, AOT ve net8.0 smoke
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (adlı sentezleyicili fonksiyon düğümü), çıktı belgeye yazıldı — aşağıdaki tablo
- [x] `secret` taraması boş döndü — `kapi.py tarama`: temiz (17 işaretli sentetik değer atlandı)
- [x] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-MM-126`…`131`; `126`, `127`, `129` ve `131` adım 1 örnek uygulamada; `128` 👤 görsel, `130` ➜ CI
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok; 🟡 #1 düzeltildi, 🟢 #1 F-445 (§ Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` temiz (build + içerik + bağlantı + ağırlık)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — **+350 B** brotli (aşağıda)
- [x] `CHANGELOG.md` `[Unreleased]` davranış değişikliğini ve yeni API'yi anar — Security, Added, Changed

### Örnek uygulama koşumu — gerçek çıktı (2026-10-13, port 5198)

`ASPNETCORE_ENVIRONMENT=Production`, üç bağlantı dizesi boş (bellek içi depo; paralel
oturumla paylaşılan veritabanına yazılmadı), `Tracon__Ui__AuthToken=` boş. ElevenLabs
anahtarı yüklenmedi: `UseVoice()` kapalı, adsız varsayılan sentezleyici yok, yalnız
adlı `tone` kayıtlı. Agent'lar Echo kipinde.

| Kontrol | Gerçek çıktı |
|---|---|
| `GET /api/voice/providers` | `[{"name":"tone","kind":"Synthesizer","providerName":"tone","supportsTenantCredentials":false}]` |
| `POST /api/voice/speak` `{"text":"Merhaba","provider":"tone"}` | `200`; ek `speech-20261013-065408-5567710.wav`, `audio/wav`, `5804` B; `characters 7`, `isEstimated false`, `cost null` (fiyat tanımsız) |
| Aynısı `provider` olmadan | `501` "Voice provider not configured" (varsayılan yok) |
| Aynısı `"provider":"cloud"` | `400` "No speech synthesizer is registered under the name 'cloud'. Registered synthesizers: tone." |
| `GET /api/voice/voices?provider=tone` | `200`; `low` / `mid` / `high` |
| `PUT /api/tenants/default/egress` `["echo"]`, sonra `/speak` `tone` | `403` "Tenant 'default' is not allowed to call speech provider 'tone'. Allowed providers: echo."; ek sayısı `1` → `1` (sağlayıcı çağrılmadı); `/voices?provider=tone` `403` |
| `narrated-tone` workflow (`support` agent + `synthesize` düğümü `{"provider":"tone","voice":"high"}`), politika `["echo"]` | run `Failed`; hata `Tracon.TenantProviderRefusedException`, `class: Unknown` (geçici değil), mesaj yukarıdakiyle aynı; yeni ek yok |
| Politika `["echo","tone"]`, aynı run | `Completed`; `WorkflowOutput` "Echo: Kargom nerede? … [spoken by tone: attachment 01a1340a-98c4-…, 17324 bytes]"; ek `speech.wav` `audio/wav` `17324` B run'a ve oturuma bağlı (3 kelime × 2880 örnek × 2 B + 44 B başlık) |
| `PUT /api/tenants/default/providers/tone` değersiz anahtar adıyla, `/speak` `tone` | `403` "… pointing at configuration key 'Tracon:ProviderKeys:Tone', but that key has no value … the call does NOT fall back to the global key." |
| Günlük | `fail: Tracon.WorkflowRunner … node synthesize failed. (ref: cb39036a)` — yalnız ret run'ı; başka hata yok |

Bundle payı (worktree tabanı `65d72b98`, `npx vite build && node scripts/postbuild.mjs`):
`index-DLWGvGp3.js.br` 170 990 B → `index-DfMBSjQB.js.br` 171 340 B (**+350 B** brotli);
JS gzip toplamı 203,7 KB → 204,1 KB (bütçe 250 KB).

### Doğrulama komutları

```bash
curl -s http://localhost:5081/tracon/api/voice/providers
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Politikalı kiracı sesi kaybeder | Açık Soru 2; `CHANGELOG`; konsol paneli |
| Adlı ve adsız kayıt `TryAdd` sırasıyla çakışır | Keyed kayıt; çift ad hatası; test 3 iki sırayı koşar |
| Bir kiracı paylaşılan hesabın eşzamanlılığını tüketir | Tek örnek tek sayaç; F-322 ile ölçülür |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.
> Uygulama oturumunda yazıldı (2026-10-13); yapısal iddialar `65d72b98` üzerinde ölçüldü.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | `ListVoicesAsync(credential, ct)` | `ListVoicesWithCredentialAsync(ModelProviderCredential?, CancellationToken = default)` | Var olan `ListVoicesAsync(ct = default)` opsiyonel parametre taşıyor: aynı adlı ikinci aşırı yükleme opsiyonelli `RS0026`, opsiyonelsiz `RS0027` verir. K-878 emsali (`AttachSessionAsync`) gibi ayrı ad |
| 2 | Kanıt tablosu satırları (`99fe0b13`) | Konum ve sıra iddialarının hepsi tuttu; satır kaymaları: `TraconWorkflowContextExtensions.cs:52` → `:53`; `WorkflowRunner` yolu `src/Tracon.Workflows/Internal/`, kapsam `:612`/`:630`; `ModelProviderRegistry.cs:330-334` → `:320-336` | Faz 201/204 arada geldi; ses dosyalarına dokunmadılar |
| 3 | Kapsam dışı: "Görsel yolunun egress'i (F-384)" | F-384 bu fazdan önce kapandı (K-912). Speech ret yolu onun `TenantProviderRefusedException`'ını (internal) yeniden kullanır; uçlar `403`'ü ondan çıkarır | Ret tipi uçta `403` ile sağlayıcı hatasının `502`'sini ayırır; ikinci bir ret tipi kopya olurdu. Görsel ret metni "model provider" der; değiştirilmedi |
| 4 | "`DescribeDenial` tür alır" | İç enum `TenantProviderCallKind { Model, Speech }` ve yeni aşırı yükleme; eski imza model metnini aynen üretir | `SecondaryModelEgressTests`, `EgressPolicyCacheTests` ve `ImageTenantPolicyTests` değişmeden yeşil |
| 5 | "Bilinmeyen ad → `TraconException`" | İç `SpeechProviderNotFoundException : TraconException`; uç adsız istekte `501`, adlıda `400` verir. Geçersiz biçimli ad (boş, 64+, karakter dışı) da `400`'dür ve mesaj girdiyi tekrar etmez | Public API büyümez; function node'da çağıran yine `TraconException` görür |
| 6 | "keyed singleton + iç ad kaydı" | Anahtar çıplak ad değil, iç `SpeechProviderRegistration` kaydıdır | Tüketicinin aynı dizeyle yaptığı keyed kayıt çakışmaz ve adlı örnek `GetKeyedService` ile kapı atlanarak çözülemez. `TraconBuilderRegistrationSnapshotTests.Describe` keyed tanımlayıcıyı okuyacak biçimde genişledi (keyed tanımlayıcıda `ImplementationInstance` atar) |
| 7 | "Her isteğe kiracı anahtarını koyan sarmalayıcı döner" | Sarmalayıcı yalnız kiracının anahtarı varsa döner; yoksa ham örnek döner. Çağıranın verdiği `Credential` sarmalayıcıda kiracınınkiyle değişir | Bağlamasız kiracıda davranış ve tip aynı kalır; çağıran başka kiracının anahtarını seçemez (`A_caller_supplied_credential_is_replaced_by_the_tenants`) |
| 8 | §198.5 fiyat sırası | `/speak` adsız istekte `ForSynthesis` `null` dönerse `ForCharacters`'a düşer; `VoicePricing` yapılandırılmış modele yalnız varsayılan sağlayıcının `ProviderName`'i için düşer. `VoicePricing` kurucusu `ISpeechTranscriber` alır (internal) | `ForSynthesis` varsayılan üyesi `null` döner: yalnız `ForCharacters` uygulayan tüketici okuyucusu varsayılan sentezleyicide fiyatını kaybetmesin. Adlı sentezleyici varsayılanın fiyatını hiç ödünç almaz (`VoicePricingTests`, iki proje) |
| 9 | Konuşma: "oturum kapısı ile slot arası" | Sürücü `ResolveProvidersAsync` (internal) ile **iki** sağlayıcıyı da (STT + TTS) el sıkışmadan önce çözer; `RunAsync` çözülmüş çifti alır | Ses tanıyıcıya, yanıt metni sentezleyiciye gider; ikisi ayrı sağlayıcı adı taşıyabilir |
| 10 | HTTP: `/voices` `403`/`400` | `/speak` ve `/voices` `.Produces`/`.ProducesProblem` üstverisini açıkça taşır (`400`/`403`/`501`/`502`); OpenAPI'de 132 yol / 170 işlem | Önceden iki uç belgede yalnız `200` taşıyordu; yeni yanıt kodları üretilen istemcilere ancak böyle girer |
| 11 | — | `provider: ""` `400`'dür, varsayılana düşmez | Boş ad belirsizdir; varsayılan `null`/alan yok ile seçilir |
| 12 | Site: `getting-started/security.md` | "The boundaries Tracon enforces" tablosuna "Tenant provider policy" satırı; `check-content.mjs` aynı satırı `reference/threat-model.md`'de de istedi ("fourteen" → "fifteen") | Kural model, konuşma, canlı ses ve görsel sağlayıcısının hepsini kapsıyor; önce hiçbir tabloda yoktu |
| 13 | Site sayfaları | İşlem sayısı sekiz yerde `169` → `170` (iki README, iki paket README'si, `http-api.md`, `capabilities.md`, `getting-started/index.md`, `reference/compatibility.md`, ana sayfa). `packages.md` değişmedi | `--site-denetle`'nin `paket-readme` kuralı `src/Tracon.AspNetCore/README.md` (sayı) ve `src/Tracon.Voice/README.md` değişimi için `packages.md`'yi ister; paket tanımı (`Tracon.Voice`: "speech synthesis, transcription, or live conversation") değişmedi. `--denetle` yeşil |
| 14 | Manuel case'ler: altı satır | `MT-MM-126`…`131`: plan case 1 ve 2 tek case'te (`126`); `131` eklendi (değersiz bağlama + anahtar almayan motor, hata modu 8) | `docs/manuel-test/*.md` ağaç bütçesi 2 556 000 B; ekleme sonrası 2 555 792 B (208 B boş) |
| 15 | `ui.md` ekran görüntüsü | Ekran görüntüleri yeniden üretilmedi; yalnız `.ui-source.sha256` damgası tazelendi | Kiracı paneli `settings.png`'nin görünür alanının altında (üretilen görüntü okundu: yalnız sürüm metni değişiyordu) |
| 16 | Hata modu 11 (`SpeechResolverTenantTests`) | Test kuyruklu iki kiracıyı doğru ayırır, ama "run kapsamı önce" sırasını AYIRT ETMEZ: kiracı yalnız `ITenantContext`'ten okununca da yeşil kaldı (mutasyon) | İş işçisi `AmbientTenantScope` açar; kuyruk yolunda iki kaynak aynı kiracıyı verir. Sıra `Tracon.Voice.UnitTests.SpeechTenantPolicyTests` ile kilitli: run kiracısı `acme`, `ITenantContext` `default`; aynı mutasyonda altı test kırmızı |
| 17 | Test sınıfı adları | Hata modu 14'ün birim kısmı `Core.UnitTests/Voice/SpeechProviderRegistrationTests`; 12 birim `Tracon.Voice.UnitTests/VoicePricingTests`, fonksiyonel `AspNetCore.FunctionalTests/VoicePricingTests`; üç tool'un ret testleri `Tracon.Voice.UnitTests/SpeechTenantPolicyTests` | Fonksiyonel test projesi `Tracon.Voice`'a bilerek referans vermez (soyutlamanın doğru pakette olduğunun kanıtı); tool'lar orada DI bileşimi (`AddTracon().UseVoice()`), kurulum anı sağlayıcısı ve `Services`'siz argümanla koşar |
| 18 | `<example>` | `ExampleCompilationTests` örnekteki tüketici tiplerini ister; `ExamplePrelude`'a `StudioSpeechSynthesizer`, `OnPremiseTranscriber`, `NarrationSettings` taslakları eklendi | Repo deseni (`OnPremiseModelProvider`, `TtsSettings`) |
| 19 | Örnek uygulama | Var olan `speak` düğümü (sahte) değişmedi; yeni `synthesize` düğümü (`SynthesizeSettings { Provider, Voice }`) sesi gerçekten üretir ve ek yazar | Eski düğüm `MT-WF-*` case'lerinin dayandığı çıktıyı üretir |
| 20 | Hata modu 15: "çözümlemede iptal sağlayıcıyı çağırmaz" | Çözümleyici iptali kontrollerden **sonra** da denetler (`35721434`) | Denetimin ikincil notu testin sağlayıcı-çağrı iddiasını boş buldu; anlamlı hâliyle (belirteci yok sayan depolar) test kırmızı oldu: depo iptalden sonra normal dönünce çözümleyici sağlayıcıyı iptal edilmiş çağırana veriyordu |
| 21 | Sevk edilen metin: "politika her çağrıda okunur", "Tracon örneği doğrudan çağırmaz" | Soket bağlantı başına bir kez denetler; sağlık kontrolü varsayılan örneği doğrudan çağırır — XML, kılavuz, `Tracon.Voice` README ve `CHANGELOG` bunu yazar (`0ebc2a73`) | Denetim 🟡 #1; kod plana (§198.4 el sıkışma kapısı) uyuyordu, metin fazlasını vaat ediyordu |
| 22 | `MIMARI-GUVENLIK.md` bölümü | BYOK/egress paragrafı "her sağlayıcı çağrısı" olarak düzeltildi; canlı ses, görsel ve konuşma tek cümlede | Dosya bütçesi 23 200 B, boşluk 12 B idi; yeni metin 23 199 B'ye sıkıştırıldı (taşınacak birikimli kısım yoktu) |

## Bu Fazda Verilen Kararlar

Karar defterine giren üç karar (`docs/KARARLAR.md`):

- **K-930** *(güvenlik, public-api; kullanıcı kararı: 2-A, 3-A, 4-A)* — her konuşma çağrısının kiracı kapısı; egress `ProviderName`'e; anahtar yalnız `SupportsTenantCredentials`'a; soket bağlantı başına bir kez.
- **K-931** *(public-api; kullanıcı kararı: 1-A, 5-A)* — adlı kayıt (`AddSpeechSynthesizer`/`AddSpeechTranscriber`), çözümleyici, ad kuralı, çift ad hatası, `GET /api/voice/providers`; motoru çağıran seçer.
- **K-931**'in parçası: `ListVoicesWithCredentialAsync` adı (RS0026/RS0027).
- **K-932** *(public-api)* — fiyat kaynağı: `ModelId` ve `IVoicePricingReader` varsayılan üyeleri.

Kategori dışı yerel tercihler (K-* açılmadı):

- `provider: ""` `400`'dür; varsayılan yalnız alan yokken ya da `null` iken seçilir.
- Sarmalayıcı yalnız kiracının anahtarı varsa döner; çağıranın verdiği `Credential` kiracınınkiyle değişir.
- Ret ve "bulunamadı" iç istisnalardır (`TenantProviderRefusedException`, `SpeechProviderNotFoundException`); uç `403`/`400`/`501`'i onlardan çıkarır.
- Örnek uygulamanın `tone` sentezleyicisi `ProviderName` `"tone"` taşır ve egress'ten muaf değildir (3-A'nın örneği).
- Ekran görüntüleri yeniden üretilmedi, yalnız damga (Plandan Sapmalar #15).

## Gerçekleşen Public API

```csharp
// Tracon.Abstractions
public interface ISpeechProviderResolver
{
    IReadOnlyList<SpeechProviderDescriptor> Providers { get; }
    ValueTask<ISpeechSynthesizer> GetSynthesizerAsync(string? name = null, CancellationToken cancellationToken = default);
    ValueTask<ISpeechTranscriber> GetTranscriberAsync(string? name = null, CancellationToken cancellationToken = default);
}
public sealed record SpeechProviderDescriptor
{
    public string? Name { get; init; }
    public required SpeechProviderKind Kind { get; init; }
    public required string ProviderName { get; init; }
    public required bool SupportsTenantCredentials { get; init; }
}
[JsonConverter(typeof(JsonStringEnumConverter<SpeechProviderKind>))]
public enum SpeechProviderKind { Synthesizer = 0, Transcriber = 1 }

// ISpeechSynthesizer (varsayılan üyeler)
bool SupportsTenantCredentials => false;
ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesWithCredentialAsync(
    ModelProviderCredential? credential, CancellationToken cancellationToken = default);   // kimlik verilirse NotSupportedException
// ISpeechTranscriber
bool SupportsTenantCredentials => false;
// IVoicePricingReader (varsayılan üyeler, null döner)
decimal? ForSynthesis(string providerName, string? modelId, decimal characters) => null;
decimal? ForTranscription(string providerName, string? modelId, TimeSpan duration) => null;

// Yeni init özellikleri
SpeechRequest.Credential                 // ModelProviderCredential?
SpeechTranscriptionOptions.Credential    // ModelProviderCredential?
SpeechAudio.ModelId                      // string?
SpeechTranscript.ModelId                 // string?
SpeakRequest.Provider                    // string?

// Tracon.Core
public static ITraconBuilder AddSpeechSynthesizer(this ITraconBuilder builder, string name, Func<IServiceProvider, ISpeechSynthesizer> factory);
public static ITraconBuilder AddSpeechTranscriber(this ITraconBuilder builder, string name, Func<IServiceProvider, ISpeechTranscriber> factory);
```

HTTP: `GET /api/voice/providers` (Reader, `AgentsRead`; `TraconVoiceProviders`) ·
`POST /api/voice/speak` `provider` + `400`/`403`/`501`/`502` · `GET /api/voice/voices`
`?provider=` + `400`/`403`/`501`. OpenAPI 132 yol / 170 işlem; iki istemci yeniden
üretildi (`TraconVoiceProvidersAsync`, `TraconVoiceListAsync(provider, ct)`).
`Tracon.Abstractions` public tip sayısı 411 → 414 (`public-surface-baseline.txt`).

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Abstractions/  Voice/SpeechProviderContracts.cs (yeni), Voice/SpeechContracts.cs, Voice/SpeechModels.cs,
                          Tenancy/{TenantEgressPolicy,TenantProviderBinding,ITenantEgressPolicyStore}.cs (XML),
                          Models/ModelProviderCredential.cs (XML), PublicAPI.Unshipped.txt
src/Tracon.Core/          TraconBuilderExtensions.Speech.cs (yeni), Voice/SpeechProviderRegistration.cs (yeni),
                          Voice/SpeechProviderResolver.cs (yeni: çözümleyici, iki sarmalayıcı, SpeechProviderNotFoundException),
                          Voice/VoiceConversationDriver.cs, Voice/VoiceConversationBuilderExtensions.cs,
                          Tenancy/TenantProviderAccess.cs (TenantProviderCallKind),
                          TraconServiceCollectionExtensions.Registration.Core.cs, PublicAPI.Unshipped.txt,
                          buildTransitive/Tracon.AgentMap.md (üretildi)
src/Tracon.Voice/         Tools/{Speak,Transcribe,ListVoices}Tool.cs, Internal/VoicePricing.cs,
                          Internal/ElevenLabsSpeechClient.cs, README.md
src/Tracon.AspNetCore/    Endpoints/VoiceEndpoints.cs, Voice/VoiceConversationEndpoint.cs, README.md
src/Tracon.UI/frontend/   components/tenant-provider-panel.tsx (+ .test.tsx yeni), locales/{en,tr}/operations.ts,
                          test/api-fixtures.ts
src/Tracon.Client/        Generated/TraconApiClient.g.cs, Generated/TraconClientJsonContext.g.cs (üretildi)
packages/tracon-client/   src/schema.ts (üretildi)
samples/Tracon.Api/       ToneSpeechSynthesizer.cs (yeni), Program.cs
docs/openapi/tracon.json
```

### Testler (gerçekleşen)

Yeni ya da genişleyen ~84 vaka:

- `Tracon.Core.UnitTests/Voice/SpeechProviderRegistrationTests` (18 vaka × 3 TFM) — ad kuralı, çift ad, ayrı ad alanları, bulunamayan/geçersiz ad mesajı, varsayılan üyeler.
- Kayıt anlık görüntüleri: `TraconBuilderRegistrationSnapshotTests` (+2 yöntem; keyed tanımlayıcı) · `ServiceRegistrationSnapshotTests` (+1 satır).
- `Tracon.AspNetCore.FunctionalTests` (37):
  - `NamedSpeechProviderTests` (7)
  - `SpeechTenantPolicyTests` (9)
  - `SpeechTenantCredentialTests` (6)
  - `VoiceConversationTenantPolicyTests` (3)
  - `SpeechResolverTenantTests` (1)
  - `VoicePricingTests` (3)
  - `VoiceEndpointTests` (+7)
  - `SecretLeakTests` (+1)
- `Tracon.Voice.UnitTests` (19):
  - `SpeechTenantPolicyTests` (9; üç tool × üç durum)
  - `VoicePricingTests` (5)
  - `CustomVoiceProviderTests` (+4; iki kayıt sırası)
  - `SecretLeakTests` (+1)
  - var olan tool testleri çözümleyiciyle kuruldu
- Vitest `tenant-provider-panel.test.tsx` (5).
- E2E `SettingsTests` (+1).
- `ExamplePrelude` üç taslak tip (örnek derleme kapısı).

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 (2026-10-13: Önkoşul satırı Faz 203 sırasıyla yeniden tartıldı); uygulama plan metnini değiştirmedi, sapmalar tabloda (22 satır) |
| Düzeltme turu sayısı | 3 — (1) ilk kapanış kapısı kırmızı: etiketsiz `Task.Delay` (`db9fc9fe`); (2) denetim: 🟡 #1 metin düzeltmesi + ikincil notun açığa çıkardığı iptal kusuru (`35721434`, `8585802c`, `0ebc2a73`); (3) `main`'de yayın provası: sürüm notu `TraconVoiceListAsync`'in kırıcı imzasını anmıyordu (`61283c9a`) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — `TestDelayClassificationTests` kapısını kıran etiketsiz `Task.Delay` (yeni test; kapanış kapısında yakalandı). Ayrıca yeni kodun kendi kusuru: iptalden sonra sağlayıcıyı vermek (birleştirmeden önce düzeltildi) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı; sonraki `kusur-giderme` koşumu doldurur) |

## Denetim Bulguları

**🔴 yok.**

| Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 🟡 #1 | Sevk edilen metin konuşma soketi için koddan fazlasını vaat ediyordu: "politika her çağrıda okunur, şimdi kaydedilen politika sonraki çağrıyı durdurur" (soket bağlantı başına bir kez okur, `VoiceConversationDriver.cs`) ve "Tracon örneği hiç doğrudan çağırmaz" (sağlık kontrolü doğrudan çağırır, `SynthesizerVoiceHealthCheck.cs:48`). Yerler: `SpeechProviderContracts.cs`, `SpeechContracts.cs`, `guides/voice.md` | Gerçek (metin; kod plana uyuyordu) | **Düzeltildi** — `0ebc2a73`: XML (çözümleyici, sözleşme, iç çözümleyici), `guides/voice.md`, `Tracon.Voice` README, `CHANGELOG` "bağlantı başına bir kez; açık bağlantı kapanana kadar sağlayıcısını tutar" ve sağlık kontrolünü platform istisnası olarak yazar; `llms*.txt` yeniden üretildi. Tarama: `grep -rn -i "stops the next\|never calls an instance\|every call: a policy"` başka sevk edilen yerde eşleşme bırakmadı (`api/` üretilir) |
| 🟢 #1 | Konuşma soketi ses sağlayıcısını tur başına yeniden çözmez (model tarafı her turda okur, F-310) | — | **Devredildi** — **F-445** (Faz planlama; tur ortası ret davranışı protokol kararı ister) |
| Not | İki boş iddia: iptal testindeki `Studio.Calls.ShouldBe(0)` (test sentezlemiyordu) ve geçersiz ad testindeki `ShouldNotContain("\"\"")` | — | **Düzeltildi** — iptal testi belirteci yok sayan depolarla yeniden yazıldı ve **kırmızı** oldu: çözümleyici iptalden sonra sağlayıcıyı veriyordu; çözümleyici artık kontrollerden sonra da iptali denetler (`35721434`). Geçersiz ad testi kural metnini ve girdinin yankılanmadığını ölçer (`8585802c`) |

## Sonraki Faza Devir Notu

**Sıradaki faz:** [Faz 203 — Çok Konuşmacılı Diyalog Sentezi](../../203-COK-KONUSMACILI-DIYALOG-SENTEZI.md) (F-296). Devir oraya da yazıldı.

**Devralınan sözleşmeler:**
- Motorlar `AddSpeechSynthesizer(name, factory)` / `AddSpeechTranscriber(name, factory)` ile kaydolur. Çağıran `ISpeechProviderResolver.GetSynthesizerAsync(name, ct)` ile alır; `null` adsız varsayılandır.
- Kiracı kapısı çözümleyicidedir. Kiracı `TraconRunContext.Current?.TenantId ?? ITenantContext.TenantId` sırasıyla okunur; egress `ProviderName`'e uygulanır.
- Kiracının anahtarı yalnız `SupportsTenantCredentials` olan motora `SpeechRequest.Credential` ile gider.
- Fiyat `IVoicePricingReader.ForSynthesis(providerName, audio.ModelId ?? request.ModelId, characters)` ile hesaplanır. Motor `SpeechAudio.ModelId`'yi doldurursa doğru fiyatlanır.

**Bilinen tuzaklar:**
- 🚨 Çözümleyici kiracının anahtarı **yoksa ham örneği**, **varsa** `TenantCredentialSpeechSynthesizer` sarmalayıcısını döner. Sarmalayıcı bir arayüzü koşullu uygulayamaz: `is ISpeechDialogueSynthesizer` denetimi bağlamalı kiracıda düşer. İki yol var:
  - yetenek başına bir sarmalayıcı sınıfı (diyalog arayüzünü de uygulayan, kimlik bilgisini ileten);
  - çözümleyicide ayrı bir `GetDialogueSynthesizerAsync`.

  Seçim public API kararıdır. Çağıran tip denetimi yerine `ProviderName` okur.
- 🚨 Adlı motora varsayılanın `SynthesisModelId`/`OutputFormat`'ı verilmez. Diyalog isteği de modelini kendisi taşır.
- 🚨 Diyalog isteğinin tipi de `Credential` taşımalıdır. Varsayılan üye, kimlik bilgisi gelince `NotSupportedException` atmalıdır (`ListVoicesWithCredentialAsync` deseni).
- 🚨 Konuşma soketi sağlayıcıyı bağlantı başına bir kez çözer (F-445). Diyalog soket yoluna girerse aynı sınır geçerlidir.
- Ret testleri için sayaçlı sahte: `tests/Tracon.AspNetCore.FunctionalTests/Infrastructure/CountingSpeechProvider.cs`. Kuyruklu test "run kapsamı önce" sırasını ayırt etmez (`docs/hafiza/ses-ve-konusma.md`).

**Yarım kalan işler:**
- F-441 (manuel test bütçesi, 208 B boş) bir sonraki fazdan önce yapılmalı.
- F-442…F-446 kanal tablolarında.
- `MT-MM-128` 👤 görsel; `MT-MM-130` canlı ElevenLabs anahtarı ister.
