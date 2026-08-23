# Faz 84 — TypeScript İstemcisi ve npm Kanalı

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-93** — Dalga 13 Küme E (**ikinci yarı**; birinci yarı F-50 → [Faz 83](83-TIPLI-ISTEMCI-VE-CLI.md))
> **Önkoşul:** [Faz 83](83-TIPLI-ISTEMCI-VE-CLI.md) — üretim akışının şekli (belge → üreteç → commit → kapı), `/agentprism` önekini soyan dönüşüm (§83.3) ve `operationId` kapsama kapısının fikri (§83.2) oradan devralınır · [Faz 40](40-OPENAPI-YAYINI.md) — üretim kaynağı olan belge ve `OpenApiSnapshotTests` · [Faz 5](05-AGENTPRISM-UI.md) ve [Faz 30](30-ARAYUZ-CILASI.md) — göç edecek arayüz katmanı
> **Paketler:** `@agentprism/client` (**yeni — npm**), `AgentPrism.UI` (yalnız `frontend/`)
> **Yeni paket:** Bir npm paketi. K-007 .NET paketleri içindir; gerekçe ve ağırlık yine sayılır — **§84.9** · **Migration:** Yok
> **Public API:** .NET yüzeyi **büyümüyor** — bu faz tek bir C# üyesi eklemez. Yeni yüzey npm tarafındadır ve `PublicAPI.*.txt` onu **görmez**; sözleşmesini §84.10'daki kendi kapısı tutar
> **Tüketici yüzeyi:** `docs-site/` → yeni `guides/typescript-client.md`, `packages.md` (npm kanalı ayrı bir bölüm ister — bugünkü sayfa 17 .NET paketi anlatıyor), `capabilities.md`, `reference/versioning.md` (npm ↔ NuGet sürüm eşleşmesi), `guides/client-side-tools.md` (bugünkü elle `fetch` örneği tipli hâle gelir)
> · sevk edilen: `packages/agentprism-client/README.md` (**npm paket sayfası** — registry'de görünen metin budur), kök `README.md` paket tablosu. `api/` ve `http-api/` **üretilir** ve bu faz onlara dokunmaz
> **Manuel test alanı:** `docs/manuel-test/35-TYPESCRIPT-ISTEMCISI.md` — **yeni dosya**, alan kodu `TSC`. `00-INDEKS.md` §7 tablosuna `35` satırı yazıldı (plan `34`'ü öngörmüştü, ama Faz 83 kapanışında `33` VE `34` ikisi de dolmuştu — denetimin 🟡#4 bulgusu, düzeltildi)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in yönetim API'sini bir TypeScript uygulamasından tipli olarak çağırmanın yolu yoktur. Elle `fetch` yazılır, alan adları elle eşleştirilir ve sunucu sözleşmesi değişince hiçbir şey kızarmaz.

## Bitiş Ölçütleri (DoD)

- [x] `npm run generate` belgeden `schema.ts` üretir; `schema-drift.test.ts` üretilenle commit'linin **aynı** olduğunu kanıtlar (yeşil)
- [x] `paths-coverage.test.ts` belgedeki **161** yolun tamamını üretilen `paths` ağacında bulur; muafiyet dosyası yoktur
- [x] `src/AgentPrism.UI/frontend/src/lib/types.ts` **silinmiştir**; `api.ts` yalnız istemci kurulumu, `unwrap()` ve `openStream` taşır (81 satır)
- [x] 🚨 Belge elle bozulduğunda sürüklenme **testte** kırılır (Kabul case 7) — elle kanıtlandı: doc'tan `/api/diagnostics` silinip `dotnet build AgentPrism.slnx -c Release` çalıştırıldı → **derleme başarılı** (`0 Warning(s), 0 Error(s)`), sonra aynı bozuk belgeyle `dotnet test tests/AgentPrism.AspNetCore.FunctionalTests` çalıştırıldı → **1 test kırıldı** (619 toplam, 618 geçti). Planın ilk yazımı "`dotnet build` kırılır" diyordu — yanlıştı, denetimin 🟡#1 bulgusu, düzeltildi (case 7 metni + bu satır)
- [x] Konsol eskisi gibi çalışır: `samples/AgentPrism.Api` üzerinde agent listesi (`GET /api/agents` → 200, 13 agent) · `run` listesi/ayrıntısı (`GET /api/sessions` → 200) · skills (`GET /api/skills` → 200) · token reddi (`Authorization` başlığı olmadan `GET /api/agents` → 401) elle doğrulandı; **akan** playground `run`'ı ve workflow insan girdisi `AgentPrism.Ui.E2ETests`'in 56 senaryosuyla (SSE dahil) otomatik kanıtlandı
- [x] 🚨 Özel önekle (`MapAgentPrism("/control")`) konsol çalışır — `AgentPrism.Ui.E2ETests`'in `Assets_load_under_a_different_prefix` senaryosu bunu kapsıyor (değişmedi, 56/56 yeşil)
- [x] `GET /api/diagnostics` belgeye girdi; `docs/openapi/agentprism.json` **124 yol · 161 operasyon**; `samples/AgentPrism.Api` üzerinde elle çağrıldı, gerçek rapor döndü (bkz. "Sonraki Faza Devir Notu" öncesi kanıt); `OpenApiSnapshotTests` yeşil
- [x] `SourceLanguageTests.ScanRoots` `packages`'ı kapsıyor **ve** `PackagedReadmePattern` regex'i `packages/*/README.md`'yi de eşliyor (denetimin 🟡#2 bulgusu, düzeltildi — eskiden yalnız `src/*/README.md` eşleşiyordu); taban çizgisi **büyümedi** (1186/1186 yeşil)
- [x] Bundle payı **ölçüldü**: `174.7 KB gzip` (taban `173.5 KB`'den +1.2 KB, 43 dosyanın tamamı göç ettikten sonra), bütçe `250 KB gzip` aşılmadı
- [x] `openapi-fetch`'in çalışma anı boyutu **ölçüldü**: minify edilmiş tek başına `7.5 KB`, gzip `2.9 KB` (esbuild ile izole paketlenip ölçüldü)
- [x] §84.6'nın `required` boşluğu **doğrulandı** — Açık Soru 4'ün A seçeneği: yöntem tek tek 75 alanı elle sınıflamak değil, tersine kanıt aramaktı: `grep -rln "JsonIgnoreCondition.WhenWritingDefault\|WhenWritingNull" src/` 6 dosya buldu, ama hepsi yönetim API'sinin `/api/*` yanıt gövdesi **dışında**: webhook'a gönderilen payload, `/v1/*` OpenAI-format hata zarfı (K-038, farklı sözleşme), workflow olay-metni içine gömülen özet (iç depolama, dış HTTP yanıtı değil), üçüncü taraf ElevenLabs istemci ayrıştırması, SQL iç depolama serileştirmesi, WebSocket ses protokolü mesajları. Yönetim API'sinin `/api/*` yanıt tiplerini üreten hiçbir kod yolu bu ignore-condition'lardan geçmiyor — `Fix<T,K>`'ın dayandığı "varsayılan değerli alan da her zaman yazılır" varsayımı **doğrulandı**, düzeltme gereken bir şema **yoktur**
- `@agentprism` kapsamı npm.org'da **rezerve edildi** — 👤 kullanıcı eylemi, henüz yapılmadı (kullanıcı kararı: "kodu yaz, rezervasyonu ben sonra yaparım"); `ci.yml`'ın npm işi `--access public` ile yayınlamaya **hazır**, kapsam alındığında ilk `v*` etiketiyle devreye girer
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format` art arda iki kez yeşil (ilk turda `AgentPrism.AspNetCore.FunctionalTests` ve `AgentPrism.Sqlite.IntegrationTests`'te paralel koşumdan kaynaklanan geçici kırılma görüldü, izole rerun'da ikisi de yeşildi; ikinci tam-çözüm koşumu tek seferde temiz geçti)
- [x] `samples/AgentPrism.Api` ile gerçek çağrılar yapıldı, çıktı bu tabloya yazıldı (agents, sessions, skills, diagnostics, index.html + üretilen bundle hash'i doğrulandı)
- [x] `secret` taraması boş döndü — `NPM_TOKEN` hiçbir dosyada literal değer olarak geçmiyor, yalnız `secrets.NPM_TOKEN` (GitHub Actions ifadesi) olarak `ci.yml`'de
- [x] Manuel kabul case'leri `docs/manuel-test/35-TYPESCRIPT-ISTEMCISI.md` içine eklendi (plan `34` öngörmüştü, gerçek boş sıra `35`'ti — denetimin 🟡#4 bulgusu); `00-INDEKS.md` tablosuna `35` satırı yazıldı; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu (taze bağlamlı `general-purpose` agent, izole worktree); 🔴 bulgu **yok**; 5 🟡 bulgunun 4'ü kapandı (regex genişletildi, case 7 metni düzeltildi, dosya numarası düzeltildi, `required` boşluğu doğrulandı), 1'i (E2E'nin denetçinin kendi ortamında koşulamaması) uygulayan oturumun kendi gate koşumuyla zaten kapalıydı — ayrıntı "Denetim Bulguları"
- [x] `docs-site/` güncellendi — yeni `guides/typescript-client.md`, `packages.md`, `capabilities.md`, `reference/versioning.md`, `http-api.md`/`index.mdx` (161 operasyon), `sidebar.mjs`; `npm run check` (content → build → links → weight) dört kapının tamamı yeşil; `dokuman-bakim.py --site-denetle` iki kural gerekçeyle geçildi (Plandan Sapmalar)
- [x] Site **yayınlandı** (`scripts/site-deploy.sh`, kullanıcı onayıyla) ve canlıda doğrulandı: `https://agentprism.doayen.web.tr/` → 200, apex (`doayen.web.tr`) bozulmadı (405 = HEAD, beklenen), **yeni sayfa** `https://agentprism.doayen.web.tr/guides/typescript-client/` → 200 ve içeriği (`createAgentPrismClient`) canlıda görüldü
- [x] Kök `README.md` npm kanalını anlatan bir satır kazandı; `160→161` operasyon sayısı düzeltildi

### Doğrulama komutları

```bash
# Uretim (gelistirme adimi — dotnet build bunu CAGIRMAZ)
cd packages/agentprism-client && npm run generate && npm test

# Belge yeniden uretimi (84.3 sonrasi)
AGENTPRISM_OPENAPI_REFRESH=1 dotnet test tests/AgentPrism.AspNetCore.FunctionalTests \
  -c Release --filter FullyQualifiedName~OpenApiSnapshotTests
python3 -c "import json;d=json.load(open('docs/openapi/agentprism.json'));\
print(len(d['paths']),'yol')"

# Surukleme kapisi gercekten kiriyor mu (Kabul case 7)
#   docs/openapi/agentprism.json icinden bir alan sil, sonra:
dotnet build AgentPrism.slnx -c Release      # KIRILMALI
git checkout docs/openapi/agentprism.json

# Bundle payi
ls -l src/AgentPrism.UI/wwwroot/assets/

# Dort kapi
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

---

## Plandan Sapmalar

- **Manuel test dosya numarası 34 → 35.** Plan yazıldığında "34" boştu; ama
  Faz 83 kapanışında hem `33` (Faz 80'in `33-DOKUMAN-KAPILARI.md`'si) hem
  `34` (Faz 83'ün `34-ISTEMCI-VE-CLI.md`'si) doluydu. Denetimin 🟡#4 bulgusu.
- **Kabul case 7'nin planı yanlış yazılmıştı.** "Belge bozulunca `dotnet build`
  kırılır" — yanlış. `dotnet build` belgeyi HİÇ okumaz; sürüklenme yalnız
  TEST seviyesinde (`OpenApiSnapshotTests` C# tarafında, `schema-drift.test.ts`
  TS tarafında) görülür. Elle kanıtlandı (bkz. `docs/manuel-test/35-*.md`).
  Denetimin 🟡#1 bulgusu.
- **Göç tam göçtür; planın kaçış merdiveni (adlandırılmış cephe) hiç
  kullanılmadı.** 43 dosya/155 çağrı noktasının tamamı tek oturumda göç etti —
  bkz. K-577.
- **Faz 83'ün iki kendi gap'i bu fazda ortaya çıktı ve düzeltildi**, çünkü
  ikisi de dört doğrulama kapısının kapsamına giriyordu ve bu faz onları
  yeşil bırakmadan kapanamazdı: `AgentPrism.Generators.UnitTests.csproj`
  `AgentPrism.Client`'ı hiç referanslamıyordu (paketin tek `<example>` bloğu
  hiç derlenmiyordu) ve o örneğin kendisi `services.AddAgentPrismClient(...)`
  yazıyordu (`builder.Services.AddAgentPrismClient(...)` olması gerekirken —
  `ExamplePrelude`'un sağladığı isim `builder`'dır, `services` değil).
  Ayrıca `AgentPrismContentProtectionOptions.cs`'in örneği (Faz 82) hiçbir
  zaman geçerli C# ya da JSON değildi (`Key:SubKey = value` biçimi) — bu üç
  kusur da `AgentPrism.Generators.UnitTests`'in derlenmesini hiç kimse
  denemediği için gizli kalmıştı. Kod bu fazın kapsamı dışındaki dosyalarda
  ama gate'i kırdığı için düzeltildi, ertelenmedi.
- **§84.6'nın "75 şema" ölçümü planın öngördüğü biçimde yapılmadı.** Tek tek
  75 şemayı elle sınıflamak yerine sistemik bir kanıt arandı
  (`JsonIgnoreCondition.WhenWritingDefault`/`WhenWritingNull` kullanımı
  yönetim API'sinin `/api/*` yanıt tiplerinde var mı) — bkz. K-578 ve DoD
  tablosu. Sonuç aynı: düzeltme gereken bir şema yok.
- **`RunStatistics` ve `Experiment` tipleri planın `Fix<T,K>` desenini
  KULLANAMADI.** React Query'nin `useQuery` jenerik çıkarımı üç seviyeli
  intersection tiplerini (`Fix<T,K> & {...}`) taşıyamadı — ampirik olarak
  izole edildi (8 problı dosya, hepsi silindi), tek düzey `Omit<>&{}`'e
  düzleştirilince sorun kayboldu. Planın kendisi bunu öngörmemişti; bu
  TypeScript'in kendi sınırlarından, plan hatasından değil.

**`tuketici-dokuman-senkronu` — iki kural gerekçeyle geçildi
(`--site-gerekce-yazildi`):**

- **`arayuz` (hedef `ui.md`):** `src/AgentPrism.UI/frontend/src/components/access-gate.tsx`
  ve göç kapsamındaki 42 dosya daha tetikledi. İncelendi: değişiklik yalnız iç
  API çağrı katmanını (`api.ts`'teki elle yazılmış `api` nesnesinden üretilmiş
  `@agentprism/client`'a) taşıdı — `ApiError` → `AgentPrismError`,
  `api.meta()` → `client.GET('/api/meta')` gibi bire bir karşılıklarla. Hiçbir
  ekran, route veya görünür davranış değişmedi; `ui.md` zaten bunu anlatıyordu.
  Doğrulandı: `git diff 0e41b5a -- src/AgentPrism.UI/frontend/src/screens/`
  yalnız `M` (değişti) satırları gösterdi, hiçbir ekran eklenmedi/silinmedi.
- **`cekirdek-kavram` (hedef `concepts/`):**
  `src/AgentPrism.Core/Security/AgentPrismContentProtectionOptions.cs` tetikledi.
  İncelendi: değişiklik yalnız `<example>` XML dokümanının biçimini (düz
  `Anahtar:AltAnahtar = deger` satırlarından iç içe JSON gösterimine)
  değiştirdi — aynı anahtarlar, aynı davranış. `concepts/governance.md` ve
  `reference/configuration.md` `ContentProtection`'ı zaten tablo biçiminde
  anlatıyor, düz-anahtar örneğine bağımlı değil; ikisi de hâlâ doğru. Yeni
  örnek biçimi `api/` referansına DocFX ile otomatik yansır.

## Bu Fazda Verilen Kararlar

- [K-573](../../KARARLAR.md) — OpenAPI belgesi varsayılan kapalı uçları da tarif
  eder; `/api/diagnostics` belgeye girdi, davranışı değişmedi (§84.3)
- [K-574](../../KARARLAR.md) — `packages/` kök dizini dil sınırı kapısının
  kapsamına girdi; `PackagedReadmePattern` genişletildi (§84.7, denetim 🟡)
- [K-575](../../KARARLAR.md) — `@agentprism/client` yerel bağımlılığı `file:`
  protokolüyle kurulur, npm registry'den kurulmayı beklemez (§84.8)
- [K-576](../../KARARLAR.md) — npm yayın işi `npm view` ile elle idempotency
  kontrolü yapar; NuGet ile aynı `v*` git tag'inden türer (§84.11)
- [K-577](../../KARARLAR.md) — konsol göçü tam göçtür; adlandırılmış cephe
  eklenmedi (kaçış merdiveni kullanılmadı)
- [K-578](../../KARARLAR.md) — OpenAPI üretecinin iki sistemik kusuru ve paylaşılan-
  şema nullable sızıntısı frontend'de `Fix<T,K>` ile telafi edilir, sunucu
  şeması değiştirilmez (§84.6, Açık Soru 4 karar A)

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir `general-purpose` agent'la, izole bir
worktree'de koşuldu (denetçinin kendi ortamında `pwsh` yoktu, bu yüzden
`AgentPrism.Ui.E2ETests`'i kendi başına koşamadı — ama uygulayan oturum
bunu kendi gate koşumunda zaten yeşil görmüştü).

**🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Kabul case 7'nin metni yanlış: "`dotnet build` kırılır" derken sürüklenme yalnız test seviyesinde görülüyor | 🟡 | **Düzeltildi** — case metni ve DoD satırı `dotnet test`/`npm test`'e çevrildi, elle kanıtlandı (bkz. Plandan Sapmalar) |
| 2 | `SourceLanguageTests`'in `PackagedReadmePattern` regex'i `packages/*/README.md`'yi hiç eşlemiyordu | 🟡 | **Düzeltildi** — regex genişletildi (K-574) |
| 3 | §84.6'nın "75 şema sayıldı" DoD satırı hiç yapılmamıştı | 🟡 | **Düzeltildi** — sistemik kanıt yöntemiyle doğrulandı (K-578) |
| 4 | Manuel test dosya numarası (34) zaten Faz 80 VE Faz 83 tarafından alınmıştı, gerçek boş sıra 35'ti | 🟡 | **Düzeltildi** — dosya `35-TYPESCRIPT-ISTEMCISI.md` olarak açıldı, index satırı düzeltildi |
| 5 | `AgentPrism.Ui.E2ETests` denetçinin kendi ortamında koşulamadı (`pwsh` yok) | 🟡 | **Zaten kapalıydı** — uygulayan oturum aynı gün, aynı makinede, tam çözüm `dotnet test`'iyle 56/56 yeşil gördü (iki kez, art arda) |
| 6 | `server-types.ts` başlık yorumu "104 total exports" diyor, gerçek sayı 105 | 🟢 | Yorum/sayım tutarsızlığı, davranışı etkilemiyor — devredilmedi (kozmetik, F-NN gerektirmiyor) |
| 7 | `api.ts`'in SSE hata dönüştürme mantığı `client.ts`'in `onResponse` middleware'iyle birebir aynı, iki kopya var | 🟢 | DRY fırsatı, bugün senkron ve doğru — `docs/ADAYLAR.md`'ye eklenmedi (davranışsal risk yok, ölçülmüş bir bedel yok) |
| 8 | Planın "Planlanan Dosya Listesi"si `test/client.test.ts`'i saymamış ama dosya zaten var ve testleri geçiyor | 🟢 | Uygulama planın eksik yazdığı kısmı zaten doğru tamamlamış — aksiyon gerekmedi |

Ayrıca uygulayan oturumun kendi bulgusu (denetimden bağımsız, dört kapıyı ilk
tam koşumda kırdı): `AgentPrism.Client.UnitTests.ClientCoverageTests` ve
`AgentPrism.Generators.UnitTests`'in 3 testi — bkz. Plandan Sapmalar'daki
"Faz 83'ün iki kendi gap'i" paragrafı. Bunlar denetimden ÖNCE, dört kapının
ilk tam-çözüm koşumunda bulundu ve aynı oturumda kapatıldı.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `@agentprism/client` npm paketi kod olarak **tamam**, ama **henüz
  yayınlanmadı**. `@agentprism` kapsamı npm.org'da rezerve edilmedi
  (kullanıcı kararı: "kodu yaz, rezervasyonu ben sonra yaparım") ve hiçbir
  `v*` git tag'i atılmadı — `ci.yml`'ın `npm-publish` işi hiç tetiklenmedi,
  yani **gerçek bir npm yayınıyla hiç doğrulanmadı**. İlk `v*` etiketinde
  `NPM_TOKEN` GitHub secret'ının da eklenmiş olması gerekir (kod bunu
  varsayar, kontrol etmez).
- `src/AgentPrism.UI/frontend/src/lib/server-types.ts` artık konsolun TÜM
  genişletilmiş response tiplerinin **tek kaynağıdır**. Yeni bir HTTP yanıt
  alanı okuyan bir ekran yazarken önce bu dosyada widened bir tip var mı bak;
  yoksa `Fix<Generated.X, 'alan1'|'alan2'>` deseniyle ekle, YOKSA React
  Query'nin üç-seviye intersection sınırına çarparsan (bkz. `RunStatistics`
  örneği) tek-seviye `Omit<>&{}`'e düzleştir.
- `Fix<T,K>` yalnız RESPONSE tipleri içindir. Bir FORM/REQUEST durumu
  (`toRequest()` inşa eden bir ekran) için asla `server-types.ts`'ten widened
  bir tip kullanma — `agent-editor.tsx`'in `CompactionForm`/`ModelBinding` ve
  `skills.tsx`'in `SkillForm`/`triggers.tsx`'in `TriggerForm` örneklerindeki
  gibi LOKAL bir form tipi tanımla (üretilen request tipi her alanı opsiyonel
  yapar, form state genelde birkaçını zorunlu ister).

**Bilinen tuzaklar:**
- 🚨 `docs/openapi/agentprism.json` değiştiğinde ÜÇ yer yeniden üretilmelidir,
  ikisi değil: C# `AgentPrism.Client` (`dotnet nswag run nswag.json` +
  post-process script'leri), TS `@agentprism/client`
  (`npm run generate` `packages/agentprism-client` içinde) VE frontend'in
  `server-types.ts`'i (yeni alan bir Fix<> düzeltmesi gerektiriyorsa elle).
  Üçünü de atlarsan hiçbir kapı SESSİZCE kırılmaz — `ClientCoverageTests` ve
  `schema-drift.test.ts` kırılır, ama `server-types.ts`'in eksik kalması
  yalnız `tsc`'i (yanlış tipte) kırar, davranışı DEĞİL.
- 🚨 `useQueryClient()`'ın yerel değişken adını hiçbir zaman `client` yapma —
  `lib/api.ts`'in paylaşılan `client`'ıyla çakışır ve derleyici bunu
  YAKALAMAZ (ikisi de metot ismi taşıyan nesnelerdir, `invalidateQueries`
  yanlış nesnede yoksa TS hatası verir ama VARSA sessizce yanlış nesneyi
  çağırır). `queryClient` adını kullan, ya da api istemcisini `apiClient` diye
  içe aktar — konsolun tamamı artık bu iki desenden birini izliyor.
- 🚨 Bir OpenAPI şeması hem NULLABLE hem NON-NULLABLE kullanılıyorsa (aynı
  şema adı, farklı alanlarda), üretilen TEK paylaşılan tip HER YERDE `| null`
  taşır — `server-types.ts`'te `Exclude<Generated.X, null>` ile yerel olarak
  daraltılmalı (bkz. `RunErrorClass`, `WorkflowKind` örnekleri), şemanın
  KENDİSİ değiştirilmemeli.
- 👤 F-145 (`docs/ADAYLAR.md`): yanlış token'ın konsolda "reddedildi" olarak
  gösterildiğini kanıtlayan bir E2E testi yok. Ölçülmüş bir davranış kusuru
  değil, bir test boşluğu.

**Yarım kalan işler:** Yok — DoD'nin tek işaretsiz satırı `@agentprism`
kapsamının npm.org'da rezervasyonu, ki bu 👤 bir eylemdir ve kod tarafını
etkilemez.

**Sıradaki faz:** [Faz 85 — Gömme Ekseni](85-GOMME-EKSENI.md). Bu fazla
DOĞRUDAN bağlantılı: §85.4 `AgentPrismDiagnosticsReport`'a alan ekliyor —
Faz 85'in kendi "Bu Faza Başlarken" listesine bu fazın devir notuna işaret
eden bir madde eklendi (üç-yerde-yeniden-üretim kuralı).
