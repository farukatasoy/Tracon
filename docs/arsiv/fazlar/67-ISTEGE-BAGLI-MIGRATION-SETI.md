# Faz 67 — İsteğe Bağlı Migration Seti (`pgvector` opt-in)

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-110**
> **Önkoşul:** [Faz 51](51-VEKTOR-BELLEK-VE-RAG.md) — `0024_vector.sql` ve `AgentPrismKnowledgeOptions` oradan gelir
> **Paketler:** `AgentPrism.Sql.Shared`, `AgentPrism.PostgreSql`
> **Yeni paket:** Yok · **Migration:** **yeni migration yok** — var olan bir dosya ayrı bir sete taşınır. 🚨 Tuzak bu fazın tamamıdır, aşağıya bak
> **Public API:** büyüyor (küçük) — `MigrationRunner` ve `SqlStoreContext` set kavramı öğrenir. `PublicAPI.Shipped.txt` bugün **boş** (ölçüldü: 16 pakette toplam 16 satır, her biri yalnız `#nullable enable`) — şimdi bedava
> **Site etkisi:** `getting-started/persistence.md`, `guides/knowledge.md`, `guides/production.md`, `packages.md`, `troubleshooting.md`
> **Manuel test alanı:** [`docs/manuel-test/03-KALICILIK-POSTGRESQL.md`](../../manuel-test/03-KALICILIK-POSTGRESQL.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/67-ISTEGE-BAGLI-MIGRATION-SETI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün `UsePostgreSql()` çağıran herkes `pgvector` kurmak zorundadır — Knowledge hiç kullanılmasa bile. Bu faz migration setini ikiye ayırır: **çekirdek** her zaman koşar, **knowledge** yalnız istendiğinde. Kazanan, uzantı kurma izni olmayan yönetilen bir PostgreSQL üzerinde çalışan tüketicidir.

## Bitiş Ölçütleri (DoD)

- [x] `pgvector` **olmayan** bir PostgreSQL üzerinde `UsePostgreSql()` ile
      uygulama açılır ve `/api/diagnostics` `Healthy` döner
      — gerçek `postgres:18-alpine` konteynerine karşı koşuldu, bkz. MT-PG-062
- [x] `EnableKnowledge = true` ve uzantı yokken **okunur** bir başlangıç hatası
      verilir (mesaj belgeye yazılır)
      — gerçek hata metni MT-PG-063'te; ayrıca `RestrictedEnvironmentMigrationTests`
      (otomatik, gerçek konteyner)
- [x] `\dx` çıktısı knowledge kapalıyken `vector` içermez — MT-PG-062'de doğrulandı
- [x] SQL Server ve SQLite sözleşme koşumları değişmeden geçer
      — 540/540 ve 554/554, gerçek konteynerlere karşı
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
      — MT-PG-064: gerçek belge yükleme + OpenAI gömü + `pgvector` arama turu
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri
      [`docs/manuel-test/03-KALICILIK-POSTGRESQL.md`](../../manuel-test/03-KALICILIK-POSTGRESQL.md)
      içine eklendi; otomatikleştirilebilenler koşuldu
      — MT-PG-062..066, dördü gerçek konteynerlere karşı koşulup kanıtı yazıldı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
      — beş 🟡 bulgu kapandı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi (beş sayfa); `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Uzantı gerçekten yaratılmamış mı
psql "$CONN" -c '\dx' | grep -c vector      # beklenen: 0

# Bekleyen migration listesi kapalı setin dosyalarını içermemeli
curl -s http://localhost:5081/agentprism/api/diagnostics | jq '.pendingMigrations'
```

> 🚨 Plan taslağı `.persistence` alt-nesnesi varsayıyordu; gerçekleşen
> `/api/diagnostics` gövdesi DÜZDÜR (`pendingMigrations` kök seviyede) —
> yukarıdaki komut gerçekleşen şekle göre düzeltildi.

---

## Plandan Sapmalar

**Açık Soru 2'nin cevabı A'dan (numaralı çekirdek migration dosyası) sapıldı.**
Uygulama anında ölçüldü: bu değişiklik `__migrations`'ın KENDİ şemasını
değiştiriyor ve `MigrationRunner.ApplyOneAsync` her migration'ın SQL'ini
`InsertMigration`'ın SABİT metniyle (artık `set_name`'e referans veren) TEK bir
toplu komutta birleştiriyor (K-388). SQL Server bir toplu işi baştan sona
derler; `ALTER TABLE ... ADD set_name` sonrası aynı iste `InsertMigration`
"Invalid column name" ile patlar — `0018_audit_chain.sql`'in zaten belgelediği
tuzak, ama bu kez `InsertMigration` HER migration'a otomatik eklendiği için
`EXEC` ile sarılamaz. Çözüm: yükseltme `CreateMigrationsTable` gibi migration
döngüsü BAŞLAMADAN ÖNCE kendi ayrı komutuyla çalışan
`SqlDialect.UpgradeMigrationsTableAsync` oldu — numaralı bir dosya değil. Karar
K-475.

**HTTP `endpoint`'lerine yeni alan eklenmedi (plan "alan adı kapanışta
belirlenir" diyordu).** `SqlPersistenceDiagnosticsSnapshot.PendingMigrations`
mevcut `IReadOnlyList<string>` şeklini korudu; çekirdek-dışı bir setin bekleyen
adı `"{set}:{ad}"` önekiyle taşınır. Karar K-477.

**`RestrictedRoleMigrationTests` planın önerdiği isimle DEĞİL, gerçek bir
`postgres:18-alpine` konteynerine karşı koşan `RestrictedEnvironmentMigrationTests`
adıyla yazıldı** (`tests/AgentPrism.PostgreSql.IntegrationTests/`). Gerekçe:
PostgreSQL'de `CREATE SCHEMA IF NOT EXISTS` bile, şema ÖNCEDEN var olsa dahi,
veritabanı düzeyinde `CREATE` izni ister (ölçüldü: `docker exec` ile
doğrulandı, "permission denied for database") — yani gerçek bir "şema-sahibi
ama veritabanı-CREATE'i yok" rolü simüle etmek `MigrationRunner`'ın BAŞLANGIÇ
adımını (`CreateSchema`) hiç geçemiyor, bu Faz 67'ye özgü değil, tüm kalıcılık
katmanının Faz 2'den beri var olan bir sınırı. Gerçekçi ve fazın asıl
motivasyonuyla (uzantı kurulu olmayan bir sunucu) birebir örtüşen senaryo,
adı geçen testin yaptığı budur: `pgvector`'ın hiç KURULU OLMADIĞI düz bir
sunucu, rol izni değil.

## Bu Fazda Verilen Kararlar

- **K-475** — Migration ledger'ın `set_name`/`(set_name, id)` şema yükseltmesi
  numaralı bir migration dosyası değil, `SqlDialect.UpgradeMigrationsTableAsync`
  bootstrap adımıdır.
- **K-476** — `IVectorSearchStore`, `EnableKnowledge = false` iken kayıtsız
  bırakılmaz; fabrikası `null` döner.
- **K-477** — `SqlPersistenceDiagnosticsSnapshot.PendingMigrations`'a yeni alan
  eklenmedi; çekirdek-dışı bir setin bekleyen adı `"{set}:{ad}"` önekiyle
  yazılır.

## Denetim Bulguları

Bağımsız denetçi (taze bağlamlı `general-purpose` agent, çalışma ağacına karşı)
🔴 bulgu bulmadı. Beş 🟡 bulundu, hepsi bu fazda kapatıldı:

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | Üç `*Queries.cs` yorumunda var olmayan bir migration dosyasından ("0034_migration_sets.sql"/"0021_migration_sets.sql") bahsediliyordu — K-475'in "numaralı dosya DEĞİL" kararıyla çelişiyordu | **Düzeltildi** — üç yorum da `UpgradeMigrationsTableAsync`'e doğru referans verecek şekilde güncellendi |
| 2 | Planın `RestrictedRoleMigrationTests`'i yazılmamıştı | **Düzeltildi** — `RestrictedEnvironmentMigrationTests` eklendi (gerçek `postgres:18-alpine` konteynerine karşı, bkz. Plandan Sapmalar) |
| 3 | `LogOrphanedRelocatedMigrations`/`RelocatedCoreMigrationSets` hiçbir testte tetiklenmiyordu | **Düzeltildi** — `OptionalMigrationSetTests.Orphaned_relocated_core_row_is_logged_and_harmless` eklendi (özel `ILogger` yakalayıcısıyla) |
| 4 | `IVectorSearchStore`'un `EnableKnowledge`'a göre null/gerçek çözümlenmesi DI konteynerinden geçen bir testte kanıtlanmamıştı | **Düzeltildi** — `ServiceRegistrationTests`'e iki test eklendi |
| 5 | `GetSnapshotAsync`'in kapalı bir setin dosyalarını "pending" saymadığını kanıtlayan otomatik test yoktu | **Düzeltildi** — `OptionalMigrationSetTests.GetSnapshotAsync_does_not_count_a_disabled_sets_files_as_pending` eklendi |

Düzeltmeler sonrası dört doğrulama kapısı ve tüm PostgreSQL/SQL Server/SQLite
entegrasyon test paketleri (1082/1082, 540/540, 554/554) yeniden koşuldu ve
yeşil.

## Sonraki Faza Devir Notu

- Faz 51'in `document_embeddings` şema kararları (K-341/K-346) değişmedi;
  yalnız migration'ın HANGİ SETTE olduğu değişti. Faz 51'in dokümanına dokunan
  bir sonraki faz `0024_vector.sql`'i değil `MigrationsKnowledge/0001_vector.sql`'i
  arasın.
- `docs/manuel-test/00-INDEKS.md`/`03-KALICILIK-POSTGRESQL.md`'deki migration
  SAYILARI (`28`/`29`) bu fazdan ÖNCE de zaten güncel değildi (birçok faz
  boyunca güncellenmemiş) — bu faz yalnız KENDİ değiştirdiği case'leri (MT-PG-020..024)
  doğru sayılara taşıdı. `00-INDEKS.md`'nin geri kalanında ("Bu fazın kendi
  verisi" gibi başka bölümlerde) benzer sayısal drift olabilir; genel bir tarama
  bu fazın kapsamında DEĞİLDİ.
- `AgentPrismPostgreSqlOptionsValidator`'a `EnableKnowledge` için bir doğrulama
  eklenmedi (bool, doğrulanacak bir kısıt yok) — bir sonraki faz
  `Dimensions`/`EnableKnowledge` arasında çapraz bir kısıt isterse (örn.
  "knowledge açıkken Dimensions > 0 zorunlu" — bugün zaten `AgentPrismKnowledgeOptions`
  tarafında ayrı doğrulanıyor) burası genişler.

> Kapanışta doldurulur.
