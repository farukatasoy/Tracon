# 35 — TypeScript İstemcisi ve npm Kanalı (`TSC`)

> **Alan kodu:** `TSC` · **Faz:** 84
> **Kaynak:** `packages/agentprism-client/` · `src/AgentPrism.UI/frontend/src/lib/{api.ts,server-types.ts}` ·
> `src/AgentPrism.UI/AgentPrism.UI.Frontend.targets` · `.github/workflows/ci.yml`
>
> Ortam kurulumu ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.
> Dosya numarası **35**'tir — planın yazıldığı anda "34" boştu ama kapanışta
> `33` VE `34` ikisi de doluydu (Faz 80'in `33-DOKUMAN-KAPILARI.md`'si ve
> Faz 83'ün `34-ISTEMCI-VE-CLI.md`'si); bkz. faz dokümanının Plandan Sapmalar
> bölümü.

---

## Bu dosya neyi kanıtlar

Gömülü yönetim konsolunun 43 dosya/155 çağrı noktası kapsayan göçünün
(elle yazılmış `api.ts` → üretilmiş `@agentprism/client`) davranışı **hiç
değiştirmediğini**, OpenAPI belgesiyle üretilen istemcinin sürüklenmesinin
gerçekten bir kapıyı kırdığını, ve npm paketinin NuGet kardeşiyle (`AgentPrism.Client`,
Faz 83) aynı sürüm numarasıyla yayınlandığını kanıtlar.

## Koşmadan önce

```bash
dotnet build AgentPrism.slnx -c Release
cd packages/agentprism-client && npm ci && npm test
```

Konsol case'leri gerçek bir dinleyici ister:

```bash
cd samples/AgentPrism.Api
dotnet run --no-build -c Release --urls http://localhost:5081
```

---

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-TSC-001` | Temiz `artifacts/`, Node.js 20.19+ kurulu | `dotnet build AgentPrism.slnx -c Release` | `@agentprism/client` arayüzden **önce** derlenir (`AgentPrismBuildClientPackage` hedefi); arayüz derlemesi geçer; sıfır uyarı |
| 2 | `MT-TSC-002` | Derleme geçti, `samples/AgentPrism.Api` ayakta | Konsol açılır | Agent listesi, `run` listesi ve `run` ayrıntısı **eskisi gibi** yüklenir |
| 3 | `MT-TSC-003` | Konsol açık, bir agent seçili | Playground'da bir `run` başlatılır | Token'lar **akarak** gelir — SSE yolu göçten etkilenmemiştir (`openStream` hâlâ elle yazılı) |
| 4 | `MT-TSC-004` | Konsol açık | Bir workflow çalıştırılır ve insan girdisi istenir | `resume`/`respond` akışı çalışır |
| 5 | `MT-TSC-005` | Konsol açık, **yanlış** token girilir | Herhangi bir ekran yenilenir | `401` token istemine döner ve istem "reddedildi" der — `onUnauthorized` bağlıdır |
| 6 | `MT-TSC-006` | `MapAgentPrism("/control")` ile başlatılmış uygulama | Konsol `/control` altından açılır | Tüm çağrılar çalışır — önek `document.baseURI`'den geliyor |
| 7 | `MT-TSC-007` | `docs/openapi/agentprism.json`'dan bir alan silinir, üretim koşulmaz | `dotnet test tests/AgentPrism.AspNetCore.FunctionalTests` **veya** `npm test` (`packages/agentprism-client`) | 🚨 **Test kırılır** — kapı budur. `dotnet build` **tek başına yakalamaz**: `schema.ts` commit'li ve içeriği değişmediği için `tsc` fark etmez. Case sonunda değişiklik geri alınır |
| 8 | `MT-TSC-008` | 👤 insan gerekir — npm kapsamı hazır | Temiz bir Node projesinde `npm i @agentprism/client` sonra bir agent listelenir | Paket kurulur, IntelliSense yol ve alan adlarını gösterir, çağrı sonuç döner |
| 9 | `MT-TSC-009` | 👤 insan gerekir — bir `v*` etiketi atıldı | CI koşumu izlenir | NuGet ve npm **aynı sürüm numarasıyla** yayınlanır; ikinci koşum var olan sürümü **atlar**, kırılmaz |
| 10 | `MT-TSC-010` | Tanı ucu açık bir uygulama (`EnableDiagnosticsEndpoint = true`) | `GET /agentprism/api/diagnostics` çağrılır | Rapor döner — §84.3 kanıtlanır |
| 11 | `MT-TSC-011` | Tanı ucu **kapalı** (varsayılan) | Aynı çağrı | `404` döner |

## Otomasyon karşılığı

Case 1, `dotnet build`'in kendisidir — arka arkaya iki tam-çözüm koşumuyla
(`0 Warning(s)`, `0 Error(s)`) kapanışta doğrulandı; frontend bundle'ı
**174.7 KB gzip** (bütçe 250 KB) olarak ölçüldü.

Case 2 ve 3, `AgentPrism.Ui.E2ETests`'in 56 senaryosuyla (Playwright, gerçek
tarayıcı) otomatikleştirilmiştir — özellikle `Playground_stream_arrives_and_tool_card_fills_in`
(case 3'ün karşılığı). Kapanışta ayrıca `samples/AgentPrism.Api` üzerinde elle
`curl` ile doğrulandı: `GET /api/agents` → 200 (13 agent), `GET /api/sessions` →
200, `GET /api/skills` → 200, konsolun `index.html`'i ve derlenen bundle
(`assets/index-taZ8z-M6.js`) doğru hash'le servis edildi.

Case 4'ün karşılığı `Pending_request_card_can_be_answered` ve
`Approvals_screen_shows_pending_request_and_run_completes_once_approved`'dır.

Case 5 **otomatikleştirilmemiştir**. `Shell_opens_and_asks_for_token_when_required`
DOĞRU token'ın kabul edildiği yolu kanıtlıyor, YANLIŞ token'ın reddedildiği yolu
değil. `auth.test.ts` (4 birim testi) `rejectToken`/`setToken` durum geçişlerini
kanıtlıyor, ama canlı bir `401` yanıtından `onUnauthorized` çağrısına kadar olan
uçtan uca teli değil. Bu case **elle koşulmalıdır** — E2E boşluğu `docs/ADAYLAR.md`'ye **F-145**
olarak devredildi.

Case 6'nın karşılığı `Assets_load_under_a_different_prefix`'tir.

Case 7, kapanışta elle koşuldu: `docs/openapi/agentprism.json`'dan
`/agentprism/api/diagnostics` yolu silindi, `dotnet build AgentPrism.slnx -c Release`
çalıştırıldı → **derleme başarılı** (sürüklenmeyi yakalamadı), sonra
`dotnet test tests/AgentPrism.AspNetCore.FunctionalTests` çalıştırıldı →
**1 test kırıldı** (619 toplam, 618 geçti — `OpenApiSnapshotTests`). Belge
sonra geri yüklendi ve `124 yol · 161 operasyon` olduğu doğrulandı.

Case 8 ve 9 👤 gerektirir; bu fazda koşulmadı (`@agentprism` kapsamı henüz
rezerve edilmedi — kullanıcı kararı).

Case 10 ve 11'in ikisi de kapanışta koşuldu. Case 10: `samples/AgentPrism.Api`
(`EnableDiagnosticsEndpoint = true`) üzerinde `curl` ile çağrıldı, gerçek bir
rapor döndü (`persistenceProvider: "PostgreSQL"`, `canConnect: true`,
`toolCount: 8`, `agentCount: 13`). Case 11, `DiagnosticsEndpointTests.Endpoint_does_not_map_at_all_while_off_by_default`
ile otomatikleştirilmiştir ve dört kapının parçası olarak zaten yeşildir —
ayrıca kendisi elle tekrar çağrılmadı çünkü otomasyon zaten varsayılan
kapalı-yapılandırmayı kapsıyor.
