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
- **🚨 `ObservableGauge` geri çağırması I/O BEKLEMEZ** (2026-09-23, K-256'nın yerini aldı): geri çağırma MeterProvider'ın tüm enstrümanlarını toplayan thread'de koşar. Desen `CachedGaugeSource<T>` (`BackgroundService` + `PeriodicTimer(aralık, TimeProvider)`, başta bir kez tazeler); ilk tazelemeden önce ölçüm yoktur. Test ilk tazelemeyi `CompletedRefreshes` ile bekler. Tam metin: `docs/arsiv/HAFIZA-GECMISI.md` § Faz 199.
- **`TraconMetrics`'e yeni bir sayac eklerken `CompleteAsync` icindeki cagriyi `scope.Depth`'e KOSULLU yapma — `RecordRun` gibi HER calistirmada (kok + alt) cagir** (Faz 35): `RecordCost` `if (scope.Depth == 0)` blogunun DISINDA durur ve K-151'in "sayac yalniz KENDI maliyetini yayar" kuralini dogal olarak saglar. Kota da HER run'da yazilir (Faz 205); yalniz olay yayini (`PublishRunEventAsync`) ve ornekleme kokte kalir — iki kural KARISTIRILMAMALI.
- **🚨 Elle yazilmis `InputCost + OutputCost` toplami ucuncu bir terim eklenince SESSIZCE eksik raporlar — denetim YEDI yerde buldu** (Faz 68, K-483): cache ucreti `input_cost`'un alt kumesi DEGIL ucuncu terimdir; maliyet tavani olan bir kiraci tavani asabilirdi. `RunCost.Total()` tek cevaptir — alan eklerken `Total()`'i guncelle, `grep -rn "InputCost ?? 0" src/` ile sinifi tara. Kapi: `Every_cost_total_includes_the_cache_charge`. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bir sayacin `null` gelmesi ile `0` gelmesi AYRI bilgidir** (Faz 68, K-482): `0` "olculdu, yoktu" IDDIASIDIR ve susan saglayici icin kendinden emin %0 cache isabeti uretir. Uc nokta: `MergeUsage` (`AddOrNull`), `CompactionUsageAccumulator` (sayac basina bildirim bayragi), `TreeUsage` (`COALESCE` KASITLI yok). Fiyatta da: bildirilmediyse ucret `null`. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Sıkıştırma özetleyicisi ANA agent turlarıyla AYNI `BuildPipeline`'dan geçer** (2026-08-26, Faz 114): boru hattına konan HER halka (bütçe, cache, guard) özetleme çağrısını da görür; `scope.ExtraUsage` toplamını bütçeye ayrıca eklemek ÇİFTE SAYIM olurdu (K-632). Tam metin: `docs/arsiv/HAFIZA-GECMISI.md` § Faz 199.

- **🚨 Bir sayacin "bir kez artar" iddiasini SIRALI bir test KANITLAMAZ** (2026-09-15, Faz 173, K-782): **bir es zamanlilik korumasi eklerken, o korumanin kaldirilmasiyla dusen bir test yaz** — yoksa ne korumanin gerekli oldugunu ne de calistigini bilirsin. Cozum yarisi ZORLAMAKTIR (`GatedFailingRunStore` deseni: cagiranlari depo icinde tut, hepsi kontrolu gectikten SONRA birlikte dusur). `RunEventWriter.Disable` vakasi: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `dotnet-counters` `dotnet run`'in PID'ine baglanirsa SESSIZCE bos doner** (2026-09-15, Faz 173): gercek uygulama ayri bir alt surectir; PID `dotnet-counters ps` ciktisindaki `Tracon.Api` satirindan alinir. `collect` icin `--duration` kullan; `pkill` ile durdurulan oturum dosyayi HIC yazmaz. Belirti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Etiket degerini bir sabite cevirmek yetmez — LOG cumlesi de ayni sabitten uretilmelidir** (2026-09-15, Faz 173): sabit kumesi ve cumle eslemesi AYNI tipte yasar (`RunRecordingStages.All` + `Describe`) ve bir test her `All` uyesinin KENDINE AIT bir cumlesi oldugunu iddia eder. Ayri tutmak "etiketi olan ama cumlesi olmayan" bir asamaya izin verirdi. Gerekce: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Token dışı tutar AYRI bir toplamdır; para kararı veren her yer yalnız `RunSpend.Total(...)`** (2026-10-13, Faz 199, K-972): `RunNonTokenCost` `RunCost.Total()`'ın terimi DEĞİLDİR. Beş yer çağırır (agent kotası + webhook'u, workflow kotası + webhook'u, bütçe `RecordUsage(0, tutar)`), yer başına test `NonTokenSpendTests`. Araç ölçümü **rapor anında** girer (`TraconToolUsage.Report`, çağrı kimliğiyle, son rapor kazanır): akışsız run araç kaydını son turdan sonra okur, sonra girerse bütçe sonraki turu kesemez. `grep -rn "\.Total()" src/`.
- **🚨 Çok run'lı toplamın para birimi etiketi `MAX(...)`/ilk değerdi; 1 USD + 1 EUR "2 USD" okunuyordu** (2026-10-14, F-419, K-976): kural `CurrencyTotals`'ta, SQL sayımı `SqlQueriesBase.CostCurrencyCount`/`DescendantCurrencyCount` (yalnız tutarı olan satır, `UPPER`). Ağaç sorgusu kökü dışarıda bırakır; okuyucu kökün para birimini ekler. Yeni toplam yazarken `CurrencyTotals`'tan geç; kapı `RunStoreContract.No_cost_total_adds_amounts_in_two_currencies` (dört depo). 🚨 Beşinci depo çözümün DIŞINDADIR: `samples/Tracon.Samples.FileRunStore` yalnız yayınlanan paketlere bağlıdır, `internal` `CurrencyTotals`'ı göremez ve kendi sayımını (`CurrencySet`) yazar; sözleşmeye case ekleyen faz onu yalnız `kapi.py yayin --kuru` koşusunda görür (F-502: case geldi, örnek düştü, prova kırmızıydı). Sözleşmeye case ekledikten sonra örneği hemen koş: `samples/Tracon.Samples.FileRunStore.Tests` (`kapilar.md` § `ic-dongu`).

## Kota

- **🚨 Süreç içi "bir kez" kümesi zaman anahtarlıysa budanır; kalıcı claim atarsa anahtar geri alınır** (2026-09-23): `QuotaEnforcer._firedThresholds` dönem başı taşıyan anahtarı hiç silmiyordu ve singleton süreç ömrü boyunca büyüdü. Anahtar claim'den ÖNCE ekleniyordu; claim atınca (iptal dahil) eşik dönem sonuna kadar o süreçte sustu. Kalıcı claim (K-682) tek doğru kaynak olduğu için budama çift yayın üretmez. Desen: `(dönem, dönem başı)` kovası, kapanan kovanın fırsatçı silinmesi, claim hatasında `TryRemove`.
- **Kota HER run'da yazilir; kimlik agacin butcesindedir** (2026-10-14, Faz 205, K-990): kok `AgentRunBudget.Account`'u (`TreeAccount`: kiraci, kok adi, kullanici, `IQuotaRecorder`) `PrepareRun`'da her seferinde YENIDEN atar; cocuk (`Runs = 0`), workflow adimi ve gec tool girdisini oradan yazar. Arka plan cocugu kok bittikten sonra biter ve ortam kiraci/kullanicisi kokunki degildir (ortamdan okuyan surumde `A_late_childs_entry_goes_to_the_roots_tenant` kirmizi). Tek yazim yolu `TreeQuota.RecordAsync` (kapi `QuotaWriteSiteTests`). `run.*` webhook'u ve ornekleme yalniz kokte; webhook kapanis aninin goruntusudur (`ReadTreeConsumptionAsync` yalniz onun icin).
- **🚨 Gec tool tutari kayittan degil birikimciden okunur** (F-482): tool suresi icinde raporlayip run kapandiktan sonra donerse kayit da toplam da tutari tutar; kayittan okumak iki kez sayardi. `NonTokenCostAccumulator.TakeLateToolAmount` yalniz muhurden SONRA raporlananı verir (kapi `A_tool_that_reported_in_time_and_returns_late_is_counted_once`).
- **Alt run'in tutari yalniz fiyat para biriminde girer** (K-976 uzantisi): kokun para birimi cocuk kapanirken bilinmez. Token maliyeti `Tracon:Pricing:Currency` disindaysa (etiketsiz = o) cocuk token ve token disi tutari ile girer, token maliyeti ile girmez; kok kendi maliyetini oldugu gibi yazar.
- **`QuotaGate` `RunRecordingAgent`'a ulasmadan `429` doner — kota (`IQuotaStore`/`QuotaEnforcer`) asimi bir `RunError` URETMEZ** (2026-08-07, Faz 44, K-297): K-162'nin sonucu, hala gecerli. 🚨 **AMA `RunErrorClass.QuotaExceeded` artik BASKA bir yoldan da dolar** (2026-08-26, Faz 114, K-630): calistirma-ici bir agac butcesi (`AgentGraph.MaxTotalTokens`/`MaxTotalCost`) tukendiginde `RunBudgetChatClient`'in attigi `TraconRunBudgetExceededException` de AYNI sinifa (`4`) eslenir (fonksiyon dugumu kapisi `WorkflowNodeBudgetGate` da, Faz 210) — bu, `QuotaGate`'ten TAMAMEN bagimsiz, farkli bir mekanizmadir (kiraci/agent kotasi degil, tek bir run agacinin bütçesi). "QuotaExceeded asla dolmaz" iddiasi artik YANLIS; "kota (`IQuotaStore`) asla bir `RunError` uretmez" iddiasi hala DOGRU — ikisini karistirma.
- **🚨 Bir olayi ILGILI `run`'in KENDI SSE akisina yazmak, olayi otomatik olarak o akisa YANSITMAZ — akis tipine gore degisir** (2026-09-05, Faz 146): `scope.Writer.AppendAsync`/`AppendReservedAsync` ile yazilan HER olay `GET /api/runs/{id}/events` ucunda GORUNUR (o uc `IRunStore.ReadEventsAsync`'i canli okur), ama DOGRUDAN `POST /api/agents/{name}/run` akisinda GORUNMEZ — o akis yalniz MAF'in kendi `AgentResponseUpdate`'lerini `update` cercevesi olarak iletir, `RunEventWriter` yazimlarindan tamamen bagimsizdir. Yeni bir "olayi iki yolda da gorunur yap" ihtiyaci dogarsa tipi `RunNoticeMirror`'in kapali listesine ekle (Faz 207, K-991: kalici kaydi IKINCI KEZ okur, kopya insa etmez), yeni bir hook ACMA. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `RunEventWriter.AppendAsync`'in rezerve-onek reddi (K-673/K-674) kutuphanenin KENDI notice'ini yazmasini da engeller — bypass INTERNAL bir ikinci giris noktasindan gecer** (2026-09-05, Faz 146): `AppendCoreAsync(draft, allowReserved, ct)` tek govde, `AppendAsync` (`allowReserved: false`, public) ve `AppendReservedAsync` (`allowReserved: true`, `internal`) iki ince sarmalayicidir. Tuketici asla `AppendReservedAsync`'e erisemez (ayni derleme disinda gorunmez) — K-673/K-674'un "tuketici taklit edemez" garantisi boylece korunur. Yeni bir rezerve `CustomType` (kutuphanenin kendi yazacagi baska bir notice) gerekirse AYNI `AppendReservedAsync`'i kullan, yeni bir bypass ICAT ETME.

## Yargic skorunun olcegi (Faz 155)

**🚨 Bir yargic N adli skor dondurur, hepsi ayni olcekte DEGILDIR.** Pencereye ve histograma yalniz **mansset** skor girer: adi yargicin adina esit VE `Kind == RunScoreKind.Numeric` (K-730, `OnlineEvalJobHandler.JudgeOneAsync`). Ad yetmez (`Stars` 1-5 olceginde bir 5, 60 esiginin altina duser ve `run.score.low` her saglikli kosumda calar) ve olcek tahmini yapilmaz. Yeni yargic: mansseti 0-100 `Numeric` ver, baska olcege baska ad. Tam metin: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) § Faz 205.

## Yarim yazilmis skor kumesi (Faz 155)

**🚨 N satir yazan yargicta k'inci satirdaki store hatasi KALICI eksiklik uretir**: retry checkpoint'i (K-638) "bu yargicin satiri var mi" diye sorar, kac satir oldugunu sormaz. Cozum telafidir (`CompensateAsync`, iptal edilemez, hatasi loglanir); desen: "N kayit yaz sonra checkpoint'e guven" tamlik bilgisi ya da telafi ister. Tam metin: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) § Faz 205 — `olcum-kota-ve-secenekler.md`'den.

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
