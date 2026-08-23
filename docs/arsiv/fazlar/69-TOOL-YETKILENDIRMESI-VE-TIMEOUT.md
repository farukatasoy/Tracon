# Faz 69 — Tool Yetkilendirmesi ve Yürütme Timeout'u

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-113**, **F-114**
> **Önkoşul:** [Faz 6](06-GOZLEMLENEBILIRLIK.md) — tool onayı ve `ApprovalRequiredAIFunction` sarmalaması · [Faz 9](09-YONETISIM-VE-DENETIM-IZI.md) — rol politikaları ve denetim izi
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Generators`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (`tool_invocations` tablosuna yetki kararı ve timeout alanı). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — `ToolDescriptor` ve tool attribute'u alan alır, iki yeni arayüz gelir. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/tools.md`, `concepts/governance.md`, `getting-started/tools.md`, `getting-started/security.md`
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
> git show 9c32242:docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism bugün bir tool'un çağrılmasına **insan** kapısı koyabiliyor (onay), ama **izin** kapısı koyamıyor. "Bu kullanıcı bu tool'u hiç çağırabilir mi" sorusu sözleşmede yoktur. Aynı şekilde bir tool'un yürütmesi süresizdir. Bu faz ikisini birlikte kapatır: ikisi de aynı `ToolDescriptor` kaydına yazılır ve aynı sarmalama noktasında uygulanır.

## Bitiş Ölçütleri (DoD)

- [x] Kanca kayıtlı değilken hiçbir davranış değişmez (test kanıtıyla) —
      `AllowAllToolAuthorizationHandler` `TryAddSingleton`; 565/565 mevcut
      `AspNetCore.FunctionalTests` değişmeden geçti
- [x] Reddedilen tool çağrısı `run`'ı düşürmez; ret metni modele ulaşır —
      `ToolGovernanceEndpointTests.Denied_call_completes_the_run_and_marks_the_record_denied`,
      gerçek `FunctionInvokingChatClient` boru hattı üzerinden
- [x] Kanca `EmptyServiceProvider` üzerinden çözülmeye **çalışılmaz** —
      gerçek bir `run` ile kanıtlandı (K-218): üç yeni `ToolGovernanceEndpointTests`
      testi gerçek `FakeModelProvider` + `UseFunctionInvocation()` boru hattından
      geçiyor; ilk taslak yalnız birim testiyle "kanıtlanmıştı" — bağımsız
      denetim bunu 🔴 bulgu olarak işaretledi, gerçek `run` testleriyle kapatıldı
- [x] 1 sn timeout'lu bir tool ~1 sn'de kesilir; hata sınıfı `ToolTimeout` —
      `TimeoutAIFunctionTests` (gerçek `Stopwatch`) + `ToolGovernanceEndpointTests.Timed_out_call_...`
      (gerçek `run`, 300 ms timeout, 30 sn'lik gövde, `run` 10 sn altında tamamlanıyor)
- [x] Timeout circuit breaker sayacını **artırmaz** —
      `ToolGovernanceEndpointTests.Repeated_tool_timeouts_never_open_the_providers_circuit_breaker`
      (6 ardışık zaman aşımı, eşik 5, devre açılmıyor)
- [x] Onay bekleme süresi timeout'a düşmez (Case 6) —
      `ToolGovernanceEndpointTests.Approval_wait_is_not_bounded_by_the_tools_own_timeout`
      (200 ms timeout'lu tool, 1 sn bekleme, sonra onay — `run` `Completed`)
- [x] `GET /api/tools` yeni üç alanı döner — `docs/openapi/agentprism.json`
      yeniden üretildi (`effect`, `requiredPermission`, `timeout`, `ToolEffect` şeması)
- [x] Kaynak üreteci üç yeni alanı taşır —
      `GeneratedOutputTests.Effect_permission_and_timeout_carry_into_the_generated_registration`,
      `Undeclared_effect_permission_and_timeout_generate_defaults`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build`/`test`/`pack`/`format`
      hepsi temiz (arayüz dahil)
- `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı —
      **yapılamadı**: bu oturumun sandbox'ında model sağlayıcı API anahtarı/ağ
      erişimi yok. Yerine geçen kanıt: yukarıdaki `ToolGovernanceEndpointTests`
      GERÇEK bir `FunctionInvokingChatClient` boru hattından (`FakeModelProvider`,
      `ModelProviderRegistry`'nin her ham istemciyi `UseFunctionInvocation()` ile
      sardığı NOKTADAN) geçiyor — K-218'in "izole prob yeterli değil" dersini
      karşılıyor, ancak gerçek bir OpenAI/Anthropic/vb. çağrısı DEĞİL. `demo`
      tool `get_slow_report` (`samples/AgentPrism.Api/OrderTools.cs`) bir sonraki
      oturumda gerçek anahtarla koşulmaya hazır
- [x] `secret` taraması boş döndü — yeni eklenen hiçbir dosyada `secret` yok
- [x] Manuel kabul case'leri
      [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)
      içine eklendi (MT-SEC-120..127). Her case'in senaryosu — ret/timeout/devre
      kesici/onay+timeout — `ToolGovernanceEndpointTests`'te gerçek bir `run`
      üzerinden AYRICA otomatik test edildi; ancak case'lerin kendisi
      `samples/AgentPrism.Api`'ye karşı gerçek model anahtarıyla curl ile
      **koşulmadı** (sandbox kısıtı) — bu iki doğrulama birbirinin YERİNE
      geçmez, ikinci koşum sonraki oturuma devredildi
- [x] `faz-denetim` koşuldu; taze bağlamlı bağımsız denetçi 2 🔴 + 3 🟡 bulgu
      buldu — ikisi de 🔴 (gerçek-`run` kanıtı eksikliği, `RunErrorClass.ToolTimeout`
      arayüz sözleşmesinden eksik) kapatıldı, 🟡'lardan ikisi (üreteç snapshot testi,
      devre kesici regresyon testi) test eklenerek kapatıldı, biri (attribute
      `TimeoutSeconds` tip farkı) aşağıda dokümante edildi — **🔴 bulgu kalmadı**
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz —
      `concepts/tools.md` (yeni "Authorization and timeout" bölümü),
      `concepts/governance.md` (yeni "Tool authorization" alt bölümü),
      `getting-started/security.md` (checklist satırı), `ui.md` (rozet notu).
      **Ekran görüntüsü güncellenmedi** — `AGENTPRISM_UI_SCREENSHOTS=1` E2E
      koşumu bu oturumda yapılmadı, sonraki oturuma devredildi
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — bkz. aşağıda

### Doğrulama komutları

```bash
# Yeni alanlar sözleşmede mi
curl -s http://localhost:5081/agentprism/api/tools | jq '.[0]'

# Timeout gerçekten kesiyor mu — sure olculur
time curl -s -X POST http://localhost:5081/agentprism/api/agents/slow/run \
  -H 'content-type: application/json' -d '{"message":"call the slow tool"}'
```

---

## Plandan Sapmalar

1. **MCP tool'ları için sarmalama `ToolRegistry`'de değil, `McpTenantTools.Create`'de
   TEKRARLANDI.** Plan yalnız `ToolRegistry`'yi ("sarmalama noktası zaten doğru
   yerde", 69.1) konu alıyordu; kod okunduğunda MCP tool'larının `ToolRegistry`'den
   HİÇ geçmediği, kendi ayrı onay-sarmalama kopyasına sahip olduğu görüldü (Faz 6'dan
   beri var olan, kod yorumuyla gerekçelendirilmiş bir desen). Aynı üç katman
   (Authorizing → Timeout → ApprovalRequired) `McpTenantTools.cs`'e de elle taşındı;
   `AuthorizingAIFunction`/`TimeoutAIFunction` bu yüzden `internal` değil **public**
   (paketler arası `InternalsVisibleTo` yerine mevcut kopyalama deseni izlendi — K-487).
   Bu, planın "hata modları" tablosundaki `McpToolDescriptorTests` satırının neden
   var olduğunu da açıklıyor: MCP tool'unun effect'i planın öngördüğünden daha
   fazla kod gerektirdi.

2. **`ApprovalRequiredAIFunction`'ı ek katmanlarla sarmak MAF'ın onay kısa devresini
   KIRABİLİRDİ — ölçülerek kontrol edildi (K-490).** Plan bu riski hiç tartışmıyordu;
   `ApprovalRequiredAIFunction.InvokeCoreAsync`'in doğrudan çağrıldığında defer
   ETMEDİĞİ (gerçek gövdeyi çalıştırdığı) bir birim testiyle ölçüldü. Kısa devrenin
   `AITool.GetService<T>()` pipeline'ı üzerinden çalıştığı bulundu; `AuthorizingAIFunction`/
   `TimeoutAIFunction` bu yüzden `GetService`'i EZMİYOR (miras alınan `DelegatingAIFunction`
   davranışına bilerek güveniliyor) — bu satır kodda YOKSA sarmalama sessizce onayı
   bozardı. `ToolRegistryWrapperOrderTests.The_approval_wrapper_stays_discoverable_through_the_outer_layers`
   bunu korur.

3. **`AgentPrismToolAttribute.TimeoutSeconds` `int`, planın taslak `ToolDescriptor.Timeout`
   (`TimeSpan?`) imzasından farklı tip taşıyor.** Bir attribute parametresi
   derleme-zamanı sabiti olmalı ve `TimeSpan` olamaz; `TimeoutSeconds` (0 = "belirtilmedi,
   varsayılanı kullan") seçildi, `ToolRegistry`/`ToolMethodScanner` bunu `TimeSpan.FromSeconds(n)`'e
   çeviriyor. Bağımsız denetimin 🟡 bulgusu — kod doğru, plan bu ayrımı yazmamıştı.

4. **Gerçek `samples/AgentPrism.Api` çalıştırması ve `docs-site` ekran görüntüsü
   yapılamadı.** Bu oturumun sandbox'ında model sağlayıcı API anahtarı ve dış ağ
   erişimi yok. Yerine: `ToolGovernanceEndpointTests` (gerçek `FunctionInvokingChatClient`
   boru hattı, `FakeModelProvider` ile) ve üç SQL sağlayıcısının gerçek veritabanlarına
   (Docker Postgres/SQL Server, gerçek SQLite dosyası) karşı koşan bütünleşme testleri
   kanıt taşıyor. Sonraki oturumun `samples/AgentPrism.Api`'yi gerçek bir anahtarla
   çalıştırıp DoD'nin ilgili satırını ve `docs-site` ekran görüntüsünü
   (`AGENTPRISM_UI_SCREENSHOTS=1`) tamamlaması gerekiyor.

5. **Alan hafızası dosyaları (`docs/hafiza/maf-api.md`, `cekirdek-calistirma.md`)
   bütçeyi aştı; ikiye bölme yerine yalnız TRIM edildi.** `scripts/dokuman-bakim.py`
   ikisini de "AŞTI — ikiye böl" diye işaretledi; bu fazın yeni notları (K-487/K-488/K-490)
   en özet haline indirilerek her ikisi de bütçenin altına çekildi (16150/16000,
   15950/16000). Gerçek bir bölme (konuya göre ikinci bir dosya) yapılmadı — dosyaların
   ÖNCEDEN de bütçeye yakın olduğu ölçüldü, bu yüzden köklü bir bölme ayrı bir kapsam
   kararı gerektirir. Sonraki fazın bu iki dosyaya dokunması gerekirse önce bölünmeli.

## Bu Fazda Verilen Kararlar

K-487, K-488, K-489, K-490, K-491 — bkz. [`docs/KARARLAR.md`](../../KARARLAR.md), bölüm 2
(K-486'nın hemen altı). Özet:

- **K-487** — sarmalama sırası (Authorizing → Timeout → ApprovalRequired → gerçek
  fonksiyon) ve MCP yolunda aynı mantığın tekrarlanması.
- **K-488** — yetki reddi istisna fırlatmaz, normal sonuç döner; kanca hatası fail-closed.
- **K-489** — `RunErrorClass.ToolTimeout` (13), `tool_timeout` stabil kimliği.
- **K-490** — `ApprovalRequiredAIFunction`'ın kısa devresi `GetService<T>()` üzerinden
  çalışır (ölçüldü); yeni sarmalayıcılar bu metodu ezmemeli.
- **K-491** — varsayılan tool timeout'u 30 sn, ölçülmedi, ilk gerçek koşumdan sonra
  gözden geçirilecek (Açık Soru 5'in kararı).

## Denetim Bulguları

`faz-denetim` (taze bağlamlı bağımsız denetçi, ayrı bir worktree'de) iki 🔴 ve üç 🟡
bulgu üretti:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | DoD'nin "gerçek `run`" gerektiren maddeleri yalnız `AuthorizingAIFunction`/`TimeoutAIFunction`'ı doğrudan kurup `InvokeAsync` çağıran birim testleriyle "kanıtlanmıştı" — K-218'in tam olarak uyardığı hata | **Düzeltildi.** `ToolGovernanceEndpointTests.cs` (4 test) gerçek `FunctionInvokingChatClient` boru hattından (`FakeModelProvider` + `ModelProviderRegistry`'nin `UseFunctionInvocation()` sarması) geçen `run`'larla ret/timeout/devre-kesici/onay+timeout senaryolarını kanıtlıyor |
| 2 | 🔴 | `RunErrorClass.ToolTimeout` arayüz sözleşmesine (`types.ts`, `en.ts`, `tr.ts`) yansıtılmamıştı — üretimde gerçekleşince gösterge panelinde sessizce boş satır üretirdi | **Düzeltildi.** `RunErrorClass` union'ına `'ToolTimeout'` eklendi, iki dilde `dashboard.errorClass.ToolTimeout` çevirisi yazıldı |
| 3 | 🟡 | `GeneratorSnapshotTests` (Effect/RequiredPermission/Timeout üreteçten geçiyor mu) hiç yazılmamıştı | **Düzeltildi.** `GeneratedOutputTests`'e iki test eklendi |
| 4 | 🟡 | `CircuitBreakerCountingTests` (timeout devre kesiciyi artırmaz) hiç yazılmamıştı | **Düzeltildi.** `ToolGovernanceEndpointTests.Repeated_tool_timeouts_never_open_the_providers_circuit_breaker` |
| 5 | 🟡 | `AgentPrismToolAttribute.TimeoutSeconds` (`int`) planın taslak `TimeSpan?` imzasından farklı | **Dokümante edildi**, bkz. Plandan Sapmalar #3 — bulgu değil, gerekçeli bir tasarım zorunluluğu (attribute parametresi derleme-zamanı sabiti olmalı) |

Düzeltmelerden sonra dört doğrulama kapısı yeniden koşuldu (build/test/pack/format,
arayüz dahil) — hepsi temiz. `AgentPrism.SqlServer.IntegrationTests`'te tüm paket
takımı PARALEL koşulduğunda görülen bir `deadlock` (hata 1205, migration `0017`)
**bu fazla ilgisizdir** — Faz 63'ün KARARLAR.md'de zaten kayıtlı, tekrarlanan bir
test-altyapısı sıkışmasıdır (izole koşumda 557/557 temiz).

## Sonraki Faza Devir Notu

1. **`samples/AgentPrism.Api` gerçek anahtarla koşulmadı.** `get_slow_report` demo
   tool'u (1 sn timeout, 5 sn uyuyan, token almayan gövde) ve `cancel_order`'ın
   `Effect`/`RequiredPermission` alanları hazır — bir sonraki oturum gerçek bir
   OpenAI anahtarıyla `dotnet run` edip DoD'nin ilgili satırını gerçek çıktıyla
   doldurabilir.

2. **`docs-site` ekran görüntüsü güncellenmedi.** `screenshots/tools.png` hâlâ
   effect/permission/timeout rozetlerinden ÖNCEKİ hali gösteriyor.
   `AGENTPRISM_UI_SCREENSHOTS=1` ile E2E koşumu bunu üretir.

3. **`docs/hafiza/maf-api.md` ve `cekirdek-calistirma.md` bütçenin sınırında**
   (15950/16000, 15991/16000 — bkz. Plandan Sapmalar #5). Bu iki dosyaya dokunan
   bir sonraki faz muhtemelen bütçeyi tekrar aşacak; gerçek bir bölme (konuya göre
   ikinci dosya) o zaman ele alınmalı, tekrar tekrar TRIM edilmemeli.

4. **MCP tool sarmalaması ile kod-tanımlı tool sarmalaması artık İKİ AYRI kod
   yolu** (`ToolRegistry.cs`, `McpTenantTools.cs`) — aynı mantığı taşıyorlar ama
   fiziksel olarak ayrılar. Üçüncü bir tool kaynağı (örn. bir eklenti sistemi)
   eklenirse aynı üç katman ORAYA da elle taşınmalı; K-487'ye bak.

5. **`AgentPrismOptions.Tools.DefaultTimeout` (30 sn) ölçülmedi** (K-491). İlk
   gerçek üretim trafiğinden sonra gözden geçirilmesi öneriliyor.
