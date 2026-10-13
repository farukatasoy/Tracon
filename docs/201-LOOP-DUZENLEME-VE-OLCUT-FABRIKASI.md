# Faz 201 — Konsolda Loop Düzenleme ve DI'lı Ölçüt Fabrikası

> **Durum:** 🔧 Kapanışa hazır (2026-10-13) — kod, bağımsız denetim ve kapanış adımları 5–8 bitti; arşiv, damıtma ve `kapanis` kapısı birleştirmeden sonra
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A, 6-A, 7-A)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-302**, **F-301**
> **Genellik:** Kendi talimatı, kendi şeması ve kodla hesaplanan ön kontrolü olan yargıç birçok tüketici sınıfının kalite kapısıdır (içerik QA'sı, kod incelemesi, çıkarım doğrulaması). İkinci kanıt ilk ilkedir: MAF `LoopEvaluator` bağımlılık ister, Tracon'un kayıt yüzeyi DI vermez (K-218 sınıfı). Domain'e özgü ölçüt tüketici kodunda kalır
> **Karşı görüş:** "Yerleşik `aiJudge` çoğu kapıyı karşılar." Plan bunu kabul eder: `aiJudge` varsayılan kalır; fabrika yalnız onun ifade edemediği kapı içindir ve örnekleri alan bağımsızdır
> **Önkoşul:** İşlevsel önkoşul yok. [Faz 195](arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) 2026-10-12'de kapandı (ikisi `full-replace-editors.test.tsx`'i paylaşır)
> **Paketler:** `Tracon.Core`, `.AspNetCore`, `.UI` · üretilen: `Tracon.Client`, `@tracon/client`
> **Yeni paket:** Yok · **Migration:** Yok — `harness.loop` tanım blob'undadır; veri modeli değişmez
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/agents.md` § loop, `ui.md` § *Agents* + § *Editor boundaries* + ekran görüntüsü, `capabilities.md:47` (`api/`, `http-api/` üretilir) · sevk edilen: aşırı yüklemenin XML `<example>`'ı, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` (konsol) · `02-CEKIRDEK-VE-KATALOG.md` (fabrika, uç)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Yalnız işaret edilen bölümleri oku.

1. Bu doküman
2. Kararlar: `grep -n "K-020\|K-218\|K-706\|K-708\|K-709\|K-725\|K-874\|K-884\|K-893" docs/KARARLAR.md`
   — **K-218** (bağımlılık kurulumda), **K-706** (`aiJudge` = host'un
   `AddModelRunJudge` bağlaması), **K-708/K-709** (gölgeleme yok; genişleme
   noktası değil), **K-874** (yargıç egress + kurulum anahtarı)
3. Devir notları (Faz 151 loop'u kurdu; Faz 192 F-302'yi devretti):
   `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/{151-HARNESSIN-DONGU-YETENEGI,192-DONGU-TUKENME-POLITIKASI}.md`
4. Hafıza: [`maf-harness-ve-loop.md`](hafiza/maf-harness-ve-loop.md) ·
   [`frontend-test-altyapisi.md`](hafiza/frontend-test-altyapisi.md) ·
   [`aspnetcore-di.md`](hafiza/aspnetcore-di.md) (XML'de 🚨 ve `K-NNN` yasak)

---

## Amaç

Operatör `Loop`'u konsoldan kuramaz; editör onu yalnız taşır. Host kod
ölçütüne DI ile servis veremez. Ortak yüzey Loop ölçüt sözleşmesi ve onu
listeleyen, doğrulayan, düzenleyen yüzeydir.

- **F-302** — konsol Loop'u düzenler: ölçüt listesi (yerleşik + kodla kayıtlı
  türler), `MaxIterations`, `FreshContextPerIteration`, `aiJudge` alanları,
  `OnExhausted`, `OnCriterionError`; yollu doğrulama raporu.
- **F-301** — `AddLoopEvaluator(kind, Func<IServiceProvider, LoopEvaluator>)`:
  kod ölçütü DI servisine ulaşır.

**Tüketici bağlamı.** Çok kiracılı bir host'ta writer agent'ın Loop'u bir
`aiJudge` reviewer taşır; revizyon sayısı `MaxIterations`'tır ve operatör
ikisini konsoldan ayarlar. Host'un kural doğrulayıcısı (şema + linter) DI'lı
bir kod ölçütüdür. İnsan onayı ileride Loop ölçütü olur (kullanıcı kararı,
2026-10-12). Plan genel bir Tracon yeteneğidir.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Kod ölçütüne tanım başına ayar | Özel tür veri almaz (`LoopEvaluatorRegistry.cs:321-323`); ayrı aday (F-410) |
| Tanım başına yargıç modeli | K-706 · Açık Soru 3 |
| Onay ölçütü ve denetim izi | Ayrı aday (F-411) — §201.5 |
| `todoCompletion.Modes` kontrolü | Mod sağlayıcısı yok (`LoopCriterion.cs:86-90`); alan korunur |
| Yargıç anahtarı, BYOK; konsolda kod | K-874; K2 |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `TraconBuilderExtensions.Evaluation.cs:148-156` · `TraconLoopEvaluatorRegistration.cs:22` | Kayıt yalnız hazır örnek alır; emsal `AddRunJudge(factory)` `:64-71` |
| MAF 1.20.0 `LoopContext` (`dump-api.sh`) | Sekiz üye; `IServiceProvider` yok |
| `TraconServiceCollectionExtensions.Registration.Core.cs:288`, `:323` | Derleyici singleton'ı kayıtları kurulumda çözer |
| `CallableAgentResolver.cs:9-17` | 🚨 `IAgentCatalog → IAgentSource → AgentDefinitionCompiler → … → IAgentCatalog` döngüsü belgeli; çözüm "ilk kullanımda" |
| `HttpTenantContext.cs:74-75` · `RecordingLoopEvaluator.cs:283` | Kiracı her okumada ambient çözülür; ölçüt run'ın async akışında koşar |
| `harness-section.tsx:39-67` · `agent-editor/model.ts:201`, `:248` | Editör iki sayı alanı düzenler; `harness` (ve `loop`) olduğu gibi taşınır |
| `full-replace-editors.test.tsx:126-134` | Kapı `loop`'u tam alanla taşır (tek `aiJudge`) |
| `LoopEvaluatorRegistry.cs:350-357` | Yargıç modeli tanımda değil, host bağlamasıdır |
| `CatalogEndpoints.cs:20-29` | Ölçüt türü kataloğu yok (`grep -rn "loop-criteria" src` boş) |
| `AgentDefinitionValidator.cs:321-341` · `validation-report.tsx:35-38` | Loop hatası tek, yolsuz `compilation_error`; rapor `path` gösterebilir |
| `agent-detail.tsx:458-477` · `diff-view.tsx:112` | Sürüm farkı `loop`'u göstermez |
| `RecordingLoopEvaluator.cs:290-301` | İterasyon olayında geri bildirim metni ve model yok |
| `docs-site/…/ui.md:441-445` | Kontrolsüz alan listesi `loop`'u 2026-10-12'den beri anar (F-342 demeti); bu faz onu listeden çıkarır |

> Kanıtlar 2026-10-12 tarihinde `99fe0b13` zemininde doğrulandı.

---

## 201.1 — Fabrikalı ölçüt kaydı (F-301)

- İç kayıt fabrika taşır; örnek aşırı yüklemesi `_ => evaluator` olur.
- **Çağrılma anı: türü adlandıran ilk derleme** (Açık Soru 1). Tür başına bir
  kez başarıyla koşar; eşzamanlı ilk derlemeler tek çağrı görür. Atarsa
  önbelleğe girmez; `TraconCompilationException` türü adlandırır. Kurulumda
  çağrılsaydı `IAgentCatalog` isteyen fabrika (reviewer deseni) döngüye
  girerdi; ilk derlemede katalog kuruludur.
- Yinelenen tür ve gölgeleme (K-708) fabrika çağrılmadan kurulumda reddedilir
  (`LoopEvaluatorRegistry.cs:96-109`).
- Bağımlılık fabrikada alınır (K-218). Fabrika kök sağlayıcıyı alır; scoped
  servis çözmez.
- 🚨 **Kiracı.** Tek örnek bütün kiracılara hizmet eder; fabrika ilk kiracının
  derlemesinde koşar ve orada okunan kiracı sonrakilere sızar. Ölçüt
  `ITenantContext.TenantId`'yi değerlendirme anında okur.
- **Reviewer deseni** (`<example>`): `IAgentCatalog` fabrikada alınır;
  reviewer `TraconRunContext.Current`'tan kurulan `TraconRunOptions`
  (`ParentRunId`, `RootRunId`, `Depth + 1`) ile çocuk run koşar, kendi usage'ını
  taşır (K-893 yalnız `aiJudge` içindir). **Ölçülmeli:** ağaç bütçesi, derinlik.
- **Zorlayıcı örnek, kabul ölçütü değil.** Bir tüketicinin podcast QA'sı
  ([rapor](arsiv/incelemeler/2026-10-11-podcast-tuketici-uyum-raporu.md) § 4,
  2026-10-13) desenin sınırını gösterir: kendi şemalı karar, reviewer'dan önce
  kodla hesaplanan girdi ve run boyunca tutulan durum
  (`LoopContext.AdditionalProperties`). `aiJudge` üçünü de taşımaz. Desen
  bunları **alan bağımsız** taşımalıdır; `<example>` ve `samples/Tracon.Api`
  reviewer'ı domain'e özgü alan (kapsam haritası, replik, ritim) taşımaz. API
  biçimini tek bir tüketici belirlemez (ADAYLAR § *Tüketici geri bildirimi
  kuralı*).
- `MAAI001` bastırması yeni üyeye daraltılır (K-020). K-709 değişmez.

## 201.2 — Ölçüt kataloğu ucu

- `GET /api/loop-criteria` — **kök seviye**: `/api/agents/loop-criteria`,
  `GET /api/agents/{name}`'de `loop-criteria` adlı agent'ı gölgelerdi (ad
  kuralı yok).
- Yanıt: türler, `builtIn`, `description` (Açık Soru 4), yargıç modeli
  (`provider`, `model`) ya da `null`. **Fabrika çağrılmaz.** `ModelBinding`
  dönülmez (`ProviderSettings` sızmasın).
- `Reader` + `AgentsRead` (`CatalogEndpoints.cs:22-23`). Egress'in yargıcı
  yasaklaması doğrulama raporunda görünür (K-874).

## 201.3 — Doğrulama raporu

- Registry loop sorunlarının **tamamını** tek kaynakta toplar; `Build` aynı
  listeyi kullanır.
- `AgentDefinitionValidator` derlemeden önce bir loop adımı koşar; her mesaj
  kod ve yol taşır (`harness.loop.criteria[1].marker`). Taslak kodlar (`loop_`
  önekli, API sözleşmesi): `criteria_empty`, `max_iterations_invalid`,
  `outcome_invalid`, `criterion_kind_missing`, `criterion_unknown`,
  `marker_missing`, `judge_criteria_missing`, `judge_unconfigured`.
- Kayıt (`AgentEndpoints.cs:1852-1876`) bütün loop hatalarını `400`'de taşır.

## 201.4 — Konsol: Loop bölümü (F-302)

- `loop` `form.harness` içinde kalır; `toRequest`/`fromDefinition` değişmez.
- Yeni `sections/loop-section.tsx` (harness açıkken): Loop aç/kapat (varsayılan
  `null`, K1); ölçüt satırı ekle/sil/taşı (ipucu: ilk devam isteyen kazanır);
  `completionMarker` → `marker`; `aiJudge` → kriter başına `textarea` +
  `judgeInstructions`; kod türü → açıklama; yargıç satırı (model ya da "ayarlı değil"); `MaxIterations`
  (yer tutucu `10`), `FreshContextPerIteration`, iki seçici (`FailRun` ipucu:
  son tur bir kez daha değerlendirilir).
- Listede olmayan kayıtlı tür "kayıtlı değil" diye kalır; uç hata verse de.
- Tür değişince yeni türün okumadığı alan temizlenir; yükleme/kayıt silmez.
- 🚨 Etiket "Max iterations per request"ten ayrışır (Playwright strict mode).
- Harness kapanırsa loop kaydedilmez; bir satır söyler. Sürüm farkı loop'u
  çizer. Metin `locales/{en,tr}/agents.ts` (K-228). Loop + `JsonSchema`
  kısıtlanmaz (K-889).

## 201.5 — Onay denetim izi: ölçüldü, bu fazda değişmez

Olay yükü (`RecordingLoopEvaluator.cs:337-377`): `iteration`, `continued`,
`continuedBy`, `hasFeedback`, `failedCriterion`, `ceilingReached`, `failsRun`.
Yok: geri bildirim metni (bilinçli), sıra gelmeyen ölçütün kararı, yargıç
modeli. Agent sürümü run'dadır (`RunRecord.cs:107`); model başına döküm kalıcı
değildir (K-893). Onay ölçütü "kim = yargıç modeli + agent sürümü" ister.

---

## Planlanan Public API

> Taslak. Hepsi yeni üye/tip: **bugün eklemek bedava / `1.0.0` GA sonrası
> kırıcı** (Açık Soru 4'te mevcut aşırı yüklemeye parametre de).

```csharp
// Tracon.Core — TraconBuilderExtensions
public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind,
    Func<IServiceProvider, Microsoft.Agents.AI.LoopEvaluator> factory);

// Tracon.AspNetCore — JudgeModel null: no judge binding, aiJudge does not compile
public sealed record LoopCriterionCatalogResponse
{ public required IReadOnlyList<LoopCriterionKindResponse> Kinds { get; init; }
  public LoopJudgeModelResponse? JudgeModel { get; init; } }
public sealed record LoopCriterionKindResponse
{ public required string Kind { get; init; } public required bool BuiltIn { get; init; }
  public string? Description { get; init; } }
public sealed record LoopJudgeModelResponse
{ public required string Provider { get; init; } public required string Model { get; init; } }
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/loop-criteria` | Reader | Türler ve yargıç modeli |
| `POST` | `/api/agents/validate` | bugünkü | Loop hatası başına yollu mesaj |
| `PUT` | `/api/agents/{name}` | bugünkü | `400` bütün loop hatalarını taşır |

### Arayüz payı

Ölçüldü (2026-10-12, `wwwroot` Brotli → gzip -9): **199,8 KB**, bütçe 250 KB.
Derleme zeminden önceki ağaca aittir; başta `npm run build` ile yeniden
ölçülür. Yeni bağımlılık yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Core/
├── TraconBuilderExtensions.Evaluation.cs
└── Compilation/{TraconLoopEvaluatorRegistration,LoopEvaluatorRegistry,AgentDefinitionCompiler,AgentDefinitionValidator}.cs
src/Tracon.AspNetCore/
├── Contracts/LoopCriterionCatalogResponse.cs   (yeni)
└── Endpoints/CatalogEndpoints.cs
src/Tracon.UI/frontend/src/
├── screens/agent-editor/sections/{loop-section (yeni),harness-section}.tsx
├── screens/agent-editor/{use-agent-editor.ts,agent-editor.test.tsx}
├── screens/agent-detail.tsx · full-replace-editors.test.tsx
└── locales/{en,tr}/agents.ts
samples/Tracon.Api/Program.cs                   (fabrikalı örnek ölçüt)
```

---

## Hata Modları ve Testler

> Seviye kuralı: [`test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Fabrikalı ölçüt DI servisine ulaşamaz | Fonksiyonel | `LoopEvaluatorFactoryTests` |
| 2 | `IAgentCatalog` isteyen fabrika döngüye girer | Fonksiyonel (reviewer çocuk run, ebeveyn bağı) | `LoopEvaluatorFactoryTests` |
| 3 | **Başka kiracı:** ölçüt ilk kiracıyı yakalar | Fonksiyonel (`UseTenancy`, iki kiracı; agent + workflow) | `LoopEvaluatorTenantTests` |
| 4 | **Eşzamanlılık:** paralel ilk derleme fabrikayı iki kez çağırır | Birim (sayaç) | `LoopEvaluatorRegistryTests` |
| 5 | **Alt sistem hatası:** fabrika atar; hata önbelleğe girer, tür adlanmaz | Fonksiyonel (`400`; düzelince geçer) | `LoopEvaluatorFactoryTests` |
| 6 | Fabrika yolunda yinelenen tür/gölgeleme kaçar | Birim | `LoopEvaluatorRegistryTests` |
| 7 | Anlık görüntü yeni aşırı yüklemeyi görmez | Birim | `TraconBuilderRegistrationSnapshotTests` |
| 8 | Uç fabrikayı çağırır, rol/kapsam dışı açılır, yargıcı uydurur | Fonksiyonel | `LoopCriteriaEndpointTests` |
| 9 | Doğrulama yalnız ilk hatayı ya da yolsuz mesaj döner | Fonksiyonel (üç hata → üç yollu mesaj, `400` dahil) | `AgentValidationLoopTests` |
| 10 | Doğrulayıcı ile derleyici farklı kural uygular | Birim | `LoopEvaluatorRegistryTests` |
| 11 | **Boş/aşırı girdi:** boş `criteria`, `maxIterations` 0 ve `int.MaxValue`, boş kriter | Fonksiyonel | `AgentValidationLoopTests` |
| 12 | Editör kod türünü, yok sayılan alanı, `modes`'u siler | Vitest (derin `Complete`) | `full-replace-editors.test.tsx` |
| 13 | Uç hatasında tür düşer; sıra/tür değişimi yanlış satıra gider; harness kapanınca loop sessiz düşer | Vitest | `agent-editor.test.tsx` |
| 14 | Sürüm farkı loop'u göstermez | Vitest | `agent-detail.test.tsx` |
| 15 | Konsoldan kurulan loop koşmaz | E2E | agent editörü E2E — başta ölçülür |
| 16 | **İptal:** fabrikalı ölçüt değerlendirirken run iptal edilir | Fonksiyonel | `LoopEvaluatorFactoryTests` |
| 17 | Üretilen istemciler uç ve tipleri taşımaz | Paket | NSwag/TS üretim kapıları |
| 18 | Reviewer deseni kendi şemalı kararını, kodla hesaplanan ek girdiyi ve run başına durumu (`AdditionalProperties`) taşıyamaz | Fonksiyonel (sahte sağlayıcı, iki tur) | `ReviewerLoopEvaluatorTests` |

---

## Manuel Kabul Case'leri

> ID'ler kapanışta alınır.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama, OpenAI açık | Loop → `aiJudge` + iki kriter, `MaxIterations` 3, `FailRun` → kaydet → `GET` | `harness.loop` değerleri taşır |
| 2 | Fabrikalı örnek ölçüt | Tür seçiciyi aç | Kod türü ve açıklaması — 👤 görsel |
| 3 | OpenAI kapalı | `aiJudge` → Doğrula | `loop_judge_unconfigured`, yollu |
| 4 | — | Üç hatalı ölçüt → Doğrula | Üç yollu mesaj |
| 5 | Fabrikalı ölçütlü agent | Run → olaylar | `continuedBy` kod türünü adlandırır |
| 6 | API ile kayıtsız türlü tanım | Konsolda aç → kaydet | Tür ve alanlar korunur |
| 7 | Loop'lu agent | `MaxIterations` 3 → 4 → sürüm farkı | Loop satırı vurgulu — 👤 görsel |
| 8 | Örnek uygulamada kendi şemalı reviewer ölçütü ve kodla hesaplanan bir ön kontrol | İlk turda `fail`, ikinci turda `pass` dönen sahte model ile koş | İki `LoopIterationCompleted`; geri bildirim reviewer bulgularını taşır; run `Completed` |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Fabrika ne zaman çağrılır? | A: ilk derlemede bir kez · B: kurulumda (aday) · C: her derlemede | **A** — B döngüye girer; C örneği önbelleğe bağlar, kiracıyı yakalamaya davet eder |
| 2 | Kod türleri nasıl listelenir? | A: `GET /api/loop-criteria` · B: serbest metin | **A** — serbest metin yazım hatasını kayda kadar gizler |
| 3 | Yargıç modeli konsolda? | A: salt okunur · B: `LoopCriterion.JudgeModel` | **A** — K-706 kalır; kendi modelli reviewer F-301'dir |
| 4 | Kod türüne açıklama? | A: iki aşırı yüklemeye `string? description = null` · B: yok | **A** — bugün bedava |
| 5 | Doğrulama ayrıntısı? | A: yollu loop adımı · B: bugünkü tek mesaj | **A** — F-302'nin raporu budur |
| 6 | Tür değişince eski alanlar? | A: temizlenir · B: kalır | **A** — farkta hayalet alan kalmaz |
| 7 | Onay denetim izi? | A: ayrı aday (F-411) · B: olaya `judgeModel` şimdi | **A** — iz onay ölçütüyle tasarlanır; alan sonradan eklemelidir |

---

## Bitiş Ölçütleri (DoD)

- [x] Fabrikalı ölçüt DI servisine ve katalogdaki agent'a ulaşır; döngü yok (test 1-2) — `LoopEvaluatorFactoryTests` (4) · `ReviewerLoopEvaluatorTests` (1)
- [x] İki kiracıda ölçüt kendi run'ının kiracısını okur (test 3) — `LoopEvaluatorTenantTests`: fabrika `["tenant-a"]`, değerlendirme `["tenant-a","tenant-b","tenant-b"]` (workflow adımı dahil)
- [x] `GET /api/loop-criteria` türleri ve yargıç modelini döner; fabrika çağrılmaz (test 8) — `LoopCriteriaEndpointTests` (5); örnek uygulama çıktısı aşağıda
- [x] `POST /api/agents/validate` loop hatası başına yollu mesaj döner (test 9) — `AgentValidationLoopTests` (13; ilk 11 vakanın 9'u temel commit'te kırmızı)
- [x] Konsol Loop'u kurar ve kaydeder; run koşar (test 15); değiştirmeden kayıt `loop`'u korur (test 12) — E2E `Loop_built_in_the_console_is_saved_read_back_and_runs` · `full-replace-editors.test.tsx`
- [ ] Dört doğrulama kapısı sıfır uyarı — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — birleştirmeden sonra `main`'de koşulur (koordinatör kararı, 2026-10-13); bu dalda `ic-dongu` yeşil, tam çözüm testi dahil
- [x] `samples/Tracon.Api` ile gerçek `run` (`openai-responses`, konsoldan `aiJudge` + fabrikalı ölçüt); çıktı belgeye yazıldı — aşağıdaki tablo
- [x] `secret` taraması boş döndü — `kapi.py tarama`: temiz
- [x] Manuel kabul case'leri iki alana eklendi; otomatikleştirilebilenler koşuldu — `MT-CORE-143`…`146`, `MT-UIAG-059`…`062`
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok; üç 🟡 ve iki 🟢 düzeltildi (§ Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` temiz (build + içerik + bağlantı + ağırlık; 1056 sayfa, kırık bağlantı yok)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — JS gzip -9: 204 697 B → 208 567 B (**+3 870 B**, 203,7 KB / 250 KB)
- [x] `CHANGELOG.md` `[Unreleased]` yeni yüzeyi adıyla anar

### Örnek uygulama koşumu — gerçek çıktı (2026-10-13, lane B, port 5181)

Bellek içi depolar (`Tracon__PostgreSql__ConnectionString=` vb. boş verildi; paralel
oturumla paylaşılan veritabanına yazılmadı). Önce Echo kipi
(`Tracon__Providers__OpenAI__ApiKey=`), sonra OpenAI kipi.

| Kontrol | Gerçek çıktı |
|---|---|
| `GET /api/loop-criteria` (Echo) | dört yerleşik tür sabit sırada + `{"kind":"draft-reviewed","builtIn":false,"description":"The draft-reviewer agent approves the draft. A word count and a heading check are computed in code first."}`; `"judgeModel": null` |
| `GET /api/loop-criteria` (OpenAI) | `"judgeModel": {"provider": "openai", "model": "gpt-5.4-mini"}`; `providerSettings` yok |
| `POST /api/agents/validate` — planın komutu | `valid: false`; üç mesaj: `loop_max_iterations_invalid` @ `harness.loop.maxIterations` · `loop_marker_missing` @ `harness.loop.criteria[0].marker` · `loop_criterion_unknown` @ `harness.loop.criteria[1].kind` |
| Aynı gövde `POST /api/agents` | `400 "Definition invalid"`; `detail` üç mesajın üçünü de taşır |
| `aiJudge`, Echo kipi (yargıç yok) | tek mesaj `loop_judge_unconfigured` @ `harness.loop.criteria[0].kind` |
| `reviewed-writer` run (Echo, `echo-reviewer-1`) | run `Completed`; `LoopIterationCompleted` #1 `{"continued":true,"continuedBy":"draft-reviewed","hasFeedback":true}`, #2 `{"continued":false}`; `GET /runs/{id}/tree`: iki `draft-reviewer` run'ı, ikisi de `parent = root = writer run`, `depth 1`, `16` / `57` token; reviewer girdileri `Round 1. … Previous findings: none.` ve `Round 2. … Previous findings: The draft has no heading. -> …` |
| Konsoldan kurulan loop (Playwright, `openai-responses` / `gpt-5.4-mini`): `aiJudge` (iki ölçüt) + `draft-reviewed`, tavan 3, `FailRun` | Doğrula: `Valid`; kaydet → `GET` `harness.loop` = `[{"kind":"aiJudge","judgeCriteria":[…2…]},{"kind":"draft-reviewed"}]`, `maxIterations: 3`, `onExhausted: "FailRun"` |
| Bu agent'la gerçek run | `Completed`, `3947` token (`1024` cache), maliyet `0.00067175 + 0.000472 + 0.0000256` USD; iki iterasyon: `aiJudge` geçti, `draft-reviewed` #1'de devam istedi (bulgu: taslak iki kez yazılmış), #2'de durdu; iki reviewer çocuk run'ı `277` / `355` token |

**Gerçek koşumda bulunan desen hatası (düzeltildi).** İlk turun metni akışta iki kez
göründü ve reviewer bunu yakaladı. Ölçüldü: harness turu bir plan ya da ilk sürüm
yazıp sonra son metni yazabilir; `LastResponse.Text` turun **bütün** mesajlarını
birleştirir (F-329 notu). Reviewer deseni (XML `<example>`, örnek uygulama, test)
taslağı artık son asistan mesajından okur. Davranış Tracon kusuru değil, desenin
okuma yeriydi; site bunu yazar.

### Doğrulama komutları

```bash
curl -s http://localhost:5081/tracon/api/loop-criteria
# iki hatalı ölçüt + maxIterations 0 → üç yollu mesaj
curl -s -X POST http://localhost:5081/tracon/api/agents/validate -H 'Content-Type: application/json' \
  -d '{"name":"w","model":{"provider":"echo","model":"e"},"harness":{"loop":{"criteria":[{"kind":"completionMarker"},{"kind":"x"}],"maxIterations":0}}}'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Fabrika hatası ilk derlemeye kayar | XML yazar; tür çakışması kurulumda kalır |
| Operatör `aiJudge` maliyetini görmez | Yargıç satırı, `FailRun` ipucu |
| Faz 195 / planlı Faz 200 ile dosya çakışması | Önkoşul; sıra bakımcıda |
| Açıklamaya gizli bilgi yazılır | XML: açıklama görünürdür (K-059) |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez**.
> Uygulama oturumunda yazıldı, denetimden sonra (2026-10-13) güncellendi.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | Açık Soru 4-A: iki aşırı yüklemeye `string? description = null` | **Dört** aşırı yükleme: `(kind, evaluator)`, `(kind, evaluator, description)`, `(kind, factory)`, `(kind, factory, description)`; hiçbirinde opsiyonel parametre yok | `RS0026` (PublicApiAnalyzers) birden çok aşırı yüklemede opsiyonel parametreyi reddeder; repo deseni `AddTool(AIFunction)` / `AddTool(AIFunction, configure)`'dur. Çağıran kodu aynı kalır (`AddLoopEvaluator("k", f, "açıklama")` derlenir) |
| 2 | § 201.3 sekiz taslak kod | + `loop_judge_unavailable` (dokuzuncu) | Doğrulayıcının loop adımı yapısal sorun yoksa yargıç istemcisini derleyicinin kiracı-farkında üyesiyle (`CreateJudgeChatClientAsync`) kurar; egress'in yasakladığı ya da kayıtsız yargıç sağlayıcısı yollu raporlanır. Önceden yolsuz `compilation_error`'du (K-874: "doğrulama raporunda görünür"); `SecondaryModelEgressTests` yeni kodu bekler |
| 3 | — | `judgeCriteria`'daki **her** boş girdi `loop_judge_criteria_missing` alır, yolu girdiye kadar (`…judgeCriteria[j]`); boş liste `…judgeCriteria` | Kural tek kaynakta (`FindProblems`); önceden `[""]` ve `["ok?","  "]` derleniyor, yargıca metinsiz ölçüt gidiyordu. Uygulamada önce yalnız tamamen boş liste reddedildi; denetim 🟢2 ile kural her girdiye genişledi (K-916). Konsol textarea'sı boş satırları odak çıkınca atar (Vitest) |
| 4 | "`TraconCompilationException` türü adlandırır" | Mesaj türü ve istisna **tipini** adlandırır; istisnanın kendi mesajı yalnız host loguna gider | Mesaj yönetim API'sine (`400` `detail`, doğrulama raporu) ulaşır; fabrikanın mesajı yapılandırmadan okuduğunu taşıyabilir (K-059 ruhu). İç istisna `InnerException`'da durur |
| 5 | Registry'ye erişim tasarlanmamıştı | `AgentDefinitionCompiler.GetLoopEvaluators()` (internal metot); doğrulayıcı ve uç onu kullanır | Yeni DI kaydı ve doğrulayıcı kurucusuna parametre eklenmedi (Faz 204 aynı kurucuya dokunuyor). Özellik `IDE0032` (auto property) verip `_loopEvaluators`'ı kullanan `AgentDefinitionCompiler.ChatOptions.cs`'i değiştirmeyi gerektirirdi |
| 6 | — | Fabrika kendi türünü derlemeye kalkarsa (`lock` yeniden girişi) `TraconCompilationException` | `lock` aynı iş parçacığında yeniden girilebilir; koruma olmadan sonsuz özyineleme olurdu. `LoopEvaluatorRegistryTests` kanıtlar |
| 7 | `GET /api/loop-criteria` `description`'ı kod türü için | Yerleşik türler de İngilizce bir açıklama taşır | API tüketicisi (konsol dışı) türün ne yaptığını aynı uçtan okur; konsol yerleşik türler için kendi `en`/`tr` metnini gösterir (K-232) |
| 8 | Ekran görüntüsü: `ui.md` § Agents | Yeni `agent-editor-loop.png` (+ `-light`); üretici bu ekranı landmark'a kaydırır (`ScrolledToLandmark`) | Loop paneli ilk görünüm alanının altında kalıyor; diğer ekranlar eskisi gibi tepeden çekilir |
| 9 | Site § loop yalnız yeni yüzey | `concepts/agents.md`'de workflow satırının sınıf taşımadığını söyleyen bayat cümle düzeltildi | F-314 workflow satırına `loop_unsatisfied` + `LoopUnsatisfied` yazıyor (`HarnessLoopTests.A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion`) |
| 10 | Manuel case'ler: sekiz satır | Dört `CORE` (`143`…`146`) + dört `UIAG` (`059`…`062`); plan case 3 ve 4 tek case'te (`MT-CORE-144`), 5 ve 8 tek case'te (`MT-CORE-145`); fabrika hatası (`MT-CORE-146`) eklendi | `docs/manuel-test/*.md` ağaç bütçesi (2 556 000 B) ilk yazımla 1 456 B aştı; case'ler sıkıştırıldı, içerik silinmedi. Kapanışta `01`/`02`/`24` spesifikasyonlarındaki 49 tarihli koşum notu (`Gerçek sonuç`, `Koşuldu`) `docs/arsiv/manuel-test-kosum-2026-09/`'daki eşlerine **taşındı** (11 690 B); boşluk 12 225 B |
| 11 | Reviewer deseni `LastResponse.Text` okur | Taslak turun **son mesajından** okunur (örnek, örnek uygulama, test) | Gerçek `openai-responses` koşumunda bir harness turu metni iki kez yazdı; `Text` turun bütün mesajlarını birleştirdiği için reviewer çift taslak gördü (yukarıdaki tablo). Örnek derleme kapısı (`ExampleCompilationTests`) LINQ ve tip bildirimini almadığı için XML örneği `DelegateLoopEvaluator` ile tek blok oldu |
| 12 | Uç sayısı belgeleri | Yedi yerde `168` → `169` işlem (`README.md`, iki paket README'si, `http-api.md`, `capabilities.md`, `getting-started`, `reference/compatibility.md`, ana sayfa) | `check-content.mjs` sayıyı OpenAPI belgesinden doğrular. `src/Tracon.AspNetCore/README.md` değiştiği için `--site-denetle`'nin `paket-readme` kuralı `packages.md`'yi ister; o sayfa işlem sayısı taşımaz ve paket tanımı değişmedi — kural `--site-gerekce-yazildi` ile bu gerekçeyle geçildi |
| 13 | Karar sayısı: üç aday | Beş `K-*`: K-914 (fabrika), K-915 (katalog), K-916 (doğrulama kuralı), K-917 (fabrika mesajı, güvenlik), K-918 (editörün alan koruması, kalıcı veri) | Denetim `loop_judge_unavailable`'ı sözleşme değişimi saydı; fabrika mesajı ve tam-PUT koruması dört kategoriden birine girer |

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen adaylar (`public-api`): fabrikanın çağrılma
> anı ve kiracı kuralı, ölçüt kataloğu ucu, loop doğrulama kodları.
Plan onayı (2026-10-13) Açık Soruların yedisini de önerisiyle kapattı (kullanıcı kararı).

- **K-914** *(public-api, kullanıcı kararı 1-A, 4-A)* — fabrikalı kayıt: ilk derlemede, tür başına bir
  kez başarılı; atan fabrika önbelleğe girmez; kiracı değerlendirme anında okunur; dört aşırı yükleme (`RS0026`).
- **K-915** *(public-api, kullanıcı kararı 2-A, 3-A)* — `GET /api/loop-criteria`: kök seviye, Reader +
  `AgentsRead`; yalnız ad döner, `ModelBinding` dönmez.
- **K-916** *(public-api, kullanıcı kararı 5-A)* — tek kural kümesi, dokuz `loop_` kodu ve yol biçimi;
  `loop_judge_unavailable` eski `compilation_error`'ın yerine; her boş `judgeCriteria` girdisi reddedilir.
- **K-917** *(güvenlik)* — fabrika istisnasının mesajı yönetim API'sine gitmez.
- **K-918** *(kalıcı-veri, kullanıcı kararı 6-A)* — editör yalnız tür değişiminde temizler; yükleme/kayıt
  her alanı, listelenmeyen türü ve kanonik olmayan yazımı korur.
- **Açık Soru 7-A** *(kullanıcı kararı, kategori dışı)* — onay denetim izi F-411'de kaldı; olay yükü değişmedi.
- **Kategori dışı (faz içi):** yerleşik türlerin katalog açıklaması İngilizce Tracon metnidir (sapma #7);
  registry'ye erişim `GetLoopEvaluators()` iç metodudur (sapma #5); ekran görüntüsü üreticisi tek ekranı
  landmark'a kaydırır (sapma #8).

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

```csharp
// Tracon.Core — TraconBuilderExtensions (MAAI001 bastırması üyelere dar)
public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind, Microsoft.Agents.AI.LoopEvaluator evaluator);
public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind, Microsoft.Agents.AI.LoopEvaluator evaluator, string? description);
public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind, Func<IServiceProvider, Microsoft.Agents.AI.LoopEvaluator> factory);
public static ITraconBuilder AddLoopEvaluator(this ITraconBuilder builder, string kind, Func<IServiceProvider, Microsoft.Agents.AI.LoopEvaluator> factory, string? description);

// Tracon.AspNetCore — Contracts/LoopCriterionCatalogResponse.cs
public sealed record LoopCriterionCatalogResponse
{ public required IReadOnlyList<LoopCriterionKindResponse> Kinds { get; init; }
  public LoopJudgeModelResponse? JudgeModel { get; init; } }
public sealed record LoopCriterionKindResponse
{ public required string Kind { get; init; } public required bool BuiltIn { get; init; }
  public string? Description { get; init; } }
public sealed record LoopJudgeModelResponse
{ public required string Provider { get; init; } public required string Model { get; init; } }
```

HTTP: `GET /api/loop-criteria` (`TraconListLoopCriteria`, Reader, `AgentsRead`) — yeni.
`POST /api/agents/validate` ve kayıt yolu: sözleşme şekli değişmedi (`ValidationMessage.Code` + `Path`),
yeni kodlar.

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Core/
├── TraconBuilderExtensions.Evaluation.cs          (dört AddLoopEvaluator aşırı yüklemesi, reviewer <example>)
├── PublicAPI.Unshipped.txt
└── Compilation/
    ├── TraconLoopEvaluatorRegistration.cs         (fabrika + açıklama taşır)
    ├── LoopEvaluatorRegistry.cs                   (FindProblems, CustomCriterion, Kinds, JudgeModel)
    ├── AgentDefinitionCompiler.cs                 (GetLoopEvaluators(), registry'ye services)
    ├── AgentDefinitionValidator.cs                (partial + tek çağrı satırı)
    └── AgentDefinitionValidator.Loop.cs           (yeni: loop adımı, loop_judge_unavailable)
src/Tracon.AspNetCore/
├── Contracts/LoopCriterionCatalogResponse.cs      (yeni: üç kayıt tipi)
├── Endpoints/CatalogEndpoints.cs                  (GET /api/loop-criteria)
├── PublicAPI.Unshipped.txt · README.md (169 işlem)
src/Tracon.UI/frontend/src/
├── screens/agent-editor/sections/loop-section.tsx (yeni)
├── screens/agent-editor/sections/harness-section.tsx · use-agent-editor.ts · ../agent-editor.tsx
├── screens/agent-detail.tsx                       (Loop fark tablosu, formatLoopCriteria)
├── locales/{en,tr}/agents.ts
└── testler: agent-editor.test.tsx · agent-detail.test.tsx · full-replace-editors.test.tsx
src/Tracon.Client/Generated/* · packages/tracon-client/src/schema.ts · docs/openapi/tracon.json   (üretilen)
src/Tracon.Core/buildTransitive/Tracon.AgentMap.md · src/Tracon/README.md · README.md
samples/Tracon.Api/{DraftReviewCriterion.cs (yeni), EchoModelProvider.cs, Program.cs}
tests/Tracon.Core.UnitTests/{Compilation/LoopEvaluatorRegistryTests.cs, Hosting/TraconBuilderRegistrationSnapshotTests.cs, Architecture/public-surface-baseline.txt}
tests/Tracon.AspNetCore.FunctionalTests/{LoopEvaluatorFactoryTests, LoopEvaluatorTenantTests, ReviewerLoopEvaluatorTests, LoopCriteriaEndpointTests, AgentValidationLoopTests}.cs (yeni) · SecondaryModelEgressTests.cs
tests/Tracon.Ui.E2ETests/{Ui/AgentTests.cs, DocumentationScreenshotTests.cs}
docs-site/: concepts/agents.md · ui.md · capabilities.md · http-api.md · getting-started/index.md · reference/compatibility.md · index.mdx · llms*.txt · screenshots/* (+ agent-editor-loop{,-light}.png) · scripts/landing-captures.mjs
CHANGELOG.md · docs/manuel-test/{00,01,02,10,24}-*.md · docs/arsiv/manuel-test-kosum-2026-09/{01,02,24}-*.md
docs/KARARLAR.md (K-914…K-918) · docs/ADAYLAR.md (F-410, F-411 devri; F-422) · docs/200-*.md (devir) · docs/hafiza/{maf-harness-ve-loop,elle-kosum-ortami,dokumantasyon}.md
tests/Tracon.AspNetCore.FunctionalTests/QueuedRunParameterTests.cs · docs/arsiv/ERTELENEN-ADAYLAR.md   (F-423, kusur-giderme)
```

### Testler (gerçekleşen)

| Sınıf | Seviye | Sayı | Ne kanıtlar |
|---|---|---|---|
| `LoopEvaluatorRegistryTests` | Birim | 38 vaka/TFM (25 → 38) | Eşzamanlı ilk derleme tek çağrı (kilitsiz sürümde kırmızı), atan fabrika önbelleğe girmez ve mesajı API'ye taşımaz, `null` dönüş, yeniden giriş, sağlayıcı, yinelenen/gölgeleyen fabrika kaydı fabrika çağrılmadan reddedilir, tür listesi, yargıç modeli, `FindProblems` ↔ `Build` eşliği, boş girdi kendi yolunda, `int.MaxValue` |
| `TraconBuilderRegistrationSnapshotTests` | Birim | +3 vaka (dört aşırı yükleme) | Fabrika aşırı yüklemesi de **instance** tanımlayıcı yazar |
| `LoopEvaluatorFactoryTests` | Fonksiyonel | 4 | DI servisi; `IAgentCatalog`'lu reviewer çocuk run'ı (ebeveyn, kök, derinlik, ağaç bütçesi `StartedRuns 2`, `ConsumedTokens 30`, run usage'ları ayrı); fabrika hatası `400` + düzelince `201`; değerlendirmede iptal → `Canceled` |
| `LoopEvaluatorTenantTests` | Fonksiyonel | 1 | `UseTenancy`, iki kiracı + workflow adımı: değerlendirme run'ın kiracısını okur |
| `ReviewerLoopEvaluatorTests` | Fonksiyonel | 1 | Kendi şemalı karar, kodla hesaplanan ön kontrol, run başına durum; ikinci run yeniden `Round 1` |
| `LoopCriteriaEndpointTests` | Fonksiyonel | 5 | Türler + yargıç modeli, `providerSettings` sızmaz, fabrika çağrılmaz, `null` yargıç, Reader, `AgentsRead` |
| `AgentValidationLoopTests` | Fonksiyonel | 13 | Üç sorun → üç yollu mesaj; `400` hepsini taşır; boş/aşırı girdi (boş liste, boş tür, `0`, `int.MaxValue`, boş ve karışık `judgeCriteria`); `kind`'sız gövde bağlama hatası; `loop_judge_unconfigured`, `loop_judge_unavailable` (temel commit'te 11'in 9'u kırmızı) |
| `SecondaryModelEgressTests` | Fonksiyonel | 2 (güncellendi) | Yasak yargıç `loop_judge_unavailable` + yol |
| `agent-editor.test.tsx` | Vitest | +9 | Liste düşse de tür kalır; kayıtsız tür rozeti; tür değişimi temizler, taşıma alanlarıyla; yargıç ayarlı değil; harness kapanınca loop düşer; konsoldan açılan loop; kanonik olmayan yazım seçili ve korunur (eski bileşende kırmızı); liste yokken kayıt cümlesi yok (eski bileşende kırmızı); boş yargıç satırları odak çıkınca atılır |
| `agent-detail.test.tsx` | Vitest | +1 | Sürüm farkı Loop tablosu, değişen tavan vurgulu |
| `full-replace-editors.test.tsx` | Vitest | fixture | Kod türü, yok sayılan alan (`marker` aiJudge'da), `modes` derin `Complete` ile gidiş-dönüş |
| `AgentTests.Loop_built_in_the_console_is_saved_read_back_and_runs` | E2E | 1 | Konsoldan kurulan loop kaydedilir, geri okunur, run `LoopIterationCompleted` yazar |
| `DocumentationScreenshotTests` | E2E | 1 ekran | `agent-editor-loop` yakalaması |

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni değişmedi; 13 sapma § Plandan Sapmalar'da) |
| Düzeltme turu sayısı | 10 — elle sayıldı: egress testinin beklediği kod · ifade ağacında `out` (CS8198) · üç mimari taban (gecikme etiketi, riskli locator, public tip sayısı) · XML örneği derlenmedi · E2E `/events` SSE · E2E yankı metni (harness bağlamı) · `docs/manuel-test` bütçesi · site işlem sayısı (168 → 169) · denetim 🟡/🟢 · kapıyı kıran önceden var olan test yarışı (F-423, kusur-giderme) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 — tam çözüm testi yeşil; denetimin 🟡1'i yeni ekranın kusuruydu, önceki davranışı bozmadı |
| Faz kapandıktan sonra bulunan kusur | 0 (ölçüm anı: kapanış öncesi, 2026-10-13; arşiv birleştirmeden sonra) |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır; boş bırakılmaz.

Bağımsız denetçi (`643b9eee...HEAD`): 🔴 yok; fonksiyonel testler 51/51 yeşil; beş sapma
(dört aşırı yükleme, `loop_judge_unavailable`, boş `judgeCriteria`, fabrika mesajı, `GetLoopEvaluators()`)
kabul edildi; `loop_judge_unavailable` bir sözleşme değişimidir → K-916.

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| Tür seçici kanonik olmayan yazımı kaybediyor (`aijudge`, `Draft-Reviewed`): seçici ilk seçeneği gösteriyor, aynı türü seçmek değişiklik üretmiyor | 🟡 | gerçek | **Düzeltildi** — yazım kendi seçeneğidir, kayıt korur; Vitest eski bileşende kırmızı |
| `LoopCriterionKindResponse.Description` XML'i yerleşik türlerin Tracon açıklamasını anmıyor | 🟡 | gerçek | **Düzeltildi** — XML, OpenAPI belgesi ve iki istemci yeniden üretildi |
| Site reviewer cümlesi XML örneğiyle uyuşmuyor ("son asistan mesajı", "önceki bulgular") | 🟡 | gerçek | **Düzeltildi** — örnek son mesajı ve tur sayısını yapar; son asistan mesajı ve bulgular `DraftReviewCriterion`'a yollanır |
| Liste yüklenirken/düşmüşken kayıtlı kod türü için "bu host listelemiyor" cümlesi çıkıyor | 🟢 | gerçek | **Düzeltildi** — `catalogLoaded` kapısı; Vitest eski bileşende kırmızı |
| API yolunda karışık `judgeCriteria` (`["ok?","  "]`) geçerli, boş satır yargıca gidiyor; konsol `blur` süzgecinin testi yok | 🟢 | gerçek | **Düzeltildi** — her boş girdi `loop_judge_criteria_missing` @ `…judgeCriteria[j]` (K-916, CHANGELOG); birim + fonksiyonel + Vitest |
| Not: manuel indeksin Koşum sütunu ☐; site DoD satırı `npm run check` değil | not | gerçek | **Hizalandı** — `MT-CORE-143`…`145` ✅, `146` ➜ CI, `MT-UIAG-059` ✅, `060`…`062` ➜ CI; DoD satırı düzeltildi |

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler**

- `AddLoopEvaluator` dört aşırı yüklemesi (K-914): fabrika ilk derlemede, tür başına bir kez başarılı;
  atarsa önbelleğe girmez ve mesajı API'ye gitmez (K-917). Kayıt tanımlayıcısı her biçimde **instance**.
- `GET /api/loop-criteria` (K-915) ve dokuz `loop_` doğrulama kodu + yol biçimi (K-916). Kural tek
  kaynaktadır: `LoopEvaluatorRegistry.FindProblems`; derleyici ve doğrulayıcı onu okur.
- Konsol loop'u düzenler; tür değişimi dışında hiçbir alanı silmez (K-918). `harness.loop` tam-PUT kapısında
  derin `Complete` ile korunur.

**Bilinen tuzaklar**

- 🚨 Ölçüt turun cevabını **son mesajdan** okur; `LastResponse.Text` turun bütün mesajlarını birleştirir
  (`hafiza/maf-harness-ve-loop.md`). Gerçek `openai-responses` koşumunda harness turunun akışta iki kopya
  yaydığı görüldü (F-422, ölçüm bekliyor).
- 🚨 Reviewer deseninin çocuk run'ı Tracon'un derinlik ve run sayısı denetimini **kendiliğinden almaz**
  (`ChildAgentInvoker` yolunda değildir); ölçüt `Depth + 1 > MaxDepth` ve `Budget.TryReserveRun()`'ı kendisi denetler.
- 🚨 `docs/manuel-test/*.md` ağaç bütçesi dar: kapanışta 12 225 B boşluk (2 543 775 / 2 556 000), Faz 204
  birleşince yeniden ölçülür. Boşluk, `01`/`02`/`24` spesifikasyonlarındaki 49 tarihli koşum notunun
  arşive **taşınmasıyla** açıldı (11 690 B).
- `/api/runs/{id}/events` yalnız SSE'dir; Playwright MCP çıktısı ana checkout'a yazar (`hafiza/elle-kosum-ortami.md`).
- Kapanış koşumlarında önceden var olan iki kırmızı görüldü: `QueuedRunParameterTests` durum yarışı (F-423,
  temel commit'te de 8'de 7 kırmızı; ayrı commit'te düzeltildi) ve
  `PlaygroundTests.Playground_voice_mode_opens_microphone_and_shows_transcript` bir kez (izole ve önceki tam
  koşumda yeşil; F-180 sınıfı, yük altında E2E).

**Yarım kalan / devredilen işler**

- F-410 (kod ölçütüne tanım başına ayar), F-411 (onay ölçütü ve denetim izi) — ADAYLAR'da Faz 201 devriyle güncellendi.
- F-422 — `openai-responses` akış kopyası ölçümü.
- 👤 görsel kısımlar: `MT-UIAG-059`…`062`'nin görsel adımları insan bekler.
- Kapanış birleştirmeden sonra: `faz-arsivle` + `faz-damit`, `kapanis --taban 643b9eee`, site yayını.

**Sıradaki faz:** Faz 200 (workflow sürüm geçmişi) editör dosyalarını paylaşır; devir notu `docs/200-*.md`
"Bu Faza Başlarken" 5. maddededir.
