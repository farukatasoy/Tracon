# Faz 250 — Alt Agent Sürüm Sabiti

> **Durum:** 🔧 Uygulandı, kapanış koşuyor (2026-10-26)
> **Kademe:** tam — public API'den satır silinir (`AgentDefinition.CallableAgentNames` yerine `CallableAgents`) ve kalıcı veri şekli değişir (saklanan tanım JSON'u eski alanı okumaya devam etmeli); HTTP sözleşmesi, iki üretilen istemci ve konsol değişir
> **Plan onayı:** Bakımcı, 2026-10-26 (sohbette alınan kararlar: yapılı liste `CallableAgents: [{name, version?}]` — yan sözlük reddedildi; konsol editörü sabiti gösterir ve düzenletir)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-409**
> **Genellik:** Bir veritabanı agent'ına başka agent'ları çağırtan ve sürüm sabiti kullanan her kurulum: sabitlenmiş bir üst agent (K-989 düğüm sabiti, K-895 onay devamı, deney kolu) bugün çocuklarını güncel sürümle koşturur. İş tüketici kodunda kalamaz: çocuk çağrısı Tracon'un derleyicisinde kurulur. İkinci kanıt ilk ilkedir: sabitlenen sürüm çağırdığını da sabitlemelidir — yoksa "v1 ile koş" sözü yalnız üst agent'ın talimatını kapsar
> **Karşı görüş:** "Çocuğun güncel sürümle koşması bilinçlidir; sürümü karşılaştırmak isteyen çocuğa doğrudan deney kurar" (site `concepts/agents.md:452-460`, K-895 (c)). Cevap: varsayılan değişmez — sabitsiz çocuk güncel sürümü koşar. Sabit yalnız tanım yazarı **açıkça** bir sürüm yazınca uygulanır; deney kolu yine yalnız kurulduğu agent'a uygulanır
> **Önkoşul:** Faz 248 — numara silmede yeniden kullanılırsa çocuk sabiti başka içeriği koşturur
> **Paketler:** `Tracon.Abstractions` (`AgentDefinition`, yeni `CallableAgentReference`), `Tracon.Core` (derleyici, doğrulayıcı, `CallableAgentResolver`, `SubAgentToolNames`, replay, devam), `Tracon.Sql.Shared` (`AgentDefinitionPayload`), `Tracon.AspNetCore` (`AgentContracts`, `AgentEndpoints`, OpenAPI), `Tracon.UI` (agent editörü), `Tracon.Client` + `packages/tracon-client` (üretilir), `Tracon.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok — tanım `jsonb`/JSON metnidir; eski alan okuma anında çevrilir (250.2)
> **Public API:** Değişir — `AgentDefinition.CallableAgentNames` (2 satır) silinir, `CallableAgents` ve `CallableAgentReference` eklenir. `rc.1` öncesi kırıcı değişiklik serbesttir (UR-012 B); sürüm notunda adıyla anılır. `wc -l src/*/PublicAPI.Shipped.txt`: 18 dosya × 1 satır (2026-10-26). `AgentDescriptor.CallableAgentNames` kalır (katalog izdüşümü; çağrı grafı adla kurulur, `AgentCallGraph.cs:83`)
> **Tüketici yüzeyi:** site `concepts/agents.md` (`:452-460` metni tersine döner: "unless the parent pins it"), `concepts/workflows.md` § *Pin an agent step to a version*, üretilen `http-api/schema-agentdefinition*.md`, `ui.md` (editör) · sevk edilen: XML + `<example>`, `PublicAPI.Unshipped.txt`, OpenAPI → iki istemci, `samples/Tracon.Samples.CustomAgentSource`, `CHANGELOG.md` `[Unreleased]` → *Changed* (kırıcı: alan adı) ve *Added*
> **Manuel test alanı:** `docs/manuel-test/29-AGENT-DESTEGI.md` (çağrılabilir agent) · `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` (editör)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman ve Faz 248'in kapanış devri (kapandıysa `FAZ-DEFTERI.md` satırı)
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-895 —\|K-989 —\|K-811 —\|K-1043 —\|K-228 —\|K-232 —" docs/KARARLAR.md
   ```
   **K-895** ((c) alt agent güncel sürümü çözer — bu faz onu tanım sabitiyle
   değiştirir; satıra not düşülür), **K-989** (düğüm sabiti; sabitlenen agent'ın
   kendi alt agent'ları güncel sürümle koşar — bu faz o cümleyi kapatır),
   **K-811** (derleme önbelleği içerik özetiyle; çocuk açıklaması özetin parçası),
   **K-1043** (alt agent tool modu), **K-228** (`en.ts` + `tr.ts` eksiksiz),
   **K-232** (sunucu yanıtı çevrilmez)
3. Önceki fazdan devir: Faz 248'in sayaç tablosu; bu faz ondan yalnız "numara
   yeniden kullanılmaz" kuralını alır
4. Alan hafızası:
   [`hafiza/agent-derleme-ve-onbellek.md`](hafiza/agent-derleme-ve-onbellek.md) (çocuk parmak izi, `:100` sürüm notu) ·
   [`hafiza/workflows.md`](hafiza/workflows.md) (`:76` düğüm sabiti anahtarda, kimlikte değil) ·
   [`hafiza/frontend.md`](hafiza/frontend.md) + [`hafiza/frontend-yerellestirme.md`](hafiza/frontend-yerellestirme.md) (editör) ·
   [`hafiza/nswag-istemci-uretimi.md`](hafiza/nswag-istemci-uretimi.md) (istemci üretimi)
5. Gerektiğinde: reviewer sabiti emsali `AgentDefinitionCompiler.Agents.cs:230-258`
   ve `LoopEvaluatorRegistry.cs:546-552,607`

---

## Önceki Fazdan Devir

Faz 248 kapandı (satırı `FAZ-DEFTERI.md`'de, karar K-1100). Bu faz ondan şunları devralır:

- **Kural:** bir agent adı için sürüm numarası bir kez verilir. `SaveAsync`/`RollbackAsync` numarayı
  `agent_definition_version_counters`'tan alır (`tenant_id`, `name`, `last_version`; `agent_definitions`'a yabancı
  anahtarı yok). `DeleteAsync` tanımı ve geçmişi siler, sayacı bırakır. Numara = `max(sayaç, agent satırının sürümü) + 1`
  (eski sürümden kalan bir düğüm satırı sayaca dokunmadan yazabilir; sorgu bunu tolere eder).
- **Sonuç bu fazın sabiti için:** silinip yeniden oluşturulan çocuğun eski dönem numarası hiçbir şeye işaret etmez;
  `GetVersionAsync` `null` döner. Kayıtta ve derlemede "çocuk agent'ın böyle bir sürümü yok" kapalı düşmelidir. Emsaller:
  workflow düğümü `pins agent 'x' to version N, but the agent has no version N`, onay devamı
  `Version N of agent 'x' was not found.`
- **Sözleşme:** `AgentDefinitionStoreContract` sekiz case aldı (silme sonrası numaralama, tekrar silme, bilinmeyen adı silme,
  recreate sonrası rollback, silinmiş agent'ı rollback, kiracı yalıtımı, eşzamanlı kayıt ×2). Özel bir `IAgentDefinitionStore`
  hepsini geçmelidir; bellek içi depo silmede girdiyi tutar, yalnız sürümleri boşaltır.
- 🚨 **Sözleşme sınıfındaki her `[Fact]` public yüzeydir:** yeni case `Tracon.Testing.Contracts.Xunit/PublicAPI.Unshipped.txt`'e
  sıralı girer (`RS0016`). Plan "API büyümüyor" dese de say.
- 🚨 **`DbHelpers.ReadSingleAsync<T>` `T : class` ister;** tek bir `int` (sayaç, sürüm) için `ExecuteScalarAsync` ve
  `Convert.ToInt32(…, CultureInfo.InvariantCulture)`.
- 🚨 **Yayın provası bu makinede koşmadı:** `python3 scripts/kapi.py yayin --kuru` taban paketleri
  (`v1.0.0-preview.5`) nuget.org'dan alamadığı için taban restore'unda durdu (en yakın sürüm `preview.4`). Bu faz
  `AgentDefinition.CallableAgentNames`'i değiştirdiği için (public API'den satır silinir) prova burada **gerekir**; önce
  tabanın alınabilir olduğunu doğrula. Sebebi araştırılmadı.
- Hazırlanmış ama kullanılmayan altyapı: yok.

## Faz 249'dan Devir

Faz 249 (K-1101) `ChildAgentInvoker`'ın sürüm çözümünü yeniden düzenledi; bu faz aynı dosyada çocuk sabitini kurar:

- **Sürüm artık çağrı başına çözülür.** `ResolveForRunAsync` en başta `VersionFor(scope)` çağırır ve sonucu hem
  `ParameterizedAgentFactory.FindAsync(ad, sürüm)` hem `ResolveAsync(sürüm)`'e verir; `_pinnedVersion` yalnız sabit durumda
  bu fonksiyondan döner. Oturum metotları (`CreateSessionCoreAsync` …) `ResolveAsync(ct)` ile `_pinnedVersion`'da kalır: parametreli
  adımda `_pinnedVersion` null'dır, yani oturum güncel sürümden, run seçilen sürümden gelir. `GetWorkflowAgentStep` oturum geçmediği
  için bugün erişilmez; çocuk sabiti bir oturum yoluna dokunursa bunu bil.
  Çocuk sabiti bu iki çağrıya aynı sürümü vermeli: biri bağlı agent'ın tanımını, öbürü derlenmiş agent'ı çözer.
- **Bu fazın "sürüm yok" yolu zaten kapalıdır:** olmayan sürüm `CallableAgentResolver.ResolveAsync(ad, sürüm)`'de
  `Version N of agent 'x' was not found.` ile düşer; geçmişi olmayan kod agent'ı `does not keep version history` ile.
- **Önbellek anahtarı kuralı:** `WorkflowAgentCache.AgentKey` sürüm parametresinin *adını* taşır, değerini değil (değer çağıranın
  kontrolündedir). Çocuk sabiti tanım yazarının sabit bir sayısıdır; derleyici önbelleği için ayrı düşünülmeli (K-811).

## Amaç

Bir agent başka agent'ları adıyla çağırır (`AgentDefinition.CallableAgentNames`,
`AgentDefinition.cs:105`). Çocuk her zaman güncel sürümünü koşar: derleyici
`ChildAgentInvoker`'ı sabitsiz kurar (`AgentDefinitionCompiler.Agents.cs:197-207`).
Üst agent `v1`'e sabitlense bile (workflow düğümü, onay devamı) çocuk `v3`
koşabilir. Reviewer için sabit zaten vardır (`ReviewerAgentVersion`) ve aynı
çağırıcıyı `pinnedVersion` ile kurar.

- **F-409 (alt agent yarısı)** — tanım her çağrılabilir agent için isteğe bağlı bir
  sürüm taşır: `CallableAgents: [{ name, version? }]`. Sürüm yazılmışsa çocuk o
  sürümü koşar; tool açıklaması ve derleme parmak izi o sürümden gelir. Sürüm
  yoksa bugünkü davranış (güncel sürüm). Üst agent bir sürüme sabitlenince o
  sürümün içeriğindeki çocuk sabitleri de geçerli olur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinition.cs:105`](../src/Tracon.Abstractions/Agents/AgentDefinition.cs) | `IReadOnlyList<string> CallableAgentNames` — yalnız ad |
| [`AgentDefinitionCompiler.Agents.cs:197-207`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs) | Çocuk çağırıcısı `pinnedVersion` olmadan kurulur |
| [`AgentDefinitionCompiler.Agents.cs:240-252`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs) | Reviewer çağırıcısı aynı tipi sabitle kurar — emsal |
| [`CallableAgentResolver.cs:98-125`](../src/Tracon.Core/Graph/CallableAgentResolver.cs) | Çocuk açıklaması ve descriptor güncel sürümden okunur |
| [`AgentDefinitionCompiler.Dependencies.cs:252-301`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Dependencies.cs) | Parmak izi çocuk açıklamasını içerir (K-811) |
| [`AgentDefinitionPayload.cs:62,159,192`](../src/Tracon.Sql.Shared/Internal/AgentDefinitionPayload.cs) | Saklanan JSON `callableAgentNames` taşır |
| [`AgentContracts.cs:69,126`](../src/Tracon.AspNetCore/Contracts/AgentContracts.cs) | HTTP isteği `CallableAgentNames` taşır |
| `docs-site/src/content/docs/concepts/agents.md:452-460` | "A pin reaches only the step's own agent: the callable agents of that agent still run their current version" |
| Kullanım | `CallableAgentNames`: `src/` içinde 28 kod satırı; `Tracon.UI` 11 dosya; testler 29 dosya; site 15 sayfa (4'ü üretilen şema) |

> Kanıtlar 2026-10-26 tarihinde doğrulandı.

---

## 250.1 — Model

```csharp
// Tracon.Abstractions
public sealed record CallableAgentReference
{
    public required string Name { get; init; }
    /// <summary>The version to run; null runs the agent's current version.</summary>
    public int? Version { get; init; }
}

public sealed record AgentDefinition
{
    // CallableAgentNames silinir
    public IReadOnlyList<CallableAgentReference> CallableAgents { get => field ?? []; init; }
}
```

- `AgentDescriptor.CallableAgentNames` kalır: katalog izdüşümü `CallableAgents`'ın
  adlarıdır (`AgentDescriptorProjection.cs:44`); çağrı grafı ve döngü denetimi
  adla çalışır (`AgentCallGraph.cs:83`).
- Kod agent'ı (`CodeAgentSource.cs:102`) aynı alanı kullanır; kod agent'ı sürüm
  tutmadığı için ona sabit yazılan çocuk kayıtta reddedilir (250.3).

## 250.2 — Kalıcı veri ve HTTP

- `AgentDefinitionPayload` yeni alanı yazar. Okurken yeni alan yoksa ve
  `callableAgentNames` varsa adlar sabitsiz referanslara çevrilir. Migration yok;
  eski satırlar ilk kayıtta yeni biçime geçer. Sözleşme testi eski biçimi okur.
- HTTP isteği (`AgentContracts.cs:69`) `callableAgents` alır. Eski
  `callableAgentNames` alanı `RemovedFieldFilter` (`src/Tracon.AspNetCore/Internal/RemovedFieldFilter.cs`)
  ile `400` ve yeni adı gösteren bir mesaj alır — sessizce yok sayılmaz.
- OpenAPI yenilenir; `Tracon.Client` ve `packages/tracon-client` yeniden üretilir.

## 250.3 — Kayıt doğrulaması

K-989 emsali (`AgentVersionPin.cs:37-80`): sürüm `< 1` → `400`; sürümü olmayan
agent → `400`; sürüm tutmayan kaynak (kod agent'ı) → `400`; aynı ad iki kez →
`400`. Denetim kayıtta ve derlemede yapılır (derleme anında sürüm kaybolduysa
çağrı adıyla düşer — Faz 248 numarayı yeniden kullanmadığı için "kaybolan sürüm"
yalnız silmedir).

## 250.4 — Derleme ve çağrı

- `CreateChildAgents` her çocuğa `pinnedVersion` geçirir (reviewer yolu gibi).
- Çocuğun açıklaması, descriptor'ı ve parmak izi sabit sürümden okunur
  (`CallableAgentResolver.cs:98-125`, `Dependencies.cs:252-301`). Çocuk yeni bir
  sürüm kaydedince sabitli üst agent'ın önbelleği geçersizleşmez; sabitsiz üst
  agent'ınki bugünkü gibi geçersizleşir.
- Üç çağrı modunun üçü de sabiti uygular: varsayılan çağrı, tool modu
  (`SubAgentToolNames.cs:106-145`) ve arka plan (MAF `BackgroundAgentsProvider`, `AgentDefinitionCompiler.Agents.cs:36-38,138-140`) —
  uygulamada her biri ölçülür.
- Replay (`RunReplayService.cs:435`) ve devam (`RunContinuationJobHandler.cs:189,289`)
  yeni alanı temizler/okur.

## 250.5 — Konsol

Agent editörünün çağrılabilir agent bölümü
(`src/Tracon.UI/frontend/src/screens/agent-editor/sections/callable-agents-section.tsx`)
her satırda isteğe bağlı bir sürüm seçimi gösterir. Seçenekler
`GET /tracon/api/agents/{name}/versions`'tan gelir; varsayılan "güncel".
Editör kaydederken sabiti aynen geri yazar (`full-replace-editors.test.tsx`
deseni). Yeni metinler `locales/en.ts` + `tr.ts`. **Bundle:** bugün 225,4 KB gzip
(`index-*.js.br`'den, 2026-10-26) / bütçe 250 KB; payı kapanışta ölçülür ve
buraya yazılır.

## 250.6 — Belge

- `concepts/agents.md:452-460` tersine döner: çocuk güncel sürümü koşar, **tanım
  onu sabitlemediyse**; üst agent sabitlenince o sürümün çocuk sabitleri geçerlidir;
  deney kolu yine yalnız kurulduğu agent'a uygulanır.
- K-895 (c) ve K-989 satırlarına "Faz 250: tanım sabiti" notu düşülür
  (`faz-tamamlama` Adım 8).

---

## Planlanan Public API

```csharp
// Tracon.Abstractions — eklenir
public sealed record CallableAgentReference { string Name; int? Version; }
public IReadOnlyList<CallableAgentReference> AgentDefinition.CallableAgents { get; init; }
// silinir
// Tracon.AgentDefinition.CallableAgentNames.get / .init
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` / `PUT` | `/api/agents` · `/api/agents/{name}` | Operator | Gövde `callableAgents: [{name, version?}]`; eski `callableAgentNames` → `400` |
| `GET` | `/api/agents/{name}` · `/api/agents/{name}/versions` | Reader | Tanım `callableAgents` döner |

### Arayüz payı

Ölçülmeli (250.5); bugünkü kullanım 225,4 KB / 250 KB gzip.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Agents/{AgentDefinition,CallableAgentReference}.cs
src/Tracon.Abstractions/PublicAPI.Unshipped.txt
src/Tracon.Core/Compilation/{AgentDefinitionCompiler.Agents,AgentDefinitionCompiler.Dependencies,AgentDefinitionValidator}.cs
src/Tracon.Core/Graph/{CallableAgentResolver,SubAgentToolNames,AgentCallGraph}.cs
src/Tracon.Core/Catalog/{AgentDescriptorProjection,CodeAgentSource,CompositeAgentCatalog}.cs
src/Tracon.Core/Replay/RunReplayService.cs · Scheduling/RunContinuationJobHandler.cs
src/Tracon.Sql.Shared/Internal/AgentDefinitionPayload.cs
src/Tracon.AspNetCore/Contracts/AgentContracts.cs · Endpoints/AgentEndpoints.cs · Internal/RemovedFieldFilter.cs
src/Tracon.UI/frontend/src/screens/agent-editor/{model.ts,use-agent-editor.ts,sections/callable-agents-section.tsx}
src/Tracon.UI/frontend/src/locales/{en,tr}.ts
src/Tracon.Client (üretilir) · packages/tracon-client (üretilir)
src/Tracon.Testing.Contracts.Xunit/Contracts/AgentDefinitionStoreContract.cs
samples/Tracon.Samples.CustomAgentSource · samples/Tracon.Api (kullanım varsa)
docs-site/src/content/docs/concepts/{agents,workflows}.md · ui.md
CHANGELOG.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Sabitli çocuk güncel sürümü koşar (**önce kırmızı**: çocuk `v2` kaydedildikten sonra `v1` sabiti) | Fonksiyonel | `CallableAgentVersionPinTests` (yeni; çocuk run satırı `agentVersion = 1`) |
| Üst agent `v1`'e sabitlenince çocuk sabitleri uygulanmaz | Fonksiyonel | `WorkflowAgentVersionPinTests` yeni case |
| Modele giden tool açıklaması güncel çocuktan gelir | Fonksiyonel | `CallableAgentVersionPinTests` (model isteğindeki açıklama `v1`'inki) |
| Tool modu ya da arka plan modu sabiti atlar | Fonksiyonel | aynı sınıf, mod başına case |
| Olmayan sürüm, `< 1`, kod agent'ına sabit, tekrar eden ad kaydedilir | Fonksiyonel (HTTP) | `AgentCrudTests` / doğrulayıcı testleri |
| Eski `callableAgentNames` HTTP alanı sessizce yok sayılır | Fonksiyonel (HTTP) | `RemovedFieldFilter` testi |
| Eski saklanan JSON okunamaz | Sözleşme + entegrasyon | `AgentDefinitionStoreContract` (eski biçim) + `AgentDefinitionPayloadRoundTripTests` (Sqlite) |
| Sabitli üst agent'ın önbelleği çocuk kaydında gereksiz geçersizleşir ya da sabitsiz olanınki geçersizleşmez | Birim | parmak izi testleri (`AgentDefinitionCompiler.Dependencies`) |
| Replay ya da devam yeni alanı kaçırır | Birim | `RunReplayService` / `RunContinuationJobHandler` testleri |
| Çağrı grafı döngüsü sabitle kaçar | Birim | `AgentCallGraph` testleri (adla) |
| Editör kaydederken sabiti siler | Vitest | `full-replace-editors.test.tsx`, `agent-editor.test.tsx` |
| Editörde sürüm seçimi | E2E | `Tracon.Ui.E2ETests` agent editörü sınıfı |
| `en.ts`/`tr.ts` eksik anahtar | Derleme (K-228) | frontend build |

Beş soru: **iptal** — değişmez · **eşzamanlılık** — çocuk sürümü çağrı anında
sabitten çözülür, paylaşılan durum yok · **boş/aşırı girdi** — boş liste =
çocuk yok; sürüm `null` = güncel · **başka kiracı** — çocuk kiracının kataloğundan
çözülür (bugünkü yol); başka kiracının sürümü görünmez · **alt sistem hatası** —
sabit sürüm silindiyse çağrı adıyla düşer.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | `samples/Tracon.Api`, iki DB agent'ı (`writer` → `researcher`) | `researcher`'ı iki kez kaydet; `writer`'ı `callableAgents: [{name:"researcher", version:1}]` ile kaydet; `writer`'ı koştur | Çocuk run satırı `agentVersion: 1` |
| 2 🤖 | Aynı kurulum, konsol | Editörde `researcher` satırında sürüm `1` seç, kaydet, yeniden aç | Seçim korunur; `GET /tracon/api/agents/writer` `version: 1` döner |

Case'ler `docs/manuel-test/29-AGENT-DESTEGI.md` ve `10-ARAYUZ-AGENT-PLAYGROUND.md`'ye eklenir.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sabit kayıtta mı yalnız derlemede mi denetlenir | A: ikisinde (K-989 gibi) · B: yalnız derlemede | A |
| 2 | `AgentDescriptor` sabiti de gösterir mi (katalog listesi) | A: yalnız adlar kalır · B: `CallableAgents` izdüşümü eklenir | A — katalog sözleşmesi büyümez; sabit tanımda okunur |
| 3 | `GET …/versions` sürüm seçimine yetmezse (sayfalama) | A: son N sürüm + elle sayı girişi · B: tam liste | A; ölçülmeli |

---

## Kapsam Dışı

- Skill sürüm sabiti — K-082 (kullanıcı kararı) yeniden açılmadan yapılamaz; F-698.
- Deney kolunun çocuklara yayılması — bilinçli olarak yok (site `agents.md:452-460`).
- Saklanan workflow düğümünün alt agent sabiti — düğüm zaten üst agent'ı sabitler; çocuklar o sürümün tanımından gelir.

---

## Bitiş Ölçütleri (DoD)

- [x] `callableAgents: [{name, version: 1}]` ile çocuk `v1` koşar; açıklama ve parmak izi `v1`'den gelir; üç çağrı modunda da — `CallableAgentVersionPinTests` (Tool × Background × akışlı/akışsız; sabit satırı çıkarılınca beş test kırmızı, ölçüldü) · "üç mod" = varsayılan arka plan, `Tool`; MAF'ın arka plan sağlayıcısı aynı `CreateChildAgents`'ten geçer
- [x] Sabitsiz çocuk güncel sürümü koşar (değişmez davranış) — aynı sınıf, `A_callee_with_no_pin_follows_the_agent_to_its_new_version`
- [x] Eski JSON okunur; eski HTTP alanı `400` ve yeni adla cevaplanır — `AgentDefinitionPayloadRoundTripTests` (Sqlite) · `The_removed_callableAgentNames_field_is_refused_…` (POST, PUT, validate)
- [x] Konsol editörü sabiti gösterir, düzenletir ve korur; `en`/`tr` eksiksiz; bundle payı ölçüldü: 225,8 KB gzip / 250 KB (+0,4 KB) — Vitest (`agent-editor.test.tsx`, `full-replace-editors.test.tsx`), E2E (`AgentTests`)
- [x] İki istemci yeniden üretildi; `PublicAPI.Unshipped.txt` güncel
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban 1b0be47e`) — kapanış koşumunda
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (`MT-UIAG-067`): sabitli üst agent çocuğu `agentVersion: 1`, cevap `Echo [instructions: ALPHA text]`; sabitsiz `agentVersion: 2`, `BETA text`. OpenAI anahtarı olmadığı için echo modelleriyle (Sapmalar)
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] `🤖` case'ler `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md`'de (`MT-UIAG-067`, `068`) ve koşuldu; gerisi otomatik testte
- [x] `faz-denetim` koşuldu; 🔴 bulgu çıkmadı (🟡 üç, 🟢 beş: aşağıda)
- [x] `docs-site/` güncellendi (250.6 + kalıcılık sayfası); `npm run check` (içerik, derleme, bağlantı, ağırlık) temiz; `CHANGELOG.md` *Changed* + *Added* yazıldı
- [x] Karar `docs/KARARLAR.md`'ye yazıldı: K-1102 (`public-api` + `kalıcı-veri`; reddedilen alternatifler: yan sözlük `CallableAgentVersions`, kayıtta kilit dosyası, zamana göre gizli sabit)

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*CallableAgentVersionPinTests*"
curl -s "http://localhost:5081/tracon/api/runs?parentRunId=<runId>" | jq '.. | .agentVersion? // empty'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Eski istemci `callableAgentNames` gönderir ve çocuklar sessizce düşer | `RemovedFieldFilter` `400` döner; `CHANGELOG` *Changed* |
| Saklanan eski JSON yanlış okunur, çocuklar kaybolur | Okuma çevirisi + sözleşme testi eski biçimle |
| Editör sabiti siler | Vitest tam yerine koyma testi |
| Bundle bütçesi (24,6 KB boşluk) | Sürüm seçimi mevcut bileşenlerle yazılır; pay ölçülür |
| Parmak izi değişikliği önbelleği gereksiz boşaltır | Birim testi iki yönü ölçer |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

- **`faz-baslangic` Adım 0 atlandı (kullanıcı kararı):** `main`'in son üç CI koşumu `failure` (fazlar 245–247 kapanışları). Kullanıcı "deploy sorununa hiç bakmadan direkt başla" dedi; neden araştırılmadı.
- **`CallableAgentReference` iki üye fazla taşır:** `FromName(string)` ve `string`'ten örtük dönüşüm. Plan yalnız `Name` + `Version` yazıyordu. Gerekçe: `CallableAgentNames = ["support"]` yazan her tüketici ve testin 40+ satırı `CallableAgents = ["support"]` olarak kalır (yalnız alan adı değişir); CA2225 adlandırılmış bir eş ister, o da `FromName`. `PublicAPI.Unshipped.txt` iki satır fazla taşır.
- **Üç yeni doğrulama kodu (planda yoktu):** `callable_agent_version_invalid` (sürüm < 1 ya da kod agent'ı), `callable_agent_version_not_found`, `callable_agent_duplicate`. Plan 250.3 dört durumu saydı ama kod adı vermedi; kayıt, doğrulayıcı ve derleyici aynı kodu paylaşsın diye `ValidationCodes`'a girdi. Yan etki: `docs/sozlesme/dogrulama-kodlari.txt` (+3), `required-uyeler.txt` (+`CallableAgentReference.Name`), `public-surface-baseline.txt` (+1 tip) yenilendi.
- **Doğrulayıcı yolu:** `AgentCallGraph` ad listesiyle çalışmaya devam eder; sabit denetimi yeni `CallableAgentPins` (kayıt uç noktası `AgentDefinitionValidator` üzerinden, doğrulayıcı, derleyici). Kayıt ucundaki erken `ValidateCallGraphAsync` yalnız grafı denetler; sabit hatası `ValidateEntitiesAsync`'ten `400` olarak döner. Araç adı çakışması yolu `callableAgentNames[i]` → `callableAgents[i].name` oldu.
- **`CallableAgentInfo` (public) değişmedi.** Plan 250.4 "çocuğun açıklaması ve parmak izi sabit sürümden okunur" dedi, ama sabiti info'ya eklemek public struct'ı değiştirirdi. `CreateChildAgents` sabiti tanımdan ad eşlemesiyle okur; `DescribeAsync(çağıran, referanslar)` yeni içsel aşırı yüklemedir. Sabitli çocuğun açıklaması yalnız veritabanı kaynağında sürümden okunur; özel kaynakta (`Custom`) güncel açıklama kalır ve sürümü bilmeyen kaynak çağrıyı katalogda reddeder.
- **Saklanan satır:** eski `callableAgentNames` JSON'u metin düzeyinde taşınmadı; `AgentDefinitionPayload` onu yalnız OKUR (`ToDefinition` sürümsüz referansa çevirir, kayıt yalnız `callableAgents` yazar, boşken yazmaz). Her eski satır boş da olsa bu anahtarı taşıdığı için metin taşıması her okumada ayrıştırma isterdi. Sonuç: eski bir sürümü geri almak (`RollbackAsync`) ham JSON'u kopyalar, yani yeni satır eski anahtarı taşır; okunur ve bir sonraki kayıtta yeni biçime geçer. Sabit kaybı yoktur (eski satırda sabit yok).
- **Açık soru 3:** `GET /api/agents/{name}/versions` sayfalamaz, tam liste döner. Konsol tam listeyi gösterir (iş akışı editörüyle aynı); "son N sürüm + elle sayı" gerekmedi. İş akışı editörünün `AgentVersion`'ı ortak `components/agent-version-select.tsx` oldu ve üç yerelleştirme anahtarı `workflowEditor.agentVersion{Current,Missing,VersionsFailed}` → `agentVersion.{current,missing,loadFailed}` taşındı.
- **Yerelleştirme dosyaları:** plan `locales/en.ts` + `tr.ts` yazdı; sözlük `locales/{en,tr}/agents.ts` ve `common.ts` parçalarındadır.
- **Manuel case'ler yalnız `10-ARAYUZ-AGENT-PLAYGROUND.md`'de** (`MT-UIAG-067` API + gerçek run, `068` konsol). Plan `29-AGENT-DESTEGI.md`'yi de saydı; o dosya agent destek aracını (`tracon agent-skill`, analyzer) kapsar, çağrılabilir agent'ı değil. Eski `callableAgentNames` gövdesi taşıyan altı manuel-test dosyası (`02`, `07`, `10`, `11`, `21`, `23`) yeni alanla güncellendi; yoksa `400` alırlardı.
- **Örnek uygulama:** gerçek `run` için OpenAI anahtarı yoktu; `EchoModelProvider`'a `echo-delegator-1` eklendi (ilk tool'u bir kez çağırır, sonucu yanıtlar). Çocuk `echo-instructions-1` ile talimatını cevaba yazar, böylece hangi sürümün koştuğu metinden görünür.
- **Bundle:** 225,8 KB gzip / 250 KB (+0,4 KB; plan tabanı 225,4).
- **Devam kapısı testsiz:** `RunContinuationJobHandler` "skill ya da çağrılabilir agent varsa devam edilemez" kapısı yalnız alan adı değişikliğiyle güncellendi; kapıyı koşturan mevcut test yoktu ve eklenmedi (onay bekleyen bir run kurmak gerekir).
- **Yayın provası (`kapi.py yayin --kuru`):** kirli ağaçta koşmaz; ilk commit'ten sonra koşuldu, sonucu "Süreç Ölçümü"nün altındaki satırda.
- **Çağrı grafı ad ile çalışır; sabit onu değiştirmez (denetim 🟡1):** `AgentCallGraph` kenarlarını güncel sürümün adlarından kurar. Yalnız sabitli eski sürüm üzerinden kurulan bir döngü kayıtta görünmez ve derinlik sınırı çalışmada durdurur; tersi sahte `cycle` ile `400` alır. İlk taslağın site cümlesi ("sabit döngüyü gizlemez") yanlıştı, düzeltildi. Test: `A_cycle_that_exists_only_through_a_pinned_version_is_saved_and_the_depth_limit_stops_the_run`. Sabitli sürümün adlarıyla graf kurmak F-706'dır.
- **Derleme tekrar eden adı da reddeder (denetim 🟡2):** ilk uygulama yalnız kayıtta ve doğrulayıcıda reddediyordu; kodda yazılmış bir tanım iki girdiyi tek sabite çökertirdi. `CallableAgentResolver.DescribeAsync` artık `callable_agent_duplicate` iletisiyle `TraconCompilationException` atar. Davranış değişikliği: eskiden kabul edilen kodda tekrarlı bir liste artık derlemede düşer (`rc.1` öncesi, UR-012 B).
- **Eksik test satırları (denetim 🟡3):** (a) workflow adımı + sabitli üst agent için `CallableAgentVersionPinTests.A_workflow_step_pinned_to_a_parent_version_runs_the_callee_versions_that_version_names` eklendi (`WorkflowAgentVersionPinTests`'e değil: aynı rig, aynı sınıf). (b) `A_pin_is_judged_against_the_callers_own_tenant` eklendi (başlık tabanlı kiracılık). (c) Replay yalnız `NoTools`'ta sınandı: `LiveTools` derleme yolundan geçer ve normal run ile aynı `ResolveCallableAgentsAsync`'i koşar; ayrı test eklenmedi. (d) `AgentDefinitionStoreContract` eski biçimi okumaz: eski satır `AgentDefinitionPayload`'a özgüdür ve üç SQL sağlayıcısı aynı sınıfı paylaşır; özel bir `IAgentDefinitionStore` o anahtarı hiç görmez. Eski satırı Sqlite testi sınar.
- **Sevk edilen metin daraltıldı (denetim 🟢2, 🟢3):** sürümden açıklama okuma yalnız veritabanı agent'ı için söylenir (`Custom` kaynak güncel açıklamayı korur); `SqlAgentDefinitionStore` ve `AgentDefinitionPayload` yorumları rollback'in `callableAgentNames`'i kopyaladığını söyler.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

- **K-1102** *(public-api, kalıcı-veri)* — `CallableAgents: [{name, version?}]`; `callableAgentNames` kalktı. `docs/KARARLAR.md`'de.
- Commit mesajına: `CallableAgentReference`'ın `string`'ten örtük dönüşümü; üç `callable_agent_*` doğrulama kodu; saklanan satırda eski anahtarın yalnız okunması.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan değişmedi; 12 sapma kayda geçti) |
| Düzeltme turu sayısı | 1 (denetim sonrası: derleme tekrar reddi, üç test, belge daraltması, ekran görüntüleri) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 bilinen; kapanış koşumuna kadar kırmızı kalan: konsol ekran görüntüleri damgası ve iki sevk edilen sayı (`README.md` tür sayısı, `llms-full.txt`) — üretim adımları, kapıda yakalandı |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı (2026-10-26, `faz-denetcisi`). 🔴 yok.

| Bulgu | Seviye | Sonuç |
|---|---|---|
| Site "sabit döngüyü gizlemez" diyordu; graf güncel sürümün adlarını okur | 🟡 | Cümle düzeltildi, derinlik sınırının durdurduğunu gösteren test eklendi, graf F-706 olarak devredildi |
| Belge "derleme tekrar eden adı reddeder" diyordu; yalnız kayıt reddediyordu | 🟡 | `DescribeAsync` tekrarı reddeder; birim testi eklendi |
| Planın üç test satırı karşılıksız (workflow adımı, başka kiracı, eski biçim sözleşmesi, `LiveTools` replay) | 🟡 | İki case eklendi; ikisi Sapmalar'da gerekçelendi |
| Sabitli çocuk her derlemede ve çağrıda `GetVersionAsync` okur | 🟢 | F-707 |
| `Custom` kaynak açıklaması güncel sürümden gelir; metin koşulsuz yazıyordu | 🟢 | Sevk edilen metin daraltıldı |
| `SqlAgentDefinitionStore` ve `UpgradeLegacyKeys` yorumları rollback'te yanlış | 🟢 | Yorumlar düzeltildi |
| `describeCallable` başka bir JSDoc'un arasına girmişti | 🟢 | Taşındı |
| İki dosyada dosya sonu boş satır | 🟢 | Düzeltildi |
