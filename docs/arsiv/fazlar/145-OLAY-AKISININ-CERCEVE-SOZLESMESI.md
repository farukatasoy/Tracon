# Faz 145 — Kayıtlı Olay Akışının Çerçeve Sözleşmesi

> **Durum:** ✅ Tamamlandı (2026-09-05)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-193** (tüketici turu 4, F1)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.AspNetCore` · kapı testi `AgentPrism.Core.UnitTests`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — `EventName` `private static`, `.Produces<string>` yalnız üstveridir. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya, yani her dosya yalnız başlık taşıyor, **shipped giriş sıfır**
> **Tüketici yüzeyi:** `docs-site/`: `concepts/runs.md`, `http-api.md` (+ üretilen `api/`, `http-api/`, `llms-full.txt`) · sevk edilen: `AgentPrismStreamRunEvents` uç açıklaması, `docs/openapi/agentprism.json`, `packages/agentprism-client/src/schema.ts`, `src/AgentPrism.Client/Generated/*.g.cs` (hepsi yeniden üretilir — gerçekleşen kapsam plandan geniş, bkz. "Plandan Sapmalar")
> **Manuel test alanı:** [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](../../manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show ac95a731:docs/arsiv/fazlar/145-OLAY-AKISININ-CERCEVE-SOZLESMESI.md
> ```
>
> Damıtıldı 2026-09-05 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism iki SSE akışı sevk ediyor ve **ikisi de sözleşmesini eksik ilan ediyor**. Kayıtlı olay akışı (`GET /api/runs/{id}/events`) 31 olay tipinin 21'ini `unknown` adıyla gönderiyor; oysa aynı metodun yorumu adları *"a **stable** contract"* ilan ediyor.

## Bitiş Ölçütleri (DoD)

- [x] `EventName` 31 `RunEventType` üyesinin hepsini adlandırır; `unknown` dalı korunur ama **hiçbir üye** oraya düşmez — `RunEventFrameNameContractTests.Every_RunEventType_member_has_a_named_frame_none_falls_to_unknown` yeşil
- [x] Sevk edilmiş 10 ad **birebir** korunur (`run.started` … `child.completed`) — `RunEventFrameNameContractTests.Shipped_frame_names_are_unchanged` yeşil
- [x] `RunEventFrameNameContractTests` üç iddiayı kanıtlar: tamlık · arayüz ad eşitliği · sevk edilmiş 10 adın sabitliği — dört test, hepsi yeşil
- [x] Tarama hiçbir üye bulamazsa test **kırmızı** olur (K-642 sınıfı korunur) — üç `ShouldNotBeEmpty` iddiası taramanın gerçekten çalıştığını kanıtlıyor (boş küme geçseydi bu iddialar kendisi kırmızı olurdu)
- [x] `curl -s .../openapi/v1.json | jq '.paths["/api/runs/{runId}/events"].get.responses."200".content'` `text/event-stream` döner — `samples/AgentPrism.Api`'de canlı doğrulandı, çıktı: `{"text/event-stream":{"schema":{"type":"string"}}}`
- [x] `docs/openapi/agentprism.json` yeniden üretildi; SSE bildiren işlem sayısı **6 → 7** oldu — ölçüldü, `sorted(n)` yedi operationId listeler
- [x] `packages/agentprism-client/src/schema.ts` yeniden üretildi ve diff'te yeni `text/event-stream` girdisi görünür
- [x] `AgentPrismStreamRunEvents` `404`'ü de bildirir — `samples/AgentPrism.Api`'de canlı doğrulandı
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 234d4081` (build, 4200+ test, pack, format, docs-site dört kapısı — hepsi ✅, `exit=0`)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, akış çıktısı belgeye yazıldı — bkz. "Gerçekleşen Public API" altındaki canlı çıktı
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md` içine eklendi (MT-UIRUN-056..061); otomatikleştirilebilen beşi `samples/AgentPrism.Api`'de canlı koşuldu, altıncısı (`child.timed-out`, MT-UIRUN-061) gerçek HTTP `TestServer` üzerinden `SubAgentTimeoutTests`'in fonksiyonel testiyle kanıtlandı (canlı sağlayıcıya karşı bir alt-agent askıya alma senaryosu bu oturumun bütçesinde kurulmadı)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi (`concepts/runs.md` iki akış tablosu, `http-api.md`); `npm run build` + `check-links.mjs` temiz (157851 iç referans, 0 kırık; 1074 sayfa ağırlık bütçesinde)

### Doğrulama komutları

```bash
# Ad tamlığı — çıktıda "unknown" GEÇMEMELİ
curl -N -s "$APU/api/runs/$RUN_ID/events" -H "$APB" | grep '^event:' | sort -u

# OpenAPI içerik tipi
curl -s "$APU/openapi/v1.json" \
  | jq '.paths["/api/runs/{runId}/events"].get.responses."200".content | keys'
# beklenen: ["text/event-stream"]

# SSE bildiren işlem sayısı (6 -> 7)
python3 -c "
import json
s=json.load(open('docs/openapi/agentprism.json'))
n=[o.get('operationId') for p in s['paths'].values() for m,o in p.items()
   if m in {'get','post','put','patch','delete'} and 'event-stream' in json.dumps(o)]
print(len(n), sorted(n))"
```

---

## Plandan Sapmalar

1. **Yeni `RunEventStreamFrameTests.cs` açılmadı.** Plan bunu ayrı bir YENİ
   dosya olarak öngörüyordu. Uygulama sırasında `StreamingTests.cs`'nin
   zaten "Run events stream" adlı kendi bölümü olduğu ve `Nonexistent_runs_
   events_return_404`/`Run_events_are_streamed_in_order`/`Resumes_where_it_
   left_off_via_Last_Event_ID`/`A_consumer_written_Custom_event_carries_its_
   CustomType_over_the_wire` testlerinin ZATEN orada durduğu görüldü. Yeni
   davranışı (workflow çerçeve adları, guard çerçeve adı, `child.timed-out`
   çerçeve adı, OpenAPI içerik tipi) ilgili konunun ZATEN sahibi olan dosyaya
   eklemek tercih edildi: `WorkflowEndpointTests.cs`, `ContentGuardEndpointTests.cs`,
   `SubAgentTimeoutTests.cs`, `StreamingTests.cs` (Custom testine ek), ve
   `OpenApiResponseSchemaTests.cs` (tam olarak bu tür bir boşluk için Faz 40'ta
   açılmış dosya). Gerekçe: aynı host bootstrap yardımcılarını ikinci bir
   dosyada tekrarlamak yerine mevcut dosyaların doğal büyümesi.
2. **`RunEventFrameNameContractTests` planlanan üç iddiadan DÖRT test
   üretti** — tamlık, arayüz ad eşitliği, sevk edilmiş 10 adın sabitliği ve
   ayrıca `unknown` yakalayıcı dalının hâlâ var olduğunu doğrulayan ayrı bir
   test. Plan bunların hepsini "üç iddia" diye özetliyordu; kod dört ayrı
   `[Fact]`'e bölündü çünkü her biri bağımsız kırılabilir bir iddia.
3. **`src/AgentPrism.Client/Generated/AgentPrismApiClient.g.cs` planın
   "Planlanan Dosya Listesi"nde YOKTU** ve bağımsız denetimin 🔴 #2 bulgusu
   bunu açıkça işaretledi. `.Produces<string>(200, "text/event-stream")`
   eklemek OpenAPI belgesinin bu işlem için içerik tipini `Response was OK`
   → gerçek bir şemaya çevirdi; NSwag bunun üzerine bu işlemin dönüş tipini
   `Task` → `Task<string>`'e çevirdi (ailedeki diğer altı SSE işlemiyle AYNI
   şekle geldi). Bu, plan yazılırken görülemeyen, OpenAPI regenerasyonunun
   **mekanik yan etkisidir** — plan başlığındaki "Yeni paket: Yok" öncülü
   doğru kalır (yeni paket yok), ama "dokunulan dosya" kümesi genişledi.
4. **Bağımsız denetim `AgentPrismStreamRunEventsAsync`'in (ve ailenin
   diğer 4 saf-SSE üyesinin) her çağrıldığında `AgentPrismApiException`
   fırlattığını buldu — plandan sapma değil, denetimin bulduğu gerçek bir
   üretim kusuruydu ve kapanmadan faz bitmiyordu.** Kök neden bu fazdan
   ÖNCE `AgentPrismRunAgentAsync`'te zaten vardı (NSwag'in `ReadObjectResponseAsync
   <string>`'i, JSON OLMAYAN bir SSE gövdesine `JsonSerializer.Deserialize
   <string>` uyguluyordu) ama hiçbir test gerçek bir sunucuya karşı gerçek
   bir çağrı yapmadığı için görünmezdi. Düzeltme `scripts/nswag-postprocess-
   client.py`'a DÖRDÜNCÜ bir dönüştürme adımı ekledi: `status_ == 200` dalında
   `ReadObjectResponseAsync<string>` çağıran her işlemi (yapısal olarak
   TAM 5 eşleşme — ailedeki saf-SSE beş uç) ham metin okumaya çevirir. İki
   çift-içerikli uç (`/v1/responses`, `/v1/chat/completions`) BİLEREK
   dokunulmadı — ayrı, daha büyük bir kusur sınıfı (bkz. `ADAYLAR.md` F-198).
   Kanıt: `tests/AgentPrism.AspNetCore.FunctionalTests/GeneratedClientSseTests.cs`
   (YENİ, plan dışı) gerçek bir `TestServer`'a karşı hem `AgentPrismRunAgentAsync`
   hem `AgentPrismStreamRunEventsAsync`'i çağırır ve SSE gövdesinin ARTIK
   JSON hatası vermeden düz metin olarak döndüğünü kanıtlar. Bu test aynı
   zamanda AYRI bir pre-existing kusuru ortaya çıkardı — generated
   `AgentRunRequest`'in koleksiyon alanları (`Approvals`/`ToolResults`/
   `AttachmentIds`/`Documents`) `null` varsayılanı taşıyor ve sunucu onları
   koşulsuz `.Count` ile okuyunca `NullReferenceException` veriyor; bu Faz
   145'in kapsamı DIŞINDA bırakıldı ve `ADAYLAR.md`'ye F-197 olarak girdi.
5. **`EventName`'in XML doc yorumundaki "(phase 145)" ifadesi bağımsız
   denetimin 🔴 #1 bulgusuydu** — `ShippedDocumentationSelfContainmentTests`'in
   sevk edilen belge taban çizgisini (dahili faz numarası referansı sıfır)
   ihlal ediyordu. Cümle faz numarası olmadan yeniden yazıldı; `//` gövde
   yorumlarındaki "Phase 145" referansları (yeni nswag-postprocess-client.py
   dönüşümünün ürettiği) dokunulmadan kaldı çünkü onlar `///` DEĞİL ve
   pakete giren XML dokümanına hiç girmiyor.
6. **Manuel kabul case'lerinin (`MT-UIRUN-056..061`) resmi bir `kosumlar/`
   koşum kaydı yok.** Bağımsız denetimin 🟡 #2 bulgusu. Gerekçe: bu dosyanın
   üst notu ve `manuel-test-kosumu` skill'inin kendi tanımı `kosum kaydı`
   üretmeyi **tam set koşumuna** (yayın öncesi veya kullanıcı isteği) ait
   sayar, tek bir fazın kapanışına değil (`faz-tamamlama` Adım 3 yalnız
   SPEC'i üretir/koşar, `manuel-test-kosumu` sonucu KAYDEDER). Beşi
   `samples/AgentPrism.Api`'de gerçek bir OpenAI çağrısıyla canlı koşuldu ve
   çıktıları doğrudan bu dokümanın "Bitiş Ölçütleri" bölümüne yazıldı;
   altıncısı (`child.timed-out`, MT-UIRUN-061) gerçek HTTP `TestServer`
   üzerinden koşan `SubAgentTimeoutTests` fonksiyonel testiyle kanıtlandı.
   Bu, bir `kosumlar/` klasör girdisinden daha zayıf değil — komut ve gerçek
   çıktı bu dokümanda birebir duruyor.

## Bu Fazda Verilen Kararlar

- **K-678** — Kayıtlı olay akışının SSE çerçeve adları açık bir eşleme
  tablosuyla verilir; `RunEventType` üyesinin adından mekanik türetilmez,
  tamlık bir kapıya bağlanır (kullanıcı kararı, 145.1).
- **K-679** — `RunEventType.Custom`'ın çerçeve adı her zaman sabit `"custom"`
  dır; tüketicinin kendi `CustomType` dizgesi asla çerçeve adı olmaz
  (kullanıcı kararı, 145.1).

## Denetim Bulguları

Taze bağlamlı bağımsız denetim (`general-purpose` agent, taban `234d4081`)
iki tur koştu: ilk tur iki 🔴 buldu, düzeltmeler sonrası ikinci doğrulama
(bu oturumun kendisi tarafından, gerçek test koşumuyla) ikisini de kapattı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `EventName`'in `///` XML dokümanı "(phase 145)" taşıyor; `ShippedDocumentationSelfContainmentTests` kırmızıydı | **Düzeltildi** — ifade cümleden çıkarıldı, test yeşil (doğrulandı: `--filter-method "*ShippedDocumentationSelfContainmentTests*"`) |
| 2 | 🔴 | `AgentPrismStreamRunEventsAsync` (ve ailenin diğer 4 saf-SSE üyesi) gerçek bir SSE gövdesiyle çağrıldığında HER ZAMAN `AgentPrismApiException` fırlatıyor — NSwag'in JSON-deserialize eden yardımcısı ham SSE metnini JSON sanıyor | **Düzeltildi** — `nswag-postprocess-client.py`'a dördüncü dönüştürme adımı eklendi (yapısal eşleşme: `status_ == 200` + `ReadObjectResponseAsync<string>`, tam 5 eşleşme). Kanıt: `GeneratedClientSseTests` (YENİ) gerçek `TestServer`'a karşı iki metodu da çağırıp SSE gövdesinin düz metin döndüğünü kanıtlıyor |
| 3 | 🟡 | `AgentPrismApiClient.g.cs`'in yeniden üretilmesi plandan sapma olarak `Plandan Sapmalar`'a yazılmamıştı | **Kapandı** — bkz. "Plandan Sapmalar" #3/#4 |
| 4 | 🟡 | `MT-UIRUN-056..061` için `docs/manuel-test/kosumlar/` altında resmi bir koşum kaydı yok | **Gerekçelendi** — bkz. "Plandan Sapmalar" #6; kayıt üretmek `manuel-test-kosumu`'nun (tam set koşumu) işidir, bu fazın DoD'si canlı komut çıktısını doğrudan bu dokümana yazarak karşılandı |
| 5 | 🟢 | Ailedeki hiçbir SSE-döndüren istemci metodu gerçek bir sunucuya karşı test edilmiyordu (K-633 sınıfı kör nokta tekrarlayabilir) | `docs/ADAYLAR.md`'ye **F-197** (koleksiyon `null` varsayılanı) ve **F-198** (çift-içerikli iki uç) olarak devredildi |

**Temiz çıkan başlıklar** (denetçinin kendi ifadesiyle): 3.1 (ad tablosu
tamlığı, kod çalıştırılarak doğrulandı), 3.2 (test tiyatrosu yok), 3.3 (sınır
seviyeleri doğru), 3.5 (imza-gövde kayması yok), 3.6 (planlı public API
büyümedi), karar defteri kaydı, `docs-site` dört kapısı.

Düzeltmeler sonrası dört doğrulama kapısı yeniden koşuldu (bkz. DoD).

## Sonraki Faza Devir Notu

- **`nswag-postprocess-client.py` artık DÖRT dönüştürme adımı taşıyor**
  (enum converter × 2, colliding any-type, ve bu fazın pure-SSE-string
  düzeltmesi). Tel üzerinde görünen bir SSE içerik tipi değişikliği
  (yeni bir `.Produces<string>(200, "text/event-stream")` eklenmesi)
  yaşandığında bu script'i **otomatik** kapsar — ayrı bir elle müdahale
  gerekmez, yalnız normal 4 adımlık regen sırası (`nswag-prepare-document.py`
  → `dotnet nswag run` → `nswag-postprocess-client.py` → `generate-client-
  json-context.py`) izlenir.
- **🚨 `generate-client-json-context.py`'nin ikinci argümanı
  `src/AgentPrism.Client/Generated/AgentPrismClientJsonContext.g.cs`'dir,
  `src/AgentPrism.Client/AgentPrismApiClient.JsonContext.cs` DEĞİL.** Bu
  fazın uygulama oturumu bunu bir kez karıştırdı ve elle-yazılmış wiring
  dosyasının üzerine üretilen içeriği yazdı (`git checkout` ile geri
  alındı, `docs/hafiza/nswag-istemci-uretimi.md`'nin "Sıra" notu bunu netleştirebilir
  — dosya adları görsel olarak çok benzer).
- **`AgentPrism.Client`'ın generated DTO'larının koleksiyon alanları `null`
  varsayılanı taşıyor** (F-197). Bu fazın YENİ `GeneratedClientSseTests.cs`'i
  bunu elle `[]` atayarak aşıyor — bir sonraki fazın aynı dosyaya dokunması
  gerekirse aynı deseni tekrarlamalı, `AgentRunRequest`'i minimal alanla
  kurmaya çalışmamalı.
- **Ailedeki SSE ailesi artık YEDİ üye**, ikisi (`/v1/responses`,
  `/v1/chat/completions`) hâlâ typed client'ta yalnız JSON şeklini üretiyor
  (F-198). Bu iki uca dokunan bir sonraki faz bunu bilmeli.
- **`RunEventFrameNameContractTests`'in kaynak taraması regex'e dayanır**
  (K-642 sınıfı). Yeni bir `RunEventType` üyesi eklerken `RunEndpoints.
  EventName`'e satır eklemeyi unutmak artık derleme hatası DEĞİL, test
  kırmızısı üretir — kapı yeşilse tamlık garantidir.
