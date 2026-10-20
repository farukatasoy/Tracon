# Faz 224 — MAF Kapsama Ölçüm Aracı

> **Durum:** ✅ Tamamlandı (2026-10-20)
> **Kademe:** hafif — migration yok · public API'den satır silinmez · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-20 (sohbette: Dalga 0 sırası D-01/D-19 → P-04/P-05 → bu faz; temas ölçümü "üretilen tablo")
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-596** — MAF kapsama matrisi P-02 + P-03 + D-20 kalanı ([`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §9, §10)
> **Paketler:** yok (`scripts/`, `docs/`, `.agents/`)
> **Tüketici yüzeyi:** Yok — geliştirme aparatı; sevk edilen yapıt değişmez
> **Genellik:** Bakımcı aracıdır; tüketiciye dokunmaz. İkinci kanıt: matris 2026-10-18'de elle iki saat sürdü ve 2026-10-20 sayımı özetten sapmıştı (✅ 43 → 45, ❌ 85 → 77)
> **Karşı görüş:** "Matris yargıdır, betik onu üretemez." Cevap: betik yargı sütunlarına dokunmaz; yalnız ölçülen olguyu (jeton teması, sürüm farkı, `✗` sayısı) üretir ve kaymayı diff'te görünür yapar

## Amaç

MAF kapsama matrisinin ölçülen sütunları bugün elle sayılıyor ve kayıyor. Kanıt: 2026-10-20'de §5'in özet sayımı yanlıştı; `✗` sayısı 56'dan 54'e inmişti ve hangi satırın değiştiği hiçbir yerde kayıtlı değildi. Sürüm yükseltmede tip kapsamlı dump farkı da elle ayrıştırılıyor (`maf-api-kesfi` § *Sürüm yükseltirken*). Faz bitince:

1. **P-02:** `scripts/maf-kapsama.py temas`, §5'in her satırı için MAF jetonlarının `src/`, `tests/` ve `samples/` temasını `scripts/maf-kapsama-temas.tsv`'ye yazar. Dosya `kapi.py uret` ile üretilir ve `--denetle` tazeliğini denetler. `fark <eski> <yeni>` iki `dump-api.sh` çıktısını tip kapsamlı karşılaştırır.
2. **P-03:** `--denetle`, §5'teki `✗` sayısını tabanla karşılaştırır. Sayı artarsa da, taban bayat kalırsa da kırmızı olur. `✗⁺` sayılmaz: yeni MAF tipi onunla girer.
3. **D-20 kalanı:** `MAF-GENISLEME-NOKTALARI.md` § *Hâlâ kullanılmayan* bloğunun her satırı matristeki bir satıra eşlenir. Eşi olmayan hüküm matrise taşınır. Blok bir işaretçiye iner.

Durum sütunu (✅/🟡/❌) **elle kalır** (kullanıcı kararı, 2026-10-20). Ölçüm: doğrudan bir uyuşmazlık kapısı, 143 satırın 39'unda genel jeton (`AIAgent`, `ChatOptions`, `AsAIAgent`) ya da ad çakışması (`AsIChatClient`) yüzünden yanlış alarm verirdi.

## Tasarım notları

- §5 bölümü `## 5. Yetenek matrisi` ile `## 6.` arasıdır. Satır, ilk hücresi `✅ 🟡 ❌ ⛔`'den biri olan tablo satırıdır. Bölüm başlığı bulunamazsa ya da sıfır satır çıkarsa betik **hata verir**; sessizce boş tablo üretmez.
- Jeton: ikinci hücrenin (`Yetenek (MAF)`) ters tırnak aralıklarındaki, büyük harfle başlayan tanımlayıcılar. Kaynak dosyada yorum ve dize sabitleri ayıklanır; `obj/`, `bin/` ve üretilen dosyalar (`*.g.cs`, `Generated/`) atlanır.
- TSV sütunları: `bolum · durum · takip · yetenek · src · tests · samples`. Alanlar **sayı değil, temas eden jeton kümesi** taşır. Böylece dosya yalnız bir jeton bir alana girip çıkınca değişir.
- `fark`: dump satırı `>>> <TamAd>   base: …` tiptir, dört boşlukla başlayan satır üyedir. Çıktı: eklenen tip, kaldırılan tip, tip başına eklenen ve kaldırılan üye. Kaldırma varsa çıkış `1` olur (`maf-api-kesfi`: "kaldırma bir kusur işidir").
- Taban `scripts/maf-kapsama.py` içinde bir sabittir: plan anında **54**, kapanışta **53** (Plandan Sapmalar 1). Taban yalnız aşağı iner.
- `dokuman-bakim.py` betiği `importlib` ile yükler (`maf_paket_envanteri_test.py` emsali); CI iş akışı değişmez.

## Bitiş Ölçütleri (DoD)

- [x] `python3 scripts/maf-kapsama.py temas --yaz` §5'in 143 satırının her biri için bir TSV satırı yazar; iki koşum aynı baytı üretir
- [x] `kapi.py uret` TSV'yi yeniden üretir; elle bozulmuş TSV ile `--denetle` "Üretilen dosya tazeliği" altında kırmızıdır
- [x] `--denetle` "MAF kapsama ✗ cırcırı": `✗` tabanın bir üstünde ve bir altında kırmızı, tabanda temiz; `✗⁺` satırı eklemek sonucu değiştirmez (taban kapanışta 53)
- [x] `fark`: bir üyenin bir tipten ötekine taşınması iki tipte de görünür (düz `diff`'in kaçırdığı vaka); kaldırmada çıkış `1`
- [x] `maf-api-kesfi` § *Sürüm yükseltirken* elle ayrıştırma yerine `fark` komutunu anar
- [x] `MAF-GENISLEME-NOKTALARI.md` § *Hâlâ kullanılmayan* eşleme tablosu: her satır → matris satırı ya da taşındı; blok işaretçiye indi
- [x] `MAF-KAPSAMA.md` §10 P-02 ve P-03 ✅; §9 D-20 "P-02 betiği ve daraltma açık" notu kapandı
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban ca6b7001` — çıkış 0; 24.662 test, 38 modül (2026-10-20)

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| §5 satırı ayrıştırılır: durum, takip, jetonlar | Birim | `scripts/maf_kapsama_test.py` |
| Bölüm yok ya da sıfır satır → hata | Birim | aynı |
| Yorumdaki ve dizedeki ad temas sayılmaz | Birim | aynı |
| TSV deterministik; yalnız küme değişince değişir | Birim | aynı |
| Cırcır: artış, bayat taban, eşit; `✗⁺` sayılmaz | Birim | aynı |
| `fark`: eklenen tip, kaldırılan üye, tipler arası taşınan üye, çıkış kodu | Birim | aynı |
| `--denetle` iki yeni bulguyu gerçek repoda raporlar | Gerçek koşum | `python3 scripts/dokuman-bakim.py --denetle` |

Beş soru: iptal — yok · eşzamanlılık — yok · boş/aşırı girdi — bölüm yok, sıfır satır, boş dump dosyası açık hata verir · başka kiracı — yok · alt sistem hatası — okunamayan `.cs` dosyası atlanmaz, hata verir. Kabul case'i yok: geliştirme aracıdır ve davranışın tamamı birim testindedir.

## Açık Sorular

Yok. Temas ölçümünün biçimi kullanıcı kararıdır (2026-10-20: üretilen tablo, Durum elle).

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

1. **Taban 54 → 53.** D-20 daraltması `ImageGeneratingChatClient` satırının KORU gerekçesini GENISLEME'den matrise taşıdı; satırın `Takip`'i `✗` → `✓` oldu. Cırcır ilk koşumunda "taban bayat" dedi ve taban düşürüldü — kapının kendi ilk kanıtı.
2. **Kaynak ayıklama regex'ten tarayıcıya döndü** (denetim 🟡3). Regex dört tırnaklı ham dizeyi, enterpolasyon deliğindeki iç içe dizeyi ve `@$"` önekini yanlış ayrıştırıyordu (`PlaywrightLocatorTests.cs:204-271` gerçek vaka). Tarayıcı ile tablo **alan düzeyinde aynı** çıktı; üç yeni test eski regex'e karşı kırmızıdır (ölçüldü).
3. **`fark` tip başlığını da karşılaştırır** (denetim 🟡1): taban sınıf, `[abstract]` ya da `[sealed]` değişimi kırıcı sayılır ve çıkış `1` verir. `!!` satırı (yüklenemeyen derleme) hatadır (🟡2): eksik eski dump her kaldırmayı "eklenen tip" gibi gösterirdi.
4. **İki 🟢 bulgu aynı fazda kapandı:** ekli `✗` (ör. `✗ bayat`) cırcırda sayılır; jetonsuz satır TSV'de `-` (temas yok) değil `?` (ölçülmedi) yazar (8 satır).
5. **Ek ölçüm:** `fark` 1.24.0 → 1.25.0 çekirdek paketlerde sıfır fark, 1.20.0 → 1.25.0 sürüm damgası tablosundaki dört tipi ve sekiz üyeyi birebir verdi. Sürüm tablosundaki "MAF 1.25.0" etiketi bu yüzden "1.20 → 1.25 penceresi" anlamındadır.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 |
| Düzeltme turu sayısı | 1 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.

Bağımsız denetçi, 2026-10-20: 🔴 0 · 🟡 4 · 🟢 4.

| # | Bulgu | Sonuç |
|---|---|---|
| 🟡1 | `fark` tip başlığını (taban, `[abstract]`, `[sealed]`) atıyordu | düzeltildi — başlık değişimi kırıcı, çıkış 1; test |
| 🟡2 | `!! yuklenemedi` satırı sessizce yok sayılıyordu | düzeltildi — hata; test |
| 🟡3 | Ayıklama regex'i ham/enterpolasyonlu dizede iki yönde yanılıyordu | düzeltildi — tarayıcı; üç test eski regex'e karşı kırmızı (ölçüldü); tablo alan düzeyinde aynı |
| 🟡4 | Taban ve DoD sayıları, iki aparat metni bayattı | düzeltildi — Sapma 1, `dokuman-bakim.py` docstring'i, `kapilar.md` |
| 🟢1 | Tabanın "yalnız aşağı" kuralı mekanik değil | düzeltildi — `faz-denetim` §3.8 "yalnız iyileşir" listesine `CARPI_TABANI` |
| 🟢2 | Ekli `✗` sayılmıyordu | düzeltildi — `carpi_mi`; test |
| 🟢3 | Jetonsuz satır "temas yok" görünüyordu | düzeltildi — `?` (ölçülmedi); test |
| 🟢4 | Özet sayımı elle yazılıyor | F-597 |
