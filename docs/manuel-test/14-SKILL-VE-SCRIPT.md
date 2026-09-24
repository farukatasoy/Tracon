# 14 — Skill ve Script Çalıştırma (`SKILL`)

> **Alan kodu:** `SKILL` · **Faz:** 10, 11, 186
> **Kaynak:** `src/Tracon.Abstractions/Skills/` (tümü) ·
> `src/Tracon.Core/Skills/` (tümü: `AgentSkillCatalog`, `TraconSkillsSource`,
> `CodeSkillRegistration`, `TraconSkillScriptBuilderExtensions`,
> `Scripts/SandboxedSkillScriptRunner`, `Scripts/SkillScriptProcessRunner`,
> `Scripts/SkillScriptArgumentValidator`, `Scripts/SkillScriptConcurrencyLimiter`) ·
> `src/Tracon.Core/Storage/InMemoryAgentSkillStore.cs`,
> `InMemorySkillScriptGrantStore.cs` · `src/Tracon.Core/Audit/AuditingSkillScriptGrantStore.cs` ·
> `src/Tracon.Core/Compilation/AgentDefinitionCompiler.cs` (yalnız skill/script
> kablolaması — genel derleme akışı `02-CEKIRDEK-VE-KATALOG.md`'nin işi) ·
> `src/Tracon.Core/Compilation/AgentDefinitionValidator.cs` (yalnız `CheckSkillsAsync`/
> `CheckStructureAsync`) · `src/Tracon.AspNetCore/Endpoints/SkillEndpoints.cs`,
> `SkillScriptGrantEndpoints.cs` · `src/Tracon.AspNetCore/Contracts/AgentContracts.cs`
> (yalnız `AgentSkillRequest`/`SkillScriptGrantRequest`) ·
> `src/Tracon.PostgreSql/Migrations/0003_agent_skills.sql`,
> `0004_skill_scripts.sql`, `0053_skill_script_grant_content_hash.sql` ·
> `src/Tracon.Abstractions/Skills/SkillScriptHashing.cs` · `src/Tracon.UI/frontend/src/screens/skills.tsx`, `skills/script-grants.tsx` ·
> `src/Tracon.UI/frontend/src/screens/agent-editor.tsx` (yalnız skill seçici bölümü) ·
> `samples/Tracon.Api/Program.cs` (yalnız `tracon` değişkeni ve builder zinciri).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/14-SKILL-VE-SCRIPT.md`](../arsiv/manuel-test-kosum-2026-09/14-SKILL-VE-SCRIPT.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/14-SKILL-VE-SCRIPT.md

---

## Bu dosya neyi kanıtlar

Faz 10, çalışma anında bir agent'a yüklenen markdown tabanlı **skill**'leri
(`load_skill`/`read_skill_resource`, MAF onay zincirinden geçer) kanıtlar. Faz
11 bunun üzerine **script çalıştırmayı** ekler — K2 kuralının ("tool'lar yalnız
kodda tanımlanır") MCP'den (K-058) sonraki **ikinci bilinçli istisnası**: script
kodu, Tracon'in kendi makinesinde, ayrı bir OS sürecinde çalışır.

```mermaid
flowchart TD
    A["POST/PUT api/skills"] --> B["IAgentSkillStore<br/>tenant basina kayit"]
    B --> C["AgentSkillCatalog<br/>kod > store, MaxSkillsPerAgent"]
    C --> D["TraconSkillsSource<br/>AgentSkillDefinition -> AgentInlineSkill"]
    D --> E["AgentSkillsProvider<br/>onaylar HEP ACIK"]
    E --> F["Model: load_skill / read_skill_resource / script"]

    G["PUT api/skills/name Scripts[]"] --> H{"Extension<br/>Interpreters'ta mi?"}
    H -- "hayir" --> HX["400 kayit reddedilir"]
    H -- "evet" --> I["Kayit basarili<br/>CALISTIRILAMAZ henuz"]
    I --> J{"scripts.Enabled +<br/>AllowStoredScripts?"}
    J -- "hayir" --> JX["Model script'i hic gormez"]
    J -- "evet" --> K{"skill_script_grants<br/>gecerli izin var mi?"}
    K -- "hayir" --> KX["403/TraconException<br/>script.denied"]
    K -- "evet" --> L["Ayri OS sureci<br/>zaman asimi, cikti siniri, temiz ortam"]
    L --> M["script.run denetim izi<br/>+ tool_invocations + span"]

    style HX fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style JX fill:#7a4a1f,stroke:#3d250f,color:#ffffff
    style KX fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style M fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP zarfı (`ProblemDetails`, CRUD, idempotency) | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Üç katmanlı erişim koruması, kiracı izolasyonu, API anahtarları | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada TEKRARLANMAZ |
| Rol matrisi testi (Reader/Admin, `TraconPolicies`) | **Hiçbir dosyaya atanmamış** — bkz. aşağıdaki not |
| `cancel_order` gibi normal tool onayları, "Hatırla" kalıcı kural mekanizması | `10-ARAYUZ-AGENT-PLAYGROUND.md` (zaten üretildi) — burada yalnız `load_skill`'e özgü fark not edilir |
| MCP tool'larının onay akışı | `18-MCP-VE-A2A.md` |
| `PatternContentGuard`/içerik engeli | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` |
| Genel maliyet/kota gözlemlenebilirliği | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) |

> **Rol matrisi bu dosyada test edilmez.** `SkillEndpoints`/`SkillScriptGrantEndpoints`
> `RequireRole(roles.Reader)`/`RequireRole(roles.Admin)` kullanır
> (`RoleEndpointConventionBuilderExtensions.cs:24-29`), ama bu bir NO-OP'tur:
> `TraconPolicies.Reader`/`.Admin` tüketicinin `AuthorizationOptions`'ında
> KAYITLI DEĞİLSE (örnek uygulama kaydetmez) kısıtlama hiç uygulanmaz — statik
> bearer token TÜM skill/script uçlarına erişir. Gerçek bir rol ayrımı test
> etmek özel bir kimlik doğrulama şeması ister; bu, `13-KIRACI-VE-GUVENLIK.md`'nin
> kapsamına daha yakındır ve orada tekrarlanmamıştı — sonraki bir oturum bu
> boşluğu değerlendirmelidir.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Skill ve script özellikleri **her kalıcılık sağlayıcısında** (bellek içi
   dahil) aynı şekilde çalışır — `IAgentSkillStore`/`ISkillScriptGrantStore`
   `UsePostgreSql()` çağrılmadan `InMemoryAgentSkillStore`/
   `InMemorySkillScriptGrantStore` ile kayıtlıdır. Bu dosyanın çoğu case'i
   kalıcılık sağlayıcısından bağımsızdır; PostgreSQL gerektiren case'ler açıkça
   işaretlenmiştir.
3. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.
4. Örnek uygulama **hiçbir skill, script veya çalıştırma izni tanımlamaz**
   (`grep -n "Skill" samples/Tracon.Api/Program.cs` boş döner) ve
   `.UseSkillScripts(...)` hiç çağrılmaz — script çalıştırma varsayılan olarak
   **tamamen kapalıdır**. §6, bunu açmak için geçici bir kod değişikliği ister;
   o bölümün başında ayrıca belirtilir.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
```

> **Gerçek para uyarısı.** §3'ün case'leri ile §6 ve §8'in Playground adımları
> (👤) gerçek bir `run` başlatır ve küçük ölçüde OpenAI ücreti doğurur. Diğer
> case'ler hiçbir model çağırmaz.

---

## Bu dosyanın yerel fixture'ları

Bu veriler yalnız bu dosyaya özgüdür, `00-INDEKS.md`'ye girmez (`PROMPT.md` §4.2).

| Kimlik | Değer |
|---|---|
| `FIX-SKILL-FATURA` | Ad `fatura-kontrolu` · Açıklama `Fatura kontrol kurallarini ve KDV hesaplamasini aciklar.` · Talimat aşağıda |
| `FIX-SKILL-PROMPT` | `Fatura kontrol kurallarini uygulayarak yardim et` → `load_skill` çağrısı bekler |
| `FIX-SCRIPT-MERHABA` | Ad `merhaba` · Uzantı `sh` · İçerik `echo merhaba-tracon` |
| `FIX-AGENT-SKILL` | Ad `manuel-skill-test` · Talimat `Sen bir yardimci asistansin.` · `skillNames: ["fatura-kontrolu"]` · Tool yok |

`FIX-SKILL-FATURA`'nın `instructions` alanı (deterministik bir işaretçi taşır,
model bunu birebir yazınca skill'in gerçekten bağlama girdiği kanıtlanır):

```text
Bu bir fatura kontrol skill'idir. Bu talimati okudugunda, baska hicbir sey
eklemeden tam olarak su metni yaz: FATURA_SKILL_ACTIVE
```

---

# 1 — Skill CRUD ve Frontmatter Doğrulama (Faz 10)

Doğrulama kuralları Tracon'in kendi icadı değildir — MAF'ın
`AgentSkillFrontmatter.ValidateName/ValidateDescription/ValidateCompatibility`
metotları kullanılır (`SkillEndpoints.cs:106-120`). Bu yüzden hata `detail`
metinleri **İngilizcedir**; `title` alanı Türkçedir. Bu bilinçli bir
karışıklık değildir, doğrudan MAF'ın metnini geçirmenin sonucudur.

### MT-SKILL-001 — `PUT /api/skills/{name}` yeni bir skill oluşturur (`201`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-SKILL-FATURA`'yı oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/fatura-kontrolu" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "fatura-kontrolu",
  "description": "Fatura kontrol kurallarini ve KDV hesaplamasini aciklar.",
  "instructions": "Bu bir fatura kontrol skillidir. Bu talimati okudugunda, baska hicbir sey eklemeden tam olarak su metni yaz: FATURA_SKILL_ACTIVE",
  "enabled": true,
  "resources": [],
  "scripts": []
}'
```

**Beklenen sonuç**
- `HTTP: 201` (ilk oluşturma; `SkillEndpoints.SaveAsync` var olma kontrolüne
  göre `Created`/`Ok` seçer, `SkillEndpoints.cs:88-92`).
- Gövdede `version: 1`, `createdAt == updatedAt`.

---

### MT-SKILL-002 — Aynı skill'i tekrar `PUT` etmek günceller (`200`), `version` artar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-001 geçti.

**Adımlar**
1. Aynı ada, farklı bir `description` ile tekrar `PUT` gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/fatura-kontrolu" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "fatura-kontrolu",
  "description": "Fatura kontrol kurallarini aciklar (guncellendi).",
  "instructions": "Bu bir fatura kontrol skillidir. Bu talimati okudugunda, baska hicbir sey eklemeden tam olarak su metni yaz: FATURA_SKILL_ACTIVE",
  "enabled": true,
  "resources": [],
  "scripts": []
}'
```

**Beklenen sonuç**
- `HTTP: 200` (`Created` DEĞİL).
- `version: 2`, `createdAt` DEĞİŞMEZ, `updatedAt` ilerler
  (`InMemoryAgentSkillStore.cs:80-87`, PostgreSQL izleğinde eşdeğer upsert).

---

### MT-SKILL-003 — `GET /api/skills` kiracının skill listesini döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-001 geçti.

**Girilecek veri**
```bash
curl -s "$APU/api/skills" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Liste `fatura-kontrolu`'nu içerir.

---

### MT-SKILL-004 — `DELETE` skill'i ve cascade kaynaklarını siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- `PUT` ile, `resources` alanında bir kaynak taşıyan bir skill oluştur (`test-kaynakli`):
  ```bash
  curl -s -X PUT "$APU/api/skills/test-kaynakli" -H "$APB" -H "content-type: application/json" -d '{
    "name": "test-kaynakli", "description": "Kaynak silme testi.",
    "instructions": "test", "enabled": true,
    "resources": [{ "name": "policy.md", "description": "ilke", "mediaType": "text/plain", "content": "icerik" }],
    "scripts": []
  }'
  ```

**Adımlar**
1. Skill'i sil.
2. Aynı adı `GET` ile sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/skills/test-kaynakli" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/skills/test-kaynakli" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 204`.
- Adım 2: `HTTP: 404`, `title: "Skill bulunamadi"`.

**Doğrulama sorgusu** *(PostgreSQL izleğinde)*
```sql
SELECT count(*) FROM tracon.agent_skill_resources
WHERE skill_id = (SELECT id FROM tracon.agent_skills WHERE name = 'test-kaynakli');
-- Skill kaydinin kendisi de silindigi icin 0 satir bekleniyor (skill_id yabanci anahtari yok olur);
-- ON DELETE CASCADE (0003_agent_skills.sql:22) sayesinde ayri bir silme adimi gerekmez.
```

---

### MT-SKILL-005 — Var olmayan skill'i silmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/skills/hic-yok" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Skill bulunamadi"`.

---

### MT-SKILL-006 — Yoldaki ad ile gövdedeki ad uyuşmazsa → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/skill-a" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "skill-b", "description": "x", "instructions": "x", "enabled": true, "resources": [], "scripts": []
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Ad uyusmuyor"`, `detail`
  `Yoldaki ad 'skill-a', govdedeki ad 'skill-b'.`

---

### MT-SKILL-007 — Büyük harf/alt çizgi içeren ad → `400` (MAF'ın kendi ad kuralı)

Negatif senaryo. `AgentSkillFrontmatter.ValidateName`'in gerçek deseni
`^[a-z0-9]([a-z0-9]*-[a-z0-9])*[a-z0-9]*$`'dır (yalnız küçük harf, rakam,
tire; baş/son tire ve ardışık tire yasak) — bunlar MAF DLL'inden decompile
edilerek ölçüldü, tahmin edilmedi.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/Fatura_Kontrolu" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "Fatura_Kontrolu", "description": "x", "instructions": "x", "enabled": true, "resources": [], "scripts": []
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Skill adi gecersiz"`, `detail`
  `Skill name must use only lowercase letters, numbers, and hyphens, and must
  not start or end with a hyphen or contain consecutive hyphens.` (İngilizce —
  doğrudan MAF'ın mesajı).

---

### MT-SKILL-008 — 65 karakterlik ad (64 sınırını aşan) → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
LONGNAME=$(python3 -c "print('a-' * 32 + 'b')")   # 65 karakter, desene uygun
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/$LONGNAME" -H "$APB" \
     -H "content-type: application/json" -d "{
  \"name\": \"$LONGNAME\", \"description\": \"x\", \"instructions\": \"x\", \"enabled\": true, \"resources\": [], \"scripts\": []
}"
```

**Beklenen sonuç**
- `HTTP: 400`. `detail: "Skill name must be 64 characters or fewer."`

---

### MT-SKILL-009 — Boş `description` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/bos-aciklama" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "bos-aciklama", "description": "", "instructions": "x", "enabled": true, "resources": [], "scripts": []
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Skill aciklamasi gecersiz"`, `detail`
  `Skill description is required.`

---

### MT-SKILL-010 — `instructions` 64 KB sınırını aşarsa → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 -c "
import json
body = { 'name': 'cok-uzun-talimat', 'description': 'x', 'instructions': 'a' * 70000,
         'enabled': True, 'resources': [], 'scripts': [] }
print(json.dumps(body))
" > /tmp/skill-buyuk.json
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/cok-uzun-talimat" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/skill-buyuk.json
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Skill talimati cok buyuk"`, `detail`
  `instructions en fazla 65536 bayt olabilir.` (`TraconSkillOptions.MaxInstructionsLength`,
  varsayılan 64 KB).

---

### MT-SKILL-011 — 21. kaynak eklenirse (limit 20) → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 -c "
import json
resources = [{ 'name': f'kaynak-{i}', 'mediaType': 'text/plain', 'content': 'x' } for i in range(21)]
body = { 'name': 'cok-kaynakli', 'description': 'x', 'instructions': 'x', 'enabled': True,
         'resources': resources, 'scripts': [] }
print(json.dumps(body))
" > /tmp/skill-21-kaynak.json
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/cok-kaynakli" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/skill-21-kaynak.json
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Cok fazla kaynak"`, `detail`
  `Bir skill en fazla 20 kaynak tasiyabilir.`

---

### MT-SKILL-012 — Aynı skill içinde iki kaynak aynı adı taşırsa → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/cakisan-kaynak" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "cakisan-kaynak", "description": "x", "instructions": "x", "enabled": true,
  "resources": [
    { "name": "policy.md", "mediaType": "text/plain", "content": "a" },
    { "name": "policy.md", "mediaType": "text/plain", "content": "b" }
  ],
  "scripts": []
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Kaynak adi gecersiz"`, `detail`
  `Her kaynak adi bos olmamali ve skill icinde benzersiz olmalidir.`

---

### MT-SKILL-013 — Arayüzden skill oluşturma ve düzenleme (Skills ekranı)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- `http://localhost:5080/tracon/skills` açık.

**Adımlar**
1. "Yeni Skill" düğmesine tıkla.
2. `Ad`: `arayuz-skilli` · `Açıklama`: `Arayuzden olusturulan test skilli.`
3. `Talimatlar` metin kutusuna: `Bu skill arayuzden yazildi.`
4. Kaydet.

**Beklenen sonuç**
- Kaydet sonrası `skills` listesine geri dönülür, `arayuz-skilli` listede
  görünür, kaynak sayısı `0`.
- Markdown talimat metni **render edilmez** — düz `<textarea>` olarak kalır
  (Faz 10'un bilinçli bundle bütçesi kararı).

---

### MT-SKILL-014 — Skill'i arayüzden devre dışı bırakma, checkbox agent düzenleyicisinde kilitlenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-013 geçti (`arayuz-skilli` var).

**Adımlar**
1. `arayuz-skilli`'yi düzenle, "Etkin" kutucuğunu KALDIR, kaydet.
2. Herhangi bir agent'ı düzenle, Skills panelini aç.

**Beklenen sonuç**
- Adım 1 sonrası listede `arayuz-skilli` `Devre disi` rozetiyle görünür.
- Adım 2: `arayuz-skilli`'nin checkbox'ı **tıklanamaz** durumdadır
  (`agent-editor.tsx:549`, `disabled={!skill.enabled || ...}`) — devre dışı
  bir skill bir agent'a hiç bağlanamaz, yalnız zaten bağlıysa (önceden
  seçilmişse) listede görünmeye devam edebilir ama derlemeye girmez (bkz. §2).

---

# 2 — Skill Kataloğu Çözümleme Kuralları (Faz 10)

Bu bölümün case'leri `AgentSkillCatalog`'un davranışını kanıtlar: kod kaydının
önceliği, bilinmeyen skill, skill sayısı sınırı ve önbellek parmak izi.

### MT-SKILL-020 — Bilinmeyen skill adına işaret eden agent → SAVE zamanında `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Adımlar**
1. Var olmayan bir skill adıyla agent oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "hayalet-skilli-agent",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "skillNames": ["hic-var-olmayan-skill"]
}'
```

**Beklenen sonuç**
- `HTTP: 400`. Doğrulama raporunda `code: "unknown_skill"`,
  `message: "'hayalet-skilli-agent' agent'i 'hic-var-olmayan-skill' skill'ine
  isaret ediyor ancak skill bulunamadi."`, `path: "skillNames[0]"`
  (`AgentDefinitionValidator.cs:280-289`).

---

### MT-SKILL-021 — 🚨 `MaxSkillsPerAgent` aşımı SAVE'de geçer, yalnız RUN'da `400` verir

Bu, §2'nin başlığındaki asimetriyi doğrudan gösterir. `AgentDefinitionValidator`
skill VARLIĞINI denetler ama SAYISINI denetlemez; sayı sınırı yalnız
`AgentSkillCatalog.ResolveAsync` içindedir ve o metot yalnız agent
çalıştırılırken çağrılır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Skills:MaxSkillsPerAgent" "1"
```
Uygulama yeniden başlatılır. İki geçerli skill oluştur:
```bash
for n in birinci ikinci; do
curl -s -X PUT "$APU/api/skills/skill-$n" -H "$APB" -H "content-type: application/json" -d "{
  \"name\": \"skill-$n\", \"description\": \"Sinir testi $n.\", \"instructions\": \"talimat $n\",
  \"enabled\": true, \"resources\": [], \"scripts\": []
}"
done
```

**Adımlar**
1. İki skill adını birden taşıyan bir agent KAYDET (limit `1`'e rağmen).
2. Bu agent'ı ÇALIŞTIRMAYI dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "iki-skilli-agent",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "skillNames": ["skill-birinci", "skill-ikinci"]
}'
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/iki-skilli-agent/run" -H "$APB" \
     -H "content-type: application/json" -d '{ "message": "Merhaba" }'
```

**Beklenen sonuç**
- Adım 1: `HTTP: 201` — kayıt BAŞARILI olur, `MaxSkillsPerAgent` burada hiç
  denetlenmez.
- Adım 2: `HTTP: 400`. `title: "Agent derlenemedi"`, `detail`
  `'iki-skilli-agent' agent'i en fazla 1 skill tasiyabilir.`
  (`AgentSkillCatalog.cs:44-51`, `AgentEndpoints.cs:466-473` üzerinden
  `TraconException` yakalanıp `400`'e çevrilir).

---

### MT-SKILL-022 — Devre dışı skill derlemeye girmez, model `load_skill` içinde hiç görmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-SKILL-FATURA` var ve devre dışı bırakılmış:
  ```bash
  curl -s -X PUT "$APU/api/skills/fatura-kontrolu" -H "$APB" -H "content-type: application/json" -d '{
    "name": "fatura-kontrolu", "description": "Fatura kontrol kurallarini aciklar.",
    "instructions": "test", "enabled": false, "resources": [], "scripts": []
  }'
  ```
- `FIX-AGENT-SKILL` bu skill'e bağlı olarak oluşturulmuş.

**Adımlar**
1. Playground'da `manuel-skill-test` agent'ına `FIX-SKILL-PROMPT`'u gönder.

**Beklenen sonuç**
- Hiçbir `load_skill` onay kartı belirmez — `AgentSkillCatalog.GetEnabledAsync`
  yalnız `Enabled: true` kayıtları döner (`AgentSkillCatalog.cs:83-93`),
  `TraconSkillsSource` bu skill'i MAF'a hiç sunmaz.
- Model, herhangi bir skill talimatı olmadan genel bir yanıt üretir.

---

### MT-SKILL-023 — Kod tanımlı skill, aynı adlı DB kaydını geçersiz kılar

Bu case geçici bir kod değişikliği ister — `AddSkill` yalnız kodda çağrılır,
HTTP karşılığı yoktur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10, Faz 186 |
| **İlgili karar** | K-003 (agent'lardaki aynı kural, skill'lere uygulanmış hali) |
| **Devir** | ➜ CI: `SkillCrudTests.A_stored_skill_cannot_be_saved_under_a_name_defined_in_code` · `SkillCrudTests.Deleting_a_name_defined_in_code_removes_only_its_stored_copy` · `SkillTests.A_skill_defined_in_code_opens_read_only` |

**Ön koşul**
1. `FIX-SKILL-FATURA` DB'de kayıtlı (MT-SKILL-001), `instructions` alanı
   `FATURA_SKILL_ACTIVE` işaretçisini taşıyor.
2. `samples/Tracon.Api/Program.cs`'e, `tracon` değişkeni
   tanımlandıktan hemen sonra GEÇİCİ olarak ekleyin:
   ```csharp
   tracon.AddSkill(new AgentSkillDefinition
   {
       TenantId = "default",
       Name = "fatura-kontrolu",
       Description = "KOD TANIMLI surum - DB kaydini gecersiz kilar.",
       Instructions = "Bu talimati okudugunda tam olarak KOD_SKILL_ACTIVE yaz.",
       Enabled = true,
   });
   ```
3. Uygulamayı yeniden başlat.

**Adımlar**
1. Playground'da `manuel-skill-test`'e `FIX-SKILL-PROMPT`'u gönder, onayla.

**Beklenen sonuç**
- `load_skill` sonucu KOD tanımlı açıklamayı taşır ("KOD TANIMLI surum..."),
  DB'deki `description` DEĞİL — `AgentSkillCatalog.FindAsync`
  `_codeSkills.TryGetValue` başarılıysa store'a hiç bakmaz
  (`AgentSkillCatalog.cs:104-106`).
- Model nihai yanıtta `KOD_SKILL_ACTIVE` yazar, `FATURA_SKILL_ACTIVE` DEĞİL.

**Ek adımlar (Faz 186 — kod adı artık yazılamaz, K-003 emsali)**
2. `PUT /api/skills/fatura-kontrolu` ile DB kaydını yeniden kaydetmeyi dene
   (MT-SKILL-001'in isteği).
3. `GET /api/skills/fatura-kontrolu` → `origin`'e bak.
4. Konsolda Skills → `fatura-kontrolu` → düzenleyiciyi aç.
5. **Delete stored copy** ile DB kaydını sil; sonra aynı `DELETE`'i yeniden gönder.

**Ek beklenen sonuç**
- Adım 2: `HTTP: 409`, `title: "Code-defined skill cannot be modified"`; DB
  kaydı değişmez. (Faz 186'dan önce `200` dönüyor ve hiç çalışmayan bir kayıt
  yazıyordu.)
- Adım 3: `origin: "Code"` — uç çalışacak skill'i döndürür.
- Adım 4: form KOD tanımlı içerikle **salt okunur** açılır; "This name is
  registered in code…" uyarısı görünür, Save düğmesi yoktur. (Önceden form kod
  içeriğini gösteriyor ve Save onu DB kaydının üstüne yazıyordu.)
- Adım 5: ilk `DELETE` `204` (gölgelenen DB kaydı silinir, kod skill'i
  çalışmaya devam eder); ikinci `DELETE` `409`.

---

### MT-SKILL-024 — Skill düzenlemesi, `CompiledAgentCache` parmak izini değiştirir

Bu case Faz 10'un "önbellek atlanırsa skill düzenlemesi çalışan sürece hiç
yansımaz" riskini doğrudan sınar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-SKILL-FATURA` (`FATURA_SKILL_ACTIVE` işaretçili) ve `FIX-AGENT-SKILL`
  var (MT-SKILL-023'ün kod değişikliği GERİ ALINMIŞ olmalı — kod skill'i
  varken bu case DB skill'ini asla göremez).

**Adımlar**
1. Playground'da `manuel-skill-test`'e `FIX-SKILL-PROMPT`'u gönder, onayla,
   turun `FATURA_SKILL_ACTIVE` ürettiğini doğrula.
2. `fatura-kontrolu` skill'inin `instructions` alanını Skills ekranından
   değiştir: işaretçiyi `FATURA_SKILL_V2` yap, kaydet.
3. YENİ bir sohbette aynı promptu tekrar gönder, onayla.

**Beklenen sonuç**
- Adım 3'ün sonucu `FATURA_SKILL_V2` üretir, `FATURA_SKILL_ACTIVE` DEĞİL —
  `CreateFingerprint`'in `skill.Version`/`skill.UpdatedAt`'a bağımlılığı
  (`AgentSkillCatalog.cs:109-125`) her düzenlemede yeni bir derlenmiş agent
  zorlar, eski (önbelleğe alınmış) talimat asla sızmaz.

---

### MT-SKILL-025 — Sunucu `MaxSkillsPerAgent`'ı değiştirir, arayüz checkbox limiti HABERSİZ kalır

🚨 **Şüpheli bulgu (ölçüldü, koşulmadı).** `agent-editor.tsx:540`
`limitReached = form.skillNames.length >= 10` sabit sayı `10` ile
karşılaştırır; sunucunun gerçek `MaxSkillsPerAgent` değeri `/api/meta`
gövdesinde YOKTUR (`grep -n "MaxSkillsPerAgent" src/Tracon.AspNetCore/Endpoints/MetaEndpoints.cs`
boş döner). Sunucu sınırı `10`'dan farklı ayarlanırsa arayüz bunu asla
öğrenmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Skills:MaxSkillsPerAgent" "2"
```
Uygulama yeniden başlatılır. En az 3 etkin skill oluştur.

**Adımlar**
1. Agent düzenleyicide bir agent aç, Skills panelinde 3 skill seçmeyi dene.

**Beklenen sonuç**
- Arayüz 10'a kadar seçime izin verir (checkbox'lar 3. seçimde KİLİTLENMEZ) —
  sunucunun gerçek sınırı `2`'dir.
- Kaydet'e basınca `PUT /api/agents` başarılı olur (save-time sınır YOK, bkz.
  MT-SKILL-021), ama agent ÇALIŞTIRILDIĞINDA `400 "Agent derlenemedi"` alınır.
- Bu, kullanıcıya YANLIŞ bir izin görüntüsü veren bir arayüz/sunucu
  uyuşmazlığıdır; kusur değil, eksik senkronizasyon.

---

# 3 — Gerçek Model ile Onaylı Skill Yükleme (Faz 10 kanıtı)

Bu bölüm `load_skill` onayını gerçek modelle kanıtlar: onaylanan şey bir eylem
değil, agent'ın talimatının çalışma anında değişmesidir.

**Ön koşul (bölümün tamamı)**
- `FIX-SKILL-FATURA` ve `FIX-AGENT-SKILL` oluşturulmuş, ikisi de etkin.
- `playground/manuel-skill-test` açık, yeni sohbet.

### MT-SKILL-030 — `FIX-SKILL-PROMPT` → `load_skill` onay kartı üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Adımlar**
1. `Fatura kontrol kurallarini uygulayarak yardim et` (`FIX-SKILL-PROMPT`) gönder.
2. Akış bitene kadar bekle.

**Beklenen sonuç**
- `data-testid="approval-card"` görünür: tool adı `load_skill`, argüman
  `skillName: "fatura-kontrolu"`.
- Onay kartından sonra final metin YOKTUR, tur `done` olur (`failed` DEĞİL) —
  `MT-UIAG-028` ile aynı davranış (MAF çalıştırmayı burada durdurur).

---

### MT-SKILL-031 — Onayla → skill talimatı bağlama girer, model işaretçiyi birebir üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-030'un onay kartı ekranda.

**Adımlar**
1. "Onayla" düğmesine tıkla (`data-testid="approval-approve"`, "Hatırla"
   İŞARETLEME — sonraki case'in yeniden üretilebilir olması için).
2. Akış tamamlanana kadar bekle.

**Beklenen sonuç**
- YENİ bir tur eklenir, `load_skill` sonucu skill'in talimat metnini içerir.
- Modelin nihai yanıtı tam olarak `FATURA_SKILL_ACTIVE` dizgisini içerir —
  bu, protokolün "yanıt X dizgisini içerir" kuralına uyan **değişmez**
  bir doğrulamadır (`PROMPT.md` §4.1).

---

### MT-SKILL-032 — Reddet → skill hiç yüklenmez, model onsuz devam eder

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir sohbet başlat, `FIX-SKILL-PROMPT`'u tekrar gönder (yeni bir onay
  kartı üretmek için).

**Adımlar**
1. "Reddet" düğmesine tıkla (`data-testid="approval-reject"`).
2. Akış tamamlanana kadar bekle.

**Beklenen sonuç**
- Kart kırmızı `rejected` rozetine döner.
- Yeni turun yanıtı `FATURA_SKILL_ACTIVE` dizgisini İÇERMEZ — skill'in
  talimatı hiçbir zaman bağlama girmedi.

---

### MT-SKILL-033 — "Hatırla" ile onay → sonraki çağrıda `load_skill` kartı hiç çıkmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | K-061 |

**Ön koşul**
- Yeni bir sohbet başlat.

**Adımlar**
1. `FIX-SKILL-PROMPT`'u gönder, onay kartı gelince "Hatırla" işaretle,
   "Onayla"ya tıkla.
2. "Yeni Sohbet"e tıkla (oturumu sıfırla).
3. `FIX-SKILL-PROMPT`'u YENİ sohbette tekrar gönder.

**Beklenen sonuç**
- Adım 3: `load_skill` çalışır ama HİÇBİR onay kartı belirmez —
  `ToolApprovalRuleEvaluator` `manuel-skill-test`/`load_skill` için kalıcı
  kuralı bulur (`tool_approval_rules` tablosu, K-061). Bu, `MT-UIAG-031`'in
  `cancel_order` için kanıtladığı MEKANİZMANIN AYNISIdır, yalnız tool adı
  `load_skill`'dir.
- Nihai yanıt yine `FATURA_SKILL_ACTIVE` içerir.

---

# 4 — Skill Script Kaydı ve Doğrulama (Faz 11)

Script KAYDETMEK ile script ÇALIŞTIRMAK ayrı yetkilerdir. Bu bölümün case'leri
yalnız kayıt ve doğrulama katmanını sınar; MT-SKILL-041 dışında hiçbiri kod
değişikliği istemez.

### MT-SKILL-040 — Varsayılan durumda (Interpreters boş) HERHANGİ bir script uzantısı reddedilir

Negatif senaryo. Sıfır kurulum gerektirir — `TraconSkillScriptOptions.Interpreters`
varsayılan olarak BOŞ sözlüktür.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-088 |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
  "resources": [],
  "scripts": [{ "name": "merhaba", "extension": "py", "content": "print(1)", "parametersSchema": null }]
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Script uzantisi izinli degil"`, `detail`
  `'py' uzantisi icin kayitli bir yorumlayici yok.` (`SkillEndpoints.cs:184-190`).

---

### MT-SKILL-041 — `Interpreters`'a `sh` eklenince AYNI kayıt başarılı olur (geçici kod değişikliği gerektirir)

🚨 **ÖNERME TERSİNE ÇEVRİLDİ (2026-09-17 koşumunda ölçüldü).** Bu case
önceden "`Interpreters` config'ten HER ZAMAN bağlanır, `dotnet
user-secrets` ile açılabilir" diyordu. Güncel kod TAM TERSİNİ yapıyor:
`TraconServiceCollectionExtensions.Binding.Core.cs:190-200` `AllowStoredScripts`,
`SkillRoots` VE `Interpreters`'ı **bilinçli olarak config'ten bağlamıyor**
— üçü de sunucuda çalıştırılabilecek yüzeyi genişletir, ve kod yorumu
doğrudan `Tracon__Skills__Scripts__Interpreters__sh=/bin/sh` örneğini
verip bunun "uygulamanın hiç onaylamadığı bir yorumlayıcı eklediğini"
söylüyor — yani bu case'in ORİJİNAL senaryosu artık kasıtlı olarak
KAPALI (güvenlik sıkılaştırması, kusur değil). Yalnız `Enabled` ve
`PlatformIsolationAcknowledged` hâlâ config'ten bağlanır (bunlar yalnız
DARALTABİLİR, asla genişletemez).

Bu yüzden case artık MT-SEC-070/MT-WF-090 deseninde **geçici bir kod
değişikliği** gerektirir — `dotnet user-secrets`/ortam değişkeni ile
AÇILAMAZ.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-088 (muhtemelen sonraki bir dalgada sıkılaştırıldı) |

**Ön koşul**
`samples/Tracon.Api/Program.cs`'e GEÇİCİ olarak ekle (`tracon` değişkeni
tanımlandıktan sonra):
```csharp
tracon.UseSkillScripts(o =>
{
    o.PlatformIsolationAcknowledged = true;
    o.Interpreters["sh"] = "/bin/bash";
    // 🚨 SART. Bu bayrak olmadan depodaki script'ler modele HIC sunulmaz
    // (`TraconSkillsSource.cs:81`) ve `run_skill_script` "Script 'merhaba' not
    // found in skill 'scriptli-skill'." doner — kayit DB'de dururken. Belirti
    // yaniltici: skill API'si script'i gosterir, model goremez. (2026-09-19)
    o.AllowStoredScripts = true;
});
```
`dotnet build`, uygulamayı yeniden başlat. (Bu çağrı `Scripts.Enabled`'ı
da `true` yapar — §6/§7'nin çoğu case'i zaten bunu ister; yalnız
`Enabled:false` gereken bir case için env değişkeniyle DARALT:
`Tracon__Skills__Scripts__Enabled=false`, bkz. MT-SKILL-046/050.)

**Adımlar**
1. MT-SKILL-040'daki AYNI isteği, `extension: "sh"` ile tekrarla.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
  "resources": [],
  "scripts": [{ "name": "merhaba", "extension": "sh", "content": "echo merhaba-tracon", "parametersSchema": null }]
}'
```

**Beklenen sonuç**
- `HTTP: 201`. Kayıt DB'de durur; `scripts.Enabled` durumuna göre
  çalıştırılabilir/çalıştırılamaz (kodun bu koşumdaki hâli `Enabled:true`
  bırakır — bkz. §6).

---

### MT-SKILL-042 — 11. script eklenirse (limit 10) → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-041'in `Interpreters:sh` ayarı geçerli.

**Girilecek veri**
```bash
python3 -c "
import json
scripts = [{ 'name': f'script-{i}', 'extension': 'sh', 'content': 'echo x', 'parametersSchema': None } for i in range(11)]
body = { 'name': 'cok-scriptli', 'description': 'x', 'instructions': 'x', 'enabled': True,
         'resources': [], 'scripts': scripts }
print(json.dumps(body))
" > /tmp/skill-11-script.json
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/cok-scriptli" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/skill-11-script.json
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Cok fazla script"`, `detail`
  `Bir skill en fazla 10 script tasiyabilir.`

---

### MT-SKILL-043 — Aynı skill içinde iki script aynı adı taşırsa → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-041'in `Interpreters:sh` ayarı geçerli.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/cakisan-script" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "cakisan-script", "description": "x", "instructions": "x", "enabled": true, "resources": [],
  "scripts": [
    { "name": "ayni-ad", "extension": "sh", "content": "echo a", "parametersSchema": null },
    { "name": "ayni-ad", "extension": "sh", "content": "echo b", "parametersSchema": null }
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Script adi gecersiz"`, `detail`
  `Her script adi bos olmamali ve skill icinde benzersiz olmalidir.`

---

### MT-SKILL-044 — Geçersiz JSON `parametersSchema` → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-090 |

**Ön koşul**
- MT-SKILL-041'in `Interpreters:sh` ayarı geçerli.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/bozuk-sema" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "bozuk-sema", "description": "x", "instructions": "x", "enabled": true, "resources": [],
  "scripts": [{ "name": "s1", "extension": "sh", "content": "echo x", "parametersSchema": "{ gecersiz json" }]
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Parametre semasi gecersiz"`, `detail`
  `parametersSchema gecerli bir JSON nesnesi olmalidir.`

---

### MT-SKILL-045 — Script içeriği 64 KB sınırını aşarsa → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-041'in `Interpreters:sh` ayarı geçerli.

**Girilecek veri**
```bash
python3 -c "
import json
body = { 'name': 'buyuk-script', 'description': 'x', 'instructions': 'x', 'enabled': True, 'resources': [],
         'scripts': [{ 'name': 's1', 'extension': 'sh', 'content': 'echo ' + ('a' * 70000), 'parametersSchema': None }] }
print(json.dumps(body))
" > /tmp/skill-buyuk-script.json
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/buyuk-script" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/skill-buyuk-script.json
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Script cok buyuk"`, `detail`
  `Her script en fazla 65536 bayt olabilir.`

---

### MT-SKILL-046 — `scripts.Enabled = false` iken bile bir script KAYDEDİLİR ve GERİ OKUNUR

Bu case, "kayıt" ile "çalıştırma"nın gerçekten ayrı iki yetki olduğunu
POZİTİF yönden kanıtlar (script çalışmasa bile veri kaybolmaz).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-087 |

**Ön koşul**
- MT-SKILL-041 geçti (`scriptli-skill`, `sh` script'i kayıtlı),
  `Tracon:Skills:Scripts:Enabled` AYARLANMAMIŞ (varsayılan `false`).

**Adımlar**
1. `GET /api/skills/scriptli-skill` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Gövde `scripts` dizisinde `merhaba` script'ini TAM içerikle (`content:
  "echo merhaba-tracon"`) döner — kayıt hiçbir zaman gizlenmez, yalnız
  modele SUNULMAZ (`TraconSkillsSource.CreateSkill`'in
  `_scripts is { StoredScriptsEnabled: true }` koşulu, `TraconSkillsSource.cs:71`).

---

# 5 — Script Çalıştırma İzinleri (Grant, Faz 11)

Grant uçları `UseSkillScripts()` gerektirmez — yalnız `scripts.Enabled`
bayrağını okur. Stored bir skill'in grant'ı içeriğin hash'ini ister (Faz 186;
§6 adım 4). Gerçek çalıştırma §6'nın konusudur.

### MT-SKILL-050 — Script çalıştırma KAPALIYKEN izin vermeye çalışmak → `409`

Negatif senaryo. Sıfır kurulum gerektirir (varsayılan durum).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
     -H "content-type: application/json" -d '{ "skillName": "scriptli-skill", "scriptName": "merhaba" }'
```

**Beklenen sonuç**
- `HTTP: 409`. `title: "Script calistirma kapali"`, `detail`
  `Izin vermeden once UseSkillScripts(...) ile script calistirmayi acin.`
  (`SkillScriptGrantEndpoints.cs:58-64`) — arayüz "izin verildi" gösterip
  çalıştırmanın yine reddedilmesi gibi yanıltıcı bir durum bilerek
  engellenmiştir.

---

### MT-SKILL-051 — `scripts.Enabled = true` yapılınca (yalnız config) AYNI istek `201` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Skills:Scripts:Enabled" "true"
dotnet user-secrets set "Tracon:Skills:Scripts:PlatformIsolationAcknowledged" "true"
```
Uygulama yeniden başlatılır. (🚨 Bu ikisi TEK BAŞINA script'i
ÇALIŞTIRILABİLİR yapmaz — `UseSkillScripts()` çağrılmadığı sürece
`SkillScriptSupport` DI'da kayıtlı değildir; bkz. MT-SKILL-057. Yalnız GRANT
vermeyi mümkün kılar.)

**Adımlar**
1. MT-SKILL-050'deki isteği içeriğin hash'iyle tekrarla (Faz 186):
   `scriptli-skill` stored bir skill'dir ve grant içeriğini pinler.

**Girilecek veri**
```bash
HASH=$(curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | jq -r '.scripts[] | select(.name=="merhaba") | .contentHash')
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
     -H "content-type: application/json" \
     -d "{ \"skillName\": \"scriptli-skill\", \"scriptName\": \"merhaba\", \"expectedContentHash\": \"$HASH\" }"
```

**Beklenen sonuç**
- `HTTP: 201`, gövdede `contentHash` = `HASH`. Hash'siz aynı istek `400`
  döner (MT-SKILL-067). Gövde `grantedBy: null` (statik bearer token bir
  `ClaimsPrincipal` üretmez, `AmbientAuditActorResolver.Resolve()`
  `IsAuthenticated != true` olduğu için `null` döner,
  `AmbientAuditActorResolver.cs:32-35`), `expiresAt: null`.

---

### MT-SKILL-052 — Geçmiş bir `expiresAt` → `400`

Negatif senaryo, sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-051'in ön koşulu geçerli.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
     -H "content-type: application/json" -d '{
  "skillName": "scriptli-skill", "scriptName": "merhaba", "expiresAt": "2020-01-01T00:00:00Z"
}'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Bitis zamani gecmiste"`, `detail`
  `expiresAt gelecekte bir an olmalidir.`

---

### MT-SKILL-053 — Boş `skillName` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-051'in ön koşulu geçerli.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
     -H "content-type: application/json" -d '{ "skillName": "" }'
```

**Beklenen sonuç**
- `HTTP: 400`. `title: "Skill adi gerekli"`, `detail: "skillName bos olamaz."`

---

### MT-SKILL-054 — `GET /api/skill-script-grants` listesi, arayüzde kırmızı uyarıyla görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-051 geçti.

**Adımlar**
1. `curl -s "$APU/api/skill-script-grants" -H "$APB"` çağır.
2. `http://localhost:5080/tracon/skills` sayfasını aç, en alttaki
   "Script Çalıştırma İzinleri" panelini incele.

**Beklenen sonuç — CSS sınıfı DÜZELTİLDİ (2026-09-17 koşumunda ölçüldü)**
- Adım 1: liste `scriptli-skill`/`merhaba` çiftini içerir; satır
  `contentHash` taşır (Faz 186).
- Adım 2: tablonun **Pin** sütunu satırda `current` rozetini gösterir
  (içerik grant'tan sonra değişmediyse; değiştiyse `stale` — MT-SKILL-076).
- Adım 2: panelin üstünde belirgin bir "danger" tonlu uyarı kutusu görünür
  ve grant tablosu aynı kaydı gösterir. `data-testid` hâlâ yok; CSS sınıfı
  artık `border-red-500` DEĞİL — bileşen `src/Tracon.UI/frontend/src/screens/skills/script-grants.tsx`'e
  taşınmış ve tema token'larına geçirilmiş: `border-danger bg-danger-soft
  text-danger` (`.bg-danger-soft` seçicisiyle DOM'da doğrulandı). Kodun
  kendi yorumu bunu açıklıyor: eski `border-red-500 bg-red-500/10
  text-red-500` ham Tailwind paleti temaya uymuyordu, "ürünün en yüksek
  sesli uyarısı hiçbir yerde kullanılmayan tek renkti" — bilinçli bir
  tasarım-sistemi düzeltmesi, kusur değil. Görsel/davranışsal iddia
  (belirgin tehlike kutusu var) hâlâ doğru.

---

### MT-SKILL-055 — İzni arayüzden iptal et (`Revoke`) → liste hemen güncellenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-054 geçti.

**Adımlar**
1. Skills ekranındaki grant satırının "İptal" düğmesine tıkla.

**Beklenen sonuç**
- Satır listeden kaybolur (`ScriptGrantsPanel`'in `active` filtresi
  `revokedAt === null` satırları gösterir, `skills.tsx:149`).

**Doğrulama sorgusu** *(PostgreSQL izleğinde)*
```sql
SELECT skill_name, script_name, revoked_at FROM tracon.skill_script_grants
WHERE skill_name = 'scriptli-skill';
-- satir SILINMEZ, revoked_at doludur.
```

---

### MT-SKILL-056 — Var olmayan bir izni iptal etmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/skill-script-grants/hic-yok-skill" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`. `title: "Izin bulunamadi"`, `detail`
  `'hic-yok-skill' icin gecerli bir calistirma izni yok.`

---

# 6 — Script Çalıştırma Kapıları ve Gerçek Çalıştırma Kanıtı (Faz 11)

**Bölümün ortak ön koşulu** — MT-SKILL-057…077 bu kurulumu kullanır; MT-SKILL-057
yalnız 2-5. adımları uygular. Faz 186'da içeriğe pinli grant'a göre yeniden
yazıldı (K-860).

1. MT-SKILL-041'in geçici kod değişikliğini uygula (`UseSkillScripts` +
   `AllowStoredScripts` + `Interpreters["sh"]`).
2. `dotnet build`, uygulamayı yeniden başlat.
3. `scriptli-skill`/`merhaba`'yı kaydet (MT-SKILL-041'in isteği).
4. Grant'ı içeriğin hash'iyle ver. Stored script'in grant'ı içeriğe pinlidir:
   hash'siz istek `400` döner (MT-SKILL-067). Yardımcı fonksiyon:
   ```bash
   grant_script() {  # $1 = script adı; boş verilirse bütün skill (küme izi)
     local body
     if [ -n "$1" ]; then
       body=$(curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | jq -c --arg s "$1" \
         '{skillName: "scriptli-skill", scriptName: $s, expectedContentHash: (.scripts[] | select(.name == $s) | .contentHash)}')
     else
       body=$(curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | jq -c \
         '{skillName: "scriptli-skill", expectedContentHash: .scriptSetHash}')
     fi
     curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
          -H "content-type: application/json" -d "$body"
   }
   grant_script merhaba   # beklenen: HTTP: 201
   ```
   Bir script'in içeriği değişirse, aynı komut yeni hash'le yeniden koşulur.
5. `manuel-script-test` agent'ını oluştur:
   ```bash
   curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
     "name": "manuel-script-test", "instructions": "Sen bir yardimci asistansin.",
     "model": { "provider": "openai", "model": "gpt-5.4-mini" },
     "skillNames": ["scriptli-skill"]
   }'
   ```

**Bölüm sonu temizliği:** MT-SKILL-041'in kod satırını `Program.cs`'ten
kaldır; `manuel-script-test`'i ve `scriptli-skill`'i sil; etkin grant'ları
`DELETE /api/skill-script-grants/{skillName}` ile kaldır.

---

### MT-SKILL-057 — 🚨 Config-only kurulum (kod değişikliği OLMADAN) script'i modele HİÇ sunmaz

Bu case, "config açar" varsayımını doğrudan çürütür ve §5'in
MT-SKILL-051 notunu kanıtlar. **Bu case'i koşarken §6'nın 1. adımını
(`UseSkillScripts` kod satırı) UYGULAMAYIN** — yalnız 2-5. adımlar geçerli.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- §6'nın 2-5. adımları uygulandı, 1. adım (kod değişikliği) UYGULANMADI.
- `Enabled`/`PlatformIsolationAcknowledged` da config ile açıldı:
  ```bash
  dotnet user-secrets set "Tracon:Skills:Scripts:Enabled" "true"
  dotnet user-secrets set "Tracon:Skills:Scripts:PlatformIsolationAcknowledged" "true"
  ```

**Adımlar**
1. Playground'da `manuel-script-test`'e `Fatura kontrol kurallarini
   uygulayarak yardim et, ardindan merhaba script'ini calistir` gönder.
2. Onay kartı(ları) çıktıkça onayla.

**Beklenen sonuç**
- `load_skill` çağrılır ve onay ister (skill kaydı zaten normal işler).
- Onaydan SONRA modele `merhaba` adında ÇAĞRILABİLİR bir tool asla
  sunulmaz — model bunu ya hiç denemez ya da "böyle bir araç yok" anlamına
  gelen bir yanıt üretir. Sebebi: `TraconSkillsSource.CreateSkill`'in
  `_scripts is { StoredScriptsEnabled: true }` koşulu `_scripts`'in kendisi
  (`SkillScriptSupport`) DI'da hiç kayıtlı olmadığı için `null`'dır — config
  bayrakları burada hiç okunmaz (`TraconSkillsSource.cs:71`).
- `tool_invocations` tablosunda `source = "skill:scriptli-skill"` taşıyan
  HİÇBİR satır oluşmaz.

---

### MT-SKILL-058 — Gerçek çalıştırma kanıtı: `echo` script'i onaydan geçer ve modele sonucu döner

Bu case §6'nın TAM ön koşulunu (1-5. adımlar) gerektirir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-086, K-091 |

**Adımlar**
1. Playground'da `manuel-script-test`'e `Fatura kontrol kurallarini
   uygulayarak yardim et, ardindan merhaba script'ini calistir` gönder.
2. `load_skill` onay kartını onayla.
3. `merhaba` script çağrısının onay kartını onayla.
4. Akış tamamlanana kadar bekle.

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §6 Aile W):** Bugünkü MAF sürümünde
> ikinci onay kartının tool adı `merhaba` DEĞİL, MAF'ın kendi generic
> `run_skill_script(skillName, scriptName, arguments)` dispatcher'ıdır.
> `TraconSkillsSource.CreateSkill`'in `skill.AddScript(script.Name, ...)`
> çağrısı (`TraconSkillsSource.cs:76-81`) hâlâ AYNI şekilde script başına
> çağrılıyor — ama `Microsoft.Agents.AI.AgentSkillsProvider` (paket içi,
> `AgentInlineSkillScript.RunAsync`/`RunSkillScriptAsync`) modele TEK bir
> generic tool şeması sunuyor ve `scriptName` argümanıyla kayıtlı delegeye
> dispatch ediyor — bu, eski beklentinin dayandığı MAF davranışından
> FARKLI. Üç ayrı bağımsız çalıştırmada (canlı OpenAI çağrısı) tutarlı
> şekilde gözlendi. Kanıt: `load_skill` sonrası modele sunulan
> `gen_ai.tool.definitions` izleme özniteliği (`MT-SKILL-070`) yalnız
> `["load_skill","read_skill_resource","run_skill_script"]` listeler —
> `merhaba` diye ayrı bir tool ADI hiçbir zaman modele sunulmuyor.
- İkinci onay kartının tool adı `run_skill_script`'tir, argümanları
  `{"skillName":"scriptli-skill","scriptName":"merhaba","arguments":""}`
  taşır.
- Script sonucu modele `exit_code: 0\nstdout:\nmerhaba-tracon\n\n`
  biçiminde döner (`SandboxedSkillScriptRunner.Format`,
  `SandboxedSkillScriptRunner.cs:355-379`).
- Model nihai yanıtında `merhaba-tracon` dizgisini içerir.

~~Eski beklenti (eski bir MAF sürümüne dayanıyordu): İkinci onay kartının
tool adı `merhaba`'dır (skill kaydındaki script adı aynen tool adı olur,
MAF'ın generic `run_skill_script`'i DEĞİL).~~

**Doğrulama sorgusu** *(PostgreSQL izleğinde)*
```sql
SELECT action, entity, tenant_id FROM tracon.audit_entries
WHERE action = 'script.run' ORDER BY created_at DESC LIMIT 1;
-- entity = 'scriptli-skill/merhaba' beklenir.

SELECT tool_name, source, error FROM tracon.tool_invocations
WHERE source = 'skill:scriptli-skill' ORDER BY created_at DESC LIMIT 1;
-- tool_name = 'skill_script', error = NULL beklenir.
```

---

### MT-SKILL-059 — İzin iptal edildikten sonra AYNI script reddedilir, `script.denied` yazılır

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-089 |

**Ön koşul**
- MT-SKILL-058 geçti.
- `scriptli-skill`/`merhaba` izni iptal edildi:
  ```bash
  curl -s -X DELETE "$APU/api/skill-script-grants/scriptli-skill?scriptName=merhaba" -H "$APB"
  ```

**Adımlar**
1. YENİ bir sohbette aynı promptu tekrar gönder, `load_skill` onayını ver.
2. `merhaba` script çağrısı belirirse onayla (onay hâlâ MAF seviyesinde
   istenir — izin kaydı AYRI bir kapıdır).

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §6 Aile W) — modele dönen mesaj
> genel bir hata metnidir, spesifik değil:** MAF'ın function-invoking
> katmanı, delegenin fırlattığı istisnayı modele DOĞRUDAN İLETMEZ — sabit
> `"Error: Function failed."` metnini döner (muhtemelen istisna
> içeriklerinin model bağlamına sızmasını önleyen bilinçli bir MAF
> davranışı). Spesifik red mesajı (`'scriptli-skill/merhaba' script'i
> calistirilmadi: ...`) Tracon'in KENDİ gözlemlenebilirlik katmanında
> (`tool_invocations.error`) tam olarak korunur — yalnız modele dönen metin
> genelleşir.
- Script çalışmaz; modele dönen tool sonucu `"Error: Function failed."`dir.
- `tool_invocations.error` sütunu tam metni taşır: `'scriptli-skill/merhaba'
  script'i calistirilmadi: Bu script icin gecerli bir calistirma izni yok.`
  (`SandboxedSkillScriptRunner.cs:254-262`).
- Denetim izine `action: "script.denied"`, `entity: "scriptli-skill/merhaba"`
  düşer — **yalnız `SandboxedSkillScriptRunner.DenyAsync`'in `after`
  alanını JSON'a çeviren düzeltmeden (bu koşumda yapıldı) SONRA**; öncesinde
  bu satır sessizce kayboluyordu (HATA-K-skill-audit-json, aşağıya bakınız).

~~Eski beklenti (modele dönen mesajın spesifik metni taşıdığını
varsayıyordu): modele dönen tool sonucu bir hata metni içerir
(`'scriptli-skill/merhaba' script'i calistirilmadi: ...`).~~

**Doğrulama sorgusu**
```sql
SELECT action, entity FROM tracon.audit_entries
WHERE action = 'script.denied' ORDER BY created_at DESC LIMIT 1;
```

---

### MT-SKILL-060 — Zaman aşımı: uzun süren script öldürülür, "zaman aşımına uğradı" metni döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-058'in ön koşulu geçerli, EK olarak:
  ```bash
  dotnet user-secrets set "Tracon:Skills:Scripts:Timeout" "00:00:02"
  ```
- `scriptli-skill`'e `uyuyan` adında yeni bir script ekle (`sleep 10`):
  ```bash
  curl -s -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" -H "content-type: application/json" -d '{
    "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
    "resources": [],
    "scripts": [
      { "name": "merhaba", "extension": "sh", "content": "echo merhaba-tracon", "parametersSchema": null },
      { "name": "uyuyan", "extension": "sh", "content": "sleep 10 && echo bitti", "parametersSchema": null }
    ]
  }'
  curl -s -X POST "$APU/api/skill-script-grants" -H "$APB" -H "content-type: application/json" \
       -d '{ "skillName": "scriptli-skill", "scriptName": "uyuyan" }'
  ```
- Uygulama yeniden başlatıldı (Timeout ayarı için).

**Adımlar**
1. `manuel-script-test`'e skill'i yükleyip `uyuyan` script'ini çalıştırmasını
   iste, her onayı ver.
2. ~2-3 saniye içinde tur `done` olana kadar bekle.

**Beklenen sonuç**
- Script sonucu `Script zaman asimina ugradi ve surec agaci sonlandirildi.`
  metnini döner, `stdout`/`stderr` bölümü YOKTUR (`bitti` asla yazılmaz —
  `sleep 10` 2 saniyelik sınırdan önce bitmez).
- Toplam süre ~10 saniye DEĞİL, ~2-3 saniyedir — süreç gerçekten
  öldürülmüştür (`Process.Kill(entireProcessTree: true)`,
  `SkillScriptProcessRunner.cs:128-136`).

---

### MT-SKILL-061 — Çıktı sınırı: büyük çıktı kırpılır, kırpma mesajı eklenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-058'in ön koşulu geçerli, EK olarak:
  ```bash
  dotnet user-secrets set "Tracon:Skills:Scripts:MaxOutputBytes" "100"
  ```
- `scriptli-skill`'e `buyuk-cikti` script'i ekle
  (`content: "python3 -c \"print('x' * 5000)\""`, uzantı `sh` — betik
  kabuktan `python3`'ü çağırır, ayrı bir yorumlayıcı kaydı gerekmez), izin
  ver, uygulamayı yeniden başlat.

**Adımlar**
1. `manuel-script-test`'e skill'i yükleyip `buyuk-cikti`'yi çalıştırmasını
   iste, her onayı ver.

**Beklenen sonuç**
- Tool sonucunun `stdout` bölümü tam 100 bayt civarında kesilir ve satırın
  sonunda `\n[Tracon: cikti 100 bayt sinirinda kirpildi.]` metni
  görünür (`SkillScriptProcessRunner.cs:231-234`).
- Sürecin kendisi zaman aşımına UĞRAMAZ (`exit_code: 0` görünür) — kırpma ile
  zaman aşımı bağımsız kapılardır.

---

### MT-SKILL-062 — Ortam değişkenleri miras alınmaz: script yalnız `PATH`/`HOME` (+2 enjekte edilen) görür

Bu case Faz 11'in güvenlik iddiasını sınar: `secret` taşıyan ortam
değişkenleri (`OpenAI__ApiKey` gibi) script sürecine **miras geçmez**.

> **Kapsam daraltıldı (Faz 186, ölçüldü 2026-09-24):** bu bir yalıtım
> değildir. Script Tracon'la aynı OS kullanıcısıyla çalışır; Linux'ta
> ebeveynin başlangıç ortamını `/proc/<ppid>/environ` üzerinden okur
> (`alpine:3.20`, root ve uid 1000 ölçüldü). Case yalnız mirası sınar; kabul
> edilen risk `docs/MIMARI-TEHDIT-MODELI.md` § R8'dedir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-091 |

**Ön koşul**
- MT-SKILL-058'in ön koşulu geçerli.
- `scriptli-skill`'e `ortam-dokumu` script'i ekle (`content: "env | sort"`),
  `grant_script ortam-dokumu` ile izin ver (§6 adım 4).

**Adımlar**
1. `manuel-script-test`'e skill'i yükleyip `ortam-dokumu`'nu çalıştırmasını
   iste, her onayı ver.

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §9) — gerçek çıktı 4 değil 7
> satır:** `bash` kendi başlatma sürecinde `PWD`, `SHLVL`, `_` değişkenlerini
> KENDİSİ üretir (ebeveyn ortamından miras almaz — `ProcessStartInfo
> .Environment.Clear()`'dan bağımsız, kabuğun kendi iç muhasebesidir).
> Bunlar `secret` TAŞIMAZ (çalışma dizini yolu, kabuk iç içelik sayacı,
> son çalıştırılan yorumlayıcının yolu) — güvenlik iddiası (hiçbir
> Tracon-özel/`secret` değişkeni sızmaz) TAM olarak doğrulandı, yalnız
> "yalnız 4 değişken" sayımı eksikti.
- `stdout` çıktısı `PATH`, `HOME`, `TRACON_SKILL_TEMP=<gecici-dizin>`,
  `TRACON_SKILL_NAME=scriptli-skill` satırlarını İÇERİR (bunlar
  Tracon'in açıkça geçirdiği/izin verdiği tek değişkenlerdir —
  `SkillScriptProcessRunner.cs:92-104`, `EnvironmentAllowList` varsayılanı
  `["PATH","HOME"]`), EK olarak bash'in kendi ürettiği `PWD`, `SHLVL`, `_`
  satırları da görünür.
- `OpenAI__ApiKey`, `Tracon__PostgreSql__ConnectionString` gibi hiçbir
  Tracon-özel ortam değişkeni ÇIKTIDA GÖRÜNMEZ — `ProcessStartInfo.Environment.Clear()`
  çağıran süreci komple boşaltır.

~~Eski beklenti (eksik — bash'in kendi otomatik değişkenlerini
saymıyordu): stdout çıktısı YALNIZ PATH, HOME, TRACON_SKILL_TEMP,
TRACON_SKILL_NAME satırlarını içerir.~~

---

### MT-SKILL-063 — Kiracı başına eşzamanlılık sınırı: 3. eşzamanlı çağrı ilk ikisi bitene kadar BEKLER

Bu bir zamanlama tabanlı case'dir — sonuç bir hata DEĞİL, bir gecikmedir.
`SkillScriptConcurrencyLimiter` sınırı aşan çağrıyı REDDETMEZ, sırada
BEKLETİR (`SkillScriptConcurrencyLimiter.cs:25-42`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-058'in ön koşulu geçerli (varsayılan `MaxConcurrentPerTenant: 2`).
- `scriptli-skill`'e `bekleyen` script'i ekle (`content: "sleep 4"`),
  `grant_script bekleyen` ile izin ver (§6 adım 4).

**Adımlar**
1. Üç ayrı Playground sohbetinde (aynı kiracı `default`), üçünü de HEMEN
   ardışık başlatarak `bekleyen` script'ini çalıştırmasını iste (her onayı
   hızlıca ver) ve her turun ne zaman `done` olduğunu not et.

**Beklenen sonuç**
- İlk iki çağrı ~4 saniye içinde biter.
- Üçüncü çağrı ~8 saniye civarında biter (ilk ikisinden biri bitip
  semaforu bırakana kadar bekler) — `exit_code`'u yine `0`'dır, bir hata
  ALMAZ, yalnız GEÇ tamamlanır.

---

# 7 — Gözlemlenebilirlik (Faz 11)

### MT-SKILL-070 — `execute_skill_script` span'i doğru öznitelikleri taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | — |

**Ön koşul**
- MT-SKILL-058 geçti, OpenTelemetry konsol/dosya exporter'ı ile izleniyor
  (bkz. `12-GOZLEMLENEBILIRLIK-MALIYET.md`'nin genel OTel kurulum notu).
  > **2026-08-15 sapma (izin verilen, daha güçlü kanıt):** ayrı bir OTel
  > exporter kurmak yerine `Tracon:Observability:SuccessSampleRatio=1`
  > ile başarılı run'ların da iz tuttuğu garanti edildi, span'ler
  > Tracon'in KENDİ kalıcı iz deposundan `GET /api/runs/{id}/trace`
  > ile okundu (`12-GOZLEMLENEBILIRLIK-MALIYET.md` §7'nin MT-OBS-015/016'da
  > zaten kullandığı yöntem) — konsol/dosya exporter'ından ayrıştırmaktan
  > daha güvenilir.

**Beklenen sonuç**
- Span adı `execute_skill_script`.
- Öznitelikler: `tracon.skill.name = "scriptli-skill"`,
  `tracon.script.name = "merhaba"`, `tracon.script.exit_code = 0`,
  `tracon.script.duration_ms` pozitif bir sayı taşır
  (`TraconDiagnostics.cs:31,106,109,112,115`).

---

### MT-SKILL-071 — Denetim izine yazılamayan bir onay kararı UYGULANMAZ (kapsam dışı not)

Bu case `PROMPT.md` §6'nın "doldurma yapılmaz" kuralı gereği **yazılmadı**.
`SandboxedSkillScriptRunner.WriteAuditOrThrowAsync` (K-089) ve
`ApprovalEndpoints.WriteAuditOrThrowAsync` (aynı desen) denetim izi
yazımının script çalıştırmadan/onay kararından ÖNCE ve zorunlu olduğunu
garanti eder — ama bunu elle güvenilir biçimde tetiklemek audit deposunun
(aynı zamanda agent/run deposu olan) veritabanını GEÇİCİ olarak bozmayı
gerektirir, bu da o sıradaki HER case'i etkiler. `03-KALICILIK-POSTGRESQL.md`'nin
migration atomikliği notuyla AYNI gerekçeyle dışarıda bırakıldı — güvence
otomatik testlere (`SandboxedSkillScriptRunnerTests`) bırakılmıştır.

---

# 8 — İçeriğe Pinli Grant ve Platform Yetkisi (Faz 186)

Grant içeriği pinler (K-860), çok kiracılı host'ta stored script grant'ı
platform yetkisi ister (K-861), sınırın adı "Script execution gates"tir
(K-862). Ortak ön koşul §6'dadır.

### MT-SKILL-064 — Grant stored script'in içeriğini pinler: okunan hash'le `201`, gerçek run `merhaba-tracon` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.A_grant_pins_the_script_content_it_was_given_for` · `SkillScriptContentPinTests.The_grant_audit_entry_carries_the_pinned_hash` |

**Ön koşul**
- §6'nın 1-3. ve 5. adımları uygulandı; `scriptli-skill`/`merhaba` için
  etkin grant yok.

**Adımlar**
1. Hash'i oku.
2. Hash'le grant ver.
3. Grant listesini oku.
4. Playground'da `manuel-script-test`'e `merhaba scriptini calistir ve
   ciktisini yaz` gönder; her onayı ver. 👤

**Girilecek veri**
```bash
curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | jq '{origin, scriptSetHash, scripts: [.scripts[] | {name, contentHash}]}'
grant_script merhaba
curl -s "$APU/api/skill-script-grants" -H "$APB" | jq '.[] | {skillName, scriptName, contentHash, revokedAt}'
```

**Beklenen sonuç**
- Adım 1: `origin: "Database"`; `contentHash` 64 büyük harf onaltılık
  karakterdir.
- Adım 2: `HTTP: 201`; gövdedeki `contentHash` adım 1'deki değerdir.
- Adım 3: satır aynı `contentHash`'i taşır.
- Adım 4: yanıt `merhaba-tracon` içerir.

**Doğrulama sorgusu** *(PostgreSQL izleğinde)*
```sql
SELECT content_hash FROM tracon.skill_script_grants
WHERE skill_name = 'scriptli-skill' AND revoked_at IS NULL;
-- adım 1'deki hash; script.grant denetim kaydının after alanı da onu taşır.
```

> **Ölçüldü (2026-09-24, Faz 186, örnek uygulama + `gpt-5.4-mini`):** `201`;
> listedeki hash okunanla aynı; run çıktısı `merhaba-tracon`.

---

### MT-SKILL-065 — İçerik değişince aynı grant script'i çalıştırmaz; `script.denied` "content changed since the grant" der

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.A_grant_pins_the_script_content_it_was_given_for` · `SkillScriptContentPinTests.A_skill_recreated_with_other_content_does_not_run_under_the_old_grant` |

**Ön koşul**
- MT-SKILL-064 geçti.

**Adımlar**
1. Script'in içeriğini değiştir (v2).
2. YENİ bir Playground sohbetinde aynı promptu gönder; her onayı ver. 👤

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
  "resources": [],
  "scripts": [{ "name": "merhaba", "extension": "sh", "content": "echo degisti", "parametersSchema": null }]
}'
```

**Beklenen sonuç**
- Adım 1: `HTTP: 200`; `scripts[0].contentHash` değişti.
- Adım 2: script çalışmaz; yanıtta ne `merhaba-tracon` ne `degisti` vardır.
  Denetim izindeki son `script.denied` kaydının `after` alanı
  `The script content changed since the grant. Review it and grant it again.`
  taşır.

> **Ölçüldü (2026-09-24):** run tamamlandı, çıktıda `merhaba-tracon` yok;
> `script.denied` sebebi yukarıdaki metin.

---

### MT-SKILL-066 — Eski hash'le grant `409` döner, liste değişmez, yanıt güncel hash'i taşımaz

Negatif senaryo — okuma ile grant arasındaki pencere (TOCTOU).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.A_grant_for_content_that_changed_after_it_was_read_is_refused_and_writes_nothing` |

**Ön koşul**
- MT-SKILL-065 geçti; `HASH` MT-SKILL-064'ün adım 1'inde okunan v1 hash'idir.

**Girilecek veri**
```bash
curl -s "$APU/api/skill-script-grants" -H "$APB" > /tmp/once.json
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
     -H "content-type: application/json" \
     -d "{ \"skillName\": \"scriptli-skill\", \"scriptName\": \"merhaba\", \"expectedContentHash\": \"$HASH\" }"
curl -s "$APU/api/skill-script-grants" -H "$APB" | diff - /tmp/once.json && echo "liste ayni"
```

**Beklenen sonuç**
- `HTTP: 409`, `title: "Content changed"`; `detail` skill'i yeniden okuyup
  gözden geçirmeyi söyler, güncel hash'i **taşımaz**.
- `liste ayni` basılır; `script.grant` denetim kaydı yazılmaz.

> **Ölçüldü (2026-09-24):** `409 Content changed`; liste aynı; gövde
> güncel hash'i taşımadı.

---

### MT-SKILL-067 — Stored skill'e hash'siz ya da biçimsiz hash'le grant `400` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillScriptGrantTests.A_stored_skill_grant_without_a_well_formed_hash_is_refused_and_writes_nothing` |

**Ön koşul**
- §6'nın 1-3. adımları uygulandı.

**Girilecek veri**
```bash
for body in '{ "skillName": "scriptli-skill", "scriptName": "merhaba" }' \
            '{ "skillName": "scriptli-skill", "scriptName": "merhaba", "expectedContentHash": "abc" }'; do
  curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
       -H "content-type: application/json" -d "$body"
done
```

**Beklenen sonuç**
- İki istek de `HTTP: 400`, `title: "Content hash required"`; `detail`
  `GET /api/skills/scriptli-skill`'i ve `scripts[].contentHash`/`scriptSetHash`
  alanlarını adlandırır.
- Grant listesi değişmez; `script.grant` denetim kaydı yazılmaz.

> **Ölçüldü (2026-09-24, örnek uygulama):** hash'siz istek
> `400 Content hash required`.

---

### MT-SKILL-068 — Skill'in tamamına verilen grant küme izini pinler: yeni script eklenince iki script de çalışmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.Skill_wide_grant_refuses_every_script_after_a_script_is_added` · `SkillScriptContentPinTests.Skill_wide_grant_refuses_the_remaining_script_after_a_script_is_removed` |

**Ön koşul**
- §6'nın 1-3. ve 5. adımları uygulandı; `scriptli-skill` yalnız `merhaba`'yı
  taşır.
- Script adı OLMADAN grant verildi: `grant_script ""` → `HTTP: 201`
  (`expectedContentHash` = `scriptSetHash`).

**Adımlar**
1. İkinci bir script ekle.
2. Yeni bir sohbette `ikinci scriptini calistir` gönder; sonra yeni bir
   sohbette `merhaba scriptini calistir` gönder. 👤

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
  "resources": [],
  "scripts": [
    { "name": "merhaba", "extension": "sh", "content": "echo merhaba-tracon", "parametersSchema": null },
    { "name": "ikinci", "extension": "sh", "content": "echo ikinci-script", "parametersSchema": null }
  ]
}'
```

**Beklenen sonuç**
- Adım 1: `HTTP: 200`; `scriptSetHash` değişti.
- Adım 2: iki script de çalışmaz — değişmeyen `merhaba` dahil. İki
  `script.denied` kaydı `The script content changed since the grant.` ile
  başlar. Küme izi eklemeyi, silmeyi, yeniden adlandırmayı ve içerik
  değişikliğini birlikte kapsar.

---

### MT-SKILL-069 — Faz öncesi (hash'siz) grant yükseltmeden sonra stored script'i yetkilendirmez

PostgreSQL izleği. Yükseltme, eski satırı `content_hash = NULL` bırakır;
bilerek doldurulmaz.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillScriptGrantContentHashMigrationTests.Existing_grants_survive_with_no_content_hash_and_take_one_when_granted_again` · `SandboxedSkillScriptRunnerTests.A_grant_that_pins_no_content_refuses_a_stored_script` |

**Ön koşul**
- Yükseltmeden ÖNCE verilmiş bir grant. Elle üretmek için, yeni sürümü
  başlatmadan önce eski biçimde bir satır yaz:
  ```sql
  INSERT INTO tracon.skill_script_grants
      (id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at)
  VALUES (gen_random_uuid(), 'default', 'scriptli-skill', 'merhaba', 'faz-oncesi', now(), NULL, NULL);
  ```

**Adımlar**
1. Uygulamayı yeni sürümle başlat (migration `0053` koşar).
2. Satırı sorgula.
3. §6'nın 1-3. ve 5. adımlarını uygula (4. adımı ATLA), sonra Playground'da
   `merhaba scriptini calistir` gönder. 👤
4. `grant_script merhaba` ile yeniden grant ver, aynı promptu yeni sohbette
   gönder. 👤

**Doğrulama sorgusu**
```sql
SELECT max(id) FROM tracon.__migrations WHERE set_name = 'core';        -- 53
SELECT content_hash IS NULL FROM tracon.skill_script_grants
WHERE granted_by = 'faz-oncesi';                                        -- t
```

**Beklenen sonuç**
- Adım 2: `53` ve `t`.
- Adım 3: script çalışmaz; `script.denied` sebebi `The execution grant does
  not pin the script content. Grant it again with the content hash.`
- Adım 4: aynı satır güncellenir (`content_hash` dolar), script çalışır ve
  yanıt `merhaba-tracon` içerir.

> **Ölçüldü (2026-09-24, örnek uygulama + `gpt-5.4-mini`):** adım 2 `53`/`t`;
> adım 3'te script çalışmadı, sebep yukarıdaki metin.

---

### MT-SKILL-072 — Çok kiracılı host: kiracıya bağlı `SecurityAdmin` + `AgentsRead` anahtarı hash okur, stored grant veremez (`403`)

Negatif senaryo, kiracı sınırı. Script Tracon'un OS kimliğiyle çalışır ve
başka kiracıların verisine ulaşabilir; bu yüzden grant kurulum düzeyinde bir
karardır (K-861).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-861 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.A_tenant_bound_key_cannot_grant_a_stored_script_in_a_multi_tenant_host` · `SkillScriptGrantTests.A_disabled_stored_skill_still_needs_platform_authority_in_a_multi_tenant_host` |

**Ön koşul**
- §6'nın 1-2. adımları; çok kiracılı kurulum (`13-KIRACI-VE-GUVENLIK.md`):
  ```bash
  dotnet user-secrets set "Tracon:Tenancy:Enabled" "true"
  dotnet user-secrets set "Tracon:Tenancy:AllowHeaderResolution" "true"
  ```
- Kiracı `acme`'de `scriptli-skill`/`merhaba` kayıtlı (§6 adım 3'ün isteği
  `-H "X-Tracon-Tenant: acme"` ile).
- `acme`'ye bağlı, yalnız `SecurityAdmin` ve `AgentsRead` taşıyan bir anahtar:
  ```bash
  KEY=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "X-Tracon-Tenant: acme" \
        -H "content-type: application/json" \
        -d '{ "name": "acme-grant", "scopes": ["SecurityAdmin", "AgentsRead"] }' | jq -r '.plaintextKey')
  ```

**Girilecek veri**
```bash
HASH=$(curl -s -w "" "$APU/api/skills/scriptli-skill" -H "Authorization: Bearer $KEY" | jq -r '.scripts[0].contentHash')
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "Authorization: Bearer $KEY" \
     -H "content-type: application/json" \
     -d "{ \"skillName\": \"scriptli-skill\", \"scriptName\": \"merhaba\", \"expectedContentHash\": \"$HASH\" }"
curl -s "$APU/api/skill-script-grants" -H "$APB" -H "X-Tracon-Tenant: acme" | jq length
```

**Beklenen sonuç**
- Hash okunur (`GET` `200`).
- Grant `HTTP: 403`; `acme`'nin grant listesi boştur (`0`).

---

### MT-SKILL-073 — Çok kiracılı host: `PlatformAdmin` de taşıyan anahtar stored grant verir (`201`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-861 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.A_key_with_platform_authority_grants_a_stored_script_in_a_multi_tenant_host` · `SkillScriptContentPinTests.The_static_token_grants_a_stored_script_in_a_multi_tenant_host` · `SkillScriptContentPinTests.A_claims_admin_needs_the_registered_platform_policy_to_grant_a_stored_script` |

**Ön koşul**
- MT-SKILL-072'nin kurulumu; anahtar `["SecurityAdmin", "AgentsRead", "PlatformAdmin"]`
  kapsamlarıyla üretilir. (`PlatformAdmin` taşıyan anahtarı yalnız platform
  yetkisi olan bir kimlik üretebilir — statik token bu kimliktir.)

**Adımlar**
1. MT-SKILL-072'nin iki isteğini bu anahtarla tekrarla.
2. Statik token + `X-Tracon-Tenant: acme` ile aynı grant'ı dene (önce
   `DELETE` ile kaldır).

**Beklenen sonuç**
- Adım 1: hash `200`; grant `HTTP: 201`.
- Adım 2: `HTTP: 201`. Platform operatörü kiracı B adına grant'ı bu iki
  kimlikten biriyle ya da `Tracon.PlatformAdmin` policy'sinin kabul ettiği
  bir kullanıcıyla verir.

---

### MT-SKILL-074 — Tek kiracılı host: `SecurityAdmin` + `AgentsRead` anahtarı platform yetkisi olmadan grant verir (`201`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-861 |
| **Devir** | ➜ CI: `SkillScriptContentPinTests.A_single_tenant_host_needs_no_platform_authority_for_a_stored_script_grant` |

**Ön koşul**
- §6'nın 1-3. adımları; `Tracon:Tenancy:Enabled` AYARLANMAMIŞ.
- Anahtar: `["SecurityAdmin", "AgentsRead"]` (kiracı başlığı yok).

**Adımlar**
1. Anahtarla hash'i oku ve grant ver (MT-SKILL-072'nin istekleri, başlıksız).

**Beklenen sonuç**
- `HTTP: 201`. Platform kontrolü yalnız çok kiracılı host'ta uygulanır: tek
  kiracılı kurulumda sunucunun kimliği ile kiracının sınırı aynıdır.

---

### MT-SKILL-075 — Bellek içi mod: iptal edilen grant listede `revokedAt` dolu olarak kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-092 |
| **Devir** | ➜ CI: `SkillScriptGrantTests.A_revoked_grant_stays_listed_with_its_revocation_time` · `SkillScriptGrantContract.Revoked_grant_stays_listed` |

**Ön koşul**
- Kalıcılık sağlayıcısı YOK (bellek içi); §6'nın 1-4. adımları.

**Girilecek veri**
```bash
curl -s -o /dev/null -w "HTTP: %{http_code}\n" -X DELETE "$APU/api/skill-script-grants/scriptli-skill?scriptName=merhaba" -H "$APB"
curl -s "$APU/api/skill-script-grants" -H "$APB" | jq '.[] | {skillName, scriptName, revokedAt}'
```

**Beklenen sonuç**
- `HTTP: 204`; satır listede kalır ve `revokedAt` doludur (SQL
  sağlayıcılarıyla aynı). Önceden bellek içi store satırı silerdi ve "kim neye
  izin verdi" kaydı kaybolurdu.
- Aynı `DELETE` ikinci kez `404` döner.

---

### MT-SKILL-076 — Arayüz: bayat grant `stale` rozeti taşır; form içeriği gösterir ve yalnız okunan içeriği grant eder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-860 |
| **Devir** | ➜ CI: `SkillTests.A_grant_pins_the_reviewed_content_and_turns_stale_when_it_changes` · `SkillTests.A_grant_for_content_that_changed_after_the_review_is_refused` |

**Ön koşul**
- MT-SKILL-065 geçti (grant v1'e pinli, içerik v2).

**Adımlar**
1. `http://localhost:5080/tracon/skills` → "Script execution grants" paneli.
2. Forma `scriptli-skill` ve `merhaba` yaz, **Review content**'e bas.
3. **Grant**'a bas.
4. Forma var olmayan bir ad yaz (`yok-boyle-skill`), **Review content**'e bas.

**Beklenen sonuç**
- Adım 1: satırın **Pin** sütunu `stale` (sarı) rozetini taşır; rozetin
  açıklaması script'in bu grant altında çalışmadığını söyler.
- Adım 2: "What this grant will run" bölümü `merhaba.sh`'nin içeriğini
  (`echo degisti`), kaynağı ("Read from the database") ve `Content hash`'i
  gösterir.
- Adım 3: satır `current` (yeşil) olur; form sıfırlanır.
- Adım 4: bölüm "No stored or code skill has this name…" der; grant yalnız
  diskteki script'leri yetkilendirir.
- `TR` dilinde aynı metinler: `Pin`, `bayat`/`güncel`, `İçeriği incele`.

> **Ölçüldü (2026-09-24, örnek uygulama, tarayıcı):** dört adım ve Türkçe
> metinler beklendiği gibi.

---

### MT-SKILL-077 — Site: sınır "Script execution gates" adını taşır; hiçbir sayfa script sandbox'ı vaat etmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 186 |
| **İlgili karar** | K-862 |

**Adımlar**
1. `cd docs-site && npm run build && node scripts/check-content.mjs`.
2. Metni tara.

**Girilecek veri**
```bash
grep -c "Script execution gates" docs-site/src/content/docs/reference/threat-model.md \
     docs-site/src/content/docs/getting-started/security.md \
     docs-site/src/content/docs/reference/security-policy.md SECURITY.md
grep -rn -i "script sandboxing\|escaping its sandbox\|inside its sandbox\|isolated operating-system process" \
     SECURITY.md docs-site/src/content/docs docs-site/public/llms-full.txt
```

**Beklenen sonuç**
- Adım 1 temiz: `check-content.mjs` kapsam satırını iki politika dosyasında,
  yasak ifadeleri her sayfada ve `threat-model.md`'nin "Accepted risks"
  bölümündeki işletim sistemi kimliği riskini denetler.
- Adım 2: dört dosyada sayı ≥ 1; ikinci `grep` boş döner.
