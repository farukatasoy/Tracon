# Faz 23 — SQL Server Desteği

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-06**
> **Önkoşul:** Yok — ama şema oturduktan sonra yapılması **çok daha ucuzdur**
> **Sonraki bağımlı:** [Faz 24](24-SQLITE.md) — SQLite bu fazın soyutlamasını kullanır
> **Paketler:** **`AgentPrism.SqlServer` (YENİ)** · `AgentPrism.Abstractions`
> **Migration:** Kendi migration seti (`0001`'den başlar)

---

## Bu Faza Başlarken

1. [`02-POSTGRESQL-KALICILIK.md`](02-POSTGRESQL-KALICILIK.md) — **şablon budur**
2. [`KARARLAR.md`](KARARLAR.md) — **K-004** (elle SQL + gömülü migration), **K-013** (ayrı şema), **K-015** (uuid v7), **K-025** (`Replace`), **K-027** (`json` vs `jsonb`), **K-029** (şema adı gömülür)
3. `tests/AgentPrism.PostgreSql.IntegrationTests/Contracts/` — **sözleşme testleri**
4. Bu doküman

---

## Amaç

Kurumsal .NET dünyasının en yaygın veritabanı SQL Server'dır. `AgentPrism.PostgreSql`
iyi bir şablondur ama **SQL birebir taşınmaz**.

Bu faz aynı zamanda bir **tasarım sınavıdır**: Faz 2'de kurulan sözleşme testleri
(`Contracts/`) tam olarak bunun için yazılmıştı. Üçüncü bir uygulama eklemek
`PostgresStoreContractTests` kadar kolay olmalıdır. Zor oluyorsa soyutlama
yanlıştır ve bu, düzeltilmesi gereken bir bulgudur.

---

## 23.1 — SQL Çeviri Tablosu

| PostgreSQL | SQL Server | Not |
|------------|-----------|-----|
| `uuid` | `uniqueidentifier` | v7 sıralaması **korunur** ama SQL Server'ın `uniqueidentifier` sıralaması bayt sırasına göredir; kümelenmiş indeks kullanılacaksa dikkat |
| `jsonb` | `nvarchar(max)` + `CHECK (ISJSON(col) = 1)` | Sorgulanan yollarda hesaplanmış sütun + indeks |
| `json` | `nvarchar(max)` | K-027'nin `jsonb` sorunu SQL Server'da **yoktur** — sıra korunur |
| `text` | `nvarchar(max)` | |
| `timestamptz` | `datetimeoffset(7)` | UTC olarak yazılır |
| `smallint` | `smallint` | |
| `bigint` | `bigint` | |
| `bytea` | `varbinary(max)` | Faz 14 |
| `numeric(20,10)` | `decimal(20,10)` | Faz 20 |
| `text[]` | Ayrı tablo veya JSON dizi | Faz 21'in `events` sütunu |
| `ON CONFLICT DO UPDATE` | `MERGE` **veya** `UPDATE` + `IF @@ROWCOUNT = 0 INSERT` | Aşağıdaki uyarıya bakın |
| `pg_advisory_lock` | `sp_getapplock` | Migration kilidi |
| Kısmi indeks (`WHERE`) | Filtrelenmiş indeks (`WHERE`) | **Var** — birebir karşılık |
| İfade üzerinde `UNIQUE INDEX` | Hesaplanmış sütun (`PERSISTED`) + `UNIQUE` | `COALESCE`'li benzersizlikler için |
| `now()` | `SYSUTCDATETIME()` | |
| `generate_series` | Sayı tablosu veya `WITH` özyineleme | Faz 20 zaman serisi |
| `FOR UPDATE SKIP LOCKED` | `WITH (UPDLOCK, READPAST, ROWLOCK)` | Faz 17 iş kuyruğu |

### 🚨 `MERGE` uyarısı

SQL Server'ın `MERGE` ifadesinin bilinen eşzamanlılık ve doğruluk sorunları
vardır. Güvenli desen:

```sql
UPDATE ... WITH (UPDLOCK, SERIALIZABLE) WHERE key = @key;
IF @@ROWCOUNT = 0
    INSERT ...;
```

veya benzersiz indeks ihlalini yakalayan bir yeniden deneme. Karar uygulama
oturumunda verilir ve **gerekçesiyle** yazılır.

### K-027 burada geçerli değildir

`sessions.state` ve `conversation_items.item` sütunlarının neden `json` (jsonb
değil) olduğunu anlatan karar SQL Server'da uygulanamaz: `nvarchar(max)` zaten
anahtar sırasını korur. Sorun kendiliğinden yoktur. **Bu, kararın yanlış olduğu
anlamına gelmez** — PostgreSQL'de hâlâ geçerlidir.

---

## 23.2 — Ortak Soyutlama Nereye Konur?

Bu fazın ikinci sınavı: `AgentPrism.PostgreSql` ile `AgentPrism.SqlServer`
arasında ne kadar kod paylaşılır?

| Yol | Değerlendirme |
|-----|---------------|
| Ortak bir `AgentPrism.Sql` paketi | Üçüncü bir paket; tüketici bunu asla doğrudan kullanmaz. Ek yayın yükü |
| Paylaşılan **kaynak** dosyalar (`Compile Include="../..."`) | Paket sayısı artmaz; kod tekrarı olmaz. Derleme yapılandırması biraz karmaşıklaşır |
| Kod tekrarı (kopyala) | Basit; ama her düzeltme iki yerde yapılır ve biri unutulur |

**Öneri: paylaşılan kaynak dosyalar.** Paylaşılacaklar: migration çalıştırıcı
iskeleti, checksum hesabı, `SqlIdentifier` doğrulaması, tanım yükü tipleri
(`AgentDefinitionPayload`), JSON bağlamı. Paylaşılmayacaklar: SQL metinleri,
bağlantı/komut yardımcıları, depo uygulamaları.

Karar uygulama oturumunda kesinleşir ve karar defterine yazılır.

---

## 23.3 — Migration Seti

SQL Server'ın **kendi** gömülü `.sql` dosyaları olur ve numaralandırma `0001`'den
başlar. PostgreSQL'in migration numaralarıyla eşleşmesi **gerekmez** —
eşleştirmeye çalışmak, ileride bir sağlayıcıya özel düzeltme gerektiğinde
kilitlenme üretir.

`__migrations` tablosu aynı sözleşmeye sahiptir: ad, checksum, uygulanma zamanı.
Checksum hesabı satır sonu normalleştirmesi dâhil **birebir aynı** kodu kullanır
(paylaşılan kaynak).

Kilit: `sp_getapplock @Resource = 'AgentPrism.Migrations', @LockMode = 'Exclusive',
@LockOwner = 'Session', @LockTimeout = 30000`.

---

## 23.4 — Paket ve Kayıt

```csharp
builder.AddAgentPrism()
       .UseSqlServer(connectionString, o =>
       {
           o.SchemaName = "agentprism";
           o.CommandTimeoutSeconds = 30;
       });
```

- `UseSqlServer` **`Replace`** kullanır, `TryAdd` değil — K-025'in gerekçesi
  birebir geçerlidir
- `AgentPrism.SqlServer` ve `AgentPrism.PostgreSql` **aynı anda** kaydedilirse
  son kayıt kazanır; bu bir yapılandırma hatasıdır ve açılışta **uyarı** loglanır
- Şema adı doğrulaması: SQL Server tanımlayıcı kuralları farklıdır ama AgentPrism
  aynı katı kuralı uygular (küçük harf, rakam, alt çizgi) — iki sağlayıcı
  arasında taşınabilirlik korunur

### AOT

`Microsoft.Data.SqlClient` (7.0.2) AOT uyumluluğu **ölçülmelidir**.
`AgentPrism.PostgreSql` AOT uyumludur (Npgsql uyumlu). SqlClient uyumlu değilse
`AgentPrismAotCompatible = false` verilir ve bu, `MIMARI.md` bölüm 9'daki
tabloya yazılır. **Uyumsuzluk gizlenmez.**

---

## 23.5 — Testler

`tests/AgentPrism.SqlServer.IntegrationTests` — Testcontainers ile.

```csharp
// Testcontainers.MsSql paketi gerekir (Directory.Packages.props'a eklenir)
new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
```

> Faz 2 dersi: `PostgreSqlBuilder()` parametresiz kurucusu kullanımdan kalktı ve
> `CS0618` verdi. Testcontainers sürümünde aynı desene dikkat edin.

**Sözleşme testleri yeniden kullanılır.** `Contracts/` altındaki soyut sınıflar
bugün `InMemory*` ve `Postgres*` üzerinde koşuyor; bu faz `SqlServer*`
uygulamalarını ekler. Yeni bir sözleşme testi yazmak **gerekmemelidir** — gerekiyorsa
sözleşme eksiktir ve önce o düzeltilir.

| Test | Neden |
|------|-------|
| Tüm store sözleşmeleri | Davranış eşitliği |
| Migration idempotency | İki kez çalıştırma |
| Eşzamanlı migration | İki süreç, `sp_getapplock` |
| Checksum değişimi | Değişmiş migration hata verir |
| `uniqueidentifier` sıralaması | v7 kimlikler beklenen sırada mı |
| JSON gidiş-dönüş | Polimorfik `AIContent` bozulmadan dönüyor mu |
| Zaman dilimi | `datetimeoffset` UTC olarak yazılıp okunuyor mu |

CI: SQL Server container'ı Linux'ta çalışır ama ~2 GB bellek ister. CI iş
tanımında kaynak sınırı kontrol edilmelidir.

---

## Bu Fazda Verilecek Kararlar

1. **`MERGE` kullanılıp kullanılmayacağı** — eşzamanlılık gerekçesiyle.
2. **Ortak kod paylaşımı biçimi** (kaynak paylaşımı önerilir).
3. **Migration numaraları sağlayıcı başına bağımsızdır.**
4. **AOT uyumluluğu ölçümle belirlenir** ve `MIMARI.md`'ye yazılır.
5. **İki kalıcılık sağlayıcısı aynı anda kaydedilirse uyarı loglanır.**

---

## Açık Sorular

1. **Hangi SQL Server sürümleri desteklenecek?** `ISJSON` 2016+, `datetimeoffset`
   2008+. Öneri: **2019+** (ve Azure SQL).
2. **Azure SQL özel olarak test edilecek mi?** Container ile test edilemez.
   Öneri: **desteklenir ama CI'da test edilmez**; sınır dokümante edilir.
3. **`AgentPrism.Sql` ortak paketi mi, kaynak paylaşımı mı?** Öneri: **kaynak
   paylaşımı**.
4. **Kümelenmiş indeks stratejisi?** SQL Server'da birincil anahtar varsayılan
   olarak kümelenmiştir; `uniqueidentifier` üzerinde bu parçalanma üretebilir.
   Öneri: PK **kümelenmemiş**, zaman sütunu üzerinde kümelenmiş indeks — ölçümle
   doğrulanır.

---

## Bitiş Ölçütleri (DoD)

- [ ] `AgentPrism.SqlServer` paketi üretiliyor (`dotnet pack` sayısı artıyor)
- [ ] Tüm store sözleşme testleri SQL Server üzerinde **yeşil**
- [ ] Migration'lar temiz bir veritabanında ve tekrar çalıştırmada doğru
- [ ] Eşzamanlı iki süreçte migration bir kez uygulanıyor
- [ ] Örnek uygulama `UseSqlServer` ile uçtan uca çalışıyor (gerçek çıktı)
- [ ] AOT durumu ölçüldü ve `MIMARI.md` bölüm 9 güncellendi
- [ ] Paket kontrol listesi tamam (README, slnx, meta paket, `DependencyDirectionTests`)
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Sözleşme testleri sağlayıcıya sızmış varsayımlar içerir | Bu fazın erken bulgusu olur; sözleşme düzeltilir ve bu bir kazançtır |
| `MERGE` yarış durumu | Güvenli desen; eşzamanlılık testi |
| SqlClient AOT uyumsuz | Paket bazlı bayrak; vaat verilmez |
| CI kaynak tüketimi | Container bellek sınırı; testler `[Trait]` ile ayrılabilir |
| İki sağlayıcı çakışması | Açılışta uyarı; dokümantasyon |

---

## Sonraki Faza Devir Notu

- **Faz 24 (SQLite) bu fazın kurduğu paylaşım modelini kullanır.** Üçüncü
  sağlayıcı, modelin doğru olup olmadığının asıl kanıtıdır.
- Faz 25 (saklama) her sağlayıcı için temizleme SQL'i yazmak zorundadır;
  `IRetentionStore` sözleşmesi buna göre tasarlanmalıdır.
- Faz 17'nin `SKIP LOCKED` kuyruğu SQL Server'da `READPAST` ile karşılanır;
  Faz 17 daha önce yapıldıysa o kod bu fazda taşınır.
