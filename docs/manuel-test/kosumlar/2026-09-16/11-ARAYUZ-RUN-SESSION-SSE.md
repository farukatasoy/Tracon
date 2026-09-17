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
