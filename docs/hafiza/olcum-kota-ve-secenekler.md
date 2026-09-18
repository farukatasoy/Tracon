# Olcum, Kota ve Secenek Baglama Tuzaklari

> Metrik/sayac yazimi, maliyet toplama, kota kapisi, `secret` gizlilik suzgeci
> ve `Bind()` secenek baglama. Calistirma yolunun KENDISI (RunRecording zinciri,
> `scope`/span, olay yazimi, iptal) icin: [`cekirdek-calistirma.md`](cekirdek-calistirma.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `cekirdek-calistirma.md` 15.999/16.000 B'ye ulasmisti (%0
> bosluk). Olcum ekseni calistirma yolundan BAGIMSIZ buyuyor -- her gozlemlenebilirlik
> fazi ona ekliyordu. K-214 merdiveni: gercek bolunme.

## Secenek baglama

- **🚨 Yapılandırma bağlamada erken dönüş sonraki bölümleri yutar** (2026-08-02): `Bind` metodu `RunRecording` yoksa `return` ediyordu; `Observability` hiç okunmadı ve `Tracon__Observability__SuccessSampleRatio=1` sessizce yok sayıldı. Her alt bölüm **kendi varlığından** sorumlu olmalı (`BindRunRecording` / `BindObservability`).
- **🚨 Alan eklenip `Bind()`'a eklenmeyi 3× unutuldu; standalone `IOptionsMonitor<TNested>` içiçe seçenekten kopar** (2026-08-14): `RecordRunInput`(K-406)/`IncludeAgentVersionTag`/`Validation.McpTimeout`(K-253); çözüm `TraconOptionsBindingCoverageTests.cs`. `QuotaUsageObserver` de `Configure<>`siz `IOptionsMonitor<TraconObservabilityOptions>` alıyordu; `SectionName`'siz türü standalone enjekte etme.
- **🚨 Bir seçeneğin geçerli ARALIĞI çalışma anındaki bir değişmezi kırıyorsa, o aralık YAZIYLA değil `IValidateOptions` ile korunur** (2026-09-09, K-743): Kural: **yenileme aralığı, yenilediği geçerlilik penceresinin içinde kalmalıdır**; iki sabit tek yerde durur (`SingletonGuard.MinimumRenewInterval` · `MinimumLeaseDuration`) ve bir test onları birbirine kilitler. Aynı sınıfın doğru yazılmış örneği: `RunReconciliationOptionsValidator`'ın `OrphanThreshold >= HeartbeatInterval` kontrolü. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Gizlilik suzgeci

- **🚨 `secret` filtresinde alt dize eslemesi cogul/tekil ayrimi gozetmezse YANLIS alanlari gizler** (Faz 9, K-081): `model.maxOutputTokens` gercek bir denetim kaydinda `***` oldu ("token" fragmani "Tokens"i esledi); sentetik veri kullanan birim testleri yakalamadi. `IsSecretKey` artik "tokens" (cogul) iceren anahtarda eslesmeyi iptal eder. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `AuditSecretFilter` alan ADINA bakar, DEGERE degil** (Faz 65): adinda `apikey`/`authorization`/`token`/`password`/`secret` gecen HER alan, icerigi zararsiz olsa da `***` olur ve teshis degeri sessizce kaybolur. Cozum alani degistirmek degil, denetim ozetinde farkli adlandirmaktir (`configKeyName`). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Metrik ve maliyet

- **🚨 Eval calistirmalari `RunKind.Eval` ile isaretlenmezse `/api/stats`'i kirletir** (2026-08-03, Faz 18, K-141): zincir `TraconRunOptions.Kind` → `RunStart.Kind` → `RunStartInfo.Kind`; `IRunStore.GetStatisticsAsync` bu turu filtreler. Yeni bir `RunKind` degeri tasiyan ozellik eklerken zincirin TAMAMINI guncelle. Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Metrik etiketini kosullu eklerken `TagList` kullan, `KeyValuePair<string,object?>[]` degil** (Faz 19): `Counter<T>.Add`/`Histogram<T>.Record`'un sabit-uzunluklu `params` asiri yuklemesi yerine `System.Diagnostics.TagList` (struct) kullanilir. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Tool'un token DISI olcumu cagri kimligiyle kayda baglanir** (2026-08-05, Faz 28): kimlik `FunctionInvokingChatClient.CurrentContext.CallContent.CallId`'den okunur — tool govdesinde DOLUDUR; `AIFunctionArguments.Context` `null` gelir ve kimligi TASIMAZ. Baglanamazsa `false` doner, tool'un isi bozulmaz. Zincir: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ObservableGauge` geri cagirmasi es zamanlidir — icine `await` konamaz** (Faz 35, K-256): veritabani okuyan bir olcer `SemaphoreSlim` korumali, `TimeProvider` karsilastirmali bir onbellekle yazilir (`QuotaUsageObserver.Snapshot()` deseni). `BackgroundService`/`PeriodicTimer` denendi ve test edilemez cikti — gerekce: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`TraconMetrics`'e yeni bir sayac eklerken `CompleteAsync` icindeki cagriyi `scope.Depth`'e KOSULLU yapma — `RecordRun` gibi HER calistirmada (kok + alt) cagir** (Faz 35): `RecordCost` `if (scope.Depth == 0)` blogunun DISINDA durur ve K-151'in "sayac yalniz KENDI maliyetini yayar" kuralini dogal olarak saglar. Kota/olay yayini (`RecordQuotaAsync`/`PublishRunEventAsync`) ise KASITLI olarak yalniz kokte calisir — iki kural KARISTIRILMAMALI.
- **🚨 Elle yazilmis `InputCost + OutputCost` toplami ucuncu bir terim eklenince SESSIZCE eksik raporlar — denetim YEDI yerde buldu** (Faz 68, K-483): cache ucreti `input_cost`'un alt kumesi DEGIL ucuncu terimdir; maliyet tavani olan bir kiraci tavani asabilirdi. `RunCost.Total()` tek cevaptir — alan eklerken `Total()`'i guncelle, `grep -rn "InputCost ?? 0" src/` ile sinifi tara. Kapi: `Every_cost_total_includes_the_cache_charge`. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bir sayacin `null` gelmesi ile `0` gelmesi AYRI bilgidir** (Faz 68, K-482): `0` "olculdu, yoktu" IDDIASIDIR ve susan saglayici icin kendinden emin %0 cache isabeti uretir. Uc nokta: `MergeUsage` (`AddOrNull`), `CompactionUsageAccumulator` (sayac basina bildirim bayragi), `TreeUsage` (`COALESCE` KASITLI yok). Fiyatta da: bildirilmediyse ucret `null`. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Sikistirma (context compaction) ozetleme cagrisi ANA agent turlariyla AYNI `ModelProviderRegistry.BuildPipeline` boru hattindan gecer** (2026-08-26, Faz 114, kod okunarak olculdu): `ResolveSummarizationChatClient` → `CreateChatClient(definition, binding)` → `_models.CreateChatClient(binding)` — AYRI/basitlestirilmis bir istemci degil, `UseFunctionInvocation`/cache/telemetri dahil TAM boru hatti. Sonuc: boru hattinin icine konan HER halka (butce, cache, guard) sikistirma cagrisini da GORUR — "bu halka yalniz ana turu mu goruyor, sikistirmayi da mi" sorusu ayri bir olcum GEREKTIRMEZ, YANITI zaten HAYIR'dir (ikisini de gorur). `RunRecordingAgent.Completion.cs`'nin `scope.ExtraUsage` (compaction yan kanali) toplamini AYRICA `RunBudget`'a eklemesi bu yuzden CIFTE SAYIM olurdu — K-632 bu satiri kaldirdi.

- **🚨 Bir sayacin "bir kez artar" iddiasini SIRALI bir test KANITLAMAZ** (2026-09-15, Faz 173, K-782): **bir es zamanlilik korumasi eklerken, o korumanin kaldirilmasiyla dusen bir test yaz** — yoksa ne korumanin gerekli oldugunu ne de calistigini bilirsin. Cozum yarisi ZORLAMAKTIR (`GatedFailingRunStore` deseni: cagiranlari depo icinde tut, hepsi kontrolu gectikten SONRA birlikte dusur). `RunEventWriter.Disable` vakasi: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `dotnet-counters` `dotnet run`'in PID'ine baglanirsa SESSIZCE bos doner** (2026-09-15, Faz 173): gercek uygulama ayri bir alt surectir; PID `dotnet-counters ps` ciktisindaki `Tracon.Api` satirindan alinir. `collect` icin `--duration` kullan; `pkill` ile durdurulan oturum dosyayi HIC yazmaz. Belirti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Etiket degerini bir sabite cevirmek yetmez — LOG cumlesi de ayni sabitten uretilmelidir** (2026-09-15, Faz 173): sabit kumesi ve cumle eslemesi AYNI tipte yasar (`RunRecordingStages.All` + `Describe`) ve bir test her `All` uyesinin KENDINE AIT bir cumlesi oldugunu iddia eder. Ayri tutmak "etiketi olan ama cumlesi olmayan" bir asamaya izin verirdi. Gerekce: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Kota

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

**İki okuyucu tek kural uygular.** Uyarı ile rapor aynı `UnpricedModels.Find`
metodunu çağırır. Aynı mantığın iki kopyası kaçınılmaz olarak kayar ve
raporu uyarıyı doğrulamak için açan operatör farklı bir liste görür.

**Sınıf taraması (2026-09-18):** `grep -rn "PricingSource.Unknown" src/` —
diğer okuyucular (`RunRecordingAgent.Completion`, `RunCostRecalculationService`,
`OnlineEvalSummaryService`, `QuotaTypes`) durumu zaten doğru ele alıyor; hiçbiri
sıfır yazmıyor. `RunCostRecalculationService` fiyat sonradan girilince geçmişi
geri hesaplar, yani bu kusurda **veri kaybı yoktur** — `HATA-S1-025`'ten (tool
harcamasının kalıcı kaybı) ayıran şey budur.
