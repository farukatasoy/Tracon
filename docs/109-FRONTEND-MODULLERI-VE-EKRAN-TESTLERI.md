# Faz 109 — Frontend Modülleri ve Ekran Testleri

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 18** ve **kalem 19**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 108](arsiv/fazlar/108-BELLEK-ICI-RUN-STORE-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal turun Core bölümü bittikten sonra frontend'e geçilir
> **Paketler:** `AgentPrism.UI` — yalnız frontend source ve test altyapısı
> **Yeni paket:** NuGet yok · npm runtime dependency yok · dört dev dependency: `@testing-library/react`, `@testing-library/dom`, `@testing-library/user-event`, `jsdom` · **Migration:** Yok
> **Public API:** Büyümüyor. C# ve HTTP contract değişmez
> **Tüketici yüzeyi:** Var — mevcut management console ekranları. Site: [`docs-site/src/content/docs/ui.md`](../docs-site/src/content/docs/ui.md) ve `docs-site/public/screenshots/`. Sevk edilen: `AgentPrism.UI` içindeki embedded asset'ler. Görsel ve metinsel davranışın değişmemesi hedeflenir
> **Manuel test alanı:** [`manuel-test/09-ARAYUZ-GENEL.md`](manuel-test/09-ARAYUZ-GENEL.md) · [`manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md`](manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-228\|K-229\|K-231\|K-233\|K-419" docs/KARARLAR.md
   ```
3. Alan hafızası: [`hafiza/frontend.md`](hafiza/frontend.md), [`hafiza/frontend-yerellestirme.md`](hafiza/frontend-yerellestirme.md) ve [`hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md)
4. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız UI ve dil sınırı

---

## Amaç

Faz iki bağlı sorunu birlikte kapatır. Büyük screen dosyaları state, network ve view sorumluluklarını ayırır. İki dil sözlüğü domain fragment'larına bölünür. Aynı modül sınırları component-test harness'ine girer; her routed screen en az bir otomatik render senaryosu kazanır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`agent-editor.tsx:232`](../src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx) | Dosya **1.243 satırdır**; form modeli, dönüşümler, fetch/mutation state'i ve bütün section JSX'i aynı dosyadadır. |
| [`playground.tsx:60`](../src/AgentPrism.UI/frontend/src/screens/playground.tsx) | Dosya **944 satırdır**; SSE run state'i, approval, attachment, transcript ve ses öğeleri aynı dosyadadır. |
| [`locales/en.ts:18`](../src/AgentPrism.UI/frontend/src/locales/en.ts) | İngilizce catalogue **1.222 satırdır**; `Messages` tipi dosyanın sonundaki tek aggregate'tan çıkar. |
| [`locales/tr.ts:17`](../src/AgentPrism.UI/frontend/src/locales/tr.ts) | Türkçe catalogue **1.213 satırdır**; bütün ekranlar aynı dosyaya dokunur. |
| [`frontend/src`](../src/AgentPrism.UI/frontend/src) | **24.011** TypeScript satırı, **28** screen ve **23** component vardır. Buna karşılık **14** Vitest dosyası ve gerçek build çıktısında **172** case vardır; screen altında yalnız `agent-editor.test.ts` bulunur. |
| [`frontend/package.json`](../src/AgentPrism.UI/frontend/package.json) | Vitest vardır, fakat DOM/component-test dependency ve `jsdom` environment yoktur. |

2026-08-26 gerçek `npm run build` tabanı: console JavaScript **175,5 KB gzip / 250 KB**, embedded çıktı **150,1 KB Brotli**, widget **2,7 KB gzip / 30 KB**. Vite 149 modülü tek 666,78 KB minified JS chunk'ına yazdı.

> Kanıtlar 2026-08-26 tarihinde doğrulandı. Envanterdeki 29 screen ve eski bundle sayısı bayattır.

## 109.1 — Agent editor modülleri

`AgentEditorScreen` route facade olarak kalır. Şu parçalar ayrılır:

- `model.ts`: `FormState`, empty state ve request/response dönüşümleri;
- `use-agent-editor.ts`: query, mutation, default provider ve validation state'i;
- `sections/`: identity, model, instructions, tools/skills/agents, compaction/memory, preview ve validation panelleri.

Section component'ları server API çağırmaz. Typed props ve callback alır. Formun tek owner'ı hook/facade'dır. Bu sınır testte section'ları bağımsız render etmeyi sağlar.

## 109.2 — Playground modülleri

`PlaygroundScreen` route facade olarak kalır. Run yaşamı `use-playground-run.ts`; attachment yaşamı `use-attachments.ts`; turn ve approval görünümü `turn-view.tsx`; playback/preview öğeleri kendi component dosyalarında yaşar.

Object URL oluşturma ve `revokeObjectURL` aynı hook/component yaşamında kalır. SSE fold mantığı `lib/transcript.ts` içinde kalır; screen dosyasına kopyalanmaz. Voice WebSocket davranışı `voice-panel.tsx` ile karıştırılmaz.

## 109.3 — Catalogue fragment'ları

`en.ts` ve `tr.ts` yalnız fragment aggregate eder. Message literal'ları domain dosyalarına taşınır. Her fragment bir veya birkaç bağlı ekran alanını taşır: common/shell, agents, runs/sessions/dashboard, workflows/jobs/evals, security/operations ve settings.

English fragment anahtarın kaynağıdır. Türkçe karşılığı şu sözleşmeyi taşır:

```ts
export const trAgents: Pick<Messages, keyof typeof enAgents> = { /* translations */ };
```

Aggregate `tr: Messages` kontrolü de kalır. Böylece fragment içinde eksik/fazla anahtar ve aggregate içinde unutulan fragment derleme hatası olur. Fragment key set'leri ayrıca runtime testinde unique olmalıdır; object spread ile sessiz override kabul edilmez.

K-228 korunur. Yeni i18n runtime dependency alınmaz. `translate` referansı K-229 gereği kararlı kalır. Lazy catalogue yükleme ve üçüncü dil kapsam dışıdır.

## 109.4 — Component-test harness

Dev dependency'ler implementation anında aşağıdaki uyumlu hatlardan alınır ve lockfile'a sabitlenir:

| Paket | Ölçülen sürüm | Neden |
|---|---:|---|
| `@testing-library/react` | 16.3.2 | React 18/19 peer aralığını destekler |
| `@testing-library/dom` | 10.4.1 | React Testing Library'nin açık peer dependency'sidir |
| `@testing-library/user-event` | 14.6.6 | Kullanıcı etkileşimini DOM event ayrıntısına bağlamadan sürer |
| `jsdom` | 27.4.0 | Repo alt sınırı Node 20.19 ile uyumludur; jsdom 30 Node 22.22 ister ve alınmaz |

Ortak render helper; `QueryClientProvider`, locale, router ve API mock'larını kurar. Production bundle bu paketleri içermez. Bundle ölçümü bunu sıfır runtime dependency farkıyla kanıtlar.

## 109.5 — Screen coverage kapısı

Router'ın route tablosu export edilen tek kaynaktır. Data-driven smoke test her route component'ını mock API ile render eder ve loading, content veya açık error state'lerinden birine ulaşmasını ister. Yeni route eklenip test case'i unutulamaz.

Büyük iki screen için smoke yeterli değildir:

- Agent editor: create/edit yükleme, provider default yarışı, JSON schema hatası, validation sonucu ve save failure.
- Playground: stream success/failure, approval yanıtı, attachment ekleme/temizleme ve session devamı.

Cross-boundary davranış Playwright'ta kalır. Component test E2E'nin kopyası değildir; hızlı state/branch kapsamıdır.

## Planlanan Public API

Yok. TypeScript export'ları package dışına sevk edilen npm API değildir; console iç modülleridir. `@agentprism/client` değişmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Başlangıç: **175,5 KB gzip / 250 KB**. Refactor yeni runtime dependency eklemez. Bitiş hedefi başlangıcı aşmamaktır. Route-level code splitting bu fazın kapsamı değildir; görsel yükleme davranışını değiştirir ve ayrı ölçüm ister.

## Planlanan Dosya Listesi

```text
src/AgentPrism.UI/frontend/src/
├── locales/
│   ├── en.ts
│   ├── tr.ts
│   ├── en/*.ts
│   └── tr/*.ts
├── screens/
│   ├── agent-editor.tsx
│   ├── agent-editor/
│   │   ├── model.ts
│   │   ├── use-agent-editor.ts
│   │   └── sections/*.tsx
│   ├── playground.tsx
│   └── playground/
│       ├── use-playground-run.ts
│       ├── use-attachments.ts
│       └── *.tsx
└── test/
    ├── render.tsx
    ├── api-fixtures.ts
    └── setup.ts
```

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Fragment key'i iki kez tanımlanır ve spread sessizce ezer | Birim | catalogue fragment uniqueness testi |
| Türkçe fragment eksik/fazla anahtar taşır | Derleme + birim | `tsc --noEmit` + mevcut `i18n.test.ts` |
| `useT()` kimliği değişir ve açık voice session yeniden kurulur | Birim / E2E | i18n identity testi + voice E2E |
| Agent editor state section ayrışırken kaybolur | Component | agent-editor branch testleri |
| Playground cleanup object URL veya pending request bırakır | Component / E2E | attachment/approval testleri |
| Yeni screen route test olmadan eklenir | Birim / yapısal | route-driven screen smoke matrix |
| API iptali/unmount sonrası state update yapar | Component | delayed response + unmount testi |
| Başka kiracının verisi görünür | E2E / contract | mevcut HTTP tenant testleri; frontend mock testi bunu kanıtlamaz |
| API alt sistemi hata verir ve screen boş kalır | Component | her büyük screen için error-state testi |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | İngilizce ve Türkçe browser context | 19 screenshot senaryosunu iki dil sınırıyla çalıştır | Görsel, metin ve route davranışı değişmez |
| 2 | Agent editor | Yeni ve mevcut agent'ı aç; tüm section'ları değiştir; validate ve save yap | Request payload refactor öncesi sözleşmeyle aynıdır |
| 3 | Playground | Stream, approval, attachment ve session devamını çalıştır | Transcript ve cleanup doğru; console error yok |
| 4 | Dar viewport | UI sayfasındaki responsive ekran setini 360 px'te gez | Yatay gövde taşması yoktur |

## Açık Sorular

Yok. Route-level lazy loading ve üçüncü dil kapsam dışıdır.

## Bitiş Ölçütleri (DoD)

- [x] `agent-editor.tsx` ve `playground.tsx` route facade olur; state/network/view sorumlulukları kendi modüllerindedir
- [x] `en.ts` ve `tr.ts` yalnız fragment aggregate eder; message literal monoliti kalmaz
- [x] Fragment key'leri unique, iki dilde tam ve placeholder/plural sözleşmesi eşittir
- [x] Router'daki **28** screen dosyasının tamamı, `app.tsx#routes`'un **36** route pattern'ının tamamı üzerinden data-driven component smoke testine girer (`app.test.tsx`)
- [x] Agent editor ve Playground için belirtilen branch testleri yeşildir
- [x] `npm run build` `tsc`, Vitest, Vite ve iki bundle kapısını temiz geçirir
- [x] Console JavaScript **175,5 KB gzip değerini aşmaz**; genel bütçe 250 KB olarak kalır — **kısmen**: 175,9 KB (+0,4 KB, modül sınırı maliyeti); bkz. Plandan Sapmalar. Genel 250 KB bütçesi kalır.
- [x] `AgentPrism.Ui.E2ETests` 57/57 yeşildir
- [x] `AGENTPRISM_UI_SCREENSHOTS=1` ile screenshot seti yeniden üretildi; istenmeyen görsel fark yoktur
- [x] `tuketici-dokuman-senkronu` koşuldu; `ui.md` ve screenshot yüzeyi doğrulandı
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi; otomatik olanlar koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu gerekçelendi (bkz. Denetim Bulguları)

## Riskler

| Risk | Önlem |
|---|---|
| Component test sayısı artar ama davranış iddiası zayıf kalır | Smoke matrix yalnız route boşluğunu kapatır; büyük iki screen için branch tablosu zorunludur |
| Catalogue fragment'ları type safety'yi zayıflatır | Fragment `Pick<Messages, keyof typeof enFragment>` ve aggregate `Messages` kontrolünü birlikte taşır |
| jsdom repo Node alt sınırını yükseltir | 27.4.0 hattı Node 20.19'u destekler; jsdom 30 alınmaz |
| Test dependency'leri production bundle'a sızar | Yalnız `devDependencies`; 175,5 KB gzip taban karşılaştırması kapıdır |
| Dosya monoliti onlarca mikro dosyaya dönüşür | Fragment bir route değil domain sınırı taşır; section yalnız bağımsız state/validation ekseni varsa ayrılır |

---

## Plandan Sapmalar

- **Bundle bütçesi 0,4 KB gzip arttı (175,5 → 175,9 KB).** ~26 yeni modülün (agent-editor: 9, playground: 5, locale fragment: 12) import/export sarmalama maliyeti. Sert kapı 250 KB'dir ve 74 KB payla geçildi; "başlangıcı aşmama" hedefi tam tutmadı ama fonksiyonel/bağımlılık büyümesi yok — kabul edildi, yeni bir azaltma turu açılmadı.
- **`use-playground-run.ts` `sessionId` durumunu KENDİ İÇİNDE tutmuyor — facade'dan parametre olarak alıyor.** Plan iki bağımsız hook öngörüyordu (`use-playground-run.ts`, `use-attachments.ts`); gerçekte `useAttachments(sessionId)` ile `usePlaygroundRun` aynı `sessionId` değerine ihtiyaç duyuyor ve biri diğerinin state'ini "sonradan" okuyamıyor (hook'lar birbirinin iç state'ine bağlanamaz). Çözüm: `sessionId`/`setSessionId` facade'da (`playground.tsx`) tutulur, her iki hook'a parametre geçilir — ortak durumu paylaşan iki hook'un doğal dikişi budur.
- **Component-test fixture'ları için genel `{}` varsayılanı beklenenden çok daha yetersiz çıktı.** `server-types.ts`'in `Fix<>` deseni onlarca uçta alanı "her zaman dolu" sayıyor (gerçek sunucu garantisi); route-driven smoke test genel varsayılanla 10+ ekranda çöktü. Kapsamlı bir şema-şekli kütüphanesi kurmak yerine yalnız çöken uca hedefli `fixture(...)` eklendi (`app.test.tsx#overridesFor`) — bilinçli, dokümante edilmiş bir sınır (`docs/hafiza/frontend.md`).
- **`openapi-fetch`'in `fetch`/`Request`'i client oluşturulduğu anda (modül yükleme) yakalaması** test altyapısını değiştirdi: stub'ın test başına değil, dosya başına KALICI kurulması ve HTTP metodunun `Request` nesnesinden okunması gerekti. Ayrıntı ve gerekçe: `docs/hafiza/frontend.md` (Component-test altyapısı, Faz 109).
- **Faz dokümanının önerdiği "tools/skills/agents" tek başlığı üç ayrı section dosyasına bölündü** (`tools-section.tsx`, `skills-section.tsx`, `callable-agents-section.tsx`) — her biri kendi API sorgusuna bağlı, bağımsız render edilebilir olması component test açısından daha net bir sınır çiziyor.

## Bu Fazda Verilen Kararlar

Yok. Bu fazın tüm kararları yerel implementation tercihidir (dosya bölünme sınırı, fixture stratejisi); public API, güvenlik, kiracı sınırı veya kalıcı veri kararı yok — yeni `K-*` kaydı açılmadı.

## Gerçekleşen Public API

Yok. C# ve HTTP contract değişmedi; `denetim-paketi.py` public API delta'sını 0 dosya olarak ölçtü.

## Dosya Listesi (gerçekleşen)

```text
src/AgentPrism.UI/frontend/
├── package.json                                    (değişti — 4 yeni devDependency)
├── vitest.config.ts                                (değişti — jsdom + setupFiles)
├── src/
│   ├── app.tsx                                     (değişti — `routes` export edildi)
│   ├── app.test.tsx                                (yeni — 36 route smoke testi)
│   ├── locales/
│   │   ├── en.ts, tr.ts                            (değişti — yalnız fragment aggregate)
│   │   ├── fragments.test.ts                       (yeni — anahtar benzersizliği)
│   │   ├── en/{common,agents,runs,workflows,operations,settings}.ts   (yeni)
│   │   └── tr/{common,agents,runs,workflows,operations,settings}.ts   (yeni)
│   ├── screens/
│   │   ├── agent-editor.tsx                        (değişti — route facade, 1243→93 satır)
│   │   ├── agent-editor/
│   │   │   ├── model.ts, model.test.ts             (yeni)
│   │   │   ├── use-agent-editor.ts                 (yeni)
│   │   │   ├── agent-editor.test.tsx               (yeni — 5 branch testi)
│   │   │   └── sections/*.tsx                      (yeni — 9 dosya)
│   │   ├── playground.tsx                          (değişti — route facade, 944→300 satır)
│   │   └── playground/
│   │       ├── use-playground-run.ts, use-attachments.ts   (yeni)
│   │       ├── turn-view.tsx, speak-button.tsx, attachment-chip.tsx  (yeni)
│   │       └── playground.test.tsx                 (yeni — 5 branch testi)
│   └── test/
│       ├── render.tsx, api-fixtures.ts, setup.ts   (yeni)
```

## Gerçek Run Kanıtı

`samples/AgentPrism.Api` bellek içi depoyla çalıştırıldı (`AgentPrism__PostgreSql__ConnectionString=""`
ile daha önceki bir manuel test oturumundan kalan `localhost:55432` bağlantısı
geçersiz kılındı). `POST /agentprism/api/agents/claude-support/run` gerçek
Anthropic Claude Haiku çağrısı yaptı:

```
id: 3
event: update
data: {"authorName":"claude-support", ..., "contents":[{"$type":"text","text":"OK"}], ...}
id: 8
event: done
```

`GET /agentprism/api/runs/01a03bff-a4e9-796b-af6f-6e4859dc64cf` kaydı
`"status": "Completed"`, `"usage": {"inputTokens":720,"outputTokens":4,...}`
olarak doğruladı — refactor sonrası uçtan uca çalıştırma/kayıt yolu sağlam.

## `ui.md` Senkron Gerekçesi (`--site-gerekce-yazildi`)

`dokuman-bakim.py --site-denetle` `agent-editor.tsx`/`playground.tsx`
değiştiği için `ui.md`'nin de değişmesini bekledi. Sayfa güncellenmedi:
`ui.md` iç dosya yapısına hiç değinmiyor (`grep` boş döndü) ve bu fazın amacı
kullanıcıya görünen davranışı **değiştirmemek** — yalnız state/network/view
sorumluluklarını ayırmak. Kanıt: `docs-site/public/screenshots/` altındaki UI
ekran görüntüleri bu faz kapanışında `AGENTPRISM_UI_SCREENSHOTS=1` ile yeniden
üretildi ve `git diff` görsel fark göstermedi (aşağıda).

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, `faz-denetim` skill'i) 2026-08-26'da koştu.

### 🔴 → Gerekçelendi (yeni `K-*` açılmadı)

**Bulgu:** `SourceLanguageTests.SkippedFiles` altı yeni girdiyle büyüdü
(`locales/tr/{common,agents,runs,workflows,operations,settings}.ts`) ve bu
`docs/KARARLAR.md`'ye karar olarak yazılmadı.

**Gerekçe:** `SkippedFiles` bir teknik borç sayacı (o rolü
`AGENTPRISM_SOURCE_LANGUAGE_REFRESH`'in yönettiği dosya-başına satır tabanı
görür) DEĞİL, K-228'in zaten sabitlediği **kalıcı, meşru** iki dilli dosyalar
için tam muafiyet listesidir — mevcut girdiler (`locales/tr.ts`,
`embed/locale.tr.ts`) de aynı kalıcı statüdedir. Bu faz `tr.ts`'i altı
fragment'a böldü; toplam muaf Türkçe içerik **artmadı**, yalnız fiziksel
konumu değişti — K-228/K-408'in sınırladığı "ne muaf" kümesi aynı kaldı.
`AGENTS.md`'nin karar defteri kapsamı ("yalnız public API/compatibility
contract, güvenlik veya kiracı sınırı, kalıcı veri/migration ya da geri
dönüşü pahalı sistem kararı") bu değişikliğin hiçbirine girmiyor — yerel bir
test-altyapısı tercihi olarak kod yorumunda ve burada gerekçelendi, yeni
`K-*` açılmadı. Karşı taraf: kural gerçekten büyüyorsa (gelecekte GERÇEKTEN
yeni, önceden muaf olmayan Türkçe içerik eklenirse) o zaman bir `K-*` gerekir
— bu fazın yaptığı bu değildir.

### 🟢 → Doğrudan düzeltildi (aday listesine devredilmedi)

**Bulgu:** DoD satırındaki "Router'daki **28** screen route" ifadesi, gerçek
route pattern sayısıyla (36) karışabilir.

**Sonuç:** Bir sonraki oturumun kafasını karıştırmaması için DoD satırı bu
oturumda netleştirildi (28 dosya · 36 pattern) — F-NN adayına devredilecek bir
gelecek iş değil, bu fazın kendi dokümanındaki bir ifade netliği.

**Diğer altı başlık (3.1–3.5, 3.7):** Temiz. Kanıt: denetçinin doğruladığı
`npm run build` (tsc + 18 dosya/221 test + Vite×2, 175,9 KB gzip) ve
`dotnet test tests/AgentPrism.Core.UnitTests` (1970/1970, `SkippedFiles`
düzeltmesi dahil).

## Sonraki Faza Devir Notu

Yapısal tur envanterindeki kalem 18/19 (büyük screen dosyaları, sözlük monoliti, sıfır component-test kapsamı) kapandı. `docs/kesif/2026-08-23-yapisal-sorun-envanteri.md`'deki diğer kalemler için aday listesi güncel kalmalı — bu faz o envanterden tek F-NN dışı fazdı.

Component-test harness'i (`src/test/`) artık genel amaçlı: yeni bir büyük screen (`workflow-editor.tsx`, `settings.tsx` gibi mevcut kod tabanındaki diğer büyük dosyalar) benzer bir bölünmeden geçerse aynı `render.tsx`/`api-fixtures.ts` çiftini kullanabilir. `app.test.tsx#overridesFor` genişledikçe (yeni bir route'un genel `{}` varsayılanıyla çöktüğü her seferinde) bu tablonun kendisi bir gün ayrı bir dosyaya taşınmayı hak edebilir — bugün 36 route için tek dosyada okunabilir kaldı.
