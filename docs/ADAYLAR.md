# ADAYLAR — Planlama Kuyruğu

> **Bu dosya ne taşır:** yalnız **yapılacak** işi — faza dönüşecek adayları,
> bekleyen kalemleri, her kalemin kanalını ve aciliyetini, bekleyen kalemin
> koşulunu. Başka hiçbir şeyi.
>
> **Ne taşımaz:** yapılmış işin kaydı. Bir kalem plana dönüşünce veya
> kapanınca bu dosyadan **silinir** ve yerine yönlendirici satır yazılmaz
> (§ *Kalemin yaşam döngüsü*). Ayrıca taşımaz: faz durumunu (üretilen
> [`YOL-HARITASI.md`](YOL-HARITASI.md)) · kararları
> ([`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md)) · tur anlatılarını
> ([`kesif/`](kesif/)) · "bir daha önerilmez" listesini
> ([`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)).

**Durum (2026-10-14):** sıralanabilir kuyrukta **5** aday var (F-297, F-420
podcast turu; F-322, F-324, F-347 video tüketicisi turu). Aynı turların on kalemi
2026-10-12'de yedi faza dönüştü (Faz 196…202); 2026-10-13'te F-296 Faz 203'e,
F-320 ve F-346 Faz 204'e, F-405 Faz 196'ya girdi (planlandı); 2026-10-14'te F-418 ve F-482 Faz 205'e dönüştü, F-319 Faz 208 ve 209'a dönüştü. Bugün
yapılabilecek iş § *Bekleyen Kalemler*'in üç kanal tablosundadır. Planlanmış fazlar
burada değil, üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'dedir.

---

## Okuma Sırası

| İhtiyacın | Nereye bak |
|---|---|
| Sıradaki işi seçmek | § *Bekleyen Kalemler* — kanal tabloları, aciliyete göre sıralı |
| Sıradaki fazı seçmek | § *Sıralanabilir Adaylar* ve § *Faz planlama* tablosu |
| Hangi fazın nerede olduğu | **Buraya değil** — üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md). Planlanmış faz dokümanları `docs/` kökündedir |
| Bir kalem neden bugün iş değil | § *Tetik bekleyenler* — her satır engeli ve koşulu söyler |
| Bir F-ID nereye gitti | **Buraya değil** — `grep -rn "F-NNN" docs/`. Plana dönüşenin izi faz dokümanının `> **Kaynak:**` satırıdır; kapananınki [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) |
| Yeni aday üretmek | `aday-kesfi` skill'i; çıktısı bu dosyaya yazılır |
| Bir iş reddedildi mi, zaten var mı | [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md) |
| Geçmiş turda ne olmuştu | [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md) — tur anlatıları, taşınan gövdeler |

Bu dosya **baştan sona okunmaz.** İhtiyacın olan bölüme git.

---

## Değerlendirme Ölçütleri

| Ölçüt | Soru |
|---|---|
| **Değer** | Bu olmadan Tracon'i kim kullanamaz? |
| **Genellik** | Bu bir uygulamanın kendi sorusu mu, kontrol düzleminin eksiği mi? Hangi tüketici sınıfı etkilenir; iş tüketici kodunda kalabilir mi? |
| **Maliyet** | Kaç paket, kaç yeni public tip, kaç migration? |
| **Risk** | Bir tasarım kuralını, AOT veya bundle bütçesini zorluyor mu? |
| **Hazırlık** | MAF veya .NET ekosisteminde hazır mı, sıfırdan mı? |

Bir adayın `Mercek` satırı aşağıdaki destekleyen mercekleri numarayla sayar.

| # | Mercek | Sorusu |
|---|---|---|
| 1 | **Benimseme** | İlk agent'a kadar geçen süreyi kısaltır mı? |
| 2 | **Üretim işletimi** | Gece 03:00'te nöbetçi mühendisin işine yarar mı? |
| 3 | **Kurumsal satın alma** | Hangi kurumsal kapıyı açar? |
| 4 | **Performans ve AOT** | Sıcak yol ve tahsis bütçesi korunur mu? |
| 5 | **API ergonomisi** | Yanlış kullanım derlemede yakalanır mı? |
| 6 | **Ekosistem yerleşimi** | Aspire, OTel, MCP, A2A ve DI ile doğal mı oturur? |
| 7 | **Ölçme–iyileştirme** | Üretim verisini geliştirmeye geri besler mi? |
| 8 | **Maliyet (FinOps)** | Tüketicinin model faturasını düşürür mü? |

Her aday gövdesi şu alanları taşır: **Sorun · Kapsam · Değer · Genellik ·
Mercek · Hazırlık · Maliyet · Risk · Bağımlılık · Ekosistem · Karşı görüş.** Bir
alan ölçülmediyse öyle yazılır; boş bırakılmaz. `Genellik` 2026-10-13'te
eklendi; daha eski kalem onu plan anında doldurur.

### Tüketici geri bildirimi kuralı

Bakımcı kararı (2026-10-13). Tracon binlerce tüketicinin bağımlı olacağı bir
pakettir; bir tüketicinin isteği Tracon'un eksiğini gösteren bir **sinyaldir**,
iş emri değildir. Ölçülen sebep: Faz 192 ve 194–204'ün on ikisi aynı tüketici
projesinin iki raporundan doğdu.

- Tek bir tüketici turundan çıkan **yetenek** adayı ikinci bir kanıt olmadan
  faz planlama kanalına geçmez. İkinci kanıt başka bir tüketici, bir ekosistem
  standardı ya da ilk ilkeden türeyen bir eksiktir. Kanıt yoksa kalem
  § *Tetik bekleyenler*'e gider ya da tüketici kodunda kalır.
- Sözleşme ihlali bu kuraldan muaftır: belgelenen davranışı ya da kiracı
  sınırını bozan kusur, kim bulursa bulsun kusur kanalına girer.
- Plan onayında `Genellik` ve `Karşı görüş` açıkça cevaplanır; cevap plan
  başlığına yazılır (`faz-planlama` Adım 2).

### Kanal ve aciliyet

| Kanal | Ne zaman | Nasıl |
|---|---|---|
| **Kusur giderme** | Kod bugün yanlış davranıyor | `kusur-giderme` skill'i; faz açılmaz |
| **Faz planlama** | Yeni yetenek, public sözleşme veya geniş dokunuş | `faz-planlama` skill'i; kalem `docs/NN-*.md` olur |
| **Doğrudan** | Küçük ve kararı verilmiş iş | Tek oturumda yapılır; faz veya kusur kaydı gerekmez |
| **Tetik bekliyor** | Koşulu oluşmadı | Koşul ölçülünce kalem bir kanala taşınır |

Aciliyet: 🔴 şimdi — kurulumu bozar veya takvimi var · 🟠 bu turda · 🟡
sıradaki boşlukta · 🟢 fırsat oldukça. Her kanal tablosu aciliyete göre
sıralıdır.

### Kalemin yaşam döngüsü

- **Yeni kalem** kanalının tablosuna girer; kanalı belli değilse
  § *Tetik bekleyenler*'e. Numarayı § *F-ID tahsis kuralı* verir.
- **Plana dönüşen kalem** silinir. İzi faz dokümanının `> **Kaynak:** … F-NN`
  satırıdır; gövdesi varsa
  [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md)'ye taşınır.
- **Kapanan kalem** silinir ve
  [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Kapanan
  kalemler* tablosuna tek satırla (tarih + kanıt) taşınır. Numara orada
  yaşamaya devam eder; sayaç kapısı onu da sayar.
- **Kısmen kapanan kalem** yerinde kalır; satırı yalnız **kalan** işi anlatır.
  Bir yarısı plana dönüştüyse kalem metni o fazı `Faz NNN` diye anar.

Bu kurallar yazıyla değil **kapıyla** korunur:
`python3 scripts/dokuman-bakim.py --denetle` → *Aday dosyasının hizası*
(`adaylar_hizasi_bulgulari`). Kapı şunları kırmızı yapar: bir faz
dokümanının `> **Kaynak:**` satırındaki F-ID burada hâlâ kalemse ·
§ *Kapanan kalemler* tablosundaki F-ID burada hâlâ kalemse · gövdesi
olmayan `Gövde: § *F-NNN*` atfı · satırı olmayan aday gövdesi · aciliyet
sırası bozuk kanal tablosu · § *Sıralanabilir Adaylar*'daki gövde sayısıyla
tutmayan **Durum** satırı. Bir kalemi kapatan ya da plana çeviren oturum bu
dosyayı **aynı değişiklikte** düzeltir.

---

## Sıralanabilir Adaylar

Bir kalem buraya § *Değerlendirme Ölçütleri*'nin tam gövdesiyle girer; yeni
aday `aday-kesfi` ile üretilir. Bugünkü kalemler 2026-10-11 podcast
([keşif notu](kesif/2026-10-11-podcast-tuketici.md)) ve video tüketicisi
([keşif notu](kesif/2026-10-11-video-tuketici.md)) turlarından gelir; kanal ve aciliyet
§ *Faz planlama* tablosundadır.

### F-297 · `Tracon.Google` için Gemini konuşma (TTS) adaptörü

**Kanal:** faz planlama · 🟡

**Sorun:** `src/Tracon.Google/` konuşma yüzeyi taşımaz (2026-10-11 taraması); kullanıcı Gemini kullanıyor.

**Kapsam:** `ISpeechSynthesizer` ve Faz 203'ün `ISpeechDialogueSynthesizer` yeteneği; `SpeechConfig`/`MultiSpeakerVoiceConfig` eşlemesi; WAV/PCM çıktı; yetenek `MaxVoices = 2`.

**Değer:** ElevenLabs'in yanında ikinci sağlayıcı; workflow başına seçim gerçek olur.

**Mercek:** 3, 6, 8.

**Hazırlık:** Yeni bağımlılık yok: `Google.GenAI` 1.16.0 referanslı (`Directory.Packages.props:144`); `MultiSpeakerVoiceConfig.SpeakerVoiceConfigs`, `SpeakerVoiceConfig.Speaker`, `PrebuiltVoiceConfig.VoiceName` var (`maf-api-kesfi` dökümü, 2026-10-11).

**Maliyet:** Paket içi kayıt uzantısı; canlı doğrulama için anahtar.

**Risk:** Model adları önizleme; API anahtarı modu sınırları (F-281 deseni).

**Bağımlılık:** [Faz 203](arsiv/fazlar/203-COK-KONUSMACILI-DIYALOG-SENTEZI.md) (F-296) sözleşmeyi getirdi. Faz 198 (F-298) adlı kaydı getirdi: adaptör `AddSpeechSynthesizer("gemini", …)` ile kaydolur, kiracı kapısından `ISpeechProviderResolver` ile geçer ve kiracı anahtarını `SupportsTenantCredentials` + `SpeechRequest.Credential` ile alır.

**Sözleşme (Faz 203, gerçekleşen):** aynı örnek `ISpeechSynthesizer` ve `ISpeechDialogueSynthesizer` uygular; diyalog DI'a kaydedilmez, çağıran `ISpeechProviderResolver.GetDialogueSynthesizerAsync(string? name = null, CancellationToken ct = default)` ile alır (K-952). Üyeler: `string ProviderName`, `bool SupportsTenantCredentials => false`, `SpeechDialogueCapabilities DialogueCapabilities { MaxVoices; MaxCharactersPerRequest; SupportsSegmentTimestamps }` (Gemini: `MaxVoices = 2`), `ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(SpeechDialogueRequest, CancellationToken)`. İstek: `Turns` (`SpeechDialogueTurn { Text, VoiceId }`), `ModelId?`, `OutputFormat?`, `LanguageCode?`, `IncludeTimestamps`, `ProviderSettings` (`IReadOnlyDictionary<string, JsonElement>`), `Credential?`. Sonuç: `SpeechDialogueAudio { SpeechAudio Audio; IReadOnlyList<SpeechDialogueSegment>? Segments }`, segment `{ TurnIndex, VoiceId, Start, End }`; `SpeechAudio.ModelId` ve `ProviderRequestId` doldurulur. Ret kuralları: sınır, boş tur ve ayar ihlali ağdan önce `TraconException` (K-955), ret değeri yankılamaz (K-954); ayarlar `google.*` önekiyle `ModelProviderSettings.Validate(request.ProviderSettings, "google", keys)` (K-953); kimlik desteklenmiyorsa `NotSupportedException` (K-957 deseni). Doğrulama ElevenLabs'te `ElevenLabsSpeechClient.CheckDialogue` içindedir (`internal`, `Tracon.Voice`); Gemini adaptörü `Tracon.Google`'da olursa kural paylaşılmaz — ortak bir yardımcıya taşımak F-297'nin kararıdır.

**Ekosistem (2026-10-11):** Modeller `gemini-3.8-flash-tts`, `gemini-3.8-flash-lite-tts`, `gemini-3.1-flash-tts-preview`, `gemini-2.5-pro-preview-tts`; .NET örneği yok.

**Karşı görüş:** Faz 198 olmadan adaptör ElevenLabs'in yerine geçer, yanına gelmez; tek başına değeri düşük.

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

### F-324 · Karışık zincirde agent adımının iletim modu

**Kanal:** faz planlama · 🟢

**Sorun:** Agent adımı yanıtın bütün mesajlarını iletir (`WorkflowAgentStepExecutor.cs:33-43`); Loop'ta bu bütün taslaklar demektir (`LoopEvaluatorRegistry.cs:268-272`). Yapılandırılmış nesne kanalı yoktur. Kayıttaki tip zinciri ve ilk düğüm kuralı F-341 ile kapandı (2026-10-12, K-908: atanabilirlik, kayıt ve derleyici); bu aday yalnız iletim modunu taşır.

**Kapsam:** Agent düğümüne iletim modu (son asistan mesajı · bütün mesajlar · yapılandırılmış JSON); varsayılan bugünkü davranış. Yeni mod tip zinciri kuralına (`WorkflowDefinitionValidator.ValidateNodeTypes`) çıktı tipini bildirir.

**Değer:** Doğrulama veya TTS düğümü yalnız son, geçerli içeriği alır.

**Mercek:** 5, 6.

**Hazırlık:** Descriptor `InputType`/`OutputType` taşır.

**Maliyet:** Tanım alanı ve doğrulayıcı kuralı (public).

**Risk:** Mevcut zincirlerin davranışı korunmalı.

**Bağımlılık:** Önce podcast ham #14 ölçümü. F-341 kapandı.

**Ekosistem:** —

**Karşı görüş:** Fonksiyon düğümü son asistan mesajını kendisi seçebilir; değer tek tip davranış ve kayıtta erken hatadır.

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

---

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

## Bekleyen Kalemler

Bir kalem buradan iki yolla çıkar: iş yapılır (kalem kapanır veya plana
dönüşür) ya da kalemin artık gerekmediği ölçülür ve kalem kapatılır. İki
yolda da § *Kalemin yaşam döngüsü* uygulanır.

### Kusur giderme

Kapanan tur geçmişi (2026-10-12…14: hangi turda hangi kalemlerin kapandığı ve hangi sınıf taramalarının yeni kalem açtığı) [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § Kapanan kalemler'dedir.

| Aciliyet | Kalem | Sorun | Ne yapılır |
|---|---|---|---|
| 🟡 | **F-525** · Kapanış kapısı yeni yayımlanan güvenlik uyarısını görmez | Ölçüldü (2026-10-14, F-524): yamasız `Tracon.SqlServer`'da `dotnet build` sıfır uyarı verdi (restore no-op), `dotnet restore --force` aynı projede `NU1902`/`NU1903`'ü hata yaptı. `kapi.py kapanis` önbellekli restore'a güvenir; yeni advisory'yi yalnız şablon testi (boş dizin) ve CI'nın soğuk checkout'u görür | `kapanis`'e `dotnet list Tracon.slnx package --vulnerable --include-transitive` taraması ekle (çıkış kodu vermez: çıktıyı ayrıştır) ya da restore'u `--force-evaluate` ile koştur; süreyi ölç |
| 🟡 | **F-492** · Kuyruktaki `run`'ın checkpoint'siz kurtarma yeniden başlatması güncel tanımı derler | Faz 200 denetimi (🟡, gerekçelendi): `WorkflowRunJobHandler.Continue` ve `WorkflowJobHandler` (batch öğesi) checkpoint yoksa işlemi yeni kimlikle yeniden uygular; `run` işlemi o anki güncel tanımı derler, kesilen denemenin damgası (`runs.agent_version`) kullanılmaz. `resume`/`respond` yükü `definitionVersion` taşıdığı için etkilenmez | Zincirin kök satırı damgalıysa yeniden başlatmayı o sürüme sabitle; `Apply` yeni `run` için de sürüm kabul etsin |
| 🟢 | **F-526** · `kapi.py` sevk edilen sözleşme değişimini çözüm dışı örnek depoya bağlamıyor | Ölçüldü (2026-10-14, F-502): `RunStoreContract`'a case ekleyen değişiklik `samples/Tracon.Samples.FileRunStore`'u kırdı; çözümde olmadığı için yalnız `kapi.py yayin --kuru` gördü. `faz-tamamlama` bunu yazar, kapı hatırlatmaz | `src/Tracon.Testing.Contracts.Xunit/` dokunulduğunda `ic-dongu` ve `kapanis`, `samples/Tracon.Samples.*` uyarısının kardeşi olarak "yayın provası gerekir" bassın |
| 🟢 | **F-527** · Yeniden denemeden sonra reddedilen düğümde ilk denemenin hatası kayıttan düşer | Faz 210 denetimi (🟢): ikinci deneme bütçe kapısında reddedilirse run satırı yalnız `Could not start function node …` der; ilk denemenin geçici hatası kayıtta yok. `WorkflowNodeRetry` başarısız denemeyi bütçeden bağımsız olarak da loglamaz | Retry döngüsü her başarısız denemeyi `Warning` olarak logla; ret mesajı gerekirse ilk denemenin hata tipini anar |
| 🟢 | **F-496** · Kuyruklu workflow `respond`'unun `definitionVersion` zinciri uçtan uca sınanmıyor | Faz 200 denetimi (🟡2, kısmen): `resume` için tezgâh var (`WorkflowAsyncRunTests`); stored workflow'un insan girdisi bekleyen bir tezgâhı yok, `WorkflowEndpoints` respond satırı silinirse yalnız yük ve handler birim testleri parçalarını sınar | İnsan girdisi bekleyen stored workflow tezgâhı kur; adı konan sürüm ve olmayan sürüm (`400`, iş kuyruğa girmez) |
| 🟢 | **F-495** · Üretilen API sayfaları xunit `[Fact]` özniteliğinin mutlak kaynak yolunu taşır | Ölçüldü (2026-10-14, Faz 200): `docs-site/src/content/docs/api/Tracon.Testing.Contracts.*.md` içinde 48 sayfa `[Fact("/Users/…/src/…", 13)]` yazar (`[CallerFilePath]`); site yayınında yerel kullanıcı yolu sızar. Aynı sayfalar, adında `faz` geçen bir çalışma ağacında `SourceLanguageTests`'i kırar | `build-api-reference.mjs` öznitelik argümanlarını yazmasın |
| 🟢 | **F-485** · A2A istemcisi onay reddinin metnini görmez | Ölçüldü (2026-10-14, F-448 sınıf taraması): `ExternalAgentProxy` `TraconExternalCallException` atar, A2A host'u yanıtı `-32006` *"Agent handler did not produce any response..."* ile değiştirir; drain ve bilinmeyen agent reddi de aynı yoldan geçer. Run artık `Failed` kapanır (K-977) | Karar: hata A2A görev durumu (`failed` + mesaj) olarak mı, JSON-RPC hata metni olarak mı taşınır; önce A2A SDK'nın istisna eşlemesini ölç |
| 🟢 | **F-486** · Konsolun skill seçicisi yalnız `store` skill'lerini listeler; kod ve disk skill'i seçilemez | Kod okuması (2026-10-14, F-481 sınıf taraması): ajan düzenleyicisi `GET /api/skills`'i okur, uç `IAgentSkillStore.ListAsync`'i döner. `AddSkill` ve `SkillRoots` skill'i yalnız API'den adlandırılır | Karar: liste ucu kaynağı (`origin`) ile kod ve disk skill'ini de dönsün mü; disk taramasının liste maliyeti |
| 🟢 | **F-487** · Devre dışı `store` skill'i aynı adlı disk skill'ini katalogda gölgeler, run'da gölgelemez | Kod okuması (2026-10-14, F-481): katalog adı `store`'da bulur ve `disabled` sayar (parmak izi `:disabled`); derleme yolunda `TraconSkillsSource` devre dışı kaydı vermez, MAF dosya kaynağı aynı adlı disk skill'ini sunar | Karar: devre dışı kayıt adı diske de kapatsın mı (öncelik kuralı) |
| 🟢 | **F-390** · Sürdürme yer tutucusu kuyrukta kullanıcı ve etiket taşımaz | Kod okuması (2026-10-12, F-369): `RunReconciliationService` yer tutucu satırı `UserId`/`Labels`'sız açar; işleyici run başlayınca doldurur (test kanıtlı). Boşluk yalnız kuyrukta bekleyen ve başlamadan düşen satırdadır | Yer tutucu kaynak run'ın atfını yazar (`ApprovalEndpoints` emsali) |
| 🟢 | **F-392** · Yeniden denenen eval token toplamını eksik yazar | Kod okuması (2026-10-12, F-365 sınıf taraması): kira dolumu ya da kapanış yazımı yedeği sonrası deneme yalnız kendi koştuğu vakaların token'ını sayar (`EvalJobHandler`) | Toplam kayıtlı vaka sonuçlarından hesaplanır |
| 🟢 | **F-394** · Çevrimiçi eval özet yazımı düşerse puan özete hiç girmez | Kod okuması (2026-10-12, F-365 sınıf taraması): puanlar yazıldıktan sonra `RecordScoreAsync` (`OnlineEvalJobHandler.cs:398`) hata verirse deneme düşer; tekrar yargıcı atlar, puan çevrimiçi özete eklenmez | Özet yazımı ayrı ve tekrar edilebilir (F-348 deseni) |
| 🟢 | **F-395** · Fonksiyon düğümünün kendi model çağrısı kök akışa `ModelFallbackUsed` yazar | Kod okuması (2026-10-12, F-360): düğüm içinden yapılan model çağrısının fallback'i workflow kökünün akışına olay yazar (`FallbackChatClient.RecordFallbackUsedAsync`); `model_id` ve maliyet etkilenmez | Karar: olay yalnız atfı sahiplenen agent run'ında mı, workflow sinyali olarak mı (K-907) |
| 🟢 | **F-397** · Eval run'ları atıfsız; kök run'ın atfı tüketici bağlamına bağlı | Kod okuması (2026-10-12, F-364): eval case run'ları kullanıcı ve etiket taşımaz (`JobAttributionScopeSiteTests`'te gerekçeli). Kök run'ın atfı yalnız `IRunAttributionContext` ortam kapsamını okursa görünür; okuyucu etkin `AmbientRunAttributionScope`'u önceleyebilir (`AgentSessionManager` emsali) | Karar: eval kuyruğa alanın atfını taşır mı; okuyucu sırası (public davranış) |
| 🟢 | **F-399** · Uzlaştırıcı callable alt agent ya da skill taşıyan kök agent için devam kuyruğa koyar | Kod okuması, ölçülmedi (2026-10-12, F-344): işleyici sonra reddeder (`RunContinuationJobHandler` skill ve callable reddi), `Failed` bir yer tutucu satır kalır | `RunContinuationEligibility` bu iki reddi uzlaştırıcıda da uygular |
| 🟢 | **F-402** · Zamanlama atfı Admin'in yükte yazdığı beyandır | Karar sorusu (2026-10-12, F-364): zamanlama zarfındaki `userId`/`labels` elle yazılır (K-904); agent-run yükü de aynı alanı kabul eder. "Gövde atıf kaynağı değildir" ilkesiyle çelişir | Karar: zamanlamanın kimliği (kaydı yazan Admin · servis kimliği · beyan) |
| 🟢 | **F-403** · Eval case derlemesi altıncı elle yol; kesme işaretinin kopyası | Kod okuması (2026-10-12, F-343): `EvalJobHandler` parametreli derlemeyi elle yapar ve dekoratörleri uygular ("tek fabrika" notunun dışında); `WorkflowRequestDescriptor.cs:285` `…[truncated]` işaretini tekrarlar (`RunEventWriter.TruncationMarker` var) | `ParameterizedAgentFactory.CompileAsync`'e birleştirme; sabit kullanımı |
| 🟢 | **F-404** · Yanıt önbelleğinin depo okuma hatası "miss" metriğine yazılır | Kod okuması (2026-10-12, F-354 sınıf taraması): `TraconResponseCachingChatClient` (`:138`, `:170`) iki farklı olayı tek metrikte birleştirir; kapı etkisi yok | Ayrı `error` sonucu (metrik etiketi) |
| 🟢 | **F-316** · Akışsız run endpoint'i `LoopUnsatisfied` ve `StructuredResponseInvalid` hatasını `502` olarak döner | Faz 192 denetimi (kod okuması): akışsız yolun genel `catch`'i (`AgentEndpoints.cs:1553-1571`) her istisnayı `errorType`'sız `502` yazar; döngü ve şema reddi yukarı akış arızası değildir, istemci sebebi yalnız run kaydından okur. Akışlı yol türü `error` çerçevesinde taşır (`MT-CORE-134`) | Doğru durum kodu (ör. `422`) ve gövdede `errorType`; OpenAPI ve istemci birlikte yenilenir (public HTTP sözleşmesi) |
| 🟢 | **F-355** · Registry, sağlayıcının elle attığı ayar reddini genel bir upstream hatasına çevirir | Kod okuması (2026-10-12, F-340): üçüncü taraf sağlayıcı bir anahtarı kendi `TraconException`'ıyla reddederse `ProviderFailureNormalizer` onu maskeler; `/validate` `invalid_setting` gösterir ama anahtar adını göstermez. `ModelProviderSettingsContract` sağlayıcıyı registry'nin altında çağırdığı için farkı görmez | Sözleşmeye registry üstünden bir senaryo ya da normalizer'da ayar reddi için tip istisnası (ör. `ProviderSettingsValidationException` türetmesi) |
| 🟢 | **F-357** · Kapı: `IValidateOptions<T>` kaydı `ValidateOnStart` olmadan kalmasın | F-336 taraması aynı anda iki vaka buldu (`VoiceOptions`, `OpenAILiveOptions`); ikisi de XML'de "başlangıçta" diyordu | Kaynağı tarayan mimari test (emsal `TimeoutSiteTests`): her kayıt için `AddOptions<T>().ValidateOnStart()` veya `ProviderRegistrationCore.AddValidatedOptions` aranır. Üçüncü vakadan önce |
| 🟢 | **F-358** · Konuşma sürücüsü sentezleyicinin karakter limitini okumuyor | Kod okuması, ölçülmedi (2026-10-12, F-336): `VoiceSpeechSegmenter.MaxSegmentLength = 240`; `VoiceConversationDriver.SpeakAsync` parçayı `ISpeechSynthesizer.MaxCharactersPerRequest` kontrolü olmadan gönderir; limiti 240'tan küçük bir sentezleyici sözleşme dışı istek alır | Parça boyu `min(240, MaxCharactersPerRequest)`; fonksiyonel test |
| 🟢 | **F-359** · `UseOpenAILive` doğrulayıcısı tüketicinin kendi `ILiveVoiceProvider`'ında da OpenAI anahtarı ister | Kod okuması (2026-10-12, F-336): `ValidateOnStart` ile bu yapılandırma başlangıçta düşer; senaryo yapay | Karar: belgele veya `VoiceProviderBinding` benzeri bağlama |
| 🟢 | **F-362** · Hiçbir modeli fiyatlı olmayan karma run satırı yeniden fiyatlamada agent modeliyle fiyatlanır | Kod okuması (2026-10-12, F-349, K-893 bilinen sınırı): depo model başına usage tutmaz; fiyatlı payı olan karma satır atlanır, fiyatsız karma satır tek model satırından ayırt edilemez | Model başına döküm sütunu veya "karma" işareti (migration + public API) |
| 🟢 | **F-363** · Embedding token'ları run usage'ına ve maliyetine girmez | Kod okuması (2026-10-12, F-349): `search_knowledge` sorgu embedding'i (`VectorSearchToolFactory.cs:55`) sayılmaz; embedding üreteci fiyatlı bir sağlayıcıya bağlı değildir (K-874 kapsam dışı bıraktı). Sağlayıcı adı 2026-10-13'ten beri vardır (`TraconKnowledgeOptions.EmbeddingProvider`, K-936); fiyat kimliği yok | Embedding üretecinin sağlayıcı/fiyat kimliği gelince yan kanal payı olarak eklenir |
| 🟢 | **F-368** · Alt agent üst run'ın kültürünü almaz | Kod okuması (2026-10-12, F-351 sınıf taraması): `CallableAgentResolver` çocukları `culture: null` ile çözer; çok dilli bir üst run'ın çocuğu varsayılan talimatla koşar. `AgentRunScope` kültür taşımaz | Karar: `AgentRunScope.Culture` (eklemeli public API) ile miras ya da "çocuk varsayılan kültürle koşar" diye belgele |
| 🟢 | **F-373** · Kayıtlı `IAttachmentStorage` kalkınca retention `external_uri`'li satırı siler, bayt yetim kalır | Kod okuması (2026-10-12, F-332): depolama yoksa eski tek-ifade yolu | Satırı tut ve logla ya da koşumu düşür (karar) |
| 🟢 | **F-241** · Kırpma bir vekil (surrogate) çiftini ortadan kesebilir — sınıf | `EvaluatorRunJudge.ResolveVersion` sürümü `version[..MaxEvaluatorVersionLength]` ile kırpar; 128. karakter yüksek vekilse kodlama hatası doğar ve yazma döngüsünde tekrarlanır. Olasılık çok düşük (informational version ASCII'dir). Ayrıntı: `arsiv/ERTELENEN-ADAYLAR.md` § Kısaltılan kalem ayrıntıları | Sınıfı tek bir güvenli kırpma yardımcısına indir. Kırpılan bir alanın ASCII-dışı içerik taşıdığı ölçülürse aciliyet yükselir |
| 🟢 | **F-281** · `Tracon.Google` görsel yolunda `MediaType` verilirse SDK `NotSupportedException` atar | Canlı ölçüldü (2026-09-24): `ImageConfig.OutputMimeType` API anahtarı modunda reddedilir; yalnız `IImageGenerator`'ı doğrudan çağıranı etkiler | Davranış kararı (reddet ya da yok say), düşen test, site cümlesi. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Açık kalem gövdeleri* |
| 🟢 | **F-285** · Kırıcı kapının iki yanlış-mesaj yolu (Faz 187 denetimi) | (a) `PKV007` yanlış etiketlenir; (b) iç içe TFM listesi eşleşmez; ikisi de güvenli yön, mesaj yanlış | Gerçek `PKV007` raporunu ölç, kural sırasını düzelt. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Açık kalem gövdeleri* |
| 🟢 | **F-286** · Bütünüyle kaldırılan bir paket kırıcı kapıda hiç denetlenmez | Taban kümesi bugünkü `library_ids`'ten gelir (`scripts/kapi.py` `release_rehearsal`); tabanda olup HEAD'de olmayan paket için ne restore ne rapor olur (Faz 187 denetimi 🟢3) | Taban etiketindeki `src/*/*.csproj` kümesiyle HEAD'dekini karşılaştır; düşen paket kimliği notta code span olarak geçmeli |
| 🟢 | **F-287** · `dokuman_iddia_cakismalari` proje başına `TraconPublicApiTrackingEnabled=false` cümlesini de çelişki sayar | Faz 187 kapıyı gerçek anahtara çevirdi (`scripts/dokuman-bakim.py`); anahtar artık proje başına kapatmadır (K-424), "`Tracon.Client` `false` yazar" gibi doğru bir skill cümlesi bulgu olur. Bugün tetikleyen satır yok (denetim 🟢4) | İddia desenini varsayılana daralt ("varsayılan"/"default" ya da `src/Directory.Build.props` bağlamı) |
| 🟢 | **F-311** · Kapalı MCP oturumuna giden çağrı "TaskCanceledException" diye raporlanır | Ölçüldü (2026-10-11): kapalı oturuma giden `tools/call` `TaskCanceledException` diye raporlanır; kalan yol derleme ile çağrı arasındaki yarış | Kapalı bağlantıyı adıyla açıkla. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Açık kalem gövdeleri* |
| 🟢 | **F-428** · Model yargıcının bağlaması örnekleme denetiminden geçmez | Faz 204 denetimi 🟢3 (2026-10-13, kod okuması): `SamplingCapabilityCheck` yalnız agent tanımının bağlamasını ve yedeklerini okur. `ModelRunJudge` `Temperature`/`TopP`'yi `ModelRunJudgeOptions.Model`'den gönderir; `SupportsSampling = false` modele bağlı yargıç her örneklenen run'da `400` alır ve bunu ancak skor yazılmayınca gösterir. `CompactionSettings.SummarizationModel` örnekleme alanlarını göndermez, açık yalnız yargıçtadır | Yargıç seçeneklerinde başlangıç doğrulaması (`ValidateOnStart`) aynı yardımcıyı okur; fonksiyonel test |
| 🟢 | **F-434** · Ajan batch öğesindeki `sessionId` kayıtta kabul edilir, öğe ancak çalışınca düşer | Faz 197 Sapma 9 (2026-10-13): `BatchItemInput` iki handler'a ortaktır; zamanlama kaydı ve tetik şekli kabul eder, `AgentBatchJobHandler` öğeyi düşürür (K-928) | Kayıt ve tetik doğrulaması `sessionId`'yi handler anahtarına göre reddeder |
| 🟢 | **F-466** · İki public istisnanın tüketici kanıtı yok; yüzey envanteri kırmızı | Ölçüldü (2026-10-13): `python3 scripts/public-yuzey-envanteri.py --denetle` `exit 1` verir; `TraconChildRunTimeoutException` ve `TraconLoopUnsatisfiedException` `kanıtsız`dır: yalnız Core atar, hiçbir sayfa veya `<example>` anmaz. Betik `kapanis`'te koşmaz; GA freeze turu (UR-003) onu okur | `internal`'a çek (`errorType` yeter) ya da yakalama yolunu belgele ve `scripts/public-yuzey-gerekceleri.tsv`'ye gerekçe yaz |
| 🟢 | **F-450** · Workflow agent önbelleği süre başına girdi açar, eskisini düşürmez | Faz 196 denetimi 🟢2 (2026-10-13, kod okuması): `WorkflowAgentCache.Get` anahtarı `(kiracı, workflow, agent, timeout)` taşır (`src/Tracon.Workflows/Internal/WorkflowAgentCache.cs`); bir agent düğümünün süresini her değiştiren kayıt yeni bir `ChildAgentInvoker` girdisi açar, eskisi süreç boyunca kalır. Bugün kayıt sayısıyla sınırlı, ölçülmüş bir bellek baskısı yok | Tanım sürümü değişince aynı `(kiracı, workflow, agent)` için eski süre girdisini düşür; çok sayıda süre değişikliğinde girdi sayısını sınayan test |
| 🟢 | **F-488** · `PgVectorSearchStore` işlemi deadlock kurbanını yeniden denemez | Kod okuması (2026-10-14, F-430 sınıf taraması): kaynağın parçalarını silip yazan işlem kendi `NpgsqlDataSource`'u üzerinden açılır, `SqlStoreContext.ExecuteInTransactionAsync`'ten geçmez; aynı kaynağı eşzamanlı yazan iki yükleme `40P01` ile düşer. `SqlTransactionSiteTests` onu gerekçeli istisna sayar. Ölçülmedi | Depo `SqlStoreContext`'i alır ya da aynı sınırı (işlemin tamamı, rastgele bekleme) kendi işleminde uygular; önce eşzamanlı iki yüklemeyle kırmızı test |
| 🟢 | **F-489** · Google'ın `MALFORMED_FUNCTION_CALL` ve `UNEXPECTED_TOOL_CALL` bitişleri çağrısız `ToolCalls` olur | Kod okuması (2026-10-14, F-426 sınıf taraması; Google.GenAI 1.16.0 adaptörü decompile): ikisi `ChatFinishReason.ToolCalls`'a eşlenir, yanıtta `FunctionCallContent` yoktur. Tool döngüsü yanıtı son tur sayar; run boş ya da yarım cevapla `Completed` kapanabilir. Kalan bilinmeyen bitişler `ContentFilter`'dır (güvenli yön). Canlı ölçülmedi | Gerçek Gemini ile ölç; yanlış başarıysa `Tracon.Google` ham temsili okuyup ayrı bir sınıfa eşler (F-426'nın `TraconFinishReasons` deseni) |
| 🟢 | **F-490** · Kararlı kimliği olmayan Tracon istisnası metniyle sınıflanır | Kod okuması (2026-10-14, F-442 taraması): düz `TraconException` `StableIdentities`'te yoktur; `DefaultRunErrorClassifier` mesaj desenine bakar ve mesaj kullanıcı adları taşır. Adında `timeout`/`429` geçen ret geçici sınıf alır; düğüm yeniden denemesi yalnız `FinalFailure` kimliklerini ve kalıcı durumu reddeder | Kendi tipinde metin deseni koşmasın (`Unknown`) ya da sık retlere kararlı kimlik; önce adında `timeout` geçen ajanla kırmızı test |
| 🟢 | **F-491** · Başlamadan düşen run kaydı hatayı CLR kısa adıyla yazar; sınıfı `Unknown` olur | Kod okuması (2026-10-14, kusur turunun bağımsız incelemesi): yedi yer `Type = exception.GetType().Name` yazar (`grep -rn "GetType().Name" src`). `StableIdentities` kısa adı tanımaz: kuyruklu run'ın kiracı reddi (`TraconTenantProviderRefusedException`) ve derleme hatası `Unknown` sınıflanır; K-983'ün tek kimlik sözü bu yolda tutmaz | `TraconException.ErrorType` yazan tek yardımcı; eski kısa adlar için eşleme. Önce kuyruklu run'ın reddiyle kırmızı test |
| 🟢 | **F-483** · `HarnessDefaultsTests` işlem geneli cwd'yi değiştirir; paralel testte düşebilir | Gözlendi, tekrarlanmadı (Faz 199): tam koşumda bir kez düştü, tek başına yeşil | Açık içerik köküyle koş |
| 🟢 | **F-497** · SQLite kota sayacı tutarı ikili kayan noktayla toplar | Ölçüldü (2026-10-14, Faz 205): `quota_usage.cost` `TEXT` sütunudur; `cost = cost + excluded.cost` SQLite'ta `double` toplar (`0,1m + 0,2m` → `0,30000000000000004m`; PostgreSQL ve SQL Server `numeric`, geçti). Sapma girdi başına birikir; Faz 205 girdi sayısını artırdı. Kuruş düzeyi bozulmaz, `MaxCost` karşılaştırması 1e-17 kayar | Tutarı tamsayı alt birimde tutan sütun; `kalıcı-veri` kararı (migration), yalnız SQLite |
| 🟢 | **F-498** · Eşik denetimi her kota girdisi için iki okuma yapar; ağaç başına N+1 kez | Kod okuması (2026-10-14, Faz 205): `QuotaEnforcer.ClaimThresholdCrossingsAsync` girdi başına `ListAsync` ve (kural varsa) `GetUsageAsync` okur; önceden ağaç başına bir girdi vardı, şimdi `MaxTotalRuns + 1`'e kadar. Boş girdi yazılmaz. Ölçülmedi | Ölçülmüş yükte: alt run girdilerinin eşik denetimini ağaç kapanışında bir kez yapmak ya da kural listesini kısa süre önbelleğe almak |
| 🟢 | **F-499** · Tüm ağacı yapılandırılmışın dışında tek birimle fiyatlayan özel çözümleyicide alt run'ların token maliyeti kotaya girmez | Faz 205 sınırı (K-990): çocuk kapanırken kökün birimi bilinmez; alt run'ın token maliyeti yalnız `Tracon:Pricing:Currency` cinsindense (ya da etiketsizse) girer, kök kendi maliyetini olduğu gibi yazar. Yerleşik çözümleyici hep yapılandırılmış birimde fiyatlar; etkilenen yalnız özel `IRunPricingResolver`. Tersi de var: kök dışarıdaysa ve çocuk içerideyse sayaç iki birimi toplar | Bir tüketici bildirirse: kökün birimini ilk fiyatlanan girdiden bağlayan ağaç hesabı |
| 🟢 | **F-500** · Geç dönen tool hata verirse ya da iptal olursa daha önce raporladığı tutar hiçbir sayaca girmez | Faz 205 denetimi: `LateToolCompletionRecorder.OnSettled` hata ve iptalde `TakeLateToolAmount`'a varmadan döner. K-974 "geç başarısızlık maliyetsiz" der; tool parayı raporlayıp düşmüşse tutar gerçektir | Ölçülmüş vaka olursa: hata dalında da `TakeLateToolAmount` çağır |
| 🟢 | **F-501** · Geç tool tutarının geçirdiği eşik yalnız webhook ile duyulur; webhook yoksa kimse duymaz | Faz 205 denetimi (belgelenmiş sınır, §205.4): run akışı kapandığı için crossing atılır, claim tüketilir. `PublishThresholdToRunStream` açık, `IWebhookPublisher` yoksa bildirim görünmez | Ölçülmüş ihtiyaçta: claim etmeden önce yazılabilir akış var mı bak |
| 🟢 | **F-514** · Tek cevaplı isteği kapatılamayan satır kilitler | Faz 208 denetimi (🟢, iki tur): `WorkflowEventStream`'in `finally` kapatması try/catch'siz, depo hatası başlıktan sonra kaçar ve satır `Running` kalır (yalnız uzlaştırma kapatır); kuyruklu iptalde iş `Cancelled` olup satır kapatılamazsa satır `Queued` kalır ve tekrar onu sonsuza dek izler | `finally` kapatmasını yakala ve logla; `Queued` baş + bitmiş iş durumunu karar kuralına ekle |
| 🟢 | **F-515** · İzleyici aynı run kimliğiyle yeniden koşan işi görmez | Faz 208 ikinci tur denetimi 🟢4: `NextRunAsync` yalnız `ContinuedFromRunId` ardılı arar; kurtarma kapalı ve `MaxAttempts > 1` iken iş aynı run'ı yeniden koşar, izleyici `never started` der (varsayılan dışı yapılandırma) | Ardıl yoksa ve iş yeniden kiralandıysa aynı run'ı yeniden oku |
| 🟢 | **F-516** · Açık `Branch` `definitionVersion`'lı `/resume` tek-cevap iddiasını atlar | Faz 208 denetimi 🟢3: yalnız Operator/Admin; `Branch` modlu sürümle yeniden yayınlanan istek iddiasız cevaplanır | Karar: mod isteğin ilk cevabından mı okunur, `/resume` sürüm değişimi iddiayı mı taşır |

### Faz planlama

| Aciliyet | Kalem | Neden | Plan notu |
|---|---|---|---|
| 🟡 | **F-544** · Native agent SSE'si Loop iterasyonlarının sınırını yazmaz | Ölçüldü (2026-10-15, F-536 taraması): `POST /api/agents/{name}/run` her iterasyonu ve geri bildirimi tek metin akıtır; sınır olayı yalnız run olayları ucunda. Gömülebilir sohbet ve TS istemcisi taslağı cevap sanar (K-1006) | Eklemeli `loop.iteration` frame'i; preview.4 dışı (bakımcı kararı) |
| 🟡 | **F-545** · Kod grafında üretim başına agent sürüm sabiti yok | Kod okuması (2026-10-15, video raporu): `AgentVersion` graf kurulurken verilir, fabrika yalnız `IServiceProvider` alır; sabitsiz adım güncel sürümü çözer, template yayınından sonraki revizyon yeni sürümle koşar | Sürümü run parametresinden okuyan adım seçeneği |
| 🟡 | **F-529** · Kiracı kotasıyla fonksiyon düğümü durdurma | Faz 210 ölçtü ve kapsam dışı bıraktı: kota sayacı koşan workflow'un kendi düğüm tutarını görmez (workflow kökü payını kapanırken yazar, K-990) ve `QuotaEnforcer.CheckAsync` her çağrıda iki depo okuması yapar | Tutarlı sürüm koşan ağacın bellekteki payını sayaçla birleştirir; ayrı tasarım. K-999'un kota eşi |
| 🟡 | **F-530** · Ücretli tool çağrısının başlamadan reddi | Aynı model turunda istenen ikinci tool, ilki tavanı geçirse de koşar; sonraki model çağrısı reddedilir. Tool döngüsü MAF'tadır | Ayrı tasarım. K-999'un tool eşi |
| 🟡 | **F-531** · Checkpoint'ten devam eden run'ın bütçesi sıfırdan başlar | Faz 210 planının kod okuması: `WorkflowRunner.RunGuardedAsync` her yürütmede yeni bütçe kurar; bütçe reddiyle düşen run checkpoint'ten devam ettirilirse reddedilen düğüm başlar. Model çağrısı kapısı için de bugün aynıdır | Harcanan bütçe checkpoint'e ya da run kaydına yazılmalı; yeniden ölç, sonra planla |
| 🟡 | **F-290** · `AuthorizationConfigurationKey`'ın `1.0.0` GA'da kaldırılması: veri taşıma + form geçişi | Faz 190 alanı `[Obsolete]` yaptı (K-869); üç iş kaldı: veri taşıma, MCP formu, sütun ve üye kaldırma | GA öncesi son preview fazı; form geçişi erken yapılabilir. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Açık kalem gövdeleri* |
| 🟡 | F-297 · Gemini konuşma adaptörü | Yeni bağımlılık istemez; Faz 203'ün sözleşmesini uygular | Gövde: § *F-297* |
| 🟡 | F-322 · Kiracı başına workflow eşzamanlılığı | Semafor süreç başına, kiracılara ortak | Gövde: § *F-322* |
| 🟡 | F-347 · OpenAI strict şema modu | Kiracı başka model seçer; strict istenemez | Gövde: § *F-347* |
| 🟡 | **F-411** · Onay Loop ölçütü ve denetim izi | Bakımcı kararı (2026-10-12): video hattındaki insan onayı ileride agent onayına döner ve Loop ölçütüyle yapılır. Faz 201 Açık Soru 7-A ayırdı: `LoopIterationCompleted` ölçüt kararını, yargıç modelini ve agent sürümünü taşımaz. Ölçüt `AddLoopEvaluator(kind, factory)` ile servis alır; reviewer deseni (çocuk run, derinlik, `TryReserveRun`) `DraftReviewCriterion`'dadır | Tasarım onay ölçütüyle birlikte yapılır |
| 🟢 | F-248 · `unwrap(...) as Promise<T>` iddialarının tip düzeyinde kapısı | Bugün tek örnek elle tarandı; sonrakini kimse taramaz | Gövde: § *F-248* |
| 🟢 | F-249 · docs-site için tarayıcı tabanlı yerleşim kapısı | Elle koşulan case iki turda da kusur buldu | Gövde: § *F-249* |
| 🟢 | F-324 · Karışık zincirde agent adımının iletim modu | Loop bütün taslakları iletir; tip zinciri F-341 ile kapandı | Gövde: § *F-324* |
| 🟢 | **F-406** · Agent yolları konuşma sağlayıcısını adla seçsin | Faz 198 Açık Soru 5-C: Faz 198 adı yalnız fonksiyon düğümünde ve uçta seçtirir | Faz 198'den sonra; agent tanımına ses bağlaması |
| 🟢 | **F-408** · Skill sürüm geçmişi ve geri alma | Faz 200 Açık Soru 6-B: skill geçmişi K-082'yi (kullanıcı kararı) açar, üç tablo ve özet liste ister (20 × 256 KiB kaynak), K-860 grant'ına dokunmamalı | K-082'nin yeniden açılma kararı; Faz 200'ün workflow deseni |
| 🟢 | **F-409** · Alt agent ve skill sürüm sabiti | Faz 200 kapsam dışı: agent → skill ve alt agent adımı güncel sürümle koşar (K-895 (c)); agent numarası silmede yeniden kullanılır, sabit bunu miras alır | Faz 200'den sonra; numara yeniden kullanımı önce kapanır |
| 🟢 | **F-493** · Workflow sürüm geçmişi budanamaz | Faz 200 kapsam dışı: `workflow_definition_versions` hiç silinmez (numara kimliktir, K-987); silinen workflow'un geçmişi ve düğüm ayarındaki her değer kalır | Tüketici ihtiyacı gelince: `retention` kapsamı ya da yalnız hiçbir run'ın damgalamadığı sürümleri silen uç |
| 🟢 | **F-494** · Konsol grafı belirli bir workflow sürümü için çizemez | Faz 200 kapsam dışı: graf görünümü güncel tanımı çizer; sürüm farkı alan karşılaştırmasıdır | `GET …/graph?version=N` ve Sürümler satırında bağlantı |
| 🟢 | **F-410** · Kod Loop ölçütüne tanım başına tipli ayar | Faz 201 kapsam dışı: özel tür veri almaz — tek örnek bütün tanımlara hizmet eder (`LoopEvaluatorRegistry.BuildCriterion`, `default` dalı); konsol kod türü için alan göstermez ve tür değişiminde `marker`/`judgeCriteria`/`modes`'u temizler (`loop-section.tsx` `withKind`). Faz 195'in `Settings` deseni emsal; ayar `FindProblems`'a ve `GET /api/loop-criteria`'ya (şema) birlikte girer | Faz 201'den sonra |
| 🟢 | **F-417** · Görsel sağlayıcısı kiracının kendi anahtarını kullanabilsin | K-912 (2026-10-13): kiracının görsel sağlayıcısı için bağlaması varsa üretim reddedilir, çünkü sağlayıcı paketleri üreticiyi kurulum anahtarıyla kurar. Canlı ses `SupportsTenantCredentials` ile aynı işi çözdü (K-878) | Faz 198'in konuşma modeliyle birlikte: anahtarlı üretici fabrikası, OpenAI/Azure/Google görsel kaydı |
| 🟢 | F-420 · Önbellek yazma token'ı ve fiyatı | Faz 204'ten ayrıldı (2026-10-13); önce ölçüm | Gövde: § *F-420* |
| 🟢 | **F-465** · `GET /api/voice/providers` diyalog yeteneğini bildirmiyor | Faz 203 (2026-10-13): `SpeechProviderDescriptor` ad, tür, `ProviderName` ve `SupportsTenantCredentials` taşır; bir motorun diyalog sesleyip seslendiremediği listede görünmez, çağıran ancak `GetDialogueSynthesizerAsync` çağrısının hatasından öğrenir. Public sözleşme (descriptor alanı, OpenAPI, iki üretilmiş istemci) | Konsol motor seçimi ya da ikinci diyalog sağlayıcısı (F-297) gelince; `SupportsDialogue` ve `DialogueCapabilities` alanı |
| 🟢 | **F-445** · Konuşma soketi ses sağlayıcısını tur başına yeniden denetlesin | Faz 198 denetimi 🟢1 (2026-10-13, kod okuması): soket STT ve TTS'i bağlantı açılırken bir kez çözer (`VoiceConversationDriver.ResolveProvidersAsync`); açık bağlantı sonradan kaydedilen egress politikasını kapanana kadar görmez. Model tarafı her turda yeniden okur (F-310 emsali). Site ve XML bugün bu sınırı yazar | Karar: tur ortasında ret davranışı (hata çerçevesi, bağlantıyı kapatma) — protokol sözleşmesi; sonra tur başına çözümleme ve fonksiyonel test |
| 🟢 | **F-503** · OpenAI Responses kesik yanıtı `incomplete` işaretlemez | Ölçüldü (2026-10-14, Faz 207, geçici prob): `Length` ile kesik düz metin iki kolda `completed` döner | Karar: MAF JSON'una yama ile `incomplete` + `response.incomplete` (tel sözleşmesi) |
| 🟢 | **F-504** · Workflow SSE'si agent adımının kesildiğini söylemez | Faz 207 §207.6, kod okuması: olay alt run'da kalır; workflow `MessageDelta`'sı bitiş sebebi taşımaz | Önce ölçüm; sonra çerçeve kararı (K-991 emsali) |
| 🟢 | **F-505** · İçerikli `content_filter` yarım yanıtı kayıtta iz bırakmaz | Faz 207 §207.6: dedektör yalnız boş yanıtı hata yapar; yalnız chat/completions `content_filter` der | Karar: yeni ayrılmış `CustomType` ve ayna listesi (K-991) |
| 🟢 | **F-506** · Konsol playground'u `custom` çerçevelerini göstersin | Faz 207 §207.6: `use-playground-run.ts` `custom`'u yok sayar; kesilme ve kota bildirimi görünmez | Rozet tasarımı (`en.ts` + `tr.ts`), E2E |
| 🟢 | **F-508** · Bağlayıcıların kalan sessiz düşmeleri | Faz 206 kapsam dışı + denetim 🟢1: skaler/enum yazım hatası (`RateLimit.Partition`, `VoiceLive.DelegationMode`, bool/int/TimeSpan `TryParse`-atla) varsayılana düşer; liste anahtarına tek değer (`__0`'sız) ve boş dizi `GetChildren()`'da çocuksuzdur, yok sayılır | Karar: K-993'ü skalere genişletmek (açılışta hata) — public sözleşme |
| 🟢 | **F-513** · Retention hedefi yapılandırmadan kapatılamaz | Faz 206 Açık Soru 1-A: `MaxAgeDays` yalnız sayı alır; JSON `null` (8.0.1 `""`, 10.0.0 `null`) varsayılanı sessizce bırakır | Karar (1-B): boş değer `null` bağlar mı; saklama süresini uzatma yönü |
| 🟢 | **F-517** · Tek cevaplı istek için yönetici çıkışı | Faz 208 denetimi 🟢1 + kapsam dışı: teslimden sonra düşen devamda zıt cevap hep `409`; motorun teslimde reddettiği cevap isteği kilitler; kaydı `Abandoned` yapan uç yok | Admin ucu, denetim kaydı, konsol düğmesi |
| 🟢 | **F-518** · `workflow_responses` sınırsız büyür | Faz 208 🟡5: kayıt yalnız veri sahibi silmesiyle gider; hiçbir saklama hedefi `runs` silmez | `workflow_requests` hedefi var (K-1004); aynı kalıp `workflow_responses` için ayrı karar |
| 🟢 | **F-519** · Yalnız devam run'ı silinince cevap kaydı kalır | Faz 208 denetimi 🟢2: FK yalnız `source_run_id`'de; zincir boş görünür, aynı cevap `Gone` → `409` alır | Karar: `response_run_id` FK'sı ya da `Gone`'da yeniden sürme |
| 🟢 | **F-532** · Süre kapanışı normal kapanışın temizliğini atlar | Faz 209 denetimi 🟢2: süre ile kapanan run `KeepCheckpointsAfterCompletion = false` temizliğini ve `RecordRun` metriğini çalıştırmaz (onay emsali aynı); retention yine bırakır | Ortak kapanış yardımcısı ya da karar: emsal gibi kalsın |
| 🟢 | **F-533** · Kısmen dolmuş run'da kapanmış isteğe cevap alınır | Faz 209 denetimi 🟢5: bir istek kapanmış, öbürü açıkken kapalı isteğe cevap `WorkflowRunner` denetiminden geçer (satır durumuna bakılmaz); işaret kapalı satıra yazılmaz | Karar: kapı kapalı satırı `400` ile reddetsin mi |
| 🟢 | **F-534** · Kiracı listesi sayfa başına olay okur | Faz 209 §209.1: satır olayı `event_seq` ile adlandırır ama istem her satır için `ReadEventsAsync`'le okunur (sayfa en çok 200) | Tek olay okuyan `IRunStore` üyesi; önce ölç |
| 🟢 | **F-535** · Satırı olmayan bekleyen run listede yok ve dolmaz | Faz 209: satır yazımı best-effort; Faz 209 öncesi bekleyenler ve yazılamayan satırlar için doldurma yok (Açık Soru 7-B) | Karar: olaydan satır üreten uzlaştırma (SQL JSON ayrıştırma) |

### Doğrudan

| Aciliyet | Kalem | İş |
|---|---|---|
| 🟡 | **F-282** · `troubleshooting` sayfası ağırlık tavanında; yeni semptom girdisi eklenemiyor | Ölçüldü (2026-09-24): sayfa 58 994 B gzip, tavan 59 000 B; tavanı yükseltmek kapının kuralına aykırı. Sayfayı böl, sonra Faz 185'in girdisini ekle. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Açık kalem gövdeleri* |
| 🟢 | **F-267** · CI tam koşumu hâlâ tek test projesiyle koşuyor | Yerel kapı `-maxcpucount:2`'dedir ([Faz 184](arsiv/fazlar/184-TEST-BEKLEME-VE-E2E-YAPISI.md); aynı makinede 1 işçi 863/879 sn, 2 işçi 607/658/524 sn). `ci.yml` bilerek 1'de: runner donanımı ve Windows ayağı ölçülmedi. İş: bir CI dalında `-maxcpucount:2` ile en az üç koşum. Yeşil ve süre kazancı ölçülürse `ci.yml` güncellenir; kırmızı çıkarsa gerekçe hafızaya yazılır. Dala push gerektirir — kullanıcı onayı |
| 🟢 | **F-288** · `ReleaseArtifactFixture` yerel `1.0.0-preview.1`'i paketler; geliştirici cache'ine girerse nuget.org'daki aynı kimliği gölgeler | Faz 187 planı ölçtü (2026-09-23): `~/.nuget/packages/tracon.core/1.0.0-preview.1/.nupkg.metadata` kaynağı yerel `artifacts/package/release`'ti. 2026-09-24'te o dizin yoktu (cache temizlenmiş). Yayın provası artık izole cache kullanır, etkilenmez (K-864); risk geliştiricinin kendi tüketici denemeleridir | Fixture'ın sabit sürümünü yayınlanmış hiçbir sürümle çakışmayan bir `0.0.0-...` damgasına çevir ya da restore'u izole cache'e al; `MixedVersionGraphTests` ile birlikte ölç |
| 🟢 | **F-435** · TS istemci testi `202` gövdesini şemadan değil cast'ten okur | Faz 197 denetimi 🟢1 (2026-10-13): `packages/tracon-client/test/queued-workflow.test.ts:29-67` sahte gövdeyi cast ile geri okur; `schema.ts`'in `202` yanıtını `AcceptedRunResponse`'a bağladığını kanıtlamaz | Tip düzeyinde iddia (`expectTypeOf` ya da `satisfies`) |
| 🟢 | **F-446** · `README.md`'deki public tip sayısı bayat ve kapısız | Ölçüldü (2026-10-13, Faz 198): `README.md:347` "654 public types" der; `public-surface-baseline.txt` toplamı tabanda 668, bugün 671. İşlem sayısının aksine bu sayıyı hiçbir kapı denetlemez. İş: sayıyı DocFX'in ürettiği toplamdan al ve `check-content.mjs`'in işlem sayısı kapısı gibi denetle |
| 🟢 | **F-449** · `TimeoutSiteTests` `Task.Delay(TimeSpan, TimeProvider, …)` sitelerini görmez | Faz 196 denetimi 🟢1 (2026-10-13): kapının deseni yalnız `CancelAfter(` ve argümanlı `CancellationTokenSource` kurucusunu tarar; `Task.Delay(süre, saat, token)` siteleri (`WorkflowNodeTimeout.cs:97`, `TimeoutAIFunction.cs:111`, `WorkflowNodeRetry.cs:98`) kapsam dışıdır. Bugün üçünün değeri de kurucuda veya kayıtta `TimerPeriod` aralığıyla doğrulanır; yeni bir site doğrulamasız girerse kapı görmez | Deseni `Task.Delay` + `TimeProvider` aşırı yüklemesine genişlet; site listesine üç satır |
| 🟢 | **F-451** · `JobStoreContract` `int` milisaniye sınırını aşan süreyi sınamıyor | Faz 196 denetimi 🟢3 (2026-10-13): `A_jobs_own_time_limit_survives_the_round_trip_and_the_lease` yalnız 90 dk yazar (`src/Tracon.Testing.Contracts.Xunit/Contracts/JobStoreContract.cs:81`); süreyi `int` ms sütunda tutan üçüncü taraf depo (≈ 24,8 gün sınırı) geçer. Yerleşik sütun `bigint` | Contract'a ≈ 30 günlük bir case; `PublicAPI.Unshipped` satırı ve `CHANGELOG` |
| 🟢 | **F-507** · `SourceLanguageTests` üretilen site `api/` sayfalarını tarar | Faz 207 kapanışı (ölçüldü): sayfa `[Fact]` mutlak yolunu taşır; `faz-NNN` worktree'sinde yanlış kırmızı | Gitignore'lu üretilen ağacı atla |
| 🟢 | **F-509** · Seçenek ratchet'ı iki bölümü ve 13 tipi görmez | Faz 206 kapsam dışı: `SectionName`'siz `TraconTenantProviderOptions`, `TraconInboundTriggerOptions` (testsiz); ratchet yalnız Core ve Abstractions'ı tarar (`TraconWorkflowOptions`, `VoiceOptions`, sağlayıcı ve SQL tipleri) | Kapsamı genişlet; her biri için `<Tip>BindingTests` |
| 🟢 | **F-510** · Bağlama kanıtı kapısının boşlukları | Faz 206 denetimi 🟢2-4: `[Fact(Skip/Explicit)]` ya da ilgisiz tek `[Fact]` sınıfı sayar; `OptionsKeyCoverage` çağrısı zorlanmaz; yüklem testi `.Where`'i kanıtlamaz; (b) mesajı okunmayan anahtarda da "the test value" der; ayarlanabilir Tracon sınıfı skaler sayılır; Retention anahtarları property adından türer | Yüklemi atlanan/açık testi dışlayacak biçimde sıkılaştır; literal hedef listesi + yansımalı eşitlik |
| 🟢 | **F-512** · Doğrulayıcısı olmayan yedi seçenek tipi | Faz 206 kapsam dışı: `AsyncRun`, `ContentGuard`, `Egress`, `Idempotency`, `McpSecurity`, `RateLimit`, `SessionOwnership`; geçersiz aralığın çalışma anı etkisi ölçülmedi | Önce ölçüm; sonra `IValidateOptions` + `ValidateOnStart` |

### Tetik bekleyenler

| Kalem | Engel | Koşul ne zaman oluşur |
|---|---|---|
| **F-95** · Agent düzeyinde kesinti/devam kancası | MAF yolu kapalı (2026-09-05 ölçümü): kanca alpha pakette, native FFI taşır, kesinti/devam sözleşmesi sunmaz | Kalem yalnız F-141 üzerinden ilerler. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-178** · Model deneme (attempt) telemetrisi | Job/kuyruk metrikleri yarısı Faz 133'te kapandı; deneme telemetrisi yarısı için gerçek bir üretim fallback gecikmesi olayı ölçülmedi, tüketici bilerek erteledi | İlk fallback gecikme olayı ölçülünce. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-179** · Çalışma anı model yönlendirme policy'si | F-178 deneme süresini ölçmüyor; gerçek üretim trafiği yok | F-178 kapanır ve üretim trafiği oluşursa. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-180** · Tam paket koşumunda E2E zaman aşımı — sınıf açık | Vaka kapandı, **sınıf açık**: repro "yük altındaki tam paket koşumu"ydu ve kapanış onu kendi koşullarında tekrar koşmadı; test bir gün sonra aynı imzayla düştü. Ders `kusur-giderme` kapanış kontrolündedir | Sınıf yeniden görülürse **yeni** kusur kaydı açılır. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) |
| **F-199** · Kota eşiği bildiriminin claim SONRASI kaybı | Eşik claim edildikten SONRA webhook/akış yayını başarısız olursa o eşik dönem sonuna kadar kalıcı kaybolur. Düşük risk | Kota webhook/notice teslimi için retry/backoff istenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-200** · Çok kullanıcılı kota izolasyonu regresyon testi | Garanti **yapısaldır** (`RunEventWriter`'ın run başına özel `Guid`'i); eksik olan yalnız ona adanmış test | `RunEventWriter`/`RunRecordingAgent`'ın run izolasyonu yeniden düzenlenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-205** · `/v1/conversations/{id}` varlık asimetrisi | Kullanılmamış kimlik `200`, reddedilen `404`; katı modda varlık görünür | Tüketici varlık gizliliği isterse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-226** · SSE yanıtının şeması JSON şekli ilan ediyor | 7 `text/event-stream` yanıtının 2'si JSON şekli ilan ediyor; kütüphane OpenAPI transformer taşıyamaz (K-039) | Belgeden kod üreten üçüncü taraf bir üreteç kırılırsa (Faz 159 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-230** · `kurtarma.md` ↔ `.claude/settings.json` senkron kapısı | `KR-11` rampası `deny` listesini sayarak tekrarlıyor; senkron kapısı yok | `.claude/settings.json` `deny` bloğu ilk kez değiştiğinde. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-233** · SQLite'ın yük altındaki `SQLITE_BUSY` davranışı ölçülmedi | Test yalnız ayarların kurulduğunu doğrular; yük testi yalnız PostgreSQL'de | SQLite çok yazarlı/yük altında desteklenmek istenirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-236** · Audit yazma politikası ratchet'i değişken adına bağlı | Kaynak deseni `IAuditLog log = …; log.WriteAsync(...)` şeklini görmez; tek örnek meşru | Bir çağrı yeri gerçekten kaçarsa (Faz 171 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-240** · Kapasite kapısının kalan üç dar açığı | Faz 174 denetiminin 🟢 bulguları; üç dar açık kaldı | Kapasite ölçümü yenilendiğinde. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-237** · Etkiden önceki dar fail-closed `run` kaydı | Genel `RecordingMode.Required` reddedildi (A10); dar bir `seam` bugün yok, kayıp görünür (K-782) | Düzenlemeye tabi bir kurulum kanıtla talep ederse; önce `tracon.run.recording_failures` ölçülür. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-238** · Metrik dinleyici test yardımcısının ÜÇ kopyası | Üç `internal` `MeterListener` sarmalayıcısı; K-483 eşiğinde | Dördüncü kopya gerekirse ya da `Tracon.Testing`'e yardımcı istenirse (Faz 173 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-242** · `ResolveVersion`'ın `catch` dalı kanıtlanmadı | Sürüm çözümünde attribute okuması hata verirse alan `null` kalır (K-790); `catch` dalı testsiz | Attribute okumasının attığı bir kurulum raporlanırsa. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-243** · Analitik store yüzeylerinde metot bazında iptal kapsamı yok | Toplayıcı sorgular iptal sözleşmesinin metot bazında kapsamı dışında | Gerçek kaynak tüketimi raporlanırsa veya sınır gevşetilirse (Faz 177 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-245** · Sessiz bir `run` ile ölü bir bağlantı ekranda AYNI görünüyor | `SseDecoder` keep-alive yorumlarını düşürür; canlılık göstergesi bir arayüz kararıdır. Not: workflow akışı 2026-10-12'den beri keep-alive yazar (F-325) | Kullanıcı sessiz run'ı öldü sanarsa — **kullanıcı kararı gerekir**. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-246** · Sekiz kararlı hata kimliğinin karşılığı olan bir `RunErrorClass` YOK | 17 kimliğin 8'i bilerek eşlenmedi; yeni enum üyesi public sözleşme işidir | Operatör ayırt etmek isterse ya da taksonomi elden geçirilirken — **kullanıcı kararı gerekir**. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-250** · Kiracıya duyarlı örnek bir `IAgentSource` yok | `MT-CORE-095` iki turdur koşulamıyor; genişleme noktası sevk ediliyor, örneği yok | Bir tüketici kiracı başına agent kümesi sorarsa ya da case üçüncü turda da koşulamazsa. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-252** · Örnek uygulamanın demo kancaları bir kimlik sağlayıcısıyla değişmeli | K-834'ün dokuz demo kancası `samples/` altında | Örnek uygulama gerçek bir kimlik sağlayıcısına bağlandığında. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-253** · Konuşma paneli için sentetik ses sürücüsü | Panel `MediaRecorder` sentetik akıştan veri üretmez; iki case yalnız insanla koşulur | E2E'ye ses kapsamı istenirse veya tarayıcı bayrakları yapılandırılabilirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-254** · Kiracı egress politikası satırı YOKKEN hiçbir kısıt uygulanmıyor (fail-open) | Politikasız kurulumda kısıt yok; belgelenmiş varsayılan (K1) | Kullanıcı fail-closed seçeneğine karar verirse — **kullanıcı kararı gerekir**. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-256** · Harf-kayması sözleşme senaryosu yalnız iki store'da | Senaryo paylaşılan tabanda değil; bellek içi store'lar kimliği normalleştirmez (K-839) | K-839 genişlerse senaryo tabana taşınır. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-263** · `ic-dongu`, paylaşımlı provider kaynağının HTTP kanıtını seçmiyor | `Providers.Shared` değişikliği fonksiyonel kanıtı iç döngüde koşturmaz; kapanış yakalar | İç döngüde provider kaçışı ölçülürse (Faz 181 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-264** · Azure kimlik hatası için manuel kabul case'i yok | Davranış yalnız fonksiyonel testle kanıtlı; örnek uygulama Azure'u yalnız kimlikle kaydeder | Örnek uygulamaya Azure demo kancası eklenirse (Faz 181 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-274** · Üç SQL `Use*` metodundaki ~107 aynı kayıt satırı ortak bir iç yardımcıya taşınmadı | Parite testi kaçışı yakalar; kopya kalır | İkinci yatay değişiklik (sayım 2026-09-23'ten) ya da dördüncü SQL sağlayıcısı (K-176). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-276** · npm kanalı test edilen derlemeyi değil kendi `npm run build` çıktısını yayınlıyor | Üç ayrı derleme; NuGet eşi Faz 191'de kapandı (K-871) | `tsc` çıktısında ilk sapma ya da ilk npm yayın kusuru. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-277** · Dış katkıcı için İngilizce mimari karar özeti yok | `ARCHITECTURE.md` ve `CONTRIBUTING.md` dört Türkçe dokümana işaret eder; K-408 korunur | İlk dış PR mimari kararı Türkçe dokümandan okumak zorunda kalırsa ya da GA freeze turu. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-374** · `jobs.payload` ve `job_items.input` korumalı sütun değil; kuyruklu run'ın parametre değeri ve belge metni orada düz durur | Faz 194 (K-899, kullanıcı kararı): sütunu korumak ayrı migration ve anahtar kararıdır; site yazar | Düzenlemeye tabi bir kurulum kuyruk yükünün şifrelenmesini isterse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-375** · Batch öğesinde düz metin ile `{"message":…}` biçimli nesne ayırt edilemiyor | `JobPayload.ExtractItems` string ve nesne öğeyi aynı metne indirir (K-899) | Bir tüketici yanlış sınıflamayı bildirirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-376** · Paylaşılan boş parametre sözlükleri `Dictionary` tabanlı ve `GetRunParameters()` onları ya da kapsamın canlı sözlüğünü döner | Faz 194 denetimi (🟢): paylaşılan boş sözlükler dönüşümle yazılabilir | Bir tüketici sözlüğü dönüştürerek yazdığını bildirirse ya da yüzey genişlerse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-377** · `ReplayTools` kipiyle parametreli tanımın replay'i için test yok | `CompileParameterizedAsync(…, toolTransform, …)` yalnız dolaylı sınanıyor (Faz 194 denetimi 🟢) | Araç dönüşümü ya da replay tool kipleri değişirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-378** · Agent batch'te `anyItemCarriesValues` yalnız `Pending` öğelerden hesaplanır | Faz 194 denetimi (🟢): kira devrinde yalnız düz öğeler kalmışsa F-330 ön kontrolü job'ı tek seferde düşürür; ilk denemede aynı öğeler tek tek `Failed` olurdu. Sonuç yine reddedicidir | Bir kullanıcı kira devrinden sonra beklenmedik toplu düşüş bildirirse |
| **F-379** · `InMemoryRunInputStore` 1000 run'da keser; bekleyen workflow'un değerleri o noktada kaybolur | Faz 194 denetimi (🟢): SQL'deki retention istisnası (K-898) bellek içi depoda yok; cevap reddedilir. Bellek içi dağıtım üretim hedefi değildir | Bellek içi depo üretimde kullanılırsa ya da sınır yapılandırılabilir yapılırsa |
| **F-380** · `POST /api/workflows/{name}/run` OpenAPI açıklaması parametre kapısını anmıyor | Faz 194 denetimi (🟢): `ProducesProblem(400)` var, açıklama metni yok. Metni değiştirmek OpenAPI belgesini, `Tracon.Client` ve `@tracon/client` çıktısını yeniden üretir | Workflow ucunun açıklaması başka bir nedenle değişirse; o zaman birlikte yazılır |
| **F-381** · Workflow `run_inputs` yazımı düşerse bekleyen run kalıcı olarak cevaplanamaz | Faz 194 denetimi (🟡, gerekçelendi): yazım best-effort kalır (K-898) | Depo hatası nedeniyle cevaplanamayan bir workflow run'ı görülürse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-412** · Fonksiyon düğümünün ayar alanı şemadan üretilen bir forma dönüşmedi | Faz 195 (K-900, kullanıcı kararı): konsol JSON metin alanı ve şemanın üye listesini verir | Operatör tekrarlanan hata bildirirse ya da ayar tipleri iç içe üyeler taşırsa. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-413** · Konsol yapısal değişiklik uyarısını bekleyen run olmadan da gösteriyor | Uyarı her yapısal değişiklikte çıkar (K-901); yalnız bekleyen run varken göstermek sayım ister | Operatörler uyarıyı gürültü sayarsa. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-414** · Konsol 2^53 − 1'den geniş bir tamsayı ayarını kayıpsız taşıyamıyor | Faz 195 denetimi (🟡, gerekçelendi): konsol JSON sayısını `double` okur; sınır XML'de ve sitede yazılı | Bir tüketici 53 bitten geniş sayısal ayar bildirirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-415** · Tüketicinin dönüştürücüsünün (`JsonConverter`) fırlattığı `JsonException` mesajı `400`'e ve `runs.error`'a girer | Faz 195 denetimi (🟢): tüketicinin `JsonConverter`'ının istisna mesajı değer taşıyabilir | Mesajın değer taşıdığı görülürse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-422** · `openai-responses` harness turu cevabını akışta iki kez yayabilir — ölçüm bekliyor | Faz 201 örnek uygulama koşumu (2026-10-13): loop'lu agent'ın üç koşumunun ikisinde ilk turun metni akışta iki kopya; kaynağı ayırt edilmedi | Akışta yinelenen metin bildirilirse ya da `openai-responses` istemcisi güncellenirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-511** · Yapılandırma değişikliği çalışma anında uygulanmaz | `IOptionsChangeTokenSource` kaydı yok; `Configure<T>(lambda)` yeniden yüklemeyi taşımaz, `IOptionsMonitor` tüketicileri (ör. `TraconRateLimitFilter`) değişen dosyayı görmez. Site yeniden yükleme vaat etmez (Faz 206 kapsam dışı) | Bir tüketici yeniden başlatmadan ayar değişikliği ister |
| **F-520** · Cevabın kendisini devam run'ının olayına yazmak | Faz 208 kapsam dışı: içerik koruması (K-325, K-899) ve saklama kararı ister; bugün yalnız istek kimliği ve kaynak run yazılır (K-995) | Bir denetçi "ne cevaplandı"yı run geçmişinde isterse |
| **F-521** · Doğrudan `IWorkflowRunner.RespondStreamingAsync` çağıranın denetimi ve tek yanıtı | Faz 208 kapsam dışı: denetim ve iddia uç katmanındadır (K-079, K-370 emsali) | Bir tüketici runner'ı uçsuz çağırıp tek yanıt isterse |
| **F-522** · Tek yanıtı istek portu başına seçmek | Faz 208 kapsam dışı: mod workflow başınadır (K-994) | Port başına seçim için tüketici kanıtı gelirse |
| **F-523** · `workflow.request.answered` webhook'u | Faz 208 kapsam dışı: onay kararının da webhook'u yok | Tüketici kanıtı gelirse |

### F-248 · `unwrap(...) as Promise<T>` iddialarının tip düzeyinde kapısı

**Kanal:** faz planlama · 🟢

**Sorun:** Konsol her uç çağrısını `unwrap(client.X(...)) as Promise<T>` ile
daraltır; `T` üretilen şemanın (her alanı opsiyonel) yerine `server-types.ts`'in
daha dar tipini koyar ve bu **bilinçli** bir tasarımdır. Ama iddia yanlış bir
ŞEKLİ de adlandırabilir ve hiçbir şey bunu söylemez: `HATA-S3-008`'de bir çağrı
`{scores,failures}` döndüren bir ucu `RunScore[]` ilan etti, `data.length` her
zaman `undefined` kaldı ve arayüz sessizce hiçbir şey göstermedi.

**Kapsam:** Daraltmayı bir *assertion* yerine bir yardımcıya taşımak ve
`Narrow extends Wide` kısıtını tip sistemine kurdurmak, böylece uyumsuz bir
şekil **derleme hatası** olur. 115 çağrı yerinin hepsi dönüştürülür.

**Değer:** Sınıfın tamamı derleyiciye devredilir; bugünkü tek örnek elle
tarandı, sonrakini kimse taramaz.

**Hazırlık — ölçüldü (2026-09-19):** 115 çağrı yerinin tamamı üretilen şemaya
karşı tek seferlik bir betikle tarandı; **yalnız bir tanesi** yanlış şekil
adlandırıyordu, gerisi amaçlandığı gibi daraltıyor. 🚨 İddiaları **silmek**
denendi ve yanlıştır: `tsc` 40+ hata verdi — daraltmalar yük taşıyor.
Betiğin kendisi 115 yerde 8 yanlış pozitif üretti (204 yanıtlar, import
takma adları, çok satırlı eşleşmeler), ∴ kapı ayrıştırıcı değil tip düzeyi
olmalıdır.

**Maliyet:** Ölçülmedi. Mekanik ama geniş bir dokunuş.

**Risk:** Kısıtın meşru daraltmaları reddetmesi. Önce üç-beş yerde ölçülmeli.

---

### F-249 · docs-site için tarayıcı tabanlı yerleşim kapısı

**Kanal:** faz planlama · 🟢

**Sorun:** `MT-DKL-026` (dokuz şablon × dört genişlik × iki tema, yatay kayma
yok) **elle** koşulan bir case'tir ve iki turda da gerçek bir kusur buldu
(`HATA-S3-002`). `npm run check` içerik, bağlantı ve ağırlık ölçer; yerleşim
ölçmez. Dekoratif bir öğenin sayfayı genişletmesi hiçbir kapıyı kırmaz.

**Kapsam:** docs-site'a Playwright bağımlılığı ve `dist/` üzerinde koşan bir
`check:layout` adımı: sayfa kümesi × genişlik kümesi × iki tema için
`scrollWidth - clientWidth == 0`.

**Değer:** Bugün 72 ölçüm noktasını insan koşuyor. Kusur iki turda da oradan
çıktı.

**Hazırlık — ölçüldü (2026-09-19):** Ölçümün kendisi Playwright ile yapıldı ve
mekaniktir (on dört genişlik × iki tema, tek betik). Eksik olan yalnız
docs-site'ın kendi bağımlılığı ve CI adımı.

**Maliyet:** Ölçülmedi. Yeni bir npm bağımlılığı ve CI'da tarayıcı kurulumu.

**Risk:** CI süresine tarayıcı indirme maliyeti ekler; docs-site bugün
tarayıcısızdır.

---

## Aday Olmayan Açık Kayıtlar

Bu kalemler faz sıralamasına **girmez**. Tam kanıt ve sonraki adım keşif
kaydındadır; burada yalnız hangi eşikte bekledikleri yazar.

| Eşik | ID'ler | Kural |
|---|---|---|
| **Karar / uyumluluk eşiği** | F-72 · F-90 · F-91 · F-92 · F-132 · F-169 | Mevcut karar veya dış bağımlılık değişmeden planlanmaz. **F-169** (MAF CodeAct / Hyperlight sandbox) F-72 ile **aynı eşiktedir**: paket GA ve taşınabilir olana kadar planlanmaz — ölçüm [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md) § 9 |
| 🟢 | **F-540** · Kod grafında katılımcı kapsaması run başında yapılmaz | Faz 211 (K-1010): saklanan workflow'da agent'ların zorunlu parametreleri ilk model çağrısından önce denetlenir; kod grafında eksik değer adım koşarken düşer, önceki adımlar token harcamıştır | `WorkflowGraphReader` agent adlarını verir, `AgentVersion` sabiti bilinmez. Önce ölçüm: tüketici ihtiyacı |
| 🟢 | **F-541** · `GET /api/workflows/{name}` kod workflow'u için `404`; şema ve `requestTimeout` yalnız listede | Faz 211 (K-1010): uç `WorkflowDefinition` döner, kod workflow'unun betimleyicisi tek tek okunamaz | Betimleyici döndüren uç; uç sözleşmesi büyür (public), önce tüketici ihtiyacı ölçülmeli |
| 🟢 | **F-542** · Zaman aşımından sonra token'ı yok sayan handler `MaxConcurrency` slotunu bırakmış olur | Faz 211 denetimi: `limit` `timeout`'un dışındadır; `RetryPolicy` ile yeni deneme boşalan slotu alır, `MaxConcurrency = 1` aşılır | Slotu handler bitene kadar tutmak beklemeyi düğüm süresine katar; belgeli sınır yeter. Önce ölçüm: üretimde görülüyor mu |
| 🟢 | **F-543** · Kod `RunTimeout`'unun çalışma anı kesme dalı sınanmıyor | Faz 211 denetimi: tavan sonradan düşünce run tavana kesilir ve loglanır; testler yalnız açılış reddini sınar. Dal saklanan tanımla ortak | `IOptionsMonitor` ile tavanı çalışma anında düşüren fonksiyonel test; emsal `WorkflowDefinitionRunTimeoutTests` |
| **Ölçüm bekliyor — F-ID'leri** | F-51 · F-94 · F-96 · F-97 · F-99 · F-101 · F-123 · F-128 · F-154 · F-156 · F-157 · F-159 · F-160 · F-161 · F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. Kanıt üretmeden aday olmaz |
| **Ölçüm bekliyor — A-ID'leri** | A01 (kalan manifest bağlama) · A02 · A03 · A05'in ETag ve provenance dilimleri · A06 · A07 · A08 · A09 · A10 · A11 · A12 · A13 · A14 · A15 · A17 · A18 | 🚨 Bunlar **F-NN değildir** — 2026-09-07 tüketici analizi turunun rapor içi izleme kimliğidir; biri seçilirse o zaman aday numarası alır. Değer/maliyet/risk yargısı ve gereken kanıt [tam raporun](arsiv/incelemeler/2026-09-07-tuketici-analizi-codebase-olcumu.md) §5'indedir. **A17/A18 mimari ret DEĞİLDİR** — mevcut yetenekle çözülemeyen somut bir vaka çıkarsa yeniden değerlendirilirler |

### F-ID tahsis kuralı

Numara **geri dönüştürülmez** ve bir numara **tek kaleme** aittir. Sıradaki
numara: **F-547**.

Numarayı tahsis eden el sayacı **aynı değişiklikte** bir artırır.
`python3 scripts/dokuman-bakim.py --denetle` iki şeyi zorlar:

- Sayaç, `docs/` ve `.agents/` altındaki en büyük F-ID'nin **bir fazlasıdır**.
- Bu dosyada bir F-ID yalnız **bir** kalemi tanımlar (kalın `**F-NNN** ·`
  satırı veya `### F-NNN ·` başlığı).

---

## Bilerek Önerilmeyenler

Tek kaynak
[`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)'dir:
reddedilmiş işler ve "zaten var, eksik diye önerilmez" kalemleri. Özellikle
**F-91** (`secret` saklama sınırı) ve **F-92** (dağıtık hız sınırı / Redis)
kararı değiştirmeden yeniden aday olmaz.

**F-95 bu listede değildir** — onu bekleten bir tasarım kararı değil, MAF'ın
sözleşmesidir; bkz. § *Tetik bekleyenler* → F-95.
