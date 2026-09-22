# SQL Saglayicilari — Paylasilan Katman

> `Tracon.Sql.Shared` ve saglayicilarin ORTAK davranisi. Saglayiciya ozgu
> notlar ayri dosyalardadir: [`sql-server-tuzaklari.md`](sql-server-tuzaklari.md),
> [`postgresql.md`](postgresql.md), [`sqlite.md`](sqlite.md). Paylasilan sorgu
> URETIM mekanigi (`BuildSharedQueries`, `RunOrdinals`, `CostAddends`,
> `QualifyTable`, snapshot testleri) AYRI dosyadadir:
> [`sql-paylasilan-sorgu-uretimi.md`](sql-paylasilan-sorgu-uretimi.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.

## Paylasilan katman

- **`Tracon.Sql.Shared` bir paket DEGILDIR ve csproj tasimaz** (2026-08-04, Faz 23): dosyalar `<Compile Include="../Tracon.Sql.Shared/**/*.cs" />` ile hem `Tracon.PostgreSql` hem `Tracon.SqlServer` icine derlenir. Ayni `internal` tipler iki AYRI derlemede yasar, catisma olmaz. Karar K-176.
- **🚨 Paylasilan hicbir dosya `Npgsql` veya `Microsoft.Data.SqlClient` ad alanina referans VEREMEZ.** Saglayiciya ozgu her sey `SqlDialect` turevlerinden gecer. Bu kural bozulursa ikinci saglayici derlenmez ve neden aylar sonra anlasilir.
- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz** — yazilmayan sorgu bos metin kalir, hata yalniz CALISMA ANINDA gorunur. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `jsonb`'ye yazilan ARA tip kaynak tipin alanlarini OTOMATIK takip ETMEZ** (Faz 86, K-580): yeni alan `AgentDefinitionPayload` gibi bir ara tipe elle eklenmezse jsonb'ye HIC yazilmaz — bellek-ici depo maskeler, kusur yalniz GERCEK SQL kosusuyla gorunur. `AgentDefinitionStoreContract`'a round-trip kapsandi.
- **🚨 Saglayicinin kendi kurdugu data source ARTIK public bir DI servisi olarak KAYDEDILMEZ** (2026-08-26, Faz 110, K-625): eskiden `TryAddSingleton<NpgsqlDataSource>` (ve SQL Server/SQLite karsiliklari) — tuketicinin KENDI ayni tipte kaydi varsa iki yonlu bir sira yarisiydi. Uc saglayicida da desen ayni: `Options.DataSource` (public `DbDataSource?`) verildiyse o kullanilir ve `SqlStoreContext.OwnsDataSource = false`; verilmediyse `ConnectionString`'den kurulur ve `OwnsDataSource = true`. Data source SqlStoreContext factory'sinin **icinde** coziliur (`XxxDataSourceFactory.Resolve`), asla ayri bir DI tipi olarak degil — PgVectorSearchStore gibi somut tipe ihtiyaci olan bir tuketici bile `SqlStoreContext.DataSource`'u cast eder, yeniden `provider.GetRequiredService<NpgsqlDataSource>()` COKARMAZ (aksi halde EnableKnowledge acikken IKI ayri data source/havuz kurulurdu).
- **`Store` siniflari `internal`'dir** (2026-08-04, Faz 23): `SqlRunStore`, `SqlSessionStore`, … Sebep `internal SqlStoreContext` alan `public` kurucunun `CS0051` vermesi ve tuketicinin somut sinifa ihtiyaci olmamasi. `MigrationRunner` public kaldi (kurucusu internal; DI fabrikayla kaydeder; K-568'in `CS0433` tuzagi icin [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md)).

- **🚨 `EXISTS`/`NOT EXISTS` korelasyonunda BARE tablo adi YAZMA, `QualifyTable(name)` kullan** (2026-08-06, Faz 36): sema nitelenmemis ad, tuketicinin varsayilan semasinda cozulur ve sorgu sessizce yanlis (veya bos) sonuc verir. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`SqlDialect.AddNullableBoolean` eklendi (2026-08-05, Faz 28)**: `AddBoolean` `bool` alir ve uc durumlu bir alani (`evet`/`hayir`/`bilgi yok`) tasiyamaz — eksik bilgi sessizce `false` olurdu. `tool_invocations.usage_estimated` bu yuzden nullable yazilir. Okuma tarafinda `DbHelpers.ToBoolean` kullanilir: SQLite mantiksal tip tasimaz ve `long` (0/1) doner (K-195).
- **`SqlDialect.ArrayContains(column, paramName)`** (Faz 64): "sutun bir dizi parametrenin icinde mi" — diyalekt eslemesi. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Kimlik bazli toplu silme, `IRetentionStore`'un cutoff-tabanli arayuzunu YENIDEN KULLANMAZ; paralel bir yol acilir** — iki islem farkli anahtarlarla calisir ve tek arayuze zorlanmalari her iki tarafi da bozar. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Depoda `LoadAllAsync` (kapsamdaki TUM satirlari cekip C#'ta filtreleme) deseni bulursan supheyle yaklas** (Faz 51): suzgec SQL'e indirilir (`LIKE ... ESCAPE`, onek `EscapeLikeLiteral` ile kacislanir). 🚨 SQL Server/SQLite regex TASIMAZ — nihai eslesme HER ZAMAN istemcide `Regex` ile kalir, SQL yalniz on daraltmadir. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Linked-source (K-176) bir tipin `internal` isareti CROSS-ASSEMBLY sayim icin GUVENILMEZ** (Faz 33, K-247): ayni tip her saglayici derlemesine AYRI derlenir, CLR kimligi FARKLIDIR. Linked-source icindeki bir `internal` tipi `IEnumerable<T>` ile SAYMAK istiyorsan T **Abstractions'da** olmali. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Linked-source (K-176) tiplerin AYNI tam nitelikli adi, `Microsoft.AspNetCore.OpenApi`'nin XML yorum onbellegini CATISTIRIR** (Faz 40, K-276): 2+ SQL saglayicisini birlikte referans veren ve `AddOpenApi()` kullanan bir tuketicide `/openapi/v1.json` **500** doner. K-247'nin XML-doc kardesi. Ayrinti ve durum: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md), `docs/ADAYLAR.md` F-76.

- **🚨 CAGIRANIN VERDIGI bir metin tek basina birincil anahtar olamaz** (Faz 41, K-278): kimlik cagirandan geliyorsa anahtar **kiraciyi da icermelidir** (`sessions` → `(tenant_id, id)`); uuid v7 (K-015) bu tuzagi tasimaz. 🚨 SQLite birincil anahtari DEGISTIREMEZ — tablo yeniden kurulur, veri tasinir, indeksler ELLE kurulur. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Kiraci basina tanimli bir politika, kiraci suzgeci OLMAYAN veri duzlemiyle calisamaz** (Faz 41, K-279): `RetentionTargetDefinition.TenantPredicate`'i unutma — yanlis yuklem sessizce BUTUN kiracilarin satirlarini siler. Kendi `tenant_id`'si olmayan hedef, sahibine bakan `EXISTS` ile suzulur (korelasyon FULL NITELENDIRILMIS — K-259). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Kapsam kapisi: `TenantCoverageTests`** (Faz 41, K-281): `Stores/` altindaki her public metot ya `Covered` tablosunda ya `[TenantAgnostic("gerekce")]` ile isaretli olmali; yeni metotta build yesil kalir ama bu test duser. Gerekce 40 karakterden kisa olamaz. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `TenantCoverageTests` yalniz `Tracon.SqlServer.IntegrationTests`'te yasar, dar `faz-uygulama` kosumlari onu atlar** (2026-09-05, Faz 146 kapanis bulgusu): yeni bir `Sql*Store` metodu `Covered`'a girmeden kaldi, yalniz TAM `kapanis` kosumu yakaladi. `Covered`'a girmek TEK BASINA yetmez — jenerik bes test yalniz Seed/Exists/Count/Delete'e (`Save`/`Get`/`List`/`Delete`) baglanir; bir `Claim`/atomik metot BESPOKE capraz-kiraci testi ister (iki `tenantId`, biri digerini sessizce tuketmesin). Vaka: `QuotaStoreContract.A_different_tenants_identically_shaped_claim_is_independent`.

- **🚨 Bir UPSERT'in dönüş değerini COALESCE edilmiş yapmak için `RETURNING`/`OUTPUT` eklerken SQL Server'ın İKİ DALINA da eklemeyi unutma** (Faz 98, K-608): `UPDATE ... OUTPUT inserted.x ... WHERE ...; IF @@ROWCOUNT = 0 INSERT ... OUTPUT inserted.x ... VALUES ...;` deseninde yalnız bir dala `OUTPUT` eklemek sessiz bir sağlayıcı farkı üretir — ikinci çağrı (UPDATE dalı) çalışırken ilk çağrı (INSERT dalı) hâlâ eski değeri döner veya tersi. `DbHelpers.ReadSingleAsync` iki sonuç kümesini zaten doğru sırayla dener (Faz 65'in deseni), okuma tarafında ek iş gerekmez — yalnız YAZMA tarafında iki `OUTPUT` satırı unutulmamalı. Sözleşme testi bunu dört sağlayıcıda ayrı ayrı koşarak yakalar.
- **🚨 PAYLASILAN bir sorgunun SONUC SUTUNU her dialektte ayni CLR tipinde DEGILDIR — `COUNT(*)` SQL Server'da `int`, PostgreSQL/SQLite'ta `bigint`** (2026-09-02, Faz 133 yan bulgusu): `SelectConversationBranchPoint` paylasilan katmanda `COUNT(*)` yaziyor, okuyucu `reader.GetInt64(1)` cagiriyordu; SQL Server'da `InvalidCastException: Unable to cast 'System.Int32' to 'System.Int64'` firladi ve **konusma dallandirma (Faz 47) SQL Server'da HIC calismiyordu** — ozellik sevk edildiginden beri. Gorunmedi cunku `ConversationBranchTests` yalniz **SQLite'ta** vardi; gerekce "sorgular paylasilan katmanda, dialektten bagimsiz" idi. O gerekce sorgu **METNI** icin dogru, **OKUYUCU** icin yanlis. Kural: paylasilan bir sorguda toplama fonksiyonu yazarsan tipi `CAST(... AS bigint)` ile SABITLE. `MAX(seq)`/`SUM(kolon)` guvenlidir — sutunun kendi tipini doner ve `seq` uc dialektte de `bigint`. Tarama: 103 paylasilan sorgunun tamami, baska vaka yok. Kapi: `tests/Tracon.SqlServer.IntegrationTests/ConversationBranchTests.cs` (bes case, once KIRMIZI goruldu).
- **🚨 Yinelenen bir birincil anahtar değerini (`run_events(run_id, seq)` gibi çağıranın ürettiği bir anahtar) REDDETMEK için `SqlDialect.IsUniqueViolation` zaten var — yeniden icat etme** (Faz 98, K-607): `AppendEventAsync` gibi bir metodun ikinci çağrısı aynı anahtarla gelirse ham sürücü istisnası (`DbException`) sızmasın diye `catch (DbException ex) when (Dialect.IsUniqueViolation(ex))` ile yakalanıp `TraconException`'a çevrilir — `IsForeignKeyViolation`'ın yanına ikinci bir `catch` bloğu olarak eklenir, aynı `try` içinde. Bellek içi store aynı kuralı elle uygular (log'da doğrusal tarama, `MaxRuns` sınırlı store'larda ucuz).

- **🚨 "Bos" bir metin sutunu `NULL` ile `''` DEMEKTIR — `IS NULL` tek basina yetmez** (2026-09-07, F-214): `RunScore.MessageId`'nin sevk edilen sozlesmesi *"if empty, the score belongs to the whole run"* der; **empty** ikisini birden kapsar. Faz 154'un skor ozeti dogru yazdi (`ScoreFilter`), `SelectExperimentResults` uc saglayicida da yalniz `IS NULL` kullandi — `MessageId = ""` skoru bir sorguda run, digerinde mesaj seviyesi sayilip arm ortalamasindan **sessizce** dustu. C# karsiligi `is null` ile `is { Length: > 0 }` farkidir. 🚨 Predicate **bes** yerde tekrarlanir, birlikte degisir: uc `*Queries.cs` + `InMemoryRunStore.Analytics.cs` + `samples/.../FileRunStore.cs`; SQL Server'da bos dizge `N''`. Kapi: `RunStoreContract`'ta **iki yonlu** cift test — ikincisi olmadan "her skoru run seviyesi say" diyen asiri duzeltme de yesil gecerdi. Skoru `IRunStore` okur, YAZMAZ; sozlesme kendi store'unu `CreateScoreStoreAsync()` ile ister.

## Alt yazma yollarinda BEKLENEN kiraci (K-355)

> Faz 156'da `postgresql.md`'den taşındı: konu saglayici-geneldir.

- **🚨 Ambient kiraciyla suzmek MESRU yazmalari dusurur.** `RunStartInfo.TenantId`
  ambient kiraciyi **bilerek** ezer (workflow ve is kuyrugu boyle calisir);
  bir kez denenip GERI ALINDI (K-280). Dogru cozum cagrinin tasidigi **beklenen**
  kiracidir: `RunEvent`/`RunCompletion`/`ToolInvocationRecord`'da `TenantId`,
  `UpdateRunCostAsync`'te `string? tenantId`. Deger `RunEventWriter.StartAsync`
  icinde `RunStartInfo.TenantId`'den alinir. `null` → denetim yok (geriye donuk
  uyumlu).
- **SQL sekli:** UPDATE'lerde `AND (@tenant_id IS NULL OR tenant_id = @tenant_id)`;
  INSERT'lerde `VALUES` yerine
  `SELECT ... WHERE EXISTS (SELECT 1 FROM runs r WHERE r.id = @run_id AND (@tenant_id IS NULL OR r.tenant_id = @tenant_id))`.
  PostgreSQL parametre tiplerini hedef sutunlardan cozer; `INSERT ... SELECT`
  ek cast gerektirmedi. Uc lehcede de yesil kostu.
- **Migration setleri (opsiyonel, Faz 67, K-475/K-476): `0024_vector.sql` `MigrationsKnowledge/0001_vector.sql`'a tasindi** — knowledge seti yalniz `EnableKnowledge = true` iken uygulanir (K1, varsayilan kapali). Iki setin numaralari BAGIMSIZDIR (core `0001`, knowledge de `0001`); ledger'in birincil anahtari bu yuzden `(set_name, id)`'dir, `id` tek basina degil. `IVectorSearchStore` kapaliyken KAYITSIZ degildir — fabrikasi `null` doner (Faz 51'in "GetService null = yok" kuralinin tekrar kullanimi).


## SQL Server'a ozgu tuzaklar

Parametre sayisi, sorgu yazimi, sema farklari (`NULL` benzersizligi,
`uniqueidentifier`, `ISJSON`, diziler) ve iki dalli upsert:
[`sql-server-tuzaklari.md`](sql-server-tuzaklari.md).

## Test altyapisi

- **SQL Server'i yerelde ayaga kaldirma** (imaj secimi, tekrar dene, sinif basina paylasilan sema): [`sql-server-yerel-test.md`](sql-server-yerel-test.md).
- **Yeni saglayici eklerken sozlesme testi YAZILMAZ**, yalnizca kosucu sinif turetilir: [`test-altyapisi.md`](test-altyapisi.md).


## SQLite'a ozgu tuzaklar

SQLite'a ozgu tum notlar (indeks ad alani, upsert, `ExecuteScalarAsync` CLR
tipi, migration kilidi, uuid harf buyuklugu) **taşındı**:
[`sqlite.md`](sqlite.md) (Faz 36, bütçe asimini gidermek icin ayrildi).

- **Etiket haritası (`runs.labels`) için `jsonb` seçimi ve üç dialektin süzgeç biçimi**: [`postgresql.md`](postgresql.md) (Faz 68, K-479). SQL Server/SQLite'ta harita JSON METNİDİR ve indeks YOKTUR.
- **🚨 UPSERT'te bir alani duz uzerine yazmak ONU DOGRU BILEN yazimi silebilir** (Faz 68, K-486): kuyruklu `run` `StartRunAsync`'i IKI kez cagirir (HTTP'de kullanici bilinir, iscide `null`); `user_id = EXCLUDED.user_id` atfi SILERDI, `COALESCE(EXCLUDED.user_id, user_id)` korur. Bir alan "set → unset" yonunde MESRU degismiyorsa `COALESCE` her zaman dogrudur. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Migration ve goc kilidi

`MigrationRunner`, `__migrations` defteri, gecici catisma (unique ihlali ·
deadlock) ve yeniden deneme kurallari ayri dosyadadir:
[`sql-migration.md`](sql-migration.md) — Faz 133'te butce asimini gidermek icin
ayrildi, `sqlite.md`'nin Faz 36'daki emsaliyle.
- **🚨 `INSERT` metni ile hedef tablonun sütun kümesi ayrı ayrı bayatlar; aynı
  tabloya yazan İKİ sorgu varsa biri eksik kalabilir ve sözleşme testi yoksa
  bunu yalnız üretim gösterir** (2026-09-18, `HATA-S3-003` sınıf taraması).
  `eval_cases`'e iki sorgu yazıyordu: `InsertEvalCaseWithComputedSeq`
  (promosyon yolu) `source_run_id`/`source_kind`/`promoted_at` sütunlarını
  yazıyor, `InsertEvalCase` (`ReplaceCasesAsync` yolu) **yazmıyordu**. Sonuç:
  her tam değiştirme bir case'i sessizce "promosyonsuz" bırakıyor, store'un
  kendi `eval_cases_source_run_uq` guard'ı o run'ı tanımaz oluyor ve **aynı run
  ikinci kez yükseltilebiliyordu**. Bellek içi store kaydı olduğu gibi
  koruduğu için sapma yalnız SQL'deydi. **Kontrol:** bir tabloya yazan her
  sorguyu birlikte ara (`grep -n "INSERT INTO .*<tablo>" -r src/`) ve sütun
  listelerini karşılaştır; fark varsa bunu bir sözleşme testine çevir —
  `EvalStoreContract.ReplaceCasesAsync_keeps_the_promotion_fields_it_was_given`
  düzeltmeden önce bellek içinde YEŞİL, iki SQL sağlayıcısında KIRMIZI idi ve
  sapmayı tek koşumda gösterdi.

- **`DbDataSource` adapter'ının `ConnectionString`'i parolayı DÜŞÜRÜR** (2026-09-22, K-846): `NpgsqlDataSource` sözleşmesi budur; `SqlServerDataSource`/`SqliteDataSource` ham dizeyi döndürüyordu ve özelliği okuyan her diagnostic parolayı görürdü. Builder round-trip biçimi normalize eder — dizeyi metin karşılaştıran test yazma. Yeni adapter aynı sözleşmeyi uygular; kapı: `SqlServerDataSourceTests` · `SqliteDataSourceTests`.
