# 14 — Skill ve Script Çalıştırma (`SKILL`)

> **Alan kodu:** `SKILL` · **Faz:** 10, 11
> **Kaynak:** `src/AgentPrism.Abstractions/Skills/` (tümü) ·
> `src/AgentPrism.Core/Skills/` (tümü: `AgentSkillCatalog`, `AgentPrismSkillsSource`,
> `CodeSkillRegistration`, `AgentPrismSkillScriptBuilderExtensions`,
> `Scripts/SandboxedSkillScriptRunner`, `Scripts/SkillScriptProcessRunner`,
> `Scripts/SkillScriptArgumentValidator`, `Scripts/SkillScriptConcurrencyLimiter`) ·
> `src/AgentPrism.Core/Storage/InMemoryAgentSkillStore.cs`,
> `InMemorySkillScriptGrantStore.cs` · `src/AgentPrism.Core/Audit/AuditingSkillScriptGrantStore.cs` ·
> `src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs` (yalnız skill/script
> kablolaması — genel derleme akışı `02-CEKIRDEK-VE-KATALOG.md`'nin işi) ·
> `src/AgentPrism.Core/Compilation/AgentDefinitionValidator.cs` (yalnız `CheckSkillsAsync`/
> `CheckStructureAsync`) · `src/AgentPrism.AspNetCore/Endpoints/SkillEndpoints.cs`,
> `SkillScriptGrantEndpoints.cs` · `src/AgentPrism.AspNetCore/Contracts/AgentContracts.cs`
> (yalnız `AgentSkillRequest`/`SkillScriptGrantRequest`) ·
> `src/AgentPrism.PostgreSql/Migrations/0003_agent_skills.sql`,
> `0004_skill_scripts.sql` · `src/AgentPrism.UI/frontend/src/screens/skills.tsx` ·
> `src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx` (yalnız skill seçici bölümü) ·
> `samples/AgentPrism.Api/Program.cs` (yalnız `agentPrism` değişkeni ve builder zinciri).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/14-SKILL-VE-SCRIPT.md`](kosumlar/2026-08-13/14-SKILL-VE-SCRIPT.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

Faz 10, çalışma anında bir agent'a yüklenen markdown tabanlı **skill**'leri
(`load_skill`/`read_skill_resource`, MAF onay zincirinden geçer) kanıtlar. Faz
11 bunun üzerine **script çalıştırmayı** ekler — K2 kuralının ("tool'lar yalnız
kodda tanımlanır") MCP'den (K-058) sonraki **ikinci bilinçli istisnası**: script
kodu, AgentPrism'in kendi makinesinde, ayrı bir OS sürecinde çalışır.

```mermaid
flowchart TD
    A["POST/PUT api/skills"] --> B["IAgentSkillStore<br/>tenant basina kayit"]
    B --> C["AgentSkillCatalog<br/>kod > store, MaxSkillsPerAgent"]
    C --> D["AgentPrismSkillsSource<br/>AgentSkillDefinition -> AgentInlineSkill"]
    D --> E["AgentSkillsProvider<br/>onaylar HEP ACIK"]
    E --> F["Model: load_skill / read_skill_resource / script"]

    G["PUT api/skills/name Scripts[]"] --> H{"Extension<br/>Interpreters'ta mi?"}
    H -- "hayir" --> HX["400 kayit reddedilir"]
    H -- "evet" --> I["Kayit basarili<br/>CALISTIRILAMAZ henuz"]
    I --> J{"scripts.Enabled +<br/>AllowStoredScripts?"}
    J -- "hayir" --> JX["Model script'i hic gormez"]
    J -- "evet" --> K{"skill_script_grants<br/>gecerli izin var mi?"}
    K -- "hayir" --> KX["403/AgentPrismException<br/>script.denied"]
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
| Rol matrisi testi (Reader/Admin, `AgentPrismPolicies`) | **Hiçbir dosyaya atanmamış** — bkz. aşağıdaki not |
| `cancel_order` gibi normal tool onayları, "Hatırla" kalıcı kural mekanizması | `10-ARAYUZ-AGENT-PLAYGROUND.md` (zaten üretildi) — burada yalnız `load_skill`'e özgü fark not edilir |
| MCP tool'larının onay akışı | `18-MCP-VE-A2A.md` |
| `PatternContentGuard`/içerik engeli | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` |
| Genel maliyet/kota gözlemlenebilirliği | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) |

> **Rol matrisi bu dosyada test edilmez.** `SkillEndpoints`/`SkillScriptGrantEndpoints`
> `RequireRole(roles.Reader)`/`RequireRole(roles.Admin)` kullanır
> (`RoleEndpointConventionBuilderExtensions.cs:24-29`), ama bu bir NO-OP'tur:
> `AgentPrismPolicies.Reader`/`.Admin` tüketicinin `AuthorizationOptions`'ında
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
3. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.
4. Örnek uygulama **hiçbir skill, script veya çalıştırma izni tanımlamaz**
   (`grep -n "Skill" samples/AgentPrism.Api/Program.cs` boş döner) ve
   `.UseSkillScripts(...)` hiç çağrılmaz — script çalıştırma varsayılan olarak
   **tamamen kapalıdır**. §6, bunu açmak için geçici bir kod değişikliği ister;
   o bölümün başında ayrıca belirtilir.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
```

> **Gerçek para uyarısı.** Yalnız §3 (gerçek model ile skill yükleme onayı)
> Playground üzerinden gerçek bir `run` başlatır ve küçük ölçüde OpenAI ücreti
> doğurur. §1, §2, §4, §5, §6, §7'nin geri kalanı hiçbir model çağırmaz.

---

## Bu dosyanın yerel fixture'ları

Bu veriler yalnız bu dosyaya özgüdür, `00-INDEKS.md`'ye girmez (`PROMPT.md` §4.2).

| Kimlik | Değer |
|---|---|
| `FIX-SKILL-FATURA` | Ad `fatura-kontrolu` · Açıklama `Fatura kontrol kurallarini ve KDV hesaplamasini aciklar.` · Talimat aşağıda |
| `FIX-SKILL-PROMPT` | `Fatura kontrol kurallarini uygulayarak yardim et` → `load_skill` çağrısı bekler |
| `FIX-SCRIPT-MERHABA` | Ad `merhaba` · Uzantı `sh` · İçerik `echo merhaba-agentprism` |
| `FIX-AGENT-SKILL` | Ad `manuel-skill-test` · Talimat `Sen bir yardimci asistansin.` · `skillNames: ["fatura-kontrolu"]` · Tool yok |

`FIX-SKILL-FATURA`'nın `instructions` alanı (deterministik bir işaretçi taşır,
model bunu birebir yazınca skill'in gerçekten bağlama girdiği kanıtlanır):

```text
Bu bir fatura kontrol skill'idir. Bu talimati okudugunda, baska hicbir sey
eklemeden tam olarak su metni yaz: FATURA_SKILL_ACTIVE
```

---

# 1 — Skill CRUD ve Frontmatter Doğrulama (Faz 10)

Doğrulama kuralları AgentPrism'in kendi icadı değildir — MAF'ın
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
SELECT count(*) FROM agentprism.agent_skill_resources
WHERE skill_id = (SELECT id FROM agentprism.agent_skills WHERE name = 'test-kaynakli');
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
  `instructions en fazla 65536 bayt olabilir.` (`AgentPrismSkillOptions.MaxInstructionsLength`,
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
- `http://localhost:5080/agentprism/skills` açık.

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
dotnet user-secrets set "AgentPrism:Skills:MaxSkillsPerAgent" "1"
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
  `AgentPrismException` yakalanıp `400`'e çevrilir).

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
  `AgentPrismSkillsSource` bu skill'i MAF'a hiç sunmaz.
- Model, herhangi bir skill talimatı olmadan genel bir yanıt üretir.

---

### MT-SKILL-023 — Kod tanımlı skill, aynı adlı DB kaydını geçersiz kılar

Bu case geçici bir kod değişikliği ister — `AddSkill` yalnız kodda çağrılır,
HTTP karşılığı yoktur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 10 |
| **İlgili karar** | K-003 (agent'lardaki aynı kural, skill'lere uygulanmış hali) |

**Ön koşul**
1. `FIX-SKILL-FATURA` DB'de kayıtlı (MT-SKILL-001), `instructions` alanı
   `FATURA_SKILL_ACTIVE` işaretçisini taşıyor.
2. `samples/AgentPrism.Api/Program.cs`'e, `agentPrism` değişkeni
   tanımlandıktan hemen sonra GEÇİCİ olarak ekleyin:
   ```csharp
   agentPrism.AddSkill(new AgentSkillDefinition
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
gövdesinde YOKTUR (`grep -n "MaxSkillsPerAgent" src/AgentPrism.AspNetCore/Endpoints/MetaEndpoints.cs`
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
dotnet user-secrets set "AgentPrism:Skills:MaxSkillsPerAgent" "2"
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

### MT-SKILL-040 — Varsayılan durumda (Interpreters boş) HERHANGİ bir script uzantısı reddedilir

Negatif senaryo. Sıfır kurulum gerektirir — `AgentPrismSkillScriptOptions.Interpreters`
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

### MT-SKILL-041 — `Interpreters`'a `sh` eklenince AYNI kayıt başarılı olur (yalnız config, kod değişikliği YOK)

🚨 **Doğrulanmış keşif.** `AgentPrismSkillScriptOptions.Interpreters` sözlüğü
`IConfiguration`'dan HER ZAMAN bağlanır (`AgentPrismServiceCollectionExtensions.cs:1136-1141`,
`BindSkillScripts`), `UseSkillScripts()` çağrılıp çağrılmadığından BAĞIMSIZDIR.
Faz 11 dokümanının "Seçenek A ÖNERİLEN... kökler KODDA, arayüzden DEĞİL" notu
script KÖKLERİ (`SkillRoots`) için doğrudur ama yorumlayıcı beyaz listesi de
AYNI mekanizmayla `dotnet user-secrets` üzerinden ayarlanabilir — script
KAYDI (henüz ÇALIŞTIRMA değil) bu yolla açılabilir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-088 |

**Ön koşul**
```bash
dotnet user-secrets set "AgentPrism:Skills:Scripts:Interpreters:sh" "/bin/bash"
```
Uygulama yeniden başlatılır.

**Adımlar**
1. MT-SKILL-040'daki AYNI isteği, `extension: "sh"` ile tekrarla.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
  "resources": [],
  "scripts": [{ "name": "merhaba", "extension": "sh", "content": "echo merhaba-agentprism", "parametersSchema": null }]
}'
```

**Beklenen sonuç**
- `HTTP: 201`. Kayıt DB'de durur ama `scripts.Enabled` hâlâ `false`
  (varsayılan) olduğu için HİÇBİR ŞEKİLDE çalıştırılamaz — bkz. §6.

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
  `AgentPrism:Skills:Scripts:Enabled` AYARLANMAMIŞ (varsayılan `false`).

**Adımlar**
1. `GET /api/skills/scriptli-skill` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/skills/scriptli-skill" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Gövde `scripts` dizisinde `merhaba` script'ini TAM içerikle (`content:
  "echo merhaba-agentprism"`) döner — kayıt hiçbir zaman gizlenmez, yalnız
  modele SUNULMAZ (`AgentPrismSkillsSource.CreateSkill`'in
  `_scripts is { StoredScriptsEnabled: true }` koşulu, `AgentPrismSkillsSource.cs:71`).

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
dotnet user-secrets set "AgentPrism:Skills:Scripts:Enabled" "true"
dotnet user-secrets set "AgentPrism:Skills:Scripts:PlatformIsolationAcknowledged" "true"
```
Uygulama yeniden başlatılır. (🚨 Bu ikisi TEK BAŞINA script'i
ÇALIŞTIRILABİLİR yapmaz — `UseSkillScripts()` çağrılmadığı sürece
`SkillScriptSupport` DI'da kayıtlı değildir; bkz. MT-SKILL-057. Yalnız GRANT
vermeyi mümkün kılar.)

**Adımlar**
1. MT-SKILL-050'deki isteği tekrarla.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/skill-script-grants" -H "$APB" \
     -H "content-type: application/json" -d '{ "skillName": "scriptli-skill", "scriptName": "merhaba" }'
```

**Beklenen sonuç**
- `HTTP: 201`. Gövde `grantedBy: null` (statik bearer token bir
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
2. `http://localhost:5080/agentprism/skills` sayfasını aç, en alttaki
   "Script Çalıştırma İzinleri" panelini incele.

**Beklenen sonuç**
- Adım 1: liste `scriptli-skill`/`merhaba` çiftini içerir.
- Adım 2: panelin üstünde kırmızı bir uyarı kutusu görünür
  (`data-testid` yok ama CSS sınıfı `border-red-500`,
  `skills.tsx:154-156`) ve grant tablosu aynı kaydı gösterir.

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
SELECT skill_name, script_name, revoked_at FROM agentprism.skill_script_grants
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
  dotnet user-secrets set "AgentPrism:Skills:Scripts:Enabled" "true"
  dotnet user-secrets set "AgentPrism:Skills:Scripts:PlatformIsolationAcknowledged" "true"
  ```

**Adımlar**
1. Playground'da `manuel-script-test`'e `Fatura kontrol kurallarini
   uygulayarak yardim et, ardindan merhaba script'ini calistir` gönder.
2. Onay kartı(ları) çıktıkça onayla.

**Beklenen sonuç**
- `load_skill` çağrılır ve onay ister (skill kaydı zaten normal işler).
- Onaydan SONRA modele `merhaba` adında ÇAĞRILABİLİR bir tool asla
  sunulmaz — model bunu ya hiç denemez ya da "böyle bir araç yok" anlamına
  gelen bir yanıt üretir. Sebebi: `AgentPrismSkillsSource.CreateSkill`'in
  `_scripts is { StoredScriptsEnabled: true }` koşulu `_scripts`'in kendisi
  (`SkillScriptSupport`) DI'da hiç kayıtlı olmadığı için `null`'dır — config
  bayrakları burada hiç okunmaz (`AgentPrismSkillsSource.cs:71`).
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
> `AgentPrismSkillsSource.CreateSkill`'in `skill.AddScript(script.Name, ...)`
> çağrısı (`AgentPrismSkillsSource.cs:76-81`) hâlâ AYNI şekilde script başına
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
- Script sonucu modele `exit_code: 0\nstdout:\nmerhaba-agentprism\n\n`
  biçiminde döner (`SandboxedSkillScriptRunner.Format`,
  `SandboxedSkillScriptRunner.cs:355-379`).
- Model nihai yanıtında `merhaba-agentprism` dizgisini içerir.

~~Eski beklenti (eski bir MAF sürümüne dayanıyordu): İkinci onay kartının
tool adı `merhaba`'dır (skill kaydındaki script adı aynen tool adı olur,
MAF'ın generic `run_skill_script`'i DEĞİL).~~

**Doğrulama sorgusu** *(PostgreSQL izleğinde)*
```sql
SELECT action, entity, tenant_id FROM agentprism.audit_entries
WHERE action = 'script.run' ORDER BY created_at DESC LIMIT 1;
-- entity = 'scriptli-skill/merhaba' beklenir.

SELECT tool_name, source, error FROM agentprism.tool_invocations
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
> calistirilmadi: ...`) AgentPrism'in KENDİ gözlemlenebilirlik katmanında
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
SELECT action, entity FROM agentprism.audit_entries
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
  dotnet user-secrets set "AgentPrism:Skills:Scripts:Timeout" "00:00:02"
  ```
- `scriptli-skill`'e `uyuyan` adında yeni bir script ekle (`sleep 10`):
  ```bash
  curl -s -X PUT "$APU/api/skills/scriptli-skill" -H "$APB" -H "content-type: application/json" -d '{
    "name": "scriptli-skill", "description": "Script testi.", "instructions": "test", "enabled": true,
    "resources": [],
    "scripts": [
      { "name": "merhaba", "extension": "sh", "content": "echo merhaba-agentprism", "parametersSchema": null },
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
  dotnet user-secrets set "AgentPrism:Skills:Scripts:MaxOutputBytes" "100"
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
  sonunda `\n[AgentPrism: cikti 100 bayt sinirinda kirpildi.]` metni
  görünür (`SkillScriptProcessRunner.cs:231-234`).
- Sürecin kendisi zaman aşımına UĞRAMAZ (`exit_code: 0` görünür) — kırpma ile
  zaman aşımı bağımsız kapılardır.

---

### MT-SKILL-062 — Ortam değişkenleri sızmaz: script yalnız `PATH`/`HOME` (+2 enjekte edilen) görür

Bu case Faz 11'in en kritik güvenlik iddiasını sınar: `secret` taşıyan
ortam değişkenleri (`OpenAI__ApiKey` gibi) script sürecine HİÇ ULAŞMAZ.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 11 |
| **İlgili karar** | K-091 |

**Ön koşul**
- MT-SKILL-058'in ön koşulu geçerli.
- `scriptli-skill`'e `ortam-dokumu` script'i ekle (`content: "env | sort"`),
  izin ver.

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
> AgentPrism-özel/`secret` değişkeni sızmaz) TAM olarak doğrulandı, yalnız
> "yalnız 4 değişken" sayımı eksikti.
- `stdout` çıktısı `PATH`, `HOME`, `AGENTPRISM_SKILL_TEMP=<gecici-dizin>`,
  `AGENTPRISM_SKILL_NAME=scriptli-skill` satırlarını İÇERİR (bunlar
  AgentPrism'in açıkça geçirdiği/izin verdiği tek değişkenlerdir —
  `SkillScriptProcessRunner.cs:92-104`, `EnvironmentAllowList` varsayılanı
  `["PATH","HOME"]`), EK olarak bash'in kendi ürettiği `PWD`, `SHLVL`, `_`
  satırları da görünür.
- `OpenAI__ApiKey`, `AgentPrism__PostgreSql__ConnectionString` gibi hiçbir
  AgentPrism-özel ortam değişkeni ÇIKTIDA GÖRÜNMEZ — `ProcessStartInfo.Environment.Clear()`
  çağıran süreci komple boşaltır.

~~Eski beklenti (eksik — bash'in kendi otomatik değişkenlerini
saymıyordu): stdout çıktısı YALNIZ PATH, HOME, AGENTPRISM_SKILL_TEMP,
AGENTPRISM_SKILL_NAME satırlarını içerir.~~

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
- `scriptli-skill`'e `bekleyen` script'i ekle (`content: "sleep 4"`), izin
  ver.

**Adımlar**
1. Üç ayrı Playground sohbetinde (aynı kiracı `default`), üçünü de HEMEN
   ardışık başlatarak `bekleyen` script'ini çalıştırmasını iste (her onayı
   hızlıca ver) ve her turun ne zaman `done` olduğunu not et.

**Beklenen sonuç**
- İlk iki çağrı ~4 saniye içinde biter.
- Üçüncü çağrı ~8 saniye civarında biter (ilk ikisinden biri bitip
  semaforu bırakana kadar bekler) — `exit_code`'u yine `0`'dır, bir hata
  ALMAZ, yalnız GEÇ tamamlanır.

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
  > exporter kurmak yerine `AgentPrism:Observability:SuccessSampleRatio=1`
  > ile başarılı run'ların da iz tuttuğu garanti edildi, span'ler
  > AgentPrism'in KENDİ kalıcı iz deposundan `GET /api/runs/{id}/trace`
  > ile okundu (`12-GOZLEMLENEBILIRLIK-MALIYET.md` §7'nin MT-OBS-015/016'da
  > zaten kullandığı yöntem) — konsol/dosya exporter'ından ayrıştırmaktan
  > daha güvenilir.

**Beklenen sonuç**
- Span adı `execute_skill_script`.
- Öznitelikler: `agentprism.skill.name = "scriptli-skill"`,
  `agentprism.script.name = "merhaba"`, `agentprism.script.exit_code = 0`,
  `agentprism.script.duration_ms` pozitif bir sayı taşır
  (`AgentPrismDiagnostics.cs:31,106,109,112,115`).

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
