# 33 — Doküman Kapılarının Doğruluğu (`DKP`)

> **Alan kodu:** `DKP` · **Faz:** 80, 90, 167
> **Kaynak:** `scripts/dokuman-bakim.py` · `scripts/dokuman_bakim_test.py` ·
> `.github/workflows/ci.yml`
>
> Ortam kurulumu ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.
> Bu alan [`31-DOKUMAN-DOGRULUGU.md`](31-DOKUMAN-DOGRULUGU.md)'nün üstüne
> kurulmaz — o alan sevk edilen **metnin** doğru olduğunu kanıtlar, bu alan
> onu kanıtlayan **kapının kendisinin** doğru çalıştığını kanıtlar.

---

## Bu dosya neyi kanıtlar

`scripts/dokuman-bakim.py --site-denetle` eskiden **herhangi** bir site
sayfasının değişmesini tetiklenen **tüm** kuralların karşılığı sayıyordu;
`Workflows/` değiştirip yalnız `packages.md`yi düzenlemek de ✅ dönüyordu. Faz 80
eşlemeyi kural başına yaptı, `capabilities.md`'yi bir hedef ekledi,
`kirik_baglantilar()`'ı site-mutlak (`/...`) bağlantıları çözecek ve `.mdx`
okuyacak şekilde genişletti, kapının kendi testini yazdı ve `--denetle`'yi
CI'nın `build` işine bağladı.

---

## Koşmadan önce

```bash
cd /Users/farukatasoy/Desktop/projects/Tracon
python3 --version   # 3.10+ gerekir (X | None tip birleşimi)
```

Case 1-4 ve 6-12 doğrudan `python3 scripts/dokuman-bakim.py` çağrısıdır; .NET
veya Node derlemesi istemez. Case 5 yalnız `.github/workflows/ci.yml` okur.
Case 6-12 Faz 90'da eklendi (üç yeni kapı, damıtma komutları, muaf ağaç bütçesi).

---

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-DKP-001` | Temiz ağaç | `src/Tracon.Workflows/` altında bir dosyaya boşluk ekle, `docs-site/src/content/docs/packages.md`'ye bir satır ekle, `python3 scripts/dokuman-bakim.py --site-denetle --taban HEAD` | ❌ **kırmızı**, çıkış kodu 1 — `workflow` kuralı `concepts/workflows.md` ister, `packages.md` onu karşılamaz. Rapor kural adını, hedefi ve tetikleyen dosyayı yazar |
| 2 | `MT-DKP-002` | Case 1'in ağacı (değişiklikleri geri alma) | `concepts/workflows.md`'ye de bir satır ekle, aynı komutu tekrar koş | ✅ **yeşil**, çıkış kodu 0 — tetiklenen kuralın hedefi de değişenler arasında |
| 3 | `MT-DKP-003` | Temiz ağaç | `src/Tracon.Core/buildTransitive/Tracon.Core.targets`'a yorum satırı ekle, `python3 scripts/dokuman-bakim.py --site-denetle --taban HEAD` | ❌ **kırmızı** — rapor **`capabilities.md`**'yi ister (`buildtransitive` kuralı) **ve** ayrıca `concepts/` dizinini ister (`cekirdek-kavram` kuralı); ikisi ayrı satır |
| 4 | `MT-DKP-004` | Temiz ağaç | Elle yazılan bir sayfaya (ör. `docs-site/src/content/docs/troubleshooting.md`) bir Markdown bağlantısı ekle — görünen metin "kırık", hedef yol `/yok-boyle-sayfa/` — sonra `python3 scripts/dokuman-bakim.py --denetle` | ❌ **kırmızı** — "Kırık bağlantı" bölümünde dosya adı ve hedef yol görünür |
| 5 | `MT-DKP-005` | 👤 insan gerekir | `.github/workflows/ci.yml`'yi aç | `Dokuman kapilari` ve `Dokuman kapilari testleri` adımları **`build`** işinde; `site` işinde **değil**. `Python kur` adımı (`actions/setup-python`) bu adımlardan önce çalışır |
| 6 | `MT-DKP-006` | Temiz ağaç | `sed -i '' 's/✅ Tamamlandı/BOZUK/' docs/YOL-HARITASI.md` sonra `python3 scripts/dokuman-bakim.py --denetle` | ❌ **kırmızı** — "Üretilen dosya tazeliği" bölümü `docs/YOL-HARITASI.md — kaynakla ayni degil` der. Bu kapı olmadan bayat üretilmiş dosya sessizce commit edilebiliyordu |
| 7 | `MT-DKP-007` | Temiz ağaç | `sed -i '' 's/^### K-021.*/### SILINDI/' docs/arsiv/KARARLAR-GECMISI.md` sonra `--denetle` koş. 🚨 Desen sondaki `.*`'sız yazılırsa **fire etmez**: aynı numara iki başlıkta geçer (`### K-021` ve `### K-021 — devam (Faz 90 damıtması)`) ve biri kalırsa çapa hâlâ çözülür — kapı doğru davranır | ❌ **kırmızı** — "Karar gerekçesi işaretçisi" o `K-NNN`'yi sarkan olarak listeler. `kirik_baglantilar()` bunu **göremez**: bağlantı dosya düzeyindedir, dosya vardır, çapa denetlenmez |
| 8 | `MT-DKP-008` | Temiz ağaç | Bir damıtılmış kayıttaki (`docs/arsiv/fazlar/24-SQLITE.md`) `git show <sha>` SHA'sını `deadbee` yap, `--denetle` koş | ❌ **kırmızı** — "Damıtılmış kayıt tam metni" `deadbee:...çözülmüyor` der. Git geçmişine güvenmek ancak kapı onu her koşumda kanıtlıyorsa meşrudur |
| 9 | `MT-DKP-009` | Temiz ağaç | `python3 scripts/dokuman-bakim.py faz-damit 24 --kuru` | Çıkış 0, **hiçbir dosya değişmez** (`git status --porcelain` boş). Kayıt zaten damıtılmış olduğu için `0/1 dosya · 3.902 → 3.902 B` raporlar — damıtma **idempotenttir**, ikinci koşum kaydı bozmaz |
| 10 | `MT-DKP-010` | Kirli ağaç · `docs/` kökünde açık bir faz (`<NN>`) | `echo "" >> src/Tracon.Core/TraconServiceCollectionExtensions.cs` sonra `python3 scripts/dokuman-bakim.py faz-arsivle <NN>` | ❌ Komut **hiçbir dosyaya dokunmadan** çıkar: "çalışma ağacında commit edilmemiş değişiklik var … `git reset --hard` koşar ve onları YOK EDER". `src/` altındaki düzenleme **korunur** |
| 11 | `MT-DKP-011` | Temiz ağaç | Bir doküman dosyasına ```` ```markdown ```` bloğu içinde `[x](../../YOK.md)` yaz, `--denetle` koş | ✅ **yeşil** — kod bloğundaki örnek bağlantı sayılmaz. Aynı satırı blok **dışına** yazınca ❌ kırmızı olur; damıtma şablonunu belgeleyebilmek bunu gerektiriyor |
| 12 | `MT-DKP-012` | Temiz ağaç | `python3 scripts/dokuman-bakim.py --denetle \| grep "docs/arsiv"` | `docs/arsiv/**.md` satırı **0 değil** gerçek bir bayt sayısı gösterir. 0 görürsen `_dizin_boyutu` HARIC'i kendi üstüne uyguluyor demektir — muaf ağaç bütçeleri sessizce anlamsız olur |

Her case sonunda çalışma ağacı `git checkout -- <değiştirilen dosyalar>` ile
temizlenir; hiçbir case commit oluşturmaz.

---

## Doğrulama komutları

```bash
# Kapının kendi testi
python3 -m unittest discover -s scripts -p "*_test.py" -v

# Bugünkü ağaçta kırık site-mutlak bağlantı sayısı (taban ölçüm: 0)
python3 scripts/dokuman-bakim.py --denetle | grep "Kırık bağlantı"

# CI satırı DOĞRU işte mi (build, site/pages değil)
awk '/^  build:/{j="build"} /^  site:/{j="site"} /^  pages:/{j="pages"}
     /dokuman-bakim|Dokuman kapilari/{print j": "$0}' .github/workflows/ci.yml
```

## Bilinen sınırlar

- **Case 5 göz gerektirir.** Kapı YAML'ın **hangi işte** olduğunu ölçmez;
  `python3 -m unittest` çıktısı yalnız testlerin geçtiğini kanıtlar, CI'ya
  bağlı olduğunu değil.
- **`## Read next` bağlantısının hedefinin DOĞRU sayfa olduğu** (yalnız var
  olduğu değil) bu kapıyla denetlenmez — bilerek: çözülebilirlik makine işidir,
  doğruluk semantiktir (`tuketici-dokuman-senkronu` skill Adım 7).

## Faz 91 ek case'leri

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 13 | `MT-DKP-013` | Tamamlanmış arşiv fazında `- [ ]` satırı | `python3 scripts/dokuman-bakim.py --denetle` | Faz dosyası ve satır raporlanır; kutu düz metne çevrilince bulgu kaybolur |
| 14 | `MT-DKP-014` | Skill içinde `EnablePublicApiTracking=false` iddiası | `python3 scripts/dokuman-bakim.py --denetle` | `Directory.Build.props` gerçek değeriyle çakışma raporlanır |
| 15 | `MT-DKP-015` | CI veya kapanış skill'inde eski sync/secret deseni | `python3 scripts/dokuman-bakim.py --denetle` | Kopyalanmış desen raporlanır; desen `kapi.py`'ye taşınınca kapı temizlenir |
| 16 | `MT-DKP-016` | **Tablo biçimli** bir aile (`31`–`36`) dosyasına bir case satırı ekle, `00-INDEKS.md`'deki sayıyı **güncelleme** | `python3 scripts/dokuman-bakim.py --denetle` | Sapma `(+1)` olarak raporlanır. 🚨 Faz 167'ye kadar bu **sessizce geçiyordu**: `manuel_test_sayim_kaymasi` yalnız `### MT-` başlığı sayıyor, tablo biçimli altı aileyi hiç görmüyordu ve ikisinde gerçek sapma birikmişti (`31-*` +8, `36-*` +6) |
