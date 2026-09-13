---
name: faz-uygulama
description: Bir fazın kodu yazılırken uygulanacak protokol — planın yapısal iddiasını ölçme, davranış başına test seviyesi seçimi, imza-gövde takibi, tuzak kontrol listesi ve erken kapı koşumu. Tracon'de kusurların tamamı bu adımda doğar; faz-baslangic okuma protokolüdür, faz-tamamlama kapanış protokolüdür, bu ikisinin arasındaki iş buradadır.
---

# Faz Uygulama Protokolü

Zincirdeki yeri:

```
faz-planlama → docs/NN-*.md → faz-baslangic → [faz-uygulama] → faz-denetim → faz-tamamlama
```

Amaç tek şeydir: **kusuru üretim yerine burada bulmak.** Kapanıştaki dört kapı
bir güvenlik ağıdır, tasarım aracı değildir. Bu protokol kapıya gelmeden önce
neyin yapılacağını söyler.

Bu skill kod yazmayı bilen bir oturuma **ne zaman durup ölçeceğini** öğretir.

---

## Adım 1 — 🚨 Planın YAPISAL iddiasını kabul etmeden ölç

Plan doğru bir hedef anlatır ama **konum, sıra ve katman** iddiaları bayatlar.
Yanlış konumdaki kod derlenir, testten geçer ve yalnız gerçek senaryoda çöker.

**Ölçüldü (Faz 48, K-320).** Plan guard'ı "boru hattının en dışına" koyuyordu.
Tek bir `grep` o konumun tool çağrı turlarını göremediğini gösterdi; fazın
yarısı yeniden konumlandırmaya dönüştü.

Kod yazmadan önce planın her yapısal cümlesini bir komuta çevir:

```bash
# "X, Y'nin dışında/içinde/öncesinde çalışır" -> gerçekten orada mı?
grep -rn "class RunRecordingAgent" src/
grep -rn "await agent.RunStreamingAsync\|MoveNextAsync" src/Tracon.Core/
```

İddia düşerse **kod yazma**; plandan sapmayı fazın dokümanına yaz ve devam et.
Sapma gizlenmez — gerekçesi sonraki oturumun en değerli bilgisidir.

Bu adım [`ortak/kurtarma.md`](../../ortak/kurtarma.md) kataloğunda **`KR-07`**
(plan sapması) rampasıdır; sapma fazın kapsamını değiştiriyorsa `KR-08`'e geç.

---

## Adım 2 — Her davranış için test seviyesini seç

Birim testi yeterli sanmak bu repoda **sekiz kez** bedel ödetti. Sınır tablosu
ve hata modu soruları tek kaynakta:
[`.agents/ortak/test-seviyeleri.md`](../../ortak/test-seviyeleri.md). Bir
davranış bir **sınırı** geçiyorsa (DI · HTTP · kiracı · akış · depo · paket),
o sınırın olduğu seviyede test edilir — yeşil bir birim testi bunu kanıtlamaz.

---

## Adım 3 — Sözleşmeyi önce düşen testle sabitle

Yeni bir davranış sözleşmesi (arayüz, HTTP ucu, depo metodu) yazarken testi
**önce** yaz ve **düştüğünü gör**. Düşmeyen bir test bir şey kanıtlamaz.

Test kırmızı olmuyorsa iki olasılık vardır: davranış zaten vardır (o zaman faz
kapsamı yanlıştır) veya test yanlış seviyededir (Adım 2).

---

## Adım 4 — 🚨 İmza değiştirmek ile gövdeyi kullanmak İKİ AYRI ADIMDIR

Yeni bir alan veya parametre eklediğinde çağrı zincirindeki **her katmanın
gövdesini** elle izle. Derleyici imzayı zorlar, gövdeyi zorlamaz.

**Ölçüldü (Faz 20).** `RunEventWriter.CompleteAsync`'e `cost` parametresi
eklendi; nesne başlatıcıya `Cost = cost` yazılmadı. **1068 test yakalamadı.**

```bash
grep -rn "CompleteAsync" src/          # her çağıran
grep -rn "new RunRecord\b" src/        # her üretim noktası
```

Aynı sebeple: **struct alanını atamamak `default` bırakır.** Atanmayan
`JsonElement` `Undefined` olur ve o kaydı içeren **liste ucunun tamamı** çöker.
Kayıt üreten her kod yolunda zorunlu olmayan alanları da doldur.

---

## Adım 5 — Kapıları erken ve dar koş

Kapılar ucuzdur (sıcak build ~5 sn). İlk anlamlı değişiklikten sonra koş; faz
sonuna biriktirme.

```bash
dotnet build Tracon.slnx -c Release -p:TraconFrontendEnabled=false
dotnet test tests/Tracon.Core.UnitTests -c Release --no-build
```

Arayüze dokunuyorsan `-p:TraconFrontendEnabled=false` **kullanma** — E2E
testleri gömülü varlıkları arar ve koşum asılı kalır.

`dotnet test` dakikalarca asılı kalıyorsa öksüz MSBuild düğümlerine bak
(`MSBUILDDISABLENODEREUSE=1`). Ayrıntı: `docs/hafiza/test-altyapisi.md`.

---

## Adım 6 — Tuzak kontrol listesi

Kod yazarken bu altısına dokunduysan ilgili kuralı uygula:

| Dokunduğun şey | Kural |
|---|---|
| `span`, `scope`, `AsyncLocal` | Çağıran metodun **kendi gövdesinde** başlat; akışlı yolda **her `MoveNextAsync` öncesi** tekrarla. Vaka kaydı: [`docs/hafiza/cekirdek-calistirma.md`](../../../docs/hafiza/cekirdek-calistirma.md) |
| Tool tanımı | Tool'un gördüğü servis sağlayıcı **boştur**. Bağımlılık **kurulum anında** alınır (`new BenimTool(provider)` + fabrika kaydı, K-218) |
| Yeni kayıt tipi | Zorunlu olmayan alanları da doldur; `default` struct seri hâle getirmeyi çökertir |
| Ekran metni | `en.ts` **ve** `tr.ts`. Sunucu yanıtı çevrilmez (K-232) |
| Playwright locator | Varsayılan alt dize eşler. Panel başlığı sayfa başlığıyla aynıysa `Exact = true` veya `.First` |
| `secret` | Dosyaya **ve veritabanına** yazılmaz. Kayıtta yalnız yapılandırma anahtarının **adı** durur (K-059) |

---

## Adım 7 — Manuel kabul case'ini şimdi yaz, sonra değil

Fazın DoD satırlarını doğrularken çalıştırdığın her komut bir manuel case
taslağıdır. O anda yaz; kapanışta hatırlamaya çalışma.

Her case dört alan taşır: **ön koşul · adımlar · beklenen sonuç · alan kodu**.
Biçim ve nereye ekleneceği `faz-tamamlama` Adım 3'tedir.

---

## Yazılmayacaklar

- **`TODO` yorumu.** Açık iş kalemi koda değil dokümana yazılır.
- **Sahte nesne, yer tutucu, `NotImplementedException`.** Başladıysan bitir.
- **Atlanmış veya devre dışı bırakılmış test.** Düşen test bir bilgidir; sustur­mak o bilgiyi siler.
- **Bastırılmış analyzer tanısı** — gerekçesi koda ve `docs/KARARLAR.md`'ye yazılmadan.
- **Planda olmayan public API büyümesi** — gerekçesi faz dokümanına yazılmadan.
