# Faz 191 — Tek Derleme Zinciri

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-273** — triaj N12'nin (b) parçası = `YAYIN-HAZIRLIK` açık kalemi **A-15** ("kapı kendi bastığı byte'ları doğrulamıyor"); bu faz A-15'i tamamen kapatır. (a) parçası = **A-8** iş süre sınırları, 2026-09-23 `kusur-giderme` turu.
> **Önkoşul:** [Faz 187](187-KIRICI-DEGISIKLIK-KAPISI.md) — iki faz da `kapi.py yayin`'i değiştirir; 187'nin pack anı adımları `paketle`'ye taşınır (191.8) · 2026-09-23 `kusur-giderme` turu: her `ci.yml` işinde `timeout-minutes` ve `zaman_siniri_olmayan_isler` kapısı; sınırlar ve kapı yeşil kalmalı · Faz 185 → 190 bu fazdan önce kapanır
> **Paketler:** Yok — sevk edilen içerik değişmez. Dokunulan: `ci.yml`, `kapi.py`, `dokuman-bakim.py`, iki script testi, `Directory.Build.targets` (yalnız K-661 yorumu), `WorkflowWorkspacePathsTests.cs` (Açık Soru 2)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor, daralmıyor — `src/` altında imza değişmez. Ölçüm (2026-09-23): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır; her dosya yalnız `#nullable enable` taşır (Shipped boş, K-603)
> **Tüketici yüzeyi:** site: Yok — `git grep -in "reproducib\|provenance\|built by CI" -- docs-site/src/content/docs` 18 satır bulur; hepsi sürüm sabitleme veya `run`/skor provenance'ıdır, CI zincirini anlatmaz
> · sevk edilen: Yok — `.nupkg` içeriği, XML `<example>`, `src/*/README.md` ve `capabilities.md` satırı değişmez; `CHANGELOG.md` satırı Açık Soru 3
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) — yeni case'ler oraya; `MT-PKG-100` güncellenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 0ee4ad97:docs/arsiv/fazlar/191-TEK-DERLEME-ZINCIRI.md
> ```
>
> Damıtıldı 2026-09-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün bir `v*` etiketi üç ayrı derleme üretir: build işi birini test eder, `release-dryrun` kendi paketini üretip doğrular ve atar, `pack` işi taze bir runner'da yeniden derler. `publish` bu son dosyayı denetimsiz nuget.org'a iter. Üç derleme hiç karşılaştırılmaz ve NuGet'te geri dönüş yoktur.

## Bitiş Ölçütleri (DoD)

- [x] `grep -c "^  pack:" .github/workflows/ci.yml` → `0`; yorum dışı `dotnet pack` yok — ✅ `0`; yorum dışı `dotnet pack` yok (değişmez 2 kapıda)
- [x] `grep -v '^[[:space:]]*#' .github/workflows/ci.yml | grep -c 'kapi.py paketle'` → `1`; yapısal kapı `--denetle`'de yeşil; altı değişmezin her biri için kırmızı vaka — ✅ `1`; "CI tek derleme zinciri" ✅ temiz; `dokuman_bakim_test.TekDerlemeZinciriTestleri` 21 vaka (her değişmez ≥1 negatif)
- [x] Case 1 beklenen sonucu tuttu (187 satırları dahil); girdi dizini koşumdan sonra aynı — ✅ MT-PKG-153: `paketle` 15 sn, prova 70 sn çıkış 0, `dotnet pack` yok, 121 tip / 10 paket, girdi `diff` boş, `git status` ve `find` boş
- [x] Case 2 (2c dahil), 3, 4, 6: her bozulma çıkış 1, dosya/neden adıyla · case 5: üç TFM'de eşit — ✅ MT-PKG-154 · 155 · 156 · 158; case 5 = MT-PKG-157: üç TFM'de nupkg = test = src
- [x] Hata modu tablosunun her Birim satırı `kapi_test.py`'de bir vaka; mutasyon kanıtı kapanışta — ✅ `kapi_test.TekDerlemeZinciriTestleri` 31 vaka; mutasyon: on mutasyonun onu kırmızı ("Süreç Ölçümü")
- ⏳ Tag'siz CI yeşil (case 7, 👤); `TRACON0004` bypass'sız. Yeni süreler hafızanın "CI iş süreleri" yöntemiyle ölçüldü; `build` (100), `release-dryrun` (15), `publish` (10) yeniden hesaplandı; tablo güncellendi (`pack` satırı düştü) — **açık:** push bakımcı eylemi (MT-PKG-159 ➜ CI); yerel ölçüm ve sınır kararı "CI iş süreleri" tablosunda
- [x] 191.0-4 sonucu (veya "ölçülmedi" + gerekçe) kapanışta — ✅ ölçüldü: iki gerçek yeniden koşum, sabit adlı yükleme çakışmadı (Plandan Sapmalar 5)
- [x] Yeni K: `| **K-NNN — …** *(kategori: geri-dönüşü-pahalı)* *(kullanıcı kararı)* | …`; K-604 ve K-661'e atıf; kategori kapısı yeşil; `KARARLAR-INDEKS.md` üretildi — ✅ K-871; kategori kapısı yeşil; indeks üretildi
- [x] 191.9: `git grep -n "CI .pack. job" -- Directory.Build.targets scripts` → boş; `git grep -n "ci.yml:[0-9]" -- scripts/kapi.py scripts/kapi_test.py` → boş; `MT-PKG-100` case 9'un `awk`'ını taşır — ✅ ikisi de boş; MT-PKG-100 `awk`'ı taşır
- [x] `YAYIN-HAZIRLIK.md`: A-15 §13'ten çıktı; "Deterministic/reproducible" ikiye ayrıldı; `OP-010`'daki `nuget-publish` düzeldi — ✅
- [x] `hafiza/yayin-ve-surumleme.md`: "tek derleme zinciri" bölümü (sıra tuzağı, pack anı/artifact anı, depo imzası, 191.0 sonuçları; bugün 12.099 / 16.000 B, sığmazsa taşınır) — ✅ 15.509 / 16.000 B
- [x] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — ✅ `--taban bc1af904` EXIT 0 (Kapanış Kapısı)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (komut aşağıda; beklenen: HTTP `200`, gövde `runId` taşır; OpenAI anahtarı yoksa `echo` sağlayıcısı, `samples/Tracon.Api/Program.cs:479-485`). Fazın kendi tüketici koşumu case 1'in altı dış sample + AOT smoke'udur — ✅ HTTP `200`, SSE `event: run` `runId` `01a0d415-…`, `echo` sağlayıcısı (Örnek Uygulama Koşumu)
- [x] `secret` taraması boş döndü: `python3 scripts/kapi.py tarama` — ✅ `kapanis`'in ilk adımı
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içine eklendi; otomatikleştirilebilenler (1–6, 9) koşuldu; case 8 (👤) etiket gününe devredildi ve `YAYIN-HAZIRLIK` §13'e adım olarak bağlandı — ✅ MT-PKG-153…160 (8 case) + MT-PKG-100; 153–158 ve 100 koşuldu; 159 ➜ CI; 160 👤 `YAYIN-HAZIRLIK` §13'e bağlandı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — ✅ 🔴 0 (1 aday gürültü); 🟡1 ve 🟢1 düzeltildi, 🟡2 gerekçelendi (Denetim Bulguları)

Site ve arayüz etkisi yoktur; ilgili iki şablon satırı uygulanmaz.

### Doğrulama komutları

```bash
# Zincirin biçimi (yerel zincir: case 1)
grep -n "^  [a-z-]*:$\|kapi.py paketle\|--paket-dizini\|paket-dogrula\|nuget-packages\|nuget-verified" .github/workflows/ci.yml
python3 scripts/dokuman-bakim.py --denetle

# samples/Tracon.Api gerçek run
dotnet run --project samples/Tracon.Api -c Release &
sleep 10
curl -s -o /tmp/run.json -w "%{http_code}\n" -X POST http://localhost:5080/tracon/api/agents/support/run \
  -H "Content-Type: application/json" -d '{"message":"ORD-1001"}'
kill %1
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.


1. **191.0 ölçümleri** (HEAD `bc1af904`, taze klon `git clone --no-local`, CI ortamı):
   (1) önkoşullar birleşmişti: `timeout-minutes` 11 kez, `zaman_siniri_olmayan_isler` ve
   `_yaml_girinti` var, 187 arşivde; 187.0 adım 6 = "farksız pakette SDK rapor YAZMAZ".
   (2) `Derle` dahil her `run:` adımından sonra `git status --porcelain` **boş** → karar 3
   bir düzeltme gerektirmedi (macOS'ta ölçüldü; Linux ilk CI koşumunda, MT-PKG-159).
   (3) **Doğrulandı:** `ReleaseArtifact*` sonrası `Tracon.Core.dll` SHA-256'sı
   `dbda611e…` → `8abbc1b0…` (net10.0) ve `2a3aeb47…` → `f0d9c633…` (net8.0).
   (4) plandaki kaynak (`StudyZoneInt/Tracon`) anonim API'de `404`; public repo
   `farukatasoy/Tracon`'da ölçüldü (madde 5).
2. **Semaphore denetimi her iki modda pack aşamasına taşındı.** Plan yalnız
   `--paket-dizini`'nin semaphore'u taşımamasını istiyordu; `BreakingChangeGate` artık
   yalnız `baseline` + `report_dir` taşır, `library_ids` ve `pack_started` alanları düştü.
   Sonuç: `yayin --kuru`'da "doğrulama koşmadı" hatası kimlik/metaveri denetiminden
   **önce** gelir. Gerekçe: iki modda aynı fonksiyon (`_pack_release`), sınıflandırma kodla
   kilitli (`test_paket_dizini_modu_yesil_girdi_degismez_manifest_esit` `not_run.assert_not_called`).
3. **Commit eşitliği iki modda da koşar** (plan yalnız yeni kuralı tarif ediyordu): `.nuspec`
   `repository commit` = HEAD. Git yoksa (`yayin --kuru`, commit `unknown`) yalnız varlık
   denetlenir. `paketle` ve `--paket-dizini` git'siz **reddeder** (plan belirtmiyordu).
4. **Plan dışı iki yorum düzeltmesi:** `Directory.Build.targets` Faz 187 bloğu "yalniz yayin
   provasi" diyordu — `paketle` de taban geçirdiği için yanlış olurdu; `PackageBaselineWiringTests`
   "the CI pack job never compares" diyordu — `paketle` artık karşılaştırır. Plan yalnız K-661
   bloğuna dokunmayı söylüyordu; ikisi de iddiası bu fazla yanlışlaşan metindir.
5. **191.0-4 ölçüldü, risk düştü:** `farukatasoy/Tracon` koşum 35507188627 (deneme 1-3) ve
   35524722320 (1-2): `nuget-packages` ve `test-results-<os>` her denemede yeniden yüklendi,
   hepsi `success`. `upload-artifact@v4` adı denemeye bağlar; `overwrite: true` gerekmedi.
6. **Manifest şeması:** `release_dir` manifest'i de `baseline` taşır (iki mod). `paket-dogrula`
   plandan katıdır: manifest dışı `api-compat/*.xml`'i de, şema bozukluğunu da (mutlak/`..`
   yol, 64 hex olmayan SHA) bulgu sayar. `_content_fingerprint` bozuk zip'te `PackageFileError`
   (dosya adıyla) fırlatır; ortak yol onu çıkış 1'e çevirir.
7. **`dokuman-bakim.py` ayrıştırıcısı düz sınıftır, `dataclass` değil:** betik
   `sys.modules`'a kaydedilmeden yüklenir ve Python 3.14'te `@dataclass` modülü yükletmedi
   (hafıza: `defter-bakimi.md`).
8. Manuel case numarası plan anındaki `MT-PKG-129` değil `MT-PKG-153`'ten başladı (185–190
   araya girdi). Case 9 yeni case değil, MT-PKG-100'ün güncellemesidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.


- **K-871** *(kategori: geri-dönüşü-pahalı)* (kullanıcı kararı) — tek derleme zinciri; K-604 ve K-661'i genişletir.
- Yerel: komut adları `paketle` · `paket-dogrula` · `yayin --paket-dizini`; artifact adları
  `nuget-packages` · `nuget-verified`; dizinler `artifacts/ci-paket` · `artifacts/nuget-verified`.
- Yerel: `release_dir`'e kopya geçici ad (`.<ad>.<pid>.partial`) + `os.replace`; girdi hiç taşınmaz.
- Yerel: yükleme testlerden sonra (191.6); 191.0-4 ölçümü `overwrite: true`'yu gereksiz kıldı.
- Yerel: rapor kaydı `{"id", "report", "sha256", "validationRan"}`; farksız paket `report: null`.
- Açık Soru 1 = **A** (`publish` checkout + `paket-dogrula`) · 2 = **A** (`EveryArtifactStepPathIsGitIgnored`)
  · 3 = **A** (`CHANGELOG.md` satırı yok — paket içeriği değişmez) · 4 = **A** (`--surum` beklenen
  sürümdür) · 5 = **A** (girdi dışı paket → ret + `rm -rf` önerisi, silme yok).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, 2026-09-24, `Tracon__Ui__AuthToken` yerel bir değerle ezildi,
`Tracon__Providers__OpenAI__ApiKey=` (boş → `echo` sağlayıcısı):

```bash
curl -s -w "%{http_code}\n" -X POST http://localhost:5080/tracon/api/agents/support/run \
  -H "Authorization: Bearer <yerel>" -H "Content-Type: application/json" -d '{"message":"ORD-1001"}'
```

HTTP `200`; SSE `event: run` → `{"runId":"01a0d415-b1dc-733e-9d66-c7c1259a993c"}`, ardından
`Echo: ORD-1001`. Fazın kendi tüketici koşumu MT-PKG-153'tür: altı dış sample, AOT smoke ve
net8.0 tüketicisi `paketle`'nin ürettiği dosyalarla koştu.

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
| Plan revizyonu sayısı | 0 — sekiz sapma uygulama içinde yazıldı (Plandan Sapmalar) |
| Düzeltme turu sayısı | 3 — (1) mutasyon: semaphore'u ortak yola geri koyan mutasyon yeşil kaldı, test güçlendirildi; (2) `dataclass` betik yüklemesini kırdı; (3) denetim 🟡1 + 🟢1 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 1 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.


`faz-denetcisi`, 2026-09-24, çalışma ağacı ↔ `bc1af904`. **🔴 yok.**

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| Göreli `--cikti` ile `TraconApiCompatReportDir` proje dizinine göre çözülür ve rapor kaybolur (hipotez) | 🔴 aday | gürültü — denetçi `dotnet msbuild -v:diag` ile ölçtü: 17 paket 4 değerlendirmede kök `artifacts/ci-paket/api-compat/<P>.xml` | düşürüldü |
| Yapısal kapı adımların varlığına ve sırasına bakıyor; `if:` etiket koşulu, `continue-on-error`, `|| true` ve başka dizinin itilmesi (M1–M6) kapıdan geçiyordu | 🟡 | gerçek | **düzeltildi:** `yumusatilmis()` + `release-dryrun` iş `if:`'i + itilen/doğrulanan dizin = indirilen dizin; yedi negatif vaka (`test_prova_isi_yalniz_etikette_kosarsa_kirmizi` …) |
| `Directory.Build.targets` Faz 187 bloğunun yorumu değişti (plan yalnız K-661 bloğuna izin veriyordu) | 🟡 | gerçek (sapma) | gerekçelendi — Plandan Sapmalar 4 |
| `paketle --cikti <göreli>` kök dışından çağrılınca Python yolu cwd'ye, `dotnet pack` köke göre çözer | 🟢 | gerçek | **düzeltildi** (üç komutta `.resolve()`); aday açılmadı |

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

**Sıradaki faz: planlanmadı** — `YOL-HARITASI`'nda 191'den sonra kalem yok; yeni faz
`faz-planlama` ile `ADAYLAR.md`'den seçilir. İlgili aday: **F-276** (npm kanalı aynı ilke).

Devralınan sözleşme (K-871):

- Yayın paketinin tek kurucusu `kapi.py`'deki `pack_command`'dır. `ci.yml`'de ham
  `dotnet pack` yazmak `dokuman-bakim.py --denetle` "CI tek derleme zinciri"ni kırmızı yapar.
- Pack anı denetimi (`obj/`, pack zamanı, taban cache'i) `_pack_release`'e girer ve sonucu
  `paketle` manifest'ine yazılır. Artifact anı denetimi `_finish_release_rehearsal`'a girer.
  🚨 Yeni bir pack anı denetimini ortak yola koymak `release-dryrun`'da sessizce kırılır.
- `paketle` manifest'i: `version` · `commit` · `dirty` · `baseline` · `packages[]` ·
  `apiCompat[] {id, report|null, sha256|null, validationRan}`.

Açık iş:

- ⏳ **MT-PKG-159 ➜ CI** — bu fazın commit'leri push edilmedi (bakımcı eylemi). İlk Linux
  koşumunda `TRACON0004` olmamalı, `release-dryrun` `Manifest girdiyle aynı` yazmalı; süreler
  "CI iş süreleri" tablosuna girer. Faz 187'nin MT-PKG-142'si de aynı push'la ölçülür.
- 👤 **MT-PKG-160** — sonraki ilk `v*` etiketinde nuget.org paketi = `nuget-verified` (girdi bazında).

🚨 Tuzaklar:

- Yerelde `yayin --kuru --paket-dizini` eski sürüm taşıyan `artifacts/package/release`'i
  reddeder: `rm -rf artifacts/package/release`. Net8.0 tüketicisi `DOTNET_ROOT=~/.dotnet` ister.
- `paketle` yalnız `Derle`'nin hemen ardında anlamlıdır; fikstürler src'yi yeniden damgalar.

## Kapanış Kapısı

`DOTNET_ROOT=~/.dotnet MSBUILDDISABLENODEREUSE=1 python3 scripts/kapi.py kapanis --taban bc1af904`, 2026-09-24:

| Koşum | Ağaç | Sonuç |
|---|---|---|
| 1 | denetim düzeltmelerinden önce (commit'siz) | ✅ EXIT 0 — build 130 sn · test 658 sn · pack 14 sn · format 153 sn · site 40 sn |
| 2 | commit'li ağaç `f52bbfeb` (denetim düzeltmeleri dahil) | ✅ EXIT 0 — build 111 sn · test 752 sn · pack 9 sn · format 127 sn · site 35 sn |
