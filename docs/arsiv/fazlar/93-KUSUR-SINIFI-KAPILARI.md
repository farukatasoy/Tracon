# Faz 93 — Kusur Sınıfı Kapıları

> **Durum:** ✅ Tamamlandı (2026-08-24)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) kalem **3** (analyzer kuralları — Faz 91'de kapanmayan bölüm). Bu faz bir `F-NN` adayından gelmez.
> **Önkoşul:** [Faz 91](91-GELISTIRME-DONGUSU-KAPILARI.md) — kapı komut yüzeyi (`scripts/kapi.py`) ve `denetim-paketi.py` oradan gelir; bu faz aynı desende iki kapı daha ekler
> **Paketler:** `AgentPrism.Generators`, `AgentPrism.Workflows` (yalnız analyzer referansı), `AgentPrism.Core` (yalnız `buildTransitive` `NoWarn` listesi)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Ölçüldü 2026-08-23: `src/*/PublicAPI.Unshipped.txt` 8.079 satır, `Shipped.txt` dosyaları boş (16 dosya × 1 satır). `AgentPrism.Generators` `AgentPrismPublicApiTrackingEnabled=false` taşır; tanı ve baseline dosyaları public üye değildir
> **Tüketici yüzeyi:** **Var.** Sevk edilen: iki yeni `APG` tanısı (`AgentPrism.Core` nupkg'i, `analyzers/dotnet/cs/`) + `src/AgentPrism.Core/buildTransitive/AgentPrism.Core.targets` `NoWarn` listesi + `docs-site/src/content/docs/troubleshooting.md` (her tanı orada açıklanmalıdır — `DiagnosticIntegrityTests` bunu zorlar). Site: `troubleshooting.md`. `tuketici-dokuman-senkronu` koşar
> **Manuel test alanı:** [`docs/manuel-test/29-AGENT-DESTEGI.md`](../../manuel-test/29-AGENT-DESTEGI.md) (APG tanıları) + [`docs/manuel-test/36-GELISTIRME-KAPILARI.md`](../../manuel-test/36-GELISTIRME-KAPILARI.md) (repo kapıları)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9915df5:docs/arsiv/fazlar/93-KUSUR-SINIFI-KAPILARI.md
> ```
>
> Damıtıldı 2026-08-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz, **üç kez veya daha fazla tekrarlamış** iki kusur sınıfını yazıdan kapıya taşır. Bugün koruma "bir sonraki oturumun doğru hafıza dosyasını okuması" şartına bağlıdır. Bu bir kapı değil, bir umuttur.

## Bitiş Ölçütleri (DoD)

- [x] `APG0501` akışlı bir yineleyicide döngü dışı ambient yazımını `warning` olarak bildirir; yazım döngü içine taşınınca uyarı kaybolur (manuel case 1–2 koşuldu, gerçek tüketici derlemesinde — çıktı `docs/manuel-test/29-AGENT-DESTEGI.md` MT-AGD-021'de)
- [x] `APG0502` `using`'siz `AmbientTenantScope.Begin(...)` çağrısını `warning` olarak bildirir (manuel case 3, MT-AGD-021)
- [x] `AgentPrismUsageDiagnostics=false` iki yeni tanıyı da susturur (manuel case 4, MT-AGD-021) — `AgentPrism.Core.targets` `NoWarn` listesi güncellendi
- [x] `DiagnosticIntegrityTests` yeni iddiayı taşır: her `Usage` tanısı `.targets` `NoWarn` metninde geçer (`Every_usage_diagnostic_is_in_the_NoWarn_switch`)
- [x] `dotnet build src/AgentPrism.Workflows -c Release` sıfır `APG` uyarısı verir; ölçüm 2026-08-24: `0 Warning(s), 0 Error(s)` — bkz. "Plandan Sapmalar" (yanlış pozitif keşfi ve düzeltmesi)
- [x] `AmbientWriteSiteTests` **11** benzersiz `<yol>:<metot>` yerini taban çizgisiyle eşleştirir (14 = ham grep satır sayısı, çoklu-yazım-tek-metot durumları dedup edildi — bkz. "Plandan Sapmalar"); elle eklenen yeni yer testi düşürür (manuel case 6, gerçekten koşuldu)
- [x] `PlaywrightLocatorTests` bugünkü sayıyı (`tests/AgentPrism.Ui.E2ETests/UiTests.cs` → 128) dondurur; elle eklenen riskli locator testi düşürür (manuel case 7, gerçekten koşuldu)
- [x] İki taban çizgisi dosyasının tazeleme ortam değişkeni belgelendi ve **koşularak** doğrulandı (`AGENTPRISM_AMBIENT_WRITE_REFRESH=1`, `AGENTPRISM_PLAYWRIGHT_LOCATOR_REFRESH=1`)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban 7d1f43c`, 2026-08-24, tüm 10 alt komut ✅)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Plandan Sapmalar"
- [x] `secret` taraması boş döndü (`kapi.py tarama` → "Tarama: ✅ temiz")
- [x] Manuel kabul case'leri `docs/manuel-test/29-AGENT-DESTEGI.md` (MT-AGD-021) ve `36-GELISTIRME-KAPILARI.md` (MT-GDK-014–016) içine eklendi; otomatikleştirilebilenlerin tamamı gerçekten koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kapandı (bkz. "Denetim Bulguları")
- [x] `docs-site/` güncellendi (`troubleshooting.md` iki bölüm); `npm run check` (`check:content`+`build`+`check:links`+`check:weight`) temiz
- [x] `kusur-giderme` skill'inin sayaç tablosuna kapı sütunu eklendi — hangi sınıfın hangi kapıyla korunduğu tek yerde okunur

### Doğrulama komutları

```bash
# APG uyarısı Workflows'ta öter mi
dotnet build src/AgentPrism.Workflows/AgentPrism.Workflows.csproj -c Release 2>&1 | grep "warning APG" || echo "temiz"

# Ambient yazım yeri sayısı (taban çizgisiyle eşleşmeli)
grep -rn --include="*.cs" "SetCurrent(\|AmbientTenantScope.Begin(\|AmbientRunAttributionScope.Begin(\|StartActivity(" src/ | grep -v "///" | wc -l

# Riskli locator sayısı
grep -rn --include="*.cs" "GetByText(\|GetByPlaceholder(\|GetByLabel(\|GetByRole(" tests/ | wc -l

# Kapı testleri
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests --filter-class "*AmbientWriteSiteTests*"
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests --filter-class "*PlaywrightLocatorTests*"

# Paket kırılmadı
dotnet pack AgentPrism.slnx -c Release
```

---

## Plandan Sapmalar

1. **`APG0501`'in tetik koşulu plan tasarımından daha dar: "yaprak döngü" + `finally` hariç tutma.**
   Plan (93.1) altı bugünkü yazım yerini ölçmüş ve "hiçbiri kuralın tetiğine
   girmiyor" demişti — bu doğruydu, ama plan `AgentPrism.Workflows`'a analyzer
   referansı eklendiğinde (93.6) ortaya çıkan **yedinci** bir döngüyü
   ölçmemişti: `WorkflowRunner.RunGuardedAsync`'in kendi gövdesinde hem
   ambient yazım (`StartActivity`, `SetCurrent`) hem de ayrı, güvenli bir
   `await foreach (var produced in PumpAsync(...)) { ... }` passthrough
   döngüsü var — `PumpAsync` kendi ambient güvenliğini kendi kendine sağlıyor.
   İlk tasarım (metot gövdesinde herhangi bir await + herhangi bir yazım
   kontrolü) bunu yanlış pozitif olarak işaretledi; ikinci deneme
   (`WorkflowRunner.PumpAsync`'in kendi dış döngüsü, `finally { await
   enumerator.DisposeAsync(); }` bloğu içeren) de aynı şekilde yanlış pozitif
   üretti. Nihai kural iki daraltma taşıyor: (a) bir döngü **başka bir döngü
   içeriyorsa** (konteyner), o döngü hiç değerlendirilmez — yalnız en içteki
   (yaprak) döngüler kontrol edilir, her biri bağımsız; (b) bir döngünün
   `finally` bloğu (temizlik/dispose amaçlı) await taraması dışında tutulur.
   Gerekçe ve üç gerçek üretim vakası (`RunGuardedAsync` satır 524,
   `PumpAsync` satır 659/806) `AgentPrismUsageAnalyzer.cs`'in
   `ImmediateDescendants` ve `VisitAsyncIteratorMethod` üzerindeki `<remarks>`
   bloklarında ve `UsageAnalyzerTests.APG0501_evaluates_a_nested_loop_on_its_own_account`
   testinde belgelendi. Bu **yerel bir implementasyon tercihidir** (K-* açılmadı,
   AGENTS.md'nin "public API/güvenlik/kiracı sınırı/kalıcı veri" ölçütünü
   karşılamıyor); ölçüm gerçek `dotnet build src/AgentPrism.Workflows -c Release`
   ile doğrulandı (0 uyarı).
2. **`APG0502`'nin discard tespiti sözdizimsel değil `IOperation` tabanlı.**
   Plan yalnız "çağrı bir `ExpressionStatement`'tır" diyordu. İlk implementasyon
   (yalnız `ExpressionStatementSyntax` kontrolü) `void Run() => Begin(...);`
   şeklindeki expression-bodied metotları kaçırdı — bunların syntax ebeveyni
   `ArrowExpressionClauseSyntax`'tır, `ExpressionStatementSyntax` değil, ama
   sonuç aynı şekilde atılıyor. `context.SemanticModel.GetOperation(...)?.Parent
   is IExpressionStatementOperation` kontrolüne geçildi; bu hem normal `stmt;`
   hem de void-dönen arrow-body'yi doğru şekilde yakalıyor. Ölçüldü: testler
   önce başarısız oldu (`APG0502_reports_a_discarded_ambient_scope` 0
   diagnostic döndü), düzeltme sonrası geçti.
3. **`AmbientWriteSiteTests`/`PlaywrightLocatorTests`'in kendi kaynak dosyaları
   taramadan hariç tutuldu.** Plan bunu öngörmüyordu. İlk `REFRESH` çalıştırması
   `PlaywrightLocatorTests.cs`'in KENDİ test fixture'larındaki (string literal
   içindeki örnek `GetByText("hello")` çağrıları) kod olarak sayıldığını
   gösterdi — 5 sahte "riskli çağrı" üretti. `SourceLanguageTests`'in
   `SkippedFiles` deseni (K-281) birebir uygulandı.
4. **Taban çizgisi "14 yer" ölçümü ile gerçek dosya içeriği (11 satır) arasındaki
   fark.** Plan'ın 93.1/93.4 bölümlerindeki "14 yer" ölçümü **ham `grep` satır
   sayısıdır** (aynı metotta birden fazla ambient yazım varsa her satır ayrı
   sayılır — örn. `RunGuardedAsync` 2 satır, `PumpAsync` 2 satır,
   `RunCoreStreamingAsync` 2 satır). Taban çizgisi dosyası plan'ın kendi örnek
   satır formatına (`<yol>:<metot> | <gerekçe>`) sadık kalarak **benzersiz
   `<yol>:<metot>` çiftini** bir SET olarak tutuyor — metot adı "kusurun
   doğduğu birim" olduğu için (plan satır 207-208), aynı metotta ikinci bir
   yazım YENİ bir "yer" sayılmıyor. Sonuç: 14 ham satır → 11 benzersiz taban
   çizgisi satırı. Mekanizma doğru çalışıyor (manuel case 6 ile kanıtlandı);
   yalnızca ölçüm terminolojisi netleştirildi.
5. **`APG0501`'in "await foreach de sayılsın mı" açık sorusu (1) plandaki gibi
   basit bir "otomatik true" ile değil, "gövdede ayrı bir await var mı"
   kontrolüyle çözüldü.** `await foreach`'in kendi örtük `MoveNextAsync()`'i
   ayrıca bir "sürücü tetiği" sayılmadı (sapma #1'in gerekçesiyle aynı kök
   neden) — yalnızca döngü GÖVDESİNDE (nested loop hariç) ayrı bir `await`
   varsa tetikleniyor. Açık sorunun "A: ikisi de sayılsın" önerisi ruhen
   korundu (`await foreach` DA tetiklenebilir,
   `APG0501_reports_an_await_foreach_with_a_further_unguarded_await` testi
   kanıtlıyor) ama tetik koşulu `while`/`for` ile TUTARLI tek bir kurala
   indirgendi.

### `dokuman-bakim.py --site-denetle` gerekçeleri (`--site-gerekce-yazildi`)

Dört kural tetiklendi, biri (`buildtransitive` → `capabilities.md`) gerçek bir
güncelleme gerektirdi ve yapıldı (Usage diagnostics satırına "ambient write"/
"discarded ambient scope" eklendi). Kalan üçü **yanlış tetikleme** — heuristic
dosya-yolu bazlı, davranış bazlı değil:

- **`cekirdek-kavram` → `concepts/`**: `AgentPrism.Core.targets`'taki tek
  değişiklik `NoWarn` listesine iki tanı ID'si eklenmesi. Bu bir MSBuild
  yapılandırma detayıdır, `concepts/` altındaki hiçbir temel kavramı (agent
  catalog, run recording, tool registry) etkilemiyor.
- **`workflow` → `concepts/workflows.md`**: `AgentPrism.Workflows.csproj`'a
  eklenen tek şey `OutputItemType=Analyzer` `ProjectReference`'ıdır —
  derleme-zamanı bir analyzer bağlantısı. Workflow **yürütme** davranışı
  (execution engine, checkpoint, human input) hiç değişmedi;
  `concepts/workflows.md` bugün `Generators`/`analyzer` kelimelerini hiç
  içermiyor ve içermemeli.
- **`paket-tanimi` → `packages.md`**: Aynı csproj değişikliği. Paketin
  tüketiciye görünen tanımı (`packages.md:26` — "Multi-agent workflows,
  checkpoints, human input") değişmedi; yeni referans `PrivateAssets="all"`
  + `ReferenceOutputAssembly="false"` taşır, yani tüketicinin bağımlılık
  grafiğine **hiç yansımaz** (Core'un 52.4'ten beri kullandığı aynı desen).

## Bu Fazda Verilen Kararlar

Yeni `K-NNN` kaydı yok. Bu fazda alınan tüm kararlar (yukarıdaki "Plandan
Sapmalar") **yerel implementasyon tercihleridir** — AGENTS.md'nin karar
defteri eşiğini (public API/compatibility contract, güvenlik/kiracı sınırı,
kalıcı veri/migration, geri dönüşü pahalı sistem kararı) karşılamıyor. Kod
yorumlarında (`AgentPrismUsageAnalyzer.cs` `<remarks>` blokları) ve bu
dokümanda kalıcı olarak belgelendi.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir alt-agent ile çalıştırıldı (2026-08-24).
Denetçi bağımsız olarak `dotnet build`/`dotnet test`/`kapi.py kapanis`'i kendi
başına tekrar koşturdu ve manuel case 15-16'yı kendisi de tekrarladı.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `kusur-giderme` SKILL.md'nin sayaç tablosuna kapı sütunu eklenmemişti (DoD maddesiyle çelişki) | 🔴 | **Düzeltildi** — tabloya `Kapı` sütunu eklendi (`AsyncLocal`→`APG0501`+`AmbientWriteSiteTests`, sync kopyası→`kapi.py tarama`, Playwright→`PlaywrightLocatorTests`) |
| 2 | DoD'daki "14 yer" ifadesi taban çizgisindeki gerçek 11 satırla uyuşmuyordu | 🟡 | **Düzeltildi** — DoD satırı 11'e düzeltildi, "Plandan Sapmalar #4"e ölçüm terminolojisi (ham grep vs. benzersiz taban çizgisi) yazıldı |
| 3 | Hata Modları tablosundaki iki satır (Windows yol ayırıcısı, `RepositoryRoot` bulunamama) ayrı bir birim testi vaat ediyordu, kodda yok | 🟡 | **Gerekçelendi** — tablo satırları "davranış kodda var, ayrı test yok, `SourceLanguageTests` ile aynı desen" olarak düzeltildi; yeni test **eklenmedi** çünkü referans aldığı desenin kendisi de aynı boşluğu taşıyor |
| 4 | `samples/AgentPrism.Api` ile gerçek `run` kanıtı faz dokümanında yoktu | 🟡 | **Düzeltildi** — koşum bu bölümün altına, gerçek çıktıyla yazıldı |

**Temiz çıkan başlıklar** (denetçi raporu): 3.2 (test tiyatrosu), 3.3 (test
seviyesi), 3.5 (imza-gövde kayması), 3.6 (plan dışı public API), 3.7 (repo
kuralları — secret/İngilizce/XML doküman/`ConfigureAwait`), 3.8 (ürün yüzeyi).

Denetim sonrası dört doğrulama kapısı **yeniden koşuldu**:
`python3 scripts/kapi.py kapanis --taban 7d1f43c` → tüm 10 alt komut ✅
(2026-08-24, dördüncü koşum — üçüncü koşum iki kararsız testle kırmızı oldu,
bkz. "Kararsız test keşfi" altında).

### Kararsız test keşfi (Faz 93'ün kapsamı dışında)

Üçüncü kapı koşumunda iki test düştü:
`AspNetCore.FunctionalTests.ToolGovernanceEndpointTests.Timed_out_call_...`
ve `Ui.E2ETests.UiTests.Shell_opens_and_asks_for_token_when_required`. İkisi
de Faz 93'ün dokunduğu koddan tamamen bağımsız. Doğrulandı:
- `ToolGovernanceEndpointTests` testi **baz commit'te (`7d1f43c`, Faz 93
  öncesi) izole `git worktree`'de** de aynı `ObjectDisposedException`'ı verdi
  — `JobWorkerBackgroundService.RunJobAsync:148`'de host teardown'ı ile
  yarışan **önceden var olan** bir kusur, Faz 93 sebep olmadı.
- `Shell_opens_and_asks_for_token_when_required` testi **izole** koşulduğunda
  geçti — paralel test yüküyle kaynak çekişmesi yaşayan kararsız bir E2E testi.

Dördüncü koşumda tüm 20 test projesi (2647+ test) yeşildi. `ToolGovernanceEndpointTests`
kusuru bu fazın kapsamı dışında bırakıldı ve `docs/hafiza/cekirdek-calistirma.md`'ye
tuzak olarak kaydedildi (bu bir yetenek adayı değil, `ADAYLAR.md`'nin
formatına girmiyor).

### `samples/AgentPrism.Api` gerçek koşum (2026-08-24)

`AgentPrism__PostgreSql__ConnectionString=""` ile (yerel `user-secrets`'taki
eski bir migration checksum çakışmasını atlayıp in-memory store'a zorlamak
için — Faz 93'ün konusuyla ilgisiz, ortam-özel bir durum) uygulama ayağa
kalktı:

```bash
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' \
  -d '{"message":"where is my order"}'
# SSE akışı: event: update ... event: done, data: {"sessionId":null}

curl -s http://localhost:5081/agentprism/api/runs -H 'Authorization: Bearer <token>'
# [{"agentName":"support","status":"Completed","modelId":"gpt-5.4-mini",
#   "usage":{"inputTokens":232,"outputTokens":18,"totalTokens":250},"eventCount":17,...}]
```

Run **Completed** durumunda kaydedildi, kullanım (usage) doğru. Faz 93'ün
analyzer değişiklikleri (derleme-zamanı, çalışma-zamanı davranışını
etkilemiyor) örnek uygulamayı bozmadı.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `AgentPrism.Workflows` artık `AgentPrism.Generators`'a `OutputItemType=Analyzer`
  `ProjectReference` taşıyor (`Core`'un deseninin aynısı). Yeni bir `APG` tanısı
  eklenirse **hem** `AgentPrism.Core` **hem** `AgentPrism.Workflows` üzerinde
  `dotnet build -c Release` ile sıfır-yanlış-pozitif ölçülmeli — Faz 93 bunu
  ihmal ettiğinde iki ayrı yanlış pozitif üretti (bkz. "Plandan Sapmalar #1").
- `AmbientWriteSiteTests`/`PlaywrightLocatorTests` yalnız küçülen taban
  çizgisi kapılarıdır (`SourceLanguageTests` deseni). Yeni bir ambient yazım
  yeri veya riskli locator eklerken taban çizgisi tazelenmezse kapı düşer;
  bu **beklenen** davranıştır, kapıyı susturma.

**Bilinen tuzaklar (🚨):**
- 🚨 **Bir async iterator kuralı yazarken "metot gövdesinde herhangi bir yer"
  varsayımı yanlış pozitif üretir.** Konteyner döngüler (başka bir döngü
  içeren) ile `finally`/cleanup bloklarındaki await'ler ayrı ele alınmalı —
  detay `AgentPrismUsageAnalyzer.cs`'in `ImmediateDescendants` yorumunda.
- 🚨 **APG0502 tipi "sonuç discard edildi mi" analizleri sözdizimsel
  (`ExpressionStatementSyntax`) DEĞİL, `IOperation` (`IExpressionStatementOperation`)
  tabanlı yazılmalı** — sözdizimsel kontrol expression-bodied üyeleri
  (`void X() => Y();`) kaçırır.
- 🚨 Taban çizgisi dosyalarının "kaç yer" ölçümünü faz dokümanına yazarken
  **ham grep satır sayısı ile taban çizgisinin kendi granülerliği (metot
  bazında dedup) arasındaki farkı açıkça belirt** — aksi hâlde denetim bunu
  bulgu olarak işaretler (bu fazda oldu).

**Yarım kalan iş:** Yok. DoD'un tüm satırları ✅.

**Sıradaki faz:** `docs/94-SQL-TEK-KAYNAK.md` (SQL Tek Kaynak) — yol
haritasında zaten `📋 Planlandı` durumunda, bu fazdan bağımsız bir konu
(elle tekrarlanan toplama ifadesi kusur sınıfının SQL tarafı, K-483'ün
devamı). Faz 93'ün YAPTIĞI değişikliklerden Faz 94'ü etkileyecek hiçbir
sözleşme yok.
