# Faz 187 — Yayınlanmış Sürüme Karşı Kırıcı Değişiklik Kapısı

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-270** (triaj bulgusu `P6P9`, alt bulgu 6)
> **Önkoşul:** [Faz 185](185-KARDES-PAKET-SURUM-SABITLEME.md) ve [Faz 186](186-SCRIPT-IZNI-ICERIK-PINI.md) kapanmış olmalı (sıra kararı) · aynı turun `kusur-giderme` kulvarları ana dalda olmalı. Denetim: "Bu Faza Başlarken" adım 3 · etkileşim: 187.9
> **Paketler:** Kaynak kodu değişen paket yok. 17 `lib` paketinin paketleme davranışı değişir (187.4). Aparat: `scripts/`, `Directory.Build.targets`, `src/Directory.Build.props`, `CHANGELOG.md`
> **Yeni paket:** Yok — ApiCompat .NET SDK'nın içindedir; yeni NuGet bağımlılığı yok, K-007 gerekmez · **Migration:** Yok
> **Public API:** Büyümüyor, daralmıyor — C# yüzeyine dokunulmaz. `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya × 1 satır (`#nullable enable`), toplam 17 (ölçüldü 2026-09-23; K-603 gereği boş). Strict mode bundan sonra TFM'ye özgü public üyeyi reddeder (187.4)
> **Tüketici yüzeyi:** site: `reference/versioning.md` ("Release notes" bölümüne bir söz) · `reference/changelog` (üretilen, `docs-site/scripts/build-changelog.mjs`; joker satırı düzelince değişir)
> · sevk edilen: `CHANGELOG.md` (site sayfası ve GitHub Release gövdesi ondan üretilir). XML `<example>` yok · paket `README.md`'si yok · `capabilities.md` satırı yok — yayın süreci ürün yeteneği değildir
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` (`MT-PKG`) — numara uygulama anında alınır (bugün son `MT-PKG-129`; Faz 185 de ekleyebilir)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 967f410c:docs/arsiv/fazlar/187-KIRICI-DEGISIKLIK-KAPISI.md
> ```
>
> Damıtıldı 2026-09-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`1.0.0-preview.1` ve `.2` 2026-09-20'de nuget.org'a çıktı (`CHANGELOG.md:146`, `:182`). O günden beri public yüzey küçüldü: `git diff --numstat v1.0.0-preview.2 HEAD -- 'src/*/PublicAPI.Unshipped.txt'` → 5 ekleme, 656 silme (Unshipped toplamı 9.120 satır). Değişikliğin tek listesi elle yazılan `CHANGELOG.md`'dir; hiçbir kapı onu yayınlanmış pakete karşı denetlemez.

## Tasarım — bağlayıcı kararlar

Bakımcı engelleyici soruları plan yazılmadan önce yanıtladı. Aşağıdakilerin
hepsi **(kullanıcı kararı, 2026-09-23)**'dir ve tartışmaya açık değildir.

| # | Karar |
|---|---|
| 1 | Son yayınlanmış sürüme karşı paket doğrulaması **yalnız yayın provasında** koşar: `python3 scripts/kapi.py yayin` (CI `release-dryrun` her push'ta koşar). Her `dotnet pack`'te koşmaz. Günlük döngü ve K-603 değişmez. Makine üretimi kırıcı liste `CHANGELOG.md` ile karşılaştırılır |
| 2 | `CHANGELOG.md`'de adıyla geçmeyen kırılmış bir public tip **her koşumu** kırar, `release-dryrun` dahil. Notlar `changelog.read_release_notes` ile okunur, `section_body('Unreleased')` ile değil — etiket anında `[Unreleased]` başlığı yeniden adlandırılır (K-825) |
| 3 | Strict mode (TFM'ler arası aynı public yüzey) `src/Directory.Build.props`'ta **koşulsuz** açılır. `Directory.Build.targets:20-28`'deki ölü `EnablePackageValidationGate` bloğu silinir. `src/Directory.Build.props:86-94`'teki bayat "has not shipped yet" yorumu düzeltilir. Triajın "20 paket geçiyor" iddiası temiz bir ölçümle yeniden ölçülür |
| 4 | Taban sürümü **otomatiktir**: HEAD'den önceki son `v*` etiketi (`git describe`) |
| 5 | `CHANGELOG.md` her tipi **tam adıyla** anar. Joker satırı (`*ChatClientFactory` / `*ModelCatalog`) açık adlarla yeniden yazılır |
| 6 | nuget.org'da tabanı olmayan (ilk kez yayınlanacak) paket **açık bir csproj bayrağıyla** muaf tutulur |
| 7 | Taban paketi **izole bir `NUGET_PACKAGES`'a** restore edilir, asla geliştirici cache'ine değil |
| 8 | TFM düşürme (`PKV006`) **kırıcı sayılır**; `CHANGELOG.md`'de paket + TFM ile anılır. net8.0/net9.0 düşüşü 2026-11-10'dan sonraki ilk sürümde gelir (F-266) |
| 9 | Ölü `EnablePublicApiTracking` anahtarının akıbeti Açık Soru 2'dir |
| — | Triajın alt bulgusu (9) `NU5104` kapandı; yalnız referans verilir |

---

## Bitiş Ölçütleri (DoD)

- [x] 187.0'ın yedi ölçümü yazıldı: taban · 20 projenin her biri ve `IntermediateOutputPath`'i (17 lib: strict `EXIT=0`, semaphore bu koşumdan; `Tracon`, `Tracon.Templates`: doğrulandı veya boş karşılaştırma; `Tracon.Cli`: doğrulanmadı + sebep) · paket paket taban listesi · `PKV006` biçimi · zorlama · farksız pakette rapor · Core raporunun kayıt sayısı — ✅ "Ölçüm Sonuçları (187.0)" bölümü
- [x] `scripts/testdata/breaking-changes/core-preview2.xml` 187.0 adım 7'nin tam raporudur; `test_gercek_rapor_fikstur` onu okur — ✅ 216 kayıt; `test_gercek_rapor_fikstur` 71 tip bekler
- [x] `grep -rn "EnablePackageValidationGate" --exclude-dir=arsiv --exclude-dir=.git --exclude-dir=.claude --exclude-dir=artifacts .` → yalnız bu faz dokümanı — ✅ yalnız bu doküman
- [x] `grep -n "not shipped yet" src/Directory.Build.props` → boş — ✅ boş
- [x] `dotnet msbuild src/Tracon.Client/Tracon.Client.csproj -getProperty:EnableStrictModeForCompatibleTfms` → `true`; `StrictModeIsOnForEveryLibraryPackage` 17 pakette yeşil — ✅ `true`; 17 pakette yeşil
- [x] `grep -n '\*ChatClientFactory\|\*ModelCatalog' CHANGELOG.md` → boş; sekiz ad code span olarak geçer — ✅ boş
- [x] `python3 scripts/kapi.py yayin --kuru` temiz ağaçta çıkış 0; çıktı taban, "17 paket" ve `breaking-changes.json` yolunu yazar; ardından `git status --porcelain` ve `find src -name CompatibilitySuppressions.xml` boş; koşum süresi yazıldı — ✅ `f8f85052`, çıkış 0, 112,5 sn (MT-PKG-135)
- [x] Bilerek kırmızı koşum (Manuel 2): çıkış 1, eksik ad paketiyle, release dizinine yeni dosya yok — ✅ MT-PKG-136
- [x] `python3 -m unittest discover -s scripts -p '*_test.py'` yeşil; her Birim satırının testi var — ✅ 539 test
- [x] `python3 scripts/kapi.py test --proje Tracon.Package.Tests --sinif "*PackageBaselineWiring*"` yeşil — ✅ 9/9 (denetim düzeltmesinden sonra 79,6 sn)
- ⏳ CI `release-dryrun` bu fazın push'unda yeşil; koşum kimliği yazıldı — **açık:** push bakımcı eylemi (MT-PKG-142 ➜ CI, devir notu)
- [x] `docs/YAYIN-HAZIRLIK.md` package validation kutusu yeni kapıya bağlı; §13'ten taban doğrulaması çıktı, `Shipped` dolumu kaldı — ✅
- [x] İki K satırı kategori etiketiyle açıldı; yerel kararlar "Bu Fazda Verilen Kararlar"da — ✅ K-864, K-865
- [x] Açık Soru 1 ve 2 uygulandı; `python3 scripts/dokuman-bakim.py --denetle` çıkış 0 — ✅ AS 1 = C, AS 2 = B
- [x] Faz 191 sözleşmesi (187.9) devir notunda — ✅
- [x] `docs/hafiza/paketleme-ve-dagitim.md` yeni tuzakları taşır ve 16.000 B altındadır — ✅ 15.556 B
- [x] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — ✅ "Kapanış Kapısı" bölümü
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (komut aşağıda) — ✅ "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş döndü: `python3 scripts/kapi.py tarama` — ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içine eklendi; otomatikleştirilebilenler koşuldu — ✅ MT-PKG-135…144; 142 ➜ CI
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — ✅ 🔴 yok, 3 🟡 düzeltildi
- [x] `docs-site/` güncellendi (`reference/versioning.md`; `reference/changelog` üretildi); `npm run build` + `check-links.mjs` temiz — ✅ `versioning.md`; `changelog` sayfası `[Unreleased]`'i atlar (Sapma 6); agent haritası yeniden üretildi

### Doğrulama komutları

Kapı ve grep komutları DoD satırlarındadır. Örnek uygulama koşumu:

```bash
REPO=$(git -C /Users/farukatasoy/Desktop/projects/Tracon rev-parse --show-toplevel)
# Yerelde Faz 183'ün notu geçerlidir: DOTNET_ROOT=~/.dotnet
# Tarif: docs/hafiza/elle-kosum-ortami.md "Uygulamayı başlatma"; ortam (Staging, geçici SQLite):
# awk '/## Örnek Uygulama Koşumu/,/## Kapanış Kapısı/' docs/arsiv/fazlar/183-COKLU-TFM-TEST-MATRISI.md
dotnet build "$REPO/samples/Tracon.Api" -c Release
curl -s http://127.0.0.1:5199/tracon/api/diagnostics            # 200, canConnect: true
curl -s -N -X POST http://127.0.0.1:5199/tracon/api/agents/support/run \
  -H 'Content-Type: application/json' -d '{"message":"ping"}'  # SSE: run · update · done
```

---

## Ölçüm Sonuçları (187.0) (2026-09-24, HEAD `fa34f7ed`, SDK 10.0.100, macOS)

1. **Taban:** `v1.0.0-preview.2` (güvenlik sürümü kesilmedi).
2. **Strict mode:** `dotnet pack Tracon.src.slnf` + iki strict özelliği → `EXIT=0`, 64 sn, sıfır
   hata/uyarı. 17 lib: semaphore bu koşumdan · `Tracon` (meta): semaphore yeni, `lib/` yok → boş
   karşılaştırma · `Tracon.Templates`: aynı · `Tracon.Cli`: semaphore **yok** — SDK
   `Microsoft.NET.PackTool.targets:47` `PackAsTool` için `EnablePackageValidation=false` yazar
   (`-getProperty` doğruladı). `-getProperty:IntermediateOutputPath` 20 projede de
   `artifacts/obj/<P>/release/` verir; diskte 19 semaphore `Release/` altında (APFS'te eski klasör
   adı kalır), Templates `release/`. Taze worktree'de hepsi `release/` (MT-PKG-136). Lib kırmızı
   yok → Açık Soru 4 uygulanmadı.
3. **Taban listesi:** çıkış 1; 279 `CP0001` + **9** `CP0002` (3 üye × 3 TFM). 93 ayrık tip:
   Abstractions 11 · Anthropic 2 · Azure 2 · Core 69 · Google 2 · OpenAI 3 · PostgreSql 1 ·
   SqlServer 1 · Sqlite 1 · Voice 1. Üyeler: `TenantProviderCredentialResolver` ctor +
   `ValidatePrefix` ve Faz 186'nın `SandboxedSkillScriptRunner.RunStoredScriptAsync`'i.
4. **`PKV006` biçimi:** `DiagnosticId=PKV006`, `Target=net8.0` (TFM), **`Left`/`Right`/
   `IsBaselineSuppression` yok**. Dosya `scripts/testdata/breaking-changes/voice-net10-only.xml`.
   Planın komutu (`-p:TargetFrameworks=net10.0`) çalışmadı: global özellik
   `ProjectReference`'a akar ve `Tracon.Generators` (`netstandard2.0`) `NETSDK1005` verir;
   TFM worktree'de csproj'a yazıldı.
5. **Zorlama:** yeni `ApiCompatSuppressionOutputFile` yolu hedefi yeniden koşturur (ikinci
   koşumda semaphore 10:05:59 → 10:06:03). Kontrol: yol verilmeden üçüncü pack semaphore'a
   **dokunmadı** — kapatılan sessiz yeşil budur.
6. **Farksız paket:** SDK rapor dosyasını **hiç yazmaz**. Kanıt semaphore'da kalır (187.5
   varsayılanı); Faz 191 sözleşmesinin (3) birinci seçeneği geçerlidir.
7. **Core fikstürü:** çıkış 0; 216 kayıt (207 `CP0001`, 9 `CP0002`), hepsi
   `IsBaselineSuppression=true`, 59.992 B, UTF-8 BOM, mutlak yol yok; `src/**/CompatibilitySuppressions.xml`
   yok. İndirgenmiş: 71 tip (69 + 2 üye tipi) → `core-preview2.xml`.

🚨 Ölçüm sırasında: `$TMPDIR` altındaki worktree'de (`/var` → `/private/var`) `.editorconfig`
uygulanmadı ve MA0048 hata oldu; worktree `pwd -P` yoluna taşınınca geçti.

## Plandan Sapmalar

1. **`PKV006` kaydı `IsBaselineSuppression` taşımaz** (187.0 adım 4). Planın tablosu "ilk
   eşleşen kural" sırasında `IsBaselineSuppression=false`'u ilk sıraya koyuyordu; bu sıra
   her düşen TFM'i strict hata sayardı. Gerçek sıra: `PKV006` → taban dışı (strict) →
   `CP*` + DocId → tanınmayan. Strict kayıt da alanı taşımaz; ayrım tanı koduyladır.
2. **`baseline_package_ids(root, library_ids)`** — plan tek parametre diyordu. Paket
   profili kuralı `kapi.py`'de (`_package_profile`) yaşar; `breaking_changes.py`
   onu kopyalamaz (senkronizasyon kopyası sınıfı), listeyi çağırandan alır.
3. **`release_notes_for(changelog, version)`** — `baseline` parametresi gerekmedi. Açık
   Soru 1 = C'nin mesajı ayrı `untagged_cut_hint(changelog, baseline)` ile üretilir.
   Ek public yardımcılar: `compare_versions`, `version_is_above_baseline`,
   `has_first_release_flag`, `pack_properties`, `write_result`, `check` (kapının tek
   çağrısı; CLI ile `kapi.py` aynı yolu kullanır).
4. **`TRACON0007`** (planda yoktu): `TraconPackageBaselineRoot` verilip sürüm veya rapor
   dizini verilmezse pack durur. Türetme koşulu üçünü birden ister; aksi hâlde taban
   sessizce atlanırdı.
5. **Paket testi `TraconSkipCleanWorkingTreeCheck` kullanmaz.** K-661 bu bayrağın yeni
   çağıran noktasını yasaklar. `SecondPackStillValidates` mevcut yerel kaçışı kullanır:
   `TraconAllowDirtyPack=true` + `MinVerVersionOverride=0.0.0-dirty.baseline-wiring`
   (emsal `PackCleanlinessGateTests.OverrideWithDirtyVersionPacksSuccessfully`), `CI`
   boşaltılır. CI'da ağaç temizdir ve kaçış etkisizdir.
6. **Site `reference/changelog` değişmedi.** `build-changelog.mjs` `[Unreleased]`'i bilerek
   atlar; joker düzeltmesi sayfaya ancak sürüm kesilince girer. Plan "joker satırı düzelince
   değişir" diyordu — yanlıştı.
7. **187.0 adım 4'ün komutu çalışmadı.** `-p:TargetFrameworks=net10.0` global özellik
   olarak `ProjectReference`'a akar; `Tracon.Generators` (`netstandard2.0`) `NETSDK1005`
   verir. TFM worktree'de Voice csproj'una yazıldı.
8. **Ölçümlerde worktree `pwd -P` yolunda açılır.** `$TMPDIR` altındaki worktree'de
   (`/var` → `/private/var`) `.editorconfig` uygulanmadı, MA0048 hata oldu.
9. **`Tracon.Cli` doğrulanmaz** — SDK kararı: `Microsoft.NET.PackTool.targets:47`
   `PackAsTool` için `EnablePackageValidation=false` yazar. Kapının kanıt kümesi zaten
   yalnız library paketleridir.
10. **Plan dışı düzeltme:** `CHANGELOG.md`'nin "A version section is not written ahead of
    time" paragrafı Deprecated kulvarından sonra `### Changed` ile `### Deprecated`
    arasında kalmıştı; `[Unreleased]`'in sonuna taşındı. Notların okunuşu değişmedi.
11. **`kod-haritasi.md` ve `00-INDEKS.md`** satırları taşındı (plan `:14-15`, `:734-739`
    diyordu; içerik aynıydı).

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

**K satırları:** **K-864** (yayın provası kırıcı değişiklik kapısı, `public-api`) ·
**K-865** (strict mode, `public-api`, `geri-dönüşü-pahalı`). K-421'e Açık Soru 2 = B notu
eklendi.

**Açık Sorular (kullanıcı kararı, 2026-09-24):** AS 1 = **C** — kod mekanizması değişmez;
kesim commit'i ve etiket `git push --atomic origin main v<sürüm>` ile birlikte itilir;
kapı notu bulamazsa ve CHANGELOG'da tabanın üstünde etiketsiz bir sürüm bölümü varsa
mesaj bu komutu yazar. AS 2 = **B** — kök `EnablePublicApiTracking` silindi;
`dokuman_iddia_cakismalari` `src/Directory.Build.props`'taki
`TraconPublicApiTrackingEnabled` varsayılanını denetler ve ölü adı skill'de bulgu sayar.
AS 3 = A (tip adı notun herhangi bir yerinde, code span) · AS 4 = A (uygulanmadı: lib
kırmızı yoktu) · AS 5 = A (restore hatası kırmızı, mesaj yayını tamamlamayı söyler).

**Yerel kararlar (K almaz):**

| Karar | Gerekçe |
|---|---|
| İki adımlı izole restore: depo dışı `mkdtemp`, `<clear/>` + nuget.org, `--packages` + `NUGET_PACKAGES`, `Directory.Build.*` ve CPM kapalı | Geliştirici cache'i ve repo props'u tabana değmez; `.nupkg.metadata` kaynağı kanıttır |
| Rapor dizini `artifacts/package/api-compat/run-<rastgele>/`, koşum sonunda silinir | Yok olan çıktı yolu doğrulamayı zorlar (187.0 adım 5); kalıcı sonuç `breaking-changes.json`'dur |
| Kanıt: semaphore `mtime >= floor(pack başlangıcı)`; klasör harf duyarsız `release`, tam bir eşleşme | Farksız pakette rapor yazılmaz (187.0 adım 6); saniye çözünürlüklü dosya sistemi |
| Kimlik `$(MSBuildProjectName)` | Depo sözleşmesi proje adı = paket kimliği |
| Tanı kodu tablosu (Sapma 1) ve code span kuralı: `<…>`, `(…)` atılır, `.` parçası tam eşit; TFM için paket ve TFM aynı liste maddesinde tam span | Karar 5 ve 8 |
| `breaking-changes.json` `artifacts/package/` altında, release dizini dışında; koşum başında silinir | Release dizini örneklerin feed'idir; kırmızı koşumdan sonra eski yeşil sonuç kalmaz |
| Git yok / etiket yok / sığ klon / tabanda eşit sürüm → kırmızı | Taban adımının ikinci savunma hattı yoktur |
| Bayrak adı `TraconPackageFirstRelease`; bayat bayrak `git show <etiket>:src/<P>/<P>.csproj` ile, ağsız | Karar 6 |
| `TRACON0007` (Sapma 4) | Üç özellik birlikte gelir |

## Örnek Uygulama Koşumu

`samples/Tracon.Api` (`dotnet build -c Release`, `--contentRoot` ile DLL, Production, `echo`):
`GET /tracon/api/diagnostics` → `canConnect: true`, `migrationsUpToDate: true`;
`POST /tracon/api/agents/support/run` `{"message":"Hello from phase 187"}` → SSE `event: run` ×1 ·
`event: update` ×5 · `event: done` ×1; `GET /tracon/api/runs/01a0d261-c5e1-7f7e-b6ca-f8067431e719`
→ `Completed`. Faz çalışma anı koduna dokunmadı; koşum regresyon kontrolüdür.

**Yayın provası** (`kapi.py yayin --kuru`, temiz ağaç `f8f85052`, 2026-09-24) → **EXIT 0,
112,5 sn**: `Taban: v1.0.0-preview.2 (git describe)` · `Taban paketleri izole cache'ten: 17
paket, kaynak api.nuget.org` · `✅ Kırıcı liste: 95 tip, 0 TFM düşüşü, 10 paket — hepsi
'Unreleased' notunda` · 20 paket `1.0.0-preview.2.53` · npm dry-run ✅ · AOT smoke ✅ ·
net8.0 smoke ✅ · 6 exact-version sample ✅. Sonra `git status --porcelain`, `find src -name
CompatibilitySuppressions.xml`, `artifacts/package/api-compat/` boş. Manuel case'ler:
MT-PKG-135…141, 143, 144 koşuldu (sonuçlar case'lerde); 142 ➜ CI.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: arşivdeki her
> `- [ ]` satırı `tamamlanmis_faz_isaretsiz_kutular()` kapısında ayrıca hata
> sayılır ve bulgunun kaynağı bulanıklaşır.
>
> `dokuman-bakim.py --denetle` 14. kapısı (`surec_olcumu_bulgulari`) bu tabloyu
> **eşik 167**'den itibaren her kapanmış fazda arar. Boş bir değer hücresi
> kırmızıdır; `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir
> **karar** arar. Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez
> (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (11 sapma yazıldı, plan yeniden yazılmadı) |
| Düzeltme turu sayısı | 4 (denetim 🟡 turu · CHANGELOG paragraf konumu · kapanış: agent haritası · kapanış: iki mimari testi) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (`kapi_test.py` git-yok testi davranış değişikliğiyle güncellendi; regresyon değil) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

Denetçi: `faz-denetcisi` (salt-okunur alt agent), taban `fa34f7ed` → çalışma ağacı,
2026-09-24. **🔴 yok** (triyaj gerekmedi).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `SecondPackStillValidates` semaphore'u `başlangıç − 1 sn` ile kıyaslıyordu; önceki pack'in son dokunuşu bu pencereye düşerse atlanan doğrulama da geçerdi | 🟡 | **Düzeltildi** — her koşum önceki semaphore zamanını kaydeder, 1,1 sn bekler, kesin büyük ister; 9/9 yeşil |
| 2 | Paket testi `TraconPackageFirstRelease` taşıyan bir library'yi hesaba katmıyordu; `faz-tamamlama`'nın yeni maddesi ilk uygulandığında test kırmızı olurdu | 🟡 | **Düzeltildi** — `HasFirstReleaseFlag` (aynı metin kuralı); bayraklı library boş yol bekler |
| 3 | Site "by its full name" diyordu; kapı ad alanısız basit adla eşler | 🟡 | **Düzeltildi** — "by its exact name" |
| 4 | Gerçek `PKV007` büyük olasılıkla `IsBaselineSuppression` taşımaz → "strict" diye etiketlenir; iç içe liste TFM eşleşmesini kaçırır | 🟢 | **F-285** |
| 5 | Bütünüyle kaldırılan paket denetlenmez | 🟢 | **F-286** |
| 6 | `dokuman_iddia_cakismalari` proje başına `false` cümlesini de çelişki sayar | 🟢 | **F-287** |

Plan kaynaklı ek aday: `ReleaseArtifactFixture` gölgesi → **F-288**.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

**Sıradaki faz: [188](../../188-DI-KURUCU-DARALTMA.md)** — kırıcı değişiklik kapısının ilk
gerçek tüketicisi. Kapının son biçimi:

- Kurucu daraltması `CP0002` üretir (`M:Ns.Tip.#ctor(...)`); kapı onu **bildiren tipe**
  indirger. `CHANGELOG.md`'de tip adı code span olarak geçmelidir: `` `RunEventWriter` ``
  veya `` `RunEventWriter.RunEventWriter(...)` `` kabul; `` `*Writer` `` ve çıplak (span'siz)
  ad kabul **değil**. Tip notun herhangi bir yerinde geçebilir (AS 3 = A).
- Kendi değişikliğini doğrulamak için: `python3 scripts/kapi.py yayin --kuru` temiz ağaçta
  (commit gerekir). Eksik ad çıktıda `Tracon.Core: <Tip>` satırıdır. Ağ ister (nuget.org).
- `artifacts/package/release` eski damga taşıyorsa örnek sözleşmesi durur (`release feed
  contains stale Tracon packages`) — `rm -rf artifacts/package/release`.
- Yeni library paketi ekleyen faz csproj'a `<TraconPackageFirstRelease>true</…>` yazar ve
  ilk yayından sonra kaldırır (`faz-tamamlama` Adım 1).

**Faz 191'in taşıyacağı sözleşme (187.9):**

1. Tek pack komutu üç `-p` alır: `TraconPackageBaselineRoot`, `TraconPackageBaselineVersion`,
   `TraconApiCompatReportDir` (`breaking_changes.pack_properties`). `PackageValidationBaselineVersion`
   **verilmez** (geliştirici cache'i tuzağı).
2. Taban restore'u (`restore_baselines` + `baseline_source_violations`) aynı işte pack'ten
   **önce** koşar; geçici cache depo dışında, `finally`'de silinir.
3. **187.0 adım 6: farksız pakette SDK rapor YAZMAZ.** Koşum kanıtı semaphore'dadır ve pack'i
   koşan işte hesaplanır (`validation_not_run(root, library_ids, pack_started)`); raporlar
   yalnız fark olan paketler için vardır. 191'in manifest kaydı "rapor yok"u meşru bir
   değer olarak taşımalıdır — "eksik rapor"u kırmızı yapmak her farksız paketi kırar.
4. `_finish_release_rehearsal` artık zorunlu `breaking_gate` alır; kapı sürüm çözümünden
   sonra, promote'tan önce koşar. `--paket-dizini` modunda kanıt adımı taşınmaz, rapor
   okuma ve not eşleşmesi (`breaking_changes.check`) taşınır.

**Açık iş:**

- ⏳ **CI `release-dryrun` (MT-PKG-142) ölçülmedi** — bu fazın commit'leri push edilmedi
  (bakımcı eylemi). İlk Linux koşumu semaphore klasörünü `release/` görmelidir; log
  `Taban: v1.0.0-preview.2` ve `17 paket` taşımalıdır. Ağ restore'u koşuma ~dakika ekler.
- Site yayını (`faz-tamamlama` Adım 10, `reference/versioning.md`) bakımcı onayı bekler.

🚨 Tuzaklar (ayrıntı `hafiza/paketleme-ve-dagitim.md` "Yayınlanmış sürüme karşı
doğrulama" ve `hafiza/yayin-ve-surumleme.md` "Sürüm kesimi ve etiket TEK push'ta"):

- `RunPackageValidation` artımlıdır; yeni rapor yolu olmadan ikinci pack doğrulamayı atlar.
- Rapor yolu boşsa SDK `src/<P>/CompatibilitySuppressions.xml` yazar ve kırılma kalıcı gizlenir.
- Kesim commit'i etiketsiz itilirse prova kırmızıdır (AS 1 = C).

## Kapanış Kapısı

`DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py kapanis --taban fa34f7ed`, commit'li ağaç
`50b50444`, 2026-09-24 → **EXIT 0, 810 sn**:

| Adım | Süre | Sonuç |
|---|---|---|
| `kapi.py tarama` | 5,6 sn | ✅ temiz |
| `dokuman-bakim.py --denetle` · Python testleri · ajan haritası · denetim paketi | ~8 sn | ✅ |
| `dotnet build Tracon.slnx -c Release` | 73,1 sn | ✅ 0 uyarı |
| `dotnet test … -maxcpucount:2 -- --report-trx` | 548,8 sn | ✅ 17.585 test (38 koşum), 0 kırmızı |
| `dotnet pack` | 8,6 sn | ✅ |
| `dotnet format --verify-no-changes` | 132,6 sn | ✅ |
| `docs-site npm run check` | 33,1 sn | ✅ |

İlk iki deneme kırmızıydı (düzeltme turları): (1) `versioning.md` değişikliği sevk edilen
agent haritasını (`llms-full.txt`) bayatlattı — yeniden üretildi; (2) iki mimari testi:
paket testindeki `Task.Delay` `// delay: <class>` etiketi taşımıyordu (`product` eklendi) ve
`src/Directory.Build.props` yorumunda Türkçe "Faz" kelimesi `SourceLanguageTests` tabanını
büyütüyordu (İngilizceye çevrildi). 🚨 `kapi.py test --sinif A --sinif B` yalnız sonuncuyu
koşar; iki sınıf ayrı çağrıyla doğrulandı.
