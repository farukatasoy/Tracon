# SQLite Tuzaklari — Indeks Ad Alani ve Upsert

> `AgentPrism.Sqlite`'a ozgu davranis. Paylasilan katman ve SQL Server icin:
> [`sql-saglayicilari.md`](sql-saglayicilari.md). PostgreSQL icin:
> [`postgresql.md`](postgresql.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 36'da `sql-saglayicilari.md`'nin bütçe asimini gidermek icin ayrildi
> (icerik degismedi, yalniz tasindi).

> Kararlar: K-190 (tablo oneki) … K-197 (SQLitePCLRaw surum sabitleme).
> Ayrintili gerekce icin `docs/KARARLAR.md`.

- **🚨 SQLite'ta indeks (ve tetikleyici/gorunum) adlari VERITABANI GENELINDE
  tektir — sema veya tabloya gore `scope`'lanmis DEGILDIR.** PostgreSQL semaya, SQL
  Server tabloya gore `scope`'lar; SQLite'ta TUM nesneler TEK duz ad
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
  zorunlulugu (`sql-saglayicilari.md`) burada YOKTUR.
- **Yabanci anahtar zorlamasi VARSAYILAN KAPALIDIR**; her baglantida
  `PRAGMA foreign_keys = ON` acikca calistirilir (`SqliteDataSource`), aksi
  halde `REFERENCES ... ON DELETE CASCADE` sessizce yok sayilir.
- **Yeni bir tablo eklemek uc migration + uc sorgu + bir `store` + bir sozlesme testi demektir** (2026-08-05, Faz 29): `voice_sessions` icin dokunulanlar — `PostgreSql/Migrations/0016_*.sql`, `SqlServer/Migrations/0004_*.sql`, `Sqlite/Migrations/0004_*.sql`, `SqlQueriesBase` (+2 ozellik), uc `*Queries.cs`, `Sql.Shared/Stores/SqlVoiceSessionStore.cs`, uc `*BuilderExtensions.cs` icinde `services.Replace(...)`, uc `*TestContext.cs`, dort sozlesme turevi. 🚨 SQL Server'da **MERGE KULLANILMAZ** (K-177): once `UPDATE ... WITH (UPDLOCK, SERIALIZABLE)`, sonra `IF @@ROWCOUNT = 0 INSERT`. 🚨 `MigrationTests`'teki sabit tablo sayisi kirilir (38 → 39) — bu bilinclidir, guncelleyin.
- **🚨 `EXISTS`/`NOT EXISTS` korelasyonunda BARE tablo adı YAZMA, `QualifyTable(name)` kullan** (2026-08-06, Faz 36, K-259): PostgreSQL/SQL Server'da bare ad aliassiz FROM'u da bulur ama SQLite'ta gercek nesne `onek+ad` bitisigidir (K-193) ve bare ad HICBIR ZAMAN eslesmez — "no such column", yalniz CALISMA ANINDA. `RetentionTargetRegistry` (Faz 25) bunu 3 hedefte (`eval_case_results`, `workflow_checkpoints`, `attachments`) tasiyordu, Faz 36'nin SQLite'a karsi kayan sozlesme testi yakaladi. Ayrinti: `sql-saglayicilari.md`.
- **🚨 SQLite `LIKE` VARSAYILAN OLARAK BUYUK/KUCUK HARFE DUYARSIZDIR** (2026-08-08, Faz 51): bir sorguyu client-side `StringComparison.Ordinal` filtrelemeden SQL `LIKE`'a tasirken bu fark gozden kacar — Postgres/SQL Server `LIKE` (varsayilan collation'da) buyuk/kucuk harfe duyarlidir. Duzeltme: baglanti acilirken `PRAGMA case_sensitive_like = ON;` (`SqliteDataSource.OnStateChange`, digger PRAGMA'larla ayni yerde). Depoda baska hicbir `LIKE` kullanimi olmadigi icin global ayar guvenliydi; yeni bir `LIKE` eklerken bu pragmanin hala varsayilan davranisi (artik duyarlı) verdigini unutma.
