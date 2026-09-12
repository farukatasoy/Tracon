# Faz 10 — Agent Skill'leri (script'siz)

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-09** (1/2)
> **Önkoşul:** [Faz 9](09-YONETISIM-VE-DENETIM-IZI.md) — denetim izi dekoratörü hazır olmalı
> **Sonraki:** [Faz 11](11-SKILL-SCRIPT-CALISTIRMA.md) — script çalıştırma
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0003 (planlanan sırada)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/10-AGENT-SKILLERI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir agent'a çalışma anında yüklenen, markdown tabanlı yetenek paketleri verilebilsin. Claude Code'un skill mekanizmasının MAF karşılığıdır ve MAF 1.16.0 içinde **hazırdır**. Bu faz **yalnız talimat ve kaynak** taşıyan skill'leri destekler. Script çalıştırma Faz 11'dir ve ayrı bir güvenlik kararıdır. ---

## Bu Fazda Verilecek Kararlar

1. **Skill onayları kapatılmaz** (`Disable*Approval` hep `false`) — skill
   yükleme, agent'ın talimatını çalışma anında değiştirmektir.
2. **Skill → agent bağlantısı `AgentDefinition.SkillNames` içinde** — ayrı
   bağlantı tablosu sürüm geri almayı bozardı.
3. **Bu fazda `AgentFileSkill` kullanılmaz** — dosya kaynağı script çalıştırıcı
   ister; o Faz 11'dir.
4. **Önbellek anahtarına skill parmak izi eklendi** — aksi hâlde düzenleme
   çalışan sürece yansımaz.
5. **Markdown arayüzde render edilmez** — bundle bütçesi.

---

## Gerçekleşen Uygulama

### Public API

- `AgentSkillDefinition`, `AgentSkillResourceDefinition` ve `IAgentSkillStore`
  `Tracon.Abstractions/Skills/` altında eklendi.
- `AgentDefinition.SkillNames` agent tanımının ve PostgreSQL JSON yükünün parçasıdır.
- `ITraconBuilder.AddSkill(AgentSkillDefinition)` kod kayıtlarını ekler.
- `TraconSkillOptions`: `MaxSkillsPerAgent = 10`, instructions için 64 KB,
  kaynak içeriği için 256 KB ve skill başına 20 kaynak sınırı taşır.
- Yönetim API'si `GET/PUT/DELETE {prefix}/api/skills` uçlarını sağlar. `PUT`
  oluşturma ve güncelleme için aynı uçtur; doğrulama MAF
  `AgentSkillFrontmatter.Validate*` metotlarını kullanır.

### Çalışma Zamanı ve Kalıcılık

- `AgentSkillCatalog`, kod kayıtlarını tenant store'un önünde çözer. Aynı ad
  için kod kaydı kazanır. Bilinmeyen ad `TraconCompilationException` üretir;
  kapalı skill listede kalır ama MAF'a verilmez.
- `TraconSkillsSource`, her tanımı `AgentInlineSkill` ve kaynaklarına çevirir.
  Düz agent `AgentSkillsProvider` ile `AIContextProviders` üzerinden; harness
  `AgentSkillsSource` üzerinden bağlanır. Hiçbir `Disable*Approval` bayrağı
  ayarlanmaz.
- Cache anahtarı `(agent name, definition version, skill fingerprint)` biçimindedir.
  Parmak izi tenant, tanımdaki skill sırası, sürüm ve `UpdatedAt` değerlerinden
  SHA-256 ile üretilir; skill düzenlemesi sonraki çözümlemede yeni agent üretir.
- Migration `0003_agent_skills.sql`, `agent_skills` ve cascade bağlı
  `agent_skill_resources` tablolarını ekler. `PostgresAgentSkillStore` metadata
  için kaynak üretilmiş JSON bağlamını kullanır.

### Arayüz ve Gerçek Model Kanıtı

- `Skills` ekranı liste, frontmatter düzenleme, markdown gövde ve kaynak
  ekleme/çıkarma sağlar; markdown render edilmez. Agent düzenleyicisi etkin
  skill'leri checkbox ile seçer ve 10 seçimde yeni seçimi kapatır.
- Örnek uygulama, `invoice-guidance` skill'i ve `skill-proof` OpenRouter agent'ı
  ile çalıştırıldı. İlk SSE turu
  `response.function_approval.requested` içinde
  `load_skill({"skillName":"invoice-guidance"})` döndürdü. Playground'da
  onaydan sonraki tur `load_skill done` sonucu içinde skill talimatını gösterdi
  ve model tam olarak `INVOICE_SKILL_ACTIVE` yanıtını üretti.

### Testler

- Core: `InMemoryAgentSkillStoreTests`, `AgentSkillCatalogTests` ve
  `CompiledAgentCacheTests` kimlik/sürüm, tenant izolasyonu, code-over-database,
  disabled skill, bilinmeyen skill ve cache parmak izini doğrular.
- PostgreSQL: `SkillStoreTests` kaynak okuma/cascade ve tenant izolasyonunu gerçek
  container üzerinde doğrular.
- HTTP: `SkillCrudTests` CRUD, MAF geçersiz ad ve sürüm artışını doğrular.
- UI: `UiTests.Arayuzden_skill_olusturulur_ve_listelenir` skill oluşturmayı;
  navigation testi `Skills` bağlantısını doğrular.

---

## Bitiş Ölçütleri (DoD)

- [x] Arayüzden skill oluşturulur ve agent düzenleyicisinden bağlanır
- [x] Gerçek OpenRouter modelinde skill yükleme onay ister; onaydan sonra
  talimat bağlama girer (`INVOICE_SKILL_ACTIVE`)
- [x] Skill düzenlemesi cache parmak iziyle yeni derlemeyi zorlar
- [x] İki tenant senaryosunda skill sızıntısı yok
- [x] Kaynaklar skill ile birlikte döner; yükleme/okuma MAF onay zincirindedir
- [x] Harness yolu bağlandı; K-053 nedeniyle gerçek model doğrulaması düz
  `ChatClientAgent` ile yapıldı
- [x] Dört doğrulama kapısı sıfır uyarı; bundle 95.1 KB gzip

---

## Sonraki Faza Devir Notu

- Faz 11 bu fazın `AgentSkillsSource` zincirine **script'li bir kaynak** ekler.
  Zincir bu yüzden `AggregatingAgentSkillsSource` ile kurulmalıdır; tek kaynak
  varsayımı yapmayın.
- `AgentSkillsProviderOptions.DisableRunSkillScriptApproval` alanı Faz 11'in
  konusudur ve **yine `false` kalacaktır**.
- Boyut sınırlarını taşıyan `TraconSkillOptions` Faz 11'de script sınırları
  (zaman aşımı, çıktı boyutu) ile genişler.
