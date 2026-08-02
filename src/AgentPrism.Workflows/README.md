# AgentPrism.Workflows

AgentPrism icin workflow yurutme motoru. Katalogdaki agent'lari hazir desenlerle
birbirine baglar, her yurutmeyi `runs` tablosuna kaydeder ve kontrol
noktalarindan surdurulebilir kilar.

## Kurulum

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString)
       .UseWorkflows();
```

## Arayuzden tanimlanan workflow

Bir workflow tanimi bir **graftir**, kod degildir: yalnizca katalogdaki
agent'larin adlarini ve bir deseni tasir.

| Desen | Ne yapar |
|-------|----------|
| `Sequential` | Agent'lar sirayla calisir; her cikti sonrakinin girdisidir |
| `Concurrent` | Agent'lar ayni anda calisir; sonuclar birlestirilir |
| `Handoff` | Ilk agent isi baslatir, gerektiginde digerlerine devreder |
| `GroupChat` | Round-robin bir yonetici sirayi dagitir |
| `Magentic` | Yonetici agent plan kurar, ilerlemeyi izler, yeniden planlar |

```http
PUT /agentprism/api/workflows/inceleme
Content-Type: application/json

{
  "kind": "Sequential",
  "agentNames": ["arastirmaci", "yazar", "editor"]
}
```

## Kodda tanimlanan workflow

Serbest graf — ozel `Executor` tipleri, kosullu kenarlar, alt workflow'lar —
yalnizca kodda tanimlanir:

```csharp
builder.AddWorkflow("ozel-graf", services =>
{
    var start = ExecutorBindingExtensions.BindAsExecutor<string, string>(
        input => input.ToUpperInvariant(), id: "buyut");

    return new WorkflowBuilder(start).WithName("ozel-graf").Build();
});
```

## Calistirma

```http
POST /agentprism/api/workflows/inceleme/run
{ "message": "Q3 raporunu incele" }
```

Yanit SSE'dir. Her cerceve bir `RunEvent` tasir; ilk cerceve calistirma
kimligini bildirir. Workflow icinde cagrilan her agent kendi `runs` satirini
acar ve workflow satirinin altina baglanir — `GET /api/runs/{id}/tree` agacin
tamamini dondurur.

## Kontrol noktalari

Her super-step'te bir kontrol noktasi yazilir. Yarim kalan bir yurutme
ortasindan devam ettirilebilir:

```http
GET  /agentprism/api/workflows/runs/{runId}/checkpoints
POST /agentprism/api/workflows/runs/{runId}/resume
```

Kalici sürdürme `UsePostgreSql()` gerektirir; bellek ici kurulumda kontrol
noktalari surec omruyle sinirlidir.

## Sinirlar

| Ayar | Varsayilan | Ne yapar |
|------|-----------|----------|
| `MaxConcurrentRuns` | 4 | Ayni anda calisan workflow sayisi |
| `RunTimeout` | 10 dk | Tek bir calistirmanin en fazla suresi |
| `MaxSuperSteps` | 100 | Sonsuz dongu korumasi |
| `EnableCheckpointing` | `true` | Kontrol noktasi yazimi |

Bu paket **AOT uyumlu degildir**: yurutme motoru yansima kullanir. Diger
AgentPrism paketleri etkilenmez.

## Lisans

MIT
