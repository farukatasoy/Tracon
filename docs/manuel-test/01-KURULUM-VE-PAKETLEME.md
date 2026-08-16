# 01 — Kurulum ve Paketleme (`PKG`)

> **Alan kodu:** `PKG` · **Faz:** 0, 52
> **Kaynak:** `global.json` · `NuGet.config` · `Directory.Build.props` ·
> `Directory.Build.targets` · `src/Directory.Build.props` · `src/*/*.csproj` ·
> `src/AgentPrism.Generators/`
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/01-KURULUM-VE-PAKETLEME.md`](kosumlar/2026-08-13/01-KURULUM-VE-PAKETLEME.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

AgentPrism bir NuGet paket ailesidir. Bu dosya **paketin kendisini** test eder:
derleme kapıları, üretilen `.nupkg` içeriği, bağımlılık grafiği, derleme anı
tanıları, AOT vaadi ve temiz bir tüketicinin ilk beş dakikası.

Bu dosya **kapıdır**. Burada bir case kalırsa sonraki hiçbir dosya anlamlı sonuç
vermez — paket yanlışsa üstündeki her şey yanlış zeminde koşar.

```mermaid
flowchart LR
    A["Ortam"] --> B["Dort kapi"]
    B --> C["nupkg icerigi"]
    C --> D["Bagimlilik grafigi"]
    D --> E["Kaynak ureteci"]
    E --> F["AOT"]
    F --> G["Izlek A: temiz tuketici"]
```

## Koşmadan önce

1. Repo temiz olmalıdır: `git status` çıktısı `docs/manuel-test/` dışında boş.
2. Bu dosya **veritabanı istemez**. PostgreSQL/SQL Server container'ları kapalı olabilir.
3. Çalışma dizinleri:
   - Repo: `/Users/farukatasoy/Desktop/projects/AgentPrism`
   - Temiz tüketici (İzlek A): `~/agentprism-manuel/` — repo **dışında**
   - Yerel feed: `~/agentprism-local-feed/`
4. Bu dosyadaki hiçbir case repo kaynağını kalıcı değiştirmez. Geçici dosya ekleyen
   case'ler son adımda onu siler.

> 🚨 **Her `dotnet pack` komutunun başına `MSBUILDDISABLENODEREUSE=1` yazılır.**
> Atlanırsa öksüz MSBuild düğümleri boruyu açık tutar ve komut dakikalarca asılı
> kalır (repo hafızasında ölçüldü: 8 dk+ → 18,5 sn).

---

# 1 — Ön koşullar

### MT-PKG-001 — SDK sürümü ve roll-forward politikası

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

**Ön koşul**
- Repo kökünde bulunulur.

**Adımlar**
1. Yüklü SDK'ları listele.
2. Repo kökünde çözülen SDK sürümünü oku.
3. `global.json` içeriğini oku.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
dotnet --list-sdks
dotnet --version
cat global.json
```

**Beklenen sonuç**
- `dotnet --version` çıktısı `10.0.1xx` bandındadır.
- `global.json` şu üç değeri taşır: `version: 10.0.100`, `rollForward: latestFeature`,
  `allowPrerelease: false`.
- `dotnet --version` bir önizleme (`-preview`, `-rc`) sürümü **döndürmez** —
  `allowPrerelease: false` bunu engeller.

---

### MT-PKG-002 — Node.js ve npm arayüz derlemesi için yeterli

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0, Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- Yok.

**Adımlar**
1. Node sürümünü oku.
2. npm sürümünü oku.

**Girilecek veri**
```bash
node --version
npm --version
```

**Beklenen sonuç**
- Node sürümü **20.19** veya üstüdür.
- `npm --version` sıfır çıkış kodu verir.

---

### MT-PKG-003 — Docker hazır ve `mssql/server` imajı arm64'te çekilebiliyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | — |

Bu case yalnız **ortamı** doğrular. SQL Server davranışı
[`04-KALICILIK-DIGER.md`](04-KALICILIK-DIGER.md)'dedir.

**Ön koşul**
- Docker Desktop çalışır durumda.

**Adımlar**
1. Docker durumunu oku.
2. Makine mimarisini oku.
3. `mssql/server` imajını çek.

**Girilecek veri**
```bash
docker info --format '{{.ServerVersion}} · {{.NCPU}} CPU · {{.MemTotal}} bayt'
uname -m
docker pull mcr.microsoft.com/mssql/server:2022-latest
```

**Beklenen sonuç**
- `docker info` bellek değeri **8 GB veya üstünü** gösterir.
- `uname -m` çıktısı `arm64`'tür.
- `docker pull` başarılı biter. Başarısız olursa Docker Desktop →
  Settings → General → *Rosetta for x86/amd64 emulation* açılır ve tekrarlanır.
- Rosetta açılamıyorsa bu case `Atlandı` işaretlenir ve
  [`00-INDEKS.md`](00-INDEKS.md) §2.1'deki sapma notu uygulanır.

### MT-PKG-010 — Dört kapı sıfır uyarı verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

**Ön koşul**
- Repo temiz (`git status` yalnız `docs/manuel-test/` gösterir).
- Docker çalışıyor (entegrasyon testleri container ister).

**Adımlar**
1. Çözümü derle.
2. Testleri koş.
3. Paketle.
4. Biçim denetimini koş.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
MSBUILDDISABLENODEREUSE=1 dotnet pack AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

**Beklenen sonuç**
- Dört komut da çıkış kodu `0` döndürür.
- `dotnet build` çıktısında `Warning(s)` sayısı **0**'dır.
- `dotnet test` çıktısında `failed` sayısı **0**'dır.
- `dotnet format` hiçbir dosya değişikliği bildirmez.
- Her komutun süresi not edilir. `dotnet test` **2,5 dakikayı** aşarsa bu bir
  kusurdur: asılı kalan alt süreç aranır (`ps aux | grep MSBuild`).

---

### MT-PKG-011 — `dotnet format` build'in görmediğini yakalar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

Sınır senaryosu. `dotnet build` yeşilken `dotnet format` kırmızı olabilir; bu
repo'da bir kez 276 `IDE0055` hatası bu şekilde çıktı.

**Ön koşul**
- MT-PKG-010 geçti.

**Adımlar**
1. Bir kaynak dosyasına bilerek bozuk girinti ekle.
2. Yalnız derle.
3. Biçim denetimini koş.
4. Değişikliği geri al.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
printf '\ninternal static class BicimTesti\n{\n        public static int Deger => 1;\n}\n' \
  >> src/AgentPrism.Abstractions/Tools/AgentPrismToolAttribute.cs

dotnet build  AgentPrism.slnx -c Release
dotnet format AgentPrism.slnx --verify-no-changes --no-restore ; echo "format cikis kodu: $?"

git checkout -- src/AgentPrism.Abstractions/Tools/AgentPrismToolAttribute.cs
```

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** `Directory.Build.props:28`
> `EnforceCodeStyleInBuild=true` taşır (`AnalysisLevel=latest-recommended`
> ile birlikte) — bu, `dotnet build`'in KENDİSİNİN `IDE0055` gibi biçim
> kurallarını **derleme hatası** olarak raporladığı anlamına gelir. Yani
> "build yeşil / format kırmızı" durumu bu repoda **yapısal olarak
> imkânsızdır**: bozuk girintili bir satır eklendiğinde `dotnet build`
> kendisi `IDE0055` ile **kırmızı** çıkar, `dotnet format`'a hiç sıra
> gelmeden. `dotnet format` yalnız build'in YAKALAMADIĞI (derleyicinin
> görmediği) analyzer tanılarını yakalar — bu, dosyanın en üstündeki uyarı
> notuyla (`⚠️ dotnet format, dotnet build'in yakalamadığı analyzer
> tanılarını yakalayabilir`) tutarlıdır, ama BU case'in kurgusu ("build
> farkına varmaz, format yakalar") o notun kapsadığı senaryo değil — bugünkü
> yapılandırmada IDE0055 sınıfı için ikisi de aynı anda kırmızıdır.

~~Eski beklenti: `dotnet format` çıkış kodu sıfır değildir ve `IDE0055`
(veya kardeşi) bildirir; `dotnet build` bunu YAKALAMAZ (yeşil kalır). Son
adımdan sonra `git status` yalnız `docs/manuel-test/` gösterir.~~

---

### MT-PKG-012 — Uyarı gerçekten hataya dönüşüyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

Negatif senaryo. `TreatWarningsAsErrors=true` ve `WarningsNotAsErrors` boştur —
yani hiçbir uyarı muaf değildir. Bu iddia ölçülmeden kabul edilmez.

**Ön koşul**
- Repo temiz.

**Adımlar**
1. `AgentPrism.Core` içine, XML dokümanı olmayan bir public tip ekle.
2. Yalnız o projeyi derle.
3. Dosyayı sil.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
cat > src/AgentPrism.Core/ManuelUyariTesti.cs <<'EOF'
namespace AgentPrism;

public sealed class ManuelUyariTesti
{
    public int Deger { get; set; }
}
EOF

dotnet build src/AgentPrism.Core -c Release ; echo "cikis kodu: $?"
rm src/AgentPrism.Core/ManuelUyariTesti.cs
```

**Beklenen sonuç**
- Derleme **başarısız** olur (çıkış kodu ≠ 0).
- Çıktıda `CS1591` **`error`** olarak görünür, `warning` olarak değil.
- Dosya silindikten sonra `dotnet build src/AgentPrism.Core -c Release` yeniden geçer.

---

### MT-PKG-013 — Senkronizasyon kopyası derlemeyi kırar, sessizce geçmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29, 30, 41, 48 |
| **İlgili karar** | K-228 |

Negatif senaryo. `<ad> 2.cs` biçimindeki bulut senkronizasyon kopyaları bu repo'da
dört kez bedel ödetti. Bir kopya arayüzü **sessizce** boşaltabilir. Bu case
denetimin gerçekten çalıştığını kanıtlar.

**Ön koşul**
- MT-PKG-010 geçti.

**Adımlar**
1. Kopya denetimini çalıştır — temiz olmalı.
2. Bir `.cs` dosyasının kopyasını oluştur.
3. Derle.
4. Kopyayı sil ve denetimi tekrarla.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
find src -name "* 2.*" -not -path "*/node_modules/*"          # bos olmali

cp src/AgentPrism.Abstractions/Tools/AgentPrismToolAttribute.cs \
   "src/AgentPrism.Abstractions/Tools/AgentPrismToolAttribute 2.cs"

dotnet build src/AgentPrism.Abstractions -c Release ; echo "cikis kodu: $?"

rm "src/AgentPrism.Abstractions/Tools/AgentPrismToolAttribute 2.cs"
find src -name "* 2.*" -not -path "*/node_modules/*"          # yine bos olmali
```

**Beklenen sonuç**
- 1. adımdaki `find` **hiçbir şey** döndürmez.
- 3. adımdaki derleme `CS0101` (aynı ad alanında yinelenen tip) ile başarısız olur.
- 4. adımdan sonra `find` yine boştur ve
  `dotnet build src/AgentPrism.Abstractions -c Release` geçer.

---

### MT-PKG-014 — Hızlı iç döngü arayüz zincirini atlar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PKG-010 geçti.

**Adımlar**
1. Arayüz kapalı biçimde derle ve süreyi ölç.
2. Çıktıda npm adımlarını ara.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
time dotnet build AgentPrism.slnx -c Release -p:AgentPrismFrontendEnabled=false 2>&1 \
  | grep -Ei "npm ci|npm run build|arayuz derleniyor" || echo "NPM ADIMI YOK - beklenen"
```

**Beklenen sonuç**
- Çıktı `NPM ADIMI YOK - beklenen` yazar.
- Derleme başarılı biter.
- Bu bayrakla derlenen çıktı ile **E2E testi koşulmaz** — arayüz varlığı yoktur.

---

### MT-PKG-015 — Arayüz varlığı yokken `pack` hata verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 5 |
| **İlgili karar** | — |

Negatif senaryo. İçi boş bir arayüz paketi yayınlanamaz. Kod bunu iki seviyede
korur: derlemede `AGENTPRISM0002` uyarısı, `pack` aşamasında `AGENTPRISM0003` hatası.

**Ön koşul**
- MT-PKG-010 geçti.

**Adımlar**
1. Üretilmiş arayüz çıktısını ve damgayı geçici bir yere taşı.
2. `AgentPrism.UI` paketini, arayüz zinciri kapalıyken paketle.
3. Çıktıyı geri koy ve normal derlemeyle yeniden üret.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
mv src/AgentPrism.UI/wwwroot /tmp/ap-wwwroot-yedek

MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.UI -c Release \
  -p:AgentPrismFrontendEnabled=true --no-build -o /tmp/ap-ui-test 2>&1 | grep AGENTPRISM0003
echo "cikis kodu: $?"

mv /tmp/ap-wwwroot-yedek src/AgentPrism.UI/wwwroot
dotnet build src/AgentPrism.UI -c Release
```

**Beklenen sonuç**
- `pack` **başarısız** olur ve `AGENTPRISM0003` kodlu hata verir.
- Hata metni "arayuz varligi uretilmemis" ve "Node.js 20.19+ kurun" ifadelerini taşır.
- Son adımdan sonra `src/AgentPrism.UI/wwwroot/index.html` yeniden vardır.

> ⚠️ Bu case dosya taşır. Adım 3 **atlanmaz**; atlanırsa sonraki tüm arayüz
> dosyaları yanlış sonuç verir.

---

### MT-PKG-016 — Yayınlanabilir pakette `README.md` eksikse derleme durur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

Negatif senaryo. NuGet paket sayfası `README.md` olmadan boş görünür.
`Directory.Build.targets` bunu `AGENTPRISM0001` ile erken yakalar.

**Ön koşul**
- Repo temiz.

**Adımlar**
1. `AgentPrism.Voice` paketinin `README.md` dosyasını geçici olarak taşı.
2. O projeyi paketle.
3. Dosyayı geri koy.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
mv src/AgentPrism.Voice/README.md /tmp/ap-voice-readme.md

MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Voice -c Release -o /tmp/ap-voice-test 2>&1 \
  | grep AGENTPRISM0001

mv /tmp/ap-voice-readme.md src/AgentPrism.Voice/README.md
```

**Beklenen sonuç**
- `pack` başarısız olur ve `AGENTPRISM0001` kodlu hata verir.
- Hata metni paket adını (`AgentPrism.Voice`) taşır.
- Dosya geri konduktan sonra aynı komut başarılı biter.

### MT-PKG-020 — Paket sayısı ve sembol paketi sayısı

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 0, 37 |
| **İlgili karar** | — |

`src/` altında 18 proje vardır. Biri (`AgentPrism.Generators`) `IsPackable=false`
taşır ve yayımlanmaz. `AgentPrism.Templates` sembol paketi üretmez
(`IncludeSymbols=false`) — içinde derlenen bir derleme yoktur.

**Ön koşul**
- Repo temiz.

**Adımlar**
1. Eski paket çıktısını temizle.
2. Çözümü paketle.
3. Üretilen dosyaları say.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
rm -rf /tmp/ap-pack && mkdir -p /tmp/ap-pack

MSBUILDDISABLENODEREUSE=1 dotnet pack AgentPrism.slnx -c Release -o /tmp/ap-pack

echo "nupkg : $(ls /tmp/ap-pack/*.nupkg  | wc -l)"
echo "snupkg: $(ls /tmp/ap-pack/*.snupkg | wc -l)"
ls /tmp/ap-pack/*.nupkg | sed 's#.*/##' | sort
ls /tmp/ap-pack/*.nupkg | grep -i generators || echo "Generators yayimlanmadi - beklenen"
```

**Beklenen sonuç**
- **17** `.nupkg` üretilir.
- **16** `.snupkg` üretilir; eksik olan `AgentPrism.Templates`'tir.
- `AgentPrism.Generators` hiçbir çıktı üretmez ve son satır
  `Generators yayimlanmadi - beklenen` yazar.
- Paket adları: `AgentPrism`, `.Abstractions`, `.Anthropic`, `.AspNetCore`,
  `.Azure`, `.Core`, `.Google`, `.Mcp`, `.OpenAI`, `.PostgreSql`, `.SqlServer`,
  `.Sqlite`, `.Templates`, `.Testing`, `.UI`, `.Voice`, `.Workflows`.
- `AgentPrism.Sql.Shared` bir paket **değildir** ve listede görünmez.

---

### MT-PKG-021 — 🚨 Kaynak üreteci `.nupkg` içinde taşınıyor mu

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-348 |

🚨 **Bu case bir şüphe üzerine yazıldı ve üretim oturumunda ölçüldü.** Ölçüm
şunu gösterdi: `--no-build` ile paketlenen `AgentPrism.Core`, üreteç DLL'ini
**taşımıyor**. Ayrıntı [`00-INDEKS.md`](00-INDEKS.md) §8'dedir. Koşum bu ölçümü
tekrarlar ve kapsamını kesinleştirir.

Etki: üreteç eksikse tüketicinin `AddGeneratedTools()` çağrısı `CS1061` verir.
Faz 52'nin bütün kazanımı bu tek dosyaya bağlıdır.

**Ön koşul**
- Repo temiz.

**Adımlar**
1. `AgentPrism.Core`'u tek başına, `--no-build` **olmadan** paketle ve içeriğe bak.
2. `AgentPrism.Core`'u tek başına, `--no-build` **ile** paketle ve içeriğe bak.
3. Çözüm genelinde, `--no-build` **olmadan** paketle ve içeriğe bak.
4. Çözüm genelinde, doğrulama kapısının kullandığı biçimde (`--no-build` ile)
   paketle ve içeriğe bak.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
dotnet build AgentPrism.slnx -c Release

for etiket in "tek-build" "tek-nobuild" "cozum-build" "cozum-nobuild"; do
  rm -rf "/tmp/ap-$etiket" && mkdir -p "/tmp/ap-$etiket"
done

MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Core -c Release            -o /tmp/ap-tek-build
MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Core -c Release --no-build -o /tmp/ap-tek-nobuild
MSBUILDDISABLENODEREUSE=1 dotnet pack AgentPrism.slnx     -c Release            -o /tmp/ap-cozum-build
MSBUILDDISABLENODEREUSE=1 dotnet pack AgentPrism.slnx     -c Release --no-build -o /tmp/ap-cozum-nobuild

for etiket in "tek-build" "tek-nobuild" "cozum-build" "cozum-nobuild"; do
  printf '%-16s ' "$etiket"
  unzip -l /tmp/ap-$etiket/AgentPrism.Core.*.nupkg \
    | grep -c "analyzers/dotnet/cs/AgentPrism.Generators.dll"
done
```

**Beklenen sonuç**
- Dört satırın **dördü de** `1` yazmalıdır.
- Herhangi biri `0` yazarsa case **Kaldı**'dır ve `Kritik` bir hata bildirimi
  açılır. Bugünkü ölçüm `tek-nobuild` için `0` bekliyor — bu doğrulanırsa
  yayın **durur**.
- `1` yazan çıktıda DLL boyutu ~53 KB'dir.

**Doğrulama sorgusu**
```bash
unzip -l /tmp/ap-cozum-nobuild/AgentPrism.Core.*.nupkg | grep analyzers
```

---

### MT-PKG-022 — Her pakette üç TFM ve XML dokümanı var

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | K-005 |

**Ön koşul**
- MT-PKG-020 koşuldu, `/tmp/ap-pack` dolu.

**Adımlar**
1. `AgentPrism.Core` paketinin dosya listesini oku.
2. Tek TFM'li paketleri ayrı doğrula.

**Girilecek veri**
```bash
unzip -l /tmp/ap-pack/AgentPrism.Core.*.nupkg    | grep -E "lib/|README"
unzip -l /tmp/ap-pack/AgentPrism.Testing.*.nupkg | grep -E "lib/"
unzip -l /tmp/ap-pack/AgentPrism.*.nupkg         2>/dev/null | head -1
```

**Beklenen sonuç**
- `AgentPrism.Core` şunları içerir: `lib/net8.0/`, `lib/net9.0/`, `lib/net10.0/` —
  her birinde `AgentPrism.Core.dll` **ve** `AgentPrism.Core.xml`.
- Paket kökünde `README.md` vardır.
- `AgentPrism.Testing` yalnız `lib/net10.0/` içerir (tek TFM, bilinçli).
- `AgentPrism` (meta) hiçbir `lib/` klasörü içermez.

---

### MT-PKG-023 — nuspec üstverisi eksiksiz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. `AgentPrism.Core` paketinden nuspec'i çıkar.
2. Üstveri alanlarını oku.

**Girilecek veri**
```bash
cd /tmp && rm -rf ap-nuspec && mkdir ap-nuspec && cd ap-nuspec
unzip -o /tmp/ap-pack/AgentPrism.Core.*.nupkg "*.nuspec" > /dev/null
cat AgentPrism.Core.nuspec
```

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** `<requireLicenseAcceptance>`
> elementi nuspec'e hiç yazılmaz (2026-08-15 koşumunda `AgentPrism.Core
> 0.0.0-preview.0.166` nuspec'inde de doğrulandı — element yok). NuGet bu
> yokluğu `false` olarak yorumlar, yani davranışsal etki yoktur; dokümanın
> "vardır" iddiası koda göre düzeltildi.

~~Eski beklenti: `<requireLicenseAcceptance>false</requireLicenseAcceptance>`
vardır.~~
- `<license type="expression">MIT</license>` vardır.
- `<readme>README.md</readme>` vardır.
- `<authors>Faruk Atasoy</authors>` ve `<projectUrl>` /
  `<repository type="git" url="https://github.com/farukatasoy/AgentPrism">` vardır.
- `<repository>` düğümü **boş olmayan** bir `commit` niteliği taşır.
- `<tags>` içinde `agentprism ai agents microsoft-agent-framework llm dotnet`
  geçer.
- 🚨 Hiçbir alan `TODO`, `placeholder` veya boş dize taşımaz.

---

### MT-PKG-024 — Sembol paketi taşınabilir PDB taşır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. `AgentPrism.Core` sembol paketinin içeriğini listele.

**Girilecek veri**
```bash
unzip -l /tmp/ap-pack/AgentPrism.Core.*.snupkg | grep -E "\.pdb|\.nuspec"
```

**Beklenen sonuç**
- Üç TFM için birer `.pdb` dosyası vardır (`lib/net8.0/`, `lib/net9.0/`, `lib/net10.0/`).
- Paket uzantısı `.snupkg`'dir (`.symbols.nupkg` değil).

---

### MT-PKG-025 — Deterministik build: iki paketleme aynı derlemeyi üretir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

Sınır senaryosu. `Deterministic=true` aynı girdiden aynı çıktıyı vaat eder.
Karşılaştırma `.nupkg` üzerinde değil, içindeki **derleme** üzerinde yapılır —
zip zaman damgası her seferinde değişir.

**Ön koşul**
- Repo temiz. `git status` yalnız `docs/manuel-test/` gösterir.

**Adımlar**
1. Birinci kez paketle ve `AgentPrism.Abstractions.dll` özetini al.
2. Ara çıktıyı sil.
3. İkinci kez paketle ve özeti tekrar al.
4. İki özeti karşılaştır.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
ozet() {
  rm -rf /tmp/ap-det && mkdir -p /tmp/ap-det && cd /tmp/ap-det
  unzip -o "$1"/AgentPrism.Abstractions.*.nupkg "lib/net10.0/*.dll" > /dev/null
  shasum -a 256 lib/net10.0/AgentPrism.Abstractions.dll | cut -d' ' -f1
  cd /Users/farukatasoy/Desktop/projects/AgentPrism
}

rm -rf /tmp/ap-d1 /tmp/ap-d2
MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Abstractions -c Release -o /tmp/ap-d1
A=$(ozet /tmp/ap-d1)

rm -rf artifacts/obj/AgentPrism.Abstractions
MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Abstractions -c Release -o /tmp/ap-d2
B=$(ozet /tmp/ap-d2)

echo "1: $A"; echo "2: $B"; [ "$A" = "$B" ] && echo "AYNI" || echo "🚨 FARKLI"
```

**Beklenen sonuç**
- Çıktı `AYNI` yazar.
- `FARKLI` çıkarsa gömülü mutlak yol veya zaman damgası aranır.

---

### MT-PKG-026 — Şablon paketi doğru biçimde kurulur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | — |

`.template.config` klasörü nokta ile başlar. `NoDefaultExcludes` olmadan pakete
hiç girmez ve `dotnet new install` **sessizce çalışmayan** bir paket kurar.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. Şablon paketinin içeriğini listele.
2. nuspec'te paket tipini oku.

**Girilecek veri**
```bash
unzip -l /tmp/ap-pack/AgentPrism.Templates.*.nupkg | grep -E "template.config|content/"
cd /tmp && rm -rf ap-tpl && mkdir ap-tpl && cd ap-tpl
unzip -o /tmp/ap-pack/AgentPrism.Templates.*.nupkg "*.nuspec" > /dev/null
grep -E "packageType|<readme>" AgentPrism.Templates.nuspec
```

**Beklenen sonuç**
- `content/AgentPrism.Starter/.template.config/template.json` **ve**
  `dotnetcli.host.json` paket içindedir.
- `content/AgentPrism.Starter/.gitignore` paket içindedir.
- Yollar tek katmanlıdır: `content/content/...` **yoktur**.
- nuspec `<packageType name="Template" ...>` taşır.
- Paket hiçbir `lib/` klasörü içermez.

---

### MT-PKG-027 — Sürüm git etiketinden gelir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | — |

Sınır senaryosu. Bugün repo'da **hiç etiket yoktur**; MinVer taban sürümü ve
commit yüksekliğini kullanır. Yayın anında bu davranış değişir.

**Ön koşul**
- Repo temiz. Bu case yerel bir etiket oluşturur ve **siler**.

**Adımlar**
1. Etiketsiz durumdaki sürümü oku.
2. Geçici bir sürüm etiketi at.
3. Sürümü yeniden oku.
4. Etiketi sil ve ilk sürümün geri geldiğini doğrula.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
git tag                                     # bos olmali

MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Abstractions -c Release -o /tmp/ap-v1
ls /tmp/ap-v1/*.nupkg | sed 's#.*/##'

git tag v1.0.0-preview.1
MSBUILDDISABLENODEREUSE=1 dotnet pack src/AgentPrism.Abstractions -c Release -o /tmp/ap-v2
ls /tmp/ap-v2/*.nupkg | sed 's#.*/##'

git tag -d v1.0.0-preview.1
```

**Beklenen sonuç**
- Etiketsiz sürüm `0.0.0-preview.0.<N>` biçimindedir; `<N>` commit sayısıdır.
- Etiketten sonra sürüm tam olarak `1.0.0-preview.1`'dir.
- `git tag -d` sonrası `git tag` yine boştur.

### MT-PKG-030 — Meta paket yalnız altı bileşen getirir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | K-185 |

Tasarım kuralı: PostgreSQL kullanmayan tüketici `Npgsql`'i çekmez, Gemini
kullanmayan tüketici `Google.Apis.Auth` zincirini almaz.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. Meta paketin nuspec bağımlılıklarını oku.

**Girilecek veri**
```bash
cd /tmp && rm -rf ap-meta && mkdir ap-meta && cd ap-meta
unzip -o /tmp/ap-pack/AgentPrism.0.*.nupkg "*.nuspec" > /dev/null
grep -E "<dependency id=" AgentPrism.nuspec | sort -u
```

**Beklenen sonuç**
- Yalnız şu altı bağımlılık görünür: `AgentPrism.AspNetCore`, `AgentPrism.Mcp`,
  `AgentPrism.OpenAI`, `AgentPrism.PostgreSql`, `AgentPrism.UI`,
  `AgentPrism.Workflows`.
- Şunlar **görünmez**: `AgentPrism.SqlServer`, `AgentPrism.Sqlite`,
  `AgentPrism.Anthropic`, `AgentPrism.Google`, `AgentPrism.Azure`,
  `AgentPrism.Voice`, `AgentPrism.Testing`, `AgentPrism.Templates`.

---

### MT-PKG-031 — Geçişli sabitleme kapalı: grafik kirlenmiyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | K-007 |

Sınır senaryosu. `CentralPackageTransitivePinningEnabled=true` olsaydı
`AgentPrism.PostgreSql` 13 doğrudan bağımlılık bildirirdi. Kapalıyken 2 bekleniyor.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. `AgentPrism.PostgreSql` nuspec'inin `net10.0` grubunu oku.
2. `AgentPrism.Abstractions` için tekrarla.

**Girilecek veri**
```bash
cd /tmp && rm -rf ap-dep && mkdir ap-dep && cd ap-dep
unzip -o /tmp/ap-pack/AgentPrism.PostgreSql.*.nupkg   "*.nuspec" > /dev/null
unzip -o /tmp/ap-pack/AgentPrism.Abstractions.*.nupkg "*.nuspec" > /dev/null

echo "--- PostgreSql"   && grep -A20 'targetFramework="net10.0"' AgentPrism.PostgreSql.nuspec   | grep "dependency id"
echo "--- Abstractions" && grep -A20 'targetFramework="net10.0"' AgentPrism.Abstractions.nuspec | grep "dependency id"
```

**Beklenen sonuç**
- `AgentPrism.PostgreSql` **2** bağımlılık bildirir: `AgentPrism.Core` ve `Npgsql`.
- `AgentPrism.Abstractions` **2** bağımlılık bildirir:
  `Microsoft.Agents.AI.Abstractions` ve `Microsoft.Extensions.AI.Abstractions`.
- Hiçbirinde `OpenTelemetry.Api`, `OpenAI` gibi geçişli paketler **görünmez**.

---

### MT-PKG-032 — Önsürüm MAF paketleri yalnız `AgentPrism.AspNetCore`'da

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | K-008 |

Negatif senaryo. Bir önsürüm bağımlılığının `Core`'a veya `Abstractions`'a
sızması, kararlı bir 1.0 verilmesini imkânsız kılar.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. Her paketin nuspec'inde önsürüm sürüm dizgisi ara.

**Girilecek veri**
```bash
cd /tmp && rm -rf ap-pre && mkdir ap-pre && cd ap-pre
for f in /tmp/ap-pack/*.nupkg; do
  ad=$(basename "$f" .nupkg)
  unzip -p "$f" "*.nuspec" 2>/dev/null \
    | grep -E 'dependency id="(Microsoft\.Agents|A2A)' \
    | grep -E 'preview|alpha|rc|beta' \
    | sed "s#^#$ad : #"
done
```

**Beklenen sonuç**
- Çıktının **her** satırı `AgentPrism.AspNetCore` ile başlar.
- `AgentPrism.Core`, `AgentPrism.Abstractions`, `AgentPrism.PostgreSql`,
  `AgentPrism.OpenAI` hiçbir satırda görünmez.
- Beklenen önsürüm paketleri: `Microsoft.Agents.AI.Hosting`,
  `Microsoft.Agents.AI.Hosting.OpenAI`, `Microsoft.Agents.AI.Hosting.A2A`,
  `Microsoft.Agents.AI.Hosting.AspNetCore`, `A2A.AspNetCore`.

---

### MT-PKG-033 — Roslyn tüketicinin grafiğine sızmıyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-348, K-349 |

Negatif senaryo. Üreteç `Microsoft.CodeAnalysis.CSharp 4.8.0`'a bağlıdır. Bu
bağımlılık tüketiciye geçerse ağır ve gereksiz bir yük olur.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. Tüm nuspec'lerde `CodeAnalysis` ara.

**Girilecek veri**
```bash
for f in /tmp/ap-pack/*.nupkg; do
  unzip -p "$f" "*.nuspec" 2>/dev/null | grep -i "CodeAnalysis" \
    && echo "🚨 $(basename $f)"
done
echo "tarama bitti"
```

**Beklenen sonuç**
- Hiçbir `🚨` satırı yazılmaz; yalnız `tarama bitti` görünür.
- `Microsoft.CodeAnalysis.CSharp` hiçbir pakette bağımlılık olarak görünmez.

---

### MT-PKG-034 — Ağır sağlayıcı zincirleri kendi paketlerinde kalıyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-205 |

`Google.GenAI`, `Google.Apis.Auth` üzerinden `Newtonsoft.Json`,
`System.Management` ve `System.CodeDom` getirir. Bu yük yalnız Gemini
kullananları ilgilendirir.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. Sağlayıcı SDK'larının hangi paketlerde bildirildiğini ara.

**Girilecek veri**
```bash
for f in /tmp/ap-pack/*.nupkg; do
  ad=$(basename "$f" .nupkg | sed 's/\.0\.0\.0.*//')
  unzip -p "$f" "*.nuspec" 2>/dev/null \
    | grep -E 'dependency id="(Google\.GenAI|Anthropic|OpenAI|Npgsql|Microsoft\.Data\.SqlClient)"' \
    | sed "s#^#$ad : #"
done
```

**Beklenen sonuç**
- `Google.GenAI` yalnız `AgentPrism.Google`'da görünür.
- `Anthropic` yalnız `AgentPrism.Anthropic`'te görünür.
- `Npgsql` yalnız `AgentPrism.PostgreSql`'de görünür.
- `Microsoft.Data.SqlClient` yalnız `AgentPrism.SqlServer`'da görünür.
- Hiçbiri meta pakette (`AgentPrism`) görünmez.

### MT-PKG-040 — Üreteç işaretli statik metodu kaydeder

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-350 |

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. İşaretli bir tool sınıfı yaz.
2. Üretilen kodu diske yazdırarak derle.
3. Üretilen dosyayı oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Tools.cs <<'EOF'
using AgentPrism;

internal static class SiparisTools
{
    [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
    public static string GetOrderStatus(string orderId) => $"{orderId}: kargoda";
}
EOF

dotnet build -c Release \
  -p:EmitCompilerGeneratedFiles=true \
  -p:CompilerGeneratedFilesOutputPath=obj/generated

find obj/generated -name "*.g.cs" | head
cat $(find obj/generated -name "AgentPrismGeneratedTools.g.cs" | head -1)
```

**Beklenen sonuç**
- Derleme sıfır uyarıyla biter.
- `AgentPrismGeneratedTools.g.cs` üretilir.
- Dosyanın ilk satırı `// <auto-generated/>`'dır.
- Dosya `AddGeneratedTools` adlı bir uzantı metodu tanımlar
  (`namespace AgentPrism`).
- Dosyada `get_order_status` dizgisi geçer.
- Dosyada `System.Reflection`, `Activator.`, `GetMethod(` ve
  `AIFunctionFactory` dizgilerinin **hiçbiri** geçmez.

---

### MT-PKG-041 — Üretilen kod `dotnet format` kapısını geçer

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | — |

Sınır senaryosu. Üretilen kod tüketicinin biçim kapısını kırarsa paket
kullanılamaz hâle gelir.

**Ön koşul**
- MT-PKG-040 geçti.

**Adımlar**
1. Tüketici projesinde biçim denetimini koş.
2. Üretilen dosyada TAB ve satır sonu boşluğu ara.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
dotnet format --verify-no-changes --no-restore ; echo "format cikis kodu: $?"

G=$(find obj/generated -name "AgentPrismGeneratedTools.g.cs" | head -1)
grep -Pn '\t'      "$G" && echo "🚨 TAB var" || echo "TAB yok"
grep -Pn '[ \t]+$' "$G" && echo "🚨 satir sonu boslugu var" || echo "satir sonu temiz"
```

**Beklenen sonuç**
- `dotnet format` çıkış kodu **0**'dır.
- Çıktı `TAB yok` ve `satir sonu temiz` yazar.

---

### MT-PKG-042 — `APG0001`: aynı tool adı iki metotta

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | — |

Negatif senaryo. Bugüne kadar hiçbir yerde yakalanmıyordu; son kayıt sessizce
kazanabiliyordu.

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. Aynı adı iki metoda ver.
2. Derle.
3. Dosyayı sil.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Hata.cs <<'EOF'
using AgentPrism;

internal static class CakisanTools
{
    [AgentPrismTool("ayni_ad", "Birinci.")]
    public static string Bir(string a) => a;

    [AgentPrismTool("ayni_ad", "Ikinci.")]
    public static string Iki(string a) => a;
}
EOF

dotnet build -c Release 2>&1 | grep -E "APG0001|error"
rm Hata.cs
```

**Beklenen sonuç**
- Derleme **başarısız** olur.
- `APG0001` **error** olarak çıkar.
- Mesaj `ayni_ad` adını ve **iki** metodu birden listeler
  (`CakisanTools.Bir, CakisanTools.Iki`).
- Tanı iki ayrı konumda birden bildirilir.

---

### MT-PKG-043 — `APG0002`: geçersiz karakterli tool adı

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | — |

Negatif ve sınır senaryosu. Kural: 1–64 karakter, yalnız harf, rakam, `_` ve `-`.

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. Üç geçersiz ad dene: boşluklu, nokta içeren, 65 karakter.
2. Derle.
3. Sınırın tam üstünü (64 karakter) ayrı dene.
4. Dosyaları sil.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Hata.cs <<'EOF'
using AgentPrism;

internal static class KotuAdTools
{
    [AgentPrismTool("get order", "Bosluk var.")]
    public static string A(string x) => x;

    [AgentPrismTool("get.order", "Nokta var.")]
    public static string B(string x) => x;

    [AgentPrismTool("aaaaaaaaaabbbbbbbbbbccccccccccddddddddddeeeeeeeeeeffffffffffggggg", "65 karakter.")]
    public static string C(string x) => x;
}
EOF
dotnet build -c Release 2>&1 | grep -c "APG0002"

cat > Hata.cs <<'EOF'
using AgentPrism;

internal static class SinirAdTools
{
    [AgentPrismTool("aaaaaaaaaabbbbbbbbbbccccccccccddddddddddeeeeeeeeeeffffffffffgggg", "64 karakter - gecerli.")]
    public static string D(string x) => x;
}
EOF
dotnet build -c Release 2>&1 | grep -c "APG0002"

rm Hata.cs
```

**Beklenen sonuç**
- Birinci derleme **3** `APG0002` tanısı üretir ve başarısız olur.
- İkinci derleme **0** `APG0002` üretir ve başarılı biter — 64 karakter geçerlidir.
- Mesaj hem metot adını hem geçersiz tool adını taşır.

---

### MT-PKG-044 — `APG0003`: desteklenmeyen parametre tipi

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-351 |

Negatif senaryo. Beyaz liste `record`/`class` tiplerini **kapsamaz**; bu bilinçli
bir sınırdır ve tanı kaçış yolunu göstermelidir.

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. Bir `record` parametreli tool yaz.
2. Derle.
3. Beyaz listedeki tipleri ayrı bir dosyada dene.
4. Dosyaları sil.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Hata.cs <<'EOF'
using AgentPrism;

internal sealed record Siparis(string Id, int Adet);

internal static class BilesikTools
{
    [AgentPrismTool("siparis_ver", "Bilesik tip - desteklenmez.")]
    public static string SiparisVer(Siparis siparis) => siparis.Id;
}
EOF
dotnet build -c Release 2>&1 | grep "APG0003"

cat > Hata.cs <<'EOF'
using System;
using System.Collections.Generic;
using System.Threading;
using AgentPrism;

internal enum Oncelik { Dusuk, Yuksek }

internal static class BeyazListeTools
{
    [AgentPrismTool("beyaz_liste", "Desteklenen tiplerin tamami.")]
    public static string Hepsi(
        string a, int b, long c, double d, decimal e, bool f,
        Guid g, DateTime h, DateTimeOffset i, Oncelik j,
        int? k, string[] l, IReadOnlyList<int> m, CancellationToken ct) => a;
}
EOF
dotnet build -c Release 2>&1 | grep -c "APG0003"

rm Hata.cs
```

**Beklenen sonuç**
- Birinci derleme başarısız olur ve `APG0003` verir.
- `APG0003` mesajı desteklenen tipleri **listeler** ve
  `AddTool(AIFunctionFactory.Create(...))` kaçış yolunu gösterir.
- İkinci derleme **0** `APG0003` üretir ve başarılı biter.
- İkinci derlemede `CancellationToken` bir tool parametresi olarak kabul edilir.

---

### MT-PKG-045 — `APG0004`: generic metot tool olamaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | — |

Negatif senaryo. Bu hata daha önce **çalışma anında** çıkıyordu.

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. Generic bir tool metodu yaz.
2. Derle.
3. Dosyayı sil.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Hata.cs <<'EOF'
using AgentPrism;

internal static class GenericTools
{
    [AgentPrismTool("generic_tool", "Generic - desteklenmez.")]
    public static string Getir<T>(string id) => id;
}
EOF
dotnet build -c Release 2>&1 | grep "APG0004"
rm Hata.cs
```

**Beklenen sonuç**
- Derleme başarısız olur ve `APG0004` **error** verir.
- Mesaj somut bir sarmalayıcı metot yazmayı önerir.

---

### MT-PKG-046 — `APG0005`: çağrı var, işaretli metot yok

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | — |

Negatif senaryo. Sessiz atlama yasaktır: `AddGeneratedTools()` çağrıldığı hâlde
hiç tool yoksa derleme durur.

**Ön koşul**
- Bölüm ön koşulu uygulandı. `Tools.cs` **silinmiş** olmalıdır.

**Adımlar**
1. İşaretli tool bırakma.
2. `AddGeneratedTools()` çağıran bir kurulum yaz.
3. Derle.
4. Dosyaları eski hâline getir.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
mv Tools.cs /tmp/ap-tools-yedek.cs

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism().AddGeneratedTools();
EOF

dotnet build -c Release 2>&1 | grep "APG0005"

mv /tmp/ap-tools-yedek.cs Tools.cs
```

**Beklenen sonuç**
- Derleme başarısız olur ve `APG0005` **error** verir.
- Mesaj iki çözüm önerir: metotları işaretlemek veya çağrıyı kaldırmak.
- `Tools.cs` geri konduktan sonra aynı derleme geçer.

---

### MT-PKG-047 — `APG0006`: açıklama eksik — hata değil, uyarı

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | — |

Sınır senaryosu. Bir kütüphane, tüketicinin derlemesini kırma hakkını dikkatli
kullanır. `APG0006` tek **uyarı** seviyeli tanıdır ve bastırılabilir olmalıdır.

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. Açıklamasız bir tool yaz.
2. Derle ve tanının seviyesini oku.
3. Tanıyı bastırarak derle.
4. Dosyayı sil.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Hata.cs <<'EOF'
using AgentPrism;

internal static class AciklamasizTools
{
    [AgentPrismTool("aciklamasiz")]
    public static string Getir(string id) => id;
}
EOF

dotnet build -c Release 2>&1 | grep "APG0006"
echo "--- bastirilmis ---"
dotnet build -c Release -p:NoWarn=APG0006 2>&1 | grep -c "APG0006"

rm Hata.cs
```

**Beklenen sonuç**
- Birinci derlemede `APG0006` **warning** olarak görünür.
- `TreatWarningsAsErrors` bu projede tanımlı değildir, bu yüzden derleme
  **başarılı** biter.
- `-p:NoWarn=APG0006` ile derlemede tanı sayısı **0**'dır ve derleme geçer.

---

### MT-PKG-048 — `APG0007`: örnek metot tool olamaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-218, K-347 |

Negatif senaryo. Bu, üretecin en değerli tanısıdır. Örnek metot tool'ları
**sessizce bozuktu**: MAF, `AIFunctionArguments.Services` olarak boş bir
sağlayıcı geçirir ve yazılan koruma hiç çalışmaz.

**Ön koşul**
- Bölüm ön koşulu uygulandı.

**Adımlar**
1. Örnek (non-static) bir tool metodu yaz.
2. Derle.
3. Metodu `static` yap ve tekrar derle.
4. Dosyayı sil.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Hata.cs <<'EOF'
using AgentPrism;

internal sealed class OrnekTools
{
    [AgentPrismTool("ornek_metot", "Ornek metot - desteklenmez.")]
    public string Getir(string id) => id;
}
EOF
dotnet build -c Release 2>&1 | grep "APG0007"

sed -i '' 's/public string Getir/public static string Getir/' Hata.cs
dotnet build -c Release 2>&1 | grep -c "APG0007"

rm Hata.cs
```

**Beklenen sonuç**
- Birinci derleme başarısız olur ve `APG0007` **error** verir.
- Mesaj K-218'e açıkça atıf yapar ve iki çözüm gösterir: metodu `static` yapmak
  veya tool'u kurulum anında örnekleyip `AddTool(AIFunctionFactory.Create(...))`
  ile kaydetmek.
- `static` yapıldıktan sonra tanı sayısı **0**'dır ve derleme geçer.

---

### MT-PKG-049 — İşaretsiz metot sessizce tool olmaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K2 |

Sınır senaryosu. İşaretleme **açık tercihtir**. Bir sınıfa yeni bir public metot
eklemek onu kendiliğinden agent'lara açmamalıdır — bu bir güvenlik sınırıdır.

**Ön koşul**
- MT-PKG-040 geçti; `Tools.cs` yerinde.

**Adımlar**
1. Aynı sınıfa işaretsiz bir public metot ekle.
2. Derle.
3. Üretilen kodda o metodu ara.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Tools.cs <<'EOF'
using AgentPrism;

internal static class SiparisTools
{
    [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
    public static string GetOrderStatus(string orderId) => $"{orderId}: kargoda";

    public static string GizliYardimci(string x) => x;
}
EOF

rm -rf obj/generated
dotnet build -c Release -p:EmitCompilerGeneratedFiles=true \
  -p:CompilerGeneratedFilesOutputPath=obj/generated

G=$(find obj/generated -name "AgentPrismGeneratedTools.g.cs" | head -1)
grep -c "GizliYardimci"  "$G"
grep -c "GetOrderStatus" "$G"
```

**Beklenen sonuç**
- Derleme sıfır uyarıyla biter.
- `GizliYardimci` sayısı **0**'dır.
- `GetOrderStatus` sayısı **0'dan büyüktür**.
- Hiçbir tanı üretilmez — işaretsiz metot bir hata değildir.

---

### MT-PKG-050 — Üreteç meta paket üzerinden de akıyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-348 |

Sınır senaryosu. Faz 52, üreteci **yalnız** `AgentPrism.Core`'a doğrudan
`PackageReference` veren bir tüketiciyle doğruladı. Gerçek tüketicilerin çoğu
meta paketi (`AgentPrism`) alır. Analyzer varlıkları meta paket üzerinden
geçişli olarak akmalıdır.

**Ön koşul**
- `/tmp/ap-pack` dolu.

**Adımlar**
1. Yeni bir tüketici projesi kur ve **yalnız meta paketi** referansla.
2. İşaretli bir tool ve `AddGeneratedTools()` çağrısı yaz.
3. Derle.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/meta && mkdir -p ~/agentprism-manuel/meta
cd ~/agentprism-manuel/meta
dotnet new web -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .

SURUM=$(ls /tmp/ap-pack/AgentPrism.0.*.nupkg | sed 's#.*/AgentPrism\.##;s#\.nupkg##')
dotnet add package AgentPrism --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;

var builder = WebApplication.CreateBuilder(args);
builder.AddAgentPrism().AddGeneratedTools();
var app = builder.Build();
app.MapAgentPrism("/agentprism");
app.Run();
EOF

cat > Tools.cs <<'EOF'
using AgentPrism;

internal static class MetaTools
{
    [AgentPrismTool("meta_tool", "Meta paket uzerinden uretilen tool.")]
    public static string Getir(string id) => id;
}
EOF

dotnet build -c Release ; echo "cikis kodu: $?"
```

**Beklenen sonuç**
- Derleme **başarılı** olur.
- `CS1061` (`AddGeneratedTools` bulunamadı) hatası **çıkmaz**.
- Çıkış kodu `0`'dır.
- 🚨 `CS1061` çıkarsa bu `Kritik` bir kusurdur: meta paketi alan tüketici
  önerilen yansımasız yolu hiç kullanamaz. MT-PKG-021 ile birlikte incelenir.

### MT-PKG-060 — AOT uyumlu paketler sıfır trim uyarısı verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 0, 52 |
| **İlgili karar** | K-006 |

**Ön koşul**
- MT-PKG-040 geçti; `~/agentprism-manuel/uretec` çalışır durumda.

**Adımlar**
1. Tüketici projesine AOT bayrağını ekle.
2. `osx-arm64` için yayınla.
3. Trim ve AOT uyarılarını ara.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism().AddGeneratedTools();
Console.WriteLine("kayit tamam");
EOF

dotnet publish -c Release -r osx-arm64 -p:PublishAot=true 2>&1 | tee /tmp/ap-aot.log | tail -5
grep -Ec "IL2[0-9]{3}|IL3[0-9]{3}" /tmp/ap-aot.log
./bin/Release/net*/osx-arm64/publish/uretec
```

**Beklenen sonuç**
- `IL2xxx`/`IL3xxx` sayısı **0**'dır.
- Yayınlama başarılı biter.
- Üretilen ikili çalışır ve `kayit tamam` yazar.

---

### MT-PKG-061 — `AddToolsFrom` AOT bedelini çağırana iletiyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 52 |
| **İlgili karar** | K-350 |

Negatif senaryo. Yansıma yolu meşru bir kaçış kapısıdır ama **bedelsiz değildir**.
Uyarı bastırılmamalı, çağırana iletilmelidir.

**Ön koşul**
- MT-PKG-060 geçti.

**Adımlar**
1. `AddGeneratedTools()` yerine `AddToolsFrom(...)` kullan.
2. AOT ile yayınla.
3. Kodu eski hâline getir.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/uretec
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism().AddToolsFrom(typeof(SiparisTools));
Console.WriteLine("kayit tamam");
EOF

dotnet publish -c Release -r osx-arm64 -p:PublishAot=true 2>&1 \
  | grep -E "IL2026|IL3050" | head -5
```

**Beklenen sonuç**
- En az bir `IL2026` veya `IL3050` uyarısı **çıkar**.
- Uyarı `RequiresUnreferencedCode` / `RequiresDynamicCode` gerekçesini taşır.
- Bu bir kusur değildir; beklenen ve belgelenmiş davranıştır. Uyarı **çıkmazsa**
  case `Kaldı`'dır: bastırma yapılmış demektir.

---

### MT-PKG-062 — AOT bayrağı paket bazında doğru

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 0 |
| **İlgili karar** | K-006 |

Sınır senaryosu. AOT vaadi verilen paketle verilmeyen paket ayrı olmalıdır;
vermek yanıltıcı olur.

**Ön koşul**
- Repo kökünde bulunulur.

**Adımlar**
1. Bayrağı `false` yapan projeleri listele.
2. README'nin AOT iddiasıyla karşılaştır.

**Girilecek veri**
```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
grep -l "AgentPrismAotCompatible>false" src/*/*.csproj | sed 's#src/##;s#/.*##' | sort
```

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** Bu case'in kendi eski
> beklentisi ("AOT uyumlu paket olarak yalnız dördü sayılır") güncelliğini
> yitirmişti; `AGENTS.md` zaten "Sekiz paket uyumludur" diyor ve bu koddan
> türetilen kümeyle birebir örtüşüyor. Düzeltilecek taraf bu test dosyasıydı,
> `AGENTS.md`/kod değil.
- Bayrağı `false` yapan projeler tam olarak şunlardır: `AgentPrism` (meta),
  `AgentPrism.AspNetCore`, `AgentPrism.Generators`, `AgentPrism.Mcp`,
  `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.Templates`,
  `AgentPrism.Testing`, `AgentPrism.UI`, `AgentPrism.Workflows`.
- Geriye kalan (AOT uyumlu, **8 paket**) — `AGENTS.md` "Sekiz paket
  uyumludur" der: `Abstractions`, `Anthropic`, `Azure`, `Core`, `Google`,
  `OpenAI`, `PostgreSql`, `Voice`.

~~Eski beklenti (güncelliğini yitirmiş): Geriye kalan (AOT uyumlu) paketler
yalnız dört tanedir — `Abstractions`, `Core`, `PostgreSql`, `OpenAI`.~~

### MT-PKG-070 — Yerel feed kurulur ve şablon yüklenir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PKG-020 geçti.

**Adımlar**
1. Paketleri yerel feed dizinine kopyala.
2. Feed'i NuGet kaynağı olarak ekle.
3. Şablonu kur.
4. Şablonun listede göründüğünü doğrula.

**Girilecek veri**
```bash
mkdir -p ~/agentprism-local-feed
rm -f ~/agentprism-local-feed/*.nupkg
cp /tmp/ap-pack/*.nupkg ~/agentprism-local-feed/

dotnet nuget list source | grep agentprism-local \
  || dotnet nuget add source ~/agentprism-local-feed -n agentprism-local

dotnet new install AgentPrism.Templates::*-* --add-source ~/agentprism-local-feed
dotnet new list agentprism
```

**Beklenen sonuç**
- `dotnet new list agentprism` çıktısında `agentprism-api` kısa adı görünür.
- Şablon adı `AgentPrism control plane (ASP.NET Core)`'dur.
- Dil sütunu `C#`, tip sütunu `project`'tir.

---

### MT-PKG-071 — Varsayılan şablon derlenir ve çalışır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | K-018 |

Varsayılanlar: `--persistence memory`, `--provider openai`, `--ui true`. Bu case
tasarım kuralı #1'i de kanıtlar: veritabanı olmadan hiçbir şey kırılmaz.

**Ön koşul**
- MT-PKG-070 geçti.

**Adımlar**
1. Yeni projeyi üret.
2. Derle.
3. Çalıştır.
4. `meta` ucuna istek at.
5. Arayüzü tarayıcıda aç.
6. Uygulamayı durdur.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/varsayilan && mkdir -p ~/agentprism-manuel/varsayilan
cd ~/agentprism-manuel/varsayilan
cp ~/agentprism-manuel/uretec/nuget.config .

SURUM=$(ls ~/agentprism-local-feed/AgentPrism.0.*.nupkg | sed 's#.*/AgentPrism\.##;s#\.nupkg##')
dotnet new agentprism-api -o . --AgentPrismVersion "$SURUM"

dotnet build -c Release
dotnet run -c Release &
sleep 8

curl -s http://localhost:5081/agentprism/api/meta | head -c 400
open http://localhost:5081/agentprism
```

**Beklenen sonuç**
- `dotnet new` bir hata vermez ve `Program.cs`, `Tools/OrderTools.cs`,
  `appsettings.json`, `README.md`, `.gitignore` üretilir.
- `dotnet build` sıfır hata verir.
- `curl` HTTP 200 döndürür ve gövde bir JSON nesnesidir.
- Tarayıcıda `/agentprism` arayüzü açılır ve boş bir agent listesi değil,
  `support` agent'ını gösterir.
- Uygulama, hiçbir bağlantı dizesi ve API anahtarı **tanımlı değilken** ayağa kalkar.
- Üretilen `appsettings.json` `ConnectionString` ve `ApiKey` alanlarını **boş
  dize** olarak taşır; hiçbir gerçek `secret` içermez.

---

### MT-PKG-072 — Şablon gerçek bir model çağrısı yapar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | K-032 |

Sınır senaryosu. Şablon model adını bilerek **sabitlemez**; yer tutucu
`MODEL_ADINI_BURAYA_YAZIN`'dır. Bu case yer tutucunun gerçekten fark edildiğini
ve düzeltildikten sonra yolun çalıştığını kanıtlar.

**Ön koşul**
- MT-PKG-071 geçti. Uygulama durduruldu.

**Adımlar**
1. Yer tutucuyla çalışırken bir `run` dene.
2. Model adını düzelt.
3. API anahtarını `user-secrets` ile ver.
4. Uygulamayı başlat ve `run`'ı tekrarla.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/varsayilan

# 1) Yer tutucu ile
dotnet run -c Release & sleep 8
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" \
  -d '{"message":"ORD-1001 siparisim nerede?"}' | head -c 400
kill %1

# 2) Model adini duzelt
sed -i '' 's/MODEL_ADINI_BURAYA_YAZIN/gpt-5.4-mini/' Program.cs

# 3) Anahtari ver
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "<OPENAI_ANAHTARINIZ>"

# 4) Tekrar dene
dotnet build -c Release
dotnet run -c Release & sleep 8
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" \
  -d '{"message":"ORD-1001 siparisim nerede?"}' | head -c 600
```

**Beklenen sonuç**
- 1. adımda `run` **başarısız** olur. Yanıt bir hata taşır ve hata metni model
  adını (`MODEL_ADINI_BURAYA_YAZIN`) içerir. Uygulama **çökmez**.
- 4. adımda `run` başarılı biter.
- Yanıt `ORD-1001` dizgisini içerir **ve** `get_order_status` tool'u tam bir kez
  çağrılır. (Model metni sabit değildir; metne bağlı iddia yapılmaz.)
- `/agentprism/api/runs` en az bir kayıt döndürür ve son kaydın `status` alanı
  tamamlanmış durumu gösterir.

---

### MT-PKG-073 — Kalıcılık seçenekleri doğru paket ve kod üretiyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | K-185 |

**Ön koşul**
- MT-PKG-070 geçti.

**Adımlar**
1. Üç kalıcılık varyantını ayrı dizinlerde üret.
2. Her birinin `.csproj` ve `Program.cs` içeriğini denetle.
3. Üçünü de derle.

**Girilecek veri**
```bash
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.0.*.nupkg | sed 's#.*/AgentPrism\.##;s#\.nupkg##')

for p in postgres sqlite sqlserver; do
  rm -rf ~/agentprism-manuel/$p && mkdir -p ~/agentprism-manuel/$p
  cd ~/agentprism-manuel/$p
  cp ~/agentprism-manuel/uretec/nuget.config .
  dotnet new agentprism-api -o . --persistence $p --AgentPrismVersion "$SURUM"
  echo "=== $p csproj ==="   && grep "PackageReference" *.csproj
  echo "=== $p Program.cs ===" && grep -E "Use(PostgreSql|Sqlite|SqlServer)" Program.cs
  echo "=== $p appsettings ===" && grep -E "PostgreSql|Sqlite|SqlServer" appsettings.json
  dotnet build -c Release > /dev/null && echo "$p derlendi"
done
```

**Beklenen sonuç**
- `postgres` varyantı yalnız `AgentPrism` paketini referanslar (PostgreSQL meta
  pakete dâhildir) ve `UsePostgreSql` çağrısını taşır.
- `sqlite` varyantı ek olarak `AgentPrism.Sqlite` paketini referanslar.
- `sqlserver` varyantı ek olarak `AgentPrism.SqlServer` paketini referanslar.
- Her varyantın `appsettings.json` dosyası yalnız **kendi** sağlayıcısının
  bölümünü taşır; diğer ikisi yoktur.
- Hiçbir varyantta `#if` / `//#if` yönergesi üretilmiş dosyada kalmaz.
- Üç varyant da derlenir.

---

### MT-PKG-074 — Sağlayıcı seçenekleri doğru paket ve kod üretiyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26, 27, 37 |
| **İlgili karar** | K-185 |

**Ön koşul**
- MT-PKG-070 geçti.

**Adımlar**
1. Üç sağlayıcı varyantını üret ve derle.
2. Azure varyantını ayrı üret ve derle.

**Girilecek veri**
```bash
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.0.*.nupkg | sed 's#.*/AgentPrism\.##;s#\.nupkg##')

for s in openai anthropic google azure; do
  rm -rf ~/agentprism-manuel/prov-$s && mkdir -p ~/agentprism-manuel/prov-$s
  cd ~/agentprism-manuel/prov-$s
  cp ~/agentprism-manuel/uretec/nuget.config .
  dotnet new agentprism-api -o . --provider $s --AgentPrismVersion "$SURUM"
  echo "=== $s ===" && grep -E "PackageReference|Use(OpenAI|Anthropic|Google|AzureOpenAI)" *.csproj Program.cs
  dotnet build -c Release > /dev/null && echo "$s derlendi"
done
```

**Beklenen sonuç**
- `openai` varyantı ek paket referanslamaz (`AgentPrism.OpenAI` meta paketten gelir).
- `anthropic`, `google`, `azure` varyantları sırasıyla `AgentPrism.Anthropic`,
  `AgentPrism.Google`, `AgentPrism.Azure` paketlerini ekler.
- Her varyantın `Program.cs` dosyası tek bir `Use*` çağrısı taşır.
- Dördü de **derlenir**. Azure varyantının derlenmesi kimlik bilgisi istemez.

⏭ **ATLA — Azure kimliği yok:** Azure varyantının gerçek bir `run` yapması
[`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md)'de test edilir ve orada atlanır.
Bu case yalnız üretim ve derlemeyi kapsar.

---

### MT-PKG-075 — `--ui false` arayüzü hiç bağlamaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | — |

Sınır senaryosu. Arayüz istemeyen tüketici için HTTP API çalışmaya devam etmelidir.

**Ön koşul**
- MT-PKG-070 geçti.

**Adımlar**
1. Arayüzsüz varyantı üret.
2. Derle ve çalıştır.
3. API ucunu ve arayüz kökünü ayrı ayrı dene.

**Girilecek veri**
```bash
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.0.*.nupkg | sed 's#.*/AgentPrism\.##;s#\.nupkg##')
rm -rf ~/agentprism-manuel/uisiz && mkdir -p ~/agentprism-manuel/uisiz
cd ~/agentprism-manuel/uisiz
cp ~/agentprism-manuel/uretec/nuget.config .
dotnet new agentprism-api -o . --ui false --AgentPrismVersion "$SURUM"

grep -c "UseUI" Program.cs
dotnet build -c Release
dotnet run -c Release & sleep 8

echo "meta : $(curl -s -o /dev/null -w '%{http_code}' http://localhost:5081/agentprism/api/meta)"
echo "arayuz: $(curl -s -o /dev/null -w '%{http_code}' http://localhost:5081/agentprism)"
kill %1
```

**Beklenen sonuç**
- `grep -c "UseUI"` çıktısı **0**'dır.
- `meta` satırı **200** gösterir.
- `arayuz` satırı 200 **göstermez** (404 beklenir).
- Uygulama çökmez ve log'da arayüzle ilgili hata yoktur.

---

### MT-PKG-076 — Şablon sürüm sabitlemesi çalışıyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | — |

Negatif ve sınır senaryosu. Varsayılan `*-*` en güncel önsürümü alır. Yanlış bir
sürüm verildiğinde hata **anlaşılır** olmalıdır.

**Ön koşul**
- MT-PKG-070 geçti.

**Adımlar**
1. Sürüm vermeden üret ve `.csproj`'u oku.
2. Var olmayan bir sürümle üret ve restore et.
3. Geçerli sürümle üret.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/surum && mkdir -p ~/agentprism-manuel/surum
cd ~/agentprism-manuel/surum
cp ~/agentprism-manuel/uretec/nuget.config .

dotnet new agentprism-api -o . --skip-restore
grep "AgentPrism" *.csproj

rm -rf ~/agentprism-manuel/surum2 && mkdir -p ~/agentprism-manuel/surum2
cd ~/agentprism-manuel/surum2
cp ~/agentprism-manuel/uretec/nuget.config .
dotnet new agentprism-api -o . --AgentPrismVersion 99.99.99 --skip-restore
dotnet restore 2>&1 | tail -3
```

**Beklenen sonuç**
- Birinci projede `Version="*-*"` görünür ve `AGENTPRISM_TEMPLATE_PACKAGE_VERSION`
  yer tutucusu **kalmaz**.
- `--skip-restore` verildiğinde `dotnet new` restore çalıştırmaz (çıktıda
  "Restoring" satırı yoktur).
- İkinci projede `dotnet restore` **başarısız** olur ve hata mesajı
  `AgentPrism` paket adını **ve** `99.99.99` sürümünü açıkça yazar (`NU1102`).

---

### MT-PKG-077 — Şablon `secret` sızdırmıyor

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 37 |
| **İlgili karar** | K-059 |

Negatif senaryo. Üretilen proje bir geliştiricinin ilk dosyasıdır. Yanlış bir
desen burada öğrenilirse her yerde tekrarlanır.

**Ön koşul**
- MT-PKG-072 koşuldu (`user-secrets` tanımlandı).

**Adımlar**
1. Üretilen projedeki tüm dosyalarda anahtar deseni ara.
2. `user-secrets` kimliğinin proje dosyasında tanımlı olduğunu doğrula.
3. `.gitignore` içeriğini denetle.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/varsayilan
grep -rn "sk-\|api[_-]\?key.*[:=].*[A-Za-z0-9]\{20,\}" \
  --include="*.json" --include="*.cs" --include="*.csproj" . | grep -v "bin/\|obj/"
echo "--- tarama bitti ---"

grep "UserSecretsId" *.csproj
grep -E "appsettings\.\*\.json|\.env|secrets" .gitignore
```

**Beklenen sonuç**
- Tarama hiçbir satır döndürmez; yalnız `--- tarama bitti ---` görünür.
- `appsettings.json` içindeki `ApiKey` ve `ConnectionString` değerleri **boş
  dizedir**.
- `.csproj` bir `UserSecretsId` taşır.
- `.gitignore` en az bir `secret` deseni içerir.

### MT-PKG-080 — Tüketicinin kaydı her zaman kazanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

AgentPrism servislerini `TryAdd*` ile kaydeder. Tüketici kendi
uygulamasını **önce** kaydettiyse AgentPrism onu ezmemelidir.

**Ön koşul**
- MT-PKG-070 geçti.

**Adımlar**
1. Yeni bir konsol projesi kur.
2. Kendi `IRunStore` uygulamanı AgentPrism'den **önce** kaydet.
3. Çözümlenen tipi yazdır.
4. Kaydı AgentPrism'den **sonra** yapıp tekrarla.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/tryadd && mkdir -p ~/agentprism-manuel/tryadd
cd ~/agentprism-manuel/tryadd
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.Core.*.nupkg | sed 's#.*AgentPrism.Core\.##;s#\.nupkg##')
dotnet add package AgentPrism.Core --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

// 1) Tuketicinin kaydi ONCE
var once = new ServiceCollection();
once.AddSingleton<IRunStore, BenimRunStore>();
once.AddAgentPrism();
Console.WriteLine("once: " + once.BuildServiceProvider().GetRequiredService<IRunStore>().GetType().Name);

// 2) Tuketicinin kaydi SONRA
var sonra = new ServiceCollection();
sonra.AddAgentPrism();
sonra.AddSingleton<IRunStore, BenimRunStore>();
Console.WriteLine("sonra: " + sonra.BuildServiceProvider().GetRequiredService<IRunStore>().GetType().Name);
EOF

# BenimRunStore: IRunStore'un tum uyelerini NotSupportedException ile uygular.
# Uyeler icin: grep -n "" src/AgentPrism.Abstractions/Runs/IRunStore.cs

dotnet run -c Release
```

**Beklenen sonuç**
- `once:` satırı `BenimRunStore` yazar — AgentPrism kaydı **ezmez**.
- `sonra:` satırı `BenimRunStore` yazar — son kayıt kazanır.
- Hiçbir adımda istisna atılmaz.

> Not: `BenimRunStore` sınıfını yazmak için önce arayüz üyeleri okunur:
> `grep -n "" src/AgentPrism.Abstractions/Runs/IRunStore.cs`. Bu case, tam
> gövdesi [`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md)'de yazılacak
> `MT-CORE` case'leriyle birlikte koşulabilir.

---

### MT-PKG-081 — Hiçbir `Use*` çağrılmadan kurulum ayakta kalır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | K-018 |

Tasarım kuralı #1: `UsePostgreSql()` çağrılmazsa hiçbir şey kırılmaz. Bu, bellek
içi izleğin **tek** doğrudan testidir. Küçük görünür, atlanmaz.

**Ön koşul**
- MT-PKG-080 projesi hazır.

**Adımlar**
1. Yalnız `AddAgentPrism()` çağır.
2. Servis sağlayıcıyı kur.
3. Çekirdek servisleri çözümle.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/tryadd
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism();

var provider = services.BuildServiceProvider();

Console.WriteLine("IRunStore     : " + provider.GetRequiredService<IRunStore>().GetType().Name);
Console.WriteLine("ISessionStore : " + provider.GetRequiredService<ISessionStore>().GetType().Name);
Console.WriteLine("IToolRegistry : " + provider.GetRequiredService<IToolRegistry>().GetType().Name);
Console.WriteLine("kurulum tamam");
EOF

dotnet run -c Release ; echo "cikis kodu: $?"
```

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** İsim beklentisi eski —
> `ISessionStore` denetim izi dekoratörü (`AuditingSessionStore`) ile sarılı
> (iç deposu yine bellek içi, dış bağımlılık yok); `IToolRegistry` hiç
> `InMemory` önekiyle adlandırılmamış (`ToolRegistry`). 2026-08-15'te kaynak
> yeniden doğrulandı: `src/AgentPrism.Core/Tools/ToolRegistry.cs:9`,
> `src/AgentPrism.Core/Storage/InMemoryRunStore.cs:22`,
> `src/AgentPrism.Core/Audit/AuditingSessionStore.cs:13`.
- Çıkış kodu `0`'dır.
- `IRunStore` → `InMemoryRunStore`, `ISessionStore` → `AuditingSessionStore`,
  `IToolRegistry` → `ToolRegistry` (önek yok).
- Son satır `kurulum tamam`'dır.
- Hiçbir bağlantı denemesi ve hiçbir uyarı log'u yoktur.

~~Eski beklenti (güncelliğini yitirmiş): Üç satır da `InMemory` ile başlayan
bir tip adı yazar.~~

---

### MT-PKG-082 — İki kalıcılık sağlayıcısı aynı anda verilirse

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 23, 24 |
| **İlgili karar** | — |

Negatif senaryo. `README.md` şunu iddia eder: *"üçü AYNI ANDA verilmez; verilirse
son kayıt kazanır ve uyarı loglanır."* Bu iddia ölçülür.

**Ön koşul**
- PostgreSQL container'ı çalışıyor (`ap-pg`).
- SQLite dosya yolu yazılabilir.

**Adımlar**
1. Şablonun `sqlite` varyantına gidip her iki bağlantı dizesini de tanımla.
2. `Program.cs`'e ikinci `Use*` çağrısını ekle.
3. Çalıştır ve açılış log'unu oku.
4. Hangi deponun aktif olduğunu `meta` ucundan doğrula.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/sqlite
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString" "Data Source=manuel-cift.db"
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" \
  "Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"

# Program.cs'e PostgreSql cagrisini de ekle (Sqlite cagrisinin ALTINA).
# agentPrism.UsePostgreSql(builder.Configuration.GetSection("AgentPrism:PostgreSql"));

dotnet build -c Release
dotnet run -c Release 2>&1 | tee /tmp/ap-cift.log &
sleep 10
curl -s http://localhost:5081/agentprism/api/meta
kill %1
grep -iE "warn|uyari|birden fazla|multiple" /tmp/ap-cift.log
```

**Beklenen sonuç**
- Uygulama **çöker değil**, ayağa kalkar.
- `meta` yanıtı **tek** bir aktif kalıcılık sağlayıcısı bildirir.
- Aktif olan, `Program.cs`'te **son** çağrılan sağlayıcıdır.
- Log'da bir uyarı satırı vardır ve iki sağlayıcının birlikte tanımlandığını
  söyler. Uyarı **yoksa** case `Kaldı`'dır: `README.md`'nin iddiası kodla
  doğrulanmıyor demektir.
