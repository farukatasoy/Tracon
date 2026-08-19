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
`AgentPrismRunContext.Current?.AgentName`'den okur; arayuzde parametre yoktur
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
`net10.0` destekler (`NU1202`). `AgentPrism.Testing` mirasi
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
amaciyla `dotnet pack AgentPrism.slnx -c Release` cagiriyordu. Cozum 16 `src/`
paketinin yaninda 13 test projesi (Postgres/SqlServer/Sqlite container'li
entegrasyon testleri, Playwright E2E dahil) barindirir; `dotnet pack` bir cozum
dosyasi aldiginda HER proje icin Pack hedefini calistirir — paketlenemeyen
projelerde Pack no-op'tur ama Build ONA BAGIMLI oldugu icin YINE DE calisir.
Olculdu: Templates.Tests'in tek basina calismasi ~1 saat surdu; bunun buyuk
kismi bu gereksiz 13 test projesi derlemesiydi (`artifacts/package/release`
zaman damgalari bir `dotnet pack` cagrisinin 20-35 dakika surdugunu gosterdi).
Cozum: repo koku `AgentPrism.src.slnf` (yalniz 16 `src/` projesini listeleyen
bir cozum FILTRESI) eklendi; `dotnet sln <filtre>.slnf` `.slnx` formatini da
destekler (.NET 10 SDK ile dogrulandi). Warm pack ~30 saniyeye dustu, toplam
sure ~15 dakikaya (kalan sure gercek is: npm/frontend derlemesi + uretilen 3
projenin gercek NuGet restore'u).

### `wwwroot` + damga silindikten sonra arayuz derlemesi yarisir (2026-08-07, Faz 48)


K-050'nin kapatmadigi bosluk; iki kez yasandi. Belirti:
`ENOENT: ... unlink '.../wwwroot/assets/index-*.js'` ve
`npm run build exited with code 1`. K-050'nin cozumu zinciri
`BeforeTargets="DispatchToInnerBuilds"` ile **dis** derlemeye aldi, ama
`AgentPrismCollectFrontendAssets` hedefi "tek hedefle derlerken dis derleme
yoktur" durumunu da karsilamak icin zinciri **kendisi** calistirir. Solution
derlemesinde `AgentPrism.UI`'a farkli TFM'lerden referans veren projeler (meta
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
`AgentPrism.SqlServer.xml`, `AgentPrism.Sqlite.xml` ve `AgentPrism.PostgreSql.xml`
dosyalarinin HER BIRINE ayni `T:AgentPrism.MigrationRunner` doku kimligiyle
yazilir. `AddOpenApi()` kullanan bir tuketici 2+ SQL saglayicisini BIRLIKTE
referans verirse (`samples/AgentPrism.Api`'nin K-185 icin bilerek yaptigi gibi),
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

Bir tuketici kaydi (`AgentPrism.Testing.RunAssertions.ShouldHaveOutputContaining`)
yalniz `MessageCompleted`'e bakarsa HTTP uzerinden calisan her run icin
yanlislikla BOS cikti gorur. Testlerin kendisi degil, gercek bir dis tuketici
senaryosu (`docs/39-TEST-PAKETI.md` DoD'sindeki "depo disi tuketici" adimi)
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
`ErrorFingerprint.Compute` DEGIL (K-364 — o tip `AgentPrism.Core`'da
internal'dir, `AgentPrism.Sql.Shared` ona erisemez).

### MAF tool'a bos servis saglayici gecirir; `AIFunctionArguments.Services` kullanilamaz (Faz 28, K-218)

Tool govdesinde `arguments.Services` `EmptyServiceProvider`'dir.
`ChatClientAgentOptions` bir `Services` ozelligi tasimaz ve
`AsAIAgent(..., _services)` saglayicisi fonksiyon cagrisina AKMAZ. Tool
bagimliliklarini KURULUM aninda alin:
`services.AddSingleton(p => new AgentPrismToolRegistration(new BenimTool(p)))`.

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
olayi, `done` ile biter) — hata AgentPrism kodunda degil, Harness paketinin
onay-baglama zincirinde (`Microsoft.Agents.AI.ApprovalResponseBindingChatClient`).
Ornek kasitli olarak degistirilmedi, kusur belgelendi.

### `FunctionInvokingChatClient` tool istisnasini yutar (2026-08-07, Faz 47, K-309)

Olculdu: yeniden oynatma sirasinda firlatilan `ReplayToolMismatchException` HTTP
ucuna hic ulasmadi ve istek `200` dondu — istisna bir tool sonucuna cevrilir ve
dongu devam eder. Donguyu kesmenin yolu
`FunctionInvokingChatClient.CurrentContext.Terminate = true`'dur
(`FunctionInvocationContext.Terminate`, public); hata ise calistirma bittikten
sonra, model cagrisinin DISINDA firlatilmalidir. AgentPrism bunu
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

- **🚨 Linked-source (K-176) bir tipin `internal` isareti CROSS-ASSEMBLY sayim icin GUVENILMEZ** (2026-08-06, Faz 33, K-247): `MigrationHostedService`'in K-183 sayaci `internal SqlPersistenceRegistration` kullaniyordu; bu tip `AgentPrism.PostgreSql.dll` ve `AgentPrism.SqlServer.dll` icine AYRI AYRI derlenir ve CLR kimligi FARKLIDIR — `UsePostgreSql()` + `UseSqlServer()` birlikte cagrildiginda hicbir `MigrationHostedService` digerinin isaretini GOREMEZ ve cift kayit uyarisi hic tetiklenmez. Sayim/teshis Abstractions'da PAYLASILAN tek bir derlenmis tipe (`SqlPersistenceRegistrationMarker`) tasindi. Ayni tuzak: linked-source icindeki herhangi bir `internal` tipi `IEnumerable<T>` ile SAYMAK istiyorsan, T Abstractions'da olmali.

- **🚨 `IDENTITY` var olan bir tabloya `ALTER TABLE ... ADD` ile EKLENEMEZ** (2026-08-18, Faz 64): `IDENTITY` yalnız `CREATE TABLE` anında tanımlanabilir; PostgreSQL'in `ADD COLUMN ... GENERATED BY DEFAULT AS IDENTITY`'sinin SQL Server karşılığı yoktur. Yerine: ayrı bir `CREATE SEQUENCE {schema}.x AS bigint`, sonra `ALTER TABLE ... ADD col bigint NOT NULL DEFAULT (NEXT VALUE FOR {schema}.x)` — yeni her satır varsayılan değer olarak sıradaki sayıyı alır, `IDENTITY` ile aynı garantiyi (veritabanının atadığı, artan bir sayaç) verir. 🚨 Test altyapısı da güncellenmelidir: şema silme sırası TABLOLAR → SEQUENCE'lar → `DROP SCHEMA` olmalıdır (`SqlServerTestContext.DropSchemaAsync`), aksi halde "Cannot drop schema because it is being referenced by object" hatası alınır.

- **🚨 CAGIRANIN VERDIGI bir metin tek basina birincil anahtar olamaz** (Faz 41, K-278): `sessions.id` butun kiracilar arasinda benzersizdi ve `ON CONFLICT (id) DO UPDATE` bir kiracinin digerinin oturumunu UZERINE YAZMASINA izin veriyordu. Anahtar `(tenant_id, id)` oldu. **Kural**: kimlik cagirandan geliyorsa anahtar kiraciyi de icermelidir; uuid v7 (K-015) bu tuzagi tasimaz. SQLite birincil anahtari DEGISTIREMEZ — tablo yeniden kurulur, veri tasinir, indeksler ELLE yeniden olusturulur.

- **🚨 Kiraci basina tanimlanan bir politika, kiraci suzgeci OLMAYAN bir veri duzlemiyle calisamaz** (Faz 41, K-279): `RetentionTargetRegistry` hicbir hedefte `tenant_id` tasimiyordu; bir kiracinin saklama politikasi BUTUN kiracilarin satirlarini siliyordu. `RetentionTargetDefinition` artik `TenantPredicate` tasir; kendi `tenant_id` sutunu olmayan uc hedef sahibine bakan `EXISTS` ile suzulur (korelasyon FULL NITELENDIRILMIS adla — K-259). Yeni hedef eklerken `TenantPredicate`'i unutma; yanlis yuklem sessizce calisir.

- **Preview'ı ayrı bir `COUNT` yerine gerçek `DELETE`'i çalıştırıp `ROLLBACK`/`COMMIT` ile ayırmak DAHA GÜVENLİ (2026-08-18, Faz 64)**: iki ayrı sorgu seti zamanla sapar; `SqlDataSubjectStore` `dryRun`'da her zaman `ROLLBACK`, değilse geri çağrı başarılıysa `COMMIT` eder — tek doğruluk kaynağı.

- **🚨 Depoda `LoadAllAsync` (kapsamdaki TUM satirlari cekip C#'ta filtreleme) deseni bulursan supheyle yaklas** (Faz 51): `SqlAgentFileStore` prefix+glob'u client-side filtreliyordu; SQL'e indirildi (`LIKE ... ESCAPE`, onek `EscapeLikeLiteral` ile kacislanir). SQL Server/SQLite regex TASIMAZ — nihai eslesme HER ZAMAN .NET `Regex` ile istemcide kalir, SQL yalniz on daraltmadir. `SqlDialect.IsInvalidRegexError` PostgreSQL ARE'sinde gecersiz bir .NET desenini yakalayip on suzgecsiz yeniden dener.

## Cekirdek calistirma — Faz 68 vaka anlatilari

> `docs/hafiza/cekirdek-calistirma.md` butcesini asinca TASINDI. Kural ozeti
> alan dosyasinda kaldi.

- **🚨 Elle yazılmış `InputCost + OutputCost` toplamı, üçüncü bir maliyet terimi eklendiği an SESSİZCE eksik raporlar — bağımsız denetim beş yerde birden buldu** (2026-08-19, Faz 68, K-483): cache ücreti (`RunCost.CachedInputCost`) `input_cost`'un ALT KÜMESİ değil ÜÇÜNCÜ terimidir (`runs.input_cost` cache token'ını zaten dışarıda bırakır). SQL tarafı düzeltilmişti ama **çalışma anındaki** toplamların hiçbiri değildi: kota muhasebesi, `agentprism.run.cost` metriği, webhook özeti, workflow kotası, `ModelRunJudge`, `OnlineEvalSummaryService` ve arayüzdeki `run-comparison.tsx`. Etki teorik değil — **maliyet tavanı olan bir kiracı tavanı aşabilirdi**. Çözüm bir yardımcı metottur: `RunCost.Total()`/`RunTreeCost.Total()`, ve "bu run ne tuttu" sorusunun TEK cevabıdır. **Kural**: `RunCost`'a alan eklerken `Total()`'ı güncelle ve `grep -rn "InputCost ?? 0" src/` ile sınıfı tara. Kapı: `Every_cost_total_includes_the_cache_charge` (özet + zaman serisi + deney sonucu + ağaç, dört depoda birden).

- **🚨 Bellek içi store ile SQL store'un AYNI sorguya farklı yanıt vermesi sözleşme testinden kaçabilir — test o sorguyu hiç sormuyorsa** (2026-08-19, Faz 68, denetim 🔴#2): cache ücreti üç dialektin `GetTimeSeriesAsync`/`GetExperimentResultsAsync` sorgularına eklenmişti ama `InMemoryRunStore`'un karşılıklarına eklenmemişti; dört sözleşme koşumu da YEŞİLDİ çünkü `RunStoreContract` maliyeti yalnız `GetStatisticsAsync` ve `TreeCost` için soruyordu. Bir alanı "her yerde" eklediğini düşündüğünde, o alanı OKUYAN her depo metodunun sözleşme testinde bir iddiası var mı diye bak.

- **🚨 Ambient bir bağlamı bir kayıt yolunda DOĞRUDAN okuma — tüketicinin uygulaması fırlatabilir ve doğrulanmamış değer döndürebilir** (2026-08-19, Faz 68, denetim 🔴#4): `WorkflowRunner` attribution'ı `_attributionContext?.UserId` ile okuyordu; `RunRecordingAgent`'ın try/catch + sınır doğrulama + `Freeze` üçlüsünün hiçbiri yoktu. İki somut arıza: tüketici implementasyonu fırlatınca workflow `run`'ı ÖLÜRDÜ ("gözlemlenebilirlik işlevselliği bozmaz" ihlali), ve 200 karakterden uzun bir `userId` SQL Server insert'ini patlatıp `RunEventWriter`'ı devre dışı bırakarak **workflow'un TÜM kaydını sessizce kaybettirirdi**. Garantiler `RunAttributionReader.Read(...)`'e çıkarıldı; her kayıt yolu onu kullanır. Gürültülü ret sınırda kalır (HTTP `400`, `Begin` → `ArgumentException`).

- **🚨 `AgentDefinitionCompiler.Compile` TAMAMEN senkron; kiracı kimlik bilgisi çözümlemesi async `store` gerektirir — ikisi çelişince YENİ paralel async yol açıldı, mevcut sync yol DEĞİŞTİRİLMEDİ** (2026-08-19, Faz 65): `IModelProviderRegistry.CreateChatClient(ModelBinding)` (sync) hep global kimlik bilgisiyle çalışır ve DEĞİŞMEDİ; yeni `CreateChatClientAsync(ModelBinding, CancellationToken)` kiracı `store`'larını çözer. Aynı ikili desen `AgentDefinitionCompiler.Compile`/`CompileAsync` ve `CompiledAgentCache.GetOrAdd`/`GetOrAddAsync`'te tekrarlanır — sync üçlü GÖVDESİ paylaşılan bir `BuildAgent`/`BuildPipeline` yardımcısına taşınır, yalnız `IChatClient` üretim adımı ayrışır. Gerçek `run` yolu (`DefinitionStoreAgentSource`/`CodeAgentSource`) async'e taşındı; `AgentDefinitionValidator.CheckModel` de (zaten async `ValidateAsync` içinde) async'e geçti. `RunReplayService` da async yola taşındı — aksi halde bir tekrar oynatım (`replay`), orijinal `run`'ın kullandığı kiracı anahtarı yerine sessizce global anahtara dönerdi.

- **🚨 `AuditSecretFilter` alan ADINA bakar, DEĞERE değil — "apikey" fragmanı taşıyan her alan içerik zararsız olsa da `***` olur** (2026-08-19, Faz 65): `tenant_provider_bindings`'in `apiKeyConfigurationName`'ı K-059 gereği zaten bir DEĞER değil, yalnız yapılandırma anahtarının ADI — ama JSON alanı `"apiKeyConfigurationName"` olarak yazılınca "apikey" fragmanı eşleşti ve denetim izinde teşhis için asıl gereken bilgi (hangi anahtar adı ayarlandı) `***` oldu. Çözüm alanın kendisini değiştirmek değil, denetim özetinde farklı adlandırmaktır: `TenantProviderEndpoints.DescribeForAudit` alanı `configKeyName` yazar. Zararsız olduğu KANITLANMIŞ bir alanı denetime yazarken adında `apikey`/`authorization`/`token` (tekil)/`password`/`secret` geçmediğinden emin ol — geçiyorsa filtre teşhis değerini sessizce yok eder.

- **🚨 `JobRecord.Payload` atanmazsa `/api/jobs` TUM listeyi 500 ile dondurur** (2026-08-03, Faz 21): `JsonElement` bir struct'tir; atanmazsa `default` olur (`ValueKind = Undefined`) ve `JsonElementConverter.Write` onu serilestiremeyip `InvalidOperationException` firlatir. Etki tek bir isle sinirli degildir — liste ucunun tamami cokar. 1231 test yakalamadi (birim testleri kuyruga yaziyor ama HTTP'den serilestirmiyordu); ornek uygulamada `/api/jobs?kind=WebhookDelivery` cagrilinca ortaya cikti (K-166). **Kural**: yeni bir `JobRecord` ureten her kod yolu `Payload` atamalidir. Regresyon testi hem `ValueKind`'i hem gercek serilestirmeyi denetler.

- **🚨 Disaridan iptal, `RunRecordingAgent`'in KENDI `CancellationTokenSource`'una guvenir, gelen `cancellationToken`'a DEGIL** (Faz 32): `RunCore*Async` gelen token'i dogrudan iletmez; `CreateLinkedTokenSource(cancellationToken)` ile kendi kaynagini kurar, deftere onu yazar ve `cancellationSource.Token`'i kullanir. Sebep: gelen token'in KENDISI iptal edilemez — yalnizca ondan turetilen bir kaynak iptal edilebilir; aksi hâlde defterin `Cancel()`'i hicbir seyi etkilemezdi. `WorkflowRunner.ExecuteAsync` ayni deseni tekrarlar.

- **🚨 Skill script: korumasiz `StandardInput.Close()` + JSON'a cevrilmemis denetim `after`'i ikisi de sessizce coker** (2026-08-15, K-400 SONRASI bulundu, Aile W): `SkillScriptProcessRunner.WriteArgumentsAsync`'in `finally`'sindeki `Close()` `try/catch` DISINDAYDI — stdin okumadan cikan (`echo`) bir script'te `Close()`'un ic flush'i `Pipe is broken` firlatip TUM calistirmayi cokertiyordu. Ayrica `SandboxedSkillScriptRunner.DenyAsync` red nedenini `jsonb` sutununa JSON'a CEVIRMEDEN yaziyordu → Postgres `22P02`, HER `script.denied` izi kayboluyordu. Ikisi de gercek alt surec+Postgres gerektirir; sahte `IAuditLog` YAKALAMAZ. Duzeltme: `Close()` kendi `catch(IOException)`'ina alindi; `after` `JsonSerializer.Serialize` ile sarildi.

- **🚨 `__migrations` defterinin KENDI semasini degistiren islem, K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (2026-08-19, Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ALTER TABLE ... ADD set_name` sonrasi AYNI iste `InsertMigration`'in `set_name` referansli SABIT metni "Invalid column name" verir, `EXEC` ile de SARILAMAZ (HER migration'a otomatik eklenir). Cozum: `CreateMigrationsTable` gibi dongu BASLAMADAN ONCE calisan `SqlDialect.UpgradeMigrationsTableAsync`. SQLite (K-278) bunu KODDA rebuild ile yapar.

- **🚨 `ObservableGauge` geri cagirmasi es zamanlidir — icine `await` konamaz** (Faz 35, K-256): veritabani okuyan bir olcer `SemaphoreSlim` korumali, `TimeProvider` karsilastirmali bir onbellekle yazilir. `QuotaUsageObserver.Snapshot()` deseni: onbellegin yasini `QuotaUsageRefreshInterval` ile karsilastir, bayatladiysa BIR kez blok olarak tazele, tazeyse onbellekten don. `BackgroundService`/`PeriodicTimer` denendi ve test edilemez cikti — gerekce: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

- **`AgentPrismMetrics`'e yeni bir sayaç eklerken `RunRecordingAgent.CompleteAsync` icindeki cagriyi `scope.Depth`'e KOSULLU yapma — `RecordRun` gibi HER calistirmada (kok + alt) cagir** (2026-08-06, Faz 35): `RecordCost` cagrisi `if (scope.Depth == 0)` blogunun DISINDA durur; K-151'in "sayac yalniz KENDI maliyetini yayar" kuralini dogal olarak saglar (her `CompleteAsync` cagrisi zaten yalniz kendi `usage`'indan hesaplanmis `cost`'u gorur). Kota/olay yayini (`RecordQuotaAsync`/`PublishRunEventAsync`) ise KASITLI olarak yalniz kok'te calisir (agac bir kez sayilsin diye) — iki kural birbirine KARISTIRILMAMALI, farkli gerekceleri var.

- **🚨 Linked-source (K-176) tiplerin AYNI tam nitelikli adi, `Microsoft.AspNetCore.OpenApi`'nin XML yorum onbellegini CATISTIRIR** (Faz 40, K-276): 2+ SQL saglayicisini birlikte referans veren ve `AddOpenApi()` kullanan bir tuketicide `/openapi/v1.json` **500** doner (`An item with the same key has already been added`). K-247'nin XML-doc kardesi. Ayrinti ve durum: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md), `docs/ADAYLAR.md` F-76.

- **🚨 Tipi verilmemis `decimal` parametresi `decimal(18,0)` sayilir ve ONDALIK KISIM SESSIZCE KESILIR** (2026-08-04, Faz 23): butun para sutunlari `decimal(20,10)`'dur; `SqlServerDialect.AddDecimal` `Precision = 20`, `Scale = 10` yazar. Yazilmazsa maliyetler tam sayiya yuvarlanir ve hicbir test bunu yakalamaz — yalnizca gidis-donus testi yakalar (`SqlServerDialectTests.Maliyet_ondaligi_kesilmeden_gidip_gelir`).

- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz.** Ozellikler `{ get; protected set; } = string.Empty;`'dir; yazilmayan sorgu bos metin kalir ve hata yalnizca CALISMA ANINDA gorunur — derleme de test de kirilmaz. Sorgu sayisi saglayici sayisiyla carpiliyorsa desen degistirilir: Faz 25'te veri duzlemi icin 11 hedef × 3 saglayici = 99 elle sorgu yerine `Sql.Shared/Internal/RetentionTargetRegistry.cs` + `SqlDialect`'te 3 sablon yontemi secildi (K-198).
