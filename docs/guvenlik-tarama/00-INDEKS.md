# Tracon — Güvenlik Tarama Planı İndeksi

> Kapsamlı bir güvenlik taraması için 10 konu başlığına bölünmüş, bağımsız
> çalıştırılabilir prompt seti. Her `NN-<konu>.md` dosyası tek başına bir
> agent oturumuna verilebilir. Bu dosya ortak çerçeveyi ve kullanım
> talimatını taşır; konu dosyaları bunu tekrarlamaz, buraya referans verir.

## Neden bu tarama

Tracon zaten olgun bir güvenlik mimarisine sahiptir (bkz.
[`MIMARI-GUVENLIK.md`](../MIMARI-GUVENLIK.md), Faz 9/11/41/48/53/63-66/69).
Bu taramanın amacı sıfırdan zafiyet aramak değil, var olan korumalarda
**regresyon** ve **boşluk** bulmaktır. Kapanmış bir K-kararını yeniden
tartışmak bulgu değildir.

## Koşum kaydı

| Tarih | Taban | Sonuç |
|---|---|---|
| 2026-08-20 | `1cda224` | 10 konu koşuldu → [`BULGULAR.md`](BULGULAR.md) — 4 🔴, 14 🟡, 18 🟢 CONFIRMED |

**Bulgu defteri [`BULGULAR.md`](BULGULAR.md)'dir.** Bir konuyu yeniden koşmadan
önce oraya bak: doğrulanmış korumalar listelenmiştir ve tekrar taranmaları
gerekmez.

## Kullanım

1. Her konu dosyasını ayrı bir agent oturumuna verin.
2. Oturum salt-okunur denetim yapar, kod değiştirmez.
3. CONFIRMED bulgu:
   - Var olan bir korumanın kod hatasıysa → `kusur-giderme` skill'i ile
     ayrı bir oturumda kapatılır.
   - Var olmayan bir koruma (yeni sağlamlaştırma yeteneği) gerekiyorsa →
     `docs/ADAYLAR.md`'ye F-NN olarak eklenir, sonra `faz-planlama`.
4. PLAUSIBLE bulgu CONFIRMED'e çevrilene kadar aksiyon almaz, yalnız not
   düşülür.

## Öncelik sırası

| Öncelik | Konu | Gerekçe |
|---|---|---|
| 🔴 1 | [01-kimlik-dogrulama-ve-yetkilendirme.md](01-kimlik-dogrulama-ve-yetkilendirme.md) | Dış yüzeyin ilk kapısı |
| 🔴 2 | [02-kiraci-izolasyonu.md](02-kiraci-izolasyonu.md) | İhlali kiracılar arası veri sızıntısı |
| 🔴 3 | [03-denetim-izi-ve-secret-redaksiyonu.md](03-denetim-izi-ve-secret-redaksiyonu.md) | Sızıntı sınıfı geçmişte 5+ kez tekrarlandı |
| 🔴 4 | [04-script-sandbox.md](04-script-sandbox.md) | İhlali kod çalıştırma sınıfı risk taşır |
| 🟡 5 | [05-dis-ag-erisimi.md](05-dis-ag-erisimi.md) | SSRF/webhook, sunucu taraflı istismar |
| 🟡 6 | [06-veri-katmani.md](06-veri-katmani.md) | SQL enjeksiyonu, at-rest koruma |
| 🟢 7 | [07-bagimlilik-tedarik-zinciri.md](07-bagimlilik-tedarik-zinciri.md) | Bilinen CVE-pin'lerin geçerliliği |
| 🟢 8 | [08-aot-reflection.md](08-aot-reflection.md) | Dinamik tip yükleme riski |
| 🟢 9 | [09-frontend-guvenligi.md](09-frontend-guvenligi.md) | İstemci tarafı, düşük blast-radius |
| 🟢 10 | [10-loglama-gizlilik.md](10-loglama-gizlilik.md) | Sızıntı riski, doğrudan istismar değil |

## Ortak çerçeve (her konu oturumu bunu uygular)

Önce oku: `AGENTS.md` (kök), `docs/MIMARI-GUVENLIK.md`,
`docs/KARARLAR-INDEKS.md`.

Kurallar:

1. **Salt-okunur.** Kod değiştirme, yalnız bulgu üret.
2. **Regresyon çerçevesi.** `docs/KARARLAR.md`'de kapanmış bir kararı
   yeniden önerme. "K-XXX şunu söylüyor, kod bunu ihlal ediyor" biçiminde
   bildir.
3. **Kanıt etiketi.** Her bulguyu CONFIRMED (somut girdi/durum → somut
   sonuç zinciri kurulabiliyor) veya PLAUSIBLE (mantıken risk, doğrulanmadı)
   diye işaretle.
4. **Ciddiyet.** 🔴 Kritik (veri sızıntısı / kiracı ihlali / kod çalıştırma)
   → 🟡 Önemli (yetki atlatma / DoS) → 🟢 Öneri (sağlamlaştırma).
5. **Kanıt zorunluluğu.** Dosya:satır olmadan bulgu geçersizdir.
6. **Düzeltme yasağı.** Bulunan kusuru bu oturumda düzeltme; bulma ve
   giderme ayrı adımdır.

Çıktı formatı (her bulgu için): dosya:satır · özet (1 cümle) · somut
senaryo · etiket (CONFIRMED/PLAUSIBLE) · ciddiyet.

## Alternatif: tam otomatik tarama

Bu ortamda `claude-security` eklentisi kurulu — "fully scan this repository
and patch what you find" tarzı tek komutla tam otomatik tarama+yama akışı
sunar. Bu indeksin sunduğu konu-başına-bölünmüş, insan onaylı akış BİLEREK
farklıdır: her konu ayrı bir oturumda, ayrı bir agent ile, mimariye özel
bağlamla çalışır. İki yaklaşım karşılıklı dışlayıcı değildir.
