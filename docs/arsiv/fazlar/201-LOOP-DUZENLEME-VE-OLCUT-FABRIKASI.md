# Faz 201 — Konsolda Loop Düzenleme ve DI'lı Ölçüt Fabrikası

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A, 6-A, 7-A)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-302**, **F-301**
> **Genellik:** Kendi talimatı, kendi şeması ve kodla hesaplanan ön kontrolü olan yargıç birçok tüketici sınıfının kalite kapısıdır (içerik QA'sı, kod incelemesi, çıkarım doğrulaması). İkinci kanıt ilk ilkedir: MAF `LoopEvaluator` bağımlılık ister, Tracon'un kayıt yüzeyi DI vermez (K-218 sınıfı). Domain'e özgü ölçüt tüketici kodunda kalır
> **Karşı görüş:** "Yerleşik `aiJudge` çoğu kapıyı karşılar." Plan bunu kabul eder: `aiJudge` varsayılan kalır; fabrika yalnız onun ifade edemediği kapı içindir ve örnekleri alan bağımsızdır
> **Önkoşul:** İşlevsel önkoşul yok. [Faz 195](195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) 2026-10-12'de kapandı (ikisi `full-replace-editors.test.tsx`'i paylaşır)
> **Paketler:** `Tracon.Core`, `.AspNetCore`, `.UI` · üretilen: `Tracon.Client`, `@tracon/client`
> **Yeni paket:** Yok · **Migration:** Yok — `harness.loop` tanım blob'undadır; veri modeli değişmez
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/agents.md` § loop, `ui.md` § *Agents* + § *Editor boundaries* + ekran görüntüsü, `capabilities.md:47` (`api/`, `http-api/` üretilir) · sevk edilen: aşırı yüklemenin XML `<example>`'ı, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` (konsol) · `02-CEKIRDEK-VE-KATALOG.md` (fabrika, uç)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 5ebf02e6:docs/arsiv/fazlar/201-LOOP-DUZENLEME-VE-OLCUT-FABRIKASI.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Operatör `Loop`'u konsoldan kuramaz; editör onu yalnız taşır. Host kod ölçütüne DI ile servis veremez. Ortak yüzey Loop ölçüt sözleşmesi ve onu listeleyen, doğrulayan, düzenleyen yüzeydir.

## Bitiş Ölçütleri (DoD)

- [x] Fabrikalı ölçüt DI servisine ve katalogdaki agent'a ulaşır; döngü yok (test 1-2) — `LoopEvaluatorFactoryTests` (4) · `ReviewerLoopEvaluatorTests` (1)
- [x] İki kiracıda ölçüt kendi run'ının kiracısını okur (test 3) — `LoopEvaluatorTenantTests`: fabrika `["tenant-a"]`, değerlendirme `["tenant-a","tenant-b","tenant-b"]` (workflow adımı dahil)
- [x] `GET /api/loop-criteria` türleri ve yargıç modelini döner; fabrika çağrılmaz (test 8) — `LoopCriteriaEndpointTests` (5); örnek uygulama çıktısı aşağıda
- [x] `POST /api/agents/validate` loop hatası başına yollu mesaj döner (test 9) — `AgentValidationLoopTests` (13; ilk 11 vakanın 9'u temel commit'te kırmızı)
- [x] Konsol Loop'u kurar ve kaydeder; run koşar (test 15); değiştirmeden kayıt `loop`'u korur (test 12) — E2E `Loop_built_in_the_console_is_saved_read_back_and_runs` · `full-replace-editors.test.tsx`
- [x] Dört doğrulama kapısı sıfır uyarı — `python3 scripts/kapi.py kapanis --taban b3a24388` birleştirmeden sonra `main`'de (`6af4f395`, Faz 201 + Faz 204 birlikte) çıkış 0: tarama 4,8 sn · `--denetle` · betik testleri · ajan haritası · denetim paketi · build 86,7 sn · test 441,0 sn (38 koşum, 20 241 geçti, 19 atlandı) · pack · format 107,8 sn · site `npm run check` 33,2 sn. İlk koşum ajan haritası tazeliğinde durdu: birleştirmeden sonra `build-agent-map.mjs` koşulmamıştı (`6af4f395` yeniden üretti). Bu dalda `ic-dongu` yeşildi, tam çözüm testi dahil
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

Bağımsız denetçi (`b3a24388...HEAD`): 🔴 yok; fonksiyonel testler 51/51 yeşil; beş sapma
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
- Birleştirmeden sonra `main`'de `kapanis --taban b3a24388` yeşil, faz arşivlendi ve damıtıldı. **Site yayınlanmadı** (`scripts/site-deploy.sh` dışa açık bir iştir; bakımcı koşar) — Faz 201 ve Faz 204'ün sayfaları birlikte yayınlanır.

**Sıradaki faz:** Faz 200 (workflow sürüm geçmişi) editör dosyalarını paylaşır; devir notu `docs/200-*.md`
"Bu Faza Başlarken" 5. maddededir.
