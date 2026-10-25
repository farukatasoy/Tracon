# 02 — Çekirdek ve Katalog (`CORE`)

> **Alan kodu:** `CORE` · **Faz:** 1, 3, 72, 101, 106, 127, 130, 135, 189, 194, 201, 206, 215, 233, 234, 242
> **Kaynak:** `src/Tracon.Abstractions` · `src/Tracon.Core`
> (`Compilation/` · `Catalog/` · `Tools/` · `Sessions/` · `TraconOptions*`)
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16) kaydı silindi (2026-10-17); tam metin:
> git show 2a8149ef:docs/arsiv/manuel-test-kosum-2026-09/02-CEKIRDEK-VE-KATALOG.md
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 0a802674:docs/manuel-test/kosumlar/2026-08-13/02-CEKIRDEK-VE-KATALOG.md

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
    C -.->|hata| H["TraconCompilationException"]
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
2. PostgreSQL container'ı (`ap-pg`) çalışır ve `Tracon:PostgreSql:ConnectionString`
   tanımlıdır. Veritabanı gereklidir: tanım **kaydı** onsuz denenemez.
3. `Tracon:Ui:AuthToken` `manuel-test-token-2026` olarak tanımlıdır.
4. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080`

Kısaltma — bu dosyadaki her `curl` şu başlıkları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
```

> 🚨 **`/run` yanıtı SSE akışıdır ve metin token token gelir.** Ham
> `grep "Kelime"` **boş dönebilir**: kelime `" Kel"` + `"ime"` diye bölünür.
> Akış case'lerinde parçaları birleştirmeden arama yapma. Ayrıca akış
> içinde `event: error` olayı gelebilir — çıkış kodu yine 0'dır, bu yüzden
> hata **gövdede** aranır.
>
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

Negatif senaryo. Bu, Tracon'in güvenlik sınırıdır: arayüzden agent
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
- Örnek uygulama **`Tracon__Egress__AllowPrivateNetworkTargets=true`** ile
  çalışıyor. 🚨 Bu ayar olmadan kayıt adımı `400 Address not allowed` döner:
  SSRF kapısı özel ağ adreslerini varsayılanda engeller. Kapı burada test
  edilenin önüne geçer, konusu `18-MCP-VE-A2A.md`'dir.
- Erişilemeyen bir MCP sunucusu kaydedilmiştir
  (adres: `http://127.0.0.1:59999/mcp` — hiçbir şey dinlemiyor).
- 🚨 Case bitince kayıt **silinir** (`DELETE $APU/api/mcp-servers/olu-mcp`).
  Kalırsa sonraki her doğrulama `inconclusive` döner ve sonraki case'ler
  yanlış sonuç verir.

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
SELECT name, version FROM tracon.agent_definitions WHERE name LIKE 'manuel-%' ORDER BY name;
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

D '{"name":"c1","model":{"provider":"echo","model":"echo-1"},"compaction":{"strategy":"Summarization"}}'
D '{"name":"c2","model":{"provider":"echo","model":"echo-1"},"compaction":{"strategy":"BoyleBirSeyYok","triggerTokens":1000}}'
# c3: modelin katalogda context window degeri OLMAMALI, yoksa deger TURETILIR
D '{"name":"c3","model":{"provider":"echo","model":"katalogda-yok"},"compaction":{"strategy":"ContextWindow","triggerTokens":1000}}'
```

**Beklenen sonuç**
- `c1`: mesaj `TriggerTokens/TriggerMessages/TriggerTurns`'ten en az birinin
  gerektiğini söyler.
  🚨 Strateji adı **`Summarization`**'dır, `Summarize` değil. Geçerli adlar:
  `None · SlidingWindow · Truncation · ToolResult · Summarization · ContextWindow`
  (`src/Tracon.Abstractions/Agents/CompactionStrategyKind.cs`). Yanlış ad
  doğrulama katmanına hiç ulaşmaz, JSON okuyucusunda 400 olur.
- `c2`: HTTP 400. Mesaj bilinmeyen strateji adını **aynen taşımaz** ve geçerli
  değerleri listelemez — alan güçlü tiplenmiş olduğu için istek Tracon'un
  doğrulayıcısından önce düşer. Bu bilinen bir tanı zayıflığıdır (`HATA-S1-008`,
  2026-09-16); düzelene kadar beklenen budur.
- `c3`: mesaj `MaxContextWindowTokens` alanının eksik olduğunu söyler **ve**
  modelin katalogda context window değeri olmadığını ekler.
  🚨 Bu ancak model katalogda **yoksa** olur: değer verilmediğinde
  `ModelDescriptor.ContextWindowTokens`'tan **türetilir** ve derleme geçer
  (`AgentDefinitionCompiler.Compaction.cs:145-168`, bilinçli karar). `echo-1`
  gibi katalogda olan bir modelle bu case `valid=true` döner.
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
rm -rf ~/tracon-manuel/skillsiz && mkdir -p ~/tracon-manuel/skillsiz
cd ~/tracon-manuel/skillsiz
dotnet new console -o . --force
cp ~/tracon-manuel/uretec/nuget.config .
SURUM=$(ls ~/tracon-local-feed/Tracon.Core.*.nupkg | sed 's#.*Tracon.Core\.##;s#\.nupkg##')
dotnet add package Tracon.Core --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddTracon().AddAgent(new AgentDefinition
{
    Name = "skill-isteyen",
    Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
    SkillNames = ["olmayan-skill"],
});

var provider = services.BuildServiceProvider();
var catalog = provider.GetRequiredService<IAgentCatalog>();

try
{
    await catalog.ResolveAsync("skill-isteyen", null, CancellationToken.None);
    Console.WriteLine("🚨 istisna ATILMADI");
}
catch (TraconCompilationException ex)
{
    Console.WriteLine("beklenen istisna: " + ex.Message);
    Console.WriteLine("AgentName: " + ex.AgentName);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `beklenen istisna:` ile başlar.
- Mesaj `Agent 'skill-isteyen' refers to skill 'olmayan-skill', but the skill
  was not found.` ifadesini taşır. 🚨 Sevk edilen metin K-228'den beri
  **İngilizce**dir; Türkçe bir alıntı görürsen spec bayattır.
- `AgentName` alanı `skill-isteyen`'dir.
- `🚨 istisna ATILMADI` satırı **görünmez**.

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
cd ~/tracon-manuel/skillsiz
dotnet add package Tracon.Testing --version "$SURUM"
cat > Program.cs <<'EOF'
using Tracon;
using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddTracon()
    // Saglayici kaydi SART: derleme tool denetiminden ONCE saglayicida duser.
    // `echo` yalniz ornek uygulamanin kendi provider'idir, yalin projede yoktur.
    .AddModelProvider(new FakeModelProvider("echo"))
    .AddTool(TraconManuelTools.Var)
    .AddAgent(new AgentDefinition
    {
        Name = "tool-eksik",
        Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
        ToolNames = ["hayali_tool"],
    });

var provider = services.BuildServiceProvider();
var catalog = provider.GetRequiredService<IAgentCatalog>();

try { await catalog.ResolveAsync("tool-eksik", null, CancellationToken.None); Console.WriteLine("🚨 istisna ATILMADI"); }
catch (TraconCompilationException ex) { Console.WriteLine(ex.Message); }

internal static class TraconManuelTools
{
    [TraconTool("var_olan_tool", "Kayitli bir tool.")]
    public static string Var(string x) => x;
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- İstisna mesajı `tool-eksik` agent adını taşır.
- Mesaj eksik tool adını (`hayali_tool`) taşır.
- Mesaj **kayıtlı tool'ları listeler** (`Var` görünür — `.AddTool(delegate)`
  metot adını kullanır, `[TraconTool]` özniteliğini değil).
- Mesaj `builder.AddTracon().AddTool(...)` yönlendirmesini içerir.

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
-- display_name diye bir sutun YOK: gorunen ad `definition` jsonb'sinin icindedir.
SELECT name, version, definition->>'displayName' AS display_name
FROM tracon.agent_definitions WHERE name = 'support';
-- Kod kaynakli agent DB'ye hic yazilmaz: beklenen sonuc 0 satirdir.
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
SELECT count(*) FROM tracon.runs WHERE agent_name = 'hic-boyle-bir-agent-yok';
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
FROM tracon.runs WHERE agent_name = 'manuel-surum' ORDER BY started_at;
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
- `support` için yanıt ya **200 + boş liste** (sürüm geçmişi yok) ya da
  **4xx** döner. Her iki durumda da gövde agent'ın **kod kaynaklı** olduğunu
  söylemelidir. Hangi biçim seçildiği koşumda kaydedilir.
- 🚨 Gövde **"böyle bir agent yok" DEMEMELİDİR**: `support` vardır
  (`GET /api/agents/support` → 200). 2026-09-16'da ölçülen davranış budur ve
  `HATA-S1-009` olarak açıktır — düzelene kadar bu case `Kaldı`dır.
  Karşılaştırma noktası `MT-CORE-030`: aynı durumda `POST /api/agents` doğru
  ve açıklayıcı bir 409 döndürür.
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
  bu beklenendir; Tracon serbest metadata'yı denetlemez.
- 🚨 `agent_definitions` tablosunda Tracon'in **kendi** yazdığı hiçbir
  sağlayıcı anahtarı yoktur.

**Doğrulama sorgusu**
```sql
SELECT name, definition::text FROM tracon.agent_definitions
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
- En az şu üçü görünür: `cancel_order`, `get_order_status`,
  `list_recent_orders`. 🚨 Sayı **sabit değildir** — örnek uygulama büyüdükçe
  artar (2026-09-16'da 10 tool). Sayıyı sabitleme, varlığı denetle.
- Liste **ada göre sıralıdır**.
- `cancel_order` için `requiresApproval` alanı `true`'dur; diğer ikisi `false`.
- 🚨 Kodda tanımlı her tool'un `source` alanı **`null`**'dır. Alan kod/uzak-MCP
  ayrımı içindir: `null` = kodda tanımlı, dolu = uzak MCP sunucusunun adı
  (`src/Tracon.Abstractions/Tools/ToolDescriptor.cs:30-38`). `"generated"`
  diye bir değer yoktur.
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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddTracon()
    .AddTool(AIFunctionFactory.Create((string a) => a, "ayni_ad", "Birinci."))
    .AddTool(AIFunctionFactory.Create((string a) => a, "ayni_ad", "Ikinci."));

var provider = services.BuildServiceProvider();

try
{
    var registry = provider.GetRequiredService<IToolRegistry>();
    Console.WriteLine("🚨 istisna ATILMADI - tool sayisi: " + registry.List().Count);
}
catch (TraconException ex)
{
    Console.WriteLine("beklenen istisna: " + ex.Message);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `beklenen istisna:` ile başlar.
- Mesaj `More than one tool is registered with name 'ayni_ad'. Tool names
  must be unique.` ifadesini taşır. 🚨 Sevk edilen metin K-228'den beri
  **İngilizce**dir.
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
- `Tracon:Providers:OpenAI:ApiKey` tanımlıdır (gerçek model çağrısı gerekir).

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
- 🚨 `cost_usd` diye bir sütun **yoktur**. Maliyet alanları
  `input_cost · output_cost · cost_currency · pricing_source`'tur.
- Maliyet ancak modelin fiyatı tanımlıysa dolar. 2026-09-16'da katalogdaki
  13 modelin **hiçbirinde** fiyat yok; `pricing_source = 2` (`NotDefined`) ve
  maliyet alanları `null` gelir — bu bilinçli bir davranıştır (sıfır yazmak
  yalan olurdu), eksik olan veridir (`HATA-S1-010`).
- `tool_invocations` tablosunda `get_order_status` için tam bir satır vardır.

**Doğrulama sorgusu**
```sql
SELECT r.id, r.agent_name, r.status, r.input_cost, r.output_cost, r.pricing_source,
       (SELECT count(*) FROM tracon.tool_invocations t
         WHERE t.run_id = r.id AND t.tool_name = 'get_order_status') AS tool_cagrisi
FROM tracon.runs r
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
- Maliyet alanları için `MT-CORE-044`'ün notu geçerlidir (`HATA-S1-010`).

**Doğrulama sorgusu**
```sql
SELECT r.id, count(t.*) AS tool_sayisi
FROM tracon.runs r
LEFT JOIN tracon.tool_invocations t ON t.run_id = r.id
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
-- 🚨 Oturum gecmisi SQL'den DOGRULANAMAZ. Iki sebep:
--   1) Sema farkli: sessions.id dis kimligin KENDISIDIR (external_id sutunu
--      yoktur) ve conversation_items session'a degil conversations'a baglidir.
--   2) sessions.state SIFRELIDIR: AddContentProtection() bir {"$apEnc",...}
--      zarfi yazar (samples/Tracon.Api/Program.cs:227).
-- Dogrulama API uzerinden yapilir:
--   curl -s "$APU/api/sessions/<id>" -H "$APB" | python3 -c \
--     "import sys,json;print(len(json.load(sys.stdin)['messages']))"
-- Oturumun kac kez yazildigini gormek icin (lost update denetimi):
SELECT id, version, updated_at FROM tracon.sessions WHERE id = '<id>';
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
-- 🚨 Oturum gecmisi SQL'den DOGRULANAMAZ. Iki sebep:
--   1) Sema farkli: sessions.id dis kimligin KENDISIDIR (external_id sutunu
--      yoktur) ve conversation_items session'a degil conversations'a baglidir.
--   2) sessions.state SIFRELIDIR: AddContentProtection() bir {"$apEnc",...}
--      zarfi yazar (samples/Tracon.Api/Program.cs:227).
-- Dogrulama API uzerinden yapilir:
--   curl -s "$APU/api/sessions/<id>" -H "$APB" | python3 -c \
--     "import sys,json;print(len(json.load(sys.stdin)['messages']))"
-- Oturumun kac kez yazildigini gormek icin (lost update denetimi):
SELECT id, version, updated_at FROM tracon.sessions WHERE id = '<id>';
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
-- 🚨 Oturum gecmisi SQL'den DOGRULANAMAZ. Iki sebep:
--   1) Sema farkli: sessions.id dis kimligin KENDISIDIR (external_id sutunu
--      yoktur) ve conversation_items session'a degil conversations'a baglidir.
--   2) sessions.state SIFRELIDIR: AddContentProtection() bir {"$apEnc",...}
--      zarfi yazar (samples/Tracon.Api/Program.cs:227).
-- Dogrulama API uzerinden yapilir:
--   curl -s "$APU/api/sessions/<id>" -H "$APB" | python3 -c \
--     "import sys,json;print(len(json.load(sys.stdin)['messages']))"
-- Oturumun kac kez yazildigini gormek icin (lost update denetimi):
SELECT id, version, updated_at FROM tracon.sessions WHERE id = '<id>';
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
-- 🚨 Oturum gecmisi SQL'den DOGRULANAMAZ. Iki sebep:
--   1) Sema farkli: sessions.id dis kimligin KENDISIDIR (external_id sutunu
--      yoktur) ve conversation_items session'a degil conversations'a baglidir.
--   2) sessions.state SIFRELIDIR: AddContentProtection() bir {"$apEnc",...}
--      zarfi yazar (samples/Tracon.Api/Program.cs:227).
-- Dogrulama API uzerinden yapilir:
--   curl -s "$APU/api/sessions/<id>" -H "$APB" | python3 -c \
--     "import sys,json;print(len(json.load(sys.stdin)['messages']))"
-- Oturumun kac kez yazildigini gormek icin (lost update denetimi):
SELECT id, version, updated_at FROM tracon.sessions WHERE id = '<id>';
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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

static void Dene(int deger)
{
    var services = new ServiceCollection();
    services.AddTracon().Configure(o => o.RunRecording.MaxPayloadLength = deger);
    try
    {
        _ = services.BuildServiceProvider().GetRequiredService<IOptions<TraconOptions>>().Value;
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
- `1048577: RED` ve mesaj `must be between 0 and 1048576` ifadesini taşır.
  🚨 Sevk edilen metin K-228'den beri **İngilizce**dir.
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

Negatif senaryo. Tracon işletim sistemi düzeyinde yalıtım **sağlamaz**.
`PlatformIsolationAcknowledged` bu sınırın okunduğunu bildiren bilinçli onaydır;
ayarlanmadan `Enabled` açılamaz.

**Ön koşul**
- MT-CORE-023 projesi hazır.

**Adımlar**
1. Onay olmadan script çalıştırmayı aç.
2. Onayla birlikte tekrar dene.

**Girilecek veri**
```bash
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

static void Dene(bool onay)
{
    var services = new ServiceCollection();
    services.AddTracon().Configure(o =>
    {
        o.Skills.Scripts.Enabled = true;
        o.Skills.Scripts.PlatformIsolationAcknowledged = onay;
    });
    try
    {
        _ = services.BuildServiceProvider().GetRequiredService<IOptions<TraconOptions>>().Value;
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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddTracon();
var o = services.BuildServiceProvider().GetRequiredService<IOptions<TraconOptions>>().Value;

Console.WriteLine($"MaxDepth      : {o.AgentGraph.MaxDepth}");
Console.WriteLine($"MaxTotalTokens: {o.AgentGraph.MaxTotalTokens}");
Console.WriteLine($"MaxTotalRuns  : {o.AgentGraph.MaxTotalRuns}");

o.AgentGraph.MaxTotalTokens = 0;
var b = o.AgentGraph.CreateBudget(TimeProvider.System);
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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddTracon();
var o = services.BuildServiceProvider().GetRequiredService<IOptions<TraconOptions>>().Value;

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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddTracon().Configure(o =>
{
    o.Pricing.Providers["echo"] = new Dictionary<string, ModelPriceOverride>
    {
        ["echo-1"] = new() { InputCostPerMillionTokens = -1m },
    };
});

try
{
    _ = services.BuildServiceProvider().GetRequiredService<IOptions<TraconOptions>>().Value;
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
cd ~/tracon-manuel/skillsiz
# appsettings.json cikti dizinine KOPYALANMALIDIR, yoksa ConfigurationBuilder
# dosyayi bulamaz. csproj'a ekle:
#   <ItemGroup><Content Include="appsettings.json"
#     CopyToOutputDirectory="PreserveNewest" /></ItemGroup>
cat > appsettings.json <<'EOF'
{
  "Tracon": {
    "Pricing": {
      "Currency": "USD",
      "//": "ANAHTAR ADLARI: Input / Output / CachedInput. C# ozellik adi (InputCostPerMillionTokens) DEGIL.",
      "echo": { "echo-1": { "Input": 0.25, "Output": 1.0 } },
      "Voice": { "elevenlabs": { "tts-1": { "PerMillionCharacters": 30.0 } } }
    }
  }
}
EOF

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
var services = new ServiceCollection();
// 🚨 Bolum AddTracon'A VERILMELIDIR: elle (AOT) baglayici oradan kosar.
// AddTracon() + Configure<TraconOptions>(...) yansima binder'ini kullanir ve
// Providers sozlugunu DOLDURAMAZ (saglayici sayisi 0 gelir).
services.AddTracon(config.GetSection(TraconOptions.SectionName));

var o = services.BuildServiceProvider().GetRequiredService<IOptions<TraconOptions>>().Value;
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

Tasarım kuralı #1'in çekirdek testidir. `Tracon.Testing` paketinin bellek
içi host'u hiçbir veritabanına ve hiçbir ağa gitmez.

**Ön koşul**
- MT-CORE-023 projesi hazır. `Tracon.Testing` paketi eklenir.

**Adımlar**
1. `Tracon.Testing` paketini ekle.
2. `FakeModelProvider` ile bir host kur.
3. Bir agent çalıştır ve kaydı oku.

**Girilecek veri**
```bash
cd ~/tracon-manuel/skillsiz
SURUM=$(ls ~/tracon-local-feed/Tracon.Testing.*.nupkg | sed 's#.*Tracon.Testing\.##;s#\.nupkg##')
dotnet add package Tracon.Testing --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Tracon.Testing;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await TraconTestHost.StartAsync(o =>
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

> Not: `TraconTestHost` kurulum imzası koşumdan önce doğrulanır:
> `grep -n "public static" src/Tracon.Testing/TraconTestHost.cs`.
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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon.Testing;

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
cd ~/tracon-manuel/skillsiz
cat > Program.cs <<'EOF'
using Tracon;

var ids = new List<Guid>();
for (var i = 0; i < 5; i++) { ids.Add(TraconId.NewId()); Thread.Sleep(2); }

var sirali = ids.Select(x => x.ToString()).SequenceEqual(ids.Select(x => x.ToString()).Order(StringComparer.Ordinal));
Console.WriteLine("sirali: " + sirali);

var damga = TraconId.GetTimestamp(ids[0]);
Console.WriteLine("damga farki (sn): " + (DateTimeOffset.UtcNow - damga).TotalSeconds.ToString("F1"));

try { TraconId.GetTimestamp(Guid.NewGuid()); Console.WriteLine("🚨 v4 KABUL EDILDI"); }
catch (ArgumentException ex) { Console.WriteLine("beklenen red: " + ex.Message); }
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `sirali: True`.
- Damga farkı **1 saniyeden küçüktür**.
- `Guid.NewGuid()` (sürüm 4) reddedilir ve mesaj `The identifier is not a UUID version 7 value.` ifadesini taşır. 🚨 Sevk edilen metin K-228'den beri **İngilizce**dir.
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
#   cd samples/Tracon.Api && dotnet run
# ve ayni komutu tekrarla.
```

**Beklenen sonuç**
- Yeniden başlatma sonrası toplam sayı **aynıdır**.
- Kod kaynaklı agent sayısı aynıdır.
- `manuel-surum` hâlâ vardır ve `version` değeri **2**'dir.
- Açılış log'unda migration'lar yeniden uygulanmaz (şema zaten günceldir).

**Doğrulama sorgusu**
```sql
SELECT name, version, origin FROM tracon.agent_definitions ORDER BY name;
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
- `"musteri"` adlı zorunlu (`required: true`, **`kind: "Text"`** — `kind` zorunludur, `AgentParameterKind`: `Text·Number·Boolean`), varsayılansız bir parametre
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
  **Ölçülebilir kısım budur** ve ön koşul gerektirmez: kaçış bozuk olsaydı
  sağlayıcı isteği hatalı JSON olur ve koşu düşerdi.
- 🚨 Kaçırılmış metni **görmek** bir OTLP trace collector ister. İstem ve yanıt
  metinleri **span'lere** yazılır (`MT-CORE-063`); `GET /api/runs/{id}` onları
  taşımaz ve `run_events.payload` `Tracon__Observability__RecordSensitiveData=true`
  ile bile `NULL` gelir (2026-09-16'da ölçüldü). Collector yoksa bu adım
  atlanır ve gerekçesi yazılır.

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

Sınır senaryosu — `TraconOptions.MaxParameterValueLength` (varsayılan 4096
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
  çağrılmaz. 2026-08-22'de `samples/Tracon.Api`'ye karşı canlı doğrulandı
  (bkz. Faz 86, "Doğrulama komutları").

---

### MT-CORE-087 — Özel `IAgentSource` ajanı `Custom` origin ile listelenir, düzenleme formu açılmaz (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
> 🚨 **Kod donmuş bir turda `samples/` değiştirilemez.** Bunun yerine repo
> DIŞINDA bir tüketici host'u kurulur: `Tracon.AspNetCore` + `Tracon.UI` +
> `Tracon.Testing` paketleri, `Tracon.Samples.CustomAgentSource`'un üç `.cs`
> dosyası kopyalanır (paketlenmiş sürümü yoktur), bir model sağlayıcısı
> kaydedilir (`FakeModelProvider("echo")` — yalın host'ta `echo` yoktur) ve
> `app.MapTracon("/tracon", o => o.AuthToken = "...")` çağrılır
> (🚨 `AuthToken` yapılandırmadan otomatik okunmaz). 2026-09-16'da bu yolla
> koşuldu.
- `samples/Tracon.Api/Program.cs`'e geçici olarak
  `builder.AddTracon().Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = "<dizin>" })`
  ve `.AddAgentSource<JsonFileAgentSource>()` eklenmiş, `<dizin>` altında
  `{"name":"greeter","instructions":"Reply briefly.","model":{"provider":"echo","model":"echo-1"}}`
  içerikli bir `greeter.json` dosyası bulunan bir `samples/Tracon.Api` koşumu.
  (`samples/Tracon.Samples.CustomAgentSource`'un `PackageReference`'ı
  local feed'den çözülür — bkz. `samples/NuGet.config`.)

**Adımlar**
1. `GET /tracon/api/agents` çağır; `greeter` adlı agent'ı bul.
2. Konsolda agent listesini aç, `greeter`'ı tıkla.

**Girilecek veri**
```bash
curl -s "$APU/api/agents" -H "$APB" | python3 -m json.tool | grep -A 3 '"name": "greeter"'
```

**Beklenen sonuç**
- Adım 1: `origin: "Custom"`, `sourceName: "json-file"`.
  🚨 `isEditable` **liste ucunda yoktur**; `GET /api/agents/{ad}` detay
  yanıtının üst seviyesindedir. Listeyi `grep`'leyen bir komut onu hiç
  göremez.
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
- Koşu tamamlanır. `run` kaydında `modelId` ve `modelProvider` alanları
  (bu adlarla; `model`/`provider` değil) **boş değildir** — kaynağın kendi `ListAsync`'inden gelen dondurulmuş descriptor
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
- Ön koşul MT-CORE-087'deki gibi, ama kaynağın `ListAsync`'i **gerçekten
  istisna fırlatmalıdır**.
  🚨 **Var olmayan bir dizin YETMEZ:** `JsonFileAgentSource.ReadAllAsync`
  (`:142-145`) `if (!Directory.Exists(...)) { yield break; }` ile bunu ele
  alır ve kaynak sessizce **boş** döner. Bozmanın çalışan yolu: dizine
  ayrıştırılamayan bir `.json` bırakmak (ad sıralamada geçerli dosyalardan
  önce gelmeli, ör. `aaa-broken.json`).
- Yerleşik kaynağın hâlâ göründüğünü kanıtlamak için host'ta en az bir kod
  agent'ı kayıtlı olmalıdır.

**Adımlar**
1. `GET /tracon/api/agents` çağır.

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
1. `GET /tracon/api/diagnostics` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool | grep -A 15 agentSources
```

**Beklenen sonuç**
- `agentSources` dizisi `[{"name":"code","priority":0,...},
  {"name":"database","priority":100,...},
  {"name":"json-file","priority":101,...}]` sırasıyla üç girdi taşır.
  🚨 Önemli olan **sıra**dır, sayının kendisi değil: örnek kaynak önceliğini
  `AgentSourcePriority.Database + 1` olarak beyan eder
  (`JsonFileAgentSource.cs:91`), yani 101.

---

### MT-CORE-093 — Aynı `Name`'e sahip iki kaynak host'u başlatmaz (Faz 101)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 101 |
| **İlgili karar** | — |

**Ön koşul**
- Aynı `Name`'i döndüren **iki farklı TİP** kayıtlı olmalıdır.
  🚨 Aynı tipi iki kez kaydetmek **işe yaramaz**: `AddAgentSource<T>()`
  `TryAddEnumerable` kullanır (`TraconBuilder.cs:142`) ve tipi tekilleştirir;
  host sorunsuz açılır. İkinci bir sınıf yazıp `Name`'ini birincininkiyle
  aynı döndür.

**Adımlar**
1. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet run --no-build -c Release --urls http://localhost:5081
```

**Beklenen sonuç**
- Uygulama **başlamaz**; konsolda `TraconAgentSourceException` ve iki
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
  geçici eklenmiş `samples/Tracon.Api` koşumu.

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
  `Tracon.Testing.Contracts.AgentSources.TenantAwareAgentSourceContract`
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
> (`tests/Tracon.Core.UnitTests/Catalog/BuiltInAgentSourceContractTests.cs`,
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
- `samples/Tracon.Samples.CustomAgentSource.Tests` projesi local feed'den
  restore edilebilir durumda (`dotnet pack Tracon.src.slnf -c Release`
  önceden koşulmuş).

**Adımlar**
1. `dotnet test samples/Tracon.Samples.CustomAgentSource.Tests -c Release` çalıştır.

**Girilecek veri**
```bash
dotnet test samples/Tracon.Samples.CustomAgentSource.Tests -c Release --no-build
```

**Beklenen sonuç**
- 15/15 test geçer: `AgentSourceContract` ailesinin 12 senaryosu,
  `JsonFileAgentSourceRunTests`'in generic (`AddAgentSource<T>()`) ve factory
  (`AddAgentSource(factory)`) kayıt yollarıyla yaptığı iki gerçek `run`, ve
  kapsam testi. `TryAddEnumerable` sample kodunda hiç geçmez
  (`grep -c TryAddEnumerable samples/Tracon.Samples.CustomAgentSource*/*.cs` → `0`).

> **Otomatik karşılığı:** `DefinitionReadFromJsonTests` — JSON okuyan bir kaynak koleksiyonu
> yazmasa da tanım boş koleksiyonla derlenir (2026-10-22: `ExtensionNames` bu case'in beşini düşürdü).

---

### MT-CORE-097 — Complex tool sonucu canonical JSON ve output limiti taşır (Faz 102)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |

**Ön koşul**
- `samples/Tracon.Samples.CustomTool.Tests` local feed ile restore edilir.

**Adımlar**
1. Sample testlerini çalıştır.

**Girilecek veri**
```bash
dotnet test samples/Tracon.Samples.CustomTool.Tests -c Release --no-build
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
- Varsayılan kurulum `TraconException` ile başlangıçta durur.
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
- 🚨 **Asıl iddia:** iki yüzeyde de bağlantı dizesi ve özgün exception mesajı
  **görünmez**.
- `GET /api/runs/{id}/tools` → `error` alanı tam olarak
  `Tool failed with <ExceptionType>.` taşır
  (`src/Tracon.Core/Tools/ToolFailureText.cs:10`).
- SSE `functionResult.result` **farklı** bir metin taşır
  (`Error: Function failed.`) — bu Microsoft.Extensions.AI'ın kendi genel
  metnidir, Tracon'un `ToolFailureText`'inden geçmez. Aynı metni bekleme;
  aranacak şey sızıntının **olmaması**dır.

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
1. `samples/Tracon.Api`'yi PostgreSQL ile ayağa kaldır (`ap-pg` container).
2. `POST /tracon/api/agents/support/run` ile gerçek bir mesaj gönder.
3. `GET /tracon/api/runs?agentName=support&limit=1` ile sonucu doğrula.

**Beklenen sonuç**
- SSE akışı `event: update` (metin + usage) ve `event: done` ile biter.
- `/api/runs` listesinde `status: "Completed"`, gerçek `usage` (input/output token) ile yeni bir kayıt görünür.

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
- Sync yol `TraconCompilationException` fırlatır ("does not resolve shared instructions blocks. Use CompileAsync()").
- Async yol bloğun metnini kendi talimatının önüne ekler.
- Otomatik koşuldu: `SharedInstructionsTests.Synchronous_Compile_refuses_a_definition_that_references_a_block`,
  `SharedInstructionsTests.Blocks_text_is_prepended_to_the_agents_own_instructions`.

---

### MT-CORE-106 — `InMemoryRunStore` ayrıştırması sonrası ağaç toplamları ve kiracı yalıtımı doğru kalır (Faz 108)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 108 |
| **İlgili karar** | — |

`InMemoryRunStore` beş sorumluluk dosyasına (`Lifecycle`/`Events`/`Queries`/
`Statistics`/`Analytics`) bölündü; state tek dosyada kaldı. Bu case bölünmenin
davranışı bozmadığını gerçek bir `run` ile kanıtlar.

**Ön koşul**
- Bağlantı dizesi verilmez — `samples/Tracon.Embedded` varsayılan olarak
  bellek içi kalıcılık kullanır (`GET /tracon/api/diagnostics` →
  `"persistenceProvider": "InMemory"`).

**Adımlar**
1. `dotnet run --project samples/Tracon.Embedded --urls http://localhost:5082`
2. İki farklı kiracı için arka plan iş kuyruğu üzerinden birer `run` üret.
3. Her kiracının kendi kimliğiyle `run` listesini ve istatistik özetini oku.

**Girilecek veri**
```bash
curl -s -X POST http://localhost:5082/jobs -H 'Content-Type: application/json' \
  -d '{"tenantId":"acme","userId":"user-42","message":"What is my account balance?"}'
curl -s -X POST http://localhost:5082/jobs -H 'Content-Type: application/json' \
  -d '{"tenantId":"globex","userId":"user-7","message":"Summarize last invoice"}'

curl -s -H 'X-Host-Tenant: acme' http://localhost:5082/tracon/api/runs
curl -s -H 'X-Host-Tenant: globex' http://localhost:5082/tracon/api/runs
curl -s -H 'X-Host-Tenant: acme' http://localhost:5082/tracon/api/stats
```

**Beklenen sonuç**
- Her `run` `Completed` durumunda döner, `usage` ve `treeUsage` doludur
  (`InMemoryRunStore.Queries.cs`'teki `WithTreeTotals`).
- Kiracı A'nın `run` listesi yalnız kendi `run`'ını içerir, kiracı B'ninkini
  içermez (`QueryRunsAsync`'in kiracı süzgeci).
- `GET /tracon/api/stats` `totalRuns: 1`, `byAgent`/`byModel`/`byUser`
  kırılımlarını doğru sayar (`GetStatisticsAsync`, `InMemoryRunStore.Statistics.cs`).

> **Otomatik karşılığı:** `InMemoryRunStoreContractTests`/`InMemoryToolInvocationContractTests`
> (`tests/Tracon.Core.UnitTests/Contracts/InMemoryStoreContractTests.cs`) —
> aynı sözleşme paketi bellek içi, PostgreSQL, SQL Server ve SQLite
> uygulamalarının tümünde koşar. Trim'in event/tool invocation kayıtlarını da
> sildiğini ve bir `IRunScoreStore` hatasının `GetStatisticsAsync`'i sessizce
> yutmadığını `InMemoryRunStoreStructureTests`
> (`tests/Tracon.Core.UnitTests/Storage/InMemoryRunStoreStructureTests.cs`)
> doğrular.

---

### MT-CORE-107 — `AddScopedTool` ile kaydedilmiş bir tool her çağrıda taze DI kapsamı alır (Faz 127)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 127 |
| **İlgili karar** | K-218 |

`AddScopedTool`, MAF'ın `AIFunctionArguments.Services` için boş sağlayıcı
geçirdiği K-218 kısıtını, çağrı başına açılan gerçek bir DI kapsamıyla kapatır.

**Ön koşul**
- `samples/Tracon.Api` çalışıyor; örnek uygulamaya `AddScopedTool` ile
  kayıtlı, `IServiceScopeFactory`'den çözülen (ör. `DbContext` benzeri) bir
  tool eklenmiş olmalı, ya da `dotnet test` ile
  `ScopedToolLifetimeTests` fonksiyonel testi elle izlenir.

**Adımlar**
1. Aynı agent'a art arda iki mesaj gönder; her mesaj `AddScopedTool` ile
   kayıtlı tool'u çağırsın.
2. Tool gövdesinin `AIFunctionArguments.Services`'ten çözdüğü örneğin
   kimliğini (ör. bir `Guid`) çıktıya yaz.

**Beklenen sonuç**
- İki çağrı FARKLI kimlikler görür — kapsam çağrı başınadır, paylaşılmaz.
- Her iki `run` da `Completed` durumunda biter; tool hatası veya sızıntı
  görünmez.

> **Otomatik karşılığı:** `ScopedToolLifetimeTests`
> (`tests/Tracon.AspNetCore.FunctionalTests/ScopedToolLifetimeTests.cs`) —
> gerçek host üzerinden art arda ve eşzamanlı çağrıların ayrı kapsam aldığını
> ve kapsamın çağrı bitince kapandığını kanıtlar. Wrapper'ın kendi mekaniği
> (`ScopedToolTests`, `tests/Tracon.Core.UnitTests/Tools/ScopedToolTests.cs`)
> birim seviyesinde ayrıca koşar.

---

### MT-CORE-108 — Örnek metot tool taraması, `AddScopedTool`'u adıyla önerir (Faz 127)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 127 |
| **İlgili karar** | K-347 |

K-347 kapanmadı: `[TraconTool]` işaretli bir örnek metot yine tarama
anında reddedilir. Bu case yalnız ret metninin güncellendiğini doğrular.

**Adımlar**
1. `[TraconTool]` ile işaretli, `static` olmayan bir örnek metoda sahip
   bir tipi `AddToolsFrom<T>()` ile kaydetmeyi dene.

**Beklenen sonuç**
- Başlangıç `TraconException` ile durur.
- Hata metni `AddScopedTool`'u adıyla anar (yalnız "static yap" değil, kalıcı
  bir bağımlılığın per-call çözülmesi gerekiyorsa hangi API'nin kullanılacağını
  söyler).

---

### MT-CORE-109 — `[Range]` üretilen şemaya `minimum`/`maximum` yazar (Faz 130)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 130 |

**Ön koşul**
- `[TraconTool]` işaretli bir metotta `[Range(1, 100)] int count` parametresi.

**Adımlar**
1. `dotnet build samples/Tracon.Api -p:EmitCompilerGeneratedFiles=true`.
2. Üretilen wrapper dosyasındaki JSON şemayı oku.

**Beklenen sonuç**
- `"count"` düğümü `"minimum":1,"maximum":100` taşır.
- Derleme sıfır uyarıyla biter.

> **Otomatik karşılığı:** `ToolSchemaConstraintTests.A_Range_attribute_on_an_integer_parameter_produces_minimum_and_maximum`
> (`tests/Tracon.Generators.UnitTests/ToolSchemaConstraintTests.cs`).

---

### MT-CORE-110 — `[MinLength]` bir `string` parametresinde `minLength`, bir dizi parametresinde `minItems` yazar (Faz 130)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 130 |

Dizi kısıtı diziye uygulanır, elemana değil — bu case'in tek amacı bu ayrımı
kanıtlamaktır (130.3).

**Ön koşul**
- `[TraconTool]` işaretli bir metotta `[MinLength(3)] string name` VE
  `[MinLength(2)] string[] tags` parametreleri.

**Adımlar**
1. `dotnet build samples/Tracon.Api -p:EmitCompilerGeneratedFiles=true`.
2. Üretilen şemada `name` ve `tags` düğümlerini oku.

**Beklenen sonuç**
- `name` düğümü `"minLength":3` taşır.
- `tags` düğümü `"minItems":2` taşır — `items` düğümünde `minLength` **yoktur**.

> **Otomatik karşılığı:**
> `ToolSchemaConstraintTests.A_MinLength_attribute_on_a_string_parameter_produces_minLength`
> ve `...An_array_parameter_produces_minItems_not_minLength`.

---

### MT-CORE-111 — Uyumsuz kısıt `TRC0010` üretir, derleme başarılı kalır (Faz 130)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 130 |

**Ön koşul**
- `[TraconTool]` işaretli bir metotta `[Range(1, 10)] string s` parametresi
  (tipe uymayan kısıt).

**Adımlar**
1. `dotnet build samples/Tracon.Api`.

**Beklenen sonuç**
- Derleme `TRC0010` UYARISI verir, **hata vermez** — build başarılı biter.
- Üretilen şemada `s` düğümünde `minimum`/`maximum` **yoktur**.

> **Otomatik karşılığı:**
> `ToolDiagnosticTests.A_Range_attribute_on_a_string_parameter_is_reported_and_omitted_from_the_schema`.

---

### MT-CORE-112 — Kısıtlı bir tool ile gerçek `run` başarıyla biter (Faz 130)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 130 |

Bu case üretilen şemanın **paket** sınırını gerçekten geçtiğini kanıtlar —
birim testi yalnız şema **metnini** kanıtlar, `analyzers/dotnet/cs/` içindeki
DLL'in gerçekten paketlenip bir `PackageReference` tüketicisine ulaştığını
değil. `samples/Tracon.Api` bunu KANITLAYAMAZ: o proje `Tracon`'e
`ProjectReference` ile bağlıdır (`ConsumerRunTests`'in kendi belgesi, K-166
emsali) — bu yüzden case, `samples/Tracon.Api` yerine yerel NuGet feed
izleğini (İzlek A) kullanan `Tracon.Package.Tests`'e taşındı (plandan
sapma, faz dokümanına not edildi).

**Adımlar**
1. `dotnet test tests/Tracon.Package.Tests --filter-method "*Constrained*"`.

**Beklenen sonuç**
- `run` `Completed` durumunda biter; tool hatası veya sızıntı görünmez.
- Üretilen şemada `"minimum":1,"maximum":10` gerçekten paketlenmiş DLL'den
  çıkar (dış tüketici projesinin stdout'unda görünür).

> **Otomatik karşılığı:**
> `ConstrainedToolPackageTests.Package_consumer_gets_the_constrained_schema_and_the_run_completes`
> (`tests/Tracon.Package.Tests/ConstrainedToolPackageTests.cs`) —
> `ConsumerRunTests`'in aynı deseni (paketle → yerel feed → dış tüketici
> projesi yaz → derle → çalıştır), kısıtlı bir parametreyle.

---

### MT-CORE-113 — `tr-TR` yerelinde ondalık ayırıcı `.` kalır (Faz 130)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 130 |

**Ön koşul**
- `[TraconTool]` işaretli bir metotta `[Range(1.5, 2.5)] double ratio`
  parametresi.

**Adımlar**
1. `LANG=tr_TR.UTF-8 dotnet build tests/Tracon.Generators.UnitTests`.
2. `dotnet build samples/Tracon.Api -p:EmitCompilerGeneratedFiles=true`
   ile üretilen şemayı oku.

**Beklenen sonuç**
- `"minimum":1.5,"maximum":2.5` — `1,5` **değil**.
- Derleme sıfır uyarıyla biter.

> **Otomatik karşılığı:**
> `ToolSchemaConstraintTests.A_decimal_Range_keeps_the_invariant_decimal_separator_under_a_comma_decimal_culture`
> (`CultureInfo.CurrentCulture` `tr-TR`'ye çevrilerek koşulur).

---

### MT-CORE-114 — Aynı girdi iki derlemede bit düzeyinde aynı şema üretir (Faz 130)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 130 |

**Adımlar**
1. `samples/Tracon.Api`'yi temiz derle (`obj`/`bin` temizlenmiş).
2. Üretilen wrapper dosyasını kopyala.
3. Tekrar temiz derle, aynı dosyayı tekrar kopyala.

**Beklenen sonuç**
- İki kopya bayt bayt aynıdır (`diff` boş döner).

> **Otomatik karşılığı:**
> `ToolSchemaDeterminismTests.The_same_source_produces_byte_identical_wrapper_text_across_two_independent_runs`.

---

### MT-CORE-115 — Nesne parametresi nested JSON Schema düğümü üretir (Faz 135)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 135 |

**Ön koşul**
- `record Rubric(string Name, int Weight)` parametreli bir tool; attribute'lar
  positional parametrenin ÜZERİNE doğrudan konur (`[property: ...]` DEĞİL —
  aksi hâlde generator onları okumaz).
- Tool'un context'i `[JsonSerializable(typeof(Rubric))]` bildiriyor.

**Adımlar**
1. `dotnet build samples/Tracon.Api -p:EmitCompilerGeneratedFiles=true`.
2. Üretilen wrapper dosyasındaki JSON şemayı oku.

**Beklenen sonuç**
- Parametre düğümü `"type":"object","properties":{...},"required":[...],
  "additionalProperties":false` taşır.
- Derleme sıfır uyarıyla biter.

> **Otomatik karşılığı:** `ToolSchemaObjectTests.An_object_parameter_produces_a_nested_object_schema_node`
> (`tests/Tracon.Generators.UnitTests/ToolSchemaObjectTests.cs`).

---

### MT-CORE-116 — Nesne dizisi parametresi `items` düğümünde nesne üretir (Faz 135)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 135 |

**Ön koşul**
- `IReadOnlyList<Rubric>` parametreli bir tool.

**Adımlar**
1. `dotnet build samples/Tracon.Api -p:EmitCompilerGeneratedFiles=true`.
2. Üretilen şemayı oku.

**Beklenen sonuç**
- Parametre düğümü `"type":"array"` taşır; `items` alt düğümü nesnenin tam
  şemasıdır (`properties`/`required`/`additionalProperties:false`).

> **Otomatik karşılığı:** `ToolSchemaObjectTests.An_object_array_parameter_produces_an_array_of_object_schema_nodes`.

---

### MT-CORE-117 — Nesne üyesindeki `[Range]` üye düğümüne `minimum`/`maximum` yazar (Faz 135)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 135 |

Faz 130'un kısıt yazımı üye seviyesinde de aynen çalışır (135.2) — bu case
yalnız bunu kanıtlar, yeni bir kısıt türü eklemez.

**Ön koşul**
- `record Rubric(string Name, [Range(1, 5)] int Weight)` parametreli bir tool.

**Adımlar**
1. `dotnet build samples/Tracon.Api -p:EmitCompilerGeneratedFiles=true`.
2. Üretilen şemada `rubric.properties.Weight` düğümünü oku.

**Beklenen sonuç**
- `Weight` düğümü `"minimum":1,"maximum":5` taşır.

> **Otomatik karşılığı:** `ToolSchemaObjectTests.A_Range_attribute_on_an_object_member_produces_minimum_and_maximum_on_the_member_node`.

---

### MT-CORE-118 — Context'te bildirilmeyen nesne tipi `TRC0011` ile derlemeyi durdurur (Faz 135)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 135 |

K-615'in kapsamı bu fazda büyüdü: context artık yalnız complex SONUÇ tipini
değil, nesne PARAMETRESİNİN grafındaki her tipi de bildirmek zorunda —
bildirmezse derleme durur, sessizce reflection'a düşmez.

**Ön koşul**
- `record Outer(Inner Nested)` / `record Inner(string X)`; tool'un context'i
  `Outer`'ı bildiriyor, `Inner`'ı bildirmiyor.

**Adımlar**
1. `dotnet build samples/Tracon.Api`.

**Beklenen sonuç**
- Derleme `TRC0011` HATASI verir (uyarı değil) — build **başarısız** biter.
- Hata metni eksik tipin adını (`Inner`) taşır.
- Context hiçbir tip bildirmiyorsa (tamamen eksikse), graftaki HER tip için
  ayrı bir `TRC0011` üretilir — tüketici hepsini tek derlemede görür.

> **Otomatik karşılığı:**
> `ToolObjectGraphTests.A_JsonSerializerContext_missing_only_a_nested_type_reports_TRC0011_for_that_type_alone`
> ve `...An_object_parameter_with_no_JsonSerializerContext_reports_TRC0011_for_every_type_in_its_graph`.

---

### MT-CORE-119 — 3'ü aşan nesne derinliği `TRC0012` ile derlemeyi durdurur (Faz 135)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 135 |

**Ön koşul**
- Beş seviyeli bir `record` zinciri (`Level0`→`Level1`→…→`Level4`); kök 0,
  `Level4` derinlik 4 (limit 3'ü aşıyor).

**Adımlar**
1. `dotnet build samples/Tracon.Api`.

**Beklenen sonuç**
- Derleme `TRC0012` HATASI verir; hata metni yolu gösterir
  (`Level0 → Level1 → Level2 → Level3 → Level4`).
- Generator **asılmaz**, derleme normal sürede biter.

> **Otomatik karşılığı:** `ToolObjectGraphTests.A_graph_four_levels_deep_produces_TRC0012_and_blocks_generation`.

---

### MT-CORE-120 — Bir cycle `TRC0012` üretir, generator asılmaz (Faz 135)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 135 |

**Ön koşul**
- `record NodeA(NodeB Next)` / `record NodeB(NodeA Next)` — `A → B → A`.

**Adımlar**
1. `dotnet build samples/Tracon.Api`.

**Beklenen sonuç**
- Derleme makul sürede (saniyeler) biter — asılı KALMAZ.
- `TRC0012` hatası yolu `NodeA → NodeB → NodeA` olarak gösterir.

> **Otomatik karşılığı:**
> `ToolObjectGraphTests.A_cycle_produces_TRC0012_naming_the_cyclic_path_and_the_generator_returns_promptly`
> (10 saniyelik bir üst sınırla `Task.WaitAsync` koşulur).

---

### MT-CORE-121 — Nesne parametreli bir tool gerçek `run`'da modelden nesne argümanı alır (Faz 135)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 135 |

Bu case'in koşumu belgeye yazılmıştır — 2026-09-02, gerçek OpenAI çağrısı
(`support` agent'ı, `gpt-5.4-mini`). `samples/Tracon.Api/OrderTools.cs`
içindeki `estimate_shipping_cost` tool'u (parametre: `ShippingAddress` — public
record, tek public kurucu) `support` agent'ının tool listesine eklendi.

**Ön koşul**
- `samples/Tracon.Api` çalışıyor (`http://localhost:5080`), OpenAI `ApiKey`
  `dotnet user-secrets` ile tanımlı.
- `support` agent'ının `ToolNames`'i `estimate_shipping_cost`'u içeriyor.

**Adımlar**
```bash
curl -s -N -X POST "$APU/api/agents/support/run" -H "$APB" \
  -H "Content-Type: application/json" \
  -d '{"message":"I need a shipping estimate for an order going to 42 Rose Ave, Springfield, postal code 62704."}'
```

**Beklenen sonuç**
- SSE akışında bir `functionCall` olayı görünür:
  `"name":"estimate_shipping_cost"`, `"arguments":{"address":{"Street":"42
  Rose Ave","City":"Springfield","PostalCode":"62704"}}` — model **nesne**
  argümanı gönderiyor, düz `string` değil.
- Aynı akışta bir `functionResult` olayı görünür:
  `"result":"Estimated shipping to Springfield, 62704: $12.50 (3-5 business
  days)."` — tool gövdesi tipli `ShippingAddress` nesnesini aldı, `Deserialize`
  reflection'a düşmedi.
- Run `finishReason":"stop"` ile biter; hata yok.

**Gerçekleşen sonuç (2026-09-02, gerçek koşum):** Yukarıdaki tam olarak
gözlendi — `functionCall`/`functionResult` çiftleri stdout'ta doğrulandı.

---

### MT-CORE-122 — Nesne parametreli tool, paketlenmiş dış tüketicide `PublishAot` altında uyarısız derlenir ve çalışır (Faz 135)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 135 |

`samples/Tracon.Api` bunu KANITLAYAMAZ (o proje `Tracon`'e
`ProjectReference` ile bağlıdır, MT-CORE-112'nin kendi notu) — case
`Tracon.Package.Tests`'e taşındı.

**Adımlar**
1. `dotnet test tests/Tracon.Package.Tests --filter-method "*An_object_parameter_tool_publishes_under_Native_AOT*"`.

**Beklenen sonuç**
- Dış tüketici projesi (`PackageReference`, `PublishAot=true`) uyarısız
  yayımlanır — `IL2026`/`IL3050` sıfır.
- Yayımlanan, trim'lenmiş yürütülebilir dosya çalıştırılınca nesne
  argümanını (`JsonElement` → `Rubric`) `JsonSerializer.Deserialize(JsonElement,
  JsonTypeInfo)` ile reflection'sız çözer ve doğru sonucu üretir.

> **Otomatik karşılığı:**
> `ObjectToolAotPackageTests.An_object_parameter_tool_publishes_under_Native_AOT_without_a_trim_warning_and_runs`
> (`tests/Tracon.Package.Tests/ObjectToolAotPackageTests.cs`) — koşuldu,
> 2026-09-02, `osx-arm64`, 31 saniyede geçti.

---

### MT-CORE-123 — `harness.loop` verilmeyen bir agent bugünkü davranışını birebir korur (Faz 151)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 151 |

**Ön koşul:** `samples/Tracon.Api` ayakta; harness'li, döngüsüz bir agent
tanımı kayıtlı.

**Adımlar**
1. Tanıma `harness.loop` **eklemeden** bir `run` at:
   ```bash
   curl -s -X POST http://localhost:5081/tracon/api/agents/<ad>/run \
     -H 'content-type: application/json' -d '{"message":"merhaba"}' | jq '.runId'
   ```
2. `curl -s "http://localhost:5081/tracon/api/runs/<runId>/events" | jq -r '.[].type' | sort -u`

**Beklenen sonuç**
- Yanıt bugünküyle aynıdır; model **bir kez** çağrılır.
- Olay akışında `LoopIterationCompleted` **yoktur**.

> **Otomatik karşılığı:** `HarnessLoopTests.A_harness_without_loop_settings_writes_no_iteration_event`
> ve `LoopCompilationTests.A_harness_without_loop_settings_gets_no_loop_at_all`.

---

### MT-CORE-124 — `completionMarker` ölçütü marker gelene kadar döner ve her iterasyonu kaydeder (Faz 151)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 151 |

**Ön koşul:** Aynı ortam. Agent talimatı modele "iş bitince `ALL DONE` yaz"
demelidir; aksi hâlde ölçüt hiç karşılanmaz ve tavana kadar döner.

**Adımlar**
1. Tanıma ekle ve kaydet:
   ```json
   "harness": { "loop": { "criteria": [ { "kind": "completionMarker", "marker": "ALL DONE" } ], "maxIterations": 4 } }
   ```
2. Bir `run` at, sonra olayları oku:
   ```bash
   curl -s "http://localhost:5081/tracon/api/runs/<runId>/events" \
     | jq '[.[] | select(.type=="LoopIterationCompleted")] | length'
   curl -s "http://localhost:5081/tracon/api/runs/<runId>/events" \
     | jq -r '.[] | select(.type=="LoopIterationCompleted") | .payload'
   ```

**Beklenen sonuç**
- Model `ALL DONE` yazana kadar tekrar çağrılır, en çok 4 kez.
- Her iterasyon için bir `LoopIterationCompleted` olayı vardır.
- Son olayın payload'ında `continued: false`, `continuedBy: null` ve
  `ceilingReached: false`; önceki olaylarda `continuedBy: "completionMarker"`.
- Hiçbir payload ölçütün modele yazdığı geri bildirim **metnini** taşımaz.

> **Otomatik karşılığı:** `HarnessLoopTests.A_marker_criterion_loops_until_the_marker_and_records_every_iteration`.

---

### MT-CORE-125 — Ulaşılamayan bir ölçüt Tracon'in kendi tavanında durur (Faz 151)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 151 |

**Ön koşul:** Aynı ortam. 🚨 Bu case **on model çağrısı** üretir; gerçek bir
sağlayıcıya karşı koşarken ucuz bir model seç.

**Adımlar**
1. `maxIterations` **vermeden**, modelin yazmayacağı bir marker ver:
   ```json
   "harness": { "loop": { "criteria": [ { "kind": "completionMarker", "marker": "ZZZ-NEVER" } ] } }
   ```
2. Bir `run` at ve tamamlanmasını bekle.

**Beklenen sonuç**
- `run` **tamamlanır** (`Completed`), sınırsız çalışmaz.
- Model **10 kez** çağrılır ama `LoopIterationCompleted` olay sayısı **9**'dur:
  tavana ulaşan tur değerlendirilmez, çünkü karar verecek bir şey kalmamıştır.
- Son olayın payload'ında `ceilingReached: true` vardır — döngüyü bitirenin
  karşılanan bir ölçüt değil tavan olduğu kayıttan okunur.

> **Otomatik karşılığı:** `HarnessLoopTests.An_unreachable_criterion_stops_at_the_Tracon_ceiling_instead_of_running_on`.

---

### MT-CORE-126 — Bilinmeyen bir ölçüt `kind`'i kayıtta `400` ile reddedilir (Faz 151)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 151 |

**Adımlar**
1. `POST /api/agents` gövdesine
   `"harness": { "loop": { "criteria": [ { "kind": "yok-boyle-bir-sey" } ] } }` koy.
2. Aynı gövdeyi `POST /api/agents/validate` ile de gönder.

**Beklenen sonuç**
- İkisi de `400`; `detail` bilinmeyen `kind`'i ve `AddLoopEvaluator` çağrısını
  adlandırır. Tanım **kaydedilmez**.
- Aynı şey `marker`'sız `completionMarker` ve `judgeCriteria`'sız `aiJudge`
  için de olur.

> **Otomatik karşılığı:** `LoopEvaluatorRegistryTests` (yedi ret vakası) ve
> `LoopCompilationTests.An_unusable_loop_setting_fails_compilation_rather_than_compiling_a_loop_that_never_stops`.

---

### MT-CORE-127 — Akışlı (`SSE`) `run` aynı iterasyon olaylarını yazar ve çerçeve adı stabildir (Faz 151)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 151 |

**Ön koşul:** MT-CORE-124'ün döngülü tanımı.

**Adımlar**
1. Akışlı uçtan bir `run` at ve çerçeveleri kaydet.
2. Kayıtlı olay akışını oku:
   `curl -s "http://localhost:5081/tracon/api/runs/<runId>/events" | head -80`
3. `GET /api/runs/<runId>/trace` ile `span` ağacına bak.

**Beklenen sonuç**
- Akışlı koşum akışsız koşumla **aynı sayıda** `LoopIterationCompleted` yazar.
- Telde çerçeve adı `event: loop.iteration-completed`'dır; `event: unknown`
  görünmez.
- Kök `span` kopmaz; iterasyonların iç `span`'leri kökün altındadır.

> **Otomatik karşılığı:** `HarnessLoopTests.The_streaming_path_records_the_same_iterations_and_names_the_frame_on_the_wire`.

---

### MT-CORE-128 — Bütçe tavanı dolunca döngü yeni iterasyon açmaz (Faz 151)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 151 |

**Ön koşul:** MT-CORE-125'in ulaşılamayan ölçütü, `maxIterations: 8`, ve
`AgentGraph.MaxTotalTokens` bir tek turda dolacak kadar düşük.

**Adımlar**
1. Bir `run` at.

**Beklenen sonuç**
- `run` bütçe hatasıyla durur.
- Model çağrısı sayısı `maxIterations`'tan **azdır** — döngü bütçe dolduktan
  sonra yeni iterasyon açmaz.

> **Otomatik karşılığı:** `HarnessLoopTests.An_exhausted_tree_budget_stops_the_loop_rather_than_letting_it_open_another_iteration`.

### MT-CORE-129 — `AddAgentDecorator()` tüketicinin kendi decorator'ını sıraya sokar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 1 |
| **İlgili karar** | — |

**Ön koşul:** `samples/Tracon.Api/Program.cs`'e GEÇİCİ olarak bir tüketici
decorator'ı eklenir ve `Order => 5` verilir; `Decorate` sarmaladığı agent'ı
çağırmadan önce ve sonra bir satır loglar. Kayıt `tracon.AddAgentDecorator<T>()`
ile yapılır. Değişiklik case sonunda GERİ ALINIR.

**Adımlar**
1. Uygulamayı başlat, `support` agent'ına bir `run` at.
2. Logları ve üretilen `run` kaydını oku.

**Beklenen sonuç**
- Decorator **her** agent'a uygulanır; katalogdan çözülen tek bir agent bile
  atlanmaz.
- Sıralama belgelenmiş kuralı izler: **düşük `Order` DIŞTA sarar.** `Order 5`
  ile tüketici decorator'ı `RunRecordingAgentDecorator`'ın (`Order 0`, en
  dıştaki) **içinde**, `OpenTelemetryAgentDecorator`'ın (`Order 10`) **dışında**
  kalır. Yani logladığı iş `run` kaydının içinde görünür, telemetri span'inin
  dışında.
- Yerleşik decorator'lar (`0` kayıt · `10` telemetri · `20` tool onayı ·
  `30` yapısal yanıt) kaybolmaz; tüketicininki aralarına girer.
- Aynı davranış üç kayıt biçiminin üçünde de aynıdır: `AddAgentDecorator<T>()`,
  bir örnek alan aşırı yükleme ve `IServiceProvider` alan fabrika.

> Bu bir **genişleme noktasıdır** (`CapabilityEntryPoints`, K-509). Yerleşik
> decorator'ların davranışı `08-OPENAI-UYUMLU-UCLAR.md` ve
> `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` içindedir; burada kanıtlanan, **üçüncü
> tarafın kendi decorator'ını kaydedebilmesidir.**

---

### MT-CORE-130 — Üretilmiş tool kaydı özniteliğin her ayarını taşır (Faz 189)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 189 |
| **İlgili karar** | K-867 |

**Ön koşul**
- `samples/Tracon.Api` Development'ta ayakta (`docs/hafiza/elle-kosum-ortami.md` tarifi,
  `--urls http://127.0.0.1:5199`); `Tracon:Ui:AuthToken` user-secrets'ta (`$APB`).

**Adımlar**
1. `curl -s http://127.0.0.1:5199/tracon/api/tools -H "$APB" | python3 -c "import sys,json; [print(t['name'], t['requiresApproval'], t['effect'], t.get('requiredPermission'), t.get('timeout'), t.get('source')) for t in json.load(sys.stdin) if t['name'] in ('cancel_order','get_slow_report')]"`

**Beklenen sonuç**
- `cancel_order`: `requiresApproval: true`, `effect: "Destructive"`, `requiredPermission: "orders.cancel"`, `source: null`.
- `get_slow_report`: `timeout: "00:00:01"`.
- Değerler **üreteç** yolundan gelir (`AddGeneratedTools()`); kayıt kodu örnek uygulamanın kendi derlemesindedir.

> **Otomatik karşılığı:** `GeneratedRegistrationParityTests` ·
> `EmbeddedSampleTests.Generated_tools_keep_the_permission_declared_on_the_attribute`.

---

### MT-CORE-131 — `init` ayarlı `TraconToolRegistration` descriptor'a yedi alanı taşır (Faz 189)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 189 |
| **İlgili karar** | K-867 |

**Ön koşul**
- `samples/Tracon.Api/Program.cs`'e GEÇİCİ olarak şu kayıt eklenir; case sonunda GERİ ALINIR:
  `builder.Services.AddSingleton(new TraconToolRegistration(AIFunctionFactory.Create(() => "ok", "probe_tool")) { RequiresApproval = true, Effect = ToolEffect.Write, RequiredPermission = "x.y", Timeout = TimeSpan.FromSeconds(9), SafeToRepeat = true, MaxOutputBytes = 1024 });`
- Uygulama MT-CORE-130'daki gibi ayakta.

**Adımlar**
1. `GET /tracon/api/tools` → `probe_tool` satırı.
2. Aynı kaydı yalnız `new TraconToolRegistration(fn)` ile yaz, uygulamayı yeniden başlat, tekrarla.

**Beklenen sonuç**
- (1) `requiresApproval: true`, `effect: "Write"`, `requiredPermission: "x.y"`, `timeout: "00:00:09"`,
  `safeToRepeat: true`, `maxOutputBytes: 1024`, `source: null`.
- (2) Verilmeyen her alan varsayılanda: `false`, `"Read"`, `null`.

> **Otomatik karşılığı:** `CatalogEndpointTests.Every_configured_tool_setting_reaches_the_listed_descriptor`
> (`AddTool` ve `AddScopedTool` iki yolu) · `ToolRegistrationParityTests`.

---

### MT-CORE-132 — `onExhausted: "FailRun"` tükenen döngüde `run`'ı `LoopUnsatisfied` ile düşürür (Faz 192)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 192 |
| **İlgili karar** | K-884 |

**Ön koşul:** Örnek uygulama, gerçek model. Harness modu `openai` (Chat Completions)
sağlayıcısında da koşar (F-315 2026-10-12'de kapandı, MT-OAI-094); case
`openai-responses` ile yazıldı ve öyle kalır.

**Adımlar**
1. ```json
   POST /tracon/api/agents
   {"name":"loop-gate","model":{"provider":"openai-responses","model":"gpt-5.4-mini"},
    "instructions":"Answer in one short sentence.",
    "harness":{"loop":{"criteria":[{"kind":"completionMarker","marker":"NEVER-WRITTEN"}],
               "maxIterations":2,"onExhausted":"FailRun"}}}
   ```
2. `POST /tracon/api/agents/loop-gate/run` (`{"message":"Say hello."}`), sonra `GET /api/runs/{id}/events`.

**Beklenen sonuç**
- `run` `Failed`; `error.type` `loop_unsatisfied`, `error.class` `LoopUnsatisfied`.
- İki `LoopIterationCompleted` olayı; sonuncusunun metni "…ends the loop and fails the run",
  payload'ı `ceilingReached: true`, `failsRun: true`.
- Hata mesajı modelin yanıtını taşımaz (agent adı, tur sayısı, ölçüt türü).
- `run.usage` ve `run.cost` doludur ve iki turun toplamını taşır (denetim düzeltmesi:
  istisnayla biten run'ın tek `usage` kaynağı yan kanaldır).

> **Otomatik karşılığı:** `HarnessLoopTests.FailRun_on_exhaustion_fails_the_run_after_exactly_MaxIterations_turns`.

---

### MT-CORE-133 — Alanlar verilmeyince döngü bugünkü gibi son yanıtı döner (Faz 192)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 192 |
| **İlgili karar** | K-884 |

**Ön koşul:** MT-CORE-132'nin tanımı, `onExhausted` çıkarılmış (`PUT`).

**Adımlar**
1. Aynı `run`'ı yeni oturumla at; olayları oku.

**Beklenen sonuç**
- `run` `Completed`; tek `LoopIterationCompleted` olayı, `ceilingReached: true`, `failsRun: false`.

> **Otomatik karşılığı:** `HarnessLoopTests` mevcut on vakası değişmeden yeşil.

---

### MT-CORE-134 — Akışlı istemci son turun metnini, sonra hata çerçevesini alır (Faz 192)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 192 |
| **İlgili karar** | K-884 |

**Ön koşul:** MT-CORE-132'nin tanımı (`FailRun`).

**Adımlar**
1. `curl -N -X POST …/api/agents/loop-gate/run` ile SSE akışını oku.

**Beklenen sonuç**
- Önce `update` (metin) çerçeveleri, en sonda `event: error` (`TraconLoopUnsatisfiedException`);
  `run` `Failed`.

> **Otomatik karşılığı:** `HarnessLoopTests.FailRun_on_exhaustion_streams_the_last_turn_and_then_fails_the_run`.

---

### MT-CORE-135 — `onCriterionError: "FailRun"` ölçütün kendi hatasıyla düşer (Faz 192)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 192 |
| **İlgili karar** | K-884 |
| **Devir** | ➜ CI: `HarnessLoopTests.FailRun_on_criterion_error_fails_the_run_with_the_criterions_own_class` |

**Ön koşul:** `aiJudge` ölçütlü bir tanım; yargıç binding'i (`AddModelRunJudge`) ulaşılamayan
bir uca işaret eder; `onCriterionError: "FailRun"`.

**Adımlar**
1. `run` at.

**Beklenen sonuç**
- `run` `Failed`; sınıf yargıç hatasının sınıfıdır (`ProviderUnavailable`/`Timeout`), `LoopUnsatisfied` DEĞİL.
- Olay `failedCriterion: "aiJudge"`, `failsRun: true`.

---

### MT-CORE-136 — Workflow, döngüsü `FailRun` ile düşen agent'ta durur (Faz 192)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 192 |
| **İlgili karar** | K-884 |

**Ön koşul:** MT-CORE-132'nin tanımı (`FailRun`); örnek uygulamanın `word-count` fonksiyonu.

**Adımlar**
1. `PUT /tracon/api/workflows/loop-gate-flow` → `{"kind":"Sequential","nodes":[{"name":"loop-gate","kind":"Agent"},{"name":"word-count","kind":"Function"}]}`
2. `POST /tracon/api/workflows/loop-gate-flow/run` (`{"message":"Say hello."}`).

**Beklenen sonuç**
- Workflow `run` `Failed`; akışta `word-count` için `ExecutorInvoked` YOKTUR.
- Katılımcı agent run'ı `LoopUnsatisfied` taşır. Workflow satırı da aynı kimliği ve sınıfı
  taşır: `error.type` `loop_unsatisfied`, `error.class` `LoopUnsatisfied` (F-314, 2026-10-12).

> **Otomatik karşılığı:** `HarnessLoopTests.A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion`.

---

### MT-CORE-137 — Başarılı döngü akışsız yolda son turu döner, usage tüm turların toplamıdır (kusur-giderme, 2026-10-12, F-317)

Regresyon. Üç turlu döngü yalnız son turun usage'ını kaydediyordu. Otomatik
karşılığı: `HarnessLoopTests.A_loop_that_succeeds_records_the_usage_of_every_iteration`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-889 |

**Ön koşul:** MT-CORE-132'nin döngülü agent'ı, `ReturnLastResponse`; gerçek model.

**Adımlar**
1. `Idempotency-Key` ile (akışsız) run başlat; ölçüt birkaç tur sürsün.
2. `GET /api/runs/{runId}` ve olayları oku.

**Beklenen sonuç**
- Yanıt yalnız son turun metnidir.
- `usage.totalTokens` her `LoopIterationCompleted` turunun toplamına yakındır,
  son turunkine değil.
- 🚨 Eski kusur: iki turlu döngü `6599`, aynı iki tur `FailRun` ile `13142` kaydetti.

---

### MT-CORE-138 — Zorunlu parametreli agent kuyrukta reddedilir, varsayılanlı agent bağlanır (kusur-giderme, 2026-10-12, F-330)

Regresyon. Kuyruklu run, OpenAI uçları, MCP ve A2A talimata `{{param}}`'ı olduğu
gibi gönderiyordu. Otomatik karşılığı: `AgentParameterPathTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-890 |

**Ön koşul**
- `billing-agent` (`customer` zorunlu, varsayılansız) ve `greeting-agent`
  (`customer` varsayılanı `friend`).

**Adımlar**
1. `Prefer: respond-async` ile iki agent'a da run gönder.
2. `POST /tracon/v1/chat/completions` ile `billing-agent`'a istek gönder.

**Beklenen sonuç**
- `billing-agent`: kuyrukta `400` ve `missingParameters: ["customer"]`;
  OpenAI ucunda `400`.
- `greeting-agent`: run `Completed`; sağlayıcıya giden talimat `friend`'i taşır.
- 🚨 Eski kusur: iki agent da `{{customer}}`'ı model'e olduğu gibi gönderdi.

---

### MT-CORE-139 — Harness agent'ı istenmeyen skill, dosya belleği ve web aramasını açmaz; tur sınırı tutar (kusur-giderme, 2026-10-12, F-339 · F-315)

Regresyon. Skill adı vermeyen harness agent'ı sunucunun çalışma dizinindeki
`SKILL.md` dosyalarını okuyordu. Dosya belleği açılmayan agent model içeriğini
`./agent-file-memory` altına yazıyordu. Her harness agent'ı sağlayıcının ücretli
web aramasını alıyordu. `MaximumIterationsPerRequest` etkisizdi. Otomatik
karşılığı: `HarnessDefaultsTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-892 |

**Ön koşul**
- Örnek uygulama, gerçek model (`openai-responses`).
- Uygulamanın çalışma dizininde `skills/probe/SKILL.md` var. Açıklamasında
  `PROBE-SKILL` sözcüğü geçer.
- `harness-probe` agent'ı: `harness: {}`, `skillNames` boş, `memory` yok.

**Adımlar**
1. `harness-probe`'a "Hangi skill'lerin ve araçların var? Bir not dosyasına yaz." gönder.
2. Run olaylarını ve sağlayıcıya giden isteği (span) incele.
3. Çalışma dizininde `agent-file-memory` klasörünü ara.
4. Agent'ı `harness: {"enableWebSearch": true, "disableWebSearch": true}` ile kaydet.
5. Agent'ı `harness: {"maximumIterationsPerRequest": 2}` ve her turda tool çağıran
   bir istemle koştur.

**Beklenen sonuç**
- Adım 1–2: istekte `load_skill`, `file_memory_*` ve web arama aracı yoktur;
  talimat `PROBE-SKILL` içermez.
- Adım 3: `agent-file-memory` klasörü oluşmaz.
- Adım 4: kayıt `400` döner; mesaj iki alanı da adlandırır.
- Adım 5: üçüncü model çağrısı fonksiyon aracı sunmaz; model yanıt verir.
- 🚨 Eski kusur: skill CWD'den okundu, not `./agent-file-memory/<oturum>/` altına
  yazıldı, `MaximumIterationsPerRequest = 2` ile 7 model çağrısı yapıldı.

---

### MT-CORE-140 — Kuyruklu run isteğin `culture`'ının talimatıyla koşar (kusur-giderme, 2026-10-12, F-351)

Regresyon. Kuyruk worker'ı agent'ı `culture` olmadan çözüyordu; kuyruklu run her
zaman varsayılan talimatla koştu. Otomatik karşılığı:
`CultureInstructionEndpointTests.A_queued_run_uses_the_requested_cultures_instructions`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | — |

**Ön koşul**
- `polyglot-agent`: `instructions` "Reply briefly.",
  `instructionsByCulture: {"tr": "Answer using the tr culture text."}`.
- Kuyruk açık (`UseScheduling`).

**Adımlar**
1. `Prefer: respond-async` ile `{"message":"hi","culture":"tr"}` gönder.
2. Run `Completed` olunca sağlayıcıya giden isteği (span) incele.
3. Aynı isteği `culture` olmadan tekrarla.

**Beklenen sonuç**
- Adım 1–2: talimat "Answer using the tr culture text." olur.
- Adım 3: talimat "Reply briefly." olur.
- Run kaydı `culture` taşımaz. Bu run'ın approval devamı, replay'i ve
  otomatik devamı varsayılan talimatla koşar (bakımcı kararı).
- 🚨 Eski kusur: Adım 1 de "Reply briefly." gönderdi.

---

### MT-CORE-141 — Replay kayıtlı değerle koşar, üzerine yazma birleşir, kaydı olmayan run reddedilir (Faz 194, F-294)

Sınır senaryosu. K-890'ın "replay parametreli tanımı reddeder" maddesi bu fazda
değişti. Otomatik karşılığı: `RunReplayParameterTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 194 |
| **İlgili karar** | K-898 |

**Ön koşul**
- Örnek uygulama Echo kipinde; `podcast-editor` kayıtlı (kod tanımlı: replay yalnız `LiveTools`).

**Adımlar**
1. `Idempotency-Key` ile `podcast-editor`'a `{"message":"Episode 14","parameters":{"tone":"dry"}}` gönder (`$RID`).
2. `POST /api/runs/$RID/replay` `{"toolMode":"LiveTools"}`.
3. Aynısını `{"toolMode":"LiveTools","parameters":{"voice":"onyx"}}` ile tekrarla.
4. `{"parameters":{"vice":"x"}}` ile tekrarla.
5. Bir workflow run'ını (MT-WF-127) replay et.

**Beklenen sonuç**
- Adım 2: çıktı `Edit for a dry tone, voice alloy.` — kayıtlı değer ve varsayılan.
- Adım 3: `Edit for a dry tone, voice onyx.` — yalnız adı verilen değişir.
- Adım 4: `400`; mesaj `Unknown parameter 'vice'` içerir.
- Adım 5: `400`; mesaj workflow run'ının checkpoint'ten sürdürüldüğünü, replay edilmediğini söyler.
- `RecordRunInput=false` ile koşmuş bir run'ın replay'i `404` (girdi yok); parametre kaydı olmayan
  ama mesajı olan eski bir satır `400` ve `did not record the values` ile reddedilir.

---

### MT-CORE-142 — Çökme sonrası devam ve onay-devamı kayıtlı değerleri kullanır; kaydı olmayan kaynak reddedilir (Faz 194, F-294)

Sınır senaryosu. Otomatik karşılığı: `RunContinuationTests` ·
`ApprovalResumeParameterTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 194 |
| **İlgili karar** | K-898 |

**Ön koşul**
- Onay isteyen bir tool'a bağlı, parametreli bir agent; kuyruk açık; `Tracon:RunRecording:RecordRunInput` açık.

**Adımlar**
1. Agent'a `Prefer: respond-async` ile `parameters` vererek bir run gönder; run `AwaitingApproval` olur.
2. Onayı ver (`POST /api/approvals/{id}/decide`).
3. Aynı akışı `RecordRunInput=false` ile tekrarla.

**Beklenen sonuç**
- Adım 2: yeni run `Completed`; talimat, onayı isteyen run'ın değerini taşır (varsayılanı değil) ve yeni run da
  değerini kaydeder.
- Adım 3: yeni run `Failed`; hata `did not record the values` ve `RecordRunInput` adını içerir; model çağrısı yoktur.
- Çökme sonrası devam (otomatik) aynı kuralı izler: kaydı olan run kayıtlı değerle devam eder, kaydı olmayan reddedilir.

---

### MT-CORE-143 — `GET /api/loop-criteria` türleri ve yargıç modelini döner; fabrika çağrılmaz (Faz 201)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 201 |
| **İlgili karar** | K-706 |

**Ön koşul** — Örnek uygulama; bir kez Echo kipinde, bir kez OpenAI kipinde.

**Adımlar** — `curl -s $BASE/api/loop-criteria`

**Beklenen sonuç**
- Dört yerleşik tür sabit sırada (`builtIn: true`), sonra `draft-reviewed` (`builtIn: false`) ve açıklaması.
- Echo: `judgeModel: null`; OpenAI: `{"provider":"openai","model":"…"}`; `providerSettings` alanı yoktur.
- Fabrika liste çağrısıyla koşmaz (`LoopCriteriaEndpointTests`).

---

### MT-CORE-144 — Loop sorunları yollu ve tek tek raporlanır; kayıt `400` hepsini taşır (Faz 201)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 201 |
| **İlgili karar** | K-706 |

**Ön koşul** — Örnek uygulama Echo kipinde (yargıç modeli bağlı değil).

**Adımlar**
1. `POST $BASE/api/agents/validate`:
   `{"name":"w","model":{"provider":"echo","model":"echo-1"},"harness":{"loop":{"criteria":[{"kind":"completionMarker"},{"kind":"x"}],"maxIterations":0}}}`
2. Aynı gövdeyle `POST $BASE/api/agents`.
3. Adım 1'i `"criteria":[{"kind":"aiJudge","judgeCriteria":["Complete?"]}]` ile tekrarla.

**Beklenen sonuç**
- Adım 1: sırayla `loop_max_iterations_invalid` (`harness.loop.maxIterations`), `loop_marker_missing`
  (`…criteria[0].marker`), `loop_criterion_unknown` (`…criteria[1].kind`); `compilation_error` yok.
- Adım 2: `400`; `detail` üç mesajı da taşır.
- Adım 3: tek mesaj `loop_judge_unconfigured` (`…criteria[0].kind`) (`AgentValidationLoopTests`).

---

### MT-CORE-145 — Fabrikalı reviewer ölçütü çocuk run koşar, kendi şemalı karar ve run başına durum taşır (Faz 201)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 201 |
| **İlgili karar** | K-218 |

**Ön koşul** — Örnek uygulama Echo kipinde; `echo-reviewer-1` 1. turda `fail`, sonra `pass` döner.

**Adımlar**
1. `POST $BASE/api/agents/reviewed-writer/run` `{"message":"Write a two-line note about tea."}` → `$RID`.
2. `GET $BASE/api/runs/$RID/events` ve `GET $BASE/api/runs/$RID/tree`.
3. İki `draft-reviewer` run'ının girdisini oku (`GET $BASE/api/runs/{id}/input`).

**Beklenen sonuç**
- Run `Completed`; iki `LoopIterationCompleted`: #1 `continuedBy: "draft-reviewed"`, `hasFeedback: true`; #2 `continued: false`.
- Ağaçta iki `draft-reviewer` run'ı: `parentRunId = rootRunId = $RID`, `depth: 1`.
- Girdiler `Round 1. … Previous findings: none.` ve `Round 2. … Previous findings: The draft has no heading. -> …`;
  yeni bir run yine `Round 1.` ile başlar (`ReviewerLoopEvaluatorTests`, `LoopEvaluatorFactoryTests`).

---

### MT-CORE-146 — Fabrika hatası türü adlandırır ve önbelleğe girmez (Faz 201)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | 201 |
| **İlgili karar** | K-059 |

**Ön koşul** — İlk çağrıda atan bir fabrika (`AddLoopEvaluator("flaky", sp => …)`) kaydeden bir host.

**Adımlar** — `flaky` türünü adlandıran bir tanımı iki kez `POST $BASE/api/agents` ile kaydet.

**Beklenen sonuç**
- İlk kayıt `400`; `detail` `'flaky'` ve istisna tipini adlandırır, istisnanın kendi mesajını taşımaz (log taşır).
- İkinci kayıt `201`; agent koşar (`LoopEvaluatorFactoryTests`).

---

### MT-CORE-147 — `Retention:IdempotencyKeys` yapılandırmadan bağlanır (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 206 |
| **İlgili karar** | K-021 |

**Ön koşul** — Örnek uygulama; `Tracon__Retention__Enabled=true`, `Tracon__Retention__IdempotencyKeys__MaxAgeDays=0`.

**Adımlar** — Uygulamayı başlat; sonra değeri `3` yapıp yeniden başlat.

**Beklenen sonuç**
- İlk açılış durur; mesaj `TraconRetentionOptions.idempotency_keys.MaxAgeDays must be at least 1 when specified. Actual value: 0.` içerir. Eski kusur: değer yok sayılıyor, uygulama açılıyordu.
- `3` ile açılır (`TraconRetentionOptionsBindingTests.Every_retention_target_is_bound`).

---

### MT-CORE-148 — Okunamayan kota eşiği açılışı durdurur ve girdiyi alıntılar (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 206 |
| **İlgili karar** | K-993 |

**Ön koşul** — `Tracon__Quotas__ThresholdPercents__0=80`, `Tracon__Quotas__ThresholdPercents__1=yuz`.

**Adımlar** — Uygulamayı başlat.

**Beklenen sonuç**
- Açılmaz; mesaj `TraconQuotaOptions.ThresholdPercents[1] is 'yuz', which is not a whole number` içerir (yerine bağlanan `0`'ı değil, yazılan metni). Eski kusur: `[80]` ile açılıyordu.
- ➜ CI: `TraconQuotaOptionsBindingTests.An_unreadable_threshold_is_named_as_written`.

---

### MT-CORE-149 — Okunamayan webhook gecikmesi açılışı durdurur ve girdiyi alıntılar (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 206 |
| **İlgili karar** | K-993 |

**Ön koşul** — `Tracon__Webhooks__RetryDelays__0=00:01:00`, `Tracon__Webhooks__RetryDelays__1=soon`.

**Adımlar** — Uygulamayı başlat.

**Beklenen sonuç**
- Açılmaz; mesaj `TraconWebhookOptions.RetryDelays[1] is 'soon', which is not a TimeSpan` içerir. Eski kusur: merdiven bir basamak kısa açılıyordu.
- ➜ CI: `TraconWebhookOptionsBindingTests.An_unreadable_retry_delay_is_named_as_written`.

---

### MT-CORE-150 — Bilinmeyen korunan sütun adı, koruma kapalıyken de açılışı durdurur (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | 206 |
| **İlgili karar** | K-993 |

**Ön koşul** — Örnek uygulamanın `appsettings.json`'ı (`ContentProtection:Enabled=true`); `Tracon__ContentProtection__Columns__0=RunInputt`.

**Adımlar**
1. Uygulamayı başlat.
2. `Tracon__ContentProtection__Enabled=false` ekleyip yeniden başlat.

**Beklenen sonuç**
- İki kezde de açılmaz; mesaj `TraconContentProtectionOptions.Columns contains 'RunInputt', which is not a ProtectedColumn name` içerir. Eski kusur: girdi atlanıyor, kastedilen sütun düz metin kalıyordu.
- Bir SQL sağlayıcısı kayıtlıysa bu hata `IStartupValidator` toplamından **önce** atılır; aynı koşumdaki öteki seçenek hataları görünmez. Case'i tek başına koş.

---

### MT-CORE-151 — Boş anahtar adı koruma kapalıyken de açılışı durdurur (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 206 |
| **İlgili karar** | K-993 |

**Ön koşul** — `Tracon__ContentProtection__Keys__old=` (boş ortam değişkeni), `Tracon__ContentProtection__Enabled=false`.

**Adımlar** — Uygulamayı başlat.

**Beklenen sonuç**
- Açılmaz; mesaj `TraconContentProtectionOptions.Keys['old'] cannot be empty` içerir. Ölçüldü (2026-10-14, macOS): boş ortam değişkeni sağlayıcıda `""` durur; JSON'daki `"old": ""` aynı sonucu verir.

---

### MT-CORE-152 — `MaskedPii` yazım hatası açılışı durdurur; kodun değeri kazanır (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | 206 |
| **İlgili karar** | K-993 |

**Ön koşul** — `Tracon__ContentGuard__Pattern__MaskedPii=Emial,CreditCard`.

**Adımlar**
1. `Tracon__Demo__SuppressRegistrations=ContentGuard` ile başlat.
2. `SuppressRegistrations`'ı kaldırıp yeniden başlat.

**Beklenen sonuç**
- 1: açılmaz; mesaj `PatternContentGuardOptions.MaskedPii is 'Emial,CreditCard', which is not a comma-separated list of PiiPatterns names` içerir. Eski kusur: değer `None` kalıyor, PII maskeleme kapanıyordu.
- 2: açılır. Örnek kod `MaskedPii`'yi `AddPatternContentGuard(...)` içinde yazar; bağlamadan sonra yazan kod kazanır.

---

### MT-CORE-153 — Temiz yapılandırma davranış değişikliğinden etkilenmez (Faz 206, F-247)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 206 |
| **İlgili karar** | K-993 |

**Ön koşul** — Örnek uygulama; bu dosyadaki ortam değişkenlerinin hiçbiri yok.

**Adımlar** — Uygulamayı başlat; `POST $BASE/api/agents/support/run` `{"message":"ping","sessionId":"mt-core-153"}`.

**Beklenen sonuç**
- Açılır; akış `run` · `update` · `done`; `GET $BASE/api/runs/{runId}` → `status: Completed`.

---

### MT-CORE-154 — Okunamayan `UtilityModel` sayısı açılışı durdurur ve yazılan metni alıntılar (Faz 215, F-568)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 215 |
| **İlgili karar** | — (F-568; K-993'ün "okunamayan girdi atlanmaz" kuralı) |

**Ön koşul** — `Tracon__UtilityModel__Provider=openai`, `Tracon__UtilityModel__Model=gpt-5.4-mini`, `Tracon__UtilityModel__Temperature=abc`.

**Adımlar** — Uygulamayı başlat. Sonra `Tracon__UtilityModel__Temperature` değerini boş yap ve yeniden başlat.

**Beklenen sonuç**
- İlkinde açılmaz; mesaj `TraconOptions.UtilityModel.Temperature is 'abc', which is not a number` içerir (bağlanan `0`'ı değil, yazılan metni). Eski kusur: `Temperature = 0` ile açılıyordu ve bağlam özetleyicisi sıfır sıcaklıkta koşuyordu.
- İkincisinde açılır; sağlayıcı varsayılanı kullanılır.
- ➜ CI: `TraconUtilityModelBindingTests.An_unreadable_number_fails_at_startup` · `An_empty_number_means_the_provider_default`.

---

### MT-CORE-155 — Konuşma hafızası: kullanıcı yeni oturumda hatırlanır; başka kullanıcı ve silinen oturum hatırlanmaz (Faz 233, F-627)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 233 |
| **İlgili karar** | K-1065, K-1066, K-1067 |

**Devir** — 🤖 ajan koşar — örnek uygulama, `curl`, gerçek `openai` modeli ve gerçek PostgreSQL + pgvector (`CommunityToolkit.VectorData.PgVector`)

**Ön koşul** — Örnek uygulama Development'ta, `Tracon:PostgreSql` ve `Tracon:Providers:OpenAI` ayarlı (`memory-assistant` agent'ı yalnız ikisi birlikte varken kaydolur). pgvector'lü PostgreSQL ayakta. 🚨 Uygulama paylaşılan yerel veritabanına bağlanır; `AutoApplyMigrations` bekleyen migration'ı oraya yazar (`hafiza/elle-kosum-ortami.md`). Kimlik: `X-Demo-User` başlığı.

**Adımlar**
1. `X-Demo-User: ada`, `sessionId: s1` ile `POST $BASE/api/agents/memory-assistant/run` `{"message":"My favourite colour is teal."}`.
2. `X-Demo-User: ada`, **yeni** `sessionId: s2` ile `{"message":"What is my favourite colour?"}`.
3. `X-Demo-User: grace`, `sessionId: s3` ile aynı soru.
4. `DELETE $BASE/api/sessions/s1` ve `s2` (ada'nın bilgiyi taşıyan oturumları). Sonra `X-Demo-User: ada`, `sessionId: s4` ile aynı soru.
5. Kayıtlar: tabloda `tracon_conversation_memory` satırlarını kullanıcı ve oturuma göre say.

**Beklenen sonuç**
- 2: yanıt "teal" der. Modele giden istek `## Memories` başlıklı bir mesaj taşır.
- 3: yanıt bilmez ("I don't have that in memory").
- 4: oturumlar `204` ile silinir; ada'nın satırı kalmaz, grace'in satırları kalır. `s4`'te yanıt bilmez.
- 5: satırlar yalnız `user` ve `assistant` rolü taşır (tool mesajı yok); `ApplicationId` kiracı, `AgentId` agent adı, `UserId` kullanıcıdır.
- ➜ CI: `ConversationMemoryTests` (fonksiyonel) · `ConversationMemoryConnectorTests` (gerçek bağlayıcı).

---

### MT-CORE-156 — Konuşma hafızası `OnDemand`: model aramayı tool olarak çağırır ve yalnız arayanın kapsamını okur (Faz 233, F-627)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 233 |
| **İlgili karar** | K-1065 |

**Devir** — 🤖 ajan koşar — örnek uygulama, `curl`, gerçek `openai` modeli

**Ön koşul** — MT-CORE-155 ortamı. Yönetim API'siyle bir agent oluştur: `POST $BASE/api/agents` `{"name":"memory-ondemand","instructions":"…call the search_conversation_memory tool first when the user asks about something they told you before.","model":{"provider":"openai","model":"gpt-5.4-mini"},"memory":{"enableConversationMemory":true,"conversationMemorySearch":"OnDemand"}}`.

**Adımlar**
1. `X-Demo-User: ada`, `sessionId: o1` ile `{"message":"My dog is called Rex."}`.
2. `X-Demo-User: ada`, yeni oturumla `{"message":"What is my dog called?"}`; akıştaki tool çağrılarına bak.
3. `X-Demo-User: grace` ile aynı soru.
4. Temizlik: oturumları ve agent'ı sil.

**Beklenen sonuç**
- 2: akışta `search_conversation_memory` tool çağrısı vardır; yanıt "Rex" der.
- 3: tool çağrılır ama ada'nın kaydı dönmez; yanıt bilmez.
- Tool adı `ToolNames`'te yer almaz ve onay istemez.
- ➜ CI: `ConversationMemoryTests.An_on_demand_agent_offers_the_search_tool_and_the_tool_reads_only_the_callers_memory`.

---

### MT-CORE-157 — Oturum çalışma alanı: yazma onay bekler, onaydan sonra dosya oluşur, okuma onaysız çalışır (Faz 234, F-628)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 234 |
| **İlgili karar** | K-1068, K-1069 |

**Devir** — 🤖 ajan koşar — örnek uygulama (SQLite, boş bir veritabanı dosyası), `curl`, gerçek `openai` modeli

**Ön koşul** — Örnek uygulama Development'ta, `Tracon__PostgreSql__ConnectionString=` `Tracon__SqlServer__ConnectionString=` `Tracon__Ui__AuthToken=` boş, `Tracon__Sqlite__ConnectionString=Data Source=<geçici dosya>` ve `Tracon:Providers:OpenAI:ApiKey` ayarlı (`user-secrets`). `workspace-drafter` agent'ı yalnız OpenAI açıkken kaydolur. Satırları saymak için `agent_files` tablosunu `sqlite3` ya da Python ile oku.

**Adımlar**
1. `POST $BASE/api/agents/workspace-drafter/run` `Prefer: respond-async`, `{"message":"Create a file notes.md with exactly three short bullet points about tea. Then tell me it is done.","sessionId":"ws-s1"}`. Run `AwaitingApproval` olana dek bekle.
2. `GET $BASE/api/approvals/pending`; `agent_files` satırlarını say.
3. `POST $BASE/api/approvals/{id}/decide` `{"approved":true}`; devam run'ı `Completed` olana dek bekle; `agent_files` satırlarını say.
4. Aynı oturumda (`ws-s1`) senkron: `{"message":"Show me the content of notes.md."}`; akıştaki tool çağrılarına bak.

**Beklenen sonuç**
- 2: bekleyen onay `file_access_write` tool'u içindir; `agent_files` boştur (yazma bir kişiyi bekler).
- 3: tablo tek satır taşır; yol `/default/~workspace/{32 onaltılık karakter}/workspace-drafter/notes.md`'dir (oturum kimliği yolda yoktur).
- 4: akışta `file_access_read` çağrısı vardır ve onay istenmez (`workspace-drafter` okumayı onaysız açar); yanıt üç maddeyi gösterir.
- ➜ CI: `SessionWorkspaceApprovalTests` (onay akışı) · `SessionWorkspaceTests` (tool listesi, izolasyon).

---

### MT-CORE-158 — Oturum çalışma alanı: başka oturum boş bir alanla başlar (Faz 234, F-628)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 234 |
| **İlgili karar** | K-1068 |

**Devir** — 🤖 ajan koşar — MT-CORE-157 ortamı

**Ön koşul** — MT-CORE-157 koşulmuş; `ws-s1` oturumunda `notes.md` vardır.

**Adımlar**
1. Senkron: `POST $BASE/api/agents/workspace-drafter/run` `{"message":"List my files.","sessionId":"ws-s2"}`; akıştaki tool çağrılarına bak.

**Beklenen sonuç**
- Akışta `file_access_ls` çağrısı vardır; yanıt çalışma alanının boş olduğunu söyler (`ws-s1`'in dosyası görünmez).
- ➜ CI: `SessionWorkspaceTests.A_file_in_one_session_is_listed_in_that_session_and_not_in_another_session_of_the_same_agent`.

---

### MT-CORE-159 — Oturum çalışma alanı: oturum silinince dosyalar da silinir (Faz 234, F-628)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 234 |
| **İlgili karar** | K-1068, K-1070 |

**Devir** — 🤖 ajan koşar — MT-CORE-157 ortamı

**Ön koşul** — MT-CORE-157 koşulmuş; `agent_files` `ws-s1`'in bir satırını taşır.

**Adımlar**
1. `DELETE $BASE/api/sessions/ws-s1`.
2. `agent_files` satırlarını say.

**Beklenen sonuç**
- 1: `204`.
- 2: tablo boştur. Başka bir oturumun dosyası olsaydı kalırdı.
- ➜ CI: `SessionWorkspaceErasureTests` (SQLite, onay akışıyla yazılan dosya) · `SessionWorkspaceFilesTests` (SQLite, PostgreSQL, SQL Server: veri sahibi silme önizleme/dışa aktarma/silme ve saklama).

---

### MT-CORE-160 — `agentReview` reviewer'ı çocuk run koşturur; iterasyon olayı onaylayanı adlandırır (Faz 242, F-411)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 242 |
| **İlgili karar** | K-1087, K-1088 |

**Devir** — 🤖 ajan koşar — örnek uygulama (Echo kipi; OpenAI anahtarı varsa ikinci koşu gerçek sağlayıcıyla)

**Ön koşul** — Örnek uygulama Echo kipinde; `echo-approver-1` ilk iterasyonda `approved: false`, sonra `approved: true` döner. `reviewed-writer-builtin` agent'ı yerleşik `agentReview` türünü `draft-approver` ile kullanır.

**Adımlar**
1. `GET $BASE/api/loop-criteria`.
2. `POST $BASE/api/agents/reviewed-writer-builtin/run` `{"message":"Write a two-line note about tea."}` → `$RID`.
3. `GET $BASE/api/runs/$RID/events` ve `GET $BASE/api/runs/$RID/tree`.

**Beklenen sonuç**
- 1: ilk beş tür sırayla `completionMarker`, `todoCompletion`, `aiJudge`, `backgroundTaskCompletion`, `agentReview` ve hepsi `builtIn: true`.
- Run `Completed`; iki `LoopIterationCompleted`: #1 `continuedBy: "agentReview"`, `hasFeedback: true`, `criteria[0] = {kind: "agentReview", decision: "continue", reviewer: {agentName: "draft-approver", agentVersion: 1, runId}}`; #2 `continued: false`, `criteria[0].decision: "stop"`.
- Ağaçta iki `draft-approver` run'ı: `parentRunId = rootRunId = $RID`, `depth: 1`; olaylardaki iki `runId` bu iki run'dır.
- Olay `text`'i `iteration 1 continued by 'agentReview' (reviewer 'draft-approver' v1)`; yük reviewer'ın geri bildirim metnini taşımaz.
- Gerçek sağlayıcı koşusunda (anahtar varsa) en az bir `continue` ve bir `stop` iterasyonu; reviewer'ın cevabı kendi run'ının çıktısındadır (`AgentReviewLoopTests`).

---

### MT-CORE-161 — Konsol Loop editörü `agentReview` türünü düzenler ve tür değişiminde reviewer alanlarını temizler (Faz 242, F-411)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 242 |
| **İlgili karar** | K-918, K-1087 |

**Devir** — 🤖 ajan koşar — Playwright MCP

**Ön koşul** — Örnek uygulama ayakta; konsol `$UI` adresinde; katalogda `draft-approver` ve `draft-reviewer` vardır.

**Adımlar**
1. Yeni agent: harness özelliklerini ve Loop'u aç; ölçüt türünü `agentReview` yap.
2. Reviewer olarak `draft-approver` seç, sürümü `1` yaz, kaydet; sayfayı yenile.
3. Türü `aiJudge`'a, sonra yine `agentReview`'a çevir.
4. Doğrula düğmesine bas.

**Beklenen sonuç**
- 1: ölçüt satırı bir agent listesi ve bir sürüm alanı gösterir; liste düzenlenen agent'ı taşımaz.
- 2: kayıttan sonra tanım `reviewerAgent: "draft-approver"` ve `reviewerAgentVersion: 1` taşır (`GET $BASE/api/agents/{ad}`); yenilemeden sonra alanlar dolu gelir.
- 3: `aiJudge` iken reviewer alanları görünmez; `agentReview`'a dönünce reviewer boş ve sürüm boştur (K-918: alanlar yalnız tür değişiminde temizlenir).
- 4: reviewer boşken `loop_reviewer_missing` `harness.loop.criteria[0].reviewerAgent` yolunda raporlanır (`agent-editor.test.tsx` aynı kuralı Vitest ile sabitler).
