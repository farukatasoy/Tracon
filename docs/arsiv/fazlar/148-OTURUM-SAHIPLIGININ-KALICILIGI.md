# Faz 148 — Oturum Sahipliğinin Kalıcılığı

> **Durum:** ✅ Tamamlandı (2026-09-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-196** (tüketici turu 4, A1 · sahiplik yarısı)
> **Önkoşul:** [Faz 147](147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md) — kapı bütün kaynak grafiğini kapsamadan sahiplik yarım bir sınır olur; sahipli liste dönerken `run` okuma açık kalırsa sızıntı kapanmaz
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç set** (`sessions`'a sütun + indeks). Numaralar uygulama anında alınır (K-178)
> **Public API:** Büyüyor — `SessionRecord` ve `SessionQuery`'ye birer alan, bir seçenek sınıfı. `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya (yalnız başlık), **shipped giriş sıfır**: bugün eklemek bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `concepts/sessions.md`, `concepts/governance.md`, `guides/embedding.md`, `guides/write-your-own-store.md`, `capabilities.md` · sevk edilen: `ISessionStore` XML `<example>`, `src/AgentPrism.Abstractions/README.md`, `SessionStoreContract`
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
> git show 7321ea32:docs/arsiv/fazlar/148-OTURUM-SAHIPLIGININ-KALICILIGI.md
> ```
>
> Damıtıldı 2026-09-06 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 139 ve 147 kapıyı kurdu: AgentPrism **sorar**, tüketici **karar verir**. Sorunun bir yarısı hâlâ cevapsız — AgentPrism, bir oturumun kime ait olduğunu hiçbir yerde saklamıyor. Bu iki somut sonuç doğuruyor: 1. Tüketici sahipliği kendi tablosunda tutmak zorunda ve **listeyi filtreleyemiyor**.

## Bitiş Ölçütleri (DoD)

- [x] Mod kapalı (varsayılan) kurulumda **hiçbir** davranış değişmez; `owner_id` `NULL` kalır
- [x] Mod açıkken `GET /api/sessions` yalnız çağıranın oturumlarını döner
- [x] Filtre **sayfalamadan önce** uygulanır: 5 sahipli + 5 sahipsiz satırda `take=3` **üç sahipli** satır döner (`SessionStoreContract`, dört koşumda)
- [x] `owner_id IS NULL` satır sahipli listede **hiç** görünmez; yönetim listesinde görünmeye devam eder
- [x] Başka sahibin oturumunda `Read`/`Delete`/`Branch` `404` döner ve gövdesi var olmayan oturumla **birebir aynıdır**
- [x] Gövdedeki hiçbir alan sahibi değiştiremez
- [x] Dallandırma sahibi **kaynaktan** korur; idempotent create ikinci istekte sahibi değiştirmez
- [x] `RequireAuthenticatedOwner=true` iken kimlik çözülemezse istek **reddedilir**; sahipsiz satır açılmaz
- [x] Kuyruğa alınmış `run` (`HttpContext` yok) sahibi iş zarfından alır
- [x] `SessionStoreContract` beş yeni iddiayı bellek içi + üç SQL sağlayıcısında kanıtlar
- [x] Üç migration seti uygulandı; `SqlTextSnapshotTests` ve üç `IntegrationTests` yeşil
- [x] `SessionEndpoints.cs:36`'daki "liste filtrelenmez" yorumu **düzeltildi** — artık filtrelenir ve gerekçesi yazılı
- [x] `IRunAuthorizationHandler` XML'indeki *"it never learns which user … a session belongs to"* cümlesi **güncellendi**
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, sahipli liste çıktısı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi
      (MT-SEC-164..174, on biri). Onunun her biri **otomatik karşılığıyla** koşuldu
      (`SessionOwnershipTests`, 36 case yeşil) ve çekirdek olanları ayrıca örnek
      uygulamaya karşı **elle** doğrulandı — koşum kaydı yukarıdaki bölümdedir.
      🚨 MT-SEC-174 (👤 migration kilidi) **koşulmadı**: üretim boyutunda dolu bir
      tablo ister ve süresi tahmin edilemez, ölçülmelidir
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (`concepts/sessions.md`, `guides/write-your-own-store.md`); `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Sahipli liste — yalnız A'nın oturumları
curl -s "$APU/api/sessions?take=200" -H "Authorization: Bearer $TOKEN_A" \
  | jq '[.[] | .ownerId] | unique'
# beklenen: ["<A>"]

# Filtre sayfalamadan ÖNCE — üç satırın üçü de sahipli
curl -s "$APU/api/sessions?take=3" -H "Authorization: Bearer $TOKEN_A" | jq 'length'

# Ret gövdesi, var olmayan oturumla aynı olmalı
diff <(curl -s "$APU/api/sessions/$B_SESSION" -H "Authorization: Bearer $TOKEN_A") \
     <(curl -s "$APU/api/sessions/yok-boyle-bir-id" -H "Authorization: Bearer $TOKEN_A")
```

---

## Plandan Sapmalar

1. **`AgentPrismSessionOwnershipOptions` `AgentPrism.Core`'a değil
   `AgentPrism.Abstractions`'a girdi.** Plan onu `Core/Sessions/` altında
   gösteriyordu. `ISessionStore`'un kendi sözleşme XML'i (`SessionRecord.OwnerId`,
   `SessionQuery.OwnerId`) kuralı bu tipi ADLANDIRMADAN anlatamıyor ve
   Abstractions Core'u göremez — Core-yerleşimli bir seçenek sözleşme
   dokümanını `<c>` yer tutucularına indirger ve sahiplik sözleşmesini iki
   pakete böler. Emsal: `AgentPrismEgressOptions`, `AgentPrismMcpSecurityOptions`
   (ikisi de güvenlik sınırı, ikisi de Abstractions'ta). Açık Soru 1'in cevabı
   ("A: ayrı bir sınıf") değişmedi; yalnız paketi değişti.

2. **Üçüncü bir seçenek alanı eklendi: `ManagementPolicy`.** Plan yalnız
   `Enabled` ve `RequireAuthenticatedOwner` öngörüyordu, ama manuel kabul case
   10 ("Operator olarak filtresiz kiracı listesi") bir yönetim payı olmadan
   kurulamıyordu. Kullanıcıya soruldu (2026-09-06); "istek başına `Operator`
   politikası" seçildi. Varsayılan LİTERAL yazılıdır ve
   `SessionOwnershipManagementPolicyCrossCheckTests` ile
   `AgentPrismPolicies.Operator`'a bağlanır (K-691).

3. **🚨 Kapı `run` başlatan yüzeylere de gerekti — planda yoktu.** Plan yalnız
   oturum uçlarını (`Read`/`Delete`/`Branch`) ve listeyi kapsıyordu.
   Fonksiyonel test yazılırken görüldü ki `POST /api/agents/{ad}/run` gövdesinde
   `sessionId` ile başka kullanıcının oturumunu SÜRDÜRMEK hâlâ mümkündü — ve
   bir turu sürdürmek konuşmanın tamamını modele geri okur. Sahiplik korunuyordu
   (satır B'ye geçmiyordu) ama B, A'nın konuşmasını okumuş oluyordu. Üç yüzey
   (`AgentEndpoints`, `WorkflowEndpoints`, `OpenAIResponsesEndpoints`) kapıyı
   kendi gövdelerinde çağırır hâle geldi ve `RunAuthorizationCoverageTests` bir
   kulvar daha kazandı (K-691). **Planın "yalnız oturum uçları" varsayımı
   yanlıştı.**

4. **🚨 Ambient scope tek başına yetmedi.** Plan (ve Açık Soru cevabı) kuyruklu
   `run`'ın sahibini iş zarfına yazıp işçide `AmbientRunAttributionScope` ile
   geri oynatmayı öngörüyordu. Fonksiyonel test bunu KIRMIZI gösterdi: tüketici
   kendi `IRunAttributionContext`'ini kaydettiğinde ambient scope hiç okunmuyor
   ve oturum, işçinin o an gördüğü kullanıcıya yazılıyordu. Çözüm sahiplik
   çözümünde ambient scope'a ÖNCELİK vermek oldu (K-692) — dar bir istisna,
   yalnız sahiplik için; attribution okuyucusu değişmedi.

5. **Ret `403` gövdesi iki yerden üretiliyor, tek `errorType` ile.** Erken ret
   (`GetOrCreateSessionAsync`, agent koşmadan önce) bir exception, uç kapısı
   (`CheckRunSessionAsync`) bir `ProblemHttpResult` üretir. İkisi de
   `errorType = session_owner_required` taşır; aksi hâlde istemci aynı kararı
   iki farklı biçimde görürdü.

6. **Açık Soru 2 (`runs.owner_id`) ölçüldü: gerek yok.** `runs.user_id` Faz
   68'den beri var, `RunLabels.MaxUserIdLength` ile sınırlı ve aynı anlamı
   taşıyor. `owner_id` onun tipini ve sınırını birebir aldı (Açık Soru 4).

7. **Açık Soru 3 (`?owner=none` yönetim süzgeci) planlandığı gibi AÇILMADI.**
   Yönetim listesi zaten filtresizdir ve sahipsiz satırları içerir.

8. **`SessionEndpoints`'in üç `404` gövdesi tek bir fabrikaya indirildi**
   (`SessionNotFound`). Planda yoktu; K-671'in "birebir aynı gövde" kuralı elle
   tekrarlanan üç kopyayla korunamaz.

9. **Ses ucu da kapsandı.** Planın 148.4 tablosu sesi yalnız "sahip korunur"
   satırında anıyordu; erişim reddi listelenmemişti. Bir ses soketi bağlandığı
   oturuma YAZAR, bu yüzden `VoiceConversationEndpoint` de
   `SessionOwnershipGate.DeniesAsync` çağırır. K-283 korunur: var olmayan oturum
   reddedilmez.

10. **Akışlı (SSE) yolda ret bir `error` çerçevesidir, `403` değil.** SSE
    başlıkları oturum çözümünden önce gönderilir (K-324'ün fiziksel kısıtı,
    `AgentPrismSessionConflictException` de aynı şekilde davranır). Fazın
    invariant'ı — sahipsiz satır açılmaz — orada da korunur. Oturum çözümünü
    `SseWriter.StartAsync`'in ÜSTÜNE taşımak bu fazın kapsamı dışında bırakıldı
    (devir notu).

## Bu Fazda Verilen Kararlar

| Karar | Konu |
|---|---|
| **K-688** 👤 | Sahiplik `sessions.owner_id` sütununda yaşar; süzgeç `WHERE`'de, sayfalamadan önce |
| **K-689** | Sahiplik bir kez atanır; dört depo da `COALESCE` eder; türetilen oturum kaynağın sahibini miras alır |
| **K-690** 👤 | Mod açıkken attribution bir yetkilendirme girdisidir; çözülemeyen kimlik `403` |
| **K-691** | Kapı `run` başlatan yüzeyleri de kapsar; yönetim muafiyeti yalnız listeye; kapı HTTP sınırında yaşar |
| **K-692** | Açık ambient scope sahiplik çözümünde kayıtlı servisi ezer |
| **K-693** | Sahipsiz eski satır tekil erişimde reddedilmez, listede görünmez; sahiplik geriye dönük değildir |

## Örnek Uygulama Koşumu (gerçek `run`)

`samples/AgentPrism.Api`, SQLite ve `EchoModelProvider` ile, mod **açık**
(`AgentPrism__SessionOwnership__Enabled=true`). Kimlik `X-Demo-User` başlığından
gelir (`DemoRunAttributionContext`). Migration çıktısı: **34** uygulandı (önceden
33 — yeni sette `0034_session_owner.sql`).

```
ada iki oturum açar, bob bir tane:
  ada-1 -> 200   ada-2 -> 200   bob-1 -> 200

ada listeler:  [{"id":"ada-2","ownerId":"ada"},{"id":"ada-1","ownerId":"ada"}]
bob listeler:  [{"id":"bob-1","ownerId":"bob"}]
ada'nın listesindeki benzersiz sahipler: ["ada"]

bob, ada'nın oturumunu okur:                     404
  gövde, var olmayan bir id'nin gövdesiyle BİREBİR aynı (yalnız traceId farklı)
bob, ada'nın oturumuna run atar:                 403  Session not authorized
kimliksiz istek bir oturum açmaya çalışır:       403  Session owner required
  ve `orphan` geri okunduğunda 404 — satır AÇILMADI

gövde sahibi zorlayamaz (ownerId/userId = "bob" gönderildi):
  run -> 200 · ada okur -> 200 · bob okur -> 404   (sahip "ada" kaldı)

bob'a 5 YENİ oturum daha açılır (listede en üstte olurlardı), sonra ada take=3 ister:
  [{"id":"forged","ownerId":"ada"},{"id":"ada-2","ownerId":"ada"},{"id":"ada-1","ownerId":"ada"}]
  → üç satırın üçü de ada'nın: süzgeç sayfalamadan ÖNCE uygulandı

veritabanı (agentprism_sessions):
  ada-1|ada  ada-2|ada  forged|ada  bob-1|bob  bob-x1..x5|bob
  sahipsiz satır YOK — reddedilen istek hiçbir şey yazmadı
```

## Denetim Bulguları

> `faz-denetim` koşuldu. Bulgular ve kapanışları aşağıdadır.

### 🔴 (1 bulgu, kapatıldı)

**`/v1/conversations` sahiplik kapısının TAMAMEN dışındaydı.**
`OpenAIConversationsEndpoints`'in üç işleyicisi (`RetrieveAsync`,
`DeleteAsync`, `ListItemsAsync`) aynı oturumlara başka bir adla ulaşıyor ve
yalnız kiracı kontrolü yapıyordu. Sonuç ölçüldü: `GET /api/sessions/{id}`
başka sahibin oturumuna `404` derken `GET /v1/conversations/{id}/items` o
oturumun **tüm geçmişini** döndürüyor, `DELETE /v1/conversations/{id}` ise
oturumu **siliyordu**. Bir kapı kilitli, yanındaki açık.

Sınıf, Faz 139'un dört yüzeyden ikisini kaçırmasıyla aynı: **uyumluluk yüzeyi
de bir yüzeydir**. Kapanış: üç işleyici `SessionOwnershipGate.DeniesAsync`
çağırır ve ucun kendi `NotFound` gövdesini döndürür (kiracı reddiyle birebir
aynı);
`RunAuthorizationCoverageTests.ExpectedSessionOwnershipFiles` altıncı dosyayı
kazandı; iki fonksiyonel test eklendi ve kapı devre dışı bırakılarak
**kırmızı görüldü**.

### 🟡 (6 bulgu, altısı da kapatıldı)

| # | Bulgu | Kapanış |
|---|---|---|
| 1 | Fail-closed sentinel boşluk karakterlerindendi; SQL Server `nvarchar` karşılaştırmasında **sondaki boşlukları yok sayar**, yani sentinel boşluk-only bir `owner_id` ile eşleşir ve kimliksiz çağıranın listesi sızardı — yalnız o sağlayıcıda | Dolgu karakteri görünür bir **kaçış dizisiyle** yazıldı. 🚨 Denetim sırasında ikinci bir kusur çıktı: dosyada gerçek bir **NUL baytı** vardı, bu yüzden `grep` dosyayı ikili sayıp o alanı hiç raporlamıyordu (MEMORY.md'nin K-525 dersinin aynısı). Tüm ağaç NUL için tarandı: başka vaka yok |
| 2 | Fazın Hata Modları tablosunun havale ettiği üç satırın (iptal · eşzamanlılık · `store` hatası) testi yoktu | Üçü de yazıldı: `Two_concurrent_turns_on_one_session_cannot_erase_the_owner`, `A_cancelled_write_leaves_no_unowned_row_behind`, `A_store_that_fails_the_write_does_not_report_a_session` |
| 3 | `WorkflowEndpoints` ve ses ucunun kapıları yalnız **kaynak taramasıyla** kanıtlanıyordu; yanlış `sessionId` geçirilse test yeşil kalırdı | Üç davranış testi: workflow reddi, ses soketi reddi (`404`), ve K-283'ün korunduğu (`An_unknown_session_still_opens_a_voice_socket`) |
| 4 | Yönetim politikası testleri gerçek ayrımı koşmuyordu — biri hiç politika kaydetmiyor, öteki hepsini `_ => true` ile kaydediyordu | Politika **cevabı çevrilebilir** bir iddiayla kaydedildi; aynı politika önce geçer (liste tam), sonra düşer (liste daralır). Fixture artık `store`'dan tohumlanıyor: `Operator` politikası `run` ucunu da koruyor, HTTP'den kurmak testin kendi anahtarını fixture'a bağlardı |
| 5 | `RunAuthorizationCoverageTests`'te yeni `<summary>` var olan bir bloğun önüne girmiş; `ResourceStartMarker` dokümansız kalmış, bir üye iki `<summary>` taşıyordu | Yeni bloklar `ResourceStartMarker`'ın **arkasına** taşındı |
| 6 | `run` başlatan üç ucun `WithDescription`'ı yeni `403` reddini anlatmıyordu; OpenAPI tüketicisi biçimi yalnız siteden öğrenebiliyordu | Üç açıklama da genişletildi; OpenAPI ve iki üretilmiş istemci yeniden üretildi |

### 🟢 (3 kalem — aday listesine)

Denetçinin aday olarak işaretlediği üç kalem devir notundadır: kapıdaki çift
oturum okuması, `guides/embedding.md`'ye K-692 satırı, ve `Idempotency-Key`
tekrarının manuel case'i.

### Denetimin temiz bulduğu başlıklar

İmza-gövde kayması (her `SessionRecord`/`SessionQuery` üretim noktası
`OwnerId` yazıyor; `AuditingSessionStore` düz geçiriyor) · plan dışı public API
(üçü de Plandan Sapmalar'da gerekçeli) · repo kuralları (İngilizce, `TryAdd*`,
K1 varsayılan kapalı, `secret` yok, `ConfigureAwait(false)` tam) · test tiyatrosu
yok. Denetçi ayrıca `/v1/responses` zincirinde saldırganın seçtiği bir kimliğe
yazım yolu olmadığını ve akışlı yolun `CheckRunSessionAsync` sayesinde gerçek
`403` verdiğini **doğruladı** (devir notunun akışa dair cümlesi buna göre
düzeltildi).

## Sonraki Faza Devir Notu

Sahiplik artık AgentPrism'in kendi verisidir. Devreden beş gerçek bilgi:

- 🚨 **Yeni bir HTTP yüzeyi bir oturuma dokunuyorsa İKİ şey birden gerekir:**
  yüzey `SessionOwnershipGate`'i **kendi gövdesinde** çağırır ve
  `RunAuthorizationCoverageTests.ExpectedSessionOwnershipFiles` listesine elle
  eklenir. Tarama dosya bazındadır. Liste bugün **altı** kalem: üç `run`
  başlatan yüzey (`sessionId` taşıyanlar) + `SessionEndpoints` + ses ucu +
  `OpenAIConversationsEndpoints`. 🚨 Sonuncusu bu fazın kendi denetiminin
  bulduğu 🔴 idi — uyumluluk yüzeyi de bir yüzeydir ve "oturum" kelimesini
  kullanmayan bir uç da oturuma dokunabilir.
  `TriggerEndpoints` ve `OpenAIChatCompletionsEndpoints` **bilerek dışarıdadır**
  — oturumsuz `run` başlatırlar; birine `sessionId` eklenirse listeye de eklenir.
- 🚨 **`ManagementPolicy` varsayılanı Abstractions'ta LİTERAL yazılıdır.**
  `AgentPrismPolicies.Operator` yeniden adlandırılırsa derleyici bunu görmez;
  `SessionOwnershipManagementPolicyCrossCheckTests` görür. İki taraftan birini
  tek başına değiştirme.
- 🚨 **Sahiplik `AgentSessionManager`'da DEĞİL HTTP sınırında zorlanır.** Bu
  bilinçlidir (K-691): yönetici arka plan işine de hizmet eder ve orada
  karşılaştırılacak bir çağıran yoktur. Sahipliği "tek noktada" toplamak isteyen
  bir sonraki faz bu tuzağa girer — `ApprovalResumeJobHandler` ve
  `RunContinuationJobHandler` her yeniden denemede kalıcı olarak düşer.
- **Akışlı yolda sahiplik reddi GERÇEK `403`'tür** (denetim doğruladı):
  `CheckRunSessionAsync` uç gövdesinde, `SseWriter.StartAsync`'ten **önce**
  koşar. Akışta `error` çerçevesine düşen tek kalan durum
  `AgentPrismSessionConflictException` ve elle damgalanmış bir kimliğin
  yazma anındaki reddidir — oturum çözümü hâlâ writer'dan sonradır. Onu
  `SseWriter.StartAsync`'in üstüne taşımak `409`'u da gerçek durum koduna
  çevirir; bu faz var olan davranışı değiştirmemek için kapsam dışı bıraktı.
  Aday olarak açılabilir.
- **Denetimin 🟢 kalemleri:** (1) mod açıkken `GetSessionAsync`/`DeleteSessionAsync`
  oturumu **iki kez** okur (kapı bir kez, gövde bir kez) — kapı okuduğu kaydı
  döndürseydi tur yarıya inerdi, ölçülmedi; (2) K-692'nin öncelik tersine
  çevirmesi `guides/embedding.md`'nin karşılaştırma tablosunda yok, yalnız
  sevk edilen XML'de anlatılıyor; (3) `Idempotency-Key` tekrarının manuel
  case'i yok (otomatik karşılığı var).
- **`runs` tablosuna sahip alanı EKLENMEDİ** (Açık Soru 2, cevap A): `user_id`
  yeterlidir ve ikinci bir alan iki kaynak üretirdi. `run` düzeyinde sahiplik
  isteyen bir faz önce bu ölçümü tekrar etmelidir.
