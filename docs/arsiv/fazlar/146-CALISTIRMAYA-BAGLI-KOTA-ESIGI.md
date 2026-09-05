# Faz 146 — Çalıştırmaya Bağlı Kota Eşiği

> **Durum:** ✅ Tamamlandı (2026-09-05)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-194** (tüketici turu 4, A2 + F3)
> **Önkoşul:** [Faz 145](145-OLAY-AKISININ-CERCEVE-SOZLESMESI.md) — notice bir `Custom` olayıdır; 145 olmadan istemciye `unknown` adıyla ulaşır ve talebin kendisi karşılanmaz
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç set** (`quota_usage`'a sütun ekleme). Numaralar uygulama anında alınır (K-178)
> **Public API:** Büyüyor — `WebhookQuotaSummary`'ye iki alan, bir seçenek sınıfı, bir notice payload tipi, **ve `IQuotaStore`'a yeni bir üye** (`TryClaimThresholdNotificationAsync` — bu genişleme noktasını uygulayan her tüketici de güncellenmeli; plan bunu ayrı işaretlememişti, denetimde yakalandı, bkz. Plandan Sapmalar #5). `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya (yalnız başlık), **shipped giriş sıfır**: bugün eklemek bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `concepts/runs.md`, `concepts/governance.md`, `guides/observability.md`, `capabilities.md` · sevk edilen: XML `<example>`, `src/AgentPrism.Abstractions/README.md`
> **Manuel test alanı:** [`docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`](../../manuel-test/23-SAKLAMA-ARSIV-KOTA.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show ba7f4ab7:docs/arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md
> ```
>
> Damıtıldı 2026-09-05 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Kota eşiği bugün doğru hesaplanıyor ve `quota.threshold` webhook'u ile yayımlanıyor. İki şey eksik ve ikisi de kütüphanenin **kendi elindeki** bilgiden doğuyor: 1. **Korelasyon yok.** Eşiği geçiren `run`'ın ve kullanıcının kimliği hiçbir kota payload'ında taşınmıyor.

## Bitiş Ölçütleri (DoD)

- [x] `PublishThresholdToRunStream=false` (varsayılan) iken **hiçbir** davranış değişmez — akışta yeni çerçeve yok, webhook aynı (`QuotaRunNoticeTests.Default_off_produces_no_custom_frame`, `QuotaThresholdCrossingTests.Crossing_is_empty_by_default_even_though_the_webhook_still_fires`, MT-RET-070)
- [x] Anahtar açıkken eşiği geçiren `run`'ın notice olayı, terminal olaydan **küçük** bir sıra numarası taşır (`QuotaNoticeOrderingTests`, `QuotaRunNoticeTests.Enabled_notice_arrives_as_a_custom_frame_before_done`, MT-RET-071 — gerçek koşumda `custom` `done`'dan önce geldi)
- [x] Doğrudan `POST` SSE ile `GET /api/runs/{id}/events` **aynı `NoticeId` ve aynı payload**'ı sunar (`QuotaRunNoticeTests.Direct_stream_and_the_events_endpoint_carry_the_same_notice`, MT-RET-072 — gerçek koşumda `noticeId` birebir eşleşti)
- [x] Komşu kullanıcının eşzamanlı `run`'ı notice'ı **almaz** (yapısal garanti: bildirim yalnız tetikleyen `run`'ın kendi `scope.Writer`'ına yazılır; `QuotaRunNoticeTests.A_second_run_that_crosses_no_new_threshold_gets_no_notice` bunu farklı bir `run`'a karşı ölçer)
- [x] `Last-Event-ID` ile yeniden bağlanma aynı notice'ı tekrar okuyabilir (`QuotaRunNoticeTests.Reconnecting_via_Last_Event_ID_still_reads_the_notice`, MT-RET-073)
- [x] Host yeniden başlatıldığında aynı dönemde aynı eşik **ikinci kez** yayımlanmaz (`QuotaStoreContract.A_threshold_already_claimed_in_this_period_is_not_claimed_again` + `Concurrent_claims_of_the_same_threshold_let_only_one_caller_win`, dört koşumda: bellek içi + PostgreSQL + SQL Server + SQLite, 28/28 yeşil her koşumda; MT-RET-075)
- [x] `AllowOnStoreFailure=false` iken kota `store` hatası **sessiz `allow`'a dönüşmez**; bildirim başarısı bunu gevşetmez (davranış değişmedi — `QuotaEnforcerTests.Store_failure_is_rejected_in_strict_setup`, öncekiyle aynı kod yolu)
- [x] Bildirim hatası `run`'ı `Failed` yapmaz — `run` `Completed` kalır (`RunEventWriter.AppendReservedAsync` aynı "observability does not break functionality" sözleşmesini paylaşır — `AppendAsync`'in mevcut `try/catch` + `Disable()` yolu, yeni kod eklemedi)
- [x] Çocuk kullanımı kökün tüketiminde **bir kez** bildirilir; alt `run` notice yazmaz (`RecordQuotaAsync` çağrısı `RunRecordingAgent.Completion.cs`'nin mevcut `if (scope.Depth == 0)` kapısının İÇİNE taşındı, kapı değişmedi; MT-RET-076)
- [x] `CustomType` `RunEventCustomTypes.ReservedPrefix` altındadır ve tüketici onu taklit **edemez** (`RunEventDraftValidationTests.The_quota_threshold_reserved_type_is_rejected_by_the_public_append_path`; yalnız `internal AppendReservedAsync` bypass eder)
- [x] Üç migration seti uygulandı; `SqlTextSnapshotTests` ve üç `IntegrationTests` yeşil (PostgreSQL `0046`, SQL Server `0033`, SQLite `0033`; `SqlTextSnapshotTests` 20/20; dört `QuotaStoreContract` koşumu 28/28)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 8cff12a9`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, notice çerçevesi belgeye yazıldı (2026-09-05, `gpt-5.4-mini`; MT-RET-071/072'nin `Gerçek sonuç` alanları)
- [x] `secret` taraması boş döndü (`kapi.py tarama`, kapanış kapısının ilk adımı)
- [x] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi (MT-RET-070..076); 070/071/072 gerçek koşumla ölçüldü, 073..076 otomatik karşılıklarıyla kanıtlandı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. **Denetim Bulguları** (3 🟡 bulgu, üçü de kapatıldı)
- [x] `docs-site/` güncellendi; `npm run check` (içerik/derleme/bağlantı/ağırlık, dördü de) temiz — `concepts/governance.md`, `concepts/runs.md`, `http-api.md`, `capabilities.md`, `reference/configuration.md` güncellendi; `kalicilik` kuralı gerekçeyle geçildi (Plandan Sapmalar #6)

### Doğrulama komutları

```bash
# Sıra: notice, done'dan ÖNCE
curl -N -s "$APU/api/agents/support/run" -H "$APB" -H 'content-type: application/json' \
  -d '{"input":"merhaba"}' | grep -E '^event:'
# beklenen sıra: run … custom … done

# Aynı payload iki yoldan
curl -s "$APU/api/runs/$RUN_ID/events" -H "$APB" \
  | grep -A1 'event: custom' | grep -o '"noticeId":"[^"]*"'

# Kalıcı tekillik: host yeniden başlatıldıktan sonra ikinci run
# beklenen: ikinci akışta custom çerçevesi YOK
```

---

## Plandan Sapmalar

1. **"Doğrudan POST" DoD maddesi bir mekanizma gerektiriyordu, plan bunu somutlaştırmamıştı.** Uygulama Adım 1'de ölçüldü: `/api/agents/{name}/run`'ın SSE akışı yalnız MAF'ın kendi `AgentResponseUpdate`'lerini `update` çerçevesi olarak iletir; `scope.Writer.AppendAsync` ile yazılan hiçbir olay (mevcut tüketici-yazımlı `Custom` olayları dahil) bu akışa hiçbir zaman yansımaz — `RunRecordingAgent.RunCoreStreamingAsync`'in `CompleteAsync` çağrısı, tüketiciye `yield return` edilen SON güncellemeden SONRA, görünmez bir `MoveNextAsync` turunda çalışır. Bu, plandaki "iki yoldan aynı payload" iddiasının GERÇEK bir mekanizma gerektirdiği, plan metninde tarif edilmediği anlamına geliyordu.
   İki tasarım seçeneği değerlendirildi: (a) `AgentPrismRunOptions`'a yeni bir hook eklemek (`BeforePendingApprovalIsPublished`'in emsaliyle), (b) akış bittikten SONRA, `done` yazılmadan önce, `IRunStore.ReadEventsAsync`'i kısa bir "quota eşiği bildirimi var mı" taramasıyla çağırmak. (b) seçildi: yeni public API yüzeyi AÇMAZ (plandaki "Planlanan Public API" listesini aşmaz), "aynı payload" garantisini İNŞA yerine aynı kalıcı kaydı İKİNCİ KEZ OKUYARAK sağlar (iki bağımsız kod yolunun senkronize kalması riskini ortadan kaldırır), ve `PublishThresholdToRunStream` kapalıyken (varsayılan, K1) hiçbir ekstra sorgu ÇALIŞTIRMAZ (`AgentEndpoints.WriteQuotaThresholdNoticesAsync` seçeneği önce kontrol eder). Bedel: quota notice'ı olmayan HER streaming run'da (anahtar açıkken) fazladan bir `ReadEventsAsync(runId, 0, ...)` taraması — run başına en fazla birkaç quota notice'ı arandığından ve tarama yalnız anahtar açıkken çalıştığından kabul edildi.
2. **`RunEventWriter.AppendAsync`'in reserved-prefix reddi, kütüphanenin KENDİ notice'ını yazmasını da engelliyordu.** K-673/K-674'ün kurduğu kural ("`agentprism.` önekiyle başlayan `CustomType` reddedilir") hiçbir istisna tanımıyordu — ama bu fazın notice'ı TAM OLARAK bu önekle yazılmalıydı (RunEventCustomTypes.QuotaThreshold). Çözüm: `AppendAsync` public kalır ve reddetmeye devam eder; yeni bir `internal AppendReservedAsync` yalnız `allowReserved: true` ile aynı doğrulama+yazma çekirdeğine (`AppendCoreAsync`) girer. Tüketici bu metoda hiçbir zaman erişemez (yalnız aynı derlemedeki `RunRecordingAgent.Notifications.cs` çağırır) — K-673/K-674'ün "tüketici taklit edemez" garantisi korunur.
3. **`AgentPrismRunRecordingOptions.RecordToolPayloads=false` iken notice'ın `Payload`'ı da bastırılıyordu.** `WorkflowRequest`'in zaten kurduğu istisna deseni ("payload gözlemlenebilirlik detayı değil, işlevin kendisidir") `Custom` + rezerve önek kombinasyonuna da uygulandı — `NoticeId` olmadan istemci notice'ı ne dedup edebilir ne de gösterebilir. `RunEventDraftValidationTests.A_reserved_Custom_events_payload_survives_even_when_RecordToolPayloads_is_off` bunu kilitler.
4. **Açık Soru 4 (`QuotaConsumption.RunId` tipi) ölçüldü: `string?`.** `WebhookRunSummary.RunId`'nin zaten `string` olması ve `Notifications.cs:125`'in `scope.RunId.ToString()` yazması, plandaki A seçeneğini doğruladı — `Guid?` seçilmedi.
5. **Planın "Planlanan Public API" bloğu `IQuotaStore`'un büyümesini hiç listelemiyordu** — 146.4'ün kalıcı tekilleştirme gereksinimi mantıksal olarak yeni bir `IQuotaStore` üyesi (`TryClaimThresholdNotificationAsync`) gerektiriyordu ama plan metni yalnız `QuotaEnforcer.RecordAsync`'in kırıcı değişikliğini açıkça işaretlemişti. Bağımsız denetim bunu yakaladı (🟡 bulgu #1); K-681 genişletilerek `IQuotaStore` büyümesi de kırıcı değişiklik olarak kayda geçirildi.
6. **`dokuman-bakim.py --site-denetle`'nin `kalicilik` kuralı `getting-started/persistence.md`'nin değişmesini bekledi** (`PostgresQueries.cs` değiştiği için) — gerekçeyle `--site-gerekce-yazildi` ile geçildi. Sayfa migration **mekaniğini** (uygulanma sırası, `AutoApplyMigrations`, kilit, geri alınamaz değişiklik uyarısı) anlatır, tek tek sütun/migration numarası saymaz; bu fazın migration'ı (üç sağlayıcıda rutin bir `ALTER TABLE ADD COLUMN`, var olan onlarca örnekle aynı desende) yeni bir mekanik eklemedi — anlatılacak yeni bir şey yok.

## Bu Fazda Verilen Kararlar

- **K-680** — Kota muhasebesi artık terminal olay yazımından önce çalışır; kota bir hata sonrası geri alınmaz (146.1, kullanıcı kararı).
- **K-681** — `QuotaEnforcer.RecordAsync` geçilen eşikleri döner (kırıcı imza genişlemesi, bugün bedava).
- **K-682** — Kota eşiği tekilliği `quota_usage.notified_thresholds` sütununda kalıcı hâle gelir (CSV kodlama, koşullu `UPDATE` atomikliği).

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent, `git diff 8cff12a9` + DoD)
**🔴 bulgu bulmadı**. Promptta özellikle işaretlenen yedi risk (sıra
değişikliği, reserved-prefix bypass, never-throws sözleşmesi, üç dialect
atomikliği, K1 varsayılan-kapalı parite, tail-scan maliyeti, kapsanmayan
hata yolları) doğrulandı, hiçbiri DoD ihlaline yol açmadı.

**🟡 bulgular — üçü de bu fazda kapatıldı:**

1. `IQuotaStore.TryClaimThresholdNotificationAsync` planın "Planlanan Public
   API" listesinde yoktu → **kapatıldı**: Plandan Sapmalar #5 + K-681
   genişletildi (bkz. `Gerçekleşen Public API`'nin baş notu).
2. Fazın kendi "Hata Modları ve Testler" tablosunun iki satırı (iptal edilen
   `run`'da tüketimin yazılması; alt `run`'ın hiç tüketmemesi) teslim edilen
   testlerde karşılıksızdı — davranış doğruydu (kod okunarak doğrulandı) ama
   regresyona karşı hiçbir test korumuyordu → **kapatıldı**:
   `QuotaNoticeOrderingTests.A_canceled_run_still_records_quota_consumption`,
   `A_child_run_never_records_quota_consumption` ve kontrol vakası
   `A_root_run_records_quota_consumption` eklendi (üçü de yeşil).
3. `AgentEndpoints.WriteQuotaThresholdNoticesAsync` "tail-scan" değil, anahtar
   açıkken run'ın **tüm** olay geçmişini (`fromSequence: 0`) tarıyor —
   K1 (kapalıyken sıfır maliyet) korunuyor ama açıkken olay sayısıyla orantılı
   ek bir sorgu ekleniyor → **gerekçelendi, kapsam dışına devredildi**: bkz.
   Sonraki Faza Devir Notu ve `docs/ADAYLAR.md`'ye eklenecek aday (`IRunStore`
   filtrelenmiş okuma metodu olmadan daha ucuz bir çözüm yok; bugünkü hacimde
   run başına en fazla birkaç quota notice'ı arandığından kabul edildi).

**🟢 adaylar** — `docs/ADAYLAR.md`'ye taşındı: (1) eşik claim edildikten
SONRA webhook/akış yayını başarısız olursa o eşik dönem sonuna kadar kalıcı
kaybolur (146.4'ün "exactly-once değil" vaadinin bilinen bir uzantısı,
retry/backoff ayrı bir kalem); (2) "komşu kullanıcı notice almaz" garantisi
yapısaldır (`RunEventWriter`'ın her `run`'a özel `Guid RunId` bağlaması) ama
özel bir çok-kullanıcılı regresyon testi yok.

## Sonraki Faza Devir Notu

- **`RunEventWriter` artık iki yazma yolu taşıyor: public `AppendAsync` (rezerve önek reddeder) ve `internal AppendReservedAsync` (reddetmez).** Gelecekte kütüphanenin kendi yazacağı BAŞKA bir rezerve `CustomType` (ör. gelecekte bir "run.deadline-warning" notice'ı) aynı `AppendReservedAsync`'i kullanabilir — yeni bir bypass mekanizması İCAT ETMEYE gerek yok, `AppendCoreAsync(draft, allowReserved: true, ct)` zaten hazır.
- **`RunEventWriter.AppendAsync`'in Payload gating istisnası artık ÜÇ dal taşıyor** (`RecordToolPayloads` · `WorkflowRequest` · `allowReserved && Custom`). Yeni bir "payload işlevin kendisidir" durumu eklenirse bu üçlü koşulu genişlet, yeni bir ayrı `if` bloğu açma.
- **`AgentEndpoints.WriteQuotaThresholdNoticesAsync`'in tarama deseni** (`IOptionsMonitor` kontrolü + `IRunStore.ReadEventsAsync(runId, 0, ct)` tam taraması) yalnız `agentprism.quota.threshold` CustomType'ını arar — GENEL bir "tüm Custom olaylarını direct stream'e yansıt" mekanizması İSTENMEDİ (kapsam dışı bırakıldı, Plandan Sapma #1). İleride başka bir rezerve notice de direct stream'e yansıtılmak istenirse bu metot GENELLEŞTİRİLEBİLİR (CustomType listesi parametre yapılabilir) — bugün tek bir tüketici olduğundan somutlaştırılmadı.
- **`RunScope` (private, `RunRecordingAgent.Lifecycle.cs`) artık `UserId` taşıyor.** `RunStart.UserId`'den `CreateScope`'ta kopyalanır — bu alanı okuyan yeni bir kod yolu eklenirse `start.UserId`'nin kaynağı `ResolveAttribution` (K-283 ailesi, `IRunAttributionContext`) olduğunu unutma; ambient bir bağlamdan DOĞRUDAN okumak yerine hep bu alan üzerinden geç.
- **Kalan açık uç yok.** Planın 4 açık sorusu da uygulama sırasında ölçülüp kapatıldı (Plandan Sapmalar #4, ve Açık Soru 1/2/3 planın önerdiği gibi uygulandı: `text` sütun, liste dönüşü, metrik başına ayrı notice).
