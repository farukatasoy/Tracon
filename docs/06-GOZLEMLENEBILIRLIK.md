# Faz 6 — Gözlemlenebilirlik, Workflows ve Çok Kiracılılık

> **Durum:** 🔜 Sıradaki
> **Önkoşul:** [05-AGENTPRISM-UI.md](05-AGENTPRISM-UI.md) — tamamlandı
> **Sonraki:** [07-SAGLAMLASTIRMA-VE-YAYIN.md](07-SAGLAMLASTIRMA-VE-YAYIN.md)
> **Paketler:** `AgentPrism.Core`, `AgentPrism.PostgreSql`, `AgentPrism.AspNetCore`, `AgentPrism.UI`

---

## Bu Faza Başlarken

Önce şunları bu sırayla okuyun:

1. [`MIMARI.md`](MIMARI.md) — bölüm 4 (MAF genişleme noktaları), bölüm 5 (veri modeli), bölüm 6 (çalıştırma yolu)
2. [`KARARLAR.md`](KARARLAR.md) — kapatılmış tartışmaları yeniden açmayın; özellikle K-032 (model kataloğu), K-041 (özet depoda hesaplanır)
3. [`05-AGENTPRISM-UI.md`](05-AGENTPRISM-UI.md) — "Gerçekleşen Public API", "Plandan Sapmalar" ve "Faz 6'ya Devreden Notlar"
4. [`../MEMORY.md`](../MEMORY.md) — önceki oturumların keşfettiği tuzaklar
5. Bu doküman

Skill'ler: `.agents/skills/maf-api-kesfi/` (MAF imzalarını doğrulama),
`.agents/skills/faz-tamamlama/` (faz sonu protokolü).

Çalışan bir arka uç ve arayüz:

```bash
cd samples/AgentPrism.Api && dotnet run
# http://localhost:5080/agentprism
```

---

## Devraldığınız Sözleşmeler

Bu imzalar **tamamlandı ve testli**. Faz 6 bunları değiştirmez, kullanır.

```csharp
// AgentPrism.Abstractions — Faz 5
public sealed class AgentPrismRunOptions : AgentRunOptions
{
    public Guid? RunId { get; init; }
    public override AgentRunOptions Clone();   // yeni alan eklerseniz BURAYI da guncelleyin
}

// AgentPrism.AspNetCore — Faz 5; arayuz paketi bunu uygular
public interface IAgentPrismUiProvider
{
    bool HasAssets { get; }
    ValueTask<bool> TryServeAsync(HttpContext context, string basePath, string relativePath);
}

// AgentPrism.UI — Faz 5
public static IAgentPrismBuilder UseUI(this IAgentPrismBuilder builder);

// AgentPrism.Abstractions — Faz 4
IRunStore.GetStatisticsAsync(RunStatisticsQuery, CancellationToken) -> RunStatistics
```

### Davranış sözleşmeleri (mevcut testlerin zorladığı kurallar)

| Kural | Nerede doğrulanıyor |
|-------|--------------------|
| `/api/agents/{name}/run` akışının **ilk** çerçevesi `run`'dır ve `runId` taşır | `Calistirma_olaylari_ekranda_adim_adim_gorunur` |
| Arayüz kabuğu bearer token'sız açılır; veri uçları açılmaz | `Token_gerektiginde_kabuk_acilir_ve_token_sorulur` |
| Arayüz herhangi bir prefix altında çalışır, hiçbir istek `>=400` dönmez | `Farkli_onek_altinda_varliklar_yuklenir` |
| Kodda tanımlı agent arayüzden düzenlenemez | `Kod_agenti_listede_gorunur_ve_duzenlenemez` |
| Tool kartı argüman ve sonucu gösterir | `Playground_akisi_gelir_ve_tool_karti_dolar` |
| Tema tercihi yeniden yüklemede korunur | `Koyu_tema_gecisi_calisir_ve_kalici_olur` |
| gzip JavaScript < 250 KB | `frontend/scripts/postbuild.mjs` — `npm run build` içinde |
| SSE çerçeveleme parçalı gövdede ve CRLF'te bozulmaz | `sse.test.ts` |
| Olay birleştirme `$type` ayracı değişse de çalışır | `transcript.test.ts` |

---

## 🚨 Bilinen Tuzaklar

**1. `tool_invocations` ve `traces`/`spans` tabloları BOŞ.** Şema Faz 2'de kuruldu,
yazan yok. Arayüzdeki Tools ve Models ekranları bu yüzden "gözlemlenebilirlik fazında
gelir" notu taşıyor (sapma S5). Bu fazda doldurulacaklar; notlar gerçek veriyle
değiştirilmelidir.

**2. `runs` tablosu model adı taşımaz.** Maliyet raporlaması bunu gerektirir ve yeni
bir migration ister. `/api/stats` ve Settings ekranı bugün maliyeti bilerek göstermiyor.

**3. `duration_ms` korelasyon ister.** `ToolInvoking`/`ToolInvoked` olay çiftini
eşleştirmek tek yazıcılı `RunEventWriter` tasarımına ek durum sokar. Karar defteri
bunu Faz 6'ya bu gerekçeyle erteledi.

**4. Bundle bütçesinde 162 KB boşluk var** (88,1 / 250 KB gzip). Waterfall görüntüleyici
ve grafikler bu boşluğa sığmalıdır. Kapı `postbuild.mjs` içindedir ve `dotnet build`'i kırar.

**5. Frontend derlemesi dış (outer) MSBuild derlemesindedir** (K-050). Yeni bir npm
adımı eklerken zinciri `AgentPrismCollectFrontendAssets`'in `DependsOnTargets`
listesine ekleyin — hedeflerin kendi üzerine kurmayın, MSBuild `Condition`'ı
`DependsOnTargets`'tan önce değerlendirir.

**6. Arayüz kiracı seçmiyor.** `/api/meta` kiracı bilgisi döndürmez (kimlik doğrulaması
olmayan bir uçtur). Çok kiracılılık geldiğinde arayüze bir kiracı seçici ve **korumalı**
bir kiracı listesi ucu gerekir.

**7. `dotnet format`, `dotnet build`'den fazlasını yakalar.** Dört kapıyı da çalıştırın.

**8. `arastirmaci` (Harness + tool) örneği tool çağrısında kırılıyor** (K-053).
Kanıtlandı: Playground'dan `arastirmaci` çalıştırılınca model `tool_calls` ile bitiyor
ama `Microsoft.Agents.AI.Harness` fonksiyonu hiç çağırmıyor; akış `done` olmadan
kesiliyor ve sonraki turda OpenAI `HTTP 400` ile geçmişi reddediyor. Aynı senaryo düz
`ChatClientAgent` (`support`) ile temiz çalışıyor — hata Harness paketinin onay-bağlama
zincirinde, AgentPrism kodunda değil. **Bu fazda `ToolInvoking`/`ToolInvoked` izleme
senaryolarını `arastirmaci` üzerinden test etmeyin** — tool hiç çalışmadığı için olay
çifti hiç üretilmez ve gözlemlenebilirlik kodunun kendisi sağlıklı görünüp aslında hiç
tetiklenmemiş olabilir. `support` agent'ını kullanın.

---

## Amaç

Üretim işletimi için gereken görünürlüğü ve ileri MAF yeteneklerini eklemek. Faz 5 sonunda AgentPrism çalışır; bu faz sonunda **işletilebilir** olur.

---

## 6.1 OpenTelemetry

### Toplama

MAF, `IChatClient` boru hattındaki `UseOpenTelemetry()` ile GenAI semantic convention span'leri üretir. Biz bu akışa **ek dinleyici** olarak takılırız.

**Akışı ele geçirmeyiz.** Tüketici kendi OTLP exporter'ını kullanmaya devam eder. AgentPrism yalnızca kendi `ActivitySource`'unu ekler ve ilgilendiği span'leri `agentprism.spans` tablosuna yazar.

```csharp
public static class AgentPrismDiagnostics
{
    public const string ActivitySourceName = "AgentPrism";
    public const string MeterName = "AgentPrism";
}
```

### Kalıcılık

`traces` ve `spans` tabloları Faz 2'de kuruldu (boş), bu fazda doldurulur. Aynı şey `tool_invocations` için de geçerlidir: şema hazır, yazan yok — `duration_ms` alanı `ToolInvoking`/`ToolInvoked` olay çiftinin korelasyonunu gerektirir.

Yazma yolu **örneklenir** (sampling). Varsayılan: hatalı çalıştırmaların %100'ü, başarılıların yapılandırılabilir bir oranı. Gerekçe: her span'i yazmak yüksek hacimde veritabanını darboğaza sokar.

### Metrikler

`Meter` üzerinden:

| Metrik | Tip | Etiketler |
|--------|-----|-----------|
| `agentprism.runs` | Counter | agent, status, tenant |
| `agentprism.run.duration` | Histogram | agent, status |
| `agentprism.tokens` | Counter | agent, model, direction |
| `agentprism.tool.invocations` | Counter | tool, status |
| `agentprism.tool.duration` | Histogram | tool |

### Arayüz

Runs ekranına trace görüntüleyici (waterfall) eklenir. Her span: ad, süre, öznitelikler, hata. Zaman çizelgesi çalıştırma olaylarıyla hizalanır.

---

## 6.2 Workflows

`Microsoft.Agents.AI.Workflows` (GA, 1.16.0) entegrasyonu.

| Bileşen | İş |
|---------|-----|
| `WorkflowCatalog` köprüsü | MAF'ın `WorkflowCatalog` yapısı `IAgentCatalog` yanında görünür |
| Checkpoint kalıcılığı | Workflow checkpoint'leri PostgreSQL'de; `previous_response_id` eşlemesi kiracı doğrulaması ile |
| Graf görselleştirme | Arayüzde düğüm/kenar diyagramı; çalışan düğüm vurgulanır |
| Human-in-the-loop | Bekleyen onay ekranı; onay/ret arayüzden |

**Önemli:** MAF dokümanı `previous_response_id` ve `conversation_id` için açık uyarı verir — checkpoint yüklemeden önce kiracı sahipliği doğrulanır.

---

## 6.3 MCP Tool Desteği

`Microsoft.Agents.AI.Mcp` ile uzak MCP sunucularının tool olarak bağlanması.

- Sunucu tanımları veritabanında (`mcp_servers` tablosu, bu fazda eklenir)
- Tool'lar bağlantı anında keşfedilir ve `IToolRegistry`'ye kayıtlı tool'ların yanında listelenir
- MCP tool'ları **ayrı bir rozet** ile gösterilir — kaynağı kod değil, uzak sunucudur

### Güvenlik sınırı

MCP sunucusu eklemek, dışarıdan gelen tool tanımlarını kabul etmek demektir. Bu, tasarım kuralı K2'nin ("tool'lar yalnız kodda") bilinçli bir istisnasıdır ve şu korumalarla gelir:

- MCP sunucusu ekleme yetkisi ayrı bir authorization policy gerektirir
- Her MCP tool çağrısı `tool_invocations` tablosuna kaynak sunucu bilgisiyle yazılır
- Varsayılan olarak MCP tool'ları **onay gerektirir** (auto-approval kapalı)

---

## 6.4 Çok Kiracılılık

MAF'ın hazır yapıları kullanılır:

```
SessionIsolationKeyProvider          → kiracı anahtarını üretir
IsolationKeyScopedAgentSessionStore  → oturum deposunu kiracıya kapar
```

Bunun üzerine:

- Her sorguya `tenant_id` filtresi
- `ITenantContext` — istek başına kiracıyı çözer (header, claim veya alt alan adı)
- Tek kiracılı kurulumda sabit varsayılan kiracı; hiçbir ek yapılandırma gerekmez

**Doğrulama:** `TenantIsolationTests` (Faz 2'de yazıldı) bu fazda genişletilir — her API ucu için kiracı sızıntısı testi.

---

## 6.5 Tool Onayı

`tool_invocations` tablosuna bekleyen onay durumu eklenir.

Akış:

```mermaid
flowchart TD
    A["tool.invoking"] --> B{"onay gerekli mi?"}
    B -->|hayır| RUN["çalıştır"]
    B -->|evet| W["beklet · arayüzde göster"]
    W -->|onayla| RUN
    W -->|reddet| ERR["tool hatası döndür"]
    W -->|"bir daha sorma"| RULE["kural veritabanına yazılır"] --> RUN

    classDef hata fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    class ERR hata
```

"Bir daha sorma" kuralları kiracı + agent + tool üçlüsüne bağlıdır ve arayüzden geri alınabilir.

---

## 6.6 Harness İleri Yetenekleri

Faz 3'te kapalı bırakılan yetenekler burada değerlendirilir:

| Yetenek | Karar |
|---------|-------|
| Shell erişimi | Varsayılan **kapalı**. Açmak açık yapılandırma + ayrı policy gerektirir. Her komut `audit_log`'a yazılır. |
| Arka plan agent'ları | Varsayılan **kapalı**. Açıldığında çalıştırma kaydı ve kaynak sınırı zorunludur. |

Gerekçe: bu iki yetenek sunucuda kod çalıştırma yüzeyi açar. Varsayılan olarak açık olmaları kabul edilemez.

---

## Bitiş Ölçütleri (DoD)

- [ ] Runs ekranında trace waterfall görünür
- [ ] Metrikler `Meter` üzerinden yayılır; Prometheus formatı opsiyonel olarak sunulur
- [ ] Workflow tanımlanır, çalıştırılır, grafı arayüzde görünür
- [ ] Workflow checkpoint'i PostgreSQL'den geri yüklenir
- [ ] MCP sunucusu eklenir, tool'ları keşfedilir ve onayla çalıştırılır
- [ ] İki kiracılı senaryoda hiçbir uçta veri sızıntısı yok
- [ ] Tool onay akışı arayüzden uçtan uca çalışır
- [ ] Span yazma yolu örnekleme ile sınırlı; yük testinde darboğaz oluşturmuyor

---

## Riskler

| Risk | Önlem |
|------|-------|
| Span yazma yolu veritabanını darboğaza sokar | Örnekleme + toplu yazma + `run_events` partition'ı bu fazda açılır |
| MCP sunucuları güvenilmez tool tanımı gönderir | Ayrı policy, varsayılan onay zorunluluğu, denetim izi |
| Workflow checkpoint boyutu büyür | Boyut sınırı ve saklama süresi yapılandırılabilir |
| Çok kiracılılık her sorguya dokunur | Kiracı filtresi tek bir sorgu oluşturucudan geçer; unutulması derleme hatası verir |
