# Secenek Baglama ve Gizlilik Suzgeci Tuzaklari

> `TraconOptions` elle yazilmis `Bind()` zinciri (K-021) ve denetim izinin
> `secret` suzgeci. Ikisi de ayni sekle sahiptir: bir ALAN ADI uzerinde
> calisirlar ve sessizce yanlis davranirlar.
>
> Faz 174'te `olcum-kota-ve-secenekler.md` 16.666/16.000 B'ye ulasinca ayrildi.
> Olcum, maliyet ve kota ekseni orada kaldi:
> [`olcum-kota-ve-secenekler.md`](olcum-kota-ve-secenekler.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

## Secenek baglama

- **🚨 Yapılandırma bağlamada erken dönüş sonraki bölümleri yutar** (2026-08-02): `Bind` metodu `RunRecording` yoksa `return` ediyordu; `Observability` hiç okunmadı ve `Tracon__Observability__SuccessSampleRatio=1` sessizce yok sayıldı. Her alt bölüm **kendi varlığından** sorumlu olmalı (`BindRunRecording` / `BindObservability`).
- **🚨 Alan eklenip `Bind()`'a eklenmeyi 3× unutuldu; standalone `IOptionsMonitor<TNested>` içiçe seçenekten kopar** (2026-08-14): `RecordRunInput`(K-406)/`IncludeAgentVersionTag`/`Validation.McpTimeout`(K-253); çözüm `TraconOptionsBindingCoverageTests.cs`. `QuotaUsageObserver` de `Configure<>`siz `IOptionsMonitor<TraconObservabilityOptions>` alıyordu; `SectionName`'siz türü standalone enjekte etme.
- **🚨 Bir seçeneğin geçerli ARALIĞI çalışma anındaki bir değişmezi kırıyorsa, o aralık YAZIYLA değil `IValidateOptions` ile korunur** (2026-09-09, K-743): Kural: **yenileme aralığı, yenilediği geçerlilik penceresinin içinde kalmalıdır**; iki sabit tek yerde durur (`SingletonGuard.MinimumRenewInterval` · `MinimumLeaseDuration`) ve bir test onları birbirine kilitler. Aynı sınıfın doğru yazılmış örneği: `RunReconciliationOptionsValidator`'ın `OrphanThreshold >= HeartbeatInterval` kontrolü. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Zamanlayıcıya giden her seçenek `TimerPeriod.IsValid` ile doğrulanır; `<= TimeSpan.Zero` yetmez** (2026-09-24, F-278): `PeriodicTimer` ve `Task.Delay` yalnız 1 ms – ~49,7 gün alır; timer `BackgroundService.ExecuteAsync` içinde kurulur ve varsayılan `StopHost` host'u "Application started"tan SONRA durdurur (canlı: `Tracon__Approvals__ScanInterval=00:00:00`). Türetilmiş periyot da sayılır (iş kirası/2, singleton kirası/3). Özellik kapalıyken timer yoktur → denetim yalnız açıkken. Yeni periyodik servis: seçeneği doğrulayıcıya + `ValidateOnStart` + `IStartupValidator` üzerinden DI testi (`TimerPeriodOptionValidationTests`); yeni `new PeriodicTimer(` sitesi `PeriodicTimerSiteTests`'i kırar. Timeout (`CancelAfter`) alt sınıfı ayrı: F-280.

## Gizlilik suzgeci

- **🚨 `secret` filtresinde alt dize eslemesi cogul/tekil ayrimi gozetmezse YANLIS alanlari gizler** (Faz 9, K-081): `model.maxOutputTokens` gercek bir denetim kaydinda `***` oldu ("token" fragmani "Tokens"i esledi); sentetik veri kullanan birim testleri yakalamadi. `IsSecretKey` artik "tokens" (cogul) iceren anahtarda eslesmeyi iptal eder. Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `AuditSecretFilter` alan ADINA bakar, DEGERE degil** (Faz 65): adinda `apikey`/`authorization`/`token`/`password`/`secret` gecen HER alan, icerigi zararsiz olsa da `***` olur ve teshis degeri sessizce kaybolur. Cozum alani degistirmek degil, denetim ozetinde farkli adlandirmaktir (`configKeyName`). Vaka: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
