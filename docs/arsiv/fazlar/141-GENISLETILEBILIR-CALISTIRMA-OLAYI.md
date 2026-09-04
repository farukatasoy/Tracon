# Faz 141 — Genişletilebilir Çalıştırma Olayı

> **Durum:** ✅ Tamamlandı (2026-09-04)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-187** (tüketici turu 3, A5)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.PostgreSql`,
> `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.Sql.Shared`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **Var** — plan başlığındaki "Yok" öncülü
> **yanlış çıktı**: `run_events.type` `smallint`/`INTEGER`'dır, metin değil
> (doğrulandı, bkz. 141.'nin Açık Soru 1 kararı). `custom_type` nullable text
> sütunu üç dialect'e birer migration ile eklendi (Postgres 0044, SqlServer
> 0031, SQLite 0031)
> **Public API:** Büyüdü — kapalı enum'a bir değer (`RunEventType.Custom = 29`),
> `RunEventDraft`/`RunEvent`'e birer `CustomType` alanı, yeni tip
> `RunEventCustomTypes` (`IsValidType`/`IsReserved`/`ReservedPrefix`). 🚨 **Tek
> yönlü kapı:** `Custom` sevk edildikten sonra geri alınamaz
> **Tüketici yüzeyi:** `docs-site/`: `concepts/runs.md` (yeni "Writing your own
> event" bölümü), `ui.md` (konsol satırı), `capabilities.md` (yeni satır) ·
> `getting-started/persistence.md` **güncellenmedi** — site-sync `kalicilik`
> kuralı `custom_type` migration'ı yüzünden tetiklendi ama sayfa şema
> sütunlarını hiç belgelemiyor (bağlantı dizesi, sağlayıcı seçimi,
> `AutoApplyMigrations` anlatıyor); rutin, additive bir sütun eklemenin
> davranışı sayfanın konusuyla örtüşmüyor. `--site-gerekce-yazildi` ile geçildi
> · sevk edilen: XML dokümanı (`<example>` YOK — `RunEventCustomTypes`'ın
> metotları `Add*`/`Use*`/`Map*` değil, `JobHandlerKeys.IsValidKey`/`IsReserved`
> ile aynı gerekçeyle), konsol jenerik kartı (`run-detail.tsx`)
> **Manuel test alanı:** [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](../../manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md) — MT-UIRUN-052..055

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 5f9931bf:docs/arsiv/fazlar/141-GENISLETILEBILIR-CALISTIRMA-OLAYI.md
> ```
>
> Damıtıldı 2026-09-04 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tüketici bugün SSE akışına **hiçbir** kendi olayını yazamıyor. Yazma yolu açık — `RunEventWriter.AppendAsync` public ve `AgentRunScope.Writer` erişilebilir — ama taşınacak bir tür yok: `RunEventDraft.Type` kapalı bir enum. Bu faz kapalılığı **korur** ve tek bir kaçış deliği açar.

## Bitiş Ölçütleri (DoD)

- [x] Tüketici kodu SSE akışına kendi olayını yazabilir (case 1 kanıt) — gerçek OpenAI koşumuyla ölçüldü, bkz. MT-UIRUN-052
- [x] `agentprism.` önekli tür **reddedilir** — `RunEventDraftValidationTests`
- [x] İki yönlü doğrulama çalışır (`Custom`↔`CustomType`) — test + üretimde ölçülen kanıt (`ToolInvoked.customType == null`)
- [x] `customType` bellek içi + üç SQL sağlayıcıda korunur — `RunStoreContract` dördünde de koşuldu
- [x] Replay `Custom` olayını aynen taşır — `GET .../events` canlı akış ve geçmiş okuma AYNI kod yolu (K-014); `StreamingTests` ikisini birden kanıtlar. Ayrı bir `RunReplayEndpointTests` case'i eklenmedi: `RunReplayService` `run_events`'e hiç dokunmuyor, tool kodu (Custom'ı yazan) replay altında da NORMAL çalışır — özel bir kod yolu yok, test edilecek özel bir davranış da yok
- [x] Konsol bilinmeyen türde kırılmaz; jenerik kart çizer — E2E (`Custom_run_event_renders_as_a_generic_card_named_after_its_CustomType`) + gerçek koşum
- [x] 🚨 `.cs` ve `.ts` enum listesi eşleşir; eşleşme testle kilitli — `RunEventTypeFrontendParityTests` (yeni); koşum SIRASINDA `.ts`'nin zaten iki üye (`DocumentAttached`, `RunContinuationBlocked`) eksik olduğu bulundu ve düzeltildi — bu fazdan önce vardı, konuyla ilgisiz bir kusurdu
- [x] K-647 taban çizgisi büyümedi veya `Custom` gerekçesiyle **beyan edildi** — mekanik olarak `covered` oldu (`StreamingTests.cs` gerçekten payload'ı okuyup doğruluyor), bkz. Plandan Sapmalar
- [x] Bundle payı ölçüldü ve yazıldı — **176.9 KB gzip / 250 KB bütçe** (ölçüldü, `npm run build`)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 9a243228`, commit sonrası **iki** tam koşum: 1. koşum `AgentPrism.Sql.Shared.UnitTests`'in `SqlTextSnapshotTests`'ini kırdı (beklenen — `custom_type` sütunu SQL metnini değiştirdi; `AGENTPRISM_SQL_SNAPSHOT_REFRESH=1` ile taban çizgisi yenilendi, ayrı commit), 2. koşum `AgentPrism.Ui.E2ETests.UiTests.Runs_screen_lists_only_roots_by_default`'ı bir kez kırdı — bilinen "tam çözüm koşumunda kaynak çakışması" flaky sınıfının (`docs/hafiza/test-altyapisi.md`, Faz 103'te belgelendi) altıncı örneği; izole 4/4 ve tüm E2E projesi 58/58 geçti, fazın kendi değişikliğiyle ilgisiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. MT-UIRUN-052 (run `01a06aa0-5eac-705b-9101-d0c5bdeeaea4`)
- [x] `secret` taraması boş döndü — `scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md` içine eklendi — MT-UIRUN-052..055
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — üç 🟡 bulundu, üçü de düzeltildi
- [x] `docs-site/` güncellendi; `npm run check` (dördü de: içerik, derleme, bağlantı, ağırlık) temiz; `scripts/site-deploy.sh` ile yayınlandı ve canlıda doğrulandı (`curl https://agentprism.doayen.web.tr/concepts/runs/` "Writing your own event" içeriyor)

---

## Plandan Sapmalar

- **"Migration yok" öncülü yanlıştı.** Plan başlığı `run_events.type`'ın metin
  olarak saklandığını varsayıyordu; `faz-baslangic` araştırması bunun
  `smallint`/`INTEGER` olduğunu gösterdi. Açık Soru 1 bu yüzden gerçek bir
  migration kararına dönüştü ve kullanıcıya soruldu — "ayrı sütun" (önerilen
  seçenek) onaylandı. Üç migration eklendi (141.1 planındaki gibi 1-128
  karakter, `agentprism.` rezerve önek doğrulaması `nvarchar(200)`/`text`/`TEXT`
  genişliğiyle uyumlu).
- **K-647 taban çizgisi `uncovered` DEĞİL, `covered` oldu.** Plan 141.3'ün
  beklentisi `Custom`'ın "AgentPrism payload iddiası taşımıyor" gerekçesiyle
  `uncovered` girmesiydi. Gerçekte `StreamingTests.cs`'e eklenen fonksiyonel
  test hem canlı SSE'de hem geçmiş okumada `customType`'ın hayatta kaldığını
  KANITLIYOR — `RunEventPayloadContractTests`'in taban çizgisi üretici
  script'i bunu otomatik `covered` işaretledi (dosya adı: kapsayan test).
  Ratchet yönü yalnız `uncovered → covered`'a izin verir; bu doğru yönde bir
  sapma, bir kusur değil.
- **Pre-existing kusur bulundu ve düzeltildi (fazla ilgisiz):**
  `run-event.ts`'nin `RunEventType` union'ı `DocumentAttached` (Faz 14) ve
  `RunContinuationBlocked`'ı (Faz 87) hiç taşımıyordu — K-411 sınıfının canlı
  bir örneği, `.cs` güncellenmiş `.ts` unutulmuştu. Bu faz için eklenen yeni
  `RunEventTypeFrontendParityTests` bunu ilk koşumda kırmızı yakaladı. İkisi de
  `run-event.ts`'e ve `run-detail.tsx`'in `EVENT_STYLE`'ına eklendi.
  Kullanıcının talimatı ("konuyla alakasız bug/defect'lerle karşılaşırsan onları
  da çöz") gereği ayrı bir faz açılmadı, burada kapatıldı.
- **`RunReplayEndpointTests`'e ayrı bir Custom case'i eklenmedi.**
  Hata Modları tablosu "Replay Custom olayını taşımaz | Fonksiyonel |
  ReplayTests" satırını taşıyordu. Araştırma `RunReplayService`'in
  `run_events`'e hiç dokunmadığını gösterdi (`grep -rn "RunEventType"
  src/AgentPrism.Core/Replay/RunReplayService.cs` sıfır döner) — replay
  `RunRecordingAgent`'ı yeniden çalıştırır, tool kodu (Custom'ı yazan kod
  dahil) normal yoldan geçer. Test edilecek Custom'a ÖZGÜ bir davranış yok;
  `StreamingTests`'in genel SSE/geçmiş-okuma testi (K-014: canlı ve geçmiş
  AYNI kod yolu) bunu zaten kanıtlıyor.
- **`TenantIsolationContract`'a ayrı bir Custom case'i eklenmedi.** Aynı
  gerekçe: `custom_type` sütunu `InsertRunEvent`/`SelectRunEvents`'in ZATEN var
  olan `@tenant_id` koruma cümlesinden geçiyor (bkz. `SqlQueriesBase.cs`), tür
  bazlı bir dallanma yok. Var olan tenant izolasyon testleri her `RunEvent`
  türünü zaten kapsıyor.
- **`samples/AgentPrism.Api`'de gerçek koşum yapıldı — planlanandan daha güçlü
  kanıt.** Ortamda OpenAI `user-secrets` zaten yapılandırılıydı; `mark_preview_ready`
  tool'u eklenip `support` agent'ına bağlandı ve gerçek bir `gpt-5.4-mini`
  çağrısıyla `Custom` olayı üretildi, kalıcılaştırıldı ve SSE ile geri okundu
  (run `01a06aa0-5eac-705b-9101-d0c5bdeeaea4`). Plan yalnızca "gerçek run
  yapıldı" istiyordu; bu koşum aynı zamanda iki yönlü doğrulamayı ÜRETİMDE de
  kanıtladı (`ToolInvoked` olayının `customType`'ı `null`).

## Bu Fazda Verilen Kararlar

Üçü de kullanıcıya `AskUserQuestion` ile soruldu; önerilen seçenek onaylandı —
yeni bir `K-*` kaydı açılmadı, çünkü üçü de bu fazın kendi kapsamındaki yerel
implementation tercihidir (public API/uyumluluk sözleşmesi değil; `Custom`'ın
KENDİSİ zaten plan onayıyla public API'ye giriyordu).

1. **`CustomType` ayrı bir `custom_type` sütunudur, `Payload` içine gömülmez.**
   `ToolName`/`ToolCallId` ile aynı desen; üç migration eklendi.
2. **Geçersiz `CustomType` çağrıyı `ArgumentException` ile reddeder,
   sessizce atlamaz.** `RunEventWriter.AppendAsync`'in en başında
   `ValidateCustomType` çalışır — sequence numarası TÜKETİLMEDEN.
3. **`Custom` olayı `IRunEventSink`'e de gider.** Sıfır ek kod gerekti:
   `DispatchToSinksAsync` zaten her `RunEvent`'i türden bağımsız dağıtıyordu.

## Denetim Bulguları

Bağımsız, taze bağlamlı bir denetçi (`faz-denetim`) çalışma ağacının tamamını
(`git diff 9a243228`) inceledi ve derleme + beş test projesini (`Core.UnitTests`
2329/2329, `AspNetCore.FunctionalTests` 766/766, üç SQL entegrasyon projesi
gerçek Docker konteynerleriyle) bağımsızca yeniden koştu. **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | K-647 taban çizgisinde `Custom \| covered \| StreamingTests.cs` iddiası mekanik olarak doğruydu (gate dosya-seviyesinde `RunEventType.Custom` + `Payload` metnini arıyor) ama davranışsal olarak eksikti: kapsayan test yalnız `customType`'ı doğruluyordu, `Payload`'ın İÇERİĞİNİ (`orderId`) hiç okumuyordu. | **Düzeltildi.** `StreamingTests.cs`'e `custom.Data.ShouldContain("orderId")`/`"ORD-7"` eklendi — artık gerçekten payload içeriğini okuyor. |
| 2 | 🟡 | `ValidateCustomType`'ın `Interlocked.Increment`'ten ÖNCE çağrıldığı iddiası (reddedilen bir çağrının sequence numarasını "yakmadığı") koddan doğrulanabiliyordu ama hiçbir test bunu kanıtlamıyordu. | **Düzeltildi.** `RunEventDraftValidationTests.A_rejected_call_does_not_burn_a_sequence_number` eklendi: reddedilen bir çağrıdan sonraki başarılı yazımın `Sequence`'ı atlanmadan devam ediyor. |
| 3 | 🟡 | `RunEventType.Custom`'ın XML dokümanı, kardeş üyelerin (`ToolOutputTruncated`, `StructuredResponseRejected`) aksine `Payload`'ının `AgentPrismRunRecordingOptions.RecordToolPayloads`'a tabi olduğunu belirtmiyordu. | **Düzeltildi.** `<summary>`'ye aynı uyarı eklendi. |

🟢 yok. Denetçi ayrıca on spesifik teknik soruyu (sequence sırası, SQL ordinal
eşlemesi, iki yönlü doğrulama, DoD kanıtları, `.ts` tutarlılığı, `PublicAPI`
büyümesi, migration deseni, İngilizce/iç-referans sınırı, Plandan Sapmalar
iddiaları) bağımsızca doğruladı; hepsi geçerli bulundu.

Düzeltmelerden sonra `Core.UnitTests` (2330/2330, yeni testle) ve etkilenen
`AspNetCore.FunctionalTests` testi yeniden koşuldu — yeşil.

## Sonraki Faza Devir Notu

- `RunEventCustomTypes` deseni artık `JobHandlerKeys`/`JobLanes` ailesinin
  üçüncü örneği. Benzer bir "tüketici namespace'i + rezerve önek" ihtiyacı
  çıkarsa aynı üçlüyü (`ReservedPrefix` sabiti, `IsValidType`, `IsReserved`,
  `[GeneratedRegex]`) kopyala — soyutlamaya çevirme, bu depoda BİLEREK üç kez
  tekrarlanan bir desendir.
- `run-event.ts`'nin `.cs` ile senkronu artık `RunEventTypeFrontendParityTests`
  ile kilitli. Yeni bir `RunEventType` üyesi eklerken bu test seni hem `.ts`
  union'ına HEM `run-detail.tsx`'in `EVENT_STYLE`'ına (derleyici zorlar)
  götürür.
- `run_events` tablosunun sütun sayısı arttı (`custom_type`, ordinal 8).
  `SqlRunStore.ReadEvent`'in ordinal eşlemesi kırılgandır — yeni bir sütun
  eklerken her zaman SONA ekle, var olan ordinal'leri kaydırma (bu fazda da
  öyle yapıldı: `custom_type` hem `InsertRunEvent`/`SelectRunEvents` hem
  `ReadEvent`'te listenin EN SONUNDA).
- `getting-started/persistence.md` bu fazda BİLEREK güncellenmedi (site-sync
  gerekçesi yukarıda). Sayfa şema sütunlarını hiç belgelemiyor; bir sonraki faz
  bu sayfaya gerçekten şema-seviyeli bir değişiklik getirirse (ör. yeni bir
  tablo, yeni bir sağlayıcı) bu gerekçe ARTIK geçerli olmayabilir — yeniden
  değerlendir, kopyalama.
