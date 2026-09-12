# Faz 25 — Veri Saklama Politikası ve Arşivleme

> **Durum:** ✅ **Tamamlandı (2026-08-05)**
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-08**
> **Önkoşul:** [Faz 17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) — temizleme işi kuyruğu kullanır
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql` (+ varsa `.SqlServer`, `.Sqlite`), `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0014 (planlanan sırada)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/25-VERI-SAKLAMA-VE-ARSIVLEME.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Üretimde `run_events` **sınırsız büyür**. Faz 17'den sonra `jobs`, Faz 21'den sonra `webhook_deliveries`, Faz 18'den sonra `eval_case_results` da öyle. Bu faz üç şey yapar: 1. **Saklama politikası** — yaş ve hacim bazlı temizleme 2. **Arşivleme** — soğuk depolamaya taşıma (soyutlama ile, bulut SDK'sı olmadan) 3.

## Plandan Sapmalar

Uygulama sırasında dokümanın ilk taslağından şu noktalarda ayrıldı; gerekçeleri
karar defterine yazıldı (K-198…K-203, bkz. `docs/KARARLAR.md`).

1. **`SELECT`/`DELETE`/`COUNT` SQL'i saglayıcı başına elle KOPYALANMADI.**
   25.3'ün taslağı her hedef için ayrı sorgu önerir gibi okunabilir; bunun
   yerine tek bir veri tablosu (`RetentionTargetRegistry`, `Sql.Shared/Internal/`)
   her hedefin tablosunu ve "eski" koşulunu tanımlar, `SqlDialect` yalnız 3
   şablon yöntemi (say/oku/sil) sağlar. 11 hedef × 3 sorgu × 3 sağlayıcı = 99
   elle yazılmış sorgu yerine 11 kayıt + 9 şablon yöntemi. Gerekçe: K-198.
2. **`SqlDialect.QualifyTable` eklendi.** Registry saglayıcıdan bağımsız
   olmalıydı ama PostgreSQL/SQL Server `sema.tablo` (nokta ile), SQLite
   `onektablo` (noktasız, K-193) kullanır. Yeni bir sanal yöntem bu farkı
   kapsar; SQLite `QualifyTable`'ı ezer, diğer ikisi varsayılanı kullanır.
3. **`retention_runs.tenant_id` eklendi.** 25.2'nin ilk taslağında bu sütun
   yoktu. Kiracı bazlı politika (25.1 açık soru #4, kullanıcı kararı: evet)
   kosu geçmişinin de kiracıya göre süzülebilmesini gerektirir.
4. **Zamanlama için yeni uç YAZILMADI.** `JobKind.Retention` eklenip
   `IJobHandler` kaydedildikten sonra Faz 17'nin var olan
   `/api/job-schedules` uçları (kind=Retention, targetName="*" veya belirli
   bir hedef, cron="0 3 * * *") günlük zamanlamayı hiçbir yeni kod olmadan
   karşılar. Yalnız retention'a özgü 4 uç grubu yeni: politika CRUD,
   `preview`, `run` (kuyruğa yazar), `history`.
5. **Hedef gruplaması dokümanın tablosundan biraz farklı.** "traces / spans"
   tek hedef (`traces`) olarak silinir, `spans` `ON DELETE CASCADE` ile gider.
   "jobs / job_items" de aynı şekilde tek hedef (`jobs`), `job_items` cascade
   gider. "sessions / conversation_items" ise İKİ ayrı hedefe bölündü
   (`sessions`, `conversations`) — `conversations` silinince
   `conversation_items` ve `responses` cascade gider. Kullanıcıya daha ince
   kontrol verir, varsayılan davranış (ikisi de kapalı) değişmez.
6. **`MaxRows` depoya yazılır/okunur ama `RetentionExecutor` tarafından
   UYGULANMAZ.** Yalnız `MaxAgeDays` bu fazda etkindir. Şema ve API alanı
   hazır; hacim bazlı kırpma ölçüm olmadan eklenmedi (K-063'ün kendi
   gerekçesiyle aynı desen — bkz. K-201).
7. **SQL Server gerçek `mssql/server`'a karşı BU OTURUMDA koşmadı.** Apple
   Silicon + Rosetta kapalı ortam kısıtı Faz 23'ten beri aynı (bkz.
   `23-SQL-SERVER.md`). `azure-sql-edge` denendi; Testcontainers'ın
   `MsSqlBuilder` hazır olma denetimi konteyner içinde `sqlcmd` arar ve
   `azure-sql-edge` imajında bu ikili yok — bu yüzden o ürün de bu ortamda
   koşmuyor (Faz 23'ün K-187…K-189 bulguları farklı bir makinede alınmıştı).
   Kod PostgreSQL/SQLite ile aynı paylaşılan `Sql.Shared` gövdesini kullanır
   ve temiz derlenir; üç sağlayıcı da aynı kod yolundan geçtiği için risk
   düşüktür ama SQL Server'a özgü sözleşme testleri bu oturumda **doğrulanmadı**.

---

## Bu Fazda Verilen Kararlar

1. **`audit_log` varsayılan olarak silinmez; `conversation_items` (ve `sessions`) sunulur ama varsayılan kapalıdır** — DB'de kayıt yoksa ve yapılandırma boşsa hiçbir şey silinmez.
2. **Varsayılan politika yoktur** — sürüm yükseltmesi veri silmez.
3. **Arşiv yazılamıyorsa silme yapılmaz.**
4. **Parti silme, toplu `DELETE` değil** — üç sağlayıcı üç farklı teknik kullanır (K-200).
5. **K-063 kapandı** — ölçümle: partition **açılmadı** (K-199).
6. **`IArchiveSink` genişleme noktasıdır; bulut SDK bağımlılığı yok** (K-007); `samples/Tracon.Api/FileSystemArchiveSink.cs` şablon örnektir.

---

## Açık Sorular — Karara Bağlandı

1. **`sessions` ve `conversation_items` için politika sunulsun mu?** **Evet, varsayılan kapalı.** (kullanıcı kararı)
2. **Silme işi ne zaman koşsun?** **Günlük, yapılandırılabilir saatte** — yeni kod gerekmedi, Faz 17'nin `/api/job-schedules` ucuna `kind=Retention` ile bir kayıt eklemek yeterli (bkz. Plandan Sapmalar #4).
3. **Arşiv biçimi JSONL + gzip yeterli mi?** **Evet, JSONL + gzip.** (kullanıcı kararı)
4. **Kiracı bazında farklı saklama süresi gerekli mi?** **Evet**, `tenant_id = '*'` varsayılan. (kullanıcı kararı)

---

## Bitiş Ölçütleri (DoD)

- [x] Politika tanımlanıp çalıştırılıyor; `preview` doğru sayı veriyor — örnek uygulamaya karşı `curl` ile doğrulandı
- [x] `run_events` temizleniyor, `runs` özeti **korunuyor** — `RetentionDataPlaneTests.Run_events_silinirken_runs_ozeti_korunur`
- [x] `audit_log` varsayılan yapılandırmada **hiç silinmiyor** — beyaz listede yok, `RetentionDataPlaneTests` bunu bilerek dener ve `ArgumentException` bekler
- [x] Arşiv sink'i olmadan `archive = true` politikası silmiyor — `RetentionExecutorTests.Arsiv_istenip_sink_kayitli_degilse_hicbir_satir_silinmez`
- [x] Örnek dosya sistemi sink'i JSONL üretiyor ve geri okunabiliyor — `FileSystemArchiveSink` (gzip üyeleri ardışık; standart okuyucular tek akış gibi açar)
- [x] Yük ölçümü yapıldı; **K-063 kararı yazıldı** — yukarıdaki tablo, K-199
- [x] Üç sağlayıcıda da (kurulu olanlarda) temizleme çalışıyor — PostgreSQL (gerçek container, 441 test) ve SQLite (214 test) bu oturumda doğrulandı; SQL Server kodu aynı paylaşılan gövdeyi kullanır ve derlenir ama bu makinede gerçek `mssql/server` koşmadı (Plandan Sapmalar #7)
- [x] Dört doğrulama kapısı sıfır uyarı — `build`/`test`/`pack`/`format` hepsi temiz

---

## Sonraki Faza Devir Notu

- **K-063 kapandı — partition açılmadı.** `run_events` mevcut ölçekte
  (100k satır) parti silme ile saniyede 720 bin satır siliniyor; tekrar
  açılması için tetikleyici, ölçümde gerçek bir darboğaz görülmesidir (bkz.
  K-199'un "yeniden açılma koşulu" sütunu).
- **Faz 26 (Anthropic/Gemini) bu fazdan bağımsızdır** — hiçbir sözleşme veya
  dosya paylaşmaz, "Bu Faza Başlarken" listesi değişmedi.
- **Yeni tablo ekleyen her faz, saklama hedef listesine kendi tablosunu
  eklemekle yükümlüdür** — `RetentionTargets` (Abstractions) +
  `RetentionTargetRegistry` (Sql.Shared) + `TraconRetentionOptions`
  (Core) üçlüsüne bir kayıt. Bu kural `faz-tamamlama` skill'ine eklenmelidir
  (henüz eklenmedi — bu oturumun kendi kapsamı dışında bırakıldı).
- **Yarım kalanlar:**
  - `MaxRows` (hacim bazlı kırpma) depoda var, yürütülmüyor.
  - SQL Server: kod hazır, gerçek `mssql/server`'a karşı bu oturumda
    koşmadı (ortam kısıtı, Plandan Sapmalar #7). Linux/amd64 bir makinede
    veya CI'da `dotnet test tests/Tracon.SqlServer.IntegrationTests`
    çalıştırılmalı.
  - `Tracon.Ui.E2ETests`'e Playwright senaryosu eklenmedi; panel yalnız
    örnek uygulamaya karşı `curl` ile ve `tsc`/Vitest ile doğrulandı.
  - Arşiv sink'i yalnız dosya sistemi örneğiyle test edildi (birim testinde
    sahte sink ile); gerçek bir S3/Blob sink'i Tracon'in kapsamında
    değildir (K-007).
- **`docs/KARARLAR-INDEKS.md` bütçesi ilk kez aşıldı, 24 KB → 25 KB'ye
  çıkarıldı** (`scripts/dokuman-bakim.py`). 203 karar + 24 reddedilen işle
  indeks yapısal olarak büyümeye devam edecek; kalıcı çözüm (bölüm bazlı
  indeks veya eski faz aralıklarının arşivlenmesi) henüz yazılmadı — bir
  sonraki bütçe aşımında ele alınmalı.
