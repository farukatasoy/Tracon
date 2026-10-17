# Faz Planı Şablonu

> Bu dosya `faz-planlama` skill'inin şablonudur. Kopyala,
> `docs/NN-BUYUK-HARFLI-AD.md` olarak kaydet ve doldur.
>
> `<>` içindeki her yer tutucu değiştirilir. Doldurulmayan yer tutucu kalırsa
> plan **eksiktir**.
>
> Şablonun alt yarısındaki bölümler (Plandan Sapmalar'dan sonrası) plan anında
> **boş bırakılır** ve faz **çalışırken** dolar. Doküman kapanışta silinir
> (`faz-kapat`); bölümlerin kalıcı kısmı `faz-tamamlama` Adım 5'te yerine taşınır.
> Başlıkları silme — `faz-kapat` `Süreç Ölçümü`nü buradan okur.

---

```markdown
# Faz <NN> — <Kısa Ad>

> **Durum:** 📋 Planlandı (<YYYY-AA-GG>)
> **Plan onayı:** <ad>, <YYYY-AA-GG> · <yoksa "onaylanmadı — uygulama başlamaz">
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-<NN>**<, **F-<NN>**>
> **Genellik:** <hangi tüketici sınıfı; tüketici kodunda kalabilir mi; ikinci kanıt>
> **Karşı görüş:** <adayın karşı görüşü ve planın cevabı>
> **Önkoşul:** [Faz <N>](<N>-<AD>.md) — <neden gerekli> · <yoksa "Yok">
> **Paketler:** `Tracon.<X>`, `.<Y>`
> **Yeni paket:** <Yok · veya ad + K-007 gerekçesi> · **Migration:** <Yok · veya "gerekli — numara uygulama anında alınır">
> **Public API:** <Büyümüyor · veya "büyüyor — `1.0.0` GA'dan (K-603) önce ucuz">
> **Tüketici yüzeyi:** <Yok · veya site sayfaları: `concepts/runs.md`, `packages.md`, `ui.md` + ekran görüntüsü>
> · <sevk edilen: Yok · veya XML `<example>`, `src/<Paket>/README.md`, `capabilities.md` satırı>
> **Manuel test alanı:** <`docs/manuel-test/<NN>-<ALAN>.md` — case'ler oraya eklenir>

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-0NN\|K-0MM" docs/KARARLAR.md
   ```
   **K-0NN** (<tek cümlelik özet>), **K-0MM** (<tek cümlelik özet>)
3. Önceki fazdan devir — kapanan fazın dokümanı silinir; devir **bu dokümanın**
   `## Önceki Fazdan Devir` bölümüne yazılır (`faz-tamamlama` Adım 6). Kapanmış
   bir fazın tam metni gerçekten gerekirse `FAZ-DEFTERI.md` satırındaki işaretçi:
   ```bash
   git show <sha>:<yol> | awk '/## Sonraki Faza Devir Notu/,0'
   ```
   <Neden gerekli — hangi sözleşmeyi devralıyor>
4. Alan hafızası (bu faz <n> alana dokunuyor):
   [`hafiza/<alan>.md`](hafiza/<alan>.md) (<neden>)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) bölüm <n> (<konu>)

---

## Amaç

<Bir paragraf: bu faz neyi çözer. Kim, ne kazanır.>

- **F-<NN>** — <tek cümlelik kapsam>

### Bugün ne çalışmıyor — doğrulanmış kanıt

<Kanıtlar `dosya:satır` ile. Her satır Adım 1'de doğrulanmış olmalıdır.
Doğrulama tarihini yaz.>

| Kanıt | Gözlem |
|---|---|
| [`<dosya>.cs:<satır>`](../src/<yol>) | <tek cümlelik gözlem> |

> Kanıtlar <YYYY-AA-GG> tarihinde doğrulandı.

---

## <NN>.1 — <İlk tasarım başlığı>

<Tasarım. Diyagram gerekiyorsa Mermaid.>

## <NN>.2 — <İkinci tasarım başlığı>

<...>

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// Tracon.Abstractions
public interface I<Ad>
{
    ValueTask<T> <Metot>Async(<parametreler>, CancellationToken cancellationToken = default);
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/<yol>` | <Reader/Operator/Admin> | <tek cümle> |

### Arayüz payı

<Arayüze dokunuluyorsa bundle payı **gzip KB olarak**. Dokunulmuyorsa "Yok".>

---

## Planlanan Dosya Listesi

```
src/Tracon.<Paket>/
├── <Klasor>/
│   ├── <Dosya>.cs
│   └── <Dosya>.cs
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| <somut bozulma> | <Birim · Sözleşme · Fonksiyonel · E2E · Manuel> | `<Ad>Tests` |

Her yeni kod yolu için beş soru cevaplanır ve cevabı bu tabloya girer:
iptal · eşzamanlılık · boş/aşırı girdi · başka kiracının kaydı · alt sistem hatası.

Sözleşme testi gerekiyorsa taban sınıf `src/Tracon.Testing.Contracts.Xunit/Contracts/`
altına, türetilen sınıf her sağlayıcının `tests/*/Contracts/` dizinine — hem
bellek içi hem üç SQL sağlayıcısı üzerinde koşar.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/<NN>-<ALAN>.md` içine eklenecek case'lerin
> taslağı. Otomatikleştirilebilenler kapanışta koşulur; fiziksel/görsel
> olanlar `👤 insan gerekir` diye işaretlenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | <durum> | <komut veya tıklama> | <ölçülebilir çıktı> |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | <soru> | A: <…> · B: <…> | <A veya B, gerekçesiyle> |

---

## Bitiş Ölçütleri (DoD)

- [ ] <ölçülebilir davranış: komut → beklenen çıktı>
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/<NN>-<ALAN>.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] <site etkisi varsa> `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] <arayüze dokunulduysa> `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# <ne doğrulanıyor>
curl -s http://localhost:5081/tracon/api/<yol>
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| <risk> | <önlem> |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: `faz-kapat`
> işaretsiz kutuda durur. `faz-kapat` bu tabloyu `docs/FAZ-DEFTERI.md`'ye taşır;
> eşik **167**'den itibaren boş bir değer hücresinde kapanışı durdurur.
> `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir **karar** arar.
> Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.
```
