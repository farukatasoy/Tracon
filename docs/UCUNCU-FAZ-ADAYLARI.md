# UCUNCU-FAZ-ADAYLARI.md — İkinci Faz Sonrası Aday Yetenekler

> **Durum (2026-08-05): SIRADAKİ TURUN KAYNAĞI — HAM LİSTE, PLANLANMADI.**
> İkinci tur (Faz 8–30) [Faz 30](30-ARAYUZ-CILASI.md) ile kapandı; bir sonraki
> tur bu listeden seçilir. Seçim yapılmadan faz dokümanı yazılmaz.
>
> 🚨 **Seçim yapılırken bilinmesi gerekenler (Faz 30 kapanışı):**
> arayüz artık **iki dillidir** — yeni her ekran metni `locales/en.ts` ve
> `locales/tr.ts`'ye eklenir, eksik anahtar **derlemeyi kırar** (K-228);
> bundle **151,3 KB / 250 KB** kullanıyor, arayüz ağırlığı olan bir faz kalan
> ~99 KB payı hesaba katmalıdır; metin üzerine iddia kuran E2E testi dili
> **sabitlemelidir** (K-231).
>
> Bu belge
> [`BEYIN-FIRTINASI.md`](BEYIN-FIRTINASI.md)'nin devamıdır — Faz 8–30
> [`IKINCI-FAZ-YOL-HARITASI.md`](IKINCI-FAZ-YOL-HARITASI.md) ile planlıyken,
> bu belge **F-30'dan itibaren** henüz hiçbir faz dokümanına bağlanmamış
> adayları listeler. Hiçbir kalem onaylanmadı; kullanıcı seçim yapınca
> ilgili kalemler faz dokümanına dönüştürülür ve yol haritasına eklenir.
>
> Kod kanıtları 2026-08-05 tarihinde bu depo üzerinde `grep`/`Read` ile
> doğrulandı. Depo ilerledikçe satır numaraları kayabilir — kullanmadan önce
> yeniden doğrulanmalı.

## Değerlendirme Ölçütleri

`BEYIN-FIRTINASI.md` ile aynı dört soru:

| Ölçüt | Soru |
|-------|------|
| **Değer** | Bu olmadan AgentPrism'i kim kullanamaz? |
| **Maliyet** | Kaç paket, kaç yeni public tip, kaç migration? |
| **Risk** | Bir tasarım kuralını (K1–K4) zorluyor mu? Bundle bütçesini? |
| **Hazırlık** | MAF veya .NET ekosisteminde hazır mı, sıfırdan mı? |

---

## A. Yetenek delikleri

### F-30 · Vektör bellek ve RAG (pgvector) 🔥

**Değer:** Çok yüksek. K-105'in bilerek açık bıraktığı tek büyük delik. Bugün
`ChatHistoryMemoryProvider` hiç kullanılamıyor, `TextSearchProvider` ise
`InMemoryAgentFileStore` üzerinde **regex** arıyor (K-110). Anlamsal arama,
doküman alma ve kalıcı bellek — üçü de yok.
**Hazırlık:** PostgreSQL zaten var; `pgvector` uzantısı doğal cevap.
`VectorStore` tipi `Microsoft.Extensions.VectorData` soyutlamasından gelir;
somut PostgreSQL bağlayıcısının sürüm uyumu **ölçülmelidir**.
**Maliyet:** Bir embedding sağlayıcı kararı (`IEmbeddingGenerator`), bir
migration, `PostgresAgentFileStore`. K-110 zaten "kalıcı depo eklenince kod
değişmeden kalıcı aramaya döner" diyor — zemin hazır.
**Risk:** Yeni paket (K-007 gerekçe ister). Embedding maliyeti Faz 20'nin
fiyat modeline girmelidir.

### F-31 · AgentPrism'in MCP sunucusu olması 🔥

**Değer:** Çok yüksek, stratejik. Faz 22 AgentPrism'i MCP **istemcisi**
yapıyor. Aynanın diğer yüzü yok: katalogdaki agent'lar MCP tool'u olarak dışa
açılırsa Claude Code, Copilot, Cursor ve başka agent'lar AgentPrism agent'ını
doğrudan çağırır. Kontrol düzlemi iddiası böyle iki yönlü olur.
**Hazırlık:** `ModelContextProtocol.Core` zaten bağımlılıkta. Faz 12'nin
`ChildAgentInvoker`'ı "agent'ı tool gibi çağırma" işini zaten çözdü — aynı
sınır denetimleri (bütçe, derinlik, kiracı) yeniden kullanılır.
**Risk:** Yeni bir dış yüzey; kimlik doğrulama ve kiracı çözümlemesi
`MapAgentPrism` deseniyle aynı olmalı. Onay akışı (K-103'ün alt agent sınırı)
burada da geçerlidir.

### F-32 · Guardrails ve içerik güvenliği genişleme noktası

**Değer:** Yüksek — kurumsal kapı. Bugün hiç yok. `AuditSecretFilter` yalnız
denetim kaydını temizler; modele giden ve modelden gelen içerik denetlenmez.
**Kapsam:** `IContentGuard` (giriş/çıkış), PII maskeleme, basit deny-list
yerleşik, Azure AI Content Safety adaptörü ayrı paket. Bir `IAgentDecorator`
olarak takılır — üç dekoratörlü zincir (kayıt 0 → telemetri 10 → onay 20)
hazır yuvadır.
**Risk:** Varsayılan **kapalı** olmalıdır (K1 sıfır sürpriz).

### F-33 · A2A protokolü (uzak agent çağrısı)

**Değer:** Orta-yüksek. Faz 12 süreç **içi** ağacı verdi. A2A süreçler
**arası** çağrıdır: agent kartı, uzak agent keşfi, yetenek ilanı.
**Hazırlık:** MAF'ta A2A paketi olup olmadığı **doğrulanmadı** —
`maf-api-kesfi` skill'i ile ölçülmelidir. Yoksa protokolü elle uygulamak
F-31'den pahalıdır.
**Not:** F-31 yapılırsa bu daha az acildir; MCP fiilî standart hâline geldi.

### F-34 · Talimat şablonlama ve paylaşılan prompt kütüphanesi

**Değer:** Orta. Faz 19 sürümlemeyi ve A/B'yi veriyor; **içerik yeniden
kullanımı** eksik. On agent aynı "kurum kuralları" bloğunu kopyalıyorsa tek
yerden değiştirmenin yolu yok.
**Kapsam:** Değişkenli talimat (`{{tenant_name}}`), kısmi bloklar, agent
tanımında referans.
**Risk:** Şablon dili bir güvenlik yüzeyidir; ifade değil yalnız **değer
yerleştirme** desteklenmelidir.

---

## B. Üretim sağlamlığı

Aşağıdaki dört kalem küçüktür ve **tek bir fazda** biter. Üçü bugün kanıtlı
delik.

### F-35 · Çalıştırma iptali 🔥

**Kanıt:** `RunStatus.Canceled`
([`RunStatus.cs:27`](../src/AgentPrism.Abstractions/Runs/RunStatus.cs)) tanımlı
ve `RunStatistics.CanceledRuns` onu sayıyor — ama hiçbir kod bu değeri
**yazmıyor**. Ölü bir enum değeri ve her zaman sıfır olan bir istatistik
alanı.
**Kapsam:** `POST /api/runs/{id}/cancel`, `CancellationTokenSource` kaydı,
Faz 12'nin ağacında kök iptali alt çalıştırmaları da durdurur.
**Maliyet:** Düşük. **Değer:** Yüksek — kaçak bir agent'ı durdurmanın bugün
tek yolu süreci öldürmek.

### F-36 · Öksüz çalıştırma uzlaştırması 🔥

**Kanıt:** Süreç düşerse `runs` satırı sonsuza dek `Running` kalır. Arayüzde
asla bitmeyen çalıştırmalar birikir; `RunStatistics.settled` hesabı bozulur.
**Kapsam:** Çalıştırma kirası (heartbeat sütunu), açılışta uzlaştırma —
süresi geçmiş `Running` satırları `Failed` olarak kapanır ve nedeni yazılır.
**Risk:** Çok örnekli kurulumda kira süresi doğru seçilmelidir; erken kapatma
çalışan bir işi ölü ilan eder.

### F-37 · `Idempotency-Key` desteği

**Değer:** Yüksek. Bugün istemci yeniden denemesi ikinci bir çalıştırma **ve
ikinci fatura** üretir. Ağ hatası sonrası güvenli yeniden deneme mümkün
değil.
**Kapsam:** `POST /api/agents/{name}/run` ve OpenAI uyumlu uçlar; anahtar +
kiracı benzersiz, ilk yanıt saklanır.
**Maliyet:** Düşük — bir tablo, bir ara yazılım.

### F-38 · ASP.NET Core `IHealthCheck` entegrasyonu

**Kanıt:** Depoda hiç `IHealthCheck` yok. `/api/models/health`
([`CatalogEndpoints.cs`](../src/AgentPrism.AspNetCore/Endpoints/CatalogEndpoints.cs))
var ama ASP.NET Core sağlık sistemine bağlı değil — Kubernetes, App Service ve
yük dengeleyici standart `/health` yolunu yoklar.
**Kapsam:** `AddAgentPrismHealthChecks()` → veritabanı erişimi, migration
durumu, sağlayıcı sağlığı (Faz 8'in önbelleğinden, ek istek atmadan).
**Maliyet:** Çok düşük.

### F-39 · Asenkron çalıştırma sözleşmesi (`202 Accepted`)

**Değer:** Orta. Uzun süren çalıştırmada istemci bağlantıyı tutmak zorunda.
Faz 17'nin kuyruğu altyapıyı getirecek; **HTTP sözleşmesi** ayrı bir karardır
ve Faz 17'den önce tasarlanmalıdır.

### F-40 · Kiracı başına sağlayıcı anahtarı (BYOK)

**Değer:** Yüksek — çok kiracılı SaaS için zorunlu. Bugün anahtar global
yapılandırmada; tüm kiracılar aynı faturayı paylaşır.
**Çözüm:** K-059 deseni aynen geçerli — veritabanında yalnız
**yapılandırma anahtarının adı** durur, değer çalışma anında
`IConfiguration`'dan çözülür. Sır veritabanına yazılmaz.

### F-41 · İçerik şifreleme (at-rest)

**Değer:** Orta-yüksek; regüle sektörlerde zorunlu. `conversation_items` tam
sohbet geçmişini açık saklıyor ve K-107 "özetlenen mesajlar silinmez" diyor —
depo zamanla tüm hassas içeriği biriktirir.
**Kapsam:** `IContentProtector` genişleme noktası; varsayılan uygulama yok
(K4 — her genişleme noktası değiştirilebilir).

---

## C. Model yüzeyi

### F-42 · Yapılandırılmış çıktı (JSON şeması) 🔥

**Kanıt:**
[`ModelBinding.cs`](../src/AgentPrism.Core/Models/ModelBinding.cs) yalnız
`Temperature`, `MaxOutputTokens`, `TopP`, `ReasoningEffort` taşıyor.
`ResponseFormat` yok.
**Değer:** Yüksek. Agent'ı bir API gibi kullanmanın yolu budur; OpenAI
konsolunun standart yeteneği. Faz 18'in eval'i de şemalı çıktıyı ister.
**Maliyet:** Düşük — `ChatOptions.ResponseFormat` hazır. Şema doğrulaması
derleme anında yapılmalı, çalışma anında değil.

### F-43 · Sağlayıcıya özgü ayar torbası 🔥

**Değer:** Yüksek ve **aciliyeti sıradan yüksek**: Faz 26 (Anthropic
`thinking` blokları, prompt caching) ve Faz 27 (Foundry) bunu isteyecek.
`ModelBinding` bugün `sealed record` ve public API; sonradan eklemek kırıcı
değişikliktir. **Şimdi tasarlanmazsa pahalıya patlar.**
**Kapsam:** `ModelBinding.ProviderOptions` (opak sözlük) →
`ChatOptions.AdditionalProperties`. Tanınmayan anahtar sessizce yutulmaz,
derlemede reddedilir (K1).

### F-44 · Model yedek (fallback) zinciri

**Değer:** Yüksek. Faz 8 devre kesiciyi verdi — yarısı. Eksik yarı:
"sağlayıcı A kesikse B'ye geç". Bugün devre açıldığında çalıştırma yalnız
**hata veriyor**.
**Hazırlık:** `ModelProviderRegistry.CreateChatClient` tek entegrasyon
noktası; Faz 8 zaten oraya dokundu.
**Maliyet:** Düşük. **Risk:** Yedek model farklı fiyatlıdır — Faz 20'nin
maliyet raporu hangi modelin çalıştığını yazmalıdır (`RunStatistics.ByModel`
zaten var).

### F-45 · Yanıt önbelleği ve prompt caching geçişi

**Değer:** Orta-yüksek (maliyet). `Microsoft.Extensions.AI` içinde
`DistributedCachingChatClient` hazır **görünüyor** — kullanmadan önce imza
doğrulanmalıdır.
**Risk:** Önbellek varsayılan **kapalı**; agent'ın aynı soruya farklı yanıt
vermesi beklenen davranıştır.

---

## D. Paket ailesi ve geliştirici deneyimi

### F-46 · `AgentPrism.Testing` paketi 🔥

**Kanıt:** `ScriptedModelProvider`
([`tests/AgentPrism.Ui.E2ETests/Infrastructure/`](../tests/AgentPrism.Ui.E2ETests/Infrastructure/ScriptedModelProvider.cs))
ve `RoutingModelProvider`
([`tests/AgentPrism.AspNetCore.FunctionalTests/Infrastructure/`](../tests/AgentPrism.AspNetCore.FunctionalTests/Infrastructure/RoutingModelProvider.cs))
— ikisi de test projelerinde kilitli.
**Değer:** Yüksek. Bugün AgentPrism üzerine agent yazan biri, kendi
agent'ını gerçek model çağırmadan test **edemez**. Kod zaten var, eksik olan
public tasarımı.
**Kapsam:** Sahte sağlayıcı (senaryolu yanıt, tool çağrısı üretimi), bellek
içi host fixture, çalıştırma iddiaları (`run.ShouldHaveCalledTool("x")`).
**Maliyet:** Düşük-orta. Public API olduğu için tasarım bir kez doğru
yapılmalıdır.

### F-47 · Kaynak üreteci ve analyzer paketi

**Değer:** Yüksek ve **AOT duruşuyla birebir örtüşür**.
`ToolMethodScanner`
([`src/AgentPrism.Core/Tools/ToolMethodScanner.cs`](../src/AgentPrism.Core/Tools/ToolMethodScanner.cs))
bugün `AgentPrism.Core`'daki tek yansıma noktası. Kaynak üreteci bunu derleme
anına taşır.
**Ek kazanç:** Tool adı çakışması, desteklenmeyen parametre tipi, eksik XML
özeti — üçü de derlemede yakalanır. `AddToolsFrom<T>()`'in `CS0718` tuzağı
(statik sınıf tür argümanı olamaz) da ortadan kalkar.
**Maliyet:** Orta-yüksek; Roslyn üreteci ayrı bir uzmanlık ve ayrı test
altyapısı ister.

### F-48 · GitOps: tanım dışa/içe aktarımı

**Değer:** Yüksek. Bugün agent tanımı ya koddadır ya veritabanında. Bir ekip
tanımlarını git'te sürümlemek ve dev→prod terfi etmek isterse **yolu yok**.
Faz 19'un arayüz diff'inden farklı iştir: bu dağıtım hattı işidir.
**Kapsam:** JSON/YAML dışa aktarım, `--dry-run` ile fark gösterimi, içe
aktarımda sürüm geçmişi ve denetim izi korunur (Faz 9 hazır).

### F-49 · `dotnet new` şablon paketi

**Değer:** Orta-yüksek benimseme etkisi, **maliyet çok düşük.**
`dotnet new agentprism-api` → çalışan bir kontrol düzlemi. Bugün
`samples/AgentPrism.Api` var ama şablon değil.

### F-50 · Tipli yönetim istemcisi ve CLI

**Kapsam:** `AgentPrism.Client` (AOT uyumlu, kaynak üretilmiş JSON) +
`dotnet agentprism` global aracı: migration uygula, tanım dışa/içe aktar
(F-48), sağlık denetimi. CLI istemcinin ilk tüketicisidir.
**Değer:** Orta. **Not:** Migration bugün açılışta uygulanıyor; CI/CD hattı
ayrı bir migration adımı ister.

### F-51 · .NET Aspire entegrasyonu

**Değer:** Orta-yüksek. Aspire kurumsal .NET'in yeni varsayılan besteleme
yolu. PostgreSQL kaynağı, OTel bağlantısı ve panoya bağlantı hazır gelir.
**Maliyet:** Düşük — Faz 6'nın telemetrisi zaten OTel.

---

## E. Üretim–geliştirme döngüsünü kapatanlar

Bu dördü birlikte "agent'ı ölçerek iyileştirme" döngüsünü kurar. Bugün döngü
**tek yönlü**: üretim veri üretiyor, hiçbiri geri beslenmiyor.

### F-52 · Geri bildirim ve puanlama 🔥

**Değer:** Yüksek. Depoda hiçbir geri bildirim kaydı yok. Bir çalıştırmanın
**iyi mi kötü mü** olduğu bilinmiyor; yalnız `Completed` mi `Failed` mi
olduğu biliniyor. Bu ikisi aynı şey değildir — modelin ürettiği yanlış cevap
`Completed` olarak kaydedilir.
**Kapsam:** Çalıştırma ve mesaj başına puan (olumlu/olumsuz, 1–5, serbest
yorum), `POST /api/runs/{id}/feedback`, OpenAI uyumlu uçtan da yazılabilir.
Faz 20'nin panosunda "memnuniyet" göstergesi.
**Maliyet:** Düşük — bir tablo, bir uç, bir arayüz düğmesi. **Önkoşulu yok,
bugün yapılabilir.**

### F-53 · Üretimden değerlendirme veri kümesi toplama 🔥

**Değer:** Yüksek. Faz 18 eval altyapısını getirecek ama **test kümesini kim
yazacak?** Elle yazılan eval kümeleri bayatlar. Doğru kaynak üretimin
kendisidir: başarısız veya olumsuz puanlanmış bir çalıştırmayı tek tıkla test
durumuna terfi ettirmek.
**Hazırlık:** `run_events` girdiyi ve çıktıyı zaten saklıyor;
`conversation_items` tam geçmişi tutuyor. Veri hazır, eksik olan terfi yolu.
**Bağımlılık:** F-52 → F-53 → Faz 18. **Faz 18'den önce tasarlanmalıdır**,
sonra değil.

### F-54 · Çalıştırma yeniden oynatma (replay)

**Değer:** Yüksek. "Üretimdeki şu hatayı düzelttiğim talimatla tekrar
çalıştır" bugün mümkün değil. Girdiyi elle kopyalamak gerekiyor.
**Kapsam:** Kayıtlı bir çalıştırmayı aynı girdiyle farklı bir agent sürümü
veya modelle yeniden çalıştırmak; iki sonucu yan yana koymak. Faz 19'un diff
arayüzü bunu doğal olarak barındırır.
**Risk:** Tool çağrıları yan etkilidir. Yeniden oynatma ya **tool'suz**
(yalnız model yanıtı) olmalı ya da kayıtlı tool sonuçlarını geri
oynatmalıdır. Bu bir karardır, atlanamaz.

### F-55 · Hata sınıflandırma ve arıza kümeleme

**Değer:** Orta-yüksek. Bugün `RunStatistics.FailedRuns` tek bir sayı. "Son
24 saatte en sık üç hata" sorusu cevaplanamıyor; `Failed` çalıştırmaları tek
tek açmak gerekiyor.
**Kapsam:** Hata taksonomisi (sağlayıcı hatası / kota / tool hatası / zaman
aşımı / bütçe), normalleştirilmiş hata parmak izi, kümeye göre gruplama. Faz
21'in webhook'u "bu küme %5'i aştı" kuralıyla anlam kazanır.

---

## F. Çok örnekli ve SaaS işletimi

### F-56 · Kiracı bazlı API anahtarları ve kapsamlar 🔥

**Kanıt:**
[`AgentPrismEndpointFilter.cs:22`](../src/AgentPrism.AspNetCore/Security/AgentPrismEndpointFilter.cs)
— gelen kimlik doğrulaması **tek statik token** (`AuthToken`) veya
tüketicinin kendi policy'si. Anahtar döndürme yok, iptal yok, "son kullanım"
yok, kiracıya bağlanma yok.
**Değer:** Yüksek. F-40 **giden** anahtarları (sağlayıcıya) çözüyordu; bu
**gelen** kimlik doğrulamasıdır ve çok kiracılı bir kurulumda ondan daha
kritiktir. Bugün bir kiracıya anahtar veremezsiniz — anahtar tek ve
herkesin.
**Kapsam:** `api_keys` tablosu (hash saklanır, ham değer bir kez gösterilir),
kiracı bağı, kapsam (`runs:write`, `agents:admin`), süre sonu, iptal, son
kullanım damgası. Faz 9'un rol politikalarıyla birleşir.
**Risk:** Faz 9'un `AgentPrismPolicies` yapısı korunmalı; bu onun
**alternatifi** değil, ikinci bir kimlik kaynağıdır.

### F-57 · Çok örnekli koordinasyon (tek yürütücü seçimi)

**Kanıt:** `McpDiscoveryService` bir `BackgroundService`'tir
([`McpDiscoveryService.cs:22`](../src/AgentPrism.Mcp/Internal/McpDiscoveryService.cs))
— **her replika** aynı MCP sunucularını aynı aralıkla yokluyor. Üç replika,
üç kat istek. `pg_advisory_lock` depoda **yalnız**
[`MigrationRunner`](../src/AgentPrism.PostgreSql/Migrations/MigrationRunner.cs)'da
kullanılıyor.
**Değer:** Yüksek ve **aciliyeti sıradan yüksek**: Faz 17'nin zamanlanmış
çalıştırması bu olmadan yapılırsa her cron N kez tetiklenir. Zamanlayıcıyı
yazdıktan sonra eklemek daha pahalıdır.
**Kapsam:** `pg_advisory_lock` üzerine kurulu kira tabanlı tek yürütücü
seçimi; MCP keşfi, F-36'nın uzlaştırması ve Faz 17'nin zamanlayıcısı bunu
paylaşır.

### F-58 · Veri konusu silme ve ihracı (GDPR)

**Değer:** Orta-yüksek; AB'de kurumsal kapı. K-107 "özetlenen mesajlar
silinmez" diyor — depo **tüm** hassas içeriği bilerek biriktiriyor. Faz 25
yaşa göre temizliyor; **kişiye göre** silme yolu yok.
**Çatışma:** Denetim izi değiştirilemez olmalıdır, veri konusu ise silme
hakkına sahiptir. Bu gerçek bir tasarım kararıdır — silme işaretlemesi mi,
alan bazlı maskeleme mi? `KARARLAR.md`'ye yazılacak cinsten.

---

## G. Küçük ama yüksek getirili

### F-59 · Ön uçuş bütçe denetimi ve bağlam penceresi koruması 🔥

**Kanıt:** `ModelDescriptor.ContextWindowTokens`
([`ModelDescriptor.cs:13`](../src/AgentPrism.Abstractions/Models/ModelDescriptor.cs))
tanımlı ama **hiçbir yerde okunmuyor** — tıpkı fiyat alanları gibi
(`MEMORY.md` satır 163). Kullanıcı aynı sayıyı `HarnessSettings.MaxContextWindowTokens`
ve `CompactionSettings.MaxContextWindowTokens` alanlarına
[elle yazıyor](../src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx).
Yanlış yazarsa hata sağlayıcıdan gelir.
**Kapsam:** Model metadata'sından varsayılan türetme, çağrı öncesi token
sayımı, aşımda erken ve anlaşılır red. `POST /api/agents/{name}/estimate`
ucu — Faz 12'nin bütçesi ve Faz 21'in kotası bu sayıyı **zaten** istiyor.
**Maliyet:** Düşük; tokenizer MAF üzerinden geçişli bağımlılıkta hazır
(K-104).

### F-60 · Tanım doğrulama ucu (dry-run derleme)

**Değer:** Orta-yüksek, maliyeti çok düşük. `POST /api/agents/validate` bir
tanımı **kaydetmeden ve model çağırmadan** derler: model var mı, tool'lar
çözülüyor mu, skill'ler yükleniyor mu, çağrı grafiği döngü içeriyor mu.
F-48'in (GitOps) CI adımı tam olarak budur.
**Hazırlık:** `AgentDefinitionCompiler` zaten `AgentPrismCompilationException`
fırlatıyor — yalnız bir uçtan çağrılması gerekiyor.

### F-61 · Argüman düzeyinde tool politikası

**Değer:** Orta-yüksek. Onay kuralları bugün **tool düzeyinde**:
`refund_order` ya hep onay ister ya hiç. Gerçek ihtiyaç "100 TL altı otomatik,
üstü onay" biçimindedir. Bugünkü kabalık onay yorgunluğu üretir; kullanıcı
her şeyi onaylamayı öğrenir ve onay akışı değerini kaybeder.
**Kapsam:** `ToolApprovalRule`'a argüman koşulu; izin verilen değer listesi.
`ApprovalRequiredAIFunction` sarmalaması zaten tek kapıdır (`MEMORY.md` satır
40) — yeri hazır.

### F-62 · Yapılandırma teşhisi (doctor)

**Değer:** Orta-yüksek. `MEMORY.md`'deki tuzakların çoğu **sessiz** yanlış
yapılandırmadır: `TryAdd` sırası bozulunca kalıcılık sessizce devre dışı
kalıyor (satır 57), `ASPNETCORE_ENVIRONMENT` ayarlanmayınca sırlar sessizce
boş geliyor (satır 137), Responses + `ChatHistoryProvider` çakışması yalnız
PostgreSQL açıkken patlıyor (satır 99).
**Kapsam:** Açılışta hızlı öz denetim + `/api/diagnostics`: hangi depo
gerçekten kayıtlı, migration durumu, sağlayıcı anahtarı çözüldü mü, arayüz
gömülü mü. Bu, tüketicinin sana açacağı issue'ların yarısını kapatır.

---

## H. Dış tüketici yüzeyi

### F-63 · TypeScript istemci paketi ve OpenAPI yayını

**Değer:** Orta-yüksek. `Microsoft.AspNetCore.OpenApi` zaten bağımlılıkta.
Kendi arayüzünü yazmak isteyen tüketici bugün uçları elle okuyor. Yayınlanmış
bir OpenAPI belgesi + üretilmiş npm paketi bunu kaldırır ve gömülü arayüzün
alternatifini mümkün kılar.

### F-64 · Gömülebilir sohbet bileşeni

**Değer:** Orta. Bugün arayüz **tam bir kontrol düzlemidir**; tüketicinin
kendi uygulamasına koyacağı küçük bir sohbet kutusu yok. Ayrı, küçük bir
bundle (hedef <30 KB gzip) tüketicinin son kullanıcıya agent açmasını
sağlar.
**Risk:** Bundle bütçesi ayrı tutulmalı; kontrol düzlemi bileşenleri bu
pakete sızmamalı.

### F-65 · Gelen tetikleyiciler

**Değer:** Orta. F-19 **giden** webhook'tur (olay yayını). Tersi yok: dış
bir olay (Slack mesajı, e-posta, kuyruk) bir çalıştırma başlatamıyor. Faz 17
zamanlanmış tetiklemeyi getiriyor; olay tabanlı tetikleme aynı kuyruğu
kullanır ve ucuzdur.

### F-66 · Konuşma dallandırma (fork / mesajı düzenle)

**Değer:** Orta. Playground'da bir mesajı düzenleyip oradan devam etmek
standart bir deneyimdir. `conversation_items` append-only (K-014) olduğu
için dal işaretçisi gerekir — küçük bir şema kararı.

---

## I. Kalite kapısı

### F-67 · Performans regresyon kapısı

**Değer:** Orta-yüksek ve bu depoya **kültürel olarak uygun**. Dört
doğrulama kapısı doğruluğu koruyor; performans korunmuyor. Bir kütüphanede
sıcak yolun (çalıştırma kaydı, olay yazma, tool çözümleme) tahsis bütçesi
olmalıdır.
**Kapsam:** BenchmarkDotNet + tahsis eşiği; `run_events` yazma yolu ve
`AgentDefinitionCompiler` önbelleği ilk hedefler.

---

## Bilerek önerilmeyenler

Değerlendirildi ve **alınmaması** önerildi — gerekçeleriyle:

| Kalem | Neden hayır |
|---|---|
| Arayüzden tool kodu yazma / no-code tool oluşturucu | K2'nin doğrudan ihlali. Güvenlik sınırıdır, gevşetilmez |
| OpenAI Assistants API uyumluluğu | OpenAI kendisi Responses API'ye taşıdı; ölü bir yüzeye maliyet |
| gRPC yönetim yüzeyi | HTTP + OpenAPI yeterli; ikinci yüzey iki kat bakım |
| Çoklu model konsensüs / oylama | Niş; tüketici bunu kendi agent'ında kurar |
| Agent/skill pazar yeri | Barındırma ve moderasyon işi; kütüphane sınırının dışında |

---

## Önerilen Sıralama

Üç kalem, **bağımlı oldukları fazdan önce** yapılmalıdır — sonra yapmak daha
pahalıdır:

| Kalem | Hangi fazdan önce | Neden |
|---|---|---|
| **F-43** sağlayıcıya özgü ayar torbası + **F-42** yapılandırılmış çıktı | Faz 14 (çok modluluk) / Faz 26 (Anthropic·Gemini) | `ModelBinding` public API'dir; sonradan eklemek kırıcı değişikliktir. En ucuz an şimdi |
| **F-57** çok örnekli koordinasyon | Faz 17 (zamanlanmış çalıştırma) | Zamanlayıcı yazıldıktan sonra eklemek onu yeniden yazmak demektir |
| **F-52 + F-53** geri bildirim + veri kümesi | Faz 18 (eval) | Eval kümesini elle yazmak sürdürülemez; kaynak üretim olmalı |
| **F-59** ön uçuş bütçe denetimi | Faz 21 (kota) | Kota, çağrı **öncesi** token tahmini ister; sonrasında ölçmek geç kalır |

Bunlardan bağımsız, bugün tek başına yapılabilecek en ucuz üçlü:
**F-35 + F-36 + F-38** (çalıştırma iptali, öksüz uzlaştırma, health check —
üçü de küçük, üçü de kanıtlı delik), **F-60** (doğrulama ucu), **F-62**
(teşhis) ve **F-52** (geri bildirim). Bu altısı yeni paket istemez, migration
yükü küçüktür ve her biri tüketicinin ilk gün karşılaştığı bir sorunu kapatır.

**Faz 7 hatırlatması:** `EnablePublicApiTracking` bugün `false`. F-42, F-43 ve
F-46 public yüzeyi genişletir. Yayından **önce** yapılırsa bedava; sonra
yapılırsa her biri bir sürüm kararıdır.
