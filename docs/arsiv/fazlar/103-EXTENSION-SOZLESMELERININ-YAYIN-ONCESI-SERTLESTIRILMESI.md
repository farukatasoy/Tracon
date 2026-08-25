# Faz 103 — Extension Sözleşmelerinin Yayın Öncesi Sertleştirilmesi

> **Durum:** ✅ Tamamlandı (2026-08-25)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-153**
> **Önkoşul:** [Faz 98](98-DEPOLAMA-SOZLESMESININ-YAYINI.md) — Storage contract ve packed-package sample deseni · [Faz 99](99-SAGLAYICI-SOZLESMESININ-YAYINI.md) — provider contract, BYOK ve package graph · [Faz 100](100-YARGIC-SOZLESMESININ-YAYINI.md) — judge runtime ve contract family · [Faz 101](101-KAYNAK-SOZLESMESININ-YAYINI.md) — üç overload'lı singleton registration ve sample deseni · [Faz 102](102-TOOL-SOZLESMESI-VE-SONUC-SINIRI.md) — tool contract, canonical result ve AOT sınırı
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.Mcp`, `.OpenAI`, `.Anthropic`, `.Google`, `.Azure`, `.Testing`, `.Testing.Contracts.Xunit`, `AgentPrism` meta package
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Değişiyor — `IModelProvider` BYOK capability'si ayrılır, `IAgentPrismBuilder.AddRunJudge(...)` eklenir, kullanılmayan `AgentPrismJudgeException` kaldırılır; `PublicAPI.Shipped.txt` dosyalarında K-603 gereği public symbol baseline'ı yoktur, yalnız `#nullable enable` vardır
> **Tüketici yüzeyi:** site: `guides/model-providers.md`, `guides/write-your-own-judge.md`, `guides/write-your-own-agent-source.md`, `guides/write-your-own-tool.md`, `concepts/evaluation.md`, `concepts/tools.md`, `packages.md`, `capabilities.md`, `reference/configuration.md`, `reference/compatibility.md`, `reference/versioning.md`
> · sevk edilen: extension XML'leri, `src/AgentPrism.Testing.Contracts.Xunit/README.md`, package description'ları, beş extension sample'ı
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) · [`02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md) · [`08-OPENAI-UYUMLU-UCLAR.md`](../../manuel-test/08-OPENAI-UYUMLU-UCLAR.md) · [`17-EVAL-VE-DENEYLER.md`](../../manuel-test/17-EVAL-VE-DENEYLER.md) · [`18-MCP-VE-A2A.md`](../../manuel-test/18-MCP-VE-A2A.md) · [`24-TEST-PAKETI-VE-SABLON.md`](../../manuel-test/24-TEST-PAKETI-VE-SABLON.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 1590184:docs/arsiv/fazlar/103-EXTENSION-SOZLESMELERININ-YAYIN-ONCESI-SERTLESTIRILMESI.md
> ```
>
> Damıtıldı 2026-08-25 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 98–102 third-party extension yüzeylerini ayrı ayrı yayımladı. Packed NuGet katmanı ölçümde sağlam çıktı. Kalan preview.1 riski paket eksikliği değil; üç yanlış public/runtime söz, bir güvenlik sızıntısı, zayıf executable proof ve deterministik olmayan release sample doğrulamasıdır. Bu faz yalnız bu blocker'ları preview.1 yayınından önce kapatır.

## Bitiş Ölçütleri (DoD)

- [x] BYOK mandatory/optional çelişkisi XML, contract suite ve runtime'da yoktur.
- [x] Tenant credential varken capability desteklemeyen provider setup/global
      credential ile çağrılmaz; provider invocation count `0` ve stable
      `provider_credential_unsupported` kanıtlanır.
- [x] Provider foreign raw exception text agent SSE, buffered HTTP, persisted
      `RunError`, OpenAI Responses, Chat Completions, MCP ve ölçülen A2A yüzeyinde
      görünmez. (A2A kendi `ex.Message` kullanmıyor — bkz. Plandan Sapmalar.)
- [x] Raw provider exception `ILogger` exception graph'ında korunur; duplicate log
      fırtınası yoktur.
- [x] Bilinen güvenli AgentPrism exception contract'ları korunur; caller/host OCE
      provider error'a çevrilmez.
- [x] `JudgeTimeout`, token'ı yok sayan judge için gerçek wait cutoff yapar.
- [x] Late judge success/fault gözlemlenir; unobserved exception, late score,
      summary veya metric yoktur.
- [x] Host cancellation timeout'tan ayrıdır; retry/backoff/job lease contract'ı
      regression testleriyle korunur.
- [x] `RunJudgeContract` en az bir built-in ve bir CustomRunJudge sample consumer'a
      sahiptir.
- [x] `samples/AgentPrism.Samples.CustomRunJudge.Tests` AgentPrism için yalnız exact
      local-feed `PackageReference` kullanır; contract, registration ve real
      run/evaluation + score persistence testleri geçer.
- [x] `AddRunJudge<TJudge>()`, instance ve factory overload'ları public'tir;
      singleton ve duplicate davranışları AgentSource deseniyle ölçülmüştür.
- [x] Kullanılmayan public `AgentPrismJudgeException` kaldırılmıştır; stable judge
      error code'ları korunur ve yeni gereksiz public exception eklenmez.
- [x] Tool guard ve truncation aynı canonical representation'ı görür.
- [x] Tool result matrisi string, `JsonElement`, primitive, record/class,
      collection, null, unsupported raw object, generated complex context ve
      direct `AIFunction` yolunda yeşildir.
- [x] Unsupported raw CLR tool result pass-through yapmaz; reflection serializer
      eklenmez; `AgentPrism.Core` AOT-compatible kalır.
- [x] Provider, judge, source ve tool concurrency testleri deterministic gerçek
      overlap kanıtlar; mutation/red→green kaydı vardır.
- [x] Cancellation testleri ignore davranışını başarı saymaz; uygun async
      fixture'larda in-flight cancellation ölçülür.
- [x] Storage contract suite gövdeleri gereksiz değiştirilmemiştir ve dört built-in
      provider + FileRunStore consumer'ı yeşildir.
- [x] Beş `ContractCoverage` family testi yeşildir; `Skip` sayısı `0` ve yeni
      family diğer aileleri kırmaz.
- [x] `AgentPrism.Testing.Contracts.Xunit` resolved graph'ında `AgentPrism.Core`
      yoktur; Shouldly/xunit tipi production package public API'sine sızmaz.
- [x] Release sample verification exact version, isolated `NUGET_PACKAGES`, temiz
      local feed ve AgentPrism ProjectReference yasağını zorlar.
- [x] FileRunStore, CustomModelProvider, CustomRunJudge, CustomAgentSource ve
      CustomTool packed local feed'den build/test/run olur.
- [x] Provider, source ve generated complex tool packed consumer Native AOT altında
      publish/run olur.
- [x] Meta package graph'ında `AgentPrism.Testing` veya
      `AgentPrism.Testing.Contracts.Xunit` yoktur.
- [x] `Testing.Contracts.Xunit` README/package description beş family'yi anlatır;
      `packages.md`, `compatibility.md`, `versioning.md`, capability/configuration
      sayfaları runtime ve 20-package çıktısıyla tutarlıdır.
- [x] Provider/source/tool metric name low-cardinality beklentisi ve gerekli
      instance/factory disposal ownership farkı XML/site'da açıktır.
- [x] Docs tool canonicalization ve judge timeout için runtime'dan daha güçlü söz
      vermez.
- [x] Fazın PublicAPI audit tablosundaki her tip için keep/remove gerekçesi
      kapanış kaydına yazılmıştır; plan dışı public helper yoktur.
- [x] Dört doğrulama kapısı sıfır uyarı verir.
- [x] `samples/AgentPrism.Api` ile gerçek provider + tool + judge içeren `run`
      yapılır; çıktı ve score kanıtı bu belgeye yazılır.
- [x] `secret` taraması boş döner; kasıtlı test fixture secret'ları yalnız açık
      test allowlist'i içinde ve production output dışında kalır.
- [x] Manuel kabul case'leri ilgili `docs/manuel-test/` dosyalarına eklenir;
      otomatikleştirilebilenler koşulur.
- [x] `tuketici-dokuman-senkronu` runtime donduktan sonra koşar; docs-site
      `npm run check` temizdir.
- [x] Bağımsız `faz-denetim` taze bağlamla `git diff $FAZ_TABANI` üzerinde koşar;
      özellikle test tiyatrosu, raw secret leak, packed artifact ve plan dışı
      public API arar; 🔴 bulgu kalmaz.

### Doğrulama komutları

```bash
# Dar iç döngüler
dotnet test tests/AgentPrism.Core.UnitTests --filter "ModelProviderRegistry|FallbackChatClient|OnlineEvalJobHandler|RunJudge|ToolResultText|TruncatingAIFunction|AgentSourceContract"
dotnet test tests/AgentPrism.AspNetCore.FunctionalTests --filter "ProviderOutage|AgentRun|OpenAI|Mcp|ToolCanonical"

# Contract consumers
dotnet test samples/AgentPrism.Samples.FileRunStore.Tests
dotnet test samples/AgentPrism.Samples.CustomModelProvider.Tests
dotnet test samples/AgentPrism.Samples.CustomRunJudge.Tests
dotnet test samples/AgentPrism.Samples.CustomAgentSource.Tests
dotnet test samples/AgentPrism.Samples.CustomTool.Tests

# Family/skip/public dependency sınırı
rg -n "Skip\\s*=" src/AgentPrism.Testing.Contracts.Xunit/Contracts && exit 1 || true
dotnet list src/AgentPrism.Testing.Contracts.Xunit package --include-transitive
rg -n "AgentPrism.Core" src/AgentPrism.Testing.Contracts.Xunit/obj/project.assets.json && exit 1 || true
rg -n "Shouldly|Xunit" src/AgentPrism.{Abstractions,Core}/PublicAPI.Unshipped.txt && exit 1 || true

# Exact packed consumer + Native AOT kapısı
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1

# Public API dar audit
git diff -- src/*/PublicAPI.Unshipped.txt src/*/PublicAPI.Shipped.txt
rg -n "AgentPrismJudgeException|ITenantCredentialModelProvider|AddRunJudge|AuthorizingAIFunction|TimeoutAIFunction|TruncatingAIFunction|AgentPrismGeneratedToolArguments|AllowUnverifiedToolRegistry" src/*/PublicAPI.*.txt

# Site ve kapanış
cd docs-site && npm run check
cd ..
python3 scripts/dokuman-bakim.py --site-denetle
python3 scripts/kapi.py kapanis --taban "$FAZ_TABANI"
```

Filtreler gerçek test adları oluşunca güncellenir. Hiç test seçmeyen filtre yeşil
kanıt sayılmaz; TRX/test count kontrol edilir.

---

## Plandan Sapmalar

Bu faz, önceki bir oturumun **uncommitted** bıraktığı bir gövde üzerinde
tamamlandı: implementasyonun büyük kısmı (BYOK split, provider normalizer,
`AddRunJudge`, `JudgeTimeout` gerçek cutoff, tool canonical result, CustomRunJudge
sample+test, ExtensionAotSmoke, `release_extension_samples.py`) zaten kod olarak
mevcuttu ama hiçbiri commit edilmemişti ve fazın kendi kapanış işlemleri (test
koşumu, doküman senkronu, karar defteri) hiç yapılmamıştı. Bu oturum onu bitirdi.

- **Build kırığı — `ExtensionAotSmoke`:** `Program.cs`'de eksik `using AgentPrism;`
  vardı (`AddAgentPrism`/`IAgentSource`/`AgentPrismToolRegistration` hepsi
  `namespace AgentPrism` içinde) — çözüm derlenmiyordu. Düzeltildi.
- **Örnek derleme testi kırığı:** `IAgentPrismBuilder.cs`'deki yeni
  `AddRunJudge<ResponseQualityJudge>()` XML örneği, `ExamplePrelude.cs`'nin
  `GitAgentSource` deseniyle eşleşen bir stub tipi olmadan derlenmiyordu.
  `ResponseQualityJudge` stub'ı eklendi (Core.UnitTests + Generators.UnitTests'i
  birlikte kırıyordu).
- **🔴 Provider construction normalizer regresyonu:** `ModelProviderRegistry.BuildPipeline`'ın
  construction try/catch'i, `ModelProviderSettings.Validate`'in attığı düz
  `AgentPrismException`'ı (ör. "unrecognized provider setting") foreign SDK
  hatasıyla aynı kefeye koyup generic `upstream_error`'a maskeliyordu — iki
  mevcut fonksiyonel test bunu yakaladı (`MultiProviderTests`). K-619 ile
  düzeltildi: internal, unforgeable `ProviderSettingsValidationException`.
- **BYOK sample bug'ı:** `ContosoModelProvider`'ın credential path'i her çağrıda
  yeni bir `ContosoChatClient` wrapper'ı döndürüyordu (yalnız iç `ContosoBackend`
  credential başına cache'leniyordu) — `ModelProviderCredentialContract.Concurrent_resolution_of_one_credential_stays_stable`
  kırılıyordu. Wrapper artık `(credential, model, shout)` anahtarıyla da
  cache'leniyor.
- **🔴 Bağımsız denetimin (Explore subagent) 3 yanlış alarmı:** Denetçi
  `OpenAIResponsesEndpoints.cs:237`, `OpenAIChatCompletionsEndpoints.cs:163`,
  `CatalogToolCallHandler.cs:86`'nın hâlâ ham `ex.Message` kullandığını statik
  okumayla iddia etti. Secret-like fake mesajla 6 yeni executable test
  (`ProviderOutageErrorHandlingTests`) YEŞİL çıktı: `ProviderFailureNormalizingChatClient`
  boru hattının EN İÇİNDE oturduğu için `ex.Message` bu HTTP handler'lara
  ulaştığında zaten normalize edilmiş oluyor — tek normalizasyon noktası, aşağı
  akan her tüketici otomatik güvenli. Bulgular yanlış pozitifti ama iddia
  gerçek executable proof'la doğrulandı (statik okuma yeterli değildi).
- **🔴 A2A yüzeyi ayrı test edilmedi:** `src/AgentPrism.AspNetCore/A2A/` kendi
  `ex.Message` kullanmıyor — MAF'ın A2A hosting kütüphanesine devrediyor, aynı
  `IChatClient` boru hattını (dolayısıyla aynı normalizasyonu) kullanıyor. Plan
  103.2.3'ün izin verdiği gibi ölçülüp gerekçelendirildi, ayrı test yazılmadı.
- **🔴 `kapi.py yayin` hiç bağlanmamıştı:** `release_extension_samples.py`
  tamamen yazılmıştı ama `kapi.py`'nin `yayin` komutu onu hiç çağırmıyordu —
  script hiçbir zaman koşmamıştı. Bağlandı; bağlarken 3 gerçek hata bulundu ve
  düzeltildi (K-622): yanlış `obj/` yolu (repo'nun merkezi `ArtifactsPath`'i),
  izole cache'te güvenilir çalışmayan `--use-current-runtime`, ve AOT projesi
  için ayrı restore+publish'in ILCompiler native paketini izole cache'e
  eklememesi. `kapi.py yayin --kuru` artık gerçekten uçtan uca yeşil.
- **Docs-site senkronu hiç yapılmamıştı:** `capabilities.md` (`AddRunJudge`
  eksikti — K-509 coverage testini kırıyordu), `reference/versioning.md` (19→20
  paket), `reference/compatibility.md`/`packages.md` (`Testing.Contracts.Xunit`
  açıklaması hâlâ yalnız storage'ı anlatıyordu), `guides/model-providers.md`
  (BYOK hâlâ eski tek-imza), `guides/write-your-own-judge.md` (hâlâ ham
  `AddSingleton<IRunJudge,...>`), `concepts/tools.md` (canonical result iddiası
  runtime'dan güçlüydü) — hepsi bu oturumda güncellendi. `docs-site/public/llms*.txt`
  ve `AgentPrism.AgentMap.md` yeniden üretildi.
- **`getting-started/first-agent.md` — site senkron kuralı gerekçeli geçildi:**
  `dokuman-bakim.py --site-denetle`, `AnthropicModelProvider.cs` değişince bu
  sayfayı da bekliyor; sayfa hiçbir provider imzasına veya BYOK'a referans
  vermiyor (yalnız üst seviye `.UseAnthropic(...)` kaydı gösterir) — gerçek
  drift yok, `--site-gerekce-yazildi` ile geçildi.
- **Bilinen, faz-dışı flaky testler (regresyon DEĞİL):**
  `AgentPrism.Sqlite.IntegrationTests.SqliteDialectTests.Polymorphic_JSON_round_trips_intact`
  (izole koşumda geçti, yalnız tam solution paralel koşumunda bir kez kırıldı)
  ve `AgentPrism.Ui.E2ETests` (her tam koşumda farklı bir Playwright testi flaky
  kırılıyor — frontend bu fazda hiç değişmedi). İkisi de bu fazın kodundan
  bağımsız, tekrar koşumla doğrulandı.

## Bu Fazda Verilen Kararlar

- **K-618** — BYOK iki ayrı public interface'e bölündü (`IModelProvider` /
  `ITenantCredentialModelProvider`); capability yoksa fail-closed.
- **K-619** — Provider construction normalizer'ı AgentPrism'in kendi validation
  hatasını internal, unforgeable bir işaretçi tipiyle "foreign" saymaktan çıkardı.
- **K-620** — `AgentPrismJudgeException` kaldırıldı; `AddRunJudge` üç overload
  ile `IAgentPrismBuilder`'a eklendi.
- **K-621** — `JudgeTimeout` gerçek wait cutoff'tur; late body öldürülmez, geç
  sonuç sessizce atılır.
- **K-622** — `kapi.py yayin`, izole cache'li beş-sample + Native AOT release
  gate'ine bağlandı.

Tam gerekçeler `docs/KARARLAR.md`'de K-618 ilâ K-622 satırlarındadır.

## Denetim Bulguları

Bağımsız denetim `Explore` subagent'ı ile taze bağlamda `git diff` üzerinden
koşuldu (bkz. Plandan Sapmalar).

| Bulgu | Seviye | Sonuç |
|---|---|---|
| `OpenAIResponsesEndpoints.cs:237`, `OpenAIChatCompletionsEndpoints.cs:163`, `CatalogToolCallHandler.cs:86` ham `ex.Message` kullanıyor gibi görünüyor | 🔴 | **Yanlış alarm** — 6 yeni secret-like executable test (`ProviderOutageErrorHandlingTests`) yeşil: `ProviderFailureNormalizingChatClient` boru hattının içinde oturduğu için mesaj bu noktalara ulaşmadan önce zaten normalize edilmiş oluyor. Bulgu statik okumaydı, gerçek davranış farklıydı; iddia executable proof'la kapatıldı |
| A2A yüzeyi ayrı test edilmedi | 🟡 | Gerekçelendirildi — A2A kendi `ex.Message` kullanmıyor, aynı `IChatClient` boru hattını paylaşıyor (plan 103.2.3 bu ölçüm/gerekçe seçeneğine izin veriyor) |
| Tool canonical result, `AddRunJudge`, `JudgeTimeout`, contract test sertleştirmesi, BYOK/provider normalizer | 🟢 | Denetçi tarafından doğru bulundu, ek iş gerekmedi |

🔴 ve 🟡 bulguların hiçbiri kod değişikliği gerektirmedi; ikisi de yukarıdaki
executable proof veya gerekçeyle kapatıldı.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IModelProvider.CreateChatClient(ModelBinding)` yalnız setup credential;
  `ITenantCredentialModelProvider.CreateChatClient(ModelBinding, ModelProviderCredential)`
  yalnız tenant credential. Yeni bir provider yazan biri BYOK istiyorsa ikinci
  interface'i de uygular; istemiyorsa yalnız ilkini uygular ve hiçbir zaman
  tenant credential ile çağrılmaz (fail-closed, K-618).
- `IAgentPrismBuilder.AddRunJudge<T>()` / `(instance)` / `(factory)` —
  `AddAgentSource` ile birebir aynı idempotency semantiği.
- `JudgeTimeout` gerçek wait cutoff'tur; judge implementasyonları token'ı
  onurlandırmasa bile handler zamanında döner (K-621).
- Tool canonical result: `null`/`string`/`JsonElement`/primitive her zaman
  canonicalize edilir; collection/record/class yalnız consumer
  `JsonSerializerContext` ile; unsupported raw CLR obje fail-closed, reflection
  yok.

**Bilinen tuzaklar (🚨):**
- 🚨 Bu repo merkezi `ArtifactsPath` kullanır — bir projenin `obj/`'u kendi
  yanında değil `artifacts/obj/<ProjeAdı>/` altındadır. Script veya araç yazan
  biri `csproj.parent / "obj"` varsayımı yapmamalı.
- 🚨 Native AOT publish izole bir `NUGET_PACKAGES` cache'inde çalışıyorsa: (1)
  somut bir RID gerekir (`--use-current-runtime` izole cache'te güvenilmez,
  `dotnet --info`'dan RID okuyup `-r` ile ver), (2) restore+publish AYNI komutta
  olmalı — ayrı `dotnet restore` sonra `--no-restore` ile publish, ILCompiler'ın
  native paketini izole cache'e eklemez (`PrivateSdkAssemblies` hatası verir).
- 🚨 `AgentEndpoints`/OpenAI-compat/MCP handler'larındaki `ex.Message`
  kullanımları GÜVENLİDİR ama bu güvenilirlik yalnız TEK bir gerçeğe dayanır:
  `ProviderFailureNormalizingChatClient`'ın `ModelProviderRegistry.BuildPipeline`
  içinde HER çağrı yolunun (agent run, OpenAI-compat, MCP, A2A) kullandığı
  `IChatClient`'ı sarmalaması. Bu tek noktayı bypass eden yeni bir model çağrı
  yolu eklenirse (ör. registry'yi atlayan bir kısayol), o yol KENDİ normalizasyonunu
  yapmak zorundadır — aksi hâlde secret sızar.
- 🚨 `AgentPrism.Sqlite.IntegrationTests` ve `AgentPrism.Ui.E2ETests`, tam
  solution'ı `-maxcpucount:1` ile paralel koşarken ara sıra flaky kırılıyor
  (izole koşumda geçiyor). Bu fazın kodundan bağımsız, önceden var olan bir
  test-altyapısı sorunu; `docs/hafiza/test-altyapisi.md`'ye taşınmalı.

**Yarım kalan işler:** Yok — plan kapsamındaki tüm DoD maddeleri kapatıldı.
`F-154`–`F-163` (naming regex standardı, exception taxonomy, disposal ownership
API'si, definition source facade, genel wrapper refactor'ı, NUnit/MSTest
paketleri, storage fluent registration, agents pagination, tenant-aware
load/perf, genel contract load/perf) kapsam dışı bırakıldığı gibi ADAYLAR.md'de
kalır.

**Preview.1 release readiness:** `python3 scripts/kapi.py yayin --kuru --surum
1.0.0-preview.1` uçtan uca yeşil (20 paket, npm dry-run, beş sample, izole-cache
Native AOT smoke). `samples/AgentPrism.Api` gerçek OpenAI provider + `get_order_status`
tool çağrısı + manuel judge endpoint'i ile gerçek bir run üretti ve
`judge:response-quality` skoru (100, "Directly answers the order-status question
and uses the correct tool.") persisted oldu. Blocker kalmadı.
