# 01 — Kurulum ve Paketleme (`PKG`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../01-KURULUM-VE-PAKETLEME.md`](../../01-KURULUM-VE-PAKETLEME.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk · worktree `6979fd60` |
| **Case sayısı** | 81 (MT-PKG-001..122) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): Senaryolarda geçen her
`dotnet user-secrets set/remove` adımı şeridin kendi **ortam değişkenine**
çevrilir. Depo makine genelinde tektir ve şeritler onu paylaşır. Okumak
serbesttir.

---

## Devir notu

**Oturum 1 · BİTTİ.** `MT-PKG-001..049` — **33 case: 32 ☑ Geçti · 1 ☑ Kaldı.**
Açık ya da işaretsiz case yok. Bloklar 000-009 · 010-019 · 020-029 · 030-039 ·
040-049 kapandı; blok ortasında durulmadı.

- **Nerede kalındı:** blok 040-049'un sonunda. Sıradaki case `MT-PKG-050`.
- **Sonraki oturum neyle başlamalı:** oturum 2 = `MT-PKG-050..099` (25 case).
  Blok 050-059 (1) · 060-069 AOT (3) · 070-079 İzlek A (8) · 080-089 (3) ·
  090-099 public API kapısı (10).
- **Bozuk ön koşul:** yok. Şu ikisi **hazır durumda bırakıldı**, oturum 2
  bunları yeniden kurmak zorunda değil:
  - `<scratch>/ap-pack` — 20 `.nupkg` + 18 `.snupkg` (`0.0.0-preview.0.789`)
  - `~/tracon-manuel/uretec` — `Tracon.Core` paketine **doğrudan**
    `PackageReference` ile bağlı konsol projesi; `Tools.cs` ve `Program.cs`
    özgün hâlinde. `MT-PKG-060`, `061`, `070`+ bunu kullanır.
- **Kod donuk kaldı:** `git diff 7e3a4de7..HEAD -- src samples tests` boş.
  Case'lerin geçici kaynak değişiklikleri (`MT-PKG-011`, `012`, `013`, `015`,
  `016`) aynı oturumda geri alındı; her birinin sonunda `git status --short`
  boş doğrulandı.

**Bulgular:** `HATA-S1-001` (Yüksek) · `HATA-S1-002` (Orta) · `HATA-S1-003`
(Düşük, doküman) — üçü de `MT-PKG-010`'dan. Hiçbiri sonraki case'leri
bloklamadı.

**Spec'e dokunulan yerler (skill §1.1 istisnası, hepsi doküman kusuru):**

| Case | Ne düzeltildi |
|---|---|
| — | Üreteç bölümünün **ön koşulu geri getirildi** (`946a37fb`'de kaybolmuştu) |
| MT-PKG-015 | Türkçe hata metni alıntısı → bugünkü İngilizce metin |
| MT-PKG-020 | 17/16 paket → 20/18; 18 proje → 22; sembolsüz paket 1 → 2 |
| MT-PKG-022 | `Tracon.Testing` tek TFM → üç TFM (K-780) |
| MT-PKG-040 | "sıfır uyarı" → `TRC0009` beklenir; tek dosya → iki dosya |
| MT-PKG-041 | `grep -P` (macOS'ta yok) → taşınabilir denetim |
| MT-PKG-049 | "sıfır uyarı" → `TRC0009`; sayım tüm üretilen dosyalara |

**Spec'e DOKUNULMAYAN, kapanışa bırakılan gevşeklikler:** `MT-PKG-021` DLL
boyutu (~53 KB → 110 KB) · `MT-PKG-027` "hiç etiket yok" ve "commit sayısı" ·
`MT-PKG-043` `grep -c` 3 yerine 6 sayıyor · `MT-PKG-044` "record/class
kapsanmaz" gerekçesi (artık kapsanıyor) · `MT-PKG-016` TRC0001/TRC0003 dil
tutarsızlığı · `MT-PKG-020` ↔ `098` ↔ `105` paket sayısı çelişkisi.

🚨 **Bölme noktası kuralı bu dosyada uygulanamıyor.** `DEVIR.md` §6 "bölme
noktası dosyanın kendi `#` bölüm başlığıdır" der; spec'te **tek** bölüm
başlığı var (`# 1 — Ön koşullar`, satır 52) ve 81 case'in tamamı onun
altında. Ölçüldü: bölüm başlıkları 2..8 `946a37fb` ("faz 58",
spec/kayıt ayrımı) ile spec'ten düştü, koşum kaydında kaldı. Ayrıntı ve
kullanılan sınır aşağıda — `NOT-01`. **Kullanıcı kararı (2026-09-16):**
onluk blok sınırı bölme noktasıdır; spec'e dokunulmaz.

---

## NOT-01 — Bölüm başlıkları spec'te yok, blok sınırı kullanıldı

Bu bir **koşum kusuru değil, doküman kusurudur**; ürün kodunu ilgilendirmez.

Faz 58 öncesi spec sekiz bölümdü:

| Bölüm | Case aralığı |
|---|---|
| 1 — Ön koşullar | 001..003 |
| 2 — Derleme kapıları | 010..016 |
| 3 — Paket çıktısı ve kalite denetimi | 020..027 |
| 4 — Bağımlılık grafiği | 030..034 |
| 5 — Kaynak üreteci ve derleme anı tanıları | 040..050 |
| 6 — AOT publish | 060..062 |
| 7 — İzlek A: temiz tüketici ve proje şablonu | 070..077 |
| 8 — Sıfır sürpriz ve `TryAdd` sözleşmesi | 080..082 |

Kanıt: `git show 946a37fb~1:docs/manuel-test/01-KURULUM-VE-PAKETLEME.md | grep -n "^# [0-9]\+ — "`

Faz 58'den sonra eklenen 33 case (090..122) aynı onluk blok düzenini sürdürür.
Bu oturum **onluk blok sınırını** bölme noktası saydı; hiçbir blok ortasında
durmadı.

---

## MT-PKG-001 — SDK sürümü ve roll-forward politikası

**Gerçek sonuç**
```
dotnet --list-sdks -> 9.0.305 · 9.0.306 · 10.0.100
dotnet --version   -> 10.0.100
global.json        -> version 10.0.100 · rollForward latestFeature · allowPrerelease false
```
`10.0.1xx` bandında, önizleme eki yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-002 — Node.js ve npm arayüz derlemesi için yeterli

**Gerçek sonuç**
```
node --version -> v22.23.2   (esik 20.19)
npm  --version -> 10.9.8     (cikis kodu 0)
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-003 — Docker hazır ve `mssql/server` imajı arm64'te çekilebiliyor

**Gerçek sonuç**
```
docker info -> 29.8.0 · 10 CPU · 8.319.504.384 bayt
uname -m    -> arm64
docker pull mcr.microsoft.com/mssql/server:2022-latest
  -> Status: Downloaded newer image
     Digest sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090
     cikis kodu 0
```

⚠️ **Bellek eşiği iki türlü okunuyor, ölçüm kayda geçti.** 8.319.504.384 bayt
ondalık okumada **8,32 GB** (eşiği geçer), ikili okumada **7,75 GiB** (eşiğin
hemen altında). Docker Desktop 8 GiB ayarından kendi payını düştüğü için bu
değer tipiktir. Kullanıcı kararı (2026-09-16): ondalık okuma esas alınır, case
geçer, kusur kaydı açılmaz. Rosetta sapması gerekmedi — imaj doğrudan çekildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-010 — Dört kapı sıfır uyarı verir

**Gerçek sonuç**

Şerit `ap-s1`, worktree `/Users/farukatasoy/Desktop/projects/ap-s1`.

```
1) dotnet build  Tracon.slnx -c Release              -> cikis 0 · 62 sn
   Build succeeded. 0 Warning(s) · 0 Error(s)
2) dotnet test   Tracon.slnx -c Release --no-build   -> cikis 1 · 318 sn (1. kosum)
                                                        cikis 1 · 382 sn (2. kosum)
3) dotnet pack   Tracon.slnx -c Release --no-build   -> cikis 0 ·  7 sn
   MSBUILDDISABLENODEREUSE=1 ile kosuldu; 19 .nupkg + 18 .snupkg uretildi
   (surum 0.0.0-preview.0.789)
4) dotnet format Tracon.slnx --verify-no-changes --no-restore -> cikis 0 · 85 sn
   Hicbir dosya degisikligi bildirilmedi
```

🚨 **Kapı kırmızı: `dotnet test` çıkış kodu 1.** İlk koşumun çıktısı
`tail -40` ile kesildiği için düşen proje görünmedi; koşum tam çıktıyla
**yeniden** koşuldu ve iki koşum da `1` verdi.

2. koşumun proje bazında sayımı (22 proje · **7699 test**):

| Proje | total | failed |
|---|---|---|
| `Tracon.Sqlite.IntegrationTests` | 864 | **40** |
| `Tracon.AspNetCore.FunctionalTests` | 1077 | **1** |
| diğer 20 proje | 5758 | 0 |

41 düşüşün **39'u** `[Test Class Cleanup Failure]` kaskadıdır; bağımsız kök
neden **iki** tanedir → `HATA-S1-001`, `HATA-S1-002`.

**Süre:** Beklenen sonuç `dotnet test` için 2,5 dakika eşiği koyuyor ve
aşılırsa asılı alt süreç aranmasını istiyor. Arandı: koşum sonunda
`ps aux | grep MSBuild` **16 düğüm** gösterdi (`nodeReuse:true`, build'in
kendi düğümleri); bunlar kendiliğinden sonlandı ve dinlenme hâlinde
**0** düğüm kaldı. Yani asılı süreç **yok** — süre setin boyutundandır
(yalnız `Tracon.Ui.E2ETests` tek başına 4 dk 43 sn). Eşik bugünkü set için
ulaşılabilir değil → `HATA-S1-003` (doküman, düşük önem).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### HATA-S1-001 — SQLite entegrasyon testleri tam çözüm yükü altında `database is locked` veriyor

| | |
|---|---|
| **Önem** | Yüksek |
| **Bulunduğu case** | MT-PKG-010 |
| **Sınıf** | Test altyapısı + ürün eşzamanlılık sınırı |

**Belirti.** `Tracon.Sqlite.IntegrationTests` 864 kalemin 40'ında düşüyor:
1 gerçek test + 39 `[Test Class Cleanup Failure]`.

```
failed Tracon.Sqlite.IntegrationTests.Contracts
       .SqliteSkillScriptGrantContractTests.Grant_does_not_leak_across_tenants (34s 984ms)
  Microsoft.Data.Sqlite.SqliteException : SQLite Error 5: 'database is locked'.
    at Tracon.DbHelpers.ReadSingleAsync[T](...)  src/Tracon.Sql.Shared/Internal/DbHelpers.cs:99
    at Tracon.SqlSkillScriptGrantStore.GrantAsync(...) src/Tracon.Sql.Shared/Stores/SqlSkillScriptGrantStore.cs:81
    at SkillScriptGrantContract.Grant_does_not_leak_across_tenants() src/Tracon.Testing.Contracts.Xunit/Contracts/SkillScriptGrantContract.cs:59

failed [Test Class Cleanup Failure (…SqliteSessionStoreContractTests.*)] (0ms)  × 39
  Class fixture 'SqliteSchemaFixture' threw in DisposeAsync
  ---- SQLite Error 5: 'database is locked'.
    at SqliteTestContext.ExecuteAsync(String) tests/Tracon.Sqlite.IntegrationTests/Infrastructure/SqliteTestContext.cs:317
    at SqliteTestContext.DisposeAsync()        tests/Tracon.Sqlite.IntegrationTests/Infrastructure/SqliteTestContext.cs:367
    at SqliteSchemaFixture.DisposeAsync()      tests/Tracon.Sqlite.IntegrationTests/Infrastructure/SqliteSchemaFixture.cs:43
```

**Yük bağımlı, deterministik değil — ölçüldü.** Aynı ikiliyle, aynı worktree'de,
yalnız o proje koşulduğunda:

```
dotnet test tests/Tracon.Sqlite.IntegrationTests -c Release --no-build
  -> cikis 0 · 41 sn · total 825 · failed 0
```

(825 ↔ 864 farkı, düşen koşumdaki 39 cleanup kaleminin ayrıca sayılmasıdır.)

**Kök neden okuması.** `SqliteFixture` assembly başına **tek** bir dosya açar
(`Path.GetTempPath()/tracon-tests-<guid>.db`,
`tests/Tracon.Sqlite.IntegrationTests/Infrastructure/SqliteFixture.cs:23`) ve
825 testin tamamı sınıf başına bir **tablo öneki** ile o dosyayı paylaşır.
Ürün tarafı her bağlantıda `PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000`
kurar (`src/Tracon.Sqlite/Internal/SqliteDataSource.cs:65`). WAL okuyucuyu
yazıcıdan ayırır ama **yazıcı-yazıcı** serileşmesi kalır; tam çözüm koşumunda
22 proje + Testcontainers + Playwright aynı anda CPU/IO için yarışırken bir
yazıcı 5 sn'lik `busy_timeout`'u aşıyor ve `SQLITE_BUSY` yükseliyor.

`SqliteSchemaFixture` yorumu (satır 12-15) migration'ların önek kapsamlı kilit
dosyasıyla (K-389) serileştiğini söyler; ancak `DisposeAsync`'teki
`DROP VIEW`/`DROP TABLE` akışı o kilidin **dışındadır** — 39 kaskadın oturduğu
yer burasıdır.

**Açık soru (Aşama 2).** Bu yalnız test altyapısı sınırı mı, yoksa ürünün
SQLite sağlayıcısının eşzamanlı yazıcı altında `SQLITE_BUSY`'yi yeniden
denemesi mi gerekir? Düşen test ürünün kendi `store`'unun içinde
(`SqlSkillScriptGrantStore.GrantAsync`) patlıyor; aynı duvara SQLite'ı
eşzamanlı kullanan bir tüketici de çarpar. Karar kapanış oturumuna aittir.

---

### HATA-S1-002 — `LiveVoiceLifecycleTests` yük altında kırılgan

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-PKG-010 |
| **Sınıf** | Kırılgan test (zamanlama) |

```
failed Tracon.AspNetCore.FunctionalTests.LiveVoiceLifecycleTests
       .The_transcript_is_written_to_the_session_history_when_persistence_is_on (1m 05s 221ms)
  System.TimeoutException : The condition never became true.
    at LiveVoiceLifecycleTests.WaitForAsync(Func`1 condition)
       tests/Tracon.AspNetCore.FunctionalTests/LiveVoiceLifecycleTests.cs:383
    at …The_transcript_is_written_to_the_session_history_when_persistence_is_on()
       tests/Tracon.AspNetCore.FunctionalTests/LiveVoiceLifecycleTests.cs:63
```

**Kırılganlık ölçüldü** — aynı ikili, üç gözlem:

| Koşum | Bağlam | Sonuç |
|---|---|---|
| 1 | tam çözüm, paralel | ☑ 1077/1077 geçti |
| 2 | tam çözüm, paralel | ☒ 1076/1077 — bu test düştü (1 dk 05 sn) |
| 3 | yalnız `*LiveVoiceLifecycleTests*` | ☑ 10/10 geçti · 7 sn |

`WaitForAsync` sonuç yerine **süre** bekliyor; yük altında koşul penceresi
kaçıyor. Bu, `36-GELISTIRME-KAPILARI.md` `MT-GDK-018`'in adıyla tarif ettiği
sınıfın (sonuç yerine sabit bekleme) bir örneğidir — kapanışta o kapıyla
birlikte değerlendirilmeli.

---

### HATA-S1-003 — `dotnet test` 2,5 dakika eşiği bugünkü set için ulaşılabilir değil

| | |
|---|---|
| **Önem** | Düşük (doküman) |
| **Bulunduğu case** | MT-PKG-010 |

Beklenen sonuç: "`dotnet test` **2,5 dakikayı** aşarsa bu bir kusurdur: asılı
kalan alt süreç aranır." Aranan yapıldı, asılı süreç **yok** (yukarıda).

Ölçüm: 318 sn ve 382 sn. Set bugün **22 proje · 7699 test**; tek başına
`Tracon.Ui.E2ETests` 4 dk 43 sn, `Tracon.AspNetCore.FunctionalTests` 5 dk 14 sn
sürüyor — ikisi de eşiğin tek başına üstünde. Eşik, set bu boyuta gelmeden
önce yazılmış görünüyor.

🚨 **Beklenen sonuç bu turda DEĞİŞTİRİLMEDİ.** Skill §1.1'in istisnası
"beklenen sonuç *koda göre* yanlışsa" der; buradaki eşik koda aykırı değil,
**aşılmış bir performans bütçesidir** ve yerine yeni bir sayı koymak ölçüye
dayanması gereken bir karardır (elde yalnız iki örnek var). Kapanış oturumu
ya eşiği ölçülmüş bir değere çeker ya da paralelliği düşürüp süreyi geri alır.

---

## MT-PKG-011 — `dotnet format` build'in görmediğini yakalar

**Gerçek sonuç**

2026-08-15'te düzeltilen beklenti **birebir** doğrulandı.

```
Directory.Build.props:26  <AnalysisLevel>latest-recommended</AnalysisLevel>
Directory.Build.props:28  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

dotnet build Tracon.slnx -c Release          -> cikis 1 · Build FAILED
  TraconToolAttribute.cs(119,5): error IDE0055: Fix formatting
    ...::TargetFramework=net8.0
    ...::TargetFramework=net10.0
    ...::TargetFramework=net9.0
  0 Warning(s) · 3 Error(s)

dotnet format Tracon.slnx --verify-no-changes --no-restore  -> cikis 2
  TraconToolAttribute.cs(119,5): error WHITESPACE: Delete 4 characters.
  TraconToolAttribute.cs(119,5): error IDE0055: Fix formatting
```

`IDE0055` **uyarı değil hata** olarak çıktı ve üç TFM'in üçünde de raporlandı.
Beklendiği gibi `dotnet format`'a sıra gelmeden `build` kırmızı oldu; ikisi
aynı anda kırmızıdır. `git checkout --` sonrası `git status --short` **boş**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-012 — Uyarı gerçekten hataya dönüşüyor

**Gerçek sonuç**
```
dotnet build src/Tracon.Core -c Release   (ManuelUyariTesti.cs varken) -> cikis 1
  ManuelUyariTesti.cs(3,21): error CS1591: Missing XML comment for
    publicly visible type or member 'ManuelUyariTesti'        [net8.0 · net9.0 · net10.0]
  ManuelUyariTesti.cs(5,16): error CS1591: ... 'ManuelUyariTesti.Deger' [net8.0 · net9.0 · net10.0]
  Build FAILED.

rm + dotnet build src/Tracon.Core -c Release -> cikis 0
  Build succeeded. 0 Warning(s) · 0 Error(s)
```

`CS1591` **error** olarak çıktı, `warning` olarak değil — üç TFM'in üçünde ve
iki üyenin ikisinde de (6 tanı). `TreatWarningsAsErrors` muafiyetsiz çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-013 — Senkronizasyon kopyası derlemeyi kırar, sessizce geçmez

**Gerçek sonuç**
```
1) find src -name "* 2.*" -not -path "*/node_modules/*"   -> BOS
2) cp TraconToolAttribute.cs "TraconToolAttribute 2.cs"
3) dotnet build src/Tracon.Abstractions -c Release        -> cikis 1
   TraconToolAttribute.cs(30,21): error CS0101: The namespace 'Tracon'
     already contains a definition for 'TraconToolAttribute'  [net8.0 · net9.0 · net10.0]
   Build FAILED.
4) rm + find                                              -> BOS
   dotnet build src/Tracon.Abstractions -c Release        -> cikis 0
   Build succeeded. 0 Warning(s) · 0 Error(s)
```

Kopya **sessizce geçmedi**; derleme `CS0101` ile durdu. `git status --short`
son adımdan sonra boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-014 — Hızlı iç döngü arayüz zincirini atlar

**Gerçek sonuç**
```
dotnet build Tracon.slnx -c Release -p:TraconFrontendEnabled=false
  -> cikis 0 · 42 sn · Build succeeded · 0 Warning(s) · 0 Error(s)
grep -Ei "npm ci|npm run build|arayuz derleniyor"
  -> NPM ADIMI YOK - beklenen
```

Karşılaştırma: aynı worktree'de arayüz açıkken tam derleme 62 sn (MT-PKG-010),
kapalıyken 42 sn. Bu bayrakla derlenen çıktıyla **E2E testi koşulmadı**;
MT-PKG-015'in son adımı `src/Tracon.UI`'yi normal biçimde yeniden derledi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-015 — Arayüz varlığı yokken `pack` hata verir

**Gerçek sonuç**
```
wwwroot/index.html (once)  -> var
mv src/Tracon.UI/wwwroot <yedek>
MSBUILDDISABLENODEREUSE=1 dotnet pack src/Tracon.UI -c Release \
  -p:TraconFrontendEnabled=true --no-build -o <scratch>/ap-ui-test
  -> pack cikis 1
  Tracon.UI.Frontend.targets(290,5): error TRACON0003: Tracon.UI: UI assets were
    not produced (.../src/Tracon.UI/wwwroot/index.html does not exist). A package
    with an empty UI cannot be published. Install Node.js 20.19+ and rebuild.
mv <yedek> src/Tracon.UI/wwwroot
dotnet build src/Tracon.UI -c Release -> cikis 0
wwwroot/index.html (sonra) -> var
```

⚠️ **Beklenen sonuç DÜZELTİLDİ** (skill §1.1 tek istisnası — doküman kusuru).
Spec `"arayuz varligi uretilmemis"` ve `"Node.js 20.19+ kurun"` Türkçe
alıntılarını bekliyordu; ürünün metni **İngilizce**. Ölçüldü ki bu bir dil
ihlali değildir: `Tracon.UI.Frontend.targets` **pakete girmiyor**
(`unzip -l Tracon.UI.0.0.0-preview.0.789.nupkg | grep -i targets` → boş),
yani TRACON0003 yalnız repo derlemesinde çalışan geliştirme aparatıdır ve
K-228'in "pakete giren her şey İngilizce" sınırının dışındadır. İddia edilen
davranış — `pack` düşer, kod `TRACON0003`, metin hem eksik varlığı hem çözümü
adıyla söyler — **karşılanıyor**. Spec'teki alıntı bugünkü metne çekildi.

**Sapma:** Geçici taşıma `/tmp` yerine oturumun scratchpad dizinine yapıldı
(şerit yalıtımı; `/tmp/ap-wwwroot-yedek` Faz B'de dört şerit arasında
çakışırdı). Test edilen şey taşıma yeri değil, kapının kendisidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-016 — Yayınlanabilir pakette `README.md` eksikse derleme durur

**Gerçek sonuç**
```
mv src/Tracon.Voice/README.md <yedek>
MSBUILDDISABLENODEREUSE=1 dotnet pack src/Tracon.Voice -c Release -o <scratch>/ap-voice-test
  -> pack cikis 1
  Directory.Build.targets(47,5): error TRACON0001: Yayinlanabilir paket
    'Tracon.Voice' icin README.md eksik. NuGet paket sayfasinda gorunecek bir
    README.md dosyasi ekleyin.
mv <yedek> src/Tracon.Voice/README.md
dotnet pack src/Tracon.Voice -c Release -> cikis 0
```

Hata metni paket adını (`Tracon.Voice`) taşıyor. `git status --short` boş.

📌 **Gözlem (kusur değil).** `TRACON0001` Türkçe, `TRACON0003` İngilizce.
İkisi de pakete girmeyen derleme aparatıdır (`Directory.Build.targets` de
`Tracon.Voice` nupkg'ında yok), yani K-228 ihlali yoktur — ama aynı tanı
ailesinin iki dili var. Tutarlılık kararı kapanış oturumuna aittir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-020 — Paket sayısı ve sembol paketi sayısı

**Gerçek sonuç**
```
MSBUILDDISABLENODEREUSE=1 dotnet pack Tracon.slnx -c Release -o <scratch>/ap-pack
  -> cikis 0
nupkg : 20
snupkg: 18
snupkg URETMEYEN: Tracon (meta) · Tracon.Templates
Generators yayimlanmadi - beklenen
Sql.Shared paket degil - beklenen
```

Üretilen 20 paket: `Tracon` · `.Abstractions` · `.Anthropic` · `.AspNetCore` ·
`.Azure` · `.Cli` · `.Client` · `.Core` · `.Google` · `.Mcp` · `.OpenAI` ·
`.PostgreSql` · `.SqlServer` · `.Sqlite` · `.Templates` · `.Testing` ·
`.Testing.Contracts.Xunit` · `.UI` · `.Voice` · `.Workflows`.

⚠️ **Beklenen sonuç DÜZELTİLDİ** (skill §1.1 tek istisnası — doküman kusuru).
Spec 17 `.nupkg` / 16 `.snupkg` bekliyordu ve sembolsüz tek paketin
`Tracon.Templates` olduğunu söylüyordu. Koda göre ölçüldü:

- `src/` altında **22** proje var (spec 18 diyordu). `Tracon.Generators`
  `IsPackable=false` taşır, `Tracon.Sql.Shared` paket üretmez → 22 − 2 = **20**.
- Üç paket spec yazıldıktan sonra eklenmiş: `Tracon.Cli`, `Tracon.Client`,
  `Tracon.Testing.Contracts.Xunit`. 17 + 3 = 20.
- Sembol üretmeyen **iki** paket var: `Tracon.Templates`
  (`Tracon.Templates.csproj:35`) **ve** meta paket `Tracon`
  (`Tracon.csproj:20`, yorumu "for the same reason as Tracon.Templates").
  20 − 2 = **18**.

Üçü de tasarım gereğidir; ürün kusuru yoktur, spec'in sayıları bayattı.

📌 **Aynı dosyada üç farklı paket sayısı var.** `MT-PKG-020` 17 diyordu,
`MT-PKG-098` "19 paketin hepsi" diyor, `MT-PKG-105` "20/20" diyor. Ölçülen
**20**. `098` blok 090-099'da yeniden ölçülecek; sayıyı tek yerden türetmek
kapanış oturumunun işidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-021 — 🚨 Kaynak üreteci `.nupkg` içinde taşınıyor mu

**Gerçek sonuç**
```
dotnet build Tracon.slnx -c Release -> cikis 0

analyzers/dotnet/cs/Tracon.Generators.dll sayimi:
  tek-build        1
  tek-nobuild      1
  cozum-build      1
  cozum-nobuild    1

unzip -l <cozum-nobuild>/Tracon.Core.*.nupkg | grep analyzers
  112640  09-16-2026 14:28   analyzers/dotnet/cs/Tracon.Generators.dll
```

✅ **Dördü de `1`.** Şüphelenilen kusur — `--no-build` ile tek proje
paketlendiğinde üreteç DLL'inin düşmesi — bu turda **yeniden üretilemedi**.
`00-INDEKS.md` §8'in kaydettiği ölçüm (`tek-nobuild` → `0`) bugün geçerli
değil; kusur arada kapanmış. Yayın bu gerekçeyle durmaz.

Bu, `DEVIR.md` §8'in uyardığı sınıfın bir örneğidir: düzeltmeden önce kusuru
ampirik olarak yeniden üret — 2026-08 turunda üç kusur zaten kapanmış çıkmıştı.

📌 **Boyut bayat.** Beklenen sonuç "DLL boyutu ~53 KB" diyor; ölçülen
**112.640 bayt (110 KB)**. Üreteç büyümüş. Case'in iddiası **varlıktır**,
boyut ikincil bir gözlemdir; bu yüzden spec'e dokunulmadı — kapanış oturumu
ya sayıyı tazeler ya da satırı düşürür.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-022 — Her pakette üç TFM ve XML dokümanı var

**Gerçek sonuç**
```
Tracon.Core.nupkg
  lib/net8.0/Tracon.Core.dll   1.741.312 B   lib/net8.0/Tracon.Core.xml   1.312.906 B
  lib/net9.0/Tracon.Core.dll   1.742.336 B   lib/net9.0/Tracon.Core.xml   1.314.249 B
  lib/net10.0/Tracon.Core.dll  1.742.336 B   lib/net10.0/Tracon.Core.xml  1.315.424 B
  README.md                        5.942 B   (paket kokunde)

Tracon.Testing.nupkg
  lib/net8.0/ · lib/net9.0/ · lib/net10.0/  -> her birinde .dll + .xml

Tracon.0.0.0-preview.0.789.nupkg (meta)
  meta pakette lib/ YOK - beklenen
```

Üç TFM'in üçünde de `.dll` **ve** `.xml` yan yana; XML dokümanı kapısı paket
düzeyinde tutuyor. README paket kökünde. Meta paket `lib/` taşımıyor.

⚠️ **Beklenen sonuç DÜZELTİLDİ** (skill §1.1 tek istisnası — doküman kusuru).
Spec `Tracon.Testing` için "yalnız `lib/net10.0/` (tek TFM, **bilinçli**)"
diyordu; pakette üç TFM var. Koda göre ölçüldü — bu bilinçli bir genişletmedir,
kayıp bir kısıt değil:

- `docs/KARARLAR-INDEKS.md:83` → **K-780**: "`Tracon.Testing` çalışma
  paketleriyle AYNI matrisi hedefler (`net8.0;net9.0;net10.0`); K-270'in
  tek-TFM daralması KALDIRILDI".
- Commit `7146ce7b` "feat(testing): widen Tracon.Testing to the runtime target matrix".
- `src/Tracon.Testing/Tracon.Testing.csproj` yorumu gerekçeyi taşıyor:
  ".NET 8 LTS üzerinde koşan bir tüketici uygulamasını test edebilmelidir."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-024 — Sembol paketi taşınabilir PDB taşır

**Gerçek sonuç**
```
unzip -l <scratch>/ap-pack/Tracon.Core.*.snupkg
    5.876 B  Tracon.Core.nuspec
  514.376 B  lib/net8.0/Tracon.Core.pdb
  514.612 B  lib/net9.0/Tracon.Core.pdb
  514.248 B  lib/net10.0/Tracon.Core.pdb

uzanti: Tracon.Core.0.0.0-preview.0.789.snupkg   (.symbols.nupkg DEGIL)
```

Üç TFM için birer `.pdb`; uzantı `.snupkg`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-023 — nuspec üstverisi eksiksiz

**Gerçek sonuç** — `Tracon.Core.0.0.0-preview.0.789.nuspec`
```xml
<id>Tracon.Core</id>
<version>0.0.0-preview.0.789</version>
<authors>Faruk Atasoy</authors>
<requireLicenseAcceptance>true</requireLicenseAcceptance>
<license type="file">LICENSE.md</license>
<icon>icon.png</icon>
<readme>README.md</readme>
<projectUrl>https://tracon.dev/</projectUrl>
<releaseNotes>https://tracon.dev/reference/changelog/#v0.0.0-preview.0.789</releaseNotes>
<copyright>Copyright (c) Faruk Atasoy</copyright>
<tags>tracon ai agents microsoft-agent-framework llm dotnet runtime catalog harness</tags>
<repository type="git" url="https://github.com/farukatasoy/Tracon"
            branch="refs/heads/test/kosum-s1"
            commit="6979fd60e7bda2a0ef7e29ec622be8f56a0da5be" />
```

Madde madde:

- `<license type="file">LICENSE.md</license>` ✅ (Faz 160)
- `<requireLicenseAcceptance>true</requireLicenseAcceptance>` ✅ — PolyForm
  lisanslı pakette element **yazılıyor**; 2026-08-15'te düzeltilen not tutuyor
- `<readme>README.md</readme>` ✅
- `<authors>Faruk Atasoy</authors>` · `<projectUrl>` · `<repository type="git" url=...>` ✅
- `<repository>` `commit` niteliği **boş değil** ✅ → `6979fd60e7bd…`
- `<tags>` içinde `tracon ai agents microsoft-agent-framework llm dotnet` ✅
  (sonuna `runtime catalog harness` de eklenmiş)
- `TODO` / `placeholder` / boş eleman taraması → **temiz** ✅

📌 **Gözlem.** `branch` niteliği `refs/heads/test/kosum-s1` yazıyor — bu şerit
worktree'sinin dalı. Gerçek yayında `main` olacaktır; koşum ortamının izidir,
kusur değil. `<licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>`
NuGet'in `license` elementi kullanıldığında otomatik yazdığı yer tutucudur.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-025 — Deterministik build: iki paketleme aynı derlemeyi üretir

**Gerçek sonuç**
```
1: 44956cd7e44ad45abb90053df1980f6145723ae518cd032badc82fba9435ac56
2: 44956cd7e44ad45abb90053df1980f6145723ae518cd032badc82fba9435ac56
AYNI
```

`artifacts/obj/Tracon.Abstractions` silindikten sonra yeniden paketlendi;
`lib/net10.0/Tracon.Abstractions.dll` SHA-256 özeti **birebir aynı**. Gömülü
mutlak yol veya zaman damgası yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-026 — Şablon paketi doğru biçimde kurulur

**Gerçek sonuç**
```
content/Tracon.Starter/.template.config/template.json        3.526 B
content/Tracon.Starter/.template.config/dotnetcli.host.json    391 B
content/Tracon.Starter/.gitignore                              145 B
content/Tracon.Starter/Program.cs · appsettings.json · appsettings.Development.json
content/Tracon.Starter/Properties/launchSettings.json · README.md
content/Tracon.Starter/Tools/OrderTools.cs · Tracon.Starter.csproj

"content/content/" eslesme sayisi : 0
"lib/"            eslesme sayisi : 0

nuspec:
  <readme>README.md</readme>
  <packageTypes><packageType name="Template" /></packageTypes>
```

Nokta ile başlayan iki yol (`.template.config/`, `.gitignore`) pakete
**girmiş** — `NoDefaultExcludes` çalışıyor. Yollar tek katmanlı, `lib/` yok,
paket tipi `Template`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-027 — Sürüm git etiketinden gelir

**Gerçek sonuç**
```
etiketsiz : Tracon.Abstractions.0.0.0-preview.0.789.nupkg
git tag v1.0.0-preview.1
etiketli  : Tracon.Abstractions.1.0.0-preview.1.nupkg
git tag -d v1.0.0-preview.1   -> Deleted tag (was 6979fd60)
```

MinVer davranışı doğrulandı: etiketsizken `0.0.0-preview.0.<N>`, etiket
atılınca **tam olarak** `1.0.0-preview.1`, etiket silinince eski sürüm geri
geldi. Etiket şerit worktree'sinde atıldı ve silindi; `git tag` listesi
koşum öncesi ve sonrası **birebir aynı**.

⚠️ **İki doküman sapması — ikisi de case'in iddiasını bozmuyor:**

1. Spec "Bugün repo'da **hiç etiket yoktur**" diyor; `git tag` **iki** etiket
   gösteriyor: `arsiv/ilk-gun-stash-2026-08-01` ve `docs/damitma-oncesi-2026-08`.
   İkisi de sürüm etiketi **değildir** (MinVer yalnız `v`/sürüm önekli
   etiketleri okur), bu yüzden etiketsiz sürüm doğru türedi. Spec'in `git tag`
   adımı "boş olmalı" yorumu bugün için yanlış — ama **sürüm etiketi** yok
   iddiası doğru.
2. Spec `<N>`'in "commit sayısı" olduğunu söylüyor. `git rev-list --count HEAD`
   → **828**, sürümdeki `<N>` → **789**. MinVer commit sayısını değil, taban
   sürüm etiketinden itibaren **yüksekliği** sayar. Fark (39) beklenen bir
   şeydir; "commit sayısı" ifadesi gevşek.

Spec'e dokunulmadı: ikisi de case'in **iddiasını** (sürüm etiketten türer)
değiştirmiyor, yalnız yan cümleleri gevşek. Kapanış oturumu ifadeyi
sıkılaştırabilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-030 — Meta paket yalnız altı bileşen getirir

**Gerçek sonuç** — `Tracon.nuspec` bağımlılıkları (tamamı):
```
Tracon.AspNetCore · Tracon.Mcp · Tracon.OpenAI
Tracon.PostgreSql · Tracon.UI · Tracon.Workflows
```

Tam **altı**, beklenen altının aynısı. Görünmeyenler doğrulandı:
`Tracon.SqlServer` · `Tracon.Sqlite` · `Tracon.Anthropic` · `Tracon.Google` ·
`Tracon.Azure` · `Tracon.Voice` · `Tracon.Testing` · `Tracon.Templates`.

📌 MT-PKG-020'de bulunan üç yeni paket (`Tracon.Cli`, `Tracon.Client`,
`Tracon.Testing.Contracts.Xunit`) meta pakete **girmemiş** — K-185'in
tasarım kuralı korunuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-031 — Geçişli sabitleme kapalı: grafik kirlenmiyor

**Gerçek sonuç** (`net10.0` grubu)
```
Tracon.PostgreSql   -> 2 bagimlilik
  Tracon.Core  0.0.0-preview.0.789
  Npgsql       10.0.3
Tracon.Abstractions -> 2 bagimlilik
  Microsoft.Agents.AI.Abstractions      1.20.0
  Microsoft.Extensions.AI.Abstractions  10.9.0
```

İkisi de **2**. `CentralPackageTransitivePinningEnabled=true` olsaydı
`Tracon.PostgreSql` 13 bildirirdi. `OpenTelemetry.Api`, `OpenAI` gibi geçişli
paketler hiçbirinde görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-032 — Önsürüm MAF paketleri yalnız `Tracon.AspNetCore`'da

**Gerçek sonuç** — 20 paketin tamamı tarandı, çıkan **her** satır:
```
Tracon.AspNetCore : A2A.AspNetCore                         1.0.0-preview2
Tracon.AspNetCore : Microsoft.Agents.AI.Hosting            1.20.0-preview.260831.1
Tracon.AspNetCore : Microsoft.Agents.AI.Hosting.A2A        1.20.0-preview.260831.1
Tracon.AspNetCore : Microsoft.Agents.AI.Hosting.AspNetCore 1.20.0-preview.260831.1
Tracon.AspNetCore : Microsoft.Agents.AI.Hosting.OpenAI     1.20.0-alpha.260831.1
```

Beş satırın beşi de `Tracon.AspNetCore` ile başlıyor. `Tracon.Core`,
`Tracon.Abstractions`, `Tracon.PostgreSql`, `Tracon.OpenAI` hiçbir satırda yok.
Beklenen beş önsürüm paketinin beşi de listede. K-008 sınırı tutuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-033 — Roslyn tüketicinin grafiğine sızmıyor

**Gerçek sonuç**
```
(hicbir BULGU satiri yok)
tarama bitti
```

20 paketin nuspec'inde `CodeAnalysis` geçen **tek** satır yok.
`Microsoft.CodeAnalysis.CSharp` hiçbir pakette bağımlılık olarak bildirilmiyor
— üreteç `analyzers/dotnet/cs/` altında taşınıyor (MT-PKG-021), bağımlılık
olarak değil. K-348 · K-349 tutuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-034 — Ağır sağlayıcı zincirleri kendi paketlerinde kalıyor

**Gerçek sonuç** — 20 paket tarandı, çıkan **tüm** satırlar:
```
Tracon.Anthropic  : Anthropic                    12.39.0
Tracon.Google     : Google.GenAI                  1.16.0
Tracon.OpenAI     : OpenAI                        2.12.0
Tracon.PostgreSql : Npgsql                        10.0.3
Tracon.SqlServer  : Microsoft.Data.SqlClient       7.0.2
```

Her sağlayıcı SDK'sı **tam olarak bir** pakette. Meta pakette hiçbiri yok
(MT-PKG-030'un altı satırında görülebilir). K-205 tutuyor: Gemini
kullanmayan tüketici `Google.Apis.Auth` → `Newtonsoft.Json` →
`System.Management` zincirini almıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

> ### 🔧 Bölüm ön koşulu bu turda GERİ GETİRİLDİ
> `MT-PKG-040`..`050` (ve `060`, `061`) "Bölüm ön koşulu uygulandı" diyor ama
> o ön koşul spec'te **yoktu** — `946a37fb` ("faz 58") bölüm başlıklarıyla
> birlikte onu da düşürmüş, koşum kaydında bırakmıştı. Sekiz case koşulamaz
> durumdaydı. Blok git'ten çözülüp bugünkü adlara çevrildi ve spec'e
> `## Üreteç bölümünün ön koşulu` başlığıyla geri kondu (bölüm **başlığı**
> eklenmedi — kullanıcı kararı gereği bölme noktası onluk bloktur).
> Ölçüm: setin geri kalanında tanımsız referans **yok** (yalnız bu dosya, 8 ref).
>
> Uygulanan ön koşul: `~/tracon-manuel/uretec` konsol projesi, `nuget.config`
> yalnız nuget.org + yerel paket dizini, `Tracon.Core 0.0.0-preview.0.789`
> **doğrudan** `PackageReference`. Sapma: yerel dizin `/tmp/ap-pack` yerine
> oturumun scratchpad'i.

## MT-PKG-040 — Üreteç işaretli statik metodu kaydeder

**Gerçek sonuç**
```
dotnet new console + dotnet add package Tracon.Core --version 0.0.0-preview.0.789
  -> ikisi de cikis 0

dotnet build -c Release -p:EmitCompilerGeneratedFiles=true \
             -p:CompilerGeneratedFilesOutputPath=obj/generated
  -> cikis 0 · Build succeeded · 1 Warning(s) · 0 Error(s)

uretilen dosyalar:
  TraconGeneratedTools.g.cs
  GetOrderStatus_274C17A0Tool.g.cs

TraconGeneratedTools.g.cs:1   // <auto-generated/>
TraconGeneratedTools.g.cs:20  namespace Tracon
TraconGeneratedTools.g.cs:31  public static global::Tracon.ITraconBuilder
                                AddGeneratedTools(this global::Tracon.ITraconBuilder builder)
GetOrderStatus_274C17A0Tool.g.cs:12  public override string Name => "get_order_status";

YASAKLI dizgi taramasi (System.Reflection · Activator. · GetMethod( · AIFunctionFactory)
  -> hicbiri yok
```

Üreteç paketten geldi ve çalıştı — `MT-PKG-021`'in ölçtüğü DLL'in canlı
karşılığı budur. Kayıt **yansımasızdır**.

⚠️ **Beklenen sonuç İKİ noktada DÜZELTİLDİ** (skill §1.1 istisnası):

1. *"Derleme sıfır uyarıyla biter"* → tek uyarı çıktı ve **beklenen** bir
   uyarıdır:
   ```
   Tools.cs(6,26): warning TRC0009: Parameter 'orderId' of tool
     'get_order_status' has no description. The model has only the parameter
     name to go on; add [Description].
   ```
   `TRC0009` beyan edilmiş bir tanıdır —
   `src/Tracon.Generators/AnalyzerReleases.Unshipped.md:16` ve
   `src/Tracon.Generators/ToolDiagnostics.cs:90`, Faz 125'te eklendi. Case'in
   fixture kodu `[Description]` taşımadığı için doğru biçimde tetikleniyor.
   Spec `TRC0001`..`TRC0007`'yi tanıyor; `TRC0009`'dan eskidir.

2. *"Dosyada `get_order_status` dizgisi geçer"* → `TraconGeneratedTools.g.cs`
   içinde **geçmiyor**, çünkü üreteç artık tool başına ayrı bir tip dosyası
   üretiyor. Ad `GetOrderStatus_274C17A0Tool.g.cs:12`'de. Kayıt listesinde
   yalnız üretilen tipin adı (`GetOrderStatus_274C17A0Tool`) var. Ürün doğru
   çalışıyor; spec tek dosyalı dönemden kalmış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-041 — Üretilen kod `dotnet format` kapısını geçer

**Gerçek sonuç**
```
dotnet format --verify-no-changes --no-restore  -> cikis 0

GetOrderStatus_274C17A0Tool.g.cs -> TAB satiri: 0 · satir sonu bosluklu satir: 0
TraconGeneratedTools.g.cs        -> TAB satiri: 0 · satir sonu bosluklu satir: 0
```

İki bağımsız yöntemle doğrulandı (BSD `grep -c` ve Python satır taraması);
ikisi de aynı sonucu verdi.

🚨 **Case'in kendi komutu KIRIKTI ve sessizce yeşil görünüyordu.** Spec
`grep -Pn '\t'` kullanıyor; macOS'un BSD grep'inde `-P` **yoktur**:
```
grep: invalid option -- P
usage: grep [-abcdDEFGHhIiJLlMmnOopqRSsUVvwXxZz] ...
```
grep hata verince `|| echo "TAB yok"` dalı çalışıyor ve çıktı `TAB yok`
yazıyordu — yani iddia **hiç ölçülmeden** geçmiş sayılıyordu. Bu, bu repo'nun
"kapı sessizce geçer" sınıfının bir örneğidir. `00-INDEKS.md` §2.1 ortamı
macOS olarak sabitliyor, yani komut bu sette hiçbir zaman çalışmadı.

Beklenen sonuç ve komut taşınabilir biçime çevrildi (§1.1 istisnası); yeni
komut üretilen **tüm** `.g.cs` dosyalarını tarar, yalnız birini değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-042 — `TRC0001`: aynı tool adı iki metotta

**Gerçek sonuç**
```
dotnet build -c Release -> cikis 1 (Build FAILED)

Hata.cs(6,26): error TRC0001: Tool name 'ayni_ad' is used on more than one
  method: global::CakisanTools.Bir, global::CakisanTools.Iki. Each tool name
  must be unique within the compilation.
  (https://tracon.dev/capabilities/#tools-skills-and-context)
Hata.cs(9,26): error TRC0001: (ayni mesaj)
```

Dört iddianın dördü de tuttu:
- Derleme **başarısız** ✅
- `TRC0001` **error** olarak çıktı (uyarı değil) ✅
- Mesaj `ayni_ad` adını **ve iki metodu birden** listeliyor
  (`CakisanTools.Bir, CakisanTools.Iki`) ✅
- Tanı **iki ayrı konumda** bildirildi: `(6,26)` ve `(9,26)` ✅

Son kayıt sessizce kazanmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-043 — `TRC0002`: geçersiz karakterli tool adı

**Gerçek sonuç**
```
1. derleme (3 gecersiz ad) -> cikis 1
   TRC0002 essiz tani sayisi: 3   (ham satir sayisi 6 - her tani iki kez basiliyor)
   'KotuAdTools.A' -> 'get order'  (bosluk)
   'KotuAdTools.B' -> 'get.order'  (nokta)
   'KotuAdTools.C' -> 65 karakterlik ad
   Mesaj: "A tool name must be 1-64 characters and contain only letters,
           digits, '_', or '-'."
2. derleme (64 karakter) -> cikis 0 · TRC0002 sayisi 0
```

Üç iddia da tuttu: 3 tanı, 64 karakter geçerli, mesaj **hem metot adını hem
geçersiz tool adını** taşıyor.

📌 **Sayım nüansı.** Spec `grep -c "TRC0002"` diyor ve "3" bekliyor; ham sayım
**6** verir çünkü her tanı iki kez basılıyor. Eşsiz tanı sayısı 3'tür. Spec'e
dokunulmadı — iddia (üç geçersiz ad, üç tanı) doğru; ölçen komut gevşek.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-044 — `TRC0003`: desteklenmeyen parametre tipi

**Gerçek sonuç**
```
1. derleme (internal record parametre) -> cikis 1
error TRC0003: Parameter 'siparis' (type 'Siparis') of method
  'BilesikTools.SiparisVer' is not supported by the generator. Supported types:
  primitive types, string, Guid, DateTime(Offset), enum, arrays/IReadOnlyList<T>
  of these, CancellationToken, and a supported object - a public record or class
  with a single public constructor, up to 3 nested object levels deep
  (see TRC0011, TRC0012). For another type, register manually with
  'AddTool(AIFunctionFactory.Create(...))'.

2. derleme (14 parametreli beyaz liste) -> cikis 0 · TRC0003 sayisi 0
```

Dört iddia da tuttu: 1. derleme düştü, mesaj desteklenen tipleri **listeliyor**,
`AddTool(AIFunctionFactory.Create(...))` kaçış yolunu **gösteriyor**, 2. derleme
temiz ve `CancellationToken` bir tool parametresi olarak **kabul edildi**.

📌 **Case'in gerekçe metni bayat, iddiası değil.** Spec başlığında "Beyaz liste
`record`/`class` tiplerini **kapsamaz**; bu bilinçli bir sınırdır" yazıyor.
Tanının kendi metni artık bunun tersini söylüyor: *public* record/class **tek**
public kurucuyla, 3 seviye derinliğe kadar **destekleniyor** (TRC0011 · TRC0012
sonradan eklenmiş). Case yine de geçiyor çünkü fixture `internal sealed record`
kullanıyor — `internal` olduğu için reddediliyor. Spec'e dokunulmadı; gerekçe
cümlesinin tazelenmesi kapanış oturumunun işidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-045 — `TRC0004`: generic metot tool olamaz

**Gerçek sonuç**
```
dotnet build -c Release -> cikis 1
error TRC0004: Method 'GenericTools.Getir' is marked with [TraconTool] but is
  generic. Tool methods cannot be generic; write a concrete wrapper method.
```

`error` seviyesinde ve mesaj **somut bir sarmalayıcı metot** yazmayı öneriyor.
Eskiden çalışma anında çıkan hata artık derleme anında yakalanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-046 — `TRC0005`: çağrı var, işaretli metot yok

**Gerçek sonuç**
```
Tools.cs tasindi; Program.cs -> services.AddTracon().AddGeneratedTools();
dotnet build -c Release -> cikis 1
error TRC0005: 'AddGeneratedTools()' was called, but this compilation has no
  method marked with [TraconTool]. Mark tool methods, or remove this call.

baska derleme hatasi (CS****): YOK
Tools.cs geri konuldu -> cikis 0 · TRC0005 sayisi 0
```

Üç iddia da tuttu: derleme düştü, `TRC0005` **error**, mesaj **iki çözüm**
öneriyor (metotları işaretle **veya** çağrıyı kaldır). Sessiz atlama yok.
Başka bir derleme hatası tanıyı gölgelemedi.

**Sapma:** `Tools.cs` yedeği `/tmp` yerine scratchpad'e alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-047 — `TRC0006`: açıklama eksik — hata değil, uyarı

**Gerçek sonuç**
```
1. derleme -> cikis 0  (BASARILI)
warning TRC0006: Tool 'aciklamasiz' has no description. The model cannot know
  when to call the tool without one; give a description for [TraconTool].

dotnet build -p:NoWarn=TRC0006 -> cikis 0 · TRC0006 sayisi 0
```

Üç iddia da tuttu: `TRC0006` **warning** seviyesinde, tüketicinin derlemesi
**kırılmadı** (çıkış 0), ve `NoWarn` ile tamamen bastırılabiliyor. Kütüphane
tüketicinin derlemesini kırma hakkını dikkatli kullanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-048 — `TRC0007`: örnek metot tool olamaz

**Gerçek sonuç**
```
1. derleme (instance metot) -> cikis 1
error TRC0007: 'OrnekTools.Getir' is an instance method and cannot be a tool.
  MAF passes an empty provider as AIFunctionArguments.Services (decision K-218).
  Make the method 'static', or instantiate the tool at setup time and register
  it with 'AddTool(AIFunctionFactory.Create(...))'.

static yapildi -> cikis 0 · TRC0007 sayisi 0
```

Üç iddia da tuttu: derleme düştü, mesaj **K-218'e açıkça atıf yapıyor**
("decision K-218"), ve **iki çözüm** gösteriyor (`static` yap **veya** kurulum
anında örnekleyip `AddTool(AIFunctionFactory.Create(...))` ile kaydet).
Eskiden sessizce bozuk olan yol artık derleme anında duruyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-049 — İşaretsiz metot sessizce tool olmaz

**Gerçek sonuç**
```
dotnet build -c Release -p:EmitCompilerGeneratedFiles=true ... -> cikis 0
  1 Warning(s) · 0 Error(s)   (tek uyari: TRC0009 - bkz. MT-PKG-040)

TraconGeneratedTools.g.cs icinde
  GizliYardimci  sayisi: 0
  GetOrderStatus sayisi: 1
uretilen dosya sayisi   : 2
GizliYardimci HICBIR uretilen dosyada gecmiyor (iki dosya da tarandi)
```

Güvenlik sınırı tutuyor: aynı sınıfa eklenen **işaretsiz** public metot
üretilen kodun hiçbir yerine girmedi, agent'lara açılmadı. İşaretleme açık bir
tercih olarak kalıyor. İşaretsiz metot için **hiçbir tanı** üretilmedi.

⚠️ **Beklenen sonuç DÜZELTİLDİ** — MT-PKG-040 ile aynı gerekçe: "sıfır uyarı"
beklentisi `TRC0009` eklenmeden önce yazılmış. Çıkan tek uyarı işaretsiz
metotla ilgili değil, `orderId` parametresinin `[Description]` taşımamasıyla
ilgilidir. Ayrıca "`GizliYardimci` sayısı 0" iddiası tek dosya yerine
**üretilen tüm dosyalar** için ölçülecek biçimde netleştirildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
