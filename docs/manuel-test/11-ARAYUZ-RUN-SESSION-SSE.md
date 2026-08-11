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
- İstatistik şeridi HİÇ görünmez (`stats.isSuccess` içi boş sayılarla da doğru
  ama liste tarafı `Empty` bileşenini gösterir).
- `runs.empty.title` başlığı ve içinde `playground` rotasına giden bir bağlantı
  görünür.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Adım 3: gövde `CodeBlock` içinde biçimlendirilmiş JSON olarak görünür
  (`prettyJson`), tool adı da satırın kendisinde rozet olarak tekrarlanır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Transkript panelinde bu olaya karşılık gelen HİÇBİR kart/metin YOKTUR —
  panel ya boştur ya da yalnız engellemeden ÖNCEKİ içeriği taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Adım 3: `stats.data.awaitingInputRuns > 0` olduğu için dördüncü kutu artık
  `errorRate` DEĞİL, `awaitingInputRuns` sayacını ve `stat.awaitingHint`
  ipucunu gösterir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
  ATILMAZ (`trace.isPending` her zaman `false` kalır, ağ sekmesinde
  `.../trace` isteği YOK).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---
