# SQL Saglayicilari — Paylasilan Katman, SQL Server ve SQLite Tuzaklari

> `AgentPrism.Sql.Shared`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite` ve
> saglayicilarin ortak davranisi. PostgreSQL'e ozgu notlar icin:
> [`postgresql.md`](postgresql.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.

## Paylasilan katman

- **`AgentPrism.Sql.Shared` bir paket DEGILDIR ve csproj tasimaz** (2026-08-04, Faz 23): dosyalar `<Compile Include="../AgentPrism.Sql.Shared/**/*.cs" />` ile hem `AgentPrism.PostgreSql` hem `AgentPrism.SqlServer` icine derlenir. Ayni `internal` tipler iki AYRI derlemede yasar, catisma olmaz. Karar K-176.
- **🚨 Paylasilan hicbir dosya `Npgsql` veya `Microsoft.Data.SqlClient` ad alanina referans VEREMEZ.** Saglayiciya ozgu her sey `SqlDialect` turevlerinden gecer. Bu kural bozulursa ikinci saglayici derlenmez ve neden aylar sonra anlasilir.
- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz.** Ozellikler `{ get; protected set; } = string.Empty;`'dir; yazilmayan sorgu bos metin kalir ve hata yalnizca CALISMA ANINDA gorunur — derleme de test de kirilmaz. Faz 25 (`IRetentionStore`) bu tuzagin ilk gercek adayiydi; kontrol duzlemi (politika/kosu CRUD) icin bu kural aynen uygulandi. Veri duzlemi (say/sil/arsiv-oku) icin ise 11 hedef × 3 saglayici = 99 elle sorgu yazmak yerine `Sql.Shared/Internal/RetentionTargetRegistry.cs` (tablo+kosul, saglayicidan bagimsiz) + `SqlDialect`'te 3 sablon yontemi (K-198) tercih edildi — ayni tuzagin 9 kat buyumesini onledi.
- **`SqlDialect.QualifyTable(tableName)` eklendi (2026-08-05, Faz 25)**: saglayicidan bagimsiz SQL uretimi (`RetentionTargetRegistry` gibi) tablo adini nitelendirmek icin dogrudan `{Schema}.{tablo}` YAZAMAZ — PostgreSQL/SQL Server nokta ile, SQLite (K-193) onek bitistirerek nitelendirir. Varsayilan uygulama nokta ile birlestirir; `SqliteDialect` ezer.
- **Depo siniflari `internal`'dir** (2026-08-04, Faz 23): `SqlRunStore`, `SqlSessionStore`, … Sebep `internal SqlStoreContext` alan `public` kurucunun `CS0051` vermesi ve tuketicinin somut sinifa ihtiyaci olmamasi. `MigrationRunner` public kaldi (kurucusu internal; DI fabrikayla kaydeder).
- **`DbDataSource` uyarlayicisi elle yazildi**: `Microsoft.Data.SqlClient` bir `DbDataSource` uygulamasi sunmaz (Npgsql sunar). `SqlServerDataSource` yalnizca `CreateDbConnection()`'i uygular; taban sinifin `CreateCommand` uygulamasi baglanti omrunu Npgsql ile ayni sekilde yonetir.

## SQL Server parametre tuzaklari

- **🚨 Tipi verilmemis `decimal` parametresi `decimal(18,0)` sayilir ve ONDALIK KISIM SESSIZCE KESILIR** (2026-08-04, Faz 23): butun para sutunlari `decimal(20,10)`'dur; `SqlServerDialect.AddDecimal` `Precision = 20`, `Scale = 10` yazar. Yazilmazsa maliyetler tam sayiya yuvarlanir ve hicbir test bunu yakalamaz — yalnizca gidis-donus testi yakalar (`SqlServerDialectTests.Maliyet_ondaligi_kesilmeden_gidip_gelir`).
- **`varbinary(max)` parametresine uzunluk `-1` verilir**: verilmezse SqlClient boyutu degerden cikarir ve 8000 baytin uzerinde hata olusur.
- **Zaman damgalari `DateTimeOffset` olarak, UTC'ye cevrilerek yazilir.** PostgreSQL `timestamptz` icin `DateTime` (`Kind = Utc`) bekler; cevirim `SqlDialect.AddTimestamp` turevlerindedir. Depo kodu `.UtcDateTime` cagirmaz.
- **Istege bagli suzgec parametreleri acikca tiplenmelidir.** PostgreSQL tipsiz NULL'da `42P08` verir (bkz. `postgresql.md`); SQL Server tipsiz NULL'i `nvarchar` sayar ve sessizce yanlis plan uretebilir. Ikisi de `SqlDialect.Add*` ile tiplenir.

## SQL Server sorgu tuzaklari

- **🚨 `FETCH NEXT @take ROWS ONLY` `@take = 0` iken HATA VERIR**; PostgreSQL'de `LIMIT 0` bos liste dondururdu. Davranis esitligi icin sayfali sorgular WHERE'e `AND @take > 0` ekler ve `FETCH` degerini `CASE WHEN @take < 1 THEN 1 ELSE @take END` ile en az bire sabitler. Ikisi birlikte gerekir: WHERE tek basina yetmez cunku FETCH degeri satir olmasa da dogrulanir.
- **🚨 `COUNT(*) FILTER (WHERE p)` -> `COALESCE(SUM(CASE WHEN p THEN 1 ELSE 0 END), 0)`.** `COALESCE` ZORUNLUDUR: bos kume uzerinde `SUM` NULL dondururken PostgreSQL'in `COUNT`'u sifir donduruyordu. Unutulursa istatistik uclari bos veritabaninda NULL doner.
- **`LEAST` / `GREATEST` SQL Server 2019'da YOKTUR** (2022 ile geldi). `CASE` zinciriyle yazilir ve PostgreSQL'in NULL atlama davranisi elle kurulur: `GREATEST(a, b)` NULL argumani yok sayar, duz bir `CASE WHEN a > b` ise NULL'da UNKNOWN dondurur.
- **`DATETRUNC` 2022+'dir; 2019 uyumu icin `DATEADD(unit, DATEDIFF(unit, 0, x), 0)` kullanilir.** Ayrica `DATEPART` PARAMETRELENEMEZ: `date_trunc(@bucket_unit, x)` karsiligi bir `CASE` ifadesidir.
- **Veri degistiren CTE T-SQL'de YOKTUR.** PostgreSQL'in `WITH updated AS (UPDATE ... RETURNING)` yapisi `DECLARE @t TABLE` + `OUTPUT ... INTO @t` ile kurulur (`ReportJobItem`).
- **`MERGE` kullanilmaz** (K-177). Upsert deseni: `UPDATE ... WITH (UPDLOCK, SERIALIZABLE) ... OUTPUT inserted.*` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT inserted.*`. Iki dal AYNI sutunlari dondurmelidir; yoksa paylasilan okuyucu bozulur.
- **`@@ROWCOUNT` bilesik kosulda once bir degiskene alinir.** `IF @@ROWCOUNT = 0 AND NOT EXISTS (...)` yazarsan alt sorgu once degerlendirilirse sayac sifirlanir (`UpsertExperiment`).
- **`CREATE SCHEMA` bir toplu islemin ILK ifadesi olmak zorundadir**; kosullu calistirma `EXEC(N'CREATE SCHEMA ...')` ile sarilir.
- **`nvarchar(max)` INDEKSLENEMEZ.** Anahtar/indeks sutunlari `nvarchar(200)` (veya `nvarchar(64)`/`(128)`) boyutludur; serbest metin `nvarchar(max)` kalir. Nonclustered indeks anahtar siniri 1700 bayt, clustered 900 bayt.

## Semaya ozgu farklar

- **🚨 NULL benzersizligi TERS calisir** (K-184): PostgreSQL'de NULL hicbir NULL'a esit degildir → `COALESCE`'li ifade indeksi gerekiyordu. SQL Server NULL'lari ESIT sayar → duz `UNIQUE` yeter. Ama ayni kural `jobs (schedule_id, scheduled_for)` kisitinda ters tarafa duser: SQL Server ikinci bir zamanlamasiz isi engellerdi, bu yuzden orada kisit `WHERE schedule_id IS NOT NULL` filtreli benzersiz indekstir. Sorgu tarafinda eslesme `ISNULL(c, N'') = ISNULL(@p, N'')` ile yazilir; `@p` NULL iken duz `=` UNKNOWN dondururdu.
- **🚨 `uniqueidentifier` siralamasi bayt sirasina gore DEGILDIR** (son alti bayt once karsilastirilir). uuid v7 (K-015) SQL Server'da zaman sirali GORUNMEZ ve kumelenmis birincil anahtar sayfa bolunmesi uretir. Yogun tablolarda PK `NONCLUSTERED`, kumelenmis indeks `(zaman_sutunu, id)` uzerindedir (K-180).
- **`ISJSON` kisitlari yalnizca PostgreSQL'de `jsonb`/`json` olan sutunlarda vardir** — davranis esitligi icin. `run_events.payload` ve `tool_invocations.arguments/result` PostgreSQL'de `text`'tir (gecerli JSON olmayabilir) ve kisit TASIMAZ. `audit_log.before/after` de kisit tasimaz: gozlemlenebilirlik islevselligi bozmaz.
- **Diziler JSON metnidir** (K-182): `OPENJSON` ile acilir, `[key]` 0 tabanlidir ve `UNNEST ... WITH ORDINALITY`'nin `ord - 1` degerine birebir denk gelir.

## Iki dalli upsert desenindeki gizli tuzaklar (K-187, K-188, K-189)

Bu ucu, `azure-sql-edge` ile ilk kez gercek testler kosturulduğunda (2026-08-05,
Faz 23 kapanisi) 204 testin 204'u de kirilmisti. Kok sebepler:

- **🚨 `@@ROWCOUNT` onekini unutma.** `ROWCOUNT` tek basina T-SQL'de gecersiz
  sozdizimidir; sistem degiskeni her zaman `@@ROWCOUNT`'tir. `SqlServerQueries.cs`
  genelinde 17 sorguda bu onek eksikti — hicbir derleme veya format kapisi
  yakalamaz, yalnizca calisma aninda "Incorrect syntax near ROWCOUNT" verir.
- **🚨 K-177'nin iki dalli upsert deseni (`UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0
  INSERT ... OUTPUT`) UPDATE 0 satir etkiledigende gercek satiri IKINCI sonuc
  kumesine yazar.** `DbHelpers.ReadSingleAsync` ve `ExecuteScalarAsync` yalnizca
  ilk kumeye bakiyordu; kayit INSERT edilmis olsa bile ilk kume bos oldugu icin
  `null` donuyordu. Ikisi de artik `NextResultAsync` ile satir/deger bulunana
  kadar sonraki kumelere duser. PostgreSQL'in tek ifadelik `RETURNING`
  deseninde bu dongu zararsizdir (tek kume var).
- **🚨 Paylasilan bir depo, saglayiciya ozgu bir ADO.NET tipine (`GetFieldValue<string[]>`)
  dogrudan basvurmamalidir — dizi/JSON okumasi HER ZAMAN `Dialect.ReadTextArray`/
  `ReadUuidArray` uzerinden gecer.** `SqlWebhookStore.ReadSubscription` bunu
  atlayip Npgsql'in dogal dizi destegine dayanmisti; PostgreSQL'de sessizce
  calisiyordu ama SQL Server'da `InvalidCastException` verdi. Yeni bir depo
  yazarken dizi/JSON donen her sutun icin `Dialect.Read*` cagrildigini kontrol et.

## Test altyapisi

- **🚨 `mcr.microsoft.com/mssql/server` yalnizca `linux/amd64`'tur.** Apple Silicon'da Docker Desktop'ta "Use Rosetta for x86_64/amd64 emulation" acik degilse container `exit 133` ile duser ve HICBIR SQL Server testi kosmaz. `azure-sql-edge` arm64 tasir ama ayri bir urundur ve gercek SQL Server'i kanitlamaz.
- **🚨 `azure-sql-edge` ge​cici ikame artik HER ZAMAN calismayabilir.** K-186'nin kullandigi teknik (imaji `SqlServerFixture`'da gecici degistirip testleri kosturmak) 2026-08-05'te (Faz 25) tekrar denendi ve BASARISIZ oldu: `Testcontainers.MsSql`'in hazir-olma denetimi konteynerin icinde `sqlcmd` ikili dosyasini arar (`MsSqlContainer.FindSqlCmdFilePathAsync`), `azure-sql-edge` imajinda bu arac YOK — container calismis olsa bile `System.NotSupportedException: The sqlcmd binary could not be found` ile fixture baslatma basarisiz olur. K-186'nin basarili kosusu farkli bir Testcontainers surumunde/imaj etiketinde alinmis olabilir; bu artik guvenilir bir yedek yol DEGILDIR. Gercek dogrulama icin linux/amd64 bir makine veya CI gerekir.
- **Sozlesme testleri `tests/Shared/` altindadir** ve saglayici basina bir entegrasyon test projesine derlenir (`AgentPrism.StoreContracts` ad alani). Yeni bir saglayici eklerken sozlesme testi YAZILMAZ; yalnizca kosucu sinif turetilir. SQLite bu iddianin DORDUNCU kanitidir (K-194).
- **Her test kendi semasini/onekini kullanir** (`t_<16 hex>`), her saglayicida. Bu hem yalitim saglar hem `SchemaName`/`TablePrefix` ayarinin gercekten calistigini her testte dogrular.

## SQLite (Faz 24) — indeks ad alani ve upsert

> Kararlar: K-190 (tablo oneki) … K-197 (SQLitePCLRaw surum sabitleme).
> Ayrintili gerekce icin `docs/KARARLAR.md`.

- **🚨 SQLite'ta indeks (ve tetikleyici/gorunum) adlari VERITABANI GENELINDE
  tektir — sema veya tabloya gore kapsamli DEGILDIR.** PostgreSQL semaya, SQL
  Server tabloya gore kapsamli tutar; SQLite'ta TUM nesneler TEK duz ad
  alanini paylasir. Migration DDL'inde yalnizca TABLO adlarini onekle yazip
  INDEKS adlarini onceksiz birakmak, ayni fiziksel `.db` dosyasini paylasan
  farkli `TablePrefix` degerleri arasinda `CREATE INDEX IF NOT EXISTS`
  CAKISMASINA yol acar: ikinci tablonun indeksi "zaten var" sanilip SESSIZCE
  atlanir ve o tablonun `ON CONFLICT` hedefi calisma aninda patlar. Migration
  dosyasindaki HER indeks adi da tablo onekini tasimalidir (K-193).
- **🚨 `const string sutunlar = """...""";` icinde `{Schema}` yazmak DERLENIR
  ama INTERPOLE EDILMEZ.** `const` bir string, `$"""..."""` olmadan
  yazildiginda `{Schema}` harfi harfine SQL metnine gomulur ("unrecognized
  token: '{'"). Sutun listesi bir alt sorgu icinde tablo adina ihtiyac
  duyuyorsa (`SelectRun`'daki korele skaler alt sorgular gibi) degisken
  `var sutunlar = $"""...""";` olarak yazilmalidir — `const` yalnizca hicbir
  interpolasyon TASIMAYAN sutun listeleri icindir.
- **Upsert PostgreSQL ile birebir aynidir** (K-194): `INSERT ... ON CONFLICT
  (…) DO UPDATE … RETURNING`, ifade tabanli catisma hedefleri (`COALESCE(col, '')`)
  dahil. SQL Server'in iki-dalli deseni (K-177) ve onun cektigi coklu-sonuc-kumesi
  tuzagi (K-188) SQLite'ta hic YOKTUR.
- **🚨 `ExecuteScalarAsync`in dondurdugu CLR tipi saglayiciya gore DEGISIR**
  (K-195): PostgreSQL/SQL Server `uuid`/`uniqueidentifier` icin `Guid`, SQLite
  `TEXT` oldugu icin `string` doner; PostgreSQL/SQL Server mantiksal bir
  karsilastirma icin `bool`, SQLite icin `long` (0/1) doner. Cagri yerinde
  `(Guid)result!` veya `result is bool b && b` YAZMA — `DbHelpers.ToGuid`/
  `ToBoolean` kullan.
- **Migration kilidi sidecar dosya kilididir, islem DEGILDIR** (K-192):
  `Microsoft.Data.Sqlite` ic ice islem desteklemez; kilidi `BEGIN IMMEDIATE`
  ile acik tutmak `MigrationRunner`'in kendi ic-ice islemleriyle catisirdi.
- **uuid BUYUK harfle yazilir, kucuk harfe CEVRILMEZ** (K-191): zorunlu
  Guid'ler (`DbHelpers.Add`) ile nullable Guid'ler (`Dialect.AddUuid`) FARKLI
  harf buyuklugu kullansaydi ayni kimlik iki temsille saklanir ve
  `WHERE`/`JOIN` esitligi sessizce kirilirdi.
- **`decimal` icin ozel islem GEREKMEZ**: surucu tipli/tipsiz fark etmeksizin
  her zaman TEXT yazar, kulturden bagimsizdir. SQL Server'in `Precision`/`Scale`
  zorunlulugu (yukarida) burada YOKTUR.
- **Yabanci anahtar zorlamasi VARSAYILAN KAPALIDIR**; her baglantida
  `PRAGMA foreign_keys = ON` acikca calistirilir (`SqliteDataSource`), aksi
  halde `REFERENCES ... ON DELETE CASCADE` sessizce yok sayilir.
