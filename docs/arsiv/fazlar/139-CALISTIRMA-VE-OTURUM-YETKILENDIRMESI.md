# Faz 139 — Çalıştırma ve Oturum Yetkilendirmesi

> **Durum:** ✅ Tamamlandı (2026-09-03)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-185** (tüketici turu 3, A1 + A8)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — yeni kontrat + `AgentRunScope`'a iki alan. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (yalnız başlık), yani shipped giriş **sıfır**: bugün eklemek bedava, GA'dan sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `guides/embedding.md` (genişleme noktası listesi), `concepts/sessions.md`, `concepts/runs.md`, `capabilities.md` · sevk edilen: XML `<example>`, `src/AgentPrism.Abstractions/README.md`
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
> git show 90e83ee7:docs/arsiv/fazlar/139-CALISTIRMA-VE-OTURUM-YETKILENDIRMESI.md
> ```
>
> Damıtıldı 2026-09-03 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün bir kiracıdaki her `Operator`, aynı kiracıdaki **başka bir kullanıcının** konuşmasını okuyabiliyor, ona yazabiliyor ve silebiliyor. AgentPrism sahipliği kiracı düzeyinde çiziyor; kiracı **içindeki** kullanıcıyı hiçbir yerde ayırmıyor. Bu faz sahipliği AgentPrism'e **öğretmez** — tüketiciye **sorar**.

## Bitiş Ölçütleri (DoD)

- [x] Handler kaydedilmemiş kurulumda **hiçbir** davranış değişmez (case 1 kanıt) — `Nothing_changes_when_no_handler_is_registered`, canlı doğrulama aşağıda
- [x] Dört run başlatan yüzeyin **dördü** de kapıdan geçer; `RunAuthorizationEndpointTests` dördünü ayrı ayrı kanıtlar (`Handler_denies_a_different_user_and_no_run_row_opens`, `Workflow_run_is_covered`, `Inbound_trigger_is_covered`, `OpenAI_compatible_endpoint_is_covered`)
- [x] `throw` eden handler çağrıyı **reddeder** (fail-closed) — `Throwing_handler_denies_the_run_fail_closed`
- [x] Reddedilen session okuması `404`, reddedilen liste `403` döner — `Denied_session_read_returns_404_not_403`, `Denied_session_list_returns_403`, `Denied_session_delete_returns_404`, `Denied_session_branch_returns_404`
- [x] Reddedilen run `runs` satırı **açmaz** ve kota **tüketmez** — `Handler_denies_a_different_user_and_no_run_row_opens`, `Denied_run_does_not_consume_the_quota`
- [x] `AgentRunScope.UserId` tool gövdesinde görünür; akışlı yolda da dolu — `Allowed_user_id_reaches_the_tool_via_scope` (varsayılan akışlı/SSE yolu üzerinden)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis`
      (üç koşumda `dotnet test AgentPrism.slnx` adımı iki kez tek bir testte
      kırmızı çıktı: `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance`
      — bu faz **öncesinde** `docs/hafiza/test-altyapisi.md`'de belgelenmiş,
      yalnız tüm çözüm birlikte koşarken kaynak çakışmasından ortaya çıkan
      bilinen bir flaky test; izole (`AgentPrism.Core.UnitTests` tek başına,
      4 kez) ve tek test filtreli (3 kez) koşumların tamamı geçti — regresyon
      DEĞİL)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-SEC-140..150, tamamı otomatik ve yeşil
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları — gerçek çıktı (2026-09-03, `samples/AgentPrism.Api`, handler kayıtlı DEĞİL)

```bash
$ curl -s "$APU/api/diagnostics" -H "$APB" | python3 -c "..."
6
ITenantContext SingleTenantContext True
IRunAttributionContext DemoRunAttributionContext False
IToolAuthorizationHandler AllowAllToolAuthorizationHandler True
IRunAuthorizationHandler AllowAllRunAuthorizationHandler True
IRunEventSink (none) True
IAttachmentStorage (database) True

$ curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" \
     -H "$APB" -H 'content-type: application/json' -d '{"message":"What is your return policy in one sentence?"}'
# ... SSE akışı, gerçek OpenAI yanıtı ("You can return most unused items within 30 days...") ...
HTTP: 200

$ curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/does-not-exist-xyz" -H "$APB"
{"type":"...","title":"Session not found","status":404,"detail":"There is no session with id 'does-not-exist-xyz'."}
HTTP: 404
```

Bu üç çağrı, handler kayıtlı OLMADIĞI (K1 varsayılan) davranışın gerçek bir
sunucuda bozulmadığını kanıtlar. Reddeden bir handler'ın 403/404 ürettiği
davranış — `samples/AgentPrism.Api`'ye geçici kod eklemek yerine —
`RunAuthorizationEndpointTests`'in 18 testinde gerçek bir `TestServer`
üzerinden (Kestrel'in kendisi değil ama aynı `RequestDelegate` boru hattı)
kanıtlanmıştır; bu skill'in Adım 2 notu ("birim testleri geçmesi yetmez")
model çağrısı içeren gerçek entegrasyon davranışına ("yapılandırma okunmuyor",
`AsyncLocal` kaybı gibi) işaret eder — bu fazın kapısı ise saf HTTP
yönlendirme/red mantığıdır ve `TestServer` bunu Kestrel'le birebir aynı
kod yolundan (`RequestDelegateFactory`) çalıştırır.

---

## Plandan Sapmalar

1. **`SessionAuthorizationRequest.SessionId` planın taslağında `required string`
   idi, gerçekte `string?`.** `List` erişiminin tekil bir session kimliği yok;
   `required` bırakılsaydı çağıran boş dizge gibi bir sentinel uydurmak zorunda
   kalırdı — K1'in "sıfır sürpriz" ruhuna aykırı bir gizli sözleşme. `Access ==
   List` iken `null`, diğer üç erişimde her zaman dolu.
2. **`RunAuthorizationGate.CheckRunAsync`/`CheckSessionAsync` planın ima ettiği
   gibi `IResult?` değil, `ProblemHttpResult?` döner.** Ölçüldü:
   `Microsoft.AspNetCore.Http.Results.Problem(...)` derleme-anı `IResult`
   döndürür, `TypedResults.Problem(...)` ise `ProblemHttpResult`. Session
   uçlarının üçü (`GetSessionAsync`, `DeleteSessionAsync`, `BranchSessionAsync`)
   zaten `Results<T, ProblemHttpResult>` tipinde imzalar taşıyordu; gate `IResult`
   dönseydi bu union'lara **implicit olarak dönüşemezdi**. Bkz.
   `docs/hafiza/http-uc-tuzaklari.md`.
3. **`GET /api/sessions` ucunun imzası `Task<IResult>`'a gevşetilip sonra geri
   `Task<Results<Ok<IReadOnlyList<SessionRecord>>, ProblemHttpResult>>>`'a
   döndürüldü.** İlk gevşetme `OpenApiSnapshotTests` refresh'inde 200 yanıtının
   OpenAPI çıktısından SESSİZCE düştüğünü, yerine yalnız 403'ün kaldığını
   gösterdi — somut dönüş tipi olmayınca üretici yalnız açık
   `.ProducesProblem(...)` çağrısını görüyor. Aynı hafıza notu.
4. **Plandaki test sınıfı isimleri (`RunAuthorizationCoverageTests`,
   `RunAuthorizationOrderTests`, `QueuedRunAuthorizationTests`,
   `RunScopeAttributionTests`, `RunAuthorizationConcurrencyTests`) ayrı
   dosyalara açılmadı; tek bir `RunAuthorizationEndpointTests` (18 test) altında
   toplandı.** Her biri plandaki senaryoyu birebir kanıtlıyor (bkz. "Testler"),
   yalnız dosya sınırı farklı — tekrarlanan host-kurulum kodunu (`StartAsync`
   yardımcı metodu) tek dosyada paylaşmak, aynı senaryoyu beş küçük dosyaya
   bölmekten daha az tekrar üretti.
5. **`TenantIsolationContract` (plandaki sözleşme testi tablosu) ayrı bir
   `Contracts/` sınıfı olarak açılmadı.** Bu fazda kalıcı bir session-sahiplik
   veri modeli yok — kanıtlanacak şey yalnız "kapı kendi `TenantId` değeri
   uydurmuyor, `ITenantContext`'ten okuyor" — bu, `Handler_receives_the_ambient_tenant`
   fonksiyonel testiyle tam olarak kanıtlanıyor; SQL/bellek içi ayrımı taşıyan
   bir sözleşme testi burada fazladan soyutlama olurdu.
6. **`AgentPrismDiagnosticsCollector`'ın dokümanı "beş" yerine "altı" genişleme
   noktasından söz edecek şekilde güncellendi** (`AgentPrismDiagnosticsReport.cs`,
   `ExtensionPointDiagnostic.cs`) — plan bunu açıkça yazmıyordu ama Açık Soru
   1'in "A" cevabının doğal sonucu.

## Bu Fazda Verilen Kararlar

- **K-670** — `IRunAuthorizationHandler` dört run başlatan yüzeyin dördünü de
  kapsar; kapı `QuotaGate` ile aynı çağrı şeklini taşır (elle çağrı, ortak
  `IEndpointFilter` değil). Gerekçe ve sıra (`DrainGate → RunAttributionGate →
  RunAuthorizationGate → QuotaGate → PreflightGate`): `docs/KARARLAR.md`.
- **K-671** — Reddedilen session `List` erişimi `403` döner (asla filtrelenmez);
  reddedilen `Read`/`Delete`/`Branch` `404` döner ve gövdesi gerçekten var
  olmayan bir session'la birebir aynıdır. Gerekçe: `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent, 2026-09-03) çalışma ağacının
tamamını (kod + testler + `docs-site` + karar defteri) inceledi.

**🔴 bulgu:** Yok.

**🟡 bulgu (1, kapandı):** Riskler tablosunun vaat ettiği ikinci savunma
katmanı — "yeni bir run ucu eklenirse test onu görmez, bu yüzden testin
kendisi `QuotaGate` çağrı yerlerini `grep` ile sayan bir iddia taşır" —
kod yazılırken atlanmıştı; yalnız fonksiyonel `RunAuthorizationEndpointTests`
vardı. Kapatma: `RunAuthorizationCoverageTests` (Core.UnitTests/Architecture,
2 test) eklendi — dört bilinen run-başlatan dosyanın kaynak metnini
`RunAuthorizationGate.CheckRunAsync(` çağrısı için tarar. İlk yazımda
`Contains` düz dizge araması kullanıldı ve **kendi K-642 sınıfı tuzağına
düştü**: gerçek çağrı `RunAuthorizationGate` ile `.CheckRunAsync(` arasında
satır kırıyor (`if (await RunAuthorizationGate\n        .CheckRunAsync(...)`),
düz arama hiçbir zaman eşleşmiyordu — test dört dosyayı da "eksik" diye
işaretledi. Regex'e (`RunAuthorizationGate\s*\.\s*CheckRunAsync\s*\(`) geçilip
doğrulandı.

**Temiz çıkan başlıklar** (denetçinin raporundan): DoD kanıtları koda karşı
doğrulandı (3.1); test tiyatrosu yok, testler gerçek `IRunStore`/`IJobStore`/
`QuotaEnforcer` durumunu okuyor (3.2); doğru test seviyesi — DI/HTTP/kiracı/akış
sınırı gerçek `TestServer` üzerinden geçiyor (3.3); iptal/eşzamanlılık/boş
girdi/başka kiracı/alt sistem hatası beş sorusu kapsandı — `throw` fail-closed,
kiracı ambient okunuyor (3.4); imza-gövde takibi doğru — `attribution.UserId`/
`Labels` senkron gövdede okunup scope'a yazılıyor, `RunAttributionReader.Read`
üzerinden (3.5); yeni public API planla birebir, küçük sapmalar (yukarıdaki
"Plandan Sapmalar") gerekçeli (3.6); İngilizce, XML doküman tam, `secret` yok,
her `await`te `ConfigureAwait(false)` var (3.7); OpenAPI `403`/`404` eklendi,
`docs-site` dört sayfa güncel ve tutarlı, manuel test MT-SEC-140..150 eklendi (3.8).

## Sonraki Faza Devir Notu

Faz 140 ("İçerik Guard'ının Kaynağı") bu fazın dokunduğu boru hattına
(`ContentGuardPipeline`, model çağrı zinciri) dokunuyor ama **konu olarak
bağımsızdır** — bu fazın `IRunAuthorizationHandler`/`RunAuthorizationGate`'iyle
hiçbir çakışması yok, kendi "Bu Faza Başlarken" bölümü zaten tam.

Bu fazdan devreden, sonraki fazları etkileyebilecek tek gerçek bilgi:

- 🚨 **Yeni bir run başlatan HTTP yüzeyi eklenirse** (örn. bir MCP/A2A dışa
  açılan run tetikleyicisi, Faz 66'nın "gelen tetikleyiciler"inin bir
  benzeri), o yüzey `RunAuthorizationGate.CheckRunAsync(...)`'ı **kendi
  gövdesinde**, atıf kontrolünden sonra ve kota kontrolünden önce çağırmalı
  VE `RunAuthorizationCoverageTests.ExpectedRunStartingFiles`'a eklenmelidir
  — aksi hâlde kapı sessizce bypass edilir ve hiçbir test bunu yakalamaz.
- `IRunAuthorizationHandler` ve `IToolAuthorizationHandler` artık simetrik
  iki kardeş sözleşmedir (ikisi de fail-closed, ikisi de `TryAdd` ile
  kaydedilir, ikisi de tek metotlu değil — biri iki metotlu). Yeni bir
  yetkilendirme sınıfı gerekirse önce bu ikisinin desenine bakılmalı.
- Voice oturumları (`VoiceEndpoints`) bu fazın KAPSAMI DIŞINDA bırakıldı
  (Açık Soru 3, seçenek B) — ayrı bir depo ve ömür taşıdıkları için. Ses
  oturumu erişimine bir gün sahiplik eklenmek istenirse bu, ayrı bir faz
  olarak K-283'ün ölçtüğü vakayla aynı sınıfa dikkatle yaklaşmalıdır.
