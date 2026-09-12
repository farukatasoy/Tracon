# Faz 57 — Kod Dili Birleştirme (İngilizce)

> **Durum:** ✅ Tamamlandı (2026-08-16)
> **Kaynak:** Kullanıcı isteği (2026-08-15). Aday listesinde karşılığı **yoktur** —
> bu bir yetenek değil, paket kalitesi işidir.
> **Önkoşul:** Adım 0 (doküman bütçesi rahatlatması) — bu fazın **kapanışı** ona bağlı;
> `faz-tamamlama` `MEMORY.md`'ye yazar ve orada 1 bayt boşluk vardır.
> **Paketler:** Tümü (17 yayınlanan + `Generators`) · `tests/` · `samples/`
> **Yeni paket:** Yok · **Migration:** Yeni migration yok; **var olan 61 dosyanın
> checksum'ı değişir** (bkz. 57.4)
> **Public API:** **Değişmiyor.** İmzalar zaten İngilizce; yalnız XML doküman
> *içeriği* ve string *değerleri* değişir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/57-KOD-DILI-BIRLESTIRME.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'in kodu Türkçe yorumlanmış, İngilizce adlandırılmış bir kütüphanedir. Bu, geliştirme boyunca doğru tercihti. Ama `GenerateDocumentationFile` açık olduğu için **XML doküman `.xml` dosyası olarak NuGet paketine giriyor**; paketi kuran bir geliştirici IntelliSense'te Türkçe açıklama görüyor.

## Bitiş Ölçütleri (DoD)

- [x] `source-language-baseline.txt` **boş**; `SourceLanguageTests` yeşil —
      789/789 test geçti, taban çizgisi dosyası yalnız başlığı taşıyor
- [x] `grep -rln '[çğıöşüÇĞİÖŞÜ]' src/ tests/ samples/` yalnız `locales/tr.ts`,
      `locales/en.ts`, `SourceLanguageTests.cs` (sözlüğün kendisi) ve
      `UiTests.cs` (K-228, çevrilmiş metni doğrulayan tek satır) döndürür
- [x] Üretilen XML doküman gözle denetlendi:
      `artifacts/bin/Tracon.Abstractions/release_net10.0/Tracon.Abstractions.xml` —
      İngilizce, Türkçe karakter sıfır
- [x] `dotnet pack` çıktısındaki `.nupkg` açıldı; `lib/net10.0/*.xml` İngilizce —
      `Tracon.Abstractions`/`Tracon.Core` paketlerinde Türkçe karakter sıfır
- [x] `dotnet new tracon-api --help` çıktısı İngilizce — doğrulandı, tüm
      seçenek açıklamaları (`--persistence`, `--provider`, `-ui`) İngilizce
- [x] 🚨 **Faz öncesinden kalan** bir PostgreSQL veritabanına karşı
      `samples/Tracon.Api` çalıştırıldı; migration checksum yordamı
      (düşür + yeniden kur) belgeye yazıldı — bkz. K-409 ve aşağıdaki gerçek koşum
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü (kod dizinlerinde; `docs/manuel-test/**`teki
      sahte test anahtarları/yerel dev şifreleri fazdan önceden var ve kapsam dışı)
- [x] `find src -name "* 2.*" -not -path "*/node_modules/*"` boş
- [x] `AGENTS.md` dil sınırı kuralını taşıyor — "🚨 Dil sınırı" bölümü ve
      `SourceLanguageTests` referansı zaten mevcuttu

### Gerçek koşum — migration checksum riski (2026-08-16, PostgreSQL, `ap-pg` docker)

```
# 1) Temiz semaya karsi normal calistirma — 29/29 migration uygulandi, basladi
ASPNETCORE_ENVIRONMENT=Development dotnet Tracon.Api.dll  -> "Application started"

# 2) 0001_initial satirinin checksum'i elle bozuldu (var olan veritabanini simule eder)
UPDATE tracon.__migrations SET checksum = 'DEADBEEF...' WHERE name = '0001_initial';

# 3) Yeniden baslatma -- BEKLENEN COKUS
fail: Microsoft.Extensions.Hosting.Internal.Host[11] Hosting failed to start
Tracon.TraconException: Migration '0001_initial' has been applied to the
database but the file's content has changed. Checksum in the database:
DEADBEEF...  checksum of the file: ADFA1197...
  at Tracon.MigrationRunner.VerifyChecksum(...)
Unhandled exception. (process exits non-zero)

# 4) Kurtarma -- sema dusurulup yeniden kuruldu
DROP SCHEMA tracon CASCADE;  -- 46 nesne dusuruldu
# yeniden baslatma: 29/29 migration sifirdan basariyla uygulandi, "Application started"
```

**Sonuc:** DoD'nin öngördüğü risk gerçek bir PostgreSQL koşumunda doğrulandı —
uygulanmış migration checksum'ı değişince uygulama başlangıçta güvenli biçimde
çöküyor (veri kaybı riski yok, sessiz devam yok); kurtarma yordamı (şema
düşür + yeniden kur) tek komutla çalışıyor. Testler bu senaryoyu YAKALAYAMAZ
(Testcontainers her koşumda sıfır bir veritabanı kurar) — bu doğrulama yalnız
elle, var olan bir veritabanına karşı yapılabilir.

### Gerçek `run` (2026-08-16, `samples/Tracon.Api`, PostgreSQL, gerçek `openai`/`anthropic`/`google` çağrıları)

Dört doğrulama kapısı (`build`/`test`/`pack`/`format`) ve örnek uygulama
İngilizce yapılandırılmış bir PostgreSQL veritabanına karşı gerçek çağrılarla
doğrulandı; diagnostics ucu `persistenceProvider: "PostgreSql"`,
`migrationsUpToDate: true`, `pendingMigrations: []` döndü.

### Doğrulama komutları

```bash
# Cırcır kapısı
dotnet test tests/Tracon.Core.UnitTests -c Release --no-build

# Pakete giren XML doküman gerçekten İngilizce mi
unzip -p artifacts/package/release/Tracon.Abstractions.*.nupkg \
  lib/net8.0/Tracon.Abstractions.xml | head -40

# Şablon açıklaması
dotnet new tracon-api --help

# Türkçe karakter kalıntısı
grep -rln '[çğıöşüÇĞİÖŞÜ]' src/ tests/ samples/ --exclude-dir=node_modules
```

---

## Plandan Sapmalar

1. **57.1 kapısı iki kalıcı istisna kazandı, plan bunu öngörmüyordu.** `AllowedLinePattern`
   regex'i `shell.language.tr`'nin yanına ikinci bir kalıcı satır aldı:
   `Gösterge Paneli` — `tests/Tracon.Ui.E2ETests/UiTests.cs`'deki lokalizasyon
   E2E testleri gerçekten çevrilmiş Türkçe başlığı doğruluyor (K-228). Baştan
   sona İngilizce bir kod tabanında bu tek satır meşru bir istisnadır, borç değil.
2. **`SourceLanguageTests`'in kelime sınırlı taraması camelCase'e yapışık Türkçe
   kökleri kaçırdı — elle ikinci bir tarama gerekti.** `SahteTokenKimligi`,
   `IstenenTokenSayisi`, `SonKapsam` (`tests/Tracon.Azure.UnitTests/`) ve
   iki kaçak XML doküman cümlesi (`AttachmentTypeGuard.cs`) otomatik kapıdan
   geçti ama gerçekte Türkçe kaldı. 20 dosyada tekrarlayan bir `<paramref .../>
   <see langword="null"/> ise.` kalıbı da (33 oluşum, `Sql.Shared/Stores/*`)
   aynı sebeple kaçtı. Hepsi elle grep ile bulunup düzeltildi; kalıcı ders
   `docs/hafiza/workflows.md`'ye değil (o alana özgü değil) burada kayıtlıdır.
3. **Çeviri iki bağımsız, doğrulanmamış varsayılana dayanan iki test bulgusu
   ortaya çıkardı — ikisi de düzeltildi, plan bunları öngörmüyordu:**
   - `AzureOpenAIProviderExtensionsTests.Catalog_comes_from_configuration_and_is_reflected_in_the_provider`:
     `"deneme-gpt"` → `"test-gpt"` çevirisi, `AzureOpenAIModelCatalog.Build`'in
     isme göre ALFABETİK sıraladığı gerçeğiyle çarpıştı — `"deneme" < "production"`
     ama `"test" > "production"`; beklenen sıra tesadüfen doğruydu, çeviri
     tesadüfü bozdu. Düzeltme: beklenen sırayı belgelenen sözleşmeye göre yaz.
   - `tests/Tracon.Ui.E2ETests/Infrastructure/ApprovalWorkflow.cs`'nin
     `WithName(...)` değeri ile `UiHost.cs`'nin `AddWorkflow("approval-flow", ...)`
     KAYIT ANAHTARI hiçbir zaman eşleşmiyordu (öncesi: `onay-akisi` ↔
     `approval-flow`) — yönlendirme kayıt anahtarına göre çalışır, `WithName`'e
     göre değil (bkz. `docs/hafiza/workflows.md`). Çeviri her iki dizgeyi de
     aynı şekilde çevirdiği için tutarsızlık ilk kez GÖRÜNÜR oldu (49 E2E
     testinden ikisi 30 saniye zaman aşımına uğradı). Düzeltme: üç yerde de
     (kayıt anahtarı, `WithName`, test URL'i) aynı dizge — `approval-flow`.
4. **`docs/hafiza/build-ve-analyzer.md` ve `docs/hafiza/sql-saglayicilari.md`'ye
   dokunulmadı** — plan bu iki alan dosyasını "Bu Faza Başlarken" listesinde
   önerdi ama fazın kendisi bu dosyaların içeriğini değiştirecek bir build/analyzer
   ya da SQL sağlayıcı kuralı üretmedi; yalnız `docs/hafiza/workflows.md`'ye yeni
   bir tuzak eklendi (madde 2, 3).
5. Bayat bulunan Türkçe yorum yoktu — çeviri sırasında kodla karşılaştırılan
   her yorum güncel davranışı doğru anlatıyordu; yalnız dil değişti.

## Bu Fazda Verilen Kararlar

`docs/KARARLAR.md`'ye K-408 – K-410 eklendi:

- **K-408** — Kaynak dili sınırı kalıcı kural oldu (kullanıcı kararı).
- **K-409** — Migration yorumu politikası: temizlendi, checksum değişti,
  var olan veritabanı düşürülüp yeniden kurulmalı (kullanıcı kararı) —
  gerçek koşumla doğrulandı (yukarıdaki DoD bölümü).
- **K-410** — `SourceLanguageTests` cırcır kapısı: taban çizgisi güdümlü,
  kelime sınırlı tarama; camelCase sınırlaması not edildi.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**

- Kaynak dili sınırı kalıcıdır (K-408): pakete giren veya çalışma anında
  çalışan her şey İngilizce. `SourceLanguageTests` bunu her fazda zorlar;
  `source-language-baseline.txt` **boş** başlar ve yalnız küçülebilir —
  yeni bir dosyaya yanlışlıkla Türkçe yorum/metin eklenirse build kırılır.
- Yeni migration `.sql` dosyaları İngilizce yorumla yazılır (K-409); zaten
  `AGENTS.md`'de yazılıydı, bu faz onu kanıtladı.

**Bilinen tuzaklar (🚨):**

- `SourceLanguageTests`'in kelime sınırlı taraması **camelCase'e yapışık**
  Türkçe kökleri kaçırır (`SahteTokenKimligi` gibi) — yeni bir tip/üye adı
  yazarken yalnız otomatik kapıya güvenme, `grep -rnoiE` ile ayrıca kontrol et.
  Ayrıntı ve örnek desen: bu dokümanın "Plandan Sapmalar" madde 2'si.
- Bir kod-tanımlı workflow eklerken `AddWorkflow(name, ...)`'in kayıt anahtarı
  ile `WorkflowBuilder.WithName(...)`'in değeri **aynı olmak zorunda değildir
  ama tutarsız olmaları sessizce 404/zaman aşımı üretir** — yalnız kayıt
  anahtarı yönlendirmede okunur. Ayrıntı: `docs/hafiza/workflows.md`.
- `samples/Tracon.Api`'yi elle çalıştırırken `dotnet <dll>` yerine
  `ASPNETCORE_ENVIRONMENT=Development` **şart** — `user-secrets` yalnız
  Development ortamında otomatik yüklenir; aksi hâlde uygulama sessizce
  `InMemory` depoya düşer ve hiçbir SQL hatası vermez (bu fazın kapanışında
  gerçek migration testinde 30 dakika kayba yol açtı).
- `Tracon.Ui.E2ETests` 49 testten **1 tanesi** tam koşumda rastgele
  zaman aşımına uğrayabilir (izole çalıştırıldığında hep geçer) — Playwright'ın
  paralel tarayıcı yükü altında bilinen bir kararsızlıktır, fazdan bağımsızdır.

**Sıradaki faz:** `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`'ye ve README'nin yol
haritası tablosuna bakın (**Faz 58 — Doküman Düzeni**).
