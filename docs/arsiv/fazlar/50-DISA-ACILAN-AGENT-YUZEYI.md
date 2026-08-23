# Faz 50 — Dışa Açılan Agent Yüzeyi (MCP sunucusu ve A2A)

> **Durum:** ✅ Tamamlandı (2026-08-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-31**, **F-33** (birleşti)
> **Önkoşul:** Yok. Faz 12'nin `ChildAgentInvoker` sınır denetimleri **yeniden kullanılır**
> **Paketler:** `AgentPrism.AspNetCore` (yeni bağımlılıklar **yalnız burada**), `.Abstractions`, `.Core`
> **Yeni paket:** Yok (AgentPrism paketi) · **Yeni NuGet:** `ModelContextProtocol.AspNetCore` (GA) + Açık Soru 1'e bağlı olarak A2A · **Migration:** Yok
> **Public API:** büyüyor — iki eşleme metodu, iki ayar sınıfı. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/50-DISA-ACILAN-AGENT-YUZEYI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

[Faz 22](22-MCP-DERINLESMESI.md) AgentPrism'i MCP **istemcisi** yaptı: uzak sunucuların tool'ları katalogda görünüyor. Aynanın diğer yüzü yok — AgentPrism'in agent'ları dışarıya **hiç** açılmıyor. Bu faz o yüzü açar. Claude Code, Copilot, Cursor veya başka bir agent, AgentPrism'deki bir agent'ı doğrudan çağırabilir. Kontrol düzlemi iddiası böylece iki yönlü olur.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 Beyaz liste boşken `tools/list` **boş** döner — hiçbir agent
      varsayılan olarak açık değildir (`Bos_beyaz_liste_hicbir_tool_dondurmez`)
- [x] Beyaz listedeki agent `tools/list`'te `agentprism_{ad}` olarak görünür
- [x] `tools/call` agent'ı çalıştırır ve normal bir `runs` satırı üretir
- [x] 🚨 Çalışma anında eklenen bir agent (beyaz listede) MCP'de **yeni sunucu
      kurulmadan** görünür (`Dinamik_katalog_yeni_agent_sunucu_yeniden_kurulmadan_gorunur`)
- [x] 🚨 Onay gerektiren tool taşıyan agent dışa **açılamaz**; açılışta
      anlaşılır hata verir (MCP ve A2A, ikisi de ayrı test)
- [x] 🚨 `AllowRemoteAccess = true` + dış yüzey açık → **açılışta hata**;
      mesaj F-56'yı işaret eder (MCP ve A2A)
- [x] `MaxDepth = 1` iken dışarıdan çağrılan agent alt agent çağıramaz —
      🚨 **düzeltme (K-340):** `MaxDepth=N`, N seviye devire izin verir; test
      `MaxDepth=0` ile "hiç alt çağrı yok" sınırını doğrular
      (`Derinlik_siniri_alt_cagriyi_engeller`)
- [x] Kiracı yalıtımı korunur; başka kiracının agent'ı görünmez
      (`Kiraci_yalitimi_korunur`)
- [x] Dış çağrı denetim izine `external.call` olarak yazılır (MCP ve A2A)
- [x] A2A agent kartı beyaz listedeki agent'lar için üretilir —
      🚨 **düzeltme (K-336):** tek bir kart değil, her agent kendi
      `{prefix}/{agent}/.well-known/agent-card.json`'unda
- [x] 🚨 A2A'nın dinamik kısıtı **test edilerek** belgelenmiştir
      (`Calisma_aninda_eklenen_agent_A2Ada_gorunmez`)
- [x] 🚨 `DependencyDirectionTests`: `AgentPrism.Mcp` sunucu paketlerine bağımlı
      **değildir** (`Mcp_istemci_paketi_sunucu_paketlerine_bagli_degildir`)
- [x] Gerçek bir MCP istemcisiyle (Claude Code) **uçtan uca** çağrı yapıldı ve
      çıktı bu belgeye yazıldı — bkz. [Gerçek Doğrulama](#gerçek-doğrulama)
- [x] Dört doğrulama kapısı sıfır uyarı verir — bkz.
      [Gerçek Doğrulama](#gerçek-doğrulama) (SqlServer.IntegrationTests hariç:
      bu makinede ARM64/amd64 imaj uyuşmazlığı nedeniyle ortam kaynaklı, faza
      ilişkisiz — ayrıntı orada)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye
      yazıldı — bkz. [Gerçek Doğrulama](#gerçek-doğrulama)
- [x] `secret` taraması boş döndü (tek eşleşme `docs/hafiza/sql-server-yerel-test.md`
      içinde önceden var olan, gerçek olmayan bir değişken referansı — bu
      fazda eklenmedi)

## Gerçek Doğrulama

`samples/AgentPrism.Api` çalıştırıldı (`.UseMcpServer(o => o.ExposedAgents.Add("ozetleyici"))`,
`.UseA2A(o => o.ExposedAgents.Add("ozetleyici"))`; "ozetleyici" bilerek seçildi
çünkü tool taşımaz, onay sınırına hiç dokunmaz).

**MCP `tools/list`:**

```json
{"result":{"tools":[{"name":"agentprism_ozetleyici","description":"Gelen metni uc maddede ozetler.","inputSchema":{"type":"object","properties":{"message":{"type":"string","description":"Agent'a gonderilecek kullanici mesaji."}},"required":["message"]}}]},"id":1,"jsonrpc":"2.0"}
```

**MCP `tools/call`:**

```json
{"result":{"content":[{"type":"text","text":"- Bugün hava çok güzeldi.\n- İş yerinde her şey yolunda gitti.\n- Toplantılar verimliydi."}]},"id":2,"jsonrpc":"2.0"}
```

**A2A agent kartı** (`GET /agentprism/a2a/ozetleyici/.well-known/agent-card.json`):

```json
{"name":"ozetleyici","description":"Gelen metni uc maddede ozetler.","version":"1","supportedInterfaces":[{"url":"/agentprism/a2a/ozetleyici","protocolBinding":"JSONRPC","protocolVersion":"1.0"}],"capabilities":{"streaming":false,"pushNotifications":false},"defaultInputModes":["text/plain"],"defaultOutputModes":["text/plain"]}
```

**A2A `SendMessage`:**

```json
{"jsonrpc":"2.0","id":1,"result":{"message":{"role":"ROLE_AGENT","parts":[{"text":"- A2A protokolü üzerinden mesaj gönderildi.\n- Mesaj başarıyla agente ulaştı.\n- Mesaj işlendi."}],"messageId":"chatcmpl-EALwaI6FeRRYEZxT8mVTDZq8QmbqW","contextId":"a6b3eff65ac849c4b274661d554db203"}}}
```

**Her ikisi de gerçek bir `runs` satırı ve `external.call` denetim kaydı üretti**
(`GET /agentprism/api/runs`, `GET /agentprism/api/audit?action=external.call`
ile doğrulandı — sırasıyla 2 satır, `protocol` alanı `mcp`/`a2a`).

**Gerçek MCP istemcisi — Claude Code CLI:**

```
$ claude mcp add --transport http agentprism-test http://localhost:5080/agentprism/mcp -s local
Added HTTP MCP server agentprism-test with URL: http://localhost:5080/agentprism/mcp to local config

$ claude mcp get agentprism-test
agentprism-test:
  Scope: Local config (private to you in this project)
  Status: ✔ Connected
  Type: http
  URL: http://localhost:5080/agentprism/mcp
```

`✔ Connected`, gerçek MCP `initialize` el sıkışması geçti (basit bir HTTP `200`
değil). Doğrulama sonrası `claude mcp remove agentprism-test -s local` ile
temizlendi.

**Dört doğrulama kapısı** (2026-08-08, `AgentPrism.slnx`, tam çözüm):

| Kapı | Sonuç |
|---|---|
| `dotnet build -c Release` | 0 uyarı, 0 hata |
| `dotnet test -c Release --no-build` | Tüm derlemeler yeşil **SqlServer.IntegrationTests hariç** — `SqlServerFixture` Testcontainers ile mssql imajını başlatırken `TimeoutException` veriyor; kök sebep `WARNING: The requested image's platform (linux/amd64) does not match the detected host platform (linux/arm64/v8)` (bu oturumun makinesi Apple Silicon). İzole tekrar (`dotnet test tests/AgentPrism.SqlServer.IntegrationTests`) aynı sonucu verdi; bu fazda `AgentPrism.SqlServer`'a hiçbir dosya dokunulmadı — ortam kısıtı, kod regresyonu değil |
| `dotnet pack -c Release --no-build` | 236 `.nupkg`/`.snupkg`, 16 paketin `.52` sürümü dahil sıfır hata |
| `dotnet format --verify-no-changes` | Sıfır fark |
| `secret` taraması | Tek eşleşme, bu fazdan önce var olan gerçek olmayan bir değişken referansı (yukarı bakınız) |

### Doğrulama komutları

```bash
# 1) Beyaz liste bos — HICBIR tool gorunmemeli
curl -s -X POST http://localhost:5081/agentprism/mcp \
  -H "content-type: application/json" \
  -H "Authorization: Bearer $AGENTPRISM_TOKEN" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | jq '.result.tools | length'
#    beklenen: 0

# --- ornek uygulamada acilir: ExposedAgents = ["asistan"] ---

# 2) Tool listesi
curl -s -X POST http://localhost:5081/agentprism/mcp \
  -H "content-type: application/json" \
  -H "Authorization: Bearer $AGENTPRISM_TOKEN" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' \
  | jq '.result.tools[] | {name, description}'

# 3) Tool cagrisi -> gercek bir runs satiri
BEFORE=$(psql -tA "$AGENTPRISM_CONN" -c "SELECT count(*) FROM agentprism.runs;")
curl -s -X POST http://localhost:5081/agentprism/mcp \
  -H "content-type: application/json" \
  -H "Authorization: Bearer $AGENTPRISM_TOKEN" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call",
       "params":{"name":"agentprism_asistan","arguments":{"message":"merhaba"}}}' | jq
AFTER=$(psql -tA "$AGENTPRISM_CONN" -c "SELECT count(*) FROM agentprism.runs;")
echo "runs: $BEFORE -> $AFTER  (bir artmali)"

# 4) 🚨 Dinamik katalog — yeni agent, sunucu yeniden kurulmadan gorunmeli
curl -s -X POST http://localhost:5081/agentprism/api/agents \
  -H "content-type: application/json" \
  -d '{"name":"yeni-agent","instructions":"...","model":{"provider":"openai","modelId":"gpt-5-mini"}}'
#    (ExposedAgents listesine eklendikten sonra)
curl -s -X POST http://localhost:5081/agentprism/mcp \
  -H "content-type: application/json" -H "Authorization: Bearer $AGENTPRISM_TOKEN" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/list"}' | jq '.result.tools | length'

# 5) Kimliksiz istek reddedilmeli
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/agentprism/mcp \
  -H "content-type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/list"}'

# 6) A2A agent karti
curl -s http://localhost:5081/agentprism/a2a/.well-known/agent-card.json \
  -H "Authorization: Bearer $AGENTPRISM_TOKEN" | jq

# 7) Denetim izi
curl -s "http://localhost:5081/agentprism/api/audit?action=external.call" | jq 'length'

# 8) 🚨 Bagimlilik yonu — AgentPrism.Mcp sunucu paketi ALMAMALI
dotnet list src/AgentPrism.Mcp/AgentPrism.Mcp.csproj package --include-transitive \
  | grep -i "ModelContextProtocol.AspNetCore" || echo "TEMIZ"

# 9) Gercek istemci — Claude Code MCP yapilandirmasi ile
#    (cikti bu belgeye yazilir)
```

---

## Plandan Sapmalar

Plan ile gerçek arasındaki fark gizlenmez — beşi de gerçek koşumda (fonksiyonel
test veya `samples/AgentPrism.Api`) ortaya çıktı, plan taslağının ölçümünde
görünmüyordu:

1. **MCP sunucu kaydı üçüncü bir çağrı ister: `WithHttpTransport()`.** Plan
   taslağı yalnız `AddMcpServer().WithListToolsHandler/WithCallToolHandler`
   biliyordu; bu üçüncü çağrı olmadan `MapMcp()` açılışta
   `InvalidOperationException: You must call WithHttpTransport()` verir.
   Ayrıntı: `docs/hafiza/mcp-a2a-sunucu.md`, K-334.
2. **A2A'nın ölçülen ek maliyeti "+2 paket" değil "+4 paket".** Plan yalnız
   `Microsoft.Agents.AI.Hosting.A2A` + `A2A`'yı ölçmüştü. `AddA2AServer` yalnız
   DI KAYDI yapar; HTTP ucunu açan `MapA2A`/`MapWellKnownAgentCard` uzantıları
   plan taslağında hiç geçmeyen **ayrı bir paket** olan `A2A.AspNetCore`'dadır,
   o da `Microsoft.Agents.AI.Hosting.AspNetCore`'u ister. Dördü de aynı SDK
   ailesinden, yabancı bağımlılık yok — yalnızca sayı düzeltildi. K-335.
3. **A2A tek bir `.well-known/agent-card.json` yayımlayamaz.** Plan taslağının
   doğrulama örneği tekil bir kart varsayıyordu. A2A protokolü bir sunucuyu
   bir agent kimliği olarak modeller; birden çok agent AYRI alt yollara
   (`{prefix}/{agent}`) bağlanır, her biri kendi kartını taşır. K-336.
4. **Dış çağrı `ChildAgentInvoker`'ı kullanmaz.** Plan bunu açıkça belirtmiyordu
   ama örtük varsayımı ("aynı sınır katmanı yeniden kullanılır") bu sınıfın
   AMBIENT bir üst kapsam beklediği gerçeğiyle çelişirdi — dış çağrının böyle
   bir kapsamı yoktur. İki küçük, ayrı sınıf yazıldı: `CatalogToolCallHandler`
   (MCP) ve `ExternalAgentProxy` (A2A, `CallableAgentResolver`'ın aynı
   "geç çözüm" deseniyle). K-337.
5. **`MaxDepth` semantiği DoD'nin sözel iddiasından farklı.** "`MaxDepth=1`
   iken alt agent çağıramaz" yanlıştı; `ChildAgentInvoker.Refuse()`'un
   `Depth+1 > MaxDepth` kuralı `MaxDepth=1`'de BİR seviyeye izin verir.
   Varsayılan yine de 1 bırakıldı (Açık Soru 6'nın gerekçesi geçerli); test
   `MaxDepth=0` ile gerçek "hiç alt çağrı yok" sınırını doğruladı. K-340.

Ayrıca: `MapAgentPrismMcpServer`/`MapAgentPrismA2A` planın taslak imzasından
(`Action<TOptions>? configure` parametreli) SAPTI — MCP'de `configure`
kaldırıldı (ayarlar `UseMcpServer()`'da, IServiceCollection zamanında
sabitlenir; K-251 deseni: `Map...` yalnız zaten kurulmuş servisleri HTTP'ye
bağlar) ve A2A'da hiç yoktu (`ExposedAgents` kayıt anında sabit, K-336/K-337).

## Bu Fazda Verilen Kararlar

K-334 – K-340. Tam gerekçeler `docs/KARARLAR.md`'de (grep ile):

```bash
grep -n "K-334\|K-335\|K-336\|K-337\|K-338\|K-339\|K-340" docs/KARARLAR.md
```

Özet:

| Karar | Ne |
|---|---|
| K-334 | K-057 güncellendi: AgentPrism artık MCP istemcisi VE sunucusu; `WithHttpTransport()` zorunluluğu ölçüldü |
| K-335 | A2A'nın gerçek ek maliyeti 4 paket (2 değil); `A2A.AspNetCore` + `Microsoft.Agents.AI.Hosting.AspNetCore` plan taslağında yoktu |
| K-336 | A2A agent başına ayrı alt yol + ayrı kart; tekil kart varsayımı terk edildi |
| K-337 | Dış çağrı `ChildAgentInvoker` kullanmaz; her zaman YENİ bir kök çalıştırma (`CatalogToolCallHandler`/`ExternalAgentProxy`) |
| K-338 | Erişim ayarları `IApplicationBuilder.Properties` ile `MapAgentPrism`'den devralınır; `MapAgentPrism` önce çağrılmalı |
| K-339 | `ChildRunApproval` public yapıldı — üçüncü tüketici MCP/A2A dış çağrı katmanı |
| K-340 | `MaxDepth=N` → N seviye devire izin verir (0 değil); DoD cümlesi düzeltildi, davranış (Faz 12) değişmedi |

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `MapAgentPrismMcpServer(pattern)` ve `MapAgentPrismA2A(pattern)` —
  `MapAgentPrism(...)`'den **SONRA** çağrılmalı (`RequireSharedEndpointOptions`
  aksi halde `InvalidOperationException` fırlatır).
- `builder.UseMcpServer(o => ...)` / `builder.UseA2A(o => ...)` —
  `IServiceCollection` zamanında (Build() öncesi) çağrılmalı.
- `ChildRunApproval` artık **public**; yeni bir dış çağrı yüzeyi (varsa) aynı
  tespiti yeniden yazmadan kullanabilir.

**Bilinen tuzaklar (🚨) — ayrıntı `docs/hafiza/mcp-a2a-sunucu.md`:**
1. `AddMcpServer()` sonrası `.WithHttpTransport()` unutulursa açılışta patlar.
2. `AddA2AServer`'ın keyed kaydı `IA2ARequestHandler` değil somut `A2AServer`'dır.
3. `A2A.AspNetCore.MapA2A(..., path)` boş `path` kabul etmez (`MapWellKnownAgentCard` eder).
4. A2A JSON-RPC gövdesi PascalCase + `"SendMessage"` (spec'in `message/send` biçimi DEĞİL) + `Role` `"ROLE_USER"`/`"ROLE_AGENT"` yazar — elle yazma, `A2AJsonUtilities.DefaultOptions` ile serileştir.

**Yarım kalan / ertelenen işler (aynen plandan devraldı, değişmedi):**
1. 🚨 **F-56 (kiracı bazlı API anahtarları) artık ACİLDİR.** Bu faz dış bir
   yüzey açtı ve onu tek statik bir token koruyor. `AllowRemoteAccess` kilidi
   geçici bir savunmadır; F-56 yapıldığında kaldırılabilir.
2. **A2A istemci tarafı** (`Microsoft.Agents.AI.A2A`, client paketi —
   `Google.Protobuf` taşır, sunucu paketlerinden AYRI ölçülmelidir) yeni bir
   aday kalemidir. K-008 gereği yerleşimi ayrıca kararlaştırılmalıdır.
3. **MCP kaynak ve istem yayını** (`resources`, `prompts`) yeni bir aday kalemidir.
4. **Akışlı MCP/A2A yanıtı ölçülmedi** (Açık Soru 4). `AgentCapabilities.Streaming = false`
   olarak bırakıldı; ileride ölçülürse taahhüt buraya yazılır.
5. **A2A agent kartının `Url` alanı GÖRELİ yol taşır** (`/agentprism/a2a/{agent}`),
   mutlak değil — çalışma anında uygulamanın genel host adresi (ters vekil
   arkasında olabilir) bilinmiyor. Gerçek bir A2A istemcisi mutlak URL
   beklerse tüketici kendi kartını üretmelidir; bu bir aday kalemi olabilir.
>    protokolde var; ölçüm yapılırsa sonucu buraya yazılmalıdır.
