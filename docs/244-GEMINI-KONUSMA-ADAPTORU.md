# Faz 244 — Gemini Konuşma Adaptörü ve Diyalog Yeteneğinin Görünürlüğü

> **Durum:** 📋 Planlandı (2026-10-25)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (yalnız ekleme) · hedef framework değişmez · güvenlik/kiracı sınırı değişmez: kapı `SpeechProviderResolver`'dadır (K-878 kuralı); adaptör yalnız kapının verdiği kimliği kullanır ve Google sohbet sağlayıcısının kimlik önbelleği desenini izler · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-25 (sohbette alınan karar: adaptör kiracının kendi Google anahtarını kullanabilir — `SupportsTenantCredentials = true`)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-297**, **F-465**
> **Genellik:** Konuşma sözleşmesini (Faz 198, Faz 203) kullanan ve Gemini sağlayıcısı olan her kurulum. Tek tüketici turundan geldi (2026-10-11 podcast). İkinci kanıt ilk ilkedir: diyalog sözleşmesinin (K-952) bugün tek uygulaması vardır (ElevenLabs). Tek uygulamalı bir sözleşmenin sağlayıcıdan bağımsız olduğu kanıtlanmamıştır. Ekosistem kanıtı: Gemini çok konuşmacılı sentezi (`MultiSpeakerVoiceConfig`) resmî SDK'da hazırdır. F-465'in plan notu tetiği adıyla yazar: "ikinci diyalog sağlayıcısı (F-297) gelince"
> **Karşı görüş:** "Faz 198 olmadan adaptör ElevenLabs'in yerine geçer, yanına gelmez." Cevap: Faz 198 kapandı. Adaptör adlı sağlayıcı olarak (`AddSpeechSynthesizer("gemini", …)`) ElevenLabs'in **yanına** kaydolur; çağıran adla seçer
> **Paketler:** `Tracon.Google` (adaptör, kayıt uzantısı), `Tracon.Abstractions` (`SpeechProviderDescriptor` alanı, diyalog istek kuralı), `Tracon.Core` (çözümleyicinin descriptor'ı), `Tracon.Voice` (ElevenLabs ortak kurala geçer), `Tracon.AspNetCore` (OpenAPI)
> **Tüketici yüzeyi:** site `guides/voice.md` (Gemini bölümü, diyalog yeteneği), `packages.md` (`Tracon.Google` satırı), `capabilities.md` satırı · sevk edilen: `src/Tracon.Google/README.md`, `UseGoogleSpeech` XML `<example>`, OpenAPI → iki istemci (üretilir), `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/19-COK-MODLULUK-VE-SES.md`

## Amaç

`Tracon.Google` bugün konuşma yüzeyi taşımaz. Tek eşleşme bir güvenlik eşiğinin
adıdır (`GoogleProviderNames.cs:28-29`, `SafetyHateSpeechSetting`). Ayrıca
`GET /api/voice/providers` bir motorun diyalog seslendirip seslendiremediğini
söylemez. Çağıran bunu ancak `GetDialogueSynthesizerAsync` hatasından öğrenir.

- **F-297** — `UseGoogleSpeech(...)`: Gemini TTS modeliyle tek ses ve çok
  konuşmacılı diyalog. Aynı örnek `ISpeechSynthesizer` ve
  `ISpeechDialogueSynthesizer` uygular (Faz 203 sözleşmesi).
- **F-465** — `SpeechProviderDescriptor`'a diyalog yeteneği alanı;
  `GET /api/voice/providers` onu gösterir.

**Doğrulanmış kanıt (2026-10-25):**

| Kanıt | Gözlem |
|---|---|
| `src/Tracon.Google/` (`grep -rln "ISpeechSynthesizer"` boş) | Konuşma yüzeyi yok |
| [`SpeechProviderContracts.cs:159-176`](../src/Tracon.Abstractions/Voice/SpeechProviderContracts.cs) | Descriptor: `Name`, `Kind`, `ProviderName`, `SupportsTenantCredentials`. Diyalog yok |
| [`SpeechProviderResolver.cs:59`](../src/Tracon.Core/Voice/SpeechProviderResolver.cs), `:142-171` | Liste kayıtlı örnekleri çözer; diyalog yeteneği örneğin tipinden (`is ISpeechDialogueSynthesizer`) okunur |
| [`ElevenLabsSpeechClient.cs:597-642`](../src/Tracon.Voice/Internal/ElevenLabsSpeechClient.cs) | Diyalog kuralı (tur, ses kimliği, ses sayısı, karakter sınırı) `internal` ve `Tracon.Voice` içindedir; `Tracon.Google` onu göremez |
| [`OpenAISpeechBuilderExtensions.cs:47-118`](../src/Tracon.OpenAI/OpenAISpeechBuilderExtensions.cs) | Sağlayıcı paketinde adlı konuşma kaydının emsali: `UseOpenAI`'nin istemcisini paylaşır, egress adı sohbetle aynıdır, `Voices` seçenekten gelir |
| [`GoogleImageBuilderExtensions.cs`](../src/Tracon.Google/GoogleImageBuilderExtensions.cs) · [`GoogleChatClientFactory.cs:129`](../src/Tracon.Google/GoogleChatClientFactory.cs) | `UseGoogle`'ın istemcisini paylaşan ikinci yetenek emsali (`CreateImageGenerator`) |
| [`GoogleModelProvider.cs:115-150`](../src/Tracon.Google/GoogleModelProvider.cs) | Kiracı anahtarı yolu: `BuildCredentialFactory` + `ModelProviderCore` kimlik önbelleği + egress soket koruması |
| `Google.GenAI` 1.26.1 (`maf-api-kesfi`, 2026-10-25) | `SpeechConfig { VoiceConfig, MultiSpeakerVoiceConfig, LanguageCode }`, `SpeakerVoiceConfig { Speaker, VoiceConfig }`, `PrebuiltVoiceConfig.VoiceName`; `GenerateContentConfig { ResponseModalities, SpeechConfig }`; `Models.GenerateContentAsync` / `GenerateContentStreamAsync`; ses `Blob { Data, MimeType }`; `GenerateContentResponseUsageMetadata`. Aday 1.16.0'ı ölçmüştü; paket 1.26.1'e yükseldi (`Directory.Packages.props:134`). 1.26.1'de `VoiceConfig.Voice` ve `ReplicatedVoiceConfig` (ses klonlama) yeni |

## Tasarım

- **Kayıt:** `UseGoogleSpeech(Action<GoogleSpeechOptions>)`. Seçenekler:
  `Model` (zorunlu; model adı kodda tutulmaz), `Name` (varsayılan `gemini`),
  `Voices` (ses adları; liste kodda tutulmaz — "Yerleşik OpenAI model listesi
  kodda tutulmadı" kararının eşi), `MaxCharactersPerRequest`,
  `MaxDialogueCharactersPerRequest`, `UseAsDefault` (`false`; K1).
  `UseGoogle` önce çağrılır; adaptör onun `GoogleChatClientFactory`'sinin
  istemcisini paylaşır. Egress adı `google`dur: sohbet, görsel ve konuşma tek
  kural (OpenAI emsali).
- **Tek ses:** `GenerateContentAsync(model, text, config)`;
  `ResponseModalities = ["AUDIO"]`, `SpeechConfig.VoiceConfig.PrebuiltVoiceConfig.VoiceName`,
  `LanguageCode`.
- **Diyalog:** `MultiSpeakerVoiceConfig.SpeakerVoiceConfigs` — ses başına bir
  konuşmacı takma adı (`Speaker1`, `Speaker2`). Metin `Speaker1: …` satırlarından
  kurulur. `MaxVoices = 2`, `SupportsSegmentTimestamps = false`. 🚨 Tur metni
  satır başında başka bir takma ad taşırsa sesi değiştirebilir. Adaptör bu
  durumu ağdan önce reddeder; metni sessizce değiştirmez.
- **Çıktı:** Gemini ham PCM verir (`audio/L16;codec=pcm;rate=…`). Adaptör
  `wav` (varsayılan; RIFF başlığı kodda) ya da `pcm` verir. Başka bir
  `OutputFormat` ağdan önce reddedilir (K-955). Örnekleme hızı yanıttaki
  `MimeType`'tan okunur. **Ölçülmeli:** gerçek yanıtın `MimeType` değeri.
- **Akışlı yol:** `GenerateContentStreamAsync` TTS modelinde sesi parça parça
  veriyor mu? **Uygulamanın ilk ölçümü.** Vermiyorsa `SynthesizeStreamingAsync`
  bütün sesi tek parça olarak verir; XML ve site bunu yazar.
- **Zaman damgası:** Gemini hizalama vermez. `IncludeTimestamps = true` ağdan
  önce `TraconException` alır (K-955).
- **Ayarlar:** konuşma isteği `google.*` anahtarı kabul etmez:
  `ModelProviderSettings.Validate(request.ProviderSettings, "google", [])` (K-953).
  Ret, değeri yankılamaz (K-954).
- **Kiracı anahtarı (bakımcı kararı):** `SupportsTenantCredentials = true`.
  `SpeechRequest.Credential` / `SpeechDialogueRequest.Credential` doluysa istemci
  `GoogleModelProvider.BuildCredentialFactory` deseniyle ve aynı kimlik önbelleğiyle
  kurulur. Kiracı uç noktası egress soket korumasından geçer. İkinci bir önbellek
  yazılmaz. Kimlik yoksa kurulum istemcisi kullanılır.
- **Fatura:** Gemini TTS token'la faturalar, karakterle değil. Tracon'un konuşma
  ücret modeli karakter başınadır (`IVoicePricingReader.ForSynthesis`). Adaptör
  OpenAI emsalini izler: `CharactersBilled` = metin uzunluğu,
  `UsageSource = Estimated`. Ücret, kurulum karakter fiyatı tanımladıysa
  hesaplanır. Token tabanlı kesin ücret kapsam dışıdır (Açık Soru 2).
- **Kimlikler:** `SpeechAudio.ModelId` yanıtın model sürümünden,
  `ProviderRequestId` yanıt kimliğinden doldurulur (K-956). **Ölçülmeli:** iki
  alanın `GenerateContentResponse`'taki adları.
- **Ortak diyalog kuralı:** ElevenLabs'teki kural (`CheckDialogue`) bir ortak
  yardımcıya taşınır ve iki adaptör onu kullanır. Yer: `Tracon.Abstractions`
  içinde public bir yardımcı (ör. `SpeechDialogueCapabilities.Check(request, providerName)`).
  Gerekçe: üçüncü taraf bir diyalog adaptörü de aynı sözleşmeyi (K-955) uygulamak
  zorundadır. Reddedilen alternatif: `Tracon.Core` içinde `internal` +
  `InternalsVisibleTo("Tracon.Google")` — tüketicinin adaptörü kuralı kopyalar.
  ElevenLabs'in ret metni korunur (mevcut testler değişmeden yeşil).
- **F-465:** `SpeechProviderDescriptor.Dialogue` (`SpeechDialogueCapabilities?`).
  `null` = diyalog yok. Değer kayıtlı örnekten okunur, kiracı sarmalayıcısından
  okunmaz (çözümleyicinin bugünkü kuralı). Transcriber için her zaman `null`.
- **AOT:** `Tracon.Google` AOT vaadi taşımaz (K-1019). Adaptör yansımaya dayanmaz;
  WAV başlığı elle yazılır.

## Bitiş Ölçütleri (DoD)

- [ ] `UseGoogleSpeech` adlı bir synthesizer kaydeder; `POST /api/voice/speak` `provider: "gemini"` ile WAV döner
- [ ] Diyalog iki sesle seslendirilir; üç ses, boş tur, ses kimliği yok, aşırı karakter, zaman damgası ve bilinmeyen `google.*` ayarı ağdan önce `TraconException` alır
- [ ] Kiracı anahtarı olan kiracının isteği o anahtarla gider; anahtarsız kiracı kurulum anahtarıyla; iki kiracı birbirinin istemcisini kullanmaz (tel düzeyinde test)
- [ ] `GET /api/voice/providers` her synthesizer için `dialogue` alanını verir (ElevenLabs ve Gemini dolu, OpenAI `null`); OpenAPI ve iki istemci yeniden üretildi
- [ ] ElevenLabs ortak kurala geçti; diyalog testleri değişmeden yeşil
- [ ] Akışlı yol ölçümü ve `MimeType` ölçümü belgeye yazıldı
- [ ] `samples/Tracon.Api` Google anahtarıyla `UseGoogleSpeech` kaydeder; gerçek tek ses ve diyalog çıktısı alındı (komut + boyut + süre belgeye yazıldı)
- [ ] Site sayfaları, `src/Tracon.Google/README.md` ve `CHANGELOG.md [Unreleased]` güncel
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| İstek gövdesi (`responseModalities`, `speechConfig`, konuşmacı eşlemesi) doğru kurulur | Birim (sahte HTTP işleyicisi, `Google.GenAI` istemcisi) | `GoogleSpeechSynthesizerTests` |
| PCM → WAV başlığı ve örnekleme hızı | Birim | `GoogleSpeechSynthesizerTests` |
| Ağdan önceki retler (K-955) ve ret metninin değeri yankılamaması (K-954) | Birim | `GoogleSpeechSynthesizerTests` |
| Takma ad enjeksiyonu reddedilir | Birim | `GoogleSpeechSynthesizerTests` |
| Adlı kayıt, egress politikası (`google`) ve çözümleyici | Fonksiyonel (gerçek host) | `NamedSpeechProviderTests` (Gemini satırı) |
| Kiracı anahtarı yolu, iki kiracı | Fonksiyonel (tel düzeyi, sahte Gemini ucu) | `SpeechDialogueTenantTests` (Gemini satırı) |
| Descriptor'da `dialogue` alanı | Fonksiyonel (HTTP) | `NamedSpeechProviderTests` |
| ElevenLabs ortak kurala geçince davranışı değişmez | Birim (mevcut) | `ElevenLabsSpeechClientTests` |
| Sağlayıcı hatası normalleşir, anahtar sızmaz | Fonksiyonel | `SecretLeakTests` (Gemini satırı) |

Beş soru: iptal — token `GenerateContentAsync`'e geçer; akış iptalde durur ·
eşzamanlılık — tek örnek, durum tutmaz; istemci paylaşılır (Google SDK iş
parçacığı güvenliği `UseGoogleImages` ile zaten kullanılıyor) · boş/aşırı girdi —
yukarıdaki retler · başka kiracının kaydı — kimlik önbelleği kimliğe göre
anahtarlanır; test iki kiracıyı ayırır · alt sistem hatası — sağlayıcı hatası
bugünkü normalleştiriciden geçer. `🤖` case: gerçek Gemini ile tek ses ve diyalog;
çıktının dinlenmesi `👤` değil, süre ve boyut ölçümüyle kabul edilir.

## Açık Sorular

| # | Soru | Öneri |
|---|---|---|
| 1 | Ortak diyalog kuralı public mi olsun? | Evet (Tasarım). Alternatif `internal` + `InternalsVisibleTo`'dur; tüketici adaptörü kuralı kopyalar |
| 2 | Token tabanlı kesin ücret | Bu fazda yok. `SpeechAudio` token taşımaz; ücret modeli değişikliği ayrı aday olur. Ölçülen yanıtın `UsageMetadata`'sı belgeye yazılır |
| 3 | Descriptor ayrıca `SupportsDialogue` bool taşısın mı? | Hayır. `dialogue != null` aynı bilgidir; iki alan ayrışabilir |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.
> Beklenen kayıtlar: ortak diyalog kuralının public yardımcı olması *(public-api;
> alternatif: internal + InternalsVisibleTo)* · `SpeechProviderDescriptor.Dialogue`
> *(public-api; alternatif: ayrı `SupportsDialogue` bool)*.

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
