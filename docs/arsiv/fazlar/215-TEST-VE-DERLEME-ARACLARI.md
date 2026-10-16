# Faz 215 — Test ve Derleme Araçları

> **Durum:** ✅ Tamamlandı (2026-10-16)
> **Plan onayı:** kullanıcı fazı `faza başla ve bitir` diyerek başlattı (2026-10-16); Açık Sorular 1-3 ayrıca yanıtlanmadı, **planın önerdiği seçenekle (A) uygulandı** — bkz. *Plandan Sapmalar* 1 ve *Bu Fazda Verilen Kararlar*. Kapsam kararı: **UR-009 B**, farukatasoy, 2026-10-16.
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-557** (üç fazın ikincisi), **F-568** (analyzer yükseltmesinin bulduğu yapılandırma kusuru)
> **Genellik:** Tüketiciye iki yoldan dokunur: sevk edilen `Tracon.Testing.Contracts.Xunit` `xunit.v3`'e bağlıdır (A-70) ve `Tracon.Client` NSwag ile üretilir. Geri kalanı geliştirme aparatıdır (`PrivateAssets=all`), ama kapanış kapısı ve CI ona dayanır. İkinci kanıt: K-543'ün beş `ignore` kaleminden ikisinin gerekçesi artık yok (deneme, 2026-10-16).
> **Karşı görüş:** "Araç yükseltmesi ürün değeri üretmez; MTP 2 test koşum hattını değiştirir ve kapanış kapısını kırabilir." **Cevap:** Doğru, bu yüzden faz ayrıdır ve ölçütü kapının kendisidir: kapı aynı testleri koşar, düşen testi aynı yolla yeniden koşar ve TRX'i aynı yere yükler. Deneme bu üçünün **sessizce** bozulduğunu gösterdi (§215.1); düzeltme kanıtlanmadan faz bitmez. Analyzer yükseltmesi bir gerçek kusur buldu (F-568) — bu da değerdir.
> **Önkoşul:** [Faz 214](214-CALISMA-ANI-BAGIMLILIKLARI-VE-MAF-125.md) — `Directory.Packages.props`, `dependabot.yml`, `CHANGELOG.md` ortak; paralel koşmaz.
> **Paketler:** `Tracon.Testing.Contracts.Xunit` (sevk edilen bağımlılık aralığı), `Tracon.Client` (NSwag üretimi, Açık Soru 3), `Tracon.Core` (F-568), analyzer'ların dokunduğu kaynaklar
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `Tracon.Client` üretilen yüzeyi NSwag 14.7.1 ile değişebilir (Açık Soru 3; ApiCompat ölçer). `PublicAPI.Shipped.txt` bugün **0** satır.
> **Tüketici yüzeyi:** site: `guides/testing.md` (contract paketinin xunit sürüm aralığı) · `reference/compatibility.md` · sevk edilen: `src/Tracon.Testing.Contracts.Xunit/README.md`, `CHANGELOG.md` `[Unreleased]` (F-568 davranış değişikliği)
> **Manuel test alanı:** [`manuel-test/36-GELISTIRME-KAPILARI.md`](../../manuel-test/36-GELISTIRME-KAPILARI.md) (kapı hattı) · [`manuel-test/24-TEST-PAKETI-VE-SABLON.md`](../../manuel-test/24-TEST-PAKETI-VE-SABLON.md) (contract paketi)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 76aabda9:docs/arsiv/fazlar/215-TEST-VE-DERLEME-ARACLARI.md
> ```
>
> Damıtıldı 2026-10-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Bitiş Ölçütleri (DoD)

- [x] `Directory.Packages.props` ve `.config/dotnet-tools.json` §215.2 hedeflerinde; `restore` NU1605/NU1608/NU1903 yok — `dotnet list Tracon.slnx package --vulnerable --include-transitive`: açık yok; `restore` uyarısı yok
- [x] `global.json` MTP koşucusunu adlandırır; `ci.yml` ve `kapi.py` `--max-parallel-test-modules` kullanır — `kapi_test.py` ikisini ve TRX yolunu birlikte denetler
- [x] Negatif kontrol kayda geçti: kasıtlı kırmızı test kapıyı kırar, izole yeniden koşulur, TRX doğru yerdedir (manuel case 1) — *Örnek Uygulama Koşumu*'nun altındaki *Negatif kontrol* tablosu (`MT-GDK-057`, `059`)
- [x] Contract paketinin nuspec aralığı Açık Soru 1'in seçimini taşır; `release_extension_samples` yeşil — `kapi.py yayin --kuru --surum 1.0.0-preview.5.215` EXIT 0 (altı paketlenmiş örnek: FileRunStore 129/129, CustomModelProvider 39, CustomAgentSource 15, CustomTool 18, CustomRunJudge 11, CustomJobHandler 10; Native AOT ve net8 tüketici smoke); alt sınırı `dependency_floor_errors` kapısı sabitler
- [x] F-568: düşen test önce kırmızıydı (13 vakanın 7'si); düzeltme sonrası 13/13 yeşil ×3 TFM; sınıf taraması sonucu *Plandan Sapmalar* 6'dan sonraki *Sınıf taraması* bölümünde
- [x] Meziantou 3.0.303 ile sıfır uyarı — `dotnet build Tracon.slnx -c Release` 0 uyarı, 0 hata
- [x] `SSH.NET` sabiti yok; `dotnet list … --vulnerable --include-transitive` açık yok — Testcontainers 4.16.0 `SSH.NET` `2026.0.0`'ı kendisi çeker (`project.assets.json`)
- [x] `dependabot.yml`: xunit ve TrxReport `ignore`'u kalktı; CodeAnalysis gerekçesi yeni metinle; K-543 notu (ek: iki alt paket için yeni `ignore`, *Plandan Sapmalar* 3)
- [x] Açık Soru 3'ün seçimi uygulandı; `Tracon.Client` farkı okundu — public/protected/internal bildirim satırları özdeş (1981/1981); gövde farkı `ReadAsStringAsync(content, cancellationToken)`, 325 sürüm damgası, iki `#pragma`
- [x] `.agents/ortak/kapilar.md` ve `hafiza/test-kosum-tuzaklari.md` yeni komut yüzeyini yazar (ek: `MEMORY.md`, `nswag-istemci-uretimi.md`, `build-ve-analyzer.md`)
- [x] Dört doğrulama kapısı sıfır uyarı: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — sonuç *Sonraki Faza Devir Notu*'nun sonundaki *Kapanış kapısı* satırında
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — *Örnek Uygulama Koşumu*
- [x] `secret` taraması boş döndü — `kapi.py tarama` (kapanış kapısının ilk adımı) temiz
- [x] Manuel kabul case'leri `36-GELISTIRME-KAPILARI.md` (`MT-GDK-057`…`060`, `054` yeniden yazıldı), `24-TEST-PAKETI-VE-SABLON.md` (`MT-TEST-098`) ve `02-CEKIRDEK-VE-KATALOG.md` (`MT-CORE-154`) içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — *Denetim Bulguları*
- [x] İlk CI koşumunda test sayısı ve TRX yüklemesi okundu (push bakımcı onayıyla) — **bakımcıya devredildi**: push yapılmadı; yerel eşdeğer (`kapi.py` TRX sayımı ve `kapi_test.py` CI↔kapı uyumu) koştu; *Sonraki Faza Devir Notu* → *Bakımcı eylemleri*

---

## Plandan Sapmalar

1. **Açık Sorular 1-3 = A, A, A** (plan önerisi; kullanıcı ayrıca yanıtlamadı). (1) Sevk edilen paket `xunit.v3.extensibility.core` ve `xunit.v3.assert` için `>= 3.2.2` kalır — `VersionOverride` yerine `Directory.Packages.props` iki sabiti `3.2.2`'de tutar ve gerekçesini yorumda taşır (tek tüketen csproj contract paketidir; fark yok). (2) `MA0054` bilinçli vakası gerekçeli `#pragma`. (3) NSwag `14.7.1` bu fazda; `Tracon.Client` farkı yalnız gövde, public yüzey değişmedi.
2. **`bench/capacity/…Acceptance.csproj` CPM'e alınmadı; `3.2.0` → `4.0.2` literal.** Plan "CPM'e al" diyordu. Kapasite aparatı bilerek CPM'in **dışındadır** (Faz 166: `scripts/capacity.py` dizini depo dışındaki geçici bir dizine kopyalar ve `bench/capacity/Directory.Packages.props` `ManagePackageVersionsCentrally=false` ile arama yolunu keser; `Shouldly` ve `Npgsql` de aynı sebeple literal). CPM'e almak kopyada `NU1008` verirdi.
3. **`dependabot.yml`'a iki yeni `ignore` girdi** (`xunit.v3.extensibility.core`, `xunit.v3.assert` `>= 4.0.0`) — plan yalnız silinen kalemleri yazıyordu. Açık Soru 1 = A'nın bot tarafı: gruplanan `xunit*` deseni alt sınırı sessizce 4.x'e çekerdi (K-1018). DoD'daki "xunit ve TrxReport `ignore`'u kalktı" korundu.
4. **`nswag.json` `runtime: Net90` → `Net100`.** Plan yazmıyordu: NSwag `14.7.1` konsol aracı `net10.0` altında koşar ve `The specified runtime in the document (Net90) differs from the current process runtime (Net100)` ile durur. İlk denemede üreteç `generate-client-json-context.py`'nin **çıktı yolunu** yanlış verdim (`src/Tracon.Client/TraconApiClient.JsonContext.cs`, elle yazılan dosya) ve dosya ezildi; `git checkout` ile geri alındı, doğru yol `src/Tracon.Client/Generated/TraconClientJsonContext.g.cs`'tir. NSwag adımı hata verince eski üretilmiş dosya yerinde kalır; zincir `&&` ile bağlanmadığında sonraki adımlar yine koşar (post-işlemci "idempotent değil" diye reddetti, JSON context üreteci ise elle yazılan dosyayı ezdi). Hafıza notu `nswag-istemci-uretimi.md`'dedir.
5. **TRX yolu planın tarif ettiğinden daha sağlam okunur.** Plan `failed_tests_from_trx` ayrıştırmasını "TRX'i `--results-directory` altında ara" diye yazıyordu. Gerçekleşen: `--results-directory-layout per-module` (`<proje>/<tfm>_<mimari>/`) + ikilinin klasörü (`release` / `release_<tfm>`) TRX'in `TestMethod codeBase` alanından okunur. Bildirilen çerçeveden türetmek bayat `release/` klasörünü yanlış koşturabilirdi (Faz 183 tuzağı).
6. **Sıfır test kapısı eklendi** (`kapi.py` `require_executed_tests`, çıkış `8`). Plan "test sayısı > 0 iddiası" diyordu ama mekanizmayı belirtmiyordu. Çıkış kodu 0 ve TRX'siz/boş bir koşum artık kırmızıdır. Ayrıca `_dotnet_test_project` ve `release_extension_samples.py` konumsal csproj yerine `--project` kullanır; `runs_multi_target_tests` hedefi `--solution`/`--project` değerinden okur (yoksa `args[2]` `--solution` olur ve çok hedefli runtime ön denetimi sessizce kalkardı).
7. **`dokuman-bakim.py` `tek_derleme_zinciri_bulgulari` düzeltildi** (plan listesinde yok): kapı `dotnet test Tracon.slnx` dizesini aradığı için `--solution Tracon.slnx` biçimi "test adımı yok" sayılırdı. Düzenli ifade iki biçimi de kabul eder; yeni test eklendi.
8. **`performans` kapısı depo içi artık ajan worktree'leriyle kırıldı.** Faz 214 ve 216 denemelerinin `.claude/worktrees/agent-*` dizinleri (ikisi de `1ae92822` tabanlı) BenchmarkDotNet'te "Found more than one matching project file" verdi (`kapilar.md` bunu önceden yazar). Kapı `bench/` yoluyla tetiklendiği için çıkış kırmızıydı. Silinmedi (a6ce… Faz 216 denemesinin kaydedilmemiş farkını taşıyor): `git worktree move` ile depo dışına, `~/Desktop/projects/Tracon-parked-worktrees/` altına park edildi.
9. **`docs/hafiza/test-kosum-tuzaklari.md` bütçesi** (16.000 B): yeni bölüm için çözülmüş Faz 176/178 AOT paragrafı, AOT `.pdb` paragrafı ve başlık geçmişi sıkıştırıldı. Denetim üç ayrıntının düştüğünü buldu (gerçek `clang` yolu ile teşhis ipucu, "lisans kapısı shim'lerin hepsini reddeder" cümlesi, "yayın provası AOT smoke dahil yeşil" ölçümü); tam metin `git show a0ec0223:docs/hafiza/test-kosum-tuzaklari.md` ile çözülür. Bütçe 16.000 B'de olduğundan geri eklenmedi.
10. **Playwright önbellek anahtarı** (`ci.yml`) `1.61.0` → `1.63.0`: planda "CI'daki `playwright install` sürümü ölç" vardı; kurulum betiği paketten gelir (`playwright.ps1`), yalnız anahtar elle yazılıdır.
11. **Site senkronu (`--site-denetle --taban a0ec0223`) üç kural karşılanmadı bildirdi; gerekçeleri:** (a) `http-api` — `McpTaskRunProvisioning.cs` yalnız `MA0060` için ifadeye çevrildi, davranış ve `docs/openapi/tracon.json` aynı; (b) `cekirdek-kavram` — `TraconOptions.cs`'e `internal` bir taşıyıcı eklendi, kavram yüzeyi aynı; kullanıcıya görünen davranış (okunamayan `UtilityModel` sayısı açılışı durdurur) `reference/configuration.md` ve CHANGELOG'da yazıldı; (c) `paket-readme` — `Tracon.Testing.Contracts.Xunit/README.md`'ye xunit alt sınırı cümlesi eklendi, `packages.md` paketin ne işe yaradığını anlatır, bağımlılık aralığını değil; aralık `guides/write-your-own-store.md`'de (güncellendi) ve CHANGELOG'dadır. Site değişti (iki sayfa); yayın bakımcı eylemidir.

### Sınıf taraması (F-568)

- Kusur deseni: bir `TryParse`'ın dönüş değeri yok sayılır ve `out` değeri (`0`) kullanılır. Tarama: `grep -rn "\.TryParse(" src --include='*.cs' | grep -v "if\|is \|=>\|&&\|||\|?"` (üç vaka `Binding.Models.cs`'te + `McpTaskRunProvisioning.cs:84`'ün `TryParseAgentName`'i). İkinci vaka işlevsel olarak zararsızdı (`out` başarısızlıkta `""` verir), `MA0060` yüzünden ifadeye çevrildi.
- Başka statement düzeyinde yok sayılan `TryParse` kalmadı (Meziantou 3.0.303 ile derleme 0 uyarı bunu tüm `src/`, `tests/`, `samples/` için doğrular). Kalan satırlar `X[key] = int.TryParse(…) ? v : 0` biçimidir (`Binding.Operations.cs:108,133`, `Binding.Models.cs:152`) ve bilerek öyledir: okunamayan bütçe `0` bağlanır, doğrulayıcı açılışta adlandırır (F-385/K-993).
- **Geniş kol açık:** `if (X.TryParse(…)) { option = v; }` okunamayan skaleri sessizce yutar ve seçeneği varsayılanda bırakır (~132 satır). Bu fazın kapsamı dışında; bağımsız denetim buldu → **F-572**.

## Bu Fazda Verilen Kararlar

Kayıt kuralına girenler `docs/KARARLAR.md`'ye yazıldı:

- **K-1018** *(kategori: public-api)* — sevk edilen `Tracon.Testing.Contracts.Xunit` paketi `xunit.v3.extensibility.core` ve `xunit.v3.assert` için `>= 3.2.2` alt sınırını korur; reponun kendi testleri xunit.v3 `4.0.2` + TrxReport `2.5.1` üzerinde koşar (Açık Soru 1 = A).
- **K-543** notu — `xunit.v3` ve `TrxReport` `ignore` kalemleri koşulları gerçekleştiği için silindi; iki alt paket için yeni kalem (K-1018).
- **K-993** notu — "okunamayan girdi atlanmaz" kuralı `Tracon:UtilityModel` sayılarına genişledi (F-568).

Faz içi kararlar (dört kategoriye girmediği için `K-*` açılmadı):

- **Açık Soru 2 = A:** `ExplainedEnumConverter`'daki `MA0054` gerekçeli `#pragma` (yakalanan istisna zaten değiştirilen ham STJ mesajıdır; zincir CLR tip adını ve bayt ofsetini geri getirirdi).
- **Açık Soru 3 = A:** NSwag `14.7.1`, `Tracon.Client` public yüzeyi özdeş olduğundan kabul edildi; "public tip değişirse dur" koşulu tetiklenmedi.
- **Tam koşum paralelliği:** yerel kapı `--max-parallel-test-modules 2` (Faz 184 ölçümü), CI `1` (runner donanımı ölçülmedi) — sayılar eski `-maxcpucount` değerleriyle aynı.
- **Sıfır test kapısı çıkış kodu `8`** (MTP'nin kendi "sıfır test" kodu): `kapi.py` TRX'ten sayar; modül başına sıfırı MTP kendisi yakalar, toplam sıfırı bu kapı.
- **MTP kipi** `global.json` `test.runner` ile açılır; hiçbir `csproj` özelliği değişmedi (`UseMicrosoftTestingPlatformRunner` ve `TestingPlatformDotnetTestSupport` yerinde kaldı).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, Production, bellek içi depo, `echo` sağlayıcısı (`dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll --contentRoot … --urls http://127.0.0.1:5299`).

| Case | Girdi | Gerçek çıktı |
|---|---|---|
| `MT-CORE-154` | `Tracon__UtilityModel__Provider=openai`, `__Model=gpt-5.4-mini`, `__Temperature=abc` | Süreç **açılmadı**, çıkış `134`: `OptionsValidationException: TraconOptions.UtilityModel.Temperature is 'abc', which is not a number. Write a number or remove the key to use the provider default.` |
| | aynı, `__Temperature=` (boş) | Açıldı; `POST /tracon/api/agents/support/run` → `run · update · update · done`, `GET /tracon/api/runs/{id}` → `Completed`, çıktı `Echo: ping` |
| varsayılan dışı | `__Temperature=0.2`, `__MaxOutputTokens=256`, `Tracon__Observability__SuccessSampleRatio=1` | Aynı akış, `Completed`; günlükte hata satırı yok (eşleşenler `ProviderFailureNormalizingChatClient` adı) |
| varsayılan | `UtilityModel` yok | Aynı akış, `Completed` |

### Negatif kontrol (kapı)

Kasıtlı düşen test (`Tracon.Testing.UnitTests` içinde `false.ShouldBeTrue()`), üç projelik geçici bir `.slnf` (Testing.UnitTests, Contracts.Xunit.UnitTests ×3 TFM, Client.UnitTests) ve `kapi.full_solution_test_command()`'ın komutu:

| Durum | Gerçek çıktı |
|---|---|
| Kasıtlı kırmızı (`MT-GDK-057`) | `total: 100 · failed: 1`; `Test run completed with non-success exit code: 2`; kapı `❌ Çıkış 2 (6.46 s)`, ardından `🔬 İzole yeniden koşum` ve `❌ izole de DÜŞTÜ  Tracon.Testing.UnitTests · …Deliberately_red_for_the_negative_control`; TRX `artifacts/test-results/<proje>/<tfm>_arm64/` altında (beş dosya) |
| Test silindi, proje yeniden derlendi (pozitif) | `99 test, 5 modül (TRX: artifacts/test-results)`, `✅ 6.06 s`, `RC = 0` |
| Eski bayraklar (`-maxcpucount:2 -- --report-trx`, `MT-GDK-059`) | her modülde `Exit code: 5`, hiç test koşmadı |

Paketlenmiş contract paketine karşı tüketici (`MT-TEST-098`, `1.0.0-preview.5.215`): `xunit.v3 3.2.2` + `TrxReport 1.9.1` ve `4.0.2` + `2.5.1` — ikisinde `dotnet restore -warnaserror` uyarısız; çözülen `xunit.v3.extensibility.core` `3.2.2` ve `4.0.2`. `global.json` `test.runner` olmadan `4.0.2` tüketicisinde `dotnet test`: `Testing with VSTest target is no longer supported`; eklenince `1/1` yeşil.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 2: (1) tam koşumda `ShippedDocumentationSelfContainmentTests` ×3 TFM — F-568 commit'i sevk edilen `///` satırına iç numara yazdı; (2) bağımsız denetimin 🟡 7 bulgusu (xunit alt sınır kapısı, konumsal `dotnet test` kalıntıları, yorum yeri, üç bayat doküman, DoD kanıt boşlukları) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 1 / 0 — gerçek: iç numara (kapı da yakaladı); gürültü: `release_extension_samples` konumsal `dotnet test` (SDK 10.0.402 biçimi kabul ediyor, ölçüldü; yine de `--project`'e çevrildi) |
| Fazın ürettiği regresyon | 1 (yukarıdaki iç numara; kapı yakaladı, aynı oturumda kapandı) |
| Faz kapandıktan sonra bulunan kusur | 0 |

## Denetim Bulguları

`faz-denetcisi` (taze bağlam, salt-okunur), `a0ec0223..HEAD`. Odak: kapı mantığı, CI, F-568, bağımlılıklar, istemci farkı, dokümanlar.

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | `release_extension_samples.verify` konumsal `dotnet test <csproj>` koşuyordu | gürültü (10.0.402 kabul ediyor) ama yön doğru | düzeltildi (`--project`); `kapi.py yayin --kuru` EXIT 0 |
| 2 | 🔴 | Sevk edilen `///` satırında iç numara; `ShippedDocumentationSelfContainmentTests` kırmızı | gerçek | düzeltildi (`9f0bc848`); tam kapanış yeniden koşuldu |
| 3 | 🟡 | xunit alt sınırını (K-1018) hiçbir kapı sabitlemiyor; plan bir nuspec iddiası vaat ediyordu | gerçek | düzeltildi: `kapi.py` `dependency_floor_errors` + 2 test + gerçek nuspec doğrulandı |
| 4 | 🟡 | Konumsal `dotnet test` kalıntıları (README, 3 skill, docs-site betiği; 28 test dosyasında yenileme komutları) | gerçek / ucuz kısmı | skill, README, betik düzeltildi; test dosyalarındaki komut metinleri **devredildi** (F-573: konumsal biçim ölçüldü, çalışıyor) |
| 5 | 🟡 | `ValidatePricing`'in `<summary>`'si `ValidateUtilityModel`'in üstünde kalmıştı | gerçek | düzeltildi (yer değişti) |
| 6 | 🟡 | `build-ve-analyzer.md`, `YAYIN-HAZIRLIK.md` A-70 ve `mercekler.md` bu fazın ölçümüyle çelişiyordu | gerçek | düzeltildi; A-70 kapandı olarak işaretlendi |
| 7 | 🟡 | DoD kanıt boşlukları (`yayin --kuru` koşulmadı, `24-…` case'i yok, son kapanış kırmızı) | gerçek | `yayin --kuru` EXIT 0, `MT-TEST-098` yazıldı ve koşuldu, kapanış yeniden koşuldu |
| 8 | 🟢 | Skaler `TryParse`-atla deseni (~132 satır) okunamayan değeri yutuyor | gerçek, kapsam dışı | **F-572** (🟡) |
| 9-10, 12 | 🟢 | `_trx_output_directory` `codeBase` yoksa `"release"`; bayat yorumlar; test dosyalarındaki komut metinleri | gerçek, küçük | **F-573** |
| 11 | 🟢 | `test-kosum-tuzaklari.md` sıkıştırması üç ayrıntıyı düşürdü | gerçek | *Plandan Sapmalar* 9 düzeltildi |
| 12 | 🟢 | Tüketici metni xunit 4'ün `global.json` şartını söylemiyordu | gerçek | README ve rehber cümlesi eklendi (ölçüldü) |
| 13 | 🟢 | `nswag.json` `runtime` kuralı yalnız faz dokümanındaydı | gerçek | `nswag-istemci-uretimi.md`'ye yazıldı |
| 14 | 🟢 | CHANGELOG "boş değer" cümlesi dardı | gerçek | "boş ya da boşluk" yazıldı |

Denetçinin kendi eylem açıklaması: iki geçici dosyayı depo dışında (scratchpad) yönlendirmeyle yazdı ve `dotnet test --help` çalıştırdı (test ikililerini `--help` ile başlatır); depoya dokunmadı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım kalan işler, sıradaki faz.

**Kapanış kapısı:** `python3 scripts/kapi.py kapanis --taban a0ec0223` — 11 adımın hepsi yeşil (2026-10-16): `tarama` · `dokuman-bakim --denetle` · `unittest` (scripts) · `llms-full --check` · `denetim-paketi` · `dotnet build` (0 uyarı, 97,6 sn) · `dotnet test` **23.409 test, 38 modül, 0 kırmızı** (699 sn; ilk koşum `ShippedDocumentationSelfContainmentTests` ×3 TFM ile kırmızıydı, düzeltme sonrası temiz) · `dotnet pack` · `dotnet format --verify-no-changes` · `performans` (üç benchmark tabanda: 0 B · 112 B · 48.184 B) · `docs-site npm run check`. MinVer 8: paket sürümü `1.0.0-preview.4.52` = `git rev-list --count v1.0.0-preview.4..HEAD` (52); sürüm kaymadı. `kapi.py yayin --kuru --surum 1.0.0-preview.5.215` EXIT 0 (20 paket, kırıcı liste `0 TFM düşüşü`, `npm publish --dry-run`, altı örnek, Native AOT ve net8 smoke).
