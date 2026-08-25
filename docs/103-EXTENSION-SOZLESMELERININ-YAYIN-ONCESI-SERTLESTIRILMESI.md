# Faz 103 — Extension Sözleşmelerinin Yayın Öncesi Sertleştirilmesi

> **Durum:** 📋 Planlandı (2026-08-25)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-153**
> **Önkoşul:** [Faz 98](arsiv/fazlar/98-DEPOLAMA-SOZLESMESININ-YAYINI.md) — Storage contract ve packed-package sample deseni · [Faz 99](arsiv/fazlar/99-SAGLAYICI-SOZLESMESININ-YAYINI.md) — provider contract, BYOK ve package graph · [Faz 100](arsiv/fazlar/100-YARGIC-SOZLESMESININ-YAYINI.md) — judge runtime ve contract family · [Faz 101](arsiv/fazlar/101-KAYNAK-SOZLESMESININ-YAYINI.md) — üç overload'lı singleton registration ve sample deseni · [Faz 102](arsiv/fazlar/102-TOOL-SOZLESMESI-VE-SONUC-SINIRI.md) — tool contract, canonical result ve AOT sınırı
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.Mcp`, `.OpenAI`, `.Anthropic`, `.Google`, `.Azure`, `.Testing`, `.Testing.Contracts.Xunit`, `AgentPrism` meta package
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Değişiyor — `IModelProvider` BYOK capability'si ayrılır, `IAgentPrismBuilder.AddRunJudge(...)` eklenir, kullanılmayan `AgentPrismJudgeException` kaldırılır; `PublicAPI.Shipped.txt` dosyalarında K-603 gereği public symbol baseline'ı yoktur, yalnız `#nullable enable` vardır
> **Tüketici yüzeyi:** site: `guides/model-providers.md`, `guides/write-your-own-judge.md`, `guides/write-your-own-agent-source.md`, `guides/write-your-own-tool.md`, `concepts/evaluation.md`, `concepts/tools.md`, `packages.md`, `capabilities.md`, `reference/configuration.md`, `reference/compatibility.md`, `reference/versioning.md`
> · sevk edilen: extension XML'leri, `src/AgentPrism.Testing.Contracts.Xunit/README.md`, package description'ları, beş extension sample'ı
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](manuel-test/01-KURULUM-VE-PAKETLEME.md) · [`02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md) · [`08-OPENAI-UYUMLU-UCLAR.md`](manuel-test/08-OPENAI-UYUMLU-UCLAR.md) · [`17-EVAL-VE-DENEYLER.md`](manuel-test/17-EVAL-VE-DENEYLER.md) · [`18-MCP-VE-A2A.md`](manuel-test/18-MCP-VE-A2A.md) · [`24-TEST-PAKETI-VE-SABLON.md`](manuel-test/24-TEST-PAKETI-VE-SABLON.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.
> Dosyaların tamamını okuma. Yalnız gösterilen bölümleri ve çağrı yollarını oku.

1. Bu doküman.
2. Kararlar — yalnız aşağıdaki kayıtları grep'le:
   ```bash
   grep -n "K-487\|K-509\|K-592\|K-593\|K-594\|K-603\|K-605\|K-609\|K-610\|K-611\|K-613\|K-614\|K-615\|K-616\|K-617" docs/KARARLAR.md
   ```
   - **K-487:** Tool wrapper sırası ve Core/MCP composition sınırı.
   - **K-509:** Public registration coverage bütün giriş noktalarını kapsar.
   - **K-592/K-593:** Tool budget UTF-8 baytla ölçülür ve kesilen sonuç stable JSON zarfıdır.
   - **K-594:** Ham CLR sonuç bugün geçer; bu fazda source-generated context artık bulunduğu için yeniden açılır.
   - **K-603:** Preview hattında `PublicAPI.Shipped.txt` boş kalır; `Unshipped` diff'i yine zorunludur.
   - **K-605:** Contract paketi yalnız `AgentPrism.Abstractions` üretim bağımlılığını alır; Core'a inmez.
   - **K-609:** Provider'ın döndürdüğü `IChatClient`'ı AgentPrism dispose etmez.
   - **K-610/K-611:** Coverage family-scoped'tur; optional davranış ayrı opt-in contract'tır.
   - **K-613:** Judge model çağrısı setup credential yolunu kullanır.
   - **K-614:** `IToolRegistry` ve `AllowUnverifiedToolRegistry` bilinçli public güvenlik yüzeyidir.
   - **K-615/K-616/K-617:** Generated complex result context'i, contract/Core sınırı ve uninspectable result fail-closed davranışı.
3. Faz 98–102 devir notları:
   ```bash
   for n in 98 99 100 101 102; do
     f=$(find docs/arsiv/fazlar -name "$n-*.md" -print -quit)
     awk '/## Sonraki Faza Devir Notu/,0' "$f"
   done
   ```
   Özellikle Faz 100'ün bırakılan `CustomRunJudge.Tests` boşluğunu, Faz 101'in
   stale package cache tuzağını ve Faz 102'nin generated complex-result/AOT
   kararını devral.
4. Alan hafızası:
   - [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) — provider wrapper/fallback sırası.
   - [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) — streaming, cancellation ve `AsyncLocal` tuzakları.
   - [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) — builder/DI lifetime desenleri.
   - [`hafiza/paketleme-ve-dagitim.md`](hafiza/paketleme-ve-dagitim.md) — local feed, package graph ve cache.
   - [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md) — AOT ve PublicAPI kapıları.
   - [`hafiza/dokumantasyon.md`](hafiza/dokumantasyon.md) — site ve sevk edilen doküman kapıları.
5. MAF/MEAI imzasına dokunmadan önce `maf-api-kesfi` skill'ini kullan. Planlama
   ölçümü pinlenmiş sürümde şunları doğruladı: `AIFunction.InvokeAsync(...)`,
   `DelegatingAIFunction.InvokeCoreAsync(...)`, `FunctionResultContent.Result`
   `object` taşır. Paket sürümü değiştiyse ölçümü tekrarla; imza tahmin etme.
6. İlk kod satırından önce `faz-uygulama` skill'ini uygula. Bu faz bir genel
   refactor değildir. Aşağıdaki kapsam dışı tabloyu genişletme.

---

## Amaç

Faz 98–102 third-party extension yüzeylerini ayrı ayrı yayımladı. Packed NuGet
katmanı ölçümde sağlam çıktı. Kalan preview.1 riski paket eksikliği değil; üç
yanlış public/runtime söz, bir güvenlik sızıntısı, zayıf executable proof ve
deterministik olmayan release sample doğrulamasıdır. Bu faz yalnız bu blocker'ları
preview.1 yayınından önce kapatır. Extension ecosystem'i genel olarak yeniden
tasarlamaz.

- **F-153** — Preview.1 sonrasında değiştirilmesi pahalı olacak BYOK, provider
  error, judge timeout/registration, judge public exception ve tool result
  contract'larını düzelt; bunları gerçek contract consumer ve release gate ile
  kanıtla.

### Faz sınırı

| Bu fazda | Bu fazda değil |
|---|---|
| Provider BYOK capability + fail-closed registry yolu | Bütün seam'ler için ortak naming regex/length standardı — **F-154** |
| Provider foreign exception için ortak model-pipeline normalization boundary'si | Tek cross-seam exception taxonomy — **F-155**, provider sınıflandırma alt sorusu ayrıca **F-149** |
| Judge gerçek wait cutoff, registration API, contract consumer ve sample test | Singleton disposal ownership API'sini baştan tasarlamak — **F-156** |
| AOT-safe tool canonical result ve fail-closed unsupported raw result | Definition source compiler/cache facade — **F-157** |
| Provider/judge/source/tool contract test kalite sertleştirmesi | Genel wrapper/helper public surface refactor'ı — **F-158** |
| Exact-version, isolated-cache, packed-sample release gate | Tenant-aware load/perf — **F-159**; genel contract load/perf — **F-163** |
| İlgili XML/site/package drift'i | NUnit/MSTest paketleri — **F-160**; storage fluent registration — **F-161**; agents pagination — **F-162** |

Storage contract gövdeleri güçlü ve packed consumer'ı yeşildir. Yalnız regresyon
kapısı olarak koşulur; simetri için yeniden yazılmaz.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`IModelProvider.cs:131`](../src/AgentPrism.Abstractions/Models/IModelProvider.cs) | Base interface nullable `credential` alır ve tenant key geldiğinde setup key'e düşmeme sözü verir. |
| [`ModelProviderCredentialContract.cs:10`](../src/AgentPrism.Testing.Contracts.Xunit/Contracts/Providers/ModelProviderCredentialContract.cs) | Aynı davranış optional opt-in diye belgelenir; provider parametreyi yok sayıp çalışabilir denir. |
| [`ModelProviderRegistry.cs:160`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs) | Registry tenant credential'ı çözer ve capability negotiation olmadan `CreateChatClient`'a verir. |
| [`ModelProviderRegistry.cs:364`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs) | Provider çağrısı nullable credential ile doğrudan yapılır; unsupported BYOK fail-closed sınırı yoktur. |
| [`FallbackChatClient.cs:282`](../src/AgentPrism.Core/Models/FallbackChatClient.cs) | Fallback exhaustion mesajı `firstFailure.Message` değerini stable AgentPrism exception mesajına kopyalar. |
| [`AgentEndpoints.cs:1135`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | Agent SSE error frame `ex.Message` yayımlar; buffered yollar da aynı metni `ProblemDetails.detail` içine koyar. |
| [`OpenAIResponsesEndpoints.cs:237`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs) | OpenAI Responses buffered/streaming upstream error gövdeleri raw exception mesajını kullanır. |
| [`OpenAIChatCompletionsEndpoints.cs:163`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIChatCompletionsEndpoints.cs) | Chat Completions buffered/streaming yolları raw mesajı `upstream_error` olarak yayımlar. |
| [`CatalogToolCallHandler.cs:86`](../src/AgentPrism.AspNetCore/McpServer/CatalogToolCallHandler.cs) | MCP agent tool sonucu raw `ex.Message` içerir. |
| [`RunRecordingAgent.cs:1293`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Foreign exception mesajı `RunError.Message` içine yazılır; sonradan run API üzerinden de görülebilir. |
| [`OnlineEvalJobHandler.cs:166`](../src/AgentPrism.Core/Evaluation/OnlineEvalJobHandler.cs) | `CancelAfter` linked token'ı iptal eder, fakat `JudgeAsync` doğrudan await edilir; token'ı yok sayan judge handler'ı tutar. |
| [`RunJudgeContract.cs:40`](../src/AgentPrism.Testing.Contracts.Xunit/Contracts/Judges/RunJudgeContract.cs) | `Judge.Name.ShouldBe(Judge.Name)` test tiyatrosudur. Concurrency yalnız `Task.WhenAll`; cancellation token'ı yok saymak kabul edilir. |
| [`AgentSourceContract.cs:34`](../src/AgentPrism.Testing.Contracts.Xunit/Contracts/AgentSources/AgentSourceContract.cs) | `Priority.ShouldBe(Priority)` test tiyatrosudur; cancellation testi beş saniyede hang olmamayı contract sayar. |
| [`ModelProviderContract.cs:168`](../src/AgentPrism.Testing.Contracts.Xunit/Contracts/Providers/ModelProviderContract.cs) | Provider concurrency testi başlangıç/overlap gate'i kurmadan yalnız 32 `Task.Run` sonucunu sayar. |
| [`CustomToolContract.cs:49`](../src/AgentPrism.Testing.Contracts.Xunit/Contracts/Tools/CustomToolContract.cs) | Tool concurrency testi declaration-only tool'u test etmeden döner ve gerçek overlap'ı kendisi kurmaz. |
| [`TruncatingAIFunction.cs:80`](../src/AgentPrism.Core/Tools/TruncatingAIFunction.cs) | `ToolResultText` normalize edemediği raw CLR sonucu budget uygulamadan aynen geçirir. |
| [`TruncatingAIFunctionTests.cs:115`](../tests/AgentPrism.Core.UnitTests/Tools/TruncatingAIFunctionTests.cs) | Mevcut test bu pass-through davranışını bilinçli olarak yeşile kilitler. |
| [`concepts/tools.md:131`](../docs-site/src/content/docs/concepts/tools.md) | Site, her tool sonucunun tek canonical text formuna dönüştüğünü söyler; runtime bu kadar güçlü değildir. |
| [`AgentPrismJudgeException.cs:4`](../src/AgentPrism.Abstractions/Exceptions/AgentPrismJudgeException.cs) | Public exception ve dört public üye vardır; repo genelinde sıfır constructor çağrısı ölçüldü. Runtime yalnız sabitleri kullanır. |
| [`AgentPrismOnlineEvaluationBuilderExtensions.cs:51`](../src/AgentPrism.Core/Evaluation/AgentPrismOnlineEvaluationBuilderExtensions.cs) | Yalnız built-in `AddModelRunJudge` vardır. Custom judge guide ham `IServiceCollection.AddSingleton` kullanmak zorundadır. |
| [`AgentPrism.Samples.CustomRunJudge.csproj:8`](../samples/AgentPrism.Samples.CustomRunJudge/AgentPrism.Samples.CustomRunJudge.csproj) | Judge sample yalnız source project'tir; test project, contract consumer ve real score persistence kanıtı yoktur. |
| [`AgentPrism.Samples.CustomModelProvider.csproj:21`](../samples/AgentPrism.Samples.CustomModelProvider/AgentPrism.Samples.CustomModelProvider.csproj) | Extension sample'ları `VersionOverride="*-*"` kullanır; feed ve global cache birden fazla preview içerirse seçim deterministik değildir. |
| [`kapi.py:389`](../scripts/kapi.py) | `yayin` stale package output'u temizleyip exact pack version üretir, fakat beş extension sample'ını isolated package cache ile build/test etmez. |
| [`AgentPrism.Testing.Contracts.Xunit.csproj:4`](../src/AgentPrism.Testing.Contracts.Xunit/AgentPrism.Testing.Contracts.Xunit.csproj) | Package description yalnız 33 storage contract'ını anlatır. |
| [`versioning.md:9`](../docs-site/src/content/docs/reference/versioning.md) | Site “19 packages” der; güncel pack çıktısı ve compatibility tablosu 20 package'tır. |
| [`PublicAPI.Unshipped.txt:2`](../src/AgentPrism.Abstractions/PublicAPI.Unshipped.txt) | `AgentPrismJudgeException` bugün hâlâ ücretsiz kaldırılabilir yüzeydedir; `Shipped` baseline yalnız `#nullable enable` taşır. |

> Kanıtlar 2026-08-25 tarihinde kaynak, test gövdesi ve packed-artifact
> probuyla yeniden doğrulandı.

### Sağlam kalan baseline — yeniden icat edilmez

| Ölçüm | Sonuç |
|---|---|
| `dotnet pack` | 20 `.nupkg`, ortak exact sürüm |
| Repo dışı packed consumer | `IRunStore`, `IModelProvider`, `IRunJudge`, `IAgentSource`, generated custom tool build/run başarılı |
| Source tree / nupkg farkı | Eksik dependency veya public API bulunmadı |
| Native AOT | Provider, source, generated string ve generated complex tool çalıştı |
| Contract package graph | `AgentPrism.Testing.Contracts.Xunit` → `AgentPrism.Abstractions`; `AgentPrism.Core` yok |
| Contract family sayısı | Storage: 32 class · Provider: 3 class/17 Fact · Judge: 1/6 · AgentSource: 3/15 · Tool: 2/4; `Skip` yok |
| Gerçek consumer | Storage: built-in'ler + sample · Provider: sample · Judge: **yok** · AgentSource: built-in + sample · Tool: Core + sample |

---

## 103.1 — BYOK Optional Capability, Runtime Fail-Closed

BYOK bütün provider'lar için mandatory değildir. Capability optional'dır. Fakat
tenant credential varsa desteklemeyen provider'a setup/global credential ile
çağrı yapmak yasaktır.

### 103.1.1 Public contract'ı iki interface'e ayır

`IModelProvider.CreateChatClient(ModelBinding, ModelProviderCredential?)` imzasını
iki açık yola ayır:

- `IModelProvider.CreateChatClient(ModelBinding)` yalnız setup-time credential
  yoludur.
- Yeni `ITenantCredentialModelProvider : IModelProvider`, non-null
  `ModelProviderCredential` alan ikinci metodu taşır.
- Empty marker ekleme. Nullable parametreyi base interface'te bırakma. Aksi hâlde
  XML ile capability yine çelişir.
- Dört built-in provider, `FakeModelProvider` ve BYOK destekleyen CustomModelProvider
  sample'ı yeni interface'i uygular.
- BYOK sunmayan third-party provider yalnız `IModelProvider` uygular ve setup
  credential ile normal çalışır.

`ModelProviderCredentialContract`, `Provider` değerinin
`ITenantCredentialModelProvider` olduğunu açıkça ister. Optional davranış ayrı
opt-in class olarak kalır; K-611 korunur.

### 103.1.2 Registry branch'i provider çağrısından önce kapat

Registry invariant'ı:

```text
tenant credential yok
  → IModelProvider.CreateChatClient(binding)

tenant credential var + capability var
  → ITenantCredentialModelProvider.CreateChatClient(binding, credential)

tenant credential var + capability yok
  → provider çağrılmaz
  → setup credential kullanılmaz
  → stable provider_credential_unsupported
```

- Fail-closed kontrolü provider invocation'dan önce yapılır.
- Credential değeri exception, log property, metric veya diagnostic payload'a
  yazılmaz.
- BYOK compile-cache bypass ve tenant-scoped circuit key korunur. Unsupported
  capability cache'e setup client koyamaz.
- Fallback link'i de aynı tenant credential/capability yolundan geçer. Primary
  unsupported olduğunda fallback'e geçerek başka setup credential kullanmak
  configuration hatasını gizleyemez.
- `CreateSetupChatClientAsync` K-613 gereği yalnız setup path'ini çağırır. Judge
  kontrol düzlemi tenant BYOK anahtarını tüketmez.
- Yeni public exception tipi ekleme. Internal normalized exception stable
  `ErrorType = "provider_credential_unsupported"` ve secret taşımayan generic
  text üretir.

### 103.1.3 Executable proof

- Unit: unsupported capability durumunda provider invocation sayısı `0`, setup
  client sayısı `0`, stable error code doğru.
- Unit: capability provider'a exact tenant credential gider; iki tenantın key'i
  karışmaz; setup yoluna düşülmez.
- Functional: gerçek compile/run tenant binding ile unsupported provider'da
  public response secret içermeden fail-closed olur.
- Contract: BYOK opt-in class yeni interface'i şart koşar ve recording transport
  exact credential'ın uygulandığını kanıtlar.
- Mutation proof: capability check geçici kaldırıldığında test, setup client'ın
  çağrıldığını görerek kırmızı olmalıdır.

---

## 103.2 — Provider Foreign Exception Normalization Boundary

Tek tek HTTP endpoint maskesi yazma. Provider/SDK exception'ı public yüzeye
çıkmadan önce bütün model çağrı yollarını kapsayan ortak boundary kur.

### 103.2.1 Boundary yerleşimi

İki failure anı ayrı ele alınır:

1. **Client construction:** `ModelProviderRegistry` provider'ın
   `CreateChatClient` çağrısını ortak helper içinde yapar. Foreign synchronous
   exception burada normalize edilir.
2. **Model invocation/streaming:** Normalizer, fallback/circuit/content-filter
   zincirinin dış halkasıdır. İç halkalar raw exception graph'ını önce görür;
   retry/fallback classification bozulmaz. Dış halka son failure'ı public-safe
   exception'a çevirir.

Fallback chain bugün first failure mesajını kendi mesajına kopyalar. Chain
exhaustion mesajı yalnız denenen provider adlarını ve generic failure metnini
taşır; `firstFailure` inner exception olarak korunur, message içine kopyalanmaz.

### 103.2.2 Normalization policy

| Girdi | Runtime sonucu | Public sonuç | Log |
|---|---|---|---|
| Caller/host `OperationCanceledException` | Aynı cancellation semantiği | Cancellation yüzeyinin mevcut contract'ı | Gerekli mevcut seviye; error diye normalize edilmez |
| AgentPrism'in bilinen güvenli exception alt tipi | Tip, stable code ve güvenli alanlar korunur | Mevcut güvenli contract | Inner graph korunur |
| Foreign provider/SDK exception | Internal provider invocation exception, raw exception inner | `upstream_error` + stable generic text | Raw exception object, provider/model/run context ile tam |
| Foreign `AgentPrismException` veya bilinmeyen alt tip | Adına güvenilmez; allowlist dışında foreign sayılır | Generic `upstream_error` | Raw exception korunur |
| Fallback chain exhausted | `provider_unavailable`, raw first failure inner | Generic chain failure; raw first message yok | Full chain ve inner exception |

“`AgentPrismException` ise olduğu gibi geçir” şeklinde geniş bir catch filtresi
kullanma. Tip public ve türetilebilir. Yalnız AgentPrism'in güvenli text contract'ı
olan bilinen tipleri preserve et.

### 103.2.3 Public surface coverage

Secret-like fake mesaj kullan:

```text
https://tenant-private.example; Authorization=Bearer sk-secret-preview1; account=acct-42
```

Aşağıdaki yüzeylerin hiçbirinde bu metin, exception CLR type name'i, endpoint,
header veya account id görünmez:

- Agent run SSE error frame.
- Agent buffered HTTP `ProblemDetails`.
- Persisted `RunError` ve run detail API.
- OpenAI Responses buffered ve streaming.
- OpenAI Chat Completions buffered ve streaming.
- MCP server agent-tool sonucu.
- A2A adapter aynı model pipeline'ını kullanıyorsa A2A error payload; kullanmıyorsa
  test bunu ölçüp neden kapsam dışı kaldığını faz sapmasına yazar.

Log capture aynı unique secret-like metnin raw exception ile bulunduğunu
kanıtlar. Yalnız string log aramak yetmez; `Exception` alanının original/inner
graph'ı taşıdığı da doğrulanır. Bir failure için katman katman duplicate error log
üretme.

---

## 103.3 — JudgeTimeout Gerçek Wait Cutoff

`JudgeTimeout` adı korunur ve gerçek wait cutoff olur. “Cooperative cancellation
budget” olarak yeniden adlandırılmaz.

### 103.3.1 İki ayrı kontrol

- Judge body'ye caller token ile timeout token'ının linked token'ı verilir. Bu,
  cooperative implementasyonun çalışmayı bırakmasını ister.
- Handler aynı zamanda await'i timeout süresinde keser. Judge token'ı yok saysa
  bile `JudgeOneAsync` timeout civarında `judge_timeout` failure'ı üretip döner.
- Timeout body'yi öldürmez. Late body arkada tamamlanabilir. Bu hem XML'de hem
  guide'da açıkça yazılır.

`TimeoutAIFunction`'ın late-task gözlem deseni ölçülür ve uygunsa tekrar kullanılır;
genel timeout framework'ü çıkarılmaz.

### 103.3.2 Race ve job semantics

- Caller/host token iptal edilmişse timeout değil `OperationCanceledException`
  kazanır. Yakın yarış için host cancellation önceliği deterministic test edilir.
- Timeout `judge_timeout`, ordinary foreign failure `judge_failed`, out-of-range
  skor `judge_contract` kalır.
- Timeout failure mevcut job politikasında retryable kalır. `ExecuteAsync` tek
  `JobRetryException` üretir ve backoff'u korur.
- Score persistence yalnız zamanında tamamlanan judgment'tan sonra olur. Late
  completion score yazamaz ve summary/metric üretemez.
- Job lease handler tamamlanana kadar yenilenir; gerçek cutoff sonrası handler
  lease'i bırakabilir. Late judge body lease sahibi değildir.
- Retry geç başlamış late body ile overlap edebilir. `IRunJudge` XML'i
  singleton/concurrency/idempotent side-effect sorumluluğunu açıkça söyler.
  F-152'deki per-judge durable checkpoint bu faza alınmaz.
- Late success sessizce tüketilir. Late fault gözlemlenir ve uygun debug/warning
  seviyesinde loglanır; `TaskScheduler.UnobservedTaskException` üretmez.

### 103.3.3 Deterministic timeout testi

Token'ı bilerek yok sayan gated fake judge kullan:

1. Body başladığını `TaskCompletionSource` ile bildirir.
2. `JudgeTimeout` kısa ve test sınırı kontrollüdür.
3. Handler body gate'i açılmadan timeout sonucu döner.
4. Failure exact `judge_timeout` ve retryable'dır.
5. Body sonra success ve ayrı vakada fault ile tamamlanır.
6. Score store çağrılmaz; late fault gözlemlenir; unobserved event yoktur.
7. Ayrı test host token'ını iptal eder ve timeout failure yerine OCE görür.

Uzun `Task.Delay(5s)` kullanma. Gate, fake `TimeProvider` veya kısa bounded wait
ile wall-clock flaky test üretme.

---

## 103.4 — Custom Judge Registration ve Public Exception Temizliği

### 103.4.1 `IAgentPrismBuilder.AddRunJudge` yüzeyi

AgentSource deseniyle aynı üç overload doğrudan `IAgentPrismBuilder` üzerinde
olur:

- Generic overload `TryAddEnumerable(ServiceDescriptor.Singleton<IRunJudge,
  TJudge>())` kullanır. Aynı implementation type iki kez eklenirse idempotent'tır.
- Instance overload `AddSingleton<IRunJudge>(judge)` kullanır. Aynı CLR type'tan
  farklı configured instance'lara izin verir.
- Factory overload `AddSingleton<IRunJudge>(factory)` kullanır. Aynı CLR type'tan
  farklı configured üretimlere izin verir.
- Üçü de singleton'dır. Factory'nin resolved scoped dependency yakalaması
  yasaktır. Instance ownership ile container-created generic/factory ownership
  farkı XML ve guide'da yazılır; F-156 genel disposal modeli bu faza alınmaz.
- Duplicate `Name`, CLR type değil runtime identity'dir. `RunJudgeSet` startup'ta
  `OrdinalIgnoreCase` ile duplicate adı reddetmeye devam eder.
- `AddModelRunJudge` public extension method olarak kalır. Kendi options
  configuration'ını yapar ve aynı generic registration primitive'ini kullanır;
  duplicate `ModelRunJudge` registration üretmez.
- Custom judge guide'ın ana yolu artık ham `services.AddSingleton<IRunJudge,...>`
  değildir.

K-509 gereği capability/registration coverage testi üç yeni entry point'i de
görür. Public API analyzer için generic parameter AOT annotation'ı
`AddAgentSource<TSource>` ile aynı olur.

### 103.4.2 `AgentPrismJudgeException` kaldır

Repo genelinde sıfır constructor call ölçüldü. Public tipi “belki sonra” diye
bırakma:

- `AgentPrismJudgeException` ve Unshipped kayıtları kaldırılır.
- `judge_failed`, `judge_timeout`, `judge_contract` runtime'ın internal stable
  constants alanına taşınır.
- `JudgeFailure.ErrorType` public stable code taşımaya devam eder. Bunun için
  yeni public exception hierarchy eklenmez.
- XML, site, OpenAPI/schema ve classifier referansları eski CLR type'a bağlıysa
  code'a bağlanır.

---

## 103.5 — Judge Contract Family'yi Gerçekten Executable Yap

### 103.5.1 Built-in consumer

`tests/AgentPrism.Core.UnitTests` içinde en az bir gerçek built-in judge
`RunJudgeContract` türetir. Tercih `ModelRunJudge`'dır; network yerine fake
`IModelProviderRegistry`/chat client kullanılır. Built-in consumer şu iki şeyi
birlikte kanıtlar:

- Contract family değiştiğinde AgentPrism'in kendi implementation'ı kırmızıya
  düşer.
- `ContractCoverage.JudgeContracts` yeni family class'ı geldiğinde consumer'ı
  uyarır.

### 103.5.2 CustomRunJudge package-only test project

Yeni `samples/AgentPrism.Samples.CustomRunJudge.Tests`:

- Sample source'a `ProjectReference` kullanabilir. Hiçbir `src/AgentPrism.*`
  project'ine `ProjectReference` kullanamaz.
- AgentPrism üretim/test bağımlılıklarının tamamı local feed'deki exact-version
  `PackageReference` olur.
- `ResponseQualityJudgeContractTests : RunJudgeContract` içerir.
- `ContractCoverage.MissingDerivedTypes(..., JudgeContracts)` boş döner.
- Üç `AddRunJudge` registration overload'ını DI sınırında test eder; ana sample
  generic overload'ı kullanır.
- `AgentPrismTestHost` ile gerçek run üretir, manual/online evaluation yolunu
  çalıştırır ve `IRunScoreStore` üzerinden `judge:response-quality` score satırını,
  value/comment/tenant/run kimliğini doğrular.
- Bu test salt `JudgeAsync` method çağrısı değildir; run input, run events,
  evaluation handler ve score persistence sınırlarını geçer.

### 103.5.3 Contract gövdesi sertleştirmesi

`RunJudgeContract` içindeki şu zayıf iddiaları kaldır:

- `Name.ShouldBe(Name)` yoktur. Aynı instance üzerinde iki okuma ve iki ayrı
  çağrı arasında stable identity gözlenir; gerçek regex/length doğrulaması kalır.
- Concurrency caller'ları `Barrier`/start gate arkasında hazırlar. Aynı singleton
  instance'a aynı anda giren gated fixture ile gerçek overlap kanıtlanır.
- Pre-cancelled token'ı yok saymak başarı değildir. Contract başlatılmadan iptal
  edilmiş token için OCE ister.
- In-flight cancellation yalnız gerçekten await eden fixture üzerinde gate ile
  ölçülür. Anında tamamlanan deterministic implementation'a yapay “in-flight”
  iddiası yazılmaz; onun pre-cancel contract'ı yine zorunludur.
- Timeout, `IRunJudge` implementer contract'ına konmaz. Gerçek wait cutoff
  `OnlineEvalJobHandler` functional/unit sınırında kanıtlanır.
- Contract değişikliğinin gücü mutation/red→green ile kaydedilir: cancellation'ı
  yok sayan ve mutable field ile concurrent çağrıyı bozan kontrollü fake eski
  suite'te yeşil, yeni suite'te kırmızı olmalıdır.

---

## 103.6 — Tool Canonical Result ve Truncation Contract

Runtime garantisi docs seviyesine çıkarılır. Docs yalnız daraltılarak kusur
örtülmez.

### 103.6.1 Tek internal canonicalizer

`ToolResultText` veya onun yerini alan tek internal helper şu tüketicilerin ortak
kaynağıdır:

- Content guard inspection ve masking.
- `MaxOutputBytes` UTF-8 ölçümü.
- Truncation envelope içeriği.
- Run/tool invocation persistence için kullanılan text projection varsa o yol.

İki ayrı serialization/normalization algoritması oluşamaz. Guard'ın gördüğü bayt
dizisi ile truncation'ın ölçtüğü canonical representation aynı olmalıdır.

### 103.6.2 Result matrisi ve AOT sınırı

| Sonuç | Canonical davranış |
|---|---|
| `null` | Açık, stable JSON/text representation; guard ve budget aynı değeri görür |
| `string` | Mevcut wire semantics ölçülür; tek canonical text'e çevrilir |
| `JsonElement` | `GetRawText()` tabanlı canonical JSON |
| Primitive/enum/Guid/date | Culture-independent canonical JSON/text |
| Collection ve record/class, source-generated `JsonSerializerContext` ile | Consumer context'i ile `JsonElement`/canonical JSON |
| Generated complex tool | K-615/APG0008 yolu; context zorunlu ve AOT-safe |
| `AIContent` | Protocol-special result; inline canonical result guarantee'sinin açık istisnası |
| Direct custom `AIFunction` raw CLR object, type info yok | **Fail-closed**; pass-through yok, reflection serializer yok |

- `JsonSerializer.Serialize(object)` veya reflection-based type discovery ekleme.
- `AgentPrism.Core` AOT flag'i düşürülemez.
- Unsupported raw result generic, secret taşımayan bir tool failure/masked result
  üretir. Raw object `ToString()` ile modele, run record'a veya log text'e yazılmaz.
- Content guard açık veya kapalı olsa da output budget bypass edilemez. Guard'ın
  fail-closed K-617 davranışı ve budget birbirine bağımlı olmaz.
- K-592/K-593 korunur. K-594 kapanışta yeni ölçülen davranışla bilinçli olarak
  yeniden yazılır.
- Canonicalizer internal kalır. Bu iş için yeni public result/helper/type eklenmez.

### 103.6.3 Gerçek registration yolları

İzole `AIFunction` probe'u yeterli değildir. Test matrisi şu gerçek yolları ayrı
koşar:

- `AddTool(AIFunction)` direct custom function.
- `AddTool(Delegate)` / `AIFunctionFactory` yolu.
- `[AgentPrismTool]` generated string/primitive tool.
- Generated complex result + consumer `JsonSerializerContext`.
- Collection, record/class, `null`, unsupported raw CLR object.
- Guard açık/kapalı ve budget altı/üstü kombinasyonları.
- MCP registry aynı `TruncatingAIFunction` yolunu kullanıyorsa parity; ayrı
  normalization kopyası oluşturulmaz.

Her test guard'a verilen exact text ile `Encoding.UTF8.GetByteCount` için kullanılan
exact text'i karşılaştırır. “İkisi de başarılı oldu” eşitlik kanıtı değildir.

---

## 103.7 — Provider, Judge, AgentSource ve Tool Contract Test Kalitesi

Storage family yalnız regression olarak koşulur. Dört küçük family'nin her
public testi için test adı ile gövde eşleştirilir.

### 103.7.1 Kalite kuralları

- `x.ShouldBe(x)`, no-op assertion ve yalnız instance üretme testi silinir.
- Sırf `Task.WhenAll` kullanmak concurrency kanıtı değildir. Caller start gate,
  gated implementation ve maximum-overlap sayacı kullanılır.
- Pre-cancelled token'ı ignore etmek başarı değildir. Contract token'ı honor etme
  sözü veriyorsa OCE gerekir.
- In-flight cancellation `TaskCompletionSource` ile “body başladı” sinyali
  alındıktan sonra tetiklenir. Beş saniye hang olmamak contract değildir.
- Dışarıdan gözlenemeyen implementation detail test edilmez. Örneğin scoped
  dependency capture contract suite'te introspection ile aranmaz; DI functional
  testinde gerçek scope davranışı ölçülür.
- Sync `IModelProvider.CreateChatClient` için uydurma cancellation testi eklenmez.
  Provider cancellation, dönen `IChatClient` runtime functional testinde kalır.
- Tool body cancellation token almıyorsa `CustomToolContract` ona olmayan bir
  contract yüklemez. Pipeline timeout/cancellation K-616 gereği Core functional
  testinde kalır.
- Optional behavior gerekiyorsa yeni opt-in class olur; `Skip`, koşullu return veya
  “ikisi de kabul” assertion'ı olmaz.

### 103.7.2 Family bazında hedef

| Family | Mevcut sayı | Sertleştirme |
|---|---:|---|
| Providers | 3 class / 17 Fact | Start gate + cache/credential identity assertions; BYOK interface opt-in; raw pipeline/cancellation Core testinde |
| Judges | 1 / 6 | Self-equality silinir; deterministic concurrency; pre-cancel zorunlu; gated in-flight proof; built-in + sample consumer |
| AgentSources | 3 / 15 | Priority self-equality silinir; list/resolve start gate; pre-cancel zorunlu; gated in-flight source probe; built-in + sample consumers korunur |
| Tools | 2 / 4 | Declaration-only silent return kaldırılır veya explicit declaration contract'a ayrılır; invocable path gerçek overlap ve semantic result doğrular |
| Storage | 32 class | Gövde değişmez; dört built-in provider ve FileRunStore sample yeşil kalır |

### 103.7.3 Coverage family invariant'ı

- Namespace sabitleri `Storage`, `Providers`, `Judges`, `AgentSources`, `Tools`
  olarak kalır.
- Yeni family eklenmesi diğer family coverage testlerini kıramaz; K-610 testi
  korunur.
- `Skip` sayısı `0` kalır.
- `Shouldly` ve xunit implementation tipleri yalnız test contract package'ının
  bilinçli public fixture yüzeyinde kalır; üretim `Abstractions`/`Core` yüzeyine
  sızmaz.
- Package'ın resolved graph'ında `AgentPrism.Core` bulunmaz.

---

## 103.8 — Deterministic Packed-Sample Release Gate

Yeni package formatı veya dependency graph tasarlama. Var olan `kapi.py yayin`
akışına gerçek extension consumer doğrulaması ekle.

### 103.8.1 Gate sahipliği

Canonical entry point `python3 scripts/kapi.py yayin --kuru` olarak kalır. Gate
zaten stale `artifacts/package/release` içeriğini temizler, 20 paketi üretir ve
exact ortak sürümü çözer. Sample orchestration ayrı, odaklı bir script/helper
olabilir; fakat `yayin` onu resolved version sonrasında çağırır. CI
`release-dryrun` aynı entry point'i kullandığı için otomatik kazanır.

Heavy pack/sample/AOT işlemini normal unit test içine gömme. `ReleaseArtifactTests`
package content'i test etmeye devam eder; release consumer gate'i release akışının
sorumluluğudur.

### 103.8.2 İzolasyon contract'ı

Her gate koşumu:

1. Bu koşumdan önce release feed'i temizler.
2. Tek exact version üretir ve bütün sample `PackageReference` override'larına
   `/p:AgentPrismSamplePackageVersion=$PAKET_SURUMU` veya eşdeğer tek property ile verir.
3. Yeni `mktemp -d` package cache oluşturur ve `NUGET_PACKAGES`'ı oraya ayarlar.
4. NuGet source olarak yalnız bu koşumun local feed'ini ve gerekli third-party
   paketler için nuget.org'u kullanır. AgentPrism için source mapping local feed'i
   zorlar.
5. Global `~/.nuget/packages` içeriğine dokunmaz ve ondan çözüm yapmaz.
6. Sample csproj'lerinde literal `VersionOverride="*-*"` release yolu kalmaz.
   Normal developer convenience default'u tutulursa release gate exact property
   vermediğinde test bunu kırmızı yapar.
7. `src/AgentPrism.*` project'lerine `ProjectReference` bulunmadığını grep ve
   restore graph ile doğrular. Test-project → sample-source ProjectReference
   meşrudur; AgentPrism üretim kodu yalnız nupkg'den gelir.

### 103.8.3 Beş consumer ve AOT

Exact packed artifact üzerinden:

| Consumer | Zorunlu doğrulama |
|---|---|
| `FileRunStore` | Storage contract + gerçek store davranışı |
| `CustomModelProvider` | Provider contract, BYOK opt-in, registration, gerçek run |
| `CustomRunJudge` | Judge contract, registration, gerçek run/evaluation, persisted score |
| `CustomAgentSource` | Source contract, registration, gerçek agent run |
| `CustomTool` | Tool contract, generated complex result, gerçek tool run |

Sonra provider, source ve generated complex tool consumer Native AOT publish/run
smoke testleri aynı exact feed ve isolated cache ile tekrar edilir. Bu gate
`AgentPrism.Testing.Contracts.Xunit` graph'ında Core olmadığını ve meta package
graph'ında testing package olmadığını `project.assets.json`/`dotnet list package
--include-transitive` üzerinden doğrular.

Script testleri en az şunları mutation ile kanıtlar: wildcard version, stale
feed package, global cache path, AgentPrism ProjectReference ve eksik sample
liste öğesi gate'i kırar.

---

## 103.9 — XML, Site, Package ve Sample Drift'ini Runtime Sonrasında Kapat

Doküman önce hedef davranışı yazıp kırmızı runtime'ı gizleyemez. Sıra:

1. Runtime + tests yeşil.
2. Public XML ve shipped README güncel.
3. Docs site güncel.
4. Site/content/link/package gates.

Zorunlu senkronizasyon:

- `IModelProvider` XML ve provider guide optional capability'yi, iki interface'i
  ve unsupported tenant credential fail-closed sonucunu aynı dille anlatır.
- Provider guide sample yeni registration ve interface'i kullanır. Setup client
  disposal ownership K-609 ile tutarlıdır.
- Judge guide `AddRunJudge` ana yolunu kullanır. Singleton, factory/instance
  ownership, concurrency, idempotent retry, true wait cutoff ve “body öldürülmez”
  ayrımını açık yazar.
- `JudgeTimeout` configuration tablosu gerçek wait cutoff der; cooperative hard
  cancellation iddiası vermez.
- Tool guide/concept/capabilities canonical matrix'i runtime ile birebir anlatır;
  `AIContent` ve unsupported raw direct function sınırı görünürdür.
- `Testing.Contracts.Xunit` README beş family'yi — Storage, Providers, Judges,
  AgentSources, Tools — örnek ve coverage sabitleriyle anlatır.
- Package description storage-only metinden beş-family metnine döner.
- `packages.md` Tool family'yi; `compatibility.md` bütün family'leri ve
  ContractCoverage reflection helper'ının trimming/AOT sınırını anlatır.
  Contract test methodlarının varlığı “paketin tamamı AOT altında çalışmaz” diye
  genellenmez.
- `versioning.md` package count `20` olur ve pack gate ile otomatik doğrulanır.
- Provider/source/tool metric-name XML ve guide'ları stable low-cardinality name
  beklentisini söyler. Bu faz ortak regex standardı getirmez.
- Instance/factory registration disposal ownership yalnız gerçekten farklı olan
  yerlerde açıklanır. Bütün seam'ler için yeni ownership API'si yazılmaz.
- Sample source ile guide aynı `AddRunJudge`, `AddModelProvider`, `AddAgentSource`
  ve `AddTool` API'sini kullanır.

`tuketici-dokuman-senkronu`, runtime donduktan sonra `faz-tamamlama` içinden
koşar. `docs-site` İngilizce kalır; geliştirme kaydı bu dokümanda Türkçe kalır.

---

## 103.10 — Public API Freeze Öncesi Dar Audit

Bu audit yalnız fazın dokunduğu veya kullanıcı tarafından özel olarak işaretlenen
yüzeyleri sorgular. Genel public surface temizliği yapmaz.

| Yüzey | Ölçülen doğrudan tüketici | Planlanan sonuç |
|---|---|---|
| `AgentPrismJudgeException` | Constructor call `0` | Kaldır; internal error constants kullan |
| `IAgentPrismBuilder.AddRunJudge` | Custom judge için ergonomik yol yok | Üç singleton overload ekle |
| `ITenantCredentialModelProvider` | Registry + dört built-in + BYOK sample | Yeni optional capability interface ekle |
| Tool canonicalizer | Core guard/truncation/persistence | Internal kalır; public helper ekleme |
| `AuthorizingAIFunction` | Core ve MCP package-boundary composition | K-487 kullanımı tekrar doğrulanır; blocker işi gerektirmiyorsa korunur, genel refactor F-158 |
| `TimeoutAIFunction` | Core ve MCP wrapper zinciri | Aynı ölçüm; publicliği sebepsiz değiştirme |
| `TruncatingAIFunction` | Core ve MCP wrapper zinciri | Davranış düzelir; public shape ancak gerçek cross-package kullanım gerektiriyorsa kalır |
| `AgentPrismGeneratedToolArguments` | Generator consumer assembly'sinde emit edilen kod çağırır | Public kalır; cross-assembly compile testi korur |
| `AgentPrismToolOptions.AllowUnverifiedToolRegistry` | K-614 explicit escape hatch | Public kalır; startup güvenlik contract'ı korunur |

- `PublicAPI.Unshipped.txt` diff'i paket bazında incelenir.
- `PublicAPI.Shipped.txt` K-603 gereği preview.1 için public symbol ile
  doldurulmaz; yalnız `#nullable enable` kalır. Bu, audit yapılmadığı anlamına
  gelmez.
- Yeni public type/overload yalnız Planlanan Public API tablosunda varsa kabul
  edilir. Implementation helper yanlışlıkla public olursa gate kırmızı olmalıdır.
- `Shouldly`/xunit tipleri üretim package public API'sinde görünemez.

---

## Planlanan Public API

> Taslak imzalardır. Uygulama sırasında analyzer ve gerçek overload convention'ı
> ile doğrulanır. Gerçekleşen imzalar kapanışta ayrı bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public interface IModelProvider
{
    string Name { get; }
    IReadOnlyList<ModelDescriptor> Models { get; }
    IChatClient CreateChatClient(ModelBinding binding);
}

public interface ITenantCredentialModelProvider : IModelProvider
{
    IChatClient CreateChatClient(
        ModelBinding binding,
        ModelProviderCredential credential);
}

// AgentPrism.Core
public interface IAgentPrismBuilder
{
    IAgentPrismBuilder AddRunJudge<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TJudge>()
        where TJudge : class, IRunJudge;

    IAgentPrismBuilder AddRunJudge(IRunJudge judge);

    IAgentPrismBuilder AddRunJudge(
        Func<IServiceProvider, IRunJudge> factory);
}
```

Kaldırılacak public yüzey:

```csharp
// AgentPrism.Abstractions — remove before preview.1
public sealed class AgentPrismJudgeException : AgentPrismException;
```

Yeni public provider error exception, tool result type, canonicalizer, timeout
helper veya registration helper planlanmaz.

### HTTP `endpoint`'leri

Yeni endpoint yoktur. Mevcut yüzeylerin response contract'ı sertleşir:

| Metot | Yol | Değişmeyen rol | Sertleşen davranış |
|---|---|---|---|
| `POST` | `/agentprism/api/agents/{name}/run` | Mevcut run rolü | Buffered ve SSE provider failure generic stable text/code taşır |
| `POST` | `/openai/v1/responses` | Mevcut OpenAI-compatible rol | Buffered/streaming raw provider text yayımlamaz |
| `POST` | `/openai/v1/chat/completions` | Mevcut OpenAI-compatible rol | Buffered/streaming raw provider text yayımlamaz |
| MCP call | Agent tool | Mevcut MCP auth/policy | Provider raw text tool error'a girmez |
| `POST` | `/agentprism/api/runs/{id}/judge` | Mevcut eval rolü | `judge_timeout` gerçek wait cutoff sonucudur |

OpenAPI shape değişmiyorsa document regeneration yapılmaz; example/error text
snapshot'ı değişiyorsa ilgili snapshot güncellenir.

### Arayüz payı

Yok. Frontend kaynak veya bundle değişikliği planlanmaz.

---

## Planlanan Dosya Listesi

> Dosya adları implementation sırasında ölçülerek daraltılabilir. Yeni shared
> helper yalnız iki veya daha fazla gerçek çağrı yolu gerektiriyorsa açılır.

```text
src/
├── AgentPrism.Abstractions/
│   ├── Models/IModelProvider.cs
│   ├── Evaluation/IRunJudge.cs
│   ├── Exceptions/AgentPrismJudgeException.cs          # kaldırılır
│   └── PublicAPI.Unshipped.txt
├── AgentPrism.Core/
│   ├── IAgentPrismBuilder.cs
│   ├── AgentPrismBuilder.cs                            # gerçek implementation yolu ölçülür
│   ├── Evaluation/
│   │   ├── AgentPrismOnlineEvaluationBuilderExtensions.cs
│   │   ├── OnlineEvalJobHandler.cs
│   │   └── RunJudgeSet.cs
│   ├── Models/
│   │   ├── ModelProviderRegistry.cs
│   │   ├── FallbackChatClient.cs
│   │   └── ProviderFailureNormalizer.cs
│   ├── Tools/
│   │   ├── ToolResultText.cs
│   │   └── TruncatingAIFunction.cs
│   ├── Guards/ContentGuardMessageMasker.cs
│   └── PublicAPI.Unshipped.txt
├── AgentPrism.OpenAI/OpenAIModelProvider.cs
├── AgentPrism.Anthropic/AnthropicModelProvider.cs
├── AgentPrism.Google/GoogleModelProvider.cs
├── AgentPrism.Azure/AzureOpenAIModelProvider.cs
└── AgentPrism.Testing.Contracts.Xunit/
    ├── AgentPrism.Testing.Contracts.Xunit.csproj
    ├── README.md
    ├── Contracts/Providers/{ModelProviderContract,ModelProviderCredentialContract}.cs
    ├── Contracts/Judges/RunJudgeContract.cs
    ├── Contracts/AgentSources/AgentSourceContract.cs
    ├── Contracts/Tools/CustomToolContract.cs
    └── PublicAPI.Unshipped.txt

tests/
├── AgentPrism.Core.UnitTests/
│   ├── Models/{ModelProviderRegistryTenantCredentialTests,ModelProviderFailureNormalizationTests}.cs
│   ├── Evaluation/{OnlineEvalJobHandlerTests,RunJudgeContractTests,RunJudgeSetTests}.cs
│   ├── Tools/{ToolResultTextTests,TruncatingAIFunctionTests,CustomToolContractTests}.cs
│   └── Catalog/BuiltInAgentSourceContractTests.cs
├── AgentPrism.AspNetCore.FunctionalTests/
│   ├── ProviderOutageErrorHandlingTests.cs
│   ├── ProviderFailureExposureTests.cs
│   ├── McpServerEndpointTests.cs
│   ├── A2AEndpointTests.cs
│   └── ToolCanonicalResultEndpointTests.cs
└── AgentPrism.Package.Tests/
    └── ReleaseArtifactTests.cs

samples/
├── Directory.Build.props                                # exact-version property, gerekirse
├── AgentPrism.Samples.FileRunStore*/
├── AgentPrism.Samples.CustomModelProvider*/
├── AgentPrism.Samples.CustomRunJudge/
│   ├── AgentPrism.Samples.CustomRunJudge.csproj
│   └── ResponseQualityJudge.cs
├── AgentPrism.Samples.CustomRunJudge.Tests/
│   ├── AgentPrism.Samples.CustomRunJudge.Tests.csproj
│   ├── ResponseQualityJudgeContractTests.cs
│   ├── RunJudgeRegistrationTests.cs
│   └── ResponseQualityJudgeRunTests.cs
├── AgentPrism.Samples.CustomAgentSource*/
└── AgentPrism.Samples.CustomTool*/

scripts/
├── kapi.py
├── release_extension_samples.py                         # orchestration ayrı dosya gerektirirse
└── kapi_test.py

docs-site/src/content/docs/
├── guides/{model-providers,write-your-own-judge,write-your-own-agent-source,write-your-own-tool}.md
├── concepts/{evaluation,tools}.md
├── packages.md
├── capabilities.md
└── reference/{configuration,compatibility,versioning}.md

docs/manuel-test/
├── 01-KURULUM-VE-PAKETLEME.md
├── 02-CEKIRDEK-VE-KATALOG.md
├── 08-OPENAI-UYUMLU-UCLAR.md
├── 17-EVAL-VE-DENEYLER.md
├── 18-MCP-VE-A2A.md
└── 24-TEST-PAKETI-VE-SABLON.md
```

---

## Hata Modları ve Testler

> Sınır geçen davranış birim testiyle kapatılmaz. DI, HTTP/SSE, tenant, storage,
> package ve AOT davranışları kendi gerçek sınırlarında ölçülür.

| Ne bozulabilir | Seviye | Test sınıfı / kanıt |
|---|---|---|
| Tenant credential unsupported provider'da setup client'a düşer | Birim + fonksiyonel | `ModelProviderRegistryTenantCredentialTests`, tenant run testi |
| Fallback link'i tenant credential'ı kaybeder | Birim | Registry fallback credential/capability matrisi |
| BYOK capability var ama wrong tenant key kullanılır | Contract + birim | `ModelProviderCredentialContract`, recording provider |
| Provider construction exception raw kalır | Birim + fonksiyonel | `ModelProviderFailureNormalizationTests`, buffered run |
| Provider invocation exception fallback'tan önce normalize edilir ve fallback çalışmaz | Birim | `FallbackChatClientTests` + wrapper-order assertion |
| Fallback exhaustion first failure mesajını public'e taşır | Birim + fonksiyonel | Chain exhaustion secret-like exception |
| SSE raw secret/type/endpoint sızdırır | Fonksiyonel | Agent SSE provider error testi |
| Buffered `ProblemDetails` raw text taşır | Fonksiyonel | Agent buffered provider error testi |
| OpenAI Responses/Chat buffered veya streaming raw text taşır | Fonksiyonel | `ProviderOutageErrorHandlingTests` genişletmesi |
| MCP/A2A adapter raw text taşır | Fonksiyonel | MCP handler; A2A ölçülen gerçek yol |
| Raw exception log'da kaybolur | Fonksiyonel | Capturing logger, original `Exception` graph assertion |
| Cancellation provider error diye normalize edilir | Birim + fonksiyonel | Caller/host OCE testleri |
| Token'ı yok sayan judge handler'ı tutar | Birim | Gated `OnlineEvalJobHandlerTests` |
| Timeout ile host cancellation karışır | Birim | Deterministic race tests |
| Late judge fault unobserved kalır | Birim | Late fault observer + `UnobservedTaskException` guard |
| Late judge score/summary/metric yazar | Birim | Store/summary/metric call count `0` |
| Timeout retry/lease semantics'i değiştirir | Birim + fonksiyonel | `ExecuteAsync` retry/backoff ve worker lease testi |
| Generic judge kaydı iki kez duplicate olur | Birim/DI | Builder registration tests |
| Instance/factory ile iki configured judge kaybolur | Birim/DI | Builder registration tests |
| Duplicate judge adı case farkıyla startup'tan geçer | Fonksiyonel startup | `RunJudgeSetTests`/host startup |
| `AddModelRunJudge` yeni API ile iki built-in üretir | Birim/DI | Registration enumeration count |
| Judge contract'ın consumer'ı yine yoktur | Coverage | Core built-in + CustomRunJudge `JudgeContracts` |
| Judge concurrency testi overlap yaratmaz | Contract + mutation | Barrier/gate + max-overlap; broken fake red |
| Judge/source cancellation ignore edilir | Contract + mutation | Pre-cancel + gated in-flight cancellation |
| Provider/source concurrency yalnız scheduler şansına bağlıdır | Contract + mutation | Caller barrier + gated fixture |
| Declaration-only tool sessizce contract'tan kaçar | Contract | Ayrı explicit davranış veya fail-fast fixture |
| Raw CLR tool sonucu budget/guard bypass eder | Birim + fonksiyonel | Full result matrix, direct `AIFunction` path |
| Guard ve truncation farklı text ölçer | Birim | Exact canonical representation equality |
| Reflection serializer AOT promise'ını kırar | Analyzer + AOT E2E | Core build warnings + packed AOT consumer |
| Generated complex tool context'i kaybolur | Generator + package E2E | APG0008 tests + CustomTool AOT run |
| Release sample eski preview çözer | Script integration | Two-version/stale-feed mutation |
| Release sample global cache kullanır | Script integration | Isolated `NUGET_PACKAGES` assertion |
| Sample AgentPrism source project'ine bağlanır | Package/script | ProjectReference scan + assets graph |
| Contract package Core'a inmeye başlar | Package graph | `dotnet list package --include-transitive`/assets assertion |
| Meta package testing dependency alır | Package graph | `.nuspec`/assets assertion |
| Yeni family storage coverage'ı kırar | Coverage | Beş family coverage testlerinin tamamı |
| Docs runtime'dan daha güçlü söz verir | Docs/site | `dokuman-bakim.py --site-denetle`, content assertions |
| Package count yeniden kayar | Package/docs | 20-pack manifest karşılaştırması |
| Gereksiz public helper eklenir | PublicAPI | Package-specific `Unshipped` diff audit |

Her yeni yol için beş soru:

- **İptal:** Caller token timeout'tan ayrılıyor mu? Tool/provider/judge path'i OCE'yi
  normalize ediyor mu?
- **Eşzamanlılık:** Aynı singleton instance gerçek overlap altında güvenli mi?
- **Boş/aşırı girdi:** Null/unsupported result, empty catalog/output ve byte budget
  ne yapıyor?
- **Başka kiracı:** Credential, cache/circuit key, score ve log context'i tenant
  sınırını koruyor mu?
- **Alt sistem hatası:** Provider SDK, judge late task, score store, logger ve package
  restore failure'ı hangi stable sonuca dönüyor?

---

## Manuel Kabul Case'leri

> Numaralar kapanışta hedef dosyaların sıradaki gerçek numaralarıyla alınır.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Tenant binding var; provider BYOK interface'ini uygulamıyor | Tenant ile agent run başlat | Provider/setup client çağrısı `0`; stable `provider_credential_unsupported`; credential değeri hiçbir response/log text'inde yok |
| 2 | Fake provider exception secret-like mesaj taşıyor | Aynı run'ı buffered ve SSE çağır | Generic stable public hata; raw mesaj yalnız captured server log exception'ında |
| 3 | Aynı failing provider OpenAI-compatible endpoint'e bağlı | Responses ve Chat Completions buffered + streaming çağır | `upstream_error`; raw mesaj yok |
| 4 | Aynı failing agent MCP server'da tool olarak açık | MCP tool çağır | Generic safe error; raw mesaj yok |
| 5 | Token'ı yok sayan custom judge ve kısa timeout | Manual judge endpoint'i çağır; judge gate'ini kapalı tut | Endpoint/handler timeout civarında `judge_timeout` ile döner; body'nin devam ettiği gözlenir |
| 6 | Late judge sonra fault eder | Handler döndükten sonra gate'i aç | Process/test host çökmez; unobserved exception yok; score yazılmaz |
| 7 | CustomRunJudge sample local feed exact version kullanıyor | Contract test + real run/evaluation çalıştır | Contract coverage boş; persisted `judge:response-quality` score var |
| 8 | Direct `AIFunction` raw CLR object döndürüyor | Guard kapalı/açık ve budget ile çağır | Raw object pass-through yok; AOT-safe fail-closed stable sonuç |
| 9 | Generated complex tool context taşıyor | Guard ve küçük `MaxOutputBytes` ile run yap | Guard ile truncation aynı canonical JSON'u görür; zarf budget'ı aşmaz |
| 10 | Release feed iki eski preview/global cache içeriyor | `kapi.py yayin --kuru` koş | Gate yalnız bu koşumun exact version ve isolated cache'ini kullanır; beş sample geçer |
| 11 | Packed provider/source/generated complex tool consumer | Native AOT publish ve binary run | Sıfır trim/AOT warning; üç binary başarılı |
| 12 | `samples/AgentPrism.Api` gerçek provider credential ile çalışıyor | Custom/built-in extension kayıtlarını içeren gerçek run ve judge yap | Run tamamlanır, tool çağrısı ve judge score'u gözlenir; secret output'a yazılmaz |

---

## Açık Sorular

Yok. Planı bloklayan seçimler kullanıcı tarafından 2026-08-25 tarihinde
kapatıldı:

- `F-153 → Faz 103`; kapsam dışı işler ayrı F-154–F-163 adaylarıdır.
- BYOK optional ikinci interface'tir ve runtime fail-closed'tur.
- Provider foreign exception ortak model-pipeline boundary'sinde normalize edilir;
  yeni public exception eklenmez.
- `JudgeTimeout` gerçek wait cutoff'tur; late body öldürülmez.
- `AgentPrismJudgeException` kaldırılır.
- Tool canonical guarantee runtime'da tamamlanır; unsupported raw CLR fail-closed,
  reflection yoktur.
- `AddRunJudge` üç overload ile doğrudan `IAgentPrismBuilder` üyesidir.

Implementation sırasında bu contract'ları değiştiren yeni bir tercih çıkarsa kodu
ilerletme; kullanıcıya sor. Dosya yerleşimi veya internal helper adı gibi contract
etkilemeyen yerel kararlar sessiz varsayım değildir.

---

## Bitiş Ölçütleri (DoD)

- [ ] BYOK mandatory/optional çelişkisi XML, contract suite ve runtime'da yoktur.
- [ ] Tenant credential varken capability desteklemeyen provider setup/global
      credential ile çağrılmaz; provider invocation count `0` ve stable
      `provider_credential_unsupported` kanıtlanır.
- [ ] Provider foreign raw exception text agent SSE, buffered HTTP, persisted
      `RunError`, OpenAI Responses, Chat Completions, MCP ve ölçülen A2A yüzeyinde
      görünmez.
- [ ] Raw provider exception `ILogger` exception graph'ında korunur; duplicate log
      fırtınası yoktur.
- [ ] Bilinen güvenli AgentPrism exception contract'ları korunur; caller/host OCE
      provider error'a çevrilmez.
- [ ] `JudgeTimeout`, token'ı yok sayan judge için gerçek wait cutoff yapar.
- [ ] Late judge success/fault gözlemlenir; unobserved exception, late score,
      summary veya metric yoktur.
- [ ] Host cancellation timeout'tan ayrıdır; retry/backoff/job lease contract'ı
      regression testleriyle korunur.
- [ ] `RunJudgeContract` en az bir built-in ve bir CustomRunJudge sample consumer'a
      sahiptir.
- [ ] `samples/AgentPrism.Samples.CustomRunJudge.Tests` AgentPrism için yalnız exact
      local-feed `PackageReference` kullanır; contract, registration ve real
      run/evaluation + score persistence testleri geçer.
- [ ] `AddRunJudge<TJudge>()`, instance ve factory overload'ları public'tir;
      singleton ve duplicate davranışları AgentSource deseniyle ölçülmüştür.
- [ ] Kullanılmayan public `AgentPrismJudgeException` kaldırılmıştır; stable judge
      error code'ları korunur ve yeni gereksiz public exception eklenmez.
- [ ] Tool guard ve truncation aynı canonical representation'ı görür.
- [ ] Tool result matrisi string, `JsonElement`, primitive, record/class,
      collection, null, unsupported raw object, generated complex context ve
      direct `AIFunction` yolunda yeşildir.
- [ ] Unsupported raw CLR tool result pass-through yapmaz; reflection serializer
      eklenmez; `AgentPrism.Core` AOT-compatible kalır.
- [ ] Provider, judge, source ve tool concurrency testleri deterministic gerçek
      overlap kanıtlar; mutation/red→green kaydı vardır.
- [ ] Cancellation testleri ignore davranışını başarı saymaz; uygun async
      fixture'larda in-flight cancellation ölçülür.
- [ ] Storage contract suite gövdeleri gereksiz değiştirilmemiştir ve dört built-in
      provider + FileRunStore consumer'ı yeşildir.
- [ ] Beş `ContractCoverage` family testi yeşildir; `Skip` sayısı `0` ve yeni
      family diğer aileleri kırmaz.
- [ ] `AgentPrism.Testing.Contracts.Xunit` resolved graph'ında `AgentPrism.Core`
      yoktur; Shouldly/xunit tipi production package public API'sine sızmaz.
- [ ] Release sample verification exact version, isolated `NUGET_PACKAGES`, temiz
      local feed ve AgentPrism ProjectReference yasağını zorlar.
- [ ] FileRunStore, CustomModelProvider, CustomRunJudge, CustomAgentSource ve
      CustomTool packed local feed'den build/test/run olur.
- [ ] Provider, source ve generated complex tool packed consumer Native AOT altında
      publish/run olur.
- [ ] Meta package graph'ında `AgentPrism.Testing` veya
      `AgentPrism.Testing.Contracts.Xunit` yoktur.
- [ ] `Testing.Contracts.Xunit` README/package description beş family'yi anlatır;
      `packages.md`, `compatibility.md`, `versioning.md`, capability/configuration
      sayfaları runtime ve 20-package çıktısıyla tutarlıdır.
- [ ] Provider/source/tool metric name low-cardinality beklentisi ve gerekli
      instance/factory disposal ownership farkı XML/site'da açıktır.
- [ ] Docs tool canonicalization ve judge timeout için runtime'dan daha güçlü söz
      vermez.
- [ ] Fazın PublicAPI audit tablosundaki her tip için keep/remove gerekçesi
      kapanış kaydına yazılmıştır; plan dışı public helper yoktur.
- [ ] Dört doğrulama kapısı sıfır uyarı verir.
- [ ] `samples/AgentPrism.Api` ile gerçek provider + tool + judge içeren `run`
      yapılır; çıktı ve score kanıtı bu belgeye yazılır.
- [ ] `secret` taraması boş döner; kasıtlı test fixture secret'ları yalnız açık
      test allowlist'i içinde ve production output dışında kalır.
- [ ] Manuel kabul case'leri ilgili `docs/manuel-test/` dosyalarına eklenir;
      otomatikleştirilebilenler koşulur.
- [ ] `tuketici-dokuman-senkronu` runtime donduktan sonra koşar; docs-site
      `npm run check` temizdir.
- [ ] Bağımsız `faz-denetim` taze bağlamla `git diff $FAZ_TABANI` üzerinde koşar;
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

## Riskler

| Risk | Önlem |
|---|---|
| `IModelProvider` imza değişikliği dört built-in ve sample'ı sessiz kırar | Solution build'e güvenme; bütün implementasyonları `rg "IModelProvider"` ile say, packed external consumer'ı yeniden compile et |
| Optional capability fallback zincirinde atlanır | Primary ve fallback için tenant credential/capability matrisi; provider invocation count assertion |
| Normalization fallback classification'dan içeri konur | Wrapper-order testi; retryable raw SDK exception fallback'i hâlâ tetiklemeli |
| “AgentPrismException preserve” kuralı foreign derived type ile secret sızdırır | Bilinen güvenli type/code allowlist; foreign derived fake ile negative functional test |
| Streaming normalizer yalnız ilk move'da çalışır | Async enumerable'ın her `MoveNextAsync` failure noktasını test et; helper içinde `Activity`/scope açma tuzağına dikkat et |
| Judge timeout late task host kapanışında process'i düşürür | Explicit late-task observer; success/fault/cancel üç terminal state testi |
| Timeout retry aynı singleton judge'da overlap üretir | XML idempotency/concurrency sözü, gated retry testi; F-152'yi bu faza çekme |
| Contract test deterministic gate implementer detail'ine dönüşür | Yalnız public observable result ve cancellation ölç; test fixture hook'unu en küçük tut |
| Tool canonicalization wire formatını istemeden değiştirir | Her registration yolunu gerçek MAF conversion ile ölç; string/JsonElement snapshot ve real run |
| Raw object fail-closed davranışı legitimate `AIContent` sonucunu bozar | `AIContent` explicit protocol exception; dedicated regression test |
| Reflection serializer AOT'u bozar | Source-generated context dışında serialization yok; analyzer + real Native AOT publish |
| `kapi.py yayin` çok yavaşlar veya recursive pack yapar | Tek pack, tek resolved version; consumer helper `--no-pack`/local feed kullanır; normal unit gate'e ekleme |
| Wildcard default release yoluna geri sızar | Script resolved exact property yoksa fail-fast; stale/two-version mutation testi |
| Test project sample source ProjectReference'ı “repo source dependency” sanılır | Yasağı açık tanımla: `src/AgentPrism.*` yasak, sample test → sample source meşru; assets graph AgentPrism'i nupkg'den göstermeli |
| Public wrapper cleanup kapsamı büyütür | Yalnız ölçülen Core↔MCP usage ve K-487/K-614 audit'i; genel değişiklik F-158 |
| Docs hedef davranışı runtime'dan önce yeşil gösterir | Runtime/test commit'i önce; docs sync ikinci kulvar; site claim'leri executable test adlarına bağla |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa “🔴 ve 🟡 yok” yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler ve preview.1 release readiness sonucu.
