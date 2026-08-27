# Faz 117 — MCP Tasks Uzantısı

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-167** (yalnız **Tasks dilimi**; MRTR ve elicitation bu fazda **değil** — § 117.7)
> **Önkoşul:** Faz 50 (dışa açılan agent yüzeyi) ve Faz 46 (dayanıklı çalıştırma, `JobKind.AgentRun`) — ikisi de arşivde
> **Paketler:** `AgentPrism.AspNetCore` (yalnız `McpServer/`)
> **Yeni paket:** `ModelContextProtocol.Extensions.Tasks` 2.2.0 — **net yeni geçişli paket: 0** (ölçüldü, § 117.5) · **Migration:** **Yok** (§ 117.3)
> **Public API:** Büyüyor — `AgentPrismMcpServerOptions` üzerinde bir opt-in alanı. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır, hepsi başlık (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/external-agents.md`, `capabilities.md`
> · sevk edilen: `AgentPrismMcpServerOptions` XML dokümanı, `src/AgentPrism.AspNetCore/README.md`
> **Manuel test alanı:** `docs/manuel-test/18-MCP-VE-A2A.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c90f78e:docs/arsiv/fazlar/117-MCP-TASKS-UZANTISI.md
> ```
>
> Damıtıldı 2026-08-27 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'i MCP sunucusu olarak tüketen bir ekip, uzun süren bir agent çağrısı boyunca **bağlantıyı açık tutmak** zorundadır: bugünkü handler agent'ı satır içinde `await` eder ve `tools/call` yanıtı run bitene kadar gelmez. MCP 2026-07-28 bunun için resmî bir uzantı tanımladı ve C# SDK'sı onu ayrı bir pakette gönderdi.

## Bitiş Ölçütleri (DoD)

- [x] `EnableTasks=false` (varsayılan) iken MCP davranışı **bit-bit bugünküyle aynı** — `McpTasksEndpointTests.EnableTasks_false_still_answers_synchronously` + `McpServerEndpointTests`'in 15 var olan testi (regresyon) yeşil
- [x] `EnableTasks=true` iken uzun run `CreateTaskResult` döner, bağlantı kapanır, `TaskId` **run kimliğine eşittir** — fonksiyonel test + `samples/AgentPrism.Api`'de gerçek koşum (aşağıda)
- [x] `tasks/get` `RunStatus`'u doğru `McpTaskStatus`'a eşler — `Queued`/`Running`→`Working`, `Completed`→`Completed`, `Failed`→`Failed`, `Canceled`→`Cancelled`, `AwaitingApproval`→`Completed` (K-103, test edildi) hepsi test edilir; `AwaitingInput` yalnız `RunKind.Workflow` satırlarında görülür ve `CatalogToolCallHandler` yalnız agent çalıştırdığı için bu koddan **pratikte hiç üretilemez** — eşleme yine de savunmacı olarak `AwaitingApproval` ile aynı dalda durur, testi yok
- [x] `tasks/cancel` mevcut run iptalini tetikler; ikinci bir iptal modeli **yoktur** — `Tasks_cancel_cancels_the_underlying_run` + gerçek sunucuda `CancelTaskAsync` sonrası `Cancelled` görüldü
- [x] Başka kiracının task id'si okunamaz — `McpTaskTenantIsolationTests` (2 senaryo: `tasks/get`, `tasks/cancel`'ın müdahale-ama-asla-okuma özelliği). **Sapma:** planın "dört koşumda" ifadesi dört SQL sağlayıcısını kastediyordu; bu izolasyon `IRunStore.GetRunAsync`'in AYNEN kullanılmasına dayanır, yeni bir kiracı filtresi eklenmedi — dört sağlayıcı güvencesi zaten Faz 41'in `TenantCoverageTests`'idir, burada yeniden kanıtlanmadı
- [x] 🚨 K-103 kontrolü yeni yola **taşındı**: onay isteyen tool taşıyan agent task modunda `Failed` değil **`Completed` (IsError=true)** döner, mesaj bugünkü inline metinle aynı. **Sapma:** plan `Failed` öngörüyordu; ölçüldü ki SDK, `CatalogToolCallHandler`'ın ürettiği hata `CallToolResult`'ını `SetCompletedAsync` ile taşır (`SetFailedAsync` değil) — bkz. Plandan Sapmalar §2
- [x] `AwaitingApproval`/`AwaitingInput` **hiçbir koşulda** `InputRequired`'a eşlenmiyor — `Approval_requiring_tool_added_after_exposure_rejects_the_task_with_todays_inline_message` bunu doğrudan denetler
- [x] Yeni tablo **yok**, migration **yok** — `ls src/AgentPrism.PostgreSql/Migrations/` bu fazdan önceki son dosyada duruyor
- [x] `dotnet list package --include-transitive` net yeni geçişli paket **0** gösteriyor — ölçüldü, aşağıdaki komut çıktısı
- [x] `AgentPrism.Mcp` uzantı paketini **görmüyor** (K-057 korunuyor); `DependencyDirectionTests` yeşil
- [x] İki örnekli kurulumda task ikinci örnekten okunuyor — **Sapma:** plan bunu 👤 elle koşulacak bir case sayıyordu; `McpTaskCrossInstanceTests` iki gerçek `AgentPrismTestHost`'u AYNI SQLite dosyasına bağlayarak otomatikleştirdi — ikinci host'un `RunBackedMcpTaskStore`'u ilkinin cache'ini hiç görmez, bu yüzden `McpTaskStatusMapping`'in yeniden-inşa yolunu da GERÇEKTEN test eder (`faz-denetim`'in 🔴#3 bulgusu)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 2ca326e`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — § "Gerçek sunucu koşumu"
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/18-MCP-VE-A2A.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/guides/external-agents.md` task yüzeyini ve **varsayılanın kapalı olduğunu** yazar; `npm run check` temiz

### Doğrulama komutları — gerçek çıktı

```text
$ dotnet list src/AgentPrism.AspNetCore/AgentPrism.AspNetCore.csproj package --include-transitive --framework net10.0 | grep -i modelcontextprotocol
   > ModelContextProtocol.AspNetCore                2.2.0                     2.2.0
   > ModelContextProtocol.Extensions.Tasks          2.2.0                     2.2.0
   > ModelContextProtocol                              2.2.0
   > ModelContextProtocol.Core                         2.2.0
# 2.2.0'ın kendisi zaten .AspNetCore uzerinden grafikteydi; net yeni paket yalniz
# ModelContextProtocol.Extensions.Tasks'in kendisi — sifir yeni GECISLI paket.

$ dotnet list src/AgentPrism.Mcp/AgentPrism.Mcp.csproj package --include-transitive --framework net10.0 | grep -i modelcontextprotocol
   > ModelContextProtocol.Core                      2.2.0       2.2.0
# Extensions.Tasks hic gorunmuyor — K-057 korunuyor.

$ ls src/AgentPrism.PostgreSql/Migrations/ | tail -3
0017_read_views.sql
0018_...   # (bu fazdan once) — bu faz hicbir yeni dosya eklemedi
```

### Gerçek sunucu koşumu

`samples/AgentPrism.Api`'ye `EnableTasks = true` eklendi (kalıcı, demonstratif — diğer
yeteneklerle aynı desen). Gerçek PostgreSQL + gerçek model sağlayıcısına karşı,
gerçek `ModelContextProtocol.Client.McpClient` ile (2026-07-28 protokolü,
`io.modelcontextprotocol/tasks` capability'si açık) çalıştırıldı:

```text
Connected.
IsTask: True
TaskId: 01a0417e-37d2-7ef7-9de8-779cb99b981c
Status: Working
  poll 0..14: WorkingTaskResult (status=Working)
  poll 15: CompletedTaskResult (status=Completed)
Completed. IsError=
Text: - Phase 117 real-server smoke test
- Summarize this sentence

--- tasks/cancel on a fresh task ---
CancelTaskAsync returned without throwing.
After cancel: CancelledTaskResult (status=Cancelled)
```

Aynı taskId, yönetim API'sinden çalıştırıldı — kimlik gerçekten paylaşılıyor:

```text
$ curl -s "$APU/api/runs/01a0417e-37d2-7ef7-9de8-779cb99b981c" -H "$APB"
{
  "id": "01a0417e-37d2-7ef7-9de8-779cb99b981c",
  "agentName": "summarizer",
  "status": "Completed",
  "tenantId": "default",
  "usage": { "inputTokens": 41, "outputTokens": 18, "totalTokens": 59 },
  ...
}
```

---

## Plandan Sapmalar

1. **Mermaid sıra diyagramı (§ Amaç) yanlıştı — `CreateTaskAsync()` çağrının hangi
   agent/kiracı olduğunu HİÇ bilmiyor, iş kuyruğu (`JobKind.AgentRun`)
   hiç kullanılmıyor.** Plan, SDK'nın `IMcpTaskStore.CreateTaskAsync()`'i
   `tools/call`'un parametrelerine erişebileceğini varsayıyordu; paket
   decompile edilerek ölçüldü (2.2.0) ve YANLIŞ çıktı — metot yalnız bir
   `CancellationToken` alır, SDK asıl tool çağrısını KENDİ `Task.Run(...)`'ıyla
   arka planda çalıştırır, AgentPrism'in kuyruğuna hiç dokunmaz. Gerçek
   mekanizma: `.WithTasks(...)`'tan ÖNCE kayıtlı kendi `CallToolWithAlternateFilters`
   girdimiz (`McpTaskRunProvisioningFilter`) `RunId`+`AgentName`+`TenantId`'yi
   orijinal istekte üretip `AsyncLocal` (`McpTaskRunAmbient`) ile aşağı akıtır —
   filtreler kayıt sırasına göre çalıştığı için bizimki Tasks'ın kendi
   filtresinden ÖNCE çalışır. "Task id = run id" hedefi (§117.3) böylece
   AYNI kalır, yalnız KURULUŞ yolu değişti. AsyncLocal'ın bu yönde
   (ata → çağırdığı her şey, `Task.Run` dahil) güvenilir aktığı gerçek
   `Task.Delay` ile zorlanan askıya alma dahil ölçüldü.
2. **§117.3'ün durum eşleme tablosu yanlıştı: `AwaitingApproval`/`AwaitingInput`
   `McpTaskStatus.Failed`'e DEĞİL, `Completed` (IsError=true)'a eşlenir.**
   Ölçüldü: `CatalogToolCallHandler`'ın K-103 kontrolü (değişmedi) bir hata
   `CallToolResult` DÖNDÜRÜR (istisna fırlatmaz); SDK bunu `SetCompletedAsync`
   ile taşır, `SetFailedAsync` ile DEĞİL — `SetFailedAsync` yalnız tool
   pipeline'ı hiç çalıştırılamadığında (SDK'nin kendi güvenlik ağı) tetiklenir.
   Bu, K-103'ün güvenlik özelliğini (asla `InputRequired`) BOZMAZ; hangi
   terminal kovanın taşıdığı bir tel-şekli detayıdır.
3. **Kiracı sınırı planın öngördüğünden daha ince: SDK'nin `tasks/cancel`'ı
   kiracı-oblivious bir sözlüğü koşulsuz iptal eder — kapatılamayan, kabul
   edilen bir sınır.** § 117.1 "SDK sözleşmesinde kiracı parametresi yoktur"
   diyordu ama bunun `IMcpTaskStore`'un KENDİSİYLE sınırlı olduğunu
   varsayıyordu. Fonksiyonel testle bulundu (statik incelemeyle değil):
   `ModelContextProtocol.Extensions.Tasks`'in `_cancellationSources` sözlüğü
   YALNIZ taskId ile anahtarlanır ve `HandleCancelTask`, `IMcpTaskStore.SetCancelledAsync`'in
   dönüşünü HİÇ okumadan bu sözlüğü koşulsuz iptal eder — AgentPrism'in bu
   sözlüğe erişimi yok, kapatılamaz. Kabul edilen sınır: bir kiracı BAŞKA
   kiracının task id'sini bilerek onu iptal EDEBİLİR (SDK'nin kusuru), ama
   ASLA okuyamaz (`GetTaskAsync` yalnız çağıranın ambient kiracısını kullanır —
   tek gerçek izolasyon sınırı budur). Ayrıntı: K-637, `docs/hafiza/mcp-a2a-sunucu.md`.
4. **Arka plan yürütmesi kendi `AmbientTenantScope` sarmalını gerektirdi —
   plan bunu hiç öngörmemişti.** SDK'nın `Task.Run(...)`'ı app KÖK
   provider'ından yeni bir `IServiceScope` açar, hiçbir `HttpContext` taşımaz;
   `ITenantContext` (HTTP tabanlı) bu durumda sessizce varsayılan kiracıya
   düşerdi. `JobWorkerBackgroundService`'in kuyruklu işler için ZATEN
   uyguladığı desenin (`AmbientTenantScope.Begin(...)`) aynısı
   `CatalogToolCallHandler.HandleAsync`'e eklendi.
5. **Bağımsız denetim (Adım 4) dört 🔴 bulgu buldu; hepsi kod değişikliğiyle
   kapatıldı, kapanış kapıları YENİDEN koşuldu.** Ayrıntı: § Denetim Bulguları.
   En önemlisi: `SetCompletedAsync`, `CatalogToolCallHandler`'ın `agent.RunAsync`'e
   hiç ULAŞMADAN döndüğü üç erken-dönüş yolunda (bilinmeyen/exposed olmayan
   tool, boş `message`, katalogda bulunamayan agent) `CreateTaskAsync`'in
   açtığı `Queued` satırı KAPATMIYORDU — ilk yazımın "run zaten kapandı"
   varsayımı bu üç dal için YANLIŞTI.
6. **`McpTaskStatusMapping.ToTaskInfoAsync` başlangıçta `run.Status`'a göre
   dallanıyordu; bu, aynı-örnek önbellek isabetinde YANLIŞ ALANA yanlış
   şekilli veri yazan bir kusur üretiyordu** (🔴#3'ü kapatırken bulundu, ayrı
   bir denetim turu gerektirmeden düzeltildi): bir run'ın KENDİ `RunStatus`'u
   `Failed`/`AwaitingApproval` olsa bile, `CatalogToolCallHandler` bunu bir
   hata `CallToolResult`'a çevirip `SetCompletedAsync`'e taşıyabilir — eski
   kod `run.Status == Failed` dalına girip önbellekteki `CallToolResult`'ı
   `Error` alanına (bir `JsonRpcErrorDetail` beklenen yere) yazardı. Düzeltme:
   önbellek isabeti VARSA run.Status'a HİÇ bakılmadan doğrudan kullanılır;
   run.Status yalnız önbellek YOKSA (çapraz-örnek / dar yarış penceresi)
   devreye girer.
7. **Kalıcılık:** `_cache`/`_taskTenants` süreç ömrü boyunca tahliye
   edilmiyordu (🔴#2) — `InMemoryMcpTaskStore`'un kendi `SweepExpired` deseniyle
   (30 sn'de bir, `TaskTimeToLive` bazlı) düzeltildi. Yalnız İKİ örneğin
   in-memory cache'ini etkiler; `IRunStore` her zaman gerçek kaynaktır.

## Bu Fazda Verilen Kararlar

- **K-636** — `ModelContextProtocol.Extensions.Tasks` 2.2.0 yalnız
  `AgentPrism.AspNetCore`'a eklendi; net yeni geçişli paket sıfır.
- **K-637** — MCP task kimliği AgentPrism'in run kimliğidir; SDK'nın
  `CreateTaskAsync()`'i bağlamsız olduğu için bu eşleşme `AsyncLocal`
  sağlayıcı filtresiyle kurulur; arka plan yürütmesi kendi
  `AmbientTenantScope` sarmalını taşır; kiracı-oblivious SDK-içi
  `tasks/cancel` müdahalesi kabul edilen, kapatılamayan bir sınır olarak
  belgelenir.

## Denetim Bulguları

`faz-denetim` (taze bağlamlı, kod yazmadı) 4× 🔴, 5× 🟡, 4× 🟢 buldu. Tümü
kapatıldı; kapanıştan sonra dört kapı YENİDEN koşuldu (`git diff`'in `--taban`
karşılaştırdığı taban commit değişmedi, yalnız çalışma ağacı değişti).

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `SetCompletedAsync`, `agent.RunAsync`'e hiç ulaşmayan üç erken-dönüş yolunda `Queued` satırı hiç kapatmıyordu — istemci sonsuza kadar `Working` görür | Düzeltildi: `SetCompletedAsync` de `CloseIfStillQueuedAsync` çağırır |
| 2 | 🔴 | `_cache`/`_taskTenants` süreç ömrü boyunca hiç tahliye edilmiyordu | Düzeltildi: `InMemoryMcpTaskStore` desenli TTL bazlı `SweepExpired` |
| 3 | 🔴 | `McpTaskStatusMapping`'in cache-miss dalı (yeniden-inşa) hiç test edilmemişti; DoD'nin "iki örnek" satırı kanıtsızdı | Düzeltildi: `McpTaskCrossInstanceTests` (iki gerçek host, tek SQLite dosyası) — bu sırada `run.Status`'a önbellek varken bile bakan ayrı bir kusur bulundu ve düzeltildi (bkz. Plandan Sapmalar §6) |
| 4 | 🔴 | Tek iptal testi, iptal TAM BİR NO-OP olduğunda da geçiyordu (echo çağrısı iptal ulaşmadan bitiyordu) | Düzeltildi: gerçek engelleyen tool + `SemaphoreSlim` senkronizasyonu (Task.Delay YOK) ile deterministik test; ayrıca `CancelRunEndpointTests`'in seed deseniyle `Queued` dalı ayrı test edildi |
| 5 | 🟡 | K-103 fallback metni (cache-miss) bugünkü tam metinle AYNI değil (tool adı eksik) — DoD "aynı" iddiasını ihlal ediyordu | Kod DEĞİŞTİRİLMEDİ (tool adı `IRunStore`'dan kurtarılamaz — bilinçli); DoD satırı ve test bunu açıkça "genel/farklı" olarak işaretler |
| 6 | 🟡 | `AmbientWriteSiteTests`'in marker listesi `McpTaskRunAmbient.Current = ...`'ı görmüyordu — kusur sınıfının kendi ratchet'i yeni yazım yerini kaçırıyordu | Düzeltildi: `McpTaskRunAmbient` `SetCurrent(...)` metoduna çevrildi (mevcut `AgentPrismRunContext.SetCurrent` deseniyle aynı ad) |
| 7 | 🟡 | `docs/hafiza/mcp-a2a-sunucu.md`'nin bir maddesi "hiçbir şey yazmaz" diyordu, kod (owning-tenant düzeltmesi sonrası) YAZAR | Düzeltildi: not güncellendi |
| 8 | 🟡 | `EnableTasks=false` + tenancy açık kombinasyonu hiç test edilmemişti — yeni `AmbientTenantScope` sarmalı senkron yolu da etkiliyor | Düzeltildi: `EnableTasks_false_still_resolves_the_real_tenant_when_tenancy_is_on` |
| 9 | 🟡 | Kapanış adımları (site, manuel test, faz dokümanı) henüz açıktı | Bu, denetimin KENDİSİNİN ÖNCESİNDE koştuğu paralel kulvarın (Adım 2-3/7) parçasıydı — kapanışta tamamlandı |
| 10 | 🟢 | Yorum yanlış kural numarası veriyordu (K2 yerine K1) | Düzeltildi |
| 11 | 🟢 | İki kez `UseMcpServer()` çağrısı bir guard'sız senaryo | Düzeltilmedi — bugün desteklenmeyen bir kullanım, ayrı bir karar ister |
| 12 | 🟢 | `AgentName = "(unresolved)"` geçici satırlar UI'da görünebilir | Bulgu 1 kapanınca ömrü kısaldı, ayrı aksiyon alınmadı |
| 13 | 🟢 | `TaskPollInterval`/`TaskTimeToLive` doğrulanmıyor | Doğrulanmadı — `Budget` da doğrulanmıyor, mevcut desenle tutarlı |

## Site Senkron Gerekçesi

`dokuman-bakim.py --site-denetle` üç kuralı dosya YOLUNA göre tetikledi;
üçü de bu faz için YANLIŞ ALARM — hedef sayfalar açıldı ve içerik gerçekten
etkilenmediği doğrulandı:

- **`http-api` → `http-api.md`** (`AgentPrismMcpServerBuilderExtensions.cs`
  tetikledi): sayfa GENEL bir kılavuzdur (kimlik doğrulama, akış, sayfalama,
  hatalar), tek tek `endpoint` listelemez — o iş üretilen `api/` referansı ve
  `agentprism.json`'undur. Bu faz **hiçbir yeni HTTP `endpoint`'i açmadı**
  ("Yeni AgentPrism ucu yok" — § Planlanan Public API); `tasks/get`/`tasks/cancel`
  mevcut MCP transport'unun İÇİNDEKİ protokol metotlarıdır, OpenAPI belgesine
  hiç girmez. Güncellenecek bir şey yok.
- **`paket-tanimi`/`paket-readme` → `packages.md`** (`.csproj` + `README.md`
  değişti): sayfa PAKET BAŞINA tek satırlık bir açıklama taşır
  (`AgentPrism.AspNetCore` → "The HTTP API and the access layers"); bu faz
  paketin AMACINI değiştirmedi, yalnız İÇİNE bir `PackageReference` ve
  `README.md`'ye bir cümle ekledi. Açıklama hâlâ doğru.

## Sonraki Faza Devir Notu

- **`RunBackedMcpTaskStore`'un in-memory `_cache`/`_taskTenants`/`_createdAt`
  haritaları TEK örnek ömürlüdür ve `TaskTimeToLive` bazlı sweep ile
  tahliye edilir** — bu üç harita ARTIK "no new table" kısıtının bir parçası
  değil, tamamen performans/aynı-örnek-hızlandırma katmanıdır; kaynak gerçeği
  her zaman `IRunStore`. Bir sonraki faz bu store'a dokunursa önce bu üç
  haritanın ne zaman/nasıl senkron kaldığını (§ Plandan Sapmalar 6-7) okusun.
- **`McpTaskRunAmbient` deseni** (pre-filter + `AsyncLocal` + `SetCurrent`)
  başka bir SDK-genişletme noktası "çağrının bağlamını bilmiyor" sorunuyla
  karşılaşırsa yeniden kullanılabilir bir kalıptır — `McpTaskRunProvisioning.cs`
  içindeki uzun `<remarks>` bunun NEDEN çalıştığını (ve neden AsyncLocal'ın
  normalde yasak yönünün TERSİ olduğunu) anlatır.
- **MRTR/elicitation** (§117.7, kapsam dışı bırakıldı) aynı `IMcpTaskStore`
  üzerine biner — `SetInputRequestsAsync`/`ResolveInputRequestsAsync` şu an
  boş. Biri bunu talep ederse `RunBackedMcpTaskStore`'a ÜÇÜNCÜ bir durum modeli
  EKLEMEDEN, mevcut onay/input modeliyle nasıl örtüştüğü ölçülmeli.
  `İki örnekli kurulum` testinin (bu fazda eklenen `McpTaskCrossInstanceTests`)
  deseni MRTR testleri için de yeniden kullanılabilir.
- **SDK'nın kiracı-oblivious `tasks/cancel` davranışı** (K-637) AgentPrism
  tarafından kapatılamaz bir SDK sınırıdır; gelecekteki bir SDK sürümü
  `_cancellationSources`'a kiracı farkındalığı eklerse bu not ve
  `McpTaskTenantIsolationTests`'in ilgili testi gözden geçirilmeli.
