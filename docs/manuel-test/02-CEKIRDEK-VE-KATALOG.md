# 02 — Çekirdek ve Katalog (`CORE`)

> **Alan kodu:** `CORE` · **Faz:** 1, 3, 72, 101, 106
> **Kaynak:** `src/AgentPrism.Abstractions` · `src/AgentPrism.Core`
> (`Compilation/` · `Catalog/` · `Tools/` · `Sessions/` · `AgentPrismOptions*`)
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/02-CEKIRDEK-VE-KATALOG.md`](kosumlar/2026-08-13/02-CEKIRDEK-VE-KATALOG.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

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
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** `provider:"echo"` yanlış
> seçim — `EchoModelProvider.CreateChatClient`
> (`samples/AgentPrism.Api/EchoModelProvider.cs`) ağa çıkmayan yerel örnek
> sağlayıcı olduğu için `ModelBinding.ProviderSettings`'i hiç okumaz/
> doğrulamaz. Doğrulama gerçek mekanizması (`ModelProviderSettings.Validate`,
> `src/AgentPrism.Abstractions/Agents/ModelProviderSettings.cs`) yalnız ağa
> çıkan sağlayıcılarda (`anthropic`/`google`/`azure`) çalışır.
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-kotu-ayar",
  "model": {
    "provider": "anthropic",
    "model": "claude-3-5-haiku-latest",
    "providerSettings": { "anthropic.boyle.bir.ayar.yok": true }
  }
}'
```

~~Eski girdi (yanlış öncül — `echo` sağlayıcısı `ProviderSettings`'i hiç
okumaz): `provider: "echo"`, `providerSettings: { "echo.boyle.bir.ayar.yok":
true }`.~~

**Beklenen sonuç**
- `valid` `false`'tur ve `code` değeri `invalid_setting` olan bir mesaj vardır.
- Mesaj metni sağlayıcının **desteklediği anahtarları listeler**.
- Ayar sessizce yok sayılmaz.

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
curl -s -X PUT "$APU/api/mcp-servers/olu-mcp" -H "$APB" -H "content-type: application/json" \
  -d '{ "endpoint": "http://127.0.0.1:59999/mcp", "enabled": true }'

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
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** `AddAgentPrism()`
> (`src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs:381`)
> `AgentSkillCatalog`'u `TryAddSingleton` ile KOŞULSUZ kaydeder (doküman'ın
> `IAgentSkillCatalog` adı hatalı — arayüz yok, somut sınıf kaydediliyor) —
> bir tüketicinin `AddAgentPrism()` çağırdığı hiçbir standart senaryoda
> katalog "kayıtlı değil" olamaz. Doğru ve tek erişilebilir dal, tanımın
> **var olmayan bir skill'e** işaret ettiği dalıdır. 2026-08-15'te kod
> yeniden doğrulandı: `AgentDefinitionCompiler.cs:307`/`:1174`'teki "skill
> katalogu kayitli degil" hata dalı bugün de erişilemez durumda (ölü kod,
> yalnız bir tüketici `AgentSkillCatalog` kaydını elle kaldırırsa tetiklenir).
- Çıktı `beklenen istisna:` ile başlar.
- Mesaj "'skill-isteyen' agent'i 'olmayan-skill' skill'ine isaret ediyor
  ancak skill bulunamadi." ifadesini taşır.
- `AgentName` alanı `skill-isteyen`'dir.
- `🚨 istisna ATILMADI` satırı **görünmez**.

~~Eski beklenti (yanlış öncül — "minimum kurulumda katalog kayıtlı
değildir"): Mesaj "skill katalogu kayitli degil" ifadesini taşır.~~

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
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8):** Script'in kendisi iki farklı
> tool kayıt deyimini karıştırmış: `[AgentPrismTool("var_olan_tool", ...)]`
> özniteliğini koyuyor ama `.AddTool(delegate)` ile kaydediyor.
> `src/AgentPrism.Core/IAgentPrismBuilder.cs:47` `AddTool(Delegate method,
> string? name = null, ...)` — "name boş bırakılırsa METOT ADI kullanılır"
> diye açıkça belgeler, kasıtlı davranış. `[AgentPrismTool]` özniteliği
> yalnız `AddToolsFrom<T>()` tarafından okunur, `.AddTool(delegate)` onu hiç
> görmez. Doğru kayıtlı ad bu yüzden metot adı `"Var"`dır, öznitelikteki
> `"var_olan_tool"` DEĞİL.
- İstisna mesajı `tool-eksik` agent adını taşır.
- Mesaj eksik tool adını (`hayali_tool`) taşır.
- Mesaj **kayıtlı tool'ları listeler** (`Var` görünür — `.AddTool(delegate)`
  metot adını kullanır, `[AgentPrismTool]` özniteliğini değil).
- Mesaj `builder.AddAgentPrism().AddTool(...)` yönlendirmesini içerir.

~~Eski beklenti (yanlış öncül — script iki tool-kayıt idiomunu
karıştırmıştı): Mesaj kayıtlı tool'ları listeler (`var_olan_tool` görünür).~~

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

---

### MT-CORE-075 — Kültür sözlüğü boş agent'ta `culture` verilse de davranış değişmez (F-117)

Regresyon — mutlu yol.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- `InstructionsByCulture` boş bir agent (`kod-agent` gibi kod kaynaklı bir agent yeterli).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/kod-agent/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"merhaba","culture":"tr"}' | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıt, `culture` alanı hiç gönderilmemiş gibi davranır —
  `Instructions` kullanılır, hata veya farklı davranış yoktur.

---

### MT-CORE-076 — Eşleşen kültür kendi talimatını seçer (F-117)

Gerçek entegrasyon — mutlu yol, kritik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- `en` ve `tr` talimatlı bir agent (`POST /api/agents` ile `instructionsByCulture: {"tr": "Kisa cevap ver ve TAMAMEN TURKCE yaz."}` alanıyla oluştur; `instructions` alanına İngilizce bir varsayılan yaz).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"nasilsin","culture":"tr"}' | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Model yanıtı Türkçe talimata **uyar** (modelin kendi
  yorumuna bağlı olduğundan "birebir" değil, gözle kontrol: yanıt açıkça
  Türkçe ve kısa).

---

### MT-CORE-077 — Bölge alt etiketi ebeveynine düşer: `tr-TR` → `tr` (F-117)

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-076'daki agent.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"nasilsin","culture":"tr-TR"}' | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Yanıt MT-CORE-076 ile aynı şekilde Türkçe talimata uyar —
  `tr-TR` girdisi `tr` sözlük anahtarına düşer.

---

### MT-CORE-078 — Eşleşmeyen kültür varsayılana düşer, hata VERMEZ (F-117)

Negatif senaryo — K1: sessiz geri düşüş, patlama değil.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-076'daki agent.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/$AGENT/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"nasilsin","culture":"de"}'
```

**Beklenen sonuç**
- `HTTP: 200` — `de` sözlükte yoktur ama istek **reddedilmez**. Yanıt,
  agent'ın varsayılan (`en`) talimatına göre üretilir.

---

### MT-CORE-079 — `Accept-Language` başlığı talimatı DEĞİŞTİRMEZ (F-117)

Negatif senaryo — K-232'nin çizgisiyle tutarlı: sunucu içeriği ambient bir
tarayıcı başlığından beslenmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-076'daki agent.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$AGENT/run" -H "$APB" -H "content-type: application/json" \
  -H "Accept-Language: tr" -d '{"message":"nasilsin"}' | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Gövdede `culture` **verilmediği** için yanıt agent'ın
  varsayılan (İngilizce) talimatına göre üretilir — `Accept-Language: tr`
  başlığı **yok sayılır**, Türkçeye çevrilmez.

---

### MT-CORE-080 — Arka arkaya farklı kültürlerle `run` — önbellek yanlış dili TUTMAZ (F-117)

Sınır senaryosu — bu case `CompiledAgentCache` anahtarına kültürün
eklendiğini kanıtlar; eklenmeseydi ikinci çağrı ilk çağrının dilinde
kalırdı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-076'daki agent.

**Adımlar**
1. `culture: "tr"` ile `run` çağır, yanıtı kaydet.
2. Hemen ardından, aynı agent'a `culture` **vermeden** (veya `"en"` ile) `run` çağır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"nasilsin","culture":"tr"}' | python3 -m json.tool

curl -s -X POST "$APU/api/agents/$AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"nasilsin"}' | python3 -m json.tool
```

**Beklenen sonuç**
- İlk yanıt Türkçe talimata uyar. İkinci yanıt İngilizce talimata uyar —
  ilk çağrının derlenmiş agent'ı ikinciye SIZMAZ.

---

### MT-CORE-081 — Agent editöründe dil sekmesi; sürüm diff'i iki dili de gösterir (F-117)

Arayüz — mutlu yol.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | K-228 |

**Adımlar**
1. Konsolda MT-CORE-076'daki agent'ın düzenleme ekranını aç.
2. "Instructions" panelindeki "Instructions by culture" bölümünü bul.
3. Yeni bir satır ekle (`de` / `Kurz antworten.`), kaydet.
4. Agent'ın sürüm geçmişine git, son iki sürümü karşılaştır.

**Beklenen sonuç**
- 👤 Panelde her kültür satırı için bir dil kodu alanı ve bir metin alanı
  vardır; satır eklenip kaydedilince yeni sürüm oluşur. Sürüm karşılaştırma
  ekranında `de` için ayrı bir "Instructions (de)" bölümü belirir ve iki
  sürüm arasındaki fark (eklenen metin) vurgulanır. Dil TR↔EN değişince
  panel etiketleri çevrilir (`en.ts`/`tr.ts`, K-228).

---

### MT-CORE-082 — Zorunlu parametre eksikken `run` başlamaz; ad hatada geçer (Faz 86, F-34)

Mutlu yoldan sapma — koşu **hiç başlamamalı** ve eksik parametrenin adı
gövdede görünmeli.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 86 |
| **İlgili karar** | — |

**Ön koşul**
- `"musteri"` adlı zorunlu (`required: true`), varsayılansız bir parametre
  taşıyan ve talimatında `{{musteri}}` geçen bir agent (`PARAM_AGENT`)
  oluşturulmuş olmalı.

**Adımlar**
1. `parameters` alanı **olmadan** `run` çağır.
2. Aynı gövdeyi `estimate` ucuna gönder.
3. Aynı gövdeyi `POST /api/agents/validate`'e gönder (tanımın kendisiyle,
   `run`'daki gibi bir değer kümesiyle değil — bu uç şema tutarlılığını
   kontrol eder, değer eksikliğini değil).

**Girilecek veri**
```bash
for UC in run estimate; do
  curl -s -X POST "$APU/api/agents/$PARAM_AGENT/$UC" -H "$APB" -H "content-type: application/json" \
    -d '{"message":"selam"}' | python3 -m json.tool
done
```

**Beklenen sonuç**
- Her iki uç da `400` döner; gövdedeki `missingParameters` dizisi
  `"musteri"` içerir. İki ucun hata gövdesi **aynı biçimdedir** — tek bir
  doğrulayıcı (`AgentParameterValidator`) her ikisini de besler.

---

### MT-CORE-083 — Fazladan parametre sessizce yutulmaz (Faz 86, F-34)

Sınır senaryosu — bir yazım hatası taşıyan parametre adı üretimde fark
edilmeden kaybolmamalı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 86 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-082'deki `PARAM_AGENT`.

**Adımlar**
1. `run` çağır; `parameters` alanında hem `musteri` hem de var olmayan bir
   `musteriii` anahtarı gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$PARAM_AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"selam","parameters":{"musteri":"Acme","musteriii":"oops"}}' | python3 -m json.tool
```

**Beklenen sonuç**
- `400`; gövdedeki `unknownParameters` dizisi `"musteriii"` içerir. Koşu
  başlamaz, model çağrılmaz.

---

### MT-CORE-084 — Değer JSON yapısını bozmaz; talimat tırnak içeren değerle bile geçerli JSON üretir (Faz 86, F-34)

Mutlu yol — JSON-güvenli kaçışın gerçek bir sağlayıcı isteğinde çalıştığını
kanıtlar.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 86 |
| **İlgili karar** | — |

**Ön koşul**
- Talimatı `{"customer": "{{musteri}}"}` gibi bir JSON örneği taşıyan bir
  agent.

**Adımlar**
1. `musteri` değeri olarak tırnak ve ters bölü içeren bir metin gönder
   (`a"b\c`).
2. Çalıştırmanın kaydını (`/api/runs/{id}` veya trace) incele; sağlayıcıya
   giden talimatın hâlâ geçerli JSON olduğunu doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$PARAM_AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"selam","parameters":{"musteri":"a\"b\\c"}}' | python3 -m json.tool
```

**Beklenen sonuç**
- Koşu başarıyla tamamlanır (sağlayıcı JSON'u ayrıştırma hatası vermez).
  Kayıtlı girdi metninde `a"b\c` **kaçırılmış** biçimde görünür
  (`a\"b\\c`), talimatın çevresindeki JSON yapısı bozulmamıştır.

---

### MT-CORE-085 — Paylaşılan talimat bloğu prepend edilir; bloğa referans veren blok reddedilir (Faz 86, F-34)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 86 |
| **İlgili karar** | — |

**Ön koşul**
- `house-rules` adlı, `Instructions = "Her zaman kaynağını belirt."` olan
  sıradan bir agent tanımı (blok olarak kullanılacak, hiç çalıştırılmayacak).

**Adımlar**
1. `SharedInstructionsName: "house-rules"` taşıyan yeni bir agent
   (`SHARED_AGENT`) oluştur, `Instructions = "Fatura sorularını yanıtla."`.
2. `SHARED_AGENT`'ı çalıştır; talimatın her iki metni de içerdiğini
   trace/kayıttan doğrula.
3. `house-rules`'un kendisine `SharedInstructionsName: "SHARED_AGENT"`
   (döngü) yazmayı dene.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"shared-e2e","instructions":"Fatura sorularini yanitla.","sharedInstructionsName":"house-rules","model":{"provider":"...","model":"..."}}' \
  | python3 -m json.tool
```

**Beklenen sonuç**
- Adım 2: derleme başarılı; birleşik talimat `"Her zaman kaynağını belirt.\n\nFatura sorularını yanıtla."`
  biçimindedir. Adım 3: `400` — bir blok başka bir bloğa referans veremez,
  derlemede reddedilir (döngü tespiti gerekmez, tek atlama kuralı yeter).

---

### MT-CORE-086 — Aşırı uzun parametre değeri koşuyu düşürür (Faz 86, F-34, denetim 🟡 bulgusu)

Sınır senaryosu — `AgentPrismOptions.MaxParameterValueLength` (varsayılan 4096
bayt UTF-8) sınırını doğrular.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 86 |
| **İlgili karar** | K-582 |

**Ön koşul**
- MT-CORE-082'deki `PARAM_AGENT`.

**Adımlar**
1. `musteri` değeri olarak 4096 bayttan uzun bir metin gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/$PARAM_AGENT/run" -H "$APB" -H "content-type: application/json" \
  -d "{\"message\":\"hi\",\"parameters\":{\"musteri\":\"$(python3 -c "print('a'*5000)")\"}}" | python3 -m json.tool
```

**Beklenen sonuç**
- `400`; gövdede `"detail":"Parameter 'musteri' exceeds the maximum value
  length."` ve `tooLongParameters: ["musteri"]`. Koşu başlamaz, model
  çağrılmaz. 2026-08-22'de `samples/AgentPrism.Api`'ye karşı canlı doğrulandı
  (bkz. `docs/arsiv/fazlar/86-TALIMATIN-GIRDI-YUZEYI.md`, "Doğrulama komutları").

---

### MT-CORE-087 — Özel `IAgentSource` ajanı `Custom` origin ile listelenir, düzenleme formu açılmaz (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/AgentPrism.Api/Program.cs`'e geçici olarak
  `builder.AddAgentPrism().Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = "<dizin>" })`
  ve `.AddAgentSource<JsonFileAgentSource>()` eklenmiş, `<dizin>` altında
  `{"name":"greeter","instructions":"Reply briefly.","model":{"provider":"echo","model":"echo-1"}}`
  içerikli bir `greeter.json` dosyası bulunan bir `samples/AgentPrism.Api` koşumu.
  (`samples/AgentPrism.Samples.CustomAgentSource`'un `PackageReference`'ı
  local feed'den çözülür — bkz. `samples/NuGet.config`.)

**Adımlar**
1. `GET /agentprism/api/agents` çağır; `greeter` adlı agent'ı bul.
2. Konsolda agent listesini aç, `greeter`'ı tıkla.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" | python3 -m json.tool | grep -A 3 '"name": "greeter"'
```

**Beklenen sonuç**
- Adım 1: `origin: "Custom"`, `sourceName: "json-file"`, `isEditable: false`.
- Adım 2: rozet kaynağın adını gösterir; agent açılınca düzenleme formu **yok**
  (kod agent'ıyla aynı salt-okunur görünüm).

---

### MT-CORE-088 — `Custom` kaynağa ait ada `PUT`/`POST` çakışması `409` döner ve kaynağı adlandırır (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-087'deki kurulum (`greeter` agent'ı `json-file` kaynağında).

**Adımlar**
1. `PUT /api/agents/greeter` dene.
2. Aynı adla `POST /api/agents` dene.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/agents/greeter" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"greeter","instructions":"x","model":{"provider":"echo","model":"echo-1"}}'
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"greeter","instructions":"x","model":{"provider":"echo","model":"echo-1"}}'
```

**Beklenen sonuç**
- İkisi de `409`; gövdedeki `detail` alanı `'greeter' belongs to the
  'json-file' agent source` metnini içerir — kaynağın adı görünür, `404`
  (var olmayan kayıt) yerine doğru çakışma anlatılır.

---

### MT-CORE-089 — `Custom` kaynaktaki agent gerçek bir `run` tamamlar; kayıt model/sağlayıcı alanlarını dolu taşır (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-087'deki kurulum.

**Adımlar**
1. `greeter` ile bir `run` başlat.
2. `run` kaydını (`/api/runs/{id}`) incele.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/greeter/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"selam"}' | python3 -m json.tool
```

**Beklenen sonuç**
- Koşu tamamlanır. `run` kaydında `model` ve `provider` alanları **boş
  değildir** — kaynağın kendi `ListAsync`'inden gelen dondurulmuş descriptor
  doğru atıf taşır (uydurma `Origin=Code`/`Model=null` yok).

---

### MT-CORE-090 — Bozuk bir kaynak varken `GET /api/agents` sağlıklı kalır (Faz 101, fail-open)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- Ön koşul MT-CORE-087'deki gibi, ama `JsonFileAgentSourceOptions.Directory`
  var olmayan/okunamaz bir yola işaret ediyor (kaynağın `ListAsync`'i her
  çağrıda istisna fırlatsın diye).

**Adımlar**
1. `GET /agentprism/api/agents` çağır.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/agents" -H "$APB"
```

**Beklenen sonuç**
- `200`. Yerleşik `code`/`database` kaynaklarının agent'ları listede görünür;
  bozuk kaynağınkiler yoktur. Sunucu logunda kaynak adını taşıyan bir
  `LogError` satırı vardır.

---

### MT-CORE-091 — Bozuk kaynağa ait adla `run` denemesi ham hata metnini sızdırmaz (Faz 101, fail-closed)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-090'daki bozuk kaynak kurulumu; kaynağın adı bilinsin (örn. `json-file`).

**Adımlar**
1. Bozuk kaynağa ait olduğu bilinen bir adla `run` dene (`greeter`).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/greeter/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message":"selam"}' | python3 -m json.tool
```

**Beklenen sonuç**
- `400`; `detail` alanı yalnız `Agent source 'json-file' failed during
  resolve (agent_source_failed).` biçimindedir — dosya sistemi hata metni,
  yol adı veya .NET exception stack'i **görünmez**.

---

### MT-CORE-092 — `GET /api/diagnostics` kayıtlı kaynakları önceliğe göre listeler (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- MT-CORE-087'deki kurulum; `EnableDiagnosticsEndpoint=true`.

**Adımlar**
1. `GET /agentprism/api/diagnostics` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool | grep -A 15 agentSources
```

**Beklenen sonuç**
- `agentSources` dizisi `[{"name":"code","priority":0,...},
  {"name":"database","priority":100,...},
  {"name":"json-file","priority":200,...}]` sırasıyla üç girdi taşır.

---

### MT-CORE-093 — Aynı `Name`'e sahip iki kaynak host'u başlatmaz (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/AgentPrism.Api/Program.cs`'e aynı `Name`'i döndüren iki
  `IAgentSource` (örn. `AddAgentSource<JsonFileAgentSource>()` iki farklı
  dizinle, ikisi de `json-file` adını taşıyacak şekilde) geçici eklenmiş.

**Adımlar**
1. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet run --no-build -c Release --urls http://localhost:5081
```

**Beklenen sonuç**
- Uygulama **başlamaz**; konsolda `AgentPrismAgentSourceException` ve iki
  kaynağın adını taşıyan bir hata mesajı görünür. `curl` bağlantı
  reddedilir (`http://localhost:5081` açılmaz).

---

### MT-CORE-094 — Yavaş bir kaynak başlangıcı geciktirmez (Faz 101, startup doğrulaması I/O yapmaz)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- `ListAsync`'i çağrıldığında 3 saniye bekleyen bir test `IAgentSource`
  geçici eklenmiş `samples/AgentPrism.Api` koşumu.

**Adımlar**
1. Uygulamayı başlat; ilk `200` yanıtına kadar geçen süreyi ölç.

**Girilecek veri**
```bash
time (until curl -s -o /dev/null "$APU/api/agents" -H "$APB"; do sleep 0.2; done)
```

**Beklenen sonuç**
- Başlangıç **gecikmez** (birkaç saniye içinde, tipik soğuk başlangıç
  süresiyle aynı mertebede) — startup doğrulaması `ListAsync`'i hiç çağırmaz,
  yalnız `Name`/`Priority` alanlarını okur.

---

### MT-CORE-095 — Kiracıya duyarlı özel kaynak: kiracı A'nın listesi kiracı B'nin agent'ını içermez (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- Çok kiracılı bir kurulum ve `ITenantContext`'i okuyan, kiracıya göre
  filtreleyen özel bir `IAgentSource` (bu depoda hazır örneği yok — bu case
  `TenantAwareAgentSourceContract`'ı sağlayan bir kaynak yazıldığında koşulur;
  `AgentPrism.Testing.Contracts.AgentSources.TenantAwareAgentSourceContract`
  otomatik sözleşme testinin insan gözüyle tekrarıdır).

**Adımlar**
1. Kiracı A kimliğiyle `GET /api/agents` çağır.
2. Kiracı B kimliğiyle `GET /api/agents` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "X-Tenant-Id: tenant-a" -H "$APB"
curl -s "$APU/api/agents" -H "X-Tenant-Id: tenant-b" -H "$APB"
```

**Beklenen sonuç**
- Listeler ayrışır; kiracı B'nin listesi kiracı A'ya özel agent'ı içermez.

> 👤 **İnsan gerekir** — bu depoda kiracıya duyarlı bir örnek `IAgentSource`
> yok; case yalnız böyle bir kaynak eklendiğinde koşulabilir. Otomatik
> karşılığı `TenantAwareAgentSourceContract` sözleşme suite'idir
> (`tests/AgentPrism.Core.UnitTests/Catalog/BuiltInAgentSourceContractTests.cs`,
> `DefinitionStoreTenantAwareAgentSourceContractTests`).

---

### MT-CORE-096 — `AddAgentSource` kayıt yüzeyi: generic ve factory aşırı yüklemeleri tek instance üretir (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/AgentPrism.Samples.CustomAgentSource.Tests` projesi local feed'den
  restore edilebilir durumda (`dotnet pack AgentPrism.src.slnf -c Release`
  önceden koşulmuş).

**Adımlar**
1. `dotnet test samples/AgentPrism.Samples.CustomAgentSource.Tests -c Release` çalıştır.

**Girilecek veri**
```bash
dotnet test samples/AgentPrism.Samples.CustomAgentSource.Tests -c Release --no-build
```

**Beklenen sonuç**
- 15/15 test geçer: `AgentSourceContract` ailesinin 12 senaryosu,
  `JsonFileAgentSourceRunTests`'in generic (`AddAgentSource<T>()`) ve factory
  (`AddAgentSource(factory)`) kayıt yollarıyla yaptığı iki gerçek `run`, ve
  kapsam testi. `TryAddEnumerable` sample kodunda hiç geçmez
  (`grep -c TryAddEnumerable samples/AgentPrism.Samples.CustomAgentSource*/*.cs` → `0`).

---

### MT-CORE-097 — Complex tool sonucu canonical JSON ve output limiti taşır (Faz 102)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |

**Ön koşul**
- `samples/AgentPrism.Samples.CustomTool.Tests` local feed ile restore edilir.

**Adımlar**
1. Sample testlerini çalıştır.

**Girilecek veri**
```bash
dotnet test samples/AgentPrism.Samples.CustomTool.Tests -c Release --no-build
```

**Beklenen sonuç**
- `preview_order` generated tool'u `OrderPreview` record'unu JSON olarak modele geçirir.
- `OrderId` ve `Status` alanları cevapta görünür; CLR tip adı görünmez.
- Registration `MaxOutputBytes = 768` taşır.

---

### MT-CORE-098 — Doğrulanmamış tool registry startup'ta reddedilir (Faz 102)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |

**Adımlar**
1. Kendi `IToolRegistry` implementation'ını kaydet ve host'u başlat.
2. Aynı kurulumu `Tools:AllowUnverifiedToolRegistry=true` ile tekrar başlat.

**Beklenen sonuç**
- Varsayılan kurulum `AgentPrismException` ile başlangıçta durur.
- Opt-in kurulum başlar ve warning yazar; bu yalnız geçiş yolu içindir.

---

### MT-CORE-099 — Tool hatası foreign exception ayrıntısını sızdırmaz (Faz 102)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. Tool gövdesinden bağlantı dizesi taşıyan yabancı bir exception at.
2. Run kaydını ve SSE hata olayını incele.

**Beklenen sonuç**
- İki yüzeyde de yalnız `Tool failed with <ExceptionType>.` görünür.
- Bağlantı dizesi veya özgün exception mesajı görünmez.

### MT-CORE-100 — BYOK desteklemeyen provider'da tenant credential fail-closed'tır (Faz 103)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. Yalnız `IModelProvider` uygulayan (BYOK yok) bir provider kaydet.
2. Bu provider'a bağlı bir agent'ı tenant credential ile çalıştır.

**Beklenen sonuç**
- Provider/setup client çağrısı **0** kez yapılır.
- Stable `provider_credential_unsupported` hatası döner; setup/global credential ile sessizce çalışmaz.
- Credential değeri hiçbir response veya log metninde yer almaz.

### MT-CORE-101 — `AddRunJudge` üç overload'ı AgentSource deseniyle aynı idempotency'i taşır (Faz 103)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |

**Adımlar**
1. `AddRunJudge<TJudge>()`'ı aynı tip için iki kez çağır.
2. `AddRunJudge(instance)` ve `AddRunJudge(factory)`'i farklı yapılandırılmış örneklerle çağır.

**Beklenen sonuç**
- Generic aşırı yükleme tekrar kaydı yok sayar (tek singleton).
- Instance/factory aşırı yüklemeleri farklı yapılandırılmış sonuçları korur.
- Aynı `Name` case-insensitive çakışırsa startup `RunJudgeSet` hata verir.

### MT-CORE-102 — `JudgeTimeout` token'ı yok sayan judge için gerçek wait cutoff'tur (Faz 103)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |

**Adımlar**
1. Cancellation token'ını yok sayan, gecikmeli bir custom judge kaydet; `JudgeTimeout`'u kısa tut.
2. Manuel judge ucunu çağır.

**Beklenen sonuç**
- Çağrı `JudgeTimeout` civarında `judge_timeout` ile döner; judge gövdesi arkada devam eder.
- Gövde sonradan başarı veya hata ile bitse de skor yazılmaz; `TaskScheduler.UnobservedTaskException` üretilmez.

### MT-CORE-103 — `AgentDefinitionCompiler` ayrıştırmasından sonra `support` agent aynı şekilde çalışır (Faz 106)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |

**Adımlar**
1. `samples/AgentPrism.Api`'yi PostgreSQL ile ayağa kaldır (`ap-pg` container).
2. `POST /agentprism/api/agents/support/run` ile gerçek bir mesaj gönder.
3. `GET /agentprism/api/runs?agentName=support&limit=1` ile sonucu doğrula.

**Beklenen sonuç**
- SSE akışı `event: update` (metin + usage) ve `event: done` ile biter.
- `/api/runs` listesinde `status: "Completed"`, gerçek `usage` (input/output token) ile yeni bir kayıt görünür.
- **Koşuldu (2026-08-26):** run `01a03b4f-e7c2-7177-a572-596ad682c0fd`, `usage.inputTokens: 234, outputTokens: 40`, `status: "Completed"`.

### MT-CORE-104 — Culture taşıyan tanım en yakın culture talimatını çözer (Faz 106)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |

**Adımlar**
1. `InstructionsByCulture` içinde `"tr"` girdisi taşıyan bir tanım derle.
2. `culture: "tr"` ile `Compile`/`CompileAsync` çağır.

**Beklenen sonuç**
- `ChatOptions.Instructions` `"tr"` girdisinin metnini taşır, varsayılan `Instructions`'ı değil.
- Otomatik koşuldu: `AgentDefinitionCompilerPathTests.Sync_full_overload_resolves_the_requested_culture`,
  `Async_CompileAsync_resolves_the_requested_culture`.

### MT-CORE-105 — Shared instructions kullanan tanım sync yolda açık hata verir, async yolda çözülür (Faz 106)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |

**Adımlar**
1. `SharedInstructionsName` taşıyan bir tanım için sync `Compile()` çağır.
2. Aynı tanım için async `CompileAsync()` çağır.

**Beklenen sonuç**
- Sync yol `AgentPrismCompilationException` fırlatır ("does not resolve shared instructions blocks. Use CompileAsync()").
- Async yol bloğun metnini kendi talimatının önüne ekler.
- Otomatik koşuldu: `SharedInstructionsTests.Synchronous_Compile_refuses_a_definition_that_references_a_block`,
  `SharedInstructionsTests.Blocks_text_is_prepended_to_the_agents_own_instructions`.
