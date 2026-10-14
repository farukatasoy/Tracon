# Faz 206 — Yapılandırma Bölümlerinin Bağlama Kanıtı

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** Bakımcı, 2026-10-14 — kapsam kararı sohbette alındı (22 tipin tamamı + IdempotencyKeys + okunamayan değerin açılışta hatası) ve Faz 205 ile paralel başlatma onaylandı; plan metni sohbette okunmadı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-247**
> **Genellik:** İlk ilke: belgelenen ama bağlanmayan bir anahtar bir sözleşme ihlalidir. Yapılandırma dosyası ya da ortam değişkeni kullanan her tüketici etkilenir. İş tüketici kodunda kalamaz, çünkü bağlayıcıyı yalnız Tracon yazar. Ölçülen iki vaka var: `TraconImageOptions.Timeout` (`HATA-S1-010`) ve bu planın ölçtüğü `TraconRetentionOptions.IdempotencyKeys` (§206.3). Kalem bir sözleşme ihlali sınıfıdır, bu yüzden § *Tüketici geri bildirimi kuralı*'ndan muaftır
> **Karşı görüş:** "Bağlayıcılar küçüktür ve gözle denetlenebilir. 22 test sınıfı bakım yükü ekler. Okunamayan değerde açılışı durdurmak, yazım hatası olan tüketicinin uygulamasını yükseltmeden sonra açılmaz yapar." Cevap: gözle denetim aynı sınıfı beş kez kaçırdı (`RecordRunInput` K-406, `IncludeAgentVersionTag`, `McpTimeout` K-253, F-385 lane koleksiyonları, F-416 Knowledge) ve altıncısı bu planın ölçümünde çıktı. Test maliyeti tip başına bir kezdir; ratchet yeni bölümü zaten zorlar. Açılıştaki hata anahtarı adlandırır. Bugünkü sessiz varsayılan ise hatayı üretime taşır: PII maskeleme kapanır, korunması istenen bir sütun korumasız kalır. Davranış değişikliği `CHANGELOG`'da adıyla yazılır
> **Önkoşul:** Yok. Faz 205'in kirli dosya kümesine dokunmaz: `AgentRunBudget.cs`, `QuotaEnforcer.cs`, `src/Tracon.Core/Recording/*`, `WorkflowRunner.cs`, `QuotaStoreContract.cs`, `IQuotaRecorder.cs`. Faz 205 ile paralel koşabilir. Paylaşılan dosyalar yalnız birleştirmede çakışabilir (§ *Riskler*)
> **Paketler:** `Tracon.Core` (bağlayıcılar, bir yeni `internal` doğrulayıcı) · testler `Tracon.Core.UnitTests`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — yeni doğrulayıcı ve olası ham metin taşıyıcısı `internal`'dır. Ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır; her biri yalnız `#nullable enable`. Bir sapma public üye isterse GA'dan (K-603) önce hâlâ ucuzdur. Dört public seçeneğin **davranışı** değişir (§206.4): preview'da serbesttir, sürüm notunda adıyla anılır
> **Tüketici yüzeyi:** site: `reference/configuration.md` (bölüm indeksine üç satır, `Columns` varsayılanı, `IdempotencyKeys` paragrafı, :708 başlığı, dört koleksiyon satırına "açılışta hata" cümlesi) · sevk edilen: `CHANGELOG.md` (`Changed` — davranış değişikliği · `Fixed` — `IdempotencyKeys`), dört seçeneğin XML `<remarks>`'ı (`ThresholdPercents`, `RetryDelays`, `Keys`/`Columns`, `MaskedPii`)
> **Manuel test alanı:** `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` (`MT-CORE-*`; yapılandırma bağlama emsali `MT-CORE-065`). Numara kapanışta alınır; bugün son numara `MT-CORE-146`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 64152c52:docs/arsiv/fazlar/206-YAPILANDIRMA-BAGLAMA-KANITI.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'un elle bağlanan 22 kardeş yapılandırma bölümünün her anahtarı gerçekten seçeneğe ulaşmalıdır. Okunamayan bir değer de sessizce varsayılana düşmemelidir. Bugün bunu yalnız `TraconOptions` ağacı için bir test kanıtlar.

## Bitiş Ölçütleri (DoD)

- [x] `options-section-coverage-baseline.txt` yalnız başlık satırlarını taşır; `OptionsSectionCoverageTests` yeşil
- [x] 22 `<Tip>BindingTests` sınıfı vardır; her biri en az "her skaler", varsa koleksiyon(lar) ve "yok bölüm varsayılanları" testini taşır
- [x] Boş bir `<Tip>BindingTests` ratchet'ı geçmez; test önce kırmızı görüldü
- [x] `Every_retention_target_is_bound` önce kırmızı (`IdempotencyKeys`), düzeltmeden sonra yeşil; kırmızı çıktı faz dokümanına yazıldı
- [x] Dört bağlayıcının "açılışta hata" testleri önce kırmızı görüldü ve `IStartupValidator` üzerinden yeşil
- [x] `CHANGELOG.md` `[Unreleased]`: `Changed` (davranış değişikliği) ve `Fixed` (`IdempotencyKeys`) girdileri var
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban 1292e24a` (§ *Örnek Uygulama Koşumu*, son madde)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (manuel case 7) ve case 1-6'nın açılış hataları yazıldı
- [x] `secret` taraması boş döndü (`kapi.py tarama`: temiz)
- [x] Manuel kabul case'leri `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` içine eklendi (`MT-CORE-147`…`153`); hepsi örnek uygulamada koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (iki 🟡 düzeltildi)
- [x] `docs-site/` güncellendi (§206.6); `npm run check` (içerik · derleme · bağlantı · ağırlık) kapanış kapısında temiz
- [x] `docs/hafiza/secenek-baglama-ve-gizlilik.md`'ye `IdempotencyKeys` notu (elle denetimin altıncı vakası, HATA-S1-005'in beşinci hedefi) eklendi

### Doğrulama komutları

```bash
# Ratchet ve 22 bağlama testi
python3 scripts/kapi.py test --proje Tracon.Core.UnitTests --sinif OptionsSectionCoverageTests '*BindingTests'

# Taban dosyasında tip adı kalmadı (yalnız # ile başlayan satırlar)
grep -v '^#' tests/Tracon.Core.UnitTests/Configuration/options-section-coverage-baseline.txt | grep -c .   # → 0

# Retention bağlayıcısı 16 hedefin hepsini okur
grep -c 'BindRetentionTarget(section.GetSection' src/Tracon.Core/TraconServiceCollectionExtensions.Binding.Security.cs   # → 16

# Kapanış (taban: ana dalın faz birleşmesinden önceki son commit'i)
python3 scripts/kapi.py kapanis --taban 1292e24a
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

Uygulama oturumu (2026-10-14) yazdı; kapanış gözden geçirir.

1. **§206.5 — `[Fact]`'li private iç tip yazılamaz.** Plan yüklemi "iki `private` iç
   tiple" sınıyordu. `[Fact]` taşıyan private bir sınıf `xUnit1000` (*Test classes must
   be public*) üretir ve derleme hata verir. Public bir iç tip ise xunit'in keşfettiği
   hayalet bir test olurdu. Çözüm: boş durum `private sealed class EmptyBindingTests;`
   ile, sayılan durum `OptionsSectionCoverageTests`'in kendisiyle sınanır. Ölçüm de
   teste girdi: xunit.v3 3.2.2'de `TheoryAttribute` `FactAttribute`'tan türer (yansıma
   probu); `typeof(TheoryAttribute).IsSubclassOf(typeof(FactAttribute))` satırı bu
   varsayım bozulursa düşer. Yüklem `IsDefined(typeof(FactAttribute), inherit: true)`
   ile tek denetimdir.
2. **Ortak test yardımcısı `SectionBinding`.** Plan iskeleti her sınıfa bir private
   `Bind` yazıyordu. 22 kopya yerine `tests/…/Configuration/SectionBinding.cs` üç metot
   taşır: `Bind<T>` (`IOptions<T>.Value`), `FailsAtStartup` ve `StartsCleanly`
   (`IStartupValidator.Validate()`). Voice kompozisyonu `compose` parametresiyle geçer.
   Emsal üç sınıf (`Image`, `Knowledge`, `Scheduling`) değişmedi.
3. **`OptionsKeyCoverageTests` eklendi.** Planda yoktu. Yardımcı boş geçerse 22 test boş
   kanıt olur; beş test her boşlukta düştüğünü gösterir.
4. **Retention'ın "her skaler" testi hedef anahtarlarını yansımayla üretir.** Literal
   anahtar kuralının tek istisnasıdır ve §206.3'ün kuralıdır: 16 hedef × 2 anahtar
   elle yazılsa 17. hedef yine görünmezdi. Üç üst seviye skaler literaldir.
5. **`Columns`'ta virgüllü girdi de okunamaz sayılır.** Ölçülmeden fark edildi:
   `Enum.TryParse` virgüllü metni bayrak olmayan enumda da OR'lar;
   `"RunInput,ToolArguments"` (2|5) `AgentFileContent` (7) olarak bağlanıyordu. Aynı
   kural sınıfıdır (bilinmeyen girdi → açılışta hata); kapsam büyümedi. `CHANGELOG`
   bunu yazar.
6. **Ham metin taşıyıcılarının biçimi** (plan "uygulamada seçilir" diyordu):
   `TraconContentProtectionOptions.UnreadableColumns` (`internal
   Dictionary<ProtectedColumn, string>`; anahtar, girdinin yerine bağlanan tanımsız
   değerdir — tanımsız sayı kendisi, değilse `-1`'den aşağı ilk boş değer, böylece iki
   yazım hatası kümede iki üye olur) ve `PatternContentGuardOptions.UnreadableMaskedPii`
   (`internal (string Text, PiiPatterns Bound)?`). Okunamayan `MaskedPii` metni
   `PatternContentGuardOptionsValidator.UnreadableMaskedPii` = `(PiiPatterns)int.MinValue`
   olarak bağlanır. Doğrulayıcı ham metni yalnız kayıttaki tanımsız bitler bugünkü
   değerin tanımsız bitleriyle aynıysa adlandırır; değilse sayıyı yazar (kod yolu).
   Denetimden sonra (🟡1) iki liste de aynı yolu izler:
   `TraconQuotaOptions.UnreadableThresholdPercents` ve
   `TraconWebhookOptions.UnreadableRetryDelays` (`internal Dictionary<int, string>`,
   anahtar girdinin indeksidir). Doğrulayıcı o indeksteki değer hâlâ yerine bağlanan
   sıfırsa ham metni alıntılar (`ThresholdPercents[1] is '9O'`); değilse eski mesajı
   (`Actual value: …`) yazar. Public API büyümedi.
7. **Mevcut testler.** `TraconContentProtectionOptionsValidatorTests.Disabled_options_always_pass_regardless_of_shape`
   adı artık yanlıştı → `Disabled_options_skip_the_active_key_check`; yanına
   `Disabled_options_with_an_empty_configuration_key_name_fail` ve
   `An_undefined_column_fails_whatever_enabled_says` eklendi.
   `ServiceRegistrationSnapshotTests` yeni doğrulayıcı tanımlayıcısıyla güncellendi.
8. **XML dokümanında geliştirme kaydı kimliği yok.** İlk sürüm `///` içinde `F-247`,
   `F-385`, `K4` yazıyordu; `ShippedDocumentationSelfContainmentTests` yakaladı. Kimlikler
   `//` yorumlarında kalır.
9. **Örnek uygulamada `ContentProtection` hatası tek başına görünür.** SQL deposunun
   fabrikası (`UseSqlite`) barındırılan servisler çözülürken
   `IOptions<TraconContentProtectionOptions>.Value` okur; hata `IStartupValidator`
   toplamından önce atılır ve aynı dosyadaki öteki hatalar görünmez. Bugünkü host
   davranışıdır, bu faz değiştirmedi. Manuel case 4 notu bunu yazar.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Fazın kararları burada yazılır. Dört kategoriden birine
> giren karar (AGENTS.md) ayrıca `docs/KARARLAR.md`'ye K-NNN ve kategori
> etiketiyle girer ve numarası burada anılır; girmeyen karar yalnız burada
> kalır (`faz-tamamlama` Adım 8). Plan numara rezerve etmez.

- **K-993** (kategori: public-api, güvenlik; kullanıcı kararı): okunamayan yapılandırma girdisi atlanmaz,
  açılışta hata verir (dört bağlayıcı); ham metin `internal` taşıyıcıda, mesaj onu alıntılar; hata bağlanmış
  değerden çıkar (Açık Soru 2-A: kod kazanır); `Keys`/`Columns` denetimi `Enabled`'dan bağımsız; tanımsız enum
  değeri kod yolundan da reddedilir; boş `MaskedPii` "yok"tur; virgüllü `Columns` girdisi okunamaz sayılır.
- Kategori dışı (yalnız burada): ratchet yalnız test metodu taşıyan sınıfı sayar (`CountsAsBindingTest`;
  xunit.v3 3.2.2'de `TheoryAttribute : FactAttribute` ölçüldü ve testte kilitli) · Açık Soru 3-A
  (`OptionsKeyCoverage`) · ortak test yardımcısı `SectionBinding` · Açık Soru 1-A (`MaxAgeDays` bağlaması
  değişmedi; aday F-513).

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: arşivdeki her
> `- [ ]` satırı `tamamlanmis_faz_isaretsiz_kutular()` kapısında ayrıca hata
> sayılır ve bulgunun kaynağı bulanıklaşır.
>
> `dokuman-bakim.py --denetle` 14. kapısı (`surec_olcumu_bulgulari`) bu tabloyu
> **eşik 167**'den itibaren her kapanmış fazda arar. Boş bir değer hücresi
> kırmızıdır; `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir
> **karar** arar. Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez
> (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 3 (XML'deki geliştirme kaydı kimlikleri `ShippedDocumentationSelfContainmentTests`'te bir kez kırmızı · denetimin iki 🟡'ı tek turda · kapanış kapısının ilk koşumu `build-agent-map.mjs --check`'te: site düzenlemesinden sonra `llms-full.txt` yeniden üretilmemişti) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (`ServiceRegistrationSnapshotTests` yeni doğrulayıcı tanımlayıcısıyla, bir doğrulayıcı testinin adı yeni davranışla bilinçli güncellendi) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz bugün kapandı) |

### Kırmızı → yeşil kanıtı

Uygulama oturumu yazdı (2026-10-14, `net10.0`, `kapi.py test`). Her test düzeltmeden
önce koşuldu ve düştü; düzeltmeden sonra üç TFM'de yeşil.

- **`TraconRetentionOptionsBindingTests.Every_retention_target_is_bound`** —
  `Tracon:Retention:IdempotencyKeys: expected MaxAgeDays=211 and Archive=true, got
  MaxAgeDays=1 and Archive=False. The binder does not read this target.` Aynı koşumda
  `Every_scalar_of_the_section_is_bound` da düştü (`OptionsKeyCoverage` (b):
  `TraconRetentionOptions.IdempotencyKeys.MaxAgeDays: the test value '1' equals the
  shipped default`). Düzeltme: `BindRetention`'a tek satır; `grep -c` → 16.
- **`OptionsSectionCoverageTests.An_empty_binding_test_class_does_not_count`** — yüklem
  bugünkü anlamıyla (`=> true`) çıkarıldıktan sonra: `CountsAsBindingTest(typeof(EmptyBindingTests))
  should be False but was True`.
- **Dört bağlayıcı, 16 kırmızı vaka** — hepsi `IStartupValidator.Validate()` `should
  throw but did not`:
  `TraconQuotaOptionsBindingTests.An_unreadable_threshold_fails_at_startup` (`yuz`, `0`, `-5`) ·
  `TraconWebhookOptionsBindingTests.An_unreadable_retry_delay_fails_at_startup` (`soon`,
  `00:00:00`, `-00:00:05`) ·
  `TraconContentProtectionOptionsBindingTests.An_unknown_column_fails_at_startup`
  (`true/RunInputt`, `false/RunInputt`, `false/99`, `false/""`) ·
  `TraconContentProtectionOptionsBindingTests.An_empty_key_name_fails_at_startup` ·
  `TraconContentProtectionOptionsBindingTests.An_undefined_column_from_code_fails_at_startup_too` ·
  `PatternContentGuardOptionsBindingTests.An_unreadable_pii_list_fails_at_startup`
  (`Emial,CreditCard`, `64`, `-1`) ·
  `PatternContentGuardOptionsBindingTests.An_undefined_pii_value_from_code_fails_at_startup_too`.
- **Yeşil kalması gereken sınır testleri** düzeltmeden önce de yeşildi: `An_empty_pii_list_means_none`,
  dört "kod kazanır" testi, `An_empty_management_policy_is_kept`.
- **Süre (Risk satırı):** `Tracon.Core.UnitTests` `net10.0` yalıtılmış koşum: önce 3916
  test 8,9 sn; sonra 4000 test 8,3–8,5 sn. Ölçülebilir artış yok.
- **Denetim 🟡1 (ham metin).** `TraconQuotaOptionsBindingTests.An_unreadable_threshold_is_named_as_written`
  (`ThresholdPercents:1 = "9O"`) ve `TraconWebhookOptionsBindingTests.An_unreadable_retry_delay_is_named_as_written`
  (`RetryDelays:1 = "soon"`) düzeltmeden önce düştü: mesaj
  `…ThresholdPercents values must be between 1 and 100. Actual value: 0.` ve
  `…RetryDelays values must be greater than zero. Actual value: 00:00:00.` idi,
  `ThresholdPercents[1] is '9O'` / `RetryDelays[1] is 'soon'` yoktu.
- **Denetim 🟡2 (iki `Columns` davranışı, mutasyon kanıtı).** (a)
  `An_unknown_column_fails_at_startup` satırı `("false", "RunInput,ToolArguments")`:
  `BindProtectedColumn`'daki virgül denetimi geçici olarak silinince düştü
  (`should throw but did not` — girdi sessizce `AgentFileContent` bağlandı); denetim
  geri kondu, yeşil. (b) `Every_unknown_column_is_named` (`RunInputt` + `ToolResultt`):
  yer tutucu geçici olarak sabit `(ProtectedColumn)(-1)` yapılınca düştü (yalnız
  `'ToolResultt'` adlandı); geri kondu, yeşil.

### Kapı koşumu notu

İlk `kapi.py ic-dongu --taban 245afe91` koşumunda `JobWorkerDeadlineTests.With_attempts_left_the_timed_out_attempt_is_released_and_the_next_attempt_gets_the_whole_limit`
`net8.0` tam koşumda 30 sn'de düştü (iş `Leased`'te kaldı). Yalıtılmış koşumda 3/3
geçti; ikinci tam koşum yeşildi. Faz 200'ün arşivindeki aynı kaynak çekişmesi
vakasıdır, bu fazla ilgisi yoktur (zamanlayıcı seçeneklerine dokunulmadı).

## Örnek Uygulama Koşumu

`samples/Tracon.Api` (Release, `artifacts/bin/Tracon.Api/release`, `Development`
ortamı, port 5206). Ortak veritabanına dokunmamak için:
`Tracon__PostgreSql__ConnectionString=` (boş) ve
`Tracon__Sqlite__ConnectionString=Data Source=<scratchpad>/faz206-sample.db`. Ücretli
çağrı olmasın diye `Tracon__Providers__OpenAI__ApiKey=` (boş → `echo` sağlayıcısı).
Hiçbir `secret` değeri yazılmadı ya da basılmadı.

1. **Okunamayan değerli dosya, 1. koşum** — geçici `appsettings.Development.json`
   (koşumdan sonra silindi): `Quotas:ThresholdPercents=["80","yuz"]`,
   `Webhooks:RetryDelays=["00:01:00","soon"]`, `ContentProtection:Columns=["RunInput","RunInputt"]`,
   `ContentProtection:Keys:old=""`, `ContentGuard:Pattern:MaskedPii="Emial,CreditCard"`
   (+ `Demo:SuppressRegistrations=ContentGuard`), `Retention:Enabled=true`,
   `Retention:IdempotencyKeys:MaxAgeDays=0`. Çıkış 134:
   `OptionsValidationException: TraconContentProtectionOptions.Keys['old'] cannot be empty. …; TraconContentProtectionOptions.Columns contains 'RunInputt', which is not a ProtectedColumn name. Known names: SessionState, …, WorkflowCheckpointState.`
   Yalnız `ContentProtection` göründü (Sapma 9).
2. **Aynı dosya, `ContentProtection` girdileri olmadan, 2. koşum** — çıkış 134, tek
   `AggregateException` içinde dört hata:
   `TraconQuotaOptions.ThresholdPercents values must be between 1 and 100. Actual value: 0.` ·
   `TraconWebhookOptions.RetryDelays values must be greater than zero. Actual value: 00:00:00.` ·
   `TraconRetentionOptions.idempotency_keys.MaxAgeDays must be at least 1 when specified. Actual value: 0.` ·
   `PatternContentGuardOptions.MaskedPii is 'Emial,CreditCard', which is not a comma-separated list of PiiPatterns names. Known names: None, Email, Iban, CreditCard, TurkishNationalId, ProviderApiKey.`
3. **Manuel case 5** — yalnız `Tracon__ContentProtection__Keys__old=` ve
   `Tracon__ContentProtection__Enabled=false`: çıkış 134, `Keys['old'] cannot be empty`.
4. **Temiz açılış + gerçek `run` (manuel case 7)** — dosya yok: 3 sn'de `Application started`.
   `POST /tracon/api/agents/support/run` → SSE `run` · `update` · `done`;
   `GET /tracon/api/runs/01a139d4-b2dd-76f0-8b12-84d009df143d` → `status: Completed`,
   `agentName: support`.
5. **Manuel case 6'nın ikinci yarısı** — `Tracon__ContentGuard__Pattern__MaskedPii=Emial,CreditCard`,
   `SuppressRegistrations` yok: açıldı. Örnek kod `MaskedPii`'yi yazar, kod kazanır (2-A).
6. **2. koşumun tekrarı, denetim 🟡1 düzeltmesinden sonra** (örnek yeniden derlendi) —
   çıkış 134; ilk iki hata artık girdiyi alıntılar:
   `TraconQuotaOptions.ThresholdPercents[1] is 'yuz', which is not a whole number. Values must be between 1 and 100.` ·
   `TraconWebhookOptions.RetryDelays[1] is 'soon', which is not a TimeSpan (for example 00:05:00). Values must be greater than zero.`
   Retention ve `MaskedPii` hataları aynı kaldı.
7. **Kapanış kapısı:** `python3 scripts/kapi.py kapanis --taban 1292e24a` → **çıkış 0** (ikinci koşum):
   tarama 5 sn · `--denetle` · betik testleri · agent haritası · denetim paketi · build 177 sn (0 uyarı) ·
   test 700 sn (38 test koşumu, 22 444 test, 0 düşen; E2E dahil) · pack · format 154 sn · `npm run check`
   47 sn (1090 sayfa, ağırlık tavanında). İlk koşum yalnız `build-agent-map.mjs --check`'te düştü:
   `public/llms-full.txt` site düzenlemesinden sonra yeniden üretilmemişti; üretildi ve commit edildi. İki
   koşumdan önce üretilen site `api/` dizini silindi (F-507); `SourceLanguageTests` yeşil.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

Bağımsız denetim (2026-10-14): 🔴 yok, iki 🟡, dört 🟢.

| # | Bulgu | Seviye | Triyaj · Sonuç |
|---|---|---|---|
| 1 | `ThresholdPercents` ve `RetryDelays` hata mesajı operatörün yazdığı metni değil yerine bağlanan `0` / `00:00:00`'ı gösteriyordu; plan "hata anahtarı ve ham metni adlandırır" diyordu | 🟡 | gerçek · Düzeltildi: `internal` ham metin taşıyıcıları + doğrulayıcı alıntısı; iki test önce kırmızı (§ *Kırmızı → yeşil kanıtı*) |
| 2 | `Columns` bağlayıcısının iki davranışı testsizdi: virgüllü girdinin reddi ve iki yazım hatasının iki ayrı yer tutucu alması | 🟡 | gerçek (test eksiği; kod doğruydu) · Düzeltildi: `"RunInput,ToolArguments"` satırı + `Every_unknown_column_is_named`; ikisi de mutasyonla kırmızı görüldü |
| 3 | Liste anahtarına tek değer yazılırsa (`__0`'sız ortam değişkeni) `GetChildren()` boş döner ve değer yine sessizce yok sayılır (`…Binding.Operations.cs:205` `ThresholdPercents`, `…Binding.Security.cs:240` `Columns`, `:375` `RetryDelays`; ölçüm 🟡1 düzeltmesinden sonraki satırlar) | 🟢 | gerçek · **F-508** olarak devredildi (skaler/enum yazım hatasıyla birlikte; boş dizi de aynı sınıf) |
| 4 | Ratchet yanlış negatifleri: `[Fact(Skip)]`, `[Fact(Explicit)]` ya da ilgisiz tek bir `[Fact]` sınıfı saydırır; 23. bölüm sınıfını `OptionsKeyCoverage` çağırmaya zorlayan bir şey yok; `An_empty_binding_test_class_does_not_count` yalnız yüklemi sınar (`:40`'taki `Where` silinse de yeşil kalır) | 🟢 | gerçek · **F-510** olarak devredildi |
| 5 | `OptionsKeyCoverage` (b) mesajı bağlayıcı anahtarı hiç okumadığında da "the test value" der (`OptionsKeyCoverage.cs:114`); ayarlanabilir bir Tracon sınıfı property'si skaler sayılır (`:138`) | 🟢 | gerçek · **F-510** olarak devredildi |
| 6 | Retention anahtarları property adından türetilir (`TraconRetentionOptionsBindingTests.cs:114`); bir yeniden adlandırma belgelenen anahtarı sessizce taşır. Yansımalı sayıma eşit literal bir liste iki güvenceyi birden verirdi | 🟢 | gerçek · **F-510** olarak devredildi |

## Tüketici Yüzeyi Envanteri

`tuketici-dokuman-senkronu` Adım 1 (taban `1292e24a`):

1. **`docs-site/` (elle):** `reference/configuration.md` — bölüm indeksine üç satır (`Tracon:InboundTriggers`,
   `Tracon:SessionOwnership`, `Tracon:Voice:Live`, anahtarların yaşadığı sayfaya bağlantıyla); `Quotas:ThresholdPercents`,
   `ContentGuard:Pattern:MaskedPii`, Webhooks `RetryDelays`, ContentProtection `Keys`/`Columns` satırlarına "açılışta
   hata" cümlesi; "eleven" → "twelve"; `Keys`/`Columns` denetiminin `Enabled`'dan bağımsızlığı ve boş `Columns`
   listesi paragrafı; Retention'daki bayat `IdempotencyKeys` paragrafı yerine `MaxAgeDays` paragrafı (Açık Soru
   1-A: yapılandırma bir hedefi kapatamaz; kapalı politika ya da kod); `### Live voice conversation` →
   `### Voice conversation` (bağlantı yok, ölçüldü). `concepts/governance.md` — "listeyi boşalt" cümlesi
   yapılandırma için yanlıştı (`[]` çocuksuzdur, ölçüldü): kodda `Clear()`, açılış hatası cümlesi.
   **Üretilen:** `api/` (beş public üyenin `<remarks>`'ı), `reference/changelog.md` (`CHANGELOG.md`'den).
2. **Sevk edilen metin:** beş public üyenin XML `<remarks>`'ı · `CHANGELOG.md` (`Changed` + `Fixed`). Paket
   `README.md`'si ve `tracon.json` değişmedi.
3. **Yerel referans / agent haritası:** yeni giriş noktası yok; `capabilities.md` değişmedi
   (`build-agent-map.mjs --check`: güncel).

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

Sıradaki faz yok: seçilmemiş işler `ADAYLAR.md`'dedir (bu faz F-508…F-513'ü açtı). Faz 208 paralel daldadır.

**Devralınan sözleşmeler**

- Okunamayan yapılandırma girdisi açılışta hata verir (K-993). Kural F-385 + bu faz: girdi atlanmaz, tanımsız ya
  da sıfır değer olarak bağlanır, ham metin `internal` taşıyıcıda durur, hata bağlanmış değerden çıkar.
- Her elle bağlanan `SectionName` bölümünün bir `<Tip>BindingTests` sınıfı vardır (taban dosyası boş); sınıf en az
  bir test metodu taşımalı ve `Every_scalar_of_the_section_is_bound` `OptionsKeyCoverage.ShouldCoverEveryMember`'ı
  çağırmalı. Kompozisyon `SectionBinding` (gerçek `AddTracon(section)` + `IStartupValidator`).
- Retention hedefleri yansımayla sayılır ve `RetentionTargets.All` ile eşitlenir.

**Bilinen tuzaklar**

- 🚨 Yeni seçenek alanı: bağlayıcıya **ve** `<Tip>BindingTests`'in anahtar sözlüğüne yazılır; yazılmazsa
  `OptionsKeyCoverage` (a) kızarır. Yeni koleksiyon alanı ya testte adlandırılır ya da `exempt` edilip kendi testini alır.
- 🚨 Örnek uygulamada `ContentProtection` doğrulama hatası `IStartupValidator` toplamından önce, tek başına atılır
  (SQL deposu fabrikası seçeneği erken okur). Açılış hatası case'lerini tek tek koş.
- 🚨 JSON `null` ve `[]` yapılandırma paketi sürümüne göre farklı okunur; liste anahtarına `[]` yazmak varsayılanı
  değiştirmez (`hafiza/secenek-baglama-ve-gizlilik.md`).
- ➜ ratchet hâlâ `Skip`/`Explicit` testi sayar ve `OptionsKeyCoverage` çağrısını zorlamaz (F-510).
