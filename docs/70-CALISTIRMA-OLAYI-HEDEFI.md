# Faz 70 — Çalıştırma Olayı Hedefi ve Düşünme Akışı

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-115**
> **Önkoşul:** [Faz 6](06-GOZLEMLENEBILIRLIK.md) — `RunRecordingAgent` ve olay yazımı · [Faz 61](61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) — gömülebilir bileşen, taşıyıcı tarafının istemci yarısı
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **büyüyor (küçük)** — bir arayüz ve `RunEventType`'a **bir ekleme**. Enum sonuna ekleme K-040 ile serbesttir. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/runs.md`, `guides/observability.md`, `concepts/agents.md` (reasoning)
> **Manuel test alanı:** [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-040\|K-014\|K-055\|K-398" docs/KARARLAR.md
   ```
   **K-040** (enum'lar JSON'a ad olarak yazılır; sıra değişmez, yalnız eklenir),
   **K-014** (olay akışı yalnız eklemelidir), **K-055** (OTel toplama
   `ActivityListener` ile), **K-398** (erken `DisposeAsync`'te terminal durum yazılır).
3. [`61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md`](61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md
   ```
   SSE başlıklarının akış başlamadan gönderilme kısıtı (K-439) bu fazı ilgilendirir.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (🚨 `AsyncLocal` ve akışlı yol — **dört kez** bedel ödetti) ·
   [`hafiza/frontend.md`](hafiza/frontend.md) (arayüz, bundle)

---

## Amaç

AgentPrism'in olay akışı zengindir ama **tek bir tüketicisi** vardır: veritabanı.
Kendi gerçek zamanlı arayüzüne gömen bir tüketici bu akışa ancak kendi HTTP
sunucusuna SSE ile bağlanarak ya da `IRunStore`'u dekore ederek ulaşır. İkisi de
yanlış yerdir. Bu faz, olayları süreç içinde dinlenebilir kılar.

İkinci parça aynı yerdedir: modelin düşünme çıktısı bugün kayda **hiç girmiyor**.

- **F-115** — kayıt edilebilir bir çalıştırma olayı hedefi ve `ReasoningDelta`
  olay tipi.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunEventWriter.cs:22`](../src/AgentPrism.Core/Recording/RunEventWriter.cs) | Tek hedef `IRunStore`. Gözlemci genişleme noktası yok |
| `grep -rn "IRunEventSink\|IRunEventObserver" src --include="*.cs" \| wc -l` → **0** | Kavram kod tabanında yok |
| [`RunEventType.cs`](../src/AgentPrism.Abstractions/Runs/RunEventType.cs) | Yirmi iki değer, `0`–`21`. `ReasoningDelta` yok |
| [`RunRecordingAgent.cs:947-965`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | `WriteContentsAsync` `switch`'i üç tipi tanır: `TextContent`, `FunctionCallContent`, `FunctionResultContent`. Geri kalan `default: break` |
| `Microsoft.Extensions.AI.Abstractions` **10.8.3** | `TextReasoningContent` **vardır** ve MAF onu üretir |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

🚨 **Aday listesindeki ifade ölçümle düzeltildi.** F-115 "düşünme akışı
`MessageDelta`'ya karışır" diyordu. Gerçek daha kötüdür: `TextReasoningContent`
`default: break` dalına düşer ve **hiç kaydedilmez**. Karışma değil, kayıp.

---

## 70.1 — Olay hedefi

```mermaid
flowchart LR
    A["RunRecordingAgent"] --> W["RunEventWriter<br/>tek yazici, sira numarasi"]
    W --> S["IRunStore<br/>kalicilik"]
    W --> K["IRunEventSink<br/>0..n, varsayilan bos"]
    K --> X["SignalR / kuyruk / kendi arayuzu"]
```

**Dört kural bozulmaz:**

1. **Tek yazıcı korunur.** Sıra numarasını `RunEventWriter` üretir. Hedef
   olayları **numaralanmış hâlde** alır; canlı akış ile sonradan yapılan replay
   birebir aynı kalır. Bu, paketin bugünkü en güçlü garantilerinden biridir.
2. **Hedef bir `run`'ı asla bozmaz.** `RunEventWriter`'ın yut-ve-devam kuralı
   hedefe **birebir** uygulanır: hata loglanır, hedef o `run` için devre dışı
   kalır, `run` devam eder. Gözlemlenebilirlik işlevi düşürme hakkına sahip
   değildir.
3. **Hedef `run`'ı yavaşlatmaz.** Yazım `await` edilir ama hedef **sıcak
   yoldadır**; yavaş bir hedef akışı geciktirir. Plan bunu çözmez, **sınırlar**:
   sözleşme "hızlı ol, kuyruğa at" der ve doküman bunu açıkça yazar. Kuyruğa
   alma tüketicinin işidir.
4. **Varsayılan boştur.** Hiçbir hedef kayıtlı değilse bugünkü kod yolu
   birebir aynıdır — sıfır tahsis, sıfır dallanma farkı (K1).

### Neden `IRunStore` dekorasyonu yeterli değil

Bugün mümkün olan tek yol budur ve üç şeyi yanlış yapar:

| Sorun | Neden |
|---|---|
| Tüketici **tüm** kalıcılık sözleşmesini yeniden uygulamak zorunda | `IRunStore` on'dan fazla metot taşır; ilgilenilen tek şey olay akışıdır |
| Depo hatası ile yayın hatası **aynı** yut-geç kuralına girer | İkisi ayrı sorumluluktur; biri veri kaybı, diğeri gecikmiş bildirim |
| Sıra numarası garantisi tüketiciye devredilir | Yanlış uygulanırsa canlı akış ile replay ayrışır |

### 🚨 `AsyncLocal` uyarısı

Hedef, akışlı yolda çağrılır. MEMORY'de **dört kez** kayıtlı tuzak burada
geçerlidir: `AsyncLocal` yazımı çağırana geri akmaz ve akışlı yolda **her
`MoveNextAsync` öncesi** tekrarlanmalıdır. Hedef bir `Activity` veya kapsam
açıyorsa bunu kendi gövdesinde yapmalıdır; plan bunu XML dokümanına yazar.

---

## 70.2 — `ReasoningDelta`

`RunEventType`'ın **sonuna** `ReasoningDelta = 22` eklenir. Sıra değişmez
(K-040).

`WriteContentsAsync` `switch`'ine bir dal girer:

```csharp
case TextReasoningContent reasoning
    when _options.RecordReasoningDeltas && !string.IsNullOrEmpty(reasoning.Text):
    // ayrı olay tipi — MessageDelta'ya karıştırılmaz
```

### 🚨 Düşünme çıktısı varsayılan olarak kaydedilmez

`AgentPrismRunRecordingOptions.RecordReasoningDeltas` varsayılanı **`false`**
gelir. `RecordMessageDeltas`'tan farklı davranmasının üç gerekçesi vardır:

| Gerekçe | Açıklama |
|---|---|
| **Hacim** | Düşünme çıktısı yanıttan kat kat uzun olabilir; kayıt hacmi ölçülmedi |
| **Gizlilik** | Ara akıl yürütme kullanıcı verisini yanıtta görünmeyen biçimlerde tekrarlayabilir |
| **Sağlayıcı politikası** | Bazı sağlayıcılar ham düşünme metnini saklamaya kısıt koyar; kısıtlar **doğrulanmadı** |

Açmak isteyen bir satır yazar. K1 ile uyumludur ve
[Faz 25](25-VERI-SAKLAMA-VE-ARSIVLEME.md)'in saklama politikasıyla çelişmez.

**Not:** Faz 68 planlanmışsa `ReasoningTokens` alanı orada gelir. İkisi
bağımsızdır: token **sayısı** kullanımdan, `ReasoningDelta` **metinden** gelir.
Biri açılmadan diğeri çalışır.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions
/// <summary>
/// Receives run events as they are written. Registered instances must be fast:
/// they run on the hot path. Queue and return; do not block.
/// A sink failure never fails the run.
/// </summary>
public interface IRunEventSink
{
    ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default);
}

public enum RunEventType
{
    // ... 0-21 unchanged ...

    /// <summary>A reasoning (thinking) delta. Off by default.</summary>
    ReasoningDelta = 22,
}

// AgentPrism.Core
public sealed class AgentPrismRunRecordingOptions
{
    /// <summary>Records reasoning deltas. Default false — volume and privacy.</summary>
    public bool RecordReasoningDeltas { get; set; }
}
```

Kayıt `TryAddEnumerable` ile birden çok hedefe izin verir; sıra kayıt sırasıdır.

### HTTP `endpoint`'leri

Yeni uç yok. Var olan SSE akışı `ReasoningDelta` olayını **açıkken** taşır.

### Arayüz payı

Düşünme akışı için katlanabilir bir blok. Yeni bağımlılık yok. Bugünkü bundle
146.104 B brotli (`index-*.js.br` 140.614 + `index-*.css.br` 5.490); bütçe
250 KB gzip. Fazın payı kapanışta ölçülür. Yeni metin `en.ts` **ve** `tr.ts`
içine girer (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Runs/
├── IRunEventSink.cs                    (YENİ)
└── RunEventType.cs                     (değişir — ReasoningDelta = 22)

src/AgentPrism.Core/
├── Recording/RunEventWriter.cs         (değişir — hedeflere yayın)
├── Recording/RunRecordingAgent.cs      (değişir — TextReasoningContent dalı)
├── AgentPrismRunRecordingOptions.cs    (değişir — RecordReasoningDeltas)
└── AgentPrismServiceCollectionExtensions.cs (değişir — hedef kaydı)

src/AgentPrism.UI/                      (düşünme bloğu + locales)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Hedef patlar → `run` düşer | Fonksiyonel | `RunEventSinkFailureTests` |
| Hedef patlar → depoya yazım da durur | Fonksiyonel | aynı sınıf — iki yol **bağımsız** olmalı |
| Hedef olayları sırasız alır | Fonksiyonel (akışlı) | `RunEventSinkOrderingTests` |
| Hedef yavaşsa akış tamamen durur | Fonksiyonel | `RunEventSinkSlowTests` — davranış **belgelenir**, gizlenmez |
| Hedef kayıtlı değilken sıcak yol değişir | Birim | `RunEventWriterNoSinkTests` |
| Birden çok hedefte biri patlayınca diğeri de susar | Fonksiyonel | `RunEventSinkFailureTests` |
| `ReasoningDelta` varsayılan olarak yazılır | Birim | `ReasoningRecordingOptionsTests` |
| `ReasoningDelta` `MessageDelta` ile karışır | Fonksiyonel (SSE) | `ReasoningStreamTests` |
| Enum eklemesi eski istemcide çöker | Sözleşme | `RunEventTypeContractTests` — bilinmeyen tip **yok sayılır** |
| İptal edilen `run`'da hedef terminal olayı almaz (K-398) | Fonksiyonel | `RunEventSinkCancellationTests` |
| Hedef içinde `AsyncLocal` yazımı akışta kaybolur | Fonksiyonel | `RunEventSinkAmbientTests` |
| Başka kiracının olayı hedefe sızar | Sözleşme | `TenantIsolationContract` |

**Beş soru:** iptal — K-398 terminal olayı hedefe de gider · eşzamanlılık —
paralel `run`'lar tek hedefe yazar, hedef **thread-safe olmak zorundadır** ve bu
sözleşmeye yazılır · boş/aşırı girdi — çok uzun düşünme metni · başka kiracı —
sözleşme testi · alt sistem hatası — hedef ve depo bağımsız düşer.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Hedef kayıtlı değil | Bir `run` | Bugünkü davranış birebir |
| 2 | Olayları sayan bir hedef | Bir akışlı `run` | Hedefin gördüğü sıra numaraları **boşluksuz** ve `GET /api/runs/{id}/events` ile birebir aynı |
| 3 | Her zaman patlayan bir hedef | Bir `run` | `run` başarılı biter; uyarı loglanır; **depoya yazım tam** |
| 4 | İki hedef, biri patlıyor | Bir `run` | Sağlam hedef tüm olayları alır |
| 5 | Reasoning destekleyen model, seçenek **kapalı** | Bir `run` | `ReasoningDelta` olayı **yok**; yanıt normal |
| 6 | Aynı model, seçenek **açık** | Bir `run` | `ReasoningDelta` olayları akar; `MessageDelta`'dan ayrı |
| 7 | Arayüz, seçenek açık | Aynı `run`'ı arayüzde izle | 👤 Düşünme bloğu ayrı ve katlanabilir görünür |
| 8 | 2 sn uyuyan bir hedef | Bir akışlı `run` | 👤 Akış gözle görülür yavaşlar. **Belgelenmiş sınır** — hedef hızlı olmalıdır |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Hedef sıcak yolda mı, kuyrukta mı? | A: sıcak yol, "hızlı ol" sözleşmesi · B: paket içinde kuyruk | **A** — B bir kuyruk, bir tüketici görevi ve bir taşma politikası demektir; K1'i (sıfır sürpriz) zorlar. Kuyruğa alma tüketicinin bilinçli kararıdır |
| 2 | Hedef hatası kaç kez tolere edilir? | A: ilk hatada o `run` için susar (`IsDisabled` deseni) · B: her olayda yeniden dener | **A** — `RunEventWriter`'ın var olan deseniyle birebir aynı; ikinci bir davranış modeli öğretmez |
| 3 | `ReasoningDelta` saklama politikasına nasıl girer? | A: `MessageDelta` ile aynı kova · B: ayrı saklama süresi | **A** bu fazda; B ölçülmemiş bir ihtiyaçtır. [Faz 25](25-VERI-SAKLAMA-VE-ARSIVLEME.md) sahibidir |
| 4 | Hedef `RunEvent`'i mi yoksa daraltılmış bir görünümü mü alır? | A: `RunEvent` · B: ayrı bir DTO | **A** — ikinci bir tip iki sözleşme demektir; `RunEvent` zaten public |

---

## Bitiş Ölçütleri (DoD)

- [x] Hedef kayıtlı değilken sıcak yol değişmez (test kanıtıyla) —
      `RunEventSinkTests.No_sink_registered_leaves_the_run_unaffected`;
      `_sinks.Count == 0` erken dönüşü `DispatchToSinksAsync`'te
- [x] Hedefin gördüğü sıra numaraları depodakiyle birebir aynı —
      `RunEventSinkTests.A_sink_sees_the_exact_same_sequence_numbers_as_the_store`,
      `WorkflowEventSinkTests.A_registered_sink_sees_the_workflow_s_own_events`;
      gerçek koşumda da ölçüldü (aşağı bak, `0..10` boşluksuz)
- [x] Patlayan hedef `run`'ı düşürmez ve depoya yazımı **engellemez** —
      `RunEventSinkTests.A_sink_that_always_throws_is_disabled_after_its_first_failure_and_the_run_still_completes`,
      `One_sink_failing_does_not_silence_a_healthy_sink_registered_alongside_it`,
      `A_sink_still_receives_every_event_when_the_store_itself_is_disabled`
      (bu son test, denetim sırasında bulunan bir kusuru da kanıtlar — bkz.
      Plandan Sapmalar)
- [x] `RecordReasoningDeltas` varsayılanı `false`; açıkken `ReasoningDelta`
      olayları `MessageDelta`'dan ayrı akar — `ReasoningRecordingTests` (5
      test), gerçek koşumda da ölçüldü
- [x] Bilinmeyen olay tipi eski istemcide yok sayılır — `foldRunEvents`'in
      `default: break` dalı (`transcript.ts`) yapısal olarak garanti eder;
      ayrıca `Record<RunEventType,...>` (TypeScript) yeni bir üye eklendiğinde
      derleme hatası verir, bu da `ModelFallbackUsed`'ın frontend'de hiç
      tanımlanmadığı bağımsız boşluğunu ortaya çıkardı (bkz. Plandan Sapmalar)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/
      `format --verify-no-changes`, hepsi temiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
      — bkz. MT-UIRUN-048, gerçek Anthropic extended-thinking çağrısı: 7
      `ReasoningDelta` + 2 `MessageDelta` + `RunStarted` + `RunCompleted` = 11
      olay, sıra `0..10` boşluksuz, düşünme metni 253 karakter / yanıt 143
      karakter (hacim riskine ilk somut veri noktası)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri
      [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md)
      içine eklendi (MT-UIRUN-047/048/049); 047/048 API üzerinden koşuldu,
      049 (arayüzde katlanabilir blok) 👤 insan gerektirir olarak işaretlendi
      — paylaşılan Playwright tarayıcı oturumu meşguldü
- [x] `faz-denetim` koşuldu; 🔴 ve 🟡 bulgu yok (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz —
      `concepts/runs.md`, `guides/observability.md`, `ui.md`; `concepts/workflows.md`
      kasıtlı atlandı (gerekçe: Plandan Sapmalar)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — yeni bir
      i18n anahtarı GEREKMEDİ (`ReasoningBlock`/`transcript.reasoning` Faz
      70'ten önce zaten vardı); bundle 146,1 KB → **~148,2 KB** brotli
      (`postbuild.mjs` çıktısı), bütçe 250 KB gzip'in çok altında (172,8 KB
      gzip ölçüldü)

### Doğrulama komutları

```bash
# Reasoning olayı ayrı akıyor mu — claude-thinking zaten örnek uygulamada var
# (extended thinking, samples/AgentPrism.Api/Program.cs), "thinker" değil.
curl -N -s http://localhost:5080/agentprism/api/agents/claude-thinking/run/stream \
  -H "Authorization: Bearer manuel-test-token-2026" \
  -H 'content-type: application/json' -d '{"message":"think step by step"}' \
  | grep -c "ReasoningDelta"

# Sıra numaraları boşluksuz mu
curl -s http://localhost:5081/agentprism/api/runs/$RUN/events | jq '[.[].sequence] | . as $s | ($s|length) == ($s|max)'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Yavaş hedef akışı durdurur | Sözleşme XML dokümanında açıkça yazar; Manuel Case 8 sınırı belgeler; Açık Soru 1 karara bağlanır |
| Hedef `run`'ı düşürür | Yut-ve-devam kuralı `RunEventWriter`'dan birebir devralınır; `RunEventSinkFailureTests` DoD'de |
| Tek yazıcı garantisi bozulur → replay ile canlı akış ayrışır | Sıra numarası yalnız `RunEventWriter` üretir; hedef numaralanmış olay alır |
| Düşünme metni beklenmedik hacim üretir | Varsayılan **kapalı**; hacim ilk açık koşumda ölçülür ve kapanış notuna yazılır |
| Sağlayıcı düşünme metnini saklama kısıtı koyar | Doğrulanmadı — açık soru olarak dokümana yazıldı; varsayılan kapalı olduğu için risk gerçekleşmez |
| `AsyncLocal` hedef içinde kaybolur | XML dokümanı uyarıyı taşır; `RunEventSinkAmbientTests` |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **`ReasoningDelta = 23`, plandaki 22 DEĞİL.** Plan "yirmi iki değer, 0–21"
   diyordu ve bunu ÖLÇÜLDÜ etiketiyle kanıt tablosuna yazmıştı — ama bu ölçüm
   Faz 62'nin `ModelFallbackUsed = 22` eklemesinden ÖNCEKİ bir kod
   okumasındandı. `RunEventType.cs` yeniden okununca 22 zaten doluydu; K-040
   (sıra korunur, yalnız eklenir) gereği yeni değer sona (23) eklendi. K-492.

2. **Doğrulama komutlarındaki `thinker` agent'ı hiç var olmadı.** Plan
   `curl .../agents/thinker/run/stream` örneği veriyordu. Gerçekte
   `samples/AgentPrism.Api/Program.cs` zaten `claude-thinking` adında,
   Anthropic extended thinking açık bir agent taşıyordu (Faz 62'den kalma
   `ProviderSettings[ThinkingBudgetTokensSetting]` örneği). Yeni bir agent
   eklemek yerine bu doğrulama koşumu `claude-thinking`'i kullandı ve
   `samples/AgentPrism.Api/appsettings.json`'a `RunRecording.RecordReasoningDeltas: true`
   eklendi (sample'ın kendi bilinçli demonstrasyon deseni — `EnableKnowledge`,
   `PersistAudio` gibi diğer alanlarla aynı stil). Doğrulama komutu düzeltildi.

3. **`RunEventWriter.CompleteAsync`'in üstündeki `if (IsDisabled) return;`
   koruması kaldırıldı — planda YOKTU, uygulama sırasında bulundu.** Depo
   aynı `run` içinde DAHA ÖNCE bir yazımda başarısız olduysa, bu koruma
   kapanış olayının (RunCompleted/RunFailed) hiç üretilmemesine yol
   açıyordu — sağlıklı bir sink bu yüzden terminal olayı asla görmüyordu.
   "Depo ve sink bağımsız olmalı" ilkesinin (planın kendi "Neden `IRunStore`
   dekorasyonu yeterli değil" tablosu) doğal bir sonucu olarak koruma
   kaldırıldı; yalnız gerçek `_store.CompleteRunAsync` çağrısı `IsDisabled`'a
   bağlı kaldı. Kanıt: `RunEventSinkTests.A_sink_still_receives_every_event_when_the_store_itself_is_disabled`.
   K-493.

4. **Frontend'de `ModelFallbackUsed` (Faz 62) bu fazdan bağımsız, önceden var
   olan bir boşluk olarak bulundu ve aynı anda düzeltildi.** `lib/types.ts`
   `RunEventType` union'ı bu değeri hiç taşımıyordu ve `run-detail.tsx`'teki
   `EVENT_STYLE: Record<RunEventType, ...>` — `ReasoningDelta` eklenince
   TypeScript zaten her üye için bir giriş ZORUNLU kılıyordu — bu eksikliği
   derleme hatasıyla ortaya çıkardı. Kapsam dışı ama bedavaydı (tek satır
   union + tek sözlük girişi); ayrı bir kusur açmak yerine aynı yerde
   düzeltildi.

5. **`docs/KARARLAR.md` ve `docs/hafiza/cekirdek-calistirma.md` bütçe
   aşımına uğradı, arşivlendi.** Bu fazın iki yeni kararı `KARARLAR.md`'yi
   475.000 bayt bütçesinin 1.071 bayt üzerine taşıdı (dosya zaten %99,8
   doluydu). K-391'in tam gerekçesi `arsiv/KARARLAR-GECMISI.md`'ye taşındı,
   satır kısaltılarak korundu. `cekirdek-calistirma.md`'de üç madde aynı
   şekilde `arsiv/HAFIZA-GECMISI.md`'ye taşındı. **Not:** `KARARLAR.md` şimdi
   474.455/475.000 bayt (%0 boş) — bir sonraki faz ilk kararında yeniden
   arşivleme gerekecek.

6. **`concepts/workflows.md` docs-site güncellemesi kasıtlı atlandı.**
   `WorkflowRunner`'a `sinks` parametresi eklendi ama bu, zaten
   `concepts/runs.md`'de genel olarak belgelenen `IRunEventSink` kavramının
   ötesinde workflow'a özgü yeni bir davranış TAŞIMIYOR — agent ve workflow
   çalıştırmaları aynı arayüzü paylaşır. `scripts/dokuman-bakim.py --site-denetle`
   bunu aday olarak işaretledi ama kapı geçti (üç sayfa zaten değişmişti).

## Bu Fazda Verilen Kararlar

- **K-492** — `RunEventType.ReasoningDelta` değeri 23, plandaki 22 değil.
- **K-493** — `IRunEventSink` fan-out'u depo başarısından tam bağımsız;
  `RunEventWriter.CompleteAsync` artık `IsDisabled` iken erken dönmez.

Tam metin: `docs/KARARLAR.md` (K-492, K-493).

## Gerçekleşen Public API

Taslak imzalar `AgentPrism.Abstractions/PublicAPI.Unshipped.txt` ve
`AgentPrism.Core/PublicAPI.Unshipped.txt`'te birebir yansır. Plandan tek fark:
`ReasoningDelta = 23` (bkz. Plandan Sapmalar #1).

```csharp
// AgentPrism.Abstractions
public interface IRunEventSink
{
    ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default);
}

public enum RunEventType
{
    // ... 0-22 unchanged (ModelFallbackUsed = 22 already existed, Faz 62) ...
    ReasoningDelta = 23,
}

// AgentPrism.Core
public sealed class AgentPrismRunRecordingOptions
{
    public bool RecordReasoningDeltas { get; set; } // default false
}

// Genişletilen kurucular (hepsi sondan bir önceki opsiyonel parametre —
// PublicAPI.Shipped.txt boş olduğu için kırıcı değişiklik sayılmadı):
public sealed class RunEventWriter
{
    public RunEventWriter(
        IRunStore store,
        AgentPrismRunRecordingOptions options,
        ILogger logger,
        Guid runId,
        IReadOnlyList<IRunEventSink>? sinks = null);
}

public sealed class RunRecordingAgent : DelegatingAIAgent
{
    public RunRecordingAgent(
        /* ... mevcut 20 parametre değişmedi ..., */
        IReadOnlyList<IRunEventSink>? sinks = null);
}

public sealed class RunRecordingAgentDecorator : IAgentDecorator
{
    public RunRecordingAgentDecorator(
        /* ... mevcut 15 parametre değişmedi ..., */
        IEnumerable<IRunEventSink>? sinks = null);
}
```

`WorkflowRunner` (internal, public API'de görünmez) aynı desenle
`IEnumerable<IRunEventSink>? sinks = null` parametresi aldı.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Runs/
├── IRunEventSink.cs                    (YENİ)
└── RunEventType.cs                     (değişti — ReasoningDelta = 23)

src/AgentPrism.Core/
├── Recording/RunEventWriter.cs         (değişti — sink fan-out, CompleteAsync düzeltmesi)
├── Recording/RunRecordingAgent.cs      (değişti — sinks alanı, TextReasoningContent dalı)
├── Recording/RunRecordingAgentDecorator.cs (değişti — sinks parametresi)
├── AgentPrismOptions.cs                (değişti — RecordReasoningDeltas)
└── AgentPrismServiceCollectionExtensions.cs (değişti — BindRunRecording, sink DI kablolaması)

src/AgentPrism.Workflows/Internal/WorkflowRunner.cs (değişti — sinks parametresi)

src/AgentPrism.UI/frontend/src/
├── lib/types.ts                        (değişti — ReasoningDelta + ModelFallbackUsed union'a eklendi)
├── lib/transcript.ts                   (değişti — case 'ReasoningDelta')
└── screens/run-detail.tsx              (değişti — EVENT_STYLE iki yeni giriş)

samples/AgentPrism.Api/
├── appsettings.json                    (değişti — RunRecording.RecordReasoningDeltas: true)
└── Program.cs                          (dokunulmadı — claude-thinking zaten vardı)

tests/AgentPrism.Core.UnitTests/Recording/
├── RunEventSinkTests.cs                (YENİ — 7 test)
└── ReasoningRecordingTests.cs          (YENİ — 5 test)

tests/AgentPrism.Workflows.UnitTests/
├── WorkflowEventSinkTests.cs           (YENİ — 1 test)
└── Fakes/WorkflowTestHost.cs           (değişti — CreateRunner sinks aşırı yükü)

docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md (değişti — MT-UIRUN-047/048/049)
docs-site/src/content/docs/
├── concepts/runs.md                    (değişti — ReasoningDelta, IRunEventSink bölümü)
├── guides/observability.md             (değişti — RecordReasoningDeltas, sorun giderme satırları)
└── ui.md                               (değişti — düşünme bloğu tek cümle)
```

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (2026-08-19). **🔴 ve 🟡 yok.**
Denetçinin tek gözlemi (`RunEndpoints.EventName()`'in `ReasoningDelta`/
`ModelFallbackUsed` gibi birçok tip için SSE `event:` alanında `"unknown"`a
düşmesi) bu fazdan ÖNCE de aynı davranıştaydı (`ContentMasked`,
`WorkflowStarted` vb. de kapsanmıyor) — frontend `event:` alanını değil
`data:` JSON'undaki `type`'ı okuduğu için davranışsal etkisi yok; bu fazın
kapsamı dışında bırakıldı, aday listesine de yazılmadı (davranış değişikliği
gerektirmiyor).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IRunEventSink` kaydı düz DI: `services.AddSingleton<IRunEventSink, T>()`
  (veya `TryAddEnumerable`) yeterli — özel bir `AddXxx` uzantı metodu yok,
  `IContentGuard`/`IAgentSource` deseniyle aynı.
- Bir sink her zaman `RunEvent`'in KENDİSİNİ alır (daraltılmış bir DTO değil);
  olay zaten sıra numarası ve `TenantId` damgasıyla gelir.
- `RunEventWriter`'ın kurucusuna 5. parametre (`sinks`) eklendi;
  `RunRecordingAgent`/`RunRecordingAgentDecorator`/`WorkflowRunner`'ın
  kurucularına da SONA (mevcut son opsiyonel parametreden sonra) eklendi.

**🚨 Bilinen tuzaklar:**
- Bir kurucuya `IEnumerable<IRunEventSink>?`/`IReadOnlyList<IRunEventSink>?`
  gibi yeni bir opsiyonel parametre eklerken, o kurucuyu POZİSYONEL argümanla
  (özellikle `params` dizisinden önce) çağıran test yardımcıları KIRILABİLİR
  — `WorkflowTestHost.CreateRunner`'da yaşandı, çözüm ikinci bir aşırı yüktü.
  Ayrıntı: `docs/hafiza/workflows.md`.
- `RunEventType`'a yeni bir değer eklemeden önce dosyanın KENDİSİ okunur;
  planın "son değer N" iddiası güvenilmez (K-492).
- Bir `Record<EnumTürü, ...>` (TypeScript) sözlüğü eksik anahtarı derleme
  hatası yapar — yeni bir backend enum üyesi eklerken frontend union'ı VE bu
  tür sözlükleri birlikte güncellenmeli, `docs/hafiza/frontend.md`.

**Yarım kalan/kapsam dışı bırakılan işler:**
- MT-UIRUN-049 (arayüzde düşünme bloğunun görsel doğrulaması) 👤 insan
  gerektirir olarak işaretlendi — paylaşılan Playwright tarayıcı oturumu bu
  oturumda meşguldü.
- `docs/KARARLAR.md` %0 boş — bir sonraki fazın ilk kararı ekleme yapmadan
  önce yeniden arşivleme gerektirebilir.

**Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek; F-115 bu fazla kapandı.
`docs/71-WORKFLOW-KOD-DUGUMU.md` (F-116) zaten planlanmış durumda.
