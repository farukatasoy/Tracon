# Faz 147 — Yetkilendirme Kapısının Kaynak Kapsamı

> **Durum:** ✅ Tamamlandı (2026-09-05)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-195** (tüketici turu 4, A1 · kapsam yarısı)
> **Önkoşul:** Yok — [Faz 139](139-CALISTIRMA-VE-OTURUM-YETKILENDIRMESI.md) sözleşmeyi zaten sevk etti; bu faz onun kapsamını tamamlar
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — iki enum'a üye, bir request record. `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya (yalnız başlık), **shipped giriş sıfır**: bugün eklemek bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `guides/embedding.md`, `concepts/governance.md`, `concepts/runs.md`, `guides/voice.md`, `capabilities.md` · sevk edilen: `IRunAuthorizationHandler` XML `<example>`, `src/AgentPrism.Abstractions/README.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 1881ff31:docs/arsiv/fazlar/147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md
> ```
>
> Damıtıldı 2026-09-05 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 139 `IRunAuthorizationHandler`'ı sevk etti ve **run başlatan** yüzeyleri kapıya bağladı. Kapı orada durdu. Bugün bir tüketici bu kapıyı doğru kurduğunda bile, aynı kiracıdaki bir `Reader` başka bir kullanıcının `run`'ının metnini, girdisini, trace'ini, tool çağrılarını ve eklerini okuyabiliyor; bir `Operator` onu iptal edebiliyor.

## Bitiş Ölçütleri (DoD)

- [x] Handler kaydedilmemiş kurulumda **hiçbir** davranış değişmez — 21 ucun hepsi için kanıt. **Tek istisna, bilinçli:** `GET /api/runs/{id}/tools` var olmayan bir `run` için artık `200 []` yerine `404` döner (K-685, Plandan Sapmalar 1)
- [x] 21 çağrı yerinin hepsi kapıdan geçer; `RunResourceAuthorizationTests` her birini **ayrı ayrı** kanıtlar. Denetim **beş** yüzey daha buldu (workflow `resume` · `respond` · `checkpoints` · `requests`, eval `judge` ve `cases/from-run`); hepsi kapatıldı — bkz. Denetim Bulguları
- [x] `POST /api/runs/{id}/replay` reddedildiğinde `403` döner, `runs` satırı **açılmaz** ve kota tüketilmez
- [x] `/v1/chat/completions` akışlı ve akışsız dalda ayrı ayrı kapsanır
- [x] `throw` eden handler her kaynağı **reddeder** (fail-closed)
- [x] Reddedilen tekil kaynak `404` döner ve gövdesi var olmayan kaynakla **birebir aynıdır**; reddedilen liste `403` döner
- [x] `RunAuthorizationCoverageTests` run başlatan yüzeyleri (yedi girdi, `WorkflowEndpoints.cs` **iki** marker ile) ve **sekiz** kaynak erişimi dosyasını sayar; testin dokümanı düzeltildi
- [x] Ses: başka kullanıcının oturumuna bağlanma reddedilir; var olmayan session'ın ilk turu **hâlâ açılır** (K-283 korunur)
- [x] `RunAccess`/`SessionAccess` XML'i sayısal değerin bir persistence sözleşmesi **olmadığını** açıkça söyler
- [x] `OpenApiSnapshotTests` yeşil; hiçbir ucun `200` yanıtı sessizce düşmedi
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı ve reddeden bir handler'la ret çıktısı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi (MT-SEC-151 … MT-SEC-163). On üçünün de otomatikleştirilmiş karşılığı yeşil; ikisi (MT-SEC-152 · MT-SEC-157) ayrıca `samples/AgentPrism.Embedded` üzerinde elle koşuldu ve sonuç case'e yazıldı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (`guides/embedding.md` genişleme noktası anlatısı, `concepts/governance.md`, `guides/voice.md`); `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Kapı çağrısı sayımı — beklenen: her dosyada > 0
for f in Endpoints/RunEndpoints Endpoints/ObservabilityEndpoints \
         Endpoints/AttachmentEndpoints Endpoints/ApprovalEndpoints \
         OpenAICompat/OpenAIChatCompletionsEndpoints Voice/VoiceConversationEndpoint; do
  printf '%-55s %s\n' "$f" \
    "$(grep -cE 'RunAuthorizationGate\s*\.\s*Check' "src/AgentPrism.AspNetCore/$f.cs")"
done

# Reddedilen run okuması, var olmayan run ile AYNI gövdeyi döndürmeli
diff <(curl -s "$APU/api/runs/$OTHER_RUN" -H "$APB") \
     <(curl -s "$APU/api/runs/00000000-0000-0000-0000-000000000000" -H "$APB")
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Neden |
|---|---|---|---|
| 1 | `GET /api/runs/{id}/tools` `Read` + `404` sınıfında; "hiçbir davranış değişmez" | Uç artık `run`'ı **önce okuyor**; var olmayan/başka kiracıya ait `run` için `200 []` yerine `404` dönüyor | **Kullanıcı kararı, K-685.** Plan bu ucun bugün `run`'ı hiç okumadığını ölçmemişti. Ret `404` dönerken yokluk `200 []` dönseydi ret bir **varlık kanıtı** olurdu — K-671'in kapatmak için var olduğu yan kanalın aynısı. Bedel: DoD 1'in "hiçbir davranış değişmez" cümlesinden bilinçli bir sapma ve bir ek depo sorgusu. Uç ayrıca kardeşleriyle (`/trace`, `/input`, `/feedback`) tutarlı hâle geldi |
| 2 | `POST /api/attachments` (yükleme) ret kodu `404` | `403` | **Kullanıcı kararı, K-686.** Yükleme adreslenen bir kaynak taşımaz; "Attachment not found" yanıtı işlemle çelişirdi ve gizlenecek kimlik zaten yok. `GET`/`DELETE /api/attachments/{id}` `404` kaldı |
| 3 | Ses reddi "bugünkü ret yolunu izler ve **aynı** durum kodunu kullanır" | `404`, `OwnsSessionAsync` başarısızlığının gövdesiyle birebir | **Kullanıcı kararı, K-687.** Plan cümlesi belirsizdi: uçta **iki** bugünkü ret yolu var (kimlik doğrulama `401`, erişilemeyen oturum `404`). `401` kimlik doğrulaması başarılı bir çağıranı yanlış yönlendirirdi, `403` oturumun varlığını doğrulardı |
| 4 | Replay kapıyı `CheckRunAsync` ile çağıracaktı (plan "run başlatma" sınıfına koymuştu) | `CheckRunResourceAsync` + `RunAccess.Start` + kaynak `run`'ın `RunId`'si | Ölçüm: `CheckRunAsync` `RunId` taşıyamıyor. Onsuz bir handler "bu agent'ı çalıştır" ile "başkasının kayıtlı konuşmasını yeniden oynat"ı **ayırt edemez** — replay'i kapsamanın tek gerekçesi tam olarak bu ayrımdı. `RunAuthorizationCoverageTests` bu yüzden `RunEndpoints.cs` için ayrı bir regex kullanıyor |
| 5 | Kapı ret yanıtını kendisi üretecekti (plan `CheckRunResourceAsync`'i `ProblemHttpResult?` dönen bir yardımcı olarak tarif ediyordu) | Ret yanıtı **çağırandan** `denied` parametresiyle alınıyor | Ölçüm: ret metni uçtan uca değişiyor (`"Run not found"` · `"Trace not found"` · `"Attachment not found"` · `"Approval request not found"`). Kapının tek bir metin üretmesi en az bir uçta gövde birebirliğini bozardı — ve o ayrışma bir varlık oracle'ıdır |
| 6 | 21 çağrı yeri | **23** çağrı yeri (21 HTTP ucu + ses; `CompareRuns` **iki kez** çağırıyor) | `CompareRuns` iki `run` okur; plan bunu 147.1'de zaten söylüyordu ama toplam sayıya tek çağrı olarak katmıştı |

**Sapma olmayan, doğrulanan iddialar:** Açık Soru 3'ün ölçümü yapıldı —
`AttachmentEndpoints`'in yükleme ucu `RunId`'yi **hiç doldurmuyor**
(`AttachmentDescriptor.RunId` yalnız agent'ın ürettiği ekler için dolu). Bu
yüzden yüklemede `RunId` `null` gidiyor, indirme/silmede `descriptor.RunId`
gidiyor — planın öngördüğü davranışın aynısı.

## Bu Fazda Verilen Kararlar

| # | Karar |
|---|---|
| K-683 | Kaynak yetkilendirmesi AYRI bir sözleşme açmaz: `RunAccess`/`SessionAccess` büyür, `RunAuthorizationRequest` `RunId` kazanır ve `AgentName` `required` olmaktan çıkar; `IRunAuthorizationHandler`'ın metot sayısı DEĞİŞMEZ. Enum'un sayısal değeri bir persistence sözleşmesi DEĞİLDİR ve bu XML'e açıkça yazılır 👤 |
| K-684 | Reddedilen TEKİL kaynak `404` (gövdesi var olmayanla birebir aynı), reddedilen LİSTE `403`; kapı kiracı kontrolünden SONRA, durum okumasından ÖNCE sorulur; ret yanıtını çağıran verir |
| K-685 | `GET /api/runs/{id}/tools` var olmayan `run` için `200 []` değil `404` döner 👤 |
| K-686 | `POST /api/attachments` reddi `403` döner, `404` değil 👤 |
| K-687 | Ses WebSocket yetkilendirme reddi `404` (erişilemeyen oturumla birebir aynı); var olmayan oturum reddedilmez, K-283 korunur 👤 |

## Denetim Bulguları

`faz-denetim` koşuldu (2026-09-05, taze bağlamlı bağımsız denetçi). **Üç 🔴
bulgu** çıktı ve üçü de gerçekti — üçü de "kapı hangi yüzeye ulaşmıyor"
sorusunun cevabıydı, yani tam olarak bu fazın işi. Hepsi kapatıldı.

### 🔴 Kapatıldı

| # | Bulgu | Nasıl kapatıldı |
|---|---|---|
| 1 | `DELETE /api/runs/{runId}/feedback/{scoreId}` kapıyı **`runId`** için soruyor ama silmeyi **`scoreId`** ile yapıyordu; `IRunScoreStore.DeleteAsync` `runId` almadığı için iki kimlik hiç bağlanmamıştı. Kendi `run`'ını puanlamaya izinli bir çağıran, rotada kendi `run`'ını yazıp **başkasının skorunu** silebiliyordu | Silmeden önce skorun **gerçekten o `run`'a ait olduğu** doğrulanıyor (`scores.ListAsync(tenantId, runId)` üyelik kontrolü); değilse `404`. Kanıt: `Feedback_delete_refuses_a_score_that_belongs_to_a_different_run`. Kusur Faz 147'den ÖNCE de vardı — kapı onu görünür yaptı, üretmedi |
| 2 | `POST /api/workflows/runs/{id}/resume` ve `/respond` **yeni bir `run` satırı açıyor** ama ne yetki ne kota kapısından geçiyordu. "Run başlatan yüzey sayısı altıdır" iddiası **eksikti** | İkisi de `RunAccess.Start` + kaynak `run`'ın `RunId`'siyle kapıya bağlandı; ret `403`. `RunAuthorizationCoverageTests` artık `WorkflowEndpoints.cs`'i **iki kez** sayıyor (`CheckRunAsync` **ve** `RunAccess.Start`) — tek bir dosya girdisi ilk çağrıda yeşile döner ve diğer ikisini gizlerdi. Kanıt: `Denied_workflow_resume_returns_403_and_opens_no_run_row`, `Denied_workflow_respond_returns_403` |
| 3 | `POST /api/runs/{runId}/judge` bir `run`'ı okuyup içeriğinden **skor yazıyordu**; kapı çağrısı yoktu. `RunAccess.Read` ve `RunAccess.Feedback`, aynı `/api/runs/{id}/…` yol önekinde bypass edilebiliyordu | Uç kapıdan **iki kez** geçiyor (`Read` ve `Feedback`); herhangi biri reddederse çağrı reddedilir. Kanıt: `Denied_judge_returns_404_and_writes_no_score`, `Denied_judge_is_refused_on_the_feedback_half_too` |

### 🟡 Kapatıldı

| # | Bulgu | Nasıl kapatıldı |
|---|---|---|
| 4 | `A_cancelled_request_is_not_swallowed_into_a_denial` hiçbir şey ayırt etmiyordu: `EnsureSuccessStatusCode()` her 2xx-dışı yanıtta patladığı için `OperationCanceledException` yutulsa da test **yeşil kalırdı** | İddia ayırt edici hâle getirildi: yanıt `404` **olmamalı** (yutulsaydı tam olarak o gelirdi) ve `200` de olmamalı |
| 5 | DoD 1 ("21 ucun hepsi için kanıt") 21 değil **16** uç için kanıtlıydı — `replay`, `/v1/chat/completions`, `DELETE .../feedback/{scoreId}` ve HTTP ek yükleme testte hiç çağrılmıyordu | Dördü de `Every_resource_endpoint_is_unchanged_when_no_handler_is_registered`'a eklendi. Ayrıca workflow devam uçları için ayrı bir kanıt yazıldı: `Workflow_continuation_is_unchanged_when_no_handler_is_registered` |
| 6 | Planın "alt sistem hatası: `IRunStore` hata verir" satırının karşılığı yoktu | `A_failing_run_store_does_not_turn_into_an_allow`: okunamayan bir `run` handler'a **hiç ulaşmıyor** ve istek yetkilendirilmiş bir okuma değil `404` ile bitiyor. Depo canlı host'ta değiştirilebilir olmadığı için ulaşılabilir en yakın eşdeğer ölçüldü — sınır bu, dokümanda açık |
| 7 | `docs/openapi/agentprism.json` yeni `403`/`404` yanıtları kazandı ama `src/AgentPrism.Client/Generated/AgentPrismApiClient.g.cs` yeniden üretilmemişti; drift kapısı yalnız `operationId` ↔ metot adı karşılaştırdığı için bunu göremiyordu | İstemci reçeteyle yeniden üretildi (`nswag-prepare` → `nswag run` → `postprocess` → `json-context`). Eşlenmiş dalda tüketici artık `AgentPrismApiException<ProblemDetails>` alıyor, ham `string` değil |
| 8 | Aynı sınıftan üç **yalnız okuyan** yüzey daha kapısızdı: `GET /api/workflows/runs/{id}/checkpoints`, `.../requests`, `POST /api/evals/{name}/cases/from-run/{runId}` | Üçü de `RunAccess.Read`'e bağlandı; ret `404`. `EvalEndpoints.cs` ve `WorkflowEndpoints.cs` kapsam kapısının **kaynak** listesine eklendi. Kanıt: `Denied_workflow_checkpoints_and_requests_return_404` |

### 🟢 Aday listesine devredildi

| # | Bulgu | Neden şimdi değil |
|---|---|---|
| 9 | `GET /api/runs/{id}/events` kapıyı akış başlamadan **bir kez** soruyor; uzun süren bir SSE bağlantısı sırasında yetki geri alınırsa akış devam eder | Planın bilinçli kararı: durum kodu akış başladıktan sonra değiştirilemez. Periyodik yeniden yetkilendirme ayrı bir tasarım kararıdır |
| 10 | Kapsam kapısı **dosya** düzeyinde sayıyor; bir dosyada tek bir çağrı kalması tüm dosyayı "kapsanmış" gösterir — bulgu 2 tam olarak bu boşluktan geçti | Bulgu 2 için `WorkflowEndpoints.cs`'e ikinci bir marker eklenerek **noktasal** kapatıldı, ama genel çözüm (rota ↔ çağrı eşlemesi) ayrı bir işçiliktir |

### Denetçinin temiz bulduğu başlıklar

3.5 (imza-gövde kayması — eklenen her `runAuthorizationHandler` parametresinin
gövdesinde çağrı var), 3.6 (plan dışı public API yok), 3.7 (İngilizce metin,
her public üyede XML, `ConfigureAwait(false)`, `secret` yok, MAF sarmalama yok,
handler yokken kapı tam no-op).

Denetçi ayrıca üç kullanıcı kararının gerekçesinin **kodda görünür** olduğunu
doğruladı ve `OpenApiSnapshotTests` riskinin gerçekleşmediğini ölçtü.

## Sonraki Faza Devir Notu

[Faz 148](../../148-OTURUM-SAHIPLIGININ-KALICILIGI.md) bu fazın hemen üstüne biniyor:
Faz 147 kapıyı **her kaynağa** ulaştırdı ama sahipliği hâlâ **öğretmiyor** —
`IRunAuthorizationHandler` tüketicinin kendi kaydına soruyor. Faz 148 o kaydı
AgentPrism'in içine taşıyacak. Devreden dört gerçek bilgi:

- 🚨 **Yeni bir run başlatan VEYA run kaynağına dokunan HTTP yüzeyi eklenirse**
  iki şey birden yapılmalıdır: yüzey kapıyı **kendi gövdesinde** çağırır ve
  `RunAuthorizationCoverageTests`'in **doğru listesine** eklenir
  (`ExpectedRunStartingFiles` altı dosya · `ExpectedResourceFiles` altı dosya).
  Tarama dosya bazındadır ve yeni bir dosyayı **kendiliğinden keşfedemez** —
  Faz 139 tam olarak bunu kaçırdı ve iki yüzey (replay, `/v1/chat/completions`)
  kapının dışında kaldı.
- 🚨 **Kapı sahipliği BİLMİYOR.** Çağrı yerleri yalnız handler'a soruyor; Faz
  148 depoyu ve sorguyu değiştirecek, **çağrı yerlerini değil**. Planın bu
  öngörüsü doğru çıktı — 23 çağrı yerinin hiçbiri sahiplik kavramına dokunmuyor.
- 🚨 **`RunAuthorizationRequest.RunId` Faz 148'in giriş noktasıdır.** Sahiplik
  kalıcı hâle geldiğinde varsayılan handler artık `AllowAll` olmayabilir; o
  karar alınırsa `RunResourceAuthorizationTests`'in "handler kayıtlı değilken
  hiçbir davranış değişmez" testi **kasten kırmızıya döner** ve bu bir
  regresyon değil, kararın kanıtıdır.
- **Ses `SessionAccess.Voice` ile kapsandı ve K-283 korundu.** Var olmayan bir
  oturum hâlâ açılıyor; Faz 148 oturum sahipliğini kalıcılaştırırken bu
  davranışı **bozmamalıdır** — `VoiceAuthorizationTests` ve MT-SEC-163 onu
  kilitliyor.
