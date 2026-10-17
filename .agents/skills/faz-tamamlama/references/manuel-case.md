# Ek — Manuel kabul case'lerini sete ekle

> `faz-tamamlama` Adım 3'ten açılır. **Tam kademede** her faz; **hafif
> kademede** yalnız 👤 gerektiren davranış için.

`docs/manuel-test/` yayın öncesi elle koşulan kabul setidir. Case'ler **alan
dosyasına** eklenir, faz başına yeni dosya açılmaz:

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

## Otomatikleştirilebilen case'i şimdi koş (tam kademe)

Örnek uygulama zaten ayakta (Adım 2). `curl`/HTTP ile koşulabilen her case'i
**şimdi koş** ve gerçek çıktıyı fazın DoD tablosuna yaz.

**Hafif kademede** bu case'ler manuel sete girmez: `curl`/HTTP ile koşulabilen
davranış otomatik teste (fonksiyonel test) yazılır.

Fiziksel veya görsel eylem isteyen case (mikrofon, dosya yükleme, göz denetimi)
her iki kademede de `👤 insan gerekir` diye işaretlenir. Bu işaret bir eksiklik
değil, koşum planının girdisidir.

> Tam set koşumu ayrı bir iştir — [`manuel-test-kosumu`](../../manuel-test-kosumu/SKILL.md)
> skill'i yürütür ve sürüm öncesi yapılır. Bu ek yalnız **fazın kendi**
> case'lerini üretir.
