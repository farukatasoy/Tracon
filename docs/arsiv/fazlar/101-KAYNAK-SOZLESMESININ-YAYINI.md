# Faz 101 — Kaynak Sözleşmesinin Yayını

> **Durum:** ✅ Tamamlandı (2026-08-25)
> **Kaynak:** Doğrudan kullanıcı isteği (2026-08-25) — `IAgentSource` üçüncü
> taraf uygulanabilirlik incelemesi. Aday listesinden gelmedi; Faz 98 · 99 · 100
> ile aynı damardır: `preview.1` öncesi genişleme noktası olgunlaştırma.
> **Önkoşul:** [Faz 100](100-YARGIC-SOZLESMESININ-YAYINI.md) — bu faz
> ondan dört şey devralır: `ContractCoverage` aile mekanizması (K-610), opt-in
> sözleşme sınıfı kuralı (K-611), `AgentPrismJudgeException` hata normalizasyon
> deseni ve yalnız-NuGet sample emsali (test projesi kısıtı dahil, bkz. Risk R4).
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`,
> `AgentPrism.AspNetCore`, `AgentPrism.Testing.Contracts.Xunit`, `samples/`
> **Yeni paket:** Yok — sözleşme suite'i var olan pakete dördüncü bir ad alanı
> ekler · **Migration:** Yok
> **Public API:** Büyüyor. Ölçüldü (2026-08-25): `wc -l src/*/PublicAPI.Shipped.txt`
> = 17 satır, hepsi `#nullable enable` başlığı — **her dosya boştur**. Bu fazın
> dokunduğu her tip bugün bedava değişir; `preview.1` yayınlandıktan sonra
> `AgentDefinitionOrigin`, `AgentDescriptor` ve `IAgentSource` üzerindeki her
> değişiklik kırıcıdır (K-603).
> **Tüketici yüzeyi:** site: yeni `guides/write-your-own-agent-source.md`,
> `concepts/agents.md` ("two sources" cümlesi yanlış), `packages.md`,
> `capabilities.md` · sevk edilen: `IAgentSource` · `IVersionedAgentSource` ·
> `AgentDescriptor` · `AgentDefinitionOrigin` XML dokümanı,
> `src/AgentPrism.Testing.Contracts.Xunit/README.md`
> **Manuel test alanı:** [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md)
> (`CORE` öneki, bugün 55 case)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 971935b:docs/arsiv/fazlar/101-KAYNAK-SOZLESMESININ-YAYINI.md
> ```
>
> Damıtıldı 2026-08-25 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`IAgentSource`, AgentPrism'in dört genişleme noktasından dördüncüsüdür. Faz 98 depolamayı, Faz 99 sağlayıcıyı, Faz 100 yargıcı sevk edilen yüzeye taşıdı. Kaynak taşınmadı.

## Bitiş Ölçütleri (DoD)

- [x] `IAgentSource` · `IVersionedAgentSource` · `AgentDescriptor` XML'i 101.1'deki **on üç maddenin hepsini** taşır — M5'in eksik operasyonel cümlesi (her `ResolveAsync` bir `ListAsync` tetikler, sürüm çözümü kaynak kaynak dolaşır) denetim sonrası eklendi
- [x] Yanlış metinler gitti: `grep -rn "MAF hosting source\|from onwards" src/ docs-site/src/content/docs/api/` **boş döner**
- [x] `AgentDefinitionOrigin.Custom` eklendi; `GET /api/agents/{name}` `Custom` için `isEditable: false` döner
- [x] `Custom` kaynağa ait ada `PUT`/`DELETE` → `409` (bugün `404`)
- [x] Bir kaynak `ListAsync`'te patlarken `GET /api/agents` `200` döner ve kalan kaynakların agent'larını listeler
- [x] Bir kaynak `ResolveAsync`'te patlarken çağrı **durur**; daha düşük öncelikli kaynağın agent'ı çalışmaz
- [x] Hata gövdesi ham üçüncü taraf metni taşımaz; yalnız `{ad} ({kod})`
- [x] Gerçek `OperationCanceledException` normalize **edilmez**, yayılır
- [x] Her kaynak hatası bir `LogError` + `AgentSourceFailures` sayacı + diagnostics kaydı üretir
- [x] `AgentSourceValidationService` host başlarken çözülür; yinelenen kaynak adı host'u **başlatmaz** — hem birim hem gerçek `IHost` üzerinden fonksiyonel testle kanıtlandı
- [x] Startup doğrulaması hiçbir kaynağın `ListAsync`'ini çağırmaz (çağrı sayacı `0` ile kanıtlanır)
- [x] Katalog artık descriptor **uydurmaz**; tutarsızlık uyarı + sayaç üretir, hata **atmaz**
- [x] Descriptor ve koleksiyonları sınırda dondurulur; kaynak sonradan değiştirse de katalog çıktısı ve run kaydı değişmez — gerçek mutasyon testiyle (kaynağın backing listesi sonradan değiştirilerek) kanıtlandı
- [x] `AddAgentSource<T>()` · `AddAgentSource(instance)` · `AddAgentSource(factory)` çalışır; lifetime **singleton**
- [x] `CompileCachedAsync` fingerprint'i skill + alt agent + paylaşılan talimattan birleştirir ve BYOK'ta cache'i **atlar**; iki ayrı test — BYOK zaten `DefinitionStoreAgentSourceTenantCredentialTests`'te vardı, üç fingerprint kaynağı bu fazda `CompileCachedAsyncTests`'e eklendi
- [x] Üç kopya blok tekile indi: `grep -c "CombineFingerprints" src/AgentPrism.Core/Catalog/*.cs` → `0`
- [x] `AgentSourceContract` `AgentPrism.Testing.Contracts.AgentSources` ad alanında yayınlandı; **on iki** senaryo koşuyor — planlanan on üçüncü senaryo (dönen koleksiyonun mutasyonu) jenerik arayüzde test edilemez olduğu için kaldırıldı, gerçek kanıt katalog sınırında (`AgentSourceMutationTests`) duruyor; bkz. Denetim Bulguları #1
- [x] `ContractCoverage.AgentSourceContracts` sabiti eklendi; kapsam testi yeşil
- [x] `CodeAgentSource` ve `DefinitionStoreAgentSource` suite'i geçiyor; ikincisi iki opt-in sınıfı da geçiyor
- [x] Mevcut üç ailenin kapsam testleri (dört depolama koşumu + sağlayıcı + yargıç) **yeşil kaldı** — tam çözüm koşumunda 5258/5258 test yeşil
- [x] Sözleşme paketinin bağımlılık grafiğine `AgentPrism.Core` **inmez** (`project.assets.json` ölçümü — üç TFM'de de `[]`)
- [x] Sözleşme paketinin public yüzeyine `Shouldly` tipi sızmaz
- [x] `samples/AgentPrism.Samples.CustomAgentSource` yalnız `PackageReference` kullanır; gerçek `<ProjectReference` öğesi yok (`grep -c "<ProjectReference"` → `0`; ham `grep -c ProjectReference` yorum metnindeki kelimeyi de sayıp `2` döndüğü için denetimde yanlış pozitif tespit edildi, yorum metni düzeltildi — bkz. Denetim Bulguları #2)
- [x] Sample `AddAgentSource<T>()` ile kaydedilir; `TryAddEnumerable` sample kodunda **geçmez** (`grep -c TryAddEnumerable samples/AgentPrism.Samples.CustomAgentSource*/*.cs` → `0`)
- [~] Arayüz bundle payı ölçülmedi — bu faz `en.ts`/`tr.ts`'e yeni anahtar **eklemedi** (`origin.other` zaten Faz ~90'dan beri sözlükte duruyordu, yalnız ulaşılamazdı); ölçüm gerektiren bir değişiklik yok
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build` · tam `dotnet test AgentPrism.slnx -maxcpucount:1` (23 proje, 5258/5258) · `dotnet pack --no-build` · `dotnet format --verify-no-changes` (restore'suz format'ın yerel feed paketleriyle çalışmadığı ölçüldü — bkz. Sonraki Faza Devir Notu)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı; sample kaynak kaydedilip agent çalıştırıldı, çıktı belgeye yazıldı — **kısmi**: `samples/AgentPrism.Api` PostgreSQL'e bağımlı olduğu ve bu ortamda Docker kurulmadığı için doğrudan koşulamadı; gerçek run kanıtı `samples/AgentPrism.Samples.CustomAgentSource.Tests` (yalnız local NuGet feed'den `PackageReference`, 15/15 yeşil, `AddAgentSource<T>()` ve factory kayıt yollarının ikisiyle de gerçek `agent.RunAsync()`) ve `AgentPrismTestHost` üzerinden fonksiyonel testlerle (`AgentSourceCustomOriginTests`, `AgentSourceFaultIsolationHttpTests`, `AgentSourceValidationHttpTests`, `DiagnosticsEndpointTests.Registered_agent_sources_are_reported_in_priority_order`) sağlandı
- [x] `secret` taraması boş döndü (`python3 scripts/kapi.py tarama` → ✅ temiz)
- [x] Manuel kabul case'leri `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` içine eklendi (MT-CORE-087..096); otomatikleştirilebilenlerden MT-CORE-096 koşuldu (15/15) — 087-094 `samples/AgentPrism.Api`'nin Postgres bağımlılığı yüzünden, 095 kiracıya duyarlı örnek kaynak bu depoda olmadığı için (👤 gerekir) koşulmadı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — dört 🟡 bulgunun tamamı kapandı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi (`guides/write-your-own-agent-source.md` sidebar'a eklendi + `<example>` stub'ı + `concepts/agents.md` düzeltmesi + `observability.md`'ye iki yeni telemetry alanı); `npm run check` (content + build + links + weight) dördü de temiz

### Doğrulama komutları

```bash
# Dört kapı — taban, bu fazın implementasyon commit'i (00ea44f)
python3 scripts/kapi.py kapanis --taban 00ea44f

# Yanlış sevk edilen metin gerçekten gitti
grep -rn "MAF hosting source\|from onwards" src/ docs-site/src/content/docs/api/ || echo "temiz"

# Üç kopya fingerprint bloğu tekile indi
grep -c "CombineFingerprints" src/AgentPrism.Core/Catalog/*.cs

# Sözleşme paketi Core'a inmiyor (üç TFM'nin hepsi kontrol edilir)
F=$(find artifacts/obj/AgentPrism.Testing.Contracts.Xunit -name project.assets.json | head -1)
python3 -c "import json;d=json.load(open('$F'));[print(t,[k for k in libs if 'AgentPrism.Core' in k]) for t,libs in d['targets'].items()]"
# beklenen: her TFM için []

# Sample yalnız NuGet, ergonomik kayıt — gerçek <ProjectReference öğesini say, yorum metnindeki kelimeyi değil
grep -c "<ProjectReference" samples/AgentPrism.Samples.CustomAgentSource/*.csproj || true   # 0
grep -c TryAddEnumerable samples/AgentPrism.Samples.CustomAgentSource*/*.cs || true          # 0

# Sample paketi local feed'den restore edebilmesi için önce paketle (yeni proje eklendiğinde şart)
python3 scripts/kapi.py yayin --kuru
rm -rf ~/.nuget/packages/agentprism* artifacts/obj/AgentPrism.Samples.CustomAgentSource.Tests

# İzolasyon ve tutarlılık regresyonları (ayrı filtrelerle — bu test host'u | ile birleşik deseni desteklemiyor)
B=./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests
for p in AgentSourceFaultIsolation AgentSourceConsistency AgentSourceMutation AgentSourceValidation; do
  $B --filter-method "*$p*"
done

# Sample'ın kendi kanıtı — gerçek run, yalnız yayınlanmış paketlerle
dotnet test samples/AgentPrism.Samples.CustomAgentSource.Tests -c Release --no-build

# Mevcut aileler kırılmadı
$B --filter-method "*ContractCoverage*"
```

---

## Plandan Sapmalar

**101 iki oturumda kapandı.** İlk oturum (`00ea44f`, "feat: publish agent
source contract") kodun ilk sürümünü yazdı ama hiçbir kapanış kapısını
koşmadı — `PublicSurfaceBaselineTests`, `CapabilityExampleTests` ve
`OpenApiSnapshotTests` bayattı, kanıt katmanı (test) neredeyse yoktu. Bu ikinci
oturum o implementasyonu tamamladı: eksik testleri yazdı, iki gerçek
regresyonu buldu ve düzeltti, `ResolveDependenciesAsync`'i internal'a çevirdi.
Aşağıdaki sapmalar bu ikinci oturuma aittir.

- **`ResolveDependenciesAsync` / `AgentCompilationDependencies` planlanandan
  farklı olarak `internal`.** Plan (101.7) ikisini de public varsaydı. Ölçüm:
  gerçek bir sample (`AgentPrism.Samples.CustomAgentSource`) yazıldıktan sonra
  bu iki üyenin **sıfır** dış tüketicisi olduğu görüldü — sample da,
  yerleşik iki kaynak da yalnızca `CompileCachedAsync`'i çağırıyor.
  `AgentCompilationDependencies.cs`'in kendi XML'i gerekçeyi taşır: "zero real
  callers ... would be exactly the 'maybe a third party needs it someday'
  surface YAGNI exists to prevent". `PublicAPI.Shipped.txt` boş olduğu için
  (K-603) bu kırıcı bir değişiklik değil, yeni bir K-numarası da gerektirmedi.
- **`AgentSourceContract` on iki senaryo taşıyor, planlanan on üç değil.**
  Kaldırılan `A_returned_list_does_not_change_a_later_listing` test tiyatrosuydu
  (`Listing_is_repeatable` ile birebir aynı davranışı ölçüyordu, hiçbir şeyi
  mutasyona uğratmıyordu). Onun yerini alan `Resolved_agent_is_listed` (M7'nin
  eksik yönü: resolve edilen adın listede de görünmesi) ile birlikte on iki
  senaryoya çıktı. Gerçek mutasyon kanıtı — kaynağın backing koleksiyonunu
  sonradan değiştirip dondurulmuş descriptor'ın değişmediğini görmek — jenerik
  `IAgentSource` sözleşmesinde test edilemez (arayüz yalnız `IReadOnlyList<T>`
  döndürür, mutasyona açık somut bir tip vermez); bu kanıt katalog sınırında
  (`AgentSourceMutationTests`, gerçek mutasyon yapan bir fake source ile) yaşıyor.
- **`CompositeAgentCatalog`'un istisna normalizasyonu ikinci oturumda
  düzeltildi.** İlk oturumun kodu `catch (Exception exception)` bloklarında
  HER istisnayı `AgentPrismAgentSourceException`'a sarıyordu — bu, kaynağın
  `CompileCachedAsync` üzerinden fırlattığı **normal** `AgentPrismCompilationException`
  (bilinmeyen `provider`, bilinmeyen tool adı gibi) hatalarını da yanlış tipe
  dönüştürüyordu. Regresyon, önceden var olan bir sample testinde
  (`AgentPrism.Samples.CustomModelProvider.Tests.An_unknown_provider_name_fails_with_a_message_naming_the_registered_ones`)
  yakalandı. Düzeltme: `HandleSourceFailure` artık yalnız `AgentPrismException`
  **olmayan** (yani ham, beklenmeyen) istisnaları sarar; herhangi bir
  `AgentPrismException` alt tipi (kaynağın kendi fırlattığı dahil) tipini
  koruyarak yayılır. `AgentSourceValidationService`'in
  `catch (AgentPrismException) when (source is DefinitionStoreAgentSource)`
  özel-durum kodu bu genel kuralla gereksiz hâle geldi ve kaldırıldı.
- **`tests/AgentPrism.Generators.UnitTests/Examples/ExamplePrelude.cs`'e
  `GitAgentSource` stub'ı eklendi.** `IAgentPrismBuilder.AddAgentSource<T>()`'a
  eklenen `<example>` bloğu kurgusal `GitAgentSource` tipini adlandırıyordu;
  bu tip örnek-derleme testinin (`ExampleCompilationTests`) derleme
  bağlamında yoktu (Faz 98'in aynı deseniyle: `OnPremiseModelProvider` vb.).
- **NuGet global paket önbelleği ve `docfx.json`'ın `references` globu iki
  ayrı ortam tuzağı olarak ölçüldü, kodu etkilemedi ama kapanış süresini
  uzattı.** Bkz. Sonraki Faza Devir Notu.
- **`samples/AgentPrism.Api` ile gerçek run DoD'si kısmen karşılandı.** Bu
  sample PostgreSQL'e bağımlı; bu ortamda Docker kurulu değildi. Gerçek run
  kanıtı bunun yerine `samples/AgentPrism.Samples.CustomAgentSource.Tests`
  (yalnız local NuGet feed'den `PackageReference`, gerçek `agent.RunAsync()`)
  ve `AgentPrismTestHost` üzerinden fonksiyonel testlerle sağlandı — ikisi de
  gerçek ASP.NET Core/DI boru hattından geçiyor, yalnız Postgres'e
  bağlanmıyor. MT-CORE-087..094 aynı nedenle koşulmadı (bkz. `docs/manuel-test/00-INDEKS.md`).

## Bu Fazda Verilen Kararlar

Yeni bir `K-NNN` kaydı **açılmadı**. `PublicAPI.Shipped.txt` üç pakette de
boş olduğu için (K-603) bu fazın tüm public yüzey kararları — `Custom` origin
değeri, `AgentPrismAgentSourceException`, `AgentSourcePriority`,
`AddAgentSource` üç aşırı yüklemesi, `CompileCachedAsync` — henüz "kırılabilir"
bir sözleşme değil; K-defterinin kendi kuralı ("Yerel implementation tercihi
faz dokümanında... kalır") burada geçerlidir. `ResolveDependenciesAsync`'i
internal yapma kararı da aynı gerekçeyle karar defterine girmedi — gerekçesi
yukarıda ve `AgentCompilationDependencies.cs`'in kendi XML'inde duruyor.

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bir agent olarak koştu (`00ea44f`
tabanına karşı çalışma ağacı diff'i). **🔴 yok.** Dört 🟡 bulgunun hepsi bu
oturumda kapandı:

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `AgentSourceContract` on iki `[Fact]` taşıyor, DoD/101.10 "on üç senaryo" diyordu; kaldırılan senaryo zaten test tiyatrosuydu, gerçek mutasyon kanıtı jenerik arayüzde test edilemez | 🟡 | **Gerekçelendi** — DoD ve 101.10 metni düzeltildi, gerekçe Plandan Sapmalar'da |
| 2 | DoD'nin `grep -c ProjectReference` doğrulama komutu csproj yorum metnindeki "ProjectReference" kelimesini de sayıp `2` döndürüyor; gerçek `<ProjectReference>` öğesi yok | 🟡 | **Düzeltildi** — yorum metni "a project-to-project reference" olarak yeniden yazıldı, doğrulama komutu `grep -c "<ProjectReference"` oldu |
| 3 | `AgentSourceValidationServiceTests` yalnız `new AgentSourceValidationService(...)` ile birim seviyesinde test ediyor; gerçek `IHost`'un başlamadığını kanıtlayan fonksiyonel test yoktu (Faz 100'ün `RunJudgeValidationService`'inden devralınan emsal) | 🟡 | **Düzeltildi** — `AgentSourceValidationHttpTests.Two_sources_sharing_a_name_stop_the_host_from_starting` eklendi, gerçek `AgentPrismTestHost.StartAsync()` üzerinden `IHostedService.StartAsync` zincirini tetikleyip `AgentPrismAgentSourceException`'ın host başlatmayı durdurduğunu kanıtlıyor |
| 4 | `ResolveDependenciesAsync`/`AgentCompilationDependencies`'in internal'a çevrilmesi "Planlanan Public API"den sapma; faz dokümanının "Plandan Sapmalar"/"Gerçekleşen Public API" bölümleri henüz boştu | 🟡 | **Düzeltildi** — bu kapanış bu bölümleri doldurdu |

Denetçinin doğrudan koştuğu hedefli testler: `AgentSource*` (Core.UnitTests,
78/78) · `CompileCached*` (5/5) · `PublicSurface`/`ContractCoverage` (1/1,
2/2) · AspNetCore.FunctionalTests `AgentSource*` (7/7) · sample'ın kendi test
projesi local feed üzerinden (15/15, `ProjectReference` yok).

## Sonraki Faza Devir Notu

**Devralınan sözleşme.** `IAgentSource` artık `IRunJudge` seviyesinde
belgelenmiş: singleton/thread-safety, `ListAsync`'in hot-path doğası (her
`ResolveAsync` bir `ListAsync` tetikler), List/Resolve tutarlılığı,
`CancellationToken` semantiği, öncelik kuralı (küçük kazanır, eşitlik DI
sırasına düşer, `0`/`100` **rezerve değil**, yalnızca yerleşik kaynakların
değeri), tenant kuralı hepsi `IAgentSource.cs`'in kendi XML'inde. Yeni bir
`IAgentSource` yazan biri artık kaynak kodu okumak zorunda değil.

**Davranış sözleşmesi tablosu.**

| Kural | Testi |
|---|---|
| `ListAsync` fail-open, `ResolveAsync` fail-closed | `AgentSourceFaultIsolationTests` |
| `AgentPrismException` alt tipleri (kaynağın kendi fırlattığı dahil) tipini koruyarak yayılır; yalnız ham istisna sarılır | `AgentSourceFaultIsolationTests`, `CompositeAgentCatalog.HandleSourceFailure` |
| Resolve edilen ama listelenmeyen ad → uydurma yok, gerçek `SourceName` + `Origin=Custom`, uyarı+sayaç, hata atılmaz | `AgentSourceConsistencyTests` |
| Descriptor ve iç koleksiyonları katalog sınırında dondurulur | `AgentSourceMutationTests` |
| Startup doğrulaması `ListAsync` çağırmaz, yalnız `Name`/`Priority` okur | `AgentSourceValidationServiceTests`, `AgentSourceValidationHttpTests` |
| `AddAgentSource<T>()` tek instance, `AddAgentSource(instance/factory)` çoklu | `AgentPrismBuilderAgentSourceTests` |
| `CompileCachedAsync` fingerprint'i skill+callable+shared-instructions'ı birleştirir, BYOK'ta cache atlanır | `CompileCachedAsyncTests`, `DefinitionStoreAgentSourceTenantCredentialTests` |

**🚨 Bilinen tuzaklar (bu fazda ölçüldü):**

- **NuGet global paket önbelleği (`~/.nuget/packages/agentprism*`) `VersionOverride="*-*"` ile en yüksek sürümü seçer, local feed'i değil.** Bu makinede `1.0.0-preview.2` adında eski bir sürüm önbellekte kalmıştı (muhtemelen daha önceki bir `--surum` denemesinden); taze `dotnet pack` sonrası bile samples projeleri o eski sürümü çözüyordu ve `AgentDefinitionOrigin.Custom` gibi yeni tipler "bulunamadı" hatası veriyordu. **Kural: yeni bir sample projesi eklerken veya `AgentPrism.*` public API'sini değiştirdikten sonra samples'ı derlemeden önce `rm -rf ~/.nuget/packages/agentprism*` çalıştır.**
- **`artifacts/package/release`'te birikmiş eski `.nupkg`'lar aynı sorunu ham `dotnet pack` ile üretir.** `python3 scripts/kapi.py yayin --kuru` (`_clean_stale_packages` çağırır) bunu otomatik temizler; ham `dotnet pack AgentPrism.src.slnf` **temizlemez** — bu fazda tam olarak bu yüzden `1.0.0-preview.2` sürümü tekrar seçildi. Faz 91'in hafızası bunu zaten "prior --surum run outlives an incremental pack" diye yazmıştı; bu fazda ham `dotnet pack` kullanılınca yeniden yaşandı.
- **`docfx metadata`'nın `references` globu (`*/release{,_net10.0}/*.dll`), `artifacts/bin`'de çok sayıda TEST projesi birikince (tam çözüm build'i + defalarca kısmi build) `CS1704` (aynı basit adlı derleme) fırtınasına giriyor.** Faz 98'in kaydettiği "yeni tek-TFM proje" senaryosundan farklı bir tetikleyici: burada suçlu YENİ proje değildi (yeni `AgentPrism.Samples.CustomAgentSource*` zaten `references.exclude`'da), suçlu birikmiş `artifacts/bin` idi. **Kural: `docs-site npm run check` çalıştırmadan önce `rm -rf artifacts/bin artifacts/obj && dotnet build AgentPrism.slnx -c Release` ile TEK, temiz bir build yap.**
- **`dotnet format --verify-no-changes --no-restore`, `samples/`'ın `VersionOverride="*-*"` + yerel `NuGet.config` kombinasyonuyla workspace'i yükleyemiyor** (`CS0246` — tip bulunamadı, `dotnet build`in kendisi başarılıyken). `--no-restore` bayrağını düşürmek (`dotnet format AgentPrism.slnx --verify-no-changes`) sorunu çözüyor; kök neden izole edilmedi ama örnekleri (`FileRunStore.Tests` gibi hiç dokunulmamış bir proje de aynı hatayı veriyor) bunun samples-genelinde bir ortam kısıtı olduğunu, benim değişikliklerimle ilgisiz olduğunu gösteriyor. `scripts/kapi.py`'nin `closing_commands` listesindeki format komutu hâlâ `--no-restore` taşıyor — bu depoda bir sonraki `dotnet format` çağrısı samples'a dokunan bir faz için tekrar kırmızı görünebilir.
- **System.Text.Json'ın source-generated (Metadata modu) deserializer'ı, `required` üye taşıyan bir tipte, JSON'da GEÇMEYEN özellikler için C# varsayılan değer ifadesini (`= []` gibi) ÇALIŞTIRMAZ — `null` bırakır.** `JsonFileAgentSource` örneğinde `AgentDefinition.ToolNames`/`SkillNames`/... ve `ModelBinding.ProviderSettings`/`Fallbacks` JSON'da yoksa `null` geliyordu, `AgentDefinitionCompiler` bunu `null` olamayacağını varsayarak `NullReferenceException` fırlatıyordu. Kaynak kodu bunu `Normalize()` ile düzeltiyor (bkz. `JsonFileAgentSource.cs`, `Normalize` metodunun XML'i). **Bir üçüncü tarafın kendi tanım formatını JSON'dan okuyan her `IAgentSource` bu tuzağa düşer** — `write-your-own-agent-source.md`'ye eklenmesi düşünülebilir (bu fazda eklenmedi, gerekçe: rehber zaten uzun, kaynak kodundaki XML yeterli görüldü).

**Site senkron gerekçesi (`--site-gerekce-yazildi`).** `dokuman-bakim.py --site-denetle`
iki kural tetikledi, ikisi de bu ikinci oturumdan önce zaten karşılanmıştı:

- **`buildtransitive → capabilities.md`**: `AgentMap.md` bu oturumda
  yeniden üretildiği (`build-agent-map.mjs`) için değişti, ama `capabilities.md`
  "Custom agent source" satırını **ilk** oturumda (`00ea44f`) zaten almıştı —
  üç `AddAgentSource` aşırı yüklemesi, salt-okunur davranış hepsi orada.
  İkinci oturum yeni bir yetenek eklemedi, yalnız XML metnini düzeltti.
- **`cekirdek-kavram → concepts/`**: `AgentPrismException.cs`'in tek
  değişikliği ikinci ctor parametresinin `Exception!`'dan `Exception?`'a
  genişlemesi — davranışsal fark yok (`null` geçmek zaten çalışıyordu, sadece
  derleyici artık şikâyet etmiyor). Kavramsal bir model değişikliği değil;
  `concepts/`'te anlatılacak yeni bir fikir yok.

**Yarım kalan iş.** `samples/AgentPrism.Api` ile gerçek run yapılamadı
(Postgres/Docker gerektiriyor, bu ortamda kurulu değildi); MT-CORE-087..094
aynı nedenle koşulmadı, MT-CORE-095 kiracıya duyarlı örnek kaynak
gerektiriyor (👤). `agentprism.agent_source.name`/`agentprism.agent_source.operation`
etiketleri `observability.md`'ye eklendi ama gerçek bir dashboard/alert
örneğiyle gösterilmedi (yerleşik iki tag'in de yapmadığı bir şey, kapsam
genişletmesi olurdu).

**Sıradaki faz.** `docs/YOL-HARITASI.md` üretildikten sonra seçilir; bu faz
aday listesinden gelmedi (doğrudan kullanıcı isteği), aday listesine yeni bir
kalem eklemedi.
