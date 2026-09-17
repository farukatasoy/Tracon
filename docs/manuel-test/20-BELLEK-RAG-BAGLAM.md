# 20 — Bağlam Sıkıştırma, Bellek Sağlayıcıları ve Anlamsal Arama (`MEM`)

> **Alan kodu:** `MEM` · **Faz:** 13, 51
> **Kaynak:** `src/Tracon.Abstractions/Agents/CompactionSettings.cs`,
> `CompactionStrategyKind.cs`, `MemorySettings.cs` ·
> `src/Tracon.Abstractions/Knowledge/` (tümü: `IVectorSearchStore.cs`,
> `VectorChunk.cs`, `VectorSearchRequest.cs`, `VectorSearchHit.cs`) ·
> `src/Tracon.Core/Compilation/AgentDefinitionCompiler.cs` (yalnız
> sıkıştırma/bellek/anlamsal-arama bağlama kısmı: `BuildCompactionStrategy`,
> `CreateMemoryProviders`, `AddVectorSearchTool`, harness çakışma denetimi) ·
> `src/Tracon.Core/Compilation/ObservedCompactionStrategy.cs`,
> `CompactionUsageTrackingChatClient.cs` ·
> `src/Tracon.Core/Knowledge/` (tümü: `TraconKnowledgeOptions.cs`,
> `TextChunker.cs`, `KnowledgeIngestionService.cs`, `VectorSearchToolFactory.cs`) ·
> `src/Tracon.PostgreSql/Stores/PgVectorSearchStore.cs` ·
> `src/Tracon.PostgreSql/MigrationsKnowledge/0001_vector.sql` (Faz 67
> öncesi: `Migrations/0024_vector.sql`) ·
> `src/Tracon.AspNetCore/Endpoints/KnowledgeEndpoints.cs` ·
> `src/Tracon.AspNetCore/Contracts/KnowledgeContracts.cs` ·
> `samples/Tracon.Api/Program.cs` (yalnız embedding kaydı ve
> `knowledge-assistant` bloğu).
>
> 🚨 **Kaynak eşlemesi düzeltmesi.** `00-INDEKS.md`'nin §7 tablosu bu dosya
> için yalnız `src/Tracon.Core` (memory) ve migration dosyasını
> listeliyordu. Ölçüldü: Faz 51'in yönetim yüzeyi (`KnowledgeEndpoints.cs`,
> `KnowledgeContracts.cs`) ve Faz 13/51'in sözleşme tipleri
> (`Tracon.Abstractions/Agents/*Settings.cs`, `Knowledge/*.cs`) başka
> hiçbir dosyanın kaynak eşlemesinde yok — bu iki alan olmadan bu dosyanın
> ana kanıtı (belge yükleme, arama, HTTP hataları) hiç test edilemezdi.
> Yukarıdaki liste düzeltilmiş hâldir.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/20-BELLEK-RAG-BAGLAM.md`](kosumlar/2026-08-13/20-BELLEK-RAG-BAGLAM.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

Faz 13, MAF'ta hazır duran ama hiç kullanılmayan iki yeteneği açığa çıkardı:
**bağlam sıkıştırma** (beş strateji + sabit sıralı bir pipeline) ve **bellek
sağlayıcıları** (dosya belleği, todo takibi, dosya deposu üzerinde metin
araması). Faz 51 bunun üzerine **anlamsal arama**yı (`pgvector`) ekledi ve
dosya aramasının performans sorununu SQL'e indirdi (bu dosyanın kapsamı
yalnız Faz 51'in İş B'sidir — anlamsal arama; İş A, regex performans
düzeltmesi, `04-KALICILIK-DIGER.md`'nin işidir).

```mermaid
flowchart TD
    A["AgentDefinition.Compaction/Memory"] --> B["AgentDefinitionCompiler"]
    B -- "gecersiz tetikleyici/alan" --> BX["TraconCompilationException<br/>400 Agent derlenemedi"]
    B -- "gecerli" --> C["CompactionProvider / FileMemoryProvider /<br/>TodoProvider / TextSearchProvider / search_knowledge tool"]

    C --> D["Gercek calistirma"]
    D --> E["HistoryCompacted olayi<br/>run_events"]
    D --> F["conversation_items<br/>SILINMEZ (K-107)"]

    G["POST api/knowledge/collection/documents"] --> H["KnowledgeIngestionService<br/>parcala + gomule"]
    H --> I["IVectorSearchStore<br/>YALNIZ PostgreSQL (PgVectorSearchStore)"]
    I --> J["document_embeddings<br/>tenant_id + collection ile suzulur"]
    J --> K["search_knowledge tool'u<br/>agent'a baglanir"]

    L["TextSearchProvider<br/>Faz 13, eski"] -. "kok / recursive arama" .-> M["PAYLASILAN AgentFileStore<br/>tek surec-capinda singleton"]

    style BX fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style M fill:#7a4a1f,stroke:#3d250f,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP zarfı (`ProblemDetails`, CRUD, idempotency) | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Üç katmanlı erişim koruması, kiracı izolasyonu (genel), API anahtarı kapsamları | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada TEKRARLANMAZ, yalnız §6/§7'deki bu dosyaya özgü boşluklar için referans verilir |
| Agent düzenleyicisinin "Context" paneli (strateji seçimi, koşullu alan görünürlüğü) | `10-ARAYUZ-AGENT-PLAYGROUND.md` `MT-UIAG-011` (zaten üretildi) — burada TEKRARLANMAZ |
| `run-detail.tsx`'in `history.compacted` rozeti, playground transkriptindeki `'compaction'` satırı | `10-ARAYUZ-AGENT-PLAYGROUND.md`/`11-ARAYUZ-RUN-SESSION-SSE.md` (zaten üretildi) — burada TEKRARLANMAZ; bu dosya yalnız HTTP/DB seviyesini doğrular |
| Faz 51 İş A (regex/önek aramasının SQL'e inmesi, üç sağlayıcı) | `04-KALICILIK-DIGER.md` (zaten üretildi) |
| `document_embeddings`'in saklama (retention) politikasıyla gerçekten silinmesi | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) — burada yalnız hedefin var olduğu kod okumasıyla anılır, çalıştırılmaz |
| Belge yükleme maliyetinin kota/fiyat modeline girip girmediği | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) kapsamı dışında kaldı (Faz 51 doc, Açık Soru 5) — burada da ölçülmez |
| Anlamsal aramanın SQL Server/SQLite'ta uygulanması | **Yok** — `IVectorSearchStore`'un tek somut uygulaması PostgreSQL'dir (K-343); bu bir kapsam boşluğu değil, bilinçli bir sınırdır |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama PostgreSQL ile çalışır (`pgvector` uzantılı,
   `Tracon:PostgreSql:ConnectionString` ayarlı) — bu dosyanın §4-§6
   bölümleri **yalnız PostgreSQL'de** anlamlıdır (K-343). §1-§3 (sıkıştırma,
   dosya belleği/todo/metin araması) kalıcılık sağlayıcısından bağımsızdır.
3. `Tracon:Providers:OpenAI:ApiKey` tanımlı (§2, §4, §5 gerçek model ve
   gerçek embedding çağırır).
4. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
```

> **Gerçek para uyarısı.** §2 her case'te en az 4-5 gerçek model çağrısı
> yapar (çok turlu konuşma, sıkıştırmayı tetiklemek için). §4, §5 her belge
> yüklemede bir embedding çağrısı, her aramada bir embedding çağrısı yapar
> (`text-embedding-3-small`, düşük maliyetli). §1, §3 (yalnız pozitif dosya
> belleği/todo case'leri hariç — onlar da gerçek model çağırır), §6, §7 kısa
> gerçek çalıştırmalar içerir. §1'in yapısal doğrulama case'leri (`/validate`)
> **hiçbir model çağırmaz** (`ValidateAgentAsync`'in kendi XML dokümanı: "hiçbir
> model çağırmadan derler").

---

## Bu dosyanın yerel fixture'ları

Bu veriler yalnız bu dosyaya özgüdür, `00-INDEKS.md`'ye girmez (`PROMPT.md`
§4.2). `knowledge-assistant` (hazır, PostgreSQL + embedding ister) için bkz.
`00-INDEKS.md` §3.1.

| Kimlik | Değer |
|---|---|
| `FIX-MEM-COMPACT-01` | Ad `manuel-sikistir` · Model `{provider: openai, model: gpt-5.4-mini}` · Talimat `"Kisa yanit ver, en fazla 5 kelime kullan."` · `Compaction: {Strategy: SlidingWindow, TriggerMessages: 6, MinimumPreservedTurns: 1}` — Faz 13'ün kendi gerçek kanıtıyla (bkz. `13-BAGLAM-SIKISTIRMA-VE-BELLEK.md`, §"Gerçek kanıt") AYNI eşik: 5 kullanıcı turundan sonra sıkıştırma tetiklenir |
| `FIX-MEM-COMPACT-02` | `FIX-MEM-COMPACT-01` ile aynı, yalnız `Compaction.Strategy: Summarization`, `MinimumPreservedGroups: 2`, ad `manuel-ozetle` |
| `FIX-MEM-COMPACT-03` | `FIX-MEM-COMPACT-01` ile aynı, yalnız `Compaction.Strategy: Pipeline`, ad `manuel-pipeline` |
| `FIX-MEM-FILE-01` | Ad `manuel-dosya-bellek` · Model `{provider: openai, model: gpt-5.4-mini}` · Talimat `"Kullanici bir bilgiyi hatirlamani isterse dosya bellegine kisa bir not olarak yaz. Sorulduğunda dosyadan okuyup cevapla."` · `Memory: {EnableFileMemory: true}` |
| `FIX-MEM-FILE-02` | Ad `manuel-dosya-arama` · aynı model · Talimat `"Kullanicinin sordugu konuyu dosya belleginde ara ve bulduğun iceriği ozetle."` · `Memory: {EnableTextSearch: true}` |
| `FIX-MEM-COLLECTION-01` | Koleksiyon adı `manuel-bilgi` (sample'ın `kurumsal` koleksiyonuyla çakışmaz) |
| `FIX-MEM-DOC-01` | `sourceId: "izin-notu"` · metin `"Yillik izin 14 gundur. Bes yildan sonra 20 gune cikar."` (Faz 51'in kendi gerçek kanıtıyla aynı örnek — kelime eşleşmesi olmayan sorguyla arandığında bulunması ÖLÇÜLMÜŞ) |
| `FIX-MEM-QUERY-01` | `"tatil hakkim ne kadar"` — `FIX-MEM-DOC-01` içinde "tatil" kelimesi HİÇ geçmez |

> 🚨 **Koşum sırasında bulunan doküman sapması (2026-08-13, S1-4, ürün kusuru
> DEĞİL).** `MT-MEM-006/008/009/011/013/027`'nin "Girilecek veri" script'leri
> yeni bir fixture agent'ı `PUT /api/agents/{ad}` ile kaydediyordu. Ölçüldü:
> `UpdateAgentAsync` (`AgentEndpoints.cs:303`) yalnız **var olan** bir tanımı
> günceller — `definitions.GetAsync(name)` `null` dönerse `404 "Agent
> bulunamadi"` verir (satır ~332). Yeni kayıt `POST /api/agents` gerektirir
> (`CreateAgentAsync`, aynı gövde, isim URL'de değil gövdede). Altı case'in
> script'i `POST "$APU/api/agents"` kullanacak şekilde düzeltildi; orijinal
> (bozuk) `PUT` biçimi burada kayıt olarak kalsın: `PUT
> "$APU/api/agents/<ad>"` ile aynı gövde → `404`. `MT-MEM-031`'in `PUT
> "$APU/api/agents/kapsam-kontrol"` çağrısı bu düzeltmenin DIŞINDA
> bırakıldı — o case kapsam denetiminin (`RequireApiKeyScope`) agent
> var/yok kontrolünden ÖNCE devreye girip girmediğini ölçüyor; beklenen
> `403`, endpoint gövdesine hiç girmeden filtre seviyesinde üretilir.

---

# 1 — Sıkıştırma: Yapısal Doğrulama (Faz 13, İzlek B, model çağırmaz)

`POST /api/agents/validate`, `AgentDefinitionValidator.CheckStructureAsync`
üzerinden `AgentDefinitionCompiler.Compile(...)`'ı **gerçekten** çalıştırır
(`AgentDefinitionValidator.cs:322-336`) ve fırlayan
`TraconCompilationException`'ı `messages` dizisine `code:
"compilation_error"` ile ekler. Hiçbir şey kaydedilmez, hiçbir model
çağrılmaz. `POST`/`PUT /api/agents` bu denetimi **YAPMAZ** — yalnız temel
alan kontrolü ve çağrı grafiği döngü denetimi yapar
(`AgentEndpoints.CreateAgentAsync`/`UpdateAgentAsync`, satır 242-330); bir
sıkıştırma/bellek/anlamsal-arama hatası olan bir tanım **kaydedilir** ve
yalnız `/run` çağrıldığında `400 "Agent derlenemedi"` ile ortaya çıkar — bu
ayrım §2'de bir case ile ayrıca ölçülür.

### MT-MEM-001 — Tetikleyicisiz `SlidingWindow` derleme hatası verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `TriggerTokens`/`TriggerMessages`/`TriggerTurns` alanlarının **hiçbirini**
   vermeden `Strategy: SlidingWindow` doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-tetiksiz",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "SlidingWindow" }
}'
```

**Beklenen sonuç**
- `valid: false`.
- `messages[0].code` = `"compilation_error"`.
- `messages[0].message` tam olarak şu metni içerir (🚨 doküman düzeltildi,
  koşum 2026-09-17 ap-s3 — kaynak İngilizce'dir, K-228): `"Agent
  'manuel-tetiksiz' selected the 'SlidingWindow' compaction strategy but
  gave no trigger (at least one of TriggerTokens/TriggerMessages
  /TriggerTurns is required)."`
- Hiçbir kayıt oluşmaz: `GET $APU/api/agents` çıktısında `manuel-tetiksiz`
  **yoktur**.

---

### MT-MEM-002 — `ContextWindow` stratejisi `MaxContextWindowTokens` olmadan derleme hatası verir

`ContextWindow` diğer stratejilerin aksine bir **tetikleyici** istemez (kendi
içinde kurulur) ama `MaxContextWindowTokens` **zorunludur** — farklı bir
doğrulama kuralı, farklı kod yolu (`BuildContextWindowStrategy`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `MaxContextWindowTokens` vermeden `Strategy: ContextWindow` doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-pencere-eksik",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "ContextWindow" }
}'
```

**Beklenen sonuç**
- `valid: false`.
- `messages[0].message` şunu İÇERİR (🚨 doküman düzeltildi, koşum
  2026-09-17 ap-s3 — kaynak İngilizce'dir, K-228; mesaj ayrıca modelin
  kendi katalog bağlam penceresine de baktığını söyleyen bir cümle
  KAZANMIŞ, spec'in yazıldığı andan sonra — ürün kusuru değil, daha
  bilgilendirici hâle gelmiş): `"selected the ContextWindow compaction
  strategy but did not supply MaxContextWindowTokens, and its model
  ('openai/gpt-5.4-mini') has no context window size in the catalog
  either. Set MaxContextWindowTokens explicitly."`

---

### MT-MEM-003 — Geçerli `SlidingWindow` tanımı `valid: true` döner (pozitif kontrol)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `TriggerMessages` verilmiş geçerli bir `SlidingWindow` tanımı doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-gecerli",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "SlidingWindow", "triggerMessages": 6, "minimumPreservedTurns": 1 }
}'
```

**Beklenen sonuç**
- `valid: true`, `messages` dizisi **boştur**.

---

### MT-MEM-004 — Bilinmeyen `strategy` string değeri JSON deserialize hatası verir (`400`)

`CompactionStrategyKind` `JsonStringEnumConverter<T>` ile işaretli (K-040
deseni). Bu, `TraconCompilationException` **değil** — istek gövdesi
model bağlama ulaşamadan reddedilir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-040 |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `strategy` alanına enum'da olmayan bir değer gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "manuel-bilinmeyen-strateji",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "Sihirli" }
}'
```

**Beklenen sonuç**
- `HTTP: 400` — `ValidateAgentAsync`'in kendi XML dokümanına göre bu, "gövde
  ayrıştırılamıyor" durumudur (yalnızca bu durumda `/validate` de `400`
  döner).
- Yanıt gövdesi bir JSON ayrıştırma hatası bildirir; `valid` alanı **yoktur**
  (rapor hiç üretilmedi).

---

### MT-MEM-005 — Harness çakışma denetimi: `Disable*` bayrakları etkin ayarlarla çelişirse derleme hatası

Üç bağımsız çakışma denetimi (`AgentDefinitionCompiler.cs:1052-1090`), aynı
desen: `Compaction`/`Memory` bir yetenek istiyor ama aynı tanımın `Harness`
bloğu onu kapatıyor. Yalnızca `Harness` bloğu **dolu** bir tanımda (harness
derleme yoluna girer) tetiklenir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K1 (sıfır sürpriz) |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Compaction.Strategy != None` + `Harness.DisableCompaction: true` doğrula.
2. `Memory.EnableFileMemory: true` + `Harness.DisableFileMemory: true` doğrula.
3. `Memory.EnableTodo: true` + `Harness.DisableTodoProvider: true` doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-cakisma-1",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "harness": { "disableCompaction": true },
  "compaction": { "strategy": "SlidingWindow", "triggerMessages": 6 }
}'
```
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-cakisma-2",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "harness": { "disableFileMemory": true },
  "memory": { "enableFileMemory": true }
}'
```
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-cakisma-3",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "harness": { "disableTodoProvider": true },
  "memory": { "enableTodo": true }
}'
```

**Beklenen sonuç** (🚨 doküman düzeltildi, koşum 2026-09-17 ap-s3 — kaynak
İngilizce'dir, K-228)
- Adım 1: `valid: false`, mesaj tam olarak: `"Agent 'manuel-cakisma-1'
  wants compaction, but HarnessSettings.DisableCompaction is turned
  off."`
- Adım 2: `valid: false`, mesaj tam olarak: `"Agent 'manuel-cakisma-2'
  wants file memory, but HarnessSettings.DisableFileMemory is turned
  off."`
- Adım 3: `valid: false`, mesaj tam olarak: `"Agent 'manuel-cakisma-3'
  wants todo tracking, but HarnessSettings.DisableTodoProvider is turned
  off."`

### MT-MEM-006 — `SlidingWindow` gerçek konuşmada tetiklenir; `HistoryCompacted` olayı üretilir

Faz 13'ün kendi "Gerçek kanıt" ölçümüyle AYNI eşik ve tur sayısı (bkz. dosya
başındaki `FIX-MEM-COMPACT-01`) — orada `echo` sağlayıcısıyla (ağa çıkmadan)
ölçülmüştü, burada gerçek OpenAI ile tekrarlanır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-104, K-106, K-109 |

**Ön koşul**
- `FIX-MEM-COMPACT-01` kaydedildi.

**Adımlar**
1. `manuel-sikistir` agent'ını kaydet.
2. Aynı `sessionId` ile **5 ayrı turda** kısa mesajlar gönder (her turu ayrı
   bir `curl` çağrısı, sırayla).
3. Son run'ın olay listesini oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-sikistir",
  "instructions": "Kisa yanit ver, en fazla 5 kelime kullan.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "SlidingWindow", "triggerMessages": 6, "minimumPreservedTurns": 1 }
}'
```
```bash
for i in 1 2 3 4 5; do
  curl -s -X POST "$APU/api/agents/manuel-sikistir/run" -H "$APB" \
       -H "Idempotency-Key: manuel-mem-006-tur-$i-$(date +%s%N)" \
       -H "content-type: application/json" \
       -d "{\"message\":\"Tur $i: bir kelimeyle cevap ver.\",\"sessionId\":\"mem-sikistir-01\"}" \
       | python3 -c "import json,sys; d=json.load(sys.stdin); print('runId=', d.get('runId'), 'text=', d.get('text'))"
done
```
```bash
# En son yazdirilan runId ile:
curl -s "$APU/api/runs/<son-runId>/events" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- 5 turun tamamı `HTTP 200` döner (JSON, akışsız — `Idempotency-Key` başlığı
  nedeniyle).
- En az bir run'ın olay listesinde `type: "HistoryCompacted"` bir satır
  vardır.
- O satırın `text` alanı `"N messages compacted"` biçimindedir (`N > 0`
  — 🚨 doküman düzeltildi, koşum 2026-09-17 ap-s3, kaynak İngilizce'dir,
  K-228) — **strateji `SlidingWindow` olsa bile** metin hep "compacted"
  der (`ObservedCompactionStrategy` tüm stratejiler için aynı sabit
  metni yazar — bu bir isimlendirme tuhaflığıdır, gerçek bir özetleme
  çağrısı OLMAYABİLİR; §MT-MEM-009 ile karşılaştırın).
- `payload` alanı `beforeMessages=`, `afterMessages=`, `beforeTokens=`,
  `afterTokens=` alanlarını taşır ve `afterMessages < beforeMessages`.

**Doğrulama sorgusu**

🚨 **Doküman notu (koşum, 2026-09-17, ap-s3):** `text`/`payload` sütunları
DB'de uygulama-seviyesi şifreli (`$apEnc` zarfı) — bu sorgu yalnız SATIR
SAYAR (`type=10` var/yok), içeriği OKUYAMAZ; içerik `GET
/api/runs/{id}/events` üzerinden okunmalı. Ayrıca kendi şerit şeması
(`mt_s3`) kullanılmalı, `tracon` DEĞİL.
```sql
SELECT r.id, e.seq, e.type, e.text, e.payload
FROM tracon.run_events e
JOIN tracon.runs r ON r.id = e.run_id
WHERE r.agent_name = 'manuel-sikistir' AND e.type = 10
ORDER BY e.created_at DESC;
```

---

### MT-MEM-007 — Sıkıştırılan mesajlar `conversation_items`'ta SİLİNMEZ (K-107)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-107 |

**Ön koşul**
- `MT-MEM-006` koşuldu (aynı `mem-sikistir-01` oturumu, sıkıştırma tetiklendi).

**Adımlar**
1. O oturuma ait konuşmanın **tüm** öğe sayısını say.

**Girilecek veri**
```sql
SELECT c.id AS conversation_id, count(ci.*) AS toplam_oge
FROM tracon.conversations c
JOIN tracon.conversation_items ci ON ci.conversation_id = c.id
WHERE c.tenant_id = 'default' AND c.agent_name = 'manuel-sikistir'
GROUP BY c.id;
```

**Beklenen sonuç**
- `toplam_oge`, 5 turun ürettiği TÜM mesajları (kullanıcı + asistan, ~10)
  kapsar — sıkıştırma modelin GÖRDÜĞÜ bağlamı küçültür, **saklanan geçmişi
  silmez**. `MT-MEM-006`'nın `payload.afterMessages` değeri bu sayıdan
  **küçük** olmalıdır (modelin gördüğü ile diskte saklanan farklıdır).

---

### MT-MEM-008 — `Summarization` stratejisi: özet sonrası token sayısı azalır, çalıştırma toplamı pozitif kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-108 |

**Ön koşul**
- `FIX-MEM-COMPACT-02` kaydedildi.

**Adımlar**
1. `manuel-ozetle` agent'ını kaydet.
2. Aynı `sessionId` ile 5 tur gönder (MT-MEM-006'daki döngüyle aynı desen).
3. Sıkıştırmanın tetiklendiği run'ı oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-ozetle",
  "instructions": "Kisa yanit ver, en fazla 5 kelime kullan.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "Summarization", "triggerMessages": 6, "minimumPreservedGroups": 2 }
}'
```
```bash
for i in 1 2 3 4 5; do
  curl -s -X POST "$APU/api/agents/manuel-ozetle/run" -H "$APB" \
       -H "Idempotency-Key: manuel-mem-008-tur-$i-$(date +%s%N)" \
       -H "content-type: application/json" \
       -d "{\"message\":\"Tur $i: bir kelimeyle cevap ver.\",\"sessionId\":\"mem-ozetle-01\"}" \
       | python3 -c "import json,sys; d=json.load(sys.stdin); print('runId=', d.get('runId'))"
done
```
```bash
curl -s "$APU/api/runs/<sikistirmali-runId>" -H "$APB" | python3 -m json.tool
curl -s "$APU/api/runs/<sikistirmali-runId>/events" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Olay listesinde `HistoryCompacted` vardır ve `payload.afterTokens <
  payload.beforeTokens` (Summarization gerçekten bir modele özetletir, salt
  atma değildir — `SlidingWindow`'dan farkı budur).
- `usage.totalTokens` alanı `NULL` değildir ve pozitiftir — özetleme
  çağrısının kendi token'ları `CompactionUsageTrackingChatClient` +
  `MergeUsage` ile çalıştırma toplamına **eklenmiştir** (K-108).

---

### MT-MEM-009 — `Pipeline` stratejisi uçtan uca çalışır ve çökmez

Sabit sıra (`ToolResult → SlidingWindow → Summarization`, K-109) birim
testlerle (`ObservedCompactionStrategy.Inner` üzerinden, `internal` erişim)
zaten doğrulanmıştır; bu case yalnız üç iç stratejinin ZİNCİRLEME
çalıştığını, hiçbirinin diğerini bozmadığını HTTP seviyesinde kanıtlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-109 |

**Ön koşul**
- `FIX-MEM-COMPACT-03` kaydedildi.

**Adımlar**
1. `manuel-pipeline` agent'ını kaydet.
2. Aynı desenle 5 tur gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-pipeline",
  "instructions": "Kisa yanit ver, en fazla 5 kelime kullan.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "compaction": { "strategy": "Pipeline", "triggerMessages": 6, "minimumPreservedGroups": 2, "minimumPreservedTurns": 1 }
}'
```
```bash
for i in 1 2 3 4 5; do
  curl -s -w " HTTP:%{http_code}\n" -X POST "$APU/api/agents/manuel-pipeline/run" -H "$APB" \
       -H "Idempotency-Key: manuel-mem-009-tur-$i-$(date +%s%N)" \
       -H "content-type: application/json" \
       -d "{\"message\":\"Tur $i: bir kelimeyle cevap ver.\",\"sessionId\":\"mem-pipeline-01\"}"
done
```

**Beklenen sonuç**
- 5 turun tamamı `HTTP:200`'dür, hiçbiri `5xx` vermez.
- Son run'ın olay listesinde en az bir `HistoryCompacted` olayı vardır.

---

### MT-MEM-010 — Sıkıştırma **kapalıyken** uzun konuşmada `HistoryCompacted` hiç üretilmez (kontrol grubu)

Sınır durumu / negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-106 (sıkıştırma varsayılan kapalı) |

**Ön koşul**
- `support` agent'ı (hazır, `Compaction` alanı **yok**).

**Adımlar**
1. Aynı oturumda 5 tur gönder (`Compaction` göndermeden).
2. Olay listesini oku.

**Girilecek veri**
```bash
for i in 1 2 3 4 5; do
  curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
       -H "Idempotency-Key: manuel-mem-010-tur-$i-$(date +%s%N)" \
       -H "content-type: application/json" \
       -d "{\"message\":\"Tur $i: bir kelimeyle cevap ver.\",\"sessionId\":\"mem-kontrol-01\"}"
done
```

**Beklenen sonuç**
- Hiçbir run'ın olay listesinde `HistoryCompacted` **görünmez** — 5 turluk
  kısa bir konuşma, `Compaction` tanımsız bir agent'ı sıkıştırmaya
  zorlamaz (K-106: sıkıştırma yalnız açıkça istenirse çalışır).

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM tracon.run_events e
JOIN tracon.runs r ON r.id = e.run_id
WHERE r.agent_name = 'support' AND r.session_id = 'mem-kontrol-01' AND e.type = 10;
-- beklenen: 0
```

### MT-MEM-011 — `EnableFileMemory`: agent turlar arası bir notu dosyaya yazıp geri okuyabilir

Metin eşleşmesi burada **istisnaen** kabul edilir (kural 4.1'in "sipariş
numarası" örneğiyle aynı sınıf: rastgele, benzersiz bir işaretçi dizgisi,
tam cümle değil).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-062 |

**Ön koşul**
- `FIX-MEM-FILE-01` kaydedildi.

**Adımlar**
1. `manuel-dosya-bellek` agent'ını kaydet.
2. 1. turda benzersiz bir işaretçiyi hatırlamasını iste.
3. 2. turda (AYNI oturum) işaretçiyi geri sor.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-dosya-bellek",
  "instructions": "Kullanici bir bilgiyi hatirlamani isterse dosya bellegine kisa bir not olarak yaz. Sorulduğunda dosyadan okuyup cevapla.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "memory": { "enableFileMemory": true }
}'
```
```bash
curl -s -X POST "$APU/api/agents/manuel-dosya-bellek/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-011-t1-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"Bunu dosyaya kaydet: kayit-kodu FILE-7841.","sessionId":"mem-dosya-01"}'
```
```bash
curl -s -X POST "$APU/api/agents/manuel-dosya-bellek/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-011-t2-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"Kayit kodu neydi?","sessionId":"mem-dosya-01"}'
```

**Beklenen sonuç**
- 2. turun yanıt metni `FILE-7841` dizgisini içerir.

---

### MT-MEM-012 — `EnableTodo`: agent bir todo listesi oluşturur ve kalan kalemleri hatırlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-dosya-bellek`'e benzer, `memory.enableTodo: true` olan geçici bir
  agent (`manuel-todo`) kaydedilir; `instructions`: `"Kullanicinin istedigi
  gorevleri bir todo listesi olarak takip et."`.

**Adımlar**
1. 1. turda iki görev iste ("raporu yaz", "sunumu hazirla").
2. 2. turda (AYNI oturum) kalan görevleri sor.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/manuel-todo/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-012-t1-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"Iki gorevim var: raporu yaz, sunumu hazirla. Todo listesine ekle.","sessionId":"mem-todo-01"}'
```
```bash
curl -s -X POST "$APU/api/agents/manuel-todo/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-012-t2-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"Todo listemde neler var?","sessionId":"mem-todo-01"}'
```

**Beklenen sonuç**
- 2. turun yanıtı her iki görevi de (raporu/sunumu ima eden bir biçimde)
  anar. Tam cümle karşılaştırması **yapılmaz**; yalnız iki konunun da
  yanıtta geçtiği doğrulanır (gevşek kontrol — `TodoProvider`'ın modele nasıl
  bir bağlam enjekte ettiği kod okumasıyla doğrulanmadı, bu case koşumda
  gözlemlenen gerçek davranışı kaydeder).

---

### MT-MEM-013 — `EnableTextSearch`: agent dosya belleğinde arama yapıp sonucu kullanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K-110 |

**Ön koşul**
- `MT-MEM-011` koşuldu (`manuel-dosya-bellek` `mem-dosya-01` oturumunda
  `FILE-7841` içeren bir dosya yazdı — **aynı paylaşılan `AgentFileStore`**).
- `FIX-MEM-FILE-02` kaydedildi.

**Adımlar**
1. `manuel-dosya-arama` agent'ını (FARKLI bir agent, FARKLI bir oturum)
   çalıştır ve `FILE-7841` ile ilgili bir arama iste.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-dosya-arama",
  "instructions": "Kullanicinin sordugu konuyu dosya belleginde ara ve bulduğun iceriği ozetle.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "memory": { "enableTextSearch": true }
}'
```
```bash
curl -s -X POST "$APU/api/agents/manuel-dosya-arama/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-013-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"FILE-7841 ile ilgili bir kayit var mi? Ara ve bul.","sessionId":"mem-arama-01"}'
```

**Beklenen sonuç**

🚨 **Doküman düzeltildi (koşum, 2026-09-17, ap-s3) — spec'in "2026-08-10
durumu" notu artık BAYAT.** `docs/arsiv/PLANA-DONUSEN-ADAYLAR.md:363`:
`F-105 · Dosya belleği kiracı-içi sınırı — ✅ KAPATILDI (2026-08-18)`.
Yani spec'in kendi notunun yazıldığı 2026-08-10'dan SEKİZ gün sonra,
aynı kiracı İÇİNDEKİ ajan sınırı da kapatılmış:
`TenantPrefixingAgentFileStore`'un öneki artık `/{tenantId}/{agentName}/...`
(yalnız `/{tenantId}/...` değil — kaynağın kendi belgesi,
`TenantPrefixingAgentFileStore.cs:20-23`, bunu doğruluyor). Sonuç: ADIM 1'in
ORİJİNAL iddiası (`manuel-dosya-arama`, `manuel-dosya-bellek`'in dosyasını
BULUR) artık **yanlış**; spec'in 2026-08-10 düzeltmesi de (`"Kaldı, F-105'e
bağlı"` beklentisi) artık **bayat** — F-105 kapandığı için bu case ARTIK
GERÇEKTEN GEÇMELİDİR (izolasyon çalışır, sızıntı YOKTUR).

- Yanıt `FILE-7841` dizgisini İÇERMEMELİDİR — `manuel-dosya-arama`,
  `manuel-dosya-bellek`'in dosyasına erişememelidir (ajan-düzeyi izolasyon).
- Bu davranışın gerçekten çalıştığını GÖSTERMEK için AYNI agent'ın hem
  yazıp hem araması gerekir (`manuel-dosya-hem` gibi, `enableFileMemory`
  VE `enableTextSearch` ikisi birden) — farklı bir oturumda yazdığı notu
  kendi text search'üyle bulabilmesi, mekanizmanın kendisinin ÇALIŞTIĞININ
  kanıtıdır (yalnız cross-agent izolasyonun kör bırakmadığının kanıtı).

---

### MT-MEM-014 — `TextSearchProvider` artık kiracılar arası ARAMAZ — düzeltilmiş kiracı yalıtımını doğrular

**Düzeltilmiş kusur (2026-08-10).** Bu case önceden "KRİTİK ŞÜPHE" olarak
yazılmıştı: `AgentDefinitionCompiler.SearchFileStoreAsync`
(`AgentDefinitionCompiler.cs`) `TextSearchProvider`'ın arama callback'ini
`fileStore.SearchAsync("/", ...)` ile HER ZAMAN kök dizinden kuruyordu ve
paylaşılan `AgentFileStore` tek bir süreç-çapında singleton olduğu için hiçbir
`tenant_id` filtresi taşımıyordu. Kod okumasıyla doğrulandı ve düzeltildi:
`RequireFileStore` artık paylaşılan depoyu her kiracı için
`TenantPrefixingAgentFileStore` ile sarmalıyor (öneki artık
`/{tenantId}/{agentName}/...` — 🚨 doküman düzeltildi, koşum 2026-09-17
ap-s3: F-105 de `✅ KAPATILDI (2026-08-18)`, ajan adı da öneke eklendi,
yalnız `/{tenantId}/...` değil) — hem yazma (`FileMemoryProvider`) hem
okuma (`TextSearchProvider`) tarafı artık hem kendi kiracısının hem kendi
agent'ının izole alt ağacında çalışıyor. Bu case düzeltilmiş kiracı
davranışını doğrular; ajan-içi sınır `MT-MEM-013`'te ayrıca doğrulanır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 13 |
| **İlgili karar** | K1 (sıfır sürpriz), kiracı yalıtımı ilkesi |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md`'nin tenancy açma deseni (§3 ön koşulu) bilinir:
  ```bash
  dotnet user-secrets set "Tracon:Tenancy:Enabled" "true"
  dotnet user-secrets set "Tracon:Tenancy:AllowHeaderResolution" "true"
  ```
  Uygulama yeniden başlatılır.
- `manuel-dosya-bellek` ve `manuel-dosya-arama` kayıtlı (`MT-MEM-011`/`013`).

**Adımlar**
1. `X-Tracon-Tenant: kiraci-alfa` başlığıyla `manuel-dosya-bellek`'i
   çalıştır, benzersiz bir işaretçi yazdır.
2. `X-Tracon-Tenant: kiraci-beta` başlığıyla (FARKLI kiracı, FARKLI
   oturum) `manuel-dosya-arama`'yı çalıştır ve aynı işaretçiyi ara.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/manuel-dosya-bellek/run" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa" \
     -H "Idempotency-Key: manuel-mem-014-yaz-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"Bunu dosyaya kaydet: gizli-anahtar SIZINTI-9902.","sessionId":"mem-sizinti-alfa"}'
```
```bash
curl -s -X POST "$APU/api/agents/manuel-dosya-arama/run" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-beta" \
     -H "Idempotency-Key: manuel-mem-014-ara-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"SIZINTI-9902 ile ilgili bir kayit var mi? Ara ve bul.","sessionId":"mem-sizinti-beta"}'
```

**Beklenen sonuç**
- Yanıt `SIZINTI-9902` dizgisini İÇERMEZ — `kiraci-beta`'nın agent'ı
  `kiraci-alfa`'nın dosya belleğine erişemez.
- Bu gözlemlenMEZse (yanıt `SIZINTI-9902` içerirse): fix'in regresyonu —
  **Kusur, Önem: Kritik**, `00-INDEKS.md` §5'in yayın durduran eşiğine girer.
  Hemen `TenantPrefixingAgentFileStore`'un `Rewrite`/`StripPrefix`
  mantığından şüphelenilmelidir.

### MT-MEM-015 — Düz metinle belge yükleme: sunucu parçalar ve gömüler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K-341 |

**Ön koşul**
- PostgreSQL (`pgvector` uzantılı) ve OpenAI anahtarı aktif.

**Adımlar**
1. `FIX-MEM-DOC-01`'i `FIX-MEM-COLLECTION-01` koleksiyonuna yükle.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "sourceId": "izin-notu",
  "text": "Yillik izin 14 gundur. Bes yildan sonra 20 gune cikar."
}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- Gövde: `{"sourceId":"izin-notu","chunkCount":1}` (metin `ChunkSize=1000`
  karakterin altında, tek parça bekleniyor).

**Doğrulama sorgusu**
```sql
SELECT source_id, chunk_index, length(content), created_at
FROM tracon.document_embeddings
WHERE tenant_id = 'default' AND collection = 'manuel-bilgi';
```

---

### MT-MEM-016 — `GET .../documents` yüklenen kaynağı listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-MEM-015` koşuldu.

**Adımlar**
1. Koleksiyondaki kaynakları listele.

**Girilecek veri**
```bash
curl -s "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB"
```

**Beklenen sonuç**
- `["izin-notu"]` — plandaki taslak arayüzde YOKTU, gerçekleşen
  `IVectorSearchStore.ListSourcesAsync` bu uç için sonradan eklendi
  (Faz 51 doc, "Plandan Sapmalar" #4).

---

### MT-MEM-017 — Anlamsal arama KELİME EŞLEŞMESİ OLMAYAN bir sorguyla doğru parçayı bulur

Faz 51'in kendi gerçek kanıtıyla AYNI örnek (`FIX-MEM-QUERY-01`, "tatil"
kelimesi belgede hiç geçmez).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K-341 |

**Ön koşul**
- `MT-MEM-015` koşuldu.

**Adımlar**
1. `FIX-MEM-QUERY-01` ile ara.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/knowledge/manuel-bilgi/search" -H "$APB" \
     -H "content-type: application/json" -d '{"query":"tatil hakkim ne kadar","top":3}'
```

**Beklenen sonuç**
- Sonuç dizisi **boş değildir**.
- İlk sonucun `sourceId` alanı `"izin-notu"`dur.
- `distance` alanı `2.0`'dan küçüktür (kosinüs mesafesi; tam eşik koşumda
  gözlemlenip kaydedilir — Faz 51'in kendi ölçümü `0.241` idi).

---

### MT-MEM-018 — `DELETE .../documents/{sourceId}` kaynağı siler; sonraki arama onu döndürmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-MEM-015` koşuldu.

**Adımlar**
1. `izin-notu` kaynağını sil.
2. Aynı sorguyla tekrar ara.
3. Listeyi tekrar al.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/knowledge/manuel-bilgi/documents/izin-notu" -H "$APB"
curl -s -X POST "$APU/api/knowledge/manuel-bilgi/search" -H "$APB" \
     -H "content-type: application/json" -d '{"query":"tatil hakkim ne kadar"}'
curl -s "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 204`.
- Adım 2: boş dizi `[]`.
- Adım 3: boş dizi `[]`.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM tracon.document_embeddings
WHERE tenant_id = 'default' AND collection = 'manuel-bilgi' AND source_id = 'izin-notu';
-- beklenen: 0
```

---

### MT-MEM-019 — Hazır `chunks` ile (embedding VERİLMİŞ) yükleme: sunucu yeniden gömmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | Açık Soru 4 (§51) |

**Ön koşul**
- PostgreSQL aktif. `TraconKnowledgeOptions.Dimensions` varsayılanı
  `1536`'dır — bu case boyutu **doğru** vermelidir (yanlış boyut için bkz.
  `MT-MEM-022`).

**Adımlar**
1. 1536 uzunluklu, elle üretilmiş bir embedding dizisiyle (tekrarlayan sabit
   değer, gerçek bir model çağrısı GEREKMEZ — İzlek C) yükle.

**Girilecek veri**
```bash
python3 -c "
import json
emb = [0.001] * 1536
print(json.dumps({'sourceId':'hazir-parca','chunks':[{'index':0,'content':'Elle gomulu test parcasi.','embedding':emb}]}))
" > /tmp/manuel-mem-019.json

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/manuel-mem-019.json
```

**Beklenen sonuç**
- `HTTP: 200`, `{"sourceId":"hazir-parca","chunkCount":1}`.
- Hiçbir embedding API çağrısı yapılmaz (gövdede zaten dolu) —
  `KnowledgeIngestionService.EmbedMissingAsync` yalnız BOŞ embedding'li
  parçaları gömer.

---

### MT-MEM-020 — Aynı `sourceId` ile yeniden yükleme ESKİ parçaları değiştirir (upsert)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-MEM-015` benzeri bir belge (`tekrar-notu`) yüklü: `text: "Ilk surum
  metni."`.

**Adımlar**
1. `tekrar-notu`'yu ilk metinle yükle.
2. AYNI `sourceId` ile FARKLI bir metinle yeniden yükle.
3. Parça sayısını kontrol et.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" -H "content-type: application/json" \
     -d '{"sourceId":"tekrar-notu","text":"Ilk surum metni."}'
curl -s -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" -H "content-type: application/json" \
     -d '{"sourceId":"tekrar-notu","text":"Ikinci surum metni, tamamen farkli icerik."}'
```

**Beklenen sonuç**
- İki çağrı da `200` döner.

**Doğrulama sorgusu**
```sql
SELECT chunk_index, content FROM tracon.document_embeddings
WHERE tenant_id = 'default' AND collection = 'manuel-bilgi' AND source_id = 'tekrar-notu';
-- beklenen: TEK satir, icerik "Ikinci surum..." ile baslar; "Ilk surum" YOKTUR
```

---

### MT-MEM-021 — Hem `text` hem `chunks` birlikte gönderilirse `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL aktif.

**Adımlar**
1. `text` VE `chunks` alanlarını birlikte gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "sourceId": "cakisma-test",
  "text": "Bir metin.",
  "chunks": [{"index":0,"content":"Bir parca."}]
}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `detail` tam olarak: `Ya text ya da chunks verilmelidir; ikisi birden ya da
  hicbiri olamaz.`

---

### MT-MEM-022 — Ne `text` ne `chunks` gönderilirse `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Adımlar**
1. İkisini de vermeden yükle.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d '{"sourceId": "bos-test"}'
```

**Beklenen sonuç**
- `HTTP: 400`, aynı `detail` metni (`MT-MEM-021` ile aynı kural, ters uç).

---

### MT-MEM-023 — Geçersiz koleksiyon adı `400` verir

Negatif senaryo. `[a-zA-Z0-9_-]+` dışındaki karakterler reddedilir —
koleksiyon adı sorguya PARAMETRE olarak geçse de arayüzden gelebileceği için
serbest metin kabul edilmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | Açık Soru 6 (§51) |

**Adımlar**
1. Boşluk içeren bir koleksiyon adına yükle.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/kurumsal%20bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d '{"sourceId":"x","text":"deneme"}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `detail` tam olarak: `'kurumsal bilgi' gecerli bir koleksiyon adi degil.
  Yalniz harf, rakam, alt cizgi ve tire icerebilir.`

---

### MT-MEM-024 — Yanlış boyutlu hazır embedding `400` verir

Negatif senaryo, İzlek C (model çağırmaz).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Adımlar**
1. Depo boyutundan (1536) çok küçük bir embedding (2 eleman) gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "sourceId": "yanlis-boyut",
  "chunks": [{"index":0,"content":"a","embedding":[0.1,0.2]}]
}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `detail` tam olarak: `Parca 0 gomu uzunlugu (2) depo boyutuyla (1536)
  eslesmiyor.`
- `document_embeddings`'te `yanlis-boyut` için hiçbir satır yazılmaz (kısmi
  yazma yok — `UpsertAsync` tek transaction).

---

### MT-MEM-025 — PostgreSQL kapalıyken (bellek içi/SQLite) bilgi tabanı uçları `501` verir

Negatif senaryo / sınır durumu. `KnowledgeIngestionService.IsSupported`
`false` iken sessizce boş sonuç DÖNMEZ (K1).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K4, K-343 |

**Ön koşul**
```bash
dotnet user-secrets remove "Tracon:PostgreSql:ConnectionString"
```
Uygulama bellek içi depolarla yeniden başlatılır (`UsePostgreSql()`
çağrılmaz, dolayısıyla `IVectorSearchStore` **hiç** kayıtlı olmaz — bu bayrak
bellek içi/SQLite/SQL Server'ın hepsi için aynı sonucu verir, K-343).

**Adımlar**
1. Belge yüklemeyi dene.
2. Listelemeyi dene.
3. Aramayı dene.
4. Silmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "content-type: application/json" -d '{"sourceId":"x","text":"deneme"}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/manuel-bilgi/search" -H "$APB" \
     -H "content-type: application/json" -d '{"query":"deneme"}'
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/knowledge/manuel-bilgi/documents/x" -H "$APB"
```

**Beklenen sonuç**
- Dördü de `HTTP: 501`.
- `title` tam olarak: `Bilgi tabani desteklenmiyor`.
- `detail` tam olarak: `Bir IVectorSearchStore (bugun yalniz PostgreSQL:
  UsePostgreSql()) VE bir IEmbeddingGenerator<string, Embedding<float>>
  birlikte kayitli olmalidir.`

**Ön koşulu geri al**
```bash
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" \
  "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon"
```

### MT-MEM-026 — `knowledge-assistant` uçtan uca: belge yükle → soru sor → `search_knowledge` tam bir kez çağrılır

Faz 51'in kendi gerçek kanıtıyla aynı akış; hazır fixture (`knowledge-assistant`,
`00-INDEKS.md` §3.1) `kurumsal` koleksiyonuna bağlıdır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K-341 |

**Ön koşul**
- `knowledge-assistant` katalogda görünüyor (`GET /api/agents`), PostgreSQL +
  OpenAI anahtarı aktif.

**Adımlar**
1. `kurumsal` koleksiyonuna bir belge yükle.
2. `knowledge-assistant`'na kelime eşleşmesi olmayan bir soru sor.
3. Run'ın tool çağrılarını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/knowledge/kurumsal/documents" -H "$APB" -H "content-type: application/json" \
     -d '{"sourceId":"izin-politikasi","text":"Yillik izin 14 gundur. Bes yildan sonra 20 gune cikar."}'

curl -s -X POST "$APU/api/agents/knowledge-assistant/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-026-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"Kac gun tatilim var?","sessionId":"mem-rag-01"}' \
     | python3 -c "import json,sys; d=json.load(sys.stdin); print('runId=',d.get('runId')); print(d.get('text'))"
```

**Beklenen sonuç**
- Yanıt `14` dizgisini içerir.
- `GET /api/runs/{runId}/events` çıktısında `tool_name: "search_knowledge"`
  taşıyan bir `ToolInvoked` olayı **tam bir kez** görünür.

**Doğrulama sorgusu**
```sql
SELECT tool_name, count(*) FROM tracon.tool_invocations
WHERE run_id = '<runId>' GROUP BY tool_name;
```

---

### MT-MEM-027 — `VectorCollection` boş bırakılırsa koleksiyon adı olarak AGENT ADI kullanılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL + OpenAI anahtarı aktif.

**Adımlar**
1. `VectorCollection` **vermeden** `EnableVectorSearch: true` bir agent
   kaydet.
2. Agent'ın KENDİ ADIYLA aynı koleksiyona bir belge yükle.
3. Agent'a onunla ilgili bir soru sor.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-varsayilan-koleksiyon",
  "instructions": "search_knowledge tool unu kullanarak cevap ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "memory": { "enableVectorSearch": true }
}'
curl -s -X POST "$APU/api/knowledge/manuel-varsayilan-koleksiyon/documents" -H "$APB" \
     -H "content-type: application/json" -d '{"sourceId":"n1","text":"Ofis WiFi sifresi: bulut-42."}'
curl -s -X POST "$APU/api/agents/manuel-varsayilan-koleksiyon/run" -H "$APB" \
     -H "Idempotency-Key: manuel-mem-027-$(date +%s%N)" -H "content-type: application/json" \
     -d '{"message":"WiFi sifresi nedir?","sessionId":"mem-varsayilan-01"}'
```

**Beklenen sonuç**
- Yanıt `bulut-42` dizgisini içerir — `VectorSearchToolFactory`'ye geçirilen
  koleksiyon, `definition.Name` (`manuel-varsayilan-koleksiyon`) olmuştur.

---

### MT-MEM-028 — `EnableVectorSearch=true` + `IVectorSearchStore` kayıtlı değil → derleme hatası, sessizce boş sonuç DÖNMEZ

Negatif senaryo. `/validate` ile (model çağırmaz).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K4, K-343 |

**Ön koşul**
```bash
dotnet user-secrets remove "Tracon:PostgreSql:ConnectionString"
```
Uygulama bellek içi depoyla yeniden başlatılır.

**Adımlar**
1. `EnableVectorSearch: true` bir tanımı doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-vektor-yok",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "memory": { "enableVectorSearch": true }
}'
```

**Beklenen sonuç**
- `valid: false`.
- `messages[0].message` tam olarak: `'manuel-vektor-yok' agent'i anlamsal
  arama istiyor ancak IVectorSearchStore kayitli degil (bugun yalniz
  PostgreSQL: UsePostgreSql()).`

**Ön koşulu geri al**
```bash
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" \
  "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon"
```

---

### MT-MEM-029 — `EnableVectorSearch=true` + PostgreSQL var ama `IEmbeddingGenerator` kayıtlı değil → derleme hatası

Negatif senaryo. `IEmbeddingGenerator`, örnek uygulamada yalnız
`openAiEnabled` iken kaydedilir (`Program.cs:142-145`) — OpenAI anahtarı
boşaltılırsa PostgreSQL kayıtlı olsa BİLE bu kayıt hiç oluşmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets remove "Tracon:Providers:OpenAI:ApiKey"
```
Uygulama yeniden başlatılır (PostgreSQL bağlantısı KALIR).

**Adımlar**
1. `EnableVectorSearch: true` bir tanımı doğrula — model sağlayıcısı
   olarak KAYITLI KALAN bir şey seçilemeyeceği için (OpenAI kapalı) bu
   agent'ın modeli de `echo` olmalıdır (örnek uygulama bu modda `echo`'yu
   kaydeder, `Program.cs:149`).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-gomu-yok",
  "model": { "provider": "echo", "model": "echo-1" },
  "memory": { "enableVectorSearch": true }
}'
```

**Beklenen sonuç**
- `valid: false`.
- `messages[0].message` tam olarak: `'manuel-gomu-yok' agent'i anlamsal
  arama istiyor ancak IEmbeddingGenerator<string, Embedding<float>> kayitli
  degil.`

**Ön koşulu geri al**
```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "<OPENAI_ANAHTARINIZ>"
```

### MT-MEM-030 — Vektör arama kiracı yalıtımı: bir kiracının belgesi diğerinde görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K-343, Faz 41 sözleşme testi |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Tenancy:Enabled" "true"
dotnet user-secrets set "Tracon:Tenancy:AllowHeaderResolution" "true"
```
Uygulama yeniden başlatılır.

**Adımlar**
1. `kiraci-alfa` başlığıyla bir belge yükle.
2. `kiraci-beta` başlığıyla aynı koleksiyonda ara.
3. Kontrol: `kiraci-alfa` başlığıyla ARAMA yaparak belgenin ORADA
   bulunduğunu doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/knowledge/manuel-bilgi/documents" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" \
     -d '{"sourceId":"alfa-belge","text":"Bu belge yalnizca kiraci-alfaya aittir, gizli-kod XYZ."}'

curl -s -X POST "$APU/api/knowledge/manuel-bilgi/search" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-beta" -H "content-type: application/json" \
     -d '{"query":"gizli kod nedir"}' | python3 -m json.tool

curl -s -X POST "$APU/api/knowledge/manuel-bilgi/search" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" \
     -d '{"query":"gizli kod nedir"}' | python3 -m json.tool
```

**Beklenen sonuç**
- Adım 2 (`kiraci-beta`): boş dizi `[]`.
- Adım 3 (`kiraci-alfa`, kontrol grubu): `alfa-belge` sonuç kümesinde
  görünür.

**Doğrulama sorgusu**
```sql
SELECT tenant_id, source_id FROM tracon.document_embeddings
WHERE collection = 'manuel-bilgi' AND source_id = 'alfa-belge';
-- beklenen: tek satir, tenant_id = 'kiraci-alfa'
```

### MT-MEM-031 — 🚨 `KnowledgeEndpoints` hiçbir ucunda `RequireApiKeyScope` çağırmıyor — yalnız-okuma anahtarı belge yazabiliyor/silebiliyor mu?

Şüpheli davranış — koddan ölçüldü, koşumda doğrulanacak/çürütülecek.
`grep -n "RequireApiKeyScope" src/Tracon.AspNetCore/Endpoints/KnowledgeEndpoints.cs`
**boş** döner — karşılaştırma: `AgentEndpoints.cs`/`RunEndpoints.cs` her
uca `RequireApiKeyScope(ApiKeyScope.AgentsAdmin/RunsWrite/...)` ekler.
Bu, `WorkflowEndpoints` (`15-WORKFLOWS.md` `MT-WF-100`), `SchedulingEndpoints`
(`16-IS-KUYRUGU-VE-ZAMANLAMA.md` `MT-JOB-090`), Eval/Experiment yüzeyinin
(`17-EVAL-VE-DENEYLER.md` `MT-EVAL-100`/`101`, farklı kök nedenle: kapsam
değeri hiç tanımlı değil) ve `GovernanceEndpoints`'in (`18-MCP-VE-A2A.md`
`MT-MCP-051`/`052`) ardından **beşinci bilinen** tekrardır (önceki
notlardaki sayaç `00-INDEKS.md` içinde tutarsız ilerliyor — bu doküman kesin
bir sıra numarası iddia etmez, yalnız listelenen dört önceki örneğe ek
BİR tekrar daha olduğunu kaydeder) — sistematik bir denetim boşluğuna
işaret eder.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51, 53 |
| **İlgili karar** | K-… (53.3, kapsam ∩ rol ilkesi) |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md` `MT-SEC-050`'nin API anahtarı oluşturma deseni
  bilinir.

**Adımlar**
1. Yalnız `RunsRead` kapsamıyla bir API anahtarı üret.
2. Bu anahtarla bir belge YAZMAYI dene.
3. Aynı anahtarla o belgeyi SİLMEYİ dene.
4. Karşılaştırma: aynı anahtarla `AgentsAdmin` gerektiren bir agent ucuna
   (`PUT /api/agents/{name}`) yaz, `403` aldığını doğrula (kontrol grubu).

**Girilecek veri**
```bash
KEY_JSON=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "mem-kapsam-testi", "scopes": ["RunsRead"] }')
echo "$KEY_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['rawKey'])"
export MEMKEY="Authorization: Bearer <yukaridaki-rawKey>"

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/kapsam-testi/documents" -H "$MEMKEY" \
     -H "content-type: application/json" -d '{"sourceId":"x","text":"kapsam testi"}'

curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/knowledge/kapsam-testi/documents/x" -H "$MEMKEY"

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/kapsam-kontrol" -H "$MEMKEY" \
     -H "content-type: application/json" -d '{"name":"kapsam-kontrol","instructions":"test"}'
```

**Beklenen sonuç (şüphe)**
- Adım 2 ve 3: `HTTP: 200`/`204` — kapsam kısıtı UYGULANMAZ (kodun okuduğu
  hâliyle beklenen).
- Adım 4: `HTTP: 403` — kontrol grubu, kapsam sisteminin `AgentEndpoints`'te
  çalıştığını ama `KnowledgeEndpoints`'te HİÇ devrede olmadığını gösterir.
- Doğrularsa: **Kusur, Önem: Yüksek** — bir okuma-amaçlı otomasyon anahtarı
  bilgi tabanı içeriğini yazabilir/silebilir (embedding maliyeti de dahil).
  Çürürse not güncellenir.
