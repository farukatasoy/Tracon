# Faz 97 — Sürüm Politikası ve Yayın Provası

> **Durum:** ✅ Tamamlandı (2026-08-24)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **madde 2** (stabil sürüm MAF ön sürümüne yapısal olarak bağlı) + **madde 1** (1.0 yok; public API'nin tamamı `Unshipped`). Kalemler `ADAYLAR.md`'de değildir; F numarası yoktur. Sıra bölüm 7.2'de kullanıcı tarafından sabitlendi (sıra 4).
> **Önkoşul:** [Faz 96](96-PUBLIC-YUZEY-KUCULTME.md) — yüzey küçültme yayından **önce** bitmeliydi; bitti (618 tip). Yayın anından sonra aynı iş bir sürüm kararı olurdu.
> **Paketler:** `src/` altındaki **19** paketin hepsi. Kod değişmez; `src/Directory.Build.props`, 19 `.csproj` ve CI değişir.
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Değişmiyor.** `PublicAPI.Unshipped.txt` dosyalarına satır eklenmez, silinmez. `Shipped.txt` dosyaları **boş kalır** — bkz. 97.1, karar 2.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/reference/compatibility.md` (başlık ve tablo **17 → 19**; `AgentPrism.Client` ve `AgentPrism.Cli` satırları eklenir) · `reference/versioning.md` (tek sürüm hattı ve `Shipped` politikası beyanı) · sevk edilen: **19 `.nupkg`'nin hepsi `icon.png` kazanır** — nuget.org paket kartında ikon görünür; paket `README.md`'leri değişmez
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) — alan kodu `PKG`, sıradaki case **MT-PKG-097**

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show de5c2af:docs/arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md
> ```
>
> Damıtıldı 2026-08-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism 97 faz ve 337 commit sonra hâlâ **yayınlanmadı**. Depoda tek bir sürüm etiketi yoktur, yani hiç kimse `dotnet add package AgentPrism` diyemez. Bu faz yayının önündeki her yapısal engeli kaldırır ve yayını **prova edilebilir** hâle getirir: sürüm politikası karara bağlanır, paketlerin içeriği bir kapıya bağlanır ve yayın işi o kapı geçmeden koşamaz.

## Bitiş Ölçütleri (DoD)

- [x] `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` yeşil; çıktı 19 paket kimliğini ve sürümünü listeliyor ve belgeye yazıldı — bkz. MT-PKG-097 kaydı
- [x] Prova sonrası `git tag` **yalnız** `docs/damitma-oncesi-2026-08` gösteriyor — komut depoyu kirletmedi (MT-PKG-097'de doğrulandı)
- [x] `ReleaseArtifactTests` yeşil; beş iddiayı da kanıtlıyor (kimlik kümesi · sürüm · ikon · metaveri · K-008 ön sürüm sınırı) — `5/5` geçti
- [x] `unzip -l` ile **19 paketin hepsinde** `icon.png` doğrulandı (MT-PKG-098) — 19/19, sıfır eksik
- [x] `EnablePackageValidation=true`; `dotnet pack` sıfır uyarı. Bastırılan hiçbir kural yok, ya da bastırılan her kuralın gerekçesi karar defterinde — hiçbir kural bastırılmadı, `dotnet pack AgentPrism.src.slnf -c Release` temiz
- [x] `ci.yml`: `release-dryrun` işi var; `publish` **ve** `npm-publish` işlerinin `needs:` satırı onu içeriyor — `grep -n "needs:" .github/workflows/ci.yml` → `publish: needs: [pack, release-dryrun]` (satır 262), `npm-publish: needs: [build, release-dryrun]` (satır 297)
- [x] `compatibility.md` başlığı "The 19 packages"; tablo 19 satır; `Client` ve `Cli` satırlarının AOT sütunu **ölçülerek** dolduruldu — `grep -l "AotCompatible>false" src/*/*.csproj` ikisini de listeledi (No)
- [x] `check-content.mjs` yeni iddiayı taşıyor; MT-PKG-099 koşuldu ve kapı **kırıldı** — satır silinince "18 row(s), expected 19", geri alınca temiz
- [x] `versioning.md` tek sürüm hattını ve preview hattı boyunca `Shipped`'in boş kaldığını beyan ediyor
- [x] 97.1'in iki kararı `docs/KARARLAR.md`'ye yazıldı; **K-068 kapatıldı** ve **K-421'in yeniden açılma notu** yeni politikaya göre güncellendi — K-602/K-603/K-604
- [x] `find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'` → **0**; `PublicSurfaceBaselineTests` hâlâ 618 tip görüyor (yüzey değişmedi) — ikisi de ölçüldü
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 2fa5a40`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — fresh SQLite: 24 migration uygulandı, `/health` `Degraded` (model provider yok, beklenen), host temiz açıldı/kapandı
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` → ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içine eklendi (MT-PKG-097..100); MT-PKG-100 dışındakiler koşuldu — 097/098/099 gerçekten koşuldu, 100 👤 yordamı belgelendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 2 🟡 bulundu ve aynı oturumda kapatıldı (bkz. "Denetim Bulguları")
- [x] `docs-site/` güncellendi; `npm run check` (dört alt kapı) temiz
- [x] **Etiket ATILMADI.** Faz, yordamı belgeleyip durur; `git push origin v1.0.0-preview.1` kullanıcının kararıdır (👤 2026-08-24)

### Doğrulama komutları

```bash
# Prova — aga hicbir sey yazmaz
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1

# Ikon 19 pakette mi
for f in artifacts/package/release/*.1.0.0-preview.1.nupkg; do
  unzip -l "$f" | grep -q icon.png || echo "IKON YOK: $f"
done

# K-008 siniri — yalniz AspNetCore on surum bagimliligi beyan etmeli
for f in artifacts/package/release/*.1.0.0-preview.1.nupkg; do
  id=$(basename "$f" .1.0.0-preview.1.nupkg)
  unzip -p "$f" "$id.nuspec" \
    | grep -oE 'id="[^"]*" version="[0-9]+\.[0-9]+\.[0-9]+-[^"]*"' \
    | grep -v 'id="AgentPrism' | sed "s|^|$id: |"
done
# Beklenen: yalnizca AgentPrism.AspNetCore satirlari

# Yayin kapisinin yerinde oldugu
grep -n -A2 "^  publish:\|^  npm-publish:\|^  release-dryrun:" .github/workflows/ci.yml

# Yuzey degismedi
find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'   # 0
find src -name PublicAPI.Unshipped.txt -exec cat {} + \
  | grep -vE '^\s*$|^#' | grep -vE ' -> |\(' | sort -u | wc -l               # 618
```

---

## Plandan Sapmalar

1. **`release-dryrun` CI işi kendi `dotnet pack`'ini koşar; `pack` işinin artifact'ini indirmez.**
   Plan iki farklı yerde iki farklı şey söylüyordu: 97.2'nin numaralı adımları
   ("1. Sürümü zorlar ve `dotnet pack` koşar") komutun **kendi başına** paketlediğini
   varsayıyordu; Riskler tablosundaki bir satır ise "ayrı bir `pack` koşmaz…
   `release-dryrun` işi `pack`'in artifact'ini indirir" diyordu. İkisi birlikte
   tutarlı değildi. Davranışsal sözleşme (numaralı adımlar) esas alındı: `kapi.py
   yayin` her zaman kendi `dotnet pack`'ini koşar — hem yerel prova hem CI için
   aynı komut, artifact indirme/yükleme borusu eklenmedi. `release-dryrun` işi
   `pack` işinden **bağımsız**, yalnız `build`'e bağımlı, **paralel** koşar.
2. **CI'daki `release-dryrun` çağrısı `--surum` GEÇMEZ.** Flowchart zaten bunu
   gösteriyordu (`kapi.py yayin --kuru`, sürüm yok) ama gerekçesi açık yazılmamıştı:
   gerçek bir `v*` etiketinde MinVer'in kendi hesapladığı sürüm zaten hedeftir,
   zorlamaya gerek yoktur. Zorlama yalnız **etiket atılmadan önceki** yerel
   provada (MT-PKG-097) anlamlıdır. `--surum` verilmeden koşulduğunda komut
   `1.0.0-preview.N` desenine uymayan bir sürümü **hata değil uyarı** olarak
   işaretler (`⚠️`) — CI'nin sıradan her push'ta yeşil kalması gerekir.
3. **🚨 Ölçülerek bulunan gerçek kusur: `--surum` verilmeden koşulduğunda bayat
   bir paket "en son yazılan" sanılabiliyordu.** İlk uygulamada
   `_resolve_nupkg`'in "en son yazılan dosyayı seç" sezgisi, artımlı `dotnet
   pack`'in değişmemiş bir projenin çıktısını YENİDEN ÜRETMEMESİNDEN
   yararlanan bir önceki `--surum` koşumunun bayat `.nupkg`'sini seçebiliyordu
   — canlı tekrar üretildi: `AgentPrism` ve `AgentPrism.Templates` iki koşumda
   da değişmediği için pack onları atladı, `1.0.0-preview.1` etiketli bayat
   dosyaları "en yeni" göründü ve "Paketler tek bir sürüm hattında değil"
   hatası üretti. Düzeltme: `dotnet pack` çalışmadan ÖNCE her izlenen projenin
   KENDİ önceki `.nupkg`/`.snupkg` dosyaları silinir (`_clean_stale_packages`)
   — böylece "en son yazılan" her zaman BU koşumun ürünüdür. `scripts/kapi_test.py`
   içine hem tekrar üreten hem düzeltmeyi kanıtlayan testler eklendi.
4. **Bağımsız denetimin bulduğu iki 🟡, aynı oturumda kapatıldı** (bkz. "Denetim
   Bulguları"): `kapi.py yayin` başta yalnız EKSİK paketi yakalıyordu (fazla/
   beklenmeyen paketi değil) ve TFM başına XML doküman varlığını
   doğrulamıyordu — ikisi de 97.2'nin kendi metninin vaat ettiği kontrollerdi.
   İki kontrol de eklendi, gerçek pakete karşı koşuldu (`--surum` ile ve
   olmadan) ve `scripts/kapi_test.py`'ye birim testleri eklendi.
5. **`compatibility.md`'nin AOT sütunu plan taslağının "ölçülecek" yer
   tutucusu yerine doğrudan ölçülmüş değerle yazıldı.** `grep -l
   "AotCompatible>false" src/*/*.csproj` her iki yeni satırda da (`Client`,
   `Cli`) dosyayı listeledi — ikisi de AOT **değil**.
6. **`docs-site/public/llms-full.txt` yeniden üretildi** (`node
   scripts/build-agent-map.mjs`). Planın dosya listesinde açıkça yoktu ama
   `compatibility.md`/`versioning.md` düzenlemesinin doğrudan sonucudur — bu
   dosya sevk edilen (commit'li) her elle yazılan sayfanın tam metnini taşır;
   düzenlemeden sonra yeniden üretilmezse `check-content.mjs` kırmızı kalır
   (ölçüldü, `docs-site senkronu` bölümünde).
7. **`docs/manuel-test/00-INDEKS.md` satır 01 güncellendi** (hedef case 48→52,
   Faz sütununa 97 eklendi, Koşum sütununa 🆕 notu) — plan dosya listesinde
   yoktu ama `faz-tamamlama` Adım 3'ün standart defter tutma işidir.

## Bu Fazda Verilen Kararlar

- **K-602** — Tek sürüm hattı: paketlenen 19 projenin hepsi `1.0.0-preview.N`
  (kullanıcı kararı). K-068'i kapatır.
- **K-603** — `PublicAPI.Shipped.txt` preview hattı boyunca boş kalır; 618
  tipin dolumu `1.0.0` GA'ya ertelendi (kullanıcı kararı). K-421'in yeniden
  açılma notunu günceller.
- **K-604** — Yayın işleri (`publish`, `npm-publish`) yayın provası kapısına
  (`release-dryrun`) bağlandı; kapı her push'ta (PR dahil) koşar.
- K-068 ve K-421'in kayıtları yukarıdaki üç kararı işaret edecek şekilde
  güncellendi (bkz. `docs/KARARLAR.md`).

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent, `faz-denetim` skill'i) 2026-08-24'te
koştu. Gerçek koşumlarla doğruladı: `dotnet pack` (19 paket, 0 uyarı,
`EnablePackageValidation=true` temiz), `dotnet build` (0 uyarı/hata), `dotnet
test tests/AgentPrism.Package.Tests` (43/43), `kapi.py yayin --kuru` (19 paket
+ `npm publish --dry-run` yeşil), `check-content.mjs` (temiz) ve MT-PKG-099'un
gerçekten kırıp geri döndüğü.

**🔴 Kapanmadan faz bitmez:** Yok.

**🟡 Aynı fazda kapanır veya gerekçelenir:**

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `kapi.py yayin`'in ilk hâli yalnız EKSİK paketi yakalıyordu, beklenmeyen (fazla) paketi yakalamıyordu — 97.2 madde 2 ve Hata Modları tablosu bunu açıkça vaat ediyordu | **Düzeltildi** — `release_rehearsal` artık `resolved_version`'a ait gerçek `.nupkg` kümesini `project_ids`'e karşı iki yönlü karşılaştırıyor (`unexpected` kontrolü). `kapi.py yayin --kuru [--surum]` her iki biçimde de gerçek pakete karşı koşuldu; `scripts/kapi_test.py` etkilenmedi (saf fonksiyon testleri zaten ayrı) |
| 2 | Aynı fonksiyon TFM başına `.xml` doküman varlığını doğrulamıyordu — 97.2 madde 4 bunu açıkça vaat ediyordu | **Düzeltildi** — `_target_frameworks` eklendi (Testing gibi tekil-TFM override'ları okur), `release_rehearsal` her `library`/`tool` profili için beklenen XML sayısını gerçek girişlerle karşılaştırıyor. `test_hedef_frameworkler_tekil_override_okur` eklendi; komut gerçek pakete karşı koşuldu |

**🟢 Aday listesine:** Yok.

**Temiz çıkan başlıklar:** 3.1 (kalan DoD — sürüm tekliği, ikon, metaveri,
K-008, `Shipped` boş kalma, `PublicSurfaceBaselineTests` 618 tip, KARARLAR/
KARARLAR-INDEKS senkronu), 3.2 (`denetim-paketi.py`'nin "iddiasız" işaretlediği
üç test — `EveryPackageCarriesRequestedVersion`,
`OnlyAspNetCoreDeclaresPrereleaseDependency`, `PackageIdsMatchProjectSet` —
üçü de gerçek `.ShouldBeEmpty(...)` iddiası taşıyor; işaret,
`denetim-paketi.py`'nin `\bShould\b` regex'inin Shouldly'nin bitişik
`Should*` CamelCase metotlarını (kelime sınırı yok) kaçırmasından doğan bilinen
bir yanlış pozitiftir — script davranışı, bu fazın kapsamı dışında), 3.5, 3.6,
3.7, 3.8 (ölçülerek dolduruldu, tahmin edilmedi).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `python3 scripts/kapi.py yayin --kuru [--surum <sürüm>]` — ağa hiçbir şey
  yazmaz. `--surum` verilmezse MinVer'in bugünkü değerini kullanır ve
  `1.0.0-preview.N` desenine uymuyorsa **uyarır**, hata vermez.
- CI'da `release-dryrun` işi her push'ta (PR dahil) koşar; `publish` ve
  `npm-publish` işleri ona `needs:` ile bağlıdır (`ci.yml:262`, `:297`).
- `assets/icon.png` her paketin köküne `PackageIcon` olarak paketlenir
  (`src/Directory.Build.props`); kaynağı `docs-site/public/favicon.svg`,
  yeniden üretme komutu `npm run build:icon` (docs-site içinde).
- `EnablePackageValidation=true` — `dotnet pack` artık TFM'ler arası (net8/9/10)
  API farkını denetler. `PackageValidationBaselineVersion` **henüz atanmadı**
  (ilk yayından sonra anlam kazanır, 97.4).
- `tests/AgentPrism.Package.Tests/ReleaseArtifactTests.cs` +
  `Infrastructure/{ReleaseArtifactFixture,PackableProjects}.cs` — beş
  davranışı (sürüm tekliği · kimlik kümesi · K-008 sınırı · ikon · metaveri)
  gerçek `dotnet pack` çıktısına karşı kanıtlar; `1.0.0-preview.1` sabit
  sürümüyle paketler (fixture-özel, `TemplateFixture`'ın doğal sürümünden ayrı).

**Bilinen tuzaklar (🚨):**
- 🚨 **Artımlı `dotnet pack` değişmemiş bir projenin çıktısını yeniden
  üretmeyebilir.** `--surum` olmadan "en son yazılan dosya" seçimi bu yüzden
  bayat bir paketi yanlışlıkla seçebilir — `kapi.py`'nin `_clean_stale_packages`'ı
  bunu her koşumda önler (kendi önceki artefaktlarını siler). Bu deseni elle
  tekrarlayan bir script yazarsan aynı tuzağa düşersin.
- 🚨 `denetim-paketi.py`'nin "iddiası olmayan test metotları" taraması
  Shouldly'nin `ShouldBeEmpty`/`ShouldContain` gibi bitişik CamelCase metot
  adlarını (`\bShould\b` kelime sınırı bulamıyor) kaçırıyor — yeni bir
  Shouldly testi yazınca bu "aday" listesinde görünmesi normaldir, denetçi
  dosyanın gerçek içeriğini okuyarak kapatır.
- 🚨 Yayın işi hâlâ **tetiklenmedi** — `git tag v1.0.0-preview.1` ve `git push
  origin v1.0.0-preview.1` kullanıcının kararıdır (bkz. `MT-PKG-100` yordamı,
  `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`).

**Yarım kalan iş:** Yok — faz kapsamı tamamlandı, etiket atma bilinçli olarak
faz dışında bırakıldı (Amaç bölümü).

**Sıradaki faz:** Blok B — madde 12 · 15 · 23
(`docs/kesif/2026-08-23-yapisal-sorun-envanteri.md` bölüm 7.6). Henüz
planlanmadı; `faz-planlama` skill'i ile yazılacak.
