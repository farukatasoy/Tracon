# Faz 90 — Doküman Damıtma Politikası

> **Durum:** ✅ Tamamlandı (2026-08-23)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 20** (F numarası yok; kullanıcı doğrudan seçti)
> **Önkoşul:** Yok
> **Paketler:** Yok — bu faz `scripts/`, `docs/` ve `.agents/skills/` üzerinde çalışır
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — bu faz C# koduna dokunmaz
> **Tüketici yüzeyi:** Yok (site sayfası değişmez) · sevk edilen metin değişmez
> **Manuel test alanı:** [`docs/manuel-test/33-DOKUMAN-KAPILARI.md`](../../manuel-test/33-DOKUMAN-KAPILARI.md) — yeni kapılar oraya eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 36badd9:docs/arsiv/fazlar/90-DOKUMAN-DAMITMA-POLITIKASI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in doküman disiplini **bütçeli bölgede çalışıyor, muaf bölgede çalışmıyor.** Bir oturum `AGENTS.md` (228 satır) + `MEMORY.md` (103) + faz dokümanı ile başlıyor; `docs/hafiza/` alan bazlı ve çakışmasız. Buna karşılık `docs/`'un **%62'si (89.569 satır)** hiçbir tavana tabi değil.

## Bitiş Ölçütleri (DoD)

- [x] `python3 scripts/dokuman-bakim.py --denetle` çıkış 0
- [x] `kirik_baglantilar()` = 0 · **kod bloğu ve satır içi kod farkındalığı eklendi**
- [x] `tam_metin_denetle()` temiz — 90 faz kaydı **+ 25 koşum kaydı**
- [x] `gecmis_isaretci_denetle()` = 0 bulgu (bu fazın başında **18**)
- [x] Üretilen dosya tazeliği kapısı devrede · **`.github/workflows/ci.yml` değişmedi**
- [x] `docs/YOL-HARITASI.md` damıtma öncesi/sonrası **byte-identical**
- [x] `docs/arsiv/fazlar/*.md` her biri ≤ **31.000 B** (ölçülen max 26.068) — plan
      21.000 diyordu; DoD özeti reddedildiği için sınır ölçülene göre kondu
- [x] `docs/KARARLAR.md` bütçesinde · `_kararlar_kalemleri()` **600 kalem** ·
      👤 ve 🔁 sayıları damıtma öncesiyle **aynı**
- [x] `docs/hafiza/*.md` hiçbiri DAR değil · `HAFIZA_DOSYA_BUTCESI` **hâlâ 16.000**
- [x] Geçmeyen manuel case bloğu değişmemiş (35/35 bire bir)
- [x] `python3 -m unittest discover -s scripts -p "*_test.py"` — **109 test** yeşil
- [x] Dört kapı: `build` 0 uyarı · `format` temiz · `pack` başarılı · `test` çıkış 0
- [x] MinVer kayması yok — etiket öncesi/sonrası sürüm aynı ölçüldü
- [x] `secret` taraması boş
- [x] Manuel kabul case'leri 5 → **12**; yedisi de **koşuldu** ve kapıyı kırdı
- [x] `faz-denetim` koşuldu; 🔴 bulguların üçü de kapandı

> **DAR ≤ 1 şartı KARŞILANMADI** (ölçülen 5) ve bu bilinçlidir: `AGENTS.md`,
> `README.md`, `MIMARI-GUVENLIK.md` bu fazdan **önce** DAR'dı ve kapsam
> dışıdır; `docs/manuel-test/*` kullanıcı kararıyla kapsam dışı;
> `docs/KARARLAR.md` %4 → %14'e **iyileşti**. Şartın kendisi fazlaydı — bir faz
> kendi kapsamı dışındaki dosyaları DoD'sine yazmamalıydı.

### Doğrulama komutları

```bash
# Kapılar
python3 scripts/dokuman-bakim.py --denetle
python3 -m unittest discover -s scripts -p "*_test.py" -v
python3 scripts/dokuman-bakim.py --projeksiyon

# Damıtma güvenliği — YOL-HARITASI byte-identical kalmalı
cp docs/YOL-HARITASI.md /tmp/yh-once.md
python3 scripts/dokuman-bakim.py && diff /tmp/yh-once.md docs/YOL-HARITASI.md

# Tam metin elle örnekleme
git show $(git log -1 --format=%H -- docs/arsiv/fazlar/00-ALTYAPI.md):docs/arsiv/fazlar/00-ALTYAPI.md | head -20

# Sürüm kayması yok
dotnet build AgentPrism.slnx -c Release
```

---

## Plandan Sapmalar

Planın **iki mekanik kuralı ölçümle çürütüldü**; ikisi de aynı sınıftandı —
"kısa olan yeterlidir" varsayımı, ayrıntının kanıt olduğu yerde.

1. **DoD tek satıra özetlenmedi.** Plan 86 fazın tamamı ✅ olduğu için DoD'yi
   `N/N ✅` satırına indirmeyi öngörüyordu. Ölçüldü: **11 fazın tek DoD'u**
   işaretsiz kutu taşıyor ve o kutu kapanmamış bir işi kaydediyor
   (`24-SQLITE.md`: "AOT ölçülmedi"). DoD **aynen** korunuyor; yalnız aynı
   dosyada İKİ DoD varken planın işaretsiz kopyası düşüyor (5 dosya).
   Sonucu: dosya başına bütçe 21.000 değil **31.000** (ölçülen max 26.068).

2. **`docs/hafiza` dosyalarında 400 B madde tavanı uygulanmadı** (kullanıcı
   kararı). `test-altyapisi.md:20` (1.296 B) 400 B'de kesilseydi kök sebep
   kalır, **çözüm giderdi** — `MSBUILDDISABLENODEREUSE=1` ve 8 dk → 18,5 sn
   ölçümü kuyruktaydı. Yerine K-214'ün merdiveni uygulandı: **eksene göre
   gerçek bölünme**, sıfır bayt kaybı. Yedi dosya bölündü (planda üçtü);
   `dokumantasyon.md` de bölündü çünkü tek bölümü dosyanın %31'ine ulaşmıştı.

3. **Koşum damıtmasının "geçti → tek satır" kuralı daraltıldı.** Ölçüldü: geçen
   1.061 case'in **254'ü** ⚠️/🚨/`düzeltme`/`kusur` işareti taşıyor —
   `MT-RET-001` "Geçti" olduğu hâlde **iki doküman düzeltmesi** kaydediyor.
   Düz kural 383 KB eylem taşıyan içeriği yok ederdi. `doküman` ve `eksik`
   kelimeleri işaret kümesinden **çıkarıldı**: `MT-PKG-022` ("her pakette XML
   dokümanı var") dokümana DAİR bir case'tir, doküman kusuru değil.

4. **`karar-damit` tavanı yumuşatıldı.** 596 satırın **146'sında** iskelet
   (başlık + tarih + koşul + işaretçi) tek başına 450 B'yi aşıyor. Plan bunları
   atlıyordu; atlamak 146 satırı tümüyle damıtma dışı bırakırdı. Gerekçe yine
   ilk cümleye indiriliyor, başlık ve koşul korunuyor. Sonuç 482 satır (366 değil).

### Planda olmayan, ölçümle ortaya çıkan dört iş

5. **Bağlantı kapısı kod bloğunu ayırt etmiyordu.** Bu fazın kendi planı
   `docs/90-...md -> ../../ADAYLAR.md` diye kırık bağlantı ürettirdi: `LINK`
   regex'i ham metni tarıyordu, bir dokümanın markdown ÖRNEĞİ göstermesi
   yanlış pozitif oluyordu. Damıtma şablonu `INDEKS.md`'de anlatılamazdı.
   `_kod_bloklarini_soy` eklendi — fence yalnız **sütun 0**'da tanınır: liste
   öğesi içindeki kod bloğunun kapanış fence'i girintilidir
   (`30-YEREL-REFERANS.md:451`) ve girintiliyi saysaydık o satır YENİ bir blok
   açıp dosyanın geri kalanını kapıdan **sessizce** düşürürdü.

6. **`MEMORY.md` yönlendirme tablosu ayrıldı.** Bölünmeler tabloyu 20 → 27
   satıra çıkarınca dosya %1 boşluğa düştü. Tablo **alan sayısıyla**, tuzak
   listesi **öğrenilen dersle** büyür — iki eğri tek bütçede sıkışıyordu
   (`MIMARI.md` §7 ile aynı şekil, K-524). Tablo
   [`docs/hafiza/00-INDEKS.md`](../../hafiza/00-INDEKS.md)'e taşındı ve **SORGU**
   bağlamına kaydedildi: `faz-baslangic` Adım 1 onu okumaz, Adım 3 okur.
   `MEMORY.md` 7.521 → 5.233 B (%6 → %35 boş).

7. **18 sarkan karar işaretçisi kapatıldı.** Yeni kapı bulunca araştırıldı:
   12'si arşivde **hiç geçmiyordu**, 6'sı yalnız değinilmişti — hiçbirinin
   ayrı gerekçesi yoktu. İşaretçi 18 vakada da **yalan sözdü**; kaldırıldı.
   İşaretçi metni standart olmadığı için (beş farklı yazım ölçüldü) kapı karar
   numarasını satırın **kendi** `| **K-NNN` önekinden okur.

8. **Taşınan gerekçelerin bağlantıları yeniden yazılmalıydı.** İlk uygulama
   **292 kırık bağlantı** üretti — `docs/`e göre yazılmış bağlantılar
   `docs/arsiv/`'den çözülmüyor. Script'in kendi yorumunda yazılı tuzağın
   ta kendisi (Faz 58: önce 17, sonra 3).

9. **🔁 işareti yanlış pozitif üretiyordu.** K-600 ifadeyi **alıntıladığı**
   için "yeniden açılmış" göründü. Ayrıştırıcı şablonun iki noktasını
   (`yeniden açıldı:`) ister hâle getirildi — altı gerçek kaydın hepsi taşıyor.

## Bu Fazda Verilen Kararlar

**K-597** (damıtma politikası · 👤) · **K-598** (git geçmişi + kanıtlayan kapı) ·
**K-599** (`HARIC` korunur, her ağaca kendi bütçesi · 👤) · **K-600** (450 B
karar tavanı, önce taşı sonra kes).

Ledger'a girmeyen yerel tercihler burada kalır: `docs/hafiza` bölünmelerinin
eksenleri, işaret kümeleri, alt komut adları.

## Denetim Bulguları

Bağımsız denetçi (taze bağlam) `efd5247..HEAD` aralığını denetledi. Fazın
merkezî iddiası **doğrulandı**: 90/90 kayıtta `git show` çözüldü ve tam metin
döndü, `Plandan Sapmalar`/`Devir Notu`/`Kararlar`/`Denetim Bulguları` 90/90
bire bir korundu, üç yeni kapı mock'suz kırıldı ve gerçekten kırmızı oldu.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `faz-arsivle` kapsamsız `git reset --hard` koşuyordu ama temizlik ön denetimi yalnız `docs`/`.agents`/üç kök dosyaya bakıyordu — `src/` altındaki commit edilmemiş düzenleme geri almada **kalıcı** kaybolurdu | **Düzeltildi.** Ön denetim ağacın **tamamını** kapsar (`-uno`: izlenmeyen dosyalar hariç, `reset --hard` onlara dokunmaz). 3 test |
| 2 | 🔴 | Damıtma `NN.x` önekli bölümleri uyarısız düşürüyordu; Faz 29 sapmalarını `29.0 — Plandan Sapmalar` diye numaralandırdığı için **fazın kendi sözü sessizce bozuldu** | **Düzeltildi.** `NN.x` bir numaralandırma konvansiyonudur: sınıflandırma numaradan **sonraki** başlıkla yapılır. Faz 29 tam metinden yeniden damıtıldı. Ölçüldü: 397 `NN.x` başlığının etkilenen **3**'ü. 4 test |
| 3 | 🔴 | Planlanan 7 manuel kabul case'i yazılmamıştı | **Düzeltildi.** 7 case eklendi (5 → 12) ve **hepsi koşuldu**. Koşum iki kusur daha buldu: tablo bir boş satırla kesilmişti (K-539 sınıfı) ve case 7'nin deseni fire etmiyordu |
| 4 | 🟡 | `komut_faz_arsivle` için uçtan uca test yok | **Kısmen kapandı.** Ön denetim ve sınıflandırma test edildi; `git mv` + geri alma yolu gerçek bir git fixture'ı ister, `MT-DKP-010` manuel karşılığı kapsıyor |
| 5 | 🟡 | Koşum kayıtları `git log --follow` yazıyordu — fazın kendi §90.3'ü bunu **yasaklıyor**; kapı koşum kayıtlarını hiç taramıyordu | **Düzeltildi.** 25 kaydın işaretçisi `git show <sha>:<yol>` oldu; kapı artık `kosumlar/`'ı da tarar (bozuk SHA ile kırmızı olduğu doğrulandı) |
| 6 | 🟡 | `tam_metin_denetle()` SHA'nın çözüldüğünü kanıtlıyordu, çözülen içeriğin **damıtılmamış** olduğunu değil | **Düzeltildi.** Kapı çözülen içerikte damıtma işareti arar. 1 test |
| 7 | 🟡 | DoD satırları sevk edilen sabitlerle çelişiyordu ve 15 kutunun 15'i işaretsizken başlık ✅ diyordu | **Düzeltildi.** DoD gerçeğe uyduruldu; karşılanmayan tek şart gerekçesiyle **açıkça** yazıldı |
| 8 | 🟡 | Bu fazda konan iki bütçe kapanış günü DAR — sınır fazın kendi eklemelerinden **önce** ölçülmüştü | **Düzeltildi.** Kapanış boyutuna göre yeniden kalibre edildi |
| 9 | 🟢 | `_DUS_DESENLERI` sürüm-sabitli MAF imza bölümlerini düşürüyor | **Devredildi** → **F-146** |

🚨 **Denetimden sonra iki kusur daha bu dosyanın kendisinde çıktı** — ikisi de
"düz metin arama, gösterimi gerçekten ayırt edemez" sınıfı:

- Bu dokümanı **kapatan düzenleme** dokümanı bozdu: `s.index("## Sonraki Faza
  Devir Notu")` bir `awk` komutunun **içinde** eşleşti ve dosyayı satır ortasından
  böldü; içerik ikiye katlandı. Son iyi sürümden geri yüklendi, düzenleme satır
  başına çapalanmış `rindex` ile tekrarlandı.
- Damıtmanın idempotans denetimi (`DAMITMA_ISARETI in metin`) bu dokümanı
  **zaten damıtılmış** sandı — §90.2 işareti bir **örnek** olarak gösteriyor.
  Denetim artık `_kod_bloklarini_soy` üzerinden koşar.

## Sonraki Faza Devir Notu

**Devraldığın sözleşmeler**

- Faz kapanışı artık **iki komut** ister: `faz-arsivle <NN>` sonra
  `faz-damit <NN>`. `faz-tamamlama` Adım 7 bunu yazıyor. Elle `mv` **kullanma**.
- `--denetle` üç yeni kapı koşuyor: **tazelik** (üretilen dosyayı elle
  üretmeden commit edersen kırmızı), **karar işaretçisi**, **tam metin SHA**.
- Muaf ağaçların hepsinin bütçesi var. Yeni bir ağacı `HARIC`e eklersen
  `test_muaf_agaclarin_hepsinin_butcesi_var` **kırılır** — bütçesini de yaz.

🚨 **Tuzaklar**

- `DIZIN_BUTCESI` anahtarı **üçlüdür**. `_dizin_boyutu(yol, oz)` varsayılanı
  HARIC'i uygular ve `docs/arsiv` **kendini düşürüp 0 ölçer**.
- Damıtma tanımadığı bölümü **düşürmez**, korur ve uyarır. `--kuru` çıktısındaki
  uyarıları oku; cevabı **desen** olsun, ad listesi değil (98 tanınmayan ad var).
- `kirik_baglantilar()` fence'i yalnız **sütun 0**'da tanır. Girintili fence'in
  içi taranır — güvenli yön.
- Bir dosyayı `docs/`'tan `docs/arsiv/`'e taşırken içindeki göreli bağlantıları
  **mutlaka** yeniden yaz. `faz-arsivle` bunu yapar; elle taşırsan yapmaz.

**Açık kalanlar**

- 🚨 **CI'ın `secret` taraması KENDİ yorumunu yakalıyor** (bu fazda bulundu,
  kapsam dışı — `ci.yml` bu fazda değişmedi). Desen
  `(Password|pwd)=[^ \";']{6,}`; `.github/workflows/ci.yml:58` ve
  `.agents/skills/faz-tamamlama/SKILL.md:73` deseni **anlatırken** birebir
  içeriyor ve `--exclude-dir` listesi bu iki yolu kapsamıyor. İkisi de
  `efd5247`'de aynen vardı, yani adım CI'da ilk koştuğunda **kırmızı olur**.
  Düzeltme ucuz: desen anlatılırken değer bölünür (`Pass` + `word=`) ya da
  `--exclude-dir=.github --exclude-dir=.agents` eklenir. Bir sonraki faz bunu
  ilk iş kapatmalı.

- **DAR 5 kalem:** `AGENTS.md` (%4), `README.md` (%4), `MIMARI-GUVENLIK.md`
  (%10) — üçü de bu fazdan **önce** DAR'dı; `KARARLAR.md` (%14, %4'ten
  iyileşti); `docs/manuel-test/*` (%10, **bilerek** kapsam dışı — envanter
  kalem 9'un otomatikleştirme zinciri).
- **F-130 üçüncü kez görüldü.** `Ui.E2ETests` tam koşumda kırılgan;
  `docs/hafiza/test-kosum-tuzaklari.md` üç vakayı da kaydediyor. Artık bir
  **sınıf**: o paket tam koşumda yalıtılmalı.
