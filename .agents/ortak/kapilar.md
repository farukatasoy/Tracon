# Kapı Koşumu — Ortak Sözleşme

> `.agents/skills/` **dışındadır** — skill keşfi bu dosyayı bir skill sanmaz.
> Bağlayan dosyalar: `AGENTS.md` · `faz-tamamlama/SKILL.md` ·
> `kusur-giderme/SKILL.md`. Ham komutlar yalnız burada ve `scripts/kapi.py`
> içinde yaşar; üç çağıran da bu dosyaya bağlanır, komutu kopyalamaz (Faz 91).

Dördü de sıfır uyarı vermelidir. Bir tanesi kırmızıysa iş **bitmemiştir**.

```bash
python3 scripts/kapi.py ic-dongu                 # build + etkilenen test projeleri (hızlı iç döngü)
python3 scripts/kapi.py tarama                    # yalnız sync kopyası + secret (saniyeler)
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>   # tamamı, ucuzdan pahalıya, tek özet
python3 scripts/kapi.py test --proje <Proje> --sinif "*Ad*"   # MTP filtresi, doğru biçimde
```

`kapi.py` koştuğu her komutu ekrana basar, ilk kırmızıda durur ve süreleri
`artifacts/kapi-olcum.jsonl`'a ekler. `--komutlari-bas` hiçbirini koşmadan
listeler — ayıklama bilgisi kaybolmaz.

## Neden dördü de zorunlu

`dotnet format`, `dotnet build`'in yakalamadığı analyzer tanılarını yakalar.
`dotnet pack` yalnız derlenen değil, **paketlenen** yüzeyi doğrular
(`README.md`, `PublicAPI.*.txt`). Sıcak build ölçüldü: ~5 sn — **erken ve sık
çalıştır**, faz sonuna biriktirme.

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır. Bir analyzer
kuralını bastırmadan önce **neden** tetiklendiğini anla; bastırma gerekiyorsa
gerekçesini koda ve `docs/KARARLAR.md`'ye yaz.

## Hızlı iç döngü

Arayüze dokunmuyorsan `-p:AgentPrismFrontendEnabled=false` npm/Vite/Vitest
adımlarını atlar (`kapi.py ic-dongu` bunu değişen dosyalara bakarak otomatik
seçer). Dördünün tamamı **faz kapanışında** ve arayüz/paket değişiminde
çalışır. E2E tuzağı: `faz-uygulama` Adım 5 — arayüze dokunuyorsan bu bayrağı
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

## MTP filtresi — `dotnet test --filter` YAZMA

MTP'de `--filter` diye bir seçenek **yoktur**; sessizce yutulur ve paketin
**tamamı** koşup yeşil döner — daralttığını sanırsın. `kapi.py test` derlenmiş
ikiliyi `--filter-class` ile çağırır; yanlış biçim yazılamaz çünkü ham komut
elinde değildir.
