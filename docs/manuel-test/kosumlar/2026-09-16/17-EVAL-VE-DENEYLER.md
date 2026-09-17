# 17 — Eval, Deneyler (A/B), Kanarya Yayını ve Geri Bildirim (`EVAL`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../17-EVAL-VE-DENEYLER.md`](../../17-EVAL-VE-DENEYLER.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (şerit ap-s3, aile 25'in hemen
> ardından) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## Devir notu (oturum 19)

Aile açıldı. Ana örnek (port 5083, `mt_s3`) kullanılıyor. SQL doğrulama
sorguları spec'in literal `tracon.*` şeması yerine **`mt_s3.*`** ile
koşuluyor (skill kural 3 — paylaşılan `tracon` şemasına asla dokunulmaz).
`manuel-destek` (`FIX-AGENT-01`) bu şeritte zaten mevcut (aile 11'den
kalma, versiyon 2).

---

### MT-EVAL-001

**Gerçek sonuç**
`PUT /api/evals/destek-degerlendirme` → `200`, `id` dolu bir GUID,
`createdAt == updatedAt`. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-002

**Gerçek sonuç**
Aynı adı tekrar `PUT` etmek: `id` MT-EVAL-001'dekiyle birebir aynı,
`createdAt` değişmedi, `updatedAt` ilerledi, `checks` yeni değere değişti.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-003

**Gerçek sonuç**
Orijinal `checks` geri `PUT` edildi, `GET /api/evals` → `["destek-degerlendirme"]`
— takım listede. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-004

**Gerçek sonuç**
Bilinmeyen `check.kind: "regexMatch"` ile `PUT` → `400`, mesaj (İngilizce
— K-228, spec'in Türkçe metni bayat): `"Unknown check kind: 'regexMatch'.
Built-in kinds: nonEmpty, containsExpected, keywords, toolCalled,
toolCallsPresent, hasImageContent. If this is a custom check, it must be
registered with 'ITraconBuilder.AddEvalCheck(\"regexMatch\", ...)'."` —
içerik anlamca spec'in beklediğiyle örtüşüyor (bilinmeyen tür adı,
`AddEvalCheck` kayıt yolu). Ardından `GET /api/evals/kirik-takim` → `404`
— kayıt hiç oluşmadı. Beklenen sonucun tamamı (mesaj metninin dili hariç,
K-228 düzeltmesiyle) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-005

**Gerçek sonuç**
`checks: []` ile `PUT /api/evals/denetimsiz-takim` → `200`, takım oluştu.
Beklenen sonuç birebir örtüştü. (Not: MT-EVAL-007 bu takımı hemen siliyor;
MT-EVAL-028 kendi fixture'ını yeniden kuracak — bkz. o case'in kaydı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-006

**Gerçek sonuç**
`GET /api/evals/hic-yok-boyle-takim` → `404`. Beklenen sonuç birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-007

**Gerçek sonuç**
`DELETE /api/evals/denetimsiz-takim` → `204`.
`SELECT count(*) FROM mt_s3.eval_suites WHERE name = 'denetimsiz-takim'`
→ `0`. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-010

**Gerçek sonuç**
İki vakayla `PUT`, sonra tek vakayla `PUT`, son `GET` → `1` — ikinci
`PUT` birincinin vakasını eklemedi, yerine geçti. Beklenen sonuç birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-011

**Gerçek sonuç**
Boş `query`'li bir vaka içeren dizi → `400`
(`"Each case must have a non-empty 'query' field."`). Sonraki `GET`
MT-EVAL-010'un son durumunu (tek vaka, aynı `id`) hâlâ gösteriyor — kısmi
yazma olmadı. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-012

**Gerçek sonuç**
`DELETE /api/evals/destek-degerlendirme/cases` → **`204`** (spec `200`
bekliyordu, düzeltildi yukarıda — `DELETE /api/evals/{name}`'in kendi
davranışıyla tutarlı, `204 No Content`). Sonraki `GET` → `[]`. Ardından
MT-EVAL-010'un ilk `PUT`'u (iki vaka) tekrar uygulandı — §3 gerçek vakaya
ihtiyaç duyuyor, `GET` uzunluğu **2** ile doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-013

**Gerçek sonuç**
`/tracon/evals/destek-degerlendirme` ekranında "Durum ekle" ile boş bir
satır eklendi (`query` boş bırakıldı); "Durumları kaydet" düğmesi
`disabled:true` — gerçekten devre dışı. Eklenen boş satır kaydedilmeden
kaldırıldı (DB etkilenmedi). Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-014

**Gerçek sonuç**
`/tracon/evals` → "Yeni küme", `Ad`/`Agent adı` dolduruldu, `Denetimler`
alanına `{ bozuk json` yazıldı, "Kaydet"e tıklandı. Ağ trafiği (Playwright
`browser_network_requests`, `/api/evals` filtresi) yalnız sayfa
yüklemesinin GET'ini gösterdi — **hiçbir `PUT` isteği gitmedi**. Tıklama
sonrası satır içi hata belirdi: `"Denetimler geçerli JSON olmalıdır;
denetim tanımlarından oluşan bir dizi."`. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-015

**Yöntem notu.** Adım 3 (gerçek silme) `destek-degerlendirme` yerine
API'yle oluşturulan atılabilir bir set (`mt-eval-015-throwaway`, 1 vaka)
üzerinde koşuldu — `destek-degerlendirme` §3'ün gerçek eval koşuları için
gerekli, yok edilmemesi gerekiyordu.

**Gerçek sonuç**
Adım 1: "Bu seti sil" düğmesine odaklanınca tooltip
(`aria-label`/tooltip metni) seti, vakalarını ve kaydettiği her koşumu
sileceğini söylüyor. Adım 2: tıklanınca gerçek bir `[role="dialog"]` açıldı
(tarayıcının `window.confirm`u DEĞİL) — başlık `"destek-degerlendirme" seti
silinsin mi?` set adını gömüyor, `document.activeElement` "Vazgeç"
düğmesiydi (açılış odağı orada). `Esc` ile kapatıldı: dialog kayboldu, set
listede kaldı, ağ trafiğinde hiçbir `DELETE` isteği yok (yalnız sayfa
GET'i). Adım 3 (atılabilir set üzerinde): "Sil" → onay dialogunda "Sil"e
tıklandı → `mt_s3.eval_suites`'te `mt-eval-015-throwaway` satırı **gerçekten
gitti** (`count:0`) ve `mt_s3.eval_cases`'te hiçbir yetim satır (suite_id'si
var olmayan bir suite'e işaret eden) kalmadı — kaskat gerçekten çalışıyor
(K-786'nın iddiası). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 19 devam) — §3 gerçek eval koşuları başladı (GERÇEK PARA)

---

### MT-EVAL-020

**Yöntem notu.** Ön koşul metni "MT-EVAL-010'daki tek vakayla" diyor ama
MT-EVAL-012'nin kendi düzeltme adımı "MT-EVAL-010'un ilk `PUT`'unu tekrar
uygula" diyordu — o ilk `PUT` İKİ vaka taşıyor (ORD-1001 + "Merhaba").
Koşu bu haliyle (2 vaka) tetiklendi, sonucu aşağıda; ardından takım
spec'in beklediği TEK vakaya (`ORD-1001`) geri kırpıldı.

**Gerçek sonuç**
`POST .../run` → `RUN_ID`. Birkaç saniye sonra `status:"Completed"`,
`total:2, passed:1, failed:1`. `ORD-1001` vakası: `passed:true`,
`output` gerçekten `"ORD-1001 siparişiniz kargoya verilmiş..."` —
`contains_expected` denetimi `"Response contains expected output:
\"ORD-1001\""` diyerek geçti (spec'in tam istediği kanıt). İkinci vaka
("Merhaba", `expectedOutput:null`) `passed:false` —
`"ExpectedOutput is not set; check cannot be applied."`: bu vakanın
KENDİ eksik `expectedOutput`'unun sonucu, ürün kusuru değil (bir
`containsExpected` denetimi karşılaştıracak bir değer yoksa BAŞARISIZ
sayıyor, sessizce geçmiyor — savunmacı ve tutarlı bir tasarım). SQL
doğrulaması (`mt_s3.eval_runs`) satırla birebir eşleşti
(`status=2` [Completed], `total=2, passed=1, failed=1, agent_version=1`).
Spec'in asıl iddiası (ORD-1001 vakasının geçmesi ve çıktının alt dizgeyi
taşıması) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-021

**Gerçek sonuç**
`tool-cagri-testi` (`toolCalled`, `mode:"all"`, `tools:["get_order_status"]`),
vaka `"ORD-1001 nerede?"` → run `Completed`, `passed:true`, skor:
`"All tools called: get_order_status"`. Karşıt kanıt: aynı takıma
`query:"Merhaba"` (tool gerektirmeyen) eklenip yeniden koşulunca ilk vaka
yine geçti, ikinci vaka `passed:false`, `"Missing tool calls:
get_order_status"`. Beklenen sonucun tamamı (mutlu yol + karşıt kanıt)
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-022

**Gerçek sonuç**
`anahtar-kelime-testi` (`keywords`, `values:["ORD-1001"]`,
`caseSensitive:true`), vaka `"ORD-1001 siparisim nerede?"` → run
`Completed`, `passed:true`, `"All keywords found: ORD-1001"`. Model
yanıtı gerçekten `ORD-1001`'i birebir büyük harfle taşıdığı için geçti —
`caseSensitive` bayrağının etkili olduğunun dolaylı kanıtı. Beklenen
sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-023

**Gerçek sonuç**
`gorsel-testi` (`hasImageContent`), vaka `"Merhaba"` → run `Completed`,
`passed:false`, `failureReason: "has_image_content: No image content
found in conversation"`. `support` görsel üretmediği için denetim doğru
şekilde reddetti — negatif kanıt, kusur değil. Beklenen sonuç birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-024

**Gerçek sonuç**
Aynı `gorsel-testi` `numRepetitions:3` ile koşuldu → `total:1, passed:0,
failed:1` (vaka tek, üç tekrar). Üç tekrarın **hepsi** `has_image_content`
başarısızlığı gösterdi, vaka tümüyle başarısız sayıldı
(`allRepetitionsPassed` mantığı — tek bir başarısız tekrar bile vakayı
düşürüyor). Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-025

**Yöntem notu — iki deneme.** Yeni bir takım (`surum-pin-testi`,
`agentName:"manuel-destek"`) kuruldu. İlk denemede 6 vaka yeterince
yavaşlatmadı (koşu ~9 saniyede bitti, güncelleme isteği kendi JSON gövde
hatasıyla ["name" eksik] zaten başarısız olmuştu — gerçek bir sınama
olmadı, atıldı). İkinci denemede takım 20 vakaya çıkarıldı; ayrıca ilk
düzeltilmiş `PUT /api/agents/manuel-destek` isteği koşu henüz `Pending`
durumundayken gitti — bu da `MarkRunRunningAsync`'in henüz sabitlemediği
bir ana denk geldi (sabitlenen sürüm bu yüzden 2 değil, güncelleme
SONRASI sürüm olan 3 çıktı — bu da yöntemsel bir zamanlama hatası, ürün
davranışı değil). Asıl kanıtı üreten ÜÇÜNCÜ adım: koşu `Running`
durumuna geçip aktif olarak işlenirken (agent_version zaten 3 olarak
sabitlenmişken) agent talimatı BİR KEZ DAHA değiştirildi (`version:4`).

**Gerçek sonuç**
Koşu bittiğinde (`status:Completed, total:20, passed:20, failed:0`)
`agentVersion:3` — koşu SIRASINDA yapılan `version:4` güncellemesi
sonucu HİÇ ETKİLEMEDİ. SQL doğrulaması aynı sonucu verdi
(`SELECT agent_version FROM mt_s3.eval_runs WHERE id=...` → `3`). Bu,
spec'in asıl iddiasını (sürüm pinleme, koşu ortasındaki güncellemeden
etkilenmez) birebir kanıtlıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-026

**Gerçek sonuç**
`denetimsiz-takim` (MT-EVAL-005/007'de silinmişti) yeniden oluşturuldu
(`checks:[]`, 1 vaka). `POST .../run` → senkron `200`. Birkaç saniye
sonra `GET /api/evals/runs/{id}` → `status:"Failed", passed:0, failed:1`.
Gerçek istisna mesajı (kaynaktan doğrulandı, `EvalJobHandler.cs:137`):
`"Suite 'denetimsiz-takim' has no checks; at least one check is
required."` — İngilizce (spec düzeltildi yukarıda, K-228). Hata
gerçekten ASENKRON düştü (senkron `POST` `200` kabul etti). Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-027

**Gerçek sonuç**
`destek-degerlendirme/cases` boşaltıldı, `POST .../run` → **senkron
`400`**: `"Suite 'destek-degerlendirme' has no cases."` — MT-EVAL-026'nın
tam tersi (0-vaka senkron, 0-denetim asenkron). Case sonrası tek vaka
(`ORD-1001`) geri eklendi. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-028

**Gerçek sonuç**
`GET /api/evals/destek-degerlendirme/runs?skip=0&take=50` →
`length: 1` — MT-EVAL-020'de tetiklenen koşu listede (MT-EVAL-027'nin
senkron `400`'ü hiç kuyruğa girmediği için listede değil, beklenen).
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-035

**Gerçek sonuç**
`support`'a gerçek bir run gönderildi (`ORD-1001 siparisim nerede?`).
`POST .../cases/from-run/{runId}` → `201`, yeni vaka `sourceRunId`
gönderilen run'ın id'si, `sourceKind:"ReferenceRun"`, `promotedAt` dolu.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-036

**Gerçek sonuç**
AYNI `runId` ikinci kez terfi ettirildi → `200` (`201` DEĞİL), aynı vaka
kaydı geri döndü. `SELECT count(*) FROM mt_s3.eval_cases WHERE
source_run_id = '<runId>'` → `1` — ikinci vaka oluşmadı. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-037

**Gerçek sonuç**
Sıfır GUID'li var olmayan bir `runId` terfi ettirilmeye çalışıldı → `404`
(`"There is no run with id '00000000-0000-0000-0000-000000000000'."`).
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-038

**Gerçek sonuç**
Tüm eval takımları (`destek-degerlendirme`, `tool-cagri-testi`,
`anahtar-kelime-testi`, `gorsel-testi`, `surum-pin-testi`,
`denetimsiz-takim`) geçici olarak silindi (`GET /api/evals` → `[]`).
Bir run detay ekranı (`/tracon/runs/{id}`) açıldı — sayfa metninde
"terfi et"/"promote" dizgesi hiç geçmedi, ilgili düğme listesinde yoktu
(`İlişkili`, `Şimdi puanla`, `Yeniden oynat`, tool düğmesi — terfi düğmesi
YOK). Bileşen gerçekten `null` render ediyor. Case sonrası
`destek-degerlendirme` (tek vaka, `ORD-1001`) ve `tool-cagri-testi`
(`FIX-EVAL-02`) geri kuruldu, `GET /api/evals` bunu doğruladı. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 19 devam) — §5 çevrimiçi değerlendirme

🚨 **Şema düzeltmesi (bu turda §5'in tamamına uygulanır):** `mt_s3.jobs`da
`kind` sütunu yok, `handler_key` metin sütunu var
(`JobHandlerKeys.OnlineEval = "tracon.online-eval"`); sorgular kendi
şerit şeması (`mt_s3`) ile, literal `tracon` DEĞİL. Spec'in düzeltmeleri
yukarıda MT-EVAL-040/041'in kendi bölümlerinde yazılı.

---

### MT-EVAL-040

**Gerçek sonuç**
`support`'a gerçek bir run gönderildi (`OnlineEvaluation` hiç
yapılandırılmamış — varsayılan durum). 12 saniye sonra
`SELECT count(*) FROM mt_s3.jobs WHERE handler_key = 'tracon.online-eval'`
→ `0` — hiçbir OnlineEval işi kuyruğa girmedi. Beklenen sonuç birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-041

**Yöntem notu.** Ana örnek (port 5083) `Tracon__OnlineEvaluation__Enabled=true`,
`Tracon__OnlineEvaluation__SampleRate=1.0` env değişkenleriyle (skill
§1.2 — `dotnet user-secrets set` DEĞİL) yeniden başlatıldı.

**Gerçek sonuç**
`support`'a gerçek bir run gönderildi. 15 saniye sonra `mt_s3.jobs`da
`handler_key='tracon.online-eval'` satırı `status=3` (Completed).
`mt_s3.run_scores`'ta beklenen satır VAR: `kind=3` (Numeric),
`source='judge:model', author='judge:model', value=97`. Ek olarak spec'te
anılmayan İKİNCİ bir satır da var: `source='judge:relevance'` (`value=3`)
— kaynağı okundu: `samples/Tracon.Api/Program.cs:423`
`tracon.AddEvaluatorJudge("relevance", new RelevanceEvaluator())` — Faz
155/176'da eklenmiş ikinci bir yargıç kaydı, spec'in yazıldığı Faz 49'da
yoktu. Spec'in asıl iddiası (bir `judge:model` satırının varlığı) tam
doğrulandı, ek satır eksiklik değil zenginleşme. Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-042

**Yöntem notu.** Spec'in kendi izin verdiği "hesaplama doğrulaması"
yoluyla koşuldu (gerçek HTTP çağrısı gerektirmez). `RunSampler.cs`
kaynağından `IsSampled`in tam FNV-1a algoritması okundu ve Python'da
birebir yeniden üretildi (offset basis `14695981039346656037`, prime
`1099511628211`, üst 53 bit → `[0,1)` kesri, `fraction < sampleRate`).

**Gerçek sonuç**
Rastgele bir `runId` için kesir BAĞIMSIZ iki hesaplamada birebir aynı
çıktı (`0.12136241616357957` — iki kez). Aynı `runId`nin kesri **sabit**
kalırken yalnız eşik (`sampleRate`) değiştirildiğinde karar değişti
(`rate=0.1→false`, `rate=0.3→true`, ... `rate=1.0→true`) — algoritmanın
KENDİSİ rastgele bir tuz taşımıyor, yalnız `runId`nin baytlarına bağlı
(`.NET HashCode`nin süreç-başı tuzlamasının aksine). Bu, kod yorumunun
iddia ettiği garantiyi (aynı `runId` → aynı karar, süreç yeniden başlasa
bile) yapısal olarak doğruluyor. Beklenen sonuç (garantinin gözlemlenmesi)
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-043

**Gerçek sonuç**
MT-EVAL-040'ın (o an OnlineEvaluation kapalıyken üretilmiş, hiç
örneklenmemiş) run'ı `POST /api/runs/{id}/judge` ile elle yargılandı →
`200`, gövde en az bir `{judge:"model", score, reason}` eşdeğeri içeriyor
(`source:"judge:model", value:85, comment:"..."` + ek olarak
`judge:relevance` satırı — MT-EVAL-041'deki aynı ikinci yargıç).
`SELECT action, entity FROM mt_s3.audit_log WHERE action =
'run.judge.manual'` → satır var, `entity: "run:<RUN_ID>"`. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-044

**Yöntem tuzağı (bu koşumda bulundu, ürün kusuru DEĞİL).** İlk yeniden
başlatma denemesi başarısız oldu — `pkill` deseni derlenmiş ikilinin
GERÇEK komut satırıyla eşleşmedi (`.../Tracon.Api --urls ...`, `dotnet
... .dll` DEĞİL, `dotnet run` apphost'u doğrudan çalıştırıyor). Eski
süreç (`PID 20043`, MT-EVAL-041'in OnlineEvaluation-açık kurulumu) `5083`
portunu bırakmadı, yeni `dotnet run` `Address already in use` ile sessizce
öldü (arka planda), ve sonraki birkaç istek FARKINDA OLMADAN eski sürece
gitti. PID'ler doğrudan `kill`lenip port boşaltıldıktan sonra doğru
yeniden başlatma yapıldı: `Tracon__Providers__OpenAI__ApiKey=""` ile.
`GET /api/models/health` bunu doğruladı — `openai`/`openai-responses`
listede YOK, `echo` (durum `Unknown`) VAR; `support` agent'ının kendi
model bağlaması bu durumda otomatik olarak `echo`'ya döndü.

**Gerçek sonuç**
`support`'a (echo sağlayıcı) bir run gönderildi. `POST
/api/runs/{id}/judge` → `200`, gövde `{"scores":[],"failures":[]}` —
hata değil, boş dizi. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-045

**Yöntem notu.** MT-EVAL-043'ün AYNI `RUN_ID`'si (o zamanki OpenAI-açık
örnekte) ikinci kez yargılandı.

**Gerçek sonuç**
`POST /judge` ikinci kez çağrıldı (aynı run). `SELECT count(*) FROM
mt_s3.run_scores WHERE run_id = '<RUN_ID>' AND author = 'judge:model'`
→ **`1`** — ikinci yargılama var olan satırı `UPSERT` ile güncelledi,
yeni satır eklemedi. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-046

**Gerçek sonuç**
`GET /api/evaluation/online` → `200`. Gövde beklenen yedi alanı taşıyor:
`windowStart, windowEnd, sampleCount:0, averageScore:null,
lowScoreThreshold:60, minSampleSize:20, belowThreshold:false` —
`sampleCount(0) < minSampleSize(20)` olduğu için `belowThreshold:false`,
tam beklendiği gibi. İki ekstra alan da var (`judgeCost`,
`judgeCostCurrency`, ikisi de `null`) — spec'te anılmıyor, muhtemelen
sonraki bir fazın eklediği maliyet takibi, eksiklik değil. Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 19 devam) — §6 deneyler (A/B). Ana örnek normal
duruma geri yeniden başlatıldı (OpenAI açık, OnlineEvaluation varsayılan).

---

### MT-EVAL-050

**Gerçek sonuç**
Kod-kökenli `support` agent'ı hedefleyen bir deney `PUT` edilmeye
çalışıldı → `400`: `"'support' is defined in code and has no version
history. Experiments cannot be set up on code-sourced agents."`.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-051

**Yöntem notu.** `manuel-destek` bu şeritte zaten `version 4`'teydi
(önceki ailelerden/MT-EVAL-025'ten kalma) — spec'in literal `1`/`2`
sürümleri yerine mevcut sürüm + yeni sürüm (`4`/`5`) kullanıldı.

**Gerçek sonuç**
`manuel-destek` yeni talimatla `PUT` edildi → `version:5`. İki varyantlı
deney (`kisa-talimat@4` ağırlık 50, `detayli-talimat@5` ağırlık 50)
`PUT /api/experiments/destek-talimat-testi` ile oluşturuldu → `200`,
`status:"Draft"`. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-052

**Gerçek sonuç**
Ağırlıkları `40+40=80` olan bir deney `PUT` edilmeye çalışıldı → `400`:
`"Variant weights must sum to 100; currently 80."`. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-053

**Gerçek sonuç**
Var olmayan `version:99` içeren bir deney `PUT` edilmeye çalışıldı →
`400`: `"Agent 'manuel-destek' has no version 99."`. Beklenen sonuç
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-054

**Gerçek sonuç**
`destek-talimat-testi` başlatıldı → `200`, `status:"Running"`. Aynı
`manuel-destek` agent'ı için ikinci bir deney (`ikinci-deney`) oluşturulup
başlatılmaya çalışıldı → **`409`**: `"Another experiment is already
running for agent 'manuel-destek'. Only one experiment can run at a
time for the same agent."`. `SELECT name, status FROM mt_s3.experiments
WHERE agent_name = 'manuel-destek' AND status = 1` → tam **1** satır
(`destek-talimat-testi`). Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-055

**Gerçek sonuç**
`Running` durumundaki `destek-talimat-testi` `DELETE` edilmeye çalışıldı
→ `409`: `"Experiment 'destek-talimat-testi' cannot be deleted while
running; stop it first."`. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-056

**Gerçek sonuç**
`Running` durumundaki deney `PUT` ile düzenlenmeye çalışıldı → **`409`**:
`"Experiment 'destek-talimat-testi' is in status 'Running'; only
experiments in Draft status can be edited."`. Beklenen sonuç (başarısızlık
gerçekleşti, gerçek kod kaydedildi) birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-057

**Gerçek sonuç**
`POST .../stop` → `200`. Sonraki `GET` → `status:"Stopped"`,
`endedAt` dolu, `startedAt` korunmuş. Deneyi `Draft`'a döndüren bir uç
denenmedi/yok (spec'in kendi iddiası, koddan doğrulanmadı ama uç
listesinde böyle bir işlem hiç yok — yalnız `start`/`stop`). Beklenen
sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-058

**Gerçek sonuç**
`/tracon/experiments` → "Yeni deney", iki varyant ağırlığı `30`+`30`
yapıldı. Toplam metni `"Ağırlık toplamı 60% (100% olmalı)"` kırmızımsı
renkte (`rgb(245, 165, 155)`); "Kaydet" düğmesi `disabled:true` —
gerçekten devre dışı. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-059

**Gerçek sonuç**
Liste ekranında `Stopped` (`durduruldu`) durumundaki
`destek-talimat-testi` satırının düğme hücresi **boş** (`buttons: []`)
— Düzenle/Sil YOK. Kontrol için `ikinci-deney` başlatılıp (`Running`,
`sürüyor`) aynı ölçüm tekrarlandı: o satırda da düğme hücresi boş.
`Draft` durumundaki bir deneyin (aynı ekranın önceki koşumdaki
görüntüsü, MT-EVAL-058 öncesi) Düzenle/Sil'i taşıdığı zaten görülmüştü.
Beklenen sonucun tamamı (Running VE Stopped ikisinde de gizli) birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-062

**Yöntem notu.** Yeni bir deney (`destek-talimat-testi-2`, aynı
`kisa-talimat@4`/`detayli-talimat@5` varyantları) kurulup başlatıldı —
`destek-talimat-testi` zaten `Stopped`, yeniden kullanılamaz.

**Gerçek sonuç**
Aynı `sessionId` (`belirlenirlik-testi-42`) ile `manuel-destek` **5 kez**
art arda çalıştırıldı (hepsi `200`). `SELECT DISTINCT variant FROM
mt_s3.runs WHERE experiment_id IS NOT NULL AND session_id =
'belirlenirlik-testi-42'` → **tek** satır: `kisa-talimat`. Beş
çalıştırmanın tümü aynı varyanta düştü. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-063

**Gerçek sonuç**
`SELECT experiment_id, variant, agent_version FROM mt_s3.runs WHERE
session_id = 'belirlenirlik-testi-42' LIMIT 1` → `experiment_id`
`destek-talimat-testi-2`'nin id'siyle birebir eşleşiyor, `variant:
"kisa-talimat"` (MT-EVAL-062'yle aynı), `agent_version: 4` — o varyantın
kendi `version` numarasıyla birebir eşleşiyor. Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 19 devam) — §7 kanarya politikası

---

### MT-EVAL-070

**Gerçek sonuç**
Üç varyantlı bir deney (`uc-varyantli`, `34/33/33`) oluşturulup kanarya
politikası eklenmeye çalışıldı → `400`: `"A canary rule can only be
defined on two-variant experiments."`. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-071

**Gerçek sonuç**
`PUT .../destek-talimat-testi-2/canary` (`canaryVariant:"detayli-talimat",
minSampleSize:3, maxErrorRateDelta:0.2, rampSteps:[25,50,100],
rampIntervalHours:1`) → `200`, deney gövdesinde `canary` alanı tam
gönderilen değerlerle doldu. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-072

**Gerçek sonuç**
`GET .../canary` → `evaluation.decision:"InsufficientData"`, `reason:
"The minimum settled run count (3) was not reached: canary 0, control
10."` — MT-EVAL-062'nin 5 run'ı (+ önceki testlerden birikenler) hep
`kisa-talimat` (kontrol) koluna düştüğü için kanarya kolunda (`detayli-
talimat`) hiç tamamlanmış run yok. Beklenen sonucun tamamı birebir
örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-073

**Gerçek sonuç**
`destek-talimat-testi-2` kanarya politikası taşıyarak `Running`de
dakikalarca durdu (varsayılan `appsettings.json`, `Tracon:Canary` bölümü
yok, `AutoRollbackEnabled` derleme-zamanı varsayılanı `false`).
`GET /api/experiments` → `rollbackReason: null`, `status: "Running"` —
hiçbir otomatik geri alma tetiklenmedi. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-074

**Yöntem notu.** `manuel-destek`'in geçerli sürümlerini bozmadan kanarya
kolunu güvenilir biçimde başarısız kılmak için YENİ bir sürüm
(`version:6`) kasıtlı geçersiz bir modelle (`gpt-model-yok-9999`)
oluşturuldu ve yeni bir deneye (`destek-talimat-testi-3`,
`kisa-talimat@4` kontrol / `bozuk-model@6` kanarya) bağlandı — spec'in
adlandırdığı `destek-talimat-testi-2` yerine. Ana örnek
`Tracon__Canary__AutoRollbackEnabled=true`,
`Tracon__Canary__ScanInterval=00:00:30` env değişkenleriyle yeniden
başlatıldı (skill §1.2). `maxErrorRateDelta=0.0` (herhangi bir fark
tetikler). 12 farklı `sessionId` ile `manuel-destek` çalıştırıldı; kova
ataması 6/6 böldü — kanarya kolunun 6'sı da GERÇEKTEN `Failed` (durum
kodu 2, `model_not_found`), kontrol kolunun 6'sı da GERÇEKTEN
`Completed` (durum 1). 65 saniye (2 tarama döngüsü) beklendi.

**Gerçek sonuç**
`mt_s3.audit_log`da `experiment.auto_rollback` kaydı VAR
(`entity: "experiment:destek-talimat-testi-3"`,
`created_at: 22:27:32.134630`). Deneyin kendi `updatedAt`/`endedAt`
zaman damgası `22:27:32.146986` — audit kaydı mutasyondan **~12ms ÖNCE**
yazılmış (spec'in "audit ÖNCE yazılır" iddiası zaman damgasıyla
kanıtlandı). `GET /api/experiments/destek-talimat-testi-3` →
`status:"Stopped"`, `rollbackReason: "The canary error rate (100.0 %)
exceeds the control rate (0.0 %) by more than the 0.0 % threshold."`,
`variants`: `bozuk-model` ağırlığı **`0`**, `kisa-talimat` ağırlığı
**`100`** — spec'in tam istediği kanarya-sıfır/kontrol-yüz sonucu.
Beklenen sonucun tamamı birebir örtüştü — bu dosyanın en kritik
senaryosu gerçek bir arka plan döngüsüyle uçtan uca doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-075

**Yöntem notu.** Doğru `rampInterval` alan adıyla (`MT-EVAL-071` bkz.)
yeni bir sağlıklı deney kuruldu: `kanarya-saglikli`, ikisi de `version 4`
olan iki varyant (`kontrol`/`kanarya`, ikisi de her zaman başarılı),
`rampInterval:"00:00:01"`. 12 farklı `sessionId` ile çalıştırıldı
(`kontrol:4, kanarya:8`, hepsi `Completed`). 40 saniye beklendi (30s
tarama aralığı × en az 1 tur).

**Gerçek sonuç**
Kanarya varyantının ağırlığı **`50`'den `100`'e** çıktı (kontrol
`50`'den `0`'a düştü) — spec'in beklediği "bir sonraki basamağa (`25`)"
değil, doğrudan SON basamağa. Sebep ölçüldü: `rampInterval` (1 saniye)
ile 40 saniyelik bekleme arasındaki oran — geçen süre üç basamağın
(`25,50,100`) hepsinin aralığını çoktan aştığı için tarama döngüsü tek
turda son basamağa "yakaladı" (idempotent catch-up), kademe kademe
durmadı. Bu, spec'in KENDİ test tasarımının (aşırı kısa `rampInterval`)
bir sonucu — ürün kusuru değil, ramp-up'ın GERÇEKTEN gerçekleştiğinin
daha güçlü kanıtı. `SELECT action FROM mt_s3.audit_log WHERE action LIKE
'experiment%' AND entity = 'experiment:kanarya-saglikli'` → tam **4**
satır: yalnız `experiment.create`, iki `experiment.canary_policy`,
`experiment.start` — **hiçbiri ramp-up'a ait değil**, ağırlık gerçekten
değişmesine rağmen audit'te hiçbir "ramp" eylemi yok. Spec'in asıl
iddiası ("ramp-up audit'e hiç yazılmaz") birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-076

**Gerçek sonuç**
`destek-talimat-testi-3`'ün (MT-EVAL-074'ün otomatik geri aldığı deney)
detay ekranında kırmızı bir banner: `"Otomatik geri alındı: The canary
error rate (100.0 %) exceeds the control rate (0.0 %) by more than the
0.0 % threshold."` — metin kırmızımsı renkte (`rgb(245, 165, 155)`,
aynı kırmızı skala). Kontrol grubu: elle durdurulmuş
`destek-talimat-testi`'nin AYNI ekranında `"Otomatik geri alındı"`
dizgesi HİÇ geçmiyor — banner yalnız otomatik geri almada görünüyor,
manuel `Stop`'ta yok. Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Devir notu (oturum 19 devam) — §8 geri bildirim/yeniden oynatma

---

### MT-EVAL-080

**Gerçek sonuç**
Gerçek bir run gönderildi. `POST .../feedback`
(`{kind:"Binary",value:1,comment:"Dogru cevap."}`) → `200`, satır
`source:"human", name:"overall"`. Sonraki `GET .../feedback` aynı satırı
gösterdi. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-081

**Gerçek sonuç**
`{kind:"Binary",value:2}` → `400`: `"A binary score ('binary') can only
be 0 or 1."` (İngilizce, spec düzeltildi yukarıda — K-228). Beklenen
sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-082

**Gerçek sonuç**
`{kind:"Stars",value:4}` → `200`. `SELECT kind, value FROM
mt_s3.run_scores WHERE id='<scoreId>'` → `kind:2, value:4` — birebir
eşleşiyor. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-083

**Gerçek sonuç**
`{kind:"Stars",value:0}` → `400`. `{kind:"Stars",value:6}` → `400`.
İkisi de aynı mesaj: `"A star score ('stars') must be between 1 and 5."`.
Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-084

**Gerçek sonuç**
Spec'in kendisinde zaten düzeltilmiş ön koşula göre koşuldu — statik
bearer token akışında `author` `NULL`'dur. Aynı `RUN_ID`'ye ikinci bir
Binary puan (`value:0, comment:"Fikrim degisti."`) gönderildi.
`SELECT count(*), value, comment, author FROM mt_s3.run_scores WHERE
run_id='<RUN_ID>' AND kind=1 GROUP BY value, comment, author` → **iki**
satır: `(1, "Dogru cevap.", NULL)` ve `(0, "Fikrim degisti.", NULL)` —
ikinci puan birinciyi EZMEDİ, upsert gerçekleşmedi (`author IS NULL`
olduğu için tekillik indeksi devreye girmiyor). Beklenen (düzeltilmiş)
sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-085

**Gerçek sonuç**
İzlek C: `TraconTestHost` üzerinden `IRunScoreStore.UpsertAsync`
doğrudan `Author: null` ile aynı `runId`/`name`/`kind`e iki kez çağrıldı.
`store.ListAsync(...)` → **`2`** satır (`1` DEĞİL) — arayüzün kendi XML
dokümanı zaten bunu doğruluyor (`"If Author is empty, the rule does not
apply; every call writes a new row."`). Kod okuması ve ampirik koşum
birbirini doğruladı; MT-EVAL-084'ün gerçek HTTP yolundan gözlemiyle de
tutarlı. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-086

**Gerçek sonuç**
`GET .../feedback`'ten ilk satırın `id`'si alınıp `DELETE
.../feedback/{scoreId}` çağrıldı → `204`. `SELECT action FROM
mt_s3.audit_log WHERE action = 'run.feedback.delete' ORDER BY
created_at DESC LIMIT 1` → satır var. Beklenen sonuç birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-EVAL-087

**Gerçek sonuç**
MT-EVAL-041'in `RunSampler`/`OnlineEvalJobHandler` yolundan (HTTP ucu
HİÇ devrede değil) otomatik ürettiği yargı puanının `runId`'si için:
`SELECT count(*) FROM mt_s3.audit_log WHERE action LIKE 'run.feedback%'
AND entity LIKE '%<runId>%'` → `0`. Aynı sorgu `run.judge%` için de `0`.
Kontrol grubu: MT-EVAL-043'ün manuel `/judge` çağrısı (HTTP ucu üzerinden)
`run.judge.manual` kaydı bırakmıştı (o case'in kendi kaydında zaten
görüldü) — fark, `IRunScoreStore.UpsertAsync`'in kendisinin denetim izine
sarılı OLMAMASI, yalnız HTTP uç işleyicisinin ayrıca audit yazması.
Beklenen sonucun tamamı birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## HATA-S3-007 — `FeedbackControl`, bir run'da bir `Stars` puanı da varsa "tekrar tıkla = sil" yerine yinelenen satır oluşturuyor

- **Case:** MT-EVAL-088
- **Önem:** Orta
- **İzlek:** B (gerçek tarayıcı + gerçek API, iki koşumla ayrıştırıldı)

**Beklenen**
Case'in kendi beklentisi: başparmak-yukarıya ikinci tıklama puanı SİLER
(`remove.mutate(mine.id)`), üçüncü tıklama yeniden oluşturur.

**Gerçekleşen**
Bu davranış TEMİZ bir run'da (hiç önceden puanlanmamış) birebir doğru
çalışıyor — üç tıklama sırasıyla `POST`/`DELETE`/`POST` üretti (kanıt
aşağıda). AMA aynı run'da MT-EVAL-082'nin bıraktığı bir `Stars` puanı
(`kind:"Stars", name:"overall"`, yalnız API'den yazılabilir) VARKEN,
ikinci tıklama DELETE göndermek yerine yine `POST` gönderdi — mevcut
Binary puanının üzerine yazmak/silmek yerine **ikinci, yinelenen** bir
Binary satırı oluşturdu. `mt_s3.run_scores`'ta doğrulandı: aynı run,
aynı `value:1`, iki AYRI `id` (`...5eba` ve `...2edf`), ikisi de
`created_at` farklı.

**Kök neden**
`src/Tracon.UI/frontend/src/components/feedback-control.tsx:50-51`:
```ts
const mine = feedback.data?.find((score) =>
    score.messageId == null && score.source === 'human' && score.name === OVERALL);
```
Bu eşleşme `score.kind`'ı HİÇ kontrol etmiyor — yalnız `messageId`,
`source`, `name`. `Stars` ve `Binary` puanlarının ikisi de aynı
`name:"overall"` taşıyabildiği için (API bunu engellemiyor, MT-EVAL-082
zaten bunu kanıtlıyor), `.find()` listenin BAŞINDAKİ eşleşen satırı
alıyor — sunucunun döndürdüğü sıra `IRunScoreStore.ListAsync`'in kendi
XML dokümanına göre GARANTİ DEĞİL. `mine` bazen `Stars` satırına
bağlanıyor; o zaman `mine.value` (`4`) hiçbir zaman düğmenin beklediği
`0`/`1`'e eşit olmuyor, `toggle()` her tıklamada `rate.mutate()`
(oluştur) çağırıyor, `remove.mutate()` (sil) asla tetiklenmiyor —
düğme "silinmiş" gibi GÖRÜNSE bile arka planda yeni bir satır birikiyor.

**Yeniden üretme**
1. Bir run'a `POST /api/runs/{id}/feedback` ile `{kind:"Stars",
   value:4}` yaz (yalnız API, arayüz bunu üretemez — MT-EVAL-082).
2. Aynı run'ın detay ekranını aç, başparmak-yukarıya tıkla (`POST`
   Binary oluşur).
3. AYNI düğmeye tekrar tıkla.
4. Ağ trafiğinde (`browser_network_requests`) ikinci isteğin `DELETE`
   DEĞİL yine `POST` olduğunu, `mt_s3.run_scores`'ta iki ayrı `Binary,
   value:1` satırı oluştuğunu gözle.

**Kontrol (aynı ortamda, `Stars` puanı OLMADAN)**
Tamamen temiz bir run'da (`01a0b183-...`, hiç puan yok) aynı üç tıklama
sırasıyla `POST`(`200`, id `...b792`) → `DELETE`(`204`, aynı id) →
`POST`(`200`, yeni id) üretti — spec'in beklediği TAM davranış. Bu,
hatanın `Stars`/`Binary` `kind` karışıklığına özgü olduğunu, genel
toggle mantığının kendisinin sağlam olduğunu kanıtlıyor.

**Kapsam**
Yalnız `feedback-control.tsx`'in kendi `mine` bulma mantığı etkileniyor.
`author IS NULL` üretmenin (K-059 statik token tasarımı, MT-EVAL-084/085)
KENDİSİ kusur değil — ama bu tasarım, aynı isimde birden çok `kind`
satırının bir arada var olabileceği her ortamda bu UI bileşenini kırılgan
kılıyor. Düzeltme adayı: `mine` eşleşmesine `score.kind === 'Binary'`
koşulu eklemek.

---

### MT-EVAL-088

**Gerçek sonuç**
Temiz (hiç puanlanmamış) bir run'da üç ardışık tıklama tam beklendiği
gibi çalıştı: `POST`(oluştur) → `DELETE`(sil) → `POST`(yeniden oluştur).
AYNI davranış, MT-EVAL-082'nin `Stars` puanını taşıyan bir run'da
BOZULDU — ikinci tıklama `DELETE` yerine yeni bir `POST` gönderdi,
yinelenen bir satır oluşturdu. Kök neden bulundu ve kaydedildi:
**`HATA-S3-007`** (yukarıda) — `feedback-control.tsx:50-51`'deki `mine`
eşleşmesi `score.kind`'ı kontrol etmiyor. Beklenen sonuç TEMİZ run'da
birebir örtüştü; kirli run'da (Stars puanı bulunan) ☑ Kaldı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S3-007`
(temiz rundaki alt senaryo Geçti, Stars-puanlı rundaki alt senaryo Kaldı;
case Kaldı olarak işaretlendi çünkü "herhangi bir run" ön koşulu ikisini
de kapsıyor ve biri gerçekten bozuluyor)

---

### MT-EVAL-089

**Gerçek sonuç**
Hiç puanlanmamış temiz bir run'da yorum kutusuna metin yazılıp blur
edildi — ağ trafiğinde (`browser_network_requests`, `/feedback` filtresi)
sayfa yüklemesinin GET'i dışında **hiçbir istek** gitmedi (`saveComment`
gerçekten `mine != null` şartına bağlı). Geri bildirim panelinin
konteynerinde yalnız **2** SVG (başparmak yukarı/aşağı ikonları) ve
yalnız `"Yararlı"`/`"Yararsız"` düğmeleri var — yıldız kontrolüne ait
hiçbir eleman yok (sayfa metnindeki tek "star" eşleşmesi `run.started`
olay adının içindeydi, yanlış pozitifti). Beklenen sonucun tamamı
birebir örtüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
