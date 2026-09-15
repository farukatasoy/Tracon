# Faz 172 — Tehdit Modeli

> **Durum:** ✅ Tamamlandı (2026-09-15)
> **Plan onayı:** onaylandı (2026-09-15) — açık sorular önerilen seçeneklerle kapatıldı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-235**
> **Önkoşul:** Yok. Sınır envanteri tüketici geri bildirimi turunda (2026-09-15) üretildi
> **Paketler:** Yok — bu faz kod yazmaz
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** site: yeni `reference/threat-model.md`, `getting-started/security.md` (çapraz link), `reference/security-policy.md` (kapsam gerekçesi)
> · sevk edilen: Yok
> **Manuel test alanı:** Yok — doğrulama `dokuman-bakim.py` kapısıdır

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8b961ab4:docs/arsiv/fazlar/172-TEHDIT-MODELI.md
> ```
>
> Damıtıldı 2026-09-15 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon bir kontrol düzlemidir ve kendisi bir güvenlik sınırıdır. `SECURITY.md` kapsam içi sekiz alan sayar, site 14 sınır listeler — ama hiçbir yerde **saldırgan modeli** yoktur: kim, nereden, neyi hedefler ve hangi sınır onu durdurur. Kurumsal güvenlik incelemesinin ilk istediği belge budur. Bu faz o belgeyi yazar ve **bayatlamasını engelleyen bir kapı** kurar.

## Bitiş Ölçütleri (DoD)

- [x] `docs/MIMARI-TEHDIT-MODELI.md` var; 14 sınırın **hepsi** matriste bir hücreye bağlı
- [x] Kapanmayan her hücre "kabul edilen risk" olarak **adıyla** yazılmış (R1-R7, § 6)
- [x] `docs-site/.../reference/threat-model.md` var ve sidebar'dan erişiliyor (`Reference` bölümü)
- [x] `SECURITY.md` kapsam listesi modele link veriyor
- [x] Tazelik kapısı **iki yönlü** çalışıyor: eksik sınır ve fazla sınır ayrı ayrı kırmızı döndürüyor
- [x] Kapı bilerek bozulup kırmızı döndüğü **gösterildi** (manuel case 1 ve 2 — aşağıda gerçek çıktı)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis`, 10/10 komut ✅)
- [x] `secret` taraması boş döndü (`kapi.py tarama` — 6 işaretli sentetik credential atlandı)
- [x] `docs-site/` için `npm run check` temiz
- [x] `SourceLanguageTests` taban çizgisi büyümedi (bu faz C# dokunmadı; `kapi.py kapanis`'in `dotnet test` adımı doğruladı)
- [x] `python3 scripts/dokuman-bakim.py --denetle` çıkış kodu 0; yeni dosya bütçeye kaydedildi (`docs/MIMARI-TEHDIT-MODELI.md`: 16_600, olculen 14_069)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (1 🟡 bulundu ve kapandı — bkz. Denetim Bulguları)

### Doğrulama komutları

```bash
# Kapı gerçekten kırmızı dönüyor mu
cd docs-site && npm run check:content   # sınır eklenip modele yazılmadan koşulur

# Site kapıları
cd docs-site && npm run check

# Doküman bütçesi
python3 scripts/dokuman-bakim.py --denetle
```

---

## Plandan Sapmalar

- Açık soru 1 (kapı konumu) planın önerdiği gibi ölçülüp **A: `check-content.mjs`**
  seçildi — `SECURITY.md` ↔ `reference/security-policy.md` senkron kapısının
  (`securityPolicyFacts`) yanına, aynı dosyada, aynı desenle eklendi.
- Açık soru 2 ve 3 önerilen seçeneklerle (B: varlık×saldırgan matrisi; A: sürüm+tarih
  damgası) uygulandı — sapma yok.
- Plan "Tüketici yüzeyi" başlığında `getting-started/security.md`'ye "çapraz link"
  ve `reference/security-policy.md`'ye "kapsam gerekçesi" vaat ediyordu ama ilk
  taslak bu iki dosyayı hiç değiştirmedi — yalnız kök `SECURITY.md` link verdi.
  Bağımsız denetim (Adım 4) bunu 🟡 bulgu olarak yakaladı; her iki site sayfasına
  da `threat-model.md`'ye giden bir cümle eklenerek kapandı (bkz. Denetim Bulguları).
- Planlanan dosya listesindeki `docs-site/scripts/check-content.mjs (VEYA
  scripts/dokuman-bakim.py)` belirsizliği check-content.mjs lehine çözüldü;
  `scripts/dokuman-bakim.py`'ye yalnız yeni dosyanın bütçe kaydı eklendi (Adım 172.3'ün
  kendi planladığı ölçüm), yeni bir kapı değil.

## Bu Fazda Verilen Kararlar

- **K-779** — Denetim izinin `before`/`after` içeriği at-rest content protection
  kapsamı dışındadır; adlandırılmış bir kabul edilen risktir (§ 6, R5). Matris
  kurulurken ölçüldü, plana yazılı değildi.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan onaylandı, açık sorular önerilen seçeneklerle kapandı) |
| Düzeltme turu sayısı | 1 (denetimin 🟡 bulgusu için) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (`kapi.py kapanis` 10/10 ✅, tam test paketi geçti) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (henüz kapanış sonrası bir tur geçmedi) |

## Denetim Bulguları

`faz-denetcisi` (taze bağlamlı) koştu. Kapsam: `git diff 33abea50...` (commit
edilmemiş çalışma ağacı, 9 dosya).

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🟡 | Plan `getting-started/security.md` ve `reference/security-policy.md`'nin tehdit modeline link vereceğini vaat ediyordu; ilk taslakta ikisi de değişmemişti | gerçek | düzeltildi — her iki sayfaya bir cümle eklendi, `check:content` yeniden temiz koştu |

🔴 bulgu yok. 🟢 aday listesine giden bulgu yok. Denetçi `kapi.py kapanis`'i
koşturmadı (diff `src/`/`tests/` dışında, "denetim ucuz olmalı" ilkesi) — bu faz
oturumu kapanış kapısını ayrıca ve tam olarak koşturdu (bkz. DoD).

## Sonraki Faza Devir Notu

- Bu faz kod yazmadı; sonraki fazın devraldığı bir arayüz/sözleşme yok.
- **Tazelik kapısının deseni artık ikinci emsalidir** (birincisi
  `securityPolicyFacts`): bir Türkçe/İngilizce veya kök/site içerik çifti
  senkron kalmalıysa `docs-site/scripts/check-content.mjs`'e iki yönlü bir
  karşılaştırma eklemek bu iki örneği takip edebilir.
- **Sınır kümesi 14'te sabit değil.** Bir sonraki faz `getting-started/security.md`'nin
  "The boundaries Tracon enforces" tablosuna satır eklerse `docs/MIMARI-TEHDIT-MODELI.md`
  § 4/5 ve `reference/threat-model.md`'nin "Boundary mapping" tablosu da güncellenmeli
  — kapı bunu unutulursa yakalar (kırmızı döner), unutmayı önlemez.
- R1-R7'de adlandırılan kabul edilen risklerden biri (örn. content guard'ların
  opt-in olması, R4) gelecekte varsayılan davranışa dönüştürülürse hem
  `TraconProductionRisk` hem bu iki doküman güncellenmelidir.
