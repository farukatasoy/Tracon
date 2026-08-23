# Faz 24 — SQLite Desteği

> **Durum:** ✅ Kod tamam · 205/205 sözleşme+diyalekt testi yeşil · AOT ölçülmedi (bkz. "Açık Kalan")
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-07**
> **Önkoşul:** [Faz 23](23-SQL-SERVER.md) — ortak SQL soyutlaması orada olgunlaşır
> **Paketler:** **`AgentPrism.Sqlite` (YENİ)** · `AgentPrism.Abstractions`
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

- [x] `AgentPrism.Sqlite` paketi üretiliyor (`dotnet pack` sayısı arttı)
- [x] Tüm store sözleşme testleri SQLite üzerinde yeşil (205/205)
- [x] WAL ve `busy_timeout` bağlantı açılışında ayarlanıyor (test)
- [x] Örnek uygulama tek dosyalık veritabanıyla uçtan uca çalışıyor (elle doğrulandı, yukarıda)
- [x] Uygulama kapatılıp açıldığında veri duruyor (gerçek çıktı — aynı tenant kimliği)
- Sınırlar README ve `/api/meta`'da bildiriliyor — **README tamam**, `/api/meta`'da `:memory:` ayrımı eksik (Açık Soru 1)
- AOT durumu ölçüldü ve `MIMARI.md` güncellendi — **ölçülmedi**, K-196
- [x] Dört doğrulama kapısı sıfır uyarı (build, format, pack, `AgentPrism.Core.UnitTests`; PostgreSQL 416/416 ve SQL Server 204/204 regresyonsuz)

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
