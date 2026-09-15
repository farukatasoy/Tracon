# Faz 24 — SQLite Desteği

> **Durum:** ✅ Tamamlandı — 205/205 sözleşme+diyalekt testi yeşil. Açık kalan tek ölçüm (yük altında `SQLITE_BUSY`) aday kuyruğundadır: **F-233**
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-07**
> **Önkoşul:** [Faz 23](23-SQL-SERVER.md) — ortak SQL soyutlaması orada olgunlaşır
> **Paketler:** **`Tracon.Sqlite` (YENİ)** · `Tracon.Abstractions`
> **Migration:** Kendi migration seti — `0001_initial.sql`
> **Kararlar:** K-190 … K-197

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/24-SQLITE.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tek dosyalık kurulum. Değeri üç yerdedir: - **Demo ve deneme** — `dotnet run` ile çalışan, veri kaybetmeyen bir kurulum - **Gömülü senaryolar** — masaüstü veya kenar (edge) uygulamaları - **Test** — bellek içi depoların ötesinde, gerçek SQL davranışı Bellek içi depolar (K-018) zaten birinci sınıf.

## Bu Fazda Verilecek Kararlar

1. **`uuid` ve zaman `TEXT` olarak saklanır** — sözlüksel sıralama garantisi.
2. **`numeric` `TEXT` olarak saklanır** — `REAL` kullanılmaz.
3. **Şema yerine tablo öneki** — K-013'ün SQLite karşılığı.
4. **Sınırlar `/api/meta` ve README'de bildirilir** — K-018 deseni.
5. **AOT durumu ölçümle belirlenir.**

---

## Bitiş Ölçütleri (DoD)

- [x] `Tracon.Sqlite` paketi üretiliyor (`dotnet pack` sayısı arttı)
- [x] Tüm store sözleşme testleri SQLite üzerinde yeşil (205/205)
- [x] WAL ve `busy_timeout` bağlantı açılışında ayarlanıyor (test)
- [x] Örnek uygulama tek dosyalık veritabanıyla uçtan uca çalışıyor (elle doğrulandı, yukarıda)
- [x] Uygulama kapatılıp açıldığında veri duruyor (gerçek çıktı — aynı tenant kimliği)
- Sınırlar README ve `/api/meta`'da bildiriliyor — **README tamam**, `/api/meta`'da `:memory:` ayrımı eksik (Açık Soru 1)
- AOT durumu ölçüldü ve `MIMARI.md` güncellendi — **ölçülmedi**, K-196
- [x] Dört doğrulama kapısı sıfır uyarı (build, format, pack, `Tracon.Core.UnitTests`; PostgreSQL 416/416 ve SQL Server 204/204 regresyonsuz)

---

## Açık Kalan

Faz 23'ün "Açık Kalan" bölümüyle aynı disiplinle: gizlenmez, açıkça yazılır.

> 🔁 **2026-09-15'te yeniden ölçüldü** (açık küçük kalem turu). Dördünden
> **ikisi bayattı** ve koda göre düzeltildi; kalan ikisi aşağıdadır.

1. **Yük altında eşzamanlılık testi yok** — **hâlâ açık; aday F-233.** `SqliteDialectTests`
   yalnız WAL ve `busy_timeout`'un **kurulduğunu** doğrular
   (`WAL_and_busy_timeout_are_set_when_the_connection_opens`), çekişme
   altındaki **davranışı** değil; `BoundedSqlLoadTests` yalnız PostgreSQL
   fixture'ıyla koşar. `SQLITE_BUSY` gerçek çekişme altında ölçülmedi.
2. **AOT/publish ölçümü yapılmadı** (K-196) — ama bu bir **boşluk değil, açık
   bir vaat yokluğudur.** `Tracon.Sqlite.csproj` `TraconAotCompatible=false`
   yazar ve `README.md`'nin AOT vaadi veren sekiz paketlik listesi bu paketi
   içermez. Ölçüm ancak vaat verilmek istenirse gerekir.

**Kapanan iki kalem (2026-09-15):**

- ~~`/api/memory` `:memory:` ayrımı yapmıyor~~ — **başka bir tasarımla
  çözüldü.** Çıplak `:memory:` artık başlangıçta **reddedilir**
  (`TraconSqliteOptionsValidator`); `/api/meta`'nın ayırması gereken,
  hiç ayağa kalkamayan bir yapılandırmadır.
- ~~Gerçek `mssql/server` doğrulanmadı (K-186)~~ — **K-386 kapattı.**
  `SqlServerFixture` `mcr.microsoft.com/mssql/server:2022-latest` kullanır ve
  fixture hiç değiştirilmeden **479/479** yeşil koştu
  (`docs/hafiza/sql-server-yerel-test.md`). CI'ın `ubuntu-latest` işi
  `Tracon.slnx`'in tamamını koşar, yani bu paket her itmede gerçek imajla
  sınanır.

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
