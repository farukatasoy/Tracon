# SQL Saglayicilari — Paylasilan Katman ve SQL Server Tuzaklari

> `AgentPrism.Sql.Shared`, `AgentPrism.SqlServer` ve saglayicilarin ortak
> davranisi. PostgreSQL'e ozgu notlar icin: [`postgresql.md`](postgresql.md).
> SQLite'a ozgu notlar icin: [`sqlite.md`](sqlite.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.

## Paylasilan katman

- **`AgentPrism.Sql.Shared` bir paket DEGILDIR ve csproj tasimaz** (2026-08-04, Faz 23): dosyalar `<Compile Include="../AgentPrism.Sql.Shared/**/*.cs" />` ile hem `AgentPrism.PostgreSql` hem `AgentPrism.SqlServer` icine derlenir. Ayni `internal` tipler iki AYRI derlemede yasar, catisma olmaz. Karar K-176.
- **🚨 Paylasilan hicbir dosya `Npgsql` veya `Microsoft.Data.SqlClient` ad alanina referans VEREMEZ.** Saglayiciya ozgu her sey `SqlDialect` turevlerinden gecer. Bu kural bozulursa ikinci saglayici derlenmez ve neden aylar sonra anlasilir.
- **🚨 `SqlQueriesBase`'e yeni sorgu eklerken HER alt sinifta karsiligini yaz.** Ozellikler `{ get; protected set; } = string.Empty;`'dir; yazilmayan sorgu bos metin kalir ve hata yalnizca CALISMA ANINDA gorunur — derleme de test de kirilmaz. Sorgu sayisi saglayici sayisiyla carpiliyorsa desen degistirilir (K-198: tek kayit + `SqlDialect` sablon yontemi). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`SqlDialect.QualifyTable(tableName)` eklendi (2026-08-05, Faz 25)**: saglayicidan bagimsiz SQL uretimi (`RetentionTargetRegistry` gibi) tablo adini nitelendirmek icin dogrudan `{Schema}.{tablo}` YAZAMAZ — PostgreSQL/SQL Server nokta ile, SQLite (K-193) onek bitistirerek nitelendirir. Varsayilan uygulama nokta ile birlestirir; `SqliteDialect` ezer.
- **`Store` siniflari `internal`'dir** (2026-08-04, Faz 23): `SqlRunStore`, `SqlSessionStore`, … Sebep `internal SqlStoreContext` alan `public` kurucunun `CS0051` vermesi ve tuketicinin somut sinifa ihtiyaci olmamasi. `MigrationRunner` public kaldi (kurucusu internal; DI fabrikayla kaydeder).

- **🚨 `EXISTS`/`NOT EXISTS` korelasyonunda BARE tablo adı YAZMA, `QualifyTable(name)` kullan (2026-08-06, Faz 36, K-259)**: `WHERE er.id = eval_case_results.eval_run_id` PostgreSQL/SQL Server'da calisir (bare ad aliassiz FROM'u da bulur) ama SQLite'ta gercek nesne `onek+ad` bitisigidir (K-193) ve bare ad HICBIR ZAMAN eslesmez — "no such column", yalniz CALISMA ANINDA. `RetentionTargetRegistry` (Faz 25) bunu 3 hedefte tasiyordu, Faz 36'nin SQLite testi yakaladi.
- **`SqlDialect.AddNullableBoolean` eklendi (2026-08-05, Faz 28)**: `AddBoolean` `bool` alir ve uc durumlu bir alani (`evet`/`hayir`/`bilgi yok`) tasiyamaz — eksik bilgi sessizce `false` olurdu. `tool_invocations.usage_estimated` bu yuzden nullable yazilir. Okuma tarafinda `DbHelpers.ToBoolean` kullanilir: SQLite mantiksal tip tasimaz ve `long` (0/1) doner (K-195).
- **`SqlDialect.ArrayContains(column, paramName)` eklendi (2026-08-18, Faz 64)**: "sütun bir dizi parametrenin içinde mi" için — `= ANY(events)` deseninin ayna yönü. PostgreSQL `col = ANY(@dizi)`; SQL Server/SQLite dizi JSON metnidir (K-182), `EXISTS (SELECT 1 FROM OPENJSON/json_each(@dizi) WHERE value = col)` ile eşlenir. `DataSubjectTargetRegistry` (Faz 64) dokuz hedefi bununla filtreler.
- **Kimlik bazlı toplu silme, `IRetentionStore`'un cutoff-tabanlı arayüzünü YENİDEN KULLANMAZ; paralel bir kayıt (`DataSubjectTargetRegistry`) yazılır (2026-08-18, Faz 64)**: yapı farklı (tek `@cutoff` yerine çok parametreli `IN`/`EXISTS`, tabloya göre değişen topoloji — `responses`'ın `tenant_id`'si bile yok, `sessions` üzerinden EXISTS ile doğrulanır). Ortak olan yalnız K-198'in "tek kayıt, üç dialekt şablonu" deseni.
- **🚨 Depoda `LoadAllAsync` (kapsamdaki TUM satirlari cekip C#'ta filtreleme) deseni bulursan supheyle yaklas** (Faz 51): suzgec SQL'e indirilir (`LIKE ... ESCAPE`, onek `EscapeLikeLiteral` ile kacislanir). 🚨 SQL Server/SQLite regex TASIMAZ — nihai eslesme HER ZAMAN istemcide `Regex` ile kalir, SQL yalniz on daraltmadir. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Linked-source (K-176) bir tipin `internal` isareti CROSS-ASSEMBLY sayim icin GUVENILMEZ** (Faz 33, K-247): ayni tip her saglayici derlemesine AYRI derlenir, CLR kimligi FARKLIDIR. Linked-source icindeki bir `internal` tipi `IEnumerable<T>` ile SAYMAK istiyorsan T **Abstractions'da** olmali. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`MigrationRunner` artik `ISqlPersistenceDiagnostics` uygular** (2026-08-06, Faz 33, K-248): `GetSnapshotAsync` migration UYGULAMAZ, yalniz baglanti + bekleyen liste okur. `__migrations` defteri henuz yoksa (DbException) baglanti calisiyor sayilir, tum migration'lar bekliyor kabul edilir — `CanConnect=false` yalniz baglanti KURULAMADIGINDA doner.
- **🚨 Linked-source (K-176) tiplerin AYNI tam nitelikli adi, `Microsoft.AspNetCore.OpenApi`'nin XML yorum onbellegini CATISTIRIR** (Faz 40, K-276): 2+ SQL saglayicisini birlikte referans veren ve `AddOpenApi()` kullanan bir tuketicide `/openapi/v1.json` **500** doner. K-247'nin XML-doc kardesi. Ayrinti ve durum: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md), `docs/ADAYLAR.md` F-76.

- **🚨 CAGIRANIN VERDIGI bir metin tek basina birincil anahtar olamaz** (Faz 41, K-278): kimlik cagirandan geliyorsa anahtar **kiraciyi da icermelidir** (`sessions` → `(tenant_id, id)`); uuid v7 (K-015) bu tuzagi tasimaz. 🚨 SQLite birincil anahtari DEGISTIREMEZ — tablo yeniden kurulur, veri tasinir, indeksler ELLE kurulur. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Kiraci basina tanimlanan bir politika, kiraci suzgeci OLMAYAN bir veri duzlemiyle calisamaz** (Faz 41, K-279): `RetentionTargetDefinition.TenantPredicate`'i unutma — yanlis yuklem sessizce calisir ve BUTUN kiracilarin satirlarini siler. Kendi `tenant_id`'si olmayan hedef sahibine bakan `EXISTS` ile suzulur (korelasyon FULL NITELENDIRILMIS adla — K-259). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Kapsam kapisi: `TenantCoverageTests`** (2026-08-07, Faz 41): `Stores/` altindaki her public metot ya `TenantCoverageTests.Covered` tablosunda ya `[TenantAgnostic("gerekce")]` ile isaretli olmalidir. Yeni bir metot eklediginde build yesil kalir ama bu test duser. Muafiyet gerekcesi 40 karakterden kisa olamaz (ayri test). Yansima yalniz test projesindedir; oznitelik `Sql.Shared/Internal/` icinde ve `internal`'dir (K-281).
- **Preview'i ayri bir `COUNT` yerine gercek `DELETE`'i calistirip `ROLLBACK`/`COMMIT` ile ayirmak DAHA GUVENLI** (Faz 64): iki ayri sorgu seti zamanla sapar; tek dogruluk kaynagi kalir.

## SQL Server parametre tuzaklari

- **🚨 Tipi verilmemis `decimal` parametresi `decimal(18,0)` sayilir ve ONDALIK KISIM SESSIZCE KESILIR** (Faz 23): butun para sutunlari `decimal(20,10)`'dur; `SqlServerDialect.AddDecimal` `Precision = 20`, `Scale = 10` yazar. Yazilmazsa maliyetler tam sayiya yuvarlanir ve **hicbir test bunu yakalamaz** — yalnizca gidis-donus testi yakalar.
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
- **🚨 `IDENTITY` var olan bir tabloya `ALTER TABLE ... ADD` ile EKLENEMEZ** (Faz 64): yalniz `CREATE TABLE` aninda tanimlanabilir. Yerine ayri bir `CREATE SEQUENCE` + `ADD col bigint NOT NULL DEFAULT (NEXT VALUE FOR ...)`. 🚨 Test altyapisinda sema silme sirasi TABLOLAR → SEQUENCE'lar → `DROP SCHEMA` olmali. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`nvarchar(max)` INDEKSLENEMEZ.** Anahtar/indeks sutunlari `nvarchar(200)` (veya `nvarchar(64)`/`(128)`) boyutludur; serbest metin `nvarchar(max)` kalir. Nonclustered indeks anahtar siniri 1700 bayt, clustered 900 bayt.

## Semaya ozgu farklar

- **🚨 NULL benzersizligi TERS calisir** (K-184): PostgreSQL'de NULL hicbir NULL'a esit degildir → `COALESCE`'li ifade indeksi gerekiyordu. SQL Server NULL'lari ESIT sayar → duz `UNIQUE` yeter. Ama ayni kural `jobs (schedule_id, scheduled_for)` kisitinda ters tarafa duser: SQL Server ikinci bir zamanlamasiz isi engellerdi, bu yuzden orada kisit `WHERE schedule_id IS NOT NULL` filtreli benzersiz indekstir. Sorgu tarafinda eslesme `ISNULL(c, N'') = ISNULL(@p, N'')` ile yazilir; `@p` NULL iken duz `=` UNKNOWN dondururdu.
  - **En keskin ornek `run_scores`** (K-239, Faz 31): ayni tabloda IKI sutun TERS ihtiyac tasidi. `message_id` PostgreSQL/SQLite'ta `COALESCE(…, '')` ister; `author` ise TAM TERSI — kimliksiz puan benzersizlige hic girmemeli, bu PostgreSQL/SQLite'ta bedava ama SQL Server'da indeksi `WHERE author IS NOT NULL` ile FILTRELEMEK gerekir. Genelleme ("hep filtrele" / "hep duz birak") gecersizdir: **her sutun icin NULL semantigi ayri dusunulur.**
- **🚨 `uniqueidentifier` siralamasi bayt sirasina gore DEGILDIR** (son alti bayt once karsilastirilir). uuid v7 (K-015) SQL Server'da zaman sirali GORUNMEZ ve kumelenmis birincil anahtar sayfa bolunmesi uretir. Yogun tablolarda PK `NONCLUSTERED`, kumelenmis indeks `(zaman_sutunu, id)` uzerindedir (K-180).
- **`ISJSON` kisitlari yalnizca PostgreSQL'de `jsonb`/`json` olan sutunlarda vardir** — davranis esitligi icin. `run_events.payload` ve `tool_invocations.arguments/result` PostgreSQL'de `text`'tir (gecerli JSON olmayabilir) ve kisit TASIMAZ. `audit_log.before/after` de kisit tasimaz: gozlemlenebilirlik islevselligi bozmaz.
- **Diziler JSON metnidir** (K-182): `OPENJSON` ile acilir, `[key]` 0 tabanlidir ve `UNNEST ... WITH ORDINALITY`'nin `ord - 1` degerine birebir denk gelir.

## Iki dalli upsert desenindeki gizli tuzaklar (K-187, K-188, K-189)

> Bu uc kural, `azure-sql-edge` ile ilk gercek koşumda 204 testin 204'unu birden
> kirdi. Vaka anlatisi [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

- **🚨 `@@ROWCOUNT` onekini unutma.** `ROWCOUNT` tek basina gecersiz sozdizimidir;
  hicbir derleme veya format kapisi yakalamaz.
- **🚨 Iki dalli upsert (`UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0 INSERT ...
  OUTPUT`) UPDATE 0 satir etkiledigende satiri IKINCI sonuc kumesine yazar.**
  `DbHelpers.ReadSingleAsync`/`ExecuteScalarAsync` bu yuzden `NextResultAsync`
  ile sonraki kumelere duser; PostgreSQL'in tek kumeli `RETURNING`'inde zararsiz.
- **🚨 Paylasilan `store` saglayiciya ozgu ADO.NET tipine basvurmaz.** Dizi/JSON
  okumasi HER ZAMAN `Dialect.ReadTextArray`/`ReadUuidArray` uzerinden gecer.
  Yeni bir `store` yazarken dizi/JSON donen her sutunda bunu kontrol et.

## Test altyapisi

- **`mcr.microsoft.com/mssql/server` bu makinede artık koşuyor** (K-386). `azure-sql-edge` ikamesi (K-317) yedek kalır. Kurulum, tekrar dene ve **performans** (K-387..K-391, ayri sema yerine sinif basina paylasilan sema + `ResetDataAsync`): [`sql-server-yerel-test.md`](sql-server-yerel-test.md).
- **Sozlesme testleri `tests/Shared/` altindadir** ve saglayici basina bir entegrasyon test projesine derlenir (`AgentPrism.StoreContracts` ad alani). Yeni bir saglayici eklerken sozlesme testi YAZILMAZ; yalnizca kosucu sinif turetilir. SQLite bu iddianin DORDUNCU kanitidir (K-194).

- **Iki dalli upsert `OUTPUT` GEREKTIRMEZSE cok basitlesir** (2026-08-19, Faz 65): `UpsertAsync` deger dondurmuyorsa `UPDATE WITH (UPDLOCK, SERIALIZABLE) ...; IF @@ROWCOUNT = 0 INSERT ...;` yeter — K-187/188/189'un asil tuzagi (`OUTPUT` ikinci sonuc kumesine duser) hic devreye girmez. `tenant_provider_bindings`/`tenant_egress_policies` bunu kullanir.

- **🚨 `__migrations` defterinin KENDI semasini degistiren islem, K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ALTER TABLE ... ADD set_name` sonrasi AYNI iste `InsertMigration`'in `set_name` referansli SABIT metni "Invalid column name" verir ve `EXEC` ile de SARILAMAZ. Cozum: dongu BASLAMADAN ONCE calisan `SqlDialect.UpgradeMigrationsTableAsync`. SQLite (K-278) bunu KODDA rebuild ile yapar. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## SQLite'a ozgu tuzaklar

SQLite'a ozgu tum notlar (indeks ad alani, upsert, `ExecuteScalarAsync` CLR
tipi, migration kilidi, uuid harf buyuklugu) **taşındı**:
[`sqlite.md`](sqlite.md) (Faz 36, bütçe asimini gidermek icin ayrildi).

- **🚨 `runs` gibi ordinal okunan bir tabloya sütun eklerken sıra ÜÇ dialektte de sona eklenir** (2026-08-19, Faz 68): `SqlRunStore.ReadRun` sabit konumdan okur ve üç `runColumns` metni birebir aynı sırayı taşımak ZORUNDADIR. Faz 68 sonunda son ordinal **51**'dir (40-41 attribution · 42-45 token kırılımı · 46 cache maliyeti · 47-50 ağaç token · 51 ağaç cache maliyeti). Aynı kural `SelectRunStatistics`'in sonuç kümeleri için de geçerlidir: bugün **sekiz** küme var ve yenisi SONA eklenir.
- **Etiket haritası (`runs.labels`) için `jsonb` seçimi ve üç dialektin süzgeç biçimi**: [`postgresql.md`](postgresql.md) (Faz 68, K-479). Kısaca: SQL Server/SQLite'ta harita JSON METNİDİR (`OPENJSON`/`json_each`, K-182 deseni) ve indeks YOKTUR — serbest bir etiket kümesi, hesaplanmış sütun indeksinin isteyeceği önceden bilinen anahtar listesini veremez.
- **🚨 UPSERT'te bir alanı düz üzerine yazmak, ONU DOĞRU BİLEN yazımı silebilir** (2026-08-19, Faz 68, K-486): kuyruğa alınmış `run` (Faz 46) `StartRunAsync`'i iki kez çağırır — yer tutucu satır HTTP isteği içinde (kullanıcı BİLİNİR), sonra arka plan işçisi (HTTP'ye bağlı bir bağlam `null` döner). `user_id = EXCLUDED.user_id` atfı SİLERDİ; `COALESCE(EXCLUDED.user_id, user_id)` korur. `InMemoryRunStore` aynı davranışı kodda tekrarlar. Bir alan "set → unset" yönünde MEŞRU olarak değişmiyorsa `COALESCE` her zaman doğrudur.
