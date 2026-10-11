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

**Durum (2026-10-11):** sıralanabilir kuyrukta **20** aday var (F-294…F-303
podcast turu, F-318…F-324 ve F-345…F-347 Explainer-Video turu). Bugün yapılabilecek iş § *Bekleyen Kalemler*'in üç
kanal tablosundadır. Planlanmış fazlar burada değil, üretilen
[`YOL-HARITASI.md`](YOL-HARITASI.md)'dedir.

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

Her aday gövdesi şu alanları taşır: **Sorun · Kapsam · Değer · Mercek ·
Hazırlık · Maliyet · Risk · Bağımlılık · Ekosistem · Karşı görüş.** Bir alan
ölçülmediyse öyle yazılır; boş bırakılmaz.

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
([keşif notu](kesif/2026-10-11-podcast-tuketici.md)) ve Explainer-Video
([keşif notu](kesif/2026-10-11-explainer-video-tuketici.md)) turlarından gelir; kanal ve aciliyet
§ *Faz planlama* tablosundadır.

### F-294 · Workflow run parametreleri agent `{{param}}`'larına ve fonksiyon düğümlerine ulaşsın

**Kanal:** faz planlama · 🟠

**Sorun:** Agent run'ı `Parameters` taşır ve önbelleksiz bağlar (`AgentEndpoints.cs:823-836`). Workflow run'ı yalnız `Message` ve `SessionId` taşır (`WorkflowContracts.cs:81-90`, `IWorkflowRunner.cs:121-144`); `src/Tracon.Workflows/Internal/` parametreye dokunmaz. Katılımcı katalogdan, talimatı bağlanmadan derlenir (`AgentDefinitionCompiler.ChatOptions.cs:115-119`).

**Kapsam:** Run isteğine `Parameters`; agent adımlarına bağlama; fonksiyon düğümüne `IWorkflowContext` üzerinden okuma; resume aynı değerleri görür. Şema kaynağı (workflow mu, katılımcılar mı) karar ister.

**Explainer turu eki (2026-10-11):** kuyruklu agent run'ı ve batch öğesi de `Parameters` ve `Documents` taşır (bugün 400: `AgentEndpoints.cs:1007-1015`). Bağlanmadan koşma kusuru F-330'dur.

**Değer:** Bölüm başına ton, süre, revizyon sayısı tanım kopyalanmadan verilir; çok kiracılı host tek tanımı parametreyle sürer.

**Mercek:** 1, 3, 5.

**Hazırlık:** Agent tarafı hazır (`InstructionParameterBinder`, `AgentParameterValidator`); workflow tarafı sıfırdan.

**Maliyet:** İki public istek alanı; checkpoint'e parametre yazımı (olası migration).

**Risk:** Parametreli derleme önbelleği atlar — maliyeti ölçülmeli; resume'da kayıp.

**Bağımlılık:** [Faz 192](arsiv/fazlar/192-DONGU-TUKENME-POLITIKASI.md) ile (F-293; revizyon sayısı parametre olursa `MaxIterations`'a bağlanır).

**Ekosistem:** Taranmadı (Tracon içi boşluk).

**Karşı görüş:** Değerler mesaja JSON olarak gömülebilir; ama talimat şablon olmaktan çıkar ve doğrulanmaz.

### F-295 · Fonksiyon düğümüne veri ayarı (JSON + şema)

**Kanal:** faz planlama · 🟠

**Sorun:** `WorkflowNodeReference` yalnız `Name` ve `Kind` taşır (`WorkflowNodeReference.cs:22-33`); fonksiyon yalnız girdi ve `IWorkflowContext` alır (`TraconWorkflowFunctionExtensions.cs:89`). Aynı fonksiyonu iki workflow'da farklı ses, LUFS hedefi veya TTS sağlayıcısıyla kullanmak kodda ayrı kayıt ister.

**Kapsam:** Düğüm başına `Settings` (JSON), kod kaydında isteğe bağlı şema, kaydetmede doğrulama, fonksiyonda tipli okuma. Ayar veridir, ifade değildir — K2 ile uyumlu.

**Değer:** Workflow başına sağlayıcı ve ses seçimi operatörün düzenlediği veri olur.

**Explainer turu eki (2026-10-11):** operatör template'i ve motoru canlı günceller; değer artar.

**Mercek:** 1, 5, 6.

**Hazırlık:** JSON şema doğrulama deseni var (`AgentParameter`).

**Maliyet:** Public alan (blob, migration yok) + kayıt aşırı yüklemesi; editör F-292 ile.

**Risk:** Ayar bir mini dile dönüşürse K2 aşınır — yalnız değer, ifade yok.

**Bağımlılık:** F-292 (konsol düğüm düzenleme).

**Ekosistem:** Taranmadı.

**Karşı görüş:** Sağlayıcı bugün düğüm adıyla seçilebilir (`tts-elevenlabs`, `tts-gemini`); seçenek azken ayar alanı fazla yüzeydir.

### F-296 · Konuşma sözleşmesi v2: çok konuşmacılı diyalog, ses ayarları, uzun metin

**Kanal:** faz planlama · 🟡

**Sorun:** `ISpeechSynthesizer.SynthesizeAsync(SpeechRequest)` istek başına tek ses taşır (`SpeechContracts.cs:27-47`; `SpeechModels.cs:6-44`: `Text`, `VoiceId`, `ModelId`, `OutputFormat`, `LanguageCode`, `IncludeTimestamps`); istek başına 5.000 karakter (`VoiceOptions.cs:62`). Ses ayarı, konuşmacı listesi ve uzun metni bölüp birleştirme yok.

**Kapsam:** Konuşmacı-tur listesi, sağlayıcıdan bağımsız ses ayarları, yetenek bildirimi (en çok konuşmacı, karakter sınırı), bölme ve birleştirme. Tek sesli yol korunur.

**Explainer turu eki (2026-10-11):** tek anlatıcılı tüketici diyalog değil süreklilik ister. Önce istek ayarları dilimi: `seed`, `previous_text`/`next_text`, `previous_request_ids` (≤ 3), `voice_settings`, yetenek bildirimi (alanlar 2026-10-11 doğrulandı); diyalog sonra.

**Değer:** İki konuşmacılı bölüm, tüketici kendi TTS katmanını yazmadan üretilir.

**Mercek:** 1, 3, 6.

**Hazırlık:** İki sağlayıcı da diyalog sunar (Ekosistem).

**Maliyet:** `Tracon.Abstractions`'ta yeni public tipler; ElevenLabs adaptörü; birleştirme için format bilgisi (PCM/WAV kolay, MP3 değil).

**Risk:** Sınırlar farklı (2 ve 10 konuşmacı); en küçük ortak payda sözleşmeyi zayıflatır — yetenek bildirimi şart.

**Bağımlılık:** F-297, F-298 buna dayanır.

**Ekosistem (2026-10-11):** ElevenLabs `POST /v1/text-to-dialogue`: en çok 10 farklı ses, istek başına ≤2.000 karakter, `stability`/`similarity`, varsayılan `eleven_v3`. Gemini TTS: istekte en çok 2 konuşmacı, 24 kHz mono 16-bit PCM/WAV (Google dokümanı, güncelleme 2026-10-01).

**Karşı görüş:** Ses üretimi çekirdek iş değil; tüketici SDK'yı bir fonksiyon düğümünde çağırabilir. Tracon'un asıl katkısı maliyet kaydı ve kiracı anahtarıdır (F-298, F-299) ve sözleşmesiz de verilebilir.

### F-297 · `Tracon.Google` için Gemini konuşma (TTS) adaptörü

**Kanal:** faz planlama · 🟡

**Sorun:** `src/Tracon.Google/` konuşma yüzeyi taşımaz (2026-10-11 taraması); kullanıcı Gemini kullanıyor.

**Kapsam:** `ISpeechSynthesizer` (F-296 sonrası diyalog) uygulaması; `SpeechConfig`/`MultiSpeakerVoiceConfig` eşlemesi; WAV/PCM çıktı.

**Değer:** ElevenLabs'in yanında ikinci sağlayıcı; workflow başına seçim gerçek olur.

**Mercek:** 3, 6, 8.

**Hazırlık:** Yeni bağımlılık yok: `Google.GenAI` 1.16.0 referanslı (`Directory.Packages.props:144`); `MultiSpeakerVoiceConfig.SpeakerVoiceConfigs`, `SpeakerVoiceConfig.Speaker`, `PrebuiltVoiceConfig.VoiceName` var (`maf-api-kesfi` dökümü, 2026-10-11).

**Maliyet:** Paket içi kayıt uzantısı; canlı doğrulama için anahtar.

**Risk:** Model adları önizleme; API anahtarı modu sınırları (F-281 deseni).

**Bağımlılık:** F-296; F-298 olmadan iki sağlayıcı aynı süreçte seçilemez.

**Ekosistem (2026-10-11):** Modeller `gemini-3.8-flash-tts`, `gemini-3.8-flash-lite-tts`, `gemini-3.1-flash-tts-preview`, `gemini-2.5-pro-preview-tts`; .NET örneği yok.

**Karşı görüş:** F-298 olmadan adaptör ElevenLabs'in yerine geçer, yanına gelmez; tek başına değeri düşük.

### F-298 · Adlı konuşma sağlayıcı kaydı ve kiracı başına ses anahtarı

**Kanal:** faz planlama · 🟠

**Sorun:** `UseVoice` tek bir `ElevenLabsSpeechClient`'ı `ISpeechSynthesizer` olarak kaydeder (`VoiceBuilderExtensions.cs:76-85`); anahtar süreç geneli (`VoiceOptions.cs:35`). Sağlayıcı adla seçilemez; kiracının egress politikası ve kendi anahtarı konuşma çağrısına uygulanmaz.

**Kapsam:** Konuşma sağlayıcıları için adlı kayıt, kiracı bağlaması (`tenant_provider_bindings` deseni) ve egress denetimi; fonksiyon düğümü ve `speak` tool'u sağlayıcıyı adla ister. `UseVoice` korunur (K1).

**Explainer turu eki (2026-10-11):** aynı süreçte yerel GPU ve bulut motoru, kiracı başına seçim; aciliyet 🟠. Anahtar sahibi host'tur (iki turda da); kiracı BYOK ikincil. Önce F-336 kapanır. Emsal: görsel yolunun keyed kaydı (`ImageGeneratorResolver.cs:28-38`).

**Değer:** Çok kiracılı SaaS'ta ses kiracının faturasına gider; yasak sağlayıcıya veri çıkmaz.

**Mercek:** 3, 6, 8.

**Hazırlık:** Model BYOK/egress altyapısı yeniden kullanılır (K-466, K-467, K-874).

**Maliyet:** Yeni public kayıt yüzeyi.

**Risk:** Adlı ve adsız iki kayıt yolu birlikte yaşar; `TryAdd` sırası.

**Bağımlılık:** 2026-08-27 ses turunun B3'üne (kiracı ses politikası) komşu: B3 sesi yetkilendirir, bu kalem sağlayıcıyı ve anahtarı seçer.

**Ekosistem:** Taranmadı (Tracon içi simetri boşluğu).

**Karşı görüş:** Kiracı başına ses anahtarı ihtiyacı ölçülmedi; host kendi anahtarıyla üretip maliyeti dağıtabilir.

### F-299 · Fonksiyon düğümünün token dışı maliyeti kaydedilsin, kotaya girsin

**Kanal:** faz planlama · 🟡

**Sorun:** `TraconToolUsage.Report` çağrı kimliğini `FunctionInvokingChatClient.CurrentContext`'ten okur, yoksa `false` döner (`TraconToolUsage.cs:47-58`). Fonksiyon düğümünde bu bağlam yok: TTS karakter maliyeti yazılmaz, kota ve run bütçesi onu görmez (önceki tüketici turunun A08 ses notu).

**Kapsam:** Fonksiyon düğümü için usage raporlama (birim, miktar, maliyet, tahmini mi) ve `run` maliyetine/kotaya katılma kuralı.

**Explainer turu eki (2026-10-11):** birden çok birim (TTS karakteri, GPU saniyesi, render saniyesi), sağlayıcı/model alanı, ayrı toplam ve konsol gösterimi (F-342).

**Değer:** Bölüm başına gerçek maliyet; kiracı kotası TTS'i de sayar.

**Mercek:** 2, 7, 8.

**Hazırlık:** `ToolCallUsage` modeli var; workflow yolu yok.

**Maliyet:** Public raporlama girişi; kalıcı kayıt gerekirse migration (ölçülmedi).

**Risk:** Para birimi karışımı; tahmini maliyetin kotaya girmesi kararı.

**Bağımlılık:** F-298 ile fiyat kaynağı paylaşılır.

**Ekosistem:** Taranmadı.

**Karşı görüş:** Maliyet tüketicinin domain kaydında tutulabilir; değer yalnız Tracon kotasını kullanan host içindir.

### F-300 · Workflow ve skill tanımlarında sürüm geçmişi ve geri alma

**Kanal:** faz planlama · 🟡

**Sorun:** Agent tanımı sürüm geçmişi ve fark taşır (`IAgentDefinitionStore.cs:42,69`; `AgentEndpoints.cs:138,164`). Workflow ve skill store'ları yalnız `Get`/`List`/`Save`/`Delete` taşır (`IWorkflowDefinitionStore.cs:20-51`, `IAgentSkillStore.cs:18-45`); operatörün düzenlediği bir tanımın önceki hâline dönüş yolu yok.

**Kapsam:** İki store'a sürüm satırı, liste/getir/fark uçları, geri alma (yeni sürüm olarak), konsolda geçmiş paneli.

**Explainer turu eki (2026-10-11):** workflow kök run'ına tanım sürümü damgası ve resume'da sürüm denetimi. Operatör şemayı canlı günceller; üretim boyunca agent sürümü sabit kalmalı (kusur F-331). L43 ilgisiz.

**Değer:** "Dünkü düzenleme bozdu" anında geri dönüş.

**Mercek:** 2, 3, 7.

**Hazırlık:** Agent sürüm deseni (sözleşme, uçlar, konsol farkı) var.

**Maliyet:** Üç SQL lehçesinde migration (`kalıcı-veri`), sözleşme testleri, store arayüzlerine yeni üyeler (public API).

**Risk:** Üçüncü taraf store'lar yeni üyeleri uygulamalı — ayrı arayüz mü, varsayılan üye mi kararı.

**Bağımlılık:** Yok.

**Ekosistem:** Taranmadı.

**Karşı görüş:** Host tanımları kodla veya IaC ile yönetirse geçmiş git'tedir; değer yalnız konsoldan düzenleyen operatör içindir.

### F-301 · `AddLoopEvaluator` için `IServiceProvider` alan fabrika aşırı yüklemesi

**Kanal:** faz planlama · 🟢

**Sorun:** `AddLoopEvaluator(kind, LoopEvaluator)` yalnız hazır örnek alır (`TraconBuilderExtensions.Evaluation.cs:148`; `TraconLoopEvaluatorRegistration.cs:22`). Katalogdan ayrı bir reviewer agent'ı çağıran veya tüketici servisi kullanan ölçüt DI'a ulaşamaz.

**Kapsam:** `AddLoopEvaluator(kind, Func<IServiceProvider, LoopEvaluator>)`; örnek derleyici kurulurken çözülür.

**Değer:** Reviewer'ı kendi talimatı ve modeliyle ayrı agent olarak çalıştıran kalite kapısı kodla kurulur.

**Mercek:** 5, 6.

**Hazırlık:** Builder deseni hazır.

**Maliyet:** Bir public aşırı yükleme.

**Risk:** Singleton fabrika kiracıyı çalışma anında ambient `ITenantContext`'ten okumalı.

**Bağımlılık:** Yok.

**Ekosistem:** Taranmadı.

**Karşı görüş:** Yerleşik `aiJudge` (operatörün düzenlediği `JudgeCriteria`/`JudgeInstructions`) çoğu kapıyı karşılar; ayrı reviewer yalnız tool kullanan inceleme için gerekir.

### F-302 · Konsolda harness `Loop` düzenleme

**Kanal:** faz planlama · 🟡

**Sorun:** Editör `harness` nesnesini olduğu gibi taşır (`agent-editor/model.ts:201,248`), ama `harness-section.tsx` yalnız `maxContextWindowTokens` ve `maximumIterationsPerRequest` alanlarını düzenler (`harness-section.tsx:42,55`); `loop` için kontrol yok.

**Kapsam:** Ölçüt listesi (yerleşik dört tür + kodla kayıtlı türler), `MaxIterations`, `FreshContextPerIteration`, `aiJudge` alanları; doğrulama raporu. Kod yazdırmaz — K2 ile uyumlu.

**Değer:** Operatör revizyon sayısını ve yargıç ölçütlerini konsoldan ayarlar.

**Mercek:** 1, 2.

**Hazırlık:** Kodla kayıtlı türleri listeleyen bir uç gerekebilir (ölçülmedi).

**Maliyet:** Arayüz bölümü, iki dil sözlüğü, `full-replace-editors.test.tsx` vakası; bundle bütçesi.

**Risk:** Düşük.

**Bağımlılık:** [Faz 192](arsiv/fazlar/192-DONGU-TUKENME-POLITIKASI.md) `OnExhausted` / `OnCriterionError` alanlarını ekler; editör onları da gösterir.

**Ekosistem:** —

**Karşı görüş:** Host tanımları API'den yönetirse konsol editörü ikincildir.

### F-303 · Fonksiyon düğümüne zaman aşımı

**Kanal:** faz planlama · 🟡

**Sorun:** Bir fonksiyon düğümü sonsuza kadar çalışabilir; yalnız workflow'un genel `RunTimeout`'u (varsayılan 10 dk, `TraconWorkflowOptions.cs:54`) onu keser (K-497). 10–20 dakikalık bölümün TTS ve ses işleme düğümleri dakikalar sürer; genel sınırı büyütmek her düğümün sınırını büyütür. K-497'nin koşulu ("ihtiyaç ölçülürse") bu tüketicide oluştu.

**Kapsam:** Düğüm başına zaman aşımı (kayıtta varsayılan, F-295 ayarıyla geçersiz kılma); aşımda düğümü iptal edip run'ı ayırt edilebilir hatayla bitirme (K-497'nin önerdiği `TimeoutAIFunction` deseni).

**Explainer turu eki (2026-10-11):** agent düğümü süresi (`ChildDeadline` kurulum geneli 2 dk) ve workflow başına `RunTimeout`; aşımda run düşer (F-328).

**Değer:** Takılan bir dış servis bütün run'ı tutmaz.

**Mercek:** 2, 5.

**Hazırlık:** Tool timeout deseni var (Faz 69).

**Maliyet:** Kayıt aşırı yüklemesi; zamanlayıcı aralık doğrulaması şart (F-280 sınıfı).

**Risk:** İptali onurlandırmayan fonksiyon yine takılır — belgelenmeli.

**Bağımlılık:** F-280 ile aynı aralık yardımcısı.

**Ekosistem:** —

**Karşı görüş:** Tüketici fonksiyonunda `CancelAfter` kullanabilir; çerçeve desteği yalnız tek tip davranış ve görünürlük ekler.

### F-318 · Workflow `run`, `resume` ve `respond` bağlantıdan bağımsız koşsun

**Kanal:** faz planlama · 🟠

**Sorun:** Workflow run'ı onu okuyan HTTP akışına bağlıdır; okuyucu düşerse run `Canceled` olur (K-845; `WorkflowEndpoints.cs:443-455,738-799`). Agent yolundaki `Prefer: respond-async` workflow'da yoktur. `tracon.workflow` job'u yalnız yeni run başlatır, bekleyen run'a yanıt veremez (`WorkflowJobHandler.cs:56-60`).

**Kapsam:** Üç uca `Prefer: respond-async`: run id önceden üretilir, `Queued` satırı açılır, 202 + `Location` döner. `WorkflowJobHandler`'a resume ve respond iş türü; job yükünde kullanıcı ve etiket. İzleme `GET /api/runs/{id}/events`.

**Değer:** Saatlik üretim ve makine yanıtı bağlantı tutmaz; konsol uzun run'ı arka planda başlatır.

**Mercek:** 2, 3, 6.

**Hazırlık:** Agent eşi var (`TraconAsyncRunOptions`, `JobHandlerKeys.ApprovalResume`).

**Maliyet:** Public HTTP sözleşmesi (OpenAPI, istemci yenilemesi), job yükü şeması; migration beklenmez (ölçülmedi).

**Risk:** At-least-once: kira kaybında öğe iki kez koşar (önce F-326). Kuyruğa giren respond için kota kapısı kararı (K-394).

**Bağımlılık:** F-325 (keep-alive) ayrı kusurdur; F-319 (tek yanıt) respond kuyruğundan önce gelir.

**Ekosistem:** Taranmadı (Tracon içi simetri boşluğu).

**Karşı görüş:** Tüketici public `IWorkflowRunner`'ı kendi job handler'ında çağırabilir; ama o yolda `IRunAuthorizationHandler` kapısı çalışmaz (`WorkflowEndpoints.cs:621-627`).

### F-319 · Workflow HITL karar sözleşmesi: kiracı geneli liste, opt-in tek yanıt, karar denetimi, süre

**Kanal:** faz planlama · 🟠

**Sorun:** İstekler yalnız run başına listelenir (`WorkflowEndpoints.cs:175-186`). K-130 gereği yanıtlanan run `AwaitingInput` kalır; gerçekten bekleyen ayrılamaz. İkinci yanıt yeni dal açar; `respond` `Idempotency-Key` almaz. Yanıt denetim izine yazılmaz, tool onayı yazar (`ApprovalEndpoints.cs:238`). İstek için süre yoktur.

**Kapsam:** Kiracı geneli bekleyen istek listesi (sayfalı, "yanıtlandı" işaretli). Workflow başına opt-in tek yanıt: aynı cevap aynı devamı sürer, zıt cevap 409 (tool onayı kuralı). `workflow.respond` denetim kaydı (aktör, requestId, karar özeti hash'i) ve yeni run'a kaynak run bağı. İsteğe bağlı süre ve süre dolunca kapanış.

**Değer:** Host ekranı n revizyonlu onayı tek listeden yönetir; çift tıklama ikinci render üretmez; "kim neyi onayladı" cevaplanır.

**Mercek:** 2, 3, 5.

**Hazırlık:** Tool onay posta kutusu (Faz 55) ve fail-closed denetim deseni var.

**Maliyet:** Yeni uç ve public yanıt tipi, `RunEventType` üyesi; olası projeksiyon tablosu (migration; K-128'i açar).

**Risk:** K-128: kiracı geneli olay taraması pahalı olabilir — önce ölçülür. K-130 varsayılanı korunur.

**Bağımlılık:** F-327 (istek kaybı) ve F-335 (konsol) önce kapanır.

**Ekosistem:** MCP Tasks `input_required` aynı ihtiyacı protokolde karşılar; Tracon görev deposu onu boş bırakır (`RunBackedMcpTaskStore.cs`, 2026-10-11).

**Karşı görüş:** Onay tüketicinin ekranında ve domain'inde tutulabilir; değer yalnız Tracon HITL'ini kullanan host içindir.

### F-320 · Kesilen model çıktısı ayrı bir sonuç olsun ve derleme uyarısı versin

**Kanal:** faz planlama · 🟡

**Sorun:** `ChatFinishReason.Length` okunmaz; yalnız `ContentFilter` yakalanır (`ContentFilterDetectingChatClient.cs:45-81`). Doğrulama kapalıysa kesik JSON `Completed` döner; açıksa "not valid JSON" ile reddedilir ve onarım aynı sınıra yine çarpar. Anthropic varsayılanı 4096'dır (`AnthropicProviderOptions.cs:55`); 44 KB içerik kesilir.

**Kapsam:** `Length` tespiti, run olayı ve `RunErrorClass` üyesi (`OutputTruncated`); bu sınıfta onarım koşmaz. Derlemede `JsonSchema` + boş `MaxOutputTokens` + düşük sağlayıcı varsayılanı için uyarı; binding değeri `ModelDescriptor.MaxOutputTokens` ile karşılaştırılır.

**Değer:** Uzun yapılandırılmış çıktının neden düştüğü okunur; boşa onarım turu harcanmaz. Kiracı başına model seçen host her sağlayıcıda aynı sonucu görür.

**Mercek:** 1, 2, 8.

**Hazırlık:** İçerik filtresi dekoratör deseni hazır.

**Maliyet:** Public enum üyeleri; site ve OpenAPI.

**Risk:** Tool turundaki ara `Length` ve akışlı yolun son güncellemesi ayrı ele alınır; sağlayıcı eşlemesi farklı olabilir.

**Bağımlılık:** Yok.

**Ekosistem:** MEAI chat span'i `gen_ai.response.finish_reasons` yazar (10.9.0, 2026-10-11); Tracon onu yalnız iz olarak saklar.

**Karşı görüş:** Doğrulama açıkken kesik JSON zaten reddedilir; değer sınıflama, onarımı atlama ve uyarıdır.

### F-321 · Çöken workflow run'ı son checkpoint'ten otomatik sürsün (opt-in)

**Kanal:** faz planlama · 🟡

**Sorun:** Süreç ölünce workflow run'ı `Running` kalır; reconciliation açıksa `Failed` kapanır. Sürdürmek için `POST .../resume` gerekir (`IWorkflowRunner.cs:21-31`). Job yeniden kiralanınca öğe checkpoint'ten değil baştan koşar; içerik yeniden üretilir, onay yeniden gerekir (`WorkflowJobHandler.cs:38-60`).

**Kapsam:** `TraconWorkflowOptions` altında opt-in otomatik sürdürme (son checkpoint, deneme sınırı, devam bağı). Job tekrarında aynı run'ın checkpoint'inden devam. Workflow çocuk run'ları agent devamından hariç.

**Değer:** Deploy veya çökme sonrası yalnız yarım kalan düğüm tekrarlanır.

**Mercek:** 2, 3.

**Hazırlık:** SQL checkpoint ve kararlı executor kimliği var (K-127).

**Maliyet:** Public seçenek, reconciliation dalı, job yükü.

**Risk:** Yarım düğüm tekrar koşar (idempotency şart); MAF sürümü değişirse checkpoint okunamaz.

**Bağımlılık:** F-344 önce kapanır.

**Ekosistem:** MAF durable yolu (`Microsoft.Agents.AI.DurableTask` 1.16.0-preview, 2026-10-11) prerelease ve DTS ister; K-008 gereği çekirdeğe girmez.

**Karşı görüş:** Tüketici resume'u kendi bekçisiyle çağırabilir; podcast raporu § 12 aynı boşluğu aday numarasız yazmıştı.

### F-322 · Kiracı başına workflow eşzamanlılığı, görünür kuyruk ve adil kiralama

**Kanal:** faz planlama · 🟡

**Sorun:** `MaxConcurrentRuns` süreç başına tek semafordur ve kiracılara ortaktır (`TraconWorkflowOptions.cs:27-35`, `WorkflowRunner.cs:120,404-418`). Bekleyen run'ın satırı yoktur; görünmez ve iptal edilemez. Job kiralama FIFO'dur (`PostgresQueries.cs:1165-1177`); bir kiracı diğerlerini aç bırakabilir.

**Kapsam:** Kiracı başına sınır; bekleyen run'a `Queued` satırı ve sıra; `RunTimeout` slot alınınca başlar; `tracon.workflow` işlerinde kiracıya göre sıralı kiralama.

**Değer:** Çok kiracılı SaaS'ta saatlik üretimler adil sıraya girer.

**Mercek:** 2, 3.

**Hazırlık:** Kiracı başına sınır deseni canlı ses ve skill script'te var; `RunStatus.Queued` agent yolunda var.

**Maliyet:** Runner değişikliği; adil kiralama için üç SQL lehçesinde sorgu ve sözleşme testleri.

**Risk:** Süreç içi sınır replika sayısıyla çoğalır; küme geneli sınır dağıtık sayaç ister.

**Bağımlılık:** F-325 (slot beklemesindeki kapanış).

**Ekosistem:** Taranmadı.

**Karşı görüş:** Tüketici admission katmanında kiracı başına kabul sınırı koyabilir; Tracon sınırı ikinci katman olur.

### F-323 · Job süre sınırı ve iptalin handler token'ına ulaşması

**Kanal:** faz planlama · 🟡

**Sorun:** Job'un süre sınırı yoktur; `POST /api/jobs/{id}/cancel` handler token'ını iptal etmez, handler `IsCancelledAsync`'i yoklamalıdır (`IJobHandler.cs:38-41,56-65`; `write-your-own-job-handler.md:145-152`). Takılan bir render tek media slot'unu süresiz tutar.

**Kapsam:** `JobRequest` ve `TraconSchedulingOptions` düzeyinde isteğe bağlı `MaxDuration`; lease döngüsü iptal durumunu okur ve bağlı token'ı iptal eder; aşım ayırt edilebilir hatayla kapanır.

**Değer:** İptal çalışan sürece saniyeler içinde ulaşır; takılan iş kendiliğinden kapanır.

**Mercek:** 2, 5.

**Hazırlık:** Tool timeout deseni (Faz 69).

**Maliyet:** Yeni seçenek ve aralık doğrulaması; lease döngüsünde ek depo okuması.

**Risk:** Token'ı dinlemeyen handler yine takılır — belgelenir.

**Bağımlılık:** F-326 önce kapanır; F-303'ün job eşidir.

**Ekosistem:** —

**Karşı görüş:** Handler kendi `CancelAfter`'ını kurabilir; çerçeve desteği tek tip davranış ve görünürlük ekler.

### F-324 · Karışık zincirde agent adımının iletim modu ve kayıtta tip uyumu

**Kanal:** faz planlama · 🟢

**Sorun:** Agent adımı yanıtın bütün mesajlarını iletir (`WorkflowAgentStepExecutor.cs:33-43`); Loop'ta bu bütün taslaklar demektir (`LoopEvaluatorRegistry.cs:268-272`). Yapılandırılmış nesne kanalı yoktur. Kayıt komşu düğümlerin `InputType`/`OutputType` uyumunu denetlemez (`WorkflowDefinitionValidator.cs:51-92`).

**Kapsam:** Agent düğümüne iletim modu (son asistan mesajı · bütün mesajlar · yapılandırılmış JSON); kayıtta tip zinciri ve ilk düğüm kuralı; varsayılan bugünkü davranış.

**Değer:** Doğrulama veya TTS düğümü yalnız son, geçerli içeriği alır.

**Mercek:** 5, 6.

**Hazırlık:** Descriptor `InputType`/`OutputType` taşır.

**Maliyet:** Tanım alanı ve doğrulayıcı kuralı (public).

**Risk:** Mevcut zincirlerin davranışı korunmalı.

**Bağımlılık:** Önce podcast ham #14 ölçümü; F-341 ile birlikte.

**Ekosistem:** —

**Karşı görüş:** Fonksiyon düğümü son asistan mesajını kendisi seçebilir; değer tek tip davranış ve kayıtta erken hatadır.

### F-345 · Skill script yürütmesini harici sandbox'a yönlendiren public seam

**Kanal:** faz planlama · 🟡

**Sorun:** Beş kapıdan (Enabled · içeriğe pinli grant · yorumlayıcı listesi · argüman doğrulama · denetim) sonra script tek bir çağrıyla yerel süreçte başlar (`SandboxedSkillScriptRunner.cs:447` → internal `SkillScriptProcessRunner`). Süreç Tracon'un OS kimliğiyle koşar; R8 kabul edilen risktir (K-862). Çok kiracılı host stored script için platform yetkisi ister (K-861). Yürütmeyi host'un sandbox'ına taşıyan bir seam yoktur.

**Kapsam:** `Tracon.Abstractions`'ta yürütücü sözleşmesi: girdi yorumlayıcı, script içeriği + hash, stdin JSON, süre ve çıktı sınırı, kiracı, run kimliği; çıktı çıkış kodu, stdout/stderr, kesildi mi, süre aşımı. Varsayılan uygulama bugünkü yerel süreçtir; host kendi uygulamasını (gVisor, mikro-VM, uzak servis) kaydeder. Kapılar, içerik pini, onay ve denetim Tracon'da kalır. Yeni kod yazma yüzeyi açılmaz.

**Değer:** Operatör kodunu çalışma anında koşturan çok kiracılı host (podcast ve Explainer turları) stored script'i gerçek yalıtımla koşar; K-861 ve K-862'nin yeniden açılma koşulu ölçülebilir olur.

**Mercek:** 3, 6.

**Hazırlık:** Tek çağrı noktası belli; sonuç tipi internal `SkillScriptExecutionResult`.

**Maliyet:** Public arayüz ve DTO'lar; `Tracon.Testing.Contracts.Xunit` sözleşme testi; site güvenlik sayfası.

**Risk:** Uzak yürütücü script içeriğini ağda taşır; süre ve iptal ağ üzerinden işler. Yalıtımı host kurar — Tracon yalnız kapıları vaat eder (K-862 dili korunur).

**Bağımlılık:** F-169 eşiği aşılırsa Hyperlight bir uygulama olur. Uygulama kayıtlıyken K-861/K-862 metni karar ister.

**Ekosistem:** Hyperlight 0.7.0 (2026-10-11), eşik aşılmadı. gVisor GPU'yu `nvproxy` ile sınırlı destekler; Firecracker GPU geçirmez (2026-10-11 tarandı).

**Karşı görüş:** Seam yalnız LLM'in çağırdığı skill script yolunu kapsar; Explainer'ın zorunlu medya adımları host job'unda kalır. Operatör kodunu Tracon'un koşturduğu düğüm K2/L20 ile reddedildi (podcast ham #18); bu kalem onu açmaz.

### F-346 · Anthropic güncel API ayarları: önbellek TTL'i ve yerleşimi, adaptive thinking, örnekleme kısıtı

**Kanal:** faz planlama · 🟡

**Sorun:** `anthropic.promptCaching` yalnız üst düzey `cache_control: ephemeral` yazar (`AnthropicProviderSettingsChatClient.cs:103-108`); otomatik breakpoint son bloğa düşer, farklı belge taşıyan istekler talimat önekini paylaşmaz; TTL 5 dk sabittir. `anthropic.thinking.budgetTokens` `{type: enabled, budget_tokens}` yazar (`:110-114`). `ModelBinding.Temperature`/`TopP` doğrudan gider (`AgentDefinitionCompiler.ChatOptions.cs:115-124`). Güncel modeller ikisini de 400 ile reddeder; derleme yakalamaz (`ModelDescriptor` bayrak taşımaz).

**Kapsam:** `ttl` (`5m`/`1h`) ayarı; talimat bloğunun sonunda açık breakpoint seçeneği; `adaptive` thinking ve `effort` (ya da MEAI `Reasoning` eşlemesinin ölçülüp belgelenmesi); katalogda örnekleme/thinking yetenek bayrağı ve derleme uyarısı.

**Değer:** Sabit talimatlı, belgesi değişen üretimlerde önbellek isabeti; güncel modelde ilk run'da 400 yerine derleme uyarısı.

**Mercek:** 1, 8.

**Hazırlık:** Ayar anahtarı deseni var (`AnthropicProviderNames.cs`).

**Maliyet:** Public sabitler, gövde parçaları, canlı ölçüm (anahtar gerekir).

**Risk:** Model aileleri hızlı değişir (K-032): yetenek katalogdan gelir, kodda tutulmaz.

**Bağımlılık:** F-320; F-340 (ayar doğrulama deseni).

**Ekosistem:** Anthropic API başvurusu (2026-10-11 okundu): `cache_control` `ttl: "1h"` alır; paylaşılan önek değişen sonekle bitiyorsa açık işaret önerilir; `budget_tokens` Opus 5.5/5/4.8/4.7, Sonnet 5.5/5, Fable 5/5.1 ve Haiku 5.5'te 400; örnekleme bu modellerin çoğunda 400; Opus 5.5 `effort` varsayılanı `medium`.

**Karşı görüş:** Tüketici `Temperature`, `TopP` ve `budgetTokens`'ı boş bırakabilir; ama önbellek yerleşimini Tracon dışından değiştiremez.

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

**Karşı görüş:** Host doğrulayıcısı ve onarım turu strict olmadan da şemayı zorlar; değer ilk denemede uyum ve onarım maliyetidir.

---

## Bekleyen Kalemler

Bir kalem buradan iki yolla çıkar: iş yapılır (kalem kapanır veya plana
dönüşür) ya da kalemin artık gerekmediği ölçülür ve kalem kapatılır. İki
yolda da § *Kalemin yaşam döngüsü* uygulanır.

### Kusur giderme

F-325…F-344 (Explainer-Video turu) satırları kısadır; tam kanıt ve repro fikri [tüketici raporu § 12](arsiv/incelemeler/2026-10-11-explainer-video-tuketici-uyum-raporu.md#12-tracon-kusurları-kanıt-tablosu)'dedir. Onaylanan sıra: F-325 → F-326 → F-328 → F-327 → F-329 → F-330 → F-331, sonra F-332.

| Aciliyet | Kalem | Sorun | Ne yapılır |
|---|---|---|---|
| 🟠 | **F-317** · Çok turlu harness döngüsünün başarılı run'ı yalnız son turun `usage`'ını kaydeder | Ölçüldü (2026-10-11, Faz 192 denetim düzeltmesi, `HarnessLoopTests` üzerinde katman probu, MAF 1.20.0): üç turlu döngüde (tur başı 10 token) `LoopAgent`, `HarnessAgent` ve dış `ToolApprovalAgent` yanıtı `30 token / 5 mesaj` taşır; en dıştaki MAF `OpenTelemetryAgent` `10 / 1` döner. Sebep: `LoopAgent` toplamı son turun yanıtına yerinde yazar, ama o yanıtın `RawRepresentation`'ı son turun `ChatResponse`'udur; `AsChatResponse` onu tercih eder ve `OpenTelemetryAgent` yanıtı ondan yeniden kurar — toplam `usage` ve transkript düşer. Akışlı yolda da `run.usage` 10 kalır: `RunRecordingAgent` akıştaki `UsageContent`'leri toplamaz, son değeri yazar (`RunRecordingAgent.cs:474-480`). Sonuç: `run.usage`, maliyet ve kiracı kotası döngüyü `(N-1)/N` eksik sayar; ağaç bütçesi `IChatClient` katmanında olduğu için doğrudur. Canlı (örnek uygulama, `gpt-5.4-mini`): iki turlu varsayılan döngü `totalTokens = 6599`, aynı iki tur `FailRun` ile `13142` kaydetti. Ayrıca `aiJudge` ölçütünün model çağrısı hiçbir yolda run `usage`'ına girmez | Önce kırmızı test: üç turlu döngü, akışlı ve akışsız, `root.Usage.TotalTokens == 30`. Akışsız yol: `OpenTelemetryAgent`'in `RawRepresentation` tercihi Tracon tarafında mı aşılır, MAF'a mı bildirilir — karar ister. Akışlı yol: `UsageContent` toplanır; araç çağrılı akışlı run'ın aynı eksikten etkilenip etkilenmediği ölçülür. 🚨 Akışlı istisna yolu `usage` yazmaya başlarsa `RecordingLoopEvaluator`'ın FailRun'da `ExtraUsage`'a eklediği toplam çift sayılır — ikisi birlikte düzeltilir. `aiJudge` `usage`'ı başka modelin fiyatıyla sayılmalıdır; ayrı karar |
| 🟠 | **F-325** · Workflow SSE akışı keep-alive yazmaz; konsol 30 sn sessiz run'ı keser, run `Canceled` olur | Kod okuması (2026-10-11): keep-alive yalnız `RunEndpoints.cs:1272`'de, workflow akışında yok (`WorkflowEndpoints.cs:738-799`); konsol keser (`workflow-detail.tsx:88`), K-845 iptal eder. Slot beklemesinde `RunTimeout` kapanış çerçevesiz kaçar (`WorkflowRunner.cs:404-418`). | Kırmızı fonksiyonel test (45 sn sessiz düğüm). Keep-alive + kapanış çerçevesi. Kalıcı yol F-318 |
| 🟠 | **F-326** · Job worker'ın kira, deneme ve iptal sözleşmesi tutmuyor | Kod okuması (2026-10-11): yenileme ilk hatada durur (`JobWorkerBackgroundService.cs:612-635`); kirası dolan job `MaxAttempts`'sız yeniden kiralanır (`:459-461`, `TraconAsyncRunOptions.cs:28-38` vaadi); iptal edilen job hata atınca `Pending` olur (`:446-491`). | Üç kırmızı test (`IJobStore` sarmalayıcı, depo sözleşmesi, `WorkerHarness`); tick başına yakalama, kiralamada deneme sınırı, iptal terminal |
| 🟠 | **F-328** · Workflow agent adımında deadline/bütçe reddi run'ı düşürmez; ret metni içerik olarak akar | Kod okuması, çalıştırılmadı: ret yanıt olarak döner (`ChildAgentInvoker.cs:493-505`), adım çıktısı olur (`WorkflowAgentStepExecutor.cs:33-43`); `ChildDeadline` 2 dk (`TraconOptions.cs:229`). | Önce repro (yavaş sahte agent). Workflow bağlamında adım düşer. Süre ayarı F-303 |
| 🟠 | **F-327** · Bekleyen insan isteği kalıcı olarak yanıtlanamaz hâle gelir | Kod okuması (2026-10-11): 8 KB kırpılan istek listeden düşer (`RunEventWriter.cs:278-293`, `WorkflowRequestDescriptor.cs:58-90`); retention `AwaitingInput` checkpoint'ini ve olayını siler (`RetentionTargetRegistry.cs:77-82,151-157`; K-130'a aykırı); `KeepCheckpointsAfterCompletion=false` aynı sonucu verir (`WorkflowRunner.cs:1215-1222`). | Kırmızı birim + üç SQL sözleşme testi; istek kimliği kırpılmadan okunur; retention `AwaitingInput`'u süzer |
| 🟠 | **F-329** · Loop + yapılandırılmış doğrulama birlikte açıkken birleşik metin doğrulanır | Kod okuması (2026-10-11): doğrulayıcı `LoopAgent`'ın birleşik yanıtını görür (`LoopEvaluatorRegistry.cs:268-272`, `StructuredResponseValidatingAgent.cs:165-169`); son JSON geçerli olsa da `StructuredResponseInvalid`. Tool öncesi ara metin aynı sınıf; kök F-317 ile ortak. | Fonksiyonel kırmızı test (gerçek zincir); doğrulama son asistan mesajını görür; F-317 ile birlikte |
| 🟠 | **F-330** · Parametreli agent kuyruklu, batch ve replay yollarında bağlanmadan koşar | Kod okuması, çalıştırılmadı: kuyruk dalı parametre kapısından önce ayrılır (`AgentEndpoints.cs:270-284` ↔ `:807-814`); bağlayıcı yalnız `:836` ve `EvalJobHandler.cs:301`'de. Site tersini söyler (`concepts/agents.md:63-67`). | Repro: parametresiz kuyruklu run, sistem metninde `{{`. Bağlama veya açık ret; kuyruğa parametre F-294 |
| 🟠 | **F-331** · Sabit sürümlü çözümleme güncel sürümün descriptor'ını kullanır | Kod okuması (2026-10-11): `CompositeAgentCatalog.cs:149-214`; sabit sürümlü eval ve deney güncel `ResponseFormat` ile doğrular, eval güncel sürümü yazar (`EvalJobHandler.cs:111-113`). Canlı şema güncellemesinde üretim yanlış şemayla doğrulanır. | Fonksiyonel kırmızı test (v1 `Text`, v2 `JsonSchema`); descriptor sabit sürümden |
| 🟡 | **F-314** · Workflow run satırı hata türünü CLR adıyla yazar ve hiç sınıflanmaz | Ölçüldü (2026-10-11, Faz 192, `HarnessLoopTests.A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion` ve örnek uygulama `MT-CORE` case'i): katılımcı agent `LoopUnsatisfied` ile düşünce workflow satırı `error.type = "Tracon.TraconLoopUnsatisfiedException"`, `error.class = null` taşır. `WorkflowRunner.ToRunError` (`src/Tracon.Workflows/Internal/WorkflowRunner.cs:1315`) `TraconException.ErrorType` yerine `GetType().FullName` yazar ve `IRunErrorClassifier`'ı hiç çağırmaz — bu yalnız döngü değil BÜTÜN workflow hataları içindir; panodaki sınıf dağılımı workflow hatalarını göremez. MAF istisnayı sarmalamıyor (ölçüldü) | `ToRunError` kararlı `ErrorType`'ı yazar ve sınıflandırıcıyla `Class`/`Fingerprint` doldurur (`RunRecordingAgent.Completion` deseni). Etki: workflow `error.type` değeri değişir — tüketici sözleşmesi ve site `concepts/runs.md` birlikte; mevcut workflow testlerinin tür iddiaları taranır |
| 🟡 | **F-315** · Harness modu OpenAI Chat Completions sağlayıcısında HTTP 400 ile düşer | Canlı ölçüldü (2026-10-11, örnek uygulama, `gpt-5.4-mini`, `provider: "openai"`): harness'lı agent'ın ilk isteği `400 unknown_parameter: web_search_options` alır (MAF harness'ının varsayılan web araması); `disableWebSearch: true` ile ilk tur geçer, model tool çağırdıktan sonraki istek `400 invalid_request_error, Parameter: messages.[1].role` alır. Aynı tanım `provider: "openai-responses"` ile sorunsuz koşar. Run `ProviderError` sınıfıyla düşer; tüketici sebebi göremez | Önce repro'yu fonksiyonel düzeye indir (Chat Completions şekilli sahte sunucu + harness tanımı). Sonra iki yol: harness, Chat Completions sağlayıcısında desteklenmeyen sağlayıcıları (web araması) kapatır ve rol eşlemesini düzeltir — ya da harness tanımı Chat Completions sağlayıcısıyla derlemede açık bir hata verir. Hangisinin doğru olduğu MAF'ın harness sözleşmesine bakılarak kararlaştırılır |
| 🟡 | **F-332** · Retention ve erasure harici ek baytını silmez; workflow eki 7 günde yetim sayılır | Kod okuması: ham SQL DELETE, `IAttachmentStorage.DeleteAsync` yok (`SqlRetentionStore.cs:30-36`, `SqlDataSubjectStore.cs:63-113`; vaat `IDataSubjectStore.cs:35`); workflow oturumunun `sessions` satırı yok (`RetentionTargetRegistry.cs:170-178`). | SQL sözleşme + fonksiyonel test; commit sonrası harici silme + süpürücü |
| 🟡 | **F-333** · Belgelenen `workflow.request.pending`, `job.*`, `eval.completed` webhook'ları yayınlanmaz | Ölçüldü (grep): `WebhookTypes.cs:26-87` tanımlar, site vaat eder (`concepts/governance.md:684-694`), `PublishAsync` yok; workflow kökü `run.*` üretmez. Faz 21 kapsam sınırıydı. | Fonksiyonel test; bağlama veya vaadi geri çekme (karar) |
| 🟡 | **F-334** · `ExecutorFailed` olayı ham istisna mesajını kalıcı yazar | Kod okuması: `WorkflowEventMapper.cs:53-57,78-82`, test sabitler; `SafeErrorText.cs:8-18` ve K-640'a aykırı. | Kırmızı test; `ToolFailureText` deseni; MT-WF-114 güncellenir |
| 🟡 | **F-335** · Konsol başka yerden başlayan run'ın isteğini açamaz; K-130 yorumu ve testi çelişkili | Kod okuması: sorgu yalnız sayfadaki run için (`workflow-detail.tsx:62,73-84`), metin "workflow ekranında yanıtla" der (`runs.ts:81-84`); `WorkflowRunner.cs:152-155` yorumu ve `Answered_run_no_longer_shows_a_pending_request` testi K-130'a aykırı. | E2E kırmızı test; ekran runId'yi URL'den okur. Kiracı geneli kutu F-319 |
| 🟡 | **F-336** · Ses: özel sentezleyici ElevenLabs ayarına bağlı; başlangıç doğrulaması yok; dosya adı `.mp3` sabit | Kod okuması: sitedeki örnek (`guides/voice.md:145-155`) düşer (`VoiceOptionsValidator.cs:25-43`); fiyat `ProviderName`'i okumaz (`VoicePricing.cs:36-40`); `speak` sınırı ayardan (`SpeakTool.cs:66-75`); `ValidateOnStart` yok. | Fonksiyonel testler; F-298'den önce; `ValidateOnStart` için CHANGELOG |
| 🟡 | **F-337** · Workflow kökü atıf taşımaz (`Labels`/`UserId`, kota `RunId`, `RunAttributionGate`) | Kod okuması: kapsam atıftan önce kurulur (`WorkflowRunner.cs:449-459,473-503`; vaat `TraconRunContext.cs:83-102`); kota `RunId`'siz (`:1191-1209`); gate yalnız `AgentEndpoints.cs:223`. | Atıf aynı gövdede kapsamdan önce okunur; gate her başlatma ucunda |
| 🟡 | **F-338** · Düğüm retry'ı kalıcı 4xx'i geçici sayar | Kod okuması: `DefaultRunErrorClassifier.cs:222-226` her 4xx'i `ProviderError` yapar, `WorkflowNodeRetry.cs:10-22` dener; sözleşme 5xx der (`RunErrorClass.cs:29-30`) | Birim test; 4xx (408/429 hariç) kalıcı |
| 🟡 | **F-339** · Skill'siz harness agent MAF ile çalışma dizininden skill keşfeder | Kod + MAF 1.20.0 XML: `AgentDefinitionCompiler.Agents.cs:265,283-286`; vaat `AgentDefinition.cs:52-56`, kök kuralı `TraconOptions.cs:350-364` | Süreç sınırında kırmızı test (CWD'de `skills/probe`); sağlayıcı kapanır |
| 🟡 | **F-340** · OpenAI sağlayıcısı `ProviderSettings` anahtarlarını sessizce yok sayar | Kod okuması: `OpenAIChatClientFactory.cs:80-92` doğrulamaz, Azure reddeder; sözleşme `ModelBinding.cs:48-53`, K-208 | `ModelProviderSettingsContract` türetilir; kırıcı etki notu |
| 🟢 | **F-316** · Akışsız run endpoint'i `LoopUnsatisfied` ve `StructuredResponseInvalid` hatasını `502` olarak döner | Faz 192 denetimi (kod okuması): akışsız yolun genel `catch`'i (`AgentEndpoints.cs:1553-1571`) her istisnayı `502 Bad Gateway` ve `errorType`'sız problem olarak yazar. Karşılanmayan döngü ya da geçersiz yapılandırılmış yanıt bir yukarı akış arızası değildir; istemci sebebi yalnız run kaydından okur. Akışlı yol türü `error` çerçevesinde taşır (`MT-CORE-134`) | Doğru durum kodu kararlaştırılır (ör. `422`); problem gövdesine `errorType` eklenir. OpenAPI anlık görüntüsü ve istemci birlikte yenilenir. Etki: public HTTP sözleşmesi |
| 🟢 | **F-241** · Kırpma bir vekil (surrogate) çiftini ortadan kesebilir — sınıf | `EvaluatorRunJudge.ResolveVersion` sürümü `version[..MaxEvaluatorVersionLength]` ile kırpar. 128. karakter yüksek vekilse yalnız kalan vekil `varchar`/`nvarchar` yazımında kodlama hatası doğurur; hata yazma döngüsünde `retryable` sayılıp tekrarlanır. Aynı desen `TextValue` kırpmasında da var ([`EvaluatorRunJudge.cs`](../src/Tracon.Core/Evaluation/EvaluatorRunJudge.cs)). Olasılık çok düşük — informational version pratikte ASCII'dir | Sınıfı tek bir güvenli kırpma yardımcısına indir. Kırpılan bir alanın ASCII-dışı içerik taşıdığı ölçülürse aciliyet yükselir |
| 🟢 | **F-281** · `Tracon.Google` görsel yolunda `MediaType` verilirse SDK `NotSupportedException` atar | Canlı ölçüldü (2026-09-24, Google.GenAI 1.16.0, API anahtarı): `ImageConfig.OutputMimeType` her değer için *"outputMimeType parameter is only supported in Gemini Enterprise Agent Platform mode, not in Gemini Developer API mode"* ile reddedilir. `GoogleImageGenerator` (`src/Tracon.Google/Internal/GoogleImageGenerator.cs:77`) `ImageGenerationOptions.MediaType` verilince onu gönderir; Tracon'un iki sevk edilen yolu (`/api/images/generate`, `generate_image`) `MediaType` vermediği için bugün yalnız `IImageGenerator`'ı doğrudan çağıran tüketiciyi etkiler | Paket yalnız API anahtarı modunu desteklediği sürece `MediaType` istemde reddedilir ya da yok sayılır — hangisi olacağı küçük bir davranış kararıdır; karar sonrası düşen test + site cümlesi |
| 🟢 | **F-285** · Kırıcı kapının iki yanlış-mesaj yolu (Faz 187 denetimi) | (a) `PKV007` gibi taban kaydı gerçekte `IsBaselineSuppression` taşımıyorsa (ölçülen `PKV006` taşımıyor) `read_breaking_changes` onu "strict uyumsuzluk" diye etiketler, "tanınmayan kayıt" değil (`scripts/breaking_changes.py`, kural sırası); test `test_pkv007_kirmizi` ölçülmemiş biçimi kullanır. (b) Düşen TFM iç içe listeyle yazılırsa (üst madde TFM, alt maddeler paket) `_list_items` girintili `-` satırını yeni madde sayar ve eşleşme kaçar. İkisi de güvenli yön (kırmızı), yalnız mesaj yanlış | Gerçek bir `PKV007` raporu ölç ve kuralı ona göre sırala; iç içe maddeyi üst maddenin span'leriyle birleştir. F-266'nın `### Removed` maddesi tek madde biçimindeyse acil değil |
| 🟢 | **F-286** · Bütünüyle kaldırılan bir paket kırıcı kapıda hiç denetlenmez | Taban kümesi bugünkü `library_ids`'ten gelir (`scripts/kapi.py` `release_rehearsal`); tabanda olup HEAD'de olmayan paket için ne restore ne rapor olur (Faz 187 denetimi 🟢3) | Taban etiketindeki `src/*/*.csproj` kümesiyle HEAD'dekini karşılaştır; düşen paket kimliği notta code span olarak geçmeli |
| 🟢 | **F-287** · `dokuman_iddia_cakismalari` proje başına `TraconPublicApiTrackingEnabled=false` cümlesini de çelişki sayar | Faz 187 kapıyı gerçek anahtara çevirdi (`scripts/dokuman-bakim.py`); anahtar artık proje başına kapatmadır (K-424), "`Tracon.Client` `false` yazar" gibi doğru bir skill cümlesi bulgu olur. Bugün tetikleyen satır yok (denetim 🟢4) | İddia desenini varsayılana daralt ("varsayılan"/"default" ya da `src/Directory.Build.props` bağlamı) |
| 🟢 | **F-311** · Kapalı MCP oturumuna giden çağrı "TaskCanceledException" diye raporlanır | Ölçüldü (2026-10-11, F-305 repro): kapalı oturumdan giden `tools/call` için `ToolFailed` olay metni "Tool failed with TaskCanceledException." idi. Model "Error: Function failed." gördü, run `Completed` döndü; neden iptal değil kapalı bağlantıydı ve operatör onu iptal sanır. `{server}_read_resource` aynı durumda yanıltıcı "not among the resources declared" hatası verir. K-880'den sonra ana yol, F-310'dan sonra uzun ömürlü oturumlar bu duruma girmez; kalan yol derleme ile çağrı arasındaki yarış | MCP tool sarmalı (ya da `ExplainedFailureAIFunction`), çağıranın token'ı iptal değilken dispose edilmiş oturumdan gelen `OperationCanceledException`'ı "MCP connection to server '<ad>' is closed" diye açıklar; `read_resource` gövdesi kapalı bağlantıyı ayrıca söyler |
| 🟢 | **F-341** · `respond` dönüştürülemeyen yanıtta 400 yerine 200 + `Failed` döner; kayıt komşu tip uyumunu denetlemez | Kod okuması: `WorkflowContracts.cs:103-110` ↔ `WorkflowEndpoints.cs:611-648`; `WorkflowDefinitionValidator.cs:51-92` ↔ `concepts/workflows.md:48-50` | Dönüşüm uçta; kayıtta tip zinciri (F-324 ile) |
| 🟢 | **F-342** · Belge-kod kaymaları (Explainer turu demeti) | Ölçüldü: site `RunTimeout`/düğüm mesajı, `JobHandlerKeys.Workflow`, `SkillRoots`, `MaxOutputBytes`, resume hatası, konsolda `ToolCallUsage`, fallback `ChatOptions` ↔ K-444, MT-WF-114. | Belge koda göre; konsol gösterimi ve K-444 karar ister |
| 🟢 | **F-343** · Eval yolunda bağlam kaybı: `EvalCase.Context` agent'a gitmez; terfi belgeyi ve 8 KB üstünü düşürür | Kod okuması: `EvalJobHandler.cs:336-339` ↔ `EvalCase.cs:35-36`; `RunToCasePromoter.cs:83-108` | Belge düzeltilir veya `Context` belge kanalıyla gider (karar); terfi `run_inputs`'tan |
| 🟢 | **F-344** · Reconciliation yetim workflow kökünü agent turu gibi sürdürür | Kod okuması: `RunReconciliationService.cs:139-174` `RunKind` ayırmaz; girdi kaydı yok (`WorkflowRunner.cs:507-512`); iki ayar varsayılan kapalı | Fonksiyonel test; workflow kökü atlanır (F-321 önkoşulu) |

### Faz planlama

| Aciliyet | Kalem | Neden | Plan notu |
|---|---|---|---|
| 🟠 | F-294 · Workflow run parametreleri | Workflow run'ı yalnız `Message` taşır; `{{param}}` bağlanmaz | Gövde: § *F-294* |
| 🟠 | F-295 · Fonksiyon düğümüne veri ayarı | Workflow başına sağlayıcı/ses seçimi bugün kod ister | Gövde: § *F-295* |
| 🟠 | F-298 · Adlı konuşma sağlayıcı kaydı + kiracı ses anahtarı | Ses çağrısı egress ve BYOK dışında; aynı süreçte yerel ve bulut motor | Gövde: § *F-298* |
| 🟠 | F-318 · Bağlantıdan bağımsız workflow yürütmesi | Saatlik run HTTP akışına bağlı | Gövde: § *F-318* |
| 🟠 | F-319 · Workflow HITL karar sözleşmesi | n revizyonlu onay; ikinci yanıt dal açar | Gövde: § *F-319* |
| 🟡 | **F-290** · `AuthorizationConfigurationKey`'ın `1.0.0` GA'da kaldırılması: veri taşıma + form geçişi | Faz 190 alanı `[Obsolete]` yaptı ve `1.0.0`'da kalkacağını `CHANGELOG`, XML ve `production.md`'de duyurdu (K-869). Üç iş kaldı: (1) `authorization_configuration_key` dolu satırları `header_configuration_keys["Authorization"]`'a taşıyan migration (üç sağlayıcı; iki alan birlikte doluysa ne olacağı karar ister); (2) MCP formu (`mcp.tsx`) eski alanı yazıyor — yeni alanı yazmalı, yoksa kaldırma formu kırar; (3) sütunu ve public üyeyi kaldırma, `McpTransportFactory`/`McpHeaderBuilder` eski yol, `#pragma warning disable CS0618` blokları (kapanışta 11 adet: `grep -rn "pragma warning disable CS0618" src tests`) | GA tarihine bağlı; GA öncesi son preview fazı olarak planlanır. Form geçişi (2) GA'dan bağımsız erken yapılabilir |
| 🟡 | **F-292** · Konsol workflow editörüne düğüm düzenleme (agent + fonksiyon seçici) | Faz 71 Sapma #6 yalnız okuma tarafını sevk etti ve "sonraki faz için aday" dedi; aday hiç yazılmadı. 2026-10-10 kusur-giderme turu editörün `nodes`'u silmesini kapattı: zincir artık salt okunur görünür ve korunur, ama değiştirmek hâlâ `PUT /api/workflows/{name}` ister. Bir tüketici (podcast hattı, 2026-10-10) iş akışını konsoldan düzenlemek istiyor | Kapsam: düğüm listesi (agent/fonksiyon) ekle-sırala-sil ve `GET /api/workflows/functions` seçicisi. Karar ister: son fonksiyon düğümü silinince `agentNames`'e dönüş kuralı — iki derleme yolu farklı akar (`BuildSequential` ↔ karışık zincir, K-495). Kapı: `full-replace-editors.test.tsx` vakaları |
| 🟡 | F-247 · Yirmi dört yapılandırma bölümü için bağlama kanıtı | Risk yok, iş mekanik ve paralel yürür. Sınıf gerçek bir kusur üretti (`TraconImageOptions.Timeout`) | Gövde: § *F-247* |
| 🟡 | F-296 · Konuşma sözleşmesi v2 (diyalog) | İki konuşmacılı ses tek sesli sözleşmeyle üretilemez | Gövde: § *F-296* |
| 🟡 | F-297 · Gemini konuşma adaptörü | Yeni bağımlılık istemez; F-296'ya bağlı | Gövde: § *F-297* |
| 🟡 | F-299 · Fonksiyon düğümünün token dışı maliyeti | TTS maliyeti hiçbir yere yazılmaz | Gövde: § *F-299* |
| 🟡 | F-300 · Workflow ve skill sürüm geçmişi | Operatör düzenlemesi geri alınamaz | Gövde: § *F-300* |
| 🟡 | F-302 · Konsolda `Loop` düzenleme | Revizyon sayısı yalnız API'den değişir | Gövde: § *F-302* |
| 🟡 | F-303 · Fonksiyon düğümü zaman aşımı | K-497'nin koşulu oluştu: dakikalar süren düğüm | Gövde: § *F-303* |
| 🟡 | F-320 · Kesilen model çıktısı ayrı sonuç | Anthropic 4096 varsayılanı uzun JSON'u keser | Gövde: § *F-320* |
| 🟡 | F-321 · Workflow'u checkpoint'ten otomatik sürdürme | Çökmede elle resume | Gövde: § *F-321* |
| 🟡 | F-322 · Kiracı başına workflow eşzamanlılığı | Semafor süreç başına, kiracılara ortak | Gövde: § *F-322* |
| 🟡 | F-323 · Job süre sınırı ve iptal token'ı | Takılan iş slot'u süresiz tutar | Gövde: § *F-323* |
| 🟡 | F-345 · Skill script'i harici sandbox'a yönlendiren seam | Operatör kodu iki tüketicide; R8 kabul edilen risk | Gövde: § *F-345* |
| 🟡 | F-346 · Anthropic güncel API ayarları | `budget_tokens`/örnekleme 400; önbellek öneki paylaşılmaz | Gövde: § *F-346* |
| 🟡 | F-347 · OpenAI strict şema modu | Kiracı başka model seçer; strict istenemez | Gövde: § *F-347* |
| 🟢 | **F-291** · Anahtar alanına değer yazan operatörün değeri `400` ve teslim `error`'ında yankılanır | Faz 190 denetimi 🟢2: `ConfigurationKeyGuard` mesajı (`ConfigurationKeyGuard.cs:112-114`) verilen adı olduğu gibi yazar; `{"X-API-Key":"sk-live"}` gibi bir hata değeri yanıta ve `webhook_deliveries.error`'a taşır. Tek alanlı `*ConfigurationKey` alanlarında da faz öncesinden beri aynı | Önek dışı adı yankılamadan reddet (yalnız alan adı + beklenen önek) ya da kimlik biçimli değeri maskele; dört yüzeyin (MCP, BYOK, webhook, trigger) testleri birlikte |
| 🟢 | F-248 · `unwrap(...) as Promise<T>` iddialarının tip düzeyinde kapısı | Bugün tek örnek elle tarandı; sonrakini kimse taramaz | Gövde: § *F-248* |
| 🟢 | F-249 · docs-site için tarayıcı tabanlı yerleşim kapısı | Elle koşulan case iki turda da kusur buldu | Gövde: § *F-249* |
| 🟢 | F-301 · `AddLoopEvaluator` fabrika aşırı yüklemesi | Ölçüt DI'a ulaşamaz | Gövde: § *F-301* |
| 🟢 | F-324 · Karışık zincirde iletim modu ve tip uyumu | Loop bütün taslakları iletir | Gövde: § *F-324* |

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
| **F-245** · Sessiz bir `run` ile ölü bir bağlantı ekranda AYNI görünüyor | `SseDecoder` keep-alive yorumlarını düşürür; canlılık göstergesi bir arayüz kararıdır. Not: workflow akışı keep-alive yazmaz (F-325) | Kullanıcı sessiz run'ı öldü sanarsa — **kullanıcı kararı gerekir**. Tam metin: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
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
numara: **F-348**.

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
