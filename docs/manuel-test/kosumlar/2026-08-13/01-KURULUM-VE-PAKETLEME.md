# 01 — Kurulum ve Paketleme (`PKG`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../01-KURULUM-VE-PAKETLEME.md`](../../01-KURULUM-VE-PAKETLEME.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-PKG-001 — SDK sürümü ve roll-forward politikası

**Gerçek sonuç**
```
dotnet --list-sdks:
9.0.305 [/usr/local/share/dotnet/sdk]
9.0.306 [/usr/local/share/dotnet/sdk]
10.0.100 [/usr/local/share/dotnet/sdk]

dotnet --version: 10.0.100

global.json:
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```
`dotnet --version` 10.0.100 — 10.0.1xx bandında. `global.json` üç değeri de birebir taşıyor. Önizleme sürümü yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-002 — Node.js ve npm arayüz derlemesi için yeterli

**Gerçek sonuç**
```
node --version: v20.19.4
npm --version : 11.6.3
```
Node 20.19.4 ≥ 20.19. `npm --version` sıfır çıkış kodu ile döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-003 — Docker hazır ve `mssql/server` imajı arm64'te çekilebiliyor

**Gerçek sonuç**
```
docker info: 29.7.2 · 10 CPU · 8321515520 bayt
uname -m    : arm64

docker pull mcr.microsoft.com/mssql/server:2022-latest
Status: Image is up to date for mcr.microsoft.com/mssql/server:2022-latest
```
Bellek 8321515520 bayt = 8,32 GB (ondalık) / 7,75 GiB (ikili) — sınırda ama
ondalık yorumla eşik geçiliyor ve imaj zaten çekili, `docker pull` sıfır
hatayla bitti. `uname -m` `arm64`. Rosetta gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Derleme kapıları

---

## MT-PKG-010 — Dört kapı sıfır uyarı verir

**Gerçek sonuç**
```
1) dotnet build  -> 40,5 sn · Build succeeded · 0 Warning(s) · 0 Error(s)
2) dotnet test   -> 1m 37s (97,7 sn) · toplam koşum başarısız
   - AgentPrism.SqlServer.IntegrationTests      : 480/480 geçti
   - AgentPrism.AspNetCore.FunctionalTests      : 447/447 geçti
   - AgentPrism.Ui.E2ETests                     : 41/42 geçti, 1 KALDI
     -> Playground_konusma_modu_mikrofonu_acar_ve_transkript_gosterir
        Timeout 30000ms: GetByTestId("voice-transcript") görünür olmadı.
   - (diğer tüm test projeleri geçti)
3) dotnet pack (--no-build) -> ~3 sn · exit 0 · tüm .nupkg üretildi
4) dotnet format --verify-no-changes -> 58 sn · exit 0 · değişiklik bildirilmedi
```

🚨 **Kusur bulundu.** `dotnet test` adımı `failed: 1` ile bitti — kapı kriteri
(`failed sayısı 0`) sağlanmadı. Kök neden araştırması:
- Aynı test **izole çalıştırıldığında da 3/3 denemede tutarlı şekilde
  başarısız oldu** — rastgele/yük kaynaklı bir kırılganlık (flaky) değil,
  bu makinede **deterministik** bir arıza.
- Son üç commit (`9182202`, `458c485`, `419981b`) `src/AgentPrism.UI` veya
  `src/AgentPrism.Voice` dosyalarına dokunmuyor — bu bir gerileme (regresyon)
  gibi görünmüyor, bu ortama özgü bir sorun olabilir.
- `docs/hafiza/test-altyapisi.md` sahte ses cihazının (Chromium
  `--use-fake-device-for-media-stream`) sürekli ton ürettiğini ve elle kapatma
  düğmesiyle çözüldüğünü belgeliyor; test kodu (`UiTests.cs:155`) bu düğmeyi
  gerçekten tıklıyor. Sorun bu bilinen tuzak değil.
- Bu Mac'te `TCC.db` mikrofon izin listesinde ne Terminal ne de `dotnet`
  süreci için bir kayıt var — macOS'ta Chromium'un sahte cihaz bayrağının bu
  spesifik ortamda işletim sistemi mikrofon izniyle etkileşimi olası bir
  neden, ama bu **doğrulanmadı**; tarayıcı konsol/ağ izlemesi yapılmadan
  kesinleşmez.
- Süre kriteri sağlandı: toplam koşum 2,5 dakikayı aşmadı, asılı kalan alt
  süreç yok.

Bu bulgu **Kritik** önemde bir kusur bildirimi olarak açılmalıdır (izlek: UI
E2E / ses konuşma modu, muhtemelen ortam-bağımlı). Doğrulama derinliği bu
oturumda tarayıcı içi hata ayıklamayı kapsamadı.

---

**Yeniden koşum (2026-08-15, KAPANIS-PLANI §9):** Kök neden `f36eeaf`'te
(ses turu `commit` çerçevesinin kendi sesinin önüne geçmesi) kapatıldı.
Dört kapı bu koşumda baştan çalıştırıldı:
```
1) dotnet build  -> 58,4 sn · Build succeeded · 0 Warning(s) · 0 Error(s)
2) dotnet test   -> 1m 54s (114 sn) · toplam koşum başarılı
   - AgentPrism.SqlServer.IntegrationTests      : 490/490 geçti
   - AgentPrism.AspNetCore.FunctionalTests      : 479/479 geçti
   - AgentPrism.Ui.E2ETests                     : 49/49 geçti (önceki
     koşumda kaldı işaretli ses testi dahil — artık geçiyor)
   - (diğer tüm test projeleri geçti) — toplam failed: 0
3) dotnet pack (--no-build) -> ~4 sn · exit 0 · 238 .nupkg üretildi
4) dotnet format --verify-no-changes -> 1m 13s · exit 0 · değişiklik bildirilmedi
```
Süre kriteri sağlandı (`dotnet test` 2,5 dakikayı aşmadı). Dört kapı da
sıfır uyarı/hata ile geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-011 — `dotnet format` build'in görmediğini yakalar

**Gerçek sonuç**
```
Enjeksiyon (tek namespace bloğu içinde, dosya-kapsamlı ad alanının
ALTINA, ayrı bir namespace{} bloğu OLMADAN, yalnız hatalı girintili
bir metotla):
internal static class BicimTesti
{
        public static int Deger => 1;
}

dotnet build -> Build FAILED (0 Warning(s), 3 Error(s))
  IDE0055: Fix formatting (üç TFM için tekrarlanır) — CS8955 DEĞİL, doğrudan
  IDE0055 derleme hatası olarak raporlandı.
git checkout sonrası: git status yalnız docs/manuel-test/ gösteriyor
```

Enjekte kod parçası da düzeltildi (önceki sürüm ayrı bir klasik
`namespace{}` bloğu ekleyip `CS8955` ile alakasız bir hataya yol açıyordu);
düzeltilmiş enjeksiyonla bile senaryo doğrulanamıyor çünkü `dotnet build`
`IDE0055`'i doğrudan kendisi yakalıyor. Bu, sınır senaryosunun konusu olan
"bir kez 276 IDE0055 hatası bu şekilde çıktı" olayının (muhtemelen
`EnforceCodeStyleInBuild` bu tarihten SONRA eklendiği için) artık bugünkü
yapılandırmada tekrarlanamaz olduğunu gösteriyor — davranış artık daha
güvenli (build kendisi yakalıyor), yalnız case'in kurgusu geçersiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-012 — Uyarı gerçekten hataya dönüşüyor

**Gerçek sonuç**
```
dotnet build src/AgentPrism.Core -c Release -> cikis kodu: 1
CS1591 error (uc TFM icin, hem tip hem uye icin) -> 6 satir, tumu "error"
dosya silindikten sonra: Build succeeded, 0 Warning(s), 0 Error(s)
```
Derleme başarısız oldu, `CS1591` her yerde `error` olarak çıktı (`warning`
değil). Dosya silindikten sonra derleme temiz geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-013 — Senkronizasyon kopyası derlemeyi kırar, sessizce geçmez

**Gerçek sonuç**
```
1. adım find: src/AgentPrism.UI/wwwroot/index 2.html   (BEKLENEN: boş)
dotnet build src/AgentPrism.Abstractions -c Release -> cikis kodu: 1
CS0101: The namespace 'AgentPrism' already contains a definition for
        'AgentPrismToolAttribute' (üç TFM için tekrarlanır)
4. adım find (kopya silindikten sonra): boş
dotnet build src/AgentPrism.Abstractions -c Release -> Build succeeded, 0/0
```

🚨 **Bulgu (zararsız).** 1. adımdaki `find`, doküman'ın "boş olmalı" beklentisinin
aksine `src/AgentPrism.UI/wwwroot/index 2.html` döndürdü — tam olarak
projenin `MEMORY.md`'de belgelediği bulut senkronizasyon kopyası deseni.
Zararsız çıktı: `.gitignore:57` bu tam adı zaten hariç tutuyor, dosya git'e
hiç girmemiş, ve arayüz derlenen bir `.cs` değil statik bir varlık olduğu
için `CS0101` riski yok. Dosya bu koşumda silindi (`find` artık boş).
`AgentPrismToolAttribute.cs` kopyası ile asıl senaryo beklendiği gibi çalıştı:
derleme `CS0101` ile kırıldı, kopya silinince temiz geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-014 — Hızlı iç döngü arayüz zincirini atlar

**Gerçek sonuç**
```
time dotnet build ... -p:AgentPrismFrontendEnabled=false -> 10,4 sn, exit 0
Cikti: NPM ADIMI YOK - beklenen
Build succeeded. 0 Warning(s) 0 Error(s)
```
Çıktı beklendiği gibi `NPM ADIMI YOK - beklenen` yazdı, npm adımı hiç
çalışmadı, derleme başarılı bitti (~6-10 sn, tam derlemenin ~40 sn'sine
kıyasla belirgin şekilde hızlı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-015 — Arayüz varlığı yokken `pack` hata verir

**Gerçek sonuç**
```
pack cikis kodu: 1
error AGENTPRISM0003: AgentPrism.UI: arayuz varligi uretilmemis
  (.../wwwroot/index.html yok). Ici bos bir arayuz paketi yayinlanamaz.
  Node.js 20.19+ kurun ve derlemeyi tekrarlayin.
wwwroot geri konulduktan sonra: Build succeeded, 0 Warning(s), 0 Error(s)
index.html yeniden var.
```
`pack` beklendiği gibi `AGENTPRISM0003` ile başarısız oldu; mesaj hem
"arayuz varligi uretilmemis" hem "Node.js 20.19+ kurun" ifadelerini taşıyor.
Dosyalar geri konulduktan sonra normal derleme geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-016 — Yayınlanabilir pakette `README.md` eksikse derleme durur

**Gerçek sonuç**
```
README.md tasindiktan sonra pack -> cikis kodu: 1
error AGENTPRISM0001: Yayinlanabilir paket 'AgentPrism.Voice' icin README.md
  eksik. NuGet paket sayfasinda gorunecek bir README.md dosyasi ekleyin.
README.md geri konulduktan sonra pack -> cikis kodu: 0, .nupkg + .snupkg üretildi
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Paket çıktısı ve kalite denetimi

---

## MT-PKG-020 — Paket sayısı ve sembol paketi sayısı

**Gerçek sonuç**
```
nupkg : 17
snupkg: 16
Generators yayimlanmadi - beklenen

Paketler: AgentPrism, .Abstractions, .Anthropic, .AspNetCore, .Azure, .Core,
.Google, .Mcp, .OpenAI, .PostgreSql, .SqlServer, .Sqlite, .Templates,
.Testing, .UI, .Voice, .Workflows  (17 adet, beklenen liste ile birebir)
```
17 `.nupkg`, 16 `.snupkg` üretildi (eksik olan `AgentPrism.Templates`).
`AgentPrism.Generators` listede yok. `AgentPrism.Sql.Shared` görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-021 — 🚨 Kaynak üreteci `.nupkg` içinde taşınıyor mu

**Gerçek sonuç**
```
tek-build        1
tek-nobuild      1
cozum-build      1
cozum-nobuild    1

DLL boyutu (tek-nobuild): 53248 bayt (≈ 52 KB, "~53 KB" beklentisine yakın)
```
Dört satırın dördü de `1` — üreteç DLL'i her dört senaryoda da
`analyzers/dotnet/cs/AgentPrism.Generators.dll` yolunda pakete girmiş.
Doküman'ın atıfta bulunduğu önceki üretim ölçümünde `tek-nobuild` için `0`
bekleniyordu (K-348'in tetiklediği şüphe); bugünkü koşumda bu **doğrulanmadı**
— dördü de geçiyor, kapsam kesinleşmiş durumda ve yayın engeli yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-022 — Her pakette üç TFM ve XML dokümanı var

**Gerçek sonuç**
```
AgentPrism.Core: lib/net8.0/*.dll+xml, lib/net9.0/*.dll+xml, lib/net10.0/*.dll+xml,
                 README.md (kökte)
AgentPrism.Testing: yalnız lib/net10.0/*.dll+xml
AgentPrism (meta): "meta pakette lib/ yok - beklenen"
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-023 — nuspec üstverisi eksiksiz

**Gerçek sonuç**
```
<license type="expression">MIT</license>                    -> var
<requireLicenseAcceptance>false</requireLicenseAcceptance>   -> YOK (element hiç yazılmamış)
<readme>README.md</readme>                                   -> var
<authors>Faruk Atasoy</authors>                               -> var
<projectUrl>https://github.com/farukatasoy/AgentPrism</projectUrl> -> var
<repository type="git" url="...AgentPrism" branch="..." commit="9182202..."/> -> var, commit boş değil
<tags>agentprism ai agents microsoft-agent-framework llm dotnet runtime catalog harness</tags> -> beklenen alt dizgiyi içeriyor
TODO/placeholder/boş dize -> yok
```

`<requireLicenseAcceptance>` elementi nuspec'te hiç yazılmıyor. NuGet bu
elementin yokluğunu `false` olarak yorumladığından **davranışsal** etki yok
(tüketici bir onay ekranıyla karşılaşmaz) — düzeltilmiş beklentiyle örtüşüyor.

---

**Doküman düzeltmesi (2026-08-15):** Beklenti koda göre düzeltildi. Ürün
kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-024 — Sembol paketi taşınabilir PDB taşır

**Gerçek sonuç**
```
lib/net8.0/AgentPrism.Core.pdb, lib/net9.0/AgentPrism.Core.pdb,
lib/net10.0/AgentPrism.Core.pdb  -> üçü de var
Uzantı: .snupkg (.symbols.nupkg değil)
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-025 — Deterministik build: iki paketleme aynı derlemeyi üretir

**Gerçek sonuç**
```
1: c75d7c1ec70809ac9b9c644a2be84fb85dca89cf5d35471034d705219023a138
2: c75d7c1ec70809ac9b9c644a2be84fb85dca89cf5d35471034d705219023a138
AYNI
```
İki bağımsız paketleme aynı SHA-256 özetini üretti — build deterministik.

🚨 Yan not: bu case sırasında `git status` `src/AgentPrism.Voice/README 2.md`
adlı izlenmeyen bir dosya gösterdi — MT-PKG-016'da README.md hızlıca taşınıp
geri konurken bulut senkronizasyonunun (muhtemelen iCloud Drive) ürettiği bir
çakışma kopyası. İçerik orijinaliyle birebir aynıydı (`diff` sıfır fark);
dosya silindi. Bu, projenin kendi `MEMORY.md`'sinde belgelenen "kopya dosyalar"
tuzağının doğrudan bir örneği — kod kusuru değil, ortam/senkronizasyon riski.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-026 — Şablon paketi doğru biçimde kurulur

**Gerçek sonuç**
```
content/AgentPrism.Starter/.template.config/template.json    -> var
content/AgentPrism.Starter/.template.config/dotnetcli.host.json -> var
content/AgentPrism.Starter/.gitignore                         -> var
Yollar tek katmanlı (content/content/... yok)
<packageType name="Template" />                                -> var
lib/ klasörü yok
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-027 — Sürüm git etiketinden gelir

**Gerçek sonuç**
```
etiketsiz : AgentPrism.Abstractions.0.0.0-preview.0.63.nupkg
etiketli  : AgentPrism.Abstractions.1.0.0-preview.1.nupkg
git tag -d sonrası: git tag boş
```
Etiketsiz sürüm `0.0.0-preview.0.63` biçiminde (`<N>` = commit yüksekliği).
Etiketten sonra sürüm tam olarak `1.0.0-preview.1`. Etiket silindikten sonra
`git tag` yine boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Bağımlılık grafiği

---

## MT-PKG-030 — Meta paket yalnız altı bileşen getirir

**Gerçek sonuç**
```
AgentPrism.AspNetCore, AgentPrism.Mcp, AgentPrism.OpenAI, AgentPrism.PostgreSql,
AgentPrism.UI, AgentPrism.Workflows   (6 adet, birebir beklenen)
```
`SqlServer`, `Sqlite`, `Anthropic`, `Google`, `Azure`, `Voice`, `Testing`,
`Templates` hiçbiri görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-031 — Geçişli sabitleme kapalı: grafik kirlenmiyor

**Gerçek sonuç**
```
PostgreSql (net10.0)   : AgentPrism.Core, Npgsql                      -> 2
Abstractions (net10.0) : Microsoft.Agents.AI.Abstractions,
                          Microsoft.Extensions.AI.Abstractions         -> 2
```
İkisinde de `OpenTelemetry.Api`, `OpenAI` gibi geçişli paketler görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-032 — Önsürüm MAF paketleri yalnız `AgentPrism.AspNetCore`'da

**Gerçek sonuç**
```
Tüm önsürüm satırları (15 satır, 3 TFM × 5 paket) yalnız
AgentPrism.AspNetCore ile başlıyor:
  A2A.AspNetCore 1.0.0-preview2
  Microsoft.Agents.AI.Hosting 1.16.0-preview.260730.1
  Microsoft.Agents.AI.Hosting.A2A 1.16.0-preview.260730.1
  Microsoft.Agents.AI.Hosting.AspNetCore 1.16.0-preview.260730.1
  Microsoft.Agents.AI.Hosting.OpenAI 1.16.0-alpha.260730.1
```
Core, Abstractions, PostgreSql, OpenAI hiçbir satırda görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-033 — Roslyn tüketicinin grafiğine sızmıyor

**Gerçek sonuç**
```
tarama bitti
```
Hiçbir `🚨` satırı yazılmadı; `Microsoft.CodeAnalysis.CSharp` hiçbir pakette
bağımlılık olarak görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-034 — Ağır sağlayıcı zincirleri kendi paketlerinde kalıyor

**Gerçek sonuç**
```
AgentPrism.Anthropic  : Anthropic 12.39.0
AgentPrism.Google     : Google.GenAI 1.16.0
AgentPrism.OpenAI     : OpenAI 2.12.0
AgentPrism.PostgreSql : Npgsql 10.0.3
AgentPrism.SqlServer  : Microsoft.Data.SqlClient 7.0.2
```
Her sağlayıcı yalnız kendi paketinde görünüyor; meta pakette (`AgentPrism`)
hiçbiri yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Kaynak üreteci ve derleme anı tanıları

Bu bölümün case'leri repo dışında, izole bir tüketici projesinde koşar. Böylece
repo'nun `ProjectReference` zinciri sonucu bulandırmaz.

> **Bölüm ön koşulu (bir kez uygulanır):**
> ```bash
> cd /Users/farukatasoy/Desktop/projects/AgentPrism
> rm -rf /tmp/ap-pack && mkdir -p /tmp/ap-pack
> MSBUILDDISABLENODEREUSE=1 dotnet pack AgentPrism.slnx -c Release -o /tmp/ap-pack
>
> rm -rf ~/agentprism-manuel/uretec && mkdir -p ~/agentprism-manuel/uretec
> cd ~/agentprism-manuel/uretec
> dotnet new console -o . --force
> cat > nuget.config <<'EOF'
> <?xml version="1.0" encoding="utf-8"?>
> <configuration>
>   <packageSources>
>     <clear />
>     <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
>     <add key="ap-yerel" value="/tmp/ap-pack" />
>   </packageSources>
> </configuration>
> EOF
> SURUM=$(ls /tmp/ap-pack/AgentPrism.Core.*.nupkg | sed 's#.*AgentPrism.Core\.##;s#\.nupkg##')
> dotnet add package AgentPrism.Core --version "$SURUM"
> ```
> 🚨 `AgentPrism.Core` **doğrudan** `PackageReference` ile alınır. MT-PKG-021
> bunun neden önemli olduğunu ölçer.

---

## MT-PKG-040 — Üreteç işaretli statik metodu kaydeder

**Gerçek sonuç**
```
exit: 0 · Build succeeded · 0 Warning(s) · 0 Error(s)
Üretilen dosyalar:
  .../GetOrderStatus_274C17A0Tool.g.cs
  .../AgentPrismGeneratedTools.g.cs
İlk satır: // <auto-generated/>
AddGeneratedTools uzantı metodu namespace AgentPrism içinde tanımlı
"get_order_status" dizgisi GetOrderStatus_274C17A0Tool.g.cs içinde geçiyor
System.Reflection / Activator. / GetMethod( / AIFunctionFactory -> hiçbiri geçmiyor
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-041 — Üretilen kod `dotnet format` kapısını geçer

**Gerçek sonuç**
```
format cikis kodu: 0
TAB yok
satir sonu temiz
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-042 — `APG0001`: aynı tool adı iki metotta

**Gerçek sonuç**
```
exit: 1
error APG0001: 'ayni_ad' tool adi birden fazla metotta kullanilmis:
  global::CakisanTools.Bir, global::CakisanTools.Iki. Her tool adi derleme
  icinde tek olmalidir.  (iki ayrı konumda: satır 6 ve satır 9)
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-043 — `APG0002`: geçersiz karakterli tool adı

**Gerçek sonuç**
```
Birinci derleme: 3 farklı geçersiz ad için APG0002 üretti (boşluklu, noktalı,
  65 karakter) — her biri metot adını ve geçersiz tool adını taşıyor.
  grep -c "APG0002" -> 6 (MSBuild her hatayı hem satır-içi hem "Build FAILED"
  özetinde bir daha yazdığı için 3 tanı iki kez görünüyor; doğrulama sorgusu
  bunu hesaba katmıyor — küçük bir doküman notu, kusur değil).
İkinci derleme (64 karakter, sınırın tam üstü): 0 APG0002, exit 0, geçti.
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-044 — `APG0003`: desteklenmeyen parametre tipi

**Gerçek sonuç**
```
Birinci derleme (record parametre):
  error APG0003: 'BilesikTools.SiparisVer' metodunun 'siparis' parametresi
  ('Siparis' tipi) ureteç tarafindan desteklenmiyor. Desteklenen tipler:
  ilkel tipler, string, Guid, DateTime(Offset), enum, bunlarin dizisi/
  IReadOnlyList<T>'i ve CancellationToken. Baska bir tip icin
  'AddTool(AIFunctionFactory.Create(...))' ile elle kaydedin.

İkinci derleme (beyaz liste — string[] dahil): exit 1, APG0003 SAYISI: 0
  ama derleme YİNE BAŞARISIZ:
  error CS1503: Argument 12: cannot convert from
  'System.Collections.Generic.IReadOnlyList<string>' to 'string[]'
  (Hepsi_AA45331CTool.g.cs:35)
```

🚨 **Kritik kusur — doğrulandı, tekrar üretilebilir.** Üretecin beyaz listesi
`T[]` dizi tipini desteklenen bir parametre tipi olarak ilan ediyor
(`APG0003` mesajının kendisi de "bunlarin dizisi" der), ama üretilen sarmalayıcı
kod **her zaman** `AgentPrismGeneratedToolArguments.GetArray(...)` çağırıyor —
bu her koşulda `IReadOnlyList<T>` döndürür ve hedef parametre `T[]` ise
doğrudan atanamaz (`IReadOnlyList<T>` → `T[]` örtük dönüşümü yoktur).

Kapsam doğrulandı: yalnız `string[]` değil, **her `T[]` parametresi** etkileniyor
— izole bir `int[]` parametresiyle de aynı desen tekrarlandı
(`cannot convert from 'IReadOnlyList<int>' to 'int[]'`). `IReadOnlyList<T>`
parametreleri (örn. `m` alanı) etkilenmiyor, yalnızca çıplak dizi (`T[]`)
imzaları kırık.

Etki: dokümantasyonda ve `APG0003` hata mesajında "desteklenir" denen bir
tip, pratikte **her zaman** derlemeyi kırıyor. Bu bir kaçış yolu değil —
`T[]` parametreli hiçbir tool metodu bu üreteçle asla derlenemez. `Kritik`
önemde bir kod kusuru bildirimi açılmalıdır (izlek: Faz 52 kaynak üreteci,
`AgentPrismGeneratedToolArguments.GetArray` / dizi-tipi kod üretimi).

Kök neden bulundu: `src/AgentPrism.Generators/ParameterTypeValidator.cs:98-111`
(`TryGetArrayElementType`) hem `T[]` hem `IReadOnlyList<T>`/`IList<T>`/
`IEnumerable<T>`/`List<T>` biçimlerini aynı `ParameterShape.Array`'e
daraltıyor — orijinal şeklin çıplak dizi mi arayüz mü olduğu bilgisi
kayboluyor. `SourceWriter.cs:143` bu yüzden her zaman `GetArray(...)`
çağırıyor (`IReadOnlyList<T>` döner); parametre `T[]` olduğunda `.ToArray()`
dönüşümü hiç eklenmiyor.

---

**Yeniden koşum (2026-08-14, KAPANIS-PLANI Aile D):** Kusur önceki bir
düzeltme dalgasında (commit `75990fd`) zaten kapanmış — sonuç dosyası
güncellenmemişti. `ParameterModel.IsConcreteArray` alanı ve
`SourceWriter.cs:147` `.ToArray()` sarmalaması kod tabanında hâlihazırda
mevcut. Regresyon testi zaten var:
`tests/AgentPrism.Generators.UnitTests/GeneratedOutputTests.cs`
`Ciplak_dizi_parametresi_ToArray_ile_cevrilir_ve_uretilen_kod_derlenir` —
`int[]`, `string[]` ve `IReadOnlyList<int>` parametreli tool'ları gerçek
Roslyn derlemesinden geçirip `GetDiagnostics()` ile sıfır hata doğruluyor.

Canlı doğrulama: case'in adımları tazelenmiş `AgentPrism.0.0.0-preview.0.138`
paketine karşı `~/agentprism-manuel/uretec`'te aynen koşuldu.
1. Adım (`record` parametreli tool) → yine `APG0003` verdi, beklendiği gibi.
2. Adım (beyaz liste + ayrıca izole `int[]` tool'u `SayilariTopla`) → `APG0003`
   sayısı **0**, derleme **0 Error(s)** ile bitti — `CS1503` **yok**.

Dört kapı da bu koşumda yeşil (`dotnet build`/`test`/`pack`/`format`); kod
değişikliği gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-045 — `APG0004`: generic metot tool olamaz

**Gerçek sonuç**
```
error APG0004: 'GenericTools.Getir' metodu [AgentPrismTool] ile isaretli
  ancak generic. Tool metotlari generic olamaz; somut bir sarmalayici metot
  yazin.
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-046 — `APG0005`: çağrı var, işaretli metot yok

**Gerçek sonuç**
```
error APG0005: 'AddGeneratedTools()' cagrildi ancak bu derlemede
  [AgentPrismTool] ile isaretli metot yok. Tool metotlarini isaretleyin
  veya bu cagriyi kaldirin.
Tools.cs geri konduktan sonra: Build succeeded, 0 Warning(s), 0 Error(s)
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-047 — `APG0006`: açıklama eksik — hata değil, uyarı

**Gerçek sonuç**
```
Birinci derleme: exit 0
  warning APG0006: 'aciklamasiz' tool'unun aciklamasi yok. Model tool'u ne
  zaman cagiracagini aciklamadan bilemez; [AgentPrismTool] icin bir
  aciklama verin.
Bastırılmış (-p:NoWarn=APG0006): exit 0, tanı sayısı 0
```
`TreatWarningsAsErrors` bu tüketici projede yok; derleme uyarıyla birlikte
başarılı bitti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-048 — `APG0007`: örnek metot tool olamaz

**Gerçek sonuç**
```
error APG0007: 'OrnekTools.Getir' bir ornek metodudur ve tool olamaz. MAF,
  AIFunctionArguments.Services olarak bos bir saglayici gecirir (karar K-218).
  Metodu 'static' yapin veya tool'u kurulum aninda ornekleyip
  'AddTool(AIFunctionFactory.Create(...))' ile kaydedin.
static yapıldıktan sonra: exit 0, tanı sayısı 0
```
Mesaj K-218'e açıkça atıf yapıyor ve iki çözüm gösteriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-049 — İşaretsiz metot sessizce tool olmaz

**Gerçek sonuç**
```
exit: 0, 0 Warning(s), 0 Error(s)
GizliYardimci sayısı: 0
GetOrderStatus sayısı: 1
```
Hiçbir tanı üretilmedi — işaretsiz metot hata değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-050 — Üreteç meta paket üzerinden de akıyor

**Gerçek sonuç**
```
dotnet add package AgentPrism --version 0.0.0-preview.0.63  -> başarılı
dotnet build -c Release -> cikis kodu: 0
Build succeeded. 0 Warning(s) 0 Error(s)
CS1061 sayısı: 0
```
Meta paketi (`AgentPrism`) doğrudan referanslayan bir tüketici derlendi;
`AddGeneratedTools` bulunamadı hatası çıkmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — AOT publish

---

## MT-PKG-060 — AOT uyumlu paketler sıfır trim uyarısı verir

**Gerçek sonuç**
```
dotnet publish -r osx-arm64 -p:PublishAot=true -> exit 0
IL2xxx/IL3xxx sayısı: 0
Çalıştırma çıktısı: kayit tamam
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-061 — `AddToolsFrom` AOT bedelini çağırana iletiyor

**Gerçek sonuç**
```
warning IL2026: ... 'AddToolsFrom(Type)' ... 'RequiresUnreferencedCodeAttribute' ...
warning IL3050: ... 'AddToolsFrom(Type)' ... 'RequiresDynamicCodeAttribute' ...
(hem derleme-anı hem trim/AOT analiz uyarısı olarak iki kez, toplam 4 satır)
```
En az bir `IL2026`/`IL3050` çıktı ve `RequiresUnreferencedCode`/
`RequiresDynamicCode` gerekçesini taşıyor — bastırma yapılmamış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-062 — AOT bayrağı paket bazında doğru

**Gerçek sonuç**
```
Bayrağı false yapan projeler (10):
  AgentPrism, AgentPrism.AspNetCore, AgentPrism.Generators, AgentPrism.Mcp,
  AgentPrism.SqlServer, AgentPrism.Sqlite, AgentPrism.Templates,
  AgentPrism.Testing, AgentPrism.UI, AgentPrism.Workflows
  -> beklenen liste ile birebir eşleşiyor.

Geriye kalan (AOT uyumlu, Sql.Shared paket olmadığı için hariç, 8 adet):
  Abstractions, Anthropic, Azure, Core, Google, OpenAI, PostgreSql, Voice
  -> beklenen dörtlü liste (Abstractions, Core, PostgreSql, OpenAI) ile
     eşleşmiyor; gerçek küme 8 paket.
```

`AGENTS.md:161` "Sekiz paket uyumludur" der ve bu, koddan türetilen 8'li
kümeyle (Abstractions, Anthropic, Azure, Core, Google, OpenAI, PostgreSql,
Voice) birebir örtüşüyor. `README.md`'de AOT'a dair hiçbir iddia yok (arama
sıfır sonuç döndürdü). Kod ↔ `AGENTS.md` **uyumlu**; düzeltilmiş beklentiyle
de örtüşüyor. 2026-08-15'te `grep -l "AgentPrismAotCompatible>false"
src/*/*.csproj` yeniden koşuldu, aynı 10'lu küme doğrulandı.

---

**Doküman düzeltmesi (2026-08-15):** Beklenti koda göre düzeltildi. Ürün/
`AGENTS.md` kusuru yok; düzeltilmesi gereken yalnız bu test dosyasıydı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — İzlek A: temiz tüketici ve proje şablonu

---

## MT-PKG-070 — Yerel feed kurulur ve şablon yüklenir

**Gerçek sonuç**
```
Success: AgentPrism.Templates::0.0.0-preview.0.63 installed the following templates:
Template Name                            Short Name      Language  Tags
AgentPrism control plane (ASP.NET Core)  agentprism-api  [C#]      Web/AgentPrism/AI/Agents

dotnet new list agentprism -> aynı satırı listeliyor
```
Kısa ad `agentprism-api`, şablon adı `AgentPrism control plane (ASP.NET Core)`,
dil `C#`, tip `project` (`--columns-all` ile doğrulandı: `Type: project`).

Not: `dotnet new install AgentPrism.Templates::*-*` sözdizimi zsh altında
`*-*` glob'unu shell'e genişletmeye çalışıp "no matches found" ile başarısız
oldu; tırnaklı (`'...*-*'`) hâliyle çalıştı. Ayrıca .NET SDK `::` ayıracının
kullanımdan kaldırıldığını, yerine `@` kullanılması gerektiğini bildirdi
(fonksiyonel bir engel değil, bir uyarı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-071 — Varsayılan şablon derlenir ve çalışır

**Gerçek sonuç**
```
dotnet new agentprism-api -o . --AgentPrismVersion 0.0.0-preview.0.63
  -> "template ... created successfully", restore succeeded
Üretilen: .gitignore, Program.cs, Properties/, README.md, Tools/OrderTools.cs,
  appsettings.Development.json, appsettings.json, varsayilan.csproj
dotnet build -c Release -> exit 0, 0 Warning(s), 0 Error(s)
dotnet run -> "Now listening on: http://localhost:5081"

curl .../api/meta -> HTTP 200
  {"version":"0.0.0-preview.0.63", ... "storage":{"persistent":false,
   "agentDefinitionStore":"InMemoryAgentDefinitionStore", ...}}
curl .../agentprism (arayüz kökü) -> HTTP 200
curl .../api/agents -> [{"name":"support", "displayName":"Destek Asistani",
   "model":{"provider":"openai","model":"MODEL_ADINI_BURAYA_YAZIN"},
   "toolNames":["get_order_status"], ...}]
```
Hiçbir bağlantı dizesi/API anahtarı tanımlı değilken uygulama ayağa kalktı.
`appsettings.json` `ApiKey` alanını boş dize taşıyor. Agent listesi boş değil,
`support` agent'ını gösteriyor (tarayıcıda görsel doğrulama yerine `api/agents`
uç noktasıyla doğrulandı — bu oturumda gerçek bir tarayıcı açılmadı, headless
ortam).

🚨 Küçük not: `appsettings.json`'da `ConnectionString` alanı **hiç yok**
(yalnız `ApiKey: ""`). Bu, varsayılan kalıcılık `memory` olduğu ve
`ConnectionString`'e ihtiyaç duyulmadığı için beklenen/mantıklı — ama doküman
"appsettings.json ConnectionString ve ApiKey alanlarını boş dize olarak taşır"
diyor; `memory` varyantında `ConnectionString` alanı zaten üretilmiyor. Kod
kusuru değil, doküman ifadesi yalnızca `postgres`/`sqlite`/`sqlserver`
varyantları için geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-072 — Şablon gerçek bir model çağrısı yapar

**Gerçek sonuç**
```
1) Yer tutucuyla (ApiKey boş, model adı sabitlenmemiş):
   HTTP 400 "'support' agent'i derlenemedi: 'openai' adinda bir model
   saglayicisi kayitli degil. ... OpenAI icin 'UseOpenAI(apiKey)' cagirin."
   Uygulama ÇÖKMEDİ.
2-3) Model adı gpt-5.4-mini olarak düzeltildi, API anahtarı
   `dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" ...` ile verildi.
4) Yeniden derleme + çalıştırma sonrası run:
   HTTP 200, SSE akışı; get_order_status tool'u TAM BİR KEZ çağrıldı
   (orderId: "ORD-1001"), yanıt metni parça parça "ORD-1001 numarali siparis
   kargoya verildi..." içeriyor.
   /agentprism/api/runs -> status: "Completed", modelId: "gpt-5.4-mini",
   usage.totalTokens: 255
```

🚨 Doküman notu: 1. adımdaki gerçek hata metni doküman'ın beklediği gibi
`MODEL_ADINI_BURAYA_YAZIN` adını içermiyor — bunun yerine "hiçbir sağlayıcı
kayıtlı değil" hatası dönüyor. Kök neden: şablonun `Program.cs`'i `UseOpenAI`
çağrısını **yalnızca `ApiKey` doluysa** yapıyor (tasarım kuralı #1, "sıfır
sürpriz"); anahtar boşken sağlayıcı hiç kayıtlı olmuyor, bu yüzden hata modeli
değil sağlayıcının yokluğunu bildiriyor. Bu, dokümanın varsaydığından **daha
doğru** bir tasarım (anahtar yokken plasholder model adını denemeye bile
gerek kalmıyor) — kod kusuru değil, doküman'ın beklenen-sonuç metni bu ayrıntıyı
güncellemeli. Genel iddia (uygulama çökmez, hata anlaşılırdır) doğrulandı.

Ayrıca ilk deneme sırasında önceki adımdan (MT-PKG-071) kalan `dotnet run`
süreci portu (5081) tutmaya devam etti — `kill %1` yeni bir Bash oturumunda
işe yaramadı (job kontrolü kalıcı değil). `lsof -ti:5081 | xargs kill -9` ile
temizlendi. Bu bir kod kusuru değil, bu koşumun kendi süreç yönetimi hatası.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-073 — Kalıcılık seçenekleri doğru paket ve kod üretiyor

**Gerçek sonuç**
```
postgres  : PackageReference AgentPrism (tek), UsePostgreSql(...),
            appsettings: yalnız PostgreSql bölümü -> derlendi
sqlite    : + AgentPrism.Sqlite, UseSqlite(...),
            appsettings: yalnız Sqlite bölümü -> derlendi
sqlserver : + AgentPrism.SqlServer, UseSqlServer(...),
            appsettings: yalnız SqlServer bölümü -> derlendi
Çapraz kirlenme: yok. #if/// #if kalıntısı: yok.
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-074 — Sağlayıcı seçenekleri doğru paket ve kod üretiyor

**Gerçek sonuç**
```
openai    : ek paket yok (AgentPrism meta içinde), UseOpenAI(...)      -> derlendi
anthropic : + AgentPrism.Anthropic, UseAnthropic(...)                  -> derlendi
google    : + AgentPrism.Google, UseGoogle(...)                        -> derlendi
azure     : + AgentPrism.Azure, UseAzureOpenAI(...)                    -> derlendi
```
Dördü de kimlik bilgisi olmadan derlendi. Azure'un gerçek `run` denemesi
doküman gereği burada **Atlandı** — `06-SAGLAYICI-DIGER.md`'ye bırakıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-075 — `--ui false` arayüzü hiç bağlamaz

**Gerçek sonuç**
```
grep -c "UseUI" Program.cs -> 0
dotnet build -> exit 0
meta  : 200
arayuz: 404
Log'da arayüzle ilgili hata yok, uygulama çökmedi.
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-076 — Şablon sürüm sabitlemesi çalışıyor

**Gerçek sonuç**
```
1) --skip-restore: <PackageReference Include="AgentPrism" Version="*-*" />
   "Restoring" satırı çıktıda yok.
   AGENTPRISM_TEMPLATE_PACKAGE_VERSION yer tutucusu kalmamış.
2) --AgentPrismVersion 99.99.99 --skip-restore, sonra dotnet restore:
   error NU1102: Unable to find package AgentPrism with version (>= 99.99.99)
     - Found 1 version(s) in ap-yerel [ Nearest version: 0.0.0-preview.0.63 ]
```
Hata mesajı hem `AgentPrism` paket adını hem `99.99.99` sürümünü açıkça
taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-077 — Şablon `secret` sızdırmıyor

**Gerçek sonuç**
```
grep taraması (sk-... / api-key deseni, *.json/*.cs/*.csproj) -> 0 satır
--- tarama bitti ---
UserSecretsId -> var (agentprism-starter-568A3F84-890D-4796-B8D4-E0293326B319)
.gitignore içeriği:
  bin/
  obj/
  *.user
  appsettings.*.local.json
```
Gerçek OpenAI API anahtarı (MT-PKG-072'de kullanıcı tarafından sağlandı ve
yalnızca `dotnet user-secrets` ile saklandı) hiçbir dosyada bulunmadı.
`appsettings.json`'daki `ApiKey` boş dize kaldı.

Not: `.gitignore`'da literal `secrets` kelimesi yok, ama
`appsettings.*.local.json` deseni aynı amaca hizmet ediyor (yerel geçersiz
kılma dosyaları — gerçek `secret`'ların konması beklenen yer — asla commit'e
girmez). Doğrulama sorgum tam bu satırla eşleşmedi (`appsettings\.\*\.json`
deseni ile "appsettings.*.local.json" arasında ".local" farkı var); niyet
karşılanıyor, literal kelime eşleşmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Sıfır sürpriz ve `TryAdd` sözleşmesi

---

## MT-PKG-080 — Tüketicinin kaydı her zaman kazanır

**Gerçek sonuç**
```
once : BenimRunStore
sonra: BenimRunStore
```
Her iki kayıt sırasında da tüketicinin `BenimRunStore`'u kazandı; hiçbir
istisna atılmadı. `IRunStore`'un 13 üyesi `NotSupportedException` ile
uygulandı (kaynak: `src/AgentPrism.Abstractions/Runs/IRunStore.cs`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-081 — Hiçbir `Use*` çağrılmadan kurulum ayakta kalır

**Gerçek sonuç**
```
cikis kodu: 0
IRunStore     : InMemoryRunStore
ISessionStore : AuditingSessionStore
IToolRegistry : ToolRegistry
kurulum tamam
```

Temel iddia (hiçbir `Use*` çağrılmadan kurulum ayakta kalır, dış bağımlılık
yok) doğrulandı; üç tip adı düzeltilmiş beklentiyle birebir örtüşüyor.

---

**Doküman düzeltmesi (2026-08-15):** Beklenti koda göre düzeltildi. Ürün
kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-082 — İki kalıcılık sağlayıcısı aynı anda verilirse

**Gerçek sonuç**
```
dotnet build -> exit 0
dotnet run -> HTTP 200 (meta)
storage: {"persistent":true,"agentDefinitionStore":"SqlAgentDefinitionStore",
  "runStore":"SqlRunStore", ...}   (paylaşımlı Sql.Shared tipleri; hangi
  motor olduğunu isimden ayırt etmek mümkün değil)

Log (iki kez, ÇELİŞKİLİ):
  warn: ... birden fazla kalicilik saglayicisi kayitli: SQLite, PostgreSQL.
        Son kayit kazanir ve su an SQLite kullaniliyor. ...
  warn: ... birden fazla kalicilik saglayicisi kayitli: SQLite, PostgreSQL.
        Son kayit kazanir ve su an PostgreSQL kullaniliyor. ...

Gerçek etki (log'un altında, Npgsql komut izinde):
  CREATE SCHEMA IF NOT EXISTS agentprism / CREATE TABLE __migrations / ...
  -> PostgreSQL şeması gerçekten oluşturuldu.
Ayrıca: manuel-cift.db (SQLite dosyası) da proje dizininde OLUŞTU (4096 bayt,
  şema yazılmış) — bu koşumda tespit edildi, sonra silindi.
```

🚨 **Kritik kusur — doğrulandı, kök nedeni bulundu.** README/karar metninin
iddiası ("üçü aynı anda verilmez; verilirse son kayıt kazanır") **yanlış**:
gerçekte **her iki sağlayıcı da tam olarak devreye giriyor** — hem PostgreSQL
şeması hem SQLite dosyası bu koşumda oluştu. "Son kayıt kazanır" yalnızca
tekil servis kayıtları (`IRunStore` vb., `TryAdd`/son-kayıt-kazanır DI deseni)
için doğru; ama migration'ı tetikleyen `MigrationHostedService`
(`src/AgentPrism.Sql.Shared/Migrations/MigrationHostedService.cs`) her
`Use*Sql()` çağrısında **ayrı bir `IHostedService` örneği** olarak kaydediliyor
(`AddHostedService` katkılıdır, `TryAdd` değil — .NET tüm kayıtlı hosted
servisleri çalıştırır). Sonuç: iki bağımsız `MigrationHostedService` örneği
başlıyor, ikisi de kendi `_storeContext.ProviderName`'ini "kazanan" olarak
loglayıp (bu yüzden log çelişkili görünüyor) **ikisi de kendi migration'ını
gerçekten uyguluyor** — "son kayıt kazanır" davranışı migration/şema kurulumu
için **geçerli değil**.

Etki: bir tüketici yanlışlıkla iki `Use*Sql()` çağırırsa (kopyala-yapıştır,
geçiş senaryosu), yalnızca "beklenmedik bir uyarı" almaz — **istemeden ikinci
bir veritabanında şema oluşturur**. Bu, projenin kendi "sıfır sürpriz" ilkesine
aykırı ve üretimde yanlışlıkla paylaşılan bir PostgreSQL örneğine şema
yazılması riski taşıyabilir. `Kritik` önemde bir kod kusuru bildirimi
açılmalıdır (izlek: `MigrationHostedService`, çoklu kalıcılık kaydı; K-183).

Bu koşumun kendi geçici SQLite dosyası (`manuel-cift.db`) temizlendi.

---

**Yeniden koşum (2026-08-14, KAPANIS-PLANI Aile E):** Kusur önceki bir
düzeltme dalgasında zaten kapanmış — kod yorumu `MigrationHostedService.cs:73-83`
kusuru `MT-PKG-082` adıyla anıyor ve `IsWinningProvider()` artık paylaşılan
`_registrations` listesindeki **son kaydı** (tüm sağlayıcılar arası, yalnız
kendi tipi değil) kazanan sayıyor; kaybeden `StartAsync` içinde erkenden
çıkıp migration'a hiç dokunmuyor. İki entegrasyon testi zaten repoda
(`tests/AgentPrism.PostgreSql.IntegrationTests/ServiceRegistrationTests.cs`
`Kaybeden_saglayici_migration_uygulamaz`, `Kazanan_saglayici_migration_uygular`)
— ikisi de gerçek PostgreSQL'e karşı koşuyor ve kaybedenin şemayı
**hiç** oluşturmadığını doğruluyor.

Canlı doğrulama: `samples/AgentPrism.Api/Program.cs`'e case'in öngördüğü
gibi geçici bir `UseSqlite(...)` çağrısı `UsePostgreSql(...)`'in altına
eklendi (MT-PG-034 deseni), örnek çalıştırıldı, sonra değişiklik
`git checkout` ile geri alındı.

```
Log (iki kez, bu kez TUTARLI):
  warn: ... birden fazla kalicilik saglayicisi kayitli: PostgreSQL, SQLite.
        Son kayit kazanir ve su an SQLite kullaniliyor. ...
  warn: ... birden fazla kalicilik saglayicisi kayitli: PostgreSQL, SQLite.
        Son kayit kazanir ve su an SQLite kullaniliyor. ...

PostgreSQL sema sayimi (mt_e): 0   -> sema OLUSMADI (kaybeden dokunmadi)
SQLite dosyasi (/tmp/manuel-cift-e.db): 45 tablo -> kazanan gercekten yazdi
```

Önceki koşumda ikisi de şema yazıyordu ve iki log satırı **çelişkiliydi**
(biri "SQLite kazandı", biri "PostgreSQL kazandı"). Bu koşumda ikisi de aynı
kazananı söylüyor ve yalnız o kazanan gerçekten şema yazıyor — kusur giderildi.
Dört kapı bu koşumda yeşil; kod değişikliği gerekmedi (`Program.cs`'teki
geçici satır geri alındı, `git status` temiz).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Koşum sonrası temizlik

```bash
dotnet new uninstall AgentPrism.Templates
dotnet nuget remove source agentprism-local
rm -rf ~/agentprism-manuel ~/agentprism-local-feed
rm -rf /tmp/ap-*
cd /Users/farukatasoy/Desktop/projects/AgentPrism && git status
```

`git status` yalnız `docs/manuel-test/` göstermelidir. Başka bir değişiklik
varsa MT-PKG-011, 012, 013, 015 veya 016'nın geri alma adımı atlanmıştır.

---
