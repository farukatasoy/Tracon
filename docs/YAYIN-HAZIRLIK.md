# Tracon Yayın Hazırlığı

> Bu dosya, Tracon paket ailesinin ilk NuGet.org yayını için yaşayan kontrol
> düzlemidir. Faz planı, sohbet özeti veya genel karar defteri değildir. Yalnız
> ölçülen kanıtı, yayın kararlarını, risk kabulünü ve doğrulama durumunu taşır.
>
> **Son güncelleme:** 2026-09-20  
> **Çalışma modu:** `nuget-danismani` — Yayın sonrası  
> **🚨 GÜNCEL DURUM iki bloktur.** ✅ `1.0.0-preview.1` **SEVK EDİLDİ** (§4
> başı, YAYINLANDI bloğu) — fakat ❌ **DUYURU HÂLÂ YAPILMAMALI**: §4'teki
> **2026-09-20 duyuru öncesi tüketici denetiminin** source ve doküman işleri
> kapandı. Ancak immutable `preview.1` şablonu hâlâ `1.0.0` üretir (A-34) ve
> npm'de `latest` hâlâ preview sürümünü gösterir (A-42). Duyurudan önce
> düzeltmeler `preview.2` olarak yayımlanmalı ve npm dist-tag'leri canlıda
> hizalanmalıdır. Yeni kalemler **A-34…A-58**. Tag `v1.0.0-preview.1` → `f14f6309`;
> tag koşumu `35510131648` sekiz işin sekizinde de yeşil; `@tracon/client`
> npm'de yayında, 20 NuGet paketi push edildi, GitHub release oluştu. Bu dosya
> artık bir **yayın** kararı taşımıyor — taşıdığı karar **duyuru** kararıdır;
> yayın sonrası kayıt ve ilk 72 saat planı (§14) için okunur. Yayın günü **üç kusur** bulundu ve üçü de kapandı —
> üçü de yalnız yayın yolunda koşan, o güne kadar hiç gerçek veri görmemiş
> adımlardı: **A-29** (tahsis kapısı `/_`; CI'da 21 push boyunca `skipped`,
> ilk koşumu tag'di), **A-32** (iptal edilen workflow `run`'ı hiç kapanmıyordu;
> desen agent yolunda vardı, workflow yoluna taşınmamıştı), **A-33**
> (`NuGet/login` `user:` policy'nin sahibini değil **oluşturanını** ister).
> A-10 (site deploy) ve A-12 (`nuget` environment kuralı `branch` → `tag`) de
> kapandı. Aşağıdaki 2026-09-19 ve öncesi metinler **tarihsel bağlamdır**.
> **Eski not — 2026-09-19 üçüncü turu için geçerliydi:** ❌ O gün tag
> atılmazdı, açık ÜRÜN 🔴'si olduğu için değil. O tur ikinci turun
> ❌'ini devralmadı: artifact'i `c5ed5573`'ten **yeniden ölçtü** ve temiz buldu.
> Kalan üç 🔴 (A-10 site deploy sırası · A-11 trusted publishing policy ·
> A-12 environment koruması) **operasyoneldir ve hiçbiri repoda değildir**.
> İkinci turun dört 🔴'sini kapatma turu (KG-035) kapattı; kalan iki 🟡'yi
> (A-26 · A-27) üçüncü tur kapattı. Aşağıdaki ikinci ve birinci tur metinleri
> **tarihsel bağlamdır**.
> Kalan yolu ve dört ürün kararını (KG-026…029) 2026-09-16 bloğu taşır; o blok
> hâlâ sıra kaynağıdır, fakat **yayın kararı 2026-09-19'dadır**. 2026-09-16'nın
> ❌'i devralınamaz: adım 1–3 bitti ve prova tag'lenecek commit'ten koştu.
> **Eski not — 2026-09-14 bloğu için geçerliydi:** 2026-09-03 kararı
> ("✅ Yayınlanabilir") **devralınamaz**: o günden beri 184 commit, ~30 faz, bir
> ürün yeniden adlandırması ve bir lisans değişikliği geçti. Aşağıdaki
> 2026-09-02 ve 2026-08-28 anlatıları tarihsel bağlamdır.  
> **Hedef durumu:** Seam matrisi **11/11 küme tamam**; BL-006 **kapandı**;
> BL-026 ölçümle **🟡'ye indirildi**, belge kısmı **[Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı**;
> BL-027/BL-037 sınıf taramasıyla **21 vakaya
> genişledi** ve **[Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md) ile kapandı** (26 vaka
> kapatıldı — sınıf taraması 5 ek vaka daha buldu; `SafeErrorText` + mimari
> cırcır kapısı); BL-041 **[Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) ile
> kapandı** (`IJobHandler` sözleşmesi at-least-once'ı yazıyor, `JobHandlerContract`
> üç yerleşik handler + bir dış sample tarafından koşuyor, `JobLeaseExpiryTests`
> davranışı ölçüyor); **35× 🟡 hattının kulvar 3'ü (XML sözleşme boşlukları)
> [Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı** — BL-024/028/029/035/038/042/043/046
> tam, BL-048/050 kısmen (KG-017); `SeamContractDocumentationTests`'in küçülen
> taban çizgisi 174 satır (78 arayüz × 3 boyut) bilinen borç olarak kalır  
> **Karar (2026-09-19):** ✅ **Yayınlanabilir** — açık 🔴 yok ve artifact kanıtı
> `225f1472`'den taze. Önceki ❌ yayın kritik yolu kapanmadığı içindi; kapandı.
> **Eski geçici karar (2026-09-16):** ❌ Yayınlanmamalı — açık 🔴 olduğu için değil,
> yayın kritik yolu henüz kapanmadığı için. Açık 🔴 yoktur; UR-003 GA'ya ertelendi
> (KG-016). **Yol B seçildi (KG-019):** 35× 🟡 hattı burada durduruldu — kulvar
> 2, 3 ve 6 kapandı (Faz 121 · 122). **Kulvar 1, 4 ve 5 [Faz 123](arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md)
> ile kapandı:** **BL-052** (yayın kapısı artık altı dış sample'ın altısını da
> koşuyor, envanter kapısıyla kilitli), **BL-015** (dört shipped adaptör de
> `ModelProviderContract`/`ModelProviderCredentialContract`'ı türetiyor —
> sözleşme gerçek bir BYOK-cache kusuru buldu, K-646 ile dördünde de düzeltildi),
> **OP-007** (`CHANGELOG.md` + sürüme çapalı `PackageReleaseNotes` + fail-closed
> kapı) ve **OP-006** (`github-release` CI işi, aynı ayrıştırıcıyı OP-007 ile
> paylaşır). **Doküman drift taraması
> [KG-022](#11-karar-günlüğü) ile kapandı** — 12 bayat iddia düzeltildi, dört
> kapının dördü de yeşil. Hesap ve operasyon kararları (OP-008/009, ilk 72 saat,
> CI secret/protection modeli) 2026-09-03 turuyla kapandı. GitHub Free yayını
> engellemez; ancak private repo'da environment secret, required reviewer ve
> deployment tag restriction sağlamaz (KN-020) — repo KG-028 ile public
> yapılacağı için bu sınır da kalkar. **`preview.1` tag'inden önce kalan iş
> 2026-09-16 bloğundaki on adımdır**; adım 1, 2 ve 3 bitti — sıradaki iş
> **yayın turudur** (adım 4, `nuget-danismani` Adım 1→8). RK-014 kapandı
> (KG-030/031): geçmişte gerçek credential yok, rotate gerekmedi. Yayın türü `preview` (UR-001), sürüm `1.0.0-preview.1`
> (KG-029), paket kapsamı tam entegrasyon seti (UR-002/BL-002) ve owner modeli
> kişisel hesap (OP-001) sabit — bkz. §4 ve §13.

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

### ✅ YAYINLANDI — 2026-09-20, `1.0.0-preview.1`

**Tag `v1.0.0-preview.1` → `f14f6309`.** Koşum `35510131648`: sekiz işin
sekizi de yeşil (`build` ×2 · `pack` · `release-dryrun` · `npm-publish` ·
`publish` · `github-release`; `site` yalnız `main`'de koşar, etikette atlanır).
`@tracon/client@1.0.0-preview.1` npm'de `latest` etiketiyle; 20 NuGet paketi
push edildi; GitHub release oluştu.

🚨 **"Push başarılı" ile "paket görünür" ayrı anlardır.** İlk kez yayınlanan
paket kimlikleri nuget.org doğrulamasından geçer; `v3-flatcontainer`,
registration ucu ve paket sayfası bir süre `404` döner. Yayın kanıtı CI'ın
`publish` işidir, indeksin o andaki hâli değil.

#### Sevk edilen artifact

Prova `f14f6309`'un içeriğinden koştu (`YAYIN_EXIT=0`): 20 paket + 18 sembol
paketi, tek sürüm hattı, manifest `dirty: false`, **38/38 `sha256` bağımsız
yeniden hesaplandı**, fazla/eksik paket yok, 20 `.nuspec`'in 20'si
`https://tracon.dev/reference/changelog/#v1.0.0-preview.1` ve doğru
`repository commit` taşıyor. Kapanış kapısı aynı içerikten 10/10 yeşil.

#### Yayın günü bulunan ve kapatılan üç kusur

Üçünün de ortak sınıfı var: **yalnız yayın yolunda koşan, o güne kadar hiç
gerçek veri görmemiş adımlar.** Üçü de yerel kapılardan geçmişti.

| # | Kusur | Kök sebep | Kapanış |
|---|---|---|---|
| **A-29** | Tahsis kapısı her tag'de koşup `exit 134` veriyordu | `CI=true` → `ContinuousIntegrationBuild` → `DeterministicSourcePaths` kaynak yollarını `/_` yapar; bench'in `[CallerFilePath]` tabanlı `RepositoryRoot()`'u `/_` döndürür ve BenchmarkDotNet dosya sistemi kökünde dizin açmaya çalışır. CI geçmişinde kapı **21 push boyunca `skipped`**'ti; ilk gerçek koşumu tag oldu | `4e415098` — çözüm `AppContext.BaseDirectory`'den yukarı yürüyüş (`RepoRoot.cs` deseni); `BenchmarkRepositoryLayoutTests` her CI koşumunda koşar; `ci.yml` etiketi artık **açıkça** ele alır |
| **A-32** | İptal edilen workflow `run`'ı hiç kapanmıyordu | `RunGuardedAsync` bir async iterator; `writer.StartAsync` ile `CompleteAsync` arasında `try/finally` **yoktu**. Tüketici `break` ederse enumerator dispose edilir ve gövde yalnız `finally` bloklarını koşar. `RunReconciliationService` yetimi sonunda **`Failed`** olarak kapatıyordu ⇒ kullanıcının iptali, dakikalar sonra, olmamış bir başarısızlık olarak raporlanıyordu | `d337d5af` — `finally` hâlâ açıksa `Canceled` yazar. **Sınıf taraması:** `run` açan tam iki yüzey var; `RunRecordingAgent` bu `finally`'yi iki kusurla (HATA-S4-012, HATA-S4-003) zaten kazanmıştı, workflow yoluna taşınmamıştı |
| **A-33** | `NuGet/login` OIDC takası `HTTP 401` | `user:` policy'yi **oluşturan** hesabın profil adıdır, policy'nin **sahibi** değil. Policy `Tracon` org sahipliğinde olduğu için `user: Tracon` doğru görünüyordu ve `ci.yml` yorumu bunu savunuyordu | `f14f6309` — `user: farukatasoy`. **Hiçbir testle yanlışlanamazdı**: yalnız gerçek bir token takası ölçer, o da yalnız `v*` etiketinde olur |

#### Yayın hesabı tarafında ölçülen iki ayar

- **npm granular token'ında "Bypass two-factor authentication (2FA)" kutusu
  işaretli olmalı.** Scope'lar (`@tracon` + `tracon` org, read/write) doğru olsa
  bile kutu kapalıyken `npm error code EOTP`. Org düzeyinde 2FA enforcement
  açıkken zorunlu.
- **Trusted publishing policy'nin glob'u `Tracon*`** ve scope'u *"Push new
  packages and package versions"* — 20 kimliğin 20'si de yeniydi, "yalnız yeni
  sürüm" seçeneği hiçbirini basamazdı. Glob ölçüldü: **20/20 eşleşiyor**.

#### İki yarım yayın denemesi ve neden bedelsiz kaldı

npm `EOTP`'de düştüğünde `publish` (NuGet) `needs: npm-publish` olduğu için
**hiç koşmadı**; NuGet OIDC'de düştüğünde `dotnet nuget push` **hiç koşmadı**.
İkisinde de sürüm numarası yanmadı. `npm-publish` var olan sürümü `npm view`
ile görüp **atlar ve iş kırılmaz** ⇒ etiketi taşıyıp yeniden denemek bedelsiz.
Sıralama (geri dönüşü olan kanal önce) tasarlandığı işi yaptı — RK-011.

---


### 🚨 GÜNCEL KARAR — 2026-09-20 (`nuget-danismani`, duyuru öncesi tüketici denetimi)

**❌ Bugün duyurulmamalı.** Paket artifact'i sağlamdır; **tüketicinin ilk yarım
saati** kırıktır. İki bağımsız 🔴 var ve ikisi de aynı yerde buluşuyor — duyuruyu
okuyan kişinin tıkladığı ilk iki yüzey:

1. **`dotnet new tracon-api` restore edilemeyen bir proje üretiyor** (A-34).
   Sevk edilen şablon `Version="1.0.0"` yazıyor; o sürüm nuget.org'da **yok**.
2. **Sevk edilen dokümantasyon hâlâ "yayınlanmadı" diyor** (A-35…A-38).
   `tracon.dev` ana sayfası ilk ekranda *"In development. Not yet published to
   NuGet or npm."* diyor ve **aynı sayfanın** aşağısında *"The packages are
   published as `1.0.0-preview.1`"* diyor.

Bu tur **artifact'i yeniden yargılamadı** — 2026-09-20 YAYINLANDI bloğu geçerli.
Yargılanan şey **tüketicinin gördüğü yüzeydir**: nuget.org · GitHub ·
tracon.dev · paketten tüketim · npm.

#### Ölçülen kanıtlar

| Ne | Nasıl ölçüldü | Sonuç |
|---|---|---|
| Şablondan tüketim | Repo **dışında** temiz dizin, `<clear/>` + yalnız nuget.org, izole `NUGET_PACKAGES` | `dotnet new install Tracon.Templates` ✅ (`--prerelease` gerekmiyor) → `dotnet new tracon-api` **`NU1102`** |
| Elle kurulum | Aynı izole dizin | `dotnet add package Tracon --prerelease` → `1.0.0-preview.1`, `dotnet build` **0 uyarı** ✅ |
| Çalışma anı | `dotnet run`, `curl` | `/tracon/api/meta` → `"version":"1.0.0-preview.1"`, bellek içi store fallback ✅ |
| Konsol | Playwright, temiz `localStorage` | `<html lang="en">`, "Dashboard" — İngilizce ✅ (ilk ölçümdeki Türkçe **yerel makinenin** `tracon.locale=tr` kalıntısıydı) |
| Anahtarsız hata | `POST /api/agents/support/run` | *"No model provider named 'openai' is registered… call `builder.AddTracon().UseOpenAI(apiKey)`"* — düzeltmeyi **adıyla** söylüyor ✅ |
| CLI | `dotnet tool install --tool-path … Tracon.Cli --prerelease` | Kuruldu ve koştu ✅ |
| README kod bloğu | Yayınlanan pakete karşı **derlendi** | `README.md:118` → **`CS1501: No overload for method 'ResolveAsync' takes 1 arguments`** |
| Konsol ağırlığı | `wwwroot/assets/*.js.br` → brotli aç → gzip -9 (kapının yöntemi) | **193,2 KB** / 250 KB. README **184,1** der, site **192,4** der |
| Ekran ve rota | `app.tsx` ayrıştırıldı | **30 ekran · 36 rota** — README doğru ✅ |
| Paket sayısı | `src/*/*.csproj`, `IsPackable=false` hariç | **20** ✅ · nuget.org arama indeksi **21 hit** (yetişti) ✅ |
| `AgentPrism` | Tüm repo `git grep -i` + canlı HTML | Sevk edilen yüzeyde **0** ✅ (`site.config.mjs:34` kasıtlı **deny-list**) |
| Dil sınırı (K-228) | `docs-site/src`, canlı HTML, `llms*.txt`, paket README'leri | **0 Türkçe** ✅ |
| Lisans | 5 `.nupkg` içi + npm + site + README | Dört yüzeyde tutarlı ✅ |
| `releaseNotes` çapası | `https://tracon.dev/reference/changelog/#v1.0.0-preview.1` | `id="v1.0.0-preview.1"` **var** ✅ |
| 20 paket sayfası (canlı) | Her biri tek tek açıldı | 20/20 açılıyor · README render ediyor · icon · tag · `projectUrl` · **Prefix Reserved** · ön sürüm bandı ✅ Fazladan sürüm **yok** (flat container 20/20 yalnız `1.0.0-preview.1`) |
| Sembol paketleri | `globalcdn.nuget.org/symbol-packages/*` | **18/18** `200`. Eksik ikisi (`Tracon`, `Tracon.Templates`) assembly taşımıyor ⇒ doğru ✅ |
| Source Link | PDB doküman tabloları | `raw.githubusercontent.com/farukatasoy/Tracon/f14f6309…` → `200`; repo public, commit tag'le **eşleşiyor** ✅ |
| K-008 (ön sürüm yoğunlaşması) | 20 paketin tamamı tarandı | Tracon dışı ön sürüm bağımlılığı **yalnız** `Tracon.AspNetCore`'da, **5 adet** — kural **tutuyor** ✅ |
| README taşınabilirliği | 20 README, canlı sayfa | **0 göreli link · 0 resim · 0 ham HTML · 0 anchor-only link**; tablolar render ediyor ✅ (tek istisna A-55) |
| Bağımlılık grupları | Çok-TFM 17 paket | net8/9/10 grupları **birebir aynı**, `lib/` üçünde de dolu ✅ |

#### Bulgular

| # | Bulgu | Seviye | Kök sebep | Kanal | Duyuruyu bloklar |
|---|---|---|---|---|---|
| **A-34** | **`dotnet new tracon-api` restore edilemiyor.** Sevk edilen `template.json` `defaultValue: "1.0.0"`; nuget.org'da yalnız `1.0.0-preview.1` var ⇒ `NU1102`. Her seçenek kombinasyonunda, her ek paket referansında tekrarlanır | 🔴 | `TraconStampTemplateVersionDefault` (`Tracon.Templates.csproj:119`) `BeforeTargets="_GetPackageFiles;GenerateNuspec"`; **`_GetPackageFiles` MinVer'den ÖNCE koşar** ⇒ `$(Version)` hâlâ SDK varsayılanı `1.0.0`. Kardeş hedef `TraconSetPackageReleaseNotes` (`src/Directory.Build.props:120`) yalnız `GenerateNuspec` kullanır ve **doğru** çalışır — kendi yorumu "`GenerateNuspec` … son nokta" der. **Kapı neden görmedi:** `<Error>` yalnız yer tutucunun *bulunduğunu* doğrular, ikamenin doğru sürümü ürettiğini değil; `TemplateFixture.cs:135` **her zaman** `--TraconVersion {Version}` + `--skip-restore` geçer ⇒ tüketicinin aldığı **varsayılan hiç koşulmadı**. 🚨 **Ağırlaştırıcı:** `dotnetcli.host.json` `TraconVersion`'ı `isHidden: "true"` yapıyor ⇒ `dotnet new tracon-api -h` **dört** seçenek gösteriyor, `--TraconVersion` **sıfır** kez geçiyor. Tek kurtuluş `NU1102`'nin kendi metnindeki "Nearest version" ipucuyla `.csproj`'u elle düzenlemek | `kusur-giderme` | **EVET** |
| **A-35** | **Sevk edilen metin "yayınlanmadı" diyor — 18 dosya.** `Hero.astro:14` *"In development. Not yet published to NuGet or npm."* (**ilk ekran**) · `Footer.astro:13` (**1150 sayfanın hepsi**) · `index.mdx:183` · `README.md:219` **"## Installation / Not published yet"** · + 14 sayfa `:::caution`. Ana sayfa **kendi içinde çelişiyor** | 🔴 | Premis üç katmana gömülü (component · sayfa admonition'ı · config flag); `b76351fd` yalnız **düzyazıyı** düzeltti | `tuketici-dokuman-senkronu` | **EVET** |
| **A-36** | **Kanonik ilk rehber kaynak checkout'u öğretiyor.** `getting-started/first-agent.md`: açıklaması *"Build a Tracon host from an authorized source checkout"*, ön koşulu *"An authorized checkout"*, adımı `dotnet add reference ../Tracon/src/…csproj`. `dotnet add package` / `dotnet new tracon-api` **sıfır kez** geçiyor. Repo **public**, paketler **canlı** — sitenin söylemediği iki çıkış yolu da açık | 🔴 | Rehber yayın öncesi yazıldı, yayın sonrası yeniden yazılmadı | `tuketici-dokuman-senkronu` | **EVET** |
| **A-37** | **Troubleshooting, sebep olduğu hataya yanlış cevap veriyor.** `troubleshooting.md:64`, *"NuGet says no stable version exists"* başlığı altında: *"Tracon has not been published yet: no version exists on NuGet at all… Build from a clone"*. Doğru tek satırlık çözüm (`--prerelease`) gelecek zamanlı bir ihtimal olarak sunuluyor | 🔴 | A-35 ile aynı premis; ama bu sayfa **zaten takılmış** tüketiciyi yanlış yöne yolluyor | `tuketici-dokuman-senkronu` | **EVET** |
| **A-38** | **Site'den ürüne hiçbir link yok.** 1150 sayfanın hiçbirinde nuget.org, github.com veya npm linki yok. Kök sebep `site.config.mjs:51` `repositoryIsPublic = false`; `check-content.mjs:1303` repo linkini **derlemeyi kırarak** yasaklıyor, Starlight `editLink`/GitHub ikonu kapalı. Flag'in kendi yorumu "Make the repository public and flip the flag" der; repo 2026-09-19'da public oldu | 🔴 | Flag elle bakımlı, gerçeğe bağlı değil | `kusur-giderme` (flag) + `tuketici-dokuman-senkronu` (linkler) | **EVET** |
| **A-39** | **README'nin amiral kod bloğu derlenmiyor.** `README.md:118` `await catalog.ResolveAsync("support")` → yayınlanan pakete karşı **`CS1501`**. `IAgentCatalog.ResolveAsync`'in iki aşırı yüklemesi var, ikisi de ≥2 argüman ister | 🟡 | Blok elle yazılmış, derlenen bir örnekten üretilmiyor | `kusur-giderme` | hayır |
| **A-40** | **GitHub deposu anonim.** `description: null` · `homepage: null` · `topics: []` · Discussions **kapalı**. `og:description` *"Contribute to farukatasoy/Tracon development…"*'a düşüyor ⇒ paylaşılan her duyuru linki jenerik kart olarak önizleniyor. Release `prerelease: false` ⇒ "Latest" rozeti taşıyor | 🟡 | Depo public yapılırken metadata doldurulmadı | Manuel müdahale | hayır (ama duyurunun **bedeli**) |
| **A-41** | **npm README'si SSE'yi "desteklenmiyor" diyor; paket destekliyor.** README *"call these with your own `EventSource`"* diyor; paket `readSse`/`SseDecoder` **export ediyor** ve `sse.d.ts:8` `EventSource`'un `Authorization` header'ı gönderemediğini ve POST edemediğini yazıyor ⇒ tüketici **imkânsız** bir yola yollanıyor. Ayrıca "Six operations", gerçek **yedi** | 🟡 | `packages/tracon-client/README.md` Faz 159 öncesi sürümde kalmış; site rehberi güncel | `tuketici-dokuman-senkronu` | hayır |
| **A-42** | **npm `latest` bir ön sürümü gösteriyor.** Çıplak `npm install @tracon/client` sessizce preview kurar ve `^1.0.0-preview.1` **floating** aralık yazar — sitenin kendi `versioning` sayfası floating aralıktan kaçınmayı söyler. NuGet tarafında `--prerelease` **zorunlu**; iki ekosistem zıt davranıyor ve bu hiçbir yerde yazılı değil | 🟡 | Yayın `--tag next` yerine varsayılan `latest` ile yapıldı | Ürün kararı + `tuketici-dokuman-senkronu` | hayır |
| **A-43** | **npm `package.json`'da `repository`, `bugs`, `keywords` yok.** npm sayfası "Keywords: none" gösteriyor; `npm repo` çalışmıyor; provenance/attestation `repository` olmadan mümkün değil | 🟡 | — | `kusur-giderme` | hayır |
| **A-44** | **Konsol ekran görüntüleri 10 arayüz commit'i bayat.** `docs-site/public/screenshots/` 40 dosyanın hepsi 2026-09-15 (`ada74d11`); o tarihten sonra `src/Tracon.UI/frontend/` 10 commit aldı (onay adımı, streaming caret, admin panelleri). `check-console-screens.mjs:55` yalnız `existsSync` bakar | 🟡 | Kapı **varlığı** ölçüyor, **tazeliği** değil | `kusur-giderme` (kapı) + yeniden çekim | hayır |
| **A-45** | **Konsol ağırlığı üç yerde üç farklı.** Ölçülen **193,2 KB** gzip (kapının yöntemi) · `README.md` **184,1** · `ui.md:392` **192,4**. `kalite-sozlesmesi.md:201` 184,1'i "yalnız düşer" diye kaydeder — gerçek **yükseldi** ve kimse görmedi | 🟢 | Sayı üç yerde elle tutuluyor, ölçüme bağlı değil | `tuketici-dokuman-senkronu` + kapı | hayır |
| **A-46** | **`README.md:183` `Tracon.Voice` için "Zero NuGet dependencies" diyor**; sevk edilen `.nuspec`'te `Tracon.Core` bağımlılığı var. Paketin **kendi** README'si doğru yazıyor ("`Tracon.Core` is the only dependency… no third-party NuGet"). A-25 bu kalemi **kapandı** diye kaydetmişti — site ve paket README'si düzeldi, kök README atlandı | 🟢 | Sınıf taraması kök README'yi kapsamadı | `tuketici-dokuman-senkronu` | hayır |
| **A-47** | **README'de üç bayat sayı.** `:338` "671 public types" (site: **764**) · `:310` "20 test projects" (çözümde **23**, koşan **22**) · `:192` CLI listesi `agent-skill`'i **atlıyor** (6 komutun 5'i) · `:282` yol haritası "release phase stays open" diyor | 🟢 | Elle tutulan sayılar | `tuketici-dokuman-senkronu` | hayır |
| **A-48** | **`samples/` giriş noktasından görünmez.** 6 extension sample + 6 test projesi; **hiçbirinde README yok**, **hiçbiri `Tracon.slnx`'te değil**, kök README yalnız `samples/Tracon.Api`'yi linkliyor — onun da README'si yok. README:192 "derive from them to verify your own `IRunStore`…" diye **reklamını yapıyor** | 🟢 | — | `faz-planlama` | hayır |
| **A-49** | **`SourceLanguageTests` sevk edilen altı yüzeyi görmüyor:** `CHANGELOG.md`, `SECURITY.md`, `.github/**`, `samples/**/*.md`, `docs-site/**`, release notu. Bugün **hepsi temiz** — ama kapı bir regresyonu yakalayamaz | 🟢 | `ScanRoots` + `ScannedRootFiles` dar | `kusur-giderme` (kapı) | hayır |
| **A-50** | **`check:weight` 277 B boşlukta.** `troubleshooting/index.html` 58 723 B / 59 000 B. 🚨 **A-37'nin düzeltmesi tam bu sayfayı düzenliyor** — tavan önce bilinçli yükseltilmeli | 🟢 | — | `kusur-giderme` | hayır |
| **A-51** | **Yerel ortam kalıntısı:** `~/.nuget/NuGet/NuGet.Config` hâlâ `agentprism-local` feed'i taşıyor. Sevk edilen hiçbir şeyde değil, ama MEMORY.md'nin "yeniden adlandırma yerel ortamı geride bırakır" sınıfının **beşinci** vakası. Bu denetimin ilk şablon koşumunu da kirletti | 🟢 | — | Manuel müdahale | hayır |
| **A-52** | **`Tracon.Client` `Description`'ı kendi bağımlılık tablosuyla çelişiyor.** nuspec: *"Takes no Tracon package and **no NuGet package**."* Gerçek: `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.11`. Paketin **kendi README'si doğru** yazıyor ("**One** NuGet package"), kök `README.md:191` de doğru. Yanlış olan yalnız nuspec `Description`'ı — sayfanın en üstünde ve arama sonucunda görünen metin | 🟡 | "one" → "no" düzenleme kayması | `kusur-giderme` (`src/Tracon.Client/*.csproj`) | hayır |
| **A-53** | **`Tracon.AspNetCore/README.md:20` "143 operations over 112 paths" diyor.** Gerçek **168 operasyon / 130 yol** — ve bunu ispatlayan OpenAPI dokümanı **aynı `.nupkg`'in içinde** (`buildTransitive/tracon.json`). Meta paketin README tablosu da 143'ü tekrarlıyor. Site ve landing sayfası 168 diyor ve kapılı | 🟡 | Sayı elle tutuluyor; `check-content.mjs` yalnız **site** sayfalarını ve landing metriklerini ölçüyor, paket README'lerini değil | `tuketici-dokuman-senkronu` + kapıyı `src/*/README.md`'ye genişlet | hayır |
| **A-54** | **`Tracon.Abstractions/README.md:20` "64 interfaces and roughly 280 public types" diyor.** Sevk edilen `lib/net10.0/Tracon.Abstractions.xml`'den ölçüldü: **429 dokümante public tip**, **85** `I<Upper>` adlı arayüz (bağımsız reflection ölçümü 418 exported tip / 85 arayüz). Arayüz +%33, tip +%50 eksik sayılmış. Bu paketin **tek işi** "işte uygulayacağın sözleşmeler" demek | 🟡 | A-53 ile aynı sınıf | `tuketici-dokuman-senkronu` | hayır |
| **A-55** | **nuget.org Mermaid basmıyor — `Tracon.Core` sayfasında diyagram yerine DSL kaynağı görünüyor.** Canlı HTML: `<pre><code class="language-mermaid">flowchart LR … A["Agent sources&lt;br/&gt;code + database"]` — okuyucu düz metin bir blok ve içinde literal `&lt;br/&gt;` görüyor. 20 README'nin **1'i** (`src/Tracon.Core/README.md:37`). GitHub'da doğru render ettiği için fark edilmedi | 🟡 | 🚨 `AGENTS.md`'nin "**her diyagram Mermaid**" kuralının **paket README'si istisnası yok**; nuget.org kısıtlı Markdown'dır | `tuketici-dokuman-senkronu` + kuralı `AGENTS.md`'de daralt | hayır |
| **A-56** | **İki paket README'sinde kopyalanamayan kurulum komutu:** `dotnet new install Tracon.Templates@1.0.0-preview.N` (`src/Tracon/README.md:33`, `src/Tracon.Templates/README.md:8`). `N` literal yer tutucu; hemen ardından açıklanıyor, ama artık **tek** sürüm var ⇒ yer tutucu hiçbir şey kazandırmıyor, bir başarısız yapıştırma maliyeti getiriyor. `A-19`'un kapısı (`--prerelease`/`--version` varlığı) bunu **geçerli** sayıyor | 🟢 | Kapı bayrağın **varlığını** ölçüyor, sürümün **çözülüp çözülmediğini** değil | `tuketici-dokuman-senkronu` | hayır |
| **A-57** | **`tracon --version` yok** — `Unknown command '--version'` döner. Kurulumu doğrulamak için ilk yazılan komut budur; `dotnet tool list -g` dolaylı çözüm | 🟢 | — | `kusur-giderme` | hayır |
| **A-58** | **nuget.org kenar çubuğu 20 pakette de yalnız jenerik "License Info" gösteriyor**, çünkü hepsi SPDX ifadesi yerine `license type="file"` kullanıyor. Tüketici sayfa mobilyasından 17 paketin **gelir tavanlı source-available** olduğunu göremez; yalnız README gövdesinde yazıyor. PolyForm'un SPDX kimliği var (`PolyForm-Small-Business-1.0.0`) ve npm paketi onu **zaten kullanıyor** | 🟢 | İki kanal iki farklı lisans ifade biçimi kullanıyor | Ürün kararı + `kusur-giderme` | hayır |

#### Bu turun sınıfı: **premis bayatladı, metin değil**

A-34 · A-35 · A-36 · A-37 · A-38 tek bir cümlenin çocuklarıdır: *"Tracon henüz
yayınlanmadı."* O cümle 2026-09-20 12:44'te yanlış oldu. `b76351fd` **iki**
düzyazı cümlesini düzeltti; premis ise **beş ayrı mekanizmaya** gömülüydü:
Astro component'i · sayfa admonition'ı · config flag'i · MSBuild hedefi ·
şablon varsayılanı. Hiçbir kapı bunu göremez çünkü **hiçbir kapı registry'ye
bakmıyor** — "yayınlandı mı?" sorusunun CI'da bir ölçümü yok.

🚨 **Ders:** yayın gününün üç kusuru (A-29 · A-32 · A-33) "yalnız tag yolunda
koşan adımlar" sınıfındandı. Bu turun beş kusuru onun **ikizi**: *yalnız yayın
GERÇEKLEŞTİĞİNDE yanlışlanabilen iddialar.* İkisi de aynı boşluğu gösterir —
gerçek dünyayla ilk teması yayın anında olan hiçbir iddianın kapısı yok.

#### Doküman drift (bu dosyanın kendisi)

- **Kapandı.** §3 artık `Tracon.Testing` için `net8.0;net9.0;net10.0` yazar.
  Envanterin 20 satırı da yayımlanan `1.0.0-preview.1` sürümünü gösterir.

#### Kapanış — 2026-09-20

**Source tarafındaki 25 kalemin 25'i sonuçlandı.** 23 kalem tam kapandı. A-34
ve A-42 için kalıcı source düzeltmesi hazırdır; ancak registry'deki mevcut
artefact ve dist-tag immutable/live state olduğu için duyuru kararı henüz
yeşil değildir.

| Aralık | Sonuç | Kanıt |
|---|---|---|
| **A-34** | 🟡 **Source kapandı, live açık.** Şablon stamp hedefi artık MinVer'den sonra koşar; gizli `TraconVersion` seçeneği görünürdür. Packed-consumer testi sürümü override etmeden gerçek `.nupkg`'i kurar ve üretilen projeyi restore/build eder. Mevcut `preview.1` paketi değiştirilemez; düzeltme `preview.2` ister | `TemplateInstantiationTests` **3/3**; minimal ve full şablon build'i yeşil |
| **A-35…A-41** | ✅ Kapandı | Site artık yayını ve public giriş yollarını anlatır; `tracon.dev` yeniden deploy edildi ve canlıdan doğrulandı. README örneği derlenir; GitHub metadata/Discussions/release türü canlıda düzeltildi; npm SSE README'si gerçek API'yi anlatır. Canlı kontrolde bulunan çift source-path'li `Edit page` URL'si de düzeltildi ve rendered URL kalıcı kapıya alındı |
| **A-42** | 🟡 **CI ve doküman kapandı, live açık.** Preview yayınları bundan sonra `next` alır; workflow `latest` bir preview'e bakıyorsa onu kaldırır. Kurulum metni `@next` kullanır. Yerel ortam npm'e authenticated olmadığı için mevcut canlı `latest` bu turda değiştirilemedi | `npm view @tracon/client dist-tags --json` → `latest: 1.0.0-preview.1`; `npm whoami` → `E401` |
| **A-43…A-57** | ✅ Kapandı | npm metadata, screenshot tazelik hash'i, tek ağırlık ölçümü, paket/README sayıları, sample kataloğu, dil ve operation-count kapıları, NuGet-uyumlu diyagram, exact kurulum sürümü ve `tracon --version` eklendi |
| **A-58** | ✅ **Değişiklik gerektirmiyor.** NuGet license expression yalnız SPDX değil, NuGet'in kabul ettiği OSI/FSF lisansları için kullanılabilir. PolyForm bu kümede değildir. `PackageLicenseFile` doğru ve doğrulanabilir sunumdur | [NuGet nuspec `license` sözleşmesi](https://learn.microsoft.com/nuget/reference/nuspec#license) ve [.NET library guidance](https://learn.microsoft.com/dotnet/standard/library-guidance/nuget) |

Kalıcı kapılar da genişledi: template testi artık packed default'u ölçer;
`check-content.mjs` README çağrı imzasını ve paket README operation count'larını
denetler; screenshot kapısı UI source hash'ini doğrular; `SourceLanguageTests`
sevk edilen ek yüzeyleri tarar; CI preview/stable release ve npm dist-tag
politikasını birbirinden ayırır.

`dokuman-bakim.py --site-denetle` içindeki `cekirdek-kavram` kuralı
`Tracon.Abstractions/README.md` değişikliği nedeniyle tetiklendi. Değişiklik
yalnız ölçülen interface/public type sayısını günceller; kavram, davranış veya
public contract değiştirmez. Bu nedenle `concepts/` sayfası değişmedi ve
gerekçeli geçiş kullanıldı.

**Duyuru için kalan sıra:** temiz commit üzerinde `preview.2` yayın dry-run'ı →
`1.0.0-preview.2` yayını → authenticated npm dist-tag doğrulaması → dışarıdan
`dotnet new tracon-api` restore/build smoke. Tam kapanış ve site deploy bu turda
yeşil tamamlandı. `preview.2` dry-run'ı temiz worktree zorunluluğu nedeniyle
commit öncesinde bilinçli olarak başlamadı; güvenlik kapısı aşılmadı. Canlı
registry state'ini ölçmeden bu karar ✅ olmaz.

Kapanış kanıtı: `python3 scripts/kapi.py kapanis --taban b76351fd` çıkış **0**;
build **0 warning/0 error**, tüm .NET test projeleri yeşil, format ve paket
kapıları yeşil. Site **1151 sayfa**, **189.663** internal reference, **0** kırık
link, SEO **0 hata** ve tüm sayfalar 59.000 B gzip tavanının altında. İki
`scripts/site-deploy.sh` koşumu çıkış **0**; son koşum doğru GitHub edit URL'sini,
NuGet/npm/GitHub bağlantılarını ve yayın metnini canlıya taşıdı.

---

### Önceki karar — 2026-09-20 (`nuget-danismani`, tag sonrası yayın turu)

**❌ Bu tag'den yayın çıkmaz — `1.0.0-preview.1`.** Açık bir **ürün** 🔴'si
yoktur; artifact `75478786`'dan yeniden ölçüldü ve temiz. Blocker **CI'ın tag
yolunun kendisidir**: tag atıldı, koşum düştü, yayın işlerinin üçü de atlandı.

#### Tag atıldı — ve hiçbir şey yayınlanmadı

`refs/tags/v1.0.0-preview.1` → `75478786` origin'de. Koşum `35499705399`
(08:29Z) **failure**; `Paketle`, `Yayin provasi`, `NuGet.org'a yayinla`,
`npm.org'a yayinla` ve `GitHub release` **atlandı**. Üç kanaldan doğrulandı:
**20 paket kimliğinin 20'si de nuget.org'da `404`**, npm `@tracon/client` `404`,
GitHub releases `0`. Fail-fast çalıştı — `1.0.0-preview.1` hiçbir kayıtta
yanmadı ve tag bedelsiz yeniden atılabilir.

#### 🔴 A-29 — tahsis kapısı YALNIZ tag yolunda koşuyor ve orada düşüyor

CI geçmişi tarandı (son 11 koşum, 22 örnek): `Performans kapisi (tahsis)`
**21 kez `skipped`**, **1 kez `failure`** — ve o tek koşum **tag koşumudur**.
∴ kapı CI'da bugüne kadar **hiç başarıyla koşmadı**; ilk gerçek koşumu tag
oldu ve 9 saniyede düştü (derleme adımı 4 dakikada başarılıydı; 9 sn bir eşik
ihlali için fazla kısa, BenchmarkDotNet'in ürettiği projenin derlenme fazı).

**Kök sebep ölçüldü — yapısaldır, flake değildir.** [`ci.yml:149`](../.github/workflows/ci.yml)
`BASE="${{ github.event.pull_request.base.sha || github.event.before }}"`
yazar. Tag push'unda `github.event.before` **kırk sıfırdır**; `git cat-file -e`
düşer ve fallback `triggered=true` verir. Yerelde doğrulandı: sıfırlarla
`triggered=true`, `main` push'unda taban `47680acf` ile değişen üç doküman
dosyası `performance_gate_triggered=False` veriyor. ∴ kapı, adımın kendi
yorumunun ("yalnız üç sıcak yoldan biri değiştiyse koşar") tersine, **her
tag'de koşulsuz koşar** — yani tam olarak düşmesinin yayını engellediği olayda.

**Kapının kendisi sağlam:** yerelde `kapi.py performans` → **çıkış 0**, üç
benchmark de taban değerinde (24 B · 112 B · 45040 B, 82 sn).

🚨 **KÖK SEBEP ÖLÇÜLDÜ (log 👤) — flake değil, HER tag'de tekrarlar:**

```
System.UnauthorizedAccessException: Access to the path '/_' is denied.
 ---> System.IO.IOException: Permission denied
   at BenchmarkDotNet.Extensions.CommonExtensions.CreateIfNotExists(String)
   at BenchmarkDotNet.Running.BenchmarkRunnerClean.GetRootArtifactsFolderPath(...)
   at Program.<Main>$(String[]) in /_/bench/Tracon.Benchmarks/Program.cs:line 28
❌ benchmark koşumu çıkış 134
```

Zincir üç dosyayı birbirine bağlar ve üçü de tek başına doğrudur:

1. [`Directory.Build.props:33`](../Directory.Build.props) —
   `<ContinuousIntegrationBuild Condition=" '$(CI)' == 'true' ">true</...>`.
   CI'da açılır; SDK bunu görünce `DeterministicSourcePaths`'i açar ve depo
   kökünü kaynak yollarında **`/_`** ile değiştirir.
2. [`bench/Tracon.Benchmarks/Program.cs:14`](../bench/Tracon.Benchmarks/Program.cs)
   `RepositoryRoot([CallerFilePath] string sourceFilePath = "")` yazar. Üstündeki
   yorum varsayımı **açıkça** kurar: *"`[CallerFilePath]` gives this source
   file's own absolute path AT BUILD TIME … always this checkout's path."*
   O varsayım CI'da **yanlıştır**: yol `/_/bench/Tracon.Benchmarks/Program.cs`
   olur (stack trace'te birebir görünür).
3. ∴ `RepositoryRoot()` = `/_`, `WithArtifactsPath("/_/artifacts/benchmarks")`
   ve BenchmarkDotNet **dosya sistemi kökünde** dizin açmaya çalışır → izin yok.

**Ders — deponun kendi sınıfı:** yorum, farkında olduğu varsayımı ("build ve run
aynı makinede") yazmış ama sessizce güvendiği ikinci varsayımı ("kaynak yolları
gerçek yollardır") yazmamış. Onu iki dosya ötedeki determinizm ayarı bozuyor.
Kapı 21 push'ta `skipped` olduğu için kusur bugüne kadar **hiç görünmedi**;
ilk gerçek koşumu tag oldu.

**Kapsam (`kusur-giderme`):** `RepositoryRoot()` `[CallerFilePath]`'e
güvenmemeli. Seçenekler — **A:** bench projesinde
`<DeterministicSourcePaths>false</DeterministicSourcePaths>` (tek satır; bench
paketlenmediği için determinizm orada bir şey kazandırmıyor). **B:** yolu
`kapi.py`'den `--artifacts` ile geçir (çağıran sahibi olur). **C:** çözülen yol
yoksa `AppContext.BaseDirectory`'den yukarı yürüyen kendini savunan fallback.
**Öneri: C + A** — C kusuru her ortamda kapatır ve sessizce geri gelmesini
engeller, A da determinizmin bench'te hiç devreye girmemesini sağlar.

#### 🔴 A-30 — `Kapasite smoke (packed tuketici)` aynı commit'te iki sonuç verdi

`47680acf` koşumunda **success** (3 dk 38 sn), `75478786` `main` koşumunda
**failure**. İki commit arasındaki tek fark `CHANGELOG.md` + iki doküman
dosyasıdır ⇒ kod regresyonu **olamaz**. Adım hiçbir süreyi eşiğe bağlamaz
(K-738) ve kendi PostgreSQL container'ını başlatır; ∴ altyapı kaynaklı.
Kesin sınıflandırma yine log gerektirir.

🚨 **Aynı commit `75478786` iki koşumda üç FARKLI adımda düştü:** tag'de
windows `Test et (Docker gerektirmeyenler)` + ubuntu `Performans kapisi`,
`main`'de ubuntu `Kapasite smoke`. Windows aynı commit'te `main` koşumunda
**success** verdi. ∴ tag koşumunun windows düşüşü flake'tir; ubuntu'nun iki
düşüşü A-29 ve A-30'dur.

#### Artifact kanıtı — TEMİZ (`75478786`)

`kapi.py kapanis --taban ce23527b` → **çıkış 0** (10/10 adım; test 693 sn).
`kapi.py yayin --kuru --surum 1.0.0-preview.1` → **çıkış 0**: 20 paket + 18
sembol paketi, tek sürüm hattı, `npm publish --dry-run`, 6/6 packed sample,
Native AOT smoke. `package-manifest.json`: `commit: 75478786`, `dirty: false`;
**38 dosyanın 38'inin `sha256`'sı bağımsız yeniden hesaplandı ve tuttu**, fazla
paket yok, eksik paket yok. 20 `.nuspec`'in 20'si de `releaseNotes` olarak
`https://tracon.dev/reference/changelog/#v1.0.0-preview.1` ve `repository
commit="75478786…"` taşıyor.

#### 🚨 Kapanış kapısı ilk turda 81 testle KIRMIZI döndü — sebebi üründe değildi

81 düşüşün 81'i tek mesajdı: `UI assets are not embedded`. Guard'ın saydığı iki
sebebin ikisi de elendi (`* 2.*` → 0 eşleşme; hiçbir build dosyası
`TraconFrontendEnabled=false` demiyor). Gerçek sebep **bayat çıktı kopyasıdır**:
`artifacts/bin/Tracon.Ui.E2ETests/release/Tracon.UI.dll` 11:28'de 23.040 B
(varlıksız), `artifacts/bin/Tracon.UI/release_net10.0/Tracon.UI.dll` 11:32'de
197.120 B (varlıklı) — tüketici, bağımlılığın varlık taşıyan çıktısı
üretilmeden **dört dakika önce** kopyayı almış. Kapının build adımı
`dotnet build Tracon.slnx -c Release` ve **`-m:1` taşımıyor**; frontend hedefi
bu koşumda hiç çalışmadı (damga ve `wwwroot` güncel). Yalnız E2E projesini
yeniden derlemek kopyayı tazeledi (23.040 → 197.120 B) ve **81/81 test geçti**.
Sevk edilen assembly üç TFM'de de varlıkları taşıyor ⇒ **ürün etkilenmedi**.

**A-31 (🟡 GA):** kapı artımlı durumdan yeniden üretilebilir değil ve yanlış
yönde yanılıyor — düşen testleri izole koşup "izole de düşen test GERÇEK
regresyondur" diyor, oysa izole koşum da aynı bayat dll'i okuduğu için ayrım
yapamıyor. A-15 sınıfı.

#### Bu turda kapanan

- **A-12 kapandı.** `nuget` environment'ının tek deployment kuralı
  `name='v*' type='branch'` idi; `npm`'de aynı kural `type='tag'`. İkisi de
  `refs/tags/v*` ile tetiklenir ve GitHub dokümanı "Name patterns must be
  configured for branches or tags individually" der ⇒ tag ref'i branch
  kuralıyla eşleşmez. Sıra en kötüsüydü: `npm-publish` önce koşup basar,
  `publish` reddedilir, `@tracon/client` NuGet'siz kalırdı ve npm bir sürüm
  numarasını unpublish sonrası bile geri vermediği için `1.0.0-preview.1`
  yanardı — CI'nin kendi RK-011 sözünün ihlali. Kullanıcı düzeltti; yeniden
  ölçüldü: `name='v*' type='tag'`, `required_reviewers: farukatasoy` korunuyor.
- **A-11 kullanıcı beyanı 👤:** trusted publishing policy kuruldu ve aktif
  (7 gün). Anonim doğrulanamaz. Dolaylı kanıt tutarlı: nuget.org'da `Tracon`
  profili var, 20 kimliğin 20'si boş (⇒ "yeni paket" scope'u şart),
  `ci.yml` `user: Tracon` yazar.
- **RK-013 bayatladı (🟢).** "Source Link üçüncü taraf için çözemez — repo
  private" artık yanlış: repo public (anonim API `visibility: public`) ve
  `RepositoryUrl` `github.com/farukatasoy/Tracon`'a bakıyor ⇒ Source Link
  çözer. Kabul edilmiş bir risk kendiliğinden kapandı; §10 satırı düzeltilmeli.

#### A-10 hâlâ AÇIK ve sıra bozuldu

Site deploy tag'den **önce** gelmeliydi; gelmedi.
`https://tracon.dev/reference/changelog/` canlıda hâlâ "has not been released"
diyor, `#v1.0.0-preview.1` çapası **yok**. Üretici tarafı hazır:
`build-changelog.mjs` → çıkış 0 ve `<a id="v1.0.0-preview.1"></a>` basıyor.
CI düştüğü için paketler **hiç basılmadı** ⇒ sıra hâlâ kurtarılabilir.

#### Kalan yol

1. Düşen üç adımın log'unu aç (yalnız kullanıcı okuyabilir): ubuntu
   `Performans kapisi (tahsis)` · ubuntu `Kapasite smoke` · windows
   `Test et (Docker gerektirmeyenler)`
2. A-29'u kapat — tag push'unda tabanın sıfır olması `triggered=true`
   üretmemeli; kapının kendi yorumundaki niyet uygulanmalı
3. A-30'u sınıflandır ve kapat
4. `scripts/site-deploy.sh` (gerçek) + `curl` ile çapayı doğrula
5. Tag'i sil, yeniden at: `git push origin :refs/tags/v1.0.0-preview.1`
6. `CHANGELOG.md` sevk tarihi (`2026-09-20`) gerçek tag gününe çekilmeli

---

### Önceki karar — 2026-09-19 (`nuget-danismani`, üçüncü tur)

**❌ Bugün tag atılmaz — `1.0.0-preview.1`.** Açık bir **ürün** 🔴'si yoktur.
Kalan üç 🔴 operasyoneldir, üçü de repo dışındadır ve üçü de tag sırasına
bağlıdır: A-10 · A-11 · A-12. Bu tur ikinci turun ❌'ini **devralmadı**;
artifact'i tag'lenecek commit'ten yeniden ölçtü.

#### Artifact kanıtı — TEMİZ (`c5ed5573`)

`python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` → **çıkış 0**.
20 paket + 18 sembol paketi, `npm publish --dry-run`, 6/6 packed sample, Native
AOT smoke. `package-manifest.json`: `commit: c5ed5573`, `dirty: false`; **38
dosyanın 38'inin `sha256`'sı elle yeniden hesaplandı ve tuttu.**

🚨 **Kapının ilk koşumu `EXIT=1` döndü ve sebebi kapının kendisi değildi:**
`artifacts/package/release` bir önceki turun (`49caa193`) paketlerini
taşıyordu; promote kapısı aynı kimlikte farklı içeriği reddetti (KG-033'ün
koruduğu yol). Dizin boşaltılınca yeşil. **Adım 6'da aynı tuzak vardır** —
prova, dizin pristine değilse bir şey kanıtlamaz.

#### Bu turda ölçülen ve kapatılanlar

- **A-27 kapandı.** Ölçüldü: `docs/KARARLAR.md` 495.820 / 496.000 B (%0 boş) ve
  `kapi.py kapanis` içindeki `dokuman-bakim.py --denetle` bütçe aşımında **1
  döner** ⇒ yayın turunun yazacağı ilk karar adım 6'yı kırmızıya çevirirdi.
  `karar-damit` **59 satırın gerekçesini taşıdı** (41.807 B); defter 455.089 B
  (%8 boş). Tavan **değişmedi** — K-781 ve 2026-09-15 turlarının aksine bu kez
  taşıma tükenmemişti.
- **A-26 kapandı** (K-844 👤). Kök sebep ölçüldü: sebep üç ayrı **çıkışta**
  kuruluyordu, gerçek yol ise MAF'ın sessiz bitişiydi ve o yol hiçbirinden
  geçmiyordu. Sebep artık `run`'ın **kapandığı tek yerde** eklenir ve yalnız
  `timeout` kaynağına bakar. Durum `Canceled` **kalır** (kullanıcı kararı).
  Kapı: `WorkflowRunTimeoutReasonTests` — iki yarım, üç koşumda da kararlı.
- **Kontrol düzleminin kendi drift'i kapandı:** §4 başlığı ikinci turun ❌'ini
  taşıyordu (KG-035 onu kapatmıştı) · §10'un K-835 kalemi "karar verilmedi"
  diyordu (kod `GenerateContentAsync`'e geçmişti) · §13 adım 10 Faz 176–178'i
  `preview.2` işi sayıyordu (üçü de **2026-09-16'da landi**, KG-026 pratikte
  tersine döndü) · adım 9'un "bitti ölçütü" hücresi boştu.

#### Kapanış kapısı ÜÇ kusur buldu — üçü de sürüm kesiminin açtığı yollardı

Kapı dört kez koştu ve ilk üç turun her biri gerçek bir kusur kapattı. Hiçbiri
testlerin yeşilliğinden görülemezdi; üçü de **kesimin kendisi** tarafından
açığa çıkarıldı:

1. **`changelog_test.py` bir anı dondurmuştu.** Deponun kendi `CHANGELOG.md`'sinin
   `Unreleased` yedeğine düştüğünü iddia ediyordu — yalnız ilk kesime kadar doğru
   olan bir cümle. Test artık dosyanın **kestiği sürümü** okuyup notların oradan
   geldiğini doğruluyor; kesim yoksa yedeğe düşüyor. Yedek kuralının kendisini
   üstündeki sentetik case'ler tutuyor.
2. **Üretilen sayfa sitenin kendi adresini yazıyordu.** Kesim gövdeyi ilk kez
   render etti ve gövde lisans sayfasına **mutlak** bir bağlantı taşıyor — kök
   `CHANGELOG.md`'de doğru (GitHub release gövdesi ve paket release-notes
   bağlantısı site dışındadır), `docs-site/src` altında yanlış. Üretici artık
   kendi origin'ini düşürüyor; autolink'i **önce** dönüştürerek, çünkü
   `<https://host/x/>`'ten host'u çıkarmak `</x/>` bırakır ve o artık autolink
   değildir.
3. **Sevk edilen bir XML yorumunda alarm emojisi vardı.** `ShippedDocumentationSelfContainmentTests`
   bunu yasaklar (paket metni kırpılmamış sevk eder) ve ölçüldü: emojili hâl kapıyı
   **kırmızı**, emojisiz hâl yeşil yapıyor.

**Ders:** sürüm kesimi bir başlık yeniden adlandırması değildir — **sevk
edilmeyen bir gövdeyi ilk kez sevk edilir hâle getirir** ve o gövdeye bakan her
kapı o an ilk kez gerçek veriyle karşılaşır.

#### 🆕 A-28 — sesli delegasyonun zaman aşımı, barge-in'den AYIRT EDİLEMİYOR

A-26'nın sınıf taraması bir **ikinci vaka** buldu ve bu turda **kapatılmadı**.
`LiveVoiceSessionHost` delegasyonu `CreateLinkedTokenSource(_lifetime.Token)` +
`CancelAfter(_options.DelegationTimeout)` ile kurar ve barge-in **aynı** kaynağı
iptal eder (`_delegations.Values` üzerinden). ∴ `catch (OperationCanceledException)`
dalında "süre doldu" ile "kullanıcı sözünü kesti" ayırt edilemez; alttaki agent
`run`'ı `RunRecordingAgent` tarafından sebepsiz `Canceled` kapatılır. Düzeltme
workflow'unkiyle aynı şekildedir (ayrı bir timeout kaynağı) ama **sesli seam'i
yeniden yapılandırır** ve A-26'nın kapsamında değildir. Seviye 🟡: kayıt doğru
kapanır, yalnız sebebi yoktur.

**Sınıf taramasının geri kalanı temiz.** `src/` içindeki 20 iç deadline
sahasının hepsi tek tek okundu; sebebini **zaten** adıyla kaydedenler:
`ChildAgentInvoker` (`WriteTimedOutAsync` + `TimeoutRefusal`) ·
`OnlineEvalJobHandler` (`JudgeFailureTypes.Timeout`) · `SkillScriptProcessRunner`
(`TimedOut`) · `WebhookDeliveryJobHandler` (`"Timed out (Ns)."`) ·
`ToolApprovalPresenterRunner` (limiti log'a yazar) · sağlayıcı health check'leri
ve MCP (çağırana fırlatır). `RunRecordingAgent`'ın üç `Canceled` sahasının
**kendi deadline'ı yoktur** ⇒ orada `Canceled` her zaman "biri istedi" demektir
ve yeni kuralla tutarlıdır.

---

### Önceki karar — 2026-09-19 (`nuget-danismani`, bağımsız ikinci tur)

**❌ Bugün yayınlanmamalı — `1.0.0-preview.1`.** Aynı günün önceki turu
✅ demişti; bu tur onu **devralmadı** ve yeniden ölçtü. Paket artifact'i temiz
çıktı, fakat **artifact dışında** üç açık kalem bulundu. İkisi sevk edilen
dokümanın runtime ile çeliştiği yerler, biri kiracı yalıtımı sınırıdır.

#### Artifact kanıtı — TEMİZ (yeniden üretildi)

`python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` → **çıkış 0**,
HEAD `49caa193`'ten, `release` dizini **boşaltıldıktan sonra**. 20 paket, tek
sürüm hattı, 6/6 packed sample + Native AOT smoke. `package-manifest.json`
yeniden doğrulandı: **20/20 paket ve 18/18 sembol paketi hash uyumlu**, 20
`.nuspec`'in hepsi `commit="49caa193…"` taşıyor.

#### 🔴 1 — Kiracı kimliğinin karşılaştırma semantiği sağlayıcılar arasında AYRIŞIYOR

Kiracı kimliği **hiçbir yerde normalize edilmiyor** (`src/` içinde `TenantId` +
`ToLower|Normaliz|Canonical` → **0 eşleşme**). `HttpTenantContext`
`^[a-zA-Z0-9_.-]+$` kabul eder (büyük harf serbest); `AllowedTenants`
`StringComparer.Ordinal`; `RunCancellationRegistry` `StringComparison.Ordinal`.
Buna karşılık SQL Server'da `tenant_id nvarchar(200)` için **hiçbir `COLLATE`
yok** ve store sorguları düz `tenant_id = @tenant_id`.

**Ölçüldü** (testlerin kullandığı imaj, Tracon'un sütun tanımı):
`SQL_Latin1_General_CP1_CI_AS`; `'acme'` yazılan satır **`'Acme'` sorgusuna
döndü**. ∴ SQL Server'da yetkilendirme katmanı iki kiracıyı **ayrı**, depolama
katmanı **aynı** sayar. PostgreSQL/SQLite varsayılanı case-sensitive olduğu için
aynı kurulum orada **ters** davranır (tek kiracının verisi sessizce ikiye bölünür).

🚨 **Emsal:** birebir aynı sınıf `provider_name` için K-639 / migration 0025 ile
bir **güvenlik düzeltmesi** olarak kapatılmıştı (RK-007). Kiracı kimliği — yani
ürünün birincil yalıtım anahtarı — o düzeltmenin dışında kaldı.
**Ölçülmeyen:** Tracon'un kendi store API'si üzerinden uçtan uca koşum.

🚨 **Ağırlaştıran ikinci kol — case-SENSITIVE motorlarda adı konmuş bir güvenlik
kontrolü sessizce uygulanmıyor.** Zincir uçtan uca doğrulandı:
`TraconTenancyOptions.AllowedTenants` **varsayılan boştur** ve dokümanı "If left
empty, any value matching the format is accepted" der; `HttpTenantContext.Accept`
o durumda adayı **olduğu gibi** döndürür; `ModelProviderRegistry.cs:287` ise
`if (policy is not null)` — yani kiracı satırı **bulunamazsa hiçbir egress kısıtı
uygulanmaz (fail-open)**. ∴ PostgreSQL/SQLite'ta `acme` için yazılmış egress
allow-list'i, istek `Acme` olarak geldiğinde **tamamen devre dışı kalır** ve
kiracı yasaklanmış bir sağlayıcıyı çağırabilir. Saldırgan kontrollü yol API key
sunulduğunda `TraconEndpointFilter` (Ordinal → 403) ile **kapalıdır**; kalan
gerçekçi tetikleyici claim tabanlı (JWT/OIDC) kurulumda IdP'nin harf durumu
kayması ve API key zorunlu olmayan loopback varsayılanıdır — saldırı değil,
**operasyonel kayma**.

🚨 **Sınıf taraması sonucu:** aynı ayrışma `tool_name`, `agent_name`,
`skill_name`/`script_name` için de **var**, fakat üçü de **fail-closed** yöndedir
(grant ıskalanır → yetki doğmaz). `provider_name` (K-639) ve API key hash'i
(`varbinary(32)`, collation-bağışık) **kapalıdır**. ∴ `tenant_id` bu sınıfın
**tek fail-open üyesidir** — ve düzeltmenin şablonu depoda hazır durur.

#### 🟡 4 — Public API düz metin BYOK anahtarı döndürüyor

`TenantChatClientCacheKey.For(...)` **public**'tir ve `preview.1` ile çıkar
(`PublicAPI.Unshipped.txt:624,1411`). Döndürdüğü string'in **ilk alanı kiracının
düz metin API anahtarıdır** (`string.Concat(credential.ApiKey, "|", …)`); XML
yalnız "A key stable across calls with observably identical inputs" der,
anahtarın içeride olduğunu **söylemez**. Tracon'un kendi kodunda aktif sızıntı
**yok** (yalnız in-process dictionary anahtarı); risk tüketici davranışıdır —
"cache miss'te anahtarı logla" doğal bir debug hamlesidir. K-059 disiplininin
dışında kalmış tek yüzey. `Shipped.txt` boş olduğu için bugün `internal` yapmak
ya da değeri hash'lemek **bedava**.

#### 🟡 6 — Bir `store` OCE'si TÜM HOST'U durduruyor — ürün kuralının kendi yazıldığı yerde ihlali

Depoda **hiçbir yer** `BackgroundServiceExceptionBehavior` ayarlamıyor (ölçüldü:
`src/`, `tests/`, `samples/` → **0 eşleşme**) ⇒ .NET varsayılanı **`StopHost`**.
Altı arka plan servisi ortak bir desen kullanıyor: tick
`catch (Exception ex) when (ex is not OperationCanceledException)`, döngü
`catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)`.
`stoppingToken` iptal edilmemişken gelen bir OCE **iki filtreyi de geçer**,
`ExecuteAsync` fault eder ve host durur.

🚨 [RunHeartbeatWriter.cs:83](../src/Tracon.Core/Recording/RunHeartbeatWriter.cs#L83)
— filtrenin **hemen üstündeki yorum** "Observability does not break
functionality. A failed cycle does not affect active runs and the next cycle
retries." diyor. Yani kural tam olarak ihlal edildiği satırda yazılı.

**Tetikleyici:** `IRunStore`/`IApprovalStore`/`IJobStore` **public genişleme
noktalarıdır** ve rehber sayfaları vardır; bir tüketici store'unu HTTP üzerinden
yazarsa `HttpClient.Timeout` → `TaskCanceledException` (OCE türevi) → host durur.
Birinci-parti SQL sürücüleri command timeout'u `SqlException`/`NpgsqlException`
olarak bildirdiği için **yerleşik yol temizdir**. Daha sinsi alt vaka:
`ApprovalExpirationService` `finally { await guardTask; }` ile istisnayı yutar ⇒
singleton seçimi **açıkken** servis **sessizce, log'suz** durur; onaylar bir daha
hiç expire olmaz. Desen sınıfı: `is not OperationCanceledException` `src/` içinde
**97 yerde**. Düzeltme yerel ve API kırmaz.

#### 🟡 7 — `Timeout` adlı üç public options alanı gerçek wait cutoff DEĞİL

Gerçek cutoff yalnız üç yerde var (`TimeoutAIFunction`, `IRunJudge`,
`SkillScriptProcessRunner` — üçü de `WhenAny` + geç tamamlanmayı observe).
Kalanlar `CreateLinkedTokenSource` + `CancelAfter`, yani **kooperatif**:

- `TraconWebhookOptions.Timeout` — CTS `SendAsync` dönünce **dispose** edilir
  (`WebhookHttpClient.cs:88-99`), gövde okuması job worker token'ıyla sürer ⇒
  yalnız **header fazı** kesilir. XML "a single delivery attempt" diyor.
- `TraconWorkflowOptions.RunTimeout` — süper-adım **sınırında** kontrol edilir;
  MAF adım ortasında token'ı onurlandırmıyor (repo bunu F-107 ile zaten ölçmüş).
  `rg "RunTimeout" tests/` → **sıfır test**. `MaxConcurrentRuns` varsayılanı 4 ve
  XML'i "shared across all tenants" diyor.
- `TraconSchedulingOptions`'da **hiç** `Timeout` alanı yok; `RenewLoopAsync`
  kiralamayı handler bitene kadar **süresiz** yeniler ⇒ takılan bir handler
  `MaxConcurrentJobs` (varsayılan 2) slotunu kalıcı tutar ve başka replika işi
  **alamaz**. `IJobHandler` XML'i ise lease dolunca yeniden lease edilmeyi anlatır.

#### 🟡 5 — Onay parmak izinin ayırıcı varsayımı zorlanmıyor

`ToolApprovalRuleEvaluator.cs:226-236` argümanları `U+001F` ile birleştirir ve
yorum "It is not present in text values" der; **bu önerme hiçbir yerde
doğrulanmıyor** — JSON `` taşıyabilir. Elle inşa edilen çakışma:
`{"a":"x","b":"y"}` ile `{"a":"x␟b=y"}` **aynı** `arguments_hash`'i üretir, yani
"bu tam çağrıyı bir daha sorma" grant'i argüman şekli farklı bir çağrıya miras
kalabilir. Yol script dispatcher'da — yani kod çalıştıran yüzeyde — zorunludur.
**Dürüst sınır:** çakışma inşa edilebiliyor, **istismarı gösterilemedi** (çakışan
sözlük alanları birleştirir, değer değiştirmez). Hash formatını değiştirmek
preview sonrası migration + davranış değişikliğidir; bugün maliyeti sıfıra yakın.

#### 🔴 2 — Sevk edilen doküman, çalışmayan bir yeteneği çalışıyor gibi anlatıyor

K-835 (`MT-MM-121`): `Tracon.Google` görsel yolu artık sunulmayan Imagen
`:predict` ucunu hedefliyor; **canlı ölçümde her üretim `502`**. Buna rağmen
`docs-site/capabilities.md:126`, `api/Tracon.md:557` ve `UseGoogleImages` XML
`<example>`'ı yeteneği çalışır gösteriyor ve hiçbir yerde uyarı yok. Kusurun
kendisi kullanıcı kararına bırakılmıştı; **doküman çelişkisi bırakılmamıştı**.

#### 🟡 3 — 20 paketin release-notes bağlantısı, "yayınlanmadı" diyen bir sayfaya gidiyor

20/20 `.nuspec`:
`<releaseNotes>https://tracon.dev/reference/changelog/#v1.0.0-preview.1</releaseNotes>`.
**Ölçüldü:** sayfa HTTP 200 döner, fakat `1.0.0-preview.1` **hiç geçmez** ve metin
"has not been released yet" der. `.nuspec` basıldıktan sonra **değiştirilemez**;
yalnız sayfa düzeltilebilir ⇒ site deploy'u tag'in **önüne** alınmalı.

#### Bu turda kapatılanlar

- §10 checklist'i OP-001/OP-005 ile hizalandı: trusted publishing policy kalemi
  `[x]`'ten **`[ ]`**'e çekildi (organizasyon sahipliğinde yeniden kurulmalı;
  policy'nin **"yeni paket" scope'u** ayrıca doğrulanmalı — ilk yayın 20 YENİ
  kimliktir). Owner modeli "kişisel hesap" yerine **organizasyon `Tracon`** yazıldı.
- Manuel kabul turu kalemi gerçeğe çekildi ve K-835 açık kalem olarak eklendi.
- `dependabot.yml`'a beşinci ekosistem: **`packages/tracon-client`** — sevk edilen
  npm paketinin kendi ağacını üç ekosistemin hiçbiri görmüyordu.

#### Kapının koşmadıkları (bu tur ölçülen sınırlar)

- `release-dryrun` kendi paketini üretip **atar**; `publish` ise **`pack` işinin**
  (`dotnet pack Tracon.slnx`) ürettiği artifact'ı glob ile basar. İkisi arasında
  fingerprint karşılaştırması **yok**. Bugün iki yol da tam 20 paket üretiyor
  (ölçüldü: `tests`/`samples`/`bench` + `Generators` hepsi `IsPackable=false`),
  yani kusur **gizil**, canlı değil.
- Kapı `package-manifest.json`'ı **yazar ama bir daha doğrulamaz**. Bu tur
  başında dizin karışık durumdaydı: 18 paket `49caa193`, 2 paket `225f1472`,
  manifest ise 20'sinin de `225f1472` olduğunu iddia ediyordu ve **18'inin hash'i
  tutmuyordu**. Dizin boşaltılınca sorun kayboldu ⇒ yerel prova kanıtı, dizin
  pristine değilse **güvenilmez**.
- Paketlenmiş kurulum yolu bu turda **elle ölçüldü ve çalıştı**:
  `dotnet tool install Tracon.Cli` (izole `--tool-path`) → `tracon --help` ✅;
  `dotnet new install Tracon.Templates.1.0.0-preview.1.nupkg` → `dotnet new
  tracon-api` ✅. 🚨 Ancak üretilen proje `<PackageReference Include="Tracon"
  Version="*-*" />` taşır (bilinçli varsayılan, `--TraconVersion` ile
  geçersiz kılınabilir) ve **ölçümde global cache'teki bayat
  `0.0.0-preview.0.789`'a çözüldü** — `1.0.0-preview.1`'e değil.

---

### Önceki karar — 2026-09-19 (`nuget-danismani`, yayın turu)

**✅ Yayınlanabilir — `1.0.0-preview.1`.** Açık 🔴 yoktur ve bu tur ilk kez
**tag atılacak commit'in kendisinden** üretilmiş artifact kanıtına dayanır.
Karar testlerin yeşil olmasına değil, paketin ölçülmüş davranışına dayanır.

#### Neden 2026-09-02 kanıtı devralınamazdı

| Ölçüm | Sonuç |
|---|---|
| Son prova tabanı (KN-018) | `b46978a7`, 2026-09-02 |
| O günden beri | **498 commit** · `src/` içinde **1420 dosya**, +66.981/−31.667 |
| Public yüzey dosyası | 33 `PublicAPI.Unshipped.txt` değişti |
| Migration | **28 yeni** |
| Ürün adı | `AgentPrism` → `Tracon` (Faz 162) |

#### Bu turun artifact kanıtı (5.–7. seviye)

`python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` → **çıkış 0**.
Prova, dizin **boşaltıldıktan** ve ağaç **commit edildikten** sonra koştu;
üretilen `package-manifest.json` commit `225f1472`'yi taşır — yani kanıt
tag'lenecek ağacın kendisine aittir, bir yakınına değil.

| Ölçüm | Komut | Sonuç |
|---|---|---|
| Paket kimliği | `ls *.nupkg` · `package-manifest.json` | **20/20**, tek sürüm hattı `1.0.0-preview.1`, manifest commit `225f1472` |
| Sembol paketi | `ls *.snupkg` | 18 — eksik ikisi `Tracon` (meta, assembly yok) ve `Tracon.Templates` (içerik paketi). **RK-006 kapandı:** meta paket artık PDB'siz `.snupkg` ÜRETMİYOR (KN-015'te üretiyordu) |
| K-008 ön sürüm sınırı | Tüm 20 `.nuspec` | Tracon dışı ön sürüm bağımlılığı **yalnız** `Tracon.AspNetCore`'da: A2A 1.0.0-preview2, MAF Hosting ×3 preview, Hosting.OpenAI **alpha**. K-008'in kendi metni alpha'yı zaten adlandırır ve `1.0.0` stable'ı MAF GA'ya bağlar — `preview.1` bu sınırı ihlal etmez |
| Paket metaverisi | `unzip -l` ×20 | **20/20** paket README + icon taşıyor |
| XML doküman | `unzip -l` | TFM başına `.dll` + `.xml` (net8.0 · net9.0 · net10.0) |
| Test paketi sızıntısı | Tüm `.nuspec` | `Tracon.Testing*` bağımlılığı **sıfır** üretim paketinde; meta paket 6 paket derler (AspNetCore · Mcp · OpenAI · PostgreSql · UI · Workflows) |
| Dış tüketici | Prova, izole `NUGET_PACKAGES` + exact sürüm | **6/6 sample geçti** (BL-052 sonrası altı) + `provider/source/generated-tool AOT smoke passed` |
| npm kanalı | `npm publish --dry-run` (elle doğrulandı) | `@tracon/client@0.0.0`, 23 dosya, 99,5 kB — npm **mevcut** (10.9.8), sessizce atlanmadı |
| `Tracon.Cli` 28 MB | `unzip -l` | Açıklandı: çok-RID yerel SQLite (`win-x64`, `win-arm64`, `linux-ppc64le`, …) + `Microsoft.Data.SqlClient` win/unix. `dotnet tool` üç depolama sağlayıcısına da migration koşmak zorundadır; boyut kusur değil |

#### Bağımsız çapraz ölçümler

| Soru | Sonuç |
|---|---|
| Yeniden adlandırma sevk edilen yüzeyde iz bıraktı mı | `src/`, `README.md`, `docs-site/src` → **0 `AgentPrism`** |
| Uygulanmış migration değiştirildi mi (immutability) | 2026-09-02'den beri `M` **yok**, hepsi `A`. Kapı bunu bir kez kırmızı verdi ve `ffa9afbf` üç migration'ı manifest'e çapaladı |
| Sağlayıcılar arası capability asimetrisi | Migration **adları** farklı (PostgreSQL 51 · SQL Server 40 · SQLite 39) ama **tablolar aynı** — dosya birleştirmesi, boşluk değil |
| Depolama seam'i simetrik mi | Üç sağlayıcı da **aynı 32 contract**'ı türetiyor, fark **sıfır** |
| Sevk edilen XML'de iç referans | **0** (`K-`/`F-`/`MT-`/`docs/NN-`; ilk taramanın 14 eşleşmesi `UTF-8` yanlış pozitifiydi) |
| Shipped baseline | 0 giriş — K-603 yürürlükte, yüzey daraltmak hâlâ ucuz |
| Unshipped tip | 765 (676 → 763 → 765). Artış UR-003'ü değiştirmez; freeze GA'dadır (KG-016) |

#### Ne KOŞMADI — kapının kendi sınırları

Prova yeşildir, fakat şunları **ölçmez** ve bu bilinçli kabullerdir:

- Paket **içeriğinin** doğruluğunu (yalnız kimlik ve metaveri) ve XML dokümanın
  **doğruluğunu** (yalnız varlığını)
- Gerçek Source Link kaynak çözümlemesi — repo public olmadan ölçülemez (RK-013,
  adım 7'de kendiliğinden kapanır)
- `Tracon.Cli` dışındaki paketlerin AOT davranışı (AOT smoke tek sample'dır)
- Güvenlik taraması konu 11 ve 12 **hiç koşulmadı**, tarama tabanı 477 commit
  bayat — KG-030 ile bilinçli kabul
- Reproducible build, package validation, tam artifact-seviyesi güvenlik matrisi
  — GA hattı (§13)

#### Açık 🟡'ler — yayını engellemez, GA'yı bekler

BL-007 · BL-017 · BL-022 · BL-023 · BL-030 · BL-044 · BL-055 ve kısmi kalemler.
Hepsi **doğrulama** boşluğudur (reusable contract testi · dış sample · metric),
spesifikasyon boşluğu değil; KG-019 bunları bilerek GA'ya taşıdı ve gerekçe
bugün de ölçüldü: `PublicAPI.Shipped.txt` **boştur**.

**BL-022 yeniden ölçüldü** çünkü kümenin en yüksek riskli kalemidir
(`IMcpOAuthCoordinator`, kiracı izolasyonu + CSRF). Testte hâlâ **sıfır**
referans var. Ancak Adım 7'nin 7. sorusu belirleyicidir — **kusur ölçülmedi:**
`state` 32 bayt CSPRNG'dir (`RandomNumberGenerator.GetBytes(32)`, 64 hex),
`_pending` eşzamanlıdır, süre aşımı temizlenir, ve tekrar gönderilen bir
`state` ikinci bir yetkilendirme üretmez — `TrySetResult` zaten tamamlanmış
görevi değiştirmez, çağrı **aynı** sonucu döner. Boşluk doğrulamadır, kusur
değil: **🟡 kalır.**

🟢 **Doküman kesinliği (yeni):** `IMcpOAuthCoordinator`'ın XML'i `state` için
"single-use" der; `CompleteAsync` kaydı `TryGetValue` ile okur, tüketmez —
silme `RunAuthorizationAsync`'in `finally`'sindedir. Gözlemlenebilir davranış
idempotenttir, yani doküman **yanlış değil kesin değil**. GA turunda
netleştirilir.

#### En küçük güvenli yayın kapsamı

Daraltma **önerilmez**. 20 paket UR-002 ile kullanıcı kararıdır, tek sürüm
hattından çıkar ve meta paket zaten yalnız altısını derler — tüketicinin
karşılaştığı yüzey paket sayısı değil meta pakettir. Bir paketi çıkarmak
sürüm hattını böler ve kazancı yoktur.

---

### Güncel karar — 2026-09-16 (yayın sırası ve dört ürün kararı)

**❌ Bugün tag atılmaz — açık 🔴 olduğu için değil, sıra kullanıcı kararıyla
yeniden dizildiği için.** Bu tur bir yayın provası değildir; kalan yolu
sabitler. Açık 🔴 yoktur. 2026-09-14 kararı da **devralınamaz**: o günden beri
49 commit geçti (Faz 166–175, aralarında kapasite ölçümü, tehdit modeli ve
denetim izi yazma politikası var).

#### Bu turun ölçümleri

| Ölçüm | Komut | Sonuç |
|---|---|---|
| Çalışma ağacı | `git status --short` | Temiz; HEAD `340d4aef` |
| Uzak durum | `git branch -vv` | `main` **`origin`'in 7 commit önünde** — GitHub'da olmayan bir commit'e tag atılamaz |
| CHANGELOG | `grep -n "^## \[" CHANGELOG.md` | Yalnız `## [Unreleased]`. `3d992233` sürüm bölümünü açmış, `6cfbc2d3` **bilerek geri almıştır**: sürüm bölümü paketler basıldığı gün yazılır. Kapı burada durur ve bu tasarlanmış davranıştır |
| Faz durumu | `docs/YOL-HARITASI.md` | Faz 176 · 177 · 178 📋 Planlandı; üçünün de plan onayı yok |
| Manuel kabul seti | `ls docs/manuel-test/*.md` · `kosumlar/` | 36 aile; son tam tur **2026-08-13** ve yalnız 25 aile. **11 aile (26–36) hiç koşulmadı** |
| Doküman kapısı | `dokuman-bakim.py --denetle` | ✅ temiz; `docs/**.md` %14 boş |
| Secret kapısının kapsamı | [`kapi.py`](../scripts/kapi.py) `find_secrets` | ✅ **kapandı 2026-09-19.** Geçmiş `gitleaks` ile ayrıca tarandı (RK-014) ve kapının **iki** boşluğu düzeltildi: deseni ürünün kendi `ap_*` formatını tanımıyordu, kapsamı `arsiv`/`manuel-test` ağaçlarını hiç yürümüyordu. Tarama artık iki katmanlıdır (KG-031) |

#### Alınan dört ürün kararı 👤

KG-026 (faz sırası) · KG-027 (manuel tur kapsamı) · KG-028 (repo görünürlüğü) ·
KG-029 (sürüm numarası) — gerekçeleri §11'dedir.

#### Sabitlenen sıra

| # | İş | Yürüten | Bitti ölçütü |
|---|---|---|---|
| 1 | **Tam manuel kabul turu — 36 aile** · ✅ **BİTTİ (2026-09-18)** | `manuel-test-kosumu` | 36/36 aile kaydı `arsiv/manuel-test-kosum-2026-09/` altında; 1856 benzersiz case — 1693 Geçti · 35 Kaldı · 107 Beklemede · 18 Atlandı · 3 işaretsiz. Kod tur boyunca `7e3a4de7`'de donuk kaldı, merge sonrası da donuk (ölçüldü) |
| 2 | Turun bulduğu kusurlar · ✅ **BİTTİ (2026-09-19)** → [KAPANIŞ PLANI](arsiv/manuel-test-kosum-2026-09/KAPANIS-PLANI.md) | `kusur-giderme` | 22 ailenin 22'si kapandı; 48 kusur kaydının hepsi kapandı. Kapanış ölçümü: **1.859 case — 1.813 Geçti · 26 Beklemede · 19 Atlandı · 1 Kaldı** (`MT-MM-121`, K-835: `Tracon.Google` görsel yolu, düzeltme kullanıcı kararına bırakıldı). 26 açık kalemin 26'sı + kayıt bloğu olmayan 8 case gerekçesiyle [`00-INDEKS.md` §7.2](manuel-test/00-INDEKS.md)'ye yazıldı. Üç kalıcı karar: K-833 · K-834 · K-835 |
| 3 | **Public öncesi geçmiş denetimi** · ✅ **BİTTİ (2026-09-19)** | Kullanıcı + `nuget-danismani` | 1065 commit / 53 MB `gitleaks git` ile tarandı: **gerçek credential sıfır** — 127 bulgunun tamamı yer tutucu, test fixture veya migration SHA'sı. Bağımsız doğrulama: `sk-proj-`, `sk-ant-api03-`, `AIza`, `AKIA`, `xoxb-`, `ghp_`, `npm_`, `glpat-` → **tüm geçmişte 0 eşleşme**; geçmişte commit edilmiş `.env`/`.pem`/`.pfx` yok; parola taşıyan tüm connection string'ler localhost docker. **Rotate gereken dış credential yok.** Yayımla/çıkar kararı: KG-030. Kapının kendi boşluğu: KG-031 |
| 4 | **Yayın turu** · ✅ **BİTTİ (2026-09-19)** | `nuget-danismani` Adım 1→8 | ✅ **Yayınlanabilir.** Prova `225f1472`'den koştu (çıkış 0): 20 paket, tek sürüm hattı, K-008 korunur, 20/20 README+icon, TFM başına XML, test paketi sızıntısı yok, 6/6 dış sample + AOT smoke, npm provası gerçek. Açık 🔴 yok; açık 🟡'ler doğrulama boşluğudur ve GA'dadır (KG-019). Tam blok §4'ün başındadır |
| 5 | **Sürüm kesimi** · ✅ **BİTTİ (2026-09-19)** | Elle | `CHANGELOG.md`'nin `## [Unreleased]` **başlığı** `## [1.0.0-preview.1] - <sevk tarihi>` olarak yeniden adlandırılır (gövde taşınmaz, başlık değişir), üstüne **boş** bir `## [Unreleased]` açılır, commit |
| 6 | **Kapılar** · ✅ **BİTTİ (2026-09-19, `ce23527b`)** | `kapi.py kapanis --taban <commit>` · `kapi.py yayin --kuru --surum 1.0.0-preview.1` | İkisi de **çıkış 0**. Kapanış dört turda yeşile döndü ve üç turun her biri GERÇEK bir kusur kapattı (bkz. §4). Prova: 20 paket + 18 sembol paketi, manifest `commit: ce23527b` · `dirty: false` · **38/38 hash elle yeniden hesaplandı**, 6/6 packed sample, AOT smoke, npm provası. 🚨 Bu satırdan SONRA atılan her commit kapıları geçersiz kılar |
| 7 | **Repo public + push** · ✅ **BİTTİ (2026-09-19)** | Kullanıcı | Ölçüldü 2026-09-20: GitHub API `visibility: public`, `origin/main` yerel `HEAD` ile aynı. 🚨 **Yan etki, planlanmamıştı ve bir engeli kaldırdı:** repo private'ken Free plan Actions dakikasını tükettiği için CI "recent account payments have failed" ile hiç başlamıyordu; public olduktan sonra standart runner ücretsiz olduğu için pipeline kendiliğinden geri geldi (14:11 ve 14:25 koşumları kırmızı, 16:00 koşumu koştu) |
| 8 | **Tag** · 🔄 **SIRADAKİ** | Kullanıcı | `git tag v1.0.0-preview.1 && git push origin v1.0.0-preview.1`; CI: build → pack + release-dryrun → npm-publish → publish (OIDC) → github-release. 🚨 **Ön koşul:** NuGet trusted publishing policy'si *pasif* kurulur ve "Activate for 7 days" ile açılır; kapalıyken tag atılırsa `npm-publish` basar, `publish` OIDC'de düşer ve `@tracon/client` NuGet'siz kalır |
| 9 | **Site deploy** (adım 8'den ÖNCE) + ilk 72 saat | `site-deploy.sh` · §14 | `scripts/site-deploy.sh` koştu ve **canlı** `https://tracon.dev/reference/changelog/` sayfasında `v1.0.0-preview.1` çapası GERÇEKTEN çözülüyor (`curl` ile doğrulanır). 20 `.nuspec`'in `releaseNotes` URL'i tam olarak o çapayı gösterir ve `.nuspec` basıldıktan sonra değişmez ⇒ sayfa tag'den önce canlı olmalıdır (A-10) |
| 10 | `preview.2` hattı | Faz zinciri | 🚨 **KG-026 pratikte tersine döndü:** Faz 176, 177 ve 178 `preview.1` ÖNCESİNDE (2026-09-16) tamamlandı ve `preview.1` içindedir. Bu adım artık A-8 · A-15 · A-16 · A-17 · A-18 · A-28 ve UR-003'ü taşır |

🚨 **Tag `origin`'e gider.** Repo'nun ikinci bir remote'u vardır
(`intelera` → `StudyZoneInt/Tracon`). Trusted publishing policy'si
`farukatasoy/Tracon` + `ci.yml` + `environment: nuget` üçlüsüne bağlıdır;
başka bir remote'a atılan tag yayın üretmez.

🚨 **Adım 5 bir yeniden adlandırmadır, yeni bir bölüm yazmak değildir.**
Notlar zaten `## [Unreleased]` altında birikir; kesim o başlığı sürüme ve sevk
tarihine çevirir. Prova (`kapi.py yayin --kuru`) ve `github-release` işi **aynı
ayrıştırıcıyı** ve aynı yedeği kullanır (K-825): sürüm bölümü yoksa ikisi de
`## [Unreleased]`'i okur. Bu yüzden prova Adım 5'ten **önce** de yeşil olabilir
ve provanın yeşili bir şey ifade eder; ikisinden yalnız biri yedeği kullansaydı
prova yeşil, release gövdesi boş olurdu.

Kapının koruduğu şey "bölüm var mı" değil, **notsuz sürüm çıkmasın**dır: ikisi
de boşsa prova kırmızıdır. Adım 5'in kendisi yine de atlanamaz — atlanırsa
yayınlanan sürümün notları `Unreleased` başlığı altında kalır ve bir sonraki
sürüm onları ikinci kez sevk eder.

---

### Güncel karar — 2026-09-15 (tüketici geri bildirimi turu)

**Yayın kararı DEĞİŞMEDİ** — aşağıdaki 2026-09-14 girdisi yürürlüktedir. Bu tur bir
yayın provası değil, dış bir tüketicinin **yalnız `tracon.dev` okuyarak** ürettiği
değerlendirmenin ölçümüdür. Değeri iki yönlüdür: sitenin ürettiği yanlış sonuçlar
sitenin kusurudur, ve kanıtla çürütülen iddialar bir daha açılmamalıdır.

#### Bu turun bulguları

| # | Bulgu | Seviye | Durum |
|---|---|---|---|
| 1 | `concepts/workflows.md` MAF'ın motorunu ismen anmıyordu; `Microsoft.Agents.AI.Workflows` adı api dışı 357 sayfanın **1'inde** geçiyordu. Tüketici bundan "Tracon kendi workflow engine'ini yazıyor" sonucunu çıkardı | 🟡 | ✅ **kapandı 2026-09-15** — atıf sayfanın ilk ekranına taşındı |
| 2 | Denetim izi garanti ayrımı yayımlanmamıştı: `capabilities.md` "unit of evidence" diyor, uyarı ise `governance.md`'nin 263. satırındaki yan nottaydı | 🟡 | ✅ **kapandı 2026-09-15** — K-776; `What is guaranteed to be written` bölümü |
| 3 | O notun kendisi **yanlıştı**: "Approvals and skill scripts are the only two places" diyordu, ölçülen sayı **altı** | 🟡 | ✅ **kapandı 2026-09-15** |
| 4 | `SECURITY.md` kökte var ve iyi, ama site ona **hiç link vermiyordu** — tüketici için zafiyet bildirim yolu yok hükmündeydi | 🟡 | ✅ **kapandı 2026-09-15** — `reference/security-policy.md` + `check-content.mjs` senkron kapısı |
| 5 | Güvenlik sınırı ifadesi 15 `api/` sayfasına dağılmış 22 geçişti; toplu bir liste yoktu | 🟢 | ✅ **kapandı 2026-09-15** — `getting-started/security.md` § *The boundaries Tracon enforces* |
| 6 | `api/index.md` "16 packages" derken `packages.md` "Twenty packages" diyordu | 🟢 | ✅ **kapandı 2026-09-15** — üreteç "the N packages that ship a library API of their own" yazıyor |
| 7 | Job queue'nun MAF durability uzantısına göre konumu **hiçbir yerde** yazılı değil (site: 0 eşleşme; karar defteri: 0 kayıt) | 🟡 | ✅ **kapandı 2026-09-15** — K-778; `guides/background-work.md` § *What this queue is, and what it is not* |
| 8 | Dört `WriteAuditOrThrowAsync` kopyası tek politika değil; `ApprovalEndpoints.cs:345` kopyayı kendi yorumunda kabul ediyor | 🟡 | ✅ **kapandı 2026-09-15** — [Faz 171](arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md); BL-047 aynı fazda kapandı |
| 9 | Tehdit modeli dokümanı yok (`threat model`/`STRIDE` → `docs/` genelinde 0) | 🟡 | **Planlandı 2026-09-15** — [Faz 172](arsiv/fazlar/172-TEHDIT-MODELI.md) |
| 10 | SBOM üretimi ve paket imzalama yok | 🟢 | **GA hattı** — K-777, gerekçesiyle ertelendi |
| 11 | `KARARLAR.md`'de K-662…K-777 arası kararlar tek bir kod bloğunun içinde kalıyor; tablo olarak render olmuyor | 🟢 → **🔴 çıktı** | ✅ **kapandı 2026-09-15** — "yalnız okunabilirlik" yargısı YANLIŞTI. Ölçüldü: **101 karar** §3'ün şablon kod bloğundaydı, yani `kararlar_denetle`'nin üç kontrolü de (yinelenen numara · tabloyu kesen boş satır · sıra dışı numara) onlara **kördü** — kapı §2 ile sınırlı, indeks üreteci ise dosyanın tamamını okuyor. Bedel gerçekti: **iki farklı karar K-703'ü paylaşıyordu** ve iki üretilen indeks farklı bir K-703 gösteriyordu. Satırlar §2'ye taşındı (§2: 661 → **783 kalem**), çakışma tarih kuralıyla çözüldü (F-211 → **K-783**, beş referansı taşındı) ve sınıf `_bolum_disi_karar_satirlari()` kapısıyla kapatıldı (mutasyonla doğrulandı) |

#### Kanıtla çürütülen beş iddia — yeniden açılmaz

| İddia | Çürüten kanıt |
|---|---|
| "Kendi workflow engine'ini yazıyor" | `WorkflowRunner.cs:964` MAF `InProcessExecution.RunStreamingAsync`; derleyici `AgentWorkflowBuilder`'ın beş fabrikası; checkpoint MAF `CheckpointManager.CreateJson`. Tracon'un workflow public yüzeyi **5 tip** |
| "Public API yüzeyi çok büyük, 1.0 öncesi diyet gerekir" | Sayı doğru (763 tip), çıkarım hedef dışı: Abstractions'ın 415 tipinin **205'i record, 61'i enum**. Bu tur bunu ÜÇÜNCÜ kez ölçtü — 2026-09-14 girdisi ve K-601 (Faz 96'da 96 yaprak tip `internal`) aynı sonucu vermişti |
| "`RequireProductionProfile()` Production'da default olmalı" | **K-773**: profil kümesi bir sürüm sözleşmesidir; otomatik açılan kapı, kümeye eklenen her yeni anahtarda çalışan kurulumları durdururdu |
| "Migration startup'tan ayrılmalı" | Zaten iki yol var ve belgeli: `AutoApplyMigrations:false` + `tracon migrate` (`guides/production.md:168,202`) |
| "PolyForm lisansı adoption friction üretir" | **K-740/741**: iş kararı, friction tahsilat mekanizmasının kendisidir. `reference/licensing.md` eşiği, SPDX kimliğini ve 32 günü zaten yazıyor |

⚠️ **Doküman bütçesi — bu turun en sert kısıtı.** K-776, K-777 ve K-778 sonrası
`docs/KARARLAR.md` 419.690/420.000 bayt: **310 bayt boş (%0)**. Bir sonraki karar
eklenmeden ÖNCE `karar-damit` koşulmalıdır; bugün bir `K-*` daha yazmak bütçeyi aşar.
Aynı turda § *Bu turun bulguları* 11. satırdaki kod-bloğu kusuru da ele alınabilir —
ikisi aynı dosyaya dokunur.

---

### Güncel karar — 2026-09-14 (`nuget-danismani`, dış inceleyici turu)

**⚠️ Bugün tag atılmaz — açık 🔴 olduğu için değil, HEAD'de yayın kanıtı
eksik olduğu için.** Aşağıdaki 2026-09-03 kararı **devralınamaz**: o günden
beri 184 commit ve ~30 faz geçti, aralarında ürün yeniden adlandırması
([Faz 162](arsiv/fazlar/162-TRACON-YENIDEN-ADLANDIRMA.md)) ve lisans modeli
değişikliği ([Faz 160](arsiv/fazlar/160-LISANS-MODELI-VE-PAKET-METAVERISI.md))
var. Paket kimliğini ve metaverisini en çok etkileyen iki değişiklik tam da
bunlardır.

**Kapı HEAD'de koşuldu** (`kapi.py yayin --kuru --surum 1.0.0-preview.1`):

| Adım | Sonuç |
|---|---|
| 20 paketin tamamı üretildi | ✅ |
| Tek sürüm hattı, istenen sürüm zorlandı | ✅ `1.0.0-preview.1` |
| `CHANGELOG.md` bölümü | ❌ **durdu** — `## [1.0.0-preview.1]` yok |
| Fazla paket · metaveri/K-008 · artifact kimliği · npm · beş extension sample · AOT smoke | ⬜ **koşmadı** (CHANGELOG kapısından sonra gelirler) |

`CHANGELOG.md`'nin `Unreleased` bölümü tam yazılmıştır ve "ilk gerçek yayın
kendi bölümünü alacak" der. Kapı tasarlandığı gibi davrandı; eksik olan bir
kusur değil, **kullanıcının sürüm kesme kararıdır**. Kapı temiz ağaç ister,
yani kesim bir commit gerektirir.

#### Bu turun bulguları

| # | Bulgu | Seviye | Durum |
|---|---|---|---|
| 1 | Kalan altı kapı adımı HEAD'de koşmadı | 🔴 karar için | Açık — CHANGELOG kesimi + kapı koşumu kapatır |
| 2 | 77 arayüz `lifetime`/`tenant`/`delivery` sözleşmesini yazmıyor (172 boyut, `seam-contract-baseline.txt`) | 🟡 | GA hattı; UR-003 ile aynı turda |
| 3 | `production.md` tablosunda dört güvenlik anahtarı yoktu | 🟡 | ✅ **kapandı 2026-09-14** — beş satır + checklist kalemi eklendi |
| 4 | Options düzeyinde production doğrulayıcısı yok | 🟡 | Açık — preview.2 hattı, `RequireProductionProfile` önerisi |
| 5 | F-171 sürüm damgası sapması | 🟢 | ✅ **kapandı 2026-09-14** — damga yeniden ölçüldü, kapı genişletildi |

**Dış inceleyicinin beş maddesi ölçüldü.** "411 public type" sayısı tam
isabettir ama çıkarımı yanlış hedeftedir: 205'i record, 59'u enum — üçüncü
tarafın implement ettiği yüzey 84 arayüştür ve asıl boşluk bulgu 2'dir.
"Scope freeze" zaten uygulanıyor: son 11 fazın **9'u** public API büyütmedi.
"Performans kanıtı" haklıdır ve [Faz 166](arsiv/fazlar/166-HTTP-KAPASITE-OLCUMU.md) olarak
planlıdır; inceleyicinin yedi metriğinden beşini kapsar, **PostgreSQL write
amplification** ile **multi-node lease** kapsam dışıdır.

**Önerilen sıra:** F-171 ✅ → CHANGELOG kesimi + kapı sonuna kadar → tag →
preview.2'de production profili ve Faz 166.

---

### Güncel karar — 2026-09-03 (`nuget-danismani`, tag öncesi tur)

**✅ Yayınlanabilir. Kalan tek adım kullanıcının tag onayıdır.** Açık 🔴
yoktur ve dört kapının dördü de yeşil koştu. Bu tur bir 🔴 açtı ve aynı turda
kapattı (BL-056), dört operasyon kararını sabitledi (OP-008/009/011 + §14) ve
kapanış kapısını kırmızıya çeken F-180'i kök nedeninden kapattı (K-660).

| Ölçüm | Sonuç |
|---|---|
| `git log --since=2026-09-02` | 2026-09-02 provasından **sonra** bir commit: `ef06fc37`, `Tracon.Core`'da davranış değişikliği (K-658). Prova onu görmedi |
| `curl` × 3, anonim | `github.com/farukatasoy/Tracon` → **404**; `.../blob/v1.0.0-preview.1/CHANGELOG.md` → **404**; `tracon.dev` → **200** |
| `grep -r "github.com/farukatasoy" docs-site README.md` | **0 bağlantı** — ölü bağlantı yalnız paket metaverisindeydi, sevk edilen metinde değil |
| `grep -rl "new Meter(" src` | 3 dosya. BL-044 ve BL-047 hâlâ açık (ölçüldü, GA hattında) |

**BL-056 bu turun bulgusudur ve sınıfı §10'un kendi kapısıdır:** checklist'teki
`[x] ... repository ve project URL varlığı doğrulandı` kalemi alanların `.nuspec`
içinde **var olduğunu** ölçüyordu, **çözüldüğünü** değil. Ağa çıkmayan bir kapı
(K-604) bunu yapısal olarak göremez; kanıt merdiveninin 5. seviyesi 6. seviyeyi
kapsamaz.

**Yapılan iş (2026-09-03):** CHANGELOG'a K-658 davranışı ve tag tarihi yazıldı ·
`PackageProjectUrl` ve `PackageReleaseNotes` doküman sitesine çevrildi (K-659) ·
site release-notes sayfası kök `CHANGELOG.md`'den **üretilir** oldu
(`docs-site/scripts/build-changelog.mjs`, ayna kopya yok) · `versioning.md`'nin
private repo yüzünden yanlışlaşan dört iddiası düzeltildi ("source diff'i oku" →
release notes) · `SECURITY.md` ve issue şablonları eklendi.

**Taze kapı koşumu (2026-09-03, yukarıdaki değişikliklerden sonra):**

| Kapı | Sonuç |
|---|---|
| `kapi.py yayin --kuru --surum 1.0.0-preview.1` | ✅ `EXIT=0` — 20 paket · `npm publish --dry-run` · altı sample exact sürüm ve izole `NUGET_PACKAGES` ile · Native AOT smoke publish **ve çalıştırma** (`provider/source/generated-tool AOT smoke passed`) |
| `kapi.py kapanis --taban 77a60970` | ❌ `EXIT=1` — 10 adımın **dokuzu** yeşil; yalnız `dotnet test Tracon.slnx` düştü, **tek** test: `UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript` |
| `dotnet test` yalnız E2E projesi (izole) | ✅ **57/57 yeşil** |

**Bu, kayıtlı F-180'dir; bu turun ürünü değildir.** Aynı test, aynı 30000 ms
Playwright zaman aşımı (`GetByTestId("voice-transcript")`), aynı koşul: yalnız
tam çözüm koşumu. Bugünkü değişiklikler `Tracon.Core`'un yapısal yanıt
yolu, paket metaverisi ve `docs-site`'tır — ses veya playground yoluna
dokunulmadı. 2026-09-02 turunda aynı kapı E2E'yi 57/57 geçmişti, yani kusur
aralıklıdır.

**F-180 kapatıldı (2026-09-03, `kusur-giderme`, K-660) — ve yalıtım kusuru
değil, sevk edilen bir ürün kusuru çıktı.** `VoiceConversationDriver.Commit`
ses gelmeden ulaşan bir `commit`'te hiçbir çerçeve göndermeden dönüyordu;
istemci gönder'e basıldığı anda kendini `'thinking'`e alıp kaydediciyi
durdurduğu için panel kalıcı asılıyor ve mikrofon bir daha açılmıyordu. Yük
yalnız pencereyi genişletiyordu. **Sınıf taraması ikinci ve üretimde daha olası
vakayı buldu:** transcriber boş metin döndüğünde `ProcessTurnAsync` aynı sessiz
dönüşü yapıyordu. İkisi de yeni `idle` sunucu çerçevesiyle kapatıldı; ikisi de
red→green kanıtlandı.

**Düzeltme sonrası kapı koşumu (2026-09-03):**

| Kapı | Sonuç |
|---|---|
| `kapi.py kapanis --taban 77a60970` | ✅ `EXIT=0` — 10/10 adım; **E2E 57/57**; toplam test koşumu 464,92 sn |
| `kapi.py yayin --kuru --surum 1.0.0-preview.1` | ✅ `EXIT=0` — 20 paket · `npm publish --dry-run` · altı sample · Native AOT smoke publish ve çalıştırma |

**Kalan tek adım:** `1.0.0-preview.1` için açık tag onayı. Tag gününde
`CHANGELOG.md`'nin tarihi (`2026-09-03`) yeniden doğrulanır.

### Önceki karar — 2026-09-02 (`nuget-danismani`, Faz 129–135 sonrası)

**✅ Teknik olarak yayınlanabilir.** Açık 🔴 yoktur. Kalan tek şey hesap ve
operasyon kararlarıdır (OP-008/009/011, §14) — bunlar repo dışı kullanıcı
işidir ve tag için zaten açık onay gerekir.

> Bu karar aynı gün **iki kez** verildi. Sabah turunda iki 🔴 ölçüldü
> (BL-053, BL-054); ikisi de aynı gün kapatıldı ve prova sonuna kadar yeşil
> koştu. Tarihçe aşağıdadır — silinmedi, çünkü asıl ders kusurlarda değil,
> **kapanış kapısının onları görememesinde**.

| Ölçüm | Sonuç |
|---|---|
| `python3 scripts/kapi.py kapanis --taban HEAD` (2026-09-02) | ✅ 10/10 adım yeşil; E2E 57/57; toplam test koşumu 506 sn |
| `kapi.py yayin --kuru --surum 1.0.0-preview.1` — **1. koşum** | ❌ `EXIT=1`, BL-053 |
| `kapi.py yayin --kuru --surum 1.0.0-preview.1` — **2. koşum** (düzeltmelerden sonra) | ✅ `EXIT=0`; 20 paket · `npm publish --dry-run` · **altı** sample 169 test · Native AOT smoke publish **ve çalıştırma** (`provider/source/generated-tool AOT smoke passed`) |

Kapanış kapısı yeşilken yayın provasının düşmesi bu turun asıl bulgusudur ve
**BL-053**'ün sınıfını tanımlar: `samples/` hiçbir çözüm dosyasında değildir
(`grep -c Samples Tracon.slnx` → `0`), dolayısıyla `dotnet test
Tracon.slnx` onları çalıştıramaz. Sevk edilen bir sözleşmeyi genişleten faz,
o sözleşmenin dış referans implementation'ında geçtiğini kapanış kapısıyla
kanıtlayamaz. Kural K-657 ile `faz-tamamlama` Adım 1'e eklendi.

**İkinci koşumda altı sample'ın altısı da ölçüldü** (ilk koşum ilk düşen
sample'da durmuştu): `FileRunStore` 92, `CustomModelProvider` 38,
`CustomRunJudge` 11, `CustomAgentSource` 15, `CustomTool` 8,
`CustomJobHandler` 5 — hepsi exact sürüm ve izole `NUGET_PACKAGES` ile.

**En küçük güvenli yayın kapsamı değişmedi** (20 paket, `preview`). Sürüm
numarası `1.0.0-preview.1` yeniden kullanılabilir: hiç yayınlanmadı.

**Faz 129–135'in bu kayda etkisi:** **BL-044** kısmen kapandı — Faz 133
`Scheduling` kümesine `tracon.job.executions`, `tracon.job.duration` ve
opt-in `tracon.job.queue.depth`'i sevk etti. `Webhooks`, `Coordination`,
`Idempotency` ve `Triggers` hâlâ metriksizdir. **BL-047** (audit-write
başarısızlık metriği) ölçüldü, hâlâ açık.


**❌ Yayınlanmamalı — şu an.** Yayın türü (`preview`, UR-001) ve paket kapsamı
(tam entegrasyon seti, UR-002) kullanıcı tarafından sabitlendi. 22 sütunlu seam
matrisi 11/11 kümede tamamlandı (§15) ve **4 bağımsız 🔴 preview-blocker kusur
sınıfının tamamı kapandı**:

| # | Kusur sınıfı | Kayıtlar | Durum (2026-08-27, `kusur-giderme` sonrası) |
|---|---|---|---|
| 1 | BYOK credential case-sensitivity | BL-006 | ✅ **KAPANDI.** Düşen testle yeniden üretildi → normalizasyon + 3 migration + contract case'leri → yeşil. K-639. Sınıf taraması: 4 aday temiz, `provider` tek outlier |
| 2 | Drain/yeni-run yarışı | BL-026 | ⬇️ **🟡'ye indirildi.** Pencere var ama iş kaybı yok: Kestrel request draining (HTTP) ve `WaitForRunningJobsAsync` (job) boşluğu kapatıyor; drain zaten varsayılan **kapalı**. Ölçülmüş repro üretilemedi |
| 3 | Ham exception → kalıcı/dışa açık durum | BL-027, BL-037 | ✅ **KAPANDI (Faz 119).** Sınıf taraması bilinen 2 vakanın üstüne 19 vaka daha bulmuştu (§16); uygulama sırasında **5 ek vaka** daha bulundu (`EgressAddressValidator`, `ConversationBranchService`, `RetentionJobHandler`, `RetentionExecutor`, `ModelRunJudge`) — toplam **26 vaka** kapatıldı. `SafeErrorText` (K-640) + `RawExceptionTextSiteTests` mimari cırcır kapısı 22. sızıntıyı otomatik yakalar |
| 4 | `IJobHandler` sözleşmesi at-least-once'ı söylemiyor | BL-041 | ✅ **KAPANDI (Faz 120).** `ExecuteAsync` ve `JobContext.Items`'ın XML dokümanı at-least-once'ı, süzülmemiş `Items`'ı ve handler sorumluluğunu açıkça yazıyor. `JobHandlerContract` (üç yerleşik handler + `Tracon.Samples.CustomJobHandler` dış sample'ı) kuralı kilitliyor; `JobLeaseExpiryTests` lease süresi dolunca gerçekten yeniden kiralandığını ve item listesinin süzülmeden geri geldiğini ölçüyor |

Bunların **hepsi** `kusur-giderme`'ye devredilmeden (ve kusur sınıfı taraması
tamamlanmadan) preview yayınlanamazdı. **Dördü de kapandı** (BL-006, BL-026
🟡'ye indirildi, BL-027/BL-037 Faz 119, BL-041 Faz 120); açık 🔴 kalmadı.
Geri kalan 35× 🟡 ve 17× 🟢 bulgu **1.0 blocker'ı değil**, ilk preview'ı
engellemez — release notes'a ve sonraki iterasyon planına girer (bkz. §6 tam
liste). Nihai "yayınlanabilir" kararı yine de verilmedi: §7'deki public API
freeze taraması (UR-003) ve §8'deki NuGet.org operasyon kararları (OP-001..009)
açık — bu ikisi bir sonraki `nuget-danismani` turunun konusudur.

Paket artifact'i (dry-run, exact sürüm, 20 paket, 160 sample testi, Native AOT
smoke) teknik olarak yeşildir — bu yalnız **başlangıç** kanıtıdır, seam
matrisinin bulduğu kusur sınıflarını geçersiz kılmaz.

**UR-003 bu turda 🟡'ye indirildi (KG-016).** 680 tiplik public yüzeyin
freeze taraması bir preview blocker'ı **değildir**, GA blocker'ıdır. Üç kanıt
aynı yöne bakıyor: (1) sevk edilen tüketici sözleşmesi bunu zaten yazıyor —
`docs-site/src/content/docs/reference/versioning.md:11-13`, *"a narrowing or a
reshaped type is not treated as a breaking change until the family reaches
`1.0.0`"*; (2) **K-603** (kullanıcı kararı) `Shipped.txt`'i preview hattı
boyunca boş tutar ve dolumu GA'ya erteler; (3) **K-602** (kullanıcı kararı) tek
sürüm hattını tam olarak "preview hattında yüzey küçültme kırıcı değişiklik
sayılmaz" gerekçesiyle seçti. Adım 7 filtresinde 3., 4., 6. ve 7. soruları
geçemiyor. Tarama, `Unshipped → Shipped` dolumuyla **aynı** GA turuna taşındı.

**Geçici en küçük güvenli yayın kapsamı:** `1.0.0-preview.1`, tam 20 paket
(UR-002 kararı). Teknik ön koşullar açık; yayın kullanıcı kararıyla 35× 🟡
sistemik hattın arkasına alındı (§13).

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

**2026-09-02 itibarıyla açık 🔴 yoktur.** BL-053 ve BL-054 aynı gün açıldı ve
kapandı; BL-055 açık bir 🟡'dir. Aşağıdaki 2026-08-28 maddelerinin çoğu blocker
değil, doğrulama kapısıdır.

| Kimlik | Durum | Bulgu veya soru | Seviye | Ölçülen kanıt | Sorumlu workflow | Doğrulama ölçütü |
|---|---|---|---|---|---|---|
| BL-001 | Tamamlandı | Exact release artifact'i üret ve temel kapıyı çalıştır | Blocker değil | Exact dry-run çıkış `0`; 20 paket, 160 sample testi ve AOT smoke yeşil | `nuget-danismani` | Tamamlandı |
| BL-002 | Tamamlandı | 20 public paketin tamamının ilk preview için gerekli ve yeterince olgun olup olmadığı bilinmiyor | Blocker değil — kapsam kararı verildi | UR-002: kullanıcı tam entegrasyon setini seçti | `nuget-danismani` | Kapsam sabit; olgunluk artık paket bazında değil seam bazında (BL-003) ölçülür |
| BL-003 | **Tamamlandı** | Extension seam sözleşmelerinin birbiriyle tutarlılığı ölçüldü — 11/11 küme, 78 seam | Blocker değil — ölçüm bitti, bulgular BL-006/026/027/037/041 (🔴) + BL-007…051 (🟡/🟢) olarak kaydedildi | §15 tam matris planı ve küme raporları | `nuget-danismani` | Tamamlandı — bkz. §4 nihai özet |
| BL-004 | **Tamamlandı (KN-022)** | NuGet.org hesap, owner ve credential modeli sabitlendi | Blocker değil — kapandı | Kişisel owner `farukatasoy`; OIDC trusted publishing; `Tracon*`; push-only; bir saatlik geçici key | Kullanıcı + yayın operasyonu | İlk başarılı publish policy aktivasyonunu kalıcılaştırır |
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
| BL-055 | **Açık** | `JobStoreContract` Faz 129 ve 133'te **+110 satır** case kazandı (lane filtresi, `GetQueueDepthAsync`) ama **hiçbir dış sample'ı yoktur** — `CustomJobHandler` `JobHandlerContract`'ı koşar, `JobStoreContract`'ı değil. Yeni case'ler paketlenmiş tüketiciye karşı hiç doğrulanmadı | 🟡 1.0 blocker | `grep -rn JobStoreContract samples/` boş; `git diff --stat` Faz 129–135 aralığında yalnız iki contract dosyası değişti | `nuget-danismani` → faz zinciri | `IJobStore` uygulayan bir dış sample eklenir ve `JobStoreContract`'ı exact sürümle koşar. BL-007/BL-017/BL-030 ile aynı aile |


## 7. Ürün ve public API kararları

| Kimlik | Konu | Durum | Bulgu veya soru | Ölçülen kanıt | Seçenekler | Alınan karar | Gerekçe | Risk | Workflow | Doğrulama | Tarih |
|---|---|---|---|---|---|---|---|---|---|---|---|
| UR-001 | Yayın türü | Tamamlandı | İlk dış yayın preview, rc veya stable mı olmalı? | KN-005 ve pre-release Hosting bağımlılıkları | `preview` / `rc` / `stable 1.0` | `preview` | Shipped baseline boş (0 giriş, 676 unshipped tip) ve `Tracon.AspNetCore` pre-release MAF bağımlılığı taşıyor — stable/RC taahhüdü bugün karşılanamaz; preview SemVer'de kırıcı değişikliğe izin verir | Yüksek | `nuget-danismani` | Kullanıcı kararı | 2026-08-27 |
| UR-002 | İlk hedef tüketici | Tamamlandı | Ürün anlatısı ve en küçük paket kapsamı hangi birincil persona için optimize edilmeli? | Repo bir control plane, provider, storage, transport, UI ve extension paketleri taşıyor | Yalnız çekirdek / tam entegrasyon seti / çekirdek + kanıtlanmış alt küme | Tam entegrasyon seti (20 paketin tamamı) | Kullanıcı, dry-run'da zaten kanıtlanmış tam kapsamı (20 paket, 160 sample testi, AOT smoke yeşil) korumayı seçti | Yüksek — audit yükü en geniş seçenek düzeyinde | `nuget-danismani` | Kullanıcı kararı | 2026-08-27 |
| UR-003 | Public API freeze | **Tamamlandı — GA'ya ertelendi** | Preview öncesinde hangi yüzey korunmalı veya küçültülmeli? | Ölçüldü 2026-08-27: 17 `PublicAPI.Shipped.txt` **0** satır, unshipped **8417** satır / **680** tip | Preview'dan önce tara / **GA turuna ertele** | **GA turuna ertelendi; seviye 🔴 → 🟡** | Sevk edilen `versioning.md:11-13` preview hattında yüzey daralmasının kırıcı sayılmadığını zaten söylüyor; K-603 `Shipped` dolumunu GA'ya erteledi; K-602 tek sürüm hattını bu gerekçeyle seçti. Faz 96 aynı erişilebilirlik ölçütünü koşup 96 tipi `internal` yapmıştı (K-601) — ikinci turun verimi düşük | Orta — GA'da yüzey büyükse daraltma pahalılaşır; azaltım: tarama `Unshipped → Shipped` dolumuyla aynı turda koşar | `nuget-danismani` (GA turu) | GA kapısında `Shipped.txt` dolumu | 2026-08-27 |

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
| OP-010 | npm/NuGet asimetrik kısmi yayın | **Tamamlandı (2026-08-28, KG-021/KN-021)** | Aynı `v*` tag'i `nuget-publish` ([`ci.yml:284`](../.github/workflows/ci.yml#L284)) ve `npm-publish` ([`ci.yml:319`](../.github/workflows/ci.yml#L319)) işlerini **paralel** tetikler (farklı `needs`). `tracon` npm scope'u bugün yok; `NPM_TOKEN` durumu repo dışında. Scope hazır değilse 20 NuGet paketi **kalıcı** yayınlanır, npm işi kırılır — ve sevk edilen doküman `npm install @tracon/client` diyor (`docs-site/src/content/docs/packages.md:70`, `guides/typescript-client.md:25`) | **Yol A + C.** **C uygulandı:** `publish` işi artık `needs: [pack, release-dryrun, npm-publish]` ([`ci.yml:293`](../.github/workflows/ci.yml#L293)) — geri dönüşü olmayan kanal (NuGet) EN SON basar; npm kırılırsa 20 paket hiç yayınlanmaz. Zincir kırılmaz: `npm-publish` var olan sürümü atlar, aynı etiket yeniden itilebilir. **A tamamlandı (KN-021):** npm scope ve `NPM_TOKEN` hazırlığını kullanıcı doğruladı; token yetkisi ilk publish işinde ölçülecek |
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
| RK-012 | **Kabul edildi** | Kişisel owner modeli (OP-001): 20 paketin sahipliği tek hesaba bağlıdır; devir paket başına elle yapılır ve hesap kaybı 20 kimliği birden etkiler | Orta | Düşük / yüksek | Kullanıcı bilinçli olarak kabul etti (2026-08-27). Azaltım: 2FA (OP-002) ve gerekirse sonradan organization'a devir | Yayın operasyonu |
| RK-014 | **Kapandı (2026-09-19, KG-030/031)** | Repo public yapıldığında **1065 commit'lik geçmiş** de public olur. Mevcut secret kapısı ([`kapi.py:183-209`](../scripts/kapi.py)) yalnız çalışma ağacını yürür ve `git log`'a hiç bakmaz; bugün temiz olan bir dosya geçmişte bir credential ile commit edilmiş olabilir | 🔴 Public öncesi | Bilinmiyor / **geri dönüşsüz** — public olduktan sonra geçmişi temizlemek pratikte imkânsızdır (fork, cache ve GitHub nesneleri kalır) | Koşuldu: `gitleaks git .` 1065 commit / 53 MB — gerçek credential sıfır, rotate gerekmedi. Kapının iki yapısal boşluğu (desen + kapsam) aynı turda kapatıldı ve mutasyonla doğrulandı (KG-031). 🚨 **Kalan kabul:** redakte edilen `ap_default_*` anahtarı `fc1998c9`'dan itibaren **geçmişte durmaya devam eder** — yerel loopback DB'sine bağlıdır, dışarıdan erişilebilir bir yüzeyi yoktur; hâlâ ayakta bir yerel örnekte kaydı varsa silinir | Kullanıcı + `nuget-danismani` |

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
- [ ] Deterministic/reproducible release ölçüldü.
- [ ] Package validation sonucu incelendi.

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

| Kimlik | Tarih | Durum | Karar | Gerekçe | Doğrulama |
|---|---|---|---|---|---|
| KG-001 | 2026-08-27 | Geçersiz kılındı | Önceki `1.0.0-preview.1` önerisi | Kullanıcı, tüm önceki yayın kararlarının bağlayıcı olmadan yeniden değerlendirilmesini istedi | Yeni yayın hedefi kararı bekleniyor |
| KG-002 | 2026-08-27 | Tamamlandı | Yaşayan kayıt `docs/YAYIN-HAZIRLIK.md` konumunda tutulur | Bu çalışma faz değildir; kökteki tek ledger, faz planı ve karar defteriyle rol çakışması üretmez. Doküman bütçesi içinde kalır | `dokuman-bakim.py --denetle` yeniden koşulacak |
| KG-003 | 2026-08-27 | Geçersiz kılındı | Geçici karar ⚠️ “teknik olarak yayınlanabilir, fakat önerilmez” | Bu sonuç eski hedef varsayımına dayanıyordu | Yeni hedef ve kapsam belirlendikten sonra yeniden karar verilecek |
| KG-004 | 2026-08-27 | Tamamlandı | Önceki yayın kararları bağlayıcı olmadan baştan değerlendirme yapılacak | Kullanıcı talimatı | Bu dosyada tarihsel karar ile ölçülen kanıt ayrımı korunacak |
| KG-005 | 2026-08-27 | Tamamlandı | Yayın türü `preview` olarak sabitlendi (UR-001) | Kullanıcı kararı; shipped baseline boş ve `Tracon.AspNetCore` pre-release bağımlılık taşıyor | Sonraki karar grubu: birincil hedef tüketici (UR-002) ve paket kapsamı (BL-002) |
| KG-006 | 2026-08-27 | Tamamlandı | Paket kapsamı tam entegrasyon seti (20 paket) olarak sabitlendi (UR-002, BL-002) | Kullanıcı kararı; dry-run zaten tam kümeyi kanıtlamıştı | Audit yükü şimdi 22 sütunlu seam matrisi (BL-003) ve 4.3/4.4 güvenlik-capability mercekleri üzerinde yoğunlaşacak |
| KG-007 | 2026-08-27 | Tamamlandı | 22 sütunlu seam matrisi 78 seam'in tamamına, tam kapsam ve çok turlu olarak uygulanacak | Kullanıcı kararı; hiçbir seam kapsam dışı bırakılmayacak | §15'teki küme planı ve öncelik sırası; her küme raporu bu dosyaya işlenir |
| KG-008 | 2026-08-27 | Tamamlandı | Küme B ve C ölçüldü: Küme B 1× 🔴 (BL-006, BYOK case-sensitivity) + 3× 🟡 + 2× 🟢 üretti; Küme C 0× 🔴 (BYOK fail-closed tüm adaptörlerde tutarlı) + 2× 🟡 + 2× 🟢 üretti | İki arka plan ajanının bağımsız, file:line kanıtlı ölçümü | BL-006 bir preview blocker'dır — sıradaki küme çalışmasından önce ya da paralel olarak `kusur-giderme`'ye devredilmeli |
| KG-009 | 2026-08-27 | Tamamlandı | Küme E ve F ölçüldü: ikisi de 0× 🔴 üretti (Küme E 4× 🟡 + 1× 🟢; Küme F 4× 🟡 + 3× 🟢). Ajanın önerdiği 2 aday 🔴 (Küme E) Adım 7 filtresiyle 🟡'ye indirildi — gerekçe BL-017/BL-018'de | Bağımsız ölçüm + skill'in kendi Adım 7 seviyelendirme tablosuna karşı elle doğrulama | 4/11 küme tamam (B, C, E, F); A, D, G, H, I, J, K sırada; toplam açık 🔴 hâlâ yalnız BL-006 |
| KG-010 | 2026-08-27 | Tamamlandı | Küme A ve D ölçüldü: Küme A 2× 🔴 (BL-026 drain yarışı, BL-027 `IRunStore` ham exception sızıntısı) + 4× 🟡 + 2× 🟢; Küme D 0× 🔴 (görevin şüphelendiği decorator-sırası ve version-fallback ikisi de fail-closed çıktı) + 4× 🟡 + 2× 🟢 üretti | Bağımsız ölçüm; BL-027 özellikle önemli çünkü aynı sınıf kusur (`HATA-S3-006`) daha önce bir kardeş yolda düzeltilmiş ama burada tekrarlanmış | 6/11 küme tamam (A, B, C, D, E, F); G, H, I, J, K sırada; toplam açık 🔴 sayısı 3 (BL-006, BL-026, BL-027) |
| KG-011 | 2026-08-27 | Tamamlandı | Küme G ölçüldü: 1× 🔴 (BL-037 — BL-027 ile aynı sınıf, `WorkflowRunner`'da tekrarı) + 3× 🟡 + 1× 🟢 | Bağımsız ölçüm; ham exception sızıntısının **iki bağımsız çalıştırma yolunda** bağımsız olarak keşfedilmesi bunu tek vaka değil sınıf yapıyor | 7/11 küme tamam; H, I, J, K sırada; toplam açık 🔴 sayısı 4 (BL-006, BL-026, BL-027, BL-037 — son ikisi tek sınıf) |
| KG-012 | 2026-08-27 | Tamamlandı | Küme H ölçüldü: 1× 🔴 (BL-041 — `IIdempotencyStore` job loop'unda kullanılmıyor) + 4× 🟡 + 2× 🟢 | Bağımsız ölçüm | 8/11 küme tamam; I, J, K sırada; toplam açık 🔴 sayısı 5 |
| KG-013 | 2026-08-27 | Tamamlandı | Küme K elle ölçüldü (ajan gerekmedi, yalnız 2 arayüz) — 0× 🔴, 0× 🟡, bulgu yok | Doğrudan kaynak okuması | 9/11 küme tamam; I, J sırada (arka planda çalışıyor) |
| KG-014 | 2026-08-27 | Tamamlandı | Küme J ve I ölçüldü — ikisi de 0× 🔴 (Küme J 3× 🟡 + 1× 🟢; Küme I 4× 🟡 + 1× 🟢). **11/11 küme tamam.** Toplam: 5 blocker kaydı / 4 bağımsız 🔴 kusur sınıfı (BL-027+BL-037 tek sınıf), 35× 🟡, 17× 🟢 | Bağımsız ölçüm; BL-003 (seam matrisi) artık kapalı | Nihai yayın kararı verilebilir — bkz. §4 |
| KG-015 | 2026-08-27 | Tamamlandı | BL-041 [Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) ile kapandı — `IJobHandler.ExecuteAsync`/`JobContext.Items`'ın XML dokümanı at-least-once'ı ve süzülmemiş item listesini açıkça yazıyor; `JobHandlerContract` (üç yerleşik handler + `Tracon.Samples.CustomJobHandler` dış sample'ı) kuralı kilitliyor; `JobLeaseExpiryTests` lease-expiry davranışının gerçekten var olduğunu `InMemoryJobStore` üzerinde ölçüyor. **4/4 preview-blocker kusur sınıfı artık kapalı; açık 🔴 kalmadı** | Faz uygulaması + kendi kendini doğrulayan regresyon kanıtı (bir yerleşik handler'ın skip-kontrolü geçici olarak bozulup contract'ın gerçekten kırmızı verdiği doğrulandı, sonra geri alındı) | Nihai "yayınlanabilir" kararı hâlâ §7 (UR-003 public API freeze) ve §8 (OP-001..009 NuGet.org operasyonu) açık kararlarını bekliyor — bir sonraki `nuget-danismani` turunun konusu |
| KG-016 | 2026-08-27 | Tamamlandı | **UR-003 (public API freeze) 🔴'dan 🟡'ye indirildi ve GA turuna ertelendi**; **OP-001 kişisel hesap olarak sabitlendi**; OP-004/OP-005/OP-010 kullanıcı tarafından ertelendi; **sıradaki iş 35× 🟡 sistemik hat seçildi** (yayın onun arkasına alındı) | UR-003 için üç bağımsız kanıt aynı yöne bakıyor: sevk edilen `versioning.md:11-13`, K-603 ve K-602 — üçü de preview hattında yüzey daralmasını kırıcı saymıyor; Adım 7 filtresinin 3/4/6/7. sorularını geçemiyor. OP-001, OP-004, OP-005, OP-010 ve iş sıralaması kullanıcı kararıdır 👤 | Bu turda ölçülenler: 20/20 NuGet ID müsait (OP-003), release notes artifact'i yok (OP-007), CI GitHub release üretmiyor (OP-006), BL-033 HTTP sızıntısı yeniden üretilemedi (🟡'de kaldı, iddiası daraltıldı), OP-010/RK-011 yeni kaydedildi |
| KG-017 | 2026-08-28 | Tamamlandı | **35× 🟡 hattının kulvar 3'ü (XML sözleşme boşlukları) [Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı.** BL-024, BL-028, BL-029, BL-035, BL-038, BL-042, BL-043, BL-046 tam kapandı; BL-048, BL-050 kısmen kapandı (yalnız doküman boyutu; contract test/registration API kulvar 1/2'de kalır); BL-026 belge kısmı kapandı, muhasebe-hassasiyeti riski değişmedi. **Beklenmedik bulgu:** BL-046'nın "hiçbir shipped yol tetiklemiyor" değerlendirmesi eksikti — `InMemoryAuditLog` (referans implementasyon) null-tenant'ta GERÇEKTEN tüm kiracıları tarıyordu; `kusur-giderme` kapsamına girmeden aynı fazda düzeltildi (K-644) | Bağımsız denetim (taze bağlamlı agent, `faz-denetim`): 0× 🔴, 1× 🟡 (Public API planı ile gerçekleşen arasındaki fark dokümana yazılmalı — bu turda yazıldı), 1× 🟢 (`IAgentSkillStore`'un audit decorator'ı yok — aday listesine) | Kulvar 3'ün geri kalanı (dokunulmayan ~28 arayüzün taban çizgisindeki 174 satırı) `seam-contract-baseline.txt`'de bilinen borç olarak duruyor; sıradaki iş kullanıcı kararına bağlı — kulvar 2/1/4 turu mu, yoksa doğrudan `nuget-danismani`'nin yayın kararı turu mu |
| KG-018 | 2026-08-28 | Tamamlandı | **35× 🟡 hattının kulvar 2 (kayıt API'si) ve kulvar 6 (kod düzeltmesi) [Faz 122](arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md) ile kapandı.** BL-018, BL-033, BL-034, BL-051 tam; BL-008, BL-016, BL-019, BL-039 kısmen. Kulvar 2'nin kapsamı ölçümle bilinçli daraltıldı: tüketiciye dönük **çoklu** seam yalnız 5 ve yalnız `IAgentDecorator`'da kayıt API'si eksikti; kalan 60 seam **tekil** ve `TryAdd*` deseniyle zaten doğru çalışıyor | Ledger'ın "~19 arayüz" tahmini ölçümle yanlış çıktı (dedicated kayıt API'si olan: 8/76). Spekülatif 68 metot yerine 3 gerçek eksik kapatıldı + `ITraconBuilder.Services`'in XML dokümanına tekil/çoklu seam sözleşme tablosu eklendi (metin kapısı: `OrderingContractDocumentationTests`) | Bu kayıt geriye dönük eklendi: Faz 122'nin kapanışı §6 ve §13'e işlenmiş, karar günlüğüne işlenmemişti |
| KG-019 | 2026-08-28 | Tamamlandı | **Yol B seçildi: iş 35× 🟡 hattından yayın kritik yoluna döndü.** KG-016'nın "yayını hattın arkasına al" kararı revize edildi. Kalan üç kulvar (1 contract testi, 4 dış sample, 5 gözlemlenebilirlik) `preview.1` → GA hattına taşındı 👤 | Üç kulvar da ölçümle açık doğrulandı, ama üçü de 🟡'dir ve `PublicAPI.Shipped.txt` boştur (K-603) — preview hattında yüzey hâlâ ucuzdur. Kulvar 1'in kalan boşluğu spesifikasyon değil doğrulamadır; hangi seam'in gerçekten genişletildiğini bilmeden 26 suite yazmak YAGNI ihlalidir. Preview geri bildirimi bu sırayı ucuza belirler | Yayın kritik yolu: (1) kapının taze koşumu, (2) BL-052 sample kapsam boşluğu, (3) BL-015, (4) OP-007 release notes hattı, (5) doküman drift taraması, (6) ledger düzeltmeleri |
| KG-020 | 2026-08-28 | Tamamlandı | **Yayın kritik yolunun üç kalemi tek faza bağlandı: [Faz 123](arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md)** — BL-052 (kapı kapsamı), BL-015 (adaptör sözleşmesi), OP-007 (sürüm notu hattı). Üç ürün kararı alındı 👤: `PackageReleaseNotes` **sürüme çapalı URL** taşır (20 paket tek sürüm hattından çıktığı için gömülü metin 20 kez tekrarlanırdı); kapı CHANGELOG'da `[<sürüm>]` bölümü yoksa **fail-closed** kırmızı döner; `docs-site` **yalnız bağlantı** verir, ayna sayfa açılmaz. CHANGELOG biçimi **Keep a Changelog** | Üç kalem tek fazdadır çünkü üçü de aynı altyapıya dokunur — `v*` tag'inde ne üretildiğini ve neyin doğrulandığını belirleyen hat (`kapi.py`, `ci.yml`, `Directory.Build.props`). Ayrı fazlar aynı üç dosyayı üç kez açardı | **`faz-planlama` Adım 1 kök nedeni buldu:** BL-052 tek satırlık bir unutma değil — `release_extension_samples.py` iki liste taşıyor. `validate_sample_contract` `glob` ile altı sample'ı da **şekil** olarak doğruluyor, ama koşum listesi `SAMPLE_TEST_PROJECTS` elle yazılmış beşli bir tuple. K-622 "beş sample" ifadesini karar defterine dondurmuş; Faz 120 altıncıyı ekledi, tuple güncellenmedi. Bu yüzden faz tek satır eklemekle yetinmez, envanter ≠ liste durumunu yakalayan bir kapı da yazar |
| KG-021 | 2026-08-28 | Tamamlandı | **RK-011/OP-010 için yol A + C seçildi 👤.** **C uygulandı:** `ci.yml`'de `publish` işi `npm-publish`'e bağlandı (`needs: [pack, release-dryrun, npm-publish]`); npm kanalı kırılırsa NuGet **hiç** basmaz. **A kullanıcıdadır:** `tracon` npm scope'u + `NPM_TOKEN` tag'den önce hazırlanır | Ölçüldü 2026-08-28: iki iş `if:` koşulu aynı, `needs:` farklıydı ve **paralel** koşuyordu ([`ci.yml:284`/`:319`, değişiklik öncesi](../.github/workflows/ci.yml)); `tracon` npm scope'u yok (OP-003, `404`) ve sevk edilen doküman **10 satırda** `@tracon/client` diyor. NuGet'te geri dönüş yoktur, npm'in kısıtlı bir unpublish penceresi vardır — bu yüzden ucuz kanal ÖNCE basar. Sıralama zinciri kırmaz: `npm-publish` var olan sürümü atlar (`npm view` kontrolü), yani aynı etiket yeniden itilebilir | YAML çözümlendi, 7 iş, döngü yok; `publish -> [pack, release-dryrun, npm-publish]`, `github-release -> [publish, npm-publish]`. `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md:2579` kabul ölçütü (`needs:` satırı `release-dryrun` içerir) hâlâ geçer |
| KG-022 | 2026-08-28 | Tamamlandı | **Doküman drift taraması koşuldu (`tuketici-dokuman-senkronu`, tam kapsam) ve kapandı.** 12 bayat iddia düzeltildi; hiçbiri makine kapısı tarafından yakalanmıyordu. RK-005 aynı turda kapatıldı | **Ölçülen yer gerçeği:** konsol **30 ekran / 36 rota** (`app.tsx`: 30 `*Screen` importu, 36 `pattern:`) — sevk edilen metin 27 (×2) ve 28 (×3) diyordu, rota 33 ve 36 diyordu · JS bundle **175,9 KB gzip** (`npm run build`) — üç yüzey 180,2 / 165,8 / 169,4 diyordu · istemci **162 operasyon** (`tracon.json` + üretilen `TraconApiClient.g.cs`) — kök README 161 diyordu (×2) · store contract'ı **31** (`Contracts/` kökünde 32 dosya, biri ortak taban `TenantIsolationContract`) — README ve paket `Description`'ı “32 others”/“29 more” diyordu · kök README paket tablosu **19 satır** taşıyordu, 20 paket var (`Tracon.Testing.Contracts.Xunit` eksikti) · `Tracon.UI` README'sinde **Triggers alanı hiç yoktu** (site sayfası kapsıyordu) · `Contracts.Xunit` README'si ve `Description`'ı **altı contract ailesinin ikisini** anlatıyordu (Tools ve Scheduling eksik) — CHANGELOG ise altısını da vaat ediyor · `packages.md` `IJobHandler`'ı atlıyordu · `Tracon.Client`'ın `Description`'ı sevk edilen metinde **iç repo yolu** taşıyordu (`docs/openapi/tracon.json`; repo özel, tüketicinin elinde yok) → yayınlanan adrese çevrildi · kök README'nin “4408 tests, 16 projects” satırı: 16→**20** ölçüldü, test sayısı **ölçülemedi** (tam Release koşumu MEMORY.md'nin boru-hattı tuzağına takıldı, %0,6 CPU'da asıldı) ve **uydurulmak yerine kaldırıldı** | **Dört kapı da yeşil:** (1) `ShippedDocumentationSelfContainmentTests`+`CapabilityExampleTests`+`SourceLanguageTests` 6/6, `LocalReferenceTests` 10/10; (2) `build-agent-map.mjs --check` — **önce kızardı**, paket `Description`'ı haritayı besliyor, yeniden üretildi; (3) `npm run check` — 1044 sayfa, 151 442 bağlantı, ağırlık tavanı; (4) `--site-denetle` — `paket-tanimi`/`paket-readme` **gerçek boşluk buldu** (`packages.md`'de `IJobHandler` eksikti, düzeltildi); `buildtransitive` ve `cekirdek-kavram` gerekçelendi: ikisini de **üretilen** `Tracon.AgentMap.md` tetikledi, diff yalnız revizyon hash'i + bir paket açıklaması satırıdır — tüketicinin gördüğü MSBuild yüzeyi de çekirdek kavram da değişmedi. **Kalıcı kapı yazılmadı; [F-171](ADAYLAR.md) olarak kaydedildi** — 12 kalemin altısı elle kopyalanmış ölçüm sayısıydı (K-483 sınıfı) |
| KG-023 | 2026-08-28 | Tamamlandı | **NuGet.org authentication modeli trusted publishing olarak sabitlendi.** Kullanıcı kişisel-owner policy'yi `Tracon*`, push-only, `farukatasoy/Tracon`, `ci.yml`, `nuget` sınırlarıyla oluşturdu; CI kalıcı `NUGET_API_KEY` yerine OIDC kullanır 👤 | Uzun ömürlü secret ve rotation riski kalkar; NuGet.org her koşumda bir saatlik key üretir. GitHub Free/private repo OIDC'yi engellemez. Policy ilk başarılı publish'e kadar yedi günlük geçici aktivasyondadır | `.github/workflows/ci.yml`: publish job `contents: read` + `id-token: write`; `NuGet/login@v1`; step output key; `secrets.NUGET_API_KEY` referansı sıfır |
| KG-024 | 2026-09-02 | Tamamlandı | **Yayın kararı Faz 129–135 sonrası yenilendi: ❌ Yayınlanmamalı.** İki yeni 🔴 (BL-053, BL-054). Sample'lar çözüme ALINMADI; onun yerine `faz-tamamlama` Adım 1'e koşullu yayın provası eklendi (K-657, kullanıcı kararı) | Kapanış kapısı 10/10 yeşilken `kapi.py yayin --kuru` `EXIT=1` döndü — iki kapı farklı şeyler ölçüyor ve fazlar zayıf olanla kapanıyordu | Prova ilk düşen sample'da durdu; beş sample ve AOT smoke ÖLÇÜLMEDİ. BL-053/054 kapandıktan sonra prova sonuna kadar koşulur ve karar yeniden verilir |
| KG-025 | 2026-09-02 | Tamamlandı | **BL-053 ve BL-054 kapatıldı; prova sonuna kadar yeşil koştu (`EXIT=0`). Karar ❌ → ✅ teknik olarak yayınlanabilir.** Sınıf taraması `JobStoreContract`'ın dış sample'ı olmadığını buldu → BL-055 | Altı sample 169 test + Native AOT smoke publish ve çalıştırma, exact sürüm ve izole `NUGET_PACKAGES` ile | 🚨 **Yerel sürüm kimliği tuzağı ölçüldü:** global NuGet cache'te 28 Ağustos'tan kalma bir `1.0.0-preview.1` vardı ve izolasyonsuz `dotnet test` ona derledi — aynı sürüm dizesi iki farklı içeriği adlandırıyor. Rehber sayfasına `NUGET_PACKAGES=$(mktemp -d)` uyarısı eklendi |
| KG-026 | 2026-09-16 | Tamamlandı | **Faz 176, 177 ve 178 `preview.1`'den SONRA uygulanır** 👤 — üçü de `preview.2` hattına gider | Üçü de public yüzeyi büyütür, ama `PublicAPI.Shipped.txt` her pakette **boştur** ve freeze Faz 7'dedir (K-603, UR-003/KG-016): yüzey büyütmek Faz 7'ye kadar ucuzdur, `preview.1`'e kadar değil. Preview'ın varlık sebebi gerçek tüketici geri bildirimidir; onu üç faz geciktirmenin bedeli, üç fazı bir sürüm sonra sevk etmenin bedelinden büyüktür | Faz 176'nın üç migration seti ve `RunScore` alanı `preview.2` sürüm notunda **davranışsal ek** olarak anlatılır |
| KG-027 | 2026-09-16 | Tamamlandı | **Manuel kabul setinin TAM turu (36 aile) tag'den önce koşar** 👤 | Ölçüldü: son tam tur **2026-08-13**'tür ve yalnız 25 aile almıştır; 26–36 arası **11 aile hiç koşulmamıştır** ve o günden beri ~50 faz geçmiştir. §5'in "henüz ölçülmeyen alanlar" listesindeki *Tam manuel kabul setinin güncel koşumu* kalemi bugün yayın kararının en zayıf kanıtıdır. Kullanıcı kısmi turu değil tam turu seçti | `kosumlar/<tarih>/` altında 36 aile kaydı kapanır; bulunan her kusur `kusur-giderme` ile sınıfıyla kapanır ve son kod değişikliğinden sonra dört kapı yeniden koşar |
| KG-028 | 2026-09-16 | Tamamlandı | **Repo `preview.1` gününde public yapılır** 👤 — public, tag'den **önce** | Üç şeyi birden açar: Source Link üçüncü tarafta çözer (RK-013 kapanır), `SECURITY.md` zafiyet kanalı görünür olur ve issue şablonları kusur kanalı olur — §14'ün ilk 72 saat planı bugün private repo'da **boştur**. PolyForm lisansı değişmez; kaynağın görünür olması kullanım hakkı vermez | 🚨 İki ön koşul: (1) tam `git` geçmişinde secret taraması — mevcut kapı yalnız çalışma ağacını yürür (RK-014); (2) `docs/guvenlik-tarama/` (12 dosyalık iç tarama kaydı) ve `.agents/` için yayımla/çıkar kararı. Tehdit modelini yayımlamak ile iç zafiyet tarama kaydını yayımlamak **ayrı** kararlardır |
| KG-029 | 2026-09-16 | Tamamlandı | **Sürüm `1.0.0-preview.1` olarak sabitlendi** 👤; tag adı `v1.0.0-preview.1` | UR-001 ile seçilen `preview` türünün ilk numarasıdır ve tüm prova kanıtı (20 paket, altı sample, AOT smoke) bu numarayla ölçülmüştür. Düşük major (`0.1.0-preview.1`) seçeneği reddedildi: sevk edilen `versioning.md` ve `compatibility.md` tek sürüm hattı ve 1.0 anlatısı üzerine kuruludur | §10'daki sürüm onayı kalemi kapandı; CHANGELOG başlığı `## [1.0.0-preview.1]` olarak kesilir |
| KG-030 | 2026-09-19 | Tamamlandı | **`docs/guvenlik-tarama/` ve `.agents/` repo ile birlikte yayımlanır** 👤 — KG-028'in ikinci ön koşulu kapandı | Tarama kaydının yayımlanabilirliği **ölçüldü, varsayılmadı**: `BULGULAR.md`'nin TÜM 🔴 ve TÜM 🟡 CONFIRMED bulguları KAPANDI ya da YANLIŞ POZİTİF çıktı. Açık kalan 3× 🟡 PLAUSIBLE (B01-3, B02-7, B03-7) ve ~18× 🟢 sağlamlaştırmadır — yayımlanan şey canlı bir istismar haritası değil, kapanmış bir denetim kaydı ve bir sağlamlaştırma backlog'udur. Geçmişten çıkarma seçeneği **bedeliyle** reddedildi: `filter-repo` 759 commit'in SHA'sını değiştirir ve `docs/` içindeki **278 gerçek commit atıfını** geçersiz kılar. HEAD'den silmek güvenlik kazancı vermez — 8 commit'in içeriği public geçmişte okunabilir kalır. `.agents/` 24 dosyalık iş akışı talimatıdır; tek secret bulgusu localhost docker parolası ve manuel test token'ıdır | 🚨 Kalan borç, gizlenmez: indeksin kendi uyarısı yürürlüktedir — tarama tabanı `1cda224`'tür, o günden beri 477 commit geçmiştir ve **konu 11 ile 12 hiç koşulmamıştır**. Yayımlanan kayıt bunu kendi başında yazar |
| KG-031 | 2026-09-19 | Tamamlandı | **Secret taraması iki katmanlı hâle getirildi.** Credential *şekli* olan desenler (`ap_*`, `ghp_`, `AKIA`, `AIza`, `whsec_`, `sk-*`, `AVNS_`) **tüm ağaçta** koşar — `arsiv` ve `manuel-test` dahil; yerel kurulum deyimi (`Pass`+`word=`/`pwd=`) yalnız kod ağacında aranır | 🚨 Kapı ✅ **temiz** derken `docs/arsiv/fazlar/53-KIRACI-API-ANAHTARLARI.md:78` içinde GERÇEK bir `ApiKeyGenerator` çıktısı duruyordu. İki bağımsız boşluk vardı ve **her biri tek başına yeterliydi**: (1) desen ürünün kendi anahtar formatını tanımıyordu — bir secret kapısının kendi ürününün credential'ını tanımaması, kapının olmamasıyla aynı sınıftadır; (2) `SCAN_EXCLUDED_DIRS` `arsiv` ve `manuel-test` ağaçlarını hiç yürümüyordu, oysa **gerçek koşum çıktısı taşıyan tek yer orasıdır** — üretilmiş bir credential'ın yapışacağı yer tam olarak burasıdır. Kapsamı tüm desenlere açmak çözüm değildi: ölçüldü, 51 eşleşmenin 48'i localhost docker parolasıdır ve hepsini işaretlemek kapının kendi yorumunun yasakladığı şeyi (sessizce büyüyen istisna listesi) üretirdi. İki katman ölçülen maliyeti 51'den **9'a** indirir | Mutasyonla doğrulandı (8/8): arşivdeki/manuel-testteki/koddaki üretilmiş anahtar **yakalanır**; arşivdeki yerel deyim **yakalanmaz**, koddaki **yakalanır**; snake_case metin anahtar sanılmaz; işaretli satır atlanır ve sayılır. `IkiKatmanliSecretTaramasiTestleri` (7 test) sınıfı kilitler; `kapi_test.py` 71/71, `kapi.py tarama` ✅ (15 işaretli istisna), `ic-dongu` ✅ (derleme + 2895 + 292 test). **Desen yazarken iki tuzak ölçüldü:** `ap_...{43,}` snake_case İngilizce metinde 70+ yanlış pozitif verir (uzunluk TAM verilmeli), ve deseni düz yazan yorum kapıyı kendi kaynağı üzerinde kırmızı yapar |
| KG-032 | 2026-09-19 | Tamamlandı | **Yayın kararı ❌ → ✅ Yayınlanabilir (`1.0.0-preview.1`).** Yayın kapsamı daraltılmaz: 20 paket (UR-002), tek sürüm hattı | Karar testlerin yeşilliğine değil artifact davranışına dayanır. 2026-09-02 kanıtı (KN-018) devralınamazdı — arada **498 commit**, `src/`'de 1420 dosya, 28 migration ve bir ürün yeniden adlandırması vardı. Bu tur prova **boşaltılmış** artifact dizininde ve **commit edilmiş** ağaçta koştu; `package-manifest.json` commit `225f1472`'yi taşır, yani kanıt tag'lenecek ağacın kendisine aittir. Açık 🔴 yok; açık 🟡'ler (BL-007/017/022/023/030/044/055) doğrulama boşluğudur ve KG-019 ile GA'ya taşınmıştı — gerekçe bugün yeniden ölçüldü: `PublicAPI.Shipped.txt` **boş**. BL-022 en riskli kalem olduğu için ayrıca ölçüldü: test yok ama **kusur da yok** (`state` 32 bayt CSPRNG, eşzamanlı sözlük, süre aşımı temizliği, tekrar gönderim ikinci yetkilendirme üretmiyor) → 🟡 kalır | KN-024 · KN-025 · KN-026. Kapının **koşmadıkları** da yazıldı: paket içerik doğruluğu, XML doğruluğu, Source Link gerçek çözümlemesi (repo public olmadan ölçülemez), AOT yalnız tek sample, güvenlik taraması konu 11/12 hiç koşmadı (KG-030 kabulü) |
| KG-033 | 2026-09-19 | Tamamlandı | **`nuget-danismani`'nin kendi kaynağındaki iki bayat iddia düzeltildi** | Skill'in kanıt tablosu hâlâ Faz 136 ÖNCESİNİ anlatıyordu: "bayat `.nupkg` siler, sonra paketler". O davranış kaldırıldı; bugün koşuma özgü bir **staging** dizinine paketlenir ve `release_dir`'e promote edilirken aynı kimlikte farklı içerik varsa **hiçbiri taşınmaz** (`_promote_staged_packages`, fail-closed). Ayrıca "beş extension sample" iddiası BL-052'den (Faz 123) beri yanlıştı — altı | Bayat bir kanıt kaynağı, ölçümü yanlış yere bakmaya yönlendirir; bu turda tam olarak o oldu ve kod okunarak düzeltildi |
| KG-034 | 2026-09-19 | Tamamlandı | **13.0 aksiyon tablosunun altı önkoşul kararı alındı 👤.** **K1** = kiracı kimliği, karşılaştırıcı değiştirilerek değil **değer normalleştirilerek** case-duyarsız yapılır; iş tek turluk bir düzeltme değil **[Faz 179](arsiv/fazlar/179-KIRACI-KIMLIGI-NORMALLESTIRME.md)** olarak açılır. **K2** = seçenek **A**: kusur düzeltilir (`GenerateImagesAsync` → `GenerateContentAsync`), `Count`/`MediaType` sözleşmesi birlikte yeniden yazılır. **K3** = `TenantChatClientCacheKey` **`internal`** yapılır. **K4** = onay parmak izi **uzunluk-önekli** formata geçer. **K5** = şablonun `TraconVersion` varsayılanı **şablon paketinin kendi sürümüne** çapalanır. **K6** = job süre sınırı **GA'ya ertelenir**; `preview.1`'de yalnız doküman gerçeğe çekilir | K1: aynı sınıf `provider_name` için K-639/migration 0025 ile zaten normalleştirme yönünde kapatılmıştı — emsalin tersine gitmek iki kimlik alanını iki ayrı kurala bağlardı; kapsam (kod + 3 migration + contract + doküman + public kural) bir `kusur-giderme` turundan büyüktür, bu yüzden faz zinciri ve bağımsız denetçi seçildi. K2: yeteneğin **aynı anahtarla** çalıştığı ölçülmüştü (K-835 — `gemini-2.5-flash-image:generateContent` 3,2 MB PNG üretti), yani düzeltme spekülatif değil; çalışmayan bir yolu sevk etmek ya da yalnız dokümana uyarı yazmak preview'ın varlık sebebiyle (gerçek tüketici geri bildirimi) çelişir. K3/K4/K5: `PublicAPI.Shipped.txt` her pakette **boştur** (K-603) — bugün bedava olan üç değişiklik `preview.1`'den sonra sırasıyla yüzey kırılması, migration ve şablon davranış değişikliği olur. K6: public yüzeyi büyütür ve KG-026 tam olarak bu sebeple üç fazı `preview.2`'ye ertelemişti; sınırın kendisi bugün yalnız yanlış **anlatılıyor**, düzeltilecek olan o | Oturum kapsamı olarak "tüm tag öncesi kalemler" seçildi 👤: 🔴 A-1, A-2, A-3, A-19 · 🟡 A-4, A-5, A-6, A-9, A-13, A-20, A-21, A-22, A-23, A-24 · 🟢 A-25. Elle müdahale kalemleri (A-10 site deploy sırası, A-11 trusted publishing policy, A-12 environment koruması) kullanıcıda kalır. Son kod değişikliğinden sonra dört kapı + `kapi.py yayin --kuru` yeniden koşar |
| KG-035 | 2026-09-19 | Tamamlandı | **13.0 aksiyon tablosunun 17 kalemi kapatıldı — dört 🔴'nin dördü dahil.** A-1 [Faz 179](arsiv/fazlar/179-KIRACI-KIMLIGI-NORMALLESTIRME.md) olarak kapandı (K-836…839); A-2, A-3, A-4, A-5, A-6, A-7, A-9, A-13, A-14, A-19, A-20, A-21, A-22, A-23, A-24, A-25 kendi kanallarında kapandı (K-840…843). A-8 K6 gereği GA'ya ertelendi, dokümanı gerçeğe çekildi | Faz 179'a bağımsız denetim koştu ve **2× 🔴 · 8× 🟡 · 2× 🟢** buldu; ikisi de gerçekti ve on ikisinin onu düzeltildi, ikisi gerekçelendi. 🚨 Denetimin birinci 🔴'si fazın kendi risk tablosunun birinci satırıydı: SQL Server'ın `COLLATE Latin1_General_BIN2` predicate'i **hiçbir testten geçmiyordu** — guard'ın tek testi SQLite'tı ve orada temel yüklem zaten doğru çalıştığı için override silinse tek bir test bile kırılmıyordu. Şimdi gerçek SQL Server üzerinde ihlal yolu ölçülüyor ve mutasyonla kanıtlandı. İkinci 🔴 guard'ın tek sarmalanmamış bootstrap adımı olmasıydı. Dört düzeltme ayrıca mutasyonla kanıtlandı (kiracı normalleştirme 6'nın 5'i, tıkaç kapısı, arka plan servisi OCE, onay parmak izi çakışması üçünün üçü) | Kapanış kapısı ve dört doküman kapısı yeşil. **Kalan kök faz yaşam döngüsü bulgusu bilinçlidir:** `docs/179-*.md` `✅ Tamamlandı` ile kökte duruyor çünkü `faz-arsivle` temiz çalışma ağacı ister — arşivleme commit'ten sonradır ve commit kullanıcıya aittir. Yeni açılan kalemler: **A-26** (zaman aşımına uğrayan workflow run'ı sebep taşımıyor), **A-27** (`KARARLAR.md` bütçenin tam sınırında), **F-254** (egress fail-open varsayılanı), **F-255** (Google görsel yolu için canlı kanıt yok), **F-256** (harf-kayması sözleşmesi iki store'da) |
| KG-036 | 2026-09-19 | Tamamlandı | **Üçüncü tur: karar ❌ olarak KALIR, fakat gerekçesi DEĞİŞTİ — artık açık bir ürün 🔴'si yoktur; kalan üç 🔴 (A-10 · A-11 · A-12) operasyoneldir ve repo dışındadır.** A-26 (K-844 👤) ve A-27 kapatıldı; §4/§10/§13'ün kendi drift'i gerçeğe çekildi; A-28 açıldı | İkinci turun ❌'i devralınamazdı: arada Faz 179 dahil dört commit ve 17 kalem kapanışı vardı. Artifact `c5ed5573`'ten YENİDEN ölçüldü — `kapi.py yayin --kuru` çıkış 0, manifest `dirty: false` ve **38/38 dosya hash'i elle doğrulandı**. Kapının ilk koşumu kirli bir `release` dizini yüzünden `EXIT=1` döndü; bu, kapının promote korumasının çalıştığının kanıtıdır ve adım 6 için yazılı bir uyarıya dönüştü | Kalan yol §13'ün adım 5–9'udur; adım 9 artık tag'den ÖNCEdir ve kendi bitti ölçütünü taşır |
| KG-037 | 2026-09-19 | Tamamlandı | **Adım 5 (sürüm kesimi) ve adım 6 (kapılar) bitti; tag'e hazır commit `ce23527b`.** `CHANGELOG.md` `## [1.0.0-preview.1] - 2026-09-19` taşır ve üstünde boş bir `## [Unreleased]` vardır | Kesim bir başlık yeniden adlandırması sanılıyordu; ölçüldü ki **sevk edilmeyen bir gövdeyi ilk kez sevk edilir hâle getirir**. Kapanış kapısı üç gerçek kusur buldu (bayat changelog testi · üretilen sayfanın sitenin kendi adresini yazması · sevk edilen XML'de alarm emojisi) ve dördüncü turda çıkış 0 verdi. Prova ayrı koştu: 38/38 hash elle doğrulandı | 🚨 Bundan sonraki HER commit iki kapıyı da geçersiz kılar; tag ancak yeniden koşulan bir turdan sonra atılır |
| KG-038 | 2026-09-19 | Tamamlandı | **Site deploy'u tag gününe ERTELENDİ 👤; provası bugün koşuldu ve geçti.** Adım 7'nin `git push origin main` yarısı yeniden açıldı | Sayfayı bugün yayımlamak, NuGet'te var olmayan bir sürümü yayınlanmış gösterirdi — A-11 ve A-12 kullanıcıdadır ve süreleri belirsizdir. Push kalemi bu turun kendi altı commit'i yüzünden yeniden açıldı: bir ölçümü cümleye çevirirken o ölçümün neyi varsaydığını da yazmak gerekiyor | Tag günü: `site-deploy.sh` → push → public → A-11/A-12 → tag; deploy bir kez düşerse tekrar denenir (kopma geçici ölçüldü) |
| KG-039 | 2026-09-20 | Tamamlandı | **Sevk tarihi `2026-09-19` → `2026-09-20` düzeltildi ve tag commit'i `3ca2f9e9` → yeni kapanış commit'ine taşındı.** A-12 kapandı (ölçüldü: `nuget` ve `npm` environment'ları `required_reviewers` + `branch_policy` taşıyor). A-11 alanları `ci.yml` ile birebir doğrulandı; prefix `Tracon` aynı hesaba rezerve edildi. Kalan tek NuGet adımı policy'nin 7 günlük aktivasyonudur | 🚨 Kesimden SONRA sekiz commit daha indi ve sekizi de **sevk edilen** `src/` dosyalarına dokundu (`OpenAILiveSideband` 183 satır, `LiveVoiceSessionHost`/`Registry` yarış düzeltmeleri, `WorkflowRunner`'ın timeout'u artık `_timeProvider`'ı onurlandırıyor). Kapılar bu yüzden yeniden koşuldu — kesim gününün sarkması tarihi de bayatlattı: sürüm başlığı sevk GÜNÜNÜ söyler, kesim gününü değil | `CHANGELOG.md` ilk yayın olduğu için sürüm bölümü diff değil KAPSAM anlatır; yayınlanmamış koda gelen düzeltmeler ayrı kalem almaz |

## 12. Ertelenen işler ve gerekçeleri

| Kimlik | Konu | Durum | Gerekçe | Yeniden açılma ölçütü |
|---|---|---|---|---|
| ER-001 | `stable 1.0` freeze | Ertelendi | Preview artifact ve üçüncü taraf feedback kanıtı yok; MAF Hosting/A2A pre-release | Stable ölçütlerinin tamamlanması |
| ER-002 | Gerçek NuGet.org push | Ertelendi | Açık kullanıcı onayı ve operasyon checklist'i yok | Tüm preview blocker'lar kapanır ve kullanıcı onay verir |
| ER-003 | Kapsamlı düzeltme fazı | Ertelendi | Henüz doğrulanmış aksiyon kapsamı yok | Audit ölçülmüş bir iş üretir ve kullanıcı faz açılmasını onaylar |

## 13. Sonraki adım

> 🚨 **`preview.1` SEVK EDİLDİ (2026-09-20).** Aşağıdaki tablo kapanmış
> `preview.1` yolunun kaydıdır; A-10 · A-11 · A-12 dahil tüm tag öncesi
> kalemler kapandı. **Sıradaki iş `preview.2`/GA hattıdır** ve açık kalemler
> şunlardır: A-8 (job süre sınırı) · A-15 (kapı kendi bastığı byte'ları
> doğrulamıyor) · A-16 (`Tracon.Mcp` üç sevk edilen tipi testsiz) · A-17
> (`Tracon.Azure` çağrı yolu kanıtsız) · A-18 (dış sample kanıtı `IRunStore`
> ile sınırlı) · A-28 (sesli delegasyon timeout ≠ barge-in) · UR-003 (public
> API freeze taraması) · A-31 (**yeni**: kapanış kapısı artımlı durumdan
> yeniden üretilebilir değil — bayat çıktı kopyası 81 testi düşürdü ve kapı
> bunu "gerçek regresyon" diye raporladı; A-15 sınıfı) · **4 eksik store
> contract'ı** (A-21 ölçümü).
>
> Yayın günü bulunan A-29 · A-32 · A-33 **kapandı** — üçü de §4'ün başındaki
> YAYINLANDI bloğundadır.

### 13.0 Aksiyon tablosu — 2026-09-19 ikinci turunun çıktısı

Her satır bir **kanala** aittir. Kanal, işi hangi skill'in ya da kimin
yürüteceğini söyler; `Önkoşul` boş değilse o karar verilmeden işe başlanmaz.

| # | Bulgu | Kanal | Önkoşul karar | Öncelik |
|---|---|---|---|---|
| A-1 | **Kiracı kimliği semantiği ayrışıyor** — normalizasyon yok; SQL Server'da CI collation kiracıları birleştiriyor, PG/SQLite'ta egress allow-list'i `policy is null` ile fail-open düşüyor | **Faz adayı** (kod + 3 migration + contract + doküman; `provider_name`/K-639 şablonu hazır) | ✅ **KAPANDI** — [Faz 179](arsiv/fazlar/179-KIRACI-KIMLIGI-NORMALLESTIRME.md); K-836…839 | 🔴 tag öncesi |
| A-2 | **`Tracon.Google` görsel yolu çalışmıyor** (K-835, canlı 502) ama site + XML çalışır gösteriyor | **K2'ye göre:** A→`kusur-giderme` · B→public yüzey daraltma · C→`tuketici-dokuman-senkronu` | ✅ **KAPANDI** — `GenerateContentAsync` geçişi; `Count`/`MediaType` sözleşmesi yeniden yazıldı | 🔴 tag öncesi |
| A-3 | **Arka plan servisinde `store` OCE'si host'u durduruyor** — `BackgroundServiceExceptionBehavior` hiç ayarlanmamış, .NET varsayılanı `StopHost`; ürün kuralı ihlal edildiği satırda yazılı | **`kusur-giderme`** — 🚨 **SINIF TARAMASI zorunlu**: `is not OperationCanceledException` deseni `src/` içinde **97 yerde** | ✅ **KAPANDI** — `OperationCancellation.IsFailure`; 12 çağrı yeri, K-840 | 🔴 tag öncesi |
| A-4 | **`TenantChatClientCacheKey.For(...)` public ve düz metin BYOK anahtarı döndürüyor** | **`kusur-giderme`** (küçük: `internal` yap ya da hash'le) | ✅ **KAPANDI** — `internal`; K-842 | 🟡 tag öncesi |
| A-5 | **Onay parmak izi `U+001F` ayırıcı varsayımını zorlamıyor** — çakışma inşa edildi, istismar gösterilemedi | **`kusur-giderme`** (uzunluk-önekli format) | ✅ **KAPANDI** — uzunluk-önekli format; K-841 | 🟡 tag öncesi |
| A-6 | **`TraconWebhookOptions.Timeout` yalnız header fazını kesiyor** — CTS `SendAsync` dönünce dispose ediliyor | **`kusur-giderme`** | ✅ **KAPANDI** — deadline tüm denemeyi kapsıyor | 🟡 tag öncesi |
| A-7 | **`TraconWorkflowOptions.RunTimeout` wait cutoff değil** ve **sıfır testi var**; XML koşulsuz "maximum duration" diyor | **`tuketici-dokuman-senkronu`** (XML'i gerçeğe çek) + test | ✅ **KAPANDI** — XML gerçeğe çekildi + `WorkflowRunTimeoutTests` | 🟡 |
| A-8 | **Job gövdesi için süre sınırı yok**; kiralama süresiz yenileniyor, takılan handler `MaxConcurrentJobs` slotunu kalıcı tutuyor | **Faz adayı** (options alanı + kesme davranışı) | ✅ **K6 uygulandı** (KG-034) — GA'ya ertelendi; `preview.1`'de yalnız doküman gerçeğe çekildi (`IJobHandler` XML + iş kuyruğu rehberi: kiralama SÜRESİZ yenilenir, `JobTimeout` yoktur, takılan handler slotu kalıcı tutar) | 🟡 |
| A-9 | **Şablonun ürettiği proje `Version="*-*"` taşıyor** — ölçümde global cache'teki bayat `0.0.0-preview.0.789`'a çözüldü | **`kusur-giderme`** (varsayılanı şablon paketinin kendi sürümüne çek) | ✅ **KAPANDI** — paketleme anında damgalanıyor; K-843 | 🟡 tag öncesi |
| A-10 | **20 paketin `releaseNotes` çapası canlı sayfada yok**; sayfa "has not been released yet" diyor. `.nuspec` basıldıktan sonra değişmez | **Manuel müdahale** — site deploy'u tag'in **önüne** al; §4 adım 9'un "bitti ölçütü" hücresi **boş**, doldurulmalı | — | 🔴 tag sırası |
| A-11 | **Trusted publishing policy geçersiz** (OP-005): sahibi kişisel hesap, alanları eski ad, pencere dolmuş | **Manuel müdahale** — `Tracon` org sahipliğinde, **repo public olduktan sonra**, **"yeni paket" scope'u doğrulanarak** | — | 🔴 tag öncesi |
| A-12 | **`environment:` blokları bugün etkisiz** (Free + private). Public adımı ile tag adımı arasında koruma kuran adım yok | **Manuel müdahale** — adım 7 ile 8 arasına gir | — | 🔴 tag sırası |
| A-13 | **Action'lar SHA'ya pinli değil**; `NuGet/login@v1` OIDC token'ını gören iştir | **`kusur-giderme`** (küçük, Dependabot SHA pinlerini günceller) | ✅ **KAPANDI** — 27 `uses:` satırı SHA'ya pinli | 🟡 tag öncesi |
| A-14 | **`net8.0`/`net9.0` 52 gün sonra destek dışı**; `compatibility.md` lifecycle hakkında tek kelime etmiyor | **`tuketici-dokuman-senkronu`** | ✅ **KAPANDI** — `compatibility.md` lifecycle bölümü | 🟡 |
| A-15 | **Kapı kendi bastığı byte'ları doğrulamıyor** — `release-dryrun` kendi paketini üretip atar, `publish` `pack` işininkini glob'la basar; ayrıca `package-manifest.json` yazılır ama bir daha doğrulanmaz | **Faz adayı** (kapı sertleştirme; bugün gizil, canlı değil) | — | ⚪ GA |
| A-16 | **`Tracon.Mcp`'nin üç sevk edilen tipi hiçbir seviyede test edilmiyor**; `IMcpOAuthCoordinator._pending` süreç-içi ⇒ çok-replikada akış kırılır ve XML bunu söylemiyor | **Faz adayı** | — | 🟡 GA |
| A-17 | **`Tracon.Azure` çağrı yolu için hiçbir kanıt yok** (manuel aile 06 kimlik yokluğundan atlandı) | **Faz adayı** | — | 🟡 GA |
| A-18 | **Dış sample kanıtı `IRunStore` ile sınırlı** — 29/34 store arayüzü (A-21 ölçümü: 34 arayüz, 30 sözleşme)'ının dış tüketicisi yok; boşluğun kusur ürettiği bir kez ölçüldü (BL-053) | **Faz adayı** (KG-019 ile zaten GA'ya taşınmıştı) | — | 🟡 GA |
| A-19 | 🚨 **13 paket README'sinde 14 kurulum komutu `--prerelease`/`--version` TAŞIMIYOR.** Stable sürüm yok ⇒ tüketicinin nuget.org'da gördüğü ilk komut `NU1103` ile biter. Kök `README.md:221` doğru yazıyor; kapı (`check-content.mjs:425-434`) yalnız **site** sayfalarında koşuyor. Sitede de bir delik var: `guides/coding-agents.md:177` `dotnet tool install -g Tracon.Cli` bayraksız | **`tuketici-dokuman-senkronu`** + kapıyı `src/*/README.md`'ye ve `dotnet tool install` desenine genişlet | ✅ **KAPANDI** — 18 komut düzeltildi, kapı paket README'lerine genişledi | 🔴 tag öncesi |
| A-20 | **Meta paketin nuget.org `Description`'ı "brings in all Tracon components" diyor**; nuspec **6** doğrudan bağımlılık sayıyor (+Core +Abstractions = 8/20). Paketin **kendi README'si** "the common set" diyor — sayfa kendiyle çelişiyor | **`kusur-giderme`** (kod: `src/Tracon/Tracon.csproj` `<Description>`) | ✅ **KAPANDI** — meta paket `Description`'ı gerçeğe çekildi | 🟡 tag öncesi |
| A-21 | **Contract-suite kapsamı üç sevk edilen yüzeyde çelişiyor:** `compatibility.md:54` "32 other" (33) · contracts nuspec `Description` "30 others" (31) · `write-your-own-store.md:173` "every other store interface". **Gerçek: 29 store contract'ı, 33 store arayüzü** ⇒ `IConversationBranchStore`, `IDataSubjectStore`, `ITenantStore`, `IVectorSearchStore` contract'sız. Ayrıca `compatibility.md:54` "five extension families" diyor, **altı** (`JobHandlerContract`) | **`tuketici-dokuman-senkronu`** (sayıları eşitle) **+ Faz adayı** (4 eksik contract) | ✅ **KAPANDI (doküman)** — ölçüldü: 34 arayüz, 30 sözleşme, 4 kapsanmayan; üç yüzey eşitlendi. 4 eksik contract GA | 🟡 tag öncesi (doküman) |
| A-22 | **`IJobHandler` rehberin seam tablosunda "Multi-registration" satırında**; runtime **anahtarlı tekil kayıt** istiyor (`AddJobHandler<T>("key")`). Tabloyu izleyen tüketici `AddSingleton<IJobHandler, MyHandler>()` yazar, kayıt **sessizce hiçbir şeye bağlanmaz**, ilk kuyruğa atmada `JobDispatcher` patlar | **`tuketici-dokuman-senkronu`** | ✅ **KAPANDI** — `IJobHandler` "Keyed" satırına taşındı | 🟡 tag öncesi |
| A-23 | **`IRunErrorClassifier`'ın dokümansız ikinci tüketicisi var:** `WorkflowNodeRetry.IsTransient` node'un retry edilip edilmeyeceğine karar veriyor. O yolda `try/catch` **yok**, log **yok**, built-in'e fallback **yok** — üstelik çağrı bir exception filter'ının içinde ⇒ atan sınıflandırıcı **sessizce yutulur** ve node retry edilmez. Rehber ise "classifier throws ⇒ built-in devralır, hata loglanır" diyor | **`tuketici-dokuman-senkronu`** (ikinci iş yazılır) **+ `kusur-giderme`** (doküman daha güvenli davranışı anlatıyor ⇒ **kod yanlış**) | ✅ **KAPANDI** — `WorkflowNodeRetry` built-in'e düşüyor ve logluyor | 🟡 tag öncesi |
| A-24 | **"never throws" / "never blocks" / "does not stop the run" XML sınıfı — A-3'ün doküman yarısı.** Yeni üyeler: `IRunInputStore.cs:23` ve `ITraceStore.cs:8` (ikisi de koşulsuz, runtime OCE'yi dışlıyor) · `QuotaEnforcer.cs:129` "never throws" ama `ArgumentNullException` ve OCE atıyor · `ToolApprovalPresenterRunner.cs:15-18` timeout'u sert sınır gibi anlatıyor. 🚨 Store rehberi tüketiciye **"OCE at"** diye öğretiyor (`write-your-own-store.md:123-124`) ⇒ **yön: KOD yanlış**, doküman doğru | **`kusur-giderme`** (A-3 ile aynı iş) + kalan XML cümleleri | ✅ **KAPANDI** — A-3 ile aynı iş + `QuotaEnforcer`/`ToolApprovalPresenterRunner` XML'leri | 🟡 tag öncesi |
| A-25 | **Küçük doküman drift'leri:** decorator envanteri üç sayıyor, gerçek dört (`Order = 30`) · `TimeoutAIFunction` XML'inde iki kırık cümle (pakete girdiği doğrulandı) · üretilen API sayfasında `?text=` ham query string (tek vaka, üretici iç metinli `<see cref>`'i basamıyor) · `Tracon.Voice` "Zero NuGet dependencies" (bir bağımlılık var: `Tracon.Core`) | **`tuketici-dokuman-senkronu`** + üretici düzeltmesi | ✅ **KAPANDI** — dört kalem; üretici iç metinli `<see cref>`'i artık basıyor | 🟢 |
| A-26 | 🆕 **Zaman aşımına uğrayan workflow run'ı SEBEP TAŞIMIYOR.** A-7'nin testi yazılırken ölçüldü: `WorkflowRunner` `Tracon:Workflows:RunTimeout` adını içeren bir `RunError` **üretiyor**, ama kapanış `RunFailed` olayı `Text`'siz ve `Payload`'suz geliyor ve `RunRecord.Error` **null** kalıyor. Operatör çıplak bir `Canceled` görüyor. `WorkflowRunTimeoutTests` bu boşluğu şimdiden kilitliyor — kapandığı gün test kırmızı döner | **`kusur-giderme`** | ✅ **KAPANDI** — sebep `run`'ın kapandığı tek yerde eklenir; durum `Canceled` kalır; K-844 | 🟡 |
| A-27 | 🆕 **`docs/KARARLAR.md` bütçesinin tam sınırında** (495 820 / 496 000 B, %0 boş). Bu turun sekiz kararının uzun gerekçeleri `arsiv/KARARLAR-GECMISI.md`'ye taşındı ve ledger ancak öyle bütçeye girdi. Bir sonraki karar bütçeyi aşar | **Manuel müdahale** — damıtma turu; içerik SİLİNMEZ, taşınır | ✅ **KAPANDI** — `karar-damit` 59 satır · 41.807 B taşıdı; defter 455.089 B (%8 boş), tavan değişmedi | 🟡 |
| A-28 | 🆕 **Sesli delegasyonun zaman aşımı barge-in'den ayırt edilemiyor** — `DelegationTimeout` ile kullanıcı kesmesi AYNI `CancellationTokenSource`'u iptal eder; alttaki agent `run`'ı sebepsiz `Canceled` kapanır. A-26'nın sınıf taramasının bulduğu ikinci vaka | **Faz adayı** (ayrı timeout kaynağı; düzeltme workflow'unkiyle aynı şekil) | — | 🟡 |

**Önkoşul kararlar (kullanıcıya ait, §4'te gerekçeleri var):**
K1 kiracı kimliği semantiği · K2 Google görsel yüzeyi · K3 `TenantChatClientCacheKey`
· K4 onay parmak izi formatı · K5 şablon sürüm varsayılanı · K6 job süre sınırı.

**A-1'i pekiştiren doküman ölçümü:** `docs-site` kiracı kimliği için **hiçbir
kural yayımlamıyor** — ne harf duyarlılığı, ne karakter kümesi, ne uzunluk.
`TraconTenancyOptions.AllowedTenants` XML'i "any value matching **the format** is
accepted" diyor ama o "format" ne tipin `<remarks>`'inde ne sitede tanımlı.
Biçim sevk edilen metinde **tek bir yerde** geçiyor (`Tracon.Client` içindeki
tenant display-name ucunun açıklaması) ve orası runtime ile uyuşuyor. ∴
operatörün `acme` ile `Acme`'nin ayrı kiracı olduğunu öğrenebileceği **hiçbir
sevk edilen cümle yok.**

**Altı kulvarın hepsi koştu.** Doküman kulvarının temiz çıkardıkları: sevk
edilen yüzeyde **`AgentPrism` kalıntısı yok** · **ASCII kutu çizimi yok** ·
lisans anlatısı doğru ve tutarlı (17 PolyForm / 3 MIT, hiçbir yerde "open
source" iddiası yok) · fail-closed audit tablosu tam (6 satır, 9 çağrı yeri) ·
konsol `en`/`tr` dil dosyaları birebir eşit (1238 anahtar) · `TryAdd*` "senin
kaydın kazanır" anlatısı doğru · K-835 **tekil**, sınıf değil.

---

**Aşağıdaki anlatı adım 5–8'in nasıl koşulacağını tarif eder; sırası hâlâ
geçerlidir, fakat 13.0 kapanmadan başlamaz.** Repo içi yayın kritik yolu
Faz 123 ve KG-022 ile, hesap ve operasyon kararları 2026-09-03 turuyla kapandı.
Manuel kabul turu (adım 1, KG-027) ve kusur kapanışı (adım 2) 2026-09-18/19'da,
public öncesi geçmiş denetimi (adım 3, RK-014) ve **yayın turu (adım 4, KG-032 →
✅ Yayınlanabilir)** 2026-09-19'da bitti. Kalan altı adımın tam sırası §4'ün
2026-09-16 bloğundadır; açık iş yeni bir faz **değildir**.

Kalan beşi kullanıcı eylemidir ve sıra **tersine çevrilemez**: adım 5 sürüm
kesimi (`## [Unreleased]` başlığı `## [1.0.0-preview.1] - <sevk tarihi>` olur,
üstüne boş bir `## [Unreleased]` açılır), adım 6 kapılar (`kapanis` +
`yayin --kuru`, ikisi de sıfır uyarı), adım 7 repo public, **adım 9'un site
deploy'u**, adım 8 tag. **Public de site de tag'den öncedir** — Source Link tag
commit'ine bakar, ve 20 `.nuspec`'in `releaseNotes` çapası basıldıktan sonra
değişmez (A-10). Tag `origin`'e gider, `intelera`'ya değil; trusted publishing
policy'si `farukatasoy/Tracon` + `ci.yml` + `environment: nuget` üçlüsüne
bağlıdır ve **`Tracon` organizasyonu sahipliğinde yeniden kurulmalıdır** (A-11:
[`ci.yml`](../.github/workflows/ci.yml) `NuGet/login` adımı `user: Tracon`
yazar; 20 kimliğin yirmisi de nuget.org'da hâlâ **boştur**, yani policy'nin
"yeni paket" scope'u zorunludur). A-12 adım 7 ile adım 8'in arasına girer.

🚨 **Adım 7 İKİ iştir ve bu tur ikisini de açık bıraktı.** Ölçüldü
2026-09-19 kapanışta: `origin/main` `c5ed5573`, `HEAD` `44eb68d1` ⇒ `main`
**6 commit önde**. Bu satırın bir önceki hâli "push artık gerekmiyor" diyordu;
o cümleyi bu turun KENDİ commit'leri bayatlattı — aynı sınıf, aynı gün,
üçüncü kez. Ders: bir ölçümü cümleye çevirirken ölçümün neyi varsaydığını da
yaz. Geriye kalan: `git push origin main` **ve** görünürlüğü public yapmak;
anonim GitHub API isteği bugün hâlâ `404` döner.

🚨 **Adım 9'un provası koşuldu, deploy'un kendisi BİLEREK ertelendi** 👤
(2026-09-19). `scripts/site-deploy.sh --dry-run` uçtan uca **çıkış 0**: 1151
sayfa derlendi, 189.676 iç bağlantının hiçbiri kırık değil, SEO 0 hata, ağırlık
tavanı altında, `rsync` hedefe ulaştı. Deploy **tag günü, tag'den hemen önce**
koşar: A-11 ve A-12 kullanıcıdadır ve süreleri belirsizdir; sayfayı bugün
yayımlamak, NuGet'te var olmayan bir sürümü yayınlanmış gösterirdi — bu turun
kapattığı "sevk edilen doküman runtime ile çelişiyor" sınıfının ta kendisi.
⚠️ İlk iki prova denemesi `Connection closed by … port 22` ile düştü, üçüncüsü
geçti; `ssh` ve `rsync` ayrı ayrı sağlam ölçüldü ⇒ kopma geçicidir, eksik
yetki değil. Tag günü deploy bir kez düşerse **tekrar dene**.

🚨 **Her yeni commit adım 6'yı geçersiz kılar ve bu tur bunu İKİ kez ödedi.**
Üçüncü turun provası `c5ed5573`'ten koşmuştu; sürüm kesimi ve defter kapanışı
onun üstüne binince kapılar iki kez yeniden koşuldu. Tag ancak, tag'lenecek
commit'ten koşulmuş bir `kapanis` + `yayin --kuru` çiftinden sonra atılır. 🚨 O koşumdan önce `artifacts/package/release`
**boşaltılır** — üçüncü turda kirli dizin kapıyı `EXIT=1` ile durdurdu.

### Önceki sistemik hat — tarihsel kapsam

35× 🟡 sistemik hat KG-016 ile seçildi. Kulvar 2, 3 ve 6 kapandı; kulvar 1, 4
ve 5 KG-019 ile GA'ya taşındı. Aşağıdaki ayrıntı bu kararın kanıt kaydıdır;
güncel sıradaki iş değildir.

### Hattın tek cümlelik tanımı

25 blocker kaydı bağımsız 25 iş değildir. Hepsi **aynı** boşluğun örnekleridir:
Tracon'in extension seam'leri için **tek bir sözleşme standardı** yoktur.
Repo'da o standardın iki referans örneği zaten var — `IRunJudge` (kayıt üçlüsü +
contract test + startup validation) ve `IRunStore` (tenant-mode tablosu taşıyan
XML dokümanı). İş, bu iki deseni kalan seam'lere yaymaktır.

### Altı kulvar (kapsam)

| # | Kulvar | Kayıtlar | Ölçülen kapsam |
|---|---|---|---|
| 1 | **Reusable contract testi yok** — üçüncü tarafın koşabileceği suite | BL-007, BL-015, BL-017, BL-022, BL-023, BL-030, BL-039, BL-048, BL-050 | **26 arayüz** + shipped 4 provider adaptörünün mevcut `ModelProviderContract`'ı türetmemesi |
| 2 | ~~**Kayıt (registration) API'si yok veya eksik**~~ ✅ **KAPANDI — [Faz 122](arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md)** | BL-008 kısmen, BL-019 kısmen, BL-034 tam, BL-050 (kulvar 1/2 dışı) (+🟢 BL-016 kısmen) | **Ölçüldü 2026-08-28 — ledger'ın "~19 arayüz"ü yanlıştı.** Dedicated kayıt API'si olan: **8/76**. İş 68 metot değildi: tüketiciye dönük **çoklu** seam yalnız 5 (`IJobHandler`, `IContentGuard`, `IAgentSource`, `IAgentDecorator`, `IRunJudge`) ve dördünde API vardı — **yalnız `IAgentDecorator`'da yoktu, artık var** (`AddAgentDecorator` üçlüsü). `IContentGuard`/`IModelProvider` eksik overload'ları da tamamlandı. Kalan 60 seam **tekil** ve hepsi `TryAdd*` (düz `Add*` kaydı **0**) — dedicated API yerine `ITraconBuilder.Services`'in metin kapısına bağlı bir sözleşme tablosu eklendi; 6 tenant/store arayüzü (BL-008'in kalanı) bilinçli olarak kapsam dışı bırakıldı |
| 3 | ~~**XML sözleşme boşlukları**~~ ✅ **KAPANDI — [Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md)** (KG-017) | BL-024/028/029/035/038/042/043/046 tam; BL-048/050 kısmen (boyut 1-2); BL-026 belge kısmı | Dört boyutlu seam sözleşme standardı + `SeamContractDocumentationTests`'in küçülen taban çizgisi (K-643). **BL-046 doküman değil gerçek kusur çıktı:** `InMemoryAuditLog` null tenant'ta tüm kiracıları tarıyordu, `SqlAuditLog` sessizce boş dönüyordu — ikisi de düzeltildi (K-644). Kalan borç: `seam-contract-baseline.txt`, 174 satır |
| 4 | **Dış sample yok** (`PackageReference` ile koşan tüketici) | BL-016, BL-019, BL-023, BL-030, BL-036, BL-039, BL-045, BL-048 | Bugün altı sample var (ledger önceden "beş" diyordu — `CustomJobHandler` Faz 120'de eklendi); Küme B, F, G, H ve I'nın hiçbirinde yok |
| 5 | **Operasyonel gözlemlenebilirlik** | BL-044, BL-047 | `Scheduling`, `Webhooks`, `Coordination`, `Idempotency`, `Triggers` içinde hiç `ActivitySource`/`Meter` yok; audit-write-failure sayacı yok |
| 6 | ~~**Kod düzeltmesi (doküman değil)**~~ ✅ **KAPANDI — [Faz 122](arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md)** | BL-018 tam, BL-033 tam, BL-039 kısmen (yalnız duplicate-name), BL-051 tam | Decorator hatası artık `TraconAgentSourceException`'a normalize ediliyor (sınıf taraması: 4 çağrı yeri); duplicate workflow adı `TraconException` veriyor; guard/retention kayıtsızken `SilentGapWarningService` Production'da bir kez uyarıyor |

### Sıralama gerekçesi

**Kulvar 3 önce gelmelidir** ve tek başına en yüksek kaldıraçlıdır: iki gerçek
hata içerir, ölçüldü —

- **`IAgentDecorator.Order`'ın XML dokümanı gerçek davranışın TERSİNİ söylüyor**
  (BL-034). Doğrulandı: `RunRecordingAgentDecorator.cs:114` `Order=0` ile
  **dıştan**, `ToolApprovalAgentDecorator.cs:38` `Order=20` ile **içten** sarıyor;
  doküman bunun tersini yazıyor. Güvenlikle ilişkili bir decorator'ı yanlış
  katmana koyduran tek kalem budur — sözleşme kusuru olarak diğer 34'ün önündedir.
- **`IAuditLog`/`AuditQuery.TenantId=null`** kiracı sınırını yalnız DTO yorumunda
  taşıyor (BL-046); bugün hiçbir shipped yol tetiklemiyor ama üçüncü taraf
  implementasyon tüm kiracıların kaydını dönebilir.

Sonra **kulvar 2 → kulvar 1 → kulvar 4** birlikte yürür: kayıt API'si olmayan bir
seam'in contract testi de sample'ı da yazılamaz, çünkü tüketicinin yazacağı kod
henüz yoktur. Kulvar 5 ve 6 bağımsızdır, paralel gidebilir.

**Kulvar 3, [Faz 121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md) ile kapandı (2026-08-28, KG-017).** Her iki
gerekçe kalemi kapandı: `IAgentDecorator.Order` daha önce Faz 120'de (K-642),
`IAuditLog`/`AuditQuery.TenantId=null` bu fazda — hem doküman hem gerçek
implementasyon davranışı olarak (K-644, aşağı bakınız).

**Kulvar 2 ve 6, [Faz 122](arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md) ile kapandı
(2026-08-28, KG-018).** Altı kulvarın üçü (2, 3, 6) artık kapalıdır.

**Hat burada durduruldu (2026-08-28, KG-019 — yol B).** Kalan üç kulvar (1, 4, 5)
ölçümle açık doğrulandı ama üçü de 🟡 = 1.0 blocker'dır, `preview` blocker değil.
Kulvar 3 seam **sözleşmesini** yazdı ve kulvar 6 gerçek **hataları** kapattı;
kulvar 1'de kalan boşluk spesifikasyon değil **doğrulamadır**. Doğrulamayı kimin
kullandığını bilmeden yapmak koşulmayan 26 suite üretir (YAGNI). Bu yüzden kalan
üç kulvar `preview.1` → GA hattına taşındı ve iş yayın kritik yoluna döndü.

### `preview.1` öncesi güncel kalanlar

Aşağıdakiler bu hattın parçası **değildir**; `preview.1` tag'inden önce ayrıca
kapanmalıdır:

0. ~~**BL-053 ve BL-054**~~ — **kapandı (2026-09-02)**; prova sonuna kadar
   yeşil koştu. Tag gününde `CHANGELOG.md`'nin tarihi doğrulanır.
1. ~~**OP-011**~~ — **kapandı (2026-09-03):** GitHub Free'de kalındı, seçenek A.
2. ~~**OP-008, OP-009 ve §14**~~ — **kapandı (2026-09-03):** yalnız ileri sürüm ·
   mevcut Dependabot + `NU1903` restore kapısı yeterli · `SECURITY.md` ve issue
   şablonları eklendi.
3. ~~**BL-056**~~ — **kapandı (2026-09-03):** paket URL'leri doküman sitesine
   çevrildi (K-659).
4. ~~**F-180**~~ — **kapandı (2026-09-03, K-660):** kök neden ses
   protokolündeki sessiz dönüştü, düzeltildi ve sınıfı tarandı.
5. ~~Taze `kapanis` + `yayin --kuru`~~ — **koşuldu (2026-09-03), ikisi de
   yeşil.**
6. **Açık kalan tek adım:** `1.0.0-preview.1` için gerçek tag onayı. Tag
   gününde `CHANGELOG.md`'nin tarihi yeniden doğrulanır.

### GA turuna ertelenenler

- **UR-003 public API freeze taraması** — 680 tip, `Unshipped → Shipped`
  dolumuyla aynı turda (KG-016).
- §10'daki Source Link gerçek çözümleme, reproducible build, package validation
  ve tam artifact-seviyesi contract/güvenlik matrisi.
- KG-019 ile ertelenen contract suite, dış sample ve operasyonel metric kulvarları.

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

## 15. Seam envanteri ve matris planı (BL-003)

Kullanıcı kararı: 22 sütunlu matris **tam kapsam, çok turlu** çalışır (bkz.
KG-007). `src/Tracon.Abstractions` tek başına **76** public interface,
diğer paketlerde **2** daha (`ITraconUiProvider`, `ITraconBuilder`)
taşıyor — toplam **78** aday seam. Skill'in örnek listesindeki 9 kategoriden
çok daha geniş. Matris interface başına değil **küme başına** doldurulur:
kümenin temsilci üyesi tam derinlikte ölçülür, kümenin geri kalanı sütun 1-2
ve dokümantasyon tutarlılığı için taranır; sapma bulunursa o üye ayrıca
derinleştirilir.

### Küme planı

| Küme | Kapsam | Üye sayısı | Öncelik | Durum |
|---|---|---|---|---|
| A | Runs & Observability: `IRunStore`, `IRunScoreStore`, `IRunInputStore`, `IRunEventSink`, `IRunAttributionContext`, `IRunCancellationRegistry`, `IRunErrorClassifier`, `IRunPricingResolver`, `ITraceStore`, `ITraconDrainState` | 10 | Yüksek — çekirdek çalıştırma yolu | **Tamamlandı** — 2× 🔴, 4× 🟡, 2× 🟢 (§6) |
| B | Tenancy, Security, Privacy: `ITenantContext`, `ITenantStore`, `ITenantEgressPolicyStore`, `ITenantProviderBindingStore`, `IApiKeyStore`, `IContentProtector`, `IDataSubjectStore`, `IDataSubjectResolver`, `IQuotaStore` | 9 | En yüksek — `secret`/kiracı sızıntı riski | **Tamamlandı** — 1× 🔴 (BL-006), 3× 🟡, 2× 🟢 (§6, §9) |
| C | Model Provider & Retry (BYOK): `IModelProvider`, `ITenantCredentialModelProvider`, `IModelProviderHealthCheck`, `IModelProviderRegistry`, `IProviderRetryClassifier`, `IModelProviderConfigurationDiagnostics` | 6 | En yüksek — capability fail-closed (4.4) | **Tamamlandı** — 0× 🔴, 2× 🟡, 2× 🟢 (§6) |
| D | Agents: `IAgentSource`, `IVersionedAgentSource`, `IAgentDecorator`, `IAgentCatalog`, `IAgentDefinitionStore`, `IAgentSkillStore`, `ISkillScriptGrantStore` | 7 | Yüksek | **Tamamlandı** — 0× 🔴, 4× 🟡, 2× 🟢 (§6) |
| E | Tools, Guards, Approvals: `IToolRegistry`, `IToolAuthorizationHandler`, `IContentGuard`, `IPendingApprovalStore`, `IToolApprovalRuleStore` | 5 | Yüksek — güvenlik sınırı (4.3) | **Tamamlandı** — 0× 🔴, 4× 🟡, 1× 🟢 (§6) |
| F | MCP / Transport: `IMcpOAuthCoordinator`, `IMcpPromptClient`, `IMcpResourceClient`, `IMcpResourceContextProviderFactory`, `IMcpToolRefresher`, `IMcpServerStore` | 6 | Yüksek — wire contract | **Tamamlandı** — 0× 🔴, 4× 🟡, 3× 🟢 (§6) |
| G | Workflows: `IWorkflowRunner`, `IWorkflowDefinitionStore`, `IWorkflowCheckpointStore`, `IWorkflowFunctionCatalog` | 4 | Orta | **Tamamlandı** — 1× 🔴 (sınıf tekrarı, BL-027 ile aynı kök neden), 3× 🟡, 1× 🟢 (§6) |
| H | Scheduling, Coordination, Idempotency, Webhooks, Triggers: `IJobStore`, `IJobHandler`, `IJobScheduleStore`, `ISingletonLeaseStore`, `IIdempotencyStore`, `IWebhookStore`, `IWebhookPublisher`, `IInboundTriggerStore` | 8 | Orta | **Tamamlandı** — 1× 🔴, 4× 🟡, 2× 🟢 (§6) |
| I | Sessions, Attachments, Retention, Evaluation, Experiments, Knowledge: `ISessionStore`, `IConversationBranchStore`, `IAttachmentStore`, `IAttachmentStorage`, `IRetentionStore`, `IRetentionPolicyStore`, `IArchiveSink`, `IEvalStore`, `IRunJudge`, `IExperimentStore`, `IMigrationApplier`, `ISqlPersistenceDiagnostics`, `IVectorSearchStore` | 13 | Orta | **Tamamlandı** — 0× 🔴, 4× 🟡, 1× 🟢 (§6) |
| J | Voice & Audit: `ISpeechSynthesizer`, `ISpeechTranscriber`, `IVoicePricingReader`, `IVoiceHealthCheck`, `IVoiceSessionStore`, `IAuditLog`, `IAuditActorResolver`, `IAuditDecorated` | 8 | Orta | **Tamamlandı** — 0× 🔴, 3× 🟡, 1× 🟢 (§6) |
| K | Builder/UI surfaces: `ITraconUiProvider`, `ITraconBuilder` | 2 | Düşük | **Tamamlandı** (elle ölçüldü, ajan gerekmedi) — 0× 🔴, 0× 🟡, bulgu yok |

**Sıra gerekçesi:** B ve C önce — kiracı/`secret`/BYOK sızıntısı ve capability
fail-closed ihlali preview'da bile 🔴 üretebilecek tek iki alan. E ve F hemen
ardından — tool authorization ve transport wire contract'ı sonradan kırmak
pahalı. A (çalıştırma yolu) ve D (agent source) sonra. G, H, I, J, K en
sonda — ölçülmüş kanıt bugüne kadar bu kümelerde bilinen bir kusur riski
göstermiyor.

Her küme tamamlandığında bu tablo güncellenir ve bulgular §6 (blocker) veya §9
(risk)'e taşınır. Matrisin ham hücreleri bu dosyada değil, kümeyi ölçen ajanın
raporunda tutulur; yalnız **bulgu** (tutarsızlık, dokümansız davranış,
fail-open) buraya girer — 78×22 boş matrisi doğrudan bu dosyaya basmak
doküman bütçesini anlamsızca şişirir.

### Küme B ve C sonuçları (2026-08-27)

**Küme C (Model Provider/BYOK) temiz çıktı:** `ModelProviderRegistry.BuildPipeline`
(`src/Tracon.Core/Models/ModelProviderRegistry.cs:396-402`), bir kiracı
credential'ı çözülmüş ama provider `ITenantCredentialModelProvider` değilse
sessiz global-credential düşüşü yerine `ProviderInvocationException.CredentialUnsupported`
fırlatıyor — dört adaptörün (Anthropic/Azure/Google/OpenAI) tamamında tutarlı,
dedicated testle kilitli (`ModelProviderRegistryTenantCredentialTests.cs:348`).
4.4 mercek için bu kümede 🔴 yok.

**Küme B (Tenancy/Security/Privacy) 1× 🔴 üretti:** `ITenantProviderBindingStore`
BYOK lookup'ı üç farklı case-sensitivity davranışı taşıyor (bkz. BL-006) —
tam olarak `ModelProviderRegistry`'nin kendi yorumunun reddettiği "sessiz
global credential düşüşü" senaryosunu üretebilir. Diğer sekiz seam temiz;
`IDataSubjectResolver`'ın kayıtsız durumda `409` dönmesi kümenin en iyi
fail-closed örneği olarak ölçüldü.

Bulguların tamamı §6'ya işlendi: BL-006 (🔴), BL-007/BL-008/BL-015 (🟡),
BL-016 (🟢, toplu).

### Küme E ve F sonuçları (2026-08-27)

**Küme E (Tools/Guards/Approvals) 0× 🔴 üretti — ajanın önerdiği iki 🔴 aday
(`kusur-giderme`'ye değil) Adım 7 filtresinden geçirilip 🟡'ye indirildi:**
"contract test eksikliği" skill'in kendi Adım 7 tablosunda açıkça 🟡
kategorisidir (`Executable contract eksikliği`), 🔴 değil; `IContentGuard`'ın
kayıtsızken fail-open olması ise **dokümante edilmiş, kasıtlı bir varsayılan**
— BL-006'nın aksine dokümante sözle çelişmiyor, yalnız operasyonel bir uyarı
eksik. CLAUDE.md'nin "Tool'lar yalnızca kodda tanımlanır" iddiası kodda
doğrulandı: `AgentDefinitionCompiler.ChatOptions.cs:78` veri/JSON kaynaklı tool
kaydını açıkça reddediyor.

**Küme F (MCP/Transport) 0× 🔴 üretti — temiz.** Kiracı izolasyonu OAuth token
cache'i için `McpTenantServerKey` ile yapısal olarak kilitli (önceki bir
string-interpolation sızıntısını kapatmak için özel olarak yazılmış);
kaynak/resource okumaları sunucunun kendi bildirdiği URI listesiyle
sınırlanıyor (SSRF koruması); OAuth token yenileme başarısızlığında sessiz
stale-token kullanımı yok, `fail-closed`. En büyük açık: `IMcpOAuthCoordinator`
— kümenin en yüksek riskli sınıfı — hiçbir testte referans edilmiyor.

Bulguların tamamı §6'ya işlendi: BL-017/BL-018/BL-019/BL-022/BL-023/BL-024
(🟡), BL-021/BL-025 (🟢, toplu).

### Küme A sonuçları (2026-08-27)

**Küme A (Runs/Observability) 2× 🔴 üretti — bu tur en ciddi kümesi:**

1. **Drain/yeni-run yarışı** — `AgentEndpoints.cs:193-196`'daki `DrainGate.Check`
   kontrolü, `IRunCancellationRegistry.Register`'ın gerçekten çağrıldığı
   `RunRecordingAgent.cs:214-218`'den **önce**, ama body binding/attribution/
   quota/preflight/catalog resolution'dan **sonra** çalışıyor. `ApplicationStopping`
   tam bu pencerede tetiklenirse `TraconDrainService.StopAsync` `ActiveCount==0`
   görüp hemen dönebilir — süreç, başlamak üzere olan bir run'ı yarıda
   kesebilir. Kanıt seviyesi kaynak izleme (Seviye 1-2); yarışı fiilen tetikleyen
   bir eşzamanlılık testi yok — `kusur-giderme`'ye devredilirken bu da istenmeli.
2. **`IRunStore`'a ham exception mesajı sızıyor** — `RunRecordingAgent.Completion.cs:282`
   `RunError.Message = exception.Message`'ı `ContentGuardPipeline`'dan
   **geçirmeden** yazıyor. Kardeş yol `IRunInputStore` tam olarak bu sınıf
   bir kusur için (`HATA-S3-006`) daha önce düzeltilmiş ve guard'dan geçiriliyor
   (`RunRecordingAgent.Persistence.cs:36-41`) — aynı düzeltme run-error yoluna
   uygulanmamış. Bu, K-059'un `secret` disiplini ruhuna doğrudan aykırı bir
   sınıf tekrarı örneği.

Diğer bulgular: cancellation cooperative-only ama arayüz dokümanında bu sınır
belirtilmiyor (🟡); tenant-mode dokümantasyonu yalnız `IRunStore`'da tam,
`ITraceStore`'un ambient-tenant davranışı dokümante değil (🟡); 6 arayüz için
contract test yok, 8/10 için dış sample yok (🟡, BL-008 ile aynı repo-geneli
kayıt ergonomisi deseni tekrar gözlendi — ayrı kayıt açılmadı). Bulguların
tamamı §6'ya işlendi: BL-026/BL-027 (🔴), BL-028/BL-029/BL-030 (🟡), BL-031
(🟢, toplu).

### Küme D sonuçları (2026-08-27)

**Küme D (Agents) 0× 🔴 üretti.** Görevin şüphelendiği iki en kritik davranış —
decorator kompozisyon sırası ve version-not-found — ikisi de ölçümde
fail-closed çıktı: sıralama framework tarafından sabit (`OrderByDescending`,
consumer DI kaydıyla değiştiremez), olmayan bir agent versiyonu sessizce
"latest"e düşmüyor, `TraconException` fırlatıyor. En dikkat çekici bulgu
`IAgentDecorator.Order`'ın XML dokümanının **kendi kendiyle çelişmesi** —
"lower value wraps inside, higher value wraps outside" cümlesi gerçek
davranışın (düşük = dıştan, yüksek = içten; `RunRecordingAgentDecorator`
Order=0/dıştan, `ToolApprovalAgentDecorator` Order=20/içten) tam tersini
söylüyor. Güvenlik-ilişkili bir decorator yazan üçüncü taraf bu cümleye
güvenirse yanlış katmana yerleştirebilir — runtime doğru, doküman yanlış.

Diğer bulgular: decorator exception'ları source exception'larının aksine
normalize edilmiyor, ham exception ASP.NET Core'un varsayılan handler'ına
kadar sızabilir (doğrulanmadı, derinleştirme gerekiyor); `IAgentDecorator`
için hiç builder API'si yok (yalnız ham `TryAddEnumerable`); `IAgentDefinitionStore`
ambient tenant kullanırken `IAgentSkillStore`/`ISkillScriptGrantStore` explicit
`tenantId` parametresi kullanıyor — aynı kümede tutarsız desen. Bulguların
tamamı §6'ya işlendi: BL-033/BL-034/BL-035 (🟡), BL-036 (🟢, toplu).

### Küme G sonuçları (2026-08-27)

**Küme G (Workflows) 1× 🔴 üretti — ve bu BL-027'yle AYNI kusur sınıfının
ikinci örneği:** `WorkflowRunner.ToRunError` (`WorkflowRunner.cs:1179-1198`)
yakaladığı exception'ın ham `.Message`'ını `RunEvent.Text`'e **hiçbir
guard'dan geçirmeden** yazıyor — `IRunStore`'daki `RunError.Message` sızıntısıyla
(BL-027) birebir aynı desen, farklı bir çalıştırma yolunda. Bu, CLAUDE.md'nin
kendi tuzak kaydının tarif ettiği "aynı kusur sınıfı defalarca tekrarladı"
örüntüsünün tam bu turda yakalanmış hâli — `kusur-giderme`'nin SINIF TARAMASI
adımı bu ikisini birlikte kapatmalı, ayrı ayrı değil.

Ayrıca: workflow resume'un side-effecting adımları **tekrar çalıştırabileceği**
(at-least-once semantics) yalnız `AddWorkflowFunction<T>()`'ın XML dokümanında
(`TraconWorkflowFunctionExtensions.cs:66-77`) anlatılıyor — bu bilginin asıl
karşılığı olması gereken `IWorkflowRunner`/`IWorkflowCheckpointStore`
(`Tracon.Abstractions`, paketin asıl public sözleşme yüzeyi) bundan hiç
bahsetmiyor. Davranışın kendisi doğru ve kasıtlı (tool'lardaki `SafeToRepeat`
deseniyle tutarlı), yalnız yanlış dosyada dokümante — 🟡.

Bulguların tamamı §6'ya işlendi: BL-037 (🔴, BL-027 ile bağlantılı), BL-038/
BL-039 (🟡), BL-040 (🟢, toplu).

### Küme H sonuçları (2026-08-27)

**Küme H (Scheduling/Coordination/Idempotency/Webhooks/Triggers) 1× 🔴
üretti:** `IIdempotencyStore` — kendi dokümanına göre "özellikle bu iş için"
var olan tip — job dispatch loop'unda (`JobWorkerBackgroundService.cs`) hiç
kullanılmıyor. `IJobHandler.cs`'nin kendi örneği (`NightlyReportJobHandler`)
lease kaybı/retry sonrası `context.Items`'ın tamamen yeniden geleceğini ve
zaten `Completed` item'ların da geleceğini söylemiyor — built-in handler'lar
(`AgentBatchJobHandler`, `WorkflowJobHandler`, `EvalJobHandler`) bunu savunmacı
`item.Status != Pending` kontrolüyle örtük olarak çözüyor ama bu **sözleşme
değil, kabile bilgisi**. Dokümante edilen örneği harfiyen izleyen bir
tüketici crash/lease-kaybı/retry'de side effect'i (örn. e-posta) iki kez
gönderir.

Diğer bulgular: `ISingletonLeaseStore`'un lease-sahibi donduğunda oluşan
sınırlı split-brain penceresi dokümante değil; webhook teslimatı at-least-once
ama arayüz dokümanında belirtilmiyor (dedup anahtarı yalnız kaynak kodunda
görülebiliyor); kümede hiç `span`/metric yok; 6 store arayüzünün lifetime/
thread-safety dokümantasyon eksikliği zaten bilinen tekrarlayan desenin
(BL-024/BL-029/BL-039) bir örneği daha — ayrı kayıt açılmadı.

Bulguların tamamı §6'ya işlendi: BL-041 (🔴), BL-042/BL-043/BL-044 (🟡),
BL-045 (🟢, toplu).

### Küme K sonuçları (2026-08-27, elle ölçüldü)

**Küme K (Builder/UI) 0× 🔴, 0× 🟡 üretti — tek düşük öncelikli, gerçekten
temiz küme.** `ITraconBuilder`'ın kendisi bu ölçümde görülen en iyi
dokümante edilmiş arayüz — her metotta thread-safety notu, AOT annotasyonu
(`[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` doğru yerlerde) ve
örnek kod var. `AddModelProvider`'ın generic `<T>()` overload'ı olmadığı
burada da doğrulandı (BL-016'nın parçası, yeni kayıt açılmadı).
`ITraconUiProvider` da güçlü dokümante — `HasAssets=false` durumunda
boş sayfa yerine `404` dönmesi (build-time varlık eksikliğinde) kasıtlı ve
dokümante bir fail-safe. Contract test taban sınıfı yok ama bu arayüz zaten
E2E/functional testlerle (`Tracon.Ui.E2ETests`, `Tracon.AspNetCore.FunctionalTests/SecurityTests.cs`)
kapsanıyor — HTTP sunan bir seam için makul bir seçim, ayrı bulgu açılmadı.

### Küme J sonuçları (2026-08-27)

**Küme J (Voice/Audit) 0× 🔴 üretti.** `IAuditLog`'un fail-open tasarımı
(audit write başarısız olursa asıl işlem yine de tamamlanır) doğrulandı ve bu
**sessiz değil** — `AuditRecorder.WriteAsync` her başarısızlığı loglar.
`AuditSecretFilter.Redact` tüm decorator'ları tek bir merkezi noktadan
geçiriyor, atlanamaz. En yakın 🔴 adayı (`AuditQuery.TenantId=null` →
`InMemoryAuditLog`'da tüm kiracıları dönebilme) bugün hiçbir shipped kod
yolunda tetiklenmiyor (`AuditEndpoints.cs` her zaman tenant'ı açıkça geçiyor)
— 🟡'ye indirildi.

Bulguların tamamı §6'ya işlendi: BL-046/BL-047/BL-048 (🟡), BL-049 (🟢, toplu).

### Küme I sonuçları (2026-08-27) — SON KÜME

**Küme I (Sessions/Attachments/Retention/Evaluation/Experiments/Knowledge)
0× 🔴 üretti.** Üç en olası 🔴 adayı — attachment'ta path traversal, judge
başarısızlığında sessiz geçer not, cross-tenant sızıntı — üçü de temiz
çıktı: attachment fiziksel anahtarı her zaman sunucu üretimli GUID (dosya adı
yalnız metadata), `IRunJudge` parse hatasında `Score: null` yazıyor (asla
sessiz `0` değil, "a silent 0 is NOT written" yorumuyla açıkça belgelenmiş),
`EvalRunQuery.TenantId` K-277 sonrası `required` (önceden nullable="tüm
kiracılar" tuzağıydı, kasıtlı düzeltilmiş). `IRunJudge` kümenin referans
deseni: tam kayıt üçlüsü, contract test hem built-in hem dış
`CustomRunJudge` sample'ında koşuyor — diğer 10 arayüz bu deseni taşımıyor.

Bulguların tamamı §6'ya işlendi: BL-050 (🟡), BL-051 (🟢).

---

## 11/11 küme tamam — özet

| Küme | 🔴 | 🟡 | 🟢 |
|---|---|---|---|
| A (Runs/Observability) | 2 | 4 | 2 |
| B (Tenancy/Security/Privacy) | 1 | 3 | 2 |
| C (Model Provider/BYOK) | 0 | 2 | 2 |
| D (Agents) | 0 | 4 | 2 |
| E (Tools/Guards/Approvals) | 0 | 4 | 1 |
| F (MCP/Transport) | 0 | 4 | 3 |
| G (Workflows) | 1 | 3 | 1 |
| H (Scheduling/Coordination/Idempotency/Webhooks/Triggers) | 1 | 4 | 2 |
| I (Sessions/Attachments/Retention/Evaluation/Knowledge) | 0 | 4 | 1 |
| J (Voice/Audit) | 0 | 3 | 1 |
| K (Builder/UI) | 0 | 0 | 0 |
| **Toplam** | **5 kayıt / 4 bağımsız kusur sınıfı** | **35** | **17** |

BL-027 ve BL-037 tek bir kusur sınıfının iki bağımsız örneği (ham exception
mesajının `ContentGuardPipeline`'dan geçirilmeden persist edilmesi) —
`kusur-giderme`'ye tek sınıf taraması olarak birlikte gider. Kalan üç 🔴
(BL-006, BL-026, BL-041) birbirinden bağımsız.

**En dikkat çekici cross-cluster örüntü:** `IRunStore` (Küme A) ve `IRunJudge`
(Küme I) bu ölçümde görülen iki referans-kalite arayüz — tam dokümantasyon,
tam kayıt üçlüsü, gerçek contract test + dış sample. Geri kalan ~74 arayüzün
çoğu aynı bar'a ulaşmıyor: kayıt ergonomisi (dedicated `AddX()` yok),
singleton/thread-safety dokümantasyonu ve contract test/sample kapsamı
tutarlı biçimde eksik. Bu, tek tek düzeltilecek 30+ ayrı 🟡 değil, **tek bir
sistemik desen** — `IRunJudge`'ın kayıt+contract+sample üçlüsü şablon
alınarak kalan arayüzlere uygulanabilir.

---

## 16. Sınıf taraması: ham exception → kalıcı/dışa açık durum (BL-027 · BL-037)

> **✅ Kapandı — [Faz 119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md), K-640 (2026-08-27).**
> Aşağıdaki 21 vaka artık `Tracon.SafeErrorText` üzerinden geçiyor; uygulama
> sırasında 5 ek vaka daha bulundu ve kapatıldı (`EgressAddressValidator`,
> `ConversationBranchService`, `RetentionJobHandler`, `RetentionExecutor`,
> `ModelRunJudge`) — toplam 26. Bu bölüm artık **tarihsel kanıt kaydı**dır,
> güncel durum için Faz 119 dokümanına bakın.

`kusur-giderme` Adım 5 uygulandı. Denetim iki vaka bildirmişti; tarama **19 vaka
daha** buldu. Kusur artık "iki satırı guard'dan geçir" değil, **sistemik bir
kapsam sorunudur**: `ContentGuardPipeline`'ın `src/` içinde yalnız iki tüketicisi
var — `ContentGuardingChatClient` (model giriş/çıkışı) ve `HATA-S3-006`
düzeltmesi (`RunRecordingAgent.Persistence.cs:41-46`). Kalıcılaştıran veya dışa
gönderen **başka hiçbir yol** guard'dan geçmiyor.

### Kalıcılaştıran vakalar (12)

| file:line | Giren metin | Nereye düşüyor |
|---|---|---|
| `Recording/RunRecordingAgent.Notifications.cs:138` | bilinen vaka 1'in aynı `RunError`'ı | `WebhookRunSummary.Error` → `webhook_deliveries.payload` **ve kiracının tanımladığı dış URL'ye POST edilir** — kutudan çıkıyor |
| `Scheduling/AgentRunJobHandler.cs:265` | kuyruklu run'ın provider SDK exception'ı | `runs.error_message` |
| `Scheduling/RunContinuationJobHandler.cs:200` | aynı | aynı |
| `Approvals/ApprovalResumeJobHandler.cs:174` | aynı | aynı |
| `Webhooks/WebhookDeliveryJobHandler.cs:327` | **uzak webhook hedefinin ham HTTP gövdesi** (`$"HTTP {status}: {body}"`) | `webhook_deliveries.error` — tamamen üçüncü taraf kontrolünde |
| `Webhooks/WebhookDeliveryJobHandler.cs:335` | `HttpRequestException.Message` (host:port taşır) | aynı |
| `Scheduling/JobWorkerBackgroundService.cs:269` | **her** handler'ın exception'ı (AgentRun · Eval · Webhook · Workflow) | `jobs.error_message` — kodun en geniş hunisi |
| `Scheduling/JobWorkerBackgroundService.cs:280` | aynı | `ReleaseForRetryAsync(...)` |
| `Scheduling/AgentBatchJobHandler.cs:84` | provider SDK exception | `job_items.error` |
| `Scheduling/WorkflowJobHandler.cs:87` | aynı (`:70` ayrıca bilinen vaka 2'nin çıktısını kopyalar) | aynı |
| `Evaluation/EvalJobHandler.cs:351` | `agent.RunAsync` exception'ı | `eval_case_results.failure_reason` |
| `Recording/RunReconciliationService.cs:328` | job-store exception (iç kaynaklı — daha düşük) | `runs.error_message` |

### Dışa açık vakalar (8)

Hepsi, kendi yorumları "provider SDK exception'ları ortak bir taban tip
paylaşmadığı için" geniş `catch (Exception)` olduğunu söyleyen bloklarda —
sonra o exception'ın mesajını çağırana aynen yazıyorlar.

| file:line | Sink |
|---|---|
| `AgentEndpoints.cs:1135` | SSE `error` frame |
| `AgentEndpoints.cs:1233` | 502 `ProblemDetails.detail` |
| `OpenAICompat/OpenAIChatCompletionsEndpoints.cs:163`,`:318` | 502 gövde / SSE hata gövdesi |
| `OpenAICompat/OpenAIResponsesEndpoints.cs:237`,`:397` | 502 gövde / SSE hata |
| `RunEndpoints.cs:603` | replay 502 `detail` |
| `ImageEndpoints.cs:169` | 502 `detail` (`:157` `HttpRequestException` için doğrusunu yapıyor — `:169` sızıntı) |
| `McpServer/CatalogToolCallHandler.cs:106` | MCP `CallToolResult` hata metni (BL-025'te kayıtlıydı, doğrulandı) |
| `Mcp/McpOAuthAuthorizationCoordinator.cs:253` | `McpOAuthCompleteResult.Error` → `GovernanceEndpoints.cs:84` → **HTML sayfasına basılır**; bu uç **bearer auth'tan muaf** (`GovernanceEndpoints.cs:40`) ve token-exchange bacağı `client_secret` yankılayabilir |

OpenAI-uyumlu yüzey en açık olanı: dış OpenAI istemcileri için drop-in olarak
tasarlandı.

### Doğru desenin repo içindeki emsalleri

Düzeltme sıfırdan tasarlanmayacak — repo bunu üç yerde zaten doğru yapıyor:
`ToolFailureText.cs:6-11` (yalnız tip adı), `ElevenLabsSpeechClient.cs:637-659`
(provider gövdesi okunur ve **atılır**), dört provider health check
(`OpenAIProviderHealthCheck.cs:84` vd. — `exception.Message`'ı host:port taşıdığı
için açıkça reddeder). `RunTraceCollector.cs:282-286` farklı ama gerçek bir
guard taşır (`IsSensitive("error.message")` + `RecordSensitiveData`).

### En yüksek kaldıraçlı üç düzeltme

1. `JobWorkerBackgroundService.cs:269,280` — tek nokta, **her** job türünü kapatır.
2. `RunRecordingAgent.Notifications.cs:138` — ham provider metninin kutudan çıktığı tek yol.
3. `WebhookDeliveryJobHandler.cs:327` — uzaktan kontrol edilen gövdenin kalıcılaştığı yer.

### Yargı gerektiren, ölçülmesi gereken 5 kalem — Faz 119 kapanış kararı

`RetentionJobHandler.cs:35` · `RetentionExecutor.cs:202` (Npgsql mesajı SQL metni
taşıyabilir) · `WorkflowRunner.cs:890` ← `WorkflowResponseFactory.cs:106`
(kullanıcı girdisi kaynaklı `JsonException`) · `ModelRunJudge.cs:255` →
`run_scores.comment` (model çıktısı kaynaklı) · `WorkflowEndpoints.cs:636` ve
`VoiceConversationDriver.cs:554` (dar filtre; yalnız `HttpRequestException` kolu
host:port sızdırır).

**Sonuç (2026-08-27):**

| Kalem | Karar | Gerekçe |
|---|---|---|
| `RetentionJobHandler.cs:35` | Düzeltildi | `SafeErrorText` + yeni opsiyonel `ILogger<RetentionJobHandler>` |
| `RetentionExecutor.cs:202` | Düzeltildi | `SafeErrorText` + mevcut `ILogger<RetentionExecutor>` |
| `ModelRunJudge.cs:255` | Düzeltildi | `ParseJudgment` artık `ILogger` alıyor; `SafeErrorText` uygulanıyor |
| `WorkflowEndpoints.cs:636` + `VoiceConversationDriver.cs:554` | Düzeltildi | `TraconException` kolu korunur, `HttpRequestException`/`InvalidOperationException` kolu `SafeErrorText`'e yönlendirildi |
| `WorkflowRunner.cs:890` ← `WorkflowResponseFactory.cs:106` | **Kapsam dışı bırakıldı (gerekçeyle)** | `WorkflowRunner.cs:890`'daki `catch (TraconException exception)` zaten kural #1'i sağlıyor (mesaj bizim). Asıl soru `WorkflowResponseFactory.cs:106`'nın kendi `TraconException`'ının mesajına bir iç `JsonException.Message` gömmesi — ama bu, workflow'u DEVAM ETTİRMEK için cevap gönderen AYNI çağrının KENDİ gönderdiği bozuk JSON'ı açıklıyor (self-referential doğrulama geri bildirimi, `OpenAIResponsesEndpoints.HandleAsync`'in istek gövdesi ayrıştırma hatasıyla aynı desen — bkz. `raw-exception-text-baseline.txt`). Host/credential/altyapı detayı taşımaz |

Ayrıca sınıf taraması bu 21+5 kalemin ÜSTÜNE **5 vaka daha** buldu (uygulama
sırasında, mimari cırcır kapısı + elle inceleme ile): `EgressAddressValidator.cs:290`
(DNS/argüman hatası — düzeltildi, yalnız tip adı tutulur; paylaşılan statik
sınıfa `ILogger` eklemek üç çağıran yüzeyi ölçüsüz büyütür, bu yüzden korelasyon
kimliği YOK — SocketException/ArgumentException mesajı zaten yalnız çağıranın
KENDİ verdiği host adını anlatır, `secret`/host:port taşımaz), `ConversationBranchService.cs:146`
(düzeltildi, yeni opsiyonel `ILogger<ConversationBranchService>`),
`RetentionJobHandler.cs`, `RetentionExecutor.cs`, `ModelRunJudge.cs` (üçü de
yukarıda). Toplam kapatılan vaka: **26**.

**Mimari cırcır kapısının bilinen kapsam sınırı:** `RawExceptionTextSiteTests`
yalnız `catch (Exception` (isimsiz/genel) şeklini tarar; `catch (HttpRequestException`
gibi isimli bloklar kapsam dışıdır — bu fazda ELLE incelendi ve gerekliyse
düzeltildi, ama gelecekte isimli bir catch'te YENİ bir sızıntı açılırsa kapı
onu YAKALAMAZ. Kabul edilen bir sınır (K-640); genişletme ayrı bir kalem.
