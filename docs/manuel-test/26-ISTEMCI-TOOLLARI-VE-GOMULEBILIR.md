# 26 — İstemci Tool'ları ve Gömülebilir Sohbet (`IST`)

> **Alan kodu:** `IST` · **Faz:** 61, 112
> **Kaynak:** `src/AgentPrism.Core/Tools/AgentPrismClientToolExtensions.cs` ·
> `src/AgentPrism.Core/Tools/ToolRegistry.cs` ·
> `src/AgentPrism.AspNetCore/Internal/ClientToolResultResolver.cs` ·
> `src/AgentPrism.AspNetCore/Internal/AgentPrismCorsMiddleware.cs` ·
> `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs` (`toolResults`
> handling) · `src/AgentPrism.AspNetCore/AgentPrismEndpointOptions.cs`
> (`AllowedOrigins`) · `src/AgentPrism.UI/frontend/src/embed/` (widget) ·
> `samples/AgentPrism.Api/Program.cs` (`read_shopping_cart`) ·
> `src/AgentPrism.Core/Replay/RunReplayService.cs` (`FindClientTool`, Faz 112).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

Bir tool'un **tanımı** kodda kalır (K2), ama **gövdesi** artık sunucuda
çalışmayabilir — tarayıcıda çalışır. Sunucu modelin çağrısını
(`FunctionCallContent`) çağırana döner; çağıran sonucu `toolResults` ile
geri gönderir ve tur öyle tamamlanır. Bu, gömülebilir bir sohbet kutusunun
kendi sayfasındaki veriyi (sepet, DOM, seçili öğe) agent'a vermesini sağlar.

```mermaid
sequenceDiagram
    participant B as Tarayici (embed widget)
    participant S as AgentPrism

    B->>S: POST /run {"message":"sepetimde ne var?"}
    S->>S: Model read_shopping_cart cagirir
    S-->>B: functionCall (govde YOK - sunucu calistirmadi)
    B->>B: read_shopping_cart(args) calistirir (yerel)
    B->>S: POST /run {"sessionId","toolResults":[{"callId","result"}]}
    S-->>B: nihai metin
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Onay akışının GENEL sözleşmesi (`Approvals`, `pending_approvals`) | `13-KIRACI-VE-GUVENLIK.md` / Faz 55'in kendi kapanışı — bu dosya yalnız istemci tool'unun onaydan **ayrık** olduğunu sınar |
| MCP tool keşfi ve onayı | `18-MCP-VE-A2A.md` |
| Konsolun genel yerelleştirme/rozet mekaniği | `09-ARAYUZ-GENEL.md` — bu dosya yalnız `runsOnClient` rozetinin KENDİSİNİ sınar |
| Guard'ın genel engelleme davranışı | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` — bu dosya yalnız `toolResults` girdisinin guard'dan **geçtiğini** sınar |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` → `http://localhost:5080`.
   ```bash
   export APB="Authorization: Bearer manuel-test-token-2026"
   export APU="http://localhost:5080/agentprism"
   ```
3. `support` agent'ı örnek uygulamada `read_shopping_cart` istemci tool'unu
   zaten taşır (Faz 61 kapanışında eklendi) — ayrı bir kayıt gerekmez.

> **Gerçek para uyarısı.** §1 ve §2'deki her case gerçek bir OpenAI çağrısı
> yapar (küçük ölçekte, `gpt-5.4-mini`). §3 (CORS) hiçbir model çağırmaz.

---

# 1 — İstemci tool'u: çağrı döner, sonuç turu tamamlar

### MT-IST-001 — İstemci tool'u çağrılır ama sunucuda ÇALIŞMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 61 |
| **İlgili karar** | K2 (bkz. `MIMARI.md` §3) |

**Ön koşul**
- Örnek uygulama çalışıyor; `support` agent'ı `read_shopping_cart` taşıyor.

**Adımlar**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: mt-ist-001' \
  -d '{"sessionId":"mt-ist-001","message":"sepetimde ne var?"}' | jq
```

**Beklenen sonuç**
- Yanıt `response.messages[].contents[]` içinde tek bir `functionCall`
  taşır (`name: "read_shopping_cart"`); `functionResult` **yoktur**.
- `finishReason: "tool_calls"`.
- Sunucu logunda `read_shopping_cart`'ın çalıştığına dair **hiçbir iz yoktur**
  (karşılaştırma: `get_order_status` çağrıldığında log satırı üretir).

---

### MT-IST-002 — `toolResults` ile sonuç gönderilince tur tamamlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 61 |
| **İlgili karar** | — |

**Ön koşul**
- MT-IST-001 çalıştırıldı; yanıttaki `callId` elde tutuluyor.

**Adımlar**
```bash
CALL_ID="<MT-IST-001 yanitindaki callId>"

curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: mt-ist-002' \
  -d "{\"sessionId\":\"mt-ist-001\",\"toolResults\":[{\"callId\":\"$CALL_ID\",\"result\":\"2x Kablosuz Fare\"}]}" | jq
```

**Beklenen sonuç**
- `200`; yanıt metni sepet içeriğinden (`2x Kablosuz Fare`) bahseder.
- `finishReason: "stop"`.

---

### MT-IST-003 — Aynı `callId` ikinci kez yanıtlanır → `409`

**Ön koşul:** MT-IST-002 tamamlandı (aynı `callId` bir kez yanıtlandı).

**Adımlar:** MT-IST-002'nin isteğini **aynen** tekrar gönder.

**Beklenen sonuç:** `409 Conflict`; `ProblemDetails.title` "Tool call already answered".

---

### MT-IST-004 — Bilinmeyen `callId` ile sonuç gönderilir → `400`

**Adımlar**
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-ist-004' \
  -d '{"sessionId":"mt-ist-001","toolResults":[{"callId":"hic-var-olmayan","result":"x"}]}'
```

**Beklenen sonuç:** `400`; mesaj bilinmeyen `callId`'yi açıklar.

---

### MT-IST-005 — `sessionId` olmadan `toolResults` gönderilir → `400`

**Adımlar**
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-ist-005' \
  -d '{"toolResults":[{"callId":"x","result":"y"}]}'
```

**Beklenen sonuç:** `400`; mesaj `sessionId`'nin zorunlu olduğunu açıklar.

---

### MT-IST-006 — Kuyruk yolunda (`Prefer: respond-async`) `toolResults` reddedilir

**Adımlar**
```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H 'Prefer: respond-async' -H 'content-type: application/json' \
  -d '{"sessionId":"mt-ist-001","toolResults":[{"callId":"x","result":"y"}]}'
```

**Beklenen sonuç:** `400`; mesaj kuyruk kısıtını açıklar (bağlı tarayıcı yok).

---

### MT-IST-007 — `errorMessage` model'e ulaşır, `result` yerine kullanılır

**Ön koşul:** Yeni bir oturumla (`mt-ist-007`) MT-IST-001 tekrarlanır, yeni bir `callId` elde edilir.

**Adımlar**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: mt-ist-007b' \
  -d "{\"sessionId\":\"mt-ist-007\",\"toolResults\":[{\"callId\":\"<callId>\",\"errorMessage\":\"kullanici izin vermedi\"}]}" | jq
```

**Beklenen sonuç:** `200`; model'in yanıtı sepeti okuyamadığını/hata olduğunu belirtir (`result` DEĞİL `errorMessage` temelinde), tur yine de **tamamlanır** — `400` DEĞİL.

---

# 2 — Güvenlik sınırları

### MT-IST-008 — İstemciden gelen sonuç guard'dan geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48, Faz 61 (§61.4) |

**Ön koşul:** `AddPatternContentGuard` ile bir yasaklı terim tanımlı örnek kurulum (bkz. `22-GUARDRAIL-VE-YAPISAL-CIKTI.md`'nin kurulum adımı) veya bu davranış zaten `ClientToolGuardTests` ile otomatik koşulur — bu case onu **gerçek** bir kurulumla teyit eder.

**Adımlar:** MT-IST-001'i tekrarla, sonra `toolResults`'a yasaklı terimi taşıyan bir `result` gönder.

**Beklenen sonuç:** `422 Unprocessable Entity`; `errorType: "content_blocked"`; yanıt gövdesi yasaklı terimi **taşımaz**.

---

### MT-IST-009 — Başka kiracının oturumuna sonuç yazılamaz

**Ön koşul:** Çok kiracılılık açık (`UseTenancy`).

**Adımlar:** `tenant-a` başlığıyla MT-IST-001'i çalıştır, `callId`'yi al; aynı `sessionId` + `callId` ile `tenant-b` başlığıyla `toolResults` gönder.

**Beklenen sonuç:** `400` (tenant-b için oturum/çağrı bulunamaz — tenant-a'nın bekleyen çağrısına asla erişilemez).

---

# 3 — CORS ve gömülebilir bileşen

### MT-IST-010 — `AllowedOrigins` boşken CORS başlığı yollanmaz

**Adımlar**
```bash
curl -s -D - -o /dev/null "$APU/api/meta" -H "Origin: https://baska-site.example.com" | grep -i access-control
```

**Beklenen sonuç:** Çıktı **boş** — `Access-Control-Allow-Origin` başlığı yok.

---

### MT-IST-011 — `AllowedOrigins` açıldığında izin verilen origin geçer

**Ön koşul:** `samples/AgentPrism.Api/appsettings.json` → `AgentPrism:Ui:AllowedOrigins`'e `https://baska-site.example.com` eklenir, uygulama yeniden başlatılır.

**Adımlar:** MT-IST-010'un aynısını tekrarla.

**Beklenen sonuç:** `Access-Control-Allow-Origin: https://baska-site.example.com` başlığı döner.

---

### MT-IST-012 — Gömülebilir bileşen tarayıcıda çalışır ve turu tamamlar 👤

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 61 |

**Ön koşul**
- MT-IST-011 uygulandı (CORS açık).
- `RunsWrite` kapsamlı bir kiracı API anahtarı üretildi (`13-KIRACI-VE-GUVENLIK.md`).
- Basit bir statik HTML dosyası hazırlanır (farklı bir origin/porttan sunulur, örn. `python3 -m http.server 8099`):
  ```html
  <script src="http://localhost:5080/agentprism/embed/embed.js"
    data-server="http://localhost:5080" data-prefix="/agentprism"
    data-agent="support" data-api-key="<uretilen anahtar>"></script>
  <script>
    window.AgentPrismEmbed.registerTool('read_shopping_cart', () => '3x Klavye');
  </script>
  ```

**Adımlar**
1. Sayfayı tarayıcıda aç (`http://localhost:8099`).
2. Sağ alttaki sohbet balonuna tıkla, "sepetimde ne var?" yaz, gönder.

**Beklenen sonuç**
- Balonun içinden "Çalışıyor…" durumu görünür, ardından `read_shopping_cart`
  kayıtlı işlev çalışır ve nihai yanıt "3x Klavye"den bahseder.
- Tarayıcı konsolunda CORS hatası **yoktur**.
- 👤 **İnsan gerekir** — gerçek bir tarayıcıda, gerçek bir üçüncü taraf
  sayfasından uçtan uca etkileşim otomatikleştirilmedi; statik-varlık
  sunumu (`Embed_widget_script_is_served_with_the_real_built_bundle`) ve
  Tools ekranı rozeti (`Tools_screen_shows_a_client_side_badge_for_a_client_tool`)
  Playwright ile otomatik koşulur, bu case onların ÜSTÜNE gerçek bir
  kullanıcı akışını doğrular.

---

### MT-IST-013 — Konsolun Tools ekranı `runsOnClient` rozetini gösterir

**Adımlar:** `$APU`'yu tarayıcıda aç → Tools → `read_shopping_cart` kartını bul.

**Beklenen sonuç:** Kartta "istemci taraflı" (`tr`) / "client-side" (`en`) rozeti görünür; `get_order_status` kartında bu rozet **yoktur**.

---

### MT-IST-014 — Persistent bir agent'ın istemci tool'u REPLAY'İ HER ÜÇ MOD'DA reddeder (Faz 112)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 112 |

**Ön koşul**
- Yalnız `read_shopping_cart`'ı taşıyan, veritabanı tanımlı (persistent) bir agent oluşturulur:
  ```bash
  curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
    -d '{"name":"manuel-ist-replay","model":{"provider":"echo","model":"echo-1"},"toolNames":["read_shopping_cart"]}'
  ```
- O agent ile bir run tamamlanır ve girdisi kaydedilir:
  ```bash
  curl -s -X POST "$APU/api/agents/manuel-ist-replay/run" -H "$APB" -H "content-type: application/json" \
    -H 'Idempotency-Key: mt-ist-014' -d '{"message":"merhaba"}' | jq '.runId'
  ```
  Yukarıdaki `runId`'yi `<RUN_ID>` yerine koy.

**Adımlar**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$APB" \
     -H "content-type: application/json" -d '{"toolMode":"ReplayTools"}'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$APB" \
     -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$APB" \
     -H "content-type: application/json" -d '{"toolMode":"NoTools"}'
```

**Beklenen sonuç**
- Üç isteğin **hepsi** `HTTP: 409` döner.
- Her yanıtın `detail` alanı `read_shopping_cart` adını ve "cannot be replayed in any tool mode" ibaresini taşır.
- `NoTools` da reddedilir: o modda hiçbir tool bağlanmaz, ama istemci tool'u agent'ın **tanımının** bir parçası olduğu için sonuç yine de karşılaştırılamaz kabul edilir (bkz. plan §112.2, Açık Soru 1).

---

### MT-IST-015 — Kod tanımlı (`support`) agent'ta da replay 409 döner (katalog kolu, Faz 112)

`support` agent'ı hem `cancel_order`'ı (onay gerektirir) hem `read_shopping_cart`'ı
(istemci tool'u) taşır; bu case katalog kolunun (kod tanımlı agent) hiçbir
şekilde delikte kalmadığını doğrular — hangi guard'ın önce tetiklendiği bu
case açısından önemli değildir. `read_shopping_cart`'ın kendi mesajının
izole gösterimi `ReplayClientToolEndpointTests.A_code_defined_agent_carrying_a_client_side_tool_is_also_rejected`
otomatik testinde (yalnız istemci tool'u taşıyan ayrı bir agent ile) kanıtlanır.

**Ön koşul:** `support` agent'ından tamamlanmış bir run (ör. MT-IST-001'in `runId`'si).

**Adımlar**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/runs/<RUN_ID>/replay" -H "$APB" \
     -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'
```

**Beklenen sonuç:** `HTTP: 409`. `detail` `cancel_order` veya `read_shopping_cart` adını taşır (hangisi olduğu guard sırasına bağlıdır); ikisi de kabul edilir sonuçtur.

---

### MT-IST-016 — Konsolun run detayı "Replay" düğmesi 409'u okunabilir gösterir 👤

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 112 |

**Ön koşul:** MT-IST-014'ün `manuel-ist-replay` run'ı hâlâ var.

**Adımlar**
1. `$APU`'yu tarayıcıda aç → Runs → MT-IST-014'ün run'ını bul → run detayına gir.
2. "Replay" düğmesine tıkla, varsayılan modla (`ReplayTools`) onayla.

**Beklenen sonuç**
- Ekran sunucunun `detail` metnini (tool adını içeren) bir hata olarak gösterir.
- Ekran **boş kalmaz** ve sonsuz "yükleniyor" durumunda takılmaz.
- 👤 **İnsan gerekir** — hata metninin gerçek bir tarayıcıda okunabilir
  biçimde render edildiğini doğrulamak otomatikleştirilmedi.

---

## Bitiş ölçütü

- [ ] MT-IST-001 … MT-IST-016 çalıştırıldı, sonuç `kosumlar/<tarih>/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`'ye yazıldı.
- [ ] `Kaldı` işaretlenen her case için `00-INDEKS.md` §6 şablonuyla bir `HATA-NNN` açıldı.
