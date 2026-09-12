# Hafiza Arsivi — Sicak Yoldan Cikarilan Uzun Anlatilar

> `docs/hafiza/*.md` dosyalari her oturumda degil, **o alana dokunurken** okunur;
> yine de bir bayt butcesi tasirlar (`scripts/dokuman-bakim.py`). Bir not bayat
> degil ama **uzunsa** — kurali degil, kuralin nasil kesfedildigini anlatiyorsa —
> tanimlayici anlati buraya tasinir, kural alan dosyasinda kalir.
>
> Bu dosya **hicbir zaman bastan sona okunmaz.** Yalniz "bu kural nasil
> bulundu?" sorusunda grep'lenir: `grep -n "NU5017" docs/arsiv/HAFIZA-GECMISI.md`.
>
> Ilk dolum: 2026-08-15, Faz 58.0 (butce rahatlatmasi).

---

## `docs/hafiza/aspnetcore-di.md`'den

### `OpenAIProviderOptions.Endpoint` ve F-03'un gercek kapsami

`OpenAIProviderOptions.Endpoint` zaten vardi ve yapilandirmadan baglaniyordu.
F-03'un gercek isi taban adres degil, **adlandirilmis coklu saglayici**
destegiydi (`alreadyRegistered` bayragi ve sabit `OpenAIProviderNames` engel) —
aday tanimlanirken kapsam bu ayrimla netlestirildi (bayat, tarihsiz not,
2026-08-19'da butce icin buraya tasindi).

### `BackgroundService.StartAsync` migration yarisi (2026-08-07, Faz 42) — Faz 77 butce rahatlatmasi

- **🚨 `BackgroundService.StartAsync` HOST'u BLOKLAMAZ; kayit SIRASI oncelikli olan bir `IHostedService`'in migration'dan ONCE calismasina izin verir** (2026-08-07, Faz 42, gercek `samples/Tracon.Api` kosumunda olculdu): Genel Host, `IHostedService.StartAsync`'i KAYIT SIRASINA gore art arda cagirir; `MigrationHostedService.StartAsync` migration'lari TAM olarak bekler (`await _runner.ApplyAsync(...)`) ama `BackgroundService.StartAsync` (taban sinif) `ExecuteAsync`'i baslatir ve HEMEN doner — sonraki hosted service'i beklemez. `.UseMcp()` `.UseSqlite()`'tan ONCE cagrilirsa `McpDiscoveryService.ExecuteAsync` (dolayisiyla `SingletonGuard`'in ilk `TryAcquireAsync`'i) migration'lar bitmeden calisabilir ve "no such table" ile basarisiz olur (guard hatayi yutar, loglar, bir sonraki yenileme turunda kendiliginden duzelir). Yeni bir kusur degildir — `McpDiscoveryService` zaten ilk turunda `mcp_servers`'i okuyordu ve ayni yarisa acikti. Bir `BackgroundService` SQL-destekli bir depoya ilk turunda dokunacaksa bu yarisi HESABA KAT.

### `RequireRole` sessiz gecersizligi (2026-08-18, F-104/K-431) — Faz 77 butce rahatlatmasi

- **🚨 "Kayıtlı değilse hiçbir şey yapmaz" tasarımı örnek uygulamada GÖRÜNMEZ bir delik üretir** (2026-08-18, F-104/K-431): `RequireRole(policyName)` politika adı `null` çözünce hiçbir yetkilendirme eklemez — yükselten kurulumu kırmamak için bilinçlidir (K1), ama `samples/Tracon.Api` üç politikayı hiç kaydetmediği için Skill/Approval/Retention/Quota/Workflow uçlarındaki HER `RequireRole` çağrısı referans dağıtımda **sessizce etkisizdi**; fonksiyonel testler izole host'ta politikayı kendileri kaydettiği için yeşildi. Ders: sessiz geri düşüş (`fallback`) tasarlarken **referans dağıtımın onu nasıl göstereceğini** de tasarla. Kalıcı kapı: bayrak açıkken `TraconEndpointOptions.RequireRolePolicies = true` yazılır ve kayıt silinirse uygulama **başlamaz**. Gösterim şeması `X-Tracon-Demo-Role` başlığını okur, hiçbir doğrulama yapmaz ve varsayılan **kapalıdır**.

### `AddTraconHealthChecks` on-kontrol tuzagi (2026-08-06, Faz 33, K-251) — Faz 77 butce rahatlatmasi

- **🚨 `IServiceCollection` KURULUM ANINDA sira-bagimsizdir; `MapTracon`in `app.Build()` SONRASI kontrol deseni burada TEKRARLANAMAZ** (2026-08-06, Faz 33, K-251): `MapTracon` `IAgentCatalog` kayitli mi diye `endpoints.ServiceProvider` (TAMAMLANMIS bir kap) uzerinden bakar — bu guvenlidir cunku o noktada tum `Add*()` cagrilari bitmistir. `AddTraconHealthChecks()` gibi bir `IServiceCollection` UZANTISI ayni kontrolu (`services.Any(d => d.ServiceType == typeof(IAgentCatalog))`) yaparsa YANLIS SONUC uretebilir: tuketici `services.AddHealthChecks().AddTraconHealthChecks()`'i `services.AddTracon()`'DEN ONCE cagirabilir ve o an henuz kayitli olmayan bir servis icin gecerli bir kurulumu hatali reddeder. Cozum: kurulum anindaki uzantilar boyle bir on-kontrol YAPMAZ; DI, servis ilk cozulmeye calisildiginda (`/health` ilk yoklandiginda) zaten acik bir hata verir.

### Minimal API govde cikarimi — iki vaka (Faz 9 ve Faz 28) — Faz 77 butce rahatlatmasi

- **🚨 Minimal API'de nullable/opsiyonel bir servis parametresi `[FromServices]` olmadan "Body" sayılabilir ve TÜM route'ları kırar** (2026-08-02, Faz 9): Ölçüldü — `IAuthorizationService? authorizationService` parametresi (test barındırıcısında `AddAuthorization()` çağrılmadığı için) minimal API'nin servis çıkarımını geçemedi ve `InvalidOperationException: Body was inferred but the method does not allow inferred body parameters` fırlattı. Hata tek bir uçta oluşsa da `RouteEndpointDataSource` tüm uçları TEK bir DFA matcher'da birleştirdiği için **119 testin 112'si** aynı anda kırıldı — semptom hedeften kopuk görünüyordu. Çözüm: `[FromServices]` özniteliği eklemek servis çözümlemesini kayıt durumundan bağımsız zorunlu kılar. Kayıtlı olmayabilecek her opsiyonel servis parametresinde bu öznitelik kullanılmalıdır.
- **🚨 Minimal API'de KAYITLI OLMAYAN bir servis parametresi GOVDE sanilir** (2026-08-05, Faz 28): istege bagli bir bagimliligi (`ISpeechSynthesizer? synthesizer`) DI'dan almak icin nullable yazmak YETMEZ. ASP.NET Core kaynak cikarimini `IServiceProviderIsService`'e sorar; tip kayitli degilse parametreyi govdeden baglamaya calisir ve uc kurulumu `InvalidOperationException: Body was inferred but the method does not allow inferred body parameters` ile patlar. Etki tek uçla sinirli DEGILDIR — `MapTracon` cagrisinin tamami coker ve **her** fonksiyonel test duser (olculdu: 252/261). Cozum: `[FromServices]` ile acikca isaretleyin. Ozellik istege bagli bir paketle geliyorsa (ornek: `Tracon.Voice` kurulu degilse) bu durum NORMALDIR ve uc `501` donmelidir.

### `WorkflowJobHandler` optional kurucu bagimliligi (2026-08-03, Faz 17) — Faz 77 butce rahatlatmasi

- **🚨 Yerlesik DI kabi, C# varsayilan parametre degeri olsa BILE optional constructor bagimliligini bazen zorunlu sayar** (2026-08-03, Faz 17): `WorkflowJobHandler(IWorkflowRunner? runner, ILogger? logger = null)` — yalnizca `logger`'da `= null` vardi, `runner`'da yoktu; `TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WorkflowJobHandler>())` (kurucu otomatik cozumleme) `IWorkflowRunner` kayitli degilken `InvalidOperationException` firlatti. `runner`'a da `= null` eklemek yetmedi (aciklamasi zaten satir ~95'te); cozum acik fabrika: `ServiceDescriptor.Singleton<IJobHandler, WorkflowJobHandler>(provider => new WorkflowJobHandler(provider.GetService<IWorkflowRunner>(), ...))`.

### Bearer token muaf ucuncu uc grubu deseni (2026-08-04, Faz 22) — Faz 77 butce rahatlatmasi

- **Bir HTTP uc grubunu bearer token denetiminden muaf tutmak icin `TraconEndpointFilter(options, requireBearerToken: false)` ile AYRI bir `MapGroup` kur** (2026-08-04, Faz 22): OAuth `/oauth/callback` ucu, saglayicinin yonlendirdigi tarayicidan gelir ve bizim bearer token'imizi tasiyamaz — `/api/meta` gibi tamamen acik olamaz (loopback + policy hala gecerli olmali) ama standart korumali gruba da giremez. Cozum, arayuz kabugunun (`MapUi`) kullandigi UCUNCU grup deseninin tekrarlanmasidir: kendi `MapGroup`, `TraconEndpointFilter(options, requireBearerToken: false)`, istege bagli `RequireAuthorization(policy)`. Ayrinti: `GovernanceEndpoints.MapMcpOAuthCallback` + `TraconEndpointRouteBuilderExtensions.MapMcpOAuthCallback`.

### `T? param = null` deseni ne zaman GUVENLIDIR (2026-08-06, Faz 31, K-241) — Faz 77 butce rahatlatmasi

  - **Tersi de doğrudur ve GÜVENLİDİR** (2026-08-06, Faz 31, K-241): tip HER ZAMAN kayıtlıysa (koşullu bir özellik değil, `AddTracon()`'in kayıtsız şartsız kaydettiği bir servisse), `T? param = null` deseni DI'da güvenle **gerçek** örneği alır — C# varsayılanı yalnız tip HİÇ kayıtlı değilken devreye girer. `InMemoryRunStore(IRunScoreStore? scores = null)` bunu kullanır: DI yolunda paylaşılan tekil `IRunScoreStore`'u alır, yalnız `new InMemoryRunStore()` ile elle kurulan (test) kod özel bir örnek üretir. Yukarıdaki uyarı yalnız "kayıtlı OLMAYABİLECEK" (opsiyonel özellik) servisler içindir.

### Kestrel WebSocket feature'i ve kosullu ara yazilim (2026-08-05, Faz 29) — Faz 77 butce rahatlatmasi

- **🚨 Kestrel `IHttpWebSocketFeature` SAGLAMAZ; `UseWebSockets()` sart** (2026-08-05, Faz 29): onu `WebSocketMiddleware` kurar. Kutuphane kodunda tuketiciden ayrica cagri istemek `MapTracon`'in tek giris noktasi olma kuralini (K1) bozar ve hata yalnizca ilk WebSocket denemesinde gorunur. `MapTracon` ara yazilimi **kosullu** kurar: yalniz ilgili servis kayitliyken ve `endpoints is IApplicationBuilder` iken (K-223). Ikinci bir `WebSocketMiddleware` ornegi zararsizdir — feature'i dolu bulup gecer. `TestServer` WebSocket yukseltmesini kendisi taklit eder, `CreateWebSocketClient()` ile surec ici test edilebilir.

## `docs/hafiza/kod-haritasi.md`'den

### Agac toplamlari SQL'de, okumada hesaplanir (2026-08-02, Faz 12)

`SqlQueries` icindeki `treeJoin` iki `LEFT JOIN LATERAL` tasir:
`children.child_count` (`parent_run_id = r.id`) ve `tree.*`
(`root_run_id = r.id`). `RunRecord.ChildRunCount` ve `TreeUsage` bunlardan
gelir; saklanmaz. Sutun indeksleri `ReadRun` icinde 15–22 araligindadir;
sorguya sutun eklerken ikisini birlikte guncelleyin.

### Ekler — `Core/Attachments/` (2026-08-02, Faz 14)

`AttachmentTypeGuard` sihirli bayt + beyaz liste denetimi yapar.
`AttachmentUriReference` yalniz `/api/attachments/{id}` izine bakar, prefix
bilmez. `AttachmentResolvingChatClient`, `ModelProviderRegistry.CreateChatClient`
icinde devre kesicinin **icine** sarilidir — her gercek ag cagrisinda taze
cozulsun diye. `PostgresAgentFileStore` agent adini ambient
`TraconRunContext.Current?.AgentName`'den okur; arayuzde parametre yoktur
(K-114).

### Maliyet zinciri (2026-08-03, Faz 20)

Akis: `RunRecordingAgent.CompleteAsync` (birlesmis `usage` hazir olur olmaz) →
`_pricingResolver.Resolve(provider, modelId, usage)` →
`RunEventWriter.CompleteAsync(..., cost)` → `RunCompletion.Cost` → `store`.
Saglayici yalniz bu hesap icin tasinir, `runs` tablosuna yazilmaz (K-154).
Sutunlar migration 0011'dedir.

---

## `docs/hafiza/build-ve-analyzer.md`'den

### `NU5017` — bos sembol paketi ANA paketi de basarisiz gosterir (2026-08-06, Faz 37)

`src/Directory.Build.props` her pakete `IncludeSymbols=true` atar.
`IncludeBuildOutput=false` olan bir projede derlenen `.pdb` yoktur; eslik eden
`.snupkg` BOS kalir ve `NuGet.Build.Tasks.Pack` onu
`NU5017: Cannot create a package that has no dependencies nor content` ile
reddeder. Hata mesaji HANGI paketten (ana mi sembol mu) geldigini SOYLEMEZ —
ana paketin `nuspec`'indeki `<files>` listesi dogru dolu olsa bile build
basarisiz olur. `dotnet pack <proje> -c Release -v:diag` ile `_PackageFiles`
item grubunu (`-t:GenerateNuspec -getItem:_PackageFiles`) karsilastirarak izole
edildi. Cozum: `<IncludeSymbols>false</IncludeSymbols>`.

### `TargetFrameworks` (cogul) mirasi `dotnet pack`i capraz-hedefletir (2026-08-06, Faz 37)

`src/Directory.Build.props` `TargetFrameworks=net8.0;net9.0;net10.0` atar.
Projede yalniz `TargetFramework` (tekil) yazmak `dotnet build`i tek TFM'e
indirger (`dotnet build` ile dogrulanir), ANCAK `dotnet pack`in
capraz-hedefleme orkestrasyonu (`_GetFrameworksWithSuppressedDependencies`)
COGUL degeri okumaya devam eder ve uc ayri ic derleme baslatir — cikti klasoru
`_net10.0` soneki alir. Derlenmeyen bir paket icin TFM anlamsizdir;
`<TargetFrameworks></TargetFrameworks>` ile bosaltmak orkestrasyonu tek TFM'e
sabitler. Not: bu, NU5017'yi TEK BASINA duzeltmez (izole test edildi) — yalniz
verimlilik/basitlik kazandirir.

Ayni tuzagin ikinci hali (2026-08-06, Faz 39): `Microsoft.AspNetCore.TestHost`
surumu barindirma framework'uyle BIREBIR eslenir; merkezi surum 10.0.10 yalniz
`net10.0` destekler (`NU1202`). `Tracon.Testing` mirasi
`<TargetFrameworks>net10.0</TargetFrameworks>` ile (yine COGUL, tekil DEGIL —
ayni gerekce) ezerek tek TFM'e sabitledi.

### `<None Pack="true">` ile keyfi dosya paketleme (2026-08-06, Faz 37)

Resmi desen budur. Yukaridaki iki tuzak duzeltildikten sonra `<Content>` de
ayni sekilde calisir; `<None>` semantik olarak dogrusudur, cunku dosyalar
derleme ciktisina kopyalanmaz. `ContentTargetFolders` + item'in kendi
`content/` kok yolu BIRLIKTE kullanilirsa `content/content/...` cift onegi
uretir; `PackagePath`'i acikca
`content/%(RecursiveDir)%(Filename)%(Extension)` ile yazip
`ContentTargetFolders`'i HIC kullanmamak tek katmanli dogru sonucu verir.

### `dotnet pack <cozum>` TUM cozumu derler (2026-08-06, Faz 39)

`TemplateFixture` (Faz 37) sablon testleri icin yerel NuGet beslemesi uretmek
amaciyla `dotnet pack Tracon.slnx -c Release` cagiriyordu. Cozum 16 `src/`
paketinin yaninda 13 test projesi (Postgres/SqlServer/Sqlite container'li
entegrasyon testleri, Playwright E2E dahil) barindirir; `dotnet pack` bir cozum
dosyasi aldiginda HER proje icin Pack hedefini calistirir — paketlenemeyen
projelerde Pack no-op'tur ama Build ONA BAGIMLI oldugu icin YINE DE calisir.
Olculdu: Templates.Tests'in tek basina calismasi ~1 saat surdu; bunun buyuk
kismi bu gereksiz 13 test projesi derlemesiydi (`artifacts/package/release`
zaman damgalari bir `dotnet pack` cagrisinin 20-35 dakika surdugunu gosterdi).
Cozum: repo koku `Tracon.src.slnf` (yalniz 16 `src/` projesini listeleyen
bir cozum FILTRESI) eklendi; `dotnet sln <filtre>.slnf` `.slnx` formatini da
destekler (.NET 10 SDK ile dogrulandi). Warm pack ~30 saniyeye dustu, toplam
sure ~15 dakikaya (kalan sure gercek is: npm/frontend derlemesi + uretilen 3
projenin gercek NuGet restore'u).

### `wwwroot` + damga silindikten sonra arayuz derlemesi yarisir (2026-08-07, Faz 48)


K-050'nin kapatmadigi bosluk; iki kez yasandi. Belirti:
`ENOENT: ... unlink '.../wwwroot/assets/index-*.js'` ve
`npm run build exited with code 1`. K-050'nin cozumu zinciri
`BeforeTargets="DispatchToInnerBuilds"` ile **dis** derlemeye aldi, ama
`TraconCollectFrontendAssets` hedefi "tek hedefle derlerken dis derleme
yoktur" durumunu da karsilamak icin zinciri **kendisi** calistirir. Solution
derlemesinde `Tracon.UI`'a farkli TFM'lerden referans veren projeler (meta
paket, ornek, E2E) paralel olarak tek hedefli derlemeler tetikler ve o yol
yarisir. Damga guncel oldugunda yaris hic olusmaz — bu yuzden normal artimli
derlemede gorulmez, yalniz kopya dosya temizliginden sonra gorulur.

---

## `docs/hafiza/sql-saglayicilari.md`'den

### Iki dalli upsert deseni — 204 testin 204'u nasil kirildi (2026-08-05, Faz 23 kapanisi)

`azure-sql-edge` ile ilk kez gercek SQL Server testleri kosturuldugunda 204
testin tamami kirildi. Uc kok sebep cikti (K-187, K-188, K-189):

1. **`@@ROWCOUNT` oneki eksikti.** `ROWCOUNT` tek basina T-SQL'de gecersiz
   sozdizimidir; sistem degiskeni her zaman `@@ROWCOUNT`'tir.
   `SqlServerQueries.cs` genelinde **17 sorguda** bu onek yoktu — hicbir derleme
   veya format kapisi yakalamaz, yalnizca calisma aninda "Incorrect syntax near
   ROWCOUNT" verir.
2. **Iki dalli upsert ikinci sonuc kumesine yazar.** K-177'nin deseni
   (`UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT`) UPDATE sifir
   satir etkiledigende gercek satiri IKINCI kumeye koyar.
   `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnizca ilk kumeye
   bakiyordu; kayit INSERT edilmis olsa bile `null` donuyordu. Ikisi de artik
   `NextResultAsync` ile satir/deger bulunana kadar sonraki kumelere duser.
   PostgreSQL'in tek ifadelik `RETURNING` deseninde bu dongu zararsizdir.
3. **Paylasilan `store` saglayiciya ozgu ADO.NET tipine basvurmusti.**
   `SqlWebhookStore.ReadSubscription`, `Dialect.ReadTextArray` yerine Npgsql'in
   dogal dizi destegine (`GetFieldValue<string[]>`) dayaniyordu; PostgreSQL'de
   sessizce calisiyordu, SQL Server'da `InvalidCastException` verdi.

### Kucuk notlar (Faz 67 bütçe tasarrufu icin arsivlendi)

- **`DbDataSource` uyarlayicisi elle yazildi**: `Microsoft.Data.SqlClient` bir
  `DbDataSource` uygulamasi sunmaz (Npgsql sunar). `SqlServerDataSource`
  yalnizca `CreateDbConnection()`'i uygular; taban sinifin `CreateCommand`
  uygulamasi baglanti omrunu Npgsql ile ayni sekilde yonetir.
- **Pencere fonksiyonu/filtreli indeks uc diyalekt ayni sozdizim** (K-299): ilk
  `ROW_NUMBER()`/`COUNT() OVER`; SQLite dogrulandi, SQL Server olculmedi.

### Linked-source tiplerin XML doku kimligi catismasi (2026-08-07, Faz 40, K-276)

`MigrationRunner`, `SqlStoreContext` gibi paylasilan (K-176) tipler
`Tracon.SqlServer.xml`, `Tracon.Sqlite.xml` ve `Tracon.PostgreSql.xml`
dosyalarinin HER BIRINE ayni `T:Tracon.MigrationRunner` doku kimligiyle
yazilir. `AddOpenApi()` kullanan bir tuketici 2+ SQL saglayicisini BIRLIKTE
referans verirse (`samples/Tracon.Api`'nin K-185 icin bilerek yaptigi gibi),
ureticinin XML yorum onbellegi (`OpenApiXmlCommentCache.GenerateCacheEntries()`)
tum derlemelerin doku girdilerini TEK sozlukte — derlemeden BAGIMSIZ anahtarla —
toplar ve `ArgumentException: An item with the same key has already been added`
ile `/openapi/v1.json` **500** doner.

K-247'nin "internal isareti cross-assembly guvenilmez" dersinin XML-doc
kardesi: burada sorun DERLEME KIMLIGI degil, XML doku ureticinin derlemeyi hic
ayirt etmemesidir.

---

## `docs/hafiza/cekirdek-calistirma.md`'den

### `MessageCompleted` yalniz akissiz yolda yazilir (2026-08-06, Faz 39)

`RunCoreAsync`, `response.Messages` uzerinde `WriteContentsAsync` (bu da
`RecordMessageDeltas` acikken HER `TextContent` icin bir `MessageDelta` yazar)
cagirdiktan SONRA ayrica bir `MessageCompleted` olayi ekler — akissiz yolda AYNI
metin iki kez, iki farkli olay tipinde bulunur. Akisli yol (HTTP
`/api/agents/{name}/run`, SSE) yalniz `WriteContentsAsync`'i her `update` icin
cagirir ve es deger bir "tamamlandi" olayi HIC yazmaz.

Bir tuketici kaydi (`Tracon.Testing.RunAssertions.ShouldHaveOutputContaining`)
yalniz `MessageCompleted`'e bakarsa HTTP uzerinden calisan her run icin
yanlislikla BOS cikti gorur. Testlerin kendisi degil, gercek bir dis tuketici
senaryosu (`docs/arsiv/fazlar/39-TEST-PAKETI.md` DoD'sindeki "depo disi tuketici" adimi)
yakaladi.

### `ObservableGauge` neden senkron kapili onbellekle yazildi (2026-08-06, Faz 35, K-256)

Ilk tasarim `BackgroundService` + `PeriodicTimer` idi (`JobWorkerBackgroundService`
ile ayni desen). net8.0'da `PeriodicTimer(TimeSpan, TimeProvider)` ve
`System.Threading.Lock` yoktur; ayrica mevcut `ManualTimeProvider` sahtesi
`CreateTimer`'i override etmiyordu — tasarim test edilemez hale geldi.
Senkron-kapili tasarim hem DoD'yi (ardisik yoklama = 1 sorgu) karsiladi hem
dogrudan test edilebilir kaldi.

### Oksuz calistirma uzlastirmasinin ayrintilari (2026-08-09, Faz 54, K-362)

`RunHeartbeatWriter` ayri bir `BackgroundService`'tir ve her `HeartbeatInterval`'da
`IRunCancellationRegistry.ActiveRunIds`'in o anki goruntusunu toplu okur;
`RunEventWriter`'a HICBIR yeni cagri EKLENMEDI — sicak yol (agent calistirma) bu
ozellikten habersizdir. `ClaimOrphanedRunsAsync` esik disi `Running` satirlari
`COALESCE(heartbeat_at, started_at) < staleBefore` ile bulur (heartbeat hic
yazilmamis bir calistirma da esik gecince yakalanir) ve KENDISI hem `runs`
UPDATE'ini hem kapanan `RunFailed` olayini yazar — `RunEventWriter` o surecte
artik yoktur. Sira numarasi `COALESCE((SELECT MAX(seq) ...), -1) + 1` alt
sorgusuyla turetilir. Hata parmak izi SABIT `"orphaned"` dizesidir,
`ErrorFingerprint.Compute` DEGIL (K-364 — o tip `Tracon.Core`'da
internal'dir, `Tracon.Sql.Shared` ona erisemez).

### MAF tool'a bos servis saglayici gecirir; `AIFunctionArguments.Services` kullanilamaz (Faz 28, K-218)

Tool govdesinde `arguments.Services` `EmptyServiceProvider`'dir.
`ChatClientAgentOptions` bir `Services` ozelligi tasimaz ve
`AsAIAgent(..., _services)` saglayicisi fonksiyon cagrisina AKMAZ. Tool
bagimliliklarini KURULUM aninda alin:
`services.AddSingleton(p => new TraconToolRegistration(new BenimTool(p)))`.

Ayni sebeple `AddToolsFrom` ile kaydedilen **ornek metot** tool'lari da
calismaz; Faz 52'de `ToolMethodScanner` reddi TARAMA ANINA tasidi (K-347).

---

## `docs/hafiza/maf-api.md`'den

### Harness + tool cagrisi = kirik akis (2026-08-02, K-053)

Kanitlandi: Playground'dan `arastirmaci` (Harness + `get_order_status`)
calistirilinca model `finishReason: tool_calls` ile bitiyor ama fonksiyon hic
cagrilmiyor ve akis `done` olmadan kesiliyor. Ayni oturumdaki bir sonraki turda
MAF, `tool_calls` iceren asistan mesajini atlayip yetim bir `tool` mesaji
gonderiyor; OpenAI `HTTP 400: messages with role 'tool' must be a response to a
preceeding message with 'tool_calls'` ile reddediyor.

Ayni senaryo duz `ChatClientAgent` (`support`) ile temiz calisiyor (37 SSE
olayi, `done` ile biter) — hata Tracon kodunda degil, Harness paketinin
onay-baglama zincirinde (`Microsoft.Agents.AI.ApprovalResponseBindingChatClient`).
Ornek kasitli olarak degistirilmedi, kusur belgelendi.

### `FunctionInvokingChatClient` tool istisnasini yutar (2026-08-07, Faz 47, K-309)

Olculdu: yeniden oynatma sirasinda firlatilan `ReplayToolMismatchException` HTTP
ucuna hic ulasmadi ve istek `200` dondu — istisna bir tool sonucuna cevrilir ve
dongu devam eder. Donguyu kesmenin yolu
`FunctionInvokingChatClient.CurrentContext.Terminate = true`'dur
(`FunctionInvocationContext.Terminate`, public); hata ise calistirma bittikten
sonra, model cagrisinin DISINDA firlatilmalidir. Tracon bunu
`ReplayMismatchGuard` ile yapar ve sarmalayiciyi `RunRecordingAgent`'in ICINE
koyar — boylece istisna kayit sarmalayicisinin `catch`'ine duser ve `runs`
satiri `Failed` kapanir.

### `ClientOAuthOptions` dogrulamasi nasil yapildi (2026-08-04, Faz 22)

`ModelContextProtocol.Core`'un `ClientOAuthOptions.RedirectUri`'si `required`dir
ve SDK yalniz Authorization Code (+PKCE) destekler; client_credentials gibi
kullanici etkilesimsiz bir akis SDK'da YOKTUR. `AuthorizationCallbackHandler`
verilmezse SDK'nin varsayilani "kullanicidan tam yonlendirme URL'sini elle
girmesini ister" (XML belge). Dogrulama `dotnet package` aramasiyla degil,
reflection + paketin gomulu XML belgesi (`~/.nuget/packages/.../*.xml`)
okunarak yapildi.

## SQL saglayicilari — Faz 33 ve Faz 64 vaka anlatilari

> `docs/hafiza/sql-saglayicilari.md` butcesini asinca TASINDI (Faz 68). Kural
> ozeti alan dosyasinda kaldi; asagisi vakanin tam anlatisidir.

- **🚨 Linked-source (K-176) bir tipin `internal` isareti CROSS-ASSEMBLY sayim icin GUVENILMEZ** (2026-08-06, Faz 33, K-247): `MigrationHostedService`'in K-183 sayaci `internal SqlPersistenceRegistration` kullaniyordu; bu tip `Tracon.PostgreSql.dll` ve `Tracon.SqlServer.dll` icine AYRI AYRI derlenir ve CLR kimligi FARKLIDIR — `UsePostgreSql()` + `UseSqlServer()` birlikte cagrildiginda hicbir `MigrationHostedService` digerinin isaretini GOREMEZ ve cift kayit uyarisi hic tetiklenmez. Sayim/teshis Abstractions'da PAYLASILAN tek bir derlenmis tipe (`SqlPersistenceRegistrationMarker`) tasindi. Ayni tuzak: linked-source icindeki herhangi bir `internal` tipi `IEnumerable<T>` ile SAYMAK istiyorsan, T Abstractions'da olmali.

- **🚨 `IDENTITY` var olan bir tabloya `ALTER TABLE ... ADD` ile EKLENEMEZ** (2026-08-18, Faz 64): `IDENTITY` yalnız `CREATE TABLE` anında tanımlanabilir; PostgreSQL'in `ADD COLUMN ... GENERATED BY DEFAULT AS IDENTITY`'sinin SQL Server karşılığı yoktur. Yerine: ayrı bir `CREATE SEQUENCE {schema}.x AS bigint`, sonra `ALTER TABLE ... ADD col bigint NOT NULL DEFAULT (NEXT VALUE FOR {schema}.x)` — yeni her satır varsayılan değer olarak sıradaki sayıyı alır, `IDENTITY` ile aynı garantiyi (veritabanının atadığı, artan bir sayaç) verir. 🚨 Test altyapısı da güncellenmelidir: şema silme sırası TABLOLAR → SEQUENCE'lar → `DROP SCHEMA` olmalıdır (`SqlServerTestContext.DropSchemaAsync`), aksi halde "Cannot drop schema because it is being referenced by object" hatası alınır.

- **🚨 CAGIRANIN VERDIGI bir metin tek basina birincil anahtar olamaz** (Faz 41, K-278): `sessions.id` butun kiracilar arasinda benzersizdi ve `ON CONFLICT (id) DO UPDATE` bir kiracinin digerinin oturumunu UZERINE YAZMASINA izin veriyordu. Anahtar `(tenant_id, id)` oldu. **Kural**: kimlik cagirandan geliyorsa anahtar kiraciyi de icermelidir; uuid v7 (K-015) bu tuzagi tasimaz. SQLite birincil anahtari DEGISTIREMEZ — tablo yeniden kurulur, veri tasinir, indeksler ELLE yeniden olusturulur.

- **🚨 Kiraci basina tanimlanan bir politika, kiraci suzgeci OLMAYAN bir veri duzlemiyle calisamaz** (Faz 41, K-279): `RetentionTargetRegistry` hicbir hedefte `tenant_id` tasimiyordu; bir kiracinin saklama politikasi BUTUN kiracilarin satirlarini siliyordu. `RetentionTargetDefinition` artik `TenantPredicate` tasir; kendi `tenant_id` sutunu olmayan uc hedef sahibine bakan `EXISTS` ile suzulur (korelasyon FULL NITELENDIRILMIS adla — K-259). Yeni hedef eklerken `TenantPredicate`'i unutma; yanlis yuklem sessizce calisir.

- **Preview'ı ayrı bir `COUNT` yerine gerçek `DELETE`'i çalıştırıp `ROLLBACK`/`COMMIT` ile ayırmak DAHA GÜVENLİ (2026-08-18, Faz 64)**: iki ayrı sorgu seti zamanla sapar; `SqlDataSubjectStore` `dryRun`'da her zaman `ROLLBACK`, değilse geri çağrı başarılıysa `COMMIT` eder — tek doğruluk kaynağı.

- **🚨 Depoda `LoadAllAsync` (kapsamdaki TUM satirlari cekip C#'ta filtreleme) deseni bulursan supheyle yaklas** (Faz 51): `SqlAgentFileStore` prefix+glob'u client-side filtreliyordu; SQL'e indirildi (`LIKE ... ESCAPE`, onek `EscapeLikeLiteral` ile kacislanir). SQL Server/SQLite regex TASIMAZ — nihai eslesme HER ZAMAN .NET `Regex` ile istemcide kalir, SQL yalniz on daraltmadir. `SqlDialect.IsInvalidRegexError` PostgreSQL ARE'sinde gecersiz bir .NET desenini yakalayip on suzgecsiz yeniden dener.

## Cekirdek calistirma — Faz 68 vaka anlatilari

> `docs/hafiza/cekirdek-calistirma.md` butcesini asinca TASINDI. Kural ozeti
> alan dosyasinda kaldi.

- **🚨 Elle yazılmış `InputCost + OutputCost` toplamı, üçüncü bir maliyet terimi eklendiği an SESSİZCE eksik raporlar — bağımsız denetim beş yerde birden buldu** (2026-08-19, Faz 68, K-483): cache ücreti (`RunCost.CachedInputCost`) `input_cost`'un ALT KÜMESİ değil ÜÇÜNCÜ terimidir (`runs.input_cost` cache token'ını zaten dışarıda bırakır). SQL tarafı düzeltilmişti ama **çalışma anındaki** toplamların hiçbiri değildi: kota muhasebesi, `tracon.run.cost` metriği, webhook özeti, workflow kotası, `ModelRunJudge`, `OnlineEvalSummaryService` ve arayüzdeki `run-comparison.tsx`. Etki teorik değil — **maliyet tavanı olan bir kiracı tavanı aşabilirdi**. Çözüm bir yardımcı metottur: `RunCost.Total()`/`RunTreeCost.Total()`, ve "bu run ne tuttu" sorusunun TEK cevabıdır. **Kural**: `RunCost`'a alan eklerken `Total()`'ı güncelle ve `grep -rn "InputCost ?? 0" src/` ile sınıfı tara. Kapı: `Every_cost_total_includes_the_cache_charge` (özet + zaman serisi + deney sonucu + ağaç, dört depoda birden).

- **🚨 Bellek içi store ile SQL store'un AYNI sorguya farklı yanıt vermesi sözleşme testinden kaçabilir — test o sorguyu hiç sormuyorsa** (2026-08-19, Faz 68, denetim 🔴#2): cache ücreti üç dialektin `GetTimeSeriesAsync`/`GetExperimentResultsAsync` sorgularına eklenmişti ama `InMemoryRunStore`'un karşılıklarına eklenmemişti; dört sözleşme koşumu da YEŞİLDİ çünkü `RunStoreContract` maliyeti yalnız `GetStatisticsAsync` ve `TreeCost` için soruyordu. Bir alanı "her yerde" eklediğini düşündüğünde, o alanı OKUYAN her depo metodunun sözleşme testinde bir iddiası var mı diye bak.

- **🚨 Ambient bir bağlamı bir kayıt yolunda DOĞRUDAN okuma — tüketicinin uygulaması fırlatabilir ve doğrulanmamış değer döndürebilir** (2026-08-19, Faz 68, denetim 🔴#4): `WorkflowRunner` attribution'ı `_attributionContext?.UserId` ile okuyordu; `RunRecordingAgent`'ın try/catch + sınır doğrulama + `Freeze` üçlüsünün hiçbiri yoktu. İki somut arıza: tüketici implementasyonu fırlatınca workflow `run`'ı ÖLÜRDÜ ("gözlemlenebilirlik işlevselliği bozmaz" ihlali), ve 200 karakterden uzun bir `userId` SQL Server insert'ini patlatıp `RunEventWriter`'ı devre dışı bırakarak **workflow'un TÜM kaydını sessizce kaybettirirdi**. Garantiler `RunAttributionReader.Read(...)`'e çıkarıldı; her kayıt yolu onu kullanır. Gürültülü ret sınırda kalır (HTTP `400`, `Begin` → `ArgumentException`).

- **🚨 `AgentDefinitionCompiler.Compile` TAMAMEN senkron; kiracı kimlik bilgisi çözümlemesi async `store` gerektirir — ikisi çelişince YENİ paralel async yol açıldı, mevcut sync yol DEĞİŞTİRİLMEDİ** (2026-08-19, Faz 65): `IModelProviderRegistry.CreateChatClient(ModelBinding)` (sync) hep global kimlik bilgisiyle çalışır ve DEĞİŞMEDİ; yeni `CreateChatClientAsync(ModelBinding, CancellationToken)` kiracı `store`'larını çözer. Aynı ikili desen `AgentDefinitionCompiler.Compile`/`CompileAsync` ve `CompiledAgentCache.GetOrAdd`/`GetOrAddAsync`'te tekrarlanır — sync üçlü GÖVDESİ paylaşılan bir `BuildAgent`/`BuildPipeline` yardımcısına taşınır, yalnız `IChatClient` üretim adımı ayrışır. Gerçek `run` yolu (`DefinitionStoreAgentSource`/`CodeAgentSource`) async'e taşındı; `AgentDefinitionValidator.CheckModel` de (zaten async `ValidateAsync` içinde) async'e geçti. `RunReplayService` da async yola taşındı — aksi halde bir tekrar oynatım (`replay`), orijinal `run`'ın kullandığı kiracı anahtarı yerine sessizce global anahtara dönerdi.

- **🚨 `AuditSecretFilter` alan ADINA bakar, DEĞERE değil — "apikey" fragmanı taşıyan her alan içerik zararsız olsa da `***` olur** (2026-08-19, Faz 65): `tenant_provider_bindings`'in `apiKeyConfigurationName`'ı K-059 gereği zaten bir DEĞER değil, yalnız yapılandırma anahtarının ADI — ama JSON alanı `"apiKeyConfigurationName"` olarak yazılınca "apikey" fragmanı eşleşti ve denetim izinde teşhis için asıl gereken bilgi (hangi anahtar adı ayarlandı) `***` oldu. Çözüm alanın kendisini değiştirmek değil, denetim özetinde farklı adlandırmaktır: `TenantProviderEndpoints.DescribeForAudit` alanı `configKeyName` yazar. Zararsız olduğu KANITLANMIŞ bir alanı denetime yazarken adında `apikey`/`authorization`/`token` (tekil)/`password`/`secret` geçmediğinden emin ol — geçiyorsa filtre teşhis değerini sessizce yok eder.

- **🚨 `JobRecord.Payload` atanmazsa `/api/jobs` TUM listeyi 500 ile dondurur** (2026-08-03, Faz 21): `JsonElement` bir struct'tir; atanmazsa `default` olur (`ValueKind = Undefined`) ve `JsonElementConverter.Write` onu serilestiremeyip `InvalidOperationException` firlatir. Etki tek bir isle sinirli degildir — liste ucunun tamami cokar. 1231 test yakalamadi (birim testleri kuyruga yaziyor ama HTTP'den serilestirmiyordu); ornek uygulamada `/api/jobs?kind=WebhookDelivery` cagrilinca ortaya cikti (K-166). **Kural**: yeni bir `JobRecord` ureten her kod yolu `Payload` atamalidir. Regresyon testi hem `ValueKind`'i hem gercek serilestirmeyi denetler.

- **🚨 Disaridan iptal, `RunRecordingAgent`'in KENDI `CancellationTokenSource`'una guvenir, gelen `cancellationToken`'a DEGIL** (Faz 32): `RunCore*Async` gelen token'i dogrudan iletmez; `CreateLinkedTokenSource(cancellationToken)` ile kendi kaynagini kurar, deftere onu yazar ve `cancellationSource.Token`'i kullanir. Sebep: gelen token'in KENDISI iptal edilemez — yalnizca ondan turetilen bir kaynak iptal edilebilir; aksi hâlde defterin `Cancel()`'i hicbir seyi etkilemezdi. `WorkflowRunner.ExecuteAsync` ayni deseni tekrarlar.

- **🚨 Skill script: korumasiz `StandardInput.Close()` + JSON'a cevrilmemis denetim `after`'i ikisi de sessizce coker** (2026-08-15, K-400 SONRASI bulundu, Aile W): `SkillScriptProcessRunner.WriteArgumentsAsync`'in `finally`'sindeki `Close()` `try/catch` DISINDAYDI — stdin okumadan cikan (`echo`) bir script'te `Close()`'un ic flush'i `Pipe is broken` firlatip TUM calistirmayi cokertiyordu. Ayrica `SandboxedSkillScriptRunner.DenyAsync` red nedenini `jsonb` sutununa JSON'a CEVIRMEDEN yaziyordu → Postgres `22P02`, HER `script.denied` izi kayboluyordu. Ikisi de gercek alt surec+Postgres gerektirir; sahte `IAuditLog` YAKALAMAZ. Duzeltme: `Close()` kendi `catch(IOException)`'ina alindi; `after` `JsonSerializer.Serialize` ile sarildi.

- **🚨 `__migrations` defterinin KENDI semasini degistiren islem, K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (2026-08-19, Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ALTER TABLE ... ADD set_name` sonrasi AYNI iste `InsertMigration`'in `set_name` referansli SABIT metni "Invalid column name" verir, `EXEC` ile de SARILAMAZ (HER migration'a otomatik eklenir). Cozum: `CreateMigrationsTable` gibi dongu BASLAMADAN ONCE calisan `SqlDialect.UpgradeMigrationsTableAsync`. SQLite (K-278) bunu KODDA rebuild ile yapar.

- **🚨 `ObservableGauge` geri cagirmasi es zamanlidir — icine `await` konamaz** (Faz 35, K-256): veritabani okuyan bir olcer `SemaphoreSlim` korumali, `TimeProvider` karsilastirmali bir onbellekle yazilir. `QuotaUsageObserver.Snapshot()` deseni: onbellegin yasini `QuotaUsageRefreshInterval` ile karsilastir, bayatladiysa BIR kez blok olarak tazele, tazeyse onbellekten don. `BackgroundService`/`PeriodicTimer` denendi ve test edilemez cikti — gerekce: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

- **`TraconMetrics`'e yeni bir sayaç eklerken `RunRecordingAgent.CompleteAsync` icindeki cagriyi `scope.Depth`'e KOSULLU yapma — `RecordRun` gibi HER calistirmada (kok + alt) cagir** (2026-08-06, Faz 35): `RecordCost` cagrisi `if (scope.Depth == 0)` blogunun DISINDA durur; K-151'in "sayac yalniz KENDI maliyetini yayar" kuralini dogal olarak saglar (her `CompleteAsync` cagrisi zaten yalniz kendi `usage`'indan hesaplanmis `cost`'u gorur). Kota/olay yayini (`RecordQuotaAsync`/`PublishRunEventAsync`) ise KASITLI olarak yalniz kok'te calisir (agac bir kez sayilsin diye) — iki kural birbirine KARISTIRILMAMALI, farkli gerekceleri var.

- **🚨 Linked-source (K-176) tiplerin AYNI tam nitelikli adi, `Microsoft.AspNetCore.OpenApi`'nin XML yorum onbellegini CATISTIRIR** (Faz 40, K-276): 2+ SQL saglayicisini birlikte referans veren ve `AddOpenApi()` kullanan bir tuketicide `/openapi/v1.json` **500** doner (`An item with the same key has already been added`). K-247'nin XML-doc kardesi. Ayrinti ve durum: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md), `docs/ADAYLAR.md` F-76.

- **🚨 Tipi verilmemis `decimal` parametresi `decimal(18,0)` sayilir ve ONDALIK KISIM SESSIZCE KESILIR** (2026-08-04, Faz 23): butun para sutunlari `decimal(20,10)`'dur; `SqlServerDialect.AddDecimal` `Precision = 20`, `Scale = 10` yazar. Yazilmazsa maliyetler tam sayiya yuvarlanir ve hicbir test bunu yakalamaz — yalnizca gidis-donus testi yakalar (`SqlServerDialectTests.Maliyet_ondaligi_kesilmeden_gidip_gelir`).

- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz.** Ozellikler `{ get; protected set; } = string.Empty;`'dir; yazilmayan sorgu bos metin kalir ve hata yalnizca CALISMA ANINDA gorunur — derleme de test de kirilmaz. Sorgu sayisi saglayici sayisiyla carpiliyorsa desen degistirilir: Faz 25'te veri duzlemi icin 11 hedef × 3 saglayici = 99 elle sorgu yerine `Sql.Shared/Internal/RetentionTargetRegistry.cs` + `SqlDialect`'te 3 sablon yontemi secildi (K-198).

## `docs/hafiza/test-altyapisi.md`'den — 2026-08-01…03 (Faz 11–19 donemi)

> Alan dosyasi butcesini asti (Faz 76). Bu on bes madde kurallarini hala
> tasiyor ama hepsi kapanmis bir gecise ait: xunit.v3/VSTest ayrimi,
> Testcontainers 4.13 ctor degisikligi, Shouldly ve Meziantou cakismalari,
> erken Playwright locator tuzaklari. Bir tuzak arayan grep buraya da bakmali.

- **xunit.v3 VSTest ile çalışmaz** (2026-08-01): MTP kullanır. `Microsoft.NET.Test.Sdk` ve `xunit.runner.visualstudio` referans **edilmez**. TRX eklentisi de sürüm uyumlu olmalı: xunit.v3 3.2.2 → Platform **v1** → `Microsoft.Testing.Extensions.TrxReport` **1.9.1** (2.x `TypeLoadException` verir).
- **Testcontainers `PostgreSqlBuilder()` parametresiz ctor'u kullanımdan kalktı** (2026-08-02): 4.13.0'da `CS0618` veriyor. `new PostgreSqlBuilder("postgres:18-alpine")` kullan.
- **`WebApplicationFactory<T>` kütüphane testinde kullanılamaz** (2026-08-02): giriş noktası derlemesi ister. `Microsoft.AspNetCore.TestHost` + `WebApplication.CreateSlimBuilder()` + `UseTestServer()` + `GetTestClient()` kullanılır.
- **TestServer'da `RemoteIpAddress` `null`'dur** (2026-08-02): loopback testleri için test barındırıcısına başlıktan IP yazan bir ara yazılım konur. `LoopbackGuard` `null`'u yerel sayar — istek bir ag soketinden gelmemiştir.
- **Başarısız policy kimlik doğrulaması olmadan `IAuthenticationService` ister** (2026-08-02): challenge üretmeye çalışır ve `InvalidOperationException` atar. Policy testlerinde bir test authentication scheme kaydedilir; o zaman `403` döner.
- **Playwright tarayıcı ikilisi kodla indirilir** (2026-08-02): `Microsoft.Playwright.Program.Main(["install", "chromium"])`. Fixture bunu çağırdığı için `dotnet test` ek kurulum adımı istemez.
- **Shouldly + Meziantou çakışmaları** (2026-08-02): `list.ShouldContain("x")` `MA0002` verir (comparer yok) → predicate kullan. Bir `record` olmayan tipte `x.ToString()` çağırmak `MA0150` verir. Nullable dönen `ToString()` sonucunu `ShouldContain`'e vermek `CS8604` verir → `?? string.Empty`. `string?.ShouldBe(string?)` ve `ShouldBeOneOf(...)` de `MA0002` verir → `string.Equals(a, b, StringComparison.Ordinal).ShouldBeTrue(...)` yaz. Satır içi `new Regex("...")` `MA0009` verir → zaman aşımı veren bir `static readonly` alan kullan.
- **Migration sayısını teste sabit yazma** (2026-08-02): `MigrationTests` "1 migration" bekliyordu, 0002 eklenince kırıldı ve kırılma testin doğruladığı davranışla ilgisizdi. Sayı gömülü kaynaklardan okunuyor artık.
- **Shouldly `ShouldContain(predicate)` void döner** (2026-08-02): bulunan öğeyi kullanmak için LINQ `Single(...)` gerekir.
- **Yeni migration eklemek `MigrationTests`'in tablo sayısını kırar** (2026-08-02, Faz 11): 0004 iki tablo ekleyince beklenen 18 → 20 oldu. Sayı bilerek sabittir (yeni tablo fark edilsin diye); güncellemeyi unutma.
- **`IAsyncLifetime.InitializeAsync()`'te kurulan `AsyncLocal` test govdesine akmayabilir** (2026-08-02, Faz 14): xunit v3 (MTP) yasam dongusu kancasini ve `[Fact]` govdesini ayri zamanlanmis isler olarak calistirabiliyor. `TraconRunContext.SetCurrent(...)` `InitializeAsync`'te degil, dogrudan test govdesinin İÇİNDE cagrilmali — Faz 6/11/12'nin `AsyncLocal` tuzaklarinin testlerdeki hali.
- **Playwright `GetByText` gizli `<option>` metnini de bulur** (2026-08-03, Faz 16): Runs listesindeki `awaiting input` rozetini bekleyen test, durum suzgecindeki gizli `<option>Awaiting input</option>` ogesini bulup zaman asimina ugradi. `new() { Exact = true }` ikisini ayirir (rozet kucuk harf, secenek buyuk). Faz 8'in `GetByPlaceholder` tuzaginin ayni hali.
- **Playwright locator'ları varsayılan olarak alt dize/çoğul eşler, strict mode ihlali verir** (2026-08-02→2026-08-03): `GetByPlaceholder("github")` `"Tracon:Mcp:GithubToken"` ile de eşleşti (Faz 16); `GetByText("Awaiting input")` gizli bir `<option>` içeriğini de buldu (Faz 16); `GetByRole(Heading, Name: "Experiments")` sayfa `h1` başlığı ile Panel `h2` başlığını **ikisini birden** buldu (Faz 19, aynı metin iki farklı heading seviyesinde). Üçünde de çözüm `new() { Exact = true }` veya `.First`; bir metin panel başlığıyla sayfa başlığında aynıysa önceden `.First` eklemek varsayılan olmalı.
- **🚨 Shouldly `HashSet<T>.ShouldBe(otherSet)` KUME esitligi degil, SIRALI esitlik denetler** (2026-08-03, Faz 17): iki `HashSet<Guid>` ayni elemanlari tasisa bile enumerasyon sirasi farkliysa test yanlislikla duser — eszamanlilik testinde iki gercek isci arasinda pay edilen 50 is bu yuzden "kayip" gibi gorundu, hicbiri kaybolmamisti. Dogrusu `left.SetEquals(right).ShouldBeTrue()`.

<!-- MEMORY.md'de ozeti var; tam metin burada korunur -->
- **`dotnet test` MTP'de `--filter-query` MSBuild anahtarı DEGIL** (2026-08-03, Faz 19): xunit v3 (Microsoft.Testing.Platform) filtre sozdizimi VSTest'ten farklidir; `dotnet test <proj> --filter-query ...` `MSB1001: Unknown switch` verir. Tek bir testi kosmak icin butun projeyi calistirip cikan metin grep'lemek daha guvenilir (proje kucukse maliyeti onemsiz).

## `docs/hafiza/cekirdek-calistirma.md`'den — Faz 77 bütçe rahatlatması

### Agac genelinde W3C trace kimligi (2026-08-02, Faz 12)

- **🚨 Bir agactaki tum calistirmalar ayni W3C trace kimligini paylasir** (2026-08-02, Faz 12): `RunTraceCollector` tamponu trace kimligiyle anahtarlar ve `CompleteRunAsync` tamponu **kaldirir**. Alt calistirma once bittigi icin tum agacin span'lerini o sahipleniyordu; gercek bir cagrida kokun `/trace` ucu `404`, alt calistirmanınki dolu geldi. Toplayici artik yalnizca `Depth == 0` iken cagrilir. Yalnizca birim testleriyle yakalanamazdi — ornek uygulamayi gercekten calistirmak ortaya cikardi.

### `AuditSecretFilter` ad-tabanli suzme (Faz 65)

- **🚨 `AuditSecretFilter` alan ADINA bakar, DEGERE degil — "apikey" fragmani tasiyan her alan icerik zararsiz olsa da `***` olur** (Faz 65): zararsiz oldugu KANITLANMIS bir alani denetime yazarken adinda `apikey`/`authorization`/`token` (tekil)/`password`/`secret` gecmediginden emin ol — geciyorsa filtre teshis degerini sessizce yok eder. Cozum alani degistirmek degil, denetim ozetinde farkli adlandirmaktir (`configKeyName`). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### Tool token disi olcum zinciri (2026-08-05, Faz 28)

- **Tool'un token DISI olcumu cagri kimligiyle kayda baglanir** (2026-08-05, Faz 28): `TraconToolUsage.Report(...)` → `AgentRunScope.ToolUsage` (`ToolUsageAccumulator`, cagri kimligine gore) → `ToolInvocationTracker.OnResult` `Take(result.CallId)`. Cagri kimligi `FunctionInvokingChatClient.CurrentContext.CallContent.CallId`'den okunur — tool govdesinde DOLUDUR (olculdu). `AIFunctionArguments.Context` ise `null` gelir ve kimligi tasimaz. Bildirim baglanamazsa `false` doner ve tool'un isi bozulmaz.

### `AgentRunScope.SessionId` sahiplik kapsami (2026-08-05, Faz 28, K-217)

- **`AgentRunScope.SessionId` tool'un urettigi icerigin sahibidir** (2026-08-05, Faz 28): bir tool `AgentSession`'a erisemez. Oturumsuz yazilan bir ek saklama politikasi tarafindan **sahipsiz** sayilip silinir (`RetentionTargetRegistry`, `session_id IS NULL`) — oturum hala yasarken icerik kaybolur. Kapsamdaki kimlik `runs.session_id` sutunundan GENIS tanimlidir: alt calistirmaya MAF oturum gecirmez ama uretilen icerik yine kok oturuma aittir (`TraconRunOptions.SessionId` ile tasinir). Karar K-217.

### Saglayici SDK istisna tipleri (2026-08-07, Faz 44, K-296)

- **🚨 Resmi saglayici SDK'lari `HttpRequestException` FIRLATMAZ** (2026-08-07, Faz 44, K-296): `DefaultRunErrorClassifier` ilk taslakta yalniz `HttpRequestException`/`SocketException`/`IOException` ariyordu; birim testleri gecti ama gercek bir OpenAI 404'unde SDK **`System.ClientModel.ClientResultException`** firlatti ve hata `Unknown`'a dustu. Desen simdi `ClientResultException`/`RequestFailedException`/`ApiException` ve mesajdaki `HTTP 4xx/5xx`'i de kapsar. Yeni saglayici eklerken gercek istisna adini `samples/Tracon.Api` ile olcun, tahmin etmeyin.

### `ISessionStore.SaveAsync` yalniz basari yolunda (2026-08-07, Faz 45, K-300)

- **🚨 `ISessionStore.SaveAsync` YALNIZ basari yolunda cagrilir; basarisiz calistirmanin oturumu HIC kaydedilmez** (2026-08-07, Faz 45, K-300): `AgentEndpoints.AgentRunStream` `sessions.SaveSessionAsync(...)`'i yalniz basari sonrasi cagirir, `catch` bloklari cagirmaz — oturum/`ChatHistoryProvider` uzerinden girdi metni okumaya calisan bir tasarim basarisiz calistirmalarda hep bosa cikar (olculdu). Girdi metni bunun yerine `RunEventWriter.StartAsync`'in yazdigi `RunStarted.Text`'ten okunur (`RunRecordingAgent.ExtractQuery`, `messages`'ten senkron cikarilir) — oturumsuz calistirmalar dahil HER zaman dolu tek kaynak budur.

### Skill script iki sessiz cokme yolu (Faz 65 oncesi, Aile W)

- **🚨 Skill script: korumasiz `StandardInput.Close()` + JSON'a cevrilmemis denetim `after`'i ikisi de sessizce coker** (Faz 65 oncesi, Aile W): `Close()`'un ic flush'i `Pipe is broken` firlatip TUM calistirmayi cokertebilir — kendi `catch(IOException)`'ina alinir. `jsonb` sutununa JSON'a CEVRILMEDEN yazilan red nedeni Postgres `22P02` verir ve HER iz kaybolur. Ikisi de gercek alt surec+Postgres gerektirir; sahte `IAuditLog` YAKALAMAZ. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## `docs/hafiza/openai-saglayici.md`'den — Faz 77 bütçe rahatlatması

### `RawRepresentationFactory` uzerine yazma olcumu (Anthropic)

- **🚨 Bir SDK'nin ham gosterimini kullanirken alanin uzerine yazilip yazilmadigini OLC.** `ChatOptions.RawRepresentationFactory` ile verilen nesneyi Anthropic adaptoru **oldugu gibi kullanir** ve `model`/`max_tokens` alanlarinin uzerine yazmaz. Olculdu: yer tutucu `Model = "PLACEHOLDER-MODEL"` ile gonderilen istek gercekten o adla gitti (`404 not_found_error: model: PLACEHOLDER-MODEL`). Bu yuzden `AnthropicProviderSettingsChatClient` model adini ve token sinirini kendisi yazar. Varsayim yerine bir yer tutucu degerle gercek cagri yapin.

### `Azure.AI.OpenAI` surum kaymasi olcumu (K-211)

- **🚨 Bir SDK baska bir SDK'nin tipini genisletiyorsa, surum kaymasini CALISMA ANINDA olcun.** `Azure.AI.OpenAI` 2.1.0, `OpenAI` **2.1.0**'a karsi derlendi; biz `OpenAI` **2.12.0** kullaniyoruz. NuGet cakismayi sessizce cozer, `dotnet build` **sifir uyari** verir — ama `Azure.AI.OpenAI.Chat.AzureChatExtensions`'in istek tarafi metotlarinin TAMAMI (`AddDataSource`, `GetDataSources`, `SetNewMaxCompletionTokensPropertyEnabled`) `MissingMethodException: 'OpenAI.Chat.ChatCompletionOptions.get_SerializedAdditionalRawData()' bulunamadi` atar. Derleme yesilligi burada hicbir sey kanitlamaz. Bu yuzden `Tracon.Azure` o yuzeye hic dokunmaz ve **hicbir `ProviderSettings` anahtari sunmaz** (K-211).

### `UseOpenAICompatible()` yapilandirma bolumu farki (2026-08-06, Faz 33, K-249)

- **🚨 `UseOpenAI()` sabit `Tracon:Providers:OpenAI` bölümüne bağlıdır, `UseOpenAICompatible()` DEĞİLDİR** (2026-08-06, Faz 33, K-249): ikincisi ayarları KODDA alır (`o.ApiKey = configuration["OpenRouter:ApiKey"]` gibi rastgele bir kaynaktan) — sabit bir bölüm yolu yoktur. `OpenAIModelProvider`'ın teşhis raporu (`IModelProviderConfigurationDiagnostics`) bu farkı `configurationSectionKey: string?` parametresiyle ayırt eder; `UseOpenAICompatible()` `null` geçer ve o sağlayıcı için hiçbir `ConfigurationDiagnostic` üretilmez. Sabit bir bölüm varsayıp hep aynı anahtarı raporlamak yanlış anahtar adı gösterirdi.

### Baglanti hatasi istisna zinciri — olculen dort katman

- **🚨 Gercek bir baglanti hatasi tek bir istisna DEGIL, IC ICE bir zincirdir; en disi asla tahmin ettigin tip DEGILDIR.** `faz-denetim`'in bagimsiz denetcisi olctu: gercek `OpenAI` 2.12.0 istemcisini dinlemeyen bir porta baglamak `System.AggregateException` ("Retry failed after 4 tries...") firlatiyor; onun `InnerException`'i `System.ClientModel.ClientResultException` ("Connection refused (...)" — HTTP durumu YOK, cunku hic yanit alinmadi); ONUN da `InnerException`'i `System.Net.Http.HttpRequestException`, ONUN da `System.Net.Sockets.SocketException`. Toplam 4 kez tekrarlanir (4 deneme). K-296'nin "resmi SDK'lar HttpRequestException firlatmaz, tahmin etme, olc" dersi bir katman DAHA derine uygulanmali: en disi de tahmin edilemez, ZINCIRIN TAMAMI gezilmelidir (`AggregateException.InnerExceptions` + `Exception.InnerException` ozyinelemeli).

### `ClientResultException` mesaj bicimi ve siniflandirma sirasi

- **Sinif adi eslemesi TEK BASINA yetmez — mesaj metni sinif kararini DEGISTIRIR.** `ClientResultException`'in mesaji gercek bir HTTP yaniti ALINDIYSA "HTTP {kod} (...)" bicimindedir (olculdu: bir 503 icin `"HTTP 503 (...)"`); HIC yanit alinamadiysa (baglanti reddi gibi) HTTP onekini TASIMAZ, yalnizca ham hata metnini tasir (`"Connection refused (...)"`). Bu, "mesajda HTTP durumu YOKSA sinif tipi kendisi sinyal olur" seklinde bir SONRAKI-ADIM kuralini guvenli kilar: 401/403 gibi kimlik dogrulama hatalari HER ZAMAN "HTTP 401/403" metniyle gelir (olculmustur), bu yuzden durum-metni denetimi ONCE calisirsa, tip-tabanli "baglanti hatasi" varsayimi SONRA calistiginda yanlislikla bir kimlik hatasini yeniden denemez.

## `docs/hafiza/maf-api.md`'den — Faz 77 bütçe rahatlatması

### MAF OpenAI storage arayuzlerinin erisilebilirligi (2026-08-02)

- **🚨 MAF'ın OpenAI `storage` arayüzleri `internal`** (2026-08-02): `IConversationStorage`, `IAgentConversationIndex`, `IResponsesService`, `IResponseExecutor` — dördü de `svcPublic=False`; `AddOpenAIResponses()` bunları `TryAddSingleton` ile kaydeder ama tüketici tipi **adlandıramaz**. Yeni bir MAF genişleme noktası kullanmadan önce tipin **public** olduğunu doğrula — `TryAdd` kaydı görmek yetmez.

### MAF alt agent cagrisinda `options = null` (2026-08-02, Faz 12)

- **🚨 MAF alt agent'i `options = null` ile cagirir** (2026-08-02, Faz 12): `BackgroundAgentsProvider`'in `background_agents_start_task` tool'u alt agent'i cagirirken hicbir `AgentRunOptions` gecmez. Agac bilgisi gelen ayarlardan okunamaz; sarmalayici ambient `scope`'tan okuyup `TraconRunOptions` nesnesini kendisi kurar. Bir `AIContextProvider`'in actigi tool'dan tetiklenen her cagride ayni varsayim gecerlidir.

### MAF Harness + tool cagrisi (K-053)

- **🚨 `Microsoft.Agents.AI.Harness` + tool çağrısı = kırık akış** (K-053): Harness'lı bir agent tool çağırınca fonksiyon hiç çalışmaz, akış `done` olmadan kesilir ve sonraki tur OpenAI'dan `HTTP 400` alır. Düz `ChatClientAgent` temiz çalışır — kusur MAF'ın onay-bağlama zincirindedir, Tracon kodunda değil. Ölçüm: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `AIContextProvider` alt sinifi yazma reçetesi (Faz 22)

- **Kendi `AIContextProvider` alt sinifini yazmak icin** (Faz 22, ornek `McpResourceContextProvider`): taban ctor'un uc filtre parametresinin de varsayilani vardir — `: base()` ile hicbir sey verme. Ezilecek metot `protected virtual ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)`; `= default` KOYULMAZSA `MA0061` hatasi verir. `AIContext { Instructions = "..." }` donerek metni baglama enjekte eder.

### `ChatClientAgentRunOptions` sealed kisiti (Faz 47)

- **`ChatClientAgentRunOptions` `sealed`dir; `TraconRunOptions` ondan TÜREYEMEZ** (Faz 47): çalıştırma başına `ChatOptions` (model/tool bindirmesi) ile Tracon'in kendi çalıştırma ayarları BİRLİKTE kullanılamaz. Bu yüzden bindirme çalıştırma anında değil **tanım düzeyinde** yapılır: `AgentDefinition` kopyalanır, üzerine yazılır ve `Compile(definition, callable, toolTransform)` ile yeniden derlenir (sonuç `CompiledAgentCache`'e GİRMEZ).

### `AgentFileStore` yalitim sinirlari (2026-08-18, F-105/K-434)

- **🚨 `AgentFileStore` süreç genelinde TEK ve paylaşılandır; yalıtım sarmalayıcıdadır** (2026-08-18, F-105/K-434): kiracı öneki yetmez — aynı kiracının iki agent'ı da aynı alt ağacı paylaşır ve biri `EnableFileMemory` ile yazdığını diğeri `EnableTextSearch` ile bulur. Önek `{tenantId}/{agentName}`'dir. Agent adı **derleme anında** bilinir (`CompiledAgentCache` anahtarı zaten taşır), bu yüzden `AsyncLocal` gerekmez — beşinci bir ambient kapsam denemesinden kaçınıldı. Oturum boyutu bilerek yalıtılmaz: dosya belleği agent düzeyinde bir bellektir.

### `CreateDeclaration` ile declaration-only tool (2026-08-18, F-108/K-436)

- **`AIFunctionFactory.CreateDeclaration(name, description, jsonSchema, returnJsonSchema)` doğrudan `AIFunctionDeclaration` döndürür** (2026-08-18, F-108/K-436): sahte gövde kurup `.AsDeclarationOnly()` ile atmaya GEREK YOK. Ölçüldü: `is AIFunction` → `false`, `is AITool` → `true`; `FunctionInvokingChatClient` çağrıyı `FunctionCallContent` olarak döndürür, **çalıştırmaz** — declaration-only tool kurmanın en kısa yolu budur.

### `FunctionResultContent` rolu (2026-08-18, F-108/K-437)

- **🚨 İstemciden/çağırandan gelen bir `FunctionResultContent` `ChatRole.Tool` altında gönderilir, `ChatRole.User` DEĞİL** (2026-08-18, F-108/K-437): `ToolApprovalResponseContent` (onay yanıtı, FARKLI bir içerik tipi) bu depoda `ChatRole.User` altında gönderiliyor (`ToolApprovalResolver`, `ApprovalResumeJobHandler`) — bu ikisini karıştırıp `FunctionResultContent`'i de `ChatRole.User` ile göndermeye kalkma. Gerçek bir OpenAI çağrısıyla ölçüldü: `new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, sonuç)])` turu tamamlıyor; `tool_call_id` eşleşmesi rol bazlı çalışıyor.

## `docs/hafiza/sql-saglayicilari.md`'den — Faz 77 bütçe rahatlatması

### NULL benzersizliginin saglayici bazli tersligi (K-184)

- **🚨 NULL benzersizligi TERS calisir** (K-184): PostgreSQL'de NULL hicbir NULL'a esit degildir → `COALESCE`'li ifade indeksi gerekiyordu. SQL Server NULL'lari ESIT sayar → duz `UNIQUE` yeter. Ama ayni kural `jobs (schedule_id, scheduled_for)` kisitinda ters tarafa duser: SQL Server ikinci bir zamanlamasiz isi engellerdi, bu yuzden orada kisit `WHERE schedule_id IS NOT NULL` filtreli benzersiz indekstir. Sorgu tarafinda eslesme `ISNULL(c, N'') = ISNULL(@p, N'')` ile yazilir; `@p` NULL iken duz `=` UNKNOWN dondururdu.

### `QualifyTable` ve korelasyonlu alt sorgular (2026-08-06, Faz 36)

- **🚨 `EXISTS`/`NOT EXISTS` korelasyonunda BARE tablo adı YAZMA, `QualifyTable(name)` kullan (2026-08-06, Faz 36, K-259)**: `WHERE er.id = eval_case_results.eval_run_id` PostgreSQL/SQL Server'da calisir (bare ad aliassiz FROM'u da bulur) ama SQLite'ta gercek nesne `onek+ad` bitisigidir (K-193) ve bare ad HICBIR ZAMAN eslesmez — "no such column", yalniz CALISMA ANINDA. `RetentionTargetRegistry` (Faz 25) bunu 3 hedefte tasiyordu, Faz 36'nin SQLite testi yakaladi.

### Kimlik bazli toplu silme ile cutoff yolunun ayriligi

- **Kimlik bazlı toplu silme, `IRetentionStore`'un cutoff-tabanlı arayüzünü YENİDEN KULLANMAZ; paralel bir kayıt (`DataSubjectTargetRegistry`) yazılır (2026-08-18, Faz 64)**: yapı farklı (tek `@cutoff` yerine çok parametreli `IN`/`EXISTS`, tabloya göre değişen topoloji — `responses`'ın `tenant_id`'si bile yok, `sessions` üzerinden EXISTS ile doğrulanır). Ortak olan yalnız K-198'in "tek kayıt, üç dialekt şablonu" deseni.

### `__migrations` sema yukseltmesi (Faz 67, K-475)

- **🚨 `__migrations` defterinin KENDI semasini degistiren islem, K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ALTER TABLE ... ADD set_name` sonrasi AYNI iste `InsertMigration`'in `set_name` referansli SABIT metni "Invalid column name" verir ve `EXEC` ile de SARILAMAZ. Cozum: dongu BASLAMADAN ONCE calisan `SqlDialect.UpgradeMigrationsTableAsync`. SQLite (K-278) bunu KODDA rebuild ile yapar. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `runs` ordinal sirasi ve kume sayisi (2026-08-19, Faz 68)

- **🚨 `runs` gibi ordinal okunan bir tabloya sütun eklerken sıra ÜÇ dialektte de sona eklenir** (2026-08-19, Faz 68): `SqlRunStore.ReadRun` sabit konumdan okur ve üç `runColumns` metni birebir aynı sırayı taşımak ZORUNDADIR. Faz 68 sonunda son ordinal **51**'dir (40-41 attribution · 42-45 token kırılımı · 46 cache maliyeti · 47-50 ağaç token · 51 ağaç cache maliyeti). Aynı kural `SelectRunStatistics`'in sonuç kümeleri için de geçerlidir: bugün **sekiz** küme var ve yenisi SONA eklenir.

### UPSERT'te `COALESCE` korumasi (2026-08-19, Faz 68, K-486)

- **🚨 UPSERT'te bir alanı düz üzerine yazmak, ONU DOĞRU BİLEN yazımı silebilir** (2026-08-19, Faz 68, K-486): kuyruğa alınmış `run` (Faz 46) `StartRunAsync`'i iki kez çağırır — yer tutucu satır HTTP isteği içinde (kullanıcı BİLİNİR), sonra arka plan işçisi (HTTP'ye bağlı bir bağlam `null` döner). `user_id = EXCLUDED.user_id` atfı SİLERDİ; `COALESCE(EXCLUDED.user_id, user_id)` korur. `InMemoryRunStore` aynı davranışı kodda tekrarlar. Bir alan "set → unset" yönünde MEŞRU olarak değişmiyorsa `COALESCE` her zaman doğrudur.

### `CompleteAsync` erken-don korumasi (Faz 70, K-493) — ikinci tur

- **🚨 `RunEventWriter.CompleteAsync`'in USTUNDEKI `if (IsDisabled) return;` koruması, depo DAHA ONCE basarisiz olduysa kapanis olayinin (RunCompleted/RunFailed) HIC URETILMEMESINE yol acar** (2026-08-19, Faz 70, K-493): `IRunEventSink` eklenince bu koruma **sink'i de** terminal olaydan mahrum birakiyordu — depo ve sink BAGIMSIZ olmali kuralinin ihlaliydi. Koruma kaldirildi; yalniz `_store.CompleteRunAsync` cagrisi `IsDisabled`'a bagli kaldi (kapanis olayi HER ZAMAN uretilir ve sink'lere dagitilir). Yeni bir "erken don" optimizasyonu eklerken ayni soruyu sor: bu optimizasyon YALNIZ depo icin mi, yoksa depo-DISI bir tuketiciyi de sessizce susturuyor mu?

### `InputCost + OutputCost` sinif taramasi (Faz 68, K-483) — ikinci tur

- **🚨 Elle yazilmis `InputCost + OutputCost` toplami, ucuncu bir maliyet terimi eklendigi an SESSIZCE eksik raporlar — bagimsiz denetim YEDI yerde birden buldu** (2026-08-19, Faz 68, K-483): cache ucreti `input_cost`'un ALT KUMESI degil UCUNCU terimidir. Etki teorik degil — **maliyet tavani olan bir kiraci tavani asabilirdi**. Kural: `RunCost.Total()`/`RunTreeCost.Total()` "bu run ne tuttu" sorusunun TEK cevabidir; `RunCost`'a alan eklerken `Total()`'i guncelle ve `grep -rn "InputCost ?? 0" src/` ile sinifi tara. Kapi: `Every_cost_total_includes_the_cache_charge`. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `AgentRunScope.SessionId` kapsami (Faz 28, K-217) — ikinci tur

- **`AgentRunScope.SessionId` tool'un urettigi icerigin sahibidir** (2026-08-05, Faz 28, K-217): bir tool `AgentSession`'a erisemez; oturumsuz yazilan ek, saklama politikasi tarafindan **sahipsiz** sayilip silinir (`session_id IS NULL`). Kapsamdaki kimlik `runs.session_id`'den GENIS tanimlidir — alt calistirma MAF oturumu almaz ama icerik yine kok oturuma aittir. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `secret` filtresi cogul/tekil ayrimi (Faz 9, K-081) — ikinci tur

- **🚨 `secret` filtresinde alt dize eşlemesi çoğul/tekil ayrımı gözetmezse yanlış alanları gizler** (Faz 9, K-081): gerçek bir `agent.create` denetim kaydında `model.maxOutputTokens` `"***"` ile gizlenmişti — "token" fragmanı "Tokens"ı da eşliyordu. Birim testleri yakalamadı (sentetik veri gerçek `AgentDefinition` şeklini taşımıyordu). Çözüm: `AuditSecretFilter.IsSecretKey`, "token" eşleştiğinde anahtar "tokens" (çoğul) içeriyorsa eşleşmeyi iptal eder.

### `ITenantStore` kayit boslugu (Faz 35, K-257) — ikinci tur

- **`ITenantStore` kaydi zorunlu degildir — kayitsiz bir kiracinin verisi `ITenantStore.ListAsync()` ile YAPILAN bir taramada GORUNMEZ** (2026-08-06, Faz 35): `IQuotaStore`'da "tum kiracilari listele" yoktur, yalniz `ListAsync(tenantId)` (tek kiracili). `QuotaUsageObserver` bu yuzden yalniz KAYITLI kiracilari tarar; `QuotaEnforcer` etkilenmez (o zaten `ITenantContext.TenantId`'den tek bir kiraciyi bilir). Çapraz kiraci bir rapor/gosterge yazarken bu bosluk unutulmamali — K-257.

### `SaveAsync` yalniz basari yolunda (Faz 45, K-300) — ikinci tur

- **🚨 `ISessionStore.SaveAsync` YALNIZ basari yolunda cagrilir; basarisiz calistirmanin oturumu HIC kaydedilmez** (2026-08-07, Faz 45, K-300): `catch` bloklari onu cagirmaz, bu yuzden oturum/`ChatHistoryProvider` uzerinden girdi metni okuyan tasarim basarisiz calistirmalarda hep bosa cikar. Girdi metni `RunEventWriter.StartAsync`'in yazdigi `RunStarted.Text`'ten okunur — HER zaman dolu tek kaynak budur. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### Sayacta `null` ile `0` ayrimi (Faz 68, K-482) — ikinci tur

- **🚨 Bir saglayici sayacinin `null` gelmesi ile `0` gelmesi AYRI bilgidir** (2026-08-19, Faz 68, K-482): `0` "olculdu, yoktu" IDDIASIDIR ve susan her saglayici icin kendinden emin bir %0 cache isabet orani uretir. Uc toplama noktasi: `MergeUsage` (`AddOrNull`), `CompactionUsageAccumulator` (sayac basina "bildirildi mi" bayragi), `TreeUsage` (`COALESCE(...,0)` KASITLI uygulanmaz). Fiyat tarafinda da gecerli: sayac bildirilmediyse ucret `0m` degil `null` olur.

### `FallbackRetryClassifier` ilk uygulamasi — ikinci tur

- **Bir istisna siniflandiricisi yazarken `exception.InnerException`'i KONTROL ETMEDEN "tip adi eslesmedi -> retry etme" sonucuna varma.** `FallbackRetryClassifier`'in ilk uygulamasi tam olarak bu hatayi yapti: yalniz en disi istisnayi (`AggregateException`) kontrol ediyordu, mesaji da tipi de eslesmedigi icin "retryable degil" diyordu — canli bir kesintide ILK `FailureThreshold` istegin HEPSI kullaniciya cIPLAK hata olarak dusuyordu (devre kesici acilana kadar). Duzeltme: `Flatten(exception)` yardimcisi (kendisi + `InnerException` zinciri + her `AggregateException` kolu) UZERINDE gez, HER ADIMDA ayni kural setini uygula.

### Saglayici ayar okuma yardimcisi (Faz 26) — ikinci tur

- **Sağlayıcıya özgü ayarlar tek yardımcıdan okunur** (2026-08-05, Faz 26): `Abstractions/Agents/ModelProviderSettings.cs` doğrulama + tipli okuma yapar. Her sağlayıcı yalnız önek sabitini ve desteklenen anahtar listesini (`*ProviderNames.SupportedSettings`) yazar. Ayarlar `ChatOptions.RawRepresentationFactory` ile gönderilir; her paketin kendi `*ProviderSettingsChatClient` dekoratörü vardır — **`Tracon.Azure` hariç**, o hiç ayar sunmaz ve dekoratörü yoktur (K-211).

### Azure yonetilen kimlik ve `scope` metni (K-210) — ikinci tur

- **Yonetilen kimlik `Azure.Identity` GEREKTIRMEZ.** `AzureOpenAIClient(Uri, Azure.Core.TokenCredential, AzureOpenAIClientOptions)` kurucusu vardir ve `TokenCredential` `Azure.Core` derlemesindedir. Tracon yalniz `Azure.Core`'a baglanir; `Func<TokenCredential>` tuketiciden gelir (K-210). Token `scope`'u `AzureOpenAIAudience.AzurePublicCloud` = `https://cognitiveservices.azure.com/.default` — bu deger zaten tam `scope` metnidir, `/.default` eklenmez.

### `AIContextProvider` alt sinifi (Faz 22) — ikinci tur

- **Kendi `AIContextProvider` alt sinifini yazmak icin** (Faz 22, ornek `McpResourceContextProvider`): `: base()` ile hicbir filtre verme (ucunun de varsayilani var). Ezilecek metot `protected virtual ValueTask<AIContext> ProvideAIContextAsync(InvokingContext, CancellationToken = default)` — `= default` KOYULMAZSA `MA0061`. `AIContext { Instructions = "..." }` metni baglama enjekte eder. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `FunctionResultContent` rolu (F-108/K-437) — ikinci tur

- **🚨 Istemciden gelen bir `FunctionResultContent` `ChatRole.Tool` altinda gonderilir, `ChatRole.User` DEGIL** (2026-08-18, F-108/K-437): `ToolApprovalResponseContent` (FARKLI bir icerik tipi) bu depoda `ChatRole.User` altinda gonderilir — ikisini karistirma. Gercek OpenAI cagrisiyla olculdu: `new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, sonuc)])` turu tamamlar. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `AgentFileStore` yalitimi (F-105/K-434) — ikinci tur

- **🚨 `AgentFileStore` surec genelinde TEK ve paylasilandir; yalitim SARMALAYICIDADIR** (2026-08-18, F-105/K-434): kiraci oneki YETMEZ — ayni kiracinin iki agent'i alt agaci paylasir. Onek `{tenantId}/{agentName}`'dir; agent adi **derleme aninda** bilinir, bu yuzden `AsyncLocal` gerekmez (besinci ambient kapsam denemesinden kacinildi). Oturum boyutu bilerek yalitilmaz. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `.JsonSchema` zincirdeki konumu (F-108/K-435) — ikinci tur

- **🚨 `AIFunctionDeclaration`'ın kendisi `.JsonSchema` taşır, ama çıplak `AITool` tabanı TAŞIMAZ** (2026-08-18, F-108/K-435): bir tool sözleşmesini `AIFunction`'dan genişletirken hedef `AITool` değil `AIFunctionDeclaration` olmalı — `AIFunction : AIFunctionDeclaration : AITool` zincirinde şema yalnız orta katmanda tanımlı. `AITool`'a genişletmek `.JsonSchema` erişiminde `CS1061` ile patlar (ölçüldü).

### `ChatClientAgentRunOptions` sealed (Faz 47) — ikinci tur

- **`ChatClientAgentRunOptions` `sealed`dir; `TraconRunOptions` ondan TUREYEMEZ** (Faz 47): calistirma basina `ChatOptions` bindirmesi ile Tracon'in kendi ayarlari BIRLIKTE kullanilamaz. Bindirme bu yuzden **tanim duzeyinde** yapilir: `AgentDefinition` kopyalanir, uzerine yazilir, yeniden derlenir (sonuc `CompiledAgentCache`'e GIRMEZ). Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### Responses API + ChatHistoryProvider (K-030) — ikinci tur

- **🚨 Responses API + `ChatHistoryProvider` = çalışma anı hatası** (K-030): `AsIChatClient(ResponsesClient, model)` sunucu tarafı `storage`'i açık bırakır; `ChatClientAgent` `Only ConversationId or ChatHistoryProvider may be used, but not both` atar. **Yalnızca `UsePostgreSql()` açıkken** görülür. Çözüm `AsIChatClientWithStoredOutputDisabled(model)`.

### NULL benzersizliginin saglayici bazli tersligi (K-184) — ikinci tur

- **🚨 NULL benzersizligi saglayicilar arasinda TERS calisir** (K-184): PostgreSQL'de NULL hicbir NULL'a esit degildir (`COALESCE`'li ifade indeksi gerekir); SQL Server NULL'lari ESIT sayar (duz `UNIQUE` yeter). Ayni kural `jobs (schedule_id, scheduled_for)`'da ters tarafa duser — orada kisit `WHERE schedule_id IS NOT NULL` filtreli benzersiz indekstir. Sorgu tarafinda eslesme `ISNULL(c, N'') = ISNULL(@p, N'')` ile yazilir; `@p` NULL iken duz `=` UNKNOWN dondururdu. Vakalarin tamami: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### UPSERT `COALESCE` korumasi (Faz 68, K-486) — ikinci tur

- **🚨 UPSERT'te bir alani duz uzerine yazmak, ONU DOGRU BILEN yazimi silebilir** (2026-08-19, Faz 68, K-486): kuyruga alinmis `run` `StartRunAsync`'i IKI kez cagirir — once HTTP isteginde (kullanici BILINIR), sonra arka plan iscisinde (`null`). `user_id = EXCLUDED.user_id` atfi SILERDI; `COALESCE(EXCLUDED.user_id, user_id)` korur (`InMemoryRunStore` ayni davranisi kodda tekrarlar). Bir alan "set → unset" yonunde MESRU degismiyorsa `COALESCE` her zaman dogrudur. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `__migrations` sema yukseltmesi (Faz 67, K-475) — ikinci tur

- **🚨 `__migrations` defterinin KENDI semasini degistiren islem, K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ALTER TABLE ... ADD set_name` sonrasi AYNI iste `set_name` referansli SABIT metin "Invalid column name" verir ve `EXEC` ile SARILAMAZ. Cozum: dongu BASLAMADAN ONCE calisan `SqlDialect.UpgradeMigrationsTableAsync` (SQLite bunu K-278 ile kodda rebuild eder). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `runs` ordinal sirasi (Faz 68) — ikinci tur

- **🚨 `runs` gibi ordinal okunan bir tabloya sutun eklerken sira UC dialektte de SONA eklenir** (2026-08-19, Faz 68): `SqlRunStore.ReadRun` sabit konumdan okur ve uc `runColumns` metni birebir ayni sirayi tasimak ZORUNDADIR. Faz 68 sonunda son ordinal **51**'dir. Ayni kural `SelectRunStatistics`'in sonuc kumeleri icin de gecerlidir (bugun **sekiz** kume; yenisi SONA eklenir). Kirilim: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### Kiraci yuklemi ve saklama hedefleri (Faz 41, K-279) — ikinci tur

- **🚨 Kiraci basina tanimlanan bir politika, kiraci suzgeci OLMAYAN bir veri duzlemiyle calisamaz** (Faz 41, K-279): `RetentionTargetDefinition.TenantPredicate`'i unutma — yanlis yuklem sessizce calisir ve BUTUN kiracilarin satirlarini siler. Kendi `tenant_id`'si olmayan hedef sahibine bakan `EXISTS` ile suzulur (korelasyon FULL NITELENDIRILMIS adla — K-259). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `TenantCoverageTests` kapsam kapisi (Faz 41, K-281) — ikinci tur

- **Kapsam kapisi: `TenantCoverageTests`** (2026-08-07, Faz 41): `Stores/` altindaki her public metot ya `TenantCoverageTests.Covered` tablosunda ya `[TenantAgnostic("gerekce")]` ile isaretli olmalidir. Yeni bir metot eklediginde build yesil kalir ama bu test duser. Muafiyet gerekcesi 40 karakterden kisa olamaz (ayri test). Yansima yalniz test projesindedir; oznitelik `Sql.Shared/Internal/` icinde ve `internal`'dir (K-281).

### `SqlQueriesBase` bos sorgu tuzagi — ikinci tur

- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz.** Ozellikler `{ get; protected set; } = string.Empty;`'dir; yazilmayan sorgu bos metin kalir ve hata yalnizca CALISMA ANINDA gorunur — derleme de test de kirilmaz. Sorgu sayisi saglayici sayisiyla carpiliyorsa desen degistirilir (K-198: tek kayit + `SqlDialect` sablon yontemi). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

### `ArrayContains` diyalekt eslemesi (Faz 64) — ikinci tur

- **`SqlDialect.ArrayContains(column, paramName)` eklendi (2026-08-18, Faz 64)**: "sütun bir dizi parametrenin içinde mi" için — `= ANY(events)` deseninin ayna yönü. PostgreSQL `col = ANY(@dizi)`; SQL Server/SQLite dizi JSON metnidir (K-182), `EXISTS (SELECT 1 FROM OPENJSON/json_each(@dizi) WHERE value = col)` ile eşlenir. `DataSubjectTargetRegistry` (Faz 64) dokuz hedefi bununla filtreler.

---

## `TagList` ile koşullu metrik etiketi (cekirdek-calistirma.md'den taşındı, 2026-08-22)

- **Metrik etiketini kosullu eklerken `TagList` kullan, `KeyValuePair<string,object?>[]` degil** (2026-08-03, Faz 19): `Counter<T>.Add`/`Histogram<T>.Record`'un `params KeyValuePair[]` asiri yuklemesi sabit uzunluklu dizi ister; bir etiketi (`tracon.agent.version`) yalniz deger varsa eklemek icin `System.Diagnostics.TagList` (struct, koleksiyon baslatici destekler) kullanilir — `TraconMetrics.RecordRun` bu deseni izler.

## Migration çakışması — K-540'ın ilk anlatısı (sql-saglayicilari.md'den taşındı, 2026-08-21)

## 🚨 Migration çakışması iki şekilde gelir; birini denemek yetmez (K-540)

`MigrationRunner` unique ihlalini sekiz kez yeniden deniyordu ama **deadlock**'u
`TraconException`'a sarıyordu. İkisi de aynı çarpışmadır: migration kilidi
şemaya kapsamlıdır (K-389), farklı şemaların ilk göçü veritabanı genelindeki
katalog nesnelerinde buluşur. Bedeli ölçüldü — dört tam koşumdan birinde beş SQL
Server case'i `fixture` ayağa kalkmadığı için düştü (error 1205).

Geçici durumu kalıcı hata gibi sunma: sunucu kurbanı **zaten** geri almıştır.
`SqlDialect.IsDeadlock` üçünü kapsar — SQL Server `1205`, PostgreSQL `40P01`,
SQLite `SQLITE_BUSY`/`SQLITE_LOCKED` (SQLite döngü tespit etmez, yazarları sıraya
sokar; **çağıran** aynı şeyi görür: reddedilen, tekrarlanınca geçen yazım).

Yeni bir yeniden deneme döngüsünde sor: bu `catch` yalnız `IsUniqueViolation`'a mı
bakıyor? Öyleyse deadlock oradan **ham** çıkar. Sınıf taraması `SqlAuditLog`'da
ikinci vakayı buldu; bedeli kaybolan bir denetim kaydıydı.

## Saklama önizlemesi neden ayrı bir `COUNT` değildir (sql-saglayicilari.md'den taşındı, 2026-08-21)

- **Preview'i ayri bir `COUNT` yerine gercek `DELETE`'i calistirip `ROLLBACK`/`COMMIT` ile ayirmak DAHA GUVENLI** (Faz 64): iki ayri sorgu seti zamanla sapar; tek dogruluk kaynagi kalir.

## Erken test-altyapisi maddeleri, Faz 29-41 (test-altyapisi.md'den taşındı, 2026-08-26)

- **🚨 Chromium'un sahte ses cihazi HIC SUSMAZ** (2026-08-05, Faz 29): `--use-fake-device-for-media-stream` surekli bir ton uretir. Sessizlik tespitine (VAD) dayanan bir E2E testi bu yuzden hicbir zaman tetiklenmez ve 30 sn'de zaman asimina ugrar — olculdu. Cozum bir test hilesi degil, urunun kendi ihtiyaciydi: elle kapatma dugmesi (bas-konus / gurultulu ortam) eklendi ve test onu tiklar. Mikrofon ayrica GUVENLI BAGLAM ister; `http://127.0.0.1:<port>` Chromium'da guvenilir sayilir, uzak bir HTTP adresi sayilmaz.
- **Sahte model saglayicisi artik `Tracon.Testing.FakeModelProvider`'dir — yeni bir test projesi kendi `IModelProvider` taklidini YAZMAZ** (2026-08-06, Faz 39): Bes ayri dosyaya kopyalanmis (`EchoModelProvider` ×2, `ScriptedModelProvider`, `RoutingModelProvider`, `Fakes/FakeModelProvider`) 523 satir birlestirildi. Her modelin KENDI sirali yanit kuyrugu vardir (`ForModel(id, cfg => cfg.CallsTool(...).RespondsWith(...))`); kuyruk BIR KEZ tuketilir, tukendikten sonra `EchoesUserMessage()`/`EchoesLastToolResult()` fallback'i devreye girer — mesaj gecmisi taranarak "hangi tool zaten cagrildi" ASLA cikarilmaz (eski Routing/ScriptedModelProvider'in yaptigi gibi). Ayni saglayicinin FARKLI modelleri (ornek: bir yonlendirici + devrettigi alt agent) BAGIMSIZ kuyruk ister — ayni model id'sini paylasmak testler arasi durum sizdirir. `TraconTestHost` (paket) ile FunctionalTests'in KENDI ic `TraconTestHost`'u (TestServer tabanli, `Infrastructure/` altinda) AYNI ada sahiptir — ayni dosyada ikisi de `using` edilirse `CS0104` (belirsiz referans) verir; `using FakeModelProvider = Tracon.Testing.FakeModelProvider;` tipi takma adla almak `using Tracon.Testing;` yerine cakismayi onler. Karar K-269.
- **`RunAssertions.ShouldHaveOutputContaining` akisli/akissiz ayrimina dikkat etmeli** (2026-08-06, Faz 39): bkz. `docs/hafiza/cekirdek-calistirma.md` — `MessageCompleted` yalniz akissiz `agent.RunAsync()` yolunda vardir, HTTP `/run` (SSE) yalniz `MessageDelta` uretir. Bu, depo ICI testlerin hicbirinde yakalanmadi (hepsi ya akissiz cagirdi ya da bu iddiayi hic kullanmadi) — yalniz depo DISINDAN paketlenmis nupkg'i kullanan gercek bir tuketici senaryosu yakaladi. **Ders**: yeni bir test paketi yayimlamadan once GERCEKTEN paketlenmis halini disaridan (ayri bir scratch projede, `NuGet.config` ile yerel beslemeye isaret ederek) dene — `ProjectReference` ile calisan bir ic test asla bu sinifta bir bosluk gormez.
- **Depo sozlesmeleri artik `TenantIsolationContract<TStore>`'tan turer** (2026-08-07, Faz 41): taban sinif hem ortak yasam dongusu tesisatini (`Store`, `CreateStoreAsync`, `InitializeAsync`/`DisposeAsync`, `OnDisposeAsync`) hem bes kiraci yalitimi testini tasir. Yeni bir sozlesme yazarken **kosum sinifi eklemek gerekmez**: var olan dort kosum (bellek ici + uc SQL) yalitim testlerini kendiliginden alir. Dort kanca yazilir: `SeedAsync`, `ExistsAsync`, `CountAsync` (zorunlu) ve `TryDeleteAsync` (silme sunmayan depoda `null` doner).
- **🚨 Kiraciyi PARAMETRE olarak almayan depolar icin iki depo ornegi KURULAMAZ** (2026-08-07, Faz 41, K-282): bellek ici depolar durumu ornek icinde tasir; iki ornek ayni arka uca bakmaz. Cozum `MutableTenantContext`: tek depo ornegi, cagrilar arasinda degisen kiraci. Sozlesme kancasinin ilk satiri `AmbientTenant.TenantId = tenantId;` olur.
- **🚨 Bellek ici depoyu kuran testte kiraci baglamini da ver** (2026-08-07, Faz 41): `new InMemoryRunStore()` varsayilan olarak `"default"` kiracisina baglanir. `RunRecordingAgent`'i baska bir `ITenantContext` ile kurup depoyu parametresiz olusturursan `QueryRunsAsync` BOS doner ve hata "test yanlis kurulmus" gibi degil "kayit yazilmamis" gibi gorunur. Faz 41'de 23 test bu sekilde kirildi; duzeltme `new InMemoryRunStore(tenantContext: ...)`.

## CA1305 ve kultur bagimli sevk edilen metin (Faz 153, K-720)

Kusur tr-TR bir makinede `tracon eval`'in "in 3,7 s" yazmasiyla gorundu;
gorunen yarisi buydu. Ayni sinif kalici bir `run` hatasina "0,004500" ve bir
`ProblemDetails` govdesine "%50" yaziyordu — ayni dagitim, uretildigi makineye
gore farkli okunuyor. Asimetri kusuru dogurmustu: GIRDI tarafi zaten
`CultureInfo.InvariantCulture` ile ayristiriyordu (`ParseOptionalDouble`),
cikti tarafi ortam kulturunu kullaniyordu.

Sinif taramasi ONCE alti yer buldu, sonra OLCUM ucunu eledi. tr-TR'de:
`0.0` → "3,7", `0.000000` → "1,500000", `P0` → "%50" (invariant "50 %") farkli;
`F0` → "3600" ve `0` → "3600" AYNI — bu iki belirtec hicbir kulturde basamak
gruplamaz. Bu yuzden ilk taramada "duzeltilen" uc `:F0` yeri
(`TimeoutAIFunction`, `ModelProviderCircuitBreaker`, `ModelProviderHealthCache`)
GERI ALINDI: orada kusur yoktu ve dokunmak gereksiz degisiklikti. Gercek uc yer
`AgentRunBudget`, `PreflightGate` ve `EvalCommand`'dir.

Analyzer bu sinifi kapatamaz. `.editorconfig`'e
`dotnet_diagnostic.CA1305.severity = warning` yazip tum solution derlendi:
sifir bulgu. Sebep, format belirtecli bir interpolasyonun `string.Format`
cagrisina degil `DefaultInterpolatedStringHandler`'a derlenmesidir; CA1305
"IFormatProvider'siz `string.Format`/`ToString`" arar ve boyle bir cagri yoktur.

Guard bu yuzden davranissaldir ve TEK bir gercek vaka kilitler
(`InvariantShippedTextTests`, `AgentRunBudget`'in maliyet dali; duzeltme geri
alinarak testin kirmizi oldugu dogrulandi). Diger iki yer bir host ve bir surec
sinirinin arkasindadir; onlari kapsayacak kultur kapsamli bir test yanindaki
paralel testlere sizardi. Kapsanmadiklari testin kendi yorumunda ACIKCA
yazilidir — hicbir sey kanitlamayan bir testle ortulmedi.
