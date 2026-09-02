# SQL Migration ve Goc Kilidi

> `MigrationRunner`, `__migrations` defteri ve goc sirasindaki gecici
> catismalar. Paylasilan katmanin geri kalani:
> [`sql-saglayicilari.md`](sql-saglayicilari.md). Saglayiciya ozgu notlar:
> [`sql-server-tuzaklari.md`](sql-server-tuzaklari.md) ·
> [`postgresql.md`](postgresql.md) · [`sqlite.md`](sqlite.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 133'te ayrildi: `sql-saglayicilari.md` 15.990/16.000 B'ye ulasmisti
> (%0 bosluk) ve K-214 merdiveni bu asimda bolunmeyi zorunlu kilar.

## MigrationRunner

- **`MigrationRunner` artik `ISqlPersistenceDiagnostics` uygular** (2026-08-06, Faz 33, K-248): `GetSnapshotAsync` migration UYGULAMAZ, yalniz baglanti + bekleyen liste okur. `__migrations` defteri henuz yoksa (DbException) baglanti calisiyor sayilir, tum migration'lar bekliyor kabul edilir — `CanConnect=false` yalniz baglanti KURULAMADIGINDA doner.
- **🚨 `__migrations`'in KENDI semasini degistiren islem K-388'in tek-toplu-komut birlestirmesiyle CELISIR** (Faz 67, K-475): SQL Server toplu isi BASTAN derler; `ADD set_name` sonrasi ayni iste `set_name` referansi "Invalid column name" verir, `EXEC` ile SARILAMAZ. Cozum: dongu ONCESI `SqlDialect.UpgradeMigrationsTableAsync` (SQLite'ta K-278 rebuild). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

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
