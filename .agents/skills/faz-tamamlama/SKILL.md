---
name: faz-tamamlama
description: Bir fazın (docs/NN-*.md) kodunu bitirdikten sonra çalıştırılacak kapanış protokolü — doğrulama kapıları, doküman senkronizasyonu, karar defteri ve hafıza güncellemesi. AgentPrism'de her faz bu protokolle kapanır; atlanırsa sonraki oturum yanlış dokümanla çalışır.
---

# Faz Tamamlama Protokolü

Bu skill, bir fazın kodu bittiğinde çalıştırılır. Amacı tek bir şeydir: **sonraki oturumun doğru bilgiyle başlaması.**

AgentPrism fazlar hâlinde ve çoğu zaman **ayrı sohbetlerde** geliştirilir. Sonraki oturum bu depoyu sıfırdan okur. Dokümanlar gerçeği yansıtmıyorsa, sonraki oturum yanlış API'ye göre kod yazar ve zaman kaybeder. Bu daha önce yaşandı: plan `AgentRunResponse` diyordu, gerçek tip `AgentResponse` idi.

---

## Adım 1 — Doğrulama kapıları

Dördü de sıfır uyarı vermelidir. Bir tanesi bile kırmızıysa faz **bitmemiştir**.

```bash
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

Ek olarak sır taraması:

```bash
grep -rIn -E "sk-[a-z]+-[A-Za-z0-9_-]{24,}|AVNS_[A-Za-z0-9]{12,}|(Password|pwd)=[^ \";']{6,}" . \
  --exclude-dir=.git --exclude-dir=artifacts --exclude-dir=node_modules
```

Desen, ön ekten sonra en az 24 karakter arar; bu yüzden dokümanlardaki örnekler
(`sk-...`) yanlış pozitif üretmez. Çıktı boş olmalıdır — sırlar yalnızca
`dotnet user-secrets` içinde yaşar.

---

## Adım 2 — Örnek uygulamayı gerçekten çalıştır

Birim testleri geçmesi yetmez. `samples/AgentPrism.Api` ayağa kalkmalı ve fazın vaat ettiği davranışı göstermelidir.

```bash
cd samples/AgentPrism.Api
dotnet run --no-build -c Release --urls http://localhost:5081
# başka bir terminalde: fazın DoD bölümündeki curl komutlarını çalıştır
```

Sonuçları faz dokümanının DoD tablosuna **gerçek çıktı olarak** yaz. "Çalışıyor" yeterli değil; ne döndüğü yazılmalı.

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

| Dosya | Ne güncellenir |
|-------|----------------|
| `docs/MIMARI.md` | "Güncel Durum" tablosu, katman diyagramı, MAF imzaları, çalıştırma yolu |
| `docs/KARARLAR.md` | Fazda alınan **her** mimari karar, gerekçesiyle ve K-NNN numarasıyla |
| `MEMORY.md` | Keşfedilen codepath'ler, desenler ve **tuzaklar** (AGENTS.md'de olmayanlar) |
| `README.md` | Yol haritası tablosundaki durum sütunu; gerekirse hızlı başlangıç örneği |
| `AGENTS.md` | Yalnızca kalıcı bir çalışma kuralı değiştiyse |

**Çapraz kontrol:** Dokümanlar birbiriyle çelişmemelidir. Şu taramayı yap:

```bash
# Bayatlamış API adları var mı?
grep -rn "ArtikOlmayanTipAdi\|KaldirilanMetot" docs/*.md README.md

# Faz durumları tutarlı mı?
grep -n "Durum:" docs/0*.md
grep -n "Tamamlandı\|Planlandı\|Sıradaki" README.md docs/MIMARI.md
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

Şu soruya dürüst cevap ver:

> Bu depoyu hiç görmemiş bir agent, yalnızca `AGENTS.md` + `docs/` okuyarak sonraki fazı doğru şekilde yapabilir mi?

Cevap "hayır" ise, eksik olan bilgiyi ilgili dokümana yaz. Bu protokolün tek amacı budur.
