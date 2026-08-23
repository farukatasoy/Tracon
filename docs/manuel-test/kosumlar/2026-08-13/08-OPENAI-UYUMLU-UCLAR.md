# 08 — OpenAI Uyumlu Uçlar (`COMPAT`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../08-OPENAI-UYUMLU-UCLAR.md`](../../08-OPENAI-UYUMLU-UCLAR.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin:
> `git log --follow -- <bu dosya>`

---

## Temiz geçen case'ler (38)

| Case | Durum | Başlık |
|---|---|---|
| MT-COMPAT-001 | ☑ | `model` alanından agent seçilir, mutlu yol |
| MT-COMPAT-002 | ☑ | `metadata.entity_id` ile agent seçilir (DevUI konvansiyonu) |
| MT-COMPAT-003 | ☑ | Ne `model` ne `metadata.entity_id` verilirse `400` |
| MT-COMPAT-004 | ☑ | Bilinmeyen agent `404` + `model_not_found` |
| MT-COMPAT-005 | ☑ | `messages` boş dizi ise `400` |
| MT-COMPAT-006 | ☑ | Bozuk JSON gövdesi `400` |
| MT-COMPAT-007 | ☑ | Parçalı içerik (`content` dizi biçimi) birleştirilir |
| MT-COMPAT-008 | ☑ | `developer` rolü `system`'e eşlenir |
| MT-COMPAT-009 | ☑ | Tool çağrısı tel biçiminde görünmez, ama çalıştırma kaydına düşer |
| MT-COMPAT-010 | ☑ | `usage` alanları `snake_case` döner |
| MT-COMPAT-011 | ☑ | Akışlı yanıt: ilk çerçeve `role` deltası, biten işaret `[DONE]` |
| MT-COMPAT-012 | ☑ | Akış durumsuzdur: hiçbir oturum/konuşma oluşmaz |
| MT-COMPAT-013 | ☑ | `Idempotency-Key` tekrarı önbellekten döner |
| MT-COMPAT-014 | ☑ | Aynı `Idempotency-Key`, farklı gövde → `422` |
| MT-COMPAT-015 | ☑ | `Idempotency-Key` + `stream: true` → `400` |
| MT-COMPAT-016 | ☑ | Aynı tel biçimi gerçek bir Anthropic agent'ıyla da çalışır |
| MT-COMPAT-017 | ☑ | `model` alanından agent seçilir, mutlu yol |
| MT-COMPAT-018 | ☑ | `metadata.entity_id` ile agent seçilir |
| MT-COMPAT-019 | ☑ | Agent seçilmezse `400` + bilinen agent listesi |
| MT-COMPAT-020 | ☑ | Bilinmeyen agent `404` + bilinen agent listesi |
| MT-COMPAT-021 | ☑ | `conversation` alanı oturumu o kimlikle saklar (K-043) |
| MT-COMPAT-022 | ☑ | `previous_response_id` geçmişi zincirler |
| MT-COMPAT-024 | ☑ | Akışlı yanıt OpenAI olay adlarını kullanır, `[DONE]` YOKTUR |
| MT-COMPAT-025 | ☑ | Gömülü `data:` URI bir eke çevrilir, sohbet geçmişine gömülmez |
| MT-COMPAT-030 | ☑ | `Idempotency-Key` + `stream: true` `/v1/responses`'ta da `400` |
| MT-COMPAT-031 | ☑ | Boş gövdeyle oluşturma: `conv_` önekli kimlik döner |
| MT-COMPAT-032 | ☑ | Metadata ile oluşturma: yalnız metin alanları geri döner |
| MT-COMPAT-033 | ☑ | Bozuk gövde sessizce yutulmaz, `400` döner |
| MT-COMPAT-034 | ☑ | Kullanılmamış konuşma `GET`'i `200` boş döner, `404` DEĞİL |
| MT-COMPAT-035 | ☑ | Kullanılmış konuşmanın `created_at`'i oturum oluşturma zamanını yansıtır |
| MT-COMPAT-037 | ☑ | Silme, altındaki oturumu da siler |
| MT-COMPAT-038 | ☑ | Kullanılmamış bir konuşmayı silmek hata değil, `deleted: false` döner |
| MT-COMPAT-040 | ☑ | Öge listesi mesaj/tool çağrısı/tool sonucu sırasını korur |
| MT-COMPAT-042 | ☑ | Kullanılmamış konuşmada öge listesi boş dizi döner |
| MT-COMPAT-045 | ☑ | `responses.create` + `responses.stream` + `previous_response_id` zinciri |
| MT-COMPAT-046 | ☑ | `conversations.create/retrieve/items.list/delete` zinciri |
| MT-COMPAT-047 | ☑ | `chat.completions.create` (akışlı/akışsız) + `NotFoundError` yakalama |
| MT-COMPAT-048 | ☑ | `Authorization` başlığı eksikse `401` |

## Ayrıntı taşıyan case'ler (11)

## MT-COMPAT-023 — Konuşma kimliğine çapraz kiracı erişimi `404` döner

**Gerçek sonuç**
**KALDI - HATA-S2-005 (Orta).** Ikinci cagri (kiraci-beta, ayni conversation kimligi manuel-conv-023) beklenen HTTP 404/not_found_error yerine HTTP 200 dondu, gercek bir model yaniti uretti. Veri SIZINTISI YOK - dogrulandi: GET /api/sessions/manuel-conv-023 tenant-alfa basligiyla hala yalniz orijinal 2 mesaji gosteriyor (tenant-beta'nin mesaji ORAYA yazilmadi); tenant-beta basligiyla ayni ID icin TAMAMEN AYRI, bagimsiz bir SessionRecord (tenantId: kiraci-beta) sessizce OLUSTURULMUS. Kok neden: OpenAICompatSupport.IsOwnedByTenantAsync (OpenAICompatSupport.cs:103-113) store.GetAsync(sessionId) cagirir ve kendi kod yorumunda 'Bellek ici depo kiraci filtresi uygulamaz' (satir 100-101) diye ACIKLAR - ama bu VARSAYIM YANLIS: InMemorySessionStore._sessions sozlugu (TenantId, Id) BILESIK anahtarla tutuluyor (InMemorySessionStore.cs:20) ve GetAsync (satir 53-59) DAIMA ambient _tenantContext.TenantId ile sorguluyor - yani depo ZATEN kiraci-kapsamli. Sonuc: tenant-beta baglaminda store.GetAsync('manuel-conv-023') HICBIR ZAMAN tenant-alfa'nin kaydini GOREMEZ (farkli anahtar), record her zaman null donuyor, IsOwnedByTenantAsync'in 'record?.TenantId is null -> true (izin ver)' dali her zaman tetikleniyor - HTTP katmanindaki 404 reddi PRATIKTE HICBIR ZAMAN calismiyor (olu kod), yerine sessizce yeni bir oturum aciliyor. Veri gizliligi baska bir mekanizmayla (depo seviyesi kiraci ayrimi) korunuyor ama kodun kendi belgeledigi/iddia ettigi acik 404 reddi calismiyor.

---

**GECTI (Aile Q, bu kosum).** Kok neden yapisaldi: `ISessionStore.GetAsync` UCUN DORDUNUN (InMemory + Postgres/Sqlite/SqlServer) hepsinde ambient kiraciyle filtreleniyordu, dolayisiyla capraz kiraci sorusu hicbir zaman dogru cevaplanamiyordu. Duzeltme: yeni `ISessionStore.GetOwnerTenantIdAsync(sessionId)` metodu eklendi - kiraci filtresi UYGULAMADAN kaydin gercek sahibini doner (InMemory: `_sessions` anahtarlarini tarar; SQL: yeni `SelectSessionOwner` sorgusu, `WHERE id = @id` - `tenant_id` filtresi YOK). `OpenAICompatSupport.IsOwnedByTenantAsync` artik bunu kullaniyor; `OpenAIResponsesEndpoints.cs`'in zaten cagirdigi bu ortak yardimci sayesinde `/v1/responses` da otomatik duzeldi. Canlı Postgres'e karsi yeniden uretildi (gecici `mt_fin_q` semasi): ikinci cagri (kiraci-beta, ayni conversation kimligi) artik `HTTP 404` + `error.type: not_found_error` donuyor; kiraci-alfa'nin oturumu (`GET /api/sessions/manuel-conv-023-q`) YENI bir tur ALMADAN, orijinal 2 mesajla degismeden kaldi. Regresyon: `SessionStoreContract.GetOwnerTenantIdAsync_ambient_kiraciden_bagimsiz_gercek_sahibi_doner` (4 saglayicida da kosar) + `OpenAIConversationsCrossTenantTests.Konusma_kimligine_capraz_kiraci_responses_cagrisi_404_doner`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-026 — Tanınmayan ikili tür `400` ile reddedilir

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi base64 govdesi (TVpqdW5rZGF0YQ==) coder MZjunkdata metnine - bu gecerli ASCII/UTF-8'dir, AttachmentTypeGuard.LooksLikePlainText (AttachmentTypeGuard.cs:135-137) tarafindan BILEREK text/plain olarak kabul edilir (guard sadece taninan ikili imzalari VEYA gecerli UTF-8 metni kabul eder; ne biri ne digeri olan icerik reddedilir). Senaryo yazarinin niyeti gercek bir ikili/non-UTF8 govde test etmekti ama saglanan payload bunu karsilamiyordu. Govde gercek bir PE-benzeri ikili (4D 5A 90 00 03 00 00 00 04 00 00 00 FF FF 00 00, base64 TVqQAAMAAAAEAAAA//8AAA==) ile degistirildi ve DOGRU sekilde HTTP 400, 'Dosya turu taninmadi. Desteklenen turler: application/pdf, audio/*, image/gif, image/jpeg, image/png, image/webp, text/plain.' dondu - guard tasarlandigi gibi calisiyor, urun kusuru YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-027 — Akışsız yolda sağlayıcı hatası `502` döner

**Gerçek sonuç**
**KALDI - HATA-S2-003 (Yuksek).** Once azure-destek hic kayitli DEGILDI (bu ortamda Azure kimligi tanimsizdi -> agentPrism.AddAgent yalniz azureOpenAIEnabled true ise cagriliyor, Program.cs:556-578) - bu kismi doküman duzeltmesidir (asagida). Gecici sahte Azure Endpoint+ApiKey+DefaultDeployment ile agent kayitli hale getirilip GERCEK bir sağlayici hatasi (DNS cozulemedi) tetiklendi. Beklenen HTTP 502 + error.type: upstream_error (OpenAICompatSupport.Error), GERCEKLESEN: HTTP 500, govde ASP.NET Core'un GENEL ProblemDetails sayfasi ({"type":"...","title":"An error occurred while processing your request.","status":500}) - OpenAI hata zarfi DEGIL. Kok neden: OpenAIResponsesEndpoints.cs:213 akissiz yolun catch filtresi hala K-296 ONCESI dar listeyi tasiyor (catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)) - gercek saglayici istisnasi (System.AggregateException, DNS hatasi) bu filtreden GECMIYOR, yakalanmadan ASP.NET Core'un varsayilan isleyicisine sizip 500 ProblemDetails uretiyor. Kapsam: OpenAIChatCompletionsEndpoints.cs:145 (/v1/chat/completions akissiz yolu) AYNI dar filtreyi tasiyor - iki compat ucunun da akissiz yollari etkileniyor. K-296'nin duzeltmesi yalniz akisli varyantlari (ResponsesStream, ChatCompletionsStream) kapsamis, kardes akissiz yollari KACIRMIS.

---

**Duzeltildi (Aile H, bu kosum).** Ucuncu kok neden alani da AYNI dar filtreyi
tasiyordu: `Endpoints/AgentEndpoints.cs` `ExecuteBufferedAsync` (akissiz
`/api/agents/{name}/run` yolu, `Idempotency-Key` ile tetiklenir). Uc dosyanin
ucunde de dar `when` filtresi kaldirildi; K-296/K-384'un akisli kardeslerde
(ResponsesStream, ChatCompletionsStream, AgentEndpoints.ExecuteStreamingAsync)
zaten uyguladigi desen — duz `catch (Exception ex)`, `OperationCanceledException`
ayrica ve ONCE yakalanir (istemci baglantiyi kesince 502 yazmaya calisilmaz) —
akissiz uc yola da uygulandi.

Ayni sahte Azure Endpoint/ApiKey/DefaultDeployment yontemiyle CANLI PostgreSQL'e
karsi yeniden dogrulandi (temiz `mt_fin` semasi):
`POST /v1/responses` → `HTTP 502`, `error.type: upstream_error`, govde
`{"error":{"message":"Retry failed after 4 tries. (nodename nor servname
provided...)","type":"upstream_error"}}` — beklenen sonuçla BIREBIR eslesiyor.
`POST /api/agents/azure-destek/run` (`Idempotency-Key` ile akissiz yol,
ucuncu kok neden alani) da ayrica dogrulandi: `HTTP 502`,
`title: "Agent calistirilamadi"`, `application/problem+json`. Regresyon testi:
`tests/AgentPrism.AspNetCore.FunctionalTests/ProviderOutageErrorHandlingTests.cs`
— gercek saglayici SDK istisnalarini (`Exception`'dan DOGRUDAN turer, whitelist'e
UYMAZ) taklit eden `ThrowingModelProvider` ile ucu de (akissiz run, `/v1/responses`,
`/v1/chat/completions`) kapsar; fix geri alinip calistirildiginda ucu de KIRMIZI
verdigi (500/ciplak ProblemDetails) ampirik olarak dogrulandi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-028 — Akışlı yolda sağlayıcı hatası `event: error` çerçevesi üretir (düzeltildi)

**Gerçek sonuç**
Basliklar 200/text/event-stream gonderildi, baglanti cercevesiz kapanmadi: event: error cercevesi geldi (DNS hatasi mesajiyla). Capraz dogrulama: /api/runs?agentName=azure-destek&take=1 bu calistirmayi Failed durumunda gosterdi. K-296 fix'i akisli yolda DOGRU calisiyor - regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-029 — Onay gerektiren bir tool çağrısı `/v1/responses` üzerinden nasıl görünür

**Gerçek sonuç**
**KALDI - HATA-S2-004 (Yuksek).** HTTP yaniti: status: completed, output: [] (BOS). Run kaydi da status: Completed (eventCount:3: run.started, message.completed BOS metinle, run.completed - hicbir run.awaiting_input yok). Ancak GET /api/sessions/manuel-conv-029 gercek durumu gosteriyor: mesaj gecmisinde bir toolApprovalRequest var (cancel_order, requiresConfirmation:true) VE state.stateBag._pendingApprovalRequests dizisinde bekleyen bir kayit var - tool GERCEKTEN onay bekliyor. Compat ucu (hem HTTP yaniti hem run kaydi) bu bekleyen onayi TAMAMEN gizliyor; ikisi de tutarli sekilde completed diyor ama gercek durum AwaitingInput'tur. cancel_order hicbir zaman calismadi (dogru - onay verilmedi) ama caller'in bunu /v1/responses uzerinden gormesinin hicbir yolu yok; yonetim API'sine (dosya 21) gitmeden sessizce takili kalir.

---

**🔧 Kapanış güncellemesi (2026-08-15, Aile V — HATA-S2-004 düzeltildi).** İki
bağımsız kök neden bulundu ve ikisi de düzeltildi:

1. **Durum etiketi.** `RunRecordingAgent`'ta kök (`Depth == 0`) bir
   çalıştırmanın `AwaitingApproval` olarak kapanması eskiden yalnız kuyruktan
   koşan çalıştırmalarda (`AgentPrismRunOptions.SuspendOnApproval == true`)
   uygulanıyordu; senkron/compat/MCP/A2A yolu bu bayrağı hiç ayarlamıyordu ve
   onay bekleyen bir tool çağrısı taşıyan bir çalıştırma her zaman `Completed`
   olarak kapanıyordu. `SuspendOnApproval` kaldırıldı (artık `Depth == 0` ve
   bekleyen onay varsa yol fark etmeksizin `AwaitingApproval`); `pending_approvals`
   deposuna yazma davranışı (K-372) DEĞİŞMEDİ — yalnız kuyruk yolu yazar, çift
   karar yarışı riski yeniden açılmadı.
2. **Wire-seviyesi gizleme.** `Microsoft.Agents.AI.Hosting.OpenAI`'ın (alpha
   paket) `OpenAIResponses.WriteResponse`'u decompile ile doğrulandı:
   `Response.Status` HER ZAMAN `ResponseStatus.Completed` olarak sabit
   yazılıyor ve `ToolApprovalRequestContent`'i tanımayan içerik dönüştürücüsü
   onu `output`'tan sessizce düşürüyor — bu, MAF'ın kendi alpha paketinin bir
   sınırlaması. `OpenAIResponsesEndpoints.HandleAsync` artık üretilen JSON'a
   (`AppendPendingApprovalOutputItems`) yama uyguluyor: bekleyen her onay
   isteği, gerçek OpenAI Responses API'sinin `function_call` öge şemasıyla
   (id/type/status/call_id/name/arguments) birebir aynı biçimde `output`'a
   eklenir — MAF'ın normal (onay istemeyen) bir tool çağrısı için ürettiği
   ögeyle SDK açısından ayırt edilemez.

Ampirik doğrulama (canlı sunucuya karşı, birebir bu case'in `curl`'ü):
`status: "completed"` (OpenAI Responses API'de fonksiyon çağrısı zaten
`requires_action` değil böyle temsil edilir), `output` artık BOŞ DEĞİL —
`{"type":"function_call","name":"cancel_order","arguments":"{\"orderId\":\"ORD-1001\"}",...}`
içeriyor. `GET /api/runs?...` artık `status: "AwaitingApproval"` döndürüyor
(`Completed` DEĞİL) — ilk gözlemin ("iki gözlem de tutarlı ama ikisi de
yanlış") tersine artık HTTP yanıtı ve run kaydı TUTARLI ve DOĞRU: ikisi de
bekleyen bir tool çağrısı olduğunu açıkça gösteriyor.

Regresyon testleri: `OpenAICompatTests.Responses_onay_bekleyen_tool_cagrisini_output_ta_gosterir`,
`ApprovalEndpointTests.Senkron_akissiz_calistirma_onay_isteyince_AwaitingApproval_ile_kapanir`,
`ApprovalEndpointTests.Senkron_akisli_calistirma_onay_isteyince_AwaitingApproval_ile_kapanir`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-036 — Konuşma `GET`'ine çapraz kiracı erişimi `404` döner

**Gerçek sonuç**
**KALDI - HATA-S2-005 kapsam genislemesi.** Beklenen HTTP 404 yerine HTTP 200 + gecerli bir conversation govdesi dondu (kiraci-beta, kiraci-alfa'nin sohbetinin VARLIGINI dogrulayamadi ama sessizce 'gecerli, bos' bir govde aldi - ayni kok neden: OpenAIConversationsEndpoints.cs:130 dogrudan sessions.GetAsync(id) cagirir, IsOwnedByTenantAsync gibi acik bir denetim YOK; InMemorySessionStore zaten (TenantId,Id) ile kapsadigi icin kiraci-beta baglaminda record hep null donuyor ve kod bunu 'hic kullanilmamis ID' (MT-COMPAT-034 davranisi) ile ayirt edemiyor. Veri sizintisi yok (icerik gorunmuyor), yalniz acik 404 sinyali eksik.

---

**GECTI (Aile Q, bu kosum).** `RetrieveAsync` artik `sessions.GetAsync`'ten ONCE, `OpenAICompatSupport.IsOwnedByTenantAsync` (yeni `ISessionStore.GetOwnerTenantIdAsync` uzerinden, kiraci filtresi UYGULAMADAN) ile sahiplik denetimi yapiyor - dosyanin kendi local `IsOwnedByTenant` yardimcisi (olu koddu, `record` zaten hep null geliyordu) kaldirildi. Kok neden ve tasarim: MT-COMPAT-023'un notuna bakiniz. Canlı Postgres'e karsi yeniden uretildi: `GET /v1/conversations/manuel-conv-036-q` kiraci-beta basligiyla artik `HTTP 404` + `error.type: not_found_error` donuyor; ayni ID'yi kiraci-alfa kendi basligiyla sorguladiginda (negatif kontrol) `HTTP 200` olarak dogru calismaya devam ediyor. Regresyon: `OpenAIConversationsCrossTenantTests.Konusma_GET_ucuna_capraz_kiraci_erisimi_404_doner`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-039 — Çapraz kiracı silme `404` döner, başka kiracının oturumunu silmez

**Gerçek sonuç**
**KALDI - HATA-S2-005 kapsam genislemesi.** Adim 2: beklenen HTTP 404 yerine HTTP 200, deleted:false dondu (ayni kok neden: RetrieveAsync/DeleteAsync'de acik kiraci denetimi yok, depo scoping'i geregi kiraci-beta icin kayit gorunmuyor, silme sessizce 'zaten yok' sayiliyor). Adim 3 DOGRU: kiraci-alfa'nin oturumu HTTP 200 ile hala var, 2 orijinal mesaj degismedi - veri kaybi/sizinti YOK, silme fiilen gerceklesmedi (guvenlik acisindan zararsiz, yalniz beklenen acik 404 sinyali eksik).

---

**GECTI (Aile Q, bu kosum).** `DeleteAsync` artik `sessions.GetAsync`'ten ONCE `OpenAICompatSupport.IsOwnedByTenantAsync` ile sahiplik denetimi yapiyor. Kok neden ve tasarim: MT-COMPAT-023'un notuna bakiniz. Canlı Postgres'e karsi yeniden uretildi: Adim 2 (`DELETE .../manuel-conv-039-q` kiraci-beta basligiyla) artik `HTTP 404` donuyor; Adim 3 (`GET /api/sessions/manuel-conv-039-q` kiraci-alfa basligiyla) `HTTP 200`, orijinal 2 mesaj degismeden - kiraci-alfa'nin oturumu kiraci-beta'nin basarisiz silme girisiminden ETKILENMEDI. Regresyon: `OpenAIConversationsCrossTenantTests.Konusma_silme_ucuna_capraz_kiraci_erisimi_404_doner_ve_sahibin_oturumu_kalir`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-041 — `limit` parametresi kırpar, `has_more` kırpma olduğunda `true` döner (düzeltildi)

**Gerçek sonuç**
limit=2 ile data tam 2 oge tasidi, has_more: true (gercek toplam 4, kirpma dogru isaretlendi) - fix regresyonu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-043 — Öge listesine çapraz kiracı erişimi `404` döner

**Gerçek sonuç**
**KALDI - HATA-S2-005 kapsam genislemesi.** Beklenen HTTP 404 yerine HTTP 200, data: [] dondu (ayni kok neden: ListItemsAsync'te acik kiraci denetimi yok; kiraci-beta icin kayit gorunmedigi icin 'kullanilmamis konusma' (MT-COMPAT-042 davranisi) ile ayni bos-liste yanitina dusuyor). Icerik SIZMADI (bos liste, alfa'nin gercek mesajlari gorunmedi) - yalniz acik 404 reddi yerine sessiz bos liste donuyor.

---

**GECTI (Aile Q, bu kosum).** `ListItemsAsync` artik `sessions.GetAsync`'ten ONCE `OpenAICompatSupport.IsOwnedByTenantAsync` ile sahiplik denetimi yapiyor. Kok neden ve tasarim: MT-COMPAT-023'un notuna bakiniz. Canlı Postgres'e karsi yeniden uretildi: `GET /v1/conversations/manuel-conv-043-q/items` kiraci-beta basligiyla artik `HTTP 404` + `error.type: not_found_error` donuyor (eskiden `data: []` ile sessizce ayirt edilemeyen bos liste). Regresyon: `OpenAIConversationsCrossTenantTests.Oge_listesine_capraz_kiraci_erisimi_404_doner`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-COMPAT-044 — `POST /v1/conversations/{id}/items` desteklenmez, düz 404 döner

**Gerçek sonuç**
**Dokuman duzeltmesi.** Beklenen 'cikri 404' yerine HTTP 405 (Allow: GET, HEAD) + application/problem+json govde dondu - rota sablonu GET icin zaten kayitli oldugundan ASP.NET Core dogru sekilde 405 uretiyor (rota HIC bagli degilmis gibi bir 404 degil). Bu orijinal varsayimdan daha spesifikasyona uygun bir davranis; Beklenen sonuc metni koşumda duzeltildi (AGENTS.md: dokuman-kod celismesinde dokuman duzeltilir), urun kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Stok Python `openai` SDK ile uçtan uca (K-036 / K-043 doğrulaması)

Bu bölüm [`00-INDEKS.md`](../../00-INDEKS.md)'nin ortam tablosundaki *"Python — stok
`openai` istemcisi ile uyumluluk doğrulanır"* maddesini karşılar. K-036/K-043
kararları bu akışı `openai` **2.52.0** ile bir kez ölçtü (üretim oturumunda);
bu case'ler insan tarafından **yeniden** koşulur.

---

## MT-COMPAT-049 — Yanlış bearer token `401`

**Gerçek sonuç**
HTTP: 401. AgentPrismEndpointFilter compat uclarina da uygulaniyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bu dosyada kanıtlanmayan, kod okurken fark edilen şüpheler

Bu bölüm bir kusur listesi değildir — koşum aşamasında doğrulanacak
**şüphelerdir** (`PROMPT.md` §7.3).

- **MT-COMPAT-029**: `cancel_order` gibi onay gerektiren bir tool çağrısının
  `/v1/responses` üzerinden tam olarak nasıl serileştiği (`OpenAIResponses
  .WriteResponse`'un `ToolApprovalRequestContent`'i nasıl işlediği) MAF'ın
  kendi kaynağından **doğrulanmadı** — yalnız AgentPrism tarafındaki
  `agent.RunAsync` çağrısının hiçbir onay-özel dallanma taşımadığı görüldü.
  Olası sonuçlar: boş `output`, yarım metin veya MAF'ın kendi hata fırlatması.
  Koşum gerçek değeri kaydeder; tutarsızlık çıkarsa HATA şablonuyla kaydedilir.
- **MT-COMPAT-028 — DÜZELTİLDİ (2026-08-10).** `OpenAIResponsesEndpoints
  .ResponsesStream`'in dış `catch`'i artık dar bir tip listesiyle sınırlı
  değil; `OperationCanceledException` dışındaki HER istisna bir `error`/
  `response.failed` çerçevesine dönüşür. MAF'ın `WriteResponseStreamAsync`'inin
  KENDİ İÇİNDE ayrıca bir çevirme yapıp yapmadığı hâlâ decompile ile
  doğrulanmadı ama artık dış katman zaten her durumu yakaladığı için pratik
  sonucu değiştirmiyor.
- **MT-COMPAT-041 — `has_more` kısmı DÜZELTİLDİ (2026-08-10).** Kırpma
  gerçekleştiğinde artık `true` döner. Gerçek OpenAI'nin `items.list`
  sözleşmesindeki `after`/`before` imleç parametreleri hâlâ desteklenmiyor —
  bu bir kusur değil, ayrı bir **yetenek** boşluğudur (`limit`'in ötesinde
  sayfa isteme yolu yok); kapsam dışı bırakıldı.

---
