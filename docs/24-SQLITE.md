# Faz 24 — SQLite Desteği

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-07**
> **Önkoşul:** [Faz 23](23-SQL-SERVER.md) — ortak SQL soyutlaması orada olgunlaşır
> **Paketler:** **`AgentPrism.Sqlite` (YENİ)** · `AgentPrism.Abstractions`
> **Migration:** Kendi migration seti

---

## Bu Faza Başlarken

1. [`23-SQL-SERVER.md`](23-SQL-SERVER.md) — paylaşım modeli ve çeviri deseni
2. [`02-POSTGRESQL-KALICILIK.md`](02-POSTGRESQL-KALICILIK.md) — şablon
3. [`KARARLAR.md`](KARARLAR.md) — **K-018** (bellek içi depolar birinci sınıf), **K-015** (uuid v7)
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
| `uuid` v7 | `TEXT` (küçük harf, tireli) | Sözlüksel sıralama = zaman sıralaması. `BLOB` daha küçüktür ama okunabilirlik ve sıralama TEXT'te doğrudan çalışır |
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

`pg_advisory_lock` veya `sp_getapplock` karşılığı yoktur. SQLite tek yazıcılıdır
ve çözüm daha basittir:

```sql
BEGIN IMMEDIATE;   -- yazma kilidini HEMEN alir
-- migration adimlari
COMMIT;
```

`BEGIN IMMEDIATE` ile iki süreç aynı anda migration çalıştıramaz; ikincisi
`busy_timeout` süresince bekler ve sonra hata verir. Bu davranış doğrudur ve
test edilir.

---

## 24.4 — Paket ve Kayıt

```csharp
builder.AddAgentPrism()
       .UseSqlite("Data Source=agentprism.db");

// Bellekte, test icin:
builder.AddAgentPrism()
       .UseSqlite("Data Source=:memory:");   // SINIR: baglanti kapanirsa veri gider
```

- Şema kavramı yoktur; tablo adları `agentprism_` **öneki** alır. K-013'ün
  ("tüketicinin şemasına dokunma") SQLite karşılığı budur. Önek
  yapılandırılabilir ve aynı katı doğrulamadan geçer (K-029)
- `UseSqlite` `Replace` kullanır (K-025)
- Bağlantı havuzu: `Microsoft.Data.Sqlite` havuzu destekler; `Pooling=True`
  varsayılan bırakılır ve WAL ile birlikte ölçülür
- AOT: `Microsoft.Data.Sqlite` (10.0.10) AOT uyumluluğu **ölçülür**; SQLitePCLRaw
  yerel kütüphane taşır ve bu, yayınlama (publish) davranışını etkileyebilir

---

## 24.5 — Testler

`tests/AgentPrism.Sqlite.IntegrationTests` — container **gerekmez**, dosya
tabanlı geçici veritabanı yeter. Bu, CI süresini kısaltır.

| Test | Neden |
|------|-------|
| Tüm store sözleşmeleri | Üçüncü uygulama; soyutlamanın asıl sınavı |
| WAL ve `busy_timeout` ayarlanıyor mu | Bağlantı açılış davranışı |
| Eşzamanlı yazma | 10 paralel yazıcı; `SQLITE_BUSY` yerine bekleme |
| Migration `BEGIN IMMEDIATE` | İki süreç |
| Kimlik ve zaman sıralaması | v7 kimlikler ve zaman damgaları sözlüksel sırada mı |
| `decimal` gidiş-dönüş | Faz 20 maliyeti kayıpsız mı |
| Dosya taşınabilirliği | Yazılan dosya başka bir süreçte açılıp okunabiliyor mu |

**Sözleşme testlerine yeni test eklemek gerekmemelidir.** Gerekiyorsa
soyutlamada bir eksik vardır (Faz 23'ün aynı ölçütü).

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
   Öneri: **evet**, ama `/api/meta` bunu "kalıcı değil" olarak bildirir.
2. **Faz 17 işçisi SQLite'ta çalışsın mı?** Tek yazıcı sınırı var ama tek
   örnekli kurulumda sorun değil. Öneri: **evet**, çok örnekli kullanım
   README'de yasaklanır.
3. **Otomatik `VACUUM` / bakım?** Öneri: **hayır** — Faz 25'in saklama işi
   silme yaptıktan sonra isteğe bağlı `VACUUM` çalıştırabilir.

---

## Bitiş Ölçütleri (DoD)

- [ ] `AgentPrism.Sqlite` paketi üretiliyor
- [ ] Tüm store sözleşme testleri SQLite üzerinde yeşil
- [ ] WAL ve `busy_timeout` bağlantı açılışında ayarlanıyor (test)
- [ ] Örnek uygulama tek dosyalık veritabanıyla uçtan uca çalışıyor
- [ ] Uygulama kapatılıp açıldığında veri duruyor (gerçek çıktı)
- [ ] Sınırlar README ve `/api/meta`'da bildiriliyor
- [ ] AOT durumu ölçüldü ve `MIMARI.md` güncellendi
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kullanıcı SQLite'ı üretimde çok örnekli kullanır | Sınır README, `/api/meta` ve açılış logunda bildirilir |
| Sıralama biçimi bozulur | Biçim sabit ve testli |
| Yerel kütüphane yayınlama sorunları | AOT/publish ölçümü DoD'de |
| `SQLITE_BUSY` hataları | WAL + `busy_timeout` + eşzamanlılık testi |

---

## Sonraki Faza Devir Notu

- Faz 25 (saklama) artık **üç** sağlayıcı için temizleme yazmak zorundadır.
  `IRetentionStore` sözleşmesi sağlayıcıdan bağımsız kalmalıdır.
- Üç uygulamalı sözleşme testleri, sonraki her depo değişikliğinde üç kez
  koşacaktır; CI süresi ölçülmeli ve gerekirse SQLite testleri hızlı katmana
  alınmalıdır.
