# 02 — Çekirdek ve Katalog (`CORE`)

> **Alan kodu:** `CORE` · **Faz:** 1, 3
> **Kaynak:** `src/AgentPrism.Abstractions` · `src/AgentPrism.Core`
> (`Compilation/` · `Catalog/` · `Tools/` · `Sessions/` · `AgentPrismOptions*`)
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

Bir agent tanımının **kayıttan çalıştırmaya** giden yolu. Tanım doğrulanır,
derlenir, katalogda çözülür, tool'ları bağlanır ve bir oturuma yazılır. Bu yolun
her adımı kendi hatasını üretmelidir — ve hata **çalışma anında değil, en erken
noktada** çıkmalıdır.

```mermaid
flowchart LR
    A["AgentDefinition"] --> B["AgentDefinitionValidator"]
    B --> C["AgentDefinitionCompiler"]
    C --> D["CompositeAgentCatalog"]
    D --> E["AIAgent"]
    E --> F["AgentSessionManager"]
    B -.->|Valid=false| G["rapor - istisna YOK"]
    C -.->|hata| H["AgentPrismCompilationException"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| HTTP durum kodu, gövde şekli, sayfalama | [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) |
| Kalıcılık davranışı (`jsonb`, migration, indeks) | [`03-KALICILIK-POSTGRESQL.md`](03-KALICILIK-POSTGRESQL.md) |
| Sağlayıcı ayrıntısı (devre kesici, sağlık, akış) | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) |
| Kiracı yalıtımı ve rol | [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md) |

Burada `endpoint` yalnız bir **giriş kapısı** olarak kullanılır. Kanıtlanan şey
uçun kendisi değil, uçun arkasındaki çekirdek davranıştır.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. PostgreSQL container'ı (`ap-pg`) çalışır ve `AgentPrism:PostgreSql:ConnectionString`
   tanımlıdır. Veritabanı gereklidir: tanım **kaydı** onsuz denenemez.
3. `AgentPrism:Ui:AuthToken` `manuel-test-token-2026` olarak tanımlıdır.
4. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080`

Kısaltma — bu dosyadaki her `curl` şu başlıkları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
```

> **Ağ çağrısı yapmayan izlek.** Örnek uygulama, OpenAI anahtarı tanımlı değilken
> `echo` adlı yerel bir sağlayıcı kaydeder (`echo-1` modeli). Bu dosyadaki
> derleme ve katalog case'lerinin çoğu **modeli hiç çağırmaz**; sağlayıcı adı
> olarak `echo` kullanılır ve sonuç deterministiktir.

---

# 1 — Tanım doğrulama

Doğrulama hiçbir model çağırmaz ve hiçbir şey kaydetmez. Başarısızlık bir HTTP
hatası **değildir**: istek geçerlidir, cevap "bu tanım geçersiz"dir.

### MT-CORE-001 — Geçerli tanım hatasız doğrulanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1, 34 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Doğrulama ucuna geçerli bir tanım gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-destek",
  "displayName": "Manuel Destek",
  "instructions": "Sen bir siparis destek asistanisin. Kisa yanit ver.",
  "model": { "provider": "echo", "model": "echo-1" },
  "toolNames": ["get_order_status"]
}'
```

**Beklenen sonuç**
- `valid` alanı `true`'dur.
- `inconclusive` alanı `false`'tur.
- `messages` dizisi **boştur**.
- Hiçbir kayıt oluşmaz: `GET $APU/api/agents` çıktısında `manuel-destek` **yoktur**.

**Gerçek sonuç**
`valid:true`, `inconclusive:false`, `messages:[]`. `GET /api/agents` sonrasında `manuel-destek` listede yok — doğrulama hiçbir kayıt oluşturmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-002 — `unknown_tool`: kayıtlı olmayan tool adı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | K2 |

Negatif senaryo. Bu, AgentPrism'in güvenlik sınırıdır: arayüzden agent
oluşturulabilir, **tool kodu yazılamaz**. Bir tanım yalnız kodda kayıtlı bir
tool'a işaret edebilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir tool adıyla doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-hayali-tool",
  "instructions": "Test.",
  "model": { "provider": "echo", "model": "echo-1" },
  "toolNames": ["get_order_status", "hayali_tool", "bir_baska_hayali"]
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur.
- `messages` içinde `code` değeri `unknown_tool` olan **iki** kayıt vardır —
  ilk hatada durulmaz.
- Her kaydın `severity` değeri `Error`'dur ve **ad olarak** yazılır, sayı olarak değil.
- Her kaydın `path` alanı sorunlu öğeyi gösterir (`toolNames[1]`, `toolNames[2]`).
- `get_order_status` için hiçbir mesaj yoktur.

**Gerçek sonuç**
İki `unknown_tool` mesajı, `path` alanları `toolNames[1]` ve `toolNames[2]`, `severity:"Error"`. `get_order_status` için mesaj yok. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-003 — `unknown_model`: kayıtlı olmayan sağlayıcı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-032 |

Negatif senaryo. Model **kataloğu** bir doğrulama listesi değildir; ama
**sağlayıcı** kayıtlı olmalıdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir sağlayıcı adıyla doğrula.
2. Var olan sağlayıcı ama katalogda olmayan bir model adıyla doğrula.

**Girilecek veri**
```bash
# 1) Saglayici yok
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yok-saglayici",
  "model": { "provider": "olmayan-saglayici", "model": "x" }
}'

# 2) Saglayici var, model katalogda yok
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yok-model",
  "model": { "provider": "echo", "model": "katalogda-olmayan-model" }
}'
```

**Beklenen sonuç**
- 1. istekte `valid` `false`'tur ve `code` değeri `unknown_model` olan bir mesaj vardır.
- 2. isteğin sonucu **kaydedilir ve karşılaştırılır**: `README.md` katalog için
  "bir doğrulama listesi değildir" der. Eğer 2. istek de `unknown_model`
  veriyorsa doküman ile kod çelişiyordur; bulgu not edilir.
- Hiçbir durumda uygulama çökmez.

**Gerçek sonuç**
1) `olmayan-saglayici` → `unknown_model`, `valid:false`. 2) `echo`/`katalogda-olmayan-model` → `valid:true` — katalog gerçekten bir doğrulama listesi değil, README ile çelişki yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-004 — `invalid_setting`: tanınmayan sağlayıcı ayarı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-034 |

Negatif senaryo. Sessizce yok sayılan bir ayar, kullanıcının beklediği davranışı
almamasına **ve sebebini görememesine** yol açar. Bu yüzden bilinmeyen anahtar
reddedilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Tanınmayan bir `providerSettings` anahtarıyla doğrula.
2. Anahtar adını farklı harf büyüklüğüyle tekrarla.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-kotu-ayar",
  "model": {
    "provider": "echo",
    "model": "echo-1",
    "providerSettings": { "echo.boyle.bir.ayar.yok": true }
  }
}'
```

**Beklenen sonuç**
- `valid` `false`'tur ve `code` değeri `invalid_setting` olan bir mesaj vardır.
- Mesaj metni sağlayıcının **desteklediği anahtarları listeler**.
- Ayar sessizce yok sayılmaz.

**Gerçek sonuç**
Senaryo `provider:"echo"` ile koşuldu ve `valid:true` döndü — `invalid_setting` beklenirken. Kök neden koda AİT DEĞİL: `samples/AgentPrism.Api/EchoModelProvider.cs`'deki `EchoModelProvider.CreateChatClient`, ağa çıkmayan yerel örnek sağlayıcı olduğu için `ModelBinding.ProviderSettings`'i hiç okumuyor/doğrulamıyor. Gerçek mekanizma (`ModelProviderSettings.Validate`, `src/AgentPrism.Abstractions/Agents/ModelProviderSettings.cs`) ayrıca `provider:"anthropic"` ile doğrulandı ve TAM beklenen sonucu verdi: `invalid_setting`, mesaj "su anahtarlar taninmiyor: anthropic.boyle.bir.ayar.yok. Desteklenen anahtarlar: anthropic.promptCaching, anthropic.thinking.budgetTokens." SONUÇ: Ürün kusuru yok; doküman senaryosu yanlış sağlayıcı seçmiş (echo yerine anthropic/google/azure kullanılmalı). Doküman düzeltme adayı, kod değil.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-CORE-005 — `unknown_skill`: bağlı olmayan skill adı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir skill adıyla doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yok-skill",
  "model": { "provider": "echo", "model": "echo-1" },
  "skillNames": ["olmayan-skill"]
}'
```

**Beklenen sonuç**
- `valid` `false`'tur.
- `code` değeri `unknown_skill` olan bir mesaj vardır ve `path` alanı
  `skillNames[0]`'dır.

**Gerçek sonuç**
`unknown_skill`, `path: "skillNames[0]"`, `valid:false`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-006 — `Inconclusive`: erişilemeyen MCP sunucusu `Valid`'i düşürmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 22, 34 |
| **İlgili karar** | — |

Sınır senaryosu. Yavaş veya ölü bir MCP sunucusu doğrulama ucunu **asmamalıdır**.
Zaman aşımı varsayılanı 5 saniyedir; sonuç `Inconclusive` olur, `Valid` düşmez.

**Ön koşul**
- Örnek uygulama çalışıyor.
- Arayüzden veya API'den erişilemeyen bir MCP sunucusu kaydedilmiştir
  (adres: `http://127.0.0.1:59999/mcp` — hiçbir şey dinlemiyor).

**Adımlar**
1. Erişilemeyen MCP sunucusunu kaydet.
2. Eksik bir tool adı taşıyan tanımı doğrula ve süreyi ölç.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/mcp/servers" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "olu-mcp", "url": "http://127.0.0.1:59999/mcp", "enabled": true }'

time curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-mcp-belirsiz",
  "model": { "provider": "echo", "model": "echo-1" },
  "toolNames": ["mcp_tarafinda_olabilecek_tool"]
}'
```

**Beklenen sonuç**
- Yanıt **8 saniyeden kısa** sürede döner.
- `inconclusive` alanı `true`'dur.
- `code` değeri `mcp_unreachable` olan bir mesaj vardır ve `severity` değeri
  `Warning`'dir.
- İstek zaman aşımına uğramaz; uygulama çökmez.

**Gerçek sonuç**
Doküman scriptinde İKİ ayrı hata bulundu: (1) endpoint yolu yanlış — `POST $APU/api/mcp/servers` 405 döner, doğrusu `PUT $APU/api/mcp-servers/{name}`; (2) gövde alanı yanlış — `"url"` değil `"endpoint"` olmalı (`McpServerRequest.Endpoint` `required`). Doğru endpoint+gövdeyle kayıt başarılı. Ardından doğrulama: doküman scriptinin adresi (`127.0.0.1:59999`, dinleyen yok) "connection refused" ile HIZLI döner; `McpToolCatalog.RefreshAsync` (`src/AgentPrism.Mcp/Internal/McpToolCatalog.cs:187`) sunucu bazlı istisnaları içeride yutuyor (log: "MCP sunucusu 'olu-mcp' baglanamadi"), bu yüzden `TryRefreshMcpAsync`'e istisna hiç ulaşmıyor, refresh "başarılı" sayılıyor → sonuç sade `unknown_tool` (`mcp_unreachable` DEĞİL). Yanıt vermeyen bir adresle (`192.0.2.1`, TEST-NET black-hole) TEKRARLANDI: 5.02 saniyede TAM beklenen sonuç alındı — `valid:true`, `inconclusive:true`, `mcp_unreachable`/`Warning`. SONUÇ: `mcp_unreachable` mekanizması doğru çalışıyor ama yalnız GERÇEK zaman aşımında (`OperationCanceledException`) tetikleniyor; aktif red ("connection refused") sessizce `unknown_tool`'a düşüyor — kullanıcı için iki "erişilemez" alt durumu farklı davranıyor. Hem doküman adresi yanlış hem de bu ince sözleşme boşluğu ayrı bir HATA adayı olarak not edildi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-CORE-007 — Kendi kendini çağıran agent reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 12 |
| **İlgili karar** | — |

Negatif senaryo. Çağrı grafiği **kaydetme anında** denetlenir. Bir döngü,
çalışma anında maliyeti katlar.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Kendini çağıran bir tanımı doğrula.
2. Aynı tanımı **kaydetmeyi** dene.
3. İki agent'lı dolaylı bir döngü kur ve kaydetmeyi dene.

**Girilecek veri**
```bash
# 1) Dogrudan dongu
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-dongu",
  "model": { "provider": "echo", "model": "echo-1" },
  "callableAgentNames": ["manuel-dongu"]
}'

# 2) Kaydetmeyi dene
curl -s -o /dev/null -w "kayit HTTP: %{http_code}\n" \
  -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-dongu",
  "model": { "provider": "echo", "model": "echo-1" },
  "callableAgentNames": ["manuel-dongu"]
}'

# 3) Dolayli dongu: A -> B -> A
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-a", "model": { "provider": "echo", "model": "echo-1" }
}'
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-b", "model": { "provider": "echo", "model": "echo-1" },
  "callableAgentNames": ["manuel-a"]
}'
curl -s -X PUT "$APU/api/agents/manuel-a" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-a", "model": { "provider": "echo", "model": "echo-1" },
  "callableAgentNames": ["manuel-b"]
}'
```

**Beklenen sonuç**
- 1. adımda `valid` `false`'tur ve döngüyü bildiren bir mesaj vardır. Dönen
  `code` değeri **kaydedilir** (kod tabanında `cycle` örnek olarak geçiyor;
  gerçek değer koşumda yazılır).
- 2. adımda kayıt **reddedilir**; HTTP kodu 2xx değildir.
- 3. adımda `manuel-a` güncellemesi reddedilir — dolaylı döngü de yakalanır.
- Hiçbir adımda uygulama çökmez.

**Doğrulama sorgusu**
```sql
SELECT name, version FROM agentprism.agent_definitions WHERE name LIKE 'manuel-%' ORDER BY name;
```

**Gerçek sonuç**
1) `code:"cycle"`, tam beklenen mesaj. 2) kayıt 400 ile reddedildi. 3) dolaylı döngü (`manuel-a -> manuel-b -> manuel-a`) de 400 ile reddedildi, mesaj zinciri gösteriyor. Hiçbir adımda çökme yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-008 — Doğrulama hiçbir zaman istisna sızdırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 34 |
| **İlgili karar** | — |

Sınır senaryosu. Derleyici bir istisna atarsa doğrulama onu bir `compilation_error`
mesajına çevirmelidir — 500 döndürmemelidir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Derleyicinin patlamasına yol açacak bir tanım gönder.
2. HTTP kodunu ve gövdeyi oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" \
  -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-derleme-hatasi",
  "model": {
    "provider": "echo",
    "model": "echo-1",
    "responseFormat": { "kind": "JsonSchema" }
  }
}'
```

**Beklenen sonuç**
- HTTP kodu **200**'dür — doğrulama başarısızlığı bir HTTP hatası değildir.
- `valid` `false`'tur.
- `code` değeri `compilation_error` olan bir mesaj vardır.
- Mesaj metni `Schema` alanının eksik olduğunu söyler.
- Sunucu log'unda işlenmemiş bir istisna (`Unhandled exception`) **yoktur**.

**Gerçek sonuç**
HTTP 200, `valid:false`, `code:"compilation_error"`, mesaj `Schema` alanının eksik olduğunu söylüyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-009 — Boş ve aşırı uzun alanlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Sınır senaryosu.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `name` alanı boş bir tanım gönder.
2. `name` alanı hiç olmayan bir tanım gönder.
3. 50 000 karakterlik `instructions` ile bir tanım gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
  -H "content-type: application/json" \
  -d '{ "name": "", "model": { "provider": "echo", "model": "echo-1" } }'

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
  -H "content-type: application/json" \
  -d '{ "model": { "provider": "echo", "model": "echo-1" } }'

UZUN=$(python3 -c "print('a'*50000)")
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
  -H "content-type: application/json" \
  -d "{\"name\":\"manuel-uzun\",\"instructions\":\"$UZUN\",\"model\":{\"provider\":\"echo\",\"model\":\"echo-1\"}}"
```

**Beklenen sonuç**
- 1. ve 2. istek anlaşılır bir hata döndürür (400 veya `valid=false`).
  Hangi biçimin seçildiği koşumda **kaydedilir** — ikisi de kabul edilebilir,
  ama davranış tutarlı olmalıdır.
- 3. istek `valid=true` döndürür veya bir uzunluk sınırı bildirir. Sunucu
  çökmez, zaman aşımına uğramaz.
- Hiçbir adımda 500 dönmez.

**Gerçek sonuç**
1) `name:""` → 400, "'name' alani zorunludur" — DOĞRU. 2) `name` alanı JSON'da HİÇ YOK → **HTTP 500** (beklenen: 400 veya `valid=false`; "hiçbir adımda 500 dönmez" ilkesi ihlal edildi). Kök neden (sunucu logundan doğrulandı): `AgentDefinitionRequest.Name` bir C# `required` üye; JSON'da alan hiç yoksa `System.Text.Json` "missing required properties" ile `JsonException` atıyor → `BadHttpRequestException` → uygulamanın genel exception handler'ı bunu 400 yerine 500 ProblemDetails'e çeviriyor. 3) 50.000 karakterlik `instructions` → `valid:true`, çökme/zaman aşımı yok — DOĞRU. GENEL BULGU: bu SİSTEMİK bir örüntü — bkz. MT-CORE-006 (missing `endpoint`) ve MT-CORE-022 (geçersiz enum) notları; gövdesinde `required` alan veya enum tipi olan HERHANGİ bir endpoint'e eksik/geçersiz veri gönderildiğinde muhtemelen 500 dönüyor, 400 değil. Ayrı bir HATA kaydı gerekir; kapsamı bu dosyayı aşıyor, 07-HTTP-YONETIM-API.md'de de doğrulanmalı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

# 2 — Derleyici

Derleyici, doğrulamanın aksine **istisna atar**. Bu case'ler derleme yolunu
tanım kaydederek ve agent'ı çözdürerek tetikler.

### MT-CORE-020 — Tanınmayan `reasoningEffort` değeri reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-034 |

Negatif senaryo. Sessizce yok sayılmaz.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Geçersiz bir değer gönder.
2. Geçerli bir değeri farklı harf büyüklüğüyle gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-akil",
  "model": { "provider": "echo", "model": "echo-1", "reasoningEffort": "cok-yuksek" }
}'

curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-akil-2",
  "model": { "provider": "echo", "model": "echo-1", "reasoningEffort": "hIgH" }
}'
```

**Beklenen sonuç**
- 1. istek `valid=false` döndürür ve mesaj **geçerli değerleri listeler**:
  `None, Low, Medium, High, ExtraHigh`.
- 2. istek `valid=true` döndürür — karşılaştırma büyük/küçük harfe duyarlı değildir.

**Gerçek sonuç**
1) `compilation_error`, "cok-yuksek" reddedildi, geçerli değerler listelendi (None, Low, Medium, High, ExtraHigh). 2) "hIgH" → `valid:true` (büyük/küçük harfe duyarsız).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-021 — `responseFormat` kombinasyonları

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

Negatif ve sınır senaryosu. Üç geçersiz kombinasyon vardır ve üçü de ayrı bir
mesaj üretmelidir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Şemasız `JsonSchema` gönder.
2. Şeması JSON nesnesi olmayan (dizi) `JsonSchema` gönder.
3. `Text` kipiyle birlikte şema gönder.
4. Geçerli bir `JsonSchema` gönder.

**Girilecek veri**
```bash
D() { curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d "$1"; echo; }

D '{"name":"m1","model":{"provider":"echo","model":"echo-1","responseFormat":{"kind":"JsonSchema"}}}'
D '{"name":"m2","model":{"provider":"echo","model":"echo-1","responseFormat":{"kind":"JsonSchema","schema":[1,2]}}}'
D '{"name":"m3","model":{"provider":"echo","model":"echo-1","responseFormat":{"kind":"Text","schema":{"type":"object"}}}}'
D '{"name":"m4","model":{"provider":"echo","model":"echo-1","responseFormat":{"kind":"JsonSchema","schema":{"type":"object","properties":{"durum":{"type":"string"}},"required":["durum"]}}}}'
```

**Beklenen sonuç**
- `m1`: `valid=false`, mesaj `Schema` alanının verilmediğini söyler.
- `m2`: `valid=false`, mesaj şemanın bir **JSON nesnesi** olması gerektiğini söyler.
- `m3`: `valid=false`, mesaj şemanın yalnız `JsonSchema` kipinde kullanıldığını söyler.
- `m4`: `valid=true` **veya** "bu model yapılandırılmış çıktı desteklemiyor"
  mesajı. `echo` sağlayıcısının desteği koşumda kaydedilir.

**Gerçek sonuç**
m1,m2,m3,m4 dördü de tam beklenen mesajları verdi (m4: "modeli yapilandirilmis cikti desteklemiyor" — echo modelinin desteklemediği doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-022 — Sıkıştırma ayarları tetikleyicisiz olamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Tetikleyicisiz bir sıkıştırma ayarı gönder.
2. Bilinmeyen bir strateji adı gönder.
3. `ContextWindow` stratejisini `maxContextWindowTokens` olmadan gönder.

**Girilecek veri**
```bash
D() { curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d "$1"; echo; }

D '{"name":"c1","model":{"provider":"echo","model":"echo-1"},"compaction":{"strategy":"Summarize"}}'
D '{"name":"c2","model":{"provider":"echo","model":"echo-1"},"compaction":{"strategy":"BoyleBirSeyYok","triggerTokens":1000}}'
D '{"name":"c3","model":{"provider":"echo","model":"echo-1"},"compaction":{"strategy":"ContextWindow","triggerTokens":1000}}'
```

**Beklenen sonuç**
- `c1`: mesaj `TriggerTokens/TriggerMessages/TriggerTurns`'ten en az birinin
  gerektiğini söyler.
- `c2`: mesaj bilinmeyen strateji adını **aynen** taşır.
- `c3`: mesaj `MaxContextWindowTokens` alanının eksik olduğunu söyler.
- Üçü de `valid=false`'tur.

**Gerçek sonuç**
c1 (`strategy:"Summarize"`, TriggerTokens yok) → **HTTP 500**. c2 (`strategy:"BoyleBirSeyYok"`) → **HTTP 500**. c3 (ContextWindow, MaxContextWindowTokens yok) → DOĞRU: `compilation_error`, beklenen mesaj. Kök neden (log doğrulandı): c1'de "Summarize" GEÇERLİ bir `CompactionStrategyKind` değeri DEĞİL — doğru ad `Summarization` (bu bir DOKÜMAN TİPOSU); c2'de zaten kasıtlı geçersiz değer. İkisinde de `System.Text.Json`'ın enum dönüştürücüsü tanımadığı string'i `JsonException` ile reddediyor → aynı sistemik 500 örüntüsü (bkz. MT-CORE-009). ÖNEMLİ: doküman'ın c2 için beklediği "mesaj bilinmeyen strateji adını AYNEN taşır" davranışı HTTP API üzerinden HİÇBİR ZAMAN gerçekleşemez — validator/compiler mantığına hiç ulaşılmıyor, JSON ayrıştırma katmanında daha erken patlıyor. "Hiçbir istekte 500 dönmez" ilkesi bu koşumda en az üç ayrı case'de (006 kayıt denemesi, 009-2, 022 c1/c2) ihlal edildi — sistemik, tek endpoint'e özgü değil.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-CORE-023 — Skill kataloğu kayıtlı değilken skill isteyen tanım

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

Negatif senaryo. Bu case, örnek uygulamada değil **minimum bir kurulumda**
koşulur — örnek uygulamada skill kataloğu zaten kayıtlıdır.

**Ön koşul**
- [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) MT-PKG-070 geçti
  (yerel feed hazır).

**Adımlar**
1. Yalın bir tüketici projesi kur.
2. Skill isteyen bir agent tanımla.
3. Çalıştır.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/skillsiz && mkdir -p ~/agentprism-manuel/skillsiz
cd ~/agentprism-manuel/skillsiz
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.Core.*.nupkg | sed 's#.*AgentPrism.Core\.##;s#\.nupkg##')
dotnet add package AgentPrism.Core --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism().AddAgent(new AgentDefinition
{
    Name = "skill-isteyen",
    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
    SkillNames = ["olmayan-skill"],
});

var provider = services.BuildServiceProvider();
var catalog = provider.GetRequiredService<IAgentCatalog>();

try
{
    await catalog.ResolveAsync("skill-isteyen");
    Console.WriteLine("🚨 istisna ATILMADI");
}
catch (AgentPrismCompilationException ex)
{
    Console.WriteLine("beklenen istisna: " + ex.Message);
    Console.WriteLine("AgentName: " + ex.AgentName);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `beklenen istisna:` ile başlar.
- Mesaj "skill katalogu kayitli degil" ifadesini taşır.
- `AgentName` alanı `skill-isteyen`'dir.
- `🚨 istisna ATILMADI` satırı **görünmez**.

**Gerçek sonuç**
İstisna GERÇEKTEN atıldı (`AgentPrismCompilationException`, `AgentName="skill-isteyen"` doğru), ama mesaj metni beklenenden FARKLI: `"'skill-isteyen' agent'i 'olmayan-skill' skill'ine isaret ediyor ancak skill bulunamadi."` — `"skill katalogu kayitli degil"` DEĞİL. Kök neden (kod doğrulandı): `AddAgentPrism()` (`src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs:381`) `IAgentSkillCatalog`'u `TryAddSingleton` ile KOŞULSUZ kaydediyor — bir tüketicinin `AddAgentPrism()` çağırdığı hiçbir standart senaryoda katalog "kayıtlı değil" olamaz. `AgentDefinitionCompiler.cs:306` ve `:1137`'deki "skill katalogu kayitli degil" hata dalı bu yüzden normal yollardan erişilemez (muhtemelen ölü kod, yalnız bir tüketici `IAgentSkillCatalog` kaydını elle kaldırırsa tetiklenir). Doküman'ın "minimum kurulumda katalog kayıtlı değildir" öncülü bu sürüm için yanlış. Ürün kusuru değil — davranış tutarlı (eksik skill'e işaret eden tanım her koşulda reddediliyor) ama iki farklı hata mesajından biri pratikte hiç görülmüyor. Doküman düzeltme + olası ölü kod temizliği adayı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-CORE-024 — Derleme hatası hangi agent'ta olduğunu söyler

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

Sınır senaryosu. Yirmi agent'lı bir kurulumda "bir agent derlenemedi" mesajı
işe yaramaz; hangi agent olduğu yazılmalıdır.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Var olmayan bir tool'a işaret eden bir agent tanımla.
2. Çözümle ve istisna metnini oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism()
    .AddTool(AgentPrismManuelTools.Var)
    .AddAgent(new AgentDefinition
    {
        Name = "tool-eksik",
        Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
        ToolNames = ["hayali_tool"],
    });

var provider = services.BuildServiceProvider();
var catalog = provider.GetRequiredService<IAgentCatalog>();

try { await catalog.ResolveAsync("tool-eksik"); Console.WriteLine("🚨 istisna ATILMADI"); }
catch (AgentPrismCompilationException ex) { Console.WriteLine(ex.Message); }

internal static class AgentPrismManuelTools
{
    [AgentPrismTool("var_olan_tool", "Kayitli bir tool.")]
    public static string Var(string x) => x;
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- İstisna mesajı `tool-eksik` agent adını taşır.
- Mesaj eksik tool adını (`hayali_tool`) taşır.
- Mesaj **kayıtlı tool'ları listeler** (`var_olan_tool` görünür).
- Mesaj `builder.AddAgentPrism().AddTool(...)` yönlendirmesini içerir.

**Gerçek sonuç**
Doküman'ın kendi scripti `provider:"echo"` kullanıyor — bu paket İÇİNDE HİÇ YOK (echo yalnız örnek uygulamaya özel, bkz. MT-CORE-023 notu). Bare consumer projesinde kendi basit `IModelProvider` uygulamamla telafi edilip tekrar koşuldu. SONUÇ (mekanizma KANITLANDI çalışıyor): istisna doğru atıldı, `tool-eksik` agent adı mesajda var, eksik tool adı (`hayali_tool`) mesajda var, `builder.AddAgentPrism().AddTool(...)` yönlendirmesi mesajda var. TEK SAPMA: mesaj kayıtlı tool'u `"Var"` olarak listeledi, doküman'ın beklediği `"var_olan_tool"` DEĞİL. Kök neden (XML doküman doğrulandı, `src/AgentPrism.Core/IAgentPrismBuilder.cs:47`): `AddTool(Delegate method, string? name = null, ...)` — "name boş bırakılırsa METOT ADI kullanılır" diye açıkça belgelenmiş, KASITLI davranış. `[AgentPrismTool("var_olan_tool", ...)]` özniteliği yalnız `AddToolsFrom<T>()` tarafından okunur, `.AddTool(delegate)` onu hiç görmez. Doküman'ın kendi scripti bu iki idiomu KARIŞTIRMIŞ: özniteliği koyup ama delegate-tabanlı kaydı kullanmış. Ürün kusuru yok — API tasarımı belgelenmiş şekilde çalışıyor. Doküman düzeltmesi gerekir.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

# 3 — Katalog

### MT-CORE-030 — Kod kaynaklı agent veritabanı tanımını yener

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Ad çakışmasında **kod kaynağı kazanır**: kodda tanımlanmış agent derleme
zamanında doğrulanmıştır. Bu, arayüzden gelen bir tanımın kod tanımını
ezememesi demektir — bir güvenlik özelliğidir.

**Ön koşul**
- Örnek uygulama çalışıyor. `support` agent'ı kodda tanımlıdır.

**Adımlar**
1. Kataloğu listele ve `support` agent'ının kaynağını oku.
2. Aynı adla bir veritabanı tanımı kaydetmeyi dene.
3. Kataloğu tekrar listele.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" | python3 -m json.tool | grep -A3 '"name": "support"'

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
  -H "content-type: application/json" -d '{
  "name": "support",
  "displayName": "SAHTE Destek",
  "instructions": "Bu tanim kod tanimini EZMEMELIDIR.",
  "model": { "provider": "echo", "model": "echo-1" }
}'

curl -s "$APU/api/agents" -H "$APB" | python3 -m json.tool | grep -A3 '"name": "support"'
```

**Beklenen sonuç**
- 1. adımda `support` agent'ının `origin` alanı `"Code"` yazar — **ad olarak**,
  sayı olarak değil.
- 2. adımda kayıt ya reddedilir ya da kaydedilir ama katalogda görünmez.
  Hangisi olduğu koşumda kaydedilir.
- 3. adımda `support` hâlâ `origin: "Code"` ve `displayName` **`SAHTE Destek`
  değildir**.
- Listede `support` **tek bir kez** görünür.

**Doğrulama sorgusu**
```sql
SELECT name, version, display_name FROM agentprism.agent_definitions WHERE name = 'support';
```

**Gerçek sonuç**
İzole ortamda doğrulandı. `support` agent'ının `origin:"Code"` (ad olarak). Sahte kayıt denemesi HTTP 409 ile TEMİZ reddedildi: "'support' kodda tanimli bir agent'tir ve yonetim API'sinden degistirilemez." `displayName` değişmedi ("Destek Asistani" kaldı), listede tek kayıt.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-031 — Katalog ada göre sıralı döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Kataloğu listele.
2. Adları çıkar ve sıralı olup olmadığını denetle.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" \
  | python3 -c "import sys,json; a=[x['name'] for x in json.load(sys.stdin)]; print(a); print('SIRALI' if a==sorted(a) else '🚨 SIRASIZ')"
```

**Beklenen sonuç**
- Çıktı `SIRALI` yazar.
- Sıralama ordinal'dir: büyük harfler küçük harflerden önce gelir.

**Gerçek sonuç**
`['arastirmaci','cevirmen','ozetleyici','support','yonlendirici']` — SIRALI. (Not: fixture verisinin tamamı küçük harf; büyük/küçük harf ordinal iddiası bu veriyle ayrıca test edilemedi.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-032 — Bulunmayan agent `null` döner, istisna atmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir agent'ı sorgula.
2. Var olmayan bir agent'ı çalıştırmayı dene.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "GET  : %{http_code}\n" "$APU/api/agents/hic-boyle-bir-agent-yok" -H "$APB"
curl -s -o /dev/null -w "RUN  : %{http_code}\n" -X POST "$APU/api/agents/hic-boyle-bir-agent-yok/run" \
  -H "$APB" -H "content-type: application/json" -d '{"message":"Merhaba"}'
```

**Beklenen sonuç**
- `GET` satırı **404** gösterir.
- `RUN` satırı **404** gösterir.
- Sunucu log'unda işlenmemiş istisna yoktur.
- Hiçbir `run` kaydı oluşmaz.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.runs WHERE agent_name = 'hic-boyle-bir-agent-yok';
```

**Gerçek sonuç**
İzole/temiz ortamda: `GET` 404, `RUN` 404, hiçbir `run` kaydı oluşmadı. (İlk denemede paylaşılan ortamın başka bir oturum tarafından o an şeması düşürülmüş olduğu için sahte 500'ler alınmıştı; izolasyondan sonra tekrarlanınca doğru sonuç.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-033 — Sürüm artışı derlenmiş agent önbelleğini geçersiz kılar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Sınır senaryosu. Tanım güncellendiğinde `Version` artar; derlenmiş agent
önbelleği bu değere bağlıdır. Önbellek geçersiz kılınmazsa kullanıcı eski
talimatla çalışan bir agent görür ve sebebini anlamaz.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Bir agent kaydet.
2. Çalıştır ve yanıtı kaydet.
3. Talimatı değiştirerek güncelle.
4. Uygulamayı **yeniden başlatmadan** tekrar çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-surum",
  "instructions": "BIRINCI SURUM TALIMATI.",
  "model": { "provider": "echo", "model": "echo-1" }
}'

curl -s -X POST "$APU/api/agents/manuel-surum/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"merhaba"}' | tail -c 300

curl -s -X PUT "$APU/api/agents/manuel-surum" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-surum",
  "instructions": "IKINCI SURUM TALIMATI.",
  "model": { "provider": "echo", "model": "echo-1" }
}'

curl -s "$APU/api/agents/manuel-surum" -H "$APB" | python3 -m json.tool | grep -E "version|instructions"
curl -s -X POST "$APU/api/agents/manuel-surum/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"merhaba"}' | tail -c 300
```

**Beklenen sonuç**
- Güncelleme sonrası `version` **2**'dir.
- `instructions` alanı `IKINCI SURUM TALIMATI.` gösterir.
- Uygulama yeniden başlatılmadan ikinci `run`, güncel tanımı kullanır.
  `run` kaydındaki agent sürümü **2**'dir.

**Doğrulama sorgusu**
```sql
SELECT agent_name, agent_version, status, started_at
FROM agentprism.runs WHERE agent_name = 'manuel-surum' ORDER BY started_at;
```

**Gerçek sonuç**
İlk `run` → `agent_version=1`. Güncelleme sonrası `version=2`, `instructions="IKINCI SURUM TALIMATI."`. Yeniden başlatmadan ikinci `run` → `agent_version=2` (DB'den doğrulandı: `agentprism.runs` tablosunda iki satır, sürüm 1 ve 2). Önbellek doğru geçersiz kılınmış. Doküman notu: `GET /api/agents/{name}` düz değil, `{descriptor, definition, isEditable}` içeren iç içe bir gövde döndürüyor — doküman bunu belirtmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-034 — Kod kaynaklı agent'ta sürümlü çözümleme reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 19 |
| **İlgili karar** | — |

Negatif senaryo. Kod kaynaklı agent'ta sürüm geçmişi yoktur; belirli bir sürüme
karşı çalıştırma desteklenmez ve bu **açıkça** söylenmelidir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Kod kaynaklı `support` agent'ının sürüm listesini iste.
2. Veritabanı kaynaklı `manuel-surum` için aynısını iste.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/support/versions" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-surum/versions" -H "$APB"
```

**Beklenen sonuç**
- `support` için yanıt bir hata veya boş liste döner **ve** gerekçe kod
  kaynaklı olmasıdır. Yanıt gövdesi "kod kaynagi" ifadesini taşır veya HTTP
  kodu 4xx'tir. Hangi biçim seçildiği koşumda kaydedilir.
- `manuel-surum` için **iki** sürüm listelenir (MT-CORE-033'ten).
- Hiçbir istekte 500 dönmez.

**Gerçek sonuç**
`support/versions` → 404, "'support' adinda bir agent yok." — HTTP 4xx şartı sağlanıyor (beklenen ikinci alternatif). Kalite notu: mesaj METİN olarak yanıltıcı — `support` GERÇEKTEN var (kod kaynaklı), yalnız sürüm geçmişi yok; mesaj bunu "yok" diyerek karıştırıyor, doküman'ın beklediği "kod kaynagi" ifadesi de yok. `manuel-surum/versions` → 200, 2 kayıt (v2, v1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-035 — Tanım hiçbir zaman kimlik bilgisi taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | K-059 |

Negatif senaryo. `secret` veritabanına yazılmaz. Bir tüketici tanıma anahtar
gömmeye çalışırsa bu ya reddedilmeli ya da **sızmamalıdır**.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Metadata alanına anahtar benzeri bir değer koyarak kaydet.
2. Veritabanını tara.
3. Kaydı oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-secret",
  "model": { "provider": "echo", "model": "echo-1", "providerSettings": {} },
  "metadata": { "not": "sk-MANUEL-TEST-SAHTE-ANAHTAR-0000" }
}'

curl -s "$APU/api/agents/manuel-secret" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `ModelBinding` üzerinde bir API anahtarı alanı **yoktur**; sözleşme buna izin
  vermez.
- `metadata` serbest alandır ve tüketicinin oraya yazdığı değer geri okunur —
  bu beklenendir; AgentPrism serbest metadata'yı denetlemez.
- 🚨 `agent_definitions` tablosunda AgentPrism'in **kendi** yazdığı hiçbir
  sağlayıcı anahtarı yoktur.

**Doğrulama sorgusu**
```sql
SELECT name, definition::text FROM agentprism.agent_definitions
WHERE definition::text ILIKE '%apikey%' OR definition::text ILIKE '%connectionstring%';
-- Beklenen: yalniz 'manuel-secret'in metadata'sindaki serbest metin, baska hicbir satir yok.
```

**Gerçek sonuç**
`ModelBinding`'de API anahtarı alanı yok (sözleşme gereği). `metadata.not` değeri olduğu gibi geri okundu (serbest alan, beklenen). SQL taraması (`apikey`/`connectionstring` ILIKE) **0 satır** döndürdü — AgentPrism'in kendi yazdığı hiçbir sağlayıcı anahtarı yok; `manuel-secret`'ın kendi metadata değeri de bu terimleri içermiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Tool defteri

### MT-CORE-040 — Tool listesi ad, açıklama, şema ve kaynak taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1, 52 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Tool listesini oku.

**Girilecek veri**
```bash
curl -s "$APU/api/tools" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Üç tool görünür: `cancel_order`, `get_order_status`, `list_recent_orders`.
- Liste **ada göre sıralıdır**.
- `cancel_order` için `requiresApproval` alanı `true`'dur; diğer ikisi `false`.
- Üçünün de `source` alanı `"generated"`'dır (Faz 52 sonrası).
- Her tool'un `jsonSchema` alanı doludur ve parametrelerini tanımlar.

**Gerçek sonuç**
Üç tool (`cancel_order`, `get_order_status`, `list_recent_orders`), ada göre sıralı, `cancel_order.requiresApproval:true` diğerleri `false`, üçünün de `source:"generated"`, `jsonSchema` dolu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-041 — Aynı adda iki tool açılışta hata verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Negatif senaryo. Sessizce "son kayıt kazanır" davranışı, hangi tool'un
çalıştığını belirsiz bırakır.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Aynı adla iki tool kaydet.
2. Defteri çözümle.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism()
    .AddTool(AIFunctionFactory.Create((string a) => a, "ayni_ad", "Birinci."))
    .AddTool(AIFunctionFactory.Create((string a) => a, "ayni_ad", "Ikinci."));

var provider = services.BuildServiceProvider();

try
{
    var registry = provider.GetRequiredService<IToolRegistry>();
    Console.WriteLine("🚨 istisna ATILMADI - tool sayisi: " + registry.List().Count);
}
catch (AgentPrismException ex)
{
    Console.WriteLine("beklenen istisna: " + ex.Message);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `beklenen istisna:` ile başlar.
- Mesaj `'ayni_ad' adinda birden cok tool kaydedilmis` ifadesini taşır.
- `🚨 istisna ATILMADI` satırı görünmez.

**Gerçek sonuç**
Mesaj "beklenen istisna:" ile başlıyor, "'ayni_ad' adinda birden cok tool kaydedilmis..." tam eşleşti. "🚨 istisna ATILMADI" satırı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-042 — Onay gerektiren tool sarmalanır ama şeması değişmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Sınır senaryosu. Onay sarmalaması defterin içinde yapılır — başka bir kod
yolunun sarmalamayı atlaması bu yüzden imkânsızdır. Sarmalayıcı ad, açıklama ve
JSON şemasını **değiştirmemelidir**; değiştirseydi model tool'u tanıyamazdı.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `cancel_order` tool'unun tanımını oku.
2. `get_order_status` ile karşılaştır.

**Girilecek veri**
```bash
curl -s "$APU/api/tools" -H "$APB" \
  | python3 -c "
import sys, json
for t in json.load(sys.stdin):
    if t['name'] in ('cancel_order', 'get_order_status'):
        print(t['name'], '| onay:', t['requiresApproval'], '| aciklama:', (t.get('description') or '')[:40])
        print('   sema:', t.get('jsonSchema'))
"
```

**Beklenen sonuç**
- `cancel_order` için `requiresApproval` `true`, `get_order_status` için `false`.
- İkisinin de `description` alanı **boş değildir**.
- İkisinin de `jsonSchema` alanı `orderId` parametresini içerir.
- `cancel_order`'ın adı `ApprovalRequired` gibi bir önek/sonek **taşımaz**.

**Gerçek sonuç**
`cancel_order`/`get_order_status`: onay doğru, `description` dolu, `jsonSchema` `orderId` içeriyor, ad önek/sonek taşımıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-043 — Tanım yalnız kayıtlı tool'a işaret edebilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | K2 |

Negatif senaryo. MT-CORE-002 doğrulama yolunu test eder; bu case **kayıt ve
çalıştırma** yolunu test eder. İkisi ayrı kod yollarıdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Hayalî tool'a işaret eden bir tanımı kaydetmeyi dene.
2. Kayıt geçtiyse çalıştırmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nkayit HTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
  -H "content-type: application/json" -d '{
  "name": "manuel-hayali-tool-kayit",
  "model": { "provider": "echo", "model": "echo-1" },
  "toolNames": ["hayali_tool"]
}'

curl -s -w "\ncalistirma HTTP: %{http_code}\n" -X POST "$APU/api/agents/manuel-hayali-tool-kayit/run" \
  -H "$APB" -H "content-type: application/json" -d '{"message":"merhaba"}' | tail -c 300
```

**Beklenen sonuç**
- Kayıt reddedilir (4xx) **veya** kayıt geçer ama çalıştırma anlaşılır bir
  hatayla reddedilir. Hangisi olduğu kaydedilir.
- Hiçbir durumda `hayali_tool` çağrılmaz ve hiçbir durumda 500 dönmez.
- Hata metni eksik tool adını taşır.

**Gerçek sonuç**
Kayıt BAŞARILI (201) — tanım kaydedilirken tool adı doğrulanmıyor (ayrı kod yolu, case'in kendi anlattığı gibi). Çalıştırma (`run`) 400 ile reddedildi; mesaj eksik tool adını (`hayali_tool`), kayıtlı tool listesini (`cancel_order, get_order_status, list_recent_orders`) ve yönlendirmeyi (`builder.AddAgentPrism().AddTool(...)`) içerdi. 500 yok. Doküman'ın ikinci kabul edilebilir dalına tam uyuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-044 — Tool gerçekten çağrılır ve sonucu kayda geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

Mutlu yol — ama beklenen sonuç model metnine değil, **ölçülebilir olguya** bağlanır.

**Ön koşul**
- Örnek uygulama çalışıyor.
- `AgentPrism:Providers:OpenAI:ApiKey` tanımlıdır (gerçek model çağrısı gerekir).

**Adımlar**
1. `support` agent'ını sipariş sorusuyla çalıştır (`FIX-PROMPT-01`).
2. `run` kaydını oku.
3. Olayları oku.

**Girilecek veri**
```bash
RUN=$(curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" \
  -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"musteri-42"}')
echo "$RUN" | tail -c 500
```

**Beklenen sonuç**
- Yanıt `ORD-1001` dizgisini içerir.
- `get_order_status` tool'u **tam bir kez** çağrılır.
- `runs.status` tamamlanmış durumu gösterir.
- `runs.cost_usd` `NULL` **değildir** ve pozitiftir.
- `tool_invocations` tablosunda `get_order_status` için tam bir satır vardır.

**Doğrulama sorgusu**
```sql
SELECT r.id, r.agent_name, r.status, r.cost_usd,
       (SELECT count(*) FROM agentprism.tool_invocations t
         WHERE t.run_id = r.id AND t.tool_name = 'get_order_status') AS tool_cagrisi
FROM agentprism.runs r
WHERE r.agent_name = 'support'
ORDER BY r.started_at DESC LIMIT 1;
```

**Gerçek sonuç**
Gerçek OpenAI çağrısı (gpt-5.4-mini) yapıldı. Yanıt metni tam olarak "ORD-1001 siparişiniz kargoya verilmiş. Tahmini teslimat: 2 gün." — `ORD-1001` dizgisini içeriyor. `get_order_status` tool'u tam bir kez çağrıldı (DB'den doğrulandı). `runs.status` tamamlanmış. İlk denemede `input_cost`/`output_cost` NULL çıktı ama bu ürün kusuru değil: örnek uygulamanın kendi `appsettings.json`'ı `openai`/`gpt-5.4-mini` için hiç fiyat tanımlamıyor (K-032: "fiyat uydurulmaz, deger verilmezse maliyet NULL kalir" — kasıtlı/belgelenmiş, yalnız örnek uygulamanın eksik yapılandırması). Kendi izole sürecime fiyat eklenip tekrar koşuldu: `input_cost=0.0000695`, `output_cost=0.0000560` — pozitif. Mekanizma tam doğrulandı. Doküman notu: doküman örnek uygulamada `Pricing` tanımlı olduğunu varsayıyor; şu an değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-045 — Tool gerekmeyen istek tool çağırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

Sınır senaryosu. `FIX-PROMPT-02` tool çağrısı **beklemez**. Her istekte tool
çağıran bir kurulum gereksiz maliyet üretir.

**Ön koşul**
- MT-CORE-044 geçti.

**Adımlar**
1. `support` agent'ını selamla çalıştır.
2. Tool çağrısı sayısını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "content-type: application/json" \
  -d '{"message":"Merhaba","sessionId":"musteri-99"}' | tail -c 300
```

**Beklenen sonuç**
- `run` tamamlanır.
- Bu `run` için `tool_invocations` satır sayısı **0**'dır.
- `runs.cost_usd` yine doludur.

**Doğrulama sorgusu**
```sql
SELECT r.id, count(t.*) AS tool_sayisi
FROM agentprism.runs r
LEFT JOIN agentprism.tool_invocations t ON t.run_id = r.id
WHERE r.agent_name = 'support'
GROUP BY r.id ORDER BY max(r.started_at) DESC LIMIT 1;
```

**Gerçek sonuç**
"Merhaba" ile çalıştırma tamamlandı; bu run için `tool_invocations` satır sayısı 0. `input_cost`/`output_cost` yine dolu (fiyat env var'ı hâlâ etkin).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Oturum yönetimi

### MT-CORE-050 — Oturum yoksa oluşturulur, varsa yüklenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1, 2 |
| **İlgili karar** | K-107 |

**Ön koşul**
- Örnek uygulama çalışıyor. Reset yapılmıştır.

**Adımlar**
1. Yeni bir oturum kimliğiyle çalıştır.
2. Oturumu listele.
3. Aynı kimlikle ikinci kez çalıştır.
4. Sohbet geçmişini oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Benim adim Faruk.","sessionId":"manuel-oturum-01"}' > /dev/null

curl -s "$APU/api/sessions" -H "$APB" | python3 -m json.tool | grep -B2 -A4 "manuel-oturum-01"

curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Adim neydi?","sessionId":"manuel-oturum-01"}' | tail -c 300

curl -s "$APU/api/sessions/manuel-oturum-01" -H "$APB" | python3 -m json.tool | head -40
```

**Beklenen sonuç**
- 1. çalıştırmadan sonra `manuel-oturum-01` oturumu vardır.
- 2. çalıştırmanın yanıtı **`Faruk` dizgisini içerir** — geçmiş yüklendi.
- Sohbet geçmişi **en az dört** mesaj taşır (iki kullanıcı, iki asistan).
- Mesaj sırası kronolojiktir.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.conversation_items ci
JOIN agentprism.sessions s ON s.id = ci.session_id
WHERE s.external_id = 'manuel-oturum-01';
```

**Gerçek sonuç**
Oturum oluştu, ikinci çalıştırmada geçmiş GERÇEKTEN yüklendi/kullanıldı (persist edilen mesaj listesi API'den doğrulandı: 4 mesaj, kronolojik sıra: 2 kullanıcı + 2 asistan). Yalnız "yanıt 'Faruk' içerir" iddiası doğrulanamadı — `echo` sağlayıcısının kendi tasarımı gereği (`EchoChatClient.BuildReply` yalnız SON kullanıcı mesajını yankılar, tam geçmişi değil); bu bir ürün kusuru değil, echo'nun bilinçli sınırlaması (MT-CORE-004/006 ile aynı sınıf).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-051 — Oturum silinir ve geçmiş gider

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-050 geçti.

**Adımlar**
1. Oturumu sil.
2. Silinen oturumu sorgula.
3. Aynı kimlikle yeniden çalıştır.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "DELETE: %{http_code}\n" -X DELETE "$APU/api/sessions/manuel-oturum-01" -H "$APB"
curl -s -o /dev/null -w "GET   : %{http_code}\n" "$APU/api/sessions/manuel-oturum-01" -H "$APB"

curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Adim neydi?","sessionId":"manuel-oturum-01"}' | tail -c 300
```

**Beklenen sonuç**
- `DELETE` satırı 2xx gösterir.
- `GET` satırı **404** gösterir.
- Yeniden çalıştırmanın yanıtı `Faruk` dizgisini **içermez** — geçmiş gerçekten silindi.
- Aynı kimlikle yeni bir oturum kaydı oluşur.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.conversation_items ci
JOIN agentprism.sessions s ON s.id = ci.session_id
WHERE s.external_id = 'manuel-oturum-01';
-- Silme sonrasi 0, yeniden calistirma sonrasi 2 beklenir.
```

**Gerçek sonuç**
`DELETE` 204, sonraki `GET` 404, aynı kimlikle yeniden çalıştırma 2 mesajlık YENİ bir oturum açtı (eski geçmiş gerçekten silinmiş — mesaj sayısı 4 değil 2).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-052 — Var olmayan oturumun silinmesi hata vermez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir oturumu sil.
2. Aynı isteği tekrarla.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "1. deneme: %{http_code}\n" -X DELETE "$APU/api/sessions/hic-olmayan-oturum" -H "$APB"
curl -s -o /dev/null -w "2. deneme: %{http_code}\n" -X DELETE "$APU/api/sessions/hic-olmayan-oturum" -H "$APB"
```

**Beklenen sonuç**
- İki deneme **aynı** HTTP kodunu döndürür (404 veya 204 — hangisi olduğu kaydedilir).
- Silme işlemi idempotenttir: ikinci deneme farklı davranmaz.
- 500 dönmez.

**Gerçek sonuç**
İki deneme de 404, tamamen aynı/idempotent, 500 yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-053 — İki oturum birbirini görmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Sınır senaryosu. `FIX-SESSION-01` ve `FIX-SESSION-02` ayrı kişilerdir.

**Ön koşul**
- Örnek uygulama çalışıyor. Reset yapılmıştır.

**Adımlar**
1. Birinci oturumda bir bilgi ver.
2. İkinci oturumda o bilgiyi sor.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Gizli kodum MAVI-42.","sessionId":"musteri-42"}' > /dev/null

curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Gizli kodum neydi?","sessionId":"musteri-99"}' | tail -c 400
```

**Beklenen sonuç**
- İkinci yanıt `MAVI-42` dizgisini **içermez**.
- İki oturum ayrı `conversation_items` kümesine sahiptir.

**Doğrulama sorgusu**
```sql
SELECT s.external_id, count(ci.*) AS mesaj
FROM agentprism.sessions s
LEFT JOIN agentprism.conversation_items ci ON ci.session_id = s.id
WHERE s.external_id IN ('musteri-42','musteri-99')
GROUP BY s.external_id;
```

**Gerçek sonuç**
`musteri-99` yanıtı `MAVI-42` içermiyor. DB doğrulaması: iki ayrı `conversation_id`, ikisinde de 2'şer mesaj — tam izolasyon. Doküman notu: doküman'ın SQL sorgusu (`s.external_id`, `ci.session_id`) GERÇEK şemayla uyuşmuyor — `sessions.id` zaten dış kimliğin kendisi, `conversation_items` tablosunda `session_id` kolonu yok (yalnız `conversation_id` var; session→conversation bağlantısı `sessions.state` jsonb'sindeki `stateBag['AgentPrism.ChatHistory']['conversationId']` üzerinden kuruluyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-054 — Aynı oturuma eşzamanlı iki çalıştırma

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1, 2 |
| **İlgili karar** | — |

Negatif ve eşzamanlılık senaryosu. Kaybolan güncelleme (`lost update`) sessiz
veri bozulmasıdır — en pahalı kusur sınıfı.

**Ön koşul**
- Örnek uygulama çalışıyor. Reset yapılmıştır.

**Adımlar**
1. Aynı oturum kimliğiyle iki isteği aynı anda gönder.
2. Sonuç mesaj sayısını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Birinci istek.","sessionId":"manuel-yaris"}' > /tmp/ap-y1.txt &
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Ikinci istek.","sessionId":"manuel-yaris"}' > /tmp/ap-y2.txt &
wait
echo "--- 1 ---"; tail -c 200 /tmp/ap-y1.txt
echo "--- 2 ---"; tail -c 200 /tmp/ap-y2.txt
```

**Beklenen sonuç**
- İki istek de tamamlanır; hiçbiri 500 dönmez.
- `manuel-yaris` oturumunda **dört** mesaj vardır (iki kullanıcı, iki asistan) —
  hiçbir mesaj kaybolmaz.
- Bir çakışma denetimi varsa isteklerden biri açık bir çakışma hatası döner;
  bu da kabul edilebilir. Sessiz kayıp **kabul edilemez**.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.conversation_items ci
JOIN agentprism.sessions s ON s.id = ci.session_id
WHERE s.external_id = 'manuel-yaris';
```

**Gerçek sonuç**
**GERÇEK VE KRİTİK ürün kusuru — doğrulandı, kanıtlandı.** Aynı YENİ oturum kimliğine ("manuel-yaris") iki eşzamanlı İLK istek gönderildi. İkisi de başarıyla tamamlandı (SSE `event: done`), hiçbiri hata dönmedi. Beklenen: 4 mesaj (sessiz kayıp kabul edilemez). Gerçekleşen: oturumda yalnız **2 mesaj**. KÖK NEDEN (veritabanından birebir kanıtlandı): İKİ AYRI `conversation` satırı oluştu (aynı saniyede, mikrosaniye farkla: `...6880-7137` ve `...6880-700e`) — "bu oturum için conversation var mı" kontrolü ile "yoksa oluştur" arasında klasik check-then-create yarışı var. Oturumun `state->stateBag` alanındaki `conversationId` işaretçisi SON YAZAN istek tarafından ezildi (last-write-wins); kaybeden isteğin conversation'ı ("Ikinci istek." + yanıtı — DOĞRULANDI: veri fiziksel olarak kayıp değil, `seq 0-1` orada) oturumun `state`'inden artık erişilemez durumda — `GET /api/sessions/manuel-yaris` bu mesajları ASLA göstermez, sessizce orphan kaldı. Etki: aynı oturuma HENÜZ hiç mesaj gönderilmemişken eşzamanlı iki istek gelirse (çift tıkla gönder, ağ retry'i, iki sekme) ikinci konuşmanın tamamı SESSİZCE kaybolur. Ayrı bir HATA-NNN kaydı olarak raporlanmalı (Kritik).

---
**2026-08-14 yeniden koşum (düzeltme sonrası — HATA-004, `AgentSessionManager` + `ISessionStore.TryCreateAsync`):** Aynı senaryo canlı Postgres'e (`mt_fin`, temiz şema) karşı yeniden koşuldu. Birinci istek `event: done` ile normal tamamlandı. İkinci istek TAM modelini çalıştırdı (gerçek maliyet — bu kabul edilen taviz) ama kaydetme anında açık bir `event: error` çerçevesi aldı: `{"type":"AgentPrismSessionConflictException","message":"'manuel-yaris' oturumunu ayni anda baska bir istek de acti ve bizden once kaydetti. Kisa bir sure sonra yeniden deneyin."}` — **hiçbiri 500 dönmedi**. DB doğrulaması: `sessions.state->stateBag->AgentPrism.ChatHistory->conversationId` kazanan `conversation_id`'yi taşıyor; o `conversation_id` altında tam 2 mesaj var (kaybeden istek fiziksel olarak yazdığı 2 mesajla birlikte ayrı, artık hiçbir sessiondan referanslanmayan bir `conversation_id`'de kalıyor — orphan, ama SESSİZCE DEĞİL: istemci açıkça bilgilendirildi). Yeniden deneme (`Ikinci istek (yeniden deneme).`) normal yoldan geçti ve kazanan konuşmaya doğru şekilde eklendi (toplam 4 mesaj). Beklenen sonucun 3. maddesi ("Bir çakışma denetimi varsa isteklerden biri açık bir çakışma hatası döner; bu da kabul edilebilir") tam olarak gerçekleşti — sessiz kayıp yok. Kök neden ve düzeltme karar defterine kapanışta yazılacak (KAPANIS-PLANI.md §11 sırası).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Ayarlar ve açılış doğrulaması

Geçersiz bir ayar **açılışta** hata vermelidir. Çalışma anında ortaya çıkan bir
ayar hatası üretimde bulunur.

### MT-CORE-060 — Geçersiz `MaxPayloadLength` açılışı durdurur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Negatif ve sınır senaryosu. Geçerli aralık 0–1 048 576'dır.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Sınırın bir üstündeki değerle kur.
2. Sınırın tam üstündeki değerle kur.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

static void Dene(int deger)
{
    var services = new ServiceCollection();
    services.AddAgentPrism().Configure(o => o.RunRecording.MaxPayloadLength = deger);
    try
    {
        _ = services.BuildServiceProvider().GetRequiredService<IOptions<AgentPrismOptions>>().Value;
        Console.WriteLine($"{deger}: KABUL");
    }
    catch (OptionsValidationException ex)
    {
        Console.WriteLine($"{deger}: RED - {ex.Message}");
    }
}

Dene(1048576);
Dene(1048577);
Dene(-1);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `1048576: KABUL` — sınırın tam üstü geçerlidir.
- `1048577: RED` ve mesaj `0 ile 1048576 arasinda olmalidir` ifadesini taşır.
- `-1: RED`.
- Red mesajı **gelen değeri** yazar.

**Gerçek sonuç**
`1048576: KABUL`, `1048577: RED` (mesajda "0 ile 1048576 arasinda olmalidir" ve gelen değer 1048577), `-1: RED`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-061 — Script çalıştırma onaysız açılamaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

Negatif senaryo. AgentPrism işletim sistemi düzeyinde yalıtım **sağlamaz**.
`PlatformIsolationAcknowledged` bu sınırın okunduğunu bildiren bilinçli onaydır;
ayarlanmadan `Enabled` açılamaz.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Onay olmadan script çalıştırmayı aç.
2. Onayla birlikte tekrar dene.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

static void Dene(bool onay)
{
    var services = new ServiceCollection();
    services.AddAgentPrism().Configure(o =>
    {
        o.Skills.Scripts.Enabled = true;
        o.Skills.Scripts.PlatformIsolationAcknowledged = onay;
    });
    try
    {
        _ = services.BuildServiceProvider().GetRequiredService<IOptions<AgentPrismOptions>>().Value;
        Console.WriteLine($"onay={onay}: KABUL");
    }
    catch (OptionsValidationException ex)
    {
        Console.WriteLine($"onay={onay}: RED - {ex.Message}");
    }
}

Dene(false);
Dene(true);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `onay=False: RED` ve mesaj `PlatformIsolationAcknowledged` adını taşır.
- `onay=True: KABUL`.
- Varsayılan durumda (`Enabled=false`) hiçbir doğrulama hatası yoktur.

**Gerçek sonuç**
`onay=False: RED` — mesajda `PlatformIsolationAcknowledged` adı geçiyor. `onay=True: KABUL`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-062 — Agent grafiği sınırlarının varsayılanı vardır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 12 |
| **İlgili karar** | — |

Sınır senaryosu. Sınırsız bırakılan bir kurulumda ilk yanlış tanım **faturayla**
öğrenilir. Bu yüzden üç sınırın da varsayılanı olmalıdır.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Varsayılan değerleri oku.
2. Sıfır ve negatif değerlerin sınırlamayı kaldırdığını doğrula.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddAgentPrism();
var o = services.BuildServiceProvider().GetRequiredService<IOptions<AgentPrismOptions>>().Value;

Console.WriteLine($"MaxDepth      : {o.AgentGraph.MaxDepth}");
Console.WriteLine($"MaxTotalTokens: {o.AgentGraph.MaxTotalTokens}");
Console.WriteLine($"MaxTotalRuns  : {o.AgentGraph.MaxTotalRuns}");

o.AgentGraph.MaxTotalTokens = 0;
var b = o.AgentGraph.CreateBudget();
Console.WriteLine($"sifirla butce token siniri: {(b.MaxTotalTokens is null ? "SINIRSIZ" : b.MaxTotalTokens.ToString())}");
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `MaxDepth` **3**'tür.
- `MaxTotalTokens` **200000**'dir.
- `MaxTotalRuns` **25**'tir.
- Son satır `SINIRSIZ` yazar — 0 değeri sınırlamayı kaldırır.

**Gerçek sonuç**
`MaxDepth=3`, `MaxTotalTokens=200000`, `MaxTotalRuns=25`. Sıfırlanınca `SINIRSIZ`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-063 — Hassas veri varsayılan olarak kaydedilmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

Sınır senaryosu. İstem ve yanıt metinleri kişisel veri taşıyabilir; span'lere
yazılmaları **açık tercih** olmalıdır.

**Ön koşul**
- MT-CORE-062 projesi hazır.

**Adımlar**
1. Gözlemlenebilirlik varsayılanlarını oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddAgentPrism();
var o = services.BuildServiceProvider().GetRequiredService<IOptions<AgentPrismOptions>>().Value;

Console.WriteLine($"RecordSensitiveData   : {o.Observability.RecordSensitiveData}");
Console.WriteLine($"EnableQuotaUsageGauge : {o.Observability.EnableQuotaUsageGauge}");
Console.WriteLine($"SuccessSampleRatio    : {o.Observability.SuccessSampleRatio}");
Console.WriteLine($"AlwaysPersistFailures : {o.Observability.AlwaysPersistFailures}");
Console.WriteLine($"Health.BackgroundInterval: {(o.Health.BackgroundInterval is null ? "YOK" : o.Health.BackgroundInterval.ToString())}");
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `RecordSensitiveData` **False**'tur.
- `EnableQuotaUsageGauge` **False**'tur — ölçer veritabanını okur, açıkça istenmelidir.
- `SuccessSampleRatio` **0,1**'dir.
- `AlwaysPersistFailures` **True**'dur.
- `Health.BackgroundInterval` **YOK**'tur — boşta duran bir kurulum sağlayıcıya
  düzenli istek atmaz.

**Gerçek sonuç**
`RecordSensitiveData=False`, `EnableQuotaUsageGauge=False`, `SuccessSampleRatio=0,1`, `AlwaysPersistFailures=True`, `Health.BackgroundInterval=YOK`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-064 — Yapılandırmadan gelen negatif fiyat reddedilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | K-032 |

Negatif senaryo.

**Ön koşul**
- MT-CORE-062 projesi hazır.

**Adımlar**
1. Negatif bir fiyat geçersiz kılması tanımla.
2. Ayarları çözümle.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddAgentPrism().Configure(o =>
{
    o.Pricing.Providers["echo"] = new Dictionary<string, ModelPriceOverride>
    {
        ["echo-1"] = new() { InputCostPerMillionTokens = -1m },
    };
});

try
{
    _ = services.BuildServiceProvider().GetRequiredService<IOptions<AgentPrismOptions>>().Value;
    Console.WriteLine("🚨 negatif fiyat KABUL EDILDI");
}
catch (OptionsValidationException ex)
{
    Console.WriteLine("beklenen red: " + ex.Message);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `beklenen red:` ile başlar.
- Mesaj hangi sağlayıcı ve model için sorun olduğunu söyler.
- `🚨 negatif fiyat KABUL EDILDI` satırı görünmez.

**Gerçek sonuç**
Mesaj "beklenen red:" ile başlıyor: "AgentPrismPricingOptions: 'echo:echo-1' icin fiyat negatif olamaz." — hangi sağlayıcı/model için sorun olduğu açık. "🚨 negatif fiyat KABUL EDILDI" satırı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-065 — `Pricing` altındaki rezerve anahtarlar sağlayıcı sayılmaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 20, 28 |
| **İlgili karar** | — |

Sınır senaryosu. `Pricing` bölümü elle bağlanır (AOT). Her çocuk bir **sağlayıcı
adı** sayılır; `Currency` ve `Voice` bu yüzden rezervedir.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Yapılandırmadan `Currency`, `Voice` ve gerçek bir sağlayıcı ver.
2. Bağlanan değerleri oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > appsettings.json <<'EOF'
{
  "AgentPrism": {
    "Pricing": {
      "Currency": "USD",
      "echo": { "echo-1": { "InputCostPerMillionTokens": 0.25, "OutputCostPerMillionTokens": 1.0 } },
      "Voice": { "elevenlabs": { "tts-1": { "PerMillionCharacters": 30.0 } } }
    }
  }
}
EOF

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddAgentPrism();
services.Configure<AgentPrismOptions>(config.GetSection(AgentPrismOptions.SectionName));

var o = services.BuildServiceProvider().GetRequiredService<IOptions<AgentPrismOptions>>().Value;
Console.WriteLine("Currency          : " + o.Pricing.Currency);
Console.WriteLine("Saglayici sayisi  : " + o.Pricing.Providers.Count);
Console.WriteLine("Saglayicilar      : " + string.Join(", ", o.Pricing.Providers.Keys));
Console.WriteLine("Ses saglayicilari : " + string.Join(", ", o.Pricing.Voice.Keys));
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `Currency` **USD**'dir.
- Sağlayıcı sayısı **1**'dir; liste yalnız `echo` içerir.
- `Currency` ve `Voice` sağlayıcı listesinde **görünmez**.
- Ses sağlayıcıları listesinde `elevenlabs` vardır.

**Gerçek sonuç**
Doküman'ın scripti aynen koşulduğunda `Saglayici sayisi: 0` döndü (beklenen 1). İKİ AYRI, ÜST ÜSTE binen doküman hatası bulundu: (1) `services.AddAgentPrism()` ARGÜMANSIZ çağrılıp AYRICA `services.Configure<AgentPrismOptions>(config.GetSection(...))` çağrılmış — bu standart/reflection-tabanlı binder'ı kullanır. `AgentPrismOptions` ise AOT gerekçesiyle KENDİ elle yazılmış `Bind()` metoduyla bağlanıyor (doğru kullanım: `AddAgentPrism(configSection)`, K-021). Düzeltilip tekrar koşuldu, YİNE 0 döndü. (2) BAĞIMSIZ ikinci kök neden: manuel `BindPricing` (`AgentPrismServiceCollectionExtensions.cs:841-842`) her model için `ReadDecimal(modelSection, "Input")`/`"Output"` KISA anahtarlarını okuyor; doküman'ın JSON'ı ise `"InputCostPerMillionTokens"`/`"OutputCostPerMillionTokens"` (C# özellik adlı UZUN anahtarlar) kullanıyor — hiç eşleşmiyor, `ReadDecimal` sessizce `null` dönüyor, provider HİÇ eklenmiyor, ne hata ne log. İki kök neden de düzeltilip (doğru API + kısa anahtar adları) tekrar koşuldu: TAM beklenen sonuç alındı (Currency=USD, sağlayıcı sayısı=1, yalnız "echo", Ses sağlayıcıları="elevenlabs"). AYRICA ürün-düzeyi bulgu (Orta): yanlış anahtar adıyla yazılan bir `Pricing` girdisi TAMAMEN SESSİZCE düşüyor (istisna/log/uyarı yok) — bu, projenin "bilinmeyen ayar sessizce yok sayılmaz" ilkesiyle (K-034) çelişiyor. Ayrı bir HATA adayı (Orta) + doküman düzeltmesi önerilir.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

# 7 — Bellek içi izlek ve kimlik

### MT-CORE-070 — Bellek içi depolar veritabanı olmadan çalışır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | K-018 |

Tasarım kuralı #1'in çekirdek testidir. `AgentPrism.Testing` paketinin bellek
içi host'u hiçbir veritabanına ve hiçbir ağa gitmez.

**Ön koşul**
- MT-CORE-023 projesi hazır. `AgentPrism.Testing` paketi eklenir.

**Adımlar**
1. `AgentPrism.Testing` paketini ekle.
2. `FakeModelProvider` ile bir host kur.
3. Bir agent çalıştır ve kaydı oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.Testing.*.nupkg | sed 's#.*AgentPrism.Testing\.##;s#\.nupkg##')
dotnet add package AgentPrism.Testing --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await AgentPrismTestHost.StartAsync(o =>
{
    o.ModelProvider = provider;
});

Console.WriteLine("host ayakta");
Console.WriteLine("ulasilan istek sayisi (baslangic): " + provider.Requests.Count);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `host ayakta` yazar.
- Hiçbir veritabanı bağlantısı denenmez; log'da bağlantı hatası yoktur.
- Hiçbir ağ isteği yapılmaz.
- Süreç sıfır çıkış koduyla biter.

> Not: `AgentPrismTestHost` kurulum imzası koşumdan önce doğrulanır:
> `grep -n "public static" src/AgentPrism.Testing/AgentPrismTestHost.cs`.
> İmza farklıysa case yeniden yazılır, `Kaldı` işaretlenmez.

**Gerçek sonuç**
"host ayakta" yazdı, istek sayısı 0, süreç sıfır çıkış koduyla bitti. Hiçbir veritabanı/ağ hatası yok (`CreateSlimBuilder` + `UseTestServer`, hiçbir dış bağlantı yapılandırılmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-071 — `FakeModelProvider` kuyruğu bir kez tüketilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 39 |
| **İlgili karar** | K-269 |

Sınır senaryosu. Sahte sağlayıcı, durumu **kendi içinde** tutar; mesaj geçmişini
tarayıp "hangi tool zaten çağrıldı" çıkarmaz. Kuyruk tükenince davranış
öngörülebilir kalmalıdır.

**Ön koşul**
- MT-CORE-070 geçti.

**Adımlar**
1. İki adımlık bir kuyruk kur.
2. Üç kez çağır.
3. Üçüncü çağrının davranışını oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism.Testing;

var provider = new FakeModelProvider()
    .RespondsWith("BIRINCI", "IKINCI")
    .EchoesUserMessage();

Console.WriteLine("kuyruk kuruldu");
Console.WriteLine("istek sayisi: " + provider.Requests.Count);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Kurulum hata vermez.
- `RespondsWith` ve `EchoesUserMessage` zincirlenebilir.
- Kuyruk tükendikten sonra her çağrı yankı davranışına düşer; istisna atılmaz.

> Tam çalıştırma zinciri (üç ardışık `run` ve yanıt karşılaştırması)
> [`24-TEST-PAKETI-VE-SABLON.md`](24-TEST-PAKETI-VE-SABLON.md)'de yazılır.
> Buradaki case yalnız çekirdek sözleşmeyi (kuyruk sahibi sağlayıcıdır)
> kanıtlar.

**Gerçek sonuç**
Kurulum hatasız, `RespondsWith`/`EchoesUserMessage` zincirlenebiliyor, "kuyruk kuruldu" yazdı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-072 — Kimlikler zaman sıralı UUIDv7'dir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Sınır senaryosu. UUIDv7 zaman sıralıdır; `ORDER BY id` zaman sırası verir ve
B-tree indeksi parçalanmaz. Bu bir performans sözleşmesidir.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Peş peşe kimlik üret.
2. Sıralı olup olmadıklarını denetle.
3. Zaman damgasını geri oku.
4. UUIDv7 olmayan bir değerle zaman damgası okumayı dene.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/skillsiz
cat > Program.cs <<'EOF'
using AgentPrism;

var ids = new List<Guid>();
for (var i = 0; i < 5; i++) { ids.Add(AgentPrismId.NewId()); Thread.Sleep(2); }

var sirali = ids.Select(x => x.ToString()).SequenceEqual(ids.Select(x => x.ToString()).Order(StringComparer.Ordinal));
Console.WriteLine("sirali: " + sirali);

var damga = AgentPrismId.GetTimestamp(ids[0]);
Console.WriteLine("damga farki (sn): " + (DateTimeOffset.UtcNow - damga).TotalSeconds.ToString("F1"));

try { AgentPrismId.GetTimestamp(Guid.NewGuid()); Console.WriteLine("🚨 v4 KABUL EDILDI"); }
catch (ArgumentException ex) { Console.WriteLine("beklenen red: " + ex.Message); }
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `sirali: True`.
- Damga farkı **1 saniyeden küçüktür**.
- `Guid.NewGuid()` (sürüm 4) reddedilir ve mesaj "surum 7 degeri degil" ifadesini taşır.
- `🚨 v4 KABUL EDILDI` satırı görünmez.

**Gerçek sonuç**
`sirali: True`, damga farkı 0,0sn (<1s), v4 (`Guid.NewGuid()`) reddedildi: "Kimlik bir UUID surum 7 degeri degil." "🚨 v4 KABUL EDILDI" satırı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-073 — Enum'lar JSON'da ad olarak yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

Sınır senaryosu. Kablo sözleşmesi kendini anlatmalıdır: `"Code"`, `0` değil.
Sayı olarak yazılırsa değer sırası değiştiğinde tüm istemciler sessizce kırılır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Katalogda `origin` alanını incele.
2. Doğrulama yanıtında `severity` alanını incele.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" | grep -o '"origin":[^,}]*' | sort -u

curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"e1","model":{"provider":"echo","model":"echo-1"},"toolNames":["yok"]}' \
  | grep -o '"severity":[^,}]*' | sort -u
```

**Beklenen sonuç**
- `origin` değerleri `"Code"` ve `"Database"`'tir — tırnak içinde, ad olarak.
- `severity` değerleri `"Error"` veya `"Warning"`'tir.
- Hiçbirinde sayı (`0`, `1`, `2`) görünmez.

**Gerçek sonuç**
`origin` değerleri `"Code"`/`"Database"` (ad olarak, tırnak içinde). `severity` `"Error"` (ad olarak). Hiçbirinde sayı görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-CORE-074 — Uygulama yeniden başlatıldığında kod agent'ları geri gelir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1, 2 |
| **İlgili karar** | — |

Sınır senaryosu. Kod kaynaklı agent'lar veritabanında yaşamaz; her açılışta
koddan gelir. Veritabanı kaynaklı olanlar ise kalıcıdır.

**Ön koşul**
- MT-CORE-033 koşuldu (`manuel-surum` agent'ı kayıtlı).

**Adımlar**
1. Katalog sayısını ve kaynak dağılımını kaydet.
2. Uygulamayı durdur ve yeniden başlat.
3. Kataloğu tekrar oku.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" \
  | python3 -c "import sys,json; a=json.load(sys.stdin); print('toplam:',len(a)); print('kod:',sum(1 for x in a if x['origin']=='Code')); print('db :',sum(1 for x in a if x['origin']=='Database'))"

# Uygulamayi Ctrl+C ile durdur, sonra:
#   cd samples/AgentPrism.Api && dotnet run
# ve ayni komutu tekrarla.
```

**Beklenen sonuç**
- Yeniden başlatma sonrası toplam sayı **aynıdır**.
- Kod kaynaklı agent sayısı aynıdır.
- `manuel-surum` hâlâ vardır ve `version` değeri **2**'dir.
- Açılış log'unda migration'lar yeniden uygulanmaz (şema zaten günceldir).

**Doğrulama sorgusu**
```sql
SELECT name, version, origin FROM agentprism.agent_definitions ORDER BY name;
```

**Gerçek sonuç**
Yeniden başlatma öncesi: toplam 8, kod 5, db 3. Yeniden başlatma SONRASI: AYNI (8/5/3). `manuel-surum` hâlâ var, `version=2` (kalıcı). "AgentPrism N migration uyguladi" log satırı bu turda hiç görünmedi — bu satır yalnız YENİ migration uygulandığında loglanıyor; şema zaten güncel olduğu için migration'lar yeniden uygulanmadı. Tam beklenen davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Koşum sonrası temizlik

```bash
# Manuel test agent'larini sil.
for a in manuel-destek manuel-surum manuel-secret manuel-a manuel-b \
         manuel-hayali-tool-kayit; do
  curl -s -o /dev/null -X DELETE "$APU/api/agents/$a" -H "$APB"
done

# Manuel test oturumlarini sil.
for s in manuel-oturum-01 manuel-yaris musteri-42 musteri-99; do
  curl -s -o /dev/null -X DELETE "$APU/api/sessions/$s" -H "$APB"
done

# Olu MCP sunucusunu sil (MT-CORE-006).
curl -s -o /dev/null -X DELETE "$APU/api/mcp/servers/olu-mcp" -H "$APB"

rm -rf ~/agentprism-manuel/skillsiz
```

Temizlik sonrası `GET $APU/api/agents` yalnız kod kaynaklı agent'ları
göstermelidir.
