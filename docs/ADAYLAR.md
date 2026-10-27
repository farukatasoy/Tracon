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

**Durum (2026-10-26):** sıralanabilir kuyrukta **5** aday var (F-324 video tüketicisi
turu; F-618…F-621 MAF Dalga 4 turu,
[`kesif/2026-10-21-maf-dalga-4.md`](kesif/2026-10-21-maf-dalga-4.md)).
2026-10-26'da iki planlama turu oldu. Birincisi: F-688 (Faz 246), F-572 + F-508
(Faz 247), F-409 (Faz 248 numara kimliği, Faz 250 alt agent sabiti) ve F-545
(Faz 249; ikinci kanıtı F-409, bakımcı kararı). F-409'un skill yarısı F-698
olarak § *Tetik bekleyenler*'dedir. İkincisi, yanlış çalıştığı ölçülen dört birim:
F-503 + F-505 (Faz 251), F-606 + F-607 (Faz 252), F-420 (Faz 253), F-689 + F-690
(Faz 254). Aynı gün doğrudan kapandı: F-495, F-640, F-673, F-702, F-704, F-705.
Bugün yapılabilecek iş
§ *Bekleyen Kalemler*'in üç kanal tablosundadır. Planlanmış fazlar burada değil,
üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'dedir.

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
aday `aday-kesfi` ile üretilir. Bugünkü kalemler 2026-10-11 video tüketicisi
turundan (F-324) ve 2026-10-21 MAF Dalga 4 turundan (F-618…F-621) gelir; kanal
ve aciliyet § *Faz planlama* tablosundadır.

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

---


### F-618 · MCP sunucusundan dağıtılan skill (`UseMcpSkills`)

**Kanal:** faz planlama · 🟢

**Sorun:** Skill'ler MCP sunucusundan dağıtılamaz. MAF `AgentSkillsProviderBuilder.UseMcpSkills(McpClient, AgentMcpSkillsSourceOptions?)` verir (`Microsoft.Agents.AI.Mcp` 1.25 alpha, decompile 2026-10-21); `src/`'de 0.

**Kapsam:** Kiracının MCP bağlantısından skill kaynağı; tek provider kuralı (F-578) ve ad çakışması kuralı (kod ↔ veri ↔ MCP); katalog listesi.

**Değer:** Ekip skill'lerini tek MCP sunucusundan yayınlar; agent'lar onları Tracon kapılarıyla alır.

**Genellik:** MCP skill dağıtımı MAF'ın ekosistem yeteneğidir; tek tüketici sinyali yok, talep ölçülmedi.

**Mercek:** 6.

**Hazırlık:** MAF hazır; Faz 219 builder'ı hazır. `AgentMcpSkill` script taşımaz (içerik ve kaynak).

**Maliyet:** Paket kararı: `Microsoft.Agents.AI.Mcp` alpha'dır ve `ModelContextProtocol` (tam) ile `Extensions.Tasks`'a bağlıdır → K-008 gereği `Tracon.Mcp` olamaz; `Tracon.AspNetCore` ya da K-1037 uydu hattı.

**Risk:** Arşiv skill'i diske açılır (`ArchiveSkillsDirectory`); skill içeriği üçüncü taraf sunucudan gelen istemdir (güven sınırı).

**Bağımlılık:** F-602 (aynı tek-provider ve ad kuralı).

**Ekosistem (2026-10-21):** MAF.Mcp 1.25.0-alpha.

**Karşı görüş:** Skill'i veri olarak saklamak (Tracon skill deposu) aynı işi kiracı yönetimiyle yapar; MCP dağıtımı ancak skill'i başka framework'lerle paylaşan ekip için değer taşır.

### F-619 · A2A görev modu ve dış çağıranın onayı

**Kanal:** faz planlama · 🟢

**Sorun:** A2A her zaman mesaj döner (`TraconA2ABuilderExtensions.cs:72`, `ReturnMessage`); onay isteyen tool'u olan agent A2A'ya açılamaz (`A2AApprovalGuardFilter`). MAF işleyicisi onay isteğini `input-required` görev durumuna çevirmez (decompile 2026-10-21); seam: MAF adla anahtarlı `IAgentHandler`'ı kullanır.

**Kapsam:** Tracon `IAgentHandler`: onay isteği → `input-required` görev; görev güncellemesi → `ToolApprovalResolver`; görev deposu kiracı ve çağıran izolasyonlu.

**Değer:** Onaylı tool'u olan agent başka agent'lara açılır; insan onayı protokoller arası taşınır.

**Genellik:** A2A görev yaşam döngüsü spec'tir. Karar sorusu: dış agent'ın (API anahtarı) onayı insan onayı sayılır mı (K-089 aktörü).

**Mercek:** 3, 6.

**Hazırlık:** MAF seam'i hazır; onay çözücüsü hazır.

**Maliyet:** İşleyici, görev deposu, kart yetenekleri, belge.

**Risk:** Dış çağıranın onay vermesi güvenlik kararıdır; varsayılan kapalı olmalıdır.

**Bağımlılık:** F-614.

**Ekosistem (2026-10-21):** A2A görev durumları (`input-required`).

**Karşı görüş:** A2A çağıranı çoğunlukla insansız bir agent'tır; onayı insana götürecek ön yüzü yoktur. Talep ölçülmedi.

### F-620 · Workflow'u agent olarak sunmak

**Kanal:** faz planlama · 🟢

**Sorun:** Saklanan workflow katalogda agent olarak görünmez; `/v1/*`, A2A ve alt agent çağrısı yalnız agent'a açıktır. MAF `Workflow.AsAIAgent(id, name, description, executionEnvironment, …)` verir (Workflows 1.25 decompile); `src/`'de 0. `WithCheckpointing(agent)` sessiz passthrough'tur (MAF-KAPSAMA §5.8).

**Kapsam:** `WorkflowRunner` sarmalı agent (MAF `AsAIAgent` doğrudan değil: kayıt, bütçe, checkpoint Tracon'un); katalogda tür ayrımı; dış yüzeyler.

**Değer:** Çok adımlı iş tek agent gibi dışarı açılır.

**Genellik:** MAF ilk ilkesi (workflow = agent); talep ölçülmedi.

**Mercek:** 1, 6.

**Hazırlık:** MAF hazır; Tracon runner'ı hazır.

**Maliyet:** Katalog modeli, run kaydı ilişkisi (workflow run ↔ agent run), insan isteği yüzeyi.

**Risk:** Workflow insan isteği beklerken agent yüzeyi (A2A, `/v1`) cevabı taşıyamaz → F-615 ve F-619 ile birlikte tasarlanır.

**Bağımlılık:** F-615; dış yüzey için F-614/F-619.

**Ekosistem (2026-10-21):** MAF 1.25.

**Karşı görüş:** Tüketici bugün workflow'u kendi HTTP ucundan çağırabilir; agent yüzeyi isteyen ölçülmedi.

### F-621 · Workflow orkestrasyon ayarları tanımda

**Kanal:** faz planlama · 🟢

**Sorun:** Saklanan workflow MAF desenlerinin yalnız temel ayarlarını taşır (`WorkflowDefinitionCompiler.cs:93,235,240-257`: `BuildSequential`, `BuildConcurrent(aggregator: null)`, `WithHandoffs`, `WithHandoffInstructions`). Kodda K1 olan ayarlar tanımda yok: Handoff `WithHandoff(from, to, reason)`, `EnableReturnToPrevious`, `WithTerminationCondition`, `WithToolCallFilteringBehavior`; Magentic `WithMaxStalls`, `WithMaxResets`; Concurrent `WithAggregator`; Sequential `WithChainOnlyAgentResponses` (decompile 2026-10-21).

**Kapsam:** Veri alanları (topoloji, sayılar, bayraklar); yürütülebilir parça (aggregator, sonlandırma koşulu, özel group chat yöneticisi) yalnız kodda kayıtlı **adla** seçilir (İ2, K2).

**Değer:** Handoff topolojisi ve Magentic sınırları arayüzden yönetilir.

**Genellik:** Workflow tanımlayan her tüketici; ilk ilke: desen ayarı tanımın işidir.

**Mercek:** 2, 5.

**Hazırlık:** MAF hazır; imzalar planlamada `maf-api-kesfi` ile doğrulanır (Magentic üyeleri deneysel olabilir).

**Maliyet:** Tanım alanları, doğrulayıcı, konsol, TS istemcisi.

**Risk:** Deneysel MAF üyesi public tanıma girmez (K-1036); yalnız veri alanı girer.

**Bağımlılık:** —

**Ekosistem (2026-10-21):** MAF 1.25.

**Karşı görüş:** Ayarların çoğunu kimse istemedi; F-601 gibi torba kalemdir, planlamada en çok istenen ikisine indirilmeli.

## Bekleyen Kalemler

Bir kalem buradan iki yolla çıkar: iş yapılır (kalem kapanır veya plana
dönüşür) ya da kalemin artık gerekmediği ölçülür ve kalem kapatılır. İki
yolda da § *Kalemin yaşam döngüsü* uygulanır.

### Kusur giderme

Kapanan tur geçmişi (2026-10-12…14: hangi turda hangi kalemlerin kapandığı ve hangi sınıf taramalarının yeni kalem açtığı) [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § Kapanan kalemler'dedir.

| Aciliyet | Kalem | Sorun | Ne yapılır |
|---|---|---|---|
| 🟠 | **F-709** · Yarım kalan yayın etiketi on gün görünmedi: hiçbir yerel kapı uzak CI'ı ya da son `v*` etiketinin nuget.org'da olup olmadığını okumaz | Ölçüldü 2026-10-27 (`nuget-danismani`, BL-059): `v1.0.0-preview.5` etiket koşumu 2026-10-17'de kırılgan testte düştü. Tuzak `hafiza/yayin-turu.md`'de yazılıydı ama kimse görmedi: 2026-10-19…26 arasında 25 `main` koşumu `Paketle`'de kırmızı kaldı, bu arada 32 faz `kapi.py kapanis` ile yeşil kapandı | `kapi.py kapanis`: son erişilen `v*` etiketinin paketleri nuget.org'da yoksa kırmızı (flat container, anonim). Ek olarak `origin/main`'in son CI sonucunu oku, kırmızıysa uyar. Sınıf: uzak durum yerel kapıda görünmez |
| 🟡 | **F-708** · `WorkflowResponseOwnershipTests.A_client_that_goes_away_between_the_run_and_the_claim…` kırılgan: ikinci cevabın SSE akışında `WorkflowOutput` yok | Etiket koşumu `39061203012` (2026-10-17, ubuntu): akış `sequence` 0, 1, 3 taşır, 2 yoktur; run `Completed`. Aynı commit `main`'de ve windows'ta yeşil. Test preview.4'ten beri var (`d5a63033`, F-319) | `kusur-giderme` Adım 2 (KR-03): kırılgan test mi, yoksa SSE aynası bir olay mı düşürüyor? `sequence` boşluğu ürün kusuru adayıdır; tekrar döngüsüyle repro |
| 🟢 | **F-679** · Slot bekleyen run'ın `Queued → Running` terfisi koşulsuz upsert'tir: bu arada kapanmış satırı yeniden `Running` yazabilir | Faz 240 denetimi (🟢8, kod okuması). `RunEventWriter.PromoteAsync` `StartRunAsync`'i çağırır; upsert `status`'u ezer, `completed_at`/hata alanlarına dokunmaz. Kuyruktan lease'li bir workflow run'ı başka bir örnekte slot beklerken iptal ucu (kayıt defteri o örnekte yok) işi iptal edip satırı `Canceled` yazarsa, bekleyen örnek slotu alınca satırı `Running`'e döndürür; iş iptalini `PollInterval` içinde fark edip run'ı yeniden kapatır. Kısa süreli tutarsız görünüm; ölçülmedi | Terfiyi "satır hâlâ `Queued` ise" koşullu yaz (depo sözleşmesine koşullu güncelleme gerekir) ya da terfiden sonra satırı oku ve kapanmışsa run'ı iptal et. Önce iki örnekli bir testle ölç |
| 🟢 | **F-680** · Uzlaştırmanın `Failed`/`orphaned` kapattığı hiç başlamamış bekleyen run, `ResumeInterruptedRuns` açıkken sonraki iş denemesinde "kesinti" sayılır ve bir `MaxResumeAttempts` hakkı harcar | Faz 240 denetimi (🟢12). `WorkflowRecovery.PlanAsync` (`WorkflowRecovery.cs:109`, `:135-160`): tek `Queued` zincir `NotStarted` sayılır, ama uzlaştırma satırı `Failed` kapattıysa zincir "kesinti" olur, devam run'ı açılır (checkpoint yoksa baştan koşar) ve hak tüketilir. Sonuç doğru, hak tüketimi belgelenmemiş. Ölçülmedi | Davranışı `Resume an interrupted workflow run` bölümüne yaz ya da `orphaned` kapanan ve olayı olmayan tek run'ı `NotStarted` say. Önce testle ölç |
| 🟢 | **F-639** · Dış yüzey kapısı yalnız açılıştaki, bağlamsız katalogu okur: açıldıktan sonra kaydedilen agent, başka kiracının agent'ı ve çağrılabilir alt agent görünmez | Faz 231 denetimi (🟡4, 🟢2). Ölçüldü: açıldıktan sonra HTTP ile kaydedilen veritabanı agent'ı denetlenmez (`SkillExposureTests` iki host ister). Okuma, ölçülmedi: bağlam yoksa kiracı varsayılandır, çok kiracılı host'ta başka kiracının agent'ı listede yoktur; çağrılabilir alt agent'ın skill ve tool'u sayılmaz. Tool kuralıyla aynı sınıf (eski). Çalışma anı reddi tutar, sızıntı yok | Önce çok kiracılı host'ta ölç. Sonra kapıyı katalog değişince ya da agent çözülürken yeniden koştur; olmazsa sınırı belgede bırak (belgede açılış anı yazılı) |
| 🟢 | **F-641** · Yedek zincir bağlantı istemcilerini kilitsiz bir sözlükte tutar | Faz 232 denetimi (🟢, kod okuması): `FallbackChatClient._fallbackClients` düz `Dictionary` (`FallbackChatClient.cs:57`, `ResolveFallbackClientAsync`); derlenmiş agent eşzamanlı run'lar arasında paylaşılır, iki run aynı anda yedeğe geçerse sözlük eşzamanlı yazılır. Faz 232'den beri uzantının halka fabrikası da bu yoldan koşar. Ölçülmedi, repro yok (`KR-05`) | Eşzamanlı yedek geçişini ölç; kusursa `ConcurrentDictionary` ya da kilit, test: iki paralel run aynı anda yedeğe geçer |
| 🟢 | **F-672** · Kesintiyle kapanan workflow run'ının tutarı kota sayacına yazılmaz | Faz 239 (kod okuması): `WorkflowRecovery.CloseInterruptedAsync` satırı kapatır ama kök kota yazımı (`WorkflowRunner.RecordQuotaAsync`, kapanışta) süreçle birlikte gitti; kalemler `non_token_usage`'dedir, sayaçta yoktur. Devam run'ı bütçeye bu tutarı devralır (`BudgetLineage`) ama sayaç eksik kalır, kota payı hesabı onu iki kez saymamak için temel alır | Önce ölç (çökmeden sonra `GET /api/quotas/usage`). Kapanışta kalemlerden bir kota girdisi yazmak (`TreeQuota.RecordAsync` tek yol) ya da sınırı belgede bırakmak |
| 🟢 | **F-674** · Devam bütçesi okumasının iki sessiz kenarı | Faz 239 denetimi 🟢2, 🟢3: (a) zincir okunurken gelen iptal yutulur, iptal edilen operatör resume'unun satırı kaynağına bağlanmaz (run yine `Canceled` kapanır; yalnız `?continuedFromRunId=` listesinde eksik görünür); (b) çok para birimli ağaç `RunSpend.Total(...) ?? 0m` ile seed'e sessizce 0 yazar, log yok (özel `IRunPricingResolver` gerekir) | (a) iptalde bağı yine yaz (satır zaten açık); (b) bir kez `Warning` yaz. İkisi birlikte küçük bir `kusur-giderme` |
| 🟢 | **F-675** · Operatör resume'u, kuyruk işinin hâlâ sahip olduğu zincire katılır | Faz 239 (kod okuması): resume artık `ContinuedFromRunId` yazar; `WorkflowContinuationChain` onu zincirin son halkası sayar. Kuyruk işi kurtarılırken canlı bir operatör resume'u `Running` görünür ve `CloseChainAsync` onu `interrupted` kapatır; resume aynı zamanda `MaxResumeAttempts` hakkından yer. Bilinen sınır CHANGELOG'da yazılı: işi bitmemiş bir run'ı resume etme | Önce ölç (iş kira süresi dolmadan resume, sonra kurtarma). Kural: kurtarma yalnız kendi açtığı devamları sayar ya da operatör resume'u işin zincirinden ayrı bağ alır |
| 🟢 | **F-643** · Workflow adımındaki agent konuşma hafızası kullanamaz | Faz 233 denetimi (🔴1): workflow'un oturum kimliğinin `sessions` satırı yoktur (F-332); oturum silme, `sessions` saklama hedefi ve oturum anahtarlı silme satırdan başlar, hafıza kaydı silinemezdi. Çözüm: `SessionHasNoRecord` işareti workflow kök kapsamından altındaki her run'a akar ve hafıza yazmaz (K-1067). Kaybedilen: bir workflow'un agent adımı, aynı kullanıcının önceki oturumlarını hatırlayamaz | Açma kararı: workflow run'ına bir silme çapası vermek (ör. veri sahibi silmede `runs.session_id` genişletmesi + `runs` saklamasında kopya silme). Ölçülmüş ihtiyaç gelirse |
| 🟢 | **F-645** · Agent silinip aynı adla yeniden oluşturulursa yeni agent eski agent'ın kullanıcı hafızasını okur; agent silme hafıza kopyasını silmez | Faz 233 denetimi (🟢1): hafıza `AgentId` olarak agent ADIYLA anahtarlanır. Oturumlar da agent silinince kalır, bu yüzden bu fazın yaşam döngüsü kapsamı dışındadır | Karar (kalıcı-veri): agent silmede kopyanın akıbeti; ad yerine tanım kimliği anahtarı |
| 🟢 | **F-646** · Workflow adımındaki agent oturum çalışma alanı kullanamaz | Faz 234 (ölçüldü, `SessionWorkspaceFileStoreTests.A_run_whose_session_has_no_record…`): workflow'un oturum kimliğinin `sessions` satırı yoktur (F-332); alanın dosyalarını oturum silme, veri sahibi silme ve saklama süresi bulamazdı, bu yüzden workflow kökü ve altındaki her run alansızdır (F-643 ile aynı çapa) | F-643'ün kararı bunu da çözer: workflow run'ına bir silme çapası vermek. Ölçülmüş ihtiyaç gelirse |
| 🟢 | **F-647** · Çalışma alanı olan agent'ın yarıda kalan run'ı otomatik devam ettirilemez | Faz 234: dosya tool'ları `AIContextProvider`'dan gelir ve `RecordedToolPlayback` onları sarmaz; devam, tamamlanmış bir yazmayı ya da silmeyi tekrarlayabilirdi, bu yüzden `RunContinuationJobHandler` reddeder (`RunContinuationTests.An_agent_with_a_session_workspace_is_not_continued…`). Kaybedilen: çökmeden sonra otomatik devam | Karar: sağlayıcı tool'larını da oynatma sarmalayıcısından geçirmek (MAF `AIContext.Tools` kancası) ya da yazma tool'larını `SafeToRepeat` dışında bırakıp okumaları serbest bırakmak |
| 🟢 | **F-648** · Çalışma alanı kotasının birimi motora göre değişir (SQL Server UTF-16) | Faz 234 (gerçek SQL Server 2022'de ölçüldü): PostgreSQL ve SQLite UTF-8 baytı sayar, SQL Server `DATALENGTH` ile 2 bayt/ASCII karakter; aynı `MaxSessionBytes` SQL Server'da yarı kadar metin tutar. Dosya sınırı her motorda UTF-8'dir | Ölçüm: SQL Server 2019+ UTF-8 harmanıyla `DATALENGTH(CAST(content COLLATE …_UTF8 AS varchar(max)))` ya da `agent_files.content_bytes` sütunu (migration). Talep gelirse |
| 🟢 | **F-649** · Çalışma alanını okuyan yönetim ucu ve konsol görünümü yok | Faz 234 Açık Soru 2 (A: bu faz değil): operatör bir oturumun dosyalarını göremez ve indiremez; yalnız veri sahibi dışa aktarma `session_workspace` altında listeler | `GET /api/sessions/{id}/workspace` (+ dosya içeriği ucu), `SessionsRead` izni ve konsol sekmesi. Talep çıkarsa |
| 🟢 | **F-650** · Kiracı kotası her yazımda kiracının tüm alan satırlarını toplar | Faz 234 (PostgreSQL 18, 20 000 alan dosyası, indeksle): kiracı toplamı 7,2 ms; satır sayısıyla doğrusal. Bir oturumun toplamı 0,5 ms | Çok büyük kiracı ölçülürse sayaç tablosu ya da `MaxTenantBytes` denetimini her N yazımda bir yapmak. Önce ölçüm |
| 🟢 | **F-651** · SQL Server'da oturum kimliğinin harfi değişirse alan anahtarı değişir | Faz 234 denetimi (KR-05, repro yok): `SessionWorkspacePaths.Key` ham `sessionId`'yi özetler; SQL Server `sessions.id` harmanı duyarsızdır, `ABC` ve `abc` aynı satırdır ama farklı anahtardır. İstemci harfi değiştirirse boş alan görür; `DELETE /api/sessions/ABC` URL'deki harfle, saklama satırdaki harfle anahtar üretir, biri öbürünün dosyasını bulamaz. PostgreSQL ve SQLite'ta harfler ayrı oturumdur | Önce gerçek SQL Server'da iki harfle repro. Çare sağlayıcıya bağlıdır (kiracı gibi tek biçime getirmek PostgreSQL'de iki oturumu birleştirir); anahtar biçimi kalıcıdır (K-1068), `rc.1` öncesi değişmesi ucuzdur |
| 🟢 | **F-652** · Oturum satırı yazılmadan önce düşen run'ın alan dosyaları yetim kalır | Faz 234 denetimi (KR-05, repro yok): `HasWorkspace` yalnız `SessionHasNoRecord = false` ister; yeni oturumun satırı run sonunda yazılır. Kalıcı "hatırla" kuralı onayı atlatırsa ilk run dosya yazıp satırdan önce düşebilir. Silme ve saklama satırdan başlar, bu dosyalara ulaşmaz | Repro: kalıcı kural + ilk run'da yaz + modeli düşür. Çare: alan yazımını oturum satırına bağlamak (ilk yazımda satırı garanti etmek) ya da satırsız anahtarlar için süpürücü |
| 🟢 | **F-653** · Konuşma dalı (branch) alanı taşımaz | Faz 234 denetimi: `ConversationBranchService` yeni oturum kimliği verir; yeni oturumun alanı boştur ama geçmiş eski dosyalara atıf yapabilir | Karar: dalda alanı kopyalamak ya da geçmişi uyarıyla sunmak. Talep gelirse |
| 🟢 | **F-654** · SQL Server `0052` indeksinin planı ölçülmedi | Faz 234: PostgreSQL ölçüldü (14,1 → 0,5 ms) ve SQLite planı testle sabit; SQL Server için `LIKE … ESCAPE '\\'` + parametre indeksi kullanır mı bakılmadı | SQL Server 2022 kapsayıcısında 200 000 satırla `SET STATISTICS IO` ölçümü |
| 🟢 | **F-655** · Kota varsayılanları (256 KiB · 5 MiB · 500 MiB) ölçümsüz | Faz 234 Açık Soru 1: yalnız sorgu maliyeti ölçüldü, dosya boyutu dağılımı ölçülmedi; dosya başına 1 KiB satır maliyeti de sabittir | Gerçek kullanım (örnek dosya boyutu, satır boyutu) ölçülünce varsayılanlar gözden geçirilir |
| 🟢 | **F-581** · OpenAI uyumlu uçların kalan iki açığı: konuşma öğelerinde görsel/dosya listelenmez; MAF `input_file` adını düşürür | İstek yönü 2026-10-19'da kapandı (kusur-giderme, K-1027: `/v1/chat/completions` agent'ın istek ayarını `400` ile reddeder, `/v1/conversations` ilk `items`'ı reddeder). Kalan: (1) `GET /v1/conversations/{id}/items` görsel ve dosya içeriğini listelemez (`ToItems` yalnız metin ve tool; kod okuması, ölçülmedi). (2) MAF `Hosting.OpenAI` `input_file`'ın `filename`'ini düşürür ve `file_data`'yı `application/octet-stream` yapar (upstream, ölçüldü) | (1) Ölç; listelenmiyorsa ek referansını öğe olarak ver. (2) Upstream bildirimi bakımcının kararı |
| 🟢 | **F-526** · `kapi.py` sevk edilen sözleşme değişimini çözüm dışı örnek depoya bağlamıyor | Ölçüldü (2026-10-14, F-502): `RunStoreContract`'a case ekleyen değişiklik `samples/Tracon.Samples.FileRunStore`'u kırdı; çözümde olmadığı için yalnız `kapi.py yayin --kuru` gördü. `faz-tamamlama` bunu yazar, kapı hatırlatmaz | `src/Tracon.Testing.Contracts.Xunit/` dokunulduğunda `ic-dongu` ve `kapanis`, `samples/Tracon.Samples.*` uyarısının kardeşi olarak "yayın provası gerekir" bassın |
| 🟢 | **F-527** · Yeniden denemeden sonra reddedilen düğümde ilk denemenin hatası kayıttan düşer | Faz 210 denetimi (🟢): ikinci deneme bütçe kapısında reddedilirse run satırı yalnız `Could not start function node …` der; ilk denemenin geçici hatası kayıtta yok. `WorkflowNodeRetry` başarısız denemeyi bütçeden bağımsız olarak da loglamaz | Retry döngüsü her başarısız denemeyi `Warning` olarak logla; ret mesajı gerekirse ilk denemenin hata tipini anar |
| 🟢 | **F-496** · Kuyruklu workflow `respond`'unun `definitionVersion` zinciri uçtan uca sınanmıyor | Faz 200 denetimi (🟡2, kısmen): `resume` için tezgâh var (`WorkflowAsyncRunTests`); stored workflow'un insan girdisi bekleyen bir tezgâhı yok, `WorkflowEndpoints` respond satırı silinirse yalnız yük ve handler birim testleri parçalarını sınar | İnsan girdisi bekleyen stored workflow tezgâhı kur; adı konan sürüm ve olmayan sürüm (`400`, iş kuyruğa girmez) |
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
| 🟢 | **F-428** · Model yargıcının bağlaması örnekleme denetiminden geçmez | Faz 204 denetimi 🟢3 (2026-10-13, kod okuması; Faz 225 denetimi: yeni istek alanları da aynı açığı taşır — `SummarizationModel`, `ModelRunJudgeOptions.Model` ve `Tracon:UtilityModel` `Seed`/`TopK`/`ToolMode` gibi alanları kabul eder, okumaz ve `ISamplingSupport` denetimi bu bağlamaları görmez): `SamplingCapabilityCheck` yalnız agent tanımının bağlamasını ve yedeklerini okur. `ModelRunJudge` `Temperature`/`TopP`'yi `ModelRunJudgeOptions.Model`'den gönderir; `SupportsSampling = false` modele bağlı yargıç her örneklenen run'da `400` alır ve bunu ancak skor yazılmayınca gösterir. `CompactionSettings.SummarizationModel` örnekleme alanlarını göndermez, açık yalnız yargıçtadır | Yargıç seçeneklerinde başlangıç doğrulaması (`ValidateOnStart`) aynı yardımcıyı okur; fonksiyonel test |
| 🟢 | **F-434** · Ajan batch öğesindeki `sessionId` kayıtta kabul edilir, öğe ancak çalışınca düşer | Faz 197 Sapma 9 (2026-10-13): `BatchItemInput` iki handler'a ortaktır; zamanlama kaydı ve tetik şekli kabul eder, `AgentBatchJobHandler` öğeyi düşürür (K-928) | Kayıt ve tetik doğrulaması `sessionId`'yi handler anahtarına göre reddeder |
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
| 🟢 | **F-570** · Üretilen sözleşmede `"examples":[null]` gürültüsü; site örneğinde `evaluatorVersion` 10.9.0 | Faz 214 denetimi 🟢1-2: ASP.NET Core OpenApi 10.0.13 + Microsoft.OpenApi 2.12.2 dört şemaya (`LoopSettings`, `SubAgentSettings`, iki `headerConfigurationKeys`) `examples:[null]` ekledi; Açık Soru 4 = A (upstream çıktısı) ile kabul edildi, ama `ModelBinding`/`ModelProviderSettings` de `<example>` taşıyor ve belirti vermiyor: sebep tam açıklanmadı. `concepts/evaluation.md:291` örnek JSON'u `10.9.0+…` diyor | Karar: dönüştürücüyle silmek (B) mi, sebebi ölçüp kabul etmek mi; örnek sürümü hash'iyle birlikte yeniden üretilir
| 🟢 | **F-571** · Anthropic duraklatılmış tur yeniden kurma kodu SDK aggregator'ına taşınabilir mi | Faz 214: SDK 12.58 `MessageContentAggregator` sunucu tool'unun `input_json_delta`'sını birleştirir; `AnthropicStopReasonChatClient.StreamedBlocks`'un varlık gerekçesi kısmen düştü. Kod bilerek kaldı (yükseltme istek değiştirmez) | Ölçüm: aggregator `redacted_thinking`, atıf ve bilinmeyen blok türlerini ham JSON'a birebir döndürüyor mu; evet ise ~80 satır silinir
| 🟢 | **F-699** · Harness agent'ında `EnableConversationMemory` sessizce yok sayılır | Faz 246 planlaması (2026-10-26, kod okuması, ölçülmedi): `CreateMemoryProviders` yalnız `CompileChatAgent`'ta çağrılır (`AgentDefinitionCompiler.Agents.cs:50`); harness sağlayıcı listesinde (`:470-498`) konuşma hafızası yok ve doğrulayıcı kuralı bulunmadı | Önce ölç: harness agent'ı `OnDemand` hafızayla koş, model isteğinde `search_conversation_memory` var mı. Yoksa `kusur-giderme`: kayıtta `400` ya da harness'e bağlama (karar) |
| 🟢 | **F-700** · Uzlaştırıcı kayıt dışı tool'u güvensiz sayar; arama, MCP ve workspace tool'u çağıran run otomatik devam edemez | Faz 246 planlaması (2026-10-26, kod okuması, ölçülmedi): `RunReconciliationService.FindUnsafeToolAsync` (`:244-261`) `toolRegistry.List()`'te olmayan tool'u güvensiz sayar; yerleşik arama tool'ları, MCP ve workspace tool'ları kayıtta değildir | Önce ölç: `search_knowledge` çağıran run'ı yarıda kes, `ResumeInterruptedRuns` açıkken devam ediyor mu. Karar: descriptor'ın `SafeToRepeat`'i yönetişimden okunur mu |

### Faz planlama

| Aciliyet | Kalem | Neden | Plan notu |
|---|---|---|---|
| 🟢 | F-248 · `unwrap(...) as Promise<T>` iddialarının tip düzeyinde kapısı | Bugün tek örnek elle tarandı; sonrakini kimse taramaz | Gövde: § *F-248* |
| 🟢 | F-249 · docs-site için tarayıcı tabanlı yerleşim kapısı | Elle koşulan case iki turda da kusur buldu | Gövde: § *F-249* |
| 🟢 | F-324 · Karışık zincirde agent adımının iletim modu | Loop bütün taslakları iletir; tip zinciri F-341 ile kapandı | Gövde: § *F-324* |
| 🟢 | **F-406** · Agent yolları konuşma sağlayıcısını adla seçsin | Faz 198 Açık Soru 5-C: Faz 198 adı yalnız fonksiyon düğümünde ve uçta seçtirir | Faz 198'den sonra; agent tanımına ses bağlaması |
| 🟢 | **F-408** · Skill sürüm geçmişi ve geri alma | Faz 200 Açık Soru 6-B: skill geçmişi K-082'yi (kullanıcı kararı) açar, üç tablo ve özet liste ister (20 × 256 KiB kaynak), K-860 grant'ına dokunmamalı | K-082'nin yeniden açılma kararı; Faz 200'ün workflow deseni |
| 🟢 | **F-493** · Workflow sürüm geçmişi budanamaz | Faz 200 kapsam dışı: `workflow_definition_versions` hiç silinmez (numara kimliktir, K-987); silinen workflow'un geçmişi ve düğüm ayarındaki her değer kalır | Tüketici ihtiyacı gelince: `retention` kapsamı ya da yalnız hiçbir run'ın damgalamadığı sürümleri silen uç |
| 🟢 | **F-494** · Konsol grafı belirli bir workflow sürümü için çizemez | Faz 200 kapsam dışı: graf görünümü güncel tanımı çizer; sürüm farkı alan karşılaştırmasıdır | `GET …/graph?version=N` ve Sürümler satırında bağlantı |
| 🟢 | **F-410** · Kod Loop ölçütüne tanım başına tipli ayar | Faz 201 kapsam dışı: özel tür veri almaz — tek örnek bütün tanımlara hizmet eder (`LoopEvaluatorRegistry.BuildCriterion`, `default` dalı); konsol kod türü için alan göstermez ve tür değişiminde `marker`/`judgeCriteria`/`modes`'u temizler (`loop-section.tsx` `withKind`). Faz 195'in `Settings` deseni emsal; ayar `FindProblems`'a ve `GET /api/loop-criteria`'ya (şema) birlikte girer | Faz 201'den sonra |
| 🟢 | **F-417** · Görsel sağlayıcısı kiracının kendi anahtarını kullanabilsin | K-912 (2026-10-13): kiracının görsel sağlayıcısı için bağlaması varsa üretim reddedilir, çünkü sağlayıcı paketleri üreticiyi kurulum anahtarıyla kurar. Canlı ses `SupportsTenantCredentials` ile aynı işi çözdü (K-878) | Faz 198'in konuşma modeliyle birlikte: anahtarlı üretici fabrikası, OpenAI/Azure/Google görsel kaydı |
| 🟢 | **F-445** · Konuşma soketi ses sağlayıcısını tur başına yeniden denetlesin | Faz 198 denetimi 🟢1 (2026-10-13, kod okuması): soket STT ve TTS'i bağlantı açılırken bir kez çözer (`VoiceConversationDriver.ResolveProvidersAsync`); açık bağlantı sonradan kaydedilen egress politikasını kapanana kadar görmez. Model tarafı her turda yeniden okur (F-310 emsali). Site ve XML bugün bu sınırı yazar | Karar: tur ortasında ret davranışı (hata çerçevesi, bağlantıyı kapatma) — protokol sözleşmesi; sonra tur başına çözümleme ve fonksiyonel test |
| 🟢 | **F-504** · Workflow SSE'si agent adımının kesildiğini söylemez | Faz 207 §207.6, kod okuması: olay alt run'da kalır; workflow `MessageDelta`'sı bitiş sebebi taşımaz | Önce ölçüm; sonra çerçeve kararı (K-991 emsali) |
| 🟢 | **F-506** · Konsol playground'u `custom` çerçevelerini göstersin | Faz 207 §207.6: `use-playground-run.ts` `custom`'u yok sayar; kesilme ve kota bildirimi görünmez. Faz 251 listeye `tracon.output.filtered`'ı ekler | Rozet tasarımı (`en.ts` + `tr.ts`), E2E; üç tip |
| 🟢 | **F-513** · Retention hedefi yapılandırmadan kapatılamaz | Faz 206 Açık Soru 1-A: `MaxAgeDays` yalnız sayı alır; JSON `null` (8.0.1 `""`, 10.0.0 `null`) varsayılanı sessizce bırakır | Karar (1-B): boş değer `null` bağlar mı; saklama süresini uzatma yönü |
| 🟢 | **F-517** · Tek cevaplı istek için yönetici çıkışı | Faz 208 denetimi 🟢1 + kapsam dışı: teslimden sonra düşen devamda zıt cevap hep `409`; motorun teslimde reddettiği cevap isteği kilitler; kaydı `Abandoned` yapan uç yok | Admin ucu, denetim kaydı, konsol düğmesi |
| 🟢 | **F-518** · `workflow_responses` sınırsız büyür | Faz 208 🟡5: kayıt yalnız veri sahibi silmesiyle gider; hiçbir saklama hedefi `runs` silmez | `workflow_requests` hedefi var (K-1004); aynı kalıp `workflow_responses` için ayrı karar |
| 🟢 | **F-519** · Yalnız devam run'ı silinince cevap kaydı kalır | Faz 208 denetimi 🟢2: FK yalnız `source_run_id`'de; zincir boş görünür, aynı cevap `Gone` → `409` alır | Karar: `response_run_id` FK'sı ya da `Gone`'da yeniden sürme |
| 🟢 | **F-532** · Süre kapanışı normal kapanışın temizliğini atlar | Faz 209 denetimi 🟢2: süre ile kapanan run `KeepCheckpointsAfterCompletion = false` temizliğini ve `RecordRun` metriğini çalıştırmaz (onay emsali aynı); retention yine bırakır | Ortak kapanış yardımcısı ya da karar: emsal gibi kalsın |
| 🟢 | **F-533** · Kısmen dolmuş run'da kapanmış isteğe cevap alınır | Faz 209 denetimi 🟢5: bir istek kapanmış, öbürü açıkken kapalı isteğe cevap `WorkflowRunner` denetiminden geçer (satır durumuna bakılmaz); işaret kapalı satıra yazılmaz | Karar: kapı kapalı satırı `400` ile reddetsin mi |
| 🟢 | **F-534** · Kiracı listesi sayfa başına olay okur | Faz 209 §209.1: satır olayı `event_seq` ile adlandırır ama istem her satır için `ReadEventsAsync`'le okunur (sayfa en çok 200) | Tek olay okuyan `IRunStore` üyesi; önce ölç |
| 🟢 | **F-535** · Satırı olmayan bekleyen run listede yok ve dolmaz | Faz 209: satır yazımı best-effort; Faz 209 öncesi bekleyenler ve yazılamayan satırlar için doldurma yok (Açık Soru 7-B) | Karar: olaydan satır üreten uzlaştırma (SQL JSON ayrıştırma) |
| 🟢 | **F-595** · C-10'un kalanı: atıf, mesaj kaynağı, workflow OTel, embedding/ses/görsel span'leri | Ölçüldü (2026-10-20): Faz 223 tanınmayan içeriği ve workflow olayını kaydeder (K-1034). Kalan: `TextContent.Annotations` (atıf) kaydedilmez (`src/`'de 0) · `AgentRequestMessageSourceAttribution` yalnız JSON bağlamında · workflow `WithOpenTelemetry` 0 · `OpenTelemetryEmbeddingGenerator` ve ses/görsel OTel halkaları 0: Tracon tüketicinin `IEmbeddingGenerator`'ını doğrudan çağırır ve span açmaz (`VectorSearchToolFactory.cs:65`) ([`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §5.1, §5.7, §5.13) | Atıf için tip başına zengin olay K-1034'ün yeniden açılma koşuludur. Hosted tool ücreti C-12'dedir (Dalga 3). Dalga 3'ün ön koşulu değil |
| 🟢 | **F-600** · Çalışan run'a mesaj (steering) (C-07) | Ölçüldü (2026-10-20, gerçek host + MAF kontrolü): MAF katmanı Tracon'da tool döngüsünün üstüne düşer; seçenek korunur (K-1038) | Döngü içi yuva Faz 232'de açıldı (`ChatClientSlot.ModelCall`); kalan HTTP ucu, kiracı, rol, olay. Yargı: [`kesif/2026-10-20-maf-dalga-3.md`](kesif/2026-10-20-maf-dalga-3.md) § F-600 |
| 🟢 | **F-601** · Bellek, bağlam ve skill seçenekleri tanımda (C-08) | Ölçüldü (okuma, 2026-10-20): `TextSearchProviderOptions`, `TodoProviderOptions`, `AgentSkillsProviderOptions` `new()` ile kurulur (`AgentDefinitionCompiler.Memory.cs:43,63-65`, `.Skills.cs:43-60`); compaction eşikleri korunan stratejide | Torba kalem: planlamada en çok istenen ikisine inilir. Yargı: [`kesif/2026-10-20-maf-dalga-3.md`](kesif/2026-10-20-maf-dalga-3.md) § F-601 Skill okuma onayı dilimi: Faz 231. |
| 🟢 | **F-602** · Kod tanımlı MAF skill'i (C-09) | Ölçüldü (2026-10-20, gerçek host): uzantının ikinci skill provider'ı Tracon skill'iyle birlikte MAF tarafından reddedilir (aynı state key) | `CreateSkillsProvider`'a `UseSkill`; C# script'i süreç içinde koşar → kapı tasarımı. Yargı: [`kesif/2026-10-20-maf-dalga-3.md`](kesif/2026-10-20-maf-dalga-3.md) § F-602 |
| 🟢 | **F-604** · Hosted MCP sunucusu (C-12 b) | Ölçüldü (2026-10-20, gerçek OpenAI teli): onay `store=false` ile çalışır; Tracon onay yolu `McpServerToolCallContent`'i `tool:unknown` yazar, kural yazmaz (`ToolApprovalResolver.cs:91,197`) | `secret` adla (K-059), adres izin listesi; OpenAI sunucuyu kendi bulutundan çağırır (Tracon egress'i yok). Bağımlılık Faz 226 (F-603). Yargı: [`kesif/2026-10-20-maf-dalga-3.md`](kesif/2026-10-20-maf-dalga-3.md) § F-604 |
| 🟢 | **F-605** · Değerlendirme: çok turlu vaka ve workflow suite'i (C-17 kalan) | Dilim (a) beklenen tool argümanı Faz 227 oldu. Kalan: vaka tek turdur (`EvalCase.cs:16`), suite yalnız agent hedefler (`EvalSuite.AgentName`); MAF `Run.EvaluateAsync` akışsız `Run` ister, Tracon `StreamingRun` kullanır (ölçüldü 2026-10-20) | Talep ölçülmeden planlanmaz. Yargı: [`kesif/2026-10-20-maf-dalga-3.md`](kesif/2026-10-20-maf-dalga-3.md) § F-605 |
| 🟢 | F-618 · MCP sunucusundan dağıtılan skill (C-14 c) | Ölçüldü (2026-10-21, decompile): `UseMcpSkills` alpha pakette | Paket yeri kararı; F-602 ile tek kural. Gövde: § *F-618* |
| 🟢 | F-619 · A2A görev modu ve dış çağıranın onayı (C-15 a2) | Ölçüldü (2026-10-21): A2A hep mesaj döner, MAF onayı `input-required`'a çevirmez | Bağımlılık F-614; dış onay kararı. Gövde: § *F-619* |
| 🟢 | F-620 · Workflow'u agent olarak sunmak (C-18 c) | Ölçüldü (2026-10-21, decompile): `Workflow.AsAIAgent` `src/`'de 0 | Bağımlılık F-615. Gövde: § *F-620* |
| 🟢 | F-621 · Workflow orkestrasyon ayarları tanımda (C-18 d) | Ölçüldü (2026-10-21): derleyici yalnız temel desen ayarlarını kurar | Torba: en çok istenen ikisine inilir. Gövde: § *F-621* |

### Doğrudan

| Aciliyet | Kalem | İş |
|---|---|---|
| 🟡 | **F-625** · Karma zincirin agent düğümü ve `GetWorkflowAgentStep` tool onayını bekleyemez | Faz 230 ölçümü (2026-10-21): bu düğümler MAF host executor'ı değil `WorkflowAgentStepExecutor`'dır (`FunctionExecutor`); `agent.RunAsync` yanıtındaki `ToolApprovalRequestContent` düşer, `AddPortHandler` internal olduğundan düğüm istek yayınlayamaz. Ret sürer (K-1055, `hostsApproval` yok) | Düğüme port düğümü ve geri kenar eklemek (graf yapısı değişir → bekleyen run'ların checkpoint'i uyuşmaz; oturum geçmişi checkpoint'e taşınmalı) ya da düğümü host'a çevirmek (`TurnToken` aktarımı ölçüldü: sürmedi). Tüketici isteği olmadan açılmaz. Emsal: `WorkflowToolApprovalTests` |
| 🟡 | **F-622** · Oturum kimliği uzunluğu SQL Server `nvarchar(200)` sınırını aşabilir | Faz 229 Açık Soru 2 (ölçülemedi, ortamda SQL Server yok): yerel run uçlarında `sessionId` uzunluğu doğrulanmıyor (`grep`: 0); 201 karakterlik kimlik SQL Server'da yazımda hata verir (beklenti: `500`). A2A bundan etkilenmez (kimlik 68 karakter) | Önce ölç: `POST /api/agents/{name}/run` 201 karakterlik `sessionId` ile SQL Server'da. `500` ise `kusur-giderme` (sınıf taraması: `conversation`, `previous_response_id`) |
| 🟢 | **F-608** · Alt agent tool adı çakışma kuralı MAF sağlayıcılarının sabit tool adlarını görmez | Faz 228 denetimi 🟢1: `SubAgentToolNames.FindProblems` `ToolNames`, skill tool'ları, `search_knowledge` ve callable adlarına bakar. Harness'ın varsayılan açık `todos_*` ve `mode_get`/`mode_set` tool'ları ile `Memory.EnableFileMemory`'nin `file_memory_*` tool'ları `Microsoft.Agents.AI` 1.25.0'da sabit adlıdır (dize okuma; sağlayıcı-ad eşlemesi ölçülmedi). `todos-add` adlı bir callable agent sağlayıcıda iki aynı adlı tool üretir ve istek `400` ile düşer | Ad kümesini sağlayıcıdan okuyup (kod) ölç; yalnız ölçülen ad girer. Olasılık düşük |
| 🟢 | **F-609** · Alt agent tool modunun üç test boşluğu | Faz 228 denetimi 🟢2-4: (a) "agent tool'u uzantıdan önce eklenir, koruma taban çizgisi onu tutar" sözünü bir test sabitlemiyor (`AddRange` `Configure`'dan sonraya kayarsa hiçbir test kırılmaz); (b) `WaitTimeout` sert kesimi tool yolunda koşulmadı (yalnız `ChildDeadline` koşuldu); (c) tool modunun akışlı HTTP ucu (SSE) otomatik testi yok, yalnız `MT-UIAG-065` elle | (a) bir uzantının `Tools`'a dokunması reddedilirken agent tool'unun taban çizgide olduğunu sabitleyen birim testi; (b) `HangingModelProvider(respectsCancellation: false)` ile tool modu; (c) `POST /api/agents/{name}/run` akışı |
| 🟢 | **F-610** · Harness web search'ü `ReplayTools`/`NoTools` replay'inde koşmasın | Faz 226 denetimi (okuma, 2026-10-21): `RunReplayService.ApplyOverrides` hosted adları `ToolNames`'ten düşürür ama `HarnessSettings.EnableWebSearch` açık agent'ın replay'i yine arama yapar ve artık ücreti de sayılır | Replay modunda harness'ın web search'ü kapatılır (`LiveTools` bırakır); kapı: replay'de `options.Tools`'ta `HostedWebSearchTool` yok |
| 🟢 | **F-611** · `toolCallArgsMatch`'in MAF biçimine düşme yolu nesne argümanını eşleyemez | Faz 227 denetimi 🟢1: `ToolCallArgumentsCheck.Evaluate` tam beklentiyi `EvalItem` örneğinden okur (`ConditionalWeakTable`); item'ı kopyalayan bir `IEvalEvaluatorFactory` (tüketici) kaybettirir. Düşme yolunda beklenen nesne JSON metni (`string`) olur, çağrılan nesne `Object` kalır; metni birebir aynı nesneler bile `args mismatch` verir (`ToolCallArgumentsCheck.cs`, `FromMafCalls`). `IEvalEvaluatorFactory` belgesine uyarı Faz 227'de yazıldı | Düşme yolunda `String` beklenen ile `Object`/`Array` çağrılanı ham metinle karşılaştır (MAF'ın yaptığı); kapı: item'ı kopyalayan sahte factory ile nesne argümanı. Yalnız factory'yi değiştiren tüketiciyi etkiler |
| 🟢 | **F-612** · `@tracon/client` yazma yönünde `JsonElement` sözlüğünü `Record<string, never>` tipler | Faz 227 denetimi 🟢2: `EvalExpectedToolCall.arguments` `null \| Record<string, never>` çıkar (`schema.ts`); konsol `as Record<string, never>` cast'i kullanır. Aynı tip okuma yönünde zaten vardı (`JobTriggerRequest.Payload` gibi); `EvalExpectedToolCall` ilk yazma yönü kullanımı. Düzeltme OpenAPI dönüştürücüsündedir (`additionalProperties` yazılmayan `object`) | `scripts/nswag-prepare-document.py` ve `openapi-typescript` ayarı: serbest nesne `Record<string, unknown>` ve C#'ta `JsonElement` olsun. Kapı: yazma yönü tip testi |
| 🟢 | **F-613** · Kod yoluyla `default(JsonElement)` taşıyan beklenen argüman depolar arasında tutarsız | Faz 227 denetimi 🟢3: HTTP bunu üretemez; `ExpectedToolCallsCodec` SQL'e `null` yazar, `ReplaceCasesAsync` dönen listede `Undefined` kalır, `InMemoryEvalStore` aynen saklar, `JsonValueEquals` `Undefined`'ı eşleşmez sayar. Bir tüketici hatası yanıtı serileştirirken `500`'e çevirebilir (MEMORY "struct alanı" dersi, tetikleyici yalnız kod) | `EvalExpectedToolCall`'ı `Arguments` set edilirken `Undefined`'ı `null` JSON'a çeviren bir üreticiden geçir ya da `ReplaceCasesAsync` girişinde normalle; üç depoda aynı davranış kapısı |
| 🟢 | **F-623** · `SendStreamingMessage` kopan bağlantıda oturum yazımını kaybedebilir | Faz 229 denetimi 🟢7 (decompile, ölçülmedi): A2A SDK'sında `SupportsStreaming` varsayılan `true`; kopan akışta yürütme ayrık sürer, `HttpContext` yoktur, `TraconCallerIsolationKeyProvider` `null` döner, strict depo atar ve tamamlanmış tur kaybolur. Kart akışı desteklenmiyor diye ilan eder | Önce ölç (bağlantıyı kopar, sonra aynı `contextId` ile mesaj); sonuç kaybolursa yakalanan kimlikle yazım (`AIHostAgent.BindIsolationKey`) |
| 🟢 | **F-624** · A2A ve MCP uçları `IRunAuthorizationHandler` çağırmaz | Faz 229 denetimi 🟢10: eskiden beri böyle; artık A2A oturum kimliği olan bir yüzey olduğu için tüketicinin kendi oturum/run kuralı oraya ulaşmıyor. Öbür run yüzeyleri `RunAuthorizationGate.CheckRunAsync` çağırır | Karar: dış çağıran için run yetkisi (kimlik: anahtar mı, atıf mı); K-691 sınıfı |
| 🟢 | **F-590** · `GetService` atan fabrika agent'ı için tanılama sessizce atlanır | Faz 221 denetimi 🟢6: `CodeAgentSource.WarnWhenModelRingsAreBypassed` `IChatClient` aramasındaki hatayı yutar; ad uyarılanlara girmez, her çözümlemede yeniden denenir ve hiçbir iz kalmaz | Debug düzeyinde tek satır log; çözümleme davranışı değişmez |
| 🟢 | **F-592** · `tracon.content` olayının `Text` alanı sağlayıcı denetimli `ErrorCode` ve `MediaType` taşır ve `RecordToolPayloads` kapalıyken de yazılır | Faz 223 denetimi 🟢5: plan ve site "`Text` kullanıcı verisi taşımaz" der; pratikte kısa bir koddur ama bir adaptör mesajı `ErrorCode`'a koyarsa kalıcı olur. Yazıcı `Text`'i yalnız `MaxPayloadLength`'e keser | Gerçek bir adaptörde ölçüm: hangi sağlayıcılar `ErrorCode` doldurur, uzunluğu ne. Gerekirse `Text`'te kodu kes ya da yalnız sabit sözlükten yaz |
| 🟢 | **F-593** · K-640 cırcırı `ContentEventPayload`'ın yabancı hata metnini yazdığı yolu görmez | Faz 223 denetimi 🟢6: `RawExceptionTextSiteTests` yalnız `new RunEventDraft(` başlatıcısındaki `.Message`'ı tarar; `ErrorContent.Message` ayrı bir yardımcıda okunur, taban çizgisinde gerekçesi yok. Ayrıca `KeepsPayloadWithPayloadsOff` listesi ile `RunEventType` önsözündeki tip adları arasında makine kapısı yok (`RunEventPayloadSuppressionTests` yalnız `RunEventType.X` sayar) | Cırcıra "yabancı metin → kalıcı alan" için `ErrorContent.Message` ve `WorkflowEvent.Data` okumalarını ekle; muafiyet listesi ↔ önsöz için ikinci bir tip adı sayımı |
| 🟢 | **F-591** · Rehberin C# blokları ile sample'ın eşliği kapıya bağlı değil | Faz 221: "her blok sample'dan alıntı" iddiasını elle koşulan bir betik doğruladı; sample değişince `any-maf-feature.md` sessizce bayatlar | `dokuman-bakim.py` kuralı: rehberin her `csharp` bloğu `samples/Tracon.Samples.MafFeatures` kaynaklarında ardışık satırlar olarak geçsin (`///` satırları sayılmaz) |
| 🟢 | **F-586** · `UseOpenAISpeech(UseAsDefault = true)` `UseVoice`'tan sonra çağrılırsa `TryAdd` sessizce hiçbir şey yapar | Faz 220 denetimi 🟢8: belgede yazılı (remarks, site, README) ama çalışma anında uyarı yok; varsayılan ElevenLabs kalır ve `speak` sessizce onu kullanır | Başlangıçta `ILogger` ile tek satır uyarı: kayıt anında anahtarsız `ISpeechSynthesizer` zaten varsa. Tüketici kaydı kazanır kuralı değişmez; yalnız görünürlük |
| 🟢 | **F-583** · Köprülenmiş ses sağlayıcısı kiracının kendi anahtarını kullanamaz | Faz 220 (Açık Soru 2-A): `AddSpeechSynthesizer`/`AddSpeechTranscriber(name, binding)` ve `UseOpenAISpeech` kurulum anahtarıyla kurulmuş istemciyi çağırır; kendi `openai` bağlaması olan kiracı `403` alır (görsel emsali, F-384). Görsel üreticisi de aynı sınırdadır | Tüketici ihtiyacı ölçülünce: istek başına kimlik bilgisiyle istemci kurma (`SpeechRequest.Credential` → yeni `AudioClient`/boru hattı) ve `SupportsTenantCredentials = true`. Görsel ve ses için **ortak** iş; önce ölçüm: kaç tüketici kiracı başına OpenAI anahtarı ister |
| 🟢 | **F-585** · OpenAI çözümünün süresi okunmaz; çözümün maliyeti `null` | Faz 220: `/v1/audio/transcriptions` `json` yanıtı `{"text":…}` taşır; `SpeechTranscript.AudioDuration` boş kalır ve `IVoicePricingReader.ForTranscription` süre ister. Bazı modeller yanıtta `usage.seconds` ya da `duration` verir (gerçek servisle ölçülemedi: örnek uygulamanın projesinde ses modeli yok) | Ses modeli erişimi olan bir anahtarla yanıtı ölç; süre varsa `SpeechToTextResponse.StartTime/EndTime`'a yaz ve `Tracon:Pricing:Voice:openai:<model>:PerMinute` ile fiyatlandır |
| 🟢 | **F-267** · CI tam koşumu hâlâ tek test projesiyle koşuyor | Yerel kapı `-maxcpucount:2`'dedir (Faz 184; aynı makinede 1 işçi 863/879 sn, 2 işçi 607/658/524 sn). `ci.yml` bilerek 1'de: runner donanımı ve Windows ayağı ölçülmedi. İş: bir CI dalında `-maxcpucount:2` ile en az üç koşum. Yeşil ve süre kazancı ölçülürse `ci.yml` güncellenir; kırmızı çıkarsa gerekçe hafızaya yazılır. Dala push gerektirir — kullanıcı onayı |
| 🟢 | **F-288** · `ReleaseArtifactFixture` yerel `1.0.0-preview.1`'i paketler; geliştirici cache'ine girerse nuget.org'daki aynı kimliği gölgeler | Faz 187 planı ölçtü (2026-09-23): `~/.nuget/packages/tracon.core/1.0.0-preview.1/.nupkg.metadata` kaynağı yerel `artifacts/package/release`'ti. 2026-09-24'te o dizin yoktu (cache temizlenmiş). Yayın provası artık izole cache kullanır, etkilenmez (K-864); risk geliştiricinin kendi tüketici denemeleridir | Fixture'ın sabit sürümünü yayınlanmış hiçbir sürümle çakışmayan bir `0.0.0-...` damgasına çevir ya da restore'u izole cache'e al; `MixedVersionGraphTests` ile birlikte ölç |
| 🟢 | **F-509** · Seçenek ratchet'ı iki bölümü ve 13 tipi görmez | Faz 206 kapsam dışı: `SectionName`'siz `TraconTenantProviderOptions`, `TraconInboundTriggerOptions` (testsiz); ratchet yalnız Core ve Abstractions'ı tarar (`TraconWorkflowOptions`, `VoiceOptions`, sağlayıcı ve SQL tipleri) | Kapsamı genişlet; her biri için `<Tip>BindingTests` |
| 🟢 | **F-510** · Bağlama kanıtı kapısının boşlukları | Faz 206 denetimi 🟢2-4: `[Fact(Skip/Explicit)]` ya da ilgisiz tek `[Fact]` sınıfı sayar; `OptionsKeyCoverage` çağrısı zorlanmaz; yüklem testi `.Where`'i kanıtlamaz; (b) mesajı okunmayan anahtarda da "the test value" der; ayarlanabilir Tracon sınıfı skaler sayılır; Retention anahtarları property adından türer | Yüklemi atlanan/açık testi dışlayacak biçimde sıkılaştır; literal hedef listesi + yansımalı eşitlik |
| 🟢 | **F-512** · Doğrulayıcısı olmayan yedi seçenek tipi | Faz 206 kapsam dışı: `AsyncRun`, `ContentGuard`, `Egress`, `Idempotency`, `McpSecurity`, `RateLimit`, `SessionOwnership`; geçersiz aralığın çalışma anı etkisi ölçülmedi | Önce ölçüm; sonra `IValidateOptions` + `ValidateOnStart` |
| 🟢 | **F-563** · OpenAI uyumlu yanıt tipleri ve yedi public enum OpenAPI belgesinde açıklamasız | Faz 212 denetimi 🟢3: `openapi-undescribed-baseline.txt` `ChatCompletion`, `ItemResource` gibi `internal` OpenAI uyumlu tipleri ve `WorkflowKind`, `RunErrorClass`, `RunScoreBucket`, `RunScoreTarget`, `TimeSeriesBucket`, `VoiceSessionEndReason`, `EvalCaseSource` enum'larını listeler. Public bir enum'un XML özeti şemaya geçmez; `CanaryDecisionKind` geçer | Önce ölçüm: enum özetinin neden geçmediği. Sonra `[Description]` ekle; taban küçülür |
| 🟢 | **F-566** · Kurucu tabanı gerekçelerinin doğruluğu denetlenmiyor | Faz 213 denetimi 🟢2: `public-constructor-baseline.txt` yalnız gerekçe uzunluğunu (≥ 40) zorlar. `JobContext`, `ToolApprovalContext`, `SpeechDialogueAudio`, `MutableTenantContext` için "tüketici kurar" iddiasının rehber/sample kanıtı yok (`grep "new JobContext"` samples ve docs-site'ta boş; testlerde var) | Her girişe somut bir kanıt yolu zorunlu kılmak (rehber, sample veya test sınıfı) ya da kanıtsız girişleri `internal` yapmak; tasarım kararı |
| 🟢 | **F-573** · Faz 215 hijyeninin kalanı: testlerin yenileme komutu konumsal biçimde yazılı | Faz 215 denetimi 🟢12: 28 test dosyası ve ratchet baseline başlıkları yenileme komutunu `dotnet test tests/…` biçiminde yazar; SDK 10.0.402 konumsal biçimi sessizce kabul eder (ölçüldü). `_trx_output_directory` dalı ve bayat yorumlar 2026-10-17'de kapandı | SDK konumsal biçimi reddederse tek geçişte `--project` olur; o güne kadar iş yok |
| 🟢 | **F-574** · Sağlayıcı AOT smoke'u yalnız anahtar yolunu, düz/akışlı sohbeti, TTS'i ve ses listesini koşar | Faz 217 denetimi 🟡4 (2026-10-17): "Azure: Yes" Entra (`BearerTokenPolicy`) yolunu, görsel yolunu; "Voice: Yes" STT'yi ve diyaloğu da kapsar, ama `ProviderAotSmoke` bunları çağırmaz. Entra dalı HTTPS ister (`http://` sahte sunucu taşıyamaz) | Smoke'a kendi imzalı sertifikalı bir `https` sahte sunucu ve Entra çağrısı ekle (ya da vaat metnini "API anahtarı yolu" ile daralt); STT, diyalog ve görsel çağrılarını ekle. Kapı: aynı betik |
| 🟢 | **F-575** · Paket README'lerindeki tek tek AOT cümleleri kapısız | Faz 217 denetimi (2026-10-17): kök `README.md` ve `packages.md` sayıyı ve ad listesini `compatibility.md` tablosundan alır (kapı `check-content.mjs`, 2026-10-17). `src/*/README.md` içindeki "AOT-compatible" / "not AOT-compatible" cümleleri serbest sözcüklerle yazılıdır ve tablodan sapabilir | Cümleleri "bu paket" öznesiyle tek kalıba çek, sonra tablodaki `Native AOT` hücresiyle eşle; ya da bırak (sapma bugün yok) |
| 🟢 | **F-577** · `RemovedFieldFilter` gövdeyi yalnız UTF-8 ve varsayılan `JsonDocument` ayarlarıyla okur | Faz 218 denetimi (2026-10-17; ölçülmedi, okumaya dayanıyor): bağlayıcı `charset` dönüştürmesi yapar ve uygulamanın `JsonOptions`'ını kullanır. `charset=utf-16` gövdede ya da `AllowTrailingCommas` açık bir tüketicide filtre `JsonException` yakalar, gövdeyi geçirir ve kaldırılan alan yine sessizce düşer. Varsayılan seçeneklerde sapma yok (`git grep AllowTrailingCommas src` boş) ve gerçekçi bir istemci bilinmiyor | Bir tüketici `ConfigureHttpJsonOptions` ile okuma seçeneklerini değiştirirse; ya da ikinci bir kaldırılan alan eklenirse filtre bağlayıcının okuma yoluna taşınır |
| 🟢 | **F-656** · stdio MCP sunucusunu kodla bağlama tarifi (D-10) | K-058 korunur (👤 2026-10-22). Tracon'un MCP sunucusu yalnız veridir: `McpServerDefinition.TenantId` zorunludur, kodla kayıt yolu yok. `McpClientTool : AIFunction` ve `StdioClientTransport` `ModelContextProtocol.Core` 2.3'tedir (tip düzeyinde ölçüldü; yeni paket yok). Tüketici açılışta istemciyi kurar ve her tool'u `AddTool` ile kaydeder; yetki, onay, zaman aşımı ve denetim Tracon'undur. İş: önce gerçek host probu, sonra `guides/any-maf-feature.md` § *Ready-made pieces* tarifi. Uyarılar: süreç bütün kiracılara ortaktır, tool listesi açılışta donar, K-060 adlandırması ve MCP kaynakları uygulanmaz. Ölçüm: [`kesif/2026-10-22-maf-yetim-dilimler.md`](kesif/2026-10-22-maf-yetim-dilimler.md) |
| 🟢 | **F-667** · AG-UI istemci tool denetimi en son tanımı okur, run ise deney kolunun sürümünü çözer | Faz 236 denetimi 🟢1: `AGUIInputMapper.CheckToolsAsync` `FindDescriptorAsync` ile katalogdaki güncel tanıma bakar; `PrepareAsync` deney kolunun `AgentVersion`'ını çözer. Kolun sürümü, güncel tanımda olmayan bir istemci tool'u taşıyorsa ve ön yüz onu beyan etmediyse run tool'u çağırır ve sonuç bekler (`AGUIInputMapper.cs`, `AgentTurnService.cs`). Zarar run'ın beklemesidir, güvenlik değil; deney + istemci tool'u birlikte nadir | Denetimi `PrepareAsync`'in çözdüğü tanım üzerinden yapmak (agent'ı bir kez çözmek) ya da kol sürümünü `CheckToolsAsync`'e geçirmek. Kapı: deney kolu eski sürümde istemci tool'u taşıyan bir fonksiyonel test |
| 🟢 | **F-668** · Aynı kesintiyi karara bağlayan iki paralel istek tool'u iki kez koşturabilir | Faz 236 denetimi 🟢2 (kod okuması, ölçülmedi): bekleyen onay oturum geçmişinden okunur ve kayıp yalnız oturum kaydında (`SaveSession`) anlaşılır; yönetim API'sinin run ucunda da aynı (önceden var). AG-UI'da çift tıklama daha olası; bağlantı kopması kaydı atlar ve tool zaten koşmuş olur (`AgentTurnService.cs`, `AGUIEventStream.cs`) | Önce ölç: iki eşzamanlı `resume` ile tool sayacı. İkiyse kusur sınıfı iki yüzeyi birden kapsar (`kusur-giderme`): kararı oturum sürümüne bağlı yazmak ya da karar anında istek başına kilit |
| 🟢 | **F-669** · Yok sayılan AG-UI tool bildirimi `RunEventWriter` yerine doğrudan `IRunStore`'a yazılır | Faz 236 denetimi 🟢3: `tracon.agui.tools.ignored` olayı olay yazarının sink dağıtımını, `RecordToolPayloads` kapalı politikasını ve kayıp sayacını atlar (`AGUIRunStream.RecordIgnoredToolsAsync` ↔ `RunEventWriter`). `SessionWriteConflicted` aynı yoldan yazılır (emsal var); yük yalnız tool adlarıdır | Olayı koşunun kendi yazarı üzerinden yazmak (`AgentRunScope`). Tüketici sink'te bu olayı bekliyorsa |
| 🟢 | **F-670** · Donmuş hat eşiği her major için `1.0.0-rc.1`'dir | Faz 237 denetimi 🟢1 (ölçüldü): `release_line("2.0.0-preview.1", "2.0.0-preview.2")` `Hat: donmuş 2.x yama` verir; `FROZEN_FROM` sabittir (`breaking_changes.py`). 2.x ön sürümleri kırılamaz | 2.0 planlanırsa eşik taban sürümün majoruna göre türer (`<major>.0.0-rc.1`). Faz 238 aynı fonksiyonu kullanır; orada karar verilir |
| 🟢 | **F-671** · Kapı `ValueTask`/`Task<T>` dönen contract testini ayırt etmez | Faz 237 denetimi 🟢2: `contract_test_name` yalnız `Task`/`void` biçimine bakar (bugün 696/696 test bu biçimde). `_show`'un "dosya yok" ile "git hata verdi" birleşmesi Faz 238'de kapandı (`ls-tree` ayrımı, `BaselineError`) | Contract paketine `Task<T>`/`ValueTask` dönen test girerse biçim genişler. Önce ölç |
| 🟢 | **F-676** · `Tracon:UtilityModel` `ModelBinding`'in 19 üyesinden altısını okur | Faz 238 denetimi (ölçüldü): `BindUtilityModel` `Provider`, `Model`, `Temperature`, `MaxOutputTokens`, `TopP`, `ReasoningEffort` okur; `Seed`, `TopK`, `FrequencyPenalty`, `PresencePenalty`, `ToolMode`, `ToolLoop`, `ResponseCache`, `ResponseFormat`, `StopSequences`, `Fallbacks` yazılsa da yok sayılır. Site satırı doğru (altısını yazar); tür ise hepsini sunar. `yapilandirma.txt` yalnız altısını taşır | Bir tüketici özetleyici modele bu alanlardan birini yapılandırmayla vermek isterse bağla (okunamayan değer kuralıyla, K-993) ya da `UtilityModel` için ayrı bir seçenek tipi yaz |
| 🟢 | **F-677** · Sözleşme anlık görüntüleri adı tutar, anlamı tutmaz; birkaç kenar kapsam dışı | Faz 238 denetimi: (a) SSE yük alanı silmek, CLI çıkış kodu anlamı ve ortam değişkenleri (`TRACON_CONNECTION`, `TRACON_TOKEN`) hiçbir dosyada yok; (b) `agent-stream` adları üç kaynak dosyadan regex'le okunur, yeni dosya ya da `eventName:` adlı argüman görünmez; (c) Tracon'un yazdığı sabit run hata tipleri `nameof(TraconException)`, `nameof(WorkflowErrorEvent)`, `nameof(OperationCanceledException)` ve üç SQL dosyasındaki `'orphaned'` katalog dışıdır; (d) `breaking_changes.check` yeşilse `artifacts/package/breaking-changes.json` yazar, sonraki yüzey kapısı kırmızı olsa da dosya kalır (okuyan yok) | Bir tüketici bu yüzeylerden birinde sessiz kırılma yaşarsa ya da yeni bir SSE ucu eklenirse |
| 🟢 | **F-678** · `tracon.audit.write_failures` ve `tracon.run.recording_failures` kiracısız hangi kiracının deliğini söylemez | Faz 238 denetimi 🟢6 (K-1082 sonucu): iki sayaç da düşük kardinaliteli ve alarm içindir; kiracı etiketi kapalıyken yalnız "bir yerde delik var" görünür, audit hatasının logu da kiracı taşımaz (`AuditRecorder.cs:73-77`) | Bir operatör bu iki sayaç için kiracı kırılımı ister ya da incelemede hangi kiracının izinde delik olduğu bulunamazsa: ikisini etiketli bırak ve K-1082'yi daralt |

### Tetik bekleyenler

| Kalem | Engel | Koşul ne zaman oluşur |
|---|---|---|
| **F-560** · Upstream'in deneysel tipi public imzada: `AddLoopEvaluator` ×4 (`LoopEvaluator`) ve `AgentFileStoreContract : TenantIsolationContract<AgentFileStore>` (freeze ölçütü 5) | MAF 1.25: ikisi de `[Experimental("MAAI001")]`. UR-005 A: Tracon'un deneysel katmanı yok, deneysel tip donamaz. Bakımcı kararı (2026-10-17): **MAF'ın GA'sı beklenir**; Tracon kendi ölçüt tipini kurmaz, sözleşmeyi `internal`'a çekmez | Her MAF sürümünde `dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- deneysel` public maruziyeti 0 gösterir (bugün 4 + taban tip). Freeze (`rc.1`) bu tetiğe bağlıdır |
| **F-569** · Kaynak üretecinin Roslyn'i `5.0.0`'a (K-349) | Üreteç tüketicinin derleyicisinde yüklenir; en eski desteklenen SDK bugün 8.0/9.0 (Roslyn `4.14` ve altı). Ölçüldü (2026-10-16): SDK 10.0.100 → Roslyn `5.0.0`, 10.0.402 → `5.9.0`. Hedef **5.0.0**'dır, en yeni değil | [Faz 193](193-NET8-NET9-DUSURME.md) net8/net9'u düşürünce; `dependabot.yml` `ignore` sınırı `> 5.0.0` olur |
| **F-95** · Agent düzeyinde kesinti/devam kancası | MAF yolu kapalı (2026-09-05 ölçümü): kanca alpha pakette, native FFI taşır, kesinti/devam sözleşmesi sunmaz | Kalem yalnız F-141 üzerinden ilerler. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-178** · Model deneme (attempt) telemetrisi | Job/kuyruk metrikleri yarısı Faz 133'te kapandı; deneme telemetrisi yarısı için gerçek bir üretim fallback gecikmesi olayı ölçülmedi, tüketici bilerek erteledi | İlk fallback gecikme olayı ölçülünce. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-179** · Çalışma anı model yönlendirme policy'si | F-178 deneme süresini ölçmüyor; gerçek üretim trafiği yok | F-178 kapanır ve üretim trafiği oluşursa. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen gövdeler* |
| **F-180** · Tam paket koşumunda E2E zaman aşımı — sınıf açık | Vaka kapandı, **sınıf açık**: repro "yük altındaki tam paket koşumu"ydu ve kapanış onu kendi koşullarında tekrar koşmadı; test bir gün sonra aynı imzayla düştü. Ders `kusur-giderme` kapanış kontrolündedir | Sınıf yeniden görülürse **yeni** kusur kaydı açılır. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) |
| **F-199** · Kota eşiği bildiriminin claim SONRASI kaybı | Eşik claim edildikten SONRA webhook/akış yayını başarısız olursa o eşik dönem sonuna kadar kalıcı kaybolur. Düşük risk | Kota webhook/notice teslimi için retry/backoff istenirse (Faz 146 denetim bulgusu) |
| **F-200** · Çok kullanıcılı kota izolasyonu regresyon testi | Garanti **yapısaldır** (`RunEventWriter`'ın run başına özel `Guid`'i); eksik olan yalnız ona adanmış test | `RunEventWriter`/`RunRecordingAgent`'ın run izolasyonu yeniden düzenlenirse (Faz 146 denetim bulgusu) |
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
| **F-629** · Sağlayıcı arka plan yanıtı (`AllowBackgroundResponses`, C-25) | K-030 korunur (👤 2026-10-21): arka plan `store=true` ister; `store` açıkken dönen `ConversationId` Tracon geçmişiyle çakışır ve yalnız onu silen bir halka aşar; OpenAI arka plan modu yanıtı ~10 dk saklar, ZDR ile uyumsuzdur (OpenAI belgesi). İş kuyruğu (`Prefer: respond-async`) aynı ihtiyacı karşılar | Yalnız arka planda çalışan bir model ya da işçi süre sınırını aşan bir model çağrısı ölçülürse. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.3 C-25, §8.4 K5-4 |
| **F-630** · `Microsoft.Agents.AI.Workflows.Declarative` için Tracon `ResponseAgentProvider`'ı (C-21 b, D-03) | K-129 korunur (👤 2026-10-21): koşul yok — XML cümlesi duruyor, Foundry dışı alt tip yok (1.25); +17 paket (Power Fx); Foundry öncelik dışı | K-129'un ölçülebilir koşulu oluşursa (her MAF sürümünde ölçülür). Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.3 C-21 b |
| **F-631** · Uzak agent'ı veriyle tanımlamak (A2A istemci K3, C-19 a) | Kodla bugün K2'dir ve rehberde (`any-maf-feature` § *Ready-made pieces*). Veriyle uç adresi SSRF, egress ve `secret` adla bağlantı tasarımı ister | Konsoldan yönetilen uzak agent isteyen tüketici talebi. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §0 #44 |
| **F-632** · MAF YAML agent içe/dışa aktarma (C-21 a) | Kararlı `Microsoft.Agents.ObjectModel` yeter (Power Fx gerekmez; `=…` ifadesi reddedilir). AOT duruşu ölçülmedi → yeri kütüphane değil `Tracon.Cli` komutu | Talep. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §0 #43 |
| **F-633** · Purview uydu paketi (`Tracon.Purview`, C-20) | Agent katmanı bugün uzantı ya da dekoratörle takılır (rehberde). Tur katmanı Faz 232'nin `ChatClientSlot.ModelCall` yuvasına girer (Tracon altında denenmedi); engel run'ı `Completed` yazar (`content_blocked` değil); M365 kiracısı olmadan ölçülemez | Talep + M365 test kiracısı (yuva Faz 232'de açıldı). Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.3 C-20 |
| **F-634** · Sağlayıcı dosya istemcisi (`IHostedFileClient`, C-23) | OpenAI, Anthropic ve Google SDK'ları uygular; yeni paket yok. Tasarım: kiracı/sağlayıcı/anahtar başına dosya kimliği, BYOK değişimi, ek silinince sağlayıcı dosyasının silinmesi | Büyük eklerin her turda yeniden gönderilmesinin maliyeti ölçülürse ya da talep. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §0 #47 |
| **F-635** · Gerçek zamanlı ses B modu (`IRealtimeClient`, C-22) | K-222 Seçenek A korunur. MEAI gerçek zamanlı tool çağırıcısı onay işlemez (onaylı tool onaysız koşar); oturum bir run değildir | K-222'nin koşulu: A modunun gecikmesi ölçülür ve kabul edilemez bulunur. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §0 #46 |
| **F-636** · Kalıcı Docker shell modu (C-13 b) | Stateless mod bugün `AddTool` ile Tracon onayıyla çalışır (rehberde). Kalıcı mod oturum başına yürütücü ömrü ister; Tracon tool'u singleton'dır. `Tools.Shell` preview → K-1037 uydu hattı | Talep. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.3 C-13 b |
| **F-637** · Hyperlight CodeAct (C-13 c, D-07) | Native yalnız `linux-x64`/`win-x64` (0.8.0), hipervizör ister; köprü iptal ve `Services` geçirmez; onay `execute_code` başınadır | Platform listesi genişler ya da talep + KVM'li ortam. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.3 C-13 c |
| **F-638** · Foundry: model sağlayıcısı, Foundry agent'ı, hafıza, eval (C-19 b/c, D-08) | Öncelik dışı (👤 2026-10-21) | Bakımcı önceliği değişirse; önce `UseAzureOpenAI` ile bir Foundry kaynağının `/openai/v1/` ucu manuel case olarak ölçülür. Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.3 C-19 b |
| **F-657** · MCP OAuth'ta istemci kimliği belgesi (CIMD, C-14 d) | Tracon OAuth'u yalnız önceden kayıtlı istemci kimliğiyle kurar: `oauthClientId` zorunludur (`GovernanceEndpoints.cs:1128`), `ClientOAuthOptions.ClientId` ondan gelir (`McpTransportFactory.cs:144`). SDK 2.3 `ClientMetadataDocumentUri` taşır (decompile, 2026-10-22). CIMD için Tracon host'u HTTPS üzerinde bir istemci üstveri belgesi sunar; `secret` yoktur (K-059 ile uyumlu). Ekosistem (2026-10-22, ikincil kaynaklar): MCP 2025-11-25 CIMD'yi tercih eder, 2026-07-28 DCR'yi kullanımdan kaldırır | Elle istemci kaydı sunmayan (yalnız CIMD ya da DCR) bir MCP sunucusuna bağlanmak isteyen tüketici. Ölçüm: [`kesif/2026-10-22-maf-yetim-dilimler.md`](kesif/2026-10-22-maf-yetim-dilimler.md) |
| **F-658** · MCP task istemcisi: uzun süren uzak tool (C-14 e) | Tracon MCP tool'unu run içinde eşzamanlı çağırır; uzun tool run'ı zaman aşımına kadar tutar. Sunucu tarafı vardır (`RunBackedMcpTaskStore`); `ModelContextProtocol.Extensions.Tasks` 2.3.0 yalnız `Tracon.AspNetCore`'dadır. Tasks MCP 2026-07-28 ile uzantıya taşındı (SEP-2663) ve istek başına katılım ister. Ekosistem (2026-10-22): sunucu uygulamaları erkendir | Task destekli bir MCP sunucusu ve onu çağıran tüketici. Görevi bekleyen run Faz 235'in askı desenini izler. Ölçüm: [`kesif/2026-10-22-maf-yetim-dilimler.md`](kesif/2026-10-22-maf-yetim-dilimler.md) |
| **F-659** · Bildirimsel workflow topolojisi: dallanma ve alt workflow (C-18 b) | `WorkflowKind` bilerek kapalı listedir (`WorkflowKind.cs:9-15`: arayüz workflow'u yalnız katalog agent'larını bağlar); `Nodes` yalnız `Sequential` içindir (K-494). Kod workflow'u (`AddWorkflow(name, factory)`) bütün topolojiyi bugün Tracon altında koşar (K1). Dallanma koşulu ancak kodda kayıtlı adlı yüklem olabilir (K2) | Arayüzden dallanan workflow isteyen tüketici **ve** kapalı liste ilkesinin açılması (👤). Ölçüm: [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §8.2 C-18 |
| **F-660** · A2A HTTP+JSON bağlaması (C-15 c) | Tracon A2A ucu yalnız JSON-RPC bağlamasını ilan eder (`TraconA2AExtensions.cs:201`). A2A v1.0 (2026-03, ikincil kaynaklar) üç bağlama tanımlar; en yaygını JSON-RPC'dir. A2A SDK HTTP+JSON'u taşır; MAF ucu değer katmaz (MAF-KAPSAMA §8.2) | Yalnız HTTP+JSON konuşan bir A2A istemcisi. Açılırsa kartta ikinci `AgentInterface`; kiracı, rol ve izolasyon filtreleri iki bağlamada aynı. Ölçüm: [`kesif/2026-10-22-maf-yetim-dilimler.md`](kesif/2026-10-22-maf-yetim-dilimler.md) |
| **F-661** · Bir tool çağrısı ikinci kez kullanıcıdan bilgi isteyebilsin (çok turlu elicitation) | Faz 235'te bir çağrı **en çok bir kez** askıya alınır: yeniden çalışan çağrı başka bir soru sorarsa `decline` alır (`mismatch`). İkinci tur, önceki cevapların askılar arasında saklanmasını ister (yeniden çağrı sunucuyu baştan koşturur ve ilk soruyu yine sorar); cevap değeri oturuma ya da denetime girmemeli (K-059'un gerekçesi) | İkinci tur isteyen gerçek bir MRTR sunucusu ve onu kullanan tüketici. Emsal ölçüm: `McpElicitationTests.A_replayed_call_that_asks_something_else_is_declined_not_given_the_old_answer` |
| **F-662** · Kuyruklu run'da ve webhook'ta askıdaki tool girdisi | `Prefer: respond-async` run'ı bugün sunucunun isteğine `decline` verir ve run olayı yazar. Bekleyen isteğin posta kutusu (`pending_approvals`'ın eşi) migration ister; `input.pending` webhook'unun tüketicisi kuyruk yolu olmadan yoktur | Kuyruklu run'ında MCP elicitation isteyen tüketici. İki iş tek tablo ve tek webhook olarak planlanır |
| **F-663** · Elicitation cevabı için dış yüzeyler: OpenAI uyumlu uçlar, A2A, MCP sunucusu | Telde cevap kanalı yok: üçü de `decline` verir. A2A'nın `input-required` durumu ve MCP görevinin `input-required` hâli aynı `ToolInputResponse` sözleşmesini kullanabilir (ad bu yüzden genel tutuldu) | F-619 (A2A görev modu) planlanınca, tek tasarım olarak |
| **F-664** · MCP `url` modu elicitation | Sunucu kullanıcıyı bir adrese yollar (OAuth, ödeme). Ayrı güven modeli ister: adres kullanıcıya gösterilir, açılışı onaylanır, `secret` istemciye uğramaz. Bugün bildirilmez; istek `decline` alır | Adrese yönlendiren bir sunucu ve onu kullanan tüketici |
| **F-665** · Aynı elicitation isteğine eşzamanlı iki cevap | Faz 235 denetimi: iki istek aynı `requestId` için cevap getirirse ikisi de eşleşmeden geçer, ikisi de denetlenir ve tool iki kez yeniden çağrılır. Oturum başına serileştirme yok; onay cevaplarında da aynı sınıf var | Bir oturumu iki sekmeden süren tüketici ya da otomatik yeniden deneme. Çözüm oturum kilidi ve bekleyen kaydın tek tüketimidir; onay yolunu da kapsar, tek tasarım olarak |
| **F-666** · MCP elicitation yeteneği için kapatma anahtarı | Yetenek her bağlantıda koşulsuz bildirilir; bağlantı `(kiracı, sunucu)` başınadır, yüzeye göre değişmez. Yeteneği denetleyip yedek yola düşen sunucu, cevap kanalı olmayan yüzeylerde (OpenAI uyumlu uçlar, oturumsuz run) artık `decline` alır; bugünkü hâl tool'un düşmesiydi | Yeteneği denetleyip yedek yola düşen bir sunucu ve onu kullanan tüketici. Aday: `TraconMcpOptions` içinde sunucu başına kapatma; yüzeye göre bildirim bağlantı paylaşımını böler |
| **F-698** · Skill sürüm sabiti (F-409'un skill yarısı) | K-082 (kullanıcı kararı): skill geçmişi tutulmaz; `AgentSkillDefinition.Version` yalnız sayaçtır ve silmede yeniden başlar (`TraconSkillsSource.cs:39-44` her çağrıda güncel içeriği okur). Faz 248 agent numarasını, Faz 250 alt agent sabitini kapatır | K-082 yeniden açılırsa, F-408 (skill geçmişi) ile birlikte |

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
| 🟢 | **F-681** · Playground, Loop'un geri bildirim `update`'ini (`role: "user"`) sonraki iterasyonun paragrafına katar | Faz 241 (K-1086): iki çerçeve arasındaki `update` modele verilen geri bildirimi taşır (`Reviewer findings (round 1): …`); `foldUpdate` rolü okumaz, metin cevabın başına yapışır. Ayraç iki metni ayırır, geri bildirim yine görünür | Gizlemek mi, ayraç altında soluk bir not olarak göstermek mi kararı; `role` yalnız canlı akışta var, kayıtlı oturum aynı katlamayı kullanır (`foldMessages`). Önce ölçüm: oturum ekranında geri bildirim nasıl görünüyor |
| 🟢 | **F-682** · `LoopIterationCompleted` payload'ı `RecordToolPayloads` kapalıyken `null`; çerçeve ve ayraç yalnız `text` taşır | Faz 241 denetimi: `RunEventWriter` payload'ı seçenek kapalıyken yalnız `KeepsPayloadWithPayloadsOff` listesindeki `Custom` türleri ile `WorkflowRequest`/`WorkflowResponseApplied` için korur. Loop olayının payload'ı serbest metin taşımaz (sayaç, bayrak, ölçüt türü) ama muaf değildir; çerçeveyi okuyan istemci cevabı bulamaz, playground ayraç çizmez. Site, OpenAPI ve CHANGELOG koşulu söyler | Kayıtlı veriyi değiştirir (bakımcı kararı): olayı muaf tutmak `QuotaThreshold` ile aynı gerekçedir. Önce ölçüm: gizlilik için seçeneği kapatan tüketici var mı |
| 🟢 | **F-683** · Loop çerçevesi: `depth == 0` koruması ve istemci kopması testsiz | Faz 241 denetimi: koruma silinse `A_sub_agents_loop_writes_to_its_own_run…` yine geçer (çocuk seçenekleri kancayı zaten taşımaz); kopan bağlantıda kancanın yazımı `OperationCanceledException` ile biter ama bu yol sınanmaz | Kopya yapıcıya yeni bir çocuk yolu eklenirse koruma devreye girer. Kapı: çocuk seçeneklerini `Clone()` ile kuran bir çağıranın kancayı taşımadığını kanıtlayan birim test ve `RequestAborted` ile biten bir akış testi |
| 🟢 | **F-684** · `TraconChildRunTimeoutException`, `TraconRunBudgetExceededException` ve `ChildRefusalMode.Fail` XML dokümanı yalnız "workflow step" der | Faz 242 denetimi: bu istisnalar artık `agentReview` reviewer'ının reddinde de atılır (`ChildAgentInvoker` `callKind`). Davranış ve ileti doğrudur; yalnız `TraconException.cs`, `TraconRunBudgetExceededException.cs` ve `ChildRefusalMode.cs` açıklaması eksik | Public XML değişir, site `api/` sayfaları yeniden üretilir; bir sonraki `ChildAgentInvoker` tüketicisiyle birlikte yazılır |
| 🟢 | **F-685** · Run ucunun OpenAPI açıklaması `loop.iteration-completed` yükünü eksik sayar | Faz 242 denetimi: `AgentEndpoints.cs:284` `criteria`'yı, `hasFeedback`'i ve `failsRun`'ı anmaz; `docs-site/http-api.md` üçünü de yazar. Metin OpenAPI belgesine, iki istemciye ve TS şemasına girer | Açıklama değişince dört üretilmiş yapıt yenilenir; yük alanı eklendiği her fazda yeniden bayatlar, listeyi tek yerden üretmek ayrı iş |
| 🟢 | **F-686** · Derinlik reddi iletisi model için yazılmıştır ("Finish the work yourself…") ve `Fail` kipinde operatöre yanlış yön gösterir | Faz 242 denetimi: `ChildAgentInvoker.RefuseAsync` aynı metni her kipte verir. Workflow adımında ve loop reviewer'ında doğru ayar `Tracon:AgentGraph:MaxDepth`'tir. Önceden vardı, bu faz kötüleştirmedi | Reddin metni testlerde sabit olabilir; `Fail` kipine ayrı metin, mevcut `ApprovalRefusal` deseniyle |
| 🟢 | **F-691** · `SpeechDialogueCapabilities` yalnız `MaxVoices` taşır; Gemini tam iki ses ister | Faz 244 denetimi: `dialogue` alanına bakan çağıran tek sesli bir diyalog planlayabilir; istek ağdan önce reddedilir (`takes exactly 2 voices`) ama sınır tanımda görünmez | Public API'ye `MinVoices` eklemek ayrı karar ve `K-*` ister; ikinci bir alt sınırlı sağlayıcı çıkarsa yazılır |
| 🟢 | **F-692** · `UseGoogleSpeech` `UseGoogle` olmadan çağrılırsa hata kayıtta değil ilk çözümlemede çıkar | Faz 244 denetimi: `GET /api/voice/providers` tüm sağlayıcılar için 500 verir ve ileti iç tip adını (`GoogleModelProvider`) taşır; `UseGoogleImages` ve `UseOpenAISpeech` aynı tembel davranıştadır, test mevcut hâli sabitler (`GoogleSpeechBuilderExtensionsTests`) | Üç `Use*Speech/Images` çağrısı için ortak bir başlangıç doğrulayıcısı (`SpeechClientBridgeValidator` deseni); tek tek düzeltmek davranışı üç yerde ayrıştırır |
| 🟢 | **F-693** · Konuşma soketi Gemini'nin ham PCM akışını oynatamaz; varsayılan sağlayıcı olarak sınırlı | Faz 244 denetimi: `voice-panel.tsx` her segmenti `decodeAudioData` ile tam dosya olarak çözer; Gemini akışı başlıksız PCM verir ve `OutputMediaType` varsayılanı `audio/mpeg`'tir. Şimdilik belgede ve XML'de "Gemini'yi adlı sağlayıcı bırak" yazılı | Panel PCM segmentini `AudioContext` ile çalacak biçimde genişlerse ya da sürücü akışı segment başına WAV'a sararsa |
| 🟢 | **F-694** · `IResponseSchemaSupport` sağlayıcı yazarı kılavuzunda geçmiyor | Faz 245 denetimi: kardeşleri `ISamplingSupport` ve `IHostedToolSupport` `guides/model-providers.md`'de "Implement X next to IModelProvider" diye anılıyor; üçüncü taraf sağlayıcı raporu doldurabileceğini keşfedemez. Site ağırlık tavanı sıfır boşlukta (en ağır sayfa 58.964 / 59.000 B), bir cümle bile sayfa kesimi ister | Ağırlık boşluğu açılınca ya da kılavuz bölününce |
| 🟢 | **F-695** · Strict + OpenAI'nin reddettiği yapı kayıtta ve derlemede durmaz; yalnız rapor uyarır | Faz 245 (K-1096, kullanıcı kararı): kod uyarıdır, çünkü cevap kiracıdan bağımsızdır ve kayıt yolu hatayı engel sayar. Çalışmayacak tanım `valid: true` döner; ilk istek `400` verir. Kiracının uç adresini bilen bir derleme yolu (`CompileAsync` kiracı bağlamıyla) kuralı kiracıya göre koşabilir | Bir tüketici "kaydedilemesin" isterse ya da kiracı uç adresi doğrulamaya taşınırsa |
| 🟢 | **F-696** · Azure OpenAI ve uyumlu sunucular için strict reddi ölçülmedi | Faz 245: reddedilen yapı listesi yalnız `api.openai.com`'da (iki yüzey) ölçüldü; Azure kaynağı ve bir uyumlu sunucu (vLLM, Ollama) yoktu. Ölçülene kadar liste boş ve rapor susar | Bir Azure OpenAI kaynağı ya da uyumlu sunucu sondaj için hazır olunca (`MT-GUARD-117` sondajı) |
| 🟢 | **F-697** · Şemayı yeniden yazılamayan tanım (`default` yanında metin olmayan `description`) raporda iz bırakmaz | Faz 245 denetimi: istemci `InvalidOperationException` verir; doğrulama o sağlayıcı için `responseSchemas` girişi yazmaz ve uyarı vermez, ilk çalıştırma düşer. Bir bulgu kodu ("şema yeniden yazılamıyor") bunu görünür yapar | Böyle bir tanım bir tüketicide görülürse |
| 🟢 | **F-701** · Yönetilen arama tool'ları: replay `OnDemand` aracını oynatmaz, üç yol testsiz | Faz 246 denetimi: `toolTransform` yalnız `ChatOptions` tool'larını sarar (`AgentDefinitionCompiler.cs:645-657`); `search_conversation_memory` sağlayıcı bağlamından gelir, replay'de canlı koşar (önceden de böyleydi, kod agent'ı replay edilemez; DB agent'ında `OnDemand` replay'i ölçülmedi). Testsiz: `search_knowledge` işleyicisinin gördüğü kiracı ile `VectorSearchRequest.TenantId`, `OnDemand` aramada depo hatası (zaman aşımı dışı), hafıza aracında doğrulayıcı ve zaman aşımı (yalnız ortak `Govern` yolu) | Önce ölçüm: DB agent'ında `OnDemand` + `ReplayTools`. Tüketici replay'de hafıza aramasının canlı koştuğunu görürse |
| 🟢 | **F-703** · `TimeSpan` anahtarında çıplak sayı gün sayılır | Faz 247 denetimi (ölçüldü): `TimeSpan.TryParse("30", Invariant)` = `30.00:00:00`; `Tracon:Webhooks:Timeout=30` otuz gün olur. `"1,5"` → 15 ile aynı sessiz yanlış okuma sınıfı, ama okunabilen değer; eski davranış | Karar: çıplak sayı reddedilir mi, saniye mi sayılır; tek okuyucu `ConfigurationValueKinds.TryTimeSpan` |
| 🟢 | **F-706** · Çağrı grafı ve döngü denetimi sabitli sürümün adlarıyla kurulmaz | Faz 250 denetimi: `AgentCallGraph` kenarları `descriptor.CallableAgentNames`'ten (güncel sürüm) kurar. `B` v1 `[A]`, güncel `B` `[]` iken `A`'yı `[B@1]` ile kaydetmek `201` alır ve çalışmada `A→B@1→A…` derinlik sınırına kadar model harcar (`CallableAgentVersionPinTests`, derinlik sınırı vakası); tersi sahte `cycle` ile `400` alır | Düğümü `(ad, sürüm)` yap: sabitli kenar için `IAgentDefinitionStore.GetVersionAsync` ile o sürümün adlarını oku. Kapı: iki yönü kanıtlayan `AgentCallGraph` testi |
| 🟢 | **F-707** · Sabitli çocuğun açıklaması her derlemede ve her çağrıda yeniden okunur | Faz 250 denetimi: `CompileCachedAsync` önbellek isabetinde de `ResolveDependenciesAsync` → `CallableAgentResolver.DescribePinnedAsync` → `GetVersionAsync` koşar. Bir sürümün içeriği değişmez (Faz 248 sayacı); ölçülmedi | Önce ölçüm: sabitli çocuk sayısı × çözümleme başına okuma. Gerekirse `(kiracı, ad, sürüm)` anahtarlı küçük bir açıklama önbelleği; silmede boşalması K-1100 ile uyumlu olmalı |
| **Ölçüm bekliyor — F-ID'leri** | F-51 · F-94 · F-96 · F-97 · F-99 · F-101 · F-123 · F-128 · F-154 · F-156 · F-157 · F-159 · F-160 · F-161 · F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. Kanıt üretmeden aday olmaz |
| **Ölçüm bekliyor — A-ID'leri** | A01 (kalan manifest bağlama) · A02 · A03 · A05'in ETag ve provenance dilimleri · A06 · A07 · A08 · A09 · A10 · A11 · A12 · A13 · A14 · A15 · A17 · A18 | 🚨 Bunlar **F-NN değildir** — 2026-09-07 tüketici analizi turunun rapor içi izleme kimliğidir; biri seçilirse o zaman aday numarası alır. Değer/maliyet/risk yargısı ve gereken kanıt [tam raporun](arsiv/incelemeler/2026-09-07-tuketici-analizi-codebase-olcumu.md) §5'indedir. **A17/A18 mimari ret DEĞİLDİR** — mevcut yetenekle çözülemeyen somut bir vaka çıkarsa yeniden değerlendirilirler |

### F-ID tahsis kuralı

Numara **geri dönüştürülmez** ve bir numara **tek kaleme** aittir. Sıradaki
numara: **F-710**.

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
