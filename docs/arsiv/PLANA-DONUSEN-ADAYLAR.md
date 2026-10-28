# Plana Dönüşen Adayların Gövdeleri

> **Kapanmış kayıt.** Baştan sona okunmaz — yalnız `grep`'lenir.
>
> Bir aday kalem faza dönüştüğünde [`../ADAYLAR.md`](../ADAYLAR.md)'den
> silinir; kalemin **tam gövdesi** buraya taşınır. 2026-09-24'ten itibaren
> aday dosyasında yönlendirici satır da kalmaz — iz faz dokümanının
> `> **Kaynak:** … F-NN` satırıdır.
> Gerekçe: aday dosyası 80 KB bütçesindedir ve her oturumda okunur; plana
> dönüşmüş bir kalemin gerekçesi artık **fazın kendi dokümanındadır**.
>
> **Bir kalemin bugünkü doğrusu burada değildir.** Gövdeler taşındıkları
> tarihte doğruydu; `dosya:satır` kanıtları o günün deposuna aittir. Güncel
> plan için fazın dokümanına bak.

| Nerede ne yaşar | |
|---|---|
| Kalemin uygulanabilir planı | `docs/NN-*.md` |
| Kalemin izi | Faz dokümanının `> **Kaynak:**` satırı — `grep -rn "F-NNN" docs/` |
| Kalemin aday gövdesi (bu dosya) | taşındığı tarihle |
| Turun tam kaydı | [`../kesif/`](../kesif/) |

---

## 2026-10-26 — F-420 → Faz 253

Taşındığı gün `docs/ADAYLAR.md`'den alınan gövde. Güncel plan: Faz 253. Aynı gün gövdesiz tablo satırları da plana dönüştü: F-503 + F-505 → Faz 251, F-606 + F-607 → Faz 252, F-689 + F-690 → Faz 254. Seçim ölçümü: § *Faz planlama* tablosunun 39 kaleminin 12'si talep ya da kullanıcı kararı bekliyordu; bugün yanlış çalıştığı ölçülen dört birim plana döndü (bakımcı kararı, 2026-10-26).

### F-420 · Önbellek yazma token'ı ve fiyatı run maliyetine girsin

**Kanal:** faz planlama · 🟢 (ölçüm kanalı değiştirebilir, bkz. Genellik)

**Sorun:** Run maliyeti yalnız önbellek **okuma** token'ını ve fiyatını taşır (`UsageBreakdown.cs:36`, `RunSupportTypes.cs:41`, `:148`; `ModelDescriptor` okuma fiyatı `:46`). Anthropic önbellek yazmasını ayrıca faturalar (5 dk TTL 1,25×, 1 sa 2× girdi fiyatı). Faz 204 ölçtü (SDK 12.39.0 decompile + canlı run): yazma token'ı `InputTokenCount`'a girer ve `AdditionalCounts["CacheCreationInputTokens"]`'e yazılır; `RunPricingResolver` önbelleksiz payı çıkarmayla bulduğu için yazma düz girdi fiyatıyla fiyatlanır. Genellik kuralına göre kalem fiyat eksiğidir ve planlamada kalır.

**Kapsam:** Adım 0 ölçümü; sonra `RunUsage` ve `RunCost`/`RunTreeCost`'a yazma terimi, `ModelDescriptor`'a TTL başına yazma fiyatı (K-032), `Total()` dört terim (K-483), üç sağlayıcıda migration, kota ve webhook toplamı.

**Değer:** Önbellekli Anthropic kullanan her tüketicinin run maliyeti ve maliyet kotası doğru olur.

**Genellik:** İlk ilke: Tracon run maliyetini sözleşme olarak sunar; önbellekli her Anthropic kullanıcısı etkilenir. Tek tüketici ölçümü yalnız sinyaldir (podcast pilotu, toplamın ~%1'i). Ölçüm token'ın hiç sayılmadığını gösterirse kalem maliyet kaydı kusurudur ve kusur kanalına geçer; girdi fiyatıyla sayılıyorsa fiyat eksiğidir ve burada kalır.

**Mercek:** 2, 8.

**Hazırlık:** Okuma fiyatı deseni hazır (`CachedInputCostPerMillionTokens`); SDK 12.39.0 `Ttl.Ttl1h` taşır.

**Maliyet:** Public alanlar, migration (üç sağlayıcı), ağaç toplamı sorgusu, konsol maliyet satırı, iki istemci.

**Risk:** Toplama ifadesine yeni terim (K-483 sınıfı): kota veya webhook terimi kaçırırsa kiracı tavanı aşılır.

**Bağımlılık:** Faz 204 (`promptCaching.ttl`); ağaç ve istatistik toplamları para birimi kuralını `CurrencyTotals`'tan alır (K-976) — yeni terim oradan geçmeli.

**Ekosistem (2026-10-13):** Anthropic önbellek yazmasını TTL'e göre fiyatlar; OpenAI yazma ücreti almaz; Gemini bağlam önbelleği saklama süresine göre ücretlendirir.

**Karşı görüş:** Ölçülen pilotta pay ~%1'dir; migration'ın maliyeti değerden büyük olabilir. Ölçüm "hiç sayılmıyor" demedikçe beklemek savunulabilir.

## 2026-10-25 — F-297 → Faz 244, F-347 → Faz 245

Taşındığı gün `docs/ADAYLAR.md`'den alınan gövdeler. Güncel plan: Faz 244 ve Faz 245. Aynı gün gövdesiz tablo satırları da plana dönüştü: F-544 → Faz 241, F-411 → Faz 242, F-530 → Faz 243, F-465 → Faz 244 (F-297 ile).

### F-297 · `Tracon.Google` için Gemini konuşma (TTS) adaptörü

**Kanal:** faz planlama · 🟡

**Sorun:** `src/Tracon.Google/` konuşma yüzeyi taşımaz (2026-10-11 taraması); kullanıcı Gemini kullanıyor.

**Kapsam:** `ISpeechSynthesizer` ve Faz 203'ün `ISpeechDialogueSynthesizer` yeteneği; `SpeechConfig`/`MultiSpeakerVoiceConfig` eşlemesi; WAV/PCM çıktı; yetenek `MaxVoices = 2`.

**Değer:** ElevenLabs'in yanında ikinci sağlayıcı; workflow başına seçim gerçek olur.

**Mercek:** 3, 6, 8.

**Hazırlık:** Yeni bağımlılık yok: `Google.GenAI` 1.16.0 referanslı (`Directory.Packages.props:144`); `MultiSpeakerVoiceConfig.SpeakerVoiceConfigs`, `SpeakerVoiceConfig.Speaker`, `PrebuiltVoiceConfig.VoiceName` var (`maf-api-kesfi` dökümü, 2026-10-11).

**Maliyet:** Paket içi kayıt uzantısı; canlı doğrulama için anahtar.

**Risk:** Model adları önizleme; API anahtarı modu sınırları (F-281 deseni).

**Bağımlılık:** Faz 203 (F-296) sözleşmeyi getirdi. Faz 198 (F-298) adlı kaydı getirdi: adaptör `AddSpeechSynthesizer("gemini", …)` ile kaydolur, kiracı kapısından `ISpeechProviderResolver` ile geçer ve kiracı anahtarını `SupportsTenantCredentials` + `SpeechRequest.Credential` ile alır.

**Sözleşme (Faz 203, gerçekleşen):** aynı örnek `ISpeechSynthesizer` ve `ISpeechDialogueSynthesizer` uygular; diyalog DI'a kaydedilmez, çağıran `ISpeechProviderResolver.GetDialogueSynthesizerAsync(string? name = null, CancellationToken ct = default)` ile alır (K-952). Üyeler: `string ProviderName`, `bool SupportsTenantCredentials => false`, `SpeechDialogueCapabilities DialogueCapabilities { MaxVoices; MaxCharactersPerRequest; SupportsSegmentTimestamps }` (Gemini: `MaxVoices = 2`), `ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(SpeechDialogueRequest, CancellationToken)`. İstek: `Turns` (`SpeechDialogueTurn { Text, VoiceId }`), `ModelId?`, `OutputFormat?`, `LanguageCode?`, `IncludeTimestamps`, `ProviderSettings` (`IReadOnlyDictionary<string, JsonElement>`), `Credential?`. Sonuç: `SpeechDialogueAudio { SpeechAudio Audio; IReadOnlyList<SpeechDialogueSegment>? Segments }`, segment `{ TurnIndex, VoiceId, Start, End }`; `SpeechAudio.ModelId` ve `ProviderRequestId` doldurulur. Ret kuralları: sınır, boş tur ve ayar ihlali ağdan önce `TraconException` (K-955), ret değeri yankılamaz (K-954); ayarlar `google.*` önekiyle `ModelProviderSettings.Validate(request.ProviderSettings, "google", keys)` (K-953); kimlik desteklenmiyorsa `NotSupportedException` (K-957 deseni). Doğrulama ElevenLabs'te `ElevenLabsSpeechClient.CheckDialogue` içindedir (`internal`, `Tracon.Voice`); Gemini adaptörü `Tracon.Google`'da olursa kural paylaşılmaz — ortak bir yardımcıya taşımak F-297'nin kararıdır.

**Ekosistem (2026-10-11):** Modeller `gemini-3.8-flash-tts`, `gemini-3.8-flash-lite-tts`, `gemini-3.1-flash-tts-preview`, `gemini-2.5-pro-preview-tts`; .NET örneği yok.

**Karşı görüş:** Faz 198 olmadan adaptör ElevenLabs'in yerine geçer, yanına gelmez; tek başına değeri düşük.

### F-347 · OpenAI strict şema modu ve sağlayıcıya giden etkin şemanın görünürlüğü

**Kanal:** faz planlama · 🟡

**Sorun:** Tracon şemayı `ChatResponseFormat.ForJsonSchema` ile verir (`AgentDefinitionCompiler.ChatOptions.cs:199`). MEAI OpenAI uyarlayıcısı strict'i `AdditionalProperties` içindeki `StrictKey`'den okur (`Microsoft.Extensions.AI.OpenAI.xml:658-673`, 10.9.0); `src/` bu anahtarı hiç yazmaz (grep). Strict olmayan istekte MEAI şema dönüşümü ölçülmedi. Kiracı başka model seçebildiği için sağlayıcıya giden etkin şema görünmez.

**Kapsam:** `openai.strictJsonSchema` sağlayıcı ayarı (Abstractions'a sızmaz); `POST /api/agents/validate` yanıtında etkin şemanın özeti veya hash'i; strict dönüşümünün (alanlar zorunlu + nullable) belgelenmesi.

**Değer:** Büyük ayrık birleşim şemasında şema dışı alan ve eksik ayırıcı ilk denemede engellenir.

**Mercek:** 5, 6, 8.

**Hazırlık:** Ayar anahtarı deseni; `ModelProviderSettingsContract`.

**Maliyet:** Bir ayar anahtarı, OpenAI paketinde dekoratör, validate ucuna alan (public HTTP).

**Risk:** Strict opsiyonel alanları zorunlu kılar; tüketici şemayı nullable alanlara çevirmelidir.

**Bağımlılık:** F-340 önce kapanır.

**Ekosistem:** MEAI.OpenAI 10.9.0 XML (2026-10-11); güncel OpenAI kısıt kümesi plan aşamasında doğrulanır.

**Faz 204 devri:** kesik strict yanıt da `OutputTruncated` olur (sağlayıcıdan bağımsız). OpenAI istemcisi `ModelOutputLimit` bildirmez, bu yüzden `output_truncation_risk` OpenAI'da çıkmaz; isteniyorsa bu kalem ekler. Boolean ayar `InvalidSettingValue`'yu `null` bırakır.

**Karşı görüş:** Host doğrulayıcısı ve onarım turu strict olmadan da şemayı zorlar; değer ilk denemede uyum ve onarım maliyetidir.

## 2026-10-23 — F-322 → Faz 240

Taşındığı gün `docs/ADAYLAR.md`'den alınan gövde. Güncel plan: Faz 240. Aynı gün gövdesiz tablo satırları da plana dönüştü: F-559 → Faz 237 + Faz 238, F-529 + F-531 → Faz 239.

### F-322 · Kiracı başına workflow eşzamanlılığı, görünür kuyruk ve adil kiralama

**Kanal:** faz planlama · 🟡

**Sorun:** `MaxConcurrentRuns` süreç başına tek semafordur ve kiracılara ortaktır (`TraconWorkflowOptions.cs:27-35`, `WorkflowRunner.cs:120,404-418`). Bekleyen run'ın satırı yoktur; görünmez ve iptal edilemez. Job kiralama FIFO'dur (`PostgresQueries.cs:1165-1177`); bir kiracı diğerlerini aç bırakabilir.

**Kapsam:** Kiracı başına sınır; bekleyen run'a `Queued` satırı ve sıra; `RunTimeout` slot alınınca başlar; `tracon.workflow` işlerinde kiracıya göre sıralı kiralama.

**Değer:** Çok kiracılı SaaS'ta saatlik üretimler adil sıraya girer.

**Mercek:** 2, 3.

**Hazırlık:** Kiracı başına sınır deseni canlı ses ve skill script'te var; `RunStatus.Queued` agent yolunda var.

**Maliyet:** Runner değişikliği; adil kiralama için üç SQL lehçesinde sorgu ve sözleşme testleri.

**Risk:** Süreç içi sınır replika sayısıyla çoğalır; küme geneli sınır dağıtık sayaç ister.

**Bağımlılık:** Yok. F-325 (2026-10-12) slot bekleyen run'ın satırını beklemenin başında açar ve `Running` gösterir; görünür kuyruk o satıra `Queued` benzeri bir durum ekler.

**Ekosistem:** Taranmadı.

**Karşı görüş:** Tüketici admission katmanında kiracı başına kabul sınırı koyabilir; Tracon sınırı ikinci katman olur.

## 2026-10-22 — F-617 → Faz 235, F-616 → Faz 236

Taşındıkları gün `docs/ADAYLAR.md`'den alınan gövdeler. Güncel plan: Faz 235, Faz 236.

### F-617 · MCP elicitation: MRTR ile askıya alınan tool çağrısı

**Kanal:** faz planlama · 🟢

**Sorun:** Tracon MCP istemcisini `clientOptions: null` ile kurar (`McpConnection.cs:174`, `McpShortLivedConnection.cs:63`, `McpOAuthAuthorizationCoordinator.cs:236`); elicitation yeteneği bildirilmez, kullanıcıdan bilgi isteyen sunucunun tool'u düşer.

**Kapsam:** MCP 2026-07-28 MRTR yolu: sunucu `InputRequiredResult` döner; Tracon run'ı onay gibi askıya alır (bekleyen istek + run olayı); cevap sonraki turun girdisidir; tool çağrısı `RequestParams.InputResponses` ve `RequestState` ile yeniden gönderilir. Eski (doğrudan istek) sunucu: bilerek reddet ya da yalnız canlı yüzeyde zaman aşımlı bekle (karar). Alt agent K-103 ile reddeder.

**Değer:** Kullanıcıdan onay veya bilgi isteyen MCP sunucuları Tracon altında çalışır.

**Genellik:** MCP bir ekosistem standardıdır; elicitation spec'in çekirdek istemci yeteneğidir. Tüketici kodunda kalamaz: bağlantı Tracon'un.

**Mercek:** 3, 6.

**Hazırlık:** SDK 2.2 MRTR'yi taşır; `InputResponses`/`RequestState` public (decompile, 2026-10-21). Tracon'un onay askısı hazır.

**Maliyet:** Bekleyen istek tipi, run olayı, HTTP cevap yolu, konsol formu, TS istemcisi.

**Risk:** Bağlantı (kiracı, sunucu) başına paylaşılır; eski sunucunun doğrudan isteği çağıran run'ı bilmez (okuma). URL modu elicitation dış tarayıcı açar.

**Bağımlılık:** —

**Ekosistem (2026-10-21):** MCP spec 2026-07-28 changelog: MRTR sunucudan istemciye isteğin yerini alır; tasks uzantıya taşındı.

**Karşı görüş:** Elicitation kullanan sunucu sayısı ölçülmedi; çoğu sunucu tool parametresiyle yetinir.


### F-616 · Tracon'un AG-UI ucu

**Kanal:** faz planlama · 🟢

**Sorun:** AG-UI ön yüzü (CopilotKit gibi) Tracon agent'ına bağlanamaz. MAF'ın `MapAGUIServer`'ı Tracon altında çalışmaz: istemci tool'unun devam turunu çalışan vekil tool ve SDK'nın ürettiği onay çiftiyle kurar; K-1039 kuralı vekili reddeder (`RunChatOptionsRule.cs:66`), girdide gelen onay çifti tool'u koşturmaz (probe, 2026-10-21; [`MAF-KAPSAMA.md`](../MAF-KAPSAMA.md) §0 #37).

**Kapsam:** `AGUI.Server`'ın olay dönüştürücüsü (`AsAGUIEventStreamAsync`) üzerinde Tracon ucu; girdi eşlemesi Tracon'un kendi mekanizmalarına: istemci tool sonucu → `toolResults` (kodda kayıtlı istemci tool'u, `AddClientTool`), onay resume → `ToolApprovalResolver` (sunucudaki bekleyen istekle eşleme, denetim satırı); oturum F-614'ün izolasyonlu deposundan.

**Değer:** AG-UI ön yüzleri Tracon'un run kaydı, kiracı, onay ve maliyet sınırıyla çalışır.

**Genellik:** AG-UI bir ekosistem standardıdır; MAF onu birinci sınıf entegrasyon olarak sunar. Tüketici kodunda kalamaz: ucu yazan Tracon'un onay ve istemci tool sözleşmesini yeniden kurmak zorunda kalır.

**Mercek:** 1, 6.

**Hazırlık:** `AGUI.Server` 1.0.1 kararlı (bağımlılık yalnız MEAI.Abstractions); Tracon'un istemci tool'u ve onay çözücüsü hazır.

**Maliyet:** Yeni uç ve kayıt uzantısı; +2 paket; konsol yok.

**Risk:** AG-UI istemcisi tool'u istek başına beyan eder, Tracon kodda beyan eder (K2 sınırının genişlemesi); karar: yalnız kodda kayıtlı istemci tool'uyla kesişim mi. `RunAgentInput.State` eşlemesi açık.

**Bağımlılık:** F-614 (izolasyon sağlayıcısı ve anahtarlı oturum deposu).

**Ekosistem (2026-10-21):** MS Learn AG-UI entegrasyonu yedi özelliği (onay, ön yüz tool'u, durum) kapsar; `AGUI.Server` yayıncısı "AG-UI Protocol"dür → `nuget-danismani` tedarik zinciri onayı.

**Karşı görüş:** Tracon'un kendi gömülebilir sohbet aracı ve TS istemcisi aynı işi görür; AG-UI talebi ölçülmedi. Değer, ekosistemin AG-UI'yi standart kabul etmesine bağlıdır.

## `docs/ADAYLAR.md` durum paragrafı — Faz 255 (iki aday satırına yer açmak için)

2026-10-26 tarihli "Durum" paragrafının tam metni:

**Durum (2026-10-26):** sıralanabilir kuyrukta **5** aday var (F-324 video tüketicisi
turu; F-618…F-621 MAF Dalga 4 turu,
[`kesif/2026-10-21-maf-dalga-4.md`](../kesif/2026-10-21-maf-dalga-4.md)).
2026-10-26'da iki planlama turu oldu. Birincisi: F-688 (Faz 246), F-572 + F-508
(Faz 247), F-409 (Faz 248 numara kimliği, Faz 250 alt agent sabiti) ve F-545
(Faz 249; ikinci kanıtı F-409, bakımcı kararı). F-409'un skill yarısı F-698
olarak § *Tetik bekleyenler*'dedir. İkincisi, yanlış çalıştığı ölçülen dört birim:
F-503 + F-505 (Faz 251), F-606 + F-607 (Faz 252), F-420 (Faz 253), F-689 + F-690
(Faz 254). Aynı gün doğrudan kapandı: F-495, F-640, F-673, F-702, F-704, F-705.
RC kalite dalgası (2026-10-28) kalemlerini yerel `Q-NN` kimliğiyle
[`RC-KALITE-DALGALARI.md`](../RC-KALITE-DALGALARI.md) tutar; F-ID planlamada verilir.
Bugün yapılabilecek iş
§ *Bekleyen Kalemler*'in üç kanal tablosundadır. Planlanmış fazlar burada değil,
üretilen [`YOL-HARITASI.md`](../YOL-HARITASI.md)'dedir.
