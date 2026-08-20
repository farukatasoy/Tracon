# Faz 24 — SQLite Desteği

> **Durum:** ✅ Kod tamam · 205/205 sözleşme+diyalekt testi yeşil · AOT ölçülmedi (bkz. "Açık Kalan")
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-07**
> **Önkoşul:** [Faz 23](23-SQL-SERVER.md) — ortak SQL soyutlaması orada olgunlaşır
> **Paketler:** **`AgentPrism.Sqlite` (YENİ)** · `AgentPrism.Abstractions`
> **Migration:** Kendi migration seti — `0001_initial.sql`
> **Kararlar:** K-190 … K-197

---

## Bu Faza Başlarken

1. [`23-SQL-SERVER.md`](23-SQL-SERVER.md) — paylaşım modeli ve çeviri deseni
2. [`02-POSTGRESQL-KALICILIK.md`](02-POSTGRESQL-KALICILIK.md) — şablon
3. [`KARARLAR.md`](../../KARARLAR.md) — **K-018** (bellek içi depolar birinci sınıf), **K-015** (uuid v7)
4. Bu doküman

---

## Amaç

Tek dosyalık kurulum. Değeri üç yerdedir:

- **Demo ve deneme** — `dotnet run` ile çalışan, veri kaybetmeyen bir kurulum
- **Gömülü senaryolar** — masaüstü veya kenar (edge) uygulamaları
- **Test** — bellek içi depoların ötesinde, gerçek SQL davranışı

Bellek içi depolar (K-018) zaten birinci sınıf. SQLite'ın eklediği tek şey
**kalıcılıktır**; bu yüzden bu faz küçüktür ve sıranın sonlarındadır.

---

## 24.1 — SQLite'ın Gerçek Sınırları

Bunlar gizlenmez; README'de ve `/api/meta` çıktısında bildirilir.

| Sınır | Sonuç |
|-------|-------|
| **Tek yazıcı** | Eşzamanlı yazma serileşir. Yüksek çalıştırma hacminde `run_events` yazımı darboğaz olur |
| WAL şart | `journal_mode=WAL` olmadan okuma/yazma birbirini bloklar. Bağlantı açılışında **zorunlu** olarak ayarlanır |
| `busy_timeout` | Varsayılan 0 — anında `SQLITE_BUSY`. 5000 ms olarak ayarlanır |
| Tip sistemi zayıf | `datetimeoffset` yok, `uuid` yok, `decimal` yok — hepsi metin/sayı olarak kodlanır |
| `ALTER TABLE` sınırlı | Sütun düşürme ve tip değişimi tablo yeniden kurmayı gerektirir; migration yazarken dikkat |
| Ağ yok | Tek süreçlidir; çok örnekli dağıtımda **kullanılamaz** |
| `SKIP LOCKED` yok | Faz 17 iş kuyruğu tek işçiyle çalışır; `RunWorker` çok örnekli olamaz |

> Bu sınırlar SQLite'ı kötü yapmaz; **yanlış yerde kullanmak** kötü yapar.
> AgentPrism'in görevi sınırı açıkça söylemektir.

---

## 24.2 — Tip Eşlemesi

| Kavram | SQLite | Gerekçe |
|--------|--------|---------|
| `uuid` v7 | `TEXT` (**büyük harfli**, tireli) | Sözlüksel sıralama = zaman sıralaması, harf büyüklüğünden bağımsız. Küçük harfe **kasıtlı olarak çevrilmez**: `Microsoft.Data.Sqlite`'ın varsayılanı büyük harftir ve zorunlu (`DbHelpers.Add`) ile nullable (`Dialect.AddUuid`) yollar aynı harf büyüklüğünü kullanmazsa `WHERE`/`JOIN` eşitliği sessizce kırılır (K-191). `BLOB` daha küçüktür ama okunabilirlik ve sıralama TEXT'te doğrudan çalışır |
| `timestamptz` | `TEXT` ISO 8601 UTC (`yyyy-MM-ddTHH:mm:ss.fffffffZ`) | Sözlüksel sıralama = kronolojik sıralama |
| `jsonb` / `json` | `TEXT` | JSON1 uzantısı sorgu için kullanılabilir; sıra korunur, K-027 sorunu **yok** |
| `bytea` | `BLOB` | Faz 14 ekleri |
| `numeric` | `TEXT` | 🚨 `REAL` **kullanılmaz** — para hesabında kayan nokta yasak. `decimal` metin olarak yazılır ve okunurken ayrıştırılır |
| `smallint` / `bigint` | `INTEGER` | |
| `text[]` | JSON dizi metni | |

**Zaman ve kimlik biçimleri sabittir ve testlidir.** Biçim değişirse sıralama
sessizce bozulur; bu, fark edilmesi en zor hata sınıfıdır.

---

## 24.3 — Migration Kilidi

`pg_advisory_lock` veya `sp_getapplock` karşılığı yoktur. İlk plan `BEGIN
IMMEDIATE ... COMMIT` idi ama **ölçüldü ve uygulanamadı**: `Microsoft.Data.Sqlite`
iç içe işlem desteklemez (`SqliteConnection does not support nested
transactions`). `MigrationRunner.ApplyOneAsync` her migration için kendi
işlemini açtığından, `AcquireMigrationLockAsync` içinde `BEGIN IMMEDIATE`
açıp tüm migration boyunca tutmak bu iç işlemle çakışırdı. Ayrı bir "kilit
bağlantısı" üzerinden `BEGIN IMMEDIATE` tutmak da denenmedi: SQLite dosya
düzeyinde tek yazıcı olduğu için birincil bağlantının kendi yazmalarını
kendi kendine kilitler (self-deadlock).

**Gerçekleştirilen çözüm — sidecar dosya kilidi** (K-192):

```csharp
new FileStream(dbPath + ".agentprism-migration-lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
```

Bağlantının işlem durumuna hiç dokunmaz; ikinci bir süreç aynı dosyayı
açamaz ve `commandTimeout` saniye boyunca yoklayarak bekler. `:memory:`
veritabanlarında atlanır (başka bir süreç aynı bağlantıyı paylaşamaz).
`SqliteDialect.AcquireMigrationLockAsync`/`ReleaseMigrationLockAsync`.

---

## 24.4 — Paket ve Kayıt

```csharp
builder.AddAgentPrism()
       .UseSqlite("Data Source=agentprism.db");

// Bellekte, test icin:
builder.AddAgentPrism()
       .UseSqlite("Data Source=:memory:");   // SINIR: baglanti kapanirsa veri gider
```

- Şema kavramı yoktur; tablo adları `agentprism_` **öneki** alır (`AgentPrismSqliteOptions.TablePrefix`).
  K-013'ün ("tüketicinin şemasına dokunma") SQLite karşılığı budur (K-190).
  Önek yapılandırılabilir ve aynı katı doğrulamadan geçer (K-029)
- 🚨 **İndeks adları da önek alır** — SQLite'ta indeks adları PostgreSQL/SQL
  Server'ın aksine VERİTABANI GENELİNDE tektir, sema/tabloya göre kapsamlı
  değildir. Yalnızca tablo adlarını önekleyip indeksleri onceksiz bırakmak,
  aynı `.db` dosyasını paylaşan farklı `TablePrefix` değerleri arasında
  `CREATE INDEX IF NOT EXISTS` çakışmasına ve sessiz indeks kaybına yol açar
  (K-193 — sözleşme testinde yakalandı)
- `UseSqlite` `Replace` kullanır (K-025)
- Yabancı anahtar zorlaması varsayılan KAPALIDIR; her bağlantıda
  `PRAGMA foreign_keys = ON` açıkça çalıştırılır (`SqliteDataSource`)
- Bağlantı havuzu: `Microsoft.Data.Sqlite` havuzu destekler; `Pooling=True`
  varsayılan bırakılır ve WAL ile birlikte ölçülür
- AOT: **ölçülmedi** (K-196). `AgentPrismAotCompatible=false` güvenli tarafta
  bırakıldı; SQLitePCLRaw yerel kütüphane taşır ve bu, yayınlama (publish)
  davranışını etkileyebilir
- `SQLitePCLRaw.*` paketleri NU1903 (GHSA-2m69-gcr7-jv3q) nedeniyle 2.1.12'ye
  sabitlendi — K-007/K-181 ile aynı desen (K-197)

---

## 24.5 — Testler

`tests/AgentPrism.Sqlite.IntegrationTests` — container **gerekmez**, dosya
tabanlı geçici veritabanı yeter. Bu, CI süresini kısaltır.

| Test | Neden | Durum |
|------|-------|-------|
| Tüm store sözleşmeleri | Dördüncü uygulama; soyutlamanın asıl sınavı | ✅ 16 sözleşme, 205 test yeşil |
| WAL ve `busy_timeout` ayarlanıyor mu | Bağlantı açılış davranışı | ✅ `SqliteDialectTests` |
| Migration sidecar dosya kilidi | Beş eşzamanlı çalıştırıcı, tek kez uygulama | ✅ `MigrationRunnerTests` (K-192, `BEGIN IMMEDIATE` planı ölçülüp terk edildi) |
| Kimlik ve zaman sıralaması | uuid BÜYÜK harfli yazılır; zaman damgaları sözlüksel sırada mı | ✅ `SqliteDialectTests` (K-191) |
| `decimal` gidiş-dönüş | Faz 20 maliyeti kayıpsız mı | ✅ `SqliteDialectTests.Maliyet_ondaligi_kesilmeden_gidip_gelir` |
| Yabancı anahtar zorlaması etkin mi | Varsayılan kapalıdır, açıkça açılmalı | ✅ `SqliteDialectTests.Yabanci_anahtar_zorlamasi_etkindir` |
| Zorunlu/nullable Guid yazma yolu tutarlılığı | K-191'in regresyon testi | ✅ `SqliteDialectTests.Nullable_ve_zorunlu_guid_yazma_yollari_tutarlidir` |
| Uçtan uca kalıcılık | Örnek uygulama, gerçek dosya, süreç yeniden başlatma | ✅ elle doğrulandı — bkz. "Uçtan Uca Doğrulama" |
| Eşzamanlı yazma (yük altında) | `SQLITE_BUSY` yerine bekleme | ❌ yazılmadı — tek süreçli sözleşme testleri bu senaryoyu kapsamıyor |
| Dosya taşınabilirliği | Yazılan dosya başka bir süreçte açılıp okunabiliyor mu | ✅ dolaylı olarak kanıtlandı (uçtan uca doğrulamada aynı dosya iki ayrı süreçte açıldı) |

**Sözleşme testlerine yeni test eklemek gerekmemelidir.** Gerçekten de
gerekmedi — bu fazın sınavıydı ve geçildi (Faz 23'ün aynı ölçütü). Bulunan
üç gerçek hata (K-193, K-195, "const string" enterpolasyon hatası) sözleşme
testleri veya derleme aşamasında yakalandı.

### Uçtan Uca Doğrulama

`samples/AgentPrism.Api`, `UseSqlite()` ile üçüncü bir seçenek olarak
bağlandı (`AgentPrism__Sqlite__ConnectionString`). Gerçek bir dosyaya karşı
elle doğrulandı:

1. İlk açılış: `AgentPrism 1 migration uyguladi` logu, `/api/meta` →
   `"persistent": true`, `"agentDefinitionStore": "SqlAgentDefinitionStore"`.
2. `default` kiracısı gerçek bir uuid v7 ile oluşturuldu.
3. Süreç durduruldu, yeniden başlatıldı: migration logu **basılmadı** (ikinci
   koşu hiçbir şey uygulamadı — `MigrationRunnerTests.Ikinci_kosu_hicbir_sey_uygulamaz`
   ile aynı davranış, gerçek uygulamada da doğrulandı).
4. `default` kiracısı **aynı kimlikle** geri okundu — veri dosyada kalıcı.

---

## Bu Fazda Verilecek Kararlar

1. **`uuid` ve zaman `TEXT` olarak saklanır** — sözlüksel sıralama garantisi.
2. **`numeric` `TEXT` olarak saklanır** — `REAL` kullanılmaz.
3. **Şema yerine tablo öneki** — K-013'ün SQLite karşılığı.
4. **Sınırlar `/api/meta` ve README'de bildirilir** — K-018 deseni.
5. **AOT durumu ölçümle belirlenir.**

---

## Açık Sorular

1. **`:memory:` desteklensin mi?** Test için kullanışlı, üretimde yanıltıcı.
   Karar: **evet** — desteklenir ve tüm sözleşme testleri `:memory:` yerine
   dosya tabanlı geçici DB kullanır (dosya taşınabilirliğini de kanıtlamak
   için). `/api/meta`'nın bunu ayrıca "kalıcı değil" olarak bildirmesi
   **yapılmadı** — `Persistent` alanı yalnızca depo TİPİNE bakar
   (`Sql*Store` mi, `InMemory*Store` mi), bağlantı dizesinin `:memory:` olup
   olmadığını ayırt etmez. Küçük ama gerçek bir eksik; bir sonraki dokunulan
   oturumda `/api/meta`'ya kolayca eklenebilir.
2. **Faz 17 işçisi SQLite'ta çalışsın mı?** Tek yazıcı sınırı var ama tek
   örnekli kurulumda sorun değil. Karar: **evet** (kod değişikliği
   gerekmedi — `SqlJobStore` paylaşılan katmandır); çok örnekli kullanım
   README'de yasaklanır.
3. **Otomatik `VACUUM` / bakım?** Karar: **hayır** — Faz 25'in saklama işi
   silme yaptıktan sonra isteğe bağlı `VACUUM` çalıştırabilir. Değişmedi.

---

## Bitiş Ölçütleri (DoD)

- [x] `AgentPrism.Sqlite` paketi üretiliyor (`dotnet pack` sayısı arttı)
- [x] Tüm store sözleşme testleri SQLite üzerinde yeşil (205/205)
- [x] WAL ve `busy_timeout` bağlantı açılışında ayarlanıyor (test)
- [x] Örnek uygulama tek dosyalık veritabanıyla uçtan uca çalışıyor (elle doğrulandı, yukarıda)
- [x] Uygulama kapatılıp açıldığında veri duruyor (gerçek çıktı — aynı tenant kimliği)
- [ ] Sınırlar README ve `/api/meta`'da bildiriliyor — **README tamam**, `/api/meta`'da `:memory:` ayrımı eksik (Açık Soru 1)
- [ ] AOT durumu ölçüldü ve `MIMARI.md` güncellendi — **ölçülmedi**, K-196
- [x] Dört doğrulama kapısı sıfır uyarı (build, format, pack, `AgentPrism.Core.UnitTests`; PostgreSQL 416/416 ve SQL Server 204/204 regresyonsuz)

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kullanıcı SQLite'ı üretimde çok örnekli kullanır | Sınır README'de bildirilir; `/api/meta`'da AYRI bir uyarı yok (izlenen risk) |
| Sıralama biçimi bozulur | Biçim sabit ve testli (K-191) |
| İndeks adı çakışması (farklı `TablePrefix`, aynı dosya) | Tüm indeks adları önek taşır (K-193, testte yakalandı) |
| Yerel kütüphane yayınlama sorunları | AOT/publish ölçümü YAPILMADI — açık risk (K-196) |
| `SQLITE_BUSY` hataları | WAL + `busy_timeout`; yük altında eşzamanlılık testi YAZILMADI — açık risk |

---

## Açık Kalan

Faz 23'ün "Açık Kalan" bölümüyle aynı disiplinle: gizlenmez, açıkça yazılır.

1. **AOT/publish ölçümü yapılmadı** (K-196). SQLitePCLRaw'ın kırpma/native
   AOT altında davranışı bilinmiyor.
2. **Yük altında eşzamanlılık testi yok.** Sözleşme testleri tek süreçli
   çalışır; `SQLITE_BUSY`/`busy_timeout` gerçek çekişme altında ölçülmedi.
3. **`/api/meta` `:memory:` ayrımı yapmıyor** (Açık Soru 1).
4. **Gerçek `mssql/server` hâlâ doğrulanmadı** (Faz 23'ten miras, K-186)
   — bu fazı engellemedi ama SQL Server tarafının nihai kanıtı hâlâ açık.

---

## Sonraki Faza Devir Notu

- Faz 25 (saklama) artık **dört** sağlayıcı için temizleme yazmak zorundadır.
  `IRetentionStore` sözleşmesi sağlayıcıdan bağımsız kalmalıdır.
- Dört uygulamalı sözleşme testleri, sonraki her depo değişikliğinde dört kez
  koşacaktır; CI süresi ölçülmeli ve gerekirse SQLite testleri hızlı katmana
  alınmalıdır (zaten en hızlısı — container gerekmiyor).
- **`ExecuteScalarAsync` sonucunu `(Guid)`/`is bool` ile cast etme** — SQLite
  bunları `string`/`long` döndürür. Yeni bir depo yazan herkes
  `DbHelpers.ToGuid`/`ToBoolean` kullanmalıdır (K-195).
- **Yeni bir tablo/indeks eklerken indeks adını da önekle.** SQLite'ın düz ad
  alanı unutulursa aynı hata (K-193) sessizce tekrarlanır.
