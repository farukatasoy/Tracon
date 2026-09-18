# 11 — Arayüz: Çalıştırma, Oturum ve SSE (`UIRUN`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../11-ARAYUZ-RUN-SESSION-SSE.md`](../../11-ARAYUZ-RUN-SESSION-SSE.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (şerit ap-s3, aile 21'in hemen
> ardından) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## HATA-S3-005 — Bağlantı sessizce koparsa çalıştırma ekranı sonsuza dek "Waiting for events…" yazısında donuk kalır; kullanıcıya hiçbir hata gösterilmez

- **Case:** MT-UIRUN-019
- **Önem:** Yüksek
- **İzlek:** B (gerçek tarayıcı, CDP `Network.emulateNetworkConditions`)
- **Ortam:** macOS arm64 · Chromium 152 (Playwright) · `mt_s3`

**Beklenen**
Case'in kendi beklentisi: bağlantı ortasında koparsa istemci bir `ErrorNote`
gösterir (`fetch` hatası yakalanır), döner simge donuk kalır — kullanıcı
bağlantının koptuğunu ANLAR.

**Gerçekleşen**
Akan bir çalıştırmanın sayfasında (`runs/{id}`) tarayıcı çevrimdışına
alındı (`page.context().setOffline(true)` — DevTools'un "Offline" onay
kutusuyla AYNI CDP çağrısı). **23 saniye** boyunca Transkript paneli
"Waiting for events…" yazısında hareketsiz kaldı; hiçbir `ErrorNote`,
hiçbir görsel değişiklik belirmedi. Ağ sekmesindeki `.../events` isteği
hâlâ `200 OK` işaretliydi (asla `failed` olmadı). Kontrol: aynı sayfada
YENİ bir istek (`fetch('/api/diagnostics')`) `"Failed to fetch"` ile
GERÇEKTEN reddedildi — yani CDP "offline" doğru çalışıyordu, yalnız
ZATEN AÇIK olan `chunked` SSE gövdesini kesmiyordu.

**Kök neden**
`src/Tracon.UI/frontend/src/screens/run-detail.tsx:184-210`'daki
`useEffect`: `for await (const frame of readSse(response)) { ... }` —
bu döngü sıradaki `chunk`'ı BEKLERKEN hiçbir zaman aşımı/`heartbeat`
denetimi TAŞIMIYOR. Yalnız alttaki `fetch` stream'i GERÇEKTEN bir hata
fırlatırsa (`catch (caught) { setError(caught) }`, satır 202-205) devreye
giriyor. Tarayıcı "offline" olduğunda YENİ bağlantılar reddedilir ama
ZATEN açık bir HTTP/1.1 `chunked` gövdenin okuyucusu (`reader.read()`)
yalnız SESSİZCE askıda kalır — hiçbir istisna fırlamaz, `for await` sonsuza
dek beklemeye devam eder. Sunucu tarafında `RunEventPollInterval` aralığında
gönderilen `: waiting\n\n` keep-alive yorumları da istemciye HİÇ ULAŞMIYOR
(bağlantı fiilen tek yönlü kopmuş), bu yüzden istemcinin kendi zaman aşımı
YOKSA hiçbir sinyal kalmıyor.

**Etki**
Kullanıcı "hâlâ çalışıyor" ile "bağlantı sessizce öldü" durumlarını AYIRT
EDEMEZ — ekranda ikisi de birebir aynı görünür (dönen nokta + "Waiting for
events…"). Tek çıkış yolu elle F5 yapmaktır (`MT-UIRUN-019`'un kendi
adım 3-4'ünün doğruladığı gibi, F5 sonrası akış TAM ve kayıpsız yeniden
kurulur — veri kaybı yok, yalnız GÖZLEMLENEBİLİRLİK boşluğu).

**Yeniden üretme**
1. `Prefer: respond-async` ile uzun süren (~30+ sn) bir run başlat.
2. `runs/{id}` sayfasını aç, akış sürerken bekle.
3. `page.context().setOffline(true)` (ya da DevTools → Network →
   Throttling → Offline).
4. 20+ saniye bekle: Transkript paneli "Waiting for events…"de donuk kalır,
   `ErrorNote` HİÇ belirmez.

**Kayıt:** MT-UIRUN-019 (bkz. koşum kaydı aşağıda).

---

## HATA-S3-006 — `RecordReasoningDeltas=true` iken model GERÇEKTEN düşünme içeriği üretse bile `ReasoningDelta` olayı HİÇ kaydedilmiyor

- **Case:** MT-UIRUN-047/048
- **Önem:** Yüksek
- **İzlek:** A (gerçek Anthropic çağrısı, `claude-haiku-4-5-20251001`, extended thinking)
- **Ortam:** macOS arm64 · net10 · PostgreSQL `mt_s3` · Anthropic (gerçek API)

**Beklenen**
`Tracon:RunRecording:RecordReasoningDeltas=true` (örnek uygulamanın kendi
varsayılanı) iken, model gerçekten düşünme içeriği ürettiğinde, kayıtlı
olay akışında (`GET /api/runs/{id}/events`) ayrı `ReasoningDelta` olayları
görünmeli (K-493, Faz 70) — `MT-UIRUN-048`'in kendi spesifikasyonu bunu
2026-08-19'da GERÇEKTEN ölçmüş (7 `ReasoningDelta` olayı kaydedilmiş).

**Gerçekleşen**
`RecordReasoningDeltas=true` ile DÖRT ayrı gerçek Anthropic çağrısı
denendi (`claude-thinking` agent'ı, `anthropic.thinking.budgetTokens:
2048`) — biri akışsız (`Idempotency-Key`), üçü akışlı. DÖRDÜNDE DE ham
yanıt/akış GERÇEKTEN `$type:"reasoning"` içerikli bir öge taşıyordu
(akışlı denemelerde 48-76 arası `reasoning` parçası SSE'de doğrudan
gözlendi; akışsız denemede `response.messages[0].contents` içinde açıkça
bir `reasoning` tipli öge vardı). DÖRDÜNDE DE kayıtlı `GET .../events`
**SIFIR** `ReasoningDelta` olayı döndü — yalnız `MessageDelta`/
`RunStarted`/`RunCompleted`. Aynı options nesnesinin KARDEŞ alanı
(`RecordMessageDeltas`) AYNI çalıştırmalarda doğru çalışıyor
(`MessageDelta` olayları GERÇEKTEN kaydediliyor) — bu, genel bir options-
bağlama sorununu EKARTE EDİYOR.

**Kök neden — kısmen izole edildi**
`src/Tracon.Core/Recording/RunRecordingAgent.Persistence.cs:189-193`'teki
`case TextReasoningContent reasoning when _options.RecordReasoningDeltas
...` ifadesinin KENDİSİ doğru: bağımsız bir reflection probu (bu turda
koşuldu) `Microsoft.Extensions.AI.Abstractions` 10.9.0'da `Text
ReasoningContent`'in JSON ayırt edicisinin tam olarak `"reasoning"`
olduğunu doğruladı — gözlenen ham JSON'la birebir eşleşiyor, tip
uyuşmazlığı YOK. `tests/Tracon.Core.UnitTests/Recording/
ReasoningRecordingTests.cs` bu switch dalını zaten sentetik
`TextReasoningContent` örnekleriyle kilitliyor (birim testi muhtemelen
YEŞİL — bu turda ayrıca koşulmadı ama kod donuk, testin kendisi
değişmedi). Akışlı/akışsız iki çağrı yolu da (`RunRecordingAgent.cs:238`
`message.Contents` VE `RunRecordingAgent.cs:458` `update.Contents`) AYNI
sonucu (sıfır kayıt) verdi — bu, sorunun akışlı/akışsız ayrımına ÖZGÜ
OLMADIĞINI gösteriyor. Sorunun tam kesişim noktası (gerçek bir MAF/
Anthropic yanıtının `response.Messages[].Contents`'e ulaşana kadar geçtiği
ara katmanlardan hangisinin `reasoning` ögesini süzdüğü ya da neden
`WriteContentsAsync`'e hiç ulaşmadığı) bu turda TAM izole EDİLEMEDİ —
birim testinin sentetik girdisiyle gerçek uçtan-uca yol arasındaki fark
araştırılmalı.

**Etki**
`RecordReasoningDeltas` özelliği reklam ettiği hiçbir şeyi YAPMIYOR —
açık olsa da kapalı olsa da SONUÇ AYNI (sıfır kayıt); yalnız `false`
durumunda bu doğru (kasıtlı), `true` durumunda YANLIŞ (sessiz veri kaybı).
K-493'ün gözlemlenebilirlik vaadi bu ortamda tutmuyor — bir kullanıcı
`RecordReasoningDeltas=true` yapıp modelin düşünme sürecini denetlemeyi
beklerse hiçbir kayıt bulamaz.

**Yeniden üretme**
1. `Tracon:RunRecording:RecordReasoningDeltas=true` (varsayılan).
2. Extended-thinking destekleyen bir agent'a (`claude-thinking`,
   `anthropic.thinking.budgetTokens` ayarlı) gerçek bir Anthropic
   çağrısı yap — akışlı VEYA akışsız, fark etmiyor.
3. Ham yanıtta/akışta `$type:"reasoning"` ögesi olduğunu doğrula.
4. `GET /api/runs/{id}/events` → `ReasoningDelta` sayısı `0`.

**Kayıt:** MT-UIRUN-047/048 (bkz. koşum kaydı aşağıda) — 048'in kendi
Beklenen sonucu 2026-08-19'da GERÇEKTEN kaydedilmiş 7 olay ölçmüştü; bu
turda AYNI davranış ASLA gözlemlenemedi, muhtemel bir gerileme (regresyon).

---

## Ortam notu (oturum başı)

- **`MT-UIRUN-001`'in "reset sonrası boş liste" ön koşulu bu oturumda
  SAĞLANAMADI.** `mt_s3` şemasını sıfırlamak (`DROP SCHEMA ... CASCADE`)
  gerekiyordu; bu komut Claude Code'un auto-mode sınıflandırıcısı
  tarafından "Cloud Storage Mass Delete" gerekçesiyle REDDEDİLDİ (kullanıcı
  onayı gerektiriyor, bu oturumda istenmedi). Şema önceki ailenin (21)
  çalıştırma geçmişini taşıyor. `MT-UIRUN-001` bu yüzden `☐ Beklemede`
  bırakıldı — gerçek "hiç çalıştırma yok" durumu doğrulanamadı. Diğer
  case'ler bundan etkilenmiyor (kendi fixture'larını kurup ölçüyorlar).
- `FIX-AGENT-01` (`manuel-destek`) — dosya 10'un ürettiği agent — bu
  şeritte üretilmemiş olabilir; ilk kullanan case'te kontrol edilip
  gerekirse `MT-UIAG-005` uygulanacak.

## Devir notu (oturum 17 · devam ediyor)

---

### MT-UIRUN-001

**Gerçek sonuç**
Yukarıdaki "Ortam notu"nda açıklandığı gibi, "reset sonrası boş liste" ön
koşulu bu şeritte doğrulanamadı: `mt_s3` şemasını sıfırlamak paylaşılan bir
şerit kaynağını geri dönüşü olmayan biçimde silmek anlamına geliyordu ve
kullanıcı onayı bu oturumda istenmedi (skill §1.4 kural 3 — paylaşılan
kaynağı bozacak eylem). Case koşulamadı.

**Durum:** ☐ Beklemede — gerekçe: `mt_s3` şemasını `DROP SCHEMA ... CASCADE`
ile sıfırlamak gerekiyor, bu paylaşılan şerit kaynağını geri dönüşsüz
siler; kullanıcı onayı istenmedi. Kapanışta `00-INDEKS.md`'nin açık kalem
tablosuna taşınacak.

---

### MT-UIRUN-002

**Gerçek sonuç**
`support`'a `FIX-PROMPT-01` gönderildi (`sessionId:mt-uirun-002`), sonra
"Çalıştırmalar" ekranı açıldı. İstatistik şeridi dört kutu: Runs, Failed,
Error rate (görünür — `awaitingInputRuns=0`), Tokens. Satırda: `completed`
rozeti, `3.19s` (>0), `428` token (`Tokens`=`Tree tokens`, tek run olduğu
için eşit), `27` olay (>0), göreli zaman "9 sec. ago" — üzerindeki `title`
özniteliği mutlak zaman taşıyor ("Sep 17, 2026, 9:37:39 PM"). Hiçbir "N
child run" rozeti yok (`childRunCount=0`). Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-003

**Gerçek sonuç**
İki agent'tan (`support`, `manuel-destek`) birer run zaten vardı. Agent
seçicisi "Support Assistant", Durum "Completed" yapıldı: ağ isteği
`GET /api/runs?agentName=support&status=Completed&skip=0&take=50` —
`includeChildren` YOK, `skip=0` (sayfa sıfırlandı). Agent seçicisi "All
agents"a döndürülünce istek `GET /api/runs?status=Completed&skip=0&take=50`
— `agentName` düştü, `status` kaldı. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-004

**Gerçek sonuç**
`router`'a `FIX-PROMPT-01` gönderildi. Varsayılan (kök) görünümde yalnız
`router`'ın satırı vardı: `"1 child run"` rozeti, `878` token ama `1,744`
tree token (fark tam olarak `support` alt çalıştırmasının token'ları
kadar — toplama tutarlı). `support`'un kendi satırı listede YOKTU. Kapsam
"Include child runs" yapılınca `support`'un satırı EKLENDİ: `"depth 1"`
rozeti taşıyor; `router`'ın satırında bu rozet YOK. Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-005

**Yöntem notu.** `manuel-bos` (`FIX-AGENT-02`) bu şeritte üretilmemiş
(`404`). 51 kez çağırmaya gerek kalmadı — önceki case'lerden zaten **77+**
run vardı, ön koşulun (`≥51 satır`) kendisi zaten sağlanmıştı.

**Gerçek sonuç**
"Çalıştırmalar" ekranı açıldığında `Pager` görünürdü: "Previous" devre
dışı, "Next" etkin, "Page 1" metni. "Next"e tıklanınca ağ isteği
`GET /api/runs?skip=50&take=50` — "Page 2" metni göründü. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-006

**Gerçek sonuç**
Sayfa 2'de (tüm satırlar bitmiş), 15 saniye işlem yapılmadan beklendi. Ağ
sekmesinde `GET /api/runs?skip=50&take=50` isteği bu pencerede **6 kez**
tekrarlandı (~5 sn aralıklı) — hiçbir satır çalışmıyorken bile liste
kendini yeniliyor. Beklenen sonuçla birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-007

**Yöntem notu.** Kısa yanıtlar (`FIX-PROMPT-02`) saniyenin altında bitiyor,
akış penceresini yakalamaya izin vermiyordu (ilk deneme: sayfa açıldığında
run zaten `Completed`). Uzun bir hikâye isteğiyle (`Prefer: respond-async`,
~40 sn'lik gerçek üretim) tekrarlandı.

**Gerçek sonuç**
Run `queued`/`running` iken sayfaya girildi: "Transcript" başlığının
yanında `animate-ping` sınıflı, `aria-label="Waiting for events…"` taşıyan
bir nabız noktası (spinner) görüldü — model ilk token'ı üretene kadar
(~20 sn, gerçek gecikme) sürekli göründü. Run `Completed` olunca: bu
spinner `<span>`'i DOM'dan kalktı (`hasPingSpinner:false`), `aria-busy`
taşıyan panel `"false"` oldu, transkript gerçek metni gösterdi ("Chapter
I: The Harbor of Salt and Lanterns..."). Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-008

**Gerçek sonuç**
`MT-UIRUN-007`'nin bitmiş run'ına (F5 ile) tam sayfa yenilemesi yapıldı.
Giden `GET .../events` isteğinin istek başlıkları incelendi: `Last-Event-
ID` YOK (yalnız `accept: text/event-stream`, `authorization`, `referer`
vb.). Transkript metni ("## Chapter I: The Harbor of Salt and Lanterns...")
yenilemeden ÖNCEKİ ve SONRAKİ okumada bayt bayt aynı. Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-009

**Gerçek sonuç**
`MT-UIRUN-002`'nin run'ında Zaman Çizelgesi: `run.started`, `tool.invoking`
(`get_order_status`), `tool.invoked`, art arda `message.delta` satırları,
sırayla mevcut. Her olay tipi kendi `className`'inde renklendirilmiş
(`text-info` — run.started/tool.invoking). `message.delta` gövdesi düz bir
`<span class="font-mono ...">` (kod bloğu DEĞİL, akan metin). `tool.
invoking` gövdesi bir `<pre class="...font-mono...">` (`CodeBlock` biçemi)
içinde, tool adı satırın kendisinde de rozet olarak tekrarlanıyor. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-010

**Yöntem notu — doküman düzeltmesi.** `FIX-PROMPT-05` bu koşumda
`"gizli-proje hakkinda bilgi ver"` diyor; kaynakta (`samples/Tracon.Api/
Program.cs:217`) engellenen terim artık `"confidential-project"`dir (aynı
bayatlık `05-SAGLAYICI-OPENAI.md` `MT-OAI-084`'te de düzeltilmişti).
`"confidential-project hakkinda bilgi ver"` kullanıldı.

**Gerçek sonuç**
Run `failed`, Zaman Çizelgesi: `run.started` → `content.blocked`
(`guard:"pattern"`, `rule:"denied-term"`, `direction:"Input"`,
`action:"Block"`) → `run.failed`. Transkript paneli TOPTAN BOŞ DEĞİL:
`ContentBlocked` olayının kendisine karşılık gelen bir kart YOK, ama
`RunFailed`in ürettiği kırmızı hata kartı (`bg-danger-soft text-danger`)
var: "Content was blocked by the 'pattern' guard...". Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-011

**Gerçek sonuç**
Var olmayan bir modelle (`manuel-model-hata`, `var-olmayan-model-xyz`) bir
run üretildi (`failed`). "Failure" paneli: kırmızı rozet (`bg-danger-soft
text-danger`) `error.type` metnini ("upstream_error") taşıyor, altında
`error.message` kırmızı metinle ("The model provider request failed.").
Durum rozeti `failed`. Beklenen sonucun tamamı birebir örtüştü. Test
agent'ı iş bitince silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-012

**Gerçek sonuç**
`summarize-and-approve` workflow'u çalıştırıldı. Run sayfasında: durum
rozeti "awaiting input"; başlık `"workflow · summarize-and-approve"`
gösteriyor (agent adı YERİNE); "workflow screen" bağlantısı `/tracon/
workflows/summarize-and-approve`'a gidiyor. "Çalıştırmalar" listesine
dönüldüğünde istatistik şeridinin üçüncü kutusu artık "Error rate" DEĞİL,
"AWAITING INPUT: 1" (+ ipucu metni). Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-013

**Gerçek sonuç**
`summarize-and-translate` workflow'u çalıştırıldı, `completed` oldu.
Sayfadaki `h2` başlıklarının TAMAMI tarandı: Feedback, Transcript, Event
timeline, Call tree, Trace, Tool calls — "Replay this run" YOKTU
(`hasReplay:false`). Çağrı ağacı ("Call tree") paneli GÖRÜNÜYORDU. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-014

**Gerçek sonuç**
`router` kök sayfasında "Call tree": `router` satırı "this run" rozetiyle
vurgulu, altında `└`-önekli, girintili `support` satırı (bağlantı). `support`
satırına tıklanıp AYNI çalıştırmanın sayfasına gidilince: AYNI iki satır,
ama şimdi `support` "this run" rozetini taşıyor, `router` sıradan bağlantı.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-015

**Gerçek sonuç**
`support` alt çalıştırmasının sayfasında "Trace" paneli boş-durum metnini
gösterdi ("Spans live on the root run..."), içindeki bağlantı `router`'ın
KÖK run kimliğine gidiyordu. Ağ sekmesinde `.../trace` isteği hiç YOKTU.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-016

**Gerçek sonuç**
Akış sürerken (`status:running`) sayfada yalnız "Transcript" ve "Event
timeline" başlıkları vardı — "Trace", "Tool calls", "Replay this run",
"Feedback" YOKTU. Run `Completed` olduktan sonra sayfa hiç elle
yenilenmeden (yalnız bekleyerek) yeniden kontrol edildi: dördü de
KENDİLİĞİNDEN belirdi (`refetchInterval`). Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-017

**Gerçek sonuç**
`runs/00000000-...` adresine gidildi. `ErrorNote`: "Run not found: There is
no run with id '00000000-0000-0000-0000-000000000000'." — sunucunun
varsayılan `en` locale mesajı (Türkçe "kimlikli bir calistirma yok" DEĞİL —
bu turun sistematik `en`-varsayılan bulgusuyla tutarlı, K-228). Hiçbir panel
(`h2` sayısı `0`) render edilmedi. Beklenen sonucun davranışsal kısmı
(404 dalı, panelsiz) birebir örtüştü; yalnız dil beklentisi bu ortamda
geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-018

**Gerçek sonuç**
Uzun bir akış üretilip `curl -D -` ile yanıt başlıkları yakalandı:
`Content-Type: text/event-stream`, `Cache-Control: no-cache,no-store`,
`Pragma: no-cache`, `X-Accel-Buffering: no`, `Content-Encoding: identity`
— beşi de var. Ham gövdede model ilk token'ı üretmeden geçen sürede **22**
kez `: waiting` yorum satırı görüldü (Türkçe `: bekleniyor` DEĞİL — `en`
varsayılan locale, bu turun sistematik bulgusuyla tutarlı). Bağlantı
kopmadı. Beklenen sonucun başlık kısmı birebir, keep-alive davranışı
davranışsal olarak (dil hariç) örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-019

**Gerçek sonuç**
`page.context().setOffline(true)` (Playwright'ın DevTools "Offline"
kısıtlamasıyla AYNI CDP mekanizmasını kullanır: `Network.
emulateNetworkConditions`) uygulanıp akan bir run'ın sayfasında **23
saniye** beklendi. Ağ sekmesi isteği hâlâ `200 OK` (hiç `failed`
işaretlenmedi) — CDP "offline" YENİ bağlantıları engelliyor (`fetch('/api/
diagnostics')` → `Failed to fetch`, doğrulandı) ama ZATEN AÇIK bir
`chunked` SSE gövdesini KESMİYOR; okuyucu (`reader.read()`) sessizce
askıda kalıyor. Sonuç: Transkript paneli 23 sn boyunca "Waiting for
events…" yazısında SESSİZCE takılı kaldı — beklenen `ErrorNote` HİÇ
belirmedi. Bu, case'in "istemci hata gösterir" beklentisiyle ÇELİŞİYOR;
gerçek davranış daha sessiz (ve kullanıcı için daha az bilgilendirici):
bağlantı koptuğunda ekranda HİÇBİR görsel değişiklik yok, "hâlâ
çalışıyor" ile "bağlantı öldü" ayırt edilemiyor.

Çevrimiçiye dönüp tam sayfa yenilemesi (F5) yapıldı: giden `.../events`
isteği yine `Last-Event-ID` TAŞIMADI (doğrulandı). Run bu sırada sunucu
tarafında zaten `Completed` olmuştu (istemci bağlantısı sunucunun kendi
üretimini durdurmuyor); yenilenen sayfada olay akışı sıfırdan yeniden
kuruldu, `Event timeline (4)` — TAM ve kayıpsız. Bu kısım beklenen
sonuçla örtüştü.

**Not:** Bu davranışın Playwright'ın `setOffline`'ı ile gerçek DevTools
"Offline" onay kutusu arasında (ikisi de aynı CDP çağrısını kullanır)
farklılık göstermesi olası DEĞİL — ama bir kablosuz bağlantının fiziksel
kopması (TCP RST/zaman aşımı) farklı davranabilir; bu ayrım koşulamadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S3-005`.

---

### Yeniden koşum — 2026-09-18 (Aile F kapanışı)

**Gerçek sonuç — ✅ GEÇTİ, ama kaydın TEŞHİSİ ÇÜRÜTÜLDÜ.**

🚨 **`setOffline(true)` açık bir `chunked` SSE gövdesini KESMİYOR.** Aynı
yordam gerçek Chromium'da tekrarlandı (uzun bir `respond-async` run, run
sayfası açık, `page.context().setOffline(true)`):

```
offline yururlukte   -> fetch('/tracon/api/diagnostics') = "threw: Failed to fetch"
                        navigator.onLine = false
ayni anda ayni akis  -> BES OLAY DAHA teslim etti, run "tamamlandi"ya gecti
                        (olay zaman cizelgesi 1 -> 5)
```

Yani bağlantı **ölmedi**; turun gördüğü 23 saniyelik donukluk sağlıklı bir
bağlantı üzerinde **sessiz bir run**'dı (düşünen model hiçbir olay
üretmiyordu). Kaydın "bağlantı sessizce koptu" teşhisi bu ölçümle
yanlışlanmıştır.

**Kaydın MEKANİZMA tespiti yine de doğruydu ve düzeltildi.** `for await`
döngüsünde hiçbir zaman aşımı yoktu; gerçekten ölü bir bağlantıda (uyuyan
dizüstü, zaman aşımına uğrayan proxy) `reader.read()` hiçbir şey atmadan
sonsuza dek bekler. `readSse` artık isteğe bağlı `idleTimeoutMs` alıyor ve
konsol 30 saniye geçiriyor (K-803). Ölçü **bayttır, frame değil**: sunucu
`RunEventPollInterval`'de bir `: waiting` gönderir ama `SseDecoder` yorumları
düşürür, yani sağlıklı ama sessiz bir run hiç frame üretmez.

Kanıt, `setOffline`'ın üretemediği koşulu doğrudan kuran testlerdedir:
`packages/tracon-client/test/sse-idle.test.ts` (dört test, ikisi düzeltmeden
önce 5 sn zaman aşımıyla düşüyordu) ve
`src/Tracon.UI/frontend/src/screens/run-detail.test.tsx` (ekran eşikten sonra
`ErrorNote` gösteriyor; düzeltmeden önce kırmızıydı).

**Altından ayrı bir bulgu çıktı ve aday olarak yazıldı (`F-245`):** sessiz bir
run ile ölü bir bağlantı kullanıcıya **hâlâ aynı görünüyor**. Keep-alive'lar
canlılığı kanıtlıyor ama konsol onları hiç görmüyor. Bu bir arayüz tasarımı
kararıdır, bu kusurun kapsamında değildir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-020

**Yöntem notu.** Kabuğun bearer token'ı `localStorage`'da DEĞİL, sekmeye
özel tutuluyor ("The token stays in this browser tab and is never written
to disk.") — ikinci sekme kendi token girişini istedi, ayrıca girildi.

**Gerçek sonuç**
Uzun bir run iki AYRI sekmede açıldı. Her iki sekme de kendi bağımsız
`GET .../events` isteğini açtı (ikisi de ayrı `request #6`, aynı URL).
Run tamamlanınca iki sekmenin de Transkript paneli BİREBİR AYNI metni
gösterdi ("## Chapter I: The Departure..."). Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-021

**Gerçek sonuç**
Akan bir run'ın sayfasında (`running`) "Cancel run" düğmesi vardı, tooltip'i
"Stops the agent at its next checkpoint. Work already recorded stays, and
the agent can be run again." — hiçbir tarayıcı onay kutusu açılmadı, düğme
tıklanabilir kaldı. Run `Completed` olunca sayfa yeniden açıldığında düğme
TAMAMEN YOKTU. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-022

**Yöntem notu.** İstek-yanıt döngüsü bu ortamda birkaç yüz ms sürdüğü için
düğmenin `busy` ara durumunu (adım 3) round-trip gecikmesiyle yakalamak
güvenilir olmadı (`MT-RES-003`'ün aynı sınırı) — istek her zaman benim bir
sonraki kontrolümden ÖNCE tamamlandı.

**Gerçek sonuç**
"Cancel run" düğmesine tıklandı: ağ isteği `POST .../cancel` → `202
Accepted`. Elle hiçbir yenileme yapılmadan (yalnız bekleyerek) durum rozeti
"canceled" oldu. `GET /api/runs/{id}` doğruladı: `status:"Canceled"`,
`completedAt` dolu. Beklenen sonucun ölçülebilir kısmı (202, kendiliğinden
Canceled'e dönüş) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-023

**Gerçek sonuç**
`MT-UIRUN-002`'nin bitmiş (`Completed`) run'ına doğrudan `POST .../cancel`
→ `409`, `title:"Run already ended"` (Türkçe `"Calistirma zaten
sonlanmis"` DEĞİL — `en` varsayılan locale, bu turun sistematik bulgusu),
`detail:"Run '...' is already in status 'Completed'."` — mevcut durum
gövdede birebir taşınıyor. Beklenen sonucun davranışsal kısmı (409 +
mevcut durumun taşınması) birebir örtüştü; yalnız dil beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-024

**Gerçek sonuç**
`Prefer: respond-async` ile bir run kuyruğa gönderilip AYNI saniyede
`POST .../cancel` çağrıldı. İşçi işi HENÜZ ALMAMIŞTI: `202`, dönen gövdede
`status:"Canceled"` DOĞRUDAN (bekleme yok), `completedAt` `startedAt`'ten
~50ms sonra. Arayüzde `runs/{id}` sayfası "canceled" rozetini gösterdi,
İptal düğmesi hiç YOKTU. Beklenen sonucun "işçi almadı" dalı birebir
gözlemlendi ve örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-025

**Gerçek sonuç**
Var olmayan bir run kimliğine `POST .../cancel` → `404`, `title:"Run not
found"`, `detail:"There is no run with id '00000000-...'."` (Türkçe
`"kimlikli bir calistirma yok"` DEĞİL — `en` varsayılan locale, sistematik
bulgu). Beklenen sonucun davranışsal kısmı (404) birebir örtüştü; dil
beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-026

**Yöntem notu.** `support` kod kökenli olduğu için `ReplayTools`/`NoTools`
istekleri `400` ile reddediliyor (bkz. `MT-RES-050`, aile 21) — mekanizma
`manuel-destek` (veritabanı kökenli, `FIX-AGENT-01`) ile ölçüldü, dosyanın
kendi "doküman düzeltmesi" notu da aynı değişimi öneriyor.

**Gerçek sonuç**
`manuel-destek`'in tool çağrılı bir run'ında "Replay this run" paneli: Araç
Modu varsayılanı "Play back recorded results" (ReplayTools); Sürüm seçicisi
"Today's version" VE "v1" (veritabanı kökenli olduğu için gerçek sürüm
listeleniyor, `404` dalı tetiklenmedi); Model alanı boş. Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-027

**Gerçek sonuç**
`manuel-destek`'in tool çağrılı run'ında Araç Modu varsayılanı (ReplayTools)
ile "Replay" tıklandı: `200`, yeni `runId` bağlantısı belirdi. Yeni run'da
`GET .../tools`: `get_order_status`, `result:"Order ORD-1001 has
shipped..."` — kayıtlı sonucun AYNISI (gerçek çağrı olmadan oynatıldı).
`replayOfRunId` kaynağı gösteriyor. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-028

**Gerçek sonuç**
Araç Modu "Do not attach tools" (NoTools) seçilince ipucu metni değişti:
"The model answers without tools. Measures the effect of an instruction
change alone." — "Replay"e tıklanınca `200`, yeni run açıldı. Yeni run'ın
`GET .../tools` çağrısı **0** kayıt döndü — tool hiç çağrılmadı. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-029

**Gerçek sonuç**
Araç Modu "Run tools for real" (LiveTools) seçilip "Replay"e tıklandı:
`200`, yeni run açıldı. Yeni run'ın `GET .../tools` çağrısı TAZE bir
`toolCallId` ve `createdAt` (tam tıklama anına ait) taşıyan bir
`get_order_status` kaydı döndü — kayıttan kopya DEĞİL, gerçek bir MAF
tool-invoke turu. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-030

**Yöntem notu.** Bu şeridin `mt_s3` şemasında `tool_approval_rules` tablosu
BOŞ — dosyanın andığı "Hatırla" kalıntısı (S4-5, `MT-UIAG-031`) burada yok.
`support`/`FIX-PROMPT-03` doğrudan kullanıldı.

**Gerçek sonuç**
`support`'ta `AwaitingApproval` durumunda bir run'da Araç Modu "Run tools
for real" (LiveTools) seçilip "Replay" tıklandı: ağ isteği `409 Conflict`.
`ErrorNote`: "A tool requiring approval cannot run live: Agent 'support'
carries the tool 'cancel_order', which requires approval..." (Türkçe
"Onay gerektiren tool canli calistirilamaz" DEĞİL — `en` varsayılan
locale, sistematik bulgu) — `cancel_order` adı gerekçede geçiyor. `GET
/api/runs?sessionId=mt-uirun-030` hâlâ **1** kayıt — yeni bir `runs` satırı
OLUŞMADI. Beklenen sonucun davranışsal kısmı birebir örtüştü; dil
beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-031

**Gerçek sonuç**
`manuel-destek` UI'dan düzenlendi: `get_order_status` kaldırıldı,
`list_recent_orders` eklendi (`v2` açıldı). `MT-UIRUN-026`'nın eski
(`v1`, `get_order_status` çağrılı) run'ında Sürüm seçicisinden `v2`
seçilip Araç Modu `ReplayTools` bırakılarak "Replay" tıklandı: `200`
(422 DEĞİL) — model YENİ koşulda (v2, `list_recent_orders` mevcut,
`get_order_status` yok) HİÇBİR tool çağırmadı, yalnız metinle yanıtladı
(olay akışı: yalnız `run.started`/`message.delta`/`message.completed`/
`run.completed`, tool olayı YOK). Bu, case'in kendi belgelediği İKİNCİ
dal ("model hiçbir tool çağırmazsa: 200 döner... kusur SAYILMAZ") —
gerçek gözlem bu dalda gerçekleşti, `422` dalı (ilk dal) TETİKLENMEDİ.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — gerçekleşen dal:
model hiç tool çağırmadı (case'in kendi ikinci beklenen sonucu).

---

### MT-UIRUN-032

**Gerçek sonuç**
`Tracon:RunRecording:RecordRunInput=false` ile yeniden başlatılıp bir run
üretildi. `GET .../input` → `404`. Sayfada "Replay this run" başlığı HİÇ
YOKTU (bileşen kendini hiç render etmedi). Ayar geri alındı, uygulama
normal (`RecordRunInput` varsayılan `true`) ayarla yeniden başlatıldı —
sonraki case'ler bu adımdan etkilenmedi. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-033

**Gerçek sonuç**
`POST /api/runs/00000000-.../replay` → `404`, `title:"Run not found"`,
`detail:"There is no run with id '00000000-...'."` (Türkçe `"Calistirma
bulunamadi"` DEĞİL — `en` varsayılan locale). Beklenen sonucun davranışsal
kısmı birebir örtüştü; dil beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-034

**Gerçek sonuç**
`MT-UIRUN-027`'nin yeni run'ında "Comparison" paneli HİÇBİR tıklama
olmadan otomatik render edildi. Alan tablosu (LEFT=kaynak, RIGHT=yeni):
Status (Completed/Completed), Version (1/1), Model (gpt-5.4-mini/
gpt-5.4-mini), Duration (3019ms/1633ms — farklı), Tokens (241/414 —
farklı), Cost (—/—), Tool calls (1/1 — aynı), Error class (—/—), Scores
(0/0). "OUTPUT" bölümünde `DiffView`: modelin ürettiği metin küçük bir
farkla döndü ("ORD-1001 siparişiniz..." → "Siparişiniz...") ve `-`/`+`
işaretli satırlarla RENKLİ gösterildi (nötr DEĞİL — gerçek bir fark vardı).
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-035

**Gerçek sonuç**
`GET .../compare/00000000-...` → `404`, `detail:"There is no run with id
'00000000-...'."` — eksik tarafın kimliği (`b`) mesajda anılıyor (Türkçe
"Calistirma bulunamadi" DEĞİL — `en` varsayılan locale). Beklenen sonucun
davranışsal kısmı birebir örtüştü; dil beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-036

**Yöntem notu — doküman düzeltmesi.** `manuel-bos` bu şeritte hiç
üretilmedi; onun yerine hiç oturumu olmayan `Translator` seçildi. Kaynak
incelemesi (`sessions.tsx:150-170`) case'in kendi varsayımını ÇÜRÜTTÜ:
boş-durum başlığı `agentName.length > 0` şartına göre dallanıyor —
belirli bir agent SEÇİLİ olduğu sürece (o agent'ın GERÇEKTEN sıfır oturumu
olsa bile) her zaman `common.noResults` + "Clear filters" gösteriliyor;
`sessions.empty.title` + Playground bağlantısı YALNIZ filtre HİÇ
uygulanmamışken (`All agents`, tüm sistemde sıfır oturum) görünüyor. Yani
case'in "boşsa oynatma alanına git bağlantısı görünür" beklentisi, ozel
BİR agent seçildiğinde asla tetiklenmiyor — bu koşum kaydı düzeltildi.

**Gerçek sonuç**
Adım 1: Agent "Support Assistant" seçilince **29** satır listelendi, her
satırda oturum kimliği (kısaltılmış), agent bağlantısı, göreli oluşturma/
güncelleme zamanı. Adım 2: "Translator" (sıfır oturumlu) seçilince liste
boşaldı ("No session for this agent yet. Clear the filter to see every
session." + "Clear filters" düğmesi) — Playground bağlantısı YOKTU
(yukarıdaki düzeltmeyle tutarlı). Adım 1 birebir örtüştü; adım 2 kod
okumasıyla düzeltilmiş beklentiyle örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-037

**Gerçek sonuç**
Bir oturumun çöp kutusu düğmesine tıklanınca konsolun KENDİ dialogu açıldı
(`role="dialog"`, tarayıcının native `confirm()`'ü DEĞİL): "Delete this
session? Deletes the conversation and every message in it..." — açılış
odağı "Cancel" düğmesindeydi. `Esc`'e basılınca dialog kapandı, oturum
HÂLÂ vardı (`GET /api/sessions/{id}` → `200`). Tekrar tıklanıp "Delete"e
basılınca: `DELETE /api/sessions/{id}` → `204`, satır elle yenileme
olmadan listeden KALKTI. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-038

**Gerçek sonuç**
`DELETE /api/sessions/yok-boyle-bir-oturum` → `404`, `title:"Session not
found"`, `detail:"There is no session with id 'yok-boyle-bir-oturum'."`
(Türkçe "Oturum bulunamadi" DEĞİL — `en` varsayılan locale). Beklenen
sonucun davranışsal kısmı birebir örtüştü; dil beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-039

**Gerçek sonuç**
`sessions/mt-uirun-002`'nin varsayılan "Chat history" sekmesi: `user`/
`assistant`/`tool` rol etiketleriyle sırayla mesaj satırları, her birinde
"Branch from here" düğmesi. "Raw state" sekmesine geçilince:
`sessionDetail.stateNotice` uyarısı ("This is the session state of the
framework itself. Tracon stores it and never interprets it...") + ham
JSON (`{"stateBag": {"toolApprovalState": {...` — kaydırılabilir,
yorumlanmamış). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-040

**Gerçek sonuç**
Dosyanın kendi "doküman düzeltmesi" notu (önceki bir tur tarafından zaten
uygulanmış) doğrulandı: PostgreSQL bağlantısı boşken (`Tracon:PostgreSql:
ConnectionString=""`) `support` ile üretilen oturumun `GET /api/sessions/
{id}` yanıtı `messages` alanını TAM doldurdu (`support` her koşulda MAF'ın
kendi `InMemoryChatHistoryProvider`'ını kullanıyor — Tracon'in SQL
seçiminden bağımsız). Arayüzde "Chat history" sekmesi mesajları normal
şekilde gösterdi, `sessionDetail.noHistory` boş-durumu TETİKLENMEDİ. Adım
2 (`POST .../branch`) → `501`, `title:"Branching not supported"` (Türkçe
"Dallandirma desteklenmiyor" DEĞİL — `en` varsayılan locale), `detail`
gerekçeyi tam anlatıyor. Bağlantı ayarı test sonunda geri kuruldu. Adım 1
düzeltilmiş beklentiyle, adım 2 (davranış, dil hariç) orijinal beklentiyle
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-041

**Gerçek sonuç**
`sessions/mt-uirun-002` sayfasında "1 run" düğmesi vardı (tekil run
sayısını doğru gösteriyor). Tıklanınca `/tracon/runs?sessionId=mt-uirun-002`
adresine gidildi; "Çalıştırmalar" listesi yalnız **1** satır gösterdi (o
oturuma ait run). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-042

**Gerçek sonuç**
Üç turluk bir oturumda (8 mesaj: tool çağrılı 1. tur, "Tesekkurler"/"Rica
ederim!" 2. tur, "Baska bir sorum daha var"/... 3. tur) ikinci kullanıcı
mesajının ("Tesekkurler", 0-tabanlı `seq=4`) "Branch from here" düğmesine
tıklandı: `POST .../branch` gövdesi `{"upToSequence":4}`, `201 Created`,
tarayıcı YENİ oturuma OTOMATİK yönlendi. Yeni oturumda **5** mesaj (0-4,
"Tesekkurler" DAHİL) — "Rica ederim!" (5) ve üçüncü tur (6-7) YOKTU.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-043

**Gerçek sonuç**
`MT-UIRUN-042`'nin yeni (dallanmış) oturumuna yeni bir tur gönderildi
("Yeni bir mesaj daha"). Eski oturumun (`mt-uirun-042`) mesaj sayısı
öncesi/sonrası BİREBİR AYNI kaldı (`8`) — dal üzerindeki yeni mesaj yalnız
yeni oturuma yazıldı. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-044

**Gerçek sonuç**
Dosyanın kendi düzeltilmiş beklentisi doğrulandı: `session-detail.tsx`'te
HER "Branch from here" düğmesi bir mesaja bağlı (parametresiz yok) — AMA
`playground/support?sessionId=mt-uirun-042` sayfasında oturum başlığının
yanında `data-testid="branch-session"` taşıyan AYRI bir düğme var. Ona
tıklanınca: `POST .../branch` gövdesi `{"upToSequence":null}`, `201`,
yanıt `copiedItemCount:8` — TÜM konuşma (orijinal `mt-uirun-042`'nin 8
mesajının hepsi) kopyalandı. Beklenen sonucun düzeltilmiş hâliyle birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-045

**Gerçek sonuç**
`POST .../branch {"newSessionId":"manuel-dal-cakisma-01"}` → `201`,
`sessionId:"manuel-dal-cakisma-01"`. AYNI istek İKİNCİ kez → `409`,
`title:"Session id in use"` (Türkçe "Oturum kimligi kullanimda" DEĞİL —
`en` varsayılan locale), `detail` üzerine yazmadığını açıkça anlatıyor.
Beklenen sonucun davranışsal kısmı birebir örtüştü; dil beklentisi
geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-046

**Gerçek sonuç**
`POST /api/sessions/yok-boyle-bir-oturum/branch` → `404`, `title:"Session
not found"`, `detail:"There is no session with id 'yok-boyle-bir-
oturum'."` (Türkçe DEĞİL — `en` varsayılan locale). Beklenen sonucun
davranışsal kısmı birebir örtüştü; dil beklentisi geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-048

**Yöntem notu.** İlk üç deneme modelden HİÇ düşünme içeriği alamadı
(Claude'un kendi kararı — bir "bütçe" üst sınırdır, düşünmeyi ZORUNLU
kılmaz). Sonraki denemelerde (bkz. `MT-UIRUN-047`'nin ortamında ve bu
case'e dönüşte) model GERÇEKTEN düşünme içeriği üretti — bu, asıl
bulguyu (aşağıda) ortaya çıkardı.

**Gerçek sonuç**
`claude-thinking` ile DÖRT AYRI çağrıda (üçü akışlı, biri akışsız —
`Idempotency-Key` ile) model GERÇEKTEN düşünme içeriği üretti: akışlı
denemelerde ham SSE'de 48-76 arası `"$type": "reasoning"` parçası
doğrudan gözlendi; akışsız denemede `response.messages[0].contents`
içinde açık bir `reasoning` tipli öge vardı. DÖRDÜNDE DE kayıtlı `GET
.../events` **SIFIR** `ReasoningDelta` döndürdü — yalnız `MessageDelta`/
`RunStarted`/`RunCompleted`(/`RunFailed` bir denemede, ilgisiz bir iptal).
Bu, `HATA-S3-006` olarak kaydedildi (bu dosyanın başında) — kaynak
incelemesiyle kısmen izole edildi: tip ayırt edicisi doğru (bağımsız
reflection probuyla doğrulandı), kardeş alan (`RecordMessageDeltas`) AYNI
çalıştırmalarda doğru çalışıyor (options bağlama sorunu EKARTE edildi),
akışlı VE akışsız yol İKİSİ DE aynı sıfır sonucu veriyor. Tam kesişim
noktası izole edilemedi ama semptom ve etki dört bağımsız denemeyle
kesin olarak kanıtlandı. `MT-UIRUN-048`'in kendi "gerçek koşumda ölçülen"
notu (2026-08-19, 7 `ReasoningDelta` kaydedilmiş) bu turda ASLA tekrar
üretilemedi — olası bir gerileme.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S3-006`.

---

### Yeniden koşum — 2026-09-18 (Aile F kapanışı)

**Gerçek sonuç — ✅ GEÇTİ; kusur YENİDEN ÜRETİLEMEDİ.** Kod tur boyunca donuk
kaldı ve bu yol A–E ailelerinde hiç değişmedi (`git diff 7e3a4de7..HEAD --
src/Tracon.Core/Recording/RunRecordingAgent.Persistence.cs`: `Reasoning`
dalına dokunulmadı). Aynı `claude-thinking` agent'ıyla, gerçek Anthropic
anahtarıyla:

```
akissiz  -> response.messages[0].contents: reasoning (349 karakter) + text
            GET .../events: RunStarted, ReasoningDelta x1, MessageDelta,
                            MessageCompleted, RunCompleted
akisli   -> SSE'de 88 reasoning parcasi
            GET .../events: ReasoningDelta x86, MessageDelta x51
            (eksik iki parca imza deltasidir; metni bos, guard dogru atliyor)
```

**Turun gördüğü semptomun ne ürettiği de ölçüldü.** Aynı uygulama
`Tracon__RunRecording__RecordReasoningDeltas=false` ile yeniden başlatıldı:

```
SSE'de 41 reasoning parcasi
GET .../events: RunStarted, MessageDelta x28, RunCompleted   <- SIFIR ReasoningDelta
```

Bu, kaydın tarif ettiği semptomun **birebir kendisidir**, ve kodda bu sonucu
üretebilecek başka hiçbir yol yok (`grep -rn "ReasoningDelta" src/`: ayarı ve
boş metni süzen dal dışında hiçbir katman bu olayı filtrelemiyor). ∴ ayar o
oturumda kapalıydı.

🚨 **Kaydın elemesi geçersizdi.** "Kardeş alan `RecordMessageDeltas` aynı
koşumlarda çalışıyor, bu options bağlama sorununu ekarte ediyor" — ama
`RecordMessageDeltas` **varsayılan olarak `true`**'dur; bağlama hiç olmasa da
çalışırdı. Eleme hiçbir şey elemiyordu ve kusur bu yüzden bir tur boyunca
izole edilemedi.

**Teşhis boşluğu kapatıldı (K-804).** `/api/diagnostics` artık yürürlükteki
`runRecording` ayarlarını bildiriyor; bir operatör "model düşünmedi" ile
"kayıt kapalı" ile "bozuk"u tek istekle ayırt edebiliyor. Alan eklenirken
ortaya çıktı ki `TraconDiagnosticsCollector`'ın DI fabrikası **son iki isteğe
bağlı argümanı hiç geçmiyordu** — rapor kurulumun değil varsayılanların
ayarlarını söylerdi, ve `logger` de hiç verilmediği için katalog okuma hatası
her kurulumda sessizce yutuluyordu. İkisi de düzeltildi.

Eksik test seviyesi de kalıcı hâle getirildi:
`ReasoningRecordingEndpointTests` (DI + HTTP; açık kaydeder, kapalı kaydetmez,
rapor hangisi olduğunu söyler). Var olan `ReasoningRecordingTests` yalnız
sarmalayıcının kendi anahtarını kilitliyordu ve kurulum-düzeyi davranış
hakkında hiçbir şey söylemiyordu — turun dört gerçek sağlayıcı çağrısı harcayıp
izole edememesinin nedeni tam olarak bu boşluktu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-047

**Yöntem notu.** İlk denemelerde (§`MT-UIRUN-048`) model düşünme içeriği
ÜRETMEMİŞTİ (model-düzeyi değişkenlik) — bu case'in kendi koşumunda model
GERÇEKTEN düşünme içeriği üretti, iki case de böylece tam ölçülebildi.

**Gerçek sonuç**
`Tracon:RunRecording:RecordReasoningDeltas=false` ile yeniden başlatılıp
`claude-thinking`'e aynı soru gönderildi. HAM canlı akışta `$type:
"reasoning"` içerikli çerçeveler **48 kez** göründü (MAF'ın ham akışı,
kayıt ayarından ETKİLENMEDİ). Aynı run'ın KAYITLI `/events`'inde
`ReasoningDelta` sayısı **0** — kayıt katmanı düşünme olaylarını hiç
yazmadı. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-050

**Yöntem notu.** Bu şerit `echo` sağlayıcısı yerine GERÇEK OpenAI kullanıyor
(bkz. dosya 02/05'in ortam notları) — case'in kendi "Sapma" notu bu farkı
zaten açıklıyor ("gerçek bir sağlayıcıda ikisi de usage döner").

**Gerçek sonuç**
Akışsız (`Idempotency-Key` ile) ve akışlı (başlıksız) aynı prompt iki kez
`support`'a gönderildi: ikisi de `status:"Completed"`. `usage` alanı
İKİSİNDE de DOLU (akışsız: 370 total token, akışlı: 368 total token) —
case'in kendi notunun öngördüğü gibi, gerçek sağlayıcıda (`echo` DEĞİL)
sapma gözlenmedi. Beklenen sonucun tamamı (gerçek sağlayıcı dalı) birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-051

**Gerçek sonuç**
Uzun bir hikâye isteğiyle akışlı bir run başlatıldı (arka planda), `Running`
olduğu doğrulandıktan sonra `POST .../cancel` → `202 Accepted`. 2 sn sonra
`GET .../runs/{id}` → `status:"Canceled"`. Arka plandaki `curl` süreci
KENDİLİĞİNDEN çıktı (exit 0) — SSE bağlantısı sunucu tarafından kapatıldı,
alınan gövde yalnız ilk `run` çerçevesinden ibaretti (207 bayt). Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-049

**Gerçek sonuç**
`HATA-S3-006` yüzünden bu ortamda `ReasoningDelta` olayı taşıyan HİÇBİR
run YOK — arayüzdeki "Reasoning" bloğunu (`components/transcript.tsx`'in
`ReasoningBlock`'u) gerçek veriyle görsel olarak doğrulamak bu turda
MÜMKÜN olmadı (kayıt katmanı veriyi hiç üretmiyor). Bileşenin KENDİSİ
kaynakta var ve `kind: 'reasoning'` öğesini render ediyor (kod okundu) ama
canlı bir örnekle kanıtlanamadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S3-006`
(bağımlı case, kök nedeni paylaşıyor).

---

### MT-UIRUN-052

**Gerçek sonuç**
`support`'a `mark_preview_ready` çağıran bir istek gönderildi. Olay
akışında `sequence:2`, `type:"Custom"`, `customType:"contoso.preview-
ready"`, `payload:"{\"orderId\":\"ORD-7\"}"` — case'in "ölçülen" örneğiyle
birebir aynı şekil. Hemen ardından `sequence:3`, `type:"ToolInvoked"`,
`customType:null` — Custom OLMAYAN olay customType taşımıyor. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-053 (👤 insan gerekir — Playwright ile programatik olarak doğrulandı)

**Gerçek sonuç**
`runs/{id}` sayfasının Zaman Çizelgesi'nde sıra 2'deki satır: olay adı
alanında (normalde `custom`/`content.blocked` gibi sabit adların olduğu
yerde) DOĞRUDAN `contoso.preview-ready` (yani `CustomType` DEĞERİNİN
kendisi) yazıyor — jenerik kart, satır adı gerçekten `CustomType`. Gövde
`prettyJson` ile 2 boşluklu girintiyle biçimlendirilmiş (`{ "orderId":
"ORD-7" }`). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-054

**Yöntem notu.** Ön koşul geçici bir tool eklemeyi istiyor — kod donuk
olduğu için bunun yerine case'in kendi andığı otomatik test doğrudan
koşuldu.

**Gerçek sonuç**
`./Tracon.Core.UnitTests --filter-method
"*A_Custom_event_under_the_reserved_tracon_prefix_is_rejected*"` →
**1/1 geçti**. `tracon.` önekli bir `CustomType` ile `AppendAsync`
çağrısının `ArgumentException` fırlattığı, donuk kod tabanında zaten
kanıtlı. Beklenen sonucun otomatik-test kanıtı birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-055

**Gerçek sonuç**
`MT-UIRUN-052`'nin run'ının olay akışı, `customType` alanını YOK SAYAN bir
ayrıştırıcıyla okundu (eski istemci simülasyonu): **23** olayın tamamı
hatasız ayrıştırıldı. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-056

**Gerçek sonuç**
`support`'a bir e-posta adresi içeren bir mesaj gönderildi
(`MaskedPii` PII kalıbı). Olay akışının benzersiz `event:` adları:
`content.masked`, `message.delta`, `run.completed`, `run.started` —
`unknown` HİÇ geçmedi. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-057

**Gerçek sonuç**
`MT-UIRUN-052`'nin run'ının ham SSE'sinde çerçeve adı `event: custom`
(tüketicinin `contoso.preview-ready` dizgesi DEĞİL). Aynı çerçevenin
`data:` satırı `customType:"contoso.preview-ready"` alanını dolu taşıyor.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-058

**Gerçek sonuç**
`MT-UIRUN-013`'ün `summarize-and-translate` run'ının benzersiz `event:`
adları: `child.completed`, `child.started`, `executor.completed`,
`executor.invoked`, `message.delta`, `run.completed`, `run.started`,
`superstep.completed`, `superstep.started`, `workflow.output`,
`workflow.started` — üçü de (`workflow.started`, `superstep.started`,
`workflow.output`) VAR, `unknown` HİÇ YOK. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-059

**Gerçek sonuç**
`MT-UIRUN-052`'nin run'ında tam akış: `id:0/event:run.started`,
`id:1/event:tool.invoking`, `id:2/event:custom`... `Last-Event-ID: 1`
başlığıyla yeniden bağlanınca ilk çerçeve `id:2`, `event:custom` — sıra
1'in BİR SONRASINDAN devam etti, aynı olay adıyla. Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-060

**Yöntem notu.** Belge `/tracon/openapi.json`'da DEĞİL, `/openapi/v1.json`'da
(kimlik doğrulama gerektirmiyor, `/tracon` önekinin dışında).

**Gerçek sonuç**
`GET /openapi/v1.json`'da `/tracon/api/runs/{runId}/events`'in `200` yanıtı:
`content: {"text/event-stream": {"schema": {"type": "string"}}}` — dolu
(boş `{}` DEĞİL). `404` yanıtı `content` anahtarı yalnız
`application/problem+json` taşıyor. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-061

**Gerçek sonuç**
`MT-RES-080`'in (aile 21) alt-agent zaman aşımına uğramış run'ının olay
adları arasında `event: child.timed-out` VAR (`child.completed`,
`child.started`, `child.timed-out`, ... — `unknown` YOK). Beklenen sonucun
tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-062

**Yöntem notu.** DevTools "kesme" yerine Playwright'ın kendi rota
yakalaması (`page.route`) kullanıldı — aynı etkiyi (istemci tarafında
`500` yanıtı zorlamak) sunucuya dokunmadan sağlıyor.

**Gerçek sonuç**
Arayüz dili Türkçeye çevrildi (düğme metni "Yeniden dene" oldu, dil
değişikliğini doğruladı). `GET /api/sessions` ilk üç denemede `500`
döndürüldü: hata notu ancak ÜÇÜNCÜ (yani konsolun kendi iki otomatik
tekrar denemesinden SONRAKİ) başarısız denemeden sonra göründü
(`interceptCount:3` — bire kadar hiç göstermedi). Hata metni HAM İngilizce
kaldı ("Internal Server Error: Simulated failure...") — Türkçeye
çevrilmemiş (K-232). "Yeniden dene" düğmesine tıklanınca (bu noktada
yakalama zaten devre dışı) istek TEKRARLANDI, sayfa yenilenmeden **48**
satır yüklendi, hata notu kayboldu. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-063

**Gerçek sonuç**
Ön koşul `TraconPolicies.Admin`'in reddedeceği bir "reader" kimliği ister.
Bu şeritte (ve dosyanın kendi sınır notunda tekrar tekrar anılan) TEK bir
statik bearer token var ve o token HER ZAMAN tam rol taşıyor (`MT-UIRUN-
029`/`MT-RES-029`'un aynı ortam sınırı) — ayrı bir "reader" kimliği üretmenin
bu örnek uygulamada bir yolu yok. Koşulamadı.

**Durum:** ☐ Beklemede — gerekçe: ortamda "reader" rolünü temsil eden ayrı
bir kimlik yok (dosyanın kendi tekrarlanan sınır notu); kapanışta
`00-INDEKS.md`'nin açık kalem tablosuna taşınacak.

---

### MT-UIRUN-064

**Yöntem notu.** İskelet-yerleşim zıplaması (👤 görsel) bu turda
gözlemlenmedi — DOM/erişilebilirlik tarafı programatik olarak ölçüldü.

**Gerçek sonuç**
`jobs` ekranındaki Lane filtresi: `<input id="_r_1_">` ile eşleşen GERÇEK
bir `<label for="_r_1_">Lane'a göre süz</label>` var (yalnız placeholder
DEĞİL) — `runs` ekranındaki Agent/Status/Scope seçicileri de (önceki
case'lerde zaten görüldü) kendi `<label>`'larını taşıyor. "Clear filters"
düğmesi filtre BOŞKEN yoktu; Lane alanına metin girilince BELİRDİ;
tıklanınca hem düğme KAYBOLDU hem alan GERÇEKTEN boşaldı (`inputValue:""`).
Beklenen sonucun ölçülebilir kısmı (etiketler, temizle düğmesinin
görünürlük/işlev döngüsü) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-065

**Gerçek sonuç**
`runs` tablosunda kimlik hücresinin linki her zaman vurgu renginde
(`rgb(115,217,194)`), agent hücresinin linki her zaman soluk
(`rgb(179,198,196)`) — hover/focus'tan bağımsız, kalıcı bir ayrım. Hover'da
kimlik linkinin `text-decoration-line` değeri `none`'dan `underline`'a
geçiyor (`:hover` gerçek fare imleciyle ölçüldü). Klavye `Tab` ile kimlik
linkine ulaşınca satırın `<tr>` arkaplanı `rgba(0,0,0,0)`'dan
`rgb(23,36,41)`'e değişiyor (satırın tamamı vurgulanıyor) ve link
`underline` alıyor; `Enter` kaydı gerçekten açtı (`/tracon/runs/{id}`'ye
navigasyon doğrulandı). Oturum detay ekranındaki "N çalıştırma" düğmesi tek
bir `<a href="/tracon/runs?sessionId=...">` — alt öge yok, dolayısıyla **tek
tab durağı**; orta tıklama gerçek bir yeni sekme açtı (ana sekme aynı sayfada
kaldı). `agents` ekranındaki "Yeni agent" düğmesi de aynı şekilde tek `<a>`
(içindeki `<svg>` odaklanabilir değil, `tabIndex` yok); orta tıklama yeni
sekme açtı. Beklenen sonucun ölçülebilir tamamı (renk ayrımı, hover/focus
altı çizgisi, satır vurgusu, Enter navigasyonu, tek tab durağı, orta tık) 👤
işaretli göz denetimi hariç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-UIRUN-066

**Yöntem notu.** Ön koşulu doldurmak için `mt-uirun-fixture` adıyla yerel bir
MCP sunucusu kaydedildi (`http://localhost:3001/mcp`, aynı
`@modelcontextprotocol/server-everything` fixture'ı — dosya 18'in kaydında
zaten belgeli). Kayıt sonrası ortamda 133 run, 30+ oturum, 50 iş, 1 MCP
sunucusu, 3 sağlayıcı (anthropic/google/openai) vardı — ön koşulun tamamı
karşılandı.

**Gerçek sonuç**
Tarayıcı 375×700'e ayarlandı. On sekiz gezinme ekranının (Gösterge Paneli,
Playground, Çalıştırmalar, Oturumlar, Onaylar, İşler, Değerlendirmeler,
Deneyler, Denetim, Teşhis, Agent'lar, Workflow'lar, Tool'lar, Skill'ler,
Modeller, MCP, Tetikleyiciler, Ayarlar) HER BİRİNDE
`document.documentElement.scrollWidth - clientWidth === 0` — sayfa gövdesi
hiçbir ekranda yatay kaymadı. Bir run detay ekranında ve bir oturumun "Ham
durum" (JSON) sekmesinde de aynı ölçüm sıfır çıktı. `runs` tablosunun kendisi
798px genişliğinde ama kendi `overflow-x-auto` sarmalayıcısının içinde —
sayfa değil, o `<div>` kayıyor (beklenen davranış). `models` ekranında en
sağdaki sağlık badge'inin (right≈331px, viewport 375px — kenara 44px)
tooltip'i hover'da göründü ve tam viewport içinde kaldı (left=148,
right=356 — hiç taşmadı). `runs` tablosunun "Ağaç token" sütun başlığının
açıklama balonu da (yatay kaydırılıp görünür alana getirildikten sonra)
viewport içinde kaldı (left=57, right=281). Beklenen sonucun ölçülebilir
kısmı (sıfır yatay taşma, tablo/kod bloğunun kendi kutusunda kalması,
tooltip'in viewport içinde kalması) birebir örtüştü; balonun "sola kayma"
mekanizmasının kendisi (CSS tarafı) ayrıca izlenmedi, yalnız SONUCU
(taşmama) ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Aile 11 (`UIRUN`) TAMAMLANDI (oturum 17 sonu)

66/66 case işlendi: 64 koşuldu (61 ☑ Geçti, 3 ☒ Kaldı), 2 case ortam sınırı
yüzünden `☐ Beklemede` kaldı — `MT-UIRUN-001` (paylaşılan `mt_s3` şemasını
sıfırlamak onay gerektiriyordu, istenmedi) ve `MT-UIRUN-063` (bu şeritte
"reader" rolünü temsil eden ayrı bir kimlik yok, tek statik bearer token her
zaman tam rol taşıyor). İkisi de kapanışta `00-INDEKS.md`'nin açık kalem
tablosuna taşınacak.

Bu ailede **üç kusur** bulundu (kayıtları yukarıda):

- `HATA-S3-005` (Yüksek) — SSE bağlantısı sessizce koparsa çalıştırma ekranı
  sonsuza dek "Waiting for events…" yazısında donuk kalır, hiçbir hata
  gösterilmez (`run-detail.tsx:184-210`, `for await` döngüsünde zaman
  aşımı/heartbeat denetimi yok).
- `HATA-S3-006` — `RecordReasoningDeltas=true` iken model gerçekten düşünme
  içeriği üretse bile `ReasoningDelta` olayı hiç kaydedilmiyor.
- `HATA-S2-002`'nin bu şeritte de tekrar gözlendiği (yalnız bilgi amaçlı,
  yeni kayıt açılmadı): `/tracon/*` sayfalarının inline tema-boyama script'i
  kendi CSP başlığı tarafından her sayfa yüklemesinde engelleniyor (konsol
  hatası bu oturumda da her navigasyonda görüldü).

`MT-UIRUN-066` için ön koşulu doldurmak amacıyla bu şeride kalıcı bir test
fixture'ı eklendi: MCP sunucusu `mt-uirun-fixture` →
`http://localhost:3001/mcp` (dosya 18'in `server-everything` fixture'ı,
salt paylaşılan/okunur, sunucu süreci durdurulmadı).

Kod tamamen donuk bırakıldı; uygulama durduruldu. Sıradaki aile:
`25-SAGLIK-TESHIS-OPENAPI.md`.

---
