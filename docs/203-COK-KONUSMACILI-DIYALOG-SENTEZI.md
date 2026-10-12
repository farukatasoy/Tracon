# Faz 203 — Çok Konuşmacılı Diyalog Sentezi ve Sağlayıcı İstek Ayarları

> **Durum:** 📋 Planlandı (2026-10-13)
> **Plan onayı:** onaylanmadı — bakımcı 2026-10-13'te F-296'nın planlanmasını ve Faz 201'den önce uygulanmasını istedi; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-296**
> **Önkoşul:** Yok. **Sıra:** Faz 198 ve Faz 201'den önce (bakımcı kararı, 2026-10-13). Faz 198 § 198.6 bu fazın yetenek arayüzünü sarmalar; F-297 (Gemini) bu sözleşmeyi uygular
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

---

## Amaç

Tracon'un ses sözleşmesi istek başına tek ses taşır. İki konuşmacılı bir
podcast bugün ya her repliği ayrı seslendirip birleştirir (dinleme testinde
"okuyor gibi" bulundu) ya da sağlayıcının diyalog API'sini kendi koduyla
çağırır. Bu faz diyalog sentezini bir **yetenek arayüzü** olarak ekler,
yerleşik ElevenLabs istemcisine uygular ve sağlayıcıya özgü istek
ayarlarını (ses ayarları, süreklilik) iki sözleşmeye de taşır.

- **F-296** — konuşmacı-tur listesi, yetenek bildirimi, segment zamanları,
  sağlayıcı istek ayarları, bölme ve PCM birleştirme yardımcıları.

**Tüketici bağlamı (ölçüldü, 2026-10-13).** Podcast pilotu ElevenLabs
`POST /v1/text-to-dialogue/with-timestamps` çağırır: `inputs[] {text,
voice_id}`, `model_id: eleven_v4`, `language_code: tr`, `settings
{stability, similarity}`, `output_format=pcm_24000`. Episode başına 7–9
istek, istek başına ≤ 1.900 karakter; yanıtın `voice_segments`'i (giriş
başına başlangıç/bitiş saniyesi) konuşmacı başına ses seviyesi ölçümüne ve
bölüm işaretlerine girer. Plan geneldir; bu tüketici ölçüt örneğidir.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Kiracı başına sağlayıcı seçimi ve kiracı anahtarı | Faz 198 (F-298); bu faz süreç geneli kalır |
| Gemini diyalog adaptörü | F-297; bu sözleşmeyi uygular |
| Fonksiyon düğümünün TTS maliyeti | Faz 199 (F-299); bu faz yalnız `CharactersBilled` döner |
| `speak` tool'unun diyalog sürümü | Tool tek replik içindir; diyalog workflow düğümünde çağrılır (Açık Soru 5) |
| MEAI `ITextToSpeechClient` uyarlaması | MEAI 10.9.0 sözleşmesi tek ses taşır (`TextToSpeechOptions.VoiceId`); diyalog karşılığı yok |
| Ses seviyesi, tempo, müzik | Tüketicinin medya işidir (ffmpeg) |

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
| [Faz 198](198-ADLI-KONUSMA-SAGLAYICISI.md) § 198.6 | "F-296'nın yetenek arayüzünü sarmalayıcı da uygular" — yer bırakılmış |
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
  `ArgumentException`. Sessiz bölme yok (K1): bölme açık bir çağrıdır
  (§ 203.4).
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

## 203.4 — Bölme ve PCM birleştirme yardımcıları

- `SpeechDialogueChunker.Split(turns, maxCharacters)` (`Tracon.Voice`): tur
  sınırında böler; sınırı aşan tek repliği cümle sınırında aynı sesle böler;
  sınırı aşan tek cümle `ArgumentException`. Parça, kaynağın tur
  indekslerini taşır. Bölüm sınırı gibi domain kuralı çağırandadır.
- `SpeechPcm.Concatenate(parts, sampleRate, gap)`: yalnız 16-bit mono PCM;
  segmentleri birleşik zamana kaydırır. MP3 birleştirilmez (kodek işi).
- İkisi de saf fonksiyondur; ağ, DI ve kiracı yoktur (Açık Soru 3).

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
static class SpeechDialogueChunker { Split(...) }  ·  static class SpeechPcm { Concatenate(...) }
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
├── SpeechDialogueChunker.cs · SpeechPcm.cs (yeni)
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
| 11 | Bölme tur sınırını bozar; uzun replik sesini kaybeder; tek uzun cümle sessizce kesilir | Birim | `SpeechDialogueChunkerTests` |
| 12 | PCM birleştirme segmentleri kaydırmaz; tek bayt kaydırması ses bozar | Birim | `SpeechPcmTests` |
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
| 5 | 4.500 karakterlik 12 replik | `SpeechDialogueChunker.Split(…, 2000)` + sentez + `SpeechPcm.Concatenate` | Üç parça; birleşik segmentler sıralı ve kesintisiz |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sözleşmenin yeri | A: ayrı yetenek arayüzü `ISpeechDialogueSynthesizer` · B: `ISpeechSynthesizer`'a varsayılan gövdeli üye | **A** — Faz 198 § 198.6 bu biçime yer bıraktı; tek sesli sağlayıcıya sahte üye yazdırmaz |
| 2 | Ayarların biçimi | A: önekli `ProviderSettings` sözlüğü · B: tipli `SpeechVoiceSettings` (stability, similarity, style, speed) | **A** — ortak payda yok (Gemini'de bu alanlar yok); tipli kayıt en küçük paydaya iner (F-296 risk satırı); K-208 deseni |
| 3 | Bölme ve birleştirme | A: yok · B: yalnız bölme · C: bölme + PCM birleştirme | **C** — ikisi de saf fonksiyon; tüketicinin en hatalı kodu (segment kaydırma) buradadır |
| 4 | Diyalog modeli varsayılanı | A: API varsayılanı · B: `VoiceOptions.DialogueModelId` | **B**, varsayılan `null` = API varsayılanı |
| 5 | HTTP ucu (`POST /api/voice/dialogue`) | A: bu fazda · B: yok | **B** — kullanım workflow düğümündedir; uç Faz 198'in çözümleyicisiyle birlikte gelir |
| 6 | `POST /api/voice/speak` `providerSettings` taşısın mı | A: evet · B: hayır | **B** — uç operatör denemesidir; OpenAPI ve iki istemci büyümez |

---

## Bitiş Ölçütleri (DoD)

- [ ] `ISpeechDialogueSynthesizer` ElevenLabs'te `text-to-dialogue` ve `/with-timestamps` çağırır; segmentler tura eşlenir (test 1, 2, 6)
- [ ] Sınır ve ayar ihlali ağdan önce `ArgumentException`; ret değeri yankılamaz (test 3, 4)
- [ ] Tek ses yolu `voice_settings` ve süreklilik alanlarını gönderir; mevcut istekler değişmez (test 5)
- [ ] Yetenek kaydı tüketicinin kaydını ezmez; yeteneksiz sentezleyici açık hata verir (test 10)
- [ ] Bölme ve PCM yardımcıları test 11-12'yi geçer
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek diyalog `run`'ı (case 1), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz; `CHANGELOG.md` yeni tipleri anar

### Doğrulama komutları

Case 1 ve 5'ten kapanışta gerçek çıktıyla yazılır.

---

## Riskler

| Risk | Önlem |
|------|-------|
| ElevenLabs diyalog API'si değişir (alan, model adı) | Gövde ve yanıt birim testleri sabit JSON'la; canlı case 1 kapanışta koşar |
| Diyalog `settings` alanları belgesiz kalır | Adım 0 ölçümü; etkisiz alan reddedilir |
| Gemini sınırı (istekte 2 ses) sözleşmeyi zayıflatır | Yetenek bildirimi (`MaxVoices`); bölme çağıranın kararı |
| Segment indeksleri sağlayıcıda kayar | Doğrulamasız eşleme yok; tutmayan yanıt `null` |

---

<!-- AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME. -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez**.

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
