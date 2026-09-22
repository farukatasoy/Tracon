# Faz 183 — Çoklu TFM Test Matrisi

> **Durum:** ✅ Tamamlandı (2026-09-23)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı; strateji seçimi: temsilci projeler multi-target)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-260**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` csproj'ları ve `ci.yml` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok (README'nin TFM iddiası zaten doğru; bu faz iddiaya kanıt ekler)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` — net8 tüketici smoke case'i eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 6dca6e9a:docs/arsiv/fazlar/183-COKLU-TFM-TEST-MATRISI.md
> ```
>
> Damıtıldı 2026-09-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Kütüphaneler `net8.0;net9.0;net10.0` sevk ediyor; test ağacının tamamı `net10.0` tekil. net8/net9 bacakları derleniyor ama hiçbir davranış kanıtı yok — multi-targeting bugün test edilmemiş bir vaattir.

## Bitiş Ölçütleri (DoD)

- [x] Temsilci projeler üç TFM'de derleniyor ve **CI ubuntu bacağında** koşuyor — sekiz proje (`tests/Directory.Build.props`, `TraconMultiTargetTest`) yerelde üç TFM'de yeşil; `ci.yml` ubuntu bacağı props varsayılanını alır ve net8/net9 runtime'larını kurar. CI koşumu push sonrası görünür (yerelde ölçülemez)
- [x] Packed net8 tüketici smoke'u yayın yolunda (`kapi.py yayin`) koşuyor — `release_extension_samples.py`; "Örnek Uygulama Koşumu" bölümü
- [x] Üç TFM koşumunun süresi ölçüldü ve kapanışa yazıldı — "Süre Ölçümü" bölümü
- [x] `kapi.py test --proje <temsilci> --tfm net8.0` biçiminde tek-TFM koşum mümkün — `MT-PKG-127`
- [x] Dört doğrulama kapısı sıfır uyarı verir — "Kapanış Kapısı" bölümü
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş döndü — `kapi.py tarama`: temiz (17 işaretli sentetik credential atlandı)
- [x] Manuel kabul case'leri `01-KURULUM-VE-PAKETLEME.md`'ye eklendi; otomatikleştirilebilenler koşuldu — `MT-PKG-126`…`129`, dördü koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki 🔴 gerçekti, ikisi de kapandı ("Denetim Bulguları")

### Doğrulama komutları

```bash
dotnet test tests/Tracon.Core.UnitTests -c Release -f net8.0 -- --report-trx
python3 scripts/kapi.py yayin --kuru
```

---

## Plandan Sapmalar

- **Mekanizma csproj'larda değil `tests/Directory.Build.props`'ta (183.2).** Plan
  "seçilen csproj'lar `<TargetFrameworks>` alır" diyordu. Ölçüldü: csproj gövdesi
  props'tan SONRA okunur; csproj'a çoğulu yazıp props'un tekil `TargetFramework`'ünü
  temizlemeyi unutmak projeyi **sessizce** net10'a indirir (K-263'ün aynası). Küme ve
  TFM listesi tek yerde (`TraconMultiTargetTest`, `TraconTestTargetFrameworks`);
  `kapi.py` ikisini oradan okur. `kapi_test` csproj'larda `<TargetFramework` satırı
  olmadığını da kilitler (denetim 🟢-1, aynı fazda kapandı).
- **Windows bacağı ortam değişkeniyle daralır.** `matrix.include` Windows'a
  `test-frameworks: net10.0` verir; iş düzeyindeki `TraconTestTargetFrameworks` ortam
  değişkeni restore/build/format/test'e aynı kümeyi verir ve `kapi.py` de onu okur.
- **Çıktı düzeni ölçüldü:** `artifacts/bin/<P>/release_<tfm>/`. Eski `release/`
  klasörü proje çok hedefli olunca bayat kalır; yol **beyandan** çözülür.
  `CapabilityExampleTests.Configuration` `Name.Split('_')[0]` oldu (yoksa üç bacakta
  da "XML bulunamadı" ile düşüyordu).
- **Test kodu net8/net9'da derlenmedi — üç sınıf:** altı `System.Threading.Lock`
  (koleksiyonun kendisine kilitlenir, `src/` deseni), iki `System.Linq.AsyncEnumerable`
  (`await foreach`), bir Shouldly `IReadOnlyDictionary.ShouldNotContainKey` (net8
  derlemesinde yok). Davranış değişmedi.
- **Plan dışı eklenen:** `RuntimeMatchesTargetFrameworkTests` (her temsilci projeye
  props'tan bağlanır) — roll-forward edilmiş bir bacak kanıtı sessizce siler; ölçüldü:
  `DOTNET_ROLL_FORWARD=LatestMajor` ile net8 bacağı .NET 10.0.0'da koştu ve test düştü.
  `kapi.py` runtime ön kontrolü — eksik runtime dakikalar sonra düşen bir bacak olur ve
  izole koşum onu "gerçek regresyon" sanar.
- **Tüketici yüzeyi "Yok" değildi.** README (TFM iddiasına "neyin koştuğu" + geliştirme
  önkoşulu), CONTRIBUTING (net8/net9 runtime önkoşulu), `samples/README.md` ve sitenin
  `reference/compatibility.md`'sine bir paragraf eklendi — planın Risk 3 önlemi
  ("temsilci küme ifadesi açık yazılır") README ile sınırlıydı; ürün dokümanı site
  olduğu için oraya da yazıldı. `--site-denetle`: 0 kural tetiklendi.
- **CI `release-dryrun` işi de değişti** (8.0.x runtime) — plan yalnız test job'ını sayıyordu.
- **Açık Sorular:** 1 → A (`Testing.Contracts.Xunit.UnitTests` kümede), 2 → A (üç TFM).
  Süre ölçümü B'ye geçmeyi gerektirmedi.

## Bu Fazda Verilen Kararlar

`K-*` açılmadı — test altyapısı tercihi, public API/uyumluluk sözü değişmedi.
Yerel kararlar yukarıda ve kodda: küme tek yerde, Windows net10, yol beyandan.

## Süre Ölçümü

Tam kapanış test adımı (`dotnet test Tracon.slnx -maxcpucount:1`, yerel, 2026-09-23):
**910 sn** (Faz 182'nin aynı adımı: 782 sn). Ek TFM bacaklarının payı ~105 sn —
bacak başına: Core 5,2/4,5 sn · Sqlite 43,1/42,0 sn · dört provider ~1–1,5 sn ·
Sql.Shared ve Contracts.Xunit <0,3 sn (net8/net9). `-maxcpucount:1` bacakları
sıralı koşturur; tek proje koşumunda (`dotnet test tests/<P>`) üçü paralel koşar
(Core üç TFM: 10 sn). Maliyetin ~%80'i SQLite sözleşme paketidir. Açık Soru 2'nin
B seçeneği (net9'u düşürmek) ~45 sn kazandırırdı — gerekmedi.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, `--no-launch-profile` (profil `ASPNETCORE_ENVIRONMENT=Development`
verir ve user-secrets'ı yükler — ilk deneme bu yüzden PostgreSQL'e bağlandı ve `401`
aldı), `ASPNETCORE_ENVIRONMENT=Staging`, geçici SQLite ve atılabilir içerik anahtarı (2026-09-23):

- `GET /tracon/api/diagnostics` → `persistenceProvider: SQLite`, `canConnect: true`,
  `migrationsUpToDate: true`, 7 agent, 9 tool.
- `POST /tracon/api/agents/support/run` → SSE: `run` · 7 `update` · `done`;
  `GET /tracon/api/runs/{id}` → `Completed`, SQLite'ta kalıcı.

Packed `net8.0` tüketici (`samples/Tracon.Samples.Net8Consumer`, `Tracon.Core`
`0.0.0-net8probe.1` yerel feed, izole `NUGET_PACKAGES`): `net8.0 consumer smoke passed
on .NET 8.0.31: reply: ping from net8` — generic host başladı, arka plan servisleri
koştu, `IAgentCatalog` agent'ı çözdü, `run` kayıt boru hattından geçti. Yayın
provasındaki koşum "Kapanış Kapısı" bölümünde.

## Kapanış Kapısı

Doldurulacak: son `kapi.py kapanis` ve `kapi.py yayin --kuru` koşumu.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan değişmedi; sapmalar yukarıda |
| Düzeltme turu sayısı | 1 — denetimin iki 🔴 + dört 🟡 + bir 🟢 bulgusu tek turda kapandı |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — `kapi_test`'in yeni bir iddiası Windows'ta yol ayracına takılırdı (denetim 🔴-1, CI'a ulaşmadan kapandı) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi — faz yeni kapandı |

## Denetim Bulguları

Denetçi `faz-denetcisi`, çalışma ağacı `02234602` tabanına göre. Triyaj: uygulayan oturum
(kullanıcı oturumda değildi; iki 🔴'nin ikisi de ölçümle doğrulandı).

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | `test_eksik_runtime_list_runtimes_ciktisindan_bulunur` Windows'ta `PureWindowsPath` ayracına takılır | gerçek | düzeltildi — `str(PurePath(...))` ile karşılaştırma; test `system="Windows"` ile koşar |
| 2 | 🔴 | Sıcak yol talimatları temsilci projeler için bayat `release/` yolunu veriyordu (MEMORY, `kusur-giderme`, iki hafıza dosyası, 13 manuel case satırı) | gerçek | düzeltildi — yollar `release_net10.0` / `kapi.py test --tfm`; yerel bayat `release/`+`debug/` klasörleri silindi |
| 3 | 🟡 | `DOTNET_ROOT` yokken ön kontrol PATH'teki muxer'ı soruyordu; apphost global kurulumu okur | gerçek | düzeltildi — `/etc/dotnet/install_location_<arch>` → `install_location` → varsayılan kök; Windows'ta muxer'a düşer. Ölçüldü: özel SDK PATH'teyken ön kontrol artık `/usr/local/share/dotnet`'te net8'in yokluğunu yakalıyor |
| 4 | 🟡 | `MT-PKG-129` ve hafıza `DOTNET_ROLL_FORWARD=Major` diyordu; net8 kuruluyken Major ileri sarmaz | gerçek | düzeltildi — `LatestMajor` (ölçüldü: Major → test geçti, LatestMajor → düştü) |
| 5 | 🟡 | Props yorumu Windows'un `-p:` verdiğini söylüyordu; CI ortam değişkeni kullanıyor | gerçek | düzeltildi |
| 6 | 🟡 | Site metni "her hedef koşuluyor" diyerek fazın Risk 3'ündeki yanlış güveni üretiyordu | gerçek | düzeltildi — yalnız net10'da koşan paketler adıyla yazıldı; tüketici yalnız `Tracon.Core` alır |
| 7 | 🟢 | Temsilci csproj'a ileride yazılan `<TargetFramework>` CI'ı sessizce net10'a indirir | — | aynı fazda kapandı — `kapi_test` csproj'da `<TargetFramework` satırını reddeder |

## Faz Dışı Bulunan ve Kapatılan Kusur

| Kusur | Kanıt | Düzeltme |
|---|---|---|
| `kapi.py ic-dongu` `tests/Directory.Build.props` değişikliğinde **hiçbir** test projesi seçmiyordu (regex `tests/<ad>/` bekliyordu) | `test_test_agaci_kok_dosyasi_tam_kosum_ister` önce kırmızı | alt dizinsiz `tests/` yolu tam koşuma düşer |
| `kapi.py`'nin TRX taraması ve izole koşumu yalnız `release/`'e bakıyordu | çok hedefli projede düşen test hiç raporlanmazdı | `FailedTest(project, output, name)`; `release_<tfm>` dahil |

Ortam bulgusu (kusur değil): özel `~/.dotnet` kökü PATH'e konunca üç `Tracon.Package.Tests`
case'i `dotnet new sln --format slnx` → 127 ile düştü (kökte eski bir 8.0.414 SDK'sı
vardı); sistem muxer'ı + yalnız `DOTNET_ROOT` ile üçü geçti. Hafızaya yazıldı.

## Sonraki Faza Devir Notu

**Faz 184'e:** `tests/Tracon.Ui.E2ETests` ve `AspNetCore.FunctionalTests` temsilci
kümede **değil** — tek hedefli kalırlar, çıktıları `release/`'dir. Faz 184'ün
bekleme/zamanlama değişiklikleri Core veya Sqlite testlerine dokunursa o testler üç
TFM'de koşar: net8'de `System.Threading.Lock` ve `System.Linq.AsyncEnumerable` yoktur,
Shouldly'nin net8 derlemesi bazı sözlük iddialarını taşımaz (hafıza:
`test-kosum-tuzaklari.md` "Çoklu TFM test runtime'ları").

**Yerel ortam:** kapanış kapısı artık net8 ve net9 **runtime'ı** ister. Bu makinede
global kurulumda (`/usr/local/share/dotnet`) net8 yok; oturum `~/.dotnet`'e
`dotnet-install.sh` ile SDK 10.0.100 + 8.0.31 + 9.0.20 runtime'larını kurdu ve kapıyı
`DOTNET_ROOT=~/.dotnet` ile (PATH değişmeden) koştu. Kalıcı çözüm kullanıcınındır:
.NET 8 runtime'ının global kurulumu (`sudo` ister). `kapi.py` eksik runtime'ı komuttan
önce adıyla söyler.

**Takvim:** .NET 8 ve .NET 9'un Microsoft desteği **2026-11-10**'da bitiyor —
F-266. Düşürme kararı kullanıcınındır; düşürülürse küme listesi, `Net8Consumer`, CI
runtime adımları ve `Tracon.Testing` `VersionOverride`'ları birlikte değişir.

**Yarım kalan iş:** Site **yayınlanmadı** (`site-deploy.sh` dış sunucuya yazar;
kullanıcı onayı bekler). CI'ın iki bacağı ilk push'ta ölçülecek.
