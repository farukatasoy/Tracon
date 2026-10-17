# Ek — Kabul case'lerini sete ekle

> `faz-tamamlama` Adım 3'ten açılır. Tetik: faz kodla tekrarlanamayan bir
> davranış getirdi — `🤖` ya da son çare olarak `👤`
> ([kabul basamakları](../../../ortak/test-seviyeleri.md)). İki kademede aynı.

`docs/manuel-test/` yayın öncesi gerçek ortamda koşulan kabul setidir; case'i
ajan koşar, insan son çaredir. Case'ler **alan dosyasına** eklenir, faz başına
yeni dosya açılmaz:

1. Fazın konusuna karşılık gelen dosyayı bul (`docs/manuel-test/<NN>-<ALAN>.md`)
2. Case'leri o dosyanın biçimiyle ekle — mevcut numaralandırmayı sürdür
3. Dosya başlığındaki **`Faz:`** satırına fazın numarasını ekle
4. 🚨 [`00-INDEKS.md`](../../../../docs/manuel-test/00-INDEKS.md)'deki **case sayısını
   güncelle** — dosya yeni olmasa da. Yeni alan dosyası açtıysan ayrıca satır ekle

> 🚨 **Madde 4 atlanırsa sapma her fazda büyür.** Ölçüldü (2026-09-08): indeks
> 1488 case yazıyordu, gerçek 1597 idi — 36 ailenin **17'si** bayat. Faz 157 bir
> aile dosyasına 170 satır ekledi ve indekse dokunmadı. Sayım artık
> `dokuman-bakim.py --denetle` içinde **kapıdır** (`manuel_test_sayim_kaymasi`).

Her case dört alan taşır: **ön koşul · adımlar · beklenen sonuç · alan kodu**.
Beklenen sonuç ölçülebilir olmalıdır — "çalışır" değil, "`429` ve `Retry-After`
başlığı döner".

## Kodla tekrarlanan davranış sete girmez (2026-10-17)

Kodla tekrarlanan davranış otomatik teste yazılır; kabul case'i olarak
**eklenmez**. Gerekçe: set 2.000 case'e ulaştı, yalnız 133'ü CI'ya
devredilmişti ve tam bir tur üç gün sürüyordu (2026-10-17 ölçümü). Faz başına
eklenen otomatikleştirilebilir case bu turu büyütüyor ama hiçbir kapı onu
koşmuyordu.

Case'in `Devir` satırı sınıfını taşır (`00-INDEKS.md` §9):

- `🤖 ajan koşar — <araç>` — ajan case'i kapanışta **koşar** ve gerçek çıktıyı
  DoD'ye yazar.
- `👤 insan gerekir — <denenen araç> · <neden yetmedi>` — yalnız ajan
  basamaktaki yolları denediyse. Denenen araç yazılmazsa denetçi bulgu yazar.

> Tam set koşumu ayrı bir iştir — [`manuel-test-kosumu`](../../manuel-test-kosumu/SKILL.md)
> skill'i yürütür ve sürüm öncesi yapılır. Bu ek yalnız **fazın kendi**
> case'lerini üretir.
