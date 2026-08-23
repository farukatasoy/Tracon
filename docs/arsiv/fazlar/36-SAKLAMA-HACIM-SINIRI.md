# Faz 36 — Saklama Hacim Sınırı (`MaxRows`)

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-73**
> **Önkoşul:** [Faz 25](25-VERI-SAKLAMA-VE-ARSIVLEME.md) — saklama altyapısı, üç diyalekt şablonu
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`
> **Yeni paket:** Yok · **Migration:** Yok — sütun **zaten var**
> **Public API:** büyümüyor — var olan alan **çalışır hâle gelir**

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/36-SAKLAMA-HACIM-SINIRI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`MaxRows` **tanımlı, doğrulanan, saklanan ama uygulanmayan** bir ayardır. Kullanıcı bugün onu ayarlıyor, kaydediliyor, arayüzde görünüyor — ve hiçbir şey olmuyor. Bu, sessiz bir yanlıştır ve yayımlanmış bir sözleşmenin ihlalidir. - **F-73** — hacim bazlı silme: en eski satırlardan başlayarak tablo satır sayısını sınırın altına indirmek.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 Yalnız `MaxRows = 100` taşıyan politika (yaş alanı **boş**), 150
      satırlık bir hedefte **50 satır** siler. Bugün sıfır siliyor — **gerçek
      koşuyla kanıtlandı**, aşağıya bakın
- [x] Tablo sınırın altındayken hiçbir silme sorgusu çalışmaz —
      `MaxRows_tablo_sinirin_altindaysa_hicbir_silme_sorgusu_calismaz`,
      `MaxRows_esigi_tablo_sinirin_altindaysa_null_doner` (Postgres + SQLite)
- [x] İki alan doluyken daha çok silen eşik uygulanır —
      `Iki_esik_doluyken_daha_yeni_olan_kazanir`
- [x] `GET /api/retention/preview` önizlemesi gerçek koşuyla aynı sayıyı verir
      (`Onizleme_MaxRows_esigini_gercek_kosuyla_ayni_hesaplar` + gerçek koşu:
      önizleme 50, gerçek koşu 50 sildi)
- [x] Üç SQL sağlayıcısında sözleşme testleri geçer — PostgreSQL (505/505) ve
      SQLite (255/255) **gerçekten koşturuldu**; SQL Server kodu yazıldı ve
      derlendi ama bu makinede **koşmadı** (Apple Silicon + Docker kısıtı,
      Faz 25'in bilinen sınırı — bkz. Plandan Sapmalar)
- [x] `RetentionTypes.cs`'teki *"henüz UYGULANMAZ"* cümlesi **silindi**
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek saklama koşusu yapıldı, çıktı bu
      belgeye yazıldı
- [x] `secret` taraması boş döndü

### Doğrulama komutları — gerçek çıktı (2026-08-06, SQLite, `samples/AgentPrism.Api`, port 5080)

`run_events` tablosuna doğrudan SQL ile 150 satır (tek `run`'a bağlı) yazıldı,
uygulama `Data Source=.../f36.db` ile başlatıldı:

```bash
$ curl -s -X PUT http://localhost:5080/agentprism/api/retention/run_events \
  -H 'content-type: application/json' -d '{"maxRows":100,"enabled":true}'
{"id":"019fd77f-...","tenantId":"default","target":"run_events",
 "maxAgeDays":null,"maxRows":100,"archive":false,"enabled":true, ...}

$ curl -s "http://localhost:5080/agentprism/api/retention/preview?target=run_events"
[{"target":"run_events","maxAgeDays":null,"enabled":true,
  "cutoff":"2026-08-06T12:54:59.006895+00:00","matchingRows":50}]

$ curl -s -X POST "http://localhost:5080/agentprism/api/retention/run?target=run_events"
{"jobId":"019fd780-...","target":"run_events"}

$ curl -s "http://localhost:5080/agentprism/api/retention/history?target=run_events"
[{"id":"019fd780-...","target":"run_events","deletedRows":50,"archivedRows":0,
  "completedAt":"2026-08-06T14:35:22.163635+00:00","error":null}]

$ sqlite3 f36.db "SELECT count(*) FROM agentprism_run_events;"
100
```

Önizlemenin verdiği `50` ile gerçek koşunun sildiği `50` **aynıdır**; kalan
satır sayısı tam `100`'dür. Bu, DoD'nin 🚨 satırının doğrudan kanıtıdır.

> Not: gerçek portu `5080`'dir (`launchSettings.json`); plandaki `5081`
> yanlıştı, düzeltildi.

---

## Plandan Sapmalar

1. **Açık Soru 2 farklı çözüldü (B, plan A öneriyordu).** `RetentionTargetRegistry.OrderColumn`
   (arşiv sıralaması) ile hacim eşiği hesabı **ayrıldı**. `RetentionTargetDefinition`'a
   dördüncü bir alan (`RowLimitOrderExpression`) eklendi. 9 hedefte bu, `WherePredicate`'in
   karşılaştırdığı sütunla özdeştir; `eval_case_results`/`workflow_checkpoints`'te
   bağlı tabloya (`eval_runs`/`runs`) bakan korele bir alt sorgudur;
   `skill_script_grants`'te `COALESCE(expires_at, revoked_at)`'tir. Bkz. K-261.
2. **Açık Soru 3 farklı çözüldü (B, plan A öneriyordu) — Faz 36 KAPANIŞINDA.**
   `MaxRows` kiracı başına değil, tablo genelinde çalışıyordu — o an mevcut 3
   şablonun (Faz 25, K-198) HİÇBİRİNDE `tenant_id` filtresi yoktu; `MaxAgeDays`
   da o zaman tablo genelinde siliyordu. `MaxRows`'u kiracıya özel yapmak iki
   eşik arasında asimetri yaratırdı. Bkz. K-260.

   > ⚠️ **Bu K-260 notu ARTIK ESKİMİŞ (2026-08-10, `docs/manuel-test/20-BELLEK-RAG-BAGLAM.md`
   > üretilirken kod okumasıyla ölçüldü).** Güncel `IRetentionStore.FindRowLimitCutoffAsync`
   > imzası bir `string? tenantId` parametresi taşır (`null` ⇒ kurulum genelinde
   > `'*'` politikası) ve `RetentionExecutor.cs:230` onu gerçekten geçirir —
   > muhtemelen Faz 41'in `DeleteBatchAsync`'e kiracı sınırlaması eklediği
   > değişiklikle birlikte veya sonrasında `MaxRows`'a da uygulanmış, ama bu
   > sayfa hiç güncellenmemiş. Güncel (doğru) davranış
   > `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`'nin `MT-RET-023` case'i ile iki
   > kiracıya karşı doğrulanır. Ders: plandan-sapma notları o fazın KAPANIŞ
   > anına aittir, sonraki fazlarda sessizce eskiyebilir.
3. **🆕 Faz 25'in kendi hatası keşfedildi ve düzeltildi (plan bunu öngörmüyordu).**
   `eval_case_results`/`workflow_checkpoints`/`attachments` hedeflerinin
   `WherePredicate`'i BARE hedef adını (`eval_case_results.eval_run_id` gibi)
   korelasyon olarak kullanıyordu. PostgreSQL/SQL Server'da çalışıyordu (ikisi
   de bare adla `schema.table`'ı eşleştirir) ama **SQLite'ta hiç çalışmıyordu**
   (`QualifyTable` önek+ad bitiştirir, K-193) — Faz 36'nın SQLite'a karşı
   kayan `MaxRows` sözleşme testi `no such column: workflow_checkpoints.run_id`
   ile bunu YAKALADI. Bu, `MaxAgeDays` ile de bu üç hedefin SQLite'ta bugüne
   kadar sessizce yanlış çalıştığı (`EXISTS`/`NOT EXISTS` her zaman aynı sabit
   sonucu döndürdüğü) anlamına gelir. Düzeltme `RetentionTargetRegistry`'de
   `Table("attachments")` gibi TAM NİTELENDİRİLMİŞ ad kullanır. Bkz. K-259.
   Yeni bir regresyon testi (`Attachments_sahipli_ek_silinmez_sahipsiz_ek_silinir`,
   SQLite) bu düzeltmeyi kanıtlar.
4. **UI planın iddia ettiği kadar hazır değildi.** Plan "MaxRows alanı arayüzde
   zaten vardır; yalnız artık çalışır" diyordu. İnceleme gösterdi ki
   `retention-panel.tsx`'in `PolicyForm`'u yalnız `maxAgeDays` alanı taşıyordu
   ve Kaydet düğmesi `maxAgeDays` boşken KAPALIYDI — bir kullanıcı `MaxRows`'u
   arayüzden HİÇBİR ZAMAN ayarlayamazdı (yalnız TS tipinde `maxRows?: number`
   vardı, form alanı yoktu). `MaxRows` girdisi eklendi, Kaydet düğmesi artık
   iki alandan biri doluyken etkin.
5. **SQL Server sözleşme testleri bu makinede koşmadı** (Docker/Apple Silicon
   kısıtı, Faz 25'ten beri bilinen sorun — `docs/hafiza/sql-saglayicilari.md`).
   Kod yazıldı, derlendi, SQL üretimi `RetentionMaxRowsDialectTests`
   (canlı DB gerektirmeyen kısım) ile doğrulandı. Gerçek `mssql/server`'a
   karşı koşu Linux/amd64 bir makinede veya CI'da yapılmalı.
6. **`AgentPrism.Ui.E2ETests`'e Playwright senaryosu eklenmedi** (Faz 25'in
   aynı sapması) — arayüz değişikliği küçük bir form alanı eklemekti,
   `tsc --noEmit`, Vitest (141/141) ve gerçek Vite build/bundle bütçesi
   (155,2 KB gzip / 250 KB) ile doğrulandı.

## Bu Fazda Verilen Kararlar

K-258, K-259, K-260, K-261 — bkz. `docs/KARARLAR.md`. K-201 (`MaxRows`
ertelendi) bu fazda **kapandı** (K-258'e atıfla).

## Sonraki Faza Devir Notu

- **`IRetentionStore` sözleşmesi büyüdü** (`FindRowLimitCutoffAsync`); yeni bir
  `IRetentionStore` uygulaması yazan biri (varsayılan bellek içi hariç, çünkü
  `NullRetentionStore` zaten kayıtlı) bu üyeyi de uygulamalıdır.
- **🚨 `RetentionTargetRegistry`'de bir hedefin `WherePredicate`'i başka bir
  tabloya (EXISTS/NOT EXISTS) bakıyorsa, korelasyon MUTLAKA `Table(...)` (tam
  nitelendirilmiş ad) ile yazılmalıdır — BARE hedef adı YAZILMAZ.** SQLite'ta
  `QualifyTable` önek+ad bitiştirir (nokta yok, K-193); bare ad orada hiçbir
  zaman FROM'daki gerçek nesneyle eşleşmez ve hata yalnız ÇALIŞMA ANINDA
  görünür (derleme/PostgreSQL/SQL Server testi YAKALAMAZ). Bu, Faz 36'da
  keşfedilen ve düzeltilen bir Faz 25 hatasıydı (K-259); yeni bir hedef
  eklerken bu kural izlenmelidir.
- **`RetentionTargetDefinition` artık 4 alan taşır**: `Table`, `WherePredicate`,
  `OrderColumn` (arşiv sıralaması), `RowLimitOrderExpression` (hacim eşiği).
  Yeni bir hedef eklerken dördü de doldurulmalıdır.
- **Yarım kalanlar:**
  - SQL Server: `MaxRows` kodu hazır, gerçek `mssql/server`'a karşı bu
    oturumda koşmadı (ortam kısıtı — Faz 25'in aynı sınırı). Linux/amd64 bir
    makinede veya CI'da `dotnet test tests/AgentPrism.SqlServer.IntegrationTests`
    çalıştırılmalı.
  - `AgentPrism.Ui.E2ETests`'e Playwright senaryosu eklenmedi (Faz 25'in aynı
    sapması, hâlâ kapatılmadı).
  - `RetentionTargetRegistry`'nin `WherePredicate`'i genelinde (bu fazın 3
    düzelttiği hedef dışında kalan) tenant_id filtresi YOKTUR — Faz 25'ten
    beri var olan, dokümante edilmemiş bir davranış (K-260 bunu şimdi
    dokümante etti). Kiracı izolasyonlu saklama istenirse ayrı bir faz gerekir.
- **Sıradaki faz:** [Faz 37 — Proje Şablonu](37-PROJE-SABLONU.md).
