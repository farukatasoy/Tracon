# Olcum, Kota ve Secenek Baglama Tuzaklari

> Metrik/sayac yazimi, maliyet toplama, kota kapisi ve bir raporun dogruyu
> soylemesi. `Bind()` secenek baglama ve `secret` gizlilik suzgeci Faz 174'te
> AYRILDI: [`secenek-baglama-ve-gizlilik.md`](secenek-baglama-ve-gizlilik.md).
> Calistirma yolunun KENDISI (RunRecording zinciri,
> `scope`/span, olay yazimi, iptal) icin: [`cekirdek-calistirma.md`](cekirdek-calistirma.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `cekirdek-calistirma.md` 15.999/16.000 B'ye ulasmisti (%0
> bosluk). Olcum ekseni calistirma yolundan BAGIMSIZ buyuyor -- her gozlemlenebilirlik
> fazi ona ekliyordu. K-214 merdiveni: gercek bolunme.

## Metrik ve maliyet

- **🚨 Eval calistirmalari `RunKind.Eval` ile isaretlenmezse `/api/stats`'i kirletir** (2026-08-03, Faz 18, K-141): zincir `TraconRunOptions.Kind` → `RunStart.Kind` → `RunStartInfo.Kind`; `IRunStore.GetStatisticsAsync` bu turu filtreler. Yeni bir `RunKind` degeri tasiyan ozellik eklerken zincirin TAMAMINI guncelle. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Metrik etiketini kosullu eklerken `TagList` kullan, `KeyValuePair<string,object?>[]` degil** (Faz 19): `Counter<T>.Add`/`Histogram<T>.Record`'un sabit-uzunluklu `params` asiri yuklemesi yerine `System.Diagnostics.TagList` (struct) kullanilir. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Tool'un token DISI olcumu cagri kimligiyle kayda baglanir** (2026-08-05, Faz 28): kimlik `FunctionInvokingChatClient.CurrentContext.CallContent.CallId`'den okunur — tool govdesinde DOLUDUR; `AIFunctionArguments.Context` `null` gelir ve kimligi TASIMAZ. Baglanamazsa `false` doner, tool'un isi bozulmaz. Zincir: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ObservableGauge` geri çağırması I/O BEKLEMEZ** (2026-09-23, K-256'nın yerini aldı): geri çağırma MeterProvider'ın TÜM enstrümanlarını toplayan thread'de koşar. Eski "bayatsa bloklayarak tazele" deseni yavaş bir DB'de bütün ihracı bekletiyordu. Desen: `CachedGaugeSource<T>` — `BackgroundService` + `PeriodicTimer(aralık, TimeProvider)`, başta bir kez tazeler, `SchemaReadyGate`'i bekler; ilk tazelemeden önce ölçüm yoktur. K-256'nın "test edilemez" öncülü yanlıştı: bu ctor net8.0'da vardır, `TriggerableTimeProvider` `CreateTimer`'ı override eder. Test ilk tazelemeyi `CompletedRefreshes` ile bekler; beklemeyen test boşuna geçer. `PeriodicTimer` yalnız 1 ms–~49,7 gün alır; `<= TimeSpan.Zero` doğrulaması 0,5 ms'yi geçirir ve servis açılıştan sonra düşer.
- **`TraconMetrics`'e yeni bir sayac eklerken `CompleteAsync` icindeki cagriyi `scope.Depth`'e KOSULLU yapma — `RecordRun` gibi HER calistirmada (kok + alt) cagir** (Faz 35): `RecordCost` `if (scope.Depth == 0)` blogunun DISINDA durur ve K-151'in "sayac yalniz KENDI maliyetini yayar" kuralini dogal olarak saglar. Kota/olay yayini (`RecordQuotaAsync`/`PublishRunEventAsync`) ise KASITLI olarak yalniz kokte calisir — iki kural KARISTIRILMAMALI.
- **🚨 Elle yazilmis `InputCost + OutputCost` toplami ucuncu bir terim eklenince SESSIZCE eksik raporlar — denetim YEDI yerde buldu** (Faz 68, K-483): cache ucreti `input_cost`'un alt kumesi DEGIL ucuncu terimdir; maliyet tavani olan bir kiraci tavani asabilirdi. `RunCost.Total()` tek cevaptir — alan eklerken `Total()`'i guncelle, `grep -rn "InputCost ?? 0" src/` ile sinifi tara. Kapi: `Every_cost_total_includes_the_cache_charge`. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bir sayacin `null` gelmesi ile `0` gelmesi AYRI bilgidir** (Faz 68, K-482): `0` "olculdu, yoktu" IDDIASIDIR ve susan saglayici icin kendinden emin %0 cache isabeti uretir. Uc nokta: `MergeUsage` (`AddOrNull`), `CompactionUsageAccumulator` (sayac basina bildirim bayragi), `TreeUsage` (`COALESCE` KASITLI yok). Fiyatta da: bildirilmediyse ucret `null`. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Sikistirma (context compaction) ozetleme cagrisi ANA agent turlariyla AYNI `ModelProviderRegistry.BuildPipeline` boru hattindan gecer** (2026-08-26, Faz 114, kod okunarak olculdu): `ResolveSummarizationChatClient` → `CreateChatClient(definition, binding)` → `_models.CreateChatClient(binding)` — AYRI/basitlestirilmis bir istemci degil, `UseFunctionInvocation`/cache/telemetri dahil TAM boru hatti. Sonuc: boru hattinin icine konan HER halka (butce, cache, guard) sikistirma cagrisini da GORUR — "bu halka yalniz ana turu mu goruyor, sikistirmayi da mi" sorusu ayri bir olcum GEREKTIRMEZ, YANITI zaten HAYIR'dir (ikisini de gorur). `RunRecordingAgent.Completion.cs`'nin `scope.ExtraUsage` (compaction yan kanali) toplamini AYRICA `RunBudget`'a eklemesi bu yuzden CIFTE SAYIM olurdu — K-632 bu satiri kaldirdi.

- **🚨 Bir sayacin "bir kez artar" iddiasini SIRALI bir test KANITLAMAZ** (2026-09-15, Faz 173, K-782): **bir es zamanlilik korumasi eklerken, o korumanin kaldirilmasiyla dusen bir test yaz** — yoksa ne korumanin gerekli oldugunu ne de calistigini bilirsin. Cozum yarisi ZORLAMAKTIR (`GatedFailingRunStore` deseni: cagiranlari depo icinde tut, hepsi kontrolu gectikten SONRA birlikte dusur). `RunEventWriter.Disable` vakasi: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `dotnet-counters` `dotnet run`'in PID'ine baglanirsa SESSIZCE bos doner** (2026-09-15, Faz 173): gercek uygulama ayri bir alt surectir; PID `dotnet-counters ps` ciktisindaki `Tracon.Api` satirindan alinir. `collect` icin `--duration` kullan; `pkill` ile durdurulan oturum dosyayi HIC yazmaz. Belirti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Etiket degerini bir sabite cevirmek yetmez — LOG cumlesi de ayni sabitten uretilmelidir** (2026-09-15, Faz 173): sabit kumesi ve cumle eslemesi AYNI tipte yasar (`RunRecordingStages.All` + `Describe`) ve bir test her `All` uyesinin KENDINE AIT bir cumlesi oldugunu iddia eder. Ayri tutmak "etiketi olan ama cumlesi olmayan" bir asamaya izin verirdi. Gerekce: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Kota

- **🚨 Süreç içi "bir kez" kümesi zaman anahtarlıysa budanır; kalıcı claim atarsa anahtar geri alınır** (2026-09-23): `QuotaEnforcer._firedThresholds` dönem başı taşıyan anahtarı hiç silmiyordu ve singleton süreç ömrü boyunca büyüdü. Anahtar claim'den ÖNCE ekleniyordu; claim atınca (iptal dahil) eşik dönem sonuna kadar o süreçte sustu. Kalıcı claim (K-682) tek doğru kaynak olduğu için budama çift yayın üretmez. Desen: `(dönem, dönem başı)` kovası, kapanan kovanın fırsatçı silinmesi, claim hatasında `TryRemove`.
- **Kota ve olay yayini YALNIZ kok calistirmada isler** (2026-08-03, Faz 21): `RunRecordingAgent.CompleteAsync` icinde `scope.Depth == 0` kosuluyla korunur. Gerekce: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`QuotaGate` `RunRecordingAgent`'a ulasmadan `429` doner — kota (`IQuotaStore`/`QuotaEnforcer`) asimi bir `RunError` URETMEZ** (2026-08-07, Faz 44, K-297): K-162'nin sonucu, hala gecerli. 🚨 **AMA `RunErrorClass.QuotaExceeded` artik BASKA bir yoldan da dolar** (2026-08-26, Faz 114, K-630): calistirma-ici bir agac butcesi (`AgentGraph.MaxTotalTokens`/`MaxTotalCost`) tukendiginde `RunBudgetChatClient`'in attigi `TraconRunBudgetExceededException` de AYNI sinifa (`4`) eslenir — bu, `QuotaGate`'ten TAMAMEN bagimsiz, farkli bir mekanizmadir (kiraci/agent kotasi degil, tek bir run agacinin bütçesi). "QuotaExceeded asla dolmaz" iddiasi artik YANLIS; "kota (`IQuotaStore`) asla bir `RunError` uretmez" iddiasi hala DOGRU — ikisini karistirma.
- **🚨 Bir olayi ILGILI `run`'in KENDI SSE akisina yazmak, olayi otomatik olarak o akisa YANSITMAZ — akis tipine gore degisir** (2026-09-05, Faz 146): `scope.Writer.AppendAsync`/`AppendReservedAsync` ile yazilan HER olay `GET /api/runs/{id}/events` ucunda GORUNUR (o uc `IRunStore.ReadEventsAsync`'i canli okur), ama DOGRUDAN `POST /api/agents/{name}/run` akisinda GORUNMEZ — o akis yalniz MAF'in kendi `AgentResponseUpdate`'lerini `update` cercevesi olarak iletir, `RunEventWriter` yazimlarindan tamamen bagimsizdir. Yeni bir "olayi iki yolda da gorunur yap" ihtiyaci dogarsa `WriteQuotaThresholdNoticesAsync` desenini tekrarla (kalici kaydi IKINCI KEZ oku, kopya insa etme), yeni bir hook ACMA. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `RunEventWriter.AppendAsync`'in rezerve-onek reddi (K-673/K-674) kutuphanenin KENDI notice'ini yazmasini da engeller — bypass INTERNAL bir ikinci giris noktasindan gecer** (2026-09-05, Faz 146): `AppendCoreAsync(draft, allowReserved, ct)` tek govde, `AppendAsync` (`allowReserved: false`, public) ve `AppendReservedAsync` (`allowReserved: true`, `internal`) iki ince sarmalayicidir. Tuketici asla `AppendReservedAsync`'e erisemez (ayni derleme disinda gorunmez) — K-673/K-674'un "tuketici taklit edemez" garantisi boylece korunur. Yeni bir rezerve `CustomType` (kutuphanenin kendi yazacagi baska bir notice) gerekirse AYNI `AppendReservedAsync`'i kullan, yeni bir bypass ICAT ETME.

## Yargic skorunun olcegi (Faz 155)

**🚨 Bir yargic artik N adli skor dondurur ve hepsi ayni olcekte DEGILDIR.**
`OnlineEvalSummaryService.RecordScoreAsync` ve `TraconMetrics.RecordJudgeScore`
**0-100** tanimlidir; `OnlineEvaluationOptions.LowScoreThreshold` varsayilan **60**
o olcekte karsilastirilir. Microsoft'un kalibre evaluator'lari ise **1-5** verir.

Kural (K-730): pencereye ve histograma yalnizca **mansset** skor girer --
adi yargicin adina **esit** VE `Kind == RunScoreKind.Numeric` olan skor.
`OnlineEvalJobHandler.JudgeOneAsync`'te tek yerdedir.

Iki tuzak, ikisi de bagimsiz denetimde bulundu:

- **Ad yetmez, `Kind` de gerekir.** Ilk yazim yalnizca ada bakiyordu. Bir yargic
  genel kararini `Stars` olarak verebilir (sevk edilen rehber bunu aciktan davet
  ediyor); 1-5 olceginde bir **5**, 60 esiginin altina duser ve `run.score.low`
  webhook'u **her saglikli kosumda** calar. Yani "kotu skor alarmi" saglam bir
  ajanda surekli oter ve insanlar onu susturmayi ogrenir.
- **Olcek tahmini yapma.** Kural bir AD KARSILASTIRMASIDIR, bir deger sezgisi
  degil. "1 ile 5 arasindaysa yildizdir" gibi bir tahmin yanlis siniflandirir;
  0-100 olceginde mesru bir 3 puan vardir.

Yeni bir yargic eklerken: mansset skorunu **0-100 `Numeric`** ver, adini yargicin
adiyla ayni yap. Baska bir olcek kullanacaksan ona **baska bir ad** ver -- saklanir,
`GET /api/evaluation/scores/summary` onu `(name, kind)` kiriliminda raporlar,
yalnizca canli ortalamaya girmez.

## Yarim yazilmis skor kumesi (Faz 155)

**🚨 Bir yargic N satir yaziyorsa, k'inci satirdaki store hatasi KALICI bir
eksiklik uretir.** Retry checkpoint'i (K-638) "bu yargicin `author`'uyla bir satir
var mi" diye sorar, kac satir oldugunu sormaz. Hayatta kalan tek satir yargici
retry'da **atlatir**: kalan metrikler bir daha hic yazilmaz, is `Completed`
raporlanir ve hicbir `JudgeFailure` gorunmez -- sessiz veri kaybi.

Cozum telafidir, transaction degil: yazma dongusu `try/catch` icindedir ve hata
halinde `CompensateAsync` o denemede yazilan satirlari **geri alir**, boylece
retry temiz sayfadan baslar (`UpsertAsync` idempotent oldugu icin yeniden yazim
guvenlidir). Iki ayrinti:

- Temizlik **iptal edilemez** (`CancellationToken.None`). Iptal edilen bir
  temizlik, tam da onlemek icin var oldugu yarim kumeyi birakirdi.
- Temizlik de basarisiz olursa **loglanir, firlatilmaz**. Store zaten yazmayi
  reddediyor; orada firlatmak raporlanan bir yargic hatasini bir is cokusune
  cevirirdi.

**Sinif taramasi:** "N kayit yaz, sonra bir checkpoint'e guven" deseni her yerde
ayni tuzagi tasir. Checkpoint kismi bir kumeyi tam bir kumeden ayirt edemiyorsa,
ya telafi yaz ya da checkpoint'e tamlik bilgisi ekle.

## 🚨 Süreç API'si makineyi anlatmaz — bir rapor alanı ne okuduğunu söyler (F-220)

Kural: bir rapor alanı makine büyüklüğü iddia ediyorsa donanımı okur
(`sysctl -n machdep.cpu.brand_string`/`hw.memsize`, `/proc/cpuinfo`
`model name` + `/proc/meminfo` `MemTotal`); okuyamıyorsa alan adında
`(process-visible)` etiketiyle ne olduğunu söyler. Okuma yolu **yumuşak düşer**
— rapor bir kapı değildir (K-738), eksik satır koşumu kırmaz.

**Sınıf taraması:** `grep -rn "Environment.ProcessorCount\|GetGCMemoryInfo"
src/ tests/` — başka vaka yok. `Environment.MachineName`'in `SingletonGuard` ve
`JobWorkerBackgroundService`'teki kullanımları bir **sahip kimliğidir**, makine
kapasitesi iddiası değil; vaka sayılmaz.

## 🚨 Dürüst bir "bilmiyorum" bir süre sonra kırık bir rapordan ayırt edilemez

`RunPricingResolver` fiyat hiçbir kaynakta yoksa `PricingSource.Unknown` yazar
ve maliyet sütunlarını **boş** bırakır — sıfır değil, çünkü sıfır modelin
bedava olduğunu iddia ederdi. Mekanizma doğrudur ve öyle kalır. Kusur
(`HATA-S1-010`) mekanizmada değil **sessizliğindeydi**: örnek kurulumun 13
modelinin hiçbirinde fiyat yoktu, yani her `run` maliyetsiz kaydediliyordu ve
sebebini öğrenmenin tek yolu bir satırdaki `pricing_source` enum'unu okumaktı.
Operatörün gördüğü şey kırık bir maliyet raporundan ayırt edilemezdi.

**Kural:** `null` ile "ölçülmedi"yi ayıran her alan için, o durumun **kalıcı**
hâle gelebileceği bir kurulum var mı diye sor. Varsa durumu bir kez söyle —
açılışta bir `Warning` (`UnpricedModelWarningService`, Aile A'nın
`PreservedStoreRegistrationWarningService` emsali) ve `/api/diagnostics`'te bir
alan (Aile F'nin `runRecording` emsali). K-808.

🚨 **Aynı sınıfın ikinci ölçülmüş vakası: `/health`** (2026-09-18, Aile M,
K-822). Hiçbir şey bir model sağlayıcısını kendiliğinden problamaz —
`ModelProviderHealthCache` yalnız `GET /api/models/health` ya da
`Tracon:Health:BackgroundInterval` ile dolar, ve **gerçek bir sohbet çağrısı
ona yazmaz** (ölçüldü: başarılı bir `run`'dan sonra beş sağlayıcının beşi
`Unknown`). `TraconHealthCheck` "hiç ölçülmedi" ile "ölçüldü ve bozuk"u tek
dala koyuyordu, yani doğru kurulmuş bir uygulama kalıcı `Degraded` raporluyordu
ve operatör gerçek bir bozulmayı bu gürültüden ayıramıyordu. Durum artık yalnız
**bilinen** arızayı yansıtır; ölçümün yokluğu mesajda söylenir. **Üç durumlu
bir göstergede "bilmiyorum" ile "bozuk" ayrı dallardır** — yukarıdaki kuralın
`null` yerine ENUM ile yazılmış hâli.

**İki okuyucu tek kural uygular.** Uyarı ile rapor aynı `UnpricedModels.Find`
metodunu çağırır. Aynı mantığın iki kopyası kaçınılmaz olarak kayar ve
raporu uyarıyı doğrulamak için açan operatör farklı bir liste görür.

**Sınıf taraması (2026-09-18):** `grep -rn "PricingSource.Unknown" src/` —
diğer okuyucular (`RunRecordingAgent.Completion`, `RunCostRecalculationService`,
`OnlineEvalSummaryService`, `QuotaTypes`) durumu zaten doğru ele alıyor; hiçbiri
sıfır yazmıyor. `RunCostRecalculationService` fiyat sonradan girilince geçmişi
geri hesaplar, yani bu kusurda **veri kaybı yoktur** — `HATA-S1-025`'ten (tool
harcamasının kalıcı kaybı) ayıran şey budur.
- **`ITenantStore` kaydi zorunlu DEGILDIR; kayitsiz kiracinin verisi `ListAsync()` taramasinda GORUNMEZ** (Faz 35, K-257): `QuotaUsageObserver` yalniz KAYITLI kiracilari tarar. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bucket araligi `Variants`'in fiziksel sirasina bagliysa, bir kolun agirligini degistirmek DIGER kollarin araligini kaydirir ve var olan atamalari bozar** (Faz 56, K-374): degisken agirlikli kolun araligi konumdan bagimsiz sabit bir uca ankorlanmali.
