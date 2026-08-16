# Ortak Kuyruk — Koşum Sonuçları (2026-08-13)

> Bu dosya `KOSUM-PLANI.md` §7'nin "Ortak kuyruk" bölümündeki K-1..K-9
> oturumlarının birleşik sonuç kaydıdır. Şerit sonuç dosyalarıyla aynı biçimi
> kullanır (`00-INDEKS.md` §6 hata şablonu), tek fark: dokuz oturumun tamamı
> tek dosyada birikir (dokuz ayrı ajan/worktree yerine tek ajan sırayla koştu).

## Ortam

- Ana kopya (`/Users/farukatasoy/Desktop/projects/AgentPrism`) üzerinde,
  worktree/dal açılmadan doğrudan `main` üzerinde koşuldu (kullanıcı talimatı).
- Port `5080`, kalıcılık **bellek içi** (varsayılan — üç bağlantı dizesi de
  boş) — dosya 14/15/17'nin çoğu case'i sağlayıcıdan bağımsız; PostgreSQL
  gerektiren case'ler kendi notunda işaretlenir.
- `dotnet user-secrets` deposu paylaşılan `agentprism-sample-api` kimliğini
  kullanır; okuma serbest, geçici config değişiklikleri (`MaxSkillsPerAgent`
  vb.) her seferinde case sonunda `remove` ile temizlendi.
- Sağlayıcı modeli: OpenAI `gpt-5.4-mini` (§2.5 maliyet kuralı).
- Playwright MCP ile arayüz case'leri koşuldu; önceki oturumdan kalan yetim
  Chrome süreci (`ms-playwright-mcp/mcp-chrome-f333cab`) temizlenip yeniden
  başlatıldı.

## Sapmalar

- **DB reset komutları sınıflandırıcı tarafından engellendi.** `docker exec
  ap-pg psql ... DROP SCHEMA` ve benzeri komutlar otomatik izin
  sınıflandırıcısı tarafından reddedildi; ajanın kendi `.claude/settings.local.json`
  dosyasını yazması da (kullanıcı onayına rağmen) engellendi — bu sert bir
  sınır. Kullanıcı dosyayı elle oluşturdu (`docker exec -i ap-pg psql*` ve
  `docker exec -i ap-mssql*` için allow kuralı). Bu olay, ortak kuyruk
  dosyalarının çoğunun kalıcılık sağlayıcısından bağımsız olması sayesinde
  K-1'i bloklamadı — bellek içi ile koşuldu, `rm -f` (SQLite silme) zaten
  sınıflandırıcı tarafından engellenmiyordu.
- **Süreç yönetimi hatası (K-1 içinde, MT-SKILL-021 ilk denemesi).** `pkill -f
  "dotnet.*AgentPrism.Api.dll"` deseni apphost ikili adını (`AgentPrism.Api`,
  `dotnet AgentPrism.Api.dll` DEĞİL) yakalamadı; "yeniden başlatma" aslında
  eski süreci hiç durdurmadı, yeni `dotnet run` "address already in use" ile
  sessizce başarısız oldu ve `MaxSkillsPerAgent=1` hiç uygulanmadı. PID ile
  `kill -9` edilip doğru ortam değişkenleriyle yeniden başlatıldıktan sonra
  case doğru sonucu verdi (bkz. case notu). Sonraki tüm yeniden başlatmalar
  `lsof -tiTCP:5080` ile PID bulup `kill -9` deseniyle yapıldı.
- **Ajan hatası: OpenAI `user-secrets` anahtarı yanlışlıkla boşaltıldı (K-6,
  17 §5, MT-EVAL-044 hazırlığı sırasında).** MT-EVAL-044 için OpenAI'yi
  geçici kapatmak amacıyla anahtarı bellekte tutup `remove` edip sonra geri
  `set` etmeyi hedefleyen tek bir `bash -c` betiği yazıldı; betik `set -e`
  taşıyordu ama `DEĞER=$(python3 ...)` atamasının komut ikamesi başarısız
  olduğunda (UTF-8 BOM nedeniyle) `set -e` bunu YAKALAMADI — betik boş
  `DEĞER` ile devam etti, gerçek anahtarı `remove` etti, testi çalıştırdı,
  sonra "geri yükleme" adımında anahtarı BOŞ DİZEYLE `set` etti. Orijinal
  değer hiçbir dosyaya yazılmamıştı (proje kuralına uyularak) ve sıkıştırma
  (compaction) sonrası konuşma bağlamında da kalmamıştı — ajan kendi
  başına kurtaramadı. Kullanıcıya doğrudan bildirildi; kullanıcı anahtarı
  tekrar paylaştı, `dotnet user-secrets set` ile geri yüklendi, uygulama
  yeniden başlatılıp gerçek bir `gpt-5.4-mini` çağrısıyla doğrulandı. Diğer
  hiçbir anahtar (Anthropic/Google/OpenRouter/ElevenLabs/Voice/Ui) etkilenmedi.
  Ders: birden fazla adımlı, geri-yükleme içeren `secret` betiklerinde
  `set -e`'ye güvenmek yerine her komutun çıkış kodu AYRICA denetlenmeli;
  boş/başarısız bir okuma ASLA bir sonraki `set` adımına girdi olarak
  kullanılmamalı.

## K-1 — 14 §1–2 (MT-SKILL-001..014, 020..025), 20 case

**Sonuç:** 20 Geçti, 0 Kaldı.

### HATA-K-001 — `POST/PUT /api/agents`, bilinmeyen skill adını SAVE zamanında hiç doğrulamıyor — ✅ DÜZELTİLDİ (2026-08-14, K-404)

- **Case:** MT-SKILL-020
- **Önem:** Yüksek
- **İzlek:** C
- **Ortam:** macOS arm64 · net10 · bellek içi kalıcılık · sağlayıcı N/A (model çağrılmadı)

**Beklenen**
`skillNames` alanında var olmayan bir skill adı taşıyan bir agent'ı `POST /api/agents` ile kaydetmeye çalışmak `400` ile reddedilir (`AgentDefinitionValidator.CheckSkillsAsync`, `code: "unknown_skill"`).

**Gerçekleşen**
`HTTP: 201 Created` — agent hiçbir doğrulama hatası olmadan kaydedildi.

**Yeniden üretme**
1. `curl -X POST $APU/api/agents -d '{"name":"hayalet-skilli-agent","model":{"provider":"openai","model":"gpt-5.4-mini"},"skillNames":["hic-var-olmayan-skill"]}'`
2. Yanıt `201`, gövdede kaydedilen tanım aynen döner.

**Kanıt**
- `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs:247-283` (`CreateAgentAsync`) yalnız `Validate(request)` (temel şekil) ve `ValidateCallGraphAsync`'i çağırıyor.
- `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs:340-372` (`UpdateAgentAsync`) aynı desende — `AgentDefinitionValidator` hiç çağrılmıyor.
- `AgentDefinitionValidator.ValidateAsync` (skill/tool/callable-agent varlık denetimini içeren, `CheckSkillsAsync` dahil, `AgentDefinitionValidator.cs:97`) yalnız ayrı `POST /api/agents/validate` ucundan (`ValidateAgentAsync`, `AgentEndpoints.cs:300-320`) çağrılıyor — bu uç bir şey KAYDETMEZ, istemci ayrıca çağırmadıkça hiçbir etkisi yok.
- Canlı istekle doğrulandı: yukarıdaki `curl` `201` döndü.

**Kapsam**
Genel — hem `POST /api/agents` (create) hem `PUT /api/agents/{name}` (update) etkilenir; yalnız skill değil, aynı kod yolunun kapsadığı diğer varlık denetimleri de (tool adı, callable-agent adı — `AgentDefinitionValidator.cs` içindeki diğer `Check*Async` metotları) muhtemelen aynı şekilde SAVE zamanında hiç çalışmıyor; bu koşum yalnız skill yüzeyini ölçtü, diğerleri ayrı bir doğrulama gerektirir. Kullanıcı arayüzü "Validate" düğmesini ayrıca çağırdığı için arayüz yolunda bu boşluk gizli kalabilir; doğrudan API tüketen istemciler etkilenir.

**Düzeltme (2026-08-14, K-404)**
`AgentDefinitionValidator` artık `CreateAgentAsync` ve `UpdateAgentAsync` içine tam olarak kablolandı — her iki uç da yeni `ValidateEntitiesAsync` yardımcı metodunu `ValidateCallGraphAsync`'ten sonra, mevcut isim çakışması/varlık denetiminden önce çağırıyor; doğrulama başarısızsa `400` + `"Tanim gecersiz"` başlığı + hata mesajları döner (`AgentEndpoints.cs`). Yalnız skill değil, `AgentDefinitionValidator.ValidateAsync`'in kapsadığı tüm `Check*Async` denetimleri (tool adı, callable-agent adı dahil) artık SAVE zamanında çalışıyor. Ampirik doğrulama: yukarıdaki `curl` artık `HTTP: 400` ve beklenen `unknown_skill` mesajını döndürüyor; regresyon kontrolü — geçerli (skill'siz) bir agent hâlâ `201` alıyor, bilinmeyen skill içeren bir `PUT` de aynı şekilde `400` alıyor. Dört kapı (`build`/`test`/`pack`/`format`) temiz.

---

## K-2 — 14 §3–5 (MT-SKILL-030..033, 040..046, 050..056), 18 case

**Sonuç:** 18 Geçti, 0 Kaldı.

Kusur bulunmadı. Tek gözlem: MT-SKILL-032'de (reddet senaryosu) `gpt-5.4-mini`
reddedilen `load_skill` çağrısını bir kez daha denedi ve ikinci bir onay kartı
üretti — bu kod tarafında bir tekrar mekanizması değil, gerçek modelin kendi
kararıydı; ikinci ret sonrası tur beklenen şekilde tamamlandı
(`FATURA_SKILL_ACTIVE` hiç üretilmedi). Not olarak case'in kendi "Gerçek
sonuç" alanına yazıldı, ayrı bir `HATA` açılmadı (üretim/sağlayıcı
değişkenliği, kod kusuru değil).

Sapma: MT-SKILL-055'in PostgreSQL doğrulama sorgusu (§ "Doğrulama sorgusu")
bellek içi kalıcılıkla koşulduğu için çalıştırılmadı; HTTP/arayüz davranışı
beklenen sonucu zaten kanıtladı.

---

## K-3 — 14 §6–7 (MT-SKILL-057..063, 070) + 15 §1 (MT-WF-001..020), 28 case

**Sonuç:** 21 Geçti, 7 Kaldı. **Dosya `14` (SKILL-VE-SCRIPT) TAMAMEN BİTTİ (47/47, MT-SKILL-071 hariç — o zaten kapsam dışı not).**

### HATA-K-002 — 🚨 KRİTİK: Script çalıştırma özelliği (Faz 11) tamamen çalışmıyor — `JsonSerializerOptions` çöküyor — ✅ DÜZELTİLDİ (2026-08-14, K-400)

- **Case:** MT-SKILL-058 (Kritik) — ayrıca MT-SKILL-059/060/061/062/063/070'i de aynı kök nedenle bloke etti
- **Önem:** Kritik
- **İzlek:** B
- **Ortam:** macOS arm64 · net10 · bellek içi kalıcılık · OpenAI `gpt-5.4-mini`

**Beklenen**
`UseSkillScripts()` açık ve bir skill'e kayıtlı, izinli bir script bağlıyken, o skill'i taşıyan bir agent çalıştırıldığında script gerçekten çalışır ve sonucu modele döner.

**Gerçekleşen**
Script içeren HERHANGİ bir skill gerçekten etkinleştirildiğinde (`UseSkillScripts()` + `AllowStoredScripts=true`), o skill'e sahip agent'a gönderilen HER istek, model hiç çağrılmadan, şu hatayla çöküyor: `InvalidOperationException: JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.`

**Yeniden üretme**
1. `agentPrism.UseSkillScripts(o => o.PlatformIsolationAcknowledged = true);` (geçici kod), `Interpreters:sh`, `AllowStoredScripts=true` ayarla, yeniden başlat.
2. `sh` script'i taşıyan bir skill oluştur, o skille agent bağla, script'e izin ver.
3. O agent'a HERHANGİ bir mesaj gönder (Playground veya `POST /api/agents/{name}/run`).
4. `run` `Failed` durumuna düşer, hata `error.message` alanında görünür.

**Kanıt**
- `src/AgentPrism.Core/Skills/AgentPrismSkillsSource.cs:10`: `private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);` — resolver hiç ayarlanmamış.
- Aynı dosya `:71-81`: `_scripts is { StoredScriptsEnabled: true }` iken bu `SerializerOptions` MAF'ın `skill.AddScript(script.Name, delegate, description, SerializerOptions)` çağrısına geçiriliyor; MAF içeride `MakeReadOnly()` çağırıyor (resolver popüle edilmeden), .NET'in "TypeInfoResolver olmadan salt-okunur işaretlenemez" korumasını tetikliyor.
- `StoredScriptsEnabled: false` iken (script kaydı/izin katmanı, §4/§5) bu kod yolu (`skill.AddScript`) hiç çağrılmadığı için sorun gizli kalıyor — bu yüzden 14 case (§4+§5) sorunsuz geçti ama ÇALIŞTIRMA katmanının tamamı (§6) çöküyor.
- İki ayrı yoldan doğrulandı: Playground üzerinden (gerçek kullanıcı akışı) ve doğrudan `POST /api/agents/{name}/run` — ikisi de aynı, tam belirlenimli hatayı üretti.

**Kapsam**
Faz 11'in "gerçek çalıştırma" özelliği (script'lerin sandbox'ta çalıştırılması) yayınlanan hâlde TAMAMEN işlevsizdir. `UseSkillScripts()` çağıran ve saklı script'i olan HER tüketici aynı çökmeyi yaşar — bu bir kenar durum değil, özelliğin ana yoludur. Script kaydı/izin (grant) katmanı (§4/§5, K-2'de 14/14 geçti) etkilenmez çünkü o katman `skill.AddScript`'i hiç çağırmaz.

**Düzeltme (2026-08-14, K-400)**
İki ayrı kök neden kodlandı: (1) `AgentPrismSkillsSource`'a kaynak-üretilen `AgentPrismSkillsJsonContext` (`string`/`JsonElement`/`object`) `TypeInfoResolver` olarak bağlandı — yansıma yok, AOT korunuyor. (2) Düzeltme #1 TEK BAŞINA yetmedi — MAF'ın kendi kodunda (`Microsoft.Agents.AI.AgentSkillsProvider`) İKİNCİ bir çöküş ortaya çıktı: `CreateStoredScriptDelegate`'in `string?` (nullable) parametresini MAF (nullable olsa bile) "required" işaretliyor, modelin argümansız bir script için doğru biçimde gönderdiği JSON `null`'ini "değer eksik" sayıp reddediyordu. `SkillScriptSupport.CreateStoredScriptDelegate` parametresi `string arguman = ""` (varsayılan değerli) yapılarak MAF'ın alanı "required değil" yayınlaması sağlandı. MT-SKILL-058 yeniden koşulup uçtan uca doğrulandı: `load_skill` onayı → `merhaba` script onayı → gerçek çalıştırma → `exit_code: 0\nstdout:\nmerhaba-agentprism\n` → model nihai yanıtında doğru metin. Dört doğrulama kapısı (build/test/pack/format) temiz. MT-SKILL-059..063/070 bu düzeltme oturumunda tek tek yeniden koşulmadı (kapsam dışı bırakıldı) — engel kalktı, gelecek bir koşumda normal şekilde tekrar denenebilir. Ayrıca (ilgisiz, aynı koşumda bulundu): `SSH.NET` `GHSA-q939-rpr3-3284` (`NU1903`, tüm çözümün restore'unu kırıyordu) `Directory.Packages.props`'a yama sürümüyle (`2026.0.0`) sabitlendi.

---

Diğer bulgular: MT-WF-020'nin dokümanı düzeltildi (kod kusuru değil) — beklenen "`event: error`" SSE çerçevesi yerine gerçek davranış normal `event: event` içinde `type: "RunFailed"` domain event'i taşıyor; mesaj içeriği doğru, yalnız çerçeve varsayımı yanlıştı. Ayrıntı MT-WF-020'nin case notunda.

---

## K-4 — 15 §2–6 (MT-WF-030..035, 040..044, 050..053, 060..066, 070..073), 26 case

**Sonuç:** 23 Geçti, 3 Kaldı.

### HATA-K-003 — 🚨 KRİTİK: Magentic + plan onayı, onay sonrası devamda `ExecutorFailed`/`RunFailed` ile çöküyor — ✅ KISMEN DÜZELTİLDİ (2026-08-14, K-401 — hata mesajı netleşti, çalıştırma hâlâ `RunFailed`; "zarif durdurma" F-106'ya devredildi)

- **Case:** MT-WF-071 (Yüksek), MT-WF-073 (Orta) — ikisi de aynı kök nedenle
- **Önem:** Yüksek
- **İzlek:** B
- **Ortam:** macOS arm64 · net10 · PostgreSQL · OpenAI `gpt-5.4-mini`, `Magentic` deseni, `maxIterations: 2`, `requirePlanApproval: true`

**Beklenen**
Plan onaylandıktan (MT-WF-071) veya düzeltme metniyle reddedildikten (MT-WF-073) sonra yönetici (Magentic manager) akışı temiz biçimde ilerletir — ya katılımcıyı çalıştırıp anlamlı bir `WorkflowOutput` üretir ya da yeniden planlayıp yeni bir `RunAwaitingInput` açar.

**Gerçekleşen**
- **Onayla (MT-WF-071):** `cevirmen` çalıştı ama `WorkflowOutput.text` gerçek çeviri değil, MAF'ın "`Task execution stopped due to hitting the maximum round count limit.`" sistem mesajıydı; ardından `cevirmen` bir kez daha çağrıldı, `ExecutorFailed` (×2) ve `RunFailed: "Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken"` ile çöktü.
- **Düzeltmeyle reddet (MT-WF-073):** Yönetici yeniden PLANLAMADI — doğrudan yürütmeye geçip gerçek bir çeviri ürettikten SONRA `ExecutorFailed` (`TargetInvocationException`) ve `RunFailed: "Error invoking handler for Microsoft.Agents.AI.Workflows.ExternalResponse"` ile çöktü.

**Yeniden üretme**
1. `PUT /api/workflows/plan-onayli` — `kind: Magentic`, `agentNames: ["cevirmen"]`, `managerAgentName: "ozetleyici"`, `maxIterations: 2`, `requirePlanApproval: true` (case'in kendi reprodüksiyon adımı).
2. Çalıştır → `RunAwaitingInput` (`PlanReview`).
3. `respond` ile onayla (`approved:true`) VEYA düzeltme metniyle reddet (`approved:false, text:"..."`).
4. Akış `ExecutorFailed`/`RunFailed` ile biter, temiz bir `Completed` veya ikinci bir `AwaitingInput` ASLA oluşmaz.

**Kanıt**
- İki bağımsız çalıştırmada (onayla + reddet) tutarlı biçimde tekrarlandı.
- `maxIterations: 2` case'in KENDİ reprodüksiyon script'inde belirtilen değer — test kurulumu hatası değil.
- Tam olay dizisi ve hata metinleri MT-WF-071/073'ün `Gerçek sonuç` alanlarında kayıtlı.

**Kapsam**
Faz 16'nın Magentic plan onayı özelliği, `maxIterations` sınırının plan+onay+yürütme döngüsü için yetersiz kaldığı durumlarda zarif bir "sınıra ulaşıldı" mesajı yerine bir iç hata zincirine (`ExecutorFailed`/`RunFailed`, `TargetInvocationException`) düşüyor. Bunun (a) yalnızca `maxIterations` ayarlama sorumluluğu tüketiciye ait bir sınır durumu mu, yoksa (b) MAF'ın/AgentPrism'in round-limit'e ulaşıldığında akışı sonlandırma mantığındaki bir kod kusuru mu olduğu ayrı bir kod incelemesi gerektirir — kod bu koşumda değiştirilmedi.

**Düzeltme (2026-08-14, K-401) — kısmi**
Kod incelemesi tamamlandı: `WorkflowRunner.cs`'de `TargetInvocationException`/`InnerException` soyan HİÇBİR kod yoktu — `ToRunError(Exception)` sarmalayıcının kendi anlamsız `.Message`'ını yazıyordu. Bu (b)'nin "opak hata" yarısıydı ve AgentPrism'in KENDİ kodundaki bir eksiklikti; düzeltildi (`ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException`'ı soyup gerçek nedeni yazıyor). `MT-WF-071` birebir tekrarlanıp doğrulandı: `RunFailed.Text` artık `"This Magentic orchestration has already terminated. To process new messages, create a new workflow instance."` — eskiden opak `"Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken"`. Çalıştırmanın KENDİSİ hâlâ `RunFailed` ile bitiyor (bu doğru — plan gerçekten tamamlanmadı, gizlemek yanlış olurdu). MAF'ın round-limit-sonrası fazla çağrısını ÖNCEDEN kestirip akışı zarif bir `Completed`'e çevirmek ((a)/(b)'nin geri kalanı, raporun "zarif durdurma" beklentisi) MAF'ın kapalı-kutu orkestrasyon durumuna bağımlı, daha kapsamlı bir tasarım kararı gerektiriyor — `F-106` olarak `docs/UCUNCU-FAZ-ADAYLARI.md`'ye yazıldı, kodlanmadı. Dört doğrulama kapısı temiz.

---

Diğer bulgular: MT-WF-020/053/072'de olduğu gibi, workflow içi hatalar (`AgentPrismException` akışın İÇİNDE oluşursa) `event: error` DEĞİL, normal `event: event` içinde `type: RunFailed` olarak geliyor; yalnız akış BAŞLAMADAN (senkron ön-kontrol, ör. MT-WF-064/065) fırlatılan istisnalar gerçek `event: error` çerçevesi üretiyor. Bu, dört case'de (020, 053, 072'de "kod kusuru değil" + 064/065'te "beklendiği gibi") tutarlı biçimde doğrulandı — genel bir kural olarak not edilir, ayrı ayrı `HATA` açılmadı.

MT-WF-042'nin "`$type` ilk 40 baytta başlar" iddiası da düzeltildi — K-027'nin asıl iddiası (sütun tipi `json`, `jsonb` değil) doğru, yalnız `$type` üst nesnede değil, iç içe bir polimorfik dizide (`edges`) görünüyor.

**Sapma:** MT-WF-066'da ilk deneme yanlış sonuç verdi (kiracı yalıtımı "kırılmış" gibi göründü) çünkü `AgentPrism:Tenancy:Enabled` kapalıydı — bu benim test kurulum hatamdı, §5'in ön koşulunu (13-KIRACI-VE-GUVENLIK.md MT-SEC-021'in header çözümlemesi) atlamıştım. Doğru config ile tekrarlanıp gerçek sonuç doğrulandı (bkz. case notu).

---

## K-5 — 15 §7–9 (MT-WF-080..084, 090..097, 100) + 17 §1–2, 27 case

### 15 §7–9 (14 case): 8 Geçti, 6 Kaldı — **dosya `15` (WORKFLOWS) TAMAMEN BİTTİ (60/60)** — kapanışta MT-WF-091/092/093 (K-402), MT-WF-095/096 (K-403) ve MT-WF-100 (K-405) düzeltilip yeniden koşuldu, güncel: 14 Geçti, 0 Kaldı

### HATA-K-004 — 🚨 KRİTİK: `AgentPrismWorkflowOptions` hiçbir konfigürasyon kaynağına bağlı değil — ✅ DÜZELTİLDİ (2026-08-14, K-402)

- **Case:** MT-WF-091 (Orta), MT-WF-092 (Orta), MT-WF-093 (Orta) — üçü de aynı kök neden
- **Önem:** Kritik
- **İzlek:** B
- **Ortam:** macOS arm64 · net10 · PostgreSQL

**Beklenen**
`AgentPrism:Workflows:Enabled`/`MaxSuperSteps`/`EnableCheckpointing` gibi `dotnet user-secrets`/`appsettings.json` ile verilen değerler, uygulama yeniden başlatıldığında `AgentPrismWorkflowOptions`'a yansır.

**Gerçekleşen**
Üç ayrı alan (`Enabled=false`, `MaxSuperSteps=2`, `EnableCheckpointing=false`) ayrı ayrı denendi, üçü de HİÇBİR ETKİ göstermedi — çalıştırmalar sanki ayar hiç verilmemiş gibi normal şekilde tamamlandı (gerçek model çağrıları dahil, gerçek ücret oluşarak).

**Kanıt**
`src/AgentPrism.Workflows/AgentPrismWorkflowsBuilderExtensions.cs:52` — `UseWorkflows()` yalnızca `services.AddOptions<AgentPrismWorkflowOptions>();` çağırıyor. `AgentPrismWorkflowOptions.SectionName` sabiti (`"AgentPrism:Workflows"`) tanımlı ama `grep -rn "AgentPrismWorkflowOptions.SectionName"` sıfır sonuç veriyor — hiçbir yerde `IConfiguration`'a bağlanmıyor. Diğer tüm `Use*()` uzantıları (`UseOpenAI`, `UsePostgreSql`, `UseSkillScripts` vb.) config bölümünü açıkça `Bind()` ederken, `UseWorkflows()` yalnızca kod-içi `configure` lambda parametresini destekliyor.

**Kapsam**
`AgentPrismWorkflowOptions`'ın YEDİ alanının TAMAMI (`Enabled`, `EnableCheckpointing`, `MaxConcurrentRuns`, `RunTimeout`, `MaxSuperSteps`, `KeepCheckpointsAfterCompletion`) etkilenir. Sonsuz döngü koruması (`MaxSuperSteps`), motor kapatma anahtarı (`Enabled`) ve checkpoint kontrolü (`EnableCheckpointing`) gibi üretim-kritik güvenlik sınırlarının HİÇBİRİ konfigürasyonla ayarlanamaz — yalnızca `Program.cs`'te `UseWorkflows(o => ...)` ile kodda sabitlenebilir.

**Düzeltme (2026-08-14, K-402)**
`UseWorkflows()` artık `OptionsBuilder<AgentPrismWorkflowOptions>.BindConfiguration("AgentPrism:Workflows")` çağırıyor (`Microsoft.Extensions.Options.ConfigurationExtensions`, yeni bağımlılık — `AgentPrism.Workflows` zaten `AgentPrismAotCompatible=false` olduğu için reflection tabanlı bağlama burada kabul edilebilir). `configure` lambda'sı bağlamadan SONRA çalışır, kod hâlâ üzerine yazabilir. Üç alan ayrı ayrı yeniden koşulup doğrulandı: `Enabled=false` → çalıştırma doğru `AgentPrismException` ile reddedildi; `MaxSuperSteps=2` → 3 süper-step üreten gerçek bir workflow doğru mesajla `RunFailed`/`status:Failed` oldu; `EnableCheckpointing=false` → hiç checkpoint yazılmadı (`count=0`), `resume` denemesi doğru "kontrol noktası yok" hatasını (checkpointing'in kapalı olduğunu da açıklayan bir varyantla) verdi. MT-WF-091/092/093 üçü de yeniden koşulup `Geçti`'ye çevrildi. Dört doğrulama kapısı temiz.

---

### HATA-K-005 — 🚨 KRİTİK: Workflow `run` ucunda `sessionId` doğrulama hatası SSE akışını hiç başlatmadan düz `HTTP 500`'e düşüyor — ✅ DÜZELTİLDİ (2026-08-14, K-403)

- **Case:** MT-WF-095 (Düşük), MT-WF-096 (Düşük) — aynı kök neden
- **Önem:** Yüksek (etkiye göre yükseltildi — istemci hiçbir teşhis bilgisi almıyor)
- **İzlek:** B

**Beklenen**
Geçersiz `sessionId` (128 karakter sınırı aşımı veya izin verilmeyen karakter) gönderildiğinde SSE akışı `event: run` ile başlar, ardından açıklayıcı bir `event: error` çerçevesi gelir.

**Gerçekleşen**
Hiçbir SSE çerçevesi gelmiyor — istemci düz, generic bir `HTTP 500` (`{"title":"An error occurred while processing your request."}`, detay YOK) alıyor. Sunucu logunda doğru hata mesajı görülüyor ama istemciye hiç ulaşmıyor.

**Kanıt**
`src/AgentPrism.Workflows/Internal/WorkflowRunner.cs:186` — `RunStreamingAsync` bir `async IAsyncEnumerable` yineleyicisi DEĞİL, düz bir metottur; `WorkflowSessionId.Require(request.SessionId)` çağrısı nesne başlatıcısının içinde SENKRON çalışır ve metot gövdesi `ExecuteAsync(...)`'in ürettiği `IAsyncEnumerable`'ı yalnızca DÖNDÜRÜR. İstisna bu yüzden `WorkflowEventStream`'in (SSE yazıcısı, `event: error` üreten `catch` bloğunu taşıyan sınıf) hiç devreye girmesine fırsat kalmadan doğrudan `WorkflowEndpoints.RunAsync`'ten fırlar, ASP.NET'in genel `ExceptionHandlerMiddleware`'ine düşer. Sunucu logu: `fail: Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware[1] An unhandled exception has occurred while executing the request.` Karşılaştırma: `RespondStreamingAsync`/`ResumeStreamingAsync` gerçek `async IAsyncEnumerable` yineleyicileridir (MT-WF-064/065/094'te doğru `event: error` üretirler) — yalnız `RunStreamingAsync`'in bu yapısal farkı bu boşluğu yaratır.

**Kapsam**
`POST /api/workflows/{name}/run` ucuna gönderilen HERHANGİ bir geçersiz `sessionId`, istemciye hiçbir teşhis bilgisi vermeyen bir 500 üretir — hata ayıklaması yalnızca sunucu logu erişimi olan biri için mümkündür.

**Düzeltme (2026-08-14, K-403)**
`RunStreamingAsync` gerçek bir `async IAsyncEnumerable<RunEvent>` yineleyicisi yapıldı (`[EnumeratorCancellation]` ile); `WorkflowExecution` kaydı ve `WorkflowSessionId.Require` çağrısı artık yineleyici gövdesinin içinde, `await foreach` başlamadan hemen önce çalışıyor — ilk `MoveNextAsync()` `WorkflowEventStream`'in kendi `try/catch`'i içinde gerçekleşiyor. `AsyncLocal`/`Activity.Current` async yardımcı metotta açılamaması kuralıyla (Faz 6/11/12/15) aynı sınıfın beşinci tekrarı. MT-WF-095/096 birebir tekrarlanıp doğrulandı: her iki senaryo da artık `event: run` ardından `event: error` (doğru mesaj), `HTTP 200`. Dört doğrulama kapısı temiz (test suitindeki tek geçici başarısızlık — voice/mikrofon Playwright testi — ilgisiz, yeniden koşulunca geçti).

---

### HATA-K-006 — 🚨 Yüksek: `RunsRead`-kapsamlı bir API anahtarı workflow yazabiliyor VE çalıştırabiliyor (doğrulanmış güvenlik açığı) — ✅ DÜZELTİLDİ (2026-08-14, K-405)

- **Case:** MT-WF-100
- **Önem:** Yüksek
- **İzlek:** B

**Beklenen**
`WorkflowEndpoints`'in yazma/çalıştırma uçları da `AgentEndpoints`/`RunEndpoints` gibi `RequireApiKeyScope(...)` uygular; yalnız `RunsRead` kapsamlı bir anahtar `PUT`/`run`'a `403` alır.

**Gerçekleşen**
Yalnız `RunsRead` kapsamlı bir anahtarla: `PUT /api/workflows/{name}` → `200` (tanım yazıldı), `POST /api/workflows/{name}/run` → `200` (gerçek bir çalıştırma başladı, gerçek model çağrısı, gerçek ücret). Kontrol grubu: aynı anahtarla `PUT /api/agents/{name}` → `403 "Kapsam yetersiz"` — kapsam sistemi `AgentEndpoints`'te çalışıyor, `WorkflowEndpoints`'te TAMAMEN devre dışı.

**Kanıt**
`WorkflowEndpoints.Map`'in hiçbir ucu `.RequireApiKeyScope(...)` çağırmıyor (doküman zaten bunu kod okumasıyla şüphe olarak işaretlemişti — bu koşum ampirik olarak doğruladı).

**Kapsam**
Yalnız-okuma niyetiyle üretilmiş bir otomasyon anahtarı, workflow tanımlarını yazabilir/silebilir ve gerçek para harcayan bir Magentic çalıştırmasını başlatabilir — API anahtarı kapsam modelinin ciddi bir ihlali.

**Düzeltme (2026-08-14, K-405)**
`ApiKeyScope`'a iki yeni üye eklendi: `WorkflowsRead`, `WorkflowsAdmin` (`AgentsRead`/`AgentsAdmin` deseniyle birebir). `WorkflowEndpoints`'in TÜM uçlarına `RequireApiKeyScope` eklendi — tanım yönetimi (`GET`/`PUT`/`DELETE` `/api/workflows*`) yeni `WorkflowsRead`/`WorkflowsAdmin` kapsamlarını, çalıştırma/run-durumu uçları (`run`/`resume`/`respond`, checkpoint/istek listeleme) `AgentEndpoints`'in `POST /api/agents/{name}/run`'un `AgentsAdmin` değil `RunsWrite` istediği presedansını izleyerek var olan `RunsRead`/`RunsWrite`'ı aldı. Ampirik doğrulama (raporun senaryosu birebir tekrarlandı, gerçek sunucuya karşı): yalnız `RunsRead` taşıyan anahtarla `GET /api/workflows` → `403 "WorkflowsRead kapsamini gerektiriyor"`; `PUT /api/workflows/{name}` → `403 "WorkflowsAdmin kapsamini gerektiriyor"`; `POST /api/workflows/{name}/run` → `403 "RunsWrite kapsamini gerektiriyor"`. Regresyon: dört kapsamın TAMAMINI taşıyan bir anahtarla aynı üç uç `200`. `docs/openapi/agentprism.json` tazelendi (`ApiKeyScope` enum listesine iki değer eklendi). Frontend `api-key-panel.tsx`'teki sabit `SCOPES` dizisi henüz bu iki değeri (ve önceden eklenmiş `KnowledgeRead`/`KnowledgeAdmin`'i) içermiyor — ayrı, bu düzeltmenin kapsamı dışında bir boşluk. Dört doğrulama kapısı temiz.

---

Diğer bulgular: MT-WF-097'de ilk deneme MT-WF-066 ile aynı sebepten (Tenancy kapalı) yanlış sonuç verdi, doğru config ile düzeltilip doğrulandı.

### 17 §1–2 (MT-EVAL-001..007, 010..015), 13 case: 13 Geçti, 0 Kaldı

Kusur bulunmadı. Küçük doküman notları: MT-EVAL-012'nin `DELETE /api/evals/{name}/cases` ucu doküman `HTTP 200` varsaymışken gerçekte `204` dönüyor (repodaki `DELETE` uçlarının tutarlı deseni — kusur değil, düzeltildi); MT-EVAL-004'ün hata mesajı `{kind}` yer tutucusu kullanıyor, doküman doğrudan `regexMatch` yazmıştı (anlam aynı).

**K-5 toplam: 27 case, 21 Geçti, 6 Kaldı** (kapanışta HATA-K-004/K-005/K-006 düzeltmeleriyle MT-WF-091/092/093/095/096/100 yeniden koşulup Geçti'ye çevrildi — güncel: 27 Geçti, 0 Kaldı; ayrıntı yukarıdaki HATA-K-004/HATA-K-005/HATA-K-006 notlarında).

## K-6 — 17 §3–6 (MT-EVAL-020..028, 035..038, 040..046, 050..059), 30 case

**Sonuç:** 30 Geçti, 0 Kaldı. Kusur bulunmadı.

### 17 §3 (MT-EVAL-020..028), 9 case: 9 Geçti, 0 Kaldı

Mutlu yol koşusu, `toolCalled`/`keywords`/`hasImageContent` denetimleri, `numRepetitions` tekrar başarısızlığı, sürüm pinleme (ilk deneme yanlış zamanlamayla yanıltıcıydı — bkz. bu K-6 girdisinin altındaki not — 10 case'lik hızlı-yoklama ile düzeltilip doğrulandı), boş `checks:[]` ile `Failed` run, 0 vakalı takımda senkron `400`, koşu geçmişi listesi. Kusur bulunmadı.

### 17 §4 (MT-EVAL-035..038), 4 case: 4 Geçti, 0 Kaldı

Run→vaka terfisi mutlu yol (`201`, `sourceRunId`/`promotedAt` dolu), aynı run'ı ikinci terfi `200` idempotent (DB'nin kısmi tekil indeksi `eval_cases_source_run_uq` doğrulandı, ikinci satır oluşmadı), var olmayan `runId` `404`, `PromoteToEvalCase` bileşeni 0 takım varken render edilmiyor (tüm takımlar silinip run detay sayfası incelendi, sonra `destek-degerlendirme`/`tool-cagri-testi` fixture'ları geri kuruldu). Kusur bulunmadı.

### 17 §5 (MT-EVAL-040..046), 7 case: 7 Geçti, 0 Kaldı

`SampleRate<=0` hiçbir işi kuyruğa yazmıyor; `Enabled+SampleRate=1.0` her tamamlanan run'ı örnekliyor (`jobs`/`run_scores` doğrulandı, gerçek `gpt-5.4-mini` yargıç çağrısı); `RunSampler.IsSampled`'ın FNV-1a tabanlı, süreç-bağımsız determinizmi kod okuması + Python'da birebir yeniden üretilen hesaplamayla doğrulandı; `/judge` örneklemeyi atlayıp doğrudan puanlıyor (audit kaydı dahil); yargıç yokken `[]`; aynı run'ı iki kez yargılamak `UPSERT` (tek satır); `GET /api/evaluation/online` doğru alanları dönüyor. Kusur bulunmadı.

**⚠️ Ajan hatası (kusur değil, koşum kazası).** MT-EVAL-044 hazırlığı sırasında OpenAI `user-secrets` anahtarı bir betik hatasıyla (`set -e` başarısız komut ikamesini yakalamadı) boş dizeyle üzerine yazıldı; kullanıcıdan yeni anahtar istenip geri yüklendi. Ayrıntı `## Sapmalar` bölümünde.

### 17 §6 (MT-EVAL-050..059), 10 case: 10 Geçti, 0 Kaldı

Kod-kökenli agent'ta deney kurma `400`; DB-kökenli agent ile iki varyantlı deney (`manuel-destek` fixture'ı önceki koşumlardan `version=3` taşıyordu, `version=4` üretilip uyarlandı — ayrıntı case notunda); ağırlık toplamı ≠100 `400`; olmayan sürüm `400`; aynı agent için ikinci `Running` deney `409` (DB kısmi tekil indeksi `experiments_running_agent_uq` doğrulandı); `Running` deneyi silme/düzenleme `409`; `STOP` tek yönlü `Stopped`; arayüzde ağırlık 30+30 iken toplam kırmızı (`rgb(190,18,60)`) ve "Save" devre dışı; `stopped` satırında Edit/Sil yok, `draft` satırında var (kontrast doğrulandı). Kusur bulunmadı.

**K-6 toplam: 30 case, 30 Geçti, 0 Kaldı.**

## K-7 — 17 §7–11 (MT-EVAL-062..063, 070..076, 080..090, 091..094, 100..101), 26 case

**Sonuç:** 20 Geçti, 4 Kaldı (`HATA-K-007`, `HATA-K-008`), 2 doküman-uyarlaması (ön koşul sürüm numaraları/ortam farkı, kusur değil). Kapanışta `HATA-K-007`/`HATA-K-008` düzeltilip MT-EVAL-092/100/101 yeniden koşuldu — güncel: 24 Geçti, 0 Kaldı.

### 17 §7 — Atama Belirlenirliği (MT-EVAL-062..063), 2 case: 2 Geçti, 0 Kaldı

Aynı `sessionId` ile 5 art arda çalıştırma HER ZAMAN aynı varyanta düştü (SHA-256/FNV tabanlı kova ataması, önbellek değil); `runs.experiment_id`/`variant`/`agent_version` sütunları tutarlı. Kusur bulunmadı.

### 17 §8 — Kanarya Yayını (MT-EVAL-070..076), 7 case: 7 Geçti, 0 Kaldı

Kanarya kuralı yalnız 2 kollu deneyde tanımlanabiliyor (`400` üç kollu deneyde); geçerli politika `PUT` ediliyor; `GET .../canary` yeterli örnek toplanana kadar `InsufficientData` döndü ve HER çağrıda canlı hesaplandığı (`evaluatedAt` değişti) doğrulandı; `AutoRollbackEnabled` varsayılan kapalı. **MT-EVAL-074 (kritik, en önemli senaryo):** mevcut sürümler `Running` bir deneyde değişmez olduğu için (kod okumasıyla doğrulandı: `CompositeAgentCatalog.ResolveAsync` saklanan `AgentDefinition`'ı doğrudan kullanır) bozuk-modelli yeni bir sürüm (v5) ve taze bir deney (`kanarya-geri-alma-testi`) kuruldu; otomatik geri alma gerçek şekilde tetiklendi — `rollback_reason` doldu, `audit_log`'da `experiment.auto_rollback`/`actor:system:canary-evaluator` kaydı, ağırlıklar kanarya `0`/kontrol `100`'e döndü (deney ayrıca `Stopped`'a geçti — dokümanın belirtmediği ek gözlem). MT-EVAL-075: sağlıklı kanarya ağırlığı otomatik yükseldi (`50→100`, kurulum kova dağılımı nedeniyle ara `25` basamağı atlandı — kusur değil), ramp-up için audit kaydı YOK (doğrulandı). MT-EVAL-076: otomatik geri alınan deneyde kırmızı "Automatically rolled back" banner'ı, manuel `Stop`'ta yok (kontrast doğrulandı). Kusur bulunmadı.

### 17 §9 — Geri Bildirim ve Puanlama (MT-EVAL-080..090), 11 case: 10 Geçti, 1 Kaldı

Binary/Stars puan geçerlilik denetimi (`400` sınır değerlerde); `DELETE` + audit kaydı; programatik yargıç yazısı (`RunSampler`→`OnlineEvalJobHandler`) audit izi bırakmıyor (kontrast: HTTP `/judge` ucu kendi `run.judge.manual` kaydını bırakıyor — doğrulandı); arayüz `FeedbackControl` toggle (1. tıklama kaydeder, 2. SİLER — aşağıya çevirmez, 3. yeniden oluşturur); yorum puansız kaydedilmiyor + yıldız UI hiç yok; "Judge now" yargıç yokken hata banner'ı değil nötr satır-içi mesaj (`rgb(107,107,121)`, `role="alert"` yok).

**HATA yok ama önemli doküman düzeltmesi — MT-EVAL-084 KALDI:** case'in kendi ön koşulu ("bu ortamda `author` `NULL` değildir") YANLIŞ — statik bearer token akışında `author` HER ZAMAN `NULL` kalıyor. `run_scores_target_author_idx` `(tenant_id, run_id, COALESCE(message_id,''), author)` üzerine kurulu, `author`'ın kendisi `COALESCE` edilmiyor → PostgreSQL'de `NULL≠NULL`, tekillik hiç devreye girmiyor. Aynı yazarın ikinci puanı ÜZERİNE YAZMIYOR, ayrı satır ekleniyor — MT-EVAL-085'in zaten "kasıtlı kabul edilmiş davranış" olarak belgelediğinin AYNISI, ama MT-EVAL-084'ün varsaydığı upsert bu ortamda hiç gerçekleşmiyor. Doküman düzeltmesi, kod kusuru değil.

### 17 §10 — Karşılaştırma/Yeniden Oynatma/Girdi (MT-EVAL-091..094), 4 case: 3 Geçti, 1 Kaldı — kapanışta HATA-K-007/K-406 düzeltilip MT-EVAL-092 yeniden koşuldu, güncel: 4 Geçti, 0 Kaldı

`GET .../compare/{a}/{b}` iki run'ı ham döndürüyor, tüm alanlar mevcut. `POST .../replay`: `support` kod-kökenli olduğu için yalnız `LiveTools` destekliyor (dokümante edilmemiş ama tutarlı ek kısıt); `agentVersion` belirtilmezse replay GÜNCEL sürümü kullanıyor (kod okumasıyla doğrulandı, `RunReplayRequest.AgentVersion` XML dokümanında zaten yazılı — kusur değil, ama bu koşumda ilk denemede `502` üretti çünkü ortamdaki "güncel sürüm" MT-EVAL-074'ün bozuk v5'iydi); `agentVersion` sabitlenerek hem `ReplayTools` hem `LiveTools` doğru çalıştığı doğrulandı.

**HATA-K-007 (Yüksek) — MT-EVAL-092 KALDI:** `AgentPrism:RunRecording:RecordRunInput=false` HİÇBİR ETKİ yapmıyor; `GET /api/runs/{id}/input` beklenen `404` yerine `200` ve tam girdiyi döndürdü. Ayrıntı aşağıda.

### 17 §11 — Güvenlik: Eval/Deney API Anahtarı Kapsamı (MT-EVAL-100..101), 2 case: 0 Geçti, 2 Kaldı — kapanışta HATA-K-008/K-407 düzeltilip ikisi de yeniden koşuldu, güncel: 2 Geçti, 0 Kaldı

**HATA-K-008 (Yüksek) — İKİSİ de KALDI:** doküman her iki case'in de "şüphesini" AMPİRİK olarak doğruladı. Ayrıntı aşağıda.

---

### HATA-K-007 — Yüksek: `AgentPrism:RunRecording:RecordRunInput` config'ten HİÇBİR ZAMAN okunmuyor — çalıştırma girdisi kapatılamıyor — ✅ DÜZELTİLDİ (2026-08-14, K-406)

- **Case:** MT-EVAL-092
- **Önem:** Yüksek
- **İzlek:** B

**Beklenen**
`AgentPrism:RunRecording:RecordRunInput=false` set edilip uygulama yeniden başlatıldığında, yeni run'ların girdisi kaydedilmez; `GET /api/runs/{id}/input` `404` döner.

**Gerçekleşen**
`RecordRunInput=false` `user-secrets`'a yazılıp uygulama yeniden başlatıldı, yeni bir run gönderildi, `GET .../input` → `HTTP 200`, tam girdi (`messages: [...]`) döndü. Bayrak hiçbir etki yapmadı.

**Kanıt**
`AgentPrismServiceCollectionExtensions.cs:1769-1795`'teki `BindRunRecording` metodu `Enabled`, `RecordMessageDeltas`, `RecordToolPayloads`, `MaxPayloadLength`'i config'ten okuyor (`TryReadBool`/`int.TryParse` çağrılarıyla) AMA `RecordRunInput`'u (`AgentPrismOptions.cs:461`, varsayılan `true`) HİÇ okumuyor — ilgili `TryReadBool` çağrısı eksik. `RunRecordingAgent`'ın kendisi `!_options.RecordRunInput` kontrolünü DOĞRU yapıyor (~satır 593); `GET /input` ucu da depoda girdi VARSA `200`/YOKSA `404` mantığını DOĞRU uyguluyor (`RunEndpoints.cs:254-286`) — sorun yalnız bağlama (binding) katmanında, bayrak config/`user-secrets`/ortam değişkeninden asla `false` olamıyor.

**Kapsam**
Kullanıcı girdisi hassas veri (PII/gizli bilgi) içerebilir; bu bayrak tam da bunu kapatmak için var. Operatör kapattığını sanırken girdi hâlâ kaydediliyor — sessiz bir gizlilik kontrolü kaçağı.

**Düzeltme (2026-08-14, K-406)**
`BindRunRecording`'e eksik `TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordRunInput), ...)` çağrısı eklendi — diğer beş alanla (`Enabled`, `RecordMessageDeltas`, `RecordToolPayloads`, `MaxPayloadLength`) birebir aynı desen. Ampirik doğrulama (gerçek sunucuya karşı, MT-EVAL-092'nin birebir tekrarı): `RecordRunInput=false` iken yeni bir çalıştırmanın `GET /input`'u artık `HTTP: 404`, `"Girdi kaydi yok"`. Regresyon: ayar kaldırılıp (varsayılan `true`) yeniden başlatılınca aynı uç `HTTP: 200` + tam girdi. Aynı kök neden bu Ortak Kuyruk koşumundan ÖNCE de iki AYRI serit sonucunda bağımsız olarak bulunmuştu (`HATA-S2-002`/`MT-API-064`, `HATA-S4-015`/`MT-UIRUN-032`) — her iki serit sonuç dosyasına da bu karara işaret eden kapanış notu eklendi. Dört doğrulama kapısı temiz.

---

### HATA-K-008 — Yüksek: `ApiKeyScope`'ta Eval/Experiment için kapsam YOK — salt-okunur anahtar eval takımı/deney yazabiliyor, feedback yazabiliyor — ✅ DÜZELTİLDİ (2026-08-14, K-407)

- **Case:** MT-EVAL-100, MT-EVAL-101
- **Önem:** Yüksek
- **İzlek:** B

**Beklenen**
`EvalEndpoints`/`ExperimentEndpoints`'in yazma uçları, `AgentEndpoints` gibi `RequireApiKeyScope(...)` uygular; yalnız `RunsRead` kapsamlı bir anahtar `403` alır. `RunEndpoints.cs` içindeki `feedback` ucu da `replay` gibi tutarlı bir kapsam gerektirir.

**Gerçekleşen**
Yalnız `RunsRead` kapsamlı bir anahtarla: `PUT /api/evals/{name}` → `200` (takım oluşturuldu), `PUT /api/experiments/{name}` → `200` (deney oluşturuldu), `POST /api/runs/{id}/feedback` → `200` (puan yazıldı). Kontrol grupları: aynı anahtarla `PUT /api/agents/{name}` → `403 "AgentsAdmin kapsamini gerektiriyor"`, `POST /api/runs/{id}/replay` → `403 "RunsWrite kapsamini gerektiriyor"` — kapsam sistemi `AgentEndpoints`/`replay`'de çalışıyor, eval/experiment/`feedback` yüzeyinde TAMAMEN devre dışı.

**Kanıt**
`ApiKeyScope` enum'ı yalnız `RunsRead=0, RunsWrite=1, AgentsRead=2, AgentsAdmin=3, ExternalInvoke=4` beş üyeden ibaret — `Eval`/`Experiment` için hiç bir kapsam değeri TANIMLANMAMIŞ. `RunEndpoints.cs` içinde `feedback`/`compare`/`input` uçları `replay`'in aksine hiç `RequireApiKeyScope` çağırmıyor.

**Kapsam**
`MT-JOB-090` (`16-IS-KUYRUGU-VE-ZAMANLAMA.md`) ve `MT-WF-100` (`15-WORKFLOWS.md`) ile AYNI kök nedenin DÖRDÜNCÜ bağımsız tekrarı. Salt-okunur niyetiyle üretilmiş bir anahtar eval takımı/deney oluşturup gerçek para harcayan çalıştırmaları dolaylı tetikleyebilir, ayrıca herhangi bir run'a keyfi geri bildirim yazabilir.

**Düzeltme (2026-08-14, K-407)**
`ApiKeyScope`'a dört yeni üye eklendi: `EvalsRead`, `EvalsAdmin`, `ExperimentsRead`, `ExperimentsAdmin` (Eval/Experiment ayrı kaynak türleri olduğu için `WorkflowsRead`/`WorkflowsAdmin` deseniyle birebir, ayrı çiftler). `EvalEndpoints`/`ExperimentEndpoints`'in TÜM uçlarına `RequireApiKeyScope` eklendi — tanım/veri yönetimi yeni kaynak-özel kapsamları, gerçek model çağırıp para harcayan uçlar (`POST /api/evals/{name}/run`, `POST /api/runs/{runId}/judge`) var olan `RunsWrite`'ı aldı (`AgentEndpoints`'in kendi `run` ucunun `AgentsAdmin` değil `RunsWrite` istemesiyle aynı mantık). `RunEndpoints`'in `feedback`(yaz)/`input`/`compare`(oku) uçlarına da eksik `RequireApiKeyScope(RunsWrite|RunsRead)` çağrıları eklendi. Ampirik doğrulama (raporun senaryosu birebir tekrarlandı, gerçek sunucuya karşı): yalnız `RunsRead` taşıyan anahtarla `PUT /api/evals/{name}` → `403 "EvalsAdmin kapsamini gerektiriyor"`; `PUT /api/experiments/{name}` → `403 "ExperimentsAdmin kapsamini gerektiriyor"`; `GET /api/evals` → `403 "EvalsRead kapsamini gerektiriyor"`; `POST /api/runs/{id}/feedback` → `403 "RunsWrite kapsamini gerektiriyor"` (kontrast: aynı anahtarla `GET /api/runs/{id}/input` hâlâ `200`, çünkü bu uç yalnız `RunsRead` istiyor). Regresyon: ilgili kapsamları taşıyan bir anahtarla eval takımı oluşturma ve feedback yazma `200`. `docs/openapi/agentprism.json` tazelendi (dört yeni enum değeri). `SchedulingEndpoints` (`MT-JOB-090`) ve `GovernanceEndpoints` bu düzeltmenin kapsamı DIŞINDA bırakıldı — bu koşumun konfirme ettiği HATA-K-NNN listesine dahil değillerdi. Dört doğrulama kapısı temiz.

---

**K-7 toplam: 26 case, 20 Geçti, 4 Kaldı.**

## K-8 — 24 §1–2 (MT-TEST-001..013, 020..030), 24 case

**Sonuç:** 24 Geçti, 0 Kaldı, 2 doküman düzeltmesi. Kusur bulunmadı. 🔒 Küresel kilit altında koşuldu (`~/agentprism-local-feed`/`dotnet new install`) — tek ajan.

Yerel NuGet feed'i taze bir `dotnet pack` (`MSBUILDDISABLENODEREUSE=1`) ile tazelendi; feed'in önceki içeriği eski sürümlere (`0.60`–`0.78`) kadardı, koşum sürümü `0.0.0-preview.0.107`.

### 24 §1 — Şablon: `dotnet new agentprism-api` (MT-TEST-001..013), 13 case: 13 Geçti, 0 Kaldı

Şablon paketten kurulup listeleniyor; en yalın (`memory`+`openai`+`ui:false`) ve en dolu (`sqlserver`+`azure`+`ui:true`) birleşimler sıfır uyarıyla derleniyor, en dolu birleşim doğru paket referanslarını taşıyor; üretilen `appsettings.json`'da `secret` yok, tüm alanlar boş; dört sağlayıcının (`openai`/`anthropic`/`google`/`azure`) hiçbiri sabit model adı taşımıyor; `-n` ile yeniden adlandırma hiçbir `AgentPrism.Starter` kalıntısı bırakmıyor; varsayılan (`memory`) birleşim `secret`'siz `dotnet run` ile ayağa kalkıyor; `--skip-restore` `obj/`'yi tamamen atlıyor; `--AgentPrismVersion` tam sürümü sabitliyor; geçersiz `--persistence` `127` ile reddediliyor; `-h` çıktısında dört bayrak görünüyor, `AgentPrismVersion` gizli; şablon paketinde `.dll` yok, üretilen proje `AgentPrism.Templates`'e hiç referans vermiyor.

**Doküman düzeltmesi — MT-TEST-008:** "İki `.csproj` birebir aynıdır (`diff` boş döner)" iddiası case'in KENDİ girilecek-veri adımlarıyla çelişiyor — iki proje farklı adlarla (`Meta.Kontrol`/`Meta.Kontrol2`) üretiliyor, bu da `RootNamespace`/`UserSecretsId`'yi kaçınılmaz olarak değiştiriyor. Asıl doğrulanmak istenen iddia (postgres seçmek ek `PackageReference` eklemiyor) doğru — her iki `.csproj` da tek satır `<PackageReference Include="AgentPrism" .../>` taşıyor. Kod kusuru değil.

### 24 §2 — `FakeModelProvider` (MT-TEST-020..030), 11 case: 11 Geçti, 0 Kaldı

Varsayılan kurulum sabit `"fake response"` dönüyor; `EchoesUserMessage()` son mesajı `Echo: ` önekiyle yankılıyor; `RespondsWith(...)` yanıtları sırayla tüketiyor; kuyruk+`EchoesUserMessage()` fallback karışımında önce kuyruktan sonra güncel mesajın yankısından dönüyor; fallback tanımlanmamışsa kuyruk sonrası sabit `"fake response"` tekrarlanıyor; `CallsTool(...)` `FunctionCallContent` üretiyor, anonim tip argümanları doğru kopyalanıyor; `ForModel(...)` modeller arası bağımsız kuyruk tutuyor; `EchoesLastToolResult` `ModelProviderRegistry` üzerinden gerçek tool-çağrı döngüsünde çalışıyor, HAM istemcide (defter olmadan) tool hiç çalıştırılmıyor (kontrast doğrulandı); `RespondsWith(text,inputTokens,outputTokens)` gerçek boru hattında `RunRecord.Usage`'a birebir yansıyor; `Requests` listesi her isteğin kendi `ChatOptions`'ını ayrı saklıyor; katalogda olmayan model adı agent kaydını engellemiyor (K-032 kasıtlı tasarım).

**Doküman düzeltmesi — MT-TEST-020/022/023/024 (tekrarlanan):** dört case'in de kod örneği yalnız `using AgentPrism.Testing;` yazıyor ama `ModelBinding` tipi `AgentPrism` ad alanındadır — `using AgentPrism;` eksik, verilen kod aynen yapıştırılınca `CS0246` ile derlenmiyor. Ekleyince tüm case'ler beklenen çıktıyı üretti. Kod kusuru değil.

**K-8 toplam: 24 case, 24 Geçti, 0 Kaldı.**

## K-9 — 24 §3–5 (MT-TEST-040..045, 050..055, 060..064), 17 case

**Sonuç:** 16 Geçti, 1 Kaldı (doküman düzeltmesi). Kusur bulunmadı. 🔒 Aynı küresel kilit altında koşuldu.

### 24 §3 — `AgentPrismTestHost` (MT-TEST-040..045), 6 case: 5 Geçti, 1 Kaldı

`StartAsync()` `secret`'siz ayağa kalkıyor, `/meta` `200` dönüyor (`version`/`prefix` alanları mevcut). Özel `Prefix` yalnız kendinden yanıt veriyor, eski önek `404`. `DisposeAsync()` sonrası `Client` kullanımı `ObjectDisposedException` fırlatıyor. Olmayan agent'la `RunAsync` `AgentPrismAssertionException` fırlatıyor, mesaj beklenen/bulunan durumu (`404`) taşıyor. `ConfigureServices` `AddAgentPrism()`'den önce çalışıyor, kayıt `host.Services`'ten erişilebiliyor.

**Doküman düzeltmesi — MT-TEST-044:** Case'in kendi sınama kodu (`args.Services is null`) HER ZAMAN `false` — MAF `Services`'i asla gerçek `null` göndermiyor, daima boş-ama-`null`-olmayan bir `EmptyServiceProvider` gönderiyor (`ToolMethodScanner.cs:22,93` yorumları). Düzeltilmiş sınamayla (`args.Services?.GetService(typeof(...))` ile gerçek bir DI kaydı çözmeye çalışmak) K-218'in ASIL iddiası (kayıt çözülemez) doğrulandı: `GetService(...)` `NULL` döndü. `AgentDefinitionCompiler.cs:945`'teki üretim bağlama kodu K-218 yazıldığından beri hiç değişmedi (git log doğrulandı) — üretim davranışında hiçbir regresyon/düzelme yok, yalnızca doküman örneğinin sınama koşulu yanlıştı.

### 24 §4 — `RunAssertions` (MT-TEST-050..055), 6 case: 6 Geçti, 0 Kaldı

`ShouldHaveCompleted`/`ShouldHaveFailedWith`/`ShouldHaveCalledTool(times:)`/`ShouldNotHaveCalledTool` — dördü de hem geçen hem düşen yolda test edildi, düşen yol mesajları dokümanla birebir eşleşti. `ShouldHaveOutputContaining`'in SSE (`/run`) yolunda `MessageCompleted` hiç yazılmadığını (`0`), `MessageDelta` parçalarının birleştirilerek okunduğunu (`1`) doğruladı — Faz 39'un kendi kaydettiği bir hatanın düzeltme kanıtı. README'nin zincirleme iddia örneği hiçbir aşamada istisna atmadan tamamlandı.

### 24 §5 — Paket kalitesi ve sınırlar (MT-TEST-060..064), 5 case: 5 Geçti, 0 Kaldı

`AgentPrism.Testing.nuspec`'in bağımlılık bloğu yalnız 3 gerçek bağımlılık listeliyor, test çerçevesi yok (paketin kendi `<description>`'ındaki "xunit'e bağlı değildir" açıklaması yanlış eşleşme olarak not edildi, gerçek bağımlılık değil). Meta paket `Testing`'e hiç referans vermiyor. `net8.0` (kurulu SDK'da artık desteklenmiyor, `net9.0` kullanıldı — aynı derecede uyumsuz) projeden paket eklemek `NU1202` ile başarısız oluyor, `PackageReference` hiç eklenmiyor. **MT-TEST-063** (önceden iddia edilmez, koşumda ölçülür): gerçek `PublishAot=true` denemesi (`CallsTool`'un yansıma yolunu tetikleyen kodla) **`0` uyarı** üretti — dokümanın kendi öngördüğü alternatif senaryo ("sıfır uyarı çıkması kod yorumunun güncelliğini yitirdiği anlamına gelebilir"), kusur olarak işaretlenmedi, yalnız ölçüm kaydedildi. Depo dışı taze bir tüketici projesinde README'nin zincirleme örneği hiçbir `secret` olmadan başarıyla çalıştı; `FakeChatClient.cs`'te ağ kullanımı olmadığı kod okumasıyla da doğrulandı.

**K-9 toplam: 17 case, 16 Geçti, 1 Kaldı.**
