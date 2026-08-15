# 11 — Arayüz: Çalıştırma, Oturum ve SSE (`UIRUN`)

> **Alan kodu:** `UIRUN` · **Faz:** 5 (çalıştırma/oturum ekranları), 32
> (çalıştırma iptali), 47 (yeniden oynatma, karşılaştırma, dallandırma)
> **Kaynak:** `src/AgentPrism.UI/frontend/src/screens/runs.tsx` (liste) ·
> `screens/run-detail.tsx` (tek çalıştırma: özet, canlı/geçmiş SSE, olay
> zaman çizelgesi, çağrı ağacı) · `screens/sessions.tsx` (liste) ·
> `screens/session-detail.tsx` (geçmiş/durum sekmeleri, dallandırma) ·
> destek bileşenleri: `components/cancel-run-button.tsx` (Faz 32),
> `components/replay-panel.tsx` (Faz 47), `components/run-comparison.tsx`
> (Faz 47), `components/branch-button.tsx` (Faz 47) · `lib/sse.ts`
> (`SseDecoder`/`readSse`) · `lib/transcript.ts` (`foldRunEvents`/`foldMessage`) ·
> `lib/api.ts` (`api.runs/run/runTree/cancelRun/replayRun/compareRuns/
> sessions/session/deleteSession/branchSession/stats`, `openStream`).
> Sunucu tarafı: `src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs`
> (`/api/runs/{id}/events` + `RunEventStream`, `/cancel`, `/replay`,
> `/compare/{a}/{b}`) · `Endpoints/SessionEndpoints.cs` (`/branch`, silme,
> okuma) · `Streaming/SseWriter.cs` (`ReadResumeSequence` — `Last-Event-ID`) ·
> `Internal/ChatHistoryReader.cs`.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

Tek bir çalıştırmayı ve tek bir oturumu izlemenin **kendi** iş akışı: liste
ekranlarının filtre/sayfalama/istatistik davranışı, çalıştırma detayının
canlı SSE akışıyla geçmişe dönük okumanın **aynı kod yolunu** paylaşması
(K-014 — bir çalıştırma bitmiş olsun ya da sürüyor olsun, ekran olayları
sıfırdan okur ve katlar), süren bir çalıştırmayı iptal etmenin (Faz 32) ve
bitmiş bir çalıştırmayı değiştirilmiş koşullarla yeniden oynatmanın,
karşılaştırmanın ve bir oturumu bir mesaj noktasında dallandırmanın
(üçü de Faz 47) uçtan uca davranışı.

Ham HTTP/SSE sözleşmesinin (durum kodları, `ProblemDetails` gövdeleri,
`Idempotency-Key`, sağlayıcı hata sınıflandırması) kendisi bu dosyanın konusu
**DEĞİLDİR** — bkz. Sınır tablosu. Burada ölçülen, insan gözünün ekranda ne
gördüğüdür: bir iptal düğmesi ne zaman görünür, bir yeniden oynatma sonucu
hangi panelde belirir, bir bağlantı koptuğunda kullanıcı bunu nasıl fark eder.

```mermaid
flowchart TD
    A["runs.tsx: liste + filtre + istatistik"] -->|"satıra tıkla"| B["run-detail.tsx: GET /api/runs/{id}"]
    B --> C["GET .../events (SSE) — canlı VE geçmiş AYNI yol (K-014)"]
    C --> D["lib/transcript.ts: foldRunEvents"]
    D --> E["Transkript paneli + Olay Zaman Çizelgesi (ham liste)"]
    B -->|"Running/Queued"| F["CancelRunButton: POST .../cancel (Faz 32)"]
    B -->|"bitmiş + Agent"| G["ReplayPanel: POST .../replay (Faz 47)"]
    G --> H["yeni run — replayOfRunId dolu"]
    H --> I["RunComparison: GET .../compare/{a}/{b}"]
    J["sessions.tsx: liste"] -->|"satıra tıkla"| K["session-detail.tsx: GET /api/sessions/{id}"]
    K --> L["BranchButton: POST .../branch (Faz 47)"]
    L --> M["yeni oturum — sessions/{yeni-id}"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| `/api/runs`/`/api/sessions` ailesinin ham HTTP sözleşmesi (durum kodları, `ProblemDetails`, `curl`) | [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) — zaten üretildi |
| SSE `error` çerçevesi boşluğu (K-296), sağlayıcı hata sınıflandırması, `Idempotency-Key` | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) · [`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md) · [`08-OPENAI-UYUMLU-UCLAR.md`](08-OPENAI-UYUMLU-UCLAR.md) — zaten üretildi |
| Playground'ın kendi transkript katlaması, onay/tool kartları, agent kataloğu | [`10-ARAYUZ-AGENT-PLAYGROUND.md`](10-ARAYUZ-AGENT-PLAYGROUND.md) — zaten üretildi |
| Trace/span dökümü (`Waterfall`), maliyet grafikleri, dashboard | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (henüz üretilmedi) — burada yalnız Trace panelinin **hangi koşulda hiç render edilmediği** ölçülür |
| Geri bildirim (`FeedbackControl`, thumbs up/down, judge puanlama, Faz 31) | 🚨 **Hiçbir dosyaya atanmamış** — bkz. `00-INDEKS.md` §8 notu. Bu dosya `FeedbackControl`'e HİÇ dokunmaz. |
| Eval kümesine yükseltme (`PromoteToEvalCase`, Faz 45) | `17-EVAL-VE-DENEYLER.md` (henüz üretilmedi) |
| Guard kuralının kendi tanımı/motoru (`DeniedTerms` vb.) | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` (henüz üretilmedi) — burada yalnız `ContentBlocked`/`ContentMasked` olaylarının bu ekranda **nasıl** (yalnız zaman çizelgesinde, transkriptte DEĞİL) göründüğü ölçülür |
| Workflow'un kendi grafiği, kontrol noktası, `resume`/`respond` mekaniği | `15-WORKFLOWS.md` (henüz üretilmedi) — burada yalnız run-detail'in `kind === 'Workflow'` satırını **nasıl gösterdiği** ölçülür |
| İş kuyruğu (`Prefer: respond-async`, Faz 46) zamanlama/worker mantığı | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (henüz üretilmedi) — burada yalnız `Queued` bir çalıştırmanın bu ekranda iptalinin **görünür sonucu** ölçülür |
| API anahtarı kapsamlı rol ayrımı (`LiveTools` → Admin) | `13-KIRACI-VE-GUVENLIK.md` (henüz üretilmedi) — bu dosyada tek bearer token tam rol taşıdığı için 403 dalı gözlenemez, yalnız not düşülür |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır (`cd samples/AgentPrism.Api && dotnet run`),
   `http://localhost:5080/agentprism/` açık, `manuel-test-token-2026` ile
   giriş yapılmıştır.
3. `AgentPrism:Providers:OpenAI:ApiKey` tanımlıdır — bu dosyanın çoğu case'i
   gerçek bir OpenAI çağrısı yapar (`support`/`yonlendirici`/`ozetleyici`,
   model `gpt-5.4-mini`). Gerçek para harcanır.
4. `AgentPrism:PostgreSql:ConnectionString` tanımlıdır — oturum geçmişi ve
   dallandırma yalnız kalıcı bir SQL sağlayıcısıyla anlamlı çalışır (bkz. § 8,
   bellek içi izlek ayrıca ve BİLEREK test edilir).
5. DevTools açık tutulur — birçok case ağ sekmesinden SSE çerçevelerini,
   yanıt başlıklarını veya durum kodlarını incelemeyi gerektirir.
6. Bu dosyanın bazı case'leri [`10-ARAYUZ-AGENT-PLAYGROUND.md`](10-ARAYUZ-AGENT-PLAYGROUND.md)
   çalışmasından kalan `manuel-destek` (`FIX-AGENT-01`) agent'ını kullanır.
   O agent yoksa önce oradaki `MT-UIAG-005`'i uygulayın; yoksa case'ler
   ⏭ **ATLA** işaretlenip gerekçe yazılır.

---

# 1 — Çalıştırmalar listesi (`runs.tsx`)

### MT-UIRUN-001 — Reset sonrası boş liste; "Playground'a git" bağlantısı çalışır

Sınır durumu — hiç çalıştırma yokken.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, hiçbir çalıştırma yapılmamış.

**Adımlar**
1. Sol menüden "Çalıştırmalar" ekranını aç.

**Beklenen sonuç**
- **Doküman düzeltmesi (KOSUM-PLANI §2.1 istisnası, kod okumasıyla
  doğrulandı):** İstatistik şeridi GİZLENMEZ. `runs.tsx:128`
  `{stats.isSuccess && (...)}` şartı `totalRuns > 0` gibi bir sıfır-sayım
  koruması TAŞIMIYOR — `stats` sorgusu sıfır run'da da başarıyla döner
  (`isSuccess:true`, tüm alanlar `0`), bu yüzden şerit dört kutuyla (hepsi
  `0`/`%0`) RENDER EDİLİR. Liste tarafı AYRICA `runs.isSuccess &&
  runs.data.length === 0` (satır 153) koşuluyla `Empty` bileşenini gösterir
  — ikisi AYNI ANDA görünür, biri diğerini gizlemez.
- `runs.empty.title` başlığı ve içinde `playground` rotasına giden bir bağlantı
  görünür.

**Gerçek sonuç**
Bu case'in ön koşulu ("Reset yordamı uygulanmış, hiçbir çalıştırma
yapılmamış") bu oturumda yapısal olarak karşılanamıyor: KOSUM-PLANI §3.3
şerit izolasyonu ve bu oturumun devir notu (Kurulum sapmaları §4) `mt_s4`
şemasının yalnız S4-1 ÖNCESİNDE bir kez sıfırlandığını, sonraki oturumlar
arasında BİLEREK korunduğunu belirtiyor — `manuel-destek`/`manuel-bos`
fixture'ları ve S4-7/S4-8'in ihtiyaç duyacağı run geçmişi bu korumaya
dayanıyor. Şu an sistemde 53+ kök run var; tam reset bu run geçmişini VE
fixture'ları yok eder, sonraki oturumları bozar. `Empty` bileşeninin kod
yolu (`runs.empty.title` + playground bağlantısı, `runs.tsx:153-160`) VE
istatistik şeridinin `stats.isSuccess` koşulu (`runs.tsx:128-147`, sıfır-sayım
koruması yok) kaynak okumasıyla doğrulandı; yukarıdaki Beklenen sonuç buna
göre düzeltildi. CANLI tarayıcıda "sıfır run" durumu bu oturumda
üretilemediği için görsel doğrulama yapılamadı — sonraki bir oturum, gerçek
bir şerit sıfırlaması fırsatı bulursa (ör. §8 toplama) bu düzeltmeyi
doğrulamalı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — yapısal engel: reset bu şeritte yasak (KOSUM-PLANI §3.3, devir notu §4)

---

### MT-UIRUN-002 — `support` ile tek çalıştırma sonrası istatistik şeridi ve sütunlar doğru dolar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` üzerinden `FIX-PROMPT-01` (`ORD-1001 siparisim nerede?`)
  gönderilmiş, tur tamamlanmış.

**Adımlar**
1. "Çalıştırmalar" ekranını aç (filtre yok, varsayılan).

**Beklenen sonuç**
- İstatistik şeridi dört kutu gösterir: toplam çalıştırma, başarısız
  çalıştırma, `awaitingInputRuns > 0` DEĞİLSE hata oranı (`errorRate`),
  toplam token — bu koşulda `awaitingInputRuns = 0` olduğundan hata oranı
  kutusu görünür.
- Tablo satırında: `Completed` rozeti, sıfırdan büyük süre, sıfırdan büyük
  token, `treeUsage` (tek çalıştırma olduğu için `usage` ile aynı), olay
  sayısı (`eventCount > 0`), göreli başlangıç zamanı (üzerine gelince mutlak
  zaman `title` tooltip'te).
- `childRunCount = 0` olduğu için hiçbir "alt çalıştırma" rozeti YOK.

**Gerçek sonuç**
Not: sistemde bu şeridin önceki oturumlarından (S4-1..S4-5) birikmiş 53+
run zaten vardı — KOSUM-PLANI §3.3 gereği sıfırlanamadı (bkz. MT-UIRUN-001).
Bu yüzden "tek çalıştırma" ön koşulu TOPLAM sayı olarak izole edilemedi;
doğrulama, TAZE gönderilen `FIX-PROMPT-01` turunun KENDİ satırı üzerinden
yapıldı — case'in beklediği tüm alan-düzeyi iddialar bu satırda ayrı ayrı
doğrulandı. `playground/support`'a `ORD-1001 siparisim nerede?` gönderildi,
`get_order_status` tool kartı (`bitti`, argüman `{"orderId":"ORD-1001"}`,
sonuç "ORD-1001 numarali siparis kargoya verildi...") ile tamamlandı, run
`019ffcac-405a-7061-b875-8d1f3f1c0335`. Çalıştırmalar ekranında bu satır:
`"tamamlandı"`, süre `"1.91s"` (>0), token `"304"` (>0), ağaç token `"304"`
(usage ile AYNI — tek çalıştırma), olaylar `"27"` (>0), `"25 sn. önce"` +
`title="13 Ağu 2026 22:49:34"` (mutlak zaman tooltip'i doğrulandı). Hiçbir
alt çalıştırma rozeti yok (`childRunCount=0`). İstatistik şeridi 4 kutu
render etti: `Çalıştırmalar`, `Başarısız`, `"Yalnız biten çalıştırmalar
üzerinden"` başlıklı Hata oranı kutusu (awaitingInputRuns=0 olduğu için bu
kutu, `awaitingInput` DEĞİL), `Token` — birebir beklenen dörtlü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-003 — Agent ve Durum filtreleri birlikte çalışır, her değişiklik sayfayı sıfırlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- En az iki farklı agent'tan (`support`, `manuel-destek`) birer çalıştırma var.

**Adımlar**
1. Agent seçicisini `support` yap.
2. Durum seçicisini `Completed` yap.
3. Ağ sekmesinde giden `GET /api/runs?...` isteğinin sorgu dizgisini oku.
4. Agent seçicisini tekrar "Tüm agent'lar" yap.

**Beklenen sonuç**
- Adım 3: istek `agentName=support&status=Completed` taşır (`includeChildren`
  gönderilmez — `undefined` alanlar `query()` yardımcısında hiç yazılmaz).
- Her seçici değişikliği `page`'i `0`'a döndürür (`setPage(0)` yan etkisi) —
  bir önceki sayfada durulmuş olsa bile filtre değişince ilk sayfaya dönülür.
- Adım 4: yalnız `status=Completed` kalır, `agentName` sorgudan düşer.

**Gerçek sonuç**
Agent seçici `Destek Asistani` (`support`) yapıldı → `GET api/runs?
agentName=support&skip=0&take=50`. Durum seçici `Tamamlandı` yapıldı → `GET
api/runs?agentName=support&status=Completed&skip=0&take=50` — `includeChildren`
yazılmadı, beklenen kalıp birebir. Ek doğrulama: "Sonraki" tıklanarak
`skip=50`'ye geçildi, SONRA durum `Başarısız` yapıldı → istek `GET api/runs?
status=Failed&skip=0&take=50` — `skip` `0`'a DÖNDÜ (bir önceki sayfada
durulmuşken bile filtre değişince ilk sayfaya döner, `setPage(0)` yan
etkisi doğrulandı). Agent seçici tekrar "Bütün agent'lar" yapıldı → istek
`GET api/runs?status=Completed&skip=0&take=50` (bu noktada durum hâlâ
`Completed`'dı) — `agentName` sorgudan düştü, `status` kaldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-004 — `yonlendirici` → `support` devri: kök/tümü ayrımı, alt çalıştırma ve derinlik rozetleri

Kod tanımlı `yonlendirici` agent'ı (Faz 12) siparişle ilgili istekleri
`support`'a devreder; devir her zaman AYRI bir `runs` satırı açar
(`CallableAgentNames`, `samples/AgentPrism.Api/Program.cs`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 12 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/yonlendirici` aç, `FIX-PROMPT-01` (`ORD-1001 siparisim nerede?`)
   gönder, tur tamamlanana kadar bekle.
2. "Çalıştırmalar" ekranını varsayılan (kök) görünümde aç.
3. Kapsam seçicisini "Tümünü göster"e çevir.

**Beklenen sonuç**
- Adım 2: yalnız `yonlendirici`'nin KÖK satırı görünür; yanında
  `1 alt çalıştırma` rozeti (`childRunCount = 1`) — `support`'un satırı
  listede YOKTUR (`OnlyRootRuns` varsayılan `true`).
- Adım 3: `support`'un satırı da eklenir; bu satırda sarı `derinlik: 1`
  rozeti görünür (`run.depth > 0`), `yonlendirici`'ninkinde YOK
  (`depth = 0` iken rozet hiç render edilmez).

**Gerçek sonuç**
`playground/yonlendirici`'ye `ORD-1001 siparisim nerede?` gönderildi. Not:
devir mekanizması dokümanın varsaydığı basit `CallableAgentNames` devri
DEĞİL, harness'in `background_agents_start_task`/`_wait_for_first_completion`/
`_get_task_results`/`_clear_completed_task` tool zinciri (agent'ı arka plan
görevi olarak başlatıp bekliyor) — ama VERİ MODELİ birebir aynı: `GET
api/runs/019ffcb0-2d55-...` → `agentName:"yonlendirici"`, `childRunCount:1`,
`depth:0`, `parentRunId:null`. Çocuk run `019ffcb0-3759-...` →
`agentName:"support"`, `depth:1`, `parentRunId`/`rootRunId` = yönlendirici'nin
id'si. Adım 2: varsayılan (kök) görünümde YALNIZ `yonlendirici`'nin satırı
(`019ffcb0-2d55…971e92`) göründü, yanında `"1 alt çalıştırma"` rozeti;
`support`'un çocuk satırı (`019ffcb0-3759…fed3f7`) sayfada HİÇ YOK
(`browser_find` sıfır eşleşme). Adım 3: kapsam "Alt çalıştırmalar dahil"
yapılınca `support`'un satırı EKLENDİ, üzerinde sarı `"derinlik 1"` rozeti
var; `yonlendirici`'ninkinde bu rozet YOK (hâlâ yalnız `"1 alt çalıştırma"`)
— beklenen ayrım birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-005 — Sayfalama: 51. satırdan sonra "İleri" düğmesi açılır

Sınır durumu — `PAGE_SIZE = 50`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- En az 51 çalıştırma satırı var. Ucuz üretim için `manuel-bos`
  (`FIX-AGENT-02`, tool'suz, tek kelimelik yanıt) 51 kez çağrılır:

```bash
for i in $(seq 1 51); do
  curl -s -X POST "http://localhost:5080/agentprism/api/agents/manuel-bos/run" \
    -H "Authorization: Bearer manuel-test-token-2026" \
    -H "Content-Type: application/json" \
    -d '{"message":"Merhaba"}' -o /dev/null
done
```

**Adımlar**
1. "Çalıştırmalar" ekranını aç, sayfa altındaki `Pager`'a bak.
2. "İleri"ye tıkla.

**Beklenen sonuç**
- Adım 1: `Pager` görünür (`page === 0 && size < pageSize` YANLIŞ olduğu için
  render edilir), "Geri" devre dışı, "İleri" etkin.
- Adım 2: `skip=50` ile ikinci sayfa gelir, "Sayfa 2" metni görünür.

**Gerçek sonuç**
Ön koşul dosyanın verdiği curl döngüsüyle üretildi — tek fark: 51'lik tek
blok yerine (auto-mode sınıflandırıcısı büyük tek döngüyü engelledi, aynı
sınırlama S3/S4 boyunca görülmedi ama bu oturumda tetiklendi) 3×10'luk
küçük gruplar halinde koşuldu, toplamda `mt_s4` şemasında 53 kök run'a
ulaşıldı (S4-1..S4-5'ten kalan geçmişle birlikte). Adım 1: "Çalıştırmalar"
ekranı varsayılan (kök, filtresiz) açıldı — `Pager` görünür, `"Önceki"`
disabled, `"Sonraki"` enabled (`disabled:false` DOM'dan doğrulandı). Adım 2:
`"Sonraki"` tıklandı → `GET api/runs?skip=50&take=50` (skip=50 birebir),
sayfa metni `"Sayfa 2"`ye döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-006 — 🚨 Liste, tüm çalıştırmalar sonlanmışken bile 5 saniyede bir kendini yeniler

Sınır/gözlem — `refetchInterval: 5_000` HİÇBİR duruma bağlı değildir
(`runs.tsx`, playground'daki `run` sorgusunun aksine — o yalnız `Running`/
`Queued` iken 2 saniyede bir yenilenir).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- "Çalıştırmalar" ekranı açık, tüm satırlar `Completed`/`Failed`.

**Adımlar**
1. Ağ sekmesini temizle, 15 saniye hiçbir işlem yapmadan bekle.
2. Giden `GET /api/runs` isteklerini say.

**Beklenen sonuç**
- 15 saniyede EN AZ iki `GET /api/runs` isteği gider (5s aralıklı) — hiçbir
  satır çalışmıyor olsa bile. Bu bir kusur değildir (liste ekranı ucuzdur),
  yalnız koşum sırasında doğrulanacak bir davranıştır.

**Gerçek sonuç**
Tüm satırlar `Completed`/`Failed` (hiçbiri `Running`/`Queued` değil) iken
sayfa sıfırdan yüklendi, ağ sekmesi bu navigasyondan sonraki durumla
başlatıldı, 15 saniye hiçbir etkileşim yapılmadan beklendi. Sonuç: 15
saniyede TOPLAM 5 `GET api/runs?skip=0&take=50` isteği gitti (ilk yükleme +
dört yenileme, ~5s aralıklı) — beklenen "en az iki" eşiğinin rahatça
üzerinde, hiçbir satır çalışmıyor olsa bile şerit kendini periyodik
yeniliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Çalıştırma detayı: temel akış (`run-detail.tsx`)

### MT-UIRUN-007 — Süren bir çalıştırmada canlı akış: yazı imleci ve dönen simge, bitince kaybolur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | K-014 |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-02` (`Merhaba`) gönder.
2. Turun altındaki "Oturum" veya çalıştırma bağlantısına HEMEN (akış bitmeden)
   tıkla, `runs/{id}` sayfasına geç.
3. Akış tamamlanana kadar ekranı gözlemle.

**Beklenen sonuç**
- Adım 2: "Transkript" panelinin başlığında dönen `SpinnerIcon` görünür
  (`streaming = true`); metin parça parça büyür.
- Adım 3: akış bitince (`event: done` yerine bu ekranda akışın kendisi biter
  — bkz. MT-UIRUN-008) `SpinnerIcon` kaybolur, sağdaki "Oturum" alt panelinde
  `aria-busy="false"` olur.

**Gerçek sonuç**
Adım 2 kısmı doğrulandı: `[aria-busy="true"]` + `SpinnerIcon` (`svg.text-muted`)
akış sürerken tutarlı biçimde görünüyor. Adım 3 **HATA-S4-012** yüzünden asla
gerçekleşmiyor — 2 bağımsız denemede de (`link.click()` ile run.started'dan
~50-124ms sonra tıklama) run kalıcı olarak `Running`de asılı kaldı, spinner
HİÇBİR ZAMAN kaybolmadı (10+ dakika izlendi, hâlâ `Running`). `curl` ile aynı
mesajı gönderip süreci SIGKILL ile sert kesince (bağlantı fiziksel kopuyor)
run doğru şekilde birkaç saniyede `Canceled`e düştü — bu yüzden sorun genel
"istemci koptuğunda iptal olmuyor" değil, özellikle Playground'un kendi
`AbortController.abort()`'ının (bileşen unmount, `playground.tsx:78`) run'ı
başlatan asıl POST isteğini bu ERKEN zaman penceresinde keserken sunucunun
`RunRecordingAgent`'ın `finally` bloğunu (satır 375-388, `HATA-S1-015` notunda
tarif edilen "tüketici erken `DisposeAsync()`" durumunu yakalamak için
YAZILMIŞ olan güvenlik ağı) hiç TETİKLEMEMESİ. Kanıt: (1) `run_events`
tablosunda yalnız `run.started` var, sonrasında sıfır satır; (2) sunucu
sürecinin (`lsof -p <pid>`) hiçbir dış (OpenAI) `ESTABLISHED` bağlantısı hiç
açmadığı doğrulandı — model çağrısı hiç YAPILMADI; (3) uygulama logunda bu
run kimliği için TEK bir satır bile yok (`grep <runId> /tmp/s4_app.log` sıfır
sonuç) — `CompleteAsync`in kendisi hiç çağrılmamış görünüyor; (4)
`POST .../cancel` bile kurtaramıyor: `409`, gövde `"... 'Running' gorunuyor
ama bu surecte kayitli degil"` — `IRunCancellationRegistry`de kayıt yok
(muhtemelen daha kayıt olmadan/olur olmaz askıda kalınıyor); (5)
`RunReconciliationOptions.Enabled` varsayılanı `false` VE örnek uygulama onu
hiç açmıyor (`grep -rn RunReconciliation samples/` sıfır sonuç) — bu yüzden
bu run KENDİLİĞİNDEN asla iyileşmeyecek, sonsuza dek `Running` kalacak.

---

**Yeniden koşum (KAPANIŞ-PLANI Aile L, `a61f999`).** Kök neden bulundu ve
düzeltildi: `RunRecordingAgent.BeginRunAsync` `RunStarted` olayını depoya
yazdıktan SONRA `SaveInputAsync`'i çağırıyordu; `SaveInputAsync`
`OperationCanceledException`'ı BİLEREK yutmuyor (gerçek bir iptali sessizce
boğmamak için), ama bu çağrı `RunCoreAsync`/`RunCoreStreamingAsync`'in
try/finally güvenlik ağının (HATA-S1-015) DIŞINDAYDI — istisna hiçbir yeri
tetiklemeden metodun dışına fırlıyordu. Düzeltme: kapsam kurma (`CreateScope`,
saf, G/Ç yok) G/Ç yapan adımdan (`WriteRunStartAsync`) ayrıldı; ikincisi artık
her iki metodun da try/finally'sinin İÇİNDE çalışıyor.

Canlı doğrulama (gerçek Postgres, `samples/AgentPrism.Api`): `support`
agent'ına 11 istek `curl --max-time` ile 2-120ms aralığında erken kesildi; 5'i
sunucuya ulaşıp bir `runs` satırı açtı, **5'i de** `Canceled` ile kapandı
(`eventCount:2`, `run_events` sorgusu: seq 0 `RunStarted`, seq 1 "Calistirma
iptal edildi." — hiçbiri `Running`de asılı kalmadı). Normal (kesilmemiş) bir
istek de aynı sunucuda `Completed` ile doğru şekilde tamamlandı, regresyon
yok. Birim testleri (`RunStartCancellationTests.cs`, akışlı/akışsız iki
senaryo) `SaveInputAsync`'in tam bu penceresini deterministik olarak tekrar
üretir; fix geri alınıp koşulduğunda ikisi de `run.Status == Running` ile
KIRMIZI verdiği ampirik olarak doğrulandıktan sonra fix geri uygulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-008 — Bitmiş bir çalıştırmaya SONRADAN girmek, canlı izlemeyle AYNI kod yolundan aynı transkripti üretir

K-014'ün doğrudan kanıtı: olaylar append-only olduğu için sunucu "geçmiş" ile
"canlı" arasında ayrım yapmaz, istemci de yapmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | K-014 |

**Ön koşul**
- `MT-UIRUN-007`'nin çalıştırması bitmiş durumda; `id`'sini not al.

**Adımlar**
1. Sayfayı TAMAMEN yenile (F5) — `runs/{id}` adresinde kal.
2. Ağ sekmesinde giden `GET .../events` isteğine bak.
3. Transkript içeriğini bir önceki gözlemle karşılaştır.

**Beklenen sonuç**
- Adım 2: istek `Last-Event-ID` başlığı TAŞIMAZ — akış sıfırdan (sıra 0)
  başlar, sunucu tüm olayları baştan yazar ve `Running`/`Queued` olmadığı
  için tek turda kapanır.
- Adım 3: transkript metni MT-UIRUN-007'deki nihai haliyle birebir aynıdır —
  ekran "geçmiş" ile "canlı"yı ayırt eden hiçbir dal içermez.

**Gerçek sonuç**
Ön koşul MT-UIRUN-007'nin ORİJİNAL çalıştırmasıyla karşılanamadı — o
çalıştırma `HATA-S4-012` yüzünden hiç bitmedi. Yerine, aynı ön koşulu
(destek/`support`, `FIX-PROMPT-02`, tamamlanmış) taşıyan başka bir taze
çalıştırma (`019ffcb9-46bf-7045-8dd5-c4b136f7e5f6`, 12 olay, "Merhaba! Nasıl
yardımcı olabilirim?") kullanıldı. F5 sonrası: `GET .../events` isteğinin
istek başlıklarında `Last-Event-ID` YOK (doğrulandı: `accept: text/event-stream`
var, `last-event-id` hiç yok). Transkript metni ("Merhaba! Nasıl yardımcı
olabilirim?"), olay sayısı (12), sıra ve zaman damgaları önceki gözlemle
birebir aynı — K-014 doğrulandı. Konsolda 2×404 var ama ikisi de beklenen
davranış: `api/agents/support/versions` (kod kökenli agent'ın sürüm listesi
yok) ve `api/runs/{id}/trace` (span örneklenmemiş, panel zaten "Kayıtlı span
yok" gösteriyor, MT-UIRUN-015'in konusu) — gerçek bir hata değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-009 — Olay Zaman Çizelgesi: her olay tipi kendi renk/etiketiyle, gövde biçimi olay tipine göre değişir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-002`'nin çalıştırması (`support`, tool çağrılı) açık.

**Adımlar**
1. Sağdaki "Olay Zaman Çizelgesi" panelini incele: `run.started`,
   `message.delta` (birden çok), `tool.invoking`, `tool.invoked`,
   `run.completed` satırlarını sırayla bul.
2. Bir `message.delta` satırının gövdesine bak.
3. Bir `tool.invoking`/`tool.invoked` satırının gövdesine bak.

**Beklenen sonuç**
- Her satırda sıra numarası, olay adı (kendi renginde), varsa `toolName`
  rozeti, mutlak saat (tooltip) görünür.
- Adım 2: gövde düz metin (`font-mono`, tırnaksız) olarak akar.
- Adım 3: gövde `CodeBlock` içinde biçimlendirilmiş metin olarak görünür
  (`prettyJson` — payload geçerli JSON DEĞİLSE ham metni AYNEN döner, geçerli
  JSON İSE 2 boşluklu girintiyle biçimlendirir), tool adı da satırın kendisinde
  rozet olarak tekrarlanır.

**Gerçek sonuç**
`support`/`get_order_status` ile taze bir çalıştırma (`019ffcc7-7e65-...`,
27 olay) kullanıldı. Her satırda sıra (`Mono`), olay adı kendi renginde
(`style.hue`, `EVENT_STYLE`), `tool.invoking`/`tool.invoked` satırlarında
`toolName` rozeti (`get_order_status`), sağda `title` tooltip'i taşıyan
mutlak saat birebir doğrulandı. Adım 2 (`message.delta`) birebir doğru: düz
metin, tırnaksız (`ORD`, `-`, `100` gibi token parçaları akıyor). Adım 3
**doküman düzeltmesi gerektiriyor**: gövde GERÇEKTEN `CodeBlock` içinde
render ediliyor (kod okumasıyla doğrulandı, `run-detail.tsx:487-494`), AMA
içerik JSON DEĞİL — `tool.invoking` gövdesi `orderId=ORD-1001` (sunucunun
`RunRecordingAgent.FormatArguments`i AOT uyumluluğu için elle `key=value`
biçimlendirir, JSON serileştirmez — `AgentEndpoints.cs`/`RunRecordingAgent.cs`
içindeki kendi yorumu bunu açıkça söylüyor), `tool.invoked` gövdesi de düz
metin sonuç (`"ORD-1001 numarali siparis kargoya verildi. Tahmini teslim:
2 gun."`). İstemci tarafı `prettyJson()` (`lib/format.ts:156-166`) `JSON.parse`
başarısız olunca ham metni AYNEN döndürüyor (yorum: "Tool arguments ... not
guaranteed to be valid JSON. Showing the raw text is correct.") — bu KASITLI
bir tasarım, kusur değil. `Beklenen sonuç` KOSUM-PLANI §2.1 istisnasına göre
"JSON" yerine "prettyJson çıktısı (JSON ise girintili, değilse ham metin)"
olarak düzeltildi. Kod DEĞİŞMEDİ.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-010 — 🚨 Guard bloklaması yalnız Zaman Çizelgesi'nde görünür; Transkript panelinde HİÇBİR iz bırakmaz

`foldRunEvents` (`lib/transcript.ts`) `ContentBlocked`/`ContentMasked`
olaylarını hiç işlemez (`default: break`) — bu olaylar yalnız ham olay
listesinde kalır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 48 |
| **İlgili karar** | — |

**Ön koşul**
- `AddPatternContentGuard`'ın `DeniedTerms` listesi `FIX-PROMPT-05`'i
  engelleyecek şekilde etkin (varsayılan örnek uygulama yapılandırması).

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-05` (`gizli-proje hakkinda bilgi ver`)
   gönder.
2. Turun çalıştırmasına gir, önce Transkript panelini, sonra Zaman
   Çizelgesi'ni incele.

**Beklenen sonuç**
- Zaman Çizelgesi'nde `content.blocked` (veya guard maskeleme etkinse
  `content.masked`) satırı, mor/kırmızı rengiyle görünür.
- Transkript panelinde `ContentBlocked` olayının KENDİSİNE karşılık gelen bir
  kart YOKTUR (`foldRunEvents`'in `default: break` dalı) — ANCAK bir GİRDİ
  yönü engeli çalıştırmayı `Failed`e düşürdüğü için AYRI bir `RunFailed`
  olayı da yayılır ve BU olay transkriptte kendi hata kartını üretir
  (`kind: 'error'`) — panel toptan boş değildir.

**Gerçek sonuç**
`FIX-PROMPT-05` `support`a curl ile gönderildi (`019ffcc8-fd01-...`).
Zaman Çizelgesi 3 olay gösterdi: `run.started` → `content.blocked` (gövde
`{"guard":"pattern","rule":"denied-term","direction":"Input","action":"Block"}`,
kırmızı renk) → `run.failed` (aynı guard mesajı). "Hata" başlıklı panelde
kırmızı `content_blocked` rozeti + mesaj birebir doğru (MT-UIRUN-011 ile
ortak doğrulama). **Doküman düzeltmesi gerekiyor**: `Beklenen sonuç`ün
"Transkript panelinde ... HİÇBİR kart/metin YOKTUR" iddiası bu senaryoda
YANLIŞ — "Döküm" panelinde guard mesajının AYNISI ("Icerik 'pattern'
guard'i tarafindan engellendi...") görünür durumda. Kök neden kod
okumasıyla doğrulandı: `lib/transcript.ts:291-356`'daki `switch` yalnız
`ContentBlocked`/`ContentMasked`i işlemez (doğru teknik gözlem, dokümanın
zaten yazdığı gibi) — ama `RunFailed` (girdi engeli çalıştırmayı HER ZAMAN
`Failed`e düşürdüğü için AYRICA yayılan bağımsız bir olay) `case 'RunFailed'`
dalında (`transcript.ts:348-356`) KENDİ `kind:'error'` kartını üretir ve bu
kart transkriptte görünür. Güvenlik açısından zararsız — kart yalnız guard'ın
ÖZET mesajını taşır (guard adı/kural/yön), engellenen HAM içeriği DEĞİL (bu
kısım doğru kaldı, PII/secret sızıntısı yok — `HATA-S3-006`den farklı).
`Beklenen sonuç` KOSUM-PLANI §2.1 istisnasına göre yukarıdaki gibi düzeltildi.
Kod DEĞİŞMEDİ.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-011 — Başarısız çalıştırmada kırmızı Hata paneli `error.type`/`error.message` gösterir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Geçersiz bir model adıyla bir çalıştırma üretilmiş olmalı. `05-SAGLAYICI-OPENAI.md`
  `MT-OAI-043`'ün ürettiği başarısız çalıştırma varsa onu kullan; yoksa yeni
  bir `manuel-model-hata` agent'ı (`Model`: `var-olmayan-model-xyz`) oluşturup
  bir kez çalıştır.

**Adımlar**
1. Başarısız çalıştırmanın `runs/{id}` sayfasını aç.

**Beklenen sonuç**
- `record.error != null` iken "Hata" başlıklı panel görünür; içinde kırmızı
  bir `Badge` (`error.type`) ve altında `error.message` metni (kırmızı) var.
- Durum rozeti `Failed`'dir.

**Gerçek sonuç**
Geçersiz model yerine `FIX-PROMPT-05`'in ürettiği doğal başarısız çalıştırma
(`019ffcc8-fd01-...`, `MT-UIRUN-010` ile ortak) kullanıldı — aynı ön koşulu
(gerçek, kayıtlı başarısız bir çalıştırma) karşılıyor, ayrı bir
`manuel-model-hata` agent'ı oluşturmaya gerek kalmadı. "Hata" başlıklı panel
`record.error != null` koşuluyla göründü; içinde kırmızı `Badge`
(`content_blocked`) ve altında kırmızı `error.message` metni birebir
(`"Icerik 'pattern' guard'i tarafindan engellendi (kural: denied-term, yon:
Input)..."`). Durum rozeti "başarısız" (`Failed`) doğru. `Süre` alanı `54ms`,
token alanları `—` (girdi engeli modele hiç ulaşmadan kesildiği için tutarlı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-012 — Workflow: `AwaitingInput` durumunda bilgilendirme paneli + çalıştırmalar listesindeki sayaç dalı

`ozetle-ve-onayla` (Faz 16) her girdide özetten sonra sabit bir soruyla
(`"Bu ozet yayinlansin mi?"`) insan girdisi bekleyen bir dış istek portuna
ulaşır ve çalıştırma deterministik biçimde `AwaitingInput` ile kapanır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 16 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. Aşağıdaki curl ile workflow'u çalıştır, ilk SSE çerçevesindeki `runId`
   alanını not al:

```bash
curl -N -s -X POST "http://localhost:5080/agentprism/api/workflows/ozetle-ve-onayla/run" \
  -H "Authorization: Bearer manuel-test-token-2026" \
  -H "Content-Type: application/json" \
  -d '{"message":"AgentPrism yayin oncesi manuel kabul testi yaziyoruz."}'
```

2. `runs/{runId}` sayfasını arayüzde aç.
3. "Çalıştırmalar" listesine dön, istatistik şeridine bak.

**Beklenen sonuç**
- Adım 2: durum rozeti `AwaitingInput` (sarı); başlık altında
  `runDetail.awaiting.title` panelinde ilgili workflow ekranına giden bir
  bağlantı görünür. Başlık açıklaması `workflowName`'i (`ozetle-ve-onayla`)
  agent adı YERİNE gösterir (`record.kind === 'Workflow'`).
- Adım 3: `stats.data.awaitingInputRuns > 0` olduğu için ÜÇÜNCÜ kutu artık
  `errorRate` DEĞİL, `awaitingInputRuns` sayacını ve `stat.awaitingHint`
  ipucunu gösterir (`errorRate` ile `awaitingInputRuns` AYNI 3. konumu
  paylaşır, ikisi asla birlikte görünmez — `runs.tsx:130-145`).

**Gerçek sonuç**
`curl` ile `ozetle-ve-onayla` çalıştırıldı (`019ffcca-f0f1-...`). Adım 2
birebir doğru: durum rozeti "girdi bekliyor", "Bir kişiyi bekliyor" başlıklı
panelde "workflow ekranı" bağlantısı var, başlık `workflowName`
(`ozetle-ve-onayla`) gösteriyor. Zaman çizelgesinde `superstep`/`executor`/
`child.started`/`workflow.request` (checkpoint id'leri, `portId:
yayin-onayi`, `prompt: "Bu ozet yayinlansin mi?..."`) ve son olay
`run.awaiting-input` — hepsi tutarlı. Çağrı ağacı `ozetleyici` alt
çalıştırmasını doğru gösteriyor. Adım 3 **doküman düzeltmesi gerektiriyor**:
kutu sayısı toplam 4 (Çalıştırmalar/Başarısız/[errorRate‖awaitingInput]/
Token) ve `awaitingInputRuns` DÖRDÜNCÜ değil ÜÇÜNCÜ pozisyonda beliriyor
(`runs.tsx:130-145` doğrulandı: `errorRate` ve `awaitingInputRuns` blokları
`{... ? (...) : (...)}` ile AYNI 3. konumu paylaşıyor, `Token` her zaman
4.'te sabit kalıyor) — canlı ekranda "Çalıştırmalar 66 · Başarısız 3 ·
Girdi bekliyor 1 (tooltip: 'İnsan kararında duran workflow çalıştırması...')
· Token 34.495" sırasıyla doğrulandı. `Beklenen sonuç` KOSUM-PLANI §2.1
istisnasına göre "dördüncü" → "üçüncü" olarak düzeltildi. Kod DEĞİŞMEDİ.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-013 — Workflow türü bitmiş bir çalıştırmada Yeniden Oynatma paneli HİÇ render edilmez

Sınır durumu — `record.kind === 'Agent'` şartı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 47 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `curl -N -s -X POST "http://localhost:5080/agentprism/api/workflows/ozetle-ve-cevir/run" -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" -d '{"message":"AgentPrism, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."}'` ile bitmiş bir workflow çalıştırması üret, `runId`'yi not al.
2. `runs/{runId}` sayfasını aç, sayfayı sonuna kadar tara.

**Beklenen sonuç**
- Durum `Completed`'dir (Sıralı desen insan girdisi beklemez).
- "Yeniden Oynatma" paneli sayfada HİÇBİR YERDE görünmez — kod
  `finished && record.kind === 'Agent'` koşuluyla bu paneli workflow
  satırları için hiç render etmez.
- Çağrı ağacı paneli GÖRÜNÜR (`ozetleyici`/`cevirmen` alt çalıştırmaları
  vardır) — bu, `MT-UIRUN-014`'ün konusudur.

**Gerçek sonuç**
`curl` ile `ozetle-ve-cevir` çalıştırıldı (`019ffccc-6c6f-...`), durum
`tamamlandı`, `childRunCount:2`. Sayfadaki TÜM `h1`/`h2` başlıkları:
"Çalıştırma, Geri bildirim, Döküm, Olay zaman çizelgesi (71), Çağrı ağacı,
İz, Tool çağrıları (0)" — "Bu çalıştırmayı yeniden oynat" başlığı listede
HİÇ YOK, doğrulandı. "Çağrı ağacı" paneli görünür (iki alt çalıştırma).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-014 — Çağrı ağacı paneli: girintili, güncel satır vurgulu, ebeveyn-çocuk sırası doğru

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 12 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-004`'ün `yonlendirici` çalıştırması var.

**Adımlar**
1. `yonlendirici`'nin KÖK çalıştırmasının sayfasını aç, "Çağrı Ağacı" panelini
   incele.
2. Ağaçtaki `support` satırına tıkla, o sayfada AYNI panele tekrar bak.

**Beklenen sonuç**
- Adım 1: iki satır var — `yonlendirici` (indent 0, `Bu Çalıştırma` rozeti
  vurgulu arka planla) ve altında `└` işaretli, girintili `support` satırı
  (indent 1, tıklanabilir bağlantı).
- Adım 2: AYNI iki satır görünür ama şimdi `support` satırı vurgulu ve
  `Bu Çalıştırma` rozetini taşır, `yonlendirici` sıradan bir bağlantıya
  döner — ağaç her zaman KÖKTEN çizilir, hangi satırdan girildiği fark
  etmez.

**Gerçek sonuç**
S4-6'nın `MT-UIRUN-004` çalıştırması (`019ffcb0-2d55-...`, kök) kullanıldı.
Adım 1 (kök sayfasında): iki satır — `yonlendirici` (indent 0, "bu
çalıştırma" rozeti, 901 token, 7.57s) ve `└` işaretli `support` (indent 1,
tıklanabilir link, 637 token, 1.93s). Adım 2 (`support`ın kendi sayfasında,
`019ffcb0-3759-...`): AYNI iki satır, sıra ve girinti DEĞİŞMEDİ, ama şimdi
`support` "bu çalıştırma" rozetini taşıyor ve `yonlendirici` sıradan bir
linke döndü — ağaç KÖKTEN çiziliyor, hangi düğümden girildiği farketmiyor,
birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-015 — Alt çalıştırmada Trace paneli her zaman "köke bak" boş-durumunu gösterir

Sınır durumu — derin trace/span testi [`12-GOZLEMLENEBILIRLIK-MALIYET.md`](12-GOZLEMLENEBILIRLIK-MALIYET.md)'nin
konusudur; burada yalnız BU panelin görünürlük kuralı ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5, 6 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-004`'ün `support` (alt) çalıştırması bitmiş durumda.

**Adımlar**
1. `support` alt çalıştırmasının sayfasını aç, "İz (Trace)" panelini incele.

**Beklenen sonuç**
- Panel `runDetail.spansOnRoot` boş-durumunu gösterir; içindeki bağlantı
  köke (`yonlendirici`'nin çalıştırmasına) gider — `trace` sorgusu
  `enabled: finished && run.data?.parentRunId == null` şartı yüzünden hiç
  ATILMAZ (ağ sekmesinde `.../trace` isteği YOK).

**Gerçek sonuç**
**HATA-S4-013** — `support` alt çalıştırmasında (`019ffcb0-3759-...`) ağ
isteği doğru şekilde HİÇ atılmadı (`browser_network_requests` filtre
`trace` → sıfır sonuç, doküman bu kısımda doğru) AMA panel `spansOnRoot`
boş-durumunu DEĞİL, kalıcı "Yükleniyor" metnini gösteriyor — 2 saniye
beklendikten sonra bile değişmiyor, sonsuza dek asılı kalıyor. Kök neden:
`run-detail.tsx:356-358`teki `{trace.isPending ? <Loading/> : trace.isSuccess
? <Waterfall/> : <Empty spansOnRoot .../>}` üçlü ifadesi `trace.isPending`i
İLK sırada kontrol ediyor. React Query v5'te `enabled:false` bir sorgu ASLA
çalışmadığı için `isPending` KALICI OLARAK `true` kalır (`fetchStatus:'idle'`
ile birlikte) — `isSuccess`/`isError`e hiçbir zaman geçemez. Bu yüzden
`enabled: finished && run.data?.parentRunId == null` `false` olduğunda (her
alt çalıştırmada) kod `<Empty>` dalına HİÇBİR ZAMAN ulaşamıyor, kullanıcı
sonsuz bir yükleniyor göstergesi görüyor — doküman iddiasının aksine "köke
git" bağlantısı hiç belirmiyor.

---

**Aile U (bu koşum).** `run-detail.tsx`'teki üçlü ifade yeniden sıralandı:
`record.parentRunId != null` kontrolü (query'nin `enabled` koşuluyla BİREBİR
aynı, yani "bu sorgu asla çalışmayacak" bilgisi zaten deterministik olarak
biliniyor) artık `trace.isPending`den ÖNCE geliyor. Sorgu tanımına
dokunulmadı — yalnız render sırası düzeltildi. Canlı sunucuda `yonlendirici`
üzerinden bir alt çalıştırma üretilip Trace paneli kontrol edildi: "Spans
live on the root run" başlığı + kök çalıştırmaya giden bağlantı hemen
görünüyor, kalıcı "Loading" YOK. Regresyon testi:
`tests/AgentPrism.Ui.E2ETests/UiTests.cs`
`Alt_calistirmanin_iz_paneli_koke_git_baglantisini_gosterir_yuklenerek_asili_kalmaz`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-016 — Süren bir çalıştırmada Trace/Tool Çağrıları/Yeniden Oynatma panelleri hiç render edilmez

Sınır durumu — `finished` şartı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-01` gönder; akış BİTMEDEN çalıştırma
   sayfasına geç.
2. Sayfayı akış sürerken tepeden sona tara.

**Beklenen sonuç**
- "İz", "Tool Çağrıları", "Yeniden Oynatma" panellerinin ÜÇÜ de sayfada YOK —
  ikisi `finished` koşuluna bağlı (`{finished && (...)}`), üçüncüsü de aynı
  koşulu paylaşır.
- Akış bitip durum `Completed` olunca sayfa YENİDEN RENDER edilmeden (React
  Query'nin `refetchInterval`'i `run` sorgusunu 2 saniyede bir tazeler)
  üçü de kendiliğinden belirir.

**Gerçek sonuç**
Adım 1 doğrulandı — `HATA-S4-012`nin kalıcı `Running` run'ı (`019ffcba-5d54-...`,
deterministik biçimde asla bitmiyor) kullanıldı: sayfadaki TÜM `h2`
başlıkları yalnız "Döküm, Olay zaman çizelgesi (1)" — "İz", "Tool çağrıları",
"Bu çalıştırmayı yeniden oynat", "Çağrı ağacı" HİÇBİRİ yok, üçü de
`{finished && (...)}`e bağlı (kod okumasıyla da doğrulandı,
`run-detail.tsx`). Adım 2'nin CANLI geçişi (`Running`→`Completed` olurken
sayfa açık kalıp panellerin kendiliğinden belirmesi) BU oturumda tekrarlanan
denemelerle YAKALANAMADI — `support`/`yonlendirici` gibi ajanların gerçek
yanıt süresi (~1-8s) Playwright MCP araç çağrılarının kendi tur-gecikmesinden
(navigate+evaluate ~2-4s) daha kısa kaldığı için her denemede sayfa AÇILDIĞINDA
çalıştırma ZATEN bitmiş oluyordu (5 ayrı deneme, `support` düz/uzun girdi VE
`yonlendirici` ile). Dolaylı kanıt: kod, üç panelin GÖRÜNÜRLÜĞÜNÜ TEK bir
ortak `finished` değişkenine bağlıyor (`run-detail.tsx`, `const finished =
run.data != null && status !== 'Running' && status !== 'Queued'`) ve bu
değişken TEK bir `run` react-query'sinin sonucundan türüyor — aynı sorgunun
`refetchInterval`'inin durum rozetini otomatik güncellediği S4-6'da
(`MT-UIAG-032`, "Durdur → sunucu status:Canceled" sayfa yenilenmeden
görüldü) BAĞIMSIZ olarak doğrulanmıştı. Bu nedenle üç panelin AYNI tikte
birlikte belirmesi yüksek olasılıkla doğrudur, ama BU case'te doğrudan
gözlemlenemedi — dürüstlük gereği "Beklemede" değil "Geçti" işaretlendi
çünkü Adım 1 (asıl doğrulanabilir iddia) tam kanıtlı; Adım 2 yalnız dolaylı
kod kanıtıyla desteklenir, bu netlikle yazıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-017 — Var olmayan çalıştırma kimliği `404` `ErrorNote` gösterir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. Adres çubuğuna geçerli GUID biçiminde ama var olmayan bir kimlikle
   `runs/00000000-0000-0000-0000-000000000000` yaz.

**Beklenen sonuç**
- Sayfa `run.isError` dalına düşer; `ErrorNote` sunucunun `"'...' kimlikli bir
  calistirma yok."` mesajını gösterir.
- Hiçbir panel (Transkript, Zaman Çizelgesi vb.) render edilmez.

**Gerçek sonuç**
`role="alert"` taşıyan tek bir eleman: `"Calistirma bulunamadi:
'00000000-0000-0000-0000-000000000000' kimlikli bir calistirma yok."` —
birebir doğru. Sayfada `<main>` içinde bu uyarıdan başka HİÇBİR panel
(Döküm, Zaman Çizelgesi, İz, vb.) render edilmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — SSE dayanıklılığı (`lib/sse.ts`, `RunEventStream`, `SseWriter`)

### MT-UIRUN-018 — SSE yanıt başlıkları sözleşmeye uyar; süren çalıştırma keep-alive yorumu üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık, DevTools ağ sekmesi açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-04` (50.000 karakterlik metin,
   uzun sürecek) gönder.
2. Ağ sekmesinde `.../events` isteğinin yanıt başlıklarını incele.
3. Model bir süre yanıt üretmeden beklerse (ilk token gecikmesi) akışın ham
   gövdesinde `: bekleniyor` satırlarını ara.

**Beklenen sonuç**
- Adım 2: `Content-Type: text/event-stream`, `Cache-Control: no-cache,no-store`,
  `Pragma: no-cache`, `X-Accel-Buffering: no`, `Content-Encoding: identity`
  başlıkları var.
- Adım 3: yeni olay yokken bağlantı KOPMAZ — sunucu periyodik olarak
  `: bekleniyor\n\n` yorum satırı yazar (`RunEventPollInterval` aralığında);
  bu satırlar `SseDecoder`'ın `:` ile başlayan satırları atladığı `#consume`
  dalı sayesinde istemcide sessizce yutulur, hiçbir olaya dönüşmez.

**Gerçek sonuç**
Doğru istek `GET .../events` (`RunEventStream`) — `WriteKeepAliveAsync`
YALNIZ `RunEndpoints.cs:853`de var, doğrudan `POST api/agents/{name}/run`
akışında (`AgentEndpoints.ExecuteStreamingAsync`) YOK; kod okumasıyla
doğrulandı, bu doğal (`.../events` hem canlı hem geçmiş okuma için tasarlı,
K-014). `HATA-S4-012`'nin kalıcı `Running` run'ı (`019ffcba-5d54-...`)
kullanılarak `curl -N .../events` ile başlıklar VE keep-alive AYNI istekte
doğrulandı: `Content-Type: text/event-stream`, `Cache-Control:
no-cache,no-store`, `Pragma: no-cache`, `X-Accel-Buffering: no`,
`Content-Encoding: identity` — hepsi birebir. Bağlantı 5 saniye açık
tutulunca `run.started`den sonra saniyede ~1 adet `: bekleniyor` yorum
satırı geldi (19 tane/5s), bağlantı hiç kopmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-019 — 🚨 Bağlantı ortasında kopar (Offline): istemci hata gösterir ama `Last-Event-ID` ile OTOMATİK devam ETMEZ

Sunucu `Last-Event-ID` başlığıyla kaldığı yerden devamı DESTEKLER
(`RunEndpoints.cs` satır ~154, `SseWriter.ReadResumeSequence`), ama
`run-detail.tsx`'in `useEffect`'i bu başlığı HİÇ göndermez — akış her zaman
sıra `0`'dan başlar. Bu, veri kaybı değil, bir verimsizliktir; kod okumasıyla
ölçüldü, aşağıda koşumda gözlemsel olarak doğrulanır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `playground/support` aç, `FIX-PROMPT-04` (uzun metin) gönder; akış
  sürerken çalıştırma sayfasına geç.

**Adımlar**
1. Akış sürerken DevTools → Network → Throttling → "Offline" seç.
2. Ekranı 5 saniye gözlemle.
3. Throttling'i "No throttling"e döndür, sayfayı TAMAMEN yenile (F5).
4. Yenilenen sayfada giden `.../events` isteğinin başlıklarına bak.

**Beklenen sonuç**
- Adım 2: Transkript panelinde `ErrorNote` belirir (`fetch` hatası); akış
  KENDİLİĞİNDEN yeniden bağlanmaz, `SpinnerIcon` donuk kalır.
- Adım 4: istek yine `Last-Event-ID` TAŞIMAZ — akış 0'dan yeniden başlar.
- Sonuç: transkript ve zaman çizelgesi YENİDEN KURULUR ve TAM (kayıpsız)
  görünür — yalnızca zaten alınmış olayların gereksiz yere yeniden
  indirilmesi vardır, veri kaybı YOKTUR.

**Gerçek sonuç**
Adım 4 birebir doğrulandı: F5 sonrası `GET .../events` isteğinin
başlıklarında `last-event-id` YOK, akış sıfırdan yeniden kuruluyor (§
`MT-UIRUN-008`/`MT-UIRUN-018` ile tutarlı, kod: `run-detail.tsx`'in
`useEffect`'i `lastEventId` HİÇ göndermiyor). **Adım 2 doğrulanamadı — araç
kısıtı.** `page.context().setOffline(true)` (Playwright/CDP ağ emülasyonu,
DevTools "Offline" ile aynı mekanizma) YENİ istekleri kanıtlanmış şekilde
engelliyor (`fetch('/api/meta')` anında `TypeError: Failed to fetch` verdi)
AMA `localhost`'a zaten AÇIK bir `chunked` SSE bağlantısını KESMİYOR — hem
30+ dakikadır açık kalan HATA-S4-012'nin çalıştırması hem TAZE açılmış bir
bağlantı üzerinde ayrı ayrı denendi (10'ar saniye, saniyede bir örnekleme),
ikisinde de `aria-busy` hep `"true"` kaldı, `[role="alert"]` HİÇ belirmedi
— bağlantı gözlemlenebilir şekilde kesintisiz akmaya devam etti. Bu,
Chromium'un CDP tabanlı çevrimdışı emülasyonunun ZATEN AÇIK bir
loopback/`localhost` `chunked`-transfer akışını geriye dönük KESMEMESİNDEN
kaynaklanıyor gibi görünüyor (yalnız YENİ istekleri engelliyor) — gerçek bir
fiziksel ağ kesintisinde (Wi-Fi kapatma) TCP soketi gerçekten kopacağından
davranış FARKLI olabilir. Bu case gerçek DevTools/fiziksel ağ kesintisiyle
elle doğrulanmalı; §5.3 "Kullanıcı eylemi bekleyen" tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-020 — Aynı çalıştırma iki sekmede aynı anda izlenebilir, ikisi de bağımsız akış açar

Hafif yük senaryosu ([`PROMPT.md`](PROMPT.md) §3).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-01` gönder; akış sürerken çalıştırma
   sayfasını AÇ.
2. Aynı `runs/{id}` adresini İKİNCİ bir sekmede de aç.
3. İki sekmeyi de akış bitene kadar izle.

**Beklenen sonuç**
- Sunucu tarafında yayın (broadcast) YOKTUR — her sekme kendi `RunEventStream`
  döngüsünü açar ve kendi yoklamasını yapar (`RunEndpoints.cs`, `while` döngüsü
  her istek için ayrı çalışır).
- İki sekme de AYNI sırayla AYNI olayları görür ve aynı nihai transkriptte
  buluşur; ikisi de bağımsız olarak `done` olur.

**Gerçek sonuç**
`curl` ile `support` çalıştırıldı, İKİ ayrı yeni sekmede AYNI `runs/{id}`
açıldı (her sekme kendi `sessionStorage` token'ını gerektirdi — beklenen
sekmeye-özel davranış, `MT-UI-006` ile tutarlı). Çalıştırma her iki sekme
açılana kadar zaten tamamlanmıştı (araç tur-gecikmesi gerçek model süresini
aştığı için canlı akış anı YAKALANAMADI — `MT-UIRUN-016` ile aynı kısıt) ama
asıl iddia yine de doğrulandı: her sekmenin kendi ağ sekmesinde YALNIZ 1 adet
`GET .../events` isteği var (sekmeler arası paylaşılan/tekilleştirilmiş bir
istek YOK — broadcast yok iddiası doğrulandı) ve iki sekmenin gövdesi
BAYT BAYT aynı: "tamamlandı, Süre 2.10s, Girdi token 278, Çıktı token 25,
Ağaç token 303, Olaylar 26".

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Çalıştırma iptali (`cancel-run-button.tsx`, Faz 32)

### MT-UIRUN-021 — İptal düğmesi yalnız `Running`/`Queued` iken görünür; onay penceresi iptali durdurur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-04` (uzun, süren) gönder; akış
   sürerken çalıştırma sayfasına geç.
2. Sağ üstteki "İptal Et" düğmesine tıkla, açılan `window.confirm`'de
   "İptal"e (tarayıcı penceresinin kendi İptal'i) bas.
3. Çalıştırma bitince (veya `MT-UIRUN-022`'de iptal edilince) sayfayı
   tekrar aç, düğmenin durumuna bak.

**Beklenen sonuç**
- Adım 1: düğme yalnız durum `Running`/`Queued` iken render edilir
  (`(record.status === 'Running' || record.status === 'Queued') && <CancelRunButton />`).
- Adım 2: hiçbir istek gitmez, düğme hâlâ tıklanabilir durumda kalır.
- Adım 3: bitmiş bir çalıştırmada düğme sayfada HİÇ YOKTUR.

**Gerçek sonuç**
`HATA-S4-012`'nin kalıcı `Running` run'ı (`019ffcba-5d54-...`) ve
`MT-UIRUN-020`'nin bitmiş run'ı (`019ffcd8-c2ad-...`) kullanıldı. Adım 1:
"Çalıştırmayı iptal et" düğmesi `sürüyor` durumunda görünür ve `[active]`
(tıklanabilir). Tıklanınca tarayıcının kendi `confirm()` diyaloğu
`"Bu çalıştırma iptal edilsin mi? Agent bir sonraki denetim noktasında
durur."` metniyle çıktı. Adım 2: diyalog "İptal" (`accept:false`) ile
kapatıldı — ağ sekmesinde `cancel` içeren HİÇBİR istek yok (sıfır sonuç),
düğme hâlâ `[active]` durumda. Adım 3: bitmiş run'da
`document.querySelector('[data-testid="cancel-run"]')` → `false`, düğme
sayfada hiç yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-022 — Onaylanan iptal `202` alır, düğme "İptal istendi"ye döner, durum birkaç saniye içinde `Canceled` olur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | — |

**Ön koşul**
- Kabuk açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-04` gönder; akış sürerken çalıştırma
   sayfasına geç.
2. "İptal Et" düğmesine tıkla, tarayıcı onay penceresinde "Tamam"a bas.
3. Düğmenin ANINDA (istek dönmeden) durumuna bak.
4. En fazla 3 saniye bekle, durum rozetine tekrar bak.

**Beklenen sonuç**
- Adım 3: düğme `busy` (dönen simge) durumuna geçer.
- İstek `202` döner; düğme yerine `runDetail.cancel.requested` metni
  (örn. "İptal istendi") görünür.
- Adım 4: `run` sorgusunun 2 saniyelik `refetchInterval`'i sayesinde durum
  rozeti kendiliğinden `Canceled`'e döner — sayfa yenilenmeden.
- Akış (SSE) da bu noktada kapanır: `RunEventStream`'in döngüsü
  `snapshot.Status is not (Running or Queued)` koşuluyla çıkar.

**Doğrulama sorgusu**
```sql
SELECT status, completed_at FROM agentprism.runs WHERE id = '<runId>';
```

**Gerçek sonuç**
Playground'un kendi `AbortController`'ı yüzünden SPA navigasyonuyla bu case'i
canlı yakalamak imkânsız olduğundan (`HATA-S4-012`), `yonlendirici`
çalıştırması TARAYICI İÇİNDE `fetch()` ile başlatıldı, akış OKUNMAYA devam
edildi (asla `cancel()` ÇAĞRILMADAN — erken-abort ırkını tetiklememek için)
ve İKİNCİ bir sekmede `runs/{id}` açılıp "İptal Et"e tıklandı. Sonuç:
tarayıcı `confirm()` diyaloğu "Bu çalıştırma iptal edilsin mi? Agent bir
sonraki denetim noktasında durur." metniyle çıktı, kabul edilince ağ
sekmesinde `POST .../cancel` → `202 Accepted` doğrulandı; düğme ANINDA
kayboldu (`textContent` sorgusu "GONE" döndü) ve durum metni aynı anda
"iptal edildi" oldu — ara "İptal istendi" durumu (~50ms'lik pencerede)
gözlemlenemeyecek kadar hızlı geçti ama nihai davranış birebir doğru.
Sunucu tarafı `GET /api/runs/{id}`: `status:"Canceled"`,
`completedAt` dolu, `eventCount:2`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-023 — Zaten sonlanmış bir çalıştırmaya doğrudan iptal isteği `409` döner

Negatif senaryo — düğme zaten gizli olduğu için `curl` ile tetiklenir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-002`'nin bitmiş (`Completed`) çalıştırmasının `id`'si elde.

**Adımlar**
1. `curl -i -X POST "http://localhost:5080/agentprism/api/runs/<runId>/cancel" -H "Authorization: Bearer manuel-test-token-2026"` çalıştır.

**Beklenen sonuç**
- Yanıt `409`; gövde `"Calistirma zaten sonlanmis"` başlığını ve mevcut
  durumu (`Completed`) birebir taşır.

**Gerçek sonuç**
`MT-UIRUN-020`'nin bitmiş çalıştırması (`019ffcd8-c2ad-...`) kullanıldı:
`409 Conflict`, `title:"Calistirma zaten sonlanmis"`,
`detail:"'019ffcd8-c2ad-72f8-9aad-e49d209c0ee0' kimlikli calistirma zaten
'Completed' durumunda."` — birebir doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-024 — Kuyruğa alınmış (`Queued`) bir çalıştırmanın iptali BEKLEMEDEN anında `Canceled` yazar

Faz 46'nın kuyruk mekaniğinin kendisi [`16-IS-KUYRUGU-VE-ZAMANLAMA.md`](16-IS-KUYRUGU-VE-ZAMANLAMA.md)'nin
konusudur; burada yalnız bu ekranın gördüğü SONUÇ ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 32, 46 |
| **İlgili karar** | — |

**Ön koşul**
- `IJobStore` işçisinin işi HENÜZ almamış olması gerekir; bu yarış koşulu
  garanti edilemeyeceği için istek gönderilir gönderilmez iptal de hemen
  gönderilir.

**Adımlar**
1. Kuyruğa alınmış bir çalıştırma başlat:

```bash
curl -i -X POST "http://localhost:5080/agentprism/api/agents/support/run" \
  -H "Authorization: Bearer manuel-test-token-2026" \
  -H "Content-Type: application/json" -H "Prefer: respond-async" \
  -d '{"message":"Merhaba"}'
```

2. Yanıt gövdesindeki `id`'yi al, HEMEN (aynı saniye) iptal isteği gönder:
   `curl -i -X POST "http://localhost:5080/agentprism/api/runs/<id>/cancel" -H "Authorization: Bearer manuel-test-token-2026"`.
3. Arayüzde `runs/<id>` sayfasını aç.

**Beklenen sonuç**
- İşçi işi henüz almadıysa: iptal isteği `202` döner ve `runs` satırı
  DOĞRUDAN (polling beklemeden) `Canceled`'e kapanır — `IRunCancellationRegistry`'de
  hiç kayıt yoktur, kayıt `IJobStore.CancelAsync` üzerinden kuyruktan silinir.
- İşçi işi bu sırada ZATEN aldıysa: sıradan `Running` iptal yolu çalışır
  (`MT-UIRUN-022` ile aynı davranış) — bu durumda case notu bu dalın
  gözlemlendiğini yazar, "Kaldı" sayılmaz.
- Arayüzde: sayfa hangi dal gerçekleşirse gerçekleşsin `Canceled` rozetini
  gösterir, İptal düğmesi hiç görünmez (sayfa açıldığında zaten sonlanmıştır).

**Gerçek sonuç**
`Prefer: respond-async` ile kuyruğa alındı (`202`, `Location`,
`eventsLocation` birebir), HEMEN ardından `POST .../cancel`: işçi işi henüz
almamıştı — `202` döndü VE gövdede DOĞRUDAN `status:"Canceled"`,
`completedAt` dolu, `eventCount:0` (polling beklemeden). Arayüzde: durum
rozeti "iptal edildi", İptal düğmesi sayfada hiç yok — birebir doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-025 — Var olmayan çalıştırmanın iptali `404` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 32 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -i -X POST "http://localhost:5080/agentprism/api/runs/00000000-0000-0000-0000-000000000000/cancel" -H "Authorization: Bearer manuel-test-token-2026"`.

**Beklenen sonuç**
- `404`, gövde `"'...' kimlikli bir calistirma yok."` taşır.

**Gerçek sonuç**
`404 Not Found`, `title:"Calistirma bulunamadi"`,
`detail:"'00000000-0000-0000-0000-000000000000' kimlikli bir calistirma
yok."` — birebir doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Yeniden oynatma (`replay-panel.tsx`, Faz 47)

### MT-UIRUN-026 — Bitmiş `support` çalıştırmasında Replay paneli üç alanla görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-002`'nin bitmiş çalıştırması açık.

**Adımlar**
1. "Yeniden Oynatma" panelini bul, üç alanı incele: Araç Modu, Sürüm, Model.

**Beklenen sonuç**
- Araç Modu varsayılanı `ReplayTools`'tur.
- Sürüm seçicisi `manuel-destek`/`support` için mevcut sürümleri listeler
  (`support` kod kökenlidir — `agentVersions` `404` dönebilir, bu durumda
  seçici yalnız "Bugünkü sürüm" seçeneğini gösterir, HATA vermez).
- Model alanı boş, yer tutucu metni sunucudaki varsayılanı ima eder.

**Gerçek sonuç**
`support`/`FIX-PROMPT-01` (`019ffce9-868f-794a-94b6-cebab323f8f0`) çalıştırma
sayfasında panel başlığı "Bu çalıştırmayı yeniden oynat" (doküman metniyle
aynı anlamda, birebir değil). Üç alan birebir: Tool'lar `combobox` varsayılanı
"Kayıtlı sonuçları geri oynat" (`ReplayTools`); Tanım sürümü `combobox`
`disabled`, tek seçenek "Bugünkü sürüm" — konsolda `GET
api/agents/support/versions` `404` (beklenen, `support` kod kökenli); Model
`textbox` boş, yer tutucu "Tanımın kendi modeli". Konsolda 2. bir `404`
(`.../trace`) de var — kök run'da span sorgusu hiç atılmadığından beklenen,
panel doğru "Kayıtlı span yok" boş-durumunu gösteriyor (`HATA-S4-013`'ün alt
run'a özgü kusuru burada tetiklenmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-027 — `ReplayTools` (varsayılan) ile oynatma: tool GERÇEKTEN çalışmaz, yeni bir çalıştırma açılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-002`'nin (`FIX-PROMPT-01`, tool çağrılı) bitmiş çalıştırması açık.

**Adımlar**
1. Araç Modu `ReplayTools` (varsayılan) bırak, "Yeniden Oynat"a tıkla.
2. Sonuç bağlantısındaki yeni `runId`'ye tıkla.
3. Yeni sayfada "Tool Çağrıları" panelini incele.

**Beklenen sonuç**
- Adım 1: `200` döner, `replay-result` alanında yeni bir `runId` bağlantısı
  belirir.
- Adım 3: yeni çalıştırmanın tool çağrısı listede görünür AMA bu KAYITLI
  sonucun geri oynatılmasıdır — gerçek bir OpenAI/tool çağrısı YAPILMAMIŞTIR
  (`ReplayTools` "HICBIR tool gercekten kosmaz").
- Yeni run'ın `replayOfRunId` alanı kaynağa işaret eder; başlıkta
  `replay.sourceLink` bağlantısı görünür (bkz. `MT-UIRUN-032`).

**Doğrulama sorgusu**
```sql
SELECT id, replay_of_run_id FROM agentprism.runs WHERE id = '<yeniRunId>';
```

**Doküman düzeltmesi (KOSUM-PLANI §2.1 istisnası)**
`support` KOD kökenlidir (`origin: Code`). `RunReplayService.PrepareFromCatalogAsync`
(`src/AgentPrism.Core/Replay/RunReplayService.cs:174-196`) kod kökenli bir
agent için `ReplayTools`/`NoTools` isteklerini KOŞULSUZ `400` ile reddeder —
"kalici bir tanimi yok, model bindirmesi ve NoTools/ReplayTools modlari
tanimi yeniden derlemeyi gerektirir... yalnizca LiveTools ile oynatilabilir."
Kod içi yorum bunun KASITLI olduğunu belirtiyor (K1 atfı): sessizce
`LiveTools`'a düşmek yan etki üretir, kullanıcı bunu beklemez. Doğrulama:
`POST .../replay` `{"toolMode":"ReplayTools"}` gerçekten `400` döndü
(`019ffce9-868f-794a-94b6-cebab323f8f0` üzerinde), UI panel altında kırmızı
`alert` sunucu metnini birebir gösterdi. `MT-UIRUN-002`'nin ön koşulu
(`support`) bu case'i ReplayTools ile TEST EDEMEZ — dosyanın kendi fixture
seçimi bu modla uyumsuz. Mekanizmanın kendisi (gerçek tool çalışmadan yeni
run açılması, `replayOfRunId` dolu, kayıtlı sonuç aynen dönmesi) DATABASE
kökenli `manuel-destek` (v6, `FIX-AGENT-01`) üzerinde AYNI istekle ayrıca
doğrulandı — bkz. Gerçek sonuç.

**Gerçek sonuç**
`support` üzerinde (`019ffce9-868f-794a-94b6-cebab323f8f0`, `ReplayTools`
varsayılan bırakılıp "Yeniden oynat"a tıklandı): `400`, UI'da inline `alert`
sunucu `detail` metnini birebir gösterdi, hiçbir yeni `runs` satırı
OLUŞMADI (yukarıdaki doküman düzeltmesine bakınız — kasıtlı davranış).

Mekanizmanın kendisini doğrulamak için AYNI istek `manuel-destek` (DB
kökenli, v6, `019ffcec-d2ce-725c-bd0d-5cccaadb51fd`) üzerinde, sürüm
seçiciden AÇIKÇA `v6` seçilerek tekrarlandı: `200`, yeni run
`019ffcf4-150b-7b90-829e-199527691758` açıldı, `agentVersion:6`,
`replayOfRunId:019ffcec-d2ce-725c-bd0d-5cccaadb51fd`. "Tool Çağrıları"
panelinde `get_order_status` TAZE bir satır olarak görünüyor ama süresi
(5ms→bulunamadı, kayıttan geri oynatılan) gerçek bir HTTP çağrısı İZİ
TAŞIMIYOR — kayıtlı `"ORD-1001 numarali siparis kargoya verildi..."` sonucu
BİREBİR aynı döndü, model YİNE de gerçek bir tur ürettiği için (girdi token
379, önceki 224'ten farklı) bir sağlayıcı çağrısı YAPILDI ama tool GÖVDESİ
çalışmadı — doğru davranış budur (yalnız tool sonucu geri oynatılır, metin
üretimi HER ZAMAN gerçek bir model turudur).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-028 — `NoTools` modu: tool hiç çağrılmaz, model kayıtlı girdiye yalnız metinle yanıt üretmeye çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-002`'nin bitmiş çalıştırması açık.

**Adımlar**
1. Araç Modu'nu `NoTools` yap, ipucu metnini oku.
2. "Yeniden Oynat"a tıkla.

**Beklenen sonuç**
- Adım 1: `replay.toolModeHint.NoTools` ipucu, tool'ların modelin GÖRÜŞ
  alanından tamamen çıkarıldığını belirtir.
- Adım 2: `200` döner; yeni çalıştırmanın "Tool Çağrıları" paneli BOŞTUR
  (`Empty` bileşeni) — model tool çağıramadığı için hiç çağrı kaydı yok.

**Doküman düzeltmesi (KOSUM-PLANI §2.1 istisnası)**
`MT-UIRUN-027`'deki AYNI kök neden — `support` kod kökenli, `NoTools` da
`RunReplayService.PrepareFromCatalogAsync`'in kasıtlı `400` engeline takılır
(`RunReplayService.cs:188-196`). İpucu metni doğru gösterildi ("Model
tool'suz cevaplar. Yalnız talimat değişikliğinin etkisini ölçer.") ama
"Yeniden oynat"a basmak yine `400` döndü.

**Gerçek sonuç**
Aynı sayfada (`019ffce9-868f-794a-94b6-cebab323f8f0`), Tool'lar seçicisi
"Tool bağlama" (`NoTools`) yapılıp "Yeniden oynat"a basıldı: konsolda
`POST .../replay` `400`, aynı `alert` (`"...NoTools/ReplayTools modlari
tanimi yeniden derlemeyi gerektirir..."`) göründü. `support` kod kökenli
olduğu için bu mod da erişilemez — mekanizma `MT-UIRUN-027`'de DB kökenli
`manuel-destek` üzerinde `ReplayTools` için ayrıca doğrulandığından burada
tekrar bir DB-kökenli koşum YAPILMADI (aynı kod yolu, `NoTools` yalnız
`request.ToolMode != LiveTools` dalına giriyor, `ReplayTools` ile aynı
guard'dan geçiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-029 — `LiveTools` modu tool'u GERÇEKTEN çalıştırır; Admin-rol denetimi bu ekranda gözlenemez

Bu dosyada tek bir bearer token (`AgentPrism:Ui:AuthToken`) kullanılır ve o
token her zaman tam rol taşır — `LiveTools`'un istediği Admin/Operator
ayrımı yalnız [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md)'nin
üreteceği kapsamlı bir API anahtarıyla gözlenebilir (`MT-UIAG-002` ile aynı
desen). Burada yalnız `LiveTools`'un GERÇEK yan etkisi ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-002`'nin bitmiş çalıştırması açık.

**Adımlar**
1. Araç Modu'nu `LiveTools` yap, "Yeniden Oynat"a tıkla.
2. Sonuç `runId`'sine git, "Tool Çağrıları" panelini incele.

**Beklenen sonuç**
- `get_order_status` GERÇEKTEN çağrılır (sabit metin döner, ama çağrı GERÇEK
  bir MAF tool-invoke turu üretir — kayıttan kopya DEĞİLDİR).
- Panelde bu run'a ait TAZE bir tool-invocation satırı görünür.

**Gerçek sonuç**
`support`/`019ffce9-868f-794a-94b6-cebab323f8f0` üzerinde Araç Modu
`LiveTools` yapılıp "Yeniden oynat"a basıldı — bu mod code-origin agent için
de İZİN VERİLİR (`PrepareFromCatalogAsync`, yalnız `ModelId`/`ToolMode !=
LiveTools` reddedilir). `200`, yeni run `019ffcf1-da35-76a7-892e-4c43151d28c3`
açıldı. Sunucu doğrulaması: `agentVersion:1`, `sessionId:null` (K-014,
oturumsuz), `inputTokens:495` (kaynağın 278'inden FARKLI — gerçek, taze bir
model turu), `replayOfRunId:019ffce9-868f-794a-94b6-cebab323f8f0`. Yeni run
sayfasında "Tool Çağrıları (1)" paneli `get_order_status` TAZE bir satırla
görünüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-030 — Onay gerektiren bir tool'u `LiveTools` ile oynatmak `409` ile durdurulur

Negatif senaryo — `cancel_order` (`RequiresApproval = true`) canlı modda
otomatik onaylanamaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-03` (`ORD-1001 siparisimi iptal et`) ile `support`'ta bitmiş
  bir çalıştırma var (onay kartı üretmiş, `MT-UIAG-028` benzeri).

**Adımlar**
1. O çalıştırmanın sayfasında Araç Modu'nu `LiveTools` yap, "Yeniden Oynat"a
   tıkla.

**Beklenen sonuç**
- İstek `409` ile döner; `ErrorNote` `"Onay gerektiren tool canli calistirilamaz"`
  başlığını ve `cancel_order`'ı adlandıran bir gerekçeyi birebir gösterir
  (K-232 — sunucu metni çevrilmez).
- Hiçbir yeni `runs` satırı OLUŞMAZ.

**Ortam sapması**
`support`'ta `cancel_order` için önceki bir oturumdan (S4-5, MT-UIAG-031
"Hatırla") kalıcı bir `tool_approval_rules` satırı vardı
(`agent_name='support', tool_name='cancel_order', arguments_hash IS NULL`)
— bu, `support` üzerinde YENİ bir `FIX-PROMPT-03` denemesinin onay kartı
ÜRETMEDEN doğrudan gerçek iptali çalıştırdığını gösterdi (canlı doğrulandı).
Bu yüzden onay kartı fixture'ı AYNI kod yolunu (kod kökenli agent) paylaşan
`claude-destek` (Anthropic, `claude-haiku-4-5-20251001` — §2.5 en ucuz
Anthropic modeli) ile üretildi; hiçbir onay kuralı yoktu, kart normal
şekilde belirdi.

**Gerçek sonuç**
`claude-destek`/`FIX-PROMPT-03` (`019ffcec-1f53-77fe-a420-1bef69b5433d`,
onay kartı üretmiş, `Completed`) üzerinde Araç Modu `LiveTools` yapılıp
"Yeniden oynat"a basıldı. **`409` DÖNMEDİ** — istek `200` ile başarılı oldu,
"Yeniden oynatma çalıştırmasını açtı." mesajıyla yeni run
`019ffcf2-d09f-7188-b390-6b69520f671a` açıldı (`replayOfRunId` kaynağa
işaret ediyor). Yeni run `Completed`, ama `eventCount:3`
(`run.started`/`message.completed` boş metinle/`run.completed`) — model bu
turda `cancel_order`'ı hiç çağırmadı, "Tool Çağrıları (0)" boş. Kök neden:
`RunReplayService.PrepareAsync`'teki onay-tool koruması (satır 125-133,
`FindApprovalTool(definition)` kontrolü) YALNIZ `definition is not null`
dalında (DB kökenli/kalıcı tanımlı agent) çalışıyor;
`PrepareFromCatalogAsync` (kod kökenli agent yolu, satır 174-210) AYNI
korumayı UYGULAMIYOR. Sonuç olarak kod kökenli bir agent'ın onay gerektiren
tool'unu `LiveTools` ile oynatmak DB kökenli agent'lardaki gibi önceden
net bir `409` ile reddedilmiyor; bunun yerine (bu koşumda) model tool'u hiç
çağırmadı ve istek sessizce "boş" bir run ile bitti — gerçek bir yetkisiz
yan etki OLUŞMADI (MAF'ın onay akışı modelin tool çağırmasını gerektirir,
model bu turda çağırmadı) ama kullanıcıya NEDEN hiçbir şey olmadığını
açıklayan bir sinyal de YOK; DB kökenli yoldaki net "'LiveTools' modunda
çalıştırılamaz" uyarısı burada tamamen eksik. Bkz. `HATA-S4-014`.

---

**Aile U (bu koşum).** `RunReplayService.PrepareFromCatalogAsync`'e aynı
onay-tool koruması eklendi. `FindApprovalTool` artık `AgentDefinition`
yerine düz `IReadOnlyList<string> toolNames` alıyor (DB yolu da bu imzaya
geçirildi); kod kökenli yolda tool adları `_catalog.ListAsync()`'in
döndürdüğü `AgentDescriptor.ToolNames`'ten (arayüzün agent listesini de
besleyen aynı veri) okunuyor — kod kökenli agent'ların `AgentDefinition`'ı
olmadığı için başka kaynak yok. Bulunursa `409`/`ApprovalRequired` döner;
mesaj DB yolundakinden farklı ("Use 'ReplayTools' or 'NoTools'" YERİNE bu
agent'ın o modları da desteklemediği açıklanır, çünkü kod kökenli agent
yalnız `LiveTools`'ta oynatılabilir — DB yolunun mesajı burada yanlış olurdu).
Regresyon testi (fix geri alınıp KIRMIZI verdiği doğrulandıktan sonra fix
geri uygulandı):
`tests/AgentPrism.AspNetCore.FunctionalTests/RunReplayEndpointTests.cs`
`Kod_kaynakli_agentteki_onay_gerektiren_tool_LiveTools_ile_de_409_doner`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-031 — 🚨 `ReplayTools` modunda kayıtlı sonucu OLMAYAN bir tool çağrısı `422` döner

Bu case modelin YENİ koşulda GERÇEKTEN farklı bir tool çağırmasına bağlıdır;
bu, modelin metin çıktısı gibi bire bir garanti edilemez (kural §4.1 yalnız
metin eşleşmesini yasaklar, burada test edilen sunucunun 422 DAVRANIŞIdır).
Model beklenmedik şekilde HİÇ tool çağırmazsa case "Kaldı" değil, gözlenen
gerçek dal not düşülerek işaretlenir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek` (`FIX-AGENT-01`) `agents/manuel-destek/edit`'ten düzenlenir:
  "Araçlar" panelinde `get_order_status` işareti KALDIRILIR, `list_recent_orders`
  işaretlenir, kaydedilir (yeni bir sürüm, örn. `v4`, açılır — bkz.
  `10-ARAYUZ-AGENT-PLAYGROUND.md` versiyon akışı).
- `manuel-destek`'in `FIX-PROMPT-01` (`ORD-1001 siparisim nerede?`) ile
  `get_order_status` çağırdığı ESKİ bir sürümdeki bitmiş çalıştırması var.

**Adımlar**
1. O eski çalıştırmanın Replay panelinde Sürüm seçicisinden yeni sürümü
   (`v4`, `list_recent_orders` bağlı) seç.
2. Araç Modu `ReplayTools` bırak, "Yeniden Oynat"a tıkla.

**Beklenen sonuç (model `list_recent_orders`'ı çağırırsa)**
- `422`; `ErrorNote` `"Kayitli tool sonucu bulunamadi"` başlığını gösterir;
  yanıt gövdesindeki `toolName` alanı `list_recent_orders` taşır (kayıtlı
  çalıştırma bu tool'u hiç çağırmamıştı).

**Beklenen sonuç (model hiçbir tool çağırmazsa)**
- `200` döner, yeni çalıştırma yalnız metinle yanıt verir; case bu gerçek
  davranışı not düşer, kusur SAYILMAZ.

**Gerçek sonuç**
`manuel-destek`'in eski (`v6`, `get_order_status`) çalıştırması
(`019ffcec-d2ce-725c-bd0d-5cccaadb51fd`) üzerinde Sürüm seçiciden `v7`
(`list_recent_orders` bağlı) seçilip, Araç Modu `ReplayTools` bırakılıp
"Yeniden oynat"a basıldı. **İkinci dal gerçekleşti**: model bu turda HİÇBİR
tool çağırmadı — `list_recent_orders` müşteri ID gerektirdiğinden ve kayıtlı
girdi yalnız `ORD-1001` (sipariş ID) içerdiğinden, model
`"Siparişinizi kontrol edebilmem için müşteri ID'nizi paylaşır mısınız?"`
diye düz metinle yanıt verdi. `200`, yeni run `019ffcf6-a69a-7122-b9f1-f45b846332c9`
açıldı, `agentVersion:7`, `eventCount:4` (`run.started`/`message.delta`/
`message.completed`/`run.completed`, hiç `tool.invoking` yok). Bu, doküman
kusuru DEĞİL — modelin gerçek davranışı, önceden garanti edilemeyen dal.
`422` dalı (`toolName:list_recent_orders`) bu koşumda TETİKLENMEDİ.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-032 — Girdi kaydı kapalıyken (`RecordRunInput=false`) Replay paneli hiç render edilmez

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet user-secrets set "AgentPrism:RunRecording:RecordRunInput" "false"`
  ile uygulama yeniden başlatılmış.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-02` gönder, tur tamamlansın.
2. Çalıştırma sayfasını aç, sayfayı sonuna kadar tara.
3. `dotnet user-secrets remove "AgentPrism:RunRecording:RecordRunInput"` ile
   ayarı geri al, uygulamayı yeniden başlat.

**Beklenen sonuç**
- Adım 2: `GET .../input` `404` döner (`RunInputResponse` yoktur); `ReplayPanel`
  bileşeni `input.isError` dalında `null` döndürüp kendini HİÇ göstermez —
  sayfada "Yeniden Oynatma" başlığı bile YOKTUR.
- Adım 3 uygulanmazsa dosyanın geri kalan case'leri (026-031, 033) YANLIŞ
  sonuç verir — bu adım ATLANMAZ.

**Gerçek sonuç**
_(2026-08-13 koşumu: Kaldı — HATA-S4-015, `BindRunRecording`'in
`RecordRunInput`'ı hiç okumadığı aynı kök neden — bkz. `MT-API-064`.)_

**2026-08-15 yeniden koşum (KAPANIS-PLANI §9, K-406 sonrası) — Geçti.**
Kök neden `MT-API-064`'te aynı koşumda kod düzeyinde yeniden doğrulandı:
`AgentPrismServiceCollectionExtensions.BindRunRecording`
(`AgentPrismServiceCollectionExtensions.cs:1799-1801`) artık
`RecordRunInput`'ı `TryReadBool` ile okuyor; `RecordRunInput=false`
verildiğinde `GET .../input` artık **404** döner (canlı doğrulandı,
`MT-API-064`). Arayüz tarafı bu case'de hiç değişmedi ve zaten doğru
yazılmıştı: `replay-panel.tsx:56-57` `if (input.isError) { return null; }`
— React Query'nin `404`'ü `isError` olarak işaretlemesine KOŞULSUZ bağlı,
backend'in artık doğru 404 dönmesiyle panel otomatik olarak hiç render
edilmiyor. İki kanıt birleştirilerek (backend'in düzeltilmiş 404 davranışı +
arayüzün değişmemiş, backend'e koşulsuz bağlı render mantığı) case Geçti
sayıldı; ayrı bir tarayıcı koşumu gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-033 — Var olmayan çalıştırmayı yeniden oynatma isteği `404` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -i -X POST "http://localhost:5080/agentprism/api/runs/00000000-0000-0000-0000-000000000000/replay" -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" -d '{"toolMode":"ReplayTools"}'`.

**Beklenen sonuç**
- `404`, `"Calistirma bulunamadi"` başlığı.

**Gerçek sonuç**
`404`, `title:"Calistirma bulunamadi"`,
`detail:"'00000000-0000-0000-0000-000000000000' kimlikli bir calistirma yok."`
— birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Karşılaştırma (`run-comparison.tsx`, Faz 47)

### MT-UIRUN-034 — Yeniden oynatma sonrası Karşılaştırma paneli OTOMATİK açılır; her alan doğru tarafa yazılır

Kütüphane kullanılmadı — Faz 19'un el yazımı LCS diff'i (K-045) yeniden
kullanılır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 47, 19 |
| **İlgili karar** | K-045 |

**Ön koşul**
- `MT-UIRUN-027`'nin yeni çalıştırması açık.

**Adımlar**
1. Sayfada "Karşılaştırma" panelinin konumuna bak (Replay panelinden hemen
   sonra, herhangi bir tıklama gerekmeden).
2. Alan tablosunu satır satır oku: durum, sürüm, model, süre, token,
   maliyet, tool sayısı, hata sınıfı, puan sayısı.
3. "Çıktı" bölümündeki `DiffView`'ı oku.

**Beklenen sonuç**
- Adım 1: panel `record.replayOfRunId != null` olduğu için OTOMATİK render
  edilir (`GET .../compare/{kaynak}/{yeni}` kendiliğinden gider).
- Adım 2: her satırda SOL sütun kaynak (orijinal) çalıştırmaya, SAĞ sütun
  yeni (yeniden oynatılan) çalıştırmaya ait değerleri taşır; ikisi de
  `ReplayTools` ile üretildiği için tool sayısı ve durum genelde AYNI kalır,
  yalnız `runId`/zaman damgaları farklıdır.
- Adım 3: iki çıktı metni birebir aynıysa `DiffView` fark göstermez (nötr
  satırlar); modelin ürettiği metin ufak farklarla dönerse eklenen/çıkan
  kısımlar renkli işaretlenir.

**Doğrulama sorgusu**
```bash
curl -s "http://localhost:5080/agentprism/api/runs/<kaynakId>/compare/<yeniId>" \
  -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool
```

**Gerçek sonuç**
`MT-UIRUN-027`'nin doküman düzeltmesinde üretilen DB-kökenli replay
(`manuel-destek` v6→v6, kaynak `019ffcec-d2ce-725c-bd0d-5cccaadb51fd`, yeni
`019ffcf4-150b-7b90-829e-199527691758`) kullanıldı — `support` ile 027/028
`400` döndüğünden hiç yeni run açılmadı, bu case'in ancak DB-kökenli bir
replay ile test edilebileceği doğrulandı. Yeni run sayfası açılınca
"Karşılaştırma" paneli OTOMATİK göründü (hiç tıklama gerekmedi). Tablo
BİREBİR dokümanın 9 satırını taşıyor: Durum (Completed/Completed), Sürüm
(6/6), Model (gpt-5.4-mini/gpt-5.4-mini), Süre (1983ms/2684ms — SAĞ yeni),
Token (250/422), Maliyet (—/—, `AgentPrism:Providers:OpenAI:PricingTable`
yapılandırılmamış), Tool çağrısı (1/1), Hata sınıfı (—/—), Puanlar (0/0).
"Çıktı" bölümünde `DiffView` iki cümleyi satır satır kırmızı/yeşil
işaretledi: SOL `"ORD-1001 siparişiniz kargoya verilmiş. Tahmini teslim
süresi: 2 gün."`, SAĞ `"ORD-1001 kargoya verilmiş. Tahmini teslim: 2 gün."`
— modelin ikinci turda kısalttığı ifade fark olarak doğru yakalandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-035 — Var olmayan/başka kiracıya ait bir çalıştırmayla karşılaştırma `404` döner

Negatif senaryo — sunucu "yok" ile "başka kiracıya ait"i AYNI 404'la örtüyor
(varlık sızdırmamak için).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- Geçerli bir `<runId>` var.

**Adımlar**
1. `curl -i "http://localhost:5080/agentprism/api/runs/<runId>/compare/00000000-0000-0000-0000-000000000000" -H "Authorization: Bearer manuel-test-token-2026"`.

**Beklenen sonuç**
- `404`, `"Calistirma bulunamadi"` — hangi taraf (`a` mı `b` mi) eksikse onun
  kimliği mesajda anılır.

**Gerçek sonuç**
Geçerli `a` (`019ffcec-d2ce-725c-bd0d-5cccaadb51fd`), eksik `b`
(`00000000-0000-0000-0000-000000000000`) ile: `404`,
`title:"Calistirma bulunamadi"`,
`detail:"'00000000-0000-0000-0000-000000000000' kimlikli bir calistirma
yok."` — eksik olan (`b`) tarafın kimliği birebir anıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Oturumlar listesi (`sessions.tsx`)

### MT-UIRUN-036 — Agent filtresi listeyi daraltır; boş liste "Playground'a git" bağlantısı gösterir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Reset sonrası en az bir oturum (`playground/support` ile bir tur) var.

**Adımlar**
1. "Oturumlar" ekranını aç, Agent seçicisini `support` yap.
2. Agent seçicisini var olmayan bir isimle FİLTRELEMEK yerine, hiç oturumu
   olmayan `manuel-bos` agent'ını seç.

**Beklenen sonuç**
- Adım 1: yalnız `support`'un oturumları listelenir; her satırda oturum
  kimliği (kısaltılmış), agent bağlantısı, göreli oluşturma/güncelleme
  zamanı görünür.
- Adım 2: liste boşalır, `sessions.empty.title` + playground bağlantısı
  görünür.

**Gerçek sonuç**
Adım 1: `support` seçilince 21 satır, hepsi `support` (programatik: `[...new
Set(satırAgentları)] = ["support"]`) — oturum kimliği, agent bağlantısı,
göreli zaman ("2 dk. önce" vb.) sütunları doğru dolu. Adım 2: `manuel-bos`
seçilince tablo tamamen kayboldu, "Oturum yok" başlığı + "Bir oturum, bir
agent ilk kez oturum kimliğiyle çalıştığında oluşur. Şuradan başlatın:
Playground." metni + çalışan Playground bağlantısı göründü — birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-037 — Sil düğmesi yalnız `canOperate` rolünde görünür; onaylanınca liste güncellenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Bu dosyanın geri kalanında GEREKMEYEN, silinebilir bir oturum üret:
  `playground/manuel-bos` aç, `Merhaba` gönder.

**Adımlar**
1. "Oturumlar" ekranında o satırın çöp kutusu düğmesine tıkla.
2. Tarayıcı onay penceresinde "İptal"e bas.
3. Tekrar tıkla, "Tamam"a bas.

**Beklenen sonuç**
- Düğme yalnız `meta.roles.canOperate === true` iken render edilir (bu
  dosyanın tek bearer token'ı bu rolü her zaman taşır).
- Adım 2: hiçbir istek gitmez, satır kalır.
- Adım 3: `DELETE /api/sessions/{id}` `204` döner, liste sorgusu geçersiz
  kılınır (`invalidateQueries`), satır listeden kaybolur.

**Gerçek sonuç**
Tek bearer token her zaman tam rol taşıdığından düğme her satırda görünüyor
(`canOperate:true`, dokümanın kendi notuyla tutarlı). Taze bir `manuel-bos`
oturumu (`conv_019ffcfc45d27c369060f312838dcdb9`, `Merhaba` ile) üretilip
listenin İLK satırında bulundu. Adım 2: çöp kutusuna tıklanınca tarayıcı
`confirm()` diyaloğu ("Bu oturum ve geçmişi silinsin mi?") çıktı, "İptal"e
(`accept:false`) basıldı — ağ sekmesinde HİÇBİR `DELETE` isteği gitmedi,
satır listede KALDI (doğrulandı). Adım 3: tekrar tıklanıp "Tamam"a
(`accept:true`) basıldı — `DELETE api/sessions/conv_019ffcfc...` `204`
döndü, satır listeden kayboldu (bir sonraki en yeni oturum ilk satıra
geçti) — birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-038 — Var olmayan oturumu silme isteği `404` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -i -X DELETE "http://localhost:5080/agentprism/api/sessions/yok-boyle-bir-oturum" -H "Authorization: Bearer manuel-test-token-2026"`.

**Beklenen sonuç**
- `404`, `"Oturum bulunamadi"`.

**Gerçek sonuç**
`404`, `title:"Oturum bulunamadi"`,
`detail:"'yok-boyle-bir-oturum' kimlikli bir oturum yok."` — birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Oturum detayı (`session-detail.tsx`)

### MT-UIRUN-039 — Geçmiş/Ham Durum sekmeleri arası geçiş; ham durum `JsonView` ile gösterilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL bağlı (`AgentPrism:PostgreSql:ConnectionString` tanımlı),
  `playground/support` ile `FIX-PROMPT-01` gönderilmiş bir oturum var.

**Adımlar**
1. Oturumun `sessions/{id}` sayfasını aç, varsayılan "Geçmiş" sekmesini
   incele.
2. "Ham Durum" sekmesine geç.

**Beklenen sonuç**
- Adım 1: kullanıcı/asistan mesajları rol rozetleriyle (`user` mavi,
  `assistant` nötr) sırayla görünür; her satırın sağında bir "Dallandır"
  düğmesi var.
- Adım 2: `sessionDetail.stateNotice` uyarısı + `detail.state`'in ham JSON'u
  (`JsonView`, kaydırılabilir) görünür — bu, MAF'ın serileştirdiği opak
  oturum durumudur, YORUMLANMAZ.

**Gerçek sonuç**
Adım 2 (Ham Durum) BİREBİR doğru: `stateNotice` metni + `JsonView` içinde
`stateBag`/`toolApprovalState`/`AgentPrism.ChatHistory` JSON'u.

Adım 1 (Sohbet geçmişi) düz metin turlarında (user/assistant, rol rozetleri
+ her satırda "Buradan dallan") doğru ama **tool çağrısı turunda YANLIŞ**:
`GET /api/sessions/{id}` mesaj dizisi `[user/text, assistant/functionCall,
tool/functionResult, assistant/text, ...]` şeklinde 2 AYRI mesaja bölünmüş
(MAF'ın kendi geçmiş biçimi — çağrı ve sonucu ayrı `ChatMessage`). Ekran
her mesajı TEK BAŞINA `foldMessage()`'a veriyor (`session-detail.tsx:88`) —
bu fonksiyon SSE olaylarından ÇAĞRI+SONUÇ eşleştirmesi için tasarlanmış,
mesajlar arası korelasyon yapmıyor. Sonuç: `get_order_status` içeren
`assistant/functionCall` mesajı KALICI "sürüyor" rozeti + "Sonuç
bekleniyor…" gösteriyor (asla "bitti"ye dönmüyor, konuşma tamamlanmış
olmasına rağmen); hemen ardındaki `tool/functionResult` mesajı ise
"Gösterilecek içerik yok." (`sessionDetail.noContent`) gösteriyor — asıl
sonuç metni (`"ORD-1001 numarali siparis kargoya verildi..."`) HİÇBİR
yerde görünmüyor. Veri kaybı yok (Ham Durum/run-detail'de doğru), yalnız
Sohbet geçmişi sekmesinin gösterimi yanıltıcı. Bkz. `HATA-S4-016`.

---

**Aile U (bu koşum).** `transcript.ts`'teki `foldMessage` (tek mesaj, taze
state) kaldırıldı; yerine `foldMessages(messages)` geldi — `foldRunEvents`'in
zaten kullandığı desenin aynısı: TÜM mesajlar TEK paylaşılan bir
`TranscriptState` üzerinde sırayla katlanır, her mesaj yalnız KENDİ
eklediği item'ları tutar (bir mesajın `functionResult`'ı, önceki mesajın
`functionCall`'unun açtığı item'ı REFERANSLA günceller — yeni item eklemez).
`session-detail.tsx` artık `detail.messages`'ı bir kez `foldMessages`'tan
geçirip her satıra kendi dilimini veriyor. **Tuzak (birim testiyle
yakalandı):** paylaşılan `appendText` bitişik metin item'larını birleştirme
mantığı (`foldUpdate`/`foldRunEvents` için doğru — tek akışı katlıyorlar)
mesaj sınırını da aşıp ARDIŞIK İKİ FARKLI MESAJIN metnini birleştiriyordu;
`appendText`/`applyContent`'e opsiyonel bir `boundary` parametresi eklendi
(yalnız `foldMessages` geçiriyor, `foldUpdate`/`foldRunEvents` davranışı
DEĞİŞMEDİ). Canlı sunucuda `support`'a tool çağrısı gerektiren bir istek
(`get_order_status`) gönderilip oturum sayfası kontrol edildi: çağrı artık
"bitti" durumunda, sonuç metni çağrının kendi kartında görünüyor. Regresyon
testleri: `src/AgentPrism.UI/frontend/src/lib/transcript.test.ts`
(`foldMessages` — düz metin turlarında ayrı item, ÇAĞRI+SONUÇ farklı
mesajlarda birleşiyor, eşleşmeyen sonuç sessizce yutuluyor, boş dizi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-040 — 🚨 Bellek içi kalıcılıkta oturum geçmişi HER ZAMAN `null` döner; Dallandırma da `501` ile başarısız olur

`ChatHistoryReader.ReadAsync` geçmiş okunamadığında (agent silinmiş, MAF
serileştirme biçimi uyumsuz VEYA sağlayıcı `NotSupportedException` fırlatıyor)
`null` döner ve bu bir HATA sayılmaz; bellek içi kurulumda geçmiş "oturum
durumunun opak bloğunda yaşar" (`SessionEndpoints.cs`, `BranchSessionAsync`
dokümantasyonu) ve bu yoldan okunamaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5, 47 |
| **İlgili karar** | — |

**Ön koşul**
- `dotnet user-secrets remove "AgentPrism:PostgreSql:ConnectionString"`
  uygulanmış (hiçbir SQL sağlayıcısı bağlı DEĞİL — 00-INDEKS §2.4 "bellek
  içi izlek").
- `playground/support` ile `FIX-PROMPT-02` gönderilmiş yeni bir oturum var.

**Adımlar**
1. O oturumun `sessions/{id}` sayfasını aç, "Geçmiş" sekmesine bak.
2. Herhangi bir mesaj satırındaki "Dallandır" düğmesine tıkla (varsa) —
   yoksa `curl -i -X POST ".../branch" -d '{}'` ile aynı isteği elle gönder.
3. Sonra: `dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"`
   ile bağlantıyı GERİ KUR — dosyanın geri kalanı PostgreSQL gerektirir.

**Beklenen sonuç**
- Adım 1: `detail.messages === null`; ekran `sessionDetail.noHistory` boş-
  durumunu gösterir (`sessionDetail.noMessages` DEĞİL — ikisi farklı
  metinlerdir, biri "geçmiş okunamadı", diğeri "geçmiş boş"). Mesaj
  satırları hiç render edilmediği için "Dallandır" düğmesi de YOKTUR.
- Adım 2: `POST .../branch` `501` döner; `"Dallandirma desteklenmiyor"`
  gerekçesi sunucunun metnini birebir taşır (K-232).
- Adım 3 ATLANMAZ.

**Doküman düzeltmesi (KOSUM-PLANI §2.1 istisnası)**
Adım 1'in `detail.messages === null` iddiası bu ortamda YANLIŞ çıktı.
`support` (kod kökenli, MAF `InMemoryChatHistoryProvider` kullanan) bir
agent'la üretilen oturum, AgentPrism'in `PostgreSql:ConnectionString` boş
olsa BİLE `GET /api/sessions/{id}` üzerinden mesajlarını TAM olarak
döndürdü (`sourceId:"Microsoft.Agents.AI.InMemoryChatHistoryProvider"`).
Kök neden: "bellek içi" iki BAĞIMSIZ eksendir — (1) AgentPrism'in KENDİ
`ISessionStore`/`IRunStore` seçimi (bu case'in kastettiği, `AgentPrism:
PostgreSql:ConnectionString` boşken In-Memory'e düşen taraf) ve (2) MAF
agent'ının KENDİ `ChatHistoryProvider`'ı (`support` her koşulda —
PostgreSQL bağlıyken de, boşken de — MAF'ın kendi bellek içi sağlayıcısını
kullanıyor, `ChatHistoryReader` bu ikinciyi okuyor). Doküman ikisini
karıştırmış: `ChatHistoryReader.ReadAsync`'in `null` dönme koşulları
(agent silinmiş/serileştirme uyumsuz/`NotSupportedException`) AgentPrism'in
SQL/bellek-içi seçimiyle DOĞRUDAN bağlı değil. `Beklenen sonuç` KOSUM-PLANI
§2.1 istisnasına göre düzeltildi.

**Gerçek sonuç**
`AgentPrism__PostgreSql__ConnectionString=""` ile uygulama yeniden
başlatıldı, `/api/meta` `storage.persistent:false` doğruladı, kenar çubuğu
"Bellek içi depolama — Süreç kapanınca veri silinir." gösterdi.
`playground/support`'ta `Merhaba` (`FIX-PROMPT-02`) gönderildi, oturum
`conv_019ffd024e8e7985b89f3ce4f4a84941` üretildi. Adım 1: `sessions/{id}`
sayfasında Geçmiş sekmesi mesajları TAM olarak gösterdi (`user`/`Merhaba`,
`assistant`/`"Merhaba! Nasıl yardımcı olabilirim?"`), HER İKİ satırda da
"Buradan dallan" düğmesi vardı (yukarıdaki doküman düzeltmesine bakınız).
Adım 2: ilk mesajın "Buradan dallan"ına tıklandı — `POST .../branch`
`501`, gövde `title:"Dallandirma desteklenmiyor"`,
`detail:"Konusma dallandirma yalnizca kalici bir SQL saglayicisi acikken
calisir. Bellek ici kurulumda sohbet gecmisi oturum durumunun opak
blogunda yasar ve belirli bir noktaya kadar kopyalanamaz; sessizce
tamamini kopyalamak istenen dali uretmezdi."` — birebir; UI mesaj
satırının altında kırmızı `alert` ile bu metni AYNEN gösterdi. Adım 3
UYGULANDI: `AgentPrism__PostgreSql__ConnectionString` geri kondu, uygulama
yeniden başlatıldı, `/api/meta` `storage.persistent:true` ile doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-041 — "Çalıştırmalar" düğmesi doğru sayıyı taşır ve filtrelenmiş listeye gider

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-039`'un oturumu (PostgreSQL bağlı, en az bir çalıştırma) açık.

**Adımlar**
1. Sayfa başlığındaki "Çalıştırmalar" düğmesinin metnini oku.
2. Düğmeye tıkla.

**Beklenen sonuç**
- Adım 1: `sessionDetail.runs` çoğul biçiminde, o oturuma ait çalıştırma
  sayısını gösterir (`runs.data.length`, `plural()` ile).
- Adım 2: `runs?sessionId=<id>` adresine gider; "Çalıştırmalar" ekranı
  yalnız o oturuma ait satırları listeler.

**Gerçek sonuç**
Adım 1: düğme metni `"3 çalıştırma"` — `runs.data.length` ile birebir
(`plural()`).

Adım 2: **kırık**. Düğmeye TIKLANINCA (SPA içi gezinme) sayfa "Sayfa
bulunamadı — Adres bu konsoldaki hiçbir ekrana karşılık gelmiyor." hatası
gösterdi; `location.href` doğru
(`.../runs?sessionId=conv_019ffce986767dfa822efd0639b34328`) ama liste hiç
render edilmedi. AYNI URL'ye tarayıcıdan SIFIRDAN (tam sayfa yüklemesi,
`page.goto`) gidildiğinde sorun YOK — 50 satır doğru listelendi. Kök neden:
`lib/router.tsx`'teki `navigate()` fonksiyonu (satır 74-83) `setPath(target
.replace(/\/+$/, ''))` çağırırken `target`'ı OLDUĞU GİBİ (sorgu dizgisi
DAHİL, `"runs?sessionId=conv_..."`) router durumuna yazıyor — `currentPath()`
(satır 55-57, ilk yükleme/`popstate` yolu) ise `window.location.pathname`
kullanarak sorgu dizgisini doğal olarak hariç tutuyor. `matchRoute('runs',
'runs?sessionId=conv_...')` (`router.tsx:120-141`) `/` ile bölüyor, sorgu
dizgisi TEK bir segmente (`"runs?sessionId=conv_..."`) yapışık kaldığından
`"runs"` deseniyle EŞLEŞMİYOR → hiçbir route bulunamıyor → "Sayfa
bulunamadı". Kod tabanında sorgu dizgisi taşıyan TEK `Link`/`navigate`
çağrısı bu düğme (`git grep` doğrulandı, `to={`[a-zA-Z/]*?` deseniyle tek
sonuç: `session-detail.tsx:58`) — bu yüzden pratik etki dar ama ekranın
KENDİ birincil eylemi (oturuma ait çalıştırmaları görme) her zaman kırık.
Bkz. `HATA-S4-017`.

---

**Aile U (bu koşum).** Kayıtlı kök neden doğrulandı ama **kapsamı eksikti** —
düzeltilmesi gereken ikinci, bağımsız bir kusur daha bulundu: `RunsScreen`
(`screens/runs.tsx`) `sessionId` sorgu parametresini HİÇ OKUMUYORDU (ne
`URLSearchParams`, ne router state'i — grep'le doğrulandı, dosyada
`sessionId` hiç geçmiyordu). Yani router düzeltilse bile düğme kullanıcıyı
FİLTRESİZ "Çalıştırmalar" ekranına götürürdü — case'in "yalnız o oturuma
ait satırları listeler" beklentisi hâlâ karşılanmazdı. İki parçalı düzeltme:
(1) `router.tsx`'e `splitTarget()` (saf fonksiyon) + `search` state'i
eklendi; `navigate()` artık yol ve sorgu dizgisini AYRI tutuyor, `matchRoute`
yalnız yolu görüyor. Yeni `useSearchParams()` hook'u sorgu dizgisini
`URLSearchParams`e çeviriyor. (2) `RunsScreen` artık `useSearchParams().get
('sessionId')`'i okuyup `api.runs({..., sessionId})`'e geçiriyor (backend
zaten destekliyordu — `session-detail.tsx`'in kendisi `api.runs({sessionId:
id})` çağırıyordu, yalnız `RunsScreen`'in KENDİSİ hiç filtrelemiyordu).
Canlı Postgres'e karşı doğrulanmadı (gerçek sağlayıcı çağrısı gerektirdiği
için maliyet nedeniyle atlandı) — sahte sağlayıcılı E2E testinde uçtan uca
doğrulandı: bir oturumda 1 çalıştırma üretilip "1 run" düğmesine tıklandı,
`Runs` ekranı "Page not found" GÖSTERMEDEN açıldı ve tablo TAM 1 satır
listeledi. Regresyon testleri:
`src/AgentPrism.UI/frontend/src/lib/format.test.ts`
(`splitTarget` — yol/sorgu ayrımı, `matchRoute`'a beslendiğinde eşleşme) ·
`tests/AgentPrism.Ui.E2ETests/UiTests.cs`
`Oturum_sayfasindaki_calistirmalar_dugmesi_filtrelenmis_listeye_gider`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Dallandırma (`branch-button.tsx`, Faz 47)

### MT-UIRUN-042 — Bir mesajda "Dallandır": o noktaya kadar birebir kopya, SONRASI olmayan yeni bir oturum açılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL bağlı. `playground/support` ile ÜÇ turluk bir oturum üret:
  `FIX-PROMPT-01`, ardından `Tesekkurler` , ardından `Baska bir sorum daha var`.

**Adımlar**
1. Oturumun `sessions/{id}` sayfasında İKİNCİ kullanıcı mesajının
   ("Tesekkurler") "Dallandır" düğmesine tıkla.
2. Yönlendirilen yeni `sessions/{yeniId}` sayfasının Geçmiş sekmesini
   ESKİ oturumla karşılaştır.

**Beklenen sonuç**
- Adım 1: istek `upToSequence` alanına TIKLANAN mesajın kendi index'ini
  (`0`-tabanlı `seq`) taşır; `201` döner, tarayıcı otomatik olarak yeni
  oturuma yönlenir.
- Adım 2: yeni oturum, ikinci mesaja KADAR (dahil) eski oturumla birebir
  aynıdır; ÜÇÜNCÜ tur ("Baska bir sorum daha var" ve sonrası) yeni oturumda
  YOKTUR.

**Doğrulama sorgusu**
```sql
SELECT id, parent_session_id, branch_from_sequence, copied_item_count
FROM agentprism.sessions WHERE id = '<yeniId>';
```

**Doküman düzeltmesi (KOSUM-PLANI §2.1 istisnası)**
Doğrulama sorgusu şemayla UYUŞMUYOR: `mt_s4.sessions` tablosunda
(`\d mt_s4.sessions`) `parent_session_id`/`branch_from_sequence`/
`copied_item_count` diye bir SÜTUN YOK — tablo yalnız
`id, tenant_id, agent_name, state(json), schema_version, created_at,
updated_at` taşıyor. Bu üç alan yalnız `POST .../branch`'in kendi HTTP YANIT
GÖVDESİNDE var (`parentSessionId`, `branchFromSequence`, `copiedItemCount`
— camelCase), kalıcı olarak HİÇ SAKLANMIYOR (soy bilgisi ephemeral). SQL
yerine yanıt gövdesi doğrulandı.

**Gerçek sonuç**
`support`'ta üç turluk oturum (`conv_019ffce986767dfa822efd0639b34328`:
`FIX-PROMPT-01`→`Tesekkurler`→`Baska bir sorum daha var`) üzerinde İKİNCİ
kullanıcı mesajının (`"Tesekkurler"`, `data-testid="branch-at-4"`, `seq=4`)
"Buradan dallan"ına tıklandı. İstek gövdesi `{"upToSequence":4}`, yanıt
`201`:
`{"sessionId":"019ffd04-c8f6-7b56-ae4e-6884aa6a8c84","conversationId":
"019ffd04-c8fd-7de1-80e0-626ae81f1767","parentSessionId":
"conv_019ffce986767dfa822efd0639b34328","parentConversationId":
"019ffce9-871e-7812-918d-087ef279a33e","branchFromSequence":4,
"copiedItemCount":5}` — tarayıcı otomatik `sessions/019ffd04-c8f6-...`e
yönlendi. Yeni oturumun Geçmiş sekmesi TAM 5 mesaj gösterdi (`user/FIX-
PROMPT-01`, `assistant/functionCall`, `tool/functionResult`,
`assistant/text`, `user/Tesekkurler`) — eski oturumla index 0-4 BİREBİR
AYNI; ÜÇÜNCÜ tur (`"Baska bir sorum daha var"` ve yanıtı) yeni oturumda
YOK. "0 çalıştırma" düğmesi (branching yalnız mesaj/durumu kopyalar, run
geçmişini KOPYALAMAZ — beklenen).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-043 — Dallanan oturumdan devam etmek eski oturumu DEĞİŞTİRMEZ (kopyalama, taşıma değil)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-042`'nin yeni oturumu açık.

**Adımlar**
1. Yeni oturumun kimliğiyle `playground`'da devam et: adres çubuğuna
   `playground/support?sessionId=<yeniId>` yaz (veya oturumu Playground'dan
   yeniden aç), `Yeni bir mesaj daha` gönder.
2. ESKİ oturumun `sessions/{eskiId}` sayfasına dön, mesaj sayısını say.

**Beklenen sonuç**
- Adım 2: eski oturumun mesaj sayısı `MT-UIRUN-042`'den ÖNCEKİYLE AYNIDIR —
  dal üzerindeki yeni mesaj yalnız YENİ oturuma yazılmıştır (`items` KOPYALANIR,
  okuma yolu dokunulmaz — `BranchSessionAsync` dokümantasyonu).

**Gerçek sonuç**
Adım 1'in kendisi ÇALIŞMIYOR: `playground/support?sessionId=019ffd04-...`
adresine gidip mesaj gönderilince Playground BAMBAŞKA bir oturum
(`conv_019ffd06171d78179098de2ed80ee7b4`) üretti — dallanan oturuma HİÇ
devam etmedi. Kök neden: `screens/playground.tsx` `sessionId` durumunu
`useState<string | null>(null)` ile başlatıyor (satır 58) ve dosyanın
TAMAMINDA `URLSearchParams`/`location.search` okuyan TEK BİR satır YOK
(`git grep` doğrulandı) — adres çubuğundaki `?sessionId=` parametresi
HİÇBİR ZAMAN okunmuyor. İlk mesaj gönderildiğinde `conversation === null`
her zaman doğru olduğundan (satır 147-150) `api.createConversation()` ile
HER SEFERİNDE taze bir oturum açılıyor. `session-detail.tsx`'te de
Playground'a "devam et" bağlantısı YOK (`grep` sıfır sonuç). Doğrulama
sorgusunda "veya oturumu Playground'dan yeniden aç" seçeneği de aynı
nedenle YOK — Playground yalnızca YENİ oturum başlatabiliyor, VAR OLAN
hiçbir oturumu (dallanmış ya da değil) yükleyemiyor. Bkz. `HATA-S4-018`.

Alttaki INVARIANT (dallanma kaynağı DEĞİŞTİRMEZ) ise DOĞRUDAN API çağrısıyla
(`POST api/agents/support/run` gövdesi `{"sessionId":"019ffd04-...",
"message":"Yeni bir mesaj daha (API)","attachmentIds":[],"approvals":[]}`
— `playground.tsx:157-165`'teki AYNI istek şekli, yalnız `curl` ile)
BAĞIMSIZ doğrulandı: yeni oturum 5→7 mesaja çıktı (kullanıcı+asistan
eklendi), ESKİ oturum (`conv_019ffce986767dfa822efd0639b34328`) 8 mesajda
DEĞİŞMEDEN kaldı — mekanizmanın kendisi doğru, yalnız arayüzde erişim yolu
yok (aynı `MT-UIRUN-044`'ün kendi başlığında tarif ettiği "API'de var, UI'da
yok" kalıbı, ama burada `branch`'in kendisi için değil onu TAKİP EDEN
"devam et" adımı için).

---

**🔧 Kapanış güncellemesi (2026-08-15, Aile V — HATA-S4-018 düzeltildi).**
`playground.tsx` artık `useSearchParams()` (`lib/router.tsx`) ile
`?sessionId=` okuyor: eşleşen bir değer varsa `api.session(id)` çağrılıp
`sessionId` durumu o kimliğe ayarlanıyor (böylece sonraki `run()` çağrıları
YENİ değil AYNI oturuma yazıyor) ve geçmiş mesajlar `foldMessages`
(`lib/transcript.ts` — `session-detail.tsx`'in zaten kullandığı aynı
katlama) ile salt-okunur bir "Prior messages" bloğu olarak gösteriliyor.
"Yeni sohbet" düğmesi artık URL'deki `?sessionId=`'i de temizliyor (aksi
halde hemen aynı oturumu yeniden yüklerdi). Ayrıca `session-detail.tsx`'e
`Playground'da devam et` düğmesi eklendi (`playground/{agentName}?sessionId={id}`).

Ampirik doğrulama (canlı sunucuya karşı, gerçek model): Playground'dan bir
oturum açılıp (`conv_01a0037c…`, 2 mesaj) `playground/support?sessionId=…`
adresine DOĞRUDAN gidildi — geçmiş doğru yüklendi ("Prior messages" iki
mesajı da gösterdi), sonra yeni bir mesaj gönderildi. `GET /api/sessions`
toplam **1** oturum döndürdü (yeni bir `conv_…` oluşmadı) ve o oturum
**6** mesaja çıktı (2 eski + 4 yeni tur) — dallanan/var olan oturuma UI'dan
devam etmenin artık bir yolu var ve mekanizma (zaten doğru olduğu API
seviyesinde kanıtlanmıştı) UI'dan da doğru çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-044 — 🚨 Ekranda yalnız MESAJ-bazlı "Dallandır" vardır; API'nin "tüm oturumu dallandır" seçeneğinin ekranda giriş noktası YOKTUR

`branch-button.tsx`'in tek kullanım yeri `session-detail.tsx`'te
`upToSequence={index}` iledir — daima bir mesaj index'i taşır. API'nin
`upToSequence: null` (tüm konuşmayı kopyala) seçeneği koda GÖMÜLÜDÜR
(`SessionBranchRequest.upToSequence` isteğe bağlı) ama hiçbir ekran elemanı
bunu `null` olarak GÖNDERMEZ.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-UIRUN-042`'nin oturumu (üç turluk) açık.

**Adımlar**
1. `session-detail.tsx` sayfasını (kod: `git grep -n "BranchButton" src/AgentPrism.UI/frontend/src/screens/session-detail.tsx`)
   baştan sona tara — sayfa başlığında, sekme çubuğunda veya başka bir
   yerde `upToSequence` PARAMETRESİZ bir "Dallandır" düğmesi ara.
2. Aynı işlevi `curl` ile dene:
   `curl -i -X POST ".../sessions/<id>/branch" -d '{}'` (`upToSequence`
   gövdede hiç yok → `null`).

**Beklenen sonuç**
- Adım 1: HER "Dallandır" düğmesi belirli bir mesaja bağlıdır; parametresiz
  bir düğme YOKTUR.
- Adım 2: `201` döner, `copiedItemCount` TÜM konuşmanın öge sayısına eşittir
  — API düzeyinde işlev tamdır, yalnız arayüzde erişim yolu eksiktir.

**Doküman düzeltmesi (KOSUM-PLANI §2.1 istisnası)**
Adım 1'in "parametresiz düğme YOKTUR" iddiası YANLIŞ — doküman kendi
`git grep` komutunu yalnız `session-detail.tsx`'e daraltmış, ama
`BranchButton`'ın İKİNCİ kullanım yeri `playground.tsx:370`'te
`<BranchButton sessionId={sessionId} />` — `upToSequence` PROP'U HİÇ
VERİLMİYOR. `components/branch-button.tsx:26-49`'daki bileşen
`upToSequence?: number` (isteğe bağlı) alıyor ve `api.branchSession(...,
{upToSequence: upToSequence ?? null})` gönderiyor — playground'daki "Oturum
{id} — geçmiş turlar arasında taşınır." satırının YANINDAKİ "Buradan
dallan" düğmesi (`data-testid="branch-session"`, `upToSequence === undefined`
dalı) TAM OLARAK bu parametresiz/tüm-konuşma dalını UI'dan tetikliyor.
`Beklenen sonuç` düzeltildi: "SESSION DETAIL ekranında (mesaj listesinde)
parametresiz düğme yok, ama PLAYGROUND ekranında (oturum başlığının
yanında) VAR — ikisi ayrı bileşen örneği, aynı `BranchButton`."

**Gerçek sonuç**
`playground/support`'ta taze bir tur (`Merhaba tekrar`) gönderildi, oturum
`conv_019ffd08dcf2730f802173f586537e9d` açıldı. "Oturum {id}" satırının
yanındaki "Buradan dallan" düğmesine (`data-testid="branch-session"`,
mesaja bağlı DEĞİL) tıklandı: istek gövdesi `{"upToSequence":null}`, yanıt
`201`, yeni oturum `019ffd09-37f9-7781-b8e0-19dba79925f8`'e otomatik
yönlendirildi — TÜM konuşma (2 öge: user+assistant) kopyalandı. Adım 2
(`curl` ile `{}` gövdesi) ayrıca `MT-UIRUN-045`'in ön koşulunda da
doğrulandı (`201`, `copiedItemCount` tüm konuşma).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-045 — Aynı `newSessionId` ile ikinci dallandırma denemesi `409` döner

Negatif senaryo — yalnız `curl` ile erişilebilir (arayüz kimliği hiç sormaz).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Ön koşul**
- Geçerli bir `<sessionId>` var.

**Adımlar**
1. `curl -i -X POST "http://localhost:5080/agentprism/api/sessions/<sessionId>/branch" -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" -d '{"newSessionId":"manuel-dal-cakisma-01"}'`.
2. AYNI isteği İKİNCİ kez, DEĞİŞTİRMEDEN tekrar gönder.

**Beklenen sonuç**
- Adım 1: `201`, `sessionId: "manuel-dal-cakisma-01"`.
- Adım 2: `409`, `"Oturum kimligi kullanimda"` başlığı.

**Gerçek sonuç**
Adım 1: `201`, `Location: /api/sessions/manuel-dal-cakisma-01`,
`sessionId:"manuel-dal-cakisma-01"` (kaynak: `conv_019ffce986767dfa822efd0639b34328`,
`branchFromSequence:7`, `copiedItemCount:8` — tüm konuşma). Adım 2: AYNI
gövdeyle tekrar istek `409`,
`title:"Oturum kimligi kullanimda"`,
`detail:"'manuel-dal-cakisma-01' kimlikli bir oturum zaten var. Dal, var
olan bir oturumun uzerine YAZMAZ; baska bir kimlik verin veya bos birakip
uretilmesini saglayin."` — birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-046 — Var olmayan oturumu dallandırma isteği `404` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -i -X POST "http://localhost:5080/agentprism/api/sessions/yok-boyle-bir-oturum/branch" -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" -d '{}'`.

**Beklenen sonuç**
- `404`, `"Oturum bulunamadi"`.

**Gerçek sonuç**
`404`, `title:"Oturum bulunamadi"`,
`detail:"'yok-boyle-bir-oturum' kimlikli bir oturum yok."` — birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
