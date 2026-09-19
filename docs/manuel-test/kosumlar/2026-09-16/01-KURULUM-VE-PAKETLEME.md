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

**Oturum 3 · BİTTİ. DOSYA 01 KAPANDI.** `MT-PKG-100..122` — **23 case:
18 ☑ Geçti · 5 ☐ Beklemede (bloklu).** Blok 100-109 · 110-119 · 120-122
kapandı.

- **Nerede kalındı:** dosya 01'in sonunda. Dosya 01 toplamı: **81/81 case ·
  72 Geçti · 4 Kaldı · 5 Beklemede** (sayım betiği, skill §7).
- **Sonraki oturum:** zincirin 2. ailesi `02-CEKIRDEK-VE-KATALOG.md`
  (97 case, 4 oturum). `ap-s1` şeridinde devam.
- **Bozuk ön koşul:** yok — 02 için dosya 01'den taşınan bağımlılık yok.

🚨 **`HATA-S1-007` — yayın hattını bloklayan yeni bulgu.** `scripts/kapi.py`
hedef sürüm için `CHANGELOG.md`'de `## [<sürüm>]` bölümü arıyor; changelog ise
bilinçli olarak yalnız `## [Unreleased]` taşıyor ("ilk gerçek yayın kendi
bölümünü alır"). İki kural birbirini kilitliyor → **prova hiçbir sürümle yeşil
olamaz**. `MT-PKG-097` ve `101` bu yüzden `Kaldı`; `104 · 105 · 115 · 116 ·
117` terfi etmiş paket istediği için `Beklemede`. Kullanıcı kararı
(2026-09-16): `CHANGELOG.md` tur boyunca **donuk kalır**, düğüm Aşama 2'de
karar olarak çözülür.

✅ **`HATA-S1-006` KAPANDI — yanlış pozitifti.** Üç bulgunun üçü de tek kök
nedenden geliyordu: `check-content.mjs` üreteçler koşmadan çalıştırılmıştı.
`reference/changelog.md` commit'li değil, `prebuild` onu kök `CHANGELOG.md`'den
üretiyor. Kontrollü deneyle kanıtlandı (sayfayı kaldır → aynı 3 bulgu; geri koy
→ kapı yeşil). Aşama 2'de kod işi yok; yalnız tuzak notu gerekiyor.

**Oturum 3'ün spec düzeltmeleri** (skill §1.1 istisnası, üçü de doküman kusuru):

| Case | Ne düzeltildi |
|---|---|
| MT-PKG-102 | Ön koşul artık var olmayan bir changelog başlığını tarif ediyordu |
| MT-PKG-107 | Beklenen hedef GitHub blob URL'i → site içi `/reference/changelog/` (repo private; üreteç yorumu gerekçeli) |
| MT-PKG-122 | Doğrulama komutu yalnız satır içi biçimi arıyordu; başlık biçimini "iddia yok" sanıyordu → iki biçimi de tarayan betikle değiştirildi |

**Kapanışa aday, düşük önem (kod donuk, düzeltilmedi):** `Tracon.Workflows`
README'si lisans iddiasında diğer 19'dan farklı biçim kullanıyor · sevk edilen
README'lerde `Licence` (13) ve `License` (7) karışık.

**Oturum 3'te ortam:** `docs-site` derlendi (`npm run build`, çıkış 0, 1147
sayfa); üretilen `reference/changelog.md` ve `dist/` ağaçta kaldı — ikisi de
`.gitignore` kapsamında, `git status` boş. Preview sunucusu durduruldu.
`<scratch>/ap-pack` (20 paket) hâlâ duruyor, dosya 02 kullanmıyor.

**Kod donuk kaldı:** `git diff 7e3a4de7..HEAD -- src samples tests` boş.
`MT-PKG-109..114` ağacı geçici kirletti, altısı da aynı case içinde
`git checkout --` ile geri alındı; her birinin sonunda `git status --porcelain`
boş doğrulandı. `MT-PKG-113`'ün ürettiği iki `dirty.deneme` paketi silindi.

---

**Oturum 2 · BİTTİ.** `MT-PKG-050..099` — **25 case: 23 ☑ Geçti · 2 ☑ Kaldı.**
Bloklar 050-059 · 060-069 · 070-079 · 080-089 · 090-099 kapandı.

- **Nerede kalındı:** blok 090-099'un sonunda. Sıradaki case `MT-PKG-100`.
- **Sonraki oturum:** oturum 3 = `MT-PKG-100..122` (23 case) — dosya 01'in son
  bloğu. Sonra zincirin 2. ailesi `02-CEKIRDEK-VE-KATALOG.md`.
- **Dosya 01 toplamı şu ana kadar:** 58/81 case · 55 Geçti · 3 Kaldı · açık yok.

**Oturum 2'nin yeni bulguları:** `HATA-S1-004` (arayüz CSP'si inline script'i
engelliyor) · `HATA-S1-005` (şablon yer tutucusu teşhis edilemeyen hata veriyor)
· `HATA-S1-006` (`docs-site` içerik kapısı temiz ağaçta kırmızı) · `NOT-02`
(`MT-PKG-027` sonraki case'lerin feed'ini kirletiyor).

**Oturum 2'de hazır bırakılanlar** — oturum 3 bunları yeniden kurmasın:
`<scratch>/ap-pack` (20 paket, temiz tek sürüm) · `~/tracon-local-feed` +
`tracon-local` NuGet kaynağı + `Tracon.Templates` şablonu (🚨 **tur sonunda
kaldırılacak**) · `~/tracon-manuel/` altında `uretec` · `varsayilan` · `meta` ·
`uisiz` · `surum` · `surum2` · `tryadd` · `postgres` · `sqlite` · `sqlserver` ·
`prov-{openai,anthropic,google,azure}` · `tuketici-probe` · `surface-probe`.

🚨 **Yayın hattı için:** `MT-PKG-097` bugün geçmiyor — `CHANGELOG.md` hedef
sürüm bölümü yazılmadan `1.0.0-preview.1` provası yeşil olamaz.

---

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

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19)**
`HATA-S1-001` · `HATA-S1-002` · `HATA-S1-003` kapandı. Boş bir makinede tam
koşum:

```
dotnet build  -c Release            -> 0 Warning(s), 0 Error(s)
dotnet test   -c Release -maxcpucount:1 -> çıkış 0 · 22 proje · 7930 test · 0 düşen · 10 dk 27 sn
dotnet format --verify-no-changes   -> temiz
```

Üç ölçüm kaydı düzeltiyor:
1. `-maxcpucount:1` test PROJELERİNİ gerçekten serileştiriyor — koşum boyunca
   işlem tablosu örneklendi, her an **tek** test süreci vardı. Doygunluk
   proje **içinden** geliyor; xunit varsayılanı işlemci başına bir thread.
   Üç ağır proje artık dörtte sınırlı (`xunit.runner.json`).
2. Altı kırılgan örnekten **hiçbiri** bu koşumda düşmedi.
3. 2,5 dakikalık eşik ulaşılamaz; ölçülen süreye ~%45 pay bırakan **15
   dakika** yazıldı. 🚨 Ölçüm başka iş koşarken yapılmaz: bir koşum yanında
   koşan derlemeler yüzünden kirlendi ve **iptal edildi**, raporlanmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

---

> ## 🔁 Oturum 2 — `MT-PKG-050..099`
> Şerit `ap-s1`, kod `7e3a4de7` donuk. Ön koşullar oturum 1'den **hazır**
> devralındı: `<scratch>/ap-pack` (20 nupkg) ve `~/tracon-manuel/uretec`.

## MT-PKG-050 — Üreteç meta paket üzerinden de akıyor

**Gerçek sonuç**
```
dotnet new web + dotnet add package Tracon --version 0.0.0-preview.0.789
  -> ikisi de cikis 0   (YALNIZ meta paket referansi)

dotnet build -c Release -> cikis 0 · Build succeeded · 0 Error(s)
CS1061 sayisi: 0
```

Analyzer varlıkları meta paket üzerinden **geçişli olarak aktı**:
`builder.AddTracon().AddGeneratedTools();` derlendi ve `[TraconTool]`
işaretli `MetaTools.Getir` tanındı. `CS1061` çıkmadı — meta paketi alan
tüketici yansımasız yolu kullanabiliyor. MT-PKG-021 ile birlikte okunduğunda
üreteç zinciri uçtan uca sağlam.

📌 Tek uyarı yine `TRC0009` (`meta_tool`'un `id` parametresinde
`[Description]` yok) — case sıfır uyarı iddia etmiyor, kusur değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-060 — AOT uyumlu paketler sıfır trim uyarısı verir

**Gerçek sonuç**
```
dotnet publish -c Release -r osx-arm64 -p:PublishAot=true -> cikis 0
  Generating native code
  uretec -> .../bin/Release/net10.0/osx-arm64/publish/

IL2xxx / IL3xxx sayisi: 0

./bin/Release/net10.0/osx-arm64/publish/uretec
  -> "kayit tamam"   (calisma cikis 0)
```

Üç iddia da tuttu: sıfır trim/AOT uyarısı, yayınlama başarılı, üretilen
**yerel ikili çalıştı** ve beklenen çıktıyı verdi. Üretilen tool kaydı AOT
altında ayakta kalıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-061 — `AddToolsFrom` AOT bedelini çağırana iletiyor

**Gerçek sonuç**
```
dotnet publish -c Release -r osx-arm64 -p:PublishAot=true -> cikis 0

Program.cs(5): AOT analysis warning IL3050: Using member
  'Tracon.ITraconBuilder.AddToolsFrom(Type)' which has
  'RequiresDynamicCodeAttribute' can break functionality when AOT compiling.
  Tool scanning may require code generation at runtime.

Program.cs(5): Trim analysis warning IL2026: Using member
  'Tracon.ITraconBuilder.AddToolsFrom(Type)' which has
  'RequiresUnreferencedCodeAttribute' can break functionality when trimming
  application code. Tool scanning uses reflection; method information may be
  lost in trimmed applications.

toplam IL2026/IL3050 satiri: 4  (iki essiz tani, her biri iki kez)
```

İki iddia da tuttu: uyarı **çıktı** (bastırılmamış) ve hem
`RequiresUnreferencedCode` hem `RequiresDynamicCode` gerekçesini **adıyla**
taşıyor. Yansıma yolu meşru bir kaçış kapısı olarak duruyor ama bedeli
çağırana iletiliyor — MT-PKG-060'ın sıfır uyarısıyla karşıtlığı tam.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-062 — AOT bayrağı paket bazında doğru

**Gerçek sonuç**
```
grep -l "TraconAotCompatible>false" src/*/*.csproj  -> 12 proje
  Tracon · Tracon.AspNetCore · Tracon.Cli · Tracon.Client · Tracon.Generators
  Tracon.Mcp · Tracon.SqlServer · Tracon.Sqlite · Tracon.Templates
  Tracon.Testing · Tracon.UI · Tracon.Workflows

src/ altinda 22 proje -> bayragi devralan (AOT uyumlu) 10 proje
  bunlardan Tracon.Sql.Shared paket degil -> AOT uyumlu PAKET: 9
```

⚠️ **Beklenen sonuç DÜZELTİLDİ** (skill §1.1 istisnası). Spec 10 muafiyet ve
8 uyumlu paket bekliyordu. Üçü de ölçüldü:

1. **`Tracon.Cli`** muafiyeti **gerekçeli**: *"Global tools ship as IL, not
   native code; nothing here promises AOT."*
2. **`Tracon.Client`** muafiyeti **gerekçeli**: 42 satır × 3 TFM için
   146 `IL2026`/`IL3050`/`IL2075` tanısı ölçülmüş; K-006'nın katman temelli
   olduğuna ve `Tracon.AspNetCore` ile `Tracon.UI`'nin de kendi gerekçeleriyle
   muaf olduğuna atıf yapıyor.
3. **`Tracon.Testing.Contracts.Xunit`** bayrak **yazmıyor**; `TraconAotCompatible`
   `src/Directory.Build.props:33`'te boşsa `true`'ya düşüyor, yani bu paket AOT
   vaadi veriyor. Sessiz bir kırılma **değil**: tek başına derlemesi
   `0 Warning(s) · 0 Error(s)` veriyor, çünkü `Directory.Build.targets:16`
   bayrak `true` iken `IsAotCompatible=true` atıyor ve trim/AOT analyzer'ları
   açıyor. Vaat analyzer ile destekleniyor. Kardeşi `Tracon.Testing`'in muafiyet
   gerekçesi (yansımayla anonim tip okuma) bu pakete uymuyor.

📌 **Dangling atıf.** Spec "`AGENTS.md` **Sekiz paket uyumludur** der" diyor;
bugünkü `AGENTS.md`'de o cümle **yok** (tek AOT geçişi satır 164, "AOT
muafiyeti"nin alan dosyasında yaşadığını söylüyor). Sayı artık koddan
türetiliyor, dokümandan değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-070 — Yerel feed kurulur ve şablon yüklenir

**Gerçek sonuç**
```
~/tracon-local-feed'e kopyalanan nupkg: 20
dotnet nuget add source ~/tracon-local-feed -n tracon-local
  -> "Package source with Name: tracon-local added successfully."
  2. tracon-local [Enabled]  /Users/farukatasoy/tracon-local-feed

dotnet new install "Tracon.Templates::*-*" --add-source ~/tracon-local-feed
  -> Success: Tracon.Templates::0.0.0-preview.0.789 installed

dotnet new list tracon --columns-all
Template Name                        Short Name  Language  Type     Author  Tags
Tracon control plane (ASP.NET Core)  tracon-api  [C#]      project  Tracon  Web/Tracon/AI/Agents
```

Dört iddia da tuttu: kısa ad `tracon-api`, şablon adı
`Tracon control plane (ASP.NET Core)`, dil `C#`, tip `project`.

📌 **Tip sütunu varsayılan görünmüyor.** `dotnet new list tracon` çıktısı
Type sütununu basmaz; `--columns-all` gerekir. Spec'in komutu tipi
doğrulayamaz. Ölçüm `--columns-all` ile yapıldı.

📌 **`::` ayırıcısı kullanımdan kaldırılıyor.** SDK uyarısı:
*"The colon separator '::' has been deprecated in favor of the at symbol '@'
… this means `Tracon.Templates@*-*`"*. Spec ve `00-INDEKS.md` §2.3 hâlâ `::`
kullanıyor. Bugün çalışıyor; kapanış oturumu `@`'e çevirmeli.

🔧 **Ortam değişikliği — kullanıcı onayı alındı (2026-09-16).** Bu case üç
kalıcı kayıt oluşturur (`~/tracon-local-feed/`, `tracon-local` NuGet kaynağı,
`Tracon.Templates` şablonu). Üçü de **tur bitince geri alınacak**; adımlar
spec'e `## Koşum sonrası temizlik` olarak yazıldı ve `DEVIR.md` §9'a borç
olarak kaydedildi.

🚨 **Case'in komutu repo'nun `NuGet.config`'ini BOZDU — düzeltildi.**
`dotnet nuget add source` en yakın `NuGet.config`'e yazar. Komut repo kökünden
koşulduğu için repo'nun kendi dosyasına yazdı:

```diff
-<?xml version="1.0" encoding="utf-8"?>
+﻿<?xml version="1.0" encoding="utf-8"?>
     <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
+    <add key="tracon-local" value="/Users/farukatasoy/tracon-local-feed" />
```

Bir kaynak satırı **ve bir BOM** eklendi. `00-INDEKS.md` §2.3 ("repo'nun
`NuGet.config` dosyası değiştirilmez") ve skill §1.4 madde 3 bunu yasaklar.

`git checkout -- NuGet.config` ile geri alındı; kaynak repo **dışından**
(`cd ~`) yeniden eklenerek kullanıcı düzeyine (`~/.nuget/NuGet/NuGet.Config`)
taşındı. Doğrulandı: repo `git status` temiz, kaynak kullanıcı config'inde,
ve repo kökünden `dotnet nuget list source | grep tracon-local` **boş** —
repo'nun `<clear />` kuralı kullanıcı kaynaklarını bastırıyor, yani istenen
yalıtım korunuyor. Şerit worktree'sinin `NuGet.config`'i hiç etkilenmedi.

Spec'teki komut repo dışında koşacak biçimde düzeltildi (§1.1 istisnası).
Bu, `MT-PKG-041` ve `MT-PKG-077` ile aynı sınıftır: **ölçen komutun kendisi
hatalı**. Farkı, bunun yan etkisinin repo'ya yazması.

🔧 **Kalıntı temizlendi.** Koşum öncesi küresel listede Ağustos turundan kalma
`AgentPrism.Templates 0.0.0-preview.0.248` duruyordu — Faz 162 öncesi ürün
adıyla. Kullanıcı onayıyla kaldırıldı:
`Success: AgentPrism.Templates::0.0.0-preview.0.248 was uninstalled.`
Kalıntının sebebi spec'te koşum sonrası temizlik bölümünün **olmamasıydı**;
bu turda o boşluk kapatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-071 — Varsayılan şablon derlenir ve çalışır

**Gerçek sonuç**
```
dotnet new tracon-api -o . --TraconVersion 0.0.0-preview.0.789  -> cikis 0
uretilen: Program.cs · Tools/OrderTools.cs · appsettings.json · README.md
          .gitignore · appsettings.Development.json
          Properties/launchSettings.json · varsayilan.csproj

dotnet build -c Release -> cikis 0 · 0 Warning(s) · 0 Error(s)

curl /tracon/api/meta -> HTTP 200
{"version":"0.0.0-preview.0.789","prefix":"/tracon",
 "authentication":{"allowRemoteAccess":false,"requiresBearerToken":false,...},
 "storage":{"persistent":false,"agentDefinitionStore":"InMemoryAgentDefinitionStore",
            "runStore":"InMemoryRunStore","sessionStore":"InMemorySessionStore",...},
 "roles":{"canRead":true,"canOperate":true,"canAdminister":true}}

curl /tracon/api/agents -> support / "Support Assistant" / origin:"Code"
```

**Arayüz (Playwright ile doğrulandı).** `/tracon` açıldı, başlık `Tracon`,
dashboard çizildi, kenar çubuğunda `In-memory storage — Data is lost when the
process exits.` rozeti ve `v0.0.0-preview.0.789` görünüyor. `/tracon/agents`
ekranında **boş liste değil**, `support` satırı var: ad `Support Assistant`,
kaynak `code` ("Declared in code by source 'code'. Read only."), model
`WRITE_MODEL_NAME_HERE` · `openai`, tool sayısı `1`.

Tasarım kuralı #1 kanıtlandı: **hiçbir bağlantı dizesi ve API anahtarı
tanımlı değilken** uygulama ayağa kalktı, `persistent:false` ile çalıştı.

📌 **İki küçük sapma, ikisi de iddiayı bozmuyor:**

1. Spec "`/tracon` arayüzü açılır ve … `support` agent'ını gösterir" diyor;
   `/tracon` kökü **Dashboard**'a düşüyor ve orada agent listesi yok. Agent
   `/tracon/agents` ekranında görünüyor. İddia karşılanıyor, rota adı gevşek.
2. Spec üretilen `appsettings.json`'ın `ConnectionString` **ve** `ApiKey`
   alanlarını boş dize taşımasını bekliyor. Ölçülen dosyada `ApiKey: ""` var,
   `ConnectionString` **hiç yok** — varsayılan `--persistence memory` olduğu
   için kalıcılık bölümü üretilmiyor. Hiçbir gerçek `secret` yok; iddianın özü
   (sızıntı yok) karşılanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-004 — Arayüzün kendi CSP'si, sevk edilen inline script'i engelliyor

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-PKG-071 (arayüz adımı) |
| **Sınıf** | Sevk edilen ölü kod + her sayfa yüklemesinde konsol hatası |

**Belirti.** Arayüzün açıldığı **her** sayfada bir konsol hatası:

```
[ERROR] Executing inline script violates the following Content Security Policy
directive 'script-src 'self''. Either the 'unsafe-inline' keyword, a hash
('sha256-qXVpEEsBZN0tkb/4NNCKKYCX/3nqPrSN9lCJAU0JadE='), or a nonce
('nonce-...') is required to enable inline execution. The action has been
blocked. @ http://localhost:5081/tracon:41
```

`/tracon` ve `/tracon/agents` ikisinde de tekrarlandı.

**Kök neden.** İki taraf birbiriyle çelişiyor:

- `src/Tracon.UI/wwwroot/index.html:42` bir **inline `<script>`** taşıyor.
  Kendi yorumu amacını söylüyor: *"Runs BEFORE the stylesheet, so a stored
  preference paints its own ground rather than the default one."* Depodan
  `tracon.theme` okuyup `document.documentElement.dataset.theme`'i yazıyor.
- `src/Tracon.UI/Internal/EmbeddedUiProvider.cs:50` gönderilen başlığa
  `script-src 'self'` yazıyor. `style-src` **`'unsafe-inline'` taşıyor**,
  `script-src` taşımıyor; nonce ya da hash mekanizması da yok
  (`grep -rniE "nonce|sha256-" src/Tracon.UI` → yalnız tarayıcının önerdiği
  hash, kodda karşılığı yok).

Sonuç: script **hiç çalışmıyor**. Var oluş sebebi — stil sayfasından önce
depolanmış temayı boyamak — tamamen boşa çıkıyor. `theme.ts` aynı yazımı modül
yüklenirken **daha geç** yapıyor, yani uygulama bozulmuyor ama varsayılan
olmayan tema seçmiş her tüketici her sayfa yüklemesinde tema sıçraması
görüyor, artı konsolunda kalıcı bir hata.

**Neden otomatik test yakalamadı.** `tests/Tracon.Ui.E2ETests/` içinde
konsol hatası ya da sayfa hatası denetleyen **hiçbir** iddia yok
(`grep -rn "PageError\|OnConsole" tests/Tracon.Ui.E2ETests/` → boş). 79 E2E
testi yeşil geçiyor çünkü hiçbiri konsola bakmıyor. Bu, skill §5'in
"sessiz bir JS hatası 'Geçti' gibi görünür" uyarısının canlı örneğidir.

**Kapanışta karar gerektirir:** ya inline script'in hash'i CSP'ye eklenir
(`'sha256-qXVpEEsBZN0tkb/4NNCKKYCX/3nqPrSN9lCJAU0JadE='`), ya bir nonce
üretilir, ya da script kaldırılıp tema sıçraması kabul edilir. Ek olarak
E2E setine konsol-hatası kapısı eklenmesi bu sınıfı kapatır.

**✅ KAPANDI 2026-09-18 (Aile N; `HATA-S2-002` ile aynı kusur).** Üç seçenekten
**hash** seçildi (K-824) — ama **sabit yazılmış** hash değil: `EmbeddedUiProvider`
hash'i sevk edilen shell'in kendi metninden, shell'i kurarken hesaplıyor.

Sabit hash reddedildi çünkü script'in ikinci bir kopyasıdır ve onu güncel tutan
hiçbir şey yoktur: script ilk değiştiğinde yeniden **sessizce** engellenirdi —
sayfa onsuz da çalıştığı için kimse fark etmezdi. Bu, kusurun bugünkü hâlinin
birebir tekrarı olurdu. Nonce reddedildi çünkü yanıt başına değişmek zorundadır,
yani shell'in istek başına render edilmesini ve önbelleklenmiş/ETag'li
dokümanın kaybını gerektirir. Script'i kaldırmak reddedildi çünkü var olma
sebebi (stil sayfasından önce doğru zemini boyamak) hâlâ geçerli.

🚨 **Kaydın istediği konsol-hatası kapısı eklendi ve ikinci bir katman
kazandı.** İki test var ve ikisi de düzeltmeden önce kırmızıydı:
`Shell_loads_with_no_console_error` gerçek tarayıcıda konsolu dinliyor
(ölçülen mesaj kayıttaki ile birebir, önerdiği hash dahil);
`Shell_CSP_allows_every_inline_script_it_ships_by_hash` tarayıcısız çalışıyor ve
**sunulan HTML'den** hash'i yeniden hesaplayıp CSP'de arıyor — yani bayatlayamaz,
ki sabit hash'in başarısız olma biçimi tam olarak buydu.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ iki test de kırmızı; tarayıcı testi CSP ihlalini birebir kaydettiği metinle raporladı |
| Sınıf taraması | ☑ `grep -rn "script-src" src/` → **tek** CSP tanımı (`EmbeddedUiProvider.cs:50`); `grep -rln "<script>" src/**.html` → tek sevk edilen shell. `HATA-S1-004` ve `HATA-S2-002` iki ayrı yüzey değil, **aynı** yüzeyin iki ayrı şeritte gözlenmiş hâli. Gömülü widget'ın kendi HTML'i yoktur; barındıran sayfanın CSP'si geçerlidir |
| `script-src` | `'unsafe-inline'` **girmedi** ve test bunu ayrıca zorluyor — hash bu script'i çalıştırır, anahtar kelime gelecekteki her enjekte script'i de çalıştırırdı |

---

## MT-PKG-072 — Şablon gerçek bir model çağrısı yapar

**Gerçek sonuç**

**Adım 4 (gerçek çağrı) tam geçti:**
```
sed WRITE_MODEL_NAME_HERE -> gpt-5.4-mini · build cikis 0
POST /tracon/api/agents/support/run  {"message":"ORD-1001 siparisim nerede?"}
  -> SSE akisi · runId 01a0aab7-d797-7a5f-8838-9d13cc7eca3a
  ORD-1001 gecis sayisi      : 2
  get_order_status cagri sayisi: 1   (tam bir kez)

GET /tracon/api/runs
  status      : "Completed"
  modelId     : "gpt-5.4-mini"   modelProvider: "openai"
  usage       : in 216 / out 25 / total 241
  eventCount  : 26   error: null
```

🚨 **Adım 1 KALDI: hata metni model adını taşımıyor.**

Case'in var oluş sebebi "yer tutucunun gerçekten fark edildiğini kanıtlamak".
İki sıralamada da fark edilmiyor:

```
(a) anahtar YOKKEN (case'in kendi sirasi):
HTTP 400 {"title":"Agent compilation failed",
 "detail":"Agent 'support' could not be compiled: No model provider named
  'openai' is registered. ... For OpenAI, call
  `builder.AddTracon().UseOpenAI(apiKey)`."}
-> Saglayici kaydindan sikayet ediyor; model adi GECMIYOR.

(b) anahtar VARKEN (ayirt edici olcum, bu turda ek olarak kosuldu):
HTTP 200, SSE acildi, sonra
event: error
data: {"type":"ProviderInvocationException",
       "message":"The model provider request failed."}
-> Yine model adi GECMIYOR.
```

İki durumda da uygulama **çökmedi** (`/tracon/api/meta` → 200) ✅ — iddianın
o yarısı tutuyor. Model adını taşıma iddiası tutmuyor → `HATA-S1-005`.

⚠️ **Spec iki noktada düzeltildi** (skill §1.1 istisnası):
1. Yer tutucu adı `MODEL_ADINI_BURAYA_YAZIN` → **`WRITE_MODEL_NAME_HERE`**
   (Faz 162 dil göçü; ölçüldü: `~/tracon-manuel/varsayilan/Program.cs:45`).
2. Adım 3 `dotnet user-secrets set` diyordu → **ortam değişkeni**
   (`export Tracon__Providers__OpenAI__ApiKey=...`). Skill §1.2 `user-secrets`
   yazımını yasaklar; depo makine genelinde tektir. Bu koşumda anahtar
   `user-secrets list` ile **okundu** (164 karakter), hiçbir dosyaya yazılmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19)**
`HATA-S1-005` kapandı (kullanıcı kararı: şablon açılışta hızlı düşer).
Şablon yer tutucuyu hâlâ **tam olarak bir kez** taşıyor (`grep -c` → `1`),
ama artık bir sağlayıcı yapılandırılmışken açılışta duruyor ve düzenlenecek
dosyayı adıyla söylüyor:

```
Program.cs still carries the model-name placeholder ('WRITE_MODEL_NAME_HERE').
Replace it with a model your provider serves today — ...
```

🚨 **Koşulsuz bir `throw` mevcut bir sözleşmeyi kırdı.**
`TemplateRunTests.Default_combination_starts_up_without_setup_and_returns_the_catalog`
şablonun kendi "sıfır sürpriz" kuralını ölçüyor: anahtar yokken uygulama yine
açılır ve kataloğu döndürür. Kontrol bu yüzden **yalnız sağlayıcı
yapılandırıldığında** koşar — anahtar yokken agent zaten modele ulaşamaz.
İki taraf da test altında: 57/57 paket testi yeşil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-005 — Şablonun kendi yer tutucusu teşhis edilemeyen bir ilk koşum hatası üretiyor

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-PKG-072 |
| **Sınıf** | İlk beş dakika · hata teşhisi |

**Belirti.** Şablon `Model = "WRITE_MODEL_NAME_HERE"` ile geliyor — bu
**bilinçli** bir yer tutucudur. Tüketici anahtarını verip değiştirmeyi
unutursa aldığı tek şey:

```
{"type":"ProviderInvocationException","message":"The model provider request failed."}
```

Model adı yok, sebep yok, korelasyon kimliği yok.

**Sunucu tarafında ayrıntı TAM olarak duruyor** (aynı koşumun logu):
```
Tracon.ForeignProviderInvocationException: HTTP 404 (invalid_request_error: model_not_found)
  The model `WRITE_MODEL_NAME_HERE` does not exist or you do not have access to it.
fail: Tracon.ModelProvider[0]
  Model provider openai failed during streaming invocation for model WRITE_MODEL_NAME_HERE.
fail: Tracon.RunRecordingAgent[0]
  Run 01a0aab8-471a-722d-a7e0-474e0efc17ff failed. (ref: 3fca8b40)
fail: Tracon.AgentEndpoints[0]
  Streaming agent run ... failed. (ref: ebdcd712)
```

**Kök neden — bilinçli bir redaksiyon.**
`src/Tracon.Core/Models/ProviderFailureNormalizer.cs:12`
`UpstreamMessage = "The model provider request failed."` sabitini tanımlar ve
`IsKnownSafe(...)` bir **izin listesi** tutar (`TraconContentFilteredException`,
`TraconCompilationException`, `TraconProviderUnavailableException`, …).
OpenAI SDK'sının `ClientResultException`'ı (`model_not_found`) bu listede
olmadığı için normalize ediliyor. Tasarım savunulabilir — yabancı sağlayıcı
ayrıntısı istemciye sızmamalı. Bedeli, şablonun **kendi sevk ettiği**
yer tutucusunun teşhis edilemez hâle gelmesi.

**İki şey güvenle geçirilebilirdi ve geçmiyor:**
1. **Model adı** — tüketicinin kendi yazdığı yapılandırma değeri, sağlayıcı
   `secret`'i değil.
2. **`ref:` korelasyon kimliği** — zaten üretiliyor ve loglanıyor
   (`AgentEndpoints.cs:1297`), ama SSE hata olayına konmuyor; tüketici kendi
   hatasını kendi logundaki satırla eşleştiremiyor.

**Kapanışta seçenekler:** `model_not_found` için ayrı bir güvenli tip açmak ·
`ref:` kimliğini hata gövdesine koymak · ya da en ucuzu, şablonun açılışta
yer tutucu hâlâ duruyorsa **hızlı düşmesi** (`WRITE_MODEL_NAME_HERE` görürse
anlaşılır bir başlangıç hatası).

---

## MT-PKG-073 — Kalıcılık seçenekleri doğru paket ve kod üretiyor

**Gerçek sonuç**

| Varyant | csproj paketleri | `Program.cs` | `appsettings.json` | `#if` | build |
|---|---|---|---|---|---|
| `postgres` | yalnız `Tracon` | `tracon.UsePostgreSql(postgreSql);` | yalnız `"PostgreSql"` | yok | ☑ |
| `sqlite` | `Tracon` + `Tracon.Sqlite` | `tracon.UseSqlite(sqlite);` | yalnız `"Sqlite"` | yok | ☑ |
| `sqlserver` | `Tracon` + `Tracon.SqlServer` | `tracon.UseSqlServer(sqlServer);` | yalnız `"SqlServer"` | yok | ☑ |

Altı iddia da tuttu: `postgres` ek paket **almıyor** (PostgreSQL meta pakete
dâhil, MT-PKG-030 ile tutarlı), diğer ikisi kendi paketini ekliyor, her
varyantın `appsettings.json`'ı yalnız **kendi** sağlayıcısının bölümünü taşıyor,
hiçbirinde `#if` / `//#if` kalıntısı yok ve üçü de derleniyor. Üçünde de
`ConnectionString` değeri boş dize.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-074 — Sağlayıcı seçenekleri doğru paket ve kod üretiyor

**Gerçek sonuç**

| Varyant | Ek paket | `Program.cs` | build |
|---|---|---|---|
| `openai` | **yok** (meta paketten) | `tracon.UseOpenAI(openAi);` | ☑ |
| `anthropic` | `Tracon.Anthropic` | `tracon.UseAnthropic(anthropic);` | ☑ |
| `google` | `Tracon.Google` | `tracon.UseGoogle(google);` | ☑ |
| `azure` | `Tracon.Azure` | `tracon.UseAzureOpenAI(azureOpenAI);` | ☑ |

Dört iddia da tuttu. Her varyant **tek bir** `Use*` çağrısı taşıyor ve dördü de
derleniyor. Azure varyantının derlenmesi **kimlik bilgisi istemedi** — case'in
kapsamı üretim ve derleme; gerçek Azure `run`'ı `06-SAGLAYICI-DIGER.md`'de
atlanır (Azure kimliği yok).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-075 — `--ui false` arayüzü hiç bağlamaz

**Gerçek sonuç**
```
dotnet new tracon-api --ui false   -> cikis 0
grep -c "UseUI" Program.cs         -> 0
csproj                             -> yalniz <PackageReference Include="Tracon" />
dotnet build -c Release            -> cikis 0

meta   : 200
arayuz : 404
logda arayuzle ilgili hata: yok
```

Dört iddia da tuttu: `UseUI` çağrısı üretilmedi, HTTP API **çalışmaya devam
etti** (200), arayüz kökü 404 döndü ve uygulama çökmedi. Arayüz istemeyen
tüketici için kontrol düzlemi ayakta kalıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-076 — Şablon sürüm sabitlemesi çalışıyor

**Gerçek sonuç**
```
1) dotnet new tracon-api --skip-restore  -> cikis 0
   csproj: <PackageReference Include="Tracon" Version="*-*" />
   TRACON_TEMPLATE_PACKAGE_VERSION yer tutucusu: 0 kez (kalmamis)
   ciktida "Restor" gecen satir: 0  (--skip-restore gercekten atladi)

2) dotnet new tracon-api --TraconVersion 99.99.99 --skip-restore
   dotnet restore -> cikis 1
   NU1102: Unable to find package Tracon with version (>= 99.99.99)
   NU1102:   - Found 0 version(s) in nuget.org
   NU1102:   - Found 1 version(s) in ap-yerel [ Nearest version: 0.0.0-preview.0.789 ]
```

Üç iddia da tuttu. Hata mesajı hem paket adını (`Tracon`) hem istenen sürümü
(`99.99.99`) açıkça yazıyor **ve** en yakın mevcut sürümü gösteriyor —
beklenenden iyi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-077 — Şablon `secret` sızdırmıyor

**Gerçek sonuç**
```
anahtar deseni taramasi (*.json · *.cs · *.csproj, bin/obj haric)
  -> hicbir satir donmedi
--- tarama bitti ---

appsettings.json:  "OpenAI": { "ApiKey": "" }        (bos dize)
csproj:            <UserSecretsId>tracon-starter-8CF6C53D-0EAD-4C8F-AD96-B4E1BBAA4144</UserSecretsId>
.gitignore:        bin/ · obj/ · *.user · appsettings.*.local.json
                   Tracon.LocalReference.md
```

Dört iddia da tuttu: tarama temiz, `ApiKey` boş dize, `.csproj` bir
`UserSecretsId` taşıyor ve `.gitignore` `secret` deseni içeriyor
(`appsettings.*.local.json` ve `*.user`).

📌 **Ön koşul sapması sorun çıkarmadı.** Case "MT-PKG-072 koşuldu
(`user-secrets` tanımlandı)" diyor; bu turda §1.2 gereği `user-secrets`
yazılmadı, ortam değişkeni kullanıldı. Fark etmedi: `UserSecretsId`'yi
**şablonun kendisi** üretiyor, `dotnet user-secrets init` gerekmiyor.

⚠️ **Spec komutu DÜZELTİLDİ** (skill §1.1 istisnası). `.gitignore` grep'i
`appsettings\.\*\.json` arıyordu; dosyadaki satır `appsettings.*.local.json`
olduğu için **eşleşmiyordu** ve komut sıfır satır dönüyordu. Case geçtiği
hâlde komutu "desen yok" diyordu — MT-PKG-041'in `grep -P` sorunuyla aynı
sınıf: ölçen komut iddiayı ölçmüyor. Desen `*.user` de kapsayacak şekilde
genişletildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-080 — Tüketicinin kaydı her zaman kazanır

**Gerçek sonuç**
```
BenimRunStore.cs -> IRunStore'un 15 uyesi de NotSupportedException ile uygulandi
  (imzalar src/Tracon.Abstractions/Runs/IRunStore.cs'ten birebir cikarildi)

dotnet run -c Release -> cikis 0
once:  BenimRunStore
sonra: BenimRunStore
```

İki iddia da tuttu. Tüketicinin kaydı **Tracon'den önce** yapıldığında Tracon
onu ezmedi (`TryAdd*` sözleşmesi), **sonra** yapıldığında son kayıt kazandı.
Hiçbir adımda istisna atılmadı. Kütüphane tüketicinin kararını her iki sırada
da koruyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-081 — Hiçbir `Use*` çağrılmadan kurulum ayakta kalır

**Gerçek sonuç**
```
dotnet run -c Release -> cikis 0
IRunStore     : InMemoryRunStore
ISessionStore : AuditingSessionStore
IToolRegistry : ToolRegistry
kurulum tamam

uyari / baglanti denemesi izi: yok
```

Beş iddia da tuttu ve 2026-08-15'te düzeltilen tip adlarının üçü de birebir
çıktı: `ISessionStore` denetim izi dekoratörüyle sarılı (`AuditingSessionStore`),
`IToolRegistry` `InMemory` öneki taşımıyor. Tasarım kuralı #1'in tek doğrudan
testi: hiçbir `Use*` çağrılmadan çekirdek servisler çözümlendi, hiçbir bağlantı
denenmedi, hiçbir uyarı loglanmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-082 — İki kalıcılık sağlayıcısı aynı anda verilirse

**Gerçek sonuç**
```
Program.cs:22  tracon.UseSqlite(sqlite);
Program.cs:29  tracon.UsePostgreSql(postgreSql);      <- SON cagri
build cikis: 0

GET /tracon/api/meta
  "storage": { "persistent": true,
               "agentDefinitionStore": "SqlAgentDefinitionStore",
               "runStore":  "SqlRunStore",
               "sessionStore": "SqlSessionStore",
               "jobStore": "SqlJobStore" }

acilis logu:
warn: Tracon.MigrationHostedService[0]
      Tracon has more than one persistence provider registered: SQLite,
      PostgreSQL. The last registration wins and PostgreSQL is currently in
      use. Call only one.

ardindan: CREATE SCHEMA IF NOT EXISTS mt_s1_cift ... (PostgreSQL migration'lari)
```

Dört iddia da tuttu:
- Uygulama **çökmedi**, ayağa kalktı ✅
- `meta` **tek** aktif kalıcılık bildirdi (tek `Sql*` depo kümesi) ✅
- Aktif olan **son** çağrılan sağlayıcı: `UsePostgreSql` (satır 29) ✅ —
  uyarı bunu açıkça söylüyor ("PostgreSQL is currently in use")
- Log'da uyarı **var** ve iki sağlayıcıyı da adıyla sayıyor ✅

`README.md:252` ve `src/Tracon.Sqlite/README.md:103` iddiası
("last registration wins and a warning is logged at startup") **kodla
doğrulandı**: uyarı `src/Tracon.Sql.Shared/Migrations/MigrationHostedService.cs:129`
içinde yaşıyor.

📌 **Uyarı iki kez basılıyor.** Aynı satır açılışta iki defa görünüyor. Zararsız
ama gürültülü; kapanış oturumu tek basıma indirebilir.

**Sapma:** Case `dotnet user-secrets set` diyor; skill §1.2 gereği iki bağlantı
dizesi de **ortam değişkeni** ile verildi. Şema `mt_s1_cift` şerit kapsamında
açıldı ve case bitince düşürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-090 — Public API kapısı temiz ağaçta sıfır uyarı verir

**Gerçek sonuç**
```
git status --short -> bos
dotnet build Tracon.slnx -c Release --no-incremental -> cikis 0 · 61 sn
grep -c "warning RS0" -> 0
Build succeeded. 0 Warning(s) · 0 Error(s)
```

Kayıtlı bir yüzeyde kapı sessiz. 2026-08-16 ölçümüyle birebir aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-091 — Kayıtsız yeni bir public üye derlemeyi kırar

**Gerçek sonuç**
```
ITraconBuilder.cs  += void ProbeUnregisteredMember();
TraconBuilder.cs   += public void ProbeUnregisteredMember() { }

dotnet build src/Tracon.Core/Tracon.Core.csproj -c Release -> cikis 1
error RS0016: Symbol 'Tracon.ITraconBuilder.ProbeUnregisteredMember() -> void'
  is not part of the declared public API
  [::TargetFramework=net8.0]  [::TargetFramework=net9.0]  [::TargetFramework=net10.0]
RS0016 satir sayisi: 6  (3 TFM × 2)
```

Kapı **gerçekten** çalışıyor: derleme kırıldı ve tanı eklenen üyenin **tam
imzasını** adıyla söyledi. 2026-08-16 ölçümüyle birebir aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-092 — `PublicAPI.Unshipped.txt`'e eklenince kapı tekrar yeşil

**Gerçek sonuç**
```
src/Tracon.Core/PublicAPI.Unshipped.txt +=
  Tracon.ITraconBuilder.ProbeUnregisteredMember() -> void

dotnet build src/Tracon.Core/Tracon.Core.csproj -c Release -> cikis 0
Build succeeded. 0 Warning(s) · 0 Error(s)
RS00xx tanisi: 0
```

Kapı kayıtlı üyeyi engellemiyor. Üç dosya `git checkout --` ile geri alındı;
`git status --short` **boş**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-093 — Sadeleşen aşırı yüklemeler paketlenmiş tüketicide görünür ve çalışır

**Gerçek sonuç**
```
ConsumerProbe: Tracon.Anthropic + Tracon.Mcp 0.0.0-preview.0.789 (yerel feed)
Program.cs yalniz PUBLIC yuzeyi kullanir:
  AnthropicChatClientFactory.CreateClient(new AnthropicProviderOptions { ApiKey = "k" })
  AnthropicChatClientFactory.FromClient(client, defaultModel: "claude-sonnet", loggerFactory: null)
  services.AddTracon().UseAnthropic("k").UseMcp();      <- bare asiri yukleme

dotnet build -c Release -> cikis 0 · 0 Warning(s) · 0 Error(s)
dotnet run  -c Release --no-build ->
  Consumer probe OK: Tracon.AnthropicChatClientFactory
```

Üç iddia da tuttu ve 2026-08-16 ölçümüyle birebir aynı çıktı üretildi.
RS0026 sadeleştirmeleri `ProjectReference` ile değil **gerçek `.nupkg`** ile
tüketildiğinde de çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-094 — `internal`'a çekilmiş bir tip paketlenmiş tüketicide görünmez, arayüzü görünür

**Gerçek sonuç**
```
SurfaceProbe: Tracon 0.0.0-preview.0.789 (yerel feed)
~/.nuget/packages/tracon* silindi (case on kosulu) - 15 dizin

adim 2: var store = new Tracon.InMemoryRunStore();
  -> cikis 1
  error CS0122: 'InMemoryRunStore' is inaccessible due to its protection level

adim 3: Tracon.IRunStore? store = null;  (kullanilarak)
  -> cikis 0 · 0 Warning(s) · 0 Error(s)
```

İki iddia da tuttu. Faz 96'nın 96 tipi `internal`'a çekmesi **gerçek
tüketicide** de tutuyor: uygulama tipi görünmüyor, arayüz görünür kalıyor.

📌 **Adım 3'ün komutu uyarı üretir.** Spec `Tracon.IRunStore? store = null;`
yazıyor ve sıfır uyarı bekliyor; bu satır tek başına `CS0219` (atanmış ama
kullanılmamış değişken) verir. Ölçüm, değişkeni gerçekten okuyan bir satırla
yapıldı. Spec'e dokunulmadı — iddia (arayüz görünür) her iki biçimde de
karşılanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### 🚨 NOT-02 — `MT-PKG-027` sonraki case'lerin feed'ini kirletiyor

Bu bir **ürün kusuru değil, set tasarımı kusurudur** ve `MT-PKG-093`/`094`'ün
ilk koşumunu geçersiz kıldı.

`MT-PKG-027` geçici bir `v1.0.0-preview.1` etiketi atıp paketliyor. `-o` ile
başka bir dizin verilse bile çıktı **`artifacts/package/release/` içine de**
düşüyor (ölçüldü: `MT-PKG-020`'nin `-o` ile koşan pack'i de oraya yazdı).
`MT-PKG-093` ve `094` tam olarak o dizini yerel feed olarak kullanıyor ve
numara sırasında `027`'den **sonra** geliyor.

🔍 **Ölçümün sınırı:** 20 paketin tamamı 17:24:28–17:24:50 arasında tek bir
kümede oluştu; etiket penceresi içindeydi ama çözüm genelinde pack'i hangi
adımın tetiklediği **izole edilemedi**. Kanıtlanan şey: dizin birikimlidir,
`027` penceresinde iki sürüm ailesi oluşur ve bu `093`/`094`'ü kırar.

Ölçülen sonuç (ilk koşum, kirli feed):
```
feed'de 39 nupkg · IKI surum ailesi: 0.0.0-preview.0.789 ve 1.0.0-preview.1
Tracon.Abstractions YALNIZ 1.0.0-preview.1 olarak duruyordu

warning NU1603: Tracon.Core 0.0.0-preview.0.789 depends on
  Tracon.Abstractions (>= 0.0.0-preview.0.789) but ... 0.0.0-preview.0.789 was
  not found. Tracon.Abstractions 1.0.0-preview.1 was resolved instead.
```

Yani iki case, test edilmesi gereken sürümden **farklı** bir `Abstractions`
ikilisine karşı ölçülüyordu. Temiz bir feed (yalnız `0.0.0-preview.0.789`,
20 paket) ile yeniden koşuldu ve ikisi de **0 uyarı** ile geçti; yukarıdaki
kayıtlar temiz koşumdur.

**Spec düzeltildi:** `MT-PKG-027`'ye atlanmaz bir temizlik adımı eklendi —
`rm -f artifacts/package/release/*1.0.0-preview.1.*`.

---

## MT-PKG-095 — Public yüzey taban çizgisi paket başına tip sayısını yakalar

**Gerçek sonuç**
```
1) degismemis baseline ile   -> cikis 0 · total 1 · failed 0
2) Tracon.Anthropic=5 -> =4  -> cikis 2 · total 1 · failed 1

failed PublicSurfaceBaselineTests.Public_type_count_matches_the_checked_in_baseline_per_package (87ms)
  The public surface baseline is stale.
  + Tracon.Anthropic: 5 public types, baseline allows 4
  A count that only shrank is fixed by refreshing:
    TRACON_PUBLIC_SURFACE_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release
  at PublicSurfaceBaselineTests.cs:87

3) git checkout -- ... -> Tracon.Anthropic=5 geri geldi
```

Kapı gerçekten kırılıyor ve mesaj **hangi paket** (`Tracon.Anthropic`),
**gerçek sayı** (5) ve **taban çizgi** (4) üçünü birden veriyor; üstüne
düzeltme komutunu da yazıyor. Sessizce büyüyen bir yüzey buradan geçemez.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-096 — Takipsiz packable paket beyan kapısında yakalanır

**Gerçek sonuç**
```
PublicAPI.Shipped.txt ve PublicAPI.Unshipped.txt gecici olarak tasindi

failed PublicApiTrackingDeclarationTests.Every_src_project_declares_its_public_API_tracking_status (30ms)
  Tracon.Core: neither carries both PublicAPI.Shipped.txt and
  PublicAPI.Unshipped.txt, nor sets TraconPublicApiTrackingEnabled=false in its
  own .csproj. Add the tracking files, or opt out explicitly.
  at PublicApiTrackingDeclarationTests.cs:77

cikis: 2 · total 1 · failed 1
dosyalar geri tasindi -> git status --short bos
```

Kapı kırıldı ve mesaj **iki seçeneği de** adıyla gösterdi: izleme dosyalarını
ekle **veya** açıkça `TraconPublicApiTrackingEnabled=false` yaz. Takip
dosyası unutulan bir paket sessizce izlenmez kalamıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-097 — Yayın provası bir tag'e hiçbir şey yazmadan yeşil olur

**Gerçek sonuç**
```
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1
  -> cikis 1 · 59 sn

Paketleme calisti (artifacts/package/staging/run-abw_27lw/ altina 1.0.0-preview.1
olarak uretildi: Tracon, .Cli, .UI, .OpenAI, .Azure, .Sqlite, .Testing, ...)
sonra:
❌ CHANGELOG.md içinde '## [1.0.0-preview.1]' bölümü yok veya boş
```

🚨 **Prova YEŞİL DEĞİL — ama doğru sebeple kırmızı.** `CHANGELOG.md` yalnız
`## [Unreleased]` başlığı taşıyor (`grep -n "^## " CHANGELOG.md` → tek satır);
hedef sürüm için bölüm yok. Kapı **fail-closed** davrandı; bu tam olarak
`MT-PKG-102`'nin ayrıca ölçtüğü davranıştır ve doğrudur.

**Case'in diğer iki iddiası TUTTU:**
- `git tag` çıktısı prova öncesi ve sonrası **birebir aynı** — prova depoya
  hiçbir etiket bırakmadı ✅
- `git status --short` prova sonrası **boş** — çalışma ağacına hiçbir şey
  yazılmadı ✅
- Paketler `artifacts/package/staging/run-<id>/` altında kaldı,
  `release/`'e **terfi etmedi** ✅ (kapı kırıldığı için)

`npm publish --dry-run` adımına **sıra gelmedi** — changelog kapısı ondan önce
durdurdu.

⚠️ **Spec'e eksik ön koşul eklendi** (skill §1.1 istisnası): "`CHANGELOG.md`
hedef sürüm için bir `## [<sürüm>]` bölümü taşımalıdır". Ayrıca "`git tag`
yalnız `docs/damitma-oncesi-2026-08` taşıyor" ifadesi düzeltildi — repoda
**iki** arşiv etiketi var (MT-PKG-027'de de ölçüldü).

Yayın hattı için anlamı: **1.0.0-preview.1 provası bugün geçmez**; changelog
bölümü yazılmadan yayın adımına geçilemez.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19)**
`HATA-S1-007` kapandı (kullanıcı kararı, K-825). Prova artık sürüm bölümünü
bulamazsa `## [Unreleased]`'i okuyor ve **dolu olmasını** şart koşuyor:

```
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1
  ℹ️ Sürüm notları '## [Unreleased]' bölümünden okundu; 'v1.0.0-preview.1'
     etiketlenirken bu başlık '## [1.0.0-preview.1] - <tarih>' olarak
     yeniden adlandırılır (YAYIN-HAZIRLIK Adım 5)
  ✅ 20 paket, sürüm '1.0.0-preview.1'
  ✅ npm publish --dry-run
  ✅ 6 exact-version packed sample ve Native AOT smoke
  çıkış 0
```

**Prova ilk kez yeşil.** Aynı yedeği `github-release` işi de kullanıyor;
yalnız biri kullansaydı prova yeşil, release gövdesi boş olurdu.
🚨 İlk iki deneme ortam yüzünden kırmızıydı, kod yüzünden değil: yerel release
feed'i eski `0.0.0-preview.0.*` paketlerini taşıyordu ve staging dizininde
aynı sürümün farklı içerikli kopyası duruyordu. İkisi de temizlendi.

Bu case `CHANGELOG` düğümünü **bloklanan** taraftan görüyordu; düğüm çözüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-098 — icon.png paketlerin hepsinde tam olarak var

**Gerçek sonuç**
```
denetlenen paket: 20 · ikonu eksik: 0
(temiz feed <scratch>/ap-pack, surum 0.0.0-preview.0.789)
```

Hiçbir "IKON YOK" satırı basılmadı; 20 paketin 20'si de **tam bir** `icon.png`
girdisi taşıyor. `<None Include=...>` kullanımı çapraz hedefli projelerde
tutuyor.

**Sapma — case'in kendi feed'i kullanılamadı.** Case `artifacts/package/release/*.1.0.0-preview.1.nupkg`
üzerinde çalışmasını istiyor ve ön koşulu "MT-PKG-097 bir kez koşmuş" diyor.
MT-PKG-097 changelog kapısında kırıldığı için paketler `staging/`'de kaldı,
`release/`'e terfi etmedi — o glob **hiç eşleşmedi**. Ölçüm, aynı iddiayı
mevcut sürüm ailesi üzerinde yaptı.

Ek olarak `artifacts/package/release/` içinde `Tracon.Abstractions` **yoktu**
(19/20) — NOT-02'deki kirlenmenin kalıntısı. Bu yüzden ölçüm birikimli dizin
yerine temiz `ap-pack` üzerinden yapıldı ve 20/20 doğrulandı.

⚠️ **Beklenen sonuç 19 → 20 güncellendi** (skill §1.1). Bağımsız kanıt:
MT-PKG-099'un kapısı da 20 bekliyor ("expected 20").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-099 — Paket tablosu repo'dan sapınca kapı kırılır

**Gerçek sonuç**
```
1) temiz agacta:            node scripts/check-content.mjs -> cikis 1
   Content check failed with 3 issue(s):
     docs-site/public/llms.txt does not match capabilities.md
     docs-site/public/llms-full.txt does not match capabilities.md
     Exemption list names a page that no longer exists: reference/changelog.md

2) compatibility.md'den `Tracon.Cli` satiri silindi -> cikis 1
   compatibility.md package table has 19 row(s), expected 20
   compatibility.md: `Tracon.Cli` targets net10.0 instead of the default matrix
     and has no row in the package table

3) git checkout -- ... -> cikis 1, yine ayni 3 taban sorunu
```

**Case'in kendi iddiası TUTTU:** tablo repo'dan sapınca kapı yeni ve **adıyla
anlaşılır** bir bulgu üretti (`has 19 row(s), expected 20`) ve satır geri
konunca o bulgu kayboldu. Faz 96'da üç hafta sessiz duran 17/19 sapması
sınıfı artık yakalanıyor. Üstelik kapı ikinci bir bulgu daha ekledi:
`Tracon.Cli`'nin farklı TFM matrisi olduğu hâlde tabloda satırı olmadığını
söyledi.

🚨 **Ama kapı temiz ağaçta ZATEN KIRMIZI** → `HATA-S1-006`.

**Sapma:** `docs-site/node_modules` yoktu; `npm ci` koşuldu (çıkış 0).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-006 — `docs-site` içerik kapısı temiz ağaçta kırmızı

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-PKG-099 |
| **Sınıf** | Doğrulama kapısı · üretilen dosya senkronizasyonu |

Hiçbir değişiklik yapılmadan, `7e3a4de7` donuk kodunda:

```
cd docs-site && node scripts/check-content.mjs   -> cikis 1
Content check failed with 3 issue(s):
  docs-site/public/llms.txt does not match capabilities.md;
    run: node docs-site/scripts/build-agent-map.mjs
  docs-site/public/llms-full.txt does not match capabilities.md;
    run: node docs-site/scripts/build-agent-map.mjs
  Exemption list names a page that no longer exists: reference/changelog.md
```

Üçü de **üretilen/bildirilen dosyanın kaynağından sapması** sınıfında:

1. `llms.txt` ve `llms-full.txt` üreteçten geçirilmemiş — `capabilities.md`
   değişmiş ama türetilmiş iki dosya tazelenmemiş. Kapı düzeltme komutunu
   yazıyor (`node docs-site/scripts/build-agent-map.mjs`).
2. Muafiyet listesi artık var olmayan bir sayfayı (`reference/changelog.md`)
   adlandırıyor — sayfa taşınmış ya da silinmiş, liste güncellenmemiş.

`AGENTS.md` dört doğrulama kapısının dördünün de sıfır uyarı vermesini şart
koşuyor ve `faz-tamamlama` Adım 7 siteyi fazın kapanışına dâhil ediyor. Bu
kapı bugün kırmızı; yani kapanış tanımı şu an sağlanmıyor.

Düzeltme ucuz görünüyor (üreteci koş + muafiyet satırını kaldır) ama **bu tur
boyunca kod donuk** olduğu için yapılmadı — Aşama 2'ye aittir.

---

**⚠️ OTURUM 3 YENİDEN ÖLÇÜMÜ (MT-PKG-107) — BU BULGU YANLIŞ POZİTİFTİR.**

Üç bulgunun **üçü de tek** kök nedenden geliyor ve o kök neden bir ürün kusuru
değil, **kapının yanlış sırada koşulması**: `check-content.mjs` üreteçler
koşmadan çalıştırılmıştı.

`docs-site/src/content/docs/reference/changelog.md` repoda **commit'li
değildir**; `scripts/build-changelog.mjs` onu kök `CHANGELOG.md`'den üretir ve
bu üreteç `prebuild` (`npm run generate`) adımında koşar. Sayfa yokken:

1. `build-agent-map` haritası changelog girdisini bulamıyor → hesaplanan
   `llms.txt`/`llms-full.txt` commit'li hâlleriyle uyuşmuyor (ikisi de
   `/reference/changelog/` satırı taşıyor — `public/llms.txt:307`).
2. Muafiyet listesi var olmayan sayfayı adlandırıyor.

**Kontrollü deney (oturum 3, aynı donuk kodda):**

```
npm run build                    -> cikis 0 (prebuild changelog.md'yi uretti)
node scripts/check-content.mjs   -> cikis 0 ✅ TEMIZ
  "Content: 56 manual pages and 1147 total pages passed."

mv src/content/docs/reference/changelog.md /tmp/   (sayfayi kaldir)
node scripts/check-content.mjs   -> cikis 1 · AYNI 3 bulgu, birebir

mv /tmp/changelog.md geri
node scripts/check-content.mjs   -> cikis 0 ✅ tekrar temiz
```

∴ Kapı temiz ağaçta **kırmızı değildir**; `npm run generate` (ya da
`npm run build`) önce koşulduğunda yeşildir. `package.json`'daki resmî sıra
zaten budur: `check` → `check:content && build && check:links && check:weight`.
Oturum 2 `check:content`'i tek başına, üretilmemiş bir ağaçta koşmuştu.

Hiçbir tracked dosya değişmedi (`git status --short` boş) — üretilen sayfa
`.gitignore` kapsamında.

**Aşama 2 için iş kalmadı.** Kalan tek gerçek risk: kapıyı üreteçsiz koşmak
yanıltıcı bir kırmızı veriyor. Bunu `docs/hafiza/dokumantasyon.md`'ye tuzak
olarak yazmak yeterli (tur sonu kontrol listesi).

---
## MT-PKG-100 — Prova kapısı gerçekten yayının önündedir

**Gerçek sonuç**
```
ci.yml:288  release-dryrun:  needs: build
ci.yml:337  publish:         needs: [pack, release-dryrun, npm-publish]
ci.yml:344                   if: startsWith(github.ref, 'refs/tags/v')
ci.yml:387  npm-publish:     needs: [build, release-dryrun]
ci.yml:391                   if: startsWith(github.ref, 'refs/tags/v')
ci.yml:234  pack:            needs: build
```

`release-dryrun` **ikisinde de** listeli. Prova yolun üzerindedir.

**Adım 3 (yalnız gözle, dosya değiştirilmedi):** `release-dryrun` iki `needs:`
satırından da çıkarılırsa graf `build → {pack, npm-publish} → publish` olur;
`publish` provayı hiç beklemeden `pack` biter bitmez koşar. Tek bir `needs:`
satırından çıkarmak yetmez — `publish` `npm-publish`'e, o da provaya bağlı
olduğu için kapı dolaylı olarak ayakta kalır. Yani koruma **iki** satıra
birden dayanıyor; ikisi de korunmalı.

Her iki iş `refs/tags/v` ile sınırlı, yani case'in "CI'da simüle edilemez"
gerekçesi doğrulandı — graf elle okundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-101 — Yayın provası altı sample'ı da sayar

**Gerçek sonuç**
```
MSBUILDDISABLENODEREUSE=1 python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1
  -> cikis 1

20 paket staging'e SORUNSUZ uretildi:
  artifacts/package/staging/run-kau8duuw/Tracon.{Core,Cli,UI,OpenAI,Azure,
  Anthropic,Mcp,Workflows,Testing,...}.1.0.0-preview.1.{nupkg,snupkg}
sonra:
❌ CHANGELOG.md içinde '## [1.0.0-preview.1]' bölümü yok veya boş
```

**Sample sayım satırına SIRA GELMEDİ.** Case'in ölçmek istediği
`✅ 6 exact-version packed sample ve Native AOT smoke: 1.0.0-preview.1`
satırı hiç basılmadı — changelog kapısı ondan **önce** durdurdu.

Kapı sırası koddan okundu (`scripts/kapi.py`):

| Satır | Adım |
|---|---|
| 1100-1105 | `CHANGELOG.md` bölüm kapısı ← **burada durdu** |
| 1213 | `_promote_staged_packages` (staging → release) |
| 1255 | `release_extension_samples.verify` ← sample sayımı burada |

Yani bu case `MT-PKG-097` ile **aynı kök nedene** takıldı (`HATA-S1-007`).
Kendi iddiası (altı sample sayılıyor mu) bugün **ölçülemez**, yanlış olduğu
gösterilmedi.

**Yan gözlemler (tutan):** `git status --short` prova sonrası boş — çalışma
ağacına hiçbir şey yazılmadı. `staging/` `finally` bloğunda temizlendi.
`artifacts/package/release/` **değişmedi**: içinde hâlâ yalnız MT-PKG-020'nin
`0.0.0-preview.0.789` paketleri var, `1.0.0-preview.1` **terfi etmedi**.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19)**
`HATA-S1-007` kapandı (kullanıcı kararı, K-825). Prova artık sürüm bölümünü
bulamazsa `## [Unreleased]`'i okuyor ve **dolu olmasını** şart koşuyor:

```
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1
  ℹ️ Sürüm notları '## [Unreleased]' bölümünden okundu; 'v1.0.0-preview.1'
     etiketlenirken bu başlık '## [1.0.0-preview.1] - <tarih>' olarak
     yeniden adlandırılır (YAYIN-HAZIRLIK Adım 5)
  ✅ 20 paket, sürüm '1.0.0-preview.1'
  ✅ npm publish --dry-run
  ✅ 6 exact-version packed sample ve Native AOT smoke
  çıkış 0
```

**Prova ilk kez yeşil.** Aynı yedeği `github-release` işi de kullanıyor;
yalnız biri kullansaydı prova yeşil, release gövdesi boş olurdu.
🚨 İlk iki deneme ortam yüzünden kırmızıydı, kod yüzünden değil: yerel release
feed'i eski `0.0.0-preview.0.*` paketlerini taşıyordu ve staging dizininde
aynı sürümün farklı içerikli kopyası duruyordu. İkisi de temizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-007 — Yayın provası `CHANGELOG.md`'de sürüm bölümü olmadığı için hiçbir sürümde yeşil olamaz

| | |
|---|---|
| **Önem** | Kritik (yayın hattını bloklar) |
| **Bulunduğu case** | MT-PKG-097 · MT-PKG-101 |
| **Sınıf** | Yayın kapısı · doküman-kod sözleşmesi |
| **Blokladığı case** | MT-PKG-104 · 105 · 115 · 116 · 117 |

`scripts/kapi.py:1100-1105` hedef sürüm için `## [<sürüm>]` bölümü arar ve
yoksa fail-closed durur. `CHANGELOG.md` ise **yalnız** `## [Unreleased]`
taşıyor (`grep -n "^## " CHANGELOG.md` → tek satır, 7).

Bu bir ihmal değil, bilinçli bir metin: `CHANGELOG.md:10-12` şöyle diyor —
*"The first real release will get its own section, fixed to the artifacts it
actually ships, and carries the date it shipped on."*

🚨 **İki kural birbirini kilitliyor:**

1. Kapı: sürüm bölümü yoksa prova geçemez.
2. Changelog politikası: bölüm ancak gerçekten sevk edilince yazılır.

∴ Prova **hiçbir** sürüm numarasıyla yeşil olamaz. Yayın hattı (`YAYIN-HAZIRLIK`
Adım 2→10) bu düğüm çözülmeden ilerleyemez.

Ayrıca `MT-PKG-102`'nin ön koşulu `CHANGELOG.md`'de
`## [1.0.0-preview.1] - 2026-08-28` satırının **var olduğunu** varsayıyor;
o satır bugün repoda yok. Spec bu noktada bayat.

**Aşama 2 için karar gerektiren:** bölüm yayından önce mi yazılır (kapı haklı,
changelog politikası gevşetilir), yoksa kapı yalnız gerçek `v*` etiketinde mi
bölüm arar (politika haklı, kapı gevşetilir)? İkisi de public bir söz verdiği
için `K-*` gerektirir.

---

## MT-PKG-102 — `CHANGELOG.md` bölümü eksikken kapı fail-closed döner

**Gerçek sonuç**
```
1) Ön kosul adimi NO-OP cikti:
   grep -n "1\.0\.0-preview\.1" CHANGELOG.md  -> eslesme YOK
   Spec'in "## [1.0.0-preview.1] - 2026-08-28 satirini gecici degistir"
   adimi bugun degistirecek bir satir bulamiyor.

2) Kapi zaten hedeflenen durumda kosuldu (MT-PKG-101 ile ayni komut):
   python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1  -> cikis 1
   ❌ CHANGELOG.md içinde '## [1.0.0-preview.1]' bölümü yok veya boş
```

**Case'in ASIL iddiası TUTTU:** bölüm eksikken kapı sıfır olmayan çıkış verdi
ve beklenen mesajı **birebir** bastı. Notsuz bir `v*` etiketi NuGet.org'a
gidemez — Önem=Kritik'in koruduğu davranış ayakta.

⚠️ **Üçüncü beklenti bugün doğrulanamadı:** "başlık geri alındıktan sonra aynı
komut tekrar `0` döner". Geri alınacak başlık yok; taban çizgisinin kendisi
bölümsüz. Bunun gerekçesi `HATA-S1-007`'dir, kapının kusuru değildir.
Kullanıcı kararı (2026-09-16): `CHANGELOG.md` bu tur boyunca **donuk kalır**,
bölüm yazılmaz. Pozitif yol Aşama 2'de düğüm çözülünce ölçülür.

⚠️ **Spec'e dokunuldu** (skill §1.1 istisnası, doküman kusuru): ön koşul artık
var olmayan bir satırı tarif ediyordu; bugünkü duruma göre düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-103 — Envanterden sarkan yeni bir sample kapıyı kırar

**Gerçek sonuç**
```
1) samples/Tracon.Samples.Deneme.Tests/ + tek .csproj olusturuldu:
   validate_sample_inventory(.) ->
   ['samples/Tracon.Samples.Deneme.Tests is not in SAMPLE_TEST_PROJECTS
     and has no SAMPLE_TEST_EXCLUSIONS entry
     (scripts/release_extension_samples.py)']

2) dizin silindi:
   validate_sample_inventory(.) -> []
```

Tek satır, sahte projeyi **adıyla** gösteriyor ve hangi iki listeye girmesi
gerektiğini söylüyor — üstelik düzeltmenin yapılacağı dosyayı da veriyor.
Faz 120'nin sessizce dışarıda kalan sample'ı bu kapıyla imkânsız. Dizin
silinince liste tekrar boş; kapı yapışkan değil.

`git status --short` case sonrası boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-106 — Dört adaptörün model-sağlayıcı sözleşmesi `secret` ve ağ olmadan geçer

**Gerçek sonuç**
```
Adaptor     build  test  total  succeeded  failed  skipped
Anthropic     0     0      79      79        0        0
Azure         0     0      76      76        0        0
Google        0     0      84      84        0        0
OpenAI        0     0     126     126        0        0
                        ----
                         365 test · 0 basarisiz · 0 atlanan
```

Dördü de `Test run summary: Passed!` (net10.0|arm64). **`skipped: 0`** — hiçbir
sözleşme case'i ortam yokluğundan sessizce düşmedi. `secret` export edilmedi,
ağ çağrısı yapılmadı.

**Sözleşme türetmeleri koddan doğrulandı:**

| Adaptör | `ModelProviderContract` | `...CredentialContract` | `...SettingsContract` |
|---|---|---|---|
| Anthropic | ✅ `:21` | ✅ `:34` | ✅ `:68` |
| Google | ✅ `:21` | ✅ `:34` | ✅ `:68` |
| Azure | ✅ `:20` | ✅ `:33` | **muaf** `:68` |
| OpenAI | ✅ `:19` | ✅ `:33` | **muaf** `:69` |

Spec'in "Anthropic ve Google ayrıca `ModelProviderSettingsContract`'ı" ifadesi
**birebir** doğru. Üstelik muafiyet sessiz değil: Azure ve OpenAI muafiyeti
`except: [nameof(ModelProviderSettingsContract)]` ile **adıyla beyan ediyor**
ve aynı dosyadaki meta-test başka bir sözleşmenin uygulanmadan kalmasını
yakalıyor (`:81`). Yani "sözleşmeyi türetmeyi unutma" kusuru da kapatılmış.

`Concurrent_resolution_of_one_credential_stays_stable` gerçek bir test:
`src/Tracon.Testing.Contracts.Xunit/Contracts/Providers/ModelProviderCredentialContract.cs:110`
ve public yüzeyde beyan edilmiş (`PublicAPI.Unshipped.txt:57`). Dört
`CredentialContract` türevinin dördünde de koştu (`failed: 0`).

K-646'nın BYOK önbellek kusurunu bulan sözleşme bugün yeşil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-107 — Site sürüm sayfası `CHANGELOG.md`'ye bağlanır

**Gerçek sonuç**
```
node scripts/build-changelog.mjs  -> cikis 0
npm run build                     -> cikis 0 · 1147 sayfa · 16.15 sn
                                     /reference/changelog/index.html uretildi
npm run preview                   -> http://localhost:4321

/reference/versioning/ :
  heading "Release notes" [level=2]            ✅ var
  link "Release notes" -> /reference/changelog/ ✅ var (govdede + sidebar +
                                                  footer + Next baglantisi)
  tiklandi -> /reference/changelog/ · 200 · baslik "Release notes | Tracon"
  browser_console_messages(warning) -> 0 hata, 0 uyari
```

⚠️ **Beklenen sonuç bayattı — bağlantı GitHub'a GİTMİYOR, gitmemesi de
bilinçli.** Hedef site içi `/reference/changelog/` sayfasıdır. Sayfa elle
yazılmaz; `docs-site/scripts/build-changelog.mjs` onu kök `CHANGELOG.md`'den
**üretir** ve `prebuild` adımında koşar (bu yüzden repoda commit'li bir
`reference/changelog.md` yoktur).

Gerekçe üretecin kendi başlığında yazılı (`build-changelog.mjs:4-10`):

> *"The repository is private, so a GitHub blob URL 404s for everyone outside
> it, and this site is the only public surface that can carry the notes.
> Keeping a second, hand-maintained copy of the changelog here would drift
> from the root file the release gate actually reads — so the root file stays
> the single source and this script publishes it."*

∴ Case'in ölçmek istediği şey (sürüm sayfasından yayın notlarına gidilebiliyor
mu, ve notlar kök `CHANGELOG.md` ile tek kaynaktan mı besleniyor) **tutuyor**;
yalnız hedefin URL'i spec'te bayat kalmış. `Beklenen sonuç` düzeltildi
(skill §1.1 istisnası).

**Üretilen sayfanın içeriği dürüst:** "Tracon has not been released yet."
diyor, olmayan bir sürümü adlandırmıyor.

🚨 **`HATA-S1-006`'nın üçüncü bulgusu bu case'te ÇÖZÜLDÜ** — aşağıya bak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-108 — Temiz ağaçta pack normal çalışır

**Gerçek sonuç**
```
git status --porcelain -> bos
dotnet pack src/Tracon.Abstractions -c Release -> cikis 0
  Successfully created package '.../release/Tracon.Abstractions.0.0.0-preview.0.789.nupkg'
  Successfully created package '.../release/Tracon.Abstractions.0.0.0-preview.0.789.snupkg'
```

Kapı temiz ağaçta **hiçbir şey yapmıyor** — `-p:` bayrağı verilmedi, ek
yapılandırma gerekmedi, uyarı çıkmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-109 — Commit'siz bir değişiklik `TRACON0004` ile pack'i durdurur

**Gerçek sonuç**
```
printf '\n' >> src/Directory.Build.props
git status --porcelain -> " M src/Directory.Build.props"
dotnet pack ... -> cikis 1

Directory.Build.targets(127,5): error TRACON0004: Working tree is not clean
('git status --porcelain' reported changes) for package 'Tracon.Abstractions'.
A packed artifact whose content cannot be traced back to a commit has no
provenance; commit or stash first. Reported entries:  M src/Directory.Build.props.
For local experimentation only, set TraconAllowDirtyPack=true together with an
explicit MinVerVersionOverride carrying 'dirty' (e.g. 0.0.0-dirty.<name>) -
never in CI.
```

**Yeni `.nupkg` ÜRETİLMEDİ — SHA-256 ile kanıtlandı:**
```
pack oncesi:  571ecb0c5166b17f2adada9c728b043926c35430c9a92e04d7df0c7187301bbd
pack sonrasi: 571ecb0c5166b17f2adada9c728b043926c35430c9a92e04d7df0c7187301bbd
```
Mevcut paket **yerinde ve dokunulmamış** kaldı. AP-REQ-002 (aynı `<id, sürüm>`
çiftinin farklı içerikli iki artifact adlandırması) bu kapıyla kapalı.

Tanı kalitesi yüksek: kirleten dosyayı **adıyla** veriyor, sebebini
(provenance) açıklıyor ve çıkış yolunu tarif ediyor. Dil İngilizce — pakete
giren kod için doğru (K-228).

`git checkout --` sonrası ağaç temiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-110 — Kirli ağaçta `dotnet build` etkilenmez

**Gerçek sonuç**
```
printf '\n' >> src/Directory.Build.props
dotnet build src/Tracon.Abstractions -c Release -> cikis 0
  Build succeeded.
grep -c TRACON0004 -> 0
```

Kapı `build` yolunu **hiç görmüyor**; yalnız `GenerateNuspec`'ten önce koşuyor.
İç geliştirme döngüsü (derle/test) kirli ağaçta bedelsiz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-111 — Yalnız untracked bir dosya da kapıyı tetikler

**Gerçek sonuç**
```
touch src/Tracon.Abstractions/.mt-pkg-111-marker
git status --porcelain -> "?? src/Tracon.Abstractions/.mt-pkg-111-marker"
dotnet pack ... -> cikis 1 · error TRACON0004
```

136.1'in bilinçli kararı doğrulandı: `git status --porcelain` `-uno` **almıyor**.
Takip edilmeyen bir `.cs` dosyası SDK'nın varsayılan glob'uyla pakete girebilir;
`-uno` olsaydı bu sessizce geçerdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-112 — Override sürümsüz verilirse `TRACON0006` ister

**Gerçek sonuç**
```
dotnet pack ... -p:TraconAllowDirtyPack=true   (MinVerVersionOverride YOK)
  -> cikis 1

error TRACON0006: TraconAllowDirtyPack=true requires an explicit
MinVerVersionOverride that carries 'dirty' (e.g. 0.0.0-dirty.<name>).
A dirty artifact must sort BELOW every clean release version, and it must
never b[e ...]
```

Override **sürüm üretmiyor, insandan istiyor**. MinVer'in kirli bir artifact'i
kendiliğinden bir sürüme bağlaması tamamen engellenmiş. Mesaj sıralama
gerekçesini de veriyor (kirli sürüm her temiz sürümün ALTINDA sıralanmalı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-113 — Açık `dirty` sürümüyle override başarıyla paketler

**Gerçek sonuç**
```
dotnet pack ... -p:TraconAllowDirtyPack=true -p:MinVerVersionOverride=0.0.0-dirty.deneme
  -> cikis 0
  Successfully created package '.../Tracon.Abstractions.0.0.0-dirty.deneme.nupkg'
  Successfully created package '.../Tracon.Abstractions.0.0.0-dirty.deneme.snupkg'
```

Kaçış yolu **çalışıyor** — kapı yerel deneyi imkânsız kılmıyor, yalnız
bilinçli ve adlandırılmış olmasını şart koşuyor. Üretilen iki dosya case'in
temizlik adımıyla silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-114 — CI'da override tamamen reddedilir

**Gerçek sonuç**
```
CI=true dotnet pack ... -p:TraconAllowDirtyPack=true -p:MinVerVersionOverride=0.0.0-dirty.deneme
  -> cikis 1   (MT-PKG-113'un AYNI komutu, yalnız CI=true eklendi)

error TRACON0005: TraconAllowDirtyPack=true was set in a CI build
(ContinuousIntegrationBuild=true or CI=true). A dirty pack has no provenance
and must never leave a developer's machine.

ls artifacts/package/release/Tracon.Abstractions.0.0.0-dirty.deneme.*
  -> eslesme yok (hicbir dirty paket uretilmedi)
```

🚨 **Kaçış yolunun kendisi de kapalı.** 113'te geçen komut, yalnız ortam
değişkeni değiştiği için reddedildi. Kirli bir pack hiçbir CI koşumundan
çıkamaz — `dirty` taşıyan açık bir sürümle bile.

`git status --porcelain` her case'in sonunda boş doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-118 — Lisans matrisi: her paket tam olarak bir lisans dosyası taşır

**Gerçek sonuç**

**Sapma:** `/tmp/ap-pack` yerine oturum 1'in bıraktığı
`<scratch>/ap-pack` kullanıldı (aynı 20 paket, `0.0.0-preview.0.789`).

```
20 satir dondu. Dagilim:

LICENSE-MIT.md (3):  Tracon.Abstractions · Tracon.Templates ·
                     Tracon.Testing.Contracts.Xunit
LICENSE.md    (17):  Tracon · .Anthropic · .AspNetCore · .Azure · .Cli ·
                     .Client · .Core · .Google · .Mcp · .OpenAI · .PostgreSql ·
                     .SqlServer · .Sqlite · .Testing · .UI · .Voice · .Workflows

Iki lisans dosyasini birden tasiyan paket: YOK
```

Spec'in saydığı üç MIT paketi **birebir** tuttu; 3 + 17 = 20. Tüketicinin
eline hangi şartların geçtiği her pakette tek anlamlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-119 — Beyan edilen lisans ile paketlenen dosya aynı

**Gerçek sonuç**
```
== Tracon.Abstractions
   <license type="file">LICENSE-MIT.md</license>
   requireLicenseAcceptance elementi: YOK           ✅ (NuGet false'u yazmaz)
   paket icindeki dosya: LICENSE-MIT.md             ✅ ayni

== Tracon.Core
   <license type="file">LICENSE.md</license>
   <requireLicenseAcceptance>true</requireLicenseAcceptance>   ✅
   paket icindeki dosya: LICENSE.md                 ✅ ayni
```

Üç beklentinin üçü de tuttu. Ticari şart taşıyan paket kabul istiyor,
permissive olan istemiyor — ayrım doğru yerde.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-120 — PolyForm gövdesi kanonik metinden sapmamış

**Gerçek sonuç**
```
curl SPDX PolyForm-Small-Business-1.0.0.txt -> 121 satir
awk ile LICENSE.md govdesi              -> 121 satir
diff                                    -> BOS · "BIREBIR"
```

Lisans **tanınır** hâlde: tüketicinin lisans tarayıcısı adı eşleştirebilir.
Bu modelin tahsilat mekanizması tam olarak o eşleşme olduğu için sapma
olmaması kritik.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-121 — 👤 npm istemcisi NuGet ikiziyle aynı şartları taşır

**Gerçek sonuç**
```
package.json: "license": "PolyForm-Small-Business-1.0.0"   ✅
npm pack --dry-run: "npm notice 6.3kB LICENSE.md"          ✅ pakete giriyor
diff packages/tracon-client/LICENSE.md ../../LICENSE.md
  -> BOS · "KOPYA BIREBIR"                                 ✅
```

npm tarafı NuGet ikiziyle aynı şartları taşıyor ve kopya kök dosyadan
sapmamış.

⏳ **İnsan doğrulaması bekliyor:** npmjs.com lisans rozetinin
`PolyForm-Small-Business-1.0.0` gösterdiği ancak paket **yayınlandıktan
sonra** görülebilir. Paket henüz yayınlanmadı (`CHANGELOG.md`: "no version has
been pushed to NuGet or npm"). Fiziksel eylem listesine alındı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-122 — Sevk edilen README'nin lisans iddiası nuspec ile aynı

**Gerçek sonuç**

**20/20 paket uyumlu — çelişki YOK.** Faz 160'ın 🔴'sının sınıfı bugün temiz:

```
nuspec=LICENSE-MIT.md (3) -> README "MIT" der        : Abstractions ·
                                                       Templates ·
                                                       Testing.Contracts.Xunit
nuspec=LICENSE.md    (17) -> README "PolyForm" der   : kalan 17
uyumsuz: 0
```

⚠️ **Spec'in doğrulama komutu yanıltıcı — `Tracon.Workflows` için BOŞ döndü.**
Komut `grep -iE "^Licen[sc]e: "` ile **satır içi** biçimi arıyor; 19 paket o
biçimde ama `Tracon.Workflows` iddiasını **başlık** biçiminde yazıyor:

```
README.md:91   ## Licence
README.md:93   PolyForm Small Business 1.0.0 - free below 100 people and ...
```

Yani paket doğru, **ölçüm** yanlış. Bu önemli bir kör nokta: lisans iddiasını
hiç taşımayan bir paket ile başlık biçimini kullanan bir paket komutun
çıktısında **birbirinden ayırt edilemez** (`readme=` ikisinde de boş). Case'in
🚨'sı tam da bu sınıfı korumak için var.

Biçimden bağımsız yeniden ölçüldü (satır içi **veya** `## Licen[sc]e` başlığı,
sonra iddianın ilk kelimesi nuspec ile karşılaştırıldı) → **20/20 OK**.
`Beklenen sonuç` bu yöntemle güncellendi (skill §1.1 istisnası).

📋 **Kapanışa aday, düşük önem (kod donuk olduğu için düzeltilmedi):**
1. `Tracon.Workflows` README'si diğer 19'dan farklı biçim kullanıyor.
2. Yazım tutarsızlığı: sevk edilen README'lerde `Licence` 13, `License` 7 kez
   geçiyor. İkisi de doğru İngilizce ama tek üründe karışık kullanılıyor;
   nuget.org bunları yan yana render ediyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-104 — `PackageReleaseNotes` çözümlenmiş sürümü taşır, ham `$(Version)` değil

**Gerçek sonuç**
Koşulmadı. Ön koşul (`MT-PKG-101` koşuldu, paketler
`artifacts/package/release/` içinde) **bugün sağlanamıyor**: prova
`HATA-S1-007` yüzünden terfi adımına gelmeden duruyor, `release/` altında
`1.0.0-preview.1` paketi yok.

⚠️ **Bu case'in beklentisi ayrıca bayat olabilir.** Beklenen değer bir GitHub
blob URL'i (`.../blob/v1.0.0-preview.1/CHANGELOG.md`). `MT-PKG-107`'de ölçüldü
ki repo private olduğu için site bilinçli olarak GitHub'a bağlanmayı bıraktı ve
`PackageReleaseNotes`'un artık site sayfasına (`tracon.dev/reference/changelog/`)
işaret etmesi bekleniyor (`docs-site/scripts/build-changelog.mjs:4-14`).
Koşulduğunda **önce bu doğrulanmalı**, yoksa doğru davranış kusur sanılır.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-19 (kapanış, §5(a) PKG) · ☑ GEÇTİ**

Engel kalktı: `HATA-S1-007` kapandı, prova terfiye ulaşıyor, `release/` altında
20 paket var (`1.0.0-preview.1`, commit `89f44ab3`).

```
$ unzip -p artifacts/package/release/Tracon.Core.1.0.0-preview.1.nupkg '*.nuspec' | grep releaseNotes
    <releaseNotes>https://tracon.dev/reference/changelog/#v1.0.0-preview.1</releaseNotes>
```

**Case'in asıl konusu geçti.** Alan çözümlenmiş sürümü (`1.0.0-preview.1`)
taşıyor; `v$(Version)` de boş sürüm de görünmüyor. `BeforeTargets="GenerateNuspec"`
hedefi MinVer'den **sonra** değerlendiği için `$(Version)` dolu okunuyor.

⚠️ **Turdaki uyarı doğru çıktı — beklenen değer bayattı.** Spec bir GitHub blob
URL'i bekliyordu. Hedef Faz 162'de (`630f3212`) site sayfasına döndü; gerekçe
`src/Directory.Build.props:120-137` yorumunda yazılı: *"a github.com URL would
404 for every consumer while the repository is private."* Spec'in
`Beklenen sonuç`'u koda göre düzeltildi (skill §1.1 istisnası). Kaydın 2026-09-16
tarihli "koşulduğunda önce bu doğrulanmalı" notu bu oturumu doğrudan kurtardı —
doğru davranış kusur sanılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-105 — 20/20 paket `releaseNotes` alanını taşır

**Gerçek sonuç**
Koşulmadı — ön koşul `MT-PKG-101`, `HATA-S1-007` ile bloklu.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-19 (kapanış, §5(a) PKG) · ☑ GEÇTİ**

```
$ for f in artifacts/package/release/*.nupkg; do
    unzip -p "$f" '*.nuspec' | grep -q '<releaseNotes>' || echo "EKSIK: $f"
  done
(çıktı yok)
$ ls artifacts/package/release/*.nupkg | wc -l
      20
```

Hiçbir satır basılmadı: **20/20** paket alanı taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-115 — Aynı sürümle iki ardışık yayın koşumu ikincisinde no-op'tur

**Gerçek sonuç**
Koşulmadı. İki koşum da `HATA-S1-007` ile **aynı** noktada (changelog kapısı)
durur; terfi hiç gerçekleşmediği için "ikinci koşum no-op mu" sorusu
ölçülemez. Kısmî gözlem (`MT-PKG-101`): birinci koşum `release/`'i
değiştirmedi ve `staging/` `finally` bloğunda temizlendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-19 (kapanış, §5(a) PKG) · ☑ GEÇTİ**

İki ardışık prova, aynı sürüm, temiz ağaç:

| Koşum | Çıkış | `❌` satırı | `Tracon.Abstractions` SHA-256 |
|---|---|---|---|
| 1 | `0` | yok | `1933629e…56ce910` |
| 2 | `0` | yok | `1933629e…56ce910` |

İkincisi deterministik no-op: aynı SHA-256 promote edilmeden geçti.

🚨 **Ön koşul eksikti ve ölçümü bir kez bozdu.** İlk denemede prova terfiden
**sonra** sıfır olmayan çıkışla durdu — sebep `git` değil, `release/` altında
§4.1'in bıraktığı 182 adet `0.0.0-preview.0.8xx` paketiydi:

```
❌ Extension sample contract ihlal edildi:
  release feed contains stale Tracon packages: Tracon.0.0.0-preview.0.865.nupkg, …
```

Kapı haklı — bayat bir feed tüketiciye yanlış sürüm çözdürür. Ama case'in
`Ön koşul`'u yalnız `git status --porcelain` boş diyordu. Spec'e ikinci ön koşul
eklendi (skill §1.1 istisnası). **Ders: yayın provası çalışma ağacına olduğu
kadar `release/` dizininin içeriğine de duyarlıdır.**

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-116 — Aynı kimlikte farklı içerik yayın koşumunu durdurur, mevcut artifact yerinde kalır

**Gerçek sonuç**
Koşulmadı — ön koşul `MT-PKG-115`, o da `HATA-S1-007` ile bloklu.

🚨 **Koşulacağı oturum için uyarı:** bu case `git add -A && git commit` ile
`src/` altına commit atıp `git reset --hard HEAD~1` ile geri alıyor. `-A`
o anda ağaçtaki **koşum kayıtlarını da** commit'ler. Koşarken yalnız hedef
dosyayı stage'le (`git add <dosya>`) ve reset'ten önce kayıtların commit'li
olduğunu doğrula — aksi hâlde `--hard` yazılmamış sonuçları siler.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-19 (kapanış, §5(a) PKG) · ☑ GEÇTİ**

🚨 **Case'in senaryosu KENDİLİĞİNDEN oluşmuştu — sentetik commit'e hiç gerek
kalmadı.** `release/` altındaki paketler commit `4b28940f`'ten, `HEAD` ise
`89f44ab3`'ten (K-831'in kod düzeltmesi, 04:40'ta indi; paketler 04:26–04:32'de
üretilmişti). Yani "aynı sürüm iddiası, farklı içerik" durumu gerçek bir
geliştirme olayı olarak zaten oradaydı. Kaydın `git add -A` uyarısı bu yüzden
hiç devreye girmedi: **hiçbir commit atılmadı, hiçbir `--hard` reset koşulmadı.**

Dört iddianın dördü de tuttu:

| İddia | Sonuç |
|---|---|
| Sıfır olmayan çıkış | ☑ iki bağımsız koşumda da `1` |
| "FARKLI içerikli bir artifact zaten var" mesajı | ☑ |
| Mesaj `Tracon.Abstractions`'ı adlandırıyor | ☑ (38 dosyanın listesinde) |
| İki `shasum` **aynı** değeri verir | ☑ `69a8297e…87b2ad7d` — iki koşumun öncesi ve sonrası |

Dosyanın `mtime`'ı da hiç oynamadı (`Sep 19 04:26:22`): mevcut artifact gerçekten
**yerinde kaldı**, üzerine yazılıp aynı içerikle geri getirilmedi.

💡 **Neden 3 proje değişmişken 20 paketin hepsi "farklı" işaretlendi?** Aşırı
raporlama değil: her `.nuspec` commit kimliğini taşıyor —
`<repository … commit="89f44ab3dfcd49a6801d9de3c4343448fc2503f9" />`. Kimlik
metadata'sı her commit'te değişir, dolayısıyla her paketin baytları da değişir.
Doğru davranış. Buildin kendisi deterministiktir; MT-PKG-115 aynı commit'ten
iki koşumda **birebir aynı** SHA-256'yı ölçtü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-PKG-117 — Manifest 20 paketin kimliğini SHA-256 ile taşır

**Gerçek sonuç**
Koşulmadı — ön koşul `MT-PKG-115`, `HATA-S1-007` ile bloklu.
`artifacts/package/release/package-manifest.json` üretilmedi (manifest
`kapi.py:1237`'de, changelog kapısından **sonra** yazılıyor).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Fiziksel eylem / ortam bekleyen case'ler — oturum 3

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-PKG-121 | Paket npmjs.com'da yayınlanmadı | Yayın sonrası npmjs.com'da lisans rozetinin `PolyForm-Small-Business-1.0.0` gösterdiği gözle doğrulanır |
| MT-PKG-104 · 105 · 115 · 116 · 117 | `HATA-S1-007` — yayın provası terfi adımına gelemiyor | `CHANGELOG.md` / kapı düğümü Aşama 2'de çözülünce beşi birden koşulur |

**Yeniden koşum — 2026-09-19 (kapanış, §5(a) PKG) · ☑ GEÇTİ**

```
$ python3 -c "import json; d=json.load(open('artifacts/package/release/package-manifest.json')); print(len(d['packages']), d['version'], d['dirty']); print(d['packages'][0])"
20 1.0.0-preview.1 False
{'id': 'Tracon', 'file': 'Tracon.1.0.0-preview.1.nupkg',
 'sha256': '7f66f0d4…022d6b4f', 'symbolsFile': None, 'symbolsSha256': None}
```

`20 1.0.0-preview.1 False` — beklenen satırın birebir kendisi. İlk kayıt `id`,
`file`, `sha256` alanlarını taşıyor.

Spec'in parantezi (*"kütüphane profilindeyse `symbolsFile`/`symbolsSha256` de
dolu"*) ayrıca doğrulandı: **18** kayıtta ikisi de dolu, boş olan **iki** paket
`Tracon` (metapaket) ve `Tracon.Templates` (şablon paketi) — ikisi de kütüphane
profili değil, yani `None` doğru değer.

```
Tracon.Abstractions → symbolsFile: Tracon.Abstractions.1.0.0-preview.1.snupkg
                      symbolsSha256: 3188ceed…eb6f3b77
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

