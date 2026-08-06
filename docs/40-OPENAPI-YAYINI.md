# Faz 40 — OpenAPI Belgesinin Yayımlanması

> **Durum:** 📋 Planlandı (2026-08-06)
> **Kaynak:** [UCUNCU-FAZ-ADAYLARI.md](UCUNCU-FAZ-ADAYLARI.md) · **F-63**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.AspNetCore`
> **Yeni paket:** Yok — 🚨 gerekçe [40.1](#401--k-039-korunur--bu-fazın-ana-kısıtı) · **Migration:** Yok
> **Public API:** büyümüyor — uç **üstverisi** zenginleşir, imzalar değişmez

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-007\|K-039" docs/KARARLAR.md
   sed -n '35p' docs/KARARLAR.md      # reddedilen is L35
   ```
   **K-039** (🚨 **bu fazın ana kısıtı** — kütüphane OpenAPI üretimini
   dayatmaz, yalnız üstveri taşır; `Microsoft.AspNetCore.OpenApi` CVE'li
   `Microsoft.OpenApi` 2.0.0 çeker), **L35** (reddedilen iş: OpenAPI paketi
   `AgentPrism.AspNetCore` bağımlılığı **yapılmadı** — bu faz o kararı
   **değiştirmez**), **K-007** (geçişli sabitleme kapalı; CVE'li sürüm tek
   bilinçli `PackageReference` ile zorlanır).
3. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (**ana kaynak** —
   uç kayıt deseni, `MapGroup`, uç üstverisi),
   [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md) (paket
   bağımlılığı ve CVE denetimi)
4. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — HTTP yüzeyi bölümü

---

## Amaç

Kendi arayüzünü yazmak veya AgentPrism'i bir dağıtım hattına bağlamak isteyen
tüketici, uçları bugün **elle** okumak zorundadır. Belge üretiliyor ama
kullanılabilir değil: 121 ucun tamamı tek bir `AgentPrism` etiketi altında
duruyor ve **on bir uç hiçbir yanıt şeması bildirmiyor** — bunlardan biri
agent'ı çalıştıran uçtur.

Bu faz belgeyi *üretilebilir* olmaktan çıkarıp **kullanılabilir** yapar.

- **F-63** — uç etiketlerinin alan bazında ayrılması, yanıt şeması üretmeyen
  on bir ucun kapatılması ve belgenin yayımlanabilir bir artefakt hâline
  getirilmesi.

### Bugün ne çalışmıyor — doğrulanmış kanıt

Aday listesi "uçların üstverisi zaten var" diyordu. **Yarısı doğru.** Ölçüm:

| Ölçüm | Değer | Sonuç |
|---|---|---|
| `Map*` çağrısı | **121** | Toplam uç sayısı |
| `WithName` | **121** | ✅ Tam kapsama — `operationId` her uçta var |
| `WithSummary` | **119** | ✅ Neredeyse tam — iki uç eksik |
| `WithTags` | **5** | 🚨 Beşi de **grup düzeyinde** ve hepsi aynı etiketi yazıyor: `"AgentPrism"` |
| `.Produces` | **0** | Hiç kullanılmıyor — ama gerek de yok, [40.2](#402--yanıt-şeması-tipli-sonuçlardan-gelir) |
| Tipli sonuç imzası (`Task<Ok<…>>`, `Task<Results<…>>`) | **103** | ✅ Yanıt şeması bunlardan **kendiliğinden** çıkar |
| Ham `Task<IResult>` imzası | **11** | 🚨 **Bu on bir uç hiçbir yanıt şeması üretmez** |

Etiketlerin tamamı beş `MapGroup` çağrısında yazılıyor:

| Kanıt | Gözlem |
|---|---|
| [`AgentPrismEndpointRouteBuilderExtensions.cs:83,87,183,224,248`](../src/AgentPrism.AspNetCore/AgentPrismEndpointRouteBuilderExtensions.cs) | Beş grup, beşinde de `.WithTags("AgentPrism")` — 121 uç tek bir kova |
| [`OpenApiDocumentTests.cs:47`](../tests/AgentPrism.AspNetCore.FunctionalTests/OpenApiDocumentTests.cs) | Test bunu doğruluyor: `meta.GetProperty("tags")[0].GetString().ShouldBe("AgentPrism")` |

Yanıt şeması üretmeyen on bir uç:

| Dosya:satır | Uç |
|---|---|
| [`AgentEndpoints.cs:293`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | 🚨 `POST /api/agents/{name}/run` — **ürünün ana ucu** |
| [`WorkflowEndpoints.cs:212,267,320`](../src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs) | workflow `run` · `resume` · `respond` |
| [`AttachmentEndpoints.cs:112`](../src/AgentPrism.AspNetCore/Endpoints/AttachmentEndpoints.cs) | ek indirme |
| [`OpenAIChatCompletionsEndpoints.cs:53`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs) | `POST /v1/chat/completions` |
| [`OpenAIResponsesEndpoints.cs:75`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs) | `POST /v1/responses` |
| [`OpenAIConversationsEndpoints.cs:71,112,135,158`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIConversationsEndpoints.cs) | conversations dört uç |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

**Bu on bir ucun ortak yanı, hepsinin koşullu yanıt üretmesidir**: akışlı mı
değil mi, kabul edildi mi reddedildi mi, ek var mı yok mu. `Task<IResult>` bu
yüzden seçilmişti ve seçim o zaman doğruydu. Bedeli belgede görünüyor.

---

## 40.1 — K-039 korunur — bu fazın ana kısıtı

🚨 **Bu faz `Microsoft.AspNetCore.OpenApi` paketini `AgentPrism.AspNetCore`'a
bağımlılık yapmaz.** K-039 ve reddedilen iş L35 bunu iki ayrı gerekçeyle
kapatmıştır:

1. Bir kütüphane tüketicinin bağımlılık grafiğine OpenAPI üretimi dayatmamalıdır
   (ölçüldü: nuspec 3 doğrudan bağımlılık taşıyor).
2. `Microsoft.AspNetCore.OpenApi` 10.0.10, CVE'li `Microsoft.OpenApi` 2.0.0
   çekiyor (NU1903, GHSA-v5pm-xwqc-g5wc).

Bu faz **hiçbirini değiştirmez.** Model aynı kalır:

```mermaid
flowchart LR
    A["AgentPrism.AspNetCore<br/>uc ustverisi tasir"] --> B["paylasilan cerceve<br/>WithName - WithTags<br/>WithSummary - Produces"]
    C["Tuketici uygulamasi<br/>AddOpenApi cagirir"] --> D["belge kendiliginden olusur"]
    B --> D
    E["Microsoft.AspNetCore.OpenApi"] -.->|"YALNIZ tuketicide<br/>ve ornek uygulamada"| C

    classDef yasak fill:#7a2f2f,stroke:#3d1717,color:#ffffff
    class E yasak
```

Kullanılan her API — `WithName`, `WithTags`, `WithSummary`, `WithDescription`,
`Produces`, `ProducesProblem`, `Accepts` — **paylaşılan çerçevededir**
(`Microsoft.AspNetCore.App`). Yeni `PackageReference` yoktur.

> Uygulayan oturum bunu bir varsayım olarak almasın: kullanacağı her yeni
> uzantı metodunun paylaşılan çerçevede olduğunu `dotnet build` ile **kanıtlar**.
> Çerçevede olmayan bir metot bulunursa o metot kullanılmaz.

## 40.2 — Yanıt şeması tipli sonuçlardan gelir

103 uç `Task<Ok<T>>` veya `Task<Results<Ok<T>, ProblemHttpResult>>` döndürüyor.
ASP.NET Core bu tiplerden yanıt şemasını **kendiliğinden** çıkarır; `.Produces`
çağrısı gereksizdir. Bu yüzden `.Produces` sayısının sıfır olması bir eksiklik
**değildir**.

Eksiklik yalnız `Task<IResult>` dönen on bir uçtadır. İki yol vardır:

| Yol | Nasıl | Değerlendirme |
|---|---|---|
| **A** — imzayı `Results<…>` yap | Handler'ın dönüş tipi tüm olası sonuçları sayar | Derleyici zorlar, üstveri kendiliğinden doğru. Ama koşullu yollarda imza uzar ve akışlı yanıt `Results<…>` içinde ifade edilemez |
| **B** — `.Produces<T>(status)` ekle | Uç kaydına elle üstveri yazılır | Akışlı ve ikili (`binary`) yanıtları ifade edebilir. Ama imzadan **kopar**; kod değişir, üstveri bayatlar |

**Seçim: uca göre.** Kural basittir:

- Dönüş kümesi **sonlu ve tipli** ise → **A**. Derleyici bakımı üstlenir.
- Yanıt **akışlı** (SSE) veya **ikili** ise → **B**. `Results<…>` bunu ifade
  edemez; `.Produces` ile `text/event-stream` veya `application/octet-stream`
  yazılır.

Ölçüm gerektiren nokta: on bir ucun hangisi hangi kovaya düşüyor. Uygulayan
oturum her handler'ın gövdesini okur ve tabloyu kapanışa yazar.

> 🚨 `POST /api/agents/{name}/run` **hem** akışlı **hem** akışsız yanıt
> üretebilir. Bu uç ikisini birden bildirmelidir: akışsız yol için tipli yanıt,
> akışlı yol için `text/event-stream`. Tek birini yazmak, üretilen istemcinin
> diğerini görmemesine yol açar.

## 40.3 — Etiketlerin alan bazında ayrılması

Bugün 121 uç tek bir `AgentPrism` etiketi taşıyor. Bir istemci üreteci bundan
**tek bir sınıf** üretir: 121 metotlu bir `AgentPrismClient`. Belgeyi okuyan
insan için de tek bir 121 satırlık liste çıkar.

Etiketler dosya sınırlarına göre bölünür — `Endpoints/` klasörü zaten doğru
ayrımı yapmış durumda:

| Etiket | Kaynak dosya |
|---|---|
| `Agents` | `AgentEndpoints.cs`, `CatalogEndpoints.cs` |
| `Runs` | `RunEndpoints.cs`, `ObservabilityEndpoints.cs` |
| `Sessions` | `SessionEndpoints.cs` |
| `Evals` | `EvalEndpoints.cs` |
| `Experiments` | `ExperimentEndpoints.cs` |
| `Workflows` | `WorkflowEndpoints.cs` |
| `Scheduling` | `SchedulingEndpoints.cs` |
| `Governance` | `GovernanceEndpoints.cs`, `AuditEndpoints.cs`, `QuotaEndpoints.cs`, `SkillScriptGrantEndpoints.cs` |
| `Skills` | `SkillEndpoints.cs` |
| `Models` | `ModelHealthEndpoints.cs` |
| `Retention` | `RetentionEndpoints.cs` |
| `Webhooks` | `WebhookEndpoints.cs` |
| `Attachments` | `AttachmentEndpoints.cs` |
| `Voice` | `VoiceEndpoints.cs` |
| `Meta` | `MetaEndpoints.cs` |
| `OpenAI` | `OpenAICompat/*` |

🚨 **`AgentPrism` etiketi kaldırılmaz, korunur.** İki gerekçe:

1. [`OpenApiDocumentTests.cs:47`](../tests/AgentPrism.AspNetCore.FunctionalTests/OpenApiDocumentTests.cs)
   `tags[0] == "AgentPrism"` iddiasını taşıyor. Bu iddia bir sözleşmedir:
   tüketici kendi uçlarıyla karışan bir belgede AgentPrism uçlarını bu etiketle
   ayırıyor olabilir.
2. Etiket **çoğuldur**. Her uç iki etiket taşır: `AgentPrism` (kapsayıcı) ve
   alan etiketi. Grup düzeyindeki `WithTags("AgentPrism")` kalır; uç düzeyinde
   ikinci bir etiket eklenir.

## 40.4 — Yayımlanan artefakt

Belge bugün yalnız **çalışan bir uygulamada** üretilebiliyor. Bir istemci
üreteci veya bir dağıtım hattı, uygulamayı ayağa kaldırmadan belgeye
erişemiyor.

Depoya işlenmiş bir `openapi.json` üretilir ve **bir test onu güncel tutar**:

```mermaid
flowchart TD
    A["OpenApiDocumentTests<br/>belgeyi calisan host'tan alir"] --> B{"docs/openapi/agentprism.json<br/>ile ayni mi"}
    B -->|evet| C["test gecer"]
    B -->|hayir| D["test DUSER<br/>mesaj: dosyayi yenile"]
    D --> E["gelistirici komutu calistirir<br/>dosya guncellenir"]
```

Bu desen, dokümanın koddan sapmasını **derleme kapısına** çevirir —
`AGENTS.md`'nin "doküman ile kod çelişirse doküman yanlıştır" kuralının
otomatik hâlidir.

> Dosya `docs/openapi/` altına konur, `src/` altına değil. Paketlenen bir
> varlık değildir; bir belgedir.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

Public C# yüzeyi **büyümez.** Değişen şey uç kayıtlarının üstverisidir:

```csharp
// Ornek — mevcut kayit
builder.MapPost("/api/agents/{name}/run", RunAsync)
    .WithName("AgentPrismRunAgent")
    .WithSummary("Bir agent'i calistirir.");

// Ornek — bu fazdan sonra
builder.MapPost("/api/agents/{name}/run", RunAsync)
    .WithName("AgentPrismRunAgent")
    .WithSummary("Bir agent'i calistirir.")
    .WithTags("Agents")
    .Accepts<AgentRunRequest>("application/json")
    .Produces<AgentRunResult>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound);
```

> Yukarıdaki tip adları (`AgentRunRequest`, `AgentRunResult`) **taslaktır**.
> Uygulayan oturum `Contracts/` altındaki gerçek sözleşme tiplerini kullanır.

### HTTP `endpoint`'leri

Yeni uç **yok**. Var olan 121 ucun üstverisi zenginleşir.

### Arayüz payı

**Yok.** Arayüze dokunulmaz.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.AspNetCore/
├── AgentPrismEndpointRouteBuilderExtensions.cs   (grup etiketleri korunur)
└── Endpoints/*.cs                                (uc bazli etiket + Produces)
    OpenAICompat/*.cs                             (ayni)

docs/openapi/agentprism.json                      (YENI — yayimlanan artefakt)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── OpenApiDocumentTests.cs                       (genisletilir)
```

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `OpenApiDocumentTests` (mevcut, genişletilir) | Belge üretilir; mevcut üç test korunur |
| `OpenApiTagCoverageTests` | 🚨 **Her ucun en az iki etiketi vardır**: `AgentPrism` + bir alan etiketi. Etiketsiz uç kalırsa test düşer |
| `OpenApiResponseSchemaTests` | 🚨 On bir ucun **hepsi** en az bir yanıt şeması bildirir. `POST /api/agents/{name}/run` hem tipli hem `text/event-stream` yanıtı bildirir |
| `OpenApiOperationIdTests` | 121 `operationId` **benzersizdir** — istemci üreteci çakışmada metot ezer |
| `OpenApiSnapshotTests` | `docs/openapi/agentprism.json` çalışan host'un ürettiği belgeyle aynıdır; farklıysa yenileme komutunu yazan bir mesajla düşer |
| `OpenApiDependencyTests` | 🚨 `AgentPrism.AspNetCore.csproj` `Microsoft.AspNetCore.OpenApi` **taşımaz** (K-039 / L35 koruması) |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | On bir uç için A (tipli `Results<…>`) mı B (`.Produces`) mi? | A: imza · B: üstveri · C: uca göre | **C.** Sonlu ve tipli dönüş kümesinde A (derleyici bakımı üstlenir); akışlı ve ikili yanıtta B (`Results<…>` bunu ifade edemez). Karar tablosu kapanışa yazılır |
| 2 | Anlık görüntü (`snapshot`) dosyası nasıl yenilenir? | A: bir ortam değişkeniyle testin kendisi yazar · B: ayrı bir script | **A.** Ayrı script bayatlar; testin kendisi yazarsa yenileme komutu tektir ve düşen testin mesajında durur |
| 3 | Belge sürümlenmeli mi (`v1`)? | A: tek belge, sürümsüz · B: sürümlü | **A.** HTTP sözleşmesi bugün sürümlü değil; belgeyi sürümlemek olmayan bir sözleşmeyi taklit eder. Faz 7'de yeniden değerlendirilir |
| 4 | `/v1/*` OpenAI uyumlu uçları belgeye girsin mi? | A: evet, `OpenAI` etiketiyle · B: hayır | **A.** Mevcut test bu uçların belgede olduğunu zaten doğruluyor; çıkarmak bir geriye gidiştir |
| 5 | İki `WithSummary`'si eksik uç hangileri? | A: **ölçülmeli** ve tamamlanmalı | **A.** 121 − 119 = 2. Uygulayan oturum bulur ve doldurur |

---

## Bitiş Ölçütleri (DoD)

- [ ] 🚨 `AgentPrism.AspNetCore.csproj` **hâlâ** `Microsoft.AspNetCore.OpenApi`
      taşımıyor (K-039 ve L35 korundu)
- [ ] Belgedeki **her** ucun en az iki etiketi var: `AgentPrism` + alan etiketi
- [ ] On bir `Task<IResult>` ucunun **hepsi** yanıt şeması bildiriyor
- [ ] `POST /api/agents/{name}/run` hem tipli yanıtı hem `text/event-stream`
      yanıtını bildiriyor
- [ ] 121 `operationId` benzersiz
- [ ] Eksik iki `WithSummary` tamamlandı; sayı 121/121
- [ ] `docs/openapi/agentprism.json` işlendi ve anlık görüntü testi geçiyor
- [ ] Belge bir istemci üretecinden geçirildi ve **hata vermeden** derlenen bir
      çıktı üretti; kullanılan komut ve sonuç bu belgeye yazıldı
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [ ] `secret` taraması boş döndü

### Doğrulama komutları

```bash
# Bagimlilik korumasi — cikti BOS olmali
grep -n "Microsoft.AspNetCore.OpenApi\|Microsoft.OpenApi" \
  src/AgentPrism.AspNetCore/AgentPrism.AspNetCore.csproj

# Ornek uygulamayi calistir, belgeyi al
curl -s http://localhost:5081/openapi/v1.json > /tmp/apidoc.json

# Etiketsiz (tek etiketli) uc kalmis mi — cikti BOS olmali
jq -r '.paths | to_entries[] | .key as $p | .value | to_entries[]
       | select((.value.tags // []) | length < 2)
       | "\($p) \(.key)"' /tmp/apidoc.json

# Yanit semasi olmayan uc kalmis mi — cikti BOS olmali
jq -r '.paths | to_entries[] | .key as $p | .value | to_entries[]
       | select((.value.responses // {}) | length == 0)
       | "\($p) \(.key)"' /tmp/apidoc.json

# operationId benzersizligi — cikti BOS olmali
jq -r '[.paths[][]?.operationId] | group_by(.) | map(select(length>1)) | .[][0]' \
  /tmp/apidoc.json

# Akisli yanit bildirilmis mi
jq '.paths["/agentprism/api/agents/{name}/run"].post.responses' /tmp/apidoc.json
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 K-039 ve L35 sessizce ihlal edilir; OpenAPI paketi bağımlılığa girer | `OpenApiDependencyTests` bunu bir testle kapatır; DoD'de ayrıca `grep` ile doğrulanır |
| Kullanılacak bir uzantı metodu paylaşılan çerçevede olmayabilir | Her metot `dotnet build` ile kanıtlanır; olmayan metot kullanılmaz |
| `.Produces` üstverisi imzadan kopar ve bayatlar | Sonlu dönüş kümesinde tipli `Results<…>` tercih edilir; `.Produces` yalnız akışlı ve ikili yanıtta kullanılır |
| Mevcut `tags[0] == "AgentPrism"` iddiası kırılır | `AgentPrism` etiketi grup düzeyinde **korunur**; alan etiketi ikinci olarak eklenir |
| Anlık görüntü dosyası her küçük değişimde düşer ve gürültü üretir | Bu **istenen** davranıştır: belge koddan sapamaz. Yenileme tek komuttur (Açık Soru 2) |
| Üretilen istemci akışlı ucu göremez | `POST /api/agents/{name}/run` iki yanıt tipini birden bildirir; test bunu doğrular |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.
> **Not:** On bir ucun her biri için "A mı B mi" kararı ve gerekçesi burada
> tablo hâlinde kayda geçer.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
>
> **Not:** Aday listesindeki **F-50** (tipli yönetim istemcisi ve CLI) bu
> belgeyi üretim kaynağı olarak kullanır. Devir notu, belgenin istemci
> üretimine hazır olup olmadığını ve hangi uçların hâlâ elle sarmalanması
> gerektiğini yazmalıdır. **TypeScript/npm paketi bu fazın kapsamı dışındadır**
> ve ayrı bir aday kalemi olarak durur.
