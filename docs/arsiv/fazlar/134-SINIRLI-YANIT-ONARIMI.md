# Faz 134 — Sınırlı Yapısal Yanıt Onarımı

> **Durum:** ✅ Tamamlandı (2026-09-02)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) — **F-177**
> **Önkoşul:** [Faz 131](131-YAPISAL-YANIT-DOGRULAMA-SEAMI.md) — onarım, doğrulama seam'i olmadan tanımsızdır
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.UI` (olay etiketi)
> **Yeni paket:** Yok · **Migration:** Yok — yeni `RunEventType` değeri mevcut `smallint` sütuna yazılır
> **Public API:** büyüyor — bir options alanı, bir `RunEventType` değeri. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş **sıfır**
> **Tüketici yüzeyi:** `docs-site/`: `guides/structured-output.md`, `concepts/runs.md` (olay listesi), `reference/configuration.md`, `capabilities.md` · sevk edilen: `AgentPrismStructuredResponseOptions` XML dokümanı, `en.ts`/`tr.ts` olay etiketi
> **Manuel test alanı:** [`docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](../../manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md) — 🚨 Faz 133'ün **133.0** kalibrasyonu uygulanmamışsa case yazılamaz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 854d4e4:docs/arsiv/fazlar/134-SINIRLI-YANIT-ONARIMI.md
> ```
>
> Damıtıldı 2026-09-02 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 131 geçersiz yapısal yanıtı **yakalıyor** ama tek yapabildiği `run`'ı başarısız kapatmak. Modelin ikinci bir denemede doğru JSON üretmesi çok olağandır; bugün bu ikinci denemeyi yapmak isteyen her tüketici onu kendi kodunda yazar. O zaman onarımın token'ı ve maliyeti ana `run` kanıtından kopar, ve `run` ağacı bütçesi onu hiç görmez.

## Bitiş Ölçütleri (DoD)

- [x] `MaxRepairAttempts` verilmemişken davranış Faz 131 ile **birebir** aynıdır; ek model çağrısı **yok** (case 1) — `MaxRepairAttempts_unset_defaults_to_zero_and_behaves_exactly_like_no_repair`, `MaxRepairAttempts_zero_behaves_exactly_like_no_repair_a_single_call_that_throws`. Tek istisna, kendisi de ölçülüp kilitlenmiş: `run.Usage` artık null değil, reddedilen tek denemenin token'ını taşır (bkz. Plandan Sapmalar) — ek model çağrısı yapılmadığı iddiasını değiştirmez
- [x] Bir onarım turu geçersiz yanıtı kurtarır ve `run` `Completed` kapanır (case 2) — `A_repair_turn_recovers_an_invalid_response_and_the_runs_usage_is_the_sum_of_both_turns`, `A_repair_turn_recovers_an_invalid_first_response`
- [x] `MaxRepairAttempts: 2` → toplam **üç** model çağrısı, ne bir eksik ne bir fazla (case 3) — `Repair_attempts_are_capped_then_the_run_fails_exactly_like_an_unrepaired_rejection`, `Repair_attempts_are_capped_at_MaxRepairAttempts_then_the_run_still_fails`
- [x] `run.Usage` bütün turların **toplamıdır**; hiçbir turun token'ı ne kaybolur ne iki kez sayılır (case 4) — `A_repair_turn_recovers_an_invalid_response_and_the_runs_usage_is_the_sum_of_both_turns` (28 input/7 output, iki turun düz toplamı), `A_discarded_attempts_usage_folds_into_the_side_channel_and_the_returned_attempts_does_not`
- [x] Bütçe, `Deadline` ve iptal onarım turunda da uygulanır (case 5) — `The_trees_token_budget_still_applies_to_a_repair_turn_and_stops_it_from_looping`, `The_trees_deadline_still_applies_to_a_repair_turn_and_stops_it_from_looping` (denetim bulgusu üzerine eklendi), `Cancellation_during_a_repair_turn_cancels_the_run_not_a_validation_failure`
- [x] Akışlı yolda onarım açılmaz (case 6) — `Streaming_never_repairs_even_when_MaxRepairAttempts_is_positive`
- [x] Onarım mesajları oturuma yazılmaz — `Repair_messages_are_not_written_to_the_callers_own_session` (repair turu `session: null` ile çağrılır; ORİJİNAL turun kendi reddedilen yanıtı yine de oturuma yazılır, bu Faz 131'in değişmeyen davranışıdır — bkz. Plandan Sapmalar)
- [x] Onarım hakkı tükenince `run` `StructuredResponseInvalid` ile biter — **yeni hata sınıfı eklenmedi** — `Repair_attempts_are_capped_then_the_run_fails_exactly_like_an_unrepaired_rejection`; `RunErrorClass.cs` diff'i yeni üye eklemediğini gösterir
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban f510c94` tamamı ✅ (tarama, doküman denetimi, script birim testleri, agent-map, denetim-paketi, `dotnet build`, `dotnet test` (735+2282+… tüm projeler, 0 başarısız), `dotnet pack`, `dotnet format --verify-no-changes`, `docs-site` `npm run check`)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` §11 başlığındaki "Gerçek sonuç" bloğu (2026-09-02, `order-summary` agent'ı, `MaxRepairAttempts: 2` açıkken geçerli-ilk-denemeli gerçek bir `gpt-5.4-mini` çağrısı — sıfır sürpriz kanıtı)
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-GUARD-100..106, hepsinin otomatik karşılığı koşuldu (106 hariç, 👤 gerekir)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 5 🟡 bulgu, tamamı kapandı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi (`structured-output.md`, `reference/configuration.md`, `capabilities.md`); `npm run build` + `check-links.mjs` temiz — `concepts/runs.md`'ye **bilerek** dokunulmadı (bkz. Plandan Sapmalar); `npm run check` (`check:content` + `build` + `check:links` + `check:weight`) tamamı ✅
- [x] `en.ts` ve `tr.ts` eksiksiz — plandan sapma: bu olay etiketi de Faz 131'deki gibi sözlükten **gelmiyor** (bkz. Plandan Sapmalar); `run-event.ts`/`run-detail.tsx` güncellendi, `tsc --noEmit` ve `vitest run` (222/222) temiz
- [x] 🚨 `dotnet test AgentPrism.slnx` TAM log dosyasından teyit edildi — `| tail` ile **değil** (Faz 130 devir notu) — `grep -n "failed \|Failed:" <tam-log> | grep -v "Failed: 0"` boş döndü, `EXIT:0`

### Doğrulama komutları

```bash
# Onarım olayları
curl -s http://localhost:5081/agentprism/api/runs/<id>/events \
  | jq '[.[] | select(.type | startswith("StructuredResponse"))]'

# Token toplamı iki turu da içeriyor mu
curl -s http://localhost:5081/agentprism/api/runs/<id> | jq '.usage'
```

---

## Plandan Sapmalar

1. **`run.Usage` artık `MaxRepairAttempts = 0` iken bile reddedilen tek
   denemenin token'ını taşır — Faz 131'de `null` idi.** 134.1'in yan kanal
   kuralı ("döndürülmeyen her `AgentResponse`'ın kullanımı `ExtraUsage`'a
   eklenir") kod düzeyinde hem "onarım devam ediyor" hem "onarım hakkı
   tükendi, hemen düşecek" dallarına aynı şekilde uygulanıyor — ayrım
   yapılmadı, çünkü ayrım yapmak (yalnız tekrar denenecekse ekle) case 3'ün
   ("hiçbir turun token'ı kaybolmaz") gerektirdiği davranışı bozardı.
   Sonuç: bu kural `MaxRepairAttempts = 0`'da da çalışır ve Faz 131'in
   kendisinde saklı bir eksiği (gerçekten harcanan token'ın `run.Usage`'da
   sessizce kaybolması) kapatır. Bağımsız denetim bunu buldu; davranış
   **düzeltilmedi** (doğru olduğuna karar verildi), bunun yerine
   `MaxRepairAttempts_unset_defaults_to_zero_and_behaves_exactly_like_no_repair`
   testi bu satırı da doğrulayacak şekilde genişletildi. DoD'nin "birebir
   aynı" iddiası yalnızca **ek model çağrısı yapılmaması** için geçerlidir;
   `run.Usage`'ın doğruluğu için değil.
2. **`concepts/runs.md`'ye dokunulmadı.** Planın dosya listesi bunu
   istiyordu ama sayfa hiçbir zaman `RunEventType`'ın tam listesini
   tutmuyor — basitleştirilmiş Mermaid diyagramı bile `ModelFallbackUsed`,
   `ContentMasked`, `StructuredResponseRejected` gibi tekil-amaçlı olayları
   hiç içermiyor (aynı emsal Faz 62/48/131'de de uygulandı). Tam liste zaten
   otomatik üretilen `docs-site/src/content/docs/api/AgentPrism.RunEventType.md`
   sayfasındadır (`npm run generate`, XML dokümanından). `guides/structured-output.md`'ye
   yeni bir "Let the model repair a rejected response" bölümü eklenmesi
   yeterli görüldü.
3. **`en.ts`/`tr.ts` sözlüğüne yeni bir anahtar eklenmedi.** Plan bunu
   istiyordu ama `StructuredResponseRejected`'ın kendisi de (Faz 131)
   sözlükten gelmiyor: `EVENT_STYLE` haritasındaki `label` alanı TSX'te
   sabit, teknik bir dizgidir (`structured-response.repair-attempted`),
   `t(...)` ile çağrılmaz ve iki dilde de aynı görünür — bu bilinçli bir
   tasarımdır (MT-GUARD-095'in kendi notu). Yeni olay etiketi aynı deseni
   izler; `run-event.ts` (birlik tipi) ve `run-detail.tsx` (`EVENT_STYLE`
   haritası) güncellendi, sözlük dosyaları değişmedi.
4. **OpenAPI belgesi ve üretilen TypeScript istemcisi yeniden üretilmedi.**
   Plan "OpenAPI → TypeScript zinciri yeniden üretilir" diyordu ama
   `RunEventType` hiçbir JSON şemasına girmez — olay akışı `text/event-stream`
   üzerinden hand-written `run-event.ts` tipiyle okunur
   (`RunEventType.cs`'in kendi dokümanının söylediği gibi, `RunErrorClass`'ın
   aksine — o, `RunError.Class` JSON alanı olduğu için OpenAPI'ye
   girer ve zaten hiç değişmedi). `docs/openapi/agentprism.json`'da
   `grep -c StructuredResponseRejected` **sıfır** döner; ölçüldü, dokunulmadı.
5. **Bağımsız denetimin bulduğu, konuyla alakasız bir test-izolasyon kusuru
   düzeltildi.** `tests/AgentPrism.AspNetCore.FunctionalTests/JobMetricEndToEndTests.cs`
   process-wide bir `MeterListener` kullanıyordu ve "bu isimli İLK ölçüm
   benimdir" varsayıyordu; bu fazın eklediği kuyruklu-run cancel testi
   eşzamanlı bir "default" lane'li `agentprism.job.executions` ölçümü
   yayınlayınca test 3/3 tekrarda kırıldı, taban commit'te (worktree ile
   izole edildi) 1/1 yeşildi. Kök sebep düzeltildi:
   `JobMetricCollector.WaitForAsync` artık isteğe bağlı bir etiket süzgeci
   alıyor, test kendi "media" lane'ini süzüyor. Aynı desenin
   `RunCostMetricEndToEndTests.cs`'de hâlâ (henüz tetiklenmemiş, gizil) var
   olduğu bulundu ve `docs/ADAYLAR.md`'ye F-181 olarak kaydedildi — bu fazın
   nedensel kapsamı dışında olduğu için burada düzeltilmedi.
6. **`docs-site/src/content/docs/ui.md`'ye dokunulmadı** (site-denetle'nin
   `arayuz` kuralı `run-detail.tsx`'in değiştiğini görüp tetiklendi).
   Sayfanın "Runs" bölümü zaten "the full event stream in order: message
   deltas, tool calls with arguments and results, errors with their class"
   diye genel geçer — `ModelFallbackUsed`, `ContentMasked`,
   `StructuredResponseRejected` gibi hiçbir tekil olay türü burada tek tek
   anılmıyor (aynı emsal); yeni `StructuredResponseRepairAttempted` satırı da
   bu genel cümlenin kapsamındadır.

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-654 — Sınırlı yapısal yanıt onarımı aynı `run` içinde kalır; yeni bir `RunKind`/child run açılmaz (Faz 134) (kullanıcı kararı)** | Kullanıcı 2026-09-02'de seçti. `runs` tablosu büyümez, migration gerekmez, `AgentRunBudget` zaten aynı `run`'ı sayıyor. Bedeli: onarımın maliyeti ana `run`'ın toplamı içinde erir, ayrı bir sütundan okunamaz — yalnız olay dizisinden (`StructuredResponseRejected`/`StructuredResponseRepairAttempted`) sayılabilir. Tam gerekçe ve bedel tablosu bu dokümanın "Karar: onarım aynı `run` içinde kalır" bölümündedir. |

Açık Soru 1 (`CompactionUsageAccumulator` → `SideChannelUsageAccumulator` yeniden
adlandırması) ve Açık Soru 2 (onarım turu `session: null` ile) planın kendi
önerisiyle (A) kapatıldı — ikisi de yerel implementasyon tercihidir, ayrı bir
`K-*` kaydı açmaz. Açık Soru 2'nin ölçümü: `Microsoft.Agents.AI` 1.18.0'a karşı
küçük bir konsol probuyla doğrulandı — `session: null` ile yapılan bir çağrıda
çerçeve kendi geçici bir `AgentSession` açıyor ve hiç geri bağlamıyor; ölçümün
kendisi koda girmedi (yalnızca kararın gerekçesidir), yalnız
`StructuredResponseValidatingAgent.cs`'nin sınıf dokümanında özetlendi.

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bir agent ile koştu. 🔴 bulgu **yok**.
5 🟡 bulgu, tamamı bu fazda kapandı:

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `MaxRepairAttempts=0` iken bile reddedilen ilk yanıtın token kullanımı `ExtraUsage`'a ekleniyor; DoD'nin "birebir aynı" iddiasını usage boyutunda değiştiriyor, hiçbir testte doğrulanmıyordu | **Gerekçelendi + test eklendi.** Davranış doğru kabul edildi (bkz. Plandan Sapmalar §1); `MaxRepairAttempts_unset_defaults_to_zero_and_behaves_exactly_like_no_repair` artık `run.Usage`'ı da doğruluyor |
| 2 | `JobMetricEndToEndTests.cs`'nin yeni koduna eklenen yorum ("no other test in this project uses this lane") yanlıştı; aynı kusur sınıfı `RunCostMetricEndToEndTests.cs`'de gizil olarak duruyordu | **Düzeltildi.** Yorum, gerçek koşulu (`RecordJob` yalnız gerçek worker'dan yayınlanır) doğru anlatacak şekilde yeniden yazıldı; `RunCostMetricEndToEndTests.cs`'nin gizil kırılganlığı `docs/ADAYLAR.md`'ye F-181 olarak devredildi (bu fazın nedensel kapsamı dışında) |
| 3 | Negatif `MaxRepairAttempts` reddini doğrudan doğrulayan birim testi yoktu | **Düzeltildi.** `AgentPrismStructuredResponseOptionsValidatorTests.cs` eklendi (3 test) |
| 4 | Onarım turunda `Deadline` (wall-clock) zorlamasını doğrudan ölçen bir test yoktu | **Düzeltildi.** `The_trees_deadline_still_applies_to_a_repair_turn_and_stops_it_from_looping` eklendi (`ClockAdvancingModelProvider` + `ManualTimeProvider`) |
| 5 | DoD/plan `concepts/runs.md`'nin güncellenmesini istiyordu; dosyaya dokunulmamıştı ve gerekçe hiçbir yerde yazılı değildi | **Gerekçelendi.** Plandan Sapmalar §2'ye yazıldı |

🔴 bulgu kapanmadığı için dört kapı **yeniden koşturulmadı** (zaten yoktu);
düzeltmeler sonrası dört kapı normal akışın bir parçası olarak yeniden koştu
(bkz. yukarıdaki DoD satırı).

## Sonraki Faza Devir Notu

- **Onarımın maliyeti ayrı bir sütunda değil.** K-654'ün kabul edilen bedeli:
  bir `run`'ın kaç token'ının onarıma gittiğini görmek için `run.usage`
  yetmez, olay dizisi (`StructuredResponseRejected`/`StructuredResponseRepairAttempted`)
  sayılmalıdır. Bir sonraki faz bunu ayrı bir sütuna (`repair_cost`)
  taşımak isterse migration gerekir — bu fazın bilerek kapsam dışı bıraktığı
  kalemdir.
- **`RunCostMetricEndToEndTests.cs`'nin `MeterListener` yalıtımı gizil
  kırılgan (F-181, `docs/ADAYLAR.md`).** Bugün hiçbir eşzamanlı test aynı
  isimli maliyet ölçümü üretmediği için tetiklenmedi, ama
  `JobMetricEndToEndTests.cs`'nin yaşadığı TAM aynı desen. Bu alana yeni bir
  eşzamanlı, gerçek-maliyetli `run` testi eklerken bunu hesaba kat —
  `AgentName`/`ModelId` etiketleriyle süzme deseni `JobMetricEndToEndTests.cs`'de
  hazır örnek olarak duruyor.
- **`run.Usage`'ın artık her zaman gerçek harcamayı yansıttığı genel bir
  ilke hâline geldi.** Faz 131 öncesi "reddedilen bir `run`'ın `Usage`'ı
  `null`'dur" varsayımı artık YANLIŞ — hem doğrudan red (bu faz, bkz.
  Plandan Sapmalar §1) hem tükenen onarım (case 3) gerçek harcamayı
  gösterir. Cost/usage raporlaması üzerine yeni bir faz yazan biri bu
  varsayımı **tekrar ölçmeden** kullanmamalı.
- **`SideChannelUsageAccumulator` artık iki kaynaklı** (compaction +
  bounded repair). Üçüncü bir yan kanal (örn. gelecekte bir "ön işleme"
  model çağrısı) eklenirse aynı sınıfı kullan, yeni bir accumulator açma —
  ad zaten genel.
- **Onarım prompt'u sabit ve İngilizce** (`BuildRepairMessages`,
  `StructuredResponseValidatingAgent.cs`). Tüketici tarafından
  değiştirilebilir bir genişleme noktası açık soru olarak bırakıldı (Açık
  Soru 3, öneri A) — ölçülmüş talep gelirse ayrı bir faz gerekir.
