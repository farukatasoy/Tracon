# Faz 142 — Onay İsteğinin Sunumu

> **Durum:** ✅ Tamamlandı (2026-09-04)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-188** (tüketici turu 3, A3)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Gerekli olabilir — sunum alanları kalıcı mı, Açık Soru 1. Numara uygulama anında alınır
> **Public API:** Büyüyor — yeni kontrat + `PendingApproval`'a alanlar
> **Tüketici yüzeyi:** `docs-site/`: `concepts/tools.md` (onay bölümü), `concepts/governance.md`, `guides/embedding.md` · sevk edilen: XML `<example>`, konsol onay ekranı + ekran görüntüsü
> **Manuel test alanı:** [`docs/manuel-test/07-HTTP-YONETIM-API.md`](../../manuel-test/07-HTTP-YONETIM-API.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8c3e6140:docs/arsiv/fazlar/142-ONAY-ISTEGININ-SUNUMU.md
> ```
>
> Damıtıldı 2026-09-04 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir onay diyaloğu bugün şuna dönüşüyor: *"`delete_skill` tool'unu `{ "skillId": "8f14e45f-…" }` argümanıyla çalıştırmak istiyor. Onaylıyor musunuz?"* Bu, kullanıcının bilinçli bir karar vermesini sağlamaz. Aynı sorun **AgentPrism'in kendi konsolunda da** vardır.

## Bitiş Ölçütleri (DoD)

- [x] Çözümleyici kayıtlı değilken **hiçbir** davranış değişmez —
  `ToolApprovalPresenterTests.Unregistered_presenter_resolves_nothing_and_is_never_called`
  (spy asla çağrılmaz) + `ToolApprovalPresenterRunner`'ın kendi hızlı yolu
  (`presenter is NullToolApprovalPresenter` → hiç `ToolApprovalContext`
  kurulmaz).
- [x] 🚨 `throw` eden çözümleyici onay isteğini **engellemez** —
  `ToolApprovalPresenterTests.Throwing_presenter_does_not_block_the_approval_request`.
- [x] Zaman aşımı çalışır; istek yine yayımlanır —
  `ToolApprovalPresenterTests.Presenter_past_the_configured_timeout_resolves_null_without_blocking`
  (20 ms yapılandırılmış zaman aşımı, sonsuz bekleyen sahte çözümleyiciye
  karşı 5 saniyeden kısa sürede döner).
- [x] 🚨 Scoped bağımlılık gerçek DI konteynerinde çözülür (K-218 kapanır) —
  `ToolApprovalPresenterScopeTests.Presenter_resolves_a_scoped_dependency_through_the_real_container`,
  gerçek `ServiceCollection.BuildServiceProvider()` + `IServiceScopeFactory`.
- [x] Ham argümanlar her zaman erişilebilir kalır — `PendingApprovalStoreContract`
  ve gerçek örnek uygulama koşumunda (aşağıda) her durumda `arguments` dolu.
- [x] Alanlar `pending` ucunda **ve** `RunAwaitingInput` payload'ında görünür —
  gerçek koşum kanıtı aşağıda.
- [x] Bundle payı ölçüldü; `en.ts`/`tr.ts` eksiksiz — `npm run build`:
  `javascript : 177.3 KB gzipped (budget 250 KB)` (Faz 141'den beri değişim
  yok denecek kadar küçük; bu fazın kendi JS eklentisi `<1 KB`).
- [x] Dört doğrulama kapısı sıfır uyarı verir — aşağıdaki "Doğrulama Kapıları
  Çıktısı" bölümü.
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
  — aşağıda.
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`'nın
  `olası secret` bölümü boş (yalnız migration-manifest sıralama boşluğu
  kaldı, bkz. Sonraki Faza Devir Notu).
- [x] Manuel kabul case'leri eklendi — **`07-HTTP-YONETIM-API.md` DEĞİL**,
  bkz. Plandan Sapmalar #2: `21-DAYANIKLILIK-VE-IPTAL.md` (MT-RES-020
  genişletildi, MT-RES-073 yeni) ve `10-ARAYUZ-AGENT-PLAYGROUND.md`
  (MT-UIAG-028 düzeltildi, MT-UIAG-053 yeni).
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları.
- [x] `docs-site/` güncellendi + onay ekranı görüntüsü yenilendi; `npm run build` +
  `check-links.mjs` temiz — `npm run check` (check:content · build ·
  check:links · check:weight) dördü de yeşil;
  `docs-site/public/screenshots/approvals.png`
  `AGENTPRISM_UI_SCREENSHOTS=1` ile yenilendi (gerçek bir `IToolApprovalPresenter`
  kayıtlı E2E host'ta, artık boş liste değil `Order ORD-7` satırı gösteriyor).

### Gerçek koşum kanıtı (`samples/AgentPrism.Api`, gerçek OpenAI sağlayıcısı)

Kuyruklu yol — `Prefer: respond-async` ile `ORD-1001` iptali:

```
POST /api/agents/support/run  (Prefer: respond-async, sessionId=faz142-demo)
→ 202 {"runId":"01a06b96-062d-7af2-a9c8-b0c1a7539332", …}

GET /api/runs/{runId}          → status: "AwaitingApproval" (ilk sorguda)

GET /api/approvals/pending     → [{
  "toolName": "cancel_order",
  "arguments": "orderId=ORD-1001",
  "presentation": {
    "entityType": "order", "entityId": "ORD-1001",
    "entityName": "Order ORD-1001",
    "message": "Cancel order ORD-1001 for Priya Shah."
  },
  "status": "Pending", …
}]

GET /api/runs/{runId}/events   → son olay:
  event: RunAwaitingInput
  payload: [{"requestId":"ficc_call_…","toolName":"cancel_order",
    "entityType":"order","entityId":"ORD-1001",
    "entityName":"Order ORD-1001",
    "message":"Cancel order ORD-1001 for Priya Shah."}]

POST /api/approvals/{id}/decide {"approved":true}
→ 200 {"status":"Approved", "presentation": {…SAME…}, …}
```

Senkron yol (playground stili) — `ORD-1002`, `event: approvals` çerçevesi:

```
POST /api/agents/support/run  (Prefer YOK — SSE akışı)
…
id: 13
event: update
data: {"contents":[{"$type":"toolApprovalRequest","toolCall":{"$type":"functionCall",
  "name":"cancel_order","arguments":{"orderId":"ORD-1002"}, …},
  "requestId":"ficc_call_ejhBzi5SUOy6MxANJrmIl9U6"}]}

id: 14
event: approvals
data: [{"requestId":"ficc_call_ejhBzi5SUOy6MxANJrmIl9U6","toolName":"cancel_order",
  "entityType":"order","entityId":"ORD-1002","entityName":"Order ORD-1002",
  "message":"Cancel order ORD-1002 for Marcus Lee."}]

id: 15
event: done
```

Fail-open — bilinmeyen `ORD-9999`:

```
GET /api/approvals/pending → [{"toolName":"cancel_order",
  "arguments":"orderId=ORD-9999", "presentation": null, "status":"Pending", …}]
```

İstek yine yayımlandı, ham argüman dolu, `presentation` yalnız `null` —
tam olarak 142.2'nin dört-durum tablosunun vaat ettiği gibi.

### Doğrulama Kapıları Çıktısı

- `dotnet build AgentPrism.slnx -c Release` — 0 uyarı, 0 hata (temiz `artifacts/`
  ile üç kez doğrulandı; ilk iki koşumun kırmızıları bu oturumun kendi eşzamanlı
  ad-hoc `dotnet pack`/`dotnet build` çağrılarının `artifacts/package/`'ı
  kirletmesinden kaynaklanıyordu — kod kusuru değildi, `rm -rf artifacts` + tek
  seferlik temiz koşumla doğrulandı).
- `dotnet test AgentPrism.slnx --no-build` — Core (2341), AspNetCore.FunctionalTests
  (766), Workflows.UnitTests (107), Sql.Shared.UnitTests (20),
  PostgreSql/SqlServer/Sqlite.IntegrationTests (gerçek Testcontainers ile),
  Generators.UnitTests (271, `<example>` blokları dahil), Ui.E2ETests
  (ekran görüntüsü koşumu dahil) — tümü yeşil.
- `python3 scripts/kapi.py tarama` — senkronizasyon kopyası: temiz; `secret`:
  temiz; bayat doküman referansı: temiz; migration-integrity: kırmızı (BEKLENEN,
  bkz. Sonraki Faza Devir Notu — kapanış commit'inden sonra ayrı bir commit'le
  kapanır).
- `docs-site`: `npm run check` (check:content · build · check:links ·
  check:weight) dördü de yeşil.

---

## Plandan Sapmalar

1. **🚨 `ApprovingAIFunction` hiç yazılmadı — planın merkezi varsayımı ölçümde
   yanlış çıktı.** `ToolRegistryWrapperOrderTests.cs`'te ZATEN ölçülmüş bir
   gerçek: MEAI'nin function-invoking istemcisi `ApprovalRequiredAIFunction`'ı
   `AITool.GetService(Type)` üzerinden BULARAK ayırır, `InvokeAsync`'i hiç
   çağırmadan — bir tool sarmalayıcısının onay tetikleyen çağrıda `InvokeAsync`'i
   hiç görmeyeceği anlamına gelir. Sunum çözümü bunun yerine
   `RunRecordingAgent`'a taşındı: `ToolApprovalRequestContent`'in ZATEN okunduğu
   tek nokta (`ChildRunApproval` ailesi). Açık Soru 2'nin cevabı NE
   `ApprovingAIFunction` NE uçtu — `RunRecordingAgent` oldu. Vaka
   `docs/hafiza/tool-onay-ve-yetkilendirme.md`'ye yazıldı.
2. **Manuel kabul case'leri `07-HTTP-YONETIM-API.md`'ye DEĞİL, mevcut yerleşik
   dosyalara eklendi.** `/api/approvals/*` uçlarının kuyruk senaryosu zaten
   `21-DAYANIKLILIK-VE-IPTAL.md` §3'te (`MT-RES-020`+) yaşıyordu — presentation
   alanları oraya (MT-RES-020 genişletildi, MT-RES-073 eklendi) ve canlı
   playground SSE senaryosu `10-ARAYUZ-AGENT-PLAYGROUND.md`'ye (MT-UIAG-028
   düzeltildi, MT-UIAG-053 eklendi) gitti — kurulu yerleşim korunuyor.
3. **Açık Soru 1 → A: kalıcı.** `pending_approvals.presentation` jsonb/text
   sütunu (3 migration: Postgres 0045, SqlServer/Sqlite 0032). Gerekçe planın
   önerdiği gibi: onay kuyruktan sürdürülüyor ve çözümleyici o an başka bir
   süreçte/pencerede olabilir; yeniden çözmek tutarsız bir "aynı istek farklı
   anda farklı ad" riski taşırdı.
4. **Açık Soru 3 → 2 saniye, `AgentPrismToolOptions.ApprovalPresentationTimeout`
   ile ayarlanabilir.** Gerekçe: çözümleyici tek bir hızlı, salt okunur arama
   (birincil anahtarla nokta okuma) yapmalı — gerçek iş değil; kısa varsayılan
   yalnız bir isim kaybettirir, onayın kendisini asla geciktirmez.
5. **🚨 `AgentPrismRunOptions.Clone()` sessizce `BeforePendingApprovalIsPublished`'ı
   düşürüyordu — fazın kapsamı dışında, K-103 döneminden kalma bir kusur, bu
   fazda dokunulan aynı satırda bulunup düzeltildi.** Private copy ctor bu
   alanı hiç kopyalamıyordu; `Clone()`'un kendi XML dokümanı "tüm alanları
   korur" diyordu, kod tutmuyordu. Bağımsız denetimin 🟡 #3 bulgusu.
6. **🚨 `RunEventWriter.CompleteAsync`'in kapanış olayı switch'i
   `RunStatus.AwaitingApproval`'ı hiç eşlemiyordu, default kola düşüp HER
   onay-bekleyen kökü `RunFailed` + "The run was canceled." olarak
   yayımlıyordu.** Kusur `RunRecordingAgentOutcomeMatrixTests`'in kendi
   yorumunda bilerek pin'lenmişti ("gelecekte düzeltilecek quirk"); fazın
   kendi DoD'si (RunAwaitingInput payload'ı) bu düzeltmeyi zorunlu kıldı. Vaka
   `docs/hafiza/cekirdek-calistirma.md`'de.
7. **Embedding point sayısı altıdan yediye çıktı — bağımsız denetimin 🟡 #2
   bulgusuyla bulundu, plan bunu öngörmüyordu.** `IToolApprovalPresenter`
   diğer altı genişleme noktasıyla (TryAdd + fail-open, tüketici override
   kazanır) aynı ailede olduğu hâlde ne `GET /api/diagnostics`'e ne
   `capabilities.md`'ye eklenmişti; ikisi de düzeltildi
   (`AgentPrismDiagnosticsCollector`, `docs-site/.../capabilities.md`).
8. **`guides/embedding.md`'nin "six points" listesine 7. madde EKLENMEDİ —
   bilinçli kapsam kararı.** O liste embedding noktalarının GENEL ailesi
   değil, bir widget'ı BAŞKA bir uygulamaya gömmenin altyapı-yapıştırma
   sorunlarıdır (kiracı, atıf, yetkilendirme, event bridge, depolama, run/session
   yetkilendirmesi); onay sunumu farklı bir eksen. Listeyi "yedi nokta"ya
   çevirip başlığı değiştirmek yerine §3'e çapraz referans eklendi.

## Bu Fazda Verilen Kararlar

| Karar | Kayıt |
|---|---|
| Sunum alanları `pending_approvals`'a kalıcı sütun olarak yazılır (Açık Soru 1 → A) | K-675 |
| `IToolApprovalPresenter` fail-open'dır — kayıtlı değil/`null` döner/`throw` eder/zaman aşımına uğrar dört durumun HİÇBİRİ onay isteğinin yayımını engellemez | K-676 |

Tam gerekçe `docs/KARARLAR.md`'de (Bölüm 2, sona eklendi).

## Denetim Bulguları

Bağımsız denetim ([`faz-denetim`](../../../.agents/skills/faz-denetim/SKILL.md)) 🔴 bulgu üretmedi.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Kuyruk yolunun (`AgentRunJobHandler`) `IToolApprovalPresenter`'ı gerçekten `PendingApproval.Presentation`'a yazdığını kanıtlayan hızlı/izole bir test yoktu — yalnız E2E ekran görüntüsü testi. | **Düzeltildi**: `AgentRunJobHandlerTests.A_registered_presenter_reaches_the_persisted_PendingApproval_row` — gerçek `RunRecordingAgent` + `AgentRunJobHandler` + `InMemoryPendingApprovalStore` zincirini DI/HTTP olmadan koşar. |
| 2 | 🟡 | `IToolApprovalPresenter` diğer altı "TryAdd + fail-open" genişleme noktasıyla aynı ailede olduğu hâlde `capabilities.md`'ye ve `AgentPrismDiagnosticsCollector`'a (`GET /api/diagnostics`) eklenmemişti. | **Düzeltildi**: yedinci nokta olarak ikisine de eklendi; `DiagnosticsCollectorTests` ve `DiagnosticsEndpointTests`'in "altı"ya sabit sayıları "yedi"ye güncellendi. |
| 3 | 🟡 | `AgentPrismRunOptions.Clone()`'un `BeforePendingApprovalIsPublished`'ı kopyalamaması (K-103 döneminden kalma, bu fazda dokunulan satırda bulunan) hiçbir yerde açıkça kayıt altına alınmamıştı. | **Gerekçelendi/kayıt altına alındı**: Plandan Sapmalar #5. |
| 🟢 | 🟢 | `ChildRunApproval.Describe`/`CollectRequests` neredeyse aynı döngüyü iki kez yürütüyor. | **Devredildi**: ayrı bir temizlik fazına değecek kadar önemli değil; birleştirilmedi, kod tekrarı davranışı etkilemiyor. |
| 🟢 | 🟢 | `ToolApprovalPresenterRunner.ResolveAllAsync` N>1 bekleyen istekte sıralı çözer. | **Devredildi**: mantık N>1 için de doğru (paylaşılan mutable state yok); performans optimizasyonu, DoD veya güvenlik sınırı değil. |

Denetimin "temiz" bulduğu başlıklar: 3.2 (test tiyatrosu), 3.6 (plan dışı public API), 3.7 (repo kuralları).

## Sonraki Faza Devir Notu

- **`IToolApprovalPresenter` artık AgentPrism'in yedinci embedding noktasıdır.**
  Yeni bir genişleme noktası eklerken bu ikili kontrolü unutma:
  `AgentPrismDiagnosticsCollector.CollectExtensionPoints()` (kod) VE
  `docs-site/.../capabilities.md`'nin "Embedding points" tablosu (doküman) —
  ikisi de elle senkron tutulur, hiçbir kapı bu boşluğu otomatik yakalamaz
  (bağımsız denetimin bu fazda bulduğu tam boşluk buydu).
- **`AgentPrismRunOptions.BeforePendingApprovalIsPublished`'ın imzası
  ikinci bir parametre kazandı** (`IReadOnlyDictionary<string,
  ToolApprovalPresentation?>`). Bu hook'u okuyan/yazan başka bir yer varsa
  (bugün yalnız `AgentRunJobHandler` ve `AgentEndpoints.cs` var) imza-gövde
  taramasını (`grep -rn "BeforePendingApprovalIsPublished" src/`) çalıştır.
- **SSE `approvals` çerçevesi yalnız SENKRON streaming run ucundadır**
  (`AgentEndpoints.AgentRunStream.ExecuteStreamingAsync`), kuyruklu
  (`Prefer: respond-async`) yolda DEĞİL — kuyruklu yolun kendi mailbox'ı
  (`GET /api/approvals/pending`) zaten var. İkisini karıştırma; bağımsız
  denetim bu ayrımı görev bağlamındaki bir özetleme hatasında bulup düzeltti.
  bkz. `docs/hafiza/cekirdek-calistirma.md`.
- **`docs/manuel-test/`'in `docs/manuel-test/*.md` bütçesi %12 boşlukla DAR
  durumdaydı bu faz başlarken** (`dokuman-bakim.py --denetle`). Yeni case
  eklerken kısa yaz; bir sonraki faz bu ağacı taşırabilir — şimdi taşı,
  aşınca değil.
- **`scripts/applied-migrations.json`'a bu fazın 3 migration'ı için
  `sourceCommits` girdisi HENÜZ yazılmadı** — kapanış commit'inden SONRA,
  o commit'in SHA'sını referanslayan ayrı bir "docs: pin phase 142's
  migrations..." commit'iyle eklenir (bkz. phase 126/129/132/137/141
  emsali). Bu commit atılmadan `kapi.py tarama` migration-integrity
  kontrolünde kırmızı kalır — bu BEKLENEN bir sıralamadır, kusur değildir.
