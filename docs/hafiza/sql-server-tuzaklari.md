# SQL Server Tuzaklari — Parametre, Sorgu, Sema Farklari, Upsert

> `Tracon.SqlServer`'a ozgu davranis ve onun diger saglayicilardan ayrildigi
> noktalar. Paylasilan katman icin: [`sql-saglayicilari.md`](sql-saglayicilari.md).
> PostgreSQL icin: [`postgresql.md`](postgresql.md). SQLite icin:
> [`sqlite.md`](sqlite.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `sql-saglayicilari.md` 15.984/16.000 B'ye ulasmisti (%0
> bosluk) ve kuyrugu coktan `HAFIZA-GECMISI.md`'ye tasinmisti -- K-214'un
> merdiveninde sira GERCEK BOLUNMEYE gelmisti. `sqlite.md` (Faz 36) ayni desen.

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
- **🚨 Agrega ifadesi dis referans (`r.*`) ile ic sutunu birlikte okuyamaz** (2026-10-14, F-419, olculdu): `COUNT(DISTINCT CASE WHEN UPPER(sub.x) <> UPPER(r.x) ...)` OUTER APPLY icinde *"Multiple columns are specified in an aggregated expression containing an outer reference"* verir; PostgreSQL ve SQLite kabul eder. Cozum: dis degeri alt sorguya ic `CROSS APPLY (SELECT ... FROM r) AS own` ile getir, agregada `own.x` oku (`SqlServerQueries` agac sayimi).
- **Veri degistiren CTE T-SQL'de YOKTUR.** PostgreSQL'in `WITH updated AS (UPDATE ... RETURNING)` yapisi `DECLARE @t TABLE` + `OUTPUT ... INTO @t` ile kurulur (`ReportJobItem`).
- **`MERGE` kullanilmaz** (K-177). Upsert deseni: `UPDATE ... WITH (UPDLOCK, SERIALIZABLE) ... OUTPUT inserted.*` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT inserted.*`. Iki dal AYNI sutunlari dondurmelidir; yoksa paylasilan okuyucu bozulur. Store bu metni `SqlStoreContext.ExecuteUpsertAsync` ile kosar (asagida F-313).
- **`@@ROWCOUNT` bilesik kosulda once bir degiskene alinir.** `IF @@ROWCOUNT = 0 AND NOT EXISTS (...)` yazarsan alt sorgu once degerlendirilirse sayac sifirlanir (`UpsertExperiment`).
- **`CREATE SCHEMA` bir toplu islemin ILK ifadesi olmak zorundadir**; kosullu calistirma `EXEC(N'CREATE SCHEMA ...')` ile sarilir.
- **🚨 `IDENTITY` var olan bir tabloya `ALTER TABLE ... ADD` ile EKLENEMEZ** (Faz 64): yalniz `CREATE TABLE` aninda tanimlanabilir. Yerine ayri bir `CREATE SEQUENCE` + `ADD col bigint NOT NULL DEFAULT (NEXT VALUE FOR ...)`. 🚨 Test altyapisinda sema silme sirasi TABLOLAR → SEQUENCE'lar → `DROP SCHEMA` olmali. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`nvarchar(max)` INDEKSLENEMEZ.** Anahtar/indeks sutunlari `nvarchar(200)` (veya `nvarchar(64)`/`(128)`) boyutludur; serbest metin `nvarchar(max)` kalir. Nonclustered indeks anahtar siniri 1700 bayt, clustered 900 bayt.

## Semaya ozgu farklar

- **🚨 NULL benzersizligi saglayicilar arasinda TERS calisir** (K-184): PostgreSQL'de NULL hicbir NULL'a esit degil (`COALESCE`'li ifade indeksi gerekir), SQL Server ESIT sayar (duz `UNIQUE` yeter). `jobs (schedule_id, scheduled_for)`'da kural ters tarafa duser — orada `WHERE schedule_id IS NOT NULL` filtreli indeks. Upsert'te NULL-guvenli eslesme `EXISTS (SELECT c INTERSECT SELECT @p)` ile yazilir; `ISNULL(c, N'') = ISNULL(@p, N'')` arama yapamaz (F-313, asagida). Vakalar: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
  - **En keskin ornek `run_scores`** (K-239, Faz 31): ayni tabloda IKI sutun TERS ihtiyac tasidi. `message_id` PostgreSQL/SQLite'ta `COALESCE(…, '')` ister; `author` ise TAM TERSI — kimliksiz puan benzersizlige hic girmemeli, bu PostgreSQL/SQLite'ta bedava ama SQL Server'da indeksi `WHERE author IS NOT NULL` ile FILTRELEMEK gerekir. Genelleme ("hep filtrele" / "hep duz birak") gecersizdir: **her sutun icin NULL semantigi ayri dusunulur.**
- **🚨 `uniqueidentifier` siralamasi bayt sirasina gore DEGILDIR** (son alti bayt once karsilastirilir). uuid v7 (K-015) SQL Server'da zaman sirali GORUNMEZ ve kumelenmis birincil anahtar sayfa bolunmesi uretir. Yogun tablolarda PK `NONCLUSTERED`, kumelenmis indeks `(zaman_sutunu, id)` uzerindedir (K-180).
- **`ISJSON` kisitlari yalnizca PostgreSQL'de `jsonb`/`json` olan sutunlarda vardir** — davranis esitligi icin. `run_events.payload` ve `tool_invocations.arguments/result` PostgreSQL'de `text`'tir (gecerli JSON olmayabilir) ve kisit TASIMAZ. `audit_log.before/after` de kisit tasimaz: gozlemlenebilirlik islevselligi bozmaz.
- **Diziler JSON metnidir** (K-182): `OPENJSON` ile acilir, `[key]` 0 tabanlidir ve `UNNEST ... WITH ORDINALITY`'nin `ord - 1` degerine birebir denk gelir.

## Iki dalli upsert tuzaklari (K-187, K-188, K-189 — 204 testi birden kirdi)

- **🚨 `@@ROWCOUNT` onekini unutma.** `ROWCOUNT` tek basina gecersiz sozdizimidir;
  hicbir derleme veya format kapisi yakalamaz.
- **🚨 `UPDATE ... OUTPUT` + `IF @@ROWCOUNT = 0 INSERT ... OUTPUT`, UPDATE 0 satir
  etkiledigende satiri IKINCI sonuc kumesine yazar.** `DbHelpers.ReadSingleAsync`/
  `ExecuteScalarAsync` bu yuzden `NextResultAsync` ile duser; PostgreSQL'in tek
  kumeli `RETURNING`'inde zararsiz.
- **🚨 Paylasilan `store` saglayiciya ozgu ADO.NET tipine basvurmaz.** Dizi/JSON
  okumasi HER ZAMAN `Dialect.ReadTextArray`/`ReadUuidArray` uzerinden gecer; ayni
  kural bir `DbConnection`'i somut tipe CAST etmek icin de gecerlidir (K-545).
  Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Paylaşılan bir sütun listesini yeniden adlandırmak, o listeyi ELLE TEKRARLAYAN bir sağlayıcı sorgusunu SESSİZCE bayat bırakır** (2026-09-03, Faz 137): `JobColumns` sabiti üç sağlayıcıda paylaşılırken SQL Server'ın `LeaseJob`'ı aynı listeyi `inserted.` önekiyle ELLE yazıyordu. `kind` → `handler_key` yeniden adlandırması yirmi sorguyu düzeltti, o birini kaçırdı; derleyici SQL metnini görmez, PostgreSQL/SQLite snapshot'ları o sorguyu içermez ve kusur yalnız SQL Server entegrasyon koşumunda "Invalid column name 'kind'" olarak çıktı. Çözüm listeyi TÜRETMEKtir (`InsertedJobColumns = Qualify(JobColumns, "inserted.")`) — okuyucu ORDINAL eşlediği için sıra da garanti altına girer. Sütun listesi değiştirirken `grep -rn "inserted\.\|excluded\.\|EXCLUDED\." src/Tracon.{SqlServer,Sqlite,PostgreSql}/Internal/` ile elle yazılmış her kopyayı tara.

- **Iki dalli upsert `OUTPUT` GEREKTIRMEZSE cok basitlesir** (2026-08-19, Faz 65): `UpsertAsync` deger dondurmuyorsa `UPDATE WITH (UPDLOCK, SERIALIZABLE) ...; IF @@ROWCOUNT = 0 INSERT ...;` yeter — K-187/188/189'un asil tuzagi (`OUTPUT` ikinci sonuc kumesine duser) hic devreye girmez. `tenant_provider_bindings`/`tenant_egress_policies` bunu kullanir.  
  *(Faz 155'te `sql-saglayicilari.md`'den butce icin tasindi.)*

- **🚨 `SERIALIZABLE` range lock SARMALANMIS bir sutun uzerinde TUTMAZ** (2026-09-07, F-215, KAPANDI): `UpsertRunScore`'un iki dalli deseni `UPDATE ... WITH (UPDLOCK, SERIALIZABLE)` ile korunur diye tasarlandi, ama predicate `ISNULL(message_id, N'') = ISNULL(@message_id, N'')` yaziyordu. Sarmalanmis sutun **sargable degildir**: SQL Server `run_scores_target_author_name_idx` uzerinde anahtar araligini kilitleyemiyordu, iki esamanli oturum da `@@ROWCOUNT = 0` gorup `INSERT` ediyordu. Ayni kayit ikinci bir sapma tasiyordu: SQL Server ham `message_id`'yi indeksliyordu (`NULL` ≠ `''`), Postgres/SQLite `COALESCE(message_id, '')` ifadesini indeksler (`NULL` = `''`) — sevk edilen benzersizlik sozlesmesi saglayiciya gore farkliydi. **Cozum** (`0037_run_score_message_key.sql`): `message_key AS ISNULL(message_id, N'') PERSISTED` computed column + index'i ona tasima + predicate'i `message_key = ISNULL(@message_id, N'')` yapma — ikisini birden kapatir, cunku artik indekslenen sutunun KENDISI `NULL`/`''` normalizasyonunu tasir. 🚨 Iki dalli bir upsert yazarken `WHERE` yan tumcesindeki her indeks sutununun **ciplak** oldugunu dogrula; `ISNULL`/`COALESCE`/`CAST` gerekiyorsa sarmali sutuna degil PARAMETREYE uygula, ya da persisted computed column indeksle.
  - Sargable predicate tek basina yetmedi; F-215 bunu bes denemelik bir yeniden denemeyle orttu. **Gercek sebep aşağıdaki F-313'tür** (islem yoktu); yeniden deneme kaldirildi. Bu notun eski hali "uc ISNULL kardesi yaris uretmiyor" diyordu — **yanlisti**: o olcum ayni anahtarı TEKRAR yaziyordu (UPDATE dali), yeni anahtarin ILK yazimini degil.
- **🚨 Iki dalli upsert ISLEM ICINDE kosmazsa atomik DEGILDIR** (2026-10-11, F-313, KAPANDI). Range lock islem kadar yasar; autocommit'te her ifade kendi islemidir, kilit UPDATE ile biter ve YENI bir anahtari yazan iki oturum da INSERT dalina girer. 28 upsert'in 20'si boyle kosuyordu (yalniz zaten `DbTransaction` acan 6'si guvendeydi). Belirti tam pakette arada bir `UNIQUE KEY` ihlaliydi; `UpsertRaceTests` (her turda TAZE anahtar, 12 yazar × 20 tur) sakin makinede 10 vakanin 10'unu her seferinde kirmizi yapti. Cozum: `SqlStoreContext.ExecuteUpsertAsync` — `SqlDialect.UpsertRequiresTransaction` (yalniz SQL Server `true`) ise ADO.NET `DbTransaction` acar (metinde `BEGIN TRAN` degil, K-388); PostgreSQL/SQLite'in tek ifadelik `ON CONFLICT`'i icin ek round trip yoktur. **Bedel:** SQL Server'da upsert basina iki round trip (BEGIN, COMMIT).
  - **🚨 Islem acinca SARMALANMIS sutun DEADLOCK uretir** (ayni gun olculdu). `ISNULL(script_name, N'') = ISNULL(@p, N'')` arama yapamaz; UPDATE komsu anahtarlari okur, elemedigi satirlardaki U kilitleri paylasimli range kilidine duser, iki oturum da birinin INSERT'ini bekler → 1205. Autocommit bunu gizliyordu (kilit hemen bitiyordu) ve yerine ihlal uretiyordu. NULL-guvenli ve arama yapabilen esitlik: `EXISTS (SELECT col INTERSECT SELECT @p)`. `IS NOT DISTINCT FROM` ayni anlamdadir ama SQL Server 2022 ister; 2019 desteklenir. `skill_script_grants`, `quotas`, `tool_approval_rules` buna gecti. Not: INTERSECT `''` ile NULL'u AYIRIR (SQL Server'in kendi unique indeksi gibi); eski ISNULL eslesmesi ikisini birlestiriyordu. Uc yuzey de bos dizeyi NULL'a cevirerek yazar.
  - **Kapi:** `SqlServerUpsertAtomicityTests` (`Tracon.Sql.Shared.UnitTests`) — iki dalli her SQL Server upsert'i ya `ExecuteUpsertAsync` ile ya da acik islemli komutla kosulmali; WHERE'de sarmalanmis anahtar sutunu yasak. Davranis: `UpsertRaceTests` (`Tracon.SqlServer.IntegrationTests`).
- **`ISJSON(N'null')` SIFIRDIR: `null` literali bir jsonb sütununa YAZILAMAZ.**
  `CHECK (ISJSON(<sütun>) = 1)` taşıyan her sütun (`job_schedules.payload`,
  `jobs.payload`, `eval_suites.checks`, `eval_case_results.scores`, …) `null`
  literalini **reddeder**; Postgres ve SQLite onu sessizce kabul eder. Bu, bir
  sağlayıcı sapmasının en sessiz biçimidir: bellek-içi ve Postgres testleri
  yeşil kalır, yalnız SQL Server INSERT'i `CHECK constraint` ihlaliyle düşer.
  Ölçüldü (`HATA-S4-002`): `payload` alanı olmayan bir zamanlama kaydı SQL
  Server'da `job_schedules_payload_json` ihlali veriyordu. **Boş değer boş
  DİZİDİR** (K-826) — üç sağlayıcıyı birden tatmin eden tek boşluk odur.
  `SqlEvalStore` bunu `scores` için elle biliyordu; kural artık `RawJson`'ın
  kendisindedir ve üç store da aynı yazımı paylaşır.
