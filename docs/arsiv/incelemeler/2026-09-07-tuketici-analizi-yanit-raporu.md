# Tracon — Bulgu Kapanış Raporu

**Yanıt verilen belge:** [Tracon — Kapsamlı Teknik ve Ürün Analizi](2026-09-07-tuketici-analizi-girdi-raporu.md) (1005 satır)
**Girdi SHA-256:** `5127dd2a98ad24792c8de8c57baafae9bf70151a428c2402e14c7101f22aec1c`
**Ölçüm zemini:** `2a3f5cff0f43078d7c41d5faf24ad45cb7131a8e`; son kapalı faz 154
**Ölçüm tarihi:** 7 Eylül 2026 · **Sevkiyat:** 8 Eylül 2026
**Ürün durumu:** Yayımlanmamış geliştirme sürümü — NuGet veya npm'de sürüm yok

Bu belge, analizinizin her bulgusunun koda karşı ölçülmesinin sonucudur. Neyin
düzeltildiğini kanıtıyla, neyin düzeltilmediğini gerekçesiyle söyler. Ölçümde
çürüyen iddiaları — kimin iddiası olursa olsun — adıyla anar.

Bu bir yayın onayı veya güvenlik sertifikası değildir.

---

## 1. Kapanış tablosu

| Ölçüm | Sonuç |
|---|---|
| Doküman kalemi (D01–D18) | **18** — 16 düzeltildi · 1 çürütüldü · 1 zaten doğruydu |
| Kusur kanalı | **3** — üçü de kapandı; ikisi karar defterine girdi |
| Kapatılan sessiz alan kaybı | **8 alan**, üç katmanda; artık üç kapı koruyor |
| Faza dönen öneri | **2** (A04, A16) — üç faz planlandı ve **üçü de sevk edildi** |
| Ölçüm bekleyen öneri | **14** — talep kanıtı veya ürün kararı bekliyor |
| Bu tur ertelenen | **2** (A17, A18) |

Analizinizin merkezî tezi — *dokümantasyon tutarlılığı ciddi bir sorundur* —
doğrulandı ve tahmin ettiğinizden biraz daha genişti. Mevcut kapılar yeşil
yanarken üç sayfa yanlış operasyon sayısı, bir sayfa yanlış tag sayısı
taşıyordu. Bunun ötesinde dört ayrı sayfa tüketiciyi **yanlış davranışa**
yönlendiriyordu: yanlış API key scope'u, yanlış worker varsayılanı, yanlış
kiracılık varsayılanı ve gereksiz ikinci bir çalıştırma başlatma tarifi.

Buna karşılık önerilerinizin tamamını 1.0 öncesi zorunlu işe çevirmedik.
Gerekçeler §4'te kalem kalem yazılıdır; hiçbiri "sonra bakarız" değildir.

---

## 2. Kapatılan üç kusur

### B01 — Definition düzenlemesinde sessiz alan kaybı

`PUT /api/agents/{name}` tam değiştirme semantiğinde çalışıyor ve mevcut kaydı
merge etmiyor. Bu, DTO'nun ifade edemediği her alanın "korunmuş" değil
**silinmiş** olması demektir. Ölçülen kayıp sekiz alan ve üç ayrı katman:

| Katman | Kaybolan alanlar |
|---|---|
| HTTP DTO | `SubAgents` · `McpResourceUris` · `Metadata` |
| SQL payload | `SubAgents` · `McpResourceUris` |
| Konsol düzenleyicisi | `parameters` · `sharedInstructionsName` · `model.providerSettings` · `model.responseCache` · `model.allowConcurrentToolCalls` · yalnız vector search açıkken tüm `memory` bloğu |

In-memory store `definition with` kopyası aldığı için aynı kayıt iki backend'de
farklı korunuyordu — kusur SQL'e değil **mapper'a** aitti.

Kalıcı çözümde PUT'un anlamı korundu ve DTO tamamlandı. Sunucu taraflı merge
reddedildi: "gönderilmedi" ile "boşalt" ayrımını yok eder ve bir listeyi kasten
boşaltmayı ifade edilemez kılar.

Kapı çift kurgulandı. Bir test property'nin **varlığını**, diğeri gerçek bir SQL
round-trip ile **atandığını** ölçer. İkisi de gerekli, çünkü property'yi eklemek
ile mapper gövdesinde kullanmak iki ayrı adımdır ve derleyici ikincisini istemez.

> Karar **K-725** · `AgentDefinitionRoundTripTests` ·
> `AgentDefinitionPayloadRoundTripTests` · konsol tarafında `model.test.ts`

### B03 — Approval kararı ile devam arasındaki hata penceresi

**Bu kalem raporunuzda yok; ölçüm sırasında biz bulduk ve failure injection ile
ürettik.**

Bir onay kararı dört ardışık yazımdır: denetim izi, kararın uygulanması, devam
çalıştırmasının açılması, işin kuyruğa alınması. Üçünü kapsayan bir transaction
yoktur.

İkinci adımdan sonra gelen bir hata, kararı *uygulanmış* ama işi *hiç
başlamamış* bırakıyordu. Tekrar denemek `409 AlreadyDecided` alıyordu: çağırana
iş bitti deniyor, oysa hiç başlamamış. Kurtaran başka bir mekanizma da yok —
çalıştırma mutabakatı yalnız `Running` satırları toplar ve varsayılanı kapalıdır.

Çözüm transaction değil **tekrar sürülebilirlik** oldu. Devam çalıştırmasının
kimliği artık approval'dan deterministik türetiliyor; bu yüzden aynı kararı
tekrarlamak ikinci bir çalıştırma yaratmıyor, yarım kalan devri tamamlıyor. Ters
cevapla gelen ikinci karar hâlâ `409` döner.

> Karar **K-726** · `ApprovalResumeHandoffTests` (kill-after-decide reprosu)

### B02 — Yanlış tüketici sözleşmesi

D01–D18'in tamamı ele alındı; dispozisyon §3'tedir. Bunun yanında iki iş yapıldı:

**Yayın durumu düzeltildi.** `CHANGELOG` yayımlanmamış bir sürümü "initial public
preview release" diye anıyordu. Sebebi teknikti: site üreteci, yayımlanmış bir
sürüm olmadan sayfa üretmeyi reddediyordu ve bu, changelog'u olmayan bir sürümü
ilan etmeye zorluyordu. Üreteç artık "henüz yayımlanmadı" durumunu meşru bir
durum olarak işliyor; kurulum yönergelerinin yanında bu uyarı duruyor.

**Sevk edilen XML'de ayrı bir sınıf bulundu.** On dört ayrı yerde "bu fazda",
"sonraki fazda" gibi **geliştirme takvimi dili** tüketiciye gidiyordu. Bunların
hiçbiri raporunuzda yoktu; sınıf taraması çıkardı. Sevk edilen metnin iç geçmişe
atıf yapmasını yasaklayan mevcut kapı yalnız numaralı biçimi ("faz 65")
yakalıyordu; kapı numarasız biçimi de yakalayacak şekilde genişletildi.

---

## 3. Doküman kalemleri — D01–D18 dispozisyonu

| Kalem | Durum | Ne yapıldı |
|---|---|---|
| **D01** | ✅ Düzeltildi | Üç sayfadaki operasyon sayısı (143 · 143 · 162) gerçek değere (**165**) çekildi. Kapı artık tek sayfayı değil **her elle yazılan sayfayı** tarıyor |
| **D02** | ✅ Düzeltildi | Tag sayısı 19 → **23** alan tag'i; ortak tag ayrı sayılıyor ve ölçüm belgeden üretiliyor |
| **D03** | ✅ Düzeltildi | "Referansta var" ile "sizin süreçte açık" iki ayrı sütuna ayrıldı. Diagnostics ucu referansta **vardır**; açık olup olmadığı yapılandırmaya bağlıdır |
| **D04** | ✅ Düzeltildi | Ekran ölçümünün **tanımı** düzeltildi: kapı import modülü sayıyordu (28), sayfalar kullanıcının gördüğü ekranı (30). Kapı artık sayfaların iddia ettiği şeyi ölçüyor |
| **D05** | ✅ Düzeltildi | "Varsayılan çok kiracılı" yanlıştı. Çok kiracılık **opt-in**; varsayılan tek kiracıdır ve her istek varsayılan kiracıya çözülür |
| **D06** | ✅ Düzeltildi | Kuyruğun tüketilmesi için `UseScheduling()` şart değil; worker **varsayılan olarak açıktır**. İlerlemeyen kuyruk, worker'ı kapatılmış bir dağıtımdır |
| **D07** | ✅ Düzeltildi | Sürüm aidiyeti belirsizliği yayın durumu düzeltilerek kapandı; CLI yetenek listesi gerçek komut kümesine çekildi |
| **D08** | ✅ Düzeltildi | OpenAI uyumlu uçlar `ExternalInvoke` değil **`RunsWrite`** ister; okuma uçları `RunsRead`. Yalnız `ExternalInvoke` ile verilmiş bir anahtar bu uçlarda reddedilir |
| **D09** | ✅ Düzeltildi | `ExposeAllAgents` ve `ToolNamePrefix` yalnız **MCP sunucusunun** ayarlarıdır; A2A'da karşılıkları yoktur |
| **D10** | ✅ Düzeltildi | "Doğrulama yok" iddiası **varsayılan için** doğruydu. Varsayılan ile opt-in ayrıldı; şema doğrulamasının hiçbir zaman yerleşik olmadığı ayrıca yazıldı |
| **D11** | ✅ Düzeltildi | Paket kimliği tekilliği **resmî** yayın hattı için geçerlidir. Yerel veya harici bir build aynı sürüm dizesini farklı içerikle taşıyabilir; iki sayfa artık aynı şeyi söylüyor |
| **D12** | ⚠️ **Çürüdü** | Kod kusuru bulunmadı; ayrıntı §5'te. Yalnız anlatım netleştirildi |
| **D13** | ✅ Düzeltildi | Karar sonrası **ikinci bir çalıştırma başlatmayın**. Karar ucu devam çalıştırmasını kendisi kurar. Üç ayrı akış (dayanıklı kutu · in-band · workflow) ayrı ayrı yazıldı. B03 bu davranışı ayrıca sağlamlaştırdı |
| **D14** | ➖ Zaten doğru | Site sınırı zaten doğru anlatıyordu: iki istisnayı adlandırıyor, altı kapıyı sayıyor ve "Tracon sandbox uygulamaz" cümlesini açıkça kuruyor. Değişiklik yapılmadı |
| **D15** | ✅ Düzeltildi | SQLite lease sözleşmesini uygular, ama **cross-instance backend değildir**. Çok node örneği artık yalnız PostgreSQL/SQL Server ile yazılıyor |
| **D16** | ✅ Düzeltildi | "Telemetri çıkmaz" mutlak ifadesi daraltıldı: Tracon kendi telemetrisini göndermez; sizin kaydettiğiniz exporter ile sağlayıcı/MCP/webhook çağrıları elbette ağ kullanır |
| **D17** | ✅ Düzeltildi | "Tek satırda konsol" ayrıldı: tek satır **çalışma zamanını** ayağa kaldırır; konsol ayrı paket ve ayrı iki çağrıdır |
| **D18** | ✅ Düzeltildi | Sıra numarası yalnız görüntüleme sırasıdır; koşumlar **kimlikle** karşılaştırılır. Yeniden sıralamak eski sonucu başka bir soruya hizalamaz |

---

## 4. Yapılmayanlar ve gerekçeleri

On sekiz önerinin ikisi faz planına dönüştü, on dördü kanıt bekliyor, ikisi
ertelendi. Gerekçeler kategoriktir; her birinin altında birden çok öneri vardır.

### 4.1 Altyapısı zaten var — A01 · A15 (kısmen)

Yayın zincirinin çekirdeği mevcut: sürüm ve commit taşıyan manifest, paket
hash'leri, kirli ağaçtan paketlemeyi reddeden build. Eksik olan altyapı değil
**anlatıydı** — o da düzeltildi.

Benzer şekilde arama sonuçlarında kaynak kimliği ve parça bilgisi zaten vardır;
"provenance hiç yok" tespiti fazla genişti. Eksik olan kullanıcı bazlı erişim
süzgecidir, kaynak izlenebilirliği değil.

### 4.2 Bilinçli ürün sınırı — A06 (kısmen) · A09 · A13 (kısmen)

Bazı öneriler yazılı bir kararla çelişiyor.

**Eval case içeriğinin koşum başına saklanması** kapsam dışında bırakılmıştı. Bu
tur kararın yeniden açılması **ayrıca değerlendirildi ve karar korundu**: kod
durumu karar günündekiyle aynı, ekosistem değişmedi, ve HTTP yolu zaten
değiştirilmiş bir case'i yeni kimlikle işaretliyor — yani sessiz yanlış eşleşme
üretmiyor, fark `Added`/`Removed` olarak görünüyor.

**MCP token'ının şifrelenerek Tracon veritabanına yazılması** reddedildi:
`secret` değerinin bu veritabanına yazılmaması bir güvenlik sınırıdır ve
şifreleme onu kaldırmaz. Host'a ait bir token store seam'i bu sınırı korurken
aynı ihtiyacı karşılayabilir; o seçenek açıktır.

### 4.3 Sorumluluk host'a ait — A03 · A08 · A11 (kısmen) · A15

Ödeme işleminin tekilliği, sert bütçe bakiyesi, belge bazlı erişim denetimi ve
script izolasyonu — dördü de **alan sahibinin** sözleşmesidir. Bir kontrol
düzlemi bunları taklit ederse ikinci bir hakikat kaynağı yaratır ve host'un
kendi denetimini zayıflatır.

Tracon'in verebileceği şey dar bir seam'dir, genel bir çözüm değil. Somut
bir kullanım ölçüldüğünde o seam tasarlanır.

### 4.4 Talep kanıtı yok — A03 · A06 · A08 · A10 · A11 · A13 · A15

Bu yedi kalemin hiçbirinde bugün somut bir sürücü yok. Bu bir değer yargısı
değil, bir **sıra** kararıdır: kanıtsız inşa edilen altyapı yanlış şekli alır ve
sonradan düzeltmek daha pahalıya gelir.

Her biri için hangi kanıtın aranacağı kayda geçti — örneğin dış etki üreten
gerçek bir domain akışı, ya da faturalı çok kiracılı bir kurulum.

### 4.5 Verilemeyecek garanti

Exactly-once agent yürütmesi, prompt-injection'a kapalı bir guard ve tüm ürün
için AOT — üçü de verilemeyecek sözlerdir. Dış sistem sınırları, model
belirsizliği ve paket matrisi bunlara izin vermiyor.

Verilebilecek şey daha dar ve daha dürüsttür: idempotency anahtarı, desen
tabanlı guard'ın **ne olmadığının** yazılması, ve AOT uyumlu paket listesinin tek
tek sayılması.

### 4.6 Bu tur ertelendi — A17 · A18

Genel workflow motoru ve ileri deney analizi mimari ret **değildir**. İkisi de
büyük ve geri dönüşü pahalı sözleşmelerdir; raporunuz bunlar için mevcut
yeteneklerle çözülemeyen somut bir akış göstermiyor. Böyle bir vaka çıkarsa ayrı
bir keşifle yeniden açılırlar.

### 4.7 Öncelik çerçevesi — tüm P0 önerileri

Raporun P0 kalemlerinin tamamını 1.0 kapısı yapmadık. Salt okuma yapan tek
süreçli bir agent ile kritik dış etki üreten bir filo aynı garantilere ihtiyaç
duymaz.

1.0 için zorunlu saydığımız şey daha dardır: doğru dokümantasyon, yapılandırma
bütünlüğü, tanımlı güven sınırlarının çalışması, sürüm ve upgrade politikası, ve
yeniden üretilebilir artifact.

---

## 5. Ölçümde çürüyen iddialar

Bu bölüm rapor nezaketi için değil, kayıt için vardır: çürümüş bir iddia yazıya
geçmezse altı ay sonra geri döner.

| İddia | Kimin | Ölçüm |
|---|---|---|
| **D12** — TypeScript istemcisinin üretildiği belge prefix taşımıyor | Analiz | **Çürüdü.** Belge prefix'i *taşıyor*; üretim onu tek kullanımlık bir ara girdide siliyor, böylece üretilen yollar mount'tan bağımsız kalıyor. Kod kusuru yok |
| **D14** — Kod-dışı içerik saklama sınırı yanlış anlatılıyor | Analiz | **Değişiklik yok.** Site bunu zaten doğru anlatıyor; iki istisnayı, altı kapıyı ve "sandbox uygulanmaz" cümlesini içeriyor |
| **A04** — Önceki sürümün ürettiği durum corpus'u test ağacında yok | **Bizim ölçüm raporumuz** | **Çürüdü.** Gerçek koşumdan yakalanmış oturum ve checkpoint corpus'ları, onları okuyan bir test ve bir fixture yenileme yasağı zaten sevk edilmiş. Corpus MAF 1.18.0, bugünkü pin 1.20.0 — çapraz sürüm kanıtı fiilen koşuyor |
| **F-210** — Skor fazı evaluator kataloğunun engelini kaldırdı | **Bizim ölçüm raporumuz** | **Çürüdü.** Kalkmamış: skor *kaydı* adlı ve tipli hâle geldi, ama yargıcın *dönüş* tipi hâlâ tek bir sayı taşıyor. Köprü bedava değil |

**Yöntem notu.** Son iki satır kendi ölçüm raporumuzun hatasıdır ve faz
planlaması sırasındaki zorunlu kanıt doğrulaması adımında yakalandı. İkisi de
düzeltilmeseydi, biri **zaten var olan bir şeyi** yeniden inşa eden, diğeri **var
olmayan bir kolaylığa** güvenen bir faz planlanacaktı.

---

## 6. Öneriden doğan üç faz — sevk edildi

İki öneri doğrudan faz planına dönüştü. Üçüncü faz, bunları planlarken yapılan
doğrulamanın ortaya çıkardığı bir sözleşme boşluğunu kapattı. **Üçü de 8 Eylül'de
tamamlandı**; aşağıdaki satırlar plan değil, teslim edilen iştir.

| Faz | Kaynak | Teslim edilen |
|---|---|---|
| **155** — Kalibre Edilmiş Evaluator Kataloğu | A18'in dar dilimi | Yargıç sözleşmesi çok adlı skora genişletildi; kayıtlı bir evaluator artık **metrik başına** ayrı skor satırı yazıyor. Ölçüm üretmeyen metrik `null` yazar, `0` değil. Hiçbir evaluator kaydedilmediğinde davranış birebir aynı kalır. MAF imzaları `maf-api-kesfi` ile ölçüldü ve **üç tespit planı değiştirdi** |
| **156** — Durum Ön Kontrolü ve Upgrade Penceresi | A04 (daraltıldı) | `tracon state-check` komutu sevk edildi: operatör, yükseltmeden önce **kendi veritabanına** kuşak sayımı ve salt okunur çözme denemesi sorabiliyor. Okunamaz kuşakta çıkış kodu `3`. Hiçbir şey yazmadığı öncesi/sonrası karşılaştırmayla, iki sağlayıcıda ve gerçek koşumda kanıtlandı. Upgrade penceresi ve başarısız restore prosedürü yayımlandı |
| **157** — Sınırlı Yük ve İki Process Arıza Kanıtı | A16 | Gerçek SQL yükü, iki process kill/devralma senaryosu ve **altı arıza manifesti** (veritabanı · yavaş sink · sağlayıcı zaman aşımı · retention hacmi · streaming fan-out · rolling upgrade) koşuldu. Dış model gecikmesi kontrol düzlemi overhead'inden ayrı raporlanıyor |

### 6.1 A16 önerisi kapsam dışı bir kusur ortaya çıkardı

Bu, öneriniz için en güçlü kanıt olduğu için ayrıca yazıyoruz.

Faz 157'nin *sağlayıcı zaman aşımı* manifesti yazılırken, hiçbir testin görmediği
bir kusur ortaya çıktı. Bir `IChatClient` zaman aşımını `TaskCanceledException`
(iç `TimeoutException`) olarak bildirir — `HttpClient` kendi istek zaman aşımını
tam olarak böyle bildirir ve her resmî sağlayıcı SDK'si `HttpClient` üzerindedir.

**Düzeltmeden önceki davranış:** çalıştırma `Canceled` + `error: null` olarak
kaydediliyordu, uç **`200` ve boş gövde** dönüyordu, ve fallback zinciri sonraki
halkayı **denemiyordu**. Yani bir sağlayıcı kesintisi hiçbir arıza panosunda
görünmüyor, çağıran ise onu **başarı sanıyordu**.

**Kök sebep:** iptal kararı istisnanın *tipinden* okunuyordu, oysa doğru kaynak
token'ın kendisidir. Düzeltme yedi yerde uygulandı (kayıt eden agent, fallback
zinciri, dört HTTP uç ailesi, workflow koşucusu, hata sınıflandırıcı) ve sınıf
taraması `catch (OperationCanceledException)` yakalayan 32 yeri kapsadı.

Bu kusur ancak arıza senaryosu **koşulduğu için** görüldü. Raporunuzun "tasarım
kapsamı üretim kanıtı değildir" tezinin doğrudan doğrulamasıdır.

## 7. Açık kalanlar ve aynı gün kapatılanlar

Raporun ilk hâlinde üç kalem açıktı. İkisi **8 Eylül'de kapandı**, biri plana
bağlandı, ve üçüncüsünü kapatırken raporunuzun §11.4'ünü doğrulayan ayrı bir
kusur çıktı.

### 7.1 Kapandı — manuel kabul setinin sayımı

İndeks 1488 case yazıyordu, gerçek 1597'ydi (36 ailenin 17'si bayat). Sapmanın
birikmiş bir temizlik işi olmadığı ölçüldü: kapanış protokolü indeksi yalnız
"yeni alan dosyası açılırsa" güncelletiyordu, yani mevcut bir aileye case
eklemek sayacı hiç güncellemiyordu. Bir faz tek başına 170 satır case ekleyip
indekse dokunmamıştı.

Üçü birlikte kapatıldı: **kapı** (sayım artık doküman denetiminde), **17 satırın
düzeltilmesi**, ve **protokol adımının düzeltilmesi**. Yalnız kapı eklemek her
fazda bir kırmızı üretir ve insanları onu susturmaya eğitirdi.

### 7.2 Kapandı — bağımlılık sürümü damgalı iddialar

Sevk edilen XML'de bir bağımlılık sürümünü adıyla anıp o sürümde ölçülmüş bir
davranış iddia eden **yedi** yorum vardı (kayıtta beş yazıyordu). Bunlar
yeniden hesaplanamaz, ama damgadaki sürüm pinle karşılaştırılabilir. Kapı
yazıldı ve iki parçalıdır: kayıtsız bir damga da kırar, yoksa yeni damga
sessizce dışarıda kalır.

### 7.3 Plana bağlandı — davranış iddialarının kapısı

Sayılabilir iddialar kapı altında; **varsayılan ve policy** iddiaları değil.
Sitede 147 varsayılan iddiası var, mekanik hedeflenebilir olan 9'u. Bu turda
elle düzeltilen dört iddianın (kiracılık, worker, scope, approval) dördünü de
kapı değil insan bulmuştu. Kapı ayrı bir faz olarak planlandı.

### 7.4 🚨 F-198'i kapatırken daha ağır bir kusur çıktı — kapatıldı

Raporunuzun §11.4'ü "üretilen istemcinin SSE/media sınırı"nı işaret ediyordu.
Ölçüm bundan **daha ağırını** buldu: `/v1/responses` ve `/v1/chat/completions`
OpenAPI belgesinde **`requestBody` hiç ilan etmiyordu**.

İkisi de gövdeyi elle okuyor ve `.Accepts<T>` bildirimi yoktu. .NET gövde
şemasını endpoint'in **parametre tipinden** türetir; parametre kaldırılınca
türetecek bir şey kalmaz. Sonuç, akış şeklinden bağımsızdı:

- .NET istemcisinin metotları **gövde parametresi almıyordu**
- TypeScript istemcisi bu iki uç için `requestBody?: never` diyordu — yani
  "gövde gönderemezsin"

Yani bu iki uç **tipli istemcilerden hiç çağrılamıyordu**. Düzeltildi; her iki
istemci de yeniden üretildi ve artık gövde alıyor. Sınıf bir kapıyla kapatıldı:
her yazma operasyonu ya gövdesini ilan eder ya "gövdesiz" kaydında gerekçesiyle
durur — yük bilerek terstir, çünkü hangi handler'ın gövde okuduğunu güvenilir
biçimde tespit etmek çözülmemiş bir kaynak ayrıştırma problemidir.

F-198'in asıl konusu olan **akışlı şekil** ayrı bir faz olarak planlandı: iki
uç için ikinci bir akış metodu üretilecek, seçim derleme anında olacak.

## 8. Doğrulama

Tüm değişiklikler dört doğrulama kapısından **sıfır uyarıyla** geçti: derleme,
tam test paketi, paketleme ve biçim denetimi, artı site içerik kapısı. Doküman
tarafında kırık bağlantı sıfır, üretilen dosyalar taze.

İki kapı bu turda **kırmızı yandı ve düzeltildi**:

1. Test fixture'ına giren bir Türkçe dize, kaynak dili taban çizgisini
   büyütüyordu. Fixture dili değiştirildi.
2. Üretilen .NET istemcisinin belgesiz üye sayısı bir arttı. Sebep ölçüldü: yeni
   alan, mevcut kardeşleri gibi `$ref` tipli olduğu için üreteç ona açıklama
   yazmıyor. Bu **kaybolmuş bir açıklama değil**; taban çizgisi bilinçli
   güncellendi.

> 66 dosya · 2917 ekleme · 332 silme · üç yeni kapı testi

---

**Tracon · Bulgu kapanış raporu · 7 Eylül 2026**
Ölçüm zemini `2a3f5cff` · Girdi raporu SHA-256 `5127dd2a…22aec1c`
Ürün yayımlanmamış geliştirme sürümüdür.
