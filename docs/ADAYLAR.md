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

**Durum (2026-10-11):** sıralanabilir kuyrukta **10** aday var (F-294…F-303,
podcast turu). Bugün yapılabilecek iş § *Bekleyen Kalemler*'in üç
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
turundan gelir
([keşif notu](kesif/2026-10-11-podcast-tuketici.md)); kanal ve aciliyet
§ *Faz planlama* tablosundadır.

### F-294 · Workflow run parametreleri agent `{{param}}`'larına ve fonksiyon düğümlerine ulaşsın

**Kanal:** faz planlama · 🟠

**Sorun:** Agent run'ı `Parameters` taşır ve önbelleksiz bağlar (`AgentEndpoints.cs:823-836`). Workflow run'ı yalnız `Message` ve `SessionId` taşır (`WorkflowContracts.cs:81-90`, `IWorkflowRunner.cs:121-144`); `src/Tracon.Workflows/Internal/` parametreye dokunmaz. Katılımcı katalogdan, talimatı bağlanmadan derlenir (`AgentDefinitionCompiler.ChatOptions.cs:115-119`).

**Kapsam:** Run isteğine `Parameters`; agent adımlarına bağlama; fonksiyon düğümüne `IWorkflowContext` üzerinden okuma; resume aynı değerleri görür. Şema kaynağı (workflow mu, katılımcılar mı) karar ister.

**Değer:** Bölüm başına ton, süre, revizyon sayısı tanım kopyalanmadan verilir; çok kiracılı host tek tanımı parametreyle sürer.

**Mercek:** 1, 3, 5.

**Hazırlık:** Agent tarafı hazır (`InstructionParameterBinder`, `AgentParameterValidator`); workflow tarafı sıfırdan.

**Maliyet:** İki public istek alanı; checkpoint'e parametre yazımı (olası migration).

**Risk:** Parametreli derleme önbelleği atlar — maliyeti ölçülmeli; resume'da kayıp.

**Bağımlılık:** [Faz 192](192-DONGU-TUKENME-POLITIKASI.md) ile (F-293; revizyon sayısı parametre olursa `MaxIterations`'a bağlanır).

**Ekosistem:** Taranmadı (Tracon içi boşluk).

**Karşı görüş:** Değerler mesaja JSON olarak gömülebilir; ama talimat şablon olmaktan çıkar ve doğrulanmaz.

### F-295 · Fonksiyon düğümüne veri ayarı (JSON + şema)

**Kanal:** faz planlama · 🟠

**Sorun:** `WorkflowNodeReference` yalnız `Name` ve `Kind` taşır (`WorkflowNodeReference.cs:22-33`); fonksiyon yalnız girdi ve `IWorkflowContext` alır (`TraconWorkflowFunctionExtensions.cs:89`). Aynı fonksiyonu iki workflow'da farklı ses, LUFS hedefi veya TTS sağlayıcısıyla kullanmak kodda ayrı kayıt ister.

**Kapsam:** Düğüm başına `Settings` (JSON), kod kaydında isteğe bağlı şema, kaydetmede doğrulama, fonksiyonda tipli okuma. Ayar veridir, ifade değildir — K2 ile uyumlu.

**Değer:** Workflow başına sağlayıcı ve ses seçimi operatörün düzenlediği veri olur.

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

**Kanal:** faz planlama · 🟡

**Sorun:** `UseVoice` tek bir `ElevenLabsSpeechClient`'ı `ISpeechSynthesizer` olarak kaydeder (`VoiceBuilderExtensions.cs:76-85`); anahtar süreç geneli (`VoiceOptions.cs:35`). Sağlayıcı adla seçilemez; kiracının egress politikası ve kendi anahtarı konuşma çağrısına uygulanmaz.

**Kapsam:** Konuşma sağlayıcıları için adlı kayıt, kiracı bağlaması (`tenant_provider_bindings` deseni) ve egress denetimi; fonksiyon düğümü ve `speak` tool'u sağlayıcıyı adla ister. `UseVoice` korunur (K1).

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

**Bağımlılık:** [Faz 192](192-DONGU-TUKENME-POLITIKASI.md) `OnExhausted` / `OnCriterionError` alanlarını ekler; editör onları da gösterir.

**Ekosistem:** —

**Karşı görüş:** Host tanımları API'den yönetirse konsol editörü ikincildir.

### F-303 · Fonksiyon düğümüne zaman aşımı

**Kanal:** faz planlama · 🟡

**Sorun:** Bir fonksiyon düğümü sonsuza kadar çalışabilir; yalnız workflow'un genel `RunTimeout`'u (varsayılan 10 dk, `TraconWorkflowOptions.cs:54`) onu keser (K-497). 10–20 dakikalık bölümün TTS ve ses işleme düğümleri dakikalar sürer; genel sınırı büyütmek her düğümün sınırını büyütür. K-497'nin koşulu ("ihtiyaç ölçülürse") bu tüketicide oluştu.

**Kapsam:** Düğüm başına zaman aşımı (kayıtta varsayılan, F-295 ayarıyla geçersiz kılma); aşımda düğümü iptal edip run'ı ayırt edilebilir hatayla bitirme (K-497'nin önerdiği `TimeoutAIFunction` deseni).

**Değer:** Takılan bir dış servis bütün run'ı tutmaz.

**Mercek:** 2, 5.

**Hazırlık:** Tool timeout deseni var (Faz 69).

**Maliyet:** Kayıt aşırı yüklemesi; zamanlayıcı aralık doğrulaması şart (F-280 sınıfı).

**Risk:** İptali onurlandırmayan fonksiyon yine takılır — belgelenmeli.

**Bağımlılık:** F-280 ile aynı aralık yardımcısı.

**Ekosistem:** —

**Karşı görüş:** Tüketici fonksiyonunda `CancelAfter` kullanabilir; çerçeve desteği yalnız tek tip davranış ve görünürlük ekler.

---

## Bekleyen Kalemler

Bir kalem buradan iki yolla çıkar: iş yapılır (kalem kapanır veya plana
dönüşür) ya da kalemin artık gerekmediği ölçülür ve kalem kapatılır. İki
yolda da § *Kalemin yaşam döngüsü* uygulanır.

### Kusur giderme

| Aciliyet | Kalem | Sorun | Ne yapılır |
|---|---|---|---|
| 🟡 | **F-310** · Canlı ses oturumu agent'ı oturum boyunca tutar; oturum ortasındaki MCP değişikliği ona ulaşmaz | Kod okuması, çalıştırılmadı (2026-10-11, F-305 sınıf taraması): `LiveVoiceSessionLauncher.cs:166` ve `VoiceConversationDriver.cs:431` agent'ı oturum başında bir kez çözer. Oturum sürerken sunucu düzenlenir veya silinirse delegasyon tool'u kapalı bağlantıya gider (F-305 belirtisi); onay yeniden açılırsa oturum sonuna kadar onay sarmalı olmayan tool kalır. K-880 yalnız `CompiledAgentCache` yolunu kapatır | Önce repro (canlı ses fonksiyonel testinde oturum ortasında sunucu değiştir); doğrulanırsa onay boyutu yüzünden 🟠. Seçenek: delegasyon başına `IAgentCatalog.ResolveAsync` (önbellek isabeti ucuz) ya da oturum bağlama parmak izi değişince yeniden çözüm |
| 🟢 | **F-241** · Kırpma bir vekil (surrogate) çiftini ortadan kesebilir — sınıf | `EvaluatorRunJudge.ResolveVersion` sürümü `version[..MaxEvaluatorVersionLength]` ile kırpar. 128. karakter yüksek vekilse yalnız kalan vekil `varchar`/`nvarchar` yazımında kodlama hatası doğurur; hata yazma döngüsünde `retryable` sayılıp tekrarlanır. Aynı desen `TextValue` kırpmasında da var ([`EvaluatorRunJudge.cs`](../src/Tracon.Core/Evaluation/EvaluatorRunJudge.cs)). Olasılık çok düşük — informational version pratikte ASCII'dir | Sınıfı tek bir güvenli kırpma yardımcısına indir. Kırpılan bir alanın ASCII-dışı içerik taşıdığı ölçülürse aciliyet yükselir |
| 🟢 | **F-281** · `Tracon.Google` görsel yolunda `MediaType` verilirse SDK `NotSupportedException` atar | Canlı ölçüldü (2026-09-24, Google.GenAI 1.16.0, API anahtarı): `ImageConfig.OutputMimeType` her değer için *"outputMimeType parameter is only supported in Gemini Enterprise Agent Platform mode, not in Gemini Developer API mode"* ile reddedilir. `GoogleImageGenerator` (`src/Tracon.Google/Internal/GoogleImageGenerator.cs:77`) `ImageGenerationOptions.MediaType` verilince onu gönderir; Tracon'un iki sevk edilen yolu (`/api/images/generate`, `generate_image`) `MediaType` vermediği için bugün yalnız `IImageGenerator`'ı doğrudan çağıran tüketiciyi etkiler | Paket yalnız API anahtarı modunu desteklediği sürece `MediaType` istemde reddedilir ya da yok sayılır — hangisi olacağı küçük bir davranış kararıdır; karar sonrası düşen test + site cümlesi |
| 🟢 | **F-285** · Kırıcı kapının iki yanlış-mesaj yolu (Faz 187 denetimi) | (a) `PKV007` gibi taban kaydı gerçekte `IsBaselineSuppression` taşımıyorsa (ölçülen `PKV006` taşımıyor) `read_breaking_changes` onu "strict uyumsuzluk" diye etiketler, "tanınmayan kayıt" değil (`scripts/breaking_changes.py`, kural sırası); test `test_pkv007_kirmizi` ölçülmemiş biçimi kullanır. (b) Düşen TFM iç içe listeyle yazılırsa (üst madde TFM, alt maddeler paket) `_list_items` girintili `-` satırını yeni madde sayar ve eşleşme kaçar. İkisi de güvenli yön (kırmızı), yalnız mesaj yanlış | Gerçek bir `PKV007` raporu ölç ve kuralı ona göre sırala; iç içe maddeyi üst maddenin span'leriyle birleştir. F-266'nın `### Removed` maddesi tek madde biçimindeyse acil değil |
| 🟢 | **F-286** · Bütünüyle kaldırılan bir paket kırıcı kapıda hiç denetlenmez | Taban kümesi bugünkü `library_ids`'ten gelir (`scripts/kapi.py` `release_rehearsal`); tabanda olup HEAD'de olmayan paket için ne restore ne rapor olur (Faz 187 denetimi 🟢3) | Taban etiketindeki `src/*/*.csproj` kümesiyle HEAD'dekini karşılaştır; düşen paket kimliği notta code span olarak geçmeli |
| 🟢 | **F-287** · `dokuman_iddia_cakismalari` proje başına `TraconPublicApiTrackingEnabled=false` cümlesini de çelişki sayar | Faz 187 kapıyı gerçek anahtara çevirdi (`scripts/dokuman-bakim.py`); anahtar artık proje başına kapatmadır (K-424), "`Tracon.Client` `false` yazar" gibi doğru bir skill cümlesi bulgu olur. Bugün tetikleyen satır yok (denetim 🟢4) | İddia desenini varsayılana daralt ("varsayılan"/"default" ya da `src/Directory.Build.props` bağlamı) |
| 🟢 | **F-311** · Kapalı MCP oturumuna giden çağrı "TaskCanceledException" diye raporlanır | Ölçüldü (2026-10-11, F-305 repro): kapalı oturumdan giden `tools/call` için `ToolFailed` olay metni "Tool failed with TaskCanceledException." idi. Model "Error: Function failed." gördü, run `Completed` döndü; neden iptal değil kapalı bağlantıydı ve operatör onu iptal sanır. `{server}_read_resource` aynı durumda yanıltıcı "not among the resources declared" hatası verir. K-880'den sonra ana yol bu duruma girmez; kalan yollar canlı ses oturumu (F-310) ve derleme ile çağrı arasındaki yarış | MCP tool sarmalı (ya da `ExplainedFailureAIFunction`), çağıranın token'ı iptal değilken dispose edilmiş oturumdan gelen `OperationCanceledException`'ı "MCP connection to server '<ad>' is closed" diye açıklar; `read_resource` gövdesi kapalı bağlantıyı ayrıca söyler |

### Faz planlama

| Aciliyet | Kalem | Neden | Plan notu |
|---|---|---|---|
| 🟠 | F-294 · Workflow run parametreleri | Workflow run'ı yalnız `Message` taşır; `{{param}}` bağlanmaz | Gövde: § *F-294* |
| 🟠 | F-295 · Fonksiyon düğümüne veri ayarı | Workflow başına sağlayıcı/ses seçimi bugün kod ister | Gövde: § *F-295* |
| 🟡 | **F-290** · `AuthorizationConfigurationKey`'ın `1.0.0` GA'da kaldırılması: veri taşıma + form geçişi | Faz 190 alanı `[Obsolete]` yaptı ve `1.0.0`'da kalkacağını `CHANGELOG`, XML ve `production.md`'de duyurdu (K-869). Üç iş kaldı: (1) `authorization_configuration_key` dolu satırları `header_configuration_keys["Authorization"]`'a taşıyan migration (üç sağlayıcı; iki alan birlikte doluysa ne olacağı karar ister); (2) MCP formu (`mcp.tsx`) eski alanı yazıyor — yeni alanı yazmalı, yoksa kaldırma formu kırar; (3) sütunu ve public üyeyi kaldırma, `McpTransportFactory`/`McpHeaderBuilder` eski yol, `#pragma warning disable CS0618` blokları (kapanışta 11 adet: `grep -rn "pragma warning disable CS0618" src tests`) | GA tarihine bağlı; GA öncesi son preview fazı olarak planlanır. Form geçişi (2) GA'dan bağımsız erken yapılabilir |
| 🟡 | **F-292** · Konsol workflow editörüne düğüm düzenleme (agent + fonksiyon seçici) | Faz 71 Sapma #6 yalnız okuma tarafını sevk etti ve "sonraki faz için aday" dedi; aday hiç yazılmadı. 2026-10-10 kusur-giderme turu editörün `nodes`'u silmesini kapattı: zincir artık salt okunur görünür ve korunur, ama değiştirmek hâlâ `PUT /api/workflows/{name}` ister. Bir tüketici (podcast hattı, 2026-10-10) iş akışını konsoldan düzenlemek istiyor | Kapsam: düğüm listesi (agent/fonksiyon) ekle-sırala-sil ve `GET /api/workflows/functions` seçicisi. Karar ister: son fonksiyon düğümü silinince `agentNames`'e dönüş kuralı — iki derleme yolu farklı akar (`BuildSequential` ↔ karışık zincir, K-495). Kapı: `full-replace-editors.test.tsx` vakaları |
| 🟡 | F-247 · Yirmi dört yapılandırma bölümü için bağlama kanıtı | Risk yok, iş mekanik ve paralel yürür. Sınıf gerçek bir kusur üretti (`TraconImageOptions.Timeout`) | Gövde: § *F-247* |
| 🟡 | F-296 · Konuşma sözleşmesi v2 (diyalog) | İki konuşmacılı ses tek sesli sözleşmeyle üretilemez | Gövde: § *F-296* |
| 🟡 | F-297 · Gemini konuşma adaptörü | Yeni bağımlılık istemez; F-296'ya bağlı | Gövde: § *F-297* |
| 🟡 | F-298 · Adlı konuşma sağlayıcı kaydı + kiracı ses anahtarı | Ses çağrısı egress ve BYOK dışında | Gövde: § *F-298* |
| 🟡 | F-299 · Fonksiyon düğümünün token dışı maliyeti | TTS maliyeti hiçbir yere yazılmaz | Gövde: § *F-299* |
| 🟡 | F-300 · Workflow ve skill sürüm geçmişi | Operatör düzenlemesi geri alınamaz | Gövde: § *F-300* |
| 🟡 | F-302 · Konsolda `Loop` düzenleme | Revizyon sayısı yalnız API'den değişir | Gövde: § *F-302* |
| 🟡 | F-303 · Fonksiyon düğümü zaman aşımı | K-497'nin koşulu oluştu: dakikalar süren düğüm | Gövde: § *F-303* |
| 🟢 | **F-291** · Anahtar alanına değer yazan operatörün değeri `400` ve teslim `error`'ında yankılanır | Faz 190 denetimi 🟢2: `ConfigurationKeyGuard` mesajı (`ConfigurationKeyGuard.cs:112-114`) verilen adı olduğu gibi yazar; `{"X-API-Key":"sk-live"}` gibi bir hata değeri yanıta ve `webhook_deliveries.error`'a taşır. Tek alanlı `*ConfigurationKey` alanlarında da faz öncesinden beri aynı | Önek dışı adı yankılamadan reddet (yalnız alan adı + beklenen önek) ya da kimlik biçimli değeri maskele; dört yüzeyin (MCP, BYOK, webhook, trigger) testleri birlikte |
| 🟢 | F-248 · `unwrap(...) as Promise<T>` iddialarının tip düzeyinde kapısı | Bugün tek örnek elle tarandı; sonrakini kimse taramaz | Gövde: § *F-248* |
| 🟢 | F-249 · docs-site için tarayıcı tabanlı yerleşim kapısı | Elle koşulan case iki turda da kusur buldu | Gövde: § *F-249* |
| 🟢 | F-301 · `AddLoopEvaluator` fabrika aşırı yüklemesi | Ölçüt DI'a ulaşamaz | Gövde: § *F-301* |

### Doğrudan

| Aciliyet | Kalem | İş |
|---|---|---|
| 🟡 | **F-282** · `troubleshooting` sayfası ağırlık tavanında; yeni semptom girdisi eklenemiyor | Ölçüldü (2026-09-24, Faz 185): sayfa 58 994 B gzip, tavan 59 000 B (`docs-site/scripts/check-weight.mjs`) — 6 B boşluk. Faz 185'in iki kısa girdisi (NU1107/NU1608 ve "Tracon packages from more than one release") 59 850 B, tek başlığa indirilmiş hâli 59 335 B ölçüldü; ikisi de geri alındı, içerik `reference/versioning.md`'de. Kapının kendi yorumu "bir sonraki yükseltme yerine sayfayı böl" der. İş: sayfayı böl (ör. `## Build diagnostics and the agent map` bölümü ayrı sayfaya; semptom dizini, `sidebar.mjs`, TRC tanı kapısı birlikte), sonra Faz 185'in girdisini ekle. Tavanı yükseltmek ölçümle bile kapının kendi kuralına aykırıdır |
| 🟢 | **F-267** · CI tam koşumu hâlâ tek test projesiyle koşuyor | Yerel kapı `-maxcpucount:2`'dedir ([Faz 184](arsiv/fazlar/184-TEST-BEKLEME-VE-E2E-YAPISI.md); aynı makinede 1 işçi 863/879 sn, 2 işçi 607/658/524 sn). `ci.yml` bilerek 1'de: runner donanımı ve Windows ayağı ölçülmedi. İş: bir CI dalında `-maxcpucount:2` ile en az üç koşum. Yeşil ve süre kazancı ölçülürse `ci.yml` güncellenir; kırmızı çıkarsa gerekçe hafızaya yazılır. Dala push gerektirir — kullanıcı onayı |
| 🟢 | **F-288** · `ReleaseArtifactFixture` yerel `1.0.0-preview.1`'i paketler; geliştirici cache'ine girerse nuget.org'daki aynı kimliği gölgeler | Faz 187 planı ölçtü (2026-09-23): `~/.nuget/packages/tracon.core/1.0.0-preview.1/.nupkg.metadata` kaynağı yerel `artifacts/package/release`'ti. 2026-09-24'te o dizin yoktu (cache temizlenmiş). Yayın provası artık izole cache kullanır, etkilenmez (K-864); risk geliştiricinin kendi tüketici denemeleridir | Fixture'ın sabit sürümünü yayınlanmış hiçbir sürümle çakışmayan bir `0.0.0-...` damgasına çevir ya da restore'u izole cache'e al; `MixedVersionGraphTests` ile birlikte ölç |

### Tetik bekleyenler

| Kalem | Engel | Koşul ne zaman oluşur |
|---|---|---|
| **F-180** · Tam paket koşumunda E2E zaman aşımı — sınıf açık | Vaka kapandı, **sınıf açık**: repro "yük altındaki tam paket koşumu"ydu ve kapanış onu kendi koşullarında tekrar koşmadı; test bir gün sonra aynı imzayla düştü. Ders `kusur-giderme` kapanış kontrolündedir | Sınıf yeniden görülürse **yeni** kusur kaydı açılır. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) |
| **F-199** · Kota eşiği bildiriminin claim SONRASI kaybı | Eşik claim edildikten SONRA webhook/akış yayını başarısız olursa o eşik dönem sonuna kadar kalıcı kaybolur. Düşük risk | Kota webhook/notice teslimi için retry/backoff istenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-200** · Çok kullanıcılı kota izolasyonu regresyon testi | Garanti **yapısaldır** (`RunEventWriter`'ın run başına özel `Guid`'i); eksik olan yalnız ona adanmış test | `RunEventWriter`/`RunRecordingAgent`'ın run izolasyonu yeniden düzenlenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-205** · `/v1/conversations/{id}` varlık asimetrisi | Kullanılmamış kimlik `200`, reddedilen kimlik `404`. Katı modda çağıran hangi id'lerin sahipsiz SATIR olduğunu sayabilir — erişim kapalı, yalnız varlık görünür. Davranış ucun rezervasyon semantiğinden gelir; kapatmak OpenAI uyumluluğunu bozar. `/api/sessions/{id}` bu sızıntıyı taşımaz | Tüketici varlık gizliliği talep ederse ([Faz 149](arsiv/fazlar/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md) denetim bulgusu) |
| **F-226** · SSE yanıtının şeması JSON şekli ilan ediyor | ASP.NET Core'un üstveri modeli aynı statü kodu için iki şema ifade edemiyor ve K-039 gereği kütüphane `Microsoft.AspNetCore.OpenApi`'ye bağımlı değil — bir `OpenApiOperationTransformer` kütüphanede yaşayamaz. Ölçüldü (2026-09-13): `tracon.json`'da 7 `text/event-stream` yanıtının **2'si** JSON şekli ilan ediyor (`/tracon/v1/responses` → `JsonElement`, `/tracon/v1/chat/completions` → `ChatCompletion`). Üretilen istemci etkilenmiyor — altıncı geçiş içerik tipinin VARLIĞINA bakar | Belgeden kod üreten üçüncü taraf bir üreteç bu yüzden kırılırsa ([Faz 159](arsiv/fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md) denetim bulgusu 🟢 3) |
| **F-230** · `kurtarma.md` ↔ `.claude/settings.json` senkron kapısı | `KR-11` rampası `deny` listesinin bugünkü içeriğini **sayarak** tekrarlıyor (`git rebase`, `git clean -fd`, `rm -rf` listede yok). Yasağın sınırını yazmayan bir rampa yanlış güven üretir (K-761). Ama `settings.json` genişlerse cümle sessizce yalan olur ve bunu sayan kapı yok | `.claude/settings.json` `deny` bloğu ilk kez değiştiğinde ([Faz 168](arsiv/fazlar/168-KURTARMA-RAMPASI-KATALOGU.md) denetim bulgusu 🟢 6) |
| **F-233** · SQLite'ın yük altındaki `SQLITE_BUSY` davranışı ölçülmedi | `SqliteDialectTests.WAL_and_busy_timeout_are_set_when_the_connection_opens` yalnız ayarların **kurulduğunu** doğrular, çekişme altındaki **davranışı** değil; `BoundedSqlLoadTests` yalnız `PostgresFixture` ile koşar (`tests/Tracon.PostgreSql.IntegrationTests/Load/`). SQLite tek yazarlıdır ve `README.md` onu çok örnekli dağıtım için önermez — ölçülmemiş olması yayımlanan bir vaadi yalanlamıyor | SQLite'ı çok yazarlı ya da yük altındaki bir kurulumda desteklemek istenirse, ya da `BoundedSqlLoadTests` deseni ikinci bir sağlayıcıya genişletilirken |
| **F-236** · Audit yazma politikası ratchet'i değişken adına bağlı | `AuditWritePolicyTests`'in deseni `[Aa]udit[Ll]og\.WriteAsync\s*\(`'dir; `IAuditLog log = …; log.WriteAsync(entry, ct);` şeklini **görmez**. Kaynak taraması "ucuz ama kırılgan" diye bilerek seçildi; Roslyn analyzer'a geçmek bir analyzer paketi maliyetidir. Ölçüldü: `src/` altında bu şekli kullanan tek yer `Tracon.Testing.Contracts.Xunit/Contracts/AuditLogContract.cs:25` ve orada **meşru** | Politikayı atlayan bir çağrı yeri gerçekten kaçarsa, ya da başka bir ratchet de analyzer isterse ([Faz 171](arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md) denetim bulgusu 🟢 7) |
| **F-240** · Kapasite kapısının kalan üç dar açığı | [Faz 174](arsiv/fazlar/174-KAPASITE-DAMGASI-KAPISI.md) denetiminin 🟢 bulguları; (3) ve (4) 2026-09-24'te kapandı. Kalan: (1) `SCHEMAS.storage` ölçülmüş tekrar sayısını başlık dizesinde sabitliyor (`'Rows per run (3 repeats)'`); (2) `P95_SAMPLE_FLOOR = 100` ile `LatencyStatistics.P95SampleFloor` elle senkron, uyumu hiçbir şey ölçmüyor; (5) 19 işaret `docs-site/public/llms-full.txt`'e düz metin olarak sızıyor (emsal `claim:` zaten 11 tane sızdırıyor) | Kapasite ölçümü yenilendiğinde — o koşum (1) ve (2)'yi zaten elden geçirtir |
| **F-237** · Etkiden önceki dar fail-closed `run` kaydı | Genel `RecordingMode.Required` **reddedildi** (A10): model çağrısı ve tool yan etkileri olduktan sonra `run`'ı düşürmek hiçbir şeyi geri almaz. Savunulabilir kalan tek biçim dar bir `seam`'dir — `run` açılışı yazılamazsa `run` başlamaz, yan etkili tool çağrısının kaydı yazılamazsa tool koşmaz. Bugün böyle bir `seam` **yok**: `RunEventWriter` her hatayı yutar ([`RunEventWriter.cs:452`](../src/Tracon.Core/Recording/RunEventWriter.cs#L452) — `Disable`) ve tüketicinin kendi `IRunStore`'u bunu değiştiremez. Kayıp **görünür** (K-782), fail-closed değil | Düzenlemeye tabi bir kurulum kanıtla talep ederse. `tracon.run.recording_failures` sayacı önce kaybın gerçek sıklığını ölçer — kanıtsız inşa edilen altyapı yanlış şekli alır |
| **F-238** · Metrik dinleyici test yardımcısının ÜÇ kopyası | Bir metriği ölçmek isteyen her test projesi kendi `MeterListener` sarmalayıcısını yazıyor: `MetricCollector` ([`tests/Tracon.Core.UnitTests/Fakes/MetricTestHelpers.cs`](../tests/Tracon.Core.UnitTests/Fakes/MetricTestHelpers.cs)), `MetricProbe` ([`tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/MetricProbe.cs`](../tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/MetricProbe.cs)) ve `WorkflowMetricProbe` ([`tests/Tracon.Workflows.UnitTests/WorkflowRecordingFailureTests.cs`](../tests/Tracon.Workflows.UnitTests/WorkflowRecordingFailureTests.cs)). Üçü de meter'ı **referansla** eşleyip `TagList`'i kopyalıyor; üçü de `internal`, paylaşılamıyor. Kopya sayısı K-483'ün "elle tekrarlanan ifade bir kusur SINIFI üretir" eşiğindedir | Dördüncü kopya gerektiğinde, ya da `Tracon.Testing` yüzeyine bir metrik doğrulama yardımcısı eklemek ayrıca istendiğinde. Bu bir **test altyapısı** kararıdır; sevk edilen yüzeyi büyütmek (public bir `MeterProbe`) ayrı bir tartışmadır ([Faz 173](arsiv/fazlar/173-CALISTIRMA-KAYDI-GORUNURLUGU.md) denetim bulgusu 🟢 7) |
| **F-242** · `ResolveVersion`'ın `catch` dalı kanıtlanmadı | Sürüm çözümünde attribute okuması **hata verirse** alan `null` kalır ve skor yine yazılır (K-790). Yalnız "attribute yok" yolu testle kanıtlı; `catch` dalını koşturmak `GetCustomAttribute`'u attıran düşmanca bir tip ister ve kazanç maliyeti karşılamıyor | Assembly attribute okumasının gerçekten attığı bir kurulum (kısıtlı host, bozuk assembly) raporlanırsa ([Faz 176](arsiv/fazlar/176-EVALUATOR-SURUM-DAMGASI.md) denetim bulgusu 🟢 2) |
| **F-243** · Analitik store yüzeylerinde metot bazında iptal kapsamı yok | `IEvalStore.DiffRunsAsync` ve `IRunScoreStore.SummarizeAsync` iptal sözleşmesinin **metot bazında** kapsamı dışında: hook'lar `ListSuitesAsync`/`ListAsync`'i hedefliyor. "Store başına bir okuma + bir yazma" sınırı bilinçlidir ve vaadi kanıtlamaya yeter — ama iptali en pahalı olan yüzeyler bu toplayıcı sorgulardır (store'un İÇİNDE hesaplanırlar, K-483 sınıfı). Aynısı `IRunStore.GetStatisticsAsync` ve `GetTimeSeriesAsync` için de geçerli | Bir toplayıcı sorgunun iptal edilmemesi gerçek bir kaynak tüketimi raporlarsa, ya da sözleşme "store başına bir okuma" sınırını gevşetmeye karar verirse ([Faz 177](arsiv/fazlar/177-STORE-IPTAL-SOZLESMESI.md) denetim bulgusu 🟢 6) |
| **F-245** · Sessiz bir `run` ile ölü bir bağlantı ekranda AYNI görünüyor | Sunucu 250 ms'de bir `: waiting` gönderiyor ve bu canlılığı kanıtlıyor, ama `SseDecoder` yorumları düşürüyor ve konsol onları hiç görmüyor: kullanıcı "düşünüyor" ile "öldü"yü ayırt edemiyor. Ölü bağlantı 30 sn bayt eşiğiyle kapanır; bu kalem **canlılık göstergesi** sorunudur ve bir arayüz tasarımı kararıdır. Ölçüm (2026-09-18): `setOffline(true)` açık bir `chunked` SSE gövdesini kesmiyor — `HATA-S3-005`'in gördüğü donukluk sağlıklı bağlantıda sessiz bir run'dı | Bir kullanıcı sessiz bir run'ı öldü sanıp elle yeniden yüklerse, ya da keep-alive'ı yüzeye çıkarmanın (frame tipi ya da "son sinyal: 2 sn önce" göstergesi) bedeli tartışılırken — **kullanıcı kararı gerekir** |
| **F-246** · Sekiz kararlı hata kimliğinin karşılığı olan bir `RunErrorClass` YOK | Kaynakta 17 kararlı hata kimliği (`const string *ErrorType`) tanımlı; `DefaultRunErrorClassifier.StableIdentities` **9**'unu tanıyor. Kalan 8'i (`session_conflict` · `session_owner_required` · `external_call_rejected` · `agent_source_contract` · `agent_source_failed` · `replay_tool_mismatch` · `job_retry` · `eval_run_diff_unavailable`) bilerek eşlenmedi: **hiçbirinin karşılığı olan bir `RunErrorClass` üyesi yok** ve var olan üyelerin doküman anlamları dar. Yanlış kovaya koymak `Unknown`'dan **kötüdür** — `Unknown` kendi dokümanında dürüst bir ölçüm aracıdır. Yeni enum üyesi public sözleşme işidir: OpenAPI belgesi, TypeScript şeması, iki arayüz sözlüğü ve `RunErrorClassContractTests` (emekli 9 numaralı boşluk disiplini, K-603) birlikte değişir | Bir operatör bu kimliklerden birini panoda ayırt etmek istediğinde, ya da taksonomi bir sonraki kez elden geçirilirken — o zaman sekizi tek turda ölçülür ve kaç yeni üye gerektiği birlikte kararlaştırılır — **kullanıcı kararı gerekir** |
| **F-250** · Kiracıya duyarlı örnek bir `IAgentSource` yok | `MT-CORE-095` iki turdur koşulamıyor: case bir kiracının kendi agent kaynağını getirdiğini ölçmek istiyor, ama depoda kiracıya duyarlı bir `IAgentSource` **örneği** yok — sevk edilen tek uygulama yapılandırmadan okur. K-834 sınıfının **dışındadır**: bir bayrağı çevirmek değil, bir örnek yazmak demektir. Aynı boşluk `samples/`'ın seam envanterinde de bir delik: genişleme noktası sevk ediliyor, örneği yok | Bir tüketici kiracı başına agent kümesi sorarsa, ya da `MT-CORE-095` üçüncü turda da koşulamazsa — o zaman örnek `samples/Tracon.Api`'ye kalıcı olarak eklenir ve case onunla koşulur |
| **F-252** · Örnek uygulamanın demo kancaları bir kimlik sağlayıcısıyla değişmeli | K-834 dokuz demo kancası ekledi (`Tracon:Demo:*` + `whoami`/`refund_order` tool'ları); 60'tan fazla manuel case geçici `Program.cs` düzenlemesi olmadan koşulabiliyor. Hepsi `samples/` altındadır. Örnek uygulama gerçek bir kimlik sağlayıcısına (OIDC/JWT) bağlanırsa `DemoRoleAuthenticationHandler` · `DemoRunAuthorization` · `DemoDenyAllToolAuthorization` üçü birlikte kaldırılmalı ve bu case'lerin ön koşulları yeniden yazılmalıdır | Örnek uygulama gerçek bir kimlik sağlayıcısına bağlandığında |
| **F-253** · Konuşma paneli için sentetik ses sürücüsü | Canlı ses yolu WebRTC'ye parça verdiği için sentetik bir `MediaStream` ile **sürülebiliyor**, ama konuşma paneli `MediaRecorder` kullanıyor ve aynı akıştan **hiç veri üretmiyor** (giden çerçeve 2, ses parçası 0; `MT-MM-086`/`087`). ∴ panelin konuşma **içeriği** isteyen iki case'i yalnız gerçek bir insanla koşulabiliyor. Chrome'un `--use-file-for-fake-audio-capture` bayrağı çözerdi ama tarayıcıyı MCP sunucusu başlatıyor ve bayrak geçirilemiyor | E2E paketine ses kapsamı eklenmek istenirse, ya da tarayıcı başlatma bayrakları yapılandırılabilir hale gelirse |
| **F-254** · Kiracı egress politikası satırı YOKKEN hiçbir kısıt uygulanmıyor (fail-open) | [`ModelProviderRegistry.cs:287`](../src/Tracon.Core/Models/ModelProviderRegistry.cs#L287) `if (policy is not null)`. `policy is null ⇒ kısıt yok` ürünün belgelenmiş varsayılanıdır — politika kurmayan kurulum her sağlayıcıyı çağırabilir. Fail-closed yapmak `TraconTenantProviderOptions`'a varsayılanı kapalı bir anahtar ister (K1 sıfır-sürpriz; KG-034) | Kullanıcı fail-closed seçeneğine karar verirse — **kullanıcı kararı gerekir** (güvenlik varsayılanı) |
| **F-256** · Harf-kayması sözleşme senaryosu yalnız iki store'da | Senaryo paylaşılan `TenantIsolationContract<T>` tabanında değil: taban bellek içi store'lar üzerinde de koşuyor ve K-839 gereği onların 30'u kiracı kimliğini normalleştirmiyor. Senaryo yalnız iki fail-open store'dadır; kalan depolama sınırının güvencesi `TenantParameterChokePointTests` metin kapısı + SQL Server/SQLite guard testleridir | Bellek içi store ailesi kiracı kimliğini normalleştirirse (K-839 genişlerse) — o zaman senaryo tabana taşınır |
| **F-263** · `ic-dongu`, paylaşımlı provider kaynağının HTTP kanıtını seçmiyor | `kapi.py` `src/Tracon.Providers.Shared` değişikliğini dört provider birim projesine bağlıyor (`PROVIDER_TEST_PROJECTS`), ama paylaşılan `authorize` yolunun HTTP seviyesindeki tek kanıtı `ModelHealthEndpointsTests` (`Tracon.AspNetCore.FunctionalTests`, ~200 sn). `ProviderHealthCheckCore` değişirse iç döngü onu koşmaz; `kapanis` tam koşum yaptığı için kaçış kapanışta yakalanır | İç döngüde bir provider kaçışı ölçülürse `Tracon.AspNetCore.FunctionalTests` eşlemeye eklenir ([Faz 181](arsiv/fazlar/181-PROVIDER-ORTAK-KATMANI.md) denetimi 🟢-1) |
| **F-264** · Azure kimlik hatası için manuel kabul case'i yok | `Credential error (...)` davranışı yalnız fonksiyonel testle (`Azure_credential_that_throws_is_Unhealthy_and_does_not_fail_the_whole_list`) kanıtlı. Kimlik gerektirmeyen bir case mümkün (`az login` yok → `DefaultAzureCredential` → `Credential error (CredentialUnavailableException)`, diğer sağlayıcılar etkilenmez) ama örnek uygulama Azure'u yalnız kimlik varken kaydediyor | Örnek uygulamaya bir Azure demo kancası eklenirse (F-252 ile aynı iş) case `06-SAGLAYICI-DIGER`'e yazılır ([Faz 181](arsiv/fazlar/181-PROVIDER-ORTAK-KATMANI.md) denetimi 🟢-2) |
| **F-274** · Üç SQL `Use*` metodundaki ~107 aynı kayıt satırı ortak bir iç yardımcıya taşınmadı | `SqlProviderRegistrationParityTests` unutulan veya dekoratörsüz kaydı kırmızı yapar; kopya kalır. `git log` ölçümü üç dosyaya birlikte dokunan 35 commit gösterdi. Hedef: `src/Tracon.Sql.Shared/Internal/` altında internal bir yardımcı; `Use*` sağlayıcıda kalır | Üç sağlayıcıda satır satır tekrarlanan ikinci bir yatay değişiklik — sayım 2026-09-23'ten başlar (kullanıcı kararı) — ya da dördüncü bir SQL sağlayıcısı (K-176) |
| **F-276** · npm kanalı test edilen derlemeyi değil kendi `npm run build` çıktısını yayınlıyor | `ci.yml` `npm-publish` işinin `Paketle` adımı (`npm run build`) kendi derlemesini yayınlar; `npm test` başka bir derlemede, `release-dryrun`'daki `npm publish --dry-run` üçüncü bir derlemede koşar. NuGet kanalındaki aynı sınıf Faz 191'de kapandı (K-871; yeniden açılma koşulu bu adaydır); `tsc` çıktısı deterministik ve npm'in 72 saatlik geri alma penceresi riski düşürür | `tsc` çıktısında ilk sapma ya da ilk npm yayın kusuru |
| **F-277** · Dış katkıcı için İngilizce mimari karar özeti yok | `ARCHITECTURE.md:59`, `:137-146` ve `CONTRIBUTING.md:114-116` dört Türkçe dokümana işaret eder; iki dosya da makine çevirisinin yeterli olduğunu söyler. K-408 (`docs/` Türkçe kalır, kullanıcı kararı) korunur | İlk dış PR veya issue mimari kararı Türkçe dokümandan okumak zorunda kalırsa ya da GA freeze turu (UR-003) |

Gövdeli tetik bekleyenler aşağıdadır: F-95, F-178 ve F-179.

### F-95 · Agent düzeyinde kesinti/devam kancası

**Engel:** MAF yolu **kapalı** (aşağıda ölçüldü). Kalem yalnız F-141 üzerinden
ilerler.

**Sorun:** Kesintiye uğramış bir agent turunu devam ettirmek için Tracon'in
agent yürütmesinin **içine** girebilmesi gerekir.

**Hazırlık — 🚨 İmza doğrulandı (2026-09-05), MAF yolu KAPALI.**
`maf-api-kesfi` koşuldu; 1.18.0 → 1.20.0 tam yüzey dump'ı diff'lendi. Kanca
çekirdek paketlerde **yoktur**: ayrı ve **alpha** bir pakettedir —
`Microsoft.Agents.AI.AgentHooks` 1.20.0-alpha.260831.1, public yüzeyi iki tip
(`AgentHooksChatClientExtensions.AsAIAgentWithAgentHooks(...)` ·
`AgentHooksOptions`). Üç ölçüm bu yolu kapatır:

1. **Sözleşme yanlış ihtiyacı karşılıyor.** Kanca bir *enforcement/interception*
   sözleşmesidir (AGENT-HOOKS-0.1): allow/deny/transform verdict, approval
   seam, `InterceptionRecord`. Bu kalemin ihtiyacı olan **kesinti/devam**
   (turu checkpoint'leyip sonra sürdürme) yüzeyde **yoktur**.
2. **Bölünemez ve pipeline'ımızla uyumsuz.** Seam decorator'ları `internal`;
   PR gövdesi "partial installs are impossible by construction" diyor.
   `AsAIAgentWithAgentHooks`, içinde `FunctionInvokingChatClient` bulunan bir
   client'ı **reddeder** ("tools would execute below the verdicts") — Tracon'in
   her provider pipeline'ı `UseFunctionInvocation()` kurar
   ([`AnthropicChatClientFactory.cs:14`](../src/Tracon.Anthropic/AnthropicChatClientFactory.cs#L14) ·
   [`AzureOpenAIChatClientFactory.cs:16`](../src/Tracon.Azure/AzureOpenAIChatClientFactory.cs#L16) ·
   [`GoogleChatClientFactory.cs:15`](../src/Tracon.Google/GoogleChatClientFactory.cs#L15)).
   Kancayı almak, agent kurulum yolunun tamamını MAF'a devretmek demektir.
3. **Bağımlılık grafiği kabul edilemez.** `ResponsibleAI.AgentHooks`
   0.1.0-alpha.4 bir **native FFI** taşır (`libagent_hooks_ffi`, 1,7 MB) ve
   yalnız dört RID kapsar: linux-x64 · osx-arm64 · osx-x64 · win-x64 —
   **linux-arm64 yoktur**. Yanında `Microsoft.ML.Tokenizers`,
   `Microsoft.Extensions.AI.Evaluation`, `VectorData.Abstractions`,
   `Compliance.Abstractions`, `FileSystemGlobbing` gelir. `Tracon.Core` bugün
   AOT-uyumludur; bu graf hem onu hem "tüketicinin bağımlılık grafiğini
   kirletme" kuralını bozar.

**Kapsam:** Kalan kapsam F-141'in kapsamıdır: kesinti/devam'ı MAF'a kanca
takmadan çözmek. Tracon paralel bir kanca hiyerarşisi kurmaz (K3) — bu kural
MAF kancası alınmadığı için de geçerli kalır.

**Değer:** Kesintiye uğramış tur, MAF'ı sarmalamadan devam ettirilebilir.

**Mercek:** 2, 5, 6.

**Maliyet:** Ölçüldü, MAF yoluyla **karşılanamaz** (2. ve 3. madde).

**Risk:** Yüzeyin tamamı `[Experimental("MAAI001")]` ve paket alpha kanalında.
K-008 ön sürüm paketlerini yalnız `Tracon.AspNetCore` içinde tutar; bu kanca
`Tracon.Core`'un agent kurulum yoluna girer.

**Bağımlılık:** F-141 (MAF-kancasız alternatif tasarım) — rakip değil, **tek
yol**.

**Ekosistem:** 2026-09-05 — [`dotnet-1.19.0` sürüm notları](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.19.0) ·
[PR #7564](https://github.com/microsoft/agent-framework/pull/7564). MAF pin
2026-09-13 itibarıyla hâlâ **1.20.0**; ölçüm güncel.

**Karşı görüş:** Kalmadı. F-141 aynı ihtiyacı MAF'a hiç kanca takmadan
karşılıyor ve 2026-08-21'de bu tasarımın **doğru** olduğu kaydedilmişti.

---

### F-178 · Model deneme (attempt) telemetrisi

**Engel:** Gerçek bir üretim fallback gecikmesi olayı ölçülmedi. Tüketici
bunu **bilerek** erteledi.

**Sorun:** Yedek zincirinde hangi linkte ne kadar süre harcandığı ölçülmüyor.
`FallbackChatClient` döngü indeksini tutuyor ve `ModelFallbackUsed`'ı yazıyor,
ama `ModelFallbackUsedEventPayload` süre veya indeks taşımıyor. Birincil model
28 sn'de timeout olup yedek 2 sn'de yanıtladığında, 30 sn'lik `run`'ın
gecikmesinin hangi linkten geldiği ayrıştırılamaz.

**Kapsam:** Deneme başına süre (monotonik saat), deneme indeksi, sağlayıcı,
model ve sonuç kategorisi taşıyan bir `run` olayı veya `span`. Prompt ve yanıt
içeriği telemetriye **girmez**; sağlayıcıya özel request ID **çıkarılmaz**
(tüketici bu maliyeti kabul etti). Etiket kardinalitesi sınırlanır.

**Değer:** "Birincil timeout değeri düşürülmeli mi?", "Yedek ilk model olmalı
mı?" soruları kanıta dayanır.

**Mercek:** 2, 7.

**Hazırlık — ölçüldü (2026-09-13):** Boşluk hâlâ tam.
`grep -c "Stopwatch\|GetTimestamp\|Elapsed" src/Tracon.Core/Models/FallbackChatClient.cs`
→ **0**. Döngü indeksi zaten tutuluyor; ekleme tamamen additive'dir.
[Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md) metrik adı ve etiket
kurallarını kurdu.

**Maliyet:** Ölçülmedi. Yeni tablo ve migration gerekmez.

**Risk:** Etiket kardinalitesi kontrolsüz büyürse metrik altyapısını boğar.

**Bağımlılık:** Faz 133 (kapandı).

**Ekosistem:** 2026-09-02 — tüketici raporu.

**Karşı görüş:** Tüketici bunu bilerek erteledi — henüz üretimde olmadığı için incident verisi sunamıyor; ilk olay ölçülünce talebi yeniden açacak. Tek istediği,
API tasarımında bunu engelleyecek bir karar alınmamasıdır; bu koşul bugün
sağlanıyor.

---

### F-179 · Çalışma anı model yönlendirme policy'si

**Engel:** F-178 attempt süresini ölçmeye başlamalı **ve** gerçek üretim
trafiği oluşmalı.

**Sorun:** Model seçimi bugün statik binding ve hata sonrası yedek zinciriyle
sınırlı. Çağrı **öncesi** maliyet, gecikme ve capability'ye göre seçim
yapılamaz; yapılsa bile seçimin nedeni `run` kanıtına girmez.

**Kapsam:** Aday binding kümesinden seçim yapan opt-in bir policy seam'i ve
seçim kararının `run` kanıtına yazılması (seçilen binding, kararlı reason code,
değerlendirilen adaylar, policy adı).

**Değer:** Yönlendirme kararı ile `run` kanıtı aynı yerde durur.

**Mercek:** 2, 7, 8.

**Hazırlık — ölçüldü (2026-09-13), ön koşulların BİRİ karşılandı:**

| Ön koşul | Durum |
|---|---|
| `run` satırı sağlayıcıyı saklamalı | ✅ **karşılandı** — [`RunRecord.ModelProvider`](../src/Tracon.Abstractions/Runs/RunRecord.cs#L73) |
| Kayan latency penceresi ölçülmeli | ❌ eksik — F-178 kapatır |
| Gerçek üretim trafiği | ❌ yok |

`ModelProviderHealthCache` yalnız sağlık durumu verir, gecikme vermez.

**Maliyet:** Ölçülmedi.

**Risk:** Ölçüm olmadan "en ucuzu seç" kararı yanlış olur.

**Bağımlılık:** F-178.

**Ekosistem:** 2026-09-02 — tüketici raporu.

**Karşı görüş:** Tüketici de *"önce doğru telemetry, sonra dinamik policy"*
diyor.

---

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
numara: **F-314**.

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
