# Faz 130 — Üretilen Şemanın Kısıtları

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-09-01-tuketici-feature-talepleri.md](../../kesif/2026-09-01-tuketici-feature-talepleri.md) — **F-173**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Generators` (tek paket)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyümüyor — üretilen şema metni değişir, C# yüzeyi değişmez. Yeni bir analyzer diagnostic kodu eklenir (`APG0010`)
> **Tüketici yüzeyi:** `docs-site/`: `guides/write-your-own-tool.md`, `concepts/tools.md`, `capabilities.md` (diagnostic bağlantısının hedef bölümü) · sevk edilen: `APG0003` metni, yeni `APG0010` metni, `AnalyzerReleases.Unshipped.md`
> **Manuel test alanı:** [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8a8e41b:docs/arsiv/fazlar/130-URETILEN-SEMANIN-KISITLARI.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Generator bugün tipi ifade eder, **kısıtı** ifade etmez. Model `count` parametresini `integer` olarak görür; "1 ile 100 arasında" bilgisini görmez. Bu bilgi tool gövdesinde kalır. Model geçersiz argümanı gönderir, tool onu reddeder, tur ve token boşa gider. Bu faz, sık kullanılan JSON Schema kısıtlarını üretilen şemaya taşır.

## Bitiş Ölçütleri (DoD)

- [x] `[Range]`, `[MinLength]`, `[MaxLength]`, `[StringLength]`, `[RegularExpression]` doğru JSON Schema anahtarlarına dönüşür (case 1–3)
- [x] Uyumsuz kısıt `APG0010` üretir, derleme başarılı kalır (case 4) — negatif değer ve argümansız `[MaxLength]` de dahil (denetim 🔴/🟡)
- [x] Aynı girdi iki derlemede **bit düzeyinde aynı** şema üretir
- [x] `tr-TR` yerelinde ondalık ayırıcı `.` kalır (case 6)
- [x] `APG0003` metni güncellendi; nested object sınırı korunur, kısıt sınırı kaldırılır
- [x] `AnalyzerReleases.Unshipped.md` `APG0010` satırını taşır
- [x] `DiagnosticIntegrityTests` yeşil (yardım bağlantısı siteyle uyumlu)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] Kısıtlı tool ile gerçek `run` yapıldı, çıktı belgeye yazıldı (case 5 — `AgentPrism.Package.Tests` üzerinden, bkz. Plandan Sapmalar #1)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (1 🔴 + 2 🟡 bulundu ve kapatıldı, bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi (`guides/write-your-own-tool.md`, `concepts/tools.md`, `capabilities.md`); `npm run build` + `check-links.mjs` temiz

### Gerçek run kanıtı (case 5)

Paketlenmiş `AgentPrism`/`AgentPrism.Testing` (yerel NuGet feed, sürüm
`0.0.0-preview.0.508`) referans alan bağımsız bir `PackageReference`
tüketicisinde, `[Range(1, 10)] int priority` taşıyan bir tool ile gerçek
`run` yapıldı (`ConstrainedToolPackageTests`'in aynı adımlarının elle tekrarı):

```
OK run=01a05d4b-981b-734d-b862-023e2f1f62b8 events=5 tools=1
SCHEMA {"type":"object","properties":{"orderId":{"description":"The order number.","type":"string"},"priority":{"description":"The priority, 1 (lowest) to 10 (highest).","type":"integer","minimum":1,"maximum":10}},"required":["orderId","priority"],"additionalProperties":false}
```

Kısıt (`minimum`/`maximum`) paketlenmiş analyzer DLL'inden gerçekten geldi;
`run` `Completed` durumunda bitti, tool bir kez çağrıldı.

### Doğrulama komutları

```bash
# Üretilen şemayı gör
dotnet build samples/... -p:EmitCompilerGeneratedFiles=true
grep -r "minimum" obj/**/generated/

# Yerel bağımsızlığı
LANG=tr_TR.UTF-8 dotnet build tests/AgentPrism.Generators.UnitTests
```

---

## Plandan Sapmalar

1. **Manuel kabul case 5'in mekanizması `samples/AgentPrism.Api` yerine `AgentPrism.Package.Tests`'e taşındı.** Plan "samples/AgentPrism.Api üzerinde tool çağırt" diyordu, ama bu proje `AgentPrism`'e `ProjectReference` ile bağlıdır (`ConsumerRunTests`'in kendi belgesi) — paketlenmiş `analyzers/dotnet/cs/` DLL'inin gerçekten bir `PackageReference` tüketicisine ulaştığını KANITLAYAMAZ, ki DoD'nin kendi paket-seviyesi satırı tam olarak bunu istiyordu ("Kısıtlı tool gerçekten paketten çıkmaz | Paket | `AgentPrism.Package.Tests`"). `ConsumerRunTests`'in aynı deseni (yerel NuGet feed → dış tüketici projesi yaz → derle → çalıştır) tekrarlanarak `ConstrainedToolConsumerProject`/`ConstrainedToolPackageTests` eklendi; `samples/AgentPrism.Api`'nin paylaşılan `OrderTools.cs`'i (onlarca başka manuel test dokümanı ve fonksiyonel testin referans verdiği `list_recent_orders`/`get_order_status`/`cancel_order` imzaları) dokunulmadan bırakıldı. Manuel case MT-CORE-112 bu gerçek mekanizmayı anlatacak şekilde güncellendi.
2. **`ParameterTypeValidator.TryCreate`'e planın taslağında olmayan bir `out EquatableArray<string> unsupportedConstraintAttributes` parametresi eklendi.** Plan yalnız `ParameterModel`e `Constraints` alanı eklenmesini gösteriyordu; APG0010'u raporlamak için "hangi attribute uyumsuzdu" bilgisinin `ToolCandidate.Create`'e taşınması gerekti — `ParameterModel`in kendisine eklemek (incremental modelin bir PARÇASI hâline getirmek) kapsam dışıydı, çünkü bu bilgi yalnız tek seferlik bir tanı raporlamak için var, şemaya asla girmiyor. `out` parametresi bunu `ParameterModel`in değer eşitliğinin dışında tutar.
3. **Kapanış kapısını koşarken, bu fazla TAMAMEN ilgisiz iki bayat taban çizgisi bulundu ve düzeltildi** (kullanıcı talimatı: "konuyla alakasız bug/defect'lerle karşılaşırsan onları da çöz"):
   - `tests/AgentPrism.Core.UnitTests/Architecture/PlaywrightLocatorTests` — Faz 129 `tests/AgentPrism.Ui.E2ETests/UiTests.cs`'e `GetByPlaceholder("default")` (ne `Exact = true` ne `.First`/`.Nth`) ekledi ama `playwright-locator-baseline.txt`'i hiç yenilemedi (dosyanın son commit'i Faz 93'ten kalıyordu, `UiTests.cs`'inki Faz 129'dandı). `Exact = true` eklendi — placeholder metni `jobs.tsx`'te tam olarak `"default"` ve sayfada tek örnek, bu yüzden davranış değişmedi, yalnız riskli sayım düştü.
   - `tests/AgentPrism.Sql.Shared.UnitTests/SqlTextSnapshotTests` — Faz 129'un `lane` sütunu (job kuyruğu lane'leri) on bir sorgu metnini değiştirdi ama üç `sql-text-baseline.*.txt` dosyası hiç yenilenmedi (son commit'leri Faz 126'dan kalıyordu). `AGENTPRISM_SQL_SNAPSHOT_REFRESH=1` ile yenilendi; `git diff` yalnız `lane` sütunu/parametresi ekleyen satırları gösterdi, üç dialekt arasında tutarlı — beklenmedik hiçbir fark yok.
   - İkisi de Faz 129'un KENDİ kapanışında `dotnet test AgentPrism.slnx` tam koşumunun (ya da onun sonucunun) gözden kaçtığını gösteriyor; bu faz onları kapattı.
4. **Ek: kendi eklediğim bir XML doküman satırı `ShippedDocumentationSelfContainmentTests`'i kırdı** — `ParameterTypeValidator.ReadConstraints`'in doc yorumunda "(Open Question 3)" ifadesi vardı; bu, tüketicinin hiç görmediği plan dokümanına içsel bir referanstı ve `internal-history.pattern`'in `\bopen question\s+\d+\b` kuralına takıldı. Cümle referans olmadan yeniden yazıldı.
5. **`dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1` koşumunda `AgentPrism.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript` bir kez zaman aşımına uğradı, izole koşumda hemen geçti.** Bu fazın değişikliği ses/mikrofon kodına hiç dokunmuyor; `docs/hafiza/test-altyapisi.md`'nin zaten belgelediği "tam koşum ara sıra flaky kırılır" sınıfının beşinci örneği olarak not edildi, kod değişikliği gerekmedi.

## Bu Fazda Verilen Kararlar

> Yeni bir `K-NNN` kaydı açılmadı. Plandaki üç açık soru (format üretilmesin,
> çelişen kısıt sessizce daraltılsın, `APG0010` uyarı olsun) planın kendi
> önerisiyle aynen uygulandı — bunlar yerel generator tasarım tercihleridir,
> public API/uyumluluk sözleşmesi, güvenlik/kiracı sınırı veya kalıcı
> veri/migration kararı değil (AGENTS.md). Yukarıdaki sapma #3'teki iki
> baseline yenilemesi de birer karar değil, önceki bir fazın kapanışında
> atlanan bir doğrulama adımının bu fazda tamamlanmasıdır.

## Denetim Bulguları

Taze bağlamlı bir denetçi (Agent, `general-purpose`) 2026-09-01'de koştu.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `[MinLength(-1)]`/`[MaxLength(-1)]` gibi negatif bir kısıt değeri, JSON Schema'nın `nonNegativeInteger` şartı taşıyan `minLength`/`maxLength`/`minItems`/`maxItems` anahtarlarına doğrulanmadan yazılıyordu — geçersiz bir şema sessizce üretiliyordu, `APG0010` hiç ötmedi | **Düzeltildi.** `ReadConstraints`'in `MinLength`/`MaxLength`/`StringLength` dallarına negatif-değer kontrolü eklendi; artık `APG0010` üretiyor, anahtar şemaya hiç girmiyor. Dört yeni test (`A_negative_MinLength_is_reported_and_omitted_from_the_schema` ve üç kardeşi) |
| 2 | 🟡 | `[MaxLength]` (argümansız kurucu, geçerli bir C# kullanımı) sessizce hiçbir etki üretmiyordu — ne şemaya yazılıyordu ne `APG0010` veriyordu; `Range(Type,...)`'ın aynı sınıftaki durumuyla (değer yok → uyarı) tutarsızdı | **Düzeltildi.** Aynı `TryReadSingleIntArgument` başarısızlık dalı artık `unsupported.Add("[MaxLength]")` çağırıyor; `A_parameterless_MaxLength_attribute_is_reported` testi ekli |
| 3 | 🟡 | Planın "Beş soru" bölümü `[MaxLength(0)]`, `[Range(int.MinValue, int.MaxValue)]` ve boş `pattern` case'lerinin yazılacağını taahhüt ediyordu; hiçbiri test dosyalarında yoktu | **Düzeltildi.** Üç sınır testi `ToolSchemaConstraintTests`'e eklendi (`A_MaxLength_of_zero_produces_maxLength_zero`, `A_Range_spanning_the_full_Int32_domain_produces_matching_minimum_and_maximum`, `An_empty_RegularExpression_pattern_produces_an_empty_pattern_string`) |

**🔴 ve 🟡 (yukarıdakiler dışında) yok.** Denetçinin bağımsızca doğruladığı
kalemler: DoD case 1-6'nın tümü gerçek assertion'larla kanıtlı; test
tiyatrosu yok; paket sınırı testi (`ConstrainedToolPackageTests`) gerçek bir
dış tüketici zinciri kuruyor; imza-gövde kayması yok
(`ParameterModel.Constraints` her üretim ve tüketim noktasında doğru
okunuyor/yazılıyor); plan dışı public API yok; repo kuralları (İngilizce,
`ParameterConstraints`'in yalnız ilkel alan taşıması, `isEnabledByDefault`)
ihlal edilmemiş; `write-your-own-tool.md`/`concepts/tools.md`/
`capabilities.md`/`troubleshooting.md` tutarlı; `AnalyzerReleases.Unshipped.md`
satırı ekli; SQL/Playwright baseline düzeltmelerinin GERÇEKTEN yalnız `lane`
sütunu ve tek bir riskli locator'la ilgili olduğu bağımsızca (satır satır
`git diff` incelemesiyle) doğrulandı.

Düzeltmelerden sonra dört kapı (`kapi.py kapanis --taban d5aef08`) yeniden
koşuldu — sıfır uyarı.

## Sonraki Faza Devir Notu

- Generator artık `minimum`/`maximum`/`minLength`/`maxLength`/`minItems`/
  `maxItems`/`pattern`'i, standart `DataAnnotations` attribute'larından
  okuyup deterministik sabit sırayla yazıyor; `ParameterConstraints` yalnız
  ilkel alan taşıyor (incremental önbellek güvenli).
- **Kapsam dışı bırakılan iki kalem hâlâ açık aday:** nested object/object
  array (K-615 ile uzlaşma gerektirir — generator kendi
  `JsonSerializerContext`'ini kullanmıyor) ve `format: email`/`format: uri`
  (ölçülmüş talep yok). İkisi de `docs/ADAYLAR.md`'ye taşınabilir bir sonraki
  keşif turunda.
- **`IToolArgumentsValidator` runtime'da kısıtı ZORLAMIYOR** — bu bilinçli bir
  sınırdı (bkz. "Bu faz runtime doğrulama eklemez"). Bir sonraki faz bunu
  değiştirmek isterse, `tool.JsonSchema`'yı okuyan hazır bir
  `IToolArgumentsValidator` örneği (`guides/write-your-own-tool.md`'de
  belgelenen desen) iyi bir başlangıç noktasıdır.
- **Faz 129'un kapanışı iki baseline'ı (Playwright locator, SQL text
  snapshot) yenilemeden bitmiş** — bu faz ikisini de düzeltti (Plandan
  Sapmalar #3). Sonraki fazın kapanışı `dotnet test AgentPrism.slnx`'in TAM
  ve KIRMIZI SATIR OLMADAN bittiğini `tail`'siz bir log dosyasından teyit
  etmeli; `| tail -200` bir önceki fazda erken proje sonuçlarını (alfabetik
  sırada `Core.UnitTests`, `Sql.Shared.UnitTests`) görünmez kılmıştı.
