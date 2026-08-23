# Faz 34 — Tanım Doğrulama Ucu

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-60**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/34-TANIM-DOGRULAMA-UCU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir agent tanımını **kaydetmeden** denemenin yolu yoktur. Bugün tek yol tanımı yazmak, kaydetmek ve çalıştırmayı denemektir; hatalıysa katalog kirlenmiş olur. - **F-60** — `POST /api/agents/validate`: bir tanımı kaydetmeden ve **hiçbir model çağırmadan** derler.

## Plandan Sapmalar

1. **🚨 Döngü denetimi YENİ yazılmadı — zaten vardı.** Planın "Bugün ne
   çalışmıyor" kanıt tablosu `grep -rni "cycle|circular"` aramasını yalnız
   `src/AgentPrism.Core/Compilation/` ve `src/AgentPrism.Core/Agents/`
   dizinlerinde çalıştırmıştı. Gerçek statik döngü denetimi
   `src/AgentPrism.Core/Graph/AgentCallGraph.cs`'te **zaten** yaşıyordu ve
   `POST /api/agents`/`PUT /api/agents/{name}` kaydetme anında kullanılıyordu;
   bu dosya ayrı bir karar numarası taşımadan, önceki bir fazda eklenmişti.
   Plan bu yüzden `CallGraphCycleDetector.cs` adında
   yeni bir dosya öngörüyordu; onun yerine `AgentCallGraph`'a kod taşıyan bir
   `ValidateDetailed` metodu eklendi (K-252). Planlanan dosya listesindeki
   `Compilation/CallGraphCycleDetector.cs` **oluşturulmadı**.
2. **`ValidateAsync`'in `mcpTimeout` parametresi kaldırıldı.** Planın taslak
   imzası yalnız `(AgentDefinition, CancellationToken)` idi; ilk taslakta bir
   `TimeSpan mcpTimeout` parametresi eklenmişti ama `AgentPrismValidationOptions`
   `IOptions<AgentPrismOptions>` üzerinden zaten kayıtlı olduğu için parametre
   gereksizdi. Kaldırıldı — planın taslak imzasıyla birebir aynı (K-253).
3. **Açık soru 2'nin "B" seçeneği farklı bir dosyada uygulandı.** Plan
   `AgentPrismEndpointOptions`'ta (AspNetCore) ayarlanabilir bir zaman aşımı
   öneriyordu. `AgentDefinitionValidator` `AgentPrism.Core`'da yaşar ve
   `AgentPrism.AspNetCore`'a bağımlı olamaz (paket yönü tersine döner); ayar bu
   yüzden `AgentPrismOptions.Validation.McpTimeout` (Core) altına kondu — aynı
   yapılandırılabilirlik, doğru katman (K-253).
4. **`unknown_model` denetimi model **adını** değil, sağlayıcı **kaydını**
   denetler.** K-032 model kataloğunun bir doğrulama listesi olmadığını
   söylüyor (`OpenAIModelProvider.CreateChatClient` bilinmeyen bir model adını
   yalnız loglar, reddetmez). Bu yüzden `unknown_model` gerçek derleme
   yolundaki tek kesin hata kaynağını tekrarlar: `ModelBinding.Provider`
   `IModelProviderRegistry.List()`'te kayıtlı mı. Planın denetim listesi bunu
   "model kataloğu" olarak anıyordu; gerçekleşen davranış K-032 ile tutarlıdır,
   yalnız hangi katalogun (sağlayıcı listesi, model listesi değil) denetlendiği
   nettir.
5. **`mcp_unreachable` durumunda `unknown_tool` YAZILMAZ, final derleme de
   ATLANIR.** İlk taslak zaman aşımından sonra da eksik kalan tool adlarını
   `unknown_tool` olarak raporluyordu; bu, DoD'nin "Ulaşılamayan MCP sunucusu
   `inconclusive:true` üretir, `valid` düşmez" şartını ihlal ederdi — hem
   çünkü doğrudan `unknown_tool` (Error) eklenir hem de final derleme aynı
   tool için `AgentPrismCompilationException` fırlatırdı. Düzeltme:
   `Inconclusive` iken ne `unknown_tool` yazılır ne de final derleme çalışır.
   Ölçüldü: `AgentDefinitionValidatorTests.Ulasilamayan_mcp_sunucusu_...` bu
   olmadan kırmızı veriyordu.

## Bu Fazda Verilen Kararlar

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **K-252 — Cağrı grafiği döngü/bilinmeyen-agent denetimi `AgentCallGraph`'ta genişletildi, yeni dosya açılmadı** | 2026-08-06 | Statik döngü denetimi Faz 34 planlanmadan önce zaten vardı (`AgentCallGraph.Validate`, kaydetme anında `POST/PUT /api/agents` tarafından kullanılıyor). Planın kanıt taraması yanlış dizinde arandığı için bunu kaçırmıştı. İki ayrı döngü denetleyicisi (biri kaydetmede, biri doğrulama ucunda) aynı mantığı iki yerde bakımsız bırakırdı — ilk sapma ikincisini yakalamazdı. Çözüm: `Validate(string)` (eski imza, testleri bozulmadan kalır) artık yeni `ValidateDetailed(...)`'i çağırır; ikincisi kod (`unknown_agent`/`cycle`) ve mesaj birlikte taşıyan `AgentCallGraphProblem` döner. | Döngü denetiminin kendisi değişirse (yeni bir hata sınıfı, örn. derinlik tahmini) her iki tüketici de aynı yerden güncellenir |
| **K-253 — `AgentPrismValidationOptions.McpTimeout` `AgentPrismOptions`'a (Core) eklendi, `AgentPrismEndpointOptions`'a (AspNetCore) değil** | 2026-08-06 | Planın açık soru 2'si zaman aşımını `AgentPrismEndpointOptions`'ta öneriyordu, ama `AgentDefinitionValidator` `AgentPrism.Core`'dadır ve `AgentPrism.Core`, `AgentPrism.AspNetCore`'a bağımlı **olamaz** (paket bağımlılık yönü K1'in bir parçası). `IOptions<AgentPrismOptions>` zaten `AgentDefinitionCompiler`'ın da kullandığı ortak yapılandırma kanalıdır; aynı kanaldan okumak yeni bir katman ihlali yaratmadan aynı yapılandırılabilirliği (varsayılan 5 sn, `AgentPrism:Validation:McpTimeout` ile değiştirilebilir) verir. | Doğrulama ucu HTTP katmanına özgü bir ayar (örn. istek başına zaman aşımı) gerektirirse, o ayar `AgentPrismEndpointOptions`'a eklenip `AgentDefinitionValidator.ValidateAsync`'e parametre olarak geçirilebilir |

## Bitiş Ölçütleri (DoD) — gerçekleşen

- [x] Geçerli bir tanım `POST /api/agents/validate` ile `200` +
      `{"valid":true,"messages":[]}` döner — `samples/AgentPrism.Api`'de
      `provider:"openai"` ile doğrulandı (aşağıda çıktı)
- [x] Bilinmeyen tool adı taşıyan tanım `200` + `valid:false` +
      `code:"unknown_tool"` döner
- [x] Üç ayrı hata taşıyan tanım **üç** mesaj döner (ilk hatada durmaz) —
      `Uc_ayri_hata_uc_mesaj_doner_ilkinde_durmaz`
- [x] `A → B → A` çağrı grafiği `code:"cycle"` üretir
- [x] 🚨 Doğrulama sonrası `GET /api/agents` listesi **değişmez** ve
      `GET /api/runs` yeni satır göstermez — hem birim testte hem gerçek
      sunucuda (11→11 agent, 0→0 run) doğrulandı
- [x] Ulaşılamayan MCP sunucusu `inconclusive:true` üretir, `valid` düşmez
- [x] Dört doğrulama kapısı sıfır uyarı verdi
- [x] `samples/AgentPrism.Api` ile gerçek doğrulama yapıldı, çıktı aşağıda
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü: **155,1 KB gzip / 250 KB**
      (önceki 151,3 KB'den +3,8 KB — planın tahmini 1–2 KB'nin biraz üzerinde,
      bütçenin hâlâ **94,9 KB** altında)

### Gerçek sunucu çıktısı (2026-08-06, `samples/AgentPrism.Api`)

```bash
$ curl -s -X POST http://localhost:5080/agentprism/api/agents/validate \
    -d '{"name":"deneme","model":{"provider":"openai","model":"gpt-5.4-mini"},"toolNames":[]}'
{"valid":true,"inconclusive":false,"messages":[]}

$ curl -s -X POST http://localhost:5080/agentprism/api/agents/validate \
    -d '{"name":"deneme","model":{"provider":"openai","model":"gpt-5.4-mini"},"toolNames":["olmayan_tool"]}'
{"valid":false,"inconclusive":false,"messages":[{"severity":"Error","code":"unknown_tool",
  "message":"'deneme' agent'i 'olmayan_tool' adli bir tool'a isaret ediyor ancak bu kodda kayitli degil.",
  "path":"toolNames[0]"}]}

$ curl -s -X POST http://localhost:5080/agentprism/api/agents/validate \
    -d '{"name":"deneme","model":{"provider":"openai","model":"gpt-5.4-mini"},"callableAgentNames":["deneme"]}'
{"valid":false,"inconclusive":false,"messages":[{"severity":"Error","code":"cycle",
  "message":"'deneme' kendisini cagiramaz. ...","path":"callableAgentNames"}]}

# GET /api/agents ve /api/runs uc curl oncesi ve sonrasi ayni sayiyi dondu: 11 ve 0.
```

## Sonraki Faza Devir Notu

**Sıradaki faz: 35** — [`35-MALIYET-VE-KOTA-METRIKLERI.md`](35-MALIYET-VE-KOTA-METRIKLERI.md).

- **`AgentCallGraph.ValidateDetailed` artık tek doğruluk kaynağı.** Çağrı
  grafiğiyle ilgili yeni bir hata sınıfı (örn. derinlik tahmini, döngü
  uzunluğu sınırı) eklenirse hem `Validate` (kaydetme, `400`) hem
  `ValidateDetailed` (doğrulama ucu, tipli kod) **aynı yerden** güncellenir —
  ikisini ayrı ayrı senkronize etmeye gerek yok.
- **`AgentDefinitionValidator` `AgentPrism.Core`'da, `IAgentCatalog`'u
  doğrudan enjekte eder.** `CallableAgentResolver`'ın aksine gecikmeli
  çözümlemeye ihtiyacı yoktur çünkü `IAgentCatalog`'un **kendi** kurulumunun
  bir parçası değildir — `IAgentCatalog` zaten tamamen kurulduktan sonra
  devreye giren bir tüketicidir. Yeni bir Core servisi `IAgentCatalog`
  isterse önce bu ayrımı (kurulumun parçası mı, sonradan gelen tüketici mi)
  netleştirin.
- **🚨 `mcp_unreachable` durumunda ilgili tool adları için `unknown_tool`
  YAZILMAZ ve final derleme ÇALIŞMAZ.** Bu bilinçli bir tasarımdır (K-253'ün
  gerekçesiyle aynı ruhta): "sunucuya ulaşılamadı" ile "ad yanlış" farklı hata
  sınıflarıdır. Faz 35 maliyet/kota metriklerine dokunuyorsa bu ayrımla
  karışmaz; ama gelecekte doğrulama ucuna yeni bir ağ-bağımlı denetim
  eklenirse aynı deseni (başarısız → Inconclusive, final adımı atla) tekrarlayın.
- **`AgentPrismValidationOptions` `AgentPrismOptions`'ın altında yaşıyor**,
  yeni bir `AgentPrism:Validation:McpTimeout` yapılandırma anahtarı açtı.
  `AgentPrismOptionsValidator` bunu doğruluyor (sıfırdan büyük olmalı).
- **Yarım kalan iş yok.** F-48 (GitOps: tanım dışa/içe aktarımı) bu ucu CI
  adımı olarak kullanacak; bu faz onun önkoşulunu (kaydetmeden derleme)
  hazırladı, F-48'in kendisi bu dalgada değildi.
