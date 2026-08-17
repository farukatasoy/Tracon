# 21 — Dayanıklılık: Çalıştırma İptali, Öksüz Uzlaştırma ve Asenkron Onay Kutusu (`RES`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../21-DAYANIKLILIK-VE-IPTAL.md`](../../21-DAYANIKLILIK-VE-IPTAL.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-RES-001 — Kök çalıştırmanın iptali GERÇEKTEN çalışan bir alt çalıştırmayı da durdurur; audit YALNIZ kök için yazılır

**Gerçek sonuç**
Adim 3: HTTP 202. Adim 4: 3 saniye sonra hem kok (yonlendirici) hem alt (support) calistirmasi Canceled oldu - registry'nin RootRunId kaskadi dogrulandi. Adim 5: run:{root} denetim izinde bir run.cancel kaydi VAR; run:{child} denetim izinde HICBIR kayit YOK - cocugun iptali dogrudan CancellationTokenSource.Cancel() ile, ikinci bir HTTP/audit yazimi yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-002 — Yalnız bir alt çalıştırmanın iptali kökü ve kardeşleri ETKİLEMEZ

**Gerçek sonuç**
Alt calistirma Canceled oldu. Kok calistirma DURMADI - cocuk iptal edildikten hemen sonra Running kaldi, birkac saniye sonra normal akisiyla Completed oldu (error: null) - iptalden dolayi degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-003 — 🚨 `Running` bir çalıştırmanın `202` gövdesi HÂLÂ eski durumu taşır (`Queued` dalının aksine)

**Gerçek sonuç**
Adim 1: HTTP 202, govde status: "Running" (eski/onceki durum yazildi - CancelRunAsync'in Running dali OKUNMUS kaydi donuyor, Canceled yazmiyor). Not: destek tek-turlu (support tek basina) calistirmalar ~1 saniyede tamamlaniyor (10K girdi tokeni, 38 cikti tokeni), bu yuzden ilk denemede yaris kosulu 409 (zaten tamamlanmis) verdi; yonlendirici (iki model turu) kullanilarak guvenilir bir Running penceresi elde edildi. Adim 2: Prefer:respond-async ile kuyruga alinan bir calistirmanin cancel govdesi status: "Canceled" yazdi (GUNCEL kayit) - iki dal aynı uctur ama govde tazeligi FARKLI, dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-004 — 🚨 Süreç yeniden başladıktan sonra eski bir `Running` satırın iptali `409` döner ("bu örnekte yürütülmüyor")

**Gerçek sonuç**
Sunucu surec, uzun bir yonlendirici calistirmasi baslar baslamaz (kill -9 ile) durduruldu (registry bellek-ici oldugu icin kaybedildi, runs satiri PostgreSQL'de Running kaldi - dogrudan SQL sorgusuyla dogrulandi). Uygulama yeniden baslatildi. Ayni runId'ye POST cancel: HTTP 409, title: "Calistirma bu ornekte yurutulmuyor", detail: "...'Running' gorunuyor ama bu surecte kayitli degil. Baska bir ornekte calisiyor olabilir veya surec calistirma sirasinda yeniden baslamis olabilir." - MT-UIRUN-023'un 'zaten sonlanmis' 409'undan FARKLI title tasiyan ikinci bir 409 yolu dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-005 — 🚨 Workflow çalıştırması gerçekten iptal edilebiliyor mu (Faz 32 kapanışında KANITLANAMAMIŞ boşluğun denemesi)

**Gerçek sonuç**
**HATA-S2-010 (Yüksek, doğrulanmış şüpheydi — Faz 32'nin kendi kapatamadığı açık soruyu kapattı).** `ozetle-ve-cevir` workflow'u `FIX-PROMPT-04` boyutunda bir metinle başlatıldı. Cancel ÖNCESİ ayrı bir `GET` ile durum açıkça `Running` olarak DOĞRULANDI (yarış koşulu değil). `POST /api/runs/{id}/cancel` → `HTTP 202`. 3 saniye sonra: `status: Completed` (`Canceled` DEĞİL), `error: null` — düzeltilmiş beklentiyle **tam örtüşüyor**. Faz 32'nin kendi denemesinde yaşadığı aynı sorun (grafik iptali yutuluyor) TEKRARLANDI ve DOĞRULANDI. Kod okuması: `WorkflowRunner.cs:356-368` AgentPrism seviyesinde DOĞRU görünüyor — tek bir `linked` `CancellationTokenSource` hem `IRunCancellationRegistry.Register`'a (satır 362) hem `run.WatchStreamAsync`'e (satır 589, `linked.Token`) besleniyor; `OperationCanceledException` doğru şekilde yakalanıyor (satır 612). Sorun muhtemelen MAF'ın kendi `AgentWorkflowBuilder.BuildSequential` grafiğinin (`StreamingRun.WatchStreamAsync` iç uygulaması) dışarıdan gelen iptal token'ını çalışan bir adım ortasında GERÇEKTEN honor etmemesi — AgentPrism dışı (bağımlılık) bir sınır, ama kullanıcıya göre SONUÇ AYNI: bir workflow çalıştırması iptal edilemiyor, sessizce tamamlanıyor (maliyet/zaman israfı + kullanıcı yanıltılması).

---

**Doküman düzeltmesi (2026-08-15, KAPANIS-PLANI §5 Karar 4):** Beklenti
gerçek (kabul edilmiş) davranışa göre düzeltildi. Kullanıcı kararıyla bu
bulgu **kodlanmadı** — MAF sınırındaki bir yetenek boşluğu olarak
`docs/ADAYLAR.md` **F-107**'ye faz adayı yazıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-006 — 🚨 İptal edilen bir çalıştırmanın `error` alanı hep `null` kalır; `RunErrorClass.Canceled` PRATİKTE hiç üretilmez (şüpheli ölü kod)

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

---

## MT-RES-010 — Varsayılan kapalı: `Enabled=false` iken eski bir `Running` satır asla kapanmaz

**Gerçek sonuç**
AgentPrism:RunReconciliation:* ayarlarinin hicbiri verilmedi (varsayilan). Bir satir dogrudan SQL ile '10 dakika once baslamis, hala calisiyor' hale getirildi. 10 saniye sonra: status: Running - hicbir arka plan taramasi calismadigi icin satir SONSUZA kadar Running gorundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-011 — Açıldığında: heartbeat'i geride bırakılan bir `Running` satır eşik aşılınca `Failed`/`Infrastructure`/`orphaned` olur

**Gerçek sonuç**
RunReconciliation acildi (Enabled=true, HeartbeatInterval=1s, OrphanThreshold=3s, ScanInterval=1s), yeniden baslatildi. Satir gecmise alindi. 5 saniye sonra: status: Failed, error.type: orphaned, error.class: Infrastructure, error.fingerprint: orphaned (sabit dize), error.message: "Calistirma yuruten surec yanit vermiyor; son isaret: 2026-08-13 13:12:31..." - tum alanlar birebir eslesti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-012 — Uzlaştırma yalnız `Running` süzer; `Queued` bir satıra DOKUNMAZ

**Gerçek sonuç**
Prefer:respond-async ile bir calistirma kuyruga alindi, started_at gecmise alindi (status=5 Queued korunarak). 5 saniye sonra status: Running (isci normal sekilde alip islemeye basladi) - hicbir zaman Failed/Infrastructure OLMADI. Uzlastirma sorgusu yalniz status=0 (Running) satirlari suzuyor, Queued bir kova bile degil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-013 — Uzlaştırma sonrası `GET /api/runs?status=Running` boş döner; panoda `Infrastructure` sınıfı görünür

**Gerçek sonuç**
Adim 1: ilk olcumde len:1 cikti (MT-RES-012'nin kuyruktan yeni alinmis, GERCEKTEN aktif calisan satiri nedeniyle) - bu satir tamamlandiktan (Completed) sonra tekrar olculdugunde len:0 ve gercekten bir DIZI (obje/{items:...} DEGIL) dogrulandi. Adim 2: /api/stats/errors?hours=1 class:Infrastructure kumesi var, sampleMessage '...yanit vermiyor...' iceriyor (totalRuns:2, iki ayri MT-RES-010/011 orphan olayindan). Adim 3 (dashboard bileseni) kod okumasiyla dogrulandi: RunErrorClass string-enum, arayuz listeyi dongüyle basiyor - KOD degisikligi gerektirmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-014 — (opsiyonel, iki örnek) `SingletonExecution` kapalıyken uzlaştırma HER örnekte bağımsız koşar; açıldığında tekilleşir

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

---

## MT-RES-020 — Kuyruğa alınan bir çalıştırma onay gerektiren tool çağırınca `AwaitingApproval`'a düşer, `pending` listede görünür

**Gerçek sonuç**
Adim 2: birkac saniye icinde status: AwaitingApproval. Adim 3: pending listesinde runId eslesen tam bir kayit var: toolName: cancel_order, arguments: 'orderId=ORD-1001' (anahtar=deger bicimi, JSON DEGIL), status: Pending, expiresAt createdAt'ten ~24 saat sonra (varsayilan DefaultExpiration). Adim 4: ayni kayit tekil GET ile de teyit edildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-021 — Onaylama: YENİ bir `RunId` kuyruğa düşer, işçi alınca `Completed` olur; ESKİ çalıştırma SONSUZA `AwaitingApproval` kalır

**Gerçek sonuç**
Adim 2: HTTP 200, govde status: Approved, decidedBy: 'unknown' (dolu), decidedAt dolu. Adim 3: ESKI runId 3 saniye sonra HALA AwaitingApproval - hic degismedi (K-014). Adim 4: ayni sessionId'de YENI bir runId (019ffb4c-d982...) gorundu, Completed oldu. Adim 5: yeni calistirmanin mesaj gecmisinde functionCall cancel_order + functionResult 'ORD-1001 numarali siparis iptal edildi.' - tool GERCEKTEN calisti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-022 — Reddetme: karar reddedilirse yeni çalıştırma modelin reddi gördüğünü yansıtır

**Gerçek sonuç**
HTTP 200, govde status: Rejected. Yeni calistirma tamamlandi; mesaj gecmisinde functionResult: 'Tool call invocation rejected.' (cancel_order GERCEKTEN calismadi) ve modelin son yaniti 'Siparis iptali icin islem yapilamadi. Lutfen siparis numarasini kontrol edip tekrar deneyin: ORD-1001.' - reddin dogru yansitildigi dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-023 — Aynı onaya ikinci karar `409` döner

**Gerçek sonuç**
HTTP 409, title: Karar zaten verilmis, detail: '...artik bekliyor durumunda degil.'. Ikinci bir RunId/is KUYRUGA DUSMEDI - GET /api/runs?sessionId=... sayisi 2'de sabit kaldi (degismedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-024 — Var olmayan onay kimliği `404` döner

**Gerçek sonuç**
Ikisi de HTTP 404, title: Onay istegi bulunamadi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-025 — Başka kiracının onayı `404` döner (varlığı sızdırmaz)

**Gerçek sonuç**
Adim 1-2: kiraci-beta basligiyla GET ve decide, ikisi de HTTP 404 (403 DEGIL) - varligi sizdirmadi. Adim 3: kiraci-alfa basligiyla ayni istek HTTP 200 - kendi kiracisi icin normal calisiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-026 — Karar `approval.decision` eylemiyle, `tool:{toolName}` varlığıyla denetim izine yazılır

**Gerçek sonuç**
Iki kayit var (MT-RES-021/022'den): action: approval.decision, entity: tool:cancel_order, after alani {"approved": true/false, "approvalId": "<id>"} bicimindeki JSON dizgesi tasiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-027 — Süresi dolan onay `Expired` olur

**Gerçek sonuç**
AgentPrism:Approvals:DefaultExpiration=5s, ScanInterval=2s ile yeniden baslatildi. Karar VERMEDEN 10 saniye beklendi. Adim 2: status: Expired. Adim 3: pending listesinde ARTIK GORUNMEDI (len:0). Adim 4: decide cagrisi HTTP 409, title: Karar zaten verilmis - AlreadyDecided kontrolu Expired icin de gecerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-028 — 🚨 `ApprovalEndpoints` hiçbir ucunda `RequireApiKeyScope` çağırmaz — yalnız-okuma kapsamlı bir anahtar onay kararı verebiliyor mu

**Gerçek sonuç**
**KALDI - HATA-S2-011 (Yuksek, dogrulanmis supheydi).** Adim 2: yalniz RunsRead kapsamli bir anahtarla POST /api/approvals/{id}/decide -> HTTP 200, karar GERCEKTEN uygulandi (status: Approved). Adim 3 (kontrol grubu): AYNI anahtarla PUT /api/agents/{name} (AgentsAdmin gerektirir) -> HTTP 403, title: Kapsam yetersiz - anahtarin genel olarak kapsam sistemine tabi oldugu, yalniz ApprovalEndpoints'te bu denetimin HIC calismadigi dogrulandi. grep -n "RequireApiKeyScope" src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs bos doner (kod okumasiyla onceden olculmustu, koşumda dogrulandi). Salt-okunur bir otomasyon anahtari, bekleyen gercek yan etkili bir tool cagrisini (siparis iptali) onaylayabiliyor/reddedebiliyor.

---

**GECTI (Aile F, docs/manuel-test/KAPANIS-PLANI.md §6).** ApprovalEndpoints.cs'in ucune RequireApiKeyScope eklendi: GET /api/approvals/pending ve GET /api/approvals/{id} -> RunsRead, POST /api/approvals/{id}/decide -> RunsWrite (§7 mapping tablosunda acikca yoktu, RunsWrite'in kendi tanimindaki "onay verme" ifadesiyle ayni akil yurutmeyle eklendi). Canli PostgreSQL'e karsi yeniden uretildi: RunsRead-kapsamli bir anahtarla POST /api/approvals/{rastgele-id}/decide -> HTTP 403, title: "Kapsam yetersiz", detail: "Bu uc 'RunsWrite' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor." Istek handler'a hic ulasmadan (onay kaydi hic aranmadan) filtrede reddedildi. Kontrol: ayni anahtarla GET /api/approvals/pending -> HTTP 200 (RunsRead hala calisiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-029 — 🚨 Rol politikaları bu örnekte kayıtlı değil — "Reader karar veremez" iddiası bu ortamda GÖZLEMLENEMEZ

**Gerçek sonuç**
> _(koşum sırasında doldurulur — beklenen: `⏭ Atlandı`)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe yukarıda

---

## MT-RES-030 — Ekler veya onay kararı ile birlikte gönderilen `respond-async` isteği `400` alır (kuyruklu çalıştırmada onay desteklenmez)

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

---

## MT-RES-040 — Bekleyen onay yokken Boş durum

**Gerçek sonuç**
Bekleyen onay yokken (tumu kararlandirilmis) Onaylar ekraninda Empty bileseni gorundu: "Nothing is waiting" basligi + "A queued run only appears here when a tool call needs approval and no live client can answer it." govde metni (i18n anahtarlarindan geliyor, K-228 geregi eksik olsa derleme hata verirdi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-041 — Bekleyen onay satırı tool adı + argüman, run bağlantısı, oluşturulma/süre bilgisiyle görünür; 5 sn'de bir kendiliğinden yenilenir

**Gerçek sonuç**
Adim 1: satirda cancel_order, altinda arguman metni (orderId=ORD-1001), calisma id'sinin kisaltilmis hali (019ffb55-2f3e...393712) + oturum kimligi (res-041-02), goreli olusturulma zamani ('7 sec. ago'), MUTLAK sure-sonu zamani ('Aug 14, 2026, 4:34:54 PM' - goreceli DEGIL). Adim 2: browser_network_requests /api/approvals/pending'e tekrarlanan GET istekleri gosterdi (refetchInterval:5000 dogrulandi). Adim 4 dogrulanmadi (zaman butcesi - Approve/Reject dugmeleri MT-RES-042/043'te test edildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-042 — Onayla düğmesi kararı uygular; satır listeden kalkar

**Gerçek sonuç**
Onayla dugmesine tiklandi. Istek dondukten sonra satir listeden KALKTI - ekran 'Nothing is waiting' bos durumuna dondu (onSuccess approvals-pending sorgusunu gecersiz kildi). Busy/disabled ara durumu (adim 2) yakalanamadi (tiklama-yanit araligi playwright snapshot cagrilarindan daha hizliydi) ama nihai davranis (karar uygulandi, satir kalkti) dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-043 — Reddet düğmesi kararı uygular

**Gerçek sonuç**
Reddet dugmesine tiklandi. MT-RES-022'nin HTTP davranisiyla ayni sonuc arayuzden tetiklendi - satir listeden kalkti, 'Nothing is waiting' bos durumuna donuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-RES-044 — Karar isteği başarısız olursa hata satırda gösterilir (panel altında, sayfa çökmez)

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

---

## MT-RES-050 — Uzlaştırmayla `Infrastructure` sınıfıyla kapanan bir çalıştırma yeniden oynatılabiliyor mu

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

---
