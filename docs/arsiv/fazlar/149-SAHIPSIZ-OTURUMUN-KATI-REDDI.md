# Faz 149 — Sahipsiz Oturumun Katı Reddi

> **Durum:** ✅ Tamamlandı (2026-09-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-201** (tüketici turu 4 yanıtı, §8 soru 1)
> **Önkoşul:** [Faz 148](148-OTURUM-SAHIPLIGININ-KALICILIGI.md) — sahiplik sütunu, `SessionOwnershipGate` ve seçenek sınıfı oradan gelir
> **Paketler:** `Tracon.Abstractions`, `Tracon.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok — `owner_id` sütunu Faz 148'de açıldı
> **Public API:** Büyüyor — bir seçenek alanı + bir uç haritalama bayrağı. `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya (yalnız başlık), **shipped giriş sıfır**: bugün eklemek bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `concepts/sessions.md`, `concepts/governance.md`, `guides/openai-api.md`, `guides/embedding.md`, `capabilities.md` · sevk edilen: `TraconSessionOwnershipOptions` XML `<example>`, `TraconEndpointOptions` XML, `src/Tracon.Abstractions/README.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 430cd0df:docs/arsiv/fazlar/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md
> ```
>
> Damıtıldı 2026-09-06 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 148 sahipliği sevk etti ve bilinçli bir taviz verdi (K-693): **sahipsiz eski satır sahipli listede görünmez, ama tekil erişimde reddedilmez.** Gerekçe, bayrağın açıldığı anda canlı olan konuşmaların kopmamasıydı. Tüketici bu tavizi reddetti ve gerekçesini yazdı: > *"Sahipliği belirlenemeyen mevcut bir session, kimliğini bilen son > kullanıcıya açık olmamalı.

## Bitiş Ölçütleri (DoD)

- [x] `RefuseUnownedSessions=false` (varsayılan) iken **hiçbir** davranış değişmez — 11 sahiplik çağrı yerinin hepsi için kanıt — `An_unowned_session_stays_reachable_on_every_surface_while_strict_mode_is_off` + Faz 148'in 36 mevcut case'i
- [x] `Enabled=false` iken `RefuseUnownedSessions=true` **hiçbir şey yapmaz** — `Strict_mode_does_nothing_while_ownership_itself_is_off`
- [x] Katı mod açıkken sahipsiz satır oturum uçlarında `404`, `run` başlatmada `403` (akışlı VE akışsız dalın ikisinde de — **sapma 1**, SSE `error` çerçevesi değil), seste `404` döner
- [x] Ret gövdesi var olmayan kaynakla **birebir aynıdır** — `Strict_mode_answers_an_unowned_session_with_the_same_404_a_missing_one_gets` + örnek uygulamada `diff` boş
- [x] 🚨 **Var olmayan** oturum katı modda da açılır — K-283 korunur — üç test + örnek uygulama koşumu
- [x] Yönetim payı taşıyan istek katı modda da sahipsiz satıra erişir — `A_management_caller_still_reads_an_unowned_session_in_strict_mode`; `run` başlatmada muafiyet **yok** (K-694)
- [x] `/v1/conversations`'ın üç okuma/silme ucu `IRunAuthorizationHandler`'dan geçer; ret `404` — `OpenAIConversationsAuthorizationTests` — kapı olmadan 9 case kırmızı olduğu ölçüldü
- [x] `RunAuthorizationCoverageTests` `OpenAIConversationsEndpoints.cs`'i sayar; tarama sıfır bulguda **kırmızı** olur — `ExpectedResourceFiles`'a eklendi; `A_file_missing_the_call_is_reported_by_name` iki yönü de sınar
- [x] Haritalama kapatıldığında dört uç `404` döner ve OpenAPI'de **görünmez** — `Turning_the_surface_off_removes_all_four_routes` · `..._from_the_OpenAPI_document`
- [x] Haritalama bayrağının varsayılanı bugünkü davranıştır (yüzey haritalanır) — `The_conversations_surface_is_mapped_by_default`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — `--taban dc196ff6`; `dotnet format` bir kez kırmızı oldu (import sırası) ve düzeltildi
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı; katı mod çıktısı belgeye yazıldı — sonuçlar "Örnek Uygulama Koşumu" bölümünde
- [x] `secret` taraması boş döndü — `scripts/kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; on biri de koşuldu — `MT-SEC-175` … `181` (yedi case, on bir iddia); hepsinin otomatik karşılığı var ve koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — kod tarafında hiç doğmadı; tek 🔴 zamanlama kaynaklıydı, beş 🟡'nin dördü düzeltildi
- [x] `docs-site/` güncellendi (`concepts/sessions.md`, `guides/openai-api.md`); `npm run build` + `check-links.mjs` temiz — `npm run check` dördü de temiz: `check:content` · `build` · `check:links` · `check:weight`
- [x] Sürüm notuna `/v1/conversations` davranış değişikliği **açıkça** yazıldı — `CHANGELOG.md` `[Unreleased]` → `### Changed`

### Doğrulama komutları

```bash
# Katı mod: sahipsiz satır, var olmayan satırla AYNI gövde
diff <(curl -s "$APU/api/sessions/$UNOWNED" -H "Authorization: Bearer $TOKEN") \
     <(curl -s "$APU/api/sessions/yok-boyle-bir-id" -H "Authorization: Bearer $TOKEN")

# Yeni oturum yolu korunuyor mu (K-283)
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$APU/api/agents/support/run" \
  -H "Authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d '{"input":"merhaba","sessionId":"hic-olmayan-id"}'
# beklenen: 200

# Haritalama kapalıyken OpenAPI
curl -s "$APU/openapi/v1.json" | jq '.paths | keys | map(select(startswith("/v1/conversations")))'
# beklenen: []
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne oldu | Neden |
|---|---|---|---|
| 1 | **Akışlı yolda ret bir SSE `error` çerçevesidir** (K-324; hata modu tablosu ve manuel case 4) | Ret **gerçek `403`'tür**, akışlı ve akışsız dalın ikisinde de | Plan bayattı. Faz 148'in devir notu bunu zaten yazmıştı: `CheckRunSessionAsync` uç gövdesinde `SseWriter.StartAsync`'ten **önce** koşar, bu yüzden başlıklar henüz gitmemiştir. Ölçüldü — `AgentEndpoints.cs:234` guard'ı `RunAsync`/`RunQueuedAsync` dallanmasından öncedir. K-324 bu fazın kapsamına hiç girmedi; `POST …/run` varsayılan olarak SSE'dir ve `Running_against_another_owners_session_is_refused` zaten `403` bekliyordu. Akışsız dal `Idempotency-Key` ile ayrı test edildi |
| 2 | Kapsam kapısına `OpenAIConversationsEndpoints.cs` **eklenir** | Ownership listesinde **zaten vardı** (Faz 148 onu eklemişti); eklenen yer `ExpectedResourceFiles`, yani `RunAuthorizationGate` listesi | Ölçüm: `ExpectedSessionOwnershipFiles` dosyayı taşıyordu, `ExpectedResourceFiles` taşımıyordu. Aynı dosya iki listeye ait; biri Tracon'in kendi sahiplik sınırı, diğeri tüketicinin handler'ı. Bir yüzey **yarım kapılı** olabilir |
| 3 | Yönetim payı "değişmez" (§149.3) | Yönetim muafiyeti **yeni** bir davranıştır ve tek noktaya eklendi | `DeniesAsync` bugüne kadar `ManagementPolicy`'ye **hiç bakmıyordu** (K-691 bilinçli kararı). "Değişmez" ancak yeni bir muafiyetle sağlanabilirdi. Kullanıcı kararı: muafiyet yalnız okuma/silme kapısında (`DeniesAsync`), `run` başlatmada **yok** → K-694 |
| 4 | `DeniesAsync` imzası değişmez | `HttpContext` parametresi eklendi (**yedi** çağrı yeri güncellendi: `SessionEndpoints` 3 · `OpenAIConversationsEndpoints` 3 · ses 1) | Yönetim politikası istek başına değerlendirilir; `SatisfiesManagementPolicyAsync` `HttpContext.User` ve `RequestServices` ister. Üç endpoint handler'ı da `HttpContext` parametresi kazandı — rota ve OpenAPI'yi etkilemez |
| 5 | Manuel case tablosu 11 kalem | 7 kalem (`MT-SEC-175`–`181`), aynı 11 iddiayı kapsıyor | Plan tablosu her satırı ayrı case sayıyordu; kabul seti biçimi her case'e birden çok adım verir. Sayı düştü, kapsam düşmedi — her case otomatik karşılığını adıyla sayar |

**Kapsamda kalmayan bir gözlem:** `docs-site` `--site-denetle`'nin `http-api`
kuralı hâlâ yanlış negatif üretiyor (Faz 148 devir notu). Bu faz onu
düzeltmedi; aday olarak açık.

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-694 — Katı modun yönetim muafiyeti yalnız OKUMA kapısındadır (`DeniesAsync`), `run` BAŞLATMADA yoktur (kullanıcı kararı)** | `ManagementPolicy` bugüne kadar tekil oturuk erişiminde hiç sorulmuyordu (K-691). Katı mod açıkken sahipsiz satır yönetim listesinde görünmeye devam ederdi ama açılamazdı — destek ekibi göremediği değil, **görüp okuyamadığı** bir satıra bakardı. Muafiyet bu boşluğu kapatır. `run` başlatmaya taşınmaz: bir turu sürdürmek konuşmaya **yazar** ve operatörü sahipsiz bir konuşmanın yazarı yapar. Sahipsiz satırda sızacak bir kullanıcı konuşması yoktur (satır kimseye ait değildir), bu yüzden K-691'in "başkasının SAHİPLİ oturumu operatöre de kapalı" kuralı bozulmaz |
| **K-695 — Sahipsiz satır reddi ile başkasının oturumu reddi AYNI metni taşır (`run` yüzeyinde `403`, oturuk yüzeyinde `404`)** | İki ayrı metin, çağıranın görmeye zaten yetkili olduğu bir yanıttan hangi oturumların sahiplikten **önce** yazıldığını öğrenmesini sağlardı. İstemcinin eylemi iki durumda da aynıdır: bu oturum kullanılamaz. Açık Soru 2 cevabı A ile aynı sınıf; `errorType` de tektir (`session_owner_required`) |
| **K-696 — `/v1/conversations`'ın üç okuma/silme ucu `IRunAuthorizationHandler`'a bağlandı; `POST` bağlanmadı** | Uyumluluk yüzeyi aynı oturumlara başka bir adla erişiyordu: handler'ı `GET /api/sessions/{id}`'yi reddedecek biçimde kurmuş bir tüketicide `GET /v1/conversations/{id}/items` aynı geçmişi veriyordu. `POST` ölçüldü — hiçbir şey yazmaz, yalnız kimlik ayırır (`CreateAsync` `ISessionStore`'a hiç dokunmaz); kapıya bağlamak var olmayan bir kaynak üzerinde ikinci bir karar noktası açardı. ⚠️ Handler kaydetmiş MEVCUT kurulumlarda **davranış değişikliğidir** (fail-closed yönde) |
| **K-697 — `MapOpenAIConversations` yalnız conversations ailesini yönetir; `/v1/responses` ve `/v1/chat/completions` kapsam dışıdır (kullanıcı kararı, Açık Soru 1 cevabı A)** | Fazın gerekçesi conversations'ın kapı boşluğuydu; diğer iki yüzey zaten kapıdan geçiyor. Geniş bir `MapOpenAICompatible` bayrağı ayrı bir talep kanıtı ister ve conversations'ı kapatmak isteyen bir kurulumu `run` yüzeyini de kapatmaya zorlardı. Varsayılan `true` — yüzey sevk edildi, sessizce geri çekilemez |

> K-693 bu fazla **kapandı**: yeniden açılma koşulu ("bir 'sahipsiz satırları da
> reddet' seçeneği talep gelirse") karşılandı ve seçenek sevk edildi. K-693'ün
> kendisi hâlâ **varsayılan** davranışı tarif eder.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, `Tracon__SessionOwnership__Enabled=true`,
`RequireAuthenticatedOwner=false` (sahipsiz satır **üretebilmek** için),
`RefuseUnownedSessions=true`, `Demo__Roles__Enabled=true`. Bellek içi `store`,
`echo` sağlayıcı — gerçek `run`, gerçek HTTP.

| Adım | Ölçülen |
|---|---|
| Kimliksiz çağıran `sessionId=legacy-1` ile `run` | `200` — sahipsiz satır yazıldı (`ownerId: null`) |
| `GET /api/sessions/legacy-1` (alice) | `404` |
| `DELETE /api/sessions/legacy-1` (alice) | `404`, satır **durdu** |
| `GET /v1/conversations/legacy-1` · `/items` | `404` · `404` |
| `404` gövdesi ↔ var olmayan oturumun gövdesi | **birebir aynı** (`traceId` ve id dışında; `diff` boş) |
| `POST /api/agents/support/run` `sessionId=legacy-1` (alice, akışlı/varsayılan) | `403` + `errorType: session_owner_required`, `detail`: *"belongs to another user"* (K-695) |
| 🚨 `sessionId=brand-new` (alice) | `200` — oturum açıldı ve sahibi alice (K-283 korundu) |
| `GET /api/sessions/brand-new` (bob) | `404` |
| `GET /api/sessions/legacy-1` **OPERATOR** rolüyle | `200` (K-694) |
| `GET /api/sessions/legacy-1` **READER** rolüyle | `404` — aynı kayıtlı politika, farklı cevap |
| `POST run` `legacy-1` **OPERATOR** rolüyle | `403` — muafiyet yazmayı kapsamaz (K-694) |
| Yönetim listesi | `[('legacy-1', None)]` — satır her reddin ardından **yerinde** |
| `GET /openapi/v1.json` (varsayılan) | Üç `/v1/conversations` yolu **var** |

Demo rolleri **kapalıyken** aynı `OPERATOR` okuması `404` verdi — `Tracon.Operator`
politikası kayıtlı değilse muafiyet **yoktur** (fail-closed, ayrıca ölçüldü).

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir denetçiyle koşuldu. Denetçi bağımsız olarak
`dotnet build` (0 uyarı), 896 fonksiyonel test ve `docs-site npm run check`
koştu. **Kod tarafında 🔴 seviyesinde kusur bulunmadı.**

Denetçi üç kritik iddiayı ayrı ayrı doğruladı: `record is null` ↔
`record.OwnerId is null` ayrımı korunuyor (K-283 ayakta), yönetim muafiyeti
asimetrik (K-694 uygulanmış) ve `DeniesAsync`'in `HttpContext` parametresi
**yedi** çağrı yerinin yedisinde de doğru — plan ve devir notu "altı" diyordu,
gerçek sayı yedi (`SessionEndpoints` üç, ses bir, conversations üç).

### 🔴

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | DoD'nin "örnek uygulama koşumu" satırı için dokümanda kayıt yok | **Geçersiz — zamanlama.** Denetçi dosyayı "Örnek Uygulama Koşumu" bölümü yazılmadan önce okudu. Bölüm bu dokümanda mevcuttur ve 13 satırlık ölçüm tablosu taşır |

### 🟡

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `A_cancelled_request_does_not_answer_200` **test tiyatrosu**: token istek gönderilmeden önce iptal ediliyor, sunucuya hiç ulaşılmıyor | **Düzeltildi.** Test `BlockingHandler` ile yeniden yazıldı: handler `AuthorizeSessionAsync` içinde isteğin **kendi** token'ını bekler, test handler'a girildiğini gördükten SONRA iptal eder ve `ObservedCancellation`'ı sınar. Artık token'ın tüketici koduna gerçekten ulaştığını kanıtlıyor |
| 2 | 🚨 Bir reddi **başka bir redle** karşılaştırıyordu; ayrıca kaynak yorumu *"a denial still cannot confirm the conversation exists"* diyordu — bu uçta **yanlış**, çünkü kullanılmamış bir kimlik `200` döner | **Düzeltildi, ikisi de.** Test artık gerçek bir **başka kiracı** kaydıyla karşılaştırıyor (`SeedAsync(..., tenantId: "other-tenant")`) ve kiracı reddinin handler'a hiç ulaşmadığını da ölçüyor (K-684). Kaynak yorumu düzeltildi: bayt eşitliğinin ne aldığı **ve ne almadığı** açıkça yazıldı — bu yüzeyde `404` her zaman "seninki değil" demektir, "hiç yok" değil; kimlik bir rezervasyondur |
| 3 | Katı mod 11 çağrı yerinin ikisinde (`WorkflowEndpoints`, `OpenAIResponsesEndpoints`) hiç denenmedi; `/v1/responses` reddi OpenAI biçimine **çevriliyor** | **Düzeltildi.** İki fonksiyonel case eklendi: `Strict_mode_refuses_an_unowned_conversation_on_the_OpenAI_run_surface` (çeviriyi de ölçer, satırın sahiplenilmediğini de) ve `Strict_mode_refuses_an_unowned_session_on_the_workflow_run_surface` |
| 4 | 17 DoD kutusunun tamamı işaretsiz | **Geçersiz — zamanlama.** Denetçi dosyayı kutular işaretlenmeden önce okudu |
| 5 | `guides/embedding.md` değişmemişti; oturum erişimi listesi conversations'ı saymıyordu | **Düzeltildi.** Liste üç conversations ucunu kapsayacak şekilde genişletildi; `POST`'un neden dışarıda olduğu da yazıldı |
| 6 | `dokuman-bakim.py` kırmızı: kapanmış faz kökte, `ADAYLAR.md` arşiv yoluna bağlanıyor | **Beklenen.** `faz-arsivle` ile kapandı; dört kapı arşivlemeden **sonra** yeniden koşuldu |

### 🟢 — aday listesine

| # | Bulgu | Kayıt |
|---|---|---|
| 1 | `ExpectedResourceFiles` dosya seviyesinde eşleşir: conversations'ın üç `CheckSessionAsync` çağrısından ikisi silinse tarama yeşil kalır | **F-204** |
| 2 | `/v1/conversations/{id}` varlık asimetrisi: yok → `200`, reddedildi → `404`; katı modda bir çağıran hangi id'lerin sahipsiz **satır** olduğunu sayabilir | **F-205** |
| 3 | `TraconSessionOwnershipOptions` `<example>` taşımıyor | Kalite sözleşmesinin `<example>` kuralı giriş noktaları (`Add*`/`Use*`/`Map*`) içindir; bu bir property. **Kayda geçmez** |

## Sonraki Faza Devir Notu

Katı mod sevk edildi ve K-693 kapandı. Devreden beş gerçek bilgi:

- 🚨 **`SessionOwnershipGate.DeniesAsync` artık `HttpContext` ister.** Bugün
  **yedi** çağrı yeri var (`SessionEndpoints` 3 · `OpenAIConversationsEndpoints` 3
  · ses 1); `CheckRunSessionAsync`'in ayrıca üç çağrı yeri var, toplam 11.
  Yeni bir oturum yüzeyi eklerken üç şey birden gerekir: kapıyı **kendi gövdesinde**
  çağır, `HttpContext`'i geçir (yönetim politikası istek başına
  değerlendirilir) ve dosyayı `RunAuthorizationCoverageTests`'in **iki**
  listesine birden ekle — `ExpectedSessionOwnershipFiles` (Tracon'in kendi
  sınırı) ve `ExpectedResourceFiles` (tüketicinin handler'ı). Faz 148 birinciyi
  ekledi, ikincisini atladı; bu faz onu bulmak için geri gelmek zorunda kaldı.
  **Bir yüzey yarım kapılı olabilir ve bir kapıyı bulmak diğeri hakkında kanıt
  değildir.**
- 🚨 **"Henüz açılmamış" ile "sahipsiz yazılmış" AYRI dallardır ve öyle
  kalmalıdır.** `DeniesAsync` içinde `record is null` ilk kontrol,
  `record.OwnerId is null` ikincisidir; `CheckRunSessionAsync` içinde de ayrı.
  Bu ikisini "sahipsiz" diye birleştiren herhangi bir sadeleştirme K-283'ü
  düşürür ve her kurulumdaki **ilk** konuşmayı sessizce öldürür. Üç test
  (`Strict_mode_still_opens_...` × 2, `Strict_mode_refuses_...`) bunu üç
  katmandan kilitler.
- **Yönetim muafiyeti tek yerdedir ve simetrik DEĞİLDİR** (K-694). Sahipliği
  "tek yardımcıda toplamak" isteyen bir sonraki faz bu asimetriyi silmeye
  eğilimlidir; `The_management_exemption_does_not_extend_to_starting_a_run`
  onu tutar.
- **`TraconEndpointOptions` yapılandırmadan bağlanmaz.** `MapOpenAIConversations`
  yalnız `MapTracon(prefix, options => ...)` ile ayarlanır. Bu bilinçlidir
  (`AuthToken` aynı tipte yaşıyor, bkz. tipin `record` olmama gerekçesi); bir
  uç ailesini `appsettings` ile kapatılabilir yapmak isteyen faz önce bunu
  ölçmelidir.
- **Sahiplik hâlâ geriye dönük DEĞİLDİR.** Bu faz sahipsiz satırı *reddedebilir*
  hâle getirdi; ona *sahip atayan* bir migration veya uç hâlâ yoktur ve
  K-689'un "bir kez atanır" kuralı korunur. Katı modu açan bir kurulum o
  satırları artık hiç kullanamaz — `docs-site` bunu açıkça yazıyor.
