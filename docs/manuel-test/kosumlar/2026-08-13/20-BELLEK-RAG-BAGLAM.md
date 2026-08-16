# 20 — Bağlam Sıkıştırma, Bellek Sağlayıcıları ve Anlamsal Arama (`MEM`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../20-BELLEK-RAG-BAGLAM.md`](../../20-BELLEK-RAG-BAGLAM.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-MEM-001 — Tetikleyicisiz `SlidingWindow` derleme hatası verir

**Gerçek sonuç**
`valid:false`, `messages[0].code="compilation_error"`, mesaj metni beklenenle
birebir eşleşti. `GET /api/agents` çıktısında `manuel-tetiksiz` yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-002 — `ContextWindow` stratejisi `MaxContextWindowTokens` olmadan derleme hatası verir

**Gerçek sonuç**
Mesaj metni beklenenle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-003 — Geçerli `SlidingWindow` tanımı `valid: true` döner (pozitif kontrol)

**Gerçek sonuç**
`valid:true`, `messages:[]`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-004 — Bilinmeyen `strategy` string değeri JSON deserialize hatası verir (`400`)

**Gerçek sonuç**
`HTTP: 500` döndü, `400` değil. Gövde: generic `ProblemDetails`
(`"title":"An error occurred while processing your request."`), `valid` alanı
yok — bu kısmı beklenene uyuyor ama durum kodu yanlış. Uygulama logu
(`Unhandled exception`) kök nedeni gösteriyor: `System.Text.Json.JsonException:
The JSON value could not be converted to AgentPrism.CompactionStrategyKind.`
istisnası ASP.NET Core'un JSON body binding aşamasında fırlıyor ve hiçbir yerde
yakalanmıyor — global exception handler'a düşüp genel `500`'e dönüşüyor.
`ValidateAgentAsync`'in kendi XML dokümanının vaat ettiği "gövde
ayrıştırılamıyorsa 400" davranışı yalnız `JsonException`'ı **kendi içinde**
yakalayan bir path için geçerli olabilir; enum dönüştürme hatası minimal
API'nin body-binding aşamasında (endpoint gövdesine hiç girmeden) oluştuğu için
o path'e hiç ulaşmıyor. **Kusur, Önem: Orta** — `HATA-S1-007` olarak kaydedildi.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-007 düzeltmesiyle yeniden koşuldu: govde artik elle okunuyor, gecersiz enum artik 400 (Gecersiz istek govdesi). Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-005 — Harness çakışma denetimi: `Disable*` bayrakları etkin ayarlarla çelişirse derleme hatası

**Gerçek sonuç**
Üç adımın üçü de beklenen mesajla birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Sıkıştırma: Gerçek Çalıştırma ve Kalıcılık (Faz 13, İzlek B)

`POST /api/agents` compile denetimi yapmaz (bkz. §1 girişi); bu bölümdeki
her case önce `PUT /api/agents/{name}` ile kaydeder, sonra gerçek turlarla
çalıştırır.

---

## MT-MEM-006 — `SlidingWindow` gerçek konuşmada tetiklenir; `HistoryCompacted` olayı üretilir

**Gerçek sonuç**
Kayıt `POST /api/agents` ile düzeltildi (bkz. dosya başındaki sapma notu),
`HTTP:201`. 5 turun tamamı `HTTP 200` döndü. Son run'ın `GET
.../events` (SSE) çıktısında `HistoryCompacted` olayı var: `text:"2 mesaj
ozetlendi"`, `payload:"beforeMessages=7, afterMessages=5, beforeTokens=34,
afterTokens=25"` — `afterMessages(5) < beforeMessages(7)` doğrulandı. (Not:
doc'un `d.get('text')` çıkarma script'i yanıt gövdesinde böyle bir alan
olmadığı için hep `None` bastı — run yanıtı `response.messages[0].contents[0].text`
altında; bu yalnız script'in kendi kolaylık çıktısı, bir kusur değil, hiçbir
`Beklenen sonuç` bu alana bağlı değil.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-007 — Sıkıştırılan mesajlar `conversation_items`'ta SİLİNMEZ (K-107)

**Gerçek sonuç**
`mem-sikistir-01` oturumu için `toplam_oge=10` — 5 turun ürettiği tüm
mesajlar (kullanıcı+asistan) korunmuş, `MT-MEM-006`'nın `afterMessages=5`
değerinden büyük.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-008 — `Summarization` stratejisi: özet sonrası token sayısı azalır, çalıştırma toplamı pozitif kalır

**Gerçek sonuç**
Son run'ın olay listesinde `HistoryCompacted`: `payload:"beforeMessages=9,
afterMessages=7, beforeTokens=36, afterTokens=27"` — `afterTokens(27) <
beforeTokens(36)`. `GET /api/runs/{id}` yanıtında `usage.totalTokens=232`
(`NULL` değil, pozitif).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-009 — `Pipeline` stratejisi uçtan uca çalışır ve çökmez

**Gerçek sonuç**
5 turun tamamı `HTTP:200`. Son run'ın olay listesinde `HistoryCompacted`:
`payload:"beforeMessages=7, afterMessages=5, beforeTokens=31, afterTokens=23"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-010 — Sıkıştırma **kapalıyken** uzun konuşmada `HistoryCompacted` hiç üretilmez (kontrol grubu)

**Gerçek sonuç**
`count=0` — `support` agent'ında (`Compaction` tanımsız) 5 turluk konuşmada
hiç `HistoryCompacted` üretilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Bellek Sağlayıcıları: Dosya Belleği, Todo, Metin Arama (Faz 13)

Üçü de `AgentDefinitionCompiler.CreateMemoryProviders` üzerinden **düz sohbet
agent'ına da** bağlanır (Harness gerekmez). Hepsi tek bir paylaşılan
`AgentFileStore` singleton'ı üzerinde çalışır
(`AgentPrismServiceCollectionExtensions.cs:300-301`,
`TryAddSingleton<AgentFileStore>` — **süreç genelinde tek örnek**, tenant/
agent/session başına ayrılmaz).

---

## MT-MEM-011 — `EnableFileMemory`: agent turlar arası bir notu dosyaya yazıp geri okuyabilir

**Gerçek sonuç**
1. tur `HTTP 200` — `FILE-7841` gerçekten `mt_s1.agent_files`'a yazıldı (SQL
ile doğrulandı: `/default/user-notes.md` içinde `kayıt kodu FILE-7841.`
satırı var). 2. tur (geri okuma/hatırlama) **`HTTP 500`** ile çöktü —
tekrarlanan denemelerde de (aynı oturumda ve yeni bir oturumda) hep aynı
sonuç. Uygulama logu kök nedeni gösteriyor:
`System.NotSupportedException: JsonTypeInfo metadata for type
'Microsoft.Agents.AI.AgentRequestMessageSourceAttribution' was not provided
by TypeInfoResolver of type 'AgentPrism.AgentPrismJsonContext'` — model
mesajının `AdditionalProperties`'ine MAF'ın eklediği bir "attribution"
(kaynak bilgisi) nesnesi, AOT kaynak-üretimli JSON context'inde
kayıtlı değil; HTTP yanıtı serileştirilirken patlıyor. Run kendisi
sunucu tarafında TAMAMLANIYOR (span/idempotency kaydı yazılıyor, `runs`
tablosunda `status=2` ile görünüyor — model çağrısı ve maliyeti gerçekleşmiş
oluyor) ama istemci hiçbir zaman bir yanıt alamıyor. **Kusur, Önem: Yüksek**
— `HATA-S1-008` olarak kaydedildi (`MT-MEM-012` ile aynı kök neden).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-008 düzeltmesiyle yeniden koşuldu: okuma turu artik 200, yazilan icerigi dogru hatirliyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-012 — `EnableTodo`: agent bir todo listesi oluşturur ve kalan kalemleri hatırlar

**Gerçek sonuç**
1. tur (todo'ya iki görev ekleme) doğrudan **`HTTP 500`** ile çöktü —
`MT-MEM-011`'in 2. turuyla birebir aynı istisna
(`AgentRequestMessageSourceAttribution` serileştirme hatası, `HATA-S1-008`).
`TodoProvider`'ın ilk yazma turunda bile bu attribution etiketini eklediği
görülüyor (`FileMemoryProvider`'dan farkı: o yalnız OKUMA turunda patlıyordu,
`TodoProvider` YAZMA turunda da patlıyor). 2. tur da aynı nedenle `HTTP 500`.
İki görevin de yanıtta anılıp anılmadığı hiç ÖLÇÜLEMEDİ — kapsayan kusur
`HATA-S1-008` bu case'i tamamen bloke ediyor.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-008 düzeltmesiyle yeniden koşuldu (ayni AgentRequestMessageSourceAttribution kok nedeni). Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-013 — `EnableTextSearch`: agent dosya belleğinde arama yapıp sonucu kullanır

**Gerçek sonuç**
Yanıt `FILE-7841` dizgisini İÇERMEDİ — beklenenin (F-105 gerekçesiyle
"bulmalı") TERSİ gözlendi. `GET .../events` çıktısında hiçbir
`ToolInvoked`/arama olayı yok; model doğrudan "eşleşen kayıt bulamadım"
yanıtı üretti. Bir kelime değişikliğiyle ("search tool unu kullanarak ara")
tekrarlandığında model bu kez "bağlı bir arama aracına erişimim yok" dedi —
yani `TextSearchProvider` (bir `AIContextProvider`, modele araç olarak
sunulmuyor, MAF'ın kendisi otomatik bağlam enjekte etmesi bekleniyor)
sorguyla eşleşen içeriği hiç BULAMADI/enjekte ETMEDİ; dosyada `FILE-7841`
harfiyen mevcut olsa bile (`MT-MEM-011`'in yazdığı `/default/user-notes.md`).
İki bağımsız denemede de sıfır sonuç — bu, F-105'in beklediği "bulur ama
izole etmeli" durumundan farklı, daha temel bir sorun: **arama hiç
çalışmıyor gibi görünüyor**. Kod seviyesinde doğrulanmadı (siyah kutu HTTP
testi bunun ötesine geçemez); **şüpheli davranış, Önem: Orta** —
`HATA-S1-009` olarak kaydedildi. `MT-MEM-014`'ün negatif sonucu bu yüzden
kiracı yalıtımının kanıtı SAYILAMAZ (bkz. o case'in notu).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-009 düzeltmesiyle (K-396) yeniden koşuldu: dogal dil sorgusu artik FILE-7841'i buluyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-014 — `TextSearchProvider` artık kiracılar arası ARAMAZ — düzeltilmiş kiracı yalıtımını doğrular

**Gerçek sonuç**
Sapma: kiracılık `Tenancy:Enabled`/`AllowHeaderResolution` açıldığında
agent'lar da kiracıya özgü olduğu için (önceki `default` kiracıda kayıtlı
`manuel-dosya-bellek`/`manuel-dosya-arama` `kiraci-alfa`/`kiraci-beta`'da
`404` verdi) her iki agent önce `POST /api/agents` ile ilgili
`X-AgentPrism-Tenant` başlığı altında AYRICA kaydedildi — dokümanın
atladığı bir ön adım. Sonrasında: yazma turu `HTTP 200`. Arama turunun
yanıtı `SIZINTI-9902` dizgisini İÇERMEDİ — beklenen sonuçla eşleşiyor GİBİ
görünüyor, ama `MT-MEM-013`'ün bulgusuna göre (`HATA-S1-009`) arama zaten
HİÇBİR sorguda bir şey bulamıyor; bu yüzden bu "negatif" sonuç kiracı
yalıtımının kanıtı değil, muhtemelen aynı temel arama arızasının bir başka
görünümü. **İnceleme sonucu belirsiz (inconclusive)** — `HATA-S1-009`
çözülmeden bu case'in gerçek anlamda "Geçti" sayılması mümkün değil.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-009 düzeltmesiyle yeniden koşuldu: bu case'in KENDİ ölçütü (kiraci-beta SIZINTI-9902'yi göremez) sağlandı — arama artik gercekten çalışıyor ve çapraz kiracı sizintisi yok. YENİ GÖZLEM (bu case'in kapsamı DIŞINDA, kök nedeni bu oturumda araştırılmadı): aynı kiracı içinde FARKLI agent (manuel-dosya-arama) manuel-dosya-bellek'in yazdığı dosyayı da göremedi (file_memory_ls boş döndü) — dokümanın "aynı kiracı içinde ajan sınırı yok" iddiasıyla çelişiyor. Sonraki bir oturum için not: agent-bazlı bir izolasyon katmanı mı var yoksa bu belirli test kurulumuna mı özgü, doğrulanmalı.

---

# 4 — Anlamsal Arama (RAG): Yönetim API'si — Belge CRUD (Faz 51)

Belge yükleme bir **yönetim** işlemidir, agent'ın işi değil (§51.6).
`KnowledgeIngestionService.IsSupported` `false` iken (PostgreSQL kayıtlı
değil VEYA `IEmbeddingGenerator` kayıtlı değil) her uç sessizce boş sonuç
DÖNMEZ, `501` döner (K1).

---

## MT-MEM-015 — Düz metinle belge yükleme: sunucu parçalar ve gömüler

**Gerçek sonuç**
Koşulamadı: `POST /api/knowledge/{collection}/documents` gerçek bir OpenAI
embedding çağrısı gerektiriyor (`text-embedding-3-small`). Bu şeridin
kullandığı OpenAI anahtarının bağlı olduğu proje bu modele (ve denenen
diğer embedding modellerine — `3-large`, `ada-002`) erişemiyor
(`403 model_not_found`, doğrudan OpenAI API'sine karşı doğrulandı; `/v1/models`
embedding modeli hiç listelemiyor). Program.cs modeli sabit kodluyor,
config'den değiştirilemiyor (bkz. dosya başındaki not). Kullanıcı kararı:
bloke edilen case'ler `Beklemede` bırakılıp koşum sürdürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — HTTP 200, gövde tam olarak {"sourceId":"izin-notu","chunkCount":1}. S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-016 — `GET .../documents` yüklenen kaynağı listeler

**Gerçek sonuç**
Koşulamadı: ön koşul `MT-MEM-015` embedding erişimi eksikliğinden
koşulamadı (bkz. o case'in notu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — ["izin-notu"] döndü, tam beklenen gibi. S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-017 — Anlamsal arama KELİME EŞLEŞMESİ OLMAYAN bir sorguyla doğru parçayı bulur

**Gerçek sonuç**
Koşulamadı: sorgu embedding'i gerektirir, embedding erişimi yok (bkz.
`MT-MEM-015`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — sourceId=izin-notu, distance=0.548 (<2.0). S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-018 — `DELETE .../documents/{sourceId}` kaynağı siler; sonraki arama onu döndürmez

**Gerçek sonuç**
Koşulamadı: ön koşul `MT-MEM-015` embedding erişimi eksikliğinden
koşulamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — silme 204, sonraki arama [] , liste []. S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-019 — Hazır `chunks` ile (embedding VERİLMİŞ) yükleme: sunucu yeniden gömmez

**Gerçek sonuç**
`HTTP: 200`, gövde tam olarak `{"sourceId":"hazir-parca","chunkCount":1}`.
Hazır embedding verildiği için embedding API'ye hiç çıkılmadı (bu, §4'ün
embedding erişimi olmadan koşulabilen tek yükleme case'i).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-020 — Aynı `sourceId` ile yeniden yükleme ESKİ parçaları değiştirir (upsert)

**Gerçek sonuç**
Koşulamadı: `text` ile yükleme embedding gerektirir, embedding erişimi yok
(bkz. `MT-MEM-015`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — iki çağrı da 200; DB'de tek satır, içerik "Ikinci surum..." ile başlıyor. S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-021 — Hem `text` hem `chunks` birlikte gönderilirse `400`

**Gerçek sonuç**
`HTTP: 400`. `detail`: `"Ya text ya da chunks verilmelidir; ikisi birden ya
da hicbiri olamaz. (Parameter 'text')"` — beklenen metinle BAŞLIYOR ama
sonunda `.NET`'in `ArgumentException(message, paramName)`'ından otomatik
eklenen `" (Parameter 'text')"` soneki var; doküman bunu hesaba katmamış.
Kod: `KnowledgeIngestionService.cs:101-103` `nameof(text)`'i paramName
olarak veriyor, `KnowledgeEndpoints.cs:85` `ex.Message`'ı doğrudan `detail`
yapıyor — `ArgumentException.Message` her zaman bu soneki ekler. **Kusur,
Önem: Düşük** (işlevsel etkisi yok, yalnız dokümante edilen tam metinle
uyuşmuyor ve iç parametre adını dışa sızdırıyor) — `HATA-S1-010` olarak
kaydedildi; aynı desen `MT-MEM-022/023/024`'te de tekrarlıyor.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-010 düzeltmesiyle yeniden koşuldu: detail artik tam beklenen metinle, sonek yok. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-022 — Ne `text` ne `chunks` gönderilirse `400`

**Gerçek sonuç**
`HTTP: 400`, aynı `HATA-S1-010` soneki (`" (Parameter 'text')"`) burada da
var.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-010 düzeltmesiyle yeniden koşuldu: detail artik tam beklenen metinle. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-023 — Geçersiz koleksiyon adı `400` verir

**Gerçek sonuç**
`HTTP: 400`, `detail`: `"'kurumsal bilgi' gecerli bir koleksiyon adi degil.
Yalniz harf, rakam, alt cizgi ve tire icerebilir. (Parameter 'collection')"`
— aynı `HATA-S1-010` soneki.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-010 düzeltmesiyle yeniden koşuldu: detail artik tam beklenen metinle. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-024 — Yanlış boyutlu hazır embedding `400` verir

**Gerçek sonuç**
`HTTP: 400`, `detail`: `"Parca 0 gomu uzunlugu (2) depo boyutuyla (1536)
eslesmiyor. (Parameter 'chunks')"` — aynı `HATA-S1-010` soneki
(`document_embeddings`'e satır yazılmadığı ayrıca doğrulanmadı, ama kod
hatayı `UpsertAsync` çağrılmadan ATIYOR — `EmbedMissingAsync` sonrası,
`_store.UpsertAsync` çağrısından ÖNCE — dolayısıyla kısmi yazma riski yok).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-010 düzeltmesiyle yeniden koşuldu: detail artik tam beklenen metinle. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-025 — PostgreSQL kapalıyken (bellek içi/SQLite) bilgi tabanı uçları `501` verir

**Gerçek sonuç**
Sapma: `user-secrets remove` yerine (§2.2, şerit izolasyonu)
`AgentPrism__PostgreSql__ConnectionString=""` ortam değişkeni kullanıldı.
**Önemli bulgu (kendi kendine düzeltildi):** ilk denemede bu değişkeni
`unset` ile kaldırdım — bu, `user-secrets`'taki paylaşılan (ve `agentprism`
şemasına işaret eden) değere GERİ DÜŞTÜ, `mt_s1` yerine paylaşılan şemaya
4 istek gitti (upload/search embedding hatasından 500 oldu, list boş `[]`
döndü, delete 0 satır etkiledi — SQL ile doğrulandı, paylaşılan şemada
hiçbir veri yok/değişmedi, zarar yok). Düzeltme: `=""` ile AÇIKÇA boş değer
atandı (§2.2'nin tam istediği desen), bu kez doğru çalıştı: dördü de
`HTTP: 501`, `title`/`detail` metinleri BİREBİR beklenenle eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Anlamsal Arama: `search_knowledge` ve Agent Entegrasyonu (Faz 51)

---

## MT-MEM-026 — `bilgi-asistani` uçtan uca: belge yükle → soru sor → `search_knowledge` tam bir kez çağrılır

**Gerçek sonuç**
Koşulamadı: belge yükleme ve `search_knowledge` tool'unun sorgu embedding'i
üretmesi gerekiyor, embedding erişimi yok (bkz. `MT-MEM-015`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — yanıt "14 gün" içeriyor, search_knowledge tam bir kez çağrıldı (tool_invocations doğrulandı). S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-027 — `VectorCollection` boş bırakılırsa koleksiyon adı olarak AGENT ADI kullanılır

**Gerçek sonuç**
Koşulamadı: belge yükleme embedding gerektiriyor, embedding erişimi yok
(bkz. `MT-MEM-015`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — yanıt "bulut-42" içeriyor, varsayılan koleksiyon = agent adı doğrulandı. S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MEM-028 — `EnableVectorSearch=true` + `IVectorSearchStore` kayıtlı değil → derleme hatası, sessizce boş sonuç DÖNMEZ

**Gerçek sonuç**
Sapma: ortam değişkeni `=""` ile kullanıldı (bu kez baştan doğru — bkz.
`MT-MEM-025`'in notu). `valid:false`, `messages[0].message` beklenenle
birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MEM-029 — `EnableVectorSearch=true` + PostgreSQL var ama `IEmbeddingGenerator` kayıtlı değil → derleme hatası

**Gerçek sonuç**
Sapma: `AgentPrism__Providers__OpenAI__ApiKey=""` ortam değişkeni ile
kapatıldı, PostgreSQL bağlantısı `mt_s1`'e açık bırakıldı. `valid:false`,
mesaj beklenenle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Anlamsal Arama: Kiracı Yalıtımı (Faz 51, K-343 ile tezat)

`PgVectorSearchStore` `document_embeddings` sorgularının HER BİRİNDE
`tenant_id` filtresi taşır (Faz 51 DoD, `VectorTenantIsolationTests`) — bu YENİ
anlamsal arama yüzeyi baştan tenant-farkındaydı. `MT-MEM-014`'ün ölçtüğü eski
`TextSearchProvider` deseni (paylaşılan dosya deposunda kiracı filtresi
yoktu) 2026-08-10'da `TenantPrefixingAgentFileStore` ile aynı garantiye
kavuştu — bkz. `MT-MEM-014`'ün güncellenmiş notu.

---

## MT-MEM-030 — Vektör arama kiracı yalıtımı: bir kiracının belgesi diğerinde görünmez

**Gerçek sonuç**
Koşulamadı: belge yükleme ve arama embedding gerektiriyor, embedding
erişimi yok (bkz. `MT-MEM-015`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — kiraci-beta araması [] döndü, kiraci-alfa (kontrol) alfa-belge'yi buldu. S1-8'de gerçek embedding erişimli bir OpenAI anahtarıyla koşuldu. Bkz. SONUCLAR-S1-2026-08-13.md.

---

# 7 — Güvenlik: API Anahtarı Kapsam Boşluğu (Faz 51)

> **Rol matrisi burada da NO-OP'tur, tekrar test edilmez** (`00-INDEKS.md`
> §8 ve `14`/`15`/`16`/`17`/`18-*.md`'nin zaten kaydettiği genel bulgu).
> `KnowledgeEndpoints`'in `RequireRole(roles.Operator/Reader)` çağrıları,
> `AgentPrismPolicies.*` örnek uygulamada kayıtlı OLMADIĞI için etkisizdir.

---

## MT-MEM-031 — 🚨 `KnowledgeEndpoints` hiçbir ucunda `RequireApiKeyScope` çağırmıyor — yalnız-okuma anahtarı belge yazabiliyor/silebiliyor mu?

**Gerçek sonuç**
Şüphe **doğrulandı**. `KEY_JSON`'ın alanı doküman'ın varsaydığı `rawKey`
değil `plaintextKey` — doküman sapması, düzeltilip devam edildi. Adım 2
(yazma): `HTTP 500` — ama `403` DEĞİL; log embedding erişimi eksikliğinden
(`HATA` değil, bu şeridin bilinen ortam kısıtı) düştüğünü gösteriyor,
yani istek kapsam filtresini GEÇTİ ve ingestion mantığına ULAŞTI (403
hiç üretilmedi). Adım 3 (silme): `HTTP 204` — TAM beklenen gibi, kapsam
denetimi hiç devrede değil, `RunsRead` anahtarı bir belgeyi serbestçe
sildi. Adım 4 (kontrol grubu) ilk denemede `500` verdi çünkü dokümanın
kendi örnek gövdesinde `model` alanı eksikti (`AgentDefinitionRequest`
zorunlu alan) — bu, `HATA-S1-007` ile aynı kök nedene çarpan ayrı bir
doküman sapması; `model` eklenerek tekrarlandığında beklenen `HTTP 403`
(`detail: "Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami
tasimiyor."`) alındı. Sonuç: kontrol grubu (`AgentEndpoints`) kapsamı
doğru uyguluyor, `KnowledgeEndpoints` hiç uygulamıyor. **Kusur, Önem:
Yüksek** — doğrulandı, `HATA-S1-011` olarak kaydedildi.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-011 düzeltmesiyle (K-397) yeniden koşuldu: RunsRead anahtari artik DELETE'te 403 Kapsam yetersiz aliyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## Özet

| Bölüm | Case sayısı | Negatif/sınır |
|---|---|---|
| §1 Sıkıştırma: yapısal doğrulama | 5 (MT-MEM-001–005) | 4 |
| §2 Sıkıştırma: gerçek çalıştırma | 5 (006–010) | 1 (010, kontrol grubu) |
| §3 Bellek sağlayıcıları | 4 (011–014) | 1 (014, şüphe) |
| §4 RAG: belge CRUD | 11 (015–025) | 6 (021–025) |
| §5 RAG: agent entegrasyonu | 4 (026–029) | 2 (028–029) |
| §6 RAG: kiracı yalıtımı | 1 (030) | — (pozitif + kontrol grubu) |
| §7 Güvenlik: kapsam boşluğu | 1 (031) | 1 (şüphe) |
| **Toplam** | **31** | **~15 (%48)** |

---
