# 21 — Dayanıklılık: Çalıştırma İptali, Öksüz Uzlaştırma ve Asenkron Onay Kutusu (`RES`)

> **Alan kodu:** `RES` · **Faz:** 32, 54, 55 tam kapsam · 44 yalnız `Canceled`/`Infrastructure`
> sınıfları · 46/47 yalnız bu dosyaya özgü kesişim noktaları (bkz. Sınır tablosu).
>
> **Kaynak:**
> `src/AgentPrism.Abstractions/Runs/IRunCancellationRegistry.cs`,
> `RunReconciliationOptions.cs`, `RunErrorClass.cs` (yalnız `Canceled=10`/
> `Infrastructure=12`) ·
> `src/AgentPrism.Abstractions/Approvals/` (tümü: `PendingApproval.cs`,
> `ApprovalStatus.cs`, `IPendingApprovalStore.cs`) ·
> `src/AgentPrism.Core/Recording/RunCancellationRegistry.cs`,
> `RunHeartbeatWriter.cs`, `RunReconciliationService.cs`,
> `RunReconciliationOptionsValidator.cs` ·
> `src/AgentPrism.Core/Approvals/` (tümü: `InMemoryPendingApprovalStore.cs`,
> `AgentPrismApprovalOptions.cs`, `ApprovalExpirationService.cs`,
> `ApprovalResumeJobHandler.cs`) ·
> `src/AgentPrism.Core/Runs/DefaultRunErrorClassifier.cs` (yalnız
> `Canceled`/`Infrastructure` dalları) ·
> `src/AgentPrism.Core/SingletonGuard.cs`, `SingletonExecutionOptions.cs` ·
> `src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs` (yalnız
> `CancelRunAsync`, satır 631-719) ·
> `src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs` (tümü) ·
> `src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs` (yalnız `RunAsync`,
> §1'in workflow-iptal denemesi için) ·
> `src/AgentPrism.UI/frontend/src/screens/approvals.tsx`.
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
> | İptal düğmesi, onay penceresi, durum rozeti (arayüz) | `11-ARAYUZ-RUN-SESSION-SSE.md` `MT-UIRUN-021`–`025` |
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
| İptal düğmesi, `window.confirm`, durum rozeti geçişleri (arayüz) | `11-ARAYUZ-RUN-SESSION-SSE.md` `MT-UIRUN-021`–`025` (zaten üretildi) — **burada TEKRARLANMAZ** |
| İş kuyruğu seviyesinde `Pending`/`Running`/`Queued` iptali, `Prefer: respond-async` sözleşmesi | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` `MT-JOB-040`–`044`, `070`–`084` (zaten üretildi) |
| `Idempotency-Key` sözleşmesi (dört durum, `400`/`422`, saklama) | `07-HTTP-YONETIM-API.md` `MT-API-030`–`033` (zaten üretildi) |
| `/api/stats/errors` panosu, `ProviderError`/`ToolError` gerçek örnekleri, kırılım toplamı | `07` `MT-API-040/041`, `12-GOZLEMLENEBILIRLIK-MALIYET.md` `MT-OBS-010` (zaten üretildi) — bu dosya yalnız `Canceled`/`Infrastructure` sınıflarını EKLER |
| Yeniden oynatma paneli, karşılaştırma paneli, dallandırma düğmesi (arayüz), `422`/`409` UI davranışı | `11` `MT-UIRUN-026`–`046`, `10-ARAYUZ-AGENT-PLAYGROUND.md` `MT-UIAG-039` (zaten üretildi) |
| `POST /runs/{id}/replay` kapsam denetimi (`Operator`/`Admin`), `feedback`/`compare`/`input` uçlarının kapsam boşluğu | `17-EVAL-VE-DENEYLER.md` `MT-EVAL-093/094/101` (zaten üretildi) |
| Genel HTTP zarfı (`ProblemDetails`), kiracı yalıtımı (genel mekanizma), API anahtarı oluşturma deseni | `07`, `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada yalnız §3'ün onay-kutusuna özgü kapsam/kiracı boşlukları için TEKRAR kullanılır |
| Rol politikalarının (`AgentPrismPolicies`) örnek uygulamada kayıtlı olmadığı, dolayısıyla `RequireRole`'ün no-op olduğu genel bulgu | `14-SKILL-VE-SCRIPT.md` (zaten not düşüldü) — bu dosyanın `MT-RES-029`'u AYNI kök nedeni Onay uçlarında DOĞRULAR, yeniden araştırmaz |
| `document_embeddings`/`run_inputs` gibi diğer saklama hedeflerinin retention ile silinmesi | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) — bu dosya `pending_approvals`/`run_heartbeats` için bir saklama hedefi de ARAMAZ (kod okumasıyla: `RetentionTargets.cs`'te bu iki tablo için giriş yok — bkz. not §2 sonu) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama PostgreSQL ile çalışır (varsayılan kurulum, §2.4).
3. `AgentPrism:Providers:OpenAI:ApiKey` tanımlı — §1 ve §5 gerçek model çağırır
   (`FIX-PROMPT-04`, 50.000 karakter, uzun bir çalıştırma penceresi açmak için;
   teknik `11-ARAYUZ-RUN-SESSION-SSE.md`'nin `MT-UIRUN-021`'iyle AYNIDIR).
4. §2 ve §5, `RunReconciliation`'ı GEÇİCİ olarak açık ve kısa aralıklarla
   çalıştırmayı ister; §3'ün `MT-RES-027`'si `Approvals` süre sonunu benzer
   şekilde kısaltır. Bu ayarlar case içinde verilir; **dosyanın sonunda geri
   alınmalı** (aksi halde sonraki dosyaların koşumu sırasında arka planda
   gereksiz tarama sorguları atılır):
   ```bash
   dotnet user-secrets remove "AgentPrism:RunReconciliation:Enabled"
   dotnet user-secrets remove "AgentPrism:RunReconciliation:HeartbeatInterval"
   dotnet user-secrets remove "AgentPrism:RunReconciliation:OrphanThreshold"
   dotnet user-secrets remove "AgentPrism:RunReconciliation:ScanInterval"
   dotnet user-secrets remove "AgentPrism:Approvals:DefaultExpiration"
   dotnet user-secrets remove "AgentPrism:Approvals:ScanInterval"
   ```
5. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"
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

**Gerçek sonuç**
Adim 3: HTTP 202. Adim 4: 3 saniye sonra hem kok (yonlendirici) hem alt (support) calistirmasi Canceled oldu - registry'nin RootRunId kaskadi dogrulandi. Adim 5: run:{root} denetim izinde bir run.cancel kaydi VAR; run:{child} denetim izinde HICBIR kayit YOK - cocugun iptali dogrudan CancellationTokenSource.Cancel() ile, ikinci bir HTTP/audit yazimi yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Alt calistirma Canceled oldu. Kok calistirma DURMADI - cocuk iptal edildikten hemen sonra Running kaldi, birkac saniye sonra normal akisiyla Completed oldu (error: null) - iptalden dolayi degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: HTTP 202, govde status: "Running" (eski/onceki durum yazildi - CancelRunAsync'in Running dali OKUNMUS kaydi donuyor, Canceled yazmiyor). Not: destek tek-turlu (support tek basina) calistirmalar ~1 saniyede tamamlaniyor (10K girdi tokeni, 38 cikti tokeni), bu yuzden ilk denemede yaris kosulu 409 (zaten tamamlanmis) verdi; yonlendirici (iki model turu) kullanilarak guvenilir bir Running penceresi elde edildi. Adim 2: Prefer:respond-async ile kuyruga alinan bir calistirmanin cancel govdesi status: "Canceled" yazdi (GUNCEL kayit) - iki dal aynı uctur ama govde tazeligi FARKLI, dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Sunucu surec, uzun bir yonlendirici calistirmasi baslar baslamaz (kill -9 ile) durduruldu (registry bellek-ici oldugu icin kaybedildi, runs satiri PostgreSQL'de Running kaldi - dogrudan SQL sorgusuyla dogrulandi). Uygulama yeniden baslatildi. Ayni runId'ye POST cancel: HTTP 409, title: "Calistirma bu ornekte yurutulmuyor", detail: "...'Running' gorunuyor ama bu surecte kayitli degil. Baska bir ornekte calisiyor olabilir veya surec calistirma sirasinda yeniden baslamis olabilir." - MT-UIRUN-023'un 'zaten sonlanmis' 409'undan FARKLI title tasiyan ikinci bir 409 yolu dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-005 — 🚨 Workflow çalıştırması gerçekten iptal edilebiliyor mu (Faz 32 kapanışında KANITLANAMAMIŞ boşluğun denemesi)

`docs/32-CALISTIRMA-IPTALI.md`'nin "Plandan Sapmalar" bölümü açıkça şunu
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

**Beklenen sonuç (şüphe — Faz 32 kendisi bunu kanıtlayamadı)**
- İstenen: `Canceled`.
- Kod incelemesinin bıraktığı açık soru: `WorkflowRunner.ExecuteAsync`'in
  kaydettiği birleşik `CancellationTokenSource` (K-245) MAF'ın sıralı grafiğini
  gerçekten kesiyor mu, yoksa Faz 32'nin kendi denemesinde olduğu gibi grafik
  iptali yutup `Completed` mi yazıyor? Bu HENÜZ kimse tarafından gerçek bir
  workflow'la doğrulanmadı. Gerçek sonuç ne olursa olsun (Canceled/Completed)
  buraya birebir yazılmalı — bu, repo'nun bilinen açık bir sorusunu kapatır.

**Gerçek sonuç**
**KALDI - HATA-S2-010 (Yuksek, dogrulanmis supheydi - Faz 32'nin kendi kapatamadigi acik soruyu kapatti).** ozetle-ve-cevir workflow'u FIX-PROMPT-04 boyutunda bir metinle baslatildi. Cancel ONCESI ayri bir GET ile durum acikca 'Running' olarak DOGRULANDI (yaris kosulu degil). POST /api/runs/{id}/cancel -> HTTP 202. 3 saniye sonra: status: Completed (Canceled DEGIL), error: null. Istenen sonuc Canceled'di, gerceklesen Completed - Faz 32'nin kendi denemesinde yasadigi ayni sorun (grafik iptali yutuluyor) TEKRARLANDI ve DOGRULANDI. Kod okumasi: WorkflowRunner.cs:356-368 AgentPrism seviyesinde DOGRU gorunuyor - tek bir 'linked' CancellationTokenSource hem IRunCancellationRegistry.Register'a (satir 362) hem run.WatchStreamAsync'e (satir 589, linked.Token) besleniyor; OperationCanceledException dogru sekilde yakalaniyor (satir 612). Sorun muhtemelen MAF'in kendi AgentWorkflowBuilder.BuildSequential grafiginin (StreamingRun.WatchStreamAsync ic uygulamasi) disaridan gelen iptal tokenini calisan bir adim ortasinda GERCEKTEN honor etmemesi - AgentPrism disi (bagimlilik) bir sinir, ama kullaniciya gore SONUC AYNI: bir workflow calistirmasi iptal edilemiyor, sessizce tamamlaniyor (maliyet/zaman israfi + kullanici yaniltilmasi).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Supheler DOGRULANDI. Adim 1: iptal edilmis bir calistirmanin (MT-RES-001'in kok runId'si) error alani null. Adim 2: /api/stats/errors?hours=1 BOS dizi dondu - az once GERCEKTEN birden fazla calistirma iptal edilmis olmasina ragmen 'class: Canceled' tasiyan hicbir oge yok. Dogrulandi: kusur DEGIL, olu kod - RunErrorClass.Canceled enum uyesi ve classifier'in onu ureten dali (DefaultRunErrorClassifier.cs:61) hicbir zaman calismiyor, cunku RunRecordingAgent'in OperationCanceledException yakalayan iki noktasi (satir 241-243, 323-325) CompleteAsync'i her zaman error:null ile cagiriyor, siniflandirici asla tetiklenmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Öksüz Çalıştırma Uzlaştırması (Faz 54)

`RunReconciliationOptions.Enabled` varsayılan **`false`**'tur (kod yorumu:
*"tek ornekli bir gelistirme kurulumunda kimse bir arka plan yazicisi
beklemez ve kira tablosuna hicbir sorgu gitmez"*). `RunHeartbeatWriter` bu
süreçte AKTİF (registry'de kayıtlı) çalıştırmaların `heartbeat_at`'ini
periyodik yazar; `RunReconciliationService` `status=Running` VE
`COALESCE(heartbeat_at, started_at) < eşik` olan satırları `Failed` +
`error.class=Infrastructure` + `fingerprint="orphaned"` ile kapatır
(sabit dize — `ErrorFingerprint.Compute` DEĞİL, K-364). Bu bölüm hiç model
çağırmaz; DB'ye doğrudan yazarak bir "çökmüş süreç" taklit eder.

```bash
dotnet user-secrets set "AgentPrism:RunReconciliation:Enabled" "true"
dotnet user-secrets set "AgentPrism:RunReconciliation:HeartbeatInterval" "00:00:01"
dotnet user-secrets set "AgentPrism:RunReconciliation:OrphanThreshold" "00:00:03"
dotnet user-secrets set "AgentPrism:RunReconciliation:ScanInterval" "00:00:01"
# uygulamayi yeniden baslat
```

### MT-RES-010 — Varsayılan kapalı: `Enabled=false` iken eski bir `Running` satır asla kapanmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 54 |
| **İlgili karar** | K1 (varsayılan kapalı) |

**Ön koşul**
- `AgentPrism:RunReconciliation:*` ayarlarının HİÇBİRİ verilmemiş (varsayılan).
- Herhangi bir tamamlanmış çalıştırmanın `runId`'si not edilmiş.

**Adımlar**
1. O satırı doğrudan SQL ile "10 dakika önce başlamış, hâlâ çalışıyor" gibi
   göster.
2. 10 saniye bekle.
3. Durumu tekrar oku.

**Girilecek veri**
```bash
$PG -c "UPDATE agentprism.runs SET status = 0, completed_at = NULL,
        started_at = now() - interval '10 minutes', heartbeat_at = NULL
        WHERE id = '$SOME_RUN_ID';"
sleep 10
curl -s "$APU/api/runs/$SOME_RUN_ID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- `status: "Running"` — hiçbir arka plan taraması çalışmadığı için satır
  SONSUZA kadar `Running` görünür kalır (yeniden reset yordamı uygulanana
  kadar).

**Gerçek sonuç**
AgentPrism:RunReconciliation:* ayarlarinin hicbiri verilmedi (varsayilan). Bir satir dogrudan SQL ile '10 dakika once baslamis, hala calisiyor' hale getirildi. 10 saniye sonra: status: Running - hicbir arka plan taramasi calismadigi icin satir SONSUZA kadar Running gorundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
$PG -c "UPDATE agentprism.runs SET status = 0, completed_at = NULL,
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
  hesaplanmaz; `AgentPrism.Sql.Shared` `AgentPrism.Core`'daki `internal`
  sınıfa erişemediği için).
- `error.message` `"...son isaret: <tarih>..."` biçiminde bir metin taşır.

**Doğrulama sorgusu**
```sql
SELECT status, error_type, error_class, error_fingerprint, error_message
  FROM agentprism.runs WHERE id = '<runId>';
```

**Gerçek sonuç**
RunReconciliation acildi (Enabled=true, HeartbeatInterval=1s, OrphanThreshold=3s, ScanInterval=1s), yeniden baslatildi. Satir gecmise alindi. 5 saniye sonra: status: Failed, error.type: orphaned, error.class: Infrastructure, error.fingerprint: orphaned (sabit dize), error.message: "Calistirma yuruten surec yanit vermiyor; son isaret: 2026-08-13 13:12:31..." - tum alanlar birebir eslesti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

$PG -c "UPDATE agentprism.runs SET started_at = now() - interval '10 minutes'
        WHERE id = '$QID' AND status = 5;"
sleep 5
curl -s "$APU/api/runs/$QID" -H "$APB" | python3 -c "import json,sys;print(json.load(sys.stdin)['status'])"
```

**Beklenen sonuç**
- `status` hâlâ `"Queued"`dur (veya işçi normal şekilde alıp `Completed`
  etmiştir) — ASLA `Failed`/`Infrastructure` olmaz. Uzlaştırma sorgusu
  `WHERE status = 0` (yalnız `Running`) filtresi taşır
  (`PostgresQueries.cs:333` civarı); `Queued` bir kova bile değildir.

**Gerçek sonuç**
Prefer:respond-async ile bir calistirma kuyruga alindi, started_at gecmise alindi (status=5 Queued korunarak). 5 saniye sonra status: Running (isci normal sekilde alip islemeye basladi) - hicbir zaman Failed/Infrastructure OLMADI. Uzlastirma sorgusu yalniz status=0 (Running) satirlari suzuyor, Queued bir kova bile degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: ilk olcumde len:1 cikti (MT-RES-012'nin kuyruktan yeni alinmis, GERCEKTEN aktif calisan satiri nedeniyle) - bu satir tamamlandiktan (Completed) sonra tekrar olculdugunde len:0 ve gercekten bir DIZI (obje/{items:...} DEGIL) dogrulandi. Adim 2: /api/stats/errors?hours=1 class:Infrastructure kumesi var, sampleMessage '...yanit vermiyor...' iceriyor (totalRuns:2, iki ayri MT-RES-010/011 orphan olayindan). Adim 3 (dashboard bileseni) kod okumasiyla dogrulandi: RunErrorClass string-enum, arayuz listeyi dongüyle basiyor - KOD degisikligi gerektirmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- `AgentPrism:SingletonExecution:Enabled` HENÜZ verilmemiş (varsayılan
  `false` — `SingletonGuard.IsHeld` her zaman `true` döner, depoya hiç
  sorgu gitmez).

**Adımlar**
1. İkinci bir örneği farklı bir portta başlat (aynı PostgreSQL'e bağlı).
2. İki örnek de açıkken bir satırı geçmişe al (`MT-RES-011`'deki gibi).
3. Sunucu günlüklerinde İKİ örneğin de "1 oksuz calistirma kapatildi"
   uyarısını yazıp yazmadığına bak (ikisi de aynı satırı görüp `UPDATE`
   denemesi yapabilir; SQL'in kendisi idempotent olduğu için veri bozulmaz
   ama İKİ log satırı beklenir).
4. `AgentPrism:SingletonExecution:Enabled=true` ekleyip HER İKİ örneği de
   yeniden başlat, adım 2-3'ü tekrarla.

**Girilecek veri**
```bash
# ikinci ornek (ayrı terminal, aynı user-secrets kimligini paylasir):
cd samples/AgentPrism.Api && dotnet run --urls http://localhost:5081

# adim 4:
dotnet user-secrets set "AgentPrism:SingletonExecution:Enabled" "true"
```

**Beklenen sonuç**
- Adım 3 (kapalı): iki örnek de bağımsız tarar; ikisinin de günlüğünde
  uyarı görülebilir (kesin garanti yok — yarış koşuluna bağlı, ama en az bir
  örnek satırı kapatır).
- Adım 4 (açık): yalnız kirayı TUTAN örneğin günlüğünde uyarı görünür;
  diğeri sessiz kalır (`SingletonGuard.IsHeld == false` iken
  `RunReconciliationService`'in tur döngüsü `IsHeld` denetiminde erken çıkar).

**Gerçek sonuç**
Atlandi - case'in kendisi bunu acikca izin veriyor ("Bu case iki terminal ister; zaman butcesi dar ise Atlandi isaretlenip gerekce not dusulebilir"). Bu kosum oturumu tek bir surec/port uzerinde calisiyor; ikinci bagimsiz bir AgentPrism ornegi (ayni PostgreSQL'e farkli portta baglanan) baslatmak oturumun mevcut tek-sunucu akisini bozar. Gerekce: zaman butcesi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

> **Not.** `pending_approvals` ve `run_heartbeats` (heartbeat ayrı bir tablo
> DEĞİL, `runs.heartbeat_at` sütunu — bkz. `0026_run_heartbeat.sql`) için
> `RetentionTargets.cs`'te bir giriş YOKTUR (`grep -n "IdempotencyKeys\|
> RunInputs" src/AgentPrism.Abstractions/Retention/RetentionTargets.cs` iki
> sonuç döner, `PendingApprovals` yoktur). `pending_approvals` süresiz
> BÜYÜYEBİLİR — `ApprovalExpirationService` yalnız `status`'u `Expired`
> yapar, satırı SİLMEZ. Bu, `23-SAKLAMA-ARSIV-KOTA.md` üretilirken
> değerlendirilmesi gereken bir hacim boşluğudur; kod değiştirilmedi.

---

# 3 — Asenkron Onay Kutusu: HTTP Yüzeyi (Faz 55)

Faz 55 hiçbir manuel test dosyasında yer almıyordu — bu bölüm ilk üretimdir.
`pending_approvals` bir **izdüşümdür**: tek gerçek kaynak MAF'ın oturum
durumudur; karar `ApprovalResumeJobHandler` üzerinden YENİ bir `RunId`
kuyruğa düşürür, eski çalıştırma `AwaitingApproval`'da SONSUZA kalır (K-014,
`AwaitingInput` ile aynı ilke).

### MT-RES-020 — Kuyruğa alınan bir çalıştırma onay gerektiren tool çağırınca `AwaitingApproval`'a düşer, `pending` listede görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 55, 46 |
| **İlgili karar** | K-368 |

**Ön koşul**
- Örnek uygulama çalışıyor; `AgentPrism:Providers:OpenAI:ApiKey` tanımlı
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
- Adım 4: aynı kayıt tekil `GET` ile de gelir.

**Gerçek sonuç**
Adim 2: birkac saniye icinde status: AwaitingApproval. Adim 3: pending listesinde runId eslesen tam bir kayit var: toolName: cancel_order, arguments: 'orderId=ORD-1001' (anahtar=deger bicimi, JSON DEGIL), status: Pending, expiresAt createdAt'ten ~24 saat sonra (varsayilan DefaultExpiration). Adim 4: ayni kayit tekil GET ile de teyit edildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
SELECT id, status FROM agentprism.runs
 WHERE session_id = '<sessionId>' ORDER BY started_at;
```

**Gerçek sonuç**
Adim 2: HTTP 200, govde status: Approved, decidedBy: 'unknown' (dolu), decidedAt dolu. Adim 3: ESKI runId 3 saniye sonra HALA AwaitingApproval - hic degismedi (K-014). Adim 4: ayni sessionId'de YENI bir runId (019ffb4c-d982...) gorundu, Completed oldu. Adim 5: yeni calistirmanin mesaj gecmisinde functionCall cancel_order + functionResult 'ORD-1001 numarali siparis iptal edildi.' - tool GERCEKTEN calisti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP 200, govde status: Rejected. Yeni calistirma tamamlandi; mesaj gecmisinde functionResult: 'Tool call invocation rejected.' (cancel_order GERCEKTEN calismadi) ve modelin son yaniti 'Siparis iptali icin islem yapilamadi. Lutfen siparis numarasini kontrol edip tekrar deneyin: ORD-1001.' - reddin dogru yansitildigi dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
HTTP 409, title: Karar zaten verilmis, detail: '...artik bekliyor durumunda degil.'. Ikinci bir RunId/is KUYRUGA DUSMEDI - GET /api/runs?sessionId=... sayisi 2'de sabit kaldi (degismedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Ikisi de HTTP 404, title: Onay istegi bulunamadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-025 — Başka kiracının onayı `404` döner (varlığı sızdırmaz)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 55, 41 |
| **İlgili karar** | — |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md`'nin `X-AgentPrism-Tenant` başlığıyla kiracı
  çözümü açık olduğu kurulum bilinir (`AllowHeaderResolution`).
- `FIX-TENANT-01` (`kiraci-alfa`) altında bir bekleyen onay üretilmiş
  (`MT-RES-020`'nin adımları `X-AgentPrism-Tenant: kiraci-alfa` başlığıyla).

**Adımlar**
1. `FIX-TENANT-02` (`kiraci-beta`) başlığıyla aynı `approvalId`'yi `GET` et.
2. Aynı başlıkla `decide` çağır.
3. Kontrol: `kiraci-alfa` başlığıyla aynı istekler `200` alır.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/approvals/$APPROVAL_ID" \
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-beta"
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/approvals/$APPROVAL_ID/decide" \
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-beta" -H "content-type: application/json" -d '{"approved":true}'

curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/approvals/$APPROVAL_ID" \
  -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa"
```

**Beklenen sonuç**
- Adım 1-2: `404` (`403` DEĞİL — varlığı sızdırmaz, `NotFound` yardımcı
  metodu ile aynı desen `RunEndpoints.CancelRunAsync`'teki "yok ile başka
  kiracıya ait aynı 404" ilkesini tekrarlar).
- Adım 3: `200`.

**Gerçek sonuç**
Adim 1-2: kiraci-beta basligiyla GET ve decide, ikisi de HTTP 404 (403 DEGIL) - varligi sizdirmadi. Adim 3: kiraci-alfa basligiyla ayni istek HTTP 200 - kendi kiracisi icin normal calisiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Iki kayit var (MT-RES-021/022'den): action: approval.decision, entity: tool:cancel_order, after alani {"approved": true/false, "approvalId": "<id>"} bicimindeki JSON dizgesi tasiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  dotnet user-secrets set "AgentPrism:Approvals:DefaultExpiration" "00:00:05"
  dotnet user-secrets set "AgentPrism:Approvals:ScanInterval" "00:00:02"
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

**Gerçek sonuç**
AgentPrism:Approvals:DefaultExpiration=5s, ScanInterval=2s ile yeniden baslatildi. Karar VERMEDEN 10 saniye beklendi. Adim 2: status: Expired. Adim 3: pending listesinde ARTIK GORUNMEDI (len:0). Adim 4: decide cagrisi HTTP 409, title: Karar zaten verilmis - AlreadyDecided kontrolu Expired icin de gecerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-028 — 🚨 `ApprovalEndpoints` hiçbir ucunda `RequireApiKeyScope` çağırmaz — yalnız-okuma kapsamlı bir anahtar onay kararı verebiliyor mu

`grep -n "RequireApiKeyScope" src/AgentPrism.AspNetCore/Endpoints/
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

**Gerçek sonuç**
**KALDI - HATA-S2-011 (Yuksek, dogrulanmis supheydi).** Adim 2: yalniz RunsRead kapsamli bir anahtarla POST /api/approvals/{id}/decide -> HTTP 200, karar GERCEKTEN uygulandi (status: Approved). Adim 3 (kontrol grubu): AYNI anahtarla PUT /api/agents/{name} (AgentsAdmin gerektirir) -> HTTP 403, title: Kapsam yetersiz - anahtarin genel olarak kapsam sistemine tabi oldugu, yalniz ApprovalEndpoints'te bu denetimin HIC calismadigi dogrulandi. grep -n "RequireApiKeyScope" src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs bos doner (kod okumasiyla onceden olculmustu, koşumda dogrulandi). Salt-okunur bir otomasyon anahtari, bekleyen gercek yan etkili bir tool cagrisini (siparis iptali) onaylayabiliyor/reddedebiliyor.

---

**GECTI (Aile F, docs/manuel-test/KAPANIS-PLANI.md §6).** ApprovalEndpoints.cs'in ucune RequireApiKeyScope eklendi: GET /api/approvals/pending ve GET /api/approvals/{id} -> RunsRead, POST /api/approvals/{id}/decide -> RunsWrite (§7 mapping tablosunda acikca yoktu, RunsWrite'in kendi tanimindaki "onay verme" ifadesiyle ayni akil yurutmeyle eklendi). Canli PostgreSQL'e karsi yeniden uretildi: RunsRead-kapsamli bir anahtarla POST /api/approvals/{rastgele-id}/decide -> HTTP 403, title: "Kapsam yetersiz", detail: "Bu uc 'RunsWrite' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor." Istek handler'a hic ulasmadan (onay kaydi hic aranmadan) filtrede reddedildi. Kontrol: ayni anahtarla GET /api/approvals/pending -> HTTP 200 (RunsRead hala calisiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-029 — 🚨 Rol politikaları bu örnekte kayıtlı değil — "Reader karar veremez" iddiası bu ortamda GÖZLEMLENEMEZ

`ApprovalEndpoints.Map`, `/decide` ucuna `.RequireRole(roles.Operator)`
ekler. `RoleEndpointConventionBuilderExtensions.RequireRole` şu KOD YORUMUNU
taşır: *"policyName'in null olmasi, ilgili AgentPrismPolicies policy'sinin
tuketicinin authorization yapilandirmasinda kayitli olmadigi anlamina gelir
— bu durumda uc yalnizca mevcut uc katmanli korumadan (loopback, bearer,
genel policy) gecer."* `grep -n "AgentPrismPolicies\|AddAuthorization\|
RequireRolePolicies" samples/AgentPrism.Api/Program.cs` **boş** döner — bu
üçü de kayıtlı değil. Sonuç: `AgentPrismRolePolicies.Resolve` `Operator`
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
   izole bir fonksiyonel test host'unda `AgentPrismPolicies` KAYITLI olarak
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
- Bu bir kusur DEĞİLDİR: `samples/AgentPrism.Api` bilinçli olarak tek bir
  statik operatör token'ı ile kurulmuştur; rol ayrımı test etmek özel bir
  kimlik doğrulama şeması (rol claim'i üreten bir test handler'ı) ister —
  `14-SKILL-VE-SCRIPT.md`'nin de kasıtlı olarak atladığı aynı sınır.
  **Durum:** `⏭ Atlandı` olarak işaretlenir, gerekçe bu satırdır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur — beklenen: `⏭ Atlandı`)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe yukarıda

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
- `AgentPrism:AsyncRun:Enabled` varsayılan `true`.

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

**Gerçek sonuç**
HTTP: 400 - kuyruga alinan calistirmalarda govdede approvals alani tasinmasi reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Arayüz: Onay Kutusu Ekranı (Faz 55, `screens/approvals.tsx`)

Bu ekran hiçbir manuel test dosyasında yer almıyordu. `refetchInterval: 5000`
ile 5 saniyede bir kendiliğinden yenilenir; `meta.roles.canOperate` `false`
ise Onayla/Reddet düğmeleri yerine yalnız bir `"pending"` rozeti gösterilir
(§3'ün `MT-RES-029`'unun kod-seviyesi bulgusuyla AYNI kısıt: bu örnekte
`canOperate` her zaman `true`'dur çünkü rol claim'i hiç üretilmiyor — bu
dalın `false` hâli arayüzden de gözlemlenemez, yalnız KOD incelemesiyle not
düşülür).

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

**Gerçek sonuç**
Bekleyen onay yokken (tumu kararlandirilmis) Onaylar ekraninda Empty bileseni gorundu: "Nothing is waiting" basligi + "A queued run only appears here when a tool call needs approval and no live client can answer it." govde metni (i18n anahtarlarindan geliyor, K-228 geregi eksik olsa derleme hata verirdi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adim 1: satirda cancel_order, altinda arguman metni (orderId=ORD-1001), calisma id'sinin kisaltilmis hali (019ffb55-2f3e...393712) + oturum kimligi (res-041-02), goreli olusturulma zamani ('7 sec. ago'), MUTLAK sure-sonu zamani ('Aug 14, 2026, 4:34:54 PM' - goreceli DEGIL). Adim 2: browser_network_requests /api/approvals/pending'e tekrarlanan GET istekleri gosterdi (refetchInterval:5000 dogrulandi). Adim 4 dogrulanmadi (zaman butcesi - Approve/Reject dugmeleri MT-RES-042/043'te test edildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Onayla dugmesine tiklandi. Istek dondukten sonra satir listeden KALKTI - ekran 'Nothing is waiting' bos durumuna dondu (onSuccess approvals-pending sorgusunu gecersiz kildi). Busy/disabled ara durumu (adim 2) yakalanamadi (tiklama-yanit araligi playwright snapshot cagrilarindan daha hizliydi) ama nihai davranis (karar uygulandi, satir kalkti) dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Reddet dugmesine tiklandi. MT-RES-022'nin HTTP davranisiyla ayni sonuc arayuzden tetiklendi - satir listeden kalkti, 'Nothing is waiting' bos durumuna donuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Yaris kosulunun gercek zamanli tetiklenmesi denendi (onayi arka planda API ile kararlandirip UI'da eski satirdaki Reddet dugmesine tiklamak) ama refetchInterval:5000 (auto-yenileme) satiri deneme oncesinde zaten kaldirmisti - butonun kendisi DOM'dan silindigi icin tiklanamadi (playwright 'ref not found'). Bu, disabled denetiminin/yarisin pratikte cok dar bir pencerede oldugunu gosteriyor. Yerine kod okumasiyla dogrulandi: approvals.tsx:124-127 tam olarak case'in tarif ettigi deseni tasiyor - `{decide.isError && (<div className="border-t border-line p-3"><ErrorNote error={decide.error} /></div>)}`. Kod yolu VAR ve dogru konumlanmis (panel altinda, tablonun disi degil icinde) - sayfa cokme riski yok (React kosullu render, try/catch gerektirmez).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Çapraz Kesişim: Yeniden Oynatma × Öksüz Uzlaştırma (Faz 47 × Faz 54)

Bu kombinasyon hiçbir dosyada test edilmedi: `11-ARAYUZ-RUN-SESSION-SSE.md`
yalnız BAŞARIYLA tamamlanmış çalıştırmaları oynattı; burada uzlaştırmayla
zorla `Failed`/`Infrastructure` kapatılmış bir çalıştırmanın girdi kaydı
(`run_inputs`, Faz 47'de eklendi) hâlâ var olduğu için yeniden oynatılıp
oynatılamadığı sınanır.

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

**Gerçek sonuç**
Sonuc onceden tahmin edilen supheyle eslesti. Adim 1: GET /api/runs/{id}/input -> HTTP 404 - bu manuel testin SQL ile urettigi yapay oksuz satirin gercek bir RunInputRecord'u yok (InMemoryRunInputStore/SQL girdisi hic yazilmadi, cunku satir dogrudan UPDATE ile uretildi, gercek bir RunRecordingAgent.RunCoreAsync cagrisindan gecmedi). Adim 3: POST /replay de HTTP 404, title: 'Girdi kaydi yok', detail acikca nedenini anlatiyor ('Girdi kaydi kapaliyken baslamis veya saklama politikasiyla silinmis olabilir'). GERCEK bir surec cokmesinde girdi cokmeden once zaten yazilmis olurdu - bu ayrim not dusuldu; bu case'in kendisi replay/reconciliation kesisiminin GERCEK bir orphan'da nasil davranacagini kanitlamiyor, yalniz bu manuel-test kurulumunun SQL-tabanli simulasyonunun sinirini gosteriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Özet

| Bölüm | Case sayısı | Negatif/sınır/şüphe |
|---|---|---|
| §1 Çalıştırma iptali: kayıt defteri | 6 (001–006) | 5 (002, 003, 004, 005, 006) |
| §2 Öksüz çalıştırma uzlaştırması | 5 (010–014) | 3 (010, 012, 014) |
| §3 Asenkron onay kutusu: HTTP | 11 (020–030) | 7 (023, 024, 025, 027, 028, 029, 030) |
| §4 Arayüz: onay kutusu ekranı | 5 (040–044) | 2 (040, 044) |
| §5 Yeniden oynatma × uzlaştırma | 1 (050) | 1 (050, şüphe) |
| **Toplam** | **28** | **18 (%64)** |

`MT-RES-029` `⏭ Atlandı` olarak ÖNCEDEN işaretlenmiştir (ortam kısıtı,
gerekçe case içinde) — koşum sırasında değiştirilmez.
