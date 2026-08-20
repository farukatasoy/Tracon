


### K-388

K-387 sonrası SqlServer paketi Postgres'ten hâlâ ~2,8 kat yavaştı (355,5 sn'e karşı 128,6 sn, tam çözüm koşusunda). `ApplyOneAsync` migration dosyası başına 4 round-trip atıyordu: `BeginTransactionAsync` → migration SQL'i çalıştır → `INSERT INTO __migrations` → `CommitAsync`. Kanıt round-trip SAYISININ değil, round-trip BAŞINA GECİKMENİN baskın maliyet olduğunu gösteriyor: Postgres'in migration dosya sayısı SqlServer'dan FAZLA (28'e karşı 15) — yani Postgres round-trip sayısı da yüksek olmalı — yine de 3 kat hızlı; bu `Microsoft.Data.SqlClient`'ın TDS protokolünün `Npgsql`'e göre komut başına daha yüksek gecikme taşıdığını gösterir. Düzeltme migration SQL'i ile deftere yazan `INSERT`'i TEK komutta birleştirir (dosya başına 4 → 3 round-trip); `BeginTransactionAsync`/`CommitAsync` ADO.NET `DbTransaction` nesnesi üzerinden AYNEN kalır, hata sınıflandırma (`DescribeDatabaseError`) DEĞİŞMEZ. Bu birleşim K-318'in "aynı toplu işlemde DEĞİŞEN bir nesneye referans" tuzağına GİRMEZ: `__migrations` migration çalışmadan ÖNCE zaten var ve migration'ın DDL'inin oluşturduğu/değiştirdiği hiçbir nesneye referans vermiyor. BİLEREK yapılmadı: (1) `CreateSchema`+`CreateMigrationsTable` birleşimi — bu TAM K-318 deseni, `CREATE TABLE {Schema}.__migrations` aynı toplu işlemde henüz derleme anında var olmayabilecek, `EXEC(N'CREATE SCHEMA...')` ile ERTELENMİŞ bir şemaya referans verirdi; (2) `BEGIN TRAN`/`COMMIT`'i ham SQL + `SET XACT_ABORT ON` ile TEK round-trip'e indirmek — bu depoda `XACT_ABORT` hiç kullanılmamış ve üç `SqlDialect`'in hata sınıflandırması bugün ADO.NET'in `DbTransaction` nesnesi üzerinden akan hataya güveniyor; daha büyük, ayrı bir karar. Üç sağlayıcının da (paylaşılan kod) entegrasyon testleri yeşil: SqlServer 479/479 (~300 sn → ~285,5 sn, izole ölçüm), Postgres 950/950, SQLite 493/493.

### K-495

**Ölçüldü:** elle kurulmuş bir grafta, giriş düğümü OLMAYAN bir `AIAgentBinding`, düz `AddEdge` ile bağlandığında sarılı agent'ı hiç çağırmıyor — mesajı alıyor ama sessizce hiçbir şey yapmadan tamamlanıyor (ne `runs` satırı, ne hata, ne çıktı). Yalnız GİRİŞ düğümü `WorkflowRunner.StartAsync`'in gönderdiği `TurnToken`'ı alıyor; MAF'ın hazır `Sequential` kalıbı ajanlar arasında bu "sırayı al" sinyalini kendi iç protokolüyle taşıyor ve bu protokol hazır kalıp kurucuları DIŞINDA erişilebilir değil. İki alternatif DENENDİ ve İKİSİ DE agent'ın gerçekten çağrılmasını sağlamadı: (1) fonksiyon düğümünün kendi işleyicisinden elle `TurnToken` göndermek (veri, sonra token sırasıyla) — `two` düğümü yine tek seferlik boş bir çağrıyla tamamlandı, `runs` satırı açmadı; (2) MAF'ın kendi `ChatForwardingExecutor`'ını iki düğüm arasına röle olarak koymak — röle çalıştı ama agent yine çağrılmadı. Çözüm: agent adımı, tree'ye bağlı agent'ı (`ChildAgentInvoker`) `RunStreamingAsync` ile DOĞRUDAN çağıran bir `FunctionExecutor<List<ChatMessage>,List<ChatMessage>>` alt sınıfı (`WorkflowAgentStepExecutor`) olarak bağlanır — `TurnToken`'a hiç ihtiyaç duymaz, her düğüm aynı desenle çalışır ve `WithOutputFrom` artık son düğüm ne olursa olsun (agent ya da fonksiyon) TEK biçimde çalışır; ayrı bir "agent sonda ise çıktı toplayıcısı ekle" özel durumuna gerek kalmadı. Ayrı bir alt sınıf (satır içi kurulmuş çıplak `FunctionExecutor` değil) olmasının nedeni: `WorkflowGraphReader`'ın bir agent adımını gerçek bir fonksiyon düğümünden yalnız DERLENMİŞ graftan (tanımdan değil) ayırt edebilmesi — tip adı `WorkflowAgentStepExecutor` bu ayrımın TEK dayanağı. **Yan etki:** karışık zincirdeki bir agent adımı workflow'un KENDİ olay akışına `MessageDelta` yaymaz (`RunStreamingAsync` tüketilip tek parça sonuç toplanıyor, `AgentResponseUpdateEvent` üretilmiyor); agent'ın kendi çocuk `runs` satırı yine de tam mesaj ve kullanım geçmişi tutar — yalnız üst seviye canlı akış o düğüm için token-token ayrıntısını kaybeder.

### K-403

`MT-WF-095`/`096` ölçtü: geçersiz `sessionId` (128 karakter sınırı aşımı veya izin verilmeyen karakter) gönderildiğinde istemci hiçbir SSE çerçevesi (ne `event: run` ne `event: error`) almıyordu — düz, teşhis bilgisi taşımayan bir `HTTP 500` (`{"title":"An error occurred while processing your request."}`) geliyordu; sunucu logunda doğru mesaj görülüyordu ama istemciye hiç ulaşmıyordu. Kök neden: `RunStreamingAsync` (`WorkflowRunner.cs:186`) `async`/`yield return` TAŞIMAYAN düz bir metottu — `WorkflowSessionId.Require(request.SessionId)` bir nesne başlatıcısının İÇİNDE, metot GÖVDESİNİN parçası olarak SENKRON çalışıyordu; metot yalnızca `ExecuteAsync(...)`'in ürettiği `IAsyncEnumerable`'ı DÖNDÜRÜYORDU. İstisna bu yüzden `WorkflowEventStream`'in (SSE yazıcısı, `AgentPrism.AspNetCore`) `await foreach` içindeki `catch` bloğuna hiç ULAŞMADAN doğrudan çağrı zincirinden fırlıyor, ASP.NET'in genel `ExceptionHandlerMiddleware`'ine düşüyordu. Karşılaştırma: `RespondStreamingAsync`/`ResumeStreamingAsync` zaten gerçek `async IAsyncEnumerable` yineleyicileriydi (MT-WF-064/065/094'te doğru `event: error` üretiyorlardı) — yalnız `RunStreamingAsync`'in bu yapısal farkı boşluğu yaratıyordu; `AsyncLocal`/`Activity.Current`'ın async yardımcı metotta açılamaması kuralıyla (Faz 6/11/12/15, `docs/hafiza/cekirdek-calistirma.md`) AYNI sınıfın **beşinci** tekrarıdır — "deferred-execution bağlamının dışında kalan kod, iteratörün gerçek çağrı zamanlamasını kaybeder." Çözüm: `RunStreamingAsync` `async IAsyncEnumerable<RunEvent>` + `[EnumeratorCancellation]` yapıldı; `WorkflowExecution` kaydı ve `WorkflowSessionId.Require` çağrısı artık yineleyici GÖVDESİNİN içinde, `await foreach` başlamadan hemen önce çalışıyor — ilk `MoveNextAsync()` çağrısı `WorkflowEventStream`'in kendi `try/catch`'i İÇİNDE gerçekleşiyor. Ampirik doğrulama (MT-WF-095/096'nın birebir tekrarı): her iki senaryo da artık `event: run` ardından `event: error` (`AgentPrismException`, doğru mesaj) üretiyor, `HTTP 200` (SSE akışı) — düz `500` yok.

### K-404

`MT-SKILL-020` ölçtü: `skillNames` alanında var olmayan bir skill adı taşıyan bir agent'ı `POST /api/agents` ile kaydetmek `HTTP: 201 Created` ile BAŞARILI oluyordu — hiçbir doğrulama hatası yoktu. Kök neden: `AgentEndpoints.cs`'deki `CreateAgentAsync`/`UpdateAgentAsync` yalnızca temel şekil doğrulaması (`Validate(request)`) ve çağrı-grafiği döngü denetimini (`ValidateCallGraphAsync`, `AgentCallGraph.Validate`) çağırıyordu; asıl varlık denetimlerini (skill/tool/callable-agent VAR MI, model geçerli mi — `AgentDefinitionValidator.ValidateAsync`, `CheckSkillsAsync` dahil) yalnız AYRI `POST /api/agents/validate` ucu (`ValidateAgentAsync`) çağırıyordu; bu uç HİÇBİR ŞEY KAYDETMEZ, istemci onu ayrıca çağırmadıkça save yolunun kendisi hiç etkilenmezdi. Rapor kapsamı skill'in ötesine genişletiyordu: aynı kod yolunun kapsadığı diğer varlık denetimleri (tool adı, callable-agent adı) de muhtemelen aynı şekilde etkileniyordu. Çözüm (K1 kuralı — "güvenlik kusurunda tam düzeltme seçer, kısmi yama değil"): dar bir "yalnız skill" yaması yerine `AgentDefinitionValidator` (zaten DI'da kayıtlı, `ValidateAgentAsync` onu zaten kullanıyordu) her iki SAVE ucuna da enjekte edildi; yeni bir `ValidateEntitiesAsync` yardımcı metodu `validator.ValidateAsync(request.ToDefinition(), ...)` çağırıp `report.Valid=false` ise hata mesajlarını birleştirip `400` döner — bu, skill/tool/callable-agent/model doğrulamasının TAMAMINI kapsar, yalnız skili değil. Var olan `ValidateCallGraphAsync` (katalog-genelinde döngü denetimi, farklı bir mekanizma) DOKUNULMADAN bırakıldı — iki denetim birbirini tamamlıyor, çakışmıyor. Ampirik doğrulama: raporun `curl` reprodüksiyonu birebir tekrarlandı → `HTTP: 400`, `"'hayalet-skilli-agent' agent'i 'hic-var-olmayan-skill' skill'ine isaret ediyor ancak skill bulunamadi."` Regresyon kontrolü: geçerli bir agent (skill'siz) hâlâ `201` ile kaydediliyor; `PUT` (update) yolunda da aynı bilinmeyen-skill reddi doğrulandı.

## K-510 — Yerel referans dosyasının konumu (Faz 74)

Plan (`docs/74-YEREL-REFERANS-YUZEYI.md` §74.2) dosyayı `AGENTS.md` ile aynı
dizine, git köküne koyuyordu. Faz denetimi bunu 🔴 bulgu olarak düşürdü.

**Ölçüm.** Bir git kökünde iki proje kuruldu: `src/Web` (`Microsoft.NET.Sdk.Web`,
`AgentPrism` meta paketi) ve `src/Worker` (`Microsoft.NET.Sdk`, yalnız
`AgentPrism.Core`). Tek `.sln`, tek `dotnet build`. Sonuç: dosyada **2** paket
satırı ve **HTTP bölümü yok** — Worker'ın görünümü kazanmıştı. Web'in sekiz
paketi ve `agentprism.json` işareti kaybolmuştu. Planın iki DoD satırı bu
düzende yanlıştı: "ikinci build dosyaya dokunmaz" ve "yalnız `AgentPrism.Core`
referanslayan projede OpenAPI satırı yok".

**Birleştirme neden düştü.** Önce dosyanın MERGE edilmesi denendi: var olan
dosya okunur, hâlâ diskte olan satırlar korunur, bu projenin satırlarıyla
birleştirilir, `Distinct()` ile tekrarlar düşer. İki sebeple bırakıldı:

1. **MSBuild ifade sınırı.** Öğe dönüşümünün (`@(X->'…')`) ayracı tek tırnaktır;
   içinde `$([System.String]::Copy('%(Identity)')…)` yazmak dönüşümü erken
   kapatır. Etiketler `%(NuGetPackageId)` üzerinden alınarak bu aşıldı, ama
   var olan satırdan yolu çıkarmak (`Substring(IndexOf(': ') + 2)`) hâlâ
   kırılgan metin cerrahisiydi.
2. **Yarış.** Daha temelde, bir çözümdeki projeler **paralel** derlenir.
   Birleştirme okuma ile yazma arasındaki yarışı çözmez; yalnız ardışık
   derlemelerde yakınsar. Ölçüldü: ilk birleştirmeli koşumda dosya yine
   Worker'ın iki satırında kaldı ve üç derleme boyunca orada takıldı.

**Seçilen tasarım.** `$(MSBuildProjectDirectory)`. Ölçüldü: Web 9 satır + HTTP
bölümü, Worker 2 satır ve HTTP bölümü yok, git kökünde hiçbir dosya yok; üç
ardışık `-t:Rebuild` sonrası iki dosyanın da `mtime`'ı değişmedi.

**Sürüm başlığının kaldırılması.** Aynı denetimin 🟡 bulgusu: iki paket farklı
sürümde çözüldüğünde `@(…->'%(NuGetPackageVersion)'->Distinct())` değerleri `;`
ile birleştiriyor ve `Include` bunu **iki satıra** bölüyordu (ölçüldü:
`Core 272` + `Sqlite 271` → dosyada iki ayrı sürüm satırı). Tek bir başlık iki
sürümü dürüst anlatamaz; sürüm zaten her yolun içindedir
(`/agentprism.core/0.0.0-preview.0.272/lib/…`).

## K-511 — OpenAPI belgesinin paketlenmesi (Faz 74)

**Kapsam ölçümü.** `samples/AgentPrism.Api` çalışırken `/openapi/v1.json`
**127 path / 252 şema** sundu; paketlenen `docs/openapi/agentprism.json`
**123 path / 250 şema** taşıyor. Fark dört **isteğe bağlı** uçtur:
`/agentprism/a2a/summarizer`, aynı yolun `.well-known/agent-card.json`'ı,
`/agentprism/api/diagnostics` ve `/agentprism/api/voice/sessions/{id}/stream`.
Belge `AgentPrismTestHost`'un her zaman açık yüzeyini anlatır ve
`OpenApiSnapshotTests` onu oraya sabitler — bu bir sapma değil, tanımın kendisi.

**Paketleme biçimi.** `<None Remove="buildTransitive/**" />` +
`<None Include="../../docs/openapi/agentprism.json" Pack="true" PackagePath="buildTransitive/" />`.
`Update` burada SESSİZCE çalışmaz: SDK'nın varsayılan `None` glob'u yalnız iç
(TFM'e özgü) derlemelerde uygulanır, `dotnet pack` ise paket dosyalarını dış
çapraz-hedefleme derlemesinde toplar (Faz 73 Sapma 10, aynı tuzak).

## K-512 / K-513 — Örnek kapısının iddiaları ve sınırı (Faz 74)

Kapı (`tests/AgentPrism.Core.UnitTests/Architecture/CapabilityExampleTests.cs`)
dört iddia taşır:

1. **Her giriş noktasının en az bir aşırı yüklemesinde `<example>` var.** Ad
   bazlıdır — `AddTool` yedi aşırı yüklemeye sahiptir ve yedisine de aynı örneği
   yazmak gürültüdür. Kapsam kapısı da ad bazlıdır (K-509).
2. **39 adın 39'u okundu.** XML doküman kimliğinde METOT jeneriği **çift** ters
   tırnak taşır (``AddContentGuard``1``, ``AddWorkflowFunction``2``); tip
   jeneriği tek ters tırnaktır. Plan `` `1 `` yazıyordu ve etkilenen üye sayısını
   üç sanıyordu — gerçek dört: `AddContentGuard`, `AddJobHandler`,
   `AddToolsFrom`, `AddWorkflowFunction`.
3. **Örnekteki her `Add*`/`Use*`/`Map*` adı public API'de var.** Ad şeklinden
   bir üyenin AgentPrism'e ait olup olmadığı anlaşılamaz; `AddSingleton`,
   `AddHealthChecks`, `MapHealthChecks` Microsoft'undur. Bu yüzden açık bir
   `ForeignRegistrationMembers` listesi vardır ve **yalnız Microsoft üyelerini**
   taşır. Oraya bir AgentPrism adı eklemek, listenin adı gereği, incelemede tam
   olarak yanlış iddia olarak görünür. Kapı bu iddiayla ilk koşumunda var olan
   bir örnekteki iki çerçeve çağrısını yakaladı.
4. **Örnek kendi üyesini çağırır.** 27 elle yazılmış örneğin davet ettiği
   kopyala-yapıştır kusurunu kapatır.

**Sessiz yeşil.** Kapı derlenmiş XML okur. XML yoksa "hiç kapsanmayan üye yok"
sonucu çıkardı; bu yüzden önce "her giriş noktası için dokümanlı bir üye bulundu
mu?" diye sorar ve bulamazsa `Build the solution first` mesajıyla düşer.
Ölçüldü: `artifacts/bin/AgentPrism.Voice/release_net10.0` silinince kapı düştü.

**Sınır (K-513).** Metin denetimi bir örneğin DERLENDİĞİNİ kanıtlayamaz. Faz 74
denetimi iki hatalı örnek buldu; ikisi de yukarıdaki dört iddiadan geçmişti:

- `Configure` örneği `options.DefaultTimeout` yazıyordu. `DefaultTimeout`
  gerçek bir API adıdır — ama `AgentPrismToolOptions` üzerinde, `AgentPrismOptions`
  üzerinde değil. Tüketicide `CS1061`.
- `UseA2A` örneği `o.ExposedAgents = ["support"]` yazıyordu. `ExposedAgents`
  gerçek bir property'dir — ama `IList<string> { get; } = []`, yani salt-okunur.
  Tüketicide `CS0200`. Doğrusu `o.ExposedAgents.Add("support")`.

Kapanış kanıtı olarak 39 giriş noktasına ait **40** `<example><code>` bloğu
derlenmiş XML'den programatik olarak çıkarıldı (elle kopyalanmadı ki
transkripsiyon hatası girmesin), her biri bir metot gövdesine kondu, yer
tutucular (`OrderTools`, `IOrderGateway`, `refundTool`, `OnPremiseModelProvider`,
`NightlyReportJobHandler`, `CustomerNameGuard`, `BuildTriageGraph`) ayrı bir
dosyada tanımlandı ve paketlenmiş `AgentPrism` + yedi sağlayıcı/depo paketiyle
derlendi: `Build succeeded`. Kalıcı bir kapı `ADAYLAR.md` **F-125**'tir.

---

## Uzun karar gerekçeleri (2026-08-20 doküman bakımı)

> `docs/KARARLAR.md` bütçesini aştı (476 063 B > 475 000; dosyanın %96'sı karar
> satırıydı ve 513 karar birikmişti). K-505'in kurduğu desen uygulandı: en uzun
> yirmi kararın **ölçüm anlatısı** buraya **birebir** taşındı, satırda kararın
> kendisi, açılış gerekçesi ve bu dosyaya bir işaret kaldı. **Hiçbir içerik
> silinmedi.** Kazanç ölçüldü: 476 063 → 445 038 B (%94 → %93,7 doluluk).

### K-402 — `UseWorkflows()` artık `AgentPrismWorkflowOptions`'ı `AgentPrism:Workflows` bölümünden `IConfiguration`'a BAĞLIYOR (HATA-K-004, Kritik, manuel kabul testi Ortak Kuyruk)

`MT-WF-091`/`092`/`093` ölçtü: `Enabled=false`, `MaxSuperSteps=2`, `EnableCheckpointing=false` — üçü de ayrı ayrı `dotnet user-secrets` ile denendi, üçü de HİÇBİR ETKİ göstermedi; çalıştırmalar ayar hiç verilmemiş gibi normal tamamlandı (gerçek model çağrıları, gerçek ücretle). Kök neden: `AgentPrismWorkflowsBuilderExtensions.cs:52`'deki `UseWorkflows()` yalnızca `services.AddOptions<AgentPrismWorkflowOptions>();` çağırıyordu — `AgentPrismWorkflowOptions.SectionName` sabiti (`"AgentPrism:Workflows"`) TANIMLIYDI ama hiçbir yerde kullanılmıyordu (`grep` sıfır eşleşme verdi); diğer tüm `Use*()` uzantıları (`UseOpenAI`, `UsePostgreSql`, `UseSkillScripts` vb.) config bölümünü açıkça bağlarken, `UseWorkflows()` yalnızca kod-içi isteğe bağlı `configure` lambda'sını destekliyordu. Sınıfın YEDİ alanının TAMAMI (`Enabled`, `EnableCheckpointing`, `MaxConcurrentRuns`, `RunTimeout`, `MaxSuperSteps`, `KeepCheckpointsAfterCompletion`) etkileniyordu — sonsuz döngü koruması, motor kapatma anahtarı ve checkpoint kontrolü gibi üretim-kritik güvenlik sınırlarının HİÇBİRİ konfigürasyonla ayarlanamıyordu. Çözüm: `AgentPrism.Workflows` projesi zaten `AgentPrismAotCompatible=false` (MAF'ın workflow motoru yansıma kullanır) olduğu için `AgentPrism.Core`'un elle-yazılmış AOT-güvenli bağlayıcı deseni yerine standart `OptionsBuilder<T>.BindConfiguration(sectionName)` (paket: `Microsoft.Extensions.Options.ConfigurationExtensions`, yeni bağımlılık, yalnız bu projeye eklendi) kullanıldı — `configure` lambda'sı bağlamadan SONRA çalışır, kod hâlâ config'in üzerine yazabilir. Ampirik doğrulama: `Enabled=false` → çalıştırma `AgentPrismException` ile doğru şekilde reddedildi (`"Workflow calistirma kapali. 'AgentPrism:Workflows:Enabled' ayarini acin."`); `MaxSuperSteps=2` → 3 süper-step üreten gerçek bir workflow artık doğru mesajla (`"Workflow 2 super-step sinirini asti ve durduruldu..."`) durduruldu — ikisi de MT-WF-091/092'nin dokümanladığı beklenen mesajla birebir eşleşti.

### K-392 — `InvariantGlobalization` hem örnek uygulamadan (`samples/AgentPrism.Api`) hem paket şablonundan (`AgentPrism.Starter`) KALDIRILDI; SQL Server desteğiyle bağdaşmıyor

Manuel kabul testi (MT-SQL-024 ve §2/§3'ün SQL Server yarısı) `UseSqlServer()` yapılandırıldığında uygulamanın HİÇ açılmadığını ölçtü: `SqlConnection.Open` koşulsuz `System.NotSupportedException: Globalization Invariant Mode is not supported.` fırlatıyor ve barındırıcı `Hosting failed to start` ile ölüyor — `Now listening` hiç yazılmıyor. `Microsoft.Data.SqlClient` Globalization Invariant Mode'u DESTEKLEMEZ. Kusur örnekle sınırlı değildi: aynı satır `AgentPrism.Starter` şablonundaydı ve şablon `--UseSqlServer` seçeneği SUNUYOR — yani `dotnet new` ile SQL Server seçen HER tüketici açılmayan bir proje alıyordu. AgentPrism bir NuGet paket ailesidir; şablon kusuru doğrudan tüketiciye taşınır. Hata mesajı AgentPrism'i hiç anmadığı için tüketicinin kök nedeni bulma şansı da yoktu. Ayırt edici kanıt: geçerli bağlantı dizesiyle de, erişilemez host'la da AYNI istisna geldi — yani hata bağlantı hedefine değil `SqlConnection.Open`'ın kendisine ait; `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0` ortam değişkeni İŞE YARAMIYOR çünkü derlenmiş `runtimeconfig.json` içindeki `configProperties` ortam değişkenini ezer. SQLite ve bellek içi izlekler etkilenmiyordu (`Microsoft.Data.Sqlite` bu moda bağımlı değil), bu yüzden kusur bugüne kadar görülmemişti. Ayarın kayıtlı bir gerekçesi YOKTU (karar defterinde hiç geçmiyordu); ICU bağımlılığından kaçınma/boyut kazancı amacıyla konmuş olmalı — doğruluk bu kazancı yener. İki csproj'a da ayarın NEDEN açılmadığını yazan bir yorum bırakıldı ki sonraki oturum geri eklemesin.

### K-406 — `BindRunRecording` artık `RecordRunInput`'ı config'ten okuyor (HATA-K-007, Yüksek, manuel kabul testi Ortak Kuyruk — aynı kök neden HATA-S2-002/HATA-S4-015'te de bağımsız bulunmuştu)

`MT-EVAL-092` ölçtü: `AgentPrism:RunRecording:RecordRunInput=false` `dotnet user-secrets`'a yazılıp uygulama yeniden başlatıldığında yeni bir çalıştırmanın girdisi YİNE tam olarak kaydediliyordu — `GET /api/runs/{id}/input` beklenen `404` yerine `200` + tam girdi (`messages: [...]`) döndürdü. Aynı kök neden bu Ortak Kuyruk koşumundan ÖNCE de iki AYRI serit sonucunda bağımsız olarak bulunmuştu: `HATA-S2-002` (`MT-API-064`, env değişkeniyle) ve `HATA-S4-015` (`MT-UIRUN-032`, env değişkeniyle + `ps eww` ile süreç ortamı doğrulanarak) — üçü de aynı satırı işaret ediyordu. Kök neden: `AgentPrismServiceCollectionExtensions.BindRunRecording` (`AgentPrismServiceCollectionExtensions.cs:1769-1799`) `Enabled`, `RecordMessageDeltas`, `RecordToolPayloads`, `MaxPayloadLength` alanlarını `TryReadBool`/`int.TryParse` ile okuyordu AMA `RecordRunInput` (`AgentPrismOptions.cs:461`, varsayılan `true`) için ilgili `TryReadBool` çağrısı hiç yazılmamıştı — property config/`user-secrets`/ortam değişkeninden asla `false` olamıyordu. `RunRecordingAgent`'ın kendisi (`!_options.RecordRunInput` kontrolü, ~satır 593) ve `GET /input` ucunun (`RunEndpoints.cs`) 200/404 mantığı DOĞRUYDU — sorun yalnız bağlama katmanındaki eksik bir satırdı. Çözüm: `BindRunRecording`'e `Enabled`'dan hemen sonra `if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordRunInput), out var recordRunInput)) { options.RecordRunInput = recordRunInput; }` eklendi — diğer beş alanla birebir aynı desen. Ampirik doğrulama (gerçek sunucuya karşı): `RecordRunInput=false` iken yeni bir çalıştırmanın `GET /input`'u artık `404 "Girdi kaydi yok"`; ayarı kaldırıp (varsayılan `true`) yeniden başlatınca aynı uç `200` + tam girdi. `docs/manuel-test/SONUCLAR-S2-2026-08-13.md` ve `SONUCLAR-S4-2026-08-13.md`'deki karşılık gelen bulgulara da bu karara işaret eden kapanış notu eklendi.

### K-431 — Örnek uygulama rol politikalarını `AgentPrism:Demo:Roles:Enabled` ile kaydeder ve o anda `RequireRolePolicies`'i AÇAR; şema gösterim amaçlı bir başlık okuyucusudur

F-104 ölçüldü ve doğrulandı: `grep -rn "AgentPrismPolicies\.\|AddPolicy" samples/` **boş dönüyordu** — `RoleEndpointConventionBuilderExtensions.RequireRole` politika adını `null` çözünce hiçbir yetkilendirme eklemez (bilinçli, K1: yükselten kurulumu `403` ile kırmamak için), bu yüzden Skill/Approval/Retention/Quota/Workflow uçlarındaki HER `RequireRole(...)` çağrısı referans dağıtımda **sessizce etkisizdi**. Sonuç: Faz 55'in "Reader karar veremez" DoD iddiası izole fonksiyonel test host'unda kanıtlıydı ama gerçek dağıtımda **gözlemlenemiyordu**; manuel kabul testi bu ortam kısıtını üç ayrı dosyada (`13-KIRACI-VE-GUVENLIK.md`, `14-SKILL-VE-SCRIPT.md`, `21-DAYANIKLILIK-VE-IPTAL.md`) ayrı ayrı kaydetmek zorunda kalmıştı. Politikaları koşulsuz kaydetmek elendi: örnek uygulamada hiçbir kimlik doğrulama şeması yoktur, koşulsuz kayıt her rol korumalı ucu `401`'e düşürüp örneği kullanılamaz hâle getirirdi. Seçilen yol: bayrak **varsayılan kapalı** (K1 — kapalıyken davranış birebir eskisi, ölçüldü: başlıksız `GET /api/agents` `200`, `POST` `201`); açıkken üç politika rol claim'ine bağlanır ve `AgentPrismEndpointOptions.RequireRolePolicies = true` yazılır — böylece kayıt sonradan silinirse uygulama **başlamaz**, kusur sessizce geri dönemez. Şema (`DemoRoleAuthenticationHandler`) `X-AgentPrism-Demo-Role` başlığını okur ve **hiçbir doğrulama yapmaz**; sınıfın XML dokümanı bunu büyük harfle yazar. Gerçek koşumla kanıtlandı: başlıksız `401`, `reader` ile liste `200`, `reader` ile agent oluşturma `403`, `admin` ile `201`. Sınıf taraması yapıldı — "kayıtlı değilse hiçbir şey yapmaz" deseninin diğer vakaları (MCP OAuth → `501`, ek deposu → içerik veritabanında) **görünür** davranışlardır ve kusur değildir.

### K-394 — Workflow çalıştırmaları artık kota muhasebesinden geçiyor: `WorkflowEndpoints.RunAsync` agent'larla AYNI `QuotaGate.CheckAsync` 429 kapısından geçer, `WorkflowRunner.CompleteAsync` workflow'un TAMAMINI (Depth 0, `TreeUsage`/`TreeCost` toplamından) TEK bir "run" olarak `QuotaEnforcer.RecordAsync`'e yazar (HATA-S1-006, Kritik, manuel kabul testi S1)

`MT-RET-033` ölçtü: `ozetle-ve-cevir` workflow'u iki kez art arda çalıştırıldı, `quota_usage.runs` **hiç artmadı** (`FARK=0`, deterministik) — `WorkflowEndpoints.RunAsync` `RunRecordingAgent`'a hiç uğramıyordu (kendi `IWorkflowHost` motoruyla çalışıyor), dolayısıyla ne 429 kapısı ne muhasebe hiç devredeydi; tanımlı HERHANGİ bir kota (çalıştırma sayısı, token, maliyet) workflow yoluyla baştan sona atlatılabiliyordu. Tasarım kararı: workflow'un kendi `runs` satırının `usage`/`cost`'u YOKTUR (Faz 20) — maliyeti yalnız altındaki agent adımları taşır; adım-adım muhasebe yerine workflow'un TAMAMI TEK bir kota "run"ı sayılır (agent tarafında zaten uygulanan `Depth==0` kuralıyla AYNI gerekçe — MT-RET-033'ün kendi sorduğu "her adım mı yoksa workflow'un kendisi mi" sorusunun cevabı). Tüketim, tamamlanan çalıştırma ağacının SQL'de zaten hesaplanan toplamından (`RunRecord.TreeUsage`/`TreeCost`) okunur — yeni bir agregasyon yazılmadı, var olan mekanizma yeniden kullanıldı. `QuotaEnforcer` `WorkflowRunner`'a opsiyonel (`null` olabilir) bağımlılık olarak eklendi; `RunResumeAsync`/`RespondStreamingAsync` da AYNI `CompleteAsync`'ten geçtiği için ayrı bir kablolamaya gerek kalmadan muhasebeyi otomatik alır — yalnız İLK `/run` çağrısı 429 kapısından geçer (K-162: devam eden bir çalıştırma kota aşılınca kesilmez, sürdürme de bu ilkeyle aynı muamele görür).

### K-386 — K-317 kapandı: Docker Desktop 4.29.0 → 4.86.0 güncellemesi gerçek `mssql/server`'daki Rosetta hatasını çözdü; SQL Server sözleşme testleri bu makinede artık `azure-sql-edge` ikamesi OLMADAN, gerçek imajla koşuyor

K-317'nin yeniden açılma koşulu ("Docker Desktop güncellenir/Rosetta gerçekten çalışırsa ... gerçek `mssql/server` ile tekrarlanır") gerçekleşti. Kök sebep teşhisi: kurulu Docker Desktop 4.29.0 (Şubat 2024) host macOS 26.5.1 için ÇOK ESKİYDİ — `useVirtualizationFrameworkRosetta: true` ayar dosyasında açık, VM `--rosetta` bayrağıyla başlıyor, HOST Rosetta (`oahd`, `arch -x86_64 /usr/bin/true`) çalışıyordu, ama container İÇİ `amd64` yürütme HER ZAMAN "Rosetta is only intended to run on Apple Silicon..." ile `exit 133` veriyordu; sistem loglarında (`log show --predicate 'eventMessage CONTAINS[c] "rosetta"'`) Docker'ın VM açılışında Rosetta kurulumunu HİÇ denemediği görüldü — Homebrew'daki güncel cask sürümü (4.85.0/4.86.0) ile aradaki ~50 sürümlük fark bunu doğruladı. `brew install --cask docker` ile güncellendi (bir ara cask'ın `postflight_steps` hatası + TTY'siz `sudo` isteği `/Applications/Docker.app`'ı sildi; önbellekteki DMG `hdiutil attach` + `cp -Rp` ile elle geri yüklendi — geçici bir olay, kalıcı iz bırakmadı). Güncelleme sonrası `docker run --rm --platform linux/amd64 busybox uname -m` `x86_64` döndü; `AgentPrism.SqlServer.IntegrationTests` `SqlServerFixture.cs` HİÇ değiştirilmeden (K-317'nin `azure-sql-edge` ikamesine hiç gerek kalmadan) gerçek `mcr.microsoft.com/mssql/server:2022-latest` ile **479/479** yeşil koştu. Ayrıntı: [`docs/hafiza/sql-server-yerel-test.md`](hafiza/sql-server-yerel-test.md).

### K-352 — F-76 (OpenAPI 500) YALNIZ `ProjectReference` tüketicisini etkiler; düzeltme kütüphanede değil, örnek uygulamanın derlemesindedir (2026-08-08 denetimi)

Rapor "`AddOpenApi()` kullanan HER çok-sağlayıcılı tüketici için `/openapi/v1.json` kırık" diyordu; **kapsam ölçülüp daraltıldı.** Kök neden doğrulandı: `AgentPrism.Sql.Shared` üç derlemeye `LinkBase` ile bağlandığı için (K-185) üç XML doküman dosyası **aynı** `<member>` kimliklerini taşıyor — SqlServer ile Sqlite arasında **620 ortak kimlik** ölçüldü, ilki `T:AgentPrism.MigrationRunner` (paylaşılan 43 tipin 42'si `internal`, yalnız `MigrationRunner` public). `Microsoft.AspNetCore.OpenApi` 10.0.10'un `GenerateAdditionalXmlFilesForOpenApi` hedefi (`build/Microsoft.AspNetCore.OpenApi.targets`) `AdditionalFiles`'a **yalnız** `'%(ReferencePath.ReferenceSourceTarget)' == 'ProjectReference'` koşulunu sağlayan derlemelerin `.xml`'ini ekler. Örnekte ölçüldü: **15 `ProjectReference`, 368 `ResolveAssemblyReference`** — NuGet paketiyle gelen derlemelerin XML'i hiç eklenmez. Uçtan uca doğrulandı: `dotnet pack` ile üretilen paketleri `PackageReference` ile tüketen izole bir uygulama (AgentPrism + SqlServer + Sqlite + `AddOpenApi()`) **HTTP 200** ve 106 yol döndürdü; aynı kurulum `ProjectReference` ile **500** veriyordu. Çözüm K-185'i yeniden açmadı: `samples/AgentPrism.Api.csproj`'a `AgentPrismRemoveDuplicateSqlXmlDocs` hedefi eklendi (`AfterTargets="GenerateAdditionalXmlFilesForOpenApi"`, SqlServer ve Sqlite XML'ini `AdditionalFiles`'tan çıkarır; PostgreSql kanonik kalır). SQL sağlayıcı tipleri hiçbir HTTP şemasında görünmediği için belgeden hiçbir şey kaybolmaz — örnek 500'den **200/110 yol**'a geçti. `OpenApiSharedSqlXmlDocTests` hedefin silinmesini yakalar.

### K-401 — `WorkflowRunner.ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException` sarmalayıcılarını soyar; gerçek neden `RunError.Message`'a yazılır (HATA-K-003, Kritik, manuel kabul testi Ortak Kuyruk)

`MT-WF-071`/`073` ölçtü: bir Magentic + `requirePlanApproval` iş akışında `maxIterations` plan+onay-sonrası-devam+katılımcı döngüsü için yetersiz kalınca (case'in kendi `maxIterations: 2` değeri), MAF'ın Magentic orkestratörü round-limit'e ulaşıp KENDİ `WorkflowOutput`'unu ("Task execution stopped due to hitting the maximum round count limit.") ürettikten SONRA çalıştırıcı bir sonraki süper-adımda orkestratörü BİR KEZ DAHA çağırıyor — MAF'ın kendi kodu (`MagenticOrchestrator`) bunu "orkestrasyon zaten sonlandı" istisnasıyla reddediyor, bu istisna MAF'ın handler-çağırma katmanınca `TargetInvocationException` ile sarmalanıyor. `WorkflowRunner.ToRunError` bu sarmalayıcının YALNIZ kendi (anlamsız) `.Message`'ını ("Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken") yazıyordu, gerçek neden `InnerException`'da kilitli kalıyordu — operatör asıl arızayı hiç göremiyordu. **Tam gerekçe:** [`arsiv/KARARLAR-GECMISI.md`](arsiv/KARARLAR-GECMISI.md) — K-401.

### K-317 — `azure-sql-edge` artık güvenilir bir yerel doğrulama ikamesidir; hazır-olma denetimi `sqlcmd` yerine ADO.NET ile yazılmalıdır (kullanıcı kararı)

K-186'nın (Faz 23) tek seferlik başarısı sonrası Faz 25'te "artık HER ZAMAN çalışmayabilir" diye kayda geçmişti (`docs/hafiza/sql-saglayicilari.md`); kök sebep hiç teşhis edilmemişti. Bu oturumda teşhis edildi: `Testcontainers.MsSql` 4.13.0'ın varsayılan hazır-olma denetimi container İÇİNE `sqlcmd` ile girer (`MsSqlContainer.GetSqlCmdFilePathAsync`), `azure-sql-edge` imajı bu ikiliyi TAŞIMAZ — container çalışır ama fixture hiç "hazır" görmez. Test projesinin kendisi `ExecScriptAsync`/`sqlcmd` kullanmıyor (yalnız ADO.NET), bu yüzden `MsSqlBuilder.WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(...))` ile gerçek bir `Microsoft.Data.SqlClient` bağlantısı deneyen özel bir `IWaitUntil` YETERLİ ve GÜVENİLİR bir ikame. Bu teknikle 431/431 sözleşme testi `azure-sql-edge` üzerinde yeşil koştu ve dört gerçek üretim hatası bulundu (K-318, K-319). Gerçek `mssql/server` bu makinede hâlâ koşamıyor: Docker Desktop 4.29.0, `useVirtualizationFrameworkRosetta: true` ayarına RAĞMEN (ayar dosyasında açık, VM içinde "Rosetta for Linux registered" logu da var) container içi amd64 çalıştırmada "Rosetta is only intended to run on Apple Silicon with a macOS host using Virtualization.framework with Rosetta mode enabled" hatasıyla `exit 133` düşüyor — macOS 26.5.1 ile bu Docker Desktop sürümü arasında bir uyumsuzluk izlenimi veriyor, Docker Desktop tam yeniden başlatıldıktan sonra bile. `SqlServerFixture` gerçek `mssql/server`'a GERİ ALINDI (CI/gerçek doğrulama hedefi değişmedi); bu karar yalnız YEREL doğrulama tekniğini belgeler.

### K-320 — Model çağrı boru hattının TAMAMINI `ModelProviderRegistry` kurar; `IModelProvider` HAM istemci döndürür (kullanıcı kararı)

Faz 48'in guard'ı tool sonuçlarını görmek zorundaydı ve ölçüm planın konum kararını çürüttü: `grep -rn "UseFunctionInvocation" src/` dört sağlayıcı fabrikasının (OpenAI:78, Anthropic:129, Google:122, Azure:112) ve `AgentPrism.Testing/FakeModelProvider:221`'in tool çağrı döngüsünü **kendi içinde** kurduğunu gösterdi. Sonuç: defterin sardığı HER halka (devre kesici, ek çözme, içerik filtresi tespiti) döngünün DIŞINDA kalıyordu ve agent turu başına yalnız BİR model çağrısı görüyordu. Bir tool sonucu modele İKİNCİ çağrıda girer; prompt injection'ın en yaygın yolu o çağrıdır ve döngü dışındaki bir halka onu yapısal olarak göremez. Boru hattı defterin içine taşındı; bugünkü sıra (dıştan içe): içerik filtresi tespiti → devre kesici → ek çözme → `FunctionInvokingChatClient` → OpenTelemetry → içerik guard'ı → sağlayıcının ham istemcisi. Üç kazanç: guard her gerçek model çağrısını görür, engellenen istek ağa hiç çıkmaz, ve üçüncü taraf bir `IModelProvider` bütün halkaları bedava devralır (eski düzende kendi boru hattını kuran bir sağlayıcı guard'ı SESSİZCE almazdı — bir güvenlik kontrolü için kabul edilemez arıza biçimi). Devre kesici ve ek çözme bilerek döngünün DIŞINDA bırakıldı: birincisi içeri alınsaydı sayım granülerliği agent turundan gerçek ağ çağrısına kayar ve `FailureThreshold`'un anlamı değişirdi, ikincisi içeri alınsaydı beş turlu bir tool döngüsü aynı eki beş kez okurdu. Dört sağlayıcı testinin boru hattı iddiası defter düzeyine taşındı.

### K-393 — `A2AApprovalGuardFilter`/`McpApprovalGuardFilter`: şema hazır değilken (`AutoApplyMigrations=false`) katalog sorgusu HEMEN uygulamayı durdurmak yerine sınırsız sayıda, üstel gecikmeli (5 sn'de tavanlanan) yeniden dener; deneme SAYISI değil yalnız uygulama kapanışı sınırlar (HATA-S1-002, manuel kabul testi S1)

`MT-SQL-004`/`014` ölçtü: `AutoApplyMigrations=false` ve şema henüz kurulmamışken uygulama `Now listening` yazıp HEMEN ardından kendini kapatıyordu — `A2AApprovalGuardFilter`/`McpApprovalGuardFilter` (uc bağlama anında BAŞLAYAN arka plan `Task`'lar) `SchemaReadyGate.MarkReady()`'nin (bu bayrak kapalıyken hiçbir doğrulama yapmadan hemen çağrıldığı) ardından `catalog.ListAsync()`'i sorguluyor, "no such table" alıyor, `LogCritical` + `StopApplication()` çağırıyordu — A2A/MCP'ye HİÇ dokunulmasa bile TÜM uygulama (agent CRUD, `/health`, her şey) düşüyordu. `MT-SQL-004`'ün kendi sözleşmesi ("uygulama açılır, `/health` `Unhealthy` döner") bunu doğrudan çürütüyordu. Düzeltme `ExternalSurfaceGuard.ListCatalogWithRetryAsync` ekler: yalnız `catalog.ListAsync()` çağrısını sarar (asıl güvenlik denetimi `EnsureNoApprovalRequiredTools`'un kendi `catch`'i hâlâ HEMEN başarısız olur — gerçek bir onay-gerektiren-tool ihlali retry'a GİRMEZ), deneme SAYISINA değil yalnız `lifetime.ApplicationStopping`'e bağlıdır — şema hiç hazırlanmazsa yalnız A2A/MCP isteklerine gelen `_checkTask` süresiz bekler, uygulamanın geri kalanı hemen kullanılabilir kalır, `StopApplication()` bu yoldan ARTIK HİÇ ÇAĞRILMAZ. Dört doğrulama kapısı yeşil (bkz. commit).

### K-381 — Paylaşılan `AgentFileStore` (dosya belleği/metin araması) her kiracı için `TenantPrefixingAgentFileStore` ile sarmalanır; kiracı sınırı MAF'ın kendi deposuna dokunmadan bir vekil (proxy) katmanında uygulanır (manuel kabul testi hazırlığı)

`AgentDefinitionCompiler`'ın `TextSearchProvider` arama callback'i (`Func<string, CancellationToken, ...>`) hangi kiracının aradığını bilmez ve derleme anında KOK "/" dizininden yinelemeli arama kurar; depo (`InMemoryAgentFileStore`, `TryAddSingleton`) tüm kiracılar arasında PAYLAŞILIR. Callback'i kiracı-farkında yapmanın tek yolu MAF'ın kapalı-kutu `FileMemoryProvider`/`TextSearchProvider` API'sini değiştirmekti (mümkün değil, üçüncü taraf paket) — bunun yerine `RequireFileStore` artık paylaşılan depoyu sarmalayan hafif bir vekil döner: her yol `/{tenantId}/...` onekiyle içeri yönlendirilir, dışarıya dönen yol/ad alanlarından da aynı önek soyulur (çağıran taraf kendi özel kök dizininde çalışıyormuş GİBİ görür). Hem yazma (`FileMemoryProvider`) hem okuma (`TextSearchProvider`) `RequireFileStore` üzerinden geçtiği için ikisi de otomatik olarak kiracıya göre yalıtılır. `InMemoryAgentFileStore` yolların GÖRECELİ olmasını ve `/` ile BAŞLAMAMASINI şart koşar (`NormalizeRelativePath`) — vekil bunu da gözetir. **Kapsam dışı bırakılan (bilinerek):** aynı kiracı içindeki ajan/oturum sınırı — `TextSearchProvider`'ın callback'i çalışma ANINDA hangi oturumun aradığını hâlâ bilmiyor (yalnız derleme anındaki kiracı kimliği yakalanabildi); bu, `docs/ADAYLAR.md` F-105 adayına bırakıldı.

### K-355 — Çalıştırmanın alt yazmaları BEKLENEN kiracıyı taşır; ambient kiracı KULLANILMAZ ve alan HTTP sözleşmesine girmez (2026-08-08 denetimi)

K-280'in kapattığı boşluk: `IRunStore.AppendEventAsync`, `CompleteRunAsync`, `UpdateRunCostAsync`, `RecordToolInvocationAsync` hiçbir kiracı süzgeci taşımıyordu. Ambient kiracıyla süzme daha önce **denenmiş ve geri alınmıştı** çünkü `RunStartInfo.TenantId` ambient kiracıyı bilerek ezer (workflow ve iş kuyruğu böyle çalışır) ve süzgeç meşru yazmaları düşürüyordu. Çözüm ambient değil, çağrının taşıdığı **beklenen** kiracıdır: `RunEvent`/`RunCompletion`/`ToolInvocationRecord`'a birer `TenantId` alanı ve `UpdateRunCostAsync`'e bir `string? tenantId` parametresi. Değer `RunEventWriter.StartAsync` içinde `RunStartInfo.TenantId`'den alınır — yani çalıştırmanın **kendi** kiracısıdır. `null` ise denetim yapılmaz (geriye dönük uyumlu, meşru yazma düşmez). SQL tarafında UPDATE'ler `AND (@tenant_id IS NULL OR tenant_id = @tenant_id)` ile, INSERT'ler `VALUES` yerine `SELECT ... WHERE EXISTS (SELECT 1 FROM runs r WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id))` ile korunur; sıcak yazma yolunun ek maliyeti bir birincil anahtar aramasıdır. 🚨 Alan **yalnız yazma tarafındadır** — bir sütuna yazılmaz, yalnız `WHERE` muhafızıdır; geri okunduğunda her zaman `null` olurdu, bu yüzden `[JsonIgnore]` ile aktarım sözleşmesinden çıkarıldı ve `docs/openapi/agentprism.json` **değişmedi**. Dört metot `[TenantAgnostic]` muafiyetinden çıkıp `TenantCoverageTests` kapsamına girdi; `RunStoreContract`'a dört **iki yönlü** test eklendi (yanlış kiracı düşer, doğru kiracı geçer) ve üç lehçede de yeşil koştu (SQLite 453, PostgreSQL 870, SQL Server 439 — sonuncusu `azure-sql-edge` ile, K-317).

### K-396 — `AgentDefinitionCompiler.SearchFileStoreAsync`: `TextSearchProvider`'ın doğal dil sorgusu artık `AgentFileStore.SearchAsync`'e ham regex olarak DEĞİL, boşluğa göre ayrılmış ≥3 karakterlik tokenlerin kaçışlanıp "VEYA" ile birleştirildiği bir desen olarak geçiyor (HATA-S1-009, manuel kabul testi S1)

`MT-MEM-013`/`014` `EnableTextSearch: true` bir agent'ın dosya belleğinde HARFİYEN mevcut bir dizgiyi ("FILE-7841") iki doğal dil sorgusuyla da BULAMADIĞINI ölçtü. Kök neden kodda doğrulandı: `SearchFileStoreAsync` `fileStore.SearchAsync("/", regexPattern: query, ...)` çağırıyordu — parametre adı `regexPattern`, `AgentFileStore.SearchAsync` bunu GERÇEKTEN bir `.NET Regex` (`new Regex(regexPattern, ...)`, PostgreSQL'de `~`) olarak çalıştırıyor, ama `TextSearchProvider` modele DOĞAL DİL sorgusu yazdırıyor ("FILE-7841 ile ilgili bir kayit var mi?" gibi). Ham cümleyi regex olarak çalıştırmak (boşluklar/noktalama regex'te dar kısıtlardır) neredeyse HİÇBİR ZAMAN eşleşmiyordu — bu, raporun "şüpheli davranış, kod seviyesinde doğrulanmadı" notunun ötesinde, kesin doğrulanmış bir kusurdu. Düzeltme sorguyu boşluğa göre (kelime İÇİ tire/rakam BOZULMADAN — "FILE-7841" tek token kalır) tokenlere ayırır, 3 karakterden kısa (çoğunlukla doldurucu: "bir", "ile", "mi") tokenleri eler, kalanları `Regex.Escape` ile kaçışlayıp `|` ile birleştirir; sıfır token kalırsa (sorgu tamamen kısa kelimelerden oluşuyorsa) ham sorgu kaçışlanıp AYNEN kullanılır — davranış hiçbir zaman bugünkünden kötü olmaz.

### K-398 — `RunRecordingAgent.RunCoreStreamingAsync`: terminal durum artık tüketicinin ERKEN `DisposeAsync()`'i (dogal bitiş değil, istisna da değil) durumunda da yazılıyor — `Canceled`, o ana kadar biriken kısmi `usage` ile (HATA-S1-015, Yüksek, manuel kabul testi S1)

`MT-MM-078` ölçtü: gerçek zamanlı bir ses turunda kullanıcı `cancel` gönderdiğinde WebSocket katmanı doğru davranıyordu ama altındaki `runs` satırı KALICI OLARAK `Running`'de asılı kalıyordu (`completedAt`/`usage`/`cost` hep `null`) — gerçek OpenAI+ElevenLabs maliyeti oluşup hiç kaydedilmiyordu (sessiz veri kaybı). Kök neden C#'ın async-iterator disposal kuralı: `CompleteAsync`'i çağıran kod `try/finally`'nin (yalnız `enumerator.DisposeAsync()` içeren) ALTINDA duruyordu; tüketici (`VoiceConversationDriver.RespondAsync`) bir `yield return`'den SONRA (TTS ağ çağrısı sürerken) erken `DisposeAsync()` çağırınca yalnız `finally` çalışıyor, ALTINDAKİ kod (terminal durumu yazan `CompleteAsync`) HİÇ ÇALIŞMIYORDU — disposal metodun geri kalanını normal akışla sürdürmez, yalnız askıdaki `finally` bloklarını çalıştırır. Düzeltme iki bayrak (`naturalEnd`, `completedByCatch`) ekler: `finally` içinde, ne doğal bitiş ne bir iç `catch` zaten tamamladıysa (yani tam bu erken-disposal senaryosu), `CompleteAsync(scope, RunStatus.Canceled, ToRunUsage(usage), null, ...)` BURADA çağrılır — `usage` o ana kadar akıştan toplanmış kısmi kullanımdır, sıfır değil (gerçek maliyet artık kaydedilir). Kök neden ses'e özgü değildir: `yield return`'den SONRA, bir sonraki `MoveNextAsync`'ten ÖNCE tüketici tarafında oluşan HERHANGİ bir erken disposal aynı sessiz kaybı üretirdi; bu yalnız onu ilk kez ölçülebilir kılan somut yoldu.

### K-397 — `ApiKeyScope`'a `KnowledgeRead`/`KnowledgeAdmin` eklendi; `KnowledgeEndpoints`'in tüm uçları artık `RequireApiKeyScope` çağırıyor (HATA-S1-011, Yüksek, manuel kabul testi S1)

`MT-MEM-031` ölçtü: `scopes:["RunsRead"]` ile üretilen salt-okunur bir API anahtarı `DELETE /api/knowledge/{collection}/documents/{sourceId}`'i `204` ile çalıştırdı — `KnowledgeEndpoints`'in HİÇBİR ucu `RequireApiKeyScope` çağırmıyordu (kontrol grubu `AgentEndpoints`'te `403` veriyordu, yani kapsam sistemi genel olarak ÇALIŞIYOR, yalnız burada hiç bağlanmamış). `ApiKeyScope` KAPALI bir liste olarak tasarlanmıştır (yetki dili bir güvenlik yüzeyidir) ama kendi XML belgesi yeni üye eklemenin kırıcı OLMADIĞINI açıkça yazar — bu yüzden yeni bir kapsam TÜRÜ (`AgentsRead`/`AgentsAdmin` ile aynı ad deseni) eklemek, mevcut bir kapsamı (ör. `RunsRead`) yeniden kullanıp anlamını GENİŞLETMEKTEN daha doğru bir çözümdür (bir "yalnız run okuma" anahtarının bilgi tabanını da okuyabilmesi ayrı, istenmeyen bir yetki genişlemesi olurdu). Yükleme/silme `KnowledgeAdmin`, listeleme/arama `KnowledgeRead` gerektirir. Raporun kendi notu bunu "beşinci bilinen tekrar" (`WorkflowEndpoints`/`SchedulingEndpoints`/eval-experiment yüzeyi/`GovernanceEndpoints`'in ardından) olarak işaretliyor — bu karar yalnız `KnowledgeEndpoints`'i (test edilip belgelenen tek yüzey) kapsar, diğerleri kapsam DIŞI bırakıldı.

### K-450 — `FallbackRetryClassifier.IsRetryable` istisnanın TAMAMINI (`InnerException` zinciri + her `AggregateException` kolu) gezer; yalnız en dıştaki istisnaya bakmaz

`faz-denetim`'in bağımsız denetçisi ölçtü: gerçek `OpenAI` 2.12.0 istemcisini dinlemeyen bir porta bağlamak `AggregateException` ("Retry failed after 4 tries...") fırlatıyor; onun `InnerException`'ı `System.ClientModel.ClientResultException` ("Connection refused (...)", HTTP durumu YOK — hiç yanıt alınmadı), onun da `HttpRequestException`, onun da `SocketException`. İlk uygulama yalnız EN DIŞTAKİ istisnayı denetliyordu — ne mesaj ne tip eşleşince "retryable değil" dönüyordu, bu yüzden canlı bir kesintide devre kesici açılana kadarki İLK `FailureThreshold` isteğin HEPSİ yedeğe hiç düşmeden çıplak hata olarak kullanıcıya dönüyordu; F-44'ün "kullanılabilirlik" amacını tam boşa çıkarırdı. K-296'nın "resmi SDK'lar tahmin ettiğin tipi fırlatmaz, ölç" dersi bir katman daha derine uygulandı. Düzeltme: `Flatten(exception)` (kendisi + `InnerException` zinciri + `AggregateException.InnerExceptions`, özyinelemeli) her adımda aynı kural setiyle (iptal → kimlik doğrulama → HTTP durumu → tip adı) taranır; `TransportExceptionTypePattern`'e `clientresultexception`/`requestfailedexception`/`apiexception`/`aggregateexception` de eklendi — güvenlidir çünkü bu desen yalnız HİÇBİR karede HTTP durum metni bulunamadığında (yani gerçekten bağlantı düzeyinde bir arıza olduğunda) son çare olarak çalışır. Canlı SDK'ya karşı doğrulandı (yamalı sınıflandırıcı gerçek "connection refused" zincirinde `true` döndü) + iki regresyon testi eklendi.

### K-323 — Yeni bir genişleme noktasının "varsayılan kapalı" kapısı KAYITTIR, bir `Enabled` bayrağı değildir (kullanıcı kararı)

Faz 48'in planı iki şeyi birlikte istiyordu: yerleşik guard `TryAddEnumerable` ile HER ZAMAN kaydedilsin, ve hiç guard kayıtlı değilken maliyet TAM SIFIR olsun. İkisi çelişir: yerleşik guard her zaman kayıtlıysa `IEnumerable<IContentGuard>` asla boş olmaz ve sıfır maliyet ölçülemez; `IContentGuard`'a bir `IsActive` üyesi eklemek de genişleme noktasını bir bayrak taşımaya zorlardı. `AddAgentPrism()` artık hiçbir guard kaydetmez ve `PatternContentGuardOptions` bir `Enabled` alanı TAŞIMAZ; iki açma yolu da açık tercihtir (`builder.AddPatternContentGuard(...)` veya `AgentPrism:ContentGuard:Pattern` bölümünü doldurmak). Ölçüldü (`GC.GetAllocatedBytesForCurrentThread`, 2 000 çağrı): guard kayıtlı değilken 736 B/çağrı — sarmalayıcı boru hattına hiç eklenmez ve bir `if` bile çalışmaz; kayıtlı ama kuralsız 952 B, kural var/eşleşme yok 1 736 B, maskeleme 2 976 B. Bu, K1'in Faz 43 (`Idempotency-Key`) ve Faz 46 (`Prefer: respond-async`) yorumundan bilinçli olarak FARKLIDIR ve sınır şudur: orada özellik yalnız başlığı GÖNDEREN istemci için devreye girer ve göndermeyen için maliyet gerçekten sıfırdır, bu yüzden kapalı gelmek "korunduğunu sanıp korunmamak" üretirdi. Guard ise HER istekte çalışır ve davranışı DEĞİŞTİRİR (maskelenmiş bir istem sürprizdir), bu yüzden K1 düz uygulanır.

### K-399 — `AgentPrismRetentionOptions`'a `RunInputs`/`VoiceSessions`/`RunScores` (varsayılan sırasıyla 30/30/180 gün) ve `DocumentEmbeddings` (varsayılan KAPALI, `MaxAgeDays=null`) eklendi — dört hedef daha önce `ForTarget`'ta `_ => null` dalına düşüp config varsayılanını SESSİZCE yok sayıyordu (HATA-S1-005, Düşük, manuel kabul testi S1)

`MT-RET-015` ölçtü: `Enabled=true` ve `RunInputs:MaxAgeDays=60` verilmiş hâlde `preview?target=run_inputs` `{"maxAgeDays":null,"enabled":false}` döndürüyordu — `AgentPrismRetentionOptions.ForTarget` 16 hedeften yalnız 12'si için ayar nesnesi döndürüyordu, kalan dördü (`run_inputs`, `voice_sessions`, `run_scores`, `document_embeddings`) hiç bağlı değildi, ne hata ne uyarı vardı. Varsayılan seçimi: `RunInputs`/`VoiceSessions` diğer olay-benzeri hedeflerle (`RunEvents`=30, `Spans`=14) aynı ömür sınıfına konuldu; `RunScores` değerlendirme geçmişiyle aynı sınıfa (`EvalCaseResults`=180) konuldu — üçü de `RetentionTargets.UserDataTargets`'ta YOKTUR (kullanıcı verisi değildir), bu yüzden config varsayılanı DOĞRUDAN devreye girer. `DocumentEmbeddings` ise kasıtlı olarak `Sessions`/`Conversations` ile AYNI temkinli desene (varsayılan KAPALI) konuldu — bilgi tabanı içeriği kullanıcının yüklediği referans veridir, log/olay değildir; otomatik silme yalnız açıkça istenirse açılmalı.

### K-425 — Aday yeteneği üretimi `aday-kesfi` skill'iyle yazılı bir protokole bağlandı; oturum elemeli diyalog olarak koşar ve her bulgu üç kanala ayrışır *(kullanıcı kararı)*

Zincirin başı yazılı değildi: `faz-planlama` "seçilmiş bir adayı" plana çevirir, ama adayın kim tarafından ve hangi disiplinle üretildiği hiçbir yerde durmuyordu. Değerlendirme çerçevesi (dört ölçüt + sekiz mercek, `ADAYLAR.md` § Değerlendirme Ölçütleri) zaten olgundu; skill onu **tekrarlamaz**, referans verir — iki yerde tutmak kayma üretir. Üç kanal kuralı (yeni yetenek → F-NN · kusur → `kusur-giderme` · ekosistemin geçersiz kıldığı karar → K-NNN yeniden açma önerisi) repo'nun kendi geçmişinden türetildi: 2026-08-08'de F-76 faza değil kusura yollandı (K-352), 2026-08-14 koşumundaki dokuz bulgudan yalnız F-106, 2026-08-15 koşumundan yalnız F-107 faza döndü. Örüntü: bir bulgu ancak var olmayan bir altyapı istiyorsa ya da MAF'ın kapalı-kutu davranışına bağımlıysa adaydır; geri kalanı kusurdur. Skill **yalnız kullanıcı istediğinde** koşar; faz kapanışına bağlanmadı, çünkü her faza bir tarama eklemek her fazı pahalılaştırır. Ekosistem taraması zorunludur fakat kaynak listesi yalnız öneridir — ölçülmemiş bir tarama maliyeti sabitlenmedi. Karşı görüş satırı her kalemde zorunludur, ama sayısal ret kotası **konmadı**: kota uydurma muhalefet üretir, gerçek gerekçe yoksa satır "ciddi bir karşı gerekçe bulunamadı" yazar.

## Faz 77 bütçe rahatlatması — tam gerekçeler

### K-522

**K-522 — Tüketici doküman standardı faz dokümanından ayrıştırılıp `tuketici-dokuman-senkronu` skill'ine taşındı (kullanıcı kararı)**

Faz 73–76 gerçek bir tüketici doküman makinesi kurdu: dokuz makine iddiası (`check-content.mjs`), sevk edilen metnin kendi kendine yetmesi kuralı, sayfa sözleşmesi, diyagram kuralı, erişilebilirlik ve ağırlık kapıları, ve tüketicinin diskinde üretilen yerel referans yüzeyi. Standardın tamamı **birikimli faz dokümanlarında** yaşıyordu (`74-*.md`, `75-*.md`, `76-*.md`) ve okuma protokolü o dosyaları okumaz — yani standart yalnız onu yazan oturumda geçerliydi. Ölçüldü: `check:content`, `check:weight`, `npm run check`, `build-agent-map`, `AgentMap`, `capabilities.md`, `LocalReference`, `CapabilityExample`, `ShippedDocumentation`, `Read next` ve muafiyet listesi terimlerinin **hiçbiri** `.agents/skills/` altında geçmiyordu. Somut sonucu bir kusurdu: `faz-tamamlama` Adım 7'nin site kapısı `npm run build && check-links.mjs` idi ve Faz 75 ile 76'nın inşa ettiği iki kapıyı (`check:content`, `check:weight`) hiç koşmuyordu. Ayrı bir skill seçildi, Adım 7'nin içine gömmek değil: `faz-tamamlama` zaten en büyük skill'dir (13 KB) ve faz hiçbir tüketici yüzeyine dokunmadığında standardın okunması bütçe israfıdır — `faz-denetim`'in Adım 4'ten çağrılmasıyla aynı desen. Aynı geçişte üç bağlanma noktası kuruldu (`faz-tamamlama` Adım 7 · `faz-denetim` §3.8 muafiyet cırcırı · `faz-planlama`) ve plan başlığındaki `Site etkisi` satırı **`Tüketici yüzeyi`** oldu; eski ad yalnız `docs-site/` eksenini istiyordu, sevk edilen yapıt ekseni (XML `<example>`, paket `README.md`'si, `capabilities.md` satırı) hiç sorulmuyordu. Ad değişimi ölçülerek güvenli bulundu: `Site etkisi` yalnız o iki skill dosyasında geçiyordu, hiçbir script onu ayrıştırmıyor.

### K-475

**K-475 — Migration ledger'ın `set_name`/`(set_name, id)` şema yükseltmesi numaralı bir migration DOSYASI değil, `SqlDialect.UpgradeMigrationsTableAsync` bootstrap adımıdır (Faz 67, plandan sapma)**

Plan (67.2, açık soru 2) yükseltmenin normal bir çekirdek migration dosyasıyla gelmesini öngörüyordu. Uygulama anında ölçüldü: bu migration KENDİ İZLEDİĞİ TABLOYU (`__migrations`) değiştiriyor, ve her migration'ın SQL'i `InsertMigration`'ın SABİT metniyle TEK bir toplu komutta (K-388) birleştirilir. SQL Server bir toplu işi BAŞTAN SONA derler; `set_name` sütununu SIRADAN bir `ALTER TABLE ... ADD` ile ekleyip AYNI toplu işte `InsertMigration`'ın (artık `set_name`'e referans veren) metnini çalıştırmak "Invalid column name" ile PATLAR — `0018_audit_chain.sql`'in zaten belgelediği tuzağın ta kendisi, ama bu kez KAÇINILAMAZ: `InsertMigration` HER migration'a sabit eklenir, dinamik SQL (`EXEC`) ile sarılamaz. Çözüm: yükseltme `CreateMigrationsTable` gibi kendi AYRI komutuyla, migration döngüsü BAŞLAMADAN ÖNCE çalışır — `InsertMigration` derlendiğinde sütun zaten katalogda vardır. PostgreSQL/SQL Server `SqlQueriesBase.UpgradeMigrationsTable` (tek deyimlik, `IF NOT EXISTS` korumalı) kullanır; SQLite birincil anahtarı yerinde DEĞİŞTİREMEDİĞİ için (K-278) kontrolü ve yeniden kurmayı kodda yapar. Gerçek PostgreSQL/SQL Server/SQLite konteynerlerine karşı ölçüldü: `OptionalMigrationSetTests.Pre_phase_67_ledger_shape_upgrades_without_data_loss` (üç sağlayıcıda da) eski şekilli bir deftere karşı sıfır veri kaybıyla geçti.

### K-348

**K-348 — Kaynak üreteci ayrı bir NuGet paketi değildir; `AgentPrism.Core` nupkg'sinde `analyzers/dotnet/cs/` altında taşınır (Faz 52)**

Üç seçenek tartıldı (52.4): ayrı paket tüketiciye ikinci bir `PackageReference` yükler ve unutulursa sessizce hiçbir şey üretilmez; `AgentPrism.Abstractions` içine koymak `netstandard2.0` (üreteç) ile `net8/9/10` (Abstractions) hedef çakışması yaratır. Seçilen: ayrı proje (`AgentPrism.Generators`, `IsPackable=false`), `Core.csproj`'a `ProjectReference` `PrivateAssets=all` + `ReferenceOutputAssembly=false` + `OutputItemType=Analyzer` ile bağlanır. 🚨 **Basit `BeforeTargets="_GetPackageFiles"` YETMEDİ** — Core üç TFM hedeflediği (net8/9/10) için `@(Analyzer)` yalnız İÇ (TFM'e özgü) derlemede doludur, dıştaki çapraz-hedefleme derlemesinde BOŞTUR (ölçüldü: boş `DEBUG` çıktısı). Resmi çözüm `TargetsForTfmSpecificContentInPackage` + `TfmSpecificPackageFile`; bu hedef HER iç derleme için tekrar çalıştığından ve DLL TFM'den bağımsız olduğundan üç kez eklemek `NU5118` verdi ("dosya zaten var") — hedef `Condition="'$(TargetFramework)' == 'net10.0'"` ile tek TFM'e sabitlendi. Doğrulama: `dotnet pack src/AgentPrism.Core -o /tmp/apk` sonrası `unzip -l` çıktısında `analyzers/dotnet/cs/AgentPrism.Generators.dll` (52736 bayt) görüldü; `.nuspec`'te `Microsoft.CodeAnalysis.CSharp` bağımlılığı YOK; izole bir dış tüketici projesinde (yalnız `PackageReference`, gerçek yerel NuGet feed'i) `dotnet list package --include-transitive` "TEMİZ" döndü VE `AddGeneratedTools()` derlenip çalıştı.

### K-269

**K-269 — `AgentPrism.Testing.FakeModelProvider` modele ozel, BIR KEZ tuketilen bir yanit kuyrugu tutar; mesaj gecmisi taranarak "hangi tool zaten cagrildi" cikarilmaz**

Bes ayri dosyaya kopyalanmis 523 satir sahte saglayici kodu (`EchoModelProvider` ×2 — tek fark ad alani, `diff` ile dogrulandi; `ScriptedModelProvider`; `RoutingModelProvider`; `Fakes/FakeModelProvider`) birlestirildi. Eski Routing/ScriptedModelProvider mesaj gecmisindeki `FunctionCallContent`/`FunctionResultContent` ciftlerini korele ederek "bu tool zaten calisti mi" cikariyordu — bu mantik genellestirilebilir ama testin KENDI durumunu (kuyruk sirasi) saglayicinin DISINDA, cagiranin kontrol edemeyecegi bir yerde (mesaj gecmisi) tutuyordu. Yeni tasarim durumu saglayicinin KENDISINDE (`Queue<FakeStep>`) tutar: kestirilebilir, dogrudan test edilebilir, mesaj gecmisi bicimine bagimli degil. `ForModel(id, cfg => ...)` ayni saglayicinin FARKLI modellerine (ornek: yonlendirici + devrettigi alt agent) BAGIMSIZ kuyruk verir — `AgentDelegationTests`/`UiHost` ile dogrulandi (gercek MAF arka plan gorev tool'lariyla, 3 farkli test katmaninda: FunctionalTests, Playwright E2E, depo disi bir tuketici). `EchoesLastToolResult(prefix)` fallback'i, nihai yanitin GERCEKTEN son tool sonucuna bagli olmasi gereken senaryolar (derinlik siniri hatasi gibi) icin eklendi — planin taslak imzalarinda yoktu.

### K-471

**K-471 — `CompiledAgentCache`, kiracı credential'ı gömülü bir `AIAgent`'ı credential'dan HABERSİZ bir anahtarla asla saklamaz; bağlama varken üç kaynak (`DefinitionStoreAgentSource`, `CodeAgentSource`) önbelleği TAMAMEN atlar (Faz 65, bağımsız denetim 🔴 #2)**

`faz-denetim`'in bağımsız denetçisi ölçtü: önbellek anahtarı (`tenantId`, `name`, `version`, bağımlılık parmak izi) kiracının provider bağlaması ROTATE edilse veya SİLİNSE bile değişmiyordu — önbellekteki `AIAgent`'ın içine gömülü eski kimlik bilgisi sonsuza dek kullanılmaya devam ederdi, sessizce. Alternatifler değerlendirildi: (A) credential'ı önbellek anahtarına eklemek — `ApiKey` değerini bellekte bir anahtar parçası olarak taşımak K-059'un ruhuna aykırı; (B) TAMAMEN ATLA — seçilen. `IModelProviderRegistry.HasTenantProviderOverrideAsync` eklendi (primary + her fallback için bağlama var mı), `AgentDefinitionCompiler.UsesTenantProviderOverrideAsync` deleger eder; bağlama VARSA `CompiledAgentCache.GetOrAddAsync` hiç çağrılmaz, her `run` kendi `CompileAsync`'ini çalıştırır (kiracı-özel ajanlar için önbellek kazancından bilerek vazgeçildi — doğruluk performanstan önce gelir). Kanıt: 3 izole birim testi + uçtan uca `DefinitionStoreAgentSourceTenantCredentialTests` (gerçek `DefinitionStoreAgentSource → AgentDefinitionCompiler → CompiledAgentCache` zincirinde: bağlama eklenince önbellek büyümeden yeni credential kullanılıyor, bağlama silinince global credential'a dönüp yeniden önbelleklenebiliyor).

### K-354

**K-354 — SQL'e dokunan arka plan servisleri `SchemaReadyGate`'i bekler; hosted service kayıt sırası ZORLANMAZ (2026-08-08 denetimi)**

Faz 42'de ölçülen kusur: `MigrationHostedService.StartAsync` migration'ları TAM bekler ama `BackgroundService` taban sınıfının `StartAsync`'i `ExecuteAsync`'i **beklemeden döner**. `IHostedService`'ler kayıt sırasında başlatıldığı için zincirde `.UseMcp()` `.UseSqlite()`'tan önce çağrılırsa `McpDiscoveryService`'in ilk SQL denemesi migration bitmeden koşar ("no such table"); kendiliğinden düzelir ama gözlenebilir bir hata üretir ve ilk açılış sağlık kontrollerinde yanlış alarm verir. İki çözüm vardı: kayıt sırasını zorlamak veya bir sinyal. **Sıra zorlamak reddedildi** — zinciri tüketici yazar ve her sıralamayı dayatamayız; kırılgan olurdu. `SchemaReadyGate` (Abstractions, `TryAddSingleton` ile Core'da kayıtlı) sıra bağımsızdır: `MigrationHostedService` migration + varsayılan kiracı yazımı bittikten **sonra** `MarkReady()` çağırır; `McpDiscoveryService` ve `JobWorkerBackgroundService` ilk SQL denemesinden **önce** `WaitAsync(stoppingToken)` ile bekler. 🚨 Hiçbir SQL sağlayıcısı kayıtlı değilse (bellek içi depolar) kapı **kendiliğinden** açıktır — `IEnumerable<SqlPersistenceRegistrationMarker>` boşsa `WaitAsync` hemen tamamlanır; aksi hâlde bellek içi kurulumda arka plan servisleri sonsuza dek beklerdi. Migration hata verirse kapı kapalı kalır, barındırıcı zaten başlamaz ve bekleyenler `stoppingToken` üzerinden çıkar.

### K-483

**K-483 — Cache fiyatı ÇIKARMALI hesaplanır ve HER maliyet toplamının ÜÇÜNCÜ terimidir; tanımsız oran `Unknown`'a DÜŞÜRMEZ (Faz 68, açık soru 2/3, kullanıcı kararı)**

`tam_fiyatlı_girdi = InputTokens − CachedInputTokens`; toplayıcı bir hesap cache token'ını İKİ KEZ faturalardı. `runs.input_cost` cache'i zaten dışarıda bıraktığı için yalnız `input+output` toplayan her sorgu cache isabetli her `run`'ı EKSİK raporlar — bağımsız denetim bunu **yedi** çalışma anı noktasında buldu (kota, `run.cost` metriği, webhook, workflow kotası, `ModelRunJudge`, `OnlineEvalSummaryService`, arayüz karşılaştırma paneli) ve **maliyet tavanı olan bir kiracı tavanı aşabilirdi**. Çözüm `RunCost.Total()`/`RunTreeCost.Total()`: "bu `run` ne tuttu" sorusunun TEK cevabı. 🚨 Oran TANIMSIZKEN hiçbir çıkarma yapılmaz ve sonuç bu fazdan ÖNCEKİYLE birebir aynıdır; `Unknown` yalnız MODEL fiyatı yokken kullanılır. `0` oranı "bedava" der ve `null`'dan ayrılır; oran tanımlı ama sağlayıcı sayacı bildirmediyse ücret `null`'dır. Akıl yürütme çıkış oranından fiyatlanır (K-032 çizgisi); ses alanları kaydedilir, fiyatlanmaz. Gerçek OpenAI prompt cache isabetiyle ölçüldü: oran tanımlıyken `input=2560, cached=2304 → 6.4e-05 + 5.76e-05`; oran kaldırılınca `inputCost=0.00064`, `cachedInputCost=null`, `source=Configuration`. `InputTokens < CachedInputTokens` VERİ hatasıdır: kırpılmaz, loglanır, tam fiyat uygulanır.

### K-367

**K-367 — `MapAgentPrismMcpServer`/`MapAgentPrismA2A` onay-yüzeyi denetimi, `Map*()` sırasında senkron çalışan bir kontrolden istek-bazlı `IEndpointFilter`e taşındı (kullanıcı kararı)**

Eski kod `Map*()` içinde `catalog.ListAsync().AsTask().GetAwaiter().GetResult()` ile SENKRON okuma yapıyordu; tamamen boş bir veritabanında (migration'lar henüz koşmamışken) bu çağrı tabloyu bulamayıp çöküyordu — `Map*()` `IHostedService`lerden ÖNCE, `MigrationHostedService` şemayı kurmadan önce çalışır. `IHostedService`-tabanlı bir düzeltme değerlendirildi ve REDDEDİLDİ: engelleyici bir `IHostedService`, tüketicinin `.UseMcpServer()`/`.UseA2A()` çağrısını kendi SQL sağlayıcısının `Use*()` çağrısından ÖNCE yaparsa sessizce kilitlenme riski taşır (K-251'in "IServiceCollection kurulum anında sıra-bağımsızdır" ilkesini ihlal eder). Seçilen çözüm: `McpApprovalGuardFilter`/`A2AApprovalGuardFilter` (`IEndpointFilter`), kurucuda hemen (`Map*()` anında, bloklamadan) `_checkTask = RunCheckAsync(...)`'i başlatır; bu task önce `SchemaReadyGate.WaitAsync` ile şema hazır olana kadar bekler, sonra kataloğu okur ve `ExternalSurfaceGuard.EnsureNoApprovalRequiredTools` çalıştırır. Her istek `InvokeAsync`'te bu task'i `await` eder — ilk istekten sona kadar hiçbir istek denetimsiz geçemez. Boş SQLite veritabanında `MapAgentPrismMcpServer`/`MapAgentPrismA2A`'nın artık çökmediği ve onay gerektiren tool'un ilk HTTP isteğinde reddedildiği yeni fonksiyonel testlerle doğrulandı.

### K-466

**K-466 — Kiracı kimlik bilgisi/egress çözümlemesi ASYNC bir ikinci yol olarak eklendi; mevcut SENKRON `AgentDefinitionCompiler.Compile`/`ModelProviderRegistry.CreateChatClient`/`CompiledAgentCache.GetOrAdd` üçlüsü DEĞİŞTİRİLMEDİ (Faz 65)**

Plan `IModelProviderRegistry.CreateChatClient`'in imzasının sabit kaldığını varsayıyordu; uygulama anında ölçüldü (`faz-uygulama` Adım 1): kiracı `store`'ları (`ITenantProviderBindingStore`, `ITenantEgressPolicyStore`) zaten kurulu async desene (`ValueTask`) uyuyor, ama `AgentDefinitionCompiler.Compile` TAMAMEN senkron. İki seçenek vardı: (A) tüm zinciri senkrondan asenkrona zorla taşımak — `RunReplayService`, `AgentDefinitionValidator`, örnekler, yüzlerce test dahil devasa bir kırılma; (B) sync-over-async (`.GetAwaiter().GetResult()`) ile köprülemek — bu depoda hiç kurulu olmayan, bilinen bir anti-desendir ve engelleyici. Seçilen: (C) `CreateChatClientAsync`/`CompileAsync`/`GetOrAddAsync` YENİ, PARALEL async metotlar; sync üçlünün GÖVDESİ ortak bir `BuildAgent`/`BuildPipeline` yardımcısına çıkarılıp yalnız `IChatClient` üretim adımı ayrıştırıldı. Gerçek `run` yolu (`DefinitionStoreAgentSource`/`CodeAgentSource`) ve `AgentDefinitionValidator`/`RunReplayService` async yola taşındı; sync yol (`credential` her zaman `null`) yalnız geriye dönük uyumluluk için kalır ve K1'i korur.

### K-432

**K-432 — Workflow çalıştırması iptal istendiğinde `Completed` yerine `Canceled` yazılır; zorlama süper-adım sınırında VE pompa çıkışında yapılır**

F-107 (`MT-RES-005`/`HATA-S2-010`) süreç içinde yeniden üretildi: `CancelingAgent` adım ortasında `Cancel()` çağırıyor, MAF'ın `AgentWorkflowBuilder.BuildSequential` grafiği token'ı honor **etmiyor** ve — kritik ölçüm — **istisna da atmıyor**: `WatchStreamAsync` akışı **sessizce** bitiriyor, `MoveNextAsync` `false` dönüyor. Bu yüzden `PumpAsync`'in `catch (OperationCanceledException)` dalı hiç çalışmıyordu ve `RunGuardedAsync`'in varsayılan `status = RunStatus.Completed` değeri olduğu gibi kalıyordu; çalıştırma `Completed` + `error: null` olarak kaydediliyordu (test bunu düzeltmeden **önce** kırmızı gösterdi). İki katmanlı zorlama seçildi: (1) her süper-adım sınırında `linked.IsCancellationRequested` denetimi + MAF'ın **kendi** `run.CancelRunAsync()` yolu — grafiği terk etmek yerine çerçeve üzerinden kapatır; (2) pompa çıkışında `status == Completed` ise `Canceled`'a çevirme — sessiz bitişi yakalayan asıl kapı budur. `Failed` ve `AwaitingInput` **korunur**: ilki hatasını, ikincisi meşru duraklamasını kaybetmemelidir. Metin eşleştirme kullanılmadı. Sınıf taraması yapıldı: `= RunStatus.Completed` varsayılanı yalnız `WorkflowRunner`'dadır; `RunRecordingAgent` iptali üç ayrı yolda açıkça yazıyor (K-398 dahil).

### K-318

**K-318 — SQL Server migration'larında `ALTER TABLE ADD` ile eklenen sütunu AYNI toplu işlemde `CREATE INDEX`'te kullanmak "Invalid column name" verir; dört migration dosyası etkiliydi**

`MigrationRunner.ApplyOneAsync` her migration dosyasını GO'suz TEK bir toplu işlem olarak gönderir (`docs/hafiza/sql-saglayicilari.md`da CREATE SCHEMA için zaten bilinen bir kısıt — aynı kök sebep). SQL Server bir toplu işlemi ÇALIŞTIRMADAN ÖNCE TAMAMINI derler; var olan bir tabloya `ALTER TABLE ADD` ile eklenen bir sütun, derleme anında henüz metadataya yansımamıştır, bu yüzden aynı toplu işlemdeki bir `CREATE INDEX` o sütunu ARARSA "Invalid column name" ile başarısız olur (yeni CREATE TABLE + aynı toplu işlem CREATE INDEX'te sorun YOKTUR — deferred name resolution nesne hiç yoksa çalışır, var olan nesnenin şeması değiştiğinde çalışmaz). Bu, K-186/187/188/189'un sözleşme testleri hiç koşmadığı için 47 faz boyunca fark edilmemiş dördüncü tekrarıdır. `0003_tool_usage.sql`, `0009_error_classification.sql`, `0010_eval_case_source.sql`, `0011_replay_and_branching.sql` (iki kez) — hepsinde `CREATE INDEX` ifadesi `EXEC(N'...')` ile sarılarak derlemesi çalışma zamanına ertelendi (CREATE SCHEMA'nın zaten kullandığı desen). K-317'nin `azure-sql-edge` koşusu bunu 431 testin 408'ini kırarak buldu.

### K-414

**K-414 — Manuel test koşum kaydı ile spesifikasyon AYRI dosyalarda yaşar; ikinci koşum `kosumlar/<tarih>/` kardeşi açar, üzerine yazmaz**

`docs/manuel-test/` 2,7 MB ile `docs/`'un yarısıydı ve her case dosyası iki farklı ömürlü şeyi aynı yerde tutuyordu: SPEC (`Ön koşul`/`Adımlar`/`Beklenen sonuç` — yeniden koşulabilir, kalıcı) ve KOŞUM KAYDI (`Gerçek sonuç`/`Durum` — 2026-08-13'e ait, donmuş). İkinci bir koşum bu 2,2 MB'ı ya ezecek ya çatallayacaktı. Dönüşüm önce YAPISAL olarak ölçüldü (1097 case tarandı: 1094'ü tek `Gerçek sonuç` + tek `Durum`, 2'si yeniden koşum nedeniyle çift `Durum`, 1'i bilerek yazılmamış `MT-SKILL-071`), sonra uygulandı: 25 dosya, 1096 kayıt, 735 KB `kosumlar/2026-08-13/` altına çıktı; spec 2,13 MB → 1,39 MB. Veri kaybı olmadığı `HEAD` ile karşılaştırılarak kanıtlandı — ayrım öncesi ve sonrası durum satırı sayıları AYNI (Kaldı 6, Beklemede 1, Atlandı 30, Geçti 1062 satır). Ayrım ayrıca bir ölçüm hatasını ortaya çıkardı: `KAPANIS-PLANI` §12 `Durum:` SATIRLARINI sayıyordu ve yeniden koşulan case'i iki kez sayıyordu; case bazında gerçek dağılım Geçti 1061 · Atlandı 29 · Kaldı 4 · Beklemede 1 · işaretsiz 1'dir. `Kaldı` 4'ü §6'nın kalıcı listesiyle birebir uyuştu, ama "koşulmamış case yok" iddiası YANLIŞ çıktı — `MT-SKILL-057` hiç koşulmamıştı. §12 artık satır değil case sayan bir betik taşır.

### K-410

**K-410 — `SourceLanguageTests` eklendi: taban çizgisi güdümlü, kelime sınırlı Türkçe sözlük + aksan taraması; `ProblemDetailsLanguageTests`in genelleştirilmiş kardeşi**

Faz 57'nin iş hacmi (~17.700 satır, ~1.030 dosya) tek oturumda bitmeyeceği için cırcır kapısı İLK yazıldı — kapı olmadan ilk oturumun temizlediği dosyaya ikinci oturum Türkçe geri yazardı. Desen `tests/AgentPrism.Core.UnitTests/Architecture/SourceLanguageTests.cs`: `src/`, `tests/`, `samples/` içinde `.cs/.sql/.csproj/.props/.targets/.json/.ts/.tsx` dosyalarını diskten okur, kelime sınırlı (`\b...\b`) 140+ kelimelik ASCII-Türkçe sözlük + Türkçe'ye özgü harf (çğıöşüÇĞİÖŞÜ) taraması yapar; `source-language-baseline.txt` dosya başına İZİN VERİLEN ihlal sayısını tutar ve sayı yalnız KÜÇÜLEBİLİR. Faz kapanışında taban çizgisi BOŞ (hedeflenen durum). İki kalıcı istisna `AllowedLinePattern` regex'iyle satır bazında geçirilir: dil seçicinin kendi Türkçe etiketi (`shell.language.tr`) ve E2E lokalizasyon testinin gerçek çevrilmiş metni doğrulayan iddiası (`Gösterge Paneli`, K-228). 🚨 Kelime sınırlı eşleşme camelCase'e YAPIŞIK Türkçe köklerini KAÇIRIR (`SahteTokenKimligi`, `IstenenTokenSayisi` gibi) — bunlar diskten elle tarayarak (`grep -o '[A-Za-z_]*(Kimlik\|Sayisi\|...)...'` deseniyle) ayrıca bulunup düzeltildi; otomatik kapı bu sınıfı hâlâ yakalayamaz.

### K-490

**K-490 — MAF `FunctionInvokingChatClient`'ın onay kısa devresi `AITool.GetService<T>()` pipeline'ı üzerinden çalışır; `ApprovalRequiredAIFunction.InvokeCoreAsync`'i DOĞRUDAN çağırmak defer ETMEZ, gerçek gövdeyi çalıştırır (ÖLÇÜLDÜ, Faz 69)**

`TimeoutAIFunction`/`AuthorizingAIFunction`, `ApprovalRequiredAIFunction`'ı sararken onun kısa devre davranışını KIRMAMASI gerekiyordu; standalone bir birim testi (`ApprovalRequiredAIFunction`'ı sarmalayıcı katmanların İÇİNDE gerçekten `.InvokeAsync` ile çağırmak) 30 saniyelik gerçek gövdeyi çalıştırdı — kısa devre `InvokeCoreAsync`'in içinde DEĞİL. `DelegatingAIFunction`'ın miras alınan (override edilmemiş) `GetService` metodu iç fonksiyona doğru zincirlediği için `((AITool)wrapped).GetService<ApprovalRequiredAIFunction>()` sarmalayıcı katmanlardan sızarak iç örneği buluyor — `IChatClient.GetService`'in kendi boru hattı deseninin (`docs/hafiza/maf-api.md`) `AIFunction` tarafındaki eşleniği. `AuthorizingAIFunction`/`TimeoutAIFunction` bu yüzden `GetService`'i EZMEZ (override etmez); miras alınan varsayılan davranışa bilerek güvenilir. Kanıt: `tests/AgentPrism.Core.UnitTests/Tools/ToolRegistryWrapperOrderTests.cs`, `The_approval_wrapper_stays_discoverable_through_the_outer_layers`.

### K-383

**K-383 — `AgentPrism.Core.csproj`'un üreteç-paketleme hedefi `@(Analyzer)` yerine Generators projesinin `GetTargetPath` çıktısını MSBuild görevi ile okur; `dotnet pack --no-build` artık `analyzers/dotnet/cs/AgentPrism.Generators.dll`'i İÇERİR (manuel kabul testi hazırlığı — daha önce Kritik olarak not düşülmüştü)**

`@(Analyzer)` item grubu yalnız `ResolveReferences` gecişiyle doldurulur ve bu geçiş BUILD'e bağlıdır; `--no-build` onu hiç çalıştırmaz — ölçüldü: `dotnet pack --no-build` sonrası `analyzers/dotnet/cs/` klasörü BOŞ çıkıyordu (`artifacts/package/release/`'deki son dört `AgentPrism.Core` paketinin DÖRDÜNDE de eksikti). Repo'nun kendi dört-kapı doğrulaması `dotnet pack AgentPrism.slnx -c Release --no-build` kullandığı için HER faz kapanışında bu kırık paket üretiliyordu. Düzeltme `@(Analyzer)`'a hiç dokunmaz; bunun yerine `<MSBuild Projects="../AgentPrism.Generators/..." Targets="GetTargetPath">` ile üreteç projesinin KENDİ çıktı yolunu ister — bu hedef build DEĞİLDİR, yalnız zaten var olan çıktının yolunu döner ve dört-kapı sırasında Generators projesi Core'dan ÖNCE normal `dotnet build` ile zaten derlenmiş olduğu için `--no-build` ile de çalışır. `dotnet pack --no-build` sonrası `unzip -l` ile doğrulandı; `AgentPrism.Templates.Tests`'in İzlek A uçtan uca testleriyle de (paketle → yerel feed → `dotnet new` → `dotnet build`, sıfır uyarı) doğrulandı.

### K-304

**K-304 — Kuyruğa alınan (`Prefer: respond-async`) bir çalıştırmanın `runs` satırı, işçinin gerçek yürütme satırıyla AYNI birincil anahtarı paylaşır; `IRunStore.StartRunAsync` bu yüzden bir UPSERT'tir**

Faz 46 planı "runs satırı kuyruğa alma anında yazılır" derken mekanizmayı açık bırakmıştı. `RunStartInfo`'ya varsayılanı `Running` olan bir `Status` alanı eklendi; enqueue anında `Queued` ile yazılan satır, işçi işi gerçekten çalıştırdığında AYNI `RunId` ile ikinci kez `StartRunAsync` çağrısı alır. Düz bir `INSERT` bu ikinci çağrıda birincil anahtar çakışması üretirdi (özellikle `MaxAttempts > 1` yapılandırıldığında). Üç SQL sağlayıcısının `InsertRun` deyimi `ON CONFLICT (id) DO UPDATE` (Postgres/SQLite) ve `UPDATE ... WITH (UPDLOCK, SERIALIZABLE)` + `IF @@ROWCOUNT = 0 INSERT` (SqlServer, `UpsertConversation` ile aynı desen) olarak değiştirildi; `InMemoryRunStore` zaten sözlük ataması olduğu için doğal olarak upsert davranışı gösteriyordu. Alternatif (iş deposunu okuyarak sentetik bir `Queued` yanıtı üretmek) hem `GET /api/runs/{id}` hem `/events` uçlarını iş kuyruğuna bağımlı kılar ve kira dolup iş yeniden başladığında istemcinin `Location`'ının kalıcı olarak anlamsızlaşmasına yol açardı. `RunStoreContract`'a eklenen test (InMemory + Postgres + SQLite'ta doğrulandı; SqlServer bu makinede konteyner çalışmadığı için yalnız gözle incelendi) bu sözleşmeyi korur.

### K-206

**K-206 — İçerik filtresi tespiti `AgentPrism.Core`'da ortak dekoratördür, sağlayıcı paketlerinde değil** *(kullanıcı kararı)**

Faz 26 dokümanı "güvenlik filtresi boş yanıtı hata olarak kaydedilir" diyordu ama nerede yapılacağını söylemiyordu. Sağlayıcı başına yazmak, aynı mantığı iki pakette tekrarlar ve Faz 27 (Azure) üçüncü kez yazardı. Bunun yerine `ContentFilterDetectingChatClient` (internal, `DelegatingChatClient`) `ModelProviderRegistry.CreateChatClient` içinde her istemciye uygulanır — devre kesiciyle **birebir aynı desen** (Faz 8, K-072). Ölçüldü: hem Anthropic hem Google adaptörü güvenlik/refuse durumunu `ChatFinishReason.ContentFilter`'a eşliyor, bu yüzden tek bir kural üçünü de kapsıyor. 🚨 **Sarmalama sırası kritiktir: dekoratör devre kesicinin DIŞINDADIR.** Filtrelenmiş bir yanıt sağlayıcının sağlıklı olduğunu gösterir; içeride olsaydı attığı istisna ardışık hata sayacını artırır ve eşik sayısınca filtrelenen istek sağlayıcıyı kapatırdı (`ContentFilterDetectingChatClientTests.Devre_kesici_filtreyi_hata_saymaz` bunu korur). **Yalnız boş yanıt** hataya çevrilir: model metin veya tool çağrısı üretip sonra kesildiyse kullanıcının elinde kısmi bir cevap vardır ve onu silmek bilgi kaybıdır. Bu, OpenAI'ın bugünkü davranışını da değiştirir — sessizce boş dönen bir filtreli yanıt artık `Failed` kaydedilir; gizlenmemesi gereken bir davranış değişikliğidir.

### K-498

**K-498 — Kontrol noktasından devam sözleşmesi ÖLÇÜLDÜ: EN SON kontrol noktasından sürdürme kod düğümünü YENİDEN ÇAĞIRMAZ, DAHA ERKEN bir kontrol noktasından sürdürme ÇAĞIRIR — `AddWorkflowFunction` işleyicisi bu yüzden İDEMPOTENT olmak ZORUNDADIR (Faz 71, F-116, planın "en riskli hata modu")**

İki bağımsız unit test ile ÖLÇÜLDÜ: `Resuming_an_ALREADY_COMPLETED_mixed_chain_does_NOT_re_run_its_function_node` (açık `checkpointId` verilmeden `/resume` — varsayılan, EN SON kontrol noktasını alır — çağrı sayacı `1`'de kalır: kontrol noktası zaten bitmiş grafı yansıtır, yapılacak bir şey kalmamıştır) ve `Resuming_from_an_EARLIER_checkpoint_RE_RUNS_the_function_node_that_follows_it` (fonksiyonun KENDİ süper-adımından ÖNCEKİ bir `checkpointId` açıkça verilirse — gerçek bir çökme kurtarmasının alacağı biçim — o süper-adım YENİDEN OYNANIR, çağrı sayacı `2`'ye çıkar). Sözleşme `AddWorkflowFunction`'ın kendi XML belgesine ve `docs-site/concepts/workflows.md`'ye yazıldı: bir dosya yazan, bir HTTP çağıran veya bir veritabanına satır ekleyen kod düğümü kendi idempotency anahtarını taşımalıdır. Bu davranış kod düğümüne ÖZGÜ değildir — MAF'ın checkpoint/resume mekanizması AYNI şekilde çalışır bir agent adımı için de — ama bir kod düğümü yan etkiyi somutlaştırdığı için ("model tekrar cevapladı" değil "dosya tekrar yazıldı") sözleşme burada AÇIKÇA yazılmaya değer bulundu.

### K-208

**K-208 — `ModelBinding.ProviderSettings` sözleşmeye eklendi; bilinmeyen anahtar derleme hatasıdır** *(kullanıcı kararı)**

Anthropic'in prompt caching'i ve Gemini'nin güvenlik eşikleri `ModelBinding`'in sabit alanlarına sığmaz; onları gövdeye eklemek `AgentPrism.Abstractions`'a bir satıcının kavramını sızdırırdı. `IReadOnlyDictionary<string, JsonElement>` seçildi — `AgentDefinition.Metadata` ile **aynı şekil**, dolayısıyla `jsonb` yolu, HTTP sözleşmesi ve kaynak üreteci bağlamı hiç değişmeden çalıştı (PostgreSQL 440 + SQLite 214 sözleşme testi doğruluyor). Bilinmeyen anahtar **sessizce yok sayılmaz**: K-034'ün `ReasoningEffort` için verdiği kararın aynısı — yok sayılan bir ayar, kullanıcının beklediği davranışı almamasına ve sebebini görememesine yol açar. Uygulama iki hata sınıfını ayırır (yanlış önek ↔ tanınmayan anahtar), çünkü düzeltmeleri farklıdır. Ayarlar `ChatOptions.RawRepresentationFactory` ile taşınır — MEAI'ın resmî kaçış kapısıdır ve AgentPrism ham HTTP yazmaz. 🚨 Ölçüldü: Anthropic adaptörü ham gösterimdeki alanların **üzerine yazmaz**; yer tutucu `Model` ile gönderilen istek gerçekten o adla gitti ve `404 not_found_error` döndü — bu yüzden `model` ve `max_tokens` dekoratörün kendi sorumluluğudur.

### K-411

**K-411 — Senkronizasyon kopyası (`<ad> 2.<uzantı>`) taraması `faz-tamamlama` skill'ine KAPI olarak eklendi; kapsam `src tests samples`**

Faz 57 kapanışı iki kopya test dosyasını COMMIT etti (`64882c6`) ve `main` DERLENMEZ hâlde kaldı: `GovernanceEndpointTests 2.cs` + `RunScoreStatisticsTests 2.cs`, ikisi de kendi ad alanında ikinci bir tip bildirdiği için `error CS0101`. Ölçümle doğrulandı — `HEAD`'te ayrı bir `git worktree` kurulup derlendi, hata aynen üretildi. Kopyalar ayrıca K-408'in çevirdiği Türkçe kaynağı geri getiriyordu (`Trace_yoksa_404_ve_gerekce_doner` gibi). İki bağımsız sebep kaçırdı: (1) tarama komutu yalnız `src`'ye bakıyordu, kopyalar `tests/` altındaydı; (2) `git status` TEMİZ görünüyordu çünkü dosyalar izlenmeye başlanmıştı — "untracked" uyarısı çıkmadı. Asıl kök sebep süreçseldi: kontrol `MEMORY.md`'de bir NOT olarak duruyordu, `faz-tamamlama` kontrol listesinde bir ADIM değildi; not okunmadığında hiçbir şey durdurmuyordu. Bu, aynı tuzağın BEŞİNCİ tekrarıdır (Faz 30, 47, 52, 57 ve bu). Çözüm: tarama `faz-tamamlama` Adım 1'e, dört kapıdan ÖNCE çalışan zorunlu bir blok olarak yazıldı; komut `find src tests samples -name "* 2.*"` ve iki tuzak (izlenen kopya, `src` yetmez) açıkça anlatıldı. `MEMORY.md`'deki not da beş vakaya güncellendi.

### K-271

**K-271 — `tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs` SILINMEDI (plandan sapma)**

Faz 39 plani bu dosyayi da "kaldirilan kopyalar" listesine yaziyordu. Inceleme gosterdi ki 27 cagrı yerinden 20'den fazlasi ARGUMANLI bir `IChatClient` (`Core.UnitTests.Fakes.FakeChatClient` veya satir-ici lambda) geciriyor — derleyici/kayit zincirinin (`AgentDefinitionCompiler`, `RunRecordingAgent`, maliyet metrikleri) INTERNALLERINI beyaz-kutu test eden, `AgentPrism.Testing`'in siralı-kuyruk tasarimiyla KARSILANAMAYAN bir kullanim bicimi. Yeni paketin public yuzeyine "ham `IChatClient` gecir" kacis kapisi eklemek K-016'nin "test paketi API'si bir kez dogru yapilmali" uyarisina aykiridir (gereksiz genisleme). Ayrica `Core.UnitTests`'in `AgentPrism.Testing`'e (ki `AgentPrism.AspNetCore`'a, dolayisiyla K-008'in on surum MAF paketlerine baglidir) baglanmasi mimarinin dogal katman yonune tersti — Core'un kendi birim testleri, Core'un UZERINE kurulu bir paketten test yardimcisi ithal etmemelidir. Kullanici "tam kapsam" secimini (plani birebir uygula) onayladiktan SONRA bu tek istisna, incelemenin ortaya cikardigi teknik gerekceyle birlikte agent tarafindan kararlastirildi ve kullaniciya bildirildi.

### K-427

**K-427 — `docs/` kökü YALNIZ canlı dokümanı taşır; kapanmış her kayıt `docs/arsiv/`'e taşınır** *(kullanıcı kararı)**

Kök 12 dosyaya çıkmıştı ve altısı ölüydü: iki dalga yol haritası (`IKINCI-FAZ-YOL-HARITASI.md`, `UCUNCU-FAZ-YOL-HARITASI.md` — anlattıkları Faz 8–56'nın tamamı bitmişti), kendini "✅ KAPATILDI" ilan eden `DEGERLENDIRME-RAPORU-2026-08.md`, ve iki SOĞUK üretilen karar indeksi (`KARARLAR-INDEKS-ARSIV.md`, `KARARLAR-INDEKS-REDDEDILEN.md` — hiçbir oturum başında okunmaz, bütçeye girmezler). Altısı da `git mv` ile `docs/arsiv/`'e taşındı; içerik SİLİNMEDİ (AGENTS.md kuralı). Kökte kalan altı dosyanın her birinin tek bir işi var: `MIMARI.md` (bugünkü resim), `KARARLAR.md` + `KARARLAR-INDEKS.md` (defter + sıcak indeks), `YOL-HARITASI.md` (üretilen durum), `ADAYLAR.md` (seçilmemiş kalemler), `MAF-GENISLEME-NOKTALARI.md` (MAF referansı). `dokuman-bakim.py` iki üretilen indeksi artık `docs/arsiv/` altına yazar ve ürettiği bağlantıları yeni derinliğe göre kurar. 🚨 Taşınan bir dosyanın KENDİ göreli bağlantıları da bir seviye derinleşir — 98 bağlantı yeniden yazıldı; aynı sınıfın Faz 58'den kalan 56 kırık bağlantısı (`arsiv/BEYIN-FIRTINASI.md` ve koşum kayıtları, taşındıklarında düzeltilmemişlerdi) bu turda kapatıldı.

### K-129

**K-129 — `Microsoft.Agents.AI.Workflows.Declarative` alınmadı** *(kullanıcı kararı)**

Faz 15'in "sürüm uyuşmazlığı" endişesi doğrulanmadı: **1.16.0 sürümü vardır**. Yine de alınmadı, iki ölçülmüş gerekçeyle. (1) **Bağımlılık grafiği:** geçişli paket sayısı 23 → 42 (**+19**); gelenler arasında tüm Power Fx yorumlayıcı yığını (`Microsoft.PowerFx.Core/.Interpreter/.Json/.LanguageServerProtocol/.Transport.Attributes`), `Microsoft.Agents.ObjectModel.*` (ayrı sürüm şeması `2026.2.4.1`) ve `System.CodeDom` var. K-001 ve "tüketicinin bağımlılık grafiğini kirletme" kuralına aykırı. (2) **MAF desteklemiyor:** `DeclarativeWorkflowOptions` zorunlu bir `ResponseAgentProvider` ister ve paketin XML dokümanı *"Using other `AIAgent` or `ChatClientAgent` patterns that are not based on the Response API is currently not supported"* der. AgentPrism'in agent'ları `ChatClientAgent` tabanlıdır; K-030 gereği Responses'ın sunucu tarafı depolaması kapalıdır ve `/v1/conversations` oturum deposu üzerine kuruludur (K-043). Pakette hazır sağlayıcı uygulaması yoktur; beş soyut üye tüketiciye bırakılmıştır.

### K-409

**K-409 — Migration `.sql` yorumları İngilizce'ye çevrildi; 61 dosyanın tamamının checksum'ı değişti — var olan bir veritabanına karşı çalıştırmadan önce şema düşürülüp yeniden kurulmalıdır** (kullanıcı kararı)**

`MigrationDescriptor.ComputeChecksum` dosyanın ham metninin TAMAMININ SHA-256'sını alır; yorumlar hash'e dahildir. AgentPrism henüz NuGet'e yayınlanmadığı (K-068) ve uygulanmış migration taşıyan bilinen bir dağıtım olmadığı için kullanıcı yorumların temizlenmesine karar verdi. Gerçek risk canlı bir PostgreSQL koşumunda ÖLÇÜLDÜ (Faz 57 kapanışı): var olan bir `__migrations` satırının checksum'ı elle bozulduğunda `samples/AgentPrism.Api` `AgentPrismException` ile başlangıçta ÇÖKTÜ (mesaj: "Migration '0001_initial' has been applied to the database but the file's content has changed..."); `DROP SCHEMA` + yeniden başlatma migrasyonları sıfırdan sorunsuz uyguladı (29/29 PostgreSQL migration). 🚨 Testler bu kusuru YAKALAYAMAZ — Testcontainers her koşumda sıfır bir veritabanı kurar; checksum uyuşmazlığı yalnız ÖNCEDEN VAR OLAN bir veritabanında görülür. Prosedür belgelendi: uygulanmış migration taşıyan bir dağıtımda bu değişiklik alınmadan önce şema düşürülüp yeniden kurulmalıdır (veya migration runner'ı ayrı bir adımda elle güncellenmelidir).

### K-385

**K-385 — K-302 güncellendi: `AddCaseAsync`'in yeniden deneme döngüsüne jitter eklendi, üst sınır 5 → 10'a çıkarıldı**

`EvalStoreContract.AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` (8 eş zamanlı çağrı) PostgreSQL'de art arda iki koşuda da `5 denemede sira numarasi atanamadi` ile GERÇEKTEN ve tekrarlanabilir şekilde başarısız oldu — hipotetik değil, ölçülen bir yakınsama kırılganlığıydı. Kök neden: kaybedenler `MAX(seq)+1` çakışmasından hemen sonra gecikmesiz yeniden deniyordu; eşit gecikmeyle art arda deneyen 8 yazıcı, kazananların ayrışmasını rastgele şansa bırakıyor ve "thundering herd" ile aynı turda tekrar çarpışabiliyordu. K-302'nin temel kararı (düz `INSERT` + `IsUniqueViolation` yakalama + yeniden deneme, `ON CONFLICT`/`MERGE` DEĞİL) korunur — yalnız yeniden deneme döngüsü `attempt > 0` iken `Random.Shared.Next(1, attempt*5+1)` ms jitter ile beklemeye başlar (kaybedenleri zamanda dağıtır) ve üst sınır test edilen eş zamanlılık seviyesine (8) pay bırakacak şekilde 10'a çıkarılır. PostgreSQL'de art arda iki tam koşumda (950/950) ve SQLite'ta (493/493) doğrulandı.

### K-127

**K-127 — 🚨 Executor kimliği `(workflow, agent)` çiftinden türetilir; MAF'ın özel alanına yazılır**

Ölçüldü: bir grafta kimliği **değişken olan tek şey** agent executor'udur (`{ad}_{AIAgent.Id}`); `OutputMessages`, `Batcher/*`, `HandoffStart`, `HandoffEnd`, `ConcurrentEnd`, `GroupChatHost`, `MagenticOrchestrator` zaten sabittir. Faz 15 dokümanı "hazır desenler yeniden yazılmalı" diyordu — gerek yoktu. `AIAgent.Id` sanal değildir ve setter'ı yoktur, ama derleyicinin ürettiği arka alan (`<Id>k__BackingField`) salt-okunur değildir (reflection ile ölçüldü: `initonly=False`). `WorkflowAgentIdentity` yalnızca AgentPrism'in **kendi sarmalayıcı örneğinde** bu alanı SHA-256 türevi 32 haneli bir değerle yazar; MAF'ın hiçbir nesnesine dokunulmaz. Bedel: MAF alanı kaldırırsa kimlik rastgele kalır — istisna atılmaz, uyarı loglanır ve davranış Faz 15'e döner. Sessiz bozulmayı `WorkflowAgentIdentityTests.Kalici_kimlik_yazilabiliyor` engeller. **Kanıt:** gerçek süreç yeniden başlatıldı, kimlik aynı kaldı ve yeniden başlatmadan **önce** oluşan bekleyen istek sonrasında cevaplandı; çıktı üretildi. Faz 15'te bu akış `InvalidDataException` ile düşerdi.

### K-434

**K-434 — Dosya belleği/metin araması alt ağacı kiracı VE agent adıyla öneklenir; oturumlar arası paylaşım BİLEREK korunur**

F-105: K-381 kiracı sınırını kapatmıştı, kiracı **içi** sınırı açık bırakmıştı — aynı kiracının `EnableFileMemory` açık bir agent'ının yazdığı dosyayı, `EnableTextSearch` açık **başka** bir agent buluyordu. Aday kalemin önerdiği yol (`AsyncLocal` tabanlı "güncel oturum" kapsamı) **alınmadı**: bu depoda `AsyncLocal` dört kez yanlış açıldı (Faz 6, 11, 12, 15) ve beşinci deneme aynı sınıf hatayı üretme riski taşıyor. Ölçüldü ki gerek de yok: `CompiledAgentCache` anahtarı zaten `(TenantId, Name, Version, DependencyFingerprint)` taşır, yani **agent adı derleme anında bilinir** ve önek `\{tenantId\}/\{agentName\}` olarak ambient durum olmadan kurulabilir. Oturum boyutu **bilerek** kapsam dışıdır: dosya belleği agent düzeyinde bir bellektir; oturum başına yalıtmak agent'ın hatırladığı her şeyi her konuşma başında silerdi — yeteneğin amacını yok eder. Kalıcı bir `AgentFileStore` uygulaması kullanan bir tüketici için yol değişimi eski dosyaları görünmez kılar; yayınlanmış sürüm olmadığı için bu bugün bedavadır, yayından sonra bir geçiş notu isterdi.

### K-438

**K-438 — CORS, `services.AddCors()` OLMADAN `CorsService`/`CorsMiddleware`'in elle kurulmasıyla uygulanır**

F-108 planı `AllowedOrigins`'i `MapAgentPrism()`'in `configure` callback'ine koyuyordu — ama `MapAgentPrism()` `app.Build()`'den SONRA çalışır ve o noktada DI kabı mühürlüdür; `AddCors()` orada çağrılamaz. Ölçüldü: `app.UseCors(...)` `AddCors()` olmadan **host başlangıcında** `InvalidOperationException` (`ICorsService` çözülemiyor) fırlatıyor. `AgentPrism.Core`'a `AddCors()`'u koşulsuz eklemek de çalışmazdı — o paket `Microsoft.AspNetCore.App`'e `FrameworkReference` taşımıyor (K1: konsol/worker host'ta da çalışmalı). Çözüm ölçüldü ve uçtan uca (gerçek `HttpClient` + preflight `OPTIONS`) doğrulandı: `CorsService`'in genel kurucusu yalnız `IOptions<CorsOptions>` (atılabilir varsayılan örnek) ve `ILoggerFactory` (zaten kayıtlı, MÜHÜRLÜ kaptan OKUNUYOR, yeni kayıt EKLENMİYOR) istiyor; `CorsMiddleware`'in "politika doğrudan" kurucusu DI'a hiç girmiyor. `AgentPrismCorsMiddleware.Map` bunları `MapAgentPrism()` içinde elle kurar ve `app.Use(...)` ile boru hattına ekler; istek yolu `prefix` ile bilerek sınırlanır (konsolun kendi CORS ihtiyacına karışmaz).

### K-259

**K-259 — Saklama korelasyonları BARE hedef adı değil, TAM NİTELENDİRİLMİŞ ad kullanır (Faz 25 hatası düzeltildi)**

`RetentionTargetRegistry`'nin `eval_case_results`/`workflow_checkpoints`/`attachments` hedefleri, kendi `EXISTS`/`NOT EXISTS` alt sorgularında dış tabloya BARE adla (`eval_case_results.eval_run_id` gibi) atıfta bulunuyordu. PostgreSQL/SQL Server'da bu çalışır çünkü ikisi de aliassız bir `FROM sema.tablo`'yu hem `sema.tablo.sütun` hem bare `tablo.sütun` ile referanslamaya izin verir; SQLite'ta ise `QualifyTable` önek+ad BİTİŞTİRİR (nokta yok, K-193) ve gerçek nesne adı (`t_xxxxxxxxworkflow_checkpoints`) bare `workflow_checkpoints`'e HİÇ eşleşmez — "no such column" ile çalışma anında patlar. Faz 36'nın SQLite'a karşı kayan `MaxRows` sözleşme testi bunu YAKALADI. Düzeltme üç hedefin de korelasyonunu `Table(...)` (tam nitelendirilmiş ad) kullanacak şekilde değiştirdi; bu ifade tüm üç sağlayıcıda geçerlidir (Postgres/SqlServer `sema.tablo.sütun`, SQLite düz `tablo.sütun`). Bu, Faz 25'in `WherePredicate`'inde de zaten var olan bir hataydı — yalnız `MaxRows`'a özgü değil; `MaxAgeDays` ile bu üç hedefin SQLite'ta silme/sayma/arşivlemesi de bugüne kadar sessizce YANLIŞ çalışıyordu (`NOT EXISTS`/`EXISTS` her zaman aynı sabit sonucu döndürüyordu).

### K-335

**K-335 — A2A sunucu maliyeti yeniden ölçüldü: "+2 paket" değil "+4 paket"; iki paket plan taslağında hiç yoktu (Faz 50)**

Plan (50.1) yalnız `Microsoft.Agents.AI.Hosting.A2A` + `A2A`'yı ölçmüştü ("artımlı maliyet iki pakettir"). Gerçek uygulama sırasında ölçüldü (`dotnet list package --include-transitive`, base=bugünkü `Microsoft.Agents.AI.Hosting`+`.Hosting.OpenAI`): gerçek ek dört pakettir — `Microsoft.Agents.AI.Hosting.A2A`, `A2A`, ve plan taslağında HİÇ geçmeyen `Microsoft.Agents.AI.Hosting.AspNetCore` + `A2A.AspNetCore`. Sebep: `AddA2AServer(...)` yalnız DI KAYDI yapar ("This method only registers the server; to expose it as an HTTP endpoint, call one of the MapA2AHttpJson or MapA2AJsonRpc endpoint mapping methods" — SDK'nın kendi XML belgesi); o eşleme uzantıları (`MapA2A`, `MapWellKnownAgentCard`) AYRI bir NuGet paketi olan `A2A.AspNetCore`'dadır ve o da `Microsoft.Agents.AI.Hosting.AspNetCore`'u (session isolation, `ClaimsIdentitySessionIsolationKeyProvider`) beraberinde ister. Dört paketin hepsi aynı SDK ailesinden (aynı sürüm hattı `1.16.0-preview.260730.1` / `1.0.0-preview2`) ve `Microsoft.Agents.AI.A2A` istemci paketinin taşıdığı `Google.Protobuf` bu dört pakette YOKTUR — ağırlık endişesi hâlâ geçersizdir, yalnız sayı düzeltildi.

### K-279

**K-279 — Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu `tenantId` alır** *(kullanıcı kararı)**

🚨 **Güvenlik düzeltmesi.** Ölçüldü (Faz 41): `RetentionTargetRegistry` hiçbir hedefte kiracı süzgeci taşımıyordu, ama saklama politikası kiracı başınadır (`retention_policies.tenant_id`, Faz 25 kullanıcı kararı). Sonuç: A kiracısının admin'i `/api/retention/run` çağırınca B kiracısının verisi de siliniyordu — çapraz kiracı **veri yıkımı**. Kullanıcı üç seçenek arasından **tam düzeltmeyi** seçti (public API'yi kırma pahasına): `IRetentionStore`'un dört metodu `string? tenantId` alır (`null` = kurulum geneli), `RetentionTargetDefinition` bir `TenantPredicate` taşır ve kendi `tenant_id` sütunu olmayan üç hedef (`run_events`, `tool_invocations`, `eval_case_results`) sahibine bakan bir `EXISTS` ile süzülür. `SqlDialect` imzası değişmedi (yüklem çağıran tarafta birleştirilir); yalnız `BuildRetentionFindNthRowCutoffSql` bir `extraPredicate` aldı. `RetentionExecutor` **her zaman** isteyen kiracıyı geçirir: `'*'` politikası artık "bütün kiracılara uygulanan bir politika" demektir, "tek çağrıda bütün kiracıları sil" demek değil.

### K-520

**K-520 — Figürler (ekran görüntüsü ve diyagram) temadan BAĞIMSIZDIR; iki temada da açık plaka üzerinde durur (Faz 76)**

İki bağımsız kısıt aynı yöne çıktı. (1) Konsol ekran görüntüleri **açık** temadadır (ölçüldü: gösterge paneli ortalaması rgb(250, 249, 251)); koyu sayfada koyu bir diyagram, yanındaki açık ekran görüntüsüyle çakışırdı. (2) Mermaid paletini çalışma anında JavaScript'te hesaplar ve bir CSS değişkenini çözemez, bu yüzden temaya tepki veren palet her tema düğmesinde yeniden render ve yanıp sönme isterdi. Belirleyici kanıt üçüncüsüydü: mermaid'in flowchart stil sayfası HER `.label`'ı `nodeTextColor` ile boyar — kenar etiketleri dâhil — ve kenar etiketi bir düğümün değil **plakanın** üzerinde, %50 saydam bir dikdörtgene çizilir. Tema değiştiren bir plakada hiçbir tek etiket rengi iki karışımda birden AA geçemez (gerekli: L ≤ 0,0945 ve L ≥ 0,2140). İlk deneme — koyu dolgu + beyaz metin — bu yüzden geri alındı; sabit açık plaka + tek mürekkep rengi ikisini birden çözer ve kontrast kapısı hepsini ölçebilir. Palet `site.css`'te yaşar, `astro.config.mjs` onu **oradan okur** (ikinci kopya yok).

### K-523

**K-523 — Kapanmış faz dokümanları `docs/arsiv/fazlar/` altında yaşar; `docs/**.md` bütçesi büyütülmez (kullanıcı kararı)**

Faz 76 kapanışında `docs/**.md` sayacı 4.925.858/5.000.000 idi (%1 boş) ve son yedi fazın ortalaması ~41 KB/faz (Faz 75 tek başına +105 KB) — bir sonraki faz sınırı kesin aşacaktı. K-214'ün "bütçe büyütülmez, bölünme uygulanır" zinciri altıncı kez tutuldu. Taşınan küme faz 00–59 (60 dosya, 1.775.033 B); sayaç **3.146.178**'e düştü (%37 boş, ~40 faz nefes alanı). Ölçüm taşımayı meşrulaştırdı: `faz-baslangic` yalnız GÜNCEL fazı ve bir öncekinin "Sonraki Faza Devir Notu" bölümünü okur — 00–59 hiçbir oturumda baştan okunmuyordu, yalnız grep/link ile geziliyordu; `scripts/dokuman-bakim.py` `docs/arsiv`'i zaten BİLEREK hariç tutuyor ("arşive taşımak sayacı gerçekten düşürür"). 874 atıf çözümlemeye dayalı bir script ile yeniden yazıldı (metin eşlemesi DEĞİL — `docs/manuel-test/` aynı `NN-AD.md` desenini kullanır ve düz `sed` onu da bozardı); `yol_haritasi_uret()` artık iki konumu da tarar, yoksa 60 faz yol haritasından sessizce düşerdi. `docs/` kökü açık ve yeni fazlara ayrıldı.

### K-382

**K-382 — `AgentPrismEndpointFilter`'a `CheckTenancyWhitelist` eklendi; `AllowedTenants` doluyken listede olmayan bir aday artık istek endpoint'e ulaşmadan 403 ile reddedilir, varsayılan kiracıya SESSİZCE düşmez (manuel kabul testi hazırlığı)**

`HttpTenantContext.TenantId`'nin `??` zinciri (`... ?? Resolve() ?? DefaultTenantId`) `Resolve()`'un `Accept()` üzerinden ürettiği `null`'ı (beyaz liste reddi) varsayılan kiracıya düşürüyordu — `AgentPrismTenancyOptions.AllowedTenants`'ın kendi XML belgesinin ("listede olmayan bir değer varsayılan kiracıya düşmez") doğrudan tersiydi. Denetim `HttpTenantContext`'in İÇİNE değil, `AgentPrismEndpointFilter`'a eklendi: filtre zaten `CheckTenantHeaderConflict`/`CheckScope` ile AYNI şekilde istek endpoint'e ulaşmadan reddetme yapıyor (kurulu desen). Denetim yalnız statik-token ve anonim (kimliksiz, `_authToken` tanımsız) yollarda çalışır — API-anahtarı yolunda kiracı ZATEN anahtarın kendisinden (`ResolveFromApiKey`, `Resolve()`'dan ÖNCE gelir) çözülür ve `CheckTenantHeaderConflict` başlığın anahtarla çelişmediğini ayrıca doğrular; `CheckTenancyWhitelist`'i o yolda da çalıştırmak `AllowedTenants`'ı API anahtarlarının kendi (daha güçlü, sır-tabanlı) yetki mekanizmasıyla karıştırırdı.

### K-351

**K-351 — Parametre tipi beyaz listesi `record`/`class` (composite) tipleri KAPSAMAZ; `APG0003` ile reddedilir (Faz 52)**

Açık Soru 5 "composite dahil beyaz liste" önermişti. Composite tip AOT-güvenli bağlamak ya reflection (`JsonSerializer`'ın reflection yolu → `RequiresUnreferencedCode`, fazın kendi AOT DoD'sini bozar) ya da ikinci bir iç içe kaynak üreteci (tüketicinin `JsonSerializerContext`'i — TEK derleme geçişinde bir üretecin diğerinin çıktısını görüp göremeyeceği Roslyn'de belgelenmemiş/garantisiz bir davranıştır) gerektirirdi; ikisi de bu fazın kapsamında doğrulanmadan kabul edilecek risklerdi. Gerçekleşen whitelist: ilkel sayısal tipler, `bool`, `string`, `Guid`, `DateTime(Offset)`, `enum`, bunların `Nullable<T>`'i, dizi/`IReadOnlyList<T>` ve `CancellationToken` — hepsi `JsonElement` üzerinde yansımasız tip-özel `Get*()` çağrılarıyla bağlanır. `APG0003` composite tipi açıkça reddeder ve kaçış yolunu (`AddTool(AIFunctionFactory.Create(...))`) gösterir; DoD'nin öngördüğü "beyaz liste haksız reddi" riski buydu.

### K-384

**K-384 — SSE akış uçlarındaki (`AgentEndpoints`, `OpenAIResponsesEndpoints`, `OpenAIChatCompletionsEndpoints`) dar istisna filtresi kaldırıldı; `OperationCanceledException` dışındaki HER istisna artık bir `error` çerçevesine dönüşür (K-296'nın tamamlanması, manuel kabul testi hazırlığı)**

K-296 yalnız KAYDEDİLEN `RunError.Class` alanını düzeltmişti; SSE akışının KENDİSİ hâlâ `catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)` ile sınırlıydı — gerçek sağlayıcı SDK istisnaları (OpenAI'nin `ClientResultException`'ı, Anthropic'in `AnthropicApiException`'ı, ikisi de bu üç tipe uymaz) baglantıyı `error` çerçevesi ÜRETMEDEN kapatıyordu; istemci (playground dahil) bunu SESSİZ BAŞARI sanıyordu — `GET /api/runs/{id}` gerçek durumun `Failed` olduğunu gösterse bile. Üç ayrı dosyada kopyalanmış AYNI desendi. Düzeltme dar `when` filtresini kaldırıp düz `catch (Exception ex)` bırakır — `OperationCanceledException` zaten ayrı ve ÖNCE yakalandığı için (istemci bağlantı kesince yazacak kimse kalmaz) geniş `catch` yalnız GERÇEK hataları kapsar; Google'ın `ClientError`/`ServerError`'ı zaten `HttpRequestException`'dan türediği için davranışı DEĞİŞMEDİ (regresyon riski yok).

### K-350

**K-350 — `AddGeneratedTools()` `IAgentPrismBuilder`'a EKLENMEDİ; derlemeye özel üretilmiş bir uzantı metodudur (Faz 52)**

Planın "Planlanan Public API" taslağı bunu arayüz üyesi gösteriyordu ama aynı planın 52.3 bölümü ve Açık Soru 7'nin cevabı ("internal statik sınıf + uzantı metodu") zaten çelişiyordu; teknik olarak da imkânsızdı — `AgentPrism.Core` derlenirken tüketicinin `AgentPrism.Generated` ad alanındaki sınıfı henüz YOKTUR, Core ileri referans veremez. Çözülen gerçek desen: üreteç HER tüketici derlemesinde kendi `namespace AgentPrism { public static class AgentPrismGeneratedToolsBuilderExtensions { public static IAgentPrismBuilder AddGeneratedTools(this IAgentPrismBuilder builder) } }` uzantısını üretir. Sonuç: `IAgentPrismBuilder` bu fazda HİÇ DEĞİŞMEDİ — Faz 36/K-yeni ve Faz 45 ile paylaşılan "Faz 7'den önce kırıcı" riski Faz 52 için ORTADAN KALKTI. Yan bulgu (ölçüldü): `OutputItemType=Analyzer`, çok-sıçramalı `ProjectReference` zincirinde (samples/AgentPrism.Api → AgentPrism meta → Core → Generators) YAYILMAZ — yalnız paket tüketiminde (`PackageReference`) NuGet'in kendi mekanizması yayılır; `samples/AgentPrism.Api.csproj`'a bu repo için doğrudan ikinci bir `Analyzer` `ProjectReference`'ı eklendi.

### K-157

**K-157 — `RunEventWriter.CompleteAsync`'e eklenen `cost` parametresi ilk yazımda `RunCompletion`'a bağlanmamıştı — canlı sınamada yakalandı** 🚨**

Derleme ve 1068 testin tamamı bu hatayı yakalamadı: birim testleri `RunPricingResolver`'ı izole çağırıyordu, sözleşme testleri `Store.CompleteRunAsync`'i `RunCompletion.Cost` elle doldurarak çağırıyordu, ikisi de `RunRecordingAgent → RunEventWriter → Store` boru hattını uçtan uca hiç çalıştırmıyordu. Örnek uygulama gerçek bir OpenAI çağrısıyla çalıştırılıp `/api/runs` çıktısında `cost: null` görülünce ortaya çıktı. Düzeltme tek satır (`Cost = cost` eksikti); kalıcı önlem `RunRecordingAgentTests`'e eklenen iki uçtan uca test (`Maliyet_pipeline_ucdan_uca_hesaplanip_yaziliyor`, `Maliyet_pipeline_fiyatsiz_modelde_unknown_yazar`) — `RunRecordingAgent`'ı gerçek `RunPricingResolver` ve `FakeChatClient` ile, decoratörü atlayıp doğrudan kurar. **Ders**: yeni bir parametre eklerken imzayı değiştirmek yetmez, çağrı gövdesini de değiştirmek gerekir; sözleşme testleri veri akışının başlangıcını (depo) sınar ama ORTA katmanın (yazıcı) parametreyi gerçekten ilettiğini sınamaz — bu yüzden örnek uygulamayı gerçek bir sağlayıcıyla çalıştırmak (Adım 2) atlanabilir bir adım değildir.

### K-372

**K-372 — Senkron/MCP/A2A çalıştırma yolu `pending_approvals`'a HİÇ yazmaz; yalnız kuyruktan koşan (`Prefer: respond-async`) çalıştırmalar yazar (Faz 55, plandan sapma — planın Açık Soru 1 önerisi "B: her ikisi" idi, uygulanan "A: yalnız kuyruk")**

Plan, operatörün "bekleyen her şeyi" tek ekranda görmesi gerekçesiyle B'yi önerdi. Ama senkron/MCP/A2A yolda karar zaten CANLI bir istemciye (bir sonraki turdaki `approvals` alanı) bağlıdır — aynı `ToolApprovalRequestContent`i AYNI ANDA hem operatör konsoldan hem orijinal istemciden karara bağlamak, ikisinin sonuç olarak AYNI oturum geçmişine yarışan `ToolApprovalResponseContent` yazmasına yol açardı (çift karar yarışı). Yalnız kuyruk yolunda CANLI istemci yoktur (Faz 46'nın çözdüğü asıl boşluk, bkz. §55.1) — mailbox yalnız bu boşluğu kapatır. `RunRecordingAgent`'ta `AgentPrismRunOptions.SuspendOnApproval` yalnız `AgentRunJobHandler`'ın (kuyruk yolu) kurduğu seçeneklerde `true`'dur; senkron/MCP/A2A çağıranları bu alanı hiç ayarlamaz.

### K-433

**K-433 — Pompa, yanıtlanmış bir istekten sonra akışı yeniden açmadan ÖNCE MAF'ın kendi çalıştırma durumunu sorar**

F-106 (`HATA-K-003`, K-401): Magentic orkestrasyonu round-limit'e ulaşınca kendi `WorkflowOutputEvent`'ini üretip **sonlanıyor**; pompa "istek yanıtlandı" diye akışı yeniden açtığı için MAF "orkestrasyon zaten tamamlandı" istisnası atıyor ve gerçekten sonuç üretmiş bir çalıştırma `RunFailed` olarak kaydediliyordu. Çözüm `run.GetStatusAsync()` ile çerçevenin **kendi durumuna** bakmaktır; `PendingRequests` veya `Running` değilse döngü kırılır. Round-limit **metnini** eşleştirmek bilerek reddedildi: MAF cümleyi değiştirdiği an kırılır. 🚨 **Dürüst sınır: bu düzeltmenin düşen bir testi YOK.** Sahte (`EchoAgent`) katılımcılarla iki ayrı repro denendi (`MaxIterations` 1 ve 2, tek ve çift onay turu); ikisi de düzeltme **kapalıyken bile geçti**, yani kusuru üretmiyorlar. Test tiyatrosu bırakmamak için o test dosyası **silindi**. Kapı manuel kabul case'idir (gerçek model + `requirePlanApproval`); F-106 bu yüzden aday listesinde **açık** kalır ve "azaltıldı, doğrulanmadı" olarak işaretlenir.

### K-337

**K-337 — MCP/A2A dış çağrısı `ChildAgentInvoker`'ı KULLANMAZ; `ExternalAgentProxy`/`CatalogToolCallHandler` her zaman YENİ bir kök çalıştırma açar (Faz 50)**

`ChildAgentInvoker` bir agent'ın BAŞKA bir agent'ı çağırmasını modeller ve `AgentPrismRunContext.Current` üzerinden ambient bir ÜST kapsam (derinlik, bütçe, kiracı) bekler — dış çağıran bir agent değildir, böyle bir üst kapsamı yoktur ve `ChildAgentInvoker.Refuse()` "calistirma kapsami yok" ile HER ZAMAN reddederdi. Çözüm iki ayrı, küçük sınıftır: MCP için `CatalogToolCallHandler` (statik handler, `IAgentCatalog.ResolveAsync` + `AgentPrismRunOptions{Depth=0,Budget=yeni}` ile çağırır), A2A için `ExternalAgentProxy` (gecikmeli çözümlü bir `AIAgent`, `AddA2AServer`'ın KAYIT ANINDA (Build() öncesi) somut bir nesne istemesiyle katalogun yalnızca kurulmuş bir `IServiceProvider` ile çözülebilmesi arasındaki çelişkiyi `CallableAgentResolver`'ın aynı "geç çözüm" deseniyle çözer — `AttachServices` `MapAgentPrismA2A()` tarafından Build() SONRASI bir kez çağrılır). İkisi de `RunRecordingAgent`'ın zaten sardığı katalog agent'ını çağırdığı için normal bir `runs` satırı ve normal kota tüketimi OTOMATİK gerçekleşir — ikinci bir kayıt yolu yazılmadı.

### K-472

**K-472 — `InboundTriggerDispatcher`, "tetikleyici yok" ile "imza yanlış"ı TEK bir `InboundTriggerOutcome.Unauthorized`'a (HTTP `401`) birleştirir; ayrı bir `NotFound`/`404` yolu YOKTUR (Faz 66, bağımsız denetim 🔴 #1)**

Plan taslağı (66.2, 66'nın DoD'si) "aynı gövde ve kod" istiyordu ama plan taslak kodu ile manuel case tablosu (case 7: "bilinmeyen kiracı → 404") ÇELİŞİYORDU — ilk uygulama bu çelişkiyi `NotFound=404` ile `Unauthorized=401`'i AYRI tutarak "çözdü", ki bu tam olarak enumeration açığının kendisiydi: rastgele imzayla gönderilen bir istek `404` alırsa ad YOK, `401` alırsa ad VAR — kiracı/tetikleyici adı numaralandırılabilirdi. `faz-denetim`'in bağımsız denetçisi bunu 🔴 olarak işaretledi ve yayınlanan `docs-site` sayfasının da (yanlışlıkla) "aynı" dediğini gösterdi. Düzeltme: bilinmeyen kiracı, bilinmeyen/devre dışı tetikleyici adı VE her imza/zaman damgası hatası TEK bir jenerik `401` yanıtına birleşti; manuel test case'leri (MT-JOB-096/097) buna göre güncellendi.

### K-353

**K-353 — `.UseMcp()` yapılandırmayı AÇIKÇA alır; AgentPrism kendiliğinden `IConfiguration` okumaz (2026-08-08 denetimi)**

Ölçülmüş kusur: `AgentPrismMcpOptions.SectionName` (`"AgentPrism:Mcp"`) tanımlıydı ama **hiçbir kod `Bind` çağırmıyordu** — `services.AddOptions<AgentPrismMcpOptions>()` yalnız kod-taraflı `configure` delegesini kabul ediyordu. `AgentPrism:Mcp:RefreshInterval` gibi bir ortam değişkeni **hiçbir hata vermeden hiçbir şey yapmıyordu**; container/K8s (env-var-first) dağıtımlarında tipik bir sessiz yapılandırma kaybı. Çözüm, otomatik bağlama **değil**, depodaki her `Use*` uzantısıyla tutarlı bir aşırı yüklemedir: `UseMcp(IConfiguration section, Action<AgentPrismMcpOptions>? configure = null)`. Otomatik okuma K1'i (sıfır sürpriz) zorlardı ve `IAgentPrismBuilder` zaten `Configuration` taşımıyor. Bağlama K-021 gereği **elle** yazıldı (yansımalı `Bind()` `IL2026`+`IL3050` üretir); yedi ayarın hepsi kapsanır ve yeni ayar eklendiğinde `Bind`'e de eklenmesi gerektiği koda yazıldı. Sıra bilinçli: `Bind` önce, `configure` sonra — kod yapılandırmayı ezer. `McpOptionsConfigurationBindingTests` gerçek kayıt yolundan (`AddAgentPrism().UseMcp(section)` → `IOptions<T>`) doğrular.

### K-219

**K-219 — `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz**

Faz 20'nin maliyet modeli **token** varsayar ve `runs` tablosuna yazar. Ses ücretlendirmesi karakter (üretim) veya süre (çözüm) bazlıdır; `tool_invocations` hiçbir ölçüm sütunu taşımıyordu, dolayısıyla planın "Migration: Yok" satırı yanlıştı. Eklenenler: `usage_unit`, `usage_quantity`, `usage_estimated`, `cost`, `cost_currency` (Postgres 0015, SqlServer/Sqlite 0003). `pricing_source` **eklenmedi**: ses fiyatının tek kaynağı yapılandırmadır, ayırt edilecek kaynak yoktur. İki birim **toplanmaz** — raporlarda ayrı kalemdir. Ölçümü tool kendisi bildirir (`AgentPrismToolUsage.Report`) ve kayda **çağrı kimliğiyle** bağlanır: `FunctionInvokingChatClient.CurrentContext.CallContent.CallId` tool gövdesinde doludur (ölçüldü), `AIFunctionArguments.Context` ise `null`'dur. Bildirim bağlanamazsa `false` döner ve tool'un işi bozulmaz — gözlemlenebilirlik işlevselliği bozmaz. Fiyat yapılandırma yolu `AgentPrism:Pricing:Voice:{sağlayıcı}:{model}` olduğu için `Voice`, `Currency` gibi **rezerve anahtar** yapıldı: `BindPricing` elle yazılmıştır ve `Pricing`'in her çocuğunu bir sağlayıcı adı sayar.

### K-319

**K-319 — `EvalCaseResult.Scores` ayarlanmamışken (varsayılan `JsonValueKind.Undefined`) SQL Server'a `"[]"` yazılır, `"null"` DEĞİL — `ISJSON` kısıtı bare `null`'ı reddeder**

`SqlEvalStore`'un paylaşılan `RawJson` yardımcı metodu `Undefined` için jenerik olarak `"null"` metnini üretiyordu; bu, PostgreSQL/SQLite'ın `json`/`jsonb` sütunlarında sorunsuzdur (ikisi de bare `null`'ı geçerli kabul eder) ama `eval_case_results.scores` sütununun `CHECK (ISJSON(scores) = 1)` kısıtı bunu REDDEDER. `scores` alanı zaten anlamca bir LİSTEDİR (dokümantasyonu "denetim bazinda skor listesi" der) ve sütunun kendi `DEFAULT` değeri de `N'[]'`dir — düzeltme `RecordCaseResultAsync` çağrı yerinde `Undefined` durumunu `RawJson` yerine doğrudan `"[]"` ile karşılamaktır. `SqlEvalStore.cs` paylaşılan kaynaktır (K-176) ve düzeltme PostgreSQL/SQLite derlemelerini de etkiler; her iki sağlayıcının sözleşme testleri de (844/844, 445/445) regresyonsuz geçti çünkü hiçbir test `Scores`'un tam JSON şeklini doğrulamıyordu.

### K-493

**K-493 — `IRunEventSink` fan-out'u depo başarısından TAM bağımsız; `RunEventWriter.CompleteAsync` artık `IsDisabled` iken erken dönmez (Faz 70, F-115, Açık Sorular 1/2/4)**

Dört karar birlikte: (1) hedef sıcak yolda çalışır, "hızlı ol" sözleşmesiyle — paket içinde kuyruk AÇILMADI, K1'i zorlardı ve tüketicinin bilinçli kararı olmalı. (2) bir hedef ilk hatada `RunEventWriter`'ın var olan `IsDisabled` deseniyle BİREBİR aynı şekilde SADECE O RUN için devre dışı kalır (`bool[]` writer örneğine bağlı, sinke değil) — ikinci bir davranış modeli öğretilmedi. (3) hedef `RunEvent`'in KENDİSİNİ alır, daraltılmış bir DTO değil — `RunEvent` zaten public. (4) **ÖLÇÜLDÜ, koda bakılarak bulundu**: `CompleteAsync`'in üstündeki `if (IsDisabled) return;` koruması, depo DAHA ÖNCE (aynı run içinde) bir yazımda başarısız olduysa kapanış olayının (RunCompleted/RunFailed) HİÇ üretilmemesine yol açıyordu — bu da sağlıklı bir sink'in TERMİNAL olayı asla görememesi anlamına gelirdi (bağımsızlık ilkesinin ihlali). Koruma kaldırıldı; yalnız `_store.CompleteRunAsync` çağrısı `IsDisabled`'a bağlı kalır. Kanıt: `RunEventSinkTests.A_sink_still_receives_every_event_when_the_store_itself_is_disabled`.

### K-188

**K-188 — `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnızca İLK sonuç kümesine bakıyordu; SQL Server'ın iki dallı upsert deseni ikinci kümeye yazabiliyor**

K-177'nin `UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT` deseni, UPDATE 0 satır etkilediğinde (kayıt henüz yoksa) gerçek satırı **ikinci** sonuç kümesine yazar. `ReadSingleAsync` `reader.ReadAsync()`'i yalnızca ilk kümede çağırıyordu — kayıt aslında INSERT edilmiş olsa bile ilk kümenin boş olması `null` döndürüyordu ve çağıran kod "veritabanı sürüm bilgisi döndürmedi" gibi yanlış hatalar fırlatıyordu. Aynı kusur `DbCommand.ExecuteScalarAsync`'in doğal davranışı yüzünden `DbHelpers.ExecuteScalarAsync`'te de vardı: `SqlTraceStore.UpsertTraceAsync` `(Guid)null!` cast'inde `NullReferenceException` fırlatıyor, `SqlExperimentStore.SaveAsync` yeni kaydı "zaten var, düzenlenemez" sanıyordu. Düzeltme: ikisi de artık `NextResultAsync` ile sonraki kümelere düşer, satır (veya skaler değer) bulunana ya da kümeler tükenene kadar. PostgreSQL'in tek ifadelik `ON CONFLICT ... RETURNING` deseni zaten tek kume ürettiği için değişiklik orada zararsızdır (416/416 PostgreSQL testi regresyon olmadan geçti).

### K-300

**K-300 — Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur; plan taslağının "run_events zaten kullanıcı girdisini taşır" iddiası yanlıştı ve düzeltildi (kullanıcı kararı)**

Faz 45 planı ilk turda oturum tabanlı okuma (`ISessionStore`/`ChatHistoryProvider`) ile uygulandı — çekirdek kayıt hattına dokunmamak için bilinçli seçildi. Gerçek bir HTTP testiyle ölçüldü: `AgentEndpoints.AgentRunStream`'de `sessions.SaveSessionAsync(...)` YALNIZ başarı yolunda çağrılıyor; ilk turu başarısız olan bir oturum `ISessionStore`'a hiç yazılmıyor. Bu, planın 🚨 işaretli en önemli DoD maddesini ("başarısız bir çalıştırma terfi edilir, `expectedOutput` boştur") YAPISAL olarak imkânsız kılıyordu. Kullanıcıya iki seçenek sunuldu (çekirdeğe dokun / kapsamı resmen daralt), kullanıcı çekirdeğe dokunmayı seçti: `RunEventWriter.StartAsync` artık bir `query` parametresi alır, `RunRecordingAgent.RunCoreAsync`/`RunCoreStreamingAsync` `messages`'ten ilk `ChatRole.User` mesajını çıkarıp `RunStarted` olayının `Text` alanına yazar. Bu, girdi metninin kalıcılaştığı TEK yerdir ve oturum durumundan bağımsız çalışır — oturumsuz (sessionId'siz) çalıştırmalar da artık terfi edilebilir.

### K-448

**K-448 — Ön uçuş token sayımı için `Microsoft.ML.Tokenizers` + `Microsoft.ML.Tokenizers.Data.O200kBase` AÇIK `PackageReference` ile eklendi (Faz 62 Açık Soru 1, seçenek A); `Microsoft.Bcl.Memory` CVE zorlamasıyla sabitlendi**

Ölçüldü: `TiktokenTokenizer.CreateForModel(...)` veri paketi olmadan `InvalidOperationException` fırlatıyor ("Microsoft.ML.Tokenizers.Data.O200kBase.dll could not be loaded"); K-104'ün "veri paketi gerekmedi" bulgusu yalnız MAF'ın SIKIŞTIRMA için tokenizer'ı KENDİ İÇİNDE çözdüğü yola özgüydü — burada ÇAĞIRAN AgentPrism'in kendisidir. İkinci ölçüm: veri paketi `Microsoft.Bcl.Memory` 9.0.4'ü çekiyor ve NU1903 (yüksek önem, GHSA-73j8-2gch-69rq) `dotnet restore`'u KIRIYORDU. K-007 deseniyle (satır 220, 222) `Microsoft.Bcl.Memory` `AgentPrism.Core.csproj`'da doğrudan `$(MicrosoftExtensionsVersion)`'a (10.0.10) sabitlendi. Sayım tek bir sabit referans model/kodlama (`gpt-4o` / `o200k_base`) kullanır — bağlanan sağlayıcıdan BAĞIMSIZ, çünkü Anthropic ve Google çevrimdışı bir tokenizer paketi yayınlamıyor; sayı YAKLAŞIKTIR.

### K-429

**K-429 — Manuel kabul testi PROTOKOLÜ `manuel-test-kosumu` skill'ine taşındı; `docs/manuel-test/` yalnız spec + koşum kaydı taşır** *(kullanıcı kararı)**

`KOSUM-PLANI.md` (26 KB) ve `KAPANIS-PLANI.md` (138 KB) iki farklı ömürlü şeyi aynı dosyada tutuyordu; ÖLÇÜLDÜ: 164 KB'ın **21 KB'ı** turdan bağımsız protokol (değişmez kurallar, şerit izolasyonu, oturum yordamı, sonuç kaydı biçimi, aile aile kapanış, bitti tanımı), **142 KB'ı (%87)** 2026-08-13 turunun kaydıdır (şerit dağılımı, A–W kusur aileleri, durum tabloları, commit hash'leri). Bir sonraki koşumu açan ajan 164 KB okumak zorundaydı. Protokol `.agents/skills/manuel-test-kosumu/SKILL.md` (13 KB) + `resources/serit-kurulumu.md` (5 KB) oldu — AGENTS.md'nin "tekrarlanan iş akışı skill'dedir" kuralı ve mevcut skill boyut aralığı (4–14 KB) ile tutarlı. Tur kaydı `docs/arsiv/manuel-test-kosum-2026-08/` altına, kardeş `SONUCLAR-*.md` dosyalarının yanına taşındı. Turdan devreden 6 canlı kalem (4 kalıcı `Kaldı` + `MT-UIRUN-019` beklemede + `MT-SKILL-057` hiç koşulmadı) arşive gömülmesin diye `00-INDEKS.md` §7.1'e çıkarıldı.

### K-439

**K-439 — `toolResults` eşleştirmesi bilerek İKİ KEZ yapılır: akış başlamadan önce doğrulama, sonra gerçek mesaj kurulumu**

K-324 aynı fiziksel kısıtı belgeliyordu (SSE başlıkları akış başlamadan ÖNCE gönderilir) ama guard bloğu için. Aynı kısıt `toolResults` için de geçerli: planın istediği `400`/`409` ayrımı (bilinmeyen `callId` / ikinci kez yanıtlanan `callId`) yalnız akış başlamadan ÖNCE bir `ProblemDetails` olarak dönebilir. `ClientToolResultResolver.MatchAsync` bu yüzden `RunAsync`'te (akıştan önce, doğrulama amaçlı) ve `BuildMessagesAsync`'te (akış içinde, gerçek mesajı kurmak için) iki kez çağrılır. Eşleştirmenin kendisi yan etkisiz (denetim izi yazmaz) olduğu için tekrar güvenlidir; yalnız `BuildResponseMessageAsync` (denetim izi + mesaj kurulumu) tek yerde çağrılır. Onaylardan (`ToolApprovalResolver`) farkı budur: bir onay kararı eşleşmezse sessizce atlanır (kararın geç gelmesi zararsızdır), ama yanıtsız kalan bir tool çağrısı modeli tıkar — bu yüzden sert doğrulama gerekiyordu ve tekrar zorunlu hâle geldi.

### K-180

**K-180 — SQL Server'da yoğun yazılan tablolarda birincil anahtar NONCLUSTERED, kümelenmiş indeks zaman sütununda**

SQL Server'ın `uniqueidentifier` karşılaştırması bayt sırasına göre **değildir** (son altı bayt önce karşılaştırılır). Bu yüzden K-015'in zaman sıralı uuid v7 anahtarları SQL Server'da zaman sıralı **görünmez** ve kümelenmiş bir birincil anahtar sayfa bölünmesi üretir — PostgreSQL'de olmayan bir sorun. Çözüm: `runs`, `tool_invocations`, `spans`, `audit_log`, `attachments`, `webhook_deliveries`, `eval_case_results` tablolarında PK `NONCLUSTERED`, kümelenmiş indeks `(zaman_sütunu, id)` üzerinde ve `UNIQUE` (uniquifier oluşmasın diye). Anahtar bilerek dardır (10 + 16 bayt): kümelenmiş anahtar her nonclustered indeks satırında tekrarlanır. Doğal bileşik anahtarı olan tablolar (`run_events`, `conversation_items`, `job_items`) kümelenmiş anahtarlarını korur. Düşük hacimli yapılandırma tablolarında varsayılan (kümelenmiş PK) bırakılır. `SqlServerDialectTests.Yogun_tablolarda_birincil_anahtar_kumelenmemistir` bunu `sys.indexes` üzerinden denetler.

### K-387

**K-387 — `SqlServerTestContext.DisposeAsync` artık test şemasını GERÇEKTEN bırakır (dokümantasyon iddia ediyordu, kod yapmıyordu); deadlock'a çarparsa 5 denemeli backoff ile yeniden dener**

`DisposeAsync`'in XML belgesi "SQL Server'da her test kendi semasını birakir" diyordu ama gövde yalnız `DataSource.DisposeAsync()` çağırıyordu — şema HİÇ silinmiyordu. Tek konteynerde paylaşılan ~340 testlik koşuda yüzlerce şema birikince `sys.tables`/`sys.indexes` katalog taramaları (migration'ların `IF OBJECT_ID(...)` idempotency kontrolleri İÇİN) giderek yavaşladı — ÖLÇÜLDÜ: 422,9 sn (kullanıcı raporu). Düzeltme önce FK kısıtlarını (`sys.foreign_keys` üzerinden), sonra tabloları, sonra şemayı dinamik SQL ile TEK round-trip'te bırakır (`QUOTENAME`, şema adı zaten `SqlIdentifier.RequireSchemaName` ile doğrulanmış). İlk denemede paralel testler `sys.foreign_keys`/`sys.tables` katalog görünümlerinde metadata-kilit çakışmasıyla (hata 1205, "deadlock victim") ARA SIRA başarısız oldu — beklenen bir durumdur, tek seferlik değil; 5 denemeli, artan gecikmeli (`50ms × deneme`) yeniden deneme eklendi. Sonuç: 479/479 yeşil, süre 422,9 sn → ~300 sn (izole ölçüm).

### K-380

**K-380 — `CompiledAgentCache`'in anahtarına `TenantId` eklendi; iki farklı kiracının aynı ad+sürüm+bağımlılık parmak izinde bir tanımı olması artık BİRİNCİ kiracının derlenmiş agent'ını İKİNCİ kiracıya sızdırmaz (manuel kabul testi hazırlığı, kullanıcı talebiyle bulundu)**

Onbellek anahtarı `(Name, Version, DependencyFingerprint)` idi — kiracı boyutu yoktu. Agent adları yalnız kiracı İÇİNDE benzersizdir (`SqlAgentDefinitionStore`); iki farklı kiracının ikisi de `"support"` adında, ikisi de ilk kayıt (sürüm 1), ikisi de skill/çağrılabilir-agent kullanmıyorsa (bağımlılık parmak izi boş) AYNI anahtara düşer. `DefinitionStoreAgentSource`/`CodeAgentSource` artık `ITenantContext` alır (repo genelinde zaten kurulu desen — `InMemoryAgentDefinitionStore` vb. aynı bağımlılığı taşır) ve `_tenantContext.TenantId`'yi anahtara ekler. Kod-tanımlı agent'lar için de gereklidir: `AddVectorSearchTool` derleme anındaki AMBIYANS kiracısını (`_tenantContext?.TenantId ?? definition.TenantId`) `search_knowledge` tool'una bindirir — kod tanımlarının kendi `TenantId`'si olmadığından, kiracı boyutu olmadan ilk derleyen kiracının bindirmesi TÜM kiracılara yayılırdı.

### K-043

**K-043 — Konuşma ile oturum aynı şeydir; `POST /v1/conversations` bir kimlik rezervasyonudur**

İkinci bir kimlik uzayı açmak (konuşma ≠ oturum) aynı sohbetin `/v1/conversations/{id}/items` ile `/api/sessions/{id}` üzerinden **farklı** görünmesine yol açardı; ayrıca yeni bir depo soyutlaması ve migration gerektirirdi. Konuşma kimliği doğrudan oturum kimliğidir — `/v1/responses` içindeki `conversation` alanı zaten öyle çalışıyordu. Oluşturma anında hangi agent'ın kullanılacağı bilinmediği, oturum ise bir agent'a bağlı olduğu için `POST /v1/conversations` yalnız kimlik üretir; oturum ilk `/v1/responses` çağrısında doğar. Tek davranış farkı: kullanılmamış bir konuşma `404` değil **boş** döner. `POST /{id}/items` desteklenmez — geçmişe doğrudan yazmak agent bağlantısı ister; doğru yol bir `/v1/responses` çağrısıdır. Doğrulandı: `openai` 2.52.0 ile `create` → `responses.create(conversation=…)` → `items.list` → `retrieve` → `delete` zincirinin tamamı çalışıyor, tool çağrıları `function_call`/`function_call_output` öğeleri olarak görünüyor.

### K-519

**K-519 — Doküman sitesinin rengi KAPALI bir token kümesidir ve kontrast derleme anında hesaplanır (Faz 76)**

Ölçüm fazın teşhisini düzeltti: site "stok tema" değildi, `site.css` zaten 163 satır ve 21 sınıflık bir ürün katmanı taşıyordu — kimlik vardı, uygulanmamıştı. Karar o katmanı kapatır: her renk `--ap-*` olarak **iki tema bloğunda birden** bildirilir, Starlight'ın semantik özellikleri (`--sl-color-bg/text/accent/hairline`, ve metin boyayan `gray-2`/`gray-3`) bu token'lara BAĞLANIR, ve `check-content.mjs` token'ları ayrıştırıp 19 çifti WCAG AA'ya karşı hesaplar (metin 4.5:1, metin dışı 1.4.11 için 3:1). Kapı tarayıcı istemez ve `check-content.mjs`'in "koddan ölç" desenidir. Üç delik kapatıldı: çifti olmayan token reddedilir, `:root` dışında bildirilen renk reddedilir, kimsenin okumadığı token reddedilir — ve çözülemeyen bir token iddiayı ATLAMAZ, hata verir. Ölçülen taban: metin 5,47:1, metin dışı 3,46:1. Gri skalayı bağlamamak ölçülebilir bir tehlikeydi: `--ap-surface-raised` bir ton açılınca kapı yeşil kalıyor ama kenar çubuğu metni 4,26:1'e düşüyordu.

### K-468

**K-468 — Dört sağlayıcı paketinin (`OpenAI`/`Anthropic`/`Google`/`Azure`) kiracı-kimlik-bilgisi başına istemci önbelleği TEK bir paylaşılan `AgentPrism.Core.ProviderCredentialClientCache<TFactory>` sınıfıyla yapılır; dört ayrı kopya YAZILMADI (Faz 65)**

Ölçüldü: dört sağlayıcının SDK istemcisi de (`OpenAIClient`/`AnthropicClient`/`Client`/`AzureOpenAIClient`) kurulum anında bir kez inşa edilip paylaşılıyor — bir kiracı kimlik bilgisi bu paylaşılan örneği kullanamaz, yeni bir istemci gerekir. Mimari kural (K-176'nın ruhu) dört paketin birbirini GÖREMEMESİ; ama hepsi `AgentPrism.Core`'a bağımlı, bu yüzden paylaşılan yardımcı ORAYA konuldu — `OpenAINamedChatClientFactoryCache`'in (adlandırılmış OpenAI-uyumlu sağlayıcılar için zaten var olan) genellenmiş hâli. Anahtar kiracı kimliği değil, kimlik bilgisinin KENDİSİDİR (`ApiKey`+`Endpoint`); aynı kimlik bilgisini paylaşan iki kiracı aynı önbellek girdisini paylaşır — bu doğrudur, kimlik sınırı kimlik bilgisidir. Önbellek girdileri hiç elden çıkarılmaz (`GoogleChatClientFactory` `IDisposable` olsa bile); kimlik bilgisi sayısı yönetici tarafından kontrol edilen küçük, sınırlı bir kümedir.

### K-191

**K-191 — SQLite'ta uuid BÜYÜK harfle yazılır; `SqliteDialect.AddUuid` özellikle EZİLMEZ**

Faz 24 dokümanının ilk planı (bölüm 24.2) uuid'in "küçük harf, tireli" TEXT olarak saklanmasını öngörüyordu. Ölçüldü: `Microsoft.Data.Sqlite` hem tipsiz (`DbHelpers.Add`'in zorunlu Guid'ler için kullandığı yol) hem `DbType.Guid` ile (taban sınıfın `AddUuid` varsayılanı, nullable Guid'ler için kullanılan yol) BİREBİR AYNI büyük harfli metni yazıyor. `SqliteDialect.AddUuid`'i küçük harfe çevirecek şekilde ezmek bu iki yolu AYRIŞTIRIRDI: aynı mantıksal kimlik (örn. `traces.run_id`) bir tabloda büyük harfle (zorunlu Guid yolu), okuma sorgusunun filtresinde farklı harfle yazılır ve SQLite'ın harf büyüklüğüne duyarlı `BINARY` metin eşitliği (`WHERE`/`JOIN`) SESSİZCE başarısız olurdu — `SqlTraceStore.UpsertTraceAsync` (nullable, `Dialect.AddUuid`) ile `GetTraceByRunAsync` (zorunlu, `DbHelpers.Add`) arasında tam olarak bu kırılma tespit edildi ve `AddUuid` ezilmeyerek çözüldü. Harf büyüklüğü TUTARLI olduğu sürece uuid v7'nin zaman sıralı önekinin sözlüksel sırası bozulmaz; yalnızca PostgreSQL'in küçük harfli gösterimiyle görsel tutarlılık feda edilir.

### K-463

**K-463 — `SessionConversationResolver` (Core) sıradan bir `AgentSession`'ın dahili sohbet geçmişini veri konusu kapsamına ekler; `DataSubjectScope.SessionIds` tek başına bunun için YETERSİZDİR (Faz 64, bağımsız denetim 🔴 #1)**

`faz-denetim`'in bağımsız denetçisi ölçtü: `SqlChatHistoryProvider`'ın yazdığı `conversation_id`, `conversations`/`conversation_items` tablolarına hiçbir `session_id` sütunu olmadığı için SQL'den erişilemez bir şekilde, oturumun kendi `state` alanı (`ChatHistoryState`, bir state bag girdisi) içine gömülüdür — bir tüketicinin `IDataSubjectResolver`'ı bunu asla bilemez. `SessionConversationResolver`, `ConversationBranchService`'in ZATEN kullandığı aynı yöntemi (agent üzerinden oturumu geri yükle, `StateBag.TryGetValue<ChatHistoryState>` ile oku) tekrar kullanır; `DataSubjectEndpoints` her iki uçta da `resolver.ResolveAsync`'ten SONRA bunu otomatik çağırıp sonucu `DataSubjectScope.ConversationIds`'e ekler — tüketici ek kod yazmaz. Bir oturumun agent'ı artık çözülemiyorsa veya durumu geri yüklenemiyorsa o oturum atlanır ve loglanır (istek düşmez); K1'in "gözlemlenebilirlik işlevi bozmaz" ilkesiyle aynı yönde.

### K-412

**K-412 — Doküman dizin bütçesi `docs/arsiv/` ve `docs/manuel-test/kosumlar/` HARİÇ ölçülür** *(kullanıcı kararı)**

Faz 58'in planı `docs/` için 4 MB üst sınır öneriyordu; gerekçesi "58.1 ~600 KB siler" idi. Ölçüm bunu çürüttü: 58.1 ve 58.3 içeriği SİLMEZ, TAŞIR (`AGENTS.md`'nin "içerik silinmez, taşınır" kuralı) ve her iki hedef de `docs/` içindedir — net etki yalnız −12 KB (`PROMPT.md`). `docs/` 5,8 MB'de kalıyordu; 4 MB ulaşılamazdı. Kullanıcıya üç seçenek ölçümle sunuldu; seçilen: sınır arşiv ve koşum kaydı HARİÇ ölçülür. Gerekçe yapısaldır — bu ikisi sayılsaydı arşive taşımak sayacı DEĞİŞTİRMEZDİ ve disiplinin istediği davranış (sıcak yoldan çıkarma) ödüllendirilmezdi; böyle bir sınır yalnızca SİLMEYE zorlardı, yani "taşınır" kuralının tam tersine. Hariç tutunca arşive taşımak sayacı gerçekten düşürür. Uygulama: `scripts/dokuman-bakim.py` içinde `HARIC` demeti + `DIZIN_BUTCESI` sözlüğü. Ölçülen: denetlenen `docs/` 4.212.753 B / 5.000.000 B sınır (%16 boş); denetim dışı yığın 1.273.537 B. Sınırlar tahminle değil ölçülen değere %15 boşluk eklenerek kondu.

### K-222

**K-222 — Konuşma katmanı Seçenek A ile ve `AgentPrism.Core`'da**

**Seçenek A (boru hattı):** ses, sağlayıcının gerçek zamanlı API'sine vekillenmez; `AIAgent.RunStreamingAsync` çağrılır. Bedel gecikmedir, karşılığı çalıştırma kaydı, span, maliyet, tool onayı, kiracı ve kotanın ses turunda da **aynen** işlemesidir — ölçüldü: gerçek bir konuşma turu `Completed` bir `runs` satırı ve 413 token üretti. AgentPrism bir kontrol düzlemidir; bu vaatleri ses için askıya alamaz. **Paket yeri:** faz planı katmanı `AgentPrism.Voice`'a koyuyordu; boru hattı yalnız `ISpeechTranscriber`/`ISpeechSynthesizer` soyutlamalarını kullandığı için oraya konsaydı (a) `AgentPrism.AspNetCore` konuşma ucunu sunmak için `AgentPrism.Voice`'a referans vermek zorunda kalır (paket yönü kuralı bunu yasaklar), (b) kendi çözüm/sentez uygulamasını kaydeden tüketici kullanmadığı ElevenLabs paketini kurmak zorunda kalırdı. Sürücü `Core/Voice/`, sözleşmeler `Abstractions/Voice/` (K-174 deseni), uç `AspNetCore/Voice/`. `AgentPrism.Voice` bu fazda hiç değişmedi.

### K-421

**K-421 — Public API takibi (`EnablePublicApiTracking`) Faz 60'ta, yayın kararından (Faz 7, K-068) bağımsız olarak açıldı** *(kullanıcı kararı)**

K-016'nın "Faz 7'ye ertelendi" gerekçesi geçerliliğini yitirdi: API yüzeyi artık hızla değişmiyor, 59 faz boyunca kayıtsız birikmişti (ölçüldü: 5.052 `RS0016` — sonra kod büyümesiyle 6.792'ye çıktı). Kullanıcı kararı: kapı yayından ÖNCE, bugün açılsın — "bugün bedava, yayından sonra pahalı" ilkesi K-160'ta zaten Faz 21 için uygulanmıştı. Uygulama: `Directory.Build.props`'ta `EnablePublicApiTracking=true`, `NoWarn` koşullu satırı silindi; 17 paketin `PublicAPI.Unshipped.txt` dosyaları `dotnet format analyzers --diagnostics RS0016` ile (13 iteratif koşum) dolduruldu, `Shipped.txt` boş kaldı. `TreatWarningsAsErrors` zaten `true`; RS tanıları artık derlemeyi kırar. Kanıt: `IAgentPrismBuilder`'a kayıtsız bir metot eklendiğinde derleme `error RS0016` ile kırıldı ve üyeyi adıyla söyledi (`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`, `MT-PKG-091`).

### K-415

**K-415 — Ürün dokümantasyonu ayrı bir Astro Starlight sitesindedir (`docs-site/`), İngilizce'dir ve `farukatasoy.github.io/AgentPrism` adresinde yayınlanır** *(kullanıcı kararı)**

Kullanıcıya dönük doküman bugüne kadar `README.md` + 18 paket README + `docs/MIMARI.md` ile ~2.500 satırdı; geri kalan ~99.500 satır Türkçe geliştirme günlüğüydü ve dış okuyucu için okunamazdı. Site altyapısı sıfırdı (repo genelinde DocFX/Docusaurus/MkDocs/Pages izi yoktu). Dil kararı K-232 ile aynı gerekçeye dayanır: paket uluslararası yayınlanır. `docs/` Türkçe kalır — iki karar bağımsızdır. Ad ayrımı bilinçlidir: `docs/` geliştirme günlüğü, `docs-site/` ürün dokümantasyonu; kural `AGENTS.md`'ye yazıldı. Site `dotnet build`'e BAĞLANMAZ ve pakete girmez; ayrı bir CI job'ıdır (`pages`). 🚨 Astro 7 **Node 22.12+** ister — arayüzün Vite 7'si 20.19 ile yetiniyordu, iki hat artık 22'de birleşti. Astro 5 seçilmedi: `npm audit` 5 açık (3 düşük, 2 yüksek — XSS + SSRF) bildirdi ve yayınlanan bir sitede kabul edilemezdi; Astro 7.2.2 + Starlight 0.41.7 ile **0 açık**.

### K-283

**K-283 — Görünmeyen bir oturum YOK sayılır; "başkasının oturumu" reddi kaldırıldı**

Ölçüldü (Faz 41): `VoiceConversationEndpoint.OwnsSessionAsync` `record is null || record.TenantId == tenantId` kuralıyla çalışıyordu ve **kiracı körü bir depo** varsayıyordu — başka bir kiracının oturumunu okuyup "başkasının" diye reddediyordu. K-277'den sonra `ISessionStore.GetAsync` kiracıyla sınırlıdır ve o kayıt `null` döner; bağlantı artık reddedilmez, kendi kiracısında **taze** bir oturum açar ve diğerinin kaydına hiç dokunulmaz. Bu bir zayıflama **değil**, güçlenmedir: eski davranış bir **varlık kâhiniydi** — bir kiracı, bir oturum kimliğinin başka bir kiracıda var olup olmadığını hata koduyla ölçebiliyordu. Yalıtım korunur (satırlar `(tenant_id, id)` ile ayrıdır, K-278), sızdırılan bilgi azalır. `OwnsSessionAsync` ve `OpenAIConversationsEndpoints.IsOwnedByTenant` derinlemesine savunma olarak yerinde bırakıldı: tüketici kiracı körü bir `ISessionStore` kaydederse denetim yine devrededir. Test `Baska_kiracinin_oturumu_REDDEDILIR` → `Baska_kiracinin_oturumuna_ERISILEMEZ` oldu ve artık mekanizmayı değil **yalıtımı** doğrular.

### K-186

**K-186 — SQL Server sözleşme testleri `azure-sql-edge` (arm64) ile doğrulandı; gerçek `mssql/server` hâlâ koşturulamadı** *(kullanıcı kararı)**

Geliştirme makinesinde (Apple Silicon, Docker Desktop) amd64 emülasyonu (Rosetta) kapalı kaldığı için `mcr.microsoft.com/mssql/server` yine başlatılamadı. Kullanıcı, Faz 24'e geçmeden önce paylaşım modelini doğrulamak amacıyla arm64 native `mcr.microsoft.com/azure-sql-edge` imajıyla 204 sözleşme testinin tamamının koşturulmasını onayladı. `SqlServerFixture` geçici olarak bu imaja yönlendirildi, testler koşturuldu, sonra **orijinal `mssql/server` yapılandırmasına geri alındı** — CI ve gerçek doğrulama hâlâ gerçek SQL Server'ı hedefler. Bu koşu üç gerçek üretim hatası buldu (K-187, K-188, K-189); yani paylaşım modelinin arm64 üzerinde bile doğrulanmamış olması gerçek riskti. `azure-sql-edge` "gerçek SQL Server'ı kanıtlamaz" (bkz. `docs/hafiza/sql-saglayicilari.md`) — T-SQL yüzeyi neredeyse özdeş olsa da motor farklıdır.

### K-413

**K-413 — `docs/YOL-HARITASI.md` ÜRETİLEN dosyadır; kaynak her fazın kendi `> **Durum:**` satırıdır**

Faz 58'in DoD'si "README yol haritası Faz 0–59'u tek tek listeliyor" istiyordu. README'nin tablosu Faz 21–56'yı üç özet satırında topluyordu; tek tek açmak ~36 satır (~4 KB) ekler ve README'yi 20 KB bütçesinden taşırırdı. Planın risk tablosu kaçış yolunu zaten yazmıştı ("gerekirse yol haritası ayrı dosyaya taşınır"). Bir adım ileri gidildi: dosya ELLE YAZILMAZ, `scripts/dokuman-bakim.py` tarafından `docs/NN-*.md` başlıklarından ve `Durum:` satırlarından üretilir — `KARARLAR-INDEKS.md`'nin deseni (K-214). Gerekçe: Faz 58'in düzelttiği kaymaların büyük kısmı ("Faz dokümanları (00–32)", "sekiz ekran", "3355 test") elle bakılan sayıların bayatlamasıydı; yol haritası da aynı sınıftaydı ve aynı şekilde bayatlayacaktı. Üretilen dosyada bir faz durumu yanlışsa düzeltilecek yer O FAZIN DOKÜMANIDIR; dosyayı elle düzenlemek bir sonraki üretimde geri alınır. README dalga özeti + işaret taşır (16.987 B, %15 boş); `MIMARI.md` de bu dosyaya yönlendirir.

### K-416

**K-416 — API referansı DocFX'in `outputFormat: markdown` çıktısından üretilir ve Starlight içine gömülür; DocFX'in kendi HTML sitesi kullanılmadı**

Faz 59.0 spike'ı ölçtü: DocFX 2.78.5'in **assembly + XML modu** 17 pakette MSBuild'siz çalışıyor (588 public tip, 0 hata, ~7 sn) — plandaki .NET 10 SDK riski böylece ortadan kalktı. İki çıktı biçimi karşılaştırıldı. `apiPage`/HTML: ikinci bir tema, ikinci bir arama indeksi. `markdown`: tek site, tek tema, Pagefind referansı da indeksler. Markdown'ın tek kusuru ölçüldü — DocFX prose içindeki `<see cref>`'leri **ham `<xref>` etiketi** olarak bırakıyor: 590 sayfanın 374'ünde 1351 oluşum, ekranda görünmez oluyorlar. `docs-site/scripts/build-api-reference.mjs` (~250 satır) bunları çözer; repo'nun "kütüphane yerine elle yaz" emsalini izler (K-045, K-228). 🚨 Çözücünün değişmez kuralı: **URL uydurmaz**. İç uid yalnız sayfası varsa, dış uid yalnız DocFX'in AYNI derlemede kendisi bağladığı bir tipse bağlanır; kalan 76'sı bağlantı yerine `<code>` olur — bağlantı kaybı kırık bağlantıdan iyidir.

### K-467

**K-467 — Kiracı egress politikası kontrolü, kimlik bilgisi çözümlemesinden ÖNCE ve TEK bir noktada (`ModelProviderRegistry.CreateChatClientAsync`) çalışır — bu nokta hem gerçek `run` derlemesini hem `AgentDefinitionValidator`'ın ön-uçuş kontrolünü kapsar (Faz 65)**

`AgentDefinitionCompiler.Compile`'in `_models.CreateChatClient(binding)`'i TEK çağırdığı nokta olduğu kod okumasıyla doğrulandı (`AgentDefinitionCompiler.cs:327`) — bu hem gerçek `run`'ın derleme adımını (`CompiledAgentCache` üzerinden) hem `AgentDefinitionValidator.CheckModel`'in doğrudan çağrısını besliyor. Egress/kimlik bilgisi mantığını BURAYA (tek nokta) değil de HTTP uç katmanına veya `AgentDefinitionCompiler`'a dağıtmak, ikinci bir giriş yolunun (ör. doğrudan `IModelProviderRegistry` kullanan bir tüketici kodu) kontrolü atlamasına izin verirdi. Sıra: (0) egress — izinsiz sağlayıcı için anahtar aramak "olmaması gereken bir yola girmektir"; (1) kiracının `binding`'i; (2) `null` (global, K1). Kayıt VAR ama değeri YOKSA (yapılandırma anahtarı hiç ayarlanmamış) global anahtara SESSİZCE düşülmez — anlaşılır `AgentPrismException` fırlatılır.

### K-424

**K-424 — `AgentPrism.Generators` ve `AgentPrism.Templates` public API takibinden yeni bir `AgentPrismPublicApiTrackingEnabled` MSBuild özelliğiyle hariç tutuldu**

Faz 60 planı bu iki paketi "dosya eklenmez" diyerek hariç tutmayı öngörüyordu, ama `src/Directory.Build.props`'taki `Microsoft.CodeAnalysis.PublicApiAnalyzers` referansı KOŞULSUZDU — dosya eklenmese bile analyzer ikisinde de çalışıyordu. Ölçüldü: `Generators` gerçekten `RS0016` üretiyordu (3 üye × 2); `dotnet format analyzers` tüm çözümü tararken bu iki projede "Adding additional documents is not supported" (`System.NotSupportedException`) ile ÇÖKTÜ — `MSBuildWorkspace` eksik `PublicAPI.Unshipped.txt` dosyasını yeni belge olarak ekleyemiyor. Çözüm: `AgentPrismPublicApiTrackingEnabled` (varsayılan `true`) özelliği eklendi, iki paket kendi `csproj`'unda `false` yazar; analyzer paket referansı bu özelliğe koşullandı. Gerekçe (Faz 60 planının açık sorusu §1, seçenek B): ikisinin de "API"si tüketici yüzeyi değildir — `Generators` tanı/üretilen kod çıktısıdır, `Templates` `dotnet new` içeriğidir, ikisi de derlenip paketlenen bir kütüphane değildir.

### K-275

**K-275 — On bir ham `Task<IResult>` ucunun tamamı yol B (`.Produces`/`.ProducesProblem` üstverisi) ile belgelendi; hiçbiri yol A'ya (`Results<...>` imza değişikliği) taşınmadı**

`docs/arsiv/fazlar/40-OPENAPI-YAYINI.md` bölüm 40.2'nin kuralı: dönüş kümesi sonlu ve tipliyse A, akışlı/ikiliyse B. On bir ucun TAMAMI incelendiğinde: beşi (`AgentPrismRunAgent`, üç workflow ucu, indirme) başarı yanıtında SAADECE SSE/ikili döndürüyor — hiçbir zaman tipli JSON dönmüyor; altısı (`/v1/responses`, `/v1/chat/completions`, dört conversations ucu) `Results.Json(...)` ile ÖZEL `JsonSerializerOptions` (`OpenAICompatSupport.JsonOptions`, `DefaultIgnoreCondition = WhenWritingNull`) kullanıyor — `TypedResults.Ok<T>`'a geçmek uygulamanın varsayılan JSON seçeneklerini kullanır (null alanlar artık dizilir) ve OpenAI istemci SDK'larıyla tel uyumluluğunu bilerek bozardı. Sonuç: A seçeneği hiçbir ucun gerçek davranışına uymuyordu; hepsi B.

### K-373

**K-373 — Kanarya kuralı yalnızca İKİ kollu deneylerde tanımlanabilir; planın "kalan kollar kontrol sayılır" (çoğul) ifadesi UYGULANMADI (Faz 56, plandan sapma)**

N>2 kollu bir deneyde kanarya ağırlığı arttıkça geri kalan ağırlığın birden fazla kontrol koluna ORANTILI dağıtılması gerekirdi; bu, `ExperimentAssignmentResolver`'ın kova aralıklarını her ramp adımında TÜM kontrol kolları için yeniden hesaplamayı gerektirir ve "var olan oturumlar kolunu değiştirmez" garantisini (56.4) yalnız kanarya-kontrol sınırı için değil, kontrol kolları ARASINDAKİ sınır için de kanıtlamayı ister — bu ikinci kanıt genel halde YOKTUR (bir kontrol kolunun payı küçülürken sınırın hangi tarafında hangi oturumların kaldığı deterministik değildir). İki kollu kısıt bu sorunu yapısal olarak ortadan kaldırır: kontrol tek koldur, kanarya-kontrol sınırı DIŞINDA hiçbir sınır yoktur. `ExperimentEndpoints.SetCanaryAsync` `experiment.Variants.Count != 2` ise 400 döner.

### K-521

**K-521 — Kenar çubuğu `src/sidebar.mjs`'te tek kaynaktır; üç tüketici onu okur (Faz 76)**

Bölüm başına `og:image` bir eşleme ister ve o eşleme kenar çubuğu bölümleriyle senkron kalmalıdır. Kenar çubuğu `astro.config.mjs` içinde gömülü kalsaydı eşleme ikinci bir kopya olurdu ve yeni bir bölüm sessizce başka bir bölümün önizlemesini miras alırdı. Şimdi `astro.config.mjs` gezinmeyi, `starlightRouteData.mjs` paylaşım görselini, `check-content.mjs` ise hem erişilebilirliği hem görsel kapsamını AYNI yapıdan okur — ve `check-content.mjs`'in erişilebilirlik denetimi metin araması (`astroConfig.includes("slug: '...'")`) olmaktan çıkıp yapı okumasına dönüştü. 🚨 Modül üretilen `src/generated/*-sidebar.json`'ı **koşullu** okur: statik import, kapıyı temiz bir checkout'ta `ERR_MODULE_NOT_FOUND` ile düşürüyordu ve yerelde yeşil görünüyordu (denetim buldu). Bölüm başına görsel bileşen geçersiz kılma İSTEMEZ — Starlight'ın `routeMiddleware`'i yeter; aynı şekilde prizma işareti `logo:` yapılandırmasıyla gelir. Faz sıfır bileşen geçersiz kıldı.

### K-395

**K-395 — `AgentPrismEndpointFilter`: `requireBearerToken:false` gruplarına (eşlenmemiş `api/*` yolları, gerçek zamanlı ses ucu) AgentPrism'in kendi statik `AuthToken`'ıyla eşleşen bir başlık artık REDDEDİLMİYOR — başlık YOKMUŞ gibi nötr davranılıyor (HATA-S1-014, manuel kabul testi S1)**

`MT-MM-031`/`063` ölçtü: `requireBearerToken:false` kurulan bir grupta (kurucu `_authToken`'ı bilerek `null` yapar) geçerli statik bearer token'lı bir istek `401` alıyordu — `IApiKeyStore`'da aranıp bulunamadığı için (statik token bir API anahtarı DEĞİLDİR) genel `Unauthorized()`'a düşüyordu; doğru token, yanlış token ve HİÇ token arasında davranış farkı YOKTU, teşhisi yanıltıyordu ("bu uç yok" yerine "kimlik doğrulanamadı" görünüyordu). Düzeltme `_staticAuthToken` (her zaman `options.AuthToken`, `requireBearerToken`'dan bağımsız) ve `_requireBearerToken` alanlarını ayırır: `!_requireBearerToken && header eşleşiyorsa` istek NÖTR kabul edilir (ne ekstra yetki verir ne reddeder) — ucun kendi mantığına (varsa 404/400) ulaşılır. `_authToken`'ın (yalnız `requireBearerToken:true`'ta dolu) davranışı DEĞİŞMEDİ.

### K-374

**K-374 — `ExperimentAssignmentResolver.SelectVariant`, kanarya kuralı tanımlıyken kanarya kolunu HER ZAMAN `[0, ağırlık)` aralığına yerleştirir — bu, `Experiment.Variants`'ın FİZİKSEL sırasından bağımsızdır (Faz 56)**

İlk uygulama kova aralıklarını `Variants` listesindeki sıraya göre hesaplıyordu (mevcut, Faz 19'dan kalan davranış); bir deney `[control, canary]` sırasıyla oluşturulup ramp ile ağırlık artınca kanarya kolunun aralığı `[95,100)`'den `[75,100)`'e KAYIYOR, control-atanmış bazı oturumlar sohbetin ORTASINDA kanaryaya GEÇİYORDU — tam olarak 56.4'ün yasakladığı davranış. `CanaryEvaluationServiceTests.Kademeli_artirma_...` ve `ExperimentAssignmentResolverTests.Kanarya_agirligi_buyudukce_...` bunu YAKALADI (ilk yazılan haliyle test kırmızıydı). Düzeltme: `SelectVariant` özel bir `OrderForAssignment` yardımcısıyla, kanarya kuralı varsa kanarya kolunu HER ZAMAN ilk sıraya alır; kural yoksa (Faz 19 A/B deneyleri) davranış DEĞİŞMEZ. Kanarya ağırlığı yalnız ARTTIĞI için `[0, ağırlık)` aralığı yalnız BÜYÜR, hiçbir zaman küçülmez — önceden kanaryaya düşen bir anahtar hep kanaryada kalır.

### K-470

**K-470 — `FallbackChatClient` bir fallback'i tetiklediğinde kiracı/egress'ten HABERSİZ senkron `CreateChatClient` metot grubunu değil, `ModelProviderRegistry.CreateChatClientAsync`'i çağırır (Faz 65, bağımsız denetim 🔴 #1)**

`faz-denetim`'in bağımsız denetçisi ölçtü: `_buildClient` alanı `Func<ModelBinding, IChatClient>` idi ve `BuildPipeline`'daki `resolveFallback` parametresi eklenmeden önce `FallbackChatClient` her zaman senkron `CreateChatClient`'ı (kiracı credential'ı ve egress kontrolü YOK, her zaman global) çağırıyordu — birincil sağlayıcı devre dışı kalıp bir fallback tetiklendiğinde kiracının kendi kimlik bilgisi asla kullanılmıyordu ve egress politikası fallback sağlayıcısına hiç uygulanmıyordu. Düzeltme: `_buildClient` imzası `Func<ModelBinding, CancellationToken, ValueTask<IChatClient>>` oldu, `ResolveFallbackClientAsync` artık `CreateChatClientAsync`'i çağırır. Kanıt: `A_triggered_fallback_resolves_its_own_tenant_credential_through_the_async_entry_point`, `Egress_policy_rejects_a_fallback_provider_not_in_the_allowed_list` (`ModelProviderRegistryTenantCredentialTests.cs`).

### K-449

**K-449 — Yedek zincirinde retryable OLMAYAN bir hata (401/403, iptal) HANGİ HALKADA olursa olsun ANINDA ve SARMALANMADAN fırlatılır; zincir yalnız TÜM halkalar retryable hatayla tükendiğinde `AgentPrismProviderUnavailableException` ile "ilk hata" özetine sarılır**

İlk tasarım "son halkada asla yakalama" kuralıyla `ChainExhausted`'ı ölü koda düşürüyordu (yakalama koşulu `index < _fallbacks.Count` idi — son halkada hep yanlış, o yüzden istisna hep ÇIPLAK kaçıyordu). Ölçüldü ve düzeltildi: kural artık üç aşamalı `catch` zinciri — `OperationCanceledException` her zaman çıplak; `!IsRetryable(ex)` (ör. 401/403) her zaman çıplak (HANGİ halkada olursa olsun — bir yedeğin YANLIŞ YAPILANDIRILMIŞ anahtarını "birincil başarısız oldu" özetinin ARKASINA GİZLEMEK, anahtarı düzeltmek yerine yanıltıcı bir hata mesajı üretirdi); geri kalan (retryable) durum son halkada `ChainExhausted(firstFailure)`, diğerlerinde sessizce sonraki halkaya geçer. Testle doğrulandı: `Authentication_errors_are_not_retried` (401/403 herhangi bir halkada çıplak kaçar), `Exhausted_chain_throws_the_first_failure_not_the_last`.

### K-204

**K-204 — Anthropic ve Google için RESMİ SDK'lar kullanıldı, topluluk paketleri değil** *(kullanıcı kararı)**

Faz 26 planı topluluk paketlerini (`Anthropic.SDK` 5.10.0, `Google_GenerativeAI` 3.6.7) varsayıyordu. Ölçüldü: ikisinin de **resmî birinci taraf** karşılığı var — `Anthropic` 12.39.0 (sahip: Anthropic, MIT, 2,8M indirme) ve `Google.GenAI` 1.16.0 (sahip: Google LLC, Apache-2.0, doğrulanmış, 2,3M indirme). İkisi de kendi `Microsoft.Extensions.AI.AsIChatClient` adaptörünü taşır; bu yüzden mesaj eşlemesi, akış, tool çağrısı ve kullanım sayaçları AgentPrism'de yazılmadı ve faz "büyük iş" senaryosuna girmedi. İkisi de `IsAotCompatible=true` altında sıfır uyarı derledi. `Microsoft.Extensions.AI.Anthropic` / `.Google` paketleri **yoktur** (doğrulandı); resmî MEAI sağlayıcısı yalnız OpenAI içindir. Yayınlanan bir NuGet paketi bağımlılığını tüketiciye geçirir; tek bakımcılı bir pakete milyonlarca tüketiciyi bağlamak kabul edilemez bir uzun vadeli risktir.

### K-260

**K-260 — `MaxRows` kiracı genelinde uygulanır, kiracı başına DEĞİL (Faz 36 planının Açık Soru 3'ünden sapma)**

Plan "A: politikanın kiracı kapsamında" öneriyordu ve sorgunun `tenant_id` filtresi taşımasını bekliyordu. İnceleme gösterdi ki `RetentionTargetRegistry`'nin ÜÇ mevcut şablonu (say/oku/sil, Faz 25) hiçbirinde `tenant_id` filtresi YOKTUR — yaş bazlı silme bugün zaten kiracı genelinde çalışır; tenant yalnız HANGİ POLİTİKANIN uygulanacağını seçer (`RetentionPolicyResolver`), silinecek SATIRLARI değil. `MaxRows`'u kiracıya özel yapmak, iki eşik mekanizması arasında (yaş vs hacim) asimetri yaratır ve `RetentionTargetRegistry`'nin TÜM şablonlarına (3 eski + 1 yeni, K-198) `tenant_id` eklemeyi gerektirirdi — bu, "public API büyümüyor" sınırını aşan bir kapsam genişlemesidir. Davranış eşitliği (`MaxAgeDays` ile `MaxRows` aynı kapsamda çalışır) kullanıcı şaşkınlığını en aza indirir.

### K-044

**K-044 — Çalıştırma kimliğini çağıran üretir (`AgentPrismRunOptions`)** *(kullanıcı kararı)**

Akışlı bir uç, çalıştırma kimliğini **ilk çerçeveden önce** bilmek zorundadır: istemci akan yanıtı çalıştırma kaydıyla ancak o zaman ilişkilendirebilir. `RunRecordingAgent` kimliği kendi içinde üretiyor ve dışarı bildirmiyordu, dolayısıyla Playground ile Runs ekranı arasında köprü kurulamıyordu. `AgentRunOptions`'tan türeyen küçük bir sınıf (`RunId`) bunu ek bir bildirim kanalı veya ortam durumu olmadan çözer ve `RunStartInfo.RunId` ile aynı yaklaşımı sürdürür. Değerlendirilen alternatifler: `OnRunStarted` geri çağrımı — kimlik ancak ilk olaydan sonra bilinir, akış başlangıcı bir tur gecikir; `AsyncLocal` ortam bağlamı — gizli durum, akışlı numaralandırıcılarda ve paralel çalıştırmalarda hata ayıklaması zor. `Clone()` `RunId`'yi korur; aksi halde ayarları kopyalayan bir ara katman kimliği sessizce düşürür ve istemciye bildirilen kimlik hiçbir kayda karşılık gelmezdi.

### K-066

**K-066 — Skill'lerde script çalıştırma kabul edildi; K2'nin ikinci bilinçli istisnası** *(kullanıcı kararı)**

K2 ("tool'lar yalnız kodda") arayüze erişen birinin sunucuda kod çalıştırmasını engeller. Birinci istisna MCP'ydi (K-058) ve orada süreç **uzakta** çalışıyor. Skill script'lerinde süreç **AgentPrism'in makinesinde** çalışır. Kullanıcı bunu kabul edilebilir buldu. Kabul, kontrolsüz çalıştırma anlamına gelmez; [Faz 11](arsiv/fazlar/11-SKILL-SCRIPT-CALISTIRMA.md) şu koşulları zorunlu kılar: yorumlayıcı beyaz listesi (varsayılan **boş**), skill başına izin kaydı, Faz 6 onay akışı, ayrı OS süreci, zaman aşımı, çıktı sınırı, temiz ortam değişkenleri ve **yazılamazsa çalıştırmayı reddeden** denetim izi. Ayrıca `PlatformIsolationAcknowledged` bayrağı olmadan özellik açılmaz: .NET taşınabilir biçimde ağ/dosya/CPU izolasyonu **sağlayamaz** ve bu sınır gizlenmez, tüketiciye bildirilir. Önkoşul [Faz 9](arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md)'dur.

## Faz 77 bütçe rahatlatması — ikinci tur

### K-325

**K-325 — Engellenen veya maskelenen içerik HİÇBİR yere yazılmaz**

Engellenen içerik tanımı gereği hassastır; onu denetim izine veya olay yüküne yazmak sorunu KALICI hale getirir. `ContentGuardResult` yalnız kural adı ve sebep taşır (eşleşme sayısı ve karakter aralığı da bilerek dışarıda bırakıldı — aralık içeriğin uzunluğunu ve konumunu sızdırır). Yazılanlar: guard adı, kural adı, yön, karar, çalıştırma kimliği, kiracı. `ProblemDetails` de aynı kuraldadır ve yasak sözcüğün KENDİSİ bile sebep metnine yazılmaz (bir kod adı olabilir). K-059'un veritabanı uzantısının aynı yönü; `AuditSecretFilter` zaten aynı yönde çalışıyor. Dört test doğrular, ikisi gerçek koşumda `grep -c` = 0. 🚨 SINIR: maskeleme MODEL SINIRINDA bir kontroldür, bir depolama redaksiyonu DEĞİLDİR — `run_events.RunStarted` kullanıcının ham istemini (Faz 45) ve `run_inputs` ham mesajları (Faz 47) saklar. Guard bunları geriye dönük temizlemez.

### K-408

**K-408 — Kaynak dili sınırı: pakete giren veya çalışma anında çalışan her şey İngilizce'dir; geliştirme aparatı (`docs/`, `.agents/skills/`, `scripts/`) Türkçe kalır** (kullanıcı kararı)**

Kullanıcı isteğiyle (Faz 57) AgentPrism'in kodu — 8.837 satır XML doküman dahil — Türkçe'den İngilizce'ye çevrildi. Kanıt: `GenerateDocumentationFile=true` yüzünden Türkçe XML doküman `.xml` dosyası olarak `.nupkg`'a giriyordu; imza İngilizce, açıklama Türkçe idi (paketin en görünür kalite kusuru). Kural artık kalıcıdır ve `SourceLanguageTests` ile zorlanır (K-410). Kapsam: `src/**` (kod, yorum, XML doküman, `exception`/log/`ProblemDetails` metni), `tests/**`, `samples/**`, migration `.sql` yorumu, `template.json` açıklaması, proje dosyaları. Kapsam dışı bilerek bırakıldı: `locales/tr.ts` (K-228, meşru çeviri sözlüğü), `.agents/skills/**`, `scripts/dokuman-bakim.py`, `docs/**`. Public API yüzeyi zaten temizdi (4.014 `public`/`protected` bildirim tarandı, sıfır Türkçe tip/üye adı) — bu yüzden karar kırıcı değişiklik içermez ve Faz 7 yayınını (K-068) beklemez.

### K-338

**K-338 — MCP/A2A dış yüzeyleri erişim ayarlarını `IApplicationBuilder.Properties` üzerinden `MapAgentPrism`'den DEVRALIR; `MapAgentPrism` önce çağrılmalıdır (Faz 50)**

`MapAgentPrismMcpServer`/`MapAgentPrismA2A` ayrı, opsiyonel `IEndpointRouteBuilder` uzantılarıdır (K1'in "tek giriş noktası" ruhunu korumak için `MapAgentPrism`'in kendisine gömülmediler — MCP/A2A herkesin ihtiyaç duymadığı ek paketler taşır). Ama AYNI üç katmanlı korumayı (loopback + bearer + policy) kullanmaları gerekir ve bu ayarlar (`AgentPrismEndpointOptions`) `MapAgentPrism` içinde yerel olarak kurulur, DI'a kaydedilmez. Çözüm: `MapAgentPrism` sonunda `((IApplicationBuilder)endpoints).Properties["AgentPrism.SharedEndpointOptions"] = options` ile paylaşılan durumu bırakır; `RequireSharedEndpointOptions` bunu geri okur ve `MapAgentPrism` hiç çağrılmadıysa (veya SONRA çağrıldıysa) anlaşılır bir `InvalidOperationException` fırlatır. `IApplicationBuilder.Properties` bu repoda zaten `endpoints is IApplicationBuilder` kontrolüyle (`UseWebSockets`, K-223) tanıdık bir desendir.

### K-376

**K-376 — `CanaryPolicy`'ye Faz 49'unkine benzer ayrı bir `EvaluationWindow` eklenmedi; kanarya kararı `ExperimentVariantResult`'ın TÜM-ZAMANLI (deney başından beri biriken) sonuçlarına dayanır (Faz 56, plandan sapma, Açık Soru 5)**

Planın Açık Soru 5'i "B: ayrı ayar, varsayılanı A'dan (Faz 49) alır" öneriyordu, ama bunu uygulamak `ExperimentResultsQuery`'ye bir zaman aralığı parametresi eklemeyi ve üç SQL sağlayıcısında da `SelectExperimentResults`'ı zaman filtreli hale getirmeyi gerektirirdi — "Planlanan Dosya Listesi" bu değişikliği HİÇ öngörmüyordu. Yapısal olarak da gereksizdir: Faz 49'un `EvaluationWindow`'u sürekli çalışan bir agent'ın SICAK/güncel hata oranını eski verilerden ayırmak içindir; bir kanarya kolu ise deneyin BAŞLANGICINDAN beri var olur ve eski/güncel ayrımı yapılacak bir "geçmiş" taşımaz — ilk çalıştırmasından bugüne TÜM verisi zaten "güncel"dir.

### K-418

**K-418 — 🚨 `AddOpenApi()` ÇIPLAK çağrılmalıdır; yapılandırma `Configure<OpenApiOptions>("v1", ...)` ile AYRI kaydedilir — aksi hâlde ŞEMA XML DOKÜMANI SESSİZCE DÜŞER**

Bu fazda `AddOpenApi(ProductOpenApiDocument.Configure)` yazıldı ve belge **226 şemanın 166'sının** property `description` alanlarını kaybetti (1080 property'nin 819'u belgeliydi → o alanlar boşaldı). Sebep: .NET 10'un XML doküman desteği bir **interceptor**'dır ve yalnız parametresiz `AddOpenApi()` çağrı şeklini yakalar; delege alan aşırı yükleme yakalanmaz ve XML transformer hiç kaydedilmez. 🚨 `OpenApiSnapshotTests` bunu YAKALAYAMAZ — yalnız "dosya host'un ürettiğiyle AYNI mı" der, "DOĞRU mu" demez; snapshot'ı yenilemek gerilemeyi GİZLER (yaşandı, bu fazda). Kalıcı kapı eklendi: `OpenApiDocumentTests.Document_carries_operation_descriptions_and_schema_documentation` — her operasyonda `description` ve 500'den fazla belgelenmiş şema property'si arar; hatalı bağlama ile gerçekten DÜŞTÜĞÜ doğrulandı.

### K-053

**K-053 — `arastirmaci` örnek agent'ı Playground'da tool çağrısıyla birlikte bozuk bırakıldı**

Kanıtlandı: Playground'dan `arastirmaci` (Harness + `get_order_status` tool'u) çalıştırılınca model `finishReason: tool_calls` ile bitiyor ama `Microsoft.Agents.AI.Harness` fonksiyonu hiç çağırmıyor; akış `done` olmadan kesiliyor. Aynı oturumda bir sonraki turda MAF, `tool_calls` içeren asistan mesajını atlayıp yetim bir `tool` mesajı gönderiyor; OpenAI bunu `HTTP 400: messages with role 'tool' must be a response to a preceeding message with 'tool_calls'` ile reddediyor. Karşılaştırma kanıtı: aynı senaryo düz `ChatClientAgent` (`support`) ile 37 SSE olayıyla ve temiz `done` ile tamamlandı — hata AgentPrism'in akış/geçmiş kodunda değil, `Microsoft.Agents.AI.Harness` paketinin onay-bağlama zincirinde. Paket zaten "evaluation purposes only" işaretli (K-020). Kullanıcı kararı: örnek kasıtlı olarak değiştirilmedi; kusur belgelenir, gizlenmez.

### K-249

**K-249 — `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez; `UseOpenAI()`'nin sabit `AgentPrism:Providers:OpenAI` bölümü yalnız KENDİSİ için geçerlidir**

İlk taslak `OpenAIModelProvider.BuildConfigurationDiagnostic`'in `OpenAIProviderOptions.SectionName` sabitini HER iki kayıt yolunda da kullanmasıydı. Yanlıştı: `UseOpenAICompatible(ad, Action<>)` yapılandırmayı KODDA alır (`o.ApiKey = configuration["OpenRouter:ApiKey"]` gibi rastgele bir kaynaktan), sabit bir bölüm yolu yoktur — sabit bir anahtar adı raporlamak kullanıcıyı yanlış yapılandırma anahtarına yönlendirirdi. Çözüm: `OpenAIModelProvider` kurucusuna `string? configurationSectionKey` eklendi (varsayılan `OpenAIProviderOptions.SectionName`, yani `UseOpenAI()` değişmeden çalışır); `UseOpenAICompatible()` acıkca `null` geçer ve teşhis raporunda o saglayıcı için hiçbir satır görünmez.

### K-365

**K-365 — Heartbeat/oksuz-kapama sorguları dizi parametresi (`WHERE id IN (@array)`) KULLANMAZ; tekil `UPDATE` döngüsü ve alt-sorgulu tek `UPDATE` tercih edildi (Faz 54)**

`AddUuidArray` SQL Server/SQLite'ta `System.Text.Json` ile diziyi KÜÇÜK harfli JSON'a serileştirir; `runs.id` ise `DbHelpers.Add` ile BÜYÜK harfli yazılır (K-191). İkisi karışırsa SQLite'ın harf-duyarlı metin eşleşmesi SESSİZCE sıfır satır günceller — tam da K-191'in uyardığı tuzak, bu kez dizi yönünde. `TouchHeartbeatAsync` bu riski almamak için `runIds` üzerinde DÖNGÜYLE tekil `UPDATE ... WHERE id = @id` çalıştırır (bu süreçteki eşzamanlı çalıştırma sayısı küçüktür, N ayrı sorgu kabul edilebilir). `ClaimOrphanedRunsAsync` ise `id` EŞİTLİĞİNE hiç ihtiyaç duymaz — adayları `WHERE id IN (SELECT id FROM runs WHERE status=Running AND COALESCE(heartbeat_at, started_at) < @stale_before ORDER BY … LIMIT @max)` alt sorgusuyla seçer; bu salt-okunur bir alt sorgudur, SQLite/SQL Server'ın veri-değiştiren CTE sınırına (bkz. `docs/hafiza/sql-saglayicilari.md`) takılmaz.

### K-192

**K-192 — SQLite migration kilidi sidecar dosya kilididir, `BEGIN IMMEDIATE` tüm migration süresince açık TUTULMAZ**

SQLite'ta `pg_advisory_lock`/`sp_getapplock` karşılığı yoktur (bölüm 24.3'ün öngördüğü gibi "dosya kilidi düşünülmeli"). Ölçüldü: `Microsoft.Data.Sqlite` iç içe işlem DESTEKLEMEZ (`SqliteConnection does not support nested transactions`); bu yüzden `AcquireMigrationLockAsync` içinde `BEGIN IMMEDIATE` açıp tüm migration boyunca tutmak, `MigrationRunner.ApplyOneAsync`'in her migration için açtığı KENDİ işlemiyle çatışırdı. Ayrıca ayrı bir "kilit bağlantısı" üzerinden `BEGIN IMMEDIATE` tutmak da denenmedi: SQLite dosya düzeyinde tek yazıcı olduğu için bu, birincil bağlantının kendi yazma işlemlerini KENDİ KENDİNE kilitlerdi (self-deadlock). Çözüm: `<veritabanı-dosyası>.agentprism-migration-lock` adında bir sidecar dosya, `FileShare.None` ile açılıp elde tutulur (yoklamalı bekleme, `commandTimeout` saniye); bu, bağlantının işlem durumuna hiç dokunmadan çapraz-süreç dışlama sağlar. `:memory:` veritabanlarında atlanır.

### K-076

**K-076 — Denetim izi aktörü `AsyncLocal` köprüsüyle (`AuditActorContext`) okunur, `IHttpContextAccessor` ile değil**

`AgentPrism.Core` ASP.NET Core'a bağımlı değildir (AOT, K-006) ama "kim yaptı" sorusu `HttpContext.User`'a ihtiyaç duyar. `AgentPrismEndpointFilter` (AspNetCore), her korumalı isteğin güvenlik denetimleri geçtikten sonra `AuditActorContext.Current = httpContext.User` yazar; `Core`'daki `AmbientAuditActorResolver` bunu okur. `ClaimsPrincipal` temel .NET kütüphanesindedir (`System.Security.Claims`), ASP.NET Core paketine ihtiyaç duymaz — bu yüzden köprü Core'un katman sınırını bozmaz. Desen `Activity.Current`'ın aynısıdır: `AsyncLocal`, aynı isteğin async çağrı zincirinde ileri doğru akar (Faz 6'nın "kök span çocuğa akmıyor" tuzağı burada **geçerli değildir** — o tuzak bir değerin nested async metottan çağırana geri akmamasıyla ilgiliydi; burada değer pipeline'ın başında yazılır ve zincirin geri kalanına ileri akar).

### K-330

**K-330 — Yargıcın `IChatClient`'ı guard boru hattından GEÇER; engelleme özel olarak ele alınmadı (Faz 49, D1)**

Üç seçenek vardı: (a) yargıç istemcisini guard'sız kurmak, (b) engellemeyi sessizce puanlama başarısızlığı saymak, (c) davranışı belgelemek. **(b) seçildi ve hiçbir özel kod yazılmadı** — `ModelRunJudge` standart `IModelProviderRegistry.CreateChatClient` yolunu kullandığı için bir `AgentPrismContentBlockedException` zaten `JudgeAsync`'ten fırlar, `OnlineEvalJobHandler` bunu genel "yargıç hata verdi" yoluyla yakalar (K-160 geri adımlı yeniden deneme), puan yazılmaz. Guard'ı atlamak (a) bu fazı güvenlik sınırının dışına çıkarırdı ve gerekçesizdi: yargıç da nihayetinde kullanıcı içeriğini (çalıştırmanın girdi/çıktısını) bir modele gönderir. Maskeleme durumunda yargıç maskelenmiş metni puanlar — bu bilinen, belgelenmiş bir sınırdır (ölçüm sapması), ayrı bir kod yolu GEREKTİRMEZ.

### K-474

**K-474 — `InboundTriggerDispatcher.EnqueueAsync`, hedef agent/workflow'un GERÇEKTEN var olup olmadığını kabul anında DOĞRULAMAZ; kontrolü işleyiciye (`AgentRunJobHandler`/`WorkflowJobHandler`) bırakır (Faz 66, bağımsız denetim ile onaylandı)**

`AgentEndpoints.RunQueuedAsync`'in aksine (o, `catalog.ResolveAsync` ile `404` döner) tetikleyici kabul anında hedefi çözmez. Bağımsız denetim bunun bir kusur mu kasıtlı bir kapsam kararı mı olduğunu sorguladı; incelemede `SchedulingEndpoints.SaveAsync` (job_schedules, Faz 17) emsalinin AYNI şekilde davrandığı (yalnız `TargetName` boş mu diye bakar, var mı diye bakmaz) doğrulandı — tutarlı bir repo kalıbıdır. Hatalı hedefe giden bir tetikleyici olayı KABUL EDİLİR (`202`), kuyruğa girer ve işleyici çalışma anında `catalog.ResolveAsync` ile çözemeyip run'ı `Failed` işaretler; operatör bunu `runs`/`jobs` geçmişinden görür.

### K-296

**K-296 — Hata sınıflandırıcının SDK istisna adları örnek uygulamada gerçek bir OpenAI hatasıyla ölçüldü; `ClientResultException` eksikti**

Plan taslağı yalnız `HttpRequestException`/`SocketException`/`IOException`'ı `provider_error` kalıbına koymuştu. `samples/AgentPrism.Api`'de gerçek bir OpenAI 404 yanıtı (`gpt-olmayan-model-xyz`) tetiklendiğinde resmi OpenAI SDK'sının `System.ClientModel.ClientResultException` fırlattığı görüldü — bu tip listede yoktu ve hata `Unknown` kovasına düştü. `DefaultRunErrorClassifier` `ClientResultException`/`RequestFailedException`/`ApiException` tip adlarını ve mesaj içindeki `HTTP 4xx`/`HTTP 5xx` deseninigi de kapsayacak şekilde genişletildi; düzeltmeden sonra aynı hata `ProviderError`'a düştü. **Ders**: SDK sarmalayıcı istisna adları .NET'in kendi ağ istisnalarından FARKLIDIR ve yalnız gerçek bir sağlayıcı çağrısıyla ortaya çıkar — birim testi bu boşluğu YAKALAYAMAZDI.

### K-334

**K-334 — K-057 güncellendi: AgentPrism artık MCP istemcisi VE sunucusudur (Faz 50)**

Eski metin "AgentPrism yalnızca istemcidir" diyordu; bu artık doğru değil. `ModelContextProtocol.AspNetCore` 2.0.0 (GA) yalnızca `AgentPrism.AspNetCore` içine girdi — 13 geçişli paket, tümü `Microsoft.Extensions.*` ailesinden ve bizim sürümlerimizle (10.0.10, `Microsoft.Extensions.AI.Abstractions` 10.8.3) birebir aynı, yabancı bağımlılık yok. `AgentPrism.Mcp` (istemci) `.Core` hattında değişmeden kaldı; `DependencyDirectionTests.Mcp_istemci_paketi_sunucu_paketlerine_bagli_degildir` bunu kalıcı kılar. 🚨 Ölçülen ek: plan taslağı yalnız `AddMcpServer().WithListToolsHandler/WithCallToolHandler` biliyordu; gerçekte `services.AddMcpServer().WithHttpTransport()` ÇAĞRILMADAN `MapMcp` "You must call WithHttpTransport()" ile açılışta patlar — bu üçüncü çağrı plan taslağında yoktu, gerçek koşumda (fonksiyonel test) ortaya çıktı.

### K-252

**K-252 — Döngü denetimi `AgentCallGraph.ValidateDetailed`'e taşındı, yeni dosya yok**

Faz 34 planı statik döngü denetiminin hiç var olmadığını varsayıyordu (`grep -rni "cycle" src/AgentPrism.Core/Compilation/ src/AgentPrism.Core/Agents/` boş döndü) ama arama yanlış dizinlerdeydi — gerçek denetim `src/AgentPrism.Core/Graph/AgentCallGraph.cs`'te önceki bir fazdan beri vardı ve `POST`/`PUT /api/agents` kaydetme anında zaten kullanılıyordu. İki ayrı döngü denetleyicisi (biri kaydetmede string mesaj döner, biri doğrulama ucunda tipli kod ister) aynı mantığı iki kopya hâlinde tutardı — biri güncellenip diğeri unutulabilirdi. Çözüm: `AgentCallGraph.Validate(string)` imzası ve davranışı **değişmeden** kalır (mevcut testler regresyonsuz geçti); yeni `ValidateDetailed(...)` aynı denetimi çalıştırıp kod (`unknown_agent`/`cycle`) ve mesajı birlikte taşıyan `AgentCallGraphProblem` döner, `Validate` artık ona delege eder.

### K-514

**K-514 — Sevk edilen dokümantasyon KENDİ KENDİNE YETER: pakete giren bir metin yalnız tüketicinin elindeki şeylere gönderme yapar (Faz 75, F-126)** *(kullanıcı kararı)**

K-408 dil sınırını çizdi; bu karar aynı sınıra hedef kitle boyutunu ekler. Ölçüldü: 15 paketin XML dosyalarında **1 033 satır**, paketlenen `agentprism.json`'da **39 yer** ve 18 paket README'sinin **9'unda** `phase 64` · `K-032` · `docs/NN-*.md` gibi tüketicide var olmayan adresler vardı. K-408'in kanıt cümlesi neredeyse aynıydı ("imza İngilizce, açıklama Türkçe"); bu, aynı kusurun bir katman derinidir — dil doğru, hedef kitle yanlış. Kapsam: `src/**/*.cs` içindeki `///` satırları, `src/*/README.md`, kök `README.md`, `docs/openapi/agentprism.json`. Kapsam DIŞI ve bilerek: `//` uygulama yorumları — bakımcı onları okur, tüketiciye hiç gitmezler. Kural yalnız referansı değil **sesi** de kapsar: 🚨/⚠️ (347 satır), `Rationale:` (98), `Measured (20…)` — site üreteçleri bunları zaten siliyordu, yani proje bu sesi tüketiciye uygun bulmuyordu.

### K-324

**K-324 — `422` yalnızca AKIŞSIZ çalıştırma dalında dönebilir (kullanıcı kararı)**

Faz 48'in planı "girişte engelleme `422` döner" diyordu. Ölçüldü: `POST /api/agents/{name}/run` varsayılan olarak SSE'dir ve `SseWriter.StartAsync` çalıştırma BAŞLAMADAN başlıkları gönderir; guard model boru hattında (K-321) olduğu için karar durum kodu yazıldıktan SONRA oluşur. Akışlı yolda `422` fiziksel olarak imkânsızdır. Akışsız dal `Idempotency-Key` başlığıyla seçilir (Faz 43) ve orada `422` + `ProblemDetails` döner; akışlı dalda engelleme SSE `error` olayı olarak görünür. `runs.error_type` her iki dalda `content_blocked` olur, yani makine tarafından okunabilir sözleşme dalın seçiminden bağımsızdır. Reddedilen iki alternatif: uç önünde ikinci bir ön-uçuş denetimi (ilk kullanıcı mesajını iki kez denetlerdi) ve SSE'yi ilk çerçeveye kadar geciktirmek (`runId` taşıyan `run` olayının sözleşmesini değiştirirdi).

### K-256

**K-256 — `QuotaUsageObserver` senkron kapılı önbellektir, zamanlayıcı değil**

Plan "önbellek arka planda tazelenir" diyordu; `PeriodicTimer`/`BackgroundService` deseni (`JobWorkerBackgroundService` ile aynı) düşünüldü ama `ObservableGauge` geri çağırması yoklama sıklığından bağımsız çalışır ve testte `TimeProvider`'ı gerçekten ilerletebilmek gerekir — `System.Threading.Lock`/`PeriodicTimer(TimeSpan, TimeProvider)` net8.0'da yoktur (proje üç çerçeveyi de hedefler) ve `ManualTimeProvider` `CreateTimer`'ı override etmez. Çözüm: geri çağırma `SemaphoreSlim` ile korunan bir `Snapshot()` çağırır; önbellek `QuotaUsageRefreshInterval`'den eskiyse BİR kez engelleyerek (`GetAwaiter().GetResult()`) tazelenir, aksi hâlde önbellekten döner. Sonuç DoD ile aynıdır (ardışık yoklama tek sorgu üretir) ve doğrudan test edilebilir.

### K-361

**K-361 — `docs/MIMARI.md` sıcak yol bütçesi 42.000 → 44.000 bayt (Faz 53)**

Faz 53 "Güvenlik Modeli" bölümüne gerçek, kalıcı bir mimari katman ekledi (API anahtarı doğrulama akışı + kiracı öncelik sırası); bu narrative/geçmiş anlatı değil, **bugünkü** mimarinin bir parçasıdır ve `docs/arsiv/`'e taşınamaz (protokolün "bugünkü mimari değiştiyse ilgili bölümü düzelt" kuralı önce denendi — bearer token katmanı ve kiracı çözümleme diyagramı yerinde güncellendi, eklenmedi). Ölçüldü: dosya faz başında 41.947/42.000 bayt ile zaten sınırdaydı (önceki 52 fazın bıraktığı boşluk tükenmişti); gerçek yeni içerik minimal ifadeyle bile 43.148 bayta çıkardı. İçeriği daha da sıkıştırmak (tablo satırlarını veya diyagram düğümlerini kısaltmak) güvenlik-kritik bir bölümün açıklığını feda ederdi.

### K-280

**K-280 — Çalıştırmanın alt yazmaları ambient kiracıyla süzülmez; `[TenantAgnostic]` ile gerekçesi yazılır**

Ölçüldü (Faz 41): `IRunStore.AppendEventAsync`, `CompleteRunAsync`, `UpdateRunCostAsync` ve `RecordToolInvocationAsync`'e ambient kiracı süzgeci eklendi ve **geri alındı**. Sebep: `RunStartInfo.TenantId` ambient kiracıyı bilerek ezebilir (workflow yürütücüsü ve iş kuyruğu bir kiracı adına çalışır); süzgeç meşru yazmaları sessizce düşürüyordu ve `Ozet_baska_kiracinin_cagrilarini_saymaz` testi bunu anında kırmızıya çevirdi. Gerçek bir denetim, çağrının **beklenen** kiracıyı taşımasını gerektirir — bu da `RunEvent`/`RunCompletion`/`ToolInvocationRecord` kayıtlarına birer alan eklemektir. Bugün ulaşılabilir bir sızıntı yoktur: çalıştırma kimlikleri uuid v7'dir, okuma tarafı kiracıyla süzülüdür ve bu metotlar hiçbir HTTP ucundan çağıranın verdiği bir kimlikle çağrılmaz. Dört metot gerekçesiyle muaf işaretlendi.

### K-027

**K-027 — Opak ve polimorfik JSON yükleri `json` sütununda, `jsonb` değil**

Ölçüldü: PostgreSQL `jsonb` nesne anahtarlarını yeniden sıralar (önce uzunluğa, sonra bayt sırasına göre). System.Text.Json'ın polimorfik ayracı `$type` nesnenin **ilk** özelliği olmak zorundadır; `jsonb` bu garantiyi bozar ve okuma `JsonException: The metadata property ... is not the first property` ile başarısız olur. `sessions.state` (MAF `SerializeSessionAsync` çıktısı) ve `conversation_items.item` (`ChatMessage`, polimorfik `AIContent` içerir) bu yüzden `json`. Bu iki alan sorgulanmaz, dolayısıyla `jsonb`'nin index desteğine ihtiyaç yoktur. Kendi ürettiğimiz `agent_definitions.definition` polimorfik değildir ve `jsonb` + GIN index olarak kalır. `run_events.payload` ise `text` — `RunEventWriter` AOT uyumlu kalmak için argümanları elle biçimlendirir ve çıktı geçerli JSON olmayabilir.

### K-332

**K-332 — Cevrimiçi değerlendirme pencere özeti BELLEK İÇİDİR; yeni bir SQL sorgu yüzeyi açılmadı (Faz 49)**

`GET /api/evaluation/online`'ın "son 1 saatteki ortalama puan" sorusu `run_scores`'u zaman aralığına göre tarayan yeni bir agregasyon sorgusu (3 diyalekt) gerektirirdi. Bunun yerine `OnlineEvalSummaryService` kiracı başına kayan bir bellek içi kuyruk tutar (`RunSampler`'ın saatlik bütçesiyle AYNI K1 sadelik tercihi) — kaynak gerçek her zaman `run_scores` tablosudur, bir operatör `SELECT source, avg(value) FROM run_scores GROUP BY source` ile tam sonuca her an ulaşır. Süreç yeniden başlatılınca pencere sıfırlanır; bu kabul edilen bir sınırdır (kalıcı bir sayaç deposu bu fazın kapsamında değil). Yargıç maliyeti ise (aynı uçta) mevcut `RunQuery.Kind` süzgeciyle `runs` tablosundan hesaplanır — ayrı bir SQL yüzeyi burada da açılmadı.

### K-494

**K-494 — Fonksiyon düğümü yalnız `Sequential`'da desteklenir; `WorkflowDefinition.Nodes` `AgentNames` ile karşılıklı dışlanır (Faz 71, F-116)**

`AgentWorkflowBuilder`'ın `Concurrent`/`Handoff`/`GroupChat`/`Magentic` için hazır kalıp kurucuları yalnız `AIAgent` kabul eder (**ölçüldü**, reflection ile: `BuildConcurrent`, `CreateHandoffBuilderWith`, `CreateGroupChatBuilderWith`, `CreateMagenticBuilderWith` — hiçbiri `Executor` almıyor); bunlara elle bir fonksiyon düğümü sokmak MAF'ın kendi orkestrasyon mantığını (round-robin sıra, fan-out/fan-in birleştirme, handoff yönlendirme, Magentic planlama) sıfırdan yazmak demektir — bu fazın kapsamının kat kat üzerinde ve K-129'un reddettiği yönde bir risk büyümesi. `Nodes` alanı bu yüzden yalnız `Sequential`'da geçerlidir; `AgentNames` dolu iken `Nodes` da doluysa tanım reddedilir (hangisinin kazanacağı belirsizliği K1'in yasakladığı türden bir sürprizdir).

### K-465

**K-465 — `SqliteDialect.AddUuidArray` Guid'leri BÜYÜK harf metin olarak yazar (`System.Text.Json`'ın varsayılan küçük harf biçimi DEĞİL) (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur)**

Bu dialect'teki TÜM DİĞER Guid bağlamaları (tekil `AddUuid`, `DbHelpers.Add`) zaten büyük harf yazıyordu (K-191, "aynı mantıksal kimlik iki farklı metinle saklanmasın" dersinin doğrudan uygulaması); yalnız dizi (array) yolu bu kuralı KAÇIRMIŞTI çünkü ayrı bir kod yolundan (`System.Text.Json`'ın Guid dönüştürücüsü, HER ZAMAN küçük harf) geçiyordu. `ArrayContains`'in ürettiği `WHERE value = {table}.id` karşılaştırması SQLite'ta bayt-bayt (case-sensitive) olduğu için, `a`-`f` içeren HERHANGİ BİR kimlik sessizce eşleşmiyordu — rastgele üretilen test id'lerine bağlı olarak KESİKLİ (flaky) başarısızlık olarak ortaya çıktı, gerçek bir hata iletisi olmadan. Düzeltme: değerler `Guid.ToString("D").ToUpperInvariant()` ile önce metne çevrilip `StringArray` bağlamı üzerinden JSON'a yazılır.

### K-277

**K-277 — Bellek içi depolar `ITenantContext` alır; kiracı süzgeci artık isteğe bağlı değildir**

Ölçüldü (Faz 41): `InMemoryAgentDefinitionStore` kiracı kavramını **hiç** taşımıyordu; `InMemorySessionStore.GetAsync/DeleteAsync`, `InMemoryRunStore.GetRunAsync/ReadEventsAsync/ListToolInvocationsAsync` ve `InMemoryTraceStore.GetTraceByRunAsync` kiracıyı okumuyordu. K-018 bu depoları **birinci sınıf** sayar ve `AddAgentPrism()` onları varsayılan olarak kaydeder — yani veritabanısız çok kiracılı bir kurulumda A kiracısı B'nin agent tanımını, oturumunu ve çalıştırmasını okuyup silebiliyordu. Kurucu parametresi **isteğe bağlıdır** (`ITenantContext? tenantContext = null`) ve verilmezse `FixedTenantContext.Default` kullanılır: bu, kırıcı bir değişiklik olmadan filtrelemeyi **her kod yolunda** zorunlu kılar — bağlam her zaman vardır, verilmediğinde yalnızca değeri sabittir. DI kaydı gerçek bağlamı geçirir. Sorgu süzgeçleri SQL ile aynı kurala geçti: `query.TenantId ?? ambient`.

### K-487

**K-487 — Tool sarmalama sırası Authorizing (dış) → Timeout → ApprovalRequired (iç) → gerçek fonksiyon; MCP yolunda AYNI mantık TEKRARLANIR (Faz 69)**

İzin, onay veya süre beklemesinden ÖNCE gelir: yapamayacağı bir çağrıyı bir insana onaylatmak veya süresini beklemek yanlıştır. Timeout onayın DIŞINDadır çünkü `ApprovalRequiredAIFunction` insanın kararını tek bir çağrı içinde HİÇ BEKLEMEZ — K-368 gereği karar YENİ bir `run` açar; bu yüzden timeout yalnız gerçek yürütmeyi sınırlar. `AgentPrism.Core/Tools/ToolRegistry.cs` ve `AgentPrism.Mcp/Internal/McpTenantTools.cs` (MCP tool'ları `ToolRegistry`'den GEÇMEZ) AYNI üç katmanı BAĞIMSIZ olarak kurar — Faz 6'dan beri var olan onay-sarmalama tekrarının doğal uzantısı; paketler arası `internal` paylaşım (`InternalsVisibleTo`) yerine mevcut kopyalama deseni izlendi, `AuthorizingAIFunction`/`TimeoutAIFunction` bu yüzden PUBLIC.

### K-122

**K-122 — 🚨 MAF executor kimlikleri agent ÖRNEĞİNDEN türer; sarmalayıcılar önbelleklenir**

Ölçüldü: kimlik `{Name}_{AIAgent.Id}` biçimindedir ve `AIAgent.Id` her örnek için rastgele üretilir — reflection ile doğrulandı, **sanal değildir**, türetilmiş sınıf değiştiremez. Sonuç: graf her çalıştırmada yeniden kurulduğunda agent'lar da yeniden kurulursa kontrol noktaları uyumsuz hale gelir (`InvalidDataException: The specified checkpoint is not compatible with the workflow` — gerçek çalıştırmada görüldü). Çözüm `WorkflowAgentCache`: sarmalayıcı örnekler `(workflowName, agentName)` çiftine göre süreç ömrü boyunca saklanır. Graf yine her çalıştırmada yeniden kurulur (executor durumu taze kalsın diye). **Bilinen sınır:** süreç yeniden başladığında kimlikler değişir ve eski kontrol noktaları kullanılamaz.

### K-419

**K-419 — Doküman sitesinin ekran görüntüleri E2E koşumundan üretilir ve commit edilir; elle alınan görsel kabul edilmez**

Elle alınan görsel arayüz değişince sessizce bayatlar ve kimse fark etmez. `DocumentationScreenshotTests` 14 ekranı gerçek tarayıcıda gezer, her ekranın işaret öğesini bekler ve görüntüyü alır; yazma `AGENTPRISM_UI_SCREENSHOTS=1` ile kapılıdır (`OpenApiSnapshotTests` emsali) — bayrak yokken test yine de KOŞAR ve render'ı doğrular, yani boş `dotnet test` dosya kirletmez ama bayatlama kapısı her koşumda çalışır. 🚨 Tarayıcı yereli `en-US`'e ve tema `Light`'a SABİTLENİR (K-231: varsayılan dil `navigator.language`'dan, tema işletim sisteminden gelir) — sabitlenmeseydi görseller koşumu yapan makineye bağlanırdı. Yakalamadan önce iki gerçek `run` üretilir; boş bir konsolun ekran görüntüsü hiçbir şey öğretmez. PNG'ler commit edilir, böylece `pages` job'ı Playwright çalıştırmaz.

### K-276

**K-276 — `docs/openapi/agentprism.json` üretim kaynağı `AgentPrism.AspNetCore.FunctionalTests`'tir, `samples/AgentPrism.Api` DEĞİL**

Ölçüldü (Faz 40, F-76): `samples/AgentPrism.Api` SqlServer + Sqlite sağlayıcılarını BİRLİKTE referanslıyor (K-185 örneği için bilerek); ikisi `AgentPrism.Sql.Shared`'ı linked-source olarak derlediği için (K-185) `MigrationRunner` gibi tipler İKİ derlemede aynı tam nitelikli adla XML doküman üretir. `Microsoft.AspNetCore.OpenApi`'nin XML yorum önbelleği bu iki girdiyi TEK sözlükte topluyor ve `ArgumentException` ile `/openapi/v1.json`'u 500'e düşürüyor (F-76). `AgentPrism.AspNetCore.FunctionalTests` hiçbir SQL sağlayıcısına referans vermediği için bu çakışmadan bağışıktır ve `AgentPrism.AspNetCore`'un kendi uç üstverisini üretim kaynağı olarak doğru temsil eder (bu fazın paket sınırı zaten yalnız `AgentPrism.AspNetCore`'dur).

### K-141

**K-141 — Eval vaka çalıştırmaları `runs` istatistiklerinden hariç tutulur; `RunKind.Eval` eklendi** *(kullanıcı kararının uygulanması, doc açık soru 4)**

Örnek uygulamada gerçek bir OpenAI çağrısıyla ölçüldü: düzeltmeden **önce** tek bir eval vakası çalıştırıldığında `/api/stats` `totalRuns:1` gösteriyordu — eval bir test çağrısı olmasına rağmen normal trafik gibi sayılıyordu. `RunKind` içine `Eval = 2` eklendi; `AgentPrismRunOptions.Kind` çağıranın (yalnız `EvalJobHandler`) türü bildirmesini sağlar; `RunRecordingAgent.PrepareRun/BeginRunAsync` bunu `RunStartInfo.Kind`'a taşır. `IRunStore.GetStatisticsAsync` (hem `InMemoryRunStore` hem `PostgresRunStore`/`SqlQueries.SelectRunStatistics`) `kind <> Eval` filtresi ekler. Aynı ölçüm düzeltmeden **sonra** tekrarlandı: `/api/stats` `totalRuns:0` döndü, eval kosusunun kendisi `Completed`/`passed:1` olarak tamamlandı. Çalıştırma satırının kendisi silinmez — yalnız özetten çıkar; transkript/span erişimi korunur.

### K-443

**K-443 — `FallbackChatClient` devre kesicinin DIŞINDA, `ContentFilterDetectingChatClient`'ın İÇİNDE durur (K-320'nin yerleştirme kuralının Faz 62'ye uygulanışı)**

Yedeğe geçmek için önce birincilin devresinin açık olduğunu görmek gerekir (devre kesici dışı zorunlu). İçerik filtresi tespitinin İÇİNDE durması ise kasıtlı bir tasarım kazancı: bir sağlayıcı filtresi bu katmanda İSTİSNA değil, yalnız `ChatFinishReason.ContentFilter` taşıyan sıradan bir `ChatResponse`'tur — dönüşüm yalnız `ContentFilterDetectingChatClient` (en dışta) tarafından yapılır. Sonuç: `FallbackChatClient` içerik filtresini HİÇ görmez, özel durum kodu yazmadan "filtrelenmiş yanıt yedeği tetiklemez" kuralını bedava sağlar. Testle doğrulandı: `FallbackChatClientTests.Content_filter_does_not_trigger_a_fallback_attempt` — filtrelenmiş birincil yanıtta yedek istemci HİÇ çağrılmıyor.

### K-247

**K-247 — K-183'ün isareti `AgentPrism.Sql.Shared`'daki internal `SqlPersistenceRegistration`'dan `AgentPrism.Abstractions`'daki public `SqlPersistenceRegistrationMarker`'a taşındı**

Faz 33 teşhis toplayıcısı, kaç SQL sağlayıcısının kayıtlı olduğunu Core katmanından okumak zorundaydı ama Core, Sql.Shared'ı referans edemez (K-176: Sql.Shared kendi derlemesi değildir, kaynağı her sağlayıcı paketine ayrı ayrı derlenir). Taşıma bir yan etki de düzeltti: eski isaret PostgreSql.dll ve SqlServer.dll içinde YAPISAL OLARAK AYNI ama CLR kimliği FARKLI iki tip üretiyordu (linked source, K-176) — `UsePostgreSql()` VE `UseSqlServer()` birlikte çağrıldığında her sağlayıcının kendi `MigrationHostedService`'i yalnız KENDİ isaretini görüyordu ve `WarnOnMultipleProviders` çift kaydı hiçbir zaman tespit edemiyordu (test kapsamı yoktu, kod okumayla bulundu). Abstractions tek bir derlenmiş tip olduğu için artık üç sağlayıcı da aynı tipi paylaşır ve sayaç doğru çalışır.

### K-288

**K-288 — `/api/agents/{name}/run` `Idempotency-Key` varsa akışsız (JSON) çalışır; plandan sapma (kullanıcı kararı)**

Faz 43 planı bu ucun hem akışsız hem akışlı çalışabileceğini varsayıyordu, ama kodda uç KOŞULSUZ SSE dönüyordu (`AgentRunStream` her zaman akıtıyordu) — fazın kendi motivasyon örneği tam olarak bu uçtu (`QuotaGate`'in ikinci kez tüketmesi). Akışsız dal eklenmeden faz kendi amacını gerçekleştiremezdi: kota-tekilleştirme testi hiç çalışamazdı. Kullanıcıya iki seçenek sunuldu (akışsız dal ekle / bu uçta hep 400 kabul et), kullanıcı akışsız dal eklenmesini seçti. `AgentRunStream` artık `streaming` bayrağıyla iki yol izliyor: akışlı (SSE, varsayılan) ve akışsız (`agent.RunAsync` + `Results.Json`). Baş yan etki: bu uçta akışlı+`Idempotency-Key` birlikteliği hiç oluşamaz (başlık varlığı zaten akışsızlığı seçiyor), bu yüzden 43.4'ün "akışlı istek+anahtar→400" kuralı yalnız `/v1/responses` ve `/v1/chat/completions` üzerinde gözlemlenebilir.

### K-278

**K-278 — `sessions` birincil anahtarı `(tenant_id, id)`; oturum kimliği kiracı içinde benzersizdir**

🚨 **Güvenlik düzeltmesi.** Ölçüldü (Faz 41, `Ayni_ad_iki_kiracida_bagimsiz_yasar` sözleşme testi kırmızıydı): `sessions.id` çağıran tarafından verilen bir metindir (`AgentSession` kimliği, `/v1/responses` konuşma kimliği) ve tek başına birincil anahtar olduğu için kimlik **bütün kiracılar arasında** benzersizdi. `ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id` bir kiracının diğerinin oturumunu üzerine yazmasına izin veriyordu: durum kayboluyor ve satırın sahipliği el değiştiriyordu. Oturum kimlikleri tahmin edilebilir olabilir (`user-42-chat`). Anahtar `(tenant_id, id)` oldu (PostgreSQL 0018, SQL Server 0006, SQLite 0006); upsert artık `tenant_id` güncellemez. SQLite birincil anahtarı değiştiremediği için tablo yeniden kurulur ve indeksler elle yeniden oluşturulur. Yabancı anahtar referansı yoktur; değişiklik bu tabloyla sınırlıdır.

### K-216

**K-216 — `AgentPrism.Voice` hiçbir NuGet paketi almaz; ham `HttpClient` kullanılır**

`ElevenLabs-DotNet` (3.7.2) mevcuttu ve alınmadı. Gerekçe üç katmanlı: (1) kullanılan yüzey **üç uçtan** ibarettir (`/v1/text-to-speech/{id}`, `/v1/speech-to-text`, `/v2/voices`) ve JSON sözleşmesi basittir; (2) `System.Text.Json` kaynak üreteci ile paket **AOT uyumlu** kalır — bir SDK'nın yansıma kullanması bunu bozardı; (3) topluluk SDK'sı bakım ve sürüm riskini tüketiciye taşır (K-007) ve Faz 27, bir SDK'nın merkezî sürüm yönetimi altında çalışma anında **sessizce** kırılabildiğini ölçmüştü (K-211). Ölçülen sonuç: paketin **tek** doğrudan bağımlılığı `AgentPrism.Core`'dur; üretilen `.nuspec` başka hiçbir paket listelemez. Sözleşme (yol, `xi-api-key` başlığı, `output_format` sorgusu, multipart STT, `/v2/voices`) taklit bir uçla uçtan uca doğrulandı.

### K-195

**K-195 — `DbHelpers.ToGuid`/`ToBoolean` eklendi: `ExecuteScalarAsync` sonucunun CLR tipi sağlayıcıya göre değişir**

`(Guid)result!` ve `result is bool b && b` kalıpları PostgreSQL (`uuid`→`Guid`, `boolean`→`bool`) ve SQL Server (`uniqueidentifier`→`Guid`, `CAST(...AS bit)`→`bool`) için dogruydu ama SQLite'ta YANLIŞTI: `id` sütunu TEXT olduğu için skaler sonuç `string` döner (`SqlTraceStore.UpsertTraceAsync` `InvalidCastException` fırlatıyordu) ve SQLite'ta mantıksal tip olmadığı için bir karşılaştırma ifadesi `long` (0/1) döner (`SqlWebhookStore.RecordSubscriptionOutcomeAsync` `result is bool` hiç eşleşmediği için HER ZAMAN `false` dönüyordu — sözleşme testinde yakalandı). Düzeltme: `DbHelpers.ToGuid(object)` (`Guid` ise aynen, değilse `Guid.Parse((string))`) ve `DbHelpers.ToBoolean(object)` (`bool`/`long` ikisini de kabul eder) eklendi; iki çağrı yeri buna geçti. PostgreSQL (416/416) ve SQL Server (204/204, azure-sql-edge) regresyonsuz geçti.

### K-340

**K-340 — Dışa açılan bir agent'ın `AgentRunBudget.MaxDepth` değeri, kaç seviye TORUN çağrısına izin verildiğidir; "0" = hiç, "1" = bir seviye (Faz 50)**

Plan taslağının DoD'si "`MaxDepth = 1` iken dışarıdan çağrılan agent alt agent çağıramaz" diyordu. Ölçüldü: `ChildAgentInvoker.Refuse()` `scope.Depth + 1 > maxDepth` kuralını uygular; dış çağrı KÖK'tür (`Depth=0`), dolayısıyla `MaxDepth=1` iken `0+1=1 > 1` YANLIŞTIR ve ilk seviye alt çağrıya İZİN VERİLİR — yalnız İKİNCİ seviye (torun) engellenir. DoD'nin sözel iddiası bu yüzden yanlıştı; test bunun yerine `MaxDepth=0` ile "hiç alt çağrı yok" sınırını doğruladı (`McpServerEndpointTests.Derinlik_siniri_alt_cagriyi_engeller`). Varsayılan yine de `MaxDepth=1` bırakıldı (Açık Soru 6'nın "öngörülemez maliyet" gerekçesi geçerliliğini korur — bir seviye sınırlı devir, sınırsız ağaçtan farklıdır); yalnızca DOKÜMAN cümlesi yanlıştı, davranış (`ChildAgentInvoker`, Faz 12'den beri değişmedi) doğruydu.

### K-199

**K-199 — `run_events` partition'ı açılmadı (K-063 ölçümle kapandı)**

Ölçüldü (PostgreSQL 18, bu makine): 100.000 satırlık `run_events` kümesinde parti parti silme (`ctid` alt sorgusu, parti 5.000) 19 partide 125 ms'de 90.000 satır sildi — saniyede ~720.000 satır. Faz 6/7'nin hedef yükü saniyede 100 çalıştırma × ~50 olay = saniyede 5.000 olaydır; ölçülen silme hızı bunun ~144 katı. Yazma tarafı da ölçüldü: 100.000 satırlık toplu ekleme 1,7 sn'de tamamlandı (~58.500 satır/sn). Hiçbir darboğaz görülmedi; partition'ın getirdiği tek gerçek kazanç (eski bölümü `DROP` ile anında silmek) bu ölçekte gerekli değil. Yan gözlem: silme sonrası tablo boyutu DEĞİŞMEDİ (11,23 MiB → 11,23 MiB) — PostgreSQL `DELETE` sayfaları hemen boşaltmaz, `VACUUM`/otomatik vakum bekler; bu işlevselliği etkilemez ama bir işletmenin bilmesi gereken bir davranıştır.

### K-166

**K-166 — `JobRecord.Payload` atanmazsa `/api/jobs` TÜM listeyi 500 ile döndürür** 🚨**

`WebhookPublisher` işi kuyruğa yazarken `Payload` alanını hiç atamamıştı; alan `default(JsonElement)` (ValueKind = `Undefined`) kaldı ve `JsonElementConverter.Write` onu serileştiremeyip `InvalidOperationException` fırlattı. Etki tek bir işle sınırlı değildi: `GET /api/jobs` **tüm** iş listesini 500 ile döndürüyordu. 1231 testin hiçbiri yakalamadı — birim testleri işi kuyruğa yazıyordu ama HTTP katmanından serileştirmiyordu. Yalnızca örnek uygulamayı çalıştırıp `/api/jobs?kind=WebhookDelivery` çağırınca ortaya çıktı. Düzeltme: yük, iş öğesiyle aynı teslim kimliğini taşıyan bir JSON dizisidir. Kalıcı önlem: `WebhookPublisherTests.Kuyruga_yazilan_is_serilestirilebilir_bir_yuk_tasir` — `ValueKind` denetimi **ve** gerçek serileştirme çağrısı. **Ders**: yeni bir `JobRecord` üreten her kod yolu `Payload`'ı atamalıdır; `JsonElement` alanları `default` bırakılamaz.

### K-172

**K-172 — `[JsonPropertyName]` iki büyük harfle başlayan alan adlarında AÇIKÇA verilir** 🚨**

`OAuthEnabled` gibi alanlar ilk yazımda öznitelik taşımıyordu. System.Text.Json'ın camelCase politikası yalnız **ilk** harfi küçültür; "OAuth" iki büyük harfle başladığı için varsayılan çıktı `oAuthEnabled` oluyordu (beklenen `oauthEnabled` değil). Birim/işlevsel testler bunu yakalamadı çünkü ASP.NET Core'un istek gövdesi bağlaması varsayılan olarak büyük/küçük harfe duyarsızdır — yalnız **yanıt** tarafı etkileniyordu. Hata, örnek uygulamaya gerçek `curl` isteği atılıp yanıt JSON'ı gözle incelenince ortaya çıktı. Düzeltme: `McpServerDefinition` ve `McpServerRequest`'teki her OAuth alanına `[JsonPropertyName("oauthXxx")]` eklendi. **Ders**: `PascalCase` adı iki veya daha fazla büyük harfle başlayan (`OAuth`, `URI`, `ID` gibi kısaltma) her yeni public alan, camelCase serileştirme çıktısını **gerçek bir istekle** doğrulamalıdır — varsayılana güvenilmez.

### K-086

**K-086 — Script çalıştırma varsayılan olarak kapalıdır ve `PlatformIsolationAcknowledged` olmadan açılamaz**

K-012'nin ("tool'lar yalnız kodda tanımlanır") ikinci bilinçli istisnasıdır. AgentPrism işletim sistemi seviyesinde yalıtım **sağlamaz**: süreç, sunucu kullanıcısının hakları ve ağ erişimiyle çalışır. Bu gerçeği tüketicinin görmeden geçmesi mümkün olmamalıdır; `AgentPrismOptionsValidator`, `Enabled = true` iken `PlatformIsolationAcknowledged = false` ise açılışta hata verir. Sağlanan korumalar: yorumlayıcı beyaz listesi, ortam değişkeni beyaz listesi, zaman aşımı + süreç ağacı öldürme, çıktı kırpma, eşzamanlılık sınırı, kiracı başına izin kaydı, denetim izi. **Sağlanmayanlar:** dosya sistemi hapsi, ağ kısıtı, bellek/CPU kotası, kullanıcı düşürme. Bunlar container, ayrıcalıksız kullanıcı ve kısıtlı ağ ile dışarıdan kurulmalıdır.

### K-360

**K-360 — API anahtarı kapsam (`scope`) denetimi yalnız agents/runs/external-invoke uçlarına uygulandı; tam taksonomi ERTELENDİ (Faz 53)**

`ApiKeyScope` beş değer tanımlar ama HTTP yüzeyi 100'ün üzerinde uç taşır. Her ucu tek tek scope'a bağlamak bu fazın kapsamını kat kat büyütürdü (K2 ruhu: gereksiz genişleme). `RequireApiKeyScope` yalnız DoD'nin adlandırdığı yüzeyde uygulandı: `AgentEndpoints` (agents:read/agents:admin), `RunEndpoints`'in run başlatma/iptal/yeniden oynatma/okuma uçları (runs:write/runs:read), ve MCP/A2A dış yüzey grupları (external:invoke). Kapsam gerektirmeyen bir uca API anahtarıyla erişim rol politikasından geçer ama scope'tan ETKİLENMEZ — statik token ile aynı zemin.

### K-193

**K-193 — SQLite'ta indeks adları VERİTABANI GENELİNDE tektir; migration DDL'indeki her indeks de tablo önekiyle EZİLİR**

PostgreSQL'de indeks adları şema içinde, SQL Server'da tablo içinde kapsamlıdır; SQLite'ta ise TÜM veritabanı için TEK düz ad alanı vardır (tablo, indeks, tetikleyici, görünüm hepsi aynı ad alanını paylaşır). İlk yazımda yalnızca TABLO adları önek alıyordu, indeks adları almıyordu (`CREATE UNIQUE INDEX IF NOT EXISTS quotas_scope_uq ON {schema}quotas ...`). Sonuç: aynı fiziksel `.db` dosyasını paylaşan ama farklı tablo önekleri kullanan iki test (veya iki `TablePrefix` yapılandırması), İKİNCİ `CREATE INDEX IF NOT EXISTS`'in adı ZATEN VAR sanıp SESSİZCE atlamasına yol açtı — ikinci tablonun hiç indeksi olmadı ve `ON CONFLICT (...)` hedefi "does not match any PRIMARY KEY or UNIQUE constraint" hatasıyla patladı (sözleşme testinde yakalandı). Düzeltme: migration dosyasındaki 39 indeks adının TAMAMI `{schema}` önekini taşır.

### K-080

**K-080 — Denetim kaydında `before`/`after` tam tanım olarak saklanır, yalnız değişen alanlar değil**

Faz 9'un açık sorusu buydu; "tam tanım" seçildi çünkü basit ve `agent_definition_versions`'ın zaten tuttuğu tam geçmişle tutarlıdır. Uygulama: `AgentDefinition`, `McpServerDefinition`, `TenantDescriptor`, `ToolApprovalRule` doğrudan `AgentPrismCoreJsonContext` (kaynak üreteci, AOT uyumlu) ile serileştirilir — PostgreSql paketindeki `AgentDefinitionPayload` DTO'su yeniden kullanılmadı çünkü o paket `internal` ve sütun/jsonb ayrımına özgüdür; denetim izi kaydı sütunları tekrarlamadığı için tam nesneyi taşıyabilir. `agent.rollback` istisnadır: before/after yalnızca sürüm numaralarıdır (`{"rolledBackToVersion":N,"newVersion":M}`), çünkü rollback zaten `agent.create`/`agent.update` gibi tam içerik taşıyan bir kayıt üretmez.

### K-225

**K-225 — `PersistAudio` yalnız agent'ın ürettiği sesi saklar; kullanıcının sesi hiç saklanmaz** *(kullanıcı kararı: saklama varsayılan kapalı)**

Ses **biyometrik veridir**. Kullanıcının söylediği zaten oturum geçmişinde transkript olarak durur; sesin ikinci kopyası risk ekler, bilgi eklemez. Denetim izi açısından değerli olan taraf agent'ın **söyledikleridir**. İkinci bir gerekçe teknik: tarayıcı WebM/Opus üretir ve `AttachmentTypeGuard` EBML imzasını tanımaz; tanıması için sihirli bayt listesine EBML eklemek gerekirdi ve bu, `audio/*` beyaz listesi üzerinden **video WebM**'i de ek yüklemesine açardı. Denetleyici bu fazda hiç değişmedi. Varsayılan kapalıdır; açıldığında arayüz kaydın yapıldığını `ready` çerçevesindeki `persistAudio` alanından okuyup görünür biçimde gösterir — sessiz kayıt yoktur.

### K-464

**K-464 — `DataSubjectTargetRegistry`'deki her `ArrayContains` çağrısı sütunu TAM NİTELİKLİ (`{table}.column`) verir, bare ad değil (Faz 64, bağımsız denetim sonrası bulunan gerçek kusur)**

Gerçek SQLite'a karşı ölçüldü: `json_each()` çıktısının kendi sabit sütun kümesi (`key, value, type, atom, id, parent, fullkey, path`) bulunur; `EXISTS (SELECT 1 FROM json_each(@dizi) WHERE value = id)` içindeki bare `id`, dış tablonun `id`'sini DEĞİL, `json_each`'in KENDİ `id` sütununu bağlar (iç kapsam dış kapsamı gölgeler) — eşleşme SESSİZCE hiçbir zaman gerçekleşmez, hata yoktur. `sqlite3` CLI'de doğrudan doğrulandı: bare `id` ile `EXISTS` her zaman `0`, `sessions.id` ile her zaman `1`. PostgreSQL'in `= ANY(...)`'i ve SQL Server'ın `OPENJSON`'ı (yalnız `key`/`value`/`type` taşır, `id` yok) bu tuzağı taşımaz — kusur yalnız SQLite'a özgüdür ve yalnız `Sessions`/`Runs`/`Conversations` hedeflerinin `id` sütunu karşılaştırmasını etkiliyordu.

### K-286

**K-286 — Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn taban); gerçek devralma ölçüldü**

İki gerçek `samples/AgentPrism.Api` süreci, aynı SQLite dosyası, `LeaseDuration=12sn` (yenileme aralığı 4 sn) ile devralma **~17,6 sn** sürdü — ölçüm: kira sahibi (B) öldürüldüğünde kira zaten neredeyse sona ermek üzereydi, devralan (A) kendi 4 sn'lik yenileme turunu bekledi. Bu oran `60sn` varsayılan kirada en kötü durumda **~90 sn**'lik bir devralma öngörür (`LeaseDuration` + bir yenileme turu). Yenileme `LeaseDuration`'ın üçte biri: iki kaçırılmış yenilemeye dayanır, yarısı seçilseydi tek bir kaçırılmış yenileme kirayı düşürürdü. 1 saniyelik taban `LeaseDuration` yanlışlıkla çok kısa ayarlanırsa (ör. test) yenileme döngüsünün mantıksız sıklıkta dönmesini engeller.

### K-496

**K-496 — Fonksiyon adı kaydetme anında da doğrulanır (agent adının aksine, yalnız derleme anında); kayıt süreç ömrü boyunca sabittir (Faz 71, F-116)**

`WorkflowDefinitionValidator`'ın kendi belgesi agent varlığının SAVE anında değil COMPILE anında denetlendiğini söyler, çünkü katalog kayıt ile ilk çalıştırma arasında değişebilir — erken denetim yanlış bir garanti verirdi. Fonksiyon kaydı ise `AddWorkflowFunction` ile YALNIZ kod içinde, uygulama başlangıcında sabitlenir; süreç ömrü boyunca değişmez (yeniden dağıtım hariç). Bu fark, fonksiyon adını SAVE anında da (HTTP katmanında, `IWorkflowFunctionCatalog?` ile, nullable — `IWorkflowRunner?` ile aynı desen) denetlemeyi güvenli kılar: faz dokümanının "kaydetme anında reddedilir" gereksinimini karşılar. Derleme anındaki denetim (`WorkflowDefinitionCompiler`) de KORUNUR — savunma derinliği; kod fonksiyonu tanım kaydedildikten sonra kaldırılıp uygulama yeniden başlatılabilir.

### K-265

**K-265 — Şablon paket sürümü varsayılanı kayan `*-*`**

AgentPrism nuget.org'da yayınlanmamıştır (Faz 7 beklemede, K-068) ve K-008 gereği sürekli `1.0.0-preview.N` olarak kalacaktır (MAF GA olana kadar). Sabit bir sürüm gömmek şablonun her kütüphane sürümünde YENİDEN PAKETLENMESİNİ gerektirirdi. NuGet'in floating version söz dizimi `*-*` "eşleşen deseni en güncel ön-sürümüyle al" anlamına gelir ve yapılandırılmış herhangi bir kaynaktan (nuget.org veya yerel `artifacts/package/release` beslemesi) otomatik en güncel sürümü çeker. `--AgentPrismVersion x.y.z` ile geçersiz kılınabilir. Test yalıtımı için `TemplateFixture` gerçek paketlenen sürümü çözüp her `dotnet new` çağrısına AÇIKÇA verir — floating sürüm çözümlemesinin CI/yerel besleme tutarlılığına bağımlı olmasını önlemek için.

### K-205

**K-205 — `Google.GenAI`'ın geçişli ağırlığı bilerek kabul edildi ve tek pakette izole edildi** *(kullanıcı kararı)**

Ölçüldü: `Google.GenAI` 1.16.0, `Google.Apis.Auth` üzerinden `Newtonsoft.Json` 13.0.3, `System.Management` 7.0.2, `System.CodeDom` 7.0.0 ve `Google.Apis`/`.Core` paketlerini çeker — toplam **11 geçişli bağımlılık** (karşılaştırma: `Anthropic` 4, hepsi Microsoft/System). Bu, K2'nin "tüketicinin bağımlılık grafiğini kirletme" kuralıyla gerilim yaratır. Alternatif `GeminiDotnet.Extensions.AI` 0.25.0 yalnız 2 geçişli bağımlılık taşıyor ama 1.0 öncesi ve tek bakımcılı. Karar: **ağırlık kabul edildi**, çünkü bağımlılık yalnızca `AgentPrism.Google` paketindedir ve Gemini kullanmayan tüketici hiçbirini almaz — meta paket de bu paketi içermez (K-209). Bakımsız kalma riski, geçişli ağırlıktan daha pahalı sayıldı.

### K-292

**K-292 — Ham govde, filtreden ÖNCE `MapAgentPrism` içine eklenen koşullu bir ara yazılımla tamponlanır**

`IdempotencyFilter` parmak izini HAM baytlardan hesaplamalıdır (43.3). Ama `/api/agents/{name}/run` govdesi minimal API'nin otomatik `[FromBody]` baglamasıyla `IEndpointFilter.InvokeAsync` ÇAĞRILMADAN ÖNCE tüketilir (ASP.NET Core, tipli parametreleri filtre zincirinden ÖNCE bağlar) — `Request.EnableBuffering()`'i filtrenin içinde çağırmak ÇOK GEÇ kalırdı, akış zaten geri sarılamaz durumda olurdu. Çözüm `MapVoiceConversation`'ın `app.UseWebSockets()` deseniyle AYNIDIR: `endpoints is IApplicationBuilder` kontrolüyle `MapAgentPrism` içine, yalnız `Idempotency-Key` başlığı taşıyan istekler için `EnableBuffering()` çağıran bir `app.Use(...)` eklendi. Bu, filtrenin kendisini (govde ÖNCESİNDE mi SONRASINDA mı okunduğundan bağımsız) route'lar arasında TEK bir koda indirger: `Request.Body.Position = 0` her zaman güvenlidir.

### K-184

**K-184 — SQL Server benzersiz indekste NULL'ları EŞİT sayar; `COALESCE`'li ifade indeksi gerekmez**

PostgreSQL'de NULL hiçbir NULL'a eşit değildir; bu yüzden `tool_approval_rules`, `skill_script_grants` ve `quotas` tablolarında benzersizlik `COALESCE(sütun, '')` ifade indeksiyle kurulmuştu (K-027 dönemi dersleri, `docs/hafiza/postgresql.md`). SQL Server benzersiz indekste NULL'ları birbirine eşit sayar ve tek bir NULL satırına izin verir — **istenen davranış zaten budur**, düz bir `UNIQUE` kısıt yeter. Ters yön de doğrudur ve bir tuzaktır: `jobs (schedule_id, scheduled_for)` kısıtı PostgreSQL'de zamanlamasız (NULL `schedule_id`) işleri birbirinden ayırt ederken SQL Server'da **ikinci bir zamanlamasız işi engellerdi**; bu yüzden orada kısıt `WHERE schedule_id IS NOT NULL` filtreli benzersiz indekstir. Sorgu tarafında eşleşme `ISNULL(sütun, N'') = ISNULL(@p, N'')` ile yazılır: `@p` NULL iken düz `=` UNKNOWN döndürürdü.

### K-479

**K-479 — Etiketler ayrı tabloya değil `runs.labels` JSON sütununa yazılır (Faz 68, açık soru 1, kullanıcı kararı)**

K-027'nin `jsonb` yasağı POLİMORFİK yükler içindir (`$type` ilk özellik olmalı); etiket haritası düz `string→string`'dir, ayraç taşımaz, sıralama zararsızdır (K-345 ile aynı gerekçe). Sekiz etiket sınırıyla ayrı tablo aşırıdır: `runs` en sıcak tablodur, her yazıma N ek `INSERT` ve her listeye dördüncü `JOIN` eklerdi. B'nin tek üstünlüğü indekslemeydi; PostgreSQL'de `gin (labels)` bunu veriyor ve süzgeç `jsonb_exists`/`@>` ile yazıldı (`?` operatörü yerine FONKSİYON biçimi — hiçbir sürücü onu parametre yer tutucusu sanmasın). SQL Server/SQLite'ta harita JSON metnidir (K-182) ve indeks yoktur: serbest bir etiket kümesi, hesaplanmış sütun indeksinin isteyeceği önceden bilinen anahtar listesini veremez.

### K-220

**K-220 — `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz**

Arayüzdeki "Speak" düğmesi bir uç ister; ama `tool_invocations.run_id` zorunlu bir yabancı anahtardır ve çalıştırmasız bir ölçüm satırı yazılamaz. Üç seçenek vardı: (a) düğmeyi kaldırıp yalnız agent'ın tool'unu bırakmak — faz kapsamındaki arayüz gereksinimini düşürürdü; (b) düğmenin bir çalıştırma başlatması — sırf ses için bir model çağrısı ödetirdi; (c) uç eklemek ve ölçümün kalıcı olmadığını **açıkça** söylemek. (c) seçildi. Maliyet görünmez değildir: uç ölçülen karakteri ve tutarı **yanıtta döndürür** ve arayüz oynatıcının yanında gösterir; uç `Operator` rolü ister ve tool ile aynı karakter sınırına uyar. Kalıcı kayıt isteyen agent'ın `speak` tool'unu kullanır. Bilinçli bir ödünleşmedir ve fazın risk tablosunda "kısmen açık" olarak durur.

### K-442

**K-442 — Gömülebilir bileşen `wwwroot/embed/` altına ayrı bir Vite girişiyle derlenir ve VAR OLAN genel varlık sunum mekanizmasıyla, sıfır yeni C# `endpoint` koduyla sunulur**

`EmbeddedUiAssetCatalog`/`EmbeddedUiProvider`/`UiEndpoints` zaten `wwwroot/**/*`'i içerik türüne göre genel olarak embed edip sunuyor (uzantıdan `Content-Type` çözer, alt dizin farketmez). `vite.embed.config.ts` çıktısını `../wwwroot/embed`'e yazınca (konsolun `emptyOutDir`'i her zaman ÖNCE çalışacak şekilde `npm run build` sırası kuruldu) mevcut `{prefix}/{**path}` rotası `embed/embed.js`'i otomatik sundu — ölçüldü, gerçek bir `UiHost` üzerinden `GET {prefix}/embed/embed.js` → `200 text/javascript`. Vite kütüphane modu (`format: 'iife'`, sabit `fileName`) tek, hash'siz bir dosya üretir çünkü üçüncü taraf sayfalar URL'yi sabit kodlar.

### K-258

**K-258 — `MaxRows` sıra, silme adımından çıkarılarak uygulandı (K-201 kapandı)**

Plan (36.1) sırayı silme sorgusundan çıkarıp önce eşiği bulmayı öneriyordu; bu birebir uygulandı. `IRetentionStore.FindRowLimitCutoffAsync(target, maxRows)` en yeniden sayarak N. satırın sıra sütunu değerini döner (üç diyalekt: PostgreSQL `OFFSET/LIMIT`, SQL Server `OFFSET/FETCH` + zorunlu `ORDER BY` (K-026), SQLite `LIMIT/OFFSET`); dönen değer MEVCUT yaş bazlı silme mekanizmasına (`DeleteBatchAsync`) doğrudan `@cutoff` olarak beslenir — K-200'ün sırasız parti silmesi değişmeden kalır. `ResolvedRetentionPolicy.MaxAgeDays` `int?`'e çevrildi (`MaxRows` eklendi); `RetentionExecutor.ComputeCutoffAsync` iki eşiği hesaplayıp DAHA YENİ olanı seçer (36.2). Kanıt: gerçek `samples/AgentPrism.Api` çalıştırılıp 150 satırlık `run_events`'e yalnız `MaxRows=100` politikası uygulandı — önizleme 50 gösterdi, gerçek koşu 50 sildi, 100 satır kaldı.

### K-368

**K-368 — `RunStatus.AwaitingApproval` eklendi; onay kararından sonra AYNI `RunId` devam ETMEZ, `AwaitingInput` emsaliyle birebir aynı şekilde YENİ bir çalıştırma açılır (Faz 55, kullanıcı kararı)**

Askıya alınmış bir çalıştırmanın satırını değiştirmek K-014'ün (olay/çalıştırma geçmişi ekleme-yalnız) ihlalidir. `RunStatus.AwaitingInput` bu problemi zaten çözmüş bir emsaldi: karar `AwaitingApproval` durumuna gelen bir çalıştırmanın satırını da SONSUZA KADAR değiştirmemek, kararı `POST /api/approvals/{id}/decide` ile YENİ bir `RunId` açarak devam ettirmek yönünde verildi — ikinci, tutarsız bir "resume" semantiği icat edilmedi. `samples/AgentPrism.Api` üzerinde gerçek bir OpenAI modeliyle uçtan uca doğrulandı: orijinal çalıştırma `AwaitingApproval` durumunda sonsuza kadar kaldı, onaydan sonra yeni bir çalıştırma otomatik kuyruğa girdi ve `Completed` durumuna ulaştı, ikinci kez karar vermeye çalışmak 409 döndü.

### K-309

**K-309 — Varsayılan tool modu `ReplayTools`; kayıtlı sonucu olmayan bir çağrı yeniden oynatmayı DURDURUR ve `422` döner**

Sadık karşılaştırmanın amacı budur ve yan etki üretmez. Sessizce atlamak modelin göremediği bir boşluk üretir ve sonucu sessizce yanlış yapar; canlı çalıştırmak kullanıcının istemediği bir yan etkidir. Eşleşme `(tool adı, argümanlar)` çiftiyle yapılır — `tool_call_id` KULLANILMAZ çünkü yeni çalıştırmada model yeni kimlikler üretir. Aynı çift birden çok kez çağrıldıysa sonuçlar kayıt sırasıyla tüketilir. 🚨 Ölçüldü: `FunctionInvokingChatClient` tool gövdesinden çıkan istisnayı YUTAR; bu yüzden eşleşmeme oynatıcıda kaydedilir, `FunctionInvocationContext.Terminate` ile döngü kesilir ve hata çalıştırma bittikten sonra `ReplayMismatchGuard` tarafından — kayıt sarmalayıcısının İÇİNDEN — fırlatılır. Sonuç: `runs` satırı `Failed` kapanır, hata tipi `replay_tool_mismatch` olur ve uç `422` döner.

### K-377

**K-377 — `ExperimentVariantResult`e `AverageScore` eklendi; `SelectExperimentResults` sorgusu `run_scores`'a (önce çalıştırma başına ortalama, sonra kol başına o ortalamaların ortalaması) genişletildi (Faz 56)**

`CanaryPolicy.MinScore` kararı (DoD ve `CanaryEvaluatorTests` açıkça ister) kanarya kolunun ortalama puanına ihtiyaç duyar ama `run_scores`'u okuyan hiçbir deney sorgusu yoktu. Doğrudan `run_scores` JOIN'i (bir çalıştırmanın birden fazla puan satırı olabilir — farklı yazar/yargıç) `GROUP BY variant` altındaki TÜM diğer toplamları (token, maliyet, sayım) ÇOĞALTIRDI; bu yüzden önce `run_avg_scores` CTE'siyle çalıştırma düzeyine indirilip SONRA `LEFT JOIN` yapıldı — kayıt çoğalması yok, puansız çalıştırmalar `NULL` (sıfır DEĞİL, "bilinmiyor") olarak kalır. `InMemoryRunStore` aynı iki-aşamalı ortalamayı `IRunScoreStore`'dan (zaten `GetStatisticsAsync`'te paylaşılan tekil örnek) okuyarak taklit eder.

### K-243

**K-243 — Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ**

`RunRecordingAgent.RunCoreAsync`/`RunCoreStreamingAsync` gelen `cancellationToken`'dan `CancellationTokenSource.CreateLinkedTokenSource` ile KENDİ kaynağını kurar ve deftere onu kaydeder. `IRunCancellationRegistry.TryCancel` bir kökü iptal ederken aynı `RootRunId`'yi taşıyan TÜM kayıtların kaynağını tek tek `Cancel()` eder — çocuğun kendi `cancellationToken`'ının kökten türetilip türetilmediğine bakmaz. Gerekçe: alt çalıştırma çağrısı MAF'ın arka plan görev tool'u üzerinden gelir ve bu zincirin gerçek çalışma anında token'ı nasıl ilettiği garanti edilebilir bir sözleşme değildir (bkz. Faz 32 devir notundaki terk edilen workflow testi). Kayıt bazlı cascade, token zincirinin gerçekte nasıl kurulduğundan bağımsız çalışır.

### K-264

**K-264 — Şablon içeriği `<None Pack>` ile paketlenir**

Resmi NuGet deseni (`learn.microsoft.com/nuget/reference/msbuild-targets#packagepath`). `ContentTargetFolders` KULLANILMAZ: açık `PackagePath` verildiğinde bu özellik hiç uygulanmıyor; birlikte kullanmak (`ContentTargetFolders=content` + item'in kendi `content/` kök yolu) "content/content/..." çift önekine yol açıyordu — ölçüldü. `%(RecursiveDir)` `content/**/*` deseninde "content/" SONRASINI verdiği için `PackagePath`'i elle `content/` ile başlatmak tek katmanlı doğru sonucu üretiyor. `<Content>` yerine `<None>` seçildi çünkü semantik olarak doğrusu bu (dosyalar derleme çıktısına kopyalanmaz); ancak K-262/K-263 düzeltildikten sonra izole test edildi ve `<Content>` da aynı `Pack`/`PackagePath` metadata'sıyla ÇALIŞIYOR — yani daha önce şüphelenilenin aksine `_PackageFiles`'ın boş kalmasının sebebi `<Content>` DEĞİL, K-262'deki boş sembol paketiydi.

### K-364

**K-364 — Oksuz hata parmak izi SABİT bir dize (`"orphaned"`); `ErrorFingerprint.Compute` ÇAĞRILMAZ (Faz 54)**

`ErrorFingerprint` `AgentPrism.Core`'da `internal`'dır; `SqlRunStore` (`AgentPrism.Sql.Shared`, ayrı derleme) ona erişemez (K-176'nın "paylaşılan dosya sağlayıcıya özgü ad alanına referans veremez" kuralının derleme-sınırı kardeşi). Alternatif olarak mesajı normalleştirip SHA-256 hash'ini elle yeniden üretmek kırılgan olurdu (algoritma değişirse iki uygulama sessizce ayrışır). Bütün oksuz çalıştırmalar zaten AYNI arızadır (süreç yanıt vermiyor); sabit bir dize hem `InMemoryRunStore` hem üç SQL sağlayıcısında BİREBİR aynı davranışı verir ve `RunStatistics.ByErrorClass` kümelemesinde tek bir küme (N oluşum) olarak görünür — mesaj metnindeki değişken zaman damgası yüzünden N ayrı kümeye bölünmez.

### K-426

**K-426 — Keşif turu kaydı `docs/kesif/<tarih>-<konu>.md` altında yaşar ve doküman dizin bütçesinden HARİÇ tutulur** *(kullanıcı kararı)**

K-412 deseninin aynısı: `docs/arsiv/` ve `docs/manuel-test/kosumlar/` gibi bu da bir **koşumun kaydıdır**, spec değildir; hiçbir oturum baştan sona okumaz, yalnız grep'lenir. Bütçeye sayılsaydı arşivlemek sayacı düşürmezdi ve disiplinin istediği davranış ödüllendirilmezdi. İki alternatif elendi: (a) turun tamamını `ADAYLAR.md`'ye yazmak — dosya 2026-08-16'da 67.195 B / 80.000 B'dir, tek bir tur onu bütçeye dayardı; (b) aday dosyasına yalnız işaretçi koyup ayrıntıyı notta bırakmak — `faz-planlama`'yı iki dosya okumaya zorlardı. Seçilen: onaylanan kalemin **tam metni** aday dosyasına gider, keşif notu tartışmanın kaydını tutar ve kalemden yalnız tek satırla söz eder.

### K-181

**K-181 — `AgentPrism.SqlServer` AOT uyumlu olarak İŞARETLENMEZ**

Ölçüldü (2026-08-04, `Microsoft.Data.SqlClient` 7.0.2, net10.0): `UseSqlServer()` çağıran bir konsol uygulaması `PublishAot=true` ile `-p:TrimmerSingleWarn=false -p:SuppressTrimAnalysisWarnings=false` altında yayınlandı. Sonuç: **sıfır** IL2xxx/IL3xxx uyarısı; native ikili üretildi ve çalıştırıldı (DI'dan `IRunStore` çözüldü). Buna rağmen `AgentPrismAotCompatible=false` bırakıldı: statik analiz temiz olsa da **canlı bir sunucuya karşı sorgu yolu AOT altında doğrulanmadı** (SQL Server container'ı geliştirme makinesinde koşturulamadı, aşağıdaki not) ve SqlClient'in Azure AD kimlik doğrulama sağlayıcıları yansımaya dayanır — tüketicinin kullanabileceği bir yoldur. Kütüphane bir vaadi ancak doğrulayabildiğinde verir. **Uyumsuzluk gizlenmiyor; vaat erteleniyor.**

### K-333

**K-333 — `OnlineEvalJobHandler` DI'da hem `IJobHandler` hem KENDİ somut tipiyle kayıtlıdır (Faz 49)**

`POST /api/runs/{id}/judge` ucu, iş kuyruğu isleyicisiyle AYNI kodu (`JudgeRunAsync`) paylaşmak için `OnlineEvalJobHandler`'ı somut tipiyle ister. `TryAddEnumerable(Singleton<IJobHandler, OnlineEvalJobHandler>())` yalnız arayüz üzerinden çözülebilen bir kayıt üretir; somut tip AYRICA kaydedilmezse uç `No service for type 'OnlineEvalJobHandler'` ile 500 döner — fonksiyonel testte YAKALANDI (birim testleri bu DI hatasını göremez, gerçek `IServiceProvider` kurmazlar). Çözüm: `TryAddSingleton<OnlineEvalJobHandler>()` + `TryAddEnumerable(Singleton<IJobHandler, OnlineEvalJobHandler>(provider => provider.GetRequiredService<OnlineEvalJobHandler>()))` — iki kayıt AYNI örneği paylaşır.

### K-234

**K-234 — Dil başına ses eşlemesi istemcide tutulur; protokol zaten taşıyordu**

🚨 Türkçe bir yanıtı İngilizce bir sesle seslendirmek sonucu anlaşılmaz kılar. Ama sağlayıcı bir sesin **hangi dili konuştuğunu bildirmez** — `VoiceDescriptor` yalnız `VoiceId`, `Name`, `Category` taşır — bu yüzden eşleme türetilemez. Operatör Ayarlar ekranında dil başına bir ses seçer; seçim `localStorage`'da durur ve konuşma paneli `start` çerçevesinde `voiceId` olarak gönderir. `VoiceClientMessage.VoiceId` bunu Faz 29'dan beri kabul ediyordu, dolayısıyla **sunucuda tek satır değişiklik yoktur** ve sunucu dil kavramı öğrenmez. Değerlendirilen alternatif `VoiceConversationOptions.VoiceId`'yi `Dictionary<string,string>` yapmaktı: sunucuya istemcinin dilini taşımayı ve yeni bir yapılandırma şeklini gerektirirdi.

### K-217

**K-217 — `AgentRunScope.SessionId` eklendi; oturumsuz yazılan ek saklama tarafından silinir**

🚨 Bir tool `AgentSession`'a erişemez; oturum kimliği `AgentSession.StateBag`'dedir. `AgentRunScope` bu alanı taşımadığı için `speak` tool'u eki `session_id = NULL` ile yazardı. `RetentionTargetRegistry`'nin `attachments` hedefi tam olarak `session_id IS NULL` satırlarını **sahipsiz** sayıp siler (Faz 25) — yani oturum hâlâ yaşarken transcript'teki ses kesim tarihinden sonra kaybolurdu. Birikme sorunundan kötüdür: sessiz veri kaybıdır. Alan `AgentRunScope`'a eklendi ve `RunRecordingAgent.PrepareRun` dolduruyor; alt çağrıya `AgentPrismRunOptions.SessionId` ile taşınıyor (MAF alt agent'a oturum geçirmez). Kapsamdaki kimlik `runs.session_id` sütununu **değiştirmez**: sütun "çalıştırma bu oturumla başlatıldı" der, kapsam "burada üretilen içerik bu oturuma aittir" der. Gerçek çalıştırmayla doğrulandı.

### K-213

**K-213 — Azure'ın Responses yüzeyi desteklenmiyor**

Ölçüldü (2026-08-05): `AzureOpenAIClient.GetResponsesClient()` Azure'a özgü bir istemci **döndürmüyor**; dönen tip `OpenAI.OpenAIClient+TopLevelResponsesClient`, yani OpenAI'ın taban sınıfı. Karşılaştırma: `GetChatClient()` Azure'a özgü `Azure.AI.OpenAI.Chat.AzureChatClient` döndürüyor. Yani SDK, Responses için Azure'un yol ve `api-version` şeklini **uygulamıyor**; çalışıp çalışmadığı gerçek bir Azure kaynağı olmadan denenemez. `AgentPrism.OpenAI` iki sağlayıcı adı (`openai`, `openai-responses`) kaydederken `AgentPrism.Azure` **tek** ad kaydeder. Doğrulanamayan bir kod yolunu yayınlamak, K-030'un sunucu tarafı depolama kısıtıyla birleşince iki katı belirsizlik üretirdi.

### K-478

**K-478 — Çalıştırma kimliği `IRunAttributionContext`'ten gelir; istek GÖVDESİNDEN asla alınmaz (Faz 68)**

Ölçüldü: faz öncesi `grep -rn "UserId" src --include="*.cs" \| wc -l` → **0**. `AgentRunRequest`'e `userId` eklemek, herhangi bir istemcinin BAŞKA bir kullanıcı adına harcama yazdırabilmesi demekti — maliyet kaydını sahteleştirirdi. Çözüm `ITenantContext`'in kardeşidir: `TryAdd` (tüketicinin kaydı kazanır, K4), varsayılan `DefaultRunAttributionContext` yalnız `AmbientRunAttributionScope`'u okur ve kapsam yoksa `null` döner — kayıt yapmayan uygulamanın davranışı BİREBİR aynı kalır (K1). Gerçek OpenAI çağrısıyla doğrulandı: gövdedeki `"userId":"ATTACKER"` yok sayıldı, kayıt `userId: "ada"` oldu. Değer OPAKTIR (Faz 64 duruşu). Attribution HER `run`'da yazılır (alt çağrılar dahil): kökte durmak, agent çağıran her agent'ta kullanıcı maliyetini eksik raporlardı. Ayrıntı: `docs/68-*.md`.

### K-302

**K-302 — `AddCaseAsync`'in `seq`/`source_run_id` eşzamanlılığı `ON CONFLICT`/`MERGE` değil, düz `INSERT` + `SqlDialect.IsUniqueViolation` yakalama + yeniden deneme ile çözülür**

`seq`, `MAX(seq)+1` alt sorgusuyla depoda atomik hesaplanır; iki eş zamanlı terfi aynı değeri hesaplayabilir ve bu GERÇEK bir `eval_cases_suite_seq_uq` ihlaline düşer (Postgres/SQLite'ın `ON CONFLICT DO NOTHING`'i SQL Server'da karşılığı olmadığı için kullanılmadı — K-177'nin "MERGE kullanılmaz" deseniyle tutarlı). `SqlEvalStore.AddCaseAsync` ihlali yakalar: `source_run_id` doluysa `SelectEvalCaseBySourceRun` ile mevcut vaka aranır (bulunursa `Created:false` ile döner — asıl "zaten terfi edilmiş" yolu budur, İSTİSNASIZ); bulunamazsa ihlal `seq` çakışmasıydı demektir ve taze bir `seq` ile yeniden denenir (en fazla 5 deneme). `EvalCaseSeqTests` (8 eşzamanlı `AddCaseAsync`) bunu PostgreSQL ve SQLite'ta doğruladı.

### K-366

**K-366 — `ClaimOrphanedRunsAsync` kapattığı çalıştırmanın `RunFailed` olayını da KENDİSİ yazar; ayrı bir yazma turu yok (Faz 54)**

`RunEventWriter` o çalıştırmayı yazan süreçte artık YOKTUR (süreç çökmüştür) — olayı biri yazmak zorundadır ve sıra numarası mevcut en büyük değerin bir fazlası olmalıdır. Store, `runs` UPDATE'inin RETURNING/OUTPUT'undan kapanan satırları okur, ardından her biri için `INSERT INTO run_events (... , seq, ...) VALUES (..., COALESCE((SELECT MAX(seq) FROM run_events WHERE run_id=@run_id), -1) + 1, ...)` çalıştırır — sıra numarası C# tarafında hesaplanmaz, tek bir SQL ifadesinde alt sorguyla türetilir. `samples/AgentPrism.Api` ile SQLite üzerinde gerçek bir oksuz satır üretilip uçtan uca doğrulandı: `GET /api/runs/{id}` `status:"Failed"`, `error.type:"orphaned"`, `error.class:"Infrastructure"` döndü ve olay akışında tek bir `RunFailed` (tip 7) kaydı oluştu.

### K-347

**K-347 — K-218 kapatıldı: `ToolMethodScanner` örnek metotları TARAMA ANINDA reddeder (Faz 52)**

Eski kod `arguments.Services is { } services ? ... : throw` deseniyle reddi ÇAĞRI ANINA erteliyordu; MAF `Services`'e her zaman `EmptyServiceProvider` (asla `null`) geçirdiği için `throw` dalı hiç çalışmıyordu — ölü kod, K-218'in yan bulgusu. Onarım: `CreateFunction` `!method.IsStatic` denetimini `Scan()` içinde, ilk tool çağrısını beklemeden yapar; `AgentPrismException` tarama anında (uygulama açılışında, `AddToolsFrom` çağrıldığı an) fırlar. `ToolMethodScanner`'ın "örnek metotlar `Services` üzerinden servis alabilir" diyen yanlış XML dokümanı da düzeltildi. Regresyon: `ToolRegistrationTests.AddToolsFrom_ornek_metodunu_tarama_aninda_reddeder`.

### K-266

**K-266 — Üretilen `OrderTools.cs` `using AgentPrism;` taşır**

C#'ın kapsayan ad alanı arama kuralı (`namespace A.B` içinden `namespace A`'daki üyelere niteliksiz erişim) yalnız ad alanı GERÇEKTEN "AgentPrism" ile başladığı sürece çalışır. Şablonun `sourceName` mekanizması "AgentPrism.Starter" metnini `-n` ile verilen HER ADA (örn. `Calisma.Deneme`) döner — bu, `namespace AgentPrism.Starter;`'ı da kapsar. Yeniden adlandırılan projede ad alanı artık "AgentPrism" ile başlamaz ve kapsayan-ad-alanı kısayolu SESSİZCE kırılır. Ölçüldü: `TemplateInstantiationTests`/`TemplateRunTests` (3 test) üretilen projede `CS0246: AgentPrismToolAttribute bulunamadı` ile başarısız oldu — `samples/AgentPrism.Api/OrderTools.cs` aynı kısayola güvenir ama HİÇBİR ZAMAN yeniden adlandırılmadığı için bugün yakalanmamıştı. Düzeltme: açık `using AgentPrism;`, yeniden adlandırmadan bağımsız çalışır.

### K-263

**K-263 — Şablonda `TargetFrameworks` boşaltılır**

`src/Directory.Build.props` `TargetFrameworks=net8.0;net9.0;net10.0` atar; projede yalnız `TargetFramework` (tekil) `net10.0` yazmak `dotnet build`i tek çıktıya indirger ama `dotnet pack`in çapraz-hedefleme orkestrasyonu (`_GetFrameworksWithSuppressedDependencies`) çoğul değeri okumaya devam eder ve üç ayrı iç derleme (`net8.0`/`net9.0`/`net10.0`) başlatır — ölçüldü: `dotnet build` tek TFM verirken `dotnet pack` çıktısında `_net10.0` klasör soneki ve üç paralel `MSBuild` alt görevi görülüyordu. Şablon hiç derlenmediği için TFM'in gerçek bir anlamı yok; `TargetFrameworks` boşaltmak orkestrasyonu tek TFM'e sabitler ve gereksiz üçe katlanan işi ortadan kaldırır. Bu, K-262'nin `NU5017` düzeltmesi için ZORUNLU DEĞİLDİR (izole test edildi — `IncludeSymbols=false` tek başına yeterliydi); saf bir verimlilik/basitlik kararıdır.

### K-036

**K-036 — OpenAI uyumlu uçlar AgentPrism tarafından yazılır, MAF'ın `Map*` uçlarıyla değil** *(kullanıcı kararı)**

Bkz. bölüm 1. Kullanılan yol paketin **public** yardımcısı `OpenAIResponses`: `ToAgentRunRequest` (gövde → `Messages`/`Options`/`ConversationId`/`PreviousResponseId`), `GetSessionStoreId`, `CreateResponseId`, `WriteResponse`, `WriteResponseStreamAsync` (hazır SSE çerçeveleri). Kablo biçimi MAF'tan geldiği için stok SDK uyumu korunur; agent çözümleme, kalıcılık, kiracı yalıtımı ve çalıştırma kaydı AgentPrism'e kalır. Doğrulandı: `openai` 2.52.0 Python SDK'sı ile `responses.create`, `previous_response_id` zinciri, `responses.stream`, `chat.completions` (akışlı ve akışsız) ve `NotFoundError` çözümleme uçtan uca çalışıyor. Chat Completions için public yazıcı yardımcısı **yoktur**; o biçim elle üretilir.

### K-482

**K-482 — Token kırılımı toplamların İÇİNDE sayılır; bildirilmeyen sayaç `null` kalır, `0` OLMAZ (Faz 68)**

Sözleşme MEAI'den devralındı: `UsageDetails` "cached input tokens should be counted as part of `InputTokenCount`" der; `SUM(input) + SUM(cached)` aynı token'ı iki kez sayar. İkinci kural ölçülebilir bir ayrımdır: `0` "ölçüldü, yoktu" İDDİASIDIR, `null` "hiç ölçülmedi" der — birleştiren bir rapor, susan HER sağlayıcı için kendinden emin bir %0 cache isabet oranı gösterirdi. Üç toplama noktasında uygulandı: `MergeUsage` (`AddOrNull`), `CompactionUsageAccumulator` (sayaç başına ayrı "bildirildi mi" bayrağı — sıfır toplamını "bildirilmedi" saymak aynı hata), `TreeUsage` (SQL'de `COALESCE(...,0)` KASITLI olarak uygulanmadı). Gerçek OpenAI çağrısında aynı satırda doğrulandı: `cachedInputTokens: 0` (bildirildi) ile `audioInputTokens: null` (bildirilmedi) yan yana.

### K-295

**K-295 — `ByErrorClass` ayrı bir depo metodu değil, `GetStatisticsAsync`'in genişletilmiş sonucu; `/api/stats/errors` o sonucun dar bir dilimi**

Plan bir `RunErrorStatisticsQuery`/ayrı uç önerebilirdi, ama `ByAgent`/`ByModel`/`ByVersion` zaten AYNI `GetStatisticsAsync` çağrısının kırılımlarıdır (K-141'in deseni) — hata kırılımı da doğal olarak oraya katıldı. SQL tarafında bu, PostgreSQL/SQL Server/SQLite'ın PAYLAŞTIĞI çok-sonuç-kümeli `SelectRunStatistics` toplu sorgusuna beşinci (sınıf toplamı) ve altıncı (kümeler) sonuç kümesi eklemek anlamına geldi — ayrı bir yuvarlanış gerekmedi. `/api/stats/errors` bu yüzden yeni bir sorgu yolu AÇMAZ; yalnızca `statistics.ByErrorClass`'ı döndüren ince bir uçtur (küçük payload isteyen panolar için).

### K-274

**K-274 — Aynı statü koduna birden fazla `.Produces` çağrısı yapılmaz; çoklu içerik tipi TEK çağrıya `additionalContentTypes` ile yazılır**

Ölçüldü (Faz 40): `/v1/responses` ve `/v1/chat/completions` hem JSON hem SSE döndürür (govdedeki `stream` bayrağına göre). İki ayrı `.Produces(200, ...)` çağrısı denendi — ikincisi birinciyi ezdi (K-272 ile aynı "son çağrı kazanır" deseni, farklı metadata tipi). Çözüm: `.Produces<T>(200, contentType: "application/json", additionalContentTypes: ["text/event-stream"])` — iki içerik tipi de aynı `T` şemasını paylaşır; ASP.NET Core'un metadata modeli aynı statü için iki FARKLI tip ifade edemez (chat completions'ta akışlı gövde gerçekte `ChatCompletionChunk`'tır, şema `ChatCompletion`'a yaklaştırıldı — bilinen, kabul edilen yaklaşıklık).

### K-298

**K-298 — Parmak izi normalleştirmesi tırnak içi metni SİLMEZ (Açık Soru 3 → C)**

Doç 44.4'ün açık sorusu ölçüm istiyordu; ölçüm şu gözlemle karara bağlandı: bir tool hatası mesajı çoğunlukla `Tool '<ad>' calisirken hata olustu` biçimindedir ve `<ad>` iki farklı arızayı ayırt eden TEK bilgidir — silinirse `refund_order` ile `track_shipment` hataları aynı kümede toplanır ve "hangi tool bozuk" sorusu kümeden cevaplanamaz hale gelir. GUID/sayı/tarih temizliği zaten mesajların çoğunluk gürültüsünü (kimlik, zaman damgası) kaldırıyor; kalan tırnak içi metnin kümeleme değeri, birleştirme riskinden yüksek bulundu. `ErrorFingerprintTests.Farkli_tool_adlari_tasiyan_mesajlar_ayri_kumede_kalir` bu kararı kilitler.

### K-128

**K-128 — Bekleyen insan istekleri için tablo açılmadı; olay yükünde yaşarlar**

İstek bir `RunEventType.WorkflowRequest` olayının `Payload` alanındadır (`requestId`, `portId`, `requestType`, `responseType`, `prompt`, `form`). Olay akışı zaten append-only (K-014), kiracı filtreli ve sayfalanabilir; ikinci bir kayıt hattı aynı bilgiyi iki yerde tutar ve zamanla ayrışırdı. Yük ayrıca `RecordToolPayloads` ayarına **tabi değildir** — K-089'un ("denetim izine yazılamayan script çalışmaz") aynı gerekçesi: burada yük bir gözlem ayrıntısı değil işlevin kendisidir, susturulursa kullanıcı cevaplayacağı soruyu hiç göremez. Yanıt verirken gerçek `ExternalRequest` nesnesi saklanmaz: kontrol noktasından sürdürülen yürütme aynı `RequestId` ile aynı isteği **yeniden yayınlar** (ölçüldü).

### K-215

**K-215 — Ses sözleşmeleri `AgentPrism.Abstractions`'ta yaşar; ElevenLabs bir uygulamadır**

`ISpeechSynthesizer`, `ISpeechTranscriber`, `IVoiceHealthCheck`, `IVoicePricingReader` ve taşıdıkları tipler `AgentPrism.Voice`'ta **değil** `Abstractions`'ta durur. Sebep paket yönü kuralıdır: `AgentPrism.AspNetCore` ses uçlarını (`/api/voice/health`, `/voices`, `/speak`) sunarken bu tipleri görmek zorundadır ama `AgentPrism.Voice`'a referans **veremez**. Aynı desen MCP soyutlamalarında uygulanmıştı (K-174). Kazanç ölçülebilir: hem `AgentPrism.AspNetCore.FunctionalTests` hem `AgentPrism.Ui.E2ETests` ses uçlarını `AgentPrism.Voice`'a **hiç referans vermeden**, yalnızca soyutlamayı uygulayan bir sahte sınıfla test ediyor. Başka bir sağlayıcı isteyen kendi uygulamasını `UseVoice`'tan önce kaydeder; `TryAdd*` onunkini korur (test: `Tuketicinin_kendi_uygulamasi_KORUNUR`).

### K-359

**K-359 — `Authorization` başlığı sunulduğunda, statik `AuthToken` tanımsız olsa bile doğrulanmalıdır; eşleşmezse 401 (Faz 53, bilinçli davranış değişikliği)**

Eski davranış: `AuthToken` ayarlanmamışsa filtre hiçbir bearer denetimi yapmıyordu — başlık ne taşırsa taşısın istek geçiyordu. API anahtarı ikinci bir kimlik kaynağı olarak eklenince bu sessiz geçiş, geçersiz bir anahtar denemesinin farkedilmeden reddedilmesi gerektiği bir güvenlik boşluğuna dönüştü. Yeni kural: başlık YOKSA eski davranış birebir korunur (K1, sıfır sürpriz, ek DB sorgusu yok); başlık VARSA statik veya API anahtarıyla eşleşmelidir, aksi halde 401. Mevcut fonksiyonel test paketi (tüm `Authorization` başlıklı testler zaten `AuthToken` ayarlıyordu) bu değişiklikten etkilenmedi; yeni davranış `ApiKeyAuthenticationTests.Statik_token_tanimsizken_bilinmeyen_deger_401_alir` ile doğrulandı.

### K-378

**K-378 — Kademeli artırma adımı denetim izine YAZILMAZ; yalnız otomatik GERİ ALMA `IAuditLog.WriteAsync` ile mutasyondan ÖNCE (K-089 emsali) yazılır (Faz 56)**

56.2'nin akış şeması yalnız "GERİ AL" dalından sonra `audit_log'a YAZ` kutusu taşır; "ağırlığı bir adım artır" dalında böyle bir kutu YOKTUR. Bu bilinçli bir ayrımdır: geri alma trafiği KESEN, insan onayı olmadan gerçekleşen ve gerekçesi sonradan incelenmesi gereken bir eylemdir (K-089 sınıfı); ramp adımı ise yalnızca trafik payını artıran, geri alınabilir (bir sonraki geri alma veya elle durdurma ile) bir eylemdir. `AuditingExperimentStore.AdvanceCanaryRampAsync` bu yüzden şeffaf bir geçiştir (denetlemez); `RollbackCanaryAsync` de öyledir çünkü denetim `CanaryEvaluationService.RollbackAsync` tarafından DOĞRUDAN, çağrıdan ÖNCE yazılır — yazma başarısız olursa `RollbackCanaryAsync` HİÇ çağrılmaz.

### K-336

**K-336 — A2A her disa acik agent icin AYRI bir alt yol ve AYRI bir agent karti kullanir; tekil kart varsayimi terk edildi (Faz 50)**

Plan taslağının doğrulama örneği `/agentprism/a2a/.well-known/agent-card.json` (tekil, agent adı yok) varsayıyordu. Ölçüldü: A2A protokolü bir sunucuyu bir agent kimliği olarak modeller (`AddA2AServer` de agent adıyla KEYED kayıt yapar); birden çok agent'ı AYNI kart altında yayımlamanın SDK'da bir yolu yoktur. Çözüm: her `ExposedAgents` girdisi `{prefix}/{agent}` alt grubuna bağlanır (`MapA2A(handler, "/")` + `MapWellKnownAgentCard(card, "")`), kart o alt yolun `.well-known/agent-card.json`'unda yayımlanır. Gerçek koşumla doğrulandı (`samples/AgentPrism.Api`, `curl http://localhost:5080/agentprism/a2a/ozetleyici/.well-known/agent-card.json`).

### K-223

**K-223 — `MapAgentPrism` `UseWebSockets()`'i koşullu olarak kendisi kurar**

Kestrel `IHttpWebSocketFeature` sağlamaz; onu `WebSocketMiddleware` kurar. Tüketiciden ayrıca `app.UseWebSockets()` istemek `MapAgentPrism`'in **tek giriş noktası** olma kuralını (K1) bozardı ve eksiklik yalnızca ilk konuşma denemesinde, anlamsız bir hatayla görünürdü. Ara yazılım **yalnızca** `VoiceConversationDriver` kayıtlıyken (yani tüketici `UseVoiceConversation()` çağırdıysa) ve `endpoints` bir `IApplicationBuilder` iken kurulur: yeteneği açan zaten onun taşımasını istemiştir, dolayısıyla sürpriz değildir. Zaten kuruluysa ikinci `WebSocketMiddleware` örneği `IHttpWebSocketFeature`'ı dolu bulur ve dokunmadan geçer.

### K-077

**K-077 — Denetim izi dekoratörleri, genel bir `Decorate<T>` yardımcısı yerine her paketin kendi kaydında sarılır**

`AddAgentPrism()` bellek içi depoları doğrudan `AuditingXStore` ile sarılmış olarak kaydeder (`new AuditingAgentDefinitionStore(new InMemoryAgentDefinitionStore(), ...)`); `UsePostgreSql()` aynı dekoratörleri `PostgresXStore` ile sarar (`ActivatorUtilities.CreateInstance` ile kurularak). Genel bir `services.Decorate<TService>(...)` yardımcısı (Scrutor deseni) çağrı sırasına duyarlı olurdu: `UsePostgreSql()` `AddAgentPrism()`'den **sonra** çalışır ve mevcut kaydı `Replace` ile değiştirir (K-025); dekorasyon kaydı yanlış sırada eklenirse ya bellek içi depoyu sarardı ya da hiç etkisi olmazdı. Her paketin kendi kaydını sarması, sıralama varsayımı olmadan doğru sonucu garanti eder.

### K-437

**K-437 — İstemciden gelen tool sonucu modele `ChatRole.Tool` altında gönderilir, `ChatRole.User` değil**

Onay yanıtları (`ToolApprovalResponseContent`) bu depoda `ChatRole.User` altında gönderiliyor (`ToolApprovalResolver`, `ApprovalResumeJobHandler`) — ama bu farklı bir içerik tipi. `FunctionResultContent` için doğru rol gerçek bir OpenAI çağrısıyla ölçüldü: `gpt-5.4-mini`'ye bildirim-yalnız bir tool tanıtılıp çağrısı alındı, sonuç `new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, sonuç)])` olarak geri gönderildi — tur başarıyla tamamlandı ve model sonucu doğru kullandı. `ChatRole.User` denenmedi çünkü OpenAI'nin `tool_call_id` eşleşmesi rol bazlı çalışır; standart sözleşmeden sapmak gereksiz risk taşırdı.

### K-349

**K-349 — Kaynak üretecinin Roslyn sürümü `Microsoft.CodeAnalysis.CSharp` `4.8.0`'dır (Faz 52)**

Plan taslağı `4.14.0` öneriyordu ama "uygulama anında ölçülmelidir" diye işaretlemişti. Ölçüm (`AssemblyName.GetAssemblyName`/`FileVersionInfo`, bu makinede kurulu üç SDK): .NET 8.0.100 SDK GA → Roslyn `4.8.0` (dotnet/roslyn#70919, WebSearch ile doğrulandı — bu makinede 8.x SDK kurulu değildi); .NET 9 SDK (9.0.305/9.0.306, kurulu) → `4.14.0`; .NET 10 SDK (10.0.100, `global.json`'daki) → `5.0.0`. AgentPrism net8.0'ı da hedeflediği (K-005) ve bir tüketicinin net8.0 projesini eski bir 8.0.1xx SDK ile derleyebileceği için taban EN ESKİ desteklenen SDK'nın Roslyn'idir — daha yeni bir sürüm seçmek üreteci o SDK'da SESSİZCE devre dışı bırakırdı.

### K-287

**K-287 — Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil**

Faz 42 planının Açık Soru 5'inde B seçeneği (uygulama saati) bilinçli seçildi: `SqlSingletonLeaseStore` `DateTimeOffset.UtcNow`'ı C# tarafında hesaplar — `SqlJobStore.LeaseAsync`/`RenewLeaseAsync`'in (Faz 17) zaten yaptığıyla birebir aynı desen. Veritabanının kendi saatini (`NOW()`/`GETUTCDATE()`/`CURRENT_TIMESTAMP`) kullanmak üç sağlayıcıda üç farklı SQL ifadesi ve üç farklı hassasiyet demektir; `jobs` kirası aynı basitleştirmeyi yapıyor ve üretimde sorun çıkarmadı. Saat kayması riski çoğu dağıtımın NTP ile senkronize sunucular kullanmasıyla küçüktür ve mevcut `jobs` kira mekanizmasıyla zaten paylaşılan bir risktir.

### K-267

**K-267 — `ModelBinding.ResponseFormat` yetenek denetimi yalnız `Json`/`JsonSchema` kiplerinde çalışır**

`ModelDescriptor.SupportsStructuredOutput` bayrağının anlamı "model JSON semasina uyan cikti uretebiliyor mu"dur; bu yalnızca `Json` (sağlayıcının JSON-modu API'si) ve `JsonSchema` (şema dayatma) kiplerinde anlamlıdır. `AgentResponseFormatKind.Text` hiçbir sağlayıcıya özel yüzey istemez — "açıkça düz metin iste" talimatı her modelde çalışır, `null` (hiçbir kısıt yok) davranışından yalnız görünürlük açısından farklıdır (bkz. K1). `Text`'i de reddetmek, yanlış `SupportsStructuredOutput=false` bayrağı yüzünden çalışması gereken bir agent'ı gereksiz yere kırardı — bu K1'in "yanlış false anlaşılır derleme hatası üretir" gerekçesini kendi kendine baltalardı.

### K-297

**K-297 — `quota_exceeded` sınıfı otomatik sınıflandırıcı için YAPISAL olarak ulaşılamazdır; taksonomide kalır ama örnek uygulamada uçtan uca gösterilemedi**

`QuotaGate` (Faz 21) bir çalıştırma `RunRecordingAgent`'a hiç ulaşmadan `429`'u HTTP katmanında döndürür (K-162: "devam eden bir çalıştırma kota aşılınca kesilmez" kuralının doğal sonucu) — kota aşımı hiçbir zaman bir `RunError` üretmez. Aday listesinin "kota aşımı gerçek bir çalıştırmada üretilir" beklentisi bu yüzden karşılanamadı; sınıf yine de taksonomide kalır (kendi sınıflandırıcısını yazan bir tüketici veya ileride eklenecek farklı bir kota mekanizması için) ve birim testiyle (mesajda "kota"/"quota" geçen sentetik bir `RunError`) doğrulandı.

### K-240

**K-240 — `ScoredRuns`/`PositiveRate` iki yolla hesaplanır**

`IRunScoreStore` bilinçli olarak `IRunStore`'dan ayrı tutuldu (açık soru 1) ama istatistik özeti `IRunStore.GetStatisticsAsync`'in bir parçası olmak zorunda (`/api/stats` tek çağrıdır). SQL sağlayıcılarında çözüm ucuzdu: `SelectRunStatistics` sorgusuna `run_scores` tablosuna bakan iki skaler alt sorgu eklendi (aynı kiracı/eval/agent/tarih filtresi tekrarlanarak) — `IRunScoreStore`'a hiç dokunmadan tek sorguda gelir. Bellek içi uygulamada bu mümkün değildi: `InMemoryRunScoreStore`'un verisine `InMemoryRunStore`'un doğrudan erişimi yoktu. Çözüm `InMemoryRunStore`'a **isteğe bağlı** bir `IRunScoreStore` kurucu parametresi eklemek oldu (bkz. K-241); özet döngüsü her eşleşen çalıştırma için `ListAsync`'i çağırır (N+1, bellek içi depoda kabul edilebilir — üretim yolu zaten SQL'dir).

### K-160

**K-160 — Webhook teslimi Faz 17'nin kuyruğunu kullanır; `IJobStore` geri adımlı beklemeyle genişletildi** *(kullanıcı kararı)**

Faz 21 dokümanı kendi içinde çelişiyordu: karar "ikinci kuyruk yazma" derken `webhook_deliveries` tablosu kendi `next_attempt_at`/`attempt` sütunlarını taşıyordu. Faz 17'nin kuyruğu geri adımlı bekleme yapamıyordu (`ReleaseForRetryAsync` gecikme almıyordu, `MaxAttempts` genel bir ayardı). Çözüm: `ReleaseForRetryAsync`'e opsiyonel `retryAfter` ve `JobRecord`'a iş başına `MaxAttempts` eklendi; gecikme `jobs.scheduled_for`'u ileri taşır ve kiralama sorgusu zaten `scheduled_for <= now` süzer. Böylece tek kuyruk, tek kiralama, tek işçi kalır. `webhook_deliveries` saf **geçmiş** tablosudur; `next_attempt_at` sütunu hiç eklenmedi. Public API'yi değiştirmenin ucuz olduğu an budur — Faz 7 yayını beklemektedir (K-068).

### K-064

**K-064 — İkinci faz önceliği: yetenek derinliği** *(kullanıcı kararı)**

Kullanıcıya üç ölçüt soruldu: kullanıcı sayısı (F-03, F-06), kurumsal satın alma (F-20, F-21) veya yetenek derinliği (F-09, F-10, F-27). Cevap **yetenek derinliği**. Sıra buna göre kuruldu: skill'ler (Faz 10–11), agent çağrı grafiği (12), bağlam yönetimi (13), çok modluluk (14) ve workflows (15–16) öne alındı; kurumsal kalemler (SQL Server, Azure) sona bırakıldı. İki istisna gerekçeyle önde tutuldu: **F-03** (Faz 8) tek başına Claude, Gemini, Groq ve yerel modelleri açar ve sonraki her fazın geliştirme maliyetini düşürür; **F-20/F-21** (Faz 9) script çalıştırmanın (Faz 11) güvenlik önkoşuludur. Ayrıntı: [`arsiv/IKINCI-FAZ-YOL-HARITASI.md`](arsiv/IKINCI-FAZ-YOL-HARITASI.md).

### K-226

**K-226 — Artımlı (geçici) transkript yok; çözüm tek atımlıdır** *(kullanıcı kararı)**

İstemci VAD'i sessizliği görüp `commit` gönderir; sunucu biriken sesi tek bir `TranscribeAsync` çağrısıyla çözer. Artımlı çözüm sağlayıcının realtime STT WebSocket'ini gerektirir ve o sözleşme **gerçek abonelik olmadan doğrulanamaz** — Faz 28'de taklit uçla iki belirsizlik zaten açık kaldı (faturalanan karakter başlığı, gerçek MP3 ilk baytları). Uygulaması olmayan bir `IStreamingSpeechTranscriber` arayüzü yazmak ise depoda ölü bir soyutlama bırakırdı. Protokoldeki `final` alanı yerinde durur ve hep `true` gelir; eksiklik faz dokümanının "Sağlanamayan Şeyler" bölümüne yazıldı.

### K-480

**K-480 — Sınır aşımı KIRPILMAZ, REDDEDİLİR; gürültülü sınır HTTP'de (`400`), sessiz düşürme kayıt yolundadır (Faz 68)**

Sınırlar: 8 etiket, anahtar 64, değer 256, kullanıcı kimliği 200 karakter (sonuncusu SQL Server'ın `nvarchar(max)` INDEKSLENEMEZ kısıtından). Kırpma reddedildi: kırpılmış bir etiket kümesi raporu sonradan okuyana EKSİKSİZ bir ölçüm gibi görünür. İki davranış bilinçlidir: (1) `RunAttributionGate` `run` başlamadan `400` döner ve sınırı sayıyla söyler — gerçek istekle doğrulandı (`"The run carries 9 labels; at most 8 are allowed."`, `totalRuns` değişmedi); `AmbientRunAttributionScope.Begin` `ArgumentException` atar. (2) `RunAttributionReader` derinlikte güvenlik ağıdır: geçersiz kümeyi BÜTÜN olarak düşürüp uyarı loglar, istisna ATMAZ — "gözlemlenebilirlik işlevselliği bozmaz" başlamış bir `run`'ı kesmeyi yasaklar.

### K-301

**K-301 — Çok turluluk, "bu oturumda DAHA ÖNCE başlamış başka bir çalıştırma var mı" sorusuyla belirlenir; tam konuşma geçmişi okunmaz**

K-300 ile sorgu artık oturumdan değil `run_events`'ten geldiği için tam transkript okumaya (ve onun `ISessionStore`/`IAgentCatalog`/`ChatHistoryProvider` bağımlılığına) gerek kalmadı. `RunToCasePromoter.HasEarlierRunInSameSessionAsync`, `IRunStore.QueryRunsAsync(new RunQuery { SessionId = ..., OnlyRootRuns = false })` ile aynı `sessionId`'de bu çalıştırmadan ÖNCE başlamış başka bir çalıştırma arar; varsa 409 (`MultiTurn`) döner. Gerekçe: bu çalıştırmanın `query`'si TEK BAŞINA, önceki turların bağlamı olmadan, orijinal davranışı yeniden üretmeye yetmez (K-034'ün sessiz bağlam kaybı yasağı).

### K-423

**K-423 — RS0041 (oblivious reference type) `AgentPrism.Abstractions.csproj`'da tek satırlık `NoWarn` ile bastırıldı; kök neden ölçüldü**

51 örneğin TAMAMI tek kaynağa izlendi: `WebhookEventPayloadJsonContext` (System.Text.Json kaynak üreteci çıktısı, `obj/.../System.Text.Json.SourceGeneration/*.g.cs`) — 17 üye × 3 TFM (net8/9/10). Bu, Microsoft'un kendi üreteç çıktısıdır; kaynak elle değiştirilemez. `dotnet format analyzers`'ın RS0016 için otomatik düzeltmesi bu üyeleri de EKLEYEBİLDİ (`PublicAPI.Unshipped.txt`'ye elle eklendi, üreteç dosyalarını değiştiremediği için), ama RS0041'in "oblivious" uyarısı kaynağın kendisinden geliyor ve düzeltilemez. Bastırma tek kurala (RS0041) ve tek projeye (`Abstractions`) sınırlı; diğer sekiz kural açık.

### K-154

**K-154 — Yeniden hesaplama saglayiciyi bilmez; model adiyla alfabetik ilk eşleşen kazanır**

`runs` tablosuna saglayici sütunu eklenmedi (yalnız `model_id` vardır — bkz. K-155'in aksine bilinçli kapsam sınırı). Canlı hesaplamada saglayici `ModelBinding.Provider`'dan bilinir ve doğru fiyatı seçer; ama geçmiş bir satırın hangi saglayiciya ait olduğu hiç kaydedilmemiştir. `RunPricingResolver.Resolve(provider: null, ...)` tek, birleşik kural kullanır: hem canlı hem yeniden hesaplama aynı kodu çalıştırır, yalnız `provider` argümanı farklıdır. Aynı model adı birden fazla saglayicida farklı fiyatla tanımlıysa alfabetik ilk saglayici sessizce kazanır — bu kabul edilmiş, dokümante edilmiş bir sınırlamadır, yeniden açılacak bir hata değil.

### K-285

**K-285 — `SingletonGuard` `public`tir; "internal yardımcı" planı uygulanamadı**

Faz 42 planı `SingletonGuard`'ı `AgentPrism.Core` içinde `internal` bir yardımcı olarak tasarlamıştı ve `McpDiscoveryService` (`AgentPrism.Mcp`) ile `ModelProviderHealthBackgroundService`'in (`AgentPrism.Core`) AYNI yardımcıyı kullanmasını istiyordu. `InternalsVisibleTo` yalnız `$(MSBuildProjectName).UnitTests/.IntegrationTests/.FunctionalTests`'i kapsar (`Directory.Build.props`) — kardeş paketleri (Mcp → Core) KAPSAMAZ. Kod paylaşımı gereksinimi kazandı: tip `public` oldu. Genel yüzey büyümesi kabul edildi çünkü `SingletonGuard` kendi veri modelini tanımlamaz, yalnız zaten public olan `ISingletonLeaseStore`/`SingletonExecutionOptions`'ı orkestre eder.

### K-436

**K-436 — İstemci tool'u bildirimi `AIFunctionFactory.Create(...).AsDeclarationOnly()` değil `AIFunctionFactory.CreateDeclaration(...)` ile kurulur**

Planın Açık Soru 2'si iki seçenek arasında kararsızdı; ikisi de ölçülmemişti. Üçüncü, ölçülmüş bir yol bulundu: `AIFunctionFactory.CreateDeclaration(name, description, jsonSchema, returnJsonSchema)` doğrudan `AIFunctionDeclaration` döndürüyor — sahte bir gövde fonksiyonu kurup sonra `AsDeclarationOnly()` ile atmaya gerek yok. Ölçüldü (probe programı): dönen tip `AIFunctionFactory+DefaultAIFunctionDeclaration`, `is AIFunction` → `false`, `is AITool` → `true`, `Name`/`Description`/`JsonSchema` doğru taşınıyor. `AddClientTool`'un gövdesi bu yüzden plandakinden daha basit.

### K-168

**K-168 — MCP OAuth yalnız Mod 1 (Authorization Code); Mod 0 SDK'da yok** 🚨**

Faz 22 planı iki mod varsayıyordu: Mod 0 (istemci kimlik bilgileri, etkileşimsiz) ve Mod 1 (yetkilendirme kodu). Kullanıcı önce yalnız Mod 0'ı seçti. `ModelContextProtocol.Core` 2.0.0'ın `ClientOAuthOptions` tipini reflection ile doğrularken `RedirectUri`'nin **zorunlu** (`required`) bir üye olduğu ve SDK'nın XML belgesinin, `AuthorizationCallbackHandler` verilmezse "varsayılan uygulama kullanıcıdan tam yönlendirme URL'sini elle girmesini ister" dediği ortaya çıktı — SDK'da client_credentials tarzı etkileşimsiz bir akış **hiç yok**. Kullanıcıya bulgu sunuldu, "Mod 1'i şimdi yap" seçildi (önceki kararın tersine çevrilmesi). `McpOAuthAuthorizationMode` enum'u bu yüzden tek üyelidir (`AuthorizationCode = 0`).

### K-459

**K-459 — Denetim zinciri "tenant'ın son yazılan satırı" sorgusu `ORDER BY created_at, id` YERİNE ayrı bir sıra sütunuyla (`chain_seq`: PostgreSQL `GENERATED ... AS IDENTITY`, SQL Server `SEQUENCE` + `DEFAULT NEXT VALUE FOR`, SQLite yerleşik `rowid`) bulunur**

Gerçek PostgreSQL'e karşı ölçüldü: uuid v7 (`id`) alt bitleri RASTGELEdir, ekleme sırasını yansıtmaz; 20 eşzamanlı yazıcı testi (`AuditLogContract.Concurrent_writes_for_one_tenant_produce_a_single_valid_chain`) bu yüzden tekrarlanan `23505` ile YAKALANMADAN kilitlendi — bir yazıcının rastgele büyük `id`'si "son" olarak SONSUZA KADAR raporlanabiliyor, yeni satırlar eklense bile. Veritabanının atadığı gerçek bir artan sayaç bu belirsizliği ortadan kaldırır. SQL Server'a `IDENTITY` var olan bir tabloya ALTER TABLE ile eklenemediği için `SEQUENCE` deseni seçildi.

### K-435

**K-435 — `IToolRegistry`/`AgentPrismToolRegistration` `AITool`'a değil `AIFunctionDeclaration`'a genişledi**

F-108 planı `AIFunction -> AITool` öngörüyordu (istemci tool'unun `AIFunction` olmadığı için). Ölçüldü: `AITool` tabanının kendisi `.JsonSchema` taşımıyor — yalnız `AIFunctionDeclaration` (ve onun altındaki `AIFunction`) taşıyor. `ToolRegistry`'nin descriptor üretimi şemaya ihtiyaç duyduğu için `AITool` ile `CS1061` verdi. `AIFunctionDeclaration` hem gerçek `AIFunction`'ı (`AIFunction : AIFunctionDeclaration`) hem `AddClientTool`'un ürettiği bildirim-yalnız tipi (`AIFunctionFactory.CreateDeclaration` çıktısı, ölçüldü: `DefaultAIFunctionDeclaration`, `is AIFunction` → `false`) kapsıyor — plandaki `AITool`'dan daha DAR ve daha doğru bir sınır.

### K-030

**K-030 — Responses API her zaman sunucu tarafı depolama kapalı çalışır**

Ölçüldü: `GetResponsesClient().AsIChatClient(model)` kullanıldığında OpenAI bir konuşma kimliği döndürüyor ve `ChatClientAgent.UpdateSessionConversationId` şu hatayı atıyor: *"Only ConversationId or ChatHistoryProvider may be used, but not both."* `UsePostgreSql()` derlenen **her** agent'a bir `ChatHistoryProvider` bağlar (Faz 2), dolayısıyla iki mekanizma aynı anda çalışamaz. Hata yalnızca PostgreSQL açıkken ortaya çıkar; bellek içi kurulumda sessizce çalışır — yani üretimde patlayan, geliştirmede görünmeyen bir hata sınıfı. Çözüm: `AsIChatClientWithStoredOutputDisabled(model)`. Geçmiş AgentPrism'in veritabanında kalır; denetim izi, kiracı yalıtımı ve replay korunur.

### K-326

**K-326 — `RunErrorClass.ContentBlocked` `ContentFiltered`'dan AYRIDIR**

Faz 44'ün taksonomisinde `ContentFiltered = 5`'in tanımı "model yanıtı SAĞLAYICININ güvenlik/içerik filtresiyle kesildi"dir. Bir guard kararını aynı kovaya yazmak operatörün "model reddetti" ile "bizim politikamız reddetti" ayrımını kaybetmesine yol açardı; karşılık gelen eylem de farklıdır (biri sağlayıcı ayarını gevşetmek — Gemini'de `google.safety.*` — diğeri politikayı gözden geçirmek). `ContentBlocked = 11` enum'un SONUNA eklendi (`smallint` sütunda saklanır, mevcut değerlerin kayması eski satırları yanlış okur) ve `DefaultRunErrorClassifier.StableIdentities`'e bağlandı. Eklenmeseydi her engelleme `Unknown` kovasına düşerdi ve Faz 44'ün panosunda sessiz bir gerileme olurdu. Arayüz sözlüğüne iki dilde bir anahtar eklendi (K-228).

### K-445

**K-445 — Sağlayıcı başına giden eşzamanlılık sınırı DOLDUĞUNDA isteği REDDETMEZ, kendi `CancellationToken`'ıyla sınırlı olarak BEKLER**

Sınırın amacı 429 fırtınasını üretmemek; anında `429` dönmek tam da önlenmek istenen sonucu üretirdi (kısa bir trafik patlamasını gerçek bir arızaya çevirir). `ProviderConcurrencyLimiter.AcquireAsync` bu yüzden `SemaphoreSlim.WaitAsync(cancellationToken)` kullanır — `VoiceConnectionLimiter`'ın CAS tabanlı ANINDA-RET desenini KOPYALAMAZ, yalnız kira/serbest bırakma ŞEKLİNİ ödünç alır. `MaxConcurrentCallsPerProvider = null` (varsayılan) iken `AcquireAsync` hiç semafor kurmadan `null` döner — sıcak yolda ek tahsis yok.

### K-268

**K-268 — `TemplateFixture` sablon testleri icin tam cozum yerine `AgentPrism.src.slnf` (yalniz `src/` paketlerini listeleyen bir cozum filtresi) paketler**

Olculdu: `dotnet pack AgentPrism.slnx` cozumdeki 13 test projesini (Postgres/SqlServer/Sqlite container'li entegrasyon + Playwright E2E dahil) de restore+build ediyordu — Pack hedefi onlarda no-op olsa da Build ONA BAGIMLI oldugu icin calisiyordu. Templates.Tests suitinin tek basina calismasi bu yuzden ~1 saat suruyordu. `.slnf` cozum filtresi `.slnx` formatini da destekler (.NET 10 SDK ile dogrulandi: `dotnet sln <filtre>.slnf list`). Sonuc: warm pack ~30 saniyeye, suit toplami ~15 dakikaya dustu (kalan sure gercek is — npm/frontend derlemesi + 3 uretilen projenin gercek NuGet restore'u).

### K-202

**K-202 — Saklama zamanlaması Faz 17'nin kuyruğunu yeniden kullanır**

`JobKind.Retention` eklenip `RetentionJobHandler` (`IJobHandler`) kaydedildikten sonra Faz 17'nin var olan zamanlama altyapısı (`job_schedules` tablosu, `JobWorkerBackgroundService`, `/api/job-schedules` uçları) hiçbir yeni kod olmadan günlük saklama koşusunu karşılıyor: bir yönetici `kind=Retention, targetName="*" (veya belirli hedef), cron="0 3 * * *"` ile bir zamanlama satırı ekler. Bu satırın kendisi hiçbir şey SİLMEZ (K-063'ün "varsayılan politika yok" kararıyla çelişmez) — yalnızca `RetentionExecutor.RunAsync`'i tetikler, o da hâlâ açık bir `retention_policies` kaydı veya `AgentPrismRetentionOptions.Enabled=true` arar. Yeni yazılan tek uçlar saklamaya ÖZGÜ olanlardır: politika CRUD, `preview`, `run` (kuyruğa yazar), `history`.

### K-214

**K-214 — `KARARLAR-INDEKS.md`'den tarih sütunu kaldırıldı**

İndeks Faz 25'te 24 KB'yi aşmış, bütçe 1 KB büyütülmüş ve `scripts/dokuman-bakim.py` içine "bir sonraki aşımda yapısal çözüm" notu yazılmıştı. Faz 27'de (213 kalem) tekrar aşıldı; söz tutuldu. İndeksin işi **kalemi bulup satır numarasını vermektir**; tarih o işte kullanılmaz ve satır başına ~15 bayt tutar — 213 kalemde ~3 KB, yani aşımın altı katı. Tarih **kaybolmadı**: `KARARLAR.md`'deki kalemin kendisinde duruyor ve indeks zaten oraya yollamak için var. Sıcak yol toplamı 104962 → 101200 bayt. Bir sonraki aşımda sıradaki adım script yorumuna yazıldı: bölüm bazlı indeks (reddedilen işler ayrı dosyaya) veya kapanmış fazların kararlarının arşiv indeksine taşınması.

### K-081

**K-081 — Sır süzgeci "token" fragmanını, çoğulu (Tokens) hariç tutacak şekilde eşler**

Ölçüldü: `/agentprism` örnek uygulamasında gerçek bir `agent.create` denetim kaydında `model.maxOutputTokens` alanı `"***"` ile gizlenmişti — basit alt dize eşlemesi "token" ile "maxOutputTokens" içindeki "Tokens"ı da eşliyordu. Bu depodaki tüm token SAYIM alanları (`maxOutputTokens`, `maxContextWindowTokens`, `totalTokens`, `inputTokens`, `outputTokens`) çoğuldur; kimlik doğrulama değerleri (`authToken`, `accessToken`) tekildir. `AuditSecretFilter.IsSecretKey`, "token" fragmanı eşleştiğinde anahtar adı "tokens" içeriyorsa eşleşmeyi iptal eder. Regresyon testi: `AuditSecretFilterTests.Cogul_token_alanlari_sir_sayilmaz`.

### K-111

**K-111 — İkili ek içeriği ayrı tabloda, mesajda yalnız küçük bir referans**

`attachments.content` (`bytea`) yükleme anında yazılır; `ChatMessage.Contents`'e eklenen tek şey `UriContent({prefix}/api/attachments/{id})`'dir. Doğrudan `DataContent` (base64) taşınsaydı 1 MB'lık bir görsel her geçmiş okumasında ~1,4 MB JSON'a dönüşürdü. Çözümleme, modele gönderilmeden **hemen önce**, `AttachmentResolvingChatClient` içinde yapılır — bu sarmalayıcı `ModelProviderRegistry.CreateChatClient` içinde devre kesicinin **içine** (gerçek istemcinin bitişiğine) konur, her gerçek ağ çağrısında taze çözülsün ve devre kesicinin erken dönüşünden etkilenmesin diye. `AttachmentUriReference.TryParse` yalnızca `/api/attachments/{id}` izine bakar; `Core` katmanı bu sayede uç noktaların yol önekini (`{prefix}`) hiç bilmez.

### K-068

**K-068 — Faz 7 (yayın) sıradan çıkarıldı; zamanı belirsiz** *(kullanıcı kararı)* *(kısmen yeniden açıldı: 2026-08-16, K-421 — public API takip yarısı artık geçersiz)**

Kullanıcı yayın zamanını henüz belirlemedi ve özellik geliştirmeye devam etmeyi seçti. Sonuç: `07-SAGLAMLASTIRMA-VE-YAYIN.md` bir sıra numarası taşımaya devam eder ama **sıradaki faz değildir**; her an araya girebilir. ~~`EnablePublicApiTracking` **`false`** kalır (K-016 aynen geçerli).~~ Faz 60'ta bu yarı K-421 ile değişti: takip **açık**, yalnız `Unshipped.txt` → `Shipped.txt` taşıması hâlâ yayın anını bekliyor. Bedeli bilinir: yayın geciktikçe ilk `PublicAPI.Shipped.txt` dolumu büyür. Bu yüzden her faz dokümanının "Gerçekleşen Public API" bölümü eksiksiz yazılır — o bölümler dolumun kaynağıdır.

### K-251

**K-251 — `AddAgentPrismHealthChecks()` `AddAgentPrism()`'in önceden çağrıldığını KAYIT ANINDA denetlemez**

İlk taslak `MapAgentPrism`'in `IAgentCatalog` kontrolünü taklit ediyordu ama `MapAgentPrism` bu kontrolü `app.Build()` SONRASI, tamamlanmış bir `IServiceProvider` üzerinde yapar — `AddAgentPrismHealthChecks()` ise `IServiceCollection` kurulum aşamasında çalışır ve `services.AddAgentPrism()` ile `services.AddHealthChecks().AddAgentPrismHealthChecks()` çağrılarının hangi sırada geleceği tüketiciye bağlıdır (DI kaydı sıradan bağımsızdır). Kayıt anında kontrol etmek, geçerli ama "yanlış sırada yazılmış" kurulumları hatalı biçimde reddederdi. `AddAgentPrism()` hiç çağrılmazsa `/health` ilk yoklandığında `AgentPrismDiagnosticsCollector` DI çözümlemesinde standart, açıklayıcı bir hata verir.

### K-476

**K-476 — `IVectorSearchStore`, `EnableKnowledge = false` iken KAYITSIZ bırakılmaz; fabrikası `null` döner (Faz 67)**

`UsePostgreSql(Action<Options> configure)` çağrıldığı anda `configure` henüz ÇALIŞMAMIŞTIR (Options deseni tembel bağlanır) — DI kayıt zamanında "`EnableKnowledge` açık mı" sorusu YANITLANAMAZ, kayıt kararı yalnız ÇÖZÜMLEME zamanında verilebilir. `services.TryAddSingleton<IVectorSearchStore>(factory)` fabrikası `EnableKnowledge` kapalıyken `null!` döner; Microsoft.Extensions.DependencyInjection bunu geçerli bir singleton değeri sayar. `provider.GetService<IVectorSearchStore>()` zaten Faz 51'den beri "`null` = kayıtlı değil" sözleşmesini taşıyordu (`AgentPrismServiceCollectionExtensions`, `EnableVectorSearch` derleme hatası) — bu K1 kapısı DEĞİŞTİRİLMEDEN yeniden kullanıldı.

### K-308

**K-308 — Çalıştırmanın girdisi AYRI bir `run_inputs` tablosunda ve `json` sütununda saklanır; `runs`'a sütun EKLENMEZ**

K-027'nin dördüncü uygulaması. `messages` polimorfik `ChatMessage` taşır ve `$type` ayracı nesnenin ilk özelliği olmak zorundadır; `jsonb` anahtarları yeniden sıralar ve okuma çalışma anında çöker. Ayrı tablo seçildi çünkü `runs` en sıcak tablodur ve her liste/istatistik sorgusu onu okur — aynı gerekçe `conversation_items`'ı da 0001'de ayırmıştı. Alternatifler ölçülerek elendi: `RunStarted` olayının yüküne yazmak `MaxPayloadLength` ile KIRPILIR ve kırpılmış bir girdi sessizce yanlış bir yeniden oynatma üretirdi. Girdi her çalıştırma için yazılır (kök + alt + eval + workflow): bir alt agent çağrısı da tek başına oynatılabilir olmalıdır. `run_inputs` bir saklama hedefidir.

### K-182

**K-182 — Diziler SQL Server'a JSON metni olarak taşınır**

SQL Server'da dizi parametresi yoktur. PostgreSQL'in `text[]`/`uuid[]` parametreleri (webhook `events`, `job_items` toplu ekleme) SQL Server tarafında `nvarchar(max)` JSON dizisi olarak gönderilir ve SQL içinde `OPENJSON` ile açılır. Tablo değerli parametre (TVP) alternatifi elenmiştir: migration'da ayrı bir `TYPE` tanımı ve `SqlDbType.Structured` gerektirir, yani sağlayıcıya özgü kod paylaşılan katmana sızardı. `= ANY(events)` karşılığı `EXISTS (SELECT 1 FROM OPENJSON(events) WHERE value = @event_type)` **tam eşleşmedir**; LIKE tabanlı bir arama `run.completed` ararken `run.completed.v2` aboneliğini de yanlışlıkla eşlerdi. C# akışı iki sağlayıcıda aynıdır; fark `SqlDialect.AddTextArray`/`AddUuidArray`/`ReadTextArray` içinde kalır.

### K-221

**K-221 — Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir**

Fazın ilk taslağı `ApiKeyConfigurationKey` (anahtarın **adı**) öneriyor ve K-059'a dayanıyordu. K-059 bir sırrın **veritabanına** yazılmasını yasaklar — MCP sunucu tanımları arayüzden oluşturulur ve veritabanında yaşar. Ses yapılandırması veritabanına **hiç girmez**; `IConfiguration` üzerinden gelir ve değer `dotnet user-secrets` içinde durur. Diğer dört sağlayıcı paketi (`OpenAI`, `Anthropic`, `Google`, `Azure`) düz `ApiKey` taşır; ikinci bir kalıp tutarsızlık üretir ve tüketiciyi iki farklı yapılandırma şekliyle karşılaştırırdı. `VoiceOptions` bir `class`'tır (K-035) ve `SecretLeakTests` anahtarın altı çıktıda görünmediğini doğrular.

### K-339

**K-339 — `ChildRunApproval` public yapıldı: üçüncü tüketici MCP/A2A dış çağrı katmanıdır (Faz 50)**

K-103'ün "onay isteyen bir alt çalıştırma `Failed` kapanır ve çağırana anlaşılır bir hata döner" sınırı artık İKİ değil ÜÇ yerde uygulanıyor: `ChildAgentInvoker` (Core), `RunRecordingAgent` (Core), ve şimdi `CatalogToolCallHandler`/`ExternalAgentProxy` (AspNetCore). Tespidi `internal` tutup üçüncü yerde YENİDEN YAZMAK "tek yerde tespit" ilkesini (aynı sınıfın kendi belgesinde yazılı) ihlal ederdi. `AgentPrism.Core` ve `AgentPrism.AspNetCore` arasında `InternalsVisibleTo` YOKTUR (ölçüldü — yalnız `.UnitTests`/`.IntegrationTests`/`.FunctionalTests` sonekleri var), bu yüzden tip `public static class ChildRunApproval` oldu; iki `Describe` aşırı yüklemesi zaten public XML belgesi taşıyordu.

### K-430

**K-430 — `00-INDEKS.md` §7 `Koşum` sütunu koşum kaydından ÖLÇÜLÜR, elle işaretlenmez**

Kapanış planının §11 adım 3'ü ("`00-INDEKS.md` §7 `Koşum` sütunu 25 satır için güncellenir") YAPILMAMIŞTI: koşum 1094 case ile bitmişken tablo hâlâ çoğu satırda `☐` gösteriyordu. Sütun `kosumlar/2026-08-13/` kayıtlarından case bazında sayılarak yeniden üretildi (`✅ geçen/toplam` + `N ☒ / N ⏭ / N ☐ / N ⬜`). Aynı denetim kapanışın adım 5'inin de eksik kaldığını gösterdi — `Blob.arrayBuffer()` zincirleme tuzağı `hafiza/frontend.md`'ye hiç yazılmamış, yalnız `voice-panel.tsx` yorumunda kalmıştı; bu turda alan dosyasına yazıldı. Ders: kapanış adımları bir sonraki oturumda DOĞRULANMAZSA sessizce yarım kalır. `manuel-test-kosumu` skill'inin "bitti tanımı" listesi bu iki adımı ayrı ayrı kutu olarak taşır.

### K-331

**K-331 — `RunScore.Author` yargıç puanlarında `judge:{ad}` ile BİLEREK DOLU yazılır (Faz 49)**

K-239'un tersine NULL semantiği ("author NULL iken benzersizlik hiç uygulanmaz, her çağrı yeni satır açar") insan geri bildirimi için kasıtlıydı (kimliksiz kurulumda her tıklama ayrı satırdır). Yargıç puanı için bu YANLIŞ davranış olurdu: bir işin geri adımlı yeniden denenmesi veya `POST /api/runs/{id}/judge`'ın elle tekrarlanması aynı yargıç için ikinci bir satır açardı. `Author`'ı `judge:{ad}` (Source ile aynı değer) ile doldurmak üç sağlayıcının UPSERT tekilliğini (`tenant_id, run_id, message_id, author`) doğal olarak devreye sokar. Gerçek koşumla doğrulandı: aynı çalıştırma `POST /api/runs/{id}/judge` ile iki kez elle puanlatıldığında `run_scores`'ta tek satır kaldı, değer güncellendi.

### K-341

**K-341 — Vektör gömüsü metin biçiminde (`::vector` cast) yazılır, hiçbir vektör paketi alınmaz (Faz 51)**

Üç aday ölçüldü: `Microsoft.SemanticKernel.Connectors.PgVector` 1.74.0-preview `Npgsql 8.0.7`'ye karşı derlenmiş (bizde 10.0.3); `Pgvector` 0.3.2 `Npgsql 8.0.5`'e karşı; `Microsoft.Extensions.VectorData.Abstractions` 10.8.0 tek başına işe yaramaz (K-343). K-211'in ikinci uygulaması: sürüm kayması bu depoda ölçülmüş bir hata sınıfıdır. Metin/ikili farkı da ölçüldü: fark yalnız yazma telinde (~2,3×, 14 416 bayt metin vs 6 148 bayt), disk saklama ve okuma **özdeş** — PostgreSQL her iki yoldan da aynı kanonik ikili gösterimi saklar (`pg_column_size` tam olarak `1536×4+4` bayt).

### K-469

**K-469 — Kiracı sağlayıcı bağlama/egress uçları (`/api/tenants/{tenantId}/providers`, `/api/tenants/{tenantId}/egress`) kiracıyı AMBIYANS `ITenantContext`'ten değil, ROTA parametresinden alır — `ApiKeyEndpoints`'in aksine, `GovernanceEndpoints`'in `PUT /api/tenants/{slug}` deseniyle AYNI (Faz 65)**

Bu uçlar platform yöneticisinin HERHANGİ bir kiracının bağlamasını yönetmesi içindir, yalnız isteği yapanın KENDİ kiracısını değil — `ApiKeyEndpoints`'in `tenants.TenantId` (ambiyans) deseni burada YANLIŞ olurdu: bir yönetici başka bir kiracı adına anahtar tanımlayamazdı. `HttpTenantContext.IsValidTenantId` ile aynı doğrulama (`GovernanceEndpoints.MapTenants`'ın kullandığı) tekrar kullanılır. Yetki `SecurityAdmin` kapsamıdır (K1: kapsam onu taşımayan hiçbir anahtar bu uçlara giremez).

### K-142

**K-142 — `LocalEvaluator.EvaluateAsync(...).DetailedItems` boş döner; gerçek sonuç `Items[0].Metrics`'tedir** 🚨**

Reflection dökümü `AgentEvaluationResults.DetailedItems`'i (tip: `IReadOnlyList<EvalItemResult>`) listeledi ve plan bunu okuma yolu sandı. Gerçek davranış küçük bir repro programıyla ölçüldü: `DetailedItems` **her zaman `null`** (yalnız uzak raporlama arka uçlarıyla doldurulan bir alan), ama `Items` (`IReadOnlyList<Microsoft.Extensions.AI.Evaluation.EvaluationResult>`) ve üstteki `AllPassed`/`Passed`/`Failed` sayaçları doğru dolu geliyor. `EvalJobHandler` vaka başına tek öğelik bir liste ile çağırdığı için `results.AllPassed` o vakanın geçip geçmediğini, `results.Items[0].Metrics` ise denetim bazlı skorları (`Name`, `Interpretation.Failed`, `Reason`) doğrudan verir.

### K-228

**K-228 — i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı**

`react-i18next` + `i18next` 15–25 KB gzip'tir ve çoğul kuralı tabloları, ad alanları, gecikmeli yükleme gibi iki yakın dilin ihtiyaç duymadığı yetenekler taşır. Elle yazılan katman **~150 satırdır** ve bir kütüphanenin veremeyeceği bir şey verir: `tr` sözlüğü `Messages` tipiyle bildirilir, bu yüzden eksik anahtar **derleme hatasıdır** (kanıt: `nav.runs` silinince `TS2741`). Çalışma anı sözlüğü olan bir kütüphane bu güvenceyi veremez. Aynı gerekçe elle yazılmış yönlendirici (K-045) ve elle çizilen grafik (K-002) ile aynıdır.

### K-241

**K-241 — `InMemoryRunStore`'a isteğe bağlı `IRunScoreStore` eklendi**

K-240'ın çözümü bir kurucu bağımlılığı gerektiriyordu ama sınıf `public`'tir ve ~20 test dosyası `new InMemoryRunStore()` ile parametresiz çağırıyordu — hepsini güncellemek gereksiz bir kırılma olurdu. Çözüm: `IRunScoreStore? scores = null` parametresi, verilmezse kendi özel `InMemoryRunScoreStore`'unu kurar. DI yolunda risk yok: `AddAgentPrism()` `IRunScoreStore`'u HER ZAMAN kaydeder (`IRunStore`'dan önce), bu yüzden `docs/hafiza/aspnetcore-di.md`'nin "varsayılan parametre DI'da dolmaz" uyarısı burada geçerli değildir — o uyarı KAYITLI OLMAYABİLECEK opsiyonel servisler içindir, `IRunScoreStore` öyle değildir. Sonuç: HTTP katmanının yazdığı puan ile `/api/stats`'ın okuduğu puan AYNI tekil örneği paylaşır.

### K-484

**K-484 — `UsageDetails`'in ses sayaçları için `MEAI001` bastırması TEK dosyada (`UsageBreakdown`) toplandı (Faz 68)**

Ölçüldü: MEAI **10.8.3**'te `CachedInputTokenCount`/`ReasoningTokenCount` işaretsizdir ama `InputAudioTokenCount`/`OutputAudioTokenCount` `[Experimental]`'dır (`TreatWarningsAsErrors` → HATA). Proje düzeyinde bastırma reddedildi: değerlendirme amaçlı bir API'nin vermesi GEREKEN sinyali her yerde gizlerdi. Bastırma iki `return` deyimine daraltıldı; desen `AgentPrismA2ABuilderExtensions` (MEAI001) ve `ChatHistoryReader` (MAAI001) ile AYNI. Üye adları tahmin edilmedi, `maf-api-kesfi` ile reflection'la doğrulandı. Yeniden adlandırma/kaldırma olursa derleme TEK yerde kırılır — istenen davranış budur.

### K-136

**K-136 — Zamanlanmış bir işi belirli bir kiracı olarak çalıştırmak `AmbientTenantScope` (AsyncLocal) ile yapılır**

`SingleTenantContext`/`HttpTenantContext` tekildir (singleton) ve kiracıyı ya sabit varsayılandan ya `HttpContext`'ten okur; ikisinde de "bu iş hangi kiracı için çalışıyor" bilgisi yoktur — bu bilgi yalnızca `JobRecord.TenantId` içinde taşınır. `AmbientTenantScope`, `IHttpContextAccessor`'ın kullandığı aynı `AsyncLocal` desenini uygular: `JobWorkerBackgroundService` bir işi yürütmeden önce `AmbientTenantScope.Begin(job.TenantId)` çağırır, her iki `ITenantContext` uygulaması da kendi varsayılan çözümlemesinden önce bunu kontrol eder.

### K-486

**K-486 — Attribution UPSERT'te `COALESCE` ile KORUNUR, üzerine yazılmaz (Faz 68)**

Kuyruğa alınmış bir `run` (Faz 46) `StartRunAsync`'i İKİ KEZ çağırır: yer tutucu HTTP isteği İÇİNDE yazılır (kullanıcı BİLİNİR), sonra arka plan işçisi yeniden yazar (HTTP'ye bağlı bir bağlamın okuyacağı istek YOKTUR → `null`). Düz üzerine yazma, ilk yazımın DOĞRU aldığı atfı SİLERDİ — K-157'nin veri tarafındaki eşleniği. Üç dialekt `COALESCE(EXCLUDED.user_id, user_id)` yazar, `InMemoryRunStore` aynı davranışı kodda tekrarlar; sözleşme testi dört depoda birden koşar. Attribution "set → unset" yönünde MEŞRU olarak değişmediği için koruma her zaman doğrudur. Aynı sebeple `ApprovalEndpoints`'in sürdürme `run`'ı atfı ORİJİNAL `run`'dan DEVRALIR — onaylayan kişi bütçesi harcanan kişi değildir.

### K-342

**K-342 — K-105 güncellenir: `ChatHistoryMemoryProvider` yine bağlanmadı, sebep artık ölçülmüş (Faz 51)**

K-105'in yeniden açılma koşulu ("`VectorStore` implementasyonu ve embedding sağlayıcısı seçildiğinde") kısmen karşılandı — embedding sağlayıcısı çözüldü (tüketici kaydeder) ama `VectorStore` çözülmedi: ölçüldü (`Microsoft.Extensions.VectorData.Abstractions` 10.8.0), `VectorStoreCollection<TKey,TRecord>.GetAsync` bir `Expression<Func<TRecord,bool>>` süzgeci ister; ifade ağacı yorumlamak `[RequiresDynamicCode]` sınıfına girer ve `AgentPrism.PostgreSql`'in AOT duruşunu bozar. `IVectorSearchStore` bu tipi SARMALAMAZ; kendi minimal sözleşmesini tanımlar.

### K-067

**K-067 — Alt agent çalıştırması ayrı bir `runs` satırıdır** *(kullanıcı kararı: "olabilir")**

Kullanıcı ayrı satırı olabilir buldu; plan bunu benimsedi. Gerekçe: tek satırda toplanırsa "3 dakika sürdü, sebebi bilinmiyor" durumu oluşur — alt agent'ın maliyeti, süresi ve hatası görünmez. Ayrı satır `runs.parent_run_id` gerektirir ve waterfall doğal olarak iç içe geçer. Ek olarak `root_run_id` denormalize edilir: özyinelemeli CTE yerine tek indeksli sorgu yeter. `RunQuery.OnlyRootRuns` varsayılan `true` olur, aksi hâlde Runs ekranı alt çağrılarla dolar. Ayrıntı: [Faz 12](arsiv/fazlar/12-AGENT-CAGRI-GRAFIGI.md).

### K-032

**K-032 — Model kataloğu yapılandırmadan gelir, kodda yerleşik liste yoktur** *(kullanıcı kararı)**

Ölçüldü: koda gömülen liste yayınlandığı gün bile yanlıştı — gerçek hesabın erişebildiği üç modelin (`gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`) hiçbiri listede yoktu ve listedeki `gpt-4.1-mini` `HTTP 403 model_not_found` döndü. OpenAI model adları ve fiyatları bir NuGet paketinin yayın sıklığından hızlı değişir; yanlış fiyat maliyet raporunu sessizce bozar. `OpenAIModelCatalog.Build(options)` yalnız `AgentPrism:Providers:OpenAI:Models` ayarını okur. Katalog bir **doğrulama listesi değildir**: burada olmayan model adı da kullanılabilir, aksi halde OpenAI'in her yeni modeli AgentPrism sürümü bekletirdi.

### K-135

**K-135 — Optional `IJobHandler` bağımlılığı DI'da açık fabrika ile enjekte edilir, kurucu varsayılan değeriyle değil**

Ölçüldü: `WorkflowJobHandler(IWorkflowRunner? runner, ILogger? logger = null)` yalnızca `logger`'da `= null` taşıyordu; `TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WorkflowJobHandler>())` (otomatik kurucu çözümleme) `IWorkflowRunner` kayıtlı değilken `InvalidOperationException` fırlattı. `runner`'a da `= null` eklemek yetmedi — yerleşik DI kabı varsayılan değerleri güvenilir doldurmaz (`TimeProvider` için de aynı ders daha önce görülmüştü, bkz. `MEMORY.md`). Çözüm: `ServiceDescriptor.Singleton<IJobHandler, WorkflowJobHandler>(static provider => new WorkflowJobHandler(provider.GetService<IWorkflowRunner>(), ...))` açık fabrikası.

### K-134

**K-134 — `WorkflowJobHandler` ayrı bir pakete değil, `AgentPrism.Core`'a konur**

`IWorkflowRunner` arayüzü zaten `AgentPrism.Abstractions`'tadır (Core zaten ona bağımlı); somut uygulama yalnız tüketici `AgentPrism.Workflows` paketini ekleyip `UseWorkflows()` çağırırsa DI'a girer. `WorkflowJobHandler` kurucusunda `IWorkflowRunner?` (nullable, opsiyonel servis) alır — `WorkflowEndpoints`'in `501` deseniyle aynı gerekçe. Kayıtlı değilse iş `Failed` olur, açık hata mesajıyla ("AgentPrism.Workflows paketini ekleyip UseWorkflows() çağırın"); süreç çökmez. Yeni bir paket gerektirmeden K4 genişleme noktası desenini korur.

### K-444

**K-444 — `ModelFallback` yalnız `Provider`+`Model` taşır; birincil bağlamanın diğer alanları (sıcaklık, `ProviderSettings`, ...) yedeğe DEVRETMEZ**

Tam `ModelBinding` taşımak özyinelemeli bir tip (`Fallbacks` içinde `Fallbacks`) üretirdi ve doğrulaması zorlaşırdı. Yedek modelin kendi ayarına ihtiyacı varsa bu ayrı bir agent/kalemdir. Doğal sonuç: yedek bağlaması `new ModelBinding { Provider, Model }` ile kurulur (`Fallbacks` varsayılan `[]`), bu yüzden yedek zincirinin kendisi ÖZYİNELEMELİ ÇAĞRI KURMAZ — `ModelProviderRegistry.CreateChatClient` yedek bağlama için yeniden çağrıldığında `binding.Fallbacks.Count > 0` koşulu hep yanlıştır.

### K-105

**K-105 — `ChatHistoryMemoryProvider` (vektör tabanlı bellek) bu fazın kapsamı dışında** *(kullanıcı kararı)**

Faz 13'ün plan taslağı bunu basit "oturum içi bellek" sanıyordu. Reflection ile doğrulandı: gerçek kurucusu `(VectorStore vectorStore, string collectionName, int vectorDimensions, ...)` istiyor — vektör tabanlı anlamsal arama. Depoda somut bir `VectorStore` implementasyonu yok; eklemek yeni bir paket + embedding sağlayıcısı kararı gerektirir (K-007 kapsamında ayrı bir gerekçe ister). `TextSearchProvider`'ın vektör aramasının zaten kapsam dışı bırakılmasıyla tutarlı.

### K-417

**K-417 — HTTP API sayfaları OpenAPI belgesinden ÜRETİLİR; Scalar gömülmedi**

Plan Scalar öngörüyordu. Ölçüldü: `@scalar/api-reference` standalone tarayıcı paketi **7,4 MB / 90 chunk**, kendi temasında render ediyor ve — kararı veren nokta — içeriğinin hiçbiri Pagefind'e girmiyor. Bu fazda 143 operasyonun **tamamına** açıklama yazıldı; onları sitenin kendi aramasından gizleyip karşılığında bir "try it" konsolu almak yanlış takastı. `scripts/build-http-api.mjs` etiket başına bir Starlight sayfası üretir (19 grup, 143 operasyon) — tek tema, tek arama indeksi, sıfır vendor MB. Ham belge `/openapi/agentprism.json` olarak yayınlanır; Scalar/Swagger/Postman isteyen oradan yükler.

### K-473

**K-473 — `InboundTriggerDispatcher.ValidateAsync`, imza doğrulamasını hız sınırından ÖNCE çalıştırır (Faz 66, bağımsız denetim 🔴 #2)**

İlk sıralama (hız sınırı → imza) `faz-denetim`'in bağımsız denetçisi tarafından somut bir DoS olarak ölçüldü: tetikleyici adını bilen/tahmin eden kimliksiz bir saldırgan, dakikada `MaxRequestsPerMinute` kadar RASTGELE imzalı istek göndererek gerçek gönderenin (örn. Slack) o dakikaki bütçesini tüketebiliyordu — 66.5'in "hız sınırı kuyruğu korur" gerekçesi ancak DOĞRULANMIŞ isteklerin kuyruğa ulaştığı bir sırada anlamlıdır. Düzeltme: imza (ve zaman damgası) önce doğrulanır; yalnız secret'ı KANITLAYAN bir istek limitten pay tüketir. Kanıt: `An_unsigned_flood_does_not_consume_a_legitimate_senders_rate_limit_budget` (birim).

### K-253

**K-253 — `AgentPrismValidationOptions.McpTimeout` Core'a eklendi, AspNetCore'a değil**

Faz 34 planının açık soru 2'si zaman aşımını `AgentPrismEndpointOptions`'ta öneriyordu; ama `AgentDefinitionValidator` `AgentPrism.Core`'da yaşar ve `AgentPrism.Core` paket yönü gereği `AgentPrism.AspNetCore`'a bağımlı olamaz. `IOptions<AgentPrismOptions>` zaten `AgentDefinitionCompiler`'ın (`UtilityModel`, `Skills.MaxSkillsPerAgent`) kullandığı ortak yapılandırma kanalıdır; aynı kanaldan okumak katman ihlali yaratmadan aynı yapılandırılabilirliği (`AgentPrism:Validation:McpTimeout`, varsayılan 5 sn) verir.

### K-250

**K-250 — `AgentPrismDiagnosticsReport` genel bir Healthy/Degraded/Unhealthy alanı TASIMAZ; üç durumlu karar yalnız `AgentPrism.AspNetCore.AgentPrismHealthCheck` içindedir**

`Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus` paylaşılan çerçeveden (`Microsoft.AspNetCore.App` FrameworkReference) gelir; `AgentPrism.Core` bu referansa sahip değildir ve olmamalıdır (K-006: AspNetCore'a özgü tipler Core'a sızmaz). `AgentPrismDiagnosticsCollector` yalnız çıplak gerçekleri toplar (`CanConnect`, `MigrationsUpToDate`, `RegisteredPersistenceProviders`, sağlayıcı listesi); `/api/diagnostics` bu raporu OLDUĞU GİBİ döner, `/health` aynı rapordan üç durumlu bir karar türetir. İki yüzey aynı veriyi okur ama yorumlama mantığı yalnız AspNetCore katmanındadır.

### K-375

**K-375 — `CanaryPolicy`'ye ayrı bir `RampRequiresSampleSize` alanı AÇILMADI; kademeli artırma da `MinSampleSize`'ı AYNEN kullanır (Faz 56, plandan sapma)**

Planın 56.4 bölümü düz yazıda `RampRequiresSampleSize` adlı üçüncü bir eşik alanından bahsediyordu ama "Planlanan Public API" kod örneği bu alanı HİÇ içermiyordu — plan kendi içinde tutarsızdı. 56.1'in kendi uyarısı ("üçüncü bir eşik kuralı yazmak üç yerde bakım demektir") burada da geçerlidir: bir sonraki ramp adımına geçmeden önce gereken asgari örnek sayısı, geri alma kararı için zaten var olan `MinSampleSize` ile AYNI kavramdır. `CanaryEvaluationService.TryAdvanceRampAsync` `evaluation.Canary`'nin sonuçlanmış çalıştırma sayısını `policy.MinSampleSize`'a karşı denetler; ikinci bir alan yoktur.

### K-167

**K-167 — `AllowInsecureHttp` loopback ADRESİNİ de açar, yalnız şemayı değil** 🚨**

İlk yazımda `AllowInsecureHttp` yalnızca `http` şemasına izin veriyor, adres denetimi loopback'i "özel ağ" sayıp reddediyordu. Sonuç: yerel bir dinleyiciye teslim **imkânsızdı** — teslim `Dropped` oldu ve kullanıcı, `10/8` ile `169.254.169.254` dâhil tüm özel ağı açan `AllowPrivateNetworkTargets`'a zorlanırdı. Geliştirme kolaylığı uğruna üretim güvenliğini feda etmek olurdu. Düzeltme: `WebhookUrlValidator.IsAllowedTarget` — `AllowInsecureHttp` açıkken **yalnızca loopback** kabul edilir; başka hiçbir özel aralık açılmaz. Canlı sınamada yakalandı; regresyon testleri: `Loopback_hedefi_AllowInsecureHttp_acikken_teslim_edilebilir`, `Loopback_izni_diger_ozel_araliklari_acmaz`.

### K-049

**K-049 — `IAgentPrismUiProvider` tek metotlu tutuldu**

Bağımlılık yönü `AgentPrism.UI → AgentPrism.AspNetCore` şeklindedir ve tersine çevrilemez; `MapAgentPrism` arayüz paketini doğrudan çağıramaz, kaydı DI'dan çözer. Soyutlamanın varlık listesi, içerik tipi, `ETag`, önbellek başlıkları, sıkıştırma biçimi ve SPA geri dönüşü gibi ayrıntıları taşıması hâlinde arayüz paketi paketleme biçimini her değiştirdiğinde HTTP katmanının **public API'si** değişirdi. Tek metot (`TryServeAsync`) bu ayrıntıların tamamını uygulamaya bırakır. `HasAssets` yanlışsa rotalar hiç bağlanmaz — Node.js olmayan bir ortamda derlenmiş bir paket boş sayfa yerine `404` verir ve nedeni aranır.

### K-321

**K-321 — İçerik guard'ı `IChatClient` katmanındadır ve tool çağrı döngüsünün İÇİNDEDİR**

Aday listesi guard'ın zincire bir `IAgentDecorator` olarak takılmasını öneriyordu. Bir dekoratör agent'ın yalnız ilk girdisini ve son çıktısını görür; aradaki turları görmez. Ölçüldü ve doğrulandı (`ContentGuardPipelineTests.Tool_sonucundaki_icerik_ikinci_model_cagrisinda_yakalanir`): uzak bir tool zararlı içerik döndürdüğünde guard ikinci model çağrısında yakalar ve sahte istemcinin çağrı sayacı `1`de kalır — zararlı sonuç modele HİÇ ulaşmaz. Ek fayda `ContentFilterDetectingChatClient`'ın gerekçesinin aynısıdır: OpenAI, Anthropic, Google ve Azure aynı davranışı tek bir yerden alır.

### K-037

**K-037 — `ChatHistoryProvider` `AddAgentPrism()` içinde açıkça kaydedilir**

Kayıt olmasaydı MAF her agent için kendi `InMemoryChatHistoryProvider` örneğini kurardı ve o örneğe dışarıdan erişilemezdi; `/api/sessions/{id}` mesaj geçmişi yalnız PostgreSQL açıkken okunabilirdi — yani K1 ("sıfır sürpriz") iki farklı davranış üretirdi. Geçmiş, MAF'ın public `ChatHistoryProvider.InvokingAsync` + `InvokingContext` kurucusu ile okunur (`MAAI001` bastırması tek dosyada: `SessionEndpoints.ReadHistoryAsync`). Durum oturumun `StateBag`'inde yaşadığı için tek örneğin tüm oturumlarca paylaşılması MAF'ın öngördüğü kullanımdır. `UsePostgreSql()` `Replace` kullandığı için hâlâ kazanır (K-025).

### K-369

**K-369 — `pending_approvals.run_id`, `runs(id)`e `ON DELETE CASCADE` ile bağlı; sözleşme testleri gerçek bir `runs` satırı önceden kuran bir `PrepareRunAsync` kancası kazandı (Faz 55)**

Yabancı anahtar olmadan rastgele bir `RunId` ile `pending_approvals` satırı yazmak veritabanı düzeyinde referans bütünlüğünü bozardı — bekleyen onay her zaman gerçek bir çalıştırmanın izdüşümüdür, çalıştırma silinirse (ör. `retention` temizliği) bekleyen onay da anlamsızlaşır. `PendingApprovalStoreContract`, `RunInputStoreContract`'ın kurduğu deseni izleyerek sanal bir `PrepareRunAsync(Guid runId, string tenantId)` kancası taşır; SQL sağlayıcı test çalıştırıcıları bunu geçersiz kılıp gerçek bir `runs` satırı tohumlar, bellek içi çalıştırıcı hiçbir şey yapmaz.

### K-299

**K-299 — Sınıf başına en sık üç küme, pencere fonksiyonlarıyla (`ROW_NUMBER()`/`COUNT() OVER`) tek geçişte hesaplanır — bu desenin kod tabanındaki İLK kullanımı**

Alternatif (kiracı başına N+1 sorgu veya uygulama tarafında sıralama) ya sağlayıcı başına ayrı bir kod yolu ya da tüm başarısız satırların belleğe çekilmesini gerektirirdi. PostgreSQL, SQL Server (2012+) ve SQLite (3.25+, Microsoft.Data.Sqlite 10.0.10'un gömdüğü sürüm) üçü de standart pencere fonksiyonlarını destekler; sorgu üç diyalektte neredeyse birebir aynı yazıldı (yalnız `COALESCE`/tip söz dizimi farklı). SQLite entegrasyon testleri (424 test) bu deseni gerçek bir SQLite motorunda doğruladı.

### K-062

**K-062 — Harness'ta shell yoktur; dosya erişimi ve arka plan agent'ları kapalı bırakıldı**

Reflection ile ölçüldü: MAF 1.16.0'ın `HarnessAgentOptions` yüzeyinde **shell erişimi diye bir üye yoktur**; faz 6 planının varsayımı hatalıydı. Sunucuda yüzey açan gerçek üyeler `FileAccessStore` (+ `FileAccessProviderOptions`) ve `BackgroundAgents`'tır. İkisi de yalnızca **değer atandığında** etkinleşir; `AgentDefinitionCompiler` o değerleri hiç atamaz, dolayısıyla varsayılan kapalıdır ve kapatmak için ek bir bayrak gerekmez. `HarnessSettings` bu alanları bilerek yansıtmaz — arayüzden açılabilir olmamalıdırlar.

### K-227

**K-227 — Ses dakikası bir kota birimi değildir** *(kullanıcı kararı)**

Faz 28'in devir notu `QuotaEnforcer`'ın token ve çalıştırma saydığını, ses dakikasının bir birim olmadığını yazmıştı. Yeni bir birim eklemek kota şeması migration'ı, `QuotaEnforcer` genişlemesi, arayüz paneli ve testler demektir. Koruma bunun yerine bu fazın kendi sınırlarından gelir: kiracı başına eşzamanlı bağlantı (5), bağlantı süresi (30 dk), boşta zaman aşımı (2 dk), konuşma parçası süresi (60 sn) ve baytı (8 MB). Ayrıca her konuşma turu **normal bir `runs` satırı** ürettiği için mevcut token ve çalıştırma kotaları ses turlarında da aynen işler.

### K-148

**K-148 — Sürüm çözümü `IVersionedAgentSource` marker arayüzüyle eklendi; `IAgentSource`'a doğrudan metot eklenmedi**

`IAgentSource`'a `ResolveVersionAsync` eklemek `CodeAgentSource`'u da bu metodu uygulamaya zorlardı — kod kaynağının sürüm kavramı yoktur (K-003). Marker arayüz yalnızca sürüm geçmişi tutan kaynakların (`DefinitionStoreAgentSource`) uygulamasını sağlar; `CompositeAgentCatalog.ResolveAsync(name, version, ct)` bir kaynağın `IVersionedAgentSource` olup olmadığını çalışma anında kontrol eder, değilse `AgentPrismException` fırlatır. `CompiledAgentCache` değişmedi — anahtar zaten `(Name, Version, DependencyFingerprint)`.

### K-461

**K-461 — Denetim zinciri yazımında eşzamanlılık, oturum/advisory kilit YERİNE benzersiz dizin + yeniden deneme + rastgele gecikme (jitter) ile çözülür**

K-284'ün "oturum kilidinden kaçının, bağlantı havuzuyla ilişkilendirilmez" ilkesi burada da uygulandı. `(tenant_id, prev_hash)` üzerindeki benzersiz dizin (yalnız `hash IS NOT NULL` satırlarında, geriye dönük uyum için filtreli) yarışı DOĞAL olarak serileştirir; kaybeden `SqlIdempotencyStore`'un izlediği desenle yeniden dener. Gerçek PostgreSQL'e karşı ölçüldü: jitter OLMADAN 20 eşzamanlı yazıcı, hepsi aynı anda aynı "son" satırı yeniden okuyup aynı anda yeniden çarpıştığı için 100 denemede bile YAKINSAMIYORDU; 1-15 ms rastgele gecikme eklenince tek haneli deneme sayısında yakınsadı.

### K-327

**K-327 — `AIJudgeLoopEvaluator` KULLANILMADI; `IRunJudge` sıfırdan yazıldı (Faz 49)**

Ölçüldü (MAF 1.16.0, reflection dökümü): `LoopEvaluation` bir PUAN döndürmez, yalnız `ShouldReinvoke` (bool) ve `Feedback` (metin) taşır; `LoopContext`'in kurucusu canlı bir `AIAgent` **ve** `AgentSession` ister — bitmiş bir çalıştırmayı puanlamak için sentetik bir agent/oturum kurmak gerekirdi. `LoopAgent` ayrı bir yetenektir (bir çalıştırmayı yeterli olana kadar tekrarlar, bir ölçüm değil). K-140'ın "kullanıcı model tabanlı puanlama isterse ayrı bir faz açılır" yeniden açılma koşulu bu fazla karşılandı; K-140 kapandı.

### K-050

**K-050 — Frontend derlemesi dış (outer) MSBuild derlemesinde çalışır**

Ölçüldü: `AgentPrism.UI` üç hedef çerçeve için derlenir ve MSBuild iç derlemeleri **paralel** koşturur. Üçü de aynı `wwwroot/` dizinine yazar; Vite `emptyOutDir` ile dizini önce boşalttığı için birbirlerinin dosyalarını siler ve derleme `ENOENT: no such file or directory, unlink .../index-*.js` ile kırılır. Zincir `BeforeTargets="DispatchToInnerBuilds"` ile dış derlemeye alındı; iç derlemeler damgayı güncel bulup adımı atlar. İkinci bir tuzak aynı dosyada: MSBuild bir hedefin `Condition`'ını `DependsOnTargets`'tan **önce** değerlendirir, bu yüzden bağımlılık zinciri hedeflerin kendi üzerinde kurulursa Node algılama hedefi hiç çalışmaz ve arayüz sessizce derlenmez.

### K-261

**K-261 — `eval_case_results`/`workflow_checkpoints` için `MaxRows` eşiği İLİŞKİLİ tablo üzerinden hesaplanır (Açık Soru 2 çözüldü)**

`id` (eval_case_results'ın sıra sütunu) bir UUID'dir, zaman damgası DEĞİLDİR — `FindRowLimitCutoffAsync`'in `DateTimeOffset?` dönüş tipine dönüştürülemez. `RetentionTargetDefinition`'a `WherePredicate`'ten AYRI bir `RowLimitOrderExpression` alanı eklendi: bu iki hedefte `WherePredicate`'in GERÇEKTEN karşılaştırdığı sütunla (bağlı `eval_runs.completed_at` / `runs.completed_at`) AYNI korele alt sorgudur; diğer 11 hedefte kendi doğrudan sütunuyla özdeştir. Gerçek PostgreSQL'e karşı ölçüldü: 3 `workflow_checkpoints` (3 ayrı `runs.completed_at`) ile `MaxRows=1` istendiğinde en yeni hariç 2'si doğru silindi.

### K-370

**K-370 — `ApprovalEndpoints.DecideAsync`, `audit` kaydını mutasyondan ÖNCE ve `AuditRecorder.WriteAsync` (hataları yutan sarmalayıcı) DEĞİL doğrudan `IAuditLog.WriteAsync` ile yazar (Faz 55)**

K-089'un "audit kaydı mutasyonu kilitler" deseni burada da geçerlidir: bir onay/red kararı geri alınamaz bir eylemdir (yeni bir çalıştırma açar), bu yüzden audit yazımı BAŞARISIZ olursa karar hiç uygulanmamalıdır. Normal yolda kullanılan `AuditRecorder.WriteAsync` yardımcı metodu audit hatalarını KASITLI olarak yutar (gözlemlenebilirlik işlevselliği bozmaz ilkesi) — ama bu, audit'in mutasyonu KİLİTLEMESİ gereken K-089 sınıfı işlemler için yanlış davranıştır; bu yüzden `DecideAsync` `IAuditLog.WriteAsync`'i doğrudan çağırır ve hatayı yutmaz.

### K-311

**K-311 — Dallanma öğeleri KOPYALAR; işaretçi zinciri reddedildi**

İşaretçi zinciri seçilseydi her geçmiş okuması özyinelemeli olurdu ve `SqlChatHistoryProvider` — her agent turunda çalışan en sıcak okuma yolu — üç diyalektte özyinelemeli CTE'ye dönerdi; dallanma kullanmayan tüketici de bedel öderdi. Kopyalamayla okuma yolu TEK SATIR bile değişmez ve `parent_conversation_id`/`branch_from_seq` yalnız köken bilgisidir. Ölçüldü: bin öğelik bir konuşmanın dallandırılması SQLite'ta ölçülebilir gecikme üretmedi (445 testlik paket 13,2 sn). 🚨 Öğeler tek bir `INSERT … SELECT` ile değil, tek işlem içinde satır satır yazılır: yeni öğe kimliği her satırda uuid v7 olmalıdır (K-015) ve üç diyalektin hiçbirinde ortak bir uuid v7 üreteci yoktur.

### K-108

**K-108 — Özet modeli çözümleme sırası: agent ayarı → yardımcı model → agent'ın kendi modeli; özet token'ları çalıştırma toplamına dâhildir**

`CompactionSettings.SummarizationModel` boşsa `AgentPrismOptions.UtilityModel` kullanılır, o da boşsa agent'ın kendi modeli — ucuz bir iş için pahalı bir modelle maliyet üretmemek amacıyla. Özetleme çağrısı agent'ın kendi `AgentResponse`'undan ayrı bir yan-kanal çağrısıdır; `CompactionUsageTrackingChatClient` bunu `compact_history` span'i açarak izler ve `CompactionUsageAccumulator` üzerinden `RunRecordingAgent.CompleteAsync`'in nihai `RunUsage`'ına **birleştirir** (`MergeUsage`). Birleştirilmezse maliyet raporu (Faz 20) ve `AgentRunBudget` eksik kalırdı. Gerçek bir çalıştırmayla doğrulandı.

### K-224

**K-224 — WebSocket bearer token'ı alt protokolde taşınır, sorgu dizesinde kabul edilmez**

Tarayıcı bir WebSocket el sıkışmasına `Authorization` başlığı **ekleyemez** — K-046'nın arayüz kabuğu için tespit ettiği kısıtın aynısı. Sorgu dizesi ise sunucu günlüklerine, ters vekil günlüklerine ve tarayıcı geçmişine yazılır; bir sır oraya konmaz. Token `Sec-WebSocket-Protocol: agentprism.voice.v1, agentprism.token.<token>` ile gelir ve uç onu `BearerTokenValidator.IsValidToken` ile **sabit zamanda** doğrular. Uç bu yüzden dördüncü bir `MapGroup`'tadır (`requireBearerToken: false`); loopback kısıtı, authorization policy ve `Operator` rolü aynen uygulanır — hiçbir katman atlanmaz. `Token_SORGU_DIZESINDE_kabul_EDILMEZ` testi bunu koruyor.

### K-138

**K-138 — `jobs` tablosuna `(schedule_id, scheduled_for)` üzerinde benzersiz kısıt eklendi; faz belgesinin DDL'i eksikti**

`docs/arsiv/fazlar/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md` bölüm 17.5 metni bu kısıtın var olduğunu söylüyordu ("Aynı schedule_id + scheduled_for için ikinci bir jobs satırı oluşmasını benzersiz bir kısıt engeller") ama bölüm 17.2'deki DDL örneğinde kısıt yoktu — metin doğru niyeti anlatıyordu, DDL eksikti. Migration 0008 metne göre tamamlandı: `CONSTRAINT jobs_schedule_scheduled_uq UNIQUE (schedule_id, scheduled_for)`. PostgreSQL NULL'ları birbirinden ayrı saydığı için elle tetiklenen (`schedule_id IS NULL`) işler bu kısıtla asla çakışmaz — ikinci savunma hattı zaten `TryClaimNextRunAsync`'in atomik CAS'ıdır.

### K-207

**K-207 — Paket adı `AgentPrism.Google`, sağlayıcı adı `google`** *(kullanıcı kararı)**

Faz dokümanı `.UseGemini(...)` örneği veriyordu. `gemini` adı paketi tek bir model ailesine kilitler; aynı paket ileride Vertex AI'yi de kapsayabilir ve o zaman ikinci bir paket ya da yanıltıcı bir ad kalırdı. Sağlayıcı adı `ModelBinding.Provider` üzerinden **veritabanında saklanır**; sonradan değiştirmek kayıtlı tüm agent tanımlarını bozar, bu yüzden ilk seferde doğru seçilmesi gerekiyordu. Uzantı metodu da tutarlılık için `UseGoogle(...)`; yapılandırma bölümü `AgentPrism:Providers:Google`.

### K-346

**K-346 — Migration şablonlama genelleştirildi: `SqlStoreContext.MigrationTemplateValues` (Faz 51)**

`{dimension}` yer tutucusu `{schema}` ile AYNI mekanizmadan geçemezdi (`SqlQueriesBase.ApplySchema` yalnız şemayı bilir) ama "vektöre özel" bir çözüm de yanlış katmana ait olurdu (`MigrationRunner` üç sağlayıcıda ortaktır, K-176). Genel bir `IReadOnlyDictionary<string,string>` eklenip `MigrationRunner.ApplyTemplate` içinde şema değiştirmesinden SONRA uygulanır; SQL Server/SQLite boş sözlükle çalışmaya devam eder. PostgreSQL entegrasyon test fixture'ının imajı da bu fazda `postgres:18-alpine`'dan `pgvector/pgvector:pg18`'e değişti — migration 0024 uzantı gerektirdiği için TÜM PostgreSQL test paketi bu değişiklik olmadan patlardı.

### K-303

**K-303 — "Olumsuz puan" otomatik terfi tetikleyicisi olarak `Binary` için `Value == 0`, `Stars` için `Value <= 2` (5 üzerinden) tanımlandı**

`RunScoreKind`in iki türü var ama `RunStatistics.PositiveRate` (Faz 31, K-239) yalnız `Binary` puanları sayar — `Stars` için "olumsuz" eşiği hiç tanımlanmamıştı. Faz 45 bu tanımı yapmak zorunda kaldı: `Stars 1-5` ölçeğinde orta noktanın (3) ALTI olumsuz sayıldı (1-2). Eşik `RunToCasePromoter.ResolveSourceKindAsync` içinde yerel bir filtre olarak yazıldı; `IRunScoreStore`'a yeni bir sorgu metodu eklenmedi (`ListAsync` zaten yeterli, filtre uygulama katmanında).

### K-164

**K-164 — SSRF koruması `WebhookHttpClient`'ın İÇİNE gömülüdür; `IHttpClientFactory` kullanılmaz**

İki gerekçe: (1) `Microsoft.Extensions.Http` paketi bağımlılık grafiğine eklenirdi (K-007) — ölçüldü, `AgentPrism.Core` grafiğinde yok; (2) tüketicinin adlandırılmış istemciyi yeniden yapılandırması korumayı sessizce kaldırabilirdi. Denetim `SocketsHttpHandler.ConnectCallback` içindedir: **doğrulanan adres, soketin bağlandığı adresin ta kendisidir**. Önce doğrulayıp sonra `SendAsync(url)` çağırmak bir TOCTOU açığı bırakırdı — `HttpClient` adı yeniden çözer ve saldırgan iki çözümleme arasında yanıtı değiştirebilir (DNS yeniden bağlama). `AllowAutoRedirect = false`: yönlendirme, denetimden geçmiş bir adresten özel ağa kaçış yoludur.

### K-178

**K-178 — Migration numaraları sağlayıcı başına bağımsızdır**

`AgentPrism.SqlServer` kendi gömülü `.sql` setini taşır ve numaralandırma `0001`'den başlar; PostgreSQL'in 0001–0013'ü ile **eşleşmez**. Eşleştirmeye çalışmak, ileride bir sağlayıcıya özel düzeltme gerektiğinde (yalnız SQL Server'da bir indeks eklemek gibi) numaraların kaymasına ve kilitlenmeye yol açardı. `__migrations` defteri sözleşmesi aynıdır (ad, checksum, uygulanma zamanı) ve checksum hesabı satır sonu normalleştirmesi dâhil **birebir aynı** paylaşılan kodu kullanır. SQL Server tarafı tek bir `0001_initial.sql` ile PostgreSQL'in 0001–0013 **birikmiş** sonucunu kurar: yükseltilecek bir kurulum yoktur, geçmişi oynatmak yalnızca okunması zor bir dosya üretirdi.

### K-454

**K-454 — `POST /api/approvals/rules`'ta "aynı kapsam + aynı koşul" çakışması, depoyu değiştirmeden ÜRETİLEN id ile DÖNEN id'yi karşılaştırarak tespit edilir**

`IToolApprovalRuleStore.AddAsync` bilerek idempotent bir upsert'tir (aynı kapsam ikinci kez eklenince var olan satır sessizce döner) — bu davranış `ToolApprovalResolver.RememberAsync`'in agent onay akışı için ZORUNLUDUR (kullanıcı "bir daha sorma"ya iki kez tıklarsa hata almamalı). Admin ekranının `409` istemesi bu sözleşmeyi BOZMADAN elde edildi: uç kuralın `Id`'sini KENDİSİ üretir, `AddAsync`'e verir, dönen satırın `Id`'si ürettiğiyle AYNI değilse (yani depo var olan bir satırı döndürdüyse) `409` çevirir. Depo sözleşmesi tek, iki tüketici farklı anlam veriyor.

### K-075

**K-075 — Rol policy'si kayıtlı değilse eski davranışa dönülür; kontrol `MapAgentPrism()` çağrısında bir kez yapılır**

`AgentPrismRolePolicies.Resolve` her rol için `IAuthorizationPolicyProvider.GetPolicyAsync(name)` çağırır ve sonucu `AgentPrismEndpointOptions.RequireRolePolicies` ile birlikte değerlendirir. `MapAgentPrism()` `app.Build()` sonrası, istek işleme dışında çağrıldığı için `GetAwaiter().GetResult()` burada güvenlidir (varsayılan sağlayıcı `Task.FromResult` ile senkron yanıtlar). Sonuç: sürüm yükseltmesi, rol policy'lerini henüz tanımlamamış bir kurulumu **kırmaz** — K-042'nin meta ucu için verdiği kararla aynı gerekçe.

### K-440

**K-440 — `ClientToolResult.Result`/`ErrorMessage` 65.536 karakterle sınırlanır**

Plan 61.4 "boyut sınırı" istiyordu ama bir sayı vermiyordu; `request.Message` için de bu depoda hiç sınır yok (emsal yok). İstemci tool sonucu farklı bir tehdit sınıfı taşıyor: DOM metni veya bir API yanıtı gibi rastgele tarayıcı içeriği, oturum geçmişinde KALICI kalır ve her sonraki turda bağlam penceresini tüketir — `request.Message`'ın aksine, kullanıcının kendi yazdığı (zaten güvenilen) metin değildir. 64K karakter bir sayfalık çıkarılmış metni rahatça karşılar; sınır `RunAsync`'te akış başlamadan `400` ile uygulanır.

### K-074

**K-074 — Devre kesici `ModelProviderRegistry.CreateChatClient` seviyesinde dekoratör olarak entegre edildi**

Devre kesici hiçbir sağlayıcı paketinin (`AgentPrism.OpenAI` vb.) içine gömülmedi; `ModelProviderRegistry` yapıcısına opsiyonel `ModelProviderCircuitBreaker? circuitBreaker = null` eklendi ve üretilen her `IChatClient`, sağlayıcı adına göre `CircuitBreakingChatClient` ile sarılır. Opsiyonel parametre sayesinde doğrudan `new ModelProviderRegistry(providers)` ile kurulan mevcut testler (`ModelProviderRegistryTests`) değişmeden geçti. Sonuç: Faz 26/27'de eklenecek Anthropic/Gemini sağlayıcıları hiçbir ek kod yazmadan aynı korumayı `IModelProvider` olarak DI'da kayıtlı olmaktan başka bir şart olmaksızın alır.

### K-200

**K-200 — Parti silme her sağlayıcıda farklı teknik kullanır**

PostgreSQL `DELETE ... LIMIT` tanımaz; `ctid` alt sorgusu kullanılır (`DELETE FROM t WHERE ctid IN (SELECT ctid FROM t WHERE kosul LIMIT n)`). SQL Server `DELETE TOP (n) FROM t WHERE kosul` kullanır — alt sorgu gerekmez ve `TOP (0)` hata VERMEZ (OFFSET/FETCH'in aksine, K-026/hafiza tuzağı burada geçerli değil). SQLite `DELETE ... LIMIT`'i varsayılan derlemede desteklemez (`SQLITE_ENABLE_UPDATE_DELETE_LIMIT` gerekir); PostgreSQL ile aynı gerekçeyle `rowid` alt sorgusu kullanılır. Üçü de kasıtlı olarak SIRASIZDIR: amaç bir parti eşleşen satırı silmektir, belirli bir sırada silmek değildir — bu, partiler arasında ek bir `ORDER BY` maliyetinden kaçınır.

### K-462

**K-462 — Veri konusu önizleme/silme AYNI SQL işlemi (transaction) üzerinden yürür: önizleme her zaman geri alınır (`ROLLBACK`), gerçek silme yalnız çağıranın denetim yazımı (`beforeCommitAsync`) başarılı olursa `COMMIT` edilir (Faz 64, K-370 emsali)**

Aynı `DELETE` sorgu kümesinin hem sayım hem gerçek silme için kullanılması, iki ayrı COUNT/DELETE sorgu setinin zamanla birbirinden sapması riskini ortadan kaldırır (tek doğruluk kaynağı). Denetim yazımı işlemin İÇİNDEN, commit'ten ÖNCE çağrılır; yazma başarısız olursa hem denetim kaydı hem silme birlikte geri alınır — "silinemeyen bir veriyi silindi diye kaydetmek" (K-370'in ele aldığı risk) yerine "silinemeyen bir veri denetimsiz silinmez" garantisi verir.

### K-428

**K-428 — Aday listesi tur numarası taşımaz: `UCUNCU-FAZ-ADAYLARI.md` → `ADAYLAR.md`** *(kullanıcı kararı)**

Dosya üçüncü turun listesi olarak doğmuştu ama dördüncü dalga (Faz 53–56) ve sonrası da ona yazıldı; bugün TEK aday listesidir ve adı yanlış yönlendiriyordu. Ad turdan bağımsız hâle getirildi. 56 dosyadaki 86 referans satırı, her dosyanın kendi konumuna göre yeniden hesaplanarak güncellendi (skill'ler, `dokuman-bakim.py`'nin `BUTCE` anahtarı ve ürettiği bağlantılar, faz dokümanları, manuel test dosyaları). Ayrı bir aday listesi dosyası AÇILMAZ — iki yerde tutmak kayma üretir (bu kural K-425'in devamıdır).

### K-455

**K-455 — `conditions_hash` kanonikleştirmesi sayısal normalizasyon YAPMAZ (`100` ile `100.0` farklı hash üretebilir); sıralama + `JsonElement.GetRawText()` yeterli sayıldı**

`faz-denetim`'in bağımsız denetçisi bunu 🟢 (aday listesi) olarak işaretledi: bugün TEK bir yazma yolu var (`POST /api/approvals/rules`, `System.Text.Json` ile serileştirilmiş sayı), dolayısıyla aynı sayısal değer her zaman aynı ham metni üretir — pratikte çakışma riski yok. Tam sayısal normalizasyon (ör. `decimal` ayrıştırma) plan kapsamının dışındaydı (plan yalnız "sıralama + boşluk normalizasyonu" istiyordu) ve gereksiz karmaşıklık eklerdi.

### K-147

**K-147 — A/B deney ataması yalnızca `AgentEndpoints.RunAsync` (deneme ucu) içine gömülüdür**

`IAgentCatalog.ResolveAsync` tüm tüketiciler (`EvalJobHandler`, `AgentBatchJobHandler`, `ChildAgentInvoker`, `WorkflowAgentBinding`, `/v1/*` uçları) arasında paylaşılan tek kapıdır. Deney ataması bu ortak kapıya gömülseydi alt-agent çağrıları ve workflow adımları da habersiz A/B'ye girerdi — bir kullanıcının gördüğü talimat çalışma anında öngörülemez hâle gelirdi. Her tüketici `IAgentCatalog.ResolveAsync`'i kendi amacıyla (güncel sürüm / sabit sürüm) çağırmaya devam eder.

### K-509

**K-509 — `CapabilityCoverageTests` kapsamı TÜM kayıt giriş noktalarıdır, yalnız `Use*`/`Map*` değil (Faz 73, F-120, kullanıcı kararı)**

Plandaki filtre iki yönden yetersiz çıktı: `AddToolApprovalPolicy`/`AddWorkflowFunction` kapının dışındaydı, `Configure`/`Services` ise "taban çizgisi boş doğar" maddesini tutmuyordu. Kapsam, alıcısı `IAgentPrismBuilder`, `IServiceCollection`, `IHostApplicationBuilder`, `IHealthChecksBuilder` veya `IEndpointRouteBuilder` olan her `Add*`/`Use*`/`Map*` üyesi oldu — 39 üye; dördü haritaya eklendi, taban çizgisi BOŞ doğdu. Eşleşme kelime sınırlıdır (sınırsız `AddAgentPrism()` metni `AddAgent` üyesine kefil oluyordu). Cırcır deseni `SourceLanguageTests` (K-410) ile aynıdır.

### K-328

**K-328 — Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrılır; yeni bir sütun açılmadı (Faz 49)**

Yargıcın KENDİ çalıştırması `agent_name = "judge:{ad}"` ile bir `runs` satırı olarak, `RunKind.Eval` ile kaydedilir (`ModelRunJudge`, ephemeral bir `ChatClientAgent`'ı doğrudan `RunRecordingAgent` ile sarar — `IAgentCatalog`'a hiç girmez). İki depo uygulaması da (`InMemoryRunStore`, `SqlRunStore`) bu türü `RunStatistics`'ten zaten dışlıyordu (Faz 18'in açık soru 4 kararı, K-141) — bu fazın ihtiyacını değiştirmeden karşıladı. Gerçek koşumla doğrulandı: `support` agent'ının `totalCost`'u yargıç çağrılarından ETKİLENMEDİ, `GET /api/runs?kind=Eval` yargıcın satırlarını `agentName: "judge:model"` ile listeledi.

### K-322

**K-322 — Devre kesici içerik engellemesini ardışık hata SAYMAZ**

Guard K-320 ile döngünün içine, devre kesici ise dışında kaldı; bir `AgentPrismContentBlockedException` `CircuitBreakingChatClient`'ın `catch (Exception)` bloğuna ulaşıyor ve `RecordFailure` çağırıyordu. Ölçüldü: `FailureThreshold = 2` ile arka arkaya engellenen istekler sağlayıcıyı kapatıyordu — bir politika kararı bir kesintiye dönüşüyordu. Çözüm `catch (AgentPrismContentBlockedException)` ayıklamasıdır; gerekçe K-206'nın `ContentFilterDetectingChatClient`'ı devre kesicinin dışında tutan gerekçesiyle aynıdır: engelleme sağlayıcının sağlıklı olduğunu gösterir, istek ağa hiç çıkmadı. Üç test doğrular (birim, fonksiyonel, gerçek koşum).

### K-262

**K-262 — Şablonda `IncludeSymbols=false` zorunlu**

Ölçüldü: `src/Directory.Build.props` her paket için `IncludeSymbols=true` + `snupkg` atar. `AgentPrism.Templates` derlenmez (`IncludeBuildOutput=false`) — üretecek `.pdb` yok, dolayısıyla eşlik eden sembol paketi BOŞ kalır ve `NuGet.Build.Tasks.Pack` onu `NU5017` ("ne bağımlılık ne içerik var") ile reddeder. Ana paketin `nuspec`'indeki `<files>` listesi doğru dolu olsa bile `dotnet pack` **başarısız** olur — hata sembol paketinden gelir, ana paketten değil; ayrı bir `dotnet pack -t:GenerateNuspec` ile ara `nuspec` dosyaları karşılaştırılarak izole edildi.

### K-270

**K-270 — `AgentPrism.Testing` yalniz `net10.0` hedefler (cogul `TargetFrameworks` ozelligiyle ezilerek)**

`Microsoft.AspNetCore.TestHost` merkezi surumu (10.0.10) yalniz `net10.0` destekler (`NU1202`, net8.0/net9.0'da basarisiz) — barindirma paketi surumu framework surumuyle birebir eslenir, tek bir surum coklu TFM'i kapsayamaz. `src/Directory.Build.props`'tan miras kalan cogul `TargetFrameworks` ozelligi ayni cogul adla `net10.0`'a sabitlendi (tekil `TargetFramework` DEGIL) — K-263'un `dotnet pack` capraz-hedefleme tuzagiyla ayni gerekce: `dotnet pack` orkestrasyonu cogul degeri okumaya devam eder.

### K-130

**K-130 — Yanıtlanmış çalıştırma `AwaitingInput` olarak kalır; yanıt YENİ bir satır açar**

Durumu geriye dönük değiştirmek olay akışının append-only kuralını (K-014) bozardı: "bu çalıştırma ne zaman ve neyle bitti" sorusu kaydın içinde cevaplanabilir kalmalıdır. Devam eden iş yeni `runs` satırındadır — `ResumeStreamingAsync` ile aynı kural. `ListPendingRequestsAsync` yalnızca gerçekten `AwaitingInput` durumundaki bir çalıştırma için istek döndürür; aynı çalıştırmayı ikinci kez yanıtlamak bir **dal** açar (farklı bir cevabı denemek) ve bu kasıtlıdır. Ayrıca bekleyen bir çalıştırmanın kontrol noktaları `KeepCheckpointsAfterCompletion` kapalı olsa bile silinmez: yanıt tam olarak onlardan devam eder.

### K-329

**K-329 — İki kapılı varsayılan: `OnlineEvaluationOptions.Enabled = false` VE `SampleRate = 0.0` (Faz 49)**

Faz 48 ile aynı düz K1 okuması: yargıç her puanlamada gerçek para harcayan bir model çağrısı yapar. Tek kapı (yalnız `Enabled`) yeterli olmazdı — bir kurulum `Enabled=true` yapıp oranı unutursa yine tam oranda (SampleRate varsayılanı 1.0 olsaydı) harcama başlardı. Üçüncü savunma `MaxScoresPerHour` (varsayılan 100). Gerçek koşumla doğrulandı: varsayılan ayarlarla yeni bir çalıştırma `GET /api/runs/{id}/feedback`'te BOŞ döndü ve `kind=Eval` satır sayısı artmadı; yalnız `AgentPrism__OnlineEvaluation__Enabled=true` + `:SampleRate=1.0` ortam değişkenleriyle açılınca yargıç gerçekten çağrıldı.

### K-006

**K-006 — Trim/AOT uyumu katman bazlı**

`Abstractions`, `Core`, `PostgreSql`, `OpenAI` → AOT uyumlu. `AspNetCore`, `UI`, meta → değil. `AgentPrismAotCompatible` özelliği ile. Veremeyeceğimiz bir vaadi vermiyoruz. **(düzeltildi: 2026-08-02, Faz 4)** `IsAotCompatible` türetmesi `src/Directory.Build.props` içindeydi; o dosya csproj gövdesinden **önce** yüklendiği için csproj'da yazan `<AgentPrismAotCompatible>false</AgentPrismAotCompatible>` hiçbir işe yaramıyordu ve bayrak geri alınamaz biçimde `true` kalıyordu. Ölçüldü: `AgentPrism.AspNetCore` minimal API yönlendirmesi için onlarca `IL2026`/`IL3050` üretti. Türetme `Directory.Build.targets` içine taşındı — orası csproj okunduktan sonra çalışır.

### K-183

**K-183 — İki kalıcılık sağlayıcısı aynı anda kaydedilirse açılışta UYARI loglanır**

`UsePostgreSql()` ve `UseSqlServer()` aynı zincirde çağrılırsa son kayıt kazanır (ikisi de `Replace` kullanır, K-025) ve verinin hangi veritabanına gittiği çağrı sırasına bağlanır. Bu bir yapılandırma hatasıdır ama **engellenmez**: bilinçli bir geçiş senaryosu olabilir ve kütüphanenin tüketicinin niyetini reddetmesi doğru değildir. Bunun yerine her `Use*` uzantısı biriken bir `SqlPersistenceRegistration` işareti ekler (`AddSingleton`, `TryAdd` değil) ve `MigrationHostedService` açılışta birden fazlaysa hangi sağlayıcının kazandığını da yazan bir uyarı loglar. Sessiz kalmak, veri kaybının en pahalı biçimidir.

### K-103

**K-103 — Alt agent onay isteyemez (v1); isteyen alt çalıştırma `Failed` olur**

Faz 6'da ölçüldü: onay gereken çağrıda çalıştırma **biter** ve karar bir sonraki turun girdisidir (sapma S4). Bir alt agent ağacın ortasında onay isterse tüm ağacın durdurulup daha sonra tam olarak aynı noktadan sürdürülmesi gerekir; MAF bunun için bir mekanizma sunmuyor. Davranış sessiz değildir: alt çalıştırma `Failed` olarak kapanır (`RunRecordingAgent`, `Depth > 0` iken yanıtta `ToolApprovalRequestContent` görürse) ve çağıran, hangi tool'un onay istediğini söyleyen bir tool sonucu alır. Tespit tek yerdedir (`ChildRunApproval`), iki tüketicisi vardır.

### K-517

**K-517 — `<see cref>` paketlenen OpenAPI belgesinde TAM İMZA olarak render edilir; sözleşme tiplerinde `<c>ÜyeAdı</c>` yazılır (Faz 75)**

Ölçüldü: `docs/openapi/agentprism.json` içinde **26 yerde** `string? ClientToolResult.ErrorMessage`, `int? ModelBinding.MaxOutputTokens` gibi metinler vardı. Kaynağı AgentPrism değil ASP.NET Core'un XML doküman üretecidir. Site kopyası bunu bir süzgeç kuralıyla siliyordu, paketlenen kopya silmiyordu. OpenAPI belgesini okuyan tüketici CLR imzasını değil JSON alanını görür; 43 satırda `<see cref>` yerine `<c>` yazıldı ve sızıntı sıfıra indi. Sınır: yalnız OpenAPI'nin seri hâle getirdiği sözleşme tipleri — iç tiplerde `<see cref>` IDE gezinmesi için kalır.

### K-158

**K-158 — Hız sınırı ve kota AYRI mekanizmalardır; biri bellekte, biri veritabanında**

İki farklı zaman ölçeği iki farklı çözüm ister. Hız sınırı saniye/dakika ölçeğinde ani yükü düzleştirir ve bir istek kadar yaşar — bellekte tutmak doğrudur, kalıcılaştırmak her isteğe bir veritabanı gidişi eklerdi. Kota gün/ay ölçeğinde toplam tüketimi sınırlar ve süreç yeniden başlasa da korunmalıdır — bellekte tutmak, çok örnekli bir dağıtımda kotayı örnek sayısına bölerdi. `System.Threading.RateLimiting` ASP.NET Core paylaşılan çerçevesindedir (ölçüldü: `Microsoft.AspNetCore.App.Ref` 9.0.9 ve 10.0.0 içinde `System.Threading.RateLimiting.dll` var), bu yüzden K-007 kapsamında **yeni paket eklenmedi**.

### K-048

**K-048 — Arayüz varlıkları Brotli sıkıştırılmış gömülür**

Ölçüldü: 315,1 KB ham → **80,9 KB** Brotli. İstemci `br` kabul ediyorsa içerik olduğu gibi gönderilir ve çalışma anında **sıfır** sıkıştırma maliyeti oluşur; kabul etmiyorsa bir kez açılıp bellekte tutulur. Hem ham hem sıkıştırılmış gömmek assembly'yi gereksiz büyütürdü, hiç sıkıştırmamak ise `AgentPrism.UI.nupkg`'i üç hedef çerçeve için üç kat şişirirdi. Saklama biçimi kaynak adından okunur (`.br` uzantısı); derleme zinciri ile çalışma zamanı arasında senkron kalması gereken ikinci bir bildirim dosyası yoktur. Sıkıştırılmış ve açılmış temsiller **farklı `ETag`** taşır — aksi halde bir ara önbellek yanlış kodlamayı sunabilirdi.

### K-189

**K-189 — `SqlWebhookStore.ReadSubscription` diziyi `Dialect.ReadTextArray` yerine doğrudan `reader.GetFieldValue<string[]>` ile okuyordu**

K-182 dizilerin SQL Server'da JSON metni olarak taşındığını ve okumanın `SqlDialect.ReadTextArray` üzerinden geçmesi gerektiğini karara bağlamıştı; `SqlWebhookStore` (paylaşılan katman) bunu atlayıp Npgsql'in doğal dizi tipine dayanan `GetFieldValue<string[]>` çağırıyordu. PostgreSQL'de çalışıyordu (Npgsql array desteği) ama SQL Server'da `System.String`'i `System.String[]`'e cast edemediği için `InvalidCastException` fırlatıyordu. Düzeltme: `ReadSubscription` `static`'ten örnek metoduna çevrildi ve `Dialect.ReadTextArray(reader, 4)` çağırır.

### K-257

**K-257 — Kota ölçeri yalnız KAYITLI kiracıları tarar**

`TenantDescriptor` belgesi kiracı kaydının ZORUNLU olmadığını söyler (`runs.tenant_id` bir yabancı anahtarla bağlanmaz) ve `IQuotaStore`'da "tüm kiracıları listele" sorgusu yoktur — yalnız `ListAsync(tenantId)` (tek kiracı kapsamlı). Faz kapsamı "yeni tablo/uç yok"tu; çapraz kiracı listeleme için `IQuotaStore`'a yeni bir üye eklemek (üç SQL sağlayıcısında uygulanması gerekir) bu sınırı aşardı. `QuotaEnforcer` etkilenmez — denetim hâlâ doğru çalışır, yalnız gösterge panosu görünürlüğü kısıtlıdır.

### K-051

**K-051 — `AgentPrism.UI.csproj` SDK'yı açık `Import` ile yükler**

`Sdk="Microsoft.NET.Sdk"` niteliği kullanıldığında SDK hedefleri projenin **en sonuna** yerleştirilir; `AgentPrism.UI.Frontend.targets` ondan önce yüklenir ve `BeforeTargets="AssignTargetPaths"` hedefi *"the target ... does not exist in the project, and will be ignored"* mesajıyla **sessizce** atılır. Sonuç ölçüldü: arayüz varlıkları hiç gömülmüyor, paket arayüzsüz üretiliyor ve derleme başarılı görünüyordu. Açık `<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />` biçiminde frontend hedefleri SDK hedeflerinden sonra yüklenir. Aynı tuzak, SDK hedeflerine kanca atan her `.targets` dosyası için geçerlidir.

### K-312

**K-312 — `conversations.parent_conversation_id` yabancı anahtar TAŞIMAZ**

Plan `ON DELETE SET NULL` öngörüyordu. Ölçüldü: SQL Server kendine referans veren bir yabancı anahtarda `SET NULL` kabul etmez (hata 1785, "may cause cycles or multiple cascade paths"). Kısıtı yalnız PostgreSQL ve SQLite'a koymak aynı silme işlemini üç sağlayıcıda üç farklı sonuca çevirirdi; K-184'ün dersi şema farkının DAVRANIŞ farkına dönüştürülmemesidir. Sonuç her sağlayıcıda aynıdır: ana konuşma silinirse dal yaşamaya devam eder, işaretçi artık çözülemeyen bir kökeni gösterir ve hiçbir okuma yolu onu JOIN'lemez. `CASCADE` zaten reddedilmişti — bir dalı ana konuşmanın saklama politikasına bağlardı.

### K-452

**K-452 — `POST /api/approvals/rules` gövdesi `ArgumentsHash` alanı TAŞIMAZ; yalnız `toolName`/`agentName`/`argumentConditions`**

`ArgumentsHash` bir parmak izidir — yalnız `ToolApprovalResolver`'ın "bu argümanları hatırla" akışında, gerçek bir çağrının argümanlarından hesaplanır; bir admin'in elle bir hash yazması anlamsızdır ve K2'nin ("arayüzden ifade yazılamaz") ruhuna aykırıdır. İki oluşturma yolu bilinçli olarak AYRIK bırakıldı: agent onay akışı `ArgumentsHash` yazar, admin ekranı `ArgumentConditions` yazar; bir kuralda ikisi birden asla oluşmaz (Açık Soru 1'in A seçeneği doğal sonuç oldu — HTTP sözleşmesi zaten ikinciyi sunmuyor, ayrıca `400` doğrulaması gerekmedi).

### K-362

**K-362 — Heartbeat toplu yazılır: `IRunStore.TouchHeartbeatAsync` tek `Guid` değil `IReadOnlyCollection<Guid>` alır (Faz 54, Açık Soru 1, seçenek B)**

Plan taslağı imzası tek `runId` alıyordu ama aday listesinin kendi önerisi ("tek bir `UPDATE ... WHERE id IN (...)` ile N çalıştırmayı günceller") çelişiyordu — taslak düzeltildi. `RunHeartbeatWriter` her turda `IRunCancellationRegistry.ActiveRunIds`'in anlık görüntüsünü alır ve depoya BİR kez yazar; N suren çalıştırma için N ayrı sorgu yerine tur başına bir sorgu, sıcak yola hiçbir şey eklemez. Bu yüzden `IRunCancellationRegistry`'ye yeni bir `ActiveRunIds` özelliği eklendi (Faz 7'den önce ucuz bir arayüz genişlemesi).

### K-481

**K-481 — Kullanıcı kimliği ve etiketler METRİK etiketi OLMAZ; yalnız sorgu boyutudur (Faz 68)**

Serbest bir etiket kümesini `Meter` boyutuna çevirmek zaman serisi kardinalitesini ÜST SINIRSIZ büyütür ve zarar TÜKETİCİNİN metrik arka ucunda oluşur. Kapı yorum değil testtir: `TelemetryTagTests` dört enstrümanın etiket kümesini AÇIKÇA yazarak sabitler ve gerçekten kullanıcı+etiket döndüren bir bağlamla uçtan uca koşar. Aynı gerekçeyle `agentprism.tokens` yalnız iki `direction` yayar: kırılım sayaçları bu iki toplamın İÇİNDE sayılır, üçüncü bir yön olarak yayılmaları panoda her token'ı MÜKERRER sayardı. Kırılım `GET /api/stats`'ın `byUser`/`byLabel` alanlarından okunur.

### K-282

**K-282 — Kiracı yalıtımı iki depo örneğiyle değil, değiştirilebilir tek bir kiracı bağlamıyla sınanır**

Faz 41 planı iki kiracı için iki depo örneği kurmayı öngörüyordu. Bellek içi depolar kendi durumlarını **örnek içinde** taşır; iki örnek aynı arka uca bakmaz ve "A'nın yazdığını B görüyor mu" sorusu hiç sorulamaz. `MutableTenantContext` (test altyapısı) tek bir depo örneğinin kiracısını çağrılar arasında değiştirir; kiracıyı parametre olarak alan depolar zaten etkilenmez. Yan fayda: `TenantIsolationContract<TStore>` ortak yaşam döngüsü tesisatını da devraldığı için 19 sözleşmedeki birebir aynı kopyalar silindi ve yeni sözleşmeler **ek koşum sınıfı gerektirmedi**.

### K-281

**K-281 — `TenantAgnosticAttribute` `AgentPrism.Sql.Shared` içinde ve `internal`'dir**

Faz 41 planı özniteliği test projesinde tanımlamayı öneriyordu. Uygulanamaz: muafiyetin gerekçesi **metodun yanında** durmalıdır (planın Açık Soru 2'de "liste dosyası uzaktadır ve bayatlar" diye reddettiği şey), bu da özniteliğin depo kodundan görünmesini gerektirir. Öznitelik `internal` olduğu için public sözleşme büyümez; K-176'nın linked-source modeli sayesinde her sağlayıcı derlemesine ayrı ayrı derlenir ve o derlemenin entegrasyon test projesi `InternalsVisibleTo` ile okur. Yansıma yalnız test projesindedir; `AgentPrism.Sql.Shared` yansımaya dokunmaz ve AOT duruşu etkilenmez.

### K-173

**K-173 — Prompt "aktarma" bu fazda panoya kopyalama olarak kaldı, agent editör entegrasyonu ertelendi** *(kapsam daraltma)**

Tam "aktarma" (agent `Metadata`'sına `mcp.prompt.*` yazma + sunucudaki değişimi rozetle gösterme) `agent-editor.tsx`'in değiştirilmesini gerektirir — ayrı bir ekranın entegrasyonu, tek oturumluk kapsamı önemli ölçüde büyütürdü. MCP ekranındaki "Copy" düğmesi kaynak sunucu/prompt adı ve SHA-256 özetini yorum olarak taşıyan hazır metni panoya yazar; yönetici elle yapıştırır. Anlık görüntü ilkesi (uzak sunucu agent'ı doğrudan etkileyemez) korunur; yalnız otomatik rozet gösterimi eksik kalır.

### K-139

**K-139 — Eval (Faz 18) için yeni bir NuGet paketi eklenmedi**

Ölçüldü: `EvalItem`/`EvalCheck`/`EvalChecks`/`LocalEvaluator`/`FunctionEvaluator` `Microsoft.Agents.AI` ad alanındadır — `AgentPrism.Core` zaten bu pakete **doğrudan** referans veriyor. Destek tipleri (`EvaluationMetric`, `EvaluationResult`, `EvaluationRating`) `Microsoft.Extensions.AI.Evaluation`'dadır; bu paket `Microsoft.Agents.AI.Abstractions` üzerinden **geçişli** gelir ve doğrudan `using` ile derlemede sıfır uyarıyla kullanılabildi (`dotnet build` kanıtı: 0 uyarı). Plan dokümanının 18.2'de öngördüğü "ayrı pakete taşıma" ihtimali hiç gerekmedi.

### K-203

**K-203 — `sessions`/`conversations` ayrı hedeftir**

25.1'in saklama tablosu ikisini tek satırda gruplamıştı. Uygulamada `conversations` hedefi silinince `conversation_items` ve `responses` `ON DELETE CASCADE` ile birlikte gider (aynı `traces`→`spans`, `jobs`→`job_items` deseni) — ayrı bir "conversation_items" hedefine gerek yoktu. `sessions` tablosu ise bağımsızdır (kendi `updated_at`'i vardır, cascade kaskadı yoktur). İkisini ayrı hedef yapmak kullanıcıya daha ince kontrol verir (birini açıp diğerini kapalı bırakabilir) ve varsayılan davranışı DEĞİŞTİRMEZ: ikisi de `RetentionTargets.UserDataTargets` içinde, ikisi de varsayılan `MaxAgeDays = null` (kapalı).

### K-116

**K-116 — `/v1/chat/completions` bu fazda çok modlu girdi kabul etmez** *(kapsam kararı)**

DoD yalnızca `/v1/responses`'ın OpenAI biçimli görsel girdiyi kabul etmesini ister (bölüm 14, doğrulama kapıları). `/v1/responses` bunu MAF'ın kendi `OpenAIResponses.ToAgentRunRequest` çözümleyicisi + `AttachmentIngestion` ile bedavaya alır; Chat Completions ise kablo biçimini elle çözer (`OpenAIChatCompletionsEndpoints.ReadContent`) ve şu an yalnızca `type: "text"` parçalarını okur. Görsel/dosya parçalarını eklemek ayrı, küçük bir iştir; DoD'yi genişletmeden bilinçli olarak bu faza alınmadı.

### K-033

**K-033 — Tool taraması açık işaretleme ister (`[AgentPrismTool]`)** *(kullanıcı kararı)**

`AddToolsFrom` bir tipin **tüm** public metotlarını alsaydı, o sınıfa eklenen her yeni metot sessizce agent'lara açılırdı — K2 güvenlik sınırının kenarından sızıntı. Açık işaretleme bunu kapatır. Ayrıca C# statik sınıfları tür argümanı kabul etmez (`CS0718`), bu yüzden `AddToolsFrom(Type)` aşırı yüklemesi de gerekti — tool sınıfları çoğunlukla `static class` olur. Statik metotlar doğrudan bağlanır; örnek metotlarında taşıyıcı nesne `AIFunctionArguments.Services` üzerinden çağrı anında çözülür, böylece tool sınıfı DI'dan servis alabilir ve durum çağrılar arasında sızmaz.

### K-294

**K-294 — Sınıflandırma TEK bir noktada, `RunRecordingAgent.CompleteAsync` içinde, `error` `null` değilse çalışır**

`RunCoreAsync`/`RunCoreStreamingAsync`'in üç ayrı hata yakalama noktası (model hatası, güvenlik filtresi, alt çalıştırma onay reddi) hepsi `CompleteAsync`'e bir `RunError` geçirir; sınıflandırma orada, depoya yazımdan hemen önce, tek satırda yapılır. Her çağrı noktasına ayrı ayrı sınıflandırıcı çağrısı eklemek üç kopya ve gelecekte eklenecek dördüncü bir hata yolunun bu adımı unutması riskini taşırdı. Yan etki: `ErrorClassifierHotPathTests`'in doğruladığı "başarılı çalıştırmada sınıflandırıcı hiç çağrılmaz" garantisi tek bir `if` ile sağlanır.

### K-123

**K-123 — Kodda tanımlı workflow'lar agent'ları `GetWorkflowAgent` ile bağlar**

🚨 Ölçüldü ve düzeltildi: örnek uygulama agent'ları `IAgentCatalog.ResolveAsync` ile **doğrudan** alıyordu; çıktı doğruydu ama `runs` ağacı **tek satır** döndü — katalogdan gelen agent MAF tarafından `options = null` ile çağrılır, ağaç bilgisini okuyamaz ve kendi kök satırını açar. 209 test yeşildi; yalnızca örnek uygulamayı gerçekten çalıştırmak ortaya çıkardı. `WorkflowAgentBinding.GetWorkflowAgent` public API'si eklendi ve `ChildAgentInvoker`'ı (Faz 12) yeniden kullanır — alt çalıştırma kuralları ikinci kez yazılmadı. Regresyon testi `Baglanan_agentler_workflow_agacina_girer`.

### K-447

**K-447 — `ModelFallbackUsed` olayı HEM `run_events`'e HEM kök span'in `agentprism.model.id` etiketine yazılır**

Gece nöbetçisi ya `run` kaydına ya izleme aracına bakar; ikisi de gerçek modeli göstermeli. `FallbackChatClient.RecordFallbackUsedAsync` olayı ambient `AgentPrismRunContext.Current.Writer` üzerinden yazar (aynı desen: `AgentRunScope.ToolUsage`, Faz 28). Span etiketi ise ayrı bir yoldan güncellenir: `RunRecordingAgent.CompleteAsync` `scope.FallbackAttribution.Current` doluysa `scope.Activity.SetTag(ModelId, ...)`'i BİRİNCİL modelin üzerine YENİDEN yazar — ilk atama `PrepareRun`'da zaten olmuştu (K-183 benzeri "atama yerinde güncellenir" deseni).

### K-132

**K-132 — Workflow grafı elle SVG ile çizilir; mermaid.js alınmadı** *(K-002'nin uygulaması)**

mermaid.js ~100 KB gzip eder; bundle bütçesi 250 KB ve tek bir ekran için bütçenin %40'ı harcanamaz. Elle çizim, tüm workflow ekran ailesiyle birlikte **+12,8 KB gzip** ile geldi (92,4 → 105,2 KB). Kaçış yolu korundu: "Copy Mermaid" düğmesi MAF'ın `ToMermaidString` çıktısını panoya kopyalar, karmaşık bir graf dış bir araçta düzgün yerleştirilir. Düzen mantığı `lib/workflow-graph.ts` içinde **saf fonksiyonlar** olarak durur ve Vitest ile test edilir; bileşen yalnızca boyar.

### K-026

**K-026 — Oturum yaşam döngüsü `AgentSessionManager` ile, MAF `AgentSessionStore` ile değil** *(kullanıcı kararı)**

MAF'ın `AgentSessionStore` sınıfı ön sürüm `Microsoft.Agents.AI.Hosting` paketindedir; K-008 gereği `AgentPrism.Core` ve `.PostgreSql` o pakete bağlanamaz. `AgentSessionManager` sağlayıcıdan bağımsızdır, bellek içi depoyla da çalışır ve Faz 4'teki `AgentSessionStore` uygulaması ona delege eder. Oturum kimliği `AgentSession.StateBag` içine damgalanır (`AgentSessionIdentity`), böylece `RunRecordingAgent` gerçek kimliği yazabilir ve kimlik oturumla birlikte kalıcılaşır.

### K-343

**K-343 — Anlamsal arama yalnız PostgreSQL'de uygulanır (Faz 51)** *(kullanıcı kararı, 2026-08-06)**

SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın `sqlite-vec` uzantısı bu fazda ÖLÇÜLMEDİ (SQLite için: `SQLitePCLRaw` yerel kütüphanemiz bunu taşımıyor). `IVectorSearchStore` Abstractions'a girdi, varsayılan uygulaması YOK (K4); SQL Server/SQLite tüketicisi kendi uygulamasını kaydedebilir. `document_embeddings` tablosu yalnız PostgreSQL migration setindedir (0024); diğer iki setin migration'ları bu fazda HİÇ değişmedi.

### K-453

**K-453 — Plandaki `ToolApprovalDecision` adı `ToolApprovalPolicyDecision` olarak gerçekleşti (Faz 63, plandan sapma)**

`AgentPrism.AspNetCore.Contracts.GovernanceContracts.cs` içinde Faz 6'dan beri `ToolApprovalDecision` adında BAŞKA bir public tip zaten vardı (arayüzden gelen onay/red kararının HTTP sözleşmesi — `RequestId`/`Approved`/`Reason`/`Remember`). Aynı isim aynı `AgentPrism` ad alanında ikinci kez tanımlanınca `CS0436` (tip çakışması) verdi; derleme hatası ölçülerek yakalandı. Yeni tip kod-tanımlı politika sonucunu (`Undecided`/`NotRequired`/`Required`) taşıdığı için `ToolApprovalPolicyDecision` adı hem çakışmayı çözdü hem anlamı netleştirdi.

### K-101

**K-101 — Varsayılan ağaç bütçesi: derinlik 3, 200.000 token, 25 alt çalıştırma** *(kullanıcı kararı)**

Faz 12 açık soruları; kullanıcı dokümanın üç önerisini de onayladı. Sınırsız bırakılan bir kurulumda ilk yanlış tanım faturayla öğrenilir — alt agent çağrısı maliyeti **çarpar**, her katman kendi model çağrılarını yapar. Değerler `AgentPrism:AgentGraph` bölümünden ayarlanır; `0` veya negatif değer ilgili sınırı kaldırır. Bütçe **yeni** alt çalıştırmaları engeller, süren bir çalıştırmayı kesmez: yarım kesilen bir alt çalıştırma modele eksik bağlam bırakır ve kök çalıştırmayı da bozardı.

### K-488

**K-488 — Yetki reddi İSTİSNA fırlatmaz, normal sonuç döner; kanca hata fırlatırsa fail-closed (Faz 69)**

Ret bir hata değil bir SINIRDIR: `run`'ı düşürmek modelin izinli bir alternatifi denemesini engeller. `AuthorizingAIFunction` reddedilen çağrıda `result.Reason`'ı DOĞRUDAN döner (K-232 — model'e giden metin), `ToolAuthorizationAccumulator` (call-id anahtarlı ambient yazma/okuma, `ToolUsageAccumulator`'ın Faz 28 deseninin aynısı) bunu `ToolInvocationRecord.AuthorizationDenied` ile işaretler. `IToolAuthorizationHandler.AuthorizeAsync` istisna fırlatırsa çağrı REDDEDİLİR (fail-open değil) — açık bir güvenlik kapısında hata "izin ver" anlamına gelemez.

### K-194

**K-194 — SQLite upsert deseni PostgreSQL ile BİREBİR aynıdır: tek ifadelik `INSERT ... ON CONFLICT ... RETURNING`**

SQL Server'ın iki-dallı `UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT` deseni (K-177) SQLite için GEREKMEDİ. Ölçüldü: SQLite 3.35+ (bağlı sürüm 3.49.1) `ON CONFLICT ... DO UPDATE ... RETURNING`'i PostgreSQL'inkiyle aynı semantikte destekler — `COALESCE(col, '')` gibi ifade tabanlı çakışma hedefleri dahil, ve NULL'ları PostgreSQL gibi birbirinden AYIRT EDER (SQL Server'ın tersi, K-184 burada geçerli değildir). Sonuç: K-188'in çözdüğü "çoklu sonuç kümesi" sınıfı hata SQLite'ta hiç OLUŞMAZ — tek ifade her zaman tek kume üretir.

### K-379

**K-379 — `IExperimentStore.SetCanaryPolicyAsync`, `SaveAsync`'in Draft-yalnız kısıtından MUAFTIR; kanarya kuralı deney Running iken de tanımlanabilir veya kaldırılabilir (Faz 56)**

Bir kanarya kuralı, deney ZATEN trafik alırken de eklenmek istenebilir (örn. bir A/B deneyi kanarya gözetimine SONRADAN alınır) — bunu `SaveAsync`'in Draft kısıtına bağlamak, kuralın yalnızca deney başlamadan ÖNCE tanımlanabilmesi gibi gereksiz bir sıralama dayatırdı. `SetCanaryPolicyAsync` `StartAsync`/`StopAsync` ile AYNI gerekçeyle (durum geçişi/yan alan güncellemesi, gövde değil) ayrı bir yazma yoludur ve durum denetimi yapmaz — yalnızca deneyin VAR olmasını ister.

### K-114

**K-114 — Kalıcı `PostgresAgentFileStore`: agent adı ambient kapsamdan okunur**

`Microsoft.Agents.AI.AgentFileStore` arayüzünde agent kimliği için bir parametre yoktur (tek bir `TryAddSingleton<AgentFileStore>` tüm agent'lar arasında paylaşılır, K-110). `PostgresAgentFileStore` bu yüzden `AgentPrismRunContext.Current?.AgentName`'i (Faz 12'nin ambient çalıştırma kapsamı) okur; kapsam yoksa açık bir `AgentPrismException` fırlatır. Kiracı `ITenantContext`'ten doğrudan enjekte edilir. Dizinler ayrı satır olarak tutulmaz — yol hiyerarşisi kayıtlı dosyaların yolundan türetilir (`ListChildrenAsync`); bu, `CreateDirectoryAsync`'i zararsız bir no-op yapar.

### K-513

**K-513 — Bir `<example>`'ın doğruluğunu yalnız DERLEME kanıtlar; metin denetimi yapısal olarak yetersizdir (Faz 74, F-121, denetim bulgusu 4)**

Denetim iki hatalı örnek buldu ve ikisi de K-512'nin metin kapısından geçmişti: `options.DefaultTimeout` (`CS1061`) ve `o.ExposedAgents = [...]` (`CS0200`). İkisi de GERÇEK API adlarıdır, yalnız yanlış tipin üzerinde kullanılmışlardır; hiçbir ad kümesi denetimi bunu göremez. Kapanış kanıtı: 40 `<example><code>` bloğu XML'den programatik olarak çıkarılıp paketlenmiş paketlerle derlendi (`Build succeeded`). Kalıcı derleme kapısı YAZILMADI — yeni yetenek, kusur değil (`ADAYLAR.md` F-125).

### K-248

**K-248 — `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular; ayrı bir adaptör sınıfı yok**

`MigrationRunner` zaten `SqlStoreContext.ProviderName`'e erişiyordu ve `GetSnapshotAsync` migration keşif/okuma mantığının çoğunu `ApplyAsync` ile paylaşıyor (aynı `MigrationDescriptor.Discover`, aynı `ReadAppliedAsync`). Ayrı bir sarmalayıcı tip, aynı veriyi taşıyan gereksiz bir dolaylama olurdu. `GetSnapshotAsync` migration UYGULAMAZ: bağlantı açılamazsa `CanConnect=false` döner (DbException yutulur), `__migrations` defteri henüz yoksa (ilk migration hiç uygulanmamış) bağlantı çalışıyor sayılır ve tüm migration'lar bekliyor kabul edilir.

### K-209

**K-209 — `AgentPrism` meta paketi Anthropic ve Google sağlayıcılarını İÇERMEZ**

K-185'in (SQL Server) doğrudan devamı. Meta paket bugün `AgentPrism.OpenAI` içerir. Anthropic ve Google'ı da eklemek, yalnız OpenAI kullanan her tüketiciye Anthropic SDK'sini ve `Google.Apis.Auth` zincirini (Newtonsoft.Json, System.Management, System.CodeDom — K-205) zorlardı. Bu sağlayıcıları isteyen tüketici paketi **açıkça** referans verir. Örnek uygulama üç sağlayıcıyı birlikte gösterebilmek için hepsine referans verir ve yapılandırmadan hangilerinin açılacağını seçer.

### K-185

**K-185 — `AgentPrism` meta paketi `AgentPrism.SqlServer`'ı İÇERMEZ**

Meta paket bugün `AgentPrism.PostgreSql` içerir. SQL Server'ı da eklemek, PostgreSQL kullanan her tüketiciye `Microsoft.Data.SqlClient` bağımlılığını (ve onun native SNI/kimlik doğrulama ağacını) zorlardı — K2 "tüketicinin bağımlılık grafiğini kirletme" kuralının doğrudan ihlali. SQL Server kullanan tüketici `AgentPrism.SqlServer` paketini **açıkça** referans verir. Örnek uygulama iki sağlayıcıyı da gösterebilmek için ikisine de referans verir ve yapılandırmadan yalnız birini seçer.

### K-046

**K-046 — Arayüz kabuğu bearer token katmanından muaftır** *(kullanıcı kararı)**

Tarayıcı bir `<script src>` veya `<link href>` isteğine `Authorization` başlığı **ekleyemez**. Kabuk token katmanıyla kilitlenseydi kullanıcı token'ı girebileceği ekranı hiçbir zaman göremezdi; token modu tarayıcıdan kullanılamaz hale gelirdi. Kabuk hiçbir veri taşımaz — yalnızca HTML, JS ve CSS. Loopback kısıtı ve authorization policy kabuğa da uygulanır; her veri ucu üç katmanın tamamında kalır. Uygulama: `AgentPrismEndpointFilter(options, requireBearerToken: false)` ile üçüncü bir uç grubu. `Token_gerektiginde_kabuk_acilir_ve_token_sorulur` testi bunu koruyor.

### K-040

**K-040 — Enum'lar JSON'da ad olarak yazılır**

`origin: 0`, `status: 1` gibi sayısal değerler kablo sözleşmesi olarak okunaksızdı ve enum sırası değişirse sessizce kırılırdı. `RunStatus`, `RunEventType` ve `AgentDefinitionOrigin` tip düzeyinde `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` taşır; böylece tüketicinin uygulama genelindeki JSON ayarlarına dokunulmadan her yerde aynı biçim elde edilir. Güvenli olduğu doğrulandı: hiçbir enum JSON olarak **kalıcı değildir** — `RunStatus`/`RunEventType` veritabanında `smallint`, `AgentDefinitionOrigin` ise `AgentDefinitionPayload` içinde saklanmaz, okumada `Database` olarak yeniden kurulur.

### K-273

**K-273 — SSE/ikili yanıtlar `Produces<T>` ile tiple bildirilir; `responseType: null` içerik tipini tamamen düşürür**

Ölçüldü (Faz 40, izole repro): `.Produces(200, contentType: "text/event-stream")` (responseType verilmeden) belgede `"200": {"description": "OK"}` üretir — `content` alanı **hiç oluşmaz**, verilen `contentType` sessizce atılır. `.Produces<string>(200, contentType: "text/event-stream")` veya `.Produces<Stream>(200, contentType: "application/octet-stream")` (ikili gövde için, `format: binary` şeması üretir) content'i doğru üretir. SSE çerçeveleri (`event:`/`data:` metni) için `string`, ham ikili indirme için `Stream` seçildi.

### K-290

**K-290 — `idempotency_keys` için ayrı bir `Retention: TimeSpan` alanı yerine standart `RetentionTargets`/`AgentPrismRetentionOptions` üçlüsü kullanıldı**

Planın taslak `AgentPrismIdempotencyOptions.Retention` alanı, Faz 25'in devir notunun zorunlu kıldığı üçlüye (`RetentionTargets` + `RetentionTargetRegistry` + `AgentPrismRetentionOptions`) PARALEL ikinci bir saklama mekanizması olurdu — aynı kavram için iki farklı yapılandırma yüzeyi. `idempotency_keys` standart hedef listesine `MaxAgeDays = 1` varsayılanıyla eklendi; TTL uygulaması `RetentionExecutor`'ın var olan toplu silme yoluyla yürür, yeni bir arka plan mekanizması icat edilmedi.

### K-063

**K-063 — `run_events` partition'ı açılmadı (kapandı → K-199)**

Faz 6 planı partition'ı bir risk önlemi olarak listeliyordu. Partition'a geçmek birincil anahtara `created_at` eklemeyi ve tabloyu yeniden kurmayı gerektirir; ölçüm olmadan yapılan böyle bir değişiklik, çözdüğünden fazla risk taşır. Faz 7'nin yük testi (saniyede 100 çalıştırma × ~50 olay) bu kararın tetikleyicisidir: darboğaz ölçülürse partition o kanıtla açılır. **(güncelleme: 2026-08-02)** Tetikleyici artık [Faz 25](arsiv/fazlar/25-VERI-SAKLAMA-VE-ARSIVLEME.md)'tir; o faz ölçümü yapar ve kararı **kapatır**. **(kapanış: 2026-08-05)** Faz 25 ölçtü: bkz. K-199.

### K-313

**K-313 — Konusma dallandırma yalnız SQL sağlayıcısı açıkken çalışır; bellek içi kurulumda uç `501` döner (kullanıcı kararı)**

Ölçüldü: SQL kayıtlı değilken sohbet geçmişi MAF'ın `InMemoryChatHistoryProvider` nesnesinde, oturum durumunun OPAK bloğunda yaşar (K3: MAF sarmalanmaz) ve belirli bir sıra numarasına kadar kopyalanamaz. Sessizce tamamını kopyalamak istenen dalı üretmez; `IConversationBranchStore` bu yüzden yalnız SQL sağlayıcılarınca kaydedilir ve kayıtsızken uç açık gerekçeli bir `501` döner (Faz 46'nın `Prefer: respond-async` kapalıyken verdiği 501 ile aynı desen).

### K-065

**K-065 — Ses: hedef gerçek zamanlı konuşma katmanı** *(kullanıcı kararı)**

F-13 iki ayrı işti: (1) tool olarak TTS, (2) gerçek zamanlı konuşma katmanı. Kullanıcı **konuşma katmanını** seçti. Yine de iki faza bölündü ([28](arsiv/fazlar/28-SES-TOOLLARI.md) ve [29](arsiv/fazlar/29-KONUSMA-KATMANI.md)): sağlayıcı soyutlaması, kimlik doğrulama, ses depolama ve maliyet ölçümü konuşma katmanından bağımsızdır ve önce çözülmelidir. Faz 29 yalnız gerçek zamanlılık sorununa odaklanır. Konuşma katmanı **barındırma modelini değiştirir** (uzun ömürlü WebSocket, yapışkan oturum); bu yüzden yetenek isteğe bağlıdır ve çağrılmazsa hiçbir uç açılmaz.

### K-293

**K-293 — `RunError` sınıf/parmak izini doğrudan taşır; ayrı bir arama tablosu açılmadı**

`error_class`/`error_fingerprint` K-151'in deseniyle (türetilmiş bilgi ham bilginin yanına) `runs` tablosuna, `RunError` record'una `Class`/`Fingerprint` alanı olarak eklendi — `RunCost`/`RunTreeCost` ayrımıyla aynı biçim. Alternatif (sınıflandırmayı yalnız okuma anında hesaplamak) her `GetRunAsync`/`QueryRunsAsync` çağrısında sınıflandırıcıyı tekrar çalıştırırdı; bu sıcak okuma yolunu yavaşlatır ve geçmiş bir hatanın sınıfı sınıflandırıcı değiştikçe kayar — "bu çalıştırma o gün ne olarak sınıflandırılmıştı" sorusu cevaplanamaz hale gelirdi.

### K-161

**K-161 — Webhook yükü yalnızca ÖZET taşır; mesaj içeriği hiçbir zaman girmez** *(kullanıcı kararı, doc açık soru 3)**

Gerekçe iki katmanlıdır: (1) yük dış bir sisteme gider ve hassas veri taşıyamaz; (2) aynı metin `webhook_deliveries.payload` sütununda da saklanır — içerik taşısaydı veritabanı yedeği de hassas veri taşırdı. İçerik isteyen alıcı `GET {prefix}/api/runs/{id}` çağırır ve kendi yetkisiyle okur. Abonelik başına `include_payload` seçeneği reddedildi: her abonelik bir sızıntı kararı hâline gelirdi ve sır süzgecinin webhook yolunda da çalışması gerekirdi. Regresyon testi: `WebhookPublisherTests.Yuk_mesaj_icerigi_tasimaz`.

### K-143

**K-143 — Eval vaka düzenleyici arayüzde JSON değil, tekrarlanan alan formu (query/expectedOutput/expectedTools/context)**

Doc'un önerdiği "vaka düzenleyici" ifadesi hem JSON metin kutusu hem yapılandırılmış form ile karşılanabilirdi; `skills.tsx`'teki `ResourceEditor`/`ScriptEditor` deseniyle tutarlılık için ikincisi seçildi — kullanıcı tırnak/virgül kaçışıyla uğraşmaz. `checks` alanı ise JSON metin kutusu olarak **kaldı**: az sayıda, nadiren düzenlenen ve doğası gereği iç içe bir yapıdır; ayrı bir form her denetim türü için özel alan seti gerektirirdi.

### K-451

**K-451 — Argüman-koşulu onay kuralı `POST /api/approvals/rules` YENİ bir uçtur; Faz 63 planı bunu yanlışlıkla "mevcut uç" sanıyordu (Faz 63, plandan sapma)**

Plan kanıt tablosu `GET`/`DELETE /api/approvals/rules`'un var olduğunu doğru tespit etmişti ama `POST`'un da var olduğunu ÖLÇMEDEN varsaydı. Ölçüldü: `grep -rn "approvals/rules" src/AgentPrism.AspNetCore/` yalnız `MapGet` ve `MapDelete` buluyordu — kural yazmanın tek yolu `ToolApprovalResolver.RememberAsync`'in (agent onay akışından tetiklenen, `ArgumentsHash` tabanlı) iç yoluydu. Admin'in arayüzden doğrudan koşullu bir kural yazabilmesi için `POST` baştan yazıldı.

### K-097

**K-097 — Alt agent yolu `AIContextProviders` üzerinden kurulur; `BackgroundAgentsProvider` için `MAAI001` bastırılır**

Reflection ile doğrulandı: `BackgroundAgentsProvider` bir `AIContextProvider`'dır, harness gerektirmez. Bu, K-053'te belgelenen harness kusurunun (tool çağrısı bağlanmıyor) bu özelliği de vurmasını engeller — düz `ChatClientAgent` birinci sınıf yoldur. Tip "evaluation purposes only" işaretli olduğu için `MAAI001` bastırılır; bastırma K-020 ile aynı gerekçeye dayanır ve tek bir metotta (`AgentDefinitionCompiler.CreateBackgroundAgentsProvider`) toplanmıştır.

### K-078

**K-078 — `/api/meta` yanıtına rol bilgisi (`roles`) eklendi**

Faz 9'un arayüz gereksinimi ("Kullanıcının rolü `/api/meta` yanıtından okunur") böyle karşılandı. `AllowAnonymous()` kimlik doğrulama boru hattını devre dışı bırakmaz, yalnızca yetkilendirme şartını kaldırır; bu yüzden `HttpContext.User` istek gerçekten kimlik doğrulamasından geçtiyse doludur ve rol alanları gerçek yetkiyi yansıtır. Her rol alanı `IAuthorizationService.AuthorizeAsync` ile hesaplanır; bir policy kayıtlı değilse (`null`) sonuç `true`'dur — rol kısıtı yoktur. Uç hâlâ hiçbir sır, kiracı verisi veya agent adı taşımaz; yalnızca üç `bool` eklendi.

### K-201

**K-201 — `MaxRows` var ama uygulanmıyor (ertelendi → kapandı K-258)**

Faz 25'in DoD'si yalnız yaş bazlı (`MaxAgeDays`) silmeyi zorunlu kılıyordu; hacim bazlı kırpma (bir hedefi en fazla N satıra indirmek) ayrı bir sorgu deseni (satır sayısına göre "en eski N hariç" seçimi) ve ayrı bir ölçüm ister. K-063'ün kendi gerekçesiyle aynı desen izlendi: ölçüm olmadan eklenen bir mekanizma, çözdüğünden fazla risk taşıyabilir. Alan kasıtlı olarak depoda/API'de tutuldu (K1: sıfır sürpriz — sonradan eklenecek bir sütun geçmiş politikaları bozmasın diye). **(kapanış: 2026-08-06)** Faz 36 `MaxRows`'u uyguladı: bkz. K-258.

### K-291

**K-291 — Idempotency desteği varsayılan AÇIKTIR (`Enabled = true`); K1'in "varsayılan kapalı" kuralının bilinçli bir yorumu**

Hız sınırı (K-165) ve kota gibi özelliklerin aksine bu özellik yalnız istemci `Idempotency-Key` başlığı GÖNDERDİĞİNDE devreye girer — başlıksız istek hiçbir ek sorgu/maliyet ödemez, davranışı değişmez. Kapalı gelseydi, başlığı gönderen bir istemci korunduğunu SANIP korunmazdı; bu, K1'in önlemeye çalıştığı "sessiz sürpriz"in ta kendisidir. `Enabled = false` iken başlık taşıyan istek `501 Not Implemented` alır (K-034/K-208'in "sessizce yok sayma" yasağıyla aynı gerekçe) — sessizce yok sayılmaz.

### K-099

**K-099 — Trace tamponunun sahibi yalnız kök çalıştırmadır**

🚨 Gerçek bir çağrıda ölçüldü: ağaçtaki her çalıştırma aynı W3C trace kimliğini paylaşır (alt span'ler kök span'in altına yerleşir) ve `RunTraceCollector` tamponu o kimlikle anahtarlar. Alt çalıştırma da tamponu kapattığında — ki **önce o biter** — tüm ağacın span'leri alt çalıştırmaya bağlandı ve kökün `/trace` ucu `404` döndü. `RunRecordingAgent` artık toplayıcıyı yalnız `Depth == 0` iken çağırır. Sonuç: tek waterfall, kök çalıştırmada, alt çalıştırmanın span'leri iç içe. Arayüz alt çalıştırmanın trace panelinde köke bağlantı gösterir ve isteği hiç yapmaz.

### K-045

**K-045 — Arayüz yönlendirmesi elle yazıldı, TanStack Router kullanılmadı** *(kullanıcı kararı)**

Uygulamada yedi ekran ve iki dinamik parametre var. `History` API üzerine ~110 satır yeterli oldu ve taban yol (base path) çalışma anından **doğal olarak** geliyor — her yönlendirme kütüphanesine bu ayrıca bildirilmek zorundadır ve AgentPrism'in prefix'i yalnızca çalışma anında bilinir. Ölçüldü: bundle ~32 KB gzip küçüldü (88,1 KB / 250 KB bütçe). Maliyet: rota tipleri elle tanımlanır; `matchRoute` ve `toRelativePath` birim testleriyle korunur.

### K-371

**K-371 — `IPendingApprovalStore.ExpireAsync`, planın taslak imzası `ValueTask<int>` yerine `ValueTask<IReadOnlyList<PendingApproval>>` döner (Faz 55, plandan sapma)**

`docs/arsiv/fazlar/55-ASENKRON-ONAY-KUTUSU.md`'nin ilk taslağı yalnız süresi dolan kayıt SAYISINI döndürüyordu. Ama `ApprovalExpirationService`'in süresi dolan her onay için karşılık gelen çalıştırma satırını da kapatması (`RunStatus.AwaitingApproval` → `Failed`, zaman aşımı) gerekiyordu — bunun için `RunId` listesine ihtiyaç vardı, salt bir sayıya değil. İmza süresi dolan `PendingApproval` kayıtlarının TAMAMINI döndürecek şekilde genişletildi.

### K-041

**K-041 — Çalıştırma özeti depoda hesaplanır**

`/api/stats` için `QueryRunsAsync` sonuçlarını bellekte toplamak yalnız **sayfalanmış** bir alt kümeyi kapsar ve yanlış sonuç verirdi. `IRunStore.GetStatisticsAsync` eklendi; PostgreSQL'de tek gidiş dönüşte iki sonuç kümesi (`COUNT(*) FILTER (...)` + `GROUP BY agent_name`), bellek içi depoda tek geçiş. Durum değerleri SQL'e sabit sayı olarak gömülmez, `RunStatus` enum'undan parametre olarak gelir. `RunStoreContract` içindeki 8 test iki depoda da koşar. Hata oranının paydası **sonuçlanmış** çalıştırmalardır; devam eden bir çalıştırma oranı yapay olarak düşürürdü.

### K-025

**K-025 — `UsePostgreSql()` `Replace` kullanır, `TryAdd` değil**

`AddAgentPrism()` bellek içi depoları `TryAddSingleton` ile kaydeder ve zincirde **önce** çalışır; `UsePostgreSql()` içinde `TryAdd` yazmak sessizce hiçbir şey yapmazdı — uygulama hata vermeden bellek içi depoyla çalışır ve tüm veri süreçle birlikte kaybolurdu. Üzerine yazma burada doğrudur çünkü `UsePostgreSql()` tüketicinin **açık** tercihidir. K4 kuralı ("TryAdd ile kaydet") AgentPrism'in *varsayılanları* içindir. `ServiceRegistrationTests` üç depoyu da denetler. Sağlayıcı ekleyen `UseOpenAI()` bunun tersidir: `AddModelProvider` yeterlidir.

### K-104

**K-104 — Sıkıştırma bağımlılığı için yeni paket alınmadı**

Ölçüldü: `dotnet list package --include-transitive` `Microsoft.ML.Tokenizers` 2.0.0'ın `Microsoft.Agents.AI.Abstractions`'ın geçişli bağımlılığı olduğunu gösterdi. Ek doğrulama: gerçek bir `RunAsync` çağrısı `Microsoft.ML.Tokenizers.Data.*` gibi bir veri paketi olmadan hatasız çalıştı — `CompactionProvider` tokenizer'ı içeride kendisi çözüyor, dışarıdan parametre almıyor. Sıkıştırma `AgentPrism.Core`'da kaldı; ayrı bir `AgentPrism.Compaction` paketi açılmadı.

### K-485

**K-485 — `/api/stats`'a `groupBy` EKLENMEDİ; `byUser`/`byLabel` her zaman döner (Faz 68, plandan sapma)**

Plan `?groupBy=user\|label` öngörüyordu. `ByAgent`/`ByModel`/`ByVersion`/`ByErrorClass`'ın hiçbiri koşullu değil ve `SqlRunStore.GetStatisticsAsync` sonuç kümelerini KONUMA göre okur — kümeleri koşullu yapmak okuyucuyu kırılgan hâle getirir ve yeni kırılımı var olan dördünden farklı davranan bir istisna yapardı. Bunun yerine `userId`/`label` ÖZETİN TAMAMINI daraltır. 🚨 `byLabel` satırları `totalRuns`'a TOPLANMAZ: üç etiket taşıyan bir `run` üç satıra girer — etiket kümesi çalıştırmaları BÖLÜMLEMEZ.

### K-244

**K-244 — `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok**

Faz 32 açık soru 4'ün önerisi (A) benimsendi: defter yalnız bellekte bir `ConcurrentDictionary` tutar, hiçbir isteği reddetmez, hiçbir yan etki üretmez — K-165'in "yeni davranış varsayılan kapalı gelir" kuralı gözlemlenebilir bir davranış değişikliğini hedefler, bu defter bir davranış değiştirmez. `AddAgentPrism()` içinde koşulsuz `TryAddSingleton<IRunCancellationRegistry, RunCancellationRegistry>()`.

### K-456

**K-456 — Denetim izi (`audit_log`) veri konusu silmesinin kapsamı dışındadır; silme yalnız İÇERİK verisinde (oturum, çalıştırma, konuşma, ek, puan, ses, çalıştırma girdisi) uygulanır (Faz 64, kullanıcı kararı)**

`audit_log` "kim ne yaptı" bilgisidir, kişinin içeriği değil — silinirse hesap verebilirlik kaybolur. Kanıt: çağıran yerler (`ApiKeyEndpoints`, `QuotaEndpoints` vb.) yönetim eylemleridir, konuşma içeriği hiç yazılmaz (`AuditContentPolicyTests` bunu kapatır). Kural bir testle sabitlendi: yeni bir çağrı yeri `AuditContentPolicyTests.AllowedCallSites`'a bilerek eklenmedikçe derleme kırmızı kalır.

### K-272

**K-272 — Uç etiketleri TEK `.WithTags("AgentPrism", "<Alan>")` çağrısıyla verilir**

Ölçüldü (Faz 40): `RouteHandlerBuilder` üzerinde art arda çağrılan iki `.WithTags(...)` **birikmez** — ikincisi birincinin ürettiği `ITagsMetadata`'yı tamamen ezer (`OpenApiDocumentTests.Belge_ozet_ve_etiket_ustverisini_tasir` bunu `tags[0]` "AgentPrism" yerine "Meta" dönerek yakaladı). 121 uçtan hiçbiri grup düzeyinde kalan `.WithTags("AgentPrism")`'i uç düzeyinde ikinci bir çağrıyla zenginleştiremez; her uç kendi zincirinde İKİ etiketi TEK çağrıda vermelidir.

### K-034

**K-034 — `ModelBinding.ReasoningEffort` derleyicide bağlanır, geçersiz değer reddedilir** *(kullanıcı kararı)**

Alan Faz 1'den beri hiçbir yerde okunmuyordu — dokümanı "destekleyen modellerde kullanılır" diyordu ama derleyici yok sayıyordu. `AgentDefinitionCompiler.BuildChatOptions` artık `ChatOptions.Reasoning`'e çeviriyor. Geçersiz değer sessizce yok sayılmaz: bu ayar hem maliyeti hem gecikmeyi değiştirir; yanlış yazılmış bir değer fark edilmeden çalışırsa kullanıcı beklediği davranışı alamaz ve sebebini göremez. Hata mesajı geçerli değerleri (`None`, `Low`, `Medium`, `High`, `ExtraHigh`) listeler.

### K-197

**K-197 — `SQLitePCLRaw.*` paketleri 2.1.12'ye sabitlendi (K-007 deseni)**

`Microsoft.Data.Sqlite` 10.0.10'un çektiği `SQLitePCLRaw.lib.e_sqlite3` 2.1.11'de bilinen yüksek önem dereceli bir açık var (NU1903, GHSA-2m69-gcr7-jv3q). Geçişli sabitleme kapalı olduğu için (K-007) `SQLitePCLRaw.bundle_e_sqlite3`/`.core`/`.lib.e_sqlite3`/`.provider.e_sqlite3` dördü birden `Directory.Packages.props`'ta 2.1.12'ye sabitlenip `AgentPrism.Sqlite.csproj`'a `SQLitePCLRaw.bundle_e_sqlite3` için açık bir `PackageReference` eklendi.

### K-149

**K-149 — `experiments.variants` tek bir `jsonb` sütununda saklanır; ayrı bir `experiment_variants` tablosu açılmadı**

Bir deneyin kolları her zaman **bütün olarak** okunur ve yazılır, hiçbir zaman tek tek güncellenmez — `EvalSuite.Checks`'in aynı gerekçesi (K-045'in devamı). Ayrı bir tablo gereksiz bir JOIN ekleyip hiçbir sorguyu hızlandırmazdı. "Aynı agent için tek Running deney" kuralı ise **veritabanında** (`experiments_running_agent_uq` kısmi benzersiz indeksi) zorlanır — uygulama katmanındaki `InMemoryExperimentStore` kontrolü ikinci savunma hattıdır, PostgreSQL'de asıl garantiyi indeks verir.

### K-029

**K-029 — Şema adı SQL metnine gömülür, katı doğrulamadan sonra**

PostgreSQL tanımlayıcıları parametre olarak gönderilemez. `SchemaName` yapılandırmadan gelir, bu yüzden `SqlIdentifier.RequireSchemaName` yalnız küçük harf, rakam ve alt çizgi kabul eder (en çok 63 karakter, `public` yasak). Böylece enjeksiyon yüzeyi kapanır ve PostgreSQL'in tırnaksız tanımlayıcıları küçük harfe çevirmesinden doğan sürprizler oluşmaz. Gömülü `.sql` dosyalarında `{schema}` yer tutucusu kullanılır; checksum yer değiştirmeden **önce** hesaplanır, böylece `SchemaName` değişikliği uygulanmış migration'ları geçersiz kılmaz.

### K-021

**K-021 — Yapılandırma elle bağlanır, `Bind()` kullanılmaz**

`optionsBuilder.Bind()` yansıma kullanır ve `IL2026` + `IL3050` üretir. Önce `EnableConfigurationBindingGenerator=true` denendi; `dotnet build` temiz geçti ama **`dotnet format` tanıları yeniden gösterdi** — kaynak üreteci format'ın analyzer geçişinde devreye girmiyor. Elle bağlama (`AgentPrismServiceCollectionExtensions.Bind`) her iki kapıda da temiz ve bir paket bağımlılığını (`Options.ConfigurationExtensions`) kaldırdı. Maliyet: yeni ayar eklerken bağlama metoduna da eklemek gerekir.

### K-087

**K-087 — Hem dosya tabanlı hem veritabanında saklanan script'ler desteklenir** *(kullanıcı kararı)**

Faz dokümanı yalnız dosya tabanlı kaynağı (A) öneriyordu; kullanıcı "A + B" seçti. Dosya kaynağı MAF'ın `AgentFileSkillsSource` üzerinden gelir ve kök dizinler **yalnız kodda** (`UseSkillScripts`) verilir — arayüzden kök eklenemez, aksi hâlde yönetici arayüzü keyfî dosya sistemi okuması yapabilirdi. Saklanan script'ler ayrıca `AllowStoredScripts` ile kapılıdır. `AggregatingAgentSkillsSource` içinde veritabanı kaynağı **önce** gelir; `DeduplicatingAgentSkillsSource` ad çakışmasında ilk kaydı tutar.

### K-363

**K-363 — `RunErrorClass.Infrastructure` yeni değer olarak eklendi (Faz 54, Açık Soru 3, seçenek A'nın düzeltilmiş hâli)**

Plan "Infrastructure" sınıfının zaten var olduğunu varsayıyordu; `RunErrorClass` enum'ı okununca böyle bir değer YOKTU. Oksuz çalıştırma "hiçbir yanıt alınamadı" durumudur ve mevcut hiçbir sınıfa (ProviderError, Timeout, ToolError…) uymaz; `Unknown`'a düşürmek taksonominin amacını (bilinen arızaları isimlendirmek) boşa çıkarırdı. Değer listenin **sonuna** eklendi (K-014: sayısal değerler yeniden numaralanmaz); `docs/openapi/agentprism.json` bunu yansıtacak şekilde yenilendi.

### K-231

**K-231 — Varsayılan dil tarayıcıdan gelir** *(kullanıcı kararı)**

Saklanmış tercih yokken `navigator.languages` içindeki ilk desteklenen birincil etiket seçilir (`tr-TR` → `tr`). Değerlendirilen alternatif "her zaman İngilizce başla": ekran görüntüsü ve destek tutarlılığı yüksek olurdu ama Türkçe bir tarayıcıda konsolu açan kullanıcı Türkçe desteği olduğunu hiç fark etmezdi. 🚨 Bunun bir test bedeli var: metin üzerine iddia kuran **her E2E testi dili sabitlemek zorundadır**, aksi halde sonuç testi çalıştıran makinenin sistem diline bağlanır. `Session.OpenAsync` varsayılanı bu yüzden `en-US`'tir.

### K-112

**K-112 — `attachments.session_id` yabancı anahtar DEĞİLDİR**

🚨 Denendi ve geri alındı: FK ile kuruldu, ancak gerçek akışta (istemci önce dosyayı yükler, oturum satırı ancak ilk çalıştırmada doğar) yükleme anında `INSERT`, "violates foreign key constraint" ile başarısız oldu — ölçüldü, `AttachmentStoreContract` testleriyle yakalandı. Bir oturum silindiğinde eklerin gitmesi tamamen uygulama katmanında yapılır: `SessionEndpoints.DeleteSessionAsync` → `IAttachmentStore.DeleteBySessionAsync`. Bellek ici depoda zaten tek yol buydu; PostgreSQL'de de aynı yol kullanılarak iki farklı davranış önlendi.

### K-489

**K-489 — `RunErrorClass.ToolTimeout` yeni değer (13); `AgentPrismToolTimeoutException` stabil kimliği `tool_timeout` (Faz 69)**

Zaman aşımı sarmalayıcının kendi `Task.WhenAny` yarışı `OperationCanceledException`'a benzer bir görünüm üretebilirdi; `Canceled` sınıfıyla birleştirmek her tool zaman aşımını "kullanıcı iptal etti" arkasına gizlerdi. Ayrıca çalıştırma-düzeyi `Timeout` (bütün `run` süresini aştı) ile karıştırılmaması için AYRI bir değer gerekti — ikisi farklı şeyleri ölçer. `DefaultRunErrorClassifier.StableIdentities`'e diğer `AgentPrismException` türevleriyle aynı desende eklendi.

