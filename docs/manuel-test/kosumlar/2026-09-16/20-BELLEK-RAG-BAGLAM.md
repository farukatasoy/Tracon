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

## 4. Bilgi/belge uçları (§4)

**Yöntem notu.** Uygulama tekrar `default` kiracı/tenancy kapalı normal
duruma döndürülerek yeniden başlatıldı (eski PID `38962` sonlandırıldı,
yeni PID `39545`, port 5083, `mt_s3` şeması) — `/health` → `Healthy`
doğrulandı.

### MT-MEM-015

**Gerçek sonuç**
`POST .../documents` → `{"sourceId":"izin-notu","chunkCount":1}`, `HTTP: 200`
— birebir örtüştü. SQL doğrulama: `mt_s3.document_embeddings`'te
`izin-notu`/`chunk_index=0`, `length=54`, `created_at` dolu — tek satır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-016

**Gerçek sonuç**
`GET .../documents` → `["izin-notu"]` — birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-017

**Gerçek sonuç**
`"tatil hakkim ne kadar"` sorgusu ("tatil" kelimesi belgede hiç geçmiyor) →
sonuç boş değil, `sourceId:"izin-notu"`, `distance: 0.5480` (`< 2.0`).
Beklenen sonuç örtüştü; ölçülen mesafe Faz 51'in kendi kanıtındaki
`0.241` değerinden farklı ama aynı kosinüs-mesafe ölçeğinde ve eşiğin
(2.0) çok altında — anlamsal eşleşme doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-018

**Gerçek sonuç**
1. `DELETE .../documents/izin-notu` → `HTTP: 204`.
2. Aynı sorguyla arama → `[]`.
3. Liste → `[]`.
4. SQL: `mt_s3.document_embeddings`'te `izin-notu` için `count = 0`.
Dördü de birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-019

**Gerçek sonuç**
1536 uzunluklu elle üretilmiş sabit embedding (`[0.001]*1536`) ile
`chunks` gönderildi → `{"sourceId":"hazir-parca","chunkCount":1}`,
`HTTP: 200`. Gövdede embedding zaten doluydu; hiçbir gömme API çağrısı
tetiklenmedi (İzlek C — model çağrısı yok, tutarlı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-020

**Gerçek sonuç**
1. İlk yükleme (`"Ilk surum metni."`) → `200`.
2. Aynı `sourceId` (`tekrar-notu`) ile ikinci yükleme (`"Ikinci surum
   metni, tamamen farkli icerik."`) → `200`.
3. SQL: `mt_s3.document_embeddings`'te `tekrar-notu` için TEK satır,
   `content = "Ikinci surum metni, tamamen farkli icerik."` — "Ilk surum"
   yok. Upsert eski parçaları gerçekten değiştiriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-021

**Doküman düzeltmesi.** Spec'in beklediği `detail` metni Türkçeydi (`Ya text
ya da chunks verilmelidir; ikisi birden ya da hicbiri olamaz.`); gerçek API
İngilizce döner (K-228 — çalışma anı mesajları İngilizce kalır). `Beklenen
sonuç` bu koşumda düzeltildi.

**Gerçek sonuç**
`text` ve `chunks` birlikte gönderildi → `HTTP: 400`, `detail`: `"Either
text or chunks must be given; not both, and not neither."` — düzeltilmiş
beklentiyle birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-022

**Gerçek sonuç**
Ne `text` ne `chunks` gönderildi → `HTTP: 400`, aynı `detail` metni
(`MT-MEM-021` ile birebir aynı) — örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-023

**Doküman düzeltmesi.** Aynı K-228 gerekçesiyle spec'in Türkçe `detail`
beklentisi İngilizce metinle değiştirildi.

**Gerçek sonuç**
`kurumsal%20bilgi` koleksiyon adına yükleme denendi → `HTTP: 400`,
`detail`: `"'kurumsal bilgi' is not a valid collection name. It may only
contain letters, digits, underscores, and hyphens."` — düzeltilmiş
beklentiyle birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-024

**Doküman düzeltmesi.** Aynı K-228 gerekçesiyle spec'in Türkçe `detail`
beklentisi İngilizce metinle değiştirildi.

**Gerçek sonuç**
2 elemanlı embedding (`[0.1,0.2]`, depo boyutu 1536) gönderildi →
`HTTP: 400`, `detail`: `"Chunk 0 embedding length (2) does not match the
store dimension (1536)."` — düzeltilmiş beklentiyle birebir örtüştü. SQL:
`mt_s3.document_embeddings`'te `yanlis-boyut` için `count = 0` — kısmi
yazma yok, tek transaction doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## 5. `knowledge-assistant` uçtan uca (§5)

### MT-MEM-026

**Doküman düzeltmesi — spec'in koleksiyon adı yanlıştı.** Spec
`knowledge-assistant` fixture'ının `kurumsal` koleksiyonuna bağlı olduğunu
iddia ediyordu. İlk deneme literal adımlarla koşuldu (`kurumsal`
koleksiyonuna yükleme) → agent `search_knowledge`'ı İKİ KEZ çağırdı, ikisi
de `[]` döndü, model kullanıcıya "bilgi tabanında kayıt bulamadım" dedi.
Kaynak okundu (`samples/Tracon.Api/Program.cs:843`):
`Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection
= "knowledge-base" }` — gerçek koleksiyon adı `knowledge-base`'dir, `kurumsal`
değil. Spec yukarıda (`FIX-MEM-COLLECTION-01` satırı ve case gövdesi)
düzeltildi.

**Gerçek sonuç**
`knowledge-base` koleksiyonuna `izin-politikasi` yüklendi (`200`).
`knowledge-assistant`'a `"Kac gun tatilim var?"` soruldu → yanıt `"Yıllık
izin **14 gün**..."` — `14` dizgisini içeriyor. SQL doğrulama:
`mt_s3.tool_invocations`'ta bu `runId` için `search_knowledge` → `count=1`
— tam bir kez çağrıldı (düzeltilmiş koleksiyonla, ilk denemedeki çift
çağrı ortadan kalktı). Düzeltilmiş beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-027

**Gerçek sonuç**
`VectorCollection` verilmeden `EnableVectorSearch:true` ile
`manuel-varsayilan-koleksiyon` agent'ı kaydedildi (yanıt gövdesinde
`"vectorCollection":null"` doğrulandı). Agent'ın KENDİ ADIYLA aynı
koleksiyona (`manuel-varsayilan-koleksiyon`) bir belge (`"Ofis WiFi
sifresi: bulut-42."`) yüklendi. Agent'a `"WiFi sifresi nedir?"` soruldu →
`search_knowledge` tek çağrıda `n1` kaydını buldu (`distance: 0.413`),
yanıt: `"WiFi şifresi: **bulut-42**"` — `bulut-42` dizgisini içeriyor.
`VectorSearchToolFactory`'ye geçirilen koleksiyonun `definition.Name`
olduğu doğrulandı. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## 8. Kapsam güvenliği (§8, sıra dışı koşuldu)

### MT-MEM-031

**Doküman düzeltmesi — spec'in şüphesi bu koşumda çürütüldü.** Spec'in
`grep -n "RequireApiKeyScope" .../KnowledgeEndpoints.cs` kanıtı (boş döner,
"beşinci bilinen tekrar" iddiası) bayat çıktı; aynı komut güncel kaynakta
(satır 25/38/52/66) `KnowledgeAdmin`/`KnowledgeRead` kapsamlarını gösteriyor.
Spec başlığı ve gövdesi yukarıda düzeltildi — bu artık `MT-WF-100` /
`MT-JOB-090` / `MT-EVAL-100`/`101` / `MT-MCP-051`/`052` ile aynı sınıftan
AÇIK bir tekrar değil, ayrı ve kapanmış bir örnek.

**Gerçek sonuç**
Yalnız `RunsRead` kapsamlı anahtar (`mem-kapsam-testi`) üretildi.
1. Belge yazma (`POST .../documents`) → `HTTP: 403`, `detail`: `"This
   endpoint requires the 'KnowledgeAdmin' scope; the key does not carry
   it."`
2. Belge silme (`DELETE .../documents/x`) → `HTTP: 403`, aynı `detail`.
3. Kontrol grubu — agent yazma (`PUT /api/agents/kapsam-kontrol`) →
   `HTTP: 403`, `detail`: `"...requires the 'AgentsAdmin' scope..."`.
Üçü de tutarlı biçimde reddetti — `KnowledgeEndpoints` kapsam denetimini
doğru uyguluyor, kusur yok. Düzeltilmiş beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## 6. `EnableVectorSearch` doğrulama hataları (§6)

**Yöntem notu.** Uygulama PostgreSQL/SQLite/SQL Server bağlantı dizgileri
BOŞ (tamamen bellek içi) olarak yeniden başlatıldı (eski PID `39547`
sonlandırıldı, yeni PID `42573`, port 5083) — `/health` → `Degraded`
(beklenen, depo yok).

### MT-MEM-025

**Doküman düzeltmesi.** Spec'in Türkçe `title`/`detail` beklentisi
gerçek İngilizce metinle değiştirildi (K-228).

**Gerçek sonuç**
Dört uç da (`POST .../documents`, `GET .../documents`, `POST .../search`,
`DELETE .../documents/x`) `HTTP: 501` döndü, `title`: `"Knowledge base not
supported"`, `detail`: `"An IVectorSearchStore (today only PostgreSQL:
UsePostgreSql()) AND an IEmbeddingGenerator<string, Embedding<float>>
must both be registered."` — dördü de birebir aynı, düzeltilmiş beklenen
sonuçla örtüştü. `IVectorSearchStore` hiç kayıtlı değilken sessizce boş
sonuç DÖNMÜYOR (K1 gereksinimi doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-028

**Doküman düzeltmesi.** Aynı K-228 gerekçesiyle spec'in Türkçe mesaj
beklentisi İngilizce metinle değiştirildi.

**Gerçek sonuç**
`EnableVectorSearch:true` bir tanım (`manuel-vektor-yok`) doğrulandı →
`valid:false`, `messages[0].message`: `"Agent 'manuel-vektor-yok' wants
semantic search, but IVectorSearchStore is not registered (today only
PostgreSQL: UsePostgreSql())."` — düzeltilmiş beklentiyle birebir örtüştü.
Sessizce geçmiyor, derleme hatası veriyor (K4/K-343 doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MEM-029

**Yöntem notu.** Uygulama PostgreSQL AÇIK, `Tracon__Providers__OpenAI__ApiKey`
BOŞ olarak yeniden başlatıldı (eski PID `42573` sonlandırıldı, yeni PID
`42950`, port 5083, `mt_s3` şeması) — `/health` → `Degraded` (beklenen,
OpenAI eksik). Örnek uygulama bu modda model sağlayıcısı olarak `echo`
kaydediyor (`Program.cs:149`), spec'in öngördüğü gibi.

**Doküman düzeltmesi.** Aynı K-228 gerekçesiyle spec'in Türkçe mesaj
beklentisi İngilizce metinle değiştirildi.

**Gerçek sonuç**
`{provider:"echo", model:"echo-1"}` modelli, `EnableVectorSearch:true`
bir tanım (`manuel-gomu-yok`) doğrulandı → `valid:false`,
`messages[0].message`: `"Agent 'manuel-gomu-yok' wants semantic search,
but IEmbeddingGenerator<string, Embedding<float>> is not registered."` —
düzeltilmiş beklentiyle birebir örtüştü. PostgreSQL (`IVectorSearchStore`)
kayıtlı olsa BİLE `IEmbeddingGenerator` eksikken derleme hatası veriyor,
sessizce geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
