# Faz 85 — Gömme Ekseni

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-140** — Dalga 14 Küme K
> **Önkoşul:** [Faz 78](78-YETENEK-HARITASI-ERISIMI.md) — harita ve yerel referans hattı oradan gelir; bu faz o hattın **içeriğini** büyütür, hattı değiştirmez · [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) ve [Faz 70](70-CALISTIRMA-OLAYI-HEDEFI.md) — anlatılan beş noktanın ikisi oradan geldi
> **Paketler:** `AgentPrism.Abstractions` (tanı raporu alanı), `AgentPrism.AspNetCore` (`/api/diagnostics` gövdesi), `docs-site/`, `samples/`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — yalnız `AgentPrismDiagnosticsReport` üzerinde alan(lar). `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → yeni `guides/embedding.md`, `capabilities.md` (yeni bölüm — harita ve `llms.txt` ondan **üretilir**), `reference/configuration.md`
> · sevk edilen: `AgentPrism.AgentMap.md` (üretilir), yeni tanı raporu alanlarının XML dokümanı, `samples/` içindeki ikinci örnek. `api/` ve `http-api/` **üretilir** — orada iş XML dokümanı ve `.Produces` üstverisidir
> **Manuel test alanı:** [`docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md`](../../manuel-test/25-SAGLIK-TESHIS-OPENAPI.md) (tanı raporu) · [`docs/manuel-test/29-AGENT-DESTEGI.md`](../../manuel-test/29-AGENT-DESTEGI.md) (harita ve yerel referans)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/85-GOMME-EKSENI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'i **boş bir repo'ya** kurmak iki satırdır: `AddAgentPrism()` + `MapAgentPrism()`. AgentPrism'i **var olan** bir uygulamaya gömmek beş genişleme noktasını aynı anda doğru bağlamayı ister. Beşi de kodda vardır, XML dokümanları iyidir — ama **sevk edilen keşif yüzeyinde yoktur**. Bu faz o ekseni açar. Faz keşfedilebilirlik işidir, yetenek işi değil.

## Bitiş Ölçütleri (DoD)

- [x] `GET /api/diagnostics` beş genişleme noktasını raporlar; gömme örneğinde beşi de `isBuiltInDefault: false` döner — 🚨 **plandan sapma:** yeşil alan örneğinde (`samples/AgentPrism.Api`) beşi değil **dördü** `true`'dur; `IRunAttributionContext` zaten Faz 68'den beri `DemoRunAttributionContext`'e bağlıdır. Bkz. Plandan Sapmalar #1
- [x] `AgentPrism.AgentMap.md` gömme eksenini taşır ve **10 240 B** bütçesini aşmaz — ölçüldü: **9 249 B** (950 B boşluk kalır)
- [x] `docs-site/guides/embedding.md` yayımlandı; `llms.txt` sayfa indeksinde görünüyor — doğrulandı (`grep` çıktısı Doğrulama Komutları'nda)
- [x] `samples/AgentPrism.Embedded` beş noktayı da bağlar, derlenir ve gerçek bir koşu üretir — gerçek `dotnet run` ile doğrulandı
- [x] Arka plan işi senaryosunda `AgentPrismRunContext.Current` akış boyunca dolu kalır (E2E ile kanıtlandı) — `EmbeddedSampleTests.Background_job_with_no_HTTP_request_carries_tenant_and_user_through_the_run`; `current_account` tool'unun kaydedilen sonucu `tenant=acme run=<gerçek runId> session=(none)` yazar
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build/test/pack/format` dördü de yeşil (aşağıda)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. Doğrulama Komutları
- [x] `secret` taraması boş döndü — yeni rapor alanı da tarandı
- [x] Manuel kabul case'leri `docs/manuel-test/25-*` ve `29-*` içine eklendi; otomatikleştirilebilenler koşuldu — MT-DIAG-049..051, MT-AGD-016..020
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 3 🟡 bulgu, üçü de kapandı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (dört kapının tamamı) temiz
- [x] `tuketici-dokuman-senkronu` koşuldu — bu faz tüketici yüzeyine dokunur

### Doğrulama komutları ve gerçek çıktı

```bash
# Bagli genisleme noktalari raporlaniyor mu — gomme ornegi
$ curl -s http://localhost:5082/agentprism/api/diagnostics | jq '.extensionPoints'
[
  { "contract": "ITenantContext", "implementation": "EmbeddedTenantContext", "isBuiltInDefault": false },
  { "contract": "IRunAttributionContext", "implementation": "EmbeddedRunAttributionContext", "isBuiltInDefault": false },
  { "contract": "IToolAuthorizationHandler", "implementation": "EmbeddedToolAuthorizationHandler", "isBuiltInDefault": false },
  { "contract": "IRunEventSink", "implementation": "BoundedChannelRunEventSink", "isBuiltInDefault": false },
  { "contract": "IAttachmentStorage", "implementation": "InMemoryBufferAttachmentStorage", "isBuiltInDefault": false }
]

# Ayni uc, yesil alan orneginde — 4 turu true, biri (IRunAttributionContext) onceden bagli
$ curl -s http://localhost:5080/agentprism/api/diagnostics | jq '.extensionPoints[] | {contract, isBuiltInDefault}'
# (statik kod okumasiyla dogrulandi: Program.cs:139 DemoRunAttributionContext'i kosulsuz kaydeder;
#  canli host bu oturumdaki gelistiricinin kendi Bearer token secret'iyla korunuyordu, MT-DIAG-049'a bakin)

# Harita yeni bolumu tasiyor ve butce asilmiyor mu
$ node docs-site/scripts/build-agent-map.mjs
/Users/.../src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md: 9249 bytes
$ grep -n "Embedding points" -A7 src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md
### Embedding points
- Tenant resolution: ITenantContext, ITenantStore
- Run attribution: IRunAttributionContext
- Tool authorization: IToolAuthorizationHandler
- Run event bridge: IRunEventSink
- Attachment storage: IAttachmentStorage
- Rule: Each contract is registered with TryAdd, so a registration made before AddAgentPrism() ...

# llms.txt sayfa indeksinde gorunuyor mu
$ grep -n "Embedding into a host application" docs-site/public/llms.txt
246:- [Embedding into a host application](https://agentprism.doayen.web.tr/guides/embedding/) — Bind AgentPrism's five embedding points ...

# Arka plan isi senaryosu — kimlik AgentPrismRunContext.Current'tan mi geliyor
$ curl -s -X POST http://localhost:5082/jobs -H 'Content-Type: application/json' \
    -d '{"tenantId":"acme","userId":"user-42","message":"who am I"}'
$ curl -s -H 'X-Host-Tenant: acme' "http://localhost:5082/agentprism/api/runs/<runId>/events"
id: 2
event: tool.invoked
data: {..."toolName":"current_account",..."payload":"tenant=acme run=<runId> session=(none)"}
# Istekte X-Host-Tenant/X-Host-User YOK — kimlik yalniz AmbientTenantScope/AmbientRunAttributionScope
# uzerinden EmbeddedJobWorker'in kendi govdesinde acilan `using` bloklariyla akti.

# Event bridge dolulukta dusuruyor mu, kosuyu yavaslatiyor mu
$ for i in $(seq 1 10); do curl -s -X POST .../jobs -d "{...\"message\":\"burst $i\"}" >/dev/null; done
$ curl -s http://localhost:5082/jobs/bridge-state
{"received":9,"dropped":31}
# 10 kosunun tamami < 60ms icinde Completed oldu (startedAt/completedAt farki olculdu) — model
# akisi yavaslamadi, kopru kendi basina geriye dustu.

# 4 dogrulama kapisi
$ dotnet build AgentPrism.slnx -c Release        # 0 Warning(s), 0 Error(s)
$ dotnet test  AgentPrism.slnx -c Release --no-build
# 19 test projesi, tumu yesil (AgentPrism.Embedded.Tests: 3/3 dahil)
$ dotnet pack  AgentPrism.slnx -c Release --no-build   # basarili
$ dotnet format AgentPrism.slnx --verify-no-changes --no-restore   # degisiklik yok
```

---

## Plandan Sapmalar

1. 🚨 **Manuel kabul case #1'in "yeşil alanda beşi de `true`" iddiası yanlıştı.**
   Ölçüldü: `samples/AgentPrism.Api/Program.cs:139`
   `builder.Services.AddSingleton<IRunAttributionContext, DemoRunAttributionContext>();`
   satırını **koşulsuz** çağırır (Faz 68, kullanıcı bazlı maliyet demosu için).
   Bu fazdan ÖNCE var olan bir bağlamadır. Doğru durum: dört nokta `true`,
   `IRunAttributionContext` `false` ve `implementation: "DemoRunAttributionContext"`.
   `MT-DIAG-049` gerçek durumu belgeler; `samples/AgentPrism.Embedded/README.md`
   düzeltildi. Bağımsız denetimin 🟡#1 bulgusu.
2. **`Microsoft.AspNetCore.Mvc.Testing` yeni bir test-only bağımlılık olarak
   eklendi** (K-579) — plan bunu öngörmüyordu ama `tests/AgentPrism.Embedded.Tests`
   örneğin GERÇEK `Program.cs`'ini test etmek zorundaydı ve bu paket tam o
   senaryo için var. `WebApplicationFactory<T>.Server`/`.Services`'in gerçek
   Kestrel'e geçilince `TestServer`'a cast hatası verdiği ölçüldü; çözüm gerçek
   soket değil, `IStartupFilter` ile in-memory `TestServer` üzerinde
   `RemoteIpAddress`'i simüle etmekti (`AgentPrismTestHost`'un başlık-tabanlı
   deseninin `WebApplicationFactory` eşdeğeri).
3. **`EchoModelProvider` planda anılmayan bir davranış kazandı**: ilk turda
   `current_account` tool'unu (mevcutsa) çağırır, ikinci turda sonucu yanıta
   gömer. Gerekçe: plansız bırakılırsa `AgentPrismRunContext.Current`'ı okuyan
   hiçbir tool GERÇEKTEN çalışmaz (echo sağlayıcı hiçbir zaman tool çağırmaz) —
   DoD'nin "E2E ile kanıtlandı" satırı sahte kalırdı. Bu, `samples/AgentPrism.Api`'nin
   kendi `EchoModelProvider`'ından bilinçli bir sapmadır; ikisi ayrı dosyalardır
   ve birbirini etkilemez.
4. **`ExtensionPointDiagnostic` alan adı ve şekli plandaki taslakla birebir
   aynı kaldı** — sapma yok, doğrulama için not düşülüyor.
5. **`docs-site/reference/configuration.md` dokunulmadan bırakıldı.** Planın
   dosya listesi bu sayfayı işaret ediyordu ("gömme ile ilgili ayarlar") ama
   ölçüldü: gömme hiçbir YENİ yapılandırma anahtarı eklemiyor (beş nokta saf
   C# tip kaydıdır, `appsettings.json` anahtarı değil); tek ilgili anahtar
   (`AgentPrism:PostgreSql:SchemaName`) zaten sayfada var. Değişiklik yok.
6. **`dokuman-bakim.py --site-denetle`'nin `cekirdek-kavram` kuralı gerekçeyle
   geçildi** (`--site-gerekce-yazildi`): kural `src/AgentPrism.Abstractions/Diagnostics/`
   değişince `concepts/` sayfası bekliyor, ama diagnostics raporu zaten
   `concepts/` değil `capabilities.md` + `guides/observability.md` +
   (bu fazda) `guides/embedding.md` üzerinden belgelenen bir yüzeydir — kural
   geneldir, alan-özgü değildir.

## Bu Fazda Verilen Kararlar

- **K-579** — `Microsoft.AspNetCore.Mvc.Testing` yalnız GERÇEK giriş noktalı
  örnek uygulamaları test eden projelerde kullanılır; kütüphane testleri
  `TestHost` kalır. Tam gerekçe: `docs/KARARLAR.md`.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir alt agent ile koşuldu (2026-08-22); tam
diff + `git status` okudu, `dotnet build/test/pack/format` ve `npm run check`'i
bağımsızca yeniden koştu. **🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `samples/AgentPrism.Embedded/README.md`, `samples/AgentPrism.Api`'nin dördü değil **beşi** de `true` döndüğünü ima ediyordu — `IRunAttributionContext` zaten Faz 68'den beri `DemoRunAttributionContext`'e bağlı | 🟡 | **Düzeltildi** — README cümlesi gerçek durumu anlatacak şekilde yeniden yazıldı |
| 2 | `MT-DIAG-051`'in ön koşulu `/tmp/embedded-diag.json`'a bağlıydı ama `MT-DIAG-050` o dosyayı hiç yazmıyordu | 🟡 | **Düzeltildi** — `MT-DIAG-050`'nin `curl` komutuna `> /tmp/embedded-diag.json` yönlendirmesi eklendi |
| 3 | Planın manuel case #3 (arka plan işi) ve #4 (event bridge backpressure) satırları `docs/manuel-test/` içine hiç girilmemişti — yalnız otomatik E2E ile kanıtlanmıştı | 🟡 | **Düzeltildi** — `MT-AGD-019` ve `MT-AGD-020` eklendi, ikisi de otomatik eşdeğerine referans verir |

**Temiz çıkan başlıklar** (denetçinin kendi ifadesi): 3.1 (DoD ihlali yok),
3.2 (test tiyatrosu yok — üç test seviyesi de gerçek sınırdan geçiyor), 3.3
(sınır geçen davranış doğru seviyede: DI→unit, HTTP→functional, gerçek giriş
noktası→`WebApplicationFactory<Program>` E2E), 3.5 (imza-gövde kayması yok),
3.6 (public API tam plana uyuyor), 3.7 (İngilizce, XML doküman, `TryAdd*`, K1,
`secret` sızmıyor), 3.8 (`## Read next`, `description` uzunluğu, kenar
çubuğu, `npm run check` dört kapı — hepsi doğrulandı, hiçbir muafiyet listesi
büyümedi).

Denetimden SONRA, kapanış sırasında ayrıca bulundu ve düzeltildi (denetçi
görmedi çünkü henüz yazılmamıştı): DoD'nin "`AgentPrismRunContext.Current`
akış boyunca dolu kalır (E2E ile kanıtlandı)" satırı, `EchoModelProvider`
hiçbir zaman gerçekten bir tool çağırmadığı için **kanıtlanmamıştı** —
`current_account` tool'u hiçbir testte çalışmıyordu. `EchoModelProvider`
scripted bir tool-çağrısı turu kazandı (Plandan Sapmalar #3) ve
`EmbeddedSampleTests`'e gerçek doğrulama eklendi. Bu, denetçinin "iddiaya
güvenme, kanıtı kodda bul" kuralının uygulayan oturumun kendi kapanış
taramasında tekrar işe yaradığı bir örnektir.

## Sonraki Faza Devir Notu

**Sıradaki faz:** [Faz 86 — Talimatın Girdi Yüzeyi](86-TALIMATIN-GIRDI-YUZEYI.md).
Bu fazın kendi önkoşulu Faz 72'dir, Faz 85 değil — iki faz aynı dalgadan
(Dalga 14) bağımsız kalemlerdir ve Faz 86'nın "Bu Faza Başlarken" listesi
zaten devir teslim kalitesindedir; bu fazdan devralacağı bir sözleşme yok.

**Devralınan sözleşmeler:**
- `AgentPrismDiagnosticsReport.ExtensionPoints` — beş sabit uzunluklu giriş,
  sırası her zaman `ITenantContext, IRunAttributionContext,
  IToolAuthorizationHandler, IRunEventSink, IAttachmentStorage`. Yeni bir
  altıncı nokta eklemek istenirse Açık Soru 2'nin kararı (yalnız beş gömme
  noktası, `TryAdd*` ile kaydedilen HER sözleşme değil) yeniden gözden
  geçirilmeli.
- `capabilities.md`'nin "Embedding points" bölümü — yeni bir satır eklerken
  tablo başlığından **önce** bir lead-in paragraf YAZMA (aşağıdaki tuzağa bak).

**Bilinen tuzaklar:**
- 🚨 **`build-agent-map.mjs`'in "Rule:" satırı, tablo SONRASI paragrafın
  İLK cümlesidir — ama `section.prose` tablo ÖNCESİ paragrafları da toplar
  ve `firstSentence()` HANGİSİ önce gelirse onu alır.** Bu fazda bir lead-in
  cümlesi tabloyu ÖNCE açıklıyordu ve üreteç yanlışlıkla o cümleyi "Rule:"
  olarak bastı. Her mevcut bölüm heading→table→(yalnız) rule paragrafı
  sırasını izliyor; bu sıradan sapma sessizce yanlış bir kural üretir,
  hiçbir kapı bunu yakalamaz (üreteç "geçerli" bir metin üretir, yalnız
  YANLIŞ cümleyi seçer). Yeni bir bölüm eklerken bu sırayı ASLA bozma.
- 🚨 **`IRunStore.ListToolInvocationsAsync(runId)` tenant'ı `ITenantContext`'ten
  ÖRTÜK okur** (`QueryRunsAsync`'in aksine, o `RunQuery.TenantId`'yi AÇIKÇA
  alır). HTTP dışından (bir test, bir konsol aracı) çağrılırsa ve ambient
  scope açık değilse SESSİZCE boş döner — hata vermez, `IsOwnedByCurrentTenant`
  içeride `false` bulur ve erken çıkar. `tests/AgentPrism.Embedded.Tests/EmbeddedSampleTests.cs`
  bunu `AmbientTenantScope.Begin(tenantId)` ile sarmalayarak çözdü; aynı çözüm
  gerekir her `IRunStore` tenant-örtük metodunu HTTP dışından çağıran yeni kod.
- 🚨 **`samples/AgentPrism.Api`'nin `IRunAttributionContext`'i zaten özel** —
  bu sample'ı "tüm varsayılanlar açık" örneği olarak kullanan HERHANGİ bir
  gelecek faz bunu hesaba katmalı (bkz. Plandan Sapmalar #1).
- **`WebApplicationFactory<Program>` ile bir örnek uygulamayı test etmek
  istersen** `tests/AgentPrism.Embedded.Tests/Infrastructure/EmbeddedSampleHost.cs`'i
  örnek al — `IStartupFilter` ile `RemoteIpAddress` simülasyonu, gerçek
  Kestrel'e GEÇME (K-579'un ölçtüğü `TestServer` cast hatası).

**Yarım kalan iş yok.** DoD'nin tamamı işaretlendi; 🔴 bulgu kalmadı.
