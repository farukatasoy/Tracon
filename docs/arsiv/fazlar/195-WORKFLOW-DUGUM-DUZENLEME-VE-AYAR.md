# Faz 195 — Workflow Düğüm Düzenleme ve Fonksiyon Düğümü Ayarı

> **Durum:** ✅ Tamamlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-12 (engelleyici kararlar sohbette alındı: F-292 + F-295 tek faz; tipli ayar; son fonksiyon düğümü silinince `agentNames`'e dönüş; konsolda JSON alanı + şema yardımı; aynı fonksiyonun zincirde iki kez kullanımı kapsam dışı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-292**, **F-295**
> **Önkoşul:** [Faz 194](194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) — **işlevsel bağımlılık yok**, ama dört dosyayı paylaşırlar (`workflow-editor.tsx`, `WorkflowContracts.cs`, `WorkflowDefinitionPayload.cs`, `full-replace-editors.test.tsx`). İki yazar aynı dosyaya aynı anda dokunmasın diye uygulama Faz 194 kapandıktan sonra başlar
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Sql.Shared`, `.UI` · üretilen: `Tracon.Client`, `@tracon/client`
> **Yeni paket:** Yok · **Migration:** Yok — workflow tanımı `workflows.definition` JSON blob'unda durur (`0007_workflows.sql`); yeni alan blob'a girer
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable` satırı
> **Tüketici yüzeyi:** site: `concepts/workflows.md` (§ *Function nodes*; `:96-98`'deki "console cannot change it yet" cümlesi), `ui.md` § *Workflows* + ekran görüntüsü, `capabilities.md` (`api/` ve `http-api/` üretilir) · sevk edilen: yeni `AddWorkflowFunction<TInput, TOutput, TSettings>` aşırı yüklemesinin XML `<example>`'ı, `src/Tracon.Workflows/README.md`, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 332436bb:docs/arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md
> ```
>
> Damıtıldı 2026-10-12 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir operatör bugün fonksiyon düğümlü bir workflow'u konsoldan kuramaz ve değiştiremez; zincir salt okunurdur ve `PUT /api/workflows/{name}` ister. Aynı fonksiyonu iki workflow'da farklı ayarla (ses, LUFS hedefi, TTS sağlayıcısı) kullanmak da kodda ayrı kayıt ister.

## Bitiş Ölçütleri (DoD)

- [x] Konsoldan agent + fonksiyon zinciri kurulur, sıralanır, silinir, kaydedilir; run koşar (test 15) — ✅ `workflow-editor.test.tsx` · E2E `A_chain_built_in_the_console_with_function_settings_is_saved_and_runs`; örnek uygulamada konsoldan kuruldu, çıktı `…[spoken by alloy at -12 LUFS]`
- [x] Ayarlı fonksiyon düğümü tipli ayarı alır (test 1) — ✅ `WorkflowFunctionSettingsTests`; örnek uygulamada `[spoken by alloy at -14 LUFS]`
- [x] Bilinmeyen alan, eksik `required`, nesne olmayan ayar ve agent düğümünde ayar `400` döner (test 2-3) — ✅ `WorkflowFunctionSettingsValidationTests` (16); örnek uygulamada altı `400`
- [x] Değişen `TSettings` run'ı ilk düğümden önce adlandırılmış hatayla düşürür (test 4) — ✅ `WorkflowFunctionSettingsDriftTests` (4)
- [x] Son fonksiyon silinince `agentNames`, ilk fonksiyon eklenince `nodes` (test 13) — ✅ `workflow-editor.test.tsx`; örnek uygulamada `agentNames: ["support"]`, `version: 2`
- [x] `GET /api/workflows/functions` `settingsSchema` taşır; uç açıklaması koda uyar — ✅ `WorkflowFunctionCatalogSchemaTests`
- [x] Ayarsız + ayarlı düğüm aynı listede `GET /api/workflows`'u çökertmez (test 5) — ✅ `WorkflowFunctionSettingsTests`
- [x] Dört doğrulama kapısı sıfır uyarı verir — ✅ `python3 scripts/kapi.py kapanis --taban 98a2295b`: 11/11 adım yeşil (bkz. *Kapanış kapıları*)
- [x] `samples/Tracon.Api` ile gerçek `run` (konsoldan kurulan ayarlı zincir) — ✅ bkz. *Örnek Uygulama Koşumu*
- [x] `secret` taraması boş; site ve XML "anahtar adı yazılır, anahtar yazılmaz" kuralını yazar — ✅ `kapi.py tarama`; site, XML, `<example>`
- [x] Manuel kabul case'leri eklendi, otomatikleştirilebilenler koşuldu — ✅ `MT-WF-132…137` (+`120`); `132`–`135` ve `137` örnek uygulamada koşuldu
- [x] `faz-denetim` koşuldu; 🔴 kalmadı — ✅ 1 gerçek (düzeltildi), 1 gürültü (gerekçeli ret); iki triyaj kullanıcı kararı
- [x] `docs-site/` güncellendi; `npm run check` temiz — ✅ 1050 sayfa; 173.496 iç referans, kırık yok; SEO 0 hata; ağırlık tavanı altında
- [x] `en.ts`/`tr.ts` eksiksiz; bundle ölçüldü — ✅ 199,9 KB gzip (bütçe 250; Faz 194 sonrası 197,7, payı +2,2)
- [x] `CHANGELOG.md` yeni aşırı yüklemeleri ve arayüz üyesini adıyla anar — ✅ iki aşırı yükleme, `ValidateSettings`, `MaxNodeSettingsLength`, iki sözleşme vakası

### Doğrulama komutları

```bash
curl -s http://localhost:5081/tracon/api/workflows/functions          # settingsSchema
curl -s -X PUT http://localhost:5081/tracon/api/workflows/narrated -H 'Content-Type: application/json' \
  -d '{"kind":"Sequential","nodes":[{"name":"support","kind":"Agent"},{"name":"speak","kind":"Function","settings":{"voice":"alloy","loudnessLufs":-14}}]}'
curl -sN -X POST http://localhost:5081/tracon/api/workflows/narrated/run -H 'Content-Type: application/json' -d '{"message":"Hello"}'
```

Adlar örnek uygulamadaki gerçek adlardır; koşum Echo kipinde, `5199` portunda yapıldı.

---

## Plandan Sapmalar

Numara `#N` ile verilir; sonraki fazlar bu numarayla atıf yapabilir.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | Opsiyonel parametreli `AddWorkflowFunction<…,TSettings>(…, description, retryPolicy)` | İki aşırı yükleme: `(name, factory)` ve `(name, factory, Action<WorkflowFunctionOptions>)`; yeni public `WorkflowFunctionOptions`. İki parametreli aşırı yükleme değişmedi | RS0026: genel tip aritesi farkı analyzer'ı yatıştırmaz; opsiyonel parametre `1.0.0`'da donar (K-902, K-422, K-867). Planın tek revizyonu |
| 2 | Örnekler `string` girdili | `List<ChatMessage>` girdili | F-341 (K-908, başka oturum) aynı anda kapandı: düğüm, önceki düğümün geçirdiği tipi almalı; `string` girdili fonksiyon agent'tan sonra `400` verir. Faz kodu değişmedi |
| 3 | Hata yalnız `JsonException` | Setter veya kurucu fırlatırsa `400` (`the settings type rejected a value (<Tür>)`), `NotSupportedException` da ret; istisna mesajı yanıta girmez, uyarı log'una gider | Aralık denetimi setter'da doğal; `500` operatörün girdisini sunucu hatası yapardı; mesaj değer taşıyabilir |
| 4 | Okunamayan ayar tipi düşünülmedi | `AddWorkflowFunction` çağrısında `EnsureReadable`: arayüz veya soyut sınıf `TraconException` ile düşer | Aksi hâlde her kayıt operatörün suçu gibi görünürdü |
| 5 | Şema üretilemezse tanımsız | `{"type":"object"}`, asla `null`; uyarı log'u | `null` "ayar almaz" demektir; ayar alan fonksiyon öyle görünmemeli |
| 6 | `WorkflowDefinitionPayload` "ölçülür" | Ölçüldü, kod değişmedi (yalnız `remarks`): düğüm alanı kendiliğinden akar | İki yeni `WorkflowDefinitionStoreContract` case'i dört depoda yeşil. Sevk edilen sözleşme genişledi: yayın provası zorunlu |
| 7 | Açık Soru 1 · 2 · 3 | 1-A `ValidateSettings` (varsayılan uygulamasız) · 2-B `MaxNodeSettingsLength` 16 KiB, yalnız kayıtta, yapılandırmadan bağlanır (canlıda ölçüldü) · 3-A yapısal değişiklikte her zaman bir satır | K-900, K-901 |
| 8 | Ölçülmeli: agent ekleme/çıkarma aynı sonucu veriyor mu | Ölçüldü (`WorkflowStructuralChangeResumeTests`, 9 vaka): agent ekleme, agent veya düğüm silme, sıra, desen, biçim değişimi, düğüm ekleme eski checkpoint'i reddettirir; açıklama ve ayar değişikliği reddettirmez. **`Concurrent`'te sıra değişikliği de reddettirir** (ekranın "order does not affect this pattern" ipucu sonuçtan söz eder, checkpoint grafla ilgilidir) | Uyarı her desende doğrudur (K-901) |
| 9 | Ölçülmeli: `net8.0`'da `RespectNullableAnnotations` | Var: `System.Text.Json` 10.0.0 geçişli paketi `net8.0/9.0/10.0`'da dört özelliği verir | Faz 193'e bağımlılık yok (K-900) |
| 10 | Tek editör dosyası | Üç dosya: `workflow-editor.tsx` · `workflow-editor/steps.tsx` · `lib/workflow-steps.ts` (saf mantık, 24 test) | Tek `Step[]` iki sunucu biçimini okur, kanonik olanı yazar |
| 11 | İstemci üretimi "mevcut kapılar" | `nswag-prepare-document.py` üçüncü dönüşüm: nullable `$ref` özelliğinde açıklamayı özelliğe taşır; `client-description-baseline` 396 → 357 | `settingsSchema` ve 41 özellik C# istemciye belgesiz gidiyordu; taban yalnız küçüldü |
| 12 | — | `ExamplePrelude`'a `TtsSettings`; `KARARLAR-INDEKS` bütçesi: başlık kısaltmak yetmedi, `ARSIV_ESIK` 88 → 84 (sonra başka oturum 60) | Yeni `<example>` derlenir; eşik yalnız sıkılaştı |
| 13 | "İptal noktası eklenmez, grep ile ölçülür" | Ölçüldü: eklenen `await`/`async` yalnız sözleşme case'lerinde; üretim kodu yeni `await` veya `CancellationToken` eklemedi | Deserialize senkron, derlemede |
| 14 | §195.5 yalnız editör | **Liste ekranı da değişti:** `workflows.tsx` iki biçimi okur, sütun **Participants**; `workflows{,-light}.png` yeniden üretildi | Konsol fonksiyonlu zinciri `nodes` yazar; liste `agentNames`'i okuyunca boş görünürdü (denetim 🟡 3) |
| 15 | "Yalnız JSON değilse Save kapanır" | **Save ve Retry** kapanır; `chainBody` geçersiz metinde `null` yazmaz, fırlatır | Retry korumayı aşıp ayarı siliyordu (denetim 🔴 1) |
| 16 | Ayar değeri "JSON" | **Sınır:** konsol JSON sayısını `double` okur; 2^53 − 1'den geniş tamsayı açılıp kaydedilince yuvarlanır. Çözülmedi; XML ve site "`string` bildirin" der | Kayıpsız taşıma özel JSON ayrıştırıcısı ister (denetim 🟡 4, F-414) |
| 17 | "Varsayılan ölçümle seçilir" | 16 KiB ölçüme değil tavan mantığına dayanır: en büyük gerçekçi örnek ayar 74 bayt (~220 kat) | Koruma tavanı, yapılandırılabilir (denetim 🟢 9) |

## Bu Fazda Verilen Kararlar

Üç karar `docs/KARARLAR.md`'ye girdi: **K-900** *(public-api, güvenlik)* ayar tipli ve katı okumayla doğrulanır, kayıtta ve her derlemede aynı okuma, ayar `secret` değildir (kullanıcı kararı) · **K-901** *(public-api)* konsol tek kanonik biçim yazar, sunucu ikisini de kabul eder, yapısal değişiklik bekleyen run'ı bozar ve editör söyler, aynı fonksiyon zincirde bir kez (kullanıcı kararı) · **K-902** *(public-api)* ayarlı kayıt `Action<WorkflowFunctionOptions>` alır; `IWorkflowFunctionCatalog.ValidateSettings`.

Yerel tercihler: ayar `JsonElement?`, `init` erişimcisi `Undefined`/`Null`'ı `null` yapar (atanmamış `JsonElement` tüm liste ucunu çökertirdi) · geçersiz JSON dışında doğrulama sunucudadır, editör kopya taşımaz · `describeSchema` bir şema yorumlayıcısı değil, listeleyicidir · her düğümde `settings` yazılır, ayarsız düğüm açık `null` taşır (K-725).

## Tüketici Yüzeyi Envanteri

| Kova | Yüzey | Durum |
|---|---|---|
| `docs-site/` (elle) | `concepts/workflows.md` (§ *Give a function settings*, "Wide numbers" dahil; § *Retry*) · `ui.md` (editör; `workflow-editor{,-light}.png`, `workflows{,-light}.png`) · `reference/configuration.md` · `capabilities.md` · `packages.md` (`Tracon.Workflows` satırı; `--site-denetle` `paket-readme` kuralı) | Yazıldı |
| `docs-site/` (üretilir) | `api/*` · `http-api/workflows.md` · `llms*.txt` (commit edilir) | Yeniden üretildi |
| Sevk edilen metin | `///` (`Settings`, `SettingsType/SettingsSchema`, `ValidateSettings`, iki aşırı yükleme ve `<example>`'ları, `WorkflowFunctionOptions`, `MaxNodeSettingsLength`, `Nodes` remarks, iki uç açıklaması) · `src/Tracon.Workflows/README.md` · `CHANGELOG.md` | İç referans yok; `ExampleCompilationTests` 81/81 |
| Agent haritası, yerel referans | `capabilities.md` iki satır → `Tracon.AgentMap.md`, `llms*.txt` | `--check` yeşil |

Site **yayınlanmadı** (`./scripts/site-deploy.sh`, Adım 10 dışarıya dönüktür, istenmedi).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, Echo sağlayıcıyla (anahtarsız), `ASPNETCORE_ENVIRONMENT=Production` (bellek içi) ve `127.0.0.1:5199` üzerinde, `9d87024b` ile birleşmiş kodla koştu. Çıktılar gerçektir.

| Komut | Çıktı |
|---|---|
| `GET /tracon/api/workflows/functions` | `speak` → `settingsSchema` `{"type":"object","properties":{"voice":{"type":"string"},"loudnessLufs":{"type":"number"},"apiKeyConfigurationKey":{"type":["string","null"]}},"required":["voice"],"additionalProperties":false}`; `narrate`, `word-count` → `null` |
| `PUT narrated` (`support` → `speak`, `{"voice":"alloy","loudnessLufs":-14}`), sonra `POST …/run` `{"message":"Hello"}` | `HTTP 200`, `nodes[1].settings` yazılan nesne; `WorkflowOutput` `Echo: Hello \n\n[spoken by alloy at -14 LUFS]` |
| `PUT` ayar `{"voice":"nova"}` → yeni run | `[spoken by nova at -16 LUFS]`: sonraki run yeni ayarı kullandı, `loudnessLufs` varsayılana döndü |
| Altı `PUT`, hepsi `HTTP 400` | `'voise'` → `Function node 'speak' has invalid settings: The JSON property 'voise' could not be mapped … Path: $.voise` · `speak` ayarsız → `… missing required properties including: 'voice'.` · `loudnessLufs: "loud"` → `… could not be converted to System.Double. Path: $.loudnessLufs` · agent düğümünde ayar → `… is an agent node but carries 'settings'.` · `word-count`'ta ayar → `… carries settings, but the function declares none.` · `[1,2]` → `… 'settings' that are a JSON array. Settings must be a JSON object` |
| **Konsol** (gömülü tarayıcı): `console-narrated`, `support` + `speak`, ayar `{"voise":"alloy"}` → Save | Hata bandı sunucunun `400` mesajını gösterir; kayıt oluşmaz |
| Aynı ekran, ayar `{"voice":"alloy","loudnessLufs":-12}` → Save → Run `Hello from the console` | `nodes` (`agentNames: []`, `version: 1`); graf `support → speak`; çıktı `Echo: Hello from the console [spoken by alloy at -12 LUFS]` |
| Aynı workflow → Edit → `speak`'i sil → Save | İki not görünür (düz agent listesi olarak kaydedilir; bekleyen run resume edilemez); desen seçici açılır; `GET` → `agentNames: ["support"]`, `nodes: []`, `version: 2` |

Varsayılan dışı yapılandırma: `Tracon__Workflows__MaxNodeSettingsLength=64` → küçük ayar `200`; 200 baytlık `voice` → `400` `… larger than 64 bytes of JSON … TraconWorkflowOptions.MaxNodeSettingsLength` (yapılandırmadan bağlanır).

Ölçülmeyen: PostgreSQL'li sample (depo sözleşmesi dört depoda test edildi); eski checkpoint'ten resume birleşik kodla sample'da yeniden koşulmadı (fazın önceki sample koşumunda ölçüldü; `WorkflowFunctionSettingsResumeTests` ve `WorkflowStructuralChangeResumeTests` yeşil).

### Kapanış kapıları ve yayın provası

`python3 scripts/kapi.py kapanis --taban 98a2295b` (denetim düzeltmelerinden **sonra**, `a7b50750` üzerinde, ilk koşumda): **11/11 adım yeşil**, çıkış 0.

| Adım | Süre |
|---|---|
| `kapi.py tarama` · `dokuman-bakim.py --denetle` · script testleri (632) · agent map `--check` · `denetim-paketi.py` | 6,83 · 1,82 · 7,95 · 0,20 · 0,48 sn |
| `dotnet build Tracon.slnx -c Release` | 97,50 sn · 0 uyarı, 0 hata |
| `dotnet test` (`-maxcpucount:2`) | 562,78 sn · 19.947 test (tüm TFM bacakları), 0 başarısız, 13 atlanmış |
| `dotnet pack` · `dotnet format --verify-no-changes` · `performans` · `docs-site` `npm run check` | 7,18 · 124,18 · 96,67 · 39,67 sn |

Başlıca test sayıları (net10.0): Core.UnitTests 3552 · AspNetCore.FunctionalTests 1572 · PostgreSql.IntegrationTests 987 (+1 atlanmış, bu fazla ilgisiz) · Sqlite 931 · SqlServer 913 · Ui.E2ETests 91 · Workflows.UnitTests 237 · Generators.UnitTests 304 · Package.Tests 80. `dotnet pack` 19 `nupkg` ve 18 `snupkg` üretti. `performans` yeşil: `CompiledAgentCacheBenchmarks.CacheHit` tahsisi 24 B → 0 B düştü (iyileşme, bu fazın yolu değil); taban `kapi.py performans --guncelle` ile güncellenir, **güncellenmedi**.

**Yayın provası** (sevk edilen `WorkflowDefinitionStoreContract` iki case kazandı): `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` → çıkış 0. Kırıcı liste 2 tip, 0 TFM düşüşü, 2 paket ve hepsi `[Unreleased]` notunda; 20 paket; `npm publish --dry-run`; altı exact-version packed sample, Native AOT smoke ve net8.0 tüketici smoke geçti. Hiçbir sample `IWorkflowDefinitionStore` uygulamıyor. İlk deneme iki kez düştü ve ikisi de **kod kusuru değildi**: biri provanın ortasında izlenen bir dosyayı düzenlediğim için (`TRACON0004`, kirli ağaç), diğeri Faz 194'ün `preview.4` çıktısı `artifacts/package/yayin/`'de durduğu için (araç eski adayı korur; gitignore'lu çıktı kaldırılıp prova yeniden koşuldu).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 — aşırı yükleme imzası (RS0026, K-902); öbür sapmalar planın "ölçülmeli" kalemlerinin sonucudur (#7–#9) |
| Düzeltme turu sayısı | 12 + kapanış kapıları — çözümleyici (3: RS0016 · CA2263/MA0009/MA0023 · test tiplerinde IDE0032/MA0015) · mimari ratchet'ler · taban çizgileri · örnek derleme · sevk edilen `///`'de iç referans · OpenAPI ve üç istemci · agent map · `KARARLAR-INDEKS` bütçesi · kültür duyarlı test dizesi · bağımsız denetim sonrası · kapanış kapıları: 0 (ilk koşumda yeşil) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 1 / 0 — triyajı kullanıcı seçti |
| Fazın ürettiği regresyon | 2 — `🔴1` (Retry, editörün korumasını aşıp ayarı siliyordu) ve `🟡3` (liste konsolun biçimini göstermiyordu); ikisini de denetim yakaladı, kapanıştan önce düzeltildi |
| Faz kapandıktan sonra bulunan kusur | 0 — kapanış anı |

## Denetim Bulguları

Denetçi `faz-denetcisi` (taze bağlam, salt-okunur), aralık `98a2295b..HEAD`; `9d87024b` yalnız F-341 etkileşimi için okundu. Denetçi derleme, test ve kapı koşturmadı. Triyaj Adım 5.1: **🔴'ların sonucunu kullanıcı seçti**; her bulgu kodda elle doğrulandı.

| # | Sv. | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | Hata bandındaki **Retry** Save'in korumasını aşar; geçersiz JSON `settings: null` olur, PUT tam değiştirdiği için kayıtlı ayar silinir (§195.5, K-725) | **Gerçek** (kullanıcı) | **Düzeltildi** (`e68f3de8`): Retry, Save gibi tutulur; `chainBody` geçersiz metinde fırlatır. Testler: editörde "holds Retry back, like Save…" (400 → bozuk metin → Retry yok, PUT yok → JSON → `null` değil), `workflow-steps.test.ts`. Mutasyon denetimi: koruma kalkınca ikisi de düşer |
| 2 | 🔴 | Taban çizgisi büyüdü: `Tracon.Workflows` 6 → 7 (`WorkflowFunctionOptions`) | **Gürültü — gerekçeli ret** (kullanıcı) | Opsiyonel parametreli aşırı yükleme RS0026 ile derlenmedi; opsiyonel parametre `1.0.0`'da donar; `AddTool` emsali; karar K-902; Faz 194 de 5 → 6 yaptı. Taban 7'de kalır |
| 3 | 🟡 | Liste `nodes` biçimini okumaz: konsolun yazdığı fonksiyonlu zincir `—` görünür | Gerçek | **Düzeltildi** (`e68f3de8`): `participantsOf`, sütun **Participants**, `workflows.test.tsx`, ekran görüntüleri |
| 4 | 🟡 | Konsol ayarı JS `Number`'ına çevirir; 2^53 − 1'i aşan tamsayı yuvarlanır | Gerçek (sınır) | **Gerekçelendi** (`52e1e11b`): XML ve site "`string` bildirin" der; kayıpsız taşıma **devredildi**, F-414 |
| 5 | 🟡 | `MaxNodeSettingsLength < 1` doğrulaması testsiz | Gerçek | **Düzeltildi** (`e04f2428`): `WorkflowNodeSettingsLimitValidationTests` (4) |
| 6 | 🟡 | DoD 9: konsoldan kurulan zincir sample'da koşulmadı | Gerçek | **Düzeltildi:** konsolda kuruldu, koşuldu, `agentNames`'e dönüldü (*Örnek Uygulama Koşumu*; `MT-WF-132`, `134`) |
| 7 | 🟢 | Tüketici `JsonConverter`'ının `JsonException` mesajı `400`'e ve `runs.error`'a girer | Gerçek, dar | **Devredildi:** F-415 |
| 8 | 🟢 | Uyarı ölçülenden geniş (sıra, desen, silme ölçülmemişti) | Gerçek | **Düzeltildi** (`e04f2428`): beş ölçüm; uyarı her desende doğrudur (#8) |
| 9 | 🟢 | 16 KiB'nin dayanağı yok; `ValidateSettings` XML'i boyut reddini anmıyor | Gerçek | **Düzeltildi** (`52e1e11b`); dayanak tavan mantığı (#17) |
| 10 | 🟢 | CHANGELOG iki yeni sözleşme vakasını anmıyor | Gerçek | **Düzeltildi** (`52e1e11b`) |
| 11 | 🟢 | `configure` aşırı yüklemesinde `<example>` yok | Gerçek | **Düzeltildi** (`52e1e11b`) |
| 12 | 🟢 | Retry testi "SAME object" der, `record` eşitliğiyle sayar | Gerçek | **Düzeltildi** (`e04f2428`): `ReferenceEquals` |
| 13 | 🟢 | `e59ee892` ilgisiz iki arşiv raporu taşıyor | Hijyen | **Dokunulmadı:** kullanıcının 2026-10-12 kararlarını taşırlar |

Denetçi "bilinen sapmaları" yargıladı (K-902 biçimi, Açık Sorular, `{"type":"object"}` yedeği, setter → `400`, tabanlar, iptal iddiası): **sağlam**. Hata Modları 1–19'un karşılığı vardır.

**Oturum olayı (bulgu değil):** bir kez `dotnet user-secrets list` yerel sağlayıcı anahtarlarını oturum kaydına ham yazdı. Anahtarlar repoda yok; döndürme kararı bakımcıdadır. Tuzak `hafiza/elle-kosum-ortami.md`'de.

## Faz Dışı Bulunan ve Kapatılan Kusur

`GetRunParameters` `<example>`'ı (`TraconWorkflowContextExtensions.cs`, Faz 194) `string` girdili fonksiyon gösteriyordu; F-341'den (K-908) sonra böyle zincir kayıtta `400` verir. `List<ChatMessage>` yapıldı (`c8201342`); `ExampleCompilationTests` 81/81. El yazısı kaynaklarda başka `string` girdili örnek yok (`grep -rn "AddWorkflowFunction<string" src samples docs-site/src/content`, `api/` hariç).

## Sonraki Faza Devir Notu

**Sıradaki faz: [Faz 196](196-UZUN-ADIM-SURE-VE-IPTAL.md).** Önkoşulu karşılandı. Faz 197–201 de bu fazın kayıt yüzeyine ve editörüne dayanır (kendi `Önkoşul` satırlarında).

### Devralınan sözleşmeler

- **`WorkflowNodeReference.Settings`** `JsonElement?`: `null` = ayar yok; yalnız `Function` düğümü, yalnız nesne; `Undefined`/`Null` → `null`. Düğüme alan ekleyen faz (196 süre, 200 agent sürümü): `WorkflowDefinitionPayload` düğüm kayıtlarını kendisi taşır, alan kendiliğinden akar; yine de `WorkflowDefinitionStoreContract`'a gidiş-dönüş case'i yazılır (K-580) ve derin `Complete<WorkflowSaveRequest>` kapısı alanı her fixture'da ister.
- **`AddWorkflowFunction<…,TSettings>`** iki aşırı yüklemedir; `description` ve `retryPolicy` `WorkflowFunctionOptions`'tadır. Yeni kayıt zamanı ayarı (süre, maliyet) **seçenek nesnesine** girer; opsiyonel parametre eklenmez (K-902).
- **Ayar okuması TEK yerdedir:** `WorkflowFunctionSettings`; kayıt (`ValidateSettings`), derleme (`CreateExecutor`) ve şema aynı seçeneği kullanır. İkinci doğrulayıcı yazma: kontrol okumadır (K-900). `IWorkflowFunctionCatalog.ValidateSettings` varsayılan uygulamasızdır.
- **Konsol zinciri** tek `Step[]`'dir (`lib/workflow-steps.ts`): `stepsOf` iki biçimi okur, `chainBody` kanonik olanı yazar (ikisi de her zaman, diğeri boş). `shapeOf` bekleyen run'ı bozan şeyi tanımlar: desen + adım türü ve adı + sıra.
- **Bundle tabanı: 199,9 KB gzip** (bütçe 250; Faz 199'un "Faz 195 yeniden ölçer" satırı bu sayıdır).

### 🚨 Bilinen tuzaklar

- **Ayar derleme başına okunur;** değişiklik sonraki run'da geçerlidir, resume güncel ayarı okur. Ayar executor kimliğinin parçası değildir: ayar eski checkpoint'i bozmaz; desen, adım, sıra, biçim bozar. Düğüme alan ekleyen faz, alanın kimliğe girip girmediğini **ölçer**.
- **Retry sarmalayıcısı ayara bağlanmış işleyicinin etrafında, derleme başına kurulur** (`withRetry`); kayıtta kurulursa her deneme ayarsız kalır. Faz 196/199'un sarmalayıcıları aynı noktaya girer.
- **F-341 (K-908):** ayarlı fonksiyon da önceki düğümün geçirdiği tipi alır; agent'tan sonra `List<ChatMessage>`.
- **Settings tipi tüketicinin kodudur:** setter ve kurucu fırlatabilir; `Deserialize`'ın ikinci `catch`'i bu yüzden `JsonException`'dan geniştir, daraltma. `settingsSchema` `null` ≠ boş: `null` "ayar almaz"dır, üretilemezse `{"type":"object"}`.
- **Konsol JSON sayısını `double` okur** (F-414). **Retry Save gibi tutulur:** yeni bir kaydet yolu aynı korumayı paylaşmalı.
- Ayrıntılar `hafiza/`'da: `installApiMock` ilk eşleşen rota (`frontend-test-altyapisi.md`), nullable `$ref` açıklaması ve istemci tabanı (`nswag-istemci-uretimi.md`), `ExamplePrelude` taslağı (`analyzer-tanilari.md`), `user-secrets list` ham değer basar (`elle-kosum-ortami.md`).

### Doküman bütçeleri (dar)

`docs/manuel-test/*.md` %1 boş (14 KB) · `docs/**.md` %1 · `ADAYLAR.md` %9 · `hafiza/workflows.md` %8 · `hafiza/nswag-istemci-uretimi.md` %6 · `KARARLAR.md` %12. Faz 196 bunlara ekleme yapmadan **önce** içerik taşır.

### Açık işler

- `F-412…F-415` ([ADAYLAR.md](../../ADAYLAR.md)): şemadan ayar formu, uyarı yalnız bekleyen run varken, 2^53'ten geniş tamsayı, `JsonConverter` mesajı.
- Site **yayınlanmadı** (Adım 10 dışarıya dönüktür, istenmedi).
- Faz 193 takvime bağlıdır (2026-11-10'dan önce başlamaz); bu fazı etkilemez: `System.Text.Json` 10.0.0 `net8.0`'a da akar (K-900).
