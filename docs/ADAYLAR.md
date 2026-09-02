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
> [Faz 113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) ·
> [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md)); bölümleri bu dosyadan
> **silindi**. Doğrulama üç aday metnini de düzeltti — düzeltmeler
> § *Sıralamayı Değiştiren Ölçümler*'dedir.
>
> **Ek (2026-08-26, dördüncü tur — planlama):** **F-168 · F-67** plana dönüştü
> ([Faz 115](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) ·
> [Faz 116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md)); bölümleri bu dosyadan silindi.
> Doğrulama ikisinin de aday metnini düzeltti — § *Sıralamayı Değiştiren
> Ölçümler*. Sıralamada **iki aday** kaldı.
>
> **Ek (2026-08-26, beşinci tur — planlama):** **F-167 · F-152** plana dönüştü
> ([Faz 117](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) ·
> [Faz 118](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md)). **Sıralanabilir aday kalmadı.**
> Kuyrukta iki kalem var ve ikisi de bugün faz değildir: F-95 ölçüm bekler,
> F-165 tek faza sığmaz. Yeni aday üretmek için `aday-kesfi` koşulur.
>
>
> **Ek (2026-09-01, tüketici turu):** Dış bir tüketici raporu koda karşı
> ölçüldü ([`kesif/2026-09-01-tuketici-feature-talepleri.md`](kesif/2026-09-01-tuketici-feature-talepleri.md)).
> Dört kalem **doğrudan plana** dönüştü — bu dosyada hiç sıralanmadılar, çünkü
> kanıtları raporla birlikte geldi ve aynı turda doğrulandı:
> **F-172** → [Faz 129](arsiv/fazlar/129-IS-KUYRUGU-LANELERI.md) · **F-173** →
> [Faz 130](arsiv/fazlar/130-URETILEN-SEMANIN-KISITLARI.md) · **F-174** →
> [Faz 131](arsiv/fazlar/131-YAPISAL-YANIT-DOGRULAMA-SEAMI.md) · **F-175** →
> [Faz 132](arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md). Aynı turdan **dört kalem**
> § *Bekleyen Kalemler*'e girdi (F-176 · F-177 · F-178 · F-179); hepsi bir
> fazın tamamlanmasını bekliyordu. **Ek (2026-09-02):** Faz 129-132 kapandı ve
> üçü plana dönüştü — **F-178'in job/kuyruk metrikleri yarısı** →
> [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md) · **F-177** →
> [Faz 134](134-SINIRLI-YANIT-ONARIMI.md) · **F-176** →
> [Faz 135](135-URETILEN-SEMANIN-NESNE-GRAFI.md). Kuyrukta **F-178'in kalan
> yarısı** (model deneme telemetrisi) ve **F-179** (dinamik routing) kaldı;
> ikisi de gerçek üretim trafiği/olayı bekliyor.
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
| F-149 | [113 — Sağlayıcı Arıza Sınıflandırmasının Genişleme Noktası](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) |
| F-166 | [114 — Çalıştırma-İçi Bütçe Tavanı](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md) |
| F-168 | [115 — Eval'in Başsız Koşucusu](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) |
| F-67 | [116 — Performans Tahsis Kapısı](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md) |
| F-167 | [117 — MCP Tasks Uzantısı](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) |
| F-152 | [118 — Yargıç Başına Checkpoint](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md) |

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
| **F-178** | Job/kuyruk metrikleri yarısı [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md)'e gitti. Kalan yarı (model deneme telemetrisi) tüketicinin kendi ölçütüne göre bekler | Gerçek bir üretim fallback gecikmesi olayı ölçülür |
| **F-179** | Ön koşulu yok: `run` satırı sağlayıcıyı saklamıyor, kayan latency penceresi ölçülmüyor | [Faz 132](arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md) kapanır **ve** F-178 attempt süresini ölçmeye başlar **ve** gerçek üretim trafiği oluşur |



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



### F-170 · Store audit kapsamının tamamlanması

**Sorun:** `IAgentSkillStore`'un hiçbir `Auditing*` decorator'ı yok — skill
kaydı ve silme denetim izine hiç yazmıyor. Kardeşleri (`IAgentDefinitionStore`,
`ISkillScriptGrantStore`) yazıyor. Faz 121'in bağımsız denetimi buldu ve 🟢
olarak devretti.

**Kapsam:** Hangi store'ların audit decorator'ı olduğunu ve olması gerektiğini
ölçmek, boşlukları kapatmak. Tek vaka değil sınıf: `Auditing*` deseninin
kapsadığı ve kapsamadığı store'lar bir tabloya çıkarılır.

**Değer:** Denetim izi eksiksiz olmayan bir kayıt, compliance için denetim izi
olmamasıyla aynı yerdedir — kısmi kapsam yanlış güven verir.

**Mercek:** 3, 6.

**Hazırlık:** Faz 121 denetim bulgusu #2. Ölçülmedi — hangi store'ların
decorator taşıdığı sayılmalı.

**Maliyet:** Orta; runtime davranış eklentisi, doküman işi değil.

**Risk:** Audit yazımının kendisi bir hata yolu üretir — `AuditRecorder`'ın
mevcut "audit yazamazsa akış durmaz" sözleşmesi korunmalıdır.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-28 — iç kalite kaydı; dış ekosistem iddiası yok.

**Karşı görüş:** Kapsam bilinçli olabilir — skill kaydı düşük riskli sayılmış
olabilir. Aday, önce **ölçmeyi** öneriyor; boşluk kasıtlıysa gerekçesi yazılır.

### F-171 · Sevk edilen metindeki ölçülmüş sayılar için kapı

**Sorun:** Sevk edilen metin, koddan **elle kopyalanmış** ölçüm sayıları taşıyor
ve hiçbir kapı onları doğrulamıyor. `preview.1` öncesi drift taraması (2026-08-28)
**altı** bayat sayı buldu ve üçü birbiriyle çelişiyordu:

| İddia | Sevk edilen değer(ler) | Ölçülen gerçek |
|---|---|---|
| Konsol ekranı | 27 (×2), 28 (×3) | **30** (`app.tsx` `*Screen` importları) |
| Konsol rotası | 33, 36 | **36** (`app.tsx` `pattern:` sayısı) |
| JS bundle gzip | 180.2 KB, 165.8 KB, 169.4 KB | **175.9 KB** (`npm run build`) |
| İstemci operasyonu | 161 (×2) | **162** (`agentprism.json`) |
| Store contract'ı | "32 others" / "29 more" | **31** (32 dosya − ortak taban) |
| Kök README paket tablosu | 19 satır | **20** paket |

Hepsi K-483'ün sınıfı: elle tekrarlanan bir ölçüm, terim değişince sessizce
yanlışa döner. Beş yüzey (kök README, üç paket README'si, iki site sayfası)
birbirinden habersiz kopya taşıyordu.

**Kapsam:** Bu sayıları koddan türeten bir kapı. En küçük hâli
`check-content.mjs`'e bir kontrol eklemektir: sevk edilen metindeki işaretli
sayıları (`app.tsx`, `agentprism.json`, `postbuild.mjs` çıktısı, packable
`.csproj` kümesi, `Contracts/` envanteri) yeniden hesaplayıp karşılaştırır.
Alternatif: sayıyı metinden **çıkarmak** — kapı yazmak yerine iddiayı
kaldırmak da geçerli bir çözümdür ve ölçülmelidir.

**Değer:** Bu tarama elle koştuğu için bulundu. Bir sonraki fazın eklediği ekran
veya operasyon aynı altı yüzeyi sessizce bayatlatır; NuGet'e basılan README
geri alınamaz.

**Mercek:** 6.

**Hazırlık:** Yukarıdaki tablo ölçüldü ve düzeltmeler uygulandı (drift taraması,
2026-08-28). Kapı yazılmadı — bu aday odur.

**Maliyet:** Düşük-orta; tek bir kontrol dosyası, mevcut `check-content.mjs`
deseninde.

**Risk:** Fazla katı bir kapı, meşru yuvarlanmış ifadeyi ("about 30 screens")
kızartabilir. Kontrol yalnız **işaretli** sayıya bakmalı, her rakama değil.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-28 — iç kalite kaydı; dış ekosistem iddiası yok.

**Karşı görüş:** Altı sayının hepsi düzeltildi ve bazıları (bundle boyutu) her
build'de değişir — belki doğru cevap sayıyı sevk edilen metinden tamamen
çıkarmaktır. Aday bu ikilemi kapsamına dahil ediyor.

## Aday Olmayan Açık Kayıtlar

Bu kalemler faz sıralamasına girmez. Tam kanıt, geçmiş ve sonraki adım keşif
kaydındadır.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Plana dönüştü** | F-109 → [Faz 112](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) · F-149 → [Faz 113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) · F-166 → [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md) · F-168 → [Faz 115](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) · F-67 → [Faz 116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md) · F-167 → [Faz 117](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) · F-152 → [Faz 118](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md) | Bölümleri bu dosyadan silindi; kanıt ve tasarım faz dokümanındadır. Aday listesine geri dönmezler. |
| **Kapatılan kusur kayıtları** | F-106, F-130, F-137, F-138, F-139 | Kapanış kanıtı keşif kaydındadır; yeniden görülürse yeni kusur kaydı açılır. |
| **Karar / uyumluluk** | F-72, F-90, F-91, F-92, F-132, **F-169** | Mevcut karar veya dış bağımlılık değişmeden planlanmaz. F-95 2026-08-26'da adaylığa döndü. **F-169** (MAF CodeAct / Hyperlight sandbox) F-72 ile **aynı eşiktedir**: paket GA ve taşınabilir olana kadar planlanmaz — ölçüm [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md) § 9. |
| **Ölçüm bekliyor** | F-51, F-94, F-96, F-97, F-99, F-101, F-123, F-128, F-154, F-156, F-157, F-159, F-160, F-161, F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. |
| **Arşivlendi / birleştirildi** | F-48, F-88, F-89, F-98, F-144, F-145, F-146, F-147, F-148, F-155, F-158, F-163 | Plan değeri yok, rutin bakım olarak kalır veya aktif adayla aynı tasarım işidir. **F-155** F-149 ile aynı tasarım işiydi; o iş artık [Faz 113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md)'tedir. **F-163** F-67'nin ölçümüne bağlıydı; o ölçüm artık [Faz 116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md)'dadır ve ayrı test ailesi olarak açılmaz. |

## Bilerek Önerilmeyenler

Reddedilmiş mimari işler için tek kaynak
[`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)'dir.
Özellikle F-91 `secret` saklama sınırını ve F-92 dağıtık hız sınırı/Redis
kararını değiştirmeden yeniden aday olmaz. **F-95 bu listede değildir** — onu
bekleten şey bir tasarım kararı değil, MAF'ta kancanın bulunmamasıydı; MAF
1.19.0 o kancayı gönderdiği için 2026-08-26'da adaylığa döndü.


### F-178 · Model deneme (attempt) telemetrisi

> **Yarısı plana dönüştü.** Job/kuyruk metrikleri
> [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md)'e gitti. Aşağıdaki gövde yalnız
> **kalan yarıyı** anlatır.

**Sorun:** Yedek zincirinde hangi linkte ne kadar süre harcandığı ölçülmüyor.
`FallbackChatClient` döngü indeksini tutuyor ve `ModelFallbackUsed`'ı yazıyor,
ama `grep -n "Stopwatch\|GetTimestamp\|Elapsed"` o dosyada **sıfır** eşleşme
veriyor ve `ModelFallbackUsedEventPayload` süre veya indeks taşımıyor. Birincil
model 28 sn'de timeout olup yedek 2 sn'de yanıtladığında, 30 sn'lik `run`'ın
gecikmesinin hangi linkten geldiği ayrıştırılamaz.

**Kapsam:** Deneme başına süre (monotonik saat), deneme indeksi, sağlayıcı,
model ve sonuç kategorisi taşıyan bir `run` olayı veya `span`. Prompt ve yanıt
içeriği telemetriye **girmez**; sağlayıcıya özel request ID **çıkarılmaz**
(tüketici bu maliyeti kabul etti). Etiket kardinalitesi sınırlanır.

**Değer:** "Birincil timeout değeri düşürülmeli mi?", "Yedek ilk model olmalı
mı?" soruları kanıta dayanır.

**Mercek:** 2, 7.

**Hazırlık:** `FallbackChatClient` döngü indeksini zaten tutuyor; ekleme
tamamen additive'dir. Faz 133 metrik adı ve etiket kurallarını kurar.

**Maliyet:** Ölçülmedi. Yeni tablo ve migration gerekmez.

**Risk:** Etiket kardinalitesi kontrolsüz büyürse metrik altyapısını boğar.

**Karşı görüş:** Tüketici bunu bilerek erteledi — *"Prodigy henüz production
olmadığı için gerçek incident kaydı sunamıyoruz … İlk fallback latency olayı
ölçüldüğünde bu talebi incident verisiyle yeniden açacağız."* Tek istediği,
API tasarımında bunu engelleyecek bir karar alınmamasıdır; bu koşul bugün
sağlanıyor.



### F-179 · Çalışma anı model yönlendirme policy'si

**Sorun:** Model seçimi bugün statik binding ve hata sonrası yedek zinciriyle
sınırlı. Çağrı **öncesi** maliyet, gecikme ve capability'ye göre seçim yapılamaz;
yapılsa bile seçimin nedeni `run` kanıtına girmez.

**Kapsam:** Aday binding kümesinden seçim yapan opt-in bir policy seam'i ve
seçim kararının `run` kanıtına yazılması (seçilen binding, kararlı reason code,
değerlendirilen adaylar, policy adı).

**Değer:** Yönlendirme kararı ile `run` kanıtı aynı yerde durur.

**Mercek:** 2, 7, 8.

**Hazırlık:** 🚨 **Ön koşulları eksik.** `RunRecord` sağlayıcı saklamıyor
(Faz 132 kapatır); kayan latency penceresi yok (F-178 kapatır);
`ModelProviderHealthCache` yalnız sağlık durumu verir.

**Maliyet:** Ölçülmedi.

**Risk:** Ölçüm olmadan "en ucuzu seç" kararı yanlış olur. Tüketici de
"önce doğru telemetry, sonra dinamik policy" diyor.

