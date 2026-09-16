# 33 — Doküman Kapılarının Doğruluğu (`DKP`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../33-DOKUMAN-KAPILARI.md`](../../33-DOKUMAN-KAPILARI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
> Spec **tablo biçiminde**dir (satır başına bir case); bu kayıt dosyası diğer
> tüm ailelerle aynı `## MT-<KOD>-<NNN>` kalıbını kullanır (DEVIR.md §8,
> oturum 13'te kararlaştırıldı) — başlık spec'in `Adımlar`/`Beklenen sonuç`
> sütunundan kısaltılmış kendi paraphrase'imdir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s2` (Faz B, ikinci aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s2` · dal `test/kosum-s2` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 25 (MT-DKP-001..025) |
| **Port** | N/A — bu aile `scripts/dokuman-bakim.py` ve `docs-site/scripts/check-content.mjs` kapılarını sınar, örnek uygulama gerekmez |
| **Depo** | N/A |

Ortam: `python3 --version` → `3.14.3` (3.10+ şartı sağlanıyor). `node
--version` → `v22.23.2`. `docs-site/node_modules` **kurulu değil** ama
`node scripts/check-content.mjs` yalnız yerel dosyaları ve Node yerleşik
modüllerini içe aktarıyor — `npm install` gerekmeden çalıştığı doğrulandı
(taban ölçüm: `Content: 56 manual pages and 57 total pages passed.`, çıkış
`0`).

Her case sonunda çalışma ağacı `git checkout -- <değiştirilen dosyalar>` ile
temizleniyor; hiçbir case commit oluşturmuyor (spec'in kendi kuralı).

---

## Devir notu

**🚀 Oturum başladı (2026-09-17, ap-s2 şeridi, ilk oturum).** Dosya taze
açılıyor.

🚨 **Taban ölçüm — `python3 scripts/dokuman-bakim.py --denetle` bu turun
başında (case 007'nin çalışması sırasında, dosya 33'e özgü hiçbir
mutasyon yokken) zaten çıkış `1` veriyor**, ailemin dışında iki nedenle:
(1) bilinen bütçe aşımı (`docs/manuel-test/kosumlar/**.md`, DEVIR.md §8'de
zaten "beklenen, panik yok" olarak kayıtlı); (2) **yeni gözlem**:
`docs/manuel-test/kosumlar/2026-09-16/36-GELISTIRME-KAPILARI.md` içindeki
**iki illüstratif metin parçası** doküman kapısının desen eşleyicisini
yanlışlıkla tetikliyor — MT-GDK-013'ün "Gerçek sonuç"unda geçen, kasıtlı
olarak **çalışmayan** bir örnek olarak yazılmış `git show
9c32242:docs/73-TUKETICI-AGENT-DESTEGI.md` metni "Damıtılmış kayıt tam
metni" kontrolünü kırıyor; MT-GDK-034'ün kaydındaki `[kapilar.md]
(kapilar.md)` Markdown bağlantı sözdizimi de "Kırık bağlantı" sayısına
giriyor (bkz. MT-DKP-004'ün yan bulgusu). İkisi de **file 36'nın kendi
prose'u**, file 33'ün kapsamı dışında; düzeltilmedi (kapanış modu değil,
koşum modu — skill §1.1). Bu yüzden aşağıdaki her case'in "çıkış kodu"
değerlendirmesi bu **iki bilinen, ailemin dışındaki** bulguyu göz ardı
ederek, yalnız case'in **kendi** iddia ettiği satır/kural üzerinden
yapıldı — tıpkı MT-GDK-036'nın (dosya 36) kendi turunda yaptığı gibi.

---

## MT-DKP-001 — `workflow` kuralı: kaynak değişti ama `concepts/workflows.md` değişmedi → kırmızı

**Gerçek sonuç**
`src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs`'e boş satır
eklendi, `docs-site/src/content/docs/packages.md`'ye de boş satır eklendi
(hedef **değil**). `python3 scripts/dokuman-bakim.py --site-denetle
--taban HEAD` → çıkış **1**:
```
❌ workflow           hedef: concepts/workflows.md   ör: src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs
❌ 1 kural karşılanmadı: workflow: hedef concepts/workflows.md değişmedi (...tetikledi)
```
Beklenen kural adı, hedef ve tetikleyen dosya birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-002 — Aynı değişiklik + hedef sayfa da değişince → yeşil

**Gerçek sonuç**
Case 1'in ağacına ek olarak `docs-site/src/content/docs/concepts/workflows.md`'ye
de boş satır eklendi, aynı komut tekrar koşuldu → çıkış **0**:
```
✅ workflow           hedef: concepts/workflows.md   ör: src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs
✅ Tetiklenen her kuralın hedefi değişenler arasında.
```
Üç dosya değiştiğinde ("3 değişen dosya" → case 2'de "4 değişen dosya", bu
oturumun kendi kayıt dosyasının izlenmemiş olması sayıya girmiyor, git'e
eklenmemiş dosyalar taranmıyor) kural tetiklendi ve karşılandı. Değişiklikler
`git checkout --` ile geri alındı (case biter bitmez).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-003 — `buildtransitive` kuralı İKİ hedef ister: `capabilities.md` VE `concepts/`

**Gerçek sonuç**
`src/Tracon.Core/buildTransitive/Tracon.Core.targets`'a bir yorum satırı
eklendi (`// mt-dkp-003`). `python3 scripts/dokuman-bakim.py --site-denetle
--taban HEAD` → çıkış **1**, **iki ayrı satır**:
```
❌ buildtransitive    hedef: capabilities.md   ör: .../Tracon.Core.targets
❌ cekirdek-kavram    hedef: concepts/         ör: .../Tracon.Core.targets
❌ 2 kural karşılanmadı: buildtransitive (capabilities.md) + cekirdek-kavram (concepts/)
```
Spec'in iddiasıyla birebir — iki kural, iki ayrı hedef, iki ayrı satır.
Değişiklik `git checkout --` ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-004 — Elle yazılan sayfaya kırık bağlantı eklenince `--denetle` dosya adı + hedef yolu raporlar

**Gerçek sonuç**
`docs-site/src/content/docs/troubleshooting.md`'nin sonuna
`[kırık](/yok-boyle-sayfa/)` eklendi. `python3 scripts/dokuman-bakim.py
--denetle` → "Kırık bağlantı" bölümünde:
```
docs-site/src/content/docs/troubleshooting.md -> /yok-boyle-sayfa/
```
Dosya adı **ve** hedef yol birebir raporlandı. Değişiklik geri alındı.

⚠️ **Yan bulgu (bu case'in kapsamı dışında, düzeltilmedi):** Aynı komut
çıktısında **ikinci** bir kırık bağlantı daha vardı:
`docs/manuel-test/kosumlar/2026-09-16/36-GELISTIRME-KAPILARI.md ->
kapilar.md`. Kaynağı incelendi (satır 729): oturum 2'nin (bu benim değil,
önceki bir oturumun) MT-GDK-034 kaydında, `kurtarma.md` içindeki eski bir
bağlantıyı göstermek için düz metin yerine gerçek Markdown bağlantı
sözdizimi (`[kapilar.md](kapilar.md)`) kullanılmış — bu bir gezinme
bağlantısı değil, alıntılanan eski içeriğin illüstrasyonu, ama tarayıcı
onu gerçek bir bağlantı sanıp kırık sayıyor. Kural 1 istisnası bu durumu
kapsamıyor (başka bir case'in kaydı, benim mutasyonum değil) ve bu family'nin
case'i değil; düzeltilmedi, yalnız not edildi. Etkisi: `--denetle`'nin genel
çıkış kodu bu satır yüzünden de `1` olabilir ama bu case'in kendi iddiası
(troubleshooting.md satırının doğru raporlanması) sağlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-DKP-005 — CI'da `Dokuman kapilari` adımları `build` işinde, `setup-python` önce koşar 👤

**Gerçek sonuç**
`.github/workflows/ci.yml` doğrudan okundu (bu oturum Claude Code içinde
çalıştığı için kendi gözüyle doğrulandı — MT-GDK-011'de kullanılan aynı
yöntem, ayrı bir insan onayı istenmiyor, bilgi amaçlı not). `build:` işi
satır 32'de başlıyor, `site:` işi satır 545'te — aralarında. `actions/
setup-python@v5` satır 68'de (yorum: "`scripts/dokuman-bakim.py` icin").
"Dokuman kapilari" adımı satır 120 (`run: python3 scripts/dokuman-bakim.py
--denetle`), "Dokuman kapilari testleri" satır 123 — **ikisi de** 68 ile
545 arasında, yani `build:` işinin içinde, `setup-python`'dan **sonra**.
`site:` işinde (545'ten sonra) `dokuman-bakim`/`Dokuman kapilari` hiç
geçmiyor (`awk` script'i `site:` etiketli hiçbir eşleşme döndürmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-006 — `docs/YOL-HARITASI.md`'de sahte "BOZUK" durumu → "Üretilen dosya tazeliği" bulur

**Gerçek sonuç**
🚨 **Case'in kendi belgelenmiş adımı koşulamadı.** `sed -i '' 's/✅
Tamamlandı/BOZUK/' docs/YOL-HARITASI.md` çalıştırıldı (worktree
`/Users/farukatasoy/Desktop/projects/ap-s2`, dal `test/kosum-s2`) → harness
**doğrudan reddetti**: `sed to
'/Users/farukatasoy/Desktop/projects/Tracon/docs/YOL-HARITASI.md' was
blocked by a deny rule.` Not: hata mesajı `ap-s2` yerine ana `Tracon`
worktree yolunu gösteriyor — kural muhtemelen dosya adına/göreli yola göre
eşleşiyor, worktree'den bağımsız. Bu, komutun `grep`'ini ayrı çalıştırdığım
ilk denemede **çalışmasından** hemen sonra, aynı oturumda, `sed`'i tek
başına tekrar denedikten sonra da **aynı şekilde** reddedildi — yani bu
önceki oturumların notundaki "sınıflandırıcı belirsizliği, basit tekrar
yeterli" deseni değil, **açık bir `deny` kuralı**. `git status --short
docs/YOL-HARITASI.md` ve `git diff docs/YOL-HARITASI.md` boş — dosyaya
hiçbir bayt yazılmadı.

Talimat gereği ("bir komut reddedilirse ve düz bir tekrar bunu temizlemezse
… iş yapmaya çalışma, tam olarak not et ve devam et") bu case'i başka bir
araçla (ör. `Edit`) aşmaya çalışılmadı — `docs/YOL-HARITASI.md`'nin "üretilir,
elle yazılmaz" (K-413, `AGENTS.md`) kuralını tam olarak bu şekilde,
araç katmanında da zorlaması davranışsal olarak **tutarlı** (kusur değil,
korumanın kendisi) ama case'in **kendi belgelenmiş prosedürünün** bu ajan
ortamında koşulamadığı bir gerçek. Aracın kendisi `python3
scripts/dokuman-bakim.py --denetle`'nin bu senaryoda ne yapacağını
(mutasyon uygulanamadığı için) **gözlemleyemedim**.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı — **bloklandı, kullanıcıya bildirilecek**

## MT-DKP-007 — İki başlıktan İKİSİ de silinince `K-021` sarkan referans olarak raporlanır

**Gerçek sonuç**
`sed -i '' 's/^### K-021.*/### SILINDI/' docs/arsiv/KARARLAR-GECMISI.md`
→ hem satır 2164 (`### K-021`) hem satır 2202 (`### K-021 — devam (Faz 90
damıtması)`) `### SILINDI`'ye döndü (regex `.*` HER İKİ satırla da eşleşti
— case'in kendi uyarısı doğrulandı: `.*` olmadan yalnız ilk satır
değişirdi, ikinci başlık ayakta kalırdı ve çapa yine çözülürdü, kapı
**yanlışlıkla** yeşil kalırdı). `python3 scripts/dokuman-bakim.py --denetle`
→ çıkış **1**, `Karar gerekçesi işaretçisi: 1 bulgu — docs/KARARLAR.md ->
arsiv/KARARLAR-GECMISI.md#K-021 (baslik yok)`. Değişiklik `git checkout --`
ile geri alındı; geri alma sonrası `Karar gerekçesi işaretçisi: ✅ temiz`
(izole doğrulandı — yalnız bu case'in bulgusu kayboldu, dosya 36'nın
yukarıda not edilen iki bilinen bulgusu aynen kaldı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-008 — Damıtılmış kaydın SHA'sı bozulunca "çözülmüyor" raporlanır

**Gerçek sonuç**
`docs/arsiv/fazlar/24-SQLITE.md:20`'deki `git show 7f1833e:...` göstergesi
`git show deadbee:...` yapıldı. `python3 scripts/dokuman-bakim.py --denetle`
→ çıkış **1**, `Damıtılmış kayıt tam metni: 2 bulgu` — biri benim
mutasyonum (`docs/arsiv/fazlar/24-SQLITE.md -> deadbee:docs/arsiv/fazlar/
24-SQLITE.md çözülmüyor`), diğeri yukarıda not edilen file 36'nın kendi
bilinen bulgusu (`9c32242:docs/73-...`, değişmedi). Case'in kendi iddiası
(SHA bozulunca kapı kırmızı olur) birebir doğrulandı. `git checkout --`
ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-009 — `faz-damit --kuru` idempotenttir, hiçbir dosya değişmez

**Gerçek sonuç**
`python3 scripts/dokuman-bakim.py faz-damit 24 --kuru` → çıkış **0**,
çıktı: `(kuru) 0/1 dosya · 4.965 → 4.965 B (-%0)` (spec'in örnek metni
`3.902 → 3.902 B` yazıyor — bayat rakam, kayıt o tarihten beri büyümüş;
case'in **iddiası** — `0/1 dosya`, değişim yok, idempotent — birebir
doğrulandı, yalnız örnek bayt sayısı güncel değildi). `git status
--porcelain` boş kaldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-010 — Kirli ağaçta `faz-arsivle` hiçbir dosyaya dokunmadan reddeder; `src/` düzenleme korunur

**Gerçek sonuç**
Bu case'in ön koşulu ("docs/ kökünde açık bir faz") bu anda **sağlanmıyor**
— repo'da açık bir `docs/<NN>-*.md` yok (tüm fazlar arşivlenmiş, kod donuk
tur). `komut_faz_arsivle`'nin kendi kaynağı (`scripts/dokuman-bakim.py:
2310-2314`) önce `docs/<NN>-*.md` eşleşmesini arıyor ve **0 eşleşmede**
kirli-ağaç kontrolüne hiç ulaşmadan farklı bir hatayla çıkıyor — yani ön
koşulsuz koşarsam case'in asıl iddiasını (kirli ağaç reddi) hiç gözlemleyemezdim.
Bunun için **geçici** bir faz dosyası oluşturuldu:
`docs/999-GECICI-MT-DKP-010.md` (izlenmeyen, 3 satır placeholder — glob
deseni `[0-9][0-9]*-*.md`'yi karşılamak için gerekli minimum). Sonra
`echo "" >> src/Tracon.Core/TraconServiceCollectionExtensions.cs` ile
**gerçek** kirli ağaç (case'in kendi belgelenmiş adımı, `src/` istisnası —
skill §1.1). `python3 scripts/dokuman-bakim.py faz-arsivle 999` → çıkış
**1**:
```
❌ çalışma ağacında commit edilmemiş değişiklik var; bu komut başarısızlıkta
   `git reset --hard` koşar ve onları YOK EDER:
   M src/Tracon.Core/TraconServiceCollectionExtensions.cs
```
Komut **hiçbir dosyaya dokunmadan** çıktı — `git status --short` sonrasında
`src/...` satırı hâlâ `M` (silinmedi/geri alınmadı), düzenleme **korundu**,
tam olarak case'in iddiası. Temizlik: `git checkout --
src/Tracon.Core/TraconServiceCollectionExtensions.cs` ile geri alındı,
`docs/999-GECICI-MT-DKP-010.md` silindi (hiç git'e eklenmemişti).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-011 — Kod bloğu içindeki örnek bağlantı sayılmaz; aynı satır blok dışında kırmızıdır

**Gerçek sonuç**
`docs-site/src/content/docs/troubleshooting.md`'nin sonuna ` ```markdown `
bloğu içinde `[x](../../YOK.md)` eklendi. `python3 scripts/dokuman-bakim.py
--denetle` → çıkış **1** ama "Kırık bağlantı" bölümünde **yalnızca**
bilinen dosya-36 bulgusu vardı (`kapilar.md`) — `troubleshooting.md`'nin
kod bloğundaki örnek bağlantı **sayılmadı**, tam olarak case'in iddiası
(genel çıkış kodu dosya-36'nın kendi bilinen bulgusundan zaten `1`, bu
case'in kendi payı `0` yeni bulgu). Sonra **aynı satır kod bloğu
dışına** eklendi (`[x](../../YOK.md)`, düz metin) ve komut tekrar koşuldu
→ "Kırık bağlantı: 2", yeni satır: `docs-site/src/content/docs/
troubleshooting.md -> ../../YOK.md`. Negatif karşılaştırma da doğrulandı
— aynı bağlantı kod bloğu dışına çıkınca kırmızı oluyor. Değişiklikler
`git checkout --` ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-012 — `docs/arsiv/**.md` muafiyet satırı sıfır değil, gerçek bir bayt sayısı gösterir

**Gerçek sonuç**
`python3 scripts/dokuman-bakim.py --denetle | grep "docs/arsiv"` →
```
docs/arsiv/**.md                 4797744   5496000   1999060  DAR (%13 boş)
```
Satır **0 değil**, gerçek bir bayt sayısı (4.797.744 B) gösteriyor —
case'in iddiası doğrulandı: `_dizin_boyutu` muaf ağacı kendi üstüne
uygulamıyor, bütçe hesaplaması anlamlı kalıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-013 — Tamamlanmış arşiv fazında işaretsiz kutu bulunur; düz metne çevrilince kaybolur

**Gerçek sonuç**
`docs/arsiv/fazlar/24-SQLITE.md:43`'teki `- [x] \`Tracon.Sqlite\` paketi
üretiliyor ...` satırı `- [ ] ...` yapıldı. `python3
scripts/dokuman-bakim.py --denetle` → çıkış **1**:
```
Tamamlanmış fazlarda işaretsiz kutu: ❌ 1 bulgu
  docs/arsiv/fazlar/24-SQLITE.md:43: işaretsiz kutu
```
Dosya adı **ve** satır numarası birebir. Sonra aynı satır kutu sözdizimini
tamamen kaybedecek şekilde düz metne çevrildi (`- \`Tracon.Sqlite\` paketi
üretiliyor ...`) ve komut tekrar koşuldu →
`Tamamlanmış fazlarda işaretsiz kutu: ✅ temiz` — bulgu kayboldu, case'in
iddiasıyla birebir. `git checkout --` ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-014 — Skill içinde `EnablePublicApiTracking=false` iddiası gerçek değerle çakışır

**Gerçek sonuç**
`Directory.Build.props:58` → `<EnablePublicApiTracking>true</...>`.
`.agents/skills/README.md`'ye geçici bir satır eklendi: "Test iddiası:
EnablePublicApiTracking=false olmalı." `python3 scripts/dokuman-bakim.py
--denetle` → çıkış **1**:
```
Doküman iddiası ↔ repo gerçeği: ❌ 1 bulgu
  .agents/skills/README.md:74: EnablePublicApiTracking=false deniyor, gerçek değer true
```
Dosya+satır+iddia edilen değer+gerçek değer birebir raporlandı. Kaynak:
`scripts/dokuman-bakim.py:892-917` (`dokuman_iddia_cakismalari`) —
`.agents/skills/**/*.md` içindeki her `EnablePublicApiTracking` iddiasını
`Directory.Build.props`'un gerçek değeriyle karşılaştırıyor. `git checkout
--` ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-015 — CI/kapanış skill'inde eski sync/secret deseni kopyası raporlanır

**Gerçek sonuç**
`.github/workflows/ci.yml`'nin sonuna `# gecici test deseni: Password|pwd
(MT-DKP-015)` satırı eklendi (kaynak: `scripts/dokuman-bakim.py:1243`,
`tekrarlanan_kapi_tanimlari` — literal `"Password|pwd"` ya da `"find src
tests samples docs .agents"` metnini arıyor). `python3
scripts/dokuman-bakim.py --denetle` → çıkış **1**:
```
Tekrarlanan kapı tanımları: ❌ 1 bulgu
  .github/workflows/ci.yml eski sync/secret desenini taşıyor
```
Case'in iddiasıyla birebir eşleşti — desen `scripts/kapi.py`'ye taşınmadıkça
(yani doğrudan CI dosyasında yeniden ortaya çıktıkça) kapı kırmızı kalıyor.
`git checkout --` ile geri alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-DKP-016 — Tablo biçimli bir ailede eklenen satır `00-INDEKS.md`'nin sayımından sapınca `(+1)` raporlanır

**Gerçek sonuç**
`docs/manuel-test/33-DOKUMAN-KAPILARI.md` (bu ailenin **spec** dosyası,
kayıt dosyası değil — case'in kendi prosedürü "tablo biçimli bir aile
(31–36) dosyasına satır ekle" diyor, kendi ailemi kullandım) sonuna geçici
bir `| 26 | \`MT-DKP-026\` | ... |` satırı eklendi, `00-INDEKS.md`'deki
`**25**` sayısına dokunulmadı. `python3 scripts/dokuman-bakim.py --denetle`
→ çıkış **1**:
```
Manuel kabul seti sayımı: ❌ 1 bulgu
  00-INDEKS.md: 33-DOKUMAN-KAPILARI.md 25 case yaziyor, dosyada 26 var (+1)
```
Sapma tam olarak `(+1)` — case'in iddiasıyla birebir. `git checkout --`
ile geri alındı; `grep -c "^| 2[0-9] "` sonrası satır sayısı yine 6 (20-25
arası), 26 kaldırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
