# 20 — Bağlam Sıkıştırma, Bellek Sağlayıcıları ve Anlamsal Arama (`MEM`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../20-BELLEK-RAG-BAGLAM.md`](../../20-BELLEK-RAG-BAGLAM.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin:
> `git log --follow -- <bu dosya>`

---

## Temiz geçen case'ler (19)

| Case | Durum | Başlık |
|---|---|---|
| MT-MEM-001 | ☑ | Tetikleyicisiz `SlidingWindow` derleme hatası verir |
| MT-MEM-002 | ☑ | `ContextWindow` stratejisi `MaxContextWindowTokens` olmadan derleme hatası verir |
| MT-MEM-003 | ☑ | Geçerli `SlidingWindow` tanımı `valid: true` döner (pozitif kontrol) |
| MT-MEM-005 | ☑ | Harness çakışma denetimi: `Disable*` bayrakları etkin ayarlarla çelişirse derleme hatası |
| MT-MEM-007 | ☑ | Sıkıştırılan mesajlar `conversation_items`'ta SİLİNMEZ (K-107) |
| MT-MEM-008 | ☑ | `Summarization` stratejisi: özet sonrası token sayısı azalır, çalıştırma toplamı pozitif kalır |
| MT-MEM-009 | ☑ | `Pipeline` stratejisi uçtan uca çalışır ve çökmez |
| MT-MEM-010 | ☑ | Sıkıştırma **kapalıyken** uzun konuşmada `HistoryCompacted` hiç üretilmez (kontrol grubu) |
| MT-MEM-015 | ☑ | Düz metinle belge yükleme: sunucu parçalar ve gömüler |
| MT-MEM-016 | ☑ | `GET .../documents` yüklenen kaynağı listeler |
| MT-MEM-017 | ☑ | Anlamsal arama KELİME EŞLEŞMESİ OLMAYAN bir sorguyla doğru parçayı bulur |
| MT-MEM-018 | ☑ | `DELETE .../documents/{sourceId}` kaynağı siler; sonraki arama onu döndürmez |
| MT-MEM-019 | ☑ | Hazır `chunks` ile (embedding VERİLMİŞ) yükleme: sunucu yeniden gömmez |
| MT-MEM-020 | ☑ | Aynı `sourceId` ile yeniden yükleme ESKİ parçaları değiştirir (upsert) |
| MT-MEM-026 | ☑ | `bilgi-asistani` uçtan uca: belge yükle → soru sor → `search_knowledge` tam bir kez çağrılır |
| MT-MEM-027 | ☑ | `VectorCollection` boş bırakılırsa koleksiyon adı olarak AGENT ADI kullanılır |
| MT-MEM-028 | ☑ | `EnableVectorSearch=true` + `IVectorSearchStore` kayıtlı değil → derleme hatası, sessizce boş sonuç DÖNMEZ |
| MT-MEM-029 | ☑ | `EnableVectorSearch=true` + PostgreSQL var ama `IEmbeddingGenerator` kayıtlı değil → derleme hatası |
| MT-MEM-030 | ☑ | Vektör arama kiracı yalıtımı: bir kiracının belgesi diğerinde görünmez |

## Ayrıntı taşıyan case'ler (12)

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
