# ADAYLAR — Normalize Edilmiş Planlama Kuyruğu

> **Durum (2026-08-26):** Bu dosya yalnız plana dönüşebilecek yetenekleri
> taşır. Eski listenin 43 açık görünen F-ID'si yeniden yargılandı: **5 aday**,
> **5 kusur**, **6 karar/uyumluluk eşiği**, **15 ölçüm bekleyen iddia** ve
> **12 arşivlenen veya birleştirilen kalem**. F-164 bu sayımdan önce kusur
> kanalında kapandı. Tam kanıt ve her ID'nin varış yeri:
> [`kesif/2026-08-26-aday-envanter-normalizasyonu.md`](kesif/2026-08-26-aday-envanter-normalizasyonu.md).
>
> **Ek (2026-08-26, ikinci tur):** Feature keşfi turu üç yeni aday ekledi
> (**F-166, F-167, F-168**) ve **F-95**'i karar kanalından adaylığa geri
> aldı — MAF 1.19.0 onu bekleten kancayı gönderdi. Tur kaydı:
> [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md).
>
> **Ek (2026-08-26, üçüncü tur — planlama):** Sıralama kanıt doğrulamasıyla
> yeniden yargılandı ve **F-109 · F-149 · F-166** plana dönüştü
> ([Faz 112](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) ·
> [Faz 113](113-ARIZA-SINIFLANDIRMA-SEAMI.md) ·
> [Faz 114](114-CALISTIRMA-ICI-BUTCE-TAVANI.md)); bölümleri bu dosyadan
> **silindi**. Doğrulama üç aday metnini de düzeltti — düzeltmeler
> § *Sıralamayı Değiştiren Ölçümler*'dedir.
>
> **Ek (2026-08-26, dördüncü tur — planlama):** **F-168 · F-67** plana dönüştü
> ([Faz 115](115-EVALIN-BASSIZ-KOSUCUSU.md) ·
> [Faz 116](116-PERFORMANS-TAHSIS-KAPISI.md)); bölümleri bu dosyadan silindi.
> Doğrulama ikisinin de aday metnini düzeltti — § *Sıralamayı Değiştiren
> Ölçümler*. Sıralamada **iki aday** kaldı.
>
> **Ek (2026-08-26, beşinci tur — planlama):** **F-167 · F-152** plana dönüştü
> ([Faz 117](117-MCP-TASKS-UZANTISI.md) ·
> [Faz 118](118-YARGIC-BASINA-CHECKPOINT.md)). **Sıralanabilir aday kalmadı.**
> Kuyrukta iki kalem var ve ikisi de bugün faz değildir: F-95 ölçüm bekler,
> F-165 tek faza sığmaz. Yeni aday üretmek için `aday-kesfi` koşulur.
>
> Faz durumu yalnız üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'dedir.
> Bir kusur bu dosyaya geri girmez; `kusur-giderme` kanalına gider. Kapatılmış
> kararın yeniden açılması kullanıcı kararıdır. Ölçüm bekleyen iddia, kanıt
> üretmeden aday olmaz.

## Okuma Sırası

**Sıralanabilir aday kalmadı** (2026-08-26). Bu dosyaya bakma sebebin
şunlardan biridir:

| İhtiyaç | Nereye bak |
|---|---|
| Sıradaki fazı seçmek | **Buraya değil** — üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'ye. Planlanmış fazlar `docs/` kökündedir |
| Yeni aday üretmek | `aday-kesfi` skill'ini koş; bu dosya onun çıktısını alır |
| Bekleyen iki kalemin durumu | § *Bekleyen Kalemler* — ikisi de bugün faz değildir |
| Bir F-ID nereye gitti | § *Aday Olmayan Açık Kayıtlar* tablosu |
| Bir sıralama neden değişti | § *Sıralamayı Değiştiren Ölçümler* |

Geçmiş tur anlatıları, kapanmış aday gövdeleri ve elenen kalemler bu dosyada
tekrarlanmaz; ilgili keşif ve arşiv kayıtlarındadır.

## Değerlendirme Ölçütleri

| Ölçüt | Soru |
|---|---|
| **Değer** | Bu olmadan AgentPrism'i kim kullanamaz? |
| **Maliyet** | Kaç paket, kaç yeni public tip, kaç migration? |
| **Risk** | Bir tasarım kuralını, AOT veya bundle bütçesini zorluyor mu? |
| **Hazırlık** | MAF veya .NET ekosisteminde hazır mı, sıfırdan mı? |

Bir adayın `Mercek` satırı aşağıdaki destekleyen mercekleri numarayla sayar.

| # | Mercek | Sorusu |
|---|---|---|
| 1 | **Benimseme** | İlk agent'a kadar geçen süreyi kısaltır mı? |
| 2 | **Üretim işletimi** | Gece 03:00'te nöbetçi mühendisin işine yarar mı? |
| 3 | **Kurumsal satın alma** | Hangi kurumsal kapıyı açar? |
| 4 | **Performans ve AOT** | Sıcak yol ve tahsis bütçesi korunur mu? |
| 5 | **API ergonomisi** | Yanlış kullanım derlemede yakalanır mı? |
| 6 | **Ekosistem yerleşimi** | Aspire, OTel, MCP, A2A ve DI ile doğal mı oturur? |
| 7 | **Ölçme–iyileştirme** | Üretim verisini geliştirmeye geri besler mi? |
| 8 | **Maliyet (FinOps)** | Tüketicinin model faturasını düşürür mü? |

## Sıralama — kuyruk boş

2026-08-26 itibarıyla **plana dönüşmeyi bekleyen sıralanabilir aday yoktur.**
Üç planlama turu dokuz adayın yedisini faza çevirdi:

| Aday | Faz |
|---|---|
| F-109 | [112 — Replay'in İstemci Tool Sözleşmesi](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) |
| F-149 | [113 — Sağlayıcı Arıza Sınıflandırmasının Genişleme Noktası](113-ARIZA-SINIFLANDIRMA-SEAMI.md) |
| F-166 | [114 — Çalıştırma-İçi Bütçe Tavanı](114-CALISTIRMA-ICI-BUTCE-TAVANI.md) |
| F-168 | [115 — Eval'in Başsız Koşucusu](115-EVALIN-BASSIZ-KOSUCUSU.md) |
| F-67 | [116 — Performans Tahsis Kapısı](116-PERFORMANS-TAHSIS-KAPISI.md) |
| F-167 | [117 — MCP Tasks Uzantısı](117-MCP-TASKS-UZANTISI.md) |
| F-152 | [118 — Yargıç Başına Checkpoint](118-YARGIC-BASINA-CHECKPOINT.md) |

Kalan ikisi § *Bekleyen Kalemler*'dedir ve **sıralamaya girmez**.

### Sıralamayı Değiştiren Ölçümler

Üç planlama turu (üçüncü, dördüncü, beşinci) kanıtı yeniden doğruladı
(`faz-planlama` Adım 1) ve aday metinlerini birikimli olarak düzeltti. Sıra
numaraları **ikinci turun** tablosuna göredir; plana dönen kalemler o tablodan
çıktı. Bu kayıt, bir kalem ileride yeniden açılırsa **hangi iddianın ölçümle
çürüdüğünü** korur.
Gerekçeler:

| Değişiklik | Ölçüm |
|---|---|
| **F-109 · 6 → 1** ve plana | Listedeki tek "kırık söz" kalemiydi: Faz 61 istemci tool'unu sevk etti, replay onu sessizce yarım bırakıyordu. Sınıf olarak K-627 ile aynıdır. Ayrıca aday metnindeki "kaydedilmiş sonucu oynat" seçeneği **imkânsız** çıktı — istemci tool sonucu `ToolInvocationRecord`'a hiç yazılmıyor. |
| **F-149 · 5 → 2** ve plana | Aday metni "seam tasarla" diyordu; ölçüm seam'in **yarısının zaten var olduğunu** buldu (`IRunErrorClassifier`, `TryAddSingleton` ile kayıtlı). Gerçek boşluk üç tane ve daha dar: retry'ın hiç seam'i yok, yerleşik sınıflandırıcı devralınamıyor, parmak izi hesabı erişilemez. |
| **F-166 · 1 → 3** ve plana | Karşı görüş ("ölçülmüş vaka yok") **düştü**: varsayılan kurulum 200 000 token'lık bir ağaç tavanı ilan ediyor ve o tavan tek agent'lı run'da hiçbir şey yapmıyor. Bu bir FinOps konforu değil, bir beyan hatası. Buna karşılık "kaçak döngü" gerekçesi **daraldı**: `HarnessSettings.MaximumIterationsPerRequest` bir iterasyon tavanı zaten veriyor; sayılmayan şey maliyet. |
| **F-167 · 2 → 3** | "SDK maliyeti zaten ödenmiş" bir talep kanıtı değil, yalnız bir indirimdir. Çalışma anı probu bayatlama korkusunu zaten çürüttü (sunucu bugün stateless). Geriye 1.0 öncesi **yeni bir NuGet paketi** almak kalıyor — burada en pahalı değişiklik türü budur. |
| **F-168 · 3 → 1** ve plana | Maliyet "Orta" yazılmıştı; ölçüm **küçük** buldu. İki HTTP çağrısı üretilmiş istemcide **zaten var**, eşik için gereken üç sayı (`Total`/`Passed`/`Failed`) sözleşmede var, CLI test altyapısı (`RealHttpHost` · `CliRunner`) hazır. Sunucu hiç değişmiyor; OpenAPI/TS/NSwag zinciri koşmuyor. |
| **F-67 · 2 → 2** ve plana | Kapsam gürültü ölçümüyle daraldı: CI kapısı **yalnız tahsis edilen bayt** olur (deterministik, sıfır tolerans), süre ölçülür ama kapı değildir. Yeni paketin ağırlığı gerçek restore ile sayıldı: BenchmarkDotNet 0.15.8 → **22 geçişli paket**. K-212'nin 37'sinden az ve — asıl fark — ölçüm projesi `IsPackable=false` olduğu için tüketiciye **hiç ulaşmıyor**. |
| **F-67 ile F-168 "aynı karar" iddiası zayıfladı** | Aday metni "F-67 ile **aynı** kararı ister" diyordu. Ölçüm bunu çürüttü: F-168 bir eşik **koymaz**, tüketiciden **alır** — AgentPrism kalite barı dayatmaz. F-67 ise bu depo için gerçek bir sayı seçmek zorundadır. Ortak olan yalnız "gürültülü kapı kurma" ilkesi; gürültünün kaynağı bile farklı (model belirsizliği ↔ paylaşılan CI makinesi). Bu yüzden **tek faz değil, iki ayrı faz** yazıldı. |
| **F-167 · 3 → 1** ve plana | En büyük maliyet iddiası ("Tasks extension'ı **yeni bir NuGet paketidir** ve geçişli ağırlığı sayılmalıdır") gerçek restore ile çürüdü: `ModelContextProtocol.Extensions.Tasks` 2.2.0 `.AspNetCore`'un üstüne **net 1 paket** ekliyor, geçişli ağırlık **sıfır** — on iki geçişli paketin tamamı zaten grafikte. Ayrıca `IMcpTaskStore` AgentPrism'in var olan run kaydı üzerine oturuyor: **yeni tablo ve migration gerekmiyor**. Buna karşılık ölçüm yeni bir risk buldu: SDK sözleşmesinde **kiracı parametresi yok** ve K-103'ün onay kontrolü run kuyruğa taşınınca handler'dan düşüyor. |
| **F-152 · 2 → 2** ve plana | Maliyet iddiası ("kalıcı model ve **üç SQL sağlayıcı migration'ı** gerekir") çürüdü: `UpsertAsync` **yargıç başına** çağrılıyor ve satır `Author = "judge:{ad}"` taşıyor; `IRunScoreStore.ListAsync` ve `JobRecord.Attempt` de zaten var. **Checkpoint bugün zaten veride duruyor** — eksik olan tek şey döngünün onu okuması. Yeni tablo, migration ve public yüzey **yok**. |
| **F-95 sıralamadan çıktı** | Dördüncü sıra, sahip olmadığı bir plan hazırlığını ima ediyordu. `Hazırlık` satırı zaten "🚨 İmza doğrulanmadı" diyor. |

## Bekleyen Kalemler

İkisi de **bugün faz değildir**. Gövdeleri, koşulları oluştuğunda plana
dönüşebilmeleri için burada duruyor.

| Kalem | Neden faz değil | Koşulu ne zaman oluşur |
|---|---|---|
| **F-95** | İmzası doğrulanmadı; ayrıca **experimental** bir MAF sözleşmesine 1.0 öncesi public yüzey bağlamak K-008'in ön sürüm sınırının tersidir | `maf-api-kesfi` imzayı doğrular **ve** F-141 ile karşılaştırma yapılır. Tercihen 1.0 sonrası |
| **F-165** | 1.650 case tek faza sığmaz; bağımsız faz olarak planlanırsa kuyruğu bitmez | Bağımsız faz olarak **hiç** planlanmaz. Her fazın dokunduğu alanın manuel ailesi o fazda otomatikleştirilir |



### F-95 · Agent düzeyinde kesinti/devam kancası (yeniden açıldı)

**Sorun:** Kesintiye uğramış bir agent turunu devam ettirmek için AgentPrism'in
agent yürütmesinin **içine** girebilmesi gerekir. Bu kalem şu ölçümle kapsam
dışına alınmıştı: *"MAF agent düzeyinde kanca vermiyor; kancayı AgentPrism
yazmak K3'ü zorlar. Kanca yalnız `Microsoft.Agents.AI.Workflows` içinde var."*

**Kapsam:** Önce MAF'ın yeni kanca sözleşmesinin AgentPrism'in ihtiyacını
gerçekten karşılayıp karşılamadığını ölç. Karşılıyorsa kancayı **doğrudan**
kullan; AgentPrism paralel bir kanca hiyerarşisi kurmaz (K3).

**Değer:** Kesintiye uğramış tur, MAF'ı sarmalamadan devam ettirilebilir.

**Mercek:** 2, 5, 6.

**Hazırlık:** **Ekosistemde ölçüldü (2026-08-26)** — MAF **1.19.0** sürüm notu
".NET: agent-hooks interception contract as a first-class experimental feature"
satırını taşır ([PR #7564](https://github.com/microsoft/agent-framework/pull/7564)).
Repo `Directory.Packages.props:19`'da **1.18.0**'dadır.
🚨 **İmza doğrulanmadı.** `faz-planlama` Adım 1'de `maf-api-kesfi` koşulmadan
bu kalem plana dönüşmez.

**Maliyet:** Ölçülmedi; MAF yükseltmesinin kendi maliyeti de sayılmalıdır.

**Risk:** Kanca **experimental** ilan edilmiştir. AgentPrism'in public yüzeyini
değişken bir MAF sözleşmesine bağlamak erken olabilir.

**Bağımlılık:** MAF 1.19.0'a yükseltme. F-141 (MAF-kancasız alternatif tasarım)
bu kalemin rakibidir — ikisi **birlikte** yargılanır, ikisi birden yapılmaz.

**Ekosistem:** 2026-08-26 — [MAF sürüm notları](https://github.com/microsoft/agent-framework/releases).

**Karşı görüş:** F-141 aynı ihtiyacı MAF'a hiç kanca takmadan karşılıyor ve
2026-08-21'de bu tasarımın **doğru** olduğu kaydedilmişti. Experimental bir
MAF yüzeyine bağlanmak, çalışan bir alternatifi elde varken net bir gerileme
olabilir.




### F-165 · Manuel kabul setinin CI'a kademeli taşınması

**Sorun:** Manuel set 37 Markdown dosyasında 1.650 case taşır ve CI'da koşmaz.
Regresyon güvencesi bir kişinin koşum zamanına bağlıdır.

**Kapsam:** Tek fazda tüm seti taşımak değil, bir aileyi test seviyeleri tablosuna
göre otomatikleştiren tekrar edilebilir devir şablonu kurmak. Manuel kalması
gereken model-yanıtı ve insan-yargısı case'leri açıkça ayrılır.

**Değer:** En yüksek riskli kabul davranışları insan zamanı beklemeden regresyon
kapısına girer; iki ayrı spec/test kaynağı oluşmaz.

**Mercek:** 2, 3, 4, 6.

**Hazırlık:** Ölçüldü — `find docs/manuel-test -maxdepth 1 -name '*.md'` 37 dosya,
case kimliği taraması 1.650 case verdi. `AgentPrism.Testing` ve Testcontainers
altyapısı zaten vardır.

**Maliyet:** Yüksek, fakat ilk dilim kontrollüdür.

**Risk:** Case'leri kör biçimde birim teste çevirmek test tiyatrosu üretir.
Sınır davranışı functional/integration seviyesinde kalmalıdır.

**Bağımlılık:** Yok; F-67 ile paralel gider.

**Ekosistem:** 2026-08-26 — depo kalite disiplini; dış ekosistem iddiası yok.

**Karşı görüş:** Model kalitesi ve görsel değerlendirme otomasyona uygun değildir.
Bu aday o case'leri silmeyi değil, otomatikleştirilebilir kısmı ayırmayı önerir.



## Aday Olmayan Açık Kayıtlar

Bu kalemler faz sıralamasına girmez. Tam kanıt, geçmiş ve sonraki adım keşif
kaydındadır.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Plana dönüştü** | F-109 → [Faz 112](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) · F-149 → [Faz 113](113-ARIZA-SINIFLANDIRMA-SEAMI.md) · F-166 → [Faz 114](114-CALISTIRMA-ICI-BUTCE-TAVANI.md) · F-168 → [Faz 115](115-EVALIN-BASSIZ-KOSUCUSU.md) · F-67 → [Faz 116](116-PERFORMANS-TAHSIS-KAPISI.md) · F-167 → [Faz 117](117-MCP-TASKS-UZANTISI.md) · F-152 → [Faz 118](118-YARGIC-BASINA-CHECKPOINT.md) | Bölümleri bu dosyadan silindi; kanıt ve tasarım faz dokümanındadır. Aday listesine geri dönmezler. |
| **Kapatılan kusur kayıtları** | F-106, F-130, F-137, F-138, F-139 | Kapanış kanıtı keşif kaydındadır; yeniden görülürse yeni kusur kaydı açılır. |
| **Karar / uyumluluk** | F-72, F-90, F-91, F-92, F-132, **F-169** | Mevcut karar veya dış bağımlılık değişmeden planlanmaz. F-95 2026-08-26'da adaylığa döndü. **F-169** (MAF CodeAct / Hyperlight sandbox) F-72 ile **aynı eşiktedir**: paket GA ve taşınabilir olana kadar planlanmaz — ölçüm [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md) § 9. |
| **Ölçüm bekliyor** | F-51, F-94, F-96, F-97, F-99, F-101, F-123, F-128, F-154, F-156, F-157, F-159, F-160, F-161, F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. |
| **Arşivlendi / birleştirildi** | F-48, F-88, F-89, F-98, F-144, F-145, F-146, F-147, F-148, F-155, F-158, F-163 | Plan değeri yok, rutin bakım olarak kalır veya aktif adayla aynı tasarım işidir. **F-155** F-149 ile aynı tasarım işiydi; o iş artık [Faz 113](113-ARIZA-SINIFLANDIRMA-SEAMI.md)'tedir. **F-163** F-67'nin ölçümüne bağlıydı; o ölçüm artık [Faz 116](116-PERFORMANS-TAHSIS-KAPISI.md)'dadır ve ayrı test ailesi olarak açılmaz. |

## Bilerek Önerilmeyenler

Reddedilmiş mimari işler için tek kaynak
[`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)'dir.
Özellikle F-91 `secret` saklama sınırını ve F-92 dağıtık hız sınırı/Redis
kararını değiştirmeden yeniden aday olmaz. **F-95 bu listede değildir** — onu
bekleten şey bir tasarım kararı değil, MAF'ta kancanın bulunmamasıydı; MAF
1.19.0 o kancayı gönderdiği için 2026-08-26'da adaylığa döndü.
