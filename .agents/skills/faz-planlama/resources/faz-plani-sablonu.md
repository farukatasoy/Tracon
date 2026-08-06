# Faz Planı Şablonu

> Bu dosya `faz-planlama` skill'inin şablonudur. Kopyala,
> `docs/NN-BUYUK-HARFLI-AD.md` olarak kaydet ve doldur.
>
> `<>` içindeki her yer tutucu değiştirilir. Doldurulmayan yer tutucu kalırsa
> plan **eksiktir**.
>
> Şablonun alt yarısındaki bölümler (Plandan Sapmalar'dan sonrası) plan anında
> **boş bırakılır**; `faz-tamamlama` doldurur. Başlıkları silme — silinirse
> kapanış adımı unutulur.

---

```markdown
# Faz <NN> — <Kısa Ad>

> **Durum:** 📋 Planlandı (<YYYY-AA-GG>)
> **Kaynak:** [UCUNCU-FAZ-ADAYLARI.md](UCUNCU-FAZ-ADAYLARI.md) · **F-<NN>**<, **F-<NN>**>
> **Önkoşul:** [Faz <N>](<N>-<AD>.md) — <neden gerekli> · <yoksa "Yok">
> **Paketler:** `AgentPrism.<X>`, `.<Y>`
> **Yeni paket:** <Yok · veya ad + K-007 gerekçesi> · **Migration:** <Yok · veya "gerekli — numara uygulama anında alınır">
> **Public API:** <Büyümüyor · veya "büyüyor — Faz 7'den önce ucuz">

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
3. [`<onceki-faz>.md`](<onceki-faz>.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/<onceki-faz>.md
   ```
   <Neden bu bölüm gerekli — hangi sözleşmeyi devralıyor>
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
// AgentPrism.Abstractions
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
src/AgentPrism.<Paket>/
├── <Klasor>/
│   ├── <Dosya>.cs
│   └── <Dosya>.cs
```

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `<Ad>Tests` | <tek cümle> |

Sözleşme testi gerekiyorsa `tests/Shared/Contracts/` altına — hem bellek içi
hem üç SQL sağlayıcısı üzerinde koşar.

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
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] <arayüze dokunulduysa> `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# <ne doğrulanıyor>
curl -s http://localhost:5081/agentprism/api/<yol>
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| <risk> | <önlem> |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
```
