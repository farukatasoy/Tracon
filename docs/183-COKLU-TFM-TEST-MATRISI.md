# Faz 183 — Çoklu TFM Test Matrisi

> **Durum:** 📋 Planlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı; strateji seçimi: temsilci projeler multi-target)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-260**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` csproj'ları ve `ci.yml` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok (README'nin TFM iddiası zaten doğru; bu faz iddiaya kanıt ekler)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` — net8 tüketici smoke case'i eklenir

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-263\|K-268" docs/KARARLAR.md
   ```
   **K-263** (`TargetFrameworks` çoğul/tekil tuzağı — pack orkestrasyonu çoğul
   okur; test projelerinde tersi yönde aynı dikkat gerekir)
3. Alan hafızası: [`hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md) ·
   [`hafiza/test-kosum-tuzaklari.md`](hafiza/test-kosum-tuzaklari.md) (MTP
   koşum biçimi TFM başına değişir)
4. [`ci.yml`](../.github/workflows/ci.yml) test job'ı — matrix ve `--no-build`
   akışı

---

## Amaç

Kütüphaneler `net8.0;net9.0;net10.0` sevk ediyor; test ağacının tamamı
`net10.0` tekil. net8/net9 bacakları derleniyor ama hiçbir davranış kanıtı
yok — multi-targeting bugün test edilmemiş bir vaattir. Tam ağacı üç TFM'e
çıkarmak CI'ı ~3 katına şişirir; seçilen strateji **temsilci projeler**: en
geniş davranış yüzeyini taşıyan test projeleri üç TFM'de koşar, gerisi
net10'da kalır.

- **F-260** — temsilci test projelerini `net8.0;net9.0;net10.0`'a çıkar; CI'da
  ubuntu bacağında üç TFM koştur; bir packed net8 tüketici smoke'u ekle.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`src/Directory.Build.props:13`](../src/Directory.Build.props) | Kütüphaneler `net8.0;net9.0;net10.0` |
| [`tests/Directory.Build.props:6`](../tests/Directory.Build.props) | Test ağacı `net10.0` **tekil** — net8/net9 hiçbir testte koşmuyor |
| [`ci.yml:211`](../.github/workflows/ci.yml) | `dotnet test Tracon.slnx` — testler tek TFM ürettiği için matris yok |
| `samples/`, `bench/` | Tamamı net10 — sevk edilen net8/net9 bacaklarını hiçbir tüketici yolu çalıştırmıyor |

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 183.1 — Temsilci küme

Seçim ölçütü: sevk edilen paketlerin davranış yüzeyini en geniş kapatan ve
dış servis istemeyen projeler.

| Proje | Neden temsilci |
|---|---|
| `Tracon.Core.UnitTests` | En geniş yüzey (1.825 test); Core + Abstractions davranışı |
| `Tracon.Sql.Shared.UnitTests` | SQL metin üretimi — üç lehçenin paylaşılan gövdesi |
| `Tracon.OpenAI/.Anthropic/.Google/.Azure.UnitTests` | Dört provider adapter'ı; TFM'e duyarlı HTTP/serileştirme yolları |
| `Tracon.Sqlite.IntegrationTests` | Docker'sız gerçek depo yolu — store davranışı TFM başına kanıtlanır |

`AspNetCore.FunctionalTests` kapsam dışı (WebApplication host TFM'i net10
altyapısına bağlı; frontend/E2E zinciri ağır). `Testing.Contracts.Xunit.UnitTests`
küçüktür, pakete TFM başına zaten derleniyor — eklenmesi Açık Soru 1'dedir.

## 183.2 — Mekanizma

Seçilen csproj'lar `<TargetFrameworks>` (çoğul) alır; `tests/Directory.Build.props`
varsayılanı **değişmez** (K-263 tuzağının tersi: çoğulun maliyeti yalnız seçilen
projelere ödetilir). MTP ikilisi TFM başına ayrı çıktı üretir
(`artifacts/bin/<Proje>/release_net8.0/` düzeni ölçülür); `kapi.py test`'in
`--proje` çözümlemesi çoklu TFM çıktısını tanıyacak şekilde güncellenir.

## 183.3 — CI şekli

Üç TFM yalnız **ubuntu** bacağında koşar (Windows bacağı net10'da kalır —
platform farkı zaten o bacağın işi, TFM farkı değil). Süre ölçülür ve kapanışa
yazılır; kabul edilemezse temsilci küme daraltılır — küme genişletme/daraltma
kararı ölçümle verilir, planla değil.

## 183.4 — Packed net8 tüketici smoke'u

`samples/` düzenindeki packed-consumer yoluna bir **net8** tüketici eklenir:
`dotnet add package Tracon.Core` + en küçük çalışan kullanım. Kanıt sınıfı
"paket net8'de restore olur ve çalışır" — test ağacının kanıtından bağımsız,
tüketicinin gerçeğine en yakın kanıt.

---

## Planlanan Public API

Yok.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
tests/Tracon.Core.UnitTests/*.csproj            (TargetFrameworks)
tests/Tracon.Sql.Shared.UnitTests/*.csproj
tests/Tracon.{OpenAI,Anthropic,Google,Azure}.UnitTests/*.csproj
tests/Tracon.Sqlite.IntegrationTests/*.csproj
samples/Tracon.Samples.Net8Consumer/            (packed smoke)
scripts/kapi.py                                 (çoklu TFM çıktı çözümleme)
.github/workflows/ci.yml                        (ubuntu bacağı TFM koşumu)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| net8'de olmayan BCL üyesi kullanılmış (bugüne kadar görünmedi çünkü test yoktu) | Derleme + temsilci koşum | üç TFM'de `dotnet test` |
| TFM'e bağlı davranış farkı (serileştirme, `TimeProvider`, HTTP) | Temsilci koşum | mevcut testler üç TFM'de |
| `kapi.py test` yanlış TFM ikilisini koşturur | Script birimi | `kapi_test.py` — çıktı yolu çözümleme vakaları |
| Packed net8 tüketicisi restore edemiyor (bağımlılık grubu hatası) | Paket | `Net8Consumer` sample'ı `kapi.py yayin` yoluna eklenir |
| CI süresi kabul edilemez büyür | Ölçüm | süre kapanışa yazılır; eşik kararı ölçüm sonrası |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Paketler yerel feed'de | net8 tüketici sample'ını derle ve koştur | Restore + build + çalışma; çıktı net8 runtime'da |
| 2 | — | `dotnet test tests/Tracon.Core.UnitTests -f net8.0` | Paket net8'de yeşil |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `Testing.Contracts.Xunit.UnitTests` temsilci kümeye girer mi? | A: evet (13 test, ucuz) · B: hayır | **A** — sevk edilen sözleşme paketinin TFM kanıtı neredeyse bedava |
| 2 | net9 da mı, yalnız net8 mi? | A: üçü de · B: net8 + net10 (uçlar) | **A** ile başla; süre ölçümü B'yi gerektirirse kapanışta karar yazılır |

---

## Bitiş Ölçütleri (DoD)

- [ ] Temsilci projeler üç TFM'de derleniyor ve **CI ubuntu bacağında** koşuyor
- [ ] Packed net8 tüketici smoke'u yayın yolunda (`kapi.py yayin`) koşuyor
- [ ] Üç TFM koşumunun süresi ölçüldü ve kapanışa yazıldı
- [ ] `kapi.py test --proje <temsilci> --tfm net8.0` biçiminde tek-TFM koşum mümkün
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `01-KURULUM-VE-PAKETLEME.md`'ye eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
dotnet test tests/Tracon.Core.UnitTests -c Release -f net8.0 -- --report-trx
python3 scripts/kapi.py yayin --kuru
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| CI süresi büyür | Yalnız ubuntu bacağı + temsilci küme; süre ölçülüp yazılır, küme ölçümle daraltılabilir |
| MTP'nin çoklu TFM çıktı düzeni beklenenden farklı | İlk iş tek projede ölçmek (`faz-uygulama` yapısal iddia kuralı); `kapi.py` değişikliği ölçümden sonra |
| Temsilci küme yanlış güven verir ("hepsi test edildi" sanılır) | README/durum metni "temsilci küme" ifadesini açık yazar; tam matris GA turunun kararıdır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
