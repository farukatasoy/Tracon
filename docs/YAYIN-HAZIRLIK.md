# Tracon Yayın Hazırlığı

> Bu dosya, Tracon paket ailesinin ilk NuGet.org yayını için yaşayan kontrol
> düzlemidir. Faz planı, sohbet özeti veya genel karar defteri değildir. Yalnız
> ölçülen kanıtı, yayın kararlarını, risk kabulünü ve doğrulama durumunu taşır.
>
> **Son güncelleme:** 2026-09-14  
> **Çalışma modu:** `nuget-danismani` — Yayın kararı  
> **🚨 Güncel karar §4'ün başındaki 2026-09-14 bloğudur.** 2026-09-03 kararı
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
> **Geçici karar:** ❌ Yayınlanmamalı — **açık 🔴 olduğu için değil**, yayın
> kritik yolu henüz kapanmadığı için. Açık 🔴 yoktur; UR-003 GA'ya ertelendi
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
> kapının dördü de yeşil. `preview.1` tag'inden önce kalan **yalnız hesap
> kararları**: OP-008/009, ilk 72 saat sorumluluğu ve private repo + GitHub Free
> için CI secret/protection modeli — tamamı repo-dışı kullanıcı işidir. GitHub Free yayını engellemez;
> ancak private repo'da environment secret, required reviewer ve deployment
> tag restriction sağlamaz (KN-020). Yayın
> türü `preview` (UR-001), paket kapsamı tam entegrasyon seti (UR-002/BL-002)
> ve owner modeli kişisel hesap (OP-001) sabit — bkz. §4 ve §13.

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

| Paket | Profil | Hedef TFM | İlk durum |
|---|---|---|---|
| `Tracon` | Meta paket | `net8.0;net9.0;net10.0` dependency group | İnceleniyor |
| `Tracon.Abstractions` | Library | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Anthropic` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.AspNetCore` | HTTP/transport host | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Azure` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Cli` | .NET tool | `net10.0` | İnceleniyor |
| `Tracon.Client` | Generated management client | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Core` | Runtime | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Google` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Mcp` | MCP client/tool integration | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.OpenAI` | Provider adapter | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.PostgreSql` | Storage provider | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.SqlServer` | Storage provider | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Sqlite` | Storage provider | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Templates` | `dotnet new` content package | `net10.0` build host | İnceleniyor |
| `Tracon.Testing` | Test helper library | `net10.0` | İnceleniyor |
| `Tracon.Testing.Contracts.Xunit` | Reusable contract suite | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.UI` | Embedded UI | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Voice` | Voice tool library | `net8.0;net9.0;net10.0` | İnceleniyor |
| `Tracon.Workflows` | Workflow runtime | `net8.0;net9.0;net10.0` | İnceleniyor |

Not: K-602'nin metni 19 paket der. Güncel kaynak 20 paket gösterir. Yeni paket
eklendiğinde kimlik kümesini dinamik çıkaran kapı bunu kapsar. Karar metnindeki
sayının ürün politikası mı yoksa bayat ölçüm mü olduğu artifact sonrasında
değerlendirilecektir.

## 4. Mevcut net yayın kararı

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
| 8 | Dört `WriteAuditOrThrowAsync` kopyası tek politika değil; `ApprovalEndpoints.cs:345` kopyayı kendi yorumunda kabul ediyor | 🟡 | ✅ **kapandı 2026-09-15** — [Faz 171](171-DENETIM-IZI-YAZMA-POLITIKASI.md); BL-047 aynı fazda kapandı |
| 9 | Tehdit modeli dokümanı yok (`threat model`/`STRIDE` → `docs/` genelinde 0) | 🟡 | **Planlandı 2026-09-15** — [Faz 172](172-TEHDIT-MODELI.md) |
| 10 | SBOM üretimi ve paket imzalama yok | 🟢 | **GA hattı** — K-777, gerekçesiyle ertelendi |
| 11 | `KARARLAR.md`'de K-662…K-777 arası kararlar tek bir kod bloğunun (satır 738–838) içinde kalıyor; tablo olarak render olmuyor | 🟢 | **Açık** — indeks üreteci etkilenmiyor, yalnız okunabilirlik |

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

### Henüz ölçülmeyen alanlar

- Tam `.nuspec` dependency graph'ının paket stratejisine göre değerlendirilmesi ve beklenmeyen içerik taraması.
- Gerçek Source Link kaynak çözümleme davranışı. Yerel ortamda `dotnet sourcelink` aracı yoktur.
- Meta paketin PDB içermeyen `.snupkg` üretmesinin NuGet.org davranışı ve gerekliliği.
- `Tracon.Cli` paketinin yaklaşık 28 MB boyutunun içerik ve support açısından değerlendirilmesi.
- Public API yaprakları ve her yüzey için tut/değiştir/kaldır/internal/capability/ertele kararı.
- Güvenlik ve transport sınırlarının artifact tabanlı runtime probe'ları.
- XML, package README, root README, `docs-site`, sample ve release note drift'i.
- NuGet.org hesap, sahiplik, 2FA, Package ID uygunluğu ve publishing credential durumu.
- Güncel resmi NuGet operasyon seçenekleri ve trusted publishing uygunluğu.
- Tam manuel kabul setinin güncel koşumu.

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
| BL-005 | Doğrulama gerekli | Meta paket PDB içermeyen `.snupkg` üretiyor | 🟢 Dokümantasyon veya cila | Artifact içinde 4 metadata girdisi ve 0 PDB ölçüldü; tüketici etkisi veya NuGet.org reddi yeniden üretilmedi | `nuget-danismani`; aksiyon çıkarsa faz zinciri | Resmi NuGet davranışı + push olmayan doğrulama |
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
| BL-047 | **KAPANDI (Faz 171, 2026-09-15)** | Audit-write başarısızlığı loglanıyor (`AuditRecorder.cs:53-60`) ama dedicated metric/counter yok — production'da audit-log bozulmasını yakalamak tamamen log taramasına bağlı | 🟡 1.0 blocker | `AuditRecorder.cs`; `TraconMetrics.cs` (audit counter yok) | **[Faz 171](171-DENETIM-IZI-YAZMA-POLITIKASI.md)** | `tracon.audit.write_failures` eklendi, `tracon.audit.outcome` etiketiyle `swallowed`/`refused` ayrılıyor. Aynı fazda F-234 de kapandı: dört `WriteAuditOrThrowAsync` kopyası tek `AuditRecorder.WriteOrThrowAsync`'e indi ve `AuditWritePolicyTests` altıncı kopyayı derleme yerine **test** hatası yapıyor |
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
| RK-006 | İnceleniyor | Meta paketin boş symbol package'i ve CLI'ın 28 MB paketi kapıdan geçiyor, fakat kapı içerik uygunluğunu yargılamıyor | Orta | Kesin / bilinmiyor | Resmi NuGet symbol davranışı ve package content audit | `nuget-danismani` |
| RK-007 | **Kapandı (BL-006/K-639)** | `ITenantProviderBindingStore` case-sensitivity tutarsızlığı BYOK credential'ının sessizce global setup credential'ına düşmesine yol açabilirdi | ~~Yüksek~~ | Ölçülen vaka kapandı | Canonical provider-name normalizasyonu, üç SQL migration'ı ve dört implementasyonda contract testleri eklendi | `kusur-giderme` |
| RK-008 | **🟡'ye indirildi (BL-026)** | Drain state kendi başına tam reservation garantisi vermez | Orta | İş kaybı yeniden üretilemedi; Kestrel request draining ve job wait iki bağımsız yedek mekanizma sağlar | Sınır XML dokümanında açıklandı; ölçülmüş iş-kaybı repro'su çıkarsa yeniden açılır | GA turu |
| RK-009 | **Kapandı (Faz 119/K-640)** | Ham exception mesajı sızıntısı kalıcı ve dışa açık yüzeylerde bir kusur sınıfıydı | ~~Yüksek~~ | 26 vaka kapatıldı | `SafeErrorText` tüm 26 siteye uygulandı; `RawExceptionTextSiteTests` yeni sızıntıları fail-closed yakalar | `kusur-giderme` |
| RK-010 | **Kapandı (Faz 120)** | `IJobHandler`'ın sözleşmesi at-least-once'ı söylemiyordu (BL-041) — dokümante edilen örneği izleyen bir tüketici crash/retry'de side effect'i iki kez çalıştırabilirdi | ~~Yüksek~~ | Orta (lease kaybı/retry production'da olağan) / yüksek (dokümante edilen doğrudan örnek yanlış) | `IJobHandler.cs`'nin XML dokümanına at-least-once uyarısı ve süzülmemiş `Items` notu eklendi; `JobHandlerContract` kuralı kilitliyor, `JobLeaseExpiryTests` davranışı ölçüyor. `IIdempotencyStore`'u job loop'una bağlamak değerlendirilmedi — BL-041'in kapanış notunun gerekçesiyle gereksiz ikinci bir mekanizma olurdu | `nuget-danismani` → `kusur-giderme` → [Faz 120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) |
| RK-011 | **Kapandı (KG-021/KN-021)** | NuGet ve npm kanallarının paralel basılması kalıcı asimetrik yayın üretebilirdi | ~~Yüksek~~ | Yapısal risk kapandı; npm credential yetkisi ilk publish işinde ölçülecek | `publish` npm işine bağlandı; kullanıcı npm scope ve `NPM_TOKEN` hazırlığını doğruladı | Yayın operasyonu |
| RK-013 | **Kabul edildi (2026-09-03, K-659)** | `RepositoryUrl` private bir repo'yu gösterir: üçüncü taraf için Source Link kaynak çözemez ve `.snupkg` sembolleri kaynak adımlamasına açılmaz | Orta | Kesin / düşük-orta | Tüketiciye dönük iki URL siteye çevrildi, yani okura sunulan hiçbir bağlantı ölü değil. Sembol paketleri yine yayımlanır (yığın izi satır numarası taşır). Repo public yapılırsa kendiliğinden çözülür | `nuget-danismani` |
| RK-012 | **Kabul edildi** | Kişisel owner modeli (OP-001): 20 paketin sahipliği tek hesaba bağlıdır; devir paket başına elle yapılır ve hesap kaybı 20 kimliği birden etkiler | Orta | Düşük / yüksek | Kullanıcı bilinçli olarak kabul etti (2026-08-27). Azaltım: 2FA (OP-002) ve gerekirse sonradan organization'a devir | Yayın operasyonu |

## 10. Yayın checklist'i

Bu checklist hem `preview.1` hem GA sertleştirme envanterini taşır. §13'te
GA'ya ertelendiği açıkça yazılan teknik `[ ]` kalemler, ilk preview'ın hesap ve
operasyon kritik yolunu yeniden açmaz.

### Ürün ve artifact

- [x] Hedef yayın türü kullanıcı tarafından onaylandı: `preview` (UR-001).
- [ ] Çalışma sürümü `1.0.0-preview.1`; gerçek tag öncesi kullanıcıdan son sürüm onayı alınmadı.
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
- [ ] `.snupkg` envanteri ölçüldü; Source Link üçüncü taraf için **çözemez** — repo private (RK-013, kabul edildi).
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

- [x] NuGet.org hesabı doğrulandı; trusted publishing policy oluşturuldu (KN-022).
- [x] NuGet.org'un zorunlu Microsoft-account 2FA sınırı authenticated policy oluşturma akışında geçildi (OP-002/KN-022).
- [x] Owner modeli seçildi: kişisel `farukatasoy` hesabı (OP-001).
- [x] 20 Package ID'nin uygunluğu ölçüldü ve kişisel sahiplik planı seçildi (OP-001/003).
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
- [ ] Gerçek yayın için kullanıcıdan açık onay alındı.

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

## 12. Ertelenen işler ve gerekçeleri

| Kimlik | Konu | Durum | Gerekçe | Yeniden açılma ölçütü |
|---|---|---|---|---|
| ER-001 | `stable 1.0` freeze | Ertelendi | Preview artifact ve üçüncü taraf feedback kanıtı yok; MAF Hosting/A2A pre-release | Stable ölçütlerinin tamamlanması |
| ER-002 | Gerçek NuGet.org push | Ertelendi | Açık kullanıcı onayı ve operasyon checklist'i yok | Tüm preview blocker'lar kapanır ve kullanıcı onay verir |
| ER-003 | Kapsamlı düzeltme fazı | Ertelendi | Henüz doğrulanmış aksiyon kapsamı yok | Audit ölçülmüş bir iş üretir ve kullanıcı faz açılmasını onaylar |

## 13. Sonraki adım

**Güncel sıradaki iş yayın hesapları ve operasyon hazırlığıdır.** Repo içi yayın
kritik yolu Faz 123 ve KG-022 ile kapandı. Açık iş yeni bir faz değildir. §10'daki
NuGet.org/npm hesap, authentication, GitHub secret/protection ve ilk 72 saat
kararları kapanır; sonra taze dry-run ve açık tag onayı alınır.

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
| Yayın öncesi | **Tamamlandı (2026-09-03)** | Sorumlu: tek bakımcı (`farukatasoy`). Güvenlik kanalı `SECURITY.md` (özel e-posta, 72 saat içinde onay); kusur kanalı GitHub issue şablonları (bug · dokümantasyon). 🚨 **Repo private olduğu sürece ikisi de dış tüketiciye görünmez** — public kanal bugün yalnız doküman sitesidir; repo public yapılana kadar bu bir kabul edilen boşluktur |
| 0–2 saat | Bekliyor | NuGet.org paket sayfaları, dependency graph, README/icon/license, symbol görünürlüğü ve temiz makinede install doğrulanır |
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
