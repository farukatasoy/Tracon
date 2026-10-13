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

**Durum (2026-10-13):** sıralanabilir kuyrukta **6** aday var (F-297, F-420
podcast turu; F-319, F-322, F-324, F-347 video tüketicisi turu). Aynı turların on kalemi
2026-10-12'de yedi faza dönüştü (Faz 196…202); 2026-10-13'te F-296 Faz 203'e,
F-320 ve F-346 Faz 204'e, F-405 Faz 196'ya girdi (planlandı). Bugün
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

**Bağımlılık:** [Faz 203](203-COK-KONUSMACILI-DIYALOG-SENTEZI.md) (F-296, planlandı); Faz 198 (F-298, planlandı) olmadan iki sağlayıcı aynı süreçte seçilemez.

**Ekosistem (2026-10-11):** Modeller `gemini-3.8-flash-tts`, `gemini-3.8-flash-lite-tts`, `gemini-3.1-flash-tts-preview`, `gemini-2.5-pro-preview-tts`; .NET örneği yok.

**Karşı görüş:** Faz 198 olmadan adaptör ElevenLabs'in yerine geçer, yanına gelmez; tek başına değeri düşük.

### F-319 · Workflow HITL karar sözleşmesi: kiracı geneli liste, opt-in tek yanıt, karar denetimi, süre

**Kanal:** faz planlama · 🟠

**Sorun:** İstekler yalnız run başına listelenir (`WorkflowEndpoints.cs:175-186`). K-130 gereği yanıtlanan run `AwaitingInput` kalır; gerçekten bekleyen ayrılamaz. İkinci yanıt yeni dal açar; `respond` `Idempotency-Key` almaz. Yanıt denetim izine yazılmaz, tool onayı yazar (`ApprovalEndpoints.cs:238`). İstek için süre yoktur.

**Kapsam:** Kiracı geneli bekleyen istek listesi (sayfalı, "yanıtlandı" işaretli). Workflow başına opt-in tek yanıt: aynı cevap aynı devamı sürer, zıt cevap 409 (tool onayı kuralı). `workflow.respond` denetim kaydı (aktör, requestId, karar özeti hash'i) ve yeni run'a kaynak run bağı. İsteğe bağlı süre ve süre dolunca kapanış.

**Değer:** Host ekranı n revizyonlu onayı tek listeden yönetir; çift tıklama ikinci render üretmez; "kim neyi onayladı" cevaplanır.

**Mercek:** 2, 3, 5.

**Hazırlık:** Tool onay posta kutusu (Faz 55) ve fail-closed denetim deseni var.

**Maliyet:** Yeni uç ve public yanıt tipi, `RunEventType` üyesi; olası projeksiyon tablosu (migration; K-128'i açar).

**Risk:** K-128: kiracı geneli olay taraması pahalı olabilir — önce ölçülür. K-130 varsayılanı korunur.

**F-327 kapanışı eki (2026-10-12):** retention artık `AwaitingInput` run'ın istek olayını ve checkpoint'lerini süresiz tutar (K-888); yanıtlanan istek de K-130 gereği `AwaitingInput` kalır, yani bu satırlar hiç silinmez. Bu adayın "süre" maddesi o büyümenin de sınırıdır: süre dolan istek kapanınca retention onu silebilir. F-332 (2026-10-12) sonrası aynı run'ın oturumundaki ekler de run yaşadıkça tutulur; süre maddesi onları da kapsar.

**Bağımlılık:** Yok (F-327 ve F-335 kapandı). Faz 197'den sonra planlanırsa tek yanıt kuralı arka plan `respond`'una da uygulanır.

**F-341 kapanışı eki (2026-10-12):** uymayan yanıt artık akıştan önce `400` alır ve run açmaz (K-908); bu adayın "zıt cevap `409`" kuralı o denetimin üstüne kurulur. Faz 197 (planlandı) `respond`'a `Idempotency-Key`'i yalnız arka plan kipinde getirir; aynı cevabın aynı devamı sürmesi ve kiracı geneli liste bu adayda kalır.

**Ekosistem:** MCP Tasks `input_required` aynı ihtiyacı protokolde karşılar; Tracon görev deposu onu boş bırakır (`RunBackedMcpTaskStore.cs`, 2026-10-11).

**Karşı görüş:** Onay tüketicinin ekranında ve domain'inde tutulabilir; değer yalnız Tracon HITL'ini kullanan host içindir.

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

**Bağımlılık:** Faz 204 (`promptCaching.ttl`); F-419 (ağaçta para birimi kuralı) aynı toplamlara dokunur.

**Ekosistem (2026-10-13):** Anthropic önbellek yazmasını TTL'e göre fiyatlar; OpenAI yazma ücreti almaz; Gemini bağlam önbelleği saklama süresine göre ücretlendirir.

**Karşı görüş:** Ölçülen pilotta pay ~%1'dir; migration'ın maliyeti değerden büyük olabilir. Ölçüm "hiç sayılmıyor" demedikçe beklemek savunulabilir.

## Bekleyen Kalemler

Bir kalem buradan iki yolla çıkar: iş yapılır (kalem kapanır veya plana
dönüşür) ya da kalemin artık gerekmediği ölçülür ve kalem kapatılır. İki
yolda da § *Kalemin yaşam döngüsü* uygulanır.

### Kusur giderme

2026-10-12'de üç tur kapandı: önce 🟠 kalemler (F-325 · F-326 · F-328 · F-327 · F-329 · F-330 · F-331, F-317 ve F-314 ile), sonra 🟡 kalemler (F-315, F-332…F-340, F-348, F-349, F-351, F-352; K-892…K-896), sonra tüketici turunun kusurları (F-341…F-344, F-354, F-360, F-364…F-367, F-369…F-372; K-903…K-911). F-382 bu turun kendi ölçümüdür; F-386…F-404 kapanan kusurların sınıf taramalarından gelir. 2026-10-13'te F-383, F-384, F-385 ve F-388 kapandı (K-912, K-913); sınıf taramaları ve bağımsız inceleme F-416…F-419'u açtı. Faz 201 kapanışında F-423 (kapıyı kıran test yarışı) açıldı ve kapandı. Kapanan kalemlerin kaydı [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § Kapanan kalemler'dedir.

| Aciliyet | Kalem | Sorun | Ne yapılır |
|---|---|---|---|
| 🟡 | **F-382** · `GET /api/workflows/functions` ve `GET /api/schedules/handler-keys` aynı adlı kaydı gölgeler | Kod okuması (2026-10-12): sabit segment `{name}` rotasından önce eşleşir (`WorkflowEndpoints.cs:53` ↔ `:70`, `SchedulingEndpoints.cs:65` ↔ `:40`); ad kuralında ayrılmış ad yok. `functions` adlı workflow `PUT` ile kaydolur, `GET` fonksiyon listesini döner; konsol editörü ve istemciler yanlış şekil alır | Karar: iki ad ayrılmış olur (`400`) ya da sabit uçlar başka yola taşınır (public HTTP sözleşmesi); önce fonksiyonel kırmızı test |
| 🟡 | **F-416** · Bilgi tabanı embedding çağrısı kiracı egress politikasından geçmez | Kod okuması (2026-10-13, F-384 sınıf taraması): `search_knowledge` sorgusu ve belge alımı tüketicinin kaydettiği adsız `IEmbeddingGenerator`'ı çağırır (`VectorSearchToolFactory.cs:55`, `KnowledgeIngestionService`); kiracının metni politikanın yasakladığı sağlayıcıya gidebilir. Üretici sağlayıcı adı taşımadığı için `TenantProviderAccess` ad ile denetleyemez | Karar: embedding sağlayıcısına ad (`Tracon:Knowledge:EmbeddingProvider` ya da keyed kayıt) ve aynı kapı; ad yoksa politikalı kiracıda ret mi geçiş mi. Önce fonksiyonel kırmızı test |
| 🟡 | **F-418** · Kök kapandıktan sonra biten arka plan alt run'ı kotaya girmez | Bağımsız inceleme (2026-10-13, F-383 sonrası, kod okuması): MAF `BackgroundAgentsProvider` `start_task`'ı hemen döndürür; `ChildAgentInvoker.cs:217-228` sert kesmeden sonra çocuğu koşar bırakır. Kök ağacı okuduğunda çocuğun satırı `Running` ve usage'sız; çocuk `Depth > 0` olduğu için kendi başına saymaz. "Görevi başlat, bekleme" talimatı alt agent harcamasını kotadan gizler (K-913 bilinen sınırı) | Karar: kök kapandıktan sonra biten alt run kendi tüketimini köke atfederek kotaya yazar mı; önce fonksiyonel kırmızı test (bekleme yapmayan kök) |
| 🟡 | **F-386** · İçerik koruyucu maskelenen belgeyi işaretsiz metin yapar | Kod okuması (2026-10-12, F-343 sınıf taraması): `ContentGuardMessageMasker.WriteText` (`:346`) belge mesajını işaretsiz `TextContent` olarak yeniden kurar; `RunStarted.Text` belge metni olur, `DocumentAttached` yazılmaz. İşareti aynen korumak ham belge adını ve özetini kayda sızdırır | Tasarım: maskeli belge için işaret (ad yerine maske). Terfi bugün blok metninden tanıyarak atlatır |
| 🟡 | **F-387** · Durum okuyucusu System.ClientModel'in `Status: nnn` mesajını görmez | Ölçüldü (2026-10-12, F-354 probu): boş ya da OpenAI dışı gövdede mesaj `Service request failed. Status: 400 (Bad Request)` olur, `ProviderHttpStatus.Read` boş döner. Devre boş gövdeli `400`/`404`'ü sayar; boş gövdeli `401`/`403`/`404`'te zincir yedeğe geçer (K-449, F-338 ihlali); düğüm retry'ı tekrarlar. Ayrıca istenmemiş her `OperationCanceledException` kökenine bakılmadan devreye sayılır | Tek okuyucuya `Status:` biçimi; `FallbackRetryClassifier` `ProviderHttpStatus`'u okur; Azure.Core, Google, Anthropic biçimleri ölçülür. K-817 parmak izi değişir |
| 🟡 | **F-389** · Batch ve eval kök run'ı onay isterse `AwaitingApproval`'da kalır | Kod okuması (2026-10-12, F-370 sınıf taraması): oturumsuz batch öğesi (`AgentBatchJobHandler`) ve eval case run'ı (`EvalJobHandler`) onay isteyen tool'da `AwaitingApproval` ile kapanır; posta kutusu satırı yok, cevaplanamaz; batch öğesi `Completed` raporlanır. AgentRun'ın oturumsuz `Failed` kuralı burada yok | Oturumsuz kök run `Failed` (AgentRun kuralı) ya da `QueuedTurn` kancası; fonksiyonel kırmızı test |
| 🟡 | **F-396** · Workflow SSE uçları girdi ve durum hatasını akış başladıktan sonra bildirir | Ölçüldü (2026-10-12, F-341 sınıf taraması): geçersiz `sessionId`, olmayan run'ı `resume`, beklemeyen run'a `respond` `200` + SSE hata alır. Olmayan `checkpointId` ile `resume` ayrıca yeni bir `Failed` run satırı açar ("Checkpoint '…' was not found.") | Durum kodu kararı (`404`/`409`/`400`) ve F-341 deseni: akıştan önce denetim |
| 🟡 | **F-400** · Veri sahibi kapsamı dışında kalanlar: `idempotency_keys`, `job_items`, terfi edilmiş eval vakası | Kod okuması (2026-10-12, F-371 sınıf taraması): `idempotency_keys` run yanıtını (`body`) ve `run_id`'yi, `job_items` öğe mesajını ve `run_id`'yi taşır; ikisi `DataSubjectCoverageTests`'te "Open" muafiyetiyle durur. Terfi edilmiş vaka üretim girdisinin kopyasıdır. Silme commit edildikten sonra koşan run checkpoint ve oturumu geri yazabilir | İki tablo hedef olur; vaka için karar (sil ya da koru); geri yazım için önce test |
| 🟡 | **F-401** · Dışa aktarım korumalı sütunları şifreli metin olarak verir; checkpoint durumu korunmaz | Kod okuması, ölçülmedi (2026-10-12, F-371): `SqlDataSubjectStore.ExportAsync` sütunları ham yazar, `ProtectedValue.Read` çağırmaz; `AddContentProtection` açıkken `sessions.state`, `run_inputs.*`, `conversation_items.item` şifreli gelir (site "your own code never sees ciphertext" der). `ProtectedColumn` `workflow_checkpoints.state`'i kapsamaz | Önce kırmızı test (koruma açık dışa aktarım); checkpoint için koruma kararı |
| 🟡 | **F-361** · `EvaluatorRunJudge` harcaması hiçbir yere yazılmıyor | Kod okuması (2026-10-12, F-349): MEAI evaluator'ını kurulum istemcisiyle çağırır; `ModelRunJudge` gibi bir Eval run satırı yazmaz, örneklenen run'a da eklemez. XML (`:29-30`) "maliyeti her örneklenen run'a eklenir" der | K-328 deseni (`judge:{name}` Eval satırı) ya da XML'in düzeltilmesi; karar ister |
| 🟢 | **F-419** · Ağaç maliyeti farklı para birimindeki alt run'ları tek etiketle toplar | Bağımsız inceleme (2026-10-13, F-383): bellek deposu ilk para birimini alır (`costCurrency ??=`), SQL `MAX(cost_currency)`; EUR ve USD alt run'lar uyarısız toplanır. Workflow kökü de aynı. K-893 kuralı ağaç toplamında uygulanmıyor | Ağaç toplamı karışık para biriminde `null` ve sayaç; kök ve workflow aynı kuralı okur |
| 🟢 | **F-390** · Sürdürme yer tutucusu kuyrukta kullanıcı ve etiket taşımaz | Kod okuması (2026-10-12, F-369): `RunReconciliationService` yer tutucu satırı `UserId`/`Labels`'sız açar; işleyici run başlayınca doldurur (test kanıtlı). Boşluk yalnız kuyrukta bekleyen ve başlamadan düşen satırdadır | Yer tutucu kaynak run'ın atfını yazar (`ApprovalEndpoints` emsali) |
| 🟢 | **F-391** · Onay satırı yazımı tekrar edilemez | Kod okuması (2026-10-12, F-367): `QueuedTurn.RecordPendingApprovalsAsync` → `IPendingApprovalStore.CreateAsync` (`QueuedTurn.cs:258`) oturum kaydından sonra hata verirse run `Failed` olur; deneme hakkı varsa model ikinci kez çağrılır. `CreateAsync` idempotent değil | Id ile önce okuma ya da idempotent ekleme; sonra F-367 penceresi |
| 🟢 | **F-392** · Yeniden denenen eval token toplamını eksik yazar | Kod okuması (2026-10-12, F-365 sınıf taraması): kira dolumu ya da kapanış yazımı yedeği sonrası deneme yalnız kendi koştuğu vakaların token'ını sayar (`EvalJobHandler`) | Toplam kayıtlı vaka sonuçlarından hesaplanır |
| 🟢 | **F-393** · Başarısız webhook gönderiminde `store` hatası beklemeyi atlar | Kod okuması (2026-10-12, F-366): başarısız gönderim yolundaki yazım hatası basamaklı beklemeyi atlar, iş hemen yeniden denenir; son denemede teslim `Pending` kalır (`WebhookDeliveryJobHandler`) | Başarısız yol da `JobOutcomeWriteRetry` ile; son denemede teslim `Failed` |
| 🟢 | **F-394** · Çevrimiçi eval özet yazımı düşerse puan özete hiç girmez | Kod okuması (2026-10-12, F-365 sınıf taraması): puanlar yazıldıktan sonra `RecordScoreAsync` (`OnlineEvalJobHandler.cs:398`) hata verirse deneme düşer; tekrar yargıcı atlar, puan çevrimiçi özete eklenmez | Özet yazımı ayrı ve tekrar edilebilir (F-348 deseni) |
| 🟢 | **F-395** · Fonksiyon düğümünün kendi model çağrısı kök akışa `ModelFallbackUsed` yazar | Kod okuması (2026-10-12, F-360): düğüm içinden yapılan model çağrısının fallback'i workflow kökünün akışına olay yazar (`FallbackChatClient.RecordFallbackUsedAsync`); `model_id` ve maliyet etkilenmez | Karar: olay yalnız atfı sahiplenen agent run'ında mı, workflow sinyali olarak mı (K-907) |
| 🟢 | **F-397** · Eval run'ları atıfsız; kök run'ın atfı tüketici bağlamına bağlı | Kod okuması (2026-10-12, F-364): eval case run'ları kullanıcı ve etiket taşımaz (`JobAttributionScopeSiteTests`'te gerekçeli). Kök run'ın atfı yalnız `IRunAttributionContext` ortam kapsamını okursa görünür; okuyucu etkin `AmbientRunAttributionScope`'u önceleyebilir (`AgentSessionManager` emsali) | Karar: eval kuyruğa alanın atfını taşır mı; okuyucu sırası (public davranış) |
| 🟢 | **F-398** · `object` girdili ilk fonksiyon düğümü iki kez koşar | Ölçüldü (2026-10-12, F-341, MAF 1.20.0): runner yanıtsız başlangıçta `TurnToken`'ı her zaman gönderir; `object` girdili ilk düğüm bir kez `List<ChatMessage>`, bir kez `TurnToken` ile koşar. Tip kuralı bu düğümü reddetmez | Karışık zincirde token gerekliliği ölçülür; gerekmezse gönderilmez, gerekirse ilk düğüm `object` alamaz |
| 🟢 | **F-399** · Uzlaştırıcı callable alt agent ya da skill taşıyan kök agent için devam kuyruğa koyar | Kod okuması, ölçülmedi (2026-10-12, F-344): işleyici sonra reddeder (`RunContinuationJobHandler` skill ve callable reddi), `Failed` bir yer tutucu satır kalır | `RunContinuationEligibility` bu iki reddi uzlaştırıcıda da uygular |
| 🟢 | **F-402** · Zamanlama atfı Admin'in yükte yazdığı beyandır | Karar sorusu (2026-10-12, F-364): zamanlama zarfındaki `userId`/`labels` elle yazılır (K-904); agent-run yükü de aynı alanı kabul eder. "Gövde atıf kaynağı değildir" ilkesiyle çelişir | Karar: zamanlamanın kimliği (kaydı yazan Admin · servis kimliği · beyan) |
| 🟢 | **F-403** · Eval case derlemesi altıncı elle yol; kesme işaretinin kopyası | Kod okuması (2026-10-12, F-343): `EvalJobHandler` parametreli derlemeyi elle yapar ve dekoratörleri uygular ("tek fabrika" notunun dışında); `WorkflowRequestDescriptor.cs:285` `…[truncated]` işaretini tekrarlar (`RunEventWriter.TruncationMarker` var) | `ParameterizedAgentFactory.CompileAsync`'e birleştirme; sabit kullanımı |
| 🟢 | **F-404** · Yanıt önbelleğinin depo okuma hatası "miss" metriğine yazılır | Kod okuması (2026-10-12, F-354 sınıf taraması): `TraconResponseCachingChatClient` (`:138`, `:170`) iki farklı olayı tek metrikte birleştirir; kapı etkisi yok | Ayrı `error` sonucu (metrik etiketi) |
| 🟢 | **F-350** · Onay bekleyen yanıt yapılandırılmış doğrulamada "boş" diye reddedilebilir | Kod okuması, doğrulanmadı (2026-10-12, F-329 incelemesi): doğrulayıcı (30) `ToolApprovalAgent`'ın (20) içindedir; onay isteğiyle biten yanıtın asistan metni yoktur ve run `AwaitingApproval`'a geçmeden `StructuredResponseInvalid` ile düşebilir | Önce kırmızı test (Json biçimli agent, onay isteyen tool). Doğrulanırsa onay bekleyen yanıt doğrulamadan geçer |
| 🟢 | **F-316** · Akışsız run endpoint'i `LoopUnsatisfied` ve `StructuredResponseInvalid` hatasını `502` olarak döner | Faz 192 denetimi (kod okuması): akışsız yolun genel `catch`'i (`AgentEndpoints.cs:1553-1571`) her istisnayı `502 Bad Gateway` ve `errorType`'sız problem olarak yazar. Karşılanmayan döngü ya da geçersiz yapılandırılmış yanıt bir yukarı akış arızası değildir; istemci sebebi yalnız run kaydından okur. Akışlı yol türü `error` çerçevesinde taşır (`MT-CORE-134`) | Doğru durum kodu kararlaştırılır (ör. `422`); problem gövdesine `errorType` eklenir. OpenAPI anlık görüntüsü ve istemci birlikte yenilenir. Etki: public HTTP sözleşmesi |
| 🟢 | **F-353** · Sınıflandırıcı durum kodunu iki yerde görmez | Kod okuması (2026-10-12, F-338): normalleştirilmiş `429` `RateLimited` değil `ProviderError`; yalnız `HttpRequestException.StatusCode`'da taşınan `5xx` `transport_error`; `ProviderFailureNormalizer.ReadStatus` `StatusCode`'u okumaz | `error_class`'ı değiştirir; K-817 "tek daraltma" ile karar. Durum okuması `ProviderHttpStatus.Read`'den |
| 🟢 | **F-355** · Registry, sağlayıcının elle attığı ayar reddini genel bir upstream hatasına çevirir | Kod okuması (2026-10-12, F-340): üçüncü taraf sağlayıcı bir anahtarı kendi `TraconException`'ıyla reddederse `ProviderFailureNormalizer` onu maskeler; `/validate` `invalid_setting` gösterir ama anahtar adını göstermez. `ModelProviderSettingsContract` sağlayıcıyı registry'nin altında çağırdığı için farkı görmez | Sözleşmeye registry üstünden bir senaryo ya da normalizer'da ayar reddi için tip istisnası (ör. `ProviderSettingsValidationException` türetmesi) |
| 🟢 | **F-356** · `kapi.py ic-dongu` tam koşuma genişleyince UI'sız derler ve E2E'nin tamamı düşer | Ölçüldü (2026-10-12, F-340): eksik proje haritası (bir `samples/` yolu) tam koşuma genişletir ama `-p:TraconFrontendEnabled=false` kalır; commit'ten sonra `HEAD`'e karşı değişiklik görmez | Tam koşumda UI'lı derle; `--taban` seçeneği |
| 🟢 | **F-357** · Kapı: `IValidateOptions<T>` kaydı `ValidateOnStart` olmadan kalmasın | F-336 taraması aynı anda iki vaka buldu (`VoiceOptions`, `OpenAILiveOptions`); ikisi de XML'de "başlangıçta" diyordu | Kaynağı tarayan mimari test (emsal `TimeoutSiteTests`): her kayıt için `AddOptions<T>().ValidateOnStart()` veya `ProviderRegistrationCore.AddValidatedOptions` aranır. Üçüncü vakadan önce |
| 🟢 | **F-358** · Konuşma sürücüsü sentezleyicinin karakter limitini okumuyor | Kod okuması, ölçülmedi (2026-10-12, F-336): `VoiceSpeechSegmenter.MaxSegmentLength = 240`; `VoiceConversationDriver.SpeakAsync` parçayı `ISpeechSynthesizer.MaxCharactersPerRequest` kontrolü olmadan gönderir; limiti 240'tan küçük bir sentezleyici sözleşme dışı istek alır | Parça boyu `min(240, MaxCharactersPerRequest)`; fonksiyonel test |
| 🟢 | **F-359** · `UseOpenAILive` doğrulayıcısı tüketicinin kendi `ILiveVoiceProvider`'ında da OpenAI anahtarı ister | Kod okuması (2026-10-12, F-336): `ValidateOnStart` ile bu yapılandırma başlangıçta düşer; senaryo yapay | Karar: belgele veya `VoiceProviderBinding` benzeri bağlama |
| 🟢 | **F-362** · Hiçbir modeli fiyatlı olmayan karma run satırı yeniden fiyatlamada agent modeliyle fiyatlanır | Kod okuması (2026-10-12, F-349, K-893 bilinen sınırı): depo model başına usage tutmaz; fiyatlı payı olan karma satır atlanır, fiyatsız karma satır tek model satırından ayırt edilemez | Model başına döküm sütunu veya "karma" işareti (migration + public API) |
| 🟢 | **F-363** · Embedding token'ları run usage'ına ve maliyetine girmez | Kod okuması (2026-10-12, F-349): `search_knowledge` sorgu embedding'i (`VectorSearchToolFactory.cs:55`) sayılmaz; embedding üreteci fiyatlı bir sağlayıcıya bağlı değildir (K-874 kapsam dışı bıraktı) | Embedding üretecinin sağlayıcı/fiyat kimliği gelince yan kanal payı olarak eklenir |
| 🟢 | **F-368** · Alt agent üst run'ın kültürünü almaz | Kod okuması (2026-10-12, F-351 sınıf taraması): `CallableAgentResolver` çocukları `culture: null` ile çözer; çok dilli bir üst run'ın çocuğu varsayılan talimatla koşar. `AgentRunScope` kültür taşımaz | Karar: `AgentRunScope.Culture` (eklemeli public API) ile miras ya da "çocuk varsayılan kültürle koşar" diye belgele |
| 🟢 | **F-373** · Kayıtlı `IAttachmentStorage` kalkınca retention `external_uri`'li satırı siler, bayt yetim kalır | Kod okuması (2026-10-12, F-332): depolama yoksa eski tek-ifade yolu | Satırı tut ve logla ya da koşumu düşür (karar) |
| 🟢 | **F-241** · Kırpma bir vekil (surrogate) çiftini ortadan kesebilir — sınıf | `EvaluatorRunJudge.ResolveVersion` sürümü `version[..MaxEvaluatorVersionLength]` ile kırpar. 128. karakter yüksek vekilse yalnız kalan vekil `varchar`/`nvarchar` yazımında kodlama hatası doğurur; hata yazma döngüsünde `retryable` sayılıp tekrarlanır. Aynı desen `TextValue` kırpmasında da var ([`EvaluatorRunJudge.cs`](../src/Tracon.Core/Evaluation/EvaluatorRunJudge.cs)). Olasılık çok düşük — informational version pratikte ASCII'dir | Sınıfı tek bir güvenli kırpma yardımcısına indir. Kırpılan bir alanın ASCII-dışı içerik taşıdığı ölçülürse aciliyet yükselir |
| 🟢 | **F-281** · `Tracon.Google` görsel yolunda `MediaType` verilirse SDK `NotSupportedException` atar | Canlı ölçüldü (2026-09-24, Google.GenAI 1.16.0, API anahtarı): `ImageConfig.OutputMimeType` her değer için *"outputMimeType parameter is only supported in Gemini Enterprise Agent Platform mode, not in Gemini Developer API mode"* ile reddedilir. `GoogleImageGenerator` (`src/Tracon.Google/Internal/GoogleImageGenerator.cs:77`) `ImageGenerationOptions.MediaType` verilince onu gönderir; Tracon'un iki sevk edilen yolu (`/api/images/generate`, `generate_image`) `MediaType` vermediği için bugün yalnız `IImageGenerator`'ı doğrudan çağıran tüketiciyi etkiler | Paket yalnız API anahtarı modunu desteklediği sürece `MediaType` istemde reddedilir ya da yok sayılır — hangisi olacağı küçük bir davranış kararıdır; karar sonrası düşen test + site cümlesi |
| 🟢 | **F-285** · Kırıcı kapının iki yanlış-mesaj yolu (Faz 187 denetimi) | (a) `PKV007` gibi taban kaydı gerçekte `IsBaselineSuppression` taşımıyorsa (ölçülen `PKV006` taşımıyor) `read_breaking_changes` onu "strict uyumsuzluk" diye etiketler, "tanınmayan kayıt" değil (`scripts/breaking_changes.py`, kural sırası); test `test_pkv007_kirmizi` ölçülmemiş biçimi kullanır. (b) Düşen TFM iç içe listeyle yazılırsa (üst madde TFM, alt maddeler paket) `_list_items` girintili `-` satırını yeni madde sayar ve eşleşme kaçar. İkisi de güvenli yön (kırmızı), yalnız mesaj yanlış | Gerçek bir `PKV007` raporu ölç ve kuralı ona göre sırala; iç içe maddeyi üst maddenin span'leriyle birleştir. F-266'nın `### Removed` maddesi tek madde biçimindeyse acil değil |
| 🟢 | **F-286** · Bütünüyle kaldırılan bir paket kırıcı kapıda hiç denetlenmez | Taban kümesi bugünkü `library_ids`'ten gelir (`scripts/kapi.py` `release_rehearsal`); tabanda olup HEAD'de olmayan paket için ne restore ne rapor olur (Faz 187 denetimi 🟢3) | Taban etiketindeki `src/*/*.csproj` kümesiyle HEAD'dekini karşılaştır; düşen paket kimliği notta code span olarak geçmeli |
| 🟢 | **F-287** · `dokuman_iddia_cakismalari` proje başına `TraconPublicApiTrackingEnabled=false` cümlesini de çelişki sayar | Faz 187 kapıyı gerçek anahtara çevirdi (`scripts/dokuman-bakim.py`); anahtar artık proje başına kapatmadır (K-424), "`Tracon.Client` `false` yazar" gibi doğru bir skill cümlesi bulgu olur. Bugün tetikleyen satır yok (denetim 🟢4) | İddia desenini varsayılana daralt ("varsayılan"/"default" ya da `src/Directory.Build.props` bağlamı) |
| 🟢 | **F-311** · Kapalı MCP oturumuna giden çağrı "TaskCanceledException" diye raporlanır | Ölçüldü (2026-10-11, F-305 repro): kapalı oturumdan giden `tools/call` için `ToolFailed` olay metni "Tool failed with TaskCanceledException." idi. Model "Error: Function failed." gördü, run `Completed` döndü; neden iptal değil kapalı bağlantıydı ve operatör onu iptal sanır. `{server}_read_resource` aynı durumda yanıltıcı "not among the resources declared" hatası verir. K-880'den sonra ana yol, F-310'dan sonra uzun ömürlü oturumlar bu duruma girmez; kalan yol derleme ile çağrı arasındaki yarış | MCP tool sarmalı (ya da `ExplainedFailureAIFunction`), çağıranın token'ı iptal değilken dispose edilmiş oturumdan gelen `OperationCanceledException`'ı "MCP connection to server '<ad>' is closed" diye açıklar; `read_resource` gövdesi kapalı bağlantıyı ayrıca söyler |
| 🟢 | **F-426** · Anthropic'in `model_context_window_exceeded` ve `pause_turn` bitişleri `Stop` sayılır | Ölçüldü (2026-10-13, Faz 204, Anthropic SDK 12.39.0 adaptörü decompile): `ToFinishReason` yalnız `refusal`→`ContentFilter`, `max_tokens`→`Length`, `tool_use`→`ToolCalls` eşler; geri kalanı `Stop` olur. Bağlam penceresine çarpıp kesilen yanıt `FinishReasonDetectingChatClient`'a tam görünür: yapılandırılmış yanıt "invalid JSON" ile reddedilir, düz metin olaysız `Completed` olur | Anthropic paketinde `RawRepresentation`'ın `stop_reason`'ını okuyup `Length`'e eşle (ya da ayrı sınıf kararı); önce sahte gövdeyle kırmızı test. Faz 204 devri: Core algılayıcısı yalnız MEAI değerini okur; düzeltme `AnthropicProviderSettingsChatClient` katmanına girer, `FakeAnthropicServer` gövdeyi verir |
| 🟢 | **F-428** · Model yargıcının bağlaması örnekleme denetiminden geçmez | Faz 204 denetimi 🟢3 (2026-10-13, kod okuması): `SamplingCapabilityCheck` yalnız agent tanımının bağlamasını ve yedeklerini okur. `ModelRunJudge` `Temperature`/`TopP`'yi `ModelRunJudgeOptions.Model`'den gönderir; `SupportsSampling = false` modele bağlı yargıç her örneklenen run'da `400` alır ve bunu ancak skor yazılmayınca gösterir. `CompactionSettings.SummarizationModel` örnekleme alanlarını göndermez, açık yalnız yargıçtadır | Yargıç seçeneklerinde başlangıç doğrulaması (`ValidateOnStart`) aynı yardımcıyı okur; fonksiyonel test |
| 🟢 | **F-429** · İş işçisi tekrarı aynı cevabı alacak model hatasını da yeniden dener | Faz 204 denetimi 🟡2 sınıf taraması (2026-10-13, kod okuması): işçi `NonRetryableJobException` dışındaki her hatayı `MaxAttempts`'e kadar tekrarlar. Değerlendirme işinde (`EvalJobHandler`, varsayılan 3 deneme) yargıç modelinin kesilen yanıtı veya kalıcı `4xx`'i işi tekrar koşturur; her deneme bitmemiş case'in agent run'ını ve yargıcını yeniden öder. Kuyruklu run, devam ve onay devamı aynı yoldadır ama varsayılanları 1 denemedir. F-338 düğüm retry'ını ve yedek zincirini kapattı, işçiyi değil | Tek karar: işçi ya da işleyici "tekrar aynı cevabı alır" hatasını (kalıcı `4xx`, `TraconOutputTruncatedException`) son deneme sayar; `NonRetryableJobException`'ın anlamı genişler veya yeni iç sinyal. Önce kırmızı fonksiyonel test |
| 🟢 | **F-430** · SQL Server upsert'i eşzamanlı yazımda deadlock kurbanı olabilir ve hatayı ham verir | Ölçüldü (2026-10-13, Faz 204 kapı koşumu): `RunScoreStoreContract.Concurrent_writes_with_null_and_empty_MessageId_leave_exactly_one_row` SQL Server'da tam derleme koşumunda bir kez `SqlException` 1205 (deadlock victim) ile düştü; izole beş koşumda geçti, önceki tam koşumda geçmişti. `SqlStoreContext.ExecuteUpsertAsync` işlemi (F-313) range kilidini tutar ama `SqlDialect.IsDeadlock`'u yeniden denemez; aynı yol ~20 store'dadır. Faz 204 SQL'e dokunmadı | Upsert idempotenttir: `ExecuteUpsertAsync` deadlock'u sınırlı sayıda yeniden dener (K-540'ın kuralı); kırmızı test için eşzamanlılığı artıran tekrar döngüsü |

### Faz planlama

| Aciliyet | Kalem | Neden | Plan notu |
|---|---|---|---|
| 🟠 | F-319 · Workflow HITL karar sözleşmesi | n revizyonlu onay; ikinci yanıt dal açar | Gövde: § *F-319* |
| 🟡 | **F-290** · `AuthorizationConfigurationKey`'ın `1.0.0` GA'da kaldırılması: veri taşıma + form geçişi | Faz 190 alanı `[Obsolete]` yaptı ve `1.0.0`'da kalkacağını `CHANGELOG`, XML ve `production.md`'de duyurdu (K-869). Üç iş kaldı: (1) `authorization_configuration_key` dolu satırları `header_configuration_keys["Authorization"]`'a taşıyan migration (üç sağlayıcı; iki alan birlikte doluysa ne olacağı karar ister); (2) MCP formu (`mcp.tsx`) eski alanı yazıyor — yeni alanı yazmalı, yoksa kaldırma formu kırar; (3) sütunu ve public üyeyi kaldırma, `McpTransportFactory`/`McpHeaderBuilder` eski yol, `#pragma warning disable CS0618` blokları (kapanışta 11 adet: `grep -rn "pragma warning disable CS0618" src tests`) | GA tarihine bağlı; GA öncesi son preview fazı olarak planlanır. Form geçişi (2) GA'dan bağımsız erken yapılabilir |
| 🟡 | F-247 · Yirmi dört yapılandırma bölümü için bağlama kanıtı | Risk yok, iş mekanik ve paralel yürür. Sınıf gerçek bir kusur üretti (`TraconImageOptions.Timeout`) | Gövde: § *F-247* |
| 🟡 | F-297 · Gemini konuşma adaptörü | Yeni bağımlılık istemez; Faz 203'ün sözleşmesini uygular | Gövde: § *F-297* |
| 🟡 | F-322 · Kiracı başına workflow eşzamanlılığı | Semafor süreç başına, kiracılara ortak | Gövde: § *F-322* |
| 🟡 | F-347 · OpenAI strict şema modu | Kiracı başka model seçer; strict istenemez | Gövde: § *F-347* |
| 🟡 | **F-411** · Onay Loop ölçütü ve denetim izi | Bakımcı kararı (2026-10-12): video hattındaki insan onayı ileride agent onayına döner ve Loop ölçütüyle yapılır. Faz 201 Açık Soru 7-A bu işi ayırdı: iterasyon olayında ölçüt kararı, yargıç modeli, agent sürümü (`LoopIterationCompleted` bugün bunları taşımaz). Faz 201 devri: onay ölçütü `AddLoopEvaluator(kind, factory)` ile servis alabilir; reviewer deseni çocuk run'ı `TraconRunOptions` ile ağaca bağlar ve derinlik ile `Budget.TryReserveRun()`'ı kendisi denetler (`DraftReviewCriterion`); olay yükü değişmedi | Faz 201'den sonra; tasarım onay ölçütüyle birlikte yapılır |
| 🟢 | **F-291** · Anahtar alanına değer yazan operatörün değeri `400` ve teslim `error`'ında yankılanır | Faz 190 denetimi 🟢2: `ConfigurationKeyGuard` mesajı (`ConfigurationKeyGuard.cs:112-114`) verilen adı olduğu gibi yazar; `{"X-API-Key":"sk-live"}` gibi bir hata değeri yanıta ve `webhook_deliveries.error`'a taşır. Tek alanlı `*ConfigurationKey` alanlarında da faz öncesinden beri aynı | Önek dışı adı yankılamadan reddet (yalnız alan adı + beklenen önek) ya da kimlik biçimli değeri maskele; dört yüzeyin (MCP, BYOK, webhook, trigger) testleri birlikte |
| 🟢 | F-248 · `unwrap(...) as Promise<T>` iddialarının tip düzeyinde kapısı | Bugün tek örnek elle tarandı; sonrakini kimse taramaz | Gövde: § *F-248* |
| 🟢 | F-249 · docs-site için tarayıcı tabanlı yerleşim kapısı | Elle koşulan case iki turda da kusur buldu | Gövde: § *F-249* |
| 🟢 | F-324 · Karışık zincirde agent adımının iletim modu | Loop bütün taslakları iletir; tip zinciri F-341 ile kapandı | Gövde: § *F-324* |
| 🟢 | **F-406** · Agent yolları konuşma sağlayıcısını adla seçsin | Faz 198 Açık Soru 5-C: Faz 198 adı yalnız fonksiyon düğümünde ve uçta seçtirir | Faz 198'den sonra; agent tanımına ses bağlaması |
| 🟢 | **F-407** · Bütçe tükenince fonksiyon düğümü başlamasın | Faz 199 kapsam dışı: davranış değişikliği (K1) | Faz 199'un maliyet kaydından sonra; ağaç bütçesi denetimi düğüm başlangıcına |
| 🟢 | **F-408** · Skill sürüm geçmişi ve geri alma | Faz 200 Açık Soru 6-B: skill geçmişi K-082'yi (kullanıcı kararı) açar, üç tablo ve özet liste ister (20 × 256 KiB kaynak), K-860 grant'ına dokunmamalı | K-082'nin yeniden açılma kararı; Faz 200'ün workflow deseni |
| 🟢 | **F-409** · Alt agent ve skill sürüm sabiti | Faz 200 kapsam dışı: agent → skill ve alt agent adımı güncel sürümle koşar (K-895 (c)); agent numarası silmede yeniden kullanılır, sabit bunu miras alır | Faz 200'den sonra; numara yeniden kullanımı önce kapanır |
| 🟢 | **F-410** · Kod Loop ölçütüne tanım başına tipli ayar | Faz 201 kapsam dışı: özel tür veri almaz — tek örnek bütün tanımlara hizmet eder (`LoopEvaluatorRegistry.BuildCriterion`, `default` dalı); konsol kod türü için alan göstermez ve tür değişiminde `marker`/`judgeCriteria`/`modes`'u temizler (`loop-section.tsx` `withKind`). Faz 195'in `Settings` deseni emsal; ayar `FindProblems`'a ve `GET /api/loop-criteria`'ya (şema) birlikte girer | Faz 201'den sonra |
| 🟢 | **F-417** · Görsel sağlayıcısı kiracının kendi anahtarını kullanabilsin | K-912 (2026-10-13): kiracının görsel sağlayıcısı için bağlaması varsa üretim reddedilir, çünkü sağlayıcı paketleri üreticiyi kurulum anahtarıyla kurar. Canlı ses `SupportsTenantCredentials` ile aynı işi çözdü (K-878) | Faz 198'in konuşma modeliyle birlikte: anahtarlı üretici fabrikası, OpenAI/Azure/Google görsel kaydı |
| 🟢 | F-420 · Önbellek yazma token'ı ve fiyatı | Faz 204'ten ayrıldı (2026-10-13); önce ölçüm | Gövde: § *F-420* |
| 🟢 | **F-427** · Canlı run akışı düz metin kesilmesini söylemez | Ölçüldü (2026-10-13, Faz 204, `OutputTruncationStreamingTests`): `update` çerçeveleri `finishReason` taşımaz; `tracon.output.truncated` yalnız run olaylarında (`GET /api/runs/{id}/events`) görünür. Akışlı istemci yarım yanıtı tam sanar | Karar: kota bildirimi deseniyle `done`'dan önce `custom` çerçevesi ya da son `update`'te bitiş sebebi (public SSE sözleşmesi). Faz 204 devri: olay `FinishReasonDetectingChatClient.RecordTruncatedTextAsync`'te yazılır; kırmızı test `OutputTruncationStreamingTests`'e girer |

### Doğrudan

| Aciliyet | Kalem | İş |
|---|---|---|
| 🟡 | **F-282** · `troubleshooting` sayfası ağırlık tavanında; yeni semptom girdisi eklenemiyor | Ölçüldü (2026-09-24, Faz 185): sayfa 58 994 B gzip, tavan 59 000 B (`docs-site/scripts/check-weight.mjs`) — 6 B boşluk. Faz 185'in iki kısa girdisi (NU1107/NU1608 ve "Tracon packages from more than one release") 59 850 B, tek başlığa indirilmiş hâli 59 335 B ölçüldü; ikisi de geri alındı, içerik `reference/versioning.md`'de. Kapının kendi yorumu "bir sonraki yükseltme yerine sayfayı böl" der. İş: sayfayı böl (ör. `## Build diagnostics and the agent map` bölümü ayrı sayfaya; semptom dizini, `sidebar.mjs`, TRC tanı kapısı birlikte), sonra Faz 185'in girdisini ekle. Tavanı yükseltmek ölçümle bile kapının kendi kuralına aykırıdır |
| 🟢 | **F-267** · CI tam koşumu hâlâ tek test projesiyle koşuyor | Yerel kapı `-maxcpucount:2`'dedir ([Faz 184](arsiv/fazlar/184-TEST-BEKLEME-VE-E2E-YAPISI.md); aynı makinede 1 işçi 863/879 sn, 2 işçi 607/658/524 sn). `ci.yml` bilerek 1'de: runner donanımı ve Windows ayağı ölçülmedi. İş: bir CI dalında `-maxcpucount:2` ile en az üç koşum. Yeşil ve süre kazancı ölçülürse `ci.yml` güncellenir; kırmızı çıkarsa gerekçe hafızaya yazılır. Dala push gerektirir — kullanıcı onayı |
| 🟢 | **F-288** · `ReleaseArtifactFixture` yerel `1.0.0-preview.1`'i paketler; geliştirici cache'ine girerse nuget.org'daki aynı kimliği gölgeler | Faz 187 planı ölçtü (2026-09-23): `~/.nuget/packages/tracon.core/1.0.0-preview.1/.nupkg.metadata` kaynağı yerel `artifacts/package/release`'ti. 2026-09-24'te o dizin yoktu (cache temizlenmiş). Yayın provası artık izole cache kullanır, etkilenmez (K-864); risk geliştiricinin kendi tüketici denemeleridir | Fixture'ın sabit sürümünü yayınlanmış hiçbir sürümle çakışmayan bir `0.0.0-...` damgasına çevir ya da restore'u izole cache'e al; `MixedVersionGraphTests` ile birlikte ölç |

### Tetik bekleyenler

| Kalem | Engel | Koşul ne zaman oluşur |
|---|---|---|
| **F-95** · Agent düzeyinde kesinti/devam kancası | MAF yolu kapalı (2026-09-05 ölçümü): kanca alpha pakette, native FFI taşır, kesinti/devam sözleşmesi sunmaz | Kalem yalnız F-141 üzerinden ilerler. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-178** · Model deneme (attempt) telemetrisi | Job/kuyruk metrikleri yarısı Faz 133'te kapandı; deneme telemetrisi yarısı için gerçek bir üretim fallback gecikmesi olayı ölçülmedi, tüketici bilerek erteledi | İlk fallback gecikme olayı ölçülünce. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-179** · Çalışma anı model yönlendirme policy'si | F-178 deneme süresini ölçmüyor; gerçek üretim trafiği yok | F-178 kapanır ve üretim trafiği oluşursa. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-180** · Tam paket koşumunda E2E zaman aşımı — sınıf açık | Vaka kapandı, **sınıf açık**: repro "yük altındaki tam paket koşumu"ydu ve kapanış onu kendi koşullarında tekrar koşmadı; test bir gün sonra aynı imzayla düştü. Ders `kusur-giderme` kapanış kontrolündedir | Sınıf yeniden görülürse **yeni** kusur kaydı açılır. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) |
| **F-199** · Kota eşiği bildiriminin claim SONRASI kaybı | Eşik claim edildikten SONRA webhook/akış yayını başarısız olursa o eşik dönem sonuna kadar kalıcı kaybolur. Düşük risk | Kota webhook/notice teslimi için retry/backoff istenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-200** · Çok kullanıcılı kota izolasyonu regresyon testi | Garanti **yapısaldır** (`RunEventWriter`'ın run başına özel `Guid`'i); eksik olan yalnız ona adanmış test | `RunEventWriter`/`RunRecordingAgent`'ın run izolasyonu yeniden düzenlenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-205** · `/v1/conversations/{id}` varlık asimetrisi | Kullanılmamış kimlik `200`, reddedilen kimlik `404`. Katı modda çağıran hangi id'lerin sahipsiz SATIR olduğunu sayabilir — erişim kapalı, yalnız varlık görünür. Davranış ucun rezervasyon semantiğinden gelir; kapatmak OpenAI uyumluluğunu bozar. `/api/sessions/{id}` bu sızıntıyı taşımaz | Tüketici varlık gizliliği talep ederse ([Faz 149](arsiv/fazlar/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md) denetim bulgusu) |
| **F-226** · SSE yanıtının şeması JSON şekli ilan ediyor | 7 `text/event-stream` yanıtının 2'si JSON şekli ilan ediyor; kütüphane OpenAPI transformer taşıyamaz (K-039) | Belgeden kod üreten üçüncü taraf bir üreteç kırılırsa (Faz 159 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-230** · `kurtarma.md` ↔ `.claude/settings.json` senkron kapısı | `KR-11` rampası `deny` listesinin bugünkü içeriğini **sayarak** tekrarlıyor (`git rebase`, `git clean -fd`, `rm -rf` listede yok). Yasağın sınırını yazmayan bir rampa yanlış güven üretir (K-761). Ama `settings.json` genişlerse cümle sessizce yalan olur ve bunu sayan kapı yok | `.claude/settings.json` `deny` bloğu ilk kez değiştiğinde ([Faz 168](arsiv/fazlar/168-KURTARMA-RAMPASI-KATALOGU.md) denetim bulgusu 🟢 6) |
| **F-233** · SQLite'ın yük altındaki `SQLITE_BUSY` davranışı ölçülmedi | Test yalnız ayarların kurulduğunu doğrular; yük testi yalnız PostgreSQL'de | SQLite çok yazarlı/yük altında desteklenmek istenirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-236** · Audit yazma politikası ratchet'i değişken adına bağlı | Kaynak deseni `IAuditLog log = …; log.WriteAsync(...)` şeklini görmez; tek örnek meşru | Bir çağrı yeri gerçekten kaçarsa (Faz 171 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-240** · Kapasite kapısının kalan üç dar açığı | [Faz 174](arsiv/fazlar/174-KAPASITE-DAMGASI-KAPISI.md) denetiminin 🟢 bulguları; (3) ve (4) 2026-09-24'te kapandı. Kalan: (1) `SCHEMAS.storage` ölçülmüş tekrar sayısını başlık dizesinde sabitliyor (`'Rows per run (3 repeats)'`); (2) `P95_SAMPLE_FLOOR = 100` ile `LatencyStatistics.P95SampleFloor` elle senkron, uyumu hiçbir şey ölçmüyor; (5) 19 işaret `docs-site/public/llms-full.txt`'e düz metin olarak sızıyor (emsal `claim:` zaten 11 tane sızdırıyor) | Kapasite ölçümü yenilendiğinde — o koşum (1) ve (2)'yi zaten elden geçirtir |
| **F-237** · Etkiden önceki dar fail-closed `run` kaydı | Genel `RecordingMode.Required` reddedildi (A10); dar bir `seam` bugün yok, kayıp görünür (K-782) | Düzenlemeye tabi bir kurulum kanıtla talep ederse; önce `tracon.run.recording_failures` ölçülür. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-238** · Metrik dinleyici test yardımcısının ÜÇ kopyası | Üç `internal` `MeterListener` sarmalayıcısı; K-483 eşiğinde | Dördüncü kopya gerekirse ya da `Tracon.Testing`'e yardımcı istenirse (Faz 173 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-242** · `ResolveVersion`'ın `catch` dalı kanıtlanmadı | Sürüm çözümünde attribute okuması **hata verirse** alan `null` kalır ve skor yine yazılır (K-790). Yalnız "attribute yok" yolu testle kanıtlı; `catch` dalını koşturmak `GetCustomAttribute`'u attıran düşmanca bir tip ister ve kazanç maliyeti karşılamıyor | Assembly attribute okumasının gerçekten attığı bir kurulum (kısıtlı host, bozuk assembly) raporlanırsa ([Faz 176](arsiv/fazlar/176-EVALUATOR-SURUM-DAMGASI.md) denetim bulgusu 🟢 2) |
| **F-243** · Analitik store yüzeylerinde metot bazında iptal kapsamı yok | Toplayıcı sorgular iptal sözleşmesinin metot bazında kapsamı dışında | Gerçek kaynak tüketimi raporlanırsa veya sınır gevşetilirse (Faz 177 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-245** · Sessiz bir `run` ile ölü bir bağlantı ekranda AYNI görünüyor | `SseDecoder` keep-alive yorumlarını düşürür; canlılık göstergesi bir arayüz kararıdır. Not: workflow akışı 2026-10-12'den beri keep-alive yazar (F-325) | Kullanıcı sessiz run'ı öldü sanarsa — **kullanıcı kararı gerekir**. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-246** · Sekiz kararlı hata kimliğinin karşılığı olan bir `RunErrorClass` YOK | 17 kimliğin 8'i bilerek eşlenmedi; yeni enum üyesi public sözleşme işidir | Operatör ayırt etmek isterse ya da taksonomi elden geçirilirken — **kullanıcı kararı gerekir**. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-250** · Kiracıya duyarlı örnek bir `IAgentSource` yok | `MT-CORE-095` iki turdur koşulamıyor; genişleme noktası sevk ediliyor, örneği yok | Bir tüketici kiracı başına agent kümesi sorarsa ya da case üçüncü turda da koşulamazsa. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-252** · Örnek uygulamanın demo kancaları bir kimlik sağlayıcısıyla değişmeli | K-834'ün dokuz demo kancası `samples/` altında | Örnek uygulama gerçek bir kimlik sağlayıcısına bağlandığında. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-253** · Konuşma paneli için sentetik ses sürücüsü | Panel `MediaRecorder` sentetik akıştan veri üretmez; iki case yalnız insanla koşulur | E2E'ye ses kapsamı istenirse veya tarayıcı bayrakları yapılandırılabilirse. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-254** · Kiracı egress politikası satırı YOKKEN hiçbir kısıt uygulanmıyor (fail-open) | [`ModelProviderRegistry.cs:287`](../src/Tracon.Core/Models/ModelProviderRegistry.cs#L287) `if (policy is not null)`. `policy is null ⇒ kısıt yok` ürünün belgelenmiş varsayılanıdır — politika kurmayan kurulum her sağlayıcıyı çağırabilir. Fail-closed yapmak `TraconTenantProviderOptions`'a varsayılanı kapalı bir anahtar ister (K1 sıfır-sürpriz; KG-034) | Kullanıcı fail-closed seçeneğine karar verirse — **kullanıcı kararı gerekir** (güvenlik varsayılanı) |
| **F-256** · Harf-kayması sözleşme senaryosu yalnız iki store'da | Senaryo paylaşılan tabanda değil; bellek içi store'lar kimliği normalleştirmez (K-839) | K-839 genişlerse senaryo tabana taşınır. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-263** · `ic-dongu`, paylaşımlı provider kaynağının HTTP kanıtını seçmiyor | `Providers.Shared` değişikliği fonksiyonel kanıtı iç döngüde koşturmaz; kapanış yakalar | İç döngüde provider kaçışı ölçülürse (Faz 181 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-264** · Azure kimlik hatası için manuel kabul case'i yok | Davranış yalnız fonksiyonel testle kanıtlı; örnek uygulama Azure'u yalnız kimlikle kaydeder | Örnek uygulamaya Azure demo kancası eklenirse (Faz 181 denetimi). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-274** · Üç SQL `Use*` metodundaki ~107 aynı kayıt satırı ortak bir iç yardımcıya taşınmadı | Parite testi kaçışı yakalar; kopya kalır | İkinci yatay değişiklik (sayım 2026-09-23'ten) ya da dördüncü SQL sağlayıcısı (K-176). Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-276** · npm kanalı test edilen derlemeyi değil kendi `npm run build` çıktısını yayınlıyor | Üç ayrı derleme; NuGet eşi Faz 191'de kapandı (K-871) | `tsc` çıktısında ilk sapma ya da ilk npm yayın kusuru. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-277** · Dış katkıcı için İngilizce mimari karar özeti yok | `ARCHITECTURE.md:59`, `:137-146` ve `CONTRIBUTING.md:114-116` dört Türkçe dokümana işaret eder; iki dosya da makine çevirisinin yeterli olduğunu söyler. K-408 (`docs/` Türkçe kalır, kullanıcı kararı) korunur | İlk dış PR veya issue mimari kararı Türkçe dokümandan okumak zorunda kalırsa ya da GA freeze turu (UR-003) |
| **F-374** · `jobs.payload` ve `job_items.input` korumalı sütun değil; kuyruklu run'ın parametre değeri ve belge metni orada düz durur | [Faz 194](arsiv/fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) (K-899, kullanıcı kararı): kuyruk yükünde mesaj zaten düz duruyordu; sütunu koruma kapsamına almak ayrı bir migration ve anahtar kararıdır (`jobs.payload` serbest metin, `ProtectedColumn` üyesi yok). Site bunu açıkça yazar | Düzenlemeye tabi bir kurulum kuyruk yükünün şifrelenmesini isterse — o zaman `ProtectedColumn` üyesi, iki sütun ve geriye dönük okuma (zarf kendini tanıtır) birlikte tasarlanır |
| **F-375** · Batch öğesinde düz metin ile `{"message":…}` biçimli nesne ayırt edilemiyor | Faz 194 (K-899): `JobPayload.ExtractItems` string ve nesne öğeyi aynı metne indirir; metin tür taşımadığı için `message` string üyeli bir JSON metni yapılı öğe sayılır (site belgeler). Tür sütunu migration ister ve gerçek çakışma ölçülmedi | Bir tüketici düz metin öğesinin yanlışlıkla yapılı sayıldığını bildirirse — o zaman `job_items`'a tür işareti eklenir |
| **F-376** · Paylaşılan boş parametre sözlükleri `Dictionary` tabanlı ve `GetRunParameters()` onları ya da kapsamın canlı sözlüğünü döner | [Faz 194](arsiv/fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) denetimi (🟢): `ParameterValueSet.Empty` ve `TraconWorkflowContextExtensions.NoValues` salt okunur görünür; `(Dictionary<,>)` dönüşümüyle yazan kod run'lar ve kiracılar arasında değeri bozar. Barındıran kod güvenilirdir; `ReadOnlyDictionary` sarmalı ucuzdur | Bir tüketici sözlüğü dönüştürerek yazdığını bildirirse ya da `GetRunParameters()` yüzeyi genişlerse |
| **F-377** · `ReplayTools` kipiyle parametreli tanımın replay'i için test yok | Faz 194 denetimi (🟢): yeni iç `CompileParameterizedAsync(…, toolTransform, …)` aşırı yüklemesi yalnız `NoTools` ve playback'li devam testinde dolaylı sınanıyor; gövde parametresiz yolla aynıdır | `AgentDefinitionCompiler` araç dönüşümü ya da replay tool kipleri değişirse |
| **F-378** · Agent batch'te `anyItemCarriesValues` yalnız `Pending` öğelerden hesaplanır | Faz 194 denetimi (🟢): kira devrinde yalnız düz öğeler kalmışsa F-330 ön kontrolü job'ı tek seferde düşürür; ilk denemede aynı öğeler tek tek `Failed` olurdu. Sonuç yine reddedicidir | Bir kullanıcı kira devrinden sonra beklenmedik toplu düşüş bildirirse |
| **F-379** · `InMemoryRunInputStore` 1000 run'da keser; bekleyen workflow'un değerleri o noktada kaybolur | Faz 194 denetimi (🟢): SQL'deki retention istisnası (K-898) bellek içi depoda yok; cevap reddedilir. Bellek içi dağıtım üretim hedefi değildir | Bellek içi depo üretimde kullanılırsa ya da sınır yapılandırılabilir yapılırsa |
| **F-380** · `POST /api/workflows/{name}/run` OpenAPI açıklaması parametre kapısını anmıyor | Faz 194 denetimi (🟢): `ProducesProblem(400)` var, açıklama metni yok. Metni değiştirmek OpenAPI belgesini, `Tracon.Client` ve `@tracon/client` çıktısını yeniden üretir | Workflow ucunun açıklaması başka bir nedenle değişirse; o zaman birlikte yazılır |
| **F-381** · Workflow `run_inputs` yazımı düşerse bekleyen run kalıcı olarak cevaplanamaz | Faz 194 denetimi (🟡, gerekçelendi): yazım best-effort kalır (K-898); ret mesajı artık depo hatasını adlandırır. `AwaitingInput`'a geçerken kayıt yoksa idempotent yeniden yazmak (`ON CONFLICT DO NOTHING` buna izin verir) run'ı kurtarırdı; kurtarma yolu yoktur ve satırı yaşa göre silen kural da yoktur | Depo hatası nedeniyle cevaplanamayan bir workflow run'ı görülürse (`tracon.run_recording.failures{stage=input}` artar) |
| **F-412** · Fonksiyon düğümünün ayar alanı şemadan üretilen bir forma dönüşmedi | [Faz 195](arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) (K-900, kullanıcı kararı): konsol bir JSON metin alanı ve şemanın üye listesini verir. Tür başına bir form (enum seçici, sayı alanı, iç içe nesne) şema yorumlayıcısı ister; bundle payı (199,9 KB, bütçe 250) ölçülmedi. Doğrulama sunucudadır, form bir kopya taşımaz | Bir operatör JSON'u elle yazarken tekrarlanan hata bildirirse ya da ayar tipleri iç içe nesne ve dizi üyeleri taşımaya başlarsa — o zaman `settingsSchema`'dan üretilen form ve bundle payı birlikte ölçülür |
| **F-413** · Konsol yapısal değişiklik uyarısını bekleyen run olmadan da gösteriyor | Faz 195 (K-901, Açık Soru 3-A): uyarı desen, adım veya sıra her değiştiğinde çıkar; hepsi ölçüldü ve eski checkpoint'i bozar (`WorkflowStructuralChangeResumeTests`). Yalnız bekleyen `AwaitingInput` run varken göstermek bir sayım çağrısı ister (ek uç ya da run listesinde workflow süzgeci) | Operatörler uyarıyı gürültü sayarsa ya da run listesi workflow adına göre süzülür hâle gelirse |
| **F-414** · Konsol 2^53 − 1'den geniş bir tamsayı ayarını kayıpsız taşıyamıyor | Faz 195 denetimi (🟡, gerekçelendi): konsol JSON sayısını `double` okur; `9007199254740993` açılıp kaydedilince `…992` olur, ayar tipi `NumberHandling.Strict` olduğundan `long` üyeye metin de yazılamaz. Sınır XML'de ve sitede yazılı: bu genişlikteki kimlik `string` bildirilir. Kayıpsız taşıma özel bir JSON ayrıştırıcısı ve yazıcısı ister (`JSON.parse` kaynak metni her tarayıcıda yok) | Bir tüketici 53 bitten geniş sayısal ayar bildirirse — o zaman ya ayarı ham metin olarak taşıyan bir editör ya da tarayıcı `JSON.parse` kaynak desteği oturunca onun kullanımı |
| **F-415** · Tüketicinin dönüştürücüsünün (`JsonConverter`) fırlattığı `JsonException` mesajı `400`'e ve `runs.error`'a girer | Faz 195 denetimi (🟢): setter ve kurucu istisnasının mesajı yanıta girmez (K-900), ama tüketicinin kendi `JsonConverter`'ı `JsonException` ya da `NotSupportedException` fırlatırsa mesaj serileştiricinin cümlesi gibi aktarılır ve değeri taşıyabilir. Kayıt yolunda uyarı logu da yazılmaz (`WorkflowFunctionSettings.Deserialize`, `IsRaisedByConsumerCode`) | Bir tüketici ayar tipinde özel dönüştürücü kullanır ve mesajının değer taşıdığı görülürse — o zaman iki istisna türü de setter istisnası gibi ele alınır |
| **F-422** · `openai-responses` harness turu cevabını akışta iki kez yayabilir — ölçüm bekliyor | [Faz 201](arsiv/fazlar/201-LOOP-DUZENLEME-VE-OLCUT-FABRIKASI.md) örnek uygulama koşumu (2026-10-13, `gpt-5.4-mini`): `aiJudge` + kod ölçütlü loop'lu agent'ın üç koşumunun ikisinde ilk turun metni `MessageDelta` dizisinde token token iki kopya; arada tool çağrısı ya da başka olay yok. Loop'suz harness 0/3. Modelin turda iki mesaj yazması mı, MAF/MEAI'nin aynı içeriği iki kez yayması mı ayırt edilmedi. Tracon tarafı: ölçüt deseni cevabı son mesajdan okur (`hafiza/maf-harness-ve-loop.md`) | Akışta yinelenen metin bildirilirse ya da `openai-responses` istemcisi güncellenirse — o zaman aynı tanımla N koşum: ham Responses olayları (sağlayıcı istemcisi düzeyi) ile `AgentResponse.Messages` sayısı karşılaştırılır; tekrar MEAI/MAF'ta ise kusur kaydına döner, modeldeyse kapanır |

### F-247 · Yirmi dört yapılandırma bölümü için bağlama kanıtı

**Kanal:** faz planlama · 🟡

**Sorun:** `TraconOptionsBindingCoverageTests` "alan eklendi ama `Bind()`'a
yazılmadı" kusurunu yapısal olarak kilitler — ama yalnız `TraconOptions`
**ağacı** için. Ölçüldü (2026-09-19): `TraconOptions` bu bölümlerin ebeveyni
**değildir**. `Tracon:Scheduling`, `Tracon:Quotas`, `Tracon:Retention` ve 21
tanesi daha kök seviyede **kardeş** bölümlerdir ve tek tek bağlanırlar;
tarayıcı hiçbirine ulaşmaz. Bu şeklin ürettiği kusur ölçüldü:
`TraconImageOptions.Timeout` eklendi, yapılandırılabilir olarak belgelendi ve
hiçbir yere bağlanmadı — canlı koşum yakaladı, hiçbir test yakalamadı
(`HATA-S1-010`).

**Kapsam:** Yirmi dört bölümün her biri için `<TypeName>BindingTests`: her
skaler alan tek bir yapılandırma kümesiyle doldurulur ve geri okunur. Kapsam
zaten kilitli — `OptionsSectionCoverageTests` bir **ratchet**'tır ve
`options-section-coverage-baseline.txt` bugün eksik olan yirmi dördü adıyla
sayar; yeni bir bölüm eklenemez, var olan biri listeden ancak testini
kazanınca çıkar. Bu aday o listeyi **boşaltmaktır**.

**Değer:** Sessizce bağlanmayan bir ayar, dokümanın vaat ettiği ama ürünün
yapmadığı şeydir; tüketici bunu yalnız canlı koşumda görür.

**Hazırlık — ölçüldü (2026-09-19):** Ratchet ve taban çizgisi yerinde;
`TraconImageOptionsBindingTests` ile `TraconPricingBindingTests` izlenecek
deseni gösteriyor. İş mekaniktir ve paralelleştirilebilir.

**Maliyet:** Ölçülmedi. Yeni ürün kodu gerekmez; yirmi dört test sınıfı.

**Risk:** Yok — yalnız test eklenir.

---

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
| **Ölçüm bekliyor — F-ID'leri** | F-51 · F-94 · F-96 · F-97 · F-99 · F-101 · F-123 · F-128 · F-154 · F-156 · F-157 · F-159 · F-160 · F-161 · F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. Kanıt üretmeden aday olmaz |
| **Ölçüm bekliyor — A-ID'leri** | A01 (kalan manifest bağlama) · A02 · A03 · A05'in ETag ve provenance dilimleri · A06 · A07 · A08 · A09 · A10 · A11 · A12 · A13 · A14 · A15 · A17 · A18 | 🚨 Bunlar **F-NN değildir** — 2026-09-07 tüketici analizi turunun rapor içi izleme kimliğidir; biri seçilirse o zaman aday numarası alır. Değer/maliyet/risk yargısı ve gereken kanıt [tam raporun](arsiv/incelemeler/2026-09-07-tuketici-analizi-codebase-olcumu.md) §5'indedir. **A17/A18 mimari ret DEĞİLDİR** — mevcut yetenekle çözülemeyen somut bir vaka çıkarsa yeniden değerlendirilirler |

### F-ID tahsis kuralı

Numara **geri dönüştürülmez** ve bir numara **tek kaleme** aittir. Sıradaki
numara: **F-431**.

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
