# Faz 198 — Adlı Konuşma Sağlayıcısı ve Kiracı Bağlaması

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** onaylanmadı — plan 2026-10-12'de bakımcı talebiyle yazıldı; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-298**
> **Önkoşul:** Yok (F-336 kapandı). Örnek, Faz 195 düğüm ayarını; o kapanmazsa run parametresini (Faz 194) kullanır
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
4. [`hafiza/ses-ve-konusma.md`](hafiza/ses-ve-konusma.md) (F-336, canlı ses) · [`hafiza/genisleme-noktalari-ve-denetim.md`](hafiza/genisleme-noktalari-ve-denetim.md) (kayıt sırası)

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

- [ ] İki adlı + adsız sentezleyici aynı süreçte; `/speak` `provider` ile doğru örneği çağırır (test 1)
- [ ] Yedi yolda ret sağlayıcıyı çağırmaz; konuşma el sıkışmadan önce `403` döner (test 5, 10)
- [ ] Desteklemeyen sağlayıcıda bağlamalı kiracı reddedilir; destekleyende anahtar ulaşır, sızmaz (test 6-7)
- [ ] Test 11, 12, 17 yeşil (ambient kiracı, model fiyatı, konsol listesi)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (adlı sentezleyicili fonksiyon düğümü), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [ ] `CHANGELOG.md` `[Unreleased]` davranış değişikliğini ve yeni API'yi anar

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

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen K adayları: konuşma çağrısının kiracı
> kapısı (`güvenlik`), adlı kayıt ve çözümleyici (`public-api`).

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
