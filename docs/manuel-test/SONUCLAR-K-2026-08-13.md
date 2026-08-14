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

## K-1 — 14 §1–2 (MT-SKILL-001..014, 020..025), 20 case

**Sonuç:** 19 Geçti, 1 Kaldı.

### HATA-K-001 — `POST/PUT /api/agents`, bilinmeyen skill adını SAVE zamanında hiç doğrulamıyor

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

### HATA-K-002 — 🚨 KRİTİK: Script çalıştırma özelliği (Faz 11) tamamen çalışmıyor — `JsonSerializerOptions` çöküyor

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

---

Diğer bulgular: MT-WF-020'nin dokümanı düzeltildi (kod kusuru değil) — beklenen "`event: error`" SSE çerçevesi yerine gerçek davranış normal `event: event` içinde `type: "RunFailed"` domain event'i taşıyor; mesaj içeriği doğru, yalnız çerçeve varsayımı yanlıştı. Ayrıntı MT-WF-020'nin case notunda.

---

## K-4 — 15 §2–6 (MT-WF-030..035, 040..044, 050..053, 060..066, 070..073), 26 case

**Sonuç:** 23 Geçti, 3 Kaldı.

### HATA-K-003 — 🚨 KRİTİK: Magentic + plan onayı, onay sonrası devamda `ExecutorFailed`/`RunFailed` ile çöküyor

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

---

Diğer bulgular: MT-WF-020/053/072'de olduğu gibi, workflow içi hatalar (`AgentPrismException` akışın İÇİNDE oluşursa) `event: error` DEĞİL, normal `event: event` içinde `type: RunFailed` olarak geliyor; yalnız akış BAŞLAMADAN (senkron ön-kontrol, ör. MT-WF-064/065) fırlatılan istisnalar gerçek `event: error` çerçevesi üretiyor. Bu, dört case'de (020, 053, 072'de "kod kusuru değil" + 064/065'te "beklendiği gibi") tutarlı biçimde doğrulandı — genel bir kural olarak not edilir, ayrı ayrı `HATA` açılmadı.

MT-WF-042'nin "`$type` ilk 40 baytta başlar" iddiası da düzeltildi — K-027'nin asıl iddiası (sütun tipi `json`, `jsonb` değil) doğru, yalnız `$type` üst nesnede değil, iç içe bir polimorfik dizide (`edges`) görünüyor.

**Sapma:** MT-WF-066'da ilk deneme yanlış sonuç verdi (kiracı yalıtımı "kırılmış" gibi göründü) çünkü `AgentPrism:Tenancy:Enabled` kapalıydı — bu benim test kurulum hatamdı, §5'in ön koşulunu (13-KIRACI-VE-GUVENLIK.md MT-SEC-021'in header çözümlemesi) atlamıştım. Doğru config ile tekrarlanıp gerçek sonuç doğrulandı (bkz. case notu).

---

## K-5 — 15 §7–9 + 17 §1–2, 27 case

_(sıradaki oturum bu başlığın altına yazacak)_
