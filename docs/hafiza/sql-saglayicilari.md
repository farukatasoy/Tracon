# SQL Saglayicilari — Paylasilan Katman

> `AgentPrism.Sql.Shared` ve saglayicilarin ORTAK davranisi. Saglayiciya ozgu
> notlar ayri dosyalardadir: [`sql-server-tuzaklari.md`](sql-server-tuzaklari.md),
> [`postgresql.md`](postgresql.md), [`sqlite.md`](sqlite.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.

## Paylasilan katman

- **`AgentPrism.Sql.Shared` bir paket DEGILDIR ve csproj tasimaz** (2026-08-04, Faz 23): dosyalar `<Compile Include="../AgentPrism.Sql.Shared/**/*.cs" />` ile hem `AgentPrism.PostgreSql` hem `AgentPrism.SqlServer` icine derlenir. Ayni `internal` tipler iki AYRI derlemede yasar, catisma olmaz. Karar K-176.
- **🚨 Paylasilan hicbir dosya `Npgsql` veya `Microsoft.Data.SqlClient` ad alanina referans VEREMEZ.** Saglayiciya ozgu her sey `SqlDialect` turevlerinden gecer. Bu kural bozulursa ikinci saglayici derlenmez ve neden aylar sonra anlasilir.
- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz** — yazilmayan sorgu bos metin kalir, hata yalniz CALISMA ANINDA gorunur. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Faz 94: 199 sorgudan 115'i ÖZDEŞ (üç dialektte, ÇÖZÜMLENMİŞ SQL metnine göre) — `SqlQueriesBase.BuildSharedQueries()`'e taşındı.** Yeni bir sorgu üç dialektte de aynı metni üretecekse `BuildSharedQueries()`'e yazılır (`{Table("ad")}` ile nitelendirilir); farklıysa ilgili `*Queries.cs`'e. **Ölçüm RAW C# KAYNAĞIYLA değil, `PostgresQueries`/`SqlServerQueries`/`SqliteQueries`'i örnekleyip ÇÖZÜMLENMİŞ metni karşılaştırarak yapılır** — kaynak metni özdeş görünen 4 sorgu (`InsertApiKey`, `TouchRunHeartbeat`, `UpdateRunCompletion`, `UpdateRunCost`) SQL Server'ın hizalama boşluğu yüzünden ÇÖZÜMLENDİĞİNDE farklı çıktı; ters yönde 2 sorgu (`SelectAttachment`, `SelectToolApprovalRules`) kaynakta farklı göründü ama özdeşti. Kapı: `tests/AgentPrism.Sql.Shared.UnitTests/SqlTextSnapshotTests.cs` (üç dialektin çözümlenmiş metnini bir taban çizgisiyle birebir karşılaştırır, Docker İSTEMEZ). Paylaşılan bir sütun listesi (`mcpServerColumns` gibi) hem özdeş hem farklı sorgularda kullanılıyorsa `protected const string XColumns` olarak tabana taşınır — türetilmiş sınıf onu miras yoluyla görmeye devam eder, silinen yerel bildirim geri eklenmez.
- **Maliyet/token toplama ifadeleri artık elle yazılmaz: `SqlQueriesBase.CostTotal(alias)`/`CountWhereAnyNotNull(columns, alias)`/`TreeSum(column, alias, coalesceToZero)`.** `CostAddends` dizisine dördüncü bir terim eklemek `RunCost.Total()`'a karşı çapraz doğrulanır (`tests/AgentPrism.Sql.Shared.UnitTests/CostAddendsCrossCheckTests.cs`) — K-483'ün SQL ikizi artık build-time'da kırılır, çalışma anında değil.
- **`SqlRunStore`'un `runs` okuyucusu (`ReadRun`/`ReadUsage`/`ReadTreeUsage`/`ReadCost`/`ReadTreeCost`) artık `RunOrdinals.X` adlandırılmış sabitlerini kullanır, çıplak ordinal YOK.** `SqlQueriesBase.RunColumnOrder` (53 kalem, `Own`/`Tree` etiketli) bu sıranın TEK belgeli kaydıdır; `RunOrdinalsCrossCheckTests` her ordinalin tam bir kez adlandırıldığını doğrular. **🚨 Bu liste SQL METNİNİ ÜRETMEZ** — üç dialekt kendi `runColumns` metnini yazmaya devam eder, çünkü bir `Tree` kalemi PostgreSQL/SQL Server'da join ile, SQLite'ta ilişkili alt sorguyla çözülür (94.4.3, Açık Soru 5 — "B" kabul edildi: birleşik metin üretimi denenmedi, yalnız ordinal sırası tek kaynağa indirildi). Yeni bir `runs` sütunu eklerken hâlâ DÖRT yer güncellenir (üç `runColumns` + `RunColumnOrder`); fark, bir uyuşmazlığın artık derleme zamanında yakalanmasıdır. Diğer okuyucular (`ReadOrphanedRun`, tool/olay/istatistik) bu fazın kapsamı DIŞINDA kaldı.
- **🚨 `jsonb`'ye yazilan ARA tip kaynak tipin alanlarini OTOMATIK takip ETMEZ** (Faz 86, K-580): yeni alan `AgentDefinitionPayload` gibi bir ara tipe elle eklenmezse jsonb'ye HIC yazilmaz — bellek-ici depo maskeler, kusur yalniz GERCEK SQL kosusuyla gorunur. `AgentDefinitionStoreContract`'a round-trip kapsandi.
- **`SqlDialect.QualifyTable(tableName)` eklendi (2026-08-05, Faz 25)**: saglayicidan bagimsiz SQL uretimi (`RetentionTargetRegistry` gibi) tablo adini nitelendirmek icin dogrudan `{Schema}.{tablo}` YAZAMAZ — PostgreSQL/SQL Server nokta ile, SQLite (K-193) onek bitistirerek nitelendirir. **Faz 94:** `SqlDialect.QualifyTable` artik `Queries.QualifyTable(tableName)`'e devreder, o da `protected virtual string Table(name)`'e — varsayilan nokta ile birlestirir, `SqliteQueries.Table` (dialekt DEGIL, sorgu sinifi) ezer. `SqliteDialect.QualifyTable` ezmesi bu yuzden SILINDI; nitelendirme artik TEK yerde (`SqlQueriesBase`/`SqliteQueries`).
- **🚨 Saglayicinin kendi kurdugu data source ARTIK public bir DI servisi olarak KAYDEDILMEZ** (2026-08-26, Faz 110, K-625): eskiden `TryAddSingleton<NpgsqlDataSource>` (ve SQL Server/SQLite karsiliklari) — tuketicinin KENDI ayni tipte kaydi varsa iki yonlu bir sira yarisiydi. Uc saglayicida da desen ayni: `Options.DataSource` (public `DbDataSource?`) verildiyse o kullanilir ve `SqlStoreContext.OwnsDataSource = false`; verilmediyse `ConnectionString`'den kurulur ve `OwnsDataSource = true`. Data source SqlStoreContext factory'sinin **icinde** coziliur (`XxxDataSourceFactory.Resolve`), asla ayri bir DI tipi olarak degil — PgVectorSearchStore gibi somut tipe ihtiyaci olan bir tuketici bile `SqlStoreContext.DataSource`'u cast eder, yeniden `provider.GetRequiredService<NpgsqlDataSource>()` COKARMAZ (aksi halde EnableKnowledge acikken IKI ayri data source/havuz kurulurdu).
- **`Store` siniflari `internal`'dir** (2026-08-04, Faz 23): `SqlRunStore`, `SqlSessionStore`, … Sebep `internal SqlStoreContext` alan `public` kurucunun `CS0051` vermesi ve tuketicinin somut sinifa ihtiyaci olmamasi. `MigrationRunner` public kaldi (kurucusu internal; DI fabrikayla kaydeder; K-568'in `CS0433` tuzagi icin [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md)).

- **🚨 `EXISTS`/`NOT EXISTS` korelasyonunda BARE tablo adi YAZMA, `QualifyTable(name)` kullan** (2026-08-06, Faz 36): sema nitelenmemis ad, tuketicinin varsayilan semasinda cozulur ve sorgu sessizce yanlis (veya bos) sonuc verir. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`SqlDialect.AddNullableBoolean` eklendi (2026-08-05, Faz 28)**: `AddBoolean` `bool` alir ve uc durumlu bir alani (`evet`/`hayir`/`bilgi yok`) tasiyamaz — eksik bilgi sessizce `false` olurdu. `tool_invocations.usage_estimated` bu yuzden nullable yazilir. Okuma tarafinda `DbHelpers.ToBoolean` kullanilir: SQLite mantiksal tip tasimaz ve `long` (0/1) doner (K-195).
- **`SqlDialect.ArrayContains(column, paramName)`** (Faz 64): "sutun bir dizi parametrenin icinde mi" — diyalekt eslemesi. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Kimlik bazli toplu silme, `IRetentionStore`'un cutoff-tabanli arayuzunu YENIDEN KULLANMAZ; paralel bir yol acilir** — iki islem farkli anahtarlarla calisir ve tek arayuze zorlanmalari her iki tarafi da bozar. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Depoda `LoadAllAsync` (kapsamdaki TUM satirlari cekip C#'ta filtreleme) deseni bulursan supheyle yaklas** (Faz 51): suzgec SQL'e indirilir (`LIKE ... ESCAPE`, onek `EscapeLikeLiteral` ile kacislanir). 🚨 SQL Server/SQLite regex TASIMAZ — nihai eslesme HER ZAMAN istemcide `Regex` ile kalir, SQL yalniz on daraltmadir. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Linked-source (K-176) bir tipin `internal` isareti CROSS-ASSEMBLY sayim icin GUVENILMEZ** (Faz 33, K-247): ayni tip her saglayici derlemesine AYRI derlenir, CLR kimligi FARKLIDIR. Linked-source icindeki bir `internal` tipi `IEnumerable<T>` ile SAYMAK istiyorsan T **Abstractions'da** olmali. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`MigrationRunner` artik `ISqlPersistenceDiagnostics` uygular** (2026-08-06, Faz 33, K-248): `GetSnapshotAsync` migration UYGULAMAZ, yalniz baglanti + bekleyen liste okur. `__migrations` defteri henuz yoksa (DbException) baglanti calisiyor sayilir, tum migration'lar bekliyor kabul edilir — `CanConnect=false` yalniz baglanti KURULAMADIGINDA doner.
- **🚨 Linked-source (K-176) tiplerin AYNI tam nitelikli adi, `Microsoft.AspNetCore.OpenApi`'nin XML yorum onbellegini CATISTIRIR** (Faz 40, K-276): 2+ SQL saglayicisini birlikte referans veren ve `AddOpenApi()` kullanan bir tuketicide `/openapi/v1.json` **500** doner. K-247'nin XML-doc kardesi. Ayrinti ve durum: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md), `docs/ADAYLAR.md` F-76.

- **🚨 CAGIRANIN VERDIGI bir metin tek basina birincil anahtar olamaz** (Faz 41, K-278): kimlik cagirandan geliyorsa anahtar **kiraciyi da icermelidir** (`sessions` → `(tenant_id, id)`); uuid v7 (K-015) bu tuzagi tasimaz. 🚨 SQLite birincil anahtari DEGISTIREMEZ — tablo yeniden kurulur, veri tasinir, indeksler ELLE kurulur. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Kiraci basina tanimli bir politika, kiraci suzgeci OLMAYAN veri duzlemiyle calisamaz** (Faz 41, K-279): `RetentionTargetDefinition.TenantPredicate`'i unutma — yanlis yuklem sessizce BUTUN kiracilarin satirlarini siler. Kendi `tenant_id`'si olmayan hedef, sahibine bakan `EXISTS` ile suzulur (korelasyon FULL NITELENDIRILMIS — K-259). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Kapsam kapisi: `TenantCoverageTests`** (Faz 41, K-281): `Stores/` altindaki her public metot ya `Covered` tablosunda ya `[TenantAgnostic("gerekce")]` ile isaretli olmali; yeni metotta build yesil kalir ama bu test duser. Gerekce 40 karakterden kisa olamaz. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

- **🚨 Bir UPSERT'in dönüş değerini COALESCE edilmiş yapmak için `RETURNING`/`OUTPUT` eklerken SQL Server'ın İKİ DALINA da eklemeyi unutma** (Faz 98, K-608): `UPDATE ... OUTPUT inserted.x ... WHERE ...; IF @@ROWCOUNT = 0 INSERT ... OUTPUT inserted.x ... VALUES ...;` deseninde yalnız bir dala `OUTPUT` eklemek sessiz bir sağlayıcı farkı üretir — ikinci çağrı (UPDATE dalı) çalışırken ilk çağrı (INSERT dalı) hâlâ eski değeri döner veya tersi. `DbHelpers.ReadSingleAsync` iki sonuç kümesini zaten doğru sırayla dener (Faz 65'in deseni), okuma tarafında ek iş gerekmez — yalnız YAZMA tarafında iki `OUTPUT` satırı unutulmamalı. Sözleşme testi bunu dört sağlayıcıda ayrı ayrı koşarak yakalar.
- **🚨 Sorgu metni değişince `SqlTextSnapshotTests` kırılır** (2026-09-01, Faz 126): davranış değil metin sınanır; `AGENTPRISM_SQL_SNAPSHOT_REFRESH=1` ile yenile, diffi oku. Sütun rename: Postgres/SQLite `RENAME COLUMN`, SQL Server `sp_rename`.
- **🚨 Yinelenen bir birincil anahtar değerini (`run_events(run_id, seq)` gibi çağıranın ürettiği bir anahtar) REDDETMEK için `SqlDialect.IsUniqueViolation` zaten var — yeniden icat etme** (Faz 98, K-607): `AppendEventAsync` gibi bir metodun ikinci çağrısı aynı anahtarla gelirse ham sürücü istisnası (`DbException`) sızmasın diye `catch (DbException ex) when (Dialect.IsUniqueViolation(ex))` ile yakalanıp `AgentPrismException`'a çevrilir — `IsForeignKeyViolation`'ın yanına ikinci bir `catch` bloğu olarak eklenir, aynı `try` içinde. Bellek içi store aynı kuralı elle uygular (log'da doğrusal tarama, `MaxRuns` sınırlı store'larda ucuz).

## SQL Server'a ozgu tuzaklar

Parametre sayisi, sorgu yazimi, sema farklari (`NULL` benzersizligi,
`uniqueidentifier`, `ISJSON`, diziler) ve iki dalli upsert:
[`sql-server-tuzaklari.md`](sql-server-tuzaklari.md).

## Test altyapisi

- **`mcr.microsoft.com/mssql/server` bu makinede artık koşuyor** (K-386). `azure-sql-edge` ikamesi (K-317) yedek kalır. Kurulum, tekrar dene ve **performans** (K-387..K-391, ayri sema yerine sinif basina paylasilan sema + `ResetDataAsync`): [`sql-server-yerel-test.md`](sql-server-yerel-test.md).
- **Sozlesme testleri `tests/Shared/` altindadir** ve saglayici basina bir entegrasyon test projesine derlenir (`AgentPrism.StoreContracts` ad alani). Yeni bir saglayici eklerken sozlesme testi YAZILMAZ; yalnizca kosucu sinif turetilir. SQLite bu iddianin DORDUNCU kanitidir (K-194).

- **Iki dalli upsert `OUTPUT` GEREKTIRMEZSE cok basitlesir** (2026-08-19, Faz 65): `UpsertAsync` deger dondurmuyorsa `UPDATE WITH (UPDLOCK, SERIALIZABLE) ...; IF @@ROWCOUNT = 0 INSERT ...;` yeter — K-187/188/189'un asil tuzagi (`OUTPUT` ikinci sonuc kumesine duser) hic devreye girmez. `tenant_provider_bindings`/`tenant_egress_policies` bunu kullanir.

- **🚨 `__migrations`'in KENDI semasini degistiren islem K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ADD set_name` sonrasi ayni iste `set_name` referansi "Invalid column name" verir, `EXEC` ile SARILAMAZ. Cozum: dongu ONCESI `SqlDialect.UpgradeMigrationsTableAsync` (SQLite'ta K-278 rebuild). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## SQLite'a ozgu tuzaklar

SQLite'a ozgu tum notlar (indeks ad alani, upsert, `ExecuteScalarAsync` CLR
tipi, migration kilidi, uuid harf buyuklugu) **taşındı**:
[`sqlite.md`](sqlite.md) (Faz 36, bütçe asimini gidermek icin ayrildi).

- **🚨 `runs` gibi ordinal okunan tabloya sutun eklerken sira UC dialektte de SONA eklenir** (Faz 68): `SqlRunStore.ReadRun` artik `RunOrdinals.X` uzerinden okur (Faz 94, satir 16), ama uc `runColumns` metni hala birebir ayni sirayi elle tasir — yeni sutun `RunColumnOrder`'a da eklenmezse `RunOrdinalsCrossCheckTests` duser. Son ordinal **52** (Faz 87). Ayni "sona ekle" kurali `SelectRunStatistics`'in **sekiz** sonuc kumesi icin de gecerlidir (o okuyucu Faz 94 kapsami DISINDA, hala cıplak ordinal kullanir). Kirilim: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Etiket haritası (`runs.labels`) için `jsonb` seçimi ve üç dialektin süzgeç biçimi**: [`postgresql.md`](postgresql.md) (Faz 68, K-479). SQL Server/SQLite'ta harita JSON METNİDİR ve indeks YOKTUR.
- **🚨 UPSERT'te bir alani duz uzerine yazmak ONU DOGRU BILEN yazimi silebilir** (Faz 68, K-486): kuyruklu `run` `StartRunAsync`'i IKI kez cagirir (HTTP'de kullanici bilinir, iscide `null`); `user_id = EXCLUDED.user_id` atfi SILERDI, `COALESCE(EXCLUDED.user_id, user_id)` korur. Bir alan "set → unset" yonunde MESRU degismiyorsa `COALESCE` her zaman dogrudur. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## 🚨 Geçici çakışma: iki şekli var, yeniden deneme YOLUN TAMAMINI kapsar (K-540, K-545)

Migration kilidi **şemaya** kapsamlıdır (K-389); farklı şemaların ilk göçü
veritabanı genelindeki katalog nesnelerinde buluşur. Çarpışma iki yüzle gelir ve
ikisi de geçicidir — **unique ihlali** ve **deadlock**; sunucu kurbanı **zaten**
geri almıştır. `SqlDialect.IsDeadlock`: SQL Server `1205` · PostgreSQL `40P01` ·
SQLite `SQLITE_BUSY`/`SQLITE_LOCKED`.

Sınıf iki kez bedel ödetti: K-540 deadlock'un `catch`'e hiç girmediğini (5 case),
K-545 `MigrationRunner`'ın **bootstrap** deyimlerinin — şema · ledger · ledger
yükseltmesi · ledger okuması — döngünün **dışında** kaldığını buldu: 15 case
birden, hepsi **0 ms**, `fixture` hiç kalkmadı.

1. Yalnız `IsUniqueViolation`'a bakan bir `catch` deadlock'u **ham** bırakır;
   `MigrationRunner.IsTransientConflict` ikisini birden sorar.
2. **"Bu yalnızca kurulum" muafiyeti yoktur** — aynı katalog nesnesine dokunan
   her deyim yarışır ve idempotentse yeniden denenir.
3. Yeniden denenen şey **re-runnable** olmalıdır; çok deyimli bir rebuild bunu
   kendiliğinden sağlamaz ([`sqlite.md`](sqlite.md)).

Dört `store`'un (`Session`, `Idempotency`, `Experiment`, `Eval`)
`IsUniqueViolation` yakalaması bu sınıf **değildir**: anlamsal daldır ve yazımları
idempotent olmadığı için denenmez. Vakalar:
[`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
