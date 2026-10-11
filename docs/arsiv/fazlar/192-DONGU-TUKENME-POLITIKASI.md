# Faz 192 — Döngü Tükenme Politikası

> **Durum:** ✅ Tamamlandı (2026-10-11)
> **Plan onayı:** Bakımcı, 2026-10-11 (engelleyici kararlar sohbette alındı: API şekli, son turun değerlendirilmesi, hata sınıfı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-293**
> **Önkoşul:** Yok. [Faz 151](151-HARNESSIN-DONGU-YETENEGI.md) döngüyü kurdu; bu faz onun bitiş davranışını seçilebilir yapar
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core` · üretilen istemciler (`Tracon.Client`, `@tracon/client`) · `Tracon.UI` (yalnız iki sözlük satırı)
> **Yeni paket:** Yok · **Migration:** Yok (tanım JSON blob'unda yaşar; `run_errors.class` zaten `smallint`)
> **Public API:** Büyüyor — bir enum, iki `LoopSettings` alanı, bir istisna tipi, bir `RunErrorClass` üyesi. `1.0.0` GA'dan (K-603) önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır (2026-10-11), her dosya yalnız başlık taşıyor
> **Tüketici yüzeyi:** site: `concepts/agents.md` (döngü bölümü), `guides/write-your-own-error-classifier.md` (sınıf listesi varsa) · sevk edilen: `LoopSettings` XML `<example>`, `RunErrorClass` ve `RunEventType.LoopIterationCompleted` XML'i, OpenAPI belgesi
> **Manuel test alanı:** `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` — Faz 151 case'lerinin (`MT-CORE-123` civarı) yanına

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 4034d073:docs/arsiv/fazlar/192-DONGU-TUKENME-POLITIKASI.md
> ```
>
> Damıtıldı 2026-10-11 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir tüketici (2026-10-11 podcast hattı) yazar agent'ına bir revizyon döngüsü kurar: `aiJudge` ölçütü metni onaylamazsa agent yeniden yazar. Döngü tavana ulaşırsa ya da yargıç hata verirse bugün run **başarılı** biter ve onaylanmamış metin workflow'un sonraki düğümüne (TTS) geçer. Bu faz, tanımın sahibine iki durumda da run'ı düşürme seçeneği verir.

## Bitiş Ölçütleri (DoD)

- [x] `onExhausted: "FailRun"` + ulaşılamayan işaret → `run` `Failed`, `error.class == "LoopUnsatisfied"`, sağlayıcı çağrısı tam `maxIterations` — `FailRun_on_exhaustion_fails_the_run_after_exactly_MaxIterations_turns`; canlı `MT-CORE-132`
- [x] Ölçüt yalnız son turda karşılanırsa `FailRun` modunda run `Completed` — `FailRun_on_exhaustion_completes_a_run_whose_last_turn_meets_the_criterion`
- [x] `onCriterionError: "FailRun"` → run orijinal hatanın sınıfıyla `Failed`; olay `failedCriterion` + `failsRun` — `FailRun_on_criterion_error_fails_the_run_with_the_criterions_own_class`
- [x] Alanlar verilmezse `HarnessLoopTests`'in mevcut on testi değişmeden yeşil — 19/19; canlı `MT-CORE-133`
- [x] Akışlı yolda içerik + hata çerçevesi + `Failed` — `FailRun_on_exhaustion_streams_the_last_turn_and_then_fails_the_run` (süreç içi akış); SSE çerçevesi canlı `MT-CORE-134`
- [x] Workflow sonraki düğümü koşmaz; workflow run sınıfı ölçüldü ve "Plandan Sapmalar"a yazıldı — `A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion`; sapma #2, F-314
- [x] `RunErrorClassContractTests`, `RunEventPayloadContractTests`, OpenAPI tazeliği, iki sözlük yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban 07afea6b`, commit'li ağaç `2dfd7826`: ✅ EXIT 0 (build 91 sn · test 437 sn · pack 7 sn · format 107 sn · site 37 sn)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — `MT-CORE-132`…`134`, `136` (`gpt-5.4-mini`)
- [x] `secret` taraması boş döndü — kapanış kapısının `tarama` adımı
- [x] Manuel kabul case'leri `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-CORE-132`…`136` (135 ➜ CI)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — tek 🔴 düzeltildi (aşağıda)
- [x] `docs-site/` güncellendi (`concepts/agents.md`: iki alan, son tur bedeli, `MaxIterations = 1` farkı, akışlı yol cümlesi); `npm run build` + `check-links.mjs` temiz — `npm run check`
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — +50 B gzip (201522 → 201572)

### Doğrulama komutları

```bash
# FailRun tükenme — örnek uygulama (port ve anahtar kurulumu: docs/manuel-test/00-INDEKS.md)
curl -s -X PUT http://localhost:5081/tracon/api/agents/loop-gate -H 'Content-Type: application/json' \
  -d '{"name":"loop-gate","model":{"provider":"openai","model":"gpt-5-mini"},"instructions":"Answer briefly.",
       "harness":{"loop":{"criteria":[{"kind":"completionMarker","marker":"NEVER-WRITTEN"}],"maxIterations":2,"onExhausted":"FailRun"}}}'
curl -s -X POST http://localhost:5081/tracon/api/agents/loop-gate/run -H 'Content-Type: application/json' -d '{"message":"hi"}'
# → run Failed, error.class LoopUnsatisfied
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*HarnessLoopTests*"
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | `TraconLoopUnsatisfiedException` üyeleri `required` | `init`, `required` değil (`AgentName`/`UnsatisfiedCriterion` `string?`) | Diğer Tracon istisnaları gibi üç standart kurucu (CA1032 deseni) kullanılabilir kalsın; `required` üye parametresiz kurucuyu `SetsRequiredMembers`'sız kullanılamaz yapardı. İstisnayı yalnız `RecordingLoopEvaluator` üretir ve üçünü de doldurur |
| 2 | Açık Soru 1: ölçümden sonra karar | **Ölçüldü:** MAF evaluator istisnasını sarmalamıyor; çocuk agent run'ı `LoopUnsatisfied` taşır. Workflow satırı `error.type` olarak CLR adını (`Tracon.TraconLoopUnsatisfiedException`) yazar ve `error.class` boş kalır — bu, `WorkflowRunner.ToRunError`'ın BÜTÜN workflow hataları için davranışıdır. Kullanıcı kararı (seçenek B): faz kapsamı dar kalır, yeni kusur adayı **F-314** | Değişiklik döngüye değil tüm workflow hatalarına dokunur ve `error.type` değerini değiştirir |
| 3 | Manuel case 1: `provider: "openai"`, `gpt-5-mini` | `provider: "openai-responses"`, `gpt-5.4-mini` (örnek uygulamanın varsayılanı) | Harness modu Chat Completions sağlayıcısında iki ayrı `400` ile düştü (web araması `web_search_options`; tool çağrısından sonra `messages.[1].role`). Faz dışı kusur, **F-315** |
| 4 | Üç kullanıcı kararı `KARARLAR.md`'ye | Tek satır **K-884** (üç kararı birlikte taşır) | Defter bütçesi %0 boşluktaydı (`karar-damit` aynı gün koştu); üçü tek bir API kararının parçalarıdır |
| 5 | MAF'a `N + 1` | `N < int.MaxValue` iken `N + 1`, aksi hâlde `N` | Taşma koruması; pratikte erişilemez |
| 6 | Arayüz: iki sözlük satırı | + `full-replace-editors.test.tsx` fikstürü iki alanı taşır; konsol ekran görüntüleri ve landing kırpım pin'leri yeniden üretildi | Üretilen TS şeması alanları zorunlu kılar; fikstür tam-PUT editörünün yeni alanları **koruduğunu** da kanıtlar (16/16). Ekran görüntüsü damgası arayüz kaynağının hash'idir; görsel değişiklik yok (kırpımlar gözle doğrulandı) |
| 7 | Site: `concepts/agents.md` (+ sınıf listesi varsa sınıflandırıcı kılavuzu) | `concepts/agents.md` yeni alt bölüm · `concepts/runs.md` sınıf cümlesi · `capabilities.md` satırı · `CHANGELOG.md` | Sınıflandırıcı kılavuzu sınıf listesi taşımıyor; değişmedi |
| 8 | Akışlı yol fonksiyonel testi | Test süreç içi `RunStreamingAsync` ile koşar, SSE ile değil; SSE `error` çerçevesi canlı `MT-CORE-134`'te ölçüldü | Hata çerçevesini yazan uç fazdan önce de vardı; fazın yeni davranışı istisnanın akışın sonunda atılmasıdır |
| 9 | Planda `usage` yoktu | `FailRun` yolu turların `usage`'ını yan kanala ekler: `RecordingLoopEvaluator` her değerlendirilen turun `LastResponse.Usage`'ını `LoopContext.AdditionalProperties`'te toplar, yalnız run'ı düşürürken `TraconRunContext.ExtraUsage`'a ekler | Denetim 🔴: istisnayla biten run'ın tek `usage` kaynağı yan kanaldır; düzeltmeden önce `run.usage`/`cost` `null`, kota `0`. Üç test düzeltmeden önce kırmızıydı (akışlı yol dahil); canlı `13142` token |
| 10 | Mağaza sözleşmesi planda yoktu | `AgentDefinitionStoreContract` `Harness.Loop`'u iki alan `FailRun` iken gidiş-dönüş taşır; dört mağazada yeşil | Denetim 🟢; yeni alanlar kalıcı JSON'a girer |
| 11 | — | Başarılı döngü run'ının `usage`'ı yalnız son turdur (akışlı ve akışsız) — fazdan ÖNCE de böyleydi. Başarılı yol testi bu yüzden eklenmedi; iş **F-317** (🟠) | Katman probu: `LoopAgent` `30/5`, dış MAF `OpenTelemetryAgent` `10/1`; akışlı yolda kaydedici son `UsageContent`'i yazar. Düzeltme boru hattı kararı ister |

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Fazın kararları burada yazılır. Dört kategoriden birine
> giren karar (AGENTS.md) ayrıca `docs/KARARLAR.md`'ye K-NNN ve kategori
> etiketiyle girer ve numarası burada anılır; girmeyen karar yalnız burada
> kalır (`faz-tamamlama` Adım 8). Plan numara rezerve etmez.
>
> Plan anında kullanıcıdan alınan üç karar (2026-10-11) kapanışta buraya ve —
> `public-api` kategorisinde — `KARARLAR.md`'ye yazılır: iki alan tek enum ·
> `FailRun`'da son tur değerlendirilir · yeni sınıf yalnız tükenme için.

- **K-884** *(public-api, kullanıcı kararı)* — üç plan kararı tek satırda: iki
  bağımsız alan + tek `LoopOutcome` enum'u; `OnExhausted = FailRun` iken tavandaki
  tur değerlendirilir (MAF'a `N + 1`); `LoopUnsatisfied = 15` yalnız tükenme için,
  ölçüt hatası orijinal istisnayla düşer.
- **Açık Soru 1** *(kullanıcı kararı, kategori dışı)* — workflow satırının
  sınıflandırılması bu fazda yapılmadı; ölçüm testte kilitli
  (`A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion`), iş F-314'te.
- **Açık Soru 2** — plan önerisi A uygulandı: `ReturnLastResponse` modunda son tur
  değerlendirilmez (bugünkü maliyet); mevcut on test değişmeden yeşil.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 |
| Düzeltme turu sayısı | 4 (test derleme/yol · site kapısı: `llms-full`, ekran görüntüsü damgası, landing pin · denetim 🔴 · denetim 🟢 sözleşme) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | 0 |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

| Bulgu | Seviye | Sonuç |
|---|---|---|
| `FailRun` ile düşen run tamamlanan turların `usage`'ını kaybeder: `run.usage`/`cost` `null`, kota `Tokens = 0` | 🔴 | **Düzeltildi** — sapma #9; üç test kırmızı → yeşil; canlı `MT-CORE-132` `13142` token |
| Akışsız run endpoint'i `LoopUnsatisfied`'ı (ve `StructuredResponseInvalid`'ı) `502` ve `errorType`'sız döner | 🟢 | **F-316** olarak devredildi — public HTTP sözleşmesi kararı |
| `AgentDefinitionStoreContract` `Harness.Loop`'u gidiş-dönüş sınamaz | 🟢 | **Düzeltildi** — sapma #10; dört mağaza yeşil |
| Akışlı test SSE değil süreç içi | not | Sapma #8'e yazıldı |

🟡 yok. Düzeltme sırasında fazdan bağımsız, önceden var olan bir kusur bulundu:
başarılı döngü run'ı yalnız son turun `usage`'ını kaydeder — **F-317** (🟠).

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

**Devralınan sözleşmeler**

- `LoopSettings.OnExhausted` / `OnCriterionError` (`LoopOutcome`), varsayılan
  `ReturnLastResponse` = Faz 151 davranışı birebir. `FailRun` tükenmede
  `TraconLoopUnsatisfiedException` (`loop_unsatisfied`, `LoopUnsatisfied = 15`);
  ölçüt hatasında orijinal istisna.
- `LoopIterationCompleted` payload'ı `failsRun` taşır; `FailRun` tükenmesinde olay
  sayısı tur sayısına eşittir (varsayılan modda bir eksik).

**Bilinen tuzaklar**

- 🚨 `FailRun` tavanı MAF'a `N + 1` vererek çalışır ve MAF'ın "tavana ulaşan tur
  değerlendirilmez" kuralına dayanır (K-884). MAF yükseltmesi bunu değiştirirse
  `HarnessLoopTests`'in "sağlayıcı istek sayısı tam N" iddiası kırmızı olur.
- 🚨 Harness modu `openai` (Chat Completions) sağlayıcısında `400` ile düşer
  (F-315); gerçek model koşumlarında `openai-responses` kullan.
- Workflow satırı hata sınıfı taşımaz ve türü CLR adıyla yazar (F-314); sınıfı
  katılımcı agent run'ından oku.
- 🚨 Başarılı döngü run'ının `usage`'ı yalnız son turdur (F-317, önceden var).
  `FailRun` yolu kendi toplamını `ExtraUsage`'a ekler; F-317 düzeltilirken akışlı
  istisna yolu `usage` yazmaya başlarsa bu ekleme çift sayar — ikisi birlikte ele alınır.

**Yarım kalan / devredilen işler:** F-317 (🟠, döngü `usage`'ı), F-314 (workflow
satırını sınıflandırma), F-315 (harness + Chat Completions), F-316 (akışsız
endpoint durum kodu), F-302 (konsolda `Loop` düzenleme — iki yeni
alan da editörde yok; tam-PUT editörü onları korur, düzenlemez).

**Sıradaki faz:** [Faz 193](../../193-NET8-NET9-DUSURME.md) — `net8.0`/`net9.0`
düşürme; K-855 gereği **2026-11-10'dan önce başlamaz**. O tarihe kadar iş
`ADAYLAR.md`'den seçilir (🟠 F-317 kusur giderme; 🟠 F-294, F-295 faz planlama;
🟡 F-314, F-315 kusur giderme).
