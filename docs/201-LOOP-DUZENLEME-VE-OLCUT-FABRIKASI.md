# Faz 201 — Konsolda Loop Düzenleme ve DI'lı Ölçüt Fabrikası

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** onaylanmadı — plan 2026-10-12'de bakımcı talebiyle yazıldı; engelleyici kararlar § Açık Sorular'da öneriyle bekler, uygulama onaydan önce başlamaz
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

- [ ] Fabrikalı ölçüt DI servisine ve katalogdaki agent'a ulaşır; döngü yok (test 1-2)
- [ ] İki kiracıda ölçüt kendi run'ının kiracısını okur (test 3)
- [ ] `GET /api/loop-criteria` türleri ve yargıç modelini döner; fabrika çağrılmaz (test 8)
- [ ] `POST /api/agents/validate` loop hatası başına yollu mesaj döner (test 9)
- [ ] Konsol Loop'u kurar ve kaydeder; run koşar (test 15); değiştirmeden kayıt `loop`'u korur (test 12)
- [ ] Dört doğrulama kapısı sıfır uyarı — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` (`openai-responses`, konsoldan `aiJudge` + fabrikalı ölçüt); çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri iki alana eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [ ] `CHANGELOG.md` `[Unreleased]` yeni yüzeyi adıyla anar

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

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen adaylar (`public-api`): fabrikanın çağrılma
> anı ve kiracı kuralı, ölçüt kataloğu ucu, loop doğrulama kodları.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
