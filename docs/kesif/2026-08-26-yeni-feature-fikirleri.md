# Keşif Turu — 2026-08-26 · Yeni feature fikirleri

> Bu bir **koşum kaydıdır**, spec değildir. Kullanıcı elemesi alınmadan hiçbir
> kalem `docs/ADAYLAR.md` dosyasına F numarasıyla girmez.

**Tetikleyen:** Kullanıcının projeye 10 yeni feature fikri istemesi ("iyi ölçümle").
**Zemin:** Faz 109 kapalı · Faz 110 ve 111 📋 Planlandı · `ADAYLAR.md` beş
planlanabilir aday taşıyor (F-67, F-109, F-149, F-152, F-165) · ölçülen en
büyük numara **F-165**.
**Ekosistem taraması:** 2026-08-26 · web erişimi **var**.
**Önceki tur:** [`2026-08-23-yeni-feature-fikirleri.md`](2026-08-23-yeni-feature-fikirleri.md)
— aynı istek, on kalem, **kullanıcı elemesi alınmadı**. Bu tur o kalemleri
bugünün ağacında yeniden ölçtü; ikisi elendi, beşi ayakta kaldı.

---

## 1. Ölçülen zemin

| Ölçüm | Komut / konum | Bulgu |
|---|---|---|
| Kota zamanlaması | `QuotaEnforcer.cs:200`, `QuotaGate.cs:34-47` | Tavan **run öncesi bir kez** ve **dönem birikimi** üzerinden bakılıyor. Tek bir run tavanı aşabilir. |
| Bütçe hata sınıfı | `grep -rn "BudgetExceeded" src/` | `RunErrorClass.cs:63` + generated client. **Üreten kod yok.** |
| MCP sunucusu | `grep -rn "Stateless\|MRTR\|Tasks\|Elicit" src/AgentPrism.AspNetCore/McpServer src/AgentPrism.Mcp` | **Sıfır isabet.** |
| MCP SDK sürümü | `Directory.Packages.props:162,172` | `ModelContextProtocol.Core` / `.AspNetCore` **2.2.0**. |
| MAF sürümü | `Directory.Packages.props:19` | `MicrosoftAgentsAIVersion` = **1.18.0**. Hosting hattı 1.18.0-preview/alpha. |
| CLI yüzeyi | `ls src/AgentPrism.Cli/Commands/` | `Health`, `Migrate`, `MigrateStatus`. Eval, run veya agent komutu yok. |
| Eval altyapısı | `ls src/AgentPrism.*/Evaluation/` | `EvalSuite`, `EvalRun`, `IRunJudge`, `EvalJobHandler`, `RunToCasePromoter` tam. Başsız koşucu yok. |
| A2A yeteneği | `AgentPrismA2AExtensions.cs:171` | `Streaming = false, PushNotifications = false`. |
| Webhook redrive | `grep -rn "Redrive\|DeadLetter" src/` | **Sıfır isabet.** |
| Model capability | `IModelProvider.cs:69-75`, `AgentDefinitionCompiler.ChatOptions.cs:208` | Yalnız `SupportsStructuredOutput` gerçek kapı; diğer üçü **advisory** ilan edilmiş. |
| Onay politikası | `grep -rn "simulate\|explain" src/AgentPrism.AspNetCore/Endpoints` | Yalnız `DataSubjectEndpoints` `dryRun`'ı. Tool policy için yok. |
| RAG citation | `VectorSearchHit.cs:1-21`, `grep -rn "Citation" src/` | Hit `SourceId`/`ChunkIndex` taşıyor. AgentPrism'in kendi citation sözleşmesi **yok** (yalnız MEAI tipi generated client'ta). |
| Prompt cache yönlendirmesi | `AnthropicProviderSettingsChatClient.cs:105` | Anthropic `cache_control` **var**. Google/OpenAI karşılığı yok. |
| Prompt cache raporlama | `ModelDescriptor.cs:46`, `RunSupportTypes.cs:41,117` | `CachedInputTokens` + `CachedInputCost` tam. |
| OTel | `ModelProviderRegistry.cs:456` | `UseOpenTelemetry(...)` **zaten** pipeline'da. |
| Aspire | `grep -rl "Aspire" src/ samples/` | **Sıfır isabet.** Yalnız `docs/` içinde tartışılmış (F-51). |
| Yerel model | `OpenAIProviderOptions.cs:31` | `Endpoint` alanı var; OpenAI-uyumlu yerel uç zaten bağlanabilir. |
| Yanıt önbelleği | `ResponseCacheSettings.cs`, `ModelProviderRegistry.cs:434` | Tenant/provider/tool-set farkındalı **tam eşleşme** anahtarı. Semantik değil. |

---

## 2. Ekosistem taraması

| Kaynak | Bakılan tarih | Ne değişti | AgentPrism'e etkisi |
|---|---|---|---|
| [MCP 2026-07-28 spesifikasyonu](https://modelcontextprotocol.io/specification/2026-07-28/changelog) | 2026-08-26 | Stateless çekirdek, Multi Round-Trip Requests (SEP-2322), header tabanlı yönlendirme, cache'lenebilir list sonuçları, authorization sertleştirmesi, resmî extension çerçevesi. Tasks resmî extension oldu. | AgentPrism'in MCP sunucusu bu yüzeylerin hiçbirini taşımıyor. 2026-08-23 turunun 2025-11-25 tabanlı gerekçeleri **bayatladı**. |
| [MCP C# SDK v2.0](https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/) | 2026-08-26 | `HttpServerTransportOptions.Stateless` varsayılanı **true**. Stateless sunucu transport oturumu açmaz, standalone SSE GET/DELETE ucunu sunmaz, istenmeyen sunucu→istemci isteğini desteklemez. MCP Apps ve Tasks ayrı extension paketleri. 2025-11-25 ve öncesiyle down-level uyum var. | Repo zaten 2.2.0'da. Bu varsayılanın mevcut davranışı sessizce değiştirip değiştirmediği **ölçülmedi** (Kanal 2). |
| [MAF Build 2026 duyurusu](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-at-build-2026-announce/) | 2026-08-26 | Agent Harness, hosted agents, CodeAct, GitHub Copilot SDK backend'i. | Harness zaten kullanılıyor (`AgentDefinitionCompiler.Agents.cs:279`). CodeAct K2'yi ihlal eder. |
| [MAF sürüm notları](https://github.com/microsoft/agent-framework/releases) | 2026-08-26 | 1.0 GA 2026-04-02. **1.19.0**: session-persisted chat routing, Azure Blob session persistence, **experimental agent-hooks interception**. 1.15.0: A2UI, workflow checkpoint registry. | Repo 1.18.0'da. agent-hooks, **F-95**'in "MAF kancası yok" gerekçesini geçersiz kılmış olabilir (Kanal 3). |
| [LLM gateway karşılaştırmaları 2026](https://insights.nomadlab.cc/blog/2026/05/langfuse-helicone-portkey-litellm-openrouter-2026) | 2026-08-26 | Üretim yığını "her katman için bir araç" deseninde: orchestration runtime · observability · gateway. Portkey tek kontrol düzleminde gateway + observability + prompt yönetimi + governance topluyor. | AgentPrism .NET/MAF tarafında bu üç katmanı zaten birleştiriyor. Bu taramada AgentPrism'de olmayan ve **ölçülebilir** tek gateway yeteneği semantik önbellek çıktı; değeri düşük ve riski yüksek görüldü (bkz. § 5). |

---

## 3. Ham liste ve eleme

| # | Fikir | Kim için | Neden şimdi | Sonuç |
|---:|---|---|---|---|
| 1 | Çalıştırma-içi maliyet tavanı (mid-run kill switch) | Platform ekibi · nöbetçi | `BudgetExceeded` beyan edilmiş, üretilmiyor; tavan yalnız run öncesi bakılıyor | ✅ |
| 2 | MCP 2026-07-28 hizalanması (stateless · MRTR · Tasks) | AgentPrism'i MCP sunucusu olarak tüketen ekip | Spec ve SDK 2.x yayında; repo SDK'yı almış, yüzeyi almamış | ✅ |
| 3 | Eval'in başsız koşucusu ve CI kapısı | MAF'ı zaten kullanan ekip · platform ekibi | Eval altyapısı tam, koşum yolu yok; CLI üç komut taşıyor | ✅ |
| 4 | A2A dayanıklı task store + streaming/push | Agent'ını başka agent'lara açan ekip | Agent card ikisini de `false` ilan ediyor | ✅ (devir) |
| 5 | Webhook dead-letter ve redrive | Nöbetçi mühendis | `Failed` durumu var, operatörün yeniden sürme yolu yok | ✅ (devir) |
| 6 | Model capability'lerinin derleme kapısına dönmesi | İlk agent'ını kuran geliştirici | Üç capability "advisory" ilan edilmiş; uyumsuzluk çalışma anına kalıyor | ✅ (devir) |
| 7 | Tool onay politikası için explain/simulate ucu | Güvenlik yöneticisi | Karar `bool`'a iniyor; "hangi kural neden eşleşti" sorulamıyor | ✅ (devir) |
| 8 | RAG kaynak soy zinciri ve citation sözleşmesi | Regüle domain ekibi | Hit `SourceId` taşıyor, yanıt taşımıyor | ✅ (devir) |
| 9 | Prompt-cache yönlendirmesinin üç sağlayıcıya yayılması | Model faturası büyüyen ekip | Anthropic'te var, Google/OpenAI'de yok; raporlama üçünde de var | ✅ |
| 10 | Aspire entegrasyon paketi (AppHost + ServiceDefaults) | İlk agent'ını kuran geliştirici | Repo'da sıfır isabet; .NET dağıtım yolunun varsayılanı | ⚠️ talep kanıtı eksik |
| 11 | GenAI OTel semconv conformance modu | İşletim ekibi | `UseOpenTelemetry` zaten pipeline'da; iş bir faz değil test seti | ❌ elendi |
| 12 | Yerel model sağlayıcısı (Ollama · Foundry Local) | Havuz dışı çalışan ekip | `OpenAIProviderOptions.Endpoint` yerel OpenAI-uyumlu ucu zaten karşılıyor | ❌ elendi |
| 13 | MAF CodeAct desteği | — | Agent'a kod yazdırır; **K2'yi doğrudan ihlal eder** | ❌ anında elendi |
| 14 | Semantik yanıt önbelleği | Model faturası büyüyen ekip | Yanlış eşleşme yanlış cevap döndürür; mevcut tam-eşleşme önbelleği güvenli | ❌ elendi |
| 15 | MCP uzak skill'leri (2026-08-23 #3) | Ortak skill kataloğu kullanan ekip | Yeni spec elicitation/MRTR'yi yeniden yazdı; #2 ölçülmeden tasarlanamaz | ❌ bu turda değil |
| 16 | Graph-wide workflow retry (2026-08-23 #5) | Workflow kullanan üretim ekibi | F-95 karar eşiğine bağlı; agent-hooks kararı önce verilmeli | ➡️ Kanal 3 |

**Önerilen üç kalem:** 1, 2, 3.

- **1** tek başına bir güvenlik/FinOps boşluğu **ve** bir beyan hatası kapatır.
- **2** dış dünyaya açılan tek protokol yüzeyinde bayatlama riskini durdurur; SDK maliyeti zaten ödenmiş.
- **3** F-67 ve F-165 ile aynı kapı altyapısını paylaşır; üçü tek dalgada ucuzlar.

---

## 4. Ayakta kalan kalemlerin kanıt ve karşı görüş satırları

### 1 · Çalıştırma-içi maliyet tavanı

**Kanıt:** Ölçüldü — `QuotaEnforcer.cs:200` dönem birikimini run **öncesi**
karşılaştırır; `QuotaGate.cs:34` bunu tek seferlik ön uçuş olarak çağırır.
`grep -rn "MaxCost" src/AgentPrism.Core/Recording src/AgentPrism.Core/Compilation`
sıfır isabet verir. `RunErrorClass.cs:63` `BudgetExceeded = 9` taşır ve `src/`
içinde onu **üreten kod yoktur**.
**Mercek:** 2, 3, 8.
**Eleyici sınır:** K2/K3 uyumlu. AOT dostu. Public yüzey büyür (`ModelBinding`
veya `AgentDefinition` üzerinde run bütçesi alanı). Migration gerekmeyebilir.
**Karşı görüş:** Tavanı run ortasında uygulamak yarım bir yanıt üretir ve
tüketici bunu "sessiz kesme" olarak görebilir. Kesme noktası tool turu sınırında
tanımlanmazsa davranış öngörülemez olur.

### 2 · MCP 2026-07-28 hizalanması

**Kanıt:** Ölçüldü — `src/AgentPrism.AspNetCore/McpServer` ve `src/AgentPrism.Mcp`
içinde `Stateless|MRTR|Tasks|Elicit` sıfır isabet. `Directory.Packages.props:162,172`
SDK'yı 2.2.0'a sabitliyor. Ekosistem kanıtı 2026-08-26 tarihli spec changelog'u
ve SDK v2.0 duyurusu.
**Mercek:** 2, 3, 6.
**Eleyici sınır:** K2 korunur (uzak taraf kod çalıştırmaz). Yeni NuGet paketi
**olabilir** (Tasks extension paketi) — geçişli ağırlığı sayılmalıdır. Public
MCP sözleşmesi büyür. Tenant/auth bağlamının task kimliğine sıkı bağlanması
zorunludur.
**Karşı görüş:** Spec bir ay önce yayınlandı ve down-level uyum zaten var.
AgentPrism'in MCP sunucusu loopback + bearer + policy ile korunuyor ve uzak
erişimle birlikte açılmıyor; stateless yönlendirmenin bugün ölçülmüş bir
tüketicisi yok.

### 3 · Eval'in başsız koşucusu ve CI kapısı

**Kanıt:** Ölçüldü — `ls src/AgentPrism.Cli/Commands/` üç komut verir.
`src/AgentPrism.Core/Evaluation/` on beş dosyayla tam bir eval çekirdeği taşır.
İkisini birleştiren, exit code üreten bir yol yoktur.
**Mercek:** 3, 7.
**Eleyici sınır:** K2/K3 uyumlu. Yeni paket gerekmez (`AgentPrism.Cli` vardır).
Public CLI sözleşmesi büyür — CLI yüzeyi de bir uyumluluk taahhüdüdür.
**Karşı görüş:** Model yanıtı deterministik değildir; eşiği yanlış seçilen bir
eval kapısı CI'ı gürültüyle kapatır. F-67'nin gürültü eşiği sorunuyla aynı
sorundur ve aynı kararı ister.

### 4 · A2A dayanıklı task store + streaming/push

**Kanıt:** Ölçüldü — `AgentPrismA2AExtensions.cs:171` `Streaming = false,
PushNotifications = false`. 2026-08-23 turunda da ölçülmüştü; bugün değişmemiş.
**Mercek:** 1, 3, 6.
**Eleyici sınır:** K3 korunur. Mevcut `A2A.AspNetCore` + MAF Hosting hattı
preview'dadır (K-008 sınırı). SQL migration ve tenant korelasyonu gerekir.
**Karşı görüş:** Push callback'i webhook'a benzeyen ikinci bir teslimat problemi
açar. İlk sürüm SQL task store + polling ile sınırlanmazsa kapsam büyür.

### 5 · Webhook dead-letter ve redrive

**Kanıt:** Ölçüldü — `grep -rn "Redrive\|DeadLetter" src/` sıfır isabet.
`WebhookDeliveryStatus.Failed` ve teslimat geçmişi vardır.
**Mercek:** 2, 3, 7.
**Eleyici sınır:** K2/K3/AOT etkisi yok. Mevcut job kuyruğu kullanılabilir.
Public admin sözleşmesi ve denetim izi gerekir.
**Karşı görüş:** Yeniden sürme dış sistemde yinelenen yan etki üretir. Yeni
delivery kimliği, orijinal kimlik soy zinciri ve operatör gerekçesi olmadan bu
yetenek güvenli değildir.

### 6 · Model capability'lerinin derleme kapısına dönmesi

**Kanıt:** Ölçüldü — `IModelProvider.cs:69-75` üç capability'yi açıkça
"advisory metadata" ilan eder; `AgentDefinitionCompiler.ChatOptions.cs:208`
yalnız `SupportsStructuredOutput` için kapı kurar.
**Mercek:** 1, 5, 8.
**Eleyici sınır:** Yeni model listesi yazılmaz (L25 korunur). AOT dostu.
Bilinmeyen model için fail-open/fail-closed kararı gerekir.
**Karşı görüş:** Katalog verisi tüketicinin yapılandırmasından gelir ve bayat
olabilir. Yanlış negatif çalışan bir agent'ı derlemede durdurur — bugünkü
çalışma anı hatasından daha kötü bir sonuçtur.

### 7 · Tool onay politikası için explain/simulate ucu

**Kanıt:** Ölçüldü — `grep -rn "simulate\|explain" src/AgentPrism.AspNetCore/Endpoints`
yalnız `DataSubjectEndpoints`'in `dryRun`'ını bulur. Onay yüzeyinde karşılığı yoktur.
**Mercek:** 2, 3, 5.
**Eleyici sınır:** K2 korunur — simülasyon hiçbir tool gövdesi çalıştırmaz.
Argüman redaksiyonu zorunludur. Public admin sözleşmesi büyür.
**Karşı görüş:** Kod ile tanımlanmış bir politikanın "neden"i genel olarak
üretilemez. Yetenek yalnız veri kuralları için başlatılmalı, yoksa yüzey
yanıltıcı olur.

### 8 · RAG kaynak soy zinciri ve citation sözleşmesi

**Kanıt:** Ölçüldü — `VectorSearchHit.cs:1-21` `SourceId` ve `ChunkIndex`
taşır. `grep -rn "Citation" src/` AgentPrism'in kendi tipini bulmaz; yalnız
generated client'ta MEAI tipi görünür.
**Mercek:** 1, 3, 7.
**Eleyici sınır:** K2 uyumlu. AOT için tipli metadata seçilmelidir. Sağlayıcıya
özgü citation ayrıştırması yapılmaz. Public yanıt/olay sözleşmesi büyür.
**Karşı görüş:** Citation zorunluluğu grounding garantisi vermez; model yanlış
kaynağa atıf yapabilir. İlk adım "kaynak kanıtı mevcut" işareti ve bir eval
kontrolü olmalıdır.

### 9 · Prompt-cache yönlendirmesinin üç sağlayıcıya yayılması

**Kanıt:** Ölçüldü — `AnthropicProviderSettingsChatClient.cs:105` `cache_control`
gövdeye yazar. Google ve OpenAI sağlayıcılarında karşılığı yoktur. Raporlama
tarafı (`ModelDescriptor.cs:46`, `RunSupportTypes.cs:41,117`) üçünde de tamdır.
**Mercek:** 6, 8.
**Eleyici sınır:** K3 uyumlu. AOT dostu. Sağlayıcı başına yüzey büyür; ortak
soyutlama F-149 seam tartışmasıyla çakışabilir.
**Karşı görüş:** OpenAI prefix cache'i otomatiktir ve yönlendirme istemez; bu
durumda yetenek yalnız Google için gerçek iş üretir ve bağımsız faz eşiğini
karşılamayabilir.

### 10 · Aspire entegrasyon paketi

**Kanıt:** Ölçüldü — `grep -rl "Aspire" src/ samples/` sıfır isabet.
**Talep kanıtı yoktur** — F-51 tam bu sebeple "ölçüm bekliyor" kanalındadır.
**Mercek:** 1, 6.
**Eleyici sınır:** Yeni NuGet paketi ve geçişli ağırlık; Faz 27'nin 37-paket
ölçümü emsaldir (K-212).
**Karşı görüş:** Bu kalem F-51'in kopyasıdır ve F-51'i bugün adaylıktan alıkoyan
şey ölçüm eksikliği değil, **gerçek tüketici talebinin bulunmamasıdır**. Talep
kanıtı üretilmeden listeye alınırsa aynı ölçüm ikinci kez yapılmış olur.

---

## 5. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye |
|---|---|---|---|
| GenAI OTel semconv conformance modu | `ModelProviderRegistry.cs:456` `UseOpenTelemetry`'yi zaten pipeline'a koyuyor. Kalan iş bir conformance test seti; faz eşiğini karşılamaz. | Hayır, bu turda değil | Bu not |
| Yerel model sağlayıcısı (Ollama · Foundry Local) | `OpenAIProviderOptions.cs:31` `Endpoint` alanı OpenAI-uyumlu yerel ucu zaten bağlar. Yeni sağlayıcı paketi ölçülmüş bir boşluk kapatmaz. | Hayır, bu turda değil | Bu not |
| MAF CodeAct desteği | ⚠️ **Bu satırın ilk gerekçesi yanlıştı ve 2026-08-26'da ölçümle düzeltildi.** Kalem K2'yi ihlal etmiyor; taşınabilirlik eşiğinde takılıyor. Tam ölçüm § 9'dadır. | **Hayır** | `ADAYLAR.md` § *Aday Olmayan Açık Kayıtlar* → **F-169**, karar/uyumluluk kanalı |
| Semantik yanıt önbelleği | Yanlış eşleşme yanlış yanıt döndürür. Mevcut tam-eşleşme önbelleği (tenant/provider/tool-set farkındalı) bu riski taşımıyor. | Hayır, bu turda değil | Bu not |
| MCP uzak skill'leri | 2026-07-28 spec'i elicitation akışını MRTR ile yeniden yazdı. Kalem #2 ölçülmeden tasarlanamaz. | Hayır, bu turda değil | Bu not |

---

## 6. Üç kanalın çıktısı

### Kanal 1 — yeni aday

**Kullanıcı elemesi alındı (2026-08-26): kalemler 1, 2, 3 seçildi.**

| F-NN | Başlık | `ADAYLAR.md`'ye yazıldı mı |
|---|---|---|
| **F-166** | Çalıştırma-içi maliyet tavanı | ✅ Evet |
| **F-167** | MCP 2026-07-28 sözleşmesine hizalanma | ✅ Evet |
| **F-168** | Eval'in başsız koşucusu ve CI kapısı | ✅ Evet |
| **F-95** | Agent düzeyinde kesinti/devam kancası (yeniden açıldı) | ✅ Evet — Kanal 3 kararı sonucu |
| 4–10 | Seçilmeyen yedi kalem | ❌ Hayır — bu notta kalır. Kanıtları burada durur; talep doğarsa numara verilir |

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` koşuldu mu |
|---|---|---|---|
| **Doğrulandı.** `RunErrorClass.BudgetExceeded = 9` public enum'da beyan edilmiş; **hiçbir kod yolu onu üretemez**. Faz 104'ün beyan doğruluğu kapsamına girer. | Tüm repo taraması: yalnız tanım (`RunErrorClass.cs:63`), `PublicAPI.Unshipped.txt:2174`, `docs/openapi/agentprism.json:13029`, `packages/agentprism-client/src/schema.ts:5480`, generated client ve **iki dil dosyası** (`locales/en/runs.ts:208`, `locales/tr/runs.ts:213`). `DefaultRunErrorClassifier.cs` hiçbir exception'ı buna eşlemez. Tree budget tükendiğinde `ChildAgentInvoker.cs:230-232` **exception atmaz**, modele metin döndürür — run başarılı biter. | Evet | ⏸ Düzeltme yönü kullanıcı kararı bekliyor (bkz. § 8) |
| **Çürütüldü — kusur değil.** MCP sunucusunun stateless olması sessiz bir davranış değişimi değildir. | **Çalışma anı probu (2026-08-26):** kurulu `ModelContextProtocol.AspNetCore` **2.2.0** paketinde `HttpServerTransportOptions` varsayılanları `Stateless = True`, `SessionMode = Stateless`, `EnableLegacySse = False`. `AgentPrismMcpServerBuilderExtensions.cs:60` `WithHttpTransport()`'u seçeneksiz çağırır → SDK varsayılanı geçerli. **Ama** `git log -p -- Directory.Packages.props` gösteriyor ki repo bu paketi **doğrudan 2.0.0'da** aldı ve 2.2.0'a çıktı; **1.x hiç kullanılmadı**, yani stateless varsayılanı hiçbir zaman değişmedi. `McpServerEndpointTests.cs` ve `McpTestClient` yüzeyi test ediyor. | Evet | Gerek yok — kapandı |

**Sınıf taraması (`kusur-giderme` zorunlu adımı):** `RunErrorClass`'ın **on dört
üyesinin tamamı** tarandı. `BudgetExceeded` üreticisi **sıfır** olan **tek**
üyedir; diğer on üç üyenin hepsinin en az bir üreticisi vardır. Kusur bir sınıf
değil, tek bir vakadır.

### Kanal 3 — yeniden açılması önerilen karar

| Kayıt | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| **F-95** (karar/uyumluluk eşiği) | "Agent düzeyinde checkpoint hook'u — **MAF kancası yok**; AgentPrism'in paralel katmanı K3'ü zorlar" | MAF **1.19.0** sürüm notu doğrulandı (2026-08-26, releases sayfası okundu): ".NET: agent-hooks interception contract as a first-class experimental feature", [PR #7564](https://github.com/microsoft/agent-framework/pull/7564). 1.18.0 notlarında yoktur. Repo 1.18.0'dadır. | ✅ **Yeniden açıldı.** `ADAYLAR.md`'ye aday olarak yazıldı; imza `maf-api-kesfi` ile doğrulanmadan plana dönmez |
| **K-008** (ön sürüm MAF paketleri yalnız `AgentPrism.AspNetCore`) | Ön sürüm bağımlılığı tüketici grafiğine sızmasın | MAF 1.0 GA 2026-04-02'de çıktı, fakat `Microsoft.Agents.AI.Hosting*` hattı **hâlâ** preview/alpha (`Directory.Packages.props:37-50`). | Değişiklik gerekmiyor — karar geçerli |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| Hangi kalemler `ADAYLAR.md`'ye aday olarak girsin? | **Kalemler 1, 2, 3.** → F-166, F-167, F-168 yazıldı. |
| `BudgetExceeded` beyan hatası için `kusur-giderme` koşulsun mu? | **Evet.** Repro sabitlendi, sınıf taraması yapıldı; **düzeltme yönü** açık kaldı (§ 8). |
| MCP stateless varsayılanı için çalışma anı probu koşulsun mu? | **Evet.** Koşuldu; **kusur çıkmadı**, bulgu kapandı. Ölçüm F-167'nin `Hazırlık` satırına taşındı. |
| F-95 (agent-hooks) yeniden açılsın mı? | **Evet.** MAF 1.19.0 sürüm notu doğrulandı; F-95 adaylığa döndü. |
| MAF CodeAct kalıcı rette `ADAYLAR.md` § *Bilerek Önerilmeyenler*'e yazılsın mı? | **Hayır.** Kullanıcı ölçüm istedi; ölçüm kalıcı retti de adaylığı da çürüttü. Kalem **F-169** olarak karar/uyumluluk kanalına yazıldı (§ 9). |

## 8. Açık kalan tasarım kararı — `BudgetExceeded`'in düzeltme yönü

Kusur doğrulandı, fakat iki geçerli düzeltme vardır ve seçim **public
contract** kararıdır:

| Yön | Ne yapılır | Lehine | Aleyhine |
|---|---|---|---|
| **A — Üyeyi kaldır** | `RunErrorClass.BudgetExceeded` silinir; OpenAI belgesi, TS şeması, generated client, `PublicAPI.Unshipped.txt` ve iki dil dosyası birlikte güncellenir | Beyan gerçeğe uyar. **Bugün ucuz:** `PublicAPI.Shipped.txt` preview boyunca boştur (K-603); 1.0 sonrası aynı silme kırıcıdır | Public enum üyesi silmek yine de bir sözleşme değişikliğidir |
| **B — Üyeyi ürettir** | Tree/context budget tükenmesi terminal bir run hatasına çevrilir | Beyan olduğu gibi kalır | `ChildAgentInvoker.cs:230-232` bütçe tükenince **bilerek** modele metin döndürür — agent alt-agent olmadan yanıtı toparlayabilir. B bu tasarımı tersine çevirir ve **davranış değiştirir** (MEMORY.md: davranışı düzeltmek çağıranı sessizce değiştirir) |

Ölçüm **A**'yı destekler: hiçbir bütçe yolu terminal hata *istemiyor*.

**Karar (2026-08-26, kullanıcı): A — üye kaldırıldı.** Kalıcı kayıt **K-627**.
Sayısal değer `9` **emekli**; yeniden numaralandırma yapılmadı, çünkü kayıtlı
run'lar ve eski istemciler eski anlamı taşır.

Uygulanan adımlar ve doğrulama:

| Adım | Sonuç |
|---|---|
| Düşen test önce yazıldı (`RunErrorClassContractTests`) | 2/2 **kırmızı** — üye kümesi ve `9`'un tanımlı olması |
| `RunErrorClass.cs` · `PublicAPI.Unshipped.txt` · `locales/en/runs.ts` · `locales/tr/runs.ts` | Üye ve iki sözlük anahtarı kaldırıldı; `9`'un neden emekli olduğu koda yorum olarak yazıldı |
| OpenAPI belgesi yeniden üretildi | `AGENTPRISM_OPENAPI_REFRESH=1` · 671/671 yeşil |
| TypeScript şeması yeniden üretildi | `npm run generate` |
| NSwag C# istemcisi yeniden üretildi | dört script zinciri |
| `@agentprism/client` `dist` yeniden derlendi | **atlanırsa frontend `tsc` kırmızı kalır** — bayat `dist` tuzağı |
| Aynı test yeniden koşuldu | 2/2 **yeşil** |
| Sözleşme sınıfı taraması | `RunErrorClass`'ın 14 üyesi tarandı; üreticisiz **tek** üye buydu |
| Frontend | `tsc --noEmit` temiz · 222/222 test yeşil |
| .NET | Çözüm derlemesi **0 uyarı** · `Core.UnitTests` 1972/1972 · `Client.UnitTests` 3/3 |
| `kapi.py tarama` | ✅ temiz |
| Hafıza notu | `hafiza/cekirdek-calistirma.md` (üretilemeyen enum üyesi) · `hafiza/paketleme-ve-dagitim.md` (dört adımlı yeniden üretim zinciri) |


## 9. F-169 · MAF CodeAct / Hyperlight — ölçüm ve sınıflandırma

**Tetikleyen:** Kullanıcı, kalıcı ret önerimi kabul etmeden ölçüm istedi. İyi
yaptı: ölçüm **hem** ilk gerekçemi **hem** alternatif adaylığı çürüttü.

### 9.1 Önceki gerekçe neden yanlıştı

K2'nin metni (`faz-planlama` SKILL.md:97) şudur: *"**Arayüzden** çalıştırılabilir
kod/ifade tanımlatan hiçbir tasarım kabul edilmez."* 2026-08-01 kararı da aynı
tehdidi tarif eder: *"Arayüze erişen herkes sunucuda kod çalıştırabilirdi."*
CodeAct'te kodu kullanıcı arayüzden yazmaz — **model** çalışma anında yazar.
Bu, K2'nin kapattığı tartışma değildir.

Ayrıca K2'nin zaten **iki bilinçli istisnası** vardır (MCP · skill script) ve
skill script istisnası şunu açıkça ilan eder (`MIMARI-GUVENLIK.md:241`):
*"AgentPrism dosya sistemi hapsi, ağ kısıtı, bellek/CPU kotası ve hak düşürme
SAĞLAMAZ."* Hyperlight bu dördünü kutudan verir. Yalıtım ekseninde CodeAct,
repo'nun **zaten sevk ettiği** istisnadan zayıf değil, **güçlüdür**.

### 9.2 Ölçülen zemin (2026-08-26, `dotnet package search` + gerçek restore)

| Ölçüm | Sonuç |
|---|---|
| `Microsoft.Agents.AI.Hyperlight` (MS dokümanının verdiği ad) | **NuGet'te YOK** — `--exact-match` boş döner |
| `CodeAct` adlı paket | Yok; ilgisiz sonuçlar |
| Gerçek paketler | `Hyperlight.HyperlightSandbox.{Api,PInvoke,Extensions.AI,Guest.Python,Guest.JavaScript}` — sahip `hyperlight-dev`, kaynak `github.com/hyperlight-dev/hyperlight-sandbox` |
| Sürüm | **0.5.0** — pre-1.0, GA değil |
| Geçişli grafik (`Extensions.AI`, net10.0, gerçek restore) | **9 paket** — hafif; K-212'nin 37-paket vakasıyla kıyaslanmaz |
| MEAI sürüm hattı | Paket **9.5.0**'a karşı derlenmiş; repo **10.9.0**'da |
| **Native RID kapsamı** | **yalnız `win-x64` ve `linux-x64`** |

### 9.3 Kararı veren ölçüm

`runtimes/` altındaki native varlıklar yalnız iki RID taşır:
`win-x64/native/hyperlight_sandbox_dotnet_ffi.dll` ve
`linux-x64/native/libhyperlight_sandbox_dotnet_ffi.so`. **macOS yoktur ve
hiçbir ARM hedefi yoktur.**

Sonuçları:

- Bu deponun geliştirme makinesi `osx-arm64`'tür. Özellik **bakımcının kendi
  makinesinde çalışamaz** — manuel kabul koşumu ve örnek uygulama doğrulaması
  (K-166/K-167: "birim testi yetmez, örnek uygulamayı gerçekten çalıştır")
  yapılamaz.
- ARM Linux (Graviton, Ampere) kapsam dışıdır.
- AgentPrism bir **NuGet ailesidir**; native RID bağımlılığı tüketicinin
  dağıtım hedefini daraltır.

### 9.4 Sınıflandırma

Bu tablo **F-72'nin (ACS) blocker'ının birebir aynısıdır**: *"ACS paketinin
beta/native RID bağımlılığı — paket GA ve taşınabilir olmadan ürün uyumluluk
kararı erken."* Aynı eşik, aynı kanal.

| Kanal | Karar |
|---|---|
| Aday | **Hayır** — taşınabilirlik eşiği aşılmadı |
| Bilerek önerilmeyenler (kalıcı ret) | **Hayır** — gerekçe mimari değil, olgunluk. Kalıcı rette yazmak gelecekteki bir oturumun meşru bir işi ölçmeden reddetmesine yol açardı |
| **Karar / uyumluluk eşiği** | ✅ **F-169** olarak yazıldı |

**Yeniden açılma koşulu:** Hyperlight sandbox paketleri 1.0'a çıkar **ve**
en az `osx-arm64` + `linux-arm64` native varlıklarını taşır. O zaman kalan iki
soru ölçülür: (1) guest'e mount edilen tool zincirinin prompt-injection yüzeyi,
(2) değerin gerçekliği — CodeAct'in vaadi yeni bir yetenek değil, var olan
tool döngüsünün gecikme ve token maliyetinin düşmesidir (Mercek 4 ve 8; 1 ve 3
değil).
