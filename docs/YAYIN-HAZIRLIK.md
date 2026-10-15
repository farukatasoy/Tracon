# Tracon Yayın Hazırlığı

> Bu dosya, Tracon paket ailesinin NuGet yayın hattı için yaşayan kontrol
> düzlemidir. Faz planı, sohbet özeti veya genel karar defteri değildir. Yalnız
> ölçülen kanıtı, yayın kararlarını, risk kabulünü ve doğrulama durumunu taşır.
>
> **Son güncelleme:** 2026-10-15  
> **Çalışma modu:** `nuget-danismani` — yayın kararı (`1.0.0-preview.4`)  
> **GÜNCEL DURUM (2026-10-15):** ✅ **`1.0.0-preview.4` yayınlanabilir; preview
> 🔴'si yok.** İçerik: Faz 194–211 ve F-536 (Loop'un taslakları cevap yüzeylerine
> gitmiyor). Yerel kanıt `eb60cfe3`'te: `kapi.py yayin --kuru --surum
> 1.0.0-preview.4` yeşil (20 paket, kırıcı liste 5 tip / 3 paket hepsi notta,
> `npm --dry-run`, 6 exact-version örnek, Native AOT, net8.0 smoke; KN-034);
> kapanış kapısı yeşil (Faz 211 kaydı); CI'ın SDK'sıyla (`10.0.402`) tam derleme
> ve `format` yeşil (KN-035). A-73 kapandı: `global.json` `10.0.402` +
> `latestPatch` (`eb60cfe3`). Kalan koşul **iki push** ister (Faz 187 kuralı):
> önce `main` (CI'da hiç koşmamış ~98 commit, iki OS + prova + site), yeşilse
> kesim + etiket **tek atomik push** 👤. Bilinen sınırlar sürüm notuna girer:
> F-544 (native SSE Loop taslağını akıtır) · F-545 (kod grafında üretim başına
> agent sürüm sabiti yok). Defter bütçeleri dolu (`KARARLAR.md` %100,
> `ADAYLAR.md` %100): sonraki fazdan önce `karar-damit` ve aday damıtması.
> Ayrıntı: §4 "KARAR — 2026-10-15 (`preview.4`)".
>
> **Önceki durum (2026-09-24, 21:50 UTC):** ✅ **`1.0.0-preview.3` SEVK EDİLDİ.**
> `v1.0.0-preview.3` → `b0771b3e`, etiket koşumu `36057172063` yeşil (npm ·
> NuGet · GitHub release). npm `latest` = `next` = preview.3; nuget.org 20/20
> görünür. `MT-PKG-160`: 20/20 paket CI'ın
> `nuget-verified` artifact'iyle girdi girdi aynı (KN-032). Site yeniden
> yayımlandı. npm deprecation ✅. Kalan 👤: nuget.org'da preview.1/2 deprecation
> (OP-008, arayüz) · A-61 advisory. Ayrıntı: §4 "YAYINLANDI — 2026-09-24".
>
> **preview.3 yayın öncesi kararı (2026-09-24):** ✅ **Yayınlanabilir; ürün
> 🔴'si yok.** UR-008 = **B** 👤: A-59 (K-872, tam aralık) ve BL-058 (lisans
> bildirimi) `de52ecdf`'de kapandı; kapanış kapısı yeşil (KN-029). Kesim
> `e0fd4f61`'de; prova ve dış tüketici repro'su yeşil (KN-030, KN-031). Açık
> tek koşul A-64'tür ve **iki push** ister: önce `6a77957d` `main`'e, CI yeşil;
> sonra kesim + etiket **tek atomik push** (Faz 187 kuralı,
> `hafiza/yayin-ve-surumleme.md`). preview.3 bir **güvenlik sürümüdür**.
> Sınıf taraması A-72'yi açtı (`Tracon.Cli`, GA). Ayrıntı: §4 "KARAR —
> 2026-09-24 (`preview.3`)".
>
> **GA durumu (2026-09-24):** ❌ **1.0 bugün çıkamaz** — `Tracon.AspNetCore`'un
> beş doğrudan ön sürüm bağımlılığının hiçbiri kararlı değil (`NU5104`, K-602,
> ER-001). 1.0 olgunluk denetimi on iki yeni bulgu açtı (A-59…A-70); biri GA
> blocker'ıdır (BL-058, lisans bildirimi). Dört ürün sorusu kullanıcı kararı
> bekler (UR-004…UR-007, §7). İlk iş A-64: 83 commit CI'da hiç koşmadı.
> Ayrıntı: §4 "GÜNCEL KARAR — 2026-09-24".
>
> **Önceki durum:** ✅ `1.0.0-preview.1` ve `1.0.0-preview.2` SEVK EDİLDİ
> (2026-09-20): 20 NuGet paketi + `@tracon/client` npm'de, GitHub release'ler
> açık. Canlı ölçüm (2026-09-22): npm `latest` = `next` = `1.0.0-preview.2`
> (**A-42 canlıda kapandı**) ve `v1.0.0-preview.2` release sayfası var —
> preview.2 koşumunun "kalan iş" listesi boşaldı. Bu dosyanın taşıdığı karar
> **duyuru** kararıdır; duyurudan önce kalan tek doğrulama repo dışından
> `dotnet new tracon-api` restore/build smoke'udur (A-34'ün canlı kanıtı).
> Ayrıntı: §4 "Damıtılan yayın kayıtları". Açık `preview.2`/GA kalemleri: §13.
>
> **Damıtıldı (2026-09-22).** 2026-08-28 → 2026-09-20 arası sekiz tur
> anlatısı, §13'ün kapanmış `preview.1` aksiyon tablosu ve §15–16'nın kapanmış
> çalışma kayıtları bu dosyadan düşürüldü; kalıcı sonuçları ilgili bölümlerin
> başında birer özettir. Tam metin:
> `git show 8634a1e3:docs/YAYIN-HAZIRLIK.md`

## 1. Yayın hedefi ve kapsamı

| Alan | Değer |
|---|---|
| Amaç | Repo hakkında bilgisi olmayan üçüncü taraf geliştiricinin Tracon'i NuGet üzerinden anlayabilmesi, güvenli kullanabilmesi ve desteklenen seam'lerden genişletebilmesi |
| İlk hedef tüketici | Tam entegrasyon setini kullanan üçüncü taraf geliştirici (UR-002) |
| Yayın kanalı | NuGet.org; aynı `v*` tag'i ile `@tracon/client` npm yayını da mevcut CI kapsamındadır |
| En küçük güvenli kapsam | Tam 20 paket; kullanıcı tarafından sabitlendi (UR-002/BL-002) |
| Kapsam dışı | Gerçek `push`, Git tag, GitHub release, NuGet.org sahiplik değişikliği ve credential değişikliği açık kullanıcı onayı olmadan yapılmaz |
| Kanıt standardı | Kaynak ve doküman yalnız varlığı ve vaadi gösterir. “Çalışıyor” kararı `.nupkg`, izole external consumer ve gerekli runtime/AOT koşumundan sonra verilir |

## 2. Hedef sürüm ve gerekçesi

| Alan | Değer |
|---|---|
| Geçici yayın türü | `preview` (UR-001 karar verildi, 2026-08-27) |
| Dry-run sürümü | `1.0.0-preview.1` artifact ölçümü için kullanıldı; kesin sürüm numarası (`1.0.0-preview.1` vb.) kapsam ve seam audit'i bittikten sonra sabitlenir |
| Durum | Yayın türü ve kapsam sabit; preview seam audit'i tamamlandı. Public API freeze GA turuna ertelendi (UR-003) |
| Ölçülen ürün gerçekleri | Shipped giriş sayısı `0`, unshipped tip sayısı `676`. `Tracon.AspNetCore` pre-release MAF Hosting/A2A bağımlılıkları taşır. Bunlar karar değil, yeni değerlendirmeye giren kanıttır. |
| Önceki yayın kararları | K-602, K-603 ve diğer yayınla ilgili kayıtlar bu turda tarihsel bağlamdır; hedef, sürüm, kapsam veya compatibility politikası için normatif kaynak değildir |
| Yeni hedefin ölçütleri | Kullanıcı kararıyla sabitlenir; sonra Adım 1–8 kanıtıyla test edilir |

## 3. Yayınlanacak paket envanteri

Kaynak ölçümü `src/*/*.csproj` altında `IsPackable=false` olmayan **20** proje
buldu. Artifact kimlik kümesi dry-run sonrasında ayrıca doğrulanacaktır.

| Paket | Profil | Hedef TFM | Yayın durumu |
|---|---|---|---|
| `Tracon` | Meta paket | `net8.0;net9.0;net10.0` dependency group | ✅ `1.0.0-preview.1` |
| `Tracon.Abstractions` | Library | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Anthropic` | Provider adapter | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.AspNetCore` | HTTP/transport host | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Azure` | Provider adapter | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Cli` | .NET tool | `net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Client` | Generated management client | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Core` | Runtime | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Google` | Provider adapter | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Mcp` | MCP client/tool integration | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.OpenAI` | Provider adapter | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.PostgreSql` | Storage provider | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.SqlServer` | Storage provider | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Sqlite` | Storage provider | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Templates` | `dotnet new` content package | `net10.0` build host | ✅ `1.0.0-preview.1` |
| `Tracon.Testing` | Test helper library | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Testing.Contracts.Xunit` | Reusable contract suite | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.UI` | Embedded UI | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Voice` | Voice tool library | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |
| `Tracon.Workflows` | Workflow runtime | `net8.0;net9.0;net10.0` | ✅ `1.0.0-preview.1` |

Not: K-602'nin metni 19 paket der. Güncel kaynak 20 paket gösterir. Yeni paket
eklendiğinde kimlik kümesini dinamik çıkaran kapı bunu kapsar. Karar metnindeki
sayının ürün politikası mı yoksa bayat ölçüm mü olduğu artifact sonrasında
değerlendirilecektir.

## 4. Mevcut net yayın kararı

### ✅ KARAR — 2026-10-15 (`nuget-danismani`, `preview.4` yayın kararı)

**Hedef:** `1.0.0-preview.4` (preview), tam 20 paket + `@tracon/client` (UR-002
kapsamı değişmedi). **Karar: ✅ Yayınlanabilir — preview 🔴'si yok.**

**Canlı durum (Adım 0.2, 2026-10-15):** nuget.org ve npm'de son sürüm
`1.0.0-preview.3`; Faz 194–211 hiçbir pakette yok. `origin/main` `c8b9ea01`
(2026-10-14); son üç `main` koşumu kırmızıydı: ikisi `CA1873` (CI'ın 10.0.402
SDK'sı; yerel 10.0.100 görmüyordu), biri içerik kapısı. Site paketin önünde:
2026-10-13 yayını preview.3'te olmayan API'leri anlatır (sürüm yayınlanınca
kapanır). MAF GA ailesi nuget.org'da `1.25.0`, Hosting ailesi hâlâ ön sürüm
(`1.25.0-preview.261014.1`); ER-001 değişmedi.

**Kanıt:** KN-034 (yayın provası), KN-035 (CI SDK'sıyla derleme), Faz 211 ve
F-536 kapanış kapıları, zafiyet taraması 47 projede 0 (2026-10-14).

**Bu turda kapanan/açılan:**
- A-73 kapandı (`eb60cfe3`): sınıf üç kez tekrarladı (`CA1875`, iki kez
  `CA1873`); kapı SDK sabitidir. Bakımcı makinesine SDK 10.0.402'nin global
  kurulumu 👤 gerekir.
- `CA1873` iki kez düzeltildi (`2ee06055`, `a98083a1`); `main`'in kırmızısının
  sebebi kapandı, CI'da doğrulanmalı.
- F-536 kapandı (K-1006): Loop'lu agent workflow adımında, OpenAI uyumlu ve
  A2A akışlarında ve iki ses yolunda yalnız son iterasyonu verir. **Davranış
  değişikliği** CHANGELOG'da.
- F-544 ve F-545 açıldı (preview.4 dışı, bakımcı kararı); sürüm notunda
  bilinen sınır olarak anılır.

**Seviyelendirme (Adım 8):** 🔴 yok. 🟡: A-62 (bot PR'ları kırmızı), A-66 (npm
token'ı; en geç 2026-11-26), A-72 (`Tracon.Cli` bildirimi, GA), F-544/F-545,
MAF 1.25'e karşı ileri uyum ölçülmedi (`uyum-probu ileri`; tüketici exact pin
ile 1.20'de kalır). 🟢: A-71 (sürüm metni elle; kesimde güncellenir), A-61.

**En küçük güvenli kapsam:** tam set; hiçbir paket çıkarılmaz. Sürüm türü
preview, çünkü public API dondurulmadı (UR-003) ve Hosting ön sürümdür.

**Sıra (👤 adımlar işaretli):**
1. 👤 `main`'i it; CI iki OS'te build/test, prova ve site kapısında yeşil.
2. Kesim commit'i (`e0fd4f61` deseni): `## [1.0.0-preview.4] - <tarih>`,
   yükseltme özeti (5 kırıcı tip, migration'lar), sevk edilen sürüm metni
   preview.3 → preview.4, site changelog ve agent map yeniden üretimi.
3. 👤 Kesim + `v1.0.0-preview.4` etiketi tek atomik push; etiket koşumu
   yeşil; nuget.org 20/20 ve npm dist-tag'ler doğrulanır (MT-PKG-160 deseni).
4. 👤 Site yayını (`scripts/site-deploy.sh`) etiketten **sonra**.
5. Tüketici duyurusu: iki uyum raporu (`docs/arsiv/incelemeler/`) başlangıç
   kitleriyle güncel.

### ✅ YAYINLANDI — 2026-09-24, `1.0.0-preview.3`

**Etiket `v1.0.0-preview.3` → `b0771b3e`**, tek atomik push (`main` + etiket).
Koşum `36057172063`: build ×2 · Yayin provasi · npm · NuGet · GitHub release
yeşil; Dokuman sitesi etikette atlanır. Faz 191 zincirinin ilk gerçek etiket
koşumu: yayınlanan baytlar CI'ın doğruladığı baytlardır.

| Ne | Kanıt |
|---|---|
| npm | `+ @tracon/client@1.0.0-preview.3` 21:27 UTC; registry'de 21:38 UTC (~11 dk önbellek gecikmesi). `latest` = `next` = preview.3 (stable yok → `latest` en yeni preview'e alınır) |
| nuget.org | 21:36 UTC'den itibaren paket paket görünür; 22:03 UTC'de **20/20** (`Tracon.Cli` en son) |
| `MT-PKG-160` | **20/20** paket: nuget.org dosyası ile `nuget-verified` artifact'i **girdi girdi aynı** (`.signature.p7s` hariç; `[Content_Types].xml` dahil). Ham SHA-256 farklı — depo imzası (KN-032) |
| Yeniden koşum | "Re-run failed jobs" önceki denemenin artifact'ini kullandı: ubuntu işi yeniden koşmadı, 2. denemenin `release-dryrun`'u 1. denemenin `nuget-packages`'ını okudu (KN-033) |
| Site | `site-deploy.sh` etiketten sonra yeniden koştu; Voice bölümü `guides/voice/#troubleshooting`'te, `troubleshooting` 58 149 B |

**Deprecation (OP-008, güvenlik düzeltmesi):** npm ✅ — `@tracon/client`
preview.1 ve preview.2 `deprecated` taşır, preview.3 taşımaz (registry'den
ölçüldü 2026-09-24 22:02 UTC; ikinci `npm deprecate` denemesinin `E422`'si
değişiklik olmadığı içindir). nuget.org 👤 — 20 paket için arayüzden
(deprecation için herkese açık API yok). A-61: GitHub Security Advisory.

`Tracon.Cli` (28 MB) push'tan ~36 dk sonra, 22:03 UTC'de görünür oldu: önce
flat container ve paket sayfası; registration ve arama indeksi birkaç dakika
geriden gelir. `MT-PKG-160` onun için de aynı (124 girdi, fark yok).

### ✅ KARAR — 2026-09-24 (`nuget-danismani`, `preview.3` yayın kararı)

**✅ `1.0.0-preview.3` yayınlanabilir — ürün 🔴'si yok.** Karar, CI'ın
görmediği commit'lere dayandığı için **koşulludur**: etiket, `main` CI'da
yeşil koşmadan atılmaz (A-64). GA kararı (aşağıdaki blok) değişmedi.

| Ne | Nasıl ölçüldü | Sonuç |
|---|---|---|
| Yayın provası | `DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.3`, **boşaltılmış** `artifacts/package/release`, temiz ağaç | Çıkış `0` (KN-027) |
| CI'ın gördüğü | `git fetch` · `git rev-list --count origin/main..HEAD` | **84** commit; `origin/main` = `804d94b2` (2026-09-20). `ci.yml` +231 satır, `kapi.py` +1207 satır CI'da hiç koşmadı |
| Tüketicinin aldığı | nuget.org flat container · `npm view @tracon/client dist-tags` | `1.0.0-preview.2` · `latest` = `next` = `1.0.0-preview.2` |
| preview.3'ün içeriği | `CHANGELOG.md` `[Unreleased]` · `origin/main`'deki aynı bölüm | 62 not: **13 Security** · 22 Changed · 14 Fixed · 8 Removed · 3 Deprecated · 2 Added. `origin/main`'de bölüm **boş** — notların hepsi yerelde |
| İleri uyum | `uyum-probu.cs -- ileri --tuketici artifacts/package/release/Tracon*.1.0.0-preview.3.nupkg` | 1.170 üye referansı · 1 eksik tip · 1 eksik üye — preview.2 ile **aynı**, ikisi de `Tracon.AspNetCore` → Hosting `1.22.0-preview.260918.1` (A-59, KN-028) |
| Zafiyet bildirimi | Anonim `GET /repos/farukatasoy/Tracon/private-vulnerability-reporting` · `/security-advisories` | `enabled: false` · advisory `[]` (A-61) |

**Etiketten önce (sıra önemli):**

1. **Kapsam kararı (UR-008).** A-59 ve BL-058 preview.3'e girecekse `kusur-giderme` ile yerelde kapanır.
2. **Sürüm kesimi commit'i (A-71).** `## [Unreleased]` → `## [1.0.0-preview.3] - <tarih>`; sevk edilen metindeki 26 `1.0.0-preview.2` (ikisi pakete giren README'de) güncellenir.
3. **`main`'i it ve CI'ı yeşil gör (A-64).** Bu adım 13 güvenlik düzeltmesini ve açıklamalarını **public** yapar; düzeltilmiş paket yoktur. Bu yüzden 1–2 önce biter ve etiket CI yeşil olunca **aynı gün** atılır. Son `main` koşumu ~35–38 dk sürdü.
4. `NPM_TOKEN`'ın bitiş tarihini npmjs.com'da oku (A-66; bu oturumda ölçülemedi). Süresi dolduysa `npm-publish` kırmızı olur ve `publish` koşmaz — güvenli yön, ama etiket yeniden itilir.

#### UR-008 = B uygulandı — 2026-09-24 (`kusur-giderme`)

| Kalem | Sonuç | Kanıt |
|---|---|---|
| A-59 | Beş ön sürüm pini `[x]`; nuspec aynısını yazar | `EveryPrereleaseThirdPartyDependencyIsExact` önce 15 kenarla kırmızı, sonra yeşil · `kapi.py` `EXACT_RANGE_PATTERN` · negatif kontrol: yayınlanmış preview.2 + Hosting `1.22.0-preview` izole cache'te **uyarısız** restore oldu |
| BL-058 | `THIRD-PARTY-NOTICES.txt` paket kökünde ve `Tracon.UI.dll`'de; 7 paket (React, React DOM, `scheduler`, TanStack ×2, `openapi-fetch`, Tailwind) | `UiPackageCarriesThirdPartyNotices` önce kırmızı, sonra yeşil · derleme kapısının negatif kontrolü: dosyaya satır eklenince `npm run build` düştü |
| Sınıf taraması | `Tracon.Cli` 54 üçüncü taraf ikili, bildirimsiz → A-72 (vaka kapandı, **sınıf açık**) · `Tracon.Templates` üçüncü taraf kod taşımıyor | `unzip -l` preview.3 adayı |
| Prova ortamı | Prova artık `artifacts/package/yayin/<sürüm>/`'e yazar; `release/`'deki eski sürüm onu düşürmez · net8 runtime'ı global kuruluma eklendi (👤 `sudo`, 8.0.31; ilk denemede muxer ezildi ve onarıldı — `hafiza/elle-kosum-ortami.md`) | `kapi_test` 8 yeni test, eski kodda 7'si kırmızı · `DOTNET_ROOT` olmadan `missing_test_runtimes` boş, `Tracon.Core.UnitTests` net8.0 bacağı 7/7 |

**Push sırası (Faz 187: kesim etiketsiz itilirse prova kırmızıdır):**

1. `git push origin 6a77957d:main` — kesim öncesi son commit (iki CI koşumunun düzeltmeleri dahil, KG-043, KG-045) → CI yeşil (A-64 kapanır).
2. `git tag v1.0.0-preview.3` (HEAD: kesim `e0fd4f61` + sonraki kanıt commit'leri) →
   `git push --atomic origin main v1.0.0-preview.3`.

Kesim commit'inde prova (KN-030) ve dış tüketici repro'su (KN-031) yeşil.

**Etiketten sonra:** `MT-PKG-160` (nuget.org baytı = `nuget-verified`) · OP-008
gereği `preview.1` ve `preview.2` nuget.org'da deprecate edilir ve preview.3'e
yönlendirilir · A-61 hattı (GHSA) açıksa advisory yayımlanır.

**Yerel prova notu.** İlk iki koşum ortam yüzünden düştü, ürün yüzünden değil:
global kurulumda net8 runtime'ı yok (`DOTNET_ROOT=~/.dotnet` gerekir) ve
`release_dir` bugünkü kapanış koşumlarının `preview.2.78/79/82` paketlerini
taşıyordu. İkisi de `docs/hafiza/test-kosum-tuzaklari.md` ve
`docs/hafiza/yayin-ve-surumleme.md`'de yazılıdır; eski paketler silinmedi,
oturum scratchpad'ine taşındı.

### 🚨 GÜNCEL KARAR — 2026-09-24 (`nuget-danismani`, 1.0 olgunluk denetimi)

**❌ 1.0 bugün çıkamaz.** Tek sert engel upstream'dedir: `Tracon.AspNetCore`'un
beş doğrudan ön sürüm bağımlılığının hiçbiri kararlı değil ve kararlı pack
`NU5104` ile durur (K-008, K-602, ER-001). Bu tur preview kararını yeniden
açmaz — 2026-09-20 blokları geçerlidir. Yargılanan şey **1.0 sözüdür**: 1.x
boyunca ne donar, upstream değişirken söz tutulur mu, tedarik zinciri
tüketiciye güven verir mi. On iki yeni bulgu A-59…A-70'tir; biri GA
blocker'ıdır (BL-058). Dört ürün sorusu kullanıcı kararı bekler
(UR-004…UR-007, §7).

**Öneri (UR-004 C):** API dondurma işi (UR-003, UR-005) bitince
`1.0.0-rc.N` çık. RC bir ön sürümdür, `NU5104` onu durdurmaz; GA, upstream
kararlı olunca aynı yüzeyle gelir.

#### Canlı durum (ölçüldü 2026-09-24)

| Ne | Nasıl ölçüldü | Sonuç |
|---|---|---|
| CI'ın gördüğü | `git fetch` + `git rev-list --count origin/main..HEAD` | **83** commit CI'da hiç koşmadı; son `main` push'u 2026-09-20 (`804d94b2`) |
| Tüketicinin aldığı | nuget.org flat container · `npm view @tracon/client dist-tags` | `1.0.0-preview.2` · `latest` = `next` = `1.0.0-preview.2` |
| Ön ek ve sahiplik | nuget.org arama API'si, `q=owner:Tracon` | 20/20 `verified=True`, sahip `Tracon` |
| Repo görünürlüğü | Anonim `GET /repos/farukatasoy/Tracon` | `visibility: public` |
| Upstream | nuget.org flat container: son / son kararlı | MAF `1.22.0` ✅ · Hosting, Hosting.A2A, Hosting.AspNetCore `1.22.0-preview.260918.1` / kararlı **yok** · Hosting.OpenAI `1.22.0-alpha.260918.1` / **yok** · `A2A.AspNetCore` `1.0.0-preview2` / **yok** · MEAI `10.10.0` · MCP `2.2.0` · `xunit.v3` `4.0.1` |
| Bağımlılık botu | GitHub API, PR #20 #21 #23 #26 #27 | Beşi de `restore` adımında kırmızıydı; 2026-09-24'te birleştirilmeden kapandı. Pin'ler değişmedi: MAF `1.20.0`, MEAI `10.9.0`, `Microsoft.Extensions.*` `10.0.11` |

#### Ölçülen kanıtlar

Ölçüm aracı yeni: `.agents/skills/nuget-danismani/scripts/uyum-probu.cs`
(.NET 10 dosya tabanlı uygulama, repo analyzer'larıyla uyarısız derlenir).
Nuget.org'daki paketlerin `net10.0` ikililerini metadata seviyesinde okur.

| Ne | Nasıl ölçüldü | Sonuç |
|---|---|---|
| İleri uyum — tüm upstream en yeni | `uyum-probu.cs -- ileri --tracon 1.0.0-preview.2` | 1.170 üye referansı · **1 eksik tip · 1 eksik üye**, ikisi de `Tracon.AspNetCore` → ön sürüm Hosting (A-59) |
| İleri uyum — yalnız GA upstream | Aynı araç, `--ust` MAF `1.22.0` + MEAI `10.10.0` | 1.024 üye referansı · **0 eksik** ✅ |
| Aracın negatif kontrolü | Aynı araç, `--ust` MAF `1.13.0` | 1 eksik tip + 2 eksik üye raporladı — araç kırılmayı görüyor ✅ |
| Deneysel maruziyet | `uyum-probu.cs -- deneysel` (pinlenen sürümler) | 196 deneysel upstream tipi; public imzada **1** (A-63). `src` bastırmaları: `MAAI001` 25 · `MEAI001` 14 · `MCPEXP002` 2 · `OPENAI001` 1 |
| Gömülü UI lisans bildirimi | `Tracon.UI.*.nupkg` listesi · `wwwroot/assets/index-*.js.br` brotli ile açıldı · `package-lock.json` | 748.550 B JS'te `@license`/`Copyright`/`MIT License` **0**; pakette notice dosyası yok; runtime bağımlılıkları MIT (A-60) |
| Kiracı metrik etiketi | `TraconMetrics.RecordRun` · `TraconObservabilityOptions` | `tracon.tenant.id` koşulsuz; kapatma seçeneği yalnız `tracon.agent.version` için var (A-68) |
| Kırılma kapısının kapsamı | `scripts/breaking_changes.py` · `OpenApiSnapshotTests` · `reference/versioning.md` | K-864 yalnız library paketlerini karşılaştırır; OpenAPI testi kod ↔ anlık görüntü kaymasını ölçer (A-67) |
| npm yayın kimliği | `ci.yml` `npm-publish` işi | `NODE_AUTH_TOKEN: secrets.NPM_TOKEN`; `publish` `npm-publish`'e bağlı (RK-011) (A-66) |
| Contract paketi × xunit 4 | `uyum-probu.cs -- ileri --tuketici Tracon.Testing.Contracts.Xunit@1.0.0-preview.2 --ust xunit.v3.*@4.0.1` | 6 üye referansı, 0 eksik; çalışma anı ölçülmedi (A-70) |

#### Bulgular

| # | Bulgu | Seviye | Kök sebep | Kanal | GA'dan önce |
|---|---|---|---|---|---|
| **A-59** | ✅ **KAPANDI (K-872, `de52ecdf`)** — **Yayınlanmış `Tracon.AspNetCore`, upstream'in yeni ön sürümüyle çalışma anında kırılır.** `1.0.0-preview.2` + MAF Hosting `1.22.0-preview.260918.1`: `Microsoft.Agents.AI.Hosting.AgentSessionStore` yok (Abstractions'a `Microsoft.Agents.AI.AgentSessionStore` olarak taşındı, forwarder yok) ve `AgentRunMode.DisallowBackground` yok. Tüketici Hosting'i yükseltirse restore uyarısız geçer; hata `TypeLoadException`/`MissingMethodException` olarak gelir. Geçici çözüm: Hosting ailesini `1.20.0-preview.260831.1`'de tut. **preview.3 artefaktı aynı kusuru taşır** (KN-028: nuspec beş ön sürüm bağımlılığı için açık alt sınır yazar; eksikler birebir aynı) | 🟡 | Nuspec ön sürüm upstream'e açık alt sınır (`>=`) yazar; ön sürüm upstream minor'lar arasında kırar | UR-006 → `kusur-giderme` | Hayır — `preview.3`'ten önce |
| **A-60** | ✅ **KAPANDI (BL-058, `de52ecdf`)** — **`Tracon.UI` üçüncü taraf lisans bildirimi taşımıyor.** Derlemeye gömülü 748 KB bundle React, React DOM, `scheduler` ve TanStack Query (MIT) içerir; bundle'da lisans başlığı 0, pakette notice dosyası yok. MIT bildirimin kopyayla gitmesini ister; kurumsal lisans tarayıcısı bunu işaretler | 🔴 | Vite çıktısı lisans yorumlarını atar; notice üreten adım yok | `kusur-giderme` (BL-058) | **Evet — blocker** |
| **A-61** | **Zafiyet duyurusu tüketicinin restore'una ulaşmaz.** `SECURITY.md` yalnız e-posta kanalı anlatır; yayınlanmış GitHub Security Advisory hattı yok. `dotnet restore` (NuGetAudit) GitHub Advisory Database'den beslenir; `SECURITY.md`'nin vaat ettiği nuget.org deprecation'ı restore'da uyarı üretmez. Repo public olduğu için private vulnerability reporting ve GHSA artık açılabilir. Ölçüldü 2026-09-24: `private-vulnerability-reporting` `enabled: false`, advisory yok. **preview.3 ilk güvenlik sürümüdür** (13 `Security` notu): bu hat yoksa preview.2 tüketicisinin restore'u düzeltmeyi haber vermez | 🟡 | Politika repo private iken yazıldı (A-65 sınıfı) | 👤 repo ayarı + `tuketici-dokuman-senkronu` | Evet |
| **A-62** | **Upstream drift kapısı fiilen kapalı.** Bot MAF, MEAI ve `Microsoft.*` platform paketlerini ayrı gruplarda açıyor; PR #20 #21 #23 #26 #27 `restore` adımında kırmızıydı ve 2026-09-24'te birleştirilmeden kapandı. Mekanizma nuspec'ten ölçüldü (iş logu okunmadı): MEAI `10.10.0` `Microsoft.Extensions.*` `10.0.12` ister; pin `10.0.11` başka grupta. Grup yapısı değişmezse bot her hafta aynı kırmızı PR'ları açar | 🟡 | `dependabot.yml` birlikte yükselmesi gereken kümeyi böler | `kusur-giderme` | Evet |
| **A-63** | **Public API upstream'in deneysel tipini taşıyor.** `TraconBuilderExtensions.AddLoopEvaluator(…, Microsoft.Agents.AI.LoopEvaluator)` — `LoopEvaluator` `[Experimental("MAAI001")]`. Tracon üyesi işaretsiz; 1.x'te donarsa upstream minor'ı Tracon'un kararlı yüzeyini kırabilir | 🟡 | Tracon'un kendi `[Experimental]` katmanı yok | UR-005 → `faz-planlama` (UR-003 turu) | Evet |
| **A-64** | ✅ **KAPANDI (KG-046)** — ilk koşum (`482dd8dd`) iki bacak kırmızı, üç kusur → `276ac42e`; ikinci (`1e77de73`) `release-dryrun` + `site` kırmızı, iki kusur → `88f8e4ce`; üçüncü (`6a77957d`) yarış testi → yeniden koşum yeşil; etiket koşumu yeşil — **83 commit CI'da hiç koşmadı.** Son `main` push'u 2026-09-20. Faz 187–191 (kırılma kapısı K-864, tek derleme zinciri K-871) yalnız yerelde yeşil. Bu fazlar CI iş akışının kendisini değiştirdi; ilk gerçek koşumları etikette olursa A-29 sınıfı tekrarlar | 🟡 | Push kullanıcı kararıdır; ölçüm ağaçta kaldı | 👤 `git push` + CI gözlemi | Her etiketten önce |
| **A-65** | **Repo public oldu; iki kayıt ve sekiz metin hâlâ "private" öncülüyle yazılı.** K-659'un yeniden açılma koşulu 2026-09-19'da sağlandı; karar yeniden değerlendirilmedi. OP-011 satırı ve RK-013 durumu eski: environment koruması A-12 ile kuruldu (KG-039), Source Link 2026-09-20'de çözüldü (§4 KARAR — 2026-09-20). Koşulsuz "private" diyen metin: `src/Directory.Build.props:46,71,80,134` · `docs-site/scripts/build-changelog.mjs:6` · `.github/workflows/ci.yml:721,735` · `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md:2940` | 🟡 | Tetiklenen yeniden açılma ölçütünü izleyen bir mekanizma yoktu | `tuketici-dokuman-senkronu` + 👤 K-659 | Evet |
| **A-66** | **npm yayını süreli bir token'a bağlı.** `ci.yml` `NODE_AUTH_TOKEN: secrets.NPM_TOKEN` kullanır. npm'de yazma yetkili granular token'ın ömrü en fazla 90 gündür ([GitHub Changelog, 2025-11-05](https://github.blog/changelog/2025-11-05-npm-security-update-classic-token-creation-disabled-and-granular-token-changes/)); KN-021 tarihinden (2026-08-28) hesaplanırsa en geç 2026-11-26'da biter (token'ın gerçek süresi okunmadı). Süre dolunca `npm-publish` kırmızı olur ve `publish` ona bağlı olduğu için (RK-011) NuGet de basmaz. npm trusted publishing (OIDC) token'ı kaldırır ve provenance ekler | 🟡 | KN-021 hazırlığı token modeliyle yapıldı | 👤 npm trusted publisher + `kusur-giderme` (`ci.yml`) | Hayır — token süresinden önce |
| **A-67** | **1.x sözü yalnız .NET API için yazılı ve kapılı.** `reference/versioning.md` HTTP API, yapılandırma anahtarı, telemetri adı, hata kodu ve CLI için 1.x kuralı vermiyor. HTTP'de `OpenApiSnapshotTests` kod ↔ anlık görüntü kaymasını yakalar, sürümden sürüme kırılmayı değil; K-864 kapısı yalnız library paketlerini karşılaştırır | 🟡 | Kırılma kapısı .NET yüzeyi için kuruldu | `faz-planlama` | Evet |
| **A-68** | **Metrik kardinalitesi GA'da donacak.** `tracon.tenant.id` her `run` metriğine koşulsuz eklenir (`TraconMetrics.RecordRun`); kapatma seçeneği yalnız `tracon.agent.version` için var (`IncludeAgentVersionTag`). `guides/observability.md` etiket adlarını "stable" ilan eder. Çok kiracılı kurulumda seri sayısı kiracı × agent × durum ile büyür | 🟡 | Varsayılan GA'dan sonra değişirse tüketicinin dashboard'u kırılır | `faz-planlama` (varsayılan kararı) | Evet |
| **A-69** | **Lisans sayfası contract paketinin bağımlılığını dar anlatıyor.** `reference/licensing.md:47` "`Tracon.Abstractions` and nothing else" der; sevk edilen nuspec ayrıca `Microsoft.Agents.AI`, `Microsoft.Extensions.AI`, `Shouldly`, `xunit.v3.assert`, `xunit.v3.extensibility.core` taşır. Cümlenin amacı (PolyForm bağımlılığı yok) doğrudur; cümlenin kendisi değil | 🟢 | Elle yazılan bağımlılık iddiası nuspec'e bağlı değil | `tuketici-dokuman-senkronu` | Hayır |
| **A-70** | **Contract paketi `xunit.v3` 3.x'e bağlı; 4.x kararlı.** Alt sınır `3.2.2`, nuget.org'da `4.0.1`. İkili metadata uyumu temiz (6 üye referansı); test platformu v2 ile çalışma anı ölçülmedi. `dependabot.yml` `xunit.v3 >= 4.0.0`'ı yok sayar | 🟢 | Repo'nun kendi test platformu kısıtı sevk edilen pakete taşınıyor | Ölçüm → `faz-planlama` | Hayır |
| **A-71** | 🟡 **Kesim yapıldı (`e0fd4f61`); kapı hâlâ yok** — **Sevk edilen sürüm metni etikete bağlı değil.** `1.0.0-preview.2` 26 yerde elle yazılı: 24'ü `docs-site/src` (`Footer.astro:16` her sayfada, `versioning.md:7` dahil), ikisi **pakete giren** README (`src/Tracon/README.md:33`, `src/Tracon.Templates/README.md:8` — `dotnet new install Tracon.Templates@1.0.0-preview.2`). Kesim commit'i unutulursa preview.3'ün nuget.org sayfası preview.2 kurulumunu önerir ve bu baytlar geri alınmaz. `grep` ile bu metni etiket sürümüne bağlayan bir kapı bulunamadı. A-35 (preview.1) ve `32fb31ee` (preview.2 kesimi) aynı sınıfın önceki vakalarıdır | 🟢 | Sürüm her turda elle yazılıyor; kapı yalnız CHANGELOG başlığını arıyor | `tuketici-dokuman-senkronu` (kesim) → `kusur-giderme` (kapı) | Her etiketten önce (kesim) |
| **A-72** | **`Tracon.Cli` 54 üçüncü taraf ikiliyi bildirimsiz taşıyor — BL-058'in sınıf taramasının ikinci vakası.** `dotnet tool` paketi yayın çıktısının tamamını taşır; preview.3 adayında Tracon dışı 54 ikili var (Npgsql — PostgreSQL License, SQLitePCLRaw — Apache-2.0, Google.Protobuf — BSD-3-Clause, `Microsoft.Data.SqlClient`, `Microsoft.IdentityModel.*` — MIT, `e_sqlite3` …). Pakette `LICENSE.md` dışında bildirim dosyası yok. Apache-2.0 ayrıca `NOTICE` ister. Kapı hazır: `THIRD_PARTY_NOTICE_PACKAGES`'e `Tracon.Cli` eklenir; üretim NuGet bağımlılıklarının lisans dosyalarından yapılır (npm yolu değil) | 🟡 preview · 🔴 GA (BL-058 ile aynı gerekçe) | Tool paketi bağımlılık ikililerini **içinde** taşır; kütüphane paketleri taşımaz | `kusur-giderme` | Evet — GA'dan önce |
| **A-73** | ✅ **KAPANDI (`eb60cfe3`, 2026-10-15)** — `global.json` `10.0.402` + `latestPatch`. **Yerel kapanış kapısı CI'ın SDK'sını koşmuyordu.** `global.json` `10.0.100` + `rollForward: latestFeature`; `setup-dotnet` en yeni bandı kurar (ölçüldü: `10.0.401`, 2026-09-08 sürümü), bakımcı makinesinde yalnız `10.0.100` vardı. `AnalysisLevel` `latest-recommended` olduğu için her yeni SDK'nın yeni kuralı CI'da hata olarak doğar: Faz 190'ın test kodu yerelde yeşil, CI'da `CA1875` ile kırmızıydı (A-64'ün ilk koşumu). Aynı koşum Windows'ta iki yol testini de düşürdü (Faz 180, 187; Windows bacağı o testleri ilk kez koştu) | 🟡 | Yerel ve CI SDK'sı iki ayrı kaynaktan seçiliyor | `faz-planlama` — öneri: `global.json`'u CI'ın sürümüne sabitle (`10.0.401`, `rollForward: latestPatch`), SDK yükseltmesi açık bir commit olsun | Hayır — preview.3 için elle aşıldı (`~/.dotnet` 10.0.401 ile build + format) |

#### Bu turun sınıfı: **dışarıdaki olgu değişti, içeride kimse ölçmedi**

İki kök var. Birincisi **upstream'in sözü ölçülmüyordu** (A-59 · A-62 · A-63
· A-70): kapılar Tracon'u yalnız pinlenen sürümlere karşı sınar; tüketicinin
alabileceği daha yeni upstream'e karşı hiçbir ölçüm yoktu. İkincisi
**tetiklenen öncüller izlenmiyordu** (A-61 · A-65 · A-66): repo public oldu ve
npm token politikası değişti, ama bu olaylara bağlı kayıtlara dönen bir adım
yoktu. Karşılık `nuget-danismani`'dedir: Adım 0.2 (canlı durum, tetiklenen
yeniden açılma, dış olgu tazeliği) ve `uyum-probu.cs`.

#### Sonraki sıra

1. **A-64** — `main`'i it, CI'ın Faz 187–191 zincirini yeşil koştuğunu gör.
2. **BL-058 / A-60** — lisans bildirimi (ucuz, GA blocker).
3. **UR-004…UR-007** — dört ürün kararı; A-59, A-63 ve A-68 bunlara bağlı.
4. **A-62** — bot gruplarını birleştir; ardından MAF `1.22`/MEAI `10.10` yükseltmesi.
5. Kalanlar GA turunda (§13).

### Damıtılan yayın kayıtları (2026-09-20 → 2026-09-22)

> `preview.1` YAYINLANDI bloğu, duyuru öncesi tüketici denetimi (A-34…A-58),
> `preview.2` yayın koşumu ve 2026-09-22 canlı ölçümü buradan düşürüldü
> (2026-09-24, bütçe). Tam metin:
> `git show c45112be:docs/YAYIN-HAZIRLIK.md` — §4, satır 167–390.

- **`preview.1`** — `v1.0.0-preview.1` → `f14f6309`, koşum `35510131648`, sekiz
  iş yeşil. "Push başarılı" ile "paket görünür" ayrı anlardır: yeni kimlikler
  bir süre `404` döner; yayın kanıtı CI'ın `publish` işidir.
- **Duyuru öncesi denetim** — 25 kalemin 25'i sonuçlandı (A-34…A-58). Sınıf:
  "premis bayatladı, metin değil". A-34 (şablon `Version="1.0.0"`) ve A-42 (npm
  `latest`) preview.2 ile registry'de kapandı.
- **`preview.2`** — `v1.0.0-preview.2` → `804d94b2`. `Dagitim etiketini hizala`
  `403` verdi: npm `latest`'i sildirmez. Kural: stable varsa preview `latest`e
  dokunmaz, yoksa `latest` en yeni preview'e alınır (`dokuman-bakim.py`
  `geri_alinamaz_registry_islemi` silme adımını yasaklar). 2026-09-22:
  `latest` = `next` = preview.2, release sayfası var.
- **Açık kalan tek doğrulama:** repo dışından `dotnet new tracon-api`
  restore/build smoke (A-34'ün canlı kanıtı).

### Damıtılan tur kayıtları (2026-08-28 → 2026-09-20)

Sekiz tur anlatısı damıtıldı — tam metin: `git show 8634a1e3:docs/YAYIN-HAZIRLIK.md`.
Kalıcı sonuçlar:

- **2026-09-20 (tag sonrası yayın turu):** A-29 (tahsis kapısı `/_` — CI'da 21
  push boyunca `skipped`, ilk koşumu tag'di) ve A-30 (kapasite smoke aynı
  commit'te iki sonuç) kapandı; artifact `75478786`'dan temiz. Kapanış
  kapısının 81 testlik ilk kırmızısı üründe değil bayat artımlı çıktıdaydı —
  A-31 sınıf kaydı açık (§13).
- **2026-09-19 (üç tur):** artifact iki bağımsız ölçümde temiz (`c5ed5573`,
  `225f1472`); kiracı kimliği karşılaştırma semantiği (A-1, Faz 179), sevk
  edilen dokümanın çalışmayan yetenek anlatısı (A-2) ve release-notes çapası
  (🟡 3) kapandı; A-28 (sesli delegasyon timeout ≠ barge-in) açık kaldı (§13).
  Karar: ✅ Yayınlanabilir.
- **2026-09-16:** dört ürün kararı (KG-026…029) ve yayın sırası sabitlendi;
  on adımlık tag öncesi liste sonraki turlarda tamamlandı.
- **2026-09-15 (tüketici geri bildirimi turu):** beş iddia kanıtla çürütüldü —
  yeniden açılmaz (karar günlüğü, KG-023).
- **2026-09-14 (dış inceleyici turu):** bulgular A-1…A-18 hattına bağlandı;
  ölçüm bekleyenler `docs/ADAYLAR.md` A-ID kanalında.
- **2026-09-03 (tag öncesi tur):** hesap ve operasyon kararları kapandı
  (OP-008/009/011, BL-056/K-659, F-180/K-660); taze `kapanis` + `yayin --kuru`
  yeşil.
- **2026-09-02 (Faz 129–135 sonrası):** ilk "✅ yayınlanabilir" ölçümü; sonraki
  turlar bu kanıtı devralmadı, yeniden ölçtü — devralmama kuralı buradan çıktı.

## 5. Ölçülen kanıtlar

| Kimlik | Konu | Durum | Ölçülen kanıt | Sonuç | Tarih |
|---|---|---|---|---|---|
| KN-001 | Packable kaynak envanteri | Tamamlandı | `grep -L '<IsPackable>false' src/*/*.csproj` | 20 proje | 2026-08-27 |
| KN-002 | Ortak TFM politikası | Tamamlandı | `src/Directory.Build.props` ve proje override'ları | Varsayılan `net8.0;net9.0;net10.0`; CLI, Templates ve Testing özel durumları var | 2026-08-27 |
| KN-003 | Paket metadatası tanımı | Tamamlandı | `src/Directory.Build.props` | MIT expression, authors, project/repository URL, icon, embedded README, symbols, Source Link ayarları tanımlı | 2026-08-27 |
| KN-004 | Deterministic build tanımı | Tamamlandı | `Directory.Build.props` | `Deterministic=true`; `ContinuousIntegrationBuild=true` yalnız CI ortamında | 2026-08-27 |
| KN-005 | Public API freeze durumu | Tamamlandı | `PublicAPI.Shipped.txt` ve `PublicAPI.Unshipped.txt` sayımı | Shipped giriş `0`; unshipped tip `676` | 2026-08-27 |
| KN-006 | CI yayın tetikleyicisi | Tamamlandı | `.github/workflows/ci.yml` | Her `v*` tag'i dry-run sonrası NuGet ve npm publish işlerini tetikler | 2026-08-27 |
| KN-007 | CI NuGet credential modeli | Tamamlandı | `.github/workflows/ci.yml` | `environment: nuget` ve `NUGET_API_KEY` secret kullanılıyor; trusted publishing yok | 2026-08-27 |
| KN-008 | Mevcut yayın kapısı kapsamı | Tamamlandı | `scripts/kapi.py` kaynak okuması | Dinamik paket kimliği, exact version, metadata, icon/README, repository commit, `.snupkg`, TFM başına XML varlığı, K-008, npm dry-run, beş external sample ve AOT smoke denetleniyor | 2026-08-27 |
| KN-009 | Worktree başlangıç durumu | Tamamlandı | `git status --short` | Kullanıcıya ait ilgisiz bir untracked keşif dosyası var; korunacak | 2026-08-27 |
| KN-010 | Doküman bütçesi | Tamamlandı | `python3 scripts/dokuman-bakim.py --denetle` | Bu dosya öncesinde `docs/**.md` bütçesinde yaklaşık %9 boşluk var; yeni ledger için ayrı kök dosya uygundur | 2026-08-27 |
| KN-011 | Exact release rehearsal | Tamamlandı | `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` | Çıkış `0`; exact sürümlü 20 paket üretildi | 2026-08-27 |
| KN-012 | Packed extension consumers | Tamamlandı | Dry-run içindeki izole feed ve izole `NUGET_PACKAGES` koşumları | Beş sample; toplam 160 test geçti, 0 failed, 0 skipped | 2026-08-27 |
| KN-013 | Native AOT smoke | Tamamlandı | `osx-arm64` publish ve üretilen binary run | Provider, agent source ve generated tool smoke geçti | 2026-08-27 |
| KN-014 | npm dry-run | Tamamlandı | `npm publish --dry-run` | Paketleme başarılı; dry-run sürümü `0.0.0`, gerçek CI sürümü `v*` tag'inden ayrıca türetiliyor | 2026-08-27 |
| KN-015 | Artifact sayısı ve semboller | Tamamlandı | `find` ve `.snupkg` zip içeriği | 20 `.nupkg`, 19 `.snupkg`; Templates bilinçli olarak symbol paketi üretmiyor. Meta `Tracon.snupkg` var fakat PDB içermiyor | 2026-08-27 |
| KN-016 | Artifact boyutları | Tamamlandı | `ls -lhS` | En büyük paket `Tracon.Cli` yaklaşık 28 MB; sonra Core 2.0 MB, Client 1.5 MB, AspNetCore 1.1 MB | 2026-08-27 |
| KN-017 | Pre-release dependency sınırı | Tamamlandı | Üretilen `.nuspec` dosyaları | Tracon dışı pre-release bağımlılık yalnız `Tracon.AspNetCore` içinde; K-008 tutuluyor | 2026-08-27 |
| KN-018 | Faz 121/122 sonrası yayın provası | Tamamlandı | `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` | Çıkış `0`. 20 `.nupkg` + 19 `.snupkg`, exact `1.0.0-preview.1`. Beş packed sample ve Native AOT smoke geçti (`provider/source/generated-tool AOT smoke passed`). **KN-011…KN-017 artık bayat değil** — o ölçümler 2026-08-27 tarihliydi, Faz 121 ve 122 public API ekledikten sonra ilk kez yeniden koşuldu; regresyon yok | 2026-08-28 |
| KN-019 | Yayın kapısının sample kapsamı | Tamamlandı | KN-018 koşumunun kendi başarı satırı: `✅ Beş exact-version packed sample ve Native AOT smoke` | Kapı **beş** sample koşuyor, repo'da **altı** dış sample var — kapının kendi çıktısı BL-052'yi doğruluyor. `CustomJobHandler.Tests` exact sürüm ve izole `NUGET_PACKAGES` altında hiç koşmadı | 2026-08-28 |
| KN-020 | GitHub planı ve private repo yayın sınırı | Tamamlandı | `origin` kimlik doğrulanmış `git ls-remote` ile erişilebilirken anonim GitHub isteği `404` dönüyor; repo private. GitHub'ın güncel resmî [plan](https://docs.github.com/en/get-started/learning-about-github/githubs-plans) ve [environment](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments) belgeleri GitHub Free'de environment secret, required reviewer ve deployment branch/tag restriction özelliklerini private repo için vermiyor | GitHub Free gerçek publish işlerini çalıştırmaya engel değil. `NUGET_API_KEY` ve `NPM_TOKEN` repository secret olarak kullanılabilir; fakat `environment: nuget/npm` tek başına approval veya tag koruması sağlamaz. Pro, private repo için environment secret ve deployment tag restriction getirir; required reviewer getirmez | 2026-08-28 |
| KN-021 | npm yayın hesabı hazırlığı | **Kullanıcı doğruladı** | Kullanıcı dört adımı tamamladığını bildirdi: `tracon` organization, npm 2FA, CI publish token ve GitHub repository `NPM_TOKEN` secret. Secret değeri okunmadı. Salt-okunur `/-/org/tracon` isteği yeniden ölçüldü ve hâlâ `404` döndü; organization metadata bağımsız doğrulanamadı | OP-010 kullanıcı tarafında tamamlandı. Token yetkisi ilk npm publish işinde ölçülür; NuGet işi npm başarıdan sonra koştuğu için başarısız token kalıcı asimetrik yayın üretmez | 2026-08-28 |
| KN-022 | NuGet.org trusted publishing | Tamamlandı | Kullanıcı NuGet.org'da kişisel owner `farukatasoy`, GitHub `farukatasoy/Tracon`, workflow `ci.yml`, environment `nuget`, pattern `Tracon*` ve yalnız `Push new packages and package versions` kapsamlı policy oluşturdu. `.github/workflows/ci.yml` publish job'ı `id-token: write` + `NuGet/login@v1` ile bir saatlik key alıyor; repository `NUGET_API_KEY` kullanımı kaldırıldı | OP-002/004/005 ve RK-004 kapandı. Private repo policy'si yedi günlük geçici aktivasyondadır; ilk başarılı publish policy'yi kalıcı yapar | 2026-08-28 |
| KN-023 | Tam `git` geçmişi secret taraması (RK-014) | Tamamlandı | `gitleaks git .` — **1065 commit, 53,29 MB, 127 bulgu**, hepsi elle sınıflandırıldı: 91 `curl-auth-header` (tamamı `manuel-test-token-2026` / `yanlis-token` / `wrong-token` yer tutucusu), 35 `generic-api-key` (test fixture, `SAHTE-*`, `applied-migrations.json` içindeki migration SHA'ları), 1 `github-pat` (`ghp_0123456789abcdef…` sentetik). Bağımsız çapraz doğrulama: `sk-proj-`, `sk-ant-api03-`, `AIza`, `AKIA`, `xoxb-`, gerçek uzunlukta `ghp_`, `npm_`, `glpat-` → **tüm geçmişte 0 eşleşme**. Geçmişte commit edilmiş `.env`/`.pem`/`.pfx`/`secrets.json`: yok. Parola taşıyan connection string'lerin tamamı localhost docker (`tracon`, `agentprism`, `postgres`, `capacity`) veya işaretli sentetik | **Rotate edilmesi gereken dış credential yok.** Tek gerçek üretilmiş credential çalışma ağacındaydı (`docs/arsiv/fazlar/53-*.md:78`, yerel loopback anahtarı) — redakte edildi; geçmişte kalır, yerel DB'ye bağlıdır ve dışa açık yüzeyi yoktur | 2026-09-19 |
| KN-024 | Yayın provası — tag'lenecek commit'ten | Tamamlandı | `kapi.py yayin --kuru --surum 1.0.0-preview.1`, **boşaltılmış** `artifacts/package/release` ve **temiz** ağaç üzerinde. Çıkış `0`. `package-manifest.json` commit `225f1472` — kanıt tag'lenecek ağacın kendisine ait | 20 `.nupkg` + 18 `.snupkg`, tek sürüm hattı; K-008 korunur (Tracon dışı ön sürüm yalnız `Tracon.AspNetCore`); 20/20 README+icon; TFM başına `.dll`+`.xml`; `Tracon.Testing*` sızıntısı **0**; **6/6** dış sample geçti + Native AOT smoke; `npm publish --dry-run` gerçekten koştu (`@tracon/client@0.0.0`, npm 10.9.8 mevcut) | 2026-09-19 |
| KN-025 | Sağlayıcılar arası capability simetrisi | Tamamlandı | Migration adları farklı (PostgreSQL 51 · SQL Server 40 · SQLite 39) ama tablolar aynı; üç entegrasyon test projesi de **aynı 32 contract**'ı türetiyor, küme farkı **boş** | Sağlayıcı seçimi tüketiciye sessiz bir yetenek kaybı yaşatmıyor. Migration sayısı farkı dosya birleştirmesidir | 2026-09-19 |
| KN-026 | Yeniden adlandırmanın sevk edilen yüzeydeki izi | Tamamlandı | `grep -rn "AgentPrism" src/ README.md docs-site/src` | **0 eşleşme.** Faz 162'nin bıraktığı dört kalem yerel ortamdaydı (MEMORY.md), sevk edilen yüzeyde değil | 2026-09-19 |
| KN-027 | `preview.3` yayın provası — HEAD'den | Tamamlandı | `DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.3`, boşaltılmış `artifacts/package/release`, temiz ağaç. `package-manifest.json`: commit `c45112be`, `dirty: false`, taban `1.0.0-preview.2` | Çıkış `0`. 20 `.nupkg` + 18 `.snupkg`. Kırıcı liste 121 tip · 0 TFM düşüşü · 10 paket, hepsi `[Unreleased]`'da. `npm publish --dry-run` ✅. Altı sample 193 test geçti (101+38+11+15+18+10), Native AOT smoke ve net8.0 tüketici smoke (.NET 8.0.31) geçti. Sürüm notları `[Unreleased]`'dan okundu — kesimde yeniden adlandırılır | 2026-09-24 |
| KN-028 | `preview.3` artefaktının ileri uyumu | Tamamlandı | `uyum-probu.cs -- ileri --tuketici artifacts/package/release/Tracon*.1.0.0-preview.3.nupkg` (upstream en yeni: MAF `1.22.0`, Hosting ailesi `1.22.0-preview.260918.1`, MEAI `10.10.0`, MCP `2.2.0`); `unzip -p Tracon.AspNetCore.1.0.0-preview.3.nupkg Tracon.AspNetCore.nuspec` | 1.170 üye referansı · 1 eksik tip (`Microsoft.Agents.AI.Hosting.AgentSessionStore`) · 1 eksik üye (`AgentRunMode.DisallowBackground`). Nuspec beş ön sürüm bağımlılığı için açık alt sınır yazar (`version="1.20.0-preview.260831.1"`). A-59 preview.3'e aynen taşınır | 2026-09-24 |
| KN-029 | UR-008 B sonrası kapanış kapısı | Tamamlandı | `DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py kapanis --taban c45112be` (commit `81134277` + defter) | Çıkış `0`: 38 test projesi, 18.116 test, 0 kırmızı, 1 atlandı; build uyarısız; pack, format, site (`npm run check`, 1042 sayfa, 0 kırık bağlantı) yeşil. İlk koşum `llms-full.txt` bayat diye düştü → yeniden üretildi (`81134277`) | 2026-09-24 |
| KN-030 | `preview.3` provası — kesim commit'inden | Tamamlandı | `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.3`, **`DOTNET_ROOT` yok** (net8 global kurulumda), temiz ağaç `e0fd4f61` (iki CI koşumunun düzeltmelerinin üstüne yeniden yazılan kesim, KG-043, KG-045); `artifacts/package/release/` bilerek dolu (`preview.1`, `preview.2.84`, `preview.2.87`, eski `preview.3`). Önceki kesimlerin (`2b6c0fb2`, `17ca8dfb`) adayları `yayin/`'den kaldırıldı (MT-PKG-116 kuralı) | Çıkış `0`, 3 dk. `📦 Prova çıktısı: artifacts/package/yayin/1.0.0-preview.3`; kırıcı liste 121 tip, notlar `[1.0.0-preview.3]` bölümünden; altı sample, AOT smoke, net8.0 tüketici smoke global .NET 8.0.31'de yeşil. `release/`'deki eski paketler provayı düşürmedi (MT-PKG-163) | 2026-09-24 |
| KN-031 | A-59 düzeltmesinin dış tüketici kanıtı (6. seviye) | Tamamlandı | Repo dışı `net10.0` konsol, izole `NUGET_PACKAGES`, `Tracon*` → yerel feed eşlemesi; `Tracon.AspNetCore` + `Microsoft.Agents.AI.Hosting` `1.22.0-preview.260918.1` | **Negatif kontrol:** yayınlanmış `1.0.0-preview.2` ile restore **uyarısız** geçti (kusurun kendisi). **Düzeltme:** preview.3 adayıyla `warning NU1608: ... requires Microsoft.Agents.AI.Hosting (= 1.20.0-preview.260831.1) but version ... 1.22.0-preview.260918.1 was resolved`. Nuspec beş bağımlılığı `[x]` yazar; `Tracon.UI` `THIRD-PARTY-NOTICES.txt`'yi kökte taşır ve repo dosyasıyla aynıdır (MT-PKG-161; 162 adım 1–2) | 2026-09-24 |
| KN-032 | `MT-PKG-160` — nuget.org baytı = CI'ın doğruladığı bayt | Tamamlandı | `nuget-verified` (koşum `36057172063`, artifact `10834248786`) indirildi; 20 paket `v3-flatcontainer`'dan; `.signature.p7s` hariç girdi kümesi ve her girdinin baytı karşılaştırıldı | 20/20 aynı, 0 fark. `Tracon.Core`: CI 16 girdi, nuget.org 17 (fark yalnız imza); ham SHA-256 farklı (beklenen). `Tracon.Cli` 22:03 UTC'de görününce ayrıca karşılaştırıldı: CI 124 girdi, nuget.org 125, fark yok | 2026-09-24 |
| KN-033 | "Re-run failed jobs" artifact'i yeniden kullanır mı | Tamamlandı | Koşum `36048047050` işleri (`filter=all`) ve artifact listesi | Evet: 2. denemede ubuntu işi yeniden koşmadı (başlangıç 19:25:32Z iki denemede aynı); tek `nuget-packages` 1. denemeden (20:00:04Z); 2. denemenin `release-dryrun`'u onu okuyup `nuget-verified`'ı üretti (20:43:07Z). `test-results-windows-latest` her denemede ayrı yüklendi | 2026-09-24 |
| KN-034 | `1.0.0-preview.4` yayın provası | Tamamlandı | `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` (`a98083a1`, 2026-10-15). İlk koşum Faz 209 provasının aynı kimlikli eski paketlerini koruyup durdu; set `artifacts/package/yayin/1.0.0-preview.4-onceki-d81ac76f`'ye alındı | Çıkış 0: kırıcı liste 5 tip / 0 TFM düşüşü / 3 paket, hepsi `Unreleased` notunda; 20 paket; `npm publish --dry-run`; 6 exact-version packed sample, Native AOT smoke, net8.0 tüketici smoke |
| KN-035 | CI'ın SDK'sıyla yerel derleme | Tamamlandı | `~/.dotnet/dotnet` 10.0.402: `build Tracon.slnx -c Release --no-incremental` + `format --verify-no-changes` (2026-10-15). Negatif kontrol: düzeltmeden önce aynı komut `CA1873` verdi (üç dosya; sonra `WorkflowNodeConcurrencyLimit`) | Çıkış 0 / 0 hata; yerel 10.0.100 aynı kodda yeşildi — kapı SDK sabitidir (A-73) |

### Henüz ölçülmeyen alanlar

- Tam `.nuspec` dependency graph'ının paket stratejisine göre değerlendirilmesi ve beklenmeyen içerik taraması.
- Gerçek Source Link kaynak çözümleme davranışı. Yerel ortamda `dotnet sourcelink` aracı yoktur.
- ~~Meta paketin PDB içermeyen `.snupkg` üretmesi.~~ **Kapandı 2026-09-19 (KN-024):** artık üretmiyor; 20 pakete 18 sembol paketi düşer ve eksik ikisi doğru.
- ~~`Tracon.Cli` paketinin 28 MB boyutu.~~ **Kapandı 2026-09-19 (KN-024):** çok-RID yerel SQLite + `SqlClient` win/unix; `dotnet tool` üç sağlayıcıya da migration koşar.
- Public API yaprakları ve her yüzey için tut/değiştir/kaldır/internal/capability/ertele kararı.
- Güvenlik ve transport sınırlarının artifact tabanlı runtime probe'ları.
- XML, package README, root README, `docs-site`, sample ve release note drift'i.
- ~~NuGet.org hesap, sahiplik, 2FA, Package ID uygunluğu ve publishing credential durumu.~~ **Kapandı (KN-022, OP-003).**
- Güncel resmi NuGet operasyon seçenekleri ve trusted publishing uygunluğu.
- ~~Tam manuel kabul setinin güncel koşumu.~~ **Kapandı 2026-09-18/19:** 36/36
  aile koşuldu (KG-027), 1.859 case — 1.813 Geçti · 1 Kaldı; 48 kusur kaydının
  hepsi kapandı. Kayıt `arsiv/manuel-test-kosum-2026-09/` altındadır.

## 6. Açık blocker'lar

**2026-09-02 itibarıyla preview için açık 🔴 yoktur.** BL-053 ve BL-054 aynı gün
açıldı ve kapandı; BL-055 açık bir 🟡'dir. BL-057 (2026-09-23) preview'u değil
**GA'yı** bloklar ve bir bakımcı eylemidir. BL-058 (2026-09-24, A-60) da
GA'yı bloklar ve bir kod işidir. Aşağıdaki 2026-08-28 maddelerinin
çoğu blocker değil, doğrulama kapısıdır.

| Kimlik | Durum | Bulgu veya soru | Seviye | Ölçülen kanıt | Sorumlu workflow | Doğrulama ölçütü |
|---|---|---|---|---|---|---|
| BL-001 | Tamamlandı | Exact release artifact'i üret ve temel kapıyı çalıştır | Blocker değil | Exact dry-run çıkış `0`; 20 paket, 160 sample testi ve AOT smoke yeşil | `nuget-danismani` | Tamamlandı |
| BL-002 | Tamamlandı | 20 public paketin tamamının ilk preview için gerekli ve yeterince olgun olup olmadığı bilinmiyor | Blocker değil — kapsam kararı verildi | UR-002: kullanıcı tam entegrasyon setini seçti | `nuget-danismani` | Kapsam sabit; olgunluk artık paket bazında değil seam bazında (BL-003) ölçülür |
| BL-003 | **Tamamlandı** | Extension seam sözleşmelerinin birbiriyle tutarlılığı ölçüldü — 11/11 küme, 78 seam | Blocker değil — ölçüm bitti, bulgular BL-006/026/027/037/041 (🔴) + BL-007…051 (🟡/🟢) olarak kaydedildi | §15 tam matris planı ve küme raporları | `nuget-danismani` | Tamamlandı — bkz. §4 nihai özet |
| BL-004 | **Tamamlandı (KN-022)** | NuGet.org hesap, owner ve credential modeli sabitlendi | Blocker değil — kapandı | Kişisel owner `farukatasoy` (2026-09-12'den beri owner org `Tracon` — OP-001); OIDC trusted publishing; `Tracon*`; push-only; bir saatlik geçici key | Kullanıcı + yayın operasyonu | İlk başarılı publish policy aktivasyonunu kalıcılaştırır |
| BL-005 | **Tamamlandı (2026-09-15)** | Meta paket PDB içermeyen `.snupkg` üretiyor | 🟢 Kapandı | Yeniden üretildi: `.snupkg` 4 metadata girdisi + **0 PDB** taşıyordu (`_rels/.rels`, nuspec, `[Content_Types].xml`, psmdcp). Kök neden `IncludeBuildOutput=false` — sembolü olacak assembly yok. `Tracon.Templates` emsaliyle `IncludeSymbols=false` kondu; `.snupkg` artık hiç üretilmiyor, ana `.nupkg` 7 girdi ve 18 bağımlılıkla değişmedi | `nuget-danismani` | Tamamlandı — taksonomi (`PackageProfile.Meta`), `ReleaseArtifactTests` ve `kapi.py yayin` birlikte güncellendi; 53/53 yeşil |
| BL-006 | **KAPANDI** (2026-08-27, `kusur-giderme`) | `ITenantProviderBindingStore` BYOK lookup'ı üç farklı case-sensitivity davranışı taşıyor: `InMemoryTenantProviderBindingStore` ordinal case-sensitive `(TenantId, ProviderName)` anahtarı kullanıyor; SQL store'lar ham `=` predikatı kullanıyor (DB collation'a bağlı — Postgres/SQLite case-sensitive, SQL Server genelde değil); `ModelProviderRegistry` ve `TenantProviderEndpoints` ise `OrdinalIgnoreCase` kullanıyor. Admin `"OpenAI"` yazıp agent tanımı `"openai"` beklerse, Postgres/SQLite'ta binding sessizce bulunamaz ve akış global setup credential'ına düşer — bu, `ModelProviderRegistry.cs:330-332`'deki "sessiz düşme yok" yorumunun tam reddettiği senaryo | 🔴 Preview blocker | `src/Tracon.Core/Tenancy/InMemoryTenantProviderBindingStore.cs:8,24`; `src/Tracon.Sql.Shared/Internal/SqlQueriesBase.cs:1655-1658`; `src/Tracon.Core/Models/ModelProviderRegistry.cs:135,321,330-332`; `src/Tracon.AspNetCore/Endpoints/TenantProviderEndpoints.cs:169` | `nuget-danismani` → `kusur-giderme` | **Tamamlandı.** Kırmızı test önce yazıldı (`A_binding_saved_under_a_different_letter_case_is_still_the_tenants_binding`, 3/3 düştü: `LastCredential should not be null`) → `TenantProviderBinding.NormalizeProviderName` (public, invariant lower) eklendi, her iki store hem yazarken hem sorgularken uyguluyor → 3 migration mevcut satırları katlıyor (PostgreSQL 0038, SQLite/SQL Server 0025) → contract'a 3 case-mismatch case'i eklendi (4 implementasyonda koşar) → yeşil (Core 2056/2056, SQLite entegrasyon 15/15). K-639. **Sınıf taraması yapıldı:** 5 aday store incelendi, 4'ü tutarlı çıktı (`Experiment`/`AgentDefinition` her katmanda `Ordinal`, `Idempotency-Key` opak token, `Session.Id` sunucu üretimli) — `provider` tek outlier'dı |
| BL-007 | Açık | `ITenantStore`, `IContentProtector`, `IDataSubjectStore`, `IDataSubjectResolver` için reusable contract test taban sınıfı yok — üçüncü taraf implementasyonun koşabileceği bir suite yok, oysa `ApiKeyStoreContract`/`QuotaStoreContract`/`TenantEgressPolicyStoreContract`/`TenantProviderBindingStoreContract` aynı kümede gerçek davranış ölçen contract'lar olarak var | 🟡 1.0 blocker | `src/Tracon.Testing.Contracts.Xunit/Contracts/` içinde bu dört arayüz için sınıf yok | `nuget-danismani` → faz zinciri | Her dördü için contract sınıfı eklenir; en az bir dış sample'da koşulur |
| BL-008 | **KISMEN KAPANDI (2026-08-28, Faz 122)** | Kayıt API ergonomisi Küme B içinde tutarsız — yalnız `IContentProtector` (`AddContentProtection`/`AddContentProtection<T>()`) ve `ITenantContext` (`UseTenancy()`) için dedicated builder metodu var; `ITenantStore`, `ITenantEgressPolicyStore`, `ITenantProviderBindingStore`, `IApiKeyStore`, `IQuotaStore`, `IDataSubjectStore` için yok — consumer ham `services.Replace(ServiceDescriptor.Singleton<...>())` çağırmak zorunda ve bu desen hiçbir yerde dokümante değil (düz `AddSingleton` iki rakip kayıt bırakır) | 🟡 1.0 blocker (kalan: 6 tekil-seam store) | Küme B raporu — 6 arayüz için dedicated `Add*`/`Use*` yok | `nuget-danismani` → faz zinciri (Faz 122 ✅ kısmen) | **Faz 122'de kapsam bilinçli daraltıldı:** ölçüm bu 6 store'un TEKİL seam olduğunu gösterdi (`TryAdd*`, "önce kaydet kazanır" zaten çalışıyor) — dedicated `Add*` yerine `ITraconBuilder.Services`'in XML dokümanına tekil/çoklu seam farkını anlatan bir sözleşme tablosu + `docs-site/guides/write-your-own-agent-decorator.md`'de aynı tablo eklendi (metin kapısı: `OrderingContractDocumentationTests`). Asıl **çoklu** seam eksiği (`IAgentDecorator`, aynı raporun asıl kod kusuru) `AddAgentDecorator` üçlüsüyle kapandı. 6 tenant/store arayüzü için dedicated `Add*` hâlâ yok — sonraki bir turda ele alınabilir |
| BL-015 | **KAPANDI (2026-08-28, Faz 123)** | `ModelProviderContract`/`ModelProviderCredentialContract` hiçbir shipped adapter (Anthropic/Azure/Google/OpenAI) test projesinde türetilmiyordu — yalnız `Tracon.Samples.CustomModelProvider` sample'ında gerçek kullanılabilirlik kanıtlanmıştı. Bir adaptörün credential-cache mantığındaki regresyon (örn. "iki farklı credential aynı client'ı paylaşmamalı") shipped provider'larda CI'da yakalanmıyordu | ~~🟡~~ ✅ Kapandı | `tests/Tracon.{Anthropic,Azure,Google,OpenAI}.UnitTests/*.csproj` bu contract sınıflarını türetmiyordu | `nuget-danismani` → **[Faz 123](arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md)** | Dördü de `ModelProviderContract` + `ModelProviderCredentialContract`'ı türetiyor (Anthropic/Google ayrıca `ModelProviderSettingsContract`'ı; Azure/OpenAI gerekçeli `ContractCoverage` muafiyeti taşıyor — ikisi de `ProviderSettings` okumuyor). **Sözleşme gerçek bir kusur buldu** (K-646): dördü de credential başına SDK istemcisini önbelleğe alıyordu ama döndürdüğü `IChatClient` sarmalayıcısını her çağrıda yeniden üretiyordu; `TenantChatClientCacheKey` eklenip dördüne de aynı düzeltme uygulandı (sınıf taraması) |
| BL-016 | **KISMEN KAPANDI (2026-08-28, Faz 122)** | Küme B/C cila bulguları (🟢, toplu): `IContentProtector`'ın kayıtsız durumda fail-**open** (plaintext) davranışı release notes'ta vurgulanmalı (davranış doğru ve dokümante, keşfedilebilirlik eksik); `NullDataSubjectStore` erasure isteğine sessizce "başarılı, 0 satır silindi" dönüyor — `IDataSubjectResolver`'ın `409`'una kıyasla tutarsız bir tuzak; Küme B seam'leri için hiç dış sample yok; ~~`AddModelProvider<T>()` generic overload'ı yok (yalnız instance/factory var)~~ **KAPANDI**; 4 provider adaptöründen 3'ü (Anthropic/Azure/Google) için AOT ölçüm kaydı dokümante değil (yalnız OpenAI ölçülmüş, `docs/MIMARI.md` §9) | 🟢 Doküman/cila (kalan: fail-open notu, erasure tutarsızlığı, dış sample, AOT kaydı) | Küme B ve C raporları | `nuget-danismani` → doküman senkronu (Faz 122 ✅ `AddModelProvider<T>()`) | `AddModelProvider<T>()` eklendi (`ITraconBuilder.AddModelProvider<TProvider>()`, `TryAddEnumerable`). Kalan dört madde doküman senkronu turunu bekliyor; kod değişikliği gerektirmiyor |
| BL-017 | Açık | `IContentGuard` ve `IToolAuthorizationHandler` için reusable contract test yok (`IToolRegistry`, `IPendingApprovalStore`, `IToolApprovalRuleStore`'un aksine) — üçüncü taraf implementasyon fail-closed/thread-safety/tenant davranışını doğrulayacak resmi bir suite'e sahip değil | 🟡 1.0 blocker | `src/Tracon.Testing.Contracts.Xunit/Contracts/` içinde bu ikisi için sınıf yok | `nuget-danismani` → faz zinciri | Her ikisi için contract sınıfı eklenir; en az bir dış sample'da koşulur |
| BL-018 | **KAPANDI (2026-08-28, Faz 122)** | `IContentGuard` kayıtsızken fail-open (hiç guard koşmuyor); davranış XML dokümanda açık ama benzer "sessiz boşluk" seam'lerinde kullanılan `NonPersistentStorageWarningService` türünden bir başlangıç uyarısı yok | 🟡 1.0 blocker | `src/Tracon.Core/TraconServiceCollectionExtensions.Registration.Operations.cs:96-99`; `IContentGuard.cs:14-21` | `nuget-danismani` → faz zinciri (Faz 122 ✅) | `SilentGapWarningService` (yeni, `NonPersistentStorageWarningService`'in aynı üç kuralı: yalnız Production · asla fırlatma · yalnız kayıtlara bak) eklendi; Production'da `IContentGuard` kayıtsızken bir kez `Warning` düşer. Gerçek `samples/Tracon.Api`'de doğrulandı (MT-DIAG-055/056) |
| BL-019 | **KISMEN KAPANDI (2026-08-28, Faz 122)** | Küme E kayıt ergonomisi tutarsız — `IToolAuthorizationHandler` için dedicated `Add*` yok, ~~`IContentGuard` yalnız generic `AddContentGuard<T>()` sunuyor (instance/factory yok)~~ **KAPANDI**; custom `IContentGuard` veya SQL-dışı approval store için dış sample yok; sütun 14 (custom tool/guard exception normalizasyonu) tam doğrulanamadı | 🟡 1.0 blocker (kalan: `IToolAuthorizationHandler`, dış sample, sütun 14) | Küme E raporu | `nuget-danismani` → faz zinciri (Faz 122 ✅ `IContentGuard` instance/factory) | `AddContentGuard(IContentGuard)` ve `AddContentGuard(Func<IServiceProvider, IContentGuard>)` eklendi, `AddContentGuard<T>()`'in kardeşleri. `IToolAuthorizationHandler` dedicated `Add*`'i (tekil seam, `ITraconBuilder.Services`'in metin kapısı sözleşmesi zaten kapsıyor) ve dış sample/sütun 14 kapsam dışı bırakıldı |
| BL-021 | Bilgi | Küme E cila bulguları (🟢, toplu): `RecordToolInvocation` metric `TenantId` tag'i taşımıyor (kasıtlı kardinalite hijyeni, dokümante değil); `IToolAuthorizationHandler` denial tracking'in dedicated metric'i yok | 🟢 Doküman/cila | Küme E raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-022 | Açık | `IMcpOAuthCoordinator` — kümenin en yüksek kiracı-izolasyon/CSRF riskli sınıfı — hiçbir testte referans edilmiyor; `McpTenantServerKey`'in kendisi önceki bir string-interpolation sızıntısını kapatmak için özel yazılmış, yani bu alan daha önce kusur üretmiş | 🟡 1.0 blocker | `tests/` altında `McpOAuthAuthorizationCoordinator`/`IMcpOAuthCoordinator` referansı yok; `McpTenantServerKey.cs:1-13` | `nuget-danismani` → faz zinciri | State/tenant binding'i kilitleyen bir contract/regresyon testi eklenir |
| BL-023 | Açık | `IMcpPromptClient`/`IMcpResourceClient`/`IMcpServerStore` için reusable contract test yok; MCP client/host'u `PackageReference` ile (yalnız `ProjectReference` değil) koşan dış bir sample yok — paketlenmiş tüketici davranışı 1.0 öncesi doğrulanmamış | 🟡 1.0 blocker | Küme F raporu | `nuget-danismani` → faz zinciri | Contract sınıfları eklenir; `samples/` içinde en az biri `PackageReference`'a geçirilir |
| BL-024 | **KAPANDI (Faz 121)** | Küme F'nin 6 arayüzünün hiçbirinin XML dokümanı DI lifetime'ı (`singleton`) açıkça belirtmiyordu — üçüncü taraf implementasyon bunu kaynağı okuyarak öğrenmek zorundaydı | ~~🟡~~ ✅ Kapandı | Küme F raporu | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | `IMcpResourceClient`, `IMcpPromptClient`, `IMcpResourceContextProviderFactory`, `IMcpOAuthCoordinator`, `IMcpToolRefresher`, `IMcpServerStore` — hepsinin XML dokümanına "DI lifetime — singleton" bölümü eklendi; `SeamContractDocumentationTests` tarafından kilitli |
| BL-025 | Bilgi (kısmen kapandı) | Küme F cila bulguları (🟢, toplu): `IMcpServerStore` ad-şekli doğrulaması abstraction'da değil bağlantı katmanında (yalnız log uyarısı, sessiz başarısızlık); ~~`CatalogToolCallHandler` MCP-host tarafında kendi run hatasının ham `ex.Message`'ını dış çağırana döndürüyor~~ **Faz 119'da kapandı** (BL-027/BL-037 sınıf taramasının bulduğu 21 vakadan biri, `SafeErrorText` uygulandı); MCP'ye özel `span`/tag yok, genel MAF OpenTelemetry enstrümantasyonuna biniyor | 🟢 Doküman/cila | Küme F raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-026 | **SEVİYE DÜŞÜRÜLDÜ** (2026-08-27, `kusur-giderme` Adım 2/7) | **Drain/yeni-run yarışı**: `DrainGate.Check` (`AgentEndpoints.cs:193`) ile `IRunCancellationRegistry.Register` (`RunRecordingAgent.cs:214-218`) arasında bir pencere var ve `TraconDrainService.StopAsync` bu pencerede `ActiveCount==0` görüp erken dönebilir. **Ancak iş kaybı OLUŞMUYOR** — iki bağımsız mekanizma bu boşluğu zaten kapatıyor | ~~🔴~~ → 🟡 1.0 blocker (muhasebe hassasiyeti, iş kaybı değil) | **Ölçüldü:** (1) HTTP yolu — `TraconDrainService` DI'ye **son** kaydedildiği için `StopAsync`'i **ilk** koşar; `GenericWebHostService` ise **son** durur, yani Kestrel'in kendi request draining'i o isteği tamamlanana kadar bekletir. (2) Job yolu — `JobWorkerBackgroundService.ExecuteAsync`'in `finally` bloğu `WaitForRunningJobsAsync()` çağırır (satır 82, 181-187) ve leased her işi bekler. (3) `TraconDrainOptions.Enabled` **varsayılan `false`** (`TraconDrainOptions.cs:18`) — yarış yalnız drain'i açıkça açan kurulumu ilgilendirir | `faz-planlama` (blocker değil) → **belge kısmı [Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)'de kapandı** | Denetimin "süreç başlamak üzere olan run'ı yarıda keser" iddiası yeniden üretilemedi. Kalan gerçek kusur: drain servisi reklam ettiği garantiyi **kendi başına** sağlamıyor, iki yedek mekanizmaya bel bağlıyor — bu artık dokümante: `ITraconDrainState`'in XML dokümanı iki yedek mekanizmayı (Kestrel request draining, `WaitForRunningJobsAsync`) ve bunlara bel bağladığını açıkça anlatıyor; `TraconDrainOptions.Enabled` de aynı nota işaret ediyor (Açık Soru 4, seçenek C — ikisine de). Ölçülmüş bir iş-kaybı repro'su üretilmeden 🔴 sayılmaz |
| BL-027 | **KAPANDI (Faz 119)** | **`IRunStore`'a ham exception mesajı sızıyor**: `RunRecordingAgent.Completion.cs:282` `RunError.Message = exception.Message`'ı `ContentGuardPipeline`'dan geçirmeden yazıyor. Kardeş yol `IRunInputStore` aynı sınıf bir kusur için (`HATA-S3-006`) daha önce düzeltilmiş ve guard'dan geçiriliyor — düzeltme run-error yoluna uygulanmamış | ~~🔴~~ ✅ Kapandı | `src/Tracon.Core/.../RunRecordingAgent.Completion.cs:282`; kıyasla `RunRecordingAgent.Persistence.cs:36-41` (`HATA-S3-006` düzeltmesi) | `nuget-danismani` → **`faz-planlama`** → **[Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md)** | **SINIF TARAMASI YAPILDI (2026-08-27) — bilinen iki vakanın ÜSTÜNE 19 vaka daha bulundu, uygulama sırasında 5 ek vaka daha (26 toplam).** Bkz. §16. `ContentGuardPipeline` genişletilmedi (opt-in, varsayılan boş); onun yerine yeni paylaşılan `SafeErrorText` (K-640) her 26 sitede uygulandı, mimari cırcır kapısı (`RawExceptionTextSiteTests`) 22. sızıntıyı otomatik yakalar |
| BL-028 | **KAPANDI (Faz 121)** | Cancellation tamamen cooperative (`RunCancellationRegistry.TryCancel` yalnız `CancellationTokenSource.Cancel()` çağırıyor) ama `IRunCancellationRegistry`'nin arayüz dokümanı bu sınırı belirtmiyordu — tüketici `TryCancel`'ın işi/faturalamayı gerçekten durdurduğunu varsayabilirdi | ~~🟡~~ ✅ Kapandı | `src/Tracon.Core/.../RunCancellationRegistry.cs:27-51`; `IRunCancellationRegistry.cs` | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | `TryCancel`'ın XML dokümanına "Guarantee limit: cancellation is cooperative, not forced" bölümü eklendi; `RunCancellationRegistryTests.TryCancel_does_not_stop_a_run_body_that_never_reads_its_token` bunu FONKSİYONEL testle de ölçüyor (token'ı hiç okumayan bir "run body" sinyalden sonra da çalışmaya devam ediyor) |
| BL-029 | **KAPANDI (Faz 121)** | Tenant-mode dokümantasyonu Küme A içinde tutarsızdı — yalnız `IRunStore` EXPECTED/AMBIENT/tenant-independent tablosu taşıyordu; `ITraceStore.GetTraceByRunAsync` fiilen ambient-tenant (`SqlTraceStore.cs:85`) ama arayüz dokümanında hiç tenant notu yoktu; `IRunScoreStore`/`IRunInputStore` yalnız parametre bazlıydı, adlandırılmış mod yoktu | ~~🟡~~ ✅ Kapandı | Küme A raporu | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | `IRunStore`'un tenant-mode deseni Küme A'nın kalan 9 üyesine uygulandı: `IRunScoreStore`/`IRunInputStore`/`IRunEventSink` EXPECTED; `ITraceStore` karışık (yaz=EXPECTED, oku=AMBIENT); `IRunCancellationRegistry` karışık (kayıt=EXPECTED, sayaçlar=TENANT-INDEPENDENT); `IRunAttributionContext`/`IRunErrorClassifier`/`IRunPricingResolver`/`ITraconDrainState` TENANT-INDEPENDENT (tenant kavramı yok) |
| BL-030 | Açık | `IRunEventSink`, `IRunAttributionContext`, `IRunCancellationRegistry`, `IRunErrorClassifier`, `IRunPricingResolver`, `ITraconDrainState` için reusable contract test yok (yalnız 4 storage arayüzünde var); 8/10 arayüz için dış sample yok (yalnız `IRunStore`/`IRunScoreStore` için `FileRunStore` var) | 🟡 1.0 blocker | Küme A raporu | `nuget-danismani` → faz zinciri | En azından `IRunCancellationRegistry` (root/child, tenant-mismatch) için contract sınıfı eklenir |
| BL-031 | Bilgi | Küme A cila bulguları (🟢, toplu): `ITraceStore`'un yüksek-kardinalite tag'lerinin (run id, tenant id) kasıtlı olduğu dokümante değil (`IRunAttributionContext.Labels`'ın kardinalite uyarısıyla tezat); `IRunPricingResolver`/`IRunErrorClassifier` "cache yok" tasarım kararı dokümante değil | 🟢 Doküman/cila | Küme A raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-033 | **KAPANDI (2026-08-28, Faz 122)** | Decorator exception'ları source exception'larının aksine normalize edilmiyor — `CompositeAgentCatalog.cs:142-145,216-219` `decorator.Decorate(...)`'ı try/catch olmadan çağırıyor, oysa aynı metotta iki satır üstteki source çağrısı `HandleSourceFailure` ile sarılı. Üçüncü taraf bir `IAgentDecorator` (veya `ToolApprovalAgentDecorator`) fırlatırsa ham exception `AgentEndpoints.cs:774`'ün yalnız `TraconException` yakalayan catch'ini atlayabilir — ASP.NET Core varsayılan handler'ına sızıp sızmadığı **ölçüldü (2026-08-27): sızmıyor** — `grep -rn "UseExceptionHandler|IExceptionHandler|AddProblemDetails" src --include="*.cs"` yalnız `JsonBindingProblemMiddleware.cs:16`'daki yorumu buluyor, yani Tracon global handler **kaydetmez** ve Production'da ASP.NET Core varsayılanı gövdesiz `500` döner; ham mesaj HTTP'ye çıkmaz. **Kalan gerçek kusur dar ve tutarlılıktır:** üçüncü taraf decorator hatası `TraconException`'a normalize edilmediği için kardeş `source` yolunun ürettiği sınıflandırılmış hata yerine çıplak `500` verir | 🟡 1.0 blocker (leak değil, tutarlılık) | `src/Tracon.Core/Catalog/CompositeAgentCatalog.cs:142-145,216-219`; `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs:774` | `nuget-danismani` → derinleştirme, sonra `kusur-giderme` (Faz 122 ✅) | **Sınıf taraması** dört çağrı yerini buldu, dördü de düzeltildi: `CompositeAgentCatalog`'un iki döngüsü artık `HandleSourceFailure(source, "decorate", exception)` ile sarılı (aynı `TraconAgentSourceException`, aynı metrik/log); `AgentDecoratorPipeline.Apply` kendi normalizasyonunu kazandı (`descriptor.SourceName` ile); `RunReplayService`/`RunContinuationJobHandler`'ın kendi ham döngüleri `AgentDecoratorPipeline.Apply`'a devredildi (DRY, tek düzeltme dört yeri kapsıyor). `OperationCanceledException` her yerde `throw;` ile geçiyor. Test: `AgentSourceFaultIsolationTests` — 2 yeni case (normalize + iptal geçirimi) |
| BL-034 | **KAPANDI (2026-08-28, Faz 122 — doküman kısmı 2026-08-27 `kusur-giderme`'de)** | ~~`IAgentDecorator` için hiç builder registration API'si yok (`IAgentSource`/`IRunJudge`/`IModelProvider`'ın aksine) — consumer ham `services.TryAddEnumerable(...)` çağırmak zorunda~~ **KAPANDI**; ~~ayrıca `IAgentDecorator.Order`'ın XML dokümanı kendi kendiyle çelişiyor~~ **doküman kusuru KAPANDI** | 🟡 1.0 blocker → **kapandı** | **Ölçüldü ve karar verildi (K-642):** sayısal yön DEĞİŞMEDİ, yanlış olan cümleydi. Yeni yazılan iki davranış testi (`AgentDecoratorOrderingTests`) mevcut mekanizmayla yeşil geçti; el yazısı `docs-site/concepts/runs.md` diyagramı da zaten doğruydu. **Sınıf taraması 2 vaka daha buldu** — `IAgentSource.Priority` yönü hiç söylemiyordu, `IAgentCatalog.ListAsync` "higher priority" diyerek yüksek sayı gibi okunuyordu; ikisi de düzeltildi. Kapı: `OrderingContractDocumentationTests` (üç vakada da ayrı ayrı kırmızı verdiği ölçüldü) | `nuget-danismani` → `kusur-giderme` (doküman ✅) → faz zinciri (Faz 122 ✅ registration API'si) | `AddAgentDecorator<T>()`/`AddAgentDecorator(IAgentDecorator)`/`AddAgentDecorator(Func<IServiceProvider,IAgentDecorator>)` eklendi — `ITraconBuilder`'ın kendi üyesi (kardeş `AddAgentSource`/`AddRunJudge` ile aynı desen), `TryAddEnumerable`. Üç decorator kaydedilip üçünün de `Order` sırasına uyarak zincirde koştuğu ölçüldü (`AgentDecoratorRegistrationTests`) |
| BL-035 | **KAPANDI (Faz 121)** | `IAgentDefinitionStore` ambient `ITenantContext` kullanırken `IAgentSkillStore`/`ISkillScriptGrantStore` explicit `tenantId` parametresi kullanıyor — aynı kümede tutarsız tenant-parametre şekli (bug değil, her ikisi de doğru filtreleniyor, ama üçüncü taraf implementer'ın arayüz başına ayrı öğrenmesi gerekiyordu) | ~~🟡~~ ✅ Kapandı | Küme D raporu | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | Üçünün de XML dokümanına tenant-mode bölümü eklendi; `IAgentDefinitionStore`'un dokümanı şekil farkının GERÇEK gerekçesini yazıyor: iki nesil farklı store konvansiyonu (kurucu enjeksiyon vs. açık parametre), derin bir tasarım kuralı değil — bir implementer her arayüzün KENDİ remarks'ına bakmalı, kümeden tutarlılık varsaymamalı |
| BL-036 | Bilgi | Küme D cila bulguları (🟢, toplu): decorator sıralama mantığı (`OrderByDescending(d => d.Order)`) 4 yerde ayrı ayrı tekrarlanıyor (`CompositeAgentCatalog.cs:40`, `RunContinuationJobHandler.cs:55`, `RunReplayService.cs:77`) — var olan `AgentDecoratorPipeline.Apply` yalnız 2 yerde kullanılıyor, şu an tutarlı ama bakım riski; `IAgentDecorator`/`IAgentCatalog` için contract test veya dış sample yok | 🟢 Doküman/cila | Küme D raporu | `nuget-danismani` → doküman senkronu / iç refactor | Docs-site'a eklenir; refactor isteğe bağlı |
| BL-037 | **KAPANDI (Faz 119)** | **`WorkflowRunner.ToRunError`'a ham exception mesajı sızıyor** — BL-027 ile birebir aynı kusur sınıfı, farklı yol: `unwrapped.Message` hiçbir guard'dan geçirilmeden `RunEvent.Text`'e yazılıyor ve persist ediliyor | ~~🔴~~ ✅ Kapandı (BL-027 ile birlikte) | `src/Tracon.Workflows/Internal/WorkflowRunner.cs:1179-1198` | `nuget-danismani` → **[Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md)** (BL-027 ile BİRLİKTE, tek sınıf taraması) | `ToRunError` artık `SafeErrorText.ForPersistence` + korelasyon kimlikli `ILogger` çağrısı kullanıyor; `WorkflowJobHandler`'ın kendi ayrı `exception.Message` yolu da aynı turda kapatıldı |
| BL-038 | **KAPANDI (Faz 121)** | Workflow resume'un side-effecting adımları tekrar çalıştırabileceği (at-least-once semantics) yalnız `AddWorkflowFunction<T>()`'ın XML dokümanında anlatılıyordu — `Tracon.Abstractions`'daki `IWorkflowRunner`/`IWorkflowCheckpointStore` (paketin asıl public sözleşme yüzeyi) bundan hiç bahsetmiyordu; davranış doğru ve kasıtlı, yalnız yanlış dosyada dokümanteydi | ~~🟡~~ ✅ Kapandı | `TraconWorkflowFunctionExtensions.cs:66-77` vs. `IWorkflowRunner.cs`, `IWorkflowCheckpointStore.cs` | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | At-least-once notu `IWorkflowRunner.ResumeStreamingAsync` ve `IWorkflowCheckpointStore.CreateAsync`'in kendi XML dokümanına da eklendi (eski konumdaki metin duruyor, tekrar etmiyor, birbirine referans veriyor) |
| BL-039 | **KISMEN KAPANDI (2026-08-28, Faz 122)** | `IWorkflowRunner`/`IWorkflowFunctionCatalog` için contract test yok (store'ların aksine); 4 arayüzün hiçbiri için dış `Custom*` sample yok (`CustomTool`/`CustomModelProvider`/`CustomRunJudge`/`CustomAgentSource`'un aksine); ~~iki kod-tanımlı workflow aynı adı paylaşırsa ham `.NET ArgumentException` fırlıyor (`WorkflowCatalog.cs:41-44`) — `WorkflowFunctionRegistry`'nin aynı durumda verdiği net `TraconException`'la tutarsız~~ **KAPANDI**; DI lifetime/thread-safety 4 arayüzün hiçbirinde dokümante değil | 🟡 1.0 blocker (kalan: contract test, dış sample, DI lifetime dokümanı) | Küme G raporu | `nuget-danismani` → faz zinciri (Faz 122 ✅ duplicate-name) | `WorkflowCatalog`'un kurucusu artık `ToDictionary` yerine elle `TryAdd` döngüsü kullanıyor; duplicate ad `WorkflowFunctionRegistry.cs:48` ile aynı desende `TraconException` fırlatıyor (`"More than one code-defined workflow is registered with name '{name}'."`). Test: `WorkflowCatalogTests` (yeni). Contract test taban sınıfı, dış sample ve DI lifetime dokümanı bu fazın kapsamı dışında bırakıldı (kulvar 6 yalnız duplicate-name kusurunu hedefliyordu) |
| BL-040 | Bilgi | Küme G cila bulguları (🟢, toplu): `IWorkflowFunctionCatalog`'un cache semantiği (executor identity stability) yalnız kayıt call site'ındaki `//` yorumunda anlatılıyor, arayüz dokümanında değil; workflow/function adları için ad-şekli doğrulaması dokümante/zorlanmış değil (yalnız non-empty kontrolü var) | 🟢 Doküman/cila | Küme G raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-041 | **KAPANDI (Faz 120)** | Denetim bunu "`IIdempotencyStore` amacına rağmen job loop'unda kullanılmıyor" diye kaydetmişti. **Ölçüldü, çerçeve yanlıştı:** `IIdempotencyStore`'un kendi XML dokümanı (`IIdempotencyStore.cs:6-14`) onu açıkça **HTTP `Idempotency-Key` başlığı** mekanizması olarak tanımlıyor ("exactly as the HTTP `Idempotency-Key` standard prescribes"); tüketicileri `IdempotencyFilter` ve `InboundTriggerDispatcher`. Job loop'unda tekrar koruması **zaten vardı** (`JobItemStatus.Pending` kontrolü, üç yerleşik handler'da da koşuyor). Job loop'una bu store'u bağlamak gereksiz ikinci bir mekanizma olurdu. **Geriye kalan gerçek kusur tek ve dardı:** `IJobHandler`'ın sözleşmesi at-least-once'ı söylemiyordu | ~~🔴~~ ✅ Kapandı | `IJobHandler.cs` (artık `ExecuteAsync`'in `<remarks>`'i at-least-once'ı ve `JobContext.Items`'ın süzülmemiş geleceğini açıkça söylüyor) | **[Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md)** | Sözleşme XML'e yazıldı + `JobHandlerContract` (`Tracon.Testing.Contracts.Scheduling`, üç yerleşik handler + `Tracon.Samples.CustomJobHandler` dış sample'ı türetiyor) + `JobLeaseExpiryTests` (`InMemoryJobStore` üzerinde: abandoned lease süresi dolunca gerçekten yeniden kiralanıyor ve `ListItemsAsync` daha önce `Completed` işaretlenen item'ı süzmeden geri veriyor) davranışın gerçekten var olduğunu ölçüyor |
| BL-042 | **KAPANDI (Faz 121)** | `ISingletonLeaseStore`'un lease-sahibi donduğunda (`GC` duraklaması, thread starvation, ağ bölünmesi) oluşan sınırlı split-brain penceresi ne arayüz dokümanında ne `SingletonGuard.cs`'de belirtiliyordu — "eventually correct, strictly exclusive değil" garantisi dokümante değildi | ~~🟡~~ ✅ Kapandı | `SingletonGuard.cs:99`; `SqlSingletonLeaseStore.cs:61-79` | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | `ISingletonLeaseStore`'un XML dokümanına "Guarantee limit: eventually correct, NOT strictly exclusive" bölümü eklendi, split-brain penceresini ve iki-owner senaryosunu açıkça anlatıyor; `SeamContractDocumentationTests`'in dördüncü boyut kapısı bu metni kilitliyor |
| BL-043 | **KAPANDI (Faz 121)** | Webhook teslimatı at-least-once (aynı `delivery.Id` her retry'da `X-Tracon-Delivery` header'ıyla gönderiliyor, alıcı-taraflı dedup bekleniyor) ama bu `IWebhookStore`/`IWebhookPublisher`'ın XML dokümanında hiç belirtilmiyordu — yalnız `WebhookDeliveryJobHandler.cs:299`'da görülebiliyordu | ~~🟡~~ ✅ Kapandı | `WebhookDeliveryJobHandler.cs:299` | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | At-least-once garantisi ve `X-Tracon-Delivery` dedup header'ı hem `IWebhookStore` hem `IWebhookPublisher`'ın XML dokümanına eklendi |
| BL-044 | Açık | Kümede hiç `ActivitySource`/`Meter` yok (`Scheduling`, `Webhooks`, `Coordination`, `Idempotency`, `Triggers` içinde grep boş) — yalnız hata yollarında `ILogger` uyarısı var; job backlog, webhook teslim başarısızlık oranı, lease çekişmesi gibi operasyonel sinyaller `IRunStore`/`TraconMetrics` seviyesine kıyasla eksik | 🟡 1.0 blocker | Küme H raporu | `nuget-danismani` → faz zinciri | Operasyonel metric'ler eklenir |
| BL-045 | Bilgi | Küme H cila bulguları (🟢, toplu): 6 store arayüzü için `Testing.Contracts` taban sınıflarını `PackageReference` ile koşan dış sample yok (contract'lar repo içinde gerçek ve koşuluyor, yalnız dış tüketici perspektifinden kanıtlanmamış); schedule/webhook/trigger adları için ad-şekli doğrulaması yok (yalnız non-empty, DB uniqueness var) | 🟢 Doküman/cila | Küme H raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-046 | **KAPANDI (Faz 121) — dokümanın ÖTESİNDE, gerçek bir kusur olarak** | `IAuditLog`/`AuditQuery.TenantId=null` "çağıranın kiracısına düşer" davranışı yalnız DTO yorumunda anlatılıyordu, `IAuditLog` arayüzünün kendisinde bir sözleşme değildi — kayıt "bugün hiçbir shipped kod yolu bunu tetiklemiyor" diyerek riski küçümsüyordu, ama ölçüldüğünde `InMemoryAuditLog` (Tracon'in KENDİ referans implementasyonu) null/boş tenant'ta gerçekten TÜM kiracıların kaydını tarıyordu — dokümante edilen niyet hiçbir implementasyonda gerçek davranış değildi | ~~🟡~~ ✅ Kapandı (K-644) | `AuditQuery.cs:6`; `InMemoryAuditLog.cs` (eski `SnapshotAll`); `SqlAuditLog.cs` (`?? string.Empty`, sessizce boş sonuç) | **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** | `IAuditLog`/`AuditQuery`/`AuditChainQuery`'nin XML dokümanı sözleşmeyi açıkça yazdı VE `InMemoryAuditLog`/`SqlAuditLog` `SqlRunStore`'un zaten kullandığı `ITenantContext` fallback desenine hizalandı (kod düzeltmesi, yalnız doküman değil). `AuditLogContract`'a (public) 3 yeni test eklendi, `InMemory`/`PostgreSQL`/`SqlServer`/`Sqlite`'ın DÖRDÜNDE de ayrı ayrı yeşil koştu |
| BL-047 | **KAPANDI (Faz 171, 2026-09-15)** | Audit-write başarısızlığı loglanıyor (`AuditRecorder.cs:53-60`) ama dedicated metric/counter yok — production'da audit-log bozulmasını yakalamak tamamen log taramasına bağlı | 🟡 1.0 blocker | `AuditRecorder.cs`; `TraconMetrics.cs` (audit counter yok) | **[Faz 171](arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md)** | `tracon.audit.write_failures` eklendi, `tracon.audit.outcome` etiketiyle `swallowed`/`refused` ayrılıyor. Aynı fazda F-234 de kapandı: dört `WriteAuditOrThrowAsync` kopyası tek `AuditRecorder.WriteOrThrowAsync`'e indi ve `AuditWritePolicyTests` altıncı kopyayı derleme yerine **test** hatası yapıyor |
| BL-048 | **KISMEN KAPANDI (Faz 121)** | `ISpeechSynthesizer`/`ISpeechTranscriber`/`IVoicePricingReader`/`IVoiceHealthCheck` için contract test yok; custom `ISpeechSynthesizer`/`IAuditLog` için dış sample yok; ~~`IAuditActorResolver`'ın 2 satırlık XML dokümanı AsyncLocal/ambient-context bağımlılığını ve singleton lifetime etkisini hiç anlatmıyor~~ **doküman kısmı kapandı** | 🟡 1.0 blocker (kalan: contract test + dış sample — kulvar 1/4) | Küme J raporu | `nuget-danismani` → faz zinciri (kalan) · **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** (doküman ✅) | `IAuditActorResolver`'ın dokümanı `AuditActorContext`'in AsyncLocal mekanizmasını ve singleton-safe olma gerekçesini artık anlatıyor; 4 ses arayüzüne de DI lifetime (singleton) + tenant mode (TENANT-INDEPENDENT) eklendi. Contract sınıfları ve dış sample kulvar 1/4 kapsamında kalır |
| BL-049 | Bilgi | Küme J cila bulguları (🟢, toplu): `ISpeechTranscriber.TranscribeAsync`'in `Stream audio` sahipliği (kim dispose eder) arayüzde belirtilmiyor; `IAuditLog.WriteAsync`'in retry'de idempotency'si (dedup anahtarı yok) dokümante değil; voice arayüzleri abstraction seviyesinde hiç `TenantId` taşımıyor (tenant başka yerde uygulanıyor, makul ama not gerekiyor) | 🟢 Doküman/cila | Küme J raporu | `nuget-danismani` → doküman senkronu | Docs-site'a eklenir |
| BL-050 | **KISMEN KAPANDI (Faz 121)** | Küme I kayıt ergonomisi tutarsız — yalnız `IRunJudge` tam üçlü (`AddRunJudge<T>()`/instance/factory) alıyor; diğer arayüzler için hiç `AddX()` yok (kulvar 2, açık kalır); `IVectorSearchStore`/`IConversationBranchStore`/`IMigrationApplier`/`ISqlPersistenceDiagnostics` için contract test yok (kulvar 1, açık kalır); ~~singleton/thread-safety/no-per-run-state 10/11 arayüzde dokümante değil~~ **doküman kısmı kapandı**; ~~`IAttachmentStore.OpenReadAsync`/`IAttachmentStorage.ReadAsync` stream sahipliğini belirtmiyor~~ **kapandı** | 🟡 1.0 blocker (kalan: registration API + contract test — kulvar 1/2) | Küme I raporu | `nuget-danismani` → faz zinciri (kalan) · **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** (doküman ✅) | 12 üyenin (`ISessionStore`, `IConversationBranchStore`, `IAttachmentStore`, `IAttachmentStorage`, `IRetentionStore`, `IArchiveSink`, `IRetentionPolicyStore`, `IEvalStore`, `IExperimentStore`, `IMigrationApplier`, `ISqlPersistenceDiagnostics`, `IVectorSearchStore`) hepsine DI lifetime (singleton, ikisi optional) eklendi; `IAttachmentStore.OpenReadAsync`/`IAttachmentStorage.WriteAsync`/`ReadAsync` stream sahipliğini (çağıran sahiplenir ve dispose eder) açıkça yazıyor. Registration API ve contract test kulvar 1/2 kapsamında kalır |
| BL-051 | **KAPANDI (2026-08-28, Faz 122)** | Küme I cila bulgusu (🟢): retention "politika yok = sonsuza kadar sakla" dokümante ve kasıtlı bir varsayılan (Faz 25 kararı) ama `Enabled=false` + politika yoksa hiçbir başlangıç uyarısı yok — compliance için retention'a güvenen bir tüketici yanlış yapılandırmayı fark etmeyebilir | 🟢 Doküman/cila → **kapandı** | Küme I raporu | `nuget-danismani` → doküman senkronu (Faz 122 ✅) | `SilentGapWarningService`, `TraconRetentionOptions.Enabled=false` iken Production'da bir kez `Warning` düşürüyor (DB politika kontrolü yok — kasıtlı, `NonPersistentStorageWarningService`'in "veritabanı açma" kuralıyla tutarlı). Gerçek `samples/Tracon.Api`'de doğrulandı (MT-DIAG-057) |
| BL-052 | **KAPANDI (2026-08-28, Faz 123)** | **Yayın kapısı altı dış sample'ın yalnız beşini koşuyordu.** `Tracon.Samples.CustomJobHandler.Tests` Faz 120'de BL-041'in paketlenmiş-tüketici kanıtı olarak eklendi, fakat `SAMPLE_TEST_PROJECTS` listesine girmemişti. Sample'ın şekli kapıda koşan `FileRunStore.Tests` ile aynıdır (`PackageReference` + `VersionOverride`), yani dışlanma teknik bir gerekçeye dayanmıyordu. Kapı dışında `TraconSamplePackageVersion` varsayılanı `*-*` (**floating**) olduğu için bu sample exact sürüm ve izole `NUGET_PACKAGES` altında **hiç** koşmuyordu — Faz 97'de ölçülen bayat-paket tuzağının tam kapsamındaydı | ~~🟡~~ ✅ Kapandı | `scripts/release_extension_samples.py` beş proje sayıyordu, `CustomJobHandler.Tests` yoktu | `nuget-danismani` → **[Faz 123](arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md)** | Sample listeye eklendi (6/6) **ve** `validate_sample_inventory` yazıldı — `samples/Tracon.Samples.*.Tests` envanteri `SAMPLE_TEST_PROJECTS` ∪ gerekçeli `SAMPLE_TEST_EXCLUSIONS` ile TAM eşleşmezse kapı adı vererek kırılır; aynı sınıf boşluk üçüncü kez sessizce tekrarlayamaz. Kanıt: `kapi.py yayin --kuru --surum 1.0.0-preview.1` → `✅ 6 exact-version packed sample ve Native AOT smoke` |
| BL-053 | **KAPANDI** (2026-09-02, `kusur-giderme`) | Sevk edilen `RunStoreContract`'ın iki Faz 132 case'i (`Completion_overrides_the_model_provider_when_a_fallback_answered`, `Completion_leaves_the_model_provider_unchanged_when_no_override_is_given`) dış sample `Tracon.Samples.FileRunStore`'a karşı **düşüyor**: `FileRunStore.cs` `ModelId`'yi işliyor (`:88`, `:183`), `ModelProvider`'ı hiç işlemiyor. `docs-site/.../guides/write-your-own-store.md` de alandan hiç söz etmiyor — rehberi izleyen üçüncü taraf aynı hatayı yazar | 🔴 Preview blocker | `kapi.py yayin --kuru --surum 1.0.0-preview.1` → `EXIT=1`; `Failed: 2, Passed: 90, Total: 92`; exact sürüm + izole `NUGET_PACKAGES` + yalnız `PackageReference` (kanıt merdiveni 6. seviye) | `nuget-danismani` → `kusur-giderme` | **Tamamlandı.** `FileRunStore.cs:89` ve `:189` `ModelProvider`'ı işliyor (`ModelId`'nin tam kardeşi: `null` mevcut değeri korur); izole cache ile red→green kanıtlandı (90/92 → **92/92**). Rehbere yedinci davranış ekseni **Completion overrides** eklendi. **Sınıf taraması:** Faz 129–135'te yalnız İKİ sevk edilen contract büyüdü — `RunStoreContract` (sample'ı vardı, düzeltildi) ve `JobStoreContract` (hiç dış sample'ı yok → **BL-055**) |
| BL-054 | **KAPANDI** (2026-09-02) | `CHANGELOG.md`'nin `## [1.0.0-preview.1]` bölümü **2026-08-28 tarihli** ve Faz 129 öncesi ürünü anlatıyor; `## [Unreleased]` boş. Bugün yayınlansa sürüm notları iki kırıcı `IJobStore` değişikliğini (lane filtresi, `GetQueueDepthAsync`), `RunCost`/`RunRecord` alanlarını, yapısal doğrulama seam'ini ve bounded repair'i hiç anmıyor | 🔴 Preview blocker | OP-007 kapısı yalnız bölümün **boş olmadığına** bakıyor (`scripts/changelog.py:44`), bayatlığı göremez | `nuget-danismani` → `tuketici-dokuman-senkronu` | **Tamamlandı.** Dört ürün seviyesi kalem eklendi (cost provenance · named job lanes · structured-response validation + bounded repair · generated tool schema constraints/nested object); tarih 2026-09-02'ye çekildi. `changelog.py` bölümü ayrıştırıyor. 🚨 Tarih tag gününde doğrulanır |
| BL-056 | **KAPANDI** (2026-09-03, aynı tur) | 20 paketin `PackageProjectUrl` ve `PackageReleaseNotes` alanları private repo'ya bakıyordu; her ikisi de tüketici için **HTTP 404** | 🔴 Preview blocker | Anonim `curl`: repo kökü ve `blob/v1.0.0-preview.1/CHANGELOG.md` → 404; doküman sitesi → 200. Adım 7 filtresinin altı sorusunu geçti (ölçüldü · artifact'ten yeniden üretilebilir · tüketici bugün yaşar · kapıyla kilitlenebilir) | `nuget-danismani` | **Tamamlandı.** İki alan `tracon.dev`'ye çevrildi (K-659); release-notes hedefi kök `CHANGELOG.md`'den üretilen `/reference/changelog/#v<sürüm>` sayfasıdır. `RepositoryUrl` bilinçli olarak repo'da kaldı — provenance alanıdır, paket kapısı `<repository commit>` ister ve bir doküman sitesi git repo'su değildir (RK-013) |
| BL-057 | **Açık** (2026-09-23) | nuget.org `Tracon` organizasyonunda ikinci bir üye yok. Repo'da ikinci üyenin kaydı yoktur; üye listesi repo'dan ölçülemez. Tek bakımcı erişilemezse 20 paketin hiçbirine güvenlik yaması yayınlanamaz (RK-012) | 🔴 GA blocker | RK-012 · OP-001 (K-755) · `git shortlog -sne HEAD` → 1145 commit'in tek yazarı | 👤 Bakımcı eylemi (nuget.org) | Organizasyonun üye listesinde ikinci bir kişi görünür ve o kişi paket yönetim yetkisini doğrular |
| BL-058 | **Kapandı** (2026-09-24, `de52ecdf`; UR-008 B) | `Tracon.UI` derlemesine gömülü JS bundle'ı üçüncü taraf lisans bildirimi taşımıyor ve pakette notice dosyası yok (A-60). MIT bildirimin kopyayla gitmesini ister | 🔴 GA blocker | Bundle brotli ile açıldı: 748.550 B, `@license`/`Copyright`/`MIT License` **0**; `unzip -l Tracon.UI.*.nupkg` notice dosyası göstermez | `kusur-giderme` | Paket bir notice dosyası taşır; içeriği `frontend/package-lock.json` runtime bağımlılıklarından üretilir ve bir kapı eksikliğini kırmızı sayar |
| BL-055 | **Açık** | `JobStoreContract` Faz 129 ve 133'te **+110 satır** case kazandı (lane filtresi, `GetQueueDepthAsync`) ama **hiçbir dış sample'ı yoktur** — `CustomJobHandler` `JobHandlerContract`'ı koşar, `JobStoreContract`'ı değil. Yeni case'ler paketlenmiş tüketiciye karşı hiç doğrulanmadı | 🟡 1.0 blocker | `grep -rn JobStoreContract samples/` boş; `git diff --stat` Faz 129–135 aralığında yalnız iki contract dosyası değişti | `nuget-danismani` → faz zinciri | `IJobStore` uygulayan bir dış sample eklenir ve `JobStoreContract`'ı exact sürümle koşar. BL-007/BL-017/BL-030 ile aynı aile |


## 7. Ürün ve public API kararları

| Kimlik | Konu | Durum | Bulgu veya soru | Ölçülen kanıt | Seçenekler | Alınan karar | Gerekçe | Risk | Workflow | Doğrulama | Tarih |
|---|---|---|---|---|---|---|---|---|---|---|---|
| UR-001 | Yayın türü | Tamamlandı | İlk dış yayın preview, rc veya stable mı olmalı? | KN-005 ve pre-release Hosting bağımlılıkları | `preview` / `rc` / `stable 1.0` | `preview` | Shipped baseline boş (0 giriş, 676 unshipped tip) ve `Tracon.AspNetCore` pre-release MAF bağımlılığı taşıyor — stable/RC taahhüdü bugün karşılanamaz; preview SemVer'de kırıcı değişikliğe izin verir | Yüksek | `nuget-danismani` | Kullanıcı kararı | 2026-08-27 |
| UR-002 | İlk hedef tüketici | Tamamlandı | Ürün anlatısı ve en küçük paket kapsamı hangi birincil persona için optimize edilmeli? | Repo bir control plane, provider, storage, transport, UI ve extension paketleri taşıyor | Yalnız çekirdek / tam entegrasyon seti / çekirdek + kanıtlanmış alt küme | Tam entegrasyon seti (20 paketin tamamı) | Kullanıcı, dry-run'da zaten kanıtlanmış tam kapsamı (20 paket, 160 sample testi, AOT smoke yeşil) korumayı seçti | Yüksek — audit yükü en geniş seçenek düzeyinde | `nuget-danismani` | Kullanıcı kararı | 2026-08-27 |
| UR-003 | Public API freeze | **Tamamlandı — GA'ya ertelendi** | Preview öncesinde hangi yüzey korunmalı veya küçültülmeli? | Ölçüldü 2026-08-27: 17 `PublicAPI.Shipped.txt` **0** satır, unshipped **8417** satır / **680** tip | Preview'dan önce tara / **GA turuna ertele** | **GA turuna ertelendi; seviye 🔴 → 🟡** | Sevk edilen `versioning.md:11-13` preview hattında yüzey daralmasının kırıcı sayılmadığını zaten söylüyor; K-603 `Shipped` dolumunu GA'ya erteledi; K-602 tek sürüm hattını bu gerekçeyle seçti. Faz 96 aynı erişilebilirlik ölçütünü koşup 96 tipi `internal` yapmıştı (K-601) — ikinci turun verimi düşük | Orta — GA'da yüzey büyükse daraltma pahalılaşır; azaltım: tarama `Unshipped → Shipped` dolumuyla aynı turda koşar | `nuget-danismani` (GA turu) | GA kapısında `Shipped.txt` dolumu | 2026-08-27 |
| UR-004 | 1.0 yolu | **Açık — kullanıcı kararı bekliyor** | Upstream kararlı olmadan 1.0'a nasıl yaklaşılır? | ER-001 · §4 GÜNCEL KARAR 2026-09-24 (Hosting ailesi ve `A2A.AspNetCore` kararlı değil) | A: upstream GA'yı bekle · B: Hosting'e bağlı özellikleri ayrı ön sürüm pakete ayır (K-602'nin reddettiği ayrık hattın türevi; yeniden açma ister) · C: yüzeyi dondur, `1.0.0-rc.N` çık, GA upstream'e bağlı | — | Öneri **C**: RC ön sürümdür, `NU5104` durdurmaz; yüzey sahada donar, GA aynı yüzeyle gelir | Orta | `nuget-danismani` | RC provası `kapi.py yayin --kuru --surum 1.0.0-rc.1` yeşil | 2026-09-24 |
| UR-005 | Donacak yüzey | **Açık — kullanıcı kararı bekliyor** | ~650 tipin hepsi mi donar, yoksa olgun olmayan kısım ayrı bir katmanda mı kalır? | A-63 · UR-003 · K-850 | A: hepsini dondur · B: kararlı çekirdek + Tracon'un kendi `[Experimental]` katmanı | — | Öneri **B**; önce K-869 emsaliyle STJ kaynak üretecine etkisi ölçülür | Orta | `faz-planlama` | Katmana giren her üye derleyici uyarısı üretir; `Shipped`'e girmez | 2026-09-24 |
| UR-006 | Ön sürüm upstream aralığı | **Tamamlandı — A (K-872)** | Ön sürüm bağımlılık alt sınır mı, tam sürüm mü yazılır? | A-59 | A: tam sürüm (`[x]`) — uyumsuzluk restore'da görünür · B: açık alt sınır + doküman | **A** (2026-09-24, UR-008 B ile) | Öneri **A**: kırılma çalışma anı yerine restore'da görünür; bedeli tüketicinin Hosting'i bağımsız yükseltememesidir, ki zaten uyumsuzdur | Orta | `kusur-giderme` | `Tracon.AspNetCore` nuspec'i beş ön sürüm bağımlılığı için `[x]` yazar | 2026-09-24 |
| UR-007 | 1.x destek penceresi | **Açık — kullanıcı kararı bekliyor** | Hangi 1.x sürümü ne kadar güvenlik yaması alır? | `SECURITY.md` yalnız preview sözü taşır | A: yalnız son minor · B: son minor + yeni major çıkınca önceki major N ay güvenlik yaması | — | Öneri **B**; N kullanıcı kararıdır | Orta | `tuketici-dokuman-senkronu` | `SECURITY.md` ve site politikası aynı tabloyu taşır | 2026-09-24 |
| UR-008 | `preview.3` kapsamı | **Tamamlandı — B** 👤 | preview.3 bugünkü içerikle mi çıkar, yoksa önce A-59 ve BL-058 mi kapanır? | KN-027 · KN-028 · 13 `Security` notu · A-64 (push, düzeltmeleri public yapar) | A: bugünkü içerikle çık; A-59 geçici çözümü sürüm notuna yazılır · B: önce UR-006 A (tam sürüm sınırı) + BL-058 (lisans bildirimi), ikisi `kusur-giderme` · C: önce MAF `1.22` / Hosting `1.22` yükseltmesi (A-62 + taşınan tip) | **B** 👤 (2026-09-24) | Öneri **B**: iki küçük iş preview.3'ün taşıyacağı iki bilinen kusuru kapatır; `main` itilmediği için güvenlik düzeltmeleri bu sürede public olmaz. C upstream ön sürüm çalkantısını güvenlik sürümünün kritik yoluna sokar | Orta | `kusur-giderme` | preview.3 nuspec'i `[x]` yazar; `Tracon.UI` paketi notice dosyası taşır | 2026-09-24 |

## 8. Operasyonel yayın kararları

| Kimlik | Konu | Durum | Mevcut kanıt | Karar |
|---|---|---|---|---|
| OP-001 | NuGet owner modeli | **Değişti (2026-09-12)** | Repo kanıtı yok | **Organizasyon `Tracon`** — kullanıcı kararı 2026-09-12, K-755. **(yeniden açıldı: 2026-09-12, ID prefix reservation kriteri 1)** — önceki karar kişisel hesap `farukatasoy` idi (2026-08-27); ön ek rezervasyonu "ön ek sahibi açıkça tanımlıyor mu?" diye sorar ve `farukatasoy` ile `Tracon` arasında görünür bağ yoktu. Organizasyon bunu yapısal olarak çözer ve RK-012'nin tek-bakımcı devir riskini de azaltır |
| OP-002 | **Tamamlandı (2026-08-28, KN-022)** | NuGet.org hesabı ve zorunlu Microsoft-account 2FA sınırı authenticated trusted-publishing policy oluşturma akışında geçildi | Kişisel hesap `farukatasoy` |
| OP-003 | Package ID sahipliği/uygunluğu | **Ölçüldü — 20/20 müsait** | 2026-09-12 (Faz 162 sonrası, YENİ adla): `registration5-semver1` GET × 20 → hepsi `404`; `packageid:Tracon` araması `totalHits: 0`. npm `@tracon/client` → `404` (yayınlanmadı). npm org `tracon` **alındı** (2026-09-12, `npm org ls tracon` → `hfarukatasoy - owner`); anonim `/-/org/tracon` isteği `404` döner ama bu kanıt değildir — o uç kimlik doğrulaması ister | Kimlik çakışması yok. `Tracon.*` ID prefix reservation başvurusu ayrı bir kalem (M1): `account@nuget.org`'a e-posta, ilk preview yayınlandıktan sonra |
| OP-004 | **Tamamlandı (2026-08-28, KN-022)** | Kalıcı `NUGET_API_KEY` kaldırıldı | GitHub OIDC üzerinden NuGet.org bir saatlik geçici API key üretir; repo secret yok |
| OP-005 | **YENİDEN YAPILACAK (2026-09-12)** | 2026-08-28'de KİŞİSEL owner `farukatasoy` altında bir policy oluşturuldu; bugün üç sebeple geçersizdir | 🚨 (1) Policy **sahibi** kişisel hesaptır; bir policy yalnız KENDİ sahibinin paketlerine uygulanır, paketler artık `Tracon` organizasyonuna ait olacak. (2) Policy'nin nuget.org'daki gerçek alanları hâlâ ESKİ adı taşır (repo adı ve `AgentPrism*` pattern'i) — Faz 162 bu dokümanı yeniden yazdı ama **nuget.org'a ulaşamaz**. (3) Private repo policy'si yedi gün geçici aktifti; 2026-08-28'den beri hiç publish olmadığı için pencere doldu. **Yapılacak:** `Tracon` organizasyonu sahipliğinde YENİ policy — repo `farukatasoy/Tracon`, workflow `ci.yml`, environment `nuget`, pattern `Tracon*`, push-only. CI `user:` değeri `Tracon` olarak güncellendi |
| OP-006 | Tag ve GitHub release | **Tamamlandı (2026-08-28, Faz 123)** | Her `v*` tag'i gerçek NuGet ve npm publish tetikler. Ölçüldü 2026-08-27: CI hiç GitHub release üretmiyordu | `ci.yml`'e `github-release` işi eklendi (`needs: [publish, npm-publish]`, yalnız `v*` etiketinde koşar). Gövde `CHANGELOG.md`'nin o sürüme ait bölümü (`scripts/changelog.py` — OP-007 ile aynı ayrıştırıcı); aynı etiket yeniden itilirse `gh release view` var olan release'i bulur ve oluşturma adımı atlanır |
| OP-007 | Release notes | **Tamamlandı (2026-08-28, Faz 123)** | Ölçüldü 2026-08-27: hiçbir artifact yoktu — `CHANGELOG.md` yok, `PackageReleaseNotes` hiçbir `Directory.Build.props`'ta tanımlı değil, `docs-site`'ta changelog sayfası yok. 20 paket sayfası boş release-notes alanıyla çıkıyordu | Kök `CHANGELOG.md` eklendi (Keep a Changelog); `src/Directory.Build.props` her pakete sürüme çapalı `PackageReleaseNotes` URL'i veriyor (`BeforeTargets="GenerateNuspec"` bir hedef içinde atanır — ölçüldü: düz bir `PropertyGroup`'ta `$(Version)` MinVer'in kendi hedefinden ÖNCE boş okunuyordu); `kapi.py yayin` zorlanan sürüm için CHANGELOG'da `[<sürüm>]` bölümünü fail-closed arıyor; `docs-site/reference/versioning.md` köke bağlanıyor (ayna sayfa yok). Kanıt: 20/20 `.nuspec` çözümlenmiş URL taşıyor |
| OP-008 | Deprecation/yank/hotfix | **Tamamlandı (2026-09-03)** | Repo politikası yoktu | **Yalnız ileri sürüm** — kullanıcı kararı. Yayınlanan sürüm unlist veya yank edilmez; düzeltme `preview.2` ile gelir. Yalnız güvenlik veya veri bütünlüğü kusurunda paket NuGet.org'da deprecate edilir ve düzeltilmiş sürüme yönlendirilir. NuGet.org'un kalıcı artifact mantığıyla tutarlıdır; `SECURITY.md` aynı sözü yazıyor |
| OP-009 | Dependency/vulnerability takibi | **Tamamlandı (2026-09-03)** | **Ölçüldü:** `.github/dependabot.yml` **dört** ekosistemi kapsıyor (NuGet haftalık · `Tracon.UI/frontend` npm haftalık · `docs-site` npm haftalık · github-actions aylık) ve `Directory.Build.props:23` `TreatWarningsAsErrors=true` altında NuGet Audit'in `NU1903`'ü restore'u zaten kırıyor | **Ek kapı eklenmedi** — kullanıcı kararı. Mevcut iki mekanizma yeterli sayıldı; ayrı bir zamanlanmış `dotnet list package --vulnerable` işi eklenmedi |
| OP-010 | npm/NuGet asimetrik kısmi yayın | **Tamamlandı (2026-08-28, KG-021/KN-021)** | Aynı `v*` tag'i `publish` (o gün `nuget-publish`) ve `npm-publish` işlerini ([`ci.yml`](../.github/workflows/ci.yml)) **paralel** tetikliyordu (farklı `needs`). `tracon` npm scope'u bugün yok; `NPM_TOKEN` durumu repo dışında. Scope hazır değilse 20 NuGet paketi **kalıcı** yayınlanır, npm işi kırılır — ve sevk edilen doküman `npm install @tracon/client` diyor (`docs-site/src/content/docs/packages.md:70`, `guides/typescript-client.md:25`) | **Yol A + C.** **C uygulandı:** `publish` işi `needs: [release-dryrun, npm-publish]` taşır (Faz 191 `pack`'i düşürdü; [`ci.yml`](../.github/workflows/ci.yml)) — geri dönüşü olmayan kanal (NuGet) EN SON basar; npm kırılırsa 20 paket hiç yayınlanmaz. Zincir kırılmaz: `npm-publish` var olan sürümü atlar, aynı etiket yeniden itilebilir. **A tamamlandı (KN-021):** npm scope ve `NPM_TOKEN` hazırlığını kullanıcı doğruladı; token yetkisi ilk publish işinde ölçülecek |
| OP-011 | GitHub Free/private repo CI koruması | **Tamamlandı (2026-09-03)** | KN-020: GitHub Free private repo'da environment secret, required reviewer ve deployment tag restriction yok. **Ölçüldü 2026-09-03:** CI'da kalıcı secret **tek**tir — `NPM_TOKEN` ([`ci.yml:405`](../.github/workflows/ci.yml#L405)); NuGet tarafı OIDC trusted publishing kullanır ve secret taşımaz (OP-004/005) | **A — GitHub Free'de kal** (kullanıcı kararı). Tek bakımcı riski kabul edilir; tag öncesi §10 checklist'i elle uygulanır. Not: repo public yapılırsa environment protection ve deployment tag restriction Free planda zaten gelir — bu, K-659'un yeniden açılma ölçütüyle aynı kapıdır |

## 9. Risk kaydı

| Kimlik | Durum | Risk | Seviye | Olasılık / etki | Azaltım | Sorumlu workflow |
|---|---|---|---|---|---|---|
| RK-001 | İnceleniyor | İlk `v*` tag'inin NuGet ve npm'e kalıcı yayın tetiklemesi | Yüksek | Orta / yüksek | Tag öncesi exact dry-run, environment protection ve credential doğrulaması | Yayın operasyonu |
| RK-002 | **Preview için kabul edildi (UR-002/KG-019)** | 20 paketlik ilk yayın, gereksiz public yüzeyi ve support yükünü aynı anda kalıcılaştırabilir | Yüksek | Orta / yüksek | Tam entegrasyon seti kullanıcı kararıdır; public API freeze GA'ya ertelendi. Preview geri bildirimi GA yüzeyini daraltmak için kullanılır | `nuget-danismani` |
| RK-003 | **🟡 olarak kabul edildi; GA kapısı açık** | Shipped baseline boşken preview tüketicileri kırıcı değişiklik yaşayabilir | Orta | Yüksek / orta | Preview compatibility politikası ve release notes sevk edildi; `Shipped.txt` dolumu ve freeze taraması GA kapısıdır | `nuget-danismani` + doküman senkronu |
| RK-004 | **Kapandı (KN-022)** | Uzun ömürlü `NUGET_API_KEY` scope, süre ve supply-chain riski | ~~Yüksek~~ | Kalıcı key kaldırıldı | GitHub OIDC + NuGet.org trusted publishing bir saatlik, tek kullanımlık geçici key üretir | Yayın operasyonu |
| RK-005 | **Kapandı (2026-08-28)** | K-602'nin “19 paket” sayısı güncel 20 paketle drift gösteriyordu | ~~Orta~~ | Kesin / düşük-orta | Sayı **güncellenmedi, kaldırıldı** — kararın özü sayıya bağlı değil (“paketlenen projelerin hepsi `1.0.0-preview.N` olarak çıkar”). `KARARLAR.md` ve `KARARLAR-INDEKS.md` düzeltildi; 20→21 olduğunda tekrar drift etmez. K-129 ve K-542'deki “19” tarihsel anlatıdır, canlı iddia değil — dokunulmadı | Karar defteri kuralları |
| RK-006 | **Kapandı (2026-09-19)** | Meta paketin boş symbol package'i ve CLI'ın 28 MB paketi kapıdan geçiyor, fakat kapı içerik uygunluğunu yargılamıyor | ~~Orta~~ | İkisi de ölçüldü | **Meta paket artık PDB'siz `.snupkg` üretmiyor** — 20 pakete 18 sembol paketi düşer, eksik ikisi `Tracon` (assembly yok) ve `Tracon.Templates` (içerik paketi); ikisi de doğru. **CLI 28 MB açıklandı:** çok-RID yerel SQLite + `Microsoft.Data.SqlClient` win/unix; `dotnet tool` üç sağlayıcıya da migration koşar, boyut kusur değil | `nuget-danismani` |
| RK-007 | **Kapandı (BL-006/K-639)** | `ITenantProviderBindingStore` case-sensitivity tutarsızlığı BYOK credential'ının sessizce global setup credential'ına düşmesine yol açabilirdi | ~~Yüksek~~ | Ölçülen vaka kapandı | Canonical provider-name normalizasyonu, üç SQL migration'ı ve dört implementasyonda contract testleri eklendi | `kusur-giderme` |
| RK-008 | **🟡'ye indirildi (BL-026)** | Drain state kendi başına tam reservation garantisi vermez | Orta | İş kaybı yeniden üretilemedi; Kestrel request draining ve job wait iki bağımsız yedek mekanizma sağlar | Sınır XML dokümanında açıklandı; ölçülmüş iş-kaybı repro'su çıkarsa yeniden açılır | GA turu |
| RK-009 | **Kapandı (Faz 119/K-640)** | Ham exception mesajı sızıntısı kalıcı ve dışa açık yüzeylerde bir kusur sınıfıydı | ~~Yüksek~~ | 26 vaka kapatıldı | `SafeErrorText` tüm 26 siteye uygulandı; `RawExceptionTextSiteTests` yeni sızıntıları fail-closed yakalar | `kusur-giderme` |
| RK-010 | **Kapandı (Faz 120)** | `IJobHandler`'ın sözleşmesi at-least-once'ı söylemiyordu (BL-041) — dokümante edilen örneği izleyen bir tüketici crash/retry'de side effect'i iki kez çalıştırabilirdi | ~~Yüksek~~ | Orta (lease kaybı/retry production'da olağan) / yüksek (dokümante edilen doğrudan örnek yanlış) | `IJobHandler.cs`'nin XML dokümanına at-least-once uyarısı ve süzülmemiş `Items` notu eklendi; `JobHandlerContract` kuralı kilitliyor, `JobLeaseExpiryTests` davranışı ölçüyor. `IIdempotencyStore`'u job loop'una bağlamak değerlendirilmedi — BL-041'in kapanış notunun gerekçesiyle gereksiz ikinci bir mekanizma olurdu | `nuget-danismani` → `kusur-giderme` → [Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) |
| RK-011 | **Kapandı (KG-021/KN-021)** | NuGet ve npm kanallarının paralel basılması kalıcı asimetrik yayın üretebilirdi | ~~Yüksek~~ | Yapısal risk kapandı; npm credential yetkisi ilk publish işinde ölçülecek | `publish` npm işine bağlandı; kullanıcı npm scope ve `NPM_TOKEN` hazırlığını doğruladı | Yayın operasyonu |
| RK-013 | **KG-028 ile kapanma yolunda** (2026-09-03'te kabul edilmişti, K-659) | `RepositoryUrl` private bir repo'yu gösterir: üçüncü taraf için Source Link kaynak çözemez ve `.snupkg` sembolleri kaynak adımlamasına açılmaz | Orta | Kesin / düşük-orta | Tüketiciye dönük iki URL siteye çevrildi, yani okura sunulan hiçbir bağlantı ölü değil. Sembol paketleri yine yayımlanır (yığın izi satır numarası taşır). Repo public yapılırsa kendiliğinden çözülür | `nuget-danismani` |
| RK-012 | **Kısmen azaltıldı (2026-09-12, OP-001/K-755); GA blocker BL-057 açık** | Tek bakımcı riski. 20 paketin sahibi artık nuget.org `Tracon` organizasyonudur (OP-001), yani devir paket başına elle yapılmaz. Ama organizasyonda ikinci bir üyenin kaydı yoktur: bakımcı erişilemezse hiçbir güvenlik yaması yayınlanamaz. 2026-08-27'deki "kişisel owner" kabulü artık geçerli değildir | Orta | Düşük / yüksek | Organizasyon sahipliği (yapıldı) · 2FA (OP-002) · organizasyona ikinci üye — GA blocker BL-057, 👤 bakımcı eylemi | Yayın operasyonu |
| RK-014 | **Kapandı (2026-09-19, KG-030/031)** | Repo public yapıldığında **1065 commit'lik geçmiş** de public olur. Mevcut secret kapısı ([`kapi.py:183-209`](../scripts/kapi.py)) yalnız çalışma ağacını yürür ve `git log`'a hiç bakmaz; bugün temiz olan bir dosya geçmişte bir credential ile commit edilmiş olabilir | 🔴 Public öncesi | Bilinmiyor / **geri dönüşsüz** — public olduktan sonra geçmişi temizlemek pratikte imkânsızdır (fork, cache ve GitHub nesneleri kalır) | Koşuldu: `gitleaks git .` 1065 commit / 53 MB — gerçek credential sıfır, rotate gerekmedi. Kapının iki yapısal boşluğu (desen + kapsam) aynı turda kapatıldı ve mutasyonla doğrulandı (KG-031). 🚨 **Kalan kabul:** redakte edilen `ap_default_*` anahtarı `fc1998c9`'dan itibaren **geçmişte durmaya devam eder** — yerel loopback DB'sine bağlıdır, dışarıdan erişilebilir bir yüzeyi yoktur; hâlâ ayakta bir yerel örnekte kaydı varsa silinir | Kullanıcı + `nuget-danismani` |
| RK-015 | **Azaltıldı** (2026-09-24, K-872) | Upstream kırılması Tracon'un yayınlanmış ikilisine çalışma anında ulaşır: nuspec upstream'e alt sınır yazar, tüketici daha yeni upstream alabilir (A-59 ölçülmüş vaka) | Orta | Orta / yüksek | Ön sürüm yarısı kapandı: preview.3'ten itibaren ön sürüm üst akış tam aralıktır, kırılma restore'da görünür. Kalan: kararlı üst akış alt sınırda kalır (bilinçli, K-872) — `uyum-probu.cs ileri` her upstream sürümünde ve etiketten önce koşar. preview.1/preview.2 açık aralıkla kalıcıdır | `nuget-danismani` |

## 10. Yayın checklist'i

Bu checklist hem `preview.1` hem GA sertleştirme envanterini taşır. §13'te
GA'ya ertelendiği açıkça yazılan teknik `[ ]` kalemler, ilk preview'ın hesap ve
operasyon kritik yolunu yeniden açmaz.

### Ürün ve artifact

- [x] Hedef yayın türü kullanıcı tarafından onaylandı: `preview` (UR-001).
- [x] Sürüm `1.0.0-preview.1` kullanıcı tarafından onaylandı (KG-029); tag adı `v1.0.0-preview.1`.
- [x] Tam manuel kabul turu (36 aile) koşuldu ve bulduğu kusurlar kapandı (KG-027, 2026-09-18/19): 1.859 case — 1.813 Geçti · 26 Beklemede · 19 Atlandı · 1 Kaldı; 48 kusur kaydının hepsi kapandı. **Tek istisna `MT-MM-121` / K-835** — `Tracon.Google` görsel yolu, düzeltme kullanıcı kararına bırakıldı; aşağıdaki açık kalem odur.
- [x] **K-835 kapandı (KG-034 seçenek A 👤, KG-035):** `Tracon.Google`'ın görsel yolu `GenerateContentAsync`'e geçirildi ve `Count`/`MediaType` sözleşmesi birlikte yeniden yazıldı; site ve XML aynı turda gerçeğe çekildi. Sevk edilen doküman ile runtime arasında bu kalemde çelişki kalmadı.
- [x] `CHANGELOG.md` sevk edilen davranışı doğru anlatıyor (K-658 eklendi) ve tarihi güncel (2026-09-03 — **tag gününde yeniden doğrulanır**).
- [x] En küçük güvenli paket kümesi onaylandı: tam 20 paket (UR-002).
- [x] Exact sürümlü temiz pack başarılı.
- [x] Üretilen paket kimlik kümesi beklenen kümeyle aynı.
- [x] Yerel feed ve izole `NUGET_PACKAGES` ile altı external consumer restore/build/test başarılı (Faz 123, BL-052).
- [x] Mevcut **altı** packed extension sample'ın contract testleri başarılı (Faz 123, BL-052).
- [x] Paketlenmiş tüketicinin gerçek runtime/AOT `run`'ı başarılı (provider, agent source ve generated tool smoke; KN-013/018).
- [x] Mevcut extension Native AOT smoke publish ve run başarılı.
- [x] Kapının beklediği her TFM assembly ve XML documentation dosyası artifact içinde mevcut.
- [x] `.nuspec` pre-release dependency sınırı doğrulandı.
- [x] Paket README, icon, license, repository ve project URL varlığı doğrulandı.
- [x] Tüketiciye dönük paket URL'lerinin gerçekten **çözüldüğü** ölçüldü (BL-056/K-659) — `PackageProjectUrl` ve `PackageReleaseNotes` doküman sitesine bakar.
- [x] `.snupkg` envanteri ölçüldü: 18 sembol paketi sevk edildi. **RK-013 kendiliğinden kapandı** — repo public yapıldı (`visibility: public`) ve `RepositoryUrl` `github.com/farukatasoy/Tracon`'a bakıyor, ∴ Source Link üçüncü taraf için **çözer**. Kabul edilen risk artık yok.
- [x] CI zinciri yeniden derlemez — itilen bayt = test edilen derleme (Faz 191, K-871): build işinin ubuntu bacağı `--no-build` ile paketler, `release-dryrun` o dosyaları paketlemeden doğrular, `publish` SHA-256 manifest'ini yeniden sınayıp aynı baytları iter. Bayt eşitliği her push'ta (PR dahil) sınanır.
- [ ] Runner'lar arası / üçüncü taraf yeniden üretim (reproducible build) ölçüldü — GA.
- [x] Package validation sonucu incelendi — **Faz 187 (2026-09-24):** her pack strict mode'la (TFM'ler arası aynı yüzey) doğrular; `kapi.py yayin` son `v*` etiketine karşı taban doğrulaması koşar ve kırılan her public tip ile düşen her TFM sürüm notunda adıyla geçmezse kırmızıdır (`publish` koşmaz, K-604). İlk koşum: taban `v1.0.0-preview.2`, 95 tip, 10 paket, hepsi `[Unreleased]`'da.

### Public contract ve güvenlik

- [x] 22 sütunlu seam matrisi tamamlandı: 11/11 küme, 78 seam (BL-003).
- [ ] Her public yüzey için freeze kararı verilmedi; kullanıcı kararıyla GA turuna ertelendi (UR-003).
- [ ] Her değişiklik SemVer etkisiyle sınıflandırıldı.
- [ ] Tenant isolation ve BYOK fail-closed davranışı artifact üzerinden ölçüldü.
- [ ] `secret`, connection string, exception, log ve persistence sızıntı probe'ları tamamlandı.
- [ ] HTTP, SSE, MCP, A2A ve OpenAI-compatible transport sözleşmeleri ölçüldü.
- [ ] Authorization, approval, egress, content guard ve output sınırları ölçüldü.
- [ ] Timeout ile caller cancellation ayrımı ölçüldü.
- [ ] Retry ve circuit breaker sözleşmeleri ölçüldü.
- [ ] Canonical serialization ve tool sonucu normalizasyonu ölçüldü.
- [ ] AOT ve trimming iddiaları doğrulandı.

### Dokümantasyon

- [x] XML documentation ile runtime davranışı hizalı (KG-022; `CapabilityExampleTests` + `SeamContractDocumentationTests`).
- [x] Package README ile root README hizalı (KG-022 — kök README'ye eksik 20. paket eklendi).
- [x] `docs-site/`, extension sample'ları ve API reference hizalı (KG-022; `npm run check` yeşil).
- [x] Compatibility ve versioning belgeleri yayın politikasını doğru anlatıyor (KG-022 — 20 paket, tek sürüm hattı, CHANGELOG bağlantısı; AOT iddiaları **güvenli yönde**: hiçbir paket `.csproj` `false` derken doküman `Yes` demiyor).
- [x] Package description ve tags güncel (KG-022 — iki `Description` düzeltildi).
- [x] Release notes hazır ve artifact kümesini doğru anlatıyor (OP-007 + KG-022 — CHANGELOG'un ekran sayısı düzeltildi).

### NuGet.org ve yayın operasyonu

- [x] NuGet.org hesabı doğrulandı (KN-022).
- [x] **Trusted publishing policy kuruldu ve KULLANILDI (OP-005).** `Tracon` org sahipliğinde, glob `Tracon*` (20/20 kimlik eşleşiyor, ölçüldü), scope *"Push new packages and package versions"*. 🚨 `user:` alanı policy'nin **oluşturanıdır** (`farukatasoy`), sahibi değil — A-33. Aşağıdaki 2026-08-28 metni tarihsel bağlamdır: 2026-08-28'de kurulan policy bugün geçersizdir: sahibi kişisel hesaptır, alanları eski adı taşır ve private-repo penceresi dolmuştur. Bir policy yalnız KENDİ sahibinin paketlerine uygulanır. Policy `preview.1`'den önce kurulmazsa `publish` işi hata verir ve npm tek başına yayınlanır (F-11). Kurulum **repo public yapıldıktan sonra** olmalı — o zaman yedi günlük pending penceresi hiç açılmaz. Policy'nin **"yeni paket yayınlama" scope'unu** taşıdığı ayrıca doğrulanmalı: ilk yayın 20 YENİ kimliktir.
- [x] NuGet.org'un zorunlu Microsoft-account 2FA sınırı authenticated policy oluşturma akışında geçildi (OP-002/KN-022).
- [x] Owner modeli seçildi: **organizasyon `Tracon`** (OP-001, 2026-09-12, K-755 — önceki kişisel hesap kararı yeniden açıldı ve değişti).
- [x] 20 Package ID'nin uygunluğu ölçüldü: 20/20 müsait (OP-003). Sahiplik modeli organizasyondur (OP-001).
- [ ] **GA:** nuget.org `Tracon` organizasyonuna ikinci bir üye eklendi (BL-057 · RK-012, 👤 bakımcı eylemi).
- [x] npm organization, npm 2FA, CI publish token ve GitHub repository `NPM_TOKEN` secret kullanıcı tarafından doğrulandı (KN-021).
- [x] Publishing authentication onaylandı: secret'sız OIDC trusted publishing, bir saatlik geçici key (OP-004/005, KN-022).
- [x] CI environment protection ve yayın yetkilendirmesi kararı verildi (OP-011 seçenek A; tek kalıcı secret `NPM_TOKEN`, NuGet tarafı OIDC).
- [x] License expression, repository URL, project URL, icon, README, authors, owners, tags ve description artifact'te doğrulandı (KN-003/018, KG-022).
- [x] Tag stratejisi ve GitHub release akışı uygulandı ve doğrulandı (OP-006/007).
- [x] Deprecation/yank yaklaşımı onaylandı (OP-008: yalnız ileri sürüm).
- [x] Bozuk release için hotfix ve geri dönüş planı onaylandı (OP-008 ile aynı karar; düzeltme yeni sürümle).
- [x] Dependency ve vulnerability izleme sorumluluğu onaylandı (OP-009).
- [x] İlk 72 saat gözlem ve destek sorumluluğu onaylandı (§14; `SECURITY.md` + issue şablonları eklendi).
- [x] Düzeltme sonrası dört kapı yeşil koştu (kapanış 10/10 · E2E 57/57 · yayın provası 20 paket + AOT).
- [x] Public öncesi `git` geçmişi secret taraması koşuldu (RK-014, 2026-09-19): 1065 commit, gerçek credential sıfır.
- [x] Repo public yapıldı ve `git push origin main` tamamlandı (KG-028); tag `origin`'e atıldı.
- [x] Gerçek yayın için kullanıcıdan açık onay alındı 👤 ve `environment: npm` / `environment: nuget` onayları verildi (`required_reviewers: farukatasoy`).

## 11. Karar günlüğü

KG-001…KG-039 (2026-08-27…2026-09-20; hepsi kapanmış) [`arsiv/YAYIN-HAZIRLIK-KARAR-GUNLUGU.md`](arsiv/YAYIN-HAZIRLIK-KARAR-GUNLUGU.md)'ye taşındı (2026-10-13, doküman bütçesi). Kimlikler değişmez; aramak için `grep -rn "KG-0NN" docs/`.

| Kimlik | Tarih | Durum | Karar | Gerekçe | Doğrulama |
|---|---|---|---|---|---|
| KG-040 | 2026-09-24 | Tamamlandı | **1.0 olgunluk denetimi: ❌ 1.0 bugün çıkamaz; A-59…A-70, BL-058, RK-015 ve UR-004…UR-007 açıldı.** `nuget-danismani` GA moduyla genişletildi (on dört sözleşme yüzeyi, on bir mercek, GA karnesi) ve ölçüm aracı `uyum-probu.cs` eklendi | Upstream'in yeni sürümüne karşı ve tetiklenen öncüllere karşı hiçbir ölçüm yoktu; iki ölçülmüş kırılma (A-59) ve beş kırmızı bot PR'ı (A-62) bunu gösterdi | §4 GÜNCEL KARAR — 2026-09-24; aracın negatif kontrolü MAF `1.13.0`'a karşı kırılma raporladı |
| KG-041 | 2026-09-24 | Tamamlandı | **`preview.3` yayın kararı: ✅ yayınlanabilir, ürün 🔴'si yok; etiket A-64 kapanmadan atılmaz.** A-71 açıldı; A-59 ve A-61 preview.3 kanıtıyla güncellendi; UR-008 açıldı | Prova HEAD'den çıkış `0` (KN-027). Açık 🔴'ler (BL-057, BL-058) GA blocker'ıdır ve preview.2'de de vardı; preview.3 onları kötüleştirmez, 13 güvenlik düzeltmesi getirir. Koşul A-64'tür: Faz 187–191'in CI zinciri GitHub'da hiç koşmadı (A-29 emsali) | `main` CI yeşil · kesim commit'i · etiket sonrası `MT-PKG-160` |
| KG-042 | 2026-09-24 | Tamamlandı | **UR-008 = B 👤: A-59 ve BL-058 preview.3'ten önce kapandı; UR-006 = A (K-872). Prova sürüme özel dizine taşındı; net8 runtime'ı global kuruluma eklenir 👤.** A-72 açıldı | Kullanıcı B'yi seçti. İki iş küçüktü ve `main` itilmediği için güvenlik düzeltmeleri bu sürede public olmadı. Prova dizini kararı Faz 136'yı dizin içinde korur (MT-PKG-116): aynı sürümün yeni commit'i reddedilir, mesaj `rm -rf` yolunu verir. KG-041'in "tek push yeter" ifadesi yanlıştı: Faz 187 kesimi etiketle tek atomik push ister | KN-029 · `MT-PKG-161`…`163` · push sonrası CI |
| KG-043 | 2026-09-24 | Tamamlandı | **A-64'ün ilk CI koşumu iki bacakta kırmızı: üç kusur `276ac42e`'de kapandı; kesim commit'i düzeltmelerin ÜSTÜNE yeniden yazıldı.** A-73 açıldı | Kesim commit'i yerelde düzeltmelerin altındaydı; etiketsiz itilirse prova kırmızı olur (Faz 187). Yerel, itilmemiş commit'ler yeniden sıralandı; eski sıra `yedek/preview3-kesim-once-1f2e507d` dalında durur. `CA1875` negatif kontrolle CI'ın SDK'sında (10.0.401) yerelde yeniden üretildi; düzeltmeyle çözüm derlemesi ve `format` o SDK'da yeşil | `276ac42e` sonrası CI iki bacakta yeşil |
| KG-044 | 2026-09-24 | Tamamlandı | **Site etiketten ÖNCE yayımlandı 👤** (`scripts/site-deploy.sh`, o anki HEAD `78dc72bc` — kesim `17ca8dfb` dahil, 21:43; Voice bölümü taşınmadan önce). KG-038 emsalinin tersine kullanıcı kararı | Risk söylendi ve kabul edildi: site `1.0.0-preview.3` der ve 13 `Security` notunu gösterir, paket nuget.org'a etiketin `publish` işiyle gelir; arada `dotnet add package … 1.0.0-preview.3` restore edilemez. Script kendi tazelik denetimini geçti (`/`, `/packages/`, `/reference/changelog/` preview.3 der) | `Published and verified (https://tracon.dev/capabilities/index.md -> 200 text/markdown)`; lisans sayfası `THIRD-PARTY-NOTICES.txt`'yi anlatır |
| KG-045 | 2026-09-24 | Tamamlandı | **İkinci CI koşumu: build yeşil, `release-dryrun` ve `site` kırmızı; iki kusur `88f8e4ce`'de kapandı, kesim yine düzeltmelerin üstüne yeniden yazıldı** | (1) Sample doğrulaması sözleşme paketinin grafiğini kaynak projenin `artifacts/obj` restore çıktısından okuyordu; Faz 191'den beri `release-dryrun` restore etmez → `FileNotFoundError`. Artık paketlenen nuspec'lerin Tracon kapanışından okunur; o `obj` dizini kaldırılmış hâlde doğrulama yerelde yeşil. Hafızadaki kural ("`release-dryrun`'da `obj/` yoktur") vardı, uygulanmamıştı. (2) `troubleshooting` CI'da 59 003 B (tavan 59 000; yerelde 58 993): kapının kendi notu gereği tavan yükseltilmedi, sayfa bölündü — Voice belirtileri `guides/voice.md#troubleshooting`'e taşındı, 58 151 B. Eski sıra `yedek/preview3-ci2-once-a0c8cbca` dalında | Üçüncü koşum: build ×2 + `release-dryrun` + `site` yeşil |
| KG-046 | 2026-09-24 | Tamamlandı | **`1.0.0-preview.3` sevk edildi; A-64 kapandı.** Üçüncü `main` koşumu (`36048047050`) Windows'taki yarış testi (F-257) yüzünden kırmızıydı; aynı kod önceki koşumda Windows'ta geçmişti ve aradaki diff `src/`/`tests/`'e dokunmuyordu → "Re-run failed jobs" (API) → yeşil. Etiket bu yeşil koşumun üstüne atıldı | Yarış testi kod kusuru değil ama etiket koşumunu da düşürebilir; F-257 🟠'a çıktı | KN-032 · KN-033 · etiket koşumu `36057172063` |

## 12. Ertelenen işler ve gerekçeleri

| Kimlik | Konu | Durum | Gerekçe | Yeniden açılma ölçütü |
|---|---|---|---|---|
| ER-001 | `stable 1.0` freeze | Ertelendi | Preview artifact ve üçüncü taraf feedback kanıtı yok; MAF Hosting/A2A pre-release | Stable ölçütlerinin tamamlanması |
| ER-002 | Gerçek NuGet.org push | Ertelendi | Açık kullanıcı onayı ve operasyon checklist'i yok | Tüm preview blocker'lar kapanır ve kullanıcı onay verir |
| ER-003 | Kapsamlı düzeltme fazı | Ertelendi | Henüz doğrulanmış aksiyon kapsamı yok | Audit ölçülmüş bir iş üretir ve kullanıcı faz açılmasını onaylar |

## 13. Sonraki adım

> **`preview.3` SEVK EDİLDİ (2026-09-24, KG-046).** Kalan 👤: preview.1/2
> deprecation (OP-008, nuget.org) · A-61 advisory. Sonra GA hattı: A-72
> (`Tracon.Cli` bildirimi) · A-73 (SDK sabitleme) · A-71 kapısı. F-257 (yarış
> testleri) 2026-10-11'de kapandı (K-879; ürün kusuru + sahte sunucu yarışı).
> Ayrıntı: §4 "YAYINLANDI — 2026-09-24".

> 🚨 **`preview.1` SEVK EDİLDİ (2026-09-20).** Kapanmış `preview.1`
> aksiyon tablosu damıtıldı (tam metin: `git show 8634a1e3:docs/YAYIN-HAZIRLIK.md`);
> A-10 · A-11 · A-12 dahil tüm tag öncesi kalemler kapandı. **Sıradaki iş `preview.2`/GA hattıdır** ve açık kalemler
> şunlardır: A-8 (job süre sınırı) · A-16 (`Tracon.Mcp` üç sevk edilen tipi testsiz) · A-17
> (`Tracon.Azure` çağrı yolu kanıtsız) · A-18 (dış sample kanıtı `IRunStore`
> ile sınırlı) · A-28 (sesli delegasyon timeout ≠ barge-in) · UR-003 (public
> API freeze taraması) · A-31 (**yeni**: kapanış kapısı artımlı durumdan
> yeniden üretilebilir değil — bayat çıktı kopyası 81 testi düşürdü ve kapı
> bunu "gerçek regresyon" diye raporladı; A-15 sınıfı) · **4 eksik store
> contract'ı** (A-21 ölçümü).
>
> Yayın günü bulunan A-29 · A-32 · A-33 **kapandı** — üçü de §4'ün başındaki
> YAYINLANDI bloğundadır. **A-15 kapandı (Faz 191, K-871):** CI tek derleme
> zinciridir; `pack` işi yoktur, prova itilen dosyaları doğrular.
>
> **Faz 191'den sonraki ilk `v*` etiketinde (👤, `MT-PKG-160`):** nuget.org'daki
> paketi (`v3-flatcontainer`) aynı koşumun `nuget-verified` artifact'iyle girdi
> bazında karşılaştır (`.signature.p7s` hariç). Ham SHA-256 farkı beklenir (depo
> imzası); girdi farkı beklenmez. Aynı gün "Re-run failed jobs"un artifact'ları
> yeniden kullanıp kullanmadığı da ölçülür.

### GA turuna ertelenenler

- **UR-003 public API freeze taraması** — 680 tip, `Unshipped → Shipped`
  dolumuyla aynı turda (KG-016).
- §10'daki Source Link gerçek çözümleme, reproducible build ve tam
  artifact-seviyesi contract/güvenlik matrisi. Package validation'ın taban
  kısmı Faz 187'de kapandı (yayın provası); `Shipped` dolumu (K-603) GA'da kalır.
- KG-019 ile ertelenen contract suite, dış sample ve operasyonel metric kulvarları.
- **BL-057** — nuget.org `Tracon` organizasyonuna ikinci üye (👤 bakımcı eylemi,
  RK-012). GA bu kalem kapanmadan çıkmaz.
- **1.0 olgunluk denetimi (2026-09-24)** — A-59…A-70 · BL-058 · RK-015 ·
  UR-004…UR-007 (§4 GÜNCEL KARAR). Sıra orada yazılıdır; ilk iş A-64.

## 14. Yayın sonrası ilk 72 saat planı

| Zaman | Durum | Plan |
|---|---|---|
| Yayın öncesi | **Tamamlandı (2026-09-03)** | Sorumlu: tek bakımcı (`farukatasoy`). Güvenlik kanalı `SECURITY.md` (özel e-posta, 72 saat içinde onay); kusur kanalı GitHub issue şablonları (bug · dokümantasyon). ✅ **Repo public yapıldı (2026-09-19), ∴ ikisi de artık dış tüketiciye görünür** — 2026-09-03'te kabul edilen boşluk kapandı |
| **Sevk anı (2026-09-20)** | **Tamamlandı** | Tag `f14f6309`; koşum `35510131648` 8/8 yeşil; npm `latest` = `1.0.0-preview.1`; 20 NuGet paketi push edildi; GitHub release oluştu. 🚨 nuget.org indeksi push'tan **sonra** dolar — o andaki `404` bir kusur değildir |
| 0–2 saat | **Tamamlandı (2026-09-20) — ❌ DUYURMA** | `nuget-danismani` duyuru öncesi tüketici denetimi koştu: beş yüzey (nuget.org · GitHub · tracon.dev · paketten tüketim · npm). Artifact **temiz**; lisans, dil sınırı (K-228), `AgentPrism` taraması, 20 paket · 168 operasyon · 30 ekran · 36 rota ve `releaseNotes` çapası **doğrulandı**. **Giriş yolu kırık:** A-34 (`dotnet new tracon-api` → `NU1102`) ve A-35…A-38 (sevk edilen doküman hâlâ "yayınlanmadı" diyor). 25 yeni kalem **A-34…A-58**; beş 🔴 duyuruyu bloklar. 20 paket sayfası tek tek gezildi: sembol paketleri 18/18, Source Link `f14f6309`'da çözülüyor, K-008 tutuyor. Bkz. §4, 2026-09-20 duyuru öncesi tüketici denetimi bloğu |
| 2–24 saat | Bekliyor | Restore/build/runtime sorunları, issue'lar, dependency ve security uyarıları izlenir; doğrulanmış kritik kusurda yeni indirmeler için deprecation değerlendirilir |
| 24–48 saat | Bekliyor | İlk tüketici geri bildirimi public API, docs ve extension ergonomisi sınıflarına ayrılır; preview compatibility etkisi yazılır |
| 48–72 saat | Bekliyor | Patch/sonraki preview kararı verilir; release retrospective ve risk kaydı güncellenir |

Bozuk yayın silinebilir varsayılmaz. NuGet.org üzerinde kalıcı artifact mantığı
esas alınır. Düzeltme yeni sürümle yapılır; deprecation ve yönlendirme kararı
olayın etkisine göre verilir.

## 15. Seam envanteri ve matris planı (BL-003) — kapandı, damıtıldı

11/11 küme tamam (2026-09-16). Kalıcı sonuç: 78 arayüz × 3 boyut XML sözleşme
kapısı `SeamContractDocumentationTests` ile kilitli; küçülen taban çizgisi 174
satır bilinen borçtur (KG-017). Kulvar kapanışları: Faz 119 · 120 · 121 · 122 ·
123. Tam küme dökümü ve plan: `git show 8634a1e3:docs/YAYIN-HAZIRLIK.md`.

## 16. Sınıf taraması: ham exception → kalıcı/dışa açık durum (BL-027 · BL-037) — kapandı, damıtıldı

26 vaka `SafeErrorText` + mimari cırcır kapısıyla kapandı ([Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md));
sınıf taraması ilk 21 vakaya 5 ek vaka kattı. Tam vaka dökümü:
`git show 8634a1e3:docs/YAYIN-HAZIRLIK.md`.
