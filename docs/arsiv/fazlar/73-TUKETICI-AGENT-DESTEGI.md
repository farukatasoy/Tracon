# Faz 73 — Tüketici Agent Desteği

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-120**
> **Önkoşul:** [Faz 52](52-KAYNAK-URETECI.md) — generator paketleme borusu ve `APG` tanı deseni oradan devralınır · [Faz 59](59-URUN-DOKUMANTASYONU.md) — `capabilities.md` ve `docs-site/scripts/` üreteç deseni
> **Paketler:** `AgentPrism.Generators`, `AgentPrism.Core` (yalnız paketleme), `AgentPrism.Templates` · `docs-site/`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Büyümüyor.** Analyzer, MSBuild target ve üretilen dosyalar public API yüzeyi değildir; `PublicAPI.*.txt` bu fazda değişmez. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` her paket için 1 satır (hepsi boş)
> **Site etkisi:** `capabilities.md` **kaynak rolü kazanır** · yeni üretilen `docs-site/public/llms.txt` ve `llms-full.txt` · yeni üreteç `docs-site/scripts/build-agent-map.mjs` · `check-content.mjs` genişler
> **Manuel test alanı:** `docs/manuel-test/29-AGENT-DESTEGI.md` (28 numarayı Faz 64 aldı)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'i entegre eden back-end uygulamalarının **kod agent'ları** paketin yeteneklerine hâkim değildir. Var olduğunu bilmedikleri yeteneği kullanmazlar; onun yerine elle yeniden yazarlar. Bu faz o boşluğu iki mekanizmayla kapatır ve mekanizmaların kod ile hizada kalmasını bir teste bağlar.

## Bitiş Ölçütleri (DoD)

- [x] `AgentPrismWriteAgentsFile` kapalıyken `dotnet build` hiçbir dosya yazmaz — `Property_unset_writes_no_file`, gerçek paket
- [x] Özellik açıkken git kökünde ≤ 10 KB `AGENTS.md` oluşur; var olan dosya ezilmez — ölçüldü: **7763 bayt**; elle düzenlenen dosya iki build sonra bayt bayt aynı
- [x] `dotnet new agentprism-api` ile oluşan projede `AGENTS.md` kendiliğinden gelir — `A_generated_project_gets_the_map_without_being_asked`
- [x] Altı tanının altısı da gerçek bir tüketici projesinde tetiklendi; çıktı aşağıda
- [x] `CapabilityCoverageTests` yeşil; taban çizgisi **boş**; iki yönde de kızardığı gösterildi (üye haritadan silinince, API'ye yeni üye eklenince)
- [x] `DiagnosticIntegrityTests` yeşil — ve denetimden sonra **gerçekten** kızardığı gösterildi (mesajdaki `AddAgentPrism()` yeniden adlandırıldı → kırmızı)
- [x] `llms.txt` (7861 B) ve `llms-full.txt` (356 KB) üretildi; `check-content.mjs` diff'i boş buldu
- [x] Dört doğrulama kapısı sıfır uyarı verir — denetim düzeltmelerinden sonra tekrarlandı: **4394 test, 0 başarısız**. İlk koşumdaki tek Playwright düşüşü ikinci koşumda tekrarlamadı (F-102/F-122 sınıfı kırılganlık; bu fazın kodu arayüze dokunmuyor)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — `claude-support`, `Completed`, SSE akışı (çıktı aşağıda)
- [x] `secret` taraması boş döndü — `src/`, `tests/`, `samples/` içinde sıfır eşleşme
- [x] Manuel kabul case'leri `docs/manuel-test/29-AGENT-DESTEGI.md` içine eklendi (15 case); on birinin tamamı otomatik testte de koşuyor
- [x] `faz-denetim` koşuldu; 3 🔴 + 4 🟡 bulgu **kapatıldı**, 🔴 kalmadı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Gerçek çıktı — altı tanı, tek tüketici derlemesi

```text
Program.cs(7,1):   warning APG0101: 'MapAgentPrism()' is called, but this compilation never calls 'AddAgentPrism()'. ...
Program.cs(16,47): warning APG0102: The model binding names provider 'anthropic', but this compilation never calls 'UseAnthropic()'. ...
Program.cs(53,41): warning APG0201: 'McpServerDefinition.AuthorizationConfigurationKey' carries a literal secret value. ...
Program.cs(22,21): warning APG0301: 'RetryingChatClient' retries a chat client call by hand. ...
Program.cs(20,21): warning APG0302: 'LoggingAgent' wraps another agent, but this compilation implements no 'IAgentDecorator'. ...
AGENTS.md(1,1):    warning APG0401: 'AGENTS.md' was generated from capability map revision '00000000', but the installed AgentPrism ships revision 'e4c7b05b'. ...
```

`rm AGENTS.md && dotnet build` sonrası: `APG0401` sayısı **0**, dosya yeniden
yazıldı (`revision: e4c7b05b`, 7763 bayt).
`-p:AgentPrismUsageDiagnostics=false` ile: altı tanının **hiçbiri** çıkmıyor.

### Gerçek çıktı — örnek uygulama

```text
POST /agentprism/api/agents/claude-support/run   → SSE
  event: run     {"runId":"01a01b53-2f08-7801-a45a-83e01e93f464"}
  event: update  ... (Anthropic gercek yaniti)
GET  /agentprism/api/runs?limit=1
  {"id":"01a01b53-...","agentName":"claude-support","status":"Completed"}
```

Depo kökündeki `AGENTS.md` **değişmedi**: örnek uygulama `ProjectReference`
kullanır, `buildTransitive` yalnız paket tüketicisine akar.

### Doğrulama komutları

```bash
# Kapali iken dosya olusmaz
dotnet build /tmp/tuketici/Tuketici.csproj && test ! -f /tmp/tuketici/AGENTS.md && echo OK

# Acik iken olusur ve butcede kalir
dotnet build /tmp/tuketici/Tuketici.csproj -p:AgentPrismWriteAgentsFile=true
wc -c /tmp/tuketici/AGENTS.md   # <= 10240

# Kapinin gercekten yakaladigi gosterilir
dotnet test --filter CapabilityCoverageTests
```

---

## Plandan Sapmalar

**1 — Harita bütçesi 6 KB değil 10 KB.** Ölçüldü: eksiksiz harita (17 paket ·
100 yetenek satırı · 12 bölüm kuralı · adresler) **7.735 bayt**. 6 KB'a
sığdırmanın tek yolu ~25 yeteneği haritadan çıkarmaktı — kapının bütün amacı
eksiksizlikti, yani bütçe içeriği değil içerik bütçeyi belirledi. Yeni bütçe
10.240 bayt (≈2500 token, bugün %76 dolu). Üreteç aşımda **hata verir**;
`check-content.mjs` ve `TemplateAgentsFileTests` aynı sayıyı zorlar.

**2 — Sürüm işareti paket sürümü değil, içerik revizyonu.** Plan
`sürüm: 1.4.0` yazıyordu. MinVer sürümü her commit'te değişir
(`0.0.0-preview.0.271`), yani harita her commit'te "bayat" görünürdü. İşaret
artık harita gövdesinin SHA-256'sının ilk 8 hanesidir. `APG0401` tüketicinin
dosyasını **paketin taşıdığı haritayla** karşılaştırır; iki dosya da
`AdditionalFiles` olarak gelir, çünkü analyzer diskten okuyamaz (RS1035).

**3 — `APG0102` yeniden tanımlandı.** Plandaki tanım ("`Use<Sağlayıcı>()`
çağrıldı ama sağlayıcı paketi referanslanmamış") **tespit edilemez**: paket
yoksa çağrı zaten `CS1061` ile derlenmez. Yerine gerçek ve tespit edilebilir
kusur kondu: `ModelBinding.Provider` yerleşik bir sağlayıcı adı taşıyor ama
derleme o sağlayıcıyı hiç kaydetmiyor. `AddModelProvider` veya
`UseOpenAICompatible` varsa tanı susar — tüketici sağlayıcısı her adı
karşılayabilir.

**4 — `APG0302` daraltıldı; plan hâli AgentPrism'in kendisini yakaladı.** İlk
uygulama "`AIAgent` sarmalayıcısı" diyordu. `dotnet pack` `AgentPrism.Core`'un
**kendi** `RunRecordingAgent` ve `ReplayMismatchGuard` sınıflarında hata verdi
(Core, üreteç projesini `OutputItemType=Analyzer` ile referanslar, yani kendi
analyzer'ını kendi üzerinde koşturur). Sarmalayıcı kusur değildir —
`IAgentDecorator`'ın işini yapma biçimidir. Kural iki kez daraltıldı: derlemede
hiç `IAgentDecorator` uygulaması **ve** hiç `AddAgent(name, factory)` çağrısı
yoksa bildirilir. Açık Soru 3'ün "riskliyse fazdan çıkarılır" yolu
kullanılmadı; tanı daraltılarak korundu. `APG0301` de aynı sebeple daraltıldı:
döngü bir `catch` içermelidir, yoksa hız sınırlayan bir istemci yanlışlıkla
"elle yeniden deneme" sayılır.

**5 — Tanı seviyesi `Info` değil `Warning`** *(kullanıcı kararı, ölçümle)*.
Açık Soru 1 önce `Info`ya çevrildi (çapraz-assembly yanlış pozitifi build
kırmasın diye). Sonra ölçüldü: **`Info` tanıları `dotnet build` çıktısına
hiçbir ayrıntı seviyesinde düşmüyor** — `-v:normal` ve `-t:Rebuild` ile de yok;
`.editorconfig` ile `warning`e çıkarılınca üçü de görünüyor. Katman 0'ın tek
okuru o çıktı olduğu için `Info` pratikte tanıyı kapatmak demekti. Altısı da
`Warning` oldu ve tek satırlık kaçış eklendi:
`AgentPrismUsageDiagnostics=false` → hedef aileyi `$(NoWarn)`'a ekler.

**6 — `CapabilityCoverageTests` kapsamı genişletildi** *(kullanıcı kararı)*.
Plandaki filtre (`IAgentPrismBuilder` üyeleri + `Use*`/`Map*`)
`AddToolApprovalPolicy` ve `AddWorkflowFunction`'ı dışarıda bırakıyordu;
`Configure` ve `Services` ise kapsanmadığı için taban çizgisi boş doğamazdı.
Kapsam **tüm kayıt giriş noktaları** oldu: alıcısı `IAgentPrismBuilder`,
`IServiceCollection`, `IHostApplicationBuilder`, `IHealthChecksBuilder` veya
`IEndpointRouteBuilder` olan her `Add*`/`Use*`/`Map*` — ölçülen **39 üye**.
Dört üye `capabilities.md`'ye eklendi; taban çizgisi DoD'nin istediği gibi
**boş** doğdu.

**7 — Manuel test dosyası 28 değil 29.** 28 numarayı Faz 64 aldı
(`28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md`).

**8 — `ToolDiagnostics` (APG0001–0007) de `HelpLinkUri` kazandı.**
`DiagnosticIntegrityTests` bütün `APG` ailesini denetliyor; yedi eski tanının
yardım bağlantısı hiç yoktu. Hepsi `#tools-skills-and-context` bölümüne çözülür.

**9 — Plan dışı düzeltme: `TemplateFixture` global paket önbelleğini
temizliyor.** MinVer sürümü commit'ler arasında sabit olduğu için yeniden
paketlenen `.nupkg` NuGet tarafından **hiç açılmıyor**; tüketici testleri günün
ilk paketine karşı koşuyordu. Bu fazda analyzer değişikliği **üç koşum boyunca**
görünmedi ve teşhis bu oldu. `ClearGlobalPackageCache` eklendi. Bu bir test
altyapısı kusurudur ve bu fazdan öncesini de etkiliyordu.

**10 — `buildTransitive` paketlemede `<None Update>` sessizce çalışmıyor.**
SDK'nın varsayılan `None` glob'u yalnız **iç** (TFM'e özgü) derlemelerde
uygulanır; `dotnet pack` paket dosyalarını **dış** çapraz-hedefleme
derlemesinde toplar. Ölçüldü: dışarıda 2, içeride 7 `None` öğesi. `Update`
hiçbir şeyle eşleşmiyor ve dosyalar **uyarısız** pakete girmiyor. Çözüm
`Remove` + `Include`.

## Bu Fazda Verilen Kararlar

| # | Karar |
|---|---|
| **K-505** | Yetenek haritası `capabilities.md`'den üretilir, commit edilir ve `dotnet pack` onu okur; Node zinciri `dotnet build`'e bağlanmaz |
| **K-506** | `AgentPrism.Usage` tanıları `Warning`'dir; `Info` `dotnet build` çıktısına düşmez (ölçüldü). Kaçış tek MSBuild özelliğidir |
| **K-507** | Harita sürüm işareti **içerik revizyonudur**, paket sürümü değil |
| **K-508** | `APG0302` sarmalayıcıyı değil, **dekoratörsüz ve fabrikasız** sarmalayıcıyı bildirir |
| **K-509** | `CapabilityCoverageTests` kapsamı tüm kayıt giriş noktalarıdır (39 üye), yalnız `Use*`/`Map*` değil |

Gerekçeler `docs/KARARLAR.md`'dedir.

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, yalnız DoD + çalışma ağacı) **3 🔴 · 4 🟡 · 4 🟢**
buldu. Üç 🔴'ın üçü de gerçekti.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `DiagnosticIntegrityTests`'in API kapısı 13 tanının 11'inde **hiçbir şey doğrulamıyordu**: desen `'Ad()'` biçimini reddediyor, yani `MapAgentPrism()`/`AddAgentPrism()` hiç denetlenmiyordu | **Düzeltildi.** Eşleşme artık tırnağın **içinde** arıyor; iç içe çağrı (`AddTool(AIFunctionFactory.Create(...))`) iki ad üretir, dosya adı (`AGENTS.md`) elenir. Kanıt: mesajdaki `AddAgentPrism()` yeniden adlandırıldı → test kızardı |
| 2 | 🔴 | Analyzer XML dokümanı hâlâ "`Info` … can never break a build" diyordu; kod `Warning` | **Düzeltildi.** Doküman ölçümü ve kaçış özelliğini yazıyor |
| 3 | 🔴 | Faz dokümanının DoD'si ve doğrulama komutu `≤ 6 KB` / `6144` diyordu; paketlenen dosya 7763 bayt. Ayrıca `28-AGENT-DESTEGI.md`, `dotnet new agentprism`, "bilgi satırı" kalıntıları | **Düzeltildi.** Gövdenin tamamı gerçekleşene göre hizalandı |
| 4 | 🟡 | `Copy` görevi `ContinueOnError` taşımıyordu: salt-okunur depo kökü (yaygın CI mount'u) tüketicinin build'ini `MSB3021` ile kırardı — üstelik şablon özelliği varsayılan açar | **Düzeltildi.** `ContinueOnError="WarnAndContinue"`; kolaylık dosyası build'i kıramaz |
| 5 | 🟡 | Harita↔kaynak sapma kapısı **hiçbir otomatik yolda değildi**: dört kapı Node'u koşmaz, `check:content` CI'da hiç çağrılmıyordu → bayat harita sevk edilebilirdi | **Düzeltildi.** CI `build` işine bağımlılıksız `build-agent-map.mjs --check` adımı eklendi |
| 6 | 🟡 | `APG0302`, dokümante edilmiş fabrika kaçış kapısına (`AddAgent(name, factory)`) yanlış pozitif veriyordu | **Düzeltildi + test.** Fabrika çağrısı olan derlemede tanı susar |
| 7 | 🟡 | `APG0301` herhangi bir döngü + `Task.Delay`'i yeniden deneme sayıyordu; hız sınırlayan istemci yanlış teşhis alırdı | **Düzeltildi + test.** Döngü bir `catch` içermelidir |
| 8 | 🟢 | `build-agent-map.mjs` içinde ölü `anchor()` | **Silindi** (ucuzdu) |
| 9 | 🟢 | Üretilen `Rule:` satırı bazı bölümlerde tablo sonrası paragrafı değil bölümün ilk paragrafını alıyor; `shorten()` kelime ortasından kesiyor | `ADAYLAR.md` · **F-124** |
| 10 | 🟢 | Kimlik bilgisi şekilli literaller birim testinde var, fonksiyonel testte çalışma anında kuruluyor — tutarsızlık | **Gerekçelendi.** İkisinin de yorumu artık literalin analyzer **girdisi** olduğunu yazıyor; depo tarama deseni hiçbirini yakalamıyor |
| 11 | 🟢 | `ClearGlobalPackageCache` makine genelindeki `~/.nuget/packages` altından siliyor | **Gerekçelendi.** Yalnız o koşumda paketlenen sürümün dizinini siler; alternatifi (izole `globalPackagesFolder`) her testte tüm geçişli bağımlılıkları yeniden indirirdi |

Üç 🔴 kapandıktan sonra dört kapı **yeniden koşuldu**.

**Denetçinin temiz bulduğu başlıklar:** 3.3 (test seviyesi — paket sınırını
geçen her davranış gerçek `dotnet build` ile, paketlenmiş meta paket üzerinden
koşuyor) · 3.5 (imza-gövde kayması yok) · 3.6 (plan dışı public API yok) ·
3.7 (repo kuralları, #2 dışında) · 3.8 (ürün yüzeyi). Denetçi §73.4 kapısını
**bağımsız olarak ölçtü**: 39 üyenin 39'u haritada, taban çizgisi gerçekten boş.

## Sonraki Faza Devir Notu

1. 🚨 **`Info` seviyeli analyzer tanısı `dotnet build` çıktısına DÜŞMEZ.**
   Ölçüldü (bu faz): `-v:normal` ve `-t:Rebuild` ile de görünmez; yalnız IDE'de
   ve `.editorconfig` ile seviye yükseltilirse çıkar. Bir agent'ın okumasını
   istediğin her tanı `Warning` olmalıdır. Kaçış mekanizmasını **aynı fazda**
   ver — AgentPrism'de bu `AgentPrismUsageDiagnostics` özelliğidir.
2. 🚨 **`AgentPrism.Core` kendi analyzer'ını KENDİ ÜZERİNDE koşturur**
   (`OutputItemType=Analyzer` ProjectReference'ı). Yeni bir `APG` tanısı
   yazarken Core'un kendi kodunu da tarayacağını hesaba kat; `APG0302`'nin ilk
   hâli `dotnet pack`'i kırdı. Diğer paketler etkilenmez — analyzer referansı
   çok sıçramalı `ProjectReference` zincirinde yayılmaz.
3. 🚨 **Tüketici testleri global NuGet önbelleğine takılır.** MinVer sürümü
   commit'ler arasında sabittir; aynı sürümle yeniden paketlenen `.nupkg` **hiç
   açılmaz**. `TemplateFixture.ClearGlobalPackageCache` bunu artık çözüyor —
   depo dışında elle bir tüketici denerken **sen de** o dizini sil, yoksa
   değişikliğin görünmez.
4. **Harita kaynağı tektir: `docs-site/src/content/docs/capabilities.md`.**
   Yeni bir yetenek eklerken oraya bir satır yaz, sonra
   `node docs-site/scripts/build-agent-map.mjs` koş. Yazmazsan
   `CapabilityCoverageTests` üyeyi adıyla söyleyerek kızarır; koşmazsan CI'ın
   sapma adımı kızarır. Üretilen üç dosya **commit edilir**.
5. **`AgentPrism.Generators` ikinci bir analyzer'ı ucuza alır.** Tanı deseni
   (`UsageDiagnostics` + `AnalyzerReleases.Unshipped.md` + `HelpLinkUri` →
   `capabilities.md` başlığı) ve test altyapısı (`AnalyzerTestHelper`, gerçek
   AgentPrism sembolleriyle) kurulu. Yeni tanı `DiagnosticIntegrityTests`'e
   kendiliğinden dahil olur — mesajdaki her API adı tırnak içinde yazılmalıdır.
6. **Katman 2 (geliştirici MCP sunucusu) hâlâ açık:** `ADAYLAR.md` **F-121**.
   Bu faz Katman 0 ve 1'i kapattı.
