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
