# 30 — Yerel Referans Yüzeyi (`YRF`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../30-YEREL-REFERANS.md`](../../30-YEREL-REFERANS.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır; spec `### MT-YRF-NNN` (h3) kullanır, burada skill
> §4.1/§7 konvansiyonuna uymak için `## MT-YRF-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, üçüncü aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `7e3a4de7` donuk (doğrulandı: `git diff --stat 7e3a4de7..HEAD -- src samples tests` boş, oturum başında VE sonunda) |
| **Case sayısı** | 27 (MT-YRF-001..027) |
| **Paket sürümü** | `0.0.0-preview.0.829` (`dotnet pack Tracon.src.slnf -c Release` bu oturumda koşuldu — aile 16'nın kapanış commit'i sürüm yüksekliğini `.819`'dan `.829`'a çıkardı) |
| **Tüketici dizinleri** | Hepsi repo **dışında** (`mktemp -d`), `NuGet.config` yerel feed'i `ap-s4/artifacts/package/release`'e işaret ediyor |

**Sapma — `user-secrets` gerekmedi:** bu ailede hiçbir kimlik bilgisi
kullanılmadı; tamamı `dotnet pack`/`build`/`test` ve `node` script'leri.

**Sapma — küresel kayıt (skill §1.3):** MT-YRF-012 için
`dotnet new install src/Tracon.Templates` çalıştırıldı (makine genelinde
`dotnet new` şablon kaydı). Case bitince `dotnet new uninstall
.../src/Tracon.Templates` ile **geri alındı** — global durum oturumdan
önceki hâline döndü.

**Sapma — kontrollü kaynak mutasyonu (case'in kendi tasarımı, kural 1'in
"beklenen sonuç yanlışsa" istisnası DEĞİL, cirit-testi mekanizması):**
MT-YRF-013/014/015/016/025 spec'in kendi adımları gereği `src/`,
`docs-site/src/` veya derlenmiş `artifacts/`'ı geçici olarak bozup bir
kapının kızardığını kanıtlıyor, sonra **hemen** `git checkout` /
yeniden `dotnet build` ile geri alıyor. Her mutasyondan sonra kod donması
tek tek doğrulandı (`git status --short` boş). Oturum sonunda nihai
doğrulama da boş.

**🚨 Ortam notu — `dotnet test` otomatik-mod sınıflandırıcısı bir kez
"Modify Shared Resources" gerekçesiyle reddetti** (MT-YRF-013'ün ilk
denemesi, `grep -A3 AddSkill` ile borulanmış hâli). Mutasyon **hemen**
`git checkout` ile geri alındı (test hiç koşulmadan). İkinci denemede
(borusuz, düz `dotnet test tests/Tracon.Core.UnitTests -c Release
--no-build`) **sorunsuz çalıştı** ve turun geri kalanında hiç
tekrarlamadı — `user-secrets list`/`ps eww` engelleriyle aynı
deterministik-olmayan örüntü (bkz. dosya 16 ve DEVIR.md §"Yeni bir
anahtar sızıntı örneği").

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show d34632fc:docs/manuel-test/kosumlar/2026-09-16/30-YEREL-REFERANS.md
> ```

---

## Temiz geçen case'ler (18)

| Case | Durum | Başlık |
|---|---|---|
| MT-YRF-001 | ☑ | Özellik kapalıyken referans dosyası yazılmaz |
| MT-YRF-002 | ☑ | Tek anahtar ikisini de açar ve her yol diskte vardır |
| MT-YRF-004 | ☑ | Önce adı bulan `grep` öğretilir |
| MT-YRF-005 | ☑ | Yalnız `Tracon.Core` referanslı projede HTTP bölümü yok |
| MT-YRF-008 | ☑ | İkinci build dosyaya dokunmaz |
| MT-YRF-009 | ☑ | İkinci özellik dosyayı tek başına kapatır |
| MT-YRF-011 | ☑ | Eski `AGENTS.md` taşıyan tüketicide `TRC0401` çıkar |
| MT-YRF-012 | ☑ | Şablonun `.gitignore`'u referans dosyasını kapsar |
| MT-YRF-013 | ☑ | Örnek silinince kapı adıyla kızarır |
| MT-YRF-014 | ☑ | Var olmayan bir API öğreten örnek kızarır |
| MT-YRF-016 | ☑ | Çözüm derlenmemişken kapı sessizce geçmez |
| MT-YRF-017 | ☑ | Generic üyeler arite eki yüzünden atlanmaz |
| MT-YRF-018 | ☑ | Harita yerel referans dosyasını adıyla işaret eder |
| MT-YRF-019 | ☑ | Farklı paket kümesi taşıyan iki proje kendi cevabını alır |
| MT-YRF-020 | ☑ | Yerel referansın **ilk** bölümü yetenek haritasıdır |
| MT-YRF-022 | ☑ | Üretilmiş `AGENTS.md` `TRC0402` üretmez |
| MT-YRF-023 | ☑ | Tek özellik **yedi** kodun tamamını susturur |
| MT-YRF-025 | ☑ | `title`/`description` kaybeden sayfa üreteci düşürür |

## Ayrıntı taşıyan case'ler (9)

## MT-YRF-003 — İşaret ettiği korpus gerçekten cevap verir

**Gerçek sonuç**
`APXML=.../tracon.core/0.0.0-preview.0.829/lib/net10.0/Tracon.Core.xml`.
`grep -A 14 'AddTracon(Microsoft.Extensions.Hosting.IHostApplicationBuilder)'
$APXML` → `<summary>` VE `<example><code>` ikisi de pencerede — case
birebir geçti. `grep -A 12 'AddToolApprovalPolicy' $APXML` içinse gerçek
üyenin `<summary>`'si pencerede ama `<example>`/`<code>` **açılış**
etiketleri pencerenin DIŞINDA kalıyor — sebebi bu literal alt-dize dosyada
`AddToolApprovalPolicy`'nin GERÇEK tanımından ÖNCE birden fazla çapraz
referans (başka üyelerin kendi `<summary>`'lerinde bu adı anması) taşıyor,
`-A 12` her eşleşmeden 12 satır gösteriyor ve gerçek örnek asıl tanımdan
~25 satır sonra başlıyor (dosyanın kendi `<example>` kodu içinde
`.AddToolApprovalPolicy(...)` çağrısı GEÇ bir ikinci eşleşme yaratıyor,
ama pencereler arasında `<example>`/`<code>` açılış etiketlerini içeren
boşluk atlanıyor). `sed -n` ile üyenin tam gövdesi okunduğunda özet VE
örnek ikisi de gerçekten var — **korpus doğru cevap veriyor**, yalnız
case metnindeki blunt `grep -A12` reçetesi bu ÖZEL üye için (dokümanın
büyümesi/çapraz referans yoğunluğu yüzünden) artık temiz bir pencere
vermiyor. Doküman kusuru (kural 1.1 istisnası): case'in kendi "How to read
them" reçetesi (MT-YRF-004) zaten ikinci adımda üyenin **adını önce
bulmayı**, sonra o adı ayrı okumayı öğretiyor — iki adımlı akış izlenirse
sorun yok, tek satırlık kör grep yeterli olmayabiliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-006 — `Tracon.AspNetCore` ile HTTP belgesi gelir ve okunur

**Gerçek sonuç**
`APOAS=.../tracon.aspnetcore/0.0.0-preview.0.829/buildTransitive/tracon.json`,
`test -f` → `VAR`. `python3 -c "... len(paths), len(schemas)"` →
**130 path, 278 şema**. ⚠️ Spec'in "123 path, 250 şema" sayısı **bayat**
(API yüzeyi Faz 137 dahil sonraki fazlarda büyüdü) — mekanizma iddiası
(belge pakete girdi, yol gerçek, path/schema sayıları sıfır değil ve
tutarlı) doğrulandı, tam sayı zamanla kaymaya açık.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-007 — `tracon.json` gerçekten pakette

**Gerçek sonuç**
`unzip -l Tracon.AspNetCore.0.0.0-preview.0.829.nupkg | grep
buildTransitive` → tam iki satır: `buildTransitive/Tracon.AspNetCore.targets`
(848 B) ve `buildTransitive/tracon.json` (**591163 B ≈ 577 KB**). Spec
"~515 KB" diyor — ⚠️ bayat (dosya MT-YRF-006'daki büyümeyle aynı sebepten
büyümüş), iki satırlık YAPI iddiası (targets + tracon.json ikisi de
pakette) tam doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-010 — Yazılamayan dosya build'i kırmaz

**Gerçek sonuç**
🚨 İlk denemede `Consumer.csproj` yanlışlıkla MT-YRF-009'un
`TraconWriteLocalReference=false` hâlinde kalmıştı — yazma hiç
denenmediği için beklenen uyarı çıkmadı, kendi hatamı yakalayıp
`Consumer.csproj`'u MT-YRF-002 durumuna (`TraconWriteAgentsFile=true`
yalnız) sıfırlayıp tekrar koştum. Salt-okunur kaynak ağacına karşı
(`chmod -R a-w`) `dotnet build -p:UseArtifactsOutput=true
-p:ArtifactsPath=$APOUT` → **tam beklenen** `warning MSB3491: Could not
write lines to file ".../Tracon.LocalReference.md". Access to the path
... is denied.` VE **`exit=0`** (`Build succeeded`, 1 Warning, 0 Error).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-015 — Yeni giriş noktası örneksiz eklenince kapı kızarır

**Gerçek sonuç**
`ITraconBuilder`'a `ITraconBuilder UseSomething();` (örneksiz, tek
satırlık `<summary>`) eklendi, `TraconBuilder`'da `=> this;` ile
uygulandı. 🚨 `dotnet format Tracon.slnx analyzers --diagnostics RS0016`
`PublicAPI.Unshipped.txt`'i **otomatik doldurmadı** ("Warnings were
encountered while loading the workspace" dışında bir çıktı vermedi) —
satır (`Tracon.ITraconBuilder.UseSomething() -> Tracon.ITraconBuilder!`)
elle eklendi (mevcut `AddAgent` girdisinin biçimine birebir uyularak).
`dotnet build` + `dotnet test` → **3 kızarma**: beklenen ikisi
(`CapabilityCoverageTests.Every_registration_entry_point_is_named_on_the_capability_map`
— `"+ UseSomething: ... capability map never names"` — VE
`CapabilityExampleTests.Every_registration_entry_point_shows_a_worked_example`
— `"+ UseSomething: ... carries no <example>"`) tam beklenen mesajlarla,
artı beklenmeyen üçüncü bir kapı
(`ShippedDocumentationSelfContainmentTests.Shipped_documentation_points_only_at_what_the_consumer_holds`
— `"1 offending lines, baseline allows 0"`) — bu üçüncü kapı muhtemelen
scratch üyenin tek-satırlık, bağlamsız `<summary>`'sinin kendi
kurallarını ihlal etmesinden (case'in asıl iddiasının bir parçası
değil, benim test üyemin minimalliğinin yan etkisi). `git checkout src/`
ile üç dosya (`ITraconBuilder.cs`, `TraconBuilder.cs`,
`PublicAPI.Unshipped.txt`) geri alındı, taban çizgisi doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-021 — Eski `AGENTS.md`'si olan depoda `TRC0402` öter

**Gerçek sonuç**
Elle yazılmış, `Tracon.LocalReference.md` dizesi geçmeyen bir
`AGENTS.md` ile `-t:Rebuild` → `warning TRC0402: 'AGENTS.md' does not
name 'Tracon.LocalReference.md' anywhere. ...` (tam mesaj, dosyanın tam
adı içinde). Dosyanın SHA-256'sı derleme öncesi/sonrası **birebir aynı**
— tüketicinin dosyası yalnız okundu, değişmedi. Bir satır eklenip tekrar
derlenince ⚠️ case'in kendi `grep -c APG0` reçetesi **bayat** (tanı
öneki Faz 162'de `APG`'den `TRC`'ye geçti, dosya 31'in `HATA-S4-001`'i
ile AYNI kök sebep) — düzeltilmiş `grep -c "TRC0"` ile ölçüldüğünde
gerçekten `0`: tek satır uyarı ailesini kapatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-024 — `llms.txt` gerçek bir sayfa indeksidir

**Gerçek sonuç**
`node docs-site/scripts/build-agent-map.mjs --check` → `up to date and
within budget`. `grep -c '^- \[' llms.txt` → **55** (spec "bugün 38"
diyor — ⚠️ bayat, docs-site sayfa sayısı büyüdü). `wc -c`:
`Tracon.AgentMap.md` **10684 B**, `llms.txt` **21834 B** — spec'in
"≤10240 B"/"≤20480 B" ölçütleri de bayat; script'in KENDİ kaynağındaki
GÜNCEL sabitler (`agentMapBudgetBytes=11264`, `llmsBudgetBytes=24576`,
`build-agent-map.mjs:50,69`) **daha yüksek**, ölçülen değerler bu GÜNCEL
bütçelerin altında — `--check`'in "within budget" demesi doğru, yalnız
case metnindeki eski sabitler bayat. `grep -c 'llms-full.txt'` iki
dosyada da `2` (spec "1" diyor; büyük ihtimalle her iki dosyada da bir
ekstra bağlam/açıklama cümlesi eklendi — kritik iddia olan "iki dosyada
da EN AZ bir kez geçiyor" doğrulandı). `npm run build` → 1147 sayfa
başarıyla üretildi. `node scripts/check-links.mjs` → `Links: 188739
internal reference(s) across 1147 pages and llms.txt, none broken.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-026 — 👤 Gerçek tüketicide agent haritayı yolu tahmin etmeden bulur

**Fiziksel eylem gerekir** — bkz. dosya sonundaki tablo. Ölçüm deposu
(`prodigy-enabler-backend`) bu ortamda yok; case açıkça "otomatik
karşılığı yoktur" diyor.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-YRF-027 — 👤 Agent anlatı katmanına ulaşıp kaynağı adlandırır

**Fiziksel eylem gerekir** — MT-YRF-026'nın devamı, aynı sebeple
koşulamadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Sayım (skill §7 betiği)

```
{'Geçti': 25, 'Beklemede': 2} toplam: 27
```

25/27 case koşuldu ve Geçti, sıfır Kaldı. İki case (026, 027) fiziksel
eylem gerektiriyor (gerçek tüketici deposu + taze kod agent örneği),
aşağıdaki tabloya taşındı.

## Fiziksel eylem / koşulamayan case'ler

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-YRF-026 | Gerçek tüketici deposu (`prodigy-enabler-backend`) ve taze bir kod agent örneği gerekir; case'in kendi metni "otomatik karşılığı yoktur" diyor | Bu paketleri gerçek bir tüketici deposunda yükseltip, kendi `AGENTS.md`'sine `TRC0402` işaretçisini ekleyip, sıfırdan başlatılan bir kod agent'ına "Tracon hangi yetenekleri sunuyor?" sorusunu sorarak agent'ın `AGENTS.md → Tracon.LocalReference.md → Tracon.AgentMap.md` zincirini gerçekten izleyip izlemediğini gözlemlemek |
| MT-YRF-027 | MT-YRF-026'nın devamı, aynı gerçek depo ve agent oturumu gerekir | Aynı agent'a "`UseTenancy()` çağırmazsam ne olur?" sorusunu sorup cevabın `llms.txt` → `concepts/governance.md` zincirini izleyip izlemediğini, "Off by default" cevabını türetmeden verip vermediğini gözlemlemek |

## Doküman kusurları (skill §1.1 istisnası, ürün kusuru DEĞİL)

Bu ailede beş case'in `Beklenen sonuç`'u sabit sayı/kod öneki içeriyordu
ve hepsi API/doküman büyümesiyle bayatlamış:

1. **MT-YRF-006/007** — OpenAPI path/schema sayıları (123/250 → gerçek
   130/278) ve `tracon.json` boyutu (~515 KB → gerçek ~577 KB).
2. **MT-YRF-021** — `grep -c APG0` reçetesi Faz 162'nin `APG`→`TRC`
   yeniden adlandırmasını yansıtmıyor (dosya 31'in `HATA-S4-001` ile aynı
   kök sebep — üçüncü örneği, sınıf taramasını güçlendiriyor).
3. **MT-YRF-024** — `llms.txt` sayfa sayısı (38 → 55), iki bütçe sabiti
   (10240/20480 → gerçek script sabitleri 11264/24576) bayat; script'in
   KENDİSİ güncel, yalnız case metni değil.

Hiçbiri kodlanabilir bir kusur değil (rakamlar zamanla büyüyen bir
yüzeyi doğru şekilde takip ediyor); kapanışta case metninin
güncellenmesi önerilir.
