# Faz 228 — Alt Agent Tek Çağrı Modu

> **Durum:** 📋 Planlandı (2026-10-20)
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

- [ ] `CallMode = Tool` olan sohbet ve harness agent'ı, alt agent'ı tek tool çağrısıyla koşar; çocuk run'ı `ParentRunId` ile bağlıdır
- [ ] Tool modunda derinlik ve bütçe reddi tool sonucu metni olarak döner (bugünkü sarmalayıcı metni)
- [ ] Ad çakışması doğrulamada `400` verir ve iki adı gösterir
- [ ] `CallMode` yokken davranış değişmez (mevcut alt agent testleri yeşil)
- [ ] Konsol `callable-agents-section.tsx` modu gösterir ve kaydeder; `en.ts` + `tr.ts`
- [ ] `samples/Tracon.Api` `router` agent'ı (`CallableAgentNames = ["support"]`, `Program.cs:614`) iki modda koşuldu; ebeveyn model çağrısı sayısı belgeye yazıldı
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Tool modunda ebeveyn tek tool görür, çocuk koşar | Fonksiyonel (`FakeModelProvider.Requests`, gerçek host) | `SubAgentToolModeTests` |
| Harness agent'ı da tool modunu alır | Fonksiyonel | `SubAgentToolModeTests` (harness satırı) |
| `MaxDepth` ve ağaç bütçesi tool modunda uygulanır | Fonksiyonel | `SubAgentToolModeTests` |
| Çocuk onay isterse tool sonucu reddi döner, ebeveyn askıda kalmaz | Fonksiyonel | `SubAgentToolModeTests` |
| Ad çakışması reddedilir | Birim (doğrulayıcı) | `AgentDefinitionValidatorTests` |
| Yük gidiş-dönüşü (`CallMode`) | Entegrasyon (SQLite yük testi deseni) | `AgentDefinitionPayloadRoundTripTests` |

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

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.
