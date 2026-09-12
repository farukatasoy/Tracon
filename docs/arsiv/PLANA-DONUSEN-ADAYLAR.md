# Plana Dönüşen Adayların Gövdeleri

> **Kapanmış kayıt.** Baştan sona okunmaz — yalnız `grep`'lenir.
>
> Bir aday kalem faza dönüştüğünde [`../ADAYLAR.md`](../ADAYLAR.md) içinde tek
> satırlık bir yönlendirici kalır; kalemin **tam gövdesi** buraya taşınır.
> Gerekçe: aday dosyası 80 KB bütçesindedir ve her oturumda okunur; plana
> dönüşmüş bir kalemin gerekçesi artık **fazın kendi dokümanındadır**.
>
> **Bir kalemin bugünkü doğrusu burada değildir.** Gövdeler taşındıkları
> tarihte doğruydu; `dosya:satır` kanıtları o günün deposuna aittir. Güncel
> plan için fazın dokümanına bak.

| Nerede ne yaşar | |
|---|---|
| Kalemin uygulanabilir planı | `docs/NN-*.md` |
| Kalemin tek satırlık izi | [`../ADAYLAR.md`](../ADAYLAR.md) |
| Kalemin aday gövdesi (bu dosya) | taşındığı tarihle |
| Turun tam kaydı | [`../kesif/`](../kesif/) |

---

## 2026-08-18 — Tüketici raporu turu (F-110…F-119)

On kalem, harici bir tüketici projesinin uygulanabilirlik raporundan doğdu ve
aynı gün altı faza dönüştü. Turun tam kaydı:
[`kesif/2026-08-18-tuketici-raporu.md`](kesif/2026-08-18-tuketici-raporu.md).

| Kalem | Faz |
|---|---|
| F-110 | [Faz 67](fazlar/67-ISTEGE-BAGLI-MIGRATION-SETI.md) |
| F-111 | [Faz 68](fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) |
| F-112 | [Faz 68](fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) |
| F-113 | [Faz 69](fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) |
| F-114 | [Faz 69](fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) |
| F-115 | [Faz 70](fazlar/70-CALISTIRMA-OLAYI-HEDEFI.md) |
| F-116 | [Faz 71](fazlar/71-WORKFLOW-KOD-DUGUMU.md) |
| F-117 | [Faz 72](fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) |
| F-118 | [Faz 72](fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) |
| F-119 | [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) |

---

### F-110 · `pgvector`'ün isteğe bağlı olması

**Sorun:** `UsePostgreSql()`, Knowledge hiç kullanılmasa bile
`CREATE EXTENSION vector` çalıştırır
(`0024_vector.sql:17` *(Faz 67'de [`MigrationsKnowledge/0001_vector.sql`](../../src/Tracon.PostgreSql/MigrationsKnowledge/0001_vector.sql)'e taşındı)*);
`MigrationDescriptor.Discover` gömülü **her** `.sql` dosyasını sırayla koşar,
koşullu set kavramı yoktur. Extension'ı olmayan ya da migration kimliğine
`CREATE EXTENSION` izni vermeyen yönetilen bir PostgreSQL'de paket
**başlangıçta** düşer.
**Kapsam:** Migration seti ikiye ayrılır — çekirdek ve Knowledge. Vektör seti
yalnız Knowledge kaydedilince koşar.
**Değer:** Yönetilen PostgreSQL kullanan tüketici bugün ya extension kurduruyor
ya SQL Server/SQLite'a düşüyor. İkisi de ilk beş dakikada verilen bir karar.
**Mercek:** 1, 3, 6.
**Hazırlık:** Sıfırdan, ama iş küçük: `Discover` bir ön ek daha alır.
**Maliyet:** Yeni paket yok. Bir dosya taşınır, `MigrationRunner` bir parametre alır.
**Risk:** 🚨 **Yayından sonra imkânsızdır.** Uygulanmış migration dokunulmazdır
(SHA-256 tüm metni kapsar). Seti bugün ayırmak bedava; ilk üretim koşumundan
sonra sıra değiştirilemez. **Faz 7'den önce yapılmalıdır.**
**Bağımlılık:** Yok.
**Ekosistem:** Taranmadı — kendi kodumuzun ölçümüne dayanır.
**Karşı görüş:** Davranış dokümantedir (`packages.md:53`, `persistence.md:67`,
`troubleshooting.md:331`) ve tüketici SQL Server veya SQLite seçebilir. Yani
kalem bir kusuru değil bir **sürtünmeyi** kaldırır. Buna karşılık sürtünme ilk
beş dakikadadır ve orada kaybedilen tüketici geri gelmez.

> **Plana dönüştü:** [Faz 67](fazlar/67-ISTEGE-BAGLI-MIGRATION-SETI.md) · taşındı 2026-08-18

---

### F-111 · Çalıştırma kimliği ve maliyet kırılım boyutları

**Sorun:** `RunRecord` kiracı, oturum ve agent taşır; **kullanıcı taşımaz** —
`grep -rn "UserId" src` repo genelinde **0 sonuç** verir. Amaç/etiket alanı da
yoktur. `RunStatisticsQuery` yalnız agent ve kiracıya göre filtreler, kırılım
agent · sürüm · modeldir
([`RunStatistics.cs`](../../src/Tracon.Abstractions/Runs/RunStatistics.cs)).
Sonuç: çok kiracılı bir üründe "hangi kullanıcı ne harcadı" ve "hangi özellik ne
harcadı" **sorulamaz**. Kiracı-içi faturalama ve iç maliyet dağıtımı imkânsızdır.
**Kapsam:** Çalıştırmaya kullanıcı kimliği ve serbest etiket kümesi; bu
boyutların kayda, filtreye, istatistik kırılımına ve arayüze taşınması.
**Değer:** Kiracı bazlı kotayı [Faz 21](fazlar/21-KOTA-VE-OLAY-YAYINI.md) çözdü; kiracı
**içindeki** dağıtımın hiçbir boyutu yok.
**Mercek:** 2, 3, 7, 8.
**Hazırlık:** Sıfırdan.
**Maliyet:** 🚨 **En geniş yüzey** — `RunRecord`, `RunStartInfo`,
`RunStatisticsQuery`, `RunStatistics`, HTTP filtreleri, arayüz, bir migration.
**Risk:** 🚨 **Kullanıcı kimliği kişisel veridir.** Tracon kişisel kimlik
**saklamamalıdır**; alan opak bir dizedir ve anlamını tüketici verir. Aynı duruş
[Faz 64](fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md)'ün `IDataSubjectResolver`
kararıyla tutarlıdır. Etiketler kardinaliteyi patlatabilir: metrik etiketi
**değil**, yalnız sorgu boyutu olmalıdır.
**Bağımlılık:** F-112 aynı tabloya dokunur.
[Faz 64](fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) veri konusu haklarını
tanımlar — kullanıcı kimliği eklemek o kapsamı büyütür.
**Ekosistem:** 2026-08-18 — [Langfuse](https://langfuse.com/docs/observability/features/token-and-cost-tracking)
her trace'te `user_id` + `session_id` + değişmez etiket taşır;
[Braintrust](https://www.braintrust.dev/articles/how-to-track-llm-costs-2026)
harcamayı kullanıcı, özellik ve model bazında kıran özel etiketler sunar.
.NET'te karşılığı yok.
**Karşı görüş:** `SessionId` opak bir dizedir; tüketici oraya kendi kullanıcı
kimliğini yazabilir. Buna karşılık o zaman oturum ile kimlik kavramı çakışır —
bir kullanıcının çok oturumu vardır ve `session_id`'yi kimlik yapmak geçmiş
gruplamasını bozar. Etiket boyutu ise bu numarayla hiç elde edilemez.

> **Plana dönüştü:** [Faz 68](fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) · taşındı 2026-08-18

---

### F-112 · Cache ve reasoning token kırılımı

**Sorun:** `RunUsage` üç alan taşır
([`RunSupportTypes.cs:6`](../../src/Tracon.Abstractions/Runs/RunSupportTypes.cs)) ve
[`RunRecordingAgent.cs:1056`](../../src/Tracon.Core/Recording/RunRecordingAgent.cs)
`UsageDetails`'ten yalnız `InputTokenCount`/`OutputTokenCount`/`TotalTokenCount`
alır. 🚨 **Ölçüldü:** sabitlenen `Microsoft.Extensions.AI.Abstractions`
**10.8.3**'te `UsageDetails` ayrıca `CachedInputTokenCount`,
`ReasoningTokenCount`, `InputAudioTokenCount`, `InputTextTokenCount`,
`OutputAudioTokenCount`, `OutputTextTokenCount` ve `AdditionalCounts` taşır —
hepsi atılıyor. Sözleşme dokümanı cache'lenmiş girdinin `InputTokenCount`
**içinde** sayılacağını yazar; `IRunPricingResolver` tüm girdiyi tam fiyattan
hesapladığı için prompt caching açık bir agent'ın maliyeti **fazla** raporlanır.
**Kapsam:** `RunUsage`'a kırılım alanları; fiyat yapılandırmasına cache-read,
cache-write ve reasoning birim fiyatı; arayüzde kırılım gösterimi.
**Değer:** Prompt caching'in getirisi ölçülemiyor.
[Faz 26](fazlar/26-ANTHROPIC-VE-GEMINI.md) caching ayarını getirdi, kazancı görünmüyor.
**Mercek:** 2, 7, 8.
**Hazırlık:** 🚨 **Yüksek** — veri tipli olarak zaten geliyor. Yazılacak olan
eşleme ve fiyatlandırmadır.
**Maliyet:** `RunUsage` + `RunCost` alanları, bir migration, arayüzde bir çubuk.
**Risk:** Bir sağlayıcının alanı doldurup doldurmadığı sağlayıcıya bağlıdır.
Doldurmayanda alan **`null` kalmalıdır — sıfır değil**; bu, "eksik kullanım
bilgisi bilinmiyor olarak kalır" kuralının aynısıdır.
**Bağımlılık:** F-111 ile aynı tabloya dokunur — sıra kararı gerekir.
**Ekosistem:** 2026-08-18'de ölçüldü — `Microsoft.Extensions.AI.Abstractions`
10.8.3 `UsageDetails` üyeleri.
**Karşı görüş:** Ciddi bir karşı gerekçe bulunamadı. Veri hazır, sözleşme
belgelenmiş, bugünkü sonuç yanlış bir sayıdır.

> **Plana dönüştü:** [Faz 68](fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) · taşındı 2026-08-18

---

### F-113 · Tool düzeyinde yetkilendirme ve etki sınıfı

**Sorun:** `ToolDescriptor` yalnız `RequiresApproval` taşır
([`ToolDescriptor.cs`](../../src/Tracon.Abstractions/Tools/ToolDescriptor.cs));
gerekli izin adı da etki sınıfı da yoktur. `IToolAuthoriz*` deseni repo
genelinde **0 sonuç** verir. Sonuç: "bu kullanıcı bu tool'u hiç çağırabilir mi"
sorusu **ifade edilemez**. Onay bir insan kapısıdır — izin değildir.
**Kapsam:** `ToolDescriptor`'a gerekli izin adı ve etki sınıfı
(`Read`/`Write`/`Destructive`/`External`); yürütmeden önce çağrılan,
değiştirilebilir bir yetkilendirme kancası; arayüzde etki rozeti.
**Değer:** Kurumsal kapı. Yıkıcı bir tool'un tek kapısı bugün onaydır ve onay
her kullanıcıya açıktır.
**Mercek:** 2, 3, 5.
**Hazırlık:** Sıfırdan. Sarmalama **registry'de** yapılır — kapıyı atlayan bir
kod yolu olamaz.
**Maliyet:** Bir `record` alanı, bir enum, bir arayüz.
**Risk:** 🚨 Tracon **kullanıcı ve rol saklamaz** ve saklamamalıdır. Kanca
kimliği çözmez, tüketicinin kimlik hattına **sorar**. Varsayılanı izin vermek
olmalıdır, yoksa `AddTracon()` tek başına çalışmaz (K1).
**Bağımlılık:** F-114 aynı `record`.
[Faz 63](fazlar/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md) `ToolApprovalRule`'a
dokunuyor — sıra kararı gerekir.
**Ekosistem:** 2026-08-18 — [LiteLLM tool izin
guardrail'i](https://docs.litellm.ai/docs/proxy/guardrails/tool_permission) ve
[MCP tool bazlı izin
yönetimi](https://docs.litellm.ai/docs/mcp_control); [Portkey MCP
Gateway](https://portkey.ai/features/mcp) takım seviyesinde tool izni. .NET'te
karşılığı yok.
**Karşı görüş:** Yetkilendirme tüketicinin işidir ve tool gövdesinde
yapılabilir. Buna karşılık bu, kararı **model çağırdıktan sonraya** bırakır:
tool zaten şemayla modele gösterilmiş ve çağrılmıştır. İzin, tool'un modele
**gösterilip gösterilmeyeceğini** de belirlemelidir.

> **Plana dönüştü:** [Faz 69](fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) · taşındı 2026-08-18

---

### F-114 · Tool yürütme timeout'u

**Sorun:** Timeout yalnız iki yerdedir — `TraconOptions.cs:71` (`McpTimeout`)
ve `:210` (skill script). Kodda tanımlı bir tool için yürütme timeout'u
**yoktur**. Harici API çağıran bir tool turu süresiz uzatır;
`MaximumIterationsPerRequest` adım sayar, süre saymaz.
**Kapsam:** `ToolDescriptor`'a timeout; kayıt anında varsayılan; aşımda tipli
hata ve `ToolFailed` olayı.
**Değer:** Nöbetçi mühendis "bu run neden yirmi dakikadır açık" sorusunu bugün
cevaplayamaz.
**Mercek:** 2, 5.
**Hazırlık:** Sıfırdan; `CancellationTokenSource.CreateLinkedTokenSource` yeter.
**Maliyet:** Düşük.
**Risk:** Timeout iptalden **ayrı** bir hata sınıfı olmalıdır, yoksa
`DefaultRunErrorClassifier` ikisini karıştırır. İptal ve guard blokları gibi
timeout da circuit breaker sayacına girmemelidir.
**Bağımlılık:** F-113 ile **aynı `record`'a** dokunur — birlikte planlanmalı.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Tool yazarı gövdede kendi `CancellationToken`'ını zaten
kullanabilir. Buna karşılık bu, her tool yazarının doğru yapmasını gerektirir;
sözleşmeye koymak tek noktada çözer — `MaxPayloadLength` ile aynı gerekçe.

> **Plana dönüştü:** [Faz 69](fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) · taşındı 2026-08-18

---

### F-115 · Çalıştırma olayı hedefi ve `ReasoningDelta`

**Sorun:** İki parça. **(a)** `RunEvent` yalnız `IRunStore`'a yazılır
([`RunEventWriter.cs:22`](../../src/Tracon.Core/Recording/RunEventWriter.cs));
canlı olayı gözlemleyecek genişleme noktası yoktur. Kendi gerçek zamanlı
arayüzüne gömen tüketici ya HTTP SSE ile kendine bağlanır ya `IRunStore`'u
dekore eder — kalıcılık ile yayını karıştıran yanlış bir yer. **(b)**
`RunEventType` 0–21 arasıdır; `ReasoningDelta` yoktur, düşünme akışı
`MessageDelta`'ya karışır.
**Kapsam:** Kayıt edilebilir bir olay hedefi (`TryAdd`, varsayılan boş) ve
enum'a `ReasoningDelta` eklenmesi.
**Değer:** [Faz 61](fazlar/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) tarayıcı
tarafını çözdü; sunucu tarafı taşıyıcısı hâlâ yok. SignalR'a, mesaj kuyruğuna
veya özel bir taşıyıcıya bağlanmak bugün tüketicinin kodudur.
**Mercek:** 1, 2, 6.
**Hazırlık:** Yazma noktası tektir — `RunEventWriter`.
**Maliyet:** Bir arayüz, bir enum değeri.
**Risk:** 🚨 **Hedef bir run'ı asla bozmamalıdır** — `RunEventWriter`'ın
yut-ve-devam kuralı hedefe de uygulanır, yoksa gözlemlenebilirlik işlevi
düşürür. Tek yazıcı garantisi (canlı akış ≡ replay) korunmalıdır.
`ReasoningDelta` enum'un **sonuna** eklenir; sıra değişmez (K-040).
**Bağımlılık:** Yok.
**Ekosistem:** Taranmadı.
**Karşı görüş:** `IRunStore` zaten değiştirilebilir; tüketici dekore edebilir.
Buna karşılık bu, her tüketiciyi kalıcılık sözleşmesinin tamamını yeniden
uygulamaya zorlar ve depo hatası ile yayın hatası aynı yut-geç kuralına girer —
ikisi ayrı sorumluluktur.

> **Plana dönüştü:** [Faz 70](fazlar/70-CALISTIRMA-OLAYI-HEDEFI.md) · taşındı 2026-08-18

---

### F-116 · Workflow kod düğümü

**Sorun:** `WorkflowNodeKind` dört değer taşır — `Agent`, `Orchestration`,
`RequestPort`, `Output`
([`WorkflowGraph.cs:94`](../../src/Tracon.Abstractions/Workflows/WorkflowGraph.cs)).
Düğüm **agent'tır**. AI çağırmayan bir adım (dosya indirme, TTS, dönüştürme,
veritabanı yazımı) grafiğe giremez. Sonuç: gerçek bir üretim hattının yalnız AI
kısmı devredilebilir, orkestrasyon tüketicinin kuyruğunda kalır.
**Kapsam:** Kayıtlı bir kod fonksiyonunu düğüm olarak bağlayan bir düğüm tipi —
tipli girdi/çıktı, kontrol noktasına katılan, run ağacında ve arayüzde görünen.
**Değer:** Bir pipeline'ın **tamamının** devredilebilmesi buna bağlıdır. Bugün
Workflows yalnız agent zinciri kurabiliyor.
**Mercek:** 1, 2, 6.
**Hazırlık:** MAF `Microsoft.Agents.AI.Workflows` executor kavramına sahip —
imza `maf-api-kesfi` ile **ölçülmeli, tahmin edilmemeli**.
**Maliyet:** Orta. Enum ekleme, `WorkflowDefinition` alanı, kayıt yolu, arayüzde
düğüm şekli.
**Risk:** 🚨 **K2 sınırındadır.** Fonksiyon **kodda kayıtlıysa** grafik ona
yalnız işaret eder — `AgentDefinition.ToolNames`'in kayıtlı tool'a işaret
etmesiyle aynı. Gövde arayüzden veya veritabanından gelirse K2 delinir. Bu sınır
faz planının **ilk adımında** yazılmalıdır.
**Bağımlılık:** Yok.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Tüketici AI olmayan adımı kendi kuyruğunda tutup workflow'u
yalnız AI kısmı için kullanabilir. Buna karşılık o zaman iki orkestratör, iki
durum kaynağı ve iki kurtarma yolu doğar — Workflows'un vaadi tam olarak bunu
ortadan kaldırmaktı.

> **Plana dönüştü:** [Faz 71](fazlar/71-WORKFLOW-KOD-DUGUMU.md) · taşındı 2026-08-18

---

### F-117 · Talimatta çok dillilik

**Sorun:** `AgentDefinition.Instructions` tek bir `string?`'tir
([`AgentDefinition.cs:33`](../../src/Tracon.Abstractions/Agents/AgentDefinition.cs)).
İki dilde çalışan bir üründe iki ayrı agent tanımı gerekir; versiyon
geçmişleri, eval kümeleri ve deneyleri **ayrışır**.
**Kapsam:** Kültür anahtarlı talimat; çözümleme çalışma anında, varsayılana
geri düşüşle.
**Değer:** Arayüz [Faz 30](fazlar/30-ARAYUZ-CILASI.md)'da yerelleşti; agent tanımı
yerelleşmedi.
**Mercek:** 1, 5.
**Hazırlık:** Sıfırdan.
**Maliyet:** `AgentDefinition` alanı, bir migration, arayüzde bir sekme.
**Risk:** [Faz 19](fazlar/19-SURUM-KARSILASTIRMA-VE-AB.md) sürüm karşılaştırması ve
[Faz 18](fazlar/18-DEGERLENDIRME.md) eval'i **tek** bir talimat metnine bakar. Kültür
eklenince "hangi metnin versiyonu" sorusu doğar. Kültür versiyonun **içinde mi
dışında mı** — plan bunu karara bağlamalıdır.
**Bağımlılık:** Yok.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Tüketici talimatı kendi kaynak dosyasından şablonla üretebilir
ve **F-34 (şablon) bunu zaten kapsayabilir**. F-34 planlanırsa bu kalem onun
içinde erimelidir; ayrı bir alan eklemek erken olabilir.

> **Plana dönüştü:** [Faz 72](fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) · taşındı 2026-08-18

---

### F-118 · Zaman damgalı konuşma sentezi

**Sorun:** `ElevenLabsSpeechClient` yalnız `text-to-speech` ve `/stream`
uçlarını çağırır
([`ElevenLabsSpeechClient.cs:318`](../../src/Tracon.Voice/Internal/ElevenLabsSpeechClient.cs)).
Kelime düzeyinde zaman damgası döndüren uç desteklenmiyor. Sentezlenen sesi
metinle hizalamak — altyazı, vurgulama, transcript senkronu — mümkün değil.
**Kapsam:** Konuşma sentezi tool'una isteğe bağlı zaman damgası çıktısı;
sözleşmeye hizalama verisi.
**Değer:** Sesli içerik üreten tüketici bugün ElevenLabs'ı ayrıca kendi çağırmak
zorunda — paketi kullanmasının bir kısmı boşa gidiyor.
**Mercek:** 1.
**Hazırlık:** Sağlayıcı ucu hazır; iş eşlemedir.
**Maliyet:** Düşük.
**Risk:** `Tracon.Voice` sözleşmesini büyütür. Zaman damgası akışlı sentezde
farklı gelir — iki yol ayrı ele alınmalıdır.
**Bağımlılık:** Yok.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Yalnız bir mercekten iyi görünüyor (1) ve tek bir satıcının
ucuna bağlıdır. Zayıf kalemdir; ses tarafında başka bir işle birleşmedikçe tek
başına faz olmamalıdır.

> **Plana dönüştü:** [Faz 72](fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) · taşındı 2026-08-18

---

### F-119 · Kiracı bazlı sağlayıcı allowlist'i

**Sorun:** Bir kiracının verisinin hangi sağlayıcıya gidebileceğini kısıtlayan
mekanizma yok. Ölçüldü: `grep -rn "AllowList\|Allowlist\|AllowedProviders" src`
**üç** sonuç verir ve üçü de aynı şeydir —
[`TraconOptions.cs:207`](../../src/Tracon.Core/TraconOptions.cs)
`EnvironmentAllowList` (skill script ortam değişkeni). Sağlayıcı tarafında
allowlist yoktur; webhook için `WebhookUrlValidator` ayrı bir mekanizmadır.
`OpenAICompatible` ile herhangi bir adrese kiracı verisi gönderen bir agent
tanımlanabilir.
**Kapsam:** Kiracı başına izinli sağlayıcı listesi; agent tanımının
**derlenmesinde** doğrulanır, çalışma anında değil.
**Değer:** Veri ikametgâhı kurumsal bir satın alma kapısıdır.
**Mercek:** 3.
**Hazırlık:** Sıfırdan; `AgentDefinitionValidator` doğru yer.
**Maliyet:** Düşük.
**Risk:** [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) sağlayıcı çözümlemesini
zaten değiştirecek. Bunu ondan **önce** yapmak aynı yere iki kez dokunmaktır.
**Bağımlılık:** [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) — birlikte
planlanmalı.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Yalnız bir mercekten iyi görünüyor (3). Tek başına zayıftır —
Faz 65'in kapsamına katılmadıkça planlanmamalıdır.

> **Plana dönüştü:** [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) · taşındı 2026-08-18

## Faz 77 bütçe rahatlatması — kapanmış kalemler ve tur anlatısı

#### F-104 · Örnek uygulama rol politikaları — ✅ KAPATILDI (2026-08-18)

> **Aday değildir.** Faza dönüşmeden bir kusur olarak düzeltildi. `samples/Tracon.Api`
> artık `Tracon:Demo:Roles:Enabled` bayrağıyla üç politikayı kaydeder ve o anda
> `RequireRolePolicies`'i açar — kayıt silinirse uygulama **başlamaz**. Gerçek koşumla
> kanıtlandı: başlıksız `401`, `reader` liste `200`, `reader` yazma `403`, `admin` `201`;
> bayrak kapalıyken davranış birebir eskisi (`200`/`201`). Tam gerekçe: **K-431**.

#### F-105 · Dosya belleği kiracı-içi sınırı — ✅ KAPATILDI (2026-08-18)

> **Aday değildir.** Kusur olarak düzeltildi: `TenantPrefixingAgentFileStore` öneki artık
> `{tenantId}/{agentName}`'dir. 🚨 Adayın önerdiği `AsyncLocal` yolu **alınmadı** —
> gerek yoktu: agent adı derleme anında bilinir (`CompiledAgentCache` anahtarı zaten
> taşır). Oturum boyutu **bilerek** kapsam dışıdır; dosya belleği agent düzeyinde bir
> bellektir ve oturum başına yalıtmak yeteneği yok ederdi. Tam gerekçe: **K-434**.

#### F-107 · Workflow iptali — ✅ KAPATILDI (2026-08-18)

> **Aday değildir.** Kusur olarak düzeltildi ve süreç içinde **yeniden üretildi**
> (düşen test önce kırmızıydı: `Completed`, beklenen `Canceled`). Kök neden ölçüldü:
> MAF grafiği token'ı honor etmiyor **ve istisna da atmıyor** — akışı sessizce
> bitiriyor. Zorlama süper-adım sınırında ve pompa çıkışında yapılır.
> Tam gerekçe: **K-432**.

- **F-113** Tool düzeyinde yetkilendirme ve etki sınıfı → [Faz 69](fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) 📋 · gövdesi: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)
- **F-119** Kiracı bazlı sağlayıcı allowlist'i → [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) ✅ · gövdesi: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)

#### F-76 · Paylaşılan SQL kaynağının XML doküman çakışması — KAPATILDI (2026-08-08)

> **Aday değildir.** Faza dönüşmeden bir kusur olarak düzeltildi;
> tam gerekçe ve koruma testi **K-352**'dedir.

#### F-72 · Agent Control Specification (ACS) uyumu — ERTELENDİ (2026-08-06)

> **Aday değildir.** Kullanıcı kararıyla ertelendi. Ölçülmüş kanıt (ACS
> şeması, kesişim noktaları, eşleme tablosu) arşivdedir:
> [`arsiv/ERTELENEN-ADAYLAR.md`](ERTELENEN-ADAYLAR.md).

### Bu Turda Neyin Değiştiği

> 🚨 **2026-08-18 turu bu tabloyu değiştirdi.** Yedi kalem daha plana dönüştü
> (Dalga 6 ve 7 → Faz 61–66) ve **üç kalem ölçümle kapandı**: F-100 (kota eşiği
> webhook'u kodda **var**), F-102 (yeniden deneme sınırı K-385 ile 10'a çıktı),
> F-103 (kapsam taksonomisi K-397/K-405/K-407 ile tamamlandı; `ApiKeyScope`
> bugün **17 üye** taşıyor ve yalnız `MetaEndpoints`/`UiEndpoints` muaf).
> Kalan açık kalem sayısı **22** — aynı turda **F-104, F-105 ve F-107 kusur olarak
> kodlandı ve kapandı** (K-431, K-434, K-432). F-106 azaltıldı ama doğrulanmadı ve
> açık kalır (K-433).

> 🚨 **Aynı gün ikinci bir tur koştu — tüketici raporu turu.** Kaynağı bu depo
> değildi: gerçek bir tüketici projesi `docs-site/`'ın 91 sayfasını tarayıp bir
> uygulanabilirlik raporu üretti; rapor ölçüldü ve **on yeni kalem** doğdu
> (F-110…F-119). Turun tamamı:
> [`kesif/2026-08-18-tuketici-raporu.md`](kesif/2026-08-18-tuketici-raporu.md).
>
> 🚨 **Onu da aynı gün plana dönüştü** — altı yeni faz (67–72) ve F-119'un
> [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md)'e katılması. Açık kalem sayısı
> **22'de kaldı**; gövdeler
> [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)'dedir.
>
> Turun kendisi bir ders verdi: on kalemin **hiçbiri** faz listesine bakarak
> görünmüyordu. Üçü stratejik boşluktu (F-110 kurulum engeli, F-113 tool izni,
> F-111 maliyet dağıtımı) ve üçü de ancak paketi **gömmeye çalışınca** ortaya
> çıktı.

| Ne | Sonuç |
|---|---|
| Bu listede kalan kalem | **23** (2026-08-18, üç tur sonrası) — kırk dört ID plana dönüştü, altı kalem kapandı |
| Tüketici raporundan doğan | **10** — F-110…F-119, **onu da aynı gün plana dönüştü** (Faz 65, 67–72); ayrıca iki kalem kalıcı olarak reddedildi (fatura üretimi, harici hosted agent yönetimi) |
| Plana dönüşen | **37** (2026-08-18 sonu) — Dalga 9: F-120 → Faz 73 · Dalga 8: F-110…F-119 → Faz 65, 67–72 · önceki 26 kalem:  Dalga 1: F-35, F-38, F-49, F-52, F-60, F-62, F-70, F-73 → Faz 31–37 · Dalga 2: F-37, F-42, F-46, F-53, F-55, F-57, F-63, F-76 → Faz 38–45 · Dalga 3: F-30, F-31, F-32, F-33, F-47, F-54, F-66, F-68, F-71 → Faz 46–52 (F-39 F-68'in içinde) |
| İptal edilen | **1** — F-43, çünkü tamamlandı |
| Seçildi ama **ertelendi** | **1** — F-72; ölçüm erteleme getirdi ve kanıt bölümüne yazıldı |
| Kanıtı düzeltilen | **19** — Dalga 1–2'de 11, Dalga 3'te 8. Kalemler ayakta, gerekçeler değişti |
| Yükseltilen | **7** — F-30, F-32, F-33, F-44, F-52, F-53, F-54 |
| Tek faza birleşen | **3 çift** — F-31+F-33, F-54+F-66, F-68 F-39'u yutar |
| Kapsamı daraltılan | **2** — F-63 (TypeScript/npm çıkarıldı) · F-30 (yalnız PostgreSQL) |
| Aciliyeti **artan** | **4** — F-36, F-56, F-69, F-74; hepsi Dalga 3'ün çıktısına bağlı |

**ID'ler sabittir.** F-35 her zaman "çalıştırma iptali"dir — kalem plana
dönüşse bile ID yeniden kullanılmaz; ID olmadan sonraki oturumun referansları
kaybolur. Bugün en büyük numara **F-119**'dur (2026-08-18, tüketici raporu
turu); yeni kalemler oradan devam eder.

**Kod kanıtları 2026-08-06'da bu depo üzerinde `grep` ile yeniden
doğrulandı.** Depo ilerledikçe satır numaraları kayar. Bir kanıtı
kullanmadan önce yeniden ölç.

---

## Faz 77 — kapanmış aday satırlarının gerekçeleri

### F-121

| ~~**F-121**~~ | ✅ **KAPANDI (2026-08-20)** — kapsamı ölçümle değişti → [Faz 74](fazlar/74-YEREL-REFERANS-YUZEYI.md) tamamlandı | 2026-08-18 tüketici agent turu · [Faz 73](fazlar/73-TUKETICI-AGENT-DESTEGI.md) | 🚨 **Kaydın istediği ölçüm yapıldı (2026-08-19) ve `dotnet tool` MCP sunucusu okumasını düşürdü.** Paket **2.96 MB** XML dokümanı (~5 600 üye) sevk ediyor ve o korpus tüketicinin `~/.nuget/packages` dizininde **zaten duruyor**; on gerçek detay sorgusunun **onu da** `grep` ile cevaplandı. Sunucunun `grep` üzerine koyacağı tek yeni yetenek anlamsal aramadır — o da RAG'dir ve Dalga 9'da elendi. Maliyet yapısal: `grep -rn PackAsTool` **boş** — yeni dağıtım kanalı, F-93 ile aynı sınıf; benimseme Faz 73'ün opt-in özelliğinden **kötü**. Ölçüm üç gerçek boşluk buldu ve Faz 74 onları alır: yerel korpusa hiçbir işaret yok, `tracon.json` (123 path) hiçbir pakete girmiyor, 39 giriş noktasının **27'sinde** çalışan örnek yok. Sunucu reddedilmedi, gerekçesi düştü; Faz 74'ün ölçümüyle yeniden açılabilir |

### F-102

| ~~**F-102**~~ | ✅ **KAPANDI (2026-08-18)** — kırılgan eşzamanlılık testi | 2026-08-08 denetimi | Karar verildi ve uygulandı: **K-385** yeniden deneme döngüsüne jitter ekledi ve üst sınırı 5 → **10**'a çıkardı ([`SqlEvalStore.cs:179`](../../src/Tracon.Sql.Shared/Stores/SqlEvalStore.cs)). Özgün kayıt: 🚨 **Ölçüldü:** `AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` PostgreSQL paketinin tamamı koşarken düştü (`SqlEvalStore.AddCaseAsync:221` — "5 denemede sira numarasi atanamadi"), **tek başına ve ikinci tam koşumda geçti** (870/870). Testin kendisi mi yoksa `AddCaseAsync`'in 5 denemelik yeniden deneme sınırı mı yetersiz — karara bağlanmalı. Bir kusur değil, **kırılgan bir test** olarak sınıflandırıldı ama sessiz bırakılmadı |

---

### F-133

| ~~**F-133**~~ | ✅ **KAPANDI (2026-08-21)** — düzeltildi, K-540/K-541 | Faz 77 kapanış koşumu (2026-08-20) | **Kaydın teşhisi 2026-08-21'de ölçümle DÜZELTİLDİ.** Kayıt "izolasyonda HER ZAMAN düşüyor, tam sette bazen geçiyor" diyordu ve zamanlama yarışı sanıyordu. Gerçek: pencere bir yarış DEĞİL, **sabit bir sıraydı** ve onay isteyen HER kuyruk çalıştırmasında açıktı. `RunRecordingAgent` çalıştırmayı `agent.RunAsync`'in İÇİNDE `AwaitingApproval` ile kapatıyor ([`AgentRunJobHandler.cs:92`](../../src/Tracon.Core/Scheduling/AgentRunJobHandler.cs)), onay satırı ise çağrı döndükten sonra yazılıyordu (satır 150) — arada `GET /api/approvals/pending` boş dizi veriyordu. Kırılgan olan tek şey tüketicinin o pencereye bakıp bakmadığıydı. Deterministik düşen test yazıldı (`ApprovalEndpointTests.Approval_row_exists_before_the_run_reports_AwaitingApproval`, casus bir `IPendingApprovalStore` ile) ve düzeltmeden önce kırmızıydı. Çözüm K-541: sıra **oturum → onay satırı → durum**, `TraconRunOptions.BeforePendingApprovalIsPublished` kancasıyla zorlanır. Özgün kayıt: `Second_decision_on_the_same_approval_gets_409`, `ApprovalEndpointTests.cs:159`, `ShouldHaveSingleItem` → 0 öğe. |

---

## Dalga 1–3 eşleme tabloları (ADAYLAR.md'den taşındı, 2026-08-21)

> Faz 31–52'nin kalem→faz eşlemesi. Faz durumu [`YOL-HARITASI.md`](../YOL-HARITASI.md)'dedir.

### Dalga 1 → Faz 31–37

| Kalem | Faz |
|---|---|
| **F-35** Çalıştırma iptali | [Faz 32](fazlar/32-CALISTIRMA-IPTALI.md) |
| **F-38** ASP.NET Core `IHealthCheck` | [Faz 33](fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md) |
| **F-49** `dotnet new` şablon paketi | [Faz 37](fazlar/37-PROJE-SABLONU.md) |
| **F-52** Geri bildirim ve puanlama | [Faz 31](fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md) |
| **F-60** Tanım doğrulama ucu | [Faz 34](fazlar/34-TANIM-DOGRULAMA-UCU.md) |
| **F-62** Yapılandırma teşhisi | [Faz 33](fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md) |
| **F-70** Maliyet ve kota OTel metrikleri | [Faz 35](fazlar/35-MALIYET-VE-KOTA-METRIKLERI.md) |
| **F-73** Saklama `MaxRows` uygulaması | [Faz 36](fazlar/36-SAKLAMA-HACIM-SINIRI.md) |

### Dalga 2 → Faz 38–45

| Kalem | Faz |
|---|---|
| **F-37** `Idempotency-Key` desteği | [Faz 43](fazlar/43-IDEMPOTENCY-KEY.md) |
| **F-42** Yapılandırılmış çıktı (JSON şeması) | [Faz 38](fazlar/38-YAPILANDIRILMIS-CIKTI.md) |
| **F-46** `Tracon.Testing` paketi | [Faz 39](fazlar/39-TEST-PAKETI.md) |
| **F-53** Üretimden eval kümesi toplama | [Faz 45](fazlar/45-URETIMDEN-EVAL-KUMESI.md) |
| **F-55** Hata sınıflandırma ve arıza kümeleme | [Faz 44](fazlar/44-HATA-SINIFLANDIRMA.md) |
| **F-57** Tek yürütücü seçimi | [Faz 42](fazlar/42-TEK-YURUTUCU-SECIMI.md) |
| **F-63** OpenAPI yayını | [Faz 40](fazlar/40-OPENAPI-YAYINI.md) |
| **F-76** Kiracı yalıtımının zorlanması | [Faz 41](fazlar/41-KIRACI-YALITIMININ-ZORLANMASI.md) |

### Dalga 3 → Faz 46–52

| Kalem | Faz |
|---|---|
| **F-30** Vektör bellek ve RAG | [Faz 51](fazlar/51-VEKTOR-BELLEK-VE-RAG.md) |
| **F-31** Tracon'in MCP sunucusu olması | [Faz 50](fazlar/50-DISA-ACILAN-AGENT-YUZEYI.md) |
| **F-32** Guardrails | [Faz 48](fazlar/48-GUARDRAILS.md) |
| **F-33** A2A protokolü | [Faz 50](fazlar/50-DISA-ACILAN-AGENT-YUZEYI.md) |
| **F-47** Kaynak üreteci | [Faz 52](fazlar/52-KAYNAK-URETECI.md) |
| **F-54** Yeniden oynatma | [Faz 47](fazlar/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) |
| **F-66** Konuşma dallandırma | [Faz 47](fazlar/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) |
| **F-68** Dayanıklı çalıştırma (F-39 içinde) | [Faz 46](fazlar/46-DAYANIKLI-CALISTIRMA.md) |
| **F-71** Çevrimiçi değerlendirme | [Faz 49](fazlar/49-CEVRIMICI-DEGERLENDIRME.md) |

---

## Dalga 1–3 anlatısı ve doğurdukları (ADAYLAR.md'den taşındı, 2026-08-21)

> Doğurdukları kalemler F-90…F-99 olarak numaralandı; canlı gerekçeler
> [`ADAYLAR.md`](../ADAYLAR.md) § *Numaralandırılan kapsam-dışı işler* tablosundadır.
> Buradaki metin o numaralandırmadan **önceki** kayıttır.

### Dalga 1 — ✅ planlandı (2026-08-06), bu listeden çıktı

Sekiz kalemin tamamı [Faz 31–37](UCUNCU-FAZ-YOL-HARITASI.md) olarak plana
dönüştü. Bölümleri bu dosyadan silindi; yönlendirme için
[Plana Dönüşenler](../ADAYLAR.md#plana-dönüşenler-2026-08-06) tablosuna bakın.

**Kod yazılmadı.** Fazlar `📋 Planlandı` durumundadır.

### Dalga 2 — ✅ planlandı (2026-08-06), bu listeden çıktı

Sekiz kalemin tamamı [Faz 38–45](UCUNCU-FAZ-YOL-HARITASI.md) olarak plana
dönüştü. Bölümleri bu dosyadan silindi; yönlendirme için
[Plana Dönüşenler](../ADAYLAR.md#plana-dönüşenler-2026-08-06) tablosuna bakın.

**Kod yazılmadı.** Fazlar `📋 Planlandı` durumundadır.

Dalganın ortak gerekçesi korunur: **her kalem başka bir işten önce yapılmazsa
iki kat pahalıya gelir** — biri kırıcı bir sürüm kararı, biri yeniden yazım,
biri güvenlik düzeltmesi olarak geri döner.

### Dalga 2'den doğan yeni aday kalemler

Planlama dokuz işi **bilinçli olarak kapsam dışına** çıkardı. Bunlar yeni kalem
olarak buraya yazılmalıdır; ID'ler **F-77'den** devam eder.

| Kapsam dışı iş | Hangi fazdan | Neden ayrı bir kalem |
|---|---|---|
| PostgreSQL RLS ile derinlemesine savunma | [Faz 41](fazlar/41-KIRACI-YALITIMININ-ZORLANMASI.md) | SQLite'ta karşılığı **yok**; üç sağlayıcıda davranış ayrışır. Faz 41 sözleşme testi kapısını seçti, RLS'i **iptal etmedi** |
| 🚨 Çalıştırmanın alt yazmalarında **açık kiracı** | [Faz 41](fazlar/41-KIRACI-YALITIMININ-ZORLANMASI.md) | `IRunStore.AppendEventAsync` · `CompleteRunAsync` · `UpdateRunCostAsync` · `RecordToolInvocationAsync` kiracı süzgeci taşımaz (K-280). Ambient ile süzmek denendi ve geri alındı: `RunStartInfo.TenantId` ambient kiracıyı bilerek ezer ve süzgeç meşru yazmaları düşürüyordu. Gerçek denetim, çağrının **beklenen** kiracıyı taşımasını ister — yani `RunEvent`/`RunCompletion`/`ToolInvocationRecord`'a birer alan. Bugün ulaşılabilir sızıntı **yok** (uuid v7 kimlikler, okuma tarafı süzülü); public API büyüteceği için ayrı kalem |
| MCP OAuth token'ının örnekler arasında paylaşılması | [Faz 42](fazlar/42-TEK-YURUTUCU-SECIMI.md) | 🚨 **K-059 ile çatışır** — `secret` veritabanına yazılmaz. Kendi kararını ister |
| Paylaşılan (dağıtık) hız sınırı | [Faz 42](fazlar/42-TEK-YURUTUCU-SECIMI.md) | K-158 bunu bilerek bellekte tuttu; tek yürütücü seçimi bu sorunu **çözmez** |
| Akışlı yanıtta idempotency | [Faz 43](fazlar/43-IDEMPOTENCY-KEY.md) | Doğru evi F-68'in `202 Accepted` + `Location` sözleşmesidir |
| TypeScript istemci paketi ve npm yayını | [Faz 40](fazlar/40-OPENAPI-YAYINI.md) | İkinci bir dağıtım kanalı; ayrı yayın hattı, kimlik bilgisi ve sürümleme ister |
| Çok turlu eval vakası terfisi | [Faz 45](fazlar/45-URETIMDEN-EVAL-KUMESI.md) | `EvalCase` sözleşmesini değiştirir; Faz 7'den **önce** karara bağlanması ucuzdur |
| `TraconMcpOptions`'ı `IConfiguration`'a bağlamak | [Faz 42](fazlar/42-TEK-YURUTUCU-SECIMI.md) | Ölçüldü: `.UseMcp()` yalnız kod-taraflı `configure` delegesi kabul eder, `IConfiguration.Bind` hiç çağrılmaz — `Tracon:Mcp:RefreshInterval` gibi bir ortam değişkeni **sessizce hiçbir şey yapmaz**. Faz 42'den önce de böyleydi; ilk kez orada gerçek bir dağıtım denemesinde ortaya çıktı |
| 🚨 `BackgroundService` başlatma sırası migration'la yarışır | [Faz 42](fazlar/42-TEK-YURUTUCU-SECIMI.md) | Ölçüldü: `MigrationHostedService.StartAsync` migration'ları TAM bekler ama `BackgroundService.StartAsync` (taban sınıf) `ExecuteAsync`'i beklemeden döner; kayıt sırası `.UseMcp()` `.UseSqlite()`'tan önceyse `McpDiscoveryService`'in ilk SQL denemesi migration bitmeden çalışabilir ("no such table"). Kendiliğinden iyileşir (bir sonraki turda) ama gözlemlenebilir bir uyarı üretir. Kalıcı çözüm hosted service sırasını garanti etmek veya ilk turu geciktirmek — ikisi de kendi kararını ister |

### Dalga 3 — ✅ planlandı (2026-08-06), bu listeden çıktı

Dokuz kalem [Faz 46–52](UCUNCU-FAZ-YOL-HARITASI.md) olarak plana dönüştü.
Bölümleri bu dosyadan silindi; yönlendirme için
[Plana Dönüşenler](../ADAYLAR.md#plana-dönüşenler-2026-08-06) tablosuna bakın.

🚨 **F-72 seçildi ama plana dönüşmedi.** Ölçüm erteleme getirdi ve kalem
[C bölümünde](../ADAYLAR.md#f-72--agent-control-specification-acs-uyumu--ölçüldü-ertelendi-2026-08-06)
ölçülmüş kanıtıyla duruyor. Dalga bu yüzden sekiz değil **yedi** fazdır.

**Kod yazılmadı.** Fazlar `📋 Planlandı` durumundadır.

Dalganın ortak gerekçesi korunur: **her kalem kendi başına bir tur
büyüklüğündedir** ve hiçbiri eksik bir yarıyı tamamlamaz; her biri .NET'te
karşılığı **hiç bulunmayan** bir yetenek ekler.

### Dalga 3'ten doğan yeni aday kalemler

Planlama **on** işi bilinçli olarak kapsam dışına çıkardı. Tam liste ve
gerekçeleri [`arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](UCUNCU-FAZ-YOL-HARITASI.md)'nin
"Dalga 3'ün Açtığı Yeni Aday Kalemler" bölümündedir; burada tekrarlanmaz.
ID'ler **F-77'den** devam eder.

Öne çıkan üçü:

| Kapsam dışı iş | Hangi fazdan | Neden ayrı bir kalem |
|---|---|---|
| Tur bazlı kontrol noktası (F-68 Okuma B) | [Faz 46](fazlar/46-DAYANIKLI-CALISTIRMA.md) | 🚨 MAF agent düzeyinde kanca **vermiyor** — ölçüldü. Kancayı Tracon yazmak K3'ü zorlar |
| Azure AI Content Safety adaptörü | [Faz 48](fazlar/48-GUARDRAILS.md) | Ağırlık **4 paket** (ölçüldü) — sorun değil. Erteleme gerekçesi doğrulanamazlıktır (K-212 emsali) |
| `IVectorSearchStore`'un SQL Server / SQLite uygulaması | [Faz 51](fazlar/51-VEKTOR-BELLEK-VE-RAG.md) | SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın `sqlite-vec` uzantısı **ölçülmedi** |

---

## Dalga 13 Küme B → Faz 81 (ADAYLAR.md'den taşındı, 2026-08-21)

F-45 ve F-134 [Faz 81](fazlar/81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md)'e dönüştü.
Aşağıdaki iki gövde **plan yazılmadan önceki** aday kaydıdır. Faz dokümanı
planlama sırasında dört şeyi yeniden ölçtü ve iki kaydı değiştirdi; bugün
geçerli olan ölçüm oradadır.

### F-45 · Yanıt önbelleği

**Sorun:** Aynı soru iki kez sorulursa iki kez ödenir. Önbellek yok.
**Kapsam:** `DistributedCachingChatClient` boru hattına takılır.
**Değer:** Deterministik iş yüklerinde fatura düşer.
**Mercek:** 8.
**Hazırlık:** **Ölçüldü (2026-08-20):** `Microsoft.Extensions.AI.DistributedCachingChatClient`
ve `DistributedCachingChatClientBuilderExtensions.UseDistributedCache` pinlenmiş
sürümde (10.8.3) doğrudan reflection ile doğrulandı —
`~/.nuget/packages/microsoft.extensions.ai/10.8.3/lib/net10.0/Microsoft.Extensions.AI.dll`
içinde tip mevcut. Sürüm yükseltmesi gerekmiyor.
🚨 **İmzalar ölçüldü (2026-08-21, reflection).** Üç şey netleşti:

| Ölçüm | Sonuç |
|---|---|
| `DistributedCachingChatClient(IChatClient, IDistributedCache)` | 🚨 `IDistributedCache` bugün Tracon'de **hiç kullanılmıyor** (`grep -rn "IDistributedCache" src` → boş). Tüketici bir cache uygulaması seçmek zorunda kalır ve bellek içi olan çok örnekli kurulumda **sessizce işe yaramaz** — bu bir K1 (sıfır sürpriz) kararıdır |
| `CacheKeyAdditionalValues { get; set; }` — `IReadOnlyList<Object>` | Kiracı anahtara **yapısal** olarak karışır; elle string birleştirme gerekmez, **K-525 sağlanır** |
| `GetCacheKey(...)` ve `EnableCaching(...)` `protected virtual` | Tracon kiracı anahtarlamasını yapılandırmaya güvenmek yerine **zorlayabilir** |

**Maliyet:** Düşük — yeni MEAI paketi yok; `IDistributedCache` kaydı tüketicinin kararı.
**Risk:** Varsayılan **kapalı**. Agent'ın aynı soruya farklı yanıt vermesi
beklenen davranıştır; önbellek bunu bozar. Kiracı yalıtımı önbellek
anahtarında olmalıdır.
🚨 **Ölçülmedi:** önbelleğin boru hattındaki **konumu**. Telemetrinin dışına
konursa isabet eden bir çağrı `chat` span'i ve token kaydı **üretmez** (harcama
yok, doğru olabilir); içine konursa sıfır token'lı bir span yazar. Karar
Faz 68'in maliyet kırılımını etkiler ve plan anında verilmelidir
([`ModelProviderRegistry.cs:300-360`](../../src/Tracon.Core/Models/ModelProviderRegistry.cs)).
**Bağımlılık:** Yok.
**Ekosistem:** LiteLLM ve Portkey'de standart (önceki turların damgası; 2026-08-21'de
**yeniden doğrulanmadı**). Anthropic'in prompt caching'i ayrı bir kavramdır ve Faz 26'da zaten var.
**Karşı görüş:** `CacheKeyAdditionalValues` **örnek başına** bir özelliktir —
kiracı başına anahtarlama kiracı başına istemci örneği demektir; bu, bugünkü
`CompiledAgentCache` kiracı anahtarlamasıyla (K-380) hizalanmalıdır.

---

### F-134 · Eşzamanlı tool çağrısını açığa çıkar

**Sorun:** Bir turda birden çok bağımsız tool çağrıldığında bugün sırayla
çalışıyor. `FunctionInvokingChatClient.AllowConcurrentInvocation` MEAI'de
zaten var (**Ölçüldü**, aynı reflection: `strings` çıktısında
`AllowConcurrentInvocation`/`allowConcurrentInvocation` alanı görünüyor) ama
Tracon hiçbir yerde açmıyor (`grep -rn AllowConcurrentInvocation src`
yalnız bir **yorum** buluyor: [`ToolUsageAccumulator.cs:18`](../../src/Tracon.Core/Recording/ToolUsageAccumulator.cs#L18)
— sınıf zaten eşzamanlı çağrıyı bekleyerek `ConcurrentDictionary` kullanıyor,
ama tetikleyen ayar hiçbir yerde `true` değil).
**Kapsam:** `ChatClientBuilder` boru hattına bir agent/model tanımı bayrağı ekler.
**Ölçüldü (2026-08-21):** ekleme noktası doğrulandı —
[`ModelProviderRegistry.cs:317-321`](../../src/Tracon.Core/Models/ModelProviderRegistry.cs)
`.AsBuilder().UseFunctionInvocation(_loggerFactory).UseOpenTelemetry(...)`; K-320'nin
merkezi kurulum noktası ayakta.
**Değer:** Bağımsız tool'ları paralel çağıran bir turda gecikme düşer —
örn. üç farklı API'ye bakan bir araştırma agent'ı.
**Mercek:** 1, 4.
**Hazırlık:** **Ölçüldü (2026-08-20).** MAF 1.18.0 (2026-08-18) bu yeteneği
agent seviyesinde de öne çıkardı: "Allow agents to opt into concurrent tool
invocation" ([release notes](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.18.0)).
Pinlenmiş sürüm 1.16.0'da bu MEAI düzeyinde zaten var; agent-seviyesi
harness entegrasyonu doğrulanmadı.
**Maliyet:** Düşük — tek bayrak, yeni paket yok.
**Risk:** Sıralı yan etkili tool'lar (ör. birbirine bağımlı yazma işlemleri)
paralelleşirse sıra garantisi bozulur. Varsayılan **kapalı** olmalı; agent
tanımında açıkça seçilmeli.
**Bağımlılık:** Yok.
**Ekosistem:** MAF 2026-08-18'de bunu ürün özelliği olarak öne çıkardı —
kendi ekosistemimizin en taze sinyali.

🚨 **Asıl risk ölçüldü (2026-08-21): eşzamanlılık Tracon'in AMBIENT tool
bağlamına çarpar.** Tool katmanı iki yerde `FunctionInvokingChatClient.CurrentContext`
okuyor — [`AuthorizingAIFunction.cs:107`](../../src/Tracon.Core/Tools/AuthorizingAIFunction.cs#L107)
(çağrı kimliğiyle yetkilendirme) ve
[`TraconToolUsage.cs:56`](../../src/Tracon.Core/Tools/TraconToolUsage.cs#L56).
Bayrak açılınca tool gövdeleri eşzamanlı koşar; bu ambient'ın çağrı başına doğru
çözülüp çözülmediği **ölçülmemiştir**. Bu depo aynı sınıf tuzağı **beş kez**
yaşadı (`MEMORY.md`, `docs/hafiza/cekirdek-calistirma.md`). Plan bunu ilk iş
olarak bir davranış probuyla kanıtlamalıdır — bayrağı açmak son adımdır.

**Karşı görüş:** Bugüne kadar hiçbir manuel test veya kullanıcı bu gecikmeyi
sorun olarak bildirmedi; çoğu agent turu tek tool çağırıyor. Ölçülmeden
"performans kazancı" iddia edilemez — ilk adım gerçek bir çok-tool senaryosunda
benchmark almaktır, doğrudan bayrağı eklemek değil.

---

## Dalga 13 Küme C → Faz 82 (ADAYLAR.md'den taşındı, 2026-08-21)

F-41 [Faz 82](fazlar/82-ICERIK-KORUMASI.md)'ye dönüştü. Aşağıdaki gövde **plan yazılmadan
önceki** aday kaydıdır; planlama sırasında **üç iddiası çürütüldü** (F-87'nin öncülü,
dokümantasyon boşluğu, "arama tamamen biter") ve bugün geçerli olan ölçüm faz
dokümanındadır.

### F-41 · İçerik şifreleme (at-rest)

**Sorun:** `conversation_items` tam sohbet geçmişini açık saklıyor. Ekler
`attachments.content` sütununda `bytea` olarak açık duruyor
([`0006_attachments.sql:22`](../../src/Tracon.PostgreSql/Migrations/0006_attachments.sql)).

🚨 **Yüzey ölçüldü (2026-08-20 güvenlik taraması, B06-2): iki sütun değil, dokuz.**
`sessions.state` · `conversation_items.item` · `responses.payload` ·
`run_events.text` · `run_events.payload` · `tool_invocations.arguments/result` ·
`run_inputs.messages` · `attachments.content` · `agent_files.content`.
**Kullanıcı istemi ve model çıktısı `run_inputs.messages` ile `run_events.text`
içinde zaten tam metin durur** — yalnız ilk iki sütunu şifrelemek korumayı eksik
bitirir. Plan bu dokuz sütunu birlikte ele almalıdır.

**Kapsam:** `IContentProtector` genişleme noktası **yazılacak** — bugün böyle bir
tip yoktur (`rg -n 'IContentProtector' src/` → sıfır sonuç); varsayılan uygulama
yok (K4).

**Not:** Bu sınır bugün **hiçbir tüketiciye dönük belgede yazmıyor** (B06-1):
`docs-site/.../security.md`, `MIMARI-GUVENLIK.md` ve `README.md` içinde
`at rest`/`encrypt` geçmiyor. F-41 planlanana kadar bile bu bir dokümantasyon
boşluğudur.
**Değer:** Regüle sektörlerde zorunlu.
**Mercek:** 3.
**Hazırlık:** .NET Data Protection API kullanılabilir.
**Maliyet:** Orta.
**Risk:** Şifreli sütun **aranamaz**. Konuşma araması ve saklama sorguları
etkilenir. Anahtar döndürme bir tasarım kararıdır.
**Bağımlılık:** F-58 ile aynı veriye dokunur.
**Ekosistem:** Genel veritabanı deseni; agent'a özgü değil.

🚨 **F-87 ile TEK KÜME olarak planlanır (2026-08-21).** İkisi de aynı dokuz
sütuna dokunur; ayrı planlanırsa ikincisi birincisini bozar — Faz 64'ün
(F-75 + F-58) emsali birebir geçerlidir.

**Çatışma haritası — üç okuma yolu ölçüldü (2026-08-21):**

| Okuma yolu | Kanıt | Şifreleme / redaksiyon etkisi |
|---|---|---|
| Yeniden oynatma | [`RunReplayService.cs:30`](../../src/Tracon.Core/Replay/RunReplayService.cs) `IRunInputStore _inputs` | Replay **ham** girdiyi ister; redakte edilirse yeniden oynatma sadık değildir |
| Eval terfisi | [`RunToCasePromoter.cs:175`](../../src/Tracon.Core/Evaluation/RunToCasePromoter.cs) `_runs.ReadEventsAsync`, `:97` `ListToolInvocationsAsync` | Vaka `run_events` ve `tool_invocations` **ham metninden** üretilir |
| Dosya araması | [`SqlAgentFileStore.cs:212`](../../src/Tracon.Sql.Shared/Stores/SqlAgentFileStore.cs) `CollectMatches(Regex regex, string content)` | Regex **düz metin** üzerinde koşar; şifreleme dosya aramasını tamamen bitirir |

Ayrıca F-87'nin öncülü koddan doğrulandı:
[`TraconServiceCollectionExtensions.cs:870`](../../src/Tracon.Core/TraconServiceCollectionExtensions.cs#L870)
açıkça yazıyor — *"RunStarted event and IRunInputStore never passes through the guards."*

**Karşı görüş:** `IContentProtector` varsayılansız gelirse (K4) bugün **hiçbir
tüketici korunmaz**. Faz 73'ün "opt-in kararının bedeli benimseme oranıdır"
cümlesi burada da geçerlidir ve F-135 o cümlenin ilk ölçülen faturasıydı.
Kümenin ilk kararı koruma algoritması değil, bu **üç okuma yolunun ne olacağıdır**.

---

## Dalga 6–12 eşleme tabloları (ADAYLAR.md'den taşındı, 2026-08-21)

### Dalga 6 → Faz 61

| Kalem | Faz |
|---|---|
| **F-108** İstemci tarafında çalışan tool | [Faz 61](fazlar/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) |
| **F-64** Gömülebilir sohbet bileşeni | [Faz 61](fazlar/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) |

İkisi tek fazdadır: gömülebilir bileşen, istemci tool'unu **çalıştıran**
taraftır ve CORS ikisinin ortak ön koşuludur.

### Dalga 7 → Faz 62–66

| Kalem | Faz |
|---|---|
| **F-44** Model yedek zinciri · **F-59** Ön uçuş bütçe denetimi | [Faz 62](fazlar/62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md) |
| **F-61** Argüman düzeyinde onay politikası | [Faz 63](fazlar/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md) |
| **F-75** Denetim hash zinciri · **F-58** Veri konusu hakları | [Faz 64](fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) |
| **F-40** Kiracı sağlayıcı anahtarları (BYOK) | [Faz 65](fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) |
| **F-65** Gelen tetikleyiciler | [Faz 66](fazlar/66-GELEN-TETIKLEYICILER.md) |

Üç faz iki kalemi birleştirir ve gerekçesi her birinde yazılıdır: F-44 ile F-59
aynı sözleşmeyi (`ModelBinding`) ve aynı boru hattını paylaşır; F-75 ile F-58
**aynı tabloda çatışır** ve ayrı planlanırsa ikincisi birincisini bozar.

🚨 **Faz 64 iki tasarım kararını plan anında verdi:** denetim izi
**dokunulmazdır** (silme yalnız içerik verisinde uygulanır) ve veri konusu
kimliği bir **genişleme noktasıyla** (`IDataSubjectResolver`) çözülür —
Tracon kişisel kimlik **saklamaz**.

🚨 **F-63'ün kapsamı daraldı.** Kalem "TypeScript istemci paketi **ve** OpenAPI
yayını" idi; [Faz 40](../arsiv/fazlar/40-OPENAPI-YAYINI.md) yalnız **belgeyi** kapsar.
TypeScript/npm yayını ayrı bir dağıtım kanalıdır ve yeni bir aday kalemidir.

🚨 **F-72 seçildi ama plana dönüşmedi.** Bölümü aşağıda duruyor ve artık
ölçülmüş kanıt taşıyor.

Planlama sırasında **on beş kanıt düzeltildi** (Dalga 1–2'de yedi, Dalga 3'te
sekiz); ayrıntı [`arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](../arsiv/UCUNCU-FAZ-YOL-HARITASI.md)
içindedir.

---

### Dalga 9 → Faz 73

| Kalem | Faz |
|---|---|
| **F-120** Tüketici agent desteği (tanılar + üretilen yetenek haritası) | [Faz 73](fazlar/73-TUKETICI-AGENT-DESTEGI.md) |

Bu kalem bir tüketici sorusundan doğdu: paketi entegre eden uygulamaların **kod
agent'ları** yeteneklere hâkim değil. Dört seçenek tartıldı (RAG · MCP · yalnız
doküman · derleme anı tanıları); RAG **elendi** — chunk sınırı imza ile kullanımı
koparır, ve indeks tüketicinin kurduğu sürümden kayar. Kalan üç katmanın ikisi
(tanılar + üretilen harita) tek faz oldu; üçüncüsü F-121'dir.

---

### Dalga 10 → Faz 74

| Kalem | Faz |
|---|---|
| **F-121** Yerel referans yüzeyi (kapsamı ölçümle değişti) | [Faz 74](fazlar/74-YEREL-REFERANS-YUZEYI.md) |

F-121 bir `dotnet tool` MCP sunucusu olarak yazılmıştı ve kendi kaydı ölçüm
istiyordu. Ölçüm 2026-08-19'da yapıldı ve **öneriyi düşürdü**: detay korpusu
(2.96 MB XML, ~5 600 üye) tüketicinin diskinde zaten duruyor ve `grep` onu
cevaplıyor. Kalan boşluk erişim değil **işaret**tir. Faz 74 üç şeyi alır:
üretilen bir yerel referans dosyası, paketlenen OpenAPI belgesi ve 39 giriş
noktasının tamamında çalışan bir örnek — hepsi yeni dağıtım kanalı açmadan.

---

### Dalga 11 → Faz 75–76

| Kalem | Faz |
|---|---|
| **F-126** Tüketici dokümanının doğruluğu ve kapıları | [Faz 75](fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md) |
| **F-124** Harita üretecinin kesme ve kural kusurları | [Faz 75](fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md) |
| **F-127** Doküman kalitesi ve görsel kimlik | [Faz 76](fazlar/76-DOKUMAN-KALITESI-VE-GORSEL-KIMLIK.md) |

Bu tur bir **denetim turudur**, bir keşif turu değil: kaynağı yeni bir ihtiyaç
değil, Faz 73 ve 74'ün kendi çıktısının ölçülmesidir. Soru şuydu — tüketicinin
kod agent'ına verdiğimiz korpus gerçekten okunabilir mi.

Ölçüm ikisini birden buldu. Sevk edilen dokümantasyon **kendi kendine
yetmiyor**: 15 paketin XML dosyalarında **1 033 satır**, paketlenen
`tracon.json`'da **39 yer** ve 18 paket README'sinin **9'unda** tüketicide
var olmayan adreslere gönderme var (`phase 64`, `K-032`, `docs/NN-*.md`). K-408
bu sınıfın bir katman yüzeyini kapatmıştı ("imza İngilizce, açıklama Türkçe");
bu, aynı kusurun bir katman derinidir — dil doğru, **hedef kitle** yanlış.

İkinci bulgu kapılarla ilgilidir: sızıntıyı arayan kod (`hasInternalHistory`)
**zaten yazılmış** ve doğru çalışıyor, ama yalnız sitenin sanitize edilmiş
kopyalarında koşuyor. Sevk edilen `.nupkg` içeriği hiçbir kapının arkasında
değil. Aynı desen beş yerde daha tekrarlandı: konsol ekranları, telemetri
öznitelikleri, `Options` üyeleri, HTTP sayıları ve harita kuralları — hiçbiri
bugün bir testi kızartmıyor.

F-127 ayrı tutuldu çünkü **farklı bir yargı türü** ister: F-126 testle
kanıtlanır, F-127 gözle. İkisini tek faza koymak DoD'yi bulanıklaştırırdı.

---

### Dalga 12 → Faz 78

| Kalem | Faz |
|---|---|
| **F-135** Yetenek haritasının var olan `AGENTS.md` taşıyan repo'ya erişimi | [Faz 78](fazlar/78-YETENEK-HARITASI-ERISIMI.md) |

F-135 bir keşif turundan değil, **gerçek bir tüketici kurulumundan** doğdu
(2026-08-21, kullanıcı sorusu — F-108 emsali). Paket
`prodigy-enabler-backend`'e eklendi ve harita o repo'ya hiç ulaşmadı: kökte
zaten bir `AGENTS.md` vardı, paket onu doğru şekilde ezmedi, ve haritayı
gösteren başka hiçbir yol yoktu.

Ölçüm teşhisi keskinleştirdi. Oradaki kod agent'ı `Tracon.LocalReference.md`'yi
**kendiliğinden** bulmuş ve `AGENTS.md:105`'te doğru biçimde belgelemiş — hatta
`Tracon.AgentMap.md` adını biliyor. Yani eksik olan bilgi değil, **yoldur**:
`LocalReference.md` XML doc yollarını yazıyor ama haritanın yolunu yazmıyor
([targets:149](../../src/Tracon.Core/buildTransitive/Tracon.Core.targets#L149)).

Aynı ölçüm bir tanı tasarımını da düşürdü: "`AGENTS.md` Tracon'den söz
ediyor mu" kontrolü o dosyada sessiz kalırdı (20'den fazla kez söz ediyor).
`APG0402`'nin tetiği bu yüzden yönlendirme **hedefine** bağlandı, konuya değil.

Kapsam ikinci bir soruyla büyüdü: *harita ulaşılır olunca yeterli mi?* Ölçüm
hayır dedi. Tüketicinin agent'ının kendi kaydettiği tuzağın
(`UseTenancy()` çağrılmazsa sessizce single-tenant) cevabı korpusta **tek bir
yerde** var — [`concepts/governance.md:15`](../../docs-site/src/content/docs/concepts/governance.md#L15),
"Off by default". Harita giriş noktasını adlandırır, XML doc tipi anlatır;
**varsayılanı** yalnız anlatı söyler. O katmanın yerel karşılığı yok (424 KB) ve
girişi de yok: `llms.txt` bir bağ listesi değil (`^- [` deseni **0** eşleşiyor),
`llms-full.txt` **401 KB**. Üstelik `llms-full.txt` satırı sevk edilen haritadan
çıkarılmış ([`build-agent-map.mjs:62`](../../docs-site/scripts/build-agent-map.mjs#L62))
— dağıtım ters. İkisi de Faz 78'e katlandı (§78.4).

Ters yönde bir ölçüm de kayda geçti: sevk edilen XML doc'lar **51**
`Tracon:` yapılandırma anahtarı taşıyor, sitenin `configuration.md`'si
**39**. Boşluk referansta değil, anlatıdadır.

Faz 73 bu bedeli önceden yazmıştı — "opt-in kararının bedeli benimseme
oranıdır". F-135 o cümlenin ilk ölçülen faturasıdır.

---

---

## Kapanmış kapsam-dışı kalemler (ADAYLAR.md'den taşındı, 2026-08-21)

| ID | Kalem | Kaynak | Gerekçe |
|---|---|---|---|
| ~~**F-87**~~ | 🚫 **KAPATILDI (2026-08-21, 👤 kullanıcı kararı).** Öncülü ölçüldü ve **yanlış çıktı**: kayıt artık guard'dan geçiyor — [`RunRecordingAgent.cs:661`](../../src/Tracon.Core/Recording/RunRecordingAgent.cs#L661) `RunStarted` olayını ve `IRunInputStore` yazımını `ContentGuardMessageMasker.PreviewAsync` ile maskeliyor. Kayıt, `TraconServiceCollectionExtensions.cs:869`'daki yorumu ters okumuştu ("bu satır **olmasaydı**" diyor ve satır **var**). Geriye dönük temizlik ihtiyacını da [Faz 64](fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) konu bazlı **silme** ile karşılıyor (`DELETE /api/data-subjects/{id}`). Geriye kalan tek dar iş — satırı tutup parçayı temizleyen **redaksiyon** — ölçülmüş bir tüketici talebine bağlı değildir; bir talep gelirse yeniden açılır |
| ~~**F-100**~~ | ✅ **KAPANDI (2026-08-18)** — bütçe eşiği uyarısı | 2026-08-08 denetimi | 🚨 **İddia ölçüldü ve yanlış çıktı.** Mekanizma koddadır: `TraconQuotaOptions.ThresholdPercents` (varsayılan `[80, 100]`), `QuotaEnforcer.PublishThresholdEventsAsync` ve `WebhookEvents.QuotaThreshold = "quota.threshold"`. Eşik aşımı **zaten** giden webhook tetikliyor |
| ~~**F-121**~~ | ✅ **KAPANDI (2026-08-20)** — kapsamı ölçümle değişti → [Faz 74](fazlar/74-YEREL-REFERANS-YUZEYI.md) tamamlandı | 2026-08-18 tüketici agent turu · [Faz 73](fazlar/73-TUKETICI-AGENT-DESTEGI.md) | Gerekçe: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](../arsiv/PLANA-DONUSEN-ADAYLAR.md) — F-121. |
| ~~**F-124**~~ | 📋 **PLANA DÖNÜŞTÜ (2026-08-20)** → [Faz 75](fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md) §75.4 | [Faz 73](fazlar/73-TUKETICI-AGENT-DESTEGI.md) denetimi (2026-08-19) | 🚨 **Kaydın teşhisi ölçümle düzeltildi (2026-08-20).** Kesme iddiası doğruydu — sevk edilen haritada üç kelime ortası kesme var. Kural iddiası **yanlıştı**: "Storage and testability" bölümünün tablodan ÖNCE düz metni yok, yani üreteç tablodan sonrakini zaten alıyor; gerçek kusur alınan cümlenin bir kural değil bir yön tarifi olmasıdır. Ölçüm **üçüncü bir kusur** buldu: 11 bölümün **ikisi hiç kural üretmiyor** ("Runs, sessions, and media" ve "Observability and operations" — `capabilities.md`'de düz metinleri yok). Üçü de Faz 75'e girdi |
| ~~**F-125**~~ | 📋 **PLANA DÖNÜŞTÜ (2026-08-21)** → [Faz 79](fazlar/79-SEVK-EDILEN-YUZEY-KAPILARI.md) | [Faz 74](fazlar/74-YEREL-REFERANS-YUZEYI.md) denetimi (2026-08-20) | Gövde faza taşındı. Planlama sırasında kanıt yeniden ölçüldü ve kayda göre üç düzeltme yapıldı; ayrıntı faz dokümanının "Bugün ne çalışmıyor" bölümündedir. |
| ~~**F-129**~~ | 📋 **PLANA DÖNÜŞTÜ (2026-08-21)** → [Faz 80](fazlar/80-DOKUMAN-KAPILARININ-DOGRULUGU.md) | [`tuketici-dokuman-senkronu`](../../.agents/skills/tuketici-dokuman-senkronu/SKILL.md) kurulumu (2026-08-20) · K-522 | Gövde faza taşındı. Planlama sırasında kanıt yeniden ölçüldü: desen sayısı 9 değil **10**; `buildTransitive/` boşluğu "eşleme yok" değil **"eşleme hatalı"**; ve bir hipotez çürüdü (üretilen haritanın bayatlaması `ci.yml`'de zaten kapalı). Ölçüm iki yeni boşluk buldu: kapının **hiç testi yok** ve **CI'da hiç koşmuyor**. |
| ~~**F-131**~~ | ✅ **TAMAMLANDI (2026-08-20)** → [Faz 77](fazlar/77-GIDEN-AG-MUHAFIZI.md) | Güvenlik taraması (2026-08-20) | Gövde faza taşındı. Planlama sırasında adayın **ölçülmemiş** tek kalemi ölçüldü: dört sağlayıcı SDK'sı da `HttpClient` enjeksiyonuna izin veriyor (`Anthropic.Core.ClientOptions.HttpClient`, OpenAI/Azure `ClientOptions`, Google kendi istemcisine sahip) — yani K-164'ün `IHttpClientFactory` yasağı korunarak muhafız üç yüzeye taşınabilir, yeni paket gerekmez |
| ~~**F-133**~~ | ✅ **KAPANDI (2026-08-21)** — kök sebep ölçüldü, "kırılgan test" teşhisi YANLIŞ çıktı, düzeltildi (K-541) | Faz 77 kapanış koşumu (2026-08-20) | Gövde: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](../arsiv/PLANA-DONUSEN-ADAYLAR.md) — F-133. |
| ~~**F-102**~~ | ✅ **KAPANDI (2026-08-18)** — kırılgan eşzamanlılık testi | 2026-08-08 denetimi | Gerekçe: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](../arsiv/PLANA-DONUSEN-ADAYLAR.md) — F-102. |
| ~~**F-136**~~ | 📋 **PLANA DÖNÜŞTÜ (2026-08-21)** → [Faz 79](fazlar/79-SEVK-EDILEN-YUZEY-KAPILARI.md) | [Faz 78](fazlar/78-YETENEK-HARITASI-ERISIMI.md) denetimi (2026-08-21) 🟢 | Gövde faza taşındı. Planlama sırasında kanıt yeniden ölçüldü ve kayda göre üç düzeltme yapıldı; ayrıntı faz dokümanının "Bugün ne çalışmıyor" bölümündedir. |

---

## Yeniden Yargı (2026-08-06) (ADAYLAR.md'den taşındı, 2026-08-21)

## Yeniden Yargı (2026-08-06)

### İptal — tamamlandı

| Kalem | Kanıt |
|---|---|
| **F-43** Sağlayıcıya özgü ayar torbası | `ModelBinding.ProviderSettings` sözleşmede yaşıyor: [`ModelBinding.cs:70`](../../src/Tracon.Abstractions/Agents/ModelBinding.cs). Karar K-208. Bilinmeyen anahtar derleme hatasıdır — istenen davranış birebir uygulanmış. Eski liste bunu "Faz 26 isteyecek" diye yazmıştı; Faz 26 bitti ve isteği karşıladı |

### Kanıtı yanlışlanan — kalem ayakta, gerekçe değişti

| Kalem | Eski iddia | 2026-08-06 ölçümü |
|---|---|---|
| **F-35** Çalıştırma iptali | "`RunStatus.Canceled` tanımlı ama hiçbir kod yazmıyor" | **Yanlış.** Üç yer yazıyor: [`RunRecordingAgent.cs:186`](../../src/Tracon.Core/Recording/RunRecordingAgent.cs), aynı dosya `:251` ve [`WorkflowRunner.cs:463`](../../src/Tracon.Workflows/Internal/WorkflowRunner.cs). Gerçek delik başkadır ve aşağıda yazılıdır |
| **F-57** Çok örnekli koordinasyon | "Faz 17 bu olmadan yapılırsa her cron N kez tetiklenir" | **Yanlış.** [`0008_scheduling.sql:46`](../../src/Tracon.PostgreSql/Migrations/0008_scheduling.sql) `jobs_schedule_scheduled_uq UNIQUE (schedule_id, scheduled_for)` kısıtını taşıyor (K-138). Cron çift tetiklemesi zaten kapalı. Kalemin aciliyeti düştü, kapsamı daraldı |

### Yükseltilenler

| Kalem | Neden yükseldi |
|---|---|
| **F-30** Vektör bellek | Yeni kanıt: kalıcı depo geldi ama arama hâlâ regex **ve** O(n). `SqlAgentFileStore.SearchAsync` her çağrıda tüm dosyaları belleğe alıyor |
| **F-32** Guardrails | Microsoft 2026-06-02'de **Agent Control Specification**'ı yayımladı. İş artık "kendi filtremizi yaz" değil, "bir standarda otur" |
| **F-33** A2A | `Microsoft.Agents.AI.A2A` ve `Microsoft.Agents.AI.Hosting.A2A` paketleri **var**. "Elle uygulamak pahalı" gerekçesi düştü |
| **F-44** Model yedek zinciri | LiteLLM ve Portkey'de temel yetenek. .NET'te karşılığı yok |
| **F-52** Geri bildirim | Önkoşulsuz, tek tablo, ölçme döngüsünün ilk halkası |
| **F-53** Üretimden eval kümesi | Faz 18 bitti. Bu artık "önce yapılmalı" değil, **eksik yarısı** |
| **F-54** Yeniden oynatma | LangGraph 1.2'nin "time travel"i fiilî standart oldu |

### Birleşmeler

| Birleşen | Nasıl |
|---|---|
| **F-31 + F-33** | Tek faz: "dışa açılan agent yüzeyi". İkisi de aynı altyapıyı ister — kimlik doğrulama, kiracı çözümleme, derinlik ve bütçe sınırı, onay sınırı. Protokoller iki ince adaptördür. İki ID korunur |
| **F-54 + F-66** | Tek faz. İkisi de "kayıtlı bir noktadan dallanma"dır; `conversation_items` append-only olduğu için ikisi de aynı dal işaretçisini ister |
| **F-39 → F-68** | F-39 (`202 Accepted`) dayanıklı çalıştırmanın **HTTP yüzüdür**. Ayrı kalem tutmak sözleşmeyi motordan koparır. F-39 ID'si F-68'in içinde yaşar |

---

## Dalga 13 önerisi — dört küme (ADAYLAR.md'den taşındı, 2026-08-21)

### Dalga 13 önerisi — dört küme (2026-08-21) 👤

Bu tur **yeni kalem üretmedi**; ekosistem boşluk tablosunun 15 satırından 14'ü
kapandığı için "X'te standart, .NET'te yok" damarı tükendi. Kalan sekiz kalem
ölçülerek **dört kümeye** toplandı. Küme mantığı Faz 64 emsalidir: aynı veriye
dokunan iki kalem ayrı planlanırsa ikincisi birincisini bozar.

Kullanıcı sırası: **A → B → C → E**. 🚨 **Küme A ikiye bölündü (2026-08-21):** `faz-planlama` "üç kalem bir faz değil, bir turdur" der ve bir faz ancak AYNI altyapıyı paylaşan iki kalemi birleştirir. F-125 ve F-136 ikisi de `Tracon.Generators.UnitTests` içinde bir C# kapısıdır (ölçüldü: `AnalyzerTestHelper` ve `DiagnosticIntegrityTests` orada) → **Faz 79**. F-129 ise `scripts/dokuman-bakim.py` içinde bir Python kapısıdır ve ayrı kalır. Faz 7 **ertelenmeye devam** eder (K-068).
Tam koşum kaydı: [`kesif/2026-08-21-faz-adaylari-tespiti.md`](kesif/2026-08-21-faz-adaylari-tespiti.md).

| Sıra | Küme | Kalemler | Ortak yanı | Bu turda ölçülen |
|---|---|---|---|---|
| 1 | **A** Tüketici yüzeyi kapıları — ✅ **tamamı plana döndü**: F-125 + F-136 → [Faz 79](fazlar/79-SEVK-EDILEN-YUZEY-KAPILARI.md) · F-129 → [Faz 80](fazlar/80-DOKUMAN-KAPILARININ-DOGRULUGU.md) | F-125 · F-129 · F-136 | Yeni yetenek/tanı/örnek sevk edilebilir, **hiçbir kapı kızarmaz** | F-136 **beş belgesiz kod** · F-125 prelüd **iki** yer tutucu · F-129 iddiası **yanlış çıktı**, kalem daraldı |
| 2 | **B** Model boru hattı ergonomisi — ✅ **plana döndü**: F-45 + F-134 → [Faz 81](fazlar/81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md) | F-45 · F-134 | İkisi de `ModelProviderRegistry` `.AsBuilder()` zincirine takılır, ikisi de varsayılan kapalı | F-45 `IDistributedCache` **yeni bağımlılık kararı** · F-134 **ambient `CurrentContext` riski** |
| 3 | **C** Kayıt içeriğinin korunması — ✅ **karara bağlandı**: F-41 → [Faz 82](fazlar/82-ICERIK-KORUMASI.md) · F-87 🚫 **kapatıldı** (öncülü çürüdü) | F-41 · F-87 | **Aynı dokuz sütun**; ayrı planlanırsa çatışır | Üç okuma yolu adlandırıldı: replay · eval terfisi · dosya araması |
| 4 | **E** Dağıtım kanalı — 📋 **ikiye bölündü (2026-08-21)**: F-50 → [Faz 83](fazlar/83-TIPLI-ISTEMCI-VE-CLI.md) · F-93 **açık kalır**, Faz 84 olarak planlanacak | F-50 · F-93 | Aynı OpenAPI belgesinden üretilir, aynı sürümleme sözleşmesi | Belge **yeterli** (0 eksik `operationId`) · "ikinci üreteç" kısıtı **konusuz** · npm gerçekten yeni kanal · 🚨 npm **ayrı kimlik bilgisi ve ayrı yayın hattı** ister; `ci.yml` yalnız NuGet.org'a iter |

**Küme D düşürüldü** (F-88 · F-89 · F-98). F-88 tek başına faz değil; F-89
yayından sonra pahalı bir **arayüz** değişimidir; F-98'in erteleme gerekçesi
(doğrulanamazlık, K-212 emsali) değişmedi. Üç kalem bu listede **kalır**.

🚨 **Kümeler faz numarası taşımaz.** Numarayı `faz-planlama` verir; her küme
kendi oturumudur.

---

## Kusur kalemlerinin ölçüm matrisleri (ADAYLAR.md'den taşındı, 2026-08-21)

> Kalemler **açıktır**; yalnız ölçüm ayrıntıları buraya taşındı. Özet ve
> `dosya:satır` [`ADAYLAR.md`](../ADAYLAR.md) § *Numaralandırılan kapsam-dışı işler* içindedir.

| ID | Kalem | Kaynak | Ölçüm |
|---|---|---|---|
| **F-122** | `Runs_button_on_session_page_navigates_to_filtered_list` (`Tracon.Ui.E2ETests`) kırılgan | Faz 65 kapanış koşumu (2026-08-19) | 🚨 **Ölçüldü:** izolasyonda 3/3 geçti; tam `Tracon.Ui.E2ETests` seti (55 test) koşarken 3 denemeden 2'sinde `tbody tr` satır sayısı, düğme etiketindeki beklenen sayıyla eşleşmeden okundu (`UiTests.cs:720`) — koşu tarayıcı/`Docker` kaynak çekişmesi altında bir zamanlama yarışı. Faz 65'in dokunduğu hiçbir dosyayla (BYOK/egress) ilgisi yok. F-102 emsali: bir kusur değil, kırılgan bir test — ama sessiz bırakılmadı. Ya `runsButton`'ın metnini bekledikten SONRA tablo satır sayısının da stabilize olmasını bekleyen bir `WaitForAsync` eklenir, ya da `expectedCount` okuması tablo render'ından SONRAya taşınır |
| **F-130** | `Eval_suite_is_created_case_added_and_run_passes` (`Tracon.Ui.E2ETests`) kırılgan | Faz 77 doküman bütçesi koşumu (2026-08-20) | 🚨 **Ölçüldü, F-122'nin aynısı.** Tam `Ui.E2ETests` setinde (56 test) `TimeoutException: Timeout 30000ms exceeded` — `GetByPlaceholder("What is your return policy?")` üzerinde `fill` sırasında *"element was detached from the DOM, retrying"* (`UiTests.cs:1113`). Üç adımlı ayırt etme protokolü (`docs/hafiza/test-altyapisi.md`) tam uygulandı: izolasyonda **1/1** geçti, **ikinci tam koşumda 56/56** geçti. Kök sebep tarayıcı/`Docker` kaynak çekişmesi altında React yeniden render'ının `textarea`'yı `fill` ortasında DOM'dan koparması — ürün kusuru değil. Faz 77 yalnız dokümana, bir Python script'ine ve `.cs` **yorumlarına** dokundu; arayüzle ilgisi yok. Çözüm F-122 ile aynı sınıftadır: `fill` öncesi elemanın stabilize olmasını bekleyen bir `WaitForAsync`, ya da locator'ı `fill` anında yeniden çözmek |
| **F-137** | `Shell_opens_and_asks_for_token_when_required` (`Tracon.Ui.E2ETests`) **çalışma kopyasına göre** düşüyor | [Faz 78](fazlar/78-YETENEK-HARITASI-ERISIMI.md) kapanış koşumu (2026-08-21) | 🚨 **Ölçüldü ve Faz 78'in DEĞİŞİKLİĞİ DEĞİL** — `git stash` ile temiz `HEAD`'de, aynı çalışma kopyasında **yine düşüyor**. Belirti: token girildikten sonra `GetByRole(Heading, "Dashboard")` 15 sn içinde görünmüyor (`UiTests.cs:599`). Ölçüm matrisi: **(a)** `Tracon.Ui.E2ETests` tek başına, `/Users/.../Desktop/projects/Tracon` → **5/5 düştü**; **(b)** tek test izole, aynı kopya → **1/1 geçti**; **(c)** `/private/tmp` altındaki `git worktree`, aynı `HEAD`, tam set → **2/2 geçti**; **(d)** `dotnet test Tracon.slnx` içinde → 1 düştü, 1 geçti. Yani **deterministik değil ama tek başına koşan sette yola bağlı olarak neredeyse her zaman düşüyor**. F-122/F-130'dan farklı sınıf: onlar kaynak çekişmesi altında araya giren kırılganlıktı, bu **yol/ortam** bağımlı. Kök sebep aranmalı: aynı kaynak ağacının iki kopyasının farklı davranması kalıcı tarayıcı profiline, `localStorage`'a veya yol izinlerine/uzunluğuna işaret eder. **Kusurdur, yeni yetenek değil** — `kusur-giderme` protokolü uygulanmalıdır |
| **F-138** | `Respond_does_not_rerun_the_entry_node_when_it_is_an_agent` (`Tracon.Workflows.UnitTests`) kırılgan | Kanal 2 kusur koşumu (2026-08-21) | 🚨 **Ölçüldü:** dört tam koşumun yalnız birinde düştü; **izolasyonda 5/5 geçti**. Belirti: `resumed.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput)` eşleşme bulamıyor (`WorkflowAgentEntryRespondTests.cs:55`) — yani `RespondStreamingAsync` devam eden akışta çıktı olayını üretmemiş. F-122/F-130 sınıfı (kaynak çekişmesi) OLABİLİR ama kanıtlanmadı: o ikisi tarayıcı/`Docker` çekişmesiydi, bu saf bir birim testidir ve dış kaynağa dokunmaz. **Bu yüzden gerçek bir çıktı-olayı kaybı ihtimali elenemedi**; ilk adım yük altında tekrar üretmektir. 🚨 **İKİNCİ VAKA ölçüldü (2026-08-21, K-545 koşumu):** `WorkflowHumanInTheLoopTests.Once_a_response_is_given_the_run_completes` (`WorkflowHumanInTheLoopTests.cs:88`) **birebir aynı** iddiada düştü — `resumed.Single(e => e.Type == RunEventType.WorkflowOutput)` eşleşme bulamadı. Aynı ölçüm profili: tam çözüm koşumunda düştü, izolasyonda **5/5**, kendi projesinin tam koşumunda **3/3** geçti. İki farklı test, tek belirti → "kaynak çekişmesi" açıklaması **zayıfladı**; `RespondStreamingAsync`'in devam eden akışında `WorkflowOutput` olayının kaybolması artık iki bağımsız kanıt taşıyor ve kalem bir **ürün kusuru** gibi ele alınmalıdır |
| **F-139** | `Version_diff_compares_two_versions` (`Tracon.Ui.E2ETests`) kırılgan | K-545 koşumu (2026-08-21) | **Ölçüldü, F-122/F-130'un aynısı.** Tam çözüm koşumunda `33 sn` sonra düştü; **izolasyonda 1/1 geçti**. Aynı sınıf: tarayıcı + `Docker` kaynak çekişmesi altında bir zamanlama yarışı, ürün kusuru değil. Kalem sessiz bırakılmadı ama tek başına faz değildir — üç E2E kırılganı (F-122, F-130, F-139) ve F-137 **birlikte** ele alınmalıdır: ortak kök tam koşumun paralelliğidir, tek tek beklemeler değil |

---

## Kapatılan kusur kalemlerinin gövdeleri (ADAYLAR.md'den taşındı, 2026-08-26)

> İkisi de **kapalıdır**; `ADAYLAR.md`'de yalnız tek satırlık işaretçi kaldı
> (F-104/F-105 deseni). Taşımanın sebebi bütçedir: F-165 eklenince
> `ADAYLAR.md` 80.000 B sınırını aştı. **İçerik silinmedi.**

### F-150 · `JobWorkerBackgroundService` kapanışta dispose edilmiş `SemaphoreSlim`'i serbest bırakıyor — süreç çöküyor — ✅ KAPATILDI (2026-08-25)

**Nereden geldi:** Faz 99 kapanış kapısının izole koşumu (2026-08-25).
**Temel commit'te doğrulandı** (`c4e3189`, `git worktree` ile) — Faz 99'un ürünü
DEĞİL, mevcut bir kusurdur.

**Kapanış:** Worker uçuştaki her işi başlatmadan önce kaydeder; `ExecuteAsync`,
slot `SemaphoreSlim`'ini dispose etmeden önce bu görevlerin tamamlanmasını
bekler. `JobWorkerBackgroundServiceTests` `StopAsync`'in iş slotu bırakılmadan
dönmediğini doğrudan kanıtlar. Ateşle-unut çağrılarının sınıf taramasında kalan
iki yol (`McpOAuthAuthorizationCoordinator`, lease renewal) sahiplenilen slot
taşımıyor veya kendi iptal/gözlem yoluna sahip; aynı kusur sınıfı bulunmadı.

> `JobWorkerBackgroundService.ExecuteAsync` semaforu `using var slots = new
> SemaphoreSlim(...)` ile sahiplenir (`JobWorkerBackgroundService.cs:62`), ama
> işleri **ateşle-unut** başlatır: `_ = RunJobAsync(job, slots, stoppingToken)`
> (satır 129). `RunJobAsync`'in `finally` bloğu `slots.Release()` çağırır
> (satır 148). Host, uçuştaki bir iş varken kapanırsa `ExecuteAsync` döner,
> `using` semaforu dispose eder ve gecikmiş `Release()`
> `ObjectDisposedException` atar.

**Belirti:** Görev `await` edilmediği için exception gözlemlenmez ve
**işlenmemiş** olur — .NET süreci sonlandırır. Ölçülen: tek bir testi izole
koşmak (`Tracon.AspNetCore.FunctionalTests --filter-method
"*Timed_out_call_completes*"`) test host'unu **exit 134 (SIGABRT)** ile
çökertir; aynı çökme `c4e3189` üzerinde birebir tekrarlanır.

**Neden bugüne kadar görünmedi:** Tam çözüm koşumunda süit uzun sürer ve host
kapanışı uçuştaki işle çakışmaz; çökme yalnız hızlı kapanışta (tek test
filtresi) ortaya çıkar. Üretimde karşılığı **hızlı yeniden başlatma** veya
`SIGTERM` sonrası kısa drain penceresidir.

**Kapsam:** Yalnız bu vakayı kapatmak yetmez, **sınıf taraması** ister: bu bir
"ateşle-unut görev, sahiplenilen kaynağı kapsam dışında serbest bırakıyor"
kusur sınıfıdır. `grep -rn "_ = [A-Za-z]*Async(" src/` ile taranmalı; her
bulunan yerde (a) görevin izlenip kapanışta beklenip beklenmediği, (b)
yakaladığı kaynağın ömrü sorgulanmalı. Olası düzeltme: uçuştaki görevleri bir
listede tut ve `ExecuteAsync` dönmeden önce `Task.WhenAll` ile bekle; ya da
semaforu `using` yerine servis ömrüne bağla.

**Değer:** Yüksek — işlenmemiş exception süreci öldürür ve bu, gözlemlenebilirlik
değil **kullanılabilirlik** sorunudur. Ayrıca `Tracon.Core`
`TraconDrainService` ile zarif kapanış vaat eder; bu kusur o vaadi deler.

**Mercek:** A (çekirdek çalıştırma yolu).

### F-151 · Uygulanmış iki PostgreSQL migration dosyası sonradan düzenlendi — mevcut kurulumlar yükseltmede BAŞLAMAZ — ✅ KAPATILDI (2026-08-25)

**Nereden geldi:** Faz 99 kapanışının örnek uygulama koşumu (2026-08-25).
Yerel geliştirme veritabanı `TraconException` ile açılışı durdurdu.

**Kapanış:** `0032_tenant_provider_bindings.sql` ve `0037_run_continuation.sql`
ilk uygulanmış baytlarına döndü. `scripts/kapi.py tarama`,
`scripts/applied-migrations.json` içindeki Git kaynak commit'lerinden baytları
okuyarak bütünlüğü doğrular; manifestte checksum değiştirerek migration değişikliği
onaylanamaz. Örnek API gerçek PostgreSQL veritabanıyla yeniden başladı ve
`/health` 200 döndü. `dokuman-bakim.py` arşivleme kodu yalnız Markdown dosyalarını
değiştirir; önceki SQL toplu-onarım kök neden iddiası ölçümle çürütüldü.

> `MigrationRunner` uygulanmış her migration'ın checksum'ını saklar ve dosya
> içeriği değişmişse **açılışı durdurur** — doğru davranıştır, mesajı da
> doğrudur: "An applied migration is never edited; add a new migration file
> for the change."
>
> Ama commit `9c32242` ("döküman düzeni sağlandı", 2026-08-23) tam olarak bunu
> yaptı: `0032_tenant_provider_bindings.sql` ve `0037_run_continuation.sql`
> dosyalarındaki **yorum satırlarında** doküman yolunu güncelledi
> (`docs/65-...md` → `docs/arsiv/fazlar/65-...md`). SQL'in kendisi değişmedi;
> checksum değişti.

**Etki:** Bu iki migration'ı `9c32242` ÖNCESİNDE uygulamış **her** kurulum,
yeni sürüme yükseltince açılışta çöker. Yerel geliştirme veritabanında
ölçüldü — `samples/Tracon.Api` başlamıyor:
`Checksum in the database: 9116FE1E...`, `checksum of the file: 16D3AB60...`.
Bu bir geliştirme rahatsızlığı değil, **sevk edilmiş bir kırılmadır**.

**Başlangıçtaki kök neden varsayımı yanlıştı:** Ölçüm, Faz 90 arşivleme yolunun
yalnız `*.md` dosyalarına dokunduğunu gösterdi. `9c32242` değişikliği başka bir
toplu düzenleme ile geldi. Yeni kapı aracı değil baytı korur; bu nedenle aynı
etki hangi düzenleme yolundan gelirse gelsin kırmızıya döner.

**Uygulanan kapsam:**
1. İki dosyanın **baytları geri alındı** (`git show f261cda:<yol>` ve
   `git show 8cf727a:<yol>`). Uygulanmış migration değişmez; içindeki bayat
   doküman bağlantısı checksum'dan daha ucuz bir sorundur.
2. Arşivleme kodunda SQL hariç tutma yapılmadı; ölçülen kök neden o kod değildir.
3. **Kapı eklendi:** uygulanmış migration, değiştirilemeyen Git kaynak baytıyla
   eşleşmezse `kapi.py tarama` kırmızı döner. Bu yol mevcut veritabanına gerek
   duymaz.

**Değer:** Yüksek ve acil — `preview.1` öncesi kapanmalıdır. Aksi hâlde ilk
yükseltme yapan tüketici açılışta çöker ve mesaj onu "yeni migration ekle"
diye yanlış yöne gönderir; sorun onun eklediği bir şey değildir.

**Mercek:** C (güvenlik, yönetişim ve uyum — veri düzlemi bütünlüğü).
