# Faz 43 — `Idempotency-Key` Desteği

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-37**
> **Önkoşul:** Yok. Ama [Faz 25](25-VERI-SAKLAMA-VE-ARSIVLEME.md)'in saklama hedef kayıt defteri **kullanılır**
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`
> **Yeni paket:** Yok · **Migration:** **gerekli** — bir tablo, üç set, numaralar uygulama anında alınır (K-178)
> **Public API:** büyüyor — bir arayüz, bir ayar, bir kayıt tipi. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/43-IDEMPOTENCY-KEY.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir istemci ağ hatası aldığında isteği yeniden gönderir. AgentPrism bugün bunu **ikinci bir çalıştırma** olarak görür: agent ikinci kez koşar, tool'lar ikinci kez yan etki üretir ve model faturası ikinci kez yazılır. Bu faz, standart `Idempotency-Key` başlığını destekler: aynı anahtarla gelen ikinci istek **yeniden çalıştırmaz**, ilk yanıtı döndürür.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 Aynı `Idempotency-Key` ile gönderilen ikinci istek **agent'ı yeniden
      çalıştırmaz**; `runs` tablosunda **tek** satır oluşur — gerçek koşumda
      doğrulandı, bkz. "Doğrulama komutları" çıktısı
- [x] Tekrarlanan istek kotayı **ikinci kez tüketmez** (`/api/quotas` ile
      doğrulanır) — `IdempotencyTests.Tekrarlanan_istek_kotayi_ikinci_kez_tuketmez`
- [x] Aynı anahtar + farklı gövde `422` döner
- [x] İki eş zamanlı istek: biri koşar, diğeri `409` alır — sözleşme testinde
      (8 eşzamanlı çağrı, ağ katmanı olmadan) doğrulandı; HTTP-seviyesi testi
      `TestServer`'ın isteklerin SIRALI mı EŞZAMANLI mı işleneceğine karar
      vermesi yüzünden "kim 409 aldı" yerine "agent YALNIZ BİR KEZ çalıştı"
      değişmezini doğrular (bkz. Plandan Sapmalar)
- [x] Başarısız çalıştırmadan sonra aynı anahtarla yeniden deneme **çalışır**
- [x] Akışlı istek + anahtar `400` döner ve sebebi yazar — `/v1/responses` ve
      `/v1/chat/completions` için; `/api/agents/{name}/run` bu durumu hiç
      ÜRETEMEZ (K-288, bkz. Plandan Sapmalar)
- [x] Aynı anahtar iki kiracıda bağımsız yaşar — sözleşme testinde doğrulandı
- [x] `idempotency_keys` bir saklama hedefidir; `GET /api/retention/idempotency_keys`
      yanıt verir — gerçek koşumda `PUT` ile doğrulandı (200)
- [x] Sözleşme testleri bellek içi + üç SQL sağlayıcısında geçer — InMemory
      (813 test içinde), PostgreSQL (813/813), SQLite (420/420) **gerçekten
      koştu**; SQL Server bu makinede **koşamadı** (Docker/arm64 kısıtı,
      önceden bilinen — bkz. Plandan Sapmalar #6)
- [x] Migration üç sette de uygulandı (K-178) — PostgreSQL `0020` ve SQLite
      `0008` gerçek koşumda uygulandı (tablo sayısı testleri 41→42); SQL
      Server `0008` dosyası yazıldı ama bu makinede UYGULANAMADI
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`pack`/`format`
      temiz; `dotnet test` SqlServer.IntegrationTests DIŞINDA tüm projelerde
      yeşil (env kısıtı, kod hatası değil)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü

### Doğrulama komutları — gerçek çıktı (2026-08-07, `samples/AgentPrism.Api`, echo sağlayıcı, "support" agent)

🚨 Planın taslak `curl`'leri `/api/agents/{name}/run` için `{"messages":[...]}`
gövdesi varsayıyordu; gerçek şema `AgentRunRequest.Message` (tekil metin) ve
akış seçimi `?stream=true` **DEĞİL**, `Idempotency-Key` başlığının kendisidir
(K-288). Aşağıdaki komutlar gerçek şemayla çalıştırıldı.

```bash
KEY=$(uuidgen)

# Ilk istek
curl -s -D - -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"message":"merhaba"}'
# -> HTTP/1.1 200 OK (Idempotency-Replayed YOK)
# {"runId":"019fdac7-f3b8-7a40-9154-5397b05a0026","response":{"messages":[{"authorName":"support","role":"assistant","contents":[{"$type":"text","text":"Merhaba! Size nasıl yardımcı olabilirim?"}],...}],...}}

# Ikinci istek — AYNI govde.
curl -s -D - -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"message":"merhaba"}'
# -> HTTP/1.1 200 OK, Idempotency-Replayed: true
# runId ve response BIREBIR AYNI (ayni messageId, ayni createdAt) — agent IKINCI KEZ CALISMADI

# /api/runs sayisi TEK olmali
curl -s http://localhost:5081/agentprism/api/runs | python3 -c "import json,sys;print(len(json.load(sys.stdin)))"
# -> 1

# Farkli govde — 422 gelmeli
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" -H "Idempotency-Key: $KEY" \
  -d '{"message":"BASKA"}'
# -> 422

# Akisli istek (stream:true govdede) + anahtar — 400 gelmeli (/v1/responses)
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/agentprism/v1/responses \
  -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"model":"support","input":"merhaba","stream":true}'
# -> 400

# Saklama hedefi taniniyor mu — PUT ile politika kaydi (GET, DB'de kayit yoksa 404 doner; bu NORMALDIR)
curl -s -o /dev/null -w "%{http_code}\n" -X PUT http://localhost:5081/agentprism/api/retention/idempotency_keys \
  -H "content-type: application/json" -d '{"maxAgeDays":1,"enabled":true}'
# -> 200
```

---

## Plandan Sapmalar

Plan ile gerçek arasındaki fark burada **gizlenmeden** yazılıdır.

1. **`POST /api/agents/{name}/run` akışsız bir dal kazandı — planın öngörmediği
   bir kod değişikliği (K-288, kullanıcı kararı).** Plan bu ucun hem akışsız
   hem akışlı çalışabileceğini varsayıyordu ("Idempotency-Key basligi tasiyan
   bir istek akissiz calisir; akisli istekte 400"). Ama koddaki gerçek durum
   FARKLIYDI: `AgentRunStream` KOŞULSUZ SSE dönüyordu, hiçbir akışsız dalı
   yoktu. Bu, doğrudan fazın kendi motivasyon örneğini ("bugün ne çalışmıyor")
   geçersiz kılıyordu — `QuotaGate`'in ikinci kez tüketmesi örneği tam olarak
   bu uçtu. Kullanıcıya iki seçenek sunuldu: (A) uca akışsız bir dal eklemek,
   (B) bu uçta `Idempotency-Key`'i hep 400 ile kapatıp DoD'un kota testini
   `/v1/responses`'a taşımak. Kullanıcı **A**'yı seçti. Sonuç:
   `AgentEndpoints.RunAsync` artık `Idempotency-Key` başlığı varsa
   `AgentRunStream`'i `streaming: false` ile kurar; `ExecuteBufferedAsync`
   `agent.RunAsync(...)` çağırır ve `Results.Json(...)` ile tek bir JSON gövde
   yazar (`AgentRunResult { RunId, SessionId, Response }`). Bu uçta akışlı+
   `Idempotency-Key` birlikteliği artık HİÇ oluşamaz (başlık varlığı zaten
   akışsızlığı seçiyor); 43.4'ün "akışlı istek+anahtar→400" kuralı yalnız
   `/v1/responses` ve `/v1/chat/completions` üzerinde gözlemlenir.
2. **`AgentPrismIdempotencyOptions` `AgentPrism.Core`'da, plandaki gibi
   `AgentPrism.AspNetCore`'da değil (K-289).** `AgentPrismRateLimitOptions`
   (Faz 21) ve `AgentPrismRetentionOptions` (Faz 25) emsali izlendi.
3. **`AgentPrismIdempotencyOptions.Retention: TimeSpan` planı terk edildi
   (K-290).** Bunun yerine `idempotency_keys` standart
   `RetentionTargets`/`RetentionTargetRegistry`/`AgentPrismRetentionOptions`
   üçlüsüne `MaxAgeDays = 1` varsayılanıyla eklendi — Faz 25'in devir notunun
   zorunlu kıldığı desen. `AgentPrismIdempotencyOptions` yalnız `Enabled` ve
   `MaxKeyLength` taşır.
4. **Ham gövde tamponlaması için `MapAgentPrism`'e koşullu bir ara yazılım
   eklendi — planda hiç yoktu (K-292).** Minimal API'nin `[FromBody]` bağlaması
   `/api/agents/{name}/run` gövdesini `IEndpointFilter.InvokeAsync`
   çağrılmadan ÖNCE tüketiyor; filtrenin kendi içinde `EnableBuffering()`
   çağırmak bu yüzden çok geç kalırdı. `MapVoiceConversation`'ın
   `app.UseWebSockets()` deseniyle aynı teknikle çözüldü.
5. **Ayırma "`ON CONFLICT DO NOTHING RETURNING`" değil, düz `INSERT` +
   `SqlDialect.IsUniqueViolation` yakalamasıdır.** Plan PostgreSQL için
   `ON CONFLICT ... RETURNING`, SQL Server için `INSERT` + yakalanan
   benzersizlik hatası öneriyordu (iki farklı teknik). Gerçekleşen, ÜÇÜNÜ DE
   tek bir C# kod yoluna indiren `SqlExperimentStore.StartAsync` deseninin
   (Faz 19) aynısıdır: düz `INSERT`, `catch (DbException ex) when
   (Dialect.IsUniqueViolation(ex))`, sonra `SelectIdempotencyKey` ile mevcut
   kaydı oku. Üç sağlayıcıda üç farklı SQL şekli yerine bir `SqlIdempotencyStore`.
6. **SQL Server entegrasyon testleri bu oturumda ÇALIŞTIRILAMADI.** Kod derlendi
   ve sözleşme testi (contract) yazıldı, ama gerçek `mssql/server` konteyneri bu
   Apple Silicon makinede başlatılamıyor (`docs/hafiza/sql-saglayicilari.md`'nin
   bilinen kısıtı — Faz 23'ten beri aynı). PostgreSQL, SQLite ve bellek içi
   sözleşme testleri **gerçekten koştu ve geçti**.

## Bu Fazda Verilen Kararlar

K-288 — K-292. Tam metin: `docs/KARARLAR.md`.

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** `IIdempotencyStore` — yukarıdaki imza. `SqlIdempotencyStore`
  yalnız iki kalıcı durum yazar (`Reserved=0`, `Completed=2`); `InProgress`/
  `FingerprintMismatch` OKUMA anında türetilir, veritabanında YOKTUR.
- 🚨 **`/api/agents/{name}/run` artık İKİ yanıt biçimine sahiptir**: `Idempotency-Key`
  YOKSA SSE (varsayılan, değişmedi), VARSA tek JSON gövde
  (`{ runId, sessionId, response: AgentResponse }`). Bu ucu değiştiren her
  gelecek faz her iki dalı da güncellemelidir (`AgentRunStream.ExecuteStreamingAsync`
  / `ExecuteBufferedAsync`).
- 🚨 **Aday listesindeki F-68 (dayanıklı çalıştırma) bu fazın üstüne oturur.**
  İki devir bilgisi zorunludur:
  1. **Akışlı idempotency** bu fazda `/v1/responses` ve `/v1/chat/completions`
     için `400` ile kapatıldı (`/run` için bu durum hiç oluşmaz — yukarı bak).
     F-68'in `202 Accepted` + `Location` sözleşmesi akışlı idempotency'nin
     doğru evidir.
  2. F-68 Okuma A "süreç düşerse iş **baştan** çalışır" diyor. Bu fazın
     `IIdempotencyStore`'u o yeniden çalışmanın yan etkili tool'ları ikinci kez
     tetiklemesini **engellemez** — anahtar HTTP yüzeyindedir, iş kuyruğunda
     değil.
- **SQL Server için gerçek doğrulama bekliyor.** Migration dosyası ve
  `SqlIdempotencyStore` kodu üç sağlayıcı için TEK yoldan yazıldı (aynı
  `IsUniqueViolation` deseni), ama `mssql/server` konteyneri bu makinede hiç
  çalışmadı. Linux/amd64 bir makinede veya CI'da
  `dotnet test tests/AgentPrism.SqlServer.IntegrationTests` çalıştırılmalı.
- **`idempotency_keys` saklama hedefi `AgentPrismRetentionOptions.IdempotencyKeys`
  ile `MaxAgeDays = 1` varsayılanı taşır** ama `RetentionExecutor`'ın gerçek
  bir üretim koşusunda ne kadar hacim sildiği ÖLÇÜLMEDİ (Faz 43 planının Açık
  Soru 4'ü — "hacim ölçülmeli" hâlâ açık).
