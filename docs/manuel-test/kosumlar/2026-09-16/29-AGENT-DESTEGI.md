# 29 — Tüketici Agent Desteği (`AGD`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../29-AGENT-DESTEGI.md`](../../29-AGENT-DESTEGI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (oturum 14) `Gerçek sonuç` ve
> `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s3` (Faz B, ilk aile — bu şeridin dağılımı: 32 · **29** · 34 · 21 · 11 · 25 · 17 · 20) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s3` · dal `test/kosum-s3` |
| **Kod** | `7e3a4de7` donuk (`ap-s3` HEAD `e5d302e5`, fast-forward — aynı kaynak) |
| **Case sayısı** | 24 (MT-AGD-001..024) |
| **Port** | Bu aile ağırlıklı olarak `dotnet build`/CLI tabanlı; şeridin kendi 5083/`mt_s3`'üne **dokunulmadı**. MT-AGD-019/020 istisna — bkz. aşağıdaki sapma notu |
| **Paket kaynağı** | `dotnet pack Tracon.src.slnf -c Release` **`ap-s3`'ün kendi worktree'sinden** koşuldu (spec'in "Koşmadan önce"i `/Users/farukatasoy/Desktop/projects/Tracon`'u yazar — bu oturumun talimatı başka worktree'ye dokunmayı yasakladığı için `ap-s3`'e uyarlandı). Üretilen paket: `Tracon 0.0.0-preview.0.821`, yerel feed `/Users/farukatasoy/Desktop/projects/ap-s3/artifacts/package/release` |

**Sapmalar (bu oturumda ölçüldü):**

1. **MT-AGD-019/020 portu değiştirildi.** Spec'in `Girilecek veri` blokları
   `localhost:5082`'yi sabit yazıyor — bu, `samples/Tracon.Embedded`'in kendi
   `launchSettings.json`'undaki varsayılan port. Ancak `5082` aynı zamanda
   `ap-s2` şeridinin **kendi** `Tracon.Api` portu (Faz B dağılım tablosu,
   DEVIR.md §5) — o an boştaydı ama koşum sırasında `ap-s2` kendi işine
   başlarsa çakışabilirdi. Kural 1.3 ("yalnız kendi portun") gereği
   `samples/Tracon.Embedded` bu koşumda **`5093`**'te açıldı, `curl` hedefleri
   buna göre uyarlandı. Davranış aynı; yalnız port farklı.
2. **Küresel `dotnet new` şablon kaydı güncellendi.** Önceki bir oturumdan
   (dosya 05, `~/tracon-manuel` akışı) kalan `Tracon.Templates::0.0.0-preview.0.789`
   `dotnet new uninstall` ile kaldırıldı, `ap-s3`'ün kendi `src/Tracon.Templates`'i
   (`0.0.0-preview.0.821`) `dotnet new install` ile kuruldu. Kural 1.3
   "küresel kayıt — yalnız o dosyayı koşan ajan dokunur" bunu bu ailenin
   sorumluluğu yapıyor; başka hiçbir şerit bu turda şablon kullanmıyor
   (yalnız aile 30/YEREL-REFERANS ileride kullanabilir).
3. **`user-secrets` hiç kullanılmadı** — bu ailenin hiçbir case'i sağlayıcı
   kimliği gerektirmiyor (`echo`/`openai` adı yalnız TRC0102'yi tetiklemek
   için literal string olarak geçiyor, gerçek çağrı yok).

**Spec düzeltmeleri (kural 1 istisnası — doküman koddan sapmıştı):**

- **MT-AGD-015/016:** `agentMapBudgetBytes` (11264, Faz 153) ile
  `llmsBudgetBytes` (24576, `llms.txt`'e özel) birbirine karıştırılmıştı;
  MT-AGD-016'nın "10 240 bayt" metni Faz 85'ten kalan bayat bir sabitti.
  `docs-site/scripts/build-agent-map.mjs` okunarak düzeltildi.
- **MT-AGD-016/018:** "Embedding points" bölümü Faz 85'te **beş** giriş
  taşıyordu; ürün o zamandan beri `IRunAuthorizationHandler` (run/session
  authorization) ve `IToolApprovalPresenter` ile büyüdü — bugün **yedi**
  giriş var (haritanın kendi `- Rule:` satırı da artık "seven" diyor).
  Her iki case'in `Beklenen sonuç`'u bu ölçümle güncellendi, eski metin
  silinmedi.

---

## Devir notu

**🎉 AİLE 29 KAPANDI — 22/24 case koşuldu, 2 case `☐ Beklemede`** (MT-AGD-018,
MT-AGD-024 — bkz. aşağıdaki fiziksel/agent-eylem tablosu). Dosya sonucu:
**22 ☑ Geçti · 0 ☑ Kaldı · 2 ☐ Beklemede · 0 ⏭ Atlandı**.

**Bu aile hiçbir yeni ürün kusuru bulmadı** — dosya 07'deki desenin aynısı.
22 koşulan case'in tamamı `Beklenen sonuç` ile birebir eşleşti (üçü, yukarıda
anlatıldığı gibi, önce spec'in kendisini düzeltmeyi gerektirdi — ürün değil
doküman bayattı).

**MT-AGD-018 ve MT-AGD-024 neden `Beklemede`:** İkisi de (👤 işaretli) bu
ailenin en elle işi ağır case'leri — "bir kod agent'ına dokümanı ver, agent'ın
kod agent'ı **olarak** ne ürettiğini gözlemle" deseninde. Bunu gerçekleştirmek
bu koşum oturumunun kendi araç setinin dışında izole, taze-bağlamlı ayrı bir
`claude` CLI çağrısı gerektiriyor (MT-AGD-024 açıkça `claude 2.1.269` sürümünü
ve `--output-format stream-json` bayrağını adlandırıyor). Bütçe ve kapsam
gerekçesiyle bu oturumda denenmedi; ikisi de sonraki 5+ case'i **bloklamıyor**
(aile zaten tamamlandı), bu yüzden sormadan `Beklemede` bırakıldı ve fiziksel
eylem tablosuna yazıldı. Her iki case'in spec metni zaten bir "Gerçek koşum
(2026-09-16 / 2026-08-21, ...)" notu taşıyor — bu, **önceki** bir doğrulamanın
kaydı, bu turun kendi `Gerçek sonuç`'u değil; iki ayrı şey karıştırılmamalı.

**Sonraki oturumun işi:** Aile 29 kapandı, sıradaki aile **34** (`ap-s3`
dağılımının bir sonraki kalemi — 32 · 29 · **34** · 21 · 11 · 25 · 17 · 20).
MT-AGD-018/024 kapanış modunda (Aşama 2) ya da ayrı bir "kod agent'ı
gözlemi" oturumunda ele alınmalı; ikisi de gerçek bir `claude` CLI çağrısı ve
transkript incelemesi istiyor.

---

## MT-AGD-001 — Özellik kapalıyken hiçbir dosya yazılmaz

**Gerçek sonuç**
Taze tüketici projesi (`TraconWriteAgentsFile` yazılmamış), `dotnet build`
başarılı (1 ilgisiz `TRC0102` uyarısı — provider kaydı yapılmadığı için,
beklenen). `test -f AGENTS.md` → çıkış kodu `1`. Dosya oluşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-002 — Özellik açıkken harita git köküne yazılır

**Gerçek sonuç**
`<TraconWriteAgentsFile>true</TraconWriteAgentsFile>` ile `dotnet build`.
`AGENTS.md` **git kökünde** (proje dizininde değil) oluştu. İlk satır:
`<!-- Tracon agent map · revision: 76d02c99 · generated by docs-site/scripts/build-agent-map.mjs -->`.
Boyut: **10684 bayt** ≤ `agentMapBudgetBytes` (11264, Faz 153'ten beri).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-003 — Var olan `AGENTS.md` asla ezilmez

**Gerçek sonuç**
`AGENTS.md`'e elle bölüm eklendi, MD5 alındı
(`ead368dd4fd07dc67b49e085c45cf704`), `dotnet build -t:Rebuild`, MD5 tekrar
alındı — **birebir aynı**. Build dosyaya dokunmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-004 — Git kökü yoksa target çökmez

**Gerçek sonuç**
`.git` içermeyen dizinde `dotnet build` **başarılı**. Çıktıda tam beklenen
mesaj: `Tracon: no repository root was found above '.../src/Consumer'.
AGENTS.md is written next to the project instead.` `AGENTS.md` proje
dizininde oluştu (git kökü aranan yerde değil, çünkü yok). Hata yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-005 — Çok projeli tek build tek dosya bırakır

**Gerçek sonuç**
Aynı git kökünde `First`/`Second` projeleri, `Both.sln`, `dotnet build
Both.sln -c Release` başarılı (ikisi de `TraconWriteAgentsFile=true`).
`find -name AGENTS.md` → **tek** sonuç, git kökünde. Yarış zararsız: dosya
varsa target ikinci projede yazmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-006 — `dotnet new tracon-api` haritayı kendiliğinden getirir

**Gerçek sonuç**
Şablon `ap-s3`'ün kendi `src/Tracon.Templates`'inden kuruldu (`shortName:
tracon-api`, doğrulandı). `dotnet new tracon-api -n Ornek --TraconVersion
0.0.0-preview.0.821` + `dotnet build` → **`0 Warning(s), 0 Error(s)`**.
`test -f AGENTS.md` → çıkış kodu `0`. Şablon özelliği kendisi açtı, hiçbir
`APG`/`TRC` tanısı tetiklenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-007 — `TRC0101`: mapping var, kayıt yok

**Gerçek sonuç**
Yalnız `app.MapTracon()`, `AddTracon()` yok. `dotnet build -t:Rebuild` →
`warning TRC0101` tam beklenen metinle, `AddTracon()` adıyla anılıyor, yardım
linki `.../capabilities/#integration-surfaces`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-008 — `TRC0102`: bağlanan sağlayıcı kayıtlı değil

**Gerçek sonuç**
`ModelBinding.Provider = "anthropic"`, `UseAnthropic()` yok → `warning
TRC0102`, hem `'anthropic'` hem `'UseAnthropic()'` adıyla anılıyor. Sonra
`tracon.AddModelProvider(new DummyModelProvider())` eklenip (test amaçlı
minimal `IModelProvider` — `IModelProvider.Models`/`CreateChatClient`
gerçekten uygulanmalı, spec'in taslak imzası eksikti, tamamlandı) tekrar
derlendi: **sessiz**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-009 — `TRC0201`: tanıma düz `secret` yazıldı

**Gerçek sonuç**
`McpServerDefinition.AuthorizationConfigurationKey = "ghp_aaaa...a"` (40 hane)
→ `warning TRC0201`, hedef tam `'McpServerDefinition.AuthorizationConfigurationKey'`
adıyla. Değer `"Tracon:McpSecrets:GithubToken"` ile değiştirilip tekrar
derlendi: **sessiz**. (Spec'in `AddMcpServer(...)` adımı derlenmiyordu — MAF'ın
kendi `AddMcpServer` extension'ıyla çakışıyor, `Tracon.ITraconBuilder`'da
böyle bir üye yok; tanının tetiklenmesi için kayıt gerekmediği doğrulandı,
`McpServerDefinition` nesnesi bir değişkene atanıp kullanıldı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-010 — `TRC0301` ve `TRC0302`: elle yazılmış yerine koyma

**Gerçek sonuç**
`DelegatingChatClient` türeten `catch`+`Task.Delay` döngülü sınıf +
`DelegatingAIAgent` türeten sarmalayıcı, hiçbir `IAgentDecorator` yok →
`dotnet build -t:Rebuild` hem `TRC0301` hem `TRC0302` verdi (iki mesaj da tam
beklenen metinle). `IAgentDecorator` uygulaması eklenip tekrar derlendi:
**yalnız `TRC0302` kayboldu**, `TRC0301` sürdü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-011 — `TRC0401`: harita bayat

**Gerçek sonuç**
`AGENTS.md` sahte `revision: 00000000` ile yazıldı → `warning TRC0401`, her
iki revizyonu da (`00000000`, `76d02c99`) adıyla anıyor. Dosya silinip tekrar
derlendi: **sessiz**, yeni dosya doğru revizyonla (`76d02c99`) yazıldı. Ek
doğrulama: dosya elle yazılmış `# Ev kurallari` içeriğiyle (işaretsiz)
değiştirilip tekrar derlendi — `TRC0401` **çıkmadı**, beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-012 — Tek özellik tüm aileyi susturur

**Gerçek sonuç**
MT-AGD-010'un tüketicisi (aktif `TRC0301`) `-p:TraconUsageDiagnostics=false`
ile derlendi: **`0 Warning(s)`**. `Tracon.Core.targets`'ın `NoWarn` listesi
kaynağından doğrulandı: yalnız `TRC0101;TRC0102;TRC0201;TRC0301;TRC0302;
TRC0401;TRC0402;TRC0403;TRC0501;TRC0502` kapsanıyor — `Tracon.Tools`
ailesinin kodları (`TRC0001`-`0007`) yapısal olarak bu listede yok, bu
koşumda ayrıca tetiklenip test edilmedi (bu ailenin hiçbir case'i gerçek bir
derleme hatası senaryosu kurmuyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-013 — Kapı: harita kod ile hizasını kaybederse test kızarır

**Gerçek sonuç**
`docs-site/src/content/docs/capabilities.md`'de `` `Tracon.Voice` ve `UseVoice()` ``
→ `` `Tracon.Voice` speech contracts `` yapıldı (adı silindi).
`dotnet test tests/Tracon.Core.UnitTests -c Release --filter
CapabilityCoverageTests` → **kızardı**:
`["+ UseVoice: a registration entry point that the capability map never names"]`,
tam beklenen mesaj formatıyla. Değişiklik `git checkout --` ile geri alındı;
`diff` ile orijinalle **birebir aynı** olduğu doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-014 — Kapı: üretilen harita commit'ten saparsa denetim kızarır

**Gerçek sonuç**
`src/Tracon.Core/buildTransitive/Tracon.AgentMap.md`'ye `- bayat satir`
eklendi (bu, kural 1'in "case kendi prosedürü gereği donuk alana geçici
dokunma" istisnası — case'in kendi 3. adımı geri almayı zaten içeriyor).
`node docs-site/scripts/check-content.mjs` → **çıkış kodu 1**, mesaj dosyayı
adıyla söyleyip üreteci koşmayı öneriyor: `src/Tracon.Core/buildTransitive/
Tracon.AgentMap.md does not match capabilities.md; run: node
docs-site/scripts/build-agent-map.mjs`. `node scripts/build-agent-map.mjs &&
node scripts/check-content.mjs` → **çıkış kodu 0**, temiz. Yeniden üretilen
dosya orijinaliyle **byte-birebir aynı** (`diff` boş) — `git diff --stat
7e3a4de7..HEAD -- src` de boş, kod donması korundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-015 — `llms.txt` siteden erişilebilir

**Gerçek sonuç**
`npm run build` (48s, 1147 sayfa). `head -1 dist/llms.txt` →
`<!-- Tracon agent map · revision: 76d02c99 · generated by
docs-site/scripts/build-agent-map.mjs -->`. `wc -c`: `llms.txt` **21834
bayt** (≤ `llmsBudgetBytes` = 24576 — spec'in "agentMapBudgetBytes" referansı
yanlış sabiti adlıyordu, `Beklenen sonuç` düzeltildi), `llms-full.txt`
**784388 bayt** (~766 KB — spec'in "~350 KB"ı Faz 85'ten kalan bayat bir
tahmindi, düzeltildi). `llms-full.txt` içinde üretilen HTTP API referansı
**yok**: dosya `https://tracon.dev/http-api/` adresine yönlendiren bir
cümle taşıyor, uç noktaları kendi gövdesinde listelemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-016 — Gömme ekseni haritada — Faz 85

**Gerçek sonuç**
`### Embedding points` bölümü bugün **yedi** satır taşıyor — Faz 85'te beş
noktayla yazılan bu case'in metni bayattı: `IRunAuthorizationHandler`
(run/session authorization) ve `IToolApprovalPresenter` (tool-approval
presentation) sonradan eklenmiş, `- Rule:` satırının kendisi de artık
"seven" diyor (`GET /api/diagnostics reports which of the seven are still
built-in`). `Beklenen sonuç` bu ölçümle güncellendi (bkz. MT-AGD-018 için
aynı düzeltme). Dosya toplamı **10684 bayt** ≤ `agentMapBudgetBytes` (11264).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-017 — Gömme sayfası `llms.txt` sayfa indeksinde — Faz 85

**Gerçek sonuç**
`grep -n "Embedding into a host application" dist/llms.txt` → tek satır:
`- [Embedding into a host application](https://tracon.dev/guides/embedding/)
— Bind Tracon's seven embedding points to your own identity, authorization,
eventing, storage, and approval presentation, and read the identity a tool
body sees.` Satır kesilmemiş, `title`/`description` eksiksiz. (Sayfanın
kendi metni de "yedi" diyor — MT-AGD-016'daki ölçümle tutarlı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-018 — 👤 Gömme sayfası bir kod agent'ına sorulmadan yeterli — Faz 85

**Gerçek sonuç**
Koşulmadı. Bu case izole, taze-bağlamlı bir kod agent'ına (Claude Code veya
benzeri) yayımlanmış `guides/embedding.md`'yi verip — `samples/
Tracon.Embedded` **gösterilmeden** — "Tracon'i mevcut bir ASP.NET Core
uygulamasına göm" görevi verip ürettiği kodu okumayı gerektiriyor. Bu, bu
koşum oturumunun kendi çalışma modelinin dışında ayrı, izole bir agent
oturumu istiyor (bu oturumun kendisi zaten bağlamı dolu bir agent'tır ve
"hiç görmeden" bir testin öznesi olamaz). Fiziksel eylem gerektiren
case'lerle aynı gerekçeyle ertelendi (skill §1.4.2) — 5+ case'i bloklamıyor,
aile zaten tamamlandı. Spec'in kendi metni Faz 85'te ("2026-08-21 ölçümünde
hiçbir sayfa bu beşini birlikte anlatmıyordu") ve bu case'in başlığındaki
"beş nokta" referansı artık bayat (bkz. MT-AGD-016 — ürün yedi noktaya
büyüdü); `Beklenen sonuç`'a bu turda bir not eklendi, canlı koşum yapılmadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-019 — Arka plan işi kendi kiracısıyla kapanır — Faz 85

**Gerçek sonuç**
`samples/Tracon.Embedded` **port 5093**'te açıldı (sapma notu #1). `POST
/jobs` (HTTP isteğinde `X-Host-*` başlığı YOK) → `GET /tracon/api/runs` ilk
kayıt: `{"tenantId":"acme","userId":"user-42","status":"Completed"}` — tam
beklenen. `GET /tracon/api/runs/{id}/events` içinde `tool.invoked` payload'ı:
`"tenant=acme run=01a0ac6e-9300-79d7-8104-044681c08982 session=(none)"` —
tam beklenen desen. `current_account` tool'u kimliği yalnız
`TraconRunContext.Current`'tan okuduğunu doğruladı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-020 — Event bridge doldurulunca DÜŞÜRÜR, koşuyu yavaşlatmaz — Faz 85

**Gerçek sonuç**
Aynı 5093 örneğinde 10 iş art arda kuyruğa alındı. `GET /jobs/bridge-state` →
`{"received":16,"dropped":50}` — `dropped` sıfırdan büyük. `GET
/tracon/api/runs` ilk 11 kayıt hepsi `Completed`, `startedAt`/`completedAt`
farkı ölçülen en büyüğü ~3ms (1 sn'nin çok altında). Uygulama logunda
beklenen desen: `Run event bridge is full; dropped a MessageDelta event for
run ...; Total dropped: N.` (birden çok satır, artan sayaçla).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-021 — `TRC0501`/`TRC0502` gerçek tüketici derlemesinde öter — Faz 93

**Gerçek sonuç**
Beş adımın hepsi tam beklenen sonucu verdi: (2) döngü dışı
`TraconRunContext.SetCurrent(scope)` içeren `async IAsyncEnumerable` →
`warning TRC0501`, `BadAsync` adıyla, `MoveNextAsync` öncesi tekrarı
anlatan tam mesaj metniyle. (3) yazım döngü başına taşınınca uyarı
**kayboldu**. (4) `AmbientTenantScope.Begin("t1")` `using` olmadan, ayrı
statik metotta → `warning TRC0502`, `'AmbientTenantScope.Begin'` adıyla. (5)
`-p:TraconUsageDiagnostics=false` ile derlenince (hem TRC0502 kaynağı hem
düzeltilmiş TRC0501 kaynağı derlemede) **`0 Warning(s)`**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-022 — `tracon agent-skill` yazar, ikinci koşum DOKUNMAZ — Faz 178

**Gerçek sonuç**
`Tracon.Cli` `artifacts/publish/Tracon.Cli/release/Tracon.Cli` ikilisinden
koşuldu (repo içi, global tool kurulmadı). (1) `tracon agent-skill` →
`.claude/skills/tracon/SKILL.md` yazıldı, çıktı yolu ve `76d02c99`
revizyonunu adlandırdı. (3-4) Dosyanın sonuna not eklenip hash tazelendi,
ikinci `tracon agent-skill` → dosyaya **dokunulmadı**, çıktı tam beklenen
metin: `... already exists and was left untouched. Pass --force to
overwrite it.` (5) `shasum -a 256 -c` → **`OK`**, not hayatta. (6) `--force`
→ dosya üzerine yazıldı (`was overwritten from capability map revision
76d02c99`), not gitti (`grep -c` → `0`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-023 — Bayat kapı skill'i `TRC0403` ötürür — Faz 178

**Gerçek sonuç**
Beş adımın hepsi tam beklenen sonucu verdi: (1) taze `tracon agent-skill` +
`PackageReference` ile temiz `dotnet build` → `TRC0403` **yok**. (2-3)
`.claude/skills/tracon/SKILL.md`'deki `revision: 76d02c99` →
`revision: deadbeef` → `dotnet build -t:Rebuild` → `warning TRC0403`, her
iki revizyonu da (`deadbeef`, `76d02c99`) adıyla anıp önce tool'u
güncellemeyi söylüyor. (4) damga geri alınıp tekrar derlenince uyarı
**kayboldu**. (5) `TraconUsageDiagnostics=false` ile damga tekrar bozulup
derlenince uyarı **çıkmadı**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-024 — 👤 Üretilen skill Claude Code'da GERÇEKTEN yükleniyor — Faz 178

**Gerçek sonuç**
Koşulmadı. Bu case gerçek `claude` CLI'ını (`--output-format stream-json`)
izole bir proje kökünde, Tracon yüzeyine dokunan bir görevle çalıştırıp
tool-çağrı transkriptinde `Skill(tracon)`'ın **ilk** eylem olduğunu, ve bir
kontrol koşumunda (`.agents/skills/` yolunda, `.claude/` değil) hiç
çağrılmadığını doğrulamayı gerektiriyor — nested bir `claude` çağrısı, bu
koşum oturumunun kapsamı ve araç setinin dışında. MT-AGD-018 ile aynı
gerekçeyle ertelendi (skill §1.4.2) — 5+ case'i bloklamıyor, aile zaten
tamamlandı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Koşulamayan case'ler

| Case | Neden | Kullanıcıdan/sonraki oturumdan istenen |
|---|---|---|
| MT-AGD-018 | 👤 İzole, taze-bağlamlı bir kod agent'ına (Claude Code veya benzeri) `guides/embedding.md`'yi verip **samples/Tracon.Embedded'i göstermeden** ürettiği kodu okumak gerekiyor — bu koşum oturumunun kendi araç setinin dışında ayrı bir agent oturumu istiyor. | Ayrı bir oturumda: agent'a yayımlanmış `guides/embedding.md` metni/URL'i verilip "Tracon'i mevcut bir ASP.NET Core uygulamasına göm" görevi verilsin, üretilen kodun yedi gömme noktasının (bkz. MT-AGD-016 düzeltmesi — artık beş değil yedi) tamamını `AddTracon()`'den önce bulup bulmadığı okunsun. |
| MT-AGD-024 | 👤 Gerçek `claude` CLI'ı (`--output-format stream-json`) izole bir proje kökünde çalıştırıp tool-çağrı transkriptinde `Skill(tracon)`'ı aramak gerekiyor — nested bir agent çağrısı, bu oturumun kapsamı dışında bırakıldı. | Ayrı bir oturumda: `tracon agent-skill` ile yazılmış skill + `Tracon.LocalReference.md` içeren izole bir projede Claude Code'u Tracon'a dokunan bir görevle çalıştırıp ilk tool çağrısının `Skill(tracon)` olduğu doğrulansın; kontrol koşumu olarak aynı dosya `.agents/skills/tracon/SKILL.md` altına konup `Skill` çağrısının **çıkmadığı** teyit edilsin. |
