# ADAYLAR — Normalize Edilmiş Planlama Kuyruğu

> **Durum (2026-08-26):** Bu dosya yalnız plana dönüşebilecek yetenekleri
> taşır. Eski listenin 43 açık görünen F-ID'si yeniden yargılandı: **5 aday**,
> **5 kusur**, **6 karar/uyumluluk eşiği**, **15 ölçüm bekleyen iddia** ve
> **12 arşivlenen veya birleştirilen kalem**. F-164 bu sayımdan önce kusur
> kanalında kapandı. Tam kanıt ve her ID'nin varış yeri:
> [`kesif/2026-08-26-aday-envanter-normalizasyonu.md`](kesif/2026-08-26-aday-envanter-normalizasyonu.md).
>
> **Ek (2026-08-26, ikinci tur):** Feature keşfi turu üç yeni aday ekledi
> (**F-166, F-167, F-168**) ve **F-95**'i karar kanalından adaylığa geri
> aldı — MAF 1.19.0 onu bekleten kancayı gönderdi. Tur kaydı:
> [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md).
>
> **Ek (2026-08-26, üçüncü tur — planlama):** Sıralama kanıt doğrulamasıyla
> yeniden yargılandı ve **F-109 · F-149 · F-166** plana dönüştü
> ([Faz 112](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) ·
> [Faz 113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) ·
> [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md)); bölümleri bu dosyadan
> **silindi**. Doğrulama üç aday metnini de düzeltti — düzeltmeler
> § *Sıralamayı Değiştiren Ölçümler*'dedir.
>
> **Ek (2026-08-26, dördüncü tur — planlama):** **F-168 · F-67** plana dönüştü
> ([Faz 115](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) ·
> [Faz 116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md)); bölümleri bu dosyadan silindi.
> Doğrulama ikisinin de aday metnini düzeltti — § *Sıralamayı Değiştiren
> Ölçümler*. Sıralamada **iki aday** kaldı.
>
> **Ek (2026-08-26, beşinci tur — planlama):** **F-167 · F-152** plana dönüştü
> ([Faz 117](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) ·
> [Faz 118](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md)). **Sıralanabilir aday kalmadı.**
> Kuyrukta iki kalem var ve ikisi de bugün faz değildir: F-95 ölçüm bekler,
> F-165 tek faza sığmaz. Yeni aday üretmek için `aday-kesfi` koşulur.
>
>
> **Ek (2026-09-01, tüketici turu):** Dış bir tüketici raporu koda karşı
> ölçüldü ([`kesif/2026-09-01-tuketici-feature-talepleri.md`](kesif/2026-09-01-tuketici-feature-talepleri.md)).
> Dört kalem **doğrudan plana** dönüştü — bu dosyada hiç sıralanmadılar, çünkü
> kanıtları raporla birlikte geldi ve aynı turda doğrulandı:
> **F-172** → [Faz 129](arsiv/fazlar/129-IS-KUYRUGU-LANELERI.md) · **F-173** →
> [Faz 130](arsiv/fazlar/130-URETILEN-SEMANIN-KISITLARI.md) · **F-174** →
> [Faz 131](arsiv/fazlar/131-YAPISAL-YANIT-DOGRULAMA-SEAMI.md) · **F-175** →
> [Faz 132](arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md). Aynı turdan **dört kalem**
> § *Bekleyen Kalemler*'e girdi (F-176 · F-177 · F-178 · F-179); hepsi bir
> fazın tamamlanmasını bekliyordu. **Ek (2026-09-02):** Faz 129-132 kapandı ve
> üçü plana dönüştü — **F-178'in job/kuyruk metrikleri yarısı** →
> [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md) · **F-177** →
> [Faz 134](arsiv/fazlar/134-SINIRLI-YANIT-ONARIMI.md) · **F-176** →
> [Faz 135](arsiv/fazlar/135-URETILEN-SEMANIN-NESNE-GRAFI.md). Kuyrukta **F-178'in kalan
> yarısı** (model deneme telemetrisi) ve **F-179** (dinamik routing) kaldı;
> ikisi de gerçek üretim trafiği/olayı bekliyor.
>
>
> **Ek (2026-09-03, tüketici turu 2):** Aynı tüketicinin ikinci raporu koda karşı
> ölçüldü ve **üç iddiasının üçü de doğrulandı**. Kalemler bu dosyada
> sıralanmadı — kanıtları raporla geldi ve aynı turda yeniden üretildi:
> **F-182** (paket kimliğinin tekilliği, AP-REQ-002) →
> [Faz 136](arsiv/fazlar/136-PAKET-KIMLIGININ-TEKILLIGI.md); repro kilitli, `1.0.0-preview.1`
> tag'inin önüne girer. **F-183** (custom job dispatch, AP-REQ-001) ve **F-184**
> (voice descriptor sağlayıcı üstverisi, AP-REQ-003) →
> [Faz 137](arsiv/fazlar/137-IS-TURUNUN-ACIK-ANAHTARI.md) ve
> [Faz 138](arsiv/fazlar/138-SES-TANIMININ-SAGLAYICI-USTVERISI.md); ikisi de tüketiciden
> kesin sözleşme yanıtı aldı.
>
> F-183'ün ölçümü raporun bulduğundan ağır çıktı: sevk edilen
> `samples/AgentPrism.Samples.CustomJobHandler` örneği `JobKind.AgentBatch`
> bildirir ve `AddAgentPrism()`'den sonra kaydolur, yani gerçek bir worker'da
> **hiç çalışmaz** — testi yalnız DI kaydını ölçüyor. Bu kusur ayrı bir kayıt
> açmaz; F-183'ün düşen testidir.
>
>
> **Ek (2026-09-05, tüketici turu 4):** ProdigyEnabler'ın `0.0.0-preview.0.589`
> raporu koda karşı ölçüldü ([kesif](kesif/2026-09-05-tuketici-turu-4-olcumu.md)).
> On dört iddianın on üçü doğru çıktı; yanlış olan tek iddia bir risk satırıydı
> (`MigrationDescriptor` public sanılmış, `internal` çıktı). Dört kalem **doğrudan
> plana** dönüştü — bu dosyada hiç sıralanmadılar, çünkü kanıtları raporla
> birlikte geldi ve aynı turda doğrulandı: **F-193** →
> [Faz 145](145-OLAY-AKISININ-CERCEVE-SOZLESMESI.md) · **F-194** (A2 + F3 birleşti) →
> [Faz 146](146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) · **F-195** →
> [Faz 147](147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md) · **F-196** →
> [Faz 148](148-OTURUM-SAHIPLIGININ-KALICILIGI.md).
>
> Planlama ölçümü raporda **olmayan** bir bulgu üretti: `POST /runs/{id}/replay`
> ve `/v1/chat/completions` de gerçek bir `run` başlatıyor ve ikisi de
> `RunAuthorizationGate`'i çağırmıyor. Faz 139'un "dört run başlatan yüzey"
> iddiası (K-670) eksiktir; gerçek sayı **altıdır**. Faz 147 bunu kapsıyor.
>
> Aynı turdan **sıralanmayan** kalemler: F2 (zorunlu extension binding profili) ·
> F6 (ses/WebSocket test harness'i) · F7 (migration plan artifact'i) — üçünün de
> boşluğu gerçek, talep kanıtı yok. **Elenenler:** F4 · F5 · F8. Gerekçeler keşif
> kaydındadır.
> Faz durumu yalnız üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'dedir.
> Bir kusur bu dosyaya geri girmez; `kusur-giderme` kanalına gider. Kapatılmış
> kararın yeniden açılması kullanıcı kararıdır. Ölçüm bekleyen iddia, kanıt
> üretmeden aday olmaz.

## Okuma Sırası

**Sıralanabilir aday kalmadı** (2026-08-26). Bu dosyaya bakma sebebin
şunlardan biridir:

| İhtiyaç | Nereye bak |
|---|---|
| Sıradaki fazı seçmek | **Buraya değil** — üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)'ye. Planlanmış fazlar `docs/` kökündedir |
| Yeni aday üretmek | `aday-kesfi` skill'ini koş; bu dosya onun çıktısını alır |
| Bekleyen iki kalemin durumu | § *Bekleyen Kalemler* — ikisi de bugün faz değildir |
| Bir F-ID nereye gitti | § *Aday Olmayan Açık Kayıtlar* tablosu |
| Bir sıralama neden değişti | § *Sıralamayı Değiştiren Ölçümler* |

Geçmiş tur anlatıları, kapanmış aday gövdeleri ve elenen kalemler bu dosyada
tekrarlanmaz; ilgili keşif ve arşiv kayıtlarındadır.

## Değerlendirme Ölçütleri

| Ölçüt | Soru |
|---|---|
| **Değer** | Bu olmadan AgentPrism'i kim kullanamaz? |
| **Maliyet** | Kaç paket, kaç yeni public tip, kaç migration? |
| **Risk** | Bir tasarım kuralını, AOT veya bundle bütçesini zorluyor mu? |
| **Hazırlık** | MAF veya .NET ekosisteminde hazır mı, sıfırdan mı? |

Bir adayın `Mercek` satırı aşağıdaki destekleyen mercekleri numarayla sayar.

| # | Mercek | Sorusu |
|---|---|---|
| 1 | **Benimseme** | İlk agent'a kadar geçen süreyi kısaltır mı? |
| 2 | **Üretim işletimi** | Gece 03:00'te nöbetçi mühendisin işine yarar mı? |
| 3 | **Kurumsal satın alma** | Hangi kurumsal kapıyı açar? |
| 4 | **Performans ve AOT** | Sıcak yol ve tahsis bütçesi korunur mu? |
| 5 | **API ergonomisi** | Yanlış kullanım derlemede yakalanır mı? |
| 6 | **Ekosistem yerleşimi** | Aspire, OTel, MCP, A2A ve DI ile doğal mı oturur? |
| 7 | **Ölçme–iyileştirme** | Üretim verisini geliştirmeye geri besler mi? |
| 8 | **Maliyet (FinOps)** | Tüketicinin model faturasını düşürür mü? |

## Sıralama — bir aday

2026-09-05 itibarıyla sıralanabilir **bir** aday vardır. İkisi de MAF yüzey
taramasından çıkmıştı (`maf-api-kesfi`, 1.18.0 → 1.20.0 tam dump diff'i +
kullanım ölçümü: 324 MAF public tipin 203'ü AgentPrism kaynağında hiç geçmiyor);
biri aynı gün plana dönüştü.

| Sıra | Aday | Neden bu sırada |
|---|---|---|
| 1 | **F-192** · Harness'in döngü yeteneği | Yeni yetenek. Pinlenmiş sürümde **zaten hazır**; yükseltme beklemez |

> **F-191 · Alt-agent bekleme zaman aşımı → [Faz 144](arsiv/fazlar/144-ALT-AGENT-BEKLEME-SINIRI.md)**
> (2026-09-05). Bağımlılığı olan MAF 1.20.0 yükseltmesi aynı oturumda yapıldı ve
> kalemin çerçevesi ölçümle değişti: yükseltme **düz agent yolunun** süresiz
> asılma riskini kod yazılmadan kapattı (`WaitTimeout` varsayılanı 00:05:00),
> geriye harness yolu · sayının AgentPrism tarafından seçilmesi · zaman aşımının
> `run` kanıtına yazılması kaldı. Plan bu üçünü kapsar.

> **Ek (2026-09-03, tüketici turu 3).** ProdigyEnabler'ın `1.0.0-preview.1`
> raporu ölçüldü ([kesif](kesif/2026-09-03-tuketici-turu-3-olcumu.md)). On iki
> iddianın onu doğru çıktı; ikisi yanlıştı ve **ikisi de bizim dokümanımızın**
> ürettiği yanlış anlamaydı — `kusur-giderme` ile kapandı ve
> `sevk_edilen_olay_anlatisi()` kapısı eklendi.
>
> Beş kalem aynı gün plana döndü ve bu listeden **çıktı**: **F-185** →
> [Faz 139](arsiv/fazlar/139-CALISTIRMA-VE-OTURUM-YETKILENDIRMESI.md) · **F-186** →
> [Faz 140](arsiv/fazlar/140-ICERIK-GUARDININ-KAYNAGI.md) · **F-187** →
> [Faz 141](arsiv/fazlar/141-GENISLETILEBILIR-CALISTIRMA-OLAYI.md) · **F-188** →
> [Faz 142](arsiv/fazlar/142-ONAY-ISTEGININ-SUNUMU.md) · **F-189** →
> [Faz 143](arsiv/fazlar/143-TOOL-ARGUMANININ-SOZLESME-TESTLERI.md).
>
> Aynı turdan **sıralanmayan** kalemler (talep kanıtı zayıf veya tüketici
> kendisi çözebiliyor): session transkript dışa aktarımı · akış delta'larının
> sunucuda birleştirilmesi · bağlama başına endpoint. Gerekçeleri keşif
> kaydındadır; koşulları oluşursa yeniden aday olurlar.

Üç planlama turu dokuz adayın yedisini faza çevirdi:

| Aday | Faz |
|---|---|
| F-109 | [112 — Replay'in İstemci Tool Sözleşmesi](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) |
| F-149 | [113 — Sağlayıcı Arıza Sınıflandırmasının Genişleme Noktası](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) |
| F-166 | [114 — Çalıştırma-İçi Bütçe Tavanı](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md) |
| F-168 | [115 — Eval'in Başsız Koşucusu](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) |
| F-67 | [116 — Performans Tahsis Kapısı](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md) |
| F-167 | [117 — MCP Tasks Uzantısı](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) |
| F-152 | [118 — Yargıç Başına Checkpoint](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md) |

Kalan ikisi § *Bekleyen Kalemler*'dedir ve **sıralamaya girmez**.

### F-192 · Harness'in döngü (loop) yeteneği

**Sorun:** `HarnessAgentOptions` iki üye taşır — `LoopEvaluators` ve
`LoopAgentOptions` — ve AgentPrism **ikisini de set etmiyor**
([`AgentDefinitionCompiler.Agents.cs:172`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.Agents.cs#L172)).
MAF bunu harness'in birinci sınıf, opt-in yeteneği yapmıştır
([PR #6544](https://github.com/microsoft/agent-framework/pull/6544)): en az bir
evaluator verilince harness `LoopAgent` ile **en dıştan** sarılır. Ölçüm
(2026-09-05): `LoopAgent` ve `LoopEvaluator` AgentPrism kaynağında **0 dosyada**
geçiyor. [`MAF-GENISLEME-NOKTALARI`](MAF-GENISLEME-NOKTALARI.md) bunu
*"planlanmadı (eval'den AYRI kavram)"* diye kaydetmiş — **reddedilmemiş,
ertelenmiş**.

**Kapsam:** `HarnessSettings`'e bir bitiş ölçütü aç. MAF'ın hazır evaluator'ları:
`TodoCompletionLoopEvaluator` (AgentPrism'in `TodoProvider`'ı zaten açık ve bu
evaluator **agent moduna göre kapsanabiliyor**) · `CompletionMarkerLoopEvaluator` ·
`AIJudgeLoopEvaluator` · `BackgroundTaskCompletionLoopEvaluator` ·
`DelegateLoopEvaluator`. Döngü iterasyonları `run` kanıtına yazılır.

**Değer:** "Bitene kadar çalış" davranışı. Bugün tüketici bunu kendi dış
döngüsüyle yazmak zorunda ve o döngü `run` kanıtının dışında kalıyor.

**Mercek:** 1, 2, 8.

**Hazırlık:** **Hazır ve çekirdekte** — `Microsoft.Agents.AI` içinde; yeni paket
yok, native bağımlılık yok. İmzalar `maf-api-kesfi` ile doğrulandı (2026-09-05)
ve **pinlenmiş 1.18.0'da zaten mevcut** (`LoopAgent`, `LoopEvaluators`,
`LoopAgentOptions` üçü de). `LoopAgent : DelegatingAIAgent` olduğu için
AgentPrism'in decorator zincirine (`IAgentDecorator` · `RunRecordingAgent`)
oturur — F-95'in kancasının aksine **yapısal engel yok**.

**Maliyet:** Ölçülmedi. Yeni paket yok. Public yüzey: `HarnessSettings`'e bitiş
ölçütü + evaluator seçimi. ⚠️ Evaluator'lar MAF tipleridir; seçimi bildirimsel
bir enum olarak mı yoksa doğrudan MAF tipiyle mi açacağı L21'e (*MAF tipleri
sarmalanmadı*) takılır — planın çözmesi gereken ilk soru budur.

**Risk:** 🚨 `MaximumIterationsPerRequest` ile **karıştırılmamalıdır**: o bir
*tavan*'dır (kaçak döngü güvenliği), `LoopAgent` bir *bitiş ölçütü*'dür. İkisi
aynı ayar gibi sunulursa tüketici yanılır. Döngü model faturasını çarpar;
`LoopAgentOptions.MaxIterations` ve [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md)'ün
bütçe tavanı ile **birlikte** yargılanmalıdır. `FreshContextPerIteration`
bağlam semantiğini değiştirir.

**Bağımlılık:** **Yok.** MAF yükseltmesi beklemez — 1.18.0 yeterlidir.

**Ekosistem:** 2026-09-05 — [PR #6544](https://github.com/microsoft/agent-framework/pull/6544).

**Karşı görüş:** Talep kanıtı ölçülmedi. Hiçbir tüketici turu "bitene kadar
çalış" istemedi; bu kalem bir yüzey taramasından çıktı, bir kullanıcı acısından
değil. F-167'nin dersi geçerlidir: *"SDK maliyeti zaten ödenmiş" bir talep kanıtı
değil, yalnız bir indirimdir.*


### F-197 · Generated client's DTO collections default to `null`, not empty

**Sorun:** Faz 145's independent audit found this while writing a real-server
test for the phase's own fix: `AgentPrism.Client`'s NSwag-generated DTOs
(`AgentRunRequest.Approvals`/`ToolResults`/`AttachmentIds`/`Documents`, and
likely others across ~250 generated types) default to `= default!` (`null`),
not an empty collection. Server code that reads them unconditionally
(`request.Documents.Count`, `AgentEndpoints.cs`) throws
`NullReferenceException` the moment a caller builds a request with only the
fields they care about — exactly the ergonomics a typed client exists to
prevent. Measured (2026-09-05): calling `AgentPrismRunAgentAsync` with only
`Message` set NREs; every collection has to be explicitly initialized to `[]`
to avoid it (`tests/AgentPrism.AspNetCore.FunctionalTests/GeneratedClientSseTests.cs`
works around it this way today).

**Kapsam:** A `nswag-postprocess-client.py` pass that rewrites every
generated collection-typed property's default from `default!` to an empty
collection literal (`new List<T>()`/`new Dictionary<K,V>()` as appropriate).
Scope the fix to properties, not constructor parameters — request DTOs here
have no constructors NSwag generates arguments for.

**Değer:** Removes a silent trap for every consumer of the typed client, not
just the SSE-returning operations Faz 145 touched — `ClientCoverageTests`
only proves a method exists, never that a minimal call succeeds, so this
class of defect is invisible until a real caller hits it in production.

**Mercek:** 1, 2.

**Hazırlık:** `nswag-postprocess-client.py` already has three precedented
rewrite passes over the same generated file (enum converters, colliding
any-types) plus the fourth this phase added (SSE string responses) — the
same script, same regeneration pipeline.

**Maliyet:** Ölçülmedi. No schema change, no new package; purely a
generated-file post-process rewrite plus regression coverage across the
generated DTOs that carry a collection property.

**Risk:** A collection property that legitimately needs to stay `null` to
distinguish "not provided" from "provided empty" (a `PATCH`-style partial
update, if one is ever added) would need an explicit exemption — none exists
in the API today (every collection here is read unconditionally), but the
fix should check for one before rewriting blindly.

### F-198 · The two dual JSON/SSE client operations never call the streaming shape

**Sorun:** `/v1/responses` and `/v1/chat/completions` report BOTH
`application/json` and `text/event-stream` for their 200 response (the
request body's `stream` flag picks one at runtime); NSwag's generated
`AgentPrismOpenAIResponsesAsync`/`AgentPrismOpenAIChatCompletionsAsync`
methods generate ONLY the JSON shape (`Task<JsonElement>`/`Task<ChatCompletion>`)
and have no way to read the streaming shape at all — a caller who sets
`stream: true` through the typed client gets a JSON-deserialization crash
against a raw SSE body, the same class of defect Faz 145 fixed for the five
pure-SSE operations (F-193's `nswag-postprocess-client.py` fourth pass
explicitly does not touch these two, noted in its own docstring).

**Kapsam:** Design a shape for a dual-response typed client method — most
likely two separate generated methods (`...Async` for JSON,
`...StreamAsync` for SSE) selected by an explicit parameter, since NSwag
itself cannot express a runtime-conditional return type. Needs either a
`nswag.json`/postprocess change or acceptance that this pair stays
JSON-only in the typed client (with `HttpClient` as the documented escape
hatch for streaming OpenAI-compatible calls).

**Değer:** Closes the last two operations in the family still silently
broken for their streaming mode through the typed client.

**Mercek:** 1, 2.

**Hazırlık:** Not started — needs a design decision (two methods vs. one
with a runtime branch) before any code.

**Maliyet:** Ölçülmedi.

**Risk:** A wrong design here (e.g., silently picking JSON always) leaves
the streaming OpenAI-compatible path permanently unreachable from the typed
client without a clear error explaining why.

### Sıralamayı Değiştiren Ölçümler

Üç planlama turu (üçüncü, dördüncü, beşinci) kanıtı yeniden doğruladı
(`faz-planlama` Adım 1) ve aday metinlerini birikimli olarak düzeltti. Sıra
numaraları **ikinci turun** tablosuna göredir; plana dönen kalemler o tablodan
çıktı. Bu kayıt, bir kalem ileride yeniden açılırsa **hangi iddianın ölçümle
çürüdüğünü** korur.
Gerekçeler:

| Değişiklik | Ölçüm |
|---|---|
| **F-109 · 6 → 1** ve plana | Listedeki tek "kırık söz" kalemiydi: Faz 61 istemci tool'unu sevk etti, replay onu sessizce yarım bırakıyordu. Sınıf olarak K-627 ile aynıdır. Ayrıca aday metnindeki "kaydedilmiş sonucu oynat" seçeneği **imkânsız** çıktı — istemci tool sonucu `ToolInvocationRecord`'a hiç yazılmıyor. |
| **F-149 · 5 → 2** ve plana | Aday metni "seam tasarla" diyordu; ölçüm seam'in **yarısının zaten var olduğunu** buldu (`IRunErrorClassifier`, `TryAddSingleton` ile kayıtlı). Gerçek boşluk üç tane ve daha dar: retry'ın hiç seam'i yok, yerleşik sınıflandırıcı devralınamıyor, parmak izi hesabı erişilemez. |
| **F-166 · 1 → 3** ve plana | Karşı görüş ("ölçülmüş vaka yok") **düştü**: varsayılan kurulum 200 000 token'lık bir ağaç tavanı ilan ediyor ve o tavan tek agent'lı run'da hiçbir şey yapmıyor. Bu bir FinOps konforu değil, bir beyan hatası. Buna karşılık "kaçak döngü" gerekçesi **daraldı**: `HarnessSettings.MaximumIterationsPerRequest` bir iterasyon tavanı zaten veriyor; sayılmayan şey maliyet. |
| **F-167 · 2 → 3** | "SDK maliyeti zaten ödenmiş" bir talep kanıtı değil, yalnız bir indirimdir. Çalışma anı probu bayatlama korkusunu zaten çürüttü (sunucu bugün stateless). Geriye 1.0 öncesi **yeni bir NuGet paketi** almak kalıyor — burada en pahalı değişiklik türü budur. |
| **F-168 · 3 → 1** ve plana | Maliyet "Orta" yazılmıştı; ölçüm **küçük** buldu. İki HTTP çağrısı üretilmiş istemcide **zaten var**, eşik için gereken üç sayı (`Total`/`Passed`/`Failed`) sözleşmede var, CLI test altyapısı (`RealHttpHost` · `CliRunner`) hazır. Sunucu hiç değişmiyor; OpenAPI/TS/NSwag zinciri koşmuyor. |
| **F-67 · 2 → 2** ve plana | Kapsam gürültü ölçümüyle daraldı: CI kapısı **yalnız tahsis edilen bayt** olur (deterministik, sıfır tolerans), süre ölçülür ama kapı değildir. Yeni paketin ağırlığı gerçek restore ile sayıldı: BenchmarkDotNet 0.15.8 → **22 geçişli paket**. K-212'nin 37'sinden az ve — asıl fark — ölçüm projesi `IsPackable=false` olduğu için tüketiciye **hiç ulaşmıyor**. |
| **F-67 ile F-168 "aynı karar" iddiası zayıfladı** | Aday metni "F-67 ile **aynı** kararı ister" diyordu. Ölçüm bunu çürüttü: F-168 bir eşik **koymaz**, tüketiciden **alır** — AgentPrism kalite barı dayatmaz. F-67 ise bu depo için gerçek bir sayı seçmek zorundadır. Ortak olan yalnız "gürültülü kapı kurma" ilkesi; gürültünün kaynağı bile farklı (model belirsizliği ↔ paylaşılan CI makinesi). Bu yüzden **tek faz değil, iki ayrı faz** yazıldı. |
| **F-167 · 3 → 1** ve plana | En büyük maliyet iddiası ("Tasks extension'ı **yeni bir NuGet paketidir** ve geçişli ağırlığı sayılmalıdır") gerçek restore ile çürüdü: `ModelContextProtocol.Extensions.Tasks` 2.2.0 `.AspNetCore`'un üstüne **net 1 paket** ekliyor, geçişli ağırlık **sıfır** — on iki geçişli paketin tamamı zaten grafikte. Ayrıca `IMcpTaskStore` AgentPrism'in var olan run kaydı üzerine oturuyor: **yeni tablo ve migration gerekmiyor**. Buna karşılık ölçüm yeni bir risk buldu: SDK sözleşmesinde **kiracı parametresi yok** ve K-103'ün onay kontrolü run kuyruğa taşınınca handler'dan düşüyor. |
| **F-152 · 2 → 2** ve plana | Maliyet iddiası ("kalıcı model ve **üç SQL sağlayıcı migration'ı** gerekir") çürüdü: `UpsertAsync` **yargıç başına** çağrılıyor ve satır `Author = "judge:{ad}"` taşıyor; `IRunScoreStore.ListAsync` ve `JobRecord.Attempt` de zaten var. **Checkpoint bugün zaten veride duruyor** — eksik olan tek şey döngünün onu okuması. Yeni tablo, migration ve public yüzey **yok**. |
| **F-95 sıralamadan çıktı** | Dördüncü sıra, sahip olmadığı bir plan hazırlığını ima ediyordu. `Hazırlık` satırı zaten "🚨 İmza doğrulanmadı" diyor. **2026-09-05 güncellemesi:** imza doğrulandı ve **MAF yolu kapandı** — kanca ayrı bir alpha pakettedir (`Microsoft.Agents.AI.AgentHooks`), sözleşmesi *enforcement*'tır (kesinti/devam değil) ve `FunctionInvokingChatClient` içeren client'ı reddeder. Kalem yalnız F-141 üzerinden ilerler; bkz. § *Bekleyen Kalemler* → F-95 `Hazırlık`. |

### F-190 · MCP Tasks testlerinin tam çözüm koşumunda yalıtımı — ✅ KAPANDI (2026-09-04)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. **K-656 sınıfı DEĞİLMİŞ** —
ilk teşhis yanlış daralmıştı (bkz. aşağıdaki kayıt). Gerçek kök neden:
`ModelContextProtocol.Core`'un istemcisi (`McpClient.CreateAsync`,
`ProtocolVersion` verilmemişse) önce `server/discover` probesini dener; bu
probe `McpClientOptions.DiscoverProbeTimeout` ile sınırlıdır ve **üretim
varsayımı 5 saniyedir**. Süre aşılırsa istemci SESSİZCE eski `initialize`
handshake'ine düşer ve `2025-11-25` negotiate eder — Tasks eklentisi bunu
reddeder, düşen testin mesajı bunu birebir söylüyordu. Tam paket koşumu CPU
baskısı altında bu 5 saniyeyi ara sıra aşıyordu. SDK'nın kendi XML dokümanı
bunu zaten belgeliyor: varsayılan "gerçek ağ eşleri" için kasıtlı kısa,
"yüksek gecikmeli ortamlar için artırın" diyor — in-memory `TestServer` +
onlarca paralel host tam olarak o ortam.

**Düzeltme:** `Infrastructure/McpTaskTestClient.cs`'e
`DiscoverProbeTimeout = TimeSpan.FromSeconds(30)` eklendi (varsayılan
`InitializationTimeout` 60 sn'nin altında kalır — SDK'nın kendi bağlanma
bütçesi böyle bozulmaz).

**Kapı:** `McpTasksEndpointTests.Discover_probe_negotiates_2026_07_28_even_when_the_first_response_is_slow` —
`configureApp`'ten geçirilen bir middleware ilk `/agentprism/mcp` isteğini
6 saniye geciktirir (SDK'nın 5 sn varsayılanının üstü, düzeltmenin 30 sn'sinin
altı). Düzeltmeden ÖNCE kırmızıydı (`2025-11-25` negotiate edildi, ölçüldü),
sonra yeşil. Gerçek CI çekişmesini beklemeden mekanizmayı deterministik
kanıtlıyor.

**Sınıf taraması:** Bu SDK istemcisini (`McpClient.CreateAsync`) kuran tek yer
`McpTaskTestClient.cs`'ti — başka vaka yok. Üretim tarafında
(`McpOAuthAuthorizationCoordinator.cs:233`) `clientOptions: null` **kasıtlı**:
o kod gerçek ağ eşlerine bağlanıyor, SDK'nın üretim varsayımı orada doğru —
kapsam dışı.

**Kapılar:** `ic-dongu` ✅ (767/767, yeni test dahil) · `tarama` ✅ temiz ·
`kapanis --taban dd0ad27e` çalıştırıldı.

**Tuzak:** [`docs/hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md).

<details>
<summary>Kapanış öncesi teşhis anlatısı (yanlış sınıflandırma, kayıt)</summary>

**Sorun:** `dotnet test AgentPrism.slnx` (tüm çözüm birlikte) koşumunda üç MCP
Tasks testi düşüyor: `McpTasksEndpointTests.Unknown_task_id_is_reported_as_a_protocol_error_not_a_500`,
`McpTaskCrossInstanceTests.Second_instance_reconstructs_a_completed_task_from_the_shared_database`,
`McpTaskCrossInstanceTests.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording`.

**Ölçüm (2026-09-04):** Yalıtım sorunudur, regresyon değildir.

| Koşum | Sonuç |
|---|---|
| Tüm çözüm (`AgentPrism.slnx`) | ❌ 3 düştü / 766 |
| Yalnız `AgentPrism.AspNetCore.FunctionalTests` derlemesi | ✅ 766/766 |
| Yalnız `*McpTask*` filtresi (HEAD) | ✅ 12/12 |
| Yalnız `*McpTask*` filtresi (temel `f2147a27`, Faz 139 öncesi) | ✅ 12/12 |

Faz 139–143 **hiçbir MCP koduna dokunmadı** (`git diff --name-only f2147a27..HEAD`
yalnız `docs-site/public/screenshots/mcp.png` veriyor).

Düşüşlerden birinin mesajı nedeni işaret ediyor: *"'GetTaskAsync' requires a
newer protocol revision that supports tasks (the '2026-07-28' revision or
later). The negotiated protocol version is '2025-11-25'."* Yani paralel koşumda
istemci **yanlış protokol sürümüyle** anlaşıyor; ayrı koşumda doğru sürümü
alıyor. **Bu ilk izlenim yanlış çıktı:** mesaj `MeterListener` vakasının
(K-656) sınıfına — process-wide bir durumun başka bir testin kurduğu duruma
bağlanması — benziyordu, ama SDK'yı decompile edip gerçek mekanizmayı
(`DiscoverProbeTimeout` + timeout-tetiklemeli fallback) bulunca sınıfın
FARKLI olduğu ortaya çıktı: paylaşılan durum değil, üretim için ayarlanmış kısa
bir zaman aşımı.

**Değer:** Kapanış kapısı tam çözüm koşumunda kırmızı çıkabiliyordu; bu,
gerçek bir regresyonu gizleyebilirdi.

**Risk:** Düşük — yalnız test altyapısı.

</details>

## Bekleyen Kalemler

İkisi de **bugün faz değildir**. Gövdeleri, koşulları oluştuğunda plana
dönüşebilmeleri için burada duruyor.

| Kalem | Neden faz değil | Koşulu ne zaman oluşur |
|---|---|---|
| **F-95** | İmzası doğrulanmadı; ayrıca **experimental** bir MAF sözleşmesine 1.0 öncesi public yüzey bağlamak K-008'in ön sürüm sınırının tersidir | `maf-api-kesfi` imzayı doğrular **ve** F-141 ile karşılaştırma yapılır. Tercihen 1.0 sonrası |
| **F-165** | 1.650 case tek faza sığmaz; bağımsız faz olarak planlanırsa kuyruğu bitmez | Bağımsız faz olarak **hiç** planlanmaz. Her fazın dokunduğu alanın manuel ailesi o fazda otomatikleştirilir |
| **F-178** | Job/kuyruk metrikleri yarısı [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md)'e gitti. Kalan yarı (model deneme telemetrisi) tüketicinin kendi ölçütüne göre bekler | Gerçek bir üretim fallback gecikmesi olayı ölçülür |
| **F-179** | Ön koşulu yok: `run` satırı sağlayıcıyı saklamıyor, kayan latency penceresi ölçülmüyor | [Faz 132](arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md) kapanır **ve** F-178 attempt süresini ölçmeye başlar **ve** gerçek üretim trafiği oluşur |



### F-95 · Agent düzeyinde kesinti/devam kancası (yeniden açıldı)

**Sorun:** Kesintiye uğramış bir agent turunu devam ettirmek için AgentPrism'in
agent yürütmesinin **içine** girebilmesi gerekir. Bu kalem şu ölçümle kapsam
dışına alınmıştı: *"MAF agent düzeyinde kanca vermiyor; kancayı AgentPrism
yazmak K3'ü zorlar. Kanca yalnız `Microsoft.Agents.AI.Workflows` içinde var."*

**Kapsam:** ~~Önce MAF'ın yeni kanca sözleşmesini ölç~~ — **ölçüldü
(2026-09-05), karşılamıyor** (bkz. `Hazırlık`). Kalan kapsam F-141'in
kapsamıdır: kesinti/devam'ı MAF'a kanca takmadan çözmek. AgentPrism paralel bir
kanca hiyerarşisi kurmaz (K3); bu kural MAF kancası alınmadığı için de geçerli
kalır — F-141 agent yürütmesinin **içine** girmeyen bir tasarım seçmelidir.

**Değer:** Kesintiye uğramış tur, MAF'ı sarmalamadan devam ettirilebilir.

**Mercek:** 2, 5, 6.

**Hazırlık:** 🚨 **İmza doğrulandı (2026-09-05) — MAF yolu KAPALI.**
`maf-api-kesfi` koşuldu; 1.18.0 → 1.20.0 tam yüzey dump'ı diff'lendi.
Kanca çekirdek paketlerde **yoktur**: ayrı ve **alpha** bir pakettedir —
`Microsoft.Agents.AI.AgentHooks` **1.20.0-alpha.260831.1**. Ölçülen public
yüzey **iki tiptir**: `AgentHooksChatClientExtensions.AsAIAgentWithAgentHooks(...)`
ve `AgentHooksOptions`. Üç ölçüm bu kalemi kapatır:

1. **Sözleşme yanlış ihtiyacı karşılıyor.** Kanca bir *enforcement/interception*
   sözleşmesidir (AGENT-HOOKS-0.1): allow/deny/transform verdict, approval seam,
   `InterceptionRecord`. Bu kalemin ihtiyacı olan **kesinti/devam** (turu
   checkpoint'leyip sonra sürdürme) yüzeyde **yoktur**.
2. **Bölünemez ve pipeline'ımızla uyumsuz.** Seam decorator'ları `internal`;
   PR gövdesi "partial installs are impossible by construction" diyor. Dahası
   `AsAIAgentWithAgentHooks`, içinde `FunctionInvokingChatClient` bulunan bir
   client'ı **reddeder** ("tools would execute below the verdicts"). AgentPrism'in
   her provider pipeline'ı `UseFunctionInvocation()` kurar
   ([`AnthropicChatClientFactory.cs:14`](../src/AgentPrism.Anthropic/AnthropicChatClientFactory.cs#L14) ·
   [`AzureOpenAIChatClientFactory.cs:16`](../src/AgentPrism.Azure/AzureOpenAIChatClientFactory.cs#L16) ·
   [`GoogleChatClientFactory.cs:15`](../src/AgentPrism.Google/GoogleChatClientFactory.cs#L15)).
   Kancayı almak, agent kurulum yolunun tamamını MAF'a devretmek demektir.
3. **Bağımlılık grafiği kabul edilemez.** `ResponsibleAI.AgentHooks`
   0.1.0-alpha.4 bir **native FFI** taşır (`libagent_hooks_ffi`, 1.7 MB) ve
   yalnız dört RID kapsar: linux-x64 · osx-arm64 · osx-x64 · win-x64 —
   **linux-arm64 yoktur**. Yanında `Microsoft.ML.Tokenizers`,
   `Microsoft.Extensions.AI.Evaluation`, `VectorData.Abstractions`,
   `Compliance.Abstractions`, `FileSystemGlobbing` gelir. `AgentPrism.Core`
   bugün AOT-uyumludur; bu graf hem onu hem "tüketicinin bağımlılık grafiğini
   kirletme" kuralını bozar.

**Maliyet:** Ölçüldü ve **karşılanamaz** — yukarıdaki 2. ve 3. maddeler.

**Risk:** Yüzeyin tamamı `[Experimental("MAAI001")]`'dir ve paket alpha
kanalındadır. K-008 ön sürüm paketlerini yalnız `AgentPrism.AspNetCore` içinde
tutar; bu kanca ise `AgentPrism.Core`'un agent kurulum yoluna girer.

**Bağımlılık:** ~~MAF 1.19.0'a yükseltme~~ — **düştü**. Yükseltme bu kalemi
**açmıyor** (1.18.0 → 1.20.0 diff'inde sıfır kaldırma, kanca ayrı pakette).
F-141 (MAF-kancasız alternatif tasarım) artık bu kalemin rakibi değil, **tek
yoludur**.

**Ekosistem:** 2026-09-05 — [`dotnet-1.19.0` sürüm notları](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.19.0) ·
[PR #7564](https://github.com/microsoft/agent-framework/pull/7564).

**Karşı görüş:** ~~F-141 rakiptir~~ — ölçümden sonra karşı görüş kalmadı. F-141
aynı ihtiyacı MAF'a hiç kanca takmadan karşılıyor ve 2026-08-21'de bu tasarımın
**doğru** olduğu kaydedilmişti. MAF yolu ölçümle kapandığına göre bu kalem
yalnız F-141 üzerinden ilerler.




### F-165 · Manuel kabul setinin CI'a kademeli taşınması

**Sorun:** Manuel set 37 Markdown dosyasında 1.650 case taşır ve CI'da koşmaz.
Regresyon güvencesi bir kişinin koşum zamanına bağlıdır.

**Kapsam:** Tek fazda tüm seti taşımak değil, bir aileyi test seviyeleri tablosuna
göre otomatikleştiren tekrar edilebilir devir şablonu kurmak. Manuel kalması
gereken model-yanıtı ve insan-yargısı case'leri açıkça ayrılır.

**Değer:** En yüksek riskli kabul davranışları insan zamanı beklemeden regresyon
kapısına girer; iki ayrı spec/test kaynağı oluşmaz.

**Mercek:** 2, 3, 4, 6.

**Hazırlık:** Ölçüldü — `find docs/manuel-test -maxdepth 1 -name '*.md'` 37 dosya,
case kimliği taraması 1.650 case verdi. `AgentPrism.Testing` ve Testcontainers
altyapısı zaten vardır.

**Maliyet:** Yüksek, fakat ilk dilim kontrollüdür.

**Risk:** Case'leri kör biçimde birim teste çevirmek test tiyatrosu üretir.
Sınır davranışı functional/integration seviyesinde kalmalıdır.

**Bağımlılık:** Yok; F-67 ile paralel gider.

**Ekosistem:** 2026-08-26 — depo kalite disiplini; dış ekosistem iddiası yok.

**Karşı görüş:** Model kalitesi ve görsel değerlendirme otomasyona uygun değildir.
Bu aday o case'leri silmeyi değil, otomatikleştirilebilir kısmı ayırmayı önerir.



### F-170 · Store audit kapsamının tamamlanması

**Sorun:** `IAgentSkillStore`'un hiçbir `Auditing*` decorator'ı yok — skill
kaydı ve silme denetim izine hiç yazmıyor. Kardeşleri (`IAgentDefinitionStore`,
`ISkillScriptGrantStore`) yazıyor. Faz 121'in bağımsız denetimi buldu ve 🟢
olarak devretti.

**Kapsam:** Hangi store'ların audit decorator'ı olduğunu ve olması gerektiğini
ölçmek, boşlukları kapatmak. Tek vaka değil sınıf: `Auditing*` deseninin
kapsadığı ve kapsamadığı store'lar bir tabloya çıkarılır.

**Değer:** Denetim izi eksiksiz olmayan bir kayıt, compliance için denetim izi
olmamasıyla aynı yerdedir — kısmi kapsam yanlış güven verir.

**Mercek:** 3, 6.

**Hazırlık:** Faz 121 denetim bulgusu #2. Ölçülmedi — hangi store'ların
decorator taşıdığı sayılmalı.

**Maliyet:** Orta; runtime davranış eklentisi, doküman işi değil.

**Risk:** Audit yazımının kendisi bir hata yolu üretir — `AuditRecorder`'ın
mevcut "audit yazamazsa akış durmaz" sözleşmesi korunmalıdır.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-28 — iç kalite kaydı; dış ekosistem iddiası yok.

**Karşı görüş:** Kapsam bilinçli olabilir — skill kaydı düşük riskli sayılmış
olabilir. Aday, önce **ölçmeyi** öneriyor; boşluk kasıtlıysa gerekçesi yazılır.

### F-171 · Sevk edilen metindeki ölçülmüş sayılar için kapı

**Sorun:** Sevk edilen metin, koddan **elle kopyalanmış** ölçüm sayıları taşıyor
ve hiçbir kapı onları doğrulamıyor. `preview.1` öncesi drift taraması (2026-08-28)
**altı** bayat sayı buldu ve üçü birbiriyle çelişiyordu:

| İddia | Sevk edilen değer(ler) | Ölçülen gerçek |
|---|---|---|
| Konsol ekranı | 27 (×2), 28 (×3) | **30** (`app.tsx` `*Screen` importları) |
| Konsol rotası | 33, 36 | **36** (`app.tsx` `pattern:` sayısı) |
| JS bundle gzip | 180.2 KB, 165.8 KB, 169.4 KB | **175.9 KB** (`npm run build`) |
| İstemci operasyonu | 161 (×2) | **162** (`agentprism.json`) |
| Store contract'ı | "32 others" / "29 more" | **31** (32 dosya − ortak taban) |
| Kök README paket tablosu | 19 satır | **20** paket |

Hepsi K-483'ün sınıfı: elle tekrarlanan bir ölçüm, terim değişince sessizce
yanlışa döner. Beş yüzey (kök README, üç paket README'si, iki site sayfası)
birbirinden habersiz kopya taşıyordu.

**Kapsam:** Bu sayıları koddan türeten bir kapı. En küçük hâli
`check-content.mjs`'e bir kontrol eklemektir: sevk edilen metindeki işaretli
sayıları (`app.tsx`, `agentprism.json`, `postbuild.mjs` çıktısı, packable
`.csproj` kümesi, `Contracts/` envanteri) yeniden hesaplayıp karşılaştırır.
Alternatif: sayıyı metinden **çıkarmak** — kapı yazmak yerine iddiayı
kaldırmak da geçerli bir çözümdür ve ölçülmelidir.

**Değer:** Bu tarama elle koştuğu için bulundu. Bir sonraki fazın eklediği ekran
veya operasyon aynı altı yüzeyi sessizce bayatlatır; NuGet'e basılan README
geri alınamaz.

**Ek kapsam — bağımlılık sürümü damgalı DAVRANIŞ iddiaları (ölçüldü 2026-09-05).**
Yukarıdaki tablo *sayılarla* ilgilidir. Aynı sınıfın ikinci yarısı sevk edilen
XML dokümanlarındadır: bir bağımlılık sürümünü **adıyla anıp** o sürümde ölçülmüş
bir davranış iddia eden yorumlar. Ölçüm: `src/` genelinde **beş** tane —
[`IVectorSearchStore.cs:16`](../src/AgentPrism.Abstractions/Knowledge/IVectorSearchStore.cs#L16) (MEAI 10.8.0) ·
[`McpTransportFactory.cs:32`](../src/AgentPrism.Mcp/Internal/McpTransportFactory.cs#L32) (MCP 2.2.0) ·
[`AgentPrismSqlServerOptions.cs:32`](../src/AgentPrism.SqlServer/AgentPrismSqlServerOptions.cs#L32) (SqlClient 7.0.2) ·
[`OpenAIResponsesEndpoints.cs:25`](../src/AgentPrism.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs#L25) (Hosting.OpenAI) ·
[`FallbackChatClient.cs:612`](../src/AgentPrism.Core/Models/FallbackChatClient.cs#L612) (OpenAI 2.12.0).

Bunlar F-171'in var olan mekanizmasıyla **kapatılamaz**: bir davranış iddiası
koddan yeniden hesaplanamaz, yalnız yeniden **ölçülebilir**. Kapının yapabileceği
şey farklıdır ve daha ucuzdur: damgadaki sürümü `Directory.Packages.props`'taki
pinle karşılaştırıp **saptığında kırmak**. O zaman yükseltme yapan oturum
iddiayı ya yeniden ölçer ya damgayı bilerek günceller; bugün olduğu gibi
**şansa** kalmaz. 🚨 Bu ihtiyaç bugün gerçek bir kaçışla kanıtlandı: MAF
1.18.0 → 1.20.0 yükseltmesinde bu beş iddianın hiçbiri hiçbir kapı tarafından
işaretlenmedi; ikisi elle grep'lendiği için yeniden ölçüldü (`ToAgentRunRequest`
ve `AddA2AServer` — ikisi de geçerli çıktı) ve üçü yükseltmenin kapsamı dışında
kaldığı için hiç bakılmadı.

**Mercek:** 6.

**Hazırlık:** Yukarıdaki tablo ölçüldü ve düzeltmeler uygulandı (drift taraması,
2026-08-28). Kapı yazılmadı — bu aday odur.

**Maliyet:** Düşük-orta; tek bir kontrol dosyası, mevcut `check-content.mjs`
deseninde. Ek kapsam (sürüm damgası) daha ucuzdur — beş iddia, tek regex ve
`Directory.Packages.props` karşılaştırması; yeniden hesaplama gerektirmez.

**Risk:** Fazla katı bir kapı, meşru yuvarlanmış ifadeyi ("about 30 screens")
kızartabilir. Kontrol yalnız **işaretli** sayıya bakmalı, her rakama değil.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-28 — iç kalite kaydı; dış ekosistem iddiası yok.

**Karşı görüş:** Altı sayının hepsi düzeltildi ve bazıları (bundle boyutu) her
build'de değişir — belki doğru cevap sayıyı sevk edilen metinden tamamen
çıkarmaktır. Aday bu ikilemi kapsamına dahil ediyor.

### F-180 · Tam paket koşumunda E2E zaman aşımı — ⚠️ VAKA KAPANDI, SINIF AÇIK (2026-09-04)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. **Yalıtım kusuru değildi —
sevk edilen bir ürün kusuruydu** (K-660).

> 🚨 **2026-09-04 · YENİDEN DÜŞTÜ.** Süreç denetiminin taban koşumunda aynı
> test aynı imzayla düştü (`Timeout 30000ms exceeded ... voice-transcript`);
> izole koşum **1/1, 2,5 sn**. K-660'ın düzeltmesi HEAD'dedir ve kapattığı iki
> ürün yolu gerçektir — ama kaydı AÇAN repro (yük altındaki tam paket koşumu)
> kapanışta tekrar koşulmadı. Bu, `test-yalitimi.md`'nin YEDİNCİ vakasıdır.
> Ölçüm ve ders: [`kesif/2026-09-04-surec-denetimi.md`](kesif/2026-09-04-surec-denetimi.md).
>
> ✅ **DÜZELTME SONRASI İLK YÜK ALTI KOŞUM YEŞİL (2026-09-05).** Kaydın eksik
> dediği kanıt budur. MAF 1.20.0 yükseltmesinin kapanış kapısında tam paket
> koşuldu (`dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1`,
> 21 proje, 6161 test, 0 düşen) ve
> `Playground_voice_mode_opens_microphone_and_shows_transcript` **geçti —
> TRX süresi 00:00:01.49**, E2E 58/58. Karşılaştırma: kaydın kendi yük altı
> geçme tabanı **2,58 sn**, düşme imzası ise 30 sn'lik `voice-transcript`
> beklemesiydi. Süre tabanın da altına indi; bu, K-660'ın `idle` çerçevesinin
> gerçekten geldiğiyle tutarlıdır.
>
> 🚨 **Sınıf yine de KAPANMADI.** Kayıt kusuru "gerçek ama seyrek" diye
> niteliyor ve düzeltme ÖNCESİNDE de üç yük altı koşum yeşil gelmişti. Bir
> koşum bir düzeltmeyi doğrular, seyrek bir sınıfı kapatmaz. Fark şudur:
> önceki üç yeşil koşum K-660'tan ÖNCEYDİ, bu ilk sonrasıdır. **Kapanış
> ölçütü:** düzeltme sonrası ard arda üç yük altı tam paket koşumunun üçünde de
> yeşil. Sayaç bugün **1/3**'tür.

Aşağıdaki teşhis anlatısı doğru daralmıştı ("kayıp olay commit sonrasındadır");
eksik olan tek şey sunucunun o yolda hiçbir çerçeve göndermediğiydi.

**Kök neden:** `VoiceConversationDriver.Commit`, ses gelmeden ulaşan bir
`commit`'te `FinishTurn(counted:false)` çağırıp **hiçbir çerçeve göndermeden**
dönüyordu. İstemci ise gönder'e basıldığı anda kendini `'thinking'`e alıp
kaydediciyi durdurur — panel kalıcı asılır, mikrofon bir daha açılmaz. Kaydedicinin
ilk 250 ms'lik dilimi içinde commit etmek bu yola girer; tam paket yükü o pencereyi
genişletiyordu, sebebi o değildi.

**Sınıf taraması ikinci vakayı buldu** (üretimde daha olası): transcriber boş metin
döndüğünde `ProcessTurnAsync` aynı sessiz dönüşü yapıyordu ve istemci boş
`transcript` çerçevesini zaten yok sayıyor. İkisi de yeni `idle` sunucu
çerçevesiyle kapatıldı, ikisi de red→green kanıtlandı
(`VoiceConversationTests`). Mevcut `Commit_without_audio_does_NOT_produce_a_turn`
testi bunu göremiyordu: yalnız **sunucunun** dinlemeye döndüğünü ölçüyordu.

Tuzak `docs/hafiza/ses-ve-konusma.md`'de; sözleşme `docs-site/guides/voice.md`'de.

<details>
<summary>Kapanış öncesi teşhis anlatısı (kayıt)</summary>

**Sorun:** `AgentPrism.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript`
**yalnız** tam çözüm koşumunda (`dotnet test AgentPrism.slnx -c Release
--no-build -maxcpucount:1`) düşüyor: Playwright'ın `voice-transcript`
test id'sini beklerken 30 sn'lik varsayılan zaman aşımına takılıyor.

**Ölçüldü (2026-09-02, Faz 133 kapanışı):** izole koşumda **3/3 yeşil**; tüm
E2E paketi tek başına **57/57 yeşil**. Yalnız makineyi onlarca paketle
paylaşırken düşüyor. Faz 133 arayüze, ses yoluna veya playground'a
**dokunmadı** — kusur o fazın ürünü değil.

**Sınıf:** `kusur-giderme` Adım 2'nin *"tek başına geçip pakette düşen test bir
yalıtım veya kilit çakışmasıdır"* kalemi. Sessiz bırakılmaz; testi uzatmak
(zaman aşımını büyütmek) semptomu susturur, sebebi değil.

**Ek ölçüm (2026-09-02, F-181 turunda):** Ağustos'tan kalan bir tam-paket
koşum kaydı (`tests/AgentPrism.Ui.E2ETests/artifacts/repro/full-suite-before/`,
izlenmiyor) bu testi **geçerken** yakalamış. TRX'teki gerçek süre
**00:00:02.58**. Koşum logundaki `(36s)` testin süresi değil, koşumun o andaki
geçen süresidir — karıştırılmamalı.

Bu, teşhisi daraltıyor: yük altında geçtiğinde test 2,6 saniye sürüyor, ama
düştüğünde 30 saniyelik `voice-transcript` beklemesine takılıyor. Aradaki fark
~12 kat. Yani sorun **yavaş ama ilerleyen** bir yol değil; bir olay **hiç
gelmiyor**. Bu, "zaman aşımını büyüt" refleksini kanıtla eler.

**Sonraki adım:** kayıp olayı ara, gecikmeyi değil — WebSocket el sıkışmasının
tamamlanıp `ready`'nin gelmemesi, ya da sahte cihazın ürettiği tondan VAD'ın
commit edilebilir bir tampon üretememesi. `UiTests.cs:259` (`voice-meter`,
20 sn) ve `:266` (`voice-commit`, 20 sn) beklemeleri düşmüyor; düşen yalnız
`:270`. Bu, el sıkışmasının tamamlandığını ve sorunun **commit sonrası
sunucu turunda** olduğunu söylüyor — bir sonraki repro turu oraya bakmalı.

**🚨 TEKRARLADI (2026-09-03, K-658 kapanış kapısı) — teşhis doğrulandı.**
`Timeout 30000ms exceeded ... waiting for GetByTestId("voice-transcript") to be
visible`, `UiTests.cs:270`. E2E 56/57. Kritik olan **hangi adımın düşmediği**:
`:259` (`voice-meter`, 20 sn) ve `:266` (`voice-commit`, 20 sn) geçti, yani
WebSocket el sıkışması tamamlandı, `ready` geldi, VAD commit edilebilir bir
tampon üretti ve düğme tıklandı. Düşen yalnız commit **sonrası** sunucu turu.
Bu, 2026-09-02'de bu kayda yazılan hipotezi doğruluyor: kayıp olay commit
sonrasındadır, el sıkışmasında değil. Kanıt:
`tests/AgentPrism.Ui.E2ETests/artifacts/repro/2026-09-03-kapanis/` (izlenmiyor).

**Sonraki adım daraldı:** commit'ten sonra sunucunun transcript üretimine kadar
olan yolu ölç — `voice-commit` tıklaması sunucuya ulaştı mı, ulaştıysa yanıt
neden gelmedi. Zaman aşımını büyütmek hâlâ yasak.

**Önceki üç koşumda tekrarlanmamıştı:** Faz 134 · Faz 135 · ve
2026-09-02'nin F-181 kapanış kapısı (`dotnet test AgentPrism.slnx -c Release
--no-build -maxcpucount:1`, E2E **57/57** yeşil, 506 sn). Kusur gerçek ama
seyrek; bir sonraki düşüşte **koşumun kendi TRX'i saklanmalıdır** — asıl eksik
kanıt, düşen koşumdaki adım sürelerinin dağılımıdır.

</details>

### F-181 · `MeterListener` yalıtımı — ✅ KAPANDI (2026-09-02)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Sınıf taraması **dört** vaka
buldu (kayıtta bir tane vardı):

| Dosya | Durum |
|---|---|
| `AspNetCore.FunctionalTests/JobMetricEndToEndTests.cs` | Faz 134'te etiket süzgeciyle düzeltilmişti; **instance bağlamasına** yükseltildi |
| `AspNetCore.FunctionalTests/RunCostMetricEndToEndTests.cs` | F-181'in kendisi; düzeltildi |
| `Core.UnitTests/Quotas/QuotaUsageObserverTests.cs` | Kayıtta yoktu; düzeltildi |
| `Core.UnitTests/Diagnostics/JobQueueDepthGaugeTests.cs` | Kayıtta yoktu (`TestMeterFactory`'si vardı ama süzgeci isme bakıyordu); düzeltildi |

**Çözüm:** Etiketle süzmek yerine `Meter` **instance**'ına bağlanma. Etiket
süzgeci, hangi testlerin çakışabileceğine dair düzyazı bir akıl yürütmeye
dayanıyordu; yeni bir test onu sessizce geçersiz kılabilirdi. Dinlenen her tip
`IMeterFactory` alıyor ve `AgentPrismTestHost.StartAsync` servis kaydına izin
veriyor — izolasyon dört vakanın dördünde de mümkündü.

**Kapı:** `MeterListenerIsolationTests` (sıfır tolerans, taban çizgisi yok).
Düzeltmeden **önce** kırmızıydı (dört vaka), sonra yeşil. Tarayıcı yorumları
ayıklar: tuzağı XML dokümanında ANLATAN `ObservabilityTests` cezalandırılmaz.

**Karar:** K-656. **Tuzak:** `docs/hafiza/test-altyapisi.md`.

## Aday Olmayan Açık Kayıtlar

Bu kalemler faz sıralamasına girmez. Tam kanıt, geçmiş ve sonraki adım keşif
kaydındadır.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Plana dönüştü** | F-109 → [Faz 112](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) · F-149 → [Faz 113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) · F-166 → [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md) · F-168 → [Faz 115](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) · F-67 → [Faz 116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md) · F-167 → [Faz 117](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) · F-152 → [Faz 118](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md) | Bölümleri bu dosyadan silindi; kanıt ve tasarım faz dokümanındadır. Aday listesine geri dönmezler. |
| **Kapatılan kusur kayıtları** | F-106, F-130, F-137, F-138, F-139 | Kapanış kanıtı keşif kaydındadır; yeniden görülürse yeni kusur kaydı açılır. |
| **Karar / uyumluluk** | F-72, F-90, F-91, F-92, F-132, **F-169** | Mevcut karar veya dış bağımlılık değişmeden planlanmaz. F-95 2026-08-26'da adaylığa döndü. **F-169** (MAF CodeAct / Hyperlight sandbox) F-72 ile **aynı eşiktedir**: paket GA ve taşınabilir olana kadar planlanmaz — ölçüm [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md) § 9. |
| **Ölçüm bekliyor** | F-51, F-94, F-96, F-97, F-99, F-101, F-123, F-128, F-154, F-156, F-157, F-159, F-160, F-161, F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. |
| **Arşivlendi / birleştirildi** | F-48, F-88, F-89, F-98, F-144, F-145, F-146, F-147, F-148, F-155, F-158, F-163 | Plan değeri yok, rutin bakım olarak kalır veya aktif adayla aynı tasarım işidir. **F-155** F-149 ile aynı tasarım işiydi; o iş artık [Faz 113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md)'tedir. **F-163** F-67'nin ölçümüne bağlıydı; o ölçüm artık [Faz 116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md)'dadır ve ayrı test ailesi olarak açılmaz. |

## Bilerek Önerilmeyenler

Reddedilmiş mimari işler için tek kaynak
[`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)'dir.
Özellikle F-91 `secret` saklama sınırını ve F-92 dağıtık hız sınırı/Redis
kararını değiştirmeden yeniden aday olmaz. **F-95 bu listede değildir** — onu
bekleten şey bir tasarım kararı değil, MAF'ta kancanın bulunmamasıydı; MAF
1.19.0 o kancayı gönderdiği için 2026-08-26'da adaylığa döndü.


### F-178 · Model deneme (attempt) telemetrisi

> **Yarısı plana dönüştü.** Job/kuyruk metrikleri
> [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md)'e gitti. Aşağıdaki gövde yalnız
> **kalan yarıyı** anlatır.

**Sorun:** Yedek zincirinde hangi linkte ne kadar süre harcandığı ölçülmüyor.
`FallbackChatClient` döngü indeksini tutuyor ve `ModelFallbackUsed`'ı yazıyor,
ama `grep -n "Stopwatch\|GetTimestamp\|Elapsed"` o dosyada **sıfır** eşleşme
veriyor ve `ModelFallbackUsedEventPayload` süre veya indeks taşımıyor. Birincil
model 28 sn'de timeout olup yedek 2 sn'de yanıtladığında, 30 sn'lik `run`'ın
gecikmesinin hangi linkten geldiği ayrıştırılamaz.

**Kapsam:** Deneme başına süre (monotonik saat), deneme indeksi, sağlayıcı,
model ve sonuç kategorisi taşıyan bir `run` olayı veya `span`. Prompt ve yanıt
içeriği telemetriye **girmez**; sağlayıcıya özel request ID **çıkarılmaz**
(tüketici bu maliyeti kabul etti). Etiket kardinalitesi sınırlanır.

**Değer:** "Birincil timeout değeri düşürülmeli mi?", "Yedek ilk model olmalı
mı?" soruları kanıta dayanır.

**Mercek:** 2, 7.

**Hazırlık:** `FallbackChatClient` döngü indeksini zaten tutuyor; ekleme
tamamen additive'dir. Faz 133 metrik adı ve etiket kurallarını kurar.

**Maliyet:** Ölçülmedi. Yeni tablo ve migration gerekmez.

**Risk:** Etiket kardinalitesi kontrolsüz büyürse metrik altyapısını boğar.

**Karşı görüş:** Tüketici bunu bilerek erteledi — *"Prodigy henüz production
olmadığı için gerçek incident kaydı sunamıyoruz … İlk fallback latency olayı
ölçüldüğünde bu talebi incident verisiyle yeniden açacağız."* Tek istediği,
API tasarımında bunu engelleyecek bir karar alınmamasıdır; bu koşul bugün
sağlanıyor.



### F-179 · Çalışma anı model yönlendirme policy'si

**Sorun:** Model seçimi bugün statik binding ve hata sonrası yedek zinciriyle
sınırlı. Çağrı **öncesi** maliyet, gecikme ve capability'ye göre seçim yapılamaz;
yapılsa bile seçimin nedeni `run` kanıtına girmez.

**Kapsam:** Aday binding kümesinden seçim yapan opt-in bir policy seam'i ve
seçim kararının `run` kanıtına yazılması (seçilen binding, kararlı reason code,
değerlendirilen adaylar, policy adı).

**Değer:** Yönlendirme kararı ile `run` kanıtı aynı yerde durur.

**Mercek:** 2, 7, 8.

**Hazırlık:** 🚨 **Ön koşulları eksik.** `RunRecord` sağlayıcı saklamıyor
(Faz 132 kapatır); kayan latency penceresi yok (F-178 kapatır);
`ModelProviderHealthCache` yalnız sağlık durumu verir.

**Maliyet:** Ölçülmedi.

**Risk:** Ölçüm olmadan "en ucuzu seç" kararı yanlış olur. Tüketici de
"önce doğru telemetry, sonra dinamik policy" diyor.

