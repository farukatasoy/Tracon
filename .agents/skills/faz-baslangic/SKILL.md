---
name: faz-baslangic
description: Bir faza (docs/NN-*.md) başlarken uygulanacak açılış protokolü — minimum okuma kümesi, sıralı keşif ve bütçe farkındalığı. AgentPrism'in dokümanları birikimlidir; hepsini okumak oturumun bütçesini bitirir. Bu skill ne okunacağını ve neyin okunmayacağını söyler.
---

# Faz Başlangıç Protokolü

Amaç tek şeydir: **fazı doğru bilgiyle, en az okumayla başlatmak.**

Bu repoda dokümanlar birikimlidir. `KARARLAR.md` 115 KB, `arsiv/` dosyaları
onlarca KB'dir. Hepsini okumak bağlamın yarısını harcar ve kod yazacak yer
bırakmaz. Ölçüldü (2026-08-03): eski protokolle bir faz **kod okumadan önce**
~125k token doküman yüküyle başlıyordu.

---

## Adım 1 — Sabit okuma kümesi (her fazda aynı)

Sırayla, tamamı:

1. `AGENTS.md` — zaten yüklü
2. [`MEMORY.md`](../../../MEMORY.md) — 4 KB
3. Fazın kendi dokümanı: `docs/NN-*.md`

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
awk '/## Sonraki Faza Devir Notu/,0' docs/20-MALIYET-VE-GOSTERGE-PANELI.md
```

---

## Adım 3 — Dokunacağın alanın hafızasını aç

`MEMORY.md`'deki yönlendirme tablosundan **yalnız ilgili** alan dosyasını oku.
Faz `scope`'una göre tipik seçim:

| Faz konusu | Alan dosyası |
|---|---|
| Yeni HTTP ucu, DI kaydı | `docs/hafiza/aspnetcore-di.md` |
| Yeni tablo/migration/sorgu | `docs/hafiza/postgresql.md` |
| Yeni MAF tipi, context provider | `docs/hafiza/maf-api.md` |
| Workflow yürütmesi | `docs/hafiza/workflows.md` |
| Yeni paket, csproj, analyzer | `docs/hafiza/build-ve-analyzer.md` |
| Yeni ekran/bileşen | `docs/hafiza/frontend.md` |
| Yeni test tipi | `docs/hafiza/test-altyapisi.md` |
| Kayıt zinciri, metrik, sürüm | `docs/hafiza/cekirdek-calistirma.md` |

Nerede yaşadığını bilmediğin bir şey için `docs/hafiza/kod-haritasi.md`.

---

## Adım 4 — Kodu hedefli oku

Doküman değil, **kod** gerçektir. Ama kodu da taramayla değil, adresli oku:

```bash
grep -rn "IRunStore" src/AgentPrism.Abstractions/   # sözleşme
grep -rn "class PostgresRunStore" src/              # uygulama
```

Bir sözleşmeyi değiştireceksen önce sözleşme testine bak —
`tests/AgentPrism.PostgreSql.IntegrationTests/Contracts/` altındaki soyut sınıflar
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
dotnet build AgentPrism.slnx -c Release -p:AgentPrismFrontendEnabled=false
dotnet test tests/AgentPrism.Core.UnitTests -c Release --no-build
```

---

## Okunmayacaklar

Faz açılışında **hiçbir koşulda** baştan sona okunmaz:

- `docs/KARARLAR.md` — indeksi ve grep'i var
- `docs/arsiv/*.md` — yalnız "neden böyle olmuş?" sorusunda grep'lenir
- Tamamlanmış fazların dokümanları — yalnız devir notu bölümü
- `docs/BEYIN-FIRTINASI.md` — tarihsel kayıt; faz dokümanı geçerlidir
- `docs/MAF-GENISLEME-NOKTALARI.md` — yalnız MAF'a dokunurken

Bunlardan birine gerçekten ihtiyaç duyduğunda **neden** gerektiğini bir cümleyle
söyle, sonra oku. Refleksle okuma.
