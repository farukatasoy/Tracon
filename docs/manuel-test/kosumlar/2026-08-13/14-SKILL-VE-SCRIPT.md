# 14 — Skill ve Script Çalıştırma (`SKILL`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../14-SKILL-VE-SCRIPT.md`](../../14-SKILL-VE-SCRIPT.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-SKILL-001 — `PUT /api/skills/{name}` yeni bir skill oluşturur (`201`)

**Gerçek sonuç**
`HTTP: 201`, `version: 1`, `createdAt == updatedAt` (`2026-08-13T23:04:47.153926+00:00`). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-002 — Aynı skill'i tekrar `PUT` etmek günceller (`200`), `version` artar

**Gerçek sonuç**
`HTTP: 200`, `version: 2`, `createdAt` aynı kaldı, `updatedAt` ilerledi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-003 — `GET /api/skills` kiracının skill listesini döner

**Gerçek sonuç**
Liste `fatura-kontrolu`'nu (version 2, güncel açıklamayla) içeriyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-004 — `DELETE` skill'i ve cascade kaynaklarını siler

**Gerçek sonuç**
Adım 1: `HTTP: 204`. Adım 2: `HTTP: 404`, `title: "Skill bulunamadi"`. Bellek içi kalıcılıkla koşuldu — PostgreSQL doğrulama sorgusu koşulmadı (case'in kendi metni bunu yalnız PostgreSQL izleğinde ölçülebilir bir tamamlayıcı kanıt olarak sunuyor), HTTP davranışı beklenen sonucu zaten sağlıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-005 — Var olmayan skill'i silmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title: "Skill bulunamadi"`, `detail: "'hic-yok' adinda bir skill yok."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-006 — Yoldaki ad ile gövdedeki ad uyuşmazsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Ad uyusmuyor"`, `detail: "Yoldaki ad 'skill-a', govdedeki ad 'skill-b'."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-007 — Büyük harf/alt çizgi içeren ad → `400` (MAF'ın kendi ad kuralı)

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill adi gecersiz"`, `detail: "Skill name must use only lowercase letters, numbers, and hyphens, and must not start or end with a hyphen or contain consecutive hyphens."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-008 — 65 karakterlik ad (64 sınırını aşan) → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill adi gecersiz"`, `detail: "Skill name must be 64 characters or fewer."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-009 — Boş `description` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill aciklamasi gecersiz"`, `detail: "Skill description is required."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-010 — `instructions` 64 KB sınırını aşarsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill talimati cok buyuk"`, `detail: "instructions en fazla 65536 bayt olabilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-011 — 21. kaynak eklenirse (limit 20) → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Cok fazla kaynak"`, `detail: "Bir skill en fazla 20 kaynak tasiyabilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-012 — Aynı skill içinde iki kaynak aynı adı taşırsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Kaynak adi gecersiz"`, `detail: "Her kaynak adi bos olmamali ve skill icinde benzersiz olmalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-013 — Arayüzden skill oluşturma ve düzenleme (Skills ekranı)

**Gerçek sonuç**
Playwright ile koşuldu. Kaydet sonrası `/agentprism/skills` listesine dönüldü, `arayuz-skilli` satırı `0` kaynak ve `Enabled` durumuyla göründü. Edit formuna tekrar girildiğinde talimat metni düz `<textbox>` içinde ham metin olarak duruyor, render edilmiyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-014 — Skill'i arayüzden devre dışı bırakma, checkbox agent düzenleyicisinde kilitlenir

**Gerçek sonuç**
Playwright ile koşuldu. Adım 1: "Enabled" kutucuğu kaldırılıp kaydedilince liste satırı `Disabled` rozetine döndü. Adım 2: `support` agent'ının düzenleyicisinde Skills panelinde `arayuz-skilli` checkbox'ı `[disabled]` durumda (erişilebilirlik ağacında `checkbox "arayuz-skilli Disabled Arayuzden olusturulan test skilli." [disabled]`), `fatura-kontrolu` checkbox'ı ise tıklanabilir kaldı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Skill Kataloğu Çözümleme Kuralları (Faz 10)

Bu bölümün case'leri `AgentSkillCatalog`'un davranışını kanıtlar: kod
kaydının önceliği, bilinmeyen skill, skill sayısı sınırı ve önbellek parmak
izi. 🚨 **Önemli ayrım:** "bilinmeyen skill adı" agent'ı **kaydederken**
(`PUT /api/agents`) yakalanır (`AgentDefinitionValidator.CheckSkillsAsync`,
yalnız VARLIK denetler) ama "skill sayısı sınırı" yalnız agent
**çalıştırılırken** (`POST /api/agents/{name}/run`) yakalanır — validator'ın
gerçek derlemeyi (`CheckStructureAsync`) tetiklediği yol
`AgentSkillCatalog.ResolveAsync`'i hiç çağırmaz; sayı sınırı yalnız
`DefinitionStoreAgentSource.ResolveAsync`'in çalıştırma anında çağırdığı
`AgentDefinitionCompiler.ResolveSkillsAsync` üzerinden devreye girer
(ölçüldü: `AgentDefinitionValidator.cs:317-337` ile
`Catalog/DefinitionStoreAgentSource.cs:80-87` karşılaştırıldı). MT-SKILL-016
bu asimetriyi kanıtlar.

---

## MT-SKILL-020 — Bilinmeyen skill adına işaret eden agent → SAVE zamanında `400`

**Gerçek sonuç**
`HTTP: 201 Created` — agent kaydedildi, hiçbir doğrulama hatası dönmedi. Kök neden: `AgentEndpoints.CreateAgentAsync` (POST /api/agents) yalnız `Validate(request)` (temel şekil denetimi) ve `ValidateCallGraphAsync`'i çağırıyor; `AgentDefinitionValidator.ValidateAsync` (asıl `CheckSkillsAsync`'i, dolayısıyla `unknown_skill` kontrolünü içeren metot) yalnız ayrı `/api/agents/validate` ucundan (`ValidateAgentAsync`, `AgentEndpoints.cs:300-320`) çağrılıyor — CreateAgentAsync/UpdateAgentAsync onu HİÇ çağırmıyor (`AgentEndpoints.cs:247-283` ve `:340-372` okundu, ikisi de aynı desende). Doküman kaydın kendisinin bu denetimi yaptığını varsayıyordu; gerçekte istemci ayrıca `/validate`'i çağırmadıkça bilinmeyen skill adı hiç yakalanmıyor, agent yalnız ÇALIŞTIRILDIĞINDA (derleme anında) patlıyor olabilir — MT-SKILL-021'in "sayı sınırı" asimetrisiyle AYNI sınıf bir varlık-denetimi boşluğu.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-001/K-404 — düzeltildi):** `CreateAgentAsync`/`UpdateAgentAsync` artık `AgentDefinitionValidator.ValidateAsync`'i SAVE zamanında çağırıyor (skill/tool/callable-agent/model — hepsi, yalnız skill değil, K1 kuralı gereği tam düzeltme). Aynı senaryo birebir tekrarlandı: `HTTP: 400`, `"'hayalet-skilli-agent' agent'i 'hic-var-olmayan-skill' skill'ine isaret ediyor ancak skill bulunamadi."` Regresyon kontrolü: skil'siz geçerli bir agent hâlâ `201` ile kaydediliyor; `PUT` (update) yolunda da aynı red doğrulandı. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-021 — 🚨 `MaxSkillsPerAgent` aşımı SAVE'de geçer, yalnız RUN'da `400` verir

**Gerçek sonuç**
Adım 1: `HTTP: 201`. Adım 2: `HTTP: 400`, `title: "Agent derlenemedi"`, `detail: "'iki-skilli-agent' agent'i en fazla 1 skill tasiyabilir."`. Tam beklendiği gibi — koşumun ilk denemesinde eski uygulama süreci `pkill` deseniyle yakalanamadığı için (apphost ikili adı `AgentPrism.Api`, `dotnet ... .dll` değil) yeniden başlama sessizce başarısız oldu ve `MaxSkillsPerAgent` hiç uygulanmadı (adım 2 yanlışlıkla `200` döndü); PID ile `kill -9` edilip doğru ortam değişkenleriyle yeniden başlatıldıktan sonra tekrarlanan koşum yukarıdaki sonucu verdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** `dotnet user-secrets remove "AgentPrism:Skills:MaxSkillsPerAgent"`.

---

## MT-SKILL-022 — Devre dışı skill derlemeye girmez, model `load_skill` içinde hiç görmez

**Gerçek sonuç**
`fatura-kontrolu` devre dışı bırakıldıktan sonra `manuel-skill-test`'e prompt gönderildi (API üzerinden `/api/agents/{name}/run`, akış olayları incelendi): hiçbir `load_skill` fonksiyon çağrısı üretilmedi, model genel bir "hangi bilgileri paylaşmalısın" yanıtı verdi, `FATURA_SKILL_ACTIVE` işaretçisi hiç görünmedi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-023 — Kod tanımlı skill, aynı adlı DB kaydını geçersiz kılar

**Gerçek sonuç**
Playwright ile Playground üzerinden koşuldu. `load_skill` onay kartı `skillName: "fatura-kontrolu"` ile çıktı, onaylandı. `load_skill` sonucu `<description>KOD TANIMLI surum - DB kaydini gecersiz kilar.</description>` içeriyordu (DB'deki "Fatura kontrol kurallarini..." açıklaması DEĞİL) ve talimat metni `KOD_SKILL_ACTIVE` yaz diyordu. Model nihai yanıtı tam olarak `KOD_SKILL_ACTIVE` oldu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** Eklenen `AddSkill(...)` bloğu `Program.cs`'ten kaldırıldı, proje yeniden derlendi (0 uyarı/hata), uygulama temiz haliyle yeniden başlatıldı.

---

## MT-SKILL-024 — Skill düzenlemesi, `CompiledAgentCache` parmak izini değiştirir

**Gerçek sonuç**
Adım 1 (API üzerinden, aynı süreçte MT-SKILL-023'ün temizliği sonrası tekrar kurulan `manuel-skill-test`/`fatura-kontrolu` fixture'ıyla): onaylandıktan sonra model `FATURA_SKILL_ACTIVE` üretti. Adım 2: skill `PUT` ile güncellendi, `version: 2`. Adım 3: YENİ bir Playground sohbetinde (farklı `sessionId`) aynı prompt gönderildi, `load_skill` yeniden onay istedi (yeni sohbet olduğu için beklenen), onaylandı, model tam olarak `FATURA_SKILL_V2` üretti — `FATURA_SKILL_ACTIVE` DEĞİL. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-025 — Sunucu `MaxSkillsPerAgent`'ı değiştirir, arayüz checkbox limiti HABERSİZ kalır

**Gerçek sonuç**
`support` agent düzenleyicisinde `skill-a`/`skill-b`/`skill-c` (üçü de etkin) sırayla seçildi; erişilebilirlik ağacında üçü de `[checked]`, hiçbiri `[disabled]` değildi — arayüz 3. seçimde kilitlenmedi (sunucunun gerçek sınırı `2` olmasına rağmen). Kod-kökenli `support`'un düzenleme formu `name`/`model` alanlarını önceden doldurmadığı için (`Save` bu yüzden devre dışı kaldı — ayrı, ilgisiz bir form-doldurma davranışı) kaydetme adımı API eşdeğeriyle tamamlandı: `POST /api/agents` (`uc-skilli-agent`, 3 skill) `HTTP: 201`, ardından `POST .../run` `HTTP: 400`, `title: "Agent derlenemedi"`, `detail: "'uc-skilli-agent' agent'i en fazla 2 skill tasiyabilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** `dotnet user-secrets remove "AgentPrism:Skills:MaxSkillsPerAgent"` uygulandı.

---

# 3 — Gerçek Model ile Onaylı Skill Yükleme (Faz 10 kanıtı)

Bu bölüm Faz 10'un kendi "gerçek model ile doğrulama zorunludur" kuralını
tekrarlar. `10-ARAYUZ-AGENT-PLAYGROUND.md`'nin `MT-UIAG-028/029/030` case'leri
`cancel_order` için AYNI onay mekanizmasını (`ToolApprovalRuleEvaluator`)
zaten kanıtladı; burada yalnız `load_skill`'e özgü fark test edilir:
onaylanan şey bir **sipariş eylemi değil, agent'ın talimatının çalışma anında
değişmesidir** (Faz 10'un 1 numaralı kararı).

**Ön koşul (bölümün tamamı)**
- `FIX-SKILL-FATURA` ve `FIX-AGENT-SKILL` oluşturulmuş, ikisi de etkin.
- `playground/manuel-skill-test` açık, yeni sohbet.

---

## MT-SKILL-030 — `FIX-SKILL-PROMPT` → `load_skill` onay kartı üretir

**Gerçek sonuç**
Playwright ile Playground'da koşuldu. `load_skill` onay kartı `arguments: {"skillName":"fatura-kontrolu"}` ile göründü, hiçbir final metin yoktu (yalnız onay kartı). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-031 — Onayla → skill talimatı bağlama girer, model işaretçiyi birebir üretir

**Gerçek sonuç**
"Hatırla" işaretlenmeden "Onayla"ya tıklandı. Yeni tur `load_skill done` kartı ve `KOD TANIMLI...` DEĞİL, DB'deki gerçek talimatı taşıyan `load_skill` sonucunu içerdi; model nihai yanıtı tam olarak `FATURA_SKILL_ACTIVE` oldu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-032 — Reddet → skill hiç yüklenmez, model onsuz devam eder

**Gerçek sonuç**
"Reddet"e tıklandı: kart `rejected` rozetine döndü, `load_skill` sonucu `Tool call invocation rejected.` oldu. Belgelenmeyen bir nüans: model, reddedilen çağrıyı bir kez daha denedi ve İKİNCİ bir `load_skill` onay kartı üretti (gpt-5.4-mini'nin retry davranışı — kod tarafında bir tekrar mekanizması değil, modelin kendi kararı); bu da reddedildi, ardından tur tamamlandı. Nihai yanıt genel bir "hangi fatura bilgilerini paylaşmalısın" metniydi, `FATURA_SKILL_ACTIVE` dizgisini İÇERMİYORDU. Asıl iddia (skill talimatı hiçbir zaman bağlama girmedi) doğrulandı; ekstra onay turu kusur değil, gerçek model davranışı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-033 — "Hatırla" ile onay → sonraki çağrıda `load_skill` kartı hiç çıkmaz

**Gerçek sonuç**
Adım 1: prompt gönderildi, onay kartında "Hatırla" işaretlenip "Onayla"ya tıklandı, model `FATURA_SKILL_ACTIVE` üretti. Adım 2/3: "Yeni Sohbet" ile farklı bir `sessionId`'de aynı prompt tekrar gönderildi — bu sefer HİÇBİR onay kartı çıkmadı, `load_skill done` doğrudan göründü, model yine `FATURA_SKILL_ACTIVE` üretti. Tam beklendiği gibi — kalıcı kural (`tool_approval_rules`) çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Skill Script Kaydı ve Doğrulama (Faz 11)

Script KAYDETMEK ile script ÇALIŞTIRMAK ayrı yetkilerdir. Bu bölümdeki
case'ler yalnız KAYIT/doğrulama katmanını sınar — hiçbiri `UseSkillScripts()`
gerektirmez, örnek uygulama hiç değiştirilmeden koşulur.

---

## MT-SKILL-040 — Varsayılan durumda (Interpreters boş) HERHANGİ bir script uzantısı reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title: "Script uzantisi izinli degil"`, `detail: "'py' uzantisi icin kayitli bir yorumlayici yok."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-041 — `Interpreters`'a `sh` eklenince AYNI kayıt başarılı olur (yalnız config, kod değişikliği YOK)

**Gerçek sonuç**
`dotnet user-secrets set "AgentPrism:Skills:Scripts:Interpreters:sh" "/bin/bash"` ile yeniden başlatıldıktan sonra `HTTP: 201`, script kaydı `content: "echo merhaba-agentprism"` ile döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-042 — 11. script eklenirse (limit 10) → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Cok fazla script"`, `detail: "Bir skill en fazla 10 script tasiyabilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-043 — Aynı skill içinde iki script aynı adı taşırsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Script adi gecersiz"`, `detail: "Her script adi bos olmamali ve skill icinde benzersiz olmalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-044 — Geçersiz JSON `parametersSchema` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Parametre semasi gecersiz"`, `detail: "parametersSchema gecerli bir JSON nesnesi olmalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-045 — Script içeriği 64 KB sınırını aşarsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Script cok buyuk"`, `detail: "Her script en fazla 65536 bayt olabilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-046 — `scripts.Enabled = false` iken bile bir script KAYDEDİLİR ve GERİ OKUNUR

**Gerçek sonuç**
`GET /api/skills/scriptli-skill` gövdesi `scripts: [{"name":"merhaba",...,"content":"echo merhaba-agentprism",...}]` döndü — tam içerikle, gizlenmeden. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Script Çalıştırma İzinleri (Grant, Faz 11)

Grant uçları da `UseSkillScripts()` GEREKTİRMEZ — yalnız `scripts.Enabled`
bayrağını okurlar (config-only). Gerçek çalıştırma §6'nın konusudur.

---

## MT-SKILL-050 — Script çalıştırma KAPALIYKEN izin vermeye çalışmak → `409`

**Gerçek sonuç**
`HTTP: 409`, `title: "Script calistirma kapali"`, `detail: "Izin vermeden once UseSkillScripts(...) ile script calistirmayi acin."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-051 — `scripts.Enabled = true` yapılınca (yalnız config) AYNI istek `201` döner

**Gerçek sonuç**
`dotnet user-secrets set "AgentPrism:Skills:Scripts:Enabled" "true"` + `PlatformIsolationAcknowledged` `"true"` ile yeniden başlatıldıktan sonra `HTTP: 201`, `grantedBy: null`, `expiresAt: null`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-052 — Geçmiş bir `expiresAt` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Bitis zamani gecmiste"`, `detail: "expiresAt gelecekte bir an olmalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-053 — Boş `skillName` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill adi gerekli"`, `detail: "skillName bos olamaz."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-054 — `GET /api/skill-script-grants` listesi, arayüzde kırmızı uyarıyla görünür

**Gerçek sonuç**
Adım 1: liste `scriptli-skill`/`merhaba` çiftini içerdi. Adım 2: `/agentprism/skills` sayfasında `border-red-500` CSS sınıflı bir uyarı kutusu doğrulandı (`document.querySelector('[class*="border-red"]')` ile), grant tablosunda aynı satır (`scriptli-skill` / `merhaba` / `Granted by: unknown`) göründü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-055 — İzni arayüzden iptal et (`Revoke`) → liste hemen güncellenir

**Gerçek sonuç**
"Revoke" düğmesine tıklandı, satır listeden kayboldu, panel "No active grant." metnine döndü. Bellek içi kalıcılıkla koşuldu; PostgreSQL doğrulama sorgusu koşulmadı (satırın silinmediği/`revoked_at` dolduğu iddiası `InMemorySkillScriptGrantStore`'un aynı `active` filtre desenini kullandığı varsayımına dayanır, ayrıca doğrulanmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-056 — Var olmayan bir izni iptal etmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title: "Izin bulunamadi"`, `detail: "'hic-yok-skill' icin gecerli bir calistirma izni yok."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Sandbox Kapıları ve Gerçek Çalıştırma Kanıtı (Faz 11)

Bu bölüm, K2 kuralının ikinci istisnasını **gerçekten çalışır hâlde** kanıtlar.
`UseSkillScripts()` `Program.cs`'te hiç çağrılmadığı için ("Koşmadan önce" §4)
`SkillScriptSupport`/`SandboxedSkillScriptRunner` DI'da kayıtlı DEĞİLDİR — bu
bölümün TAMAMI için **tek, geçici bir kod değişikliği** gerekir.

**Bölümün ortak ön koşulu**

1. `samples/AgentPrism.Api/Program.cs`'te, `var agentPrism = builder.AddAgentPrism()...` bloğunun BİTİMİNDEN (`;`'den) hemen sonra GEÇİCİ olarak ekleyin:
   ```csharp
   agentPrism.UseSkillScripts(o => o.PlatformIsolationAcknowledged = true);
   ```
   Geri kalan tüm ayarlar (`AllowStoredScripts`, `Interpreters`, `Timeout`,
   `MaxOutputBytes`, ...) `dotnet user-secrets` ile verilir — `Bind()`
   `UseSkillScripts`'in lambda'sından ÖNCE kayıtlıdır ama `Enabled`/
   `PlatformIsolationAcknowledged` dışındaki alanlara lambda hiç dokunmadığı
   için config değerleri KORUNUR (ölçüldü:
   `AgentPrismServiceCollectionExtensions.cs:44-60` ile
   `AgentPrismSkillScriptBuilderExtensions.cs:54-58` karşılaştırıldı).
2. ```bash
   dotnet user-secrets set "AgentPrism:Skills:Scripts:Interpreters:sh" "/bin/bash"
   dotnet user-secrets set "AgentPrism:Skills:Scripts:AllowStoredScripts" "true"
   ```
3. `scriptli-skill`/`merhaba` (MT-SKILL-041) kayıtlı ve `scriptli-skill`/`merhaba`
   için geçerli bir izin var (MT-SKILL-051).
4. Uygulamayı yeniden başlat.
5. `FIX-AGENT-SKILL` (`manuel-skill-test`) yerine YENİ bir agent kullanın —
   `skillNames: ["scriptli-skill"]` taşıyan `manuel-script-test`:
   ```bash
   curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
     "name": "manuel-script-test",
     "model": { "provider": "openai", "model": "gpt-5.4-mini" },
     "skillNames": ["scriptli-skill"]
   }'
   ```

**Bölüm sonu temizliği:** `agentPrism.UseSkillScripts(...)` satırını
`Program.cs`'ten kaldırın; `dotnet user-secrets remove` ile
`Interpreters:sh`, `AllowStoredScripts`, `Enabled`,
`PlatformIsolationAcknowledged` anahtarlarını temizleyin.

---

## MT-SKILL-057 — 🚨 Config-only kurulum (kod değişikliği OLMADAN) script'i modele HİÇ sunmaz

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-058 — Gerçek çalıştırma kanıtı: `echo` script'i onaydan geçer ve modele sonucu döner

**Gerçek sonuç**
🚨 **KRİTİK KUSUR.** §6'nın ortak ön koşulu (geçici `agentPrism.UseSkillScripts(o => o.PlatformIsolationAcknowledged = true);` kod satırı + `AllowStoredScripts=true`) uygulanıp yeniden derlendikten/başlatıldıktan sonra, `scriptli-skill`'e bağlı (script içeren, gerçekten etkin) HERHANGİ bir agent'a gönderilen HER istek `run` başlarken şu hatayla çöküyor: `InvalidOperationException: JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.` `load_skill` onay kartı hiç çıkmıyor, model hiç çağrılmıyor — hata skill'in MAF'a sunulacağı derleme anında oluşuyor. Hem Playground'dan (`manuel-script-test`, "Fatura kontrol..." promptu) hem doğrudan `POST /api/agents/manuel-script-test/run` ile ("merhaba" gövdesi) doğrulandı, ikisi de aynı hatayı üretti — tam belirlenimli (deterministik), model içeriğinden bağımsız.

**Kök neden (kod okunarak doğrulandı):** `src/AgentPrism.Core/Skills/AgentPrismSkillsSource.cs:10` — `private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);` — resolver'ı hiç ayarlanmamış, salt reflection tabanlı bir `JsonSerializerOptions` örneği. Bu örnek `_scripts is { StoredScriptsEnabled: true }` iken (`AgentPrismSkillsSource.cs:71-81`) MAF'ın `skill.AddScript(script.Name, delegate, description, SerializerOptions)` çağrısına aynen geçiriliyor; MAF bu seçenekler nesnesini içeride `MakeReadOnly()` ile donduruyor (resolver popüle edilmeden), bu da .NET'in "TypeInfoResolver olmadan salt-okunur işaretlenemez" korumasını tetikliyor. `StoredScriptsEnabled: false` iken (§4/§5, MT-SKILL-057) bu kod yolu (`skill.AddScript`) hiç çağrılmadığı için sorun gizli kalıyor — bu yüzden script KAYDI/İZNİ katmanındaki 14 case (§4+§5) sorunsuz geçti ama gerçek ÇALIŞTIRMA katmanının TAMAMI (§6) bu satırda çöküyor.

**Kapsam:** Bu, script çalıştırma özelliğinin (Faz 11) yayınlanan hâlde TAMAMEN işlevsiz olduğu anlamına gelir — `UseSkillScripts()` çağıran ve saklı script'i olan HER tüketici aynı çökmeyi yaşar. MT-SKILL-059..063 ve 070 AYNI kök nedenle bloklanıyor (script gerçekten çalıştırılmadan hiçbiri gözlemlenemez); kullanıcı kararıyla bu case'ler tek tek tekrar denenmeden "aynı kök nedenle Kaldı" olarak işaretlendi, ayrıntı için bu case'e bakınız.

**🔧 Kapanış güncellemesi (2026-08-14):** Kök neden HATA-K-002 olarak kodlandı ve düzeltildi (K-400, `SONUCLAR-K-2026-08-13.md`) — bu case'in kendisi yeniden koşulup uçtan uca doğrulandı (yukarıdaki Durum satırına bakınız). MT-SKILL-059..063 ve 070 henüz TEK TEK yeniden koşulmadı (bu düzeltme oturumunun kapsamı "çöküşü gider + doğrula", "her bloklu case'i tekrar koş" değildi) — engel artık kalkmış durumda, bu case'ler gelecek bir koşumda normal şekilde tekrar denenebilir.

**🚨 Kapanış güncellemesi (2026-08-15, KAPANIS-PLANI §9/§6 Aile W) — MT-SKILL-059..070'in gerçek yeniden koşumu İKİ YENİ ve BAĞIMSIZ kritik kusur buldu.** K-400 kök nedeni gerçekten kapalı (bu case'in kendi "geçti" iddiası yukarıdaki 2026-08-14 notuyla tutarlı — `load_skill` onayı sorunsuz geçiyor), ama script'in GERÇEKTEN çalıştırılması hâlâ iki farklı yerde çöküyordu; bu case'in 2026-08-14 koşumu bunları YAKALAMADI çünkü `echo merhaba-agentprism` gibi stdin okumayan bir script'in zamanlamasına şans eseri denk gelmedi (ırk koşulu — deterministik değil, aşağıya bakınız). 2026-08-15'te tam ortam kurulup (§6 ortak ön koşulu) MT-SKILL-059..070'in HEPSİ canlı OpenAI çağrısıyla gerçekten koşulunca:

1. **`SkillScriptProcessRunner.WriteArgumentsAsync`'in `finally` bloğu** (`process.StandardInput.Close()`) `try/catch`'in DIŞINDAydı; stdin'i hiç okumadan çıkan (`echo` gibi) bir script'te `Close()`'un kendi iç flush'ı `IOException: Pipe is broken` fırlatıyor ve bu YAKALANMADAN dışarı sızıyordu — MAF'ın `run_skill_script` çağrısı `"Error: Function failed."` ile başarısız oluyordu. **Script çalıştırma özelliğinin TAMAMI (K-400 kapandıktan SONRA bile) fiilen işlevsizdi.**
2. **`SandboxedSkillScriptRunner.DenyAsync`** red nedenini (düz metin) `jsonb` sütununa JSON'a çevirmeden yazıyordu; her `script.denied` denetim izi `22P02 invalid input syntax for type json` ile sessizce kayboluyordu (Faz 9'un "gözlemlenebilirlik hatası çalıştırmayı bozmaz" bilinçli istisnası devreye giriyordu — kayıt kaybolsa da red işliyordu, ama denetim izi HİÇ oluşmuyordu).

İkisi de bu koşumda düzeltildi (bkz. `docs/hafiza/cekirdek-calistirma.md`, HATA-K-skill-pipe / HATA-K-skill-audit-json), regresyon testleriyle kilitlendi
(`SkillScriptProcessRunnerTests.Stdin_okumadan_cikan_script_boru_kirik_istisnasi_firlatmaz`,
`SandboxedSkillScriptRunnerTests.Izin_yokken_script_reddedilir_ve_denetim_izine_yazilir`'e
JSON geçerlilik denetimi eklendi), ardından MT-SKILL-058..070'in TAMAMI canlı
OpenAI ile yeniden koşulup doğrulandı — ayrıntı §6 Aile W.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — 2026-08-15'te üçüncü kez uçtan uca koşuldu (iki yeni kusur düzeltildikten sonra): `load_skill` onayı → `run_skill_script(scriptName=merhaba)` onayı → gerçek çalıştırma → `exit_code: 0\nstdout:\nmerhaba-agentprism\n\n`, `tool_invocations.error IS NULL`, `audit_log`'da `script.run`/`scriptli-skill/merhaba`. Tool adı beklentisi yukarıda koda göre düzeltildi.

---

## MT-SKILL-059 — İzin iptal edildikten sonra AYNI script reddedilir, `script.denied` yazılır

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9, kod düzeltmeleri sonrası) —
Geçti.** `scriptli-skill/merhaba` izni `DELETE
/api/skill-script-grants/scriptli-skill?scriptName=merhaba` ile iptal
edildi (`204`). Yeni bir oturumda `load_skill` onaylandı, ardından
`run_skill_script(scriptName=merhaba)` onaylandı; sonuç
`"Error: Function failed."` — düzeltilmiş beklentiyle tam örtüşüyor. Canlı
Postgres'te doğrulandı: `tool_invocations.error` = `'scriptli-skill/merhaba'
script'i calistirilmadi: Bu script icin gecerli bir calistirma izni yok.`
(tam metin korunmuş); `audit_log` sorgusu `action='script.denied',
entity='scriptli-skill/merhaba', after='"Bu script icin gecerli bir
calistirma izni yok."'` döndürdü.

🚨 **Bu case ampirik olarak İKİ yeni kusur ortaya çıkardı (bu koşumda
düzeltildi, ayrıntı `docs/hafiza/cekirdek-calistirma.md` ve §6 Aile W):**
1. İlk deneme, `SandboxedSkillScriptRunner.DenyAsync`'in `after` alanını
   (düz metin) `jsonb` sütununa JSON'a çevirmeden yazdığını gösterdi —
   Postgres `INSERT`'i `22P02 invalid input syntax for type json` ile
   reddediyor, `script.denied` denetim izi HİÇ oluşmuyordu (kayıt
   başarısızlığı Faz 9'un bilinçli istisnasınca yutuluyor, red işlemeye
   devam ediyordu — ama iz kayboluyordu). Düzeltme:
   `JsonSerializer.Serialize(reason, AgentPrismCoreJsonContext.Default.String)`.
2. Aynı koşumda, İZİNLİ script'lerin (merhaba/uyuyan/vb.) gerçek
   çalıştırılması da AYRI bir kusurla (`SkillScriptProcessRunner`'ın
   `Process.StandardInput.Close()`'u) çöküyordu — bkz. `MT-SKILL-058`'in
   2026-08-15 notu ve §6 Aile W.

Re-grant sonrası izin yeniden verildi ve doğrulandı (aşağıdaki Temizlik
adımı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** İzni yeniden ver (`POST /api/skill-script-grants`,
`InMemorySkillScriptGrantStore.GrantAsync`'in upsert semantiği
`revokedAt`'i sıfırlar, `InMemorySkillScriptGrantStore.cs:60-75`).

---

## MT-SKILL-060 — Zaman aşımı: uzun süren script öldürülür, "zaman aşımına uğradı" metni döner

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9, pipe-kapama düzeltmesinden
sonra) — Geçti.** `load_skill` → `run_skill_script(scriptName=uyuyan)`
onaylandı; sonuç birebir beklenen: `"Script zaman asimina ugradi ve surec
agaci sonlandirildi.\n"`, `stdout`/`stderr` yok (`bitti` hiç yazılmadı).
Üç isteğin (ilk mesaj + iki onay) toplam süresi **3 saniye** — 10 saniyeyi
aşmadı, süreç gerçekten öldürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** `dotnet user-secrets remove "AgentPrism:Skills:Scripts:Timeout"` — uygulandı.

---

## MT-SKILL-061 — Çıktı sınırı: büyük çıktı kırpılır, kırpma mesajı eklenir

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9) — Geçti.** `load_skill` →
`run_skill_script(scriptName=buyuk-cikti)` onaylandı; sonuç birebir
beklenen: `exit_code: 0`, `stdout` tam 100 `x` karakterine kırpıldı, ardından
`\n[AgentPrism: cikti 100 bayt sinirinda kirpildi.]` metni. Zaman aşımına
UĞRAMADI. (İlk denemede script içeriği bu koşumun kendi kayıt scriptindeki
bir tırnak-kaçışı hatasıyla `print(x * 5000)` olarak kaydedilmişti — Python
`NameError` üretti, ama kırpma mesajı yine de doğru tetiklendi; içerik
düzeltilip temiz bir `stdout` ile tekrarlandı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** `dotnet user-secrets remove "AgentPrism:Skills:Scripts:MaxOutputBytes"` — uygulandı.

---

## MT-SKILL-062 — Ortam değişkenleri sızmaz: script yalnız `PATH`/`HOME` (+2 enjekte edilen) görür

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu,
iddia yalnız kod okumasıyla makul görülmüştü, gerçek çalıştırma kanıtı
yoktu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9, canlı OpenAI ile, `MaxOutputBytes`
sınırı OLMADAN tam çıktı) — Geçti, güvenlik iddiası TAM doğrulandı.**
`load_skill` → `run_skill_script(scriptName=ortam-dokumu)` onaylandı.
Gözlenen tam `stdout` (7 satır, alfabetik sıralı):
```
AGENTPRISM_SKILL_NAME=scriptli-skill
AGENTPRISM_SKILL_TEMP=/var/folders/.../agentprism-skill-X0z3EK
HOME=/Users/farukatasoy
PATH=/Users/farukatasoy/...(sistem PATH'i)
PWD=/private/var/folders/.../agentprism-script-GWwSjW
SHLVL=1
_=/usr/bin/env
```
`grep -i "secret\|ApiKey\|ConnectionString\|sk-\|sk_"` çıktıda **0 eşleşme**
— kritik güvenlik iddiası (hiçbir AgentPrism `secret`'ı script sürecine
sızmaz) canlı bir çalıştırmayla tam olarak kanıtlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-063 — Kiracı başına eşzamanlılık sınırı: 3. eşzamanlı çağrı ilk ikisi bitene kadar BEKLER

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9) — Geçti.** Playground yerine
üç bağımsız oturum kimliğiyle (aynı kiracı `default`) üç `manuel-script-test`
çalıştırması eşzamanlı (Python `threading`) başlatıldı — her biri kendi
`load_skill`/`run_skill_script(scriptName=bekleyen)` onay zincirini
yürüttü. Gözlenen toplam süreler (mesaj + iki onay dahil, LLM gecikmesi
dahil):
```
[1] sure=6.8sn
[2] sure=6.6sn
[3] sure=10.9sn
```
Üçüncü çağrı diğer ikisinden **~4,2 saniye** daha geç bitti — tam olarak
`bekleyen` script'inin (`sleep 4`) süresi kadar bir gecikme, ilk ikisinden
biri semaforu bırakana kadar üçüncünün beklediğini doğruluyor. Üçünün de
sonucu `exit_code: 0` — hiçbiri hata almadı, yalnız üçüncüsü geç tamamlandı.
Mutlak süreler dokümanın `~4sn`/`~8sn` tahminini aşıyor (gerçek OpenAI
round-trip gecikmesi dahil olduğu için) ama İLİŞKİSEL fark (üçüncü ↔
ilk ikisi) beklenen davranışla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Gözlemlenebilirlik (Faz 11)

---

## MT-SKILL-070 — `execute_skill_script` span'i doğru öznitelikleri taşır

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9) — Geçti.** `merhaba` script'i
başarıyla çalıştırıldıktan sonra o çağrının `runId`'siyle
`GET /api/runs/{id}/trace` sorgulandı. Bulunan span, beklenenle **birebir**
örtüşüyor:
```
execute_skill_script {
  'agentprism.skill.name': 'scriptli-skill',
  'agentprism.script.name': 'merhaba',
  'agentprism.script.exit_code': '0',
  'agentprism.script.duration_ms': '66.4933'
}
```
Aynı iz, model tarafına sunulan tool listesini de doğruladı —
`gen_ai.tool.definitions` yalnız `load_skill`/`read_skill_resource`/
`run_skill_script` içeriyor (`merhaba` diye ayrı bir tool adı YOK) — bu,
`MT-SKILL-058`'in tool-adı düzeltmesinin bağımsız bir doğrulamasıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
