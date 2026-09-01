# Faz 128 — Run Ağacı Süre Bütçesi

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](../../kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-7**
> **Önkoşul:** [Faz 114](114-CALISTIRMA-ICI-BUTCE-TAVANI.md) (çalıştırma-içi bütçe tavanı, `RunBudgetChatClient`) — arşivde; **damıtılmış**, tam metin `git show 3fbdc7d:docs/arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md`
> **Paketler:** `AgentPrism.Abstractions` (`Runs/AgentRunBudget.cs`), `AgentPrism.Core` (`Models/RunBudgetChatClient.cs`, `AgentPrismOptions.cs`)
> **Yeni paket:** Yok · **Migration:** Yok — tavan yapılandırmadan gelir
> **Public API:** Büyüyor — mevcut iki tipe birer alan. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/governance.md`, `guides/production.md`, `capabilities.md` · sevk edilen: `AgentRunBudget` ve `AgentPrismAgentGraphOptions` XML dokümanları
> **Manuel test alanı:** `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 17a3edb:docs/arsiv/fazlar/128-RUN-AGACI-SURE-BUTCESI.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`AgentRunBudget` token, maliyet, çocuk run sayısı ve derinlik taşır; **süre taşımaz.** İterasyon tavanı bir iterasyonun ne kadar süreceğini sınırlamaz ve tool timeout'u yalnız **tek bir çağrıyı** sınırlar. Uzun tool zinciri olan bir run saatlerce sürebilir. - **T-7** — Run ağacına bir süre boyutu; kesme noktası mevcut desene uyar.

## Bitiş Ölçütleri (DoD)

- [x] `MaxDuration` dolduğunda bir sonraki model çağrısı **yapılmaz**; run `QuotaExceeded` ile kapanır — `AgentRunBudgetDurationTests`, `RunDeadlineTests`, gerçek koşum (aşağıda)
- [x] Kesme her zaman iki model turu arasındadır; kesilen run'ın son olayı yarım bir model mesajı **değildir** — gerçek koşumda ölçüldü ve çıktı belgeye yazıldı: `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` §6, MT-RET-060/061 (2026-09-01, `samples/AgentPrism.Api`, gerçek OpenAI çağrısı)
- [x] Çocuk run'lar kökle **aynı** son tarihi görür — `AgentRunBudget` paylaşılan TEK nesnedir (değişmedi); `ChildAgentInvokerTests.Budget_is_a_SINGLE_instance_across_the_tree` + `New_child_run_does_not_start_once_the_deadline_has_passed`
- [x] Kuyruğa alınmış run'da da son tarih uygulanır — `RunDeadlineTests.Deadline_is_enforced_on_the_queued_durable_run_path_too` + gerçek koşum (MT-RET-063, 2026-09-01, 1 saniyede tamamlandı)
- [x] `MaxDuration` ayarlı değilken (sıfır) davranış bugünküyle **aynıdır** — `RunDeadlineTests.No_MaxDuration_set_leaves_todays_behavior_unchanged` + gerçek koşum (MT-RET-062)
- [x] Kesme mesajı hangi boyutun dolduğunu söyler; mesaj ile `IsExhausted` **tek** kaynaktan türer (K-483) — `AgentRunBudget.DescribeExceededLimit()`/`IsExhausted` aynı `IsTokenBudgetExhausted`/`IsCostBudgetExhausted`/`IsDurationBudgetExhausted` üçlüsünü okur; `AgentRunBudgetDurationTests.DescribeModelCallExhaustion_and_IsExhausted_derive_from_the_SAME_deadline_check`
- [x] Süre `TimeProvider` üzerinden okunur; hiçbir test gerçek saate bağlı değildir — `AgentRunBudget`'ın kendi kurucusu (`TimeSpan? maxDuration, TimeProvider? timeProvider`) `Deadline`'ı `TimeProvider.GetUtcNow()` üzerinden kurar; birim testleri `ManualTimeProvider` kullanır (fonksiyonel `RunDeadlineTests` kuyruklu case'i hariç — bkz. Plandan Sapmalar)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban HEAD` (2026-09-01): tarama, `dokuman-bakim --denetle`, `dotnet build`, tam çözüm testi (517,85 sn), `dotnet pack`, `dotnet format --verify-no-changes`, `npm run check` — hepsi ✅
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` §6 (MT-RET-060..063)
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` temiz
- [x] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi — MT-RET-060..064
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz — `reliability.md`, `governance.md`, `production.md`, `capabilities.md`

### Doğrulama komutları

```bash
# Süre tavanı gerçekten kesiyor mu
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests --filter-method "*AgentRunBudgetDuration*"

# Kuyruktaki run'da (sınır: kuyruk)
./artifacts/bin/AgentPrism.AspNetCore.FunctionalTests/release/AgentPrism.AspNetCore.FunctionalTests --filter-method "*RunDeadline*"
```

---

## Plandan Sapmalar

1. **`AgentRunBudget` bir kurucu kazandı; plan yalnız "iki alan" diyordu.**
   Plan `MaxDuration`/`Deadline`'ı `{ get; init; }`/`{ get; }` çifti olarak
   taslak çiziyordu ama "Deadline bir kez hesaplanır" gereksinimi bir
   `TimeProvider`'ın BİR YERDE tutulmasını zorunlu kıldı. `RunBudgetChatClient`
   veya `TryReserveRun`'a parametre eklemek yerine `TimeProvider`
   `AgentRunBudget`'ın kendi `private readonly` alanında tutuldu
   (kurucu: `AgentRunBudget(TimeSpan? maxDuration = null, TimeProvider? timeProvider = null)`).
   Sonuç: `IsExhausted` artık `IsTokenBudgetExhausted || IsCostBudgetExhausted
   || IsDurationBudgetExhausted` üçlüsünü okur ve `RunBudgetChatClient.
   ThrowIfExhausted()`/`TryReserveRun()` **HİÇ değişmeden** yeni boyutu otomatik
   kapsar — mermaid akışındaki tek `IsExhausted` kararı koda birebir yansıdı.
2. **`AgentPrismAgentGraphOptions.CreateBudget()` imzası kırıldı** →
   `CreateBudget(TimeProvider timeProvider)`. Plan bunu açıkça yazmamıştı;
   `Deadline`'ın `TimeProvider` üzerinden bir kez hesaplanması gereksinimi
   zorunlu kıldı. İki çağıran zaten kendi `_timeProvider` alanını taşıyordu
   (`RunRecordingAgent.Lifecycle.cs`, `WorkflowRunner.cs`) — çağıran tarafta
   ek bir bağımlılık gerekmedi. Pre-1.0/`Unshipped` olduğu için kırıcı
   değişiklik bir uyumluluk sorunu değildir (K-603).
3. **Birim testi dosyası `tests/AgentPrism.Core.UnitTests/Runs/
   RunDurationBudgetTests.cs` yerine `Graph/AgentRunBudgetDurationTests.cs`
   olarak açıldı.** `faz-uygulama`'nın "planın yapısal iddiasını kabul etmeden
   ölç" kuralı: kardeş dosyalar (`AgentRunBudgetTests.cs`,
   `AgentRunBudgetCostTests.cs`) zaten `Graph/` altındaydı — `AgentRunBudget`
   kaynağı `Abstractions/Runs/` altında olsa da testleri tarihsel olarak
   `Graph/` altında yaşıyor; tutarlılık planın tahmin ettiği yoldan ağır bastı.
4. **Kuyruklu (dayanıklı) fonksiyonel testte sahte `TimeProvider` KULLANILMADI.**
   İlk denemede `services.AddSingleton<TimeProvider>(fakeClock)` tüm host'a
   (`JobWorkerBackgroundService` dahil) uygulandı — kira yenileme/poll döngüsü
   AYNI sahte saati okuyor ve saat yalnız test kodunun elle `Advance()`
   çağırdığı anlarda ilerlediği için o döngü **sonsuza kadar donuyor** (ölçüldü:
   test 30 saniyede zaman aşımına uğradı, run hiç `Queued`'dan çıkmadı). Çözüm:
   `Deadline_is_enforced_on_the_queued_durable_run_path_too` testi GERÇEK saat +
   kısa gerçek `MaxDuration` (200ms) + kısa gerçek tool gecikmesi (`Task.Delay`
   500ms) kullanır; senkron testler sahte saatle kalmaya devam eder. Tuzak
   `docs/hafiza/test-kosum-tuzaklari.md`'ye eklendi.
5. **`docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`'nin "gerçek koşum" bölümü
   pre-existing bir `secret` yapılandırma sürüklenmesini ortaya çıkardı ve
   düzeltti (kusur değil, kod dışı).** `samples/AgentPrism.Api`'nin yerel
   `dotnet user-secrets`'ı `AgentPrism:ContentProtection:RawKeys:sample2`
   taşıyordu, ama `appsettings.json`'ın `ActiveKeyId` alanı `"sample"` bekliyordu
   — muhtemelen eski bir oturumdan kalma adlandırma kayması. Etkisi: içerik
   koruması `AgentPrismException` fırlatınca `RunRecordingAgent` **kayıt
   yazmayı bu run için devre dışı bırakır** (tasarlanan davranış — bkz.
   `docs/hafiza/`'nın "gözlemlenebilirlik işlevselliği bozmaz" kuralı) ve run
   `Running`'de asılı kalır; asıl bütçe kesmesi doğru çalışır (HTTP yanıtı doğru
   gövdeyi taşır) ama `GET /api/runs/{id}` hiçbir zaman `Failed`'e ulaşmaz.
   Yerel `secret` düzeltildi (`dotnet user-secrets set
   "AgentPrism:ContentProtection:RawKeys:sample" ...`); repo koduna dokunulmadı
   çünkü kod TASARLANDIĞI gibi davrandı. MT-RET-060/061/063'ün ölçümleri
   düzeltmeden SONRA alındı.

## Bu Fazda Verilen Kararlar

Yok — kesme, K-627/K-630'un zaten karara bağladığı `QuotaExceeded`
sınıflandırmasını aynen yeniden kullanır; yeni bir public API/uyumluluk
sözleşmesi, güvenlik sınırı veya kalıcı veri kararı alınmadı. `AgentRunBudget`
kurucusu ve `CreateBudget` imza değişikliği yerel implementation tercihidir
(bkz. Plandan Sapmalar #1-2), `K-*` kaydı gerektirmez.

## Denetim Bulguları

Bağımsız denetim (2026-09-01, taze bağlamlı ayrı agent): **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `RunErrorClass.QuotaExceeded`'ın XML doc'u yalnız "token/cost budget ran out" diyordu; süre bu fazdan sonra aynı sınıfa düşüyor ama enum üyesinin dokümanı güncellenmemişti (`src/AgentPrism.Abstractions/Runs/RunErrorClass.cs:38-42`). | **Düzeltildi** — "token, cost, or time budget" olarak güncellendi. |
| 2 | 🟢 | `samples/AgentPrism.Api`'de içerik koruması bir `AgentPrismException` fırlattığında `RunRecordingAgent` o run için kayıt yazmayı durduruyor (tasarlanan davranış) ve run `Running`'de asılı kalabiliyor — kapsam dışı, bu fazın `MaxDuration` işiyle ilgisi yok. | **Devredildi** — `docs/ADAYLAR.md`'ye taşınmadı (yerel `secret` yapılandırma sürüklenmesiydi, ürün kusuru değil; bkz. Plandan Sapmalar #5). |

Sekiz başlığın (3.1–3.8) tamamı temiz çıktı; denetçinin tam raporu bu oturumun
kayıtlarındadır, özetlendi.

## Sonraki Faza Devir Notu

- **Beşinci bir bütçe boyutu eklenecekse aynı deseni izle.** Token/maliyet
  gibi `Interlocked` sayaçlı bir boyut DEĞİLSE (yani "bir kez hesapla, sonra
  karşılaştır" türündeyse — `Deadline` gibi), yeni bir yardımcı parametre
  `RunBudgetChatClient`/`ChildAgentInvoker`'a EKLEMEYE gerek yok:
  `AgentRunBudget`'ın kendi `private readonly` alanına taşı ve `IsExhausted`'a
  bir OR dalı ekle — kesme noktası ve mesaj üretimi (`DescribeExceededLimit`)
  otomatik kapsar.
- **🚨 Fonksiyonel testte global bir sahte `TimeProvider` kaydetme.**
  `JobWorkerBackgroundService`'in kendi poll/kira-yenileme döngüsü DE aynı
  `TimeProvider`'ı okur; elle ilerleyen bir sahte saat o döngüyü SONSUZA KADAR
  dondurur (bkz. Plandan Sapmalar #4). Kuyruklu/arka-plan bir senaryoyu test
  ederken ya gerçek saat + kısa gerçek süre kullan, ya da yalnız SENKRON
  (job worker'sız) yolu sahte saatle test et.
- **`AgentGraph.MaxDuration` şu an yalnız yapılandırmadan gelir** (Açık Soru
  1, seçenek A). İstek başına (`AgentPrismRunOptions.MaxDuration`) bir ihtiyaç
  ölçülürse ayrı bir kalem olarak açılmalı — bu fazda AÇILMADI.
- Örnek uygulamanın (`samples/AgentPrism.Api`) yerel `dotnet user-secrets`
  deposu `AgentPrism:ContentProtection:RawKeys:sample` anahtarını artık
  taşıyor (bu oturumda düzeltildi, bkz. Plandan Sapmalar #5) — gelecekteki bir
  manuel test oturumu bu anahtarı tekrar eksik bulursa bu NOT'a bakabilir.
