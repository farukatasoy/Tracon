# 11 — Arayüz: Çalıştırma, Oturum ve SSE (`UIRUN`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../11-ARAYUZ-RUN-SESSION-SSE.md`](../../11-ARAYUZ-RUN-SESSION-SSE.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/11-ARAYUZ-RUN-SESSION-SSE.md
> ```

---

## Temiz geçen case'ler (22)

| Case | Durum | Başlık |
|---|---|---|
| MT-UIRUN-002 | ☑ | `support` ile tek çalıştırma sonrası istatistik şeridi ve sütunlar doğru dolar |
| MT-UIRUN-003 | ☑ | Agent ve Durum filtreleri birlikte çalışır, her değişiklik sayfayı sıfırlar |
| MT-UIRUN-004 | ☑ | `yonlendirici` → `support` devri: kök/tümü ayrımı, alt çalıştırma ve derinlik rozetleri |
| MT-UIRUN-005 | ☑ | Sayfalama: 51. satırdan sonra "İleri" düğmesi açılır |
| MT-UIRUN-011 | ☑ | Başarısız çalıştırmada kırmızı Hata paneli `error.type`/`error.message` gösterir |
| MT-UIRUN-013 | ☑ | Workflow türü bitmiş bir çalıştırmada Yeniden Oynatma paneli HİÇ render edilmez |
| MT-UIRUN-014 | ☑ | Çağrı ağacı paneli: girintili, güncel satır vurgulu, ebeveyn-çocuk sırası doğru |
| MT-UIRUN-017 | ☑ | Var olmayan çalıştırma kimliği `404` `ErrorNote` gösterir |
| MT-UIRUN-020 | ☑ | Aynı çalıştırma iki sekmede aynı anda izlenebilir, ikisi de bağımsız akış açar |
| MT-UIRUN-023 | ☑ | Zaten sonlanmış bir çalıştırmaya doğrudan iptal isteği `409` döner |
| MT-UIRUN-024 | ☑ | Kuyruğa alınmış (`Queued`) bir çalıştırmanın iptali BEKLEMEDEN anında `Canceled` yazar |
| MT-UIRUN-025 | ☑ | Var olmayan çalıştırmanın iptali `404` döner |
| MT-UIRUN-028 | ☑ | `NoTools` modu: tool hiç çağrılmaz, model kayıtlı girdiye yalnız metinle yanıt üretmeye çalışır |
| MT-UIRUN-029 | ☑ | `LiveTools` modu tool'u GERÇEKTEN çalıştırır; Admin-rol denetimi bu ekranda gözlenemez |
| MT-UIRUN-033 | ☑ | Var olmayan çalıştırmayı yeniden oynatma isteği `404` döner |
| MT-UIRUN-035 | ☑ | Var olmayan/başka kiracıya ait bir çalıştırmayla karşılaştırma `404` döner |
| MT-UIRUN-036 | ☑ | Agent filtresi listeyi daraltır; boş liste "Playground'a git" bağlantısı gösterir |
| MT-UIRUN-037 | ☑ | Sil düğmesi yalnız `canOperate` rolünde görünür; onaylanınca liste güncellenir |
| MT-UIRUN-038 | ☑ | Var olmayan oturumu silme isteği `404` döner |
| MT-UIRUN-042 | ☑ | Bir mesajda "Dallandır": o noktaya kadar birebir kopya, SONRASI olmayan yeni bir oturum açılır |
| MT-UIRUN-045 | ☑ | Aynı `newSessionId` ile ikinci dallandırma denemesi `409` döner |
| MT-UIRUN-046 | ☑ | Var olmayan oturumu dallandırma isteği `404` döner |

## Ayrıntı taşıyan case'ler (24)

## MT-UIRUN-001 — Reset sonrası boş liste; "Playground'a git" bağlantısı çalışır

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

## MT-UIRUN-006 — 🚨 Liste, tüm çalıştırmalar sonlanmışken bile 5 saniyede bir kendini yeniler

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

---

## MT-UIRUN-007 — Süren bir çalıştırmada canlı akış: yazı imleci ve dönen simge, bitince kaybolur

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

Canlı doğrulama (gerçek Postgres, `samples/Tracon.Api`): `support`
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

## MT-UIRUN-008 — Bitmiş bir çalıştırmaya SONRADAN girmek, canlı izlemeyle AYNI kod yolundan aynı transkripti üretir

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

## MT-UIRUN-009 — Olay Zaman Çizelgesi: her olay tipi kendi renk/etiketiyle, gövde biçimi olay tipine göre değişir

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

## MT-UIRUN-010 — 🚨 Guard bloklaması yalnız Zaman Çizelgesi'nde görünür; Transkript panelinde HİÇBİR iz bırakmaz

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

## MT-UIRUN-012 — Workflow: `AwaitingInput` durumunda bilgilendirme paneli + çalıştırmalar listesindeki sayaç dalı

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

## MT-UIRUN-015 — Alt çalıştırmada Trace paneli her zaman "köke bak" boş-durumunu gösterir

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
`tests/Tracon.Ui.E2ETests/UiTests.cs`
`Alt_calistirmanin_iz_paneli_koke_git_baglantisini_gosterir_yuklenerek_asili_kalmaz`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIRUN-016 — Süren bir çalıştırmada Trace/Tool Çağrıları/Yeniden Oynatma panelleri hiç render edilmez

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

## MT-UIRUN-018 — SSE yanıt başlıkları sözleşmeye uyar; süren çalıştırma keep-alive yorumu üretir

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

## MT-UIRUN-019 — 🚨 Bağlantı ortasında kopar (Offline): istemci hata gösterir ama `Last-Event-ID` ile OTOMATİK devam ETMEZ

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

## MT-UIRUN-021 — İptal düğmesi yalnız `Running`/`Queued` iken görünür; onay penceresi iptali durdurur

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

## MT-UIRUN-022 — Onaylanan iptal `202` alır, düğme "İptal istendi"ye döner, durum birkaç saniye içinde `Canceled` olur

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

## MT-UIRUN-026 — Bitmiş `support` çalıştırmasında Replay paneli üç alanla görünür

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

## MT-UIRUN-027 — `ReplayTools` (varsayılan) ile oynatma: tool GERÇEKTEN çalışmaz, yeni bir çalıştırma açılır

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

## MT-UIRUN-030 — Onay gerektiren bir tool'u `LiveTools` ile oynatmak `409` ile durdurulur

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
`tests/Tracon.AspNetCore.FunctionalTests/RunReplayEndpointTests.cs`
`Kod_kaynakli_agentteki_onay_gerektiren_tool_LiveTools_ile_de_409_doner`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIRUN-031 — 🚨 `ReplayTools` modunda kayıtlı sonucu OLMAYAN bir tool çağrısı `422` döner

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

## MT-UIRUN-032 — Girdi kaydı kapalıyken (`RecordRunInput=false`) Replay paneli hiç render edilmez

**Gerçek sonuç**
_(2026-08-13 koşumu: Kaldı — HATA-S4-015, `BindRunRecording`'in
`RecordRunInput`'ı hiç okumadığı aynı kök neden — bkz. `MT-API-064`.)_

**2026-08-15 yeniden koşum (KAPANIS-PLANI §9, K-406 sonrası) — Geçti.**
Kök neden `MT-API-064`'te aynı koşumda kod düzeyinde yeniden doğrulandı:
`TraconServiceCollectionExtensions.BindRunRecording`
(`TraconServiceCollectionExtensions.cs:1799-1801`) artık
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

## MT-UIRUN-034 — Yeniden oynatma sonrası Karşılaştırma paneli OTOMATİK açılır; her alan doğru tarafa yazılır

**Gerçek sonuç**
`MT-UIRUN-027`'nin doküman düzeltmesinde üretilen DB-kökenli replay
(`manuel-destek` v6→v6, kaynak `019ffcec-d2ce-725c-bd0d-5cccaadb51fd`, yeni
`019ffcf4-150b-7b90-829e-199527691758`) kullanıldı — `support` ile 027/028
`400` döndüğünden hiç yeni run açılmadı, bu case'in ancak DB-kökenli bir
replay ile test edilebileceği doğrulandı. Yeni run sayfası açılınca
"Karşılaştırma" paneli OTOMATİK göründü (hiç tıklama gerekmedi). Tablo
BİREBİR dokümanın 9 satırını taşıyor: Durum (Completed/Completed), Sürüm
(6/6), Model (gpt-5.4-mini/gpt-5.4-mini), Süre (1983ms/2684ms — SAĞ yeni),
Token (250/422), Maliyet (—/—, `Tracon:Providers:OpenAI:PricingTable`
yapılandırılmamış), Tool çağrısı (1/1), Hata sınıfı (—/—), Puanlar (0/0).
"Çıktı" bölümünde `DiffView` iki cümleyi satır satır kırmızı/yeşil
işaretledi: SOL `"ORD-1001 siparişiniz kargoya verilmiş. Tahmini teslim
süresi: 2 gün."`, SAĞ `"ORD-1001 kargoya verilmiş. Tahmini teslim: 2 gün."`
— modelin ikinci turda kısalttığı ifade fark olarak doğru yakalandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIRUN-039 — Geçmiş/Ham Durum sekmeleri arası geçiş; ham durum `JsonView` ile gösterilir

**Gerçek sonuç**
Adım 2 (Ham Durum) BİREBİR doğru: `stateNotice` metni + `JsonView` içinde
`stateBag`/`toolApprovalState`/`Tracon.ChatHistory` JSON'u.

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
testleri: `src/Tracon.UI/frontend/src/lib/transcript.test.ts`
(`foldMessages` — düz metin turlarında ayrı item, ÇAĞRI+SONUÇ farklı
mesajlarda birleşiyor, eşleşmeyen sonuç sessizce yutuluyor, boş dizi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIRUN-040 — 🚨 Bellek içi kalıcılıkta oturum geçmişi HER ZAMAN `null` döner; Dallandırma da `501` ile başarısız olur

**Gerçek sonuç**
`Tracon__PostgreSql__ConnectionString=""` ile uygulama yeniden
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
UYGULANDI: `Tracon__PostgreSql__ConnectionString` geri kondu, uygulama
yeniden başlatıldı, `/api/meta` `storage.persistent:true` ile doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIRUN-041 — "Çalıştırmalar" düğmesi doğru sayıyı taşır ve filtrelenmiş listeye gider

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
`src/Tracon.UI/frontend/src/lib/format.test.ts`
(`splitTarget` — yol/sorgu ayrımı, `matchRoute`'a beslendiğinde eşleşme) ·
`tests/Tracon.Ui.E2ETests/UiTests.cs`
`Oturum_sayfasindaki_calistirmalar_dugmesi_filtrelenmis_listeye_gider`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Dallandırma (`branch-button.tsx`, Faz 47)

---

## MT-UIRUN-043 — Dallanan oturumdan devam etmek eski oturumu DEĞİŞTİRMEZ (kopyalama, taşıma değil)

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

## MT-UIRUN-044 — 🚨 Ekranda yalnız MESAJ-bazlı "Dallandır" vardır; API'nin "tüm oturumu dallandır" seçeneğinin ekranda giriş noktası YOKTUR

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
