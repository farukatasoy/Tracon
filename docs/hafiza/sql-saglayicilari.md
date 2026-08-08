# SQL Saglayicilari — Paylasilan Katman ve SQL Server Tuzaklari

> `AgentPrism.Sql.Shared`, `AgentPrism.SqlServer` ve saglayicilarin ortak
> davranisi. PostgreSQL'e ozgu notlar icin: [`postgresql.md`](postgresql.md).
> SQLite'a ozgu notlar icin: [`sqlite.md`](sqlite.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.

## Paylasilan katman

- **`AgentPrism.Sql.Shared` bir paket DEGILDIR ve csproj tasimaz** (2026-08-04, Faz 23): dosyalar `<Compile Include="../AgentPrism.Sql.Shared/**/*.cs" />` ile hem `AgentPrism.PostgreSql` hem `AgentPrism.SqlServer` icine derlenir. Ayni `internal` tipler iki AYRI derlemede yasar, catisma olmaz. Karar K-176.
- **🚨 Paylasilan hicbir dosya `Npgsql` veya `Microsoft.Data.SqlClient` ad alanina referans VEREMEZ.** Saglayiciya ozgu her sey `SqlDialect` turevlerinden gecer. Bu kural bozulursa ikinci saglayici derlenmez ve neden aylar sonra anlasilir.
- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz.** Ozellikler `{ get; protected set; } = string.Empty;`'dir; yazilmayan sorgu bos metin kalir ve hata yalnizca CALISMA ANINDA gorunur — derleme de test de kirilmaz. Faz 25 (`IRetentionStore`) bu tuzagin ilk gercek adayiydi; kontrol duzlemi (politika/kosu CRUD) icin bu kural aynen uygulandi. Veri duzlemi (say/sil/arsiv-oku) icin ise 11 hedef × 3 saglayici = 99 elle sorgu yazmak yerine `Sql.Shared/Internal/RetentionTargetRegistry.cs` (tablo+kosul, saglayicidan bagimsiz) + `SqlDialect`'te 3 sablon yontemi (K-198) tercih edildi — ayni tuzagin 9 kat buyumesini onledi.
- **`SqlDialect.QualifyTable(tableName)` eklendi (2026-08-05, Faz 25)**: saglayicidan bagimsiz SQL uretimi (`RetentionTargetRegistry` gibi) tablo adini nitelendirmek icin dogrudan `{Schema}.{tablo}` YAZAMAZ — PostgreSQL/SQL Server nokta ile, SQLite (K-193) onek bitistirerek nitelendirir. Varsayilan uygulama nokta ile birlestirir; `SqliteDialect` ezer.
- **`Store` siniflari `internal`'dir** (2026-08-04, Faz 23): `SqlRunStore`, `SqlSessionStore`, … Sebep `internal SqlStoreContext` alan `public` kurucunun `CS0051` vermesi ve tuketicinin somut sinifa ihtiyaci olmamasi. `MigrationRunner` public kaldi (kurucusu internal; DI fabrikayla kaydeder).
- **`DbDataSource` uyarlayicisi elle yazildi**: `Microsoft.Data.SqlClient` bir `DbDataSource` uygulamasi sunmaz (Npgsql sunar). `SqlServerDataSource` yalnizca `CreateDbConnection()`'i uygular; taban sinifin `CreateCommand` uygulamasi baglanti omrunu Npgsql ile ayni sekilde yonetir.

- **🚨 `EXISTS`/`NOT EXISTS` korelasyonunda BARE tablo adı YAZMA, `QualifyTable(name)` kullan (2026-08-06, Faz 36, K-259)**: `WHERE er.id = eval_case_results.eval_run_id` PostgreSQL/SQL Server'da calisir (bare ad aliassiz FROM'u da bulur) ama SQLite'ta gercek nesne `onek+ad` bitisigidir (K-193) ve bare ad HICBIR ZAMAN eslesmez — "no such column", yalniz CALISMA ANINDA. `RetentionTargetRegistry` (Faz 25) bunu 3 hedefte tasiyordu, Faz 36'nin SQLite testi yakaladi.
- **`SqlDialect.AddNullableBoolean` eklendi (2026-08-05, Faz 28)**: `AddBoolean` `bool` alir ve uc durumlu bir alani (`evet`/`hayir`/`bilgi yok`) tasiyamaz — eksik bilgi sessizce `false` olurdu. `tool_invocations.usage_estimated` bu yuzden nullable yazilir. Okuma tarafinda `DbHelpers.ToBoolean` kullanilir: SQLite mantiksal tip tasimaz ve `long` (0/1) doner (K-195).
- **🚨 Linked-source (K-176) bir tipin `internal` isareti CROSS-ASSEMBLY sayim icin GUVENILMEZ** (2026-08-06, Faz 33, K-247): `MigrationHostedService`'in K-183 sayaci `internal SqlPersistenceRegistration` kullaniyordu; bu tip `AgentPrism.PostgreSql.dll` ve `AgentPrism.SqlServer.dll` icine AYRI AYRI derlenir ve CLR kimligi FARKLIDIR — `UsePostgreSql()` + `UseSqlServer()` birlikte cagrildiginda hicbir `MigrationHostedService` digerinin isaretini GOREMEZ ve cift kayit uyarisi hic tetiklenmez. Sayim/teshis Abstractions'da PAYLASILAN tek bir derlenmis tipe (`SqlPersistenceRegistrationMarker`) tasindi. Ayni tuzak: linked-source icindeki herhangi bir `internal` tipi `IEnumerable<T>` ile SAYMAK istiyorsan, T Abstractions'da olmali.
- **`MigrationRunner` artik `ISqlPersistenceDiagnostics` uygular** (2026-08-06, Faz 33, K-248): `GetSnapshotAsync` migration UYGULAMAZ, yalniz baglanti + bekleyen liste okur. `__migrations` defteri henuz yoksa (DbException) baglanti calisiyor sayilir, tum migration'lar bekliyor kabul edilir — `CanConnect=false` yalniz baglanti KURULAMADIGINDA doner.
- **🚨 Linked-source (K-176) tiplerin AYNI tam nitelikli adi, `Microsoft.AspNetCore.OpenApi`'nin XML yorum onbellegini CATISTIRIR** (2026-08-07, Faz 40, F-76/K-276): `MigrationRunner`, `SqlStoreContext` gibi paylasilan tipler `AgentPrism.SqlServer.xml`/`AgentPrism.Sqlite.xml`/`AgentPrism.PostgreSql.xml`'in HER BIRINE `T:AgentPrism.MigrationRunner` doku kimligiyle yazilir. `AddOpenApi()` kullanan bir tuketici 2+ SQL saglayicisini BIRLIKTE referans verirse (`samples/AgentPrism.Api`'nin K-185 icin bilerek yaptigi gibi), ureticinin XML yorum onbellegi (`OpenApiXmlCommentCache.GenerateCacheEntries()`) tum derlemelerin doku girdilerini TEK sozlukte (derlemeden BAGIMSIZ anahtarla) toplar ve `ArgumentException: An item with the same key has already been added` ile `/openapi/v1.json` 500 doner. K-247'nin "internal isareti cross-assembly guvenilmez" dersinin XML-doc kardesi — burada sorun DERLEME KIMLIGI degil, XML DOKU URETECININ derlemeyi hic ayirt etmemesi. Cozulmedi; `docs/UCUNCU-FAZ-ADAYLARI.md` F-76.

- **🚨 CAGIRANIN VERDIGI bir metin tek basina birincil anahtar olamaz** (2026-08-07, Faz 41, K-278): `sessions.id` bir `AgentSession`/konusma kimligidir ve tabloda BUTUN kiracilar arasinda benzersizdi; `ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id` bir kiracinin digerinin oturumunu UZERINE YAZMASINA izin veriyordu (durum kaybi + sahiplik degisimi). Anahtar `(tenant_id, id)` oldu. **Kural**: kimlik cagirandan geliyorsa anahtar kiraciyi de icermelidir; uuid v7 kimlikler (K-015) bu tuzagi tasimaz cunku uretici bizizdir. SQLite birincil anahtari DEGISTIREMEZ — tablo yeniden kurulur, veri tasinir, indeksler ELLE yeniden olusturulur (indeks ad alani veritabani genelindedir).
- **🚨 Kiraci basina tanimlanan bir politika, kiraci suzgeci OLMAYAN bir veri duzlemiyle calisamaz** (2026-08-07, Faz 41, K-279): `RetentionTargetRegistry` hicbir hedefte `tenant_id` tasimiyordu; bir kiracinin saklama politikasi BUTUN kiracilarin satirlarini siliyordu. `RetentionTargetDefinition` artik bir `TenantPredicate` tasir. Kendi `tenant_id` sutunu OLMAYAN uc hedef (`run_events`, `tool_invocations`, `eval_case_results`) sahibine bakan bir `EXISTS` ile suzulur ve korelasyon FULL NITELENDIRILMIS adla yazilir (K-259'un tuzagi burada da gecerlidir). Yeni bir hedef eklerken `TenantPredicate`'i yazmayi unutma — derleme bunu yakalamaz (kayit bir `record struct`'tir ve alan zorunludur, ama yanlis bir yuklem sessizce calisir).
- **Kapsam kapisi: `TenantCoverageTests`** (2026-08-07, Faz 41): `Stores/` altindaki her public metot ya `TenantCoverageTests.Covered` tablosunda ya `[TenantAgnostic("gerekce")]` ile isaretli olmalidir. Yeni bir metot eklediginde build yesil kalir ama bu test duser. Muafiyet gerekcesi 40 karakterden kisa olamaz (ayri test). Yansima yalniz test projesindedir; oznitelik `Sql.Shared/Internal/` icinde ve `internal`'dir (K-281).
- **Pencere fonksiyonu/filtreli indeks uc diyalekt ayni sozdizim** (K-299): ilk `ROW_NUMBER()`/`COUNT() OVER`; SQLite dogrulandi, SQL Server olculmedi.
- **🚨 Depoda `LoadAllAsync` (kapsamdaki TUM satirlari cekip C#'ta filtreleme) deseni bulursan supheyle yaklas** (2026-08-08, Faz 51): `SqlAgentFileStore.SearchAsync`/`ListChildrenAsync` prefix+glob'u client-side filtreliyordu; SQL'e onek/derinlik/glob indirilerek (LIKE ... ESCAPE) kaldirildi. Onek `EscapeLikeLiteral` ile kacislanip `%` eklenir. SQL Server/SQLite regex TASIMAZ; nihai regex eslesmesi HER ZAMAN .NET `Regex` ile istemcide kalir, SQL yalniz on daraltmadir. `SqlDialect.IsInvalidRegexError`, PostgreSQL'in ARE sozdiziminde GECERSIZ bir .NET deseni (adlandirilmis grup gibi) sunucuya gonderildiginde yakalayip on suzgecsiz yeniden dener.

## SQL Server parametre tuzaklari

- **🚨 Tipi verilmemis `decimal` parametresi `decimal(18,0)` sayilir ve ONDALIK KISIM SESSIZCE KESILIR** (2026-08-04, Faz 23): butun para sutunlari `decimal(20,10)`'dur; `SqlServerDialect.AddDecimal` `Precision = 20`, `Scale = 10` yazar. Yazilmazsa maliyetler tam sayiya yuvarlanir ve hicbir test bunu yakalamaz — yalnizca gidis-donus testi yakalar (`SqlServerDialectTests.Maliyet_ondaligi_kesilmeden_gidip_gelir`).
- **`varbinary(max)` parametresine uzunluk `-1` verilir**: verilmezse SqlClient boyutu degerden cikarir ve 8000 baytin uzerinde hata olusur.
- **Zaman damgalari `DateTimeOffset` olarak, UTC'ye cevrilerek yazilir.** PostgreSQL `timestamptz` icin `DateTime` (`Kind = Utc`) bekler; cevirim `SqlDialect.AddTimestamp` turevlerindedir. `Store` kodu `.UtcDateTime` cagirmaz.
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
  - **Ucuncu ve en keskin ornek: `run_scores`** (K-239, Faz 31). Ayni tabloda IKI sutun TERS ihtiyac tasiyordu: `message_id` PostgreSQL/SQLite'ta `COALESCE(…, '')` ister (SQL Server'da duz sutun yeter), `author` ise TAM TERSI — kimliksiz puanin (author NULL) benzersizlige HIC girmemesi istenir, bu PostgreSQL/SQLite'ta duz sutunla bedavadir ama SQL Server'da indeksi `WHERE author IS NOT NULL` ile FILTRELEMEK gerekir; aksi halde iki NULL yazari "ayni yazar" sayip ikinci puani sessizce birinciyle CATISTIRIRDI. Tek tabloda iki sutunun iki zit yon istemesi bu tuzagin genellemesini ("hep filtrele" veya "hep duz birak") gecersiz kilar — her sutun icin NULL semantigi ayri dusunulmelidir.
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
- **🚨 Paylasilan bir `store`, saglayiciya ozgu bir ADO.NET tipine (`GetFieldValue<string[]>`)
  dogrudan basvurmamalidir — dizi/JSON okumasi HER ZAMAN `Dialect.ReadTextArray`/
  `ReadUuidArray` uzerinden gecer.** `SqlWebhookStore.ReadSubscription` bunu
  atlayip Npgsql'in dogal dizi destegine dayanmisti; PostgreSQL'de sessizce
  calisiyordu ama SQL Server'da `InvalidCastException` verdi. Yeni bir `store`
  yazarken dizi/JSON donen her sutun icin `Dialect.Read*` cagrildigini kontrol et.

## Test altyapisi

- **🚨 `mcr.microsoft.com/mssql/server` bu makinede hâlâ koşmuyor; `azure-sql-edge` artık GÜVENİLİR bir yerel ikame (K-317, 2026-08-07).** Kök sebep, düzeltme (özel `IWaitUntil`, `sqlcmd` gerekmez) ve tekrar dene rehberi: [`sql-server-yerel-test.md`](sql-server-yerel-test.md).
- **Sozlesme testleri `tests/Shared/` altindadir** ve saglayici basina bir entegrasyon test projesine derlenir (`AgentPrism.StoreContracts` ad alani). Yeni bir saglayici eklerken sozlesme testi YAZILMAZ; yalnizca kosucu sinif turetilir. SQLite bu iddianin DORDUNCU kanitidir (K-194).
- **Her test kendi semasini/onekini kullanir** (`t_<16 hex>`), her saglayicida. Bu hem yalitim saglar hem `SchemaName`/`TablePrefix` ayarinin gercekten calistigini her testte dogrular.

## SQLite'a ozgu tuzaklar

SQLite'a ozgu tum notlar (indeks ad alani, upsert, `ExecuteScalarAsync` CLR
tipi, migration kilidi, uuid harf buyuklugu) **taşındı**:
[`sqlite.md`](sqlite.md) (Faz 36, bütçe asimini gidermek icin ayrildi).
