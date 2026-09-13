---
name: faz-baslangic
description: Bir faza (docs/NN-*.md) başlarken uygulanacak açılış protokolü — minimum okuma kümesi, sıralı keşif ve bütçe farkındalığı. Tracon'in dokümanları birikimlidir; hepsini okumak oturumun bütçesini bitirir. Bu skill ne okunacağını ve neyin okunmayacağını söyler.
---

# Faz Başlangıç Protokolü

Amaç tek şeydir: **fazı doğru bilgiyle, en az okumayla başlatmak.**

Bu repoda dokümanlar birikimlidir. `KARARLAR.md` 457 KB, `arsiv/` dosyaları
onlarca KB'dir. Hepsini okumak bağlamın yarısını harcar ve kod yazacak yer
bırakmaz. Ölçüldü (2026-08-03): eski protokolle bir faz **kod okumadan önce**
~125k token doküman yüküyle başlıyordu.

---

## Adım 1 — Sabit okuma kümesi (her fazda aynı)

Sırayla, tamamı:

1. `AGENTS.md` — zaten yüklü
2. [`MEMORY.md`](../../../MEMORY.md) — 5 KB
3. Fazın kendi dokümanı: `docs/NN-*.md`
   — kapanmış fazlar (00–89) `docs/arsiv/fazlar/NN-*.md` altındadır (Faz 77);
     yeri [`docs/YOL-HARITASI.md`](../../../docs/YOL-HARITASI.md) satırındaki bağlantıdır.
   — o kayıtlar **damıtılmıştır** (Faz 90): planın gövdesi değil, fazın bıraktığı
     kalıcı bilgi (sapmalar, kararlar, denetim bulguları, devir notu) durur.
     Plana gerçekten bakman gerekirse kaydın içindeki `git show <sha>:<yol>`
     komutunu koştur — **önce kaydı oku, tam metni refleksle açma.**

Bu üçü ~10k token'dır. Başka hiçbir dosya bu adımda okunmaz.

---

## Adım 2 — Fazın "Bu Faza Başlarken" listesini uygula

Her faz dokümanı bir okuma listesi taşır. **O listeyi olduğu gibi izle**, fazlasını
okuma. Listede `KARARLAR.md` kalemleri varsa tamamını değil, yalnız o kalemleri oku:

```bash
grep -n "K-059" docs/KARARLAR.md
sed -n '120,121p' docs/KARARLAR.md
```

Liste bir önceki fazın dokümanına yolluyorsa yalnız **"Sonraki Faza Devir Notu"**
bölümünü oku, dokümanın tamamını değil:

```bash
awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md
```

---

## Adım 3 — Dokunacağın alanın hafızasını aç

[`docs/hafiza/00-INDEKS.md`](../../../docs/hafiza/00-INDEKS.md) alan → dosya
eşlemesinin **tek kaynağıdır**. Oradan **yalnız ilgili** satırın dosyasını oku;
indeksi baştan sona okuma.

Bir alan birden çok dosyaya bölünmüş olabilir (ör. DI kaydı ile HTTP ucu ayrı,
test yazımı ile test koşumu ayrı). Her dosyanın başlığı kardeşine yollar.

Belirli bir şey arıyorsan indeksi hiç açma: `grep -rn "AsyncLocal" docs/hafiza/`.
Nerede yaşadığını bilmediğin bir şey için `docs/hafiza/kod-haritasi.md`.

---

## Adım 4 — Kodu hedefli oku

Doküman değil, **kod** gerçektir. Ama kodu da taramayla değil, adresli oku:

```bash
grep -rn "IRunStore" src/Tracon.Abstractions/   # sözleşme
grep -rn "class PostgresRunStore" src/              # uygulama
```

Bir sözleşmeyi değiştireceksen önce sözleşme testine bak —
`tests/Tracon.PostgreSql.IntegrationTests/Contracts/` altındaki soyut sınıflar
hem bellek içi hem PostgreSQL uygulamasında koşar.

---

## Adım 5 — MAF tipi kullanacaksan imzayı doğrula

MAF'ın .NET dokümanı çoğu sayfada eksiktir ve tip adları tahmin edilemez
(`AgentResponse`, `AgentRunResponse` değil). Yeni bir tip kullanmadan önce
`maf-api-kesfi` skill'ini çalıştır.

---

## Adım 6 — Sınırı erken çalıştır

Kapılar ucuzdur (sıcak build ~5 sn, 1070 test ~32 sn). İlk anlamlı değişiklikten
sonra hemen çalıştır; faz sonuna biriktirme.

```bash
dotnet build Tracon.slnx -c Release -p:TraconFrontendEnabled=false
dotnet test tests/Tracon.Core.UnitTests -c Release --no-build
```

---

## Adım 7 — Kod yazmaya `faz-uygulama` ile geç

Okuma bitti. Kod yazma protokolü ayrı bir skill'dedir ve **ilk kod satırından
önce** uygulanır: planın yapısal iddiasını ölçme, davranış başına test seviyesi
seçimi, imza-gövde takibi.

```
[faz-baslangic] → faz-uygulama → faz-denetim → faz-tamamlama
```

Okuma bütçesi fazın **ortasında** biterse faz doğaçlanmaz:
[`ortak/kurtarma.md`](../../ortak/kurtarma.md) kataloğundaki **`KR-09`**
(bağlam sisi / devir) rampası koşar.

---

## Okunmayacaklar

Faz açılışında **hiçbir koşulda** baştan sona okunmaz:

- `docs/KARARLAR.md` — indeksi ve grep'i var
- `docs/arsiv/*.md` — yalnız "neden böyle olmuş?" sorusunda grep'lenir
- Tamamlanmış fazların dokümanları — yalnız devir notu bölümü
- `docs/arsiv/BEYIN-FIRTINASI.md` — tarihsel kayıt; faz dokümanı geçerlidir
- `docs/MAF-GENISLEME-NOKTALARI.md` — yalnız MAF'a dokunurken

Bunlardan birine gerçekten ihtiyaç duyduğunda **neden** gerektiğini bir cümleyle
söyle, sonra oku. Refleksle okuma.
