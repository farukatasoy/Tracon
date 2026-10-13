# Faz 203 — Çok Konuşmacılı Diyalog Sentezi ve Sağlayıcı İstek Ayarları

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-B, 5-B, 6-B; Açık Soru 7 → B). Faz 196 ile paralel geliştirilir
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-296**
> **Genellik:** Ses sözleşmesi Tracon'un kendi sözleşmesidir ve iki büyük sağlayıcının (ElevenLabs Text to Dialogue, Gemini çok konuşmacılı TTS) sunduğu yeteneği ifade edemez; MEAI 10.9.0 `ITextToSpeechClient` de tek ses taşır. İkinci kanıt bu ekosistem boşluğudur, tüketici değil. Bölme, birleştirme ve ses işleme tüketici kodunda kalır
> **Karşı görüş:** "Ses üretimi çekirdek iş değil; tüketici SDK'yı fonksiyon düğümünde çağırabilir; Tracon'un katkısı kiracı kapısı ve maliyet kaydıdır." Plan bunu kabul eder: faz yalnız sözleşmeyi, yerleşik uygulamayı ve istek ayarlarını taşır; medya yardımcıları çıkarıldı; faz Faz 198'in kiracı kapısından sonra gelir
> **Önkoşul:** Yok. **Sıra:** [Faz 198](198-ADLI-KONUSMA-SAGLAYICISI.md)'den sonra (2026-10-13, yeniden tartıldı): yetenek o fazın kiracı kapısının arkasına eklenir. F-297 (Gemini) bu sözleşmeyi uygular
> **Paketler:** `Tracon.Abstractions`, `Tracon.Voice`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-13): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `guides/voice.md`, `reference/configuration.md` (§ Voice), `capabilities.md` · sevk edilen: yeni tiplerin XML `<example>`'ı, `src/Tracon.Voice/README.md`, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/19-COK-MODLULUK-VE-SES.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 842cd28e:docs/arsiv/fazlar/203-COK-KONUSMACILI-DIYALOG-SENTEZI.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'un ses sözleşmesi istek başına tek ses taşır. İki konuşmacılı bir podcast bugün ya her repliği ayrı seslendirip birleştirir (dinleme testinde "okuyor gibi" bulundu) ya da sağlayıcının diyalog API'sini kendi koduyla çağırır.

## Bitiş Ölçütleri (DoD)

- [x] `ISpeechDialogueSynthesizer` ElevenLabs'te `text-to-dialogue` ve `/with-timestamps` çağırır; segmentler tura eşlenir (test 1, 2, 6) — `ElevenLabsDialogueRequestTests` (9), `ElevenLabsDialogueResponseTests` (22); canlı case 1
- [x] Sınır ve ayar ihlali ağdan önce `TraconException` (Sapma 4, K-955); ret değeri yankılamaz (test 3, 4) — `SpeechDialogueValidationTests` (11), `SpeechProviderSettingsTests` (23), `ModelProviderSettingsContract`
- [x] Tek ses yolu `voice_settings` ve süreklilik alanlarını gönderir; mevcut istekler değişmez (test 5) — `ElevenLabsSpeechRequestTests` (5): ayarsız gövde birebir aynı
- [x] Yetenek kaydı tüketicinin kaydını ezmez; yeteneksiz sentezleyici açık hata verir (test 10) — kayıt yok (Sapma 1); `VoiceDialogueRegistrationTests` (10), `SpeechDialogueTenantTests` (14)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 9088e92e`, üç faz (196, 202, 203) `main`'e birleştirildikten sonra `c2592ccd` üzerinde tek koşum, çıkış 0; sevk edilen sözleşme değiştiği için `yayin --kuru --surum 1.0.0-preview.4` ilk koşumda çıkış 0 (§ *Kapanış kapısı ve yayın provası*)
- [x] `samples/Tracon.Api` ile gerçek diyalog `run`'ı (case 1), çıktı belgeye yazıldı — § Doğrulama komutları; 👤 dinleme bakımcıya kalır
- [x] `secret` taraması boş döndü — `kapi.py tarama`: "✅ temiz (17 işaretli sentetik credential atlandı)"
- [x] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-MM-132…137` (altısı koşuldu), `MT-PROV-032…034` güncellendi ve koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok, 3 × 🟡 kapandı (§ Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (build + content + links + weight) temiz; `CHANGELOG.md` yeni tipleri anar

### Kapanış kapısı ve yayın provası (`main`, 2026-10-13)

Faz 196, 202 ve 203 ayrı worktree dallarında paralel geliştirildi ve `main`'e birleştirildi (`bc3c7d11` → 203, `fac7fbd6` → 196, `c2592ccd` → 202). Uygulama dalları `kapanis` ve `yayin` koşmadı (orkestratör kararı, makine paylaşılıyordu); ikisi birleşik `main`'de bir kez koştu. Fazın tabanı `9088e92e`'dir.

| Kapı | Commit | Sonuç |
|---|---|---|
| `python3 scripts/kapi.py kapanis --taban 9088e92e` | `c2592ccd` | ✅ çıkış 0 — `tarama` · `--denetle` · script testleri · agent map · denetim paketi · `dotnet build` 89 sn · tam `dotnet test` 506 sn · `dotnet pack` · `dotnet format --verify-no-changes` 114 sn · `npm run check` (1078 sayfa, kırık bağlantı yok, SEO 0 hata). Performans kapısı tetiklenmedi: diff üç sıcak yola ve `bench/`'e dokunmaz |
| `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` | `c2592ccd` | ✅ ilk koşumda çıkış 0 — kırıcı liste 3 tip, 0 TFM düşüşü, 3 paket (hepsi `Unreleased` notunda) · 20 paket · npm dry-run · altı paketlenmiş örnek (`FileRunStore` 106, `CustomModelProvider` 38 + 1 atlandı, `CustomRunJudge` 11, `CustomAgentSource` 15, `CustomTool` 18, `CustomJobHandler` 10) · Native AOT smoke · net8.0 tüketici smoke |

Önceki prova çıktısı (Faz 197/198, `dcc1a88f`) silinmedi: `artifacts/package/yayin/1.0.0-preview.4-onceki-dcc1a88f`.

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

**Aşama 2 ölçümü (denetim 🟡 2):** yalnız `[pause]` taşıyan tur canlıda
`400 input_text_empty` döndü ("All inputs must include non-empty text after
removing speaker tags"). Sağlayıcı sessiz turu kabul etmez; bu yüzden segmenti
olmayan tur bozuk rapordur.

**`MT-PROV-032…034`** (örnek uygulama, `POST /api/agents/validate`):

```text
MT-PROV-032 200 {"valid":false,...,"code":"invalid_setting","message":"'anthropic.thinking.budgetTokens' must be greater than zero.","path":"model.providerSettings"}
MT-PROV-033 200 {"valid":false,...,"message":"'google.thinking.budgetTokens' must be in the [-1, 65535] range (-1 leaves it to the model, 0 turns thinking off).",...}
MT-PROV-034 200 {"valid":false,...,"message":"'google.safety.harassment' has a value outside its set. Valid values: HARM_BLOCK_THRESHOLD_UNSPECIFIED, BLOCK_LOW_AND_ABOVE, BLOCK_MEDIUM_AND_ABOVE, BLOCK_ONLY_HIGH, BLOCK_NONE, OFF.",...}
```

**Kapılar (dalda; `kapanis` orkestratörde):**

| Kapı | Sonuç |
|---|---|
| `kapi.py ic-dongu` (🟡 düzeltmeleri commit'lenmeden) | `exit 0`; build 0 uyarı; Core 3717 × 3 TFM, Generators 313, Sql.Shared, Voice 195 yeşil |
| Etkilenen projeler, adla | Voice 195, Core 3717 (net8/9/10), OpenAI 195, Azure 137, Anthropic 154, Google 131, Contracts.Xunit 14, AspNetCore.Functional 1781 — hepsi `failed: 0` |
| `kapi.py tarama` | `Tarama: ✅ temiz (17 işaretli sentetik credential atlandı)` |
| Sevk edilen metin (`ShippedDocumentationSelfContainmentTests`, `CapabilityExampleTests`, `SourceLanguageTests`) | 6/6 |
| `Tracon.Package.Tests` `*LocalReferenceTests*` | 10/10 |
| `node docs-site/scripts/build-agent-map.mjs --check` | `Agent map: up to date and within budget.` |
| `npm run check` (docs-site) | build ✅; `Content: 56 manual pages and 1070 total pages passed.`; `Links: 177276 internal reference(s) across 1070 pages and llms.txt, none broken.`; `SEO: 1070 HTML, ... 0 hata.`; `Weight: 1070 pages under 59000 B gzip.` |

---

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
8. **Segment eşlemesi segment sayısını tur sayısıyla karşılaştırmaz; turu,
   sesi, sırayı ve kapsamı karşılaştırır.** OpenAPI bir segmenti bir girişin
   "parçası" diye tanımlar; bir tura birden çok segment düşebilir. Her segment
   aralıktaki bir turu ve o turun sesini adlandırmalı, sonlu ve `start ≤ end`
   olmalıdır; tur indeksi geri gitmez, aynı turda bir parça öncekinden önce
   başlamaz; her tur en az bir segment alır (ölçüldü: sağlayıcı konuşulacak
   metni olmayan turu `400` ile reddeder). Biri tutmazsa `Segments = null`, ses
   korunur. Aşama 1'de yalnız segment içi aralık denetleniyordu; denetim 🟡 2.
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
12. **`ElevenLabsProviderNames` planın taslağından geniştir.** Plan
    `SettingsPrefix` ve `SupportedSettings`'i sayıyordu. Gerçekleşen yüz:
    `ElevenLabs` sağlayıcı adı sabiti (emsal `AnthropicProviderNames.Anthropic`,
    `GoogleProviderNames.Google`: egress politikası bu adı yazar), dokuz `*Setting`
    sabiti (emsal `GoogleProviderNames.SafetyHarassmentSetting` …: tüketici anahtarı
    elle yazmaz) ve `DialogueSupportedSettings` (diyalogun kabul ettiği alt küme;
    Adım 0 ölçümünün sonucu, Sapma 5). İç `VoiceProviderNames.ElevenLabs` artık
    public sabite bağlıdır.
13. **Faz `Tracon.Anthropic`, `Tracon.Google` ve `Tracon.Testing.Contracts.Xunit`
    paketlerine de dokundu** (Sapma 9). Plan yalnız `Tracon.Abstractions` ve
    `Tracon.Voice`'u sayıyordu.
14. **`MT-PROV-032…034` güncellendi** (Sapma 9'un manuel izi; denetim 🟡 1):
    beklenen metin yeni mesajdır ve her case "mesaj reddedilen değeri içermez"
    satırını taşır; üçü örnek uygulamada koşuldu.

## Bu Fazda Verilen Kararlar

Kategoriye girenler `docs/KARARLAR.md`'dedir: **K-952** (diyalog yetenek arayüzü
ve çözümleyici yolu, kullanıcı kararı), **K-953** (önekli ayar sözlüğü, kullanıcı
kararı), **K-954** (ret değeri yankılamaz, üç sağlayıcı ve sözleşme paketi),
**K-955** (ağdan önceki ret `TraconException`), **K-956**
(`SpeechAudio.ProviderRequestId`), **K-957** (yerleşik istemci kiracı kimliğini
reddeder).

Kategori dışı yerel tercihler:

- `VoiceOptions.MaxDialogueCharactersPerRequest` varsayılanı 2.000: OpenAPI
  "güvenilir üretim için ≤ 2.000 karakter" der; tüketici 1.900 kullanıyor.
- `MaxVoices = 10` sabittir (OpenAPI: en çok 10 ayrı ses); yapılandırılmaz.
- Diyalog doğrulaması `ElevenLabsSpeechClient.CheckDialogue`'dadır (`internal`);
  ikinci uygulayıcı (F-297) gelince ortak yardımcıya taşımak o fazın kararıdır.
- Örnek uygulamanın düğümü ham `pcm_*`'i 44 baytlık WAV başlığıyla sarar; Tracon
  ses birleştirmez (Açık Soru 3-A).
- Seed `long` okunur ve 0–4294967295 denetlenir (`ReadInt64`, `internal`).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 (onayda Açık Soru 7 → B) |
| Düzeltme turu sayısı | 1 (denetimin 3 × 🟡'si, tek tur) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | Merge'e giden: 0. Dal içinde 1: Aşama 1'in iki `<example>` bloğu derlenmiyordu; `ic-dongu` commit'ten sonra koşulduğu için görmedi, Aşama 2'de `ExampleCompilationTests` yakaladı (`.agents/ortak/kapilar.md` notu) |
| Faz kapandıktan sonra bulunan kusur | 0 |

## Denetim Bulguları

Bağımsız `faz-denetim` (2026-10-13): **🔴 yok, 3 × 🟡, 🟢 yok.** Üçü gerçek
bulgu olarak kabul edildi ve kapandı (`4c8047a5` ve Aşama 2 doküman commit'leri).

| # | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 🟡 1 | Sapma 9'un manuel case'leri bayat: `MT-PROV-032…034` değerin yankılanmasını bekliyordu | Gerçek | Beklenen metin yeni mesaja çevrildi, her case'e "mesaj reddedilen değeri içermez" eklendi; `00-INDEKS` 06 satırı ve dosya `Faz:` satırı; üçü örnek uygulamada koşuldu (§ Doğrulama komutları) |
| 🟡 2 | Segment eşlemesi segmentsiz turu ve sırası bozuk yanıtı kabul ediyordu | Gerçek | Önce kırmızı: üç `InlineData` (segmentsiz tur, geri giden tur, tur içinde geri giden zaman). Kural: her tur ≥ 1 segment, `(TurnIndex, Start)` azalmayan; değilse `null`. Canlı ölçüm: konuşulacak metni olmayan tur `400 input_text_empty` — segmentsiz tur meşru değildir. Sapma 8 düzeltildi |
| 🟡 3 | Plan dışı public üyeler gerekçesiz; `ElevenLabsProviderNames` örneği `null` kimlikte kırılırdı | Gerçek | Sapma 12 gerekçeyi ve emsali yazar; örnek `ProviderRequestId is { } id` koşulunu kullanır. Ayrıca iki `<example>` derlenmiyordu (`ExampleCompilationTests`, denetimin görmediği): ikisi de kendi kendine yeten fonksiyon düğümü kaydına çevrildi ve derlenir |
| Ek | DoD satır 2 `ArgumentException` diyordu | Gerçek | Sapma 4'e (K-955) göre hizalandı |

## Sonraki Faza Devir Notu

**Sözleşmeler.**
- Diyalog `ISpeechProviderResolver.GetDialogueSynthesizerAsync(name, ct)` ile
  alınır; yetenek DI'da yoktur (K-952). Yeni bir uygulayıcı aynı örnekte
  `ISpeechSynthesizer` ve `ISpeechDialogueSynthesizer`'ı uygular ve
  `AddSpeechSynthesizer(name, …)` ile kaydolur.
- Ret kuralları: sınır ve ayar ihlali ağdan önce `TraconException` (K-955);
  mesaj anahtarı adlandırır, değeri yazmaz (K-954); kiracı kimliği desteklenmiyorsa
  `NotSupportedException` (K-957).
- Ayarlar önekli sözlüktür; kural `ModelProviderSettings.Validate(dict, prefix,
  keys)` (K-953).

**🚨 Tuzaklar** (`docs/hafiza/ses-ve-konusma.md` § Diyalog sentezi).
- ElevenLabs diyalog servisi bilinmeyen `settings` alanını sessizce kabul eder;
  aynı `seed` aynı baytı vermez.
- Run kaydı `TraconException` olmayan istisnayı maskeler.
- `<example>` blokları derlenir (`Tracon.Generators.UnitTests`); `ic-dongu`
  commit'lenmiş değişikliği görmez.

**Devredilen işler.**
- **Faz 199** (fonksiyon düğümü maliyeti): diyalog sonucunun
  `SpeechDialogueAudio.Audio`'su raporlanır; `DialogueModelId` boşken
  `ModelId = null` → fiyat modelsiz aranır. Faz 199 dokümanının "Bu Faza
  Başlarken" listesine yazıldı.
- **F-297** (Gemini): sözleşmenin gerçek imzaları `docs/ADAYLAR.md` § F-297
  gövdesindedir; `MaxVoices = 2`. Doğrulama kuralı bugün `Tracon.Voice`'ta
  `internal`'dır.
- **F-465**: `GET /api/voice/providers` diyalog yeteneğini bildirmiyor (faz
  planlama, 🟢).
- 👤 `MT-MM-132` dinlemesi bakımcıya kalır.

**Sıradaki faz:** bakımcı seçer ([YOL-HARITASI](../../YOL-HARITASI.md)).
