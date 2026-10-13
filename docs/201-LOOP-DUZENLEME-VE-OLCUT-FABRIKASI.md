# Faz 201 — Konsolda Loop Düzenleme ve DI'lı Ölçüt Fabrikası

> **Durum:** 📋 Planlandı (2026-10-12)
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
- [x] `POST /api/agents/validate` loop hatası başına yollu mesaj döner (test 9) — `AgentValidationLoopTests` (11; temel commit'te 9'u kırmızı)
- [x] Konsol Loop'u kurar ve kaydeder; run koşar (test 15); değiştirmeden kayıt `loop`'u korur (test 12) — E2E `Loop_built_in_the_console_is_saved_read_back_and_runs` · `full-replace-editors.test.tsx`
- [ ] Dört doğrulama kapısı sıfır uyarı — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` (kapanışta; uygulamada `ic-dongu` yeşil)
- [x] `samples/Tracon.Api` ile gerçek `run` (`openai-responses`, konsoldan `aiJudge` + fabrikalı ölçüt); çıktı belgeye yazıldı — aşağıdaki tablo
- [x] `secret` taraması boş döndü — `kapi.py tarama`: temiz
- [x] Manuel kabul case'leri iki alana eklendi; otomatikleştirilebilenler koşuldu — `MT-CORE-143`…`146`, `MT-UIAG-059`…`062`
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
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
> Uygulama oturumunda (2026-10-13) yazıldı; denetim sonrası kapanışta tamamlanır.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | Açık Soru 4-A: iki aşırı yüklemeye `string? description = null` | **Dört** aşırı yükleme: `(kind, evaluator)`, `(kind, evaluator, description)`, `(kind, factory)`, `(kind, factory, description)`; hiçbirinde opsiyonel parametre yok | `RS0026` (PublicApiAnalyzers) birden çok aşırı yüklemede opsiyonel parametreyi reddeder; repo deseni `AddTool(AIFunction)` / `AddTool(AIFunction, configure)`'dur. Çağıran kodu aynı kalır (`AddLoopEvaluator("k", f, "açıklama")` derlenir) |
| 2 | § 201.3 sekiz taslak kod | + `loop_judge_unavailable` (dokuzuncu) | Doğrulayıcının loop adımı yapısal sorun yoksa yargıç istemcisini derleyicinin kiracı-farkında üyesiyle (`CreateJudgeChatClientAsync`) kurar; egress'in yasakladığı ya da kayıtsız yargıç sağlayıcısı yollu raporlanır. Önceden yolsuz `compilation_error`'du (K-874: "doğrulama raporunda görünür"); `SecondaryModelEgressTests` yeni kodu bekler |
| 3 | — | Yalnız boş satırlardan oluşan `judgeCriteria` `loop_judge_criteria_missing` alır | Kural tek kaynakta (`FindProblems`); önceden `[""]` derleniyor ve yargıca boş ölçüt gidiyordu. Konsol textarea'sı boş satırları odak çıkınca atar |
| 4 | "`TraconCompilationException` türü adlandırır" | Mesaj türü ve istisna **tipini** adlandırır; istisnanın kendi mesajı yalnız host loguna gider | Mesaj yönetim API'sine (`400` `detail`, doğrulama raporu) ulaşır; fabrikanın mesajı yapılandırmadan okuduğunu taşıyabilir (K-059 ruhu). İç istisna `InnerException`'da durur |
| 5 | Registry'ye erişim tasarlanmamıştı | `AgentDefinitionCompiler.GetLoopEvaluators()` (internal metot); doğrulayıcı ve uç onu kullanır | Yeni DI kaydı ve doğrulayıcı kurucusuna parametre eklenmedi (Faz 204 aynı kurucuya dokunuyor). Özellik `IDE0032` (auto property) verip `_loopEvaluators`'ı kullanan `AgentDefinitionCompiler.ChatOptions.cs`'i değiştirmeyi gerektirirdi |
| 6 | — | Fabrika kendi türünü derlemeye kalkarsa (`lock` yeniden girişi) `TraconCompilationException` | `lock` aynı iş parçacığında yeniden girilebilir; koruma olmadan sonsuz özyineleme olurdu. `LoopEvaluatorRegistryTests` kanıtlar |
| 7 | `GET /api/loop-criteria` `description`'ı kod türü için | Yerleşik türler de İngilizce bir açıklama taşır | API tüketicisi (konsol dışı) türün ne yaptığını aynı uçtan okur; konsol yerleşik türler için kendi `en`/`tr` metnini gösterir (K-232) |
| 8 | Ekran görüntüsü: `ui.md` § Agents | Yeni `agent-editor-loop.png` (+ `-light`); üretici bu ekranı landmark'a kaydırır (`ScrolledToLandmark`) | Loop paneli ilk görünüm alanının altında kalıyor; diğer ekranlar eskisi gibi tepeden çekilir |
| 9 | Site § loop yalnız yeni yüzey | `concepts/agents.md`'de workflow satırının sınıf taşımadığını söyleyen bayat cümle düzeltildi | F-314 workflow satırına `loop_unsatisfied` + `LoopUnsatisfied` yazıyor (`HarnessLoopTests.A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion`) |
| 10 | Manuel case'ler: sekiz satır | Dört `CORE` (`143`…`146`) + dört `UIAG` (`059`…`062`); plan case 3 ve 4 tek case'te (`MT-CORE-144`), 5 ve 8 tek case'te (`MT-CORE-145`); fabrika hatası (`MT-CORE-146`) eklendi | `docs/manuel-test/*.md` ağaç bütçesi (2 556 000 B) ilk yazımla 1 456 B aştı; case'ler sıkıştırıldı, içerik silinmedi. Kalan pay ~700 B — paralel faz (204) eklerse ağaç taşınmalıdır |
| 11 | Reviewer deseni `LastResponse.Text` okur | Taslak turun **son mesajından** okunur (örnek, örnek uygulama, test) | Gerçek `openai-responses` koşumunda bir harness turu metni iki kez yazdı; `Text` turun bütün mesajlarını birleştirdiği için reviewer çift taslak gördü (yukarıdaki tablo). Örnek derleme kapısı (`ExampleCompilationTests`) LINQ ve tip bildirimini almadığı için XML örneği `DelegateLoopEvaluator` ile tek blok oldu |
| 12 | Uç sayısı belgeleri | Yedi yerde `168` → `169` işlem (`README.md`, iki paket README'si, `http-api.md`, `capabilities.md`, `getting-started`, `reference/compatibility.md`, ana sayfa) | `check-content.mjs` sayıyı OpenAPI belgesinden doğrular |

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Beklenen adaylar (`public-api`): fabrikanın çağrılma
> anı ve kiracı kuralı, ölçüt kataloğu ucu, loop doğrulama kodları.
> Uygulama oturumunun taslağıdır; `KARARLAR.md` satırları (K-914…K-918 aralığı)
> kapanışta, bakımcı incelemesinden sonra yazılır.

- **Fabrika çağrılma anı ve kiracı kuralı** *(public-api, plan 1-A)* — ilk derlemede, tür başına bir kez
  başarılı; atan fabrika önbelleğe girmez; kiracı değerlendirme anında okunur.
- **Ölçüt kataloğu ucu** *(public-api, plan 2-A/3-A)* — `GET /api/loop-criteria`, kök seviye, Reader +
  `AgentsRead`; yalnız ad döner, `ModelBinding` dönmez.
- **Loop doğrulama kodları** *(public-api, plan 5-A)* — dokuz `loop_` kodu ve yol biçimi
  (`harness.loop.criteria[i].<alan>`); `loop_judge_unavailable` uygulamada eklendi (sapma #2).
- **Dört aşırı yükleme** *(public-api, sapma #1)* — `RS0026`.
- **Fabrika istisnasının mesajı API'ye gitmez** *(güvenlik, sapma #4)*.

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
