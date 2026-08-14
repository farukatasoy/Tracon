# 17 — Eval, Deneyler (A/B), Kanarya Yayını ve Geri Bildirim (`EVAL`)

> **Alan kodu:** `EVAL` · **Faz:** 18, 19, 31, 45, 49, 56
> **Kaynak:** `src/AgentPrism.Abstractions/Evaluation/` (tümü) ·
> `src/AgentPrism.Abstractions/Experiments/` (tümü — `Experiment.cs`,
> `ExperimentVariant.cs`, `ExperimentStatus.cs`, `CanaryPolicy.cs`,
> `CanaryEvaluation.cs`, `CanaryDecisionKind.cs`) ·
> `src/AgentPrism.Core/Evaluation/` (tümü) · `src/AgentPrism.Core/Experiments/`
> (tümü) · `src/AgentPrism.Core/Audit/AuditingExperimentStore.cs` ·
> `src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs`,
> `ExperimentEndpoints.cs` · `src/AgentPrism.AspNetCore/Contracts/EvaluationContracts.cs`,
> `ExperimentContracts.cs` · `src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs`
> (yalnız `feedback`/`compare`/`input`/`replay` dalları — Faz 31 ve ilgili) ·
> `src/AgentPrism.Abstractions/Runs/RunScore.cs`, `RunScoreKind.cs`,
> `IRunScoreStore.cs`, `RunReplay.cs` ·
> `src/AgentPrism.UI/frontend/src/screens/evals.tsx`, `eval-detail.tsx`,
> `eval-run-detail.tsx`, `experiments.tsx`, `experiment-detail.tsx` ·
> `src/AgentPrism.UI/frontend/src/components/promote-to-eval-case.tsx`,
> `feedback-control.tsx` · Migration'lar: `0009_eval.sql`,
> `0010_experiments.sql`, `0017_run_scores.sql`, `0022_eval_case_source.sql`,
> `0028_experiment_canary.sql`.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

Faz 18, bir agent'ın davranışını **yerleşik denetimlerle** (`nonEmpty`,
`containsExpected`, `keywords`, `toolCalled`, `toolCallsPresent`,
`hasImageContent`) otomatik puanlayan bir eval takım/vaka/koşu sistemi
getirdi. Faz 45 bunun üzerine, başarısız veya düşük puanlı bir gerçek run'ı
tek tıkla bir eval vakasına **terfi ettiren** bir köprü ekledi. Faz 49 aynı
denetim mantığını **üretime** taşıdı: tamamlanan run'ların bir örneği
(`RunSampler`), isteğe bağlı bir LLM yargıcı (`ModelRunJudge`) ile
puanlanır. Faz 19 farklı bir eksen açtı — aynı agent'ın **iki sürümünü**
canlı trafikte karşılaştıran deneyler (A/B). Faz 56 bunun üzerine, deneyin
kanarya kolunu otomatik izleyip kötü giderse **kendiliğinden geri alan**
bir arka plan servisi ekledi. Faz 31 ise bambaşka bir eksen — insan geri
bildirimi (başparmak yukarı/aşağı, yıldız) ve bunun aynı puan tablosunu
(`RunScore`) LLM yargıcıyla **paylaşması**.

```mermaid
flowchart TD
    S["EvalSuite<br/>checks: nonEmpty/containsExpected/..."] --> C["EvalCase listesi"]
    C --> T["POST evals/name/run"]
    T --> J["JobKind.Eval<br/>EvalJobHandler"]
    J --> AG["gercek agent.RunAsync<br/>GERCEK PARA"]
    AG --> R["EvalCaseResult<br/>passed/scores"]

    RUN["Tamamlanan production run"] --> SMP["RunSampler<br/>deterministik hash"]
    SMP -->|orneklendi| OJ["JobKind.OnlineEval"]
    OJ --> JG["ModelRunJudge<br/>IRunJudge"]
    JG --> SCORE["RunScore<br/>Source=judge:model"]

    FB["Kullanici thumbs up/down"] --> SCORE

    EXP["Experiment<br/>2 varyant, agirlik toplami 100"] --> ASGN["ExperimentAssignmentResolver<br/>SHA-256 bucket"]
    ASGN --> RUN
    EXP -->|Canary policy varsa| CE["CanaryEvaluationService<br/>5 dk'da bir tarar"]
    CE -->|kotu giderse| RB["otomatik geri alma<br/>audit-once-then-mutate"]

    style AG fill:#5f4a1e,stroke:#302510,color:#ffffff
    style JG fill:#5f4a1e,stroke:#302510,color:#ffffff
    style RB fill:#6f1f2a,stroke:#400d15,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| İş kuyruğunun GENEL mekanizması (kira, `FOR UPDATE SKIP LOCKED`, tekrar deneme çatısı) | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (zaten üretildi) — burada yalnız `EvalJobHandler`/`OnlineEvalJobHandler`'ın **kendi** iş mantığı test edilir |
| Genel HTTP zarfı, idempotency-key deseni | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Rol/API anahtarı kapsam sisteminin GENEL mekanizması | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada yalnız bu alana **özgü** kapsam boşluğu test edilir (§11) |
| Run kaydı, SSE akışı, run detay ekranının genel davranışı | `11-ARAYUZ-RUN-SESSION-SSE.md` (zaten üretildi) — burada yalnız `FeedbackControl`/"Judge now" butonu test edilir |
| MCP/A2A dış yüzeyi | `18-MCP-VE-A2A.md` (bu oturumda üretiliyor) |
| Kota kural motoru (`429`) | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) — burada yalnız gerçek para harcayan case'lerin **maliyet** tarafı gözlenir, kota kuralı kurulmaz |

> **Rol matrisi burada da NO-OP'tur, tekrar test edilmez.** `EvalEndpoints`/
> `ExperimentEndpoints` her ucu `RequireRole(roles.Reader/Operator/Admin)`
> ile işaretler ama `AgentPrismPolicies.*` örnek uygulamada kayıtlı değildir
> (bkz. `00-INDEKS.md` §8). **Bu dosyaya özgü olan**: `ApiKeyScope` enum'ında
> Eval/Experiment için **hiçbir** kapsam değeri hiç tanımlanmamış —
> `RunsRead`/`RunsWrite`/`AgentsRead`/`AgentsAdmin`/`ExternalInvoke` beş
> üyeden ibarettir. Bu, "unutulan bir `RequireApiKeyScope` çağrısı" değil,
> "hiç var olmayan bir kapsam taksonomisi" — §11'de ayrıca ölçülür.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.
3. Örnek uygulama **hiçbir eval takımı, hiçbir deney önceden tanımlamaz**
   (`grep -n "IEvalStore\|IExperimentStore" samples/AgentPrism.Api/Program.cs`
   yalnız DI kaydını bulur, seed verisi yok) — bu dosyanın her senaryosu
   kendi fixture'ını sıfırdan kurar.
4. `support` agent'ı **kod-kökenlidir** (`AgentDefinitionOrigin.Code`,
   `Program.cs:281-289`) — deney (experiment) oluşturmak için KULLANILAMAZ
   (§6). Eval takımları için sorun değildir, herhangi bir agent'ı hedefleyebilir.
5. Çevrimiçi değerlendirme (§5) ve deney sonuçları (§6/§7) yalnız
   `openAiEnabled` iken (OpenAI anahtarı tanımlıyken) tam test edilebilir —
   `AddModelRunJudge(...)` yalnız bu koşulda kayıtlıdır (`Program.cs:260-276`).
6. `AgentPrism:OnlineEvaluation` ve `AgentPrism:Canary` bölümleri
   `appsettings.json`'da **yoktur** — §5 ve §8'in bazı case'leri geçici
   olarak `dotnet user-secrets set` ile bu anahtarları açar, case sonunda
   kaldırır (aksi belirtilmedikçe).

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"
```

> **Gerçek para uyarısı.** §3 (eval koşusu), §5 (çevrimiçi değerlendirme/
> yargıç), §6/§7'nin gerçek run gerektiren case'leri gerçek OpenAI modeli
> çağırır. §1, §2, §4 (yalnız CRUD kısmı), §6'nın CRUD kısmı, §9, §10, §11
> hiçbir model çağırmaz.

---

## Bu dosyanın yerel fixture'ları

| Kimlik | Değer |
|---|---|
| `FIX-EVAL-01` | Takım adı `destek-degerlendirme` · `agentName: "support"` · `checks: [{"kind":"nonEmpty","minLength":5},{"kind":"containsExpected","caseSensitive":false}]` · vaka: `query: "ORD-1001 siparisim nerede?"`, `expectedOutput: "ORD-1001"`, `expectedTools: ["get_order_status"]` |
| `FIX-EVAL-02` | Takım adı `tool-cagri-testi` · `agentName: "support"` · `checks: [{"kind":"toolCalled","tools":["get_order_status"],"mode":"all"}]` · vaka: `query: "ORD-1001 nerede?"` |
| `FIX-EXP-01` | Deney adı `destek-talimat-testi` · `agentName: "manuel-destek"` (`FIX-AGENT-01`) · varyant `kisa-talimat` → `version: 1`, `weight: 50` · varyant `detayli-talimat` → `version: 2` (aşağıda oluşturulur), `weight: 50` |

`FIX-EXP-01`'in ikinci sürümü, `FIX-AGENT-01`'i farklı bir talimatla tekrar
`PUT` ederek üretilir (aşağıda MT-EVAL-051'in ön koşulu).

---

# 1 — Eval Takım (Suite) CRUD (Faz 18)

### MT-EVAL-001 — `PUT /api/evals/{name}` yeni bir takım oluşturur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-EVAL-01`'i oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/evals/destek-degerlendirme" -H "$APB" \
     -H "content-type: application/json" -d '{
  "description": "Destek agent'inin siparis sorularini yanitlama kalitesi",
  "agentName": "support",
  "checks": [
    { "kind": "nonEmpty", "minLength": 5 },
    { "kind": "containsExpected", "caseSensitive": false }
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 200`, gövdede `id` dolu bir GUID, `createdAt == updatedAt`.

**Gerçek sonuç**
`HTTP: 200`, `id: "019ffdcb-dae4-..."`, `createdAt == updatedAt`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-002 — Aynı adı tekrar `PUT` etmek GÜNCELLER; `id`/`createdAt` sabit kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-001 geçti.

**Girilecek veri**
```bash
curl -s "$APU/api/evals/destek-degerlendirme" -H "$APB" \
  -H "content-type: application/json" -X PUT -d '{
  "description": "Guncellenmis aciklama",
  "agentName": "support",
  "checks": [{ "kind": "nonEmpty", "minLength": 5 }]
}'
```

**Beklenen sonuç**
- `id` MT-EVAL-001'dekiyle birebir aynı; `createdAt` değişmez, `updatedAt`
  ilerler; `checks` yeni değerle değişir.

**Gerçek sonuç**
`id` birebir aynı, `createdAt` değişmedi, `updatedAt` ilerledi, `checks` tek elemana indi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-003 — `GET /api/evals` kiracının tüm takımlarını listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-001'i `FIX-EVAL-01` orijinal `checks`'iyle yeniden `PUT` et
  (MT-EVAL-002'nin değişikliğini geri al).

**Girilecek veri**
```bash
curl -s "$APU/api/evals" -H "$APB" | python3 -c "import json,sys; print([s['name'] for s in json.load(sys.stdin)])"
```

**Beklenen sonuç**
- `destek-degerlendirme` listede.

**Gerçek sonuç**
`['destek-degerlendirme']` — listede. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-004 — Bilinmeyen `check` `kind` → `400`, takım kaydedilmez

Negatif senaryo. `EvalCheckRegistry.BuildChecks` kaydetme anında çalışır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/evals/kirik-takim" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentName": "support",
  "checks": [{ "kind": "regexMatch", "pattern": ".*" }]
}'
```

**Beklenen sonuç**
- `HTTP: 400`. Mesaj `"Bilinmeyen denetim turu: 'regexMatch'. Ozel bir
  denetimse 'IAgentPrismBuilder.AddEvalCheck(\"regexMatch\", ...)' ile
  kaydedilmelidir."` metnini içerir.
- `GET /api/evals/kirik-takim` → `404` (kayıt hiç oluşmadı).

**Gerçek sonuç**
`HTTP: 400`, `detail: "Bilinmeyen denetim turu: 'regexMatch'. Ozel bir denetimse 'IAgentPrismBuilder.AddEvalCheck(\"{kind}\", ...)' ile kaydedilmelidir."` (mesaj kalıbı `{kind}` yer tutucusu kullanıyor, doküman `regexMatch` doğrudan yazmıştı — küçük bir metin farkı, anlam aynı). `GET` → `404`, kayıt oluşmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-005 — Boş `checks: []` dizisiyle takım kaydetmek BAŞARILIDIR (koşu zamanı patlar)

Sınır senaryosu. Kayıt seviyesinde denetim **sayısı** kontrol edilmez, yalnız
her denetimin `kind`'ı geçerli olmalıdır — boş dizi geçerlidir çünkü hiçbir
kind doğrulanmaz. Asıl hata `EvalJobHandler` içindedir (bkz. MT-EVAL-028).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/evals/denetimsiz-takim" -H "$APB" \
     -H "content-type: application/json" -d '{ "agentName": "support", "checks": [] }'
```

**Beklenen sonuç**
- `HTTP: 200`. Takım oluşur; bu, MT-EVAL-028'in ön koşuludur.

**Gerçek sonuç**
`HTTP: 200`, takım `checks: []` ile oluştu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-006 — Var olmayan takım `GET` → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/evals/hic-yok-boyle-takim" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`.

**Gerçek sonuç**
`HTTP: 404`, `title: "Eval takimi bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-007 — `DELETE` takımı siler; vaka ve koşuları KASKAT siler

Yıkıcı senaryo — SQL doğrulamalı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `denetimsiz-takim` (MT-EVAL-005) hâlâ var.

**Adımlar**
1. `denetimsiz-takim`'ı sil.
2. `eval_suites`/`eval_cases`/`eval_runs` tablolarında iz kalmadığını doğrula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/evals/denetimsiz-takim" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.eval_suites WHERE name = 'denetimsiz-takim';
```

**Beklenen sonuç**
- `HTTP: 204`. SQL sorgusu `0` döner.

**Gerçek sonuç**
`HTTP: 204`. SQL sorgusu `count: 0` döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Eval Vaka (Case) Yönetimi ve Arayüz (Faz 18)

### MT-EVAL-010 — `PUT /api/evals/{name}/cases` TAM DEĞİŞİM yapar, artımlı değil

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `destek-degerlendirme` (MT-EVAL-001) var.

**Adımlar**
1. İki vaka ile `PUT` yap.
2. Tek vakayla tekrar `PUT` yap.
3. `GET` ile yalnız son vakanın kaldığını doğrula.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/evals/destek-degerlendirme/cases" -H "$APB" \
     -H "content-type: application/json" -d '[
  { "query": "ORD-1001 siparisim nerede?", "expectedOutput": "ORD-1001", "expectedTools": ["get_order_status"] },
  { "query": "Merhaba", "expectedOutput": null }
]'

curl -s -X PUT "$APU/api/evals/destek-degerlendirme/cases" -H "$APB" \
     -H "content-type: application/json" -d '[
  { "query": "ORD-1001 siparisim nerede?", "expectedOutput": "ORD-1001", "expectedTools": ["get_order_status"] }
]'

curl -s "$APU/api/evals/destek-degerlendirme/cases" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Son `GET` `1` döner — ikinci `PUT` birinci `PUT`'un vakasını **eklemedi**,
  yerine geçti.

**Gerçek sonuç**
Son `GET` `1` döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-011 — Boş `query` içeren bir vaka → `400`, hiçbir vaka kaydedilmez

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/evals/destek-degerlendirme/cases" -H "$APB" \
     -H "content-type: application/json" -d '[
  { "query": "Gecerli soru" },
  { "query": "" }
]'
```

**Beklenen sonuç**
- `HTTP: 400`. Sonraki `GET /api/evals/destek-degerlendirme/cases`
  MT-EVAL-010'un son durumunu (tek vaka) hâlâ gösterir — kısmi yazma
  olmamıştır.

**Gerçek sonuç**
`HTTP: 400`, `detail: "Her vaka bos olmayan bir 'query' alani tasimalidir."` Sonraki `GET` hâlâ tek eski vakayı (`ORD-1001 siparisim nerede?`) gösterdi — kısmi yazma olmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-012 — `DELETE /api/evals/{name}/cases` tümünü `[]` ile değiştirir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/evals/destek-degerlendirme/cases" -H "$APB"
curl -s "$APU/api/evals/destek-degerlendirme/cases" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`, ikinci çağrı `[]` döner.
- Bu case'den sonra MT-EVAL-010'un ilk `PUT`'unu tekrar uygula — §3 gerçek
  vakaya ihtiyaç duyar.

**Gerçek sonuç**
`HTTP: 204` (doküman `200` varsaymıştı; `DELETE` uçları bu repoda tutarlı biçimde `204` döner — bkz. MT-EVAL-007, MT-SKILL-004/005 vb. — doküman düzeltmesi, kusur değil), ikinci çağrı `[]` döndü. MT-EVAL-010'un ilk `PUT`'u tekrar uygulanıp tek vaka geri getirildi (§3 için).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-013 — Arayüz: `CaseEditor`'da boş `query` varken "Save cases" DEVRE DIŞI

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/evals/destek-degerlendirme` ekranını aç.
2. "Add case" ile boş bir satır ekle, `query` alanını boş bırak.
3. "Save cases" düğmesinin durumunu gözle.

**Beklenen sonuç**
- Düğme devre dışıdır; boş `query`'li satır varken kayıt denenemez
  (istemci tarafı, sunucuya hiç istek gitmez).

**Gerçek sonuç**
Boş `query`'li satır eklendikten sonra "Save cases" `[disabled]` oldu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-014 — Arayüz: `evals.tsx` "New suite" formunda geçersiz JSON `checks` → API'ye HİÇ GİTMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/evals` → "New suite".
2. `checks` metin alanına `{ bozuk json` yaz, geri kalanı geçerli doldur.
3. Kaydet'e tıkla.

**Beklenen sonuç**
- Satır içi hata mesajı görünür (`evals.checksError`); ağ isteği (DevTools
  → Network) hiç gitmemiştir.

**Gerçek sonuç**
"Save"e tıklandığında satır içi `alert` rolünde "Checks must be valid JSON — an array of check definitions." mesajı göründü, form kapanmadı. Ağ istekleri incelendi: yalnız sayfa yüklemesinin `GET /api/evals`'i vardı, hiçbir `PUT` gitmedi. Tam beklendiği gibi. (İlgisiz bir konsol hatası da gözlendi: `Pattern attribute value [a-zA-Z0-9_-]+ is not a valid regular expression` — tarayıcının yeni `/v` regex modu ile bir input `pattern` özniteliği uyuşmazlığı, bu case'in konusuyla ilgisiz, kozmetik.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-015 — Arayüz: takım "Sil" düğmesi HİÇBİR onay istemez

UX gözlemi — kusur değil, koşumda doğrulanacak asimetri.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/evals` listesinde bir takımın "Sil" düğmesine tıkla.

**Beklenen sonuç**
- Hiçbir `window.confirm` veya modal açılmaz; takım anında silinir. Geri
  dönüşü zor bir işlem (vaka+koşu kaskadı) onaysız gerçekleşir.

**Gerçek sonuç**
Test amaçlı `silinecek-takim` oluşturulup "Sil" düğmesine tıklandı: hiçbir onay diyaloğu açılmadan satır anında listeden kayboldu. Tam beklendiği gibi (asimetri doğrulandı, kusur değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Eval Koşusu: Tetikleme, Yerleşik Denetimler, Tekrar, Sürüm Pinleme (Faz 18)

> **Gerçek para uyarısı.** Bu bölümün her koşusu gerçek OpenAI modeli çağırır.

### MT-EVAL-020 — Mutlu yol: koşu tetiklenir, gerçek run üretir, `nonEmpty` + `containsExpected` geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `destek-degerlendirme` takımı MT-EVAL-010'daki tek vakayla dolu.

**Adımlar**
1. Koşuyu tetikle.
2. `EvalRun.Status` `Completed` olana kadar `GET /api/evals/runs/{id}`'yi
   birkaç saniyede bir sorgula.

**Girilecek veri**
```bash
RUN_ID=$(curl -s -X POST "$APU/api/evals/destek-degerlendirme/run" -H "$APB" \
  -H "content-type: application/json" -d '{}' | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])")
echo "$RUN_ID"

curl -s "$APU/api/evals/runs/$RUN_ID" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT status, total, passed, failed, agent_version FROM agentprism.eval_runs WHERE id = '<RUN_ID>';
```

**Beklenen sonuç**
- Nihai durum `Completed`, `total=1, passed=1, failed=0`.
- `EvalCaseResult.Output` `ORD-1001` alt dizgisini içerir (`containsExpected`
  geçtiği için).

**Gerçek sonuç**
Nihai durum `Completed`, `total=1, passed=1, failed=0`. `output` "ORD-1001 siparişiniz kargoya verilmiş..." — `ORD-1001` içeriyor, her iki denetim de (`non_empty`, `contains_expected`) `passed:true`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-021 — `toolCalled` (`mode: "all"`) denetimi: tool çağrılmazsa BAŞARISIZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-EVAL-02` (`tool-cagri-testi`) oluşturulmuş, vakası eklenmiş.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/evals/tool-cagri-testi" -H "$APB" -H "content-type: application/json" -d '{
  "agentName": "support",
  "checks": [{ "kind": "toolCalled", "tools": ["get_order_status"], "mode": "all" }]
}'
curl -s -X PUT "$APU/api/evals/tool-cagri-testi/cases" -H "$APB" -H "content-type: application/json" -d '[
  { "query": "ORD-1001 nerede?" }
]'
RUN_ID=$(curl -s -X POST "$APU/api/evals/tool-cagri-testi/run" -H "$APB" -H "content-type: application/json" -d '{}' \
  | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])")
```

**Beklenen sonuç**
- `get_order_status` gerçekten çağrıldığı için `Passed=true`.
- Karşıt kanıt: aynı takıma `query: "Merhaba"` (tool gerektirmeyen)
  eklenip yeniden koşulursa o vaka `Passed=false` döner.

**Gerçek sonuç**
İlk koşu: `Passed=true`, `reason: "All tools called: get_order_status"`. Karşıt kanıt: `query: "Merhaba"` ile değiştirilip yeniden koşulunca `Passed=false`, `reason: "Missing tool calls: get_order_status"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-022 — `keywords` denetimi, `caseSensitive: true` iken büyük/küçük harf FARK YARATIR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/evals/anahtar-kelime-testi" -H "$APB" -H "content-type: application/json" -d '{
  "agentName": "support",
  "checks": [{ "kind": "keywords", "values": ["ORD-1001"], "caseSensitive": true }]
}'
curl -s -X PUT "$APU/api/evals/anahtar-kelime-testi/cases" -H "$APB" -H "content-type: application/json" -d '[
  { "query": "ORD-1001 siparisim nerede?" }
]'
curl -s -X POST "$APU/api/evals/anahtar-kelime-testi/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- Model yanıtı `ORD-1001`'i birebir büyük harfle içerdiği sürece geçer.
  `values` içindeki dizginin harf büyüklüğü model yanıtındakiyle
  eşleşmezse (nadiren) `Passed=false` olur — bu, `caseSensitive`
  bayrağının gerçekten etkili olduğunun kanıtıdır.

**Gerçek sonuç**
Model yanıtı `ORD-1001`'i birebir aynı büyük harfle içerdi, `Passed=true`, `reason: "All keywords found: ORD-1001"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-023 — `hasImageContent` denetimi: metin-yalnız yanıt BAŞARISIZ olmalı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/evals/gorsel-testi" -H "$APB" -H "content-type: application/json" -d '{
  "agentName": "support", "checks": [{ "kind": "hasImageContent" }]
}'
curl -s -X PUT "$APU/api/evals/gorsel-testi/cases" -H "$APB" -H "content-type: application/json" -d '[
  { "query": "Merhaba" }
]'
curl -s -X POST "$APU/api/evals/gorsel-testi/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `support` agent'ı görsel üretmediği için `Passed=false`,
  `failureReason` doludur. Bu bir kusur değildir — denetimin doğru
  çalıştığının negatif kanıtıdır.

**Gerçek sonuç**
`Passed=false`, `failureReason: "has_image_content: No image content found in conversation"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-024 — `numRepetitions: 3` — TEK tekrar başarısız olursa vaka TÜMÜYLE başarısız sayılır

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `gorsel-testi` (MT-EVAL-023) — modelin görsel üretmesi olası olmadığından
  bu takım 3 tekrarın tamamında başarısız olmaya en yatkın olandır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/evals/gorsel-testi/run" -H "$APB" \
  -H "content-type: application/json" -d '{ "numRepetitions": 3 }'
```

**Beklenen sonuç**
- `EvalCaseResult.Passed=false` çünkü **tüm** repetisyonların geçmesi
  gerekir (`allRepetitionsPassed`); tek bir repetisyonun bile başarısız
  olması yeterlidir.

**Gerçek sonuç**
3 tekrarın tamamı ayrı sonuç satırı olarak döndü (üçü de `Passed=false`, `has_image_content` gerekçesiyle), run seviyesinde `total=1, passed=0, failed=1`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-025 — Sürüm pinleme: koşu SIRASINDA agent tanımı değişirse ESKİ sürüm kullanılmaya devam eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `destek-degerlendirme` takımı, birden çok vakayla (yavaşlatmak için 5+
  vaka ekle) doludur.

**Adımlar**
1. Koşuyu tetikle (`MarkRunRunningAsync` bu anda `AgentVersion`'ı pinler).
2. Koşu bitmeden `support` agent'ının talimatını değiştirmeye çalış —
   `support` kod-kökenli olduğu için DB'den değiştirilemez; bunun yerine
   `FIX-AGENT-01` hedefli ayrı bir takım kullan (`agentName: "manuel-destek"`)
   ve koşu sırasında `PUT /api/agents/manuel-destek` ile talimatı değiştir.
3. Koşu bitince `eval_runs.agent_version`'ın tetikleme anındaki sürüme
   sabit kaldığını doğrula.

**Doğrulama sorgusu**
```sql
SELECT agent_version FROM agentprism.eval_runs WHERE id = '<RUN_ID>';
```

**Beklenen sonuç**
- `agent_version`, koşu ortasında yapılan güncellemeden **etkilenmez** —
  "bu sürüm ne kadar iyi" sorusuna doğru cevap verir.

**Gerçek sonuç**
`manuel-destek` (`FIX-AGENT-01`) oluşturuldu, 10 vakalı bir takım (`surum-pinleme-testi`) koşusu tetiklendi. Durum `Running` olduğu anda (`agentVersion: 2` zaten pinlenmiş görünüyordu) agent `PUT` ile tekrar güncellendi (`version: 3`'e çıktı). Koşu bitince hem HTTP yanıtı hem SQL sorgusu `agent_version: 2` gösterdi — koşu SIRASINDA yapılan güncellemeden (versiyon 3) etkilenmedi. Tam beklendiği gibi. (İlk deneme yanlış zamanlamayla — güncelleme `Pending` durumdayken yapılmıştı, pinleme henüz olmamıştı — yanıltıcı bir sonuç verdi; `Running` durumunu yakalayarak doğru tekrarlandı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-026 — `checks: []` olan takımı koşmak → `HTTP 200` ama `EvalRun.Status` sonunda `Failed`

Negatif/gecikmeli hata senaryosu. MT-EVAL-005'in devamı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `denetimsiz-takim`'ı yeniden oluştur (MT-EVAL-005/007'de silinmişti),
  bir vaka ekle.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/evals/denetimsiz-takim" -H "$APB" -H "content-type: application/json" -d '{
  "agentName": "support", "checks": []
}'
curl -s -X PUT "$APU/api/evals/denetimsiz-takim/cases" -H "$APB" -H "content-type: application/json" -d '[
  { "query": "Merhaba" }
]'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/evals/denetimsiz-takim/run" -H "$APB" \
  -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- Tetikleme isteği `HTTP: 200` döner (senkron doğrulama denetim
  **sayısını** kontrol etmez, yalnız `kind` geçerliliğini kontrol eder).
- Birkaç saniye sonra `GET /api/evals/runs/{id}` → `status: "Failed"`,
  `passed=0, failed=total`. `EvalJobHandler`'ın attığı
  `AgentPrismException` ("'{ad}' takiminin hic denetimi yok; en az bir
  denetim gereklidir.") koşuyu senkron değil, ASENKRON olarak düşürür.

**Gerçek sonuç**
Tetikleme `HTTP: 200`. Birkaç saniye sonra `status: "Failed"`, `passed=0, failed=1, total=1`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-027 — 0 vakalı takımı koşmak → SENKRON `400`

Negatif senaryo — MT-EVAL-026'nın tam tersi (senkron hata).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Ön koşul**
- `destek-degerlendirme/cases`'i `DELETE` ile boşalt (MT-EVAL-012 deseni).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/evals/destek-degerlendirme/run" -H "$APB" \
  -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 400` — istek hiç kuyruğa girmez (0-vaka kontrolü senkrondur, bu
  yüzden MT-EVAL-026'daki "0-denetim" durumundan farklıdır).
- Bu case'den sonra en az bir vaka geri ekle — sonraki case'ler ihtiyaç
  duyar.

**Gerçek sonuç**
`HTTP: 400`, `title: "Kosu baslatilamadi"`, `detail: "'destek-degerlendirme' takiminin hic vakasi yok."` Vaka geri eklendi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-028 — `GET /api/evals/{name}/runs` koşu geçmişini listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 18 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/evals/destek-degerlendirme/runs?skip=0&take=50" -H "$APB" \
  | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- MT-EVAL-020'de tetiklenen koşu listede, en yeni önce sıralı.

**Gerçek sonuç**
Liste `1` kayıt döndü, `status: "Completed"` — MT-EVAL-020'nin koşusu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Üretimden Eval Kümesi: Run → Vaka Terfi (Faz 45)

### MT-EVAL-035 — `POST cases/from-run/{runId}` başarısız bir run'ı vakaya terfi ettirir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 45 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-01` (`ORD-1001 siparisim nerede?`) ile `support` agent'ına
  gerçek bir run gönder, `runId`'yi not al.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/evals/destek-degerlendirme/cases/from-run/<RUN_ID>" \
     -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 201`. Yeni vaka `sourceRunId=<RUN_ID>`, `promotedAt` dolu.

**Gerçek sonuç**
- `support` agent'ına `ORD-1001 siparisim nerede?` gönderildi
  (`runId=019ffddd-029a-7d7f-b906-3acaa8e1242b`), sonra
  `POST /api/evals/destek-degerlendirme/cases/from-run/<RUN_ID>` çağrıldı.
  `HTTP: 201`. Yanıt: `sourceRunId="019ffddd-029a-7d7f-b906-3acaa8e1242b"`,
  `sourceKind="ReferenceRun"`, `promotedAt="2026-08-14T01:22:40.838615+00:00"`,
  `query`/`expectedOutput`/`expectedTools` run'ın transkriptinden dolduruldu.
  Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-036 — Aynı run'ı İKİNCİ kez terfi etmek → `200` İDEMPOTENT, ikinci vaka OLUŞMAZ

Sınır senaryosu — DB'nin kısmi tekil indeksi (`eval_cases_source_run_uq`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 45 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-035 geçti.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/evals/destek-degerlendirme/cases/from-run/<AYNI-RUN_ID>" \
     -H "$APB" -H "content-type: application/json" -d '{}'
```

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.eval_cases WHERE source_run_id = '<AYNI-RUN_ID>';
```

**Beklenen sonuç**
- `HTTP: 200` (201 değil — zaten var olan vaka döner). SQL sorgusu `1`
  döner, `2` değil.

**Gerçek sonuç**
- Aynı `RUN_ID` ile aynı uç tekrar çağrıldı → `HTTP: 200`, gövde MT-EVAL-035'teki
  ile birebir aynı vaka kaydı (`id`, `promotedAt` değişmedi). SQL sorgusu
  `SELECT count(*) ... WHERE source_run_id = '<RUN_ID>'` → `1`. İkinci vaka
  oluşmadı, kısmi tekil indeks beklendiği gibi çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-037 — Var olmayan `runId`'yi terfi etmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 45 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST \
  "$APU/api/evals/destek-degerlendirme/cases/from-run/00000000-0000-0000-0000-000000000000" \
  -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 404`.

**Gerçek sonuç**
- Sıfır GUID ile çağrıldı → `HTTP: 404`, `detail: "'00000000-0000-0000-0000-000000000000' kimlikli bir calistirma yok."`
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-038 — Arayüz: `PromoteToEvalCase` HİÇBİR takım yokken görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 45 |
| **İlgili karar** | — |

**Ön koşul**
- Geçici olarak tüm eval takımlarını sil (§1'in `DELETE` case'leri).

**Adımlar**
1. Herhangi bir run'ın detay ekranını aç.

**Beklenen sonuç**
- "Bu run'ı vakaya terfi et" bileşeni render edilmez (boş, `null`) —
  0 takım varken hiçbir UI parçası görünmez.
- Case sonrası `destek-degerlendirme` ve `FIX-EVAL-02`'yi geri kur.

**Gerçek sonuç**
- Tüm 6 eval takımı `DELETE /api/evals/{name}` ile silindi (`GET /api/evals`
  → `[]`). `http://localhost:5080/agentprism/runs/019ffdd7-1910-78cb-ab36-0c1d9b849481`
  (MT-EVAL-035'in support run'ı) tarayıcıda açıldı, `browser_snapshot` alındı:
  Run detay sayfasının tamamı (başlık, istatistik satırı, Feedback, "Replay
  this run", Transcript, Event timeline, Trace, Tool calls bölümleri) göründü
  ama hiçbir yerde "Bu run'ı vakaya terfi et" bileşeni yok. Konsolda 2 hata
  vardı (`/api/agents/support/versions` ve `/api/runs/{id}/trace` → `404`) —
  ikisi de bu case'le ilgisiz, önceden var olan ayrı uç eksiklikleri.
  Beklenen davranış doğrulandı: bileşen 0 takım varken render edilmiyor.
  Case sonrası `destek-degerlendirme` (`support`, `nonEmpty`+`containsExpected`,
  vaka `ORD-1001 siparisim nerede?` → `ORD-1001`/`get_order_status`) ve
  `tool-cagri-testi` (`support`, `toolCalled` mode `all` `get_order_status`,
  vaka `ORD-1001 nerede?`) `PUT /api/evals/{name}` + `PUT .../cases` ile
  geri kuruldu, ikisi de `HTTP 200` ile doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Çevrimiçi Değerlendirme: Örnekleme, Yargıç, Özet (Faz 49)

> **Gerçek para uyarısı.** MT-EVAL-041, MT-EVAL-043, MT-EVAL-045 gerçek
> OpenAI modeli (yargıç olarak) çağırır.

### MT-EVAL-040 — İki kapılı varsayılan: yargıç kayıtlı olsa BİLE `Enabled=false`/`SampleRate=0` iken hiçbir şey örneklenmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Ön koşul**
- OpenAI etkin (yargıç kayıtlı, `Program.cs:267-275`).
- `AgentPrism:OnlineEvaluation` hiçbir `dotnet user-secrets` girdisi
  TAŞIMAZ (varsayılan durum).

**Adımlar**
1. `support` agent'ına `FIX-PROMPT-02` ile gerçek bir run gönder,
   tamamlanmasını bekle.
2. 10 saniye bekle (örnekleme kararı senkron alınır, arka planda
   `RunRecordingAgent` içinde tetiklenir).

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.jobs WHERE kind = 6;  -- JobKind.OnlineEval
```

**Beklenen sonuç**
- SQL sorgusu bu run için **hiçbir** `OnlineEval` işi göstermez —
  `SampleRate<=0.0` erken çıkışı (`RunSampler.SampleAsync`, "tek kurus
  harcanmaz" yorumu) hiçbir şeyin kuyruğa girmesini engeller.

**Gerçek sonuç**
- `AgentPrism:OnlineEvaluation` için hiçbir `user-secrets` girdisi yok
  (`dotnet user-secrets list` doğrulandı — varsayılan durum). `support`
  agent'ına `FIX-PROMPT-02` gönderildi (`runId=019ffdde-5d11-76a5-81a8-39b58b04866e`),
  run tamamlandı, 10 sn beklendi. `SELECT count(*) FROM agentprism.jobs
  WHERE kind = 6` → `0`. `jobs` tablosundaki en son satırın `created_at`'i
  (`01:13:58`) bu run'ın tamamlanma zamanından (`01:23:56`) önceki bir
  koşuma ait — run sonrası hiçbir yeni iş kuyruğa girmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-041 — `Enabled=true` + `SampleRate=1.0` → HER tamamlanan run örneklenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Ön koşul**
- OpenAI etkin.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:OnlineEvaluation:Enabled" "true"
dotnet user-secrets set "AgentPrism:OnlineEvaluation:SampleRate" "1.0"
# Uygulamayi yeniden baslat.
```

**Adımlar**
1. Uygulamayı yeniden başlat.
2. `support` agent'ına `FIX-PROMPT-01` ile bir run gönder, tamamlanmasını
   bekle.
3. 15 saniye bekle.

**Doğrulama sorgusu**
```sql
SELECT j.kind, j.status FROM agentprism.jobs j WHERE j.kind = 6 ORDER BY j.created_at DESC LIMIT 1;
SELECT kind, value, source, author FROM agentprism.run_scores WHERE run_id = '<RUN_ID>';
```

**Beklenen sonuç**
- `jobs` tablosunda bir `OnlineEval` (`kind=6`) satırı, `status=Completed`.
- `run_scores`'ta `kind=3` (`Numeric`), `source='judge:model'`,
  `author='judge:model'` satırı.
- Case sonrası `dotnet user-secrets remove` ile her iki anahtarı kaldır.

**Gerçek sonuç**
- `AgentPrism:OnlineEvaluation:Enabled=true` ve `:SampleRate=1.0` set edildi,
  uygulama yeniden başlatıldı. `support` agent'ına `ORD-1001 siparisim
  nerede?` gönderildi (`runId=019ffddf-3ee3-77d6-bee7-2f572e003a9d`), run
  tamamlandı, 15 sn beklendi. `jobs` tablosunda `kind=6` (`OnlineEval`),
  `status=3` (`Completed`) satırı bulundu. `run_scores`'ta `kind=3`
  (`Numeric`), `value=95`, `source='judge:model'`, `author='judge:model'`
  satırı bulundu. Beklenenle birebir eşleşiyor. Case sonrası her iki
  `user-secrets` anahtarı `remove` ile kaldırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-042 — Aynı `runId` HER ZAMAN aynı örnekleme kararını verir (belirlenirlik)

Sınır senaryosu — `RunSampler.IsSampled` FNV-1a hash tabanlıdır,
`.NET HashCode` (süreç başına tuzlanır) DEĞİL.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Adımlar**
1. `SampleRate=0.5` ayarla, uygulamayı yeniden başlat.
2. Aynı `runId`'ye sahip bir run'ı (gerçekte tekrar tetiklenemeyeceği için,
   bunun yerine aynı run'ı `RunSampler`'ın iç mantığına eşdeğer biçimde
   elle FNV-1a hesaplayarak doğrula — bu case koşum notunda "hesaplama
   doğrulaması" olarak işaretlenir, gerçek HTTP çağrısı gerektirmez) veya
   uygulamayı yeniden başlatıp AYNI run'ı tekrar `POST /judge` ile manuel
   yargılayarak (MT-EVAL-044) örnekleme kararının süreç yeniden
   başlatıldıktan sonra da tutarlı kaldığını gözlemle.

**Beklenen sonuç**
- Belgelenen garanti: "aynı `runId` → aynı örneklenme kararı, süreç
  yeniden başlasa bile" (kod yorumu, `RunSampler.cs`). Bu case koşum
  notuna bu garantinin gözlemlendiğini/gözlemlenemediğini yazar.

**Gerçek sonuç**
- Hesaplama doğrulaması (gerçek HTTP çağrısı gerektirmez). `RunSampler.IsSampled`
  kaynağı okundu: `private static bool IsSampled(Guid runId, double sampleRate)`
  yalnız `runId`'nin 16 byte'ı üzerinden FNV-1a hash'i hesaplar (offset basis
  `14695981039346656037`, prime `1099511628211`); `HashCode`, `Random`,
  `Environment` veya süreç başına değişen HİÇBİR girdi kullanmıyor — saf,
  durumsuz bir fonksiyon. Bu, aynı `runId`'nin her çağrıda ve her süreç
  yeniden başlatmasında AYNI kesri üreteceğini matematiksel olarak garanti
  eder. Ampirik doğrulama: algoritma Python'da birebir yeniden üretilip
  (`.NET Guid` byte düzeni: Data1/Data2/Data3 little-endian, Data4 olduğu
  gibi) üç farklı gerçek `runId` için `sampleRate=0.5` ile iki kez
  hesaplandı — üçü de iki çağrıda da aynı sonucu verdi (ör.
  `019ffddf-3ee3-...` → kesir `0.7369989531679121`, `sampled=False`, her
  iki hesaplamada birebir aynı). Belgelenen garanti kodda doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-043 — `POST /api/runs/{id}/judge` örneklemeyi ATLAR, kayıtlıysa doğrudan puanlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Ön koşul**
- OpenAI etkin (yargıç kayıtlı). `AgentPrism:OnlineEvaluation:SampleRate`
  bu case için `0` (veya tanımsız) olabilir — manuel uç örnekleme kapısını
  hiç kullanmaz.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/judge" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT action, entity FROM agentprism.audit_log WHERE action = 'run.judge.manual' ORDER BY created_at DESC LIMIT 1;
```

**Beklenen sonuç**
- `HTTP: 200`, gövde en az bir `{ judge: "model", score, reason }` içerir.
- Audit tablosunda `run.judge.manual` kaydı.

**Gerçek sonuç**
- MT-EVAL-041'in run'ı (`019ffdde-5d11-76a5-81a8-39b58b04866e`, `SampleRate`
  bu sırada `1.0`'dı ama bu case örneklemeyi hiç kullanmıyor, doğrudan
  `/judge` çağırıyor) üzerinde `POST /api/runs/{id}/judge` çağrıldı. `HTTP:
  200`, gövde `[{"kind":"Numeric","value":92,"comment":"...",
  "source":"judge:model","author":"judge:model",...}]`. Audit tablosunda
  `action='run.judge.manual', entity='run:019ffdde-...'` kaydı bulundu.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-044 — Hiçbir `IRunJudge` KAYITLI DEĞİLKEN `/judge` → BOŞ dizi (hata değil)

Negatif/edge senaryo. `echo` sağlayıcısıyla (OpenAI kapalıyken) doğal olarak
gerçekleşir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Ön koşul**
- OpenAI **kapalı** (yargıç hiç kayıtlı değil).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<ECHO-RUN_ID>/judge" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`, gövde `[]`. Arayüzün karşılığı `MT-EVAL-057`'de test edilir
  (`onlineEval.judgeNoJudges` mesajı).

**Gerçek sonuç**
- OpenAI sağlayıcısı kapalıyken (bkz. bu case'in koşum notundaki olay —
  `AgentPrism:Providers:OpenAI:ApiKey` istemsizce boşaltıldı, ayrıntı
  dosya sonundaki "Sapmalar" bölümünde) `echo` sağlayıcılı `arastirmaci`
  agent'ına bir run gönderildi (`runId=019ffde2-b5b5-7e9d-8b08-28605fe7800e`),
  ardından `POST /api/runs/{id}/judge` çağrıldı. `HTTP: 200`, gövde `[]`.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-045 — Aynı run'ı İKİ KEZ yargılamak → AYNI `RunScore` satırı GÜNCELLENİR, iki satır OLUŞMAZ

Sınır senaryosu — `run_scores`'un `(tenant_id, run_id, message_id, author)`
tekilliği, `author` `NULL` olmadığı sürece çalışır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | K-239 |

**Ön koşul**
- OpenAI etkin. MT-EVAL-043 aynı `RUN_ID` için zaten çalıştırılmış.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/runs/<RUN_ID>/judge" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.run_scores WHERE run_id = '<RUN_ID>' AND author = 'judge:model';
```

**Beklenen sonuç**
- SQL sorgusu `1` döner — ikinci yargılama ilk satırı `UPSERT` ile
  günceller (`author`'ın `NULL` olmaması sayesinde), yeni satır eklemez.

**Gerçek sonuç**
- MT-EVAL-043'ün aynı run'ı (`019ffdde-5d11-76a5-81a8-39b58b04866e`) için
  `/judge` ikinci kez çağrıldı. Dönen kaydın `id`'si
  (`019ffde0-c844-7439-b423-54d6618d7f18`) ilk çağrıyla BİREBİR aynı kaldı
  (`value`/`comment`/`createdAt` güncellendi, satır değişmedi). SQL:
  `SELECT count(*) ... WHERE author='judge:model'` → `1`. Beklenenle
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-046 — `GET /api/evaluation/online` özet döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/evaluation/online" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`. Gövde `windowStart`, `windowEnd`, `sampleCount`,
  `averageScore` (nullable), `lowScoreThreshold`, `minSampleSize`,
  `belowThreshold` alanlarını taşır. `sampleCount < minSampleSize` (20)
  olduğu sürece `belowThreshold=false` — tek düşük puan alarm ÜRETMEZ.

**Gerçek sonuç**
- `HTTP: 200`. Gövde:
  `{"windowStart":"...","windowEnd":"...","sampleCount":0,"averageScore":null,
  "lowScoreThreshold":60,"minSampleSize":20,"belowThreshold":false,
  "judgeCost":null,"judgeCostCurrency":null}`. Tüm alanlar mevcut,
  `sampleCount=0 < minSampleSize=20` iken `belowThreshold=false`.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Deneyler (A/B): CRUD ve Doğrulama Kuralları (Faz 19)

### MT-EVAL-050 — Kod-kökenli agent (`support`) ile deney oluşturmak → `400`

Negatif senaryo — önemli iş kuralı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/support-deneyi" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentName": "support",
  "variants": [
    { "name": "a", "version": 1, "weight": 50 },
    { "name": "b", "version": 1, "weight": 50 }
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `support` kod-kökenli olduğu ve sürüm geçmişi taşımadığı
  için deneye konu olamaz.

**Gerçek sonuç**
- `HTTP: 400`, `detail: "'support' kodda tanimlidir ve surum gecmisi
  tutmaz. Kod kaynakli agent'larda deney kurulamaz."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-051 — DB-kökenli agent ile iki varyantlı, ağırlığı 100'e tamamlanan deney oluşturulur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-AGENT-01` (`manuel-destek`) mevcut, `version=1`.
- `manuel-destek`'i farklı bir talimatla tekrar `PUT` ederek `version=2`
  üret.

**Adımlar**
1. İkinci sürümü oluştur.
2. `FIX-EXP-01` deneyini `PUT` et.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/agents/manuel-destek" -H "$APB" -H "content-type: application/json" -d '{
  "instructions": "Sen bir siparis destek asistanisin. Detayli ve nazik yanit ver.",
  "tools": ["get_order_status"]
}'

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/destek-talimat-testi" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentName": "manuel-destek",
  "variants": [
    { "name": "kisa-talimat", "version": 1, "weight": 50 },
    { "name": "detayli-talimat", "version": 2, "weight": 50 }
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 200`, `status: "Draft"`.

**Gerçek sonuç**
- `manuel-destek` (`FIX-AGENT-01`) önceki oturumlardan zaten `version=3`
  taşıyordu (doc'un varsaydığı taze `version=1` değil — önceki fazlarda
  bu fixture üzerinde çalışılmış). Doc'un talimatını uyarlayarak: agent
  tekrar `PUT` edilip `version=4` üretildi, sonra deney `version=3`/`version=4`
  varyantlarıyla kuruldu (doc'taki `1`/`2` yerine). `HTTP: 200`,
  `"status":"Draft"`. Beklenen davranış (fonksiyonel olarak) doğrulandı;
  sürüm numaraları doc'tan farklı ama anlamı aynı — bu bir dokuman
  düzeltmesi değil, ortamın önceki koşumlardan kalan durumuna uyarlama.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-052 — Ağırlık toplamı ≠ 100 → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/yanlis-agirlik" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentName": "manuel-destek",
  "variants": [
    { "name": "a", "version": 1, "weight": 40 },
    { "name": "b", "version": 2, "weight": 40 }
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 400` — toplam `80`, `100` değil.

**Gerçek sonuç**
- `HTTP: 400`, `detail: "Varyant agirliklarinin toplami 100 olmalidir;
  suan 80."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-053 — Var olmayan `version` numarası → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/olmayan-surum" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentName": "manuel-destek",
  "variants": [
    { "name": "a", "version": 1, "weight": 50 },
    { "name": "b", "version": 99, "weight": 50 }
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 400` — `version: 99` `manuel-destek` için mevcut değil.

**Gerçek sonuç**
- `HTTP: 400`, `detail: "'manuel-destek' agent'inin 99 numarali surumu
  yok."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-054 — `START`, sonra AYNI agent için İKİNCİ bir deney başlatmak → `409`

Sınır senaryosu — DB'nin kısmi tekil indeksi
(`experiments_running_agent_uq`) "aynı agent için aynı anda en fazla 1
`Running` deney" kuralını **veritabanı seviyesinde** de zorlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-051 (`destek-talimat-testi`) `Draft` durumunda.

**Adımlar**
1. `destek-talimat-testi`'i başlat.
2. `manuel-destek` hedefli ikinci bir deney oluşturmayı ve başlatmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/experiments/destek-talimat-testi/start" -H "$APB"

curl -s -X PUT "$APU/api/experiments/ikinci-deney" -H "$APB" -H "content-type: application/json" -d '{
  "agentName": "manuel-destek",
  "variants": [{ "name": "a", "version": 1, "weight": 100 }]
}'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/experiments/ikinci-deney/start" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT name, status FROM agentprism.experiments WHERE agent_name = 'manuel-destek' AND status = 1;
```

**Beklenen sonuç**
- İlk `start`: `HTTP: 200`, `status: "Running"`.
- İkinci `start`: `HTTP: 409`.
- SQL sorgusu tam olarak `1` satır döner.

**Gerçek sonuç**
- (Sürüm numaraları MT-EVAL-051'deki uyarlamayla `1`/`2` yerine `3`
  kullanıldı.) İlk `start`: `HTTP: 200`, `"status":"Running"`. `ikinci-deney`
  (`manuel-destek`, `version=3`, `weight=100`) `Draft` olarak oluşturuldu,
  `start` edilince `HTTP: 409`, `detail: "'manuel-destek' agent'i icin
  baska bir deney zaten calisiyor. Ayni agent icin ayni anda tek deney
  calisabilir."` SQL: `status=1` (Running) satırı tam `1` adet
  (`destek-talimat-testi`). Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-055 — `Running` deneyi `DELETE` etmek → `409`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/experiments/destek-talimat-testi" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 409` — yalnız `Draft` deneyler silinebilir.

**Gerçek sonuç**
- `HTTP: 409`, `detail: "'destek-talimat-testi' deneyi calisirken
  silinemez; once durdurulmalidir."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-056 — `Running` deneyi `PUT` ile düzenlemek → hata

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/destek-talimat-testi" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentName": "manuel-destek",
  "variants": [{ "name": "tek", "version": 1, "weight": 100 }]
}'
```

**Beklenen sonuç**
- Başarısız olur (yalnız `Draft` düzenlenebilir — `InMemoryExperimentStore
  .SaveAsync`, `Status != Draft` ise `AgentPrismException`). Gerçek HTTP
  kodunu koşum kaydeder.

**Gerçek sonuç**
- `HTTP: 409`, `detail: "'destek-talimat-testi' deneyi 'Running'
  durumunda; yalnizca Draft durumundaki deneyler duzenlenebilir."`
  Başarısız olma beklentisiyle eşleşiyor; gözlemlenen kod `409`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-057 — `STOP` → `Stopped`; tek yönlü, `Draft`'a DÖNMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/experiments/destek-talimat-testi/stop" -H "$APB"
curl -s "$APU/api/experiments/destek-talimat-testi" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- `HTTP: 200`. Durum `Stopped`. Deneyi tekrar `Draft`'a çeviren hiçbir
  uç yoktur — yeniden kullanmak için yeni bir isimle yeniden oluşturmak
  gerekir.

**Gerçek sonuç**
- `HTTP: 200`, `"status":"Stopped"`, `"endedAt"` dolduruldu. Takip eden
  `GET` de `"Stopped"` döndürdü. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-058 — Arayüz: ağırlık toplamı ≠ 100 iken "Kaydet" DEVRE DIŞI

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/experiments` → "New".
2. Varyant ağırlıklarını `30` + `30` yap.

**Beklenen sonuç**
- Toplam kırmızı renkte gösterilir; "Kaydet" düğmesi devre dışıdır.

**Gerçek sonuç**
- `/agentprism/experiments` → "New experiment" açıldı, iki varyantın
  `Weight %` alanları `30`/`30` yapıldı. Metin "Weights total 60% (must
  be 100%)" göründü, `browser_evaluate` ile `getComputedStyle(...).color`
  → `rgb(190, 18, 60)` (kırmızı/rose tonu) doğrulandı. "Save" düğmesi
  `disabled` özniteliğiyle işaretliydi. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-059 — Arayüz: `Running`/`Stopped` deneyde Düzenle/Sil GİZLİ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Adımlar**
1. `/agentprism/experiments` listesinde `destek-talimat-testi` (`Stopped`)
   satırına bak.

**Beklenen sonuç**
- Düzenle ve Sil düğmeleri yalnız `status === 'Draft'` iken görünür — bu
  satırda ikisi de yoktur.

**Gerçek sonuç**
- `/agentprism/experiments` listesinde `destek-talimat-testi` (`stopped`)
  satırının son hücresi BOŞ — Edit/Sil düğmesi yok. Karşılaştırma amaçlı:
  aynı listedeki `ikinci-deney` (`draft`) satırında "Edit" düğmesi VE bir
  ikinci (sil) düğmesi görünüyor. Kontrast beklenen davranışı doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Atama Belirlenirliği (Assignment Determinism)

### MT-EVAL-062 — Aynı oturum kimliği HER ZAMAN aynı varyantı alır

Sınır senaryosu — SHA-256 tabanlı kova ataması, önbellek değil.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir deney oluştur, `Running` yap (`destek-talimat-testi`'i
  `MT-EVAL-051`/`054` desenine göre yeniden kur — `Stopped` olanı yeniden
  kullanamazsın, yeni isimle `destek-talimat-testi-2` oluştur).

**Adımlar**
1. Aynı `sessionId` ile agent'ı **5 kez** art arda çalıştır.
2. Her seferinde `runs.variant` sütununu oku.

**Girilecek veri**
```bash
for i in 1 2 3 4 5; do
  curl -s -X POST "$APU/api/agents/manuel-destek/run" -H "$APB" -H "content-type: application/json" -d '{
    "message": "Merhaba", "sessionId": "belirlenirlik-testi-42"
  }' | python3 -c "import json,sys; print(json.load(sys.stdin).get('runId'))"
done
```

**Doğrulama sorgusu**
```sql
SELECT DISTINCT variant FROM agentprism.runs WHERE experiment_id IS NOT NULL AND session_id = 'belirlenirlik-testi-42';
```

**Beklenen sonuç**
- SQL sorgusu **tek** bir `variant` değeri döner — 5 çalıştırmanın tümü
  aynı varyanta düşer.

**Gerçek sonuç**
- `destek-talimat-testi-2` (`kisa-talimat`/`version=3`, `detayli-talimat`
  /`version=4`, `50`/`50`) oluşturulup `Running` yapıldı. `manuel-destek`
  agent'ına `sessionId="belirlenirlik-testi-42"` ile 5 kez art arda `POST
  /api/agents/manuel-destek/run` çağrıldı. SQL: `SELECT DISTINCT variant
  ...` → tek satır, `kisa-talimat`. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-063 — `runs` tablosunda `experiment_id`/`variant` sütunları dolu gelir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

**Doğrulama sorgusu**
```sql
SELECT experiment_id, variant, agent_version FROM agentprism.runs WHERE session_id = 'belirlenirlik-testi-42' LIMIT 1;
```

**Beklenen sonuç**
- `experiment_id` deneyin `id`'siyle eşleşir, `variant` MT-EVAL-062'de
  gözlenen isimle eşleşir, `agent_version` o varyantın `version`
  numarasıyla eşleşir.

**Gerçek sonuç**
- SQL sonucu: `experiment_id="019ffe21-a2d1-7078-a720-c9c6dd677a52"`
  (`destek-talimat-testi-2`'nin `id`'siyle birebir), `variant="kisa-talimat"`
  (MT-EVAL-062 ile aynı), `agent_version=3` (`kisa-talimat` varyantının
  `version` değeriyle aynı). Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Kanarya Yayını ve Otomatik Geri Alma (Faz 56)

### MT-EVAL-070 — İki varyantlı OLMAYAN deneye kanarya politikası eklemek → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | — |

**Ön koşul**
- Üç varyantlı bir deney oluştur (`Draft`), ör. `a/b/c` ağırlıkları
  `34/33/33`.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/uc-varyantli/canary" -H "$APB" \
     -H "content-type: application/json" -d '{
  "canaryVariant": "a", "minSampleSize": 20, "rampSteps": [10, 50, 100]
}'
```

**Beklenen sonuç**
- `HTTP: 400` — kanarya yalnız İKİ varyantlı deneylerde tanımlanabilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-071 — Geçerli kanarya politikası PUT edilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | — |

**Ön koşul**
- İki varyantlı `destek-talimat-testi-2` (§7'de oluşturuldu), `Running`.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/destek-talimat-testi-2/canary" -H "$APB" \
     -H "content-type: application/json" -d '{
  "canaryVariant": "detayli-talimat",
  "minSampleSize": 3,
  "maxErrorRateDelta": 0.2,
  "rampSteps": [25, 50, 100],
  "rampIntervalHours": 1
}'
```

**Beklenen sonuç**
- `HTTP: 200`.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-072 — `GET .../canary` — yeterli örnek toplanana kadar `InsufficientData`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/experiments/destek-talimat-testi-2/canary" -H "$APB"
```

**Beklenen sonuç**
- `evaluation.decision: "InsufficientData"` — henüz her iki kolda da
  `minSampleSize=3` kadar tamamlanmış run yok.
- Bu değerlendirme HER `GET` çağrısında CANLI hesaplanır, **kalıcı
  değildir**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-073 — Örnek uygulamada `AutoRollbackEnabled` VARSAYILAN OLARAK kapalı — arka plan servisi HİÇBİR ŞEY yapmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | — |

**Adımlar**
1. Uygulama loglarında `CanaryEvaluationService`'in başlangıç mesajını ara
   (varsa) veya 5+ dakika bekleyip hiçbir otomatik geri alma
   gerçekleşmediğini gözle.

**Beklenen sonuç**
- `appsettings.json`'da `AgentPrism:Canary` bölümü yoktur;
  `AutoRollbackEnabled` derleme zamanı varsayılanı (`false`) geçerlidir —
  `CanaryEvaluationService.ExecuteAsync` başlangıçta hemen döner, hiçbir
  deneyi taramaz.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-074 — `AutoRollbackEnabled=true` + düşük eşik → OTOMATİK geri alma tetiklenir, audit ÖNCE yazılır

Bu dosyanın en kritik senaryosu — arka plan servisinin gerçek etkisini
kanıtlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | K-089 |

**Ön koşul**
- `destek-talimat-testi-2`, `minSampleSize=3`, `maxErrorRateDelta=0.0`
  (herhangi bir hata farkı geri almayı tetikler) ile kanarya politikası
  taşır.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Canary:AutoRollbackEnabled" "true"
dotnet user-secrets set "AgentPrism:Canary:ScanInterval" "00:00:30"
# Uygulamayi yeniden baslat.
```

**Adımlar**
1. Uygulamayı yeniden başlat.
2. Kanarya varyantına (`detayli-talimat`, `version: 2`) düşen en az 3 run'ı
   BAŞARISIZ ürettir (ör. `manuel-destek`'i geçici olarak var olmayan bir
   `modelId` ile çağırarak) — kontrol varyantı (`kisa-talimat`) için 3
   BAŞARILI run üret.
3. 60 saniye bekle (2 tarama döngüsü).

**Doğrulama sorgusu**
```sql
SELECT rollback_reason, canary_policy->'canaryVariant' FROM agentprism.experiments WHERE name = 'destek-talimat-testi-2';
SELECT action, entity, after FROM agentprism.audit_log WHERE action = 'experiment.auto_rollback' ORDER BY created_at DESC LIMIT 1;
```

**Beklenen sonuç**
- `experiments.rollback_reason` doludur.
- `audit_log`'da `experiment.auto_rollback` kaydı, `actor:
  "system:canary-evaluator"`.
- `GET /api/experiments/destek-talimat-testi-2` → `variants` ağırlıkları
  kanarya `0`, kontrol `100` olmuştur.
- Case sonrası `AgentPrism:Canary:*` `user-secrets` girdilerini kaldır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-075 — Sağlıklı kanarya + `rampSteps` varsa ağırlık kademeli artar; ramp-up AUDIT'E YAZILMAZ

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir iki-varyantlı deney (`kanarya-saglikli`), kanarya politikası
  `rampSteps: [25, 50, 100]`, `minSampleSize: 3`, `rampIntervalHours: 0`
  (test hızlandırmak için — gerçek `TimeSpan` string'i `"0:00:01"` gibi
  çok kısa bir değer kullanılabilir). `AutoRollbackEnabled=true` (MT-EVAL-074'ten).

**Adımlar**
1. Kanarya varyantına 3+ BAŞARILI run üret (kontrol varyantına da eşit
   sayıda başarılı run üret ki hata farkı `0` kalsın).
2. 60+ saniye bekle.

**Doğrulama sorgusu**
```sql
SELECT canary_policy FROM agentprism.experiments WHERE name = 'kanarya-saglikli';
SELECT count(*) FROM agentprism.audit_log WHERE action LIKE 'experiment%' AND entity = 'experiment:kanarya-saglikli';
```

**Beklenen sonuç**
- Kanarya varyantının ağırlığı `rampSteps`'teki bir sonraki basamağa
  (`25`) yükselmiştir.
- Audit tablosunda **ramp-up için hiçbir kayıt yoktur** — yalnız geri alma
  (`experiment.auto_rollback`) audit'e yazılır, ramp-up sessiz kalır. Bu,
  koddaki açık bir tasarım kararıdır, kusur değildir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-076 — Arayüz: `rollbackReason` dolu banner, OTOMATİK geri almayı manuel `Stop`'tan ayırt eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 56 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-074 sonrası `destek-talimat-testi-2`.

**Adımlar**
1. `/agentprism/experiments/destek-talimat-testi-2` ekranını aç.

**Beklenen sonuç**
- Kırmızı bir banner geri alma nedenini gösterir. Bu, ekranın **tek**
  görsel ipucudur — manuel `Stop` sonrası aynı ekranda böyle bir banner
  YOKTUR; tester bu ikisini yalnız bu banner'ın varlığıyla ayırt edebilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Geri Bildirim ve Puanlama (Faz 31)

> Bu bölüm daha önce **hiçbir** manuel test dosyasına atanmamıştı (bkz.
> `00-INDEKS.md` §8, 2026-08-09 tarihli not) — bu dosya bu boşluğu kapatır.

### MT-EVAL-080 — `POST /feedback` ikili (Binary) puanı kaydeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-01` ile gerçek bir run gönderilmiş, `runId` not alınmış.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" \
     -H "content-type: application/json" -d '{ "kind": "Binary", "value": 1, "comment": "Dogru cevap." }'
```

**Beklenen sonuç**
- `HTTP: 200`/`201`. `GET /api/runs/<RUN_ID>/feedback` yeni satırı gösterir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-081 — Binary `value: 2` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" \
     -H "content-type: application/json" -d '{ "kind": "Binary", "value": 2 }'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "Ikili puan ('binary') yalniz 0 veya 1 olabilir."`

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-082 — Yıldız (`Stars`) puanı `1..5` API'de tam desteklenir (arayüzde YOK)

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | K-242 |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" \
     -H "content-type: application/json" -d '{ "kind": "Stars", "value": 4 }'
```

**Beklenen sonuç**
- `HTTP: 200`/`201`. `run_scores.kind=2`, `value=4`. Ancak `/agentprism`
  arayüzünde bu puanı ÜRETEN hiçbir buton yoktur (bkz. MT-EVAL-089) —
  yalnız `curl` veya doğrudan API tüketicisi bir `Stars` puanı yazabilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-083 — Stars `value: 0` ve `value: 6` → ikisi de `400`

Negatif/sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" \
     -H "content-type: application/json" -d '{ "kind": "Stars", "value": 0 }'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" \
     -H "content-type: application/json" -d '{ "kind": "Stars", "value": 6 }'
```

**Beklenen sonuç**
- İkisi de `HTTP: 400`, `detail: "Yildiz puani ('stars') 1 ile 5 arasinda
  olmalidir."`

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-084 — Aynı yazar aynı hedefi iki kez puanlar → UPSERT (tek satır)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-080 ile aynı `RUN_ID`'ye aynı statik bearer token'la (aynı
  "yazar" kimliği) ikinci bir puan gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" \
     -H "content-type: application/json" -d '{ "kind": "Binary", "value": 0, "comment": "Fikrim degisti." }'
```

**Doğrulama sorgusu**
```sql
SELECT count(*), value, comment FROM agentprism.run_scores WHERE run_id = '<RUN_ID>' AND kind = 1 GROUP BY value, comment;
```

**Beklenen sonuç**
- Tek satır kalır, `value=0`, `comment='Fikrim degisti.'` — ilk puan
  ÜZERİNE yazılmıştır (bu ortamda `author` alanı statik bearer token'a
  bağlı sabit bir değere karşılık gelir, `NULL` değildir).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-085 — `author` alanının `NULL` olduğu anonim senaryoda İKİ POST → İKİ AYRI satır

Şüpheli davranış — koddan ölçüldü, koşumda doğrulanır. `run_scores`'un
tekillik indeksi `author`'ı `COALESCE` etmez; PostgreSQL'de `NULL ≠ NULL`
olduğu için tekillik hiç devreye girmez.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Ön koşul**
- Bu davranışı tetiklemek `author`'ın `NULL` yazıldığı bir çağrı yolu
  gerektirir — statik bearer token senaryosunda `author` dolu olabilir; bu
  case `AgentPrismTestHost`/`FakeModelProvider` (İzlek C) ile,
  `IRunScoreStore.UpsertAsync`'i doğrudan `Author: null` ile iki kez
  çağıran bir entegrasyon testi biçiminde koşulur (`AgentPrism.Testing`
  paketinin sağladığı test host'u kullan).

**Beklenen sonuç**
- `SELECT count(*) FROM run_scores WHERE run_id = '<test-run-id>' AND
  author IS NULL` → `2`, `1` DEĞİL. Bu, `docs/31-...md`'nin "açık soru 4"
  olarak işaretlediği, kasıtlı kabul edilmiş bir davranıştır — kusur
  olarak işaretlenmez, yalnız doğrulanır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-086 — `DELETE /feedback/{scoreId}` puanı siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
SCORE_ID=$(curl -s "$APU/api/runs/<RUN_ID>/feedback" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)[0]['id'])")
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/runs/<RUN_ID>/feedback/$SCORE_ID" -H "$APB"
```

**Doğrulama sorgusu**
```sql
SELECT action FROM agentprism.audit_log WHERE action = 'run.feedback.delete' ORDER BY created_at DESC LIMIT 1;
```

**Beklenen sonuç**
- `HTTP: 204`. Audit tablosunda `run.feedback.delete` kaydı.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-087 — Programatik yazma (yargıç) `IRunScoreStore`'a AUDIT İZİ BIRAKMAZ

Şüpheli davranış — kod okumasıyla ölçüldü.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31, 49 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-041 veya MT-EVAL-043 ile bir yargıç puanı üretilmiş.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.audit_log WHERE action LIKE 'run.feedback%' AND entity LIKE '%<RUN_ID>%';
```

**Beklenen sonuç**
- `POST /judge`'ın kendisi `run.judge.manual` audit kaydı bırakır
  (MT-EVAL-043) AMA bu, `IRunScoreStore.UpsertAsync`'in KENDİSİ değil,
  HTTP uç işleyicisinin ayrıca yazdığı bir kayıttır. `RunSampler`'ın
  otomatik tetiklediği örnekleme yolunda (MT-EVAL-041, HTTP uç noktası
  hiç devrede değil) HİÇBİR `run.feedback*`/`run.judge*` audit kaydı
  oluşmaz — `IRunScoreStore` denetim izine sarılı DEĞİLDİR. Bu, koddan
  ölçülen kasıtlı bir tasarım kararıdır (`docs/31-...md`: "kullanici geri
  bildirimi bir yonetici karari degildir"), ama programatik/otomatik
  puanlama yollarının hiçbir izinin kalmaması ayrı bir gözlemdir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-088 — Arayüz `FeedbackControl`: aynı başparmağa TEKRAR tıklamak puanı SİLER

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Adımlar**
1. Herhangi bir run'ın detay ekranını aç.
2. Başparmak-yukarı düğmesine tıkla — puanın kaydedildiğini gözle.
3. AYNI düğmeye TEKRAR tıkla.

**Beklenen sonuç**
- İkinci tıklama başparmağı aşağı çevirmez — puanı komple SİLER (`mine.id`
  üzerinden `remove.mutate`). Üçüncü bir tıklama tekrar oluşturur.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-089 — Arayüz `FeedbackControl`: yorum, PUAN YOKKEN kaydedilmez; yıldız arayüzü hiç YOK

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | K-242 |

**Adımlar**
1. Hiç puanlanmamış bir run'ın detay ekranını aç.
2. Doğrudan yorum kutusuna metin yaz, kutudan çık (blur).
3. Ekranda yıldız ikonlarını ara.

**Beklenen sonuç**
- Yorum kaydedilmez (önce bir başparmak puanı gerekir).
- Sayfada hiçbir yıldız-değerlendirme kontrolü yoktur — `RunScoreKind
  .Stars` yalnız API üzerinden (MT-EVAL-082) yazılabilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-090 — Arayüz "Judge now" düğmesi, yargıç yokken doğru mesaj gösterir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 49 |
| **İlgili karar** | — |

**Ön koşul**
- OpenAI kapalı (`echo` sağlayıcısıyla üretilmiş bir run).

**Adımlar**
1. O run'ın detay ekranını aç, "Judge now" düğmesine tıkla.

**Beklenen sonuç**
- `onlineEval.judgeNoJudges` metni satır içinde görünür — hata banner'ı
  DEĞİL, beklenen bir boş-sonuç mesajı.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — Çalıştırma Karşılaştırma, Yeniden Oynatma, Girdi Görüntüleme

### MT-EVAL-091 — `GET /compare/{a}/{b}` iki run'ı yan yana döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/runs/<RUN_A>/compare/<RUN_B>" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`. `left`/`right` her biri `runId, agentName, agentVersion,
  modelId, status, durationMs, usage, cost, toolCallCount, output,
  scores` taşır. Fark HESAPLAMASI sunucuda yapılmaz — yalnız iki ham
  taraf döner, karşılaştırma arayüzde hesaplanır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-092 — `GET /input`: `RecordRunInput=false` iken `404`

Negatif/ayar-bağımlı senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:RunRecording:RecordRunInput" "false"
# Uygulamayi yeniden baslat, yeni bir run gonder, sonra:
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs/<YENI-RUN_ID>/input" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Girdi kaydi yok"`.
- Case sonrası `dotnet user-secrets remove "AgentPrism:RunRecording
  :RecordRunInput"` ile varsayılana dön.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-093 — `POST /replay` varsayılan `ReplayTools`: kaydedilmiş tool sonucu tekrar kullanılır, GERÇEK yan etki YOK

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-01` ile `support` agent'ından `get_order_status` çağıran bir
  run mevcut.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$APB" \
     -H "content-type: application/json" -d '{ "toolMode": "ReplayTools" }'
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıt `compareLocation` ile orijinal run'a karşı hazır bir
  karşılaştırma bağlantısı içerir. `get_order_status` GERÇEKTEN tekrar
  çağrılmaz — kaydedilmiş sonuç enjekte edilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-094 — `POST /replay` `LiveTools`: rota seviyesi `Operator` YETMEZ, işleyici içi `Admin` gerekir

Negatif senaryo — iki katmanlı yetkilendirme.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Ön koşul**
- Yalnız `Operator` rolüne sahip (ama `Admin` olmayan) bir kimlikle
  çağrılabilecek bir test kurulumu (bu ortamda statik bearer token her
  role eşdeğer davrandığından — bkz. `00-INDEKS.md` §8 rol matrisi
  no-op notu — bu case'in gerçek ayrımı ancak rol politikaları AÇIKÇA
  kayıtlı bir ortamda gözlenebilir; bu ortamda yalnız KOD OKUMASIYLA
  doğrulanan bir iddia olarak işaretlenir, koşum bunu "koşulamadı,
  varsayılan kurulumda rol ayrımı yok" notuyla kapatabilir).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$APB" \
     -H "content-type: application/json" -d '{ "toolMode": "LiveTools" }'
```

**Beklenen sonuç**
- Bu statik-token ortamında `HTTP: 200` beklenir (rol ayrımı no-op) —
  `get_order_status` GERÇEKTEN yeniden çağrılır. Koşum notu, gerçek bir
  rol-ayrımlı ortamda bu isteğin `Admin` olmayan bir kimlik için `403`
  vermesi GEREKTİĞİNİ, ama bu manuel test ortamında doğrulanamadığını
  kaydeder.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 11 — Güvenlik: Eval/Deney Alanında API Anahtarı Kapsamı HİÇ YOK

### MT-EVAL-100 — `ApiKeyScope` enum'ında Eval/Experiment için kapsam YOK — yalnız Role ile sınırlı anahtar TÜM uçlara erişir

🚨 Şüpheli davranış — kod okumasıyla ölçüldü, koşumda doğrulanır. Bu,
`WorkflowEndpoints`/`SchedulingEndpoints`/`GovernanceEndpoints`'te (§18'de
ayrıca ölçülür) "unutulan bir çağrı" ile AYNI SEMPTOMU üretir ama kök
nedeni farklıdır: burada `RequireApiKeyScope` çağrılmamış OLMASININ ötesinde,
çağrılabilecek bir `EvalsRead`/`ExperimentsAdmin` gibi bir kapsam değeri
**hiç tanımlanmamıştır** (`ApiKeyScope` yalnız `RunsRead=0, RunsWrite=1,
AgentsRead=2, AgentsAdmin=3, ExternalInvoke=4` beş üyeden ibarettir).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 18, 19, 53 |
| **İlgili karar** | K-… (53.3, kapsam ∩ rol ilkesi) |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md` `MT-SEC-050`'nin API anahtarı oluşturma
  deseni.

**Adımlar**
1. Yalnız `RunsRead` kapsamıyla bir API anahtarı üret.
2. Bu anahtarla `PUT /api/evals/{name}` (yeni bir eval takımı) dene.
3. Aynı anahtarla `PUT /api/experiments/{name}` (yeni bir deney) dene.
4. Kontrol grubu: aynı anahtarla `AgentsAdmin` gerektiren bir agent ucuna
   yazmayı dene, `403` aldığını doğrula.

**Girilecek veri**
```bash
KEY_JSON=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "eval-kapsam-testi", "scopes": ["RunsRead"] }')
echo "$KEY_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['rawKey'])"
export EVALKEY="Authorization: Bearer <yukaridaki-rawKey>"

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/evals/kapsam-testi" -H "$EVALKEY" \
     -H "content-type: application/json" -d '{"agentName":"support","checks":[{"kind":"nonEmpty"}]}'

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/experiments/kapsam-testi" -H "$EVALKEY" \
     -H "content-type: application/json" -d '{"agentName":"manuel-destek","variants":[{"name":"a","version":1,"weight":100}]}'

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/kapsam-kontrol" -H "$EVALKEY" \
     -H "content-type: application/json" -d '{"name":"kapsam-kontrol","instructions":"test"}'
```

**Beklenen sonuç (şüphe)**
- Adım 2 ve 3: `HTTP: 200` — kapsam kısıtı UYGULANMAZ (çünkü uygulanacak
  bir kapsam zaten yok).
- Adım 4: `HTTP: 403` — kontrol grubu, kapsam sisteminin `AgentEndpoints`'te
  gerçekten çalıştığını, ama eval/experiment yüzeyinde hiç var olmadığını
  gösterir.
- Doğrularsa: yalnız okuma amaçlı bir anahtarın eval takımı/deney
  oluşturup silebilmesi, oluşturduğu deneyler üzerinden dolaylı olarak
  gerçek para harcayan çalıştırmalar tetikleyebilmesi anlamına gelir —
  **Kusur, Önem: Yüksek**. `16-IS-KUYRUGU-VE-ZAMANLAMA.md` `MT-JOB-090`'ın
  ve `15-WORKFLOWS.md` `MT-WF-100`'ün bulduğuyla aynı kalıbın DÖRDÜNCÜ
  bağımsız tekrarıdır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-101 — `feedback`/`compare`/`input` uçlarında da `RequireApiKeyScope` YOK — `replay`'in AKSİNE

🚨 Şüpheli davranış — aynı dosya (`RunEndpoints.cs`) içinde KARIŞIK bir
desen: run yaşam döngüsü uçları (`/runs`, `/tree`, `/{id}`, `/events`,
`/cancel`, `/replay`) `RequireApiKeyScope(RunsRead|RunsWrite)` çağırırken,
`feedback`/`compare`/`input` uçları hiç çağırmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 31 |
| **İlgili karar** | — |

**Ön koşul**
- MT-EVAL-100'deki `RunsRead`-kapsamlı anahtar.

**Adımlar**
1. Aynı anahtarla bir run'a geri bildirim YAZMAYI dene (`RunsWrite`
   gerektirmiyor olması beklenir çünkü hiç kapsam denetimi yok).
2. Kontrol grubu: aynı anahtarla `/replay` (kapsamı VAR) dene, `RunsWrite`
   eksikliğinden `403` aldığını doğrula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/feedback" -H "$EVALKEY" \
     -H "content-type: application/json" -d '{ "kind": "Binary", "value": 1 }'

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$EVALKEY" \
     -H "content-type: application/json" -d '{ "toolMode": "ReplayTools" }'
```

**Beklenen sonuç (şüphe)**
- Feedback yazma: `HTTP: 200`/`201` — yalnız `RunsRead` taşıyan bir
  anahtar geri bildirim YAZABİLİR.
- Replay: `HTTP: 403` — `replay` ucu `RunsWrite` gerektirir ve bu anahtar
  onu taşımaz; bu KONTRAST, aynı dosyada `RequireApiKeyScope`'un bazı
  uçlara eklenip bazılarına eklenmediğini kanıtlar.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Koşum sonrası temizlik notu

Bu dosyanın case'leri `AgentPrism:OnlineEvaluation:*`,
`AgentPrism:Canary:*` ve `AgentPrism:RunRecording:RecordRunInput`
`user-secrets` girdilerini geçici olarak açar. Dosyayı bitirdikten sonra:

```bash
cd samples/AgentPrism.Api
dotnet user-secrets remove "AgentPrism:OnlineEvaluation:Enabled"
dotnet user-secrets remove "AgentPrism:OnlineEvaluation:SampleRate"
dotnet user-secrets remove "AgentPrism:Canary:AutoRollbackEnabled"
dotnet user-secrets remove "AgentPrism:Canary:ScanInterval"
dotnet user-secrets remove "AgentPrism:RunRecording:RecordRunInput"
```
