# Faz 58 — Doküman Düzeni

> **Durum:** 📋 Planlandı (2026-08-15)
> **Kaynak:** Kullanıcı isteği (2026-08-15). Aday listesinde karşılığı **yoktur**.
> **Önkoşul:** Yok. **58.0 bölümü Faz 57'nin kapanışından önce yapılmalıdır** —
> gerekçe aşağıda.
> **Paketler:** Yok — bu faz koda dokunmaz · **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Değişmiyor

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar:
   ```bash
   grep -n "K-214\|K-068" docs/KARARLAR.md
   ```
   **K-214** (karar indeksinde tarih tutulmaz; reddedilenler ayrı dosyaya
   bölündü), **K-068** (Faz 7 ertelendi).
3. [`scripts/dokuman-bakim.py`](../scripts/dokuman-bakim.py) — **tamamını oku**,
   251 satır. Bütçe sözlüğü ve indeks üretimi buradadır; bu faz onu değiştirir.
4. [`AGENTS.md`](../AGENTS.md) "Faz Akışı ve Doküman Disiplini" bölümü —
   değiştirilecek kuralların bugünkü hâli

---

## Amaç

`docs/` 123 dosya, 5,7 MB, 102.049 satırdır. Disiplin iyi kurulmuş ama iki
yerde çatlamıştır:

1. **Bütçe denetimi `docs/`'un yalnız %4'ünü görüyor.** Denetlenen dokuz
   dosyanın beşinin 30 bayttan az boşluğu kaldı; denetlenmeyen 5,5 MB sınırsız
   büyüyor.
2. **Kayma birikti.** Aynı bilgi beş yerde, beş ölçülebilir sayı yanlış, üç
   dosya kendini "bayat" ilan ediyor ama duruyor.

Bu faz **çeviri yapmaz** — `docs/` Türkçe kalır (kullanıcı kararı, 2026-08-15).
Yeniden düzenleme, silme ve bütçe genişletme işidir.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `MEMORY.md` 7.999 / 8.000 bayt | **1 bayt** boşluk kaldı; `faz-tamamlama` bu dosyaya yazar |
| `docs/hafiza/kod-haritasi.md` 15.996 / 16.000 | 4 bayt |
| `docs/hafiza/build-ve-analyzer.md` 15.992 / 16.000 | 8 bayt |
| `AGENTS.md` 11.983 / 12.000 | 17 bayt |
| `docs/MIMARI.md` 43.974 / 44.000 | 26 bayt |
| `scripts/dokuman-bakim.py` `BUTCE` sözlüğü | 5 dosya kapsıyor; `KARARLAR.md` (389 KB) ve `manuel-test/` (2,7 MB) **denetimsiz** |
| `README.md:351` | "Faz dokümanları (00–32)" — gerçek 00–56 |
| `README.md:166` vs `:22-25` | "sekiz ekran" vs 14 sayılan — gerçek **27 ekran / 33 route** |
| `docs/MIMARI.md:41` | "17 paket üretir"; aynı bölümdeki tablo 16 satır ve biri paket değil |
| `docs/manuel-test/KOSUM-PLANI.md:18-27` | Dosya kendi §1'ini "⛔ BAYATTIR" ilan ediyor; yanlış tablo altında duruyor |

> Kanıtlar 2026-08-15 tarihinde doğrulandı.

---

## 58.0 — Bütçe rahatlatması (Faz 57'den ÖNCE) — ✅ **Yapıldı (2026-08-15)**

> 🚨 Bu bölüm fazın geri kalanından **ayrılır ve öne alınır.** Gerekçe:
> Faz 57'nin kapanışında `faz-tamamlama` `MEMORY.md`'ye yazar. Orada 1 bayt
> boşluk vardır. Bu bölüm yapılmazsa Faz 57 **kapanamaz**.

- `MEMORY.md`, `AGENTS.md`, `MIMARI.md`, `hafiza/kod-haritasi.md`,
  `hafiza/build-ve-analyzer.md` — doymuş içerik `docs/hafiza/` ve
  `docs/arsiv/` altına taşınır
- İçerik **silinmez, taşınır** (`AGENTS.md` doküman bütçesi kuralı)
- Hedef: her dosyada en az %15 boşluk

### Gerçekleşen

Planlanan beş dosyaya **üç dosya daha** eklendi — hepsi 60 bayttan az boşlukla
duruyordu ve Faz 57 kapanışını aynı şekilde kilitleyebilirdi:

| Dosya | Önce | Sonra | Boşluk | Ne yapıldı |
|---|---:|---:|---:|---|
| `MEMORY.md` | 7.999 | 6.796 | %15,1 | Vaka anlatıları alan dosyalarındaki karşılıklarına indirildi |
| `AGENTS.md` | 11.987 | 10.117 | %15,7 | "Canlı Referanslar" tablosu "Okuma Protokolü" tablosuyla **tek tabloya** birleşti (aynı bilgi iki yerdeydi) |
| `docs/MIMARI.md` | 43.974 | 37.396 | %15,0 | Paket tablosu → README (58.2 kararı); §7'nin işletim parametresi tabloları faz dokümanlarına (11/21/22/48); §1 gövdesi arşive |
| `hafiza/kod-haritasi.md` | 15.996 | 13.573 | %15,2 | Workflow girdileri → `workflows.md`; ses girdileri → **yeni** `ses-ve-konusma.md` |
| `hafiza/build-ve-analyzer.md` | 15.992 | 13.132 | %17,9 | Uzun tanılama anlatıları → **yeni** `arsiv/HAFIZA-GECMISI.md`; kurallar yerinde |
| `hafiza/sql-saglayicilari.md` | 15.711 | 13.543 | %15,4 | (plan dışı) Aynı desen |
| `hafiza/cekirdek-calistirma.md` | 15.977 | 13.435 | %16,0 | (plan dışı) Aynı desen |
| `hafiza/maf-api.md` | 15.947 | 13.562 | %15,2 | (plan dışı) Aynı desen |
| `docs/KARARLAR-INDEKS.md` | 24.774 | 20.623 | %17,5 | (plan dışı) **Üretilen dosya** — `ARSIV_ESIK` 150 → 115. Faz 57'nin ekleyeceği kararlar onu kesin aşırtacaktı; K-214'ün "bütçe büyütülmez, bölünme uygulanır" sözü yine tutuldu |
| `README.md` | 19.952 | 19.190 | %4,1 | **Hedefe ulaşmadı** — 58.0 kapsamında değildi; yalnız Faz 57 kapanışına yer açacak kadar sıkıştırıldı. %15 hedefi 58.2/58.4'e kalıyor (README zaten orada yeniden düzenleniyor) |

**Yeni dosyalar:** `docs/hafiza/ses-ve-konusma.md` (`MEMORY.md` yönlendirme
tablosuna satır eklendi), `docs/arsiv/HAFIZA-GECMISI.md` (sıcak yoldan çıkarılan
uzun tanılama anlatıları; yalnız grep'lenir).

**Yan bulgular (58.2'ye girdi):** `MIMARI.md`'deki "⚠️ SqlServer testleri bu
makinede koşmadı" notu K-386 ile çelişiyordu — kaldırıldı. `MEMORY.md`
`scripts/dokuman-butcesi.sh` diye var olmayan bir script'e yolluyordu.
`maf-api.md`'de "MAF hiçbir skill script'ini kendi çalıştırmaz" notu iki kez
yazılıydı. `README.md`'deki "Faz dokümanları (00–32)" **zaten düzeltilmiş**
(bugün 00–59); 58.2'nin o satırı bir şey bulmayacak.

**Kalan Faz 58 işi:** 58.1, 58.2, 58.3, 58.4 — hiçbiri yapılmadı.

---

## 58.1 — Ölü içeriği kaldır

| Hedef | Boyut | Gerekçe |
|---|---:|---|
| `manuel-test/SONUCLAR-*.md` (6 dosya) | 317 KB | Buldukları kusurlar `KAPANIS-PLANI.md`'de commit hash'leriyle kapatıldı → `docs/arsiv/` |
| `manuel-test/PROMPT.md` | 11 KB | `KOSUM-PLANI.md:6` "artık kullanılmaz" diyor |
| `manuel-test/KOSUM-PLANI.md` §1 | — | Dosya kendini "⛔ BAYATTIR" ilan ediyor |
| `manuel-test/00-INDEKS.md` §2.4 + §4 | — | Dosya "geçerli değildir" diyor; yerine geçen kopya `KOSUM-PLANI.md`'de |
| `BEYIN-FIRTINASI.md` | 19 KB | Tamamen tarihsel; üstü çizili kalemler → `docs/arsiv/` |
| `UCUNCU-FAZ-ADAYLARI.md` F-72, F-76 bölümleri | ~80 satır | F-72 ertelendi, F-76 kapatıldı — aday değiller |

---

## 58.2 — Ölçülebilir yanlışları düzelt

| Yer | Yanlış | Doğru |
|---|---|---|
| `README.md:351` | "Faz dokümanları (00–32)" | 00–59 |
| `README.md:166` | "sekiz ekran" | 27 ekran / 33 route |
| `src/AgentPrism.UI/README.md` | 7 ekran, "~88 KB" | 27 ekran, ölçülen değer |
| `MIMARI.md:41` + tablo | "17 paket", tabloda 16 satır (biri paket değil) | 17 paket; `Sql.Shared` ayrı işaretlenir |
| Test sayısı: README 3355 · AGENTS 1068 · MEMORY 1068 **ve** 1231 | Dört farklı sayı | Tek sayı + hangi kümeyi saydığı aynı cümlede |
| `manuel-test/03-*.md` | 33 case `☒`, diğer 25 dosya `☑` | `☑` |

### Tekrarları tek kaynağa indir

| Bilgi | Bugün nerede | Karar |
|---|---|---|
| Paket tablosu | `README.md:148` + `MIMARI.md:16` — **tutarsız** | README tek kaynak; MIMARI bağlantı verir |
| `Neden AgentPrism?` | `MIMARI.md:54` + `arsiv/DEVUI-KARSILASTIRMASI.md:5` | Taşıma yarım kalmış; MIMARI'deki bölüm arşive tamamlanır |
| `Kalem → Faz Haritası` | 3 dosya | Yol haritası dosyalarında kalır; `BEYIN-FIRTINASI` arşive gider |
| Faz durumu | 5 yer | `MIMARI.md:740` "tek yerdedir: README" diyor — README **gerçekten** 0–59'u tek tek listeler |

---

## 58.3 — `manuel-test/` ayrıştırması

2,7 MB, `docs/`'un %47'si. 1.097 case. Her dosya iki şeyi aynı yerde tutuyor:

- **Spesifikasyon** — `Ön koşul`, `Adımlar`, `Girilecek veri`, `Beklenen sonuç`.
  Yeniden koşulabilir; değerlidir.
- **Koşum kaydı** — `Gerçek sonuç`, `Durum: ☑ Geçti`. 2026-08-13 koşumuna aittir.

İkinci bir koşum bu 2,2 MB'ı ya üzerine yazar ya çatallar. Spec dosyada kalır;
koşum kaydı `manuel-test/kosumlar/<tarih>/` altına çıkar.

---

## 58.4 — Bütçe kapsamını genişlet

`scripts/dokuman-bakim.py` içindeki `BUTCE` sözlüğüne eklenir:

| Dosya / grup | Neden bugüne kadar denetimsizdi |
|---|---|
| `docs/KARARLAR.md` | En büyük md dosyası (389 KB); indeksi denetleniyor, kaynağı değil |
| `docs/UCUNCU-FAZ-ADAYLARI.md` | 71 KB, her planlama turunda büyüyor |
| `docs/manuel-test/` (toplam) | 2,7 MB, `docs/`'un yarısı |
| `docs/` (toplam) | Üst sınır — hangi dosyanın büyüdüğünden bağımsız fren |

Sınırlar `faz-tamamlama` kontrol listesine bağlanır.

---

## Planlanan Public API

Yok. Bu faz koda dokunmaz.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
docs/
├── arsiv/                      (+6 SONUCLAR-*.md, +BEYIN-FIRTINASI.md)
├── manuel-test/
│   └── kosumlar/2026-08-13/    (yeni — koşum kayıtları buraya)
└── (silinen: PROMPT.md)
scripts/dokuman-bakim.py        (BUTCE sözlüğü genişler)
```

---

## Testler

Otomatik test yok — bu faz koda dokunmaz. Kapı `scripts/dokuman-bakim.py`'nin
çıkış kodudur.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | 57 faz dokümanı (1,7 MB) arşive taşınsın mı? | A: yerinde kalsın · B: 00–30 arası `docs/arsiv/fazlar/`'a | **A** — `faz-baslangic` önceki fazın devir notunu okur; taşımak o yolu kırar. Bütçe üst sınırı zaten fren olur |
| 2 | `manuel-test/` spec dosyaları İngilizce'ye mi geçsin? | A: Türkçe kalsın · B: İngilizce | **A** — `docs/` kararıyla tutarlı; bunlar iç kabul testleridir |
| 3 | `docs/` üst sınırı kaç? | A: 6 MB (bugünün biraz üstü) · B: 4 MB (58.1 sonrası hedef) | **B** — 58.1 ~600 KB siler; sınır hedefi yansıtmalı, bugünü değil |

---

## Bitiş Ölçütleri (DoD)

- [ ] `python3 scripts/dokuman-bakim.py` çıkış kodu 0, **genişletilmiş** bütçe altında
- [ ] Bütçeli her dosyada en az %15 boşluk
- [ ] `docs/` toplam boyutu ölçüldü ve üst sınırın altında
- [ ] 58.2 tablosundaki altı yanlışın altısı düzeltildi
- [ ] README yol haritası Faz 0–59'u **tek tek** listeliyor
- [ ] Kendini "bayat/kullanılmaz" ilan eden üç bölüm kaldırıldı
- [ ] `grep -rn "00–32" README.md` boş döner
- [ ] Dört doğrulama kapısı sıfır uyarı verir (kod değişmese de koşulur)

### Doğrulama komutları

```bash
python3 scripts/dokuman-bakim.py; echo "çıkış: $?"
du -sh docs/
grep -rn "sekiz ekran\|00–32" README.md docs/       # boş dönmeli
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Silinen içerik sonradan gerekir | Hiçbir şey silinmez; `docs/arsiv/`'e taşınır. Tek istisna `PROMPT.md` — git geçmişinde durur |
| Bütçe sınırları çok dar konur, her faz kapanışı takılır | Sınırlar 58.1 sonrası ölçülen gerçek boyuta göre konur, tahminle değil |
| `manuel-test/` ayrıştırması bağlantıları kırar | `00-INDEKS.md` tek giriş noktasıdır; yalnız o güncellenir |
| README yol haritası 60 satıra çıkar ve README bütçesini patlatır | Faz 21–56 tek satırda özetlenmiş hâli **bilinçliydi**; genişletme bütçeyle birlikte ölçülür — gerekirse yol haritası ayrı dosyaya taşınır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen: bütçe sınırları, `manuel-test/` ayrıştırma deseni.

## Gerçekleşen Public API

> Kapanışta doldurulur. Değişiklik beklenmiyor.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur. Sıradaki faz: **Faz 59 — Ürün Dokümantasyonu**.
