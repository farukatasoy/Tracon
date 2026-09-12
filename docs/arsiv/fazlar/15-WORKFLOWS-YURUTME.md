# Faz 15 — Workflows: Yürütme ve Kalıcılık

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-27** (1/2)
> **Önkoşul:** [Faz 12](12-AGENT-CAGRI-GRAFIGI.md) — `runs.parent_run_id` bu fazda yeniden kullanıldı
> **Sonraki:** [Faz 16](16-WORKFLOWS-ARAYUZ.md) — graf, arayüz, human-in-the-loop
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, **`Tracon.Workflows` (YENİ)**
> **Migration:** 0007

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/15-WORKFLOWS-YURUTME.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Bu Fazda Ne Yapıldı

`Microsoft.Agents.AI.Workflows` 1.16.0 üzerine kurulu bir workflow yürütme
motoru eklendi. Katalogdaki agent'lar beş hazır desenle birbirine bağlanır,
her yürütme bir `runs` satırıdır ve kontrol noktalarından sürdürülebilir.

Arayüz, graf görselleştirme ve human-in-the-loop **Faz 16'dadır**.

---

## 🚨 Ölçülen MAF Davranışları

Bu bölüm fazın en değerli çıktısıdır. Aşağıdakiler **reflection ve gerçek
çalıştırma ile ölçüldü**; hiçbiri MAF dokümanında yazılı değildir.

### 1. Yürütme bir `TurnToken` ister

```csharp
var run = await InProcessExecution.RunStreamingAsync(workflow, messages, cm, sessionId, ct);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));   // ← ZORUNLU
```

Token gönderilmezse graf gelen mesajları yalnızca **yutar**: ilk super-step'ten
sonra `Idle` olur, hiçbir agent konuşmaz ve hiçbir hata verilmez. Ölçüldü —
token'sız çalıştırma 6 olay üretti ve sessizce bitti; token'lı çalıştırma 40+
olay üretti ve zinciri tamamladı.

### 2. `AgentResponseEvent`, `WorkflowOutputEvent`'ten **türer**

```
WorkflowEvent
 └── WorkflowOutputEvent
      ├── AgentResponseEvent
      └── AgentResponseUpdateEvent
```

`switch` içinde genel dal önce yazılırsa agent yanıtları "workflow çıktı üretti"
diye sınıflanır ve gerçek çıktı kaybolur. `WorkflowEventMapper` bu sırayı
korur; regresyon testi `Agent_guncellemesi_cikti_DEGIL_metin_parcasi_olarak_eslenir`.

### 3. Kontrol noktası yükü polimorfiktir — `$type` gerçekten vardır

Üç agent'lı bir `Sequential` workflow'un 7.537 baytlık kontrol noktasında
`{"$type":0,"hasCondition":false,...}` ayracı 1260. bayttan itibaren bulundu.
Ayrac bulunduğu nesnenin **ilk** özelliği olmak zorundadır → sütun `json`,
`jsonb` **değil** (K-027).

Gerçek PostgreSQL'de doğrulandı:

```
},"edges":{"ozetleyici_9f45b5e64ade4bd798da24cd4eabffaa":[{"$type":0,"hasCondition":false,
```

### 4. 🚨 Executor kimlikleri **agent örneğinden** türer

Kimlik biçimi `{Name}_{AIAgent.Id}`'dir ve `AIAgent.Id` her örnek için rastgele
üretilir — **sanal değildir**, türetilmiş bir sınıf değiştiremez (reflection ile
doğrulandı: `prop String Id { get; }`).

Ölçüldü:

| Kurulum | Sürdürme |
|---------|----------|
| Aynı `Workflow` örneği | ✅ Başarılı |
| Yeniden kurulan graf, **aynı** agent örnekleri | ✅ Başarılı |
| Yeniden kurulan graf, **yeni** agent örnekleri | ❌ `InvalidDataException: The specified checkpoint is not compatible with the workflow` |

Bu yüzden `WorkflowAgentCache` eklendi: sarmalayıcı agent örnekleri
`(workflowName, agentName)` çiftine göre süreç ömrü boyunca saklanır. Graf her
çalıştırmada yeniden kurulur (executor durumu taze kalsın diye) ama agent
örnekleri sabit kalır.

**Bilinen sınır:** önbellek süreç belleğindedir. Uygulama yeniden başlatıldığında
kimlikler değişir ve eski kontrol noktaları kullanılamaz. Koşucu bu durumda
MAF'ın ham mesajı yerine ne yapılması gerektiğini söyleyen bir hata üretir.

### 5. `InProcessExecution.RunStreamingAsync` yürütmeyi **hemen** başlatır

Executor'lar `WatchStreamAsync` pompasının içinde değil, `RunStreamingAsync`
çağrısında başlatılan bir arka plan görevinde çalışır ve o görev
`ExecutionContext`'i **tam o anda** yakalar.

Sonuç: `TraconRunContext` kapsamı yalnızca `MoveNextAsync` öncesinde
yazılırsa alt agent çağrıları "çalıştırma kaydı kapalı" diyerek reddedilir ve
workflow **sessizce boş** çalışır — hiçbir agent satırı, hiçbir kontrol noktası
oluşmaz. Kapsam `StartAsync` çağrısından **önce** de yazılmalıdır. Bu, Faz
6/11/12'nin `AsyncLocal` tuzağının dördüncü hâlidir; birim testleri yakaladı.

---

## Gerçek Kanıt

`samples/Tracon.Api`, gerçek OpenAI modeli (`gpt-5.4-mini`) ve gerçek
PostgreSQL 18 ile çalıştırıldı.

### Kodda tanımlı workflow — `ozetle-ve-cevir`

```
POST /tracon/api/workflows/ozetle-ve-cevir/run
→ 122 SSE cercevesi, ilk "run", son "done"

olaylar: RunStarted 1, WorkflowStarted 1, SuperStepStarted 3,
         ExecutorInvoked 8, ExecutorCompleted 8, MessageDelta 94,
         SuperStepCompleted 3, WorkflowOutput 1, RunCompleted 1
```

`runs` ağacı — **1 workflow + 2 agent**:

```
depth=0 kind=Workflow  name=ozetle-ve-cevir  status=Completed events=124 agac=329 token
depth=1 kind=Agent     name=ozetleyici       status=Completed events= 55 token=140
depth=1 kind=Agent     name=cevirmen         status=Completed events= 37 token=189
```

Kontrol noktaları — zincirlenmiş üç nokta:

```
019fc48b1a12 parent=-
019fc48b1d83 parent=019fc48b1a12
019fc48b1d89 parent=019fc48b1d83
```

### Arayüzden tanımlı workflow — `inceleme-zinciri`

```
PUT /tracon/api/workflows/inceleme-zinciri
{"kind":"Sequential","agentNames":["ozetleyici","cevirmen"]}
→ 200, version=1, tenantId="default"

POST .../run → 101 cerceve, agac 3 satir, agac toplami 259 token
```

Çıktı (zincirin gerçekten aktığının kanıtı — özet Türkçe, çeviri İngilizce):

```
- Faz 15, workflow yürütmesini getirdi.
- Katalogdaki agentlar hazır desenlerle birbirine bağlanır.
- Her yürütme bir runs satırıdır.
Phase 15 introduced workflow execution. Agents in the catalog are connected
to each other with ready-made patterns, and each execution is a runs row.
```

### Sürdürme

```
POST /tracon/api/workflows/runs/{runId}/resume
→ YENI runId, 98 cerceve, WorkflowOutput uretildi, RunCompleted
```

Hem kodda hem arayüzden tanımlı workflow için başarılı.

### Veritabanı

```
 runs                 | kind          | smallint
 runs                 | workflow_name | text
 workflow_checkpoints | state         | json          ← jsonb DEGIL
 workflows            | definition    | jsonb
```

`$type` ayracı korunmuş:

```
},"edges":{"ozetleyici_9f45b5e64ade4bd798da24cd4eabffaa":[{"$type":0,"hasCondition":false,
```

`runs.kind` dağılımı:

```
 kind | workflow_name    | agent_name       | count
    1 | inceleme-zinciri | inceleme-zinciri |     2
    1 | ozetle-ve-cevir  | ozetle-ve-cevir  |     2
    0 |                  | cevirmen         |     3
    0 |                  | ozetleyici       |     3
```

---

## Uygulama Sırasında Yaşanan Hatalar

Bu bölüm sonraki fazın en değerli bilgisidir.

### 1. Workflow sessizce boş çalıştı

**Belirti:** 4 birim testi düştü — workflow satırı `Completed` ama sıfır alt
agent satırı, sıfır kontrol noktası.

**Kök neden:** `TraconRunContext` kapsamı yalnızca `MoveNextAsync` öncesinde
yazılıyordu. MAF yürütmeyi `RunStreamingAsync` çağrısında başlatan bir arka plan
görevine devrediyor ve o görev `ExecutionContext`'i tam o anda yakalıyor —
kapsam henüz boştu, `ChildAgentInvoker` tüm çağrıları reddetti.

**Çözüm:** kapsam `StartAsync`'ten önce de yazılır.

### 2. Kodda tanımlı workflow ağaca bağlanmadı

**Belirti:** Örnek uygulama gerçek modelle çalıştı, çıktı doğruydu, ama
`GET /api/runs/{id}/tree` **tek satır** döndü. Agent'lar bağımsız kök
çalıştırmalar olarak listede duruyordu.

**Kök neden:** Örnek, agent'ları `IAgentCatalog.ResolveAsync` ile **doğrudan**
alıyordu. Katalogdan gelen agent kayıt sarmalayıcısını taşır ama MAF onu
`options = null` ile çağırır; sarmalayıcı ağaç bilgisini okuyamaz ve kendi kök
satırını açar.

**Çözüm:** `GetWorkflowAgent` public API'si eklendi; regresyon testi
`Baglanan_agentler_workflow_agacina_girer`. **Yalnızca örnek uygulamayı
gerçekten çalıştırmak ortaya çıkardı** — 209 test yeşildi.

### 3. Sürdürme `InvalidDataException` verdi

**Belirti:** Kontrol noktaları yazıldı, listelendi, ama sürdürme
`The specified checkpoint is not compatible with the workflow` ile düştü.

**Kök neden:** Graf her çalıştırmada yeniden kuruluyordu ve her kurulumda yeni
`ChildAgentInvoker` örnekleri üretiliyordu → yeni `AIAgent.Id` → yeni executor
kimlikleri.

**Çözüm:** `WorkflowAgentCache`. Regresyon testi
`Ayni_tanim_iki_kez_derlenirse_EXECUTOR_KIMLIKLERI_AYNI_KALIR`.

---

## Bitiş Ölçütleri (DoD)

- [x] Kodda tanımlı bir workflow çalışıyor ve SSE ile akıyor — 122 çerçeve
- [x] Arayüzden `Sequential` bir workflow tanımlanıp çalıştırılıyor — 101 çerçeve
- [x] `runs` ağacı doğru: 1 workflow satırı + N agent satırı — üç satır ölçüldü
- [x] Checkpoint yazılıyor, listeleniyor ve **sürdürme çalışıyor** — üç zincirli nokta
- [x] Başka kiracının checkpoint'i bulunamıyor — `Baska_kiracinin_noktasi_BULUNAMAZ`
- [x] Paket ekleme kontrol listesi tamam (README, slnx, meta, `DependencyDirectionTests`)
- [x] Dört doğrulama kapısı sıfır uyarı; `dotnet pack` **9 paket** üretiyor

---

## Sonraki Faza Devir Notu

- **Graf çizimi:** `WorkflowVisualizer.ToMermaidString(workflow)` ve
  `Workflow.ReflectEdges()/ReflectExecutors()/ReflectPorts()` hazır. Bu fazda
  graf **çizilmedi**, yalnızca çalıştırıldı.
- **Human-in-the-loop:** `RequestPort` / `ExternalRequest` / `ExternalResponse`
  ve `StreamingRun.SendResponseAsync` **Faz 16'da uygulandı**. Bu fazda
  `blockOnPendingRequest: false` kullanıldı ve bekleyen istek varsa çalıştırma
  anlaşılır bir hata ile bitiyordu; Faz 16'dan beri `RunStatus.AwaitingInput`
  ile kapanır ve `/respond` ucundan sürdürülür. `RunEventType.WorkflowRequest` (18) değeri
  şimdiden ayrıldı — Faz 16 enum sırasını değiştirmek zorunda kalmaz.
  `MagenticWorkflowBuilder.RequirePlanSignoff(true)` bu yolun ilk gerçek
  tüketicisidir.
- **🚨 Kontrol noktası kimlikleri süreç ömürlüdür.** Uygulama yeniden
  başlatıldığında eski kontrol noktaları kullanılamaz.
  **→ Faz 16'da ÇÖZÜLDÜ (K-127).** Ölçüldü: grafta kimliği değişken olan tek şey
  agent executor'udur; hazır desenlerin yardımcı düğümleri zaten sabittir, yani
  grafı elle kurmak gerekmedi. `WorkflowAgentIdentity` sarmalayıcının kimliğini
  `(workflow, agent)` çiftinden türetir.
- **`Microsoft.Agents.AI.Workflows.Declarative`** — Faz 16'da doğrulandı:
  **1.16.0 sürümü vardır**, sürüm endişesi yersizdi. Yine de **alınmadı**:
  +19 geçişli paket getiriyor ve `ResponseAgentProvider` sözleşmesi OpenAI
  Responses API şekline bağlı (K-129).
- **Arayüz için hazır veri:** `GET /api/runs?includeChildren=false` workflow
  satırlarını da döndürür (`kind` alanı ayırt eder). Faz 16 bir `kind` filtresi
  eklemek isteyebilir — `RunQuery` şu an bu filtreyi **taşımıyor**.
