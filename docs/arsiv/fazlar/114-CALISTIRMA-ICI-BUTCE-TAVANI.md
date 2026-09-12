# Faz 114 — Çalıştırma-İçi Bütçe Tavanı

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-166**
> **Önkoşul:** Faz 12 (agent çağrı grafiği, `AgentRunBudget`) ve Faz 21 (kota) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `Tracon.Abstractions` (`Runs/AgentRunBudget.cs`), `Tracon.Core` (`Models/`, `Recording/`)
> **Yeni paket:** Yok · **Migration:** Yok — tavan yapılandırmadan gelir, veritabanına yazılmaz
> **Public API:** Büyüyor — mevcut bir tipe alanlar, bir dekoratör, bir exception tipi. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır ve her dosya yalnız başlık taşıyor (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/governance.md`, `guides/production.md`, `capabilities.md`
> · sevk edilen: `AgentRunBudget` ve `TraconAgentGraphOptions` XML dokümanları — 🚨 ikisi de bugün **yanlış** şey ilan ediyor
> **Manuel test alanı:** `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 3fbdc7d:docs/arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon kurulumu bugün varsayılan olarak **200 000 token'lık bir ağaç bütçesi** ilan eder ve bu varsayılanın *bilerek* var olduğunu yazar. Ölçüm o tavanın **hiç zorlanmadığını** gösterdi: bütçe sayacı yalnız run **bitiminde** işlenir ve yalnız **yeni bir alt-run başlarken** sorgulanır.

## Bitiş Ölçütleri (DoD)

- [x] `MaxTotalTokens` düşük ayarlıyken uzun tool döngülü bir run **kesilir**; `runs.error_class` = `QuotaExceeded` — `RunBudgetAccountingTests` + gerçek `samples/Tracon.Api` koşumu (aşağıda)
- [x] Kesme her zaman tool turu sınırındadır — kesilen run'ın son olayı yarım bir model mesajı **değildir** — gerçek koşumda ölçüldü: son olay `RunFailed`'in kendisidir, arada YARIM DEĞİL, hiçbir tool/mesaj olayı yoktur (bkz. Plandan Sapmalar)
- [x] N turluk bir run sonunda `AgentRunBudget.ConsumedTokens` gerçek toplama **eşittir** (çifte sayım yok) — `Two_turn_tool_loop_is_recorded_exactly_once_no_cap`
- [x] `PricingSource.Unknown` modelde maliyet tavanı **uygulanmaz**; token tavanına düşülür — `Unknown_pricing_falls_back_to_the_token_cap_and_never_cuts_on_cost_alone`
- [x] Hiçbir tavan tanımlı değilken (`0`) davranış bugünküyle **aynıdır** — gerileme yok — `No_cap_configured_keeps_todays_unlimited_behaviour`
- [x] Tavan tanımsızken `IRunPricingResolver` sıcak yolda **hiç çağrılmaz** — `Pricing_resolver_is_never_consulted_when_no_cost_cap_is_set`
- [x] Ağaçtaki tüm dallar aynı bütçeyi görür; toplam tavanı aşmaz — `Two_different_agents_sharing_one_tree_budget_are_cut_off_by_each_others_spend`
- [x] `RunBudgetChatClient` içinde `AsyncLocal`'a **yazan** hiçbir satır yok (yalnız okur) — kod okunarak doğrulandı, bağımsız denetimin de teyit ettiği madde
- [x] `RunErrorClass` üye kümesi **değişmedi**; `9` tanımsız kaldı (`RunErrorClassContractTests` yeşil)
- [x] `AgentRunBudget` ve `TraconAgentGraphOptions` XML dokümanları gerçeğe uydu — denetimin 🔴 bulgusu düzeltildi (bkz. Denetim Bulguları)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 58cfd03` (tam koşum, SQL Server/Sqlite/PostgreSQL entegrasyon testleri dahil)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. aşağıdaki gerçek çıktı
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` → `✅ temiz`
- [x] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-RET-050/051 gerçek OpenAI çağrısıyla koşuldu ve ölçülen sonuca göre düzeltildi; MT-RET-052..055 tam manuel-test koşumuna bırakıldı (bkz. Sonraki Faza Devir Notu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 🔴 bulundu ve düzeltildi (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi (`concepts/governance.md` run-içi tavanı anlatır); `npm run build` + `check-links.mjs` temiz — `npm run check` tam koşum yeşil

### Doğrulama komutları (gerçekten koşuldu, 2026-08-26, `samples/Tracon.Api`, gerçek OpenAI çağrısı)

```bash
# Tavanı düşür, "support" agent'ını gerçek bir sipariş sorusuyla çalıştır
export Tracon__AgentGraph__MaxTotalTokens=150
cd samples/Tracon.Api && ASPNETCORE_ENVIRONMENT=Development dotnet run -c Release

curl -s -X POST http://localhost:5080/tracon/api/agents/support/run \
  -H "Authorization: Bearer manuel-test-token-2026" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuidgen)" -d '{"message":"Where is my order 42?"}'
```

**Gerçek yanıt (aynen):**
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.3",
  "title": "Agent run failed",
  "status": 502,
  "detail": "The run tree's token budget is exhausted (254/150). Raise Tracon:AgentGraph:MaxTotalTokens to allow more. No further model calls can be made in this run tree."
}
```

```bash
RUN_ID=$(curl -s http://localhost:5080/tracon/api/runs -H "Authorization: Bearer manuel-test-token-2026" | jq -r '.[0].id')
curl -s "http://localhost:5080/tracon/api/runs/$RUN_ID" -H "Authorization: Bearer manuel-test-token-2026" | jq '{status, errorClass: .error.class, errorType: .error.type}'
```

**Gerçek yanıt (aynen):** `{"status": "Failed", "errorClass": "QuotaExceeded", "errorType": "run_budget_exceeded"}`

**Tavansız kontrol koşumu** (aynı sorgu, `AgentGraph:MaxTotalTokens` unset — varsayılan `200000`): gerçek tool döngüsü (`get_order_status` çağrıldı, sonuç kullanıldı) toplam **551 token** ile `Completed` bitti — varsayılan tavan bu tipik run'ı hiç zorlamaz, gerileme yok.

---

## Plandan Sapmalar

- **`ModelProviderRegistry` kurucusuna plan `IRunPricingResolver?` (doğrudan) öneriyordu; gerçekleşen `IServiceProvider?`'dır.** Kod yazmadan önce ölçüldü: `RunPricingResolver`'ın kendisi `IModelProviderRegistry`'ye bağımlı olduğu için doğrudan parametre DI'nin çözemeyeceği bir döngü üretiyordu. `BuildPipeline` fiyat çözümleyiciyi agent derleme anında, DI konteyneri tamamen kurulduktan sonra, geç (lazy) çözer — bkz. K-631.
- **Açık Soru 1 (çifte sayım) planın önerdiği B seçeneğinden ("dekoratör kaydeder, `Completion` yalnız GÖRMEDİĞİNİ ekler") daha güçlü bir sonuçla kapandı.** Ölçüm (sıkıştırma özetleme çağrısının da AYNI `ModelProviderRegistry.BuildPipeline` boru hattından geçtiği) `RunBudgetChatClient`'ın hiçbir şeyi KAÇIRMADIĞINI gösterdi — `Completion.cs`'in eski satırı "eksiği tamamlayan" değil tamamen GEREKSİZ oldu ve kaldırıldı (K-632).
- **Açık Soru 2 (kilitsiz `decimal` sayaç)**: B seçildi — `long` nano-birim (1e9) sabit noktalı sayaç + `Interlocked.Add`. Planın önerdiğiyle birebir aynı yön, tek fark ölçek seçimi (mikro değil nano — token başı fiyatların altı basamakta sıfıra yuvarlanmaması için, bkz. K-632).
- **Açık Soru 3 (`RecordUsage(long)` imzası)**: A seçildi — aşırı yükleme eklendi (`RecordUsage(long, decimal?)`), eski imza `RecordUsage(long tokens) => RecordUsage(tokens, cost: null)` olarak korundu.
- **Açık Soru 4 (kesme mesajı ayrıntısı)**: A seçildi — mesaj hangi tavanın dolduğunu, ne kadar harcandığını VE hangi `Tracon:AgentGraph:*` ayarının yükseltileceğini adıyla yazar. Gerçek ölçülen örnek: *"The run tree's token budget is exhausted (254/150). Raise Tracon:AgentGraph:MaxTotalTokens to allow more. No further model calls can be made in this run tree."*
- **Açık Soru 5 (MAF'ın tool döngüsü exception'ı yutuyor mu?)**: Kod yazmadan ÖNCE küçük bir probe projesiyle ölçüldü (bu oturumda, `maf-api-kesfi` skill'i yerine doğrudan gerçek `FunctionInvokingChatClient`'a karşı bir istisna senaryosu koşularak) — **yutmuyor**, hem akışsız hem akışlı yolda istisna aynen yukarı bırakılıyor. Tasarım bu ölçümle doğrulandı, değişmedi.
- **`TryReserveRun()`'ın exhaustion kontrolü genişletildi**: plan yalnız yeni maliyet alanlarını eklemeyi öngörüyordu; uygulama sırasında `TryReserveRun`'ın mevcut `IsTokenBudgetExhausted` kontrolü de `IsExhausted`'e (token OR cost) genişletildi — aksi hâlde maliyeti tükenmiş bir ağaç hâlâ yeni alt-run başlatabilirdi, tutarsız bir davranış olurdu. Faz dokümanında planlanmamış küçük bir kapsam genişlemesidir, gerekçesi budur.
- **Bağımsız denetimin bulduğu 🔴** (`AgentRunBudget`'ın sınıf düzeyi XML `<remarks>`'i düzeltilmemişti) düzeltildi — bkz. Denetim Bulguları.
- **Manuel case'lerin ilk taslağı ölçülmeden yazılmıştı**: `samples/Tracon.Api`'ye karşı gerçek OpenAI çağrısıyla koşulduğunda iki hata bulundu ve düzeltildi — (1) başarısız bir `POST /run`'ın gövdesi bir `run` kaydı değil `ProblemDetails`'tir (`RUN_ID` `GET /api/runs`'tan okunur), (2) MAF'ın `FunctionInvokingChatClient`'ı bir istisna fırlattığında o ana kadarki kısmi ilerlemeyi (tool çağrısı GERÇEKTEN yapılmış olsa bile) hiç geri döndürmez — kesilen bir run'ın olay akışı `RunStarted → RunFailed`'ten ibarettir, aralarında YARIM DEĞİL, hiçbir tool/mesaj olayı yoktur. MT-RET-051 bu ölçülen gerçeğe göre yeniden yazıldı.

## Bu Fazda Verilen Kararlar

- **K-630** — Çalıştırma-içi bütçe kesmesi yeni bir `RunErrorClass` üyesi açmadan mevcut `QuotaExceeded` (`4`)'e eşlenir; `9` kalıcı emekli kalır.
- **K-631** — `ModelProviderRegistry` kurucusu `IServiceProvider?` alır, fiyat çözümleyiciyi `BuildPipeline` içinde geç çözer (döngüsel DI kırılımı).
- **K-632** — `AgentRunBudget`'ın maliyet sayacı kilitsiz `long` nano-birimdir; `RunRecordingAgent.CompleteAsync`'in run-sonu bütçe kaydı satırı kaldırıldı, muhasebenin %100'ü `RunBudgetChatClient`'a taşındı.

Tam gerekçeler: `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetçi (taze bağlamlı, `git diff` + faz dokümanı) 1 🔴, 2 🟡 buldu.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `AgentRunBudget`'ın sınıf düzeyi XML `<remarks>`'i hâlâ "does not interrupt a run in progress" diyordu — fazın merkezi iddiasıyla doğrudan çelişiyordu | **Düzeltildi.** `<remarks>` `TryReserveRun` (token/count, yalnız yeni child-run engeller) ile token/cost boyutunu (mid-run kesme) ayrı ayrı ve doğru anlatacak şekilde yeniden yazıldı |
| 2 | 🟡 | Ağaç genelinde paylaşılan bütçe için (iki FARKLI agent'ın kendi `RunBudgetChatClient` örneği üzerinden) otomatik test yoktu | **Düzeltildi.** `RunBudgetAccountingTests.Two_different_agents_sharing_one_tree_budget_are_cut_off_by_each_others_spend` eklendi — iki ayrı `ModelProviderRegistry` pipeline'ı, aynı `AgentRunBudget` nesnesi, birinin harcaması diğerini kestiğini kanıtlıyor |
| 3 | 🟡 | `usage == null` (boş/aşırı girdi hata modu) için test yoktu | **Düzeltildi.** `RunBudgetAccountingTests.A_response_with_no_usage_is_neither_recorded_nor_fatal` eklendi |

Denetçinin "doğrulanamayan/kanıtsız" not ettiği DoD satırı (`samples/Tracon.Api` ile gerçek run) bu kapanışta gerçekleştirildi — bkz. aşağıdaki DoD tablosu ve doğrulama komutları.

Temiz çıkan başlıklar (denetçinin raporu): 3.2 (test tiyatrosu yok), 3.3 (test seviyesi doğru), 3.5 (imza-gövde kayması yok), 3.6 (plan dışı public API yok, `PublicAPI.Unshipped.txt` tam), 3.7 (repo kuralları), 3.8 (ürün yüzeyi).

## Sonraki Faza Devir Notu

- **`RunBudgetChatClient`'ın ring pozisyonu artık kararlıdır**: `UseFunctionInvocation`'ın içinde, `TraconResponseCachingChatClient`/`UseOpenTelemetry`'nin dışında — `ModelProviderRegistry.BuildPipeline`'a yeni bir halka eklerken bu üç halkanın (budget/cache/telemetry) göreli sırasını bozmadan ekle; `.Use()` çağrılarının kayıt SIRASI (ilk kayıt = en dıştaki katman) bu dosyanın kendi yorumlarında açıklanıyor.
- **🚨 `ModelProviderRegistry`'ye kendisi `IModelProviderRegistry`'ye bağımlı bir servis eklerken döngüsel DI riskini hatırla** — bkz. `docs/hafiza/model-boru-hatti.md`, K-631. `IServiceProvider` + geç çözümleme deseni tekrarlanabilir.
- **Agent başına bütçe** (`AgentDefinition` alanı) ve **`QuotaDefinition`'ın dönem tavanının run içinde uygulanması** bu fazın KAPSAM DIŞI bıraktığı iki komşu iş — `ADAYLAR.md`'ye aday olarak eklenebilir, ayrı ölçüm gerektirir.
- **Manuel case'ler MT-RET-052..055 gerçek bir koşumla doğrulanmadı** (yalnız MT-RET-050/051 gerçek OpenAI çağrısıyla ölçüldü ve düzeltildi) — sonraki tam manuel-test koşumunda (`manuel-test-kosumu` skill'i) bu dört case'in de gerçek sunucuya karşı çalıştığını doğrula.
