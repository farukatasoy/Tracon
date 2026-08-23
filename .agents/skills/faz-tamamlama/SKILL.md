---
name: faz-tamamlama
description: Bir fazın (docs/NN-*.md) kodunu bitirdikten sonra çalıştırılacak kapanış protokolü — doğrulama kapıları, doküman senkronizasyonu, karar defteri ve hafıza güncellemesi. AgentPrism'de her faz bu protokolle kapanır; atlanırsa sonraki oturum yanlış dokümanla çalışır.
---

# Faz Tamamlama Protokolü

Bu skill, bir fazın kodu bittiğinde çalıştırılır. Amacı tek bir şeydir: **sonraki oturumun doğru bilgiyle başlaması.**

AgentPrism fazlar hâlinde ve çoğu zaman **ayrı sohbetlerde** geliştirilir. Sonraki oturum bu repoyu sıfırdan okur. Dokümanlar gerçeği yansıtmıyorsa, sonraki oturum yanlış API'ye göre kod yazar ve zaman kaybeder. Bu daha önce yaşandı: plan `AgentRunResponse` diyordu, gerçek tip `AgentResponse` idi.

---

## Adım 1 — Doğrulama kapıları

Dördü de sıfır uyarı vermelidir. Bir tanesi bile kırmızıysa faz **bitmemiştir**.

```bash
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

Yeni bir **paket** eklendiyse `dotnet pack` çıktısını say: paket sayısı beklenenle
uyuşmalıdır. Yeni paket ayrıca şunları ister — atlanırsa build veya test kırar:

- `src/<Paket>/README.md` (NuGet sayfasında görünür; `DependencyDirectionTests` zorlar)
- `AgentPrism.slnx` içine `<Project Path=... />`
- Meta pakete (`src/AgentPrism/AgentPrism.csproj`) `ProjectReference`
- `DependencyDirectionTests.AllowedReferences` içine bir satır

### 🚨 Senkronizasyon kopyası taraması (kapılardan ÖNCE)

**CI bu taramayı artık otomatik yapar** (`.github/workflows/ci.yml`,
"Senkronizasyon kopyası taraması" adımı, Kesif 2026-08-23 kalem 4). Burada
elle koşulması push'tan önce erken kapı — CI'ın bulacağı bir şeyi burada
yakalamak bir turu kurtarır. **Bu adım o yüzden atlanamaz** — Faz 57 atladığı
için `main`'i derlenmez hâlde bıraktı. Bulut senkronizasyon istemcisi
`<ad> 2.<uzantı>` kopyaları üretir; `.cs` kopyası CS0101 yağmuru, `.ts`
kopyası TS2741 verir. Beş kez yaşandı.

```bash
find src tests samples docs .agents \( -name "* 2.*" -o -name "* 2" \) \
  -not -path "*/node_modules/*" -not -path "*/obj/*" -not -path "*/bin/*"
```

Çıktı **boş olmalıdır**. Üç tuzak:

- **`git status` bu kopyaları göstermeyebilir** — bir kez `git add` edildiyse
  izlenen dosyadır ve "temiz" görünür. Taramayı `git status`'a güvenerek atlama.
- **`src` yetmez.** Faz 57'de kopyalar `tests/` altındaydı; yalnız `src`'ye
  bakan eski komut onları görmedi. `docs` ve `.agents` de taranır.
- **Kopya bir `.cs` dosyası olmak zorunda değil.** `-name "* 2.*"` tek başına
  **dizin** kopyasını kaçırır: noktası yoktur. Üç boş `resources 2/` ve
  `2026-08-13 2/` dizini tam bu yüzden aylarca durdu. Komut ikisini de arar.

Kopyaları sil (`git rm` gerekebilir), sonra `wwwroot`'u ve
`agentprism-frontend.stamp` damgasını da kaldır — damga durursa arayüz yeniden
gömülmez.

Ek olarak `secret` taraması — **CI bunu da otomatik yapar** ("Secret taraması"
adımı, aynı kesif kaydı kalem 5), burada koşmak yine erken kapıdır:

```bash
grep -rIn -E "sk-[a-z]+-[A-Za-z0-9_-]{24,}|AVNS_[A-Za-z0-9]{12,}|(Password|pwd)=[^ \";']{6,}" . \
  --exclude-dir=.git --exclude-dir=artifacts --exclude-dir=node_modules \
  --exclude-dir=manuel-test --exclude-dir=arsiv --exclude-dir=manuel-test-kosumu
```

Desen, ön ekten sonra en az 24 karakter arar. `docs/manuel-test/`,
`docs/arsiv/` ve `manuel-test-kosumu` skill kaynakları hariç tutulur —
bunlarda yerel Testcontainers/Docker varsayılanı `Password=agentprism` ve
sahte `sk-...-test-anahtari` değerleri **bilerek** vardır (Faz 79/80/81/87
emsali); hariç tutulmadan koşarsan bu satırlar taramayı boğar. Çıktı boş
olmalıdır — `secret`'lar yalnızca `dotnet user-secrets` içinde yaşar.

> Testlerde sahte `secret` literali kullanırken **tarama desenine uymayan** bir değer
> seçin. Yaşandı: `"sk-cok-gizli-..."` biçimindeki bir test sabiti taramayı
> kirletti ve sonraki oturum için gürültü üretecekti.

---

### Arayüze dokunulduysa: çeviri kapısı

Arayüz Faz 30'dan beri iki dillidir. Yeni bir ekran metni yalnız `en.ts`'e
eklenirse `tsc` durur, dolayısıyla **unutulamaz** — ama şunlar unutulabilir:

- Yeni metin gerçekten çevrildi mi, yoksa İngilizcesi mi kopyalandı?
  `i18n.test.ts` bunu denetler; birebir aynı kalması gereken teknik terimler
  testin içindeki listede **açıkça** yazılıdır. O listeye satır eklemek bir
  karardır, kısayol değil.
- Metin üzerine iddia kuran yeni E2E testi dili sabitledi mi?
  `Session.OpenAsync` varsayılanı `en-US`'tir (K-231).
- Yeni mesaj mevcut bir mesajın **öneki** mi? Playwright `GetByText` alt dizi
  eşler; önek çakışması testi strict mode ihlaliyle kırar.

## Adım 2 — Örnek uygulamayı gerçekten çalıştır

Birim testleri geçmesi yetmez. `samples/AgentPrism.Api` ayağa kalkmalı ve fazın vaat ettiği davranışı göstermelidir.

```bash
cd samples/AgentPrism.Api
dotnet run --no-build -c Release --urls http://localhost:5081
# başka bir terminalde: fazın DoD bölümündeki curl komutlarını çalıştır
```

Sonuçları faz dokümanının DoD tablosuna **gerçek çıktı olarak** yaz. "Çalışıyor" yeterli değil; ne döndüğü yazılmalı.

> 🚨 **Bu adım testlerin yakalamadığını yakalar.** Faz 6'da iki gerçek hata yalnızca
> burada ortaya çıktı: yapılandırmanın `Observability` bölümü hiç okunmuyordu
> (erken dönüş) ve kök span iç span'lerin ebeveyni olmuyordu (`AsyncLocal`).
> İkisi de 383 testten geçmişti. Varsayılan **dışı** bir yapılandırmayla da
> çalıştırın — varsayılanlar her zaman en çok test edilen yoldur:
>
> ```bash
> AgentPrism__Observability__SuccessSampleRatio=1 dotnet run --no-build -c Release
> ```

---

## Adım 3 — Manuel kabul case'lerini sete ekle

`docs/manuel-test/` yayın öncesi elle koşulan kabul setidir. **Faz kendi
case'lerini eklemezse set her fazda bir adım geride kalır** — bugün spec
dosyaları Faz 0–56'yı kapsıyor, sonrası boştur.

Case'ler **alan dosyasına** eklenir, faz başına yeni dosya açılmaz:

1. Fazın konusuna karşılık gelen dosyayı bul (`docs/manuel-test/<NN>-<ALAN>.md`)
2. Case'leri o dosyanın biçimiyle ekle — mevcut numaralandırmayı sürdür
3. Dosya başlığındaki **`Faz:`** satırına fazın numarasını ekle
4. Alan dosyası yoksa (gerçekten yeni bir alan) yenisini aç ve
   [`00-INDEKS.md`](../../../docs/manuel-test/00-INDEKS.md) durum tablosuna satır ekle

Her case dört alan taşır: **ön koşul · adımlar · beklenen sonuç · alan kodu**.
Beklenen sonuç ölçülebilir olmalıdır — "çalışır" değil, "`429` ve `Retry-After`
başlığı döner".

### Otomatikleştirilebilen case'i şimdi koş

Örnek uygulama zaten ayakta (Adım 2). `curl`/HTTP ile koşulabilen her case'i
**şimdi koş** ve gerçek çıktıyı fazın DoD tablosuna yaz.

Fiziksel veya görsel eylem isteyen case (mikrofon, dosya yükleme, göz denetimi)
`👤 insan gerekir` diye işaretlenir. Bu işaret bir eksiklik değil, koşum
planının girdisidir.

> Tam set koşumu ayrı bir iştir — [`manuel-test-kosumu`](../manuel-test-kosumu/SKILL.md)
> skill'i yürütür ve sürüm öncesi yapılır. Bu adım yalnız **fazın kendi**
> case'lerini üretir ve koşar.

---

## Adım 4 — Bağımsız denetim

Kodu yazan göz kendi kör noktasını göremez. `faz-denetim` skill'ini uygula:
taze bağlamlı bir denetçi yalnız DoD + `git diff` okur ve üç seviyede bulgu
üretir.

**🔴 bulgular kapanmadan faz bitmez.** Kapandıktan sonra Adım 1'in dört kapısı
**yeniden koşar** — düzeltme yeni kusur üretebilir.

🟡 bulgular ya kapanır ya gerekçesi faz dokümanına yazılır. 🟢 bulgular
`docs/ADAYLAR.md`'ye F-NN olarak gider.

---

## Adım 5 — Faz dokümanını gerçekleşenle hizala

`docs/NN-*.md` dosyasını aç ve şunları düzelt:

- [ ] Başlıktaki **Durum**: `Tamamlandı (YYYY-AA-GG)`
- [ ] **Plandan sapmalar** bölümü: uygulama sırasında alınan kararlar ve gerekçeleri
- [ ] **Gerçekleşen public API**: plandaki taslak imzalar değil, koddaki gerçek imzalar
- [ ] **Dosya listesi**: gerçekten oluşturulan dosyalar
- [ ] **Testler**: sınıf adları ve neyi doğruladıkları, test sayısı
- [ ] **DoD tablosu**: her satır ✅ veya gerekçeli açıklama
- [ ] **Denetim bulguları**: Adım 4'ün her bulgusu — seviye, sonuç (düzeltildi / gerekçelendi / devredildi)
- [ ] **Sonraki faza devreden notlar**: yarım kalan işler, yer tutucular, açık uçlar

> Plan ile gerçek arasındaki farkı **gizleme**. Fark, sonraki oturumun en değerli bilgisidir.

---

## Adım 6 — Sonraki fazın dokümanını devir teslim kalitesine çıkar

Bu adım en çok atlanan ve en pahalıya mal olan adımdır. Sonraki faz ayrı bir sohbette yapılacaksa, o doküman **tek başına yeterli** olmalıdır.

`docs/(NN+1)-*.md` dosyasına şunları ekle:

- [ ] **"Bu faza başlarken"** bölümü: hangi dosyalar hangi sırayla okunmalı
- [ ] **Devraldığı sözleşmeler**: bu fazda tamamlanan arayüzlerin birebir imzaları
- [ ] **Davranış sözleşmeleri**: mevcut testlerin doğruladığı kurallar tablosu
- [ ] **Bilinen tuzaklar**: bu fazda keşfedilen ve sonrakini etkileyecek şeyler (🚨 ile işaretle)
- [ ] **Dosya listesi**: oluşturulacak dosyaların önerilen düzeni
- [ ] Faz 0'da hazırlanmış ama henüz kullanılmamış altyapı (paket sürümü, csproj girdisi, MSBuild özelliği)

---

## Adım 7 — Yatay dokümanları güncelle

> 🚨 **Bu adım en pahalı adımdır.** Dosyaları baştan sona okuyup yeniden yazma.
> Her satır **nereye ait olduğu** yere yazılır; sıcak dokümana yığmak yasaktır.

| Ne öğrenildi | Nereye yazılır |
|---|---|
| Fazda alınan mimari karar | `docs/KARARLAR.md` — **sona ekle**, K-NNN ile |
| Keşfedilen tuzak / codepath | `docs/hafiza/<alan>.md` — **`MEMORY.md`'ye değil** |
| Alandan bağımsız, tekrar bedel ödeten ders | `MEMORY.md` "Her Oturumda Geçerli" (nadir) |
| "Faz N sonunda …" anlatı paragrafı | `docs/arsiv/FAZ-GECMISI.md` — **`MIMARI.md`'ye değil** |
| Pakete ne eklendiği | `docs/arsiv/PAKET-FAZ-GECMISI.md` |
| **Bugünkü** mimari değiştiyse (veri modeli, çalıştırma yolu, güvenlik sınırı) | `docs/MIMARI.md` — ilgili bölümü **düzelt**, altına ekleme yapma |
| MAF genişleme noktası kullanıldıysa | `docs/MAF-GENISLEME-NOKTALARI.md` |
| Yol haritası durumu | `README.md` tablosu (tek kaynak) |
| Kalıcı bir çalışma kuralı değiştiyse | `AGENTS.md` |
| **Kullanıcıya dönük davranış değiştiyse** | `docs-site/` — [`tuketici-dokuman-senkronu`](../tuketici-dokuman-senkronu/SKILL.md) skill'i |
| **Public tip, HTTP ucu veya yeni paket eklendiyse** | Sevk edilen metin ve yerel referans yüzeyi — aynı skill |
| `arsiv/BEYIN-FIRTINASI.md` kalemi yapıldı/reddedildi | üstünü çiz; gerekçe KARARLAR'a |

`AGENTS.md`'de faz durum tablosu **yoktur** — orada yalnız "sıradaki faz" satırı
vardır. Tam tabloyu yalnız `README.md`'de güncelle.

### 🚨 Tüketici yüzeyi senkronu

`docs/` Türkçe geliştirme günlüğüdür; `docs-site/` İngilizce **ürün
dokümantasyonudur** ve yayınlanır. İkisi karıştırılmaz. Site bayatlarsa kusur
kullanıcıya görünür — kod doğru olsa bile.

Ama site tek tüketici yüzeyi değildir. Pakete giren `///` XML dokümanı, paket
`README.md`'leri, paketlenen `agentprism.json`, sevk edilen
`AgentPrism.AgentMap.md` ve tüketicinin diskinde üretilen
`AgentPrism.LocalReference.md` de tüketiciye gider ve ayrı ayrı bayatlar.

**Bu iş [`tuketici-dokuman-senkronu`](../tuketici-dokuman-senkronu/SKILL.md)
skill'ine aittir. Onu uygula** — yüzey eşleme tablosu, kalite sözleşmesi ve dört
kapının tam sırası oradadır ve burada tekrarlanmaz.

Faz hiçbir tüketici yüzeyine dokunmadıysa skill koşmaz; gerekçesi faz dokümanına
yazılır ve aşağıdaki `--site-gerekce-yazildi` bayrağıyla geçilir.

### Bakım komutunu çalıştır (zorunlu)

```bash
python3 scripts/dokuman-bakim.py
python3 scripts/dokuman-bakim.py --site-denetle --taban <faz öncesi commit>
```

İkinci komut `docs-site` senkronunu denetler: faz kullanıcıya dönük bir yüzeye
(HTTP ucu, ekran, paket, public tip) dokunmuş ama site hiç değişmemişse çıkış
kodu 1 verir. Site gerçekten güncelleme gerektirmiyorsa gerekçesini faz
dokümanına yaz ve `--site-gerekce-yazildi` ile geç.

İlk komut iki iş yapar: `docs/KARARLAR-INDEKS.md` ve `docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md`
dosyalarını yeniden üretir (K-214, Faz 32'de ikiye ayrıldı) ve sıcak yol
bütçelerini denetler. **Çıkış kodu 0 olmalıdır.** Bütçe aşıldıysa içerik silinmez
— birikimli kısım `docs/arsiv/`'e veya `docs/hafiza/`'ya taşınır.

### Çapraz kontrol

```bash
# Faz durumu tek yerde mi, çelişki var mı?
grep -rn "Sıradaki faz" AGENTS.md README.md docs/arsiv/IKINCI-FAZ-YOL-HARITASI.md

# Bayatlamış API adı kaldı mı? (fazda kaldırdığın tipi yaz)
grep -rn "KaldirilanTipAdi" docs/ README.md src/
```

---

## Adım 8 — Karar defterine yaz

Fazda alınan her mimari karar `docs/KARARLAR.md` içine gider. İki tablo var:

- **Bölüm 1** — reddedilen işler ("bunu yapmadık, çünkü…")
- **Bölüm 2** — kalıcı tercihler (K-NNN numarası ile)

Format:

```
| **K-0NN — <karar>** | YYYY-AA-GG | <gerekçe: hangi kanıt, ölçüm veya kısıt> | <yeniden açılma koşulu> |
```

Kurallar:
- Gerekçesiz karar yazma. "İstemedik" yeterli değil.
- Ölçüm varsa sayıyı yaz ("13 bağımlılık → 2").
- Kullanıcı kararlarını `(kullanıcı kararı)` ile işaretle.

---

## Adım 9 — Commit

Kullanıcı istemedikçe commit **etme**. İstediğinde:

```bash
git status --short
git diff --stat
```

Ana dalda çalışılmaz; faz dalı kullanılır (`feature/phase-N-...`).

---

## Adım 10 — Siteyi yayınla

**Site kendiliğinden güncellenmez.** GitHub Pages bunu her push'ta yapıyordu;
K-542'den beri yapmıyor — CI'nin `site` işi yalnız derler ve kapıları koşar,
YAYINLAMAZ. Bu adım atlanırsa kod, doküman ve karar defteri günceldir ama
**kullanıcının gördüğü site bir önceki fazdan kalmadır** ve bunu hiçbir kapı
söylemez.

Adım 7 `docs-site/` içinde bir şey değiştirdiyse (veya `///` XML dokümanı,
`capabilities.md`, OpenAPI belgesi değiştiyse — üçü de üretilen sayfalara
girer):

```bash
./scripts/site-deploy.sh
```

Script derler, dört kapıyı koşar, `rsync`'ler ve konteyneri uzlaştırır. Kapılardan
biri kırmızıysa hiçbir şey yayınlanmaz. Sonra doğrula:

```bash
curl -sI https://agentprism.doayen.web.tr/ | head -1     # 200
curl -sI https://doayen.web.tr/ | head -1                # apex bozulmadi (405 = HEAD, normal)
```

🚨 **Fazın DEĞİŞTİRDİĞİ sayfayı canlıda aç.** `200` yalnız sitenin ayakta
olduğunu söyler, YENİ olduğunu değil.

---

## Kapanış kontrolü

Dört soruya dürüst cevap ver:

> 1. Bu repoyu hiç görmemiş bir agent, `faz-baslangic` skill'inin **sabit okuma
>    kümesiyle** (AGENTS.md + MEMORY.md + faz dokümanı) sonraki fazı doğru
>    başlatabilir mi?
> 2. Sıcak yol dokümanları bu fazda **büyüdü mü**? (`scripts/dokuman-bakim.py`)
> 3. Denetimin (Adım 4) **🔴 bulgusu kaldı mı**? Kaldıysa faz bitmemiştir.
> 4. Bu fazın vaat ettiği davranışı **bir kullanıcı** yayınlanan siteden **ve**
>    yerel referans dosyasından öğrenebilir mi? (Üç yüzey ayrıdır: site sayfası ·
>    `<example>` taşıyan XML dokümanı · `capabilities.md` satırı.)
> 5. Site **yayınlandı mı** (Adım 10)? Yeşil kapı yayın değildir — `dist/`
>    makinende durur, sunucuda değil.

1'e cevap "hayır" ise eksik bilgiyi **fazın kendi dokümanına** yaz — sıcak
dokümana değil.

2'ye cevap "evet" ise, eklediğin şey gerçekten bugünkü mimari mi, yoksa geçmiş
mi? Geçmişse `docs/arsiv/`'e taşı. Bu protokolün amacı sonraki oturumun **doğru
ve ucuz** başlamasıdır; ikisi birden olmadan faz kapanmaz.
