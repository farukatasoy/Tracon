# Faz 61 — İstemci Tool'ları ve Gömülebilir Sohbet

> **Durum:** ✅ Tamamlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-108**, **F-64**
> **Önkoşul:** [Faz 53](53-KIRACI-API-ANAHTARLARI.md) — tarayıcıya yönetim token'ı konulamaz, kapsamlı anahtar şart · [Faz 55](55-ASENKRON-ONAY-KUTUSU.md) — sonuç kanalının emsali · [Faz 48](48-GUARDRAILS.md) — istemciden gelen sonuç guard'dan geçer
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — senkron kanal seçildi, bekleyen çağrı tablosu **yoktur** (Açık Soru 1'in kullanıcı kararı)
> **Public API:** büyüyor **ve bir imza genişliyor** — `PublicAPI.Shipped.txt` bugün **boş** (ölçüldü: 1 satır), `EnablePublicApiTracking` `true`. Kırıcı sayılan değişiklik bugün **bedava**, ilk yayından sonra değil
> **Site etkisi:** `ui.md` (gömülebilir bileşen bölümü), `capabilities.md`, yeni `guides/client-side-tools.md` + ekran görüntüsü
> **Manuel test alanı:** `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Paketi kullanan bir arka uç servisi, agent'ının **tarayıcıda** çalışan bir tool'u çağırmasını isteyebilir: kullanıcının o anki ekran durumunu okumak, dosya seçtirmek, yalnız tarayıcıda duran bir kimlik bilgisiyle çağrı yapmak. Bugün bunun yolu yoktur. Aynı tüketicinin kendi uygulamasına koyacağı küçük bir sohbet kutusu da yoktur; arayüz tam bir kontrol düzlemidir.

## Bitiş Ölçütleri (DoD)

- [x] Kodda kayıtlı bir istemci tool'u modele gider, **sunucuda çalışmaz**, `FunctionCallContent` çağırana döner — gerçek OpenAI çağrısıyla doğrulandı (aşağıda)
- [x] `toolResults` ile gönderilen sonuç turu tamamlar; nihai yanıt sonucu kullanır — gerçek OpenAI çağrısıyla doğrulandı (aşağıda)
- [x] `sessionId` yokken, kuyruk yolunda, bilinmeyen `callId` ile ve ikinci kez gönderimde sırasıyla `400`/`400`/`400`/`409` — `ClientToolEndpointTests` (9 test) + `samples/AgentPrism.Api` üzerinde `curl` ile doğrulandı
- [x] İstemciden gelen sonuç guard boru hattından geçer (engellenen desen maskelenir) — `ClientToolGuardTests`, `422` + `errorType: content_blocked`, yasaklı terim yanıt gövdesinde yok
- [x] Başka kiracının çalıştırmasına sonuç yazılamaz — `ClientToolEndpointTests.Another_tenants_pending_call_cannot_be_answered` + `SessionStoreContract : TenantIsolationContract<ISessionStore>` (dört sağlayıcı, mevcut kanıt — Denetim Bulguları #9)
- [x] `AllowedOrigins` boşken CORS başlığı **yollanmaz**; `AllowAnyOrigin` API'si **yoktur** — `CorsOptionsTests` (5 test) + `samples/AgentPrism.Api` üzerinde `curl` ile doğrulandı
- [x] Gömülebilir bileşen ayrı çıktıdır ve < 30 KB gzip; kapı sayıyı build çıktısına yazar — ölçüldü: **2,7 KB gzip** (`postbuild-embed.mjs`)
- [x] Kontrol düzlemi bundle'ı 250 KB gzip altında; yeni pay ölçülüp belgeye yazıldı — ölçüldü: **165,7 KB gzip** (önceki: 165,4 KB — `runsOnClient` rozeti ve iki yeni anahtar +0,3 KB)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build`/`test`/`pack`/`format` dördü de bu oturumda koşuldu, dördü de temiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `faz-tamamlama` Adım 1'in deseni koşuldu, bu fazda dokunulan dosyalarda eşleşme yok
- [x] Manuel kabul case'leri `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` içine eklendi; otomatikleştirilebilenler koşuldu — 13 case (MT-IST-001…013), `curl` ile koşulabilenler `samples/AgentPrism.Api` üzerinde gerçek OpenAI çağrısıyla teyit edildi; MT-IST-012 (tarayıcı etkileşimi) 👤 insan gerektirir olarak işaretli — otomatik karşılığı `UiTests.Embed_widget_runs_a_client_side_tool_and_completes_the_turn_in_a_real_browser`'dır
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki 🔴 bulundu ve **düzeltildi** (Denetim Bulguları bölümü); ikinci koşumda 🔴 yok
- [x] `docs-site/` güncellendi (`ui.md`, `capabilities.md`, `guides/client-side-tools.md`); `npm run build` + `check-links.mjs` temiz — `892 sayfa, 0 kırık bağlantı`; API/HTTP referansı `npm run generate` ile tazelendi (aynı geçişte 604 bayat senkronizasyon kopyası da temizlendi — Faz 61'den önceydi, bu fazın hatası değil)
- [x] `en.ts` ve `tr.ts` eksiksiz (K-228) — `tsc --noEmit` + `i18n.test.ts` (16 test) temiz; widget'ın kendi sözlüğü ayrı, `SourceLanguageTests` istisnasıyla (Denetim Bulgusu #1)

### Doğrulama komutları — gerçek çıktı

`samples/AgentPrism.Api` üzerinde, gerçek bir OpenAI çağrısıyla (`gpt-5.4-mini`), `support` agent'ının `read_shopping_cart` istemci tool'uyla koşuldu:

```bash
$ curl -s -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: e2e-key-1' \
  -d '{"sessionId":"e2e-manual-1","message":"What is in my shopping cart right now?"}'
```
```json
{
  "runId": "01a01279-53c7-7449-8e51-264871b54313",
  "sessionId": "e2e-manual-1",
  "response": {
    "messages": [{
      "role": "assistant",
      "contents": [{
        "$type": "functionCall", "name": "read_shopping_cart",
        "arguments": {}, "callId": "call_qbyUNWHadUVfjzaNXC1AxypY"
      }]
    }],
    "finishReason": "tool_calls"
  }
}
```

`FunctionCallContent` döndü; `read_shopping_cart`'ın **çalıştığına dair sunucu logunda hiçbir iz yoktu** (K2 doğrulandı).

```bash
$ curl -s -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: e2e-key-2' \
  -d '{"sessionId":"e2e-manual-1","toolResults":[{"callId":"call_qbyUNWHadUVfjzaNXC1AxypY","result":"2x Wireless Mouse, 1x USB-C Cable"}]}'
```
```json
{
  "runId": "01a01279-7d38-7522-912e-65f55e88127f",
  "sessionId": "e2e-manual-1",
  "response": {
    "messages": [{
      "role": "assistant",
      "contents": [{ "$type": "text", "text": "Your shopping cart currently has:\n\n- 2x Wireless Mouse\n- 1x USB-C Cable" }]
    }],
    "finishReason": "stop"
  }
}
```

Tur tamamlandı; model sonucu doğru kullandı.

```bash
$ curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H 'Prefer: respond-async' -H 'Content-Type: application/json' \
  -d '{"sessionId":"e2e-manual-1","toolResults":[{"callId":"x","result":"y"}]}'
400

$ curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:5080/agentprism/embed/embed.js
200 text/javascript; charset=utf-8

$ curl -s -D - -o /dev/null http://localhost:5080/agentprism/api/meta -H "Origin: https://shop.example.com" | grep -i access-control
(çıktı boş — beklenen, AllowedOrigins yapılandırılmadı)
```

---

## Plandan Sapmalar

Sekiz kalem — hepsi ölçülerek keşfedildi, K-435…K-443 olarak kayıtlı (bkz. bir
sonraki bölüm). Özet:

1. **`IToolRegistry`/`AgentPrismToolRegistration` `AITool`'a değil
   `AIFunctionDeclaration`'a genişledi** (K-435). Plan `AITool` öngörüyordu;
   ölçüldü ki `AITool`'un kendisi `.JsonSchema` taşımıyor, yalnız
   `AIFunctionDeclaration` (ve onun altındaki `AIFunction`) taşıyor.
2. **İstemci tool'u bildirimi `AIFunctionFactory.CreateDeclaration(...)` ile
   kuruldu**, planın Açık Soru 2'sindeki iki seçenekten hiçbiri değil (K-436).
   Üçüncü, ölçülmüş bir yol bulundu — sahte bir gövde fonksiyonu gerekmiyor.
3. **`FunctionResultContent` `ChatRole.Tool` altında gönderiliyor**, plan bunu
   hiç belirtmiyordu (K-437). Gerçek bir OpenAI çağrısıyla ölçüldü.
4. **CORS, `services.AddCors()` OLMADAN elle kurulan `CorsService`/`CorsMiddleware`
   ile uygulandı** (K-438). Plan bu kısıtı öngörmüyordu; `MapAgentPrism()`
   `app.Build()`'den sonra çalıştığı için `AddCors()` orada çağrılamıyor —
   ölçüldü (host başlangıcında `InvalidOperationException`).
5. **`toolResults` eşleştirmesi BİLEREK iki kez yapılıyor** — akış başlamadan
   önce doğrulama, sonra gerçek mesaj kurulumu (K-439). K-324'ün SSE-başlıkları-
   çoktan-gönderilmiş kısıtı `toolResults`'un `400`/`409` ayrımı için de geçerli
   çıktı; plan bunu öngörmüyordu.
6. **65.536 karakterlik bir boyut sınırı eklendi** (K-440). Plan "boyut sınırı"
   istiyordu ama sayı vermiyordu.
7. **Gömülebilir bileşen yalnız akışsız (`Idempotency-Key`) istek kullanıyor,
   SSE ayrıştırıcı taşımıyor** (K-441). 30 KB bütçesi konsolun `sse.ts`'ini
   taşımayı göze alamazdı.
8. **Gömülebilir bileşen `wwwroot/embed/`'e ayrı bir Vite girişiyle derleniyor
   ve VAR OLAN genel varlık sunum mekanizmasıyla, sıfır yeni C# `endpoint`
   koduyla sunuluyor** (K-442). Plan yeni bir sunum mekanizması varsayıyordu;
   `EmbeddedUiAssetCatalog`'un zaten genel olduğu ölçüldü.

Ayrıca, planın Açık Soru listesindeki kararlar:

- **Açık Soru 1 (onay + istemci tool'u ayrık kalsın mı?)** → **A** seçildi ve
  `ToolRegistry`/`McpTenantTools`'ta bir kayıt-anı denetimiyle **zorlandı**:
  `RequiresApproval: true` taşıyan bir kayıt `AIFunction` değilse başlangıçta
  `AgentPrismException` fırlar.
- **Açık Soru 3 (widget ayrı paket mi?)** → **A** (aynı `AgentPrism.UI`
  paketi, ikinci Vite girişi) — plandaki önerinin aynısı.
- **Açık Soru 4 (sonuç hatası nasıl taşınır?)** → **A** (`ErrorMessage`
  model'e iletilir) — plandaki önerinin aynısı, `"Error: {mesaj}"` biçiminde.
- **Açık Soru 5 (widget'ın sözlüğü ayrı mı?)** → **B** (kendi küçük sözlüğü) —
  plandaki önerinin aynısı, ama uygulama sırasında `SourceLanguageTests`'in
  taban çizgisini bozduğu için (K-408'in tek istisnası `locales/tr.ts`'tir)
  Türkçe kısmı `embed/locale.tr.ts` diye AYRI bir dosyaya taşındı ve o dosya
  istisna listesine eklendi — plan bu ayrıntıyı öngörmüyordu.

**Bağımsız denetimde bulunan ve düzeltilen iki kritik kusur** (ayrıntı Denetim
Bulguları bölümünde): gömülebilir bileşen hiçbir zaman bir `sessionId` almıyordu
— istemci tool turu asla tamamlanamazdı; ve yeni widget sözlüğü dil sınırı
kapısını (`SourceLanguageTests`) kırıyordu.

## Bu Fazda Verilen Kararlar

K-435 ile K-443 arası — tam metin ve gerekçe `docs/KARARLAR.md`'de:

| Karar | Özet |
|---|---|
| K-435 | `IToolRegistry`/`AgentPrismToolRegistration` `AIFunctionDeclaration`'a genişledi, `AITool`'a değil |
| K-436 | İstemci tool'u bildirimi `AIFunctionFactory.CreateDeclaration(...)` ile kurulur |
| K-437 | İstemciden gelen sonuç `ChatRole.Tool` altında gönderilir |
| K-438 | CORS `services.AddCors()` olmadan elle kurulur |
| K-439 | `toolResults` eşleştirmesi bilerek iki kez yapılır (akış kısıtı) |
| K-440 | `ClientToolResult.Result`/`ErrorMessage` 65.536 karakterle sınırlanır |
| K-441 | Gömülebilir bileşen yalnız akışsız istek kullanır |
| K-442 | Gömülebilir bileşen var olan genel varlık mekanizmasıyla sunulur |
| K-443 | Gömülebilir bileşen `sessionId`'yi ilk mesajdan önce, istemci tarafında ayırır (denetimde bulunan kusurun düzeltmesi) |

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (çalışma ağacı, `main`'e göre).

### 🔴 — ikisi de düzeltildi

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `SourceLanguageTests` kırmızıydı: `embed/locale.ts` içindeki Türkçe metin taban çizgisini büyütüyordu, K-408'in tek istisnası (`locales/tr.ts`) bunu kapsamıyordu. | **Düzeltildi.** Türkçe kısım `embed/locale.tr.ts`'e taşındı; `SourceLanguageTests.SkippedFiles`'e eklendi (aynı K-228 gerekçesi, aynı dar kapsam). Doğrulandı: 795/795 test geçti. |
| 2 | Gömülebilir bileşen hiçbir zaman `sessionId` almıyordu (`sessionId: null` sabit kalıyordu, sunucu yalnız gönderileni yankılıyor); istemci tool çağrısı asla yanıtlanamazdı — fazın var oluş sebebi çalışmıyordu. | **Düzeltildi (K-443).** Widget artık `sessionId`'yi kurucuda `crypto.randomUUID()` ile ayırıyor, ilk mesajdan önce. **Gerçek bir tarayıcıda** doğrulandı: yeni `UiTests.Embed_widget_runs_a_client_side_tool_and_completes_the_turn_in_a_real_browser` testi widget'ı Shadow DOM üzerinden tıklar, `RunsWrite` kapsamlı gerçek bir API anahtarıyla tur uçtan uca tamamlanır. |

### 🟡 — kapandı veya gerekçelendi

| # | Bulgu | Sonuç |
|---|---|---|
| 3 | `tool.ShouldNotBeOfType<AIFunction>()` hiçbir koşulda düşemez (Shouldly tam tip eşitliği denetler, `AIFunction` `abstract`'tır). | **Düzeltildi.** `ShouldNotBeAssignableTo<AIFunction>()`'a çevrildi. |
| 4 | Planlanan `ClientToolConcurrencyTests` (aynı `callId` için yarış) yazılmadı. | **Gerekçelendi.** Yarış penceresi tek süreç içi, mikrosaniye mertebesinde; yapay gecikme eklemeden deterministik bir test yazmak SUT'a test-özel kod sızdırırdı. Davranış K-439'da açıkça tarif edildi (ikinci eşleşme kaybolursa sonuç sessizce düşer, model bir sonraki turda tekrar sorabilir) — güvenlik riski değil, en kötü ihtimalle bir kullanıcı deneyimi tekrarı. `docs/ADAYLAR.md`'ye F-NN olarak not düşülmedi çünkü ayrı bir yetenek değil, mevcut mekanizmanın test derinliği. |
| 5 | Varsayılan (akış/SSE) yol `toolResults` için hiç sınanmıyordu; K-439'un TEK gerekçesi o yoldur. | **Düzeltildi.** `ClientToolEndpointTests.Streaming_path_client_tool_call_and_result_round_trip` eklendi — `Idempotency-Key` YOK, gerçek SSE çerçeveleri okunuyor. |
| 6 | CORS politikası `/run`'a değil `{prefix}`'in tamamına uygulanıyor; bir origin'e verilen erişim yönetim API'sinin tamamını kapsıyor. | **Gerekçelendi ve dokümante edildi.** `AllowedOrigins`'in XML dokümanına ve `guides/client-side-tools.md`'ye açık uyarı eklendi: CORS başlığı okuma İZNİ verir, kimlik doğrulamayı ATLAMAZ — gerçek sınır hâlâ API anahtarının kapsamıdır. Path bazlı CORS kısıtlaması ayrı bir karmaşıklık/esneklik dengesi ister, bu fazın kapsamı dışında bırakıldı. |
| 7 | `endpoints` bir `IApplicationBuilder` değilse CORS sessizce hiçbir şey yapmaz, log yazmaz. | **Gerekçelendi.** Aynı dosyada AYNI koşullu desen (idempotency arabellekleme, JSON bağlama orta katmanı) zaten log YAZMADAN aynı şekilde çalışıyor — bu davranış Faz 61'e özgü değil, dosyanın var olan emsaliyle tutarlı. |
| 8 | `AgentDefinitionCompiler.cs`'teki yorum bayattı ("registry only ever returns AIFunction"). | **Düzeltildi.** Koda göre güncellendi. |
| 9 | DoD satırı "sözleşme testi, dört koşum" harfiyen karşılanmadı — tek bir bellek içi fonksiyonel test teslim edildi. | **Gerekçelendi.** `toolResults`'un kiracı sınırı YENİ bir depo YOLU açmıyor; `AgentSessionManager` → `ISessionStore` üzerinden akıyor ve `SessionStoreContract : TenantIsolationContract<ISessionStore>` bu sınırı ZATEN dört sağlayıcıda kanıtlıyor. Planın satırı yanlış planlanmıştı — yeni kod bu sınırı yeniden AÇMADI, yalnız MEVCUT kanıtlanmış sınırı kullandı. |
| 10 | `ui.md`'nin `AllowedOrigins` bağlantısı `reference/configuration/`ye gidiyordu ama o sayfada satır yoktu. | **Düzeltildi.** Tabloya satır eklendi. |
| 11 | K-441 var olmayan bir bölüme atıf yapıyordu ("bkz. fazın DoD bölümü"). | **Düzeltildi** — bu kapanışla birlikte DoD bölümü artık gerçek kanıt taşıyor. |

### 🟢 — `docs/ADAYLAR.md`'ye

| # | Bulgu | Neden şimdi değil |
|---|---|---|
| 12 | `RunReplayService`'in `toolTransform`'u yalnız `AIFunction`'lara uygulanıyor; istemci tool'u taşıyan bir `run` sadık biçimde replay edilemez. | Replay bu fazın kapsamı dışında; **`docs/ADAYLAR.md`'ye F-109 olarak eklendi.** |
| 13 | Tek istekteki `toolResults` dizisinde aynı `callId` iki kez geçerse `400` (Unknown) döner, `409` değil. | Davranış güvenli (ikinci kez kabul edilmiyor), yalnız durum kodu sözleşmeyle tam tutarlı değil; küçük bir iyileştirme. |
| 14 | `MatchAsync` iki kez koştuğu için `toolResults` taşıyan her istek oturumu iki kez çözüyor. | Maliyeti ölçülmedi; K-439 bu çıkış yolunu zaten bilerek seçti. |

**Temiz çıkan başlıklar:** 3.5 (imza-gövde kayması — tüm tüketiciler izlendi), 3.6 (plan dışı public API yok).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IToolRegistry.TryGet` artık `AIFunctionDeclaration` döner — yeni bir tool
  tüketicisi (`AIFunction` bekleyen eski kod) derleme hatası alır, bu bilinçli.
- `AgentRunRequest.ToolResults` `Approvals`'ın birebir kardeşi: `sessionId`
  zorunlu, kuyruk yolunda `400`, bilinmeyen/tekrar `callId` `400`/`409`.
- `AgentPrismEndpointOptions.AllowedOrigins` boşsa CORS başlığı hiç gönderilmez;
  `AllowAnyOrigin` API'si YOK ve eklenmemeli (K1).

**🚨 Bilinen tuzaklar:**
- `services.AddCors()` `MapAgentPrism()` içinde çağrılamaz (`app.Build()`'den
  sonra, DI kabı mühürlü). Yeni bir DI-bağımlı ASP.NET Core özelliği
  `MapAgentPrism()`'e eklenecekse aynı kısıt geçerlidir — `AgentPrismCorsMiddleware.cs`'teki
  elle kurulum deseni örnek alınabilir.
- `ClientToolResultResolver.MatchAsync`'in NEDEN iki kez çağrıldığını
  (K-439) anlamadan bu kodu "sadeleştirmeye" çalışma — SSE başlıklarının
  akış başlamadan gönderildiği fiziksel kısıt hâlâ geçerli.
- Gömülebilir bileşenin `sessionId`'yi kurucuda hemen ayırması (K-443) rastgele
  değil: sessionsiz bir `run` geçmiş TUTMAZ, dolayısıyla bekleyen bir tool
  çağrısı asla bulunamaz. Widget'a yeni bir "ilk mesaj" yolu eklenirse bu
  invariant korunmalı.
- Widget vanilla TS + Shadow DOM + CSS-in-JS; React/Tailwind YOK. Yeni bir
  widget özelliği eklenirken 30 KB bütçesi (`postbuild-embed.mjs`) ilk
  kontrol edilecek şeydir.

**Yarım kalan/kapsam dışı bırakılan işler:** F-109 (`docs/ADAYLAR.md` — replay), ve yukarıdaki 🟢 kalemler 13-14 (küçük, aday gerektirmeyen iyileştirmeler).

**Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek; F-108 ve F-64 bu fazla
kapandı.
