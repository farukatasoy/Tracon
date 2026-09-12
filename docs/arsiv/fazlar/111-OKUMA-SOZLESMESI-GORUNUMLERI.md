# Faz 111 — Okuma Sözleşmesi Görünümleri

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-26-ef-core-uyum-olcumu.md`](../../kesif/2026-08-26-ef-core-uyum-olcumu.md) — kalem **P4**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 110](110-TUKETICI-BAGLANTI-DUZLEMI.md) — `docs-site/.../guides/ef-core.md` sayfası orada doğar; bu faz ona okuma bölümünü ekler. Kod bağımlılığı yoktur
> **Paketler:** `Tracon.PostgreSql`, `.SqlServer`, `.Sqlite` — yalnız migration setleri
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç set** (PostgreSQL + SQL Server + SQLite). Numaralar sağlayıcı başına bağımsızdır (K-178) ve uygulama anında alınır
> **Public API:** C# yüzeyi yalnız üç `EnableReadViews` bool alanıyla büyüdü (üç `Options` sınıfına birer tane — bkz. "Gerçekleşen Public API"). Asıl taahhüt **kalıcı bir veri sözleşmesidir**: bir görünüm yayımlandıktan sonra sütun kaybetmez. Bu, `Shipped.txt`'in kapsamadığı bir taahhüttür ve geri alması pahalıdır
> **Tüketici yüzeyi:** Site — yeni referans sayfası `docs-site/src/content/docs/reference/read-views.md` · değişen: `guides/ef-core.md`, `packages.md` · Sevk edilen: üç sağlayıcı `README.md`'si
> **Manuel test alanı:** [`manuel-test/03-KALICILIK-POSTGRESQL.md`](../../manuel-test/03-KALICILIK-POSTGRESQL.md) · [`manuel-test/04-KALICILIK-DIGER.md`](../../manuel-test/04-KALICILIK-DIGER.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show bb364df:docs/arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tüketici kendi raporunda koşu maliyetini kendi entity'sinin yanında göstermek ister. Bugün iki yol var, ikisi de kötü: HTTP'den sayfa sayfa çekip bellekte join, ya da iç tablolara doğrudan bağlanıp bir sonraki sürümde kırılmayı göze alma. Faz, üçüncü yolu açar: **sürümlü, dar, salt-okunur bir görünüm sözleşmesi.** Görünüm yalnız ergonomi değildir.

## Bitiş Ölçütleri (DoD)

- [x] Üç sağlayıcıda `runs_v1` görünümü kurulur ve 111.2 sütun tablosunu birebir karşılar — `ReadViewColumnSetTests` (6/6) + gerçek `\d+` çıktısı aşağıda
- [x] `total_cost` üç terimi de içerir; `NULL` anlamı korunur (case 3 kanıtlar) — `ReadViewContractTests.Total_cost_is_null_not_zero_when_pricing_is_undefined`, üç sağlayıcıda
- [x] Görünüm toplamı ile `RunStatistics.TotalCost` üç sağlayıcıda birebir eşleşir — `ReadViewContractTests.Total_cost_in_the_view_matches_the_stores_own_statistics`
- [x] Toplam terim kapısı çalışıyor: `runs` tablosuna sahte bir `*_cost` sütunu eklendiğinde `ReadViewCostTermTests` **kırmızı** olur — build-time, `CostAddends` reflection ile
- [x] Sütun kümesi kapısı çalışıyor: bir sütun düşürüldüğünde `ReadViewColumnSetTests` **kırmızı** olur — elle doğrulandı (`cost_currency` → `cost_currency_renamed` denemesi kırmızı çıktı, sonra geri alındı)
- [x] İki kapı da veritabanı **açmadan** koşar (`Tracon.Sql.Shared.UnitTests` deseni) — `ReadViewSqlText.ExtractMarkedBlock` gömülü kaynak metnini okur
- [x] Görünüm sütunları ile `ProtectedColumn` kesişimi boş — `ReadViewColumnSetTests` üç ayrı test, üç sağlayıcı
- [x] `reference/read-views.md` yayında; EF keyless entity örneği çalışıyor — sayfa `docs-site/src/content/docs/reference/read-views.md`, sidebar'a eklendi
- [x] `npm run build` + `check-links.mjs` temiz — `npm run check` (dördü birden) yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban d20b46f`, iki ayrı koşumda
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Gerçek Koşum Kanıtı" altbölümü
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/03-KALICILIK-POSTGRESQL.md` ve `04-KALICILIK-DIGER.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-PG-072..075, MT-SQL-077..078
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"

### Gerçek Koşum Kanıtı (2026-08-26, kapanış)

`samples/Tracon.Api`, gerçek bir PostgreSQL konteynerine (`ap-pg`) karşı
`EnableReadViews=true` ile çalıştırıldı. `support` agent'ına gerçek bir HTTP
isteği (`POST /tracon/api/agents/support/run`) gönderildi; `EchoModelProvider`
ağ çağrısı yapmadan yanıtladı (K1: API anahtarı gerekmedi). Açılış logu **38
migration** uyguladığını yazdı (37 çekirdek + 1 `views`). Koşu tamamlandıktan
sonra `runs_v1` doğrudan sorgulandı:

```
run_id        | 01a03e53-c473-7826-b74c-8ee2582a52b2
agent_name    | support
status        | 1
status_name   | Completed
input_tokens  | 283
output_tokens | 16
total_tokens  | 299
input_cost    | (NULL)
output_cost   | (NULL)
total_cost    | (NULL)
cost_currency | (NULL)
error_type    | (NULL)
```

`EchoModelProvider`'ın fiyatı tanımsız olduğu için `total_cost` **`NULL`**
döndü — `0` değil; case 3'ün üretimdeki kanıtı budur. `\d+
tracon_phase111_demo.runs_v1` çıktısı 111.2 sütun tablosuyla birebirdi
(20 sütun, sırası ve tipleri dahil). Demo şeması iş bitince
`DROP SCHEMA ... CASCADE` ile temizlendi; `ap-pg` konteyneri önceki durumuna
(durdurulmuş) geri döndürüldü.

### Doğrulama komutları

```bash
# Görünüm sütunları
psql "$TRACON_CONNECTION" -c "\d+ tracon.runs_v1"

# Toplam, store ile eşleşiyor mu
./artifacts/bin/Tracon.PostgreSql.IntegrationTests/release/Tracon.PostgreSql.IntegrationTests \
  --filter-method "*ReadViewContract*"

# Sözleşme dışı sütun sızmış mı
grep -n "state\|payload\|arguments\|result\|content" src/Tracon.PostgreSql/Migrations/*_read_views.sql
```

---

## Plandan Sapmalar

- **Dosya yolu planınkinden farklı.** Plan `src/Tracon.PostgreSql/Migrations/NNNN_read_views.sql` diyordu (çekirdek sette). Açık Soru 3'ün B seçeneği (isteğe bağlı set) kabul edilince bu, Faz 67'nin `MigrationsKnowledge/` deseniyle tutarlı olması için `MigrationsViews/0001_read_views.sql`'e taşındı — her sağlayıcı kendi `OptionalMigrationResourcePrefixes` sözlüğünde ayrı bir embedded-resource önekiyle kayıtlı.
- **SQL Server'da yeni bir tuzak keşfedildi ve dokümana yazıldı: `CREATE (OR ALTER) VIEW` tek başına bir toplu iş olmalıdır.** `MigrationRunner.ApplyOneAsync` migration metnini `__migrations` INSERT'iyle **tek** komut metninde gönderir (K-388); bu, `CREATE OR ALTER VIEW`'in T-SQL kısıtını doğrudan ihlal ederdi. Çözüm: view tanımı `EXEC(N'...')` ile sarmalandı — kendi toplu işini açar. Plan bunu öngörmüyordu; ilk yazılan SQL Server migration'ı gerçek konteynerde "'CREATE VIEW' must be the first statement in a query batch" hatasıyla düştü, EXEC sarmalamasıyla düzeltildi.
- **İki test-altyapısı kusuru bulundu ve düzeltildi** (fazın kapsamı dışında ama onun ürettiği ilk view tarafından ortaya çıkarıldı): `SqliteTestContext.DisposeAsync()` ve SQL Server `SqlServerTestContext.DropSchemaAsync()` yalnız TABLOLARI siliyordu; `runs_v1` bir VIEW olduğu için sarkan kalıyordu. SQLite'ta bu, sonraki bir testin `ALTER TABLE ... RENAME` migration'ını "no such table" ile düşürdü (SQLite'ın rename-fixup'ı dosyadaki HER görünümü yeniden çözer); SQL Server'da `DROP SCHEMA` "being referenced by object" ile başarısız oldu. İkisi de görünüm-önce-tablo sırasıyla düzeltildi. Vaka kaydı: `docs/hafiza/sqlite.md`.
- **Bağımsız denetimin 🟡 bulguları üzerine üç ek test dosyası eklendi** (planda yoktu, denetim sonrası): `ReadViewStatusNameTests.cs` (`status_name` eşlemesinin her `RunStatus` değerini kapsadığını build zamanında doğrular — `total_cost` kapısının eşdeğeri) ve üç sağlayıcıda birer `ReadViewOptionalSetTests.cs` (`EnableReadViews=false` iken görünümün gerçekten kurulmadığını doğrudan kanıtlar — "knowledge" setinin `Knowledge_disabled_by_default_creates_no_vector_objects` deseninin eşdeğeri).

## Bu Fazda Verilen Kararlar

- **K-626** — `runs_v1` okuma sözleşmesi görünümü üç sağlayıcıda `EnableReadViews` ile isteğe bağlı yayımlandı; yayımlanan sürüm sütun kaybetmez/yeniden adlandırmaz/daraltmaz, kırıcı değişiklik `runs_v2` olarak açılır; görünüm bir kiracı sınırı DEĞİLDİR. Tam gerekçe: `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, 2026-08-26): **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `status_name` eşlemesinin `total_cost`'un eşdeğeri bir otomatik kapısı yoktu — yeni bir `RunStatus` değeri eklenip görünüme yansıtılmazsa hiçbir test kırmızı olmazdı | **Düzeltildi** — `ReadViewStatusNameTests.cs` eklendi (üç sağlayıcı, `RunStatus` enum'unu reflection ile okur) |
| 2 | 🟡 | `ProtectedRawColumnNames` listesi (`ReadViewColumnSetTests.cs`) `ProtectedColumn` enum'unun XML doc'undaki ham sütun adlarının elle kopyasıdır, reflection'la bağlı değil | **Gerekçelendi** — `ProtectedColumn` bugün 10 kalemli, nadiren değişen bir enum ve XML doc metnini reflection'la ayrıştırmak (tam otomasyon için tek yol) bu ölçekte orantısız karmaşıklık katardı. Liste kodda yorumla işaretli; yeni bir `ProtectedColumn` değeri eklenirse bu listenin güncellenmesi gerektiği hatırlatılıyor |
| 3 | 🟡 | `EnableReadViews=false` iken görünümün hiç kurulmadığını doğrudan kanıtlayan test yoktu — yalnız dolaylı kanıt (~40 sınıfın varsayılanla yeşil kalması) vardı | **Düzeltildi** — üç sağlayıcıda birer `ReadViewOptionalSetTests.cs` eklendi, "knowledge" setinin deseninin birebir eşdeğeri |
| 4 | 🟡 | DoD satırı "`samples/Tracon.Api` ile gerçek `run` yapıldı" kanıtsızdı | **Düzeltildi** — gerçek koşum yapıldı, çıktı DoD bölümüne yazıldı (bkz. "Gerçek Koşum Kanıtı") |
| 5 | 🟢 | "Planlanan Public API" bölümü "yüzey büyümüyor" diyordu ama üç `EnableReadViews` bool eklendi | **Düzeltildi** — plan başlığı ve "Gerçekleşen Public API" bölümü güncellendi |
| 6 | 🟢 | Planlanan dosya yolu (`Migrations/NNNN_read_views.sql`) gerçekleşenden (`MigrationsViews/0001_read_views.sql`) farklıydı | **Not düşüldü** — "Plandan Sapmalar"a yazıldı |

Denetimden sonra dört kapı **yeniden koştu** (yeni test dosyaları + SQL marker
değişiklikleri nedeniyle) ve yeşil kaldı.

## Sonraki Faza Devir Notu

- **`runs_v1` artık kalıcı bir sözleşmedir (K-626).** Bir sonraki faz `runs`
  tablosuna sütun eklerken bunun `runs_v1`'e de eklenip eklenmeyeceğini (additive,
  serbest) düşünmeli; sütun **yeniden adlandırma/daraltma/kaldırma** ise `runs_v2`
  gerekir, `runs_v1` elle düzenlenmez.
- **Yeni bir `*_cost` sütunu eklenirse** `ReadViewCostTermTests` onu otomatik
  ister (build zamanında kırmızı olur) — `SqlQueriesBase.CostAddends`'e eklemek
  yeterli değildir, üç `0001_read_views.sql`'in `total_cost: BEGIN/END` bloğuna
  da elle eklenmesi gerekir.
- **Yeni bir `RunStatus` değeri eklenirse** aynı şekilde `ReadViewStatusNameTests`
  kırmızı olur; üç `0001_read_views.sql`'in `status_name: BEGIN/END` bloğundaki
  `CASE`'e yeni dal eklenmesi gerekir.
- **🚨 SQL Server'da `CREATE VIEW`/`PROCEDURE`/`FUNCTION`/`TRIGGER` içeren bir
  migration yazarsan `EXEC(N'...')` ile sarmala.** `MigrationRunner` migration
  metnini `__migrations` INSERT'iyle tek komutta gönderir; bu dört DDL türü
  T-SQL'de "toplu işin tek deyimi olmalı" kısıtı taşır. Vaka: bu faz, ilk
  yazılan `CREATE OR ALTER VIEW`'de gerçek konteynerde patladı.
- **🚨 SQLite/SQL Server test context'lerinde VIEW temizliği artık TABLO
  temizliğinden ÖNCE gelir** (`SqliteTestContext.DisposeAsync`,
  `SqlServerTestContext.DropSchemaAsync`). Yeni bir test-context temizlik
  yordamı yazarsan bu sırayı koru — aksi hâlde sarkan bir view sonraki testi
  düşürür (vaka kaydı: `docs/hafiza/sqlite.md`).
- **İkinci bir görünüm (`runs_v2` dışında, örn. `run_events` için) açılmadı**
  (Açık Soru 4/A, YAGNI) — talep gelirse `ProtectedColumn` kesişim kapısı ilk
  kez gerçek bir korunan sütunla karşılaşacak, bugünkü boş-kesişim testi
  yalnız `runs_v1` için anlamlıydı.
