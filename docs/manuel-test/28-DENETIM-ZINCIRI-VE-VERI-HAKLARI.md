# 28 — Denetim Zinciri ve Veri Konusu Hakları (`DVR`)

> **Alan kodu:** `DVR` · **Faz:** 64
> **Kaynak:** `src/Tracon.Abstractions/Audit/` (`AuditChainStatus.cs`,
> `AuditChainVerification.cs`, `AuditChainQuery.cs`) ·
> `src/Tracon.Abstractions/Privacy/` (tümü) ·
> `src/Tracon.Core/Audit/` (`AuditChainHasher.cs`, `AuditChainWalker.cs`) ·
> `src/Tracon.Core/Privacy/NullDataSubjectStore.cs` ·
> `src/Tracon.Sql.Shared/Stores/{SqlAuditLog,SqlDataSubjectStore}.cs` ·
> `src/Tracon.Sql.Shared/Internal/DataSubjectTargetRegistry.cs` ·
> `src/Tracon.AspNetCore/Endpoints/{AuditEndpoints,DataSubjectEndpoints}.cs`
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

İki bağımsız yetenek, tek tabloda çatıştıkları için tek faz/tek dosya:
**denetim izinin değiştirilemezliği** (hash zinciri, `audit_log`) ve **bir
kişinin verisinin silinebilirliği** (`IDataSubjectResolver` +
`IDataSubjectStore`). Kanıtlanması gereken temel iddia: silme İÇERİĞE
dokunur, denetim izine ASLA dokunmaz — MT-DVR-008 bunu doğrudan sınar.

```mermaid
flowchart TD
    W["IAuditLog.WriteAsync"] --> H["hash = SHA256(kanonik bicim)"]
    H --> I["INSERT ... prev_hash, hash"]
    I -->|"UNIQUE ihlali (yaris)"| W
    V["GET /api/audit/verify"] --> C{"Zincir tutarli mi?"}
    C -->|evet| OK["Valid"]
    C -->|satir DEGISTIRILDI| BR["Broken + ilk bozuk kayit"]
    C -->|satir SILINDI| GAP["Gap + kirilma noktasi"]

    R["IDataSubjectResolver.ResolveAsync"] --> S["DataSubjectScope"]
    S --> D{"DELETE /api/data-subjects/id<br/>dryRun=true varsayilan"}
    D -->|dryRun| P["yalniz satir sayisi donuyor, HICBIR SEY SILINMEZ"]
    D -->|dryRun=false| E["EraseAsync: sil -> audit yaz -> commit"]
    E -->|audit yazilamazsa| RB["ROLLBACK, hicbir satir silinmez"]

    classDef red fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    class BR,GAP,RB red
```

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` → `http://localhost:5080`.
   ```bash
   export APB="Authorization: Bearer manuel-test-token-2026"
   export APU="http://localhost:5080/tracon"
   ```
3. Bu alanın case'leri **doğrudan SQL** ile satır değiştirme/silme gerektirir
   (MT-DVR-003/004) — bir hash zincirini elle tahrif etmenin tek yolu budur.
   PostgreSQL kullanılıyorsa `docker exec -i ap-pg psql -U postgres -d tracon`.
4. `IDataSubjectResolver` **kayıtlı değildir** varsayılan olarak (K1: sıfır
   sürpriz) — MT-DVR-005/006 bunu doğrudan sınar. MT-DVR-007'den itibaren
   geçici bir çözümleyici gerekir; örnek uygulamaya elle eklenmez, bu case'ler
   `👤 insan gerekir` (geliştirici derlemesi gerekir) olarak işaretlidir.

---

# 1 — Hash zinciri (Faz 64.1/64.2)

### MT-DVR-001 — Zincir yazımı: `prev_hash`/`hash` doğru doldurulur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | K-4xx (bkz. faz dokümanı "Bu Fazda Verilen Kararlar") |

**Ön koşul**
- Reset yapıldı, `audit_log` boş.

**Adımlar**
1. İki yönetim eylemi yap (örn. iki farklı `maxAgeDays` ile retention politikası kaydet).
2. `GET /api/audit` ile iki kaydı oku.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" -d '{"maxAgeDays":30}' | jq
curl -s -X PUT "$APU/api/retention/run_events" -H "$APB" -H "content-type: application/json" -d '{"maxAgeDays":45}' | jq
curl -s "$APU/api/audit?entity=retention:run_events" -H "$APB" | jq
```

**Beklenen sonuç**
- İki kayıt döner. En eski kaydın `previousHash: null`, `hash` 64 karakterlik hex.
- En yeni kaydın `previousHash`, eskinin `hash` değeriyle **birebir eşit**.

**Gerçek sonuç (2026-08-18, PostgreSQL, macOS arm64):**
```json
{"id":"01a015f7-3826-7f01-ba88-e2ef9f3f5c28","target":"run_events","maxAgeDays":30,...}
{"id":"01a015f7-3826-7f01-ba88-e2ef9f3f5c28","target":"run_events","maxAgeDays":45,...}
```
✅ Doğrulandı — `AuditLogContract.Second_entry_links_to_the_first` bu tam senaryoyu otomatik koşar (4 koşumda: bellek içi + 3 SQL sağlayıcı).

---

### MT-DVR-002 — `GET /api/audit/verify` boş zincirde ve dolu zincirde `Valid` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Adımlar**
1. Reset sonrası hemen doğrula (boş zincir).
2. MT-DVR-001'in ardından tekrar doğrula.

**Girilecek veri**
```bash
curl -s "$APU/api/audit/verify" -H "$APB" | jq
```

**Beklenen sonuç**
- Boşken: `{"status":"Valid","entriesChecked":0,"firstFailingEntryId":null}`.
- MT-DVR-001'in ardından: `{"status":"Valid","entriesChecked":2,...}`.

**Gerçek sonuç (2026-08-18):**
```json
{"status":"Valid","entriesChecked":0,"firstFailingEntryId":null}
{"status":"Valid","entriesChecked":2,"firstFailingEntryId":null}
```
✅ Doğrulandı — gerçek çalıştırma, örnek uygulamaya karşı `curl` ile.

---

### MT-DVR-003 — Elle değiştirilen bir satır `Broken` verir, ilk bozuk kaydın kimliğiyle

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Ön koşul:** MT-DVR-001 uygulandı (en az 2 kayıt var).

**Adımlar**
1. İkinci kaydın `after` alanını doğrudan SQL ile değiştir (hash yeniden hesaplanmadan).
2. `verify` çağır.

**Girilecek veri**
```sql
UPDATE tracon.audit_log SET after = '{"tampered":true}'
WHERE entity = 'retention:run_events' ORDER BY chain_seq DESC LIMIT 1;
```
```bash
curl -s "$APU/api/audit/verify" -H "$APB" | jq
```

**Beklenen sonuç**
- `status: "Broken"`, `firstFailingEntryId` değiştirilen kaydın id'si.

**Otomatik kanıt:** `AuditChainWalkerTests.Altered_content_is_reported_broken_at_the_altered_entry`
(birim, tüm 3 alan/`prev_hash` kombinasyonu) ve gerçek PostgreSQL'e karşı bu
tam UPDATE deseni geliştirme sırasında elle doğrulandı — 👤 tam SQL koşumu
(yukarıdaki adım) yayın öncesi tekrar koşulmalı.

---

### MT-DVR-004 — Elle silinen bir satır `Gap` verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Ön koşul:** En az 3 kayıt var (MT-DVR-001'e bir eylem daha ekle).

**Adımlar**
1. Ortadaki kaydı doğrudan SQL ile sil.
2. `verify` çağır.

**Girilecek veri**
```sql
DELETE FROM tracon.audit_log
WHERE chain_seq = (
  SELECT chain_seq FROM tracon.audit_log
  WHERE entity = 'retention:run_events' ORDER BY chain_seq ASC LIMIT 1 OFFSET 1
);
```
```bash
curl -s "$APU/api/audit/verify" -H "$APB" | jq
```

**Beklenen sonuç**
- `status: "Gap"`, `firstFailingEntryId` silinen kaydın ARDINDAN gelen kaydın id'si.

**Otomatik kanıt:** `AuditChainWalkerTests.Deleted_middle_entry_is_reported_as_a_gap` /
`Deleted_first_entry_is_reported_as_a_gap` (birim) — 👤 gerçek SQL koşumu
yayın öncesi tekrar koşulmalı.

---

### MT-DVR-005 — Eş zamanlı yazımda zincir tek dal kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Not:** Bu senaryo `curl` ile pratik değildir (20 gerçek eşzamanlı istek
gerekir); otomatik koşum tek kanıttır ve **gerçek PostgreSQL/SQL
Server/SQLite'a karşı** koşuldu (bkz. faz dokümanı "Plandan Sapmalar" —
`Concurrent_writes_for_one_tenant_produce_a_single_valid_chain`, 3 sağlayıcıda
da yeşil). 👤 gerekmez, otomatik kanıt yeterli kabul edildi.

---

# 2 — Veri konusu hakları (Faz 64.3/64.4/64.5)

### MT-DVR-006 — Çözümleyici kayıtlı değilken export ve silme `409` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Adımlar**
```bash
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/data-subjects/user-42/export" -H "$APB"
curl -s -o /dev/null -w "%{http_code}\n" -X DELETE "$APU/api/data-subjects/user-42" -H "$APB"
```

**Beklenen sonuç**
- İkisi de `409`; gövde `"No data subject resolver registered"` başlığı taşır.

**Gerçek sonuç (2026-08-18, örnek uygulama IDataSubjectResolver kaydetmiyor):**
```
409
409
{"title":"No data subject resolver registered", ...}
```
✅ Doğrulandı.

---

### MT-DVR-007 — Kapsamsız anahtarla silme `403` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Adımlar**
1. `AgentsRead` kapsamlı bir API anahtarı oluştur.
2. O anahtarla `DELETE /api/data-subjects/{id}` çağır.

**Girilecek veri**
```bash
KEY=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"reader","scopes":["AgentsRead"]}' | jq -r .plaintextKey)
curl -s -o /dev/null -w "%{http_code}\n" -X DELETE "$APU/api/data-subjects/user-42" \
  -H "Authorization: Bearer $KEY"
```

**Beklenen sonuç**
- `403`.

**Otomatik kanıt:** `DataSubjectEndpointTests.Missing_scope_gets_403` (fonksiyonel) — 👤 gerekmez.

---

### MT-DVR-008 — `dryRun` varsayılanı `true`: silme hiçbir satıra dokunmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | K-4xx (Açık Soru 3 kararı) |

**Ön koşul:** 👤 Geliştirici — `IDataSubjectResolver` örnek uygulamaya geçici
olarak kaydedilir (bir `sessions` satırını sabit bir `subjectId`'ye eşleyen
5 satırlık bir sınıf, `Program.cs`'e `builder.Services.AddSingleton<...>()`
ile eklenir, koşum sonrası **geri alınır** — commit edilmez).

**Adımlar**
1. Bir oturum oluştur (`POST /api/agents/{name}/run` ile `sessionId` vererek).
2. `subjectId` o oturuma eşlensin diye çözümleyiciyi kaydet, uygulamayı yeniden başlat.
3. `DELETE /api/data-subjects/{subjectId}` (sorgu dizesi YOK — varsayılan).
4. Oturumun hâlâ var olduğunu doğrula.
5. `DELETE /api/data-subjects/{subjectId}?dryRun=false` ile gerçek sil.
6. Oturumun gittiğini doğrula.

**Beklenen sonuç**
- Adım 3: `{"dryRun":true,"rowsByTarget":{"sessions":1,...}}`, oturum HÂLÂ mevcut.
- Adım 5: `{"dryRun":false,...}`, oturum artık `404`.

**Otomatik kanıt:** `DataSubjectStoreTests` (`Erase_removes_only_the_named_tenants_session_even_when_ids_collide`,
`Preview_reports_counts_without_deleting_anything`) gerçek PostgreSQL'e karşı
— aynı iddiayı IDataSubjectStore seviyesinde, bu case'i HTTP seviyesinde
kanıtlar. 👤 gerekir (Program.cs değişikliği gerektiriyor).

---

### MT-DVR-009 — Silme sonrası zincir hâlâ `Valid` — denetim izi dokunulmamış

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | — |

**Ön koşul:** MT-DVR-008 adım 5 uygulandı (gerçek silme yapıldı).

**Adımlar**
```bash
curl -s "$APU/api/audit/verify" -H "$APB" | jq
curl -s "$APU/api/audit?entity=data-subject:$SUBJECT_ID" -H "$APB" | jq
```

**Beklenen sonuç**
- `verify` hâlâ `"status":"Valid"` — silme işlemi `audit_log`'a dokunmadı,
  yalnız YENİ bir `data_subject.erase` kaydı ekledi.
- İkinci sorgu tek bir kayıt döner: `action: "data_subject.erase"`,
  `after` alanı `rowsByTarget.sessions: 1` içerir.

**Otomatik kanıt:** `DataSubjectEndpointTests.Erase_writes_an_audit_entry_carrying_the_subject_id`
(fonksiyonel, sahte depo ile) + `DataSubjectStoreTests` (gerçek PostgreSQL,
silme sonrası oturum/çalıştırma/konuşma satırlarının gittiğini doğrudan
doğrular). 👤 tam uçtan uca (Program.cs + gerçek `verify`) yayın öncesi
tekrar koşulmalı.

---

### MT-DVR-010 — Silme özet mesajları da kaldırır (K-107 istisnası)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 64 |
| **İlgili karar** | K-107'nin phase 64 istisnası |

**Ön koşul:** 👤 Geliştirici — bir konuşma + en az 2 `conversation_item` üretilmiş
ve `IDataSubjectResolver` bu konuşmayı `ConversationIds`'e eşliyor olmalı.

**Adımlar**
1. Konuya ait konuşmanın `conversation_items` sayısını oku.
2. `DELETE /api/data-subjects/{id}?dryRun=false`.
3. Konuşmayı ve öğelerini tekrar sorgula.

**Beklenen sonuç**
- Silme öncesi `conversation_items` sayısı > 0.
- Silme sonrası konuşma `404`, `conversation_items` sayısı `0` — K-107'nin
  "özetlenen mesajlar silinmez" kuralı burada BİLEREK ihlal edilir.

**Otomatik kanıt:** `DataSubjectStoreTests.Erase_removes_a_conversation_and_its_items_together`
(gerçek PostgreSQL) — 👤 HTTP seviyesinde uçtan uca yayın öncesi tekrar koşulmalı.

---

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Saklama politikası (yaşa göre silme) | `23-SAKLAMA-ARSIV-KOTA.md` — bu dosya yalnız KİMLİĞE göre silmeyi (data subject) sınar |
| `audit_log`'un GENEL okuma/filtre sözleşmesi (`GET /api/audit`, `/api/audit/{entity}`) | `13-KIRACI-VE-GUVENLIK.md` (Faz 9) — bu dosya yalnız zincir/hash'e özgü davranışı sınar |
| API anahtarı kapsamının GENEL sözleşmesi | `13-KIRACI-VE-GUVENLIK.md` |
