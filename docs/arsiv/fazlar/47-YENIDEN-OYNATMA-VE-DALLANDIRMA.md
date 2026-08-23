# Faz 47 — Yeniden Oynatma ve Konuşma Dallandırma

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-54**, **F-66** (birleşti)
> **Önkoşul:** Yok. [Faz 46](46-DAYANIKLI-CALISTIRMA.md) biterse yeniden oynatma `202` ile kuyruğa alınabilir — zorunlu değildir
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli** — bir tablo + iki sütun, üç set, numaralar uygulama anında alınır (K-178)
> **Public API:** büyüyor — bir arayüz, iki enum, dört kayıt tipi. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

İki soru bugün cevapsızdır: 1. *"Üretimdeki şu hatayı, düzelttiğim talimatla tekrar çalıştır."* — Girdiyi elle kopyalamak gerekir. 2. *"Şu mesajı düzeltip oradan devam et."* — Konuşma append-only'dir; geri dönüş yoktur. İkisi de aynı işi ister: **kayıtlı bir noktadan yeniden başlamak.** Bu yüzden tek fazdır.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 Çalıştırma girdisi `run_inputs`'a **polimorfik içeriğiyle birlikte**
      yazılır ve aynen geri okunur (`json`, `jsonb` değil — K-027)
- [x] `POST /api/runs/{runId}/replay` yeni bir çalıştırma açar; yeni satır
      `ReplayOfRunId` taşır ve kaynak çalıştırma **değişmez**
- [x] `ReplayTools` modunda **hiçbir tool gerçekten koşmaz**; kayıtlı sonuçlar
      döner. 🚨 `tool_invocations` yine de satır **alır** — gerekçe: Plandan
      Sapmalar S5. Gövdenin koşmadığı sayaçla kanıtlanır
- [x] 🚨 Eşleşmeyen tool çağrısında `422` döner ve hangi tool olduğu yazar
- [x] 🚨 Onay gerektiren tool + `LiveTools` → `409`
- [x] `LiveTools` `Operator` ile `403`, `Admin` ile geçer
- [x] Farklı `agentVersion` ve `modelId` ile oynatma o sürümü/modeli kullanır
- [x] `POST /api/sessions/{sessionId}/branch` yeni oturum açar; öğeler
      `UpToSequence`'a kadar kopyalanır
- [x] 🚨 Dala yazmak ana konuşmayı değiştirmez; `SqlChatHistoryProvider`
      **kod değişmeden** dalı okur
- [x] Ana konuşma silinince dal yaşar
- [x] Kiracı sınırı hem oynatmada hem dallandırmada korunur
- [x] `run_inputs` bir saklama hedefidir; `GET /api/retention/run_inputs` yanıt verir
- [x] Sözleşme testleri bellek içi + PostgreSQL + SQLite'ta geçer. 🚨 SQL Server
      bu makinede **koşturulamadı**: `mcr.microsoft.com/mssql/server` yalnız
      `linux/amd64`'tür (bilinen açık kalem, `AGENTS.md`). 431 testin tamamı
      container başlatma zaman aşımıyla düşer; kod yolu paylaşılan katmandadır
      ve PostgreSQL/SQLite ile aynıdır
- [x] Migration üç sette de uygulandı (K-178)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — çıktılar aşağıda
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle **151,3 → 159,3 KB gzip** (bu fazın payı
      **8,0 KB**; plan 3–5 KB tahmin etmişti). Bütçe 250 KB, kalan pay 90,7 KB


### Örnek uygulamayla ölçülen gerçek çıktı (2026-08-07)

`samples/AgentPrism.Api`, SQLite kalıcılığı ve gerçek OpenAI çağrılarıyla.

| Adım | Sonuç |
|---|---|
| Girdi kaydı (`GET /api/runs/{id}/input`) | `{"role":"user","contents":[{"$type":"text",...}]}` — 🚨 `$type` **ilk özellik**, K-027 korunuyor |
| Kaynak çalıştırmanın tool çağrısı | `get_order_status` / `orderId=ORD-42` |
| `ReplayTools` ile oynatma | `200`; çıktı kayıtlı sonuçtan geldi ("kargoya verildi. Tahmini teslim: 2 gün") |
| Soy bağı | `{"id":"019fdc4f-ff00…","replayOfRunId":"019fdc4f-b2a8…","status":"Completed"}` — kaynak **değişmedi** |
| `compare` | kaynak `1` tool / `450` token / `4428 ms` ↔ oynatma `1` tool / `446` token / **`2215 ms`** (tool gövdesi koşmadığı için yarı sürede) |
| 🚨 **422 — gerçek senaryo** | Agent'ın talimatı değiştirildi (`her zaman ORD-1 ile çağır`), oynatma `orderId=ORD-1` üretti, kayıt `orderId=ORD-99` idi → `422`, `toolName: get_order_status`, `arguments: orderId=ORD-1` |
| `LiveTools` + onay gerektiren tool | `409` — `cancel_order` |
| Aynı agent `NoTools` ile | `200` |
| Kod agent'ı (`support`) + `ReplayTools` | `400` — açık gerekçeli |
| Erişilemeyen modelle oynatma | `502` (🚨 ilk yazımda **500**'dü — aşağıya bakın) |
| Dallandırma | `{"branchFromSequence":1,"copiedItemCount":2}` |
| Dalın geçmişi | 2 mesaj; `SqlChatHistoryProvider` **kod değişmeden** okudu |
| Dala yazdıktan sonra | ana konuşma **4** mesajda kaldı, dal **4** mesaja çıktı — bağımsız |
| Saklama hedefi | `PUT /api/retention/run_inputs` `200`; bilinmeyen hedef `400` |

🚨 **Örnek uygulamanın yakaladığı gerçek hata (birim testleri kaçırdı).**
Yeniden oynatma ucu ilk yazımda sağlayıcı hatalarını
`AgentPrismException or InvalidOperationException or HttpRequestException`
listesiyle yakalıyordu. Gerçek bir OpenAI `403 model_not_found` yanıtı
`System.ClientModel.ClientResultException` fırlattı ve istek **işlenmemiş bir
500** oldu. Bu, **K-296'nın birebir tekrarıdır** (Faz 44: "resmî sağlayıcı
SDK'ları `HttpRequestException` FIRLATMAZ"). Düzeltmeden sonra aynı istek `502`
ve okunabilir bir `ProblemDetails` döndürüyor.

### Doğrulama komutları

```bash
# 0) Kaynak calistirma
RUN=$(curl -s -X POST http://localhost:5081/agentprism/api/agents/asistan/run \
  -H "content-type: application/json" \
  -d '{"message":"istanbul hava durumu"}' | jq -r '.runId')

# 1) Girdi kaydedildi mi — polimorfik icerik aynen geldi mi
curl -s "http://localhost:5081/agentprism/api/runs/$RUN/input" | jq '.messages'

# 2) jsonb tuzagi denetimi — sutun tipi `json` OLMALI
psql "$AGENTPRISM_CONN" -c \
  "SELECT data_type FROM information_schema.columns
    WHERE table_schema='agentprism' AND table_name='run_inputs' AND column_name='messages';"

# 3) Yeniden oynat — ReplayTools, tool GERCEKTEN kosmamali
BEFORE=$(psql -tA "$AGENTPRISM_CONN" -c "SELECT count(*) FROM agentprism.tool_invocations;")
REPLAY=$(curl -s -X POST "http://localhost:5081/agentprism/api/runs/$RUN/replay" \
  -H "content-type: application/json" \
  -d '{"toolMode":"ReplayTools"}' | jq -r '.runId')
AFTER=$(psql -tA "$AGENTPRISM_CONN" -c "SELECT count(*) FROM agentprism.tool_invocations;")
echo "tool_invocations: $BEFORE -> $AFTER  (ESIT olmali)"

# 4) Soy bagi
curl -s "http://localhost:5081/agentprism/api/runs/$REPLAY" | jq '{id, replayOfRunId}'

# 5) Karsilastir
curl -s "http://localhost:5081/agentprism/api/runs/$RUN/compare/$REPLAY" | jq

# 6) Onay gerektiren tool + LiveTools -> 409
curl -s -o /dev/null -w "%{http_code}\n" -X POST \
  "http://localhost:5081/agentprism/api/runs/$RUN/replay" \
  -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'

# 7) Dallandir
SESSION="oturum-1"
curl -s -X POST "http://localhost:5081/agentprism/api/agents/asistan/run" \
  -H "content-type: application/json" \
  -d "{\"message\":\"merhaba\",\"sessionId\":\"$SESSION\"}" > /dev/null
BRANCH=$(curl -s -X POST \
  "http://localhost:5081/agentprism/api/sessions/$SESSION/branch" \
  -H "content-type: application/json" -d '{"upToSequence":1}')
echo "$BRANCH" | jq

# 8) Dal bagimsiz mi — ana konusmanin oge sayisi DEGISMEMELI
psql "$AGENTPRISM_CONN" -c \
  "SELECT c.id, c.parent_conversation_id, c.branch_from_seq, count(i.id) AS oge
     FROM agentprism.conversations c
     LEFT JOIN agentprism.conversation_items i ON i.conversation_id = c.id
    GROUP BY c.id ORDER BY c.created_at;"

# 9) Saklama hedefi
curl -s http://localhost:5081/agentprism/api/retention/run_inputs | jq
```

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark gizlenmez — sonraki oturumun en değerli bilgisidir.

| # | Plan | Gerçekleşen | Neden |
|---|------|-------------|-------|
| S1 | `conversations.parent_conversation_id` `ON DELETE SET NULL` yabancı anahtarı taşır | **Yabancı anahtar YOK** | 🚨 SQL Server kendine referans veren bir FK'de `SET NULL` kabul etmez (hata 1785). Kısıtı yalnız PostgreSQL/SQLite'a koymak aynı silmeyi üç sağlayıcıda üç farklı sonuca çevirirdi. Davranış her yerde aynı: dal yaşar, işaretçi çözülemeyen bir kökeni gösterir ve hiçbir okuma yolu onu JOIN'lemez. **K-312** |
| S2 | Dal öğeleri tek bir `INSERT … SELECT` ile kopyalanır | **Tek işlem içinde satır satır** | Yeni öğe kimliği her satırda uuid v7 olmalıdır (K-015) ve üç diyalektin hiçbirinde ortak bir uuid v7 üreteci yoktur (`gen_random_uuid()` v4, `NEWID()` sıralanamaz, SQLite'ta yerleşik yok). Dayanıklılık değişmez: yazma tek işlemdedir. Ölçüldü: bin öğe ölçülebilir gecikme üretmedi. **K-311** |
| S3 | Eşleşmeyen tool çağrısı bir istisna fırlatır ve oynatma durur | **İstisna oynatıcıda KAYDEDİLİR, döngü `Terminate` ile kesilir, hata `ReplayMismatchGuard` tarafından çalıştırma sonrası fırlatılır** | 🚨 Ölçüldü: `FunctionInvokingChatClient` tool gövdesinden çıkan istisnayı YUTAR; ilk yazım uca hiç ulaşmadı ve istek `200` döndü. Sonuç aynıdır ve daha iyisidir: `runs` satırı `Failed` kapanır, hata tipi `replay_tool_mismatch` olur, uç `422` döner. **K-309** |
| S4 | — (planda yok) | **`ReplayTools`/`NoTools` modlarında skill ve çağrılabilir alt agent yüzeyleri KAPATILIR** | İkisi de tool'larını bir `AIContextProvider` üzerinden açar ve tool dönüşümünden geçmez. Açık bırakılsalardı skill script'i çalışır, alt agent gerçek bir model çağrısı harcardı — "hiçbir tool gerçekten koşmaz" sözü bozulurdu. |
| S5 | `tool_invocations` yeniden oynatmada **yeni satır almaz** | **Satır alır; ama hiçbir tool GÖVDESİ çalışmaz** | Ölçüldü. Çağrı gerçekten olmuştur (model tool'u çağırdı, oynatıcı kayıtlı sonucu döndürdü) ve `runs` kaydının kendi içinde tutarlı olması gerekir. Ayrıca `compare` ucu tool çağrısı SAYISINI karşılaştırır; oynatma hiç kaydetmeseydi her karşılaştırma sahte bir davranış değişikliği gösterirdi. Asıl garanti (`gövde koşmaz`) `ReplayTools_modunda_HICBIR_tool_gercekten_kosmaz` testinde sayaçla kanıtlanır. |
| S6 | Açık Soru 6: oynatma hem senkron hem kuyrukta çalışır | **Yalnız senkron** | Kuyruktan koşan bir oynatma iki ölçülmemiş şey ister: iş yükünün oynatma parametrelerini taşıması ve `RunReplayService`'in kiracı bağlamını işçi sürecinde doğru çözmesi. Uç sözleşmesi değişmez. **K-316**, aday kaleme yazıldı |
| S7 | `ConversationBranchService` Core'da, kopyalama `SqlSessionStore`'da | **Kopyalama ayrı bir `SqlConversationBranchStore`'da; `ChatHistoryState` Abstractions'a taşındı ve public oldu** | Core'daki servis oturum durumundaki konuşma kimliğini okuyup yeni oturuma yazmak zorundadır; durum anahtarı ve tipi Sql.Shared içinde `internal`di ve Core oradan okuyamıyordu. `SqlSessionStore`'a eklemek yerine ayrı bir depo yazmak `TenantCoverageTests` kapsamını da net tutar. |
| S8 | — (planda yok) | **`ReplayToolMode` `[JsonConverter(typeof(JsonStringEnumConverter<>))]` taşır** | 🚨 Bu işaret olmadan minimal API gövdeyi çözemez ve istek **boş gövdeli bir `400`** ile düşer; hata mesajı sebebi söylemez. `RunScoreKind` aynı işareti zaten taşıyor. Yeni bir public enum HTTP gövdesine girdiğinde ilk kontrol budur. |
| S9 | Playground'da "bir mesajdan buradan dallan" | **Mesaj bazlı dallanma OTURUM ekranında; playground konuşmanın tamamını dallandırır** | `upToSequence` bir `conversation_items.seq`'idir. Oturum ucu geçmişi `ChatHistoryProvider` üzerinden sıra numarasına göre döndürür (i'nci mesaj = `seq i`); playground'un dökümü canlı SSE'den katlanır ve hiçbir sıra numarası taşımaz. |

### Faz dışı ama bu fazda düzeltilen iki test altyapısı arızası

Fazın kendi kapsamı değildi; `dotnet test` çalıştırılamadığı için düzeltildi.

| Arıza | Kök sebep | Ölçüm |
|---|---|---|
| 🚨 `dotnet test AgentPrism.slnx` hiçbir test koşmadan **on dakikalarca asılı** kalıyordu | `AgentPrism.Templates.Tests` fikstürü `dotnet pack`'i yönlendirilmiş stdout ile çalıştırıyor; `pack`'in başlattığı MSBuild düğümleri (`nodeReuse:true`) komut bittikten sonra da yaşayıp boruyu açık tutuyor ve `Process.WaitForExitAsync` asenkron okuyucuların bitmesini de beklediği için **~15 dakika** bloke kalıyor | Düğümler `pkill` ile öldürülünce fikstür ANINDA devam etti. `MSBUILDDISABLENODEREUSE=1` eklendi: **8 dk+ (asılı) → 18,5 sn** |
| E2E paketi arayüz derlenmeden koşulduğunda 41 test 30'ar saniye zaman aşımına uğruyordu | `-p:AgentPrismFrontendEnabled=false` ile derlenmiş bir çözümde `AgentPrism.UI` hiçbir varlık gömmez | `UiHost.StartAsync` artık `IAgentPrismUiProvider.HasAssets` denetler ve saniyeler içinde açık bir mesajla düşer |

Tüm paketin süresi: **2 dk 33 sn** (SQL Server hariç — bkz. Bitiş Ölçütleri).

## Bu Fazda Verilen Kararlar

| Karar | Özet |
|---|---|
| **K-308** | Girdi ayrı bir `run_inputs` tablosunda ve `json` sütununda; `runs`'a sütun eklenmedi |
| **K-309** | Varsayılan `ReplayTools`; eşleşmeyende `422` — ve MAF'ın istisnayı yutması nedeniyle hatanın nasıl fırlatıldığı |
| **K-310** | `LiveTools` `Admin` ister; onay gerektiren tool `409` alır |
| **K-311** | Dallanmada kopyalama; işaretçi zinciri reddedildi (okuma yolu bozulmaz) |
| **K-312** | `parent_conversation_id` yabancı anahtar taşımaz (SQL Server 1785) |
| **K-313** | Dallandırma yalnız SQL sağlayıcısı açıkken; bellek içinde `501` 👤 |
| **K-314** | Kod agent'ı bindirmeyle oynatılamaz; `400` 👤 |
| **K-315** | Yeniden oynatma oturumsuzdur |
| **K-316** | Oynatma yalnız senkron; kuyruğa alma kapsam dışı |

## Sonraki Faza Devir Notu

- 🚨 **`IRunInputStore` YENİ bir arayüzdür ve Faz 7'den (yayın) ÖNCE eklendi.** Ona metot eklemek yayından sonra kırıcıdır — Faz 36 (`IRetentionStore`) ve Faz 45 (`IEvalStore`) ile aynı sınıf. Aynısı `IConversationBranchStore` için de geçerlidir.
- 🚨 **[Faz 45](45-URETIMDEN-EVAL-KUMESI.md) (üretimden eval kümesi) artık `run_inputs`'tan doğrudan yararlanabilir.** Bugün terfi, girdi metnini `run_events`'teki `RunStarted.Text`'ten okuyor (K-300) ve o metin `MaxPayloadLength` ile **kırpılabilir**; `run_inputs` kırpılmamış ve polimorfik tam girdiyi taşır. **İkinci bir girdi kaydı AÇILMAMALIDIR.**
- 🚨 **[Faz 49](49-CEVRIMICI-DEGERLENDIRME.md) girdi kaynağı hazırdır.** `IRunInputStore.GetAsync` yargıca hem soruyu hem tam bağlamı verir.
- 🚨 **Yeniden oynatma OTURUMSUZDUR (K-315).** Çok turlu bir konuşmayı baştan almanın yolu dallandırmadır. Bir sonraki faz "oturumlu oynatma" isterse önce konuşma anlık görüntüsü tasarımı ölçülmelidir.
- 🚨 **`FunctionInvokingChatClient` tool istisnalarını YUTAR.** Tool gövdesinden çıkan bir hatayı uca taşımak isteyen her faz `FunctionInvocationContext.Terminate` + çalıştırma sonrası fırlatma desenini (`ReplayMismatchGuard`) kullanmalıdır.
- **Workflow dallandırma bu fazın kapsamı DIŞINDADIR.** `workflow_checkpoints.parent_id` Faz 15'ten beri vardır ama bir uç yoktur; konuşma dallandırmasının deseni (kopyala, işaretçi kovalama) oraya doğrudan taşınmaz çünkü kontrol noktası durumu opaktır.
- **Yeni aday kalemler** (aşağıdakiler bilinçli olarak kapsam dışına çıkarıldı):
  | Kapsam dışı | Neden ayrı |
  |---|---|
  | Kuyruğa alınan yeniden oynatma (`Prefer: respond-async`) | K-316: iş yükü sözleşmesi + işçi sürecinde kiracı çözümü ölçülmedi |
  | Bellek içi konuşma deposu (dallandırmayı her kurulumda açar) | K-313: AgentPrism kendi `ChatHistoryProvider`'ını yazmalı; K3'ü zorlar |
  | Alt agent ve skill yüzeylerinin oynatılması | S4: ikisi de `AIContextProvider` üzerinden gelir ve tool dönüşümünden geçmez |
  | Workflow dallandırma ucu | Kontrol noktası durumu opaktır |
