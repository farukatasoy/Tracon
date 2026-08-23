# Faz 63 — Argüman Düzeyinde Onay Politikası

> **Durum:** ✅ Tamamlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-61**
> **Önkoşul:** [Faz 6](06-GOZLEMLENEBILIRLIK.md) — onay kuralı tablosu ve değerlendirici oradan gelir · [Faz 48](48-GUARDRAILS.md) — tool **argümanı** denetimini bilerek kapsam dışı bıraktı; bu faz o boşluğun sahibidir · [Faz 55](55-ASENKRON-ONAY-KUTUSU.md) — asenkron onay kutusu bu kuralların tüketicisidir
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Sql.Shared`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (PostgreSQL + SQL Server + SQLite). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — `ToolApprovalRule`'a bir alan, iki yeni tip, bir kayıt uzantısı. `PublicAPI.Shipped.txt` bugün **boş**; ekleme **bugün bedava**
> **Site etkisi:** `concepts/governance.md`, `concepts/tools.md`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Onay kuralları bugün **tool düzeyindedir**. `refund_order` ya **hep** onay ister ya **hiç** istemez. Gerçek ihtiyaç ise koşulludur: "100 TL altı otomatik geçsin, üstü insana sorulsun". Bu kabalık **onay yorgunluğu** üretir. Kullanıcı her şeyi onaylamayı öğrenir ve onay akışı — Faz 6'dan Faz 55'e kadar kurulan bütün makine — değerini kaybeder.

## Bitiş Ölçütleri (DoD)

- [x] Koşulsuz eski kurallar **birebir** eskisi gibi çalışır (sözleşme testi dört koşumda — `ToolApprovalRuleStoreContract`, InMemory+PostgreSQL+SQLite çalıştı; SQL Server izole koşumda 495/495)
- [x] `amount <= 100` koşullu kural 50'de otomatik geçer, 500'de onay ister (`ToolApprovalTests.Argument_condition_rule_matches_within_threshold`, `docs/manuel-test/13-...md` MT-SEC-101/102)
- [x] Yol bulunamazsa, tip uyuşmazsa veya koşul çözülemezse sonuç **onay istenir** (`ToolArgumentConditionMatcherTests` — 5 ayrı test, MT-SEC-103/104)
- [x] Kodda kayıtlı politika veri kuralını **ezer**; istisna atarsa `Required` döner (`ToolApprovalTests.Code_policy_required_overrides_a_matching_data_rule`, `A_throwing_code_policy_requires_approval`)
- [x] Aynı kapsam ve aynı koşul kümesiyle ikinci kural `409` ile reddedilir (`GovernanceEndpointTests.Same_scope_and_conditions_written_twice_is_a_conflict`; canlı doğrulandı — bkz. aşağı)
- [x] Üç sağlayıcıda da migration uygulanır ve sözleşme testleri geçer (PostgreSQL 0030, SQL Server 0017, SQLite 0017 — üçü de izole koşumda geçti)
- [x] Arayüzde serbest ifade kutusu **yoktur**; operatör açılır listedir (`mcp.tsx` `<Select>`, `UiTests.Approval_rule_with_condition_is_created_and_shown`)
- [x] Dört doğrulama kapısı sıfır uyarı verir (build/test/pack/format — kanıt bölümü aşağıda)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (aşağıda — CRUD; gerçek model çağrısıyla onay senaryosu yapılmadı, bkz. sapma notu)
- [x] `secret` taraması boş döndü (yalnız önceden bilinen, dokunulmamış yanlış pozitifler)
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi (MT-SEC-100…108); otomatikleştirilebilenler (100-104, 106-107) canlı `curl` ile koşuldu, sonuç aşağıda
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (bkz. "Denetim Bulguları")
- [x] `docs-site/` güncellendi (`concepts/governance.md`, `ui.md`; `concepts/tools.md` yalnız `governance.md`'ye işaret ediyor, ayrı içerik gerekmedi — plandan sapma); `npm run build` + `check:links` + `check:content` temiz
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — **167,5 KB gzip / 250 KB** (Faz 55'in 162,6 KB'ından +4,9 KB)

### Doğrulama komutları (gerçek çıktı)

`samples/AgentPrism.Api` kalıcı yerel PostgreSQL'e (`Host=localhost;Port=55432`) karşı ayağa kaldırıldı; migration `0030_approval_conditions` gerçekten uygulandı ("AgentPrism applied 1 migration(s)"). Kimlik doğrulama bu örnekte API anahtarı bekliyordu; manuel test setinin sabit test anahtarı kullanıldı.

```bash
# Kosullu kural yaz
curl -s -X POST http://localhost:5081/agentprism/api/approvals/rules \
  -H 'Content-Type: application/json' -H 'Authorization: Bearer manuel-test-token-2026' \
  -d '{"toolName":"refund_order","argumentConditions":[{"path":"amount","operator":"LessThanOrEqual","value":100}]}'
# → 201, gövde: {"id":"01a0159c-...","toolName":"refund_order","argumentConditions":[{"path":"amount","operator":"LessThanOrEqual","value":100}],...}
# 🚨 "operator" DİZE olarak döner ("LessThanOrEqual"), sayı DEĞİL — JsonStringEnumConverter doğrulandı (K-040 sınıfı, bkz. K-453 civarı)

# Aynı kuralı ikinci kez yaz
# → 409 {"title":"Rule already exists","detail":"A rule for the same tool, agent, and conditions is already registered."}

# Sayısal olmayan degerle GreaterThan
# → 400 {"title":"Invalid condition value","detail":"Operator 'GreaterThan' expects a number."}

# 11 kosullu kural (limit 10)
# → 400 {"title":"Too many conditions","detail":"A rule can carry at most 10 conditions."}
```

---

## Plandan Sapmalar

1. **🚨 `POST /api/approvals/rules` planın "mevcut uç" iddiasının aksine YOKTU — yeni yazıldı (K-451).**
   Plan kanıt tablosu `GET`/`DELETE`'in var olduğunu doğru ölçmüştü ama `POST`'un
   varlığını ölçmeden varsaydı. `grep -rn "approvals/rules" src/AgentPrism.AspNetCore/`
   yalnız `MapGet` ve `MapDelete` buluyordu. Bu, `faz-uygulama` Adım 1'in tam
   uyardığı sınıftan bir kusurdu ve kod yazmadan önce yakalandı.
2. **`ToolApprovalRuleRequest` gövdesi `argumentsHash` alanı TAŞIMAZ (K-452).**
   Yalnız `toolName`/`agentName`/`argumentConditions`. Açık Soru 1'in "A" önerisi
   (ikisi birlikte olamaz, `400` ile reddedilir) bu yüzden farklı bir biçimde
   gerçekleşti: HTTP sözleşmesi `argumentsHash` alanını hiç sunmuyor, dolayısıyla
   ayrı bir doğrulama koduna gerek kalmadı — iki oluşturma yolu (agent onay akışı
   vs. admin ekranı) HTTP katmanında zaten ayrık.
3. **`ToolApprovalDecision` planlanan adı `ToolApprovalPolicyDecision` oldu (K-453).**
   `AgentPrism.AspNetCore.Contracts.GovernanceContracts.cs` içinde Faz 6'dan beri
   aynı isimde BAŞKA bir public tip vardı (arayüzden gelen onay/red kararı). İsim
   çakışması `CS0436` ile ölçülerek yakalandı, yeniden adlandırıldı.
4. **`ToolApprovalRuleEvaluator`'ın kurucusu `internal` DEĞİL, `public` kaldı; `ToolApprovalPolicyRegistry` de `internal` yerine `public` oldu.**
   İlk denemede ikisi de `internal` yapılmıştı (yalnız DI'dan çözülüyorlar,
   dışarıdan `new` edilmiyorlar diye) — bu, tüm `AgentPrism.AspNetCore.FunctionalTests`
   koşumunu (19/19) `InvalidOperationException: A suitable constructor... could not
   be located` ile kırdı: yerleşik `IServiceProvider`'ın reflection tabanlı
   etkinleştiricisi yalnız PUBLIC kurucuları görür. `public` bir kurucu `internal`
   bir parametre tipi alamaz (CS0051), bu yüzden `ToolApprovalPolicyRegistry` de
   `public` olmak zorunda kaldı. Ders: DI ile çözülen bir tipin kurucusunu
   `internal` yapmadan önce gerçekten koş — derleme hatası vermez, yalnız çalışma
   anında patlar.
5. **`409` çakışma tespiti, depoyu değiştirmeden ÜRETİLEN id ile DÖNEN id
   karşılaştırmasıyla yapıldı (K-454).** `IToolApprovalRuleStore.AddAsync`'in
   var olan idempotent-upsert sözleşmesine dokunulmadı (agent onay akışı buna
   bağımlı) — uç kendi ürettiği id'yi verir, dönen satırın id'si FARKLIYSA
   var olan bir satır döndüğü anlaşılır ve `409` çevrilir.
6. **`conditions_hash` kanonikleştirmesi sayısal normalizasyon yapmaz (K-455, `faz-denetim` 🟢).**
   `100` ile `100.0` teorik olarak farklı hash üretebilir; bugün tek yazma yolu
   olduğu için pratikte risksiz. Aday listesine yazılmadı çünkü ikinci bir yazma
   yolu şu an planlı değil.
7. **`docs-site/concepts/tools.md` güncellenmedi — yalnız `governance.md`
   güncellendi.** Plan ikisini de listeliyordu ama okunduğunda `tools.md`'nin
   onay içeriği zaten yalnız `governance.md`'ye işaret eden bir "Read next"
   satırıydı, kendi derinliği yoktu — ikinci bir kopya içerik üretmek yerine
   tek kaynağı güncellemek tercih edildi.
8. **`samples/AgentPrism.Api` doğrulaması CRUD ile sınırlı kaldı; gerçek bir model
   çağrısıyla "koşul eşleşince otomatik geçer" senaryosu koşulmadı.** Örnek
   uygulamada onay isteyen, `amount` argümanlı hazır bir tool yoktu ve gerçek bir
   sağlayıcı API anahtarı bu oturumda mevcut değildi. Davranışın kendisi
   `ToolApprovalTests` (birim, gerçek `FunctionCallContent` ile) ve
   `GovernanceEndpointTests` (fonksiyonel, gerçek HTTP+DB ile) tarafından
   kanıtlanmış durumda; yalnız uçtan uca gerçek-model adımı eksik kaldı.
   `docs/manuel-test/13-...md` MT-SEC-105 bunu 👤 insan gerekir olarak işaretler.
9. **Denetim sırasında (bağımsız denetçi) bir SQL Server deadlock'u ölçüldü ve
   `MigrationRunner`'a kapsam dışı bırakıldı** — bkz. Bölüm 1 kararı ve
   "Denetim Bulguları".

## Bu Fazda Verilen Kararlar

- **K-451** — `POST /api/approvals/rules` yeni bir uçtur; plan yanlışlıkla "mevcut" sanıyordu
- **K-452** — Kural oluşturma gövdesi `argumentsHash` taşımaz; yalnız koşullar
- **K-453** — `ToolApprovalDecision` adı, mevcut bir tiple çakıştığı için `ToolApprovalPolicyDecision` oldu
- **K-454** — `409` tespiti üretilen/dönen id karşılaştırmasıyla yapılır; depo sözleşmesi değişmedi
- **K-455** — `conditions_hash` sayısal normalizasyon yapmaz (bilinçli, ölçülmüş risk)
- **Bölüm 1** — `MigrationRunner`'a deadlock yeniden denemesi eklenmedi; gerekçe ve yeniden açılma koşulu yazıldı (numarasız, "kanıt olmadan yeniden açılmayacak işler" tablosu)

Tam gerekçeler: `docs/KARARLAR.md`, K-451–K-455 (Bölüm 2) ve deadlock kararı (Bölüm 1).

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi (taze bağlam, `general-purpose` agent) çalıştırıldı.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `MigrationRunner` deadlock (1205) için yeniden deneme yapmıyor; paralel tam-paket koşumunda bir kez gözlendi | 🟡 | **Gerekçelendi** — Bölüm 1'e karar yazıldı (izole koşumda 495/495, kapsam dışı) |
| 2 | `POST /api/approvals/rules` yeni uç + `ToolApprovalDecision`→`ToolApprovalPolicyDecision` ad değişimi henüz not edilmemişti | 🟡 | **Düzeltildi** — bu bölüm ve "Plandan Sapmalar" |
| 3 | `docs-site/` henüz güncellenmemişti | 🟡 | **Düzeltildi** — `concepts/governance.md`, `ui.md`, `index.mdx` |
| 4 | `docs/manuel-test/13-...md` içine case eklenmemişti | 🟡 | **Düzeltildi** — MT-SEC-100…108 |
| 5 | `docs/KARARLAR.md`'ye karar girilmemişti | 🟡 | **Düzeltildi** — K-451…K-455 + Bölüm 1 kararı |
| 1 (🟢) | `conditions_hash` sayısal normalizasyon yapmıyor (`100` vs `100.0`) | 🟢 | **Gerekçelendi, ADAYLAR'a taşınmadı** — K-455, tek yazma yolu var, risk düşük |
| 2 (🟢) | Arayüzde sayısal operatör girişinde satır içi doğrulama yok (`NaN` sunucuya gider, sunucu `400` ile reddeder) | 🟢 | **Devredilmedi, kabul edildi** — sunucu zaten kapalı düşüyor, yalnız UX pürüzü |

**🔴 yok.** Temiz çıkan başlıklar: DoD ihlali, test tiyatrosu, yanlış test seviyesi, imza-gövde kayması, repo kuralları, arayüz payı.

## Sonraki Faza Devir Notu

- **`ToolApprovalRuleEvaluator.IsAutoApprovedAsync` artık İKİ AŞAMALIDIR: önce
  kod politikası (varsa), sonra veri kuralları.** Bu sıralamaya dokunan her
  değişiklik güvenlik sınırını değiştirir — kod hep veriden önce gelmelidir.
- **🚨 DI ile çözülen bir tipin kurucusunu `internal` yapma.** `ActivatorUtilities`
  ve yerleşik `IServiceProvider` yalnız PUBLIC kurucuları görür; bu fazda
  `ToolApprovalRuleEvaluator`/`ToolApprovalPolicyRegistry`'yi `internal` yapmak
  tüm `AspNetCore.FunctionalTests` paketini (19/19) çalışma anında kırdı — derleme
  yeşil kalmıştı. `MEMORY.md`'ye eklenmeye aday bir ders.
- **`ToolApprovalRule.ArgumentsHash` ile `ArgumentConditions` mutually exclusive'dir
  ama bunu zorlayan bir veritabanı kısıtı YOKTUR** — yalnız HTTP sözleşmesi
  (`ToolApprovalRuleRequest`'te `argumentsHash` alanı yok) ve `ToolApprovalResolver`'ın
  kendi disiplini bunu sağlıyor. Depoya doğrudan yazan yeni bir kod yolu eklenirse
  bu varsayımı ihlal edebilir.
- **`conditions_hash` yalnız `SqlToolApprovalRuleStore` içinde, private olarak
  hesaplanır — `ToolApprovalRule` domain modelinde YOKTUR.** Bilerek: yalnız SQL
  benzersizlik anahtarı için var olan bir depolama detayı.
- **Yarım kalan:** `samples/AgentPrism.Api`'de gerçek bir model çağrısıyla
  "koşul eşleşince otomatik geçer" senaryosu koşulmadı (bkz. Plandan Sapmalar 8).
  Bir sonraki oturum bunu tamamlamak isterse `AddToolApprovalPolicy` çağrısı ve
  `amount` argümanlı bir tool örnek uygulamaya eklenmeli.
- **Bir sonraki faz henüz seçilmedi** — `docs/ADAYLAR.md`'den seçim yapılacaksa
  `faz-planlama` skill'i uygulanır. Sıradaki dalga: Faz 64 (Denetim Zinciri ve
  Veri Konusu Hakları).
