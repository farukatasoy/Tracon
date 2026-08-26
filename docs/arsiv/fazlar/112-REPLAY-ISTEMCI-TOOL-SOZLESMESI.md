# Faz 112 — Replay'in İstemci Tool Sözleşmesi

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-109**
> **Önkoşul:** Faz 61 (istemci tool'ları, K-435) ve Faz 47 (replay, K-315) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `AgentPrism.Core` (`Replay/`), `AgentPrism.AspNetCore` (`Endpoints/RunEndpoints.cs`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `RunReplayOutcome`'a bir üye. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır ve her dosya yalnız başlık taşıyor (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/client-side-tools.md`, `capabilities.md`, `troubleshooting.md`
> · sevk edilen: `RunReplayOutcome` XML dokümanı, replay ucunun `.WithDescription` metni (`api/` ve `http-api/` üretilir)
> **Manuel test alanı:** `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c6c96be:docs/arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 61 `AddClientTool` ile **sevk edilmiş** bir yetenek getirdi: gövdesi tarayıcıda çalışan, sunucuda yalnız beyan olarak duran bir tool. Faz 47 replay'i getirdi ve "her run yeniden oynatılabilir" beklentisini kurdu. İkisi hiç karşılaşmadı.

## Bitiş Ölçütleri (DoD)

- [x] İstemci tool'u taşıyan bir agent'ın run'ına `POST /api/runs/{id}/replay` → `409`, `detail` tool adını içerir (üç `ToolMode` için de) — bkz. "Doğrulama komutları" altındaki gerçek çıktı
- [x] Yalnız sunucu tool'u taşıyan agent için replay eskisi gibi `200` döner — gerileme yok
- [x] Kod tanımlı (katalog) agent kolu da `409` döner
- [x] `RunReplayOutcome.ClientToolNotReplayable` için `RunEndpoints` `switch`'inde **açık** bir arm var; `_ =>` koluna düşmüyor
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban 0c4d931`)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. altında
- [x] `secret` taraması boş döndü (`python3 scripts/kapi.py tarama`)
- [x] Manuel kabul case'leri `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` içine eklendi (MT-IST-014…016); otomatikleştirilebilenler (014, 015) koşuldu, çıktı aşağıda; 016 👤 insan gerekir
- [x] `faz-denetim` koşuldu (taze bağlamlı ayrı agent); 🔴 bulgu kalmadı, iki 🟡 bulgu kapandı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi (`guides/client-side-tools.md` replay sınırını yazar, `capabilities.md` satırı düzeltilir, `troubleshooting.md`'ye eşdeğer girdi eklendi — denetimde bulundu); `npm run build` + `check-links.mjs` temiz (8 önceden var olan kırık bağlantı taban commit'te de kırık — bu fazın kapsamı dışında)
- [x] OpenAPI belgesi yeniden üretildi ve TS şeması + NSwag istemcisi + `@agentprism/client` `dist`'i zinciri koşuldu (K-627'nin dört adımlı zinciri) — dördü de tek satırlık tutarlı diff üretti

### Doğrulama komutları — gerçek çıktı (`samples/AgentPrism.Api`, echo sağlayıcı, 2026-08-26)

```bash
# Yalnız read_shopping_cart'ı taşıyan persistent (DB) agent oluşturuldu:
curl -s -X POST "$APU/api/agents" -H "content-type: application/json" \
  -d '{"name":"manuel-ist-replay","model":{"provider":"echo","model":"echo-1"},"toolNames":["read_shopping_cart"]}'
# -> HTTP 201

# Bir run tamamlandı, girdisi kaydedildi:
curl -s -X POST "$APU/api/agents/manuel-ist-replay/run" -H "content-type: application/json" \
  -H 'Idempotency-Key: mt-ist-014-demo' -d '{"message":"merhaba"}'
# -> {"runId":"01a03ee6-6b93-7be1-90fe-d149a186a3fd","response":{"messages":[{"authorName":"manuel-ist-replay","role":"assistant","contents":[{"$type":"text","text":"Echo: merhaba"}]}], ...}}

# Üç mod da 409:
for MODE in ReplayTools LiveTools NoTools; do
  curl -si -X POST "$APU/api/runs/01a03ee6-6b93-7be1-90fe-d149a186a3fd/replay" \
    -H "content-type: application/json" -d "{\"toolMode\":\"$MODE\"}"
done
# -> üçü de: HTTP 409, detail: "Agent 'manuel-ist-replay' carries the client-side tool
#    'read_shopping_cart' (AddClientTool). Its body runs in the caller's browser, not on
#    the server, and no call to it was recorded — there is no result to play back and no
#    client waiting to answer it live. This run cannot be replayed in any tool mode."

# Yalnız sunucu tool'u taşıyan agent'ta (cached-support) gerileme yok:
curl -si -X POST "$APU/api/runs/01a03ee6-c0eb-788a-bb58-c9300e76aea9/replay" \
  -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'
# -> HTTP 200, {"runId":"...","output":"Echo: hello","compareLocation":"..."}

# Kod tanımlı (katalog) support agent'ı (cancel_order onay gerektirir + read_shopping_cart
# istemci tool'u) — hangi guard önce tetiklenirse tetiklensin, ikisi de reddeder:
curl -si -X POST "$APU/api/runs/01a03ee6-9ce1-7f58-a1c6-5aa4e1e5d8e8/replay" \
  -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'
# -> HTTP 409, detail: "Agent 'support' carries the tool 'cancel_order', which requires
#    approval, and cannot run in 'LiveTools' mode. ... This agent has no persistent
#    definition (code-defined or deleted), so 'ReplayTools'/'NoTools' are not available
#    either — this run cannot be replayed."
```

---

## Plandan Sapmalar

- **Planlanan `tests/AgentPrism.Core.UnitTests/Replay/ReplayClientToolContractTests.cs` yazılmadı.** Plan bu davranışları "Birim" seviyesinde öngörmüştü (yanlış-pozitif, tool'suz agent, iptal, MCP false-positive). Ölçüm: `RunReplayService` `internal`'dır ve `PrepareAsync` yedi bağımlılığı (`IRunStore`, `IRunInputStore`, `IAgentDefinitionStore`, `IAgentCatalog`, `AgentDefinitionCompiler`, `IToolRegistry`, `ITenantContext`) birlikte kullanır — gerçek bir "birim" testi bu bağımlılıkların tamamını (mocksuz repo, gerçek `AgentDefinitionCompiler`) yeniden kurmayı gerektirir, ki bu `.agents/ortak/test-seviyeleri.md`'nin "DI kapsamı sınırını geçen davranış fonksiyonel seviyede kanıtlanır" kuralına göre zaten fonksiyonel testin işidir. Dört davranıştan ikisi (**sunucu tool'u yanlış-pozitif**, **tool'suz agent**) `ReplayClientToolEndpointTests` içinde zaten dolaylı kanıtlanıyordu. Kalan ikisi denetimde 🟡 bulgu olarak işaretlendi ve kapatıldı: **MCP false-positive** artık `McpTenantToolsTests.An_mcp_tool_is_never_marked_RunsOnClient` ile kilitli (gerçek DI olmadan, `McpTenantTools.Create`'i doğrudan çağıran ucuz bir birim testi — planın öngördüğü yerde değil ama planın istediği güvenceyi veriyor). **İptal yolu** için ayrı bir test yazılmadı: `FindClientTool`/`FindClientTool` çağrısı zaten alınmış verinin (`definition.ToolNames`/`descriptor.ToolNames`) üzerinde çalışan SENKRON bir döngüdür, iki mevcut `await` noktası arasına eklendi, yeni bir `try`/`catch` veya yeni bir asenkron sınır açmadı — iptal davranışı `PrepareAsync`'in zaten var olan, testlerle kanıtlı akışından değişmedi.
- **Manuel kabul case'leri `support` agent'ını (katalog kolu) tam izole test edemedi.** Plan case 4'ün ("Kod tanımlı agent, istemci tool'u taşıyor → 409, tool adı görünür") `read_shopping_cart`'ın adını izole göstermesini varsaymıştı. Ölçüm: örnek uygulamanın TEK istemci-tool'lu kod agent'ı (`support`) AYNI ZAMANDA `cancel_order`'ı (onay gerektirir) taşıyor; `PrepareFromCatalogAsync` onay kontrolünü istemci-tool kontrolünden ÖNCE çalıştırıyor, yani gerçek koşumda `409`'un `detail`'i `cancel_order`'ı adlandırıyor, `read_shopping_cart`'ı değil. MT-IST-015 bunu açıkça yazar; izole gösterim otomatik testte (`ReplayClientToolEndpointTests.A_code_defined_agent_carrying_a_client_side_tool_is_also_rejected`, yalnız istemci tool'u taşıyan ayrı bir agent ile) kanıtlanır. Örnek uygulamaya yeni bir "yalnız istemci tool'lu kod agent'ı" eklenmedi — kapsam dışı, gerekmiyordu.
- **`troubleshooting.md` ilk taslakta atlandı, denetimde bulundu ve eklendi.** Fazın kendi "Tüketici yüzeyi" satırı bu sayfayı listeliyordu ama DoD'nin site maddesi yalnız `client-side-tools.md`/`capabilities.md`'yi sayıyordu — plan ile DoD arasında bir sapma vardı. Bağımsız denetim (🟡 #2) bunu yakaladı; "Replay returns `409` for an agent that carries a client-side tool" bölümü artık mevcut `RunNotFound`/`InputNotFound` deseninin yanında duruyor.

## Bu Fazda Verilen Kararlar

- **K-628** — İstemci taraflı tool taşıyan agent hiçbir `toolMode`'da replay edilemez; ret `PrepareAsync`'te, run başlamadan, agent tanımının tool listesi taranarak verilir. Tam gerekçe: `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent), 2026-08-26. 🔴: yok. 🟡: 2, ikisi de kapatıldı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Planın öngördüğü `ReplayClientToolContractTests.cs` yazılmadı; iptal yolu ve MCP false-positive için kilitleyen test yok | **Kısmen düzeltildi, kısmen gerekçelendi**: MCP false-positive artık `McpTenantToolsTests.An_mcp_tool_is_never_marked_RunsOnClient` ile kilitli. İptal yolu için yeni test yazılmadı — gerekçe "Plandan Sapmalar"da |
| 2 | 🟡 | Fazın "Tüketici yüzeyi" `troubleshooting.md`'yi de listeliyordu, dokunulmamıştı | **Düzeltildi** — sayfaya `RunNotFound`/`InputNotFound` deseninde yeni bir bölüm eklendi |

**Temiz çıkan başlıklar:** 3.1 (dört ölçülebilir DoD satırı da teste bağlı, 677/677 yeşil), 3.2, 3.3, 3.5, 3.6 (tek public API üyesi plana birebir uyuyor), 3.7.

## Sonraki Faza Devir Notu

- **Devraldığı sözleşme:** `RunReplayOutcome.ClientToolNotReplayable` (= 5) — istemci tool'u taşıyan bir agent HİÇBİR `toolMode`'da replay edilemez, ret `409`. `FindClientTool` (`RunReplayService.cs`) `ToolDescriptor.RunsOnClient` okur; yeni bir tool sınıfı `RunsOnClient=true` işaretlenirse otomatik olarak bu korumaya girer, kod değişikliği gerekmez.
- **🚨 Yeni bir replay hazırlık kontrolü eklerken İKİ kolu da kapat.** `RunReplayService.PrepareAsync`'in kalıcı-tanım kolu (`definition.ToolNames`) ile katalog/kod-agent kolu (`descriptor.ToolNames`, `PrepareFromCatalogAsync`) AYRI kod yollarıdır; Faz 47 ve bu faz ikisinde de yalnız birini kapatıp ikincisini unutma riskiyle karşılaştı. `grep -n "FindApprovalTool\|FindClientTool" src/AgentPrism.Core/Replay/RunReplayService.cs` her ikisinin de İKİ çağrı yeri olduğunu doğrular.
- **Bilinen sınır:** Örnek uygulamanın (`samples/AgentPrism.Api`) tek istemci-tool'lu kod agent'ı (`support`) aynı zamanda onay gerektiren bir tool taşıyor; katalog kolunun istemci-tool mesajını İZOLE gösteren bir manuel senaryo yoktur (yalnız otomatik testte var). Yeni bir kod agent'ı gerekirse ekle, gerekmiyorsa MT-IST-015'in notunu koru.
- Faz 61'in (istemci tool'ları) ve Faz 47'nin (replay) hiçbiri bu fazla değişmedi; yalnız ikisinin kesişimi kapatıldı.
