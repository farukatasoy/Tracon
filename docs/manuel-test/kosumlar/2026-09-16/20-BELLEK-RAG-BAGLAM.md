# 20 — Bağlam Sıkıştırma, Bellek Sağlayıcıları ve Anlamsal Arama (`MEM`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../20-BELLEK-RAG-BAGLAM.md`](../../20-BELLEK-RAG-BAGLAM.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (şerit ap-s3, aile 17'nin hemen
> ardından — ap-s3'ün son ailesi) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## Devir notu (oturum 20)

Aile açıldı — ap-s3'ün SON ailesi. Ana örnek (port 5083, `mt_s3`) normal
durumda (OpenAI açık, hiçbir özel env override yok). SQL doğrulama
sorguları kendi şerit şeması (`mt_s3`) ile koşuluyor.

---

### MT-MEM-001

**Gerçek sonuç**
`POST /api/agents/validate` (`SlidingWindow`, hiç tetikleyici yok) →
`valid:false`, `messages[0].code:"compilation_error"`, mesaj (İngilizce
— spec düzeltildi, K-228): `"Agent 'manuel-tetiksiz' selected the
'SlidingWindow' compaction strategy but gave no trigger..."`. `GET
/api/agents` listesinde `manuel-tetiksiz` YOK — hiçbir kayıt oluşmadı.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-002

**Gerçek sonuç**
`ContextWindow` stratejisi `MaxContextWindowTokens` olmadan doğrulandı →
`valid:false`, mesaj spec'in beklediğinden daha ZENGİN: modelin kendi
katalog bağlam penceresine de baktığını söyleyen bir cümle içeriyor
(`"...its model ('openai/gpt-5.4-mini') has no context window size in
the catalog either..."`) — spec'in yazıldığı andan sonra eklenmiş bir
iyileştirme, ürün kusuru değil, spec düzeltildi. Beklenen sonucun temel
iddiası (`valid:false`, doğru hata) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-003

**Gerçek sonuç**
Geçerli `SlidingWindow` (`triggerMessages:6, minimumPreservedTurns:1`)
→ `valid:true`, `messages:[]`. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-004

**Gerçek sonuç**
`strategy:"Sihirli"` (enum'da olmayan) → `HTTP 400`, gövde bir JSON
dönüştürme hatası (`"The JSON value could not be converted to
Tracon.CompactionStrategyKind..."`), `valid` alanı hiç yok — rapor hiç
üretilmedi. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-005

**Gerçek sonuç**
Üç harness çakışma denetimi de doğrulandı: (1) `disableCompaction:true` +
`compaction.strategy:SlidingWindow` → `valid:false`, `"...wants
compaction, but HarnessSettings.DisableCompaction is turned off."`;
(2) `disableFileMemory:true` + `memory.enableFileMemory:true` →
`valid:false`, `"...wants file memory..."`; (3)
`disableTodoProvider:true` + `memory.enableTodo:true` → `valid:false`,
`"...wants todo tracking..."`. Üçü de doğru agent adını ve doğru ayar
adını taşıyor (İngilizce, spec düzeltildi — K-228). Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 20 devam) — §2 gerçek sıkıştırma (GERÇEK PARA)

---

### MT-MEM-006

**Yöntem notu — keşif.** `mt_s3.run_events`'in `text`/`payload` sütunları
DB'de UYGULAMA-SEVİYESİ ŞİFRELİ (`$apEnc` zarfı) — spec'in ham SQL'i
içerik OKUYAMAZ, yalnız SAYABİLİR (satır var/yok, `type=10`). İçerik
doğrulaması bu yüzden HTTP ucundan (`GET /api/runs/{id}/events`, sunucu
deşifre eder) yapıldı — bu bir kusur değil, dinlenme-hâlinde şifreleme
tasarımının doğal bir sonucu.

**Gerçek sonuç**
`manuel-sikistir` agent'ı (`FIX-MEM-COMPACT-01`) oluşturuldu. Aynı
oturuma (`mem-sikistir-01`) `Idempotency-Key` başlığıyla 6 tur gönderildi
(hepsi `HTTP 200`, JSON — SSE değil). Son run'ın olay listesinde
`type:"HistoryCompacted"` VAR: `text:"2 messages compacted"` (İngilizce
— spec'in beklediği "N mesaj ozetlendi" kalıbı yerine, K-228; `N>0`
iddiası doğru), `payload:"beforeMessages=7, afterMessages=5,
beforeTokens=28, afterTokens=19"` — tam beklenen dört alan, VE
`afterMessages(5) < beforeMessages(7)`. SQL sayımı (`type=10`) **3** satır
döndü — birden fazla tur boyunca sıkıştırma tekrar tekrar tetiklendi.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-007

**Gerçek sonuç**
`mem-sikistir-01` oturumunun `mt_s3.conversation_items`'taki toplam öge
sayısı: **12** (6 tur × kullanıcı+asistan) — MT-MEM-006'nın
`afterMessages:5`'inden BÜYÜK, tam beklendiği gibi: sıkıştırma modelin
GÖRDÜĞÜ bağlamı küçültüyor, DİSKTEKİ geçmişi silmiyor. Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-008

**Gerçek sonuç**
`manuel-ozetle` (`Summarization`) oluşturuldu, aynı desenle 5 tur
gönderildi. 4. ve 5. turlarda `HistoryCompacted` tetiklendi. 5. tur:
`payload:"beforeMessages=9, afterMessages=7, beforeTokens=37,
afterTokens=28"` — `afterTokens < beforeTokens` (gerçek özetleme, salt
atma değil). Aynı run'ın `usage.totalTokens:232` — `NULL` değil,
pozitif; özetleme çağrısının kendi token'ları koşunun toplamına
eklenmiş (K-108). 4. tur ilginç bir ek kanıt: `afterMessages=beforeMessages=7`
olsa bile `afterTokens(29) < beforeTokens(32)` — mesaj SAYISI aynı
kalsa bile içerik gerçekten kısaltılmış, `SlidingWindow`'un aksine
(o mesaj SİLER, bu METNİ kısaltır). Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-009

**Gerçek sonuç**
`manuel-pipeline` (`Pipeline`) oluşturuldu, 6 tur gönderildi — hepsi
`HTTP 200`, hiçbiri `5xx` değil (üç iç stratejinin zincirlemesi hiçbir
yerde çökmedi). Son run'ın olay listesinde `HistoryCompacted`:
`payload:"beforeMessages=7, afterMessages=5, beforeTokens=35,
afterTokens=26"`. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-010

**Gerçek sonuç**
`support` (Compaction alanı hiç yok) aynı oturumda 6 tur çalıştırıldı.
`SELECT count(*) FROM mt_s3.run_events e JOIN mt_s3.runs r ON
r.id=e.run_id WHERE r.session_id='mem-kontrol-01' AND e.type=10` → **`0`**
— oturumun HİÇBİR run'ında `HistoryCompacted` üretilmedi. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
