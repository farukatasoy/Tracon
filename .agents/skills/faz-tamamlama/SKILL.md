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

Ek olarak `secret` taraması:

```bash
grep -rIn -E "sk-[a-z]+-[A-Za-z0-9_-]{24,}|AVNS_[A-Za-z0-9]{12,}|(Password|pwd)=[^ \";']{6,}" . \
  --exclude-dir=.git --exclude-dir=artifacts --exclude-dir=node_modules
```

Desen, ön ekten sonra en az 24 karakter arar; bu yüzden dokümanlardaki örnekler
(`sk-...`) yanlış pozitif üretmez. Çıktı boş olmalıdır — `secret`'lar yalnızca
`dotnet user-secrets` içinde yaşar.

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

## Adım 3 — Faz dokümanını gerçekleşenle hizala

`docs/NN-*.md` dosyasını aç ve şunları düzelt:

- [ ] Başlıktaki **Durum**: `Tamamlandı (YYYY-AA-GG)`
- [ ] **Plandan sapmalar** bölümü: uygulama sırasında alınan kararlar ve gerekçeleri
- [ ] **Gerçekleşen public API**: plandaki taslak imzalar değil, koddaki gerçek imzalar
- [ ] **Dosya listesi**: gerçekten oluşturulan dosyalar
- [ ] **Testler**: sınıf adları ve neyi doğruladıkları, test sayısı
- [ ] **DoD tablosu**: her satır ✅ veya gerekçeli açıklama
- [ ] **Sonraki faza devreden notlar**: yarım kalan işler, yer tutucular, açık uçlar

> Plan ile gerçek arasındaki farkı **gizleme**. Fark, sonraki oturumun en değerli bilgisidir.

---

## Adım 4 — Sonraki fazın dokümanını devir teslim kalitesine çıkar

Bu adım en çok atlanan ve en pahalıya mal olan adımdır. Sonraki faz ayrı bir sohbette yapılacaksa, o doküman **tek başına yeterli** olmalıdır.

`docs/(NN+1)-*.md` dosyasına şunları ekle:

- [ ] **"Bu faza başlarken"** bölümü: hangi dosyalar hangi sırayla okunmalı
- [ ] **Devraldığı sözleşmeler**: bu fazda tamamlanan arayüzlerin birebir imzaları
- [ ] **Davranış sözleşmeleri**: mevcut testlerin doğruladığı kurallar tablosu
- [ ] **Bilinen tuzaklar**: bu fazda keşfedilen ve sonrakini etkileyecek şeyler (🚨 ile işaretle)
- [ ] **Dosya listesi**: oluşturulacak dosyaların önerilen düzeni
- [ ] Faz 0'da hazırlanmış ama henüz kullanılmamış altyapı (paket sürümü, csproj girdisi, MSBuild özelliği)

---

## Adım 5 — Yatay dokümanları güncelle

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
| `BEYIN-FIRTINASI.md` kalemi yapıldı/reddedildi | üstünü çiz; gerekçe KARARLAR'a |

`AGENTS.md`'de faz durum tablosu **yoktur** — orada yalnız "sıradaki faz" satırı
vardır. Tam tabloyu yalnız `README.md`'de güncelle.

### Bakım komutunu çalıştır (zorunlu)

```bash
python3 scripts/dokuman-bakim.py
```

İki iş yapar: `docs/KARARLAR-INDEKS.md`'yi yeniden üretir ve sıcak yol
bütçelerini denetler. **Çıkış kodu 0 olmalıdır.** Bütçe aşıldıysa içerik silinmez
— birikimli kısım `docs/arsiv/`'e veya `docs/hafiza/`'ya taşınır.

### Çapraz kontrol

```bash
# Faz durumu tek yerde mi, çelişki var mı?
grep -rn "Sıradaki faz" AGENTS.md README.md docs/IKINCI-FAZ-YOL-HARITASI.md

# Bayatlamış API adı kaldı mı? (fazda kaldırdığın tipi yaz)
grep -rn "KaldirilanTipAdi" docs/ README.md src/
```

---

## Adım 6 — Karar defterine yaz

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

## Adım 7 — Commit

Kullanıcı istemedikçe commit **etme**. İstediğinde:

```bash
git status --short
git diff --stat
```

Ana dalda çalışılmaz; faz dalı kullanılır (`feature/phase-N-...`).

---

## Kapanış kontrolü

İki soruya dürüst cevap ver:

> 1. Bu repoyu hiç görmemiş bir agent, `faz-baslangic` skill'inin **sabit okuma
>    kümesiyle** (AGENTS.md + MEMORY.md + faz dokümanı) sonraki fazı doğru
>    başlatabilir mi?
> 2. Sıcak yol dokümanları bu fazda **büyüdü mü**? (`scripts/dokuman-bakim.py`)

1'e cevap "hayır" ise eksik bilgiyi **fazın kendi dokümanına** yaz — sıcak
dokümana değil.

2'ye cevap "evet" ise, eklediğin şey gerçekten bugünkü mimari mi, yoksa geçmiş
mi? Geçmişse `docs/arsiv/`'e taşı. Bu protokolün amacı sonraki oturumun **doğru
ve ucuz** başlamasıdır; ikisi birden olmadan faz kapanmaz.
