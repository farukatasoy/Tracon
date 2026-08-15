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
