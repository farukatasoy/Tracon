# 21 — Dayanıklılık: Çalıştırma İptali, Öksüz Uzlaştırma ve Asenkron Onay Kutusu (`RES`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../21-DAYANIKLILIK-VE-IPTAL.md`](../../21-DAYANIKLILIK-VE-IPTAL.md) — `Ön koşul`, `Adımlar`,
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
> her case'in bloğu AYNEN durur. Tam metin:
> `git log --follow -- <bu dosya>`

---

## Temiz geçen case'ler (21)

| Case | Durum | Başlık |
|---|---|---|
| MT-RES-001 | ☑ | Kök çalıştırmanın iptali GERÇEKTEN çalışan bir alt çalıştırmayı da durdurur; audit YALNIZ kök için yazılır |
| MT-RES-002 | ☑ | Yalnız bir alt çalıştırmanın iptali kökü ve kardeşleri ETKİLEMEZ |
| MT-RES-010 | ☑ | Varsayılan kapalı: `Enabled=false` iken eski bir `Running` satır asla kapanmaz |
| MT-RES-011 | ☑ | Açıldığında: heartbeat'i geride bırakılan bir `Running` satır eşik aşılınca `Failed`/`Infrastructure`/`orphaned` olur |
| MT-RES-012 | ☑ | Uzlaştırma yalnız `Running` süzer; `Queued` bir satıra DOKUNMAZ |
| MT-RES-013 | ☑ | Uzlaştırma sonrası `GET /api/runs?status=Running` boş döner; panoda `Infrastructure` sınıfı görünür |
| MT-RES-020 | ☑ | Kuyruğa alınan bir çalıştırma onay gerektiren tool çağırınca `AwaitingApproval`'a düşer, `pending` listede görünür |
| MT-RES-021 | ☑ | Onaylama: YENİ bir `RunId` kuyruğa düşer, işçi alınca `Completed` olur; ESKİ çalıştırma SONSUZA `AwaitingApproval` kalır |
| MT-RES-022 | ☑ | Reddetme: karar reddedilirse yeni çalıştırma modelin reddi gördüğünü yansıtır |
| MT-RES-023 | ☑ | Aynı onaya ikinci karar `409` döner |
| MT-RES-024 | ☑ | Var olmayan onay kimliği `404` döner |
| MT-RES-025 | ☑ | Başka kiracının onayı `404` döner (varlığı sızdırmaz) |
| MT-RES-026 | ☑ | Karar `approval.decision` eylemiyle, `tool:{toolName}` varlığıyla denetim izine yazılır |
| MT-RES-027 | ☑ | Süresi dolan onay `Expired` olur |
| MT-RES-030 | ☑ | Ekler veya onay kararı ile birlikte gönderilen `respond-async` isteği `400` alır (kuyruklu çalıştırmada onay desteklenmez) |
| MT-RES-040 | ☑ | Bekleyen onay yokken Boş durum |
| MT-RES-041 | ☑ | Bekleyen onay satırı tool adı + argüman, run bağlantısı, oluşturulma/süre bilgisiyle görünür; 5 sn'de bir kendiliğinden yenilenir |
| MT-RES-042 | ☑ | Onayla düğmesi kararı uygular; satır listeden kalkar |
| MT-RES-043 | ☑ | Reddet düğmesi kararı uygular |
| MT-RES-044 | ☑ | Karar isteği başarısız olursa hata satırda gösterilir (panel altında, sayfa çökmez) |
| MT-RES-050 | ☑ | Uzlaştırmayla `Infrastructure` sınıfıyla kapanan bir çalıştırma yeniden oynatılabiliyor mu |

## Ayrıntı taşıyan case'ler (7)

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
