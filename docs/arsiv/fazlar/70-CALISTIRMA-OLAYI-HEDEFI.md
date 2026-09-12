# Faz 70 — Çalıştırma Olayı Hedefi ve Düşünme Akışı

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-115**
> **Önkoşul:** [Faz 6](06-GOZLEMLENEBILIRLIK.md) — `RunRecordingAgent` ve olay yazımı · [Faz 61](61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) — gömülebilir bileşen, taşıyıcı tarafının istemci yarısı
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **büyüyor (küçük)** — bir arayüz ve `RunEventType`'a **bir ekleme**. Enum sonuna ekleme K-040 ile serbesttir. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/runs.md`, `guides/observability.md`, `concepts/agents.md` (reasoning)
> **Manuel test alanı:** [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](../../manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/70-CALISTIRMA-OLAYI-HEDEFI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'in olay akışı zengindir ama **tek bir tüketicisi** vardır: veritabanı. Kendi gerçek zamanlı arayüzüne gömen bir tüketici bu akışa ancak kendi HTTP sunucusuna SSE ile bağlanarak ya da `IRunStore`'u dekore ederek ulaşır. İkisi de yanlış yerdir. Bu faz, olayları süreç içinde dinlenebilir kılar.

## Bitiş Ölçütleri (DoD)

- [x] Hedef kayıtlı değilken sıcak yol değişmez (test kanıtıyla) —
      `RunEventSinkTests.No_sink_registered_leaves_the_run_unaffected`;
      `_sinks.Count == 0` erken dönüşü `DispatchToSinksAsync`'te
- [x] Hedefin gördüğü sıra numaraları depodakiyle birebir aynı —
      `RunEventSinkTests.A_sink_sees_the_exact_same_sequence_numbers_as_the_store`,
      `WorkflowEventSinkTests.A_registered_sink_sees_the_workflow_s_own_events`;
      gerçek koşumda da ölçüldü (aşağı bak, `0..10` boşluksuz)
- [x] Patlayan hedef `run`'ı düşürmez ve depoya yazımı **engellemez** —
      `RunEventSinkTests.A_sink_that_always_throws_is_disabled_after_its_first_failure_and_the_run_still_completes`,
      `One_sink_failing_does_not_silence_a_healthy_sink_registered_alongside_it`,
      `A_sink_still_receives_every_event_when_the_store_itself_is_disabled`
      (bu son test, denetim sırasında bulunan bir kusuru da kanıtlar — bkz.
      Plandan Sapmalar)
- [x] `RecordReasoningDeltas` varsayılanı `false`; açıkken `ReasoningDelta`
      olayları `MessageDelta`'dan ayrı akar — `ReasoningRecordingTests` (5
      test), gerçek koşumda da ölçüldü
- [x] Bilinmeyen olay tipi eski istemcide yok sayılır — `foldRunEvents`'in
      `default: break` dalı (`transcript.ts`) yapısal olarak garanti eder;
      ayrıca `Record<RunEventType,...>` (TypeScript) yeni bir üye eklendiğinde
      derleme hatası verir, bu da `ModelFallbackUsed`'ın frontend'de hiç
      tanımlanmadığı bağımsız boşluğunu ortaya çıkardı (bkz. Plandan Sapmalar)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/
      `format --verify-no-changes`, hepsi temiz
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
      — bkz. MT-UIRUN-048, gerçek Anthropic extended-thinking çağrısı: 7
      `ReasoningDelta` + 2 `MessageDelta` + `RunStarted` + `RunCompleted` = 11
      olay, sıra `0..10` boşluksuz, düşünme metni 253 karakter / yanıt 143
      karakter (hacim riskine ilk somut veri noktası)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri
      [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](../../manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md)
      içine eklendi (MT-UIRUN-047/048/049); 047/048 API üzerinden koşuldu,
      049 (arayüzde katlanabilir blok) 👤 insan gerektirir olarak işaretlendi
      — paylaşılan Playwright tarayıcı oturumu meşguldü
- [x] `faz-denetim` koşuldu; 🔴 ve 🟡 bulgu yok (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz —
      `concepts/runs.md`, `guides/observability.md`, `ui.md`; `concepts/workflows.md`
      kasıtlı atlandı (gerekçe: Plandan Sapmalar)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — yeni bir
      i18n anahtarı GEREKMEDİ (`ReasoningBlock`/`transcript.reasoning` Faz
      70'ten önce zaten vardı); bundle 146,1 KB → **~148,2 KB** brotli
      (`postbuild.mjs` çıktısı), bütçe 250 KB gzip'in çok altında (172,8 KB
      gzip ölçüldü)

### Doğrulama komutları

```bash
# Reasoning olayı ayrı akıyor mu — claude-thinking zaten örnek uygulamada var
# (extended thinking, samples/Tracon.Api/Program.cs), "thinker" değil.
curl -N -s http://localhost:5080/tracon/api/agents/claude-thinking/run/stream \
  -H "Authorization: Bearer manuel-test-token-2026" \
  -H 'content-type: application/json' -d '{"message":"think step by step"}' \
  | grep -c "ReasoningDelta"

# Sıra numaraları boşluksuz mu
curl -s http://localhost:5081/tracon/api/runs/$RUN/events | jq '[.[].sequence] | . as $s | ($s|length) == ($s|max)'
```

---

## Plandan Sapmalar

1. **`ReasoningDelta = 23`, plandaki 22 DEĞİL.** Plan "yirmi iki değer, 0–21"
   diyordu ve bunu ÖLÇÜLDÜ etiketiyle kanıt tablosuna yazmıştı — ama bu ölçüm
   Faz 62'nin `ModelFallbackUsed = 22` eklemesinden ÖNCEKİ bir kod
   okumasındandı. `RunEventType.cs` yeniden okununca 22 zaten doluydu; K-040
   (sıra korunur, yalnız eklenir) gereği yeni değer sona (23) eklendi. K-492.

2. **Doğrulama komutlarındaki `thinker` agent'ı hiç var olmadı.** Plan
   `curl .../agents/thinker/run/stream` örneği veriyordu. Gerçekte
   `samples/Tracon.Api/Program.cs` zaten `claude-thinking` adında,
   Anthropic extended thinking açık bir agent taşıyordu (Faz 62'den kalma
   `ProviderSettings[ThinkingBudgetTokensSetting]` örneği). Yeni bir agent
   eklemek yerine bu doğrulama koşumu `claude-thinking`'i kullandı ve
   `samples/Tracon.Api/appsettings.json`'a `RunRecording.RecordReasoningDeltas: true`
   eklendi (sample'ın kendi bilinçli demonstrasyon deseni — `EnableKnowledge`,
   `PersistAudio` gibi diğer alanlarla aynı stil). Doğrulama komutu düzeltildi.

3. **`RunEventWriter.CompleteAsync`'in üstündeki `if (IsDisabled) return;`
   koruması kaldırıldı — planda YOKTU, uygulama sırasında bulundu.** Depo
   aynı `run` içinde DAHA ÖNCE bir yazımda başarısız olduysa, bu koruma
   kapanış olayının (RunCompleted/RunFailed) hiç üretilmemesine yol
   açıyordu — sağlıklı bir sink bu yüzden terminal olayı asla görmüyordu.
   "Depo ve sink bağımsız olmalı" ilkesinin (planın kendi "Neden `IRunStore`
   dekorasyonu yeterli değil" tablosu) doğal bir sonucu olarak koruma
   kaldırıldı; yalnız gerçek `_store.CompleteRunAsync` çağrısı `IsDisabled`'a
   bağlı kaldı. Kanıt: `RunEventSinkTests.A_sink_still_receives_every_event_when_the_store_itself_is_disabled`.
   K-493.

4. **Frontend'de `ModelFallbackUsed` (Faz 62) bu fazdan bağımsız, önceden var
   olan bir boşluk olarak bulundu ve aynı anda düzeltildi.** `lib/types.ts`
   `RunEventType` union'ı bu değeri hiç taşımıyordu ve `run-detail.tsx`'teki
   `EVENT_STYLE: Record<RunEventType, ...>` — `ReasoningDelta` eklenince
   TypeScript zaten her üye için bir giriş ZORUNLU kılıyordu — bu eksikliği
   derleme hatasıyla ortaya çıkardı. Kapsam dışı ama bedavaydı (tek satır
   union + tek sözlük girişi); ayrı bir kusur açmak yerine aynı yerde
   düzeltildi.

5. **`docs/KARARLAR.md` ve `docs/hafiza/cekirdek-calistirma.md` bütçe
   aşımına uğradı, arşivlendi.** Bu fazın iki yeni kararı `KARARLAR.md`'yi
   475.000 bayt bütçesinin 1.071 bayt üzerine taşıdı (dosya zaten %99,8
   doluydu). K-391'in tam gerekçesi `arsiv/KARARLAR-GECMISI.md`'ye taşındı,
   satır kısaltılarak korundu. `cekirdek-calistirma.md`'de üç madde aynı
   şekilde `arsiv/HAFIZA-GECMISI.md`'ye taşındı. **Not:** `KARARLAR.md` şimdi
   474.455/475.000 bayt (%0 boş) — bir sonraki faz ilk kararında yeniden
   arşivleme gerekecek.

6. **`concepts/workflows.md` docs-site güncellemesi kasıtlı atlandı.**
   `WorkflowRunner`'a `sinks` parametresi eklendi ama bu, zaten
   `concepts/runs.md`'de genel olarak belgelenen `IRunEventSink` kavramının
   ötesinde workflow'a özgü yeni bir davranış TAŞIMIYOR — agent ve workflow
   çalıştırmaları aynı arayüzü paylaşır. `scripts/dokuman-bakim.py --site-denetle`
   bunu aday olarak işaretledi ama kapı geçti (üç sayfa zaten değişmişti).

## Bu Fazda Verilen Kararlar

- **K-492** — `RunEventType.ReasoningDelta` değeri 23, plandaki 22 değil.
- **K-493** — `IRunEventSink` fan-out'u depo başarısından tam bağımsız;
  `RunEventWriter.CompleteAsync` artık `IsDisabled` iken erken dönmez.

Tam metin: `docs/KARARLAR.md` (K-492, K-493).

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (2026-08-19). **🔴 ve 🟡 yok.**
Denetçinin tek gözlemi (`RunEndpoints.EventName()`'in `ReasoningDelta`/
`ModelFallbackUsed` gibi birçok tip için SSE `event:` alanında `"unknown"`a
düşmesi) bu fazdan ÖNCE de aynı davranıştaydı (`ContentMasked`,
`WorkflowStarted` vb. de kapsanmıyor) — frontend `event:` alanını değil
`data:` JSON'undaki `type`'ı okuduğu için davranışsal etkisi yok; bu fazın
kapsamı dışında bırakıldı, aday listesine de yazılmadı (davranış değişikliği
gerektirmiyor).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IRunEventSink` kaydı düz DI: `services.AddSingleton<IRunEventSink, T>()`
  (veya `TryAddEnumerable`) yeterli — özel bir `AddXxx` uzantı metodu yok,
  `IContentGuard`/`IAgentSource` deseniyle aynı.
- Bir sink her zaman `RunEvent`'in KENDİSİNİ alır (daraltılmış bir DTO değil);
  olay zaten sıra numarası ve `TenantId` damgasıyla gelir.
- `RunEventWriter`'ın kurucusuna 5. parametre (`sinks`) eklendi;
  `RunRecordingAgent`/`RunRecordingAgentDecorator`/`WorkflowRunner`'ın
  kurucularına da SONA (mevcut son opsiyonel parametreden sonra) eklendi.

**🚨 Bilinen tuzaklar:**
- Bir kurucuya `IEnumerable<IRunEventSink>?`/`IReadOnlyList<IRunEventSink>?`
  gibi yeni bir opsiyonel parametre eklerken, o kurucuyu POZİSYONEL argümanla
  (özellikle `params` dizisinden önce) çağıran test yardımcıları KIRILABİLİR
  — `WorkflowTestHost.CreateRunner`'da yaşandı, çözüm ikinci bir aşırı yüktü.
  Ayrıntı: `docs/hafiza/workflows.md`.
- `RunEventType`'a yeni bir değer eklemeden önce dosyanın KENDİSİ okunur;
  planın "son değer N" iddiası güvenilmez (K-492).
- Bir `Record<EnumTürü, ...>` (TypeScript) sözlüğü eksik anahtarı derleme
  hatası yapar — yeni bir backend enum üyesi eklerken frontend union'ı VE bu
  tür sözlükleri birlikte güncellenmeli, `docs/hafiza/frontend.md`.

**Yarım kalan/kapsam dışı bırakılan işler:**
- MT-UIRUN-049 (arayüzde düşünme bloğunun görsel doğrulaması) 👤 insan
  gerektirir olarak işaretlendi — paylaşılan Playwright tarayıcı oturumu bu
  oturumda meşguldü.
- `docs/KARARLAR.md` %0 boş — bir sonraki fazın ilk kararı ekleme yapmadan
  önce yeniden arşivleme gerektirebilir.

**Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek; F-115 bu fazla kapandı.
`docs/arsiv/fazlar/71-WORKFLOW-KOD-DUGUMU.md` (F-116) zaten planlanmış durumda.
