# 21 — Dayanıklılık: Çalıştırma İptali, Öksüz Uzlaştırma ve Asenkron Onay Kutusu (`RES`)

> **Alan kodu:** `RES` · **Faz:** 32, 54, 55 tam kapsam · 87 (devam mekanizması ve
> zarif kapanış) tam kapsam · 44 yalnız `Canceled`/`Infrastructure`
> sınıfları · 46/47 yalnız bu dosyaya özgü kesişim noktaları (bkz. Sınır tablosu) ·
> 142 yalnız `PendingApproval.Presentation` alanı ve `IToolApprovalPresenter`.
>
> **Kaynak:**
> `src/Tracon.Abstractions/Runs/IRunCancellationRegistry.cs`,
> `RunReconciliationOptions.cs`, `RunErrorClass.cs` (yalnız `Canceled=10`/
> `Infrastructure=12`) ·
> `src/Tracon.Abstractions/Approvals/` (tümü: `PendingApproval.cs`,
> `ApprovalStatus.cs`, `IPendingApprovalStore.cs`) ·
> `src/Tracon.Core/Recording/RunCancellationRegistry.cs`,
> `RunHeartbeatWriter.cs`, `RunReconciliationService.cs`,
> `RunReconciliationOptionsValidator.cs` ·
> `src/Tracon.Core/Approvals/` (tümü: `InMemoryPendingApprovalStore.cs`,
> `TraconApprovalOptions.cs`, `ApprovalExpirationService.cs`,
> `ApprovalResumeJobHandler.cs`) ·
> `src/Tracon.Core/Runs/DefaultRunErrorClassifier.cs` (yalnız
> `Canceled`/`Infrastructure` dalları) ·
> `src/Tracon.Core/SingletonGuard.cs`, `SingletonExecutionOptions.cs` ·
> `src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs` (yalnız
> `CancelRunAsync`, satır 631-719) ·
> `src/Tracon.AspNetCore/Endpoints/ApprovalEndpoints.cs` (tümü) ·
> `src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs` (yalnız `RunAsync`,
> §1'in workflow-iptal denemesi için) ·
> `src/Tracon.UI/frontend/src/screens/approvals.tsx` ·
> `src/Tracon.Core/Hosting/TraconDrainService.cs`,
> `TraconDrainOptions.cs` ·
> `src/Tracon.Core/Recording/TraconRunContinuationOptions.cs`,
> `RunReconciliationService.cs` (yalnız `TryContinueAsync` ve altındakiler) ·
> `src/Tracon.Core/Scheduling/RunContinuationJobHandler.cs` ·
> `src/Tracon.Core/Replay/RecordedToolPlayback.cs` (`ToolPlaybackMismatchPolicy`) ·
> `src/Tracon.Abstractions/Runs/ITraconDrainState.cs` ·
> `src/Tracon.AspNetCore/RateLimiting/DrainGate.cs` ·
> `src/Tracon.UI/frontend/src/screens/run-detail.tsx` (yalnız `continuedFromRunId` bağı).
>
> 🚨 **Kaynak eşlemesi kökten daraltıldı — grep ile ölçülen büyük örtüşme
> (2026-08-10, bu oturumda ölçüldü).** `00-INDEKS.md`'nin §7 tablosu bu
> dosyaya Faz 32, 43, 44, 46, 47, 54, 55'in TAMAMINI ve `~45` case hedefini
> veriyordu. Üretime başlamadan önce `docs/manuel-test/*.md` içinde bu
> fazların yüzeyleri arandı (`grep -ln "cancel\|Idempotency\|RunErrorClass\|
> Queued\|respond-async\|replay\|branch\|orphan\|PendingApproval" *.md`) ve
> şu ölçüldü:
>
> | Yüzey | Zaten üretildiği dosya |
> |---|---|
> | İptal düğmesi, etki tooltip’i, durum rozeti (arayüz) | `11-ARAYUZ-RUN-SESSION-SSE.md` `MT-UIRUN-021`–`025` |
> | İş kuyruğu seviyesinde `Pending`/`Running`/`Queued` iptali | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` `MT-JOB-040`–`044`, `080`–`082` |
> | `Idempotency-Key` sözleşmesi (dört durum, `400`/`422`, saklama) | `07-HTTP-YONETIM-API.md` `MT-API-030`–`033`, `16` `MT-JOB-083/084` |
> | `/api/stats/errors` panosu, `ProviderError`/`ToolError` gerçek örnekleri | `07` `MT-API-040/041`, `12-GOZLEMLENEBILIRLIK-MALIYET.md` `MT-OBS-010` |
> | `Prefer: respond-async` sözleşmesi (202/Location/Queued/501) | `16` `MT-JOB-070`–`078` |
> | Yeniden oynatma paneli, karşılaştırma paneli, dallandırma düğmesi (arayüz) | `11` `MT-UIRUN-026`–`046`, `10-ARAYUZ-AGENT-PLAYGROUND.md` `MT-UIAG-039` |
> | `POST /replay` kapsam denetimi (`Operator`/`Admin`) | `17-EVAL-VE-DENEYLER.md` `MT-EVAL-093/094/101` |
>
> Bu yüzeyler TEKRARLANMAZ (ayrıntı: aşağıdaki Sınır tablosu). Buna karşılık
> `pending_approvals`/`PendingApproval`/`/api/approvals` için `docs/manuel-test/
> *.md` içinde **sıfır eşleşme** vardı (Faz 55 hiç üretilmemiş — ne HTTP ucu ne
> `screens/approvals.tsx`) ve `orphan`/`reconcil` yalnız `16`'da geçen bir
> cümlede vardı, kendi case'i yoktu (Faz 54 hiç üretilmemiş). Bu dosya bu
> yüzden Faz 32/44'ün YALNIZ daha önce hiçbir dosyada ölçülmemiş
> kayıt-defteri/hata-sınıfı köşelerine ve Faz 54/55'in TAMAMINA odaklanır.
> Hedef case sayısı `~45` değil, gerçek yüzeye göre **28**'dir
> (`PROMPT.md` §6: "Senaryo sayısı için doldurma yapılmaz").
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/21-DAYANIKLILIK-VE-IPTAL.md`](kosumlar/2026-08-13/21-DAYANIKLILIK-VE-IPTAL.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

```mermaid
flowchart TD
    A["POST /api/runs/id/cancel"] --> B{"Queued mu, Running mi?"}
    B -- "Queued" --> C["IJobStore.CancelAsync + CompleteRunAsync(Canceled)<br/>ANINDA - govde GUNCEL durumu tasir"]
    B -- "Running" --> D["IRunCancellationRegistry.TryCancel<br/>RunId == RootRunId ise agactaki TUM alt kayitlar da Cancel()"]
    D -- "bu surecte kayitli DEGIL" --> E["409 - baska ornek / yeniden baslamis surec"]
    D -- "kayitli" --> F["202 - govde ESKI durumu tasir<br/>nihai durum GET ile okunur"]
    F --> FA["run.cancel audit - YALNIZ istek yapilan runId icin<br/>cocuklarin Cancel()'i audit YAZMAZ"]

    G["RunHeartbeatWriter"] -. "HeartbeatInterval" .-> H["runs.heartbeat_at"]
    I["RunReconciliationService<br/>varsayilan KAPALI"] -. "ScanInterval" .-> J{"COALESCE(heartbeat_at,started_at)<br/>OrphanThreshold'dan eski mi?"}
    J -- "evet" --> K["Failed<br/>error.class=Infrastructure, fingerprint=orphaned"]

    L["Prefer: respond-async<br/>+ onay gerektiren tool"] --> M["RunStatus.AwaitingApproval<br/>pending_approvals bir IZDUSUMDUR"]
    M --> N["GET /api/approvals/pending"]
    N --> O["POST /api/approvals/id/decide"]
    O --> P["YENI RunId, JobKind.ApprovalResume kuyruga duser<br/>ESKI calistirma AwaitingApproval'da SONSUZA KALIR - K-014"]

    style E fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style K fill:#7a4a1f,stroke:#3d250f,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| İptal düğmesi, etki tooltip’i (Faz 175: `window.confirm` kalktı), durum rozeti geçişleri (arayüz) | `11-ARAYUZ-RUN-SESSION-SSE.md` `MT-UIRUN-021`–`025` (zaten üretildi) — **burada TEKRARLANMAZ** |
| İş kuyruğu seviyesinde `Pending`/`Running`/`Queued` iptali, `Prefer: respond-async` sözleşmesi | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` `MT-JOB-040`–`044`, `070`–`084` (zaten üretildi) |
| `Idempotency-Key` sözleşmesi (dört durum, `400`/`422`, saklama) | `07-HTTP-YONETIM-API.md` `MT-API-030`–`033` (zaten üretildi) |
| `/api/stats/errors` panosu, `ProviderError`/`ToolError` gerçek örnekleri, kırılım toplamı | `07` `MT-API-040/041`, `12-GOZLEMLENEBILIRLIK-MALIYET.md` `MT-OBS-010` (zaten üretildi) — bu dosya yalnız `Canceled`/`Infrastructure` sınıflarını EKLER |
| Yeniden oynatma paneli, karşılaştırma paneli, dallandırma düğmesi (arayüz), `422`/`409` UI davranışı | `11` `MT-UIRUN-026`–`046`, `10-ARAYUZ-AGENT-PLAYGROUND.md` `MT-UIAG-039` (zaten üretildi) |
| `POST /runs/{id}/replay` kapsam denetimi (`Operator`/`Admin`), `feedback`/`compare`/`input` uçlarının kapsam boşluğu | `17-EVAL-VE-DENEYLER.md` `MT-EVAL-093/094/101` (zaten üretildi) |
| Genel HTTP zarfı (`ProblemDetails`), kiracı yalıtımı (genel mekanizma), API anahtarı oluşturma deseni | `07`, `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada yalnız §3'ün onay-kutusuna özgü kapsam/kiracı boşlukları için TEKRAR kullanılır |
| Rol politikalarının (`TraconPolicies`) örnek uygulamada kayıtlı olmadığı, dolayısıyla `RequireRole`'ün no-op olduğu genel bulgu | `14-SKILL-VE-SCRIPT.md` (zaten not düşüldü) — bu dosyanın `MT-RES-029`'u AYNI kök nedeni Onay uçlarında DOĞRULAR, yeniden araştırmaz |
| `document_embeddings`/`run_inputs` gibi diğer saklama hedeflerinin retention ile silinmesi | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) — bu dosya `pending_approvals`/`run_heartbeats` için bir saklama hedefi de ARAMAZ (kod okumasıyla: `RetentionTargets.cs`'te bu iki tablo için giriş yok — bkz. not §2 sonu) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama PostgreSQL ile çalışır (varsayılan kurulum, §2.4).
3. `Tracon:Providers:OpenAI:ApiKey` tanımlı — §1 ve §5 gerçek model çağırır
   (`FIX-PROMPT-04`, 50.000 karakter, uzun bir çalıştırma penceresi açmak için;
   teknik `11-ARAYUZ-RUN-SESSION-SSE.md`'nin `MT-UIRUN-021`'iyle AYNIDIR).
4. §2 ve §5, `RunReconciliation`'ı GEÇİCİ olarak açık ve kısa aralıklarla
   çalıştırmayı ister; §3'ün `MT-RES-027`'si `Approvals` süre sonunu benzer
   şekilde kısaltır. Bu ayarlar case içinde verilir; **dosyanın sonunda geri
   alınmalı** (aksi halde sonraki dosyaların koşumu sırasında arka planda
   gereksiz tarama sorguları atılır):
   ```bash
   dotnet user-secrets remove "Tracon:RunReconciliation:Enabled"
   dotnet user-secrets remove "Tracon:RunReconciliation:HeartbeatInterval"
   dotnet user-secrets remove "Tracon:RunReconciliation:OrphanThreshold"
   dotnet user-secrets remove "Tracon:RunReconciliation:ScanInterval"
   dotnet user-secrets remove "Tracon:Approvals:DefaultExpiration"
   dotnet user-secrets remove "Tracon:Approvals:ScanInterval"
   ```
5. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
```

> **Gerçek para uyarısı.** §1'in `MT-RES-001`/`002`/`005` ve §5'in
> `MT-RES-050`'si `FIX-PROMPT-04`'ü (50.000 karakter) gerçek bir modele
> gönderir — her biri tek ama pahalı sayılabilecek bir çağrıdır (girdi
> token'ı yüksek). §3'ün onay case'leri kısa gerçek çağrılardır (`support` +
> `cancel_order`). §2 ve §4 hiçbir model çağırmaz — yalnız SQL/HTTP/arayüz.

---

# 1 — Çalıştırma İptali: Kayıt Defteri ve Sınır Durumları (Faz 32)

`IRunCancellationRegistry` bellek-içi bir `ConcurrentDictionary`dir
(`RunCancellationRegistry.cs`). `TryCancel`, yalnız `RunId == RootRunId` olan
bir kayıt için ağaca yayılır — kayıttaki her adayın `Source.Cancel()`'ini
çağırır, **audit izine hiçbir şey yazmaz** (audit yalnız HTTP ucunda,
istek yapılan `runId` için bir kez yazılır). Bir alt çalıştırmanın TEK BAŞINA
iptali (`RunId != RootRunId`) kardeşleri veya kökü ETKİLEMEZ — kod bunu
açıkça yorumluyor: *"kök o dalı Failed görür, kendisi durmaz"*.

### MT-RES-001 — Kök çalıştırmanın iptali GERÇEKTEN çalışan bir alt çalıştırmayı da durdurur; audit YALNIZ kök için yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | K-243 |

**Ön koşul**
- Örnek uygulama çalışıyor, `yonlendirici` agent'ı `support`'u alt agent
  olarak çağırabiliyor (sabit kurulum).

**Adımlar**
1. `yonlendirici`'ye, `support`'a yönlendirilecek uzun bir metin gönder
   (arka planda, akış sürerken devam et).
2. Akış başladıktan ~1 sn sonra kök `runId`'yi ilk SSE çerçevesinden al,
   `GET /api/runs/{rootRunId}/tree` ile ağacı sorgula; `support`'un alt
   çalıştırması `Running` görünene kadar tekrar et (en fazla 5 sn).
3. `POST /api/runs/{rootRunId}/cancel`.
4. 3 saniye bekle, kök ve alt çalıştırmanın durumunu tekrar oku.
5. `GET /api/audit/run:{rootRunId}` ve `GET /api/audit/run:{childRunId}`.

**Girilecek veri**
```bash
RESP=$(curl -s -D - -o /tmp/mt-res-001.json -X POST "$APU/api/agents/yonlendirici/run" \
  -H "$APB" -H "content-type: application/json" \
  -d "{\"message\":\"Destek ekibine yonlendir ve asagidaki metni ozetlemesini iste: $(python3 -c "print('lorem ipsum dolor sit amet ' * 2000)")\"}" &)
sleep 1
ROOT=$(python3 -c "import json;print(json.load(open('/tmp/mt-res-001.json'))['runId'])" 2>/dev/null)

for i in 1 2 3 4 5; do
  TREE=$(curl -s "$APU/api/runs/$ROOT/tree" -H "$APB")
  CHILD=$(echo "$TREE" | python3 -c "import json,sys; r=json.load(sys.stdin); c=[x for x in r if x['id']!='$ROOT']; print(c[0]['id'] if c and c[0]['status']=='Running' else '')")
  [ -n "$CHILD" ] && break
  sleep 1
done
echo "root=$ROOT child=$CHILD"

curl -s -i -X POST "$APU/api/runs/$ROOT/cancel" -H "$APB"
sleep 3
curl -s "$APU/api/runs/$ROOT" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
curl -s "$APU/api/runs/$CHILD" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
curl -s "$APU/api/audit/run:$ROOT" -H "$APB"
curl -s "$APU/api/audit/run:$CHILD" -H "$APB"
```

**Beklenen sonuç**
- Adım 3: `202`.
- Adım 4: hem kök hem alt çalıştırma `Canceled` olur (registry'nin
  `RootRunId` eşleşen TÜM kayıtları kaskad ettiği iddiası, `RunCancellationRegistry.TryCancel`).
- Adım 5: `run:{rootRunId}` denetim izinde bir `run.cancel` kaydı VARDIR;
  `run:{childRunId}` denetim izinde bir `run.cancel` kaydı **YOKTUR** —
  çocuğun iptali doğrudan `CancellationTokenSource.Cancel()` iledir, ikinci
  bir HTTP çağrısı/audit yazımı yoktur.

---

### MT-RES-002 — Yalnız bir alt çalıştırmanın iptali kökü ve kardeşleri ETKİLEMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | K-243 |

**Ön koşul**
- `MT-RES-001`'in adım 1-2'si (kök + çalışan bir alt çalıştırma) tekrarlanır,
  YENİ bir kök/alt çift için.

**Adımlar**
1. Alt çalıştırmanın kimliğini (`childRunId`) doğrudan iptal et — kökü DEĞİL.
2. 3 saniye bekle, kökün ve alt çalıştırmanın durumunu oku.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/runs/$CHILD/cancel" -H "$APB"
sleep 3
curl -s "$APU/api/runs/$ROOT" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
curl -s "$APU/api/runs/$CHILD" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- Alt çalıştırma `Canceled` olur.
- Kök çalıştırma **DURMAZ** — `Running` kalır (veya normal akışıyla devam
  edip sonunda `Completed`/`Failed` olur, ama iptalden dolayı DEĞİL). Alt
  çağrının kesilmesinin köke nasıl yansıdığı (hata mesajı, boş sonuç) gerçek
  koşumda gözlemlenip buraya yazılır — kod bunun ötesinde bir garanti
  vermiyor.

---

### MT-RES-003 — 🚨 `Running` bir çalıştırmanın `202` gövdesi HÂLÂ eski durumu taşır (`Queued` dalının aksine)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 32, 46 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-04` ile `support`'a süren bir çalıştırma başlatılmış.

**Adımlar**
1. `POST /api/runs/{runId}/cancel` yanıtının gövdesindeki `status` alanına
   bak — istek dönmeden HEMEN önce.
2. Aynı anda `Prefer: respond-async` ile kuyruğa alınmış (`Queued`) bir
   çalıştırmayı iptal et, gövdesindeki `status`'a bak.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/runs/$RUNNING_ID/cancel" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"

curl -s -D - -o /tmp/mt-res-003.json -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1002 siparisim nerede?"}'
QID=$(python3 -c "import json;print(json.load(open('/tmp/mt-res-003.json'))['runId'])")
curl -s -X POST "$APU/api/runs/$QID/cancel" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- Adım 1: gövde `status: "Running"` yazar — `RunEndpoints.CancelRunAsync`'in
  `Running` dalı `TypedResults.Accepted($"...", run)` ile OKUNMUŞ (eski) kaydı
  döner, `Canceled` yazmaz. Nihai durum yalnız sonraki bir `GET` ile görülür.
- Adım 2: gövde `status: "Canceled"` yazar — `Queued` dalı
  `run with { Status = Canceled, CompletedAt = now }` ile GÜNCEL kaydı döner.
- İki dal aynı uçtur (`/cancel`) ama gövde tazeliği FARKLIDIR; istemci bu
  asimetriyi bilmeden yanlış varsayımda bulunabilir.

---

### MT-RES-004 — 🚨 Süreç yeniden başladıktan sonra eski bir `Running` satırın iptali `409` döner ("bu örnekte yürütülmüyor")

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-04` ile süren bir çalıştırma var (`runId` not edilmiş).

**Adımlar**
1. Çalıştırma sürerken uygulamayı `Ctrl+C` ile durdur (registry bellek-içi
   olduğu için tüm kayıtlar kaybolur; `runs` satırı DB'de `Running` kalır).
2. Uygulamayı yeniden başlat.
3. Aynı `runId` için `POST /api/runs/{runId}/cancel` çağır.

**Girilecek veri**
```bash
# (adim 1-2: elle - Ctrl+C, sonra `dotnet run`)
curl -s -i -X POST "$APU/api/runs/$RUNNING_ID/cancel" -H "$APB"
```

**Beklenen sonuç**
- `409 Conflict`.
- `title`: `"Calistirma bu ornekte yurutulmuyor"`.
- `detail` şu ifadeyi içerir: *"...'Running' gorunuyor ama bu surecte kayitli
  degil. Baska bir ornekte calisiyor olabilir veya surec calistirma sirasinda
  yeniden baslamis olabilir."*
- Bu, "zaten sonlanmış" `409`'undan (`MT-UIRUN-023`, `11` dosyasında) FARKLI
  bir `title` taşıyan ikinci bir `409` yoludur — aynı durum kodu, farklı kök
  neden.

---

### MT-RES-005 — 🚨 Workflow çalıştırması gerçekten iptal edilebiliyor mu (Faz 32 kapanışında KANITLANAMAMIŞ boşluğun denemesi)

`docs/arsiv/fazlar/32-CALISTIRMA-IPTALI.md`'nin "Plandan Sapmalar" bölümü açıkça şunu
kaydeder: `WorkflowRunner`'ın kayıt/bırakma teli `RunRecordingAgent` ile
birebir aynı desendedir ve derleniyor, ama **gerçek bir workflow
çalıştırmasıyla hiç kanıtlanmadı** — o fazın kendi denemesi MAF'ın
`AgentWorkflowBuilder.BuildSequential` grafiğinin özel bir `AIAgent` alt
sınıfını nasıl tükettiğiyle ilgili bir nedenle `Canceled` yerine
`Completed` yazdı ve test silindi. Bu case AYNI denemeyi gerçek bir kayıtlı
workflow'la (`ozetle-ve-cevir`) tekrarlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 32, 15 |
| **İlgili karar** | K-245 |

**Ön koşul**
- Örnek uygulama çalışıyor; `ozetle-ve-cevir` workflow'u kayıtlı (sabit).

**Adımlar**
1. `ozetle-ve-cevir`'i `FIX-PROMPT-04` ile başlat (arka planda, SSE akışı
   sürerken kimliği yakala).
2. Akış sürerken kimliği `POST /api/runs/{runId}/cancel` ile iptal et.
3. 3 saniye bekle, `GET /api/runs/{runId}` ile durumu oku.

**Girilecek veri**
```bash
curl -s -N -X POST "$APU/api/workflows/ozetle-ve-cevir/run" \
  -H "$APB" -H "content-type: application/json" \
  -d "{\"message\":\"$(python3 -c "print('lorem ipsum dolor sit amet ' * 2000)")\"}" > /tmp/mt-res-005-stream.txt &
sleep 1
WFRUN=$(grep -m1 '"runId"' /tmp/mt-res-005-stream.txt | python3 -c "import json,sys;print(json.loads(sys.stdin.readline().split('data: ',1)[1])['runId'])")
curl -s -i -X POST "$APU/api/runs/$WFRUN/cancel" -H "$APB"
sleep 3
curl -s "$APU/api/runs/$WFRUN" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
> 🚨 **Beklenti 2026-08-18'de DEĞİŞTİ (F-107 kapatıldı, K-432).** Kök neden
> ölçüldü ve kaydedilenden daha keskin çıktı: MAF grafiği iptal token'ını honor
> etmiyor **ve istisna da atmıyor** — akışı **sessizce** bitiriyor, bu yüzden
> pompanın iptal dalı hiç çalışmıyor ve varsayılan `Completed` olduğu gibi
> kalıyordu. `WorkflowRunner` artık iptali kendisi zorluyor: her süper-adım
> sınırında MAF'ın kendi `CancelRunAsync` yolunu çağırıyor ve pompa çıkışında
> `Completed` görürse `Canceled`'a çeviriyor. Süreç içi düşen test
> (`WorkflowCancellationTests`) düzeltmeden önce kırmızıydı.
- `POST /api/runs/{runId}/cancel` **202** döner.
- 3 saniye sonra `GET /api/runs/{runId}`'in durumu **`Canceled`**'dır.
- 🚨 `error` alanı yine `null` olabilir (bkz. MT-RES-006); iptal bir hata
  değildir.

~~Eski beklenti (2026-08-15 koşumu): `Completed` — MAF'ın grafik-içi iptal
sınırı nedeniyle. Bu sınır hâlâ vardır ama artık Tracon onu kaydına
yansıtmıyor.~~

---

### MT-RES-006 — 🚨 İptal edilen bir çalıştırmanın `error` alanı hep `null` kalır; `RunErrorClass.Canceled` PRATİKTE hiç üretilmez (şüpheli ölü kod)

`RunRecordingAgent`'ın `OperationCanceledException` yakalayan iki noktası
(satır 241-243, 323-325) `CompleteAsync(scope, RunStatus.Canceled, null,
null, ...)` çağırır — dördüncü parametre (`RunError? error`) HER İKİSİNDE de
`null`'dur. `CompleteAsync`'in kendi yorumu açık: *"Siniflandirici YALNIZ
hata varken cagrilir: basarili bir calistirmada (error is null) sicak yolda
hic tetiklenmez."* (satır 738-744). `DefaultRunErrorClassifier.Classify`
içinde `RunErrorClass.Canceled`'i döndüren bir dal VARDIR
(`DefaultRunErrorClassifier.cs:61`) ama bu dala giren tek yol `Classify`'ın
çağrılmasıdır — ki iptalde asla çağrılmaz. Bu, `00-INDEKS.md`'nin daha önce
kaydettiği `QuotaDecision.CostFellBackToTokens` ölü-kod bulgusuyla AYNI
sınıftan bir şüphe.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 44, 32 |
| **İlgili karar** | — |

**Ön koşul**
- Az önce en az bir çalıştırma iptal edilmiş olmalı (`MT-RES-001`–`004`'ten
  biri yeterli).

**Adımlar**
1. `GET /api/runs/{iptalEdilenRunId}` — `error` alanına bak.
2. `GET /api/stats/errors?hours=1` — `class: "Canceled"` taşıyan bir küme
   ARANIR.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/$CANCELED_RUN_ID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin).get('error'))"
curl -s "$APU/api/stats/errors?hours=1" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç (şüphe)**
- Adım 1: `error: null`.
- Adım 2: çıktıda `"class": "Canceled"` taşıyan HİÇBİR öge YOKTUR — az önce
  gerçekten iptal edilmiş çalıştırmalar olmasına rağmen.
- Doğrularsa: **kusur değil, ölü kod** — `RunErrorClass.Canceled` enum
  üyesi ve classifier'ın onu üreten dalı hiçbir zaman çalışmayan, kaldırılması
  veya (iptal tamamlanmasında `error` doldurulacak şekilde) etkinleştirilmesi
  gereken bir kod yolu. `00-INDEKS.md`'ye not düşülür.

### MT-RES-010 — Varsayılan kapalı: `Enabled=false` iken eski bir `Running` satır asla kapanmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 54 |
| **İlgili karar** | K1 (varsayılan kapalı) |

**Ön koşul**
- `Tracon:RunReconciliation:*` ayarlarının HİÇBİRİ verilmemiş (varsayılan).
- Herhangi bir tamamlanmış çalıştırmanın `runId`'si not edilmiş.

**Adımlar**
1. O satırı doğrudan SQL ile "10 dakika önce başlamış, hâlâ çalışıyor" gibi
   göster.
2. 10 saniye bekle.
3. Durumu tekrar oku.

**Girilecek veri**
```bash
$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SOME_RUN_ID';"
sleep 10
curl -s "$APU/api/runs/$SOME_RUN_ID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- `status: "Running"` — hiçbir arka plan taraması çalışmadığı için satır
  SONSUZA kadar `Running` görünür kalır (yeniden reset yordamı uygulanana
  kadar).

---

### MT-RES-011 — Açıldığında: heartbeat'i geride bırakılan bir `Running` satır eşik aşılınca `Failed`/`Infrastructure`/`orphaned` olur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 54 |
| **İlgili karar** | K-362, K-363, K-364 |

**Ön koşul**
- Yukarıdaki `RunReconciliation` ayarları (`Enabled=true`,
  `HeartbeatInterval=1sn`, `OrphanThreshold=3sn`, `ScanInterval=1sn`)
  verilmiş; uygulama BU ayarlarla yeniden başlatılmış.
- `MT-RES-010`'un aynı satırı (veya yeni bir tamamlanmış çalıştırma) yeniden
  "10 dakika önce başlamış" hâline getirilmiş.

**Adımlar**
1. Satırı geçmişe al (yukarıdaki `UPDATE`).
2. En fazla 5 saniye bekle (tarama aralığı 1 sn, eşik 3 sn).
3. Durumu ve `error` alanını oku.
4. Sunucu günlüğünde `RunReconciliationService`'in uyarı satırını ara.

**Girilecek veri**
```bash
$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SOME_RUN_ID';"
sleep 5
curl -s "$APU/api/runs/$SOME_RUN_ID" -H "$APB" | python3 -c "import json,sys; r=json.load(sys.stdin); print(r['status'], r.get('error'))"
```

**Beklenen sonuç**
- `status: "Failed"`.
- `error.type == "orphaned"`.
- `error.class == "Infrastructure"`.
- `error.fingerprint == "orphaned"` (sabit dize, K-364 — `ErrorFingerprint`
  hesaplanmaz; `Tracon.Sql.Shared` `Tracon.Core`'daki `internal`
  sınıfa erişemediği için).
- `error.message` `"...son isaret: <tarih>..."` biçiminde bir metin taşır.

**Doğrulama sorgusu**
```sql
SELECT status, error_type, error_class, error_fingerprint, error_message
  FROM tracon.runs WHERE id = '<runId>';
```

---

### MT-RES-012 — Uzlaştırma yalnız `Running` süzer; `Queued` bir satıra DOKUNMAZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 54, 46 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-011`'in ayarları hâlâ açık.

**Adımlar**
1. `Prefer: respond-async` ile bir çalıştırma kuyruğa al (`Queued` olarak
   açılır).
2. O satırın `started_at`'ini de geçmişe al ama `status`'u DEĞİŞTİRME
   (`5` = `Queued` kalsın).
3. 5 saniye bekle, durumu oku.

**Girilecek veri**
```bash
RESP=$(curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1001 siparisim nerede?"}')
QID=$(echo "$RESP" | python3 -c "import json,sys;print(json.load(sys.stdin)['runId'])")

$PG -c "UPDATE tracon.runs SET started_at = now() - interval '10 minutes'
        WHERE id = '$QID' AND status = 5;"
sleep 5
curl -s "$APU/api/runs/$QID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- `status` hâlâ `"Queued"`dur (veya işçi normal şekilde alıp `Completed`
  etmiştir) — ASLA `Failed`/`Infrastructure` olmaz. Uzlaştırma sorgusu
  `WHERE status = 0` (yalnız `Running`) filtresi taşır
  (`PostgresQueries.cs:333` civarı); `Queued` bir kova bile değildir.

---

### MT-RES-013 — Uzlaştırma sonrası `GET /api/runs?status=Running` boş döner; panoda `Infrastructure` sınıfı görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 54, 44 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-011` çalıştırılmış, en az bir satır `Infrastructure` ile kapanmış.

**Adımlar**
1. `GET /api/runs?status=Running` — dizi uzunluğuna bak.
2. `GET /api/stats/errors?hours=1` — `class: "Infrastructure"` kümesine bak.
3. Gösterge panelinde ("Error breakdown", `12-GOZLEMLENEBILIRLIK-MALIYET.md`
   `MT-OBS-010`'un aynı bileşeni) `Infrastructure` satırının göründüğünü
   ekran görüntüsüyle doğrula.

**Girilecek veri**
```bash
curl -s "$APU/api/runs?status=Running" -H "$APB" | python3 -c "import json,sys;print(len(json.load(sys.stdin)))"
curl -s "$APU/api/stats/errors?hours=1" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Adım 1: `0` (uç bir dizi döner, `{items:[...]}` DEĞİL — Faz 54'ün kendi
  kanıtının düzelttiği aynı ayrıntı).
- Adım 2: `"class": "Infrastructure"` taşıyan bir küme vardır,
  `sampleMessage` `"...yanit vermiyor..."` içerir.
- Adım 3: dashboard'un genel bileşeni yeni bir sınıf değeri için KOD
  değişikliği gerektirmez (`RunErrorClass` bir string-enum, arayüz listeyi
  döngüyle basar) — bu doğrulanır.

---

### MT-RES-014 — (opsiyonel, iki örnek) `SingletonExecution` kapalıyken uzlaştırma HER örnekte bağımsız koşar; açıldığında tekilleşir

Bu case iki terminal ister; zaman bütçesi dar ise `⏭ Atlandı` işaretlenip
gerekçe (§7 kuralınca) not düşülebilir — geri kalan case'lerin hiçbiri buna
bağımlı değildir.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 54, 42 |
| **İlgili karar** | — |

**Ön koşul**
- `RunReconciliation` ayarları açık (yukarıdaki gibi).
- `Tracon:SingletonExecution:Enabled` HENÜZ verilmemiş (varsayılan
  `false` — `SingletonGuard.IsHeld` her zaman `true` döner, depoya hiç
  sorgu gitmez).

**Adımlar**
1. İkinci bir örneği farklı bir portta başlat (aynı PostgreSQL'e bağlı).
2. İki örnek de açıkken bir satırı geçmişe al (`MT-RES-011`'deki gibi).
3. Sunucu günlüklerinde İKİ örneğin de "1 oksuz calistirma kapatildi"
   uyarısını yazıp yazmadığına bak (ikisi de aynı satırı görüp `UPDATE`
   denemesi yapabilir; SQL'in kendisi idempotent olduğu için veri bozulmaz
   ama İKİ log satırı beklenir).
4. `Tracon:SingletonExecution:Enabled=true` ekleyip HER İKİ örneği de
   yeniden başlat, adım 2-3'ü tekrarla.

**Girilecek veri**
```bash
# ikinci ornek (ayrı terminal, aynı user-secrets kimligini paylasir):
cd samples/Tracon.Api && dotnet run --urls http://localhost:5081

# adim 4:
dotnet user-secrets set "Tracon:SingletonExecution:Enabled" "true"
```

**Beklenen sonuç**
- Adım 3 (kapalı): iki örnek de bağımsız tarar; ikisinin de günlüğünde
  uyarı görülebilir (kesin garanti yok — yarış koşuluna bağlı, ama en az bir
  örnek satırı kapatır).
- Adım 4 (açık): yalnız kirayı TUTAN örneğin günlüğünde uyarı görünür;
  diğeri sessiz kalır (`SingletonGuard.IsHeld == false` iken
  `RunReconciliationService`'in tur döngüsü `IsHeld` denetiminde erken çıkar).

### MT-RES-020 — Kuyruğa alınan bir çalıştırma onay gerektiren tool çağırınca `AwaitingApproval`'a düşer, `pending` listede görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 55, 46 |
| **İlgili karar** | K-368 |

**Ön koşul**
- Örnek uygulama çalışıyor; `Tracon:Providers:OpenAI:ApiKey` tanımlı
  (`support` gerçek modelle `cancel_order` tool'unu çağırmalı).

**Adımlar**
1. `support`'a `FIX-PROMPT-03` (`ORD-1001 siparisimi iptal et`) mesajını
   `Prefer: respond-async` ile gönder.
2. `GET /api/runs/{runId}` ile durumun `AwaitingApproval`'a düştüğünü
   doğrula (birkaç saniye içinde).
3. `GET /api/approvals/pending` ile listeyi al, ilgili kaydı bul.
4. `GET /api/approvals/{id}` ile tekil kaydı al.

**Girilecek veri**
```bash
RESP=$(curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1001 siparisimi iptal et"}')
RUN=$(echo "$RESP" | python3 -c "import json,sys;print(json.load(sys.stdin)['runId'])")

sleep 3
curl -s "$APU/api/runs/$RUN" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"

curl -s "$APU/api/approvals/pending" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Adım 2: `status: "AwaitingApproval"`.
- Adım 3: dizide `runId` alanı yukarıdaki `RUN` ile eşleşen tam bir kayıt
  var: `toolName: "cancel_order"`, `arguments` `"orderId=ORD-1001"` içerir
  (JSON DEĞİL, "anahtar=deger" biçiminde — AOT gerekçesiyle), `status:
  "Pending"`, `expiresAt` `createdAt`'ten `~24 saat` sonra (varsayılan
  `DefaultExpiration`).
- Adım 3 (Faz 142): kayıt ayrıca `presentation` alanı taşır —
  `samples/Tracon.Api/OrderApprovalPresenter.cs` `ORD-1001`'i çözer:
  `presentation.entityType: "order"`, `entityId: "ORD-1001"`, `entityName:
  "Order ORD-1001"`, `message` "Priya Shah" adını içerir. Ham `arguments`
  alanı DEĞİŞMEDEN kalır — sunum onun yerine geçmez.
- Adım 4: aynı kayıt tekil `GET` ile de gelir.
- (Faz 142) `GET /api/runs/{runId}/events` akışının SON olayı
  `RunAwaitingInput`'tur; `payload` alanı `requestId`, `toolName` ve aynı
  `entityName`/`message` çiftini JSON dizi olarak taşır.

---

### MT-RES-021 — Onaylama: YENİ bir `RunId` kuyruğa düşer, işçi alınca `Completed` olur; ESKİ çalıştırma SONSUZA `AwaitingApproval` kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | K-368, K-305 (AYNI ilke: `Job.Id == Run.Id`) |

**Ön koşul**
- `MT-RES-020`'nin bekleyen onayı (`approvalId`, `runId`) elde.

**Adımlar**
1. `POST /api/approvals/{id}/decide` `{"approved": true}`.
2. Yanıttaki `status`'a bak.
3. 3 saniye bekle, ESKİ `runId`'nin durumunu tekrar oku.
4. `GET /api/runs?sessionId={sessionId}` ile aynı oturuma bağlı çalıştırmaları
   listele; YENİ bir `runId`'nin `Completed` olduğunu bul.
5. Yeni çalıştırmanın olay akışında `get_order_status`/`cancel_order`
   tool'unun GERÇEKTEN çalıştığını doğrula (Faz 55'in kendi kanıtındaki gibi).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/approvals/$APPROVAL_ID/decide" -H "$APB" \
  -H "content-type: application/json" -d '{"approved":true}' | python3 -m json.tool

sleep 3
curl -s "$APU/api/runs/$RUN" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"

curl -s "$APU/api/runs?sessionId=$SESSION_ID" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Adım 2: `200`, gövde `status: "Approved"`, `decidedBy` dolu, `decidedAt`
  dolu.
- Adım 3: ESKİ `runId` HÂLÂ `AwaitingApproval`'dır — asla değişmez (K-014).
- Adım 4: aynı `sessionId`'de YENİ bir `runId` görünür, birkaç saniye içinde
  `Completed` olur.
- Adım 5: yeni çalıştırmanın olay akışında `cancel_order` tool çağrısı
  GERÇEKTEN işlenmiştir (sipariş gerçekten iptal edilmiş gibi bir yanıt).

**Doğrulama sorgusu**
```sql
SELECT id, status FROM tracon.runs
 WHERE session_id = '<sessionId>' ORDER BY started_at;
```

---

### MT-RES-022 — Reddetme: karar reddedilirse yeni çalıştırma modelin reddi gördüğünü yansıtır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | K-368 |

**Ön koşul**
- Yeni bir `FIX-PROMPT-03` çalıştırması ile taze bir bekleyen onay üretilmiş
  (`MT-RES-020`'nin adımları tekrarlanır, YENİ bir onay için).

**Adımlar**
1. `POST /api/approvals/{id}/decide` `{"approved": false}`.
2. Yeni çalıştırmanın nihai yanıt metnine bak (arayüzden veya
   `GET /api/runs/{yeniRunId}/tree` + olay akışından).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/approvals/$APPROVAL_ID/decide" -H "$APB" \
  -H "content-type: application/json" -d '{"approved":false}'
```

**Beklenen sonuç**
- `200`, gövde `status: "Rejected"`.
- Yeni çalıştırma tamamlanır; model kullanıcıya siparişin iptal
  EDİLMEDİĞİNİ ileten bir yanıt üretir (senkron `ToolApprovalResolver`
  yolundaki reddetme davranışıyla AYNI ilke — metin eşleşmesi değil,
  `cancel_order`'ın gerçekten çalışmadığının doğrulanması aranır).

---

### MT-RES-023 — Aynı onaya ikinci karar `409` döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-021`'in kararı verilmiş bir `approvalId`.

**Adımlar**
1. Aynı `approvalId`'ye ikinci kez `POST .../decide` gönder (bu kez ters
   kararla, `{"approved": false}`).

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/approvals/$APPROVAL_ID/decide" -H "$APB" \
  -H "content-type: application/json" -d '{"approved":false}'
```

**Beklenen sonuç**
- `409 Conflict`.
- `title`: `"Karar zaten verilmis"`.
- `detail` `"...artik bekliyor durumunda degil."` içerir.
- İkinci bir `RunId`/iş KUYRUĞA DÜŞMEZ (`GET /api/runs?sessionId=...`
  sayısı DEĞİŞMEZ).

---

### MT-RES-024 — Var olmayan onay kimliği `404` döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- Yok.

**Adımlar**
1. Rastgele bir `guid` ile `GET /api/approvals/{id}`.
2. Aynı kimlikle `POST /api/approvals/{id}/decide`.

**Girilecek veri**
```bash
FAKE="00000000-0000-0000-0000-000000000000"
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/approvals/$FAKE" -H "$APB"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/approvals/$FAKE/decide" \
  -H "$APB" -H "content-type: application/json" -d '{"approved":true}'
```

**Beklenen sonuç**
- İkisi de `404`, `title`: `"Onay istegi bulunamadi"`.

---

### MT-RES-025 — Başka kiracının onayı `404` döner (varlığı sızdırmaz)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 55, 41 |
| **İlgili karar** | — |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md`'nin `X-Tracon-Tenant` başlığıyla kiracı
  çözümü açık olduğu kurulum bilinir (`AllowHeaderResolution`).
- `FIX-TENANT-01` (`kiraci-alfa`) altında bir bekleyen onay üretilmiş
  (`MT-RES-020`'nin adımları `X-Tracon-Tenant: kiraci-alfa` başlığıyla).

**Adımlar**
1. `FIX-TENANT-02` (`kiraci-beta`) başlığıyla aynı `approvalId`'yi `GET` et.
2. Aynı başlıkla `decide` çağır.
3. Kontrol: `kiraci-alfa` başlığıyla aynı istekler `200` alır.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/approvals/$APPROVAL_ID" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-beta"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/approvals/$APPROVAL_ID/decide" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-beta" -H "content-type: application/json" -d '{"approved":true}'

curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/approvals/$APPROVAL_ID" \
  -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- Adım 1-2: `404` (`403` DEĞİL — varlığı sızdırmaz, `NotFound` yardımcı
  metodu ile aynı desen `RunEndpoints.CancelRunAsync`'teki "yok ile başka
  kiracıya ait aynı 404" ilkesini tekrarlar).
- Adım 3: `200`.

---

### MT-RES-026 — Karar `approval.decision` eylemiyle, `tool:{toolName}` varlığıyla denetim izine yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 55, 9 |
| **İlgili karar** | K-089, K-370 |

**Ön koşul**
- Yeni bir onay kararı verilmiş.

**Adımlar**
1. `GET /api/audit/tool:cancel_order` ile denetim izini sorgula.
2. `action`/`after` alanlarına bak.

**Girilecek veri**
```bash
curl -s "$APU/api/audit/tool:cancel_order" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Bir kayıt vardır: `action: "approval.decision"`, `entity:
  "tool:cancel_order"`.
- `after` alanı `{"approved": true/false, "approvalId": "<id>"}` biçiminde
  `secret` filtresinden geçirilmiş bir JSON dizgesi taşır (`ApprovalEndpoints
  .WriteAuditOrThrowAsync`, `AuditSecretFilter.Redact`).
- Bu yazım karardan ÖNCE olur (K-089); denetim izi yazılamasaydı karar hiç
  uygulanmazdı — bu dalın kendisi ayrı bir case OLARAK test EDİLMEZ (denetim
  izini bilinçli olarak bozmak bu ortamda pratik değil), yalnız KOD
  incelemesiyle doğrulanmış bir garanti olarak not düşülür.

---

### MT-RES-027 — Süresi dolan onay `Expired` olur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- Kısa süre sonu ayarları verilmiş, uygulama yeniden başlatılmış:
  ```bash
  dotnet user-secrets set "Tracon:Approvals:DefaultExpiration" "00:00:05"
  dotnet user-secrets set "Tracon:Approvals:ScanInterval" "00:00:02"
  ```
- Yeni bir bekleyen onay üretilmiş (`MT-RES-020`'nin adımları).

**Adımlar**
1. Karar VERMEDEN 10 saniye bekle.
2. `GET /api/approvals/{id}` ile durumu oku.
3. `GET /api/approvals/pending` listesinde göründüğüne bak.
4. Kararsız kalan onaya bağlı `decide` çağrısının artık ne döndüğüne bak.

**Girilecek veri**
```bash
sleep 10
curl -s "$APU/api/approvals/$APPROVAL_ID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
curl -s "$APU/api/approvals/pending" -H "$APB" | python3 -c "import json,sys;print(len(json.load(sys.stdin)))"
curl -s -i -X POST "$APU/api/approvals/$APPROVAL_ID/decide" -H "$APB" \
  -H "content-type: application/json" -d '{"approved":true}'
```

**Beklenen sonuç**
- Adım 2: `status: "Expired"`.
- Adım 3: `pending` listesinde ARTIK GÖRÜNMEZ (`ListPendingAsync` yalnız
  `Pending` durumundakileri döner).
- Adım 4: `409` (`AlreadyDecided` — `Status != Pending` denetimi `Expired`
  için de geçerli, `decide`'ın ilk kontrolü `approval.Status != Pending`).
- `ExpirationEnabled` varsayılan `true`'dur (bir güvenlik gereği,
  `RunReconciliation`'ın aksine); bu case bu varsayılanı DEĞİŞTİRMEZ, yalnız
  süreyi kısaltır.

---

### MT-RES-028 — 🚨 `ApprovalEndpoints` hiçbir ucunda `RequireApiKeyScope` çağırmaz — yalnız-okuma kapsamlı bir anahtar onay kararı verebiliyor mu

`grep -n "RequireApiKeyScope" src/Tracon.AspNetCore/Endpoints/
ApprovalEndpoints.cs` **boş** döner. `Workflow`/`Scheduling`/`Eval-Experiment`/
`Governance`/`Knowledge` uçlarında zaten defalarca ölçülen aynı desenin
(`00-INDEKS.md`'nin birikmiş notları) onay kutusundaki tekrarı — `AgentEndpoints
.cs`/`RunEndpoints.cs`'in aksine (`cancel` ucu `RequireApiKeyScope(ApiKeyScope
.RunsWrite)` TAŞIR, doğrulandı).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 55, 53 |
| **İlgili karar** | — |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md`'nin `MT-SEC-050` API anahtarı oluşturma deseni
  bilinir.
- Yeni bir bekleyen onay üretilmiş.

**Adımlar**
1. Yalnız `RunsRead` kapsamıyla bir API anahtarı üret.
2. Bu anahtarla bekleyen onayı `decide` etmeyi dene.
3. Kontrol grubu: aynı anahtarla `AgentsAdmin` gerektiren bir uca
   (`PUT /api/agents/{name}`) yaz, `403` aldığını doğrula.

**Girilecek veri**
```bash
KEY_JSON=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "onay-kapsam-testi", "scopes": ["RunsRead"] }')
RAW=$(echo "$KEY_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['rawKey'])")

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/approvals/$APPROVAL_ID/decide" \
  -H "Authorization: Bearer $RAW" -H "content-type: application/json" -d '{"approved":true}'

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/kapsam-kontrol" \
  -H "Authorization: Bearer $RAW" -H "content-type: application/json" \
  -d '{"name":"kapsam-kontrol","instructions":"test"}'
```

**Beklenen sonuç (şüphe)**
- Adım 2: `HTTP: 200` — kapsam kısıtı UYGULANMAZ (kodun okuduğu hâliyle
  beklenen).
- Adım 3: `HTTP: 403` — kontrol grubu, kapsam sisteminin `AgentEndpoints`'te
  çalıştığını ama `ApprovalEndpoints`'te HİÇ devrede olmadığını gösterir.
- Doğrularsa: **Kusur, Önem: Yüksek** — salt-okunur bir otomasyon anahtarı
  bekleyen bir siparişi iptal kararını (gerçek yan etkili bir tool'u)
  onaylayabilir/reddedebilir. Çürürse not güncellenir.

---

### MT-RES-029 — 🚨 Rol politikaları bu örnekte kayıtlı değil — "Reader karar veremez" iddiası bu ortamda GÖZLEMLENEMEZ

`ApprovalEndpoints.Map`, `/decide` ucuna `.RequireRole(roles.Operator)`
ekler. `RoleEndpointConventionBuilderExtensions.RequireRole` şu KOD YORUMUNU
taşır: *"policyName'in null olmasi, ilgili TraconPolicies policy'sinin
tuketicinin authorization yapilandirmasinda kayitli olmadigi anlamina gelir
— bu durumda uc yalnizca mevcut uc katmanli korumadan (loopback, bearer,
genel policy) gecer."* `grep -n "TraconPolicies\|AddAuthorization\|
RequireRolePolicies" samples/Tracon.Api/Program.cs` **boş** döner — bu
üçü de kayıtlı değil. Sonuç: `TraconRolePolicies.Resolve` `Operator`
alanını `null` çözer ve `RequireRole` bu uçta HİÇBİR yetkilendirme
EKLEMEZ. Bu, `14-SKILL-VE-SCRIPT.md`'nin daha önce Skill uçları için
kaydettiği AYNI kök nedenin onay kutusundaki DOĞRULAMASIdır.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 55, 53 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir bekleyen onay üretilmiş. Statik bearer token (`FIX-TOKEN-01`)
  dışında bir kimlik doğrulama şeması YOKTUR — örnek uygulamada "Reader"
  rolüne bağlı ayrı bir token üretmenin bir yolu yok.

**Adımlar**
1. Elde var olan TEK bearer token'la (`FIX-TOKEN-01`, hangi rolü temsil
   ettiği tanımsız çünkü rol claim'i hiç üretilmiyor) `decide` çağır.
2. Sonucu Faz 55'in kendi DoD iddiasıyla (`Reader_rolu_karar_veremez`,
   izole bir fonksiyonel test host'unda `TraconPolicies` KAYITLI olarak
   çalıştırılmıştı) karşılaştır.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/approvals/$APPROVAL_ID/decide" \
  -H "$APB" -H "content-type: application/json" -d '{"approved":true}'
```

**Beklenen sonuç (bilinen ortam kısıtı — kusur DEĞİL)**
- `200` — statik token her role açık uçlara erişir; bu ortamda "Reader"
  rolünü temsil eden ayrı bir kimlik YOK, dolayısıyla `403` beklentisi bu
  senaryoda gözlemlenemez.
- Bu bir kusur DEĞİLDİR: `samples/Tracon.Api` bilinçli olarak tek bir
  statik operatör token'ı ile kurulmuştur; rol ayrımı test etmek özel bir
  kimlik doğrulama şeması (rol claim'i üreten bir test handler'ı) ister —
  `14-SKILL-VE-SCRIPT.md`'nin de kasıtlı olarak atladığı aynı sınır.
  **Durum:** `⏭ Atlandı` olarak işaretlenir, gerekçe bu satırdır.

---

### MT-RES-030 — Ekler veya onay kararı ile birlikte gönderilen `respond-async` isteği `400` alır (kuyruklu çalıştırmada onay desteklenmez)

Bu, Faz 46'nın kendi "Plandan Sapmalar" maddesinin (`AttachmentIds`/
`Approvals` alanları kuyruğa alınan çalıştırmalarda desteklenmiyor) Faz 55
onay kutusuyla KARIŞTIRILMAMASI gereken, FARKLI bir kapsamdır: bu case
`Prefer: respond-async` isteğinin GÖVDESİNDE `approvals` alanı taşıması
(senkron akıştaki `ToolApprovalResolver` kararı gibi) durumunu test eder —
`pending_approvals`'ın kendisini DEĞİL.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 46 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:AsyncRun:Enabled` varsayılan `true`.

**Adımlar**
1. `Prefer: respond-async` ile gövdede `approvals` alanı taşıyan bir istek
   gönder.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1001 siparisimi iptal et","approvals":[{"requestId":"x","approved":true}]}'
```

**Beklenen sonuç**
- `400` — kuyruğa alınan çalıştırmalarda onay kararları desteklenmez
  (`AgentRunJobHandler`'ın DI kayıt anında HTTP `prefix`'ini bilemediği,
  ekler için de geçerli olan aynı yapısal sınır, Faz 46 Plandan Sapmalar
  madde 5).

### MT-RES-040 — Bekleyen onay yokken Boş durum

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- Hiçbir bekleyen onay yok (reset sonrası temiz durum veya tüm onaylar
  kararlandırılmış).

**Adımlar**
1. Kabukta "Onaylar" ekranına git.

**Beklenen sonuç**
- `Empty` bileşeni görünür: başlık ve gövde metni `approvals.empty.title`/
  `.body` anahtarlarından gelir (`en.ts`/`tr.ts`'te tanımlı — eksikse
  derleme zaten hata verirdi, K-228).

---

### MT-RES-041 — Bekleyen onay satırı tool adı + argüman, run bağlantısı, oluşturulma/süre bilgisiyle görünür; 5 sn'de bir kendiliğinden yenilenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-020`'nin bekleyen onayı hâlâ `Pending`.

**Adımlar**
1. "Onaylar" ekranını aç; satırı gözlemle.
2. Sekmeyi değiştirmeden ~6 saniye bekle; DevTools Network sekmesinde
   `pending`'e ikinci bir istek gittiğini doğrula.
3. Tool adının üstündeki argüman metnine (`orderId=ORD-1001`) bak.
4. Çalıştırma bağlantısına tıkla.

**Beklenen sonuç**
- Adım 1: satırda `cancel_order` (mono, kalın), altında argüman metni
  (`truncate` ile kırpılmış, `title` özniteliğinde tam hâli), run kimliğinin
  kısaltılmış hâli + oturum kimliği, göreli oluşturulma zamanı, MUTLAK
  süre-sonu zamanı (`relativeTime` DEĞİL — kod yorumu: gelecekteki bir an
  için `relativeTime` her zaman "az önce" yazardı, bu yüzden bilerek
  `absoluteTime` kullanılıyor).
- Adım 2: `refetchInterval: 5000` sayesinde ek bir istek gözlenir.
- Adım 4: `runs/{runId}` sayfasına gider — ORİJİNAL (`AwaitingApproval`)
  çalıştırma.

---

### MT-RES-042 — Onayla düğmesi kararı uygular; satır listeden kalkar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir bekleyen onay (`MT-RES-020`'nin adımları).

**Adımlar**
1. "Onaylar" ekranında ilgili satırın Onayla (baş parmak yukarı) düğmesine
   tıkla.
2. Tıklama anında düğmenin durumuna bak (`busy`).
3. İstek dönünce listeye bak.

**Beklenen sonuç**
- Adım 2: yalnız tıklanan düğme `busy` (dönen simge) olur; Reddet düğmesi
  `disabled` olur (aynı satırda ikisi birden tetiklenemesin diye).
- Adım 3: `onSuccess` `approvals-pending` sorgusunu geçersiz kılar; satır
  listeden KALKAR (artık `Pending` değil).

---

### MT-RES-043 — Reddet düğmesi kararı uygular

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir bekleyen onay.

**Adımlar**
1. Reddet (baş parmak aşağı) düğmesine tıkla.

**Beklenen sonuç**
- `MT-RES-022`'nin HTTP davranışıyla aynı sonuç arayüzden tetiklenir; satır
  listeden kalkar.

---

### MT-RES-044 — Karar isteği başarısız olursa hata satırda gösterilir (panel altında, sayfa çökmez)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 55 |
| **İlgili karar** | — |

**Ön koşul**
- Zaten kararlandırılmış bir onayın `id`'sini DevTools'tan alıp aynı isteği
  ikinci kez tetiklemenin bir yolu (ör. çift tıklama yarışı) VEYA doğrudan
  ağ sekmesinden isteği tekrarlama.

**Adımlar**
1. Aynı onaya arka arkaya iki kez hızlı tıkla (Onayla, sonra Reddet) —
   `disabled` denetimi bunu engelliyor olmalı; engellenmezse ikinci istek
   `409` alır.
2. `decide.isError` durumunun panelde nasıl göründüğüne bak.

**Beklenen sonuç**
- İkinci istek gerçekten giderse (`disabled` denetimini atlatan bir yarış),
  `ErrorNote` panelin ALTINDA görünür (`decide.isError && <div className=
  "border-t ...">`), sayfa çökmez, tablo görünür kalır.

### MT-RES-050 — Uzlaştırmayla `Infrastructure` sınıfıyla kapanan bir çalıştırma yeniden oynatılabiliyor mu

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47, 54 |
| **İlgili karar** | K-308 (girdi her zaman kaydedilir) |

**Ön koşul**
- `MT-RES-011`'in ürettiği `Infrastructure` sınıflı bir `Failed` çalıştırma
  (`RecordRunInput` varsayılan `true` olduğu için girdi zaten kaydedilmiş
  OLMALI — bu satır gerçek bir `agent.RunAsync` çağrısıyla değil doğrudan
  SQL `UPDATE` ile üretildiği için girdinin GERÇEKTEN var olup olmadığı
  BELİRSİZDİR; bu case önce bunu doğrular).

**Adımlar**
1. `GET /api/runs/{orphanedRunId}/input` — girdi kaydı var mı bak.
2. Varsa: `POST /api/runs/{orphanedRunId}/replay` `{"toolMode":
   "ReplayTools"}`.
3. Yoksa (beklenen — satır gerçek bir `RunRecordingAgent.RunCoreAsync`
   çağrısından GEÇMEDİ, doğrudan SQL'le üretildi): sonucu ve HTTP kodunu
   kaydet.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/runs/$ORPHANED_RUN_ID/input" -H "$APB"
curl -s -i -X POST "$APU/api/runs/$ORPHANED_RUN_ID/replay" -H "$APB" \
  -H "content-type: application/json" -d '{"toolMode":"ReplayTools"}'
```

**Beklenen sonuç (şüphe — bu case'in kendisi ölçer)**
- Adım 1: muhtemelen `404` — bu manuel testin SQL ile ürettiği yapay öksüz
  satırın gerçek bir `RunInputRecord`'u yoktur (`InMemoryRunInputStore`'a
  hiç yazılmadı; süreç yeniden başlatılınca bellek-içi depo da sıfırlandı).
  GERÇEK bir öksüz kalma senaryosunda (gerçek bir süreç çöküşü) girdi
  ÇÖKMEDEN ÖNCE zaten yazılmış olurdu — bu ayrım not düşülür.
- Adım 2 (girdi varsa): `RunReplayService.PrepareAsync` kaynak çalıştırmanın
  `Status`'una bakmaz (yalnız girdi ve agent tanımına bakar); `Failed`/
  `Infrastructure` olması oynatmayı ENGELLEMEMELİDİR — `200` beklenir.
  Doğrularsa bu, uzlaştırmayla kapanmış bir çalıştırmanın hâlâ
  incelenebilir/tekrarlanabilir kaldığını KANITLAR.

---

# 6 — Kesilen İşin Devamı ve Zarif Kapanış (Faz 87)

Bu bölümün case'leri süreç çöküşünü `MT-RES-010`/`011`'in tekniğiyle simüle
eder: gerçek bir `Ctrl+C` yerine `runs`/`run_inputs`/`tool_invocations`
satırları doğrudan SQL ile "yarım kalmış" hâle getirilir — deterministik ve
tekrarlanabilir. `RunContinuation` ayarları bu bölüme özeldir, `Approvals`
gibi dosyanın sonunda geri alınmalıdır:

```bash
dotnet user-secrets set "Tracon:RunReconciliation:Enabled" "true"
dotnet user-secrets set "Tracon:RunReconciliation:HeartbeatInterval" "00:00:01"
dotnet user-secrets set "Tracon:RunReconciliation:OrphanThreshold" "00:00:03"
dotnet user-secrets set "Tracon:RunReconciliation:ScanInterval" "00:00:01"
dotnet user-secrets set "Tracon:RunContinuation:Enabled" "true"
dotnet user-secrets set "Tracon:RunContinuation:MaxAttempts" "1"
# örnek uygulama bu ayarlarla yeniden başlatılmalı
```

Bu bölümdeki her case, `support` agent'ına bağlı **oturumlu** bir çalıştırma
kurar (`sessionId` verilir), sonra o çalıştırmayı SQL ile "10 dakika önce
başlamış, hâlâ çalışıyor" durumuna sokar. §5'in ortam değişkenleri (`APB`,
`APU`, `PG`) geçerlidir.

### MT-RES-060 — Varsayılan kapalı: `RunContinuation:Enabled=false` iken öksüz koşu devam ETMEZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | K1 (varsayılan kapalı) |

**Ön koşul**
- `RunReconciliation` açık, **`RunContinuation:Enabled` verilMEMİŞ**
  (varsayılan `false`).
- Oturumlu bir çalıştırma (`sessionId` alanı dolu) `runs` tablosunda var.

**Adımlar**
1. Satırı geçmişe al (`MT-RES-011`'deki `UPDATE`, artı `session_id`).
2. 5 saniye bekle.
3. Aynı `sessionId`'ye bağlı çalıştırma sayısını say.

**Girilecek veri**
```bash
$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL,
        session_id = 'mt-res-060' WHERE id = '$SOME_RUN_ID';"
sleep 5
curl -s "$APU/api/runs?sessionId=mt-res-060" -H "$APB" | python3 -c "import json,sys;print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Satır her zamanki gibi `Failed`/`Infrastructure` ile kapanır (`MT-RES-011`
  değişmedi).
- `sessionId=mt-res-060` altında **tek** çalıştırma vardır — hiçbir yeni
  `runId` açılmamıştır.

---

### MT-RES-061 — Açıldığında: oturumlu bir öksüz koşu devam eder; yeni koşunun `continuedFromRunId`'si kaynağı gösterir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | K-315'e teğet (ayrı isim: `ContinuedFromRunId` ≠ `ReplayOfRunId`) |

**Ön koşul**
- Bu bölümün başındaki `RunContinuation:Enabled=true` ayarları uygulanmış,
  uygulama yeniden başlatılmış.
- `support` agent'ına `Prefer: respond-async` ile bir çalıştırma açılmış,
  `runInputs` kaydı GERÇEKTEN var olsun diye önce normal tamamlanmasına izin
  verilmiş, sonra satır geçmişe alınmıştır (aşağıdaki gibi iki adımlı kurulum).

**Adımlar**
1. `support`'a sıradan bir soru gönder (`Prefer: respond-async`), tamamlanmasını
   bekle — girdi kaydı (`run_inputs`) böylece gerçekten yazılmış olur.
2. Aynı satırı SQL ile "10 dakika önce başlamış, hâlâ Running" hâline getir.
3. En fazla 5 saniye bekle.
4. `GET /api/runs?sessionId={sessionId}` ile aynı oturuma bağlı çalıştırmaları
   listele.

**Girilecek veri**
```bash
RESP=$(curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"mt-res-061"}')
SRC=$(echo "$RESP" | python3 -c "import json,sys;print(json.load(sys.stdin)['runId'])")
sleep 3   # gercekten Completed olmasini bekle, girdi kaydi yazilsin

$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SRC';"
sleep 5
curl -s "$APU/api/runs?sessionId=mt-res-061" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Kaynak çalıştırma (`$SRC`) `Failed`/`Infrastructure` ile kapanır (değişmez).
- Listede İKİNCİ bir satır vardır: `continuedFromRunId == $SRC`, `status`
  birkaç saniye içinde `Completed` olur (aynı soruyu yeniden model çalıştırır).
- İkinci satırın `sessionId`'si BİRİNCİYLE AYNIDIR — yeni bir konuşma değil,
  aynı oturumun devamıdır.

---

### MT-RES-062 — Devam koşusu kesintiden ÖNCEKİ tool çağrısını yeniden çalıştırmaz, SONRAKİNİ canlı çalıştırır (fazın birinci hata modu)

Bu case fazın en riskli varsayımını doğrudan sınar: `RecordedToolPlayback`'in
melez semantiği yanlış kurulmuş olsaydı, devam koşusu kesinti noktasında
`422` ile düşerdi (replay'in ESKİ davranışı). Bu case onun yerine koşunun
**tamamlandığını** ve kesinti öncesi tool sonucunun DEĞİŞMEDEN kullanıldığını
doğrudan gözlemler.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-061`'in kurduğu ayarlar hâlâ açık.
- `support` agent'ının `get_order_status` tool'u sipariş numarasına göre
  farklı bir metin döner (kod incelemesiyle doğrulanmış: `OrderTools.cs`).

**Adımlar**
1. `support`'a, modelin `get_order_status`'u ÇAĞIRACAĞI bir soru gönder
   (`Prefer: respond-async`), tamamlanmasını bekle.
2. `GET /api/runs/{runId}/tools` ile GERÇEK (canlı) sonucu not et.
3. Aynı satırı geçmişe al (`MT-RES-061`'deki gibi), ama bu kez
   `tool_invocations` tablosundaki kaydı da SQL ile DEĞİŞTİR — kaynağın
   gördüğü sonucu yapay olarak farklı bir metne çevir (`"RECORDED-DEGERI"`).
4. 5 saniye bekle, yeni (devam) çalıştırmayı bul.
5. `GET /api/runs/{devamRunId}/tools` ile devam koşusunun gördüğü sonucu oku.

**Girilecek veri**
```bash
RESP=$(curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1001 siparisimin durumu ne?","sessionId":"mt-res-062"}')
SRC=$(echo "$RESP" | python3 -c "import json,sys;print(json.load(sys.stdin)['runId'])")
sleep 3

curl -s "$APU/api/runs/$SRC/tools" -H "$APB" | python3 -m json.tool

$PG -c "UPDATE tracon.tool_invocations SET result = 'RECORDED-DEGERI'
        WHERE run_id = '$SRC' AND tool_name = 'get_order_status';"
$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SRC';"
sleep 5

CONT=$(curl -s "$APU/api/runs?sessionId=mt-res-062" -H "$APB" | \
  python3 -c "import json,sys; r=json.load(sys.stdin); print([x['id'] for x in r if x['id']!='$SRC'][0])")
curl -s "$APU/api/runs/$CONT/tools" -H "$APB" | python3 -m json.tool
curl -s "$APU/api/runs/$CONT" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- Devam koşusu **`Completed`** olur — `422`/`Failed` DEĞİL (fazın birinci
  hata modunun tersine çevrilmiş olması gerektiğinin doğrudan kanıtı).
- Devam koşusunun `get_order_status` sonucu `"RECORDED-DEGERI"`dir — model
  aynı argümanla tekrar sorduğunda tool'un GERÇEK gövdesi ÇALIŞMAMIŞ,
  kayıtlı (yapay) sonuç ÖYNETİLMİŞTİR.
- Devam koşusunun tool listesinde, kaynakta OLMAYAN bir çağrı varsa (model
  farklı bir takip sorusu sorarsa) o çağrı GERÇEK bir sonuç taşır — kesinti
  sonrası her çağrı canlı çalışır.

---

### MT-RES-063 — `Destructive` tool taşıyan koşu devam ETMEZ; gerekçe `run_events`'te okunur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | (bu fazda alınan karar — bkz. KARARLAR.md) |

**Ön koşul**
- `MT-RES-061`'in ayarları açık.
- `support`'un `cancel_order` tool'u `ToolEffect.Destructive` taşır (kod
  incelemesiyle: `OrderTools.cs`).

**Adımlar**
1. `support`'a `cancel_order`'ı çağıracak bir soru gönder, tamamlanmasını
   bekle.
2. Satırı geçmişe al (`MT-RES-062`'deki gibi, `tool_invocations`'ı DEĞİŞTİRME
   — asıl `cancel_order` kaydı yeterli).
3. 5 saniye bekle.
4. Aynı oturumda YENİ bir çalıştırma açılıp açılmadığına bak.
5. Kaynağın olay akışında `RunContinuationBlocked` tipini ara.

**Girilecek veri**
```bash
RESP=$(curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"ORD-1001 siparisimi iptal et","sessionId":"mt-res-063"}')
SRC=$(echo "$RESP" | python3 -c "import json,sys;print(json.load(sys.stdin)['runId'])")
sleep 3

$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SRC';"
sleep 5

curl -s "$APU/api/runs?sessionId=mt-res-063" -H "$APB" | python3 -c "import json,sys;print(len(json.load(sys.stdin)))"
curl -s "$APU/api/runs/$SRC/events" -H "$APB" | python3 -c "
import json,sys
events = json.load(sys.stdin)
blocked = [e for e in events if e['type'] == 'RunContinuationBlocked']
print(blocked[0]['text'] if blocked else 'YOK')
"
```

**Beklenen sonuç**
- `sessionId=mt-res-063` altında **tek** çalıştırma vardır — devam
  AÇILMAMIŞTIR.
- Kaynağın olay akışında bir `RunContinuationBlocked` olayı vardır; `text`
  alanı `cancel_order` adını içerir.

---

### MT-RES-064 — `SafeToRepeat` bildiren bir tool, `Destructive` olsa bile devamı ENGELLEMEZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-063` ile AYNI kurulum, ama örnek uygulamadaki `cancel_order` yerine
  `SafeToRepeat = true` bildiren bir tool kayıtlıysa (bu tool örnek
  uygulamada YOKSA, bu case atlanır ve `docs/hafiza/`'ya "örnek uygulamaya
  `SafeToRepeat` örneği eklenmedi" notu düşülür — DoD'un kendisi bunu
  birim/fonksiyonel testle zaten kanıtlıyor,
  `A_tool_declaring_SafeToRepeat_allows_continuation_despite_a_destructive_effect`).

**Adımlar**
1-3. `MT-RES-063` ile aynı, `SafeToRepeat` tool'u çağıran bir soruyla.

**Beklenen sonuç**
- Devam AÇILIR (`continuedFromRunId` kaynağı gösterir) — `Destructive`
  etkisine rağmen, çünkü tool kendi kodunda `SafeToRepeat` bildirmiştir.

---

### MT-RES-065 — `MaxAttempts` sonsuz devam zincirini kapatır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ön koşul**
- `RunContinuation:MaxAttempts=1` (varsayılan, bu bölümün başında verildi).
- `MT-RES-061`'in ÜRETTİĞİ devam koşusu (`$CONT`) elde.

**Adımlar**
1. Devam koşusunu da (`$CONT`) geçmişe al — sanki O DA kesilmiş gibi.
2. 5 saniye bekle.
3. Aynı oturumdaki çalıştırma sayısını say (kaynak + ilk devam = 2 olmalı,
   3 OLMAMALI).

**Girilecek veri**
```bash
$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$CONT';"
sleep 5
curl -s "$APU/api/runs?sessionId=mt-res-061" -H "$APB" | python3 -c "import json,sys;print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Sayı **2**'de kalır — ikinci bir devam AÇILMAZ. `$CONT` da `Failed`
  kapanır ve öyle kalır.

---

### MT-RES-066 — Oturumsuz bir koşu asla devam ETTİRİLMEZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ön koşul**
- `RunContinuation` açık.
- `sessionId` verilMEDEN bir çalıştırma (senkron `POST /api/agents/{name}/run`,
  `Prefer: respond-async` OLMADAN — akış tamamlanınca `runId`'yi not et).

**Adımlar**
1. Satırı geçmişe al.
2. 5 saniye bekle, `GET /api/runs/{runId}` ile durumu oku.
3. `session_id IS NULL` olduğu için hiçbir `sessionId` sorgusu YAPILAMAZ; onun
   yerine `parent_run_id`/`root_run_id IS NULL` olan ve `started_at`'i son 10
   saniyede olan satırları say (yeni bir devam açılmışsa görünür).

**Girilecek veri**
```bash
$PG -c "UPDATE tracon.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SESSIONLESS_RUN_ID';"
sleep 5
curl -s "$APU/api/runs/$SESSIONLESS_RUN_ID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
$PG -c "SELECT count(*) FROM tracon.runs WHERE started_at > now() - interval '15 seconds';"
```

**Beklenen sonuç**
- `status: "Failed"` — her zamanki gibi.
- Son 15 saniyede açılan satır sayısı **0**'dır (uzlaştırmanın kendi
  `RunFailed` olay yazımı yeni bir `runs` SATIRI açmaz, yalnız bir olay
  ekler) — hiçbir devam koşusu açılmamıştır.

---

### MT-RES-067 — 👤 insan gerekir — Konsolda devam bağı görünür ve kaynağa tıklanabilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-RES-061`'in ürettiği devam koşusunun kimliği (`$CONT`) elde.

**Adımlar**
1. Arayüzde `runs/{$CONT}` sayfasını aç.
2. Başlık satırının altındaki özet metnine bak.
3. Bağlantıya tıkla.

**Beklenen sonuç**
- Özet metninde `"..., continued from <kısaltılmış kaynak kimliği>"`
  (`en`) / `"..., devam ettiği koşu <kısaltılmış kimlik>"` (`tr`) biçiminde
  bir ibare vardır — `replayOfRunId` ile AYNI görsel desende (alt çizgili
  mono bağlantı).
3. Tıklamak kaynak çalıştırmanın (`$SRC`) sayfasına GÖTÜRÜR.

---

### MT-RES-068 — Zarif kapanış: `SIGTERM` açık koşuyu bekler, yeni koşuyu reddeder, zaman aşımından sonra kapanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Drain:Enabled" "true"
dotnet user-secrets set "Tracon:Drain:Timeout" "00:00:10"
# örnek uygulama BU ayarla yeniden başlatılmış, PID not edilmiş
```

**Adımlar**
1. `FIX-PROMPT-04` ile uzun bir çalıştırma başlat (arka planda, akış sürerken
   devam et).
2. Akış sürerken sürece `SIGTERM` gönder (`kill -TERM $PID`, `Ctrl+C` DEĞİL —
   terminal `Ctrl+C` bazı kabuklarda `SIGINT` gönderir).
3. `SIGTERM`'den hemen sonra (1 sn içinde) YENİ bir çalıştırma dene.
4. Sürecin ne zaman gerçekten çıktığını gözle (`ps`/günlük).

**Girilecek veri**
```bash
curl -s -N -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" \
  -d "{\"message\":\"$(python3 -c "print('lorem ipsum dolor sit amet ' * 2000)")\"}" > /tmp/mt-res-068.txt &
sleep 1

kill -TERM $PID

sleep 1
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -d '{"message":"hi"}'
```

**Beklenen sonuç**
- Adım 3: `503` (`title: "Service is shutting down"`) — sürecin kendisi hâlâ
  ayaktadır (drain penceresi içinde) ama yeni koşu KABUL EDİLMEZ.
- Adım 4: süreç, adım 1'in akışı TAMAMLANANA kadar (veya en fazla 10 saniye
  — `Drain:Timeout`) ayakta kalır, sonra çıkar. Süreç `SIGTERM`'den HEMEN
  sonra ölmez.

---

### MT-RES-069 — Faz 126 öncesi yazılmış oturum, migration sonrası okunmaya devam eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 126 |
| **İlgili karar** | — |

**Ön koşul**
```bash
# Faz 126'nın migration'ı (NNNN_persisted_payload_version) UYGULANMAMIŞ bir
# veritabanı: eski schema_version sütunu hâlâ mevcut, state_maf_version yok.
```

**Adımlar**
1. Migration öncesi bir oturum kaydı yaz (normal bir `run` ile, ya da doğrudan
   `INSERT INTO sessions (..., schema_version, ...)`).
2. Migration'ı koş (`dotnet tracon migrate` veya otomatik uygula).
3. `psql`/`sqlcmd`/`sqlite3` ile o oturum satırını sorgula.
4. Aynı oturuma yeni bir `run` gönder (`GET /api/sessions/{id}` değil,
   gerçek bir konuşma turu).

**Beklenen sonuç**
- Adım 3: `schema_version` sütunu `state_schema_version` adına taşınmış,
  değeri DEĞİŞMEMİŞ (`1`); yeni `state_maf_version` sütunu bu satırda
  `NULL`.
- Adım 4: oturum normal şekilde açılır ve tur çalışır — migration verideki
  tek bir baytı bile değiştirmedi, yalnız zarfı damgaladı.

---

### MT-RES-070 — Faz 126 sonrası yazılmış oturum, iki damgayı da taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 126 |
| **İlgili karar** | — |

**Ön koşul**
```bash
# Faz 126'nın migration'ı UYGULANMIŞ bir veritabanı.
```

**Adımlar**
1. `POST /api/agents/{name}/run` ile yeni bir oturum aç ve bir tur çalıştır.
2. Oturum satırını sorgula (`SELECT id, state_schema_version, state_maf_version FROM sessions ...`).

**Beklenen sonuç**
- `state_schema_version` bugünkü Tracon şema neslini taşır (`1`).
- `state_maf_version`, `Directory.Packages.props`'taki
  `MicrosoftAgentsAIVersion` ile eşleşen bir sürüm dizesi taşır — `NULL`
  değil.

---

### MT-RES-071 — Tanınmayan şema nesli: tanımlı hata, oturum silinmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 126 |
| **İlgili karar** | — |

**Ön koşul**
```bash
# Var olan bir oturumun state_schema_version'ını elle GELECEKTEKİ bir
# değere ayarla:
psql -c "UPDATE tracon.sessions SET state_schema_version = 999999 WHERE id = '<id>';"
```

**Adımlar**
1. O oturuma yeni bir tur gönder (`POST /api/agents/{name}/run` ile aynı
   `sessionId`).
2. Yanıtın hata gövdesini oku.
3. Oturum satırının hâlâ var olduğunu doğrula.

**Beklenen sonuç**
- Adım 2: hata mesajı **ölçülmüş** iki sayıyı adıyla söyler — kayıtlı nesil
  (`999999`) ve bu derlemenin anladığı nesil (`1`). "may have become
  unreadable" gibi tahmine dayalı bir ifade YOKTUR.
- Adım 3: satır silinmedi, sessizce sıfırlanmadı — aynı `state_schema_version`
  ile hâlâ orada.

---

### MT-RES-072 — 👤 insan gerekir — Yükseltme provası: fixture kapısı bugünkü MAF'a karşı koşar

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 126 |
| **İlgili karar** | — |

**Ön koşul**
```bash
# Yerel klonda Directory.Packages.props içindeki MicrosoftAgentsAIVersion
# bir sonraki gerçek MAF sürümüne yükseltilmiş (bu case yalnız GERÇEK bir
# MAF sürüm yükseltmesi elde varken anlamlıdır; günlük geliştirmede atlanır).
```

**Adımlar**
1. `dotnet restore` ile yeni MAF sürümünü çek.
2. `./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests --filter-method "*PersistedPayloadUpgrade*"` koştur.
3. `./artifacts/bin/Tracon.Workflows.UnitTests/release/Tracon.Workflows.UnitTests --filter-method "*PersistedPayloadUpgrade*"` koştur.

**Beklenen sonuç**
- Testler ya YEŞİLDİR (yeni MAF sürümü eski fixture'ları hâlâ okuyabiliyor),
  ya da KIRMIZIDIR ve hata mesajı hangi MAF sürümünün hangi fixture'ı
  okuyamadığını AÇIKÇA söyler — "bir fixture bozuldu" değil, "MAF X → Y
  yükseltmesi üretimdeki oturumları/checkpoint'leri okunamaz yapıyor" biçiminde.
- Kırmızı çıkarsa fixture YENİDEN ÜRETİLMEZ (bkz.
  `tests/Tracon.Core.UnitTests/Fixtures/README.md`); karar
  `nuget-danismani` kanalına gider.

---

### MT-RES-073 — `IToolApprovalPresenter` bilinmeyen bir varlıkta `null` döner; onay isteği yine de yayımlanır (Faz 142)

`OrderApprovalPresenter` yalnız `ORD-1001`/`ORD-1002`'yi bilir — üçüncü bir
sipariş numarası fail-open sözleşmesinin GÖZLENEBİLİR ucunu ölçer (`throw`/
zaman aşımı senaryoları `ToolApprovalPresenterTests`'te birim testiyle kilitli,
gerçek bir hata enjekte etmek için sunucu koduna dokunmak gerekir).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 142 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `support`'a "ORD-9999 siparisimi iptal et" mesajını `Prefer:
   respond-async` ile gönder.
2. Onay `Pending` olunca `GET /api/approvals/pending`'i oku.

**Beklenen sonuç**
- Kayıt yine yayımlanır (`status: "Pending"`); `presentation` alanı `null`
  döner. `arguments` `"orderId=ORD-9999"` ile DOLU kalır — çözümleyicinin
  "bilmiyorum" demesi ham argümanı gizlemez.

---

### MT-RES-080 — Kooperatif katman: çok kısa `ChildDeadline` alt-agent'ı gerçekten iptal eder (Faz 144)

İki katmanın ikisi de (kooperatif iptal + sert kesme) gerçek bir MAF
`background_agents_*` tool akışı üzerinden `SubAgentTimeoutTests`
(`Tracon.AspNetCore.FunctionalTests`) ile zaten otomatik kanıtlanmıştır —
bu case'ler örnek uygulamada elle gözlem içindir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 144 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Api` ayakta.
- `yonlendirici` adlı bir agent, `CallableAgentNames = ["arastirmaci"]` ve
  `SubAgents.ChildDeadline = 00:00:01` ile tanımlı (gerçek bir model çağrısı
  genelde bir saniyeden uzun sürer, bu yüzden kesme gerçek koşulda tetiklenir).

**Adımlar**
1. `yonlendirici`'yi çalıştır, `arastirmaci`'yi çağıracak bir istek gönder.
2. `GET /tracon/api/runs/{runId}/events` ile kök `run`'ın olay akışını oku.

**Beklenen sonuç**
- Kök `run`, `ChildDeadline` süresinde biter (alt-agent'ın kendi model
  çağrısının tam süresini beklemez).
- Olay akışında `ChildRunTimedOut` görünür; `Payload`'daki `hardCutoff` alanı
  `false`'dur (kooperatif katman kesti, kaynak bırakıldı).
- `ChildRunCompleted` de görünür — alt-agent'ın kendi çalıştırması gerçekten
  iptal edilerek bitmiştir, arka planda asılı kalmamıştır.

---

### MT-RES-081 — Harness'li kök agent aynı iki katmanı uygular (Faz 144)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 144 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RES-080 ile aynı kurulum, tek fark: `yonlendirici`'nin `Harness` alanı
  dolu (harness yolu).

**Adımlar**
- MT-RES-080 ile birebir aynı.

**Beklenen sonuç**
- Birebir aynı: `run` `ChildDeadline` süresinde biter, `ChildRunTimedOut`
  yazılır. Harness yolu düz agent yolundan farklı davranmaz — 144.4'ün amacı
  budur.

---

### MT-RES-082 — Agent'ın kendi `SubAgents.ChildDeadline`'ı kurulum varsayılanını ezer (Faz 144)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 144 |
| **İlgili karar** | — |

**Ön koşul**
- Kurulumun `Tracon:AgentGraph:ChildDeadline`'ı büyük bir değerde
  (ör. `00:02:00`, varsayılan).
- `yonlendirici`'nin kendi `SubAgents.ChildDeadline`'ı çok kısa (`00:00:01`).

**Adımlar**
- MT-RES-080 ile aynı çağrı.

**Beklenen sonuç**
- `run`, kurulumun iki dakikalık varsayılanını değil, agent'ın bir saniyelik
  değerini bekleyip zaman aşımına uğrar — çözümleme sırası agent → kurulum →
  MAF varsayılanıdır (`SubAgentSettingsResolutionTests` ile birim düzeyinde de
  kilitlidir).

---

### MT-RES-083 — Geçersiz `WaitTimeout <= ChildDeadline` uygulamayı derleme/çalıştırma anında düşürür (Faz 144)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 144 |
| **İlgili karar** | — |

**Ön koşul**
- `yonlendirici` tanımında `SubAgents = { ChildDeadline: 00:00:30, WaitTimeout:
  00:00:30 }` (eşit — geçersiz kombinasyon).

**Adımlar**
1. Uygulamayı başlat veya `yonlendirici`'yi katalogdan çöz (`GET
   /tracon/api/agents/yonlendirici` ya da ilk çalıştırma denemesi).

**Beklenen sonuç**
- `TraconCompilationException` fırlar; mesaj `yonlendirici` agent adını ve
  `ChildDeadline`/`WaitTimeout` alan adlarını taşır. Kombinasyon sessizce kabul
  EDİLMEZ.

---

### MT-RES-084 — Zaman aşımına uğrayan bir `run`'da çocuğa ait GEÇ olay YOKTUR (Faz 144)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 144 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RES-080'in zaman aşımına uğramış `run`'ı elde.

**Adımlar**
1. `run` bittikten sonra birkaç saniye bekle (alt-agent'ın gerçek model
   çağrısının doğal olarak tamamlanmasına yetecek kadar).
2. `GET /tracon/api/runs/{runId}/events`'i TEKRAR oku.

**Beklenen sonuç**
- Olay listesi ilk okumadakiyle AYNIDIR — alt-agent'ın gecikmiş sonucu
  (başarı ya da hata) hiçbir yeni olay, metrik veya sunucu hatası üretmez;
  sessizce atılır (K-621 ile aynı sözleşme).

---

### MT-RES-085 — Ölen worker'ın işi lease dolmadan DEVRALINMAZ (Faz 157)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 157 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL (SQLite DEĞİL — tek process tavsiyelidir).
- Aynı şemaya bağlı iki worker process; `Scheduling.LeaseDuration` kısa
  (örn. `00:00:06`), `PollInterval` `00:00:00.250`.
- Uzun süren bir job handler ve kuyruğa girmiş bir `job`.

**Adımlar**
1. Worker A'yı başlat; `job`'u lease ettiğini ve handler'a girdiğini doğrula
   (`GET /tracon/api/jobs/{id}` → `leaseOwner` dolu, `attempt = 1`).
2. `lease_until` değerini not al.
3. Worker B'yi başlat.
4. Worker A'yı `kill -9 <pid>` ile öldür (zarif kapanış DEĞİL).
5. `lease_until`'dan ÖNCE `job`'u oku.

**Beklenen sonuç**
- `leaseOwner` hâlâ A'dır, `attempt` hâlâ `1`'dir; B işi ALMAMIŞTIR.
- Handler'a ikinci bir giriş olmamıştır — iki worker aynı anda ASLA aynı işi
  yürütmez.

**Otomatik karşılığı:** `TwoProcessLeaseTakeoverTests.A_dead_workers_job_is_not_taken_over_before_its_lease_expires`

---

### MT-RES-086 — Lease dolduktan sonra iş devralınır ve BİR KEZ tamamlanır (Faz 157)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 157 |
| **İlgili karar** | — |

**Ön koşul**
- MT-RES-085'in kurulumu; worker A öldürülmüş durumda.

**Adımlar**
1. `LeaseDuration` süresi dolana kadar bekle.
2. `job`'u tekrar oku ve durumu izle.

**Beklenen sonuç**
- Worker B işi devralır: `leaseOwner` B'ye döner, `attempt = 2` olur.
- `job` `Completed` olur; `doneItems` madde sayısına EŞİTTİR (iki katı DEĞİL —
  madde raporlaması idempotenttir).
- B'nin handler'a giriş zamanı A'nın `lease_until` değerinden SONRADIR.
- 🚨 A'nın yarım kalan denemesinin yan etkileri geri alınmaz; yürütme
  **at-least-once**'tır. Geri dönüşü olmayan yan etkiler için idempotency
  uygulamanın sorumluluğundadır.

**Otomatik karşılığı:** `TwoProcessLeaseTakeoverTests.A_dead_workers_job_is_taken_over_after_the_lease_expires_and_never_runs_concurrently`

---

### MT-RES-087 — `RunWorker=false` process kuyruğa DOKUNMAZ (Faz 157)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 157 |
| **İlgili karar** | — |

**Ön koşul**
- Tek bir process, `Tracon:Scheduling:RunWorker=false`; aynı SQL deposu.

**Adımlar**
1. Bir `job` kuyruğa gönder.
2. Birkaç `PollInterval` kadar bekle.
3. `job`'u oku.

**Beklenen sonuç**
- `status` `Pending` kalır, `leaseOwner` `null`, `attempt = 0`.
- Bu bir kusur DEĞİLDİR: API node'unun tanımlı davranışıdır. Kuyruğun
  ilerlemesi için bir worker node gerekir.

**Otomatik karşılığı:** `TwoProcessLeaseTakeoverTests.An_api_node_leaves_the_queued_job_untouched`

---

### MT-RES-088 — Veritabanı düştüğünde worker AYAKTA kalır, `job` bozulmaz (Faz 157)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 157 |
| **İlgili karar** | — |

**Ön koşul**
- Bir worker process ve kuyrukta bir `job`.

**Adımlar**
1. Veritabanını durdur (`docker stop <container>`).
2. Birkaç `PollInterval` bekle; process listesini kontrol et.
3. Veritabanını yeniden başlat.
4. `job`'u oku.

**Beklenen sonuç**
- Worker process ÇIKMAMIŞTIR; başarısız tick loglanır ve döngü sürer.
- Veritabanı dönünce `job` bulunduğu yerdedir; sessizce tamamlanmış
  GÖRÜNMEZ (`doneItems = 0`).
- Aynı kesinti sırasında bir okuma çağrısı **istisna fırlatır** — boş liste
  dönmez.

**Otomatik karşılığı:** `FailureManifests.DatabaseUnavailableTests`

---

### MT-RES-089 — Sağlayıcı zaman aşımı `Canceled` DEĞİL, `Timeout` olarak kaydedilir (Faz 157)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 157 |
| **İlgili karar** | K-737 |
| **Regresyon** | Evet — Faz 157'de bulunan kusur |

**Ön koşul**
- Yanıt vermeyen bir sağlayıcı ucu (örn. paket düşüren bir adres) ve kısa bir
  HTTP timeout'u; agent'ta fallback zinciri YOK.

**Adımlar**
1. `POST /tracon/api/agents/{ad}/run` (`Idempotency-Key` başlığı ile —
   akışsız yol).
2. Yanıtı ve `GET /tracon/api/runs` kaydını oku.

**Beklenen sonuç**
- HTTP `502` + `application/problem+json`; **200 + boş gövde DEĞİL**.
- `run.status` `Failed`, `errorClass` `Timeout`'tur; `Canceled` DEĞİLDİR.
- Sağlayıcının ham istisna metni yanıtta GÖRÜNMEZ.
- Agent'a bir fallback eklendiğinde aynı senaryo `200` döner ve
  `modelProvider` YANIT VEREN sağlayıcıdır.

**Otomatik karşılığı:** `FailureManifests.ProviderTimeoutTests`

---

### MT-RES-090 — Yavaş bir `sink` `run`'ı YAVAŞLATIR, bozmaz (Faz 157)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 157 |
| **İlgili karar** | — |

**Ön koşul**
- Her olayda ~200 ms bekleyen bir `IRunEventSink` kaydı.

**Adımlar**
1. Akışlı bir `run` başlat ve toplam süreyi ölç.
2. `sink`'i istisna fırlatan bir uygulamayla değiştir ve tekrarla.

**Beklenen sonuç**
- Yavaş `sink`: `run` tamamlanır, ama süre olay sayısı × gecikme kadar UZAR —
  dağıtım sıcak yolda `await` edilir, arka plana atılmaz.
- Fırlatan `sink`: ilk hatadan sonra o `run` için devre dışı bırakılır, bir
  kez daha çağrılmaz; `run` normal tamamlanır ve olaylar depoya yazılır.

**Otomatik karşılığı:** `FailureManifests.SlowSinkTests`
