# Faz 64 — Denetim Zinciri ve Veri Konusu Hakları

> **Durum:** ✅ Tamamlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-75**, **F-58**
> **Önkoşul:** [Faz 9](09-YONETISIM-VE-DENETIM-IZI.md) — denetim izi oradan gelir · [Faz 25](25-VERI-SAKLAMA-VE-ARSIVLEME.md) — yaşa göre temizlik makinesi ve `IRetentionStore` devralınır
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.Sql.Shared`, `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`, `Tracon.AspNetCore`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (`audit_log`'a iki sütun). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — `AuditEntry`'ye iki alan, bir yeni arayüz (`IDataSubjectResolver`), iki uç. `PublicAPI.Shipped.txt` bugün **boş**; ekleme **bugün bedava**
> **Site etkisi:** `concepts/governance.md`, `guides/production.md`, `reference/configuration.md`
> **Manuel test alanı:** `docs/manuel-test/28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz kurumsal alıcının iki sorusunu kapatır: **"geçmişi değiştirebilir misiniz?"** ve **"bir kişinin verisini silebilir misiniz?"** Bugün ikisinin de cevabı zayıftır. `audit_log` append-only **ruhla** yazılır ama teknik olarak değişmez **değildir**; veritabanına yazma yetkisi olan biri geçmişi sessizce düzenleyebilir.

## Bitiş Ölçütleri (DoD)

- [x] `audit_log` her kayıtta `prev_hash` ve `hash` taşır; ilk kayıt hariç zincir kesintisizdir — `AuditLogContract.First_entry_of_a_tenant_carries_no_previous_hash`/`Second_entry_links_to_the_first`, 4 koşumda
- [x] Elle değiştirilen bir satır `Broken`, elle silinen bir satır `Gap` üretir — `AuditChainWalkerTests` (birim, tüm kombinasyonlar) + gerçek PostgreSQL'e karşı elle doğrulandı (MT-DVR-003/004)
- [x] Eş zamanlı N yazımda zincir **tek dal** kalır (fonksiyonel test) — `AuditLogContract.Concurrent_writes_for_one_tenant_produce_a_single_valid_chain`, N=20, 4 koşumda (bellek içi + PostgreSQL + SQL Server + SQLite) — gerçek Postgres'e karşı bu test bir livelock yakaladı (bkz. Plandan Sapmalar, K-459/K-461), düzeltildikten sonra yeşil
- [x] Zincir yazımının maliyeti ölçüldü ve sayı belgeye yazıldı — 20 ardışık `PUT /api/retention/run_events` (her biri bir denetim yazımı tetikler) gerçek uygulamaya karşı toplam **0.373 sn**, istek başına ortalama **~18,6 ms** (HTTP + politika kaydı + zincir SELECT+INSERT dahil TAMAMI; salt zincir payı bunun küçük bir kesridir). Ayrı bir "denetim yazımı isteği yavaşlatıyor mu" eşiği plan tarafından istenmedi, ölçüm bilgi amaçlıdır.
- [x] İki kiracının zinciri birbirinden bağımsızdır (sözleşme testi, dört koşum) — `AuditLogContract.Two_tenants_chains_verify_independently`
- [x] `audit_log`'a konuşma içeriği yazılmadığı testle kapatıldı — `AuditContentPolicyTests` (kapalı liste, 22 dosya, iki yönlü denetim: yeni çağrı yeri VE bayatlamış liste satırı)
- [x] `IDataSubjectResolver` kayıtlı değilken silme ve dışa aktarım `409` döner — `DataSubjectEndpointTests` + gerçek uygulamaya karşı `curl` (MT-DVR-006)
- [x] `dryRun` hiçbir satıra dokunmaz; sayıları doğru döner — `DataSubjectStoreTests.Preview_reports_counts_without_deleting_anything` (gerçek PostgreSQL) + `DataSubjectEndpointTests.Erase_without_dryRun_previews_and_deletes_nothing`
- [x] Gerçek silme oturum, çalıştırma, konuşma (hem Conversations API hem SIRADAN agent oturumu sohbet geçmişi — `SessionConversationResolver`, denetim bulgusu #1'in düzeltmesi), ek, puan ve ses verisini kaldırır; **özet mesajlar dahil** — `DataSubjectStoreTests`/`DataSubjectChatHistoryTests` üç sağlayıcının hepsinde gerçek koşuldu (18/18 yeşil). 🚨 **Sapma:** "gömü verisi" DoD kapsamından çıkarıldı — `document_embeddings` şemasında session/run/conversation'a bağlayan sütun yok (K-458, bkz. Plandan Sapmalar)
- [x] Silmeden sonra zincir doğrulaması hâlâ `Valid` — denetim izi dokunulmadı — `DataSubjectEndpointTests.Erase_writes_an_audit_entry_carrying_the_subject_id` + tasarım gereği (`SqlDataSubjectStore` hiçbir `audit_log` sorgusu çalıştırmaz)
- [x] Silme denetim izine yazılır; yazma hatası isteği düşürür (K-370) — `DataSubjectEndpointTests.Erase_fails_loudly_when_the_audit_write_fails` + `DataSubjectStoreTests.Erase_rolls_back_every_delete_when_the_commit_callback_throws`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format` hepsi yeşil, denetim sonrası düzeltmelerle birlikte son koşum: Core.UnitTests 868, PostgreSql.IntegrationTests 1007, SqlServer.IntegrationTests 508, Sqlite.IntegrationTests 522, AspNetCore.FunctionalTests 515
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. yukarıdaki maliyet ölçümü ve aşağıdaki doğrulama komutları çıktıları
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md` içine eklendi (10 case); otomatikleştirilebilenler koşuldu (MT-DVR-001/002/006/007 gerçek uygulamaya karşı bu oturumda koşuldu ve gerçek sonuç yazıldı; kalan 6 case 👤 gerekir olarak işaretli, bkz. Plandan Sapmalar)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi (`concepts/governance.md`, `guides/production.md`, `reference/configuration.md`, `getting-started/persistence.md`); `npm run build` + `check-links.mjs` temiz (927 sayfa, 114337 iç referans, hiç kırık yok)

### Doğrulama komutları (gerçekleşen çıktı, 2026-08-18, PostgreSQL, `samples/Tracon.Api`)

```bash
$ curl -s -H "$APB" http://localhost:5080/tracon/api/audit/verify
{"status":"Valid","entriesChecked":0,"firstFailingEntryId":null}

$ curl -s -H "$APB" -X PUT .../api/retention/run_events -d '{"maxAgeDays":30}'
{"id":"01a015f7-...","target":"run_events","maxAgeDays":30, ...}
$ curl -s -H "$APB" -X PUT .../api/retention/run_events -d '{"maxAgeDays":45}'
{"id":"01a015f7-...","target":"run_events","maxAgeDays":45, ...}

$ curl -s -H "$APB" http://localhost:5080/tracon/api/audit/verify
{"status":"Valid","entriesChecked":2,"firstFailingEntryId":null}

$ curl -s -H "$APB" http://localhost:5080/tracon/api/data-subjects/user-42/export
{"title":"No data subject resolver registered", "status":409, ...}

$ curl -s -H "$APB" -X DELETE http://localhost:5080/tracon/api/data-subjects/user-42
{"title":"No data subject resolver registered", "status":409, ...}
```

`IDataSubjectResolver` kayıtlı olduğu (gerçek erasure) senaryo
`DataSubjectStoreTests` ve `DataSubjectEndpointTests` ile gerçek/sahte depoya
karşı otomatik koşuldu; örnek uygulamaya geçici bir çözümleyici ekleyerek
uçtan uca insan koşumu MT-DVR-008/009/010'da 👤 olarak işaretlidir.

---

## Plandan Sapmalar

- **`DataSubjectScope`'a plandaki taslağın öngörmediği üçüncü bir liste
  (`ConversationIds`) eklendi (K-457).** Ölçüldü: `conversations` tablosunun
  `session_id` sütunu yok; yalnız `SessionIds`/`RunIds` ile konuşma verisine hiç
  ulaşılamıyordu. Plan "şema bunları zaten oturuma bağlar" varsayıyordu — yanlıştı.
- **`DocumentEmbeddings` (bilgi tabanı gömüleri) veri konusu silme/dışa aktarım
  kapsamının DIŞINDA bırakıldı (K-458).** Ölçüldü: `document_embeddings`
  şemasında session/run/conversation'a bağlayan hiçbir sütun yok — içerik idari
  yüklenen bilgi tabanıdır, bir kullanıcının verisi değil. DoD'daki "gömü verisi"
  ifadesi bu yüzden gerçekleşen kapsamdan çıkarıldı; bkz. aşağıdaki DoD notu.
- **Denetim zinciri "son satır" sorgusu plandan FARKLI bir sütunla çözüldü
  (`chain_seq`, K-459).** Plan `prev_hash`/`hash` dışında üçüncü bir sütun
  öngörmüyordu. Gerçek PostgreSQL'e karşı 20 eşzamanlı yazıcı testi, `id`
  (uuid v7) ile sıralamanın YANLIŞ olduğunu kanıtladı (alt bitler rastgele) —
  yazıcılar hiç yakınsamadı. Üç sağlayıcıda üç farklı uygulama gerekti (Postgres
  `GENERATED AS IDENTITY`, SQL Server `SEQUENCE` + `DEFAULT NEXT VALUE FOR`,
  SQLite yerleşik `rowid`).
- **`audit_log.before`/`after` PostgreSQL'de `jsonb`'den `json`'a çevrildi
  (K-460), plan bunu öngörmüyordu.** Gerçek Postgres'e karşı ölçüldü: `jsonb`
  yazılan metni bayt-bayt korumuyor, hash zinciri her `before`/`after` taşıyan
  kaydı `Broken` olarak yanlış raporluyordu.
- **Eşzamanlı yazım kilidi planın Açık Soru 1'inde önerilenden (advisory lock)
  FARKLI: benzersiz dizin + yeniden deneme + rastgele gecikme (K-461).**
  Advisory/oturum kilidi hiç denenmedi — K-284 emsali baştan bu yönü elemişti.
  Rastgele gecikme (jitter) planda hiç yoktu; gerçek ölçüm olmadan eklenmeyecek
  bir detaydı, 20 yazıcılı test onsuz asla yakınsamadığını gösterdi.
- **`IDataSubjectStore.EraseAsync`'in imzası plandakinden farklı: bir
  `beforeCommitAsync` geri çağrısı taşır.** Plan yalnız "silme denetim izine
  yazılır, K-370" diyordu, mekanizmayı belirtmiyordu. Aynı DELETE'lerin bir
  transaction içinde çalışıp `dryRun`'da her zaman `ROLLBACK`, gerçek silmede
  yalnız denetim yazımı (geri çağrı) başarılıysa `COMMIT` edilmesi (K-462) hem
  K-370'i hem "preview ile gerçek silme aynı sorgudan gelir" DRY ilkesini
  karşılıyor.
- **`SqlRetentionStore`'un özel `RowToJson`/`WriteValue` metotları
  `SqlJsonRowWriter` (Sql.Shared) adıyla paylaşılan bir yardımcıya çıkarıldı.**
  Planda yoktu; veri konusu dışa aktarımının AYNI "şemayı bilmeden JSON'a çevir"
  ihtiyacını taşıdığı ölçülünce, ikinci bir kopya yazmak yerine mevcut kod
  paylaşılan bir dosyaya taşındı (davranış değişmedi, `SqlRetentionStore` testleri
  aynı kaldı).
- **`SessionConversationResolver` (Core, yeni public tip) plan dışıydı — bağımsız
  denetimin bulduğu 🔴 #1'in düzeltmesi.** Plan `DataSubjectScope.SessionIds`'in
  tek başına yeterli olduğunu varsayıyordu; ölçüldü: sıradan bir `AgentSession`
  sohbet geçmişinin dahili `conversation_id`'si `sessions.state` içine gömülü,
  bir tüketici resolver'ının bilebileceği bir şey değil. Çözüm
  `ConversationBranchService`'in İZLEDİĞİ AYNI yöntemi (agent üzerinden oturumu
  geri yükle, `ChatHistoryState`'i oku) yeniden kullanır; `DataSubjectEndpoints`
  artık `resolver.ResolveAsync` sonrasında bu adımı otomatik ekliyor — tüketici
  hiçbir ek kod yazmaz.
- **`DataSubjectTargetRegistry`'deki `ArrayContains` çağrıları BARE sütun adından
  TAM NİTELİKLİ (`{table}.column`) adlandırmaya geçirildi — gerçek bir SQLite
  kusuru.** `json_each()`'in kendi `id` sütunu, dış tablonun `id`'sini
  gölgeliyordu (K-464); yalnız SQLite'a taşınan testler bunu yakaladı.
- **`SqliteDialect.AddUuidArray` artık BÜYÜK harf metin üretiyor, `System.Text.Json`'ın
  varsayılanı (küçük harf) değil — gerçek bir SQLite kusuru.** Bu dialect'in TÜM
  diğer Guid bağlamaları zaten büyük harf yazıyordu (K-191); tutarsızlık `a`-`f`
  içeren id'lerde SESSİZCE eşleşmiyor ve RASTGELE üretilen id'ye bağlı olarak
  kesikli (flaky) başarısızlık üretiyordu (K-465).
- **Manuel kabul case'lerinin çoğu (`MT-DVR-003/004/005/008/009/010`) 👤 gerekir
  işaretiyle kapatıldı, tam uçtan uca koşulmadı.** Zincir tahrifi (`MT-DVR-003/004`)
  ve veri konusu silme (`MT-DVR-008/009/010`) senaryoları ya doğrudan SQL ile
  satır bozma ya da örnek uygulamaya geçici bir `IDataSubjectResolver` eklenmesini
  gerektiriyor — ikisi de otomatik testlerle (Postgres'e karşı gerçek koşum dahil)
  kanıtlandı, ama HTTP uçtan uca insan koşumu yayın öncesi hâlâ gereklidir.

## Bu Fazda Verilen Kararlar

K-456 – K-465. Tam metin ve gerekçe `docs/KARARLAR.md`'de:

- **K-456** — Denetim izi veri konusu silmesinin kapsamı dışındadır (kullanıcı kararı, plan zaten belirtiyordu — burada resmî K numarası aldı ve `AuditContentPolicyTests` ile kapatıldı).
- **K-457** — `DataSubjectScope`'a `ConversationIds` eklendi.
- **K-458** — `DocumentEmbeddings` veri konusu kapsamı dışında.
- **K-459** — Zincir "son satır" sorgusu `chain_seq` ile bulunur, `(created_at, id)` ile değil.
- **K-460** — `audit_log.before`/`after` PostgreSQL'de `json`'a çevrildi.
- **K-461** — Eşzamanlı yazım benzersiz dizin + yeniden deneme + jitter ile çözülür.
- **K-462** — Veri konusu önizleme/silme aynı transaction'ı rollback/commit ile ayırır.
- **K-463** — `SessionConversationResolver` sıradan oturum sohbet geçmişini çözer (bağımsız denetim 🔴 #1).
- **K-464** — `DataSubjectTargetRegistry`'de `ArrayContains` sütunları her zaman tam nitelikli (SQLite `json_each.id` gölgelemesi).
- **K-465** — `SqliteDialect.AddUuidArray` büyük harf metin üretir (K-191 ile tutarlılık).

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi 4 bulgu buldu (2× 🔴, 2× 🟡). Hepsi **düzeltildi**;
🔴 bulgu kalmadı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | Normal `AgentSession` sohbet geçmişi (`SqlChatHistoryProvider`, `conversation_items`) `DataSubjectScope.SessionIds` ile hiç ulaşılamıyordu — dahili `conversation_id` bir oturumun `state` alanı içine gömülüydü, tüketici resolver'ı bunu bilemezdi. | **Düzeltildi.** Yeni `SessionConversationResolver` (Core) — `ConversationBranchService`'in izlediği AYNI yöntemle (agent üzerinden oturumu geri yükle, `ChatHistoryState`'i oku) — session'ların dahili konuşmasını çözer; `DataSubjectEndpoints` bunu her iki uçta da `resolver.ResolveAsync` sonrasına ekliyor. 3 yeni entegrasyon testiyle (gerçek agent çalıştırması + gerçek PostgreSQL) kanıtlandı. |
| 2 | 🔴 | Elle değiştirme→`Broken`/elle silme→`Gap` iddiası yalnız sentetik birim testleriyle (`AuditChainWalkerTests`) kanıtlanıyordu; planın istediği "dört koşumda birden sözleşme testi" (gerçek SQL üzerinden) yoktu. | **Düzeltildi.** `AuditLogContract`'a `SupportsRawTamper`/`ExecuteRawAsync`/`QualifiedAuditLogTable` kancaları eklendi; üç SQL sağlayıcısı ham `UPDATE`/`DELETE` ile gerçek satırı bozup `VerifyChainAsync`'i doğruluyor (`Tampering_a_row_directly_is_detected_as_broken`, `Deleting_a_row_directly_is_detected_as_a_gap`) — 4 koşumda (bellek içi atlanır, 3 SQL sağlayıcı gerçek koşar). |
| 3 | 🟡 | `DataSubjectStoreTests` yalnız PostgreSQL'de vardı; plan kiracı izolasyonunu "dört koşumda birden" istiyordu. | **Düzeltildi** — SqlServer ve SQLite'a aynı 6 senaryo taşındı. Taşıma sırasında SQLite'a özgü İKİ GERÇEK ÜRETİM KUSURU bulundu ve düzeltildi (aşağıda). |
| 4 | 🟡 | `AuditContentPolicyTests`'in regex'i `\bauditLog\.WriteAsync` — `_` ile başlayan bir alan adında (`_auditLog.WriteAsync(`) `\b` hiç oluşmadığı için kaçıyordu. | **Düzeltildi.** Sınır kaldırıldı, `[Aa]udit[Ll]og\.WriteAsync` ile eşleşme genişletildi — yanlış pozitif (fazladan dosya incelemesi) kabul edilebilir, yanlış negatif değil. |
| 🟢 | — | Bilinmeyen/boş `subjectId` senaryosu test edilmemişti; DoD'nin "gömü verisi" satırı K-458 ile çelişiyordu. | **Düzeltildi.** `Unknown_subjectId_resolves_to_an_empty_scope_and_erases_nothing` eklendi; DoD satırı K-458'i yansıtacak şekilde güncellendi. |

**Bulgu #3'ün taşınması sırasında bulunan iki gerçek kusur** (ikisi de yalnız SQLite'ta, ikisi de gerçek testle yakalandı):

- `json_each()`'in kendi `id` sütunu, `EXISTS (... WHERE value = id)` içindeki BARE `id` referansını GÖLGELİYORDU — `DataSubjectTargetRegistry`'deki her `ArrayContains` çağrısı artık tam nitelikli sütun adı alıyor (`{table}.id`). Bkz. K-464.
- `SqliteDialect.AddUuidArray`, `System.Text.Json`'ın VARSAYILAN (küçük harf) Guid biçimini kullanıyordu; bu dialect'in TÜM diğer Guid bağlamaları (K-191) BÜYÜK harf yazıyor — uyumsuzluk, `a`-`f` içeren id'lerde SESSİZCE eşleşmiyordu (rastgele üretilen id'ye göre KESİKLİ/flaky başarısızlık). Bkz. K-465.

## Sonraki Faza Devir Notu

- **Devralınan sözleşmeler:** `IAuditLog.VerifyChainAsync`, `IDataSubjectResolver`/
  `IDataSubjectStore` artık kararlı public API'dir. Yeni bir tablo/hedef eklenirse
  ve o hedef bir kullanıcının verisini taşıyorsa, `DataSubjectTargetRegistry`'ye
  (Sql.Shared) eklenip eklenmeyeceği düşünülmelidir — Faz 25'in "yeni tablo ekleyen
  her faz saklama hedef listesine kendi tablosunu eklemekle yükümlüdür" kuralının
  veri konusu ekseni karşılığıdır (henüz `faz-tamamlama` skill'ine eklenmedi, bu
  fazın kendi kapsamı dışında bırakıldı — bir sonraki oturumun ele alması gerekir).
- **🚨 Bir "zincirin son halkası" veya "en son yazılan satır" sorgusu asla
  `ORDER BY created_at, id` (veya benzeri zaman damgası + uuid v7 tie-break) ile
  YAZILMAZ.** uuid v7'nin alt bitleri rastgeledir; gerçek eşzamanlı yazım altında
  yanlış "son" satırı seçer ve YAKINSAMAYAN bir yarışa yol açar (K-459). Gerçek bir
  sıra gerekiyorsa veritabanının kendi atadığı bir sayaç (`IDENTITY`/`SERIAL`/
  `rowid`) kullanılır.
- **🚨 `jsonb` (PostgreSQL), yazılan metnin AYNEN geri okunmasını gerektiren HİÇBİR
  alanda kullanılmaz** (hash, imza, checksum, bir dış sistemle bayt-bayt eşleşmesi
  gereken içerik). K-027'nin listesi artık beş: `sessions.state`,
  `conversation_items.item`, `run_inputs.messages`, `workflow_checkpoints.state`,
  `audit_log.before/after`.
- **🚨 SQL Server'da var olan bir tabloya `IDENTITY` sütunu EKLENEMEZ.** `SEQUENCE`
  + `DEFAULT NEXT VALUE FOR` deseni kullanılır (bkz. `0018_audit_chain.sql`); test
  altyapısında şema silme sırası TABLOLAR → `SEQUENCE`'lar → `DROP SCHEMA` olmalı,
  aksi hâlde "Cannot drop schema because it is being referenced" hatası alınır.
- **Yarım kalanlar:**
  - Manuel kabul case'lerinin 6'sı (`MT-DVR-003/004/005/008/009/010`) 👤 gerekir
    işaretiyle kapatıldı — otomatik testlerle kanıtlandı ama tam HTTP uçtan uca
    insan koşumu yayın öncesi (`manuel-test-kosumu` turunda) hâlâ gereklidir.
  - Veri konusu dışa aktarımı yalnız session/run/conversation-bağlı içeriği
    kapsıyor; `run_events`/`tool_invocations` (operasyonel telemetri) bilinçli
    olarak dışarıda bırakıldı — GDPR "erişim hakkı" açısından tartışmalı bir sınır,
    talep gelirse ayrı bir karar gerektirir.
  - Dışa aktarım biçimi tek bir JSON belgesidir (Açık Soru 4, seçenek A); ekler
    yalnız METADATA taşır, dosya baytları hiç yok. Bir zip/dosya-demeti biçimi
    ayrı, ölçülmemiş bir iş kalemidir.
  - `DocumentEmbeddings` veri konusu kapsamının dışında kaldı (K-458) — bir gömü
    üretim akışı doğrudan bir data subject'e bağlanacak şekilde tasarlanırsa
    yeniden değerlendirilmelidir.
- **Sıradaki faz:** `docs/ADAYLAR.md`'de seçilmemiş adaylar arasından `faz-planlama`
  ile belirlenir; bu faz kapanışı belirli bir "Faz 65" dokümanı hazırlamadı.
