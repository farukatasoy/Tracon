# Faz 105 — DI Bileşen Kökü Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Mevcut iki `AddTracon` ve `UseScheduling` imzası değişmez. `EnablePublicApiTracking` açıktır (K-421); `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza, XML metni, HTTP ucu, ekran ve sevk edilen yapılandırma anahtarı değişmez
> **Manuel test alanı:** [`manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) · [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 11edb8d:docs/arsiv/fazlar/105-DI-BILESEN-KOKU-AYRISTIRMA.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`TraconServiceCollectionExtensions.cs`, DI kayıtlarını ve 40'tan fazla options bağlayıcısını aynı dosyada tutuyor. Faz, bu sınıfı public yüzeyi değiştirmeden sorumluluk odaklı `partial` dosyalara ayırır. Tüketicinin kayıt sırası, `TryAdd*` davranışı ve varsayılanları birebir kalır.

## Bitiş Ölçütleri (DoD)

- [x] Ana facade yalnız public girişleri ve üst düzey yönlendirmeyi taşır; registration ve binding gövdeleri sorumluluk dosyalarındadır
- [x] Refactor öncesi ve sonrası service descriptor snapshot'ı sıfır fark verir
- [x] `TraconOptionsBindingCoverageTests` ve ilgili registration testleri yeşildir
- [x] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Plandan Sapmalar

- **Binding dosya sayısı 3 değil 4.** 105.2'nin metni üç grup örnekliyordu
  (model/pricing/image/attachment/agent-graph · scheduling/quota/rate-limit/
  async-run/reconciliation · security/egress/approval/protection/webhook/
  retention). On dört `Bind*` yardımcısı (`BindTools`, `BindPreflight`,
  `BindModelConcurrency`, `BindValidation`, `BindAudit`, `BindSkills`,
  `BindSkillScripts`, `BindList`, `BindCircuitBreaker`, `BindHealth`,
  `BindRunRecording`, `BindObservability`, `BindOnlineEvaluation`,
  `TryReadBool`, artı yeni `BindCoreFields`) üç grubun hiçbirine temiz
  oturmadı; bunlar `TraconServiceCollectionExtensions.Binding.Core.cs`
  adıyla dördüncü bir dosyaya toplandı. Skill'in kendi metni bunu açıkça
  serbest bırakıyor ("Dosya adları uygulama anında sorumluluk kümeleri
  ölçülerek daraltılabilir... Tek koşul, her dosyanın tek bir kayıt veya
  binding ekseni taşımasıdır") — dördüncü dosya da tek eksen taşıyor
  ("çekirdek/gözlemlenebilirlik ayarları"), sapma yalnız isimlendirme.
- **`Registration.Core.cs` iki yardımcı taşıyor, tek değil.** Plan metni
  "çekirdek katalog/derleme, run yaşam döngüsü, store varsayılanları,
  işletim özellikleri ve doğrulayıcılar" olmak üzere beş kavramdan
  bahsediyordu ama dosya listesi yalnız üç `Registration.*.cs` adı
  veriyordu. Gerçekleşen: `RegisterCoreInfrastructure` (model provider
  registry, tool registry, compiler zinciri, audit — orijinal dosyanın
  282-521. satırları) ve `RegisterCatalogAssembly` (session/replay/catalog
  composition/decorator zinciri — 836-996. satırları) aynı
  `Registration.Core.cs` dosyasında iki ayrı `private static` metot olarak
  durur; ikisi de "çekirdek agent yaşam döngüsü" eksenine ait, ayrı dosyaya
  bölünmeleri gerekmiyordu.
- **Denetimde bulunan `BindCoreFields` çıkarımı.** `faz-denetim` bağımsız
  denetçisi, root `Bind()` metodunun DoD'un "yalnız alt bağlayıcıları
  çağırır" iddiasına rağmen iki alanı (`DefaultTenantId`,
  `MaxParameterValueLength`) kendi gövdesinde bağladığını buldu — orijinal
  2662 satırlık dosyada da aynıydı, taşıma bunu miras almıştı. İki satır
  `BindCoreFields(IConfiguration, TraconOptions)` adıyla
  `Binding.Core.cs`'e çıkarıldı; `Bind()` artık gerçekten yalnız
  yönlendirme yapıyor.
- **Kayıt anlık görüntüsü kapısı (105.3), plandaki "taşıma öncesi üretilir"
  akışının tersine, taşıma TAMAMLANDIKTAN sonra üretildi** — ama eşdeğerliği
  kanıtlamak için `git stash` ile pre-refactor dosyaya geçici olarak
  dönülüp aynı probe orada da çalıştırıldı; iki çıktı `diff` ile
  bayt-bayt karşılaştırıldı (sıfır fark, 160 kayıt). Sonuç aynı: taşıma
  öncesi/sonrası fark yok, yalnız kanıtlama sırası ters çalıştı çünkü
  bölme işlemi zaten atomikti (script tek seferde tüm dosyaları üretti).
- **Denetimde denenip reddedilen bir yaklaşım:** `ServiceRegistrationSnapshotTests`
  başlangıçta her factory kaydını düz "Factory" yerine
  `ImplementationFactory.Method.Name` ile ayırt etmeyi denedi (denetçinin
  2 numaralı 🟡 bulgusuna karşılık). Bu, `BindCoreFields` gibi ilgisiz tek
  bir private metot eklenince derleyicinin tüm sonraki closure'ları
  yeniden numaralandırdığını gösterdi (ölçüldü: `RegisterCoreInfrastructure`
  içindeki ilk factory `b__48_0`'dan `b__49_0`'a kaydı) — ilgisiz her
  değişiklik testi kırardı. Yaklaşım geri alındı; sınırlama testin kendi
  XML dokümanına yazıldı (bkz. Denetim Bulguları #2).

## Bu Fazda Verilen Kararlar

Yok. Plan bunu önceden belirtmişti — mekanik taşıma yeni bir public API,
compatibility contract, güvenlik/kiracı sınırı veya kalıcı veri kararı
üretmedi.

## Denetim Bulguları

Bağımsız denetçi (`faz-denetim`, taze bağlamlı alt agent) çalışma ağacını
`git show 12c867a:...` ile method-method karşılaştırdı (48 orijinal metodun
tamamı — örnekleme değil), build/format/PublicAPI/test kanıtlarını bağımsız
yeniden koştu. 🔴 bulgu **yok**.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Gerçekleşen dosya seti (4 Binding dosyası, `Registration.Core.cs` içinde 2 metot) plan metninden sapıyor | **Düzeltildi** — bkz. "Plandan Sapmalar" |
| 2 | 🟡 | `ServiceRegistrationSnapshotTests.Describe` tüm factory kayıtlarını "Factory" string'ine indirgiyor; aynı `ServiceType+Lifetime` için birden fazla factory (4× `IJobHandler`, 3× `IHostedService`) birbirinden ayırt edilemiyor — bunlar arası bir sıra değişimini test yakalamaz | **Gerekçelendi** — daha zengin bir temsil (`Method.Name`) denendi, derleyicinin closure numaralandırmasının **partial class** genelinde (dosya değil) atandığı ve ilgisiz bir metot eklemenin bile tüm sonraki numaraları kaydırdığı ölçüldü; bu, kapattığı boşluktan daha büyük bir kırılganlık üretti. Sınırlama testin kendi XML dokümanına yazıldı; `IHostedService` sırasının gerçekten önemli olduğu davranış (kapanış sırası) zaten bütünleşik/fonksiyonel test seviyesinde kanıtlanıyor (`.agents/ortak/test-seviyeleri.md`), tek başına bir DI-kayıt unit testine değil. |
| 3 | 🟡 | Root `Bind()` metodu DoD'un "yalnız alt bağlayıcıları çağırır" iddiasına rağmen iki alanı kendi gövdesinde bağlıyordu (orijinal dosyadan miras) | **Düzeltildi** — `BindCoreFields` adıyla `Binding.Core.cs`'e çıkarıldı; `Bind()` artık gerçekten yalnız yönlendirme |

**Temiz çıkan başlıklar:** 3.1 (DoD ihlali — build 0 uyarı, format temiz,
`PublicAPI.*.txt` boş, 1951/1951 yeşil, hepsi bağımsız doğrulandı), 3.2 (test
tiyatrosu yok), 3.4 (yeni kod yolu yok — saf taşıma), 3.5 (imza-gövde kayması
yok — hiçbir metot imzası değişmedi), 3.6 (plan dışı public API yok), 3.7
(İngilizce metin, `TryAdd*` korunmuş, `secret` taraması temiz), 3.8 (tüketici
yüzeyi yok).

Düzeltmeler sonrası dört kapı yeniden koşuldu (`dotnet build`, `dotnet test
Tracon.slnx --no-build -maxcpucount:1`, `dotnet format
--verify-no-changes`) — hepsi yeşil, `git diff --stat -- 'src/*/PublicAPI.*.txt'`
hâlâ boş.

## Gerçek Run Kanıtı

`samples/Tracon.Api` PostgreSQL (`ap-pg` container) ile ayağa kaldırıldı:

```text
GET /tracon/api/meta → 200
  {"version":"0.0.0-preview.0.402", ...,
   "storage":{"persistent":true,"agentDefinitionStore":"SqlAgentDefinitionStore",
   "runStore":"SqlRunStore","sessionStore":"SqlSessionStore","jobStore":"SqlJobStore",
   "jobWorkerEnabled":true}}

POST /tracon/api/agents/support/run (OpenAI, gerçek tool çağrısı) → SSE stream,
  event: done, {"sessionId":null}

GET /tracon/api/runs?limit=1 → [{"id":"01a03b1d-...", "agentName":"support",
  "status":"Completed", "usage":{"inputTokens":283,"outputTokens":16,"totalTokens":299}, ...}]
```

Host kalktı, PostgreSQL tabanlı store'lar (bölünmüş DI kaydından)
çözüldü, gerçek bir `run` tamamlandı ve kalıcı depoya yazıldı. Manuel kabul
case 1 ve 2 (bkz. faz dokümanının "Manuel Kabul Case'leri" tablosu) bununla
ve mevcut `MT-PKG-080`/`MT-PKG-081` ile karşılanmış sayılır; case 3
`TraconOptionsBindingCoverageTests` ile zaten otomatik koşuluyordu (1951
yeşil test setinin içinde).

## Sonraki Faza Devir Notu

- Bu fazın script'i (satır aralıklarını brace-derinliğiyle hesaplayıp
  bitişik dilimleri yeni dosyalara taşıyan Python betiği) Faz 106'nın
  konusu olan `AgentDefinitionCompiler`/derleyici ayrıştırması için de
  yeniden kullanılabilir bir desendir — ama derleyici muhtemelen `Bind*`
  gibi zaten ayrık metotlara sahip değil, tek büyük bir akış (`Compile`)
  olabilir; o zaman "bitişik dilim → adlandırılmış yardımcı metot" deseni
  (bu fazda `AddTracon` gövdesi için kullanıldı) daha çok işe yarar.
- Kayıt anlık görüntüsü deseni (`ServiceRegistrationSnapshotTests`,
  `ServiceType | Lifetime | Implementation` üçlüsü) benzer bir DI kökü
  ayrıştırması gerekiyorsa (`Tracon.AspNetCore`, provider paketlerinin
  `Use*` metotları) doğrudan kopyalanabilir; yalnız factory ayrımını
  `Method.Name`'e genişletmeyi TEKRAR DENEME — bu fazda ölçülüp reddedildi
  (bkz. Denetim Bulguları #2).
- `git stash` ile pre-refactor/post-refactor karşılaştırması (probe konsol
  uygulaması, `ProjectReference` ile ilgili `.csproj`'a bağlanan tek
  dosyalık scratch proje) büyük mekanik refactor'larda "davranış
  değişmedi" iddiasını insan gözünden bağımsız kanıtlamanın ucuz bir
  yoludur; sonraki ayrıştırma fazlarında (106-108) tekrar kullanılabilir.
