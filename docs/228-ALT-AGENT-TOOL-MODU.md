# Faz 228 — Alt Agent Tek Çağrı Modu

> **Durum:** ✅ Tamamlandı (2026-10-20)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (yalnız ekleme) · hedef framework değişmez · güvenlik/kiracı sınırı yok (alt agent sarmalayıcısı ve sınırları aynen kalır) · geri dönüşü pahalı karar yok
> **Plan onayı:** farukatasoy, 2026-10-20 (ölçümden sonra planla)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-599**
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.AspNetCore`, `.Client`, `.UI`
> **Tüketici yüzeyi:** site `concepts/agents.md` (alt agent bölümü, `SubAgents` örneği), `capabilities.md` · sevk edilen: `SubAgentSettings` XML doc

## Amaç

Bugün alt agent'ı yalnız MAF `BackgroundAgentsProvider` çağırır
([`AgentDefinitionCompiler.Agents.cs:36,131-135`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs);
harness `:346-347`). Tek çağrılık iş için model başlatır, bekler, sonucu okur ve
siler.

**Ölçüldü (2026-10-20, gerçek OpenAI `gpt-5.4-mini`, MAF 1.25, 3 tekrar, tek soru):**

| Mod | Ebeveyn model çağrısı | Ebeveyn girdi token'ı | Ebeveyn çıktı token'ı | Süre | Tool'lar |
|---|---|---|---|---|---|
| `BackgroundAgentsProvider` | 5 | 3.463–3.471 | 116–118 | 4,6–7,6 sn | start, wait, get_results, clear |
| `AsAIFunction` | 2 | 377 | 33 | 2,5–3,4 sn | ajan adıyla tek tool |

Çocuğun kendi maliyeti iki modda aynıdır (1 çağrı). Fark ebeveyndedir: sağlayıcının
talimatı ve altı tool şeması her turda gider. Ölçüm tek soru tipidir; çok adımlı
paralel işte arka plan modu üstün kalır.

Faz bitince tanım `SubAgents.CallMode = Tool` seçer; her çağrılabilir agent ebeveyne
ajan adıyla **tek bir tool** olarak verilir (`ChildAgentInvoker.AsAIFunction`).
Varsayılan `Background` kalır (K1). `ChildAgentInvoker` gelen seçeneği okumaz; ağaç
bilgisini `TraconRunContext`'ten alır ([`ChildAgentInvoker.cs:197-213`](../src/Tracon.Core/Graph/ChildAgentInvoker.cs)):
derinlik, ağaç bütçesi, bekleme sınırı ve onay reddi tool modunda da aynen uygulanır.

Tasarım:
- `SubAgentSettings`'e `CallMode` (`SubAgentCallMode.Background` | `Tool`); tanımın bütün çağrılabilir agent'larına uygulanır (agent başına mod Açık Soru 1).
- Derleyici tool modunda `BackgroundAgentsProvider`'ı (sohbet) ve `BackgroundAgents`'ı (harness) kurmaz; sarmalayıcıların `AsAIFunction(...)` sonucunu `ChatOptions.Tools`'a ekler. Uzantıdan önce eklenir; koruma tablosu (`Tools`) değişmez.
- Ad: MAF ajan adını temizler (`SanitizeAgentName`, geçersiz karakter → `_`). Doğrulayıcı temizlenmiş adın `ToolNames` (ve Faz 226 sonrası hosted adlar) ve skill tool'larıyla (`load_skill`, `read_skill_resource`, `run_skill_script`) çakışmasını reddeder.
- Tool zinciri: ajan tool'u kayıt defterinden gelmez (arka plan tool'ları gibi); yetki, onay, zaman aşımı sarmalayıcıdandır. Site bunu yazar.
- `BackgroundAgentsProviderOptions.WaitTimeout` tool modunda kullanılmaz; sınır `SubAgentSettings.ChildDeadline`/`WaitTimeout`'tur.

## Bitiş Ölçütleri (DoD)

- [x] `CallMode = Tool` olan sohbet ve harness agent'ı, alt agent'ı tek tool çağrısıyla koşar; çocuk run'ı `ParentRunId` ile bağlıdır — `SubAgentToolModeTests.The_parent_gets_the_callable_agent_as_one_tool_and_the_child_run_is_linked` (chat/harness × düz/akışlı); özelliği kapatınca 7 test düşer (ölçüldü)
- [x] Tool modunda derinlik ve bütçe reddi tool sonucu metni olarak döner (bugünkü sarmalayıcı metni) — `The_depth_limit_comes_back…`, `The_tree_run_budget_comes_back…`; onay isteyen çocuk: `A_child_that_needs_tool_approval_comes_back_as_a_refusal…`
- [x] Ad çakışması doğrulamada `400` verir ve iki adı gösterir — `AgentDefinitionValidatorTests`, `SubAgentToolNamesTests`, `SubAgentToolModeCompilationTests` ve HTTP'de `A_definition_saved_over_http_with_a_name_conflict…`; örnek uygulamada da `400` (ayrıntı iki adı içerir)
- [x] `CallMode` yokken davranış değişmez (mevcut alt agent testleri yeşil) — `Without_the_mode_the_parent_keeps_the_task_tools…`; `AgentDelegationTests`, `SubAgentTimeoutTests`, `BackgroundChildQuotaTests` yeşil; eski yük `Background` okur (`AgentDefinitionPayloadRoundTripTests`)
- [x] Konsol `callable-agents-section.tsx` modu gösterir ve kaydeder; `en.ts` + `tr.ts` — Vitest + Playwright (örnek uygulama, `MT-UIAG-065`)
- [x] `samples/Tracon.Api` `router` agent'ı (`CallableAgentNames = ["support"]`, `Program.cs:614`) iki modda koşuldu; ebeveyn model çağrısı sayısı belgeye yazıldı — kod agent'ı kipi değiştirmez, bu yüzden `router` (`Background`) ve aynı talimat/modelle HTTP'den kaydedilen `router-tool` (`Tool`) üçer kez koşuldu (gerçek OpenAI `gpt-5.4-mini`, `SuccessSampleRatio=1`, `chat` span'ları sayıldı):

  | Ebeveyn | `router` (`Background`) | `router-tool` (`Tool`) |
  |---|---|---|
  | Model çağrısı | 5 · 5 · 5 | 2 · 2 · 2 |
  | Girdi token'ı | 3.603 · 3.645 · 3.705 | 409 · 412 · 409 |
  | Çıktı token'ı | 139 · 147 · 170 | 48 · 53 · 51 |
  | Süre | 5,8 · 5,9 · 9,0 sn | 3,2 · 3,3 · 3,1 sn |
  | Gördüğü tool'lar | altı `background_agents_*` | `support` |

  Çocuk iki modda da 2 model çağrısı (~790 token). Günlükte `Npgsql` satırı 0.
- [x] AOT yayın probu (planın "ölçülmeli" dediği): geçici `PublishAot` projesi (`net10.0`, `osx-arm64`, `Microsoft.Agents.AI` 1.25.0), özel bir `AIAgent` ve `agent.AsAIFunction(new AIFunctionFactoryOptions { Name = "arastirma_ci" })`; `dotnet publish -c Release -r osx-arm64`. Sonuç: yayın tamam, IL2/IL3 uyarısı yok, ikili çalıştı ve şemayı (`query`) ile sonucu (`echo: sub task`) verdi. Kapsam: yalnız MAF'ın `AsAIFunction`'ı; Tracon'un `ChildAgentInvoker`'ı probda yok (`Tracon.Core` `IsAotCompatible`, analyzer derlemede koşar). Örnek AOT smoke'larına alt agent eklenmedi
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban c404aabc` — denetim düzeltmelerinden sonraki son koşum: 25.192 test, 0 düşen, `dotnet format` ve `docs-site npm run check` yeşil

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Tool modunda ebeveyn tek tool görür, çocuk koşar | Fonksiyonel (`FakeModelProvider.Requests`, gerçek host) | `SubAgentToolModeTests` |
| Harness agent'ı da tool modunu alır | Fonksiyonel | `SubAgentToolModeTests` (harness satırı) |
| `MaxDepth` ve ağaç bütçesi tool modunda uygulanır | Fonksiyonel | `SubAgentToolModeTests` |
| Çocuk onay isterse tool sonucu reddi döner, ebeveyn askıda kalmaz | Fonksiyonel | `SubAgentToolModeTests` |
| Ad çakışması reddedilir | Birim (doğrulayıcı) | `AgentDefinitionValidatorTests` |
| Yük gidiş-dönüşü (`CallMode`) | Entegrasyon (SQLite yük testi deseni) | `AgentDefinitionPayloadRoundTripTests` |
| Çocuğun sağlayıcısı düşer: ebeveyn sebebi okur, run'ı bitmez; iki kip aynı sebebi verir | Fonksiyonel | `SubAgentToolModeTests` (iki kip, dört ardışık çağrı) |
| `ChildDeadline` tool yolunda tool sonucu olur, çocuk run'ı kapanır | Fonksiyonel | `SubAgentToolModeTests` |
| Kayıtlı tanım HTTP'den `Tool` ile kaydedilir, okunur, koşar; çakışma `400` | Fonksiyonel (HTTP) | `SubAgentToolModeTests` |

Beş soru: iptal (sarmalayıcının iki katmanlı sınırı aynen) · eşzamanlılık (model bir
turda iki ajan tool'u isterse `ToolLoop.AllowConcurrentToolCalls`/bugünkü alan
yönetir) · boş/aşırı girdi (`query` boş → çocuk boş mesajla koşar; ölçülmeli) ·
başka kiracı (sarmalayıcı kiracıyı çözer) · alt sistem hatası (sağlayıcı hatası
bugünkü K-737 dalı). AOT: `AsAIFunction` MAF içinde `AIFunctionFactory.Create(Delegate)`
kullanır, `BackgroundAgentsProvider` da aynısını yapar; AOT yayın probu ölçülmeli.

## Açık Sorular

| # | Soru | Öneri |
|---|---|---|
| 1 | Mod tanım başına mı, çağrılabilir agent başına mı? | Tanım başına: veri modeli düz kalır; agent başına ihtiyaç ölçülürse `CallableAgents` listesi ayrı fazdır |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

1. **`ChildAgentInvoker` gelen seçeneği okuyor (plan: okumaz).** `96eb7843` (handoff) `CreateChildOptions`'a `(options as ChatClientAgentRunOptions)?.ChatOptions` okumasını ekledi. `AsAIFunction` çocuğa düz `AgentRunOptions{AdditionalProperties}` verir (`ilspycmd` ile okundu), yani `ChatOptions` boş gelir ve ağaç alanları yine yalnız `TraconRunContext`'ten gelir; sonuç planla aynı.
2. **Tool adını MAF değil Tracon verir.** Plan "MAF ajan adını temizler"di. `AsAIFunction`'a `AIFunctionFactoryOptions.Name` verilir; kural MAF'ınkiyle birebir (`[^0-9A-Za-z]+` → `_`) ve `SubAgentToolNamesTests` eşitliği sabitler. Böylece çakışma kuralı çerçeve sürümüne bağlı kalmaz.
3. **Çakışma kuralı plandan geniş.** `ToolNames` ve skill tool'larına ek olarak `search_knowledge` (anlamsal arama açıkken) ve iki callable agent'ın aynı ada inmesi de reddedilir; ikisi de sağlayıcıya iki aynı adlı tool giderdi. Faz 226'nın hosted adları henüz yok, o faz ekler.
4. **Boş `query` ölçüldü.** `AsAIFunction` `AIAgent.RunAsync(string)` çağırır ve o boş/boşluk iletiyi `ArgumentException` ile reddeder: model `Error: Function failed.` görür, sarmalayıcıya varılmaz, çocuk run'ı açılmaz. Düzeltilmedi (çerçeve davranışı; ebeveyn tamamlanır); site yazar.
5. **Sürüm karşılaştırması eklendi (planda yok).** Bir sürümün modu değişince karşılaştırma ekranı iki özdeş tablo gösterirdi (Faz 225 aynı gerekçeyle yaptı). `CallMode` iki taraftan biri `Tool` ise "Call mode" satırı çıkar.
6. **Açık Soru 1 önerilen seçenekle kapandı** (tanım başına mod); kullanıcıya ayrıca sorulmadı. Agent başına mod ihtiyacı doğarsa `CallableAgents` listesi ayrı fazdır.
7. **Örnek uygulama kod agent'ı kipi değiştiremez.** `router` `Program.cs`'te kod agent'ıdır; `Tool` kipini `router-tool` ile (aynı talimat ve model, HTTP'den kayıt) ölçtüm.
8. **Ekran görüntüleri yeniden üretildi.** Konsol kaynağı değiştiği için `TRACON_UI_SCREENSHOTS=1` E2E koşumu 44 görüntüyü yazdı; damga ve anasayfa kırpımlarının dört pin'i (`landing-captures.mjs`) yenilendi, iki kırpım gözle doğrulandı.
9. **`SubAgentToolFunction` planda yok.** Bağımsız denetim (🟡1) tool modunda çocuğun hatasının sebepsiz ve ardışık-hata sayacına girerek döndüğünü gösterdi; yalnız `TraconException` metne çevrilir (bkz. Denetim Bulguları).

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **K-1043** (`public-api`): mod tanım başına; `Tool`'da ad çakışması reddedilir. Tool kayıt defterinden geçmez: `Background`'ta da çocuğun yetkisi yalnız sarmalayıcıdadır, bu yüzden güvenlik sınırı değişmedi. Alternatifler satırda.
- Kapanış commit mesajına: sapma 1–8; `AsAIFunction` boş `query` davranışı; `SubAgentCallMode.Tool`'un `Background`'a göre ölçümü (tek soru, tek agent; paralel iş ölçülmedi).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 8 sapma (yukarıda); plan metni çalışırken değişmedi |
| Düzeltme turu sayısı | 2 (kapanış kapısının README sayısı; denetim 🟡 düzeltmeleri) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 bilinen |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı (taze bağlamlı denetçi, 2026-10-20); 🟢 kapanışta `ADAYLAR.md`'ye gider.

**🔴 yok.** Başlık 1–5 (koruma tablosu, `session=null` ve kapsam, testlerin Tool yolunu kanıtlaması, imza değişikliği, `toSubAgents`) temiz.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Tool modunda çocuk istisnası (alt sistem hatası) testsiz ve belgesizdi | **Düzeltildi.** Ölçüldü: `Background` ebeveyne `Task failed: The model provider request failed. Provider: 'broken', fault: '…'` verir; `Tool` yalnız `Error: Function failed.` veriyordu ve hata FIC'in ardışık-hata sayacına giriyordu (üç tur sonra ebeveyn düşer). `SubAgentToolFunction` yalnız `TraconException`'ı (mesajı Tracon yazar, sağlayıcının mesajı girmez) `Agent '…' could not complete: …` metnine çevirir; öteki istisnalar çerçevenin davranışında kalır. Testler: `A_child_whose_model_provider_fails…` (iki kip), `A_parent_that_retries…` (sarmalayıcı çıkarılınca düşer, ölçüldü). Site ve CHANGELOG yazıldı |
| 2 | 🟡 | K-517: üç `<summary>` `<see cref>` taşıyor, OpenAPI `description`'ına CLR imzası sızıyor | **Düzeltildi.** `<c>` ile yazıldı; OpenAPI, `schema.ts`, `TraconApiClient.g.cs` yeniden üretildi (imza yok) |
| 3 | 🟡 | "AOT yayın probu temiz" iddiası kanıtsız | **Kayda geçti, kapsam daraltıldı.** Probun komutu ve sonucu DoD'de; kapsamı yalnız MAF'ın `AsAIFunction`'ı. `MAF-KAPSAMA.md` ifadesi buna indirildi. Örnek AOT smoke'una alt agent eklenmedi (gerekçe: `ChildAgentInvoker` zaten `IsAotCompatible` derlemesinde analyzer'dan geçer) |
| 4 | 🟢 | `FindProblems` MAF sağlayıcılarının sabit tool adlarını (`todos_*`, `mode_*`, `file_memory_*`) görmez | `ADAYLAR.md` **F-608** |
| 5 | 🟢 | Koruma tablosu sırası, `WaitTimeout` sert kesimi ve SSE ucu testsiz | `ADAYLAR.md` **F-609** (`ChildDeadline` tool yolunda bu fazda test edildi) |
| 6 | 🟢 | `MIMARI.md` alt agent'ı yalnız `BackgroundAgentsProvider` ile anlatıyordu | **Düzeltildi** (§ Çalıştırma ağacı) |
