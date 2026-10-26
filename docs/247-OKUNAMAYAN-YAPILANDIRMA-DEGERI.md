# Faz 247 — Okunamayan Yapılandırma Değeri Açılışta Durur

> **Durum:** 📋 Planlandı (2026-10-26)
> **Kademe:** tam — geri dönüşü pahalı karar ve public davranış sözleşmesi: yapılandırmanın neyi kabul ettiği ve hostun ne zaman durduğu değişir. GA'dan sonra bu kuralı gevşetmek ya da sıkılaştırmak major sürüm ister
> **Plan onayı:** Bakımcı, 2026-10-26 (sohbette alınan kararlar: okunamayan **her** skaler açılışta hata verir — kayıt başına sınıflandırma yok; boş ya da yalnız boşluk "verilmedi" kalır; liste anahtarına verilen tek skaler değer hatadır; F-513 kapsam dışı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-572**, **F-508**
> **Genellik:** Tracon'u `appsettings`, ortam değişkeni ya da başka bir `IConfiguration` kaynağıyla kuran her kurulum. İş tüketici kodunda kalamaz: bağlayıcılar Tracon'un `Configure<T>` lambdalarıdır. İkinci kanıt ilk ilkedir: K-993 aynı kuralı listelere ve `UtilityModel` sayılarına zaten koydu; 21 XML satırı ve onlardan üretilen 16 site sayfası "any other value fails at startup" vaat eder ve okunamayan metin için bu vaat bugün yanlıştır; `Tracon:Workflows` Microsoft bağlayıcısını kullandığı için zaten katıdır
> **Karşı görüş:** "Açılışta durmak üretimi düşürür; uyar ve atla daha güvenlidir." Cevap: sessiz atlama sınırı gevşetir ve operatör hiçbir sinyal almaz — `Tracon:Tools:DefaultMaxOutputBytes = "10MB"` sınırsız kalır, `RecordToolPayloads = "hayır"` kayda yazmayı sürdürür, `RateLimit:Enabled = "evet"` hız sınırını kapalı bırakır. `Configure<T>` lambdasında logger yoktur. K-993'ün yeniden açılma koşulu ("ölçülen bir kesinti") gerçekleşmedi. `rc.1` öncesi kırıcı değişiklik serbesttir (UR-012 B)
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Abstractions` (iç okuyucu), `Tracon.Core` (dört bağlayıcı dosya, doğrulayıcılar, kayıt; `Voice/LiveVoice`, `Voice/VoiceConversation`), `Tracon.Providers.Shared` (`ProviderRegistrationCore`), `Tracon.Mcp`, `Tracon.Voice`, `Tracon.OpenAI` (Live dahil), `Tracon.Anthropic`, `Tracon.Google`, `Tracon.Azure`, `Tracon.PostgreSql`, `Tracon.Sqlite`, `Tracon.SqlServer`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — okuyucu, taşıyıcılar ve genel doğrulayıcı `internal`. Değişen public davranıştır (K-* `public-api` + `geri-dönüşü-pahalı`). `wc -l src/*/PublicAPI.Shipped.txt`: 18 dosya × 1 satır (2026-10-26)
> **Tüketici yüzeyi:** site `reference/configuration.md` § *Binding rules* (`:10-30`, genel kural), `concepts/governance.md:499-501` (liste kuralı), üretilen `api/` sayfaları (XML'den) · sevk edilen: 21 XML satırı (vaat doğru olur, metin gerekirse "a value that cannot be read" ile netleşir), `CHANGELOG.md` `[Unreleased]` → *Changed* (kırıcı: okunamayan değer açılışı durdurur)
> **Manuel test alanı:** `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-993 —\|K-1015 —" docs/KARARLAR.md
   ```
   **K-993** (okunamayan liste girdisi açılışta durur; ham metin `internal`
   taşıyıcıda; hata bağlanmış değerden çıkar, sonradan yazan kod kazanır — bu faz
   onu skalere genişletir), **K-1015** (`DelegationMode` bilinmeyen adı açılışta
   reddeder; bağlama anında atan tek emsal). Tam gerekçe:
   `grep -n "K-993" docs/arsiv/KARARLAR-GECMISI.md`
3. Önceki fazdan devir: yok
4. Alan hafızası:
   [`hafiza/secenek-baglama-ve-gizlilik.md`](hafiza/secenek-baglama-ve-gizlilik.md)
   (`:21` `ValidateOnStart`'sız doğrulayıcı tembeldir; `:22` K-993; `:23`
   `ContentProtection` hatası tek başına görünür; `:24` `null`/`[]` paket sürümüne
   göre; `:33`, `:35` skaler desen ve `IncludeTenantTag`)
5. Gerektiğinde: `tests/Tracon.Core.UnitTests/Configuration/SectionBinding.cs`
   (`FailsAtStartup`, `:54`) ve `OptionsKeyCoverage.cs`

---

## Amaç

Tracon'un bağlayıcıları skaler değerleri `if (X.TryParse(...)) { option = v; }`
deseniyle okur. Metin okunamazsa seçenek sessizce varsayılanda kalır. 181 yerde
böyledir; 25 anahtarda varsayılan gevşek taraftır, yani yazım hatası bir sınırı
ya da korumayı kapatır.

- **F-572** — okunamayan her skaler değer açılışta hata verir. Mesaj anahtarı ve
  ham metni alıntılar; bütün hatalar tek mesajda toplanır; bağlamadan sonra
  seçeneği yazan kod kazanır (K-993).
- **F-508** — liste anahtarına verilen tek skaler değer (`:0`'sız) açılışta hata
  verir ve `:0` biçimini önerir. Boş dizi ve boş değer "verilmedi" kalır (bugün
  belgelenmiş davranış, `governance.md:500`).

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`TraconServiceCollectionExtensions.Binding.Core.cs:42-49`](../src/Tracon.Core/TraconServiceCollectionExtensions.Binding.Core.cs) | `int.TryParse(section[DefaultMaxOutputBytes])` başarısızsa atlanır; `"10MB"` sınırsız kalır |
| `Binding.{Core,Operations,Security,Models}.cs` | Sessiz site sayısı 53 · 37 · 28 · 15 (`TryParse`, `TryReadBool`, `Read*` sarmalayıcıları; ham `TryParse` metni 37/26/21/18) |
| Paket bağlayıcıları | 48 sessiz site: Mcp 7, Voice 5, `Core/Voice/LiveVoice` 9, `Core/Voice/VoiceConversation` 8, OpenAI Live 2, OpenAI 2, Anthropic 3, Google 1, Azure 1, PostgreSql 4, Sqlite 3, SqlServer 3 |
| `Binding.Models.cs:57,58,64,110,114,152,179,180` | Sekiz fiyat okuması (`ReadDecimal`) `null` döner; doğrulayıcı yalnız bütün alanları boş kaydı yakalar |
| [`ProviderRegistrationCore.cs:96-122,136-163`](../src/Tracon.Providers.Shared/ProviderRegistrationCore.cs) | Model başına 10 katalog alanı sessiz okunur (XML: "null when absent or unparsable") |
| [`Binding.Core.cs:251-275`](../src/Tracon.Core/TraconServiceCollectionExtensions.Binding.Core.cs) | `BindList`, çocuksuz anahtarda `values.Length == 0` ile döner; tek skaler değer (`EnvironmentAllowList=PATH`) yok sayılır |
| [`Binding.Models.cs:312-379`](../src/Tracon.Core/TraconServiceCollectionExtensions.Binding.Models.cs) | Tek raporlayan desen (M3): `ReadFloat`/`ReadInt` `(anahtar, metin)` toplar; yalnız `UtilityModel` kullanır |
| `src/**/*.cs` | 21 XML satırı "any other value fails at startup" der (`grep -rn "any other value fails at startup" src`) |

**Sınıf dağılımı** (181 site): limit 69 · bütçe 18 · güvenlik 34 · düz 60.
Tip: bool 60 · int 67 · TimeSpan 45 · long 5 · double 3 · decimal 1 · enum 1.
**Varsayılanı gevşek olan 25 anahtar** — yazım hatası korumayı kapatır:
`Tools.DefaultMaxOutputBytes` · `Preflight.Enabled` ·
`ModelConcurrency.MaxConcurrentCallsPerProvider` · `RunRecording.RecordRunInput` ·
`RecordMessageDeltas` · `RecordToolPayloads` · `Quotas.AllowOnStoreFailure` ·
`RateLimit.Enabled` · `ContentProtection.Enabled` · `Retention.Enabled` ·
`RetentionTarget.MaxAgeDays` (17 hedefe hizmet eder) · `SessionOwnership.Enabled` ·
`RefuseUnownedSessions` · `AgentGraph.MaxTotalCost` · `Voice.RequireApproval` ·
`Voice.Timeout` · `VoiceLive.PersistTranscript` · beş sağlayıcının `Timeout`'u
(OpenAI, OpenAI Live, Anthropic, Google, Azure) · üç SQL paketinin
`AutoApplyMigrations`'ı.

> Kanıtlar 2026-10-26 tarihinde doğrulandı (salt okuma; site başına tablo bir
> tarama betiğiyle üretildi ve kalıcı değildir — uygulayan oturum listeyi
> 247.6'daki kaynak taramasıyla yeniden üretir).

---

## 247.1 — Kural

Bir anahtar boş olmayan bir değer taşıyorsa ve bağlayıcı onu hedef tipe
çeviremiyorsa host açılışta durur. Kural her skaler tip için aynıdır: `int`,
`long`, `double`, `decimal`, `bool`, `TimeSpan`, enum.

- Boş ya da yalnız boşluk değer "verilmedi"dir (`UtilityModel` emsali, K-993 notu).
- Sayılar `InvariantCulture` ile okunur (bugünkü davranış).
- Bağlamadan sonra seçeneği kodda yazan kod kazanır: doğrulayıcı yalnız seçeneğin
  değeri bağlayıcının bıraktığı değerse hata verir (K-993; `IncludeTenantTag`
  doğrulayıcısı `TraconOptionsValidator.cs:360-372` aynı sınırı kabul eder —
  varsayılanla aynı değeri yazan kod ayırt edilemez).
- Bütün okunamayan değerler **tek** `OptionsValidationException` mesajında
  listelenir (her biri: tam anahtar yolu + alıntılanmış metin + beklenen tip).

## 247.2 — Okuyucu

`internal` bir okuyucu (öneri adı `ConfigurationValueReader`; yeri Açık Soru 4). M3'ün (`Binding.Models.cs:349-379`) genelleşmesidir:

- `ReadInt32`, `ReadInt64`, `ReadDouble`, `ReadDecimal`, `ReadTimeSpan`,
  `ReadBoolean`, `ReadEnum<T>` — bölüm + anahtar + bir toplayıcı alır; değer yoksa
  ya da boşsa `null`, okunamazsa `null` döner ve `(yol, metin)` çiftini toplayıcıya
  yazar.
- 🚨 Görünürlük: `Tracon.Abstractions`'ın `InternalsVisibleTo` listesi Core,
  Workflows, AspNetCore, Cli, Mcp ve üç SQL paketini kapsar; **Anthropic, Azure,
  Google, OpenAI ve Voice'u kapsamaz** (`Tracon.Abstractions.csproj:14-46`, ölçüldü
  2026-10-26). `Tracon.Providers.Shared` bir paket değil, sağlayıcı paketlerine
  derlenen paylaşılan kaynaktır. Okuyucunun yeri Açık Soru 4'tür.
- Sessiz yardımcılar silinir: `TryReadBool` (`Binding.Core.cs:578-579`),
  `ReadDecimal` (`Binding.Models.cs:287-289`), `ProviderRegistrationCore`'un dört
  `Read*`'ı, `LiveVoiceBuilderExtensions.cs:216-220` ve
  `VoiceConversationBuilderExtensions.cs:191-195`'teki özel `TryInt`/`TryTimeSpan`.

## 247.3 — Taşıyıcı ve doğrulayıcı

- Her seçenek tipine bir `internal` taşıyıcı üye (M2/M3 emsali:
  `TraconOptions.cs:64`, `TraconQuotaOptions.cs:61`). Üye public yüzeye girmez;
  `ConfigurationKeySnapshotTests` ve `OptionsKeyCoverage` yalnız public üyeyi okur.
- Tek bir açık-generic doğrulayıcı (öneri adı
  `UnreadableConfigurationValidator<TOptions>`), taşıyıcısı olan her seçenek tipi
  için `ValidateOnStart` ile kaydedilir. Bugün doğrulayıcısı olmayan sekiz tip
  (RateLimit, Idempotency, AsyncRun, ContentGuard, Egress, SessionOwnership,
  Knowledge, InboundTrigger) yalnız bu doğrulayıcıyı alır; aralık doğrulayıcıları
  F-512'nin işidir ve bu fazda yazılmaz.
- Bugünkü ayrı mekanizmalar tek kurala katılır: M1 nöbetçi değerleri
  (`Binding.Operations.cs:106-139`, `Binding.Models.cs:185-195`) metni alıntılar;
  M2 taşıyıcıları (`ThresholdPercents`, `RetryDelays`, `Columns`, `MaskedPii`) aynı
  mesaj biçimine geçer. M4 (`DelegationMode`, bağlama anında atar) için Açık Soru 1.

## 247.4 — Liste anahtarları (F-508)

- Liste anahtarı çocuksuz ama boş olmayan bir değer taşıyorsa açılışta hata:
  "`Tracon:Skills:Scripts:EnvironmentAllowList` is 'PATH', a single value; write it
  as `…:EnvironmentAllowList:0`." Etkilenen bağlayıcılar: `BindList`
  (`EnvironmentAllowList`, `HttpSchedulableHandlerKeys`, `DeniedTerms`), `Lanes`,
  `AgentNames`, `AllowedMediaTypes`, `ThresholdPercents`, `Columns`, `RetryDelays`.
- `[]` ve boş değer varsayılanı değiştirmez (JSON paketi 8.0.1 ve 10.0.0'da
  ikisi de çocuksuzdur, `hafiza/secenek-baglama-ve-gizlilik.md:24`). Bu davranış
  belgelidir ve kalır.

## 247.5 — Belge

- `reference/configuration.md` § *Binding rules* genel kuralı bir paragrafla
  yazar: okunamayan değer açılışı durdurur; boş değer verilmemiş sayılır; liste
  `:0` ile yazılır.
- 21 XML satırının vaadi doğru olur. Metin "a value that cannot be read also fails
  at startup" ile netleşmeli mi — uygulamada okunur, gerekirse tek cümle eklenir.
- `CHANGELOG.md` `[Unreleased]` → *Changed* (kırıcı): önce sessizce yok sayılan
  okunamayan değer artık açılışı durdurur; mesaj anahtarı adlandırır.

## 247.6 — Kapılar

- **Kaynak taraması** (mimari test, öneri adı `UnreadableConfigurationSiteTests`;
  desen `TimeoutSiteTests`): `src/` altındaki bağlayıcı dosyalarında
  `if (… .TryParse(`, `TryReadBool(`, `TryInt(`, `TryTimeSpan(` ve sessiz `Read*(section`
  kalmaz. Tarama ifadesi uygulamada ölçülür; bilerek kalan site gerekçeyle listelenir.
- **Davranış probu** (`OptionsKeyCoverage` içinde): Core ve Abstractions'taki her
  public, string olmayan skaler üye için okunamayan metin bağlanır ve
  `FailsAtStartup` anahtarı adlandırmalıdır. Paket tipleri (F-509 boşluğu) prob
  ucuzsa eklenir; değilse kaynak taraması onları kapsar.

---

## Planlanan Public API

Yok. Okuyucu, taşıyıcılar ve doğrulayıcı `internal`. Public seçenek tipleri
değişmez; değişen kabul edilen değer kümesidir.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
ConfigurationValueReader.cs   (yeni, internal; yeri Açık Soru 4)
src/Tracon.Core/Configuration/UnreadableConfigurationValidator.cs      (yeni, internal; yer uygulamada)
src/Tracon.Core/TraconServiceCollectionExtensions.Binding.{Core,Operations,Security,Models}.cs
src/Tracon.Core/TraconServiceCollectionExtensions.Registration.*.cs   (doğrulayıcı kaydı)
src/Tracon.Core/Voice/{LiveVoice,VoiceConversation}BuilderExtensions.cs
src/Tracon.Providers.Shared/ProviderRegistrationCore.cs
src/Tracon.{Mcp,Voice,OpenAI,Anthropic,Google,Azure}/…ProviderExtensions.cs / BuilderExtensions.cs
src/Tracon.{PostgreSql,Sqlite,SqlServer}/Tracon*BuilderExtensions.cs
seçenek tipleri: internal taşıyıcı üyeleri
tests/Tracon.Core.UnitTests/Configuration/*BindingTests.cs, OptionsKeyCoverage.cs
tests/Tracon.Core.UnitTests/Architecture/UnreadableConfigurationSiteTests.cs   (yeni)
tests/Tracon.{Mcp,Anthropic,…}.UnitTests/*BindingTests.cs
docs-site/src/content/docs/reference/configuration.md, concepts/governance.md
CHANGELOG.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| `DefaultMaxOutputBytes = "10MB"` sessizce sınırsız kalır (**önce kırmızı**) | Birim (`SectionBinding.FailsAtStartup`) | `TraconOptionsBindingCoverageTests` (`:101` bugünkü bağlama case'inin yanına) |
| Her tip için temsilci okunamayan metin geçer: bool `"evet"`, TimeSpan `"30s"`, enum (`RateLimit.Partition`), decimal virgüllü (`"1,5"`), long taşma | Birim | ilgili `*BindingTests` |
| Birden çok hata yalnız ilki raporlanır | Birim | `UnreadableConfigurationValidatorTests` (yeni) |
| Bağlamadan sonra kodda yazılan değer hâlâ hata verir | Birim | aynı sınıf (K-993 kuralı) |
| Boş ya da boşluk değer hata verir | Birim | aynı sınıf |
| Liste anahtarına tek değer yok sayılır; `[]` hata verir | Birim | `BindListTests` ya da ilgili `*BindingTests` |
| Paket bağlayıcısı sessiz kalır (Mcp, Voice, OpenAI, Anthropic, Google, Azure, SQL) | Birim | `McpOptionsConfigurationBindingTests`, `AnthropicProviderExtensionsTests`, Core'daki `VoiceLiveOptionsBindingTests` / `VoiceConversationOptionsBindingTests` + `Tracon.Voice` paketi ve üç SQL paketi için yeni bağlama testleri (bugün yok) |
| Fiyat ve katalog alanı (`ReadDecimal`, `ProviderRegistrationCore`) sessiz kalır | Birim | `TraconPricingBindingTests` + sağlayıcı paketinin katalog bağlama testi |
| Doğrulayıcı kaydedilir ama `ValidateOnStart` yoktur, host açılır | Fonksiyonel | `TraconTestHost` ile okunamayan değer → `StartAsync` `OptionsValidationException` atar |
| Yeni bir `TryParse`-atla sitesi eklenir | Birim (mimari) | `UnreadableConfigurationSiteTests` |
| Public bir skaler üye kuralın dışında kalır | Birim (mimari) | `OptionsKeyCoverage` probu |

Beş soru: **iptal** — yok (açılış) · **eşzamanlılık** — bağlama tek iş
parçacığındadır · **boş/aşırı girdi** — boş = verilmedi; taşma (`long`) okunamaz
sayılır · **başka kiracı** — yok (kurulum düzeyi) · **alt sistem hatası** — SQL
deposu `ContentProtection`'ı erken okur ve hatası tek başına görünür
(`hafiza/secenek-baglama-ve-gizlilik.md:23`); test bunu bilerek ayrı tutar.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | `samples/Tracon.Api` | `Tracon__Tools__DefaultMaxOutputBytes=10MB Tracon__RateLimit__Enabled=evet dotnet run` | Host açılmaz; tek mesaj iki anahtarı ve `'10MB'`, `'evet'` metinlerini adlandırır |
| 2 🤖 | `samples/Tracon.Api` | `Tracon__Skills__Scripts__EnvironmentAllowList=PATH dotnet run` | Host açılmaz; mesaj `:0` biçimini önerir |

Case'ler `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`'ye eklenir.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `DelegationMode` (K-1015) bağlama anında atmayı sürdürür mü | A: taşıyıcıya geçer, tek mesaja katılır (test `OptionsValidationException` bekler) · B: olduğu gibi kalır | A — tek kural, tek istisna tipi; K-1015 satırına not düşülür |
| 2 | Kaynak taraması hangi dosyaları "bağlayıcı" sayar | A: `*BuilderExtensions.cs`, `*ProviderExtensions.cs`, `TraconServiceCollectionExtensions.Binding.*.cs`, `ProviderRegistrationCore.cs` · B: bütün `src/` | A — `TryParse` bağlayıcı dışında meşru kullanılır; liste uygulamada ölçülür |
| 3 | XML vaatlerine "a value that cannot be read" eklenir mi | A: ekle · B: mevcut metin yeter | Uygulamada okunur; vaat doğruysa B |
| 4 | Okuyucu nerede yaşar | A: `Tracon.Abstractions` `internal` + beş sağlayıcı paketine `InternalsVisibleTo` · B: paylaşılan kaynak dosya (`Tracon.Sql.Shared` / `Tracon.Providers.Shared` emsali, `<Compile Include … LinkBase>`) | B — paylaşılan kaynak paket grafiğini ve friend listesini büyütmez; her paket kendi kopyasını derler |

---

## Kapsam Dışı

- F-513 — `MaxAgeDays: null` ile saklamayı kapatmak (yeni anlam; `null` 10.0.0'da yokla ayırt edilemez). Aynı siteye (`Binding.Security.cs:460`) dokunulur ama anlam eklenmez.
- F-512 — doğrulayıcısı olmayan seçenek tiplerine aralık doğrulayıcısı.
- F-509 / F-510 — bölüm cırcırının ve bağlama kanıtı kapısının boşlukları (prob ucuzsa paket tiplerine genişler, bkz. 247.6).
- F-357 — `ValidateOnStart`'sız `IValidateOptions` kapısı (bugün canlı kusur yok).

---

## Bitiş Ölçütleri (DoD)

- [ ] `Tracon:Tools:DefaultMaxOutputBytes = "10MB"` ile host açılmaz; mesaj anahtarı ve metni alıntılar
- [ ] 181 sessiz site, 8 fiyat okuması ve 10 katalog alanı aynı kurala bağlı; kaynak taraması sıfır sessiz site raporlar
- [ ] Birden çok okunamayan değer tek mesajda listelenir; bağlamadan sonra kodda yazılan değer kazanır
- [ ] Liste anahtarına tek değer açılışı durdurur ve `:0` önerir; `[]` ve boş değer varsayılanı korur
- [ ] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`)
- [ ] `samples/Tracon.Api` ile gerçek açılış denendi (manuel case 1–2) ve geçerli yapılandırmayla gerçek `run` yapıldı; çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case'ler `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içinde ve koşuldu; gerisi otomatik testte
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (247.5); `npm run build` + `check-links.mjs` temiz; `CHANGELOG.md` *Changed* satırı yazıldı
- [ ] K-993'ün skalere genişlemesi `docs/KARARLAR.md`'ye yazıldı (reddedilen alternatifler: uyar ve atla · anahtar başına sınıflandırma · bağlama anında atmak)

### Doğrulama komutları

```bash
# Önce kırmızı
python3 scripts/kapi.py test --proje Tracon.Core.UnitTests --sinif "*TraconOptionsBindingCoverageTests*"
# Örnek uygulama açılmamalı
cd samples/Tracon.Api && Tracon__Tools__DefaultMaxOutputBytes=10MB dotnet run --no-build -c Release
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Bugün yazım hatasıyla çalışan bir kurulum güncellemeden sonra açılmaz | Kırıcı değişiklik `CHANGELOG` *Changed*'da; mesaj anahtarı ve metni adlandırır, düzeltme tek satırdır |
| Varsayılanla aynı değeri kodda yazan tüketici hâlâ hata alır | K-993'ün kabul ettiği sınır; site yazar |
| Bir paket bağlayıcısı unutulur | Kaynak taraması bütün bağlayıcı dosyalarını kapsar (Açık Soru 2) |
| Okuyucu sağlayıcı paketlerinden görünmez (friend listesi beş paketi kapsamaz) | Açık Soru 4; paylaşılan kaynak önerilir |
| Mesaj `secret` içeren bir değeri alıntılar | Skaler sayı/bool/süre anahtarları `secret` taşımaz; `ApiKey` gibi string anahtarlar bu kurala girmez — mesaj yalnız skaler okuyucudan çıkar |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.
