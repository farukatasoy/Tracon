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
[`../kesif/2026-08-18-tuketici-raporu.md`](../kesif/2026-08-18-tuketici-raporu.md).

| Kalem | Faz |
|---|---|
| F-110 | [Faz 67](../67-ISTEGE-BAGLI-MIGRATION-SETI.md) |
| F-111 | [Faz 68](../68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) |
| F-112 | [Faz 68](../68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) |
| F-113 | [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) |
| F-114 | [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) |
| F-115 | [Faz 70](../70-CALISTIRMA-OLAYI-HEDEFI.md) |
| F-116 | [Faz 71](../71-WORKFLOW-KOD-DUGUMU.md) |
| F-117 | [Faz 72](../72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) |
| F-118 | [Faz 72](../72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) |
| F-119 | [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md) |

---

### F-110 · `pgvector`'ün isteğe bağlı olması

**Sorun:** `UsePostgreSql()`, Knowledge hiç kullanılmasa bile
`CREATE EXTENSION vector` çalıştırır
([`0024_vector.sql:17`](../../src/AgentPrism.PostgreSql/Migrations/0024_vector.sql));
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

> **Plana dönüştü:** [Faz 67](../67-ISTEGE-BAGLI-MIGRATION-SETI.md) · taşındı 2026-08-18

---

### F-111 · Çalıştırma kimliği ve maliyet kırılım boyutları

**Sorun:** `RunRecord` kiracı, oturum ve agent taşır; **kullanıcı taşımaz** —
`grep -rn "UserId" src` repo genelinde **0 sonuç** verir. Amaç/etiket alanı da
yoktur. `RunStatisticsQuery` yalnız agent ve kiracıya göre filtreler, kırılım
agent · sürüm · modeldir
([`RunStatistics.cs`](../../src/AgentPrism.Abstractions/Runs/RunStatistics.cs)).
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
**Risk:** 🚨 **Kullanıcı kimliği kişisel veridir.** AgentPrism kişisel kimlik
**saklamamalıdır**; alan opak bir dizedir ve anlamını tüketici verir. Aynı duruş
[Faz 64](../64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md)'ün `IDataSubjectResolver`
kararıyla tutarlıdır. Etiketler kardinaliteyi patlatabilir: metrik etiketi
**değil**, yalnız sorgu boyutu olmalıdır.
**Bağımlılık:** F-112 aynı tabloya dokunur.
[Faz 64](../64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) veri konusu haklarını
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

> **Plana dönüştü:** [Faz 68](../68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) · taşındı 2026-08-18

---

### F-112 · Cache ve reasoning token kırılımı

**Sorun:** `RunUsage` üç alan taşır
([`RunSupportTypes.cs:6`](../../src/AgentPrism.Abstractions/Runs/RunSupportTypes.cs)) ve
[`RunRecordingAgent.cs:1056`](../../src/AgentPrism.Core/Recording/RunRecordingAgent.cs)
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

> **Plana dönüştü:** [Faz 68](../68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) · taşındı 2026-08-18

---

### F-113 · Tool düzeyinde yetkilendirme ve etki sınıfı

**Sorun:** `ToolDescriptor` yalnız `RequiresApproval` taşır
([`ToolDescriptor.cs`](../../src/AgentPrism.Abstractions/Tools/ToolDescriptor.cs));
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
**Risk:** 🚨 AgentPrism **kullanıcı ve rol saklamaz** ve saklamamalıdır. Kanca
kimliği çözmez, tüketicinin kimlik hattına **sorar**. Varsayılanı izin vermek
olmalıdır, yoksa `AddAgentPrism()` tek başına çalışmaz (K1).
**Bağımlılık:** F-114 aynı `record`.
[Faz 63](../63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md) `ToolApprovalRule`'a
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

> **Plana dönüştü:** [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) · taşındı 2026-08-18

---

### F-114 · Tool yürütme timeout'u

**Sorun:** Timeout yalnız iki yerdedir — `AgentPrismOptions.cs:71` (`McpTimeout`)
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

> **Plana dönüştü:** [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) · taşındı 2026-08-18

---

### F-115 · Çalıştırma olayı hedefi ve `ReasoningDelta`

**Sorun:** İki parça. **(a)** `RunEvent` yalnız `IRunStore`'a yazılır
([`RunEventWriter.cs:22`](../../src/AgentPrism.Core/Recording/RunEventWriter.cs));
canlı olayı gözlemleyecek genişleme noktası yoktur. Kendi gerçek zamanlı
arayüzüne gömen tüketici ya HTTP SSE ile kendine bağlanır ya `IRunStore`'u
dekore eder — kalıcılık ile yayını karıştıran yanlış bir yer. **(b)**
`RunEventType` 0–21 arasıdır; `ReasoningDelta` yoktur, düşünme akışı
`MessageDelta`'ya karışır.
**Kapsam:** Kayıt edilebilir bir olay hedefi (`TryAdd`, varsayılan boş) ve
enum'a `ReasoningDelta` eklenmesi.
**Değer:** [Faz 61](../61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) tarayıcı
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

> **Plana dönüştü:** [Faz 70](../70-CALISTIRMA-OLAYI-HEDEFI.md) · taşındı 2026-08-18

---

### F-116 · Workflow kod düğümü

**Sorun:** `WorkflowNodeKind` dört değer taşır — `Agent`, `Orchestration`,
`RequestPort`, `Output`
([`WorkflowGraph.cs:94`](../../src/AgentPrism.Abstractions/Workflows/WorkflowGraph.cs)).
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

> **Plana dönüştü:** [Faz 71](../71-WORKFLOW-KOD-DUGUMU.md) · taşındı 2026-08-18

---

### F-117 · Talimatta çok dillilik

**Sorun:** `AgentDefinition.Instructions` tek bir `string?`'tir
([`AgentDefinition.cs:33`](../../src/AgentPrism.Abstractions/Agents/AgentDefinition.cs)).
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

> **Plana dönüştü:** [Faz 72](../72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) · taşındı 2026-08-18

---

### F-118 · Zaman damgalı konuşma sentezi

**Sorun:** `ElevenLabsSpeechClient` yalnız `text-to-speech` ve `/stream`
uçlarını çağırır
([`ElevenLabsSpeechClient.cs:318`](../../src/AgentPrism.Voice/Internal/ElevenLabsSpeechClient.cs)).
Kelime düzeyinde zaman damgası döndüren uç desteklenmiyor. Sentezlenen sesi
metinle hizalamak — altyazı, vurgulama, transcript senkronu — mümkün değil.
**Kapsam:** Konuşma sentezi tool'una isteğe bağlı zaman damgası çıktısı;
sözleşmeye hizalama verisi.
**Değer:** Sesli içerik üreten tüketici bugün ElevenLabs'ı ayrıca kendi çağırmak
zorunda — paketi kullanmasının bir kısmı boşa gidiyor.
**Mercek:** 1.
**Hazırlık:** Sağlayıcı ucu hazır; iş eşlemedir.
**Maliyet:** Düşük.
**Risk:** `AgentPrism.Voice` sözleşmesini büyütür. Zaman damgası akışlı sentezde
farklı gelir — iki yol ayrı ele alınmalıdır.
**Bağımlılık:** Yok.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Yalnız bir mercekten iyi görünüyor (1) ve tek bir satıcının
ucuna bağlıdır. Zayıf kalemdir; ses tarafında başka bir işle birleşmedikçe tek
başına faz olmamalıdır.

> **Plana dönüştü:** [Faz 72](../72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) · taşındı 2026-08-18

---

### F-119 · Kiracı bazlı sağlayıcı allowlist'i

**Sorun:** Bir kiracının verisinin hangi sağlayıcıya gidebileceğini kısıtlayan
mekanizma yok. Ölçüldü: `grep -rn "AllowList\|Allowlist\|AllowedProviders" src`
**üç** sonuç verir ve üçü de aynı şeydir —
[`AgentPrismOptions.cs:207`](../../src/AgentPrism.Core/AgentPrismOptions.cs)
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
**Risk:** [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md) sağlayıcı çözümlemesini
zaten değiştirecek. Bunu ondan **önce** yapmak aynı yere iki kez dokunmaktır.
**Bağımlılık:** [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md) — birlikte
planlanmalı.
**Ekosistem:** Taranmadı.
**Karşı görüş:** Yalnız bir mercekten iyi görünüyor (3). Tek başına zayıftır —
Faz 65'in kapsamına katılmadıkça planlanmamalıdır.

> **Plana dönüştü:** [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md) · taşındı 2026-08-18

## Faz 77 bütçe rahatlatması — kapanmış kalemler ve tur anlatısı

#### F-104 · Örnek uygulama rol politikaları — ✅ KAPATILDI (2026-08-18)

> **Aday değildir.** Faza dönüşmeden bir kusur olarak düzeltildi. `samples/AgentPrism.Api`
> artık `AgentPrism:Demo:Roles:Enabled` bayrağıyla üç politikayı kaydeder ve o anda
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

- **F-113** Tool düzeyinde yetkilendirme ve etki sınıfı → [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) 📋 · gövdesi: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)
- **F-119** Kiracı bazlı sağlayıcı allowlist'i → [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md) ✅ · gövdesi: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)

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
> [`kesif/2026-08-18-tuketici-raporu.md`](../kesif/2026-08-18-tuketici-raporu.md).
>
> 🚨 **Onu da aynı gün plana dönüştü** — altı yeni faz (67–72) ve F-119'un
> [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md)'e katılması. Açık kalem sayısı
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

| ~~**F-121**~~ | ✅ **KAPANDI (2026-08-20)** — kapsamı ölçümle değişti → [Faz 74](74-YEREL-REFERANS-YUZEYI.md) tamamlandı | 2026-08-18 tüketici agent turu · [Faz 73](73-TUKETICI-AGENT-DESTEGI.md) | 🚨 **Kaydın istediği ölçüm yapıldı (2026-08-19) ve `dotnet tool` MCP sunucusu okumasını düşürdü.** Paket **2.96 MB** XML dokümanı (~5 600 üye) sevk ediyor ve o korpus tüketicinin `~/.nuget/packages` dizininde **zaten duruyor**; on gerçek detay sorgusunun **onu da** `grep` ile cevaplandı. Sunucunun `grep` üzerine koyacağı tek yeni yetenek anlamsal aramadır — o da RAG'dir ve Dalga 9'da elendi. Maliyet yapısal: `grep -rn PackAsTool` **boş** — yeni dağıtım kanalı, F-93 ile aynı sınıf; benimseme Faz 73'ün opt-in özelliğinden **kötü**. Ölçüm üç gerçek boşluk buldu ve Faz 74 onları alır: yerel korpusa hiçbir işaret yok, `agentprism.json` (123 path) hiçbir pakete girmiyor, 39 giriş noktasının **27'sinde** çalışan örnek yok. Sunucu reddedilmedi, gerekçesi düştü; Faz 74'ün ölçümüyle yeniden açılabilir |

### F-102

| ~~**F-102**~~ | ✅ **KAPANDI (2026-08-18)** — kırılgan eşzamanlılık testi | 2026-08-08 denetimi | Karar verildi ve uygulandı: **K-385** yeniden deneme döngüsüne jitter ekledi ve üst sınırı 5 → **10**'a çıkardı ([`SqlEvalStore.cs:179`](../src/AgentPrism.Sql.Shared/Stores/SqlEvalStore.cs)). Özgün kayıt: 🚨 **Ölçüldü:** `AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` PostgreSQL paketinin tamamı koşarken düştü (`SqlEvalStore.AddCaseAsync:221` — "5 denemede sira numarasi atanamadi"), **tek başına ve ikinci tam koşumda geçti** (870/870). Testin kendisi mi yoksa `AddCaseAsync`'in 5 denemelik yeniden deneme sınırı mı yetersiz — karara bağlanmalı. Bir kusur değil, **kırılgan bir test** olarak sınıflandırıldı ama sessiz bırakılmadı |

