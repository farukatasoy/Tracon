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

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman. Önce § 186.0'ın kontrollerini koş.
2. Kararlar — dosyanın tamamını **okuma**, yalnız grep'le:
   ```bash
   grep -n "\*\*K-0\(66\|82\|85\|86\|87\|89\|92\) \|\*\*K-\(603\|773\|776\|812\|853\|855\) " docs/KARARLAR.md
   sed -n '934,940p' docs/arsiv/KARARLAR-GECMISI.md   # K-086: neyi SAĞLAMAZ
   ```
   K-066 script, K2'nin istisnası · K-082 skill sürüm geçmişi yok · K-085 derlenmiş
   agent anahtarında skill parmak izi · K-086 OS yalıtımı host'un işi, yetki kararını
   kilitlemez · K-087 stored script · K-089 denetime yazılamayan script çalışmaz ·
   K-092 grant silinmez, `revoked_at` · K-603 `Shipped` boş · K-773 profil kümesine
   anahtar eklemek kırıcı · K-776 altı fail-closed işlem sözleşmedir · K-812 parmak
   izi gömülen içeriği ölçer · K-853 platform yetkisi deseni · K-855 kategori etiketi.
3. Alan hafızası — yalnız işaret edilen madde:
   ```bash
   grep -n "KISMI kapsam\|SIRAYI goremez" docs/hafiza/genisleme-noktalari-ve-denetim.md  # dekoratör; "mutasyon olmadı" testi
   grep -n "SERİLEŞTİRİLEREK" docs/hafiza/agent-derleme-ve-onbellek.md                    # alan kapsamı kapısı emsali
   grep -n "skill_script_grants" docs/hafiza/sql-server-tuzaklari.md                      # ISNULL upsert deseni
   ```
   Öteki hafıza okumaları § 186.10'da, onları kullanan adımdadır. Önceki fazın devir
   notu ve `MIMARI.md` gerekmez; bu faz önceki fazın sözleşmesini devralmaz.
4. **Faz 185'ten gelen ortam notları** (2026-09-24; sözleşme devri değil):
   - 🚨 Bu makinede net8 runtime'ı yalnız `~/.dotnet` altındadır; çok hedefli test
     projeleri için her komutta `export DOTNET_ROOT=$HOME/.dotnet` (PATH'teki
     `dotnet` sistemin kalır) — `hafiza/test-kosum-tuzaklari.md` "Çoklu TFM test
     runtime'ları".
   - Her host başlangıçta Tracon aile derlemelerinin sürümünü karşılaştırır
     (`PackageFamilyAlignmentService`, K-859). Bir test karışık derleme yüklerse
     host `TraconException` ile başlamaz; seam `LoadedPackageFamily`'dir
     (`AddTracon()`'dan önce kaydedilir).
   - Karar numarası: son `K-859`; sıradaki **`K-860`**. Listedeki "K-855 kategori
     etiketi" satırı K-855'in kendisini değil, etiket kuralının başladığı numarayı
     anlatır (K-855 = `net8.0`/`net9.0` düşürme kararı); kural `AGENTS.md`'dedir.
   - Aday sayacı `ADAYLAR.md` § *F-ID tahsis kuralı*'ndadır (bir F-ID'yi metinde
     anmak onu "kullanılmış" sayar; sayaç numarasını buraya yazma).

---

## Amaç

Script grant'ı bugün bir **ada** verilir, bir **koda** değil. `SecurityAdmin` v1'i
onaylar. `AgentsAdmin` anahtarı aynı ada v2 yazar. v2 eski grant ile sunucuda çalışır.
`SecurityAdmin` "yetkiyi yalnız ben genişletirim" vaadini verir (`ApiKeyScope.cs:80-85`);
bu vaat kırıktır. Tehdit modeli "Script sandboxing" sınırını ilan eder. Böyle bir sınır
yoktur (K-086); script Tracon'un OS kimliğiyle çalışır. Fazdan sonra grant onaylanan
içeriğe bağlıdır, çok kiracılı host'ta stored script grant'ı platform yetkisi ister ve
tehdit modeli gerçek davranışı söyler. preview.1 ve preview.2 2026-09-20'de çıktı.
Özellik varsayılan kapalıdır; açan tüketici için yükseltme bugün vardır.

- **F-269** — grant'ı içeriğe bağla (hash pin), çok kiracılı host'ta stored script
  grant'ını platform yetkisine bağla, B7'yi yeniden adlandır, R8'i ekle.

### Bugün ne çalışmıyor — doğrulanmış kanıt

Tasarım bölümlerinde geçen kanıtlar orada, tek `dosya:satır` ile durur.

| Kanıt | Gözlem |
|---|---|
| `SkillScriptGrant.cs:17-54` | Tenant, skill, script adı, veren, zamanlar, `RevokedAt`. Hash yok. `ScriptName` `null` → bütün script'ler (`:12-15`) |
| `SandboxedSkillScriptRunner.cs:255-268` | Grant yalnız `(tenant, skill, script)` adıyla aranır; içerik karşılaştırması yok |
| `SandboxedSkillScriptRunner.cs:171-231`, `:340-344` | Stored yol `Name`, `Extension.TrimStart('.')`, `ParametersSchema`, `Content` okur; `Content` scratch dosyasında çalışır; `Description` okunmaz |
| `SandboxedSkillScriptRunner.cs:12-20` | XML "isolated operating-system process" (`:14`) ve "the sandbox" der; K-086 OS yalıtımını reddeder |
| `TraconSkillsSource.cs:81-93` · `SkillScriptSupport.cs:107-133` | Delegate her derlemede güncel tanımdan kurulur, TEK script yakalar; runner öteki script'leri görmez |
| `SkillEndpoints.cs:43-45`, `:90-132` | `PUT /api/skills/{name}` Admin + `AgentsAdmin` ister; grant store'a dokunmaz |
| `SkillScriptGrantEndpoints.cs:36-38`, `:70-123` | Grant `SecurityAdmin` ister; skill'e bakmaz, hash hesaplamaz; var olmayan ada önceden grant verilir |
| `AgentDefinitionCompiler.Skills.cs:39-60` | DB kaynağı disk kaynağından önce gelir; aynı adlı stored skill grant'lı file skill'i gölgeler |
| `AgentSkillCatalog.cs:103-106` · `SkillEndpoints.cs:74-88` | Runtime önce kod skill'ine, sonra store'a bakar; `GET /api/skills[/{name}]` yalnız store'u okur. `AddSkill` + `Scripts` stored yoldan çalışır; `ITraconBuilder.cs:226` "it carries no code" der; bu yolu koşan test/örnek yok (grep) |

> 2026-09-23'te HEAD `bb9953e3` üzerinde doğrulandı (`src/`, `tests/`, `docs-site/`
> temiz). `KARARLAR-INDEKS-REDDEDILEN.md`'de `script`/`sandbox`/`grant`/`hash` için
> kalem yok (grep). F-169 (Hyperlight) karar eşiğinde (`ADAYLAR.md:378`); K-086'nın
> yeniden açılma koşulu oluşmadı.

---

## 186.0 — Başlamadan önce: birleşmiş iş ve kontroller

`main`'de varsayılan: K-852 · K-853 · K-854 (`bb9953e3`) ve bu turun kusur-giderme
şeritleri (başlık değeri `***`, sabit 403 detayı, K-855 kategori kapısı, `CHANGELOG.md`
`Deprecated` notu). 🚨 Bu fazın 403 testleri yalnız durum kodunu ve `title`'ı doğrular;
`detail` başka şeridin sahipliğindedir.

```bash
grep -n "\*\*K-855 " docs/KARARLAR.md            # boş değil; satırın adlandırdığı kapıyı koş → yeşil
awk '/^## \[Unreleased\]/{u=1;next} /^## \[/{u=0} u && (/^### Security/ || /^### Changed/)' CHANGELOG.md
                                                 # iki satır (bugün :9, :122)
grep -n "mint_a_platform_key\|mints_a_platform_key" tests/Tracon.AspNetCore.FunctionalTests/CrossTenantAuthorityTests.cs
                                                 # dört test (bugün :371, :383, :405, :418) → sınıf yeşil
```

- K-855 satırı yoksa veya kapısı kırmızıysa: dur, bakımcıya sor.
- `[Unreleased]` altında başlık eksikse: eksik başlığı aç. Güvenlik sürümü bölümü
  sürüm başlığına taşımış olabilir.
- Bu testler yoksa veya kırmızıysa: dur, bakımcıya sor. Karar 5 bu kapı olmadan
  uygulanmaz.

**Neden önkoşul?** Kapsam daraltma yalnız API anahtarıyla gelen isteğe uygulanır
(`ApiKeyEndpoints.cs:96-110`). Düzeltmeden önce `POST /api/api-keys` yalnız Admin +
`SecurityAdmin` istiyordu; claims tabanlı bir kiracı Admin'i platform policy'si
olmadan `PlatformAdmin` anahtarı basıyordu ve `CheckAsync` o anahtarla her kiracı
için geçiyordu. Ölçüldü: `Claims_user_without_platform_authority_cannot_mint_a_platform_key`
düzeltmeden önce kırmızıydı. Düzeltme (`ApiKeyEndpoints.cs:112-120`): istenen
kapsamlarda `PlatformAdmin` varsa uç `CrossTenantAuthority.CheckAsync(httpContext,
targetTenantId: null)` çağırır; red `403`'tür. Kullanıcı kararı: yeni K açılmadı,
K-853 satırına "genişletildi" notu yazıldı. Karar 5 aynı deseni kullanır; basma yolu
açık kalsaydı her kiracı Admin'i Karar 5'i kendi anahtarıyla geçerdi.

---

## 186.1 — Grant içeriği pinler: hash girdisi ve biçimi

**Karar 1 (kullanıcı kararı, 2026-09-23):** grant bir içerik hash'i taşır. Runner
stored script için hash'i karşılaştırır; uyuşmazlıkta ayrı sebeple reddeder:
"content changed since the grant". Üç SQL sağlayıcısında migration yapılır.

**Karar 2 (kullanıcı kararı, 2026-09-23):** skill geneli grant (`ScriptName` `null`)
script KÜMESİNİN parmak izini pinler. Script eklemek, silmek veya değiştirmek grant'ı
geçersiz kılar.

**Hash girdisi koddan belirlendi** (`SandboxedSkillScriptRunner.cs:171-231`, `:340-344`):

- `Extension` (`TrimStart('.')` sonrası, harf korunur) — yorumlayıcıyı seçer (`:270`),
  dosya adını kurar (`:340`). **Girer.**
- `Content` — scratch dosyasında çalışır (`:343`). **Girer**, UTF-8 bayt, normalleştirme yok.
- `ParametersSchema` — argüman kapısını kurar (`:199-215`, `:295`). **Girer**, ham
  metin; `null` boş metin gibi (runner ikisini aynı işler, `:199`).
- `Name` — grant anahtarı. Tekil hash'e girmez; küme parmak izine girer.
- `Description` — yalnız modele gider (`TraconSkillsSource.cs:118-121`). **Girmez.**

**Biçim** — emsal `ToolApprovalRuleEvaluator.ComputeArgumentsHash`
(`ToolApprovalRuleEvaluator.cs:223-272`): alan `<UTF-8 bayt uzunluğu>:<değer>` (LP),
SHA-256, `Convert.ToHexString` (64 büyük harf karakter).

- Tekil = SHA-256(`"tracon.skill-script.v1"` · LP(extension) · LP(content) · LP(schema ?? "")).
- Küme = SHA-256(`"tracon.skill-script-set.v1"` · LP(sayı) · `Name`'e göre `Ordinal`
  sıralı her script için LP(name) · LP(tekil hash)).
- Sabit önek alan ayrımıdır: tekil hash küme parmak iziyle eşleşmez (aynı sütun, § 186.5).
- Sıfır script de parmak izi üretir; eklenen ilk script onu değiştirir.

🚨 **Alan kapsamı kapısı.** `AgentSkillScriptDefinition`'a eklenen, çalıştırmayı
etkileyen bir alan hash'e girmezse pin **sessizce** delinir (K-812 sınıfı). Kapı:
`SkillScriptHashTests.Every_property_is_hashed_or_explicitly_excluded` — her özellik
hash'i değiştirir ya da gerekçeli hariç listededir (bugün `Description`, `Name`).
Emsal: `DefinitionFingerprintTests.cs:45`.

**Tek uygulama:** `Tracon.Abstractions` içinde bir `internal static` sınıf. Runner ve
grant ucu aynı public hesaplanan özellikleri okur. `SHA256` ve `Encoding.UTF8` AOT
temizdir. **Pin içeriği bağlar, kimliği değil:** aynı içerikle yeniden yaratılan
skill çalışır; farklı içerikle yaratılan reddedilir.

## 186.2 — Runner kapısı

Dosya yolu (`RunFileScriptAsync`) değişmez (Karar 10). Stored yol:

```mermaid
flowchart TD
    accTitle: Stored script runner gates after content pinning
    accDescr: The stored path checks that a grant exists, that it pins content, and that the pinned value matches the script hash or the script set hash, before the interpreter allowlist.
    A["RunStoredScriptAsync(skill, scriptName)"] --> B{"Enabled ve<br/>AllowStoredScripts?"}
    B -- hayır --> X1["red: özellik kapalı"]
    B -- evet --> C{"etkin grant?<br/>dar olan önce"}
    C -- yok --> X2["red: geçerli grant yok"]
    C -- var --> D{"ContentHash null?"}
    D -- evet --> X3["red: grant içeriği pinlemiyor"]
    D -- hayır --> E{"ScriptName null?"}
    E -- hayır --> F{"= script.ContentHash?"}
    E -- evet --> G{"= skill.ScriptSetHash?"}
    F -- hayır --> X4["red: content changed<br/>since the grant"]
    G -- hayır --> X4
    F -- evet --> H["yorumlayıcı → argüman → denetim → süreç"]
    G -- evet --> H
```

- **İmza değişir:** runner skill tanımını ve script adını alır, script'i `skill.Scripts`
  içinde `Ordinal` adla bulur, bulamazsa reddeder. `CreateStoredScriptDelegate`
  (internal) skill tanımını yakalar; `TraconSkillsSource.CreateSkill` onu geçirir.
- **Hash çalışacak nesneden hesaplanır.** Kontrol ile kullanım arasında store yeniden
  okunmaz. Bayat derlenmiş agent önbelleği (K-085/K-811) yalnız pinli içeriği çalıştırır.
- **Dar grant kazanır:** `FindActiveAsync` tek satır döner (`ISkillScriptGrantStore.cs:30-33`);
  bayat dar grant, eşleşen geniş grant varken de reddeder (Açık Soru 4).
- **Red sebepleri ayrıdır** ("pinlemiyor" · "content changed since the grant"). İkisi
  `DenyAsync` ile `script.denied` kaydına ve `ScriptDenialReason` span etiketine gider
  (`:472-500`). Kesin İngilizce metni kod seçer, testler sabitler.
- XML (`:12-43`): kapı 2 "içeriği pinleyen geçerli grant" olur; "isolated
  operating-system process" ve "the sandbox" düşer (Karar 9).

**Karar 3 (kullanıcı kararı, 2026-09-23):** hash'siz mevcut grant'lar stored script
için fail-closed'dır; sürüm notu kırıcı değişiklik yazar; file script'ler etkilenmez.
Backfill yok: değişmiş olabilecek içeriği sessizce onaylardı.

**Karar 10 (kullanıcı kararı, 2026-09-23):** file script'ler pinlenmez; dosya yolu
hash'li/hash'siz her etkin grant'ı kabul eder. Üretim dokümanı `SkillRoots`'un süreç
kullanıcısı için **salt okunur** olmasını şart koşar (K-086: host'un işi). Gerekçe:
onaylı stored script aynı OS kullanıcısıyla pinsiz file script'i değiştirebilir.

## 186.3 — Grant ucu: köken, beklenen hash ve TOCTOU

**Karar 4 (kullanıcı kararı, 2026-09-23):** stored skill için grant isteği beklenen
hash'i taşır; uyuşmazlık `409`. "v1'i gör, araya v2 yazılsın, grant v2'yi pinlesin"
penceresi kapanır.

**Köken.** Uç runtime'ın önceliğini kullanır: önce kod, sonra store
(`AgentSkillCatalog.cs:103-106`). Kataloğa kökeni döndüren `internal` bir arama eklenir
(AspNetCore Core internal'larını görür: `Properties/AssemblyInfo.cs:6`). 🚨 **Arama
`Enabled`'a göre SÜZMEZ** — `ExistsAsync` (`:83-84`) gibi. `GetEnabledAsync`
(`:86-100`) süzer; onu kullanmak devre dışı stored skill'i "bulunamadı" yapar: grant
`201` + `ContentHash = null`, platform kontrolü yok; skill etkinleşince grant her
stored script'i görünmeden reddeder.

**Beklenen hash nereden okunur?** Gözden geçiren kişi runtime'ın çözdüğü tanımı
okumalıdır. `GET /api/skills[/{name}]` ise yalnız store'u okur (`SkillEndpoints.cs:74-88`):
kod skill'inin hash'i okunamaz, gölgelemede kalıcı `409` çıkar (#26). Çözüm Açık Soru 1.

**API anahtarının kapsam kümesi:** `AgentsRead` (hash'i okuyan `GET /api/skills/{name}`,
`SkillEndpoints.cs:34`; kapsam birebir eşleşir, `TraconEndpointFilter.cs:395`) +
`SecurityAdmin` (grant) + çok kiracılı host'ta stored skill için `PlatformAdmin`.

Mevcut kontrollerden **sonra** (`Enabled` → 409, `skillName`/`expiresAt` → 400;
`SkillScriptGrantEndpoints.cs:89-108`):

| Durum | Yanıt | Grant |
|---|---|---|
| Ad hiçbir kaynağa çözülmez (file skill / henüz yok) | `201`, `ContentHash = null` | yalnız file script'i yetkilendirir |
| Stored (devre dışı dahil), beklenen hash yok / boşluk / 64 onaltılık değil | `400` | yok |
| Stored, `scriptName` skill'de yok | `404` | yok |
| Çok kiracılık açık, köken store, platform yetkisi yok | `403` | yok |
| Stored, güncel ≠ beklenen (harf duyarsız) | `409` | yok |
| Stored, eşleşir | `201`, `ContentHash` = güncel | var |
| Kod skill'i (gölgeleme dahil) | Açık Soru 1 — A: stored satırları, platform yetkisi yok · B: `409`, gölgelemeyi adlandırır · C: `201`, `ContentHash = null` | A: eşleşirse · B: yok · C: var |

- Karşılaştırılan: `scriptName` varsa script hash'i, yoksa küme parmak izi.
- `409` güncel hash'i **taşımaz**; kör yeniden deneme pencereyi geri açardı.
- 🚨 **Fail-closed sırası:** `400`/`403`/`404`/`409` `store.GrantAsync`'i çağırmaz,
  `script.grant` yazılmaz. Her red testi listenin boş kaldığını ve kaydın olmadığını
  doğrular.
- `script.grant` `after` `ContentHash`'i taşır (`AuditingSkillScriptGrantStore.cs:91`
  grant'ı serileştirir). K-776 kümesi değişmez.
- Skill kaydı grant'a dokunmaz. Otomatik revoke reddedildi: iki store arasında
  atomiklik yok; özel `IAgentSkillStore` kapıyı atlar. Revoke değişmez, platform
  yetkisi istemez.
- `WithDescription` (`:42-47`) yeni durumları, kapsam kümesini ve hash kaynağını;
  `PUT /api/skills` açıklaması (`SkillEndpoints.cs:49-57`) pin sonucunu söyler.

## 186.4 — Çok kiracılı host'ta platform yetkisi

**Karar 5 (kullanıcı kararı, 2026-09-23):** çok kiracılık açıkken
(`TraconTenancyOptions.Enabled`) STORED script'e grant K-853 deseniyle platform yetkisi
ister: anahtarda `PlatformAdmin` · statik `AuthToken` · claims için
`TraconPolicies.PlatformAdmin`. Tek kiracılı host değişmez.

Gerekçe: stored script Tracon'un OS kimliğiyle çalışır (R8); kiracı Admin'i grant'ı
kendine verir. K-853'ün sınıfıdır: kiracının başka kiracının `secret`'ına ulaşması
"korunacak bir geri düşüş değildir" (K-853, `grep -n "K-853" docs/KARARLAR.md`). K-086 yalnız OS
yalıtımını kapsar; onayı kimin vereceği yetki kararıdır.

Karar 5'in gücü § 186.0 ölçümüne ve `PlatformAdmin` anahtarlarının veriliş biçimine
bağlıdır: `PlatformAdmin` kiracı içi işlemler için de verilir (`SchedulingEndpoints.cs:54`,
`ApiKeyScopeEnforcementTests.PlatformAdmin_scoped_key_writes_a_schedule`); onu taşıyan
kiracı operatörü Karar 5'i de geçer. Üretim dokümanı bunu yazar.

- Köken store ve `Enabled` doğruysa uç `CrossTenantAuthority.CheckAsync(httpContext,
  targetTenantId: null)` çağırır.
- 🚨 **Kendi kiracısını geçme:** `CheckAsync` hedef = çağıran ise hemen geçer
  (`CrossTenantAuthority.cs:59-63`); kontrol sessizce boşa çıkar. `null` "bütün kurulum".
- 🚨 **Kiracılık koşulunu uçta kontrol et:** anahtar dalı kiracılığa bakmaz (`:65-72`);
  koşulsuz çağrı tek kiracılı host'ta `PlatformAdmin`'siz anahtarı reddeder.
- `RequireApiKeyScope(SecurityAdmin)` kalır; anahtar `SecurityAdmin` + `AgentsRead` +
  `PlatformAdmin` taşır (§ 186.3). Anonim yerel dal (`:79-82`) K-853/K-854
  ile aynıdır. Kod kökenli skill (Açık Soru 1 A) platform yetkisi istemez.

**Karar 7 (kullanıcı kararı, 2026-09-23):** yeni üretim profili anahtarı yok;
`TraconProductionRisk` değişmez (K-773).

## 186.5 — Depolama, migration ve bellek içi store

- **Sütun** `skill_script_grants.content_hash`, `NULL` olabilir: PostgreSQL `text`,
  SqlServer `nvarchar(64)`, Sqlite `TEXT`; indeks yok. Bugün yok: PostgreSQL
  `0004_skill_scripts.sql:15-26`, SqlServer `0001_initial.sql:463-477`, Sqlite
  `0001_initial.sql:359-372`.
- **Migration** sağlayıcı başına bir dosya, sıradaki boş numara (K-178; bugün son:
  PostgreSQL `0052`, SqlServer `0040`, Sqlite `0039`). Eski satırlar `NULL` kalır.
- **Sorgular:** `SelectSkillScriptGrants`, `SelectActiveSkillScriptGrant`,
  `UpsertSkillScriptGrant` sütunu okur/yazar. `grantColumns`: `SqlServerQueries.cs:205-261`,
  `SqliteQueries.cs:181-225`; PostgreSQL elle (`PostgresQueries.cs:121-165`, upsert
  `:142-154`). `ReadGrant` sıra 8'i okur, `GrantAsync` parametre ekler
  (`SqlSkillScriptGrantStore.cs:65-114`).
- 🚨 **Upsert güncelleme dalı hash'i yazmalıdır.** Benzersizlik `(tenant, skill,
  script)`. `ON CONFLICT … DO UPDATE` ve SqlServer `UPDATE` bugün yalnız `granted_by`,
  `granted_at`, `expires_at`, `revoked_at` yazar. Unutulursa yeniden grant ESKİ hash'i
  tutar; derleme ve mevcut testler görmez. Kapı: `Granting_again_replaces_the_content_hash`.
- SQL metin baseline'ları (`tests/Tracon.Sql.Shared.UnitTests/Baselines/sql-text-baseline.*.txt`,
  her birinde 4 sorgu) güncellenir.
- **Karar 12 (kullanıcı kararı, 2026-09-23):** bellek içi revoke K-092'ye uyar. Bugün
  satırı siler (`InMemorySkillScriptGrantStore.cs:96`, `TryRemove`); sözleşme
  "including revoked" der (`ISkillScriptGrantStore.cs:17`). Yeni: `RevokedAt` yazılır,
  satır `ListAsync`'te kalır. Dönüş SQL gibi: `revoked_at IS NULL` satır varsa `true`
  (süresi dolmuş dahil). Güncelleme yarışsız (`TryUpdate` döngüsü / `AddOrUpdate`).
  `GrantAsync` zaten `RevokedAt = null` yazar (`:73-78`).
- **Sözleşme paketi** (yayımlanan): `Content_hash_round_trips` ·
  `Null_content_hash_round_trips` · `Granting_again_replaces_the_content_hash` ·
  `Revoked_grant_stays_listed`. `Skill_wide_grant_covers_every_script` doğru kalır
  (pin runner'dadır). 🚨 `ContentHash` saklamayan özel store yeni testlerde düşer ve
  stored script'i reddeder (güvenli yön); sürüm notu yazar.

## 186.6 — HTTP yanıtı, üretilen istemciler ve arayüz

- **Yüzey** (Açık Soru 3): `AgentSkillScriptDefinition.ContentHash` ve
  `AgentSkillDefinition.ScriptSetHash` hesaplanan, salt okunur; saklanmaz, record
  eşitliğine girmez. `GET /api/skills[/{name}]` yanıtı onları kendiliğinden taşır.
- 🚨 **OpenAPI:** `AgentSkillRequest.Scripts` aynı tipi GİRDİ olarak kullanır. Özellik
  `required` çıkarsa TS istemcisi istekte `contentHash` ister ve `emptyScript()`
  (`model.ts:32`) derlenmez. Adım 2 şemayı ölçer: `readOnly` ve istekte zorunsuz
  olmalı; değilse Açık Soru 3 B.
- Yeniden üretilir: `docs/openapi/tracon.json` (`OpenApiSnapshotTests`),
  `TraconApiClient.g.cs`, `TraconClientJsonContext.g.cs`, `packages/tracon-client/src/schema.ts`.
- **Arayüz** (`screens/skills/script-grants.tsx`, 198 satır): form hash'i
  `GET /api/skills/{name}`'den (listeden değil) okur ve gönderir (`:46-47` bugün
  göndermez); `409`'da yeniden okumayı söyler; Açık Soru 5 A ise bayat grant'ı işaretler.
  Metinler `locales/{en,tr}/agents.ts` (K-228); `skills.scriptNotice` (`en/agents.ts:153`)
  pin'i söyler.

## 186.7 — Tehdit modeli ve doküman düzeltmeleri

**Karar 6 (kullanıcı kararı, 2026-09-23):** B7 "Script execution gates" olur. R8
eklenir: script Tracon'un OS kimliğiyle çalışır; `AllowStoredScripts` açık çok kiracılı
host'ta grant sahibi başka kiracıların `secret`'larına ulaşabilir. Yüzeyler ve kapı
**birlikte** değişir:

| Dosya | Değişiklik |
|---|---|
| `docs/MIMARI-TEHDIT-MODELI.md` | V7 (`:65`) "sandbox içinde" düşer · B7 (`:83`) = "Script execution gates"; durdurduğu: grant'sız, grant'tan sonra değişmiş veya izin listesi dışı script'in başlaması · V7×A6 (`:118`) `🔴 R2 · R8` · `### R8 — …` · V1-V3×A3 notu: grant'lı çok kiracılı host'ta B2 script sürecinde geçmez · "Ölçüldü" yeni commit |
| `reference/threat-model.md` | `:55`, `:71` aynı ad · "Accepted risks"e R8: `:::caution`, başlığında "operating-system identity" |
| `getting-started/security.md` | `:322` ad ve açıklama |
| `reference/security-policy.md` · `SECURITY.md` | `:35`, `:44`: "Script execution gates (grant, content pin, interpreter allowlist, audit)" (K-776 emsali) |
| `docs-site/scripts/check-content.mjs` | Karar 6'nın kalıcı kapısı, üç ek (regex aşağıda). (a) `securityPolicyFacts` (`:143-147`) kapsam satırını iki dosyada sabitler. (b) Yasak ifadeler her sayfada (`allContent`, üretilen `api/` dahil) ve `SECURITY.md`'de hata. (c) `threat-model.md` "Accepted risks" R8 ifadesini taşır. Sınır tablosu eşlemesi (`:160-197`) aynı kalır. 🚨 `CHANGELOG.md` eski adı alıntılamaz: `reference/changelog.md` ondan üretilir (`build-changelog.mjs`) ve (b) onu da tarar |
| `concepts/tools.md` | `:293-326`: kapı 2 "valid grant pinned to this content" · `:305` "clean environment" → "allow-listed environment, same OS user" · grant paragrafı pin, platform yetkisi, kapsam kümesi · `:319` uyarısı aynı OS kimliğini ve R8'i adlandırır |
| `guides/production.md` | `:758-760` "treat every script as deployed code" der; stored script'i API ile `AgentsAdmin` yazar. Yeni: çok kiracılıda platform yetkisi, `PlatformAdmin` anahtarı bunu da taşır, `SkillRoots` salt okunur (Karar 10), kapsam kümesi |
| `ui.md` | `:272-274` "cannot write one" yanlış — konsol script YAZAR (`skill-editor.tsx:305`) · `:302` pin ve bayat işareti |
| `capabilities.md` | `:98` "tenant grant" → "content-pinned tenant grant; platform authority for stored scripts in a multi-tenant host" |
| `reference/compatibility.md` | `:298`, `:301`: grant akışı `AgentsRead` + `SecurityAdmin`, çok kiracılıda + `PlatformAdmin` |
| `docs/MIMARI-GUVENLIK.md` | `:236-251` "kiracı izni" → "içeriğe pinli kiracı izni" |
| `CHANGELOG.md` | `Security`: hash'siz grant stored script'i yetkilendirmez, yeniden grant gerekir, file script etkilenmez, özel store `ContentHash` saklamalı, çok kiracılıda platform yetkisi · `Changed`: `SandboxedSkillScriptRunner.RunStoredScriptAsync` `(AgentSkillDefinition skill, string scriptName, …)` alır; `(string skillName, AgentSkillScriptDefinition script, …)` kalktı |

```js
['script execution gates scope', /Script execution gates/i]                                  // (a)
/script sandboxing|escaping its sandbox|inside its sandbox|isolated operating-system process/i  // (b)
/operating-system identity/i                                                                  // (c)
```

**XML** (üretilen `api/` buradan gelir):

- `SandboxedSkillScriptRunner` (`:12-43`) — **Karar 9 (kullanıcı kararı,
  2026-09-23):** ad kalır; XML der: OS sandbox yok, süreç aynı OS kullanıcısıyla
  çalışır, yalıtım host'un işi (K-086). Yeniden adlandırma reddedildi: doğrudan kuran
  tüketicide kaynak kırılır, tek kazanç ad doğruluğudur.
- `AllowStoredScripts` (`TraconOptions.cs:323-326`, "Three gates are required …"):
  pin ve platform yetkisi. `SkillRoots` (`:332-335`, "The person who writes script
  content deploys the application"): salt okunur şartı.
- `EnvironmentAllowList` (`:352-355`): "never reach the process" düşer.
  `SkillScriptProcessRunner.cs:78-101` `UserName` atamaz; `Environment.Clear()` yalnız
  mirası temizler; `HOME` varsayılanda (`:356`). Yeni metin: miras alınmaz; süreç
  kullanıcının okuyabildiğini okur. § 186.8 madde 2 kesinleştirir.
- `SkillScriptGrant` (`:3-16`) + `ContentHash`; `AgentSkillDefinition.cs:86-94`;
  `AgentContracts.cs:147-156` (`Scripts`), `:178-197` (`SkillScriptGrantRequest` +
  `<example>`); `ITraconBuilder.AddSkill` (`:226`, Açık Soru 1'e göre).
- **Karar 8 (kullanıcı kararı, 2026-09-23):** skill denetimi best-effort kalır.
  `AuditingAgentSkillStore.cs:28-32` gerekçeyi "versioned, reversible" öncülüne
  dayandırır (`:31`); K-082 sürüm tutmaz. Yeni gerekçe: kodu çalıştıran izin
  (`script.grant`, içeriğe pinli) fail-closed'dır; içerik yazımı tek başına kod çalıştırmaz.

🚨 **Bütçe:** `MIMARI-TEHDIT-MODELI.md` 14.106 bayt, sınır 16.600
(`scripts/dokuman-bakim.py:132`). R8 ~2,4 KB'ye sığar; taşarsa ayrıntı
`MIMARI-GUVENLIK.md`'ye taşınır, silinmez.

## 186.8 — Sınıf taraması ve ölçümler

Sonuçlar "Plandan Sapmalar"a veya kusur kaydına yazılır.

1. **Karar 11 (kullanıcı kararı, 2026-09-23):** `SecurityAdmin`'in onay kuralı
   (`POST /api/approvals/rules`, `GovernanceEndpoints.cs:606`, `:660-661`), `AgentsAdmin`
   MCP `Endpoint`'ini (`McpServerDefinition.cs:64`) değiştirince (`PUT /api/mcp-servers/{name}`,
   `:230`, `:285-286`) yeni hedefin tool'unu onaylıyor mu? Kural `AgentName`, `ToolName`,
   `ArgumentsHash`, `ArgumentConditions` taşır (`ToolApprovalRule.cs:37-53`); kaynağa bağ yok.
   - **"Doğrulandı":** `McpApprovalRuleSourceTests` kuralın yeni endpoint'in bir tool'unu
     otomatik onayladığını gösterir.
   - **İzinli sonuçlar, yalnız bunlar:** (i) doğrulandı + bu fazda düzeltildi · (ii)
     doğrulandı + `docs/ADAYLAR.md`'de kusur kaydı · (iii) doğrulanmadı. Test her
     sonuçta regresyon testi olarak kalır.
   - `RequiresApproval` (`:127`) aynı `AgentsAdmin` ucunda yazılır. Onayı doğrudan
     kapatabiliyorsa bu "ek yükseltme yok" sayılmaz; `ADAYLAR.md`'ye ayrı kusur satırı olur.
2. **Linux `/proc/<ppid>/environ`:** script ebeveyn ortamını okuyabiliyor mu? Linux
   container'ında ölç. Sonuç `EnvironmentAllowList` XML'ini ve R8'i kesinleştirir;
   ölçülemezse "ölçülmedi" yazılır, metin yalnız aynı OS kullanıcısını söyler.

## 186.9 — Açılacak K-* kararları (numara rezerve edilmez)

Numarayı `faz-tamamlama` alır; K-855'ten itibaren kategori etiketi zorunlu, yalnız
dört kategori K alır.

| Karar | Kategori | Ayrıntı |
|---|---|---|
| Grant içeriği pinler (Karar 1-4, 10) | `güvenlik` · `kalıcı-veri` · `public-api` | §§ 186.1-186.3, 186.5. Ek red: script taşıyan skill kaydına `SecurityAdmin` şartı |
| Çok kiracılıda stored-script grant'ı platform yetkisi ister (Karar 5, 7) | `güvenlik` | § 186.4. Ek red: `AllowStoredScripts`'i açılışta reddetmek · yalnız R8 |
| B7 "Script execution gates", R8, kapsam satırı (Karar 6, 9) | `public-api` (K-776 emsali) · `güvenlik` | § 186.7. Ek red: adı koruyup tanımı daraltmak · yalnız R8 |

Anahtar basma düzeltmesi bu fazın K'sı değildir (§ 186.0). K açılmayan kararlar ("Bu
Fazda Verilen Kararlar"): Karar 12 · Karar 8 yorumu · XML/site metni · red metinleri ·
bayat işareti (Açık Soru 5).

## 186.10 — Uygulama sırası

1. § 186.0'ı koş. Pin senaryolarının fonksiyonel testlerini yaz; kırmızı olmalılar
   (#1-#7). İlk ölçüm: `FakeModelProvider.CallsTool("run_skill_script", …)`
   (`src/Tracon.Testing/FakeModelProvider.cs:166`) ve onay kuralıyla tam HTTP yolu
   kurulabiliyor mu? Bugün stored script'i HTTP → model → MAF → runner yolunda koşan
   test yok (grep). Kurulamazsa delegate gerçek DI container'dan çözülür; seçim
   "Plandan Sapmalar"a.
2. Açık Soru 1 ve 3'ü sor. Abstractions: hash sınıfı, özellikler, `ContentHash`,
   birim testleri, alan kapsamı kapısı. OpenAPI şemasını ölç (§ 186.6).
3. Depolama: migration, sorgular, baseline, bellek içi store, sözleşme testleri. Oku:
   `grep -n -A12 "ANKRAJLANMALIDIR" docs/hafiza/sql-migration.md` ·
   `grep -n "SqlTextSnapshotTests\|SONA eklenir" docs/hafiza/sql-paylasilan-sorgu-uretimi.md`.
4. Runner, `SkillScriptSupport`, `TraconSkillsSource`. 🚨
   `tests/Tracon.Core.UnitTests/Architecture/ambient-write-baseline.txt:20`
   `RunStoredScriptAsync`'i adıyla anar; ad kalırsa satır geçerlidir.
5. Grant ucu: kökenli arama (`Enabled` süzmez), hash, `400`/`404`/`409`, platform yetkisi.
6. OpenAPI, iki istemci, arayüz, `en`/`tr`, E2E. Oku:
   `sed -n '60,75p' docs/hafiza/nswag-istemci-uretimi.md` (dört yüzey, sıra) ·
   `grep -n -A9 "ALAN kadar bayat" docs/hafiza/nswag-istemci-uretimi.md` ·
   `grep -n -A3 "ONEKI\|dili SABITLEMELIDIR" docs/hafiza/frontend-yerellestirme.md`.
7. XML, tehdit modeli, site, `check-content.mjs`, `SECURITY.md`, `CHANGELOG.md`. Oku:
   `grep -n -A12 "Internal tipin XML" docs/hafiza/dokumantasyon.md`.
8. § 186.8. Oku: `grep -n "ToolArgumentConditionFingerprint\|sarmalama sirasi" docs/hafiza/tool-onay-ve-yetkilendirme.md`.
9. Manuel case'ler, örnek uygulama, kapanış kapısı.

---

## Planlanan Public API

> Taslak imzalardır; gerçekleşen imzalar kapanışta yazılır. Aşağıdaki **her** tip
> için: bugün eklemek/değiştirmek ucuz (pre-1.0, `Shipped` boş) / GA'dan sonra kırıcı.

```csharp
// Tracon.Abstractions
public sealed record SkillScriptGrant {
    public string? ContentHash { get; init; }   // YENİ: script hash'i veya küme parmak izi; null = yalnız file script
}
public sealed record AgentSkillScriptDefinition {
    public string ContentHash { get; }          // YENİ, hesaplanan: SHA-256, 64 büyük harf karakter
}
public sealed record AgentSkillDefinition {
    public string ScriptSetHash { get; }        // YENİ, hesaplanan: küme parmak izi
}
// ISkillScriptGrantStore — imza DEĞİŞMEZ; XML: ContentHash saklanır, revoke edilen satır listelenir.

// Tracon.AspNetCore
public sealed record SkillScriptGrantRequest {
    public string? ExpectedContentHash { get; init; }   // YENİ: stored skill için zorunlu (400); uyuşmazlık 409
}

// Tracon.Core — İMZA DEĞİŞİR (eski: string skillName, AgentSkillScriptDefinition script, …)
public sealed class SandboxedSkillScriptRunner {
    public Task<object?> RunStoredScriptAsync(AgentSkillDefinition skill, string scriptName,
        JsonElement? arguments, CancellationToken cancellationToken);
}

// Tracon.Testing.Contracts.Xunit — SkillScriptGrantContract'a yeni [Fact]'ler
public Task Content_hash_round_trips();
public Task Null_content_hash_round_trips();
public Task Granting_again_replaces_the_content_hash();
public Task Revoked_grant_stays_listed();
```

Yalnız XML değişenler: `TraconSkillScriptOptions`, `ITraconBuilder.AddSkill` (Açık Soru
1 B'de davranış da). `SkillScriptGrantContract`'ın yeni testleri özel store yazan
tüketicide düşebilir.

**Beklenen uyumluluk tanısı (Faz 187 kapısı).** Eski `RunStoredScriptAsync(string,
AgentSkillScriptDefinition, JsonElement?, CancellationToken)` preview.1 ve preview.2'de
yayımlandı (`git show v1.0.0-preview.2:src/Tracon.Core/PublicAPI.Unshipped.txt`, satır
528). Kaldırılması **`CP0002`** üretir. Ötekiler yalnız record özelliği ekler. Public
arayüze üye eklenmez; `CP0006` beklenmez. Faz 187 planı (`187-KIRICI-DEGISIKLIK-KAPISI.md:502`)
`CP0006` bekler; o beklenti buna göre düzeltilir. Karşılığı `CHANGELOG.md` `Changed` satırıdır.

### HTTP `endpoint`'leri

| Metot | Yol | Rol / kapsam | Ne değişir |
|---|---|---|---|
| `POST` | `/api/skill-script-grants` | Admin · `SecurityAdmin`; çok kiracılı + stored: + platform yetkisi | `expectedContentHash`; `400`/`403`/`404`/`409` |
| `GET` | `/api/skill-script-grants` | Reader · `SecurityAdmin` | `contentHash`; bellek içinde revoke edilen satır görünür |
| `GET` | `/api/skills`, `/api/skills/{name}` | Reader · `AgentsRead` | `contentHash`, `scriptSetHash`; Açık Soru 1 A: `{name}` katalogdan çözer, `origin` taşır |
| `PUT` | `/api/skills/{name}` | Admin · `AgentsAdmin` | yalnız açıklama |
| `DELETE` | `/api/skill-script-grants/{skillName}` | Admin · `SecurityAdmin` | değişmez |

### Arayüz payı

`script-grants.tsx` ve iki locale dosyası. Bugün `index-BKpu3tFH.js.br` 162.542 bayt
(Brotli, 2026-09-20). Bütçe 250 KB (`.agents/ortak/kapilar.md:57`). Pay **ölçülmeli**,
kapanışta yazılır.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Skills/{SkillScriptGrant.cs, AgentSkillDefinition.cs, ISkillScriptGrantStore.cs,
    SkillScriptHashing.cs (YENİ, internal)}, PublicAPI.Unshipped.txt
src/Tracon.Core/Skills/Scripts/{SandboxedSkillScriptRunner.cs, SkillScriptSupport.cs}
src/Tracon.Core/Skills/{TraconSkillsSource.cs, AgentSkillCatalog.cs (kökenli arama, Enabled süzmez)}
src/Tracon.Core/{Storage/InMemorySkillScriptGrantStore.cs, Audit/AuditingAgentSkillStore.cs (yorum),
    TraconOptions.cs (XML), ITraconBuilder.cs (XML), PublicAPI.Unshipped.txt}
src/Tracon.AspNetCore/{Endpoints/SkillScriptGrantEndpoints.cs, Endpoints/SkillEndpoints.cs,
    Contracts/AgentContracts.cs, PublicAPI.Unshipped.txt}
src/Tracon.Sql.Shared/Stores/SqlSkillScriptGrantStore.cs
src/Tracon.{PostgreSql/Internal/PostgresQueries.cs, SqlServer/Internal/SqlServerQueries.cs, Sqlite/Internal/SqliteQueries.cs}
src/Tracon.{PostgreSql,SqlServer,Sqlite}/Migrations/<sıradaki>_skill_script_grant_content_hash.sql
scripts/applied-migrations.json                                   (üç ankraj)
src/Tracon.Testing.Contracts.Xunit/{Contracts/SkillScriptGrantContract.cs, PublicAPI.Unshipped.txt}
src/Tracon.Client/Generated/{TraconApiClient.g.cs, TraconClientJsonContext.g.cs}   (üretilen)
packages/tracon-client/src/schema.ts · docs/openapi/tracon.json                  (üretilen)
src/Tracon.UI/frontend/src/{screens/skills/script-grants.tsx, lib/server-types.ts (gerekirse), locales/{en,tr}/agents.ts}
tests/Tracon.Core.UnitTests/Skills/{SkillScriptHashTests.cs (YENİ), SandboxedSkillScriptRunnerTests.cs}
tests/Tracon.AspNetCore.FunctionalTests/{SkillScriptContentPinTests.cs (YENİ), SkillScriptGrantTests.cs,
    McpApprovalRuleSourceTests.cs (YENİ, Karar 11)}
tests/Tracon.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/…ContentHashMigrationTests.cs (YENİ, JobHandlerKeyMigrationTests emsali)
tests/Tracon.Sql.Shared.UnitTests/Baselines/sql-text-baseline.{postgres,sqlserver,sqlite}.txt
tests/Tracon.Ui.E2ETests/Ui/SkillTests.cs
docs/{MIMARI-TEHDIT-MODELI.md, MIMARI-GUVENLIK.md, manuel-test/14-SKILL-VE-SCRIPT.md}
docs-site/scripts/check-content.mjs                               (Karar 6 kapısı)
docs-site/src/content/docs/{concepts/tools.md, reference/{threat-model,security-policy,compatibility}.md,
    getting-started/security.md, guides/production.md, ui.md, capabilities.md}
SECURITY.md · CHANGELOG.md
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

Kısaltma: **P** `SkillScriptContentPinTests` · **G** `SkillScriptGrantTests` ·
**R** `SandboxedSkillScriptRunnerTests` · **S** `SkillScriptGrantContract` (dört koşum:
`tests/Tracon.Core.UnitTests/Contracts/InMemoryStoreContractTests.cs` +
`tests/Tracon.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/Contracts/*StoreContractTests.cs`) ·
**H** `SkillScriptHashTests`.

| # | Ne bozulabilir | Seviye | Test |
|---|---|---|---|
| 1 | v1'e grant; `AgentsAdmin` `PUT` ile v2 yazar; v2 çalışır. Runner ile uç farklı hash hesaplar (`GET` → `POST` → çalışır) | Fonksiyonel | P |
| 2 | Geniş grant'tan sonra eklenen / silinen / değişen script çalışır (Karar 2) | Fonksiyonel | P `Skill_wide_grant_refuses_every_script_after_a_script_is_added` + silme/değiştirme |
| 3 | Var olmayan ada önceden grant, sonra yaratılan stored skill'i yetkilendirir | Fonksiyonel | P |
| 4 | Aynı adlı stored skill grant'lı file skill'i gölgeler ve çalışır | Fonksiyonel | P |
| 5 | Farklı içerikle yeniden yaratılan skill eski grant'la çalışır | Fonksiyonel | P |
| 6 | Hatırlanan `run_skill_script` kuralı (`ToolApprovalResolver.cs:212-214`, argüman hash'ine bağlı) içerik değişince çalıştırır; MAF onayı delegate'ten önce gelir | Fonksiyonel | P |
| 7 | TOCTOU: `GET` ile `POST` arasında v2 yazılır, grant v2'yi pinler | Fonksiyonel | P (`409`) |
| 8 | Stored skill, beklenen hash yok / biçimsiz / aşırı uzun → grant yazılır | Fonksiyonel | G (`400`, liste boş, kayıt yok) |
| 9 | `scriptName` stored skill'de yok → grant yazılır | Fonksiyonel | G (`404`) |
| 10 | Çok kiracılı: `SecurityAdmin` + `AgentsRead` anahtarı stored grant verir; `CheckAsync`'e kendi kiracısı geçer, kontrol boşa çıkar | Fonksiyonel (kiracı) | P (`403`, mutasyon yok) |
| 11 | Çok kiracılı: `SecurityAdmin` + `AgentsRead` + `PlatformAdmin` anahtarı · statik token · policy'li claims reddedilir; policy kayıtsız claims kabul edilir | Fonksiyonel | P (dört kimlik) |
| 12 | Tek kiracılı: `SecurityAdmin` + `AgentsRead` anahtarı stored grant veremez | Fonksiyonel | P (`201`) |
| 13 | Hash'siz eski grant stored script'i yetkilendirir (Karar 3) | Birim + Sözleşme (`null` gidiş-dönüş) | R, S |
| 14 | File yolu hash ister veya hash'li grant'ı reddeder (Karar 10) | Birim | R |
| 15 | Red sebebi ayrı değil; span'e / `script.denied`'a girmez | Birim + Fonksiyonel | R, P |
| 16 | Runner'a skill'e ait olmayan script adı geçer | Birim | R |
| 17 | Yeniden grant ESKİ hash'i tutar (upsert güncelleme dalı) | Sözleşme | S `Granting_again_replaces_the_content_hash` |
| 18 | Sağlayıcı `ContentHash`'i okumaz/yazmaz | Sözleşme | S `Content_hash_round_trips` |
| 19 | Bellek içi revoke satırı siler (K-092) | Sözleşme | S `Revoked_grant_stays_listed` |
| 20 | Bellek içi revoke ile yeniden grant yarışır: iki etkin satır / kayıp güncelleme | Sözleşme (eşzamanlı) | S |
| 21 | Migration eski satırı bozar; eski satır `null` okunmaz | Entegrasyon (üç) | `…ContentHashMigrationTests` |
| 22 | SQL metni baseline'dan kayar | Birim | SQL baseline testleri |
| 23 | Çalıştırmayı etkileyen yeni alan hash'e girmez | Birim | H `Every_property_is_hashed_or_explicitly_excluded` |
| 24 | Uzunluk öneki çakışması · küme sırası izi değiştirir · tekil = küme · `null`/boş şema farklı | Birim | H |
| 25 | Kod skill'i (`AddSkill` + `Scripts`) pinsiz çalışır veya sessizce kırılır | Fonksiyonel | P (Açık Soru 1'e göre) |
| 26 | Kod skill'i stored skill'i gölgeler: stored hash onaylanır, uç kod hash'iyle karşılaştırır → kalıcı `409` veya çalışmayan içeriğe grant | Fonksiyonel | P — yanıt Açık Soru 1'in § 186.3 satırı |
| 27 | Devre dışı stored skill'e grant "bulunamadı" sayılır (`201`, `null`, platform kontrolü yok). Beklenen: `400` hash'siz, çok kiracılıda `403` | Fonksiyonel (kiracı) | G |
| 28 | `script.grant` `after` `contentHash` taşımaz | Fonksiyonel (denetim) | P |
| 29 | İptal: denetim kaydından sonra iptal, grant yarım kalır | Birim | `AuditingSkillScriptGrantStoreTests` |
| 30 | Alt sistem: uçta skill okuması atar, grant yine yazılır | Fonksiyonel (atan store) | G |
| 31 | Alt sistem: denetim yazılamaz, grant yine yazılır (mevcut) | Birim (mevcut) | `A_grant_is_not_persisted_when_its_audit_entry_cannot_be_written` korunur |
| 32 | Başka kiracı: B'nin aynı içerikli skill'i A'nın grant'ıyla çalışır | Sözleşme | mevcut `Grant_does_not_leak_across_tenants` + `TenantIsolationContract` |
| 33 | Boş girdi: sıfır script'li skill'e geniş grant, sonra eklenen script çalışır | Birim + Fonksiyonel | H, #2 |
| 34 | OpenAPI `contentHash`'i istekte zorunlu kılar; TS istemcisi kırılır | Fonksiyonel + paket | `OpenApiSnapshotTests`, arayüz `tsc` |
| 35 | Form hash göndermez (`400`) veya bayat grant'ı etkin gösterir | E2E (Playwright) | `SkillTests` |
| 36 | `en`/`tr` anahtarı eksik | derleme (`tsc`) + E2E | `LocalizationTests` |
| 37 | `security.md` ile `threat-model.md` sınır kümesi ayrışır | Kapı | `check-content.mjs` (mevcut `:160-197`) |
| 38 | Yasak sandbox ifadesi sayfaya, `api/`'ye veya `SECURITY.md`'ye döner; kapsam satırı ayrışır; R8 "Accepted risks"ten düşer | Kapı | `check-content.mjs` üç yeni kontrol; her biri bir kez geçici değişiklikle kırmızı gösterilir |
| 39 | Tehdit modeli bütçeyi aşar | Kapı | `dokuman-bakim.py --denetle` |
| 40 | MCP endpoint'i değişince `SecurityAdmin` kuralı yeni hedefi onaylar (Karar 11) | Fonksiyonel (ölçüm; her sonuçta kalır) | `McpApprovalRuleSourceTests` |
| 41 | Claims tabanlı kiracı Admin'i `PlatformAdmin` anahtarı basar, Karar 5'i aşar | Fonksiyonel — kusur-giderme'de kuruldu (`CrossTenantAuthorityTests`) | § 186.0 varlığını doğrular |
| 42 | Özel store `ContentHash`'i saklamaz | Paket + manuel | yeni S testleri tüketicide düşer; CHANGELOG |

Beş soru: iptal (#29) · eşzamanlılık (#7, #20) · boş/aşırı girdi (#8, #33) · başka
kiracı (#10-#12, #27, #32) · alt sistem hatası (#30, #31).

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/14-SKILL-VE-SCRIPT.md`](../../manuel-test/14-SKILL-VE-SCRIPT.md)
> içine eklenir; kimlik sıradaki boş numaradan (bugün `MT-SKILL-064…069` boş, `070-071`
> dolu). Ön koşul MT-SKILL-041'in geçici kod değişikliği (`UseSkillScripts` +
> `AllowStoredScripts` + `Interpreters["sh"]`). 👤 model çağırır, küçük OpenAI ücreti.
> "DK n" = Doğrulama komutları adım n; kapanışta case metnine açılır.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `scriptli-skill` v1 | DK 1-2; Playground'da çalıştır 👤 | `201`, `contentHash` = okunan; yanıtta `merhaba-tracon` |
| 2 | Case 1 | DK 4 `PUT`; aynı prompt, yeni sohbet 👤 | Çalışmaz; `script.denied` `after` "content changed since the grant" |
| 3 | Case 2 | DK 4 eski hash ile `POST` | `409`; grant listesi değişmez |
| 4 | Stored skill | DK 2 ilk istek (hash yok) | `400` |
| 5 | Güncel küme izine geniş grant | `PUT` ile ikinci script; yenisini çalıştır 👤 | İkisi de reddedilir |
| 6 | Faz öncesi hash'siz grant (PostgreSQL) | Yükselt; stored script'i çalıştır 👤 | "pinlemiyor" reddi; `content_hash IS NULL` |
| 7 | `Tracon:Tenancy:Enabled=true`; anahtar `SecurityAdmin` + `AgentsRead` | `GET` hash; stored skill'e `POST` | `200`; `403`, liste boş |
| 8 | Case 7; anahtar + `PlatformAdmin` | Aynı iki istek | `200`; `201` |
| 9 | Tek kiracılı; `SecurityAdmin` + `AgentsRead` | Hash oku; grant | `201` |
| 10 | Bellek içi mod | Grant, `DELETE`, `GET /api/skill-script-grants` | Satır `revokedAt` dolu listede |
| 11 | Case 2 sonrası | Arayüz → Skills → grant ekranı 👤 | "Bayat" işaretli (Açık Soru 5 A); formdan grant `201` |
| 12 | `npm run build` | `threat-model.md`, `security.md`, `public/llms-full.txt` | "Script execution gates" iki sayfada; "sandbox" sınırı hiçbirinde yok |

---

## Açık Sorular

> Planı bloklamayan, uygulamada karara bağlanan sorular. 1 ve 3, § 186.10 adım 2'den
> önce bakımcıya sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `AddSkill` + `Scripts` nasıl işlensin, beklenen hash nereden okunsun? Bugün kod skill'i stored yoldan çalışır (`AgentSkillCatalog.cs:103-106`); `GET` yalnız store'u okur (`SkillEndpoints.cs:74-88`); kod skill'inin hash'i okunamaz; aynı ad stored skill'i gölgeler | **A:** stored yol her kökende pinler. Hash: `GET /api/skills/{name}` katalogdan çözer (önce kod) ve `origin` (`code`/`stored`) taşır; liste store'da kalır. Gölgelemede gözden geçiren kişi çalışacak içeriği görür. Bedel: yalnız kodda olan ad artık `404` değil, yanıta alan eklenir · **B:** `AddSkill` `Scripts`'i açılışta reddeder (kod tarafı yol `SkillRoots`). Hash: `GET` değişmez. Gölgelemede `409`, gölgelemeyi adlandırır. Bedel: davranışsal kırıcı, ayrı K · **C:** kod kökenli script pinlenmez (runner'a köken bayrağı). Hash: `GET` değişmez; kod kökenli ad hash istemez. Gölgelemede stored içerik onaylanır, kod içeriği pinsiz çalışır | **A.** Runner kuralı tek kalır; gözden geçiren çalışacak içeriği görür; gölgeleme yanlış içeriğe grant üretmez. İkinci seçenek B. C gözden geçirilmemiş içeriği çalıştırır |
| 2 | (Kaldırıldı — anahtar basma önkoşuldur, § 186.0) | — | — |
| 3 | Beklenen hash hangi tiple taşınır? | A: hesaplanan salt okunur özellikler · B: `GET /api/skills` için ayrı yanıt tipi · C: istemci hesaplar (TS'de ikinci uygulama) | **A**, şema `readOnly` ve istekte zorunsuz çıkarsa (§ 186.6); değilse B |
| 4 | Bayat dar grant, eşleşen geniş grant varken de reddetsin mi? | A: evet, dar kazanır · B: runner geniş grant'a düşer (store iki satır döner) | **A.** Fail-closed; yayımlanan store değişmez |
| 5 | Arayüz bayat grant'ı işaretlesin mi? | A: evet · B: hayır | **A.** Uç "etkin görünüp çalıştırmayan grant yanıltıcıdır" der (`SkillScriptGrantEndpoints.cs:43-44`) |
| 6 | Platform operatörü kiracı B adına grant'ı hangi kimlikle verir? | A: ölç ve belgele (B'ye bağlı `SecurityAdmin` + `AgentsRead` + `PlatformAdmin` anahtarı; statik token + kiracı başlığı destekleniyorsa o) · B: uca rota kiracısı | **A.** Yeni rota gerekmez; sonuç `guides/production.md`'ye |

---

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

## Riskler

Öteki riskler bir hata modu satırında veya 🚨 notunda önlemiyle durur.

| Risk | Önlem |
|------|-------|
| `AllowStoredScripts` kullanan her kurulumda stored script'ler yükseltmeden sonra durur (Karar 3) | `CHANGELOG.md` `Security` ve red sebebi "grant again" der; file script etkilenmez; özellik varsayılan kapalı |
| Mevcut runner testleri `GrantAllAsync()` ile hash'siz grant verir; stored yol testleri kırmızıya döner | Beklenen churn; hash'li grant ile güncellenir, silinmez |
| Stored script'i HTTP → model → runner yolunda koşan fonksiyonel test yoktur | § 186.10 adım 1'in ilk ölçümü; yedek yol "Plandan Sapmalar"a |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

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

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

`PublicAPI.Unshipped.txt` farkı (taban `d6153fe1`), plandaki taslakla aynı:

```text
Tracon.Abstractions
+ Tracon.AgentSkillDefinition.ScriptSetHash.get -> string!            (hesaplanan; kayıt eşitliğine girmez)
+ Tracon.AgentSkillDefinition.Origin.get -> Tracon.AgentDefinitionOrigin
+ Tracon.AgentSkillDefinition.Origin.init -> void                      (varsayılan Database; katalog kökeni kendisi atar)
+ Tracon.AgentSkillScriptDefinition.ContentHash.get -> string!         (hesaplanan)
+ Tracon.SkillScriptGrant.ContentHash.get -> string?
+ Tracon.SkillScriptGrant.ContentHash.init -> void
Tracon.Core
- Tracon.SandboxedSkillScriptRunner.RunStoredScriptAsync(string! skillName, Tracon.AgentSkillScriptDefinition! script, JsonElement? arguments, CancellationToken) -> Task<object?>!
+ Tracon.SandboxedSkillScriptRunner.RunStoredScriptAsync(Tracon.AgentSkillDefinition! skill, string! scriptName, JsonElement? arguments, CancellationToken) -> Task<object?>!
Tracon.AspNetCore
+ Tracon.SkillScriptGrantRequest.ExpectedContentHash.get -> string?
+ Tracon.SkillScriptGrantRequest.ExpectedContentHash.init -> void
Tracon.Testing.Contracts.Xunit (SkillScriptGrantContract, public virtual test metodları)
+ Content_hash_round_trips · Null_content_hash_round_trips · Granting_again_replaces_the_content_hash
+ Revoked_grant_stays_listed · Racing_grants_and_revocations_leave_one_record_for_the_key
```

İç (internal): `SkillScriptHashing` (Abstractions; `ScriptDomain`/`SetDomain`
alan ayırıcıları, uzunluk önekli SHA-256, 64 büyük harf onaltılık) ·
`AgentSkillCatalog.FindWithOriginAsync` (kod önce; `Enabled` süzmez; kökeni
kendisi atar) · `SandboxedSkillScriptRunner.RunFileScriptAsync(string skillName,
string scriptName, string fullPath, …)` (disk yolu, pinsiz) ·
`SkillScriptSupport.CreateStoredScriptDelegate(AgentSkillDefinition, string)`.

HTTP: `POST /api/skill-script-grants` gövdesi `expectedContentHash` alır; red
sırası `409` (script çalıştırma kapalı) → `400` (ad/süre) → skill yoksa pinsiz
`201` → `400 Content hash required` → `404 Script not found` → çok kiracılı +
stored + platform yetkisi yok `403` → `409 Content changed` (güncel hash
yanıtta YOK). `GET /api/skills/{name}` katalogdan çözer, `origin`,
`scriptSetHash`, `scripts[].contentHash` taşır. `GET /api/skill-script-grants`
satırı `contentHash` taşır; bellek içi iptal satırı `revokedAt` ile kalır.
Üretilen istemciler: `TraconApiClient.g.cs` (4 özellik), `schema.ts` (5 alan).

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

```text
src/Tracon.Abstractions/Skills/{SkillScriptHashing.cs (YENİ, internal), AgentSkillDefinition.cs, SkillScriptGrant.cs}, PublicAPI.Unshipped.txt
src/Tracon.Core/Skills/{AgentSkillCatalog.cs, CodeSkillRegistration.cs, TraconSkillsSource.cs,
    Scripts/SandboxedSkillScriptRunner.cs, Scripts/SkillScriptSupport.cs}
src/Tracon.Core/{Storage/InMemorySkillScriptGrantStore.cs, Audit/AuditingAgentSkillStore.cs, TraconOptions.cs,
    ITraconBuilder.cs, PublicAPI.Unshipped.txt}
src/Tracon.AspNetCore/{Endpoints/SkillScriptGrantEndpoints.cs, Endpoints/SkillEndpoints.cs, Contracts/AgentContracts.cs,
    PublicAPI.Unshipped.txt}
src/Tracon.Sql.Shared/Stores/SqlSkillScriptGrantStore.cs
src/Tracon.{PostgreSql/Internal/PostgresQueries.cs, SqlServer/Internal/SqlServerQueries.cs, Sqlite/Internal/SqliteQueries.cs}
src/Tracon.PostgreSql/Migrations/0053_skill_script_grant_content_hash.sql      (07c787d9)
src/Tracon.SqlServer/Migrations/0041_skill_script_grant_content_hash.sql       (07c787d9)
src/Tracon.Sqlite/Migrations/0040_skill_script_grant_content_hash.sql          (07c787d9)
scripts/applied-migrations.json                                                (üç ankraj)
src/Tracon.Testing.Contracts.Xunit/{Contracts/SkillScriptGrantContract.cs, PublicAPI.Unshipped.txt}
src/Tracon.Client/Generated/TraconApiClient.g.cs · packages/tracon-client/src/schema.ts · docs/openapi/tracon.json   (üretilen)
src/Tracon.UI/frontend/src/{screens/skills/{script-grants.tsx, skill-editor.tsx, model.ts, model.test.ts (YENİ)},
    lib/server-types.ts, locales/{en,tr}/agents.ts}
tests/Tracon.Core.UnitTests/{Skills/SkillScriptHashTests.cs (YENİ), Skills/SandboxedSkillScriptRunnerTests.cs,
    Audit/AuditingSkillScriptGrantStoreTests.cs}
tests/Tracon.AspNetCore.FunctionalTests/{SkillScriptContentPinTests.cs (YENİ), McpApprovalRuleSourceTests.cs (YENİ),
    SkillScriptGrantTests.cs, SkillCrudTests.cs, Tracon.AspNetCore.FunctionalTests.csproj (Tracon.Mcp referansı)}
tests/Tracon.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/SkillScriptGrantContentHashMigrationTests.cs (YENİ)
tests/Tracon.Sql.Shared.UnitTests/Baselines/sql-text-baseline.{postgres,sqlserver,sqlite}.txt
tests/Tracon.Ui.E2ETests/{Infrastructure/UiHost.cs, Ui/SkillTests.cs, Ui/ConfirmationTests.cs}
docs-site/scripts/check-content.mjs · docs-site/src/content/docs/{concepts/tools.md, reference/{threat-model,security-policy,
    compatibility,configuration}.md, getting-started/security.md, guides/production.md, ui.md, capabilities.md}
docs-site/public/{llms-full.txt, screenshots/*.png, screenshots/.ui-source.sha256}
SECURITY.md · CHANGELOG.md · README.md
docs/{MIMARI-TEHDIT-MODELI.md, MIMARI-GUVENLIK.md, KARARLAR.md, ADAYLAR.md, manuel-test/{00-INDEKS.md, 14-SKILL-VE-SCRIPT.md}}
docs/hafiza/{http-uc-guvenlik-ve-sozlesme, maf-api, secenek-baglama-ve-gizlilik, frontend-test-altyapisi,
    tool-onay-ve-yetkilendirme, dokumantasyon, test-yalitimi, test-yalitimi-vakalari}.md
```

Testler: `SkillScriptHashTests` 10 · `SandboxedSkillScriptRunnerTests` (R, üç TFM) ·
`SkillScriptContentPinTests` 15 · `SkillScriptGrantTests` 10 metot (16 koşum) · `SkillCrudTests` +2 ·
`McpApprovalRuleSourceTests` 2 · `AuditingSkillScriptGrantStoreTests` +1 ·
`SkillScriptGrantContract` +5 (dört koşum) · migration ×3 · `SkillTests` (E2E) +3 ·
`model.test.ts` 6.

### Tüketici yüzeyi envanteri (`tuketici-dokuman-senkronu` Adım 1)

1. **`docs-site/` — elle:** `concepts/tools.md` (kapı diyagramı, pin paragrafı,
   "Tracon does not sandbox" uyarısı) · `reference/threat-model.md` (varlık, B7
   satırı, R8 uyarısı) · `getting-started/security.md` (B7 satırı ve bağlantısı) ·
   `reference/security-policy.md` (kapsam tablosu + paragraf) ·
   `guides/production.md` (stored script, platform yetkisi, `SkillRoots`) ·
   `ui.md` (Skills + yönetişim düzeltmesi) · `capabilities.md` (Skill scripts
   satırı) · `reference/compatibility.md` (`PlatformAdmin`, `SecurityAdmin`) ·
   `reference/configuration.md` (allowlist notu). **Üretilen:** `api/` (XML),
   `http-api/` (OpenAPI açıklamaları), `reference/changelog` (`CHANGELOG.md`).
   **Ekran:** 38 PNG + `.ui-source.sha256`.
2. **Sevk edilen metin:** `///` — `AgentSkillDefinition.cs`, `SkillScriptGrant.cs`,
   `SandboxedSkillScriptRunner.cs`, `TraconOptions.cs` (`AllowStoredScripts`,
   `SkillRoots`, `EnvironmentAllowList`), `ITraconBuilder.cs` (`AddSkill`),
   `AgentContracts.cs` (`SkillScriptGrantRequest`), `AuditingAgentSkillStore.cs`
   (internal); uç açıklamaları (`SkillEndpoints`, `SkillScriptGrantEndpoints`) →
   `docs/openapi/tracon.json`; kök `README.md`; `SECURITY.md`; `CHANGELOG.md`.
3. **Yerel referans / agent haritası:** yalnız `capabilities.md`'nin Skill scripts
   satırı değişti → `Tracon.AgentMap.md`, `llms.txt`, `llms-full.txt` yeniden
   üretildi (828.837 B). Yeni giriş noktası yok, yeni `<example>` gerekmedi.

Kapılar: `npm run check` dördü yeşil (1060 sayfa; bağlantı 174.988, kırık 0; en
ağır sayfa `troubleshooting` 58.994 B, sayfa dokunulmadı).

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
