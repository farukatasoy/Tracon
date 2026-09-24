# Faz 186 — Script İzninin İçeriğe Bağlanması ve Tehdit Modeli Düzeltmesi

> **Durum:** ✅ Tamamlandı (2026-09-24)
> **Plan onayı:** Bakımcı, 2026-09-23 (engelleyici kararlar sohbette alındı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-269**
> **Önkoşul:** [Faz 185](185-KARDES-PAKET-SURUM-SABITLEME.md) — sonraki sürüm (K-852…K-854 güvenlik sürümü) 185'i bekler; 186 ondan sonra başlar (kullanıcı kararı). K-853 `bb9953e3`'tedir. **Anahtar basma açığı kapandı** (kusur-giderme, 2026-09-23; K-853 genişletildi, yeni K açılmadı); bu faz yalnız sonucunu doğrular (§ 186.0).
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.AspNetCore`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Testing.Contracts.Xunit`, `.UI`; üretilen `Tracon.Client`, `@tracon/client`
> **Yeni paket:** Yok · **Migration:** gerekli — numara uygulama anında alınır (üç sağlayıcı, K-178)
> **Public API:** büyüyor; bir imza değişiyor. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, 17 satır, yalnız `#nullable enable` (K-603). Davranış da kırıcıdır (Karar 3).
> **Tüketici yüzeyi:** site: `concepts/tools.md`, `reference/{threat-model,security-policy,compatibility}.md`, `getting-started/security.md`, `guides/production.md`, `ui.md`, `capabilities.md`; üretilen `api/`, `http-api/`
> · sevk edilen: `SECURITY.md` kapsam satırı, `CHANGELOG.md` (`Security` + `Changed`), XML `<example>` (`SkillScriptGrantRequest`), `capabilities.md:98`; paket `README.md`: yok (üç README'de `grep -in "skill script\|UseSkillScripts"` boş)
> **Manuel test alanı:** [`docs/manuel-test/14-SKILL-VE-SCRIPT.md`](../../manuel-test/14-SKILL-VE-SCRIPT.md) — case'ler oraya eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 0aa5c2c2:docs/arsiv/fazlar/186-SCRIPT-IZNI-ICERIK-PINI.md
> ```
>
> Damıtıldı 2026-09-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Script grant'ı bugün bir **ada** verilir, bir **koda** değil. `SecurityAdmin` v1'i onaylar. `AgentsAdmin` anahtarı aynı ada v2 yazar. v2 eski grant ile sunucuda çalışır. `SecurityAdmin` "yetkiyi yalnız ben genişletirim" vaadini verir (`ApiKeyScope.cs:80-85`); bu vaat kırıktır. Tehdit modeli "Script sandboxing" sınırını ilan eder.

## Bitiş Ölçütleri (DoD)

- [x] v2 `PUT`'undan sonra v1'e pinli grant ile çağrı reddedilir; `script.denied` `after` "content changed since the grant" taşır — `SkillScriptContentPinTests.A_grant_pins_the_script_content_it_was_given_for`; örnek uygulamada gerçek modelle (MT-SKILL-065)
- [x] Karar 2: `Skill_wide_grant_refuses_every_script_after_a_script_is_added` yeşil (+ silme varyantı)
- [x] Stored skill için `POST`: hash yok → `400`, bayat → `409`, `scriptName` yok → `404`; üçünde liste değişmez, `script.grant` yok — `SkillScriptGrantTests` (7 durumlu teori, `404`) · `A_grant_for_content_that_changed_after_it_was_read_is_refused_and_writes_nothing`
- [x] Çok kiracılı: `SecurityAdmin` + `AgentsRead` → `403`; `+ PlatformAdmin` → `201`; statik token → `201`; claims policy'si; tek kiracılı → `201` — `SkillScriptContentPinTests`
- [x] Devre dışı stored skill'e grant: hash yok → `400`; çok kiracılıda platform yetkisiz → `403` (#27) — `SkillScriptGrantTests`
- [x] Kod/stored ad çakışması Açık Soru 1 A'yı verir (#26): kod skill'i pinlenir ve kazanır; gölgelenen kopyanın hash'i `409` — ayrıca kod adı artık yazılamaz (K-863)
- [x] Hash'siz grant stored script'i reddeder, file script'i çalıştırır — `SandboxedSkillScriptRunnerTests`; örnek uygulamada faz öncesi grant (MT-SKILL-069)
- [x] Yeni sözleşme testleri dört koşumda yeşil — bellek içi 23, PostgreSQL/SqlServer/Sqlite 20'şer
- [x] Üç migration uygulanır ve `07c787d9`'a ankrajlıdır (`kapi.py tarama` temiz); faz öncesi satır `ContentHash = null` okunur — `SkillScriptGrantContentHashMigrationTests` ×3
- [x] Karar 6: "Script execution gates" beş dosyada 1/1/1/1/2; `### R8` satır 224; `threat-model.md` "Accepted risks" R8'i taşır
- [x] `npm run build` sonrası yasak sandbox ifadesi taraması boş (`SECURITY.md`, `docs/MIMARI-TEHDIT-MODELI.md`, `docs-site/src/content/docs`, `llms-full.txt`)
- [x] `check-content.mjs` üç yeni kontrolü taşır; her biri bir kez kırmızı gösterildi ("Süreç Ölçümü" § Kırmızı → yeşil); `check-content.mjs` temiz; `dokuman-bakim.py --denetle` → 0
- [x] Karar 7: `TraconProductionRisk.cs` farkı boş
- [x] Karar 8: `grep -n "versioned, reversible" src/Tracon.Core/Audit/AuditingAgentSkillStore.cs` boş
- [x] Karar 9: `grep -rn "isolated operating-system process" src` boş; `public sealed class SandboxedSkillScriptRunner` duruyor
- [x] Karar 10: `production.md` `SkillRoots` salt okunur cümlesini taşır; `SkillRoots` XML'i aynı şartı söyler
- [x] Karar 11: sonuç (ii) — doğrulandı, `ADAYLAR.md`'de F-283; `RequiresApproval` doğrudan yolu da doğrulandı, ayrı satır F-284; `McpApprovalRuleSourceTests` kalır
- [x] Linux `/proc` ölçümü kayıtlı — okunabilir (Sapma 8, R8)
- [x] `CHANGELOG.md` `[Unreleased]`: `Security` kırıcı davranışı ve platform yetkisini, `Changed` `RunStoredScriptAsync` imzasını, `GET`'in çözümünü, kod adı `409`'unu ve bellek içi iptali yazar
- [x] Açılan dört K satırı (K-860…K-863) kategori etiketi taşır; `dokuman-bakim.py --denetle` yeşil
- [x] Dört doğrulama kapısı sıfır uyarı — "Kapanış Kapısı" bölümü
- [x] `samples/Tracon.Api` ile gerçek `run` — "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş (`kapi.py tarama`, kapanışın ilk adımı)
- [x] Manuel case'ler `MT-SKILL-064…069`, `072…077` eklendi; §6 ortak kurulumu ve bölüm başlıkları geri geldi; ➜ CI olanlar koşuldu, altısı örnek uygulamada ölçüldü
- [x] `faz-denetim` koşuldu; 🔴 2 gerçek, ikisi de düzeltildi — "Denetim Bulguları"
- [x] `docs-site/` güncellendi; `npm run check` dördü temiz
- [x] `en/agents.ts` ve `tr/agents.ts` eksiksiz (`tsc`, `i18n.test.ts`); bundle payı 162.542 → 164.676 B (Sapma 5)

### Doğrulama komutları

```bash
# Ön koşul: MT-SKILL-041'in geçici kod değişikliği; örnek uygulama çalışıyor.
export APB="Authorization: Bearer manuel-test-token-2026" APU="http://localhost:5080/tracon"
skill() { printf '{"name":"scriptli-skill","description":"Script testi.","instructions":"test","enabled":true,"resources":[],"scripts":[{"name":"merhaba","extension":"sh","content":"%s","parametersSchema":null}]}' "$1"; }
grant() { curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" -H "content-type: application/json" -d "$1"; }

# 1. v1 kaydı ve hash okuma
curl -s -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" -H "content-type: application/json" -d "$(skill 'echo merhaba-tracon')"
HASH=$(curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | jq -r '.scripts[0].contentHash')
# 2. hash yok → 400; doğru hash → 201 (GET /api/skill-script-grants: "contentHash":"<HASH>")
grant '{"skillName":"scriptli-skill","scriptName":"merhaba"}'
grant "{\"skillName\":\"scriptli-skill\",\"scriptName\":\"merhaba\",\"expectedContentHash\":\"$HASH\"}"
# 3. Gerçek run (Playground, MT-SKILL-058 adımları) → yanıtta "merhaba-tracon"
# 4. v2 yaz → aynı run reddedilir; eski hash ile grant → 409
curl -s -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" -H "content-type: application/json" -d "$(skill 'echo degisti')"
grant "{\"skillName\":\"scriptli-skill\",\"scriptName\":\"merhaba\",\"expectedContentHash\":\"$HASH\"}"
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

1. **Açık Soru 3'ün koşulu tutmadı: OpenAPI `readOnly` ÜRETİLEMİYOR; A korundu.**
   Plan "A, şema `readOnly` ve istekte zorunsuz çıkarsa; değilse B" diyordu. Ölçüm:
   `contentHash`/`scriptSetHash` şemaya `["null","string"]` ve `required` DIŞI
   girer (TS istemcisi derlenir, `emptyScript()` değişmedi) ama `readOnly` yoktur —
   ve belgenin TAMAMINDA `0` `"readOnly"` vardır: belgeyi tüketicinin
   `AddOpenApi()`'si üretir, `JsonSchemaExporter` bu anahtarı hiç basmaz
   (`Microsoft.AspNetCore.OpenApi` 10.0.10 ikilisinde yok). B de `readOnly`
   üretmezdi; yalnız alanı istek şemasından çıkarırdı, bedeli iki yeni public yanıt
   tipidir. A'da kalındı: özetler "a value sent in a request is ignored" der,
   `Hashes_and_origin_sent_with_a_skill_are_ignored` sunucunun gönderilen hash'i ve
   `origin`'i yok saydığını kanıtlar (konsol `GET` → `PUT` ile tam bunu gönderir).
   Pre-1.0'da geri dönüşü ucuzdur; kullanıcıya kapanış raporunda bildirildi.
2. **Migration'lar ayrı commit'e alındı (`07c787d9`).** Ankraj kuralı
   (`sql-migration.md`) dosyayı içeren commit'i ister; uygulama commit'i en sonda
   atılacağı için üç `.sql` önce commit'lendi ve `scripts/applied-migrations.json`
   ona ankrajlandı. Böylece `kapi.py tarama` faz boyunca yeşil kaldı.
3. **Red SEBEBİ SSE'de yok; fonksiyonel iddialar denetim izinden okunur.** MAF
   delegenin istisnasını modele `Error: Function failed.` diye geçirir. P testleri
   `script.denied` kaydının `after` alanını `/api/audit`'ten okur (`DenialsAsync`).
4. **§ 186.10 adım 1'in ölçümü: tam yol kuruldu, yedek yol gerekmedi.**
   `FakeModelProvider.CallsTool("run_skill_script", new { skillName, scriptName })`
   + `run_skill_script` için ayakta onay kuralı + run başına ayrı sağlayıcı/agent
   (sahte senaryo bir kez tüketilir). Delegeyi DI'dan çözmeye gerek kalmadı.
5. **Arayüz formu iki adımlı oldu: "Review content" → "Grant".** Plan formun hash'i
   `GET /api/skills/{name}`'den okuyup göndermesini istiyordu. Hash tıklama anında
   okunsaydı o an kayıtlı olan — kimsenin okumadığı — içerik pinlenirdi; bu, pinin
   reddetmek için var olduğu durumdur. Form içeriği gösterir ve yalnız gösterdiğini
   grant eder; `409`'da inceleme silinir ve yeniden okuma gerekir. Pin durumu
   (`current`/`stale`/`disk only`) saf fonksiyonlara taşındı
   (`screens/skills/model.ts`, `model.test.ts`, 6 test); skill kaydı pin okumalarını
   geçersiz kılar (`SKILL_PIN_QUERY_KEY`). Görsel doğrulama örnek uygulamada
   yapıldı (EN ve TR). Bundle payı: `index-*.js.br` **162.542 → 164.676 B**
   (+2.134 B, Brotli), bütçe 250 KB.
6. **E2E kancası: `UiHost.StartAsync(configureTracon:)`.** Grant ekranı
   `UseSkillScripts` ister; paylaşılan zincire eklemek her testin host'unu
   değiştirirdi. İki yeni `SkillTests` bu kancayı kullanır.
7. **Karar 11'in sonucu (ii): doğrulandı + kusur kaydı; `RequiresApproval` yolu da
   doğrulandı.** İki gerçek MCP sunucusuyla (`ModelContextProtocol.AspNetCore`,
   `Tracon.AspNetCore` üzerinden geçişli): `SecurityAdmin`'in `toolName`-yalnız
   kuralı, `AgentsAdmin` endpoint'i B'ye çevirince B'nin aynı adlı tool'unu onayladı;
   `AgentsAdmin` `requiresApproval: false` ile onayı kuralsız kapattı. Kayıtlar
   **F-283** ve **F-284** (`ADAYLAR.md` § Kusur giderme, 🟠). Düzeltme bu fazın
   kapsamı değildir (kural şeması `kalıcı-veri`, görev ayrımı kararı).
   `McpApprovalRuleSourceTests` bugünkü davranışı ölçer ve düzeltmede ters çevrilir;
   fonksiyonel test projesi bunun için `Tracon.Mcp`'ye referans alır.
8. **§ 186.8 madde 2 ölçüldü: Linux `/proc/<ppid>/environ` okunabilir.**
   `alpine:3.20`, root ve uid 1000: `env -i` ile temizlenmiş çocuk süreç
   `TRACON_TEST_SECRET=parent-env-value`'yu okudu. `EnvironmentAllowList` XML'i,
   R8, `concepts/tools.md`, `reference/threat-model.md` ve
   `reference/configuration.md` bunu söyler; MT-SKILL-062'nin başlığı "sızmaz"dan
   "miras alınmaz"a daraltıldı.
9. **Manuel test dosyasının bölüm yapısı geri kuruldu.** `14-SKILL-VE-SCRIPT.md`'nin
   bölüm başlıkları ve §6 ortak kurulumu Faz 58'de silinmişti (`946a37fb`, 459
   satır); case'ler hâlâ `§1`…`§7`'ye atıf yapıyordu (denetim 🟢1 on beşini saydı).
   Başlıklar §2–§7 geri geldi, §6 kurulumu güncel terimlerle (hash'li grant
   yardımcısı dahil) bölümün başına yazıldı, Faz 186'nın case'leri yeni §8'de
   toplandı (`064…069`, `072…077`; 070-071 doluydu). MT-SKILL-023/051/054/062/063
   güncellendi; 12 case ➜ CI.
10. **`SECURITY.md` kapsamı listeye çevrildi.** Sınır adı sitedeki tabloyla aynı
    biçimde ("Script execution gates (grant, content pin, interpreter allowlist,
    audit)") durur; OS yalıtımı "Out of scope"a girdi. Site politikası aynı iki
    satırı ve bir kapsam paragrafını taşır.
11. **`ui.md`'de ikinci bir yanlış düzeltildi.** Sayfa "skill script grants" için
    yönetişim alanında ayrı ekran olduğunu söylüyordu; grant paneli Skills
    ekranındadır.
12. **`SkillScriptGrant.ContentHash` özeti `<see cref>` taşımaz.** OpenAPI açıklaması
    property cref'ini `string AgentSkillScriptDefinition.ContentHash` diye basıyordu;
    cref'ler `<remarks>`'a taşındı. `SkillScriptGrantRequest.ExpectedContentHash`
    için aynı.
13. **Dokümantasyon ekran görüntüleri yeniden üretildi.** UI kaynağı değişince
    `check-content.mjs` damgası kırmızı olur; `TRACON_UI_SCREENSHOTS=1` +
    `refresh-console-screenshot-stamp.mjs` iki kez koşuldu (denetim düzeltmesinden
    sonra yeniden).
14. **Denetim 🔴1 — kod skill'inin adı artık yönetim API'sinden yazılamaz
    (kullanıcı kararı, 2026-09-24).** Açık Soru 1 A'nın yan etkisiydi: `GET
    /api/skills/{name}` kodu önce çözünce, `GET` ile okuyup `PUT` ile yazan konsol
    düzenleyicisi gölgelenen kayıtlı kopyanın üstüne kod içeriğini sessizce
    yazıyordu (faz öncesi form kayıtlı içeriği gösteriyordu). Seçilen düzeltme
    agent'lardaki K-003 emsalidir: `PUT` kod adına `409`
    (`Code-defined skill cannot be modified`); `DELETE` gölgelenen kayıtlı kopyayı
    siler, kopya yoksa `409`; düzenleyici `origin: Code`'da salt okunur açılır
    (`<fieldset disabled>`, uyarı, yalnız "Delete stored copy"). Katalog
    `IsDefinedInCode(name)` kazandı. Testler: `SkillCrudTests` (2 yeni),
    `SkillTests.A_skill_defined_in_code_opens_read_only`; gölgeleme pin testi
    satırı artık store üzerinden yazar. **K-863.**
15. **Denetim 🔴2 — bundle ölçüm notu `git stash` öneriyordu.** `kurtarma.md` bunu
    yasaklar; not worktree yöntemiyle yeniden yazıldı. Aynı sınıftaki
    `test-yalitimi.md` notu (🟢2) da düzeltildi (worktree; stash yalnız son çare,
    `Saved working directory` görülmeden `pop` yok).
16. **Kapanış kapısının üç kırmızısı (ilk koşum).** (a) Yeni E2E locator'ları
    `Exact`/`.First` taşımıyordu — taban büyütülmedi, çağrılar düzeltildi; (b)
    `ExpectedContentHash`'in HTTP gövdesi `<example>` içindeydi ve C# diye
    derlendi — gövde `<example>` dışına, `<code language="json">` olarak taşındı
    (`AssertValidJson` yapılandırma parçası bekler); (c) fazla ilgisiz
    `ConfirmationTests` yarışı: playground'da `Echo:` görünür görünmez `/sessions`'a
    gidiyordu, oturum kaydı ise tur kaydedilince yazılır — yük altında 30 sn
    `No session`. Yardımcı artık `GET /api/sessions` dolana kadar bekler
    (`WaitUntil`); vaka `test-yalitimi-vakalari.md`'de.
17. **İnceleme paneli argüman şemasını da gösterir (denetim 🟡1).** Hash şemayı da
    pinler; panel yalnız içeriği gösteriyordu. Şema (ya da "No argument schema")
    gösterilir, E2E bunu iddia eder.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

- **K-860** *(güvenlik, kalıcı-veri, public-api)* — script grant'ı içeriği pinler
  (Karar 1-4, 10): stored/kod script'inin grant'ı hash taşır, hash'siz eski grant
  onları yetkilendirmez, diskteki script pinlenmez.
- **K-861** *(güvenlik)* — çok kiracılı host'ta stored script grant'ı platform
  yetkisi ister (Karar 5, 7).
- **K-862** *(public-api, güvenlik)* — B7 "Script execution gates", R8 ve kapsam
  satırı (Karar 6, 9).
- **K-863** *(public-api)* — kodda kayıtlı skill adı yönetim API'sinden yazılamaz
  (`PUT`/`DELETE` `409`, gölgelenen kopya silinebilir), konsol salt okunur
  (denetim 🔴1, kullanıcı kararı).

K-* almayanlar:

| Karar | Gerekçe |
|---|---|
| Karar 12 — bellek içi iptal satırı tutar, `TryUpdate` döngüsü (kullanıcı kararı) | K-092'ye uyum; store sözleşmesi zaten "including revoked" diyordu |
| Karar 8 — skill kaydının denetimi best-effort kalır; gerekçe yeniden yazıldı (kullanıcı kararı) | Kodu çalıştıran izin (`script.grant`, pinli) fail-closed'dır |
| Açık Soru 3 → **A** korundu, `readOnly` üretilemedi | Sapma 1; özet "ignored in a request" der, test kanıtlar |
| Açık Soru 4 → **A** (dar grant bayatsa geniş grant'a düşülmez) | Fail-closed; store sözleşmesi değişmez (`FindActiveAsync` dar olanı önce döndürür) |
| Açık Soru 5 → **A** (arayüz bayat grant'ı işaretler) | "Etkin görünüp çalıştırmayan grant yanıltıcıdır" |
| Açık Soru 6 → **A** (operatör kimliği ölçüldü, `production.md`'ye yazıldı) | Kiracıya bağlı `PlatformAdmin` anahtarı · statik token + başlık · policy'li claims; yeni rota yok |
| Arayüzde iki adımlı "Review content" → "Grant" | Sapma 5 |
| Red metinleri (`Content hash required`, `Content changed`, `does not pin`, `changed since the grant`) | Uç ve runner metni; `409` güncel hash'i taşımaz |
| Karar 11 sonucu (ii) → F-283, F-284 | Sapma 7 |
| `UiHost.StartAsync(configureTracon:)` | Sapma 6 |

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
| Plan revizyonu sayısı | 0 (plan metni değişmedi; Açık Soru 3'ün koşulu ölçümle tutmadı — Sapma 1) |
| Düzeltme turu sayısı | 2 — ilk kapanış kapısı (3 kırmızı, Sapma 16) · denetim (🔴 2, 🟡 4, 🟢 3) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 |
| Fazın ürettiği regresyon | 2 — konsol düzenleyicisinin gölgelenen kopyayı ezmesi (denetim 🔴1) ve iki kapı kırmızısı (locator tabanı, `<example>` derlemesi); üçü de commit'ten önce kapandı |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz yeni kapandı) |

### Kırmızı → yeşil kanıtı

- **Pin fonksiyonel testleri** runner kapısından önce yazıldı ve kırmızıydı
  (§ 186.10 adım 1); çıktı metni kaydedilmedi.
- **`check-content.mjs`'nin üç kontrolü**, geçici değişiklikle birer kez:
  (a) `SECURITY.md no longer states its script execution gates scope;
  reference/security-policy.md still does` · (b) `concepts/tools.md: "inside its
  sandbox" promises a script sandbox Tracon does not provide; the boundary is
  "Script execution gates"` · (c) `reference/threat-model.md: "Accepted risks" no
  longer names the operating-system identity a granted script runs with`. Geri
  alınınca çıkış 0.
- **`AuditingSkillScriptGrantStoreTests.A_grant_cancelled_after_its_audit_entry_leaves_no_permission`**
  sıra ters çevrilirse (önce grant, sonra kayıt) düşer: grant iptal gelmeden
  yazılır. Ters çevrilerek koşulmadı; akıl yürütme.
- **`McpApprovalRuleSourceTests`** kırmızı→yeşil değil, ölçümdür (Karar 11).

## Örnek Uygulama Koşumu

`samples/Tracon.Api` (Debug, PostgreSQL izleği, gerçek `gpt-5.4-mini`), MT-SKILL-041'in
geçici kod değişikliğiyle; koşumdan sonra geri alındı. Yükseltmeden ÖNCE eski biçimde
bir grant satırı SQL ile yazıldı (`granted_by = 'pre-phase-seed'`). Onay kartları için
`load_skill` ve `run_skill_script` kuralları eklendi, run'lar API'den koşuldu:

| Adım | Sonuç |
|---|---|
| Açılış | `__migrations` max `53`; tohum satırı `content_hash IS NULL` = `t` |
| Case 6 (MT-SKILL-069) — faz öncesi grant | run `Completed`, çıktıda `merhaba-tracon` yok; `script.denied`: `The execution grant does not pin the script content. Grant it again with the content hash.` |
| Case 4 (MT-SKILL-067) — hash'siz `POST` | `400 Content hash required` |
| Case 1 (MT-SKILL-064) — hash'li `POST` + run | `201`; listedeki `contentHash` okunanla aynı (`origin: Database`); run çıktısı `merhaba-tracon` |
| Case 2 (MT-SKILL-065) — v2 `PUT` + run | `PUT 200`; run `Completed`, çıktıda `merhaba-tracon` yok; `script.denied`: `The script content changed since the grant. Review it and grant it again.` |
| Case 3 (MT-SKILL-066) — eski hash'le `POST` | `409 Content changed`; liste aynı; gövde güncel hash'i taşımadı |
| Case 11 (MT-SKILL-076) — arayüz | satır `stale`; "Review content" `echo degisti` + hash; Grant sonrası `current`; bilinmeyen ad "No stored or code skill…"; TR metinleri |

Koşumdan sonra grant iptal edildi, iki onay kuralı, agent ve skill silindi (iptal
edilen grant satırları SQL'de kalır — tasarım gereği).

## Kapanış Kapısı

`DOTNET_ROOT=~/.dotnet python3 scripts/kapi.py kapanis --taban d6153fe1`, commit
öncesi çalışma ağacı (kod bu hâliyle commit edildi; sonraki arşivleme ve damıtma
yalnız dokümandır), 2026-09-24 → **EXIT 0, ~703 sn**:

| Adım | Süre | Sonuç |
|---|---|---|
| `kapi.py tarama` | 5,7 sn | ✅ temiz (migration ankrajları dahil) |
| `dokuman-bakim.py --denetle` · Python testleri · ajan haritası · denetim paketi | ~8 sn | ✅ |
| `dotnet build Tracon.slnx -c Release` | 52,1 sn | ✅ 0 uyarı |
| `dotnet test … -maxcpucount:2 -- --report-trx` | **485,6 sn** | ✅ 17.577 test, 0 kırmızı |
| `dotnet pack` | 7,9 sn | ✅ |
| `dotnet format --verify-no-changes` | 112,8 sn | ✅ |
| `docs-site npm run check` | 31,4 sn | ✅ 1060 sayfa; en ağır `troubleshooting` 58 994 B |

Performans kapısı tetiklenmedi (üç sıcak yol değişmedi). İlk kapanış denemesi test
adımında durdu (Sapma 16: locator tabanı, `<example>` derlemesi, `ConfirmationTests`
yarışı); o sırada denetçi de koşuyordu — denetçiye `dotnet`/`npm` koşturulmadı.

**Yayın provası** (`kapi.py yayin --kuru`, temiz ağaç `dcb7207f`, 2026-09-24) — sevk
edilen `SkillScriptGrantContract`'a case eklendiği için zorunlu → **EXIT 0**: 20 paket
`1.0.0-preview.2.51` · `npm publish --dry-run` ✅ · `provider/source/generated-tool AOT
host smoke passed` · `net8.0 consumer smoke passed on .NET 8.0.31` · `6 exact-version
packed sample`. İlk deneme örnek sözleşmesinde durdu (`release feed contains stale
Tracon packages`: `preview.1`, `.2.46`, `.2.48` damgaları); belgelenmiş tarifle
`rm -rf artifacts/package/release` sonrası yeniden koşuldu. Grant sözleşmesini koşan
bir örnek store yoktur (`grep` boş); yeni case'ler örnekleri etkilemez.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

Denetçi (`faz-denetcisi`, 2026-09-24) salt okuma yaptı; kapanış kapısı aynı anda
koşuyordu ve denetçiye `dotnet`/`npm` koşturulmadı.

| # | Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|---|
| 🔴1 | `GET /api/skills/{name}` kodu önce çözünce konsol düzenleyicisi gölgelenen kayıtlı kopyanın üstüne kod içeriğini yazıyor | 🔴 | gerçek (kullanıcı) | Düzeltildi — `PUT`/`DELETE` `409`, salt okunur düzenleyici, 3 test (Sapma 14, K-863) |
| 🔴2 | Hafıza notu taban ölçümü için `git stash` öneriyor (`kurtarma.md` yasaklar) | 🔴 | gerçek (kullanıcı) | Düzeltildi — iki not da (Sapma 15) |
| 🟡1 | İnceleme paneli `parametersSchema`'yı göstermiyor; hash onu da pinler | 🟡 | — | Düzeltildi — şema gösterilir, E2E iddia eder (Sapma 17) |
| 🟡2 | "Pinsiz başlayan script açıktır" ifadesi disk script'ini (tasarım gereği pinsiz) kapsıyordu | 🟡 | — | Düzeltildi — `SECURITY.md`, politika, iki sınır tablosu ve B7 "stored or code-defined" ile sınırlandı |
| 🟡3 | Üç XML metni bayat (`AgentSkillRequest.Scripts`, `SkillScriptGrantRequest`, `AgentSkillDefinition.Scripts`) | 🟡 | — | Düzeltildi |
| 🟡4 | #20: yarış testi kayıp güncellemeyi yakalamaz · #29: iptal testi yok | 🟡 | — | #20 gerekçelendi: sözleşme tek kaydı ve son grant'ı kanıtlar; kayıp güncelleme kurguyla kapalıdır (bellek içi `TryUpdate` CAS döngüsü, SQL tek satır `UPDATE`) — iddia daraltıldı. #29 düzeltildi: `A_grant_cancelled_after_its_audit_entry_leaves_no_permission` |
| 🟢1 | `14-SKILL-VE-SCRIPT.md`'de başlıksız 15 bölüm atfı | 🟢 | — | Düzeltildi — §2–§8 başlıkları (Sapma 9) |
| 🟢2 | `test-yalitimi.md:92` da `git stash … pop` öneriyor | 🟢 | — | Düzeltildi (Sapma 15) |
| 🟢3 | Hiçbir kaynağın taşımadığı adda gönderilen `expectedContentHash` yok sayılır | 🟢 | — | Gerekçelendi — § 186.3'ün seçimi; runner stored yolu yine reddeder (fail-closed) |

Denetçinin "koşum kanıtı gerekiyor" listesi: kapanış kapısı ve tarama "Kapanış
Kapısı" bölümünde; `check-content.mjs`'nin kırmızı kaydı "Süreç Ölçümü" § Kırmızı →
yeşil'de.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

**Sıradaki faz: 187** (kırıcı değişiklik kapısı). 187.9'un beklediği:

- `CHANGELOG.md` `### Changed` üyeyi code span içinde tip önekiyle anar:
  `` `SandboxedSkillScriptRunner.RunStoredScriptAsync` `` — 187.6'nın eşleşme kuralı
  (`.` ile bölünen parçalardan biri tip adına eşit) bunu kabul eder. Taban
  `v1.0.0-preview.2` iken `Tracon.Core`'da `SandboxedSkillScriptRunner` için `CP0002`
  (eski aşırı yükleme kalktı) beklenir.
- Eklemeler (`ContentHash`, `ScriptSetHash`, `Origin`, `ExpectedContentHash`, beş
  sözleşme testi) kırıcı değildir; strict baseline kapalıyken raporlanmaz.
- HTTP davranış değişiklikleri ApiCompat'a görünmez, `### Changed`'dedir: `GET
  /api/skills/{name}` kodu önce çözer; `PUT`/`DELETE` kod adına `409`.

Açık işler:

- 🟠 **F-283** (onay kuralı MCP kaynağına bağlı değil) ve **F-284** (`AgentsAdmin`
  `RequiresApproval`'ı kapatır) — güvenlik; birlikte planlanır.
  `McpApprovalRuleSourceTests` bugünkü davranışı ölçer ve düzeltmede ters çevrilir.
- Manuel case'ler: `MT-SKILL-064…069`, `072…077` tur kaydında yok (altısı kapanışta
  örnek uygulamada ölçüldü, 12 case ➜ CI).
- Site yayını (`faz-tamamlama` Adım 10) kullanıcı onayı bekler.

🚨 Tuzaklar (ayrıntı alan dosyalarında):

- OpenAPI `readOnly` üretilemez — `http-uc-guvenlik-ve-sozlesme.md`.
- MAF araç hatasını modele `Error: Function failed.` diye geçirir; red sebebi denetim
  izindedir — `maf-api.md`.
- `EnvironmentAllowList` yalnız mirası keser; Linux `/proc/<ppid>/environ` okunur —
  `secenek-baglama-ve-gizlilik.md`.
- Taban ölçümü worktree'de alınır, `git stash` ile değil — `kurtarma.md`,
  `frontend-test-altyapisi.md`.
