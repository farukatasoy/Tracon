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
> Faz durumu yalnız üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'dedir.
> Bir kusur bu dosyaya geri girmez; `kusur-giderme` kanalına gider. Kapatılmış
> kararın yeniden açılması kullanıcı kararıdır. Ölçüm bekleyen iddia, kanıt
> üretmeden aday olmaz.

## Okuma Sırası

Önce aşağıdaki sıralama tablosunu, sonra seçilecek tek adayın bölümünü oku.
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

## Önerilen Sıralama

| Sıra | Aday | Neden şimdi | Önce ölçülecek/düşünülecek sınır |
|---:|---|---|---|
| 1 | **F-166** Çalıştırma-içi maliyet tavanı | Kota yalnız run öncesi bakılıyor; tek run tavanı aşabilir. | Kesme noktasının tool turu sınırına oturması ve yeni hata sınıfı kararı. |
| 2 | **F-167** MCP 2026-07-28 hizalanması | Dışa açılan tek protokol yüzeyi; SDK maliyeti zaten ödenmiş. | Tasks extension paketinin geçişli ağırlığı; task kimliğinin kiracı/auth bağlamına bağlanması. |
| 3 | **F-168** Eval'in başsız koşucusu ve CI kapısı | Eval çekirdeği tam, koşum yolu yok. | CI gürültü eşiği — F-67 ile **aynı** kararı ister; ikisi tek turda verilmeli. |
| 4 | **F-95** Agent düzeyinde kesinti/devam kancası | MAF 1.19.0 kancayı gönderdi; blocker kalkmış olabilir. | `maf-api-kesfi` ile imza doğrulaması ve experimental API riski. |
| 5 | **F-149** Sağlayıcı failure classification seam'i | Public extension contract'ı 1.0 öncesi doğru şekillenir. | Varsayılan metin eşleme korunurken yeni seam'in API maliyeti. |
| 6 | **F-109** İstemci tool'lu run replay'i | Sevk edilmiş client-tool yüzeyinin replay sözü eksiktir. | Replay sonucu oynatma mı, açık ret mi? |
| 7 | **F-165** Manuel kabul setinin CI'a taşınması | 1.650 case insan zamanına bağlıdır. | İlk dilim tek aile olur; tüm set tek faza sıkıştırılmaz. |
| 8 | **F-67** Performans regresyon kapısı | Stabil kod için taban çizgisi sonra almaktan ucuzdur. | CI gürültü eşiği ve F-163 kapsamı. |
| 9 | **F-152** Yargıç başına durable checkpoint/retry | Pahalı yargıç tekrarları kontrol edilemez. | Job-item/run kimliği ve migration maliyeti. |

## Planlanabilir Adaylar

### F-166 · Çalıştırma-içi maliyet tavanı

**Sorun:** Maliyet tavanı yalnız run **öncesi** ve **dönem birikimi** üzerinden
bakılır. Tek bir uzun run tavanı istediği kadar aşabilir; hiçbir kod yolu onu
çalışırken durdurmaz.

**Kapsam:** Run başına bir maliyet/token bütçesi ve onu çalışırken uygulayan bir
kesme noktası tasarla. Kesme **tool turu sınırında** olur; yarım bir model yanıtı
kesilmez. Terminal durumun hangi `RunErrorClass` ile raporlanacağı bu fazın
kararıdır — `QuotaExceeded` yeniden kullanılabilir ya da yeni bir sınıf açılır.

**Değer:** Kaçak bir agent döngüsü kiracının faturasını dönem tavanının çok
üstüne çıkaramaz; nöbetçi mühendisin elinde gerçek bir kesme kolu olur.

**Mercek:** 2, 3, 8.

**Hazırlık:** Ölçüldü — `QuotaEnforcer.cs:200` dönem birikimini karşılaştırır,
`QuotaGate.cs:34-47` bunu tek seferlik ön uçuş olarak çağırır.
`grep -rn "MaxCost" src/AgentPrism.Core/Recording src/AgentPrism.Core/Compilation`
sıfır isabet verir.

**Maliyet:** Orta; public yüzey büyür, migration gerekmeyebilir.

**Risk:** Yanlış konumlanan bir kesme yarım yanıt üretir ve tüketici bunu sessiz
kesme sayar. **🚨 Konum iddiası kabul edilmeden grep'le ölçülür** (K-320 deseni:
Faz 48'de guard'ın yanlış katmana konması fazın yarısını taşımaya çevirdi).

**Bağımlılık:** Yok. `RunErrorClass.BudgetExceeded` kusuru (2026-08-26) bu adayla
**aynı iş değildir** — o üye "tree veya context budget"i tarif eder, maliyeti
değil; ikisi ayrı kanallarda yürür.

**Ekosistem:** 2026-08-26 — dış iddia yok; repo runtime davranışı ölçüldü.

**Karşı görüş:** Dönem tavanı çoğu kurulumda yeterlidir ve tek bir run'ın dönem
tavanını anlamlı biçimde aştığı **ölçülmüş bir vaka yoktur**. Kanıt üretilmeden
bu, gerçek olmayan bir senaryoya karşı public yüzey büyütmek olur.

### F-167 · MCP 2026-07-28 sözleşmesine hizalanma

**Sorun:** AgentPrism'in MCP sunucusu 2026-07-28 spesifikasyonunun hiçbir yeni
yüzeyini taşımaz: Multi Round-Trip Requests, Tasks extension'ı, elicitation ve
cache'lenebilir list sonuçları yoktur. SDK maliyeti zaten ödenmiştir.

**Kapsam:** Önce hangi yüzeyin gerçek tüketicisi olduğunu ayır. Tasks extension'ı
uzun süren agent run'ını MCP tarafında poll/result/cancel edilebilir kılar ve
AgentPrism'in kuyruklu run modeline doğrudan oturur. MRTR/elicitation ayrı bir
karardır ve mevcut onay/input modeliyle **üçüncü bir state modeli üretmemelidir**.

**Değer:** AgentPrism'i MCP sunucusu olarak tüketen ekip, uzun run'ı bağlantıyı
açık tutmadan izleyebilir.

**Mercek:** 2, 3, 6.

**Hazırlık:** Ölçüldü — `src/AgentPrism.AspNetCore/McpServer` ve `src/AgentPrism.Mcp`
içinde `Stateless|MRTR|Tasks|Elicit` sıfır isabet. `Directory.Packages.props:162,172`
SDK'yı 2.2.0'a sabitler. **Çalışma anı probu (2026-08-26):** kurulu 2.2.0
paketinde `HttpServerTransportOptions.Stateless = True`, `SessionMode = Stateless`,
`EnableLegacySse = False`; `AgentPrismMcpServerBuilderExtensions.cs:60`
`WithHttpTransport()`'u **seçeneksiz** çağırır, yani sunucu bugün zaten
stateless'tır ve sunucu→istemci isteği desteklemez.

**Maliyet:** Orta–yüksek; Tasks extension'ı **yeni bir NuGet paketidir** ve
geçişli ağırlığı sayılmalıdır (K-212 emsali).

**Risk:** Task kimliği kiracı ve auth bağlamına sıkı bağlanmazsa çapraz kiracı
sızıntısı üretir. Public MCP sözleşmesi büyür.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-26 — [spec changelog](https://modelcontextprotocol.io/specification/2026-07-28/changelog)
ve [C# SDK v2.0 duyurusu](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/).
Down-level uyum vardır; 2025-11-25 istemcileri çalışmaya devam eder.

**Karşı görüş:** Spec bir ay önce yayımlandı ve down-level uyum zaten çalışıyor.
MCP sunucusu loopback + bearer + policy ile korunur ve uzak erişimle birlikte
açılmaz (`AgentPrismMcpServerExtensions.cs`). Bugün ölçülmüş bir tüketici acısı
yoktur; bu kalem şu an bir **uyumluluk konforu**, bir ihtiyaç değildir.

### F-168 · Eval'in başsız koşucusu ve CI kapısı

**Sorun:** Eval çekirdeği tamdır (`EvalSuite`, `EvalRun`, `IRunJudge`,
`EvalJobHandler`, `RunToCasePromoter`), fakat onu CI'da koşup exit code üreten
bir yol yoktur. Kalite ölçümü arayüzden elle tetiklenmeye bağlıdır.

**Kapsam:** `AgentPrism.Cli`'ye bir eval komutu ekle: suite seç, koş, eşiğe göre
exit code üret. Eşik **tüketici tarafından** verilir; AgentPrism varsayılan bir
kalite eşiği dayatmaz.

**Değer:** Agent kalitesi, kod kalitesiyle aynı kapıdan geçer.

**Mercek:** 3, 7.

**Hazırlık:** Ölçüldü — `ls src/AgentPrism.Cli/Commands/` üç komut verir
(`Health`, `Migrate`, `MigrateStatus`); `ls src/AgentPrism.Core/Evaluation/`
on beş dosya verir.

**Maliyet:** Orta; yeni paket gerekmez. **CLI yüzeyi de bir uyumluluk
taahhüdüdür** — komut adı ve exit code sözleşmesi sonradan ucuz değişmez.

**Risk:** Model yanıtı deterministik değildir; yanlış eşik CI'ı gürültüyle kapatır.

**Bağımlılık:** Yok, fakat **F-67 ile aynı kararı ister** (CI gürültü eşiği).
İkisi ayrı turlarda karara bağlanırsa iki farklı eşik felsefesi doğar.

**Ekosistem:** 2026-08-26 — dış iddia yok; repo yüzeyi ölçüldü.

**Karşı görüş:** Eval zaten HTTP ucundan tetiklenebilir; tüketici kendi CI
adımını `curl` ile yazabilir. Yeni bir CLI sözleşmesi taşımanın değeri, ölçülmüş
bir tüketici talebi olmadan kanıtlanmamıştır.

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


### F-149 · Sağlayıcı failure classification için extension seam'i

**Sorun:** `FallbackRetryClassifier` ve `DefaultRunErrorClassifier`, sağlayıcı
failure'ını exception tipi adı ve mesajdaki HTTP metni ile sınıflandırır.
Üçüncü taraf sağlayıcı bu metni taşımıyorsa retry veya hata sınıfı sessizce
yanlış olur.

**Kapsam:** Varsayılan metin eşlemeyi koruyan, `TryAdd*` ile değiştirilebilir bir
failure-classification seam'i tasarla. Sağlayıcı paketinin kendi SDK exception'ını
tipli ele alabilmesi değerlendirilir; AgentPrism Core'a sağlayıcı SDK bağımlılığı
eklenmez.

**Değer:** Sağlayıcı yazarı, mesaj biçimini tersine mühendislik etmeden retry ve
run error davranışını güvenilir kılar.

**Mercek:** 2, 3, 5, 6.

**Hazırlık:** Ölçüldü — `FallbackChatClient.cs:350` ve
`DefaultRunErrorClassifier.cs:132` regex tabanlı iki bağımsız sınıflandırıcıdır.

**Maliyet:** Orta; public interface veya opsiyonel provider capability'si gerekir.

**Risk:** Fazla genel bir abstraction, dört yerleşik sağlayıcıyı ve tüketici
uygulamalarını gereksiz karmaşıklaştırabilir.

**Bağımlılık:** Yok. Eski F-155 bu adayın bağımsız bir kopyası değildir; aynı
tasarım turunda ölçülür.

**Ekosistem:** 2026-08-26 — ekosistem iddiasına dayanmıyor; kod kanıtı yeterli.

**Karşı görüş:** Yerleşik dört sağlayıcının mevcut mesajları ölçülmüş ve çalışır.
Üçüncü taraf sağlayıcı talebi yoksa yeni public seam erken soyutlama olur.

### F-109 · İstemci tool içeren run'ların replay sözleşmesi

**Sorun:** `AddClientTool` declaration-only bir tool kaydeder. Replay derleyicisi
yalnız `AIFunction`'ları transform eder; istemci tool sonucu taşıyan eski run,
sunucunun çalıştıramayacağı çağrıda takılabilir.

**Kapsam:** Replay için iki açık sözleşmeden birini seç: kaydedilmiş istemci tool
sonucunu tekrar oynatmak veya bu run türünü anlaşılır, erken bir hata ile reddetmek.
Seçim HTTP, replay kaydı ve UI anlatımı boyunca tutarlı olur.

**Değer:** Replay yüzeyi, sevk edilmiş client-tool özelliği için yanlış bir
"her run replay edilir" beklentisi yaratmaz.

**Mercek:** 1, 2, 5, 6.

**Hazırlık:** Ölçüldü — `AgentDefinitionCompiler.cs:465` client tool'u
transform dışı bırakır; `RunReplayService.cs:126`–`280` üç replay modu taşır.

**Maliyet:** Küçük–orta; seçilen sözleşmeye göre persistence veya açık validation.

**Risk:** Kaydedilmiş sonucu oynatmak gerçek yan etkiyi simüle eder; canlı çağrı
ise no-surprises kuralını ihlal eder.

**Bağımlılık:** Faz 61 client-tool sözleşmesi (K-435).

**Ekosistem:** 2026-08-26 — dış ekosistem iddiası yok; AgentPrism'in mevcut
public davranış boşluğu ölçüldü.

**Karşı görüş:** Replay isteğe bağlı bir araçtır. İstemci tool kullanan tüketici
replay ihtiyacı duymuyorsa bu iş bekleyebilir.

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

### F-67 · Performans regresyon kapısı

**Sorun:** Mevcut kapanış kapıları doğruluğu korur; repo içinde benchmark projesi
ve performans taban çizgisi yoktur.

**Kapsam:** Önce üç sıcak yolu ölç: run-event yazımı, definition compiler cache'i
ve seçilmiş store sorgusu. Sonra yalnız anlamlı metrik için CI veya elle koşulan
eşik seçilir. F-163 ayrı bir test ailesi olarak açılmaz; bu ölçümün sonucu onu
ya kapsar ya kapatır.

**Değer:** Sıcak yol veya allocation regresyonu davranış doğruyken görünmez kalmaz.

**Mercek:** 2, 4, 7.

**Hazırlık:** Ölçüldü — 2026-08-26'da repo dosya taraması benchmark/bench projesi
bulmadı.

**Maliyet:** Orta.

**Risk:** Gürültülü CI eşiği yanlış kırmızı üretir ve kapanış maliyetini büyütür.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-26 — dış iddia yok; kendi kapı ve kaynak ağacı ölçüldü.

**Karşı görüş:** Benchmark sonucu kullanıcı davranışını doğrudan kanıtlamaz;
kararlı ölçüm ortamı yoksa elle koşulan taban çizgisi daha doğru olabilir.

### F-152 · Online evaluation için yargıç-başına durable checkpoint/retry

**Sorun:** Bir `OnlineEval` job'ı yeniden denendiğinde başarılı yargıçlar da
tekrar çalışır. `RunScore` upsert'i görünür duplicate'i engeller, fakat pahalı
veya yan etkili üçüncü taraf yargıç çağrısını engellemez.

**Kapsam:** Job item/run kimliği, başarılı yargıç sonucu ve retry sahipliğini
ölç. Yalnızca doğru idempotency sözleşmesi kurulabiliyorsa kalıcı checkpoint
modeli ve migration tasarla.

**Değer:** Pahalı yargıçlar tekrar denenirken doğru maliyet ve yan etki sınırı
korunur.

**Mercek:** 2, 3, 7, 8.

**Hazırlık:** Ölçüldü — `OnlineEvalJobHandler` retryable failure'ları toplar;
`IRunScoreStore.UpsertAsync` aynı author/run için görünür sonucu birleştirir.
Faz 100'ün açık sorusu durable sahipliği bırakmıştır.

**Maliyet:** Orta–yüksek; kalıcı model ve üç SQL sağlayıcı migration'ı gerekir.

**Risk:** Yanlış checkpoint, başarısız veya yarım yargıç sonucunu başarı diye
atlayabilir.

**Bağımlılık:** Faz 100 timeout/retry modeli; K-621'nin gerçek wait-cutoff sınırı.

**Ekosistem:** 2026-08-26 — dış iddia yok; repo runtime davranışı ölçüldü.

**Karşı görüş:** Yerleşik yargıçlar ucuz ve idempotent ise yeni kalıcı modelin
karmaşıklığı faydasını aşar.

## Aday Olmayan Açık Kayıtlar

Bu kalemler faz sıralamasına girmez. Tam kanıt, geçmiş ve sonraki adım keşif
kaydındadır.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Kapatılan kusur kayıtları** | F-106, F-130, F-137, F-138, F-139 | Kapanış kanıtı keşif kaydındadır; yeniden görülürse yeni kusur kaydı açılır. |
| **Karar / uyumluluk** | F-72, F-90, F-91, F-92, F-132, **F-169** | Mevcut karar veya dış bağımlılık değişmeden planlanmaz. F-95 2026-08-26'da adaylığa döndü. **F-169** (MAF CodeAct / Hyperlight sandbox) F-72 ile **aynı eşiktedir**: paket GA ve taşınabilir olana kadar planlanmaz — ölçüm [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md) § 9. |
| **Ölçüm bekliyor** | F-51, F-94, F-96, F-97, F-99, F-101, F-123, F-128, F-154, F-156, F-157, F-159, F-160, F-161, F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. |
| **Arşivlendi / birleştirildi** | F-48, F-88, F-89, F-98, F-144, F-145, F-146, F-147, F-148, F-155, F-158, F-163 | Plan değeri yok, rutin bakım olarak kalır veya aktif adayla aynı tasarım işidir. |

## Bilerek Önerilmeyenler

Reddedilmiş mimari işler için tek kaynak
[`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)'dir.
Özellikle F-91 `secret` saklama sınırını ve F-92 dağıtık hız sınırı/Redis
kararını değiştirmeden yeniden aday olmaz. **F-95 bu listede değildir** — onu
bekleten şey bir tasarım kararı değil, MAF'ta kancanın bulunmamasıydı; MAF
1.19.0 o kancayı gönderdiği için 2026-08-26'da adaylığa döndü.
