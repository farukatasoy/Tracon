# Faz 140 — İçerik Guard'ının Kaynağı

> **Durum:** ✅ Tamamlandı (2026-09-04)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-186** (tüketici turu 3, A4)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `ContentGuardContext`'e iki `init` alan + yeni enum. Additive; `Unknown = 0` geriye dönük uyumludur
> **Tüketici yüzeyi:** `docs-site/`: `concepts/governance.md` (content guard bölümü), `guides/reliability.md` · sevk edilen: XML `<example>`
> **Manuel test alanı:** [`docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](../../manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 01c6e267:docs/arsiv/fazlar/140-ICERIK-GUARDININ-KAYNAGI.md
> ```
>
> Damıtıldı 2026-09-04 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Guard bugün denetlediği metnin bir **tool sonucu** mu, bir **kullanıcı mesajı** mı, yoksa **model çıktısı** mı olduğunu ayırt edemiyor. Üçü de aynı torbaya giriyor.

## Bitiş Ölçütleri (DoD)

- [x] Guard bir tool sonucunu kullanıcı mesajından ayırt eder (case 2 kanıt) —
      `ContentGuardSourceTests.Tool_result_is_classified_as_ToolResult_with_its_resolved_tool_name`,
      gerçek `FunctionInvokingChatClient` ikinci turuyla; ayrıca gerçek
      `samples/AgentPrism.Api` koşumuyla da doğrulandı (aşağıda)
- [x] `ToolName` çözülemediğinde `Source` yine `ToolResult` kalır —
      `ContentGuardSourceTests.Tool_result_keeps_ToolResult_source_even_when_the_call_id_cannot_be_resolved`
- [x] `Unknown` en sıkı kuralı alır; testle kilitlenir —
      `PatternContentGuardTests.Unknown_source_is_never_treated_as_a_reason_to_allow`,
      `Decision_is_identical_regardless_of_source`
- [x] Kaynak taşımayan mevcut guard'lar değişmeden çalışır — var olan 2304
      Core testinin tamamı (guard dahil) değişmeden yeşil; `PatternContentGuard`
      `Source`'u hiç okumuyor (kasıtlı, bkz. K-672)
- [x] Sıcak yolda ek tahsis ölçüldü ve yazıldı (Faz 116 kapısı) —
      `ContentGuardAllocationTests.Building_the_tool_name_map_only_allocates_when_a_tool_result_is_present`
      (göreli ölçüm: tool sonucu YOK iken tahsis, tool sonucu VARKEN tahsisten
      kesin olarak azdır; 5 ardışık koşumda kararlı). Faz 116'nın resmi
      `bench/baseline.json` üç sıcak yolunun hiçbirine bu davranış girmiyor —
      guard hattı o listede zaten yoktu, yeni bir kayıt açılmadı (kapsam dışı,
      bkz. Denetim Bulguları 🟢)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban c77b4d3c`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı —
      aşağıda
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`
      içine eklendi — MT-GUARD-077/078/079
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi; `npm run check` (content, build, links, weight)
      dördü de temiz

### Gerçek `samples/AgentPrism.Api` koşumu (2026-09-04)

Development ortamında (`ASPNETCORE_ENVIRONMENT=Development`, gerçek OpenAI
anahtarı `dotnet user-secrets`'ten) `support` agent'ına `get_order_status`
tool'unu tetikleyen bir mesaj gönderildi. Geçici bir
`Console.Error.WriteLine` probu `ContentGuardPipeline.InspectAsync` içine
(context kurulumundan hemen sonra) eklendi, koşum yapıldı, çıktı gözlemlendi,
sonra prob **kaldırıldı** (kalıcı kodda yok — bağımsız denetim bunu ayrıca
doğruladı).

```
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
  -d '{"message": "What is the status of order 12345?"}'
```

Gözlemlenen sıra (gerçek çıktı):
```
[PHASE140-PROBE] direction=Input source=UserMessage toolName=<null> textPrefix="What is the status of order 12345?"
[PHASE140-PROBE] direction=Input source=UserMessage toolName=<null> textPrefix="What is the status of order 12345?"
[PHASE140-PROBE] direction=Input source=ToolResult toolName=get_order_status textPrefix="Order 12345 has shipped. Estimated deliv"
[PHASE140-PROBE] direction=Output source=ModelOutput toolName=<null> textPrefix="Order 12345 has shipped and is estimated"
```

Üçü de fazın planladığı ayrımla birebir eşleşiyor: kullanıcı mesajı
`UserMessage`, ikinci model çağrısındaki tool sonucu `ToolResult` **ve**
`ToolName = "get_order_status"`, nihai model yanıtı `ModelOutput`.

---

## Plandan Sapmalar

- **`guides/reliability.md` güncellenmedi.** Plan bu dosyayı da tüketici
  yüzeyi olarak işaretlemişti. Dosya baştan sona okundu (`## Failure
  boundaries at a glance`, `## Troubleshooting` dahil); guard'ın kaynak
  sınıflaması bir yeniden deneme/hata sınıflandırma davranışı değiştirmiyor,
  hiçbir satırın konusuna girmiyor. Değişiklik yapılmadı.
- **`TenantIsolationContract` sözleşme testi yazılmadı.** Plan'ın Hata
  Modları tablosu "Başka kiracının tool adı sızar → TenantIsolationContract"
  diyordu. Ölçüldü: `ToolName` hiçbir depoya yazılmıyor, yalnız o anki
  çağrının kendi mesaj listesinden (zaten o kiracının run'ı) çözülüyor —
  kalıcı bir sızma yüzeyi yok, dolayısıyla store-contract seviyesinde test
  edilecek bir davranış yok. Sözleşme testi yerine `ContentGuardPipeline`'ın
  var olan `TenantId` akışı (fazdan bağımsız, değişmedi) korundu.
- **`ContentGuardAllocationTests` Faz 116'nın resmi `bench/` kapısına
  eklenmedi.** Guard hattı zaten o üç sıcak yolun (`CacheHit`, `AppendEvent`,
  `QueryRuns`) hiçbirinde değildi; DoD yalnız "ölçüldü ve yazıldı" istiyordu,
  resmi kapıya eklenmesini istemiyordu. Bağımsız denetim bunu 🟢 olarak
  işaretledi (aday, kusur değil).

## Bu Fazda Verilen Kararlar

- **K-672** — `ContentGuardContext.Source` içerik tipine (öncelik) ve mesaj
  rolüne göre sınıflanır, `Direction`'a hiç bakılmaz; `PatternContentGuard`
  `Source`'u kasıtlı okumaz. Tam gerekçe `docs/KARARLAR.md` / `arsiv/KARARLAR-GECMISI.md`.
- **Açık Soru 1 → A** (yalnız User/Tool/Model doldurulur; `Document` ve
  `SkillResource` enum'da boş kalır) — planın önerisi aynen uygulandı.
- **Açık Soru 2 → A** (`ToolName` `RunEvent.ToolName` ile aynı normalizasyonu
  alır) — ölçüldü: repo genelinde hiçbir kod yolu tool adına normalizasyon
  (trim/lower/truncate) uygulamıyor (`RunRecordingAgent.Persistence.cs:215`,
  `ToolApprovalRuleEvaluator.cs:183` dahil hepsi ham `FunctionCallContent.Name`
  kullanıyor); "aynı normalizasyon" burada "ham ad" anlamına geliyor ve
  `ContentGuardMessageMasker.BuildToolNameMap` da aynısını yapıyor.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, 2026-09-04) çalışma ağacına (`git diff HEAD`)
baktı. **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | MT-GUARD-078'in "Girilecek veri" kod bloğu case başlığının iddia ettiği iki senaryodan yalnız birini (kullanıcı yazdı → bloklanmadı) çalıştırıyordu; ikinci senaryo (tool döndü → bloklandı) "Beklenen sonuç"te düzyazıyla "şunu ekle" deniyordu | **Düzeltildi** — ikinci `RunAsync` çağrısı (`toolDondu`) doğrudan "Girilecek veri" kod bloğuna taşındı; case artık verildiği gibi çalıştırıldığında iki iddiayı da tek koşumda kanıtlıyor |
| 2 | 🟢 | `ContentGuardAllocationTests` Faz 116'nın resmi `kapi.py performans` kapısına kayıtlı değil, göreli (mutlak değil) bir birim testi | **Devredilmedi, kapsam dışı gerekçelendi** — DoD yalnız "ölçüldü ve yazıldı" istiyordu; resmi kapıya eklenmesi ayrı bir karar (guard hattı zaten üç izlenen sıcak yoldan biri değildi) |

**Temiz çıkan başlıklar:** 3.1 (DoD), 3.2 (test tiyatrosu yok), 3.3 (test seviyesi),
3.4 (hata yolları), 3.5 (imza-gövde), 3.6 (public API), 3.7 (repo kuralları),
3.8 (ürün yüzeyi — probun kaldırıldığı ayrıca doğrulandı).

## Sonraki Faza Devir Notu

- **`ContentGuardContext.Source`/`ToolName` artık her guard çağrısında dolu.**
  Yeni bir `IContentGuard` implementasyonu yazan bir faz bu alanları
  `context.Source`/`context.ToolName` ile okuyabilir; `PatternContentGuard`'ın
  aksine kaynağa göre farklı davranmak **istenen** bir kullanım — sözleşme
  bunu engellemiyor, yalnız yerleşik guard'ın kendisi bunu yapmıyor.
- **`Document` ve `SkillResource` kaynakları enum'da var ama hiçbir kod yolu
  onları üretmiyor.** Doküman kanalı (`documents` alanı, Faz 86) ve skill
  kaynağı guard'a bugün `UserMessage`/`Unknown` gibi geçiyor olabilir —
  ölçülmedi. Bu kaynakları gerçekten dolduracak bir faz önce `ContentGuardMessageMasker`'ın
  hangi mesaj/`AIContent` şeklinin doküman/skill içeriğini taşıdığını
  `grep -rn "documents" src/AgentPrism.Core/` ile ölçmeli — tahmin etmemeli
  (K-320 sınıfı bir hata riski taşır).
  Devraldığı sözleşme: `ContentGuardMessageMasker.RewriteContentAsync`'in
  `ChatRole? role` parametresi zaten var; yeni bir kaynak eklemek
  `ClassifySource`'a bir dal eklemek kadar ucuzdur.
- **`ContentGuardPipeline.InspectAsync`/`PreviewAsync` artık 6 parametre
  alıyor.** Üçüncü bir kaynak-bağımlı alan eklenirse (örn. bir güven skoru)
  aynı imza yeniden büyür; `PublicAPI.Shipped.txt` hâlâ boşsa (yayın öncesi)
  bu ucuzdur, yayından sonra bir `ContentGuardInspectionRequest` gibi bir
  parametre nesnesine geçiş değerlendirilmelidir.
