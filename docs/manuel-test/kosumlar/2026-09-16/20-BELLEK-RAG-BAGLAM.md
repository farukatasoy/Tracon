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
