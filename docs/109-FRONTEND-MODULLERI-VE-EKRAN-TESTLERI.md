# Faz 109 — Frontend Modülleri ve Ekran Testleri

> **Durum:** 📋 Planlandı (2026-08-26)
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

- [ ] `agent-editor.tsx` ve `playground.tsx` route facade olur; state/network/view sorumlulukları kendi modüllerindedir
- [ ] `en.ts` ve `tr.ts` yalnız fragment aggregate eder; message literal monoliti kalmaz
- [ ] Fragment key'leri unique, iki dilde tam ve placeholder/plural sözleşmesi eşittir
- [ ] Router'daki **28** screen route'un tamamı data-driven component smoke testine girer
- [ ] Agent editor ve Playground için belirtilen branch testleri yeşildir
- [ ] `npm run build` `tsc`, Vitest, Vite ve iki bundle kapısını temiz geçirir
- [ ] Console JavaScript **175,5 KB gzip değerini aşmaz**; genel bütçe 250 KB olarak kalır
- [ ] `AgentPrism.Ui.E2ETests` 57/57 yeşildir
- [ ] `AGENTPRISM_UI_SCREENSHOTS=1` ile screenshot seti yeniden üretildi; istenmeyen görsel fark yoktur
- [ ] `tuketici-dokuman-senkronu` koşuldu; `ui.md` ve screenshot yüzeyi doğrulandı
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri ilgili ailelere eklendi; otomatik olanlar koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

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

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
