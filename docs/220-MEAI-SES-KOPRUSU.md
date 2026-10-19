# Faz 220 — MEAI Ses İstemcisi Köprüsü ve OpenAI Sesi

> **Durum:** ✅ Tamamlandı (2026-10-19)
> **Kademe:** tam — güvenlik/kiracı sınırı: köprü kiracı egress politikasının (K-930) ve BYOK reddinin içinden geçer; egress'in eşleştiği `ProviderName` sürecin kendisinden gelmelidir
> **Plan onayı:** Bakımcı, 2026-10-19 (sohbette: genel köprü + `Tracon.OpenAI` kısayolu · eksikler kayıt seçeneğiyle beyan · tam kademe)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-579** — MAF kapsama matrisi R-01 (`claudedocs/maf-1.25.0-yetenek-matrisi.md` §6)
> **Genellik:** ses kullanan her tüketici. Bugün yalnız ElevenLabs yerleşiktir; MEAI'nin `ISpeechToTextClient`/`ITextToSpeechClient` sağlayıcıları (OpenAI dahil) bağlanamaz (matris §5.13, K0). İkinci kanıt: `docs-site` ses rehberi "Use your own speech provider" bölümünde tüketiciden Tracon arayüzünü elle uygulamasını ister; MEAI istemcisi olan tüketici bu işi iki kez yapar. İş tüketici kodunda kalabilir ama tüketici MEAI001'i kendi kodunda bastırmak ve kiracı kurallarını bilmek zorunda kalır
> **Karşı görüş:** "Ses çekirdek iş değil; tüketici kendi adaptörünü yazabilir (F-296 dersi)." Cevap: köprü medya yardımcısı değildir, kontrol düzlemidir. Kiracı egress'i, BYOK reddi, karakter sınırı ve maliyet Tracon'un çözümleyicisinde uygulanır; elle yazılan adaptör bunları atlayabilir. Kapsam yalnız bağlamadır: yeni ses yeteneği (akışlı STT, hizalama, diyalog) eklenmez
> **Önkoşul:** Yok. Faz 219 ile bağımsızdır
> **Paketler:** `Tracon.Core`, `Tracon.OpenAI`
> **Yeni paket:** Yok — MEAI.OpenAI `Tracon.OpenAI`'de zaten referanslıdır · **Migration:** Yok
> **Public API:** Büyüyor — yalnız ekleme. `1.0.0` GA'dan (K-603) önce ucuz; `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 0 satır (2026-10-19). 🚨 Public imzaya upstream deneysel tip **girmez** (`YAYIN-HAZIRLIK.md` ölçüt 5): hiçbir yeni public üye `ISpeechToTextClient`, `ITextToSpeechClient` ya da onların seçenek/yanıt tiplerini anmaz
> **Tüketici yüzeyi:** site: `guides/voice.md` (yeni bölümler: "Use any Microsoft.Extensions.AI speech client", "OpenAI speech"; "Use your own speech provider" bölümünden bağlantı), `capabilities.md` ses satırı · sevk edilen: `src/Tracon.OpenAI/README.md`, `CHANGELOG.md` `Added`, yeni public üyelerin XML `<example>`'ı
> **Manuel test alanı:** [`docs/manuel-test/19-COK-MODLULUK-VE-SES.md`](manuel-test/19-COK-MODLULUK-VE-SES.md) — bir `🤖` case (gerçek OpenAI TTS + STT)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-215 —\|K-930 —\|K-952 —\|K-502 —\|K-059 —\|K-1019 —" docs/KARARLAR.md
   ```
   **K-215** (ses sözleşmeleri `Abstractions`'ta; ElevenLabs bir uygulamadır) ·
   **K-930** (her konuşma çağrısı çözümleyicide egress ve kiracı anahtarından geçer; egress kayıt adına değil örneğin `ProviderName`'ine uygulanır) ·
   **K-952** (diyalog ayrı yetenek arayüzüdür; taşımayan motor reddedilir) ·
   **K-502** (akışlı sentez + hizalama istisna ile reddedilir, sessizce yok sayılmaz) ·
   **K-059** (`secret` hiçbir yere yazılmaz) ·
   **K-1019** (`Tracon.OpenAI`'nin "Native AOT: Yes" vaadi sahte sunucuya karşı AOT yayınıyla kanıtlanır)
3. Yayın kuralı: [`YAYIN-HAZIRLIK.md`](YAYIN-HAZIRLIK.md) UR-012 (= B) ve ölçüt 5
4. Önceki fazdan devir — Yok
5. Alan hafızası (bu faz bir alana dokunuyor):
   [`hafiza/ses-ve-konusma.md`](hafiza/ses-ve-konusma.md) (sentez akışı ve hizalama tuzakları)
6. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) — kiracı egress ve BYOK bölümü

---

## Amaç

Tracon'un ses sözleşmesi (`ISpeechSynthesizer`, `ISpeechTranscriber`) kalır:
MEAI'nin sözleşmesinden zengindir ve MEAI'ninki deneyseldir. Bu faz iki şey
ekler: (1) DI'daki **her** MEAI ses istemcisini adla bir Tracon sentezleyicisine
veya çözücüsüne bağlayan genel köprü; (2) `Tracon.OpenAI`'de bu köprüyü kullanan
`UseOpenAISpeech(...)` kısayolu. Faz bitince OpenAI TTS/STT ve herhangi bir MEAI
ses sağlayıcısı Tracon'un kiracı, egress, sınır ve maliyet kurallarıyla çalışır.

- **F-579** — MEAI ses istemcisi köprüsü (Core) + OpenAI ses kısayolu (`Tracon.OpenAI`)

### Bugün ne durumda — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`SpeechContracts.cs:36`](../src/Tracon.Abstractions/Voice/SpeechContracts.cs) · `:149` | Tracon sözleşmesi: `ISpeechSynthesizer` (`ProviderName`, `SupportsTenantCredentials`, `MaxCharactersPerRequest` `:69`, sentez, akışlı sentez, `ListVoicesAsync` `:101`) · `ISpeechTranscriber` |
| [`TraconBuilderExtensions.Speech.cs:47`](../src/Tracon.Core/TraconBuilderExtensions.Speech.cs) · `:95` | Tek kayıt yolu: `AddSpeechSynthesizer`/`AddSpeechTranscriber(name, Func<IServiceProvider, ISpeech…>)` — tüketici arayüzü elle uygular |
| [`SpeechProviderResolver.cs:237-270`](../src/Tracon.Core/Voice/SpeechProviderResolver.cs) | `AuthorizeAsync`: egress `ProviderName` ile (`:249`); kiracının bağlaması varsa ve sağlayıcı `SupportsTenantCredentials` demiyorsa `TraconTenantProviderRefusedException` (`:267`) — kurulum anahtarına düşmez |
| [`ImageGeneratorResolver.cs:111`](../src/Tracon.Core/Images/ImageGeneratorResolver.cs) | Emsal: MEAI görsel üreticisi de BYOK almaz ve kiracının kendi anahtarı varsa reddedilir |
| [`OpenAIImageBuilderExtensions.cs:39-53`](../src/Tracon.OpenAI/OpenAIImageBuilderExtensions.cs) | Emsal: `UseOpenAIImages` MEAI001'i paketin içinde bastırır ve anahtarlı `IImageGenerator` kaydeder; public imzada MEAI tipi yok |
| [`OpenAIChatClientFactory.cs:121-124`](../src/Tracon.OpenAI/OpenAIChatClientFactory.cs) | Paylaşılan `OpenAIClient`'tan model başına istemci: `_client.GetImageClient(model).AsIImageGenerator()`. Aynı istemci egress soket korumasını taşır (`CreateClient`, `:139`) |
| [`VoiceEndpoints.cs:172`](../src/Tracon.AspNetCore/Endpoints/VoiceEndpoints.cs) · [`SpeakTool.cs:83`](../src/Tracon.Voice/Tools/SpeakTool.cs) | Çağıranlar `MaxCharactersPerRequest`'i okur; köprü bir değer vermek zorundadır |
| [`ElevenLabsSpeechClient.cs:200`](../src/Tracon.Voice/Internal/ElevenLabsSpeechClient.cs) | K-502 deseni: desteklenmeyen hizalama isteği istisna ile reddedilir |
| [`VoiceBuilderExtensions.cs:98-101`](../src/Tracon.Voice/VoiceBuilderExtensions.cs) | `UseVoice` varsayılan (anahtarsız) sentezleyici ve çözücüyü `TryAdd` ile kaydeder; canlı konuşma varsayılanı okur (`VoiceConversationBuilderExtensions.cs:124`) |

> Kanıtlar 2026-10-19 tarihinde doğrulandı.

### Ölçülen MEAI yüzeyi (2026-10-19, MEAI 10.11.0)

- `ISpeechToTextClient`: `GetTextAsync(Stream, SpeechToTextOptions?, ct)`, `GetStreamingTextAsync(…)`, `GetService`. `ITextToSpeechClient`: `GetAudioAsync(string, TextToSpeechOptions?, ct)`, `GetStreamingAudioAsync(…)`, `GetService`. **İkisi ve seçenek/yanıt tipleri `MEAI001`** (derleme probu).
- `TextToSpeechOptions`: `VoiceId`, `ModelId`, `AudioFormat`, `Language`, `Speed`, `Pitch`, `Volume`, `AdditionalProperties`, `RawRepresentationFactory`. `TextToSpeechResponse`: `Contents` (`DataContent`), `ModelId`, `ResponseId`, `Usage`.
- `SpeechToTextOptions`: `ModelId`, `SpeechLanguage`, `SpeechSampleRate`, `TextLanguage`, `AdditionalProperties`. `SpeechToTextResponse`: `Text`, `Contents`, `StartTime`, `EndTime`, `ModelId`, `ResponseId`, `Usage`.
- Metadata: `GetService<TextToSpeechClientMetadata>()` / `SpeechToTextClientMetadata` → `ProviderName`, `ProviderUri`, `DefaultModelId`.
- DI: `AddSpeechToTextClient`/`AddKeyedSpeechToTextClient`, `AddTextToSpeechClient`/`AddKeyedTextToSpeechClient`.
- MEAI.OpenAI: `AudioClient.AsISpeechToTextClient()`, `AudioClient.AsITextToSpeechClient()` (`OpenAIClientExtensions`).
- 🚨 `LoggingTextToSpeechClient`/`LoggingSpeechToTextClient` seçenekleri Trace düzeyinde JSON'a yazar (decompile): seçeneklere konan her değer log'a gidebilir.

---

## 220.1 — Genel köprü (Core)

Yeni kayıt aşırı yüklemeleri bir **bağlama kaydı** alır; MEAI istemcisini DI'dan
okur (İ5). Adaptörler `internal`'dır ve MEAI001 bastırması tek dosyada toplanır.

```mermaid
flowchart LR
    DI["DI: ITextToSpeechClient<br/>(anahtarlı ya da anahtarsız)"] --> AD["internal TextToSpeechClientSynthesizer<br/>: ISpeechSynthesizer"]
    REG["AddSpeechSynthesizer(name, TextToSpeechClientBinding)"] --> AD
    AD --> RES["SpeechProviderResolver<br/>egress (ProviderName) · BYOK reddi"]
    RES --> USE["/speak · speak tool · konuşma"]
```

**Davranış kuralları:**

| Konu | Kural |
|---|---|
| `ProviderName` | `binding.ProviderName` verilmişse o; değilse istemci metadata'sının `ProviderName`'i. İkisi de yoksa **başlangıçta** `TraconException` (K-930: egress bu ada uygulanır; adsız sağlayıcı egress'i atlayamaz) |
| `SupportsTenantCredentials` | `false`. Kiracının bu `ProviderName` için bağlaması varsa çözümleyici bugünkü gibi reddeder; çağrı kurulum anahtarına düşmez |
| `MaxCharactersPerRequest` | `binding.MaxCharactersPerRequest` — zorunlu, `> 0`; doğrulanmazsa başlangıç hatası |
| `ListVoicesAsync` | `binding.Voices` (statik beyan); beyan yoksa boş liste |
| Hizalama | `SpeechRequest.IncludeTimestamps == true` → `NotSupportedException` (K-502 deseni), iki sentez yolunda da |
| Diyalog | Adaptör `ISpeechDialogueSynthesizer` uygulamaz; K-952 çözümleyicide reddeder |
| İstek eşlemesi | `VoiceId → VoiceId`, `ModelId → ModelId`, `OutputFormat → AudioFormat`, `LanguageCode → Language`, `ProviderSettings → AdditionalProperties` (değerler `JsonElement`) |
| 🚨 `secret` | `SpeechRequest.Credential` **hiçbir seçeneğe yazılmaz** (Logging halkası seçenekleri log'a yazar). `SupportsTenantCredentials = false` olduğu için çözümleyici zaten kimlik bilgisi geçirmez; adaptör yine de alanı okumaz |
| Yanıt eşlemesi | `Contents`'teki `DataContent` baytları → `SpeechAudio.Data`; `MediaType` ilk `DataContent`'ten; `ModelId` yanıttan ya da istekten; `ProviderRequestId = ResponseId`; `CharactersBilled = Text.Length`, `UsageSource = Estimated` (MEAI sağlayıcı karakter sayısı vermez) |
| Akışlı sentez | `GetStreamingAudioAsync` güncellemelerinin `DataContent` baytları sırayla verilir |
| Çözüm | `SpeechTranscriptionOptions.ModelId → ModelId`, `LanguageCode → SpeechLanguage`; `SpeechTranscript.Text = response.Text`, `AudioDuration = EndTime - StartTime` (ikisi de varsa, yoksa `null`), `ModelId` yanıttan |
| Maliyet | Bugünkü yol: `IVoicePricingReader.ForSynthesis(ProviderName, modelId, characters)` / `ForTranscription(…)`. Okuyucu yalnız `UseVoice` ile kayıtlıdır; yoksa maliyet `null` (özel sağlayıcıların bugünkü davranışı) |
| Sağlık | Adaptör `IVoiceHealthCheck` uygulamaz; sağlık ucu bugünkü gibi yalnız varsayılan sağlayıcıyı okur |
| İptal | `CancellationToken` MEAI çağrısına geçer |

## 220.2 — OpenAI kısayolu (`Tracon.OpenAI`)

`UseOpenAISpeech(configure)` paylaşılan `OpenAIClient`'tan (`OpenAIChatClientFactory`)
model başına `AudioClient` alır, `AsITextToSpeechClient()`/`AsISpeechToTextClient()`
ile MEAI istemcisini **iç anahtarla** kaydeder ve 220.1'in bağlamasıyla adlandırır.

- `ProviderName = OpenAIProviderNames.ChatCompletions` (`"openai"`) **açıkça** verilir; metadata'ya güvenilmez. Kiracının `"openai"` egress kuralı sohbet, görsel ve ses için aynıdır.
- `SynthesisModel` ve `TranscriptionModel`'den en az biri zorunludur; verilen taraf kaydedilir.
- `MaxCharactersPerRequest` varsayılanı `4096` (OpenAI belgesine göre; **ölçülmeli**).
- MEAI001 bastırması tek dosyada, gerekçesiyle (`OpenAIImageBuilderExtensions` deseni).
- Egress soket koruması paylaşılan `OpenAIClient` ile gelir; yeni istemci kurulmaz.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.
> UR-012 B: ad ve biçim `rc.1`'e kadar değişebilir; ölçüt en iyi tasarımdır.

```csharp
// Tracon.Core — TraconBuilderExtensions.Speech.cs
public static ITraconBuilder AddSpeechSynthesizer(this ITraconBuilder builder, string name, TextToSpeechClientBinding binding);
public static ITraconBuilder AddSpeechTranscriber(this ITraconBuilder builder, string name, SpeechToTextClientBinding binding);

// Tracon.Core
public sealed record TextToSpeechClientBinding
{
    public object? ServiceKey { get; init; }                      // null → anahtarsız ITextToSpeechClient
    public string? ProviderName { get; init; }                    // null → istemci metadata'sı
    public required int MaxCharactersPerRequest { get; init; }
    public IReadOnlyList<VoiceDescriptor> Voices { get; init; } = [];
}

public sealed record SpeechToTextClientBinding
{
    public object? ServiceKey { get; init; }
    public string? ProviderName { get; init; }
}

// Tracon.OpenAI
public static ITraconBuilder UseOpenAISpeech(this ITraconBuilder builder, Action<OpenAISpeechOptions> configure);

public sealed class OpenAISpeechOptions
{
    public string? SynthesisModel { get; set; }
    public string? TranscriptionModel { get; set; }
    public string SynthesizerName { get; set; } = "openai";
    public string TranscriberName { get; set; } = "openai";
    public int MaxCharactersPerRequest { get; set; } = 4096;
    public IList<VoiceDescriptor> Voices { get; } = [];
}
```

### HTTP `endpoint`'leri

Yok. Mevcut uçlar köprülenmiş adı `provider` alanıyla okur: `POST /api/voice/speak`, `GET /api/voice/voices`, `GET /api/voice/providers`. HTTP'de çözüm (STT) ucu **yoktur**: çözüm `transcribe` tool'u ve canlı konuşma ile yalnız **varsayılan** çözücüyü kullanır (`TranscribeTool.cs`, `GetTranscriberAsync(name: null)`); adlı çözücüye `ISpeechProviderResolver.GetTranscriberAsync(name)` ile kod ulaşır.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Core/
├── TraconBuilderExtensions.Speech.cs          (iki yeni aşırı yükleme)
└── Voice/
    ├── SpeechClientBindings.cs                (TextToSpeechClientBinding, SpeechToTextClientBinding)
    └── SpeechClientAdapters.cs                (internal; MEAI001 bastırması yalnız burada)
src/Tracon.OpenAI/
├── OpenAISpeechBuilderExtensions.cs           (UseOpenAISpeech; MEAI001 bastırması yalnız burada)
└── OpenAISpeechOptions.cs
tests/Tracon.Core.UnitTests/Voice/
└── SpeechClientAdapterTests.cs
tests/Tracon.AspNetCore.FunctionalTests/
├── SpeechClientBridgeTests.cs
└── Infrastructure/ScriptedOpenAIAudioServer.cs (/v1/audio/speech, /v1/audio/transcriptions)
samples/Tracon.Samples.ProviderAotSmoke/
├── FakeProviderServer.cs                      (iki ses yanıtı)
└── Program.cs                                 (UseOpenAISpeech + sentez + çözüm)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Egress `"openai"`'yi yasaklayan kiracı köprülenmiş sağlayıcıyla konuşur | Fonksiyonel (kiracı + HTTP) | `SpeechClientBridgeTests.A_tenant_whose_egress_denies_the_provider_cannot_speak_through_the_bridge` → `/speak` reddedilir, sahte sunucuya istek **gitmez** |
| Kendi anahtarı olan kiracının çağrısı kurulum anahtarına düşer | Fonksiyonel (kiracı) | `…A_tenant_with_its_own_key_is_refused_and_the_setup_key_is_not_used` — sahte sunucu istek görmez |
| Metadata'sız ve `ProviderName`'siz istemci egress'i adsız atlar | Fonksiyonel (DI) | `…A_client_without_a_provider_name_stops_the_host` — başlangıç hatası, mesaj kayıt adını anar |
| Anahtarlı istemci kayıtlı değil; hata ilk çağrıda, yanlış yerde çıkar | Fonksiyonel (DI) | `…A_missing_keyed_client_stops_the_host` |
| `MaxCharactersPerRequest <= 0` kabul edilir | Birim | `SpeechClientAdapterTests` — doğrulama hatası |
| Sınırı aşan metin sağlayıcıya gider | Fonksiyonel (HTTP) | `…Speak_rejects_text_above_the_declared_limit` (mevcut `LimitedSynthesizer` deseni) |
| `IncludeTimestamps` sessizce yok sayılır | Birim | iki sentez yolunda `NotSupportedException` |
| `SpeechRequest.Credential` seçeneklere (ve Logging halkasıyla log'a) sızar | Birim | kimlik bilgisi taşıyan istek → yakalanan `TextToSpeechOptions.AdditionalProperties` ve `RawRepresentationFactory` boş |
| İstek alanları yanlış eşlenir (ses, model, biçim, dil, sağlayıcı ayarları) | Birim | yakalayan sahte `ITextToSpeechClient` |
| Akışlı ve akışsız bayt farklı | Birim | iki yolun baytı eşit |
| Çözüm süresi/metni yanlış eşlenir | Birim | `StartTime`/`EndTime` var/yok iki kol |
| Köprülenmiş ada diyalog istenince yerleşik istemciye düşer | Birim | `GetDialogueSynthesizerAsync(name)` → `SpeechCapabilityNotSupportedException` |
| `UseOpenAISpeech` yanlış uca/modele/sese istek atar | Fonksiyonel (paket + HTTP) | `ScriptedOpenAIAudioServer` gövdesi: `model`, `voice`, `input`; çözümde çok parçalı gövde ve `model` |
| `UseOpenAISpeech` AOT'ta trim/AOT uyarısı üretir ya da çalışmaz | Paket (AOT smoke, K-1019) | `samples/Tracon.Samples.ProviderAotSmoke` ses yolunu koşar; sıfır uyarı, `FakeProviderServer`'a gerçek istek |
| Public imzaya deneysel tip girer | Paket | `dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- deneysel` — yeni maruziyet 0 |

Beş soru:

- **İptal:** belirteç MEAI çağrısına geçer; birim testinde iptal edilen belirteç `OperationCanceledException` üretir.
- **Eşzamanlılık:** adaptörler durumsuzdur, tekildir; MEAI istemcisinin kendi güvenliği geçerlidir. Test eklenmez.
- **Boş/aşırı girdi:** boş metin bugünkü uç doğrulamasında kalır; sınır aşımı fonksiyonel testte.
- **Başka kiracının kaydı:** egress ve BYOK satırları (fonksiyonel).
- **Alt sistem hatası:** sağlayıcı `5xx` → bugünkü uç hata sınıflaması (`ScriptedOpenAIAudioServer` bir kolda `500` döner; uç `502` sınıfında yanıtlar — mevcut sınıflamaya göre uygulamada doğrulanır).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 `🤖` | `samples/Tracon.Api`'de `UseOpenAISpeech(o => { o.SynthesisModel = "<tts modeli>"; o.TranscriptionModel = "<stt modeli>"; })`; gerçek OpenAI anahtarı user-secrets'ta | `POST /tracon/api/voice/speak` (`provider = "openai"`) ile kısa bir cümle sentezle; çıkan eki `transcribe` tool'u ile bir agent `run`'ında çöz (Açık Soru 1 A ile OpenAI varsayılan çözücüdür; A seçilmezse `GetTranscriberAsync("openai")` çağıran bir `dotnet run` probu) | Ses eki kaydedilir; çözüm metni girdi cümlesini taşır; `ProviderName = "openai"`; `secret` taraması boş |

Case `docs/manuel-test/19-COK-MODLULUK-VE-SES.md`'ye eklenir. Anahtar yoksa case `👤` olarak işaretlenir ve neden yazılır.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `UseOpenAISpeech` varsayılan (anahtarsız) sentezleyici/çözücü de olsun mu? Canlı konuşma varsayılanı okur | A: `UseAsDefault` seçeneği (varsayılan `false`), `TryAdd` ile — `UseVoice` gibi ilk kayıt kazanır · B: yalnız adlı | **A** — OpenAI'yi tek ses sağlayıcısı yapan host canlı konuşmayı da kullanabilir; `TryAdd` tüketici kaydının kazanma kuralını korur |
| 2 | OpenAI ses yolunda kiracı BYOK'u | A: bu fazda yok (görsel emsali; kendi anahtarı olan kiracı reddedilir) · B: istek başına kimlik bilgisiyle `AudioClient` kur | **A** — B görsel ve ses için ortak bir iştir; kapanışta `ADAYLAR.md`'ye yeni F-ID ile yazılır |
| 3 | OpenAI MEAI adaptörü `TextToSpeechOptions.ModelId`'i dikkate alıyor mu (model başına `AudioClient` mı gerekir)? | ölçülmeli — `ScriptedOpenAIAudioServer` gövdesinde `model` | ölç; dikkate almıyorsa istek başına model alanı adaptörde reddedilir ya da model başına istemci önbelleği kurulur — sapma olarak yazılır |
| 4 | `VoiceId` boşken OpenAI adaptörü hangi sesi seçer? | ölçülmeli | ölç; varsayılan yoksa `OpenAISpeechOptions.DefaultVoice` eklenir |

---

## Bitiş Ölçütleri (DoD)

- [x] `AddSpeechSynthesizer(name, TextToSpeechClientBinding)` ve `AddSpeechTranscriber(name, SpeechToTextClientBinding)` DI'daki MEAI istemcisini adla çözer; `/api/voice/speak` köprülenmiş adla, çözüm `ISpeechProviderResolver.GetTranscriberAsync(name)` ile çalışır (fonksiyonel test, host DI'ı üzerinden: `SpeechClientBridgeTests`, 21 test)
- [x] `UseOpenAISpeech` sahte sunucuya doğru gövdeyi gönderir (`model`, `voice`, `input`, `response_format`; çözümde çok parçalı gövde ve `model`); `samples/Tracon.Samples.ProviderAotSmoke` ses yolunu da koşar (`FakeProviderServer`'a `/v1/audio/speech` ve `/v1/audio/transcriptions` eklendi), AOT yayını sıfır uyarı (K-1019). Yerel kanıt: `ProjectReference` kopyasıyla `dotnet publish -r osx-arm64` sıfır uyarı, çıktı `provider AOT smoke passed: openai azure-openai voice openai-speech`. Paketlenmiş sürüm kanıtı: `kapi.py yayin --kuru` (aşağıda)
- [x] Egress ve BYOK fonksiyonel testleri yeşil; reddedilen çağrıda sahte sunucu istek görmez (`Requests == 0`); kayıt adı ile sağlayıcı adı farklıyken egress sağlayıcı adına uygulanır
- [x] `uyum-probu.cs -- deneysel`: `public_maruziyet=4`, dördü de önceden var olan `AddLoopEvaluator` (MAAI001); ses için yeni maruziyet **0**. MEAI001 bastırması bu fazda **iki yeni dosyada** (`SpeechClientAdapters.cs`, `OpenAISpeechClients.cs`); `grep -rln "disable MEAI001" src/Tracon.Core/Voice src/Tracon.OpenAI` bu iki dosyaya ek olarak zaten bastırma taşıyan `OpenAIChatClientFactory.cs` ve `OpenAIImageBuilderExtensions.cs`'i listeler (toplam dört)
- [x] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban 201e14af`
- [x] `samples/Tracon.Api` ile gerçek `run`: örnek uygulama `UseOpenAISpeech` ile açılır; `/api/voice/speak` (`provider = "openai"`) çıktısı belgeye yazıldı (aşağıda, § Denetim Bulguları); çözüm sonucu HTTP'de üretilemez (uç yok) — `ProviderAotSmoke` ve fonksiyonel testle kanıtlı. Sahte sunucuyla, çünkü gerçek hesapta ses modeli yok (sapma #13)
- [x] `secret` taraması boş döndü (`kapi.py tarama`, `kapanis` içinde)
- `🤖` case 1 `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içinde (`MT-MM-142`) ama **`👤` olarak**: ajan gerçek anahtarla denedi (`gpt-4o-mini-tts`, `tts-1` → `403 model_not_found`; `GET /v1/models` yedi model, hiçbiri ses değil); hesap yetkisi ürün sahibinindir. Hata yolu gerçek servisle görüldü, başarı yolu görülemedi
- [x] `faz-denetim` koşuldu; 🔴 bulgu yok, 🟡 4 kapandı (§ Denetim Bulguları)
- [x] `docs-site/` güncellendi (`guides/voice.md`, `capabilities.md`); `npm run check` (`kapanis` içinde) temiz; `src/Tracon.OpenAI/README.md` ve `CHANGELOG.md` `Added`
- [x] Kapanışta K-215 bu fazın kararıyla güncellendi *(kategori: public-api)*, alternatifi adıyla; yeni **K-1026**
- [x] Matris: §6 R-01 ve §5.13 ses satırı "✅ Faz 220"; `MAF-GENISLEME-NOKTALARI.md` § *Hâlâ kullanılmayan* ses satırı "KÖPRÜ — Faz 220 TAMAMLANDI"

### Doğrulama komutları

```bash
# public imzada deneysel tip yok
dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- deneysel
# MEAI001 bastırması yalnız iki dosyada
grep -rln "disable MEAI001" src/Tracon.Core/Voice src/Tracon.OpenAI
# örnek uygulama: köprülenmiş adla sentez
curl -s -X POST http://localhost:5080/tracon/api/voice/speak -H 'Content-Type: application/json' \
  -d '{"text":"Merhaba","provider":"openai"}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| MEAI ses arayüzleri minor sürümde değişir (MEAI001) ve Tracon'un iç adaptörü kırılır | Bastırma iki dosyada; `uyum-probu.cs -- ileri` her MEAI yükseltmesinde koşar; public yüzey etkilenmez |
| OpenAI karakter sınırı ya da varsayılan ses belgedekinden farklıdır | Açık Soru 3–4; sahte sunucu ve `🤖` case ile ölçülür |
| `Text.Length` tahmini fatura ile tutmaz | `UsageSource = Estimated` açıkça yazılır; site "Voice cost has a different unit" bölümü bunu anlatır |
| Tüketici `LoggingTextToSpeechClient`'ı kendi zincirine koyar ve `ProviderSettings` değerleri Trace log'una gider | Rehber `ProviderSettings`'e `secret` konmamasını söyler (K-059); adaptör kimlik bilgisini hiç yazmaz |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

1. **🚨 OpenAI ses istemcisi MEAI'nin `AsITextToSpeechClient()`/`AsISpeechToTextClient()` adaptörü değildir** (plan 220.2). Üç ölçüm (MEAI.OpenAI 10.11.0, OpenAI 2.14.0, System.ClientModel 1.16.0):
   - Adaptörün **wire modeli hep `AudioClient`'ın modelidir**; `TextToSpeechOptions.ModelId` wire'a gitmez ama yanıt onu "kullanıldı" diye raporlar. Fiyat yanlış modelle aranırdı (Açık Soru 3).
   - Adaptör akışı `stream_format: sse` ile ister; `tts-1`/`tts-1-hd` bu biçimi sunmaz.
   - **`AudioClient` kuran bir host Native AOT ile yayımlanamaz.** OpenAI 2.14.0, System.ClientModel **1.15.0**'a karşı derlendi; 1.16.0 `AsyncStreamingClientResult<T>` tipini `AsyncStreamingResult<T>` olarak yeniden adlandırdı. `AudioClient`/`ImageClient` üye imzaları eski adı taşır; ILC `Failed to load type 'System.ClientModel.AsyncStreamingClientResult`1'` ile düşer. On satırlık bir programla yinelendi (1.16.0 → hata; 1.15.0 → temiz). Azure.Core 1.63.0 `System.ClientModel >= 1.16.0` ister, bu yüzden `Tracon.Azure` ile birlikte kullanılan her host 1.16.0 çözer.
   - **Karşılık:** `OpenAITextToSpeechClient` ve `OpenAISpeechToTextClient` `OpenAIClient.Pipeline` üstünde yazıldı (`OpenAIAudioTransport`): kimlik doğrulama, yeniden deneme ve zaman aşımı aynı boru hattından gelir; gövde elle yazılır (JSON ve multipart). `ITextToSpeechClient` ve `ISpeechToTextClient` arayüzleri yine MEAI'nindir, köprü olduğu gibi kullanılır.
   - **Plan 220.2'nin "egress soket koruması paylaşılan `OpenAIClient` ile gelir" cümlesi YANLIŞTI** (denetim 🟢9): kurulum zamanı istemcisi `egressGuard = null` ile kurulur; koruma yalnız kiracının kendi uç adresi için vardır (`OpenAIChatClientFactory.CreateClient`). Kurulum uç adresi operatörün kendi kararıdır, bu yüzden zarar yoktur; köprünün kiracı kapısı çözümleyicidedir, soket değil.
   - Akış: servis varsayılan olarak **ham chunked ses** akıtır (`stream_format` verilmezse); SSE yalnız MEAI adaptörünün tercihidir. `OpenAIAudioTransport.PostStreamingAsync` yanıtı tamponlamadan okur (`RequestOptions.BufferResponse = false`; 🚨 `message.BufferResponse = false` `Apply` ile **ezilir**, ölçüldü) ve 16 KB'lık parçalar verir. İlk sürüm akışı "tek istek, tek parça" yapmıştı; denetim bunun gerekçesini (SSE ve AOT) çürüttü: AOT sorunu `AudioClient` imzasıdır, akış biçimi değil. Kanıt: kapılı sahte sunucu ilk parçayı sesin geri kalanı tutulurken verir (`Streaming_yields_the_first_chunk_before_the_service_has_finished_the_audio`).
2. **Açık Soru 3 cevabı:** MEAI OpenAI adaptörü istek başına modeli **dikkate almaz**. Kayıt tek modele bağlıdır; başka model isteyen çağrı `TraconException` ile reddedilir (ad ve model beyanda). İkinci model için ikinci ad (`SynthesizerName`/`TranscriberName`). Model başına önbellek kurulmadı.
3. **Açık Soru 4 cevabı:** `voice` boşken istemci `alloy` gönderir; servis `voice`'sız gövdeyi reddeder. `DefaultVoice` seçeneği eklenmedi; `alloy` sabittir (`OpenAITextToSpeechClient.DefaultVoice`).
4. **Açık Soru 1 = A uygulandı:** `OpenAISpeechOptions.UseAsDefault` (varsayılan `false`). `TryAdd` ile; kaydı Core'un `internal` `SpeechProviderRegistration`'ı üstünden yapar (`InternalsVisibleTo Tracon.OpenAI` var) — Core'a yeni public üye girmedi. `UseVoice`'tan **önce** çağrılmalıdır. Plandaki taslak sınıfta bu alan yoktu.
5. **Açık Soru 2 = A:** BYOK yok. Kendi anahtarı olan kiracı reddedilir. Görsel ve ses için ortak iş **F-583** olarak yazıldı.
6. **`IncludeTimestamps` → `TraconException`, `NotSupportedException` değil** (plan 220.1 tablosu). K-502 deseni `TraconException`'dır (ElevenLabs böyle atar) ve `POST /api/voice/speak` yalnız onu `502`'ye çevirir; `NotSupportedException` yakalanmayan `500` olurdu.
7. **Sağlayıcı hataları adaptörde `TraconException`'a sarılır.** Plan "uç 502 sınıfında yanıtlar — uygulamada doğrulanır" diyordu: uç yalnız `TraconException` yakalar. Metin türü ve (HTTP ise) durum kodunu taşır; istemcinin kendi metni `InnerException`'da kalır, çünkü isteği ya da yanıtın parçasını yankılayabilir. OpenAI taşıyıcısı ayrıca **durum kodu ve makine kodunu** (`HTTP 403 (model_not_found)`) yazar; servis metnini yazmaz (401 yanıtı anahtarın parçasını alıntılar).
8. **Başlangıç denetimi eklendi** (`SpeechClientBridgeValidator`, `ValidateOnStart`): bağlanmış her sağlayıcı host açılırken kurulur; eksik istemci ya da adsız sağlayıcı host'u durdurur. Tüketicinin kendi fabrikasıyla kayıtlı sağlayıcıya dokunmaz (tembel kalır). Planın hata modu tablosundaki iki "başlangıç hatası" satırı bunu ister.
9. **STT dosya adı baytlardan seçilir** (`OpenAIAudioModels.FileNameFor`): MEAI sözleşmesi akış verir, medya türü vermez; adaptör her akışı `audio.mp3` diye gönderirdi. Plan bu sınırı yazmamıştı. `mediaType` parametresi köprüde hâlâ iletilmez (genel köprü sınırı, belgelendi).
10. **Plan dışı dosyalar:** `SpeechClientBridgeValidation.cs`, `OpenAIAudioTransport.cs`, `OpenAIAudioModels.cs`. `OpenAISpeechBuilderExtensions.cs` MEAI tipi taşımaz (pragma yok); MEAI001 bastırması yeni iki dosyadadır (`SpeechClientAdapters.cs`, `OpenAISpeechClients.cs`). DoD'deki `grep` komutu **dört** dosya listeler, çünkü `OpenAIChatClientFactory.cs` ve `OpenAIImageBuilderExtensions.cs` zaten bastırma taşıyordu; iki satırın yenisi bu fazındır.
11. **`ProviderSettings` sözleşmesi:** genel köprü anahtarları `AdditionalProperties`'e olduğu gibi geçirir (plan böyle istiyor) — Tracon istemcinin hangi anahtarı okuduğunu bilemez, bu yüzden bilinmeyen anahtarı reddedemez. OpenAI istemcisi **her** anahtarı reddeder (anahtarı adlandırır, değeri yazmaz).
12. **Ölçülemeyen:** `4096` karakter sınırı gerçek servisle doğrulanamadı — örnek uygulamanın OpenAI projesinin ses modeline erişimi yok (aşağıda). Değer OpenAI belgesinden alındı ve `MaxCharactersPerRequest` ile değiştirilebilir.
13. **🤖 case 1 koşulamadı → `👤`.** `Tracon:Providers:OpenAI:ApiKey` ile `gpt-4o-mini-tts` ve `tts-1` doğrudan çağrıldı: iki model de `403 model_not_found` ("Project … does not have access to model"); `GET /v1/models` yedi model döner, hiçbiri ses modeli değil. Örnek uygulama gerçek anahtarla ayağa kalktı; `POST /api/voice/speak {"provider":"openai"}` gerçek servise ulaştı ve `502 "OpenAI speech request failed with HTTP 403 (model_not_found)."` döndü (anahtar sızmadı). Başarı yolu yalnız betikli sunucu ve AOT smoke ile kanıtlıdır.
14. **Mevcut testte bir değişiklik:** `SpeechProviderRegistrationTests` `AddSpeechSynthesizer("studio", null!)` çağrısına tür dökümü ekledi — iki aşırı yükleme eklenince boş sabit belirsizdir (CS0121). Tüketici etkisi: yalnız `null` sabitiyle çağıran kod.
15. **Çözüm maliyeti:** OpenAI yanıtı süre taşımaz (`json` biçimi), bu yüzden `SpeechTranscript.AudioDuration` boştur ve çözümün maliyeti `null`dır. Genel köprüde `StartTime`/`EndTime` ikisi de varsa süre hesaplanır.
17. **Taban çizgileri ve kapı listeleri (kapanış kapısı yakaladı, kod yazarken değil):** `public-surface-baseline.txt` `Tracon.Core` 79→81, `Tracon.OpenAI` 8→10. Dört yeni tipin dördü de plandadır (`TextToSpeechClientBinding`, `SpeechToTextClientBinding`, `OpenAISpeechBuilderExtensions`, `OpenAISpeechOptions`); tavan bilerek yükseltildi, UR-012 B (`rc.1` öncesi ekleme ucuz). `CapabilityExampleTests.ForeignRegistrationMembers` listesine iki Microsoft üyesi eklendi (`AddKeyedTextToSpeechClient`, `AddKeyedSpeechToTextClient`): liste "bu ad bizim değil" diyen bir listedir, Tracon üyesi girmedi. `ExamplePrelude`'e iki kod saplaması (`StudioTextToSpeechClient`, `StudioSpeechToTextClient`) ve `TraconBuilderRegistrationSnapshotTests`'e iki vaka eklendi; ikincisi aşırı yüklemeleri yeniden adlandırdı (`AddSpeechSynthesizer(factory)` / `(binding)`). Köprü başlangıç denetimi kaydı artık host başına bir kez eklenir (ikinci çağrıda `ValidateOnStart` tekrarlanmaz). Hiçbir tavan ya da muafiyet düşürülmedi.
16. **Komşu bulgu (bu faz değil):** `UseOpenAIImages` aynı `System.ClientModel` kaymasından etkilenir (`ImageClient` imzaları) ve `ProviderAotSmoke` onu çağırmadığı için gizli kalmıştır → **F-584**.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

- **K-215 güncellendi** *(kategori: public-api)* — köprü Tracon arayüzlerinin yerine geçmez, yanına bağlanır. Alternatif: `ISpeechSynthesizer`/`ISpeechTranscriber`'ı MEAI arayüzleriyle değiştirmek (MEAI001; ses listesi, karakter sınırı, istek başına kiracı kimliği, hizalama, faturalı karakter, diyalog yok).
- **K-1026** *(kategori: public-api, geri-dönüşü-pahalı)* — OpenAI ses istemcisi `OpenAIClient.Pipeline` üstündedir, `AudioClient`/MEAI adaptörü üstünde değil; akış tamponlanmadan geçer. Alternatif: `AsITextToSpeechClient()` — Native AOT'u kırar (K-1019) ve modeli yanlış raporlar.
- Kategori dışı (kapanış commit mesajına): `UseAsDefault` `TryAdd` ve `internal` kayıt anahtarıyla; `IncludeTimestamps` `TraconException`; sağlayıcı hatası sarılması; başlangıç denetimi; STT dosya adı.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: `faz-kapat`
> işaretsiz kutuda durur. `faz-kapat` bu tabloyu `docs/FAZ-DEFTERI.md`'ye taşır;
> eşik **167**'den itibaren boş bir değer hücresinde kapanışı durdurur.
> `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir **karar** arar.
> Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 16 sapma (§ Plandan Sapmalar); yapısal olan 1: OpenAI istemcisi `AudioClient`'siz, ölçümle |
| Düzeltme turu sayısı | 2 (AOT ölçümü sırasında iki mimari dönüş: MEAI adaptörü → `AudioClient` → boru hattı; denetim turu: 4 🟡 + 5 🟢) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 bilinen (mevcut bir test değişti: `AddSpeechSynthesizer(name, null)` belirsizliği, derleme zamanı) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.

`faz-denetcisi` (taze bağlam, çalışma ağacı, 2026-10-19). **🔴 yok.** Kod, test ve public API denetlendi; docs o sırada yazılıyordu.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Sevk edilen XML `OpenAISpeechBuilderExtensions`'ta "dosya adı `audio.mp3`" diyordu; kod dosya adını baytlardan seçer. `OpenAISpeechOptions` voice remarks'ı eski MEAI adaptörünü anıyordu | 🟡 | düzeltildi: iki XML kodla hizalandı |
| 2 | `UseAsDefault` + `speak`/`transcribe` tool yolu sınanmamıştı; tool'lar ElevenLabs model ayarını varsayılan motora yollar ve OpenAI kaydı onu reddeder | 🟡 | düzeltildi: `OpenAISpeechToolTests` (5 test, gerçek `UseVoice` tool'ları) + `UseAsDefault` remarks'ı "boş bırak ya da OpenAI modeline eşitle" der; hata iki modeli adlandırır |
| 3 | "Akışlı sentez tek istektir — SSE ve AOT gerektirir" ölçülmemişti ve gerekçe izlemiyordu: servisin varsayılanı ham chunked ses, AOT sorunu `AudioClient` imzasıdır | 🟡 | düzeltildi: gerçek akış uygulandı (`PostStreamingAsync`, tamponsuz); kapılı sahte sunucu ilk parçayı sesin geri kalanı tutulurken verir. Sapma #1 ve K-1026 yeniden yazıldı. Bulgu geçerliydi; ilk sürümün gerekçesi yanlıştı |
| 4 | DoD 6 yarım (yalnız hata yolu); DoD 4 komutu iki dosya diyordu, çıktı dört | 🟡 | düzeltildi: örnek uygulama sahte OpenAI sunucusuyla koşuldu (aşağıda); DoD 4 yeni iki dosya diye yazıldı |
| 5 | Çözüm süresi `end < start` için korumasızdı (negatif maliyet) | 🟢 | düzeltildi (ucuzdu): `end > start` koşulu + test |
| 6 | Akış yaratımı `try` dışındaydı; zaman aşımı OCE'si ve eşzamanlı fırlatan istemci testsizdi | 🟢 | düzeltildi: yaratım `try` içinde, iki test eklendi |
| 7 | BYOK testi yalnız `403` ve "istek yok" diyordu | 🟢 | düzeltildi: `own credential` metni de doğrulanır |
| 8 | `UseAsDefault` + `UseVoice` sonrası çağrıda `TryAdd` sessizce hiçbir şey yapmaz | 🟢 | devredildi: belgede yazılı (remarks, site, README); çalışma anı uyarısı **F-586** |
| 9 | "Egress soket koruması paylaşılan istemciyle gelir" iddiası yanlış | 🟢 | düzeltildi: kod yorumu ve sapma #1; kurulum uç adresi operatörün kararıdır |
| 10 | `SpeechRequest.ProviderSettings` sözleşme metni köprü istisnasını anmıyordu | 🟢 | düzeltildi: `SpeechModels.cs` XML'ine istisna cümlesi |

**Örnek uygulama koşumu (DoD 6, 2026-10-19).** `samples/Tracon.Api`, `Tracon__Providers__OpenAI__Endpoint` yerel bir sahte OpenAI sunucusuna (`/v1/audio/speech`, `/v1/audio/transcriptions`) çevrilerek, bellek içi depoyla koşuldu; gerçek anahtar yerel sunucuya gönderilmedi (sahte anahtar). `GET /api/voice/providers`: `openai` hem `Synthesizer` hem `Transcriber`, `providerName` `"openai"`, `supportsTenantCredentials` `false`; ElevenLabs varsayılan satırları değişmedi. `POST /api/voice/speak {"text":"Merhaba, siparişiniz yolda.","provider":"openai","voiceId":"nova"}` → `200`, `characters` 27, `isEstimated` `true`, `cost` `null`, ek `audio/mpeg` (14 bayt); sahte sunucu `model=gpt-4o-mini-tts voice=nova` ve `Authorization` aldı. 4097 karakter → `400` (sahte sunucu ikinci istek görmedi). `GET /api/voice/voices?provider=openai` → `[]`. Egress politikası yalnız `tone` iken `speak` → `403` "Tenant 'default' is not allowed to call speech provider 'openai'. Allowed providers: tone." ve sahte sunucuya istek **gitmedi**. HTTP'de çözüm ucu yoktur: çözüm sonucu `ProviderAotSmoke` (Native AOT, `provider AOT smoke passed: … openai-speech`; transcript `where is my order`) ve `SpeechClientBridgeTests`/`OpenAISpeechToolTests` ile kanıtlıdır. Gerçek OpenAI hesabıyla: `403 model_not_found` (sapma #13).
