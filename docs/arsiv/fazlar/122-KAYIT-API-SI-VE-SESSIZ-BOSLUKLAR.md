# Faz 122 — Kayıt API'si ve Sessiz Boşluklar

> **Durum:** ✅ Tamamlandı (2026-08-28)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) §13 — **kulvar 2** (BL-008 · BL-019 · BL-034 · BL-050 · 🟢 BL-016) + **kulvar 6** (BL-018 · BL-033 · BL-039 · BL-051)
> **Önkoşul:** [Faz 121](121-SEAM-SOZLESME-DOKUMANI.md) — seam sözleşme standardını ve metin kapısı desenini kurdu; bu faz aynı kapıyı bir boyut daha ile genişletir
> **Paketler:** `Tracon.Core` (birincil), `Tracon.Workflows`, `Tracon.Abstractions` (yalnız XML)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Büyüdü** — 6 yeni kayıt overload'ı (`AddAgentDecorator` üçlüsü, `AddContentGuard` instance/factory ikilisi, `AddModelProvider<T>()`). Ölçüldü: `PublicAPI.Shipped.txt` **0 satır** (17 dosya), yani bugün eklemek ucuz, GA'dan sonra kırıcı
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/write-your-own-agent-decorator.md` (yeni — plan `extend/` diyordu, gerçek konvansiyon `guides/write-your-own-*` idi, bkz. Plandan Sapmalar) · `api/*` **üretildi** · sevk edilen: `ITraconBuilder`'ın XML `<example>`'ı **düzeltildi**
> **Manuel test alanı:** [`docs/manuel-test/`](../../manuel-test/00-INDEKS.md) — başlangıç uyarısı case'leri (MT-DIAG-055..057) gerçek `samples/Tracon.Api`'ye karşı koşuldu

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show b06bef5:docs/arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md
> ```
>
> Damıtıldı 2026-08-28 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz iki soruyu birlikte cevaplar, çünkü ikisi de **kayıt anına** bakar: 1. Üçüncü taraf bir tüketici, Tracon'in bir servisini nasıl **değiştirir**? Bugün bu desteklenen yol vardır ve çalışır, ama keşfedilemez — ve dokümanın kendi örneği kuralıyla çelişir. 2. Bir tüketici bir genişleme noktasını **kaydetmediğinde** ne olur?

## Bitiş Ölçütleri (DoD)

- [x] `AddAgentDecorator` üçlüsü var; üç decorator kaydedildiğinde **üçü de** zincirde ve `Order` sırasına uyuyor (fonksiyonel testle ölçüldü — `AgentDecoratorRegistrationTests.Three_registered_decorators_all_run_in_order_order`)
- [x] `AddContentGuard` instance/factory ve `AddModelProvider<T>()` var; hepsi `TryAddEnumerable`/`TryAdd*` kullanıyor
- [x] [`ITraconBuilder.cs`](../../../src/Tracon.Core/ITraconBuilder.cs)'ın `<example>`'ı **önce** kaydeden hâle geldi; iki sıranın farkı XML'de yazılı
- [x] Metin kapısına yeni satır eklendi ve **kasıtlı bozmayla kırmızı verdiği ölçüldü**; çıktı belgeye yazıldı (K-642 tuzağı) — `OrderingContractDocumentationTests`
- [x] Override sözleşmesi bölümü var; tekil/çoklu seam farkı tablo olarak anlatılıyor — `docs-site/guides/write-your-own-agent-decorator.md` (plan `extend/` diyordu, bkz. Plandan Sapmalar)
- [x] Production'da `IContentGuard` kayıtsızken **bir kez** uyarı; Development'ta ve ortam yokken **sessiz** — üçü de testle ölçüldü (`SilentGapWarningTests` x2 + `SilentGapWarningRegistrationTests`) + gerçek `samples/Tracon.Api`'de doğrulandı (MT-DIAG-055/056)
- [x] Retention kapalı/politikasızken Production uyarısı var — gerçek `samples/Tracon.Api`'de doğrulandı (MT-DIAG-057)
- [x] Uyarı servisi hiçbir koşulda host'u durdurmuyor (store çözümlemesi fırlatsa bile) — `A_throwing_content_guard_enumeration_does_not_stop_the_host`, `A_throwing_retention_options_read_does_not_stop_the_host`
- [x] BL-033 **sınıf taramasıyla** kapandı: `CompositeAgentCatalog` (iki decorate döngüsü), `AgentDecoratorPipeline` ve DÖRT tüketicisi (`RunReplayService`, `RunContinuationJobHandler`, `EvalJobHandler`, `AgentEndpoints.cs`'in parametreli-run yolu — denetimin bulduğu 4.'sü) ayrı ayrı tarandı; bulunan her yer aynı düzeltmeyi ve testi aldı
- [x] Duplicate workflow adı `TraconException` veriyor; ham `ArgumentException` yolu kapandı — `WorkflowCatalogTests`
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/format/secret taraması ayrı ayrı koşuldu, bağımsız denetim kendi izole worktree'sinde build+test'i tekrar doğruladı
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — `AddAgentDecorator` geçici bir decorator ile gerçek bir `run`a karşı doğrulandı (`MT-BL008-VERIFY: decorated 'support'` log satırı), üç uyarı case'i (MT-DIAG-055/056/057) gerçek host'a karşı koşuldu
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/` içine eklendi; MT-DIAG-055/056/057 ve MT-WF-119 gerçek/otomatik koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (2 🟡 bulundu, ikisi de düzeltildi; 1 🟢 düzeltildi)
- [x] `docs-site/` yeniden derlendi (**`--skip-docfx` KULLANILMADAN**); `check-links.mjs` temiz (151.308 referans, hiç kırık yok)
- [x] `YAYIN-HAZIRLIK.md`'de BL-008/016/018/019/033/034/039/051 güncellendi

### Doğrulama komutları

```bash
# Uc decorator da zincirde mi
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests \
  --filter-class "*AgentDecoratorRegistrationTests*"

# Retention uyarisi (MT-DIAG-057) — ornek uygulama Retention:Enabled'i hic acmaz,
# kod degisikligi GEREKMEZ
ASPNETCORE_ENVIRONMENT=Production dotnet run --project samples/Tracon.Api 2>&1 | grep -i "retention disabled"

# Content guard uyarisi (MT-DIAG-055) — ornek uygulama VARSAYILAN olarak
# .AddPatternContentGuard() cagirir; bu case'i gormek icin o cagriyi Program.cs'te
# GECICI olarak yorum satirina almak gerekir (bkz. 25-SAGLIK-TESHIS-OPENAPI.md)

# Public API buyumesi BEKLENEN kadar mi
git diff -- 'src/*/PublicAPI.Unshipped.txt' | grep -c '^+[^+]'
```

---

## Plandan Sapmalar

- **`docs-site/src/content/docs/extend/`** planın önerdiği yol yoktu — site
  konvansiyonu her genişleme noktası için `docs-site/src/content/docs/guides/
  write-your-own-*.md` (bkz. `write-your-own-agent-source.md`,
  `write-your-own-job-handler.md`, vb., hepsi `sidebar.mjs`'nin "Operate in
  production" bölümünde). Yeni `extend/` dizini açmak bu yerleşik deseni
  ikiye bölerdi. Override sözleşmesi tablosu (tekil/çoklu seam farkı) yeni
  `guides/write-your-own-agent-decorator.md` sayfasına yazıldı — `IAgentDecorator`
  zaten planın kendi çoklu-seam örneğiydi, sayfanın konusuyla birebir örtüşüyor.
- **BL-033'ün sınıf taraması planın öngördüğünden bir yer daha buldu.** Plan
  `CompositeAgentCatalog`, `AgentDecoratorPipeline` ve "iki job yolu" diyordu;
  gerçek tarama `AgentEndpoints.cs:748`'deki parametreli-run yolunu da
  `AgentDecoratorPipeline.Apply` kullanırken buldu (4. tüketici). Bu yol için
  ayrı bir gözlemlenebilirlik boşluğu (bağımsız denetimin 🟡 #2'si — aşağıda)
  ayrıca kapatıldı.
- **`RunReplayService.Decorate` ve `RunContinuationJobHandler.Decorate`
  kendi ham döngülerini elle yazıyordu; plan bunu değiştirmeyi istemiyordu**
  ama BL-033'ü DRY biçimde kapatmanın tek yolu ikisini de paylaşılan
  `AgentDecoratorPipeline.Apply`'a devretmekti (aksi hâlde aynı normalizasyon
  mantığı üç yerde ayrı ayrı yazılırdı). Davranış değişmedi, yalnız kod paylaşıldı.
- **`WorkflowCatalog`'un kurucusu `_codeWorkflows` alanını artık `ToDictionary`
  yerine elle bir döngüyle dolduruyor** (plan yalnız "duplicate ad
  `TraconException` versin" diyordu, mekanizmayı belirtmiyordu) —
  `WorkflowFunctionRegistry.cs:48`'in zaten kanıtlanmış deseni birebir kopyalandı.
- **Bağımsız denetim iki 🟡 buldu, ikisi de bu fazda kapandı** (aşağıya bkz.);
  plan bunları öngörmüyordu çünkü ikisi de uygulama sırasında ortaya çıkan
  ölçülmüş bulgulardı, plan-zamanı tahmin değildi.

## Bu Fazda Verilen Kararlar

- **K-645** — 60 tekil-registrasyon seam'ine dedicated `Add*()`/`Use*()`
  metodu eklenmez; `ITraconBuilder.Services`'in XML dokümanına bağlı bir
  metin kapısı sözleşmesi yeterli sayıldı (122.1(c)'nin planda "kapanışta
  K-NNN olarak yazılır" dediği karar).

## Denetim Bulguları

Bağımsız denetim (`faz-denetim`, taze bağlamlı ayrı agent, izole `git
worktree`'de kendi derleme+test koşumuyla doğruladı): **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | BL-033'ün sınıf taraması `CompositeAgentCatalog`'un versiyonlu `ResolveAsync(name, version, culture, token)` overload'ındaki ikinci decorate döngüsünü de düzeltti, ama yeni testler yalnız versiyonsuz overload'ı kanıtlıyordu | **Düzeltildi** — `AgentSourceFaultIsolationTests`'e `HealthyVersionedSource` + 2 yeni case eklendi (normalize + iptal geçirimi), versiyonlu overload'a karşı |
| 2 | 🟡 | `AgentDecoratorPipeline.Wrap`, kardeşi `CompositeAgentCatalog.HandleSourceFailure`'ın aksine hiç log/metrik yazmıyor; `AgentEndpoints.cs:748`'deki parametreli-run yolu (4. tüketici, `IAgentCatalog`'u tamamen atlar) bir decorator hatasını artık temiz bir `400`'e çeviriyor ama sunucu tarafında hiç iz bırakmadan | **Düzeltildi** — `AgentEndpoints.cs`'in `catch (TraconException ex)` bloğu, istisna `TraconAgentSourceException` ise `ILoggerFactory` üzerinden bir `LogError` yazıyor. `RunReplayService`/`RunContinuationJobHandler`/`EvalJobHandler` yolları zaten kendi dış `catch`'leri üzerinden logluyordu (davranış öncesinden bu yana değişmedi) — yalnız bu tek yol boştu |
| 3 | 🟢 | Faz dokümanının başlık satırı "5 yeni kayıt overload'ı" diyordu, gerçek sayı 6 (`AddAgentDecorator` üçlüsü + `AddContentGuard` ikilisi + `AddModelProvider<T>()`) | **Düzeltildi** — başlık satırı ve "Gerçekleşen Public API" bölümü düzeltildi |

Düzeltmelerden sonra dört kapı yeniden koşuldu: `dotnet build` (0 uyarı),
`dotnet test` (Core 2116/2116, Workflows 2/2 yeni, AspNetCore.FunctionalTests
697/697), `dotnet format --verify-no-changes` (temiz), secret taraması (temiz).

**Temiz çıkan başlıklar** (denetimin kendi ifadesiyle): 3.2 (test tiyatrosu
yok), 3.3 (test seviyeleri doğru), 3.5 (imza-gövde kayması yok), 3.6 (plan
dışı public API yok), 3.7 (repo kuralları), 3.8 (ürün yüzeyi).

🚨 **Bağımsız denetimin kendi build+test koşumu YALNIZ üç projeyi kapsıyordu**
(`Core.UnitTests`, `Workflows.UnitTests`, `AspNetCore.FunctionalTests`) — tam
`python3 scripts/kapi.py kapanis` ilk gerçek koşumunda `Tracon.Generators.
UnitTests` kırmızı verdi: `ITraconBuilder.cs`'in yeni `<example>`'ı
(`AuditingAgentDecorator`) `tests/Tracon.Generators.UnitTests/Examples/
ExamplePrelude.cs`'e stub olarak eklenmemişti (kardeş `GitAgentSource`/
`ResponseQualityJudge` deseni). Düzeltildi, 194/194 yeşil. **Ders:** bir
XML `<example>`'a yeni bir illüstratif tip adı (gerçekte var olmayan bir
sınıf) eklerken `ExamplePrelude.cs`'e stub eklemek gerekir — yalnız hedef
paketin kendi test projesini koşmak bunu YAKALAMAZ, tam çözüm koşumu gerekir.

## Sonraki Faza Devir Notu

- **`docs/KARARLAR.md` bütçesi bu fazın K-645 kaydıyla aşıldı** (391.720 B /
  390.000 B bütçe) — zaten `%0 boş` durumdaydı, herhangi bir yeni kayıt taşırırdı.
  Kapanış commit'inden HEMEN SONRA, çalışma ağacı temizken,
  `python3 scripts/dokuman-bakim.py karar-damit` ayrı bir commit olarak
  koşulmalı (araç `docs/KARARLAR.md`/`docs/arsiv/KARARLAR-GECMISI.md` kirliyse
  çalışmayı reddediyor — ölçüldü). `ARSIV_ESIK` (`docs/KARARLAR-INDEKS.md`
  bütçesi) bu fazda 115'ten 114'e indirildi; aynı 0-boşluk durumu tekrar
  oluşursa bir sonraki fazın kaydı yine taşırabilir.
- **`IAgentDecorator` artık kayıt API'sine sahip 5/5 çoklu seam'in
  tamamlandığı anlamına geliyor** — `IJobHandler`, `IContentGuard`,
  `IAgentSource`, `IAgentDecorator`, `IRunJudge` hepsi generic/instance/factory
  üçlüsüne sahip. Kulvar 2 kapandı; kalan tek açık kalem BL-008'in 6 tenant/store
  arayüzü (tekil seam, bilinçli olarak kapsam dışı — K-645).
- **🚨 `AgentDecoratorPipeline.Apply`'ın DÖRT tüketicisi var**, üçü değil:
  `RunReplayService`, `RunContinuationJobHandler`, `EvalJobHandler`,
  `AgentEndpoints.cs:748` (parametreli run). Decorator zincirine dokunan bir
  sonraki değişiklik bu dördünü de taramalı — `grep -rn
  "AgentDecoratorPipeline.Apply" src/` ile bulunur.
- **BL-039'un kalanı** (contract test taban sınıfı, dış sample, DI
  lifetime/thread-safety dokümanı — `IWorkflowRunner`/`IWorkflowFunctionCatalog`
  için) bu fazın kapsamı dışında bırakıldı; ayrı bir tur gerekir.
- **BL-008'in kalanı** (6 tenant/store arayüzü için dedicated `Add*`/`Use*`)
  K-645 ile bilinçli olarak kapsam dışı bırakıldı — yeniden açılma koşulu
  ölçülmüş bir tüketici şikâyetidir.
- Bu fazdan sonra `YOL-HARITASI.md`'de planlanmış bir sonraki faz **yok**.
  Sıradaki iş kullanıcı kararına bağlı: yeni bir `aday-kesfi`/`faz-planlama`
  turu, ya da `nuget-danismani`'nin yayın kararı turu (`YAYIN-HAZIRLIK.md`'nin
  kulvar 2/6'sı artık kapalı; kalan açık kulvarlar 1, 4, 5 ve BL-007/deki
  reusable contract test boşluğu).
