# 23 — Saklama, Arşiv, Kota ve Çalıştırma-İçi Bütçe — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2 devam):** §1 (MT-RET-001..015) bitiyor —
> 001, 002, 003, 004, 005, 006, 010, 011, 012 = **9/9 Geçti, 0 Kaldı** şu ana
> kadar. Sırada 013, 014, 015, sonra §2 (020-023), §3 (yok, atlanmış numara),
> §4 kota (030-035, GERÇEK OpenAI çağrısı), §5 bütçe (050-055, GERÇEK OpenAI),
> §6 süre bütçesi (060-064), §7 SSE bildirimleri (070-076).
>
> **Ortam:** ap-s2'nin PAYLAŞILAN PostgreSQL örneği (port 5082) bu aile için
> UYGUN DEĞİL — case'ler SQLite'a özgü doğrudan SQL fixture'ları kullanıyor.
> Bunun yerine kendi portumda (5087, boş olduğu doğrulandı — 5086 **başka bir
> şeride ait**, dokunulmadı) izole, tazeden kurulmuş bir SQLite örneği açıldı:
> `Tracon__Sqlite__ConnectionString="Data Source=/tmp/ap-s2-ret.db"`,
> `Tracon__PostgreSql__ConnectionString=""` (paketlenmiş `artifacts/bin/
> Tracon.Api/release/Tracon.Api.dll` doğrudan çalıştırılıyor, kod dokunulmadı).
> Bu, dosya 03/05'in "yapılandırma katmanı" tarifiyle aynı desendir. Arşiv
> testi için `/tmp/ap-s2-arsiv` kullanıldı. Tur sonunda hepsi silinecek.

---

## MT-RET-001 — Politika kaydedilir, `preview` doğru sayar, `run` gerçekten siler

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL — şema
kolonları yeniden adlandırılmış):** Case'in SQL'i `tracon_runs` için
`created_at`/`updated_at` kolonları kullanıyor; gerçek şemada bunlar
`started_at`/`completed_at`'tır (ayrıca `is_streaming` NOT NULL, doldurulmalı).
Düzeltilmiş INSERT ile 12 `run_events` satırı (10'u 40 gün önce, 2'si bugün)
eklendi. `PUT .../run_events` politikası (`maxAgeDays:30`) kaydedildi.
`preview` → `matchingRows: 10`. `run` → `{"jobId":"...","target":"run_events"}`
(silinen sayı yok — beklenen). `history[0]` → `deletedRows: 10, error: null`.
Doğrudan SQL sayımı → **2** (yalnız güncel satırlar kaldı). `runs` özet satırı
korundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-002 — Bilinmeyen hedef adı reddedilir (beyaz liste)

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (K-228 dil sınırı, kusur değil):** `400 Bad Request`,
başlık Türkçe "Bilinmeyen hedef" değil İngilizce **"Unknown target"**.
Gövde tanınan 16 hedefin tam listesini taşıyor, spec'teki liste ile birebir
eşleşiyor: `run_events, tool_invocations, traces, jobs, webhook_deliveries,
eval_case_results, workflow_checkpoints, skill_script_grants, attachments,
sessions, conversations, voice_sessions, run_scores, idempotency_keys,
run_inputs, document_embeddings`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-003 — `preview` hiçbir satır silmez

**Gerçek sonuç**
Üç ardışık `preview` çağrısı, üçü de `matchingRows: 0` (MT-RET-001 zaten
eskimiş satırları temizlemişti) — tutarlı, değişmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-004 — `run` ucu SENKRON silmez, bir iş kuyruğa yazar

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, kusur değil — alan adı
değişmiş):** `GET /api/jobs/{jobId}` yanıtında `kind` alanı **yok** —
bunun yerine `handlerKey: "tracon.retention"` var (aynı bilgiyi taşıyor,
farklı adla). `targetName: "run_events"` doğrulandı. `status: "Pending"`
(senkron tamamlanmış yanıt yok) — beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-005 — Boş politika tablosunda hiçbir şey silinmez

**Gerçek sonuç**
`tool_invocations` için (politikasız): `{"enabled":false,"matchingRows":0}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-006 — `history` geçmiş çalıştırmaları listeler

**Gerçek sonuç**
`?target=run_events&take=5` → her iki kayıt da `id, target, deletedRows,
archivedRows, startedAt, completedAt, error` alanlarını taşıyor, `target`
her ikisinde de `run_events`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-010 — `audit_log` beyaz listede YOKTUR, asla otomatik silinmez

**Gerçek sonuç**
`PUT .../audit_log` → `400`, aynı "Unknown target" hata yolu, 16 hedef
listesinde `audit_log` hiç geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-011 — `Enabled=false` kaydı, config'teki varsayılanı da GÖLGELER

**Gerçek sonuç**
🚨🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL — ama
ciddi bir kurulum hatası):** Case'in "Ön koşul"u
`Tracon:Retention:Traces:MaxAgeDays` ayarlamayı söylüyor — bu **yanlış
anahtar**. Kaynakta doğrulandı (`TraconRetentionOptions.cs`): `traces`
hedefi `Spans` özelliğine eşleniyor (`ForTarget`: `RetentionTargets.Traces
=> Spans`), `Traces` diye bir özellik **yok**. Ayrıca case hiç bahsetmiyor
ama `Enabled` özelliği **ayrı bir üst düzey bayraktır**, varsayılan
`false`dır ve "bir paket yükseltmesi tüketici config eklemeden veri
silmemeli" diye özellikle böyle tasarlanmış — `Tracon:Retention:Enabled=true`
**de** verilmeden hiçbir config varsayılanı devreye girmez.

Doğru kurulumla (`Tracon__Retention__Enabled=true` +
`Tracon__Retention__Spans__MaxAgeDays=14`) mekanizmanın **kendisi** tam
beklenen gibi çalıştı: DB kaydı `Enabled=false` iken `preview` →
`{"enabled":false,"maxAgeDays":null}` (config'in 14'ü **gölgeleniyor**,
tam beklenen). DB kaydı silinince (`DELETE`, `204`) `preview` →
`{"enabled":true,"maxAgeDays":14,"cutoff":"..."}` — config devreye
**girdi**, tam beklenen. Mekanizma sağlam; yalnız case'in kurulum
talimatı bayattı/eksikti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RET-012 — Arşiv sink'i yokken `archive=true` HİÇBİR satır silmez

**Gerçek sonuç**
İki eski satır eklendi (40 gün önce). `archive=true` politikasıyla `run`
çalıştırıldı — **sink henüz kayıtlı değilken**: `deletedRows: 0,
archivedRows: 0`, satır sayısı değişmedi (4). Uygulama
`Tracon:Retention:ArchivePath=/tmp/ap-s2-arsiv` ile yeniden başlatıldı
(`FileSystemArchiveSink` artık kayıtlı) — aynı politika ile `run`:
**`deletedRows: 2, archivedRows: 2`**, `/tmp/ap-s2-arsiv/run_events/
2026-09-17.jsonl.gz` dosyası oluştu. İki yarı da tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
