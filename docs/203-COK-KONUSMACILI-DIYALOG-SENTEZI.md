# Faz 203 — Çok Konuşmacılı Diyalog Sentezi ve Sağlayıcı İstek Ayarları

> **Durum:** 📋 Planlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-B, 5-B, 6-B; Açık Soru 7 → B). Faz 196 ile paralel geliştirilir
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-296**
> **Genellik:** Ses sözleşmesi Tracon'un kendi sözleşmesidir ve iki büyük sağlayıcının (ElevenLabs Text to Dialogue, Gemini çok konuşmacılı TTS) sunduğu yeteneği ifade edemez; MEAI 10.9.0 `ITextToSpeechClient` de tek ses taşır. İkinci kanıt bu ekosistem boşluğudur, tüketici değil. Bölme, birleştirme ve ses işleme tüketici kodunda kalır
> **Karşı görüş:** "Ses üretimi çekirdek iş değil; tüketici SDK'yı fonksiyon düğümünde çağırabilir; Tracon'un katkısı kiracı kapısı ve maliyet kaydıdır." Plan bunu kabul eder: faz yalnız sözleşmeyi, yerleşik uygulamayı ve istek ayarlarını taşır; medya yardımcıları çıkarıldı; faz Faz 198'in kiracı kapısından sonra gelir
> **Önkoşul:** Yok. **Sıra:** [Faz 198](arsiv/fazlar/198-ADLI-KONUSMA-SAGLAYICISI.md)'den sonra (2026-10-13, yeniden tartıldı): yetenek o fazın kiracı kapısının arkasına eklenir. F-297 (Gemini) bu sözleşmeyi uygular
> **Paketler:** `Tracon.Abstractions`, `Tracon.Voice`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-13): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `guides/voice.md`, `reference/configuration.md` (§ Voice), `capabilities.md` · sevk edilen: yeni tiplerin XML `<example>`'ı, `src/Tracon.Voice/README.md`, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/19-COK-MODLULUK-VE-SES.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula; yalnız işaret edilen bölümleri oku.

1. Bu doküman
2. Kararlar:
   ```bash
   grep -n "K-219\|K-878\|K-912" docs/KARARLAR.md
   grep -n "K-208" docs/arsiv/KARARLAR-INDEKS-ARSIV.md
   ```
   **K-208** (`ProviderSettings` sözleşmede; bilinmeyen anahtar hatadır) ·
   **K-219** (ses maliyeti token maliyetiyle toplanmaz) · **K-878**,
   **K-912** (kiracı kapısı emsalleri; bu faz kiracıdan habersiz kalır,
   kapı Faz 198'dedir)
3. F-340 kapanışı (sağlayıcı ayarı doğrulama deseni):
   `grep -n "F-340" docs/arsiv/ERTELENEN-ADAYLAR.md`
4. Hafıza: [`hafiza/ses-ve-konusma.md`](hafiza/ses-ve-konusma.md) ·
   [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (açık fabrika ve
   opsiyonel parametre tuzağı, F-388)
5. Tüketici kanıtı: [podcast raporu](arsiv/incelemeler/2026-10-11-podcast-tuketici-uyum-raporu.md)
   § 2.1–2.2 ve § 6.1 (ölçülen istek ve yanıt biçimi)
6. Faz 204 devri ([204](arsiv/fazlar/204-KESILEN-CIKTI-VE-ANTHROPIC-AYARLARI.md) § Sonraki Faza
   Devir Notu): `ModelProviderSettings.TypeMismatch` artık
   `ProviderSettingsValidationException` (registry maskesinden geçer). Kapalı
   değer kümesi deseni `AnthropicRequestSettings`'tedir; sözleşmede
   `InvalidSettingValue`. 🚨 Anthropic ve Google ret metni yanlış **değeri**
   yankılar; bu fazın "yankılamaz" kuralı ya üçünü birlikte bağlar ya da
   kapalı küme istisnası gerekçelenir.
7. **Faz 198 devri** ([198](arsiv/fazlar/198-ADLI-KONUSMA-SAGLAYICISI.md) § Sonraki Faza Devir
   Notu; `grep -n "K-930\|K-931\|K-932" docs/KARARLAR.md`). Kiracı kapısı ve adlı
   kayıt artık vardır; bu faz onun **arkasına** eklenir. Birebir imzalar:
   ```csharp
   // Tracon.Abstractions
   public interface ISpeechProviderResolver
   {
       IReadOnlyList<SpeechProviderDescriptor> Providers { get; }
       ValueTask<ISpeechSynthesizer> GetSynthesizerAsync(string? name = null, CancellationToken cancellationToken = default);
       ValueTask<ISpeechTranscriber> GetTranscriberAsync(string? name = null, CancellationToken cancellationToken = default);
   }
   bool ISpeechSynthesizer.SupportsTenantCredentials => false;              // varsayılan üye
   ModelProviderCredential? SpeechRequest.Credential { get; init; }        // kiracı anahtarı
   string? SpeechAudio.ModelId { get; init; }                               // fiyatın modeli
   decimal? IVoicePricingReader.ForSynthesis(string providerName, string? modelId, decimal characters) => null;
   // Tracon.Core
   public static ITraconBuilder AddSpeechSynthesizer(this ITraconBuilder builder, string name, Func<IServiceProvider, ISpeechSynthesizer> factory);
   ```
   - 🚨 **Sarmalayıcı ve `is` tuzağı:** `GetSynthesizerAsync` kiracının anahtarı
     **yoksa ham örneği**, **varsa** iç `TenantCredentialSpeechSynthesizer`'ı
     döner (`src/Tracon.Core/Voice/SpeechProviderResolver.cs`). Sarmalayıcı bugün
     yalnız `ISpeechSynthesizer` uygular; `is ISpeechDialogueSynthesizer`
     bağlamalı kiracıda **düşer**, bağlamasız kiracıda geçer — aynı motor kiracıya
     göre farklı yetenek bildirir. Açık Soru 7.
   - 🚨 Diyalog isteği de `Credential` taşımalıdır; yetenek varsayılan olarak
     kiracı anahtarı almaz (`SupportsTenantCredentials` deseni) ve kimlik
     gelince `NotSupportedException` atar (`ListVoicesWithCredentialAsync` emsali).
   - Fiyat: `ForSynthesis(audio ProviderName, audio.ModelId ?? request.ModelId, characters)`.
     Diyalog sonucunun `SpeechAudio.ModelId`'si doldurulmazsa adlı motor fiyatlanmaz.
   - Adlı motora varsayılanın `SynthesisModelId`/`OutputFormat`'ı verilmez; bu
     fazın § 203.3 kuralı ("`SynthesisModelId` diyaloğa taşınmaz") aynı çizgidedir.
   - Ret testleri için sayaçlı sahte: `tests/Tracon.AspNetCore.FunctionalTests/Infrastructure/CountingSpeechProvider.cs`.

---

## Amaç

Tracon'un ses sözleşmesi istek başına tek ses taşır. İki konuşmacılı bir
podcast bugün ya her repliği ayrı seslendirip birleştirir (dinleme testinde
"okuyor gibi" bulundu) ya da sağlayıcının diyalog API'sini kendi koduyla
çağırır. Bu faz diyalog sentezini bir **yetenek arayüzü** olarak ekler,
yerleşik ElevenLabs istemcisine uygular ve sağlayıcıya özgü istek
ayarlarını (ses ayarları, süreklilik) iki sözleşmeye de taşır.

- **F-296** — konuşmacı-tur listesi, yetenek bildirimi, segment zamanları ve
  sağlayıcı istek ayarları. Uzun diyaloğu bölmek ve sesi birleştirmek
  çağıranın işidir.

**Tüketici bağlamı (ölçüldü, 2026-10-13).** Podcast pilotu ElevenLabs
`POST /v1/text-to-dialogue/with-timestamps` çağırır: `inputs[] {text,
voice_id}`, `model_id: eleven_v4`, `language_code: tr`, `settings
{stability, similarity}`, `output_format=pcm_24000`. Episode başına 7–9
istek, istek başına ≤ 1.900 karakter; yanıtın `voice_segments`'i (giriş
başına başlangıç/bitiş saniyesi) konuşmacı başına ses seviyesi ölçümüne ve
bölüm işaretlerine girer. Plan geneldir; bu tüketici yalnız bir ölçüt
örneğidir ve sözleşmenin biçimini belirlemez.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Kiracı başına sağlayıcı seçimi ve kiracı anahtarı | Faz 198 (F-298, K-930/K-931) yaptı; bu faz yeteneği o kapının arkasına ekler, kapıyı değiştirmez (Açık Soru 7) |
| Gemini diyalog adaptörü | F-297; bu sözleşmeyi uygular |
| Fonksiyon düğümünün TTS maliyeti | Faz 199 (F-299); bu faz yalnız `CharactersBilled` döner |
| `speak` tool'unun diyalog sürümü | Tool tek replik içindir; diyalog workflow düğümünde çağrılır (Açık Soru 5) |
| MEAI `ITextToSpeechClient` uyarlaması | MEAI 10.9.0 sözleşmesi tek ses taşır (`TextToSpeechOptions.VoiceId`); diyalog karşılığı yok |
| Ses seviyesi, tempo, müzik | Tüketicinin medya işidir (ffmpeg) |
| Uzun diyaloğu bölme ve PCM birleştirme yardımcıları | Medya araç seti işidir, kontrol düzlemi değil; bölme kuralı (bölüm sınırı, cümle) domain'e aittir. 2026-10-13'te plandan çıkarıldı |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `src/Tracon.Abstractions/Voice/SpeechContracts.cs` · `ISpeechSynthesizer` | `ProviderName`, `MaxCharactersPerRequest`, `SynthesizeAsync`, `SynthesizeStreamingAsync`; konuşmacı listesi yok |
| `SpeechModels.cs:6-44` | `SpeechRequest`: `Text`, `VoiceId`, `ModelId`, `OutputFormat`, `LanguageCode`, `IncludeTimestamps`; ses ayarı ve sağlayıcı ayarı yok |
| `SpeechModels.cs:47-90` · `:92-105` | `SpeechAudio` (sınıf, `record` değil) karakter hizalaması taşır; giriş başına segment yok |
| `VoiceOptions.cs:21` · `:76` | Varsayılan çıktı `mp3_44100_128`; istek başına 5.000 karakter |
| `ElevenLabsSpeechClient.cs:36` | Varsayılan sentez modeli `eleven_multilingual_v2` |
| `ElevenLabsSpeechClient.cs:413-419` | Yalnız `v1/text-to-speech/{voiceId}` ailesi (`/stream`, `/with-timestamps`) |
| `grep -c voice_settings ElevenLabsSpeechClient.cs` → `0` | Ses ayarı hiç gönderilmez |
| `ElevenLabsSpeechClient.cs:506` · `:563` | `character-cost` başlığı ve hizalama ayrıştırması yeniden kullanılabilir |
| `VoiceBuilderExtensions.cs:87-106` | Tek istemci örneği üç arayüze kaydedilir; sağlık denetimi `as IVoiceHealthCheck` ile yetenek okur (emsal) |
| `ModelProviderSettings.cs:36` | Önekli ayar doğrulayıcısı `ModelBinding` alır; sözlük aşırı yüklemesi yok |
| [Faz 198](arsiv/fazlar/198-ADLI-KONUSMA-SAGLAYICISI.md) § 198.6 · `SpeechProviderResolver.cs` | Plan "F-296'nın yetenek arayüzünü sarmalayıcı da uygular" diyordu; gerçekleşen sarmalayıcı (`TenantCredentialSpeechSynthesizer`, internal) yalnız `ISpeechSynthesizer` uygular ve yalnız kiracının anahtarı varken döner (Açık Soru 7) |
| Tüketici `render_elevenlabs.py:155-173` | Diyalog isteğinin ve `voice_segments` eşlemesinin çalışan örneği (12–13 Ekim, 7 koşum, hepsi ilk denemede) |

> Kanıtlar 2026-10-13'te `55272673` üzerinde doğrulandı.

---

## 203.1 — Diyalog yetenek arayüzü

- `ISpeechDialogueSynthesizer` (`Tracon.Abstractions/Voice`), `ISpeechSynthesizer`'dan
  **ayrı** bir arayüzdür (Açık Soru 1). Üyeler: `ProviderName`,
  `DialogueCapabilities`, `SynthesizeDialogueAsync(SpeechDialogueRequest, ct)`.
- `SpeechDialogueRequest`: `Turns` (`SpeechDialogueTurn { Text, VoiceId }`,
  sıralı), `ModelId?`, `OutputFormat?`, `LanguageCode?`, `IncludeTimestamps`,
  `ProviderSettings` (§ 203.2).
- `SpeechDialogueCapabilities`: `MaxVoices`, `MaxCharactersPerRequest`,
  `SupportsSegmentTimestamps`. Çağıran bölmeyi bundan planlar.
- `SpeechDialogueAudio`: `Audio` (`SpeechAudio`; `CharactersBilled`,
  `UsageSource`, `ModelId` dahil) ve `Segments?`
  (`SpeechDialogueSegment { TurnIndex, VoiceId, Start, End }`).
  `IncludeTimestamps = false` iken `Segments` `null`'dır.
- **Doğrulama ağdan önce:** boş liste, boş metin, boş ses kimliği, farklı ses
  sayısı > `MaxVoices`, toplam karakter > `MaxCharactersPerRequest` →
  `ArgumentException`. Sessiz bölme yok (K1): bölme çağıranın işidir ve
  `DialogueCapabilities` ona sınırı söyler.
- **Kayıt:** `UseVoice()` yetenek arayüzünü bağlı sentezleyiciden türetir
  (`as ISpeechDialogueSynthesizer`, sağlık denetimi emsali). Bağlı
  sentezleyici yeteneği taşımıyorsa kayıt, çağrıda sağlayıcıyı adlandıran
  `TraconException` atan ve `MaxVoices = 0` bildiren bir yer tutucudur.
  Tüketicinin kendi kaydı kazanır (`TryAdd`, K4). 🚨 Fabrika açık olur ve
  opsiyonel parametre atlanmaz (F-388 dersi).

## 203.2 — Sağlayıcı istek ayarları

- `SpeechRequest` ve `SpeechDialogueRequest`
  `ProviderSettings` (`IReadOnlyDictionary<string, JsonElement>`, boş
  varsayılan) taşır. Anahtar sağlayıcı önekiyle başlar:
  `elevenlabs.stability`, `elevenlabs.similarityBoost`, `elevenlabs.style`,
  `elevenlabs.useSpeakerBoost`, `elevenlabs.speed`, `elevenlabs.seed`,
  `elevenlabs.previousText`, `elevenlabs.nextText`,
  `elevenlabs.previousRequestIds` (Açık Soru 2).
- Bilinmeyen anahtar ve yanlış tip `ArgumentException`'dır (K-208, F-340
  deseni); başka önekli anahtar yok sayılmaz, reddedilir. Ret metni değeri
  yankılamaz (F-291 dersi).
- `ModelProviderSettings` sözlük alan bir aşırı yükleme kazanır;
  `ModelBinding` sürümü onu çağırır (tek kural, K-483).
- Eşleme: tek ses `voice_settings {stability, similarity_boost, style,
  use_speaker_boost, speed}`, `seed`, `previous_text`, `next_text`,
  `previous_request_ids` (≤ 3); diyalog `settings` ve `seed`. 🚨
  **Ölçülmeli (Adım 0):** diyalog `settings` nesnesinin kabul ettiği alanlar.
  Tüketici `similarity` gönderiyor ve hata almıyor; alanın etkisi ölçülmedi.
  Etkisiz alan diyalogda reddedilir, yok sayılmaz.

## 203.3 — ElevenLabs uygulaması

- `ElevenLabsSpeechClient` `ISpeechDialogueSynthesizer`'ı uygular:
  `POST v1/text-to-dialogue` (ham ses) ya da
  `v1/text-to-dialogue/with-timestamps` (JSON: `audio_base64`, `alignment`,
  `voice_segments`), `?output_format=`.
- Gövde: `inputs[] {text, voice_id}`, `model_id`, `language_code`,
  `settings`, `seed`. Model: istek > `VoiceOptions.DialogueModelId` > API
  varsayılanı (Açık Soru 4). 🚨 `SynthesisModelId` diyaloğa taşınmaz:
  `eleven_multilingual_v2` diyalog modeli değildir.
- `voice_segments[] {dialogue_input_index, voice_id, start_time_seconds,
  end_time_seconds}` → `SpeechDialogueSegment`. Uzunluğu tutmayan ya da
  aralık dışı indeks taşıyan yanıt `Segments = null` olur, istisna değil
  (`ToAlignment` emsali, `:560`).
- Yetenek: `MaxVoices = 10`; `MaxCharactersPerRequest` =
  `VoiceOptions.MaxDialogueCharactersPerRequest` (varsayılan 2.000,
  ElevenLabs önerisi; tüketici 1.900 kullanıyor). Eşzamanlılık mevcut
  `MaxConcurrentRequests` semaforunu paylaşır.
- `CharactersBilled` `character-cost` başlığından; yoksa girişlerin karakter
  toplamı, `UsageSource.Estimated`. Fiyat `Tracon:Pricing:Voice` altında
  `ProviderName` ile okunur (F-336); maliyet kaydı Faz 199'dur.

---

## Planlanan Public API

> Taslak. Arayüz üyesi ve `sealed record` alanı eklemek **bugün bedava /
> `1.0.0` GA sonrası kırıcı**. Ayrı yetenek arayüzü mevcut uygulayıcıyı
> kırmaz.

```csharp
// Tracon.Abstractions (Voice)
public interface ISpeechDialogueSynthesizer
{
    string ProviderName { get; }
    SpeechDialogueCapabilities DialogueCapabilities { get; }
    ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(SpeechDialogueRequest request, CancellationToken cancellationToken = default);
}
public sealed record SpeechDialogueCapabilities { int MaxVoices; int MaxCharactersPerRequest; bool SupportsSegmentTimestamps; }
public sealed record SpeechDialogueTurn { required string Text; required string VoiceId; }
public sealed record SpeechDialogueRequest
{
    required IReadOnlyList<SpeechDialogueTurn> Turns; string? ModelId; string? OutputFormat; string? LanguageCode;
    bool IncludeTimestamps; IReadOnlyDictionary<string, JsonElement> ProviderSettings;
}
public sealed class SpeechDialogueAudio { required SpeechAudio Audio; IReadOnlyList<SpeechDialogueSegment>? Segments; }
public sealed record SpeechDialogueSegment { int TurnIndex; string VoiceId; TimeSpan Start; TimeSpan End; }
SpeechRequest { IReadOnlyDictionary<string, JsonElement> ProviderSettings { get; init; } }   // yeni alan
ModelProviderSettings.Validate(IReadOnlyDictionary<string, JsonElement>, string prefix, IReadOnlyCollection<string> supported) // aşırı yükleme, ad taslak

// Tracon.Voice
VoiceOptions { string? DialogueModelId; int MaxDialogueCharactersPerRequest = 2000; }
ElevenLabsProviderNames { const string SettingsPrefix = "elevenlabs"; SupportedSettings; }   // ad taslak
```

### HTTP `endpoint`'leri

Yok (Açık Soru 5). `POST /api/voice/speak` `providerSettings` alanını
kabul eder mi, Açık Soru 6.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Voice/
├── ISpeechDialogueSynthesizer.cs        (yeni)
├── SpeechDialogueModels.cs              (yeni)
└── SpeechModels.cs                      (SpeechRequest.ProviderSettings)
src/Tracon.Abstractions/Agents/ModelProviderSettings.cs   (sözlük aşırı yüklemesi)
src/Tracon.Voice/
├── Internal/ElevenLabsSpeechClient.cs   (diyalog, ayarlar)
├── Internal/ElevenLabsDialogueJson.cs   (yeni, kaynak üretecili JSON)
├── ElevenLabsProviderNames.cs           (yeni)
├── VoiceOptions.cs · VoiceOptionsValidator · VoiceBuilderExtensions.cs
└── README.md
samples/Tracon.Api/Program.cs            (iki replikli diyalog düğümü)
tests/Tracon.Voice.UnitTests/…           (sınıflar Hata Modları tablosunda)
```

---

## Hata Modları ve Testler

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Gövde yanlış: girdi sırası, ses kimliği, model, dil, çıktı biçimi, `settings` | Birim (sahte `HttpMessageHandler`) | `ElevenLabsDialogueRequestTests` |
| 2 | `voice_segments` yanlış tura eşlenir; bozuk yanıt istisna atar | Birim | `ElevenLabsDialogueResponseTests` |
| 3 | **Aşırı girdi:** boş liste, boş metin, 11 ses, sınırı aşan karakter ağa gider | Birim | `SpeechDialogueValidationTests` |
| 4 | Bilinmeyen, başka önekli ya da yanlış tipli ayar sessizce düşer; ret değeri yankılar | Birim + Sözleşme | `SpeechProviderSettingsTests`, `ModelProviderSettingsContract` |
| 5 | Tek ses yolunda `voice_settings` gitmez ya da mevcut istek değişir | Birim | `ElevenLabsSpeechRequestTests` |
| 6 | `character-cost` okunmaz; tahmin `Provider` diye işaretlenir | Birim | `ElevenLabsDialogueResponseTests` |
| 7 | **İptal:** iptal ağ çağrısına ulaşmaz; semafor sızar | Birim | `ElevenLabsDialogueConcurrencyTests` |
| 8 | **Eşzamanlılık:** diyalog ve tek ses semaforu iki kez sayar | Birim | `ElevenLabsDialogueConcurrencyTests` |
| 9 | **Alt sistem:** `4xx`/`5xx` anahtarı ya da adresi sızdırır | Birim | `ElevenLabsDialogueErrorTests` |
| 10 | DI: yetenek kaydı yok; tüketicinin kaydı ezilir; yetenek taşımayan sentezleyici sessiz kalır | Birim (DI, `ValidateOnStart`) | `VoiceDialogueRegistrationTests` |
| 13 | **Başka kiracı:** kapsam dışı — bu faz kiracıdan habersizdir; Faz 198 kapısı test eder | — | — |
| 14 | Sözleşme paket sınırında kırılır (Abstractions ↔ Voice yönü) | Mimari | `DependencyDirectionTests` |
| 15 | Gerçek sağlayıcıda Türkçe iki sesli istek | Manuel (anahtar gerekir) | case 1 |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama, ElevenLabs anahtarı, iki kütüphane sesi | Diyalog düğümünü iki Türkçe replikle `pcm_24000` ve zaman damgasıyla koş | Ses dosyası; iki segment, sırayla, boşluksuz; `characterCost` dolu — 👤 dinleme |
| 2 | Aynı | `elevenlabs.stability: 0.3` ile koş | İstek gövdesinde `settings.stability` (log) |
| 3 | Aynı | `elevenlabs.bogus` ile koş | Ağ çağrısı yok; hata anahtarı adlandırır, değeri yazmaz |
| 4 | Kendi `ISpeechSynthesizer`'ı olan host | Diyalog düğümünü koş | Hata sağlayıcıyı adlandırır; ElevenLabs'e çağrı yok |
| 5 | Sınırı aşan 12 replik | Tek istekte gönder | Ağ çağrısı yok; hata sınırı ve gerçek karakter sayısını adlandırır |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sözleşmenin yeri | A: ayrı yetenek arayüzü `ISpeechDialogueSynthesizer` · B: `ISpeechSynthesizer`'a varsayılan gövdeli üye | **A** — Faz 198 § 198.6 bu biçime yer bıraktı; tek sesli sağlayıcıya sahte üye yazdırmaz |
| 2 | Ayarların biçimi | A: önekli `ProviderSettings` sözlüğü · B: tipli `SpeechVoiceSettings` (stability, similarity, style, speed) | **A** — ortak payda yok (Gemini'de bu alanlar yok); tipli kayıt en küçük paydaya iner (F-296 risk satırı); K-208 deseni |
| 3 | Bölme ve birleştirme | A: yok · B: yalnız bölme · C: bölme + PCM birleştirme | **A — karar verildi** (bakımcı, 2026-10-13): medya araç seti işidir; ilk öneri C tek tüketicinin koduna dayanıyordu ve geri alındı |
| 4 | Diyalog modeli varsayılanı | A: API varsayılanı · B: `VoiceOptions.DialogueModelId` | **B**, varsayılan `null` = API varsayılanı |
| 5 | HTTP ucu (`POST /api/voice/dialogue`) | A: bu fazda · B: yok | **B** — kullanım workflow düğümündedir; uç Faz 198'in çözümleyicisiyle birlikte gelir |
| 6 | `POST /api/voice/speak` `providerSettings` taşısın mı | A: evet · B: hayır | **B** — uç operatör denemesidir; OpenAPI ve iki istemci büyümez |
| 7 | Diyalog yeteneği kiracı kapısından nasıl geçer (Faz 198 devri) | A: sarmalayıcı yeteneği de uygular (yetenek başına sarmalayıcı sınıfı; kiracı anahtarını `SpeechDialogueRequest.Credential` ile iletir) · B: çözümleyiciye `GetDialogueSynthesizerAsync(name, ct)` · C: yetenek kiracı anahtarını hiç almaz, bağlamalı kiracıda reddedilir | **B — karar verildi** (bakımcı, 2026-10-13): K-930 her konuşma çağrısını çözümleyiciden geçirir; B tip denetimini ve ret yolunu orada tek yerde tutar ve `is` tuzağını kaldırır. A'da sarmalayıcı yeteneği olmayan motoru da "var" gösterirdi (iki sınıf gerekirdi) |

---

## Bitiş Ölçütleri (DoD)

- [ ] `ISpeechDialogueSynthesizer` ElevenLabs'te `text-to-dialogue` ve `/with-timestamps` çağırır; segmentler tura eşlenir (test 1, 2, 6)
- [ ] Sınır ve ayar ihlali ağdan önce `ArgumentException`; ret değeri yankılamaz (test 3, 4)
- [ ] Tek ses yolu `voice_settings` ve süreklilik alanlarını gönderir; mevcut istekler değişmez (test 5)
- [ ] Yetenek kaydı tüketicinin kaydını ezmez; yeteneksiz sentezleyici açık hata verir (test 10)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek diyalog `run`'ı (case 1), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz; `CHANGELOG.md` yeni tipleri anar

### Doğrulama komutları

Örnek uygulama: port 5082, şema `faz203`, `Tracon__Ui__AuthToken=` (boş),
derlenmiş DLL `--contentRoot` ile. Sürücü betiği `PUT /api/workflows/<ad>` ve
`POST /api/workflows/<ad>/run` çağırır, sonra `GET /api/runs/{runId}` okur.
Gerçek çıktı (2026-10-13):

**Case 1 — canlı ElevenLabs** (`MT-MM-132`; iki Türkçe replik, `eleven_v4`,
`tr`, `pcm_24000`, zaman damgası; sesler `onwK4e9ZLuTAKqWW03F9`,
`TX3LPaxmHKxFdv7VOQHJ`):

```text
run status: Completed
output: [dialogue by elevenlabs: attachment 01a135f0-33b7-7590-a74a-29e414c2bfb5, 226604 bytes, 14 characters (Provider), model eleven_v4, request 68CbG0CXFbZVMwAKCKYy]
turn 0 (onwK4e9ZLuTAKqWW03F9): 0.00-3.12 s
turn 1 (TX3LPaxmHKxFdv7VOQHJ): 3.12-4.80 s
attachment GET: 200 audio/wav 226604 bytes, header b'RIFF' b'WAVE'
```

İki segment sıralı ve boşluksuzdur (ikinci tur birincinin bitişinde başlar);
`characterCost` sağlayıcıdan gelir (62 karakterlik metin için `14`). 👤 Dinleme
yapılmadı (agent oturumu).

**Case 2** (`MT-MM-133`; `elevenlabs.stability: 0.3`, istekleri kaydeden yerel
sahte ElevenLabs ucu, `Tracon__Voice__Endpoint=http://127.0.0.1:5099/`):

```text
{"method": "POST", "path": "/v1/text-to-dialogue/with-timestamps?output_format=pcm_24000", "has_key_header": true, "body": {"inputs": [{"text": "Merhaba.", "voice_id": "onwK4e9ZLuTAKqWW03F9"}, {"text": "Selam.", "voice_id": "TX3LPaxmHKxFdv7VOQHJ"}], "model_id": "eleven_v4", "language_code": "tr", "settings": {"stability": 0.3}}}
```

Aynı ayarla canlı koşum da `Completed` döndü (`3 characters (Provider)`).

**Case 3** (`MT-MM-134`; `elevenlabs.bogus: "do-not-echo-me"`; canlıda ve sahte
uçta aynı):

```text
run status: Failed
run error: {"type": "Tracon.ProviderSettingsValidationException", "message": "ProviderSettings is invalid: these keys are not recognized: elevenlabs.bogus. Supported keys: elevenlabs.nextText, elevenlabs.previousRequestIds, elevenlabs.previousText, elevenlabs.seed, elevenlabs.similarityBoost, elevenlabs.stability.", ...}
names the key: True | echoes the value: False
```

Sahte uçtaki istek sayısı değişmedi (1 → 1).

**Case 4** (`MT-MM-135`; `"provider":"tone"`): `Failed`, `The speech synthesizer
'tone' (provider 'tone') cannot voice a dialogue: it does not implement
ISpeechDialogueSynthesizer.` İstek sayısı 1 → 1.

**Case 5** (`MT-MM-136`; 12 replik, 2.726 karakter): `Failed`, `The dialogue has
2726 characters; one 'elevenlabs' dialogue request takes at most 2000
(VoiceOptions.MaxDialogueCharactersPerRequest).` İstek sayısı 1 → 1.

**Case 6** (`MT-MM-137`, plana eklendi; politika `["tone"]`): `Failed`, `Tenant
'default' is not allowed to call speech provider 'elevenlabs'. Allowed
providers: tone.` İstek sayısı 1 → 1; `DELETE` → `204`.

**Adım 0 ölçümü** (ElevenLabs OpenAPI, 2026-10-13; üç kısa canlı istek, 14
karakter): diyalog `settings` yalnız `stability` ve `similarity` taşır
(`ToDialogueSettingsResponseModel`, ikisi de 0–1). Servis bilinmeyen bir
`settings` alanını hatasız kabul eder (`settings.style: 0.9` → `200`). Aynı
`seed` (42) ile iki özdeş istek farklı ses baytı döndü; bu yüzden `similarity`
alanının **etkisi ölçülemedi** (karşılaştırma için belirlenimci çıktı yok).
Yanıt `character-cost` ve `request-id` başlıklarını taşır; `voice_segments`
giriş başına bir segment döndü ve `alignment` girişlerin metnini ayırıcısız
birleştirir. Tek ses gövdesi: `voice_settings {stability 0–1, similarity_boost
0–1, style, use_speaker_boost, speed}`, `seed` (0–4294967295), `previous_text`,
`next_text`, `previous_request_ids` (≤ 3). Diyalog gövdesi: `settings`, `seed`,
`previous_text`/`future_text` (≤ 100 karakter), `previous_request_ids`. `style`
ve `speed` aralığı belgede yok — **ölçülmedi**; sınır sağlayıcıya bırakıldı.

---

## Riskler

| Risk | Önlem |
|------|-------|
| ElevenLabs diyalog API'si değişir (alan, model adı) | Gövde ve yanıt birim testleri sabit JSON'la; canlı case 1 kapanışta koşar |
| Diyalog `settings` alanları belgesiz kalır | Adım 0 ölçümü; etkisiz alan reddedilir |
| Gemini sınırı (istekte 2 ses) sözleşmeyi zayıflatır | Yetenek bildirimi (`MaxVoices`); bölme çağıranın kararı |
| Sözleşme tek tüketicinin ihtiyacına göre biçimlenir | Genellik satırı; her alan iki sağlayıcıda karşılık bulmalı ya da önekli sağlayıcı ayarına gitmeli |
| Segment indeksleri sağlayıcıda kayar | Doğrulamasız eşleme yok; tutmayan yanıt `null` |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME. -->

## Plandan Sapmalar

> Aşama 1 sonunda yazıldı (2026-10-13); denetimden sonra hizalanır.

1. **§ 203.1 "Kayıt" düştü — yetenek DI'a kaydedilmez (Açık Soru 7-B'nin
   sonucu).** Ölçüldü: `GetService<ISpeechDialogueSynthesizer>()` ham örneği
   verirdi; çağıran egress'i ve kiracı anahtarı kuralını (K-930) atlardı. Yer
   tutucu (`MaxVoices = 0`) da gereksiz kaldı: yeteneği taşımayan motoru
   çözümleyici adıyla reddeder (`SpeechCapabilityNotSupportedException`,
   `TraconException`; mesaj kayıt adını ve `ProviderName`'i söyler). Test:
   `VoiceDialogueRegistrationTests`.
2. **`GetDialogueSynthesizerAsync` soyut üyedir, varsayılan gövdeli değil.**
   Ölçüldü: `ISpeechProviderResolver` `CHANGELOG` `[Unreleased]`'dadır;
   `git show v1.0.0-preview.3:src/Tracon.Abstractions/Voice/SpeechProviderContracts.cs`
   → dosya yok; `src/` içinde tek uygulayıcı `internal SpeechProviderResolver`'dır,
   testlerde sahte uygulayıcı yok. Ad tektir, aşırı yükleme yoktur: RS0026/RS0027
   tetiklenmez. Varsayılan gövde yalnız istisna atabilirdi (kapı Core'da
   `internal`); soyut üye, kendi çözümleyicisini yazan host'a kapıyı derleme
   anında yazdırır. Varsayılan üye emsalleri (`SupportsTenantCredentials`,
   `ListVoicesWithCredentialAsync`, `ForSynthesis`) **yayınlanmış** arayüzlere
   eklenmişti.
3. **Arayüze `SupportsTenantCredentials` (varsayılan `false`) ve isteğe
   `Credential` eklendi.** Planın taslağında yoktu; 7-B'nin kiracı anahtarı kuralı
   bunları ister (Faz 198 deseni). Kiracının anahtarı varsa ve motor destekliyorsa
   çözümleyici `TenantCredentialSpeechDialogueSynthesizer` döner; çağıranın
   kimliği değiştirilir.
4. **Ret tipi `ArgumentException` değil `TraconException`.** Ölçüldü:
   `SafeErrorText.ForPersistence` `TraconException` olmayan istisnayı run
   kaydında `ArgumentException failed. (ref: …)` diye maskeler; diyaloğun tek
   çağıranı iş akışı düğümüdür ve case 3 ile 5 mesajı run kaydında ister. Var olan
   konuşma doğrulaması (ses kimliği yok, akış + zaman damgası) ve model ayar
   doğrulaması da `TraconException`'dır. `null` istek `ArgumentNullException` kalır.
5. **Ayar anahtarları Adım 0'a göre daraldı.** `elevenlabs.style`,
   `useSpeakerBoost`, `speed` diyalogda kendi gerekçesiyle reddedilir ("no effect
   on a dialogue"); `similarityBoost` diyalogda `settings.similarity`'ye,
   `nextText` `future_text`'e eşlenir. Diyalog süreklilik metni ≤ 100 karakter;
   `stability`/`similarityBoost` 0–1, `seed` 0–4294967295 ağdan önce denetlenir.
6. **`SpeechAudio.ProviderRequestId` eklendi (planda yok).** Gerekçe: planın
   `elevenlabs.previousRequestIds` anahtarı, sağlayıcının istek kimliğini
   öğrenmenin yolu olmadan kullanılamazdı. ElevenLabs `request-id` başlığını
   döndürür (canlı ölçüldü); tek ses ve diyalog yolu doldurur.
7. **Sözlük okuyucuları `internal`; `Tracon.Voice`'a `InternalsVisibleTo`.**
   Plan yalnız `Validate` aşırı yüklemesini sayıyordu. Tip kuralı (metin `"true"`,
   `JsonValueKind` adı, değer yankısı yok) tek yerde kalır (K-483); public yüzey
   büyümez. Gerekçe model adaptörlerininkiyle aynıdır (birinci taraf adaptör).
8. **Segment eşlemesi uzunluğu karşılaştırmaz.** OpenAPI bir segmenti bir
   girişin "parçası" diye tanımlar. Her segment indeks aralığı, turun ses kimliği
   ve sıralı sonlu zamanla doğrulanır; biri tutmazsa liste `null` olur.
9. **"Ret değeri yankılamaz" üç sağlayıcıya birlikte bağlandı (Faz 204 devri).**
   Anthropic seçim ve düşünme bütçesi, Google güvenlik eşiği ve düşünme bütçesi
   mesajları değeri artık yazmaz; `ModelProviderSettingsContract` metin
   `InvalidSettingValue` için bunu doğrular (değişiklikten önce iki sözleşme
   sınıfı kırmızıydı). Sözleşme paketi sıkılaştı: değeri yankılayan üçüncü taraf
   sağlayıcı bu senaryoda düşer.
10. **Kusur (kusur-giderme, faz alanı):** yerleşik istemci tek ses, akış ve
    tanımada gelen kiracı kimliğini yok sayıp kurulum anahtarıyla çağırıyordu
    (çözümleyici göndermez; doğrudan çağıran yanlış hesaba fatura keserdi). Artık
    `NotSupportedException`. Önce kırmızı: mutasyonla iki test.
    Sınıf taraması: `src/` içinde `ISpeechSynthesizer`/`ISpeechTranscriber`
    uygulayıcıları yerleşik istemci ve testlerdir; `OpenAILiveProvider` kimliği
    kullanır; örnek `ToneSpeechSynthesizer` kimliği yok sayar (örnek, anahtarsız
    yerel motor; çözümleyici ona kimlik göndermez) — başka vaka yok.
11. **Manuel case 6 eklendi** (`MT-MM-137`, kiracı egress reddi); plan 5 case
    sayıyordu. Case 2–6 yerel sahte ElevenLabs ucuyla koşuldu: "ağ çağrısı yok"
    iddiası ancak orada sayılabilir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen: yetenek arayüzü ve ayar sözlüğü
> (`public-api`).

## Gerçekleşen Public API

> Kapanışta doldurulur.

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
> yazılır.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: sözleşmeler, tuzaklar (🚨), yarım işler, sıradaki faz.
