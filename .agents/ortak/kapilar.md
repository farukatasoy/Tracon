# Kapı Koşumu — Ortak Sözleşme

> `.agents/skills/` **dışındadır** — skill keşfi bu dosyayı bir skill sanmaz.
> Bağlayan dosyalar: `AGENTS.md` · `faz-tamamlama/SKILL.md` ·
> `kusur-giderme/SKILL.md` · [`kurtarma.md`](kurtarma.md) (`KR-12`, Faz 168).
> Ham komutlar yalnız burada ve `scripts/kapi.py` içinde yaşar; dört çağıran
> da bu dosyaya bağlanır, komutu kopyalamaz (Faz 91).

Dördü de sıfır uyarı vermelidir. Bir tanesi kırmızıysa iş **bitmemiştir**.

```bash
python3 scripts/kapi.py ic-dongu                 # build + etkilenen test projeleri (hızlı iç döngü)
python3 scripts/kapi.py tarama                    # sync kopyası · secret · migration bütünlüğü · bayat doküman referansı (saniyeler)
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>   # tamamı, ucuzdan pahalıya, tek özet
python3 scripts/kapi.py test --proje <Proje> --sinif "*Ad*" [--tfm net8.0]   # MTP filtresi; çoklu TFM projesinde her bacak ya da tek bacak (Faz 183)
python3 scripts/kapi.py performans                # tahsis kapısı - üç sıcak yol, bench/baseline.json'a karşı (`KR-12`)
```

`kapi.py` koştuğu her komutu ekrana basar, ilk kırmızıda durur ve süreleri
`artifacts/kapi-olcum.jsonl`'a ekler. `--komutlari-bas` hiçbirini koşmadan
listeler — ayıklama bilgisi kaybolmaz.

## Kapı OLMAYAN komut: `kapasite`

`python3 scripts/kapi.py kapasite --profil <ad> --surum <exact>` (Faz 166) bir
**ölçümdür**, kapı değil. K-738 yük ölçümünün rapor olduğunu ve sürenin hiçbir
eşiğe bağlanmadığını söyler; `kapanis` bu komutu **çağırmaz** ve hiçbir profil
standart kapanışa, PR yoluna veya release hattına girmez. CI'da yalnız `smoke`
profili koşar ve hızı değil doğruluğu kontrol eder. Ayrıntı:
[`bench/capacity/README.md`](../../bench/capacity/README.md).

## Neden dördü de zorunlu

`dotnet format`, `dotnet build`'in yakalamadığı analyzer tanılarını yakalar.
`dotnet pack` yalnız derlenen değil, **paketlenen** yüzeyi doğrular
(`README.md`, `PublicAPI.*.txt`). Sıcak build ölçüldü: ~5 sn — **erken ve sık
çalıştır**, faz sonuna biriktirme.

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır. Bir analyzer
kuralını bastırmadan önce **neden** tetiklendiğini anla; bastırma gerekiyorsa
gerekçesini koda ve `docs/KARARLAR.md`'ye yaz.

## Hızlı iç döngü

Arayüze dokunmuyorsan `-p:TraconFrontendEnabled=false` npm/Vite/Vitest
adımlarını atlar (`kapi.py ic-dongu` bunu değişen dosyalara bakarak otomatik
seçer). Dördünün tamamı **faz kapanışında** ve arayüz/paket değişiminde
çalışır.

`ic-dongu`'nun kapsamadığı tek yer: `samples/Tracon.Samples.*` **hiçbir
çözüm dosyasında değildir**, onları yalnız `kapi.py yayin` paketlenmiş sürüme
karşı koşar. `ic-dongu` böyle bir yola dokunduğunda bunu **yazar**; sessizce
geçmez (2026-09-04 süreç denetimi, F-183). E2E tuzağı: `faz-uygulama` Adım 5 — arayüze dokunuyorsan bu bayrağı
**kullanma**, E2E testleri gömülü varlıkları arar ve koşum asılı kalır.

`dotnet build` **arayüzü de derler**: `npm ci` → `tsc --noEmit` → Vitest →
Vite → Brotli → bundle bütçesi (250 KB gzip). Node.js 20.19+ gerekir.

## `secret`

`secret` asla dosyaya veya veritabanına yazılmaz (K-059). Bağlantı dizesi ve
API anahtarı yalnız `dotnet user-secrets` içinde yaşar; `appsettings.json` boş
placeholder taşır. `kapi.py tarama` bunu tarar; desen ön ekten sonra en az 24
karakter arar ve `docs/manuel-test/`, `docs/arsiv/` ile
`manuel-test-kosumu` skill kaynaklarını **bilerek** hariç tutar (bunlarda
yerel test varsayılanı ve sahte anahtar değerleri vardır). Testlerde sahte
`secret` literali kullanırken tarama desenine uymayan bir değer seç.

## Ortam

`MSBUILDDISABLENODEREUSE=1` — `kapi.py` bunu **her komuta** kendisi ekler;
elle koşuyorsan da eklemen gerekir (öksüz MSBuild düğümleri asılı kalmaya
yol açar). Ayrıntı: `docs/hafiza/test-altyapisi.md`.

## CI'ın Windows ayağı

Kapılar iki işletim sisteminde koşar ve ubuntu ayağı **platform varsayımlarını
gizler**. Bir kapı yalnız `windows-latest`'te kırmızıysa önce şu altısına bak;
altısı da 2026-08-28'de arka arkaya çıktı, hiçbiri ubuntu'da görünmüyordu.

- **Python çıktısı.** `scripts/*.py` Türkçe yazar; Windows'ta hem `sys.stdout`
  hem `subprocess(text=True)` varsayılan olarak **cp1252**'dir ve `ş`/`ğ`/`İ` o
  kod sayfasında yoktur — ilk `print` `UnicodeEncodeError` fırlatır. `ci.yml` iş
  düzeyinde `PYTHONUTF8: 1` taşır (UTF-8 modu ikisini birden çevirir ve `unittest`
  koşumunu da kapsar; betik içi bir düzeltme o adımı kapsayamazdı). Yeni bir
  workflow yazarsan değişkeni ekle. Yerelde: `LC_ALL=C python3 -X utf8=0 ...`
  patlamalı, `LC_ALL=C PYTHONUTF8=1 python3 ...` geçmelidir.
- **Yol ayracı iddiası.** `pathlib` Windows'ta `\` üretir; testte
  `endswith("/a/b")` orada **her zaman** False döner. Ayraç yerine parça
  karşılaştır: `PurePath(yol).parts[-3:]`.
- **Satır sonu.** `System.Text.Json`'ın girintili yazıcısı satır sonunu
  `Environment.NewLine`'dan alır; Windows'ta CRLF üretir ve LF olarak commit
  edilmiş bir snapshot ile **her satırı** farklı çıkar. Diske karşı
  karşılaştırılan metni `ReplaceLineEndings("\n")` ile sabitle.
- **Regex `$` ve CRLF.** Çok satırlı modda .NET `$`'ı `\n`'den ÖNCE
  demirler; alt sürecin CRLF çıktısında son karakterle demir arasında `\r`
  kalır ve ekranda apaçık duran satır eşleşmez. Süreç çıktısına karşı yazılan
  desende `\r?$` kullan (ölçüldü: yalın `$` CRLF'te `False`, LF'te `True`).
- **SQLite dosyası silinemez.** `Microsoft.Data.Sqlite` bağlantıyı havuzlar;
  host `dispose` olsa da dosya tanıtıcısı açık kalır. POSIX açık dosyayı siler,
  Windows "being used by another process" der. Silmeden önce
  `SqliteConnection.ClearAllPools()` çağır — testlerde bunu
  `TempSqliteDatabase` yapar, elle kopyalama.
- **`npx` bir `.cmd`'dir.** Node'un `execFileSync`'i onu çözemez (`ENOENT`) ve
  shell'siz spawn edemez. Kurulu bir CLI'yi `npx` ile değil, `package.json`'ın
  `bin` alanından çözüp `process.execPath` ile çağır (örnek:
  `packages/tracon-client/scripts/generate.mjs`). `dotnet` gibi gerçek bir
  `.exe` sorun değildir.

## MTP filtresi — `dotnet test --filter` YAZMA

MTP'de `--filter` diye bir seçenek **yoktur**; sessizce yutulur ve paketin
**tamamı** koşup yeşil döner — daralttığını sanırsın. `kapi.py test` derlenmiş
ikiliyi `--filter-class` ile çağırır; yanlış biçim yazılamaz çünkü ham komut
elinde değildir.
