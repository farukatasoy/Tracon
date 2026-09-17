# 21 — Dayanıklılık: Çalıştırma İptali, Öksüz Uzlaştırma ve Asenkron Onay Kutusu (`RES`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../21-DAYANIKLILIK-VE-IPTAL.md`](../../21-DAYANIKLILIK-VE-IPTAL.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (şerit ap-s3, family 34'ün hemen
> ardından) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## Ortam notu (oturum başı)

- `Tracon:Providers:OpenAICompatible:openrouter:ApiKey` bu şeritte **boş
  bırakıldı** — startup sırasında bu anahtar dolu iken uygulama
  `OptionsValidationException: OpenAIProviderOptions.Endpoint is required for
  compatible providers` ile çöküyordu, oysa `appsettings.json`'da
  `Tracon:Providers:OpenAICompatible:openrouter:Endpoint` **doludur**
  (`https://openrouter.ai/api/v1`) ve bağımsız bir .NET konfigürasyon
  reprodüksiyonu (aynı `appsettings.json` + aynı ortam değişkenleri, yalnız
  JSON + EnvironmentVariables sağlayıcılarıyla) `Endpoint`'i doğru okudu. Kök
  neden bu oturumda **kesinleştirilemedi** — büyük olasılıkla bu şeride özgü
  bir DI/Options zamanlama sorunu ya da bu ortamın kendine özgü bir durumu
  (`nohup` ile arka planda başlatma, env aktarımı). Bu ailenin case'lerinden
  hiçbiri `openrouter` sağlayıcısını kullanmadığı için anahtar boş bırakılarak
  ilerlendi; `HATA-S3` numarası açılmadı (yeniden üretilemedi, kapsam dışı).
  Not `docs/manuel-test/00-INDEKS.md`'ye taşınmadı — yalnız bu oturumun kaydı.

## Devir notu (oturum 16 · devam ediyor — MT-RES-028'den itibaren)

---

## § 1 — Çalıştırma İptali: Kayıt Defteri ve Sınır Durumları

### MT-RES-001

**Gerçek sonuç**
`router` → `support` alt-çalıştırması yakalandı (`Running` iken kök iptal
edildi). Kök `POST /cancel` → `202`, gövde `status:"Running"` (eski, K-uyumlu).
3 sn sonra: kök `Canceled`, alt `Canceled`. `GET /api/audit/run:{root}}`
tek kayıt taşıyor: `action:"run.cancel"`, `entity:"run:{root}"`. `GET
/api/audit/run:{child}` **boş dizi** — ikinci bir audit yazımı yok. Beklenen
sonucun tamamı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-002

**Gerçek sonuç**
Yalnız alt-çalıştırma (`support`) doğrudan iptal edildi. 3 sn sonra: alt
`Canceled`, kök **hâlâ `Running`** (durmadı). 11 sn'de kök normal akışıyla
`Completed` oldu — iptalden etkilenmedi. Beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-003

**Gerçek sonuç**
`Running` dalı: `POST /cancel` gövdesi `status:"Running"` (eski/okunmuş kayıt)
— nihai durum ayrı `GET` ile görülüyor. `Queued` dalı (`Prefer:
respond-async`): `POST /cancel` gövdesi `status:"Canceled"` (güncel kayıt).
İki dalın gövde tazeliği FARKLI — beklenen sonuçla birebir örtüşüyor. Not:
`support` agent'ı bu ortamda çok hızlı (gpt-5.4-mini, ~1-3 sn); "Running"
penceresini yakalamak için tekrarlayan ("lorem ipsum...") metin yerine
benzersiz rastgele metin (prompt cache'i devre dışı bırakmak için) ve
Python alt-süreç başlatmadan salt `grep`/`cut` ile durum kontrolü gerekti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-004

**Gerçek sonuç**
`router` çalıştırması `Running` iken süreç `kill -9` ile öldürüldü (DB satırı
`Running` kaldı, registry kayboldu). Süreç yeniden başlatıldıktan sonra aynı
`runId`'ye `POST /cancel` → **`409`**, `title: "Run is not executing on this
instance"`, `detail`: `"...shows as 'Running' but is not registered in this
process. It may be running on a different instance, or the process may have
restarted mid-run."` — `MT-RES-003`'ün "already ended" 409'undan (`title:
"Run already ended"`) FARKLI bir `title` taşıyan ikinci bir `409` yolu; kodun
Türkçe metniyle aynı anlam, ama gerçek koşumda İngilizce (`en`, varsayılan
locale) metinler üretti — beklenen davranışla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-005

**Gerçek sonuç**
`summarize-and-translate` workflow'u `Running` iken `POST /cancel` → `202`.
3 sn sonra `GET /api/runs/{id}` → `status:"Canceled"`, `error:null`, `kind:
"Workflow"`. Faz 32 kapanışındaki eski beklenti (`Completed`, MAF grafik-içi
iptal sınırı) DEĞİL — 2026-08-18 güncellenmiş beklenti (F-107/K-432)
doğrulandı: `WorkflowRunner` iptali kendisi zorluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-006

**Gerçek sonuç**
İptal edilmiş bir `run` (`MT-RES-001`'in kök çalıştırması) için `error:
null`. `GET /api/stats/errors?hours=1` **boş dizi** döndü — `"Canceled"`
sınıfı taşıyan hiçbir küme yok, birden fazla gerçek iptal olmuş olmasına
rağmen. Şüphe DOĞRULANDI: `RunErrorClass.Canceled` pratikte hiç üretilmiyor
— **kusur değil, ölü kod** (spesifikasyonun kendi tanısıyla örtüşüyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

> **Ortam notu:** Bu şeyin `zsh` kabuğunda çalıştığı doğrulandı (`ps -p $$`).
> Çok kelimeli `$PG` değişkeni `zsh`'de word-split OLMAZ (`bash`'in aksine) —
> `$PG -c "..."` tek bir komut adı gibi yorumlanıp `command not found` verir.
> Bundan sonraki tüm SQL çağrıları `docker exec -i ap-pg psql -U postgres -d
> tracon -c "..."` biçiminde doğrudan yazıldı (ya da bir `pgq()` kabuk
> fonksiyonuyla).

## § 2 — Uzlaştırma: Varsayılan Kapalı / Açık (Faz 54)

### MT-RES-010

**Gerçek sonuç**
`RunReconciliation` ayarlarının HİÇBİRİ verilmemiş (varsayılan kapalı). Bir
tamamlanmış çalıştırma SQL ile "10 dakika önce başlamış, hâlâ Running" hâline
getirildi. 10 sn sonra `GET /api/runs/{id}` hâlâ `status:"Running"` —
beklenen sonuçla birebir örtüşüyor (hiçbir arka plan taraması satırı kapatmıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-011

**Gerçek sonuç**
`RunReconciliation` açık (`Enabled=true`, `HeartbeatInterval=1sn`,
`OrphanThreshold=3sn`, `ScanInterval=1sn`), uygulama bu ayarlarla yeniden
başlatıldı. `MT-RES-010`'un aynı satırı yeniden "10 dakika önce başlamış"
hâline getirildi. 5 sn içinde: `status:"Failed"`, `error.type:"orphaned"`,
`error.class:"Infrastructure"`, `error.fingerprint:"orphaned"`,
`error.message`: `"The process running this run is not responding; last
heartbeat: 2026-09-17 00:46:28.083501+00."` — beklenen sonucun tamamı
(dört alan + mesaj biçimi) birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-012

**Gerçek sonuç**
`Prefer: respond-async` ile kuyruğa alınan bir çalıştırmanın `started_at`'i
geçmişe alındı, `status` DEĞİŞTİRİLMEDİ (`Queued` kaldı SQL güncellemesi
anında). Uzlaştırma bu satıra dokunmadı: 5 sn'de işçi normal şekilde aldı
(`Running`), birkaç saniye sonra `Completed` oldu — hiçbir noktada
`Failed`/`Infrastructure` olmadı. Beklenen sonuçla birebir örtüşüyor
(`WHERE status = 0` filtresi `Queued`'a dokunmuyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-013

**Gerçek sonuç**
`GET /api/runs?status=Running` → çıplak dizi (`{items:[...]}` DEĞİL),
uzunluk **0**. `GET /api/stats/errors?hours=1` → `class:"Infrastructure"`
kümesi var, `totalRuns:3`, `topClusters[0].fingerprint:"orphaned"`,
`sampleMessage` İngilizce metin taşıyor (`"...is not responding..."` —
spesifikasyonun beklediği `"...yanit vermiyor..."` Türkçe örneği DEĞİL,
sunucunun varsayılan `en` locale'i; anlam aynı). Adım 3 (dashboard ekran
görüntüsü) bu oturumda AYRICA doğrulanmadı — aynı bileşen `12-
GOZLEMLENEBILIRLIK-MALIYET.md` `MT-OBS-010`'da zaten görsel olarak
kanıtlanmış (sınır tablosu); burada yalnız API sözleşmesi tekrar ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-014

**Gerçek sonuç**
Zaman bütçesi bu ailenin geri kalan 40+ case'i için dar; case'in kendisi
"iki terminal ister, zaman bütçesi dar ise `⏭ Atlandı` işaretlenip gerekçe
not düşülebilir, geri kalan case'lerin hiçbiri buna bağımlı değildir" diyor.
Bu ortamda zaten dört ayrı şerit (`ap-s1..s4`) portları meşgul ediyor; ikinci
bir `support`/PostgreSQL örneği başka bir portta (`5085`+) başlatılabilirdi
ama bu ailenin geri kalanına hiçbir bağımlılığı yok. Atlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: kod
tarafından zaten opsiyonel işaretlenmiş, iki terminal/örnek gerektirir,
sonraki case'ler bağımsız.

---

## § 3 — Asenkron Onay Kutusu (Faz 55)

### MT-RES-020

**Gerçek sonuç**
Spesifikasyonun kendi örneği `sessionId` GÖNDERMİYORDU; bununla istek
`Failed` düşüyor (`TraconException`: onay kararının bir sonraki turun girdisi
olduğu, oturumsuz çözülemeyeceği net mesajıyla — kusur değil, tasarım kısıtı).
Spesifikasyon `sessionId:"mt-res-020"` eklenerek düzeltildi (yukarıda not
düşüldü). Düzeltilmiş istekle: 9 sn içinde `status:"AwaitingApproval"`.
`GET /api/approvals/pending`'de tam kayıt: `toolName:"cancel_order"`,
`arguments:"orderId=ORD-1001"` (JSON değil, `anahtar=deger`), `status:
"Pending"`, `expiresAt` `createdAt`'ten ~24 saat sonra. `presentation` alanı
DOLU: `entityType:"order"`, `entityId:"ORD-1001"`, `entityName:"Order
ORD-1001"`, `message:"Cancel order ORD-1001 for Priya Shah."` — beklenen
sonucun tamamı (Faz 142 dahil) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-021

**Gerçek sonuç**
`POST /api/approvals/{id}/decide {"approved":true}` → `200`,
`status:"Approved"`, `decidedBy`/`decidedAt` dolu. ESKİ `runId` 3 sn sonra
HÂLÂ `AwaitingApproval` (K-014 doğrulandı, hiç değişmedi). Aynı `sessionId`
altında YENİ bir `runId` (`Queued` → `Completed`, ~10 sn). Yeni koşunun tool
listesinde `cancel_order` GERÇEKTEN çalıştı: `result:"Order ORD-1001 has been
canceled."`, `succeeded:true`. Beklenen sonucun tamamı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-022

**Gerçek sonuç**
Yeni bir `ORD-1002` onayı `{"approved":false}` ile reddedildi → `200`,
`status:"Rejected"`. Yeni koşu (`mt-res-022` oturumu) `Completed` oldu; tool
listesinde `cancel_order`'ın `result:"Tool call invocation rejected."` —
sipariş GERÇEKTEN iptal edilmedi (senkron `ToolApprovalResolver` reddetme
davranışıyla aynı ilke). Beklenen sonuçla örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-023

**Gerçek sonuç**
`MT-RES-021`'in ONAYLANMIŞ onayına TERS kararla (`{"approved":false}`) ikinci
`decide` → **`409`**, `title:"Decision already made"`, `detail:"...is no
longer pending."` — beklenen sonuçla birebir örtüşüyor. `GET
/api/runs?sessionId=...` sayısı değişmedi.

**Ek gözlem (kusur değil, kasıtlı tasarım — kod incelemesiyle doğrulandı):**
`ApprovalEndpoints.DecideAsync` (satır 199-217) AYNI kararın tekrarını
kasıtlı olarak `409` DEĞİL `200` ile idempotent kabul eder ve `ResumeAsync`'i
yeniden çalıştırır (run kimliği onaydan türetildiği için tekrar aynı işi
kuyruğa sürer, ikinci bir run AÇILMAZ) — yorum satırı gerekçeyi açıkça
anlatıyor: "audit ve run-kuyruğa-alma arasında hiçbir şey yok, arada çökme
onayı sonsuza kilitler; yalnız TERS bir tekrar çakışmadır." İlk denemede
(yanlışlıkla `MT-RES-022`'nin ZATEN `false` reddedilmiş onayına yine `false`
gönderildiğinde) bu yol tetiklendi ve `200` döndü — spesifikasyonun
"ikinci karar HER ZAMAN 409" ifadesi bu nüansı kapsamıyor, ama test edilen
asıl senaryo (ters karar) doğru şekilde `409` veriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-024

**Gerçek sonuç**
Rastgele `guid` ile `GET /api/approvals/{id}` → `404`. `POST .../decide` →
`404`, `title:"Approval request not found"`, `detail:"There is no pending
approval request with id '...', or it does not belong to this tenant."` —
beklenen sonuçla örtüşüyor (varlığı sızdırmayan tek `404` yolu, kiracı
kontrolü de aynı mesajda anılıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-025

**Gerçek sonuç**
`kiraci-alfa` altında yeni bir onay üretildi (`sessionId:"mt-res-025"`).
`kiraci-beta` başlığıyla `GET /api/approvals/{id}` → `404`; aynı başlıkla
`decide` → `404`. Kontrol: `kiraci-alfa` başlığıyla `GET` → `200`. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-026

**Gerçek sonuç**
`MT-RES-025`'in onayı `kiraci-alfa` başlığıyla onaylandı (`200`,
`status:"Approved"`). `GET /api/audit/tool:cancel_order` tek kayıt döndü:
`action:"approval.decision"`, `entity:"tool:cancel_order"`, `after:
"{\"approved\":true,\"approvalId\":\"...\"}"` — beklenen sonucun tamamı
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-027

**Gerçek sonuç**
`Tracon:Approvals:DefaultExpiration=00:00:05`, `ScanInterval=00:00:02` ile
uygulama yeniden başlatıldı. Yeni bir onay üretildi (`mt-res-027`). 10 sn
sonra `GET /api/approvals/{id}` → `status:"Expired"`. `decide` denemesi →
`409`. Spesifikasyonun beklemediği ek bir gözlem: bu kadar kısa bir
`DefaultExpiration` ile **run'ın kendisi de** `Failed` düştü —
`error.type:"ApprovalExpired"`, `error.message:"The approval request for
tool 'cancel_order' expired at ..."` (async job onayı beklerken süresi
dolduğunda `AgentRunJobHandler` çalıştırmayı başarısız kapatıyor). Bu bir
kusur DEĞİL — spesifikasyonun ölçtüğü üç iddia (Expired durumu, pending
listede yokluk, ikinci `decide`'ın 409'u) birebir doğrulandı; yalnız çok
kısa süre sonu ile ek bir yan etki (run'ın da başarısız kapanması) gözlendi.
Not: `pending` listesinde daha önceki bir oturumdan kalma, `default`
kiracılı, alakasız bir kayıt (`sessionId:"mt-res-025"`, 24 saatlik varsayılan
süreyle) hâlâ görünüyordu — bu ORTAM ARTIĞI, bu case'in ölçtüğü onayla
ilgisi yok, doğal süresinde (2026-09-18) kendiliğinden dolacak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-028

**Gerçek sonuç**
Spesifikasyonun kendi gerekçesi (`RequireApiKeyScope` çağrılmıyor iddiası)
bu koşumda **doğrulanmadı** — kaynakta üç çağrı bulundu (satır 37, 54, 67).
`RunsRead` kapsamıyla üretilen anahtarla `decide` → `403`,
`title:"Insufficient scope"`, `detail:"...requires the 'RunsWrite'
scope..."`. Kontrol grubu (`PUT /api/agents/...`) → `403`,
`detail:"...requires the 'AgentsAdmin' scope..."`. Boşluk zaten kapanmış —
kusur değil (spesifikasyon başlığı ve "Beklenen sonuç" yukarıda
düzeltildi). Ayrı bir doküman düzeltmesi: API anahtarı yanıtının alan adı
`rawKey` DEĞİL `plaintextKey`'dir — `Girilecek veri` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-029

**Gerçek sonuç**
Case'in kendi metni bu senaryonun bu ortamda gözlemlenemeyeceğini zaten
söylüyor (tek statik bearer token, rol claim'i üreten ayrı bir kimlik
doğrulama şeması yok). Doğrulama amaçlı yine de çağrıldı: statik token ile
`decide` → beklenen gibi `200`/kapsam engeline TAKILMADAN geçti (RunsWrite
kapsamı statik tokende zımnen var kabul ediliyor). Gerekçesiyle atlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: ortamda
"Reader" rolünü temsil eden ayrı bir kimlik yok, case'in kendi metni bunu
belirtiyor.

---

### MT-RES-030

**Gerçek sonuç**
Gövdede `approvals` alanı taşıyan `Prefer: respond-async` isteği → `400` —
beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-040

**Gerçek sonuç**
İki eski bekleyen onay (bir tanesi `mt-res-025`'in erken denemesinden kalma
`default` kiracılı artık, diğeri `MT-RES-028`'in kapsam testinden) önce
kararlandırılarak liste boşaltıldı. "Onaylar" ekranında `Empty` bileşeni
göründü: "Nothing is waiting" başlığı + "A queued run only appears here
when a tool call needs approval and no live client can answer it." gövdesi.
Konsolda 2 hata VAR ama İKİSİ de bu case'e YABANCI: (1) bilinen `HATA-S2-002`
ile aynı CSP inline-script engeli (erken tema boyama, işlevsel etkisi yok),
(2) token girilmeden ÖNCEki ilk yüklemede `/api/agents`'a giden `401` (kimlik
doğrulama akışının normal bir parçası). Beklenen sonuçla örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-041

**Gerçek sonuç**
Yeni bir bekleyen onay (`mt-res-041`) üretilip ekran yenilendi. Satırda:
`presentation.entityName` ("Order ORD-1001", kalın) üstte, altında
`cancel_order` (mono, subtle renk — kalın DEĞİL, spesifikasyonun "mono,
kalın" beklentisiyle KISMEN farklı), altında `presentation.message`
("Cancel order ORD-1001 for Priya Shah."). Ham argüman (`orderId=ORD-1001`)
spesifikasyonun beklediği gibi HER ZAMAN görünür kırpılmış metin DEĞİL,
bir `<details><summary>Raw arguments</summary>...</details>` açılır-kapanır
bileşeninin ARKASINDA (varsayılan kapalı) — kod incelemesiyle doğrulandı
(`<p class="font-semibold">Order ORD-1001</p><span class="font-mono ...">
cancel_order</span>...<details>...<summary>Raw arguments</summary>`).
Bu bir kusur DEĞİL — Faz 142'nin `presentation` alanı eklendiğinde arayüz
kasıtlı olarak öncelik sırasını değiştirmiş (insan-okunur ad/mesaj önde, ham
argüman ayrıntıda); spesifikasyon metni bu değişiklikten ÖNCE yazılmış,
aşağıda düzeltildi. `Run` hücresi kısaltılmış run id'ye + `mt-res-041`
oturum kimliğine bağlantı, "Created" hücresi göreli ("9 sec. ago"), "Expires"
hücresi MUTLAK ("Sep 18, 2026, 8:48:27 PM") — bu ikisi beklenen sonuçla
birebir örtüşüyor. Ağ sekmesinde ekran açıkken `pending`'e ardışık birden
fazla istek gözlendi (`refetchInterval` doğrulandı). Run bağlantısına
tıklanınca `/tracon/runs/{runId}`'ye gidildi, durum rozeti "awaiting
approval" — orijinal çalıştırma, beklenen sonuçla örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-042

**Gerçek sonuç**
`MT-RES-041`'in satırındaki Onayla düğmesine tıklanınca DOĞRUDAN istek
gitmedi — bir onay diyaloğu açıldı ("Approve cancel_order?" başlığı,
"Cancel"/"Approve" düğmeleri). Spesifikasyon bu ara adımı anmıyor; aşağıda
düzeltildi (kusur değil, ek onay katmanı). Diyaloğun Onayla düğmesine
tıklanınca istek gitti; tıklama-yanıt aralığı bu ortamda `busy` durumunu
yakalamaya izin vermeyecek kadar hızlıydı (`MT-RES-003`'ün aynı sınırı).
İstek dönünce satır listeden kalktı, ekran `Empty` durumuna döndü. `GET
/api/runs?sessionId=mt-res-041` ile doğrulandı: YENİ bir run (`...f086-
7b47...`) `Completed` oldu (`cancel_order` gerçekten çalıştı), ESKİ run
(`...e608-7e5f...`) hâlâ `AwaitingApproval` — beklenen sonucun ölçülebilir
kısmı (Adım 3) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-043

**Gerçek sonuç**
Yeni bir bekleyen onay (`mt-res-043`) üretilip Reddet düğmesine tıklandı.
`MT-RES-042`'deki aynı desenle bir onay diyaloğu açıldı ("Reject
cancel_order?"), diyaloğun kendi Reddet düğmesine tıklanınca istek gitti.
Satır listeden kalktı, ekran boş duruma döndü. `GET
/api/runs?sessionId=mt-res-043` ile doğrulandı: YENİ run `Completed`
(model reddi gördü), ESKİ run hâlâ `AwaitingApproval` —
`MT-RES-022`'nin HTTP davranışıyla birebir aynı, arayüzden de doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-RES-044

**Gerçek sonuç**
İki deneme yapıldı. İlk denemede (aynı karar dışarıdan curl ile
tekrarlandı) `MT-RES-023`'ün idempotency kuralı doğrulandı: diyalog sessizce
başarıya ulaştı (hata YOK) — bu, ikinci denemenin gerekçesidir. İkinci
denemede diyalog açıkken onay dışarıdan curl ile TERS kararla (`approved:
true`) kararlandırıldı, sonra diyaloğun kendi Reddet düğmesine tıklandı:
`409`, sunucu mesajı `"Decision already made: The approval request with id
'...' is no longer pending."` — bu mesaj İKİ yerde birden göründü: diyaloğun
içinde (`role="alert"`, açıklama metninin altında) VE sayfanın alt kısmında
(boş-durum panelinin altında, birebir aynı metin). Sayfa ÇÖKMEDİ, diyalog
"Reject" düğmesi tekrar tıklanabilir kaldı. Beklenen sonucun düzeltilmiş
hâliyle birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---


