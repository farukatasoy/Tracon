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

## Devir notu (oturum 20 devam) — §3 bellek sağlayıcıları

---

### MT-MEM-011

**Gerçek sonuç**
`manuel-dosya-bellek` (`enableFileMemory`) oluşturuldu. 1. turda gerçekten
`file_memory_write` çağrıldı (`fileName:"user_note.txt", content:"kayit-kodu
FILE-7841"`). 2. turda (aynı oturum) gerçekten `file_memory_read`
çağrıldı, sonucu `"kayit-kodu FILE-7841"` döndü, nihai yanıt metni:
`"Kayıt kodu: **FILE-7841**."` — `FILE-7841` dizgisini içeriyor. Beklenen
sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-012

**Gerçek sonuç**
`manuel-todo` (`enableTodo`) oluşturuldu. 1. turda iki görev
("raporu yaz", "sunumu hazirla") eklenmesi istendi. 2. turda (aynı
oturum) "Todo listemde neler var?" sorusunun yanıtı: `"Todo listenizde
şunlar var:\n\n1. Raporu yaz\n2. Sunumu hazırla"` — iki görev de gerçekten
anıldı. Beklenen sonuç (gevşek kontrol — iki konunun da geçmesi) birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-013

**Yöntem notu — büyük keşif: spec'in kendi "2026-08-10 durumu" notu
bayat çıktı.** İlk deneme spec'in literal adımlarıyla koşuldu
(`manuel-dosya-arama`, FARKLI bir agent, `FILE-7841` aramaya çalıştı) →
model `file_memory`/arama aracı ÇAĞIRMADI, düz metin yanıtı: `"Dosya
belleğinde FILE-7841 için bir kayıt bulamadım."` / (ikinci denemede)
`"...şu anda bu oturumda araç erişimi görünmüyor."`. İlk bakışta bir
`EnableTextSearch` kusuru sanıldı. Kaynak okundu
(`TenantPrefixingAgentFileStore.cs:6-38`): önek `/{tenantId}/{agentName}/...`
— **ajan adı da önekte**, yalnız kiracı değil. `docs/arsiv/
PLANA-DONUSEN-ADAYLAR.md:363` bunu doğruladı: `"F-105 · Dosya belleği
kiracı-içi sınırı — ✅ KAPATILDI (2026-08-18)"` — spec'in "2026-08-10
durumu" notu bu kapanıştan SEKİZ GÜN ÖNCEYE ait, bayat. Hipotez ampirik
olarak doğrulandı: `enableFileMemory` VE `enableTextSearch` İKİSİNİ birden
taşıyan TEK bir agent (`manuel-dosya-hem`) kuruldu; bir oturumda not yazdı
(`file_memory_write`), FARKLI bir oturumda AYNI agent `file_memory_grep`
ile notu buldu ve doğru yanıtladı (`FILE-9999` metniyle) — mekanizmanın
kendisi (ajan-içi, oturumlar-arası) sağlam. Spec yukarıda düzeltildi.

**Gerçek sonuç**
`manuel-dosya-arama` (FARKLI agent) → `manuel-dosya-bellek`'in dosyasını
BULAMADI, `FILE-7841` yanıtta YOK — ajan-düzeyi izolasyon çalışıyor
(F-105 kapalı kalıyor, regresyon yok). Kontrol: `manuel-dosya-hem` (AYNI
agent, iki farklı oturum) kendi notunu (`FILE-9999`) başarıyla buldu.
Düzeltilmiş beklenen sonucun tamamı (izolasyon + mekanizmanın kendisi)
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-014

**Yöntem notu.** Ana örnek `Tracon__Tenancy__Enabled=true`,
`Tracon__Tenancy__AllowHeaderResolution=true` ile yeniden başlatıldı.
Her iki fixture agent'ı KENDİ kiracısı altında yeniden kaydedilmek
zorunda kaldı (`manuel-dosya-bellek`→`kiraci-alfa`,
`manuel-dosya-arama`→`kiraci-beta`) — agent tanımları da kiracıya göre
izole, `default` kiracısındaki önceki kopyalar diğer kiracılardan
görünmüyor (beklenen, ayrı bir doğrulama).

**Gerçek sonuç**
`kiraci-alfa` altında `manuel-dosya-bellek` gerçek bir `SIZINTI-9902`
notu dosyaya yazdı (`file_memory_write`, `200`). `kiraci-beta` altında
FARKLI bir agent (`manuel-dosya-arama`) aynı terimi aramaya çalıştı —
yanıt: `"Dosya belleğine erişimim yok..."`, `SIZINTI-9902` yalnız
KULLANICININ SORDUĞU TERİM olarak yankılandı (`"...içinde SIZINTI-9902'yi
arayıp özetleyeyim"` — modelin "bulamadım, sen yapıştır" önerisi), gerçek
kayıt İÇERİĞİ (`gizli-anahtar SIZINTI-9902` notunun kendisi) hiç
sızmadı. `kiraci-alfa`'nın dosyasına `kiraci-beta` erişemedi. Beklenen
sonucun tamamı (sızıntı yok) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
