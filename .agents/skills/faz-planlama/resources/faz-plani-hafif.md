# Faz Planı Şablonu — Hafif Kademe

> Bu dosya `faz-planlama` skill'inin **hafif kademe** şablonudur. Kademeyi
> skill'in "Kademe" bölümü belirler; tetik varsa tam şablonu kullan:
> [`faz-plani-sablonu.md`](faz-plani-sablonu.md).
>
> Hedef **tek sayfa**dır. Plan, uygulama ve kapanış aynı oturumda olabilir;
> devir teslim kalitesi istenmez. `<>` içindeki her yer tutucu değiştirilir.
> Alttaki dört başlık faz çalışırken dolar ve kapanışta `faz-kapat` dokümanı
> siler — başlıkları silme, `faz-kapat` `Kademe`, DoD ve `Süreç Ölçümü`nü okur.

---

```markdown
# Faz <NN> — <Kısa Ad>

> **Durum:** 📋 Planlandı (<YYYY-AA-GG>)
> **Kademe:** hafif — migration yok · public API'den satır silinmez · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** <ad>, <YYYY-AA-GG>
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-<NN>**<, **F-<NN>**>
> **Paketler:** `Tracon.<X>`
> **Tüketici yüzeyi:** <Yok · veya site sayfası, paket `README.md`'si, `capabilities.md` satırı>

## Amaç

<İki-dört cümle. Bugün ne çalışmıyor — kanıt `dosya:satır`. Faz bitince ne doğru olur.>

## Bitiş Ölçütleri (DoD)

- [ ] <Ölçülebilir madde — "çalışır" değil, "`409` döner">
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| <davranış> | <Birim · Fonksiyonel · E2E> | <sınıf/yöntem> |

Beş soru — bu faza dokunanı tek satırla yaz: iptal · eşzamanlılık · boş/aşırı
girdi · başka kiracının kaydı · alt sistem hatası. Kodla tekrarlanmayan
davranış varsa `🤖` case'i burada an (`👤` son çaredir); gerisi otomatik testtir
([kabul basamakları](../.agents/ortak/test-seviyeleri.md)).

## Açık Sorular

<Yoksa "Yok". Varsa uygulama başlamadan kullanıcıya sorulur.>

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.
```
