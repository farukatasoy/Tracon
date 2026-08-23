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

- **🚨 Yapılandırma bağlamada erken dönüş sonraki bölümleri yutar** (2026-08-02): `Bind` metodu `RunRecording` yoksa `return` ediyordu; `Observability` hiç okunmadı ve `AgentPrism__Observability__SuccessSampleRatio=1` sessizce yok sayıldı. Her alt bölüm **kendi varlığından** sorumlu olmalı (`BindRunRecording` / `BindObservability`).
- **🚨 Alan eklenip `Bind()`'a eklenmeyi 3× unutuldu; standalone `IOptionsMonitor<TNested>` içiçe seçenekten kopar** (2026-08-14): `RecordRunInput`(K-406)/`IncludeAgentVersionTag`/`Validation.McpTimeout`(K-253); çözüm `AgentPrismOptionsBindingCoverageTests.cs`. `QuotaUsageObserver` de `Configure<>`siz `IOptionsMonitor<AgentPrismObservabilityOptions>` alıyordu; `SectionName`'siz türü standalone enjekte etme.

## Gizlilik suzgeci

- **🚨 `secret` filtresinde alt dize eslemesi cogul/tekil ayrimi gozetmezse YANLIS alanlari gizler** (Faz 9, K-081): `model.maxOutputTokens` gercek bir denetim kaydinda `***` oldu ("token" fragmani "Tokens"i esledi); sentetik veri kullanan birim testleri yakalamadi. `IsSecretKey` artik "tokens" (cogul) iceren anahtarda eslesmeyi iptal eder. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `AuditSecretFilter` alan ADINA bakar, DEGERE degil** (Faz 65): adinda `apikey`/`authorization`/`token`/`password`/`secret` gecen HER alan, icerigi zararsiz olsa da `***` olur ve teshis degeri sessizce kaybolur. Cozum alani degistirmek degil, denetim ozetinde farkli adlandirmaktir (`configKeyName`). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Metrik ve maliyet

- **🚨 Eval calistirmalari `RunKind.Eval` ile isaretlenmezse `/api/stats`'i kirletir** (2026-08-03, Faz 18): birim testleri yakalamadi; ornek uygulamada gercek bir OpenAI cagrisiyla olculdu (`totalRuns:1` → duzeltmeden sonra `0`). `AgentPrismRunOptions.Kind` → `RunStart.Kind` → `RunStartInfo.Kind`; `IRunStore.GetStatisticsAsync` bu turu filtreler (K-141). Yeni bir `RunKind` degeri tasiyan bir ozellik eklerken bu zincirin tamamini guncelle.
- **Metrik etiketini kosullu eklerken `TagList` kullan, `KeyValuePair<string,object?>[]` degil** (Faz 19): `Counter<T>.Add`/`Histogram<T>.Record`'un sabit-uzunluklu `params` asiri yuklemesi yerine `System.Diagnostics.TagList` (struct) kullanilir. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Tool'un token DISI olcumu cagri kimligiyle kayda baglanir** (2026-08-05, Faz 28): kimlik `FunctionInvokingChatClient.CurrentContext.CallContent.CallId`'den okunur — tool govdesinde DOLUDUR; `AIFunctionArguments.Context` `null` gelir ve kimligi TASIMAZ. Baglanamazsa `false` doner, tool'un isi bozulmaz. Zincir: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ObservableGauge` geri cagirmasi es zamanlidir — icine `await` konamaz** (Faz 35, K-256): veritabani okuyan bir olcer `SemaphoreSlim` korumali, `TimeProvider` karsilastirmali bir onbellekle yazilir (`QuotaUsageObserver.Snapshot()` deseni). `BackgroundService`/`PeriodicTimer` denendi ve test edilemez cikti — gerekce: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **`AgentPrismMetrics`'e yeni bir sayac eklerken `CompleteAsync` icindeki cagriyi `scope.Depth`'e KOSULLU yapma — `RecordRun` gibi HER calistirmada (kok + alt) cagir** (Faz 35): `RecordCost` `if (scope.Depth == 0)` blogunun DISINDA durur ve K-151'in "sayac yalniz KENDI maliyetini yayar" kuralini dogal olarak saglar. Kota/olay yayini (`RecordQuotaAsync`/`PublishRunEventAsync`) ise KASITLI olarak yalniz kokte calisir — iki kural KARISTIRILMAMALI.
- **🚨 Elle yazilmis `InputCost + OutputCost` toplami ucuncu bir terim eklenince SESSIZCE eksik raporlar — denetim YEDI yerde buldu** (Faz 68, K-483): cache ucreti `input_cost`'un alt kumesi DEGIL ucuncu terimdir; maliyet tavani olan bir kiraci tavani asabilirdi. `RunCost.Total()` tek cevaptir — alan eklerken `Total()`'i guncelle, `grep -rn "InputCost ?? 0" src/` ile sinifi tara. Kapi: `Every_cost_total_includes_the_cache_charge`. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bir sayacin `null` gelmesi ile `0` gelmesi AYRI bilgidir** (Faz 68, K-482): `0` "olculdu, yoktu" IDDIASIDIR ve susan saglayici icin kendinden emin %0 cache isabeti uretir. Uc nokta: `MergeUsage` (`AddOrNull`), `CompactionUsageAccumulator` (sayac basina bildirim bayragi), `TreeUsage` (`COALESCE` KASITLI yok). Fiyatta da: bildirilmediyse ucret `null`. Ayrinti: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Kota

- **Kota ve olay yayini YALNIZ kok calistirmada isler** (2026-08-03, Faz 21): `RunRecordingAgent.CompleteAsync` icinde `scope.Depth == 0` kosuluyla korunur. Alt calistirma ayni kullanici isteginin parcasidir; ayrica sayilsaydi bir agent agaci kotayi derinligi kadar hizli tuketir ve her dugum icin ayri bir `run.completed` olayi yayilirdi. `RunScope`'a bu yuzden `RunId`/`RootRunId`/`Depth`/`SessionId` alanlari eklendi.
- **🚨 `QuotaGate` `RunRecordingAgent`'a ulasmadan `429` doner — kota asimi bir `RunError` URETMEZ** (2026-08-07, Faz 44, K-297): K-162'nin sonucu. `QuotaExceeded` sinifi bu yuzden asla dolmaz; kota sinyali `IQuotaStore`/`QuotaEnforcer`'dan okunmalidir.
