# Faz 244 — Gemini Konuşma Adaptörü ve Diyalog Yeteneğinin Görünürlüğü

> **Durum:** ✅ Tamamlandı (2026-10-25)
> **Kademe:** tam — plan *hafif* yazılmıştı (kapı `SpeechProviderResolver`'dadır, K-878 kuralı değişmez); denetim (🔴) kiracı secret'ını alan kod kümesi büyüdüğü için kademeyi düzeltti: `ModelProviderCore<T>.GetTenantFactory`, `GoogleModelProvider.GetTenantFactory` ve `SupportsTenantCredentials = true` yeni yetenek için kiracı anahtarı yolunu genişletir (K-1093, `kategori: güvenlik`). Tam zincirin gereği zaten karşılandı: planın kapsamı ayrıntılıydı, tel düzeyi kiracı testleri ve örnek uygulama koşumu var. Migration yok · public API'den satır silinmez (yalnız ekleme) · hedef framework değişmez
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

- [x] `UseGoogleSpeech` adlı bir synthesizer kaydeder; `POST /api/voice/speak` `provider: "gemini"` ile WAV döner — örnek uygulama, gerçek Gemini (`gemini-3.8-flash-tts`, ses `Kore`, 66 karakter): `200`, `audio/wav`, **230.756 bayt**, `RIFF`/`WAVE` 24 kHz tek kanal 16 bit, **4,68 sn**, istek 4,4 sn; `data` parçasından sonra `C2PA` parçası. Fonksiyonel: `GeminiSpeechTests`
- [x] Diyalog iki sesle seslendirilir; üç ses, boş tur, ses kimliği yok, aşırı karakter, zaman damgası ve bilinmeyen `google.*` ayarı ağdan önce `TraconException` alır — gerçek: `gemini-duo` iş akışı (`Kore`+`Puck`, 4 satır, 191 karakter): `Completed`, **658.916 bayt**, **13,6 sn**, 4 duraklama; `dialogue by google … (Estimated), model gemini-3.8-flash-tts`. Ret: `GoogleSpeechDialogueTests` (tel-öncesi, `handler.Requests` boş), mutasyonla ölçüldü
- [x] Kiracı anahtarı olan kiracının isteği o anahtarla gider; anahtarsız kiracı kurulum anahtarıyla; iki kiracı birbirinin istemcisini kullanmaz (tel düzeyinde test) — `GeminiSpeechTests` (`ScriptedGeminiSpeechServer`, `x-goog-api-key` okunur): bağlı kiracı tek ses, diyalog, akış ve `speak` ucunda kendi anahtarıyla; 30 paralel çağrıda `acme`/`globex`/anahtarsız kiracı her biri kendi anahtarıyla
- [x] `GET /api/voice/providers` her synthesizer için `dialogue` alanını verir (ElevenLabs ve Gemini dolu, OpenAI `null`); OpenAPI ve iki istemci yeniden üretildi — canlı: ElevenLabs `maxVoices 10`, Gemini `2/2000/false`, `tone` `null`. OpenAI satırı örnek uygulamada anahtarsız kayıtlı değil; `null` değeri `NamedSpeechProviderTests` ile kanıtlı (köprülenmiş sağlayıcı diyalog arayüzünü uygulamaz). `docs/openapi/tracon.json`, `packages/tracon-client/src/schema.ts`, `TraconApiClient.g.cs` yenilendi
- [x] ElevenLabs ortak kurala geçti; diyalog testleri değişmeden yeşil — `SpeechDialogueCapabilities.Check`; `Tracon.Voice.UnitTests` 211/211 (mevcut diyalog testlerine dokunulmadı); kuralın kendi testi `SpeechDialogueCapabilitiesTests`
- [x] Akışlı yol ölçümü ve `MimeType` ölçümü belgeye yazıldı — aşağıda "Ölçümler"
- [x] `samples/Tracon.Api` Google anahtarıyla `UseGoogleSpeech` kaydeder; gerçek tek ses ve diyalog çıktısı alındı (komut + boyut + süre belgeye yazıldı) — ilk iki madde; ayrıca fiyat (`Tracon:Pricing:Voice:google:gemini-3.8-flash-tts:PerMillionCharacters=100` → 66 karakter `cost 0,0066`), politika (`gemini` takma adı `403`, `google` `200`) ve yanlış konuşmacı biçimi (`gemini-2.5-flash-preview-tts` + `PartMetadata` → `HTTP 400 (INVALID_ARGUMENT)`, ret metni `TextLabels` önerir) canlı koşuldu; günlükte `Npgsql` satırı 0
- [x] Site sayfaları, `src/Tracon.Google/README.md` ve `CHANGELOG.md [Unreleased]` güncel — yeni sayfa `guides/gemini-speech`; `voice`, `model-providers`, `governance`, `packages`, `capabilities`; `npm run check` yeşil
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban efe9d389` — 27.820 test, 39 modül, `dotnet format` temiz, site `npm run check` yeşil (tavan 59.000 B, en ağır sayfa `guides/voice` 58.963 B)

## Ölçümler (2026-10-25, gerçek Gemini, `Google.GenAI` 1.26.1)

| Ölçüm | Sonuç |
|---|---|
| `MimeType` | `gemini-3.8-flash-tts`, `-flash-lite-tts`: `audio/wav` (RIFF; sample'dan sonra `C2PA` parçası, `data` boyutu dosyadan küçük). `gemini-2.5-flash-preview-tts`: `audio/L16;codec=pcm;rate=24000`. `gemini-3.1-flash-tts-preview`: `audio/l16; rate=24000; channels=1`. Akışta üç modelde de ham PCM. Örnekler 16 bit little-endian |
| Akış (`streamGenerateContent?alt=sse`) | `gemini-3.8-flash-tts`: 196 olay, 1.920–5.760 bayt, ilk parça ~1,2 sn. `3.8-flash-lite-tts`: 133 parça, ilk 0,4 sn. `3.1-flash-tts-preview`: 235 parça, ilk 0,8 sn. `2.5-flash-preview-tts`: **tek parça**, ilk (ve tek) 6,8 sn. `SynthesizeStreamingAsync` modelin verdiğini parça parça iletir, ham PCM |
| Yanıt alanları | `modelVersion` (örn. `gemini-3.8-flash-tts`) → `SpeechAudio.ModelId`; `responseId` → `ProviderRequestId`. SDK: `GenerateContentResponse.ModelVersion`, `.ResponseId`, `.UsageMetadata` |
| `UsageMetadata` | Token verir: 9 metin → 65 ses token'ı (~2,6 sn; yaklaşık 25 ses token'ı/sn). Karakter yok → `CharactersBilled` tahmin (Açık Soru 2 kapalı kaldı) |
| Konuşmacı biçimi | `3.8-*`: `Part.speechMetadata.speaker` zorunlu, satır biçimi `400`. `2.5`, `3.1`: satır biçimi çalışır, `speechMetadata` `400` ("Speech metadata is not supported for this model"). İkisi de tam 2 `speakerVoiceConfig` ister |
| Diğer | Dil `tr` ve `tr-TR` kabul, `xx` `400`. Ses vermeden `3.8` çalışır, `2.5` `400`. `Kore`, `Puck`, `Charon`, `Aoede`, `Fenrir`, `Zephyr` `3.8`de geçerli |

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| İstek gövdesi (`responseModalities`, `speechConfig`, konuşmacı eşlemesi) doğru kurulur | Birim (sahte HTTP işleyicisi, `Google.GenAI` istemcisi) | `GoogleSpeechSynthesizerTests` |
| PCM → WAV başlığı ve örnekleme hızı | Birim | `GoogleSpeechSynthesizerTests` |
| Ağdan önceki retler (K-955) ve ret metninin değeri yankılamaması (K-954) | Birim | `GoogleSpeechSynthesizerTests` |
| Takma ad enjeksiyonu reddedilir | Birim | `GoogleSpeechSynthesizerTests` |
| Adlı kayıt, egress politikası (`google`) ve çözümleyici | Fonksiyonel (gerçek host) | `GeminiSpeechTests` · `GoogleSpeechBuilderExtensionsTests` |
| Kiracı anahtarı yolu, iki kiracı | Fonksiyonel (tel düzeyi, sahte Gemini ucu) | `GeminiSpeechTests` (`ScriptedGeminiSpeechServer`) |
| Descriptor'da `dialogue` alanı | Fonksiyonel (HTTP) | `NamedSpeechProviderTests` · `GeminiSpeechTests` |
| ElevenLabs ortak kurala geçince davranışı değişmez | Birim (mevcut) | `ElevenLabsSpeechClientTests` |
| Sağlayıcı hatası normalleşir, anahtar sızmaz | Birim + fonksiyonel | `GoogleSpeechSynthesizerTests` · `GeminiSpeechTests` (anahtar yanıtta ve günlükte yok) |

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

1. **Diyalog konuşmacı biçimi iki** (kullanıcı kararı, K-1092). Plan tek biçim yazmıştı (`Speaker1: …` satırları). Canlı ölçüm: `gemini-3.8-*` bu biçimi `400` ile reddeder ve her parçada `speechMetadata.speaker` ister; `2.5`/`3.1` tersini yapar. `GoogleSpeechOptions.DialogueSpeakerMode` (`PartMetadata` varsayılan, `TextLabels`) eklendi; yanlış seçimde ret metni diğer modu adlandırır. Takma ad enjeksiyonu yalnız `TextLabels`'ta anlamlıdır (metadata biçiminde konuşmacı metinde değil).
2. **Diyalog tam iki ses ister** (plan yalnız `MaxVoices = 2` demişti): Gemini bir ya da üç `speakerVoiceConfig`'i `400` reddeder; tek sesli diyalog ağdan önce reddedilir ve `SynthesizeAsync` önerilir.
3. **WAV, modelin dosyası olarak döner.** Plan "RIFF başlığı kodda" demişti; `3.8` zaten WAV verir ve `C2PA` içerik kimliği parçası ekler. `wav` isteğinde bu dosya olduğu gibi döner (yeniden sarmak sağlayıcının imzasını düşürürdü); ham PCM veren modellerde başlık kodda yazılır. `pcm` isteği örnekleri verir, medya türü `audio/L16;rate=…;channels=…`.
4. **Akış hep ham PCM**; `wav` isteği akışta reddedilir (başlık uzunluk ister). Akışta `OutputFormat` boşsa `pcm`, tek seste `wav`.
5. **`GoogleModelProvider` somut tip olarak da kayıtlı** (`UseGoogle`) ve kimlik önbelleği `ModelProviderCore<T>.GetTenantFactory` ile paylaşıldı (paylaşılan kaynak; dört pakette derlenir). `GoogleChatClientFactory.Client` `internal` açıldı. Plan "ikinci önbellek yazılmaz" demişti: yazılmadı.
6. **`SpeechDialogueCapabilities.Check`'e üçüncü, isteğe bağlı parametre** (`characterLimitSetting`): ElevenLabs'in ret metni ayar adını (`VoiceOptions.MaxDialogueCharactersPerRequest`) taşıyordu ve testler onu sabitler; ortak kural adı parametre olarak alır.
7. **Public yüzey**: `Tracon.Google` üç yeni public tip (`GoogleSpeechOptions`, `GoogleDialogueSpeakerMode`, `GoogleSpeechBuilderExtensions`); `public-surface-baseline.txt` Google 4 → 7. `SpeechDialogueCapabilities.SupportsSegmentTimestamps` XML metni `cref` yerine `<c>` kullandı (OpenAPI açıklaması `bool …` yazıyordu).
8. **Varsayılan sınırlar** planda yoktu: `MaxCharactersPerRequest` 5.000, `MaxDialogueCharactersPerRequest` 2.000 (ElevenLabs ile aynı); Google karakter değil token sınırlar, bu yüzden Tracon'un kendi koruması.
9. **Site**: ses kılavuzu ağırlık tavanında (58,9 KB / 59 KB) olduğundan Gemini bölümü yeni sayfaya çıktı (`guides/gemini-speech`). Yan etki: yeni kenar çubuğu girişi her sayfaya 21–27 B gzip ekler; `concepts/workflows` 3 B altındaydı ve 23 B aştı. Tavan **yükseltilmedi** (denetim 🔴): `concepts/workflows.md` 191 bayt sıkılaştırıldı. `llms.txt` 24.575 / 24.576 B idi (1 bayt boşluk): yeni sayfa satırı için bütçe 24 → 25 KiB (`build-agent-map.mjs`, ölçüm dosyada). Kök `README.md` public tip sayısı 619 → 622.
10. **Kademe `hafif` → `tam`** (denetim 🔴): kiracı secret'ını alan kod kümesi büyüdü; başlıkta gerekçesi.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **K-1090** — ortak diyalog kuralı public yardımcı *(public-api; alternatif: `internal` + `InternalsVisibleTo`)*
- **K-1091** — `SpeechProviderDescriptor.Dialogue` *(public-api; alternatif: ayrı `SupportsDialogue` bool)*
- **K-1092** — `DialogueSpeakerMode`, iki konuşmacı biçimi *(public-api, kullanıcı kararı; alternatif: model adı önekinden seçim, yalnız `3.8` biçimi)*
- **K-1093** — Gemini sesi kiracının kendi anahtarıyla *(güvenlik, bakımcı kararı; alternatif: `SupportsTenantCredentials = false`, ikinci önbellek)*

Kategori dışı (kapanış commit mesajı): `llms.txt` bütçesi 24 → 25 KiB (ölçüm `build-agent-map.mjs` başında), WAV'ın modelin dosyası olarak dönmesi, akışın ham PCM olması, `UseAsDefault` ile `speak` tool'unun `wav` ayarı.

## Tüketici Yüzeyi Envanteri

1. **`docs-site/`** (elle): yeni `guides/gemini-speech.md` + `sidebar.mjs`; `guides/voice.md` (sağlayıcı listesi `dialogue`, bir bağlantı), `guides/model-providers.md`, `concepts/governance.md`, `packages.md`, `capabilities.md`. Üretilen: `http-api/` (OpenAPI), `api/` (DocFX), `llms*.txt`, `Tracon.AgentMap.md`.
2. **Sevk edilen metin**: `src/Tracon.Google/README.md`; `UseGoogleSpeech` ve `SpeechDialogueCapabilities.Check` `<example>`; `CHANGELOG.md [Unreleased]`; `docs/openapi/tracon.json`, `packages/tracon-client/src/schema.ts`, `TraconApiClient.g.cs`.
3. **Yerel referans / agent haritası**: `capabilities.md` iki satırı (adlı konuşma sağlayıcıları, diyalog); harita bütçesi içinde (`11260 / 11264` B).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 9 sapma (biri kullanıcı kararı: iki konuşmacı biçimi); kademe `hafif` → `tam` düzeltmesi denetimden |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (kapanış kapısı ve tam fonksiyonel koşum yeşil) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı (2026-10-25, taze bağlamlı `faz-denetcisi`); 🟢 `docs/ADAYLAR.md`'ye gitti.

- 🔴 **Ağırlık tavanı yükseltilmişti** (59.000 → 59.500). Gerçek: tavan "yalnız düşer" cırcırıdır. Düzeltme: tavan 59.000'de kaldı, `concepts/workflows.md` 191 bayt sıkılaştırıldı (yeni kenar çubuğu girişi sayfayı 23 bayt aşırmıştı). `llms.txt` bütçesi (24 → 25 KiB) denetçice ölçümle ve emsalle kabul edildi.
- 🔴 **Kademe `hafif` yazılmıştı**, aynı faz `kategori: güvenlik` karar (K-1093) kaydediyordu. Gerçek (etiket): kademe `tam` yapıldı, gerekçesi başlıkta.
- 🟡 `UseAsDefault` + `speak` tool'u: tool `Tracon:Voice:OutputFormat` (varsayılan `mp3_44100_128`) gönderir, Gemini reddeder. Kapandı: ret metni ayarı adlandırır; XML, site ve README `wav` ayarını ve konuşma soketi sınırını yazar; iki fonksiyonel test (`speak` tool'u `wav` ile çalışır, varsayılanla ağdan önce reddedilir).
- 🟡 Örnek koşumundan kalan SQLite dosyaları silindi (izlenmiyorlardı, commit'e girmedi).
- 🟡 Akış iptali test edilmemişti. Kapandı: iki birim test (akışın ortasında iptal `OperationCanceledException`, bağlantı kopması `TraconException`; ilk parça iletilmiş); iptal sınıflandırması mutasyonla ölçüldü.
- 🟡 `capabilities.md` diyalog satırı Gemini için zaman aralığı vaat ediyordu. Kapandı: "where the provider reports it".
- 🟢 F-691 (`MinVoices`), F-692 (`UseGoogleSpeech` erken doğrulama), F-693 (konuşma soketi ham PCM). README'deki sıra kayması bu fazda düzeltildi.
