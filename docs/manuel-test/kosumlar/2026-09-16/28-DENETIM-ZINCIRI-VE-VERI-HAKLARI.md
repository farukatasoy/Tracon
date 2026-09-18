# 28 — Denetim Zinciri ve Veri Konusu Hakları (`DVR`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md`](../../28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır. Spec dosyası kendi
> içinde MT-DVR-001/002/006 için 2026-08-18 tarihli, geliştirme sırasında
> alınmış bir "Gerçek sonuç" bloğu TAŞIYOR (istisnai — diğer 34 dosyada bu
> yok); bu koşum kaydı bunların yerine geçmez, **bu turun kendi** ölçümüdür.
>
> spec `### MT-DVR-NNN` (h3) kullanır, burada skill §4.1/§7 konvansiyonuna
> uymak için `## MT-DVR-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, sekizinci aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `392f50a3` donuk (doğrulandı: `git status --short` boş, `git diff --stat 7e3a4de7..HEAD -- src samples tests` boş) |
| **Case sayısı** | 10 (MT-DVR-001..010) |
| **Ana uygulama** | port 5084, şema `mt_s4` |

## 🚨 Sapma — `samples/Tracon.Api/Program.cs` HİÇ değiştirilmedi

MT-DVR-008/009/010'ın kendi Ön koşulu bir `IDataSubjectResolver`'ın
`Program.cs`'e **geçici** eklenmesini istiyor (`👤 Geliştirici`). Bunun
yerine izole bir `TraconTestHost` betiği (`~/tracon-manuel/dvr-scratch-s4/`,
`Tracon.Testing` + `Tracon.PostgreSql` 0.0.0-preview.0.829) kullanıldı —
gerçek `UsePostgreSql()` ile, **paylaşılan `mt_s4`/`tracon` şemalarına
DOKUNMADAN** kendi geçici `mt_s4_dvr_scratch` şemasını açtı, case'i koştu,
sonra şemayı `DROP SCHEMA ... CASCADE` ile sildi (skill §1.3: yalnız kendi
şeman düşürülür — bu **yeni açılmış, yalnız bu koşuma ait** bir şema,
`mt_s4`'ün kendisi değil, hiç dokunulmadı).

MT-DVR-003/004 (SQL tahrifi) da aynı gerekçeyle şema seçiminde ayrıştı:
003 (`UPDATE`) doğrudan `mt_s4.audit_log`'a (paylaşılan, kalıcı zincir)
uygulandı — bu SPEC'İN KENDİ istediği senaryo (zincirin kalıcı olarak
`Broken` kalması beklenen/kabul edilen bir yan etkidir, gerçek üretimde de
böyle "kanıt" kalır). 004 (`DELETE`) ise 003'ün AYNI paylaşılan zincirde
zaten `Broken` bıraktığı durumun 004'ün "Gap" iddiasını GÖLGELEYECEĞİNİ (ilk
hata her zaman en eskisini raporlar) fark edince, kendi `mt_s4_dvr_scratch`
şemasında (bkz. yukarı) temiz bir 3-kayıtlık zincirle ayrı koşuldu.

**Spec'in kendi SQL örneği çalışmıyor:** MT-DVR-003'ün `UPDATE ... ORDER BY
... LIMIT 1` sözdizimi PostgreSQL'de geçersiz (`UPDATE` `ORDER BY`/`LIMIT`
desteklemez) — `ERROR: syntax error at or near "ORDER"`. Önce `SELECT ... id
... ORDER BY ... LIMIT 1` ile hedef `id` bulunup `UPDATE ... WHERE id = '...'`
ile tamamlandı; doküman kusuru, ürün kusuru değil.

## Sapma — MT-DVR-010 kısmen kanıtlandı, tam HTTP ucu farklı bir yol izledi

`DataSubjectScope.ConversationIds`'i gerçek bir OpenAI-uyumlu `/v1/conversations`
nesnesiyle doldurmak (spec'in varsaydığı gerçek yol) bu ailenin kapsamı
dışında ayrı bir API yüzeyi (Responses/Conversations) gerektiriyordu.
Bunun yerine oturumun kendi zincirleme (`SqlChatHistoryProvider`) ürettiği
`conversationId` denendi: `rowsByTarget.conversations: 1` erase sonrası
raporlandı, ama `GET /v1/conversations/{id}` erase SONRASI hâlâ `200`
döndürdü (`conversation_items` her iki tarafta da boştu — bu oturum-tabanlı
konuşma nesnesi item doldurmuyor). Bu **çelişkili gözlem bir kusur olarak
RAPORLANMADI** — test kurulumu spec'in kastettiği gerçek yoldan (açık
`/v1/conversations` + item'lar) farklı bir iç mekanizmayı (oturum-tabanlı
sohbet geçmişi) kullandığı için, "conversations" sayacının hangi nesneye
atıfta bulunduğu bu kurulumda belirsiz — yanlış pozitif riski gerçek/asıl
yoldan daha yüksek. MT-DVR-008/009 aynı kurulumla (`sessions`) TAM VE TUTARLI
biçimde kanıtlandı; 010'un kendi iddiası `DataSubjectStoreTests.
Erase_removes_a_conversation_and_its_items_together`nin (gerçek PostgreSQL,
bu oturumda taze koşuldu) üstünde bırakıldı.

---

## MT-DVR-001 — Zincir yazımı: `prev_hash`/`hash` doğru doldurulur

**Gerçek sonuç**
Ana uygulama UZUN SÜREDİR çalışıyor (bu turun diğer ailelerinden 48 önceki
denetim kaydı birikmiş) — spec'in "audit_log boş" ön koşulu bu paylaşılan
örnekte geçerli değil (destructive bir sıfırlama diğer kapanmış ailelerin
kanıtlarını riske atacağından yapılmadı). İki `PUT /api/retention/run_events`
(30 sonra 45) çağrıldı: `GET /api/audit?entity=retention:run_events` iki
kayıt döndürdü, en yeninin `previousHash`'i eskinin `hash`'iyle **birebir
eşit**. "En eski kaydın `previousHash: null`" iddiası bu FİLTRELENMİŞ
görünümde değil, zincirin GERÇEK kökünde (`chain_seq=1`) doğrulandı: `SELECT
... FROM mt_s4.audit_log WHERE chain_seq=1` → `prev_hash IS NULL: true`.
Her `hash` tam **64** hex karakter (`length(hash)` ile doğrulandı — JSON'da
görsel olarak 65 gibi göründü, SQL kesin saydı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-002 — `GET /api/audit/verify` boş zincirde ve dolu zincirde `Valid` döner

**Gerçek sonuç**
Dolu zincir: `{"status":"Valid","entriesChecked":49,"firstFailingEntryId":null}`.
"Boş zincir" alt-iddiası bu paylaşılan/uzun-süredir-çalışan uygulamada
yıkıcı bir sıfırlama olmadan test edilemedi (yöntem notu — MT-DVR-001'le
aynı gerekçe); `AuditLogContract` sözleşme testleri (`InMemoryStoreContractTests`
dahil) boş zincir durumunu ayrıca kapsıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-003 — Elle değiştirilen bir satır `Broken` verir, ilk bozuk kaydın kimliğiyle

**Gerçek sonuç**
`mt_s4.audit_log`'da MT-DVR-001'in en yeni kaydının (`id:
01a0b323-5ca0-742d-b2a9-e5a837eaaa7f`) `after` alanı doğrudan SQL ile
`{"tampered":true}`'ya değiştirildi (hash yeniden hesaplanmadan). `GET
/api/audit/verify`: `{"status":"Broken","entriesChecked":49,
"firstFailingEntryId":"01a0b323-5ca0-742d-b2a9-e5a837eaaa7f"}` —
`firstFailingEntryId` değiştirilen kaydın id'siyle **birebir eşit**.
**Kalıcı yan etki:** bu tahrifat `mt_s4.audit_log` zincirini KALICI olarak
`Broken` bırakır (spec'in kendi tasarladığı, geri alınamaz bir kanıt
mekanizması — MT-DVR-002'nin ardından koşulan başka hiçbir case artık bu
şemada `Valid` bir zincir GÖREMEZ; bu turda MT-DVR-003'ten sonra `verify`ye
bakan tek case yoktu, ama gelecekteki bir oturum bunu bilmeli).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-004 — Elle silinen bir satır `Gap` verir

**Gerçek sonuç**
MT-DVR-003'ün `mt_s4`'ü kalıcı `Broken` bıraktığı fark edildiği için (Gap
iddiası aksi hâlde hiç gözlenemezdi — walker ilk hatayı, en eski olanı,
raporlar), ayrı bir `mt_s4_dvr_scratch` şemasında temiz bir zincir kuruldu:
izole `TraconTestHost` ile 3× `PUT /api/retention/run_events` (10→20→30),
`chain_seq` sırası SQL ile doğrulandı (1,2,3), **ortadaki** (`chain_seq=2`)
kayıt `DELETE FROM ... WHERE chain_seq=2` ile silindi. Aynı host'un
`GET /api/audit/verify`'ı: `{"status":"Gap","entriesChecked":2,
"firstFailingEntryId":"01a0b327-07c8-...-0398cb8bc"}` — bu id silinenin
**ARDINDAN gelen** (`chain_seq=3`) kaydın id'si, tam beklendiği gibi.
Scratch şema koşum sonunda `DROP SCHEMA ... CASCADE` ile silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-005 — Eş zamanlı yazımda zincir tek dal kalır

**Gerçek sonuç**
Spec'in kendisi `curl`'ün pratik olmadığını, otomatik kanıtın yeterli
kabul edildiğini söylüyor. `Concurrent_writes_for_one_tenant_produce_a_single_valid_chain`
bu oturumda TAZE koşuldu, üç sağlayıcının hepsinde yeşil: PostgreSQL
(1/1, kendi testcontainer'ı), SQL Server (1/1, kendi testcontainer'ı),
SQLite (1/1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-006 — Çözümleyici kayıtlı değilken export ve silme `409` döner

**Gerçek sonuç**
Ana uygulama (`IDataSubjectResolver` kayıtlı DEĞİL — varsayılan): `GET
/api/data-subjects/user-42/export` → `409`; `DELETE
/api/data-subjects/user-42` → `409`. Gövde: `{"title":"No data subject
resolver registered","detail":"IDataSubjectResolver is not
registered...register an IDataSubjectResolver implementation to use this
endpoint."}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-007 — Kapsamsız anahtarla silme `403` döner

**Gerçek sonuç**
`AgentsRead` kapsamlı bir API anahtarı üretildi (`POST /api/api-keys`),
o anahtarla `DELETE /api/data-subjects/user-42`: `403`. Anahtar hemen
iptal edildi (`DELETE /api/api-keys/{id}` → `204`). Otomatik kanıt da
taze koşuldu: `DataSubjectEndpointTests` (fonksiyonel) **8/8** yeşil,
`Missing_scope_gets_403` dahil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-008 — `dryRun` varsayılanı `true`: silme hiçbir satıra dokunmaz

**Gerçek sonuç**
İzole `TraconTestHost` (`mt_s4_dvr_scratch` şeması, gerçek PostgreSQL),
`subjectId → sessionId` eşleyen 5 satırlık bir `IDataSubjectResolver`
(`ConfigureServices` — `AddTracon()`'dan ÖNCE, `services.AddSingleton`)
kaydedildi. Adımlar:
1. `dvr-agent` (fake sağlayıcı) `sessionId: mt-dvr-008-session` ile
   çalıştırıldı — oturum oluştu.
2. `GET /api/sessions/mt-dvr-008-session`: `200`, mesajlar dolu.
3. `DELETE /api/data-subjects/mt-dvr-008-subject` (sorgu dizesi YOK):
   `{"dryRun":true,"rowsByTarget":{"sessions":1,"conversations":1,
   "runs":1,...}}`.
4. `GET /api/sessions/mt-dvr-008-session`: **hâlâ `200`** — dry run hiçbir
   satıra dokunmadı.
5. `DELETE ...?dryRun=false`: `{"dryRun":false,"rowsByTarget":{"sessions":1,...}}`.
6. `GET /api/sessions/mt-dvr-008-session`: **`404` "Session not found"**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-009 — Silme sonrası zincir hâlâ `Valid` — denetim izi dokunulmamış

**Gerçek sonuç**
MT-DVR-008'in AYNI host'unda (adım 5'in hemen ardından):
- `GET /api/audit/verify`: `{"status":"Valid","entriesChecked":1,
  "firstFailingEntryId":null}` — gerçek silme `audit_log`'u bozmadı.
- `GET /api/audit?entity=data-subject:mt-dvr-008-subject`: **tek** kayıt,
  `action: "data_subject.erase"`, `after`: `{"subjectId":
  "mt-dvr-008-subject","rowsByTarget":{"sessions":1,"conversations":1,
  "runs":1,...}}` — `rowsByTarget.sessions: 1` beklenen alanı birebir var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DVR-010 — Silme özet mesajları da kaldırır (K-107 istisnası)

**Gerçek sonuç**
Yukarıdaki sapma notunda açıklandığı gibi, oturum-tabanlı sohbet
geçmişinin ürettiği `conversationId` ile denendi: `rowsByTarget.
conversations: 1` erase sonrası raporlandı, ama bu nesnenin
`/v1/conversations/{id}` üzerinden hâlâ `200` dönmesi ve item listesinin
zaten (erase ÖNCESİ de) boş olması, bu iç mekanizmanın spec'in kastettiği
gerçek konuşma nesnesiyle BİREBİR aynı olmadığını gösteriyor — belirsiz bir
kısmi ölçüm, kusur olarak raporlanmadı (yukarı bakın). Asıl iddia
(silme sonrası konuşma 404, `conversation_items` sayısı 0) `DataSubjectStoreTests.
Erase_removes_a_conversation_and_its_items_together` ile (gerçek
PostgreSQL, bu oturumda taze koşuldu, **6/6** `DataSubjectStoreTests`
içinde yeşil) kanıtlı kabul edildi; HTTP seviyesinde gerçek
`/v1/conversations` akışıyla tam yeniden üretim bu ailenin kapsamı
dışında kaldı (bkz. sapma notu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Sayım

10/10 case koşuldu ve Geçti. Sıfır Kaldı, sıfır Beklemede, sıfır fiziksel
eylem gerektiren case. Yeni ürün kusuru (`HATA-S4-*`) bulunmadı.

## Devir notu

- Ana uygulama (port 5084, şema `mt_s4`) dokunulmadan bırakıldı — bu ailede
  hiçbir restart gerekmedi (`echo`/`Preflight`/`Tenancy` gibi env
  değişikliği yok).
- **Kalıcı yan etki, bilinçli:** `mt_s4.audit_log`'un zinciri MT-DVR-003'ün
  SQL tahrifinden beri **kalıcı olarak `Broken`**
  (`firstFailingEntryId: 01a0b323-5ca0-742d-b2a9-e5a837eaaa7f`). Bu,
  case'in KENDİ tasarladığı, geri alınmayan bir kanıt bırakma mekanizmasıdır
  (spec MT-DVR-003 için bir "geri al" adımı TANIMLAMIYOR). `GET
  /api/audit/verify` çağıran gelecekteki herhangi bir case/aile bunu
  bilmeli — ürün kusuru DEĞİL, bu turun kasıtlı bir izi.
- Geçici kaynaklar temizlendi: `mt_s4_dvr_scratch` şeması iki kez açılıp
  (004 ve 008/009/010 için) her seferinde `DROP SCHEMA ... CASCADE` ile
  silindi; MT-DVR-007'nin geçici API anahtarı iptal edildi.
- **Sıradaki:** `06-SAGLAYICI-DIGER.md` (45 case; Azure alt-kalemleri bu
  ortamda kimlik bilgisi olmadığı için `⏭ Atlandı` işaretlenecek — DEVIR.md
  §'sinde zaten kararlaştırılmış), henüz açılmadı. Bu, ap-s4'ün atanmış
  son ailesi (31·16·30·22·09·27·26·28·06 listesinin tamamı).
