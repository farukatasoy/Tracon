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
